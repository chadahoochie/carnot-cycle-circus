using System.Collections.Concurrent;

namespace CarnotCycleCircus.Core.Domain.Inference;

public record ApiKeyVaultEntry(
    string KeyId,
    string KeyName,
    string RawApiKey,
    string Provider = "OpenRouter",
    bool IsActive = true,
    DateTimeOffset CreatedAt = default
)
{
    public string ApiKeyMasked => RawApiKey.Length > 8
        ? $"{RawApiKey[..4]}...{RawApiKey[^4..]}"
        : "********";
}

public interface IApiKeyVaultService
{
    IReadOnlyList<ApiKeyVaultEntry> GetAllKeys();
    ApiKeyVaultEntry? GetKey(string keyId);
    ApiKeyVaultEntry? GetActiveKey();
    ApiKeyVaultEntry AddOrUpdateKey(string keyName, string rawApiKey, bool isActive = true);
    bool DeleteKey(string keyId);
    void SetActiveKey(string keyId);
    Task<bool> TestKeyConnectionAsync(string rawApiKey, CancellationToken cancellationToken = default);

    event Action<ApiKeyVaultEntry>? OnKeyUpdated;
}

public class ApiKeyVaultService : IApiKeyVaultService
{
    private readonly ConcurrentDictionary<string, ApiKeyVaultEntry> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;

    public event Action<ApiKeyVaultEntry>? OnKeyUpdated;

    public ApiKeyVaultService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();

        // Seed with a default simulated sandbox key
        var defaultKey = new ApiKeyVaultEntry(
            KeyId: "key-circus-sandbox",
            KeyName: "Default OpenRouter Sandbox (Simulation Mode)",
            RawApiKey: "sk-or-v1-sandbox-mock-carnot-circus-0001",
            Provider: "OpenRouter",
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow
        );
        _keys[defaultKey.KeyId] = defaultKey;
    }

    public IReadOnlyList<ApiKeyVaultEntry> GetAllKeys() =>
        _keys.Values.OrderByDescending(k => k.CreatedAt).ToList();

    public ApiKeyVaultEntry? GetKey(string keyId) =>
        _keys.TryGetValue(keyId, out var entry) ? entry : null;

    public ApiKeyVaultEntry? GetActiveKey() =>
        _keys.Values.FirstOrDefault(k => k.IsActive) ?? _keys.Values.FirstOrDefault();

    public ApiKeyVaultEntry AddOrUpdateKey(string keyName, string rawApiKey, bool isActive = true)
    {
        var keyId = $"key-{Guid.NewGuid().ToString("N")[..6]}";
        
        if (isActive)
        {
            // Deactivate others
            foreach (var kvp in _keys)
            {
                _keys[kvp.Key] = kvp.Value with { IsActive = false };
            }
        }

        var entry = new ApiKeyVaultEntry(
            KeyId: keyId,
            KeyName: keyName,
            RawApiKey: rawApiKey,
            Provider: "OpenRouter",
            IsActive: isActive,
            CreatedAt: DateTimeOffset.UtcNow
        );

        _keys[keyId] = entry;
        OnKeyUpdated?.Invoke(entry);
        return entry;
    }

    public bool DeleteKey(string keyId) => _keys.TryRemove(keyId, out _);

    public void SetActiveKey(string keyId)
    {
        foreach (var kvp in _keys)
        {
            var isTarget = string.Equals(kvp.Key, keyId, StringComparison.OrdinalIgnoreCase);
            _keys[kvp.Key] = kvp.Value with { IsActive = isTarget };
            if (isTarget)
            {
                OnKeyUpdated?.Invoke(_keys[kvp.Key]);
            }
        }
    }

    public async Task<bool> TestKeyConnectionAsync(string rawApiKey, CancellationToken cancellationToken = default)
    {
        if (rawApiKey.Contains("sandbox", StringComparison.OrdinalIgnoreCase) ||
            rawApiKey.Contains("mock", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(50, cancellationToken);
            return true;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/auth/key");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rawApiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var res = await _httpClient.SendAsync(req, cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
