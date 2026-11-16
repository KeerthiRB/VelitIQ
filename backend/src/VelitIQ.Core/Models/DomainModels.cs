namespace VelitIQ.Core.Models;

public enum UserRole
{
    Employee = 0,
    TeamLead = 1,
    CrossTeamViewer = 2,
    Admin = 3
}

public enum VisibilityScope
{
    TeamOnly = 0,
    DepartmentShared = 1,
    BankWideShared = 2
}

public enum ArtifactFormat
{
    Pdf = 0,
    Word = 1,
    Excel = 2,
    Csv = 3
}

public enum DocumentCategory
{
    Runbook = 0,
    ArchitectureAndDesign = 1,
    FinancialOrRiskModel = 2,
    OneNoteExport = 3,
    ProcessAndSop = 4,
    ContactsAndEscalations = 5,
    DataMapping = 6
}

public enum IngestionStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public enum HandoverStatus
{
    NotStarted = 0,
    InProgress = 1,
    PendingReview = 2,
    ReadyForExit = 3
}

public enum GapQuestionStatus
{
    Open = 0,
    Answered = 1,
    Dismissed = 2
}

public enum SecretSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AdGroupName { get; set; } = string.Empty;
    public string EntraGroupId { get; set; } = string.Empty;
    public string ManagerEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string AuthSource { get; set; } = "Local"; // Local, ActiveDirectory, EntraId
    public UserRole Role { get; set; } = UserRole.Employee;
    public Guid? PrimaryTeamId { get; set; }
    public Guid[] AdditionalTeamIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DepartingUserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public DateOnly ExitDate { get; set; }
    public string SuccessorName { get; set; } = string.Empty;
    public string SuccessorEmail { get; set; } = string.Empty;
    public HandoverStatus Status { get; set; } = HandoverStatus.InProgress;
    public int ReadinessScore { get; set; }
    public string[] SystemsOwned { get; set; } = [];
    public string[] RecurringSchedules { get; set; } = [];
    public string[] KeyContacts { get; set; } = [];
    public string ExecutiveSummary { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KnowledgeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Guid DepartingUserId { get; set; }
    public string DepartingUserName { get; set; } = string.Empty;
    public Guid? UploadedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public ArtifactFormat Format { get; set; }
    public DocumentCategory Category { get; set; }
    public VisibilityScope Visibility { get; set; } = VisibilityScope.TeamOnly;
    public string[] Tags { get; set; } = [];
    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;
    public string StatusMessage { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int PageOrSheetCount { get; set; }
    public int ChunkCount { get; set; }
    public int FormulaCount { get; set; }
    public int DiagramOrImageCount { get; set; }
    public int SecretWarningCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid TeamId { get; set; }
    public Guid DepartingUserId { get; set; }
    public VisibilityScope Visibility { get; set; }
    public int ChunkIndex { get; set; }
    public string SourceType { get; set; } = "DocumentBody"; // DocumentBody, SpreadsheetSummary, FormulaCatalog, AuthorGapAnswer, DiagramCaption
    public string SectionPath { get; set; } = string.Empty;
    public string PageOrSheetName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContextualContent { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = [];
    public int TokenEstimate { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TabularColumnSchema
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DataType { get; set; } = "string"; // string, number, date, boolean
    public List<string> SampleValues { get; set; } = [];
    public double? NumericMin { get; set; }
    public double? NumericMax { get; set; }
    public double? NumericSum { get; set; }
}

public sealed class SpreadsheetFormulaInfo
{
    public string CellAddress { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public string CachedValue { get; set; } = string.Empty;
    public List<string> ReferencedSheets { get; set; } = [];
}

public sealed class TabularSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Guid TeamId { get; set; }
    public Guid DepartingUserId { get; set; }
    public VisibilityScope Visibility { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public int SheetIndex { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string ColumnsJson { get; set; } = "[]";
    public string FormulasJson { get; set; } = "[]";
    public string SummaryMarkdown { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TabularRow
{
    public long Id { get; set; }
    public Guid SheetId { get; set; }
    public Guid DocumentId { get; set; }
    public int RowIndex { get; set; }
    public string RowDataJson { get; set; } = "{}";
    public string SearchText { get; set; } = string.Empty;
}

public sealed class HandoverGapQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepartingUserId { get; set; }
    public string DepartingUserName { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }
    public string? DocumentTitle { get; set; }
    public string Category { get; set; } = "UndocumentedDependency";
    public string Priority { get; set; } = "High";
    public string Question { get; set; } = string.Empty;
    public string ContextSnippet { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public string? AnsweredByName { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public GapQuestionStatus Status { get; set; } = GapQuestionStatus.Open;
    public Guid? IndexedChunkId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SecretScanFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public SecretSeverity Severity { get; set; } = SecretSeverity.Warning;
    public string Location { get; set; } = string.Empty;
    public string RedactedPreview { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ActorUserId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public string IpAddress { get; set; } = string.Empty;
}
