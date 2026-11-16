using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Data;

namespace VelitIQ.Mcp;

public interface IMcpLoopbackClientFactory
{
    (Uri EndpointUri, HttpClient HttpClient, bool DisposeClient) CreateLoopbackHttpTarget();
}

public sealed class DefaultMcpLoopbackClientFactory : IMcpLoopbackClientFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;

    public DefaultMcpLoopbackClientFactory(
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
    }

    public (Uri EndpointUri, HttpClient HttpClient, bool DisposeClient) CreateLoopbackHttpTarget()
    {
        var req = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = req is not null && req.Host.HasValue
            ? $"{req.Scheme}://{req.Host.Value}"
            : "http://127.0.0.1:5180";

        var client = _httpClientFactory.CreateClient("VelitIQMcpLoopback");
        return (new Uri($"{baseUrl.TrimEnd('/')}/mcp"), client, true);
    }
}

/// <summary>
/// Agentic Hybrid RAG Orchestrator powered by Keerthi_RB's Mcp.Client.Net (v1.0.1) communicating
/// over Streamable HTTP JSON-RPC 2.0 with McpHttpServer (v1.2.0).
/// </summary>
public sealed class AgenticRagOrchestrator : IAgenticRagOrchestrator
{
    private readonly IMcpLoopbackClientFactory _mcpTargetFactory;
    private readonly IAiService _ai;
    private readonly PostgresKnowledgeStore _store;
    private readonly ILogger<AgenticRagOrchestrator> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AgenticRagOrchestrator(
        IMcpLoopbackClientFactory mcpTargetFactory,
        IAiService ai,
        PostgresKnowledgeStore store,
        ILogger<AgenticRagOrchestrator> logger)
    {
        _mcpTargetFactory = mcpTargetFactory;
        _ai = ai;
        _store = store;
        _logger = logger;
    }

    public async Task<AgentChatResponse> AskAsync(
        AgentChatRequest request,
        SecurityContext caller,
        string bearerToken,
        CancellationToken ct = default)
    {
        var (mcpUri, httpClient, _) = _mcpTargetFactory.CreateLoopbackHttpTarget();

        // Connect using Keerthi_RB's Mcp.Client.Net over StreamableHttpClientTransport propagating caller's JWT Bearer token
        await using var mcpClient = await McpClientBuilder
            .ForStreamableHttp(mcpUri, httpClient, enableStandaloneSseStream: false)
            .WithHeader("Authorization", $"Bearer {bearerToken}")
            .WithClientInfo("VelitIQ.AgenticOrchestrator", "1.0.1")
            .WithTimeout(TimeSpan.FromSeconds(60))
            .BuildAndInitializeAsync(ct);

        // 1. Dynamically discover authorized MCP tools for this caller ([McpAuthorize] filters unauthorized tools automatically!)
        var toolsList = await mcpClient.ListToolsAsync(ct: ct);
        var aiTools = toolsList.Tools.Select(t => new AiToolDefinition(
            t.Name,
            t.Description ?? string.Empty,
            t.InputSchema.HasValue ? t.InputSchema.Value.GetRawText() : "{\"type\":\"object\",\"properties\":{}}"))
            .ToList();

        // 2. Optionally resolve an MCP Prompt Template if requested by the user UI
        var effectiveQuestion = request.Question;
        if (!string.IsNullOrWhiteSpace(request.PromptTemplateName))
        {
            try
            {
                string teamName = "Authorized Teams";
                string userName = "Departing Staff";
                if (request.TeamId.HasValue)
                {
                    var t = await _store.GetTeamByIdAsync(request.TeamId.Value, ct);
                    if (t is not null) teamName = t.Name;
                }
                if (request.DepartingUserId.HasValue)
                {
                    var u = await _store.GetDepartingUserByIdAsync(request.DepartingUserId.Value, ct);
                    if (u is not null) userName = u.FullName;
                }

                var promptRes = await mcpClient.GetPromptAsync(
                    request.PromptTemplateName,
                    new { departingUserName = userName, teamName },
                    ct);
                var promptText = promptRes.Messages.FirstOrDefault()?.Content?.Text;
                if (!string.IsNullOrWhiteSpace(promptText))
                {
                    effectiveQuestion = $"{promptText}\n\nSpecific User Focus: {request.Question}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Optional MCP prompt resolution skipped for {Prompt}", request.PromptTemplateName);
            }
        }

        var scopeTags = new List<string>();
        if (request.DepartmentId.HasValue) scopeTags.Add($"SCOPE_DEPT_ID={request.DepartmentId.Value}");
        if (request.TeamId.HasValue) scopeTags.Add($"SCOPE_TEAM_ID={request.TeamId.Value}");
        if (request.DepartingUserId.HasValue) scopeTags.Add($"SCOPE_USER_ID={request.DepartingUserId.Value}");

        var systemPrompt = $"""
            You are VelitIQ, Horizon Bank's Enterprise Offboarding & Knowledge Continuity Copilot.
            Caller: {caller.DisplayName} ({caller.Role}).
            Always ground answers strictly in retrieved bank artifacts via the provided MCP tools (`search_hybrid_knowledge`, `list_accessible_spreadsheets`, `query_tabular_sheet`, `read_document_section`, `get_departing_user_dossier`).
            Never fabricate numbers, GL codes, server names, or formulas—use `query_tabular_sheet` for spreadsheet figures and cite document titles and page/sheet locations clearly.
            Active Scope Directives: {string.Join(" ", scopeTags)}
            """;

        var traces = new List<McpToolExecutionTraceDto>();
        var citations = new Dictionary<string, SourceCitationDto>();
        TabularQueryResultDto? latestTableResult = null;
        var completedSteps = new List<(AiAssistantTurn AssistantTurn, IReadOnlyList<AiToolExecutionResult> Results)>();

        string finalMarkdown = string.Empty;

        // Up to 4 multi-step Agentic MCP Tool turns
        for (int step = 0; step < 4; step++)
        {
            var assistantTurn = await _ai.RunAgentTurnAsync(
                systemPrompt,
                request.History ?? [],
                effectiveQuestion,
                aiTools,
                completedSteps,
                ct);

            if (assistantTurn.ToolCalls.Count == 0)
            {
                finalMarkdown = assistantTurn.TextContent ?? "No response generated.";
                break;
            }

            var stepResults = new List<AiToolExecutionResult>();
            foreach (var tc in assistantTurn.ToolCalls)
            {
                var sw = Stopwatch.StartNew();
                object argsObj = ParseJsonArgumentsObject(tc.ArgumentsJson);
                string toolOutJson;

                try
                {
                    var callRes = await mcpClient.CallToolAsync(tc.ToolName, argsObj, ct);
                    toolOutJson = callRes.GetText() ?? "{}";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MCP tool call '{Tool}' returned error", tc.ToolName);
                    toolOutJson = JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
                }
                sw.Stop();

                HarvestCitationsAndTables(tc.ToolName, toolOutJson, citations, ref latestTableResult);

                var summary = SummarizeToolResult(tc.ToolName, toolOutJson);
                traces.Add(new McpToolExecutionTraceDto(tc.ToolName, tc.ArgumentsJson, summary, sw.ElapsedMilliseconds));
                stepResults.Add(new AiToolExecutionResult(tc.CallId, tc.ToolName, toolOutJson));
            }

            completedSteps.Add((assistantTurn, stepResults));
        }

        if (string.IsNullOrWhiteSpace(finalMarkdown))
        {
            finalMarkdown = "Completed MCP retrieval steps; review cited sources and structured tables below.";
        }

        await _store.AppendAuditLogAsync(new AuditLog
        {
            ActorUserId = caller.UserId,
            ActorEmail = caller.Email,
            ActorRole = caller.Role.ToString(),
            Action = "AGENTIC_RAG_QUERY",
            ResourceType = "McpAgenticChat",
            ResourceId = request.TeamId?.ToString() ?? "ALL_AUTHORIZED",
            TeamId = request.TeamId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                request.Question,
                ToolsInvoked = traces.Select(t => t.ToolName).ToArray(),
                CitationsCount = citations.Count
            }, JsonOpts)
        }, ct);

        return new AgentChatResponse(
            finalMarkdown,
            _ai.ActiveModeName,
            $"Mcp.Client.Net v1.0.1 -> McpHttpServer v1.2.0 ({mcpClient.ServerInfo?.ProtocolVersion ?? "StreamableHTTP"})",
            traces,
            citations.Values.ToList(),
            latestTableResult);
    }

    private static Dictionary<string, object?> ParseJsonArgumentsObject(string json)
    {
        var dict = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(json)) return dict;
        try
        {
            if (JsonNode.Parse(json) is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    dict[kv.Key] = kv.Value switch
                    {
                        null => null,
                        JsonValue v when v.TryGetValue<int>(out var i) => i,
                        JsonValue v when v.TryGetValue<double>(out var d) => d,
                        JsonValue v when v.TryGetValue<bool>(out var b) => b,
                        JsonValue v when v.TryGetValue<string>(out var s) => s,
                        _ => kv.Value.ToJsonString()
                    };
                }
            }
        }
        catch
        {
            // Return empty dictionary on malformed JSON
        }
        return dict;
    }

    private static void HarvestCitationsAndTables(
        string toolName,
        string outputJson,
        Dictionary<string, SourceCitationDto> citations,
        ref TabularQueryResultDto? latestTable)
    {
        try
        {
            if (toolName == "search_hybrid_knowledge")
            {
                var hits = JsonSerializer.Deserialize<List<HybridSearchHitDto>>(outputJson, JsonOpts);
                if (hits is null) return;
                foreach (var h in hits)
                {
                    var key = $"{h.DocumentId}:{h.PageOrSheetName}";
                    if (!citations.ContainsKey(key))
                    {
                        var excerpt = h.Content.Length > 220 ? h.Content[..220].Trim() + "..." : h.Content.Trim();
                        citations[key] = new SourceCitationDto(
                            h.DocumentId,
                            h.DocumentTitle,
                            h.OriginalFileName,
                            h.Format.ToString(),
                            h.TeamName,
                            h.DepartingUserName,
                            h.PageOrSheetName,
                            excerpt,
                            h.Visibility);
                    }
                }
            }
            else if (toolName == "query_tabular_sheet")
            {
                var tab = JsonSerializer.Deserialize<TabularQueryResultDto>(outputJson, JsonOpts);
                if (tab is not null)
                {
                    latestTable = tab;
                    var key = $"{tab.DocumentId}:Sheet:{tab.SheetName}";
                    citations[key] = new SourceCitationDto(
                        tab.DocumentId,
                        tab.DocumentTitle,
                        $"{tab.DocumentTitle}.xlsx",
                        nameof(ArtifactFormat.Excel),
                        "Authorized Team",
                        "Spreadsheet Model",
                        $"Sheet: {tab.SheetName}",
                        $"Queried {tab.MatchedRowCount}/{tab.TotalSheetRows} structured rows ({tab.Formulas.Count} formulas).",
                        VisibilityScope.TeamOnly);
                }
            }
        }
        catch
        {
            // Ignore non-matching JSON outputs
        }
    }

    private static string SummarizeToolResult(string toolName, string json)
    {
        try
        {
            if (toolName == "search_hybrid_knowledge")
            {
                var hits = JsonSerializer.Deserialize<List<HybridSearchHitDto>>(json, JsonOpts);
                return $"Retrieved {hits?.Count ?? 0} hybrid RRF chunk(s)";
            }
            if (toolName == "list_accessible_spreadsheets")
            {
                var arr = JsonNode.Parse(json)?.AsArray();
                return $"Discovered {arr?.Count ?? 0} authorized worksheet(s)";
            }
            if (toolName == "query_tabular_sheet")
            {
                var tab = JsonSerializer.Deserialize<TabularQueryResultDto>(json, JsonOpts);
                return tab is not null
                    ? $"Queried '{tab.SheetName}' ({tab.MatchedRowCount} rows, {tab.Formulas.Count} formulas)"
                    : "Executed spreadsheet query";
            }
        }
        catch { /* fallback */ }
        return $"Payload {json.Length} bytes";
    }
}

public static class McpModuleExtensions
{
    public static IServiceCollection AddVelitIQMcpModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient("VelitIQMcpLoopback", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddSingleton<IMcpLoopbackClientFactory, DefaultMcpLoopbackClientFactory>();
        services.AddSingleton<VelitIQKnowledgeMcpHandler>();
        services.AddSingleton<IAgenticRagOrchestrator, AgenticRagOrchestrator>();
        return services;
    }
}
