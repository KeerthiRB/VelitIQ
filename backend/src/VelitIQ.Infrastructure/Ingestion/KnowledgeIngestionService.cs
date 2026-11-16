using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Data;
using VelitIQ.Infrastructure.Security;

namespace VelitIQ.Infrastructure.Ingestion;

public sealed class KnowledgeIngestionService
{
    private readonly PostgresKnowledgeStore _store;
    private readonly IFileStorageService _storage;
    private readonly IEnumerable<IDocumentFormatParser> _parsers;
    private readonly SecretGuardrailScanner _secretScanner;
    private readonly IAiService _ai;
    private readonly ILogger<KnowledgeIngestionService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public KnowledgeIngestionService(
        PostgresKnowledgeStore store,
        IFileStorageService storage,
        IEnumerable<IDocumentFormatParser> parsers,
        SecretGuardrailScanner secretScanner,
        IAiService ai,
        ILogger<KnowledgeIngestionService> logger)
    {
        _store = store;
        _storage = storage;
        _parsers = parsers;
        _secretScanner = secretScanner;
        _ai = ai;
        _logger = logger;
    }

    public static ArtifactFormat DetectFormat(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ArtifactFormat.Pdf,
            ".docx" or ".doc" => ArtifactFormat.Word,
            ".xlsx" or ".xlsm" or ".xls" => ArtifactFormat.Excel,
            ".csv" => ArtifactFormat.Csv,
            _ => throw new ArgumentException($"Unsupported file format '{ext}'. Supported formats: CSV, Excel (.xlsx/.xls), Word (.docx), and PDF (.pdf).")
        };
    }

    public async Task<KnowledgeDocument> IngestDocumentAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        Guid departingUserId,
        string? customTitle,
        DocumentCategory category,
        VisibilityScope visibility,
        string[]? tags,
        SecurityContext uploader,
        string ipAddress,
        CancellationToken ct = default)
    {
        var departingUser = await _store.GetDepartingUserByIdAsync(departingUserId, ct)
            ?? throw new InvalidOperationException($"Departing user '{departingUserId}' not found.");

        if (!uploader.CanWriteToTeam(departingUser.TeamId))
            throw new UnauthorizedAccessException("You do not have write permission for this team's handover workspace.");

        var team = await _store.GetTeamByIdAsync(departingUser.TeamId, ct)
            ?? throw new InvalidOperationException("Team not found.");

        var format = DetectFormat(originalFileName);
        using var mem = new MemoryStream();
        await fileStream.CopyToAsync(mem, ct);
        mem.Position = 0;

        var hashBytes = SHA256.HashData(mem.ToArray());
        var sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var docId = Guid.NewGuid();
        var safeFile = Regex.Replace(originalFileName, @"[^A-Za-z0-9._\-]", "_");
        var storageKey = $"{team.Slug}/{departingUser.Id}/{docId}_{safeFile}";

        mem.Position = 0;
        var savedPath = await _storage.SaveAsync(mem, storageKey, contentType, ct);

        var doc = new KnowledgeDocument
        {
            Id = docId,
            DepartmentId = departingUser.DepartmentId,
            DepartmentName = departingUser.DepartmentName,
            TeamId = departingUser.TeamId,
            TeamName = departingUser.TeamName,
            DepartingUserId = departingUser.Id,
            DepartingUserName = departingUser.FullName,
            UploadedByUserId = uploader.UserId == Guid.Empty ? null : uploader.UserId,
            Title = string.IsNullOrWhiteSpace(customTitle) ? Path.GetFileNameWithoutExtension(originalFileName) : customTitle.Trim(),
            OriginalFileName = originalFileName,
            StoragePath = savedPath,
            ContentType = contentType,
            FileSizeBytes = mem.Length,
            Sha256Hash = sha256,
            Format = format,
            Category = category,
            Visibility = visibility,
            Tags = tags ?? [],
            Status = IngestionStatus.Processing,
            StatusMessage = "Extracting structure, formulas, and contextual embeddings..."
        };

        await _store.UpsertDocumentAsync(doc, ct);

        try
        {
            var ext = Path.GetExtension(originalFileName);
            var parser = _parsers.FirstOrDefault(p => p.CanParse(format, ext))
                ?? throw new InvalidOperationException($"No parser registered for {format} ({ext}).");

            mem.Position = 0;
            var parseResult = await parser.ParseAsync(mem, originalFileName, ct);

            // 1. Secret / Credential Guardrail Scan
            var secretFindings = _secretScanner.ScanParsedDocument(doc.Id, parseResult);
            if (secretFindings.Count > 0)
            {
                await _store.SaveSecretFindingsAsync(secretFindings, ct);
            }

            // 2. Build Contextual Retrieval Chunks & Embeddings
            var contextualInputs = parseResult.Chunks.Select(c =>
                $"[Department: {departingUser.DepartmentName} | Team: {departingUser.TeamName} | Author: {departingUser.FullName} ({departingUser.JobTitle}) | Document: {doc.Title} ({doc.OriginalFileName}) | Section: {c.SectionPath} | Location: {c.PageOrSheetName}]\n{c.Content}")
                .ToList();

            var embeddings = await _ai.GenerateEmbeddingsBatchAsync(contextualInputs, ct);

            var chunkEntities = new List<DocumentChunk>(parseResult.Chunks.Count);
            for (int i = 0; i < parseResult.Chunks.Count; i++)
            {
                var pc = parseResult.Chunks[i];
                chunkEntities.Add(new DocumentChunk
                {
                    DocumentId = doc.Id,
                    DepartmentId = doc.DepartmentId,
                    TeamId = doc.TeamId,
                    DepartingUserId = doc.DepartingUserId,
                    Visibility = doc.Visibility,
                    ChunkIndex = i,
                    SourceType = pc.SourceType,
                    SectionPath = pc.SectionPath,
                    PageOrSheetName = pc.PageOrSheetName,
                    Content = pc.Content,
                    ContextualContent = contextualInputs[i],
                    Embedding = i < embeddings.Count ? embeddings[i] : [],
                    TokenEstimate = Math.Max(1, pc.Content.Length / 4),
                    MetadataJson = pc.Metadata is not null ? JsonSerializer.Serialize(pc.Metadata, JsonOpts) : "{}"
                });
            }

            await _store.SaveChunksAsync(chunkEntities, ct);

            // 3. Persist Structured Spreadsheet Sheets & Rows for Exact SQL / Aggregation Queries
            foreach (var sheet in parseResult.Sheets)
            {
                var sheetEntity = new TabularSheet
                {
                    DocumentId = doc.Id,
                    DocumentTitle = doc.Title,
                    DepartmentId = doc.DepartmentId,
                    TeamId = doc.TeamId,
                    DepartingUserId = doc.DepartingUserId,
                    Visibility = doc.Visibility,
                    SheetName = sheet.SheetName,
                    SheetIndex = sheet.SheetIndex,
                    RowCount = sheet.Rows.Count,
                    ColumnCount = sheet.Columns.Count,
                    ColumnsJson = JsonSerializer.Serialize(sheet.Columns, JsonOpts),
                    FormulasJson = JsonSerializer.Serialize(sheet.Formulas, JsonOpts),
                    SummaryMarkdown = sheet.SummaryMarkdown
                };

                var rowEntities = sheet.Rows.Select((r, idx) => new TabularRow
                {
                    SheetId = sheetEntity.Id,
                    DocumentId = doc.Id,
                    RowIndex = idx + 1,
                    RowDataJson = JsonSerializer.Serialize(r, JsonOpts),
                    SearchText = string.Join(" ", r.Values.Where(v => v is not null).Select(v => v!.ToString()))
                }).ToList();

                await _store.SaveTabularSheetAndRowsAsync(sheetEntity, rowEntities, ct);
            }

            // 4. Active Handover Gap Elicitation + Dossier Update
            var insights = await _ai.AnalyzeDocumentForHandoverGapsAsync(doc, parseResult, departingUser, ct);

            doc.Summary = insights.DocumentSummary;
            doc.PageOrSheetCount = parseResult.PageOrSheetCount;
            doc.ChunkCount = chunkEntities.Count;
            doc.FormulaCount = parseResult.FormulaCount;
            doc.DiagramOrImageCount = parseResult.DiagramOrImageCount;
            doc.SecretWarningCount = secretFindings.Count;
            doc.Status = IngestionStatus.Completed;
            doc.StatusMessage = $"Indexed {chunkEntities.Count} contextual chunks" +
                                (parseResult.Sheets.Count > 0 ? $" + {parseResult.Sheets.Count} queryable table sheet(s)" : "") +
                                (secretFindings.Count > 0 ? $" ({secretFindings.Count} guardrail warning(s))" : "");
            doc.ProcessedAt = DateTimeOffset.UtcNow;

            await _store.UpsertDocumentAsync(doc, ct);

            var gapEntities = insights.GapQuestions.Select(g => new HandoverGapQuestion
            {
                DepartingUserId = departingUser.Id,
                TeamId = departingUser.TeamId,
                DocumentId = doc.Id,
                Category = g.Category,
                Priority = g.Priority,
                Question = g.Question,
                ContextSnippet = g.ContextSnippet,
                Status = GapQuestionStatus.Open
            }).ToList();

            await _store.SaveGapQuestionsAsync(gapEntities, ct);

            // Merge detected systems, schedules & contacts into DepartingUserProfile and refresh Dossier + Readiness Score
            departingUser.SystemsOwned = departingUser.SystemsOwned.Union(insights.SystemsDetected, StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
            departingUser.RecurringSchedules = departingUser.RecurringSchedules.Union(insights.RecurringSchedulesDetected, StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
            departingUser.KeyContacts = departingUser.KeyContacts.Union(insights.KeyContactsDetected, StringComparer.OrdinalIgnoreCase).Take(12).ToArray();

            var adminCtx = new SecurityContext { UserId = uploader.UserId, Role = UserRole.Admin };
            var allUserDocs = await _store.GetAccessibleDocumentsAsync(adminCtx, departingUserId: departingUser.Id, ct: ct);
            var allUserGaps = await _store.GetGapQuestionsAsync(adminCtx, departingUserId: departingUser.Id, ct: ct);
            departingUser.ExecutiveSummary = await _ai.SynthesizeUserDossierAsync(departingUser, allUserDocs, allUserGaps, ct);

            await _store.UpsertDepartingUserAsync(departingUser, ct);
            await _store.RecalculateUserReadinessScoreAsync(departingUser.Id, ct);

            await _store.AppendAuditLogAsync(new AuditLog
            {
                ActorUserId = uploader.UserId == Guid.Empty ? null : uploader.UserId,
                ActorEmail = uploader.Email,
                ActorRole = uploader.Role.ToString(),
                Action = "DOC_INGEST_COMPLETED",
                ResourceType = "KnowledgeDocument",
                ResourceId = doc.Id.ToString(),
                TeamId = doc.TeamId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    doc.OriginalFileName,
                    Format = doc.Format.ToString(),
                    Visibility = doc.Visibility.ToString(),
                    doc.ChunkCount,
                    doc.FormulaCount,
                    doc.SecretWarningCount,
                    GeneratedGapQuestions = gapEntities.Count
                }, JsonOpts),
                IpAddress = ipAddress
            }, ct);

            return (await _store.GetDocumentByIdAsync(doc.Id, ct)) ?? doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document ingestion failed for {FileName}", originalFileName);
            doc.Status = IngestionStatus.Failed;
            doc.StatusMessage = $"Ingestion error: {ex.Message}";
            await _store.UpsertDocumentAsync(doc, ct);
            throw;
        }
    }

    public async Task<HandoverGapQuestion> AnswerGapQuestionAndIndexAsync(
        Guid gapQuestionId,
        string answerText,
        SecurityContext actor,
        string ipAddress,
        CancellationToken ct = default)
    {
        var gap = await _store.GetGapQuestionByIdAsync(gapQuestionId, ct)
            ?? throw new InvalidOperationException("Handover gap question not found.");

        if (!actor.CanWriteToTeam(gap.TeamId))
            throw new UnauthorizedAccessException("You do not have write permission to answer handover questions for this team.");

        var departingUser = await _store.GetDepartingUserByIdAsync(gap.DepartingUserId, ct)
            ?? throw new InvalidOperationException("Departing user not found.");

        gap.Answer = answerText.Trim();
        gap.AnsweredByName = string.IsNullOrWhiteSpace(actor.DisplayName) ? departingUser.FullName : actor.DisplayName;
        gap.AnsweredAt = DateTimeOffset.UtcNow;
        gap.Status = GapQuestionStatus.Answered;

        // Index the author's clarification directly into Hybrid RAG so future successor queries retrieve this tribal knowledge!
        Guid? targetDocId = gap.DocumentId;
        VisibilityScope visibility = VisibilityScope.TeamOnly;

        if (targetDocId.HasValue)
        {
            var linkedDoc = await _store.GetDocumentByIdAsync(targetDocId.Value, ct);
            if (linkedDoc is not null) visibility = linkedDoc.Visibility;
        }
        else
        {
            var adminCtx = new SecurityContext { UserId = actor.UserId, Role = UserRole.Admin };
            var firstDoc = (await _store.GetAccessibleDocumentsAsync(adminCtx, departingUserId: departingUser.Id, ct: ct)).FirstOrDefault();
            if (firstDoc is not null)
            {
                targetDocId = firstDoc.Id;
                visibility = firstDoc.Visibility;
            }
        }

        if (targetDocId.HasValue)
        {
            var qaMarkdown = $"""
                ### Verified Author Handover Clarification ({departingUser.FullName} — {departingUser.TeamName})
                - **Handover Gap Question:** {gap.Question}
                - **Author Verified Answer ({gap.AnsweredByName}):** {gap.Answer}
                """;

            var contextual = $"[Department: {departingUser.DepartmentName} | Team: {departingUser.TeamName} | Author Clarification: {departingUser.FullName} | Category: {gap.Category}]\n{qaMarkdown}";
            var emb = await _ai.GenerateEmbeddingAsync(contextual, ct);
            var chunkId = gap.IndexedChunkId ?? Guid.NewGuid();

            var chunk = new DocumentChunk
            {
                Id = chunkId,
                DocumentId = targetDocId.Value,
                DepartmentId = departingUser.DepartmentId,
                TeamId = departingUser.TeamId,
                DepartingUserId = departingUser.Id,
                Visibility = visibility,
                ChunkIndex = 9000 + Math.Abs(gap.Id.GetHashCode() % 900),
                SourceType = "AuthorGapAnswer",
                SectionPath = $"Author Handover Clarifications > {gap.Category}",
                PageOrSheetName = "Handover Gap Q&A",
                Content = qaMarkdown,
                ContextualContent = contextual,
                Embedding = emb,
                TokenEstimate = Math.Max(1, qaMarkdown.Length / 4)
            };

            await _store.SaveChunksAsync([chunk], ct);
            gap.IndexedChunkId = chunkId;
        }

        await _store.SaveGapQuestionsAsync([gap], ct);

        // Refresh dossier & readiness score
        var sysCtx = new SecurityContext { UserId = actor.UserId, Role = UserRole.Admin };
        var allDocs = await _store.GetAccessibleDocumentsAsync(sysCtx, departingUserId: departingUser.Id, ct: ct);
        var allGaps = await _store.GetGapQuestionsAsync(sysCtx, departingUserId: departingUser.Id, ct: ct);
        departingUser.ExecutiveSummary = await _ai.SynthesizeUserDossierAsync(departingUser, allDocs, allGaps, ct);
        await _store.UpsertDepartingUserAsync(departingUser, ct);
        await _store.RecalculateUserReadinessScoreAsync(departingUser.Id, ct);

        await _store.AppendAuditLogAsync(new AuditLog
        {
            ActorUserId = actor.UserId,
            ActorEmail = actor.Email,
            ActorRole = actor.Role.ToString(),
            Action = "HANDOVER_GAP_ANSWERED",
            ResourceType = "HandoverGapQuestion",
            ResourceId = gap.Id.ToString(),
            TeamId = gap.TeamId,
            DetailsJson = JsonSerializer.Serialize(new { gap.Category, gap.DepartingUserId }, JsonOpts),
            IpAddress = ipAddress
        }, ct);

        return (await _store.GetGapQuestionByIdAsync(gap.Id, ct)) ?? gap;
    }
}
