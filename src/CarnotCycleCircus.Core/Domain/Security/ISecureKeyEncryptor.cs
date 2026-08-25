using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace CarnotCycleCircus.Core.Domain.Security;

/// <summary>
/// Cryptographic service providing authenticated symmetric envelope encryption (AES-256-GCM).
/// </summary>
public interface ISecureKeyEncryptor
{
    /// <summary>
    /// Active master key provider used by this encryptor.
    /// </summary>
    IMasterKeyProvider MasterKeyProvider { get; }

    /// <summary>
    /// Canonical algorithm identifier (e.g. "AES-256-GCM").
    /// </summary>
    string Algorithm { get; }

    /// <summary>
    /// Encrypts a plaintext secret string using AES-256-GCM with optional associated data binding.
    /// </summary>
    EncryptedPayload Encrypt(ReadOnlySpan<char> plainSecret, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null);

    /// <summary>
    /// Decrypts an authenticated payload and returns the original plaintext secret string.
    /// </summary>
    string Decrypt(in EncryptedPayload payload, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null);

    /// <summary>
    /// Attempts to decrypt an authenticated payload directly into a character span buffer without extra string allocations.
    /// </summary>
    bool TryDecrypt(in EncryptedPayload payload, Span<char> destination, out int charsWritten, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null);

    /// <summary>
    /// Encrypts raw plaintext byte data using AES-256-GCM.
    /// </summary>
    EncryptedPayload EncryptBytes(ReadOnlySpan<byte> plaintextBytes, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null);

    /// <summary>
    /// Decrypts raw authenticated ciphertext bytes using AES-256-GCM.
    /// </summary>
    byte[] DecryptBytes(in EncryptedPayload payload, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null);
}

/// <summary>
/// Hardware-accelerated AES-256-GCM authenticated AEAD encryptor.
/// </summary>
public class AesGcmKeyEncryptor : ISecureKeyEncryptor
{
    public const int NonceSizeBytes = 12; // 96 bits
    public const int TagSizeBytes = 16;   // 128 bits
    public const int KeySizeBytes = 32;   // 256 bits

    private readonly IMasterKeyProvider _masterKeyProvider;

    public IMasterKeyProvider MasterKeyProvider => _masterKeyProvider;
    public string Algorithm => "AES-256-GCM";

    public AesGcmKeyEncryptor(IMasterKeyProvider? masterKeyProvider = null)
    {
        _masterKeyProvider = masterKeyProvider ?? new MasterKeyProvider();
    }

    public EncryptedPayload Encrypt(ReadOnlySpan<char> plainSecret, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(plainSecret.Length);
        byte[]? rentedBytes = null;
        Span<byte> utf8Buffer = maxByteCount <= 512
            ? stackalloc byte[maxByteCount]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount));

        try
        {
            var actualBytes = Encoding.UTF8.GetBytes(plainSecret, utf8Buffer);
            var plaintextSpan = utf8Buffer[..actualBytes];
            return EncryptBytes(plaintextSpan, associatedData, overrideKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8Buffer);
            if (rentedBytes != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }

    public string Decrypt(in EncryptedPayload payload, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null)
    {
        var decryptedBytes = DecryptBytes(payload, associatedData, overrideKey);
        try
        {
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    public bool TryDecrypt(
        in EncryptedPayload payload,
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<byte> associatedData = default,
        byte[]? overrideKey = null)
    {
        charsWritten = 0;
        var decryptedBytes = DecryptBytes(payload, associatedData, overrideKey);
        try
        {
            var charCount = Encoding.UTF8.GetCharCount(decryptedBytes);
            if (destination.Length < charCount)
            {
                return false;
            }

            charsWritten = Encoding.UTF8.GetChars(decryptedBytes, destination);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    public EncryptedPayload EncryptBytes(ReadOnlySpan<byte> plaintextBytes, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null)
    {
        Span<byte> keySpan = stackalloc byte[KeySizeBytes];
        if (overrideKey != null && overrideKey.Length >= KeySizeBytes)
        {
            overrideKey.AsSpan(0, KeySizeBytes).CopyTo(keySpan);
        }
        else
        {
            _masterKeyProvider.GetMasterKeyBytes(keySpan);
        }

        using var aesGcm = new AesGcm(keySpan, TagSizeBytes);

        Span<byte> nonce = stackalloc byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        Span<byte> tag = stackalloc byte[TagSizeBytes];

        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);

        return EncryptedPayload.FromBytes(ciphertext, nonce, tag, Algorithm, 1);
    }

    public byte[] DecryptBytes(in EncryptedPayload payload, ReadOnlySpan<byte> associatedData = default, byte[]? overrideKey = null)
    {
        if (string.IsNullOrEmpty(payload.CiphertextBase64))
        {
            return Array.Empty<byte>();
        }

        var ciphertext = Convert.FromBase64String(payload.CiphertextBase64);
        var nonce = Convert.FromBase64String(payload.NonceBase64);
        var tag = Convert.FromBase64String(payload.TagBase64);

        if (nonce.Length != NonceSizeBytes)
        {
            throw new CryptographicException($"Invalid AES-GCM nonce size: {nonce.Length} bytes (expected {NonceSizeBytes}).");
        }

        if (tag.Length != TagSizeBytes)
        {
            throw new CryptographicException($"Invalid AES-GCM tag size: {tag.Length} bytes (expected {TagSizeBytes}).");
        }

        Span<byte> keySpan = stackalloc byte[KeySizeBytes];
        if (overrideKey != null && overrideKey.Length >= KeySizeBytes)
        {
            overrideKey.AsSpan(0, KeySizeBytes).CopyTo(keySpan);
        }
        else
        {
            _masterKeyProvider.GetMasterKeyBytes(keySpan);
        }

        using var aesGcm = new AesGcm(keySpan, TagSizeBytes);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException("AES-GCM decryption failed. Authentication tag verification failed or ciphertext was tampered with.", ex);
        }
    }
}
