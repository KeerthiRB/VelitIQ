using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Models;

namespace VelitIQ.Core.Interfaces;

public sealed record ParsedSectionChunk(
    string SourceType,
    string SectionPath,
    string PageOrSheetName,
    string Content,
    Dictionary<string, object?>? Metadata = null);

public sealed record ParsedSheetData(
    string SheetName,
    int SheetIndex,
    List<TabularColumnSchema> Columns,
    List<SpreadsheetFormulaInfo> Formulas,
    List<Dictionary<string, object?>> Rows,
    string SummaryMarkdown);

public sealed record DocumentParseResult(
    int PageOrSheetCount,
    int FormulaCount,
    int DiagramOrImageCount,
    List<ParsedSectionChunk> Chunks,
    List<ParsedSheetData> Sheets,
    List<byte[]>? ExtractedPageOrDiagramImages = null);

public interface IDocumentFormatParser
{
    bool CanParse(ArtifactFormat format, string extension);
    Task<DocumentParseResult> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default);
}

public interface IFileStorageService
{
    string ProviderName { get; }
    Task<string> SaveAsync(Stream content, string relativeKey, string contentType, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}

public sealed record GeneratedHandoverInsights(
    string DocumentSummary,
    string[] SystemsDetected,
    string[] RecurringSchedulesDetected,
    string[] KeyContactsDetected,
    List<GeneratedGapItem> GapQuestions);

public sealed record GeneratedGapItem(
    string Category,
    string Priority,
    string Question,
    string ContextSnippet);

public sealed record AiToolDefinition(
    string Name,
    string Description,
    string JsonSchema);

public sealed record AiToolCall(
    string CallId,
    string ToolName,
    string ArgumentsJson);

public sealed record AiAssistantTurn(
    string? TextContent,
    IReadOnlyList<AiToolCall> ToolCalls);

public sealed record AiToolExecutionResult(
    string CallId,
    string ToolName,
    string OutputJson);

public interface IAiService
{
    string ActiveModeName { get; }
    bool IsCloudLive { get; }
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    Task<GeneratedHandoverInsights> AnalyzeDocumentForHandoverGapsAsync(
        KnowledgeDocument document,
        DocumentParseResult parseResult,
        DepartingUserProfile departingUser,
        CancellationToken ct = default);
    Task<string> SynthesizeUserDossierAsync(
        DepartingUserProfile profile,
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<HandoverGapQuestion> gaps,
        CancellationToken ct = default);
    Task<AiAssistantTurn> RunAgentTurnAsync(
        string systemPrompt,
        IReadOnlyList<AgentChatMessageDto> history,
        string userQuestion,
        IReadOnlyList<AiToolDefinition> availableTools,
        IReadOnlyList<(AiAssistantTurn AssistantTurn, IReadOnlyList<AiToolExecutionResult> Results)> previousToolSteps,
        CancellationToken ct = default);
}

public interface IAuthService
{
    Task<AuthTokenResponse?> LoginWithCredentialsAsync(LoginRequest request, string ipAddress, CancellationToken ct = default);
    Task<AuthTokenResponse?> LoginDemoPersonaAsync(string email, string ipAddress, CancellationToken ct = default);
    Task<AuthTokenResponse?> LoginWindowsIdentityAsync(string windowsAccountName, IEnumerable<string> groupSidsOrNames, string ipAddress, CancellationToken ct = default);
    Task<IReadOnlyList<DemoPersonaDto>> GetDemoPersonasAsync(CancellationToken ct = default);
    Task<SecurityContext> BuildSecurityContextAsync(Guid userId, CancellationToken ct = default);
}

public interface IAgenticRagOrchestrator
{
    Task<AgentChatResponse> AskAsync(
        AgentChatRequest request,
        SecurityContext caller,
        string bearerToken,
        CancellationToken ct = default);
}
