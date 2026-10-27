using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Hypershell.ProfileServer;

public enum SshVaultMutationKind
{
    Created,
    Updated,
    Conflict,
    NotFound
}

public sealed record SshCredentialMutationResult(
    SshVaultMutationKind Kind,
    SshCredentialSummary? Credential = null);

public sealed class SshCredentialVaultStore
{
    private readonly CloudMetadataStore _metadata;
    private readonly SshSecretEnvelopeService _envelopes;
    private readonly TimeProvider _timeProvider;

    public SshCredentialVaultStore(
        CloudMetadataStore metadata,
        SshSecretEnvelopeService envelopes,
        TimeProvider timeProvider)
    {
        _metadata = metadata;
        _envelopes = envelopes;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<SshCredentialSummary>> ListAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, display_name, public_key_fingerprint, key_version,
                   created_at_utc, updated_at_utc, rotated_at_utc,
                   revoked_at_utc
            FROM ssh_credentials
            WHERE account_id = $accountId
            ORDER BY revoked_at_utc IS NOT NULL, display_name, id;
            """;
        command.Parameters.AddWithValue("$accountId", account);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var credentials = new List<SshCredentialSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            credentials.Add(ReadSummary(reader));
        }

        return credentials;
    }

    public async Task<SshCredentialMutationResult> CreateAsync(
        string accountId,
        SshCredentialMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateProtocol(request);
        if (request.ExpectedKeyVersion != 0)
        {
            return new SshCredentialMutationResult(
                SshVaultMutationKind.Conflict);
        }

        var displayName = ValidatePrintable(
            request.DisplayName,
            1,
            120,
            "credential label");
        var identity = SshPrivateKeyInspector.Inspect(
            request.PrivateKeyUtf8,
            request.PassphraseUtf8);
        var credentialId = Guid.NewGuid().ToString("N");
        const int keyVersion = 1;
        var envelope = _envelopes.Encrypt(
            account,
            credentialId,
            keyVersion,
            request.PrivateKeyUtf8,
            request.PassphraseUtf8);
        try
        {
            var now = _timeProvider.GetUtcNow();
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO ssh_credentials(
                    id, account_id, display_name, encrypted_private_key,
                    encrypted_passphrase, public_key_fingerprint,
                    created_at_utc, updated_at_utc, rotated_at_utc,
                    envelope_format, key_version, revoked_at_utc)
                VALUES(
                    $id, $accountId, $displayName, $envelope,
                    NULL, $fingerprint, $createdAtUtc, $updatedAtUtc, NULL,
                    1, $keyVersion, NULL);
                """;
            command.Parameters.AddWithValue("$id", credentialId);
            command.Parameters.AddWithValue("$accountId", account);
            command.Parameters.AddWithValue("$displayName", displayName);
            command.Parameters.Add("$envelope", SqliteType.Blob).Value =
                envelope;
            command.Parameters.AddWithValue(
                "$fingerprint",
                identity.Fingerprint);
            command.Parameters.AddWithValue(
                "$createdAtUtc",
                now.ToString("O"));
            command.Parameters.AddWithValue(
                "$updatedAtUtc",
                now.ToString("O"));
            command.Parameters.AddWithValue("$keyVersion", keyVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
            var summary = new SshCredentialSummary
            {
                Id = credentialId,
                DisplayName = displayName,
                PublicKeyFingerprint = identity.Fingerprint,
                KeyVersion = keyVersion,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            await AppendAuditAsync(
                connection,
                account,
                clientDeviceId: null,
                remoteHostId: null,
                credentialId,
                "credential.create",
                displayName,
                "created",
                cancellationToken,
                (SqliteTransaction)transaction);
            await transaction.CommitAsync(cancellationToken);
            return new SshCredentialMutationResult(
                SshVaultMutationKind.Created,
                summary);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations
                .ZeroMemory(envelope);
        }
    }

    public async Task<SshCredentialMutationResult> RotateAsync(
        string accountId,
        string credentialId,
        SshCredentialMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateCredentialId(credentialId);
        ValidateProtocol(request);
        if (request.ExpectedKeyVersion <= 0)
        {
            return new SshCredentialMutationResult(
                SshVaultMutationKind.Conflict);
        }

        var displayName = ValidatePrintable(
            request.DisplayName,
            1,
            120,
            "credential label");
        var identity = SshPrivateKeyInspector.Inspect(
            request.PrivateKeyUtf8,
            request.PassphraseUtf8);
        var nextVersion = checked(request.ExpectedKeyVersion + 1);
        var envelope = _envelopes.Encrypt(
            account,
            credentialId,
            nextVersion,
            request.PrivateKeyUtf8,
            request.PassphraseUtf8);
        try
        {
            var now = _timeProvider.GetUtcNow();
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                cancellationToken);
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText =
                """
                UPDATE ssh_credentials
                SET display_name = $displayName,
                    encrypted_private_key = $envelope,
                    encrypted_passphrase = NULL,
                    public_key_fingerprint = $fingerprint,
                    updated_at_utc = $updatedAtUtc,
                    rotated_at_utc = $updatedAtUtc,
                    envelope_format = 1,
                    key_version = $nextVersion
                WHERE id = $id AND account_id = $accountId
                  AND key_version = $expectedVersion
                  AND revoked_at_utc IS NULL;
                """;
            update.Parameters.AddWithValue("$id", credentialId);
            update.Parameters.AddWithValue("$accountId", account);
            update.Parameters.AddWithValue("$displayName", displayName);
            update.Parameters.Add("$envelope", SqliteType.Blob).Value =
                envelope;
            update.Parameters.AddWithValue(
                "$fingerprint",
                identity.Fingerprint);
            update.Parameters.AddWithValue(
                "$updatedAtUtc",
                now.ToString("O"));
            update.Parameters.AddWithValue(
                "$expectedVersion",
                request.ExpectedKeyVersion);
            update.Parameters.AddWithValue("$nextVersion", nextVersion);
            var changed = await update.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                var exists = await ExistsAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    account,
                    credentialId,
                    cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                return new SshCredentialMutationResult(
                    exists
                        ? SshVaultMutationKind.Conflict
                        : SshVaultMutationKind.NotFound);
            }

            await using var denyReleases = connection.CreateCommand();
            denyReleases.Transaction = (SqliteTransaction)transaction;
            denyReleases.CommandText =
                """
                UPDATE credential_release_authorizations
                SET denied_at_utc = $now
                WHERE account_id = $accountId
                  AND credential_id = $credentialId
                  AND consumed_at_utc IS NULL
                  AND denied_at_utc IS NULL;
                """;
            denyReleases.Parameters.AddWithValue("$now", now.ToString("O"));
            denyReleases.Parameters.AddWithValue("$accountId", account);
            denyReleases.Parameters.AddWithValue(
                "$credentialId",
                credentialId);
            await denyReleases.ExecuteNonQueryAsync(cancellationToken);
            await AppendAuditAsync(
                connection,
                account,
                clientDeviceId: null,
                remoteHostId: null,
                credentialId,
                "credential.rotate",
                displayName,
                $"rotated-to-{nextVersion}",
                cancellationToken,
                (SqliteTransaction)transaction);
            await transaction.CommitAsync(cancellationToken);
            return new SshCredentialMutationResult(
                SshVaultMutationKind.Updated,
                await FindAsync(
                    account,
                    credentialId,
                    cancellationToken: cancellationToken));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations
                .ZeroMemory(envelope);
        }
    }

    public async Task<bool> RevokeAsync(
        string accountId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateCredentialId(credentialId);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText =
            """
            UPDATE ssh_credentials
            SET revoked_at_utc = $now,
                updated_at_utc = $now
            WHERE id = $id AND account_id = $accountId
              AND revoked_at_utc IS NULL;
            """;
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        update.Parameters.AddWithValue("$id", credentialId);
        update.Parameters.AddWithValue("$accountId", account);
        var changed = await update.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 1)
        {
            await using var denyReleases = connection.CreateCommand();
            denyReleases.Transaction = (SqliteTransaction)transaction;
            denyReleases.CommandText =
                """
                UPDATE credential_release_authorizations
                SET denied_at_utc = $now
                WHERE account_id = $accountId
                  AND credential_id = $credentialId
                  AND consumed_at_utc IS NULL
                  AND denied_at_utc IS NULL;
                """;
            denyReleases.Parameters.AddWithValue("$now", now.ToString("O"));
            denyReleases.Parameters.AddWithValue("$accountId", account);
            denyReleases.Parameters.AddWithValue(
                "$credentialId",
                credentialId);
            await denyReleases.ExecuteNonQueryAsync(cancellationToken);
            await AppendAuditAsync(
                connection,
                account,
                clientDeviceId: null,
                remoteHostId: null,
                credentialId,
                "credential.revoke",
                credentialId,
                "revoked",
                cancellationToken,
                (SqliteTransaction)transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    public async Task<SshCredentialSummary?> FindAsync(
        string accountId,
        string credentialId,
        bool includeRevoked = false,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateCredentialId(credentialId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, display_name, public_key_fingerprint, key_version,
                   created_at_utc, updated_at_utc, rotated_at_utc,
                   revoked_at_utc
            FROM ssh_credentials
            WHERE id = $id AND account_id = $accountId
              AND ($includeRevoked = 1 OR revoked_at_utc IS NULL);
            """;
        command.Parameters.AddWithValue("$id", credentialId);
        command.Parameters.AddWithValue("$accountId", account);
        command.Parameters.AddWithValue(
            "$includeRevoked",
            includeRevoked ? 1 : 0);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadSummary(reader)
            : null;
    }

    public async Task<SshCredentialSecretMaterial?> ReadSecretAsync(
        string accountId,
        string credentialId,
        int expectedKeyVersion,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateCredentialId(credentialId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT encrypted_private_key, envelope_format, key_version
            FROM ssh_credentials
            WHERE id = $id AND account_id = $accountId
              AND revoked_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$id", credentialId);
        command.Parameters.AddWithValue("$accountId", account);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.GetInt32(1) != 1
            || reader.GetInt32(2) != expectedKeyVersion)
        {
            return null;
        }

        var envelope = (byte[])reader[0];
        try
        {
            return _envelopes.Decrypt(
                account,
                credentialId,
                expectedKeyVersion,
                envelope);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations
                .ZeroMemory(envelope);
        }
    }

    internal static async Task AppendAuditAsync(
        SqliteConnection connection,
        string accountId,
        string? clientDeviceId,
        string? remoteHostId,
        string? credentialId,
        string action,
        string targetSummary,
        string result,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        string? conversationId = null)
    {
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText =
            """
            INSERT INTO ssh_audit_events(
                account_id, client_device_id, remote_host_id, credential_id,
                action, target_summary, result, conversation_id,
                created_at_utc)
            VALUES(
                $accountId, $clientDeviceId, $remoteHostId, $credentialId,
                $action, $targetSummary, $result, $conversationId,
                $createdAtUtc);
            """;
        audit.Parameters.AddWithValue("$accountId", accountId);
        audit.Parameters.AddWithValue(
            "$clientDeviceId",
            (object?)clientDeviceId ?? DBNull.Value);
        audit.Parameters.AddWithValue(
            "$remoteHostId",
            (object?)remoteHostId ?? DBNull.Value);
        audit.Parameters.AddWithValue(
            "$credentialId",
            (object?)credentialId ?? DBNull.Value);
        audit.Parameters.AddWithValue("$action", action);
        audit.Parameters.AddWithValue(
            "$targetSummary",
            ValidateAuditValue(targetSummary, 256));
        audit.Parameters.AddWithValue("$result", ValidateAuditValue(result, 128));
        audit.Parameters.AddWithValue(
            "$conversationId",
            (object?)conversationId ?? DBNull.Value);
        audit.Parameters.AddWithValue(
            "$createdAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SshCredentialSummary ReadSummary(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            DisplayName = reader.GetString(1),
            PublicKeyFingerprint = reader.GetString(2),
            KeyVersion = reader.GetInt32(3),
            CreatedAtUtc = ParseTime(reader.GetString(4)),
            UpdatedAtUtc = ParseTime(reader.GetString(5)),
            RotatedAtUtc = reader.IsDBNull(6)
                ? null
                : ParseTime(reader.GetString(6)),
            IsRevoked = !reader.IsDBNull(7)
        };

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId,
        string credentialId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM ssh_credentials WHERE id = $id AND account_id = $accountId;";
        command.Parameters.AddWithValue("$id", credentialId);
        command.Parameters.AddWithValue("$accountId", accountId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static void ValidateProtocol(SshCredentialMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw new ArgumentException(
                "The SSH credential request uses an unsupported protocol version.");
        }
    }

    internal static void ValidateCredentialId(string credentialId)
    {
        if (credentialId.Length != 32
            || !credentialId.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                "The SSH credential ID is malformed.",
                nameof(credentialId));
        }
    }

    internal static string ValidatePrintable(
        string value,
        int minimum,
        int maximum,
        string field)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < minimum
            || normalized.Length > maximum
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The {field} must contain {minimum}-{maximum} printable characters.");
        }

        return normalized;
    }

    private static string ValidateAuditValue(string value, int maximum)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= maximum
            ? normalized
            : normalized[..maximum];
    }

    internal static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
