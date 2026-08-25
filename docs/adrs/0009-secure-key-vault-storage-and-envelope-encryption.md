# ADR-0009: Secure Key Storage, AEAD Envelope Encryption, and Master Key Derivation

## Status
**Accepted** (2026-08-22)

## Context
The Carnot Cycle Circus platform orchestrates multi-agent engineering workflows requiring diverse LLM provider credentials (e.g., OpenRouter, OpenAI, Anthropic). These credentials present high-value targets for exfiltration. Previous iterations persisted key definitions in unencrypted JSON documents on disk (`keys.json`), exposing raw tokens to:
1. **Host & Volume Exfiltration**: Unauthorized access to persistent volumes, container file mounts, or disk snapshots exposing cleartext credentials.
2. **Ciphertext Transplantation & Tampering**: Modification or permutation of serialized records without cryptographic integrity verification.
3. **Static Credential Exposure**: Inability to rotate the master encryption key across active key sets or safely export encrypted backups for disaster recovery.

## Decision
We implement a cryptographically hardened **Secure Key Vault & Envelope Encryption Engine** (`CarnotCycleCircus.Core.Domain.Security` and `CarnotCycleCircus.Core.Domain.Inference`):

1. **Authenticated Encryption (AES-256-GCM AEAD)**:
   - All secret material at rest is encrypted using hardware-accelerated **AES-256-GCM** (NIST SP 800-38D).
   - Each encryption operation generates a unique 96-bit (12-byte) cryptographically secure nonce and produces a 128-bit (16-byte) authentication tag.
   - Associated Authenticated Data (AAD) binds the ciphertext to the domain context (`carnot:vault:v1:{KeyId}:{Provider}`), preventing key-swapping and transplant attacks.

2. **Master Key Provider Hierarchy (`IMasterKeyProvider`)**:
   Master encryption keys (256-bit) resolve across priority tiers:
   - **Tier 1 (Environment Override)**: `CARNOT_VAULT_MASTER_KEY` / `CARNOT_MASTER_KEY` supporting raw 64-character hex, 44-character base64, or passphrase derivation.
   - **Tier 2 (Host-Bound Persistent Keyfile)**: Automatically generates and saves 32 bytes of secure entropy in `.carnot.master.key` with restricted POSIX permissions (`0600` / `UserRead | UserWrite`).
   - **Tier 3 (Passphrase KDF Derivation)**: Derives keys via **PBKDF2-HMAC-SHA256** (NIST SP 800-132) configured with 310,000 iterations and unique 128-bit cryptographic salts.
   - **Tier 4 (Ephemeral In-Memory Fallback)**: Generates ephemeral process-bound entropy for non-persistent testing environments.

3. **In-Memory Memory Protection & Zeroization**:
   - Intermediate cryptographic buffers, key material, and plaintext spans are sanitized upon completion using `CryptographicOperations.ZeroMemory`.
   - Secret key comparisons use `CryptographicOperations.FixedTimeEquals` to prevent side-channel timing attacks.
   - Display strings are masked (`ApiKeyMasked`), exposing only prefix and suffix characters.

4. **Zero-Downtime Key Rotation & Encrypted Backup Export**:
   - `RotateMasterKeyAsync(passphrase)`: Atomically re-encrypts all stored credentials under a newly derived master key, updates salt/fingerprint metadata, and persists the vault document.
   - `ExportEncryptedVaultAsync(passphrase)` / `ImportEncryptedVaultAsync(package, passphrase)`: Packages all vault items into a standalone encrypted container protected by an export passphrase and dedicated salt for disaster recovery and cross-environment migration.

5. **Transparent Legacy Migration**:
   - On initialization, the vault automatically detects legacy unencrypted `keys.json` files, transparently encrypts all records into `keys.vault.json`, and deletes the unencrypted legacy file.

## Alternatives Considered
- **OS-Specific DPAPI Only**: Rejected because Windows DPAPI does not function natively across Linux container environments (Docker/Kubernetes).
- **External Secret Managers Only (HashiCorp Vault / Azure Key Vault)**: Rejected to preserve air-gapped local developer workflows and zero-dependency offline operation.
- **AES-256-CBC with HMAC-SHA256**: Rejected in favor of AES-256-GCM AEAD, which offers superior hardware acceleration, single-pass integrity verification, and zero-allocation span integration in .NET 10.

## Consequences

### Positive
- ✅ Confidentiality and cryptographic integrity guaranteed for all API credentials at rest.
- ✅ Full protection against tampering and ciphertext substitution via AEAD tag validation.
- ✅ Zero-dependency operation on Linux, macOS, and Windows containers.
- ✅ Built-in key rotation and encrypted passphrase-protected backup/restore capabilities.
- ✅ Automatic, non-destructive migration of legacy configuration files.

### Negative / Trade-offs
- ⚠️ Master key loss renders stored credentials unrecoverable (requires re-entry of raw API keys).
