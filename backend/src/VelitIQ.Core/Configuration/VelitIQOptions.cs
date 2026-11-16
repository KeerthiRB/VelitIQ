using VelitIQ.Core.Models;

namespace VelitIQ.Core.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = "Host=127.0.0.1;Port=5432;Database=velitiq;Username=postgres;Password=postgres";
    public int EmbeddingDimensions { get; set; } = 1536;
    public bool AutoMigrateAndSeed { get; set; } = true;
}

public sealed class AzureFoundryOptions
{
    public const string SectionName = "AzureFoundry";
    /// <summary>
    /// Mode: "Auto" (uses Azure AI Foundry when Endpoint + ApiKey are provided, otherwise falls back cleanly to LocalDeterministic for laptop dev/testing),
    /// "AzureFoundry" (strictly requires Azure AI Foundry), or "LocalDeterministic".
    /// </summary>
    public string Mode { get; set; } = "Auto";
    public string AzureOpenAiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-10-21";
    public string ChatDeployment { get; set; } = "gpt-4o";
    public string VisionDeployment { get; set; } = "gpt-4o";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    /// <summary>
    /// Supported modes: "HybridAdAndLocal" (On-Prem AD/LDAP + Windows Auth + Local Accounts),
    /// "ActiveDirectoryOnly", "EntraId".
    /// </summary>
    public string Mode { get; set; } = "HybridAdAndLocal";
    public string JwtIssuer { get; set; } = "VelitIQ.BankKnowledgePlatform";
    public string JwtAudience { get; set; } = "VelitIQ.Client";
    public string JwtSigningKey { get; set; } = "HorizonBank_VelitIQ_Enterprise_Jwt_Signing_Key_2026_ChangeInProd_64Bytes!";
    public int TokenExpiryHours { get; set; } = 12;
    public bool EnableDemoPersonaSelector { get; set; } = true;

    // On-Prem Active Directory / LDAP Settings
    public LdapSettings Ldap { get; set; } = new();

    // Future Azure Entra ID Settings (Zero-rewrite cloud cutover)
    public EntraIdSettings EntraId { get; set; } = new();
}

public sealed class LdapSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "dc01.horizonbank.internal";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } = false;
    public string Domain { get; set; } = "HORIZONBANK";
    public string BaseDn { get; set; } = "DC=horizonbank,DC=internal";
    public string AdminGroupCn { get; set; } = "CN=SG-VelitIQ-Admins";
    public string CrossTeamGroupCn { get; set; } = "CN=SG-VelitIQ-CrossTeamViewers";
}

public sealed class EntraIdSettings
{
    public bool Enabled { get; set; } = false;
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    /// <summary>
    /// "LocalDisk" (Windows Server / UNC share) or "AzureBlob" (Azure cutover).
    /// </summary>
    public string Provider { get; set; } = "LocalDisk";
    public string LocalRootPath { get; set; } = "storage_data";
    public string AzureBlobConnectionString { get; set; } = string.Empty;
    public string AzureBlobContainerName { get; set; } = "velitiq-knowledge-artifacts";
    public long MaxUploadSizeBytes { get; set; } = 104_857_600; // 100 MB
}

public sealed class SecurityContext
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public UserRole Role { get; init; }
    public Guid? PrimaryDepartmentId { get; init; }
    public Guid[] AccessibleTeamIds { get; init; } = [];
    public Guid[] AccessibleDepartmentIds { get; init; } = [];

    public bool HasGlobalReadAccess => Role is UserRole.Admin or UserRole.CrossTeamViewer;
    public bool IsAdmin => Role == UserRole.Admin;

    public bool CanReadResource(Guid departmentId, Guid teamId, VisibilityScope visibility)
    {
        if (HasGlobalReadAccess) return true;
        if (visibility == VisibilityScope.BankWideShared) return true;
        if (AccessibleTeamIds.Contains(teamId)) return true;
        if (visibility == VisibilityScope.DepartmentShared && AccessibleDepartmentIds.Contains(departmentId)) return true;
        return false;
    }

    public bool CanWriteToTeam(Guid teamId)
    {
        if (IsAdmin) return true;
        return AccessibleTeamIds.Contains(teamId);
    }
}
