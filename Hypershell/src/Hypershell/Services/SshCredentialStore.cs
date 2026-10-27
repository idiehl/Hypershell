using Hypershell.Profiles;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public sealed class LocalSshCredential : IDisposable
{
    public required string CredentialId { get; init; }

    public required int KeyVersion { get; init; }

    public required string PublicKeyFingerprint { get; init; }

    public required byte[] PrivateKeyUtf8 { get; init; }

    public required byte[] PassphraseUtf8 { get; init; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(PrivateKeyUtf8);
        CryptographicOperations.ZeroMemory(PassphraseUtf8);
    }
}

public interface ISshCredentialStore
{
    IReadOnlyList<string> ListCredentialIds(
        string serverEndpoint,
        string accountId);

    void Save(
        string serverEndpoint,
        string accountId,
        SshCredentialReleaseResponse credential);

    LocalSshCredential? Load(
        string serverEndpoint,
        string accountId,
        string credentialId);

    void Delete(
        string serverEndpoint,
        string accountId,
        string credentialId);
}

/// <summary>
/// Protects released SSH credentials for the current Windows user with DPAPI.
/// The local file contains one encrypted envelope and no plaintext metadata.
/// </summary>
public sealed class DpapiSshCredentialStore : ISshCredentialStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumProtectedBytes = 16 * 1024 * 1024;
    private static readonly byte[] AdditionalEntropy =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Hypershell.SshCredentialVault.Device.v1"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 12,
        UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _gate = new();
    private readonly string _path;

    public DpapiSshCredentialStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public IReadOnlyList<string> ListCredentialIds(
        string serverEndpoint,
        string accountId)
    {
        lock (_gate)
        {
            var vault = LoadVault();
            try
            {
                var endpoint = NormalizeEndpoint(serverEndpoint);
                var account = NormalizeAccountId(accountId);
                return vault.Credentials
                    .Where(item => SameScope(item, endpoint, account))
                    .Select(item => item.CredentialId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
            }
            finally
            {
                ClearVault(vault);
            }
        }
    }

    public void Save(
        string serverEndpoint,
        string accountId,
        SshCredentialReleaseResponse credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ValidateReleasedCredential(credential);
        lock (_gate)
        {
            var vault = LoadVault();
            try
            {
                var endpoint = NormalizeEndpoint(serverEndpoint);
                var account = NormalizeAccountId(accountId);
                vault.Credentials.RemoveAll(item =>
                    SameScope(item, endpoint, account)
                    && string.Equals(
                        item.CredentialId,
                        credential.CredentialId,
                        StringComparison.Ordinal));
                vault.Credentials.Add(new LocalCredentialEnvelope
                {
                    ServerEndpoint = endpoint,
                    AccountId = account,
                    CredentialId = credential.CredentialId,
                    KeyVersion = credential.KeyVersion,
                    PublicKeyFingerprint = credential.PublicKeyFingerprint,
                    PrivateKeyUtf8 = credential.PrivateKeyUtf8.ToArray(),
                    PassphraseUtf8 = credential.PassphraseUtf8.ToArray(),
                    SavedAtUtc = DateTimeOffset.UtcNow
                });
                SaveVault(vault);
            }
            finally
            {
                ClearVault(vault);
            }
        }
    }

    public LocalSshCredential? Load(
        string serverEndpoint,
        string accountId,
        string credentialId)
    {
        ValidateCredentialId(credentialId);
        lock (_gate)
        {
            var vault = LoadVault();
            try
            {
                var endpoint = NormalizeEndpoint(serverEndpoint);
                var account = NormalizeAccountId(accountId);
                var stored = vault.Credentials.SingleOrDefault(item =>
                    SameScope(item, endpoint, account)
                    && string.Equals(
                        item.CredentialId,
                        credentialId,
                        StringComparison.Ordinal));
                return stored is null
                    ? null
                    : new LocalSshCredential
                    {
                        CredentialId = stored.CredentialId,
                        KeyVersion = stored.KeyVersion,
                        PublicKeyFingerprint =
                            stored.PublicKeyFingerprint,
                        PrivateKeyUtf8 = stored.PrivateKeyUtf8.ToArray(),
                        PassphraseUtf8 = stored.PassphraseUtf8.ToArray()
                    };
            }
            finally
            {
                ClearVault(vault);
            }
        }
    }

    public void Delete(
        string serverEndpoint,
        string accountId,
        string credentialId)
    {
        ValidateCredentialId(credentialId);
        lock (_gate)
        {
            var vault = LoadVault();
            try
            {
                var endpoint = NormalizeEndpoint(serverEndpoint);
                var account = NormalizeAccountId(accountId);
                var removed = vault.Credentials.RemoveAll(item =>
                    SameScope(item, endpoint, account)
                    && string.Equals(
                        item.CredentialId,
                        credentialId,
                        StringComparison.Ordinal));
                if (removed > 0)
                {
                    SaveVault(vault);
                }
            }
            finally
            {
                ClearVault(vault);
            }
        }
    }

    private LocalVaultEnvelope LoadVault()
    {
        if (!File.Exists(_path))
        {
            return new LocalVaultEnvelope();
        }

        byte[] protectedPayload;
        try
        {
            var info = new FileInfo(_path);
            if (info.Length is <= 0 or > MaximumProtectedBytes)
            {
                throw Failure(
                    "The protected SSH credential vault has an invalid size.");
            }

            protectedPayload = File.ReadAllBytes(_path);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
            throw Failure(
                "The protected SSH credential vault could not be read.",
                exception);
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(
                protectedPayload,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);
            var vault = JsonSerializer.Deserialize<LocalVaultEnvelope>(
                            plaintext,
                            JsonOptions)
                        ?? throw new JsonException(
                            "The SSH credential vault was empty.");
            ValidateVault(vault);
            return vault;
        }
        catch (Exception exception) when (exception is
                                          CryptographicException
                                          or JsonException
                                          or ArgumentException)
        {
            throw Failure(
                "The SSH credential vault could not be decrypted or validated for this Windows user.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private void SaveVault(LocalVaultEnvelope vault)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            vault,
            JsonOptions);
        byte[]? protectedPayload = null;
        try
        {
            protectedPayload = ProtectedData.Protect(
                plaintext,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);
            if (protectedPayload.Length > MaximumProtectedBytes)
            {
                throw Failure(
                    "The protected SSH credential vault exceeds its storage limit.");
            }

            var directory = Path.GetDirectoryName(_path)
                            ?? throw Failure(
                                "The SSH credential-vault path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(protectedPayload);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is
                                          CryptographicException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            throw Failure(
                "Windows could not protect or save the SSH credential vault.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
            }
        }
    }

    private static void ValidateVault(LocalVaultEnvelope vault)
    {
        if (vault.SchemaVersion != CurrentSchemaVersion
            || vault.Credentials.Count > 512)
        {
            throw new JsonException(
                "The SSH credential-vault schema or item count is unsupported.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in vault.Credentials)
        {
            var endpoint = NormalizeEndpoint(item.ServerEndpoint);
            var account = NormalizeAccountId(item.AccountId);
            ValidateCredentialId(item.CredentialId);
            if (item.KeyVersion <= 0
                || item.PrivateKeyUtf8.Length is <= 0 or > 1024 * 1024
                || item.PassphraseUtf8.Length > 16 * 1024
                || string.IsNullOrWhiteSpace(item.PublicKeyFingerprint)
                || !identities.Add(
                    endpoint + "\n" + account + "\n" + item.CredentialId))
            {
                throw new JsonException(
                    "The SSH credential vault contains malformed or duplicate entries.");
            }
        }
    }

    private static void ValidateReleasedCredential(
        SshCredentialReleaseResponse credential)
    {
        if (credential.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw Failure(
                "The released SSH credential uses an unsupported protocol version.");
        }

        ValidateCredentialId(credential.CredentialId);
        if (credential.KeyVersion <= 0
            || credential.PrivateKeyUtf8.Length is <= 0 or > 1024 * 1024
            || credential.PassphraseUtf8.Length > 16 * 1024
            || string.IsNullOrWhiteSpace(credential.PublicKeyFingerprint))
        {
            throw Failure(
                "The released SSH credential is malformed.");
        }
    }

    private static string NormalizeEndpoint(string value) =>
        ProfileSyncEndpoint.Normalize(value).AbsoluteUri;

    private static string NormalizeAccountId(string value)
    {
        var account = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (account.Length is < 3 or > 64
            || account.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or '-')))
        {
            throw Failure("The SSH credential account ID is malformed.");
        }

        return account;
    }

    private static void ValidateCredentialId(string value)
    {
        if (value.Length != 32 || !value.All(char.IsAsciiHexDigit))
        {
            throw Failure("The local SSH credential ID is malformed.");
        }
    }

    private static bool SameScope(
        LocalCredentialEnvelope item,
        string endpoint,
        string accountId) =>
        string.Equals(
            item.ServerEndpoint,
            endpoint,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            item.AccountId,
            accountId,
            StringComparison.OrdinalIgnoreCase);

    private static void ClearVault(LocalVaultEnvelope vault)
    {
        foreach (var item in vault.Credentials)
        {
            CryptographicOperations.ZeroMemory(item.PrivateKeyUtf8);
            CryptographicOperations.ZeroMemory(item.PassphraseUtf8);
        }
    }

    private static ProfileSyncException Failure(
        string message,
        Exception? innerException = null) =>
        new(ProfileSyncFailureKind.Configuration, message, innerException);

    private sealed record LocalVaultEnvelope
    {
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        public List<LocalCredentialEnvelope> Credentials { get; init; } = [];
    }

    private sealed record LocalCredentialEnvelope
    {
        public string ServerEndpoint { get; init; } = string.Empty;

        public string AccountId { get; init; } = string.Empty;

        public string CredentialId { get; init; } = string.Empty;

        public int KeyVersion { get; init; }

        public string PublicKeyFingerprint { get; init; } = string.Empty;

        public byte[] PrivateKeyUtf8 { get; init; } = [];

        public byte[] PassphraseUtf8 { get; init; } = [];

        public DateTimeOffset SavedAtUtc { get; init; }
    }
}
