using Hypershell.Profiles;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public enum ProfileSyncCredentialKind
{
    LegacyAccessKey,
    DeviceRefreshToken
}

public sealed record ProfileSyncStoredCredential(
    string ServerEndpoint,
    string AccountId,
    ProfileSyncCredentialKind Kind,
    string Secret,
    string ClientDeviceId = "",
    string DeviceFingerprint = "")
{
    public string AccessKey =>
        Kind == ProfileSyncCredentialKind.LegacyAccessKey
            ? Secret
            : string.Empty;

    public string RefreshToken =>
        Kind == ProfileSyncCredentialKind.DeviceRefreshToken
            ? Secret
            : string.Empty;
}

public interface IProfileSyncCredentialStore
{
    bool Exists { get; }

    ProfileSyncStoredCredential? Load();

    void Save(
        ProfileSyncDeviceConfiguration configuration,
        string accessKey);

    void SaveDeviceCredential(
        ProfileSyncDeviceConfiguration configuration,
        CloudSessionResponse session,
        string deviceFingerprint);

    void Delete();
}

public sealed class DpapiProfileSyncCredentialStore
    : IProfileSyncCredentialStore
{
    private const int CurrentSchemaVersion = 2;
    private const int LegacySchemaVersion = 1;
    private const long MaximumProtectedBytes = 64 * 1024;

    private static readonly byte[] AdditionalEntropy =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Hypershell.ProfileSync.Credential.v1"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    private readonly object _gate = new();

    public DpapiProfileSyncCredentialStore(string credentialPath)
    {
        CredentialPath = Path.GetFullPath(credentialPath);
    }

    public string CredentialPath { get; }

    public bool Exists
    {
        get
        {
            lock (_gate)
            {
                return File.Exists(CredentialPath);
            }
        }
    }

    public ProfileSyncStoredCredential? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(CredentialPath))
            {
                return null;
            }

            byte[] protectedPayload;
            try
            {
                var info = new FileInfo(CredentialPath);
                if (info.Length is <= 0 or > MaximumProtectedBytes)
                {
                    throw CredentialFailure(
                        "The saved profile credential has an invalid size.");
                }

                protectedPayload = File.ReadAllBytes(CredentialPath);
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
                throw CredentialFailure(
                    "The saved profile credential could not be read.",
                    exception);
            }

            byte[]? plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(
                    protectedPayload,
                    AdditionalEntropy,
                    DataProtectionScope.CurrentUser);
                using var document = JsonDocument.Parse(plaintext);
                if (!document.RootElement.TryGetProperty(
                        "schemaVersion",
                        out var schemaElement)
                    || !schemaElement.TryGetInt32(out var schemaVersion))
                {
                    throw new JsonException(
                        "The saved profile credential has no schema version.");
                }

                if (schemaVersion == LegacySchemaVersion)
                {
                    var legacy = JsonSerializer
                                     .Deserialize<LegacyCredentialEnvelope>(
                                         plaintext,
                                         JsonOptions)
                                 ?? throw new JsonException(
                                     "The saved legacy profile credential was empty.");
                    ValidateLegacyEnvelope(legacy);
                    return new ProfileSyncStoredCredential(
                        legacy.ServerEndpoint,
                        legacy.AccountId,
                        ProfileSyncCredentialKind.LegacyAccessKey,
                        legacy.AccessKey);
                }

                var envelope = JsonSerializer
                                   .Deserialize<CredentialEnvelope>(
                                       plaintext,
                                       JsonOptions)
                               ?? throw new JsonException(
                                   "The saved profile credential was empty.");
                ValidateEnvelope(envelope);
                return new ProfileSyncStoredCredential(
                    envelope.ServerEndpoint,
                    envelope.AccountId,
                    envelope.CredentialKind,
                    envelope.CredentialSecret,
                    envelope.ClientDeviceId,
                    envelope.DeviceFingerprint);
            }
            catch (Exception exception) when (exception is CryptographicException
                                             or JsonException
                                             or ArgumentException)
            {
                throw CredentialFailure(
                    "The saved profile credential could not be decrypted or validated for this Windows user.",
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
    }

    public void Save(
        ProfileSyncDeviceConfiguration configuration,
        string accessKey)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateAccessKey(accessKey);
        SaveCore(
            configuration,
            ProfileSyncCredentialKind.LegacyAccessKey,
            accessKey,
            clientDeviceId: string.Empty,
            deviceFingerprint: string.Empty);
    }

    public void SaveDeviceCredential(
        ProfileSyncDeviceConfiguration configuration,
        CloudSessionResponse session,
        string deviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(session);
        ValidateRefreshToken(session.RefreshToken);
        ValidateClientDeviceId(session.ClientDeviceId);
        ValidateDeviceFingerprint(deviceFingerprint);
        if (!string.Equals(
                configuration.AccountId?.Trim(),
                session.AccountId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw CredentialFailure(
                "The cloud session account does not match the device connection.");
        }

        SaveCore(
            configuration,
            ProfileSyncCredentialKind.DeviceRefreshToken,
            session.RefreshToken,
            session.ClientDeviceId,
            deviceFingerprint);
    }

    private void SaveCore(
        ProfileSyncDeviceConfiguration configuration,
        ProfileSyncCredentialKind kind,
        string secret,
        string clientDeviceId,
        string deviceFingerprint)
    {
        var endpoint = ProfileSyncEndpoint.Normalize(
            configuration.ServerEndpoint).AbsoluteUri;
        var accountId = configuration.AccountId?.Trim()
                        ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw CredentialFailure(
                "A profile account ID is required before saving its credential.");
        }

        var envelope = new CredentialEnvelope
        {
            ServerEndpoint = endpoint,
            AccountId = accountId,
            CredentialKind = kind,
            CredentialSecret = secret,
            ClientDeviceId = clientDeviceId,
            DeviceFingerprint = deviceFingerprint
        };
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            JsonOptions);
        byte[]? protectedPayload = null;
        try
        {
            protectedPayload = ProtectedData.Protect(
                plaintext,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);
            if (protectedPayload.LongLength > MaximumProtectedBytes)
            {
                throw CredentialFailure(
                    "The protected profile credential exceeds its storage limit.");
            }

            lock (_gate)
            {
                WriteAtomic(protectedPayload);
            }
        }
        catch (Exception exception) when (exception is CryptographicException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            throw CredentialFailure(
                "Windows could not protect or save the profile credential.",
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

    public void Delete()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(CredentialPath))
                {
                    File.Delete(CredentialPath);
                }
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
                throw CredentialFailure(
                    "The saved profile credential could not be removed.",
                    exception);
            }
        }
    }

    private void WriteAtomic(ReadOnlySpan<byte> payload)
    {
        var directory = Path.GetDirectoryName(CredentialPath)
                        ?? throw CredentialFailure(
                            "The profile credential path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath =
            $"{CredentialPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                CredentialPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateEnvelope(CredentialEnvelope envelope)
    {
        if (envelope.SchemaVersion != CurrentSchemaVersion)
        {
            throw new JsonException(
                $"Credential schema {envelope.SchemaVersion} is unsupported.");
        }

        ValidateIdentity(envelope.ServerEndpoint, envelope.AccountId);
        switch (envelope.CredentialKind)
        {
            case ProfileSyncCredentialKind.LegacyAccessKey:
                ValidateAccessKey(envelope.CredentialSecret);
                if (!string.IsNullOrEmpty(envelope.ClientDeviceId)
                    || !string.IsNullOrEmpty(envelope.DeviceFingerprint))
                {
                    throw new JsonException(
                        "A legacy credential contained client-device metadata.");
                }

                break;
            case ProfileSyncCredentialKind.DeviceRefreshToken:
                ValidateRefreshToken(envelope.CredentialSecret);
                ValidateClientDeviceId(envelope.ClientDeviceId);
                ValidateDeviceFingerprint(envelope.DeviceFingerprint);
                break;
            default:
                throw new JsonException(
                    "The saved profile credential kind is unsupported.");
        }
    }

    private static void ValidateLegacyEnvelope(
        LegacyCredentialEnvelope envelope)
    {
        if (envelope.SchemaVersion != LegacySchemaVersion)
        {
            throw new JsonException(
                $"Legacy credential schema {envelope.SchemaVersion} is unsupported.");
        }

        ValidateIdentity(envelope.ServerEndpoint, envelope.AccountId);
        ValidateAccessKey(envelope.AccessKey);
    }

    private static void ValidateIdentity(
        string serverEndpoint,
        string accountId)
    {
        var normalizedEndpoint = ProfileSyncEndpoint.Normalize(
            serverEndpoint).AbsoluteUri;
        if (!string.Equals(
                normalizedEndpoint,
                serverEndpoint,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(accountId)
            || accountId.Length > 64)
        {
            throw new JsonException(
                "The saved profile credential identity is malformed.");
        }
    }

    private static void ValidateAccessKey(string accessKey)
    {
        if (string.IsNullOrWhiteSpace(accessKey)
            || accessKey.Length > 256
            || accessKey.Any(char.IsControl))
        {
            throw CredentialFailure(
                "The profile access key is malformed.");
        }
    }

    private static void ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)
            || !refreshToken.StartsWith("hsr_", StringComparison.Ordinal)
            || refreshToken.Length is < 40 or > 256
            || refreshToken.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-'))
        {
            throw CredentialFailure(
                "The device refresh credential is malformed.");
        }
    }

    private static void ValidateClientDeviceId(string clientDeviceId)
    {
        if (clientDeviceId.Length != 32
            || !clientDeviceId.All(char.IsAsciiHexDigit))
        {
            throw CredentialFailure(
                "The client-device identity is malformed.");
        }
    }

    private static void ValidateDeviceFingerprint(string fingerprint)
    {
        if (fingerprint.Length is < 16 or > 128
            || fingerprint.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not ':'))
        {
            throw CredentialFailure(
                "The client-device fingerprint is malformed.");
        }
    }

    private static ProfileSyncException CredentialFailure(
        string message,
        Exception? innerException = null) =>
        new(
            ProfileSyncFailureKind.Configuration,
            message,
            innerException);

    private sealed class CredentialEnvelope
    {
        [JsonRequired]
        public int SchemaVersion { get; init; } =
            CurrentSchemaVersion;

        [JsonRequired]
        public string ServerEndpoint { get; init; } = string.Empty;

        [JsonRequired]
        public string AccountId { get; init; } = string.Empty;

        [JsonRequired]
        public ProfileSyncCredentialKind CredentialKind { get; init; }

        [JsonRequired]
        public string CredentialSecret { get; init; } = string.Empty;

        [JsonRequired]
        public string ClientDeviceId { get; init; } = string.Empty;

        [JsonRequired]
        public string DeviceFingerprint { get; init; } = string.Empty;
    }

    private sealed class LegacyCredentialEnvelope
    {
        [JsonRequired]
        public int SchemaVersion { get; init; } = LegacySchemaVersion;

        [JsonRequired]
        public string ServerEndpoint { get; init; } = string.Empty;

        [JsonRequired]
        public string AccountId { get; init; } = string.Empty;

        [JsonRequired]
        public string AccessKey { get; init; } = string.Empty;
    }
}
