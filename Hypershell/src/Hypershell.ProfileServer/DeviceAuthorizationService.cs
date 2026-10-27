using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.ProfileServer;

public enum DeviceAuthorizationPollKind
{
    Pending,
    Approved,
    Expired,
    Denied,
    Invalid,
    Consumed
}

public sealed record DeviceAuthorizationPollResult(
    DeviceAuthorizationPollKind Kind,
    CloudSessionResponse? Session = null);

public sealed record PendingDeviceAuthorization(
    string DeviceName,
    string Platform,
    DateTimeOffset ExpiresAtUtc);

public sealed class DeviceAuthorizationService
{
    private static readonly TimeSpan AuthorizationLifetime =
        TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefreshTokenLifetime =
        TimeSpan.FromDays(30);
    private readonly CloudMetadataStore _metadata;
    private readonly ProfileAccountStore _accounts;
    private readonly ProfileSessionTokenService _sessions;
    private readonly TimeProvider _timeProvider;

    public DeviceAuthorizationService(
        CloudMetadataStore metadata,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        TimeProvider timeProvider)
    {
        _metadata = metadata;
        _accounts = accounts;
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceAuthorizationResponse> StartAsync(
        DeviceAuthorizationRequest request,
        Uri verificationBaseUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(verificationBaseUri);
        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw new ArgumentException(
                "The device authorization protocol version is unsupported.");
        }

        var name = ValidatePrintable(
            request.DeviceName,
            1,
            120,
            "device name");
        var platform = ValidatePrintable(
            request.Platform,
            1,
            80,
            "device platform");
        var fingerprint = ValidateFingerprint(request.DeviceFingerprint);
        var rawDeviceCode = "hsd_" + Base64Url(
            RandomNumberGenerator.GetBytes(32));
        var userCode = CreateUserCode();
        var now = _timeProvider.GetUtcNow();
        var expires = now.Add(AuthorizationLifetime);

        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO device_authorizations(
                device_code_hash,
                user_code_hash,
                requested_name,
                requested_platform,
                device_fingerprint,
                created_at_utc,
                expires_at_utc)
            VALUES(
                $deviceCodeHash,
                $userCodeHash,
                $name,
                $platform,
                $fingerprint,
                $createdAtUtc,
                $expiresAtUtc);
            """;
        command.Parameters.Add("$deviceCodeHash", SqliteType.Blob).Value =
            Hash(rawDeviceCode);
        command.Parameters.Add("$userCodeHash", SqliteType.Blob).Value =
            Hash(NormalizeUserCode(userCode));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
        command.Parameters.AddWithValue("$expiresAtUtc", expires.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new DeviceAuthorizationResponse
        {
            DeviceCode = rawDeviceCode,
            UserCode = userCode,
            VerificationUri = new Uri(
                verificationBaseUri,
                "/Device").AbsoluteUri,
            ExpiresAtUtc = expires
        };
    }

    public async Task<ClientDeviceSummary?> ApproveAsync(
        string accountId,
        string userCode,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.FindAsync(accountId, cancellationToken);
        if (account is null || account.IsDisabled)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var authorization = await ReadAuthorizationByUserCodeAsync(
            connection,
            (SqliteTransaction)transaction,
            userCode,
            cancellationToken);
        if (authorization is null
            || authorization.ExpiresAtUtc <= now
            || authorization.ApprovedAtUtc is not null
            || authorization.DeniedAtUtc is not null
            || authorization.ConsumedAtUtc is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var existing = await FindDeviceByFingerprintAsync(
            connection,
            (SqliteTransaction)transaction,
            account.AccountId,
            authorization.DeviceFingerprint,
            cancellationToken);
        var deviceId = existing?.Id ?? Guid.NewGuid().ToString("N");
        if (existing is null)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText =
                """
                INSERT INTO client_devices(
                    id, account_id, display_name, platform,
                    device_fingerprint, created_at_utc, last_seen_at_utc)
                VALUES(
                    $id, $accountId, $displayName, $platform,
                    $fingerprint, $createdAtUtc, $lastSeenAtUtc);
                """;
            insert.Parameters.AddWithValue("$id", deviceId);
            insert.Parameters.AddWithValue("$accountId", account.AccountId);
            insert.Parameters.AddWithValue(
                "$displayName",
                authorization.RequestedName);
            insert.Parameters.AddWithValue(
                "$platform",
                authorization.RequestedPlatform);
            insert.Parameters.AddWithValue(
                "$fingerprint",
                authorization.DeviceFingerprint);
            insert.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            insert.Parameters.AddWithValue("$lastSeenAtUtc", now.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var updateDevice = connection.CreateCommand();
            updateDevice.Transaction = (SqliteTransaction)transaction;
            updateDevice.CommandText =
                """
                UPDATE client_devices
                SET display_name = $displayName,
                    platform = $platform,
                    last_seen_at_utc = $lastSeenAtUtc,
                    revoked_at_utc = NULL
                WHERE id = $id;
                """;
            updateDevice.Parameters.AddWithValue("$id", deviceId);
            updateDevice.Parameters.AddWithValue(
                "$displayName",
                authorization.RequestedName);
            updateDevice.Parameters.AddWithValue(
                "$platform",
                authorization.RequestedPlatform);
            updateDevice.Parameters.AddWithValue(
                "$lastSeenAtUtc",
                now.ToString("O"));
            await updateDevice.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var approve = connection.CreateCommand();
        approve.Transaction = (SqliteTransaction)transaction;
        approve.CommandText =
            """
            UPDATE device_authorizations
            SET account_id = $accountId,
                client_device_id = $clientDeviceId,
                approved_at_utc = $approvedAtUtc
            WHERE device_code_hash = $deviceCodeHash
              AND approved_at_utc IS NULL
              AND consumed_at_utc IS NULL
              AND denied_at_utc IS NULL;
            """;
        approve.Parameters.AddWithValue("$accountId", account.AccountId);
        approve.Parameters.AddWithValue("$clientDeviceId", deviceId);
        approve.Parameters.AddWithValue("$approvedAtUtc", now.ToString("O"));
        approve.Parameters.Add("$deviceCodeHash", SqliteType.Blob).Value =
            authorization.DeviceCodeHash;
        if (await approve.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return new ClientDeviceSummary
        {
            Id = deviceId,
            DisplayName = authorization.RequestedName,
            Platform = authorization.RequestedPlatform,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            LastSeenAtUtc = now
        };
    }

    public async Task<PendingDeviceAuthorization?> FindPendingAsync(
        string userCode,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var authorization = await ReadAuthorizationByUserCodeAsync(
            connection,
            (SqliteTransaction)transaction,
            userCode,
            cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
        return authorization is not null
               && authorization.ExpiresAtUtc > now
               && authorization.ApprovedAtUtc is null
               && authorization.ConsumedAtUtc is null
               && authorization.DeniedAtUtc is null
            ? new PendingDeviceAuthorization(
                authorization.RequestedName,
                authorization.RequestedPlatform,
                authorization.ExpiresAtUtc)
            : null;
    }

    public async Task<bool> DenyAsync(
        string userCode,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE device_authorizations
            SET denied_at_utc = $deniedAtUtc
            WHERE user_code_hash = $userCodeHash
              AND expires_at_utc > $now
              AND approved_at_utc IS NULL
              AND consumed_at_utc IS NULL
              AND denied_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "$deniedAtUtc",
            now.ToString("O"));
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.Add("$userCodeHash", SqliteType.Blob).Value =
            Hash(NormalizeUserCode(userCode));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<DeviceAuthorizationPollResult> PollAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        if (!IsBoundedToken(deviceCode, "hsd_"))
        {
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Invalid);
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var authorization = await ReadAuthorizationByDeviceCodeAsync(
            connection,
            (SqliteTransaction)transaction,
            deviceCode,
            cancellationToken);
        if (authorization is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Invalid);
        }

        if (authorization.ExpiresAtUtc <= now)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Expired);
        }

        if (authorization.DeniedAtUtc is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Denied);
        }

        if (authorization.ConsumedAtUtc is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Consumed);
        }

        if (authorization.ApprovedAtUtc is null
            || authorization.AccountId is null
            || authorization.ClientDeviceId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Pending);
        }

        var account = await _accounts.FindAsync(
            authorization.AccountId,
            cancellationToken);
        if (account is null || account.IsDisabled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Denied);
        }

        var issued = _sessions.Issue(
            account,
            authorization.ClientDeviceId);
        var refresh = CreateToken("hsr_");
        var refreshId = Guid.NewGuid().ToString("N");
        var refreshExpires = now.Add(RefreshTokenLifetime);
        await using var consume = connection.CreateCommand();
        consume.Transaction = (SqliteTransaction)transaction;
        consume.CommandText =
            """
            UPDATE device_authorizations
            SET consumed_at_utc = $consumedAtUtc
            WHERE device_code_hash = $deviceCodeHash
              AND consumed_at_utc IS NULL;
            """;
        consume.Parameters.AddWithValue("$consumedAtUtc", now.ToString("O"));
        consume.Parameters.Add("$deviceCodeHash", SqliteType.Blob).Value =
            authorization.DeviceCodeHash;
        if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            _sessions.Revoke(issued.AccessToken);
            await transaction.RollbackAsync(cancellationToken);
            return new DeviceAuthorizationPollResult(
                DeviceAuthorizationPollKind.Consumed);
        }

        await InsertRefreshTokenAsync(
            connection,
            (SqliteTransaction)transaction,
            refreshId,
            account.AccountId,
            authorization.ClientDeviceId,
            refresh,
            now,
            refreshExpires,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeviceAuthorizationPollResult(
            DeviceAuthorizationPollKind.Approved,
            ToSession(
                account,
                authorization.ClientDeviceId,
                issued,
                refresh,
                refreshExpires));
    }

    public async Task<CloudSessionResponse?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!IsBoundedToken(refreshToken, "hsr_"))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var record = await ReadRefreshTokenAsync(
            connection,
            (SqliteTransaction)transaction,
            refreshToken,
            cancellationToken);
        if (record is null
            || record.ExpiresAtUtc <= now
            || record.RevokedAtUtc is not null
            || record.DeviceRevokedAtUtc is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var account = await _accounts.FindAsync(
            record.AccountId,
            cancellationToken);
        if (account is null || account.IsDisabled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var replacement = CreateToken("hsr_");
        var replacementId = Guid.NewGuid().ToString("N");
        var replacementExpires = now.Add(RefreshTokenLifetime);
        await using var revoke = connection.CreateCommand();
        revoke.Transaction = (SqliteTransaction)transaction;
        revoke.CommandText =
            """
            UPDATE refresh_tokens
            SET revoked_at_utc = $revokedAtUtc,
                replaced_by_id = $replacementId
            WHERE id = $id AND revoked_at_utc IS NULL;
            """;
        revoke.Parameters.AddWithValue("$id", record.Id);
        revoke.Parameters.AddWithValue("$revokedAtUtc", now.ToString("O"));
        revoke.Parameters.AddWithValue("$replacementId", replacementId);
        if (await revoke.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await InsertRefreshTokenAsync(
            connection,
            (SqliteTransaction)transaction,
            replacementId,
            account.AccountId,
            record.ClientDeviceId,
            replacement,
            now,
            replacementExpires,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var issued = _sessions.Issue(account, record.ClientDeviceId);
        return ToSession(
            account,
            record.ClientDeviceId,
            issued,
            replacement,
            replacementExpires);
    }

    public async Task<IReadOnlyList<ClientDeviceSummary>> ListDevicesAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var normalized = ProfileServerIdentity.NormalizeAccountId(accountId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, display_name, platform, created_at_utc,
                   last_seen_at_utc, revoked_at_utc
            FROM client_devices
            WHERE account_id = $accountId
            ORDER BY last_seen_at_utc DESC;
            """;
        command.Parameters.AddWithValue("$accountId", normalized);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var devices = new List<ClientDeviceSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new ClientDeviceSummary
            {
                Id = reader.GetString(0),
                DisplayName = reader.GetString(1),
                Platform = reader.GetString(2),
                CreatedAtUtc = ParseTime(reader.GetString(3)),
                LastSeenAtUtc = ParseTime(reader.GetString(4)),
                RevokedAtUtc = reader.IsDBNull(5)
                    ? null
                    : ParseTime(reader.GetString(5))
            });
        }

        return devices;
    }

    public async Task<bool> RevokeDeviceAsync(
        string accountId,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var normalized = ProfileServerIdentity.NormalizeAccountId(accountId);
        var now = _timeProvider.GetUtcNow().ToString("O");
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var revokeDevice = connection.CreateCommand();
        revokeDevice.Transaction = (SqliteTransaction)transaction;
        revokeDevice.CommandText =
            """
            UPDATE client_devices
            SET revoked_at_utc = $revokedAtUtc
            WHERE id = $id AND account_id = $accountId
              AND revoked_at_utc IS NULL;
            """;
        revokeDevice.Parameters.AddWithValue("$id", deviceId);
        revokeDevice.Parameters.AddWithValue("$accountId", normalized);
        revokeDevice.Parameters.AddWithValue("$revokedAtUtc", now);
        var changed = await revokeDevice.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 1)
        {
            await using var revokeTokens = connection.CreateCommand();
            revokeTokens.Transaction = (SqliteTransaction)transaction;
            revokeTokens.CommandText =
                """
                UPDATE refresh_tokens
                SET revoked_at_utc = $revokedAtUtc
                WHERE client_device_id = $id AND revoked_at_utc IS NULL;
                """;
            revokeTokens.Parameters.AddWithValue("$id", deviceId);
            revokeTokens.Parameters.AddWithValue("$revokedAtUtc", now);
            await revokeTokens.ExecuteNonQueryAsync(cancellationToken);
            _sessions.RevokeClientDevice(normalized, deviceId);
        }

        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    private static async Task InsertRefreshTokenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        string accountId,
        string clientDeviceId,
        string token,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO refresh_tokens(
                id, account_id, client_device_id, token_hash,
                issued_at_utc, expires_at_utc)
            VALUES(
                $id, $accountId, $clientDeviceId, $tokenHash,
                $issuedAtUtc, $expiresAtUtc);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$clientDeviceId", clientDeviceId);
        command.Parameters.Add("$tokenHash", SqliteType.Blob).Value = Hash(token);
        command.Parameters.AddWithValue("$issuedAtUtc", issuedAt.ToString("O"));
        command.Parameters.AddWithValue("$expiresAtUtc", expiresAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DeviceAuthorizationRecord?>
        ReadAuthorizationByUserCodeAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string userCode,
            CancellationToken cancellationToken)
    {
        await using var command = CreateAuthorizationCommand(
            connection,
            transaction,
            "user_code_hash");
        command.Parameters.Add("$hash", SqliteType.Blob).Value =
            Hash(NormalizeUserCode(userCode));
        return await ReadAuthorizationAsync(command, cancellationToken);
    }

    private static async Task<DeviceAuthorizationRecord?>
        ReadAuthorizationByDeviceCodeAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string deviceCode,
            CancellationToken cancellationToken)
    {
        await using var command = CreateAuthorizationCommand(
            connection,
            transaction,
            "device_code_hash");
        command.Parameters.Add("$hash", SqliteType.Blob).Value = Hash(deviceCode);
        return await ReadAuthorizationAsync(command, cancellationToken);
    }

    private static SqliteCommand CreateAuthorizationCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string column)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT device_code_hash, account_id, client_device_id,
                   requested_name, requested_platform, device_fingerprint,
                   created_at_utc, expires_at_utc, approved_at_utc,
                   consumed_at_utc, denied_at_utc
            FROM device_authorizations
            WHERE {column} = $hash;
            """;
        return command;
    }

    private static async Task<DeviceAuthorizationRecord?> ReadAuthorizationAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeviceAuthorizationRecord(
            (byte[])reader[0],
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            ParseTime(reader.GetString(6)),
            ParseTime(reader.GetString(7)),
            reader.IsDBNull(8) ? null : ParseTime(reader.GetString(8)),
            reader.IsDBNull(9) ? null : ParseTime(reader.GetString(9)),
            reader.IsDBNull(10) ? null : ParseTime(reader.GetString(10)));
    }

    private static async Task<ClientDeviceSummary?> FindDeviceByFingerprintAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, display_name, platform, created_at_utc,
                   last_seen_at_utc, revoked_at_utc
            FROM client_devices
            WHERE account_id = $accountId AND device_fingerprint = $fingerprint;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClientDeviceSummary
        {
            Id = reader.GetString(0),
            DisplayName = reader.GetString(1),
            Platform = reader.GetString(2),
            CreatedAtUtc = ParseTime(reader.GetString(3)),
            LastSeenAtUtc = ParseTime(reader.GetString(4)),
            RevokedAtUtc = reader.IsDBNull(5)
                ? null
                : ParseTime(reader.GetString(5))
        };
    }

    private static async Task<RefreshTokenRecord?> ReadRefreshTokenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string token,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT r.id, r.account_id, r.client_device_id,
                   r.expires_at_utc, r.revoked_at_utc, d.revoked_at_utc
            FROM refresh_tokens r
            INNER JOIN client_devices d ON d.id = r.client_device_id
            WHERE r.token_hash = $tokenHash;
            """;
        command.Parameters.Add("$tokenHash", SqliteType.Blob).Value = Hash(token);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RefreshTokenRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            ParseTime(reader.GetString(3)),
            reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
            reader.IsDBNull(5) ? null : ParseTime(reader.GetString(5)));
    }

    private static CloudSessionResponse ToSession(
        ProfileAccountState account,
        string clientDeviceId,
        IssuedProfileSession issued,
        string refreshToken,
        DateTimeOffset refreshExpires) =>
        new()
        {
            AccountId = account.AccountId,
            AccountDisplayName = account.DisplayName,
            ClientDeviceId = clientDeviceId,
            AccessToken = issued.AccessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = issued.Principal.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshExpires
        };

    private static string ValidatePrintable(
        string value,
        int minimum,
        int maximum,
        string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length < minimum
            || normalized.Length > maximum
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"The {field} is malformed.");
        }

        return normalized;
    }

    private static string ValidateFingerprint(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 16 or > 128
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not ':'))
        {
            throw new ArgumentException("The device fingerprint is malformed.");
        }

        return normalized;
    }

    private static string CreateUserCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(8);
        var chars = bytes
            .Select(value => alphabet[value % alphabet.Length])
            .ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    private static string NormalizeUserCode(string value) =>
        (value ?? string.Empty)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Trim()
        .ToUpperInvariant();

    private static bool IsBoundedToken(string token, string prefix) =>
        !string.IsNullOrWhiteSpace(token)
        && token.StartsWith(prefix, StringComparison.Ordinal)
        && token.Length is >= 40 and <= 128
        && token.All(character => char.IsAsciiLetterOrDigit(character)
                                  || character is '_' or '-');

    private static string CreateToken(string prefix) =>
        prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    private static byte[] Hash(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private sealed record DeviceAuthorizationRecord(
        byte[] DeviceCodeHash,
        string? AccountId,
        string? ClientDeviceId,
        string RequestedName,
        string RequestedPlatform,
        string DeviceFingerprint,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ApprovedAtUtc,
        DateTimeOffset? ConsumedAtUtc,
        DateTimeOffset? DeniedAtUtc);

    private sealed record RefreshTokenRecord(
        string Id,
        string AccountId,
        string ClientDeviceId,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? RevokedAtUtc,
        DateTimeOffset? DeviceRevokedAtUtc);
}
