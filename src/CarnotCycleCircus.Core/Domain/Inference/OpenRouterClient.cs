using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Teams;

namespace CarnotCycleCircus.Core.Domain.Inference;

public record OpenRouterMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("reasoning")] string? Reasoning = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null
);

public record OpenRouterChatRequest(
    string Model,
    IReadOnlyList<OpenRouterMessage> Messages,
    double Temperature = 0.2,
    int MaxTokens = 8192
);

public record OpenRouterChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] OpenRouterMessage Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason = null,
    [property: JsonPropertyName("error")] JsonElement? Error = null
);

public record OpenRouterUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens = 0,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens = 0,
    [property: JsonPropertyName("total_tokens")] int TotalTokens = 0
);

public record OpenRouterChatResponse(
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenRouterChoice>? Choices = null,
    [property: JsonPropertyName("usage")] OpenRouterUsage? Usage = null,
    [property: JsonPropertyName("error")] JsonElement? Error = null
)
{
    public string FirstContent
    {
        get
        {
            if (Choices == null || Choices.Count == 0) return string.Empty;
            var msg = Choices[0].Message;
            if (msg == null) return string.Empty;

            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                return msg.Content;
            }

            if (!string.IsNullOrWhiteSpace(msg.Reasoning))
            {
                return msg.Reasoning;
            }

            if (!string.IsNullOrWhiteSpace(msg.ReasoningContent))
            {
                return msg.ReasoningContent;
            }

            return string.Empty;
        }
    }

    public string? FirstFinishReason => Choices != null && Choices.Count > 0 ? Choices[0].FinishReason : null;
}

public record OpenRouterRawPricingDto(
    JsonElement Prompt = default,
    JsonElement Completion = default,
    JsonElement Image = default,
    JsonElement Request = default
)
{
    public decimal GetPromptDecimal() => ParsePrice(Prompt);
    public decimal GetCompletionDecimal() => ParsePrice(Completion);
    public decimal? GetImageDecimal() => ParsePriceNullable(Image);
    public decimal? GetRequestDecimal() => ParsePriceNullable(Request);

    private static decimal ParsePrice(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ds)) return ds;
        return 0m;
    }

    private static decimal? ParsePriceNullable(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Undefined || el.ValueKind == JsonValueKind.Null) return null;
        return ParsePrice(el);
    }
}

public record OpenRouterRawArchitectureDto(
    string? Modality = null,
    string? Tokenizer = null,
    string? InstructType = null
);

public record OpenRouterRawModelDto(
    string Id,
    string? Name = null,
    string? Description = null,
    int? ContextLength = null,
    OpenRouterRawPricingDto? Pricing = null,
    OpenRouterRawArchitectureDto? Architecture = null
);

public record OpenRouterModelsResponse(
    IReadOnlyList<OpenRouterRawModelDto>? Data = null
);

public interface IOpenRouterClient
{
    Task<OpenRouterChatResponse> CompleteAsync(
        OpenRouterChatRequest request,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(
        string? apiKey = null,
        CancellationToken cancellationToken = default);
}

public class OpenRouterClient : IOpenRouterClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string ModelsUrl = "https://openrouter.ai/api/v1/models";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public OpenRouterClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
    }

    public async Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey.Contains("sandbox", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            // Offline sandbox mode or unconfigured key fallback
            await Task.Delay(50, cancellationToken);
            return Array.Empty<OpenRouterRawModelDto>();
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        httpRequest.Headers.Add("HTTP-Referer", "https://github.com/chadahoochie/carnot-cycle-circus");
        httpRequest.Headers.Add("X-Title", "Carnot Cycle Circus");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await _httpClient.SendAsync(httpRequest, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"OpenRouter Models API returned {(int)response.StatusCode} {response.ReasonPhrase}: {errBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<OpenRouterModelsResponse>(responseBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return parsed?.Data ?? Array.Empty<OpenRouterRawModelDto>();
    }

    public async Task<OpenRouterChatResponse> CompleteAsync(
        OpenRouterChatRequest request,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("An active OpenRouter API key is required to perform LLM inference.");
        }

        var json = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content ?? string.Empty }),
            temperature = request.Temperature,
            max_tokens = request.MaxTokens
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        httpRequest.Headers.Add("HTTP-Referer", "https://github.com/chadahoochie/carnot-cycle-circus");
        httpRequest.Headers.Add("X-Title", "Carnot Cycle Circus");
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"OpenRouter API returned {(int)response.StatusCode} {response.ReasonPhrase}: {errBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (parsed == null)
        {
            throw new InvalidOperationException("Failed to deserialize OpenRouter response.");
        }

        if (parsed.Error.HasValue && parsed.Error.Value.ValueKind != JsonValueKind.Undefined && parsed.Error.Value.ValueKind != JsonValueKind.Null)
        {
            var errMsg = parsed.Error.Value.ToString();
            throw new HttpRequestException($"OpenRouter returned API error: {errMsg}");
        }

        return parsed;
    }
}

public record ResolvedInferenceConfig(
    string PrimaryModel,
    string? FallbackModel,
    string ApiKey
);

public interface IAgentInferenceResolver
{
    (string Model, string ApiKey) ResolveInferenceParameters(AgentMember member, EngineeringTeam team);
    ResolvedInferenceConfig ResolveInferenceConfig(AgentMember member, EngineeringTeam team);
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
        var config = ResolveInferenceConfig(member, team);
        return (config.PrimaryModel, config.ApiKey);
    }

    public ResolvedInferenceConfig ResolveInferenceConfig(AgentMember member, EngineeringTeam team)
    {
        var primaryModel = !string.IsNullOrWhiteSpace(member.EffectiveModel)
            ? member.EffectiveModel
            : (!string.IsNullOrWhiteSpace(member.Persona.DefaultModel)
                ? member.Persona.DefaultModel
                : (!string.IsNullOrWhiteSpace(member.Persona.FallbackModel)
                    ? member.Persona.FallbackModel
                    : (!string.IsNullOrWhiteSpace(team.DefaultFallbackModel)
                        ? team.DefaultFallbackModel
                        : string.Empty)));

        string? fallbackModel = null;
        if (!string.IsNullOrWhiteSpace(member.Persona.FallbackModel) &&
            !string.Equals(member.Persona.FallbackModel, primaryModel, StringComparison.OrdinalIgnoreCase))
        {
            fallbackModel = member.Persona.FallbackModel;
        }
        else if (!string.IsNullOrWhiteSpace(team.DefaultFallbackModel) &&
                 !string.Equals(team.DefaultFallbackModel, primaryModel, StringComparison.OrdinalIgnoreCase))
        {
            fallbackModel = team.DefaultFallbackModel;
        }

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
            apiKey = _keyVault.GetActiveKey()?.RawApiKey ?? string.Empty;
        }

        return new ResolvedInferenceConfig(primaryModel, fallbackModel, apiKey);
    }
}
