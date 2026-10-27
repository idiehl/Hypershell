using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.ProfileServer;

public enum CredentialReleasePollKind
{
    Pending,
    Approved,
    Expired,
    Denied,
    Invalid,
    Consumed,
    CredentialChanged
}

public sealed record CredentialReleasePollResult(
    CredentialReleasePollKind Kind,
    SshCredentialReleaseResponse? Credential = null);

public sealed record PendingCredentialRelease(
    string CredentialLabel,
    string PublicKeyFingerprint,
    int KeyVersion,
    string ClientDeviceName,
    string ClientPlatform,
    DateTimeOffset ExpiresAtUtc);

public sealed class CredentialReleaseService
{
    private static readonly TimeSpan AuthorizationLifetime =
        TimeSpan.FromMinutes(10);

    private readonly CloudMetadataStore _metadata;
    private readonly SshCredentialVaultStore _vault;
    private readonly TimeProvider _timeProvider;

    public CredentialReleaseService(
        CloudMetadataStore metadata,
        SshCredentialVaultStore vault,
        TimeProvider timeProvider)
    {
        _metadata = metadata;
        _vault = vault;
        _timeProvider = timeProvider;
    }

    public async Task<SshCredentialReleaseAuthorization> StartAsync(
        ProfileSessionPrincipal principal,
        string credentialId,
        Uri verificationBaseUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(verificationBaseUri);
        if (string.IsNullOrWhiteSpace(principal.ClientDeviceId))
        {
            throw new UnauthorizedAccessException(
                "Authorize this PC through the browser before requesting an SSH credential.");
        }

        SshCredentialVaultStore.ValidateCredentialId(credentialId);
        var credential = await _vault.FindAsync(
            principal.AccountId,
            credentialId,
            cancellationToken: cancellationToken)
            ?? throw new KeyNotFoundException(
                "The SSH credential does not exist, is revoked, or belongs to another account.");
        var clientDeviceId = principal.ClientDeviceId;
        var rawToken = "hsk_" + Base64Url(
            RandomNumberGenerator.GetBytes(32));
        var userCode = CreateUserCode();
        var now = _timeProvider.GetUtcNow();
        var expires = now.Add(AuthorizationLifetime);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        if (!await IsActiveClientDeviceAsync(
                connection,
                principal.AccountId,
                clientDeviceId,
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "The requesting client PC is no longer approved.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO credential_release_authorizations(
                release_token_hash, user_code_hash, account_id,
                credential_id, client_device_id, expected_key_version,
                requested_at_utc, expires_at_utc)
            VALUES(
                $releaseTokenHash, $userCodeHash, $accountId,
                $credentialId, $clientDeviceId, $expectedKeyVersion,
                $requestedAtUtc, $expiresAtUtc);
            """;
        command.Parameters.Add("$releaseTokenHash", SqliteType.Blob).Value =
            Hash(rawToken);
        command.Parameters.Add("$userCodeHash", SqliteType.Blob).Value =
            Hash(NormalizeUserCode(userCode));
        command.Parameters.AddWithValue("$accountId", principal.AccountId);
        command.Parameters.AddWithValue("$credentialId", credentialId);
        command.Parameters.AddWithValue("$clientDeviceId", clientDeviceId);
        command.Parameters.AddWithValue(
            "$expectedKeyVersion",
            credential.KeyVersion);
        command.Parameters.AddWithValue(
            "$requestedAtUtc",
            now.ToString("O"));
        command.Parameters.AddWithValue(
            "$expiresAtUtc",
            expires.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await SshCredentialVaultStore.AppendAuditAsync(
            connection,
            principal.AccountId,
            clientDeviceId,
            remoteHostId: null,
            credentialId,
            "credential.release.request",
            credential.DisplayName,
            "pending-portal-reauthentication",
            cancellationToken);
        return new SshCredentialReleaseAuthorization
        {
            ReleaseToken = rawToken,
            UserCode = userCode,
            VerificationUri = new Uri(
                verificationBaseUri,
                "/CredentialRelease").AbsoluteUri,
            ExpiresAtUtc = expires
        };
    }

    public async Task<PendingCredentialRelease?> FindPendingAsync(
        string accountId,
        string userCode,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalizedCode = NormalizeUserCode(userCode);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.display_name, c.public_key_fingerprint,
                   r.expected_key_version, d.display_name, d.platform,
                   r.expires_at_utc, r.approved_at_utc, r.consumed_at_utc,
                   r.denied_at_utc, c.revoked_at_utc, d.revoked_at_utc
            FROM credential_release_authorizations r
            INNER JOIN ssh_credentials c ON c.id = r.credential_id
            INNER JOIN client_devices d ON d.id = r.client_device_id
            WHERE r.account_id = $accountId
              AND r.user_code_hash = $userCodeHash;
            """;
        command.Parameters.AddWithValue("$accountId", account);
        command.Parameters.Add("$userCodeHash", SqliteType.Blob).Value =
            Hash(normalizedCode);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var expires = ParseTime(reader.GetString(5));
        return expires > _timeProvider.GetUtcNow()
               && reader.IsDBNull(6)
               && reader.IsDBNull(7)
               && reader.IsDBNull(8)
               && reader.IsDBNull(9)
               && reader.IsDBNull(10)
            ? new PendingCredentialRelease(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                expires)
            : null;
    }

    public async Task<bool> ApproveAsync(
        string accountId,
        string userCode,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE credential_release_authorizations
            SET approved_at_utc = $now
            WHERE account_id = $accountId
              AND user_code_hash = $userCodeHash
              AND expires_at_utc > $now
              AND approved_at_utc IS NULL
              AND consumed_at_utc IS NULL
              AND denied_at_utc IS NULL
              AND EXISTS(
                  SELECT 1 FROM ssh_credentials c
                  WHERE c.id = credential_id
                    AND c.account_id = $accountId
                    AND c.revoked_at_utc IS NULL
                    AND c.key_version = expected_key_version)
              AND EXISTS(
                  SELECT 1 FROM client_devices d
                  WHERE d.id = client_device_id
                    AND d.account_id = $accountId
                    AND d.revoked_at_utc IS NULL);
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$accountId", account);
        command.Parameters.Add("$userCodeHash", SqliteType.Blob).Value =
            Hash(NormalizeUserCode(userCode));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DenyAsync(
        string accountId,
        string userCode,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE credential_release_authorizations
            SET denied_at_utc = $now
            WHERE account_id = $accountId
              AND user_code_hash = $userCodeHash
              AND expires_at_utc > $now
              AND approved_at_utc IS NULL
              AND consumed_at_utc IS NULL
              AND denied_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$accountId", account);
        command.Parameters.Add("$userCodeHash", SqliteType.Blob).Value =
            Hash(NormalizeUserCode(userCode));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<CredentialReleasePollResult> PollAsync(
        ProfileSessionPrincipal principal,
        string releaseToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrWhiteSpace(principal.ClientDeviceId)
            || string.IsNullOrWhiteSpace(releaseToken)
            || releaseToken.Length > 256)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Invalid);
        }

        var clientDeviceId = principal.ClientDeviceId;
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var record = await ReadReleaseAsync(
            connection,
            principal.AccountId,
            clientDeviceId,
            releaseToken,
            cancellationToken);
        if (record is null)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Invalid);
        }

        if (record.DeviceRevokedAtUtc is not null)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Denied);
        }

        if (record.ConsumedAtUtc is not null)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Consumed);
        }

        if (record.DeniedAtUtc is not null
            || record.CredentialRevokedAtUtc is not null)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Denied);
        }

        if (record.ExpiresAtUtc <= now)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Expired);
        }

        if (record.ExpectedKeyVersion != record.CurrentKeyVersion)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.CredentialChanged);
        }

        if (record.ApprovedAtUtc is null)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Pending);
        }

        using var secret = await _vault.ReadSecretAsync(
            principal.AccountId,
            record.CredentialId,
            record.ExpectedKeyVersion,
            cancellationToken);
        if (secret is null)
        {
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.CredentialChanged);
        }

        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var consume = connection.CreateCommand();
        consume.Transaction = (SqliteTransaction)transaction;
        consume.CommandText =
            """
            UPDATE credential_release_authorizations
            SET consumed_at_utc = $now
            WHERE release_token_hash = $releaseTokenHash
              AND account_id = $accountId
              AND client_device_id = $clientDeviceId
              AND approved_at_utc IS NOT NULL
              AND consumed_at_utc IS NULL
              AND denied_at_utc IS NULL
              AND expires_at_utc > $now;
            """;
        consume.Parameters.AddWithValue("$now", now.ToString("O"));
        consume.Parameters.Add("$releaseTokenHash", SqliteType.Blob).Value =
            Hash(releaseToken);
        consume.Parameters.AddWithValue("$accountId", principal.AccountId);
        consume.Parameters.AddWithValue("$clientDeviceId", clientDeviceId);
        if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CredentialReleasePollResult(
                CredentialReleasePollKind.Consumed);
        }

        await SshCredentialVaultStore.AppendAuditAsync(
            connection,
            principal.AccountId,
            clientDeviceId,
            remoteHostId: null,
            record.CredentialId,
            "credential.release.consume",
            record.CredentialId,
            $"released-version-{record.ExpectedKeyVersion}",
            cancellationToken,
            (SqliteTransaction)transaction);
        await transaction.CommitAsync(cancellationToken);
        return new CredentialReleasePollResult(
            CredentialReleasePollKind.Approved,
            new SshCredentialReleaseResponse
            {
                CredentialId = record.CredentialId,
                KeyVersion = record.ExpectedKeyVersion,
                PublicKeyFingerprint = record.PublicKeyFingerprint,
                PrivateKeyUtf8 = secret.PrivateKeyUtf8.ToArray(),
                PassphraseUtf8 = secret.PassphraseUtf8.ToArray()
            });
    }

    private static async Task<CredentialReleaseRecord?> ReadReleaseAsync(
        SqliteConnection connection,
        string accountId,
        string clientDeviceId,
        string releaseToken,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.credential_id, r.expected_key_version,
                   r.expires_at_utc, r.approved_at_utc, r.consumed_at_utc,
                   r.denied_at_utc, c.key_version,
                   c.public_key_fingerprint, c.revoked_at_utc,
                   d.revoked_at_utc
            FROM credential_release_authorizations r
            INNER JOIN ssh_credentials c ON c.id = r.credential_id
            INNER JOIN client_devices d ON d.id = r.client_device_id
            WHERE r.release_token_hash = $releaseTokenHash
              AND r.account_id = $accountId
              AND r.client_device_id = $clientDeviceId;
            """;
        command.Parameters.Add("$releaseTokenHash", SqliteType.Blob).Value =
            Hash(releaseToken);
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$clientDeviceId", clientDeviceId);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CredentialReleaseRecord(
                reader.GetString(0),
                reader.GetInt32(1),
                ParseTime(reader.GetString(2)),
                reader.IsDBNull(3) ? null : ParseTime(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseTime(reader.GetString(5)),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : ParseTime(reader.GetString(8)),
                reader.IsDBNull(9) ? null : ParseTime(reader.GetString(9)))
            : null;
    }

    private static async Task<bool> IsActiveClientDeviceAsync(
        SqliteConnection connection,
        string accountId,
        string clientDeviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM client_devices
            WHERE id = $id AND account_id = $accountId
              AND revoked_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$id", clientDeviceId);
        command.Parameters.AddWithValue("$accountId", accountId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static byte[] Hash(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string CreateUserCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        Span<char> code = stackalloc char[9];
        for (var index = 0; index < random.Length; index++)
        {
            code[index + (index >= 4 ? 1 : 0)] =
                alphabet[random[index] % alphabet.Length];
        }

        code[4] = '-';
        return new string(code);
    }

    private static string NormalizeUserCode(string value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        if (normalized.Length != 9
            || normalized[4] != '-'
            || normalized.Where((_, index) => index != 4)
                .Any(character =>
                    !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "The credential-release code is malformed.");
        }

        return normalized;
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private sealed record CredentialReleaseRecord(
        string CredentialId,
        int ExpectedKeyVersion,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ApprovedAtUtc,
        DateTimeOffset? ConsumedAtUtc,
        DateTimeOffset? DeniedAtUtc,
        int CurrentKeyVersion,
        string PublicKeyFingerprint,
        DateTimeOffset? CredentialRevokedAtUtc,
        DateTimeOffset? DeviceRevokedAtUtc);
}
