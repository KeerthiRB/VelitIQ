using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Ai;

/// <summary>
/// Automatically routes to Azure AI Foundry when AzureOpenAiUrl + ApiKey are configured (office / Azure / Windows Server),
/// and falls back seamlessly to LocalDeterministicAiService when running offline on a developer laptop.
/// </summary>
public sealed class CompositeAiService : IAiService
{
    private readonly AzureFoundryAiService _azure;
    private readonly LocalDeterministicAiService _local;
    private readonly AzureFoundryOptions _options;
    private readonly ILogger<CompositeAiService> _logger;

    private bool ShouldUseAzure =>
        !_options.Mode.Equals("LocalDeterministic", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(_options.AzureOpenAiUrl) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !_options.ApiKey.Contains("YOUR_AZURE_FOUNDRY_API_KEY", StringComparison.OrdinalIgnoreCase);

    public string ActiveModeName => ShouldUseAzure ? _azure.ActiveModeName : _local.ActiveModeName;
    public bool IsCloudLive => ShouldUseAzure;

    public CompositeAiService(
        AzureFoundryAiService azure,
        LocalDeterministicAiService local,
        IOptions<AzureFoundryOptions> options,
        ILogger<CompositeAiService> logger)
    {
        _azure = azure;
        _local = local;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (!ShouldUseAzure) return await _local.GenerateEmbeddingAsync(text, ct);
        try
        {
            return await _azure.GenerateEmbeddingAsync(text, ct);
        }
        catch (Exception ex) when (_options.Mode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Azure AI Foundry embedding call failed in Auto mode; falling back to local vector engine.");
            return await _local.GenerateEmbeddingAsync(text, ct);
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!ShouldUseAzure) return await _local.GenerateEmbeddingsBatchAsync(texts, ct);
        try
        {
            return await _azure.GenerateEmbeddingsBatchAsync(texts, ct);
        }
        catch (Exception ex) when (_options.Mode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Azure AI Foundry batch embedding failed in Auto mode; falling back to local vector engine.");
            return await _local.GenerateEmbeddingsBatchAsync(texts, ct);
        }
    }

    public async Task<GeneratedHandoverInsights> AnalyzeDocumentForHandoverGapsAsync(
        KnowledgeDocument document,
        DocumentParseResult parseResult,
        DepartingUserProfile departingUser,
        CancellationToken ct = default)
    {
        if (!ShouldUseAzure)
            return await _local.AnalyzeDocumentForHandoverGapsAsync(document, parseResult, departingUser, ct);

        try
        {
            return await _azure.AnalyzeDocumentForHandoverGapsAsync(document, parseResult, departingUser, ct);
        }
        catch (Exception ex) when (_options.Mode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Azure AI Foundry gap analysis failed in Auto mode; using local structural analyzer.");
            return await _local.AnalyzeDocumentForHandoverGapsAsync(document, parseResult, departingUser, ct);
        }
    }

    public async Task<string> SynthesizeUserDossierAsync(
        DepartingUserProfile profile,
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<HandoverGapQuestion> gaps,
        CancellationToken ct = default)
    {
        if (!ShouldUseAzure)
            return await _local.SynthesizeUserDossierAsync(profile, documents, gaps, ct);

        try
        {
            return await _azure.SynthesizeUserDossierAsync(profile, documents, gaps, ct);
        }
        catch (Exception ex) when (_options.Mode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Azure AI Foundry dossier synthesis failed in Auto mode; using local synthesis.");
            return await _local.SynthesizeUserDossierAsync(profile, documents, gaps, ct);
        }
    }

    public async Task<AiAssistantTurn> RunAgentTurnAsync(
        string systemPrompt,
        IReadOnlyList<AgentChatMessageDto> history,
        string userQuestion,
        IReadOnlyList<AiToolDefinition> availableTools,
        IReadOnlyList<(AiAssistantTurn AssistantTurn, IReadOnlyList<AiToolExecutionResult> Results)> previousToolSteps,
        CancellationToken ct = default)
    {
        if (!ShouldUseAzure)
            return await _local.RunAgentTurnAsync(systemPrompt, history, userQuestion, availableTools, previousToolSteps, ct);

        try
        {
            return await _azure.RunAgentTurnAsync(systemPrompt, history, userQuestion, availableTools, previousToolSteps, ct);
        }
        catch (Exception ex) when (_options.Mode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Azure AI Foundry agent turn failed in Auto mode; falling back to local MCP agent planner.");
            return await _local.RunAgentTurnAsync(systemPrompt, history, userQuestion, availableTools, previousToolSteps, ct);
        }
    }
}
