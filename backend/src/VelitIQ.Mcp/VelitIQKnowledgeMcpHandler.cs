using System.Security.Claims;
using System.Text.Json;
using McpHttpServer.Attributes;
using Microsoft.AspNetCore.Http;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Data;

namespace VelitIQ.Mcp;

[McpHandler]
public sealed class VelitIQKnowledgeMcpHandler
{
    private readonly PostgresKnowledgeStore _store;
    private readonly IAuthService _auth;
    private readonly IAiService _ai;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public VelitIQKnowledgeMcpHandler(
        PostgresKnowledgeStore store,
        IAuthService auth,
        IAiService ai,
        IHttpContextAccessor httpContextAccessor)
    {
        _store = store;
        _auth = auth;
        _ai = ai;
        _httpContextAccessor = httpContextAccessor;
    }

    // ==================== [McpAuthorize] Zero-Reflection Callbacks ====================

    public static bool RequireAuthenticatedStaff(HttpContext? context) =>
        context?.User?.Identity?.IsAuthenticated == true;

    public static bool RequireGlobalAdmin(HttpContext? context) =>
        context?.User?.Identity?.IsAuthenticated == true &&
        context.User.IsInRole(nameof(UserRole.Admin));

    private async Task<SecurityContext> ResolveCallerSecurityContextAsync(CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var sub = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
        if (Guid.TryParse(sub, out var userId))
        {
            return await _auth.BuildSecurityContextAsync(userId, ct);
        }

        throw new UnauthorizedAccessException("Authenticated banking user claim is required.");
    }

    // ==================== MCP TOOLS ====================

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpTool(
        "search_hybrid_knowledge",
        "Searches bank handover documents (PDF, Word, Excel, CSV, OneNote exports, and Author Gap Clarifications) using Hybrid Vector + PostgreSQL Full-Text Reciprocal Rank Fusion while enforcing Team/Department Chinese-wall RBAC.",
        Title = "Hybrid Knowledge Search",
        ReadOnlyHint = true,
        IdempotentHint = true)]
    public async Task<string> SearchHybridKnowledgeAsync(
        [McpParameter("Natural language search query, system name, batch job code, or GL account identifier")] string query,
        [McpParameter("Optional Department GUID filter")] string? departmentId = null,
        [McpParameter("Optional Team GUID filter")] string? teamId = null,
        [McpParameter("Optional Departing User GUID filter")] string? departingUserId = null,
        [McpParameter("Maximum chunks to return (1-15)")] int topK = 6,
        CancellationToken ct = default)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        var qEmb = await _ai.GenerateEmbeddingAsync(query, ct);

        var req = new HybridSearchRequest(
            query,
            Guid.TryParse(departmentId, out var dId) ? dId : null,
            Guid.TryParse(teamId, out var tId) ? tId : null,
            Guid.TryParse(departingUserId, out var uId) ? uId : null,
            null,
            Math.Clamp(topK, 1, 15));

        var hits = await _store.SearchHybridAsync(caller, req, qEmb, ct);
        return JsonSerializer.Serialize(hits, JsonOpts);
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpTool(
        "list_accessible_spreadsheets",
        "Lists all authorized CSV and Excel worksheets with their column schemas, numeric columns, and formula counts so exact tabular queries can be executed.",
        Title = "List Accessible Spreadsheets",
        ReadOnlyHint = true,
        IdempotentHint = true)]
    public async Task<string> ListAccessibleSpreadsheetsAsync(
        [McpParameter("Optional Team GUID filter")] string? teamId = null,
        [McpParameter("Optional Departing User GUID filter")] string? departingUserId = null,
        CancellationToken ct = default)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        var sheets = await _store.GetAccessibleSheetsAsync(
            caller,
            Guid.TryParse(teamId, out var tId) ? tId : null,
            Guid.TryParse(departingUserId, out var uId) ? uId : null,
            null,
            ct);

        var projected = sheets.Select(s =>
        {
            var cols = JsonSerializer.Deserialize<List<TabularColumnSchema>>(s.ColumnsJson, JsonOpts) ?? [];
            var formulas = JsonSerializer.Deserialize<List<SpreadsheetFormulaInfo>>(s.FormulasJson, JsonOpts) ?? [];
            return new
            {
                sheetId = s.Id,
                documentId = s.DocumentId,
                documentTitle = s.DocumentTitle,
                sheetName = s.SheetName,
                rowCount = s.RowCount,
                formulaCount = formulas.Count,
                columnsSummary = string.Join(", ", cols.Select(c => $"{c.Name} ({c.DataType})")),
                numericColumns = cols.Where(c => c.DataType == "number").Select(c => c.Name).ToArray()
            };
        });

        return JsonSerializer.Serialize(projected, JsonOpts);
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpTool(
        "query_tabular_sheet",
        "Executes exact row filtering, grouping, numeric aggregations (sum, avg, min, max, count), and Excel formula inspection on an authorized CSV or Excel worksheet.",
        Title = "Query Spreadsheet Data & Formulas",
        ReadOnlyHint = true,
        IdempotentHint = true)]
    public async Task<string> QueryTabularSheetAsync(
        [McpParameter("Target Sheet GUID from list_accessible_spreadsheets")] string sheetId,
        [McpParameter("Optional column name to filter on")] string? filterColumn = null,
        [McpParameter("Filter operator", EnumValues = new[] { "contains", "eq", "gt", "gte", "lt", "lte" })] string? filterOperator = null,
        [McpParameter("Optional value to match")] string? filterValue = null,
        [McpParameter("Optional numeric column to aggregate")] string? aggregateColumn = null,
        [McpParameter("Aggregation function", EnumValues = new[] { "sum", "avg", "min", "max", "count" })] string? aggregateFunction = null,
        [McpParameter("Optional group-by column")] string? groupByColumn = null,
        [McpParameter("Row limit (1-100)")] int limit = 25,
        CancellationToken ct = default)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        if (!Guid.TryParse(sheetId, out var sGuid))
            return JsonSerializer.Serialize(new { error = "Invalid sheetId GUID." }, JsonOpts);

        var result = await _store.QueryTabularSheetAsync(
            caller,
            new TabularQueryRequest(sGuid, filterColumn, filterOperator, filterValue, aggregateColumn, aggregateFunction, groupByColumn, limit),
            ct);

        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpTool(
        "read_document_section",
        "Reads sequential full-text chunks/sections from an authorized handover document to inspect surrounding context.",
        Title = "Read Full Document Context",
        ReadOnlyHint = true,
        IdempotentHint = true)]
    public async Task<string> ReadDocumentSectionAsync(
        [McpParameter("Target KnowledgeDocument GUID")] string documentId,
        [McpParameter("Starting chunk index (0-based)")] int startChunkIndex = 0,
        [McpParameter("Number of consecutive chunks to read (1-10)")] int count = 5,
        CancellationToken ct = default)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        if (!Guid.TryParse(documentId, out var docGuid))
            return JsonSerializer.Serialize(new { error = "Invalid documentId GUID." }, JsonOpts);

        var doc = await _store.GetDocumentByIdAsync(docGuid, ct);
        if (doc is null || !caller.CanReadResource(doc.DepartmentId, doc.TeamId, doc.Visibility))
            return JsonSerializer.Serialize(new { error = "Document not found or access denied by Team RBAC policy." }, JsonOpts);

        var chunks = await _store.GetDocumentChunksAsync(docGuid, Math.Max(0, startChunkIndex), Math.Clamp(count, 1, 10), ct);
        return JsonSerializer.Serialize(new
        {
            documentId = doc.Id,
            title = doc.Title,
            fileName = doc.OriginalFileName,
            teamName = doc.TeamName,
            departingUser = doc.DepartingUserName,
            sections = chunks.Select(c => new { c.ChunkIndex, c.SectionPath, c.PageOrSheetName, c.Content })
        }, JsonOpts);
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpTool(
        "get_departing_user_dossier",
        "Retrieves December offboarding readiness scores, owned banking systems, recurring schedules, and executive handover dossiers for authorized departing employees.",
        Title = "Get Departing Employee Handover Dossier",
        ReadOnlyHint = true,
        IdempotentHint = true)]
    public async Task<string> GetDepartingUserDossierAsync(
        [McpParameter("Optional Team GUID filter")] string? teamId = null,
        [McpParameter("Optional Departing User GUID filter")] string? departingUserId = null,
        CancellationToken ct = default)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        var profiles = await _store.GetDepartingUsersAsync(
            null,
            Guid.TryParse(teamId, out var tGuid) ? tGuid : null,
            ct);

        var filtered = profiles
            .Where(p => caller.HasGlobalReadAccess || caller.AccessibleTeamIds.Contains(p.TeamId))
            .Where(p => !Guid.TryParse(departingUserId, out var uGuid) || p.Id == uGuid)
            .ToList();

        return JsonSerializer.Serialize(filtered, JsonOpts);
    }

    [McpAuthorize(nameof(RequireGlobalAdmin))]
    [McpTool(
        "governance_audit_overview",
        "Admin-only tool returning unacknowledged secret guardrail findings and recent bank security audit logs.",
        Title = "Admin Governance & Audit Summary",
        ReadOnlyHint = true,
        IdempotentHint = true)]
    public async Task<string> GovernanceAuditOverviewAsync(CancellationToken ct = default)
    {
        var secrets = await _store.GetSecretFindingsAsync(null, ct);
        var logs = await _store.GetAuditLogsAsync(20, ct);
        return JsonSerializer.Serialize(new
        {
            unacknowledgedSecrets = secrets.Count(s => !s.IsAcknowledged),
            secretFindings = secrets.Take(10),
            recentAuditEvents = logs
        }, JsonOpts);
    }

    // ==================== MCP RESOURCES & DYNAMIC TEMPLATES ====================

    [McpResource(
        "velitiq://platform/status",
        "VelitIQ Platform Status",
        "Returns live status of PostgreSQL hybrid search, AI provider mode, and MCP server capabilities.",
        "application/json")]
    public string GetPlatformStatus()
    {
        return JsonSerializer.Serialize(new
        {
            platform = "Horizon VelitIQ — Banking Offboarding & Agentic Knowledge Platform",
            aiMode = _ai.ActiveModeName,
            isCloudAiLive = _ai.IsCloudLive,
            pgVectorNativeHnsw = _store.IsPgVectorEnabled,
            mcpTransport = "McpHttpServer v1.2.0 (Streamable HTTP)"
        }, JsonOpts);
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpResourceTemplate(
        "velitiq://teams/{teamSlug}/handover-pack",
        "Team Handover Pack",
        "Fetches a banking team's authorized departing staff, knowledge artifacts, and open handover gap questions by team slug.",
        "application/json")]
    public async Task<string> GetTeamHandoverPackAsync(string teamSlug, CancellationToken ct)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        var team = await _store.GetTeamBySlugAsync(teamSlug, ct);
        if (team is null)
            return JsonSerializer.Serialize(new { error = $"Team '{teamSlug}' not found." }, JsonOpts);

        var docs = await _store.GetAccessibleDocumentsAsync(caller, teamId: team.Id, ct: ct);
        var users = (await _store.GetDepartingUsersAsync(teamId: team.Id, ct: ct))
            .Where(u => caller.HasGlobalReadAccess || caller.AccessibleTeamIds.Contains(u.TeamId))
            .ToList();

        return JsonSerializer.Serialize(new
        {
            team.Id,
            team.Name,
            team.DepartmentName,
            departingEmployees = users,
            accessibleDocuments = docs
        }, JsonOpts);
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpResourceTemplate(
        "velitiq://users/{userId}/dossier",
        "Departing Employee Executive Dossier",
        "Returns the synthesized Markdown handover dossier for an authorized departing employee.",
        "text/markdown")]
    public async Task<string> ReadUserDossierResourceAsync(string userId, CancellationToken ct)
    {
        var caller = await ResolveCallerSecurityContextAsync(ct);
        if (!Guid.TryParse(userId, out var uGuid))
            return "Invalid departing user ID.";

        var profile = await _store.GetDepartingUserByIdAsync(uGuid, ct);
        if (profile is null || (!caller.HasGlobalReadAccess && !caller.AccessibleTeamIds.Contains(profile.TeamId)))
            return "Access denied or departing employee profile not found.";

        return string.IsNullOrWhiteSpace(profile.ExecutiveSummary)
            ? $"# {profile.FullName}\nNo handover summary synthesized yet."
            : profile.ExecutiveSummary;
    }

    // ==================== MCP PROMPTS ====================

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpPrompt(
        "successor_onboarding_plan",
        "Generates a structured Day-1 to Day-30 successor takeover briefing for a departing bank employee.",
        Title = "Successor 30-Day Takeover Briefing")]
    public string SuccessorOnboardingPlanPrompt(
        [McpArgument("Departing employee's full name")] string departingUserName,
        [McpArgument("Banking team name")] string teamName)
    {
        return $"""
            Prepare a comprehensive Day-1, Week-1, and Month-End takeover plan for the successor assuming ownership from {departingUserName} in team '{teamName}' ahead of their December departure.
            Use the MCP tools (`get_departing_user_dossier`, `search_hybrid_knowledge`, and `list_accessible_spreadsheets` / `query_tabular_sheet`) to cite exact runbooks, batch schedules, reconciliation formulas, and verified author gap clarifications.
            """;
    }

    [McpAuthorize(nameof(RequireAuthenticatedStaff))]
    [McpPrompt(
        "december_exit_gap_audit",
        "Audits operational risks, spreadsheet formulas, and unresolved handover gaps for a team before December exits.",
        Title = "December Exit Risk & Knowledge Audit")]
    public string DecemberExitGapAuditPrompt(
        [McpArgument("Banking team name to audit")] string teamName)
    {
        return $"""
            Perform a rigorous banking operational risk and knowledge completeness audit for team '{teamName}' ahead of the December offboarding deadline.
            Retrieve all departing staff dossiers, runbooks, OneNote architecture exports, and reconciliation workbooks via MCP tools, and highlight key systems, cut-off times, formula dependencies, and remaining risks.
            """;
    }
}
