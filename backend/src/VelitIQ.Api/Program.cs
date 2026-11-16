using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Serialization;
using McpHttpServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure;
using VelitIQ.Infrastructure.Data;
using VelitIQ.Infrastructure.Ingestion;
using VelitIQ.Mcp;

var builder = WebApplication.CreateBuilder(args);

// Optimize Kestrel for Streamable HTTP MCP + large bank document uploads (up to 100 MB)
builder.ConfigureKestrelForMcp();
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 104_857_600;
});
builder.Services.Configure<FormOptions>(forms =>
{
    forms.MultipartBodyLengthLimit = 104_857_600;
});

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Core Infrastructure + Keerthi_RB MCP Module
builder.Services.AddVelitIQInfrastructure(builder.Configuration);
builder.Services.AddVelitIQMcpModule();

// CORS for Next.js Web Portal
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:3000", "http://127.0.0.1:3000"];

builder.Services.AddCors(cors =>
{
    cors.AddPolicy("VelitIQWeb", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// JWT Authentication (supports AD/LDAP-minted JWTs, Local Accounts, and Azure Entra ID cutover)
var authCfg = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        if (authCfg.EntraId.Enabled && !string.IsNullOrWhiteSpace(authCfg.EntraId.TenantId))
        {
            jwt.Authority = $"{authCfg.EntraId.Instance.TrimEnd('/')}/{authCfg.EntraId.TenantId}/v2.0";
            jwt.Audience = authCfg.EntraId.Audience;
        }
        else
        {
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authCfg.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = authCfg.JwtAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authCfg.JwtSigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };
        }
    });

builder.Services.AddAuthorization();

// Register Keerthi_RB's McpHttpServer (v1.2.0) at /mcp with Tasks Extension (SEP-2663) & OAuth 2.0 Protected Resource metadata (RFC 9728)
builder.Services.AddMcpHttpServer(mcp =>
{
    mcp.ServerName = "VelitIQ.KnowledgeMcpServer";
    mcp.ServerVersion = "1.2.0";
    mcp.ServerTitle = "Horizon VelitIQ — Banking Knowledge & Offboarding MCP Server";
    mcp.Endpoint = "/mcp";
    mcp.Instructions = "Use VelitIQ MCP tools to search multi-format bank handover documents (PDF, Word, Excel, CSV, OneNote exports), query structured spreadsheets & formulas, and audit December offboarding readiness under strict Team RBAC.";
    mcp.EnableTasksExtension = true;
    mcp.UseDefaultCors = true;
    mcp.OAuthProtectedResource = new McpOAuthProtectedResourceOptions
    {
        AuthorizationServers = [authCfg.EntraId.Enabled ? $"{authCfg.EntraId.Instance.TrimEnd('/')}/{authCfg.EntraId.TenantId}/v2.0" : authCfg.JwtIssuer],
        ScopesSupported = ["velitiq:knowledge.read", "velitiq:knowledge.write", "velitiq:admin"]
    };
});

var app = builder.Build();

// Auto-initialize PostgreSQL schema & seed realistic December offboarding dataset if enabled
var dbOpts = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
var store = app.Services.GetRequiredService<PostgresKnowledgeStore>();
await store.InitializeSchemaAsync();
if (dbOpts.AutoMigrateAndSeed)
{
    var seeder = app.Services.GetRequiredService<BankDemoDataSeeder>();
    await seeder.SeedIfEmptyAsync();
}

app.UseCors("VelitIQWeb");
app.UseAuthentication();
app.UseAuthorization();

// Mount Keerthi_RB's Streamable HTTP MCP Server (/mcp)
app.MapMcpHttpServer(typeof(VelitIQKnowledgeMcpHandler));

// Helper to resolve authenticated caller SecurityContext
static async Task<SecurityContext?> TryGetCallerAsync(HttpContext http, IAuthService auth, CancellationToken ct)
{
    var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub");
    if (!Guid.TryParse(sub, out var userId)) return null;
    return await auth.BuildSecurityContextAsync(userId, ct);
}

static string GetClientIp(HttpContext http) =>
    http.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

// ==================== HEALTH & DIAGNOSTICS ====================

app.MapGet("/api/health", (
    PostgresKnowledgeStore db,
    IAiService ai,
    IFileStorageService storage,
    IOptions<AuthOptions> authOptions,
    IOptions<AzureFoundryOptions> foundryOptions) =>
{
    var fo = foundryOptions.Value;
    return Results.Ok(new
    {
        status = "Healthy",
        application = "Horizon VelitIQ",
        framework = ".NET 10.0",
        database = "PostgreSQL 17",
        pgVectorNativeHnsw = db.IsPgVectorEnabled,
        aiMode = ai.ActiveModeName,
        isCloudAiLive = ai.IsCloudLive,
        azureFoundryConfigured = !string.IsNullOrWhiteSpace(fo.AzureOpenAiUrl) && !string.IsNullOrWhiteSpace(fo.ApiKey),
        azureChatDeployment = fo.ChatDeployment,
        azureEmbeddingDeployment = fo.EmbeddingDeployment,
        authMode = authOptions.Value.Mode,
        ldapEnabled = authOptions.Value.Ldap.Enabled,
        entraIdEnabled = authOptions.Value.EntraId.Enabled,
        storageProvider = storage.ProviderName,
        mcpPackages = new[]
        {
            "McpHttpServer v1.2.0 (/mcp)",
            "Mcp.Client.Net v1.0.1",
            "Keerukee.MCPServer.Stdio v1.0.3"
        },
        timestamp = DateTimeOffset.UtcNow
    });
});

// ==================== AUTHENTICATION (AD/LDAP + WINDOWS SSO + LOCAL + DEMO) ====================

app.MapGet("/api/auth/personas", async (IAuthService auth, CancellationToken ct) =>
    Results.Ok(await auth.GetDemoPersonasAsync(ct)));

app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest req,
    HttpContext http,
    IAuthService auth,
    CancellationToken ct) =>
{
    var res = await auth.LoginWithCredentialsAsync(req, GetClientIp(http), ct);
    return res is not null
        ? Results.Ok(res)
        : Results.Json(new { error = "Invalid Active Directory / Bank credentials." }, statusCode: StatusCodes.Status401Unauthorized);
});

app.MapPost("/api/auth/demo-login", async (
    [FromBody] DemoLoginRequest req,
    HttpContext http,
    IAuthService auth,
    CancellationToken ct) =>
{
    var res = await auth.LoginDemoPersonaAsync(req.Email, GetClientIp(http), ct);
    return res is not null
        ? Results.Ok(res)
        : Results.Json(new { error = "Demo persona login is disabled or user not found." }, statusCode: StatusCodes.Status401Unauthorized);
});

app.MapPost("/api/auth/windows-sso", async (
    HttpContext http,
    IAuthService auth,
    CancellationToken ct) =>
{
    if (OperatingSystem.IsWindows())
    {
        var (isWinAuth, winName, groups) = ExtractWindowsIdentityGroups(http.User.Identity);
        if (isWinAuth && !string.IsNullOrWhiteSpace(winName))
        {
            var res = await auth.LoginWindowsIdentityAsync(winName, groups, GetClientIp(http), ct);
            if (res is not null) return Results.Ok(res);
        }
    }

    return Results.Json(
        new { error = "Windows Integrated Authentication token not present on this request. Ensure IIS Windows Auth is enabled on the On-Prem Windows Server, or sign in via AD/LDAP credentials." },
        statusCode: StatusCodes.Status401Unauthorized);
});

#pragma warning disable CA1416
static (bool IsAuthenticated, string Name, List<string> Groups) ExtractWindowsIdentityGroups(IIdentity? identity)
{
    if (identity is WindowsIdentity winId && winId.IsAuthenticated)
    {
        var groups = winId.Groups?.Select(g =>
        {
            try { return g.Translate(typeof(NTAccount)).Value; }
            catch { return g.Value; }
        }).ToList() ?? [];
        return (true, winId.Name, groups);
    }
    return (false, string.Empty, []);
}
#pragma warning restore CA1416

app.MapGet("/api/auth/me", async (
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var user = await db.GetUserByIdAsync(caller.UserId, ct);
    if (user is null) return Results.Unauthorized();

    var teams = await db.GetTeamsAsync(null, ct);
    var primary = user.PrimaryTeamId.HasValue ? teams.FirstOrDefault(t => t.Id == user.PrimaryTeamId.Value) : null;

    return Results.Ok(new UserProfileDto(
        user.Id,
        user.Username,
        user.Email,
        user.DisplayName,
        user.JobTitle,
        user.AuthSource,
        user.Role,
        primary?.Id,
        primary?.Name,
        primary?.DepartmentId,
        primary?.DepartmentName,
        caller.AccessibleTeamIds));
}).RequireAuthorization();

// ==================== DASHBOARD & ORGANIZATION HIERARCHY ====================

app.MapGet("/api/dashboard", async (
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    IAiService ai,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    return Results.Ok(await db.GetDashboardMetricsAsync(caller, ai.ActiveModeName, ct));
}).RequireAuthorization();

app.MapGet("/api/organization/departments", async (PostgresKnowledgeStore db, CancellationToken ct) =>
    Results.Ok(await db.GetDepartmentsAsync(ct))).RequireAuthorization();

app.MapPost("/api/organization/departments", async (
    [FromBody] CreateDepartmentRequest req,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsAdmin) return Results.Forbid();

    var created = await db.CreateDepartmentAsync(new Department
    {
        Name = req.Name.Trim(),
        Code = req.Code.Trim().ToUpperInvariant(),
        Description = req.Description.Trim()
    }, ct);

    return Results.Ok(created);
}).RequireAuthorization();

app.MapGet("/api/organization/teams", async (
    Guid? departmentId,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    var allTeams = await db.GetTeamsAsync(departmentId, ct);
    return Results.Ok(allTeams);
}).RequireAuthorization();

app.MapPost("/api/organization/teams", async (
    [FromBody] CreateTeamRequest req,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsAdmin) return Results.Forbid();

    var slug = System.Text.RegularExpressions.Regex
        .Replace(req.Name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-")
        .Trim('-');

    var team = await db.CreateTeamAsync(new Team
    {
        DepartmentId = req.DepartmentId,
        Name = req.Name.Trim(),
        Slug = slug,
        Description = req.Description.Trim(),
        AdGroupName = req.AdGroupName.Trim(),
        EntraGroupId = req.EntraGroupId.Trim(),
        ManagerEmail = req.ManagerEmail.Trim()
    }, ct);

    return Results.Ok(team);
}).RequireAuthorization();

// ==================== DEPARTING EMPLOYEES ====================

app.MapGet("/api/departing-users", async (
    Guid? departmentId,
    Guid? teamId,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var users = await db.GetDepartingUsersAsync(departmentId, teamId, ct);
    return Results.Ok(users);
}).RequireAuthorization();

app.MapGet("/api/departing-users/{id:guid}", async (
    Guid id,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var profile = await db.GetDepartingUserByIdAsync(id, ct);
    if (profile is null) return Results.NotFound();

    var docs = await db.GetAccessibleDocumentsAsync(caller, departingUserId: id, ct: ct);
    var gaps = await db.GetGapQuestionsAsync(caller, departingUserId: id, ct: ct);

    return Results.Ok(new
    {
        profile,
        canAccessTeamPrivate = caller.HasGlobalReadAccess || caller.AccessibleTeamIds.Contains(profile.TeamId),
        canWrite = caller.CanWriteToTeam(profile.TeamId),
        documents = docs,
        gapQuestions = gaps
    });
}).RequireAuthorization();

app.MapPost("/api/departing-users", async (
    [FromBody] CreateDepartingUserRequest req,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (!caller.CanWriteToTeam(req.TeamId)) return Results.Forbid();

    var team = await db.GetTeamByIdAsync(req.TeamId, ct);
    if (team is null) return Results.BadRequest(new { error = "Target team not found." });

    var created = await db.UpsertDepartingUserAsync(new DepartingUserProfile
    {
        DepartmentId = team.DepartmentId,
        TeamId = team.Id,
        FullName = req.FullName.Trim(),
        Email = req.Email.Trim(),
        EmployeeId = req.EmployeeId.Trim(),
        JobTitle = req.JobTitle.Trim(),
        ExitDate = req.ExitDate,
        SuccessorName = req.SuccessorName.Trim(),
        SuccessorEmail = req.SuccessorEmail.Trim(),
        SystemsOwned = req.SystemsOwned ?? [],
        RecurringSchedules = req.RecurringSchedules ?? [],
        KeyContacts = req.KeyContacts ?? []
    }, ct);

    return Results.Ok(created);
}).RequireAuthorization();

// ==================== MULTI-FORMAT DOCUMENT UPLOAD, VIEW & VISIBILITY ====================

app.MapGet("/api/documents", async (
    Guid? departmentId,
    Guid? teamId,
    Guid? departingUserId,
    ArtifactFormat? format,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var docs = await db.GetAccessibleDocumentsAsync(caller, departmentId, teamId, departingUserId, format, ct);
    return Results.Ok(docs);
}).RequireAuthorization();

app.MapGet("/api/documents/{id:guid}", async (
    Guid id,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var doc = await db.GetDocumentByIdAsync(id, ct);
    if (doc is null) return Results.NotFound();

    if (!caller.CanReadResource(doc.DepartmentId, doc.TeamId, doc.Visibility))
    {
        await db.AppendAuditLogAsync(new AuditLog
        {
            ActorUserId = caller.UserId,
            ActorEmail = caller.Email,
            ActorRole = caller.Role.ToString(),
            Action = "RBAC_DOCUMENT_ACCESS_DENIED",
            ResourceType = "KnowledgeDocument",
            ResourceId = id.ToString(),
            TeamId = doc.TeamId,
            IpAddress = GetClientIp(http)
        }, ct);
        return Results.Forbid();
    }

    var chunks = await db.GetDocumentChunksAsync(id, 0, 20, ct);
    var sheets = await db.GetAccessibleSheetsAsync(caller, documentId: id, ct: ct);
    var secrets = caller.CanWriteToTeam(doc.TeamId) || caller.HasGlobalReadAccess
        ? await db.GetSecretFindingsAsync(id, ct)
        : [];

    return Results.Ok(new { document = doc, chunks, sheets, secretFindings = secrets });
}).RequireAuthorization();

app.MapGet("/api/documents/{id:guid}/download", async (
    Guid id,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    IFileStorageService storage,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var doc = await db.GetDocumentByIdAsync(id, ct);
    if (doc is null) return Results.NotFound();

    if (!caller.CanReadResource(doc.DepartmentId, doc.TeamId, doc.Visibility))
        return Results.Forbid();

    await db.AppendAuditLogAsync(new AuditLog
    {
        ActorUserId = caller.UserId,
        ActorEmail = caller.Email,
        ActorRole = caller.Role.ToString(),
        Action = "DOC_DOWNLOAD",
        ResourceType = "KnowledgeDocument",
        ResourceId = doc.Id.ToString(),
        TeamId = doc.TeamId,
        IpAddress = GetClientIp(http)
    }, ct);

    var stream = await storage.OpenReadAsync(doc.StoragePath, ct);
    return Results.File(stream, doc.ContentType, doc.OriginalFileName);
}).RequireAuthorization();

app.MapPatch("/api/documents/{id:guid}/visibility", async (
    Guid id,
    [FromBody] VisibilityScope visibility,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var doc = await db.GetDocumentByIdAsync(id, ct);
    if (doc is null) return Results.NotFound();
    if (!caller.CanWriteToTeam(doc.TeamId)) return Results.Forbid();

    await db.UpdateDocumentVisibilityAsync(id, visibility, ct);
    await db.AppendAuditLogAsync(new AuditLog
    {
        ActorUserId = caller.UserId,
        ActorEmail = caller.Email,
        ActorRole = caller.Role.ToString(),
        Action = "DOC_VISIBILITY_UPDATED",
        ResourceType = "KnowledgeDocument",
        ResourceId = id.ToString(),
        TeamId = doc.TeamId,
        DetailsJson = $"{{\"newVisibility\":\"{visibility}\"}}",
        IpAddress = GetClientIp(http)
    }, ct);

    return Results.Ok(await db.GetDocumentByIdAsync(id, ct));
}).RequireAuthorization();

app.MapPost("/api/documents/upload", async (
    HttpRequest request,
    HttpContext http,
    IAuthService auth,
    KnowledgeIngestionService ingestion,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart/form-data upload." });

    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Uploaded file is empty or missing." });

    if (!Guid.TryParse(form["departingUserId"], out var departingUserId))
        return Results.BadRequest(new { error = "Valid departingUserId is required." });

    var title = form["title"].FirstOrDefault();
    Enum.TryParse<DocumentCategory>(form["category"].FirstOrDefault(), true, out var category);
    Enum.TryParse<VisibilityScope>(form["visibility"].FirstOrDefault(), true, out var visibility);
    var tagsRaw = form["tags"].FirstOrDefault() ?? string.Empty;
    var tags = tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    try
    {
        await using var stream = file.OpenReadStream();
        var doc = await ingestion.IngestDocumentAsync(
            stream,
            file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            departingUserId,
            title,
            category,
            visibility,
            tags,
            caller,
            GetClientIp(http),
            ct);

        return Results.Ok(doc);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization().DisableAntiforgery();

// ==================== HANDOVER GAP ELICITATION Q&A ====================

app.MapGet("/api/gaps", async (
    Guid? teamId,
    Guid? departingUserId,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    return Results.Ok(await db.GetGapQuestionsAsync(caller, teamId, departingUserId, ct));
}).RequireAuthorization();

app.MapPost("/api/gaps/{id:guid}/answer", async (
    Guid id,
    [FromBody] AnswerGapQuestionRequest req,
    HttpContext http,
    IAuthService auth,
    KnowledgeIngestionService ingestion,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Answer))
        return Results.BadRequest(new { error = "Answer cannot be empty." });

    try
    {
        var updated = await ingestion.AnswerGapQuestionAndIndexAsync(id, req.Answer, caller, GetClientIp(http), ct);
        return Results.Ok(updated);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
}).RequireAuthorization();

// ==================== SPREADSHEET EXPLORER, HYBRID SEARCH & AGENTIC MCP CHAT ====================

app.MapGet("/api/spreadsheets", async (
    Guid? teamId,
    Guid? departingUserId,
    Guid? documentId,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    return Results.Ok(await db.GetAccessibleSheetsAsync(caller, teamId, departingUserId, documentId, ct));
}).RequireAuthorization();

app.MapPost("/api/spreadsheets/query", async (
    [FromBody] TabularQueryRequest req,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    try
    {
        var res = await db.QueryTabularSheetAsync(caller, req, ct);
        return res is not null ? Results.Ok(res) : Results.NotFound();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
}).RequireAuthorization();

app.MapPost("/api/search/hybrid", async (
    [FromBody] HybridSearchRequest req,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    IAiService ai,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var qEmb = await ai.GenerateEmbeddingAsync(req.Query, ct);
    var hits = await db.SearchHybridAsync(caller, req, qEmb, ct);
    return Results.Ok(hits);
}).RequireAuthorization();

app.MapPost("/api/chat/ask", async (
    [FromBody] AgentChatRequest req,
    HttpContext http,
    IAuthService auth,
    IAgenticRagOrchestrator orchestrator,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();

    var authHeader = http.Request.Headers.Authorization.ToString();
    var bearerToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader["Bearer ".Length..].Trim()
        : string.Empty;

    var response = await orchestrator.AskAsync(req, caller, bearerToken, ct);
    return Results.Ok(response);
}).RequireAuthorization();

// ==================== GLOBAL ADMIN GOVERNANCE & AUDIT ====================

app.MapGet("/api/admin/audit-logs", async (
    int? limit,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (!caller.HasGlobalReadAccess) return Results.Forbid();

    return Results.Ok(await db.GetAuditLogsAsync(limit ?? 100, ct));
}).RequireAuthorization();

app.MapGet("/api/admin/secret-findings", async (
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (!caller.HasGlobalReadAccess) return Results.Forbid();

    return Results.Ok(await db.GetSecretFindingsAsync(null, ct));
}).RequireAuthorization();

app.MapPost("/api/admin/secret-findings/{id:guid}/acknowledge", async (
    Guid id,
    HttpContext http,
    IAuthService auth,
    PostgresKnowledgeStore db,
    CancellationToken ct) =>
{
    var caller = await TryGetCallerAsync(http, auth, ct);
    if (caller is null) return Results.Unauthorized();
    if (!caller.IsAdmin) return Results.Forbid();

    await db.AcknowledgeSecretFindingAsync(id, ct);
    return Results.Ok(new { acknowledged = true });
}).RequireAuthorization();

app.Run();

public partial class Program;
