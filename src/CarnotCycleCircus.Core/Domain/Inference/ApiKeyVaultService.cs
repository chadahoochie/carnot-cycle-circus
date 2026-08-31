using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Security;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Inference;

public record ApiKeyVaultEntry(
    string KeyId,
    string KeyName,
    string RawApiKey,
    string Provider = "OpenRouter",
    bool IsActive = true,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? LastAccessedAt = null,
    string? EncryptionAlgorithm = "AES-256-GCM",
    int KeyVersion = 1
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

    // Cryptographic Vault Operations
    Task RotateMasterKeyAsync(string newPassphraseOrKey, CancellationToken cancellationToken = default);
    Task<string> ExportEncryptedVaultAsync(string exportPassphrase, CancellationToken cancellationToken = default);
    Task<int> ImportEncryptedVaultAsync(string encryptedJsonPackage, string importPassphrase, CancellationToken cancellationToken = default);
    VaultSecurityStatus GetSecurityStatus();

    event Action<ApiKeyVaultEntry>? OnKeyUpdated;
    event Action<VaultSecurityStatus>? OnSecurityStatusChanged;
}

public class ApiKeyVaultService : IApiKeyVaultService
{
    private readonly ConcurrentDictionary<string, ApiKeyVaultEntry> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly IPersistentStorageService? _storageService;
    private readonly ISecureKeyEncryptor _encryptor;
    private readonly JsonSerializerOptions _jsonOptions;

    public const string EncryptedStorageFileName = "keys.vault.json";
    public const string LegacyStorageFileName = "keys.json";

    private DateTimeOffset _lastSavedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _vaultCreatedAt = DateTimeOffset.UtcNow;
    private string _kdfSaltBase64 = string.Empty;

    public event Action<ApiKeyVaultEntry>? OnKeyUpdated;
    public event Action<VaultSecurityStatus>? OnSecurityStatusChanged;

    public ApiKeyVaultService(
        HttpClient? httpClient = null,
        IPersistentStorageService? storageService = null,
        ISecureKeyEncryptor? encryptor = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _storageService = storageService;
        _encryptor = encryptor ?? new AesGcmKeyEncryptor();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        // Initialize KDF salt
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        _kdfSaltBase64 = Convert.ToBase64String(salt);

        var loaded = LoadFromEncryptedStorage();
        if (!loaded)
        {
            loaded = MigrateFromLegacyStorage();
        }
    }

    private static byte[] GetAssociatedData(string keyId, string provider) =>
        Encoding.UTF8.GetBytes($"carnot:vault:v1:{keyId}:{provider}");

    private bool LoadFromEncryptedStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var doc = _storageService.LoadJsonAsync<EncryptedVaultDocument>(EncryptedStorageFileName)
                .GetAwaiter()
                .GetResult();

            if (doc != null && doc.Records.Count > 0)
            {
                _vaultCreatedAt = doc.CreatedAt;
                _lastSavedAt = doc.UpdatedAt;
                if (!string.IsNullOrEmpty(doc.KdfParameters?.SaltBase64))
                {
                    _kdfSaltBase64 = doc.KdfParameters.SaltBase64;
                }

                foreach (var record in doc.Records)
                {
                    try
                    {
                        var aad = GetAssociatedData(record.KeyId, record.Provider);
                        var rawSecret = _encryptor.Decrypt(record.EncryptedSecret, aad);

                        var entry = new ApiKeyVaultEntry(
                            KeyId: record.KeyId,
                            KeyName: record.KeyName,
                            RawApiKey: rawSecret,
                            Provider: record.Provider,
                            IsActive: record.IsActive,
                            CreatedAt: record.CreatedAt,
                            LastAccessedAt: record.LastAccessedAt,
                            EncryptionAlgorithm: record.EncryptedSecret.Algorithm,
                            KeyVersion: record.EncryptedSecret.KeyVersion
                        );

                        _keys[entry.KeyId] = entry;
                    }
                    catch
                    {
                        // Record decryption error (possible tampering or key mismatch)
                    }
                }

                return _keys.Count > 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool MigrateFromLegacyStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var saved = _storageService.LoadJsonAsync<List<ApiKeyVaultEntry>>(LegacyStorageFileName)
                .GetAwaiter()
                .GetResult();

            if (saved != null && saved.Count > 0)
            {
                foreach (var k in saved)
                {
                    var secureEntry = k with
                    {
                        EncryptionAlgorithm = _encryptor.Algorithm,
                        KeyVersion = 1,
                        LastAccessedAt = DateTimeOffset.UtcNow
                    };
                    _keys[secureEntry.KeyId] = secureEntry;
                }

                // Immediately encrypt and write to keys.vault.json
                SaveToStorage();

                // Safely remove the unencrypted legacy file
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _storageService.DeleteFileAsync(LegacyStorageFileName);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                });

                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private void SaveToStorage()
    {
        if (_storageService == null) return;

        _ = Task.Run(async () =>
        {
            await _saveLock.WaitAsync();
            try
            {
                var entries = _keys.Values.ToList();
                _lastSavedAt = DateTimeOffset.UtcNow;

                var records = new List<EncryptedVaultRecord>();
                foreach (var entry in entries)
                {
                    var aad = GetAssociatedData(entry.KeyId, entry.Provider);
                    var encrypted = _encryptor.Encrypt(entry.RawApiKey, aad);

                    records.Add(new EncryptedVaultRecord(
                        KeyId: entry.KeyId,
                        KeyName: entry.KeyName,
                        Provider: entry.Provider,
                        IsActive: entry.IsActive,
                        CreatedAt: entry.CreatedAt == default ? DateTimeOffset.UtcNow : entry.CreatedAt,
                        LastModifiedAt: DateTimeOffset.UtcNow,
                        LastAccessedAt: entry.LastAccessedAt,
                        EncryptedSecret: encrypted
                    ));
                }

                var doc = new EncryptedVaultDocument(
                    SchemaVersion: 1,
                    CreatedAt: _vaultCreatedAt,
                    UpdatedAt: _lastSavedAt,
                    KdfParameters: new VaultKdfParameters(
                        Algorithm: "PBKDF2-HMAC-SHA256",
                        Iterations: MasterKeyProvider.DefaultPbkdf2Iterations,
                        SaltBase64: _kdfSaltBase64,
                        MasterKeyFingerprint: _encryptor.MasterKeyProvider.MasterKeyFingerprint
                    ),
                    Records: records
                );

                await _storageService.SaveJsonAsync(EncryptedStorageFileName, doc);
                OnSecurityStatusChanged?.Invoke(GetSecurityStatus());
            }
            catch
            {
                // Ignore transient background write error
            }
            finally
            {
                _saveLock.Release();
            }
        });
    }

    public IReadOnlyList<ApiKeyVaultEntry> GetAllKeys() =>
        _keys.Values.OrderByDescending(k => k.CreatedAt).ToList();

    public ApiKeyVaultEntry? GetKey(string keyId)
    {
        if (_keys.TryGetValue(keyId, out var entry))
        {
            var updated = entry with { LastAccessedAt = DateTimeOffset.UtcNow };
            _keys[keyId] = updated;
            return updated;
        }
        return null;
    }

    public ApiKeyVaultEntry? GetActiveKey() =>
        _keys.Values.FirstOrDefault(k => k.IsActive) ?? _keys.Values.FirstOrDefault();

    public ApiKeyVaultEntry AddOrUpdateKey(string keyName, string rawApiKey, bool isActive = true)
    {
        var keyId = $"key-{Guid.NewGuid().ToString("N")[..6]}";

        if (isActive)
        {
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
            CreatedAt: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow,
            EncryptionAlgorithm: _encryptor.Algorithm,
            KeyVersion: 1
        );

        _keys[keyId] = entry;
        OnKeyUpdated?.Invoke(entry);
        SaveToStorage();
        return entry;
    }

    public bool DeleteKey(string keyId)
    {
        var removed = _keys.TryRemove(keyId, out _);
        if (removed)
        {
            SaveToStorage();
        }
        return removed;
    }

    public void SetActiveKey(string keyId)
    {
        foreach (var kvp in _keys)
        {
            var isTarget = string.Equals(kvp.Key, keyId, StringComparison.OrdinalIgnoreCase);
            _keys[kvp.Key] = kvp.Value with { IsActive = isTarget, LastAccessedAt = isTarget ? DateTimeOffset.UtcNow : kvp.Value.LastAccessedAt };
            if (isTarget)
            {
                OnKeyUpdated?.Invoke(_keys[kvp.Key]);
            }
        }
        SaveToStorage();
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
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var res = await _httpClient.SendAsync(req, cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task RotateMasterKeyAsync(string newPassphraseOrKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassphraseOrKey))
        {
            throw new ArgumentException("New passphrase or key cannot be empty.", nameof(newPassphraseOrKey));
        }

        var newKeyBytes = new byte[MasterKeyProvider.KeySizeBytes];
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        _kdfSaltBase64 = Convert.ToBase64String(salt);

        try
        {
            MasterKeyProvider.DeriveKeyFromPassphrase(newPassphraseOrKey, salt, newKeyBytes, MasterKeyProvider.DefaultPbkdf2Iterations);

            // Update provider with new key
            _encryptor.MasterKeyProvider.SetMasterKey(newKeyBytes, "CustomPassphrase");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newKeyBytes);
        }

        // Save re-encrypts all stored records under the new master key
        if (_storageService != null)
        {
            var entries = _keys.Values.ToList();
            _lastSavedAt = DateTimeOffset.UtcNow;

            var records = new List<EncryptedVaultRecord>();
            foreach (var entry in entries)
            {
                var aad = GetAssociatedData(entry.KeyId, entry.Provider);
                var encrypted = _encryptor.Encrypt(entry.RawApiKey, aad);

                records.Add(new EncryptedVaultRecord(
                    KeyId: entry.KeyId,
                    KeyName: entry.KeyName,
                    Provider: entry.Provider,
                    IsActive: entry.IsActive,
                    CreatedAt: entry.CreatedAt == default ? DateTimeOffset.UtcNow : entry.CreatedAt,
                    LastModifiedAt: DateTimeOffset.UtcNow,
                    LastAccessedAt: entry.LastAccessedAt,
                    EncryptedSecret: encrypted
                ));
            }

            var doc = new EncryptedVaultDocument(
                SchemaVersion: 1,
                CreatedAt: _vaultCreatedAt,
                UpdatedAt: _lastSavedAt,
                KdfParameters: new VaultKdfParameters(
                    Algorithm: "PBKDF2-HMAC-SHA256",
                    Iterations: MasterKeyProvider.DefaultPbkdf2Iterations,
                    SaltBase64: _kdfSaltBase64,
                    MasterKeyFingerprint: _encryptor.MasterKeyProvider.MasterKeyFingerprint
                ),
                Records: records
            );

            await _storageService.SaveJsonAsync(EncryptedStorageFileName, doc, cancellationToken);
        }

        OnSecurityStatusChanged?.Invoke(GetSecurityStatus());
    }

    public Task<string> ExportEncryptedVaultAsync(string exportPassphrase, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(exportPassphrase))
        {
            throw new ArgumentException("Export passphrase cannot be empty.", nameof(exportPassphrase));
        }

        var exportSalt = new byte[16];
        RandomNumberGenerator.Fill(exportSalt);
        var exportSaltBase64 = Convert.ToBase64String(exportSalt);

        var exportKey = new byte[MasterKeyProvider.KeySizeBytes];
        MasterKeyProvider.DeriveKeyFromPassphrase(exportPassphrase, exportSalt, exportKey, MasterKeyProvider.DefaultPbkdf2Iterations);

        try
        {
            var keysList = _keys.Values.ToList();
            var serialized = JsonSerializer.Serialize(keysList, _jsonOptions);
            var aad = Encoding.UTF8.GetBytes("carnot:vault:export:v1");

            var payload = _encryptor.Encrypt(serialized, aad, exportKey);

            var package = new VaultExportPackage(
                PackageVersion: 1,
                ExportedAt: DateTimeOffset.UtcNow,
                KeyCount: keysList.Count,
                KdfSaltBase64: exportSaltBase64,
                KdfIterations: MasterKeyProvider.DefaultPbkdf2Iterations,
                Payload: payload
            );

            var exportJson = JsonSerializer.Serialize(package, _jsonOptions);
            return Task.FromResult(exportJson);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportKey);
        }
    }

    public Task<int> ImportEncryptedVaultAsync(string encryptedJsonPackage, string importPassphrase, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(encryptedJsonPackage))
        {
            throw new ArgumentException("Import package JSON cannot be empty.", nameof(encryptedJsonPackage));
        }
        if (string.IsNullOrWhiteSpace(importPassphrase))
        {
            throw new ArgumentException("Import passphrase cannot be empty.", nameof(importPassphrase));
        }

        var package = JsonSerializer.Deserialize<VaultExportPackage>(encryptedJsonPackage, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to parse vault export package format.");

        var salt = Convert.FromBase64String(package.KdfSaltBase64);
        var importKey = new byte[MasterKeyProvider.KeySizeBytes];
        MasterKeyProvider.DeriveKeyFromPassphrase(importPassphrase, salt, importKey, package.KdfIterations);

        try
        {
            var aad = Encoding.UTF8.GetBytes("carnot:vault:export:v1");
            var decryptedJson = _encryptor.Decrypt(package.Payload, aad, importKey);

            var importedKeys = JsonSerializer.Deserialize<List<ApiKeyVaultEntry>>(decryptedJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse decrypted keys.");

            int importedCount = 0;
            foreach (var key in importedKeys)
            {
                var entry = key with
                {
                    EncryptionAlgorithm = _encryptor.Algorithm,
                    KeyVersion = 1,
                    LastAccessedAt = DateTimeOffset.UtcNow
                };
                _keys[entry.KeyId] = entry;
                importedCount++;
            }

            SaveToStorage();
            OnSecurityStatusChanged?.Invoke(GetSecurityStatus());

            return Task.FromResult(importedCount);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(importKey);
        }
    }

    public VaultSecurityStatus GetSecurityStatus() =>
        new(
            IsEncryptedAtRest: _storageService != null,
            EncryptionAlgorithm: _encryptor.Algorithm,
            KeyDerivationAlgorithm: $"PBKDF2-HMAC-SHA256 ({MasterKeyProvider.DefaultPbkdf2Iterations:N0} iterations)",
            MasterKeySource: _encryptor.MasterKeyProvider.MasterKeySource,
            MasterKeyFingerprint: _encryptor.MasterKeyProvider.MasterKeyFingerprint,
            TotalKeysCount: _keys.Count,
            ActiveKeyId: GetActiveKey()?.KeyId,
            LastSavedAt: _lastSavedAt
        );
}
