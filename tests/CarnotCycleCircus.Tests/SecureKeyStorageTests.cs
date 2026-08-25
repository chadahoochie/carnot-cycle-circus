using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Security;
using CarnotCycleCircus.Core.Domain.Storage;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class SecureKeyStorageTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly CarnotStorageOptions _storageOptions;
    private readonly IPersistentStorageService _storageService;

    public SecureKeyStorageTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"carnot_security_test_{Guid.NewGuid():N}");
        _storageOptions = new CarnotStorageOptions
        {
            DataDirectory = _testTempDir,
            EnableAtomicWrites = true
        };
        _storageService = new FilePersistentStorageService(_storageOptions);
    }

    [Fact]
    public void AesGcmEncryptor_EncryptAndDecrypt_ShouldRestoreExactPlaintext()
    {
        // Arrange
        var keyProvider = new MasterKeyProvider();
        var encryptor = new AesGcmKeyEncryptor(keyProvider);
        var plainSecret = "sk-or-v1-supersecretkey998877665544332211";
        var aad = Encoding.UTF8.GetBytes("test-context-aad");

        // Act
        var payload = encryptor.Encrypt(plainSecret, aad);
        var decrypted = encryptor.Decrypt(payload, aad);

        // Assert
        payload.Algorithm.Should().Be("AES-256-GCM");
        payload.CiphertextBase64.Should().NotBeNullOrEmpty();
        payload.NonceBase64.Should().NotBeNullOrEmpty();
        payload.TagBase64.Should().NotBeNullOrEmpty();
        decrypted.Should().Be(plainSecret);
    }

    [Fact]
    public void AesGcmEncryptor_TryDecrypt_ShouldFillSpanBufferWithoutAllocatingString()
    {
        // Arrange
        var encryptor = new AesGcmKeyEncryptor();
        var plainSecret = "sk-or-v1-zeroallocsecret";
        var payload = encryptor.Encrypt(plainSecret);

        // Act
        Span<char> buffer = stackalloc char[128];
        var success = encryptor.TryDecrypt(payload, buffer, out var charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().Be(plainSecret.Length);
        buffer[..charsWritten].ToString().Should().Be(plainSecret);
    }

    [Fact]
    public void AesGcmEncryptor_WithTamperedCiphertext_ShouldThrowCryptographicException()
    {
        // Arrange
        var encryptor = new AesGcmKeyEncryptor();
        var plainSecret = "sk-or-v1-tampertestsecret";
        var payload = encryptor.Encrypt(plainSecret);

        // Tamper with ciphertext bytes
        var rawCipher = Convert.FromBase64String(payload.CiphertextBase64);
        rawCipher[0] ^= 0xFF; // Flip bits
        var tamperedPayload = payload with { CiphertextBase64 = Convert.ToBase64String(rawCipher) };

        // Act & Assert
        var act = () => encryptor.Decrypt(tamperedPayload);
        act.Should().Throw<CryptographicException>()
            .WithMessage("*Authentication tag verification failed*");
    }

    [Fact]
    public void AesGcmEncryptor_WithTamperedAuthTag_ShouldThrowCryptographicException()
    {
        // Arrange
        var encryptor = new AesGcmKeyEncryptor();
        var plainSecret = "sk-or-v1-tagtampertestsecret";
        var payload = encryptor.Encrypt(plainSecret);

        // Tamper with auth tag
        var rawTag = Convert.FromBase64String(payload.TagBase64);
        rawTag[0] ^= 0xAA;
        var tamperedPayload = payload with { TagBase64 = Convert.ToBase64String(rawTag) };

        // Act & Assert
        var act = () => encryptor.Decrypt(tamperedPayload);
        act.Should().Throw<CryptographicException>()
            .WithMessage("*Authentication tag verification failed*");
    }

    [Fact]
    public void AesGcmEncryptor_WithMismatchedAssociatedData_ShouldThrowCryptographicException()
    {
        // Arrange
        var encryptor = new AesGcmKeyEncryptor();
        var plainSecret = "sk-or-v1-aadmismatchtest";
        var aadExpected = Encoding.UTF8.GetBytes("key-001:OpenRouter");
        var aadWrong = Encoding.UTF8.GetBytes("key-002:OpenRouter");

        var payload = encryptor.Encrypt(plainSecret, aadExpected);

        // Act & Assert
        var act = () => encryptor.Decrypt(payload, aadWrong);
        act.Should().Throw<CryptographicException>()
            .WithMessage("*Authentication tag verification failed*");
    }

    [Fact]
    public void MasterKeyProvider_DeriveFromPassphrase_ShouldProduceDeterministicKey()
    {
        // Arrange
        var salt = Encoding.UTF8.GetBytes("1234567812345678");
        Span<byte> key1 = stackalloc byte[32];
        Span<byte> key2 = stackalloc byte[32];

        // Act
        MasterKeyProvider.DeriveKeyFromPassphrase("SuperSecurePassword123!", salt, key1, 10_000);
        MasterKeyProvider.DeriveKeyFromPassphrase("SuperSecurePassword123!", salt, key2, 10_000);

        // Assert
        key1.SequenceEqual(key2).Should().BeTrue();
        MasterKeyProvider.ComputeFingerprint(key1).Should().StartWith("sha256:");
    }

    [Fact]
    public async Task ApiKeyVault_WithStorage_ShouldPersistEncryptedDocumentAtRest()
    {
        // Arrange
        var masterKeyProvider = new MasterKeyProvider(_storageOptions);
        var encryptor = new AesGcmKeyEncryptor(masterKeyProvider);
        var vault1 = new ApiKeyVaultService(null, _storageService, encryptor);

        // Act
        var key = vault1.AddOrUpdateKey("Production Claude 3.7 Key", "sk-or-v1-topsecretproductionkey123", isActive: true);

        // Allow async file flush
        await Task.Delay(150);

        // Assert: Check that raw text is NOT present in storage file
        var fileContent = await _storageService.LoadTextAsync(ApiKeyVaultService.EncryptedStorageFileName);
        fileContent.Should().NotBeNullOrEmpty();
        fileContent.Should().NotContain("sk-or-v1-topsecretproductionkey123");
        fileContent.Should().Contain("Production Claude 3.7 Key");
        fileContent.Should().Contain("AES-256-GCM");
        fileContent.Should().Contain("CiphertextBase64");

        // Assert: Instance 2 with same key provider can decrypt and retrieve
        var vault2 = new ApiKeyVaultService(null, _storageService, encryptor);
        var loadedKey = vault2.GetKey(key.KeyId);
        loadedKey.Should().NotBeNull();
        loadedKey!.RawApiKey.Should().Be("sk-or-v1-topsecretproductionkey123");
        loadedKey.ApiKeyMasked.Should().Be("sk-o...y123");
        loadedKey.EncryptionAlgorithm.Should().Be("AES-256-GCM");
    }

    [Fact]
    public async Task ApiKeyVault_RotateMasterKey_ShouldReEncryptAllKeysUnderNewKey()
    {
        // Arrange
        var masterKeyProvider = new MasterKeyProvider(_storageOptions);
        var encryptor = new AesGcmKeyEncryptor(masterKeyProvider);
        var vault = new ApiKeyVaultService(null, _storageService, encryptor);

        var key1 = vault.AddOrUpdateKey("Key Alpha", "sk-or-v1-alpha1111111111", isActive: true);
        var key2 = vault.AddOrUpdateKey("Key Beta", "sk-or-v1-beta2222222222", isActive: false);

        await Task.Delay(100);
        var initialFingerprint = vault.GetSecurityStatus().MasterKeyFingerprint;

        // Act: Rotate to a new passphrase
        await vault.RotateMasterKeyAsync("BrandNewMasterPassphrase2026!");

        var updatedStatus = vault.GetSecurityStatus();
        updatedStatus.MasterKeyFingerprint.Should().NotBe(initialFingerprint);

        // Verify keys remain decrypted and valid in memory
        vault.GetKey(key1.KeyId)!.RawApiKey.Should().Be("sk-or-v1-alpha1111111111");
        vault.GetKey(key2.KeyId)!.RawApiKey.Should().Be("sk-or-v1-beta2222222222");

        // Verify instance 3 using new master key can load persisted vault
        var vaultNew = new ApiKeyVaultService(null, _storageService, encryptor);
        var loadedKey1 = vaultNew.GetKey(key1.KeyId);
        loadedKey1.Should().NotBeNull();
        loadedKey1!.RawApiKey.Should().Be("sk-or-v1-alpha1111111111");
    }

    [Fact]
    public async Task ApiKeyVault_LegacyMigration_ShouldMigratePlaintextKeysAndRemoveLegacyFile()
    {
        // Arrange: Write a legacy unencrypted keys.json file
        var legacyEntries = new List<ApiKeyVaultEntry>
        {
            new("key-legacy-1", "Legacy TPM Key", "sk-or-v1-legacyplaintextsecret1", "OpenRouter", true, DateTimeOffset.UtcNow),
            new("key-legacy-2", "Legacy Architect Key", "sk-or-v1-legacyplaintextsecret2", "OpenRouter", false, DateTimeOffset.UtcNow)
        };
        await _storageService.SaveJsonAsync(ApiKeyVaultService.LegacyStorageFileName, legacyEntries);

        // Act: Initialize vault service, which should trigger automatic migration
        var vault = new ApiKeyVaultService(null, _storageService);

        // Allow async flush and legacy cleanup
        await Task.Delay(200);

        // Assert: Keys loaded into vault
        var key1 = vault.GetKey("key-legacy-1");
        key1.Should().NotBeNull();
        key1!.RawApiKey.Should().Be("sk-or-v1-legacyplaintextsecret1");
        key1.EncryptionAlgorithm.Should().Be("AES-256-GCM");

        // Assert: Encrypted vault file exists
        var encryptedExists = await _storageService.FileExistsAsync(ApiKeyVaultService.EncryptedStorageFileName);
        encryptedExists.Should().BeTrue();

        // Assert: Plaintext file removed
        var legacyExists = await _storageService.FileExistsAsync(ApiKeyVaultService.LegacyStorageFileName);
        legacyExists.Should().BeFalse();
    }

    [Fact]
    public async Task ApiKeyVault_ExportAndImport_ShouldRoundtripWithPassphrase()
    {
        // Arrange
        var vaultSource = new ApiKeyVaultService(null, _storageService);
        vaultSource.AddOrUpdateKey("Export Key 1", "sk-or-v1-exportablekey1111", isActive: true);
        vaultSource.AddOrUpdateKey("Export Key 2", "sk-or-v1-exportablekey2222", isActive: false);

        var exportPassphrase = "ExportBackupPassphrase123!";

        // Act: Export package
        var encryptedPackageJson = await vaultSource.ExportEncryptedVaultAsync(exportPassphrase);

        encryptedPackageJson.Should().NotBeNullOrEmpty();
        encryptedPackageJson.Should().NotContain("sk-or-v1-exportablekey1111");
        encryptedPackageJson.Should().Contain("PackageVersion");
        encryptedPackageJson.Should().Contain("KdfSaltBase64");

        // Import into a fresh isolated vault instance
        var freshTempDir = Path.Combine(Path.GetTempPath(), $"carnot_import_test_{Guid.NewGuid():N}");
        var freshStorage = new FilePersistentStorageService(new CarnotStorageOptions { DataDirectory = freshTempDir });
        var vaultDestination = new ApiKeyVaultService(null, freshStorage);

        var importedCount = await vaultDestination.ImportEncryptedVaultAsync(encryptedPackageJson, exportPassphrase);

        // Assert
        importedCount.Should().BeGreaterThanOrEqualTo(2);
        var allKeys = vaultDestination.GetAllKeys();
        allKeys.Should().Contain(k => k.RawApiKey == "sk-or-v1-exportablekey1111");
        allKeys.Should().Contain(k => k.RawApiKey == "sk-or-v1-exportablekey2222");

        // Test wrong passphrase fails
        var wrongPassAct = () => vaultDestination.ImportEncryptedVaultAsync(encryptedPackageJson, "WrongPassphrase123!");
        await wrongPassAct.Should().ThrowAsync<CryptographicException>();

        try { Directory.Delete(freshTempDir, true); } catch { }
    }

    [Fact]
    public void ApiKeyVault_SecurityStatus_ShouldReportAccurateEnvelopeMetrics()
    {
        // Arrange
        var vault = new ApiKeyVaultService(null, _storageService);

        // Act
        var status = vault.GetSecurityStatus();

        // Assert
        status.IsEncryptedAtRest.Should().BeTrue();
        status.EncryptionAlgorithm.Should().Be("AES-256-GCM");
        status.KeyDerivationAlgorithm.Should().Contain("PBKDF2-HMAC-SHA256");
        status.MasterKeyFingerprint.Should().StartWith("sha256:");
        status.TotalKeysCount.Should().BeGreaterThan(0);
        status.ActiveKeyId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ApiKeyVault_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var vault = new ApiKeyVaultService(null, _storageService);

        // Act: Parallel writes and reads
        Parallel.For(0, 50, i =>
        {
            var keyName = $"Concurrent Key {i}";
            var rawKey = $"sk-or-v1-concurrent-{i:D4}-key";
            var entry = vault.AddOrUpdateKey(keyName, rawKey, isActive: i % 10 == 0);
            var retrieved = vault.GetKey(entry.KeyId);
            retrieved.Should().NotBeNull();
            retrieved!.RawApiKey.Should().Be(rawKey);
        });

        // Assert
        vault.GetAllKeys().Count.Should().BeGreaterThanOrEqualTo(50);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }
}
