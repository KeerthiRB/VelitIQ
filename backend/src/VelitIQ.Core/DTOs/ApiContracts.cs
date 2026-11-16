using VelitIQ.Core.Models;

namespace VelitIQ.Core.DTOs;

public sealed record LoginRequest(string UsernameOrEmail, string Password, string? Domain = null);

public sealed record DemoLoginRequest(string Email);

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserProfileDto User);

public sealed record UserProfileDto(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string JobTitle,
    string AuthSource,
    UserRole Role,
    Guid? PrimaryTeamId,
    string? PrimaryTeamName,
    Guid? PrimaryDepartmentId,
    string? PrimaryDepartmentName,
    Guid[] AccessibleTeamIds);

public sealed record DemoPersonaDto(
    string Email,
    string DisplayName,
    string JobTitle,
    UserRole Role,
    string TeamName,
    string DepartmentName,
    string ScenarioHighlight);

public sealed record DashboardMetricsDto(
    int TotalDepartments,
    int TotalTeams,
    int TotalDepartingUsers,
    int DepartingInDecemberCount,
    double AverageReadinessScore,
    int TotalDocuments,
    int TotalIndexedChunks,
    int TotalTabularSheets,
    int OpenGapQuestionsCount,
    int AnsweredGapQuestionsCount,
    int UnacknowledgedSecretsCount,
    string AiProviderMode,
    bool IsPgVectorEnabled,
    IReadOnlyList<FormatCountDto> FormatBreakdown,
    IReadOnlyList<TeamReadinessSummaryDto> TeamReadiness,
    IReadOnlyList<DepartingUserProfile> UpcomingExits);

public sealed record FormatCountDto(string Format, int Count);

public sealed record TeamReadinessSummaryDto(
    Guid TeamId,
    string TeamName,
    string DepartmentName,
    int DepartingUsersCount,
    int DocumentsCount,
    int OpenGapsCount,
    double AverageReadinessScore);

public sealed record CreateDepartmentRequest(string Name, string Code, string Description);

public sealed record CreateTeamRequest(
    Guid DepartmentId,
    string Name,
    string Description,
    string AdGroupName,
    string EntraGroupId,
    string ManagerEmail);

public sealed record CreateDepartingUserRequest(
    Guid TeamId,
    string FullName,
    string Email,
    string EmployeeId,
    string JobTitle,
    DateOnly ExitDate,
    string SuccessorName,
    string SuccessorEmail,
    string[]? SystemsOwned = null,
    string[]? RecurringSchedules = null,
    string[]? KeyContacts = null);

public sealed record AnswerGapQuestionRequest(string Answer);

public sealed record HybridSearchRequest(
    string Query,
    Guid? DepartmentId = null,
    Guid? TeamId = null,
    Guid? DepartingUserId = null,
    ArtifactFormat? Format = null,
    int TopK = 8);

public sealed record HybridSearchHitDto(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string OriginalFileName,
    ArtifactFormat Format,
    VisibilityScope Visibility,
    Guid TeamId,
    string TeamName,
    Guid DepartingUserId,
    string DepartingUserName,
    string SourceType,
    string SectionPath,
    string PageOrSheetName,
    string Content,
    double VectorSimilarity,
    double LexicalScore,
    double FusedScore);

public sealed record TabularQueryRequest(
    Guid SheetId,
    string? FilterColumn = null,
    string? FilterOperator = null, // eq, contains, gt, gte, lt, lte
    string? FilterValue = null,
    string? AggregateColumn = null,
    string? AggregateFunction = null, // sum, avg, min, max, count
    string? GroupByColumn = null,
    int Limit = 50);

public sealed record TabularQueryResultDto(
    Guid SheetId,
    Guid DocumentId,
    string DocumentTitle,
    string SheetName,
    int TotalSheetRows,
    int MatchedRowCount,
    IReadOnlyList<TabularColumnSchema> Columns,
    IReadOnlyList<SpreadsheetFormulaInfo> Formulas,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    Dictionary<string, object?>? AggregationSummary);

public sealed record AgentChatMessageDto(string Role, string Content);

public sealed record AgentChatRequest(
    string Question,
    Guid? DepartmentId = null,
    Guid? TeamId = null,
    Guid? DepartingUserId = null,
    string? PromptTemplateName = null,
    IReadOnlyList<AgentChatMessageDto>? History = null);

public sealed record McpToolExecutionTraceDto(
    string ToolName,
    string ArgumentsSummary,
    string ResultSummary,
    long DurationMs);

public sealed record SourceCitationDto(
    Guid DocumentId,
    string DocumentTitle,
    string FileName,
    string Format,
    string TeamName,
    string DepartingUserName,
    string Location,
    string Excerpt,
    VisibilityScope Visibility);

public sealed record AgentChatResponse(
    string AnswerMarkdown,
    string AiModeUsed,
    string McpTransportUsed,
    IReadOnlyList<McpToolExecutionTraceDto> McpToolTraces,
    IReadOnlyList<SourceCitationDto> Citations,
    TabularQueryResultDto? StructuredTableResult = null);
