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

    Task<OpenRouterChatResponse> CompleteStreamAsync(
        OpenRouterChatRequest request,
        string apiKey,
        Action<string>? onChunk = null,
        CancellationToken cancellationToken = default) => CompleteAsync(request, apiKey, cancellationToken);

    Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(
        string? apiKey = null,
        CancellationToken cancellationToken = default);
}

public class OpenRouterClient : IOpenRouterClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string ModelsUrl = "https://openrouter.ai/api/v1/models";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

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

        int maxRetries = 2;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            httpRequest.Headers.Add("HTTP-Referer", "https://github.com/chadahoochie/carnot-cycle-circus");
            httpRequest.Headers.Add("X-Title", "Carnot Cycle Circus");
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < maxRetries)
                {
                    await Task.Delay((attempt + 1) * 2000, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw CreateDescriptiveApiException(response, request.Model, errBody);
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
            finally
            {
                response?.Dispose();
            }
        }

        throw new HttpRequestException("OpenRouter request failed after retry attempts.");
    }

    public async Task<OpenRouterChatResponse> CompleteStreamAsync(
        OpenRouterChatRequest request,
        string apiKey,
        Action<string>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("An active OpenRouter API key is required to perform LLM inference.");
        }

        if (apiKey.Contains("mock", StringComparison.OrdinalIgnoreCase) ||
            apiKey.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return await CompleteAsync(request, apiKey, cancellationToken);
        }

        var json = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content ?? string.Empty }),
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = true
        });

        int maxRetries = 2;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            httpRequest.Headers.Add("HTTP-Referer", "https://github.com/chadahoochie/carnot-cycle-circus");
            httpRequest.Headers.Add("X-Title", "Carnot Cycle Circus");
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && attempt < maxRetries)
                {
                    await Task.Delay((attempt + 1) * 2000, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw CreateDescriptiveApiException(response, request.Model, errBody);
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var fullContent = new StringBuilder();
                string? finishReason = null;
                string? modelUsed = request.Model;
                string? completionId = null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("data: "))
                    {
                        var data = line["data: ".Length..].Trim();
                        if (data == "[DONE]") break;

                        try
                        {
                            using var doc = JsonDocument.Parse(data);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("id", out var idElem)) completionId = idElem.GetString();
                            if (root.TryGetProperty("model", out var mElem)) modelUsed = mElem.GetString();

                            if (root.TryGetProperty("choices", out var choicesElem) && choicesElem.GetArrayLength() > 0)
                            {
                                var firstChoice = choicesElem[0];
                                if (firstChoice.TryGetProperty("finish_reason", out var frElem) && frElem.ValueKind == JsonValueKind.String)
                                {
                                    finishReason = frElem.GetString();
                                }

                                if (firstChoice.TryGetProperty("delta", out var deltaElem))
                                {
                                    string? chunkText = null;
                                    if (deltaElem.TryGetProperty("content", out var cElem) && cElem.ValueKind == JsonValueKind.String)
                                    {
                                        chunkText = cElem.GetString();
                                    }
                                    else if (deltaElem.TryGetProperty("reasoning", out var rElem) && rElem.ValueKind == JsonValueKind.String)
                                    {
                                        chunkText = rElem.GetString();
                                    }

                                    if (!string.IsNullOrEmpty(chunkText))
                                    {
                                        fullContent.Append(chunkText);
                                        onChunk?.Invoke(chunkText);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }

                var contentStr = fullContent.ToString();
                return new OpenRouterChatResponse(
                    Id: completionId ?? $"gen-stream-{Guid.NewGuid():N}",
                    Model: modelUsed,
                    Choices: new List<OpenRouterChoice>
                    {
                        new(
                            Index: 0,
                            Message: new OpenRouterMessage("assistant", contentStr),
                            FinishReason: finishReason ?? "stop"
                        )
                    }
                );
            }
            catch (HttpRequestException ex) when (IsNonRetryableClientError(ex))
            {
                throw;
            }
            catch (Exception) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay((attempt + 1) * 2000, cancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
        }

        // Fallback to standard CompleteAsync if stream attempts exhausted
        return await CompleteAsync(request, apiKey, cancellationToken);
    }

    private static bool IsNonRetryableClientError(HttpRequestException ex) =>
        ex.Message.Contains("Bad Request", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Payment Required", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Model Not Found", StringComparison.OrdinalIgnoreCase);

    private static HttpRequestException CreateDescriptiveApiException(HttpResponseMessage response, string model, string errBody)
    {
        var statusCode = (int)response.StatusCode;
        var explanation = statusCode switch
        {
            400 => $"Bad Request - OpenRouter model '{model}' rejected request parameters or payload schema",
            401 => "Unauthorized - Invalid OpenRouter API key. Please check your key in Key Vault",
            402 => $"Payment Required - Insufficient OpenRouter credit balance to run model '{model}'",
            403 => $"Forbidden - Access denied to model '{model}'",
            404 => $"Model Not Found - OpenRouter model '{model}' does not exist or has been deprecated",
            429 => $"Rate Limited - OpenRouter rate limit exceeded for model '{model}'",
            _ => $"HTTP {statusCode} {response.ReasonPhrase}"
        };
        return new HttpRequestException($"OpenRouter API returned {explanation}: {errBody}");
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
