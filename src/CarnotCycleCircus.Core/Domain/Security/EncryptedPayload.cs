namespace CarnotCycleCircus.Core.Domain.Security;

/// <summary>
/// Represents an authenticated AES-256-GCM encrypted ciphertext payload with its nonce and authentication tag.
/// </summary>
public readonly record struct EncryptedPayload(
    string CiphertextBase64,
    string NonceBase64,
    string TagBase64,
    string Algorithm = "AES-256-GCM",
    int KeyVersion = 1
)
{
    public bool IsEmpty => string.IsNullOrEmpty(CiphertextBase64);

    public static EncryptedPayload FromBytes(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> tag,
        string algorithm = "AES-256-GCM",
        int keyVersion = 1) =>
        new(
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            algorithm,
            keyVersion
        );
}

/// <summary>
/// Key derivation function parameters recorded in persistent vault documents.
/// </summary>
public record VaultKdfParameters(
    string Algorithm,
    int Iterations,
    string SaltBase64,
    string MasterKeyFingerprint
);

/// <summary>
/// Single encrypted key vault record stored in the persistent encrypted vault document.
/// </summary>
public record EncryptedVaultRecord(
    string KeyId,
    string KeyName,
    string Provider,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    DateTimeOffset? LastAccessedAt,
    EncryptedPayload EncryptedSecret,
    IReadOnlyDictionary<string, string>? Tags = null
);

/// <summary>
/// Root document persisted to keys.vault.json containing encrypted credentials and envelope metadata.
/// </summary>
public record EncryptedVaultDocument(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    VaultKdfParameters KdfParameters,
    IReadOnlyList<EncryptedVaultRecord> Records
);

/// <summary>
/// Standalone portable encrypted backup package protected by a user-supplied export passphrase.
/// </summary>
public record VaultExportPackage(
    int PackageVersion,
    DateTimeOffset ExportedAt,
    int KeyCount,
    string KdfSaltBase64,
    int KdfIterations,
    EncryptedPayload Payload
);

/// <summary>
/// Comprehensive health and security audit status for the API Key Vault.
/// </summary>
public record VaultSecurityStatus(
    bool IsEncryptedAtRest,
    string EncryptionAlgorithm,
    string KeyDerivationAlgorithm,
    string MasterKeySource,
    string MasterKeyFingerprint,
    int TotalKeysCount,
    string? ActiveKeyId,
    DateTimeOffset LastSavedAt
);
