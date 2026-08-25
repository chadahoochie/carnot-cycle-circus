using System.Security.Cryptography;
using System.Text;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Security;

/// <summary>
/// Provider responsible for managing and deriving 256-bit symmetric master encryption keys.
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>
    /// Gets a copy of the active 256-bit (32 bytes) master encryption key.
    /// </summary>
    byte[] GetMasterKey();

    /// <summary>
    /// Fills the destination span with the active 256-bit master key.
    /// </summary>
    void GetMasterKeyBytes(Span<byte> destination);

    /// <summary>
    /// Source origin of the master key (e.g. EnvironmentVariable, HostKeyFile, CustomPassphrase, EphemeralMemory).
    /// </summary>
    string MasterKeySource { get; }

    /// <summary>
    /// SHA-256 fingerprint of the current master key for verification without secret leakage.
    /// </summary>
    string MasterKeyFingerprint { get; }

    /// <summary>
    /// Sets a new active master encryption key.
    /// </summary>
    void SetMasterKey(ReadOnlySpan<byte> newKey, string source = "CustomPassphrase");

    /// <summary>
    /// Derives and sets a new active master encryption key from a user or admin passphrase.
    /// </summary>
    void SetMasterKeyFromPassphrase(ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> salt = default, int iterations = 310_000);
}

/// <summary>
/// Production implementation of IMasterKeyProvider with environment, host-bound keyfile, and passphrase derivation.
/// </summary>
public class MasterKeyProvider : IMasterKeyProvider
{
    public const int KeySizeBytes = 32; // 256 bits
    public const int DefaultPbkdf2Iterations = 310_000;
    public const string DefaultKeyFileName = ".carnot.master.key";

    private readonly byte[] _masterKey = new byte[KeySizeBytes];
    private readonly object _syncRoot = new();
    private string _source = "EphemeralMemory";
    private string _fingerprint = string.Empty;

    public string MasterKeySource
    {
        get { lock (_syncRoot) return _source; }
    }

    public string MasterKeyFingerprint
    {
        get { lock (_syncRoot) return _fingerprint; }
    }

    public MasterKeyProvider(CarnotStorageOptions? storageOptions = null)
    {
        InitializeKey(storageOptions);
    }

    private void InitializeKey(CarnotStorageOptions? storageOptions)
    {
        // 1. Check environment variable
        var envKey = Environment.GetEnvironmentVariable("CARNOT_VAULT_MASTER_KEY")
                     ?? Environment.GetEnvironmentVariable("CARNOT_MASTER_KEY")
                     ?? Environment.GetEnvironmentVariable("OPENROUTER_MASTER_KEY");

        if (!string.IsNullOrWhiteSpace(envKey))
        {
            var raw = envKey.Trim();
            if (raw.Length == 64 && IsHexString(raw))
            {
                var hexBytes = Convert.FromHexString(raw);
                SetKeyInternal(hexBytes, "EnvironmentVariable");
                return;
            }

            if (raw.Length == 44 && TryParseBase64Key(raw, out var base64Bytes))
            {
                SetKeyInternal(base64Bytes, "EnvironmentVariable");
                return;
            }

            // Derive key from passphrase in environment variable
            Span<byte> derived = stackalloc byte[KeySizeBytes];
            Span<byte> defaultSalt = stackalloc byte[16];
            Encoding.UTF8.GetBytes("CarnotCycleSalt!").CopyTo(defaultSalt);
            DeriveKeyFromPassphrase(raw, defaultSalt, derived, DefaultPbkdf2Iterations);
            SetKeyInternal(derived, "EnvironmentVariable");
            return;
        }

        // 2. Check persistent storage directory for host keyfile
        if (storageOptions != null && !string.IsNullOrWhiteSpace(storageOptions.DataDirectory))
        {
            try
            {
                Directory.CreateDirectory(storageOptions.DataDirectory);
                var keyFilePath = Path.Combine(storageOptions.DataDirectory, DefaultKeyFileName);

                if (File.Exists(keyFilePath))
                {
                    var fileBytes = File.ReadAllBytes(keyFilePath);
                    if (fileBytes.Length >= KeySizeBytes)
                    {
                        SetKeyInternal(fileBytes.AsSpan(0, KeySizeBytes), "HostKeyFile");
                        return;
                    }
                }

                // Generate new persistent host key
                Span<byte> newHostKey = stackalloc byte[KeySizeBytes];
                RandomNumberGenerator.Fill(newHostKey);

                File.WriteAllBytes(keyFilePath, newHostKey.ToArray());

                // Restrict Unix file permissions to owner read/write (0600)
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    try
                    {
                        File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                    catch
                    {
                        // Ignore if filesystem does not support unix permissions
                    }
                }

                SetKeyInternal(newHostKey, "HostKeyFile");
                return;
            }
            catch
            {
                // Fall back to ephemeral in-memory key on disk/permission failure
            }
        }

        // 3. Ephemeral in-memory key fallback
        Span<byte> ephemeral = stackalloc byte[KeySizeBytes];
        RandomNumberGenerator.Fill(ephemeral);
        SetKeyInternal(ephemeral, "EphemeralMemory");
    }

    private void SetKeyInternal(ReadOnlySpan<byte> keySpan, string source)
    {
        lock (_syncRoot)
        {
            if (keySpan.Length < KeySizeBytes)
            {
                throw new ArgumentException($"Master key must be at least {KeySizeBytes} bytes (256 bits).", nameof(keySpan));
            }

            keySpan[..KeySizeBytes].CopyTo(_masterKey);
            _source = source;
            _fingerprint = ComputeFingerprint(_masterKey);
        }
    }

    public byte[] GetMasterKey()
    {
        lock (_syncRoot)
        {
            var copy = new byte[KeySizeBytes];
            _masterKey.AsSpan().CopyTo(copy);
            return copy;
        }
    }

    public void GetMasterKeyBytes(Span<byte> destination)
    {
        lock (_syncRoot)
        {
            if (destination.Length < KeySizeBytes)
            {
                throw new ArgumentException($"Destination buffer must be at least {KeySizeBytes} bytes.", nameof(destination));
            }
            _masterKey.AsSpan().CopyTo(destination);
        }
    }

    public void SetMasterKey(ReadOnlySpan<byte> newKey, string source = "CustomPassphrase")
    {
        SetKeyInternal(newKey, source);
    }

    public void SetMasterKeyFromPassphrase(ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> salt = default, int iterations = DefaultPbkdf2Iterations)
    {
        Span<byte> derivedKey = stackalloc byte[KeySizeBytes];
        Span<byte> effectiveSalt = stackalloc byte[16];

        if (salt.Length >= 16)
        {
            salt[..16].CopyTo(effectiveSalt);
        }
        else
        {
            Encoding.UTF8.GetBytes("CarnotCycleSalt!").CopyTo(effectiveSalt);
        }

        DeriveKeyFromPassphrase(passphrase, effectiveSalt, derivedKey, iterations);
        SetKeyInternal(derivedKey, "CustomPassphrase");
        CryptographicOperations.ZeroMemory(derivedKey);
    }

    public static void DeriveKeyFromPassphrase(
        ReadOnlySpan<char> passphrase,
        ReadOnlySpan<byte> salt,
        Span<byte> destinationKey,
        int iterations = DefaultPbkdf2Iterations)
    {
        if (destinationKey.Length < KeySizeBytes)
        {
            throw new ArgumentException($"Destination buffer must be at least {KeySizeBytes} bytes.", nameof(destinationKey));
        }

        Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            destinationKey[..KeySizeBytes],
            iterations,
            HashAlgorithmName.SHA256
        );
    }

    public static string ComputeFingerprint(ReadOnlySpan<byte> key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(key, hash);
        return $"sha256:{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static bool IsHexString(ReadOnlySpan<char> str)
    {
        foreach (var c in str)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }
        return true;
    }

    private static bool TryParseBase64Key(string input, out byte[] bytes)
    {
        try
        {
            var decoded = Convert.FromBase64String(input);
            if (decoded.Length == KeySizeBytes)
            {
                bytes = decoded;
                return true;
            }
        }
        catch
        {
            // Not valid base64
        }
        bytes = Array.Empty<byte>();
        return false;
    }
}
