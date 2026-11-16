using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Ai;

/// <summary>
/// Production Azure AI Foundry / Azure OpenAI client using AzureOpenAiUrl + ApiKey.
/// Supports Embeddings, Multimodal Vision diagram extraction, Handover Gap Elicitation, and MCP Tool-Calling Chat Completions.
/// </summary>
public sealed class AzureFoundryAiService : IAiService
{
    private readonly HttpClient _http;
    private readonly AzureFoundryOptions _options;
    private readonly ILogger<AzureFoundryAiService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public string ActiveModeName => $"Azure AI Foundry ({_options.ChatDeployment} / {_options.EmbeddingDeployment})";
    public bool IsCloudLive => true;

    public AzureFoundryAiService(
        HttpClient http,
        IOptions<AzureFoundryOptions> options,
        ILogger<AzureFoundryAiService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    private string BuildDeploymentEndpoint(string deployment, string operation)
    {
        var baseUrl = _options.AzureOpenAiUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            return $"{baseUrl}/deployments/{Uri.EscapeDataString(deployment)}/{operation}?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

        return $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(deployment)}/{operation}?api-version={Uri.EscapeDataString(_options.ApiVersion)}";
    }

    private HttpRequestMessage CreateAuthorizedPost(string url, object payload)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("api-key", _options.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");
        return req;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var batch = await GenerateEmbeddingsBatchAsync([text], ct);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];
        var results = new List<float[]>(texts.Count);
        var url = BuildDeploymentEndpoint(_options.EmbeddingDeployment, "embeddings");

        const int batchSize = 24;
        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var slice = texts.Skip(i).Take(batchSize)
                .Select(t => string.IsNullOrWhiteSpace(t) ? "empty" : (t.Length > 24000 ? t[..24000] : t))
                .ToList();

            var payload = new
            {
                input = slice,
                dimensions = _options.EmbeddingDimensions
            };

            using var req = CreateAuthorizedPost(url, payload);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct);
            var dataArr = json?["data"]?.AsArray();
            if (dataArr is null) throw new InvalidOperationException("Invalid embeddings payload from Azure AI Foundry.");

            foreach (var item in dataArr)
            {
                var vecArr = item?["embedding"]?.AsArray();
                var floats = vecArr?.Select(v => v?.GetValue<float>() ?? 0f).ToArray() ?? [];
                results.Add(floats);
            }
        }

        return results;
    }

    public async Task<GeneratedHandoverInsights> AnalyzeDocumentForHandoverGapsAsync(
        KnowledgeDocument document,
        DocumentParseResult parseResult,
        DepartingUserProfile departingUser,
        CancellationToken ct = default)
    {
        var sampleText = string.Join("\n\n---\n\n", parseResult.Chunks.Take(8).Select(c => $"[{c.PageOrSheetName}] {c.Content}"));
        if (sampleText.Length > 18000) sampleText = sampleText[..18000];

        var contentParts = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"""
                    Analyze this handover artifact uploaded by departing bank employee {departingUser.FullName} ({departingUser.JobTitle}, exiting {departingUser.ExitDate:yyyy-MM-dd}) in team '{document.TeamName}'.
                    Document: {document.OriginalFileName} ({document.Format})

                    Extracted Content Excerpt:
                    {sampleText}

                    Return STRICT JSON with keys:
                    - "documentSummary": concise 2-3 sentence executive summary
                    - "systemsDetected": array of server names, databases, feeds, or banking apps mentioned
                    - "recurringSchedulesDetected": array of daily/EOD/month-end batch schedules or SLAs
                    - "keyContactsDetected": array of emails, teams, or vendor escalation contacts
                    - "gapQuestions": array of 3 targeted handover clarification objects with keys ("category", "priority", "question", "contextSnippet") asking about undocumented exceptions, acronyms, manual overrides, or spreadsheet formulas before the employee leaves in December.
                    """
            }
        };

        // Attach up to 2 embedded diagram/OneNote screenshot images for Azure Foundry Vision analysis
        if (parseResult.ExtractedPageOrDiagramImages is { Count: > 0 })
        {
            foreach (var imgBytes in parseResult.ExtractedPageOrDiagramImages.Take(2))
            {
                contentParts.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject
                    {
                        ["url"] = $"data:image/png;base64,{Convert.ToBase64String(imgBytes)}"
                    }
                });
            }
        }

        var deployment = parseResult.ExtractedPageOrDiagramImages is { Count: > 0 }
            ? _options.VisionDeployment
            : _options.ChatDeployment;

        var url = BuildDeploymentEndpoint(deployment, "chat/completions");
        var payload = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = "You are a Principal Banking Knowledge Transfer Auditor. Output valid JSON only." },
                new JsonObject { ["role"] = "user", ["content"] = contentParts }
            },
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = 0.2
        };

        using var req = CreateAuthorizedPost(url, payload);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var root = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct);
        var rawJson = root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "{}";
        return JsonSerializer.Deserialize<GeneratedHandoverInsights>(rawJson, JsonOpts)
               ?? new GeneratedHandoverInsights("Processed via Azure AI Foundry.", [], [], [], []);
    }

    public async Task<string> SynthesizeUserDossierAsync(
        DepartingUserProfile profile,
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<HandoverGapQuestion> gaps,
        CancellationToken ct = default)
    {
        var docsBlock = string.Join("\n", documents.Select(d => $"- `{d.OriginalFileName}` ({d.Format}, {d.Visibility}): {d.Summary}"));
        var answeredBlock = string.Join("\n", gaps.Where(g => g.Status == GapQuestionStatus.Answered).Select(g => $"- Q: {g.Question}\n  A: {g.Answer}"));

        var url = BuildDeploymentEndpoint(_options.ChatDeployment, "chat/completions");
        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = "Generate a crisp executive Markdown Handover Dossier for a departing bank employee's successor." },
                new
                {
                    role = "user",
                    content = $"Employee: {profile.FullName} ({profile.JobTitle}), Team: {profile.TeamName}, Exit Date: {profile.ExitDate:yyyy-MM-dd}, Successor: {profile.SuccessorName}.\nDocuments:\n{docsBlock}\nAnswered Clarifications:\n{answeredBlock}"
                }
            },
            temperature = 0.2
        };

        using var req = CreateAuthorizedPost(url, payload);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var root = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct);
        return root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
    }

    public async Task<AiAssistantTurn> RunAgentTurnAsync(
        string systemPrompt,
        IReadOnlyList<AgentChatMessageDto> history,
        string userQuestion,
        IReadOnlyList<AiToolDefinition> availableTools,
        IReadOnlyList<(AiAssistantTurn AssistantTurn, IReadOnlyList<AiToolExecutionResult> Results)> previousToolSteps,
        CancellationToken ct = default)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt }
        };

        foreach (var h in history.TakeLast(6))
        {
            messages.Add(new JsonObject
            {
                ["role"] = h.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                ["content"] = h.Content
            });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = userQuestion });

        foreach (var (turn, results) in previousToolSteps)
        {
            var tcArray = new JsonArray();
            foreach (var tc in turn.ToolCalls)
            {
                tcArray.Add(new JsonObject
                {
                    ["id"] = tc.CallId,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tc.ToolName,
                        ["arguments"] = tc.ArgumentsJson
                    }
                });
            }

            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = turn.TextContent,
                ["tool_calls"] = tcArray
            });

            foreach (var res in results)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = res.CallId,
                    ["content"] = res.OutputJson
                });
            }
        }

        var toolsArray = new JsonArray();
        foreach (var t in availableTools)
        {
            JsonNode? schemaNode = null;
            try { schemaNode = JsonNode.Parse(t.JsonSchema); } catch { /* fallback */ }
            schemaNode ??= new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };

            toolsArray.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = schemaNode
                }
            });
        }

        var payload = new JsonObject
        {
            ["messages"] = messages,
            ["temperature"] = 0.1
        };

        if (toolsArray.Count > 0)
        {
            payload["tools"] = toolsArray;
            payload["tool_choice"] = "auto";
        }

        var url = BuildDeploymentEndpoint(_options.ChatDeployment, "chat/completions");
        using var req = CreateAuthorizedPost(url, payload);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var root = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct);
        var msg = root?["choices"]?[0]?["message"]?.AsObject();
        var textContent = msg?["content"]?.GetValue<string>();

        var calls = new List<AiToolCall>();
        if (msg?["tool_calls"] is JsonArray toolCallsJson)
        {
            foreach (var item in toolCallsJson)
            {
                var id = item?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var fnName = item?["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                var argsJson = item?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                if (!string.IsNullOrWhiteSpace(fnName))
                {
                    calls.Add(new AiToolCall(id, fnName, argsJson));
                }
            }
        }

        return new AiAssistantTurn(textContent, calls);
    }
}
