using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Inference;

public record OpenRouterMessage(string Role, string Content);

public record OpenRouterChatRequest(
    string Model,
    IReadOnlyList<OpenRouterMessage> Messages,
    double Temperature = 0.2,
    int MaxTokens = 2048
);

public record OpenRouterChoice(
    int Index,
    OpenRouterMessage Message,
    string? FinishReason
);

public record OpenRouterUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);

public record OpenRouterChatResponse(
    string Id,
    string Model,
    IReadOnlyList<OpenRouterChoice> Choices,
    OpenRouterUsage? Usage
)
{
    public string FirstContent => Choices.Count > 0 ? Choices[0].Message.Content : string.Empty;
}

public interface IOpenRouterClient
{
    Task<OpenRouterChatResponse> CompleteAsync(
        OpenRouterChatRequest request,
        string apiKey,
        CancellationToken cancellationToken = default);
}

public class OpenRouterClient : IOpenRouterClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";

    public OpenRouterClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<OpenRouterChatResponse> CompleteAsync(
        OpenRouterChatRequest request,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (apiKey.Contains("sandbox", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            // Offline sandbox mode fallback
            await Task.Delay(100, cancellationToken);
            return new OpenRouterChatResponse(
                Id: $"sim-{Guid.NewGuid().ToString("N")[..8]}",
                Model: request.Model,
                Choices: [
                    new OpenRouterChoice(0, new OpenRouterMessage("assistant", $"[Sandbox Output from {request.Model}]\nProcessed request with prompt length {request.Messages.Sum(m => m.Content.Length)} characters."), "stop")
                ],
                Usage: new OpenRouterUsage(120, 85, 205)
            );
        }

        var json = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = request.Temperature,
            max_tokens = request.MaxTokens
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.Add("HTTP-Referer", "https://github.com/chadahoochie/carnot-cycle-circus");
        httpRequest.Headers.Add("X-Title", "Carnot Cycle Circus");
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return parsed ?? throw new InvalidOperationException("Failed to deserialize OpenRouter response.");
    }
}

public interface IAgentInferenceResolver
{
    (string Model, string ApiKey) ResolveInferenceParameters(AgentMember member, EngineeringTeam team);
}

public class AgentInferenceResolver : IAgentInferenceResolver
{
    private readonly IApiKeyVaultService _keyVault;

    public AgentInferenceResolver(IApiKeyVaultService keyVault)
    {
        _keyVault = keyVault;
    }

    public (string Model, string ApiKey) ResolveInferenceParameters(AgentMember member, EngineeringTeam team)
    {
        var model = member.EffectiveModel;
        
        string? apiKey = null;
        if (!string.IsNullOrEmpty(member.CustomApiKeyId))
        {
            apiKey = _keyVault.GetKey(member.CustomApiKeyId)?.RawApiKey;
        }

        if (string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(team.ActiveGlobalApiKeyId))
        {
            apiKey = _keyVault.GetKey(team.ActiveGlobalApiKeyId)?.RawApiKey;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = _keyVault.GetActiveKey()?.RawApiKey ?? "sk-or-v1-sandbox-mock-carnot-circus-0001";
        }

        return (model, apiKey);
    }
}
