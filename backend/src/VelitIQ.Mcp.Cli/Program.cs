using System.Text.Json;
using MCPServer;
using MCPServer.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Ai;
using VelitIQ.Infrastructure.Data;

namespace VelitIQ.Mcp.Cli;

/// <summary>
/// Standalone STDIO MCP Companion Server built on Keerthi_RB's Keerukee.MCPServer.Stdio (v1.0.3).
/// Enables Claude Desktop, Cursor, VS Code, or CLI bank agents to query VelitIQ PostgreSQL knowledge packs over STDIO.
/// </summary>
public static class Program
{
    internal static PostgresKnowledgeStore Store = null!;
    internal static LocalDeterministicAiService LocalAi = null!;
    internal static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static void Main(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("VELITIQ_DB_CONNECTION")
                      ?? "Host=127.0.0.1;Port=5432;Database=velitiq;Username=postgres;Password=postgres";

        Store = new PostgresKnowledgeStore(
            Options.Create(new DatabaseOptions { ConnectionString = connStr }),
            NullLogger<PostgresKnowledgeStore>.Instance);

        LocalAi = new LocalDeterministicAiService(Options.Create(new AzureFoundryOptions()));

        var host = new McpServerHost(new McpServerOptions
        {
            ServerName = "VelitIQ-Stdio-KnowledgeServer",
            ServerVersion = "1.0.0",
            ProtocolVersion = "2024-11-05"
        });

        host.Run();
    }
}

public static class VelitIQStdioTools
{
    private static SecurityContext ResolveCliSecurityContext()
    {
        var teamEnv = Environment.GetEnvironmentVariable("VELITIQ_TEAM_IDS") ?? string.Empty;
        var roleEnv = Environment.GetEnvironmentVariable("VELITIQ_ROLE") ?? "CrossTeamViewer";
        Enum.TryParse<UserRole>(roleEnv, true, out var role);

        var teamGuids = teamEnv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToArray();

        return new SecurityContext
        {
            UserId = Guid.Empty,
            Username = "stdio-cli-agent",
            DisplayName = "VelitIQ STDIO CLI Agent",
            Role = role,
            AccessibleTeamIds = teamGuids
        };
    }

    [McpTool("velitiq_search", "Searches bank December offboarding knowledge artifacts using Hybrid RRF retrieval.")]
    public static string SearchKnowledge(
        [McpParameter("Search query, system code, or process name", true)] string query,
        [McpParameter("Optional Team GUID", false)] string teamId = "")
    {
        var caller = ResolveCliSecurityContext();
        var emb = Program.LocalAi.GenerateEmbeddingAsync(query).GetAwaiter().GetResult();
        var hits = Program.Store.SearchHybridAsync(
            caller,
            new HybridSearchRequest(query, null, Guid.TryParse(teamId, out var t) ? t : null, null, null, 6),
            emb).GetAwaiter().GetResult();

        return JsonSerializer.Serialize(hits, Program.JsonOpts);
    }

    [McpTool("velitiq_list_departing_staff", "Lists December departing bank employees and their handover readiness scores.")]
    public static string ListDepartingStaff(
        [McpParameter("Optional Team GUID filter", false)] string teamId = "")
    {
        var caller = ResolveCliSecurityContext();
        var users = Program.Store.GetDepartingUsersAsync(
            null,
            Guid.TryParse(teamId, out var t) ? t : null).GetAwaiter().GetResult()
            .Where(u => caller.HasGlobalReadAccess || caller.AccessibleTeamIds.Contains(u.TeamId))
            .ToList();

        return JsonSerializer.Serialize(users, Program.JsonOpts);
    }

    [McpResource("velitiq://stdio/info", "VelitIQ STDIO Server Info", "Metadata for VelitIQ local STDIO MCP server", "application/json")]
    public static string GetStdioInfo() =>
        """{"server":"VelitIQ-Stdio-KnowledgeServer","package":"Keerukee.MCPServer.Stdio 1.0.3","framework":"net10.0"}""";

    [McpPrompt("velitiq_handover_summary", "Generates a handover briefing prompt for a departing employee")]
    public static string BuildHandoverPrompt(
        [McpParameter("Employee name", true)] string employeeName) =>
        $"Summarize all systems, batch schedules, spreadsheet formulas, and handover gap answers for departing employee {employeeName} using velitiq_search.";
}
