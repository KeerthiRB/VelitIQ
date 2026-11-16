using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Ai;

/// <summary>
/// Zero-external-key local semantic & agentic engine used automatically during local development/testing
/// when Azure AI Foundry credentials are not present on the developer laptop.
/// Exercises the full PostgreSQL hybrid vector/FTS index and real Mcp.Client.Net <-> McpHttpServer tool calls.
/// </summary>
public sealed class LocalDeterministicAiService : IAiService
{
    private readonly int _dimensions;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "are", "was", "were", "have", "has",
        "had", "not", "but", "what", "when", "where", "who", "which", "how", "can", "will", "should",
        "into", "about", "all", "any", "their", "there", "been", "being", "than", "then", "also"
    };

    public string ActiveModeName => "Local Deterministic Engine (Ready for Azure AI Foundry Cutover)";
    public bool IsCloudLive => false;

    public LocalDeterministicAiService(IOptions<AzureFoundryOptions> options)
    {
        _dimensions = Math.Max(128, options.Value.EmbeddingDimensions);
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(ComputeHashedSemanticVector(text, _dimensions));

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var list = texts.Select(t => ComputeHashedSemanticVector(t, _dimensions)).ToList();
        return Task.FromResult<IReadOnlyList<float[]>>(list);
    }

    private static float[] ComputeHashedSemanticVector(string text, int dims)
    {
        var vec = new float[dims];
        if (string.IsNullOrWhiteSpace(text))
        {
            vec[0] = 1f;
            return vec;
        }

        var tokens = Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9_]{2,}")
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .ToList();

        foreach (var token in tokens)
        {
            var stemmed = StemLight(token);
            AccumulateFeature(vec, "w:" + stemmed, 2.0f);

            // Character 4-grams capture banking identifiers, partial codes (MT103, NOSTRO, GL_4010), and morphological variants
            if (stemmed.Length >= 4)
            {
                for (int i = 0; i <= stemmed.Length - 4; i++)
                {
                    AccumulateFeature(vec, "g:" + stemmed.Substring(i, 4), 0.65f);
                }
            }
        }

        // Bigrams capture phrases like "cut off", "month end", "nostro break", "var limit"
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            AccumulateFeature(vec, $"b:{StemLight(tokens[i])}_{StemLight(tokens[i + 1])}", 1.35f);
        }

        double normSq = 0;
        for (int i = 0; i < dims; i++) normSq += vec[i] * vec[i];
        if (normSq > 1e-9)
        {
            float invNorm = (float)(1.0 / Math.Sqrt(normSq));
            for (int i = 0; i < dims; i++) vec[i] *= invNorm;
        }
        else
        {
            vec[0] = 1f;
        }

        return vec;
    }

    private static string StemLight(string word)
    {
        if (word.Length > 5 && word.EndsWith("ing")) return word[..^3];
        if (word.Length > 4 && word.EndsWith("ed")) return word[..^2];
        if (word.Length > 4 && word.EndsWith("es")) return word[..^2];
        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss")) return word[..^1];
        return word;
    }

    private static void AccumulateFeature(float[] vec, string feature, float weight)
    {
        Span<byte> bytes = stackalloc byte[32];
        var utf8 = Encoding.UTF8.GetBytes(feature);
        SHA256.HashData(utf8, bytes);

        uint h1 = BitConverter.ToUInt32(bytes[..4]);
        uint h2 = BitConverter.ToUInt32(bytes[4..8]);
        int idx = (int)(h1 % (uint)vec.Length);
        float sign = (h2 & 1) == 0 ? 1f : -1f;
        vec[idx] += sign * weight;
    }

    public Task<GeneratedHandoverInsights> AnalyzeDocumentForHandoverGapsAsync(
        KnowledgeDocument document,
        DocumentParseResult parseResult,
        DepartingUserProfile departingUser,
        CancellationToken ct = default)
    {
        var fullText = string.Join("\n", parseResult.Chunks.Take(12).Select(c => c.Content));

        var systems = Regex.Matches(fullText, @"\b(?:PRD-[A-Z0-9\-]+|UAT-[A-Z0-9\-]+|SRV-[A-Z0-9\-]+|SWIFT|MT\d{3}|FEDWIRE|NOSTRO|GL_[A-Z0-9_]+|KAFKA_[A-Z0-9_]+|MUREX|CALYPSO|BLOOMBERG|SNOWFLAKE|POSTGRES|ORACLE_[A-Z0-9_]+|CTRL-M|[A-Z]{2,5}_FEED)\b")
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        var schedules = Regex.Matches(fullText, @"\b(?:EOD|SOD|Month-End|Quarter-End|Daily\s+at\s+\d{1,2}:\d{2}(?:\s*[A-Z]{2,4})?|\d{1,2}:\d{2}\s*(?:EST|CST|UTC|ET)|T\+\d)\b", RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        var contacts = Regex.Matches(fullText, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .Take(6)
            .ToArray();

        var summary = BuildStructuralSummary(document, parseResult, systems, schedules);
        var gaps = new List<GeneratedGapItem>();

        if (parseResult.FormulaCount > 0)
        {
            var sampleFormula = parseResult.Sheets.SelectMany(s => s.Formulas).FirstOrDefault();
            gaps.Add(new GeneratedGapItem(
                "FormulaLogic",
                "High",
                $"Workbook `{document.OriginalFileName}` contains {parseResult.FormulaCount} formula cell(s) (e.g., `{sampleFormula?.CellAddress}: {sampleFormula?.Formula}`). Are there manual adjustment overrides or tolerance thresholds during month-end sign-off that a successor must verify?",
                sampleFormula is not null ? $"Sheet formula at {sampleFormula.CellAddress}: {sampleFormula.Formula}" : "Spreadsheet formula audit"));
        }

        if (parseResult.DiagramOrImageCount > 0)
        {
            gaps.Add(new GeneratedGapItem(
                "UndocumentedDependency",
                "High",
                $"`{document.OriginalFileName}` includes {parseResult.DiagramOrImageCount} embedded architecture/OneNote screenshot artifact(s). Where is the editable source diagram stored, and are any firewall ports or service accounts required when failing over?",
                $"Detected {parseResult.DiagramOrImageCount} embedded visual diagram(s) across {parseResult.PageOrSheetCount} page(s)."));
        }

        if (systems.Length > 0)
        {
            gaps.Add(new GeneratedGapItem(
                "ExceptionHandling",
                "High",
                $"This artifact references critical banking components ({string.Join(", ", systems.Take(4))}). If an upstream batch or settlement feed stalls past cut-off, what is the exact manual recovery sequence and L3 escalation path?",
                $"Referenced systems: {string.Join(", ", systems)}"));
        }

        if (contacts.Length == 0)
        {
            gaps.Add(new GeneratedGapItem(
                "AccessAndCredentials",
                "Medium",
                $"No explicit vendor or internal distribution-list email contacts were detected in `{document.OriginalFileName}`. Which Active Directory group grants operational access, and who is the secondary SME backup after {departingUser.ExitDate:MMM dd}?",
                "Missing explicit SME / DL email contact metadata in uploaded artifact."));
        }

        return Task.FromResult(new GeneratedHandoverInsights(
            summary,
            systems,
            schedules,
            contacts,
            gaps.Take(3).ToList()));
    }

    private static string BuildStructuralSummary(
        KnowledgeDocument doc,
        DocumentParseResult parse,
        string[] systems,
        string[] schedules)
    {
        var sysPart = systems.Length > 0 ? $" Covers systems/identifiers: {string.Join(", ", systems.Take(5))}." : string.Empty;
        var schedPart = schedules.Length > 0 ? $" Key operating windows: {string.Join(", ", schedules.Take(4))}." : string.Empty;

        return doc.Format switch
        {
            ArtifactFormat.Excel => $"Structured Excel workbook (`{doc.OriginalFileName}`) spanning {parse.PageOrSheetCount} worksheet(s) and {parse.FormulaCount} formula(s), indexed for both semantic search and exact tabular aggregation.{sysPart}{schedPart}",
            ArtifactFormat.Csv => $"Tabular CSV dataset (`{doc.OriginalFileName}`) with {parse.Sheets.Sum(s => s.Rows.Count)} structured record(s) and {parse.Sheets.FirstOrDefault()?.Columns.Count ?? 0} schema column(s).{sysPart}",
            ArtifactFormat.Word => $"Structured Word runbook/specification (`{doc.OriginalFileName}`) organized across {parse.PageOrSheetCount} section(s) and {parse.Chunks.Count} contextual chunk(s).{sysPart}{schedPart}",
            _ => $"PDF handover document (`{doc.OriginalFileName}`, {parse.PageOrSheetCount} page(s), {parse.DiagramOrImageCount} embedded visual/OneNote diagram(s)) indexed across {parse.Chunks.Count} layout-preserving section(s).{sysPart}{schedPart}"
        };
    }

    public Task<string> SynthesizeUserDossierAsync(
        DepartingUserProfile profile,
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<HandoverGapQuestion> gaps,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Executive Handover Dossier — {profile.FullName}");
        sb.AppendLine($"- **Role & Team:** {profile.JobTitle} (`{profile.DepartmentName}` / `{profile.TeamName}`)");
        sb.AppendLine($"- **Scheduled Bank Exit:** `{profile.ExitDate:yyyy-MM-dd}` | **Designated Successor:** {profile.SuccessorName} (`{profile.SuccessorEmail}`)");
        sb.AppendLine($"- **Handover Artifacts Captured:** {documents.Count} document(s) across {string.Join(", ", documents.Select(d => d.Format.ToString()).Distinct())}");

        if (profile.SystemsOwned.Length > 0)
            sb.AppendLine($"- **Systems & Feeds Owned:** {string.Join(", ", profile.SystemsOwned.Select(s => $"`{s}`"))}");
        if (profile.RecurringSchedules.Length > 0)
            sb.AppendLine($"- **Critical Operational Cadences:** {string.Join(", ", profile.RecurringSchedules)}");
        if (profile.KeyContacts.Length > 0)
            sb.AppendLine($"- **Escalation Contacts:** {string.Join(", ", profile.KeyContacts)}");

        if (documents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Indexed Knowledge Inventory");
            foreach (var d in documents)
            {
                sb.AppendLine($"- **`{d.Title}`** (`{d.OriginalFileName}`, *{d.Visibility}*): {d.Summary}");
            }
        }

        var answered = gaps.Where(g => g.Status == GapQuestionStatus.Answered).ToList();
        if (answered.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Captured Tribal Knowledge (Author Gap Clarifications)");
            foreach (var g in answered)
            {
                sb.AppendLine($"- **Q:** *{g.Question}*");
                sb.AppendLine($"  - **Verified Answer:** {g.Answer}");
            }
        }

        return Task.FromResult(sb.ToString().Trim());
    }

    public Task<AiAssistantTurn> RunAgentTurnAsync(
        string systemPrompt,
        IReadOnlyList<AgentChatMessageDto> history,
        string userQuestion,
        IReadOnlyList<AiToolDefinition> availableTools,
        IReadOnlyList<(AiAssistantTurn AssistantTurn, IReadOnlyList<AiToolExecutionResult> Results)> previousToolSteps,
        CancellationToken ct = default)
    {
        var toolNames = availableTools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Step 0: Plan & invoke real MCP tools discovered from McpHttpServer via Mcp.Client.Net!
        if (previousToolSteps.Count == 0)
        {
            var calls = new List<AiToolCall>();

            // Extract scope filters embedded in system prompt by orchestrator if present
            var deptId = ExtractPromptTag(systemPrompt, "SCOPE_DEPT_ID");
            var teamId = ExtractPromptTag(systemPrompt, "SCOPE_TEAM_ID");
            var userId = ExtractPromptTag(systemPrompt, "SCOPE_USER_ID");

            if (toolNames.Contains("search_hybrid_knowledge"))
            {
                var argsObj = new JsonObject
                {
                    ["query"] = userQuestion,
                    ["topK"] = 8
                };
                if (!string.IsNullOrWhiteSpace(deptId)) argsObj["departmentId"] = deptId;
                if (!string.IsNullOrWhiteSpace(teamId)) argsObj["teamId"] = teamId;
                if (!string.IsNullOrWhiteSpace(userId)) argsObj["departingUserId"] = userId;

                calls.Add(new AiToolCall("call_hybrid_1", "search_hybrid_knowledge", argsObj.ToJsonString()));
            }

            bool asksTabular = Regex.IsMatch(
                userQuestion,
                @"\b(?:sum|total|average|avg|count|excel|csv|sheet|spreadsheet|formula|formulas|nostro|recon|reconciliation|variance|exposure|limit|amount|balance|table|rows)\b",
                RegexOptions.IgnoreCase);

            if (asksTabular && toolNames.Contains("list_accessible_spreadsheets"))
            {
                var sheetArgs = new JsonObject();
                if (!string.IsNullOrWhiteSpace(teamId)) sheetArgs["teamId"] = teamId;
                if (!string.IsNullOrWhiteSpace(userId)) sheetArgs["departingUserId"] = userId;
                calls.Add(new AiToolCall("call_sheets_1", "list_accessible_spreadsheets", sheetArgs.ToJsonString()));
            }

            bool asksDossierOrExit = Regex.IsMatch(
                userQuestion,
                @"\b(?:dossier|handover|leaving|departing|exit|readiness|successor|who\s+owns|onboarding| december)\b",
                RegexOptions.IgnoreCase);

            if (asksDossierOrExit && toolNames.Contains("get_departing_user_dossier"))
            {
                var dossierArgs = new JsonObject();
                if (!string.IsNullOrWhiteSpace(teamId)) dossierArgs["teamId"] = teamId;
                if (!string.IsNullOrWhiteSpace(userId)) dossierArgs["departingUserId"] = userId;
                calls.Add(new AiToolCall("call_dossier_1", "get_departing_user_dossier", dossierArgs.ToJsonString()));
            }

            if (calls.Count > 0)
            {
                return Task.FromResult(new AiAssistantTurn(null, calls));
            }
        }

        // Step 1: If spreadsheets were listed in Step 0, automatically run structured query_tabular_sheet on the best-matching sheet!
        if (previousToolSteps.Count == 1 && toolNames.Contains("query_tabular_sheet"))
        {
            var sheetsResult = previousToolSteps[0].Results.FirstOrDefault(r => r.ToolName == "list_accessible_spreadsheets");
            if (sheetsResult is not null)
            {
                var matchedSheet = PickBestMatchingSheet(sheetsResult.OutputJson, userQuestion);
                if (matchedSheet is not null)
                {
                    var queryArgs = new JsonObject
                    {
                        ["sheetId"] = matchedSheet.Value.SheetId,
                        ["limit"] = 25
                    };
                    if (!string.IsNullOrWhiteSpace(matchedSheet.Value.NumericColumn))
                    {
                        queryArgs["aggregateColumn"] = matchedSheet.Value.NumericColumn;
                        queryArgs["aggregateFunction"] = userQuestion.Contains("average", StringComparison.OrdinalIgnoreCase) ? "avg" : "sum";
                    }

                    return Task.FromResult(new AiAssistantTurn(
                        null,
                        [new AiToolCall("call_tab_query_2", "query_tabular_sheet", queryArgs.ToJsonString())]));
                }
            }
        }

        // Final Step: Synthesize grounded, cited Markdown answer from all MCP tool outputs!
        var synthesized = SynthesizeGroundedResponse(userQuestion, previousToolSteps);
        return Task.FromResult(new AiAssistantTurn(synthesized, []));
    }

    private static string? ExtractPromptTag(string prompt, string tag)
    {
        var m = Regex.Match(prompt, $@"{tag}=([0-9a-fA-F\-]{{36}})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static (string SheetId, string? NumericColumn)? PickBestMatchingSheet(string sheetsJson, string question)
    {
        try
        {
            var arr = JsonNode.Parse(sheetsJson)?.AsArray();
            if (arr is null || arr.Count == 0) return null;

            var qStems = Regex.Matches(question.ToLowerInvariant(), @"[a-z0-9]{3,}")
                .Select(m => m.Value)
                .Where(w => !StopWords.Contains(w))
                .Select(StemLight)
                .ToHashSet();

            bool wantsMathOrFormulas = Regex.IsMatch(question, @"\b(?:sum|total|average|avg|formula|formulas|amount|balance|break|var|limit)\b", RegexOptions.IgnoreCase);

            JsonObject? best = null;
            int bestScore = -1;

            foreach (var node in arr)
            {
                if (node is not JsonObject obj) continue;
                var titleTokens = Regex.Matches($"{obj["sheetName"]} {obj["documentTitle"]}".ToLowerInvariant(), @"[a-z0-9]{3,}")
                    .Select(m => StemLight(m.Value))
                    .ToHashSet();
                var colTokens = Regex.Matches($"{obj["columnsSummary"]}".ToLowerInvariant(), @"[a-z0-9]{3,}")
                    .Select(m => StemLight(m.Value))
                    .ToHashSet();

                int numColCount = obj["numericColumns"]?.AsArray()?.Count ?? 0;
                int formulaCount = obj["formulaCount"]?.GetValue<int>() ?? 0;

                int score = (qStems.Count(titleTokens.Contains) * 2) +
                            (qStems.Count(colTokens.Contains) * 3) +
                            (wantsMathOrFormulas && numColCount > 0 ? 3 : 0) +
                            (wantsMathOrFormulas && formulaCount > 0 ? 2 : 0);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = obj;
                }
            }

            best ??= arr[0]?.AsObject();
            var id = best?["sheetId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) return null;

            var numCols = best?["numericColumns"]?.AsArray()
                ?.Select(n => n?.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList() ?? [];

            var bestNumCol = numCols
                .OrderByDescending(col =>
                {
                    var cStems = Regex.Matches(col.ToLowerInvariant(), @"[a-z0-9]{3,}").Select(m => StemLight(m.Value)).ToHashSet();
                    return qStems.Count(cStems.Contains);
                })
                .FirstOrDefault();

            return (id, bestNumCol);
        }
        catch
        {
            return null;
        }
    }

    private static string SynthesizeGroundedResponse(
        string question,
        IReadOnlyList<(AiAssistantTurn AssistantTurn, IReadOnlyList<AiToolExecutionResult> Results)> steps)
    {
        var allResults = steps.SelectMany(s => s.Results).ToList();
        var sb = new StringBuilder();

        var searchOut = allResults.FirstOrDefault(r => r.ToolName == "search_hybrid_knowledge")?.OutputJson;
        var tabOut = allResults.FirstOrDefault(r => r.ToolName == "query_tabular_sheet")?.OutputJson;
        var dossierOut = allResults.FirstOrDefault(r => r.ToolName == "get_departing_user_dossier")?.OutputJson;

        var hits = new List<HybridSearchHitDto>();
        if (!string.IsNullOrWhiteSpace(searchOut))
        {
            try { hits = JsonSerializer.Deserialize<List<HybridSearchHitDto>>(searchOut, JsonOpts) ?? []; } catch { /* ignore */ }
        }

        TabularQueryResultDto? tabResult = null;
        if (!string.IsNullOrWhiteSpace(tabOut))
        {
            try { tabResult = JsonSerializer.Deserialize<TabularQueryResultDto>(tabOut, JsonOpts); } catch { /* ignore */ }
        }

        if (hits.Count == 0 && tabResult is null && string.IsNullOrWhiteSpace(dossierOut))
        {
            return """
                ### No Authorized Matching Artifacts Found

                No documents matching your query were found within your current **Department / Team RBAC scope** (or cross-team shared artifacts).
                - Verify whether the target artifact is marked **`TeamOnly`** under another banking team.
                - Try broadening the Team/Departing User filter or contact a **Global Admin / Team Lead** if cross-team access is required.
                """;
        }

        sb.AppendLine("### Grounded Knowledge Synthesis");
        sb.AppendLine();

        if (hits.Count > 0)
        {
            sb.AppendLine($"Based on **{hits.Count} retrieved knowledge section(s)** across your authorized scope:");
            sb.AppendLine();

            var displayedHits = hits.Take(5)
                .Union(hits.Where(h => h.SourceType == "AuthorGapAnswer"))
                .ToList();

            foreach (var hit in displayedHits)
            {
                var badge = hit.SourceType == "AuthorGapAnswer" ? " *(Verified Author Gap Clarification)*" : "";
                var snippet = hit.Content.Length > 1000 ? hit.Content[..1000].Trim() + "..." : hit.Content.Trim();
                sb.AppendLine($"#### `{hit.DocumentTitle}` — {hit.PageOrSheetName}{badge}");
                sb.AppendLine($"*Team: **{hit.TeamName}** | Author: **{hit.DepartingUserName}** | Scope: `{hit.Visibility}`*");
                sb.AppendLine();
                sb.AppendLine(snippet);
                sb.AppendLine();
            }
        }

        if (tabResult is not null)
        {
            sb.AppendLine($"### Structured Spreadsheet Analysis: `{tabResult.DocumentTitle}` (`{tabResult.SheetName}`)");
            sb.AppendLine($"- **Matched Rows:** {tabResult.MatchedRowCount} of {tabResult.TotalSheetRows}");

            if (tabResult.AggregationSummary is not null &&
                tabResult.AggregationSummary.TryGetValue("function", out var fn) &&
                tabResult.AggregationSummary.TryGetValue("column", out var col) &&
                tabResult.AggregationSummary.TryGetValue("value", out var val))
            {
                sb.AppendLine($"- **Exact Computed {fn} (`{col}`):** **`{val}`**");
            }

            if (tabResult.Formulas.Count > 0)
            {
                sb.AppendLine("- **Underlying Workbook Formulas:**");
                foreach (var f in tabResult.Formulas.Take(5))
                {
                    sb.AppendLine($"  - `{f.CellAddress}`: `{f.Formula}` (Cached: `{f.CachedValue}`)");
                }
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(dossierOut) && dossierOut.Length > 10 && !dossierOut.TrimStart().StartsWith("[]"))
        {
            try
            {
                var profiles = JsonSerializer.Deserialize<List<DepartingUserProfile>>(dossierOut, JsonOpts);
                if (profiles is { Count: > 0 })
                {
                    sb.AppendLine("### December Offboarding Readiness Overview");
                    foreach (var p in profiles.Take(4))
                    {
                        sb.AppendLine($"- **{p.FullName}** (*{p.JobTitle}*, `{p.TeamName}`) — Exit: **`{p.ExitDate:yyyy-MM-dd}`**, Readiness: **`{p.ReadinessScore}%`**, Successor: **{p.SuccessorName}**");
                    }
                }
            }
            catch
            {
                // Ignore non-array dossier summary
            }
        }

        return sb.ToString().Trim();
    }
}
