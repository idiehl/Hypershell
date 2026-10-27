using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public sealed record RemoteHostMutationResult(
    SshVaultMutationKind Kind,
    RemoteHostSummary? Host = null);

public sealed partial class RemoteHostStore
{
    private readonly CloudMetadataStore _metadata;
    private readonly SshCredentialVaultStore _credentials;
    private readonly TimeProvider _timeProvider;

    public RemoteHostStore(
        CloudMetadataStore metadata,
        SshCredentialVaultStore credentials,
        TimeProvider timeProvider)
    {
        _metadata = metadata;
        _credentials = credentials;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RemoteHostSummary>> ListAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateSelectCommand(connection);
        command.CommandText +=
            " WHERE h.account_id = $accountId ORDER BY h.disabled_at_utc IS NOT NULL, h.display_name, h.id;";
        command.Parameters.AddWithValue("$accountId", account);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var hosts = new List<RemoteHostSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            hosts.Add(ReadSummary(reader));
        }

        return hosts;
    }

    public async Task<RemoteHostSummary?> FindAsync(
        string accountId,
        string hostId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateHostId(hostId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateSelectCommand(connection);
        command.CommandText +=
            " WHERE h.id = $id AND h.account_id = $accountId;";
        command.Parameters.AddWithValue("$id", hostId);
        command.Parameters.AddWithValue("$accountId", account);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadSummary(reader)
            : null;
    }

    public async Task<RemoteHostMutationResult> CreateAsync(
        string accountId,
        RemoteHostMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalized = await NormalizeRequestAsync(
            account,
            request,
            requireCreateRevision: true,
            cancellationToken);
        var hostId = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO remote_hosts(
                id, account_id, credential_id, display_name, host_name,
                port, user_name, operating_system, default_root,
                host_key_algorithm, host_key_fingerprint, notes,
                created_at_utc, updated_at_utc, disabled_at_utc, revision)
            VALUES(
                $id, $accountId, $credentialId, $displayName, $hostName,
                $port, $userName, $operatingSystem, $defaultRoot,
                NULL, NULL, $notes, $createdAtUtc, $updatedAtUtc, NULL, 1);
            """;
        AddMutationParameters(command, account, normalized, now);
        command.Parameters.AddWithValue("$id", hostId);
        command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await SshCredentialVaultStore.AppendAuditAsync(
            connection,
            account,
            clientDeviceId: null,
            hostId,
            EmptyToNull(normalized.CredentialId),
            "host.create",
            $"{normalized.DisplayName} ({normalized.HostName}:{normalized.Port})",
            "created",
            cancellationToken,
            (SqliteTransaction)transaction);
        await transaction.CommitAsync(cancellationToken);
        return new RemoteHostMutationResult(
            SshVaultMutationKind.Created,
            await FindAsync(account, hostId, cancellationToken));
    }

    public async Task<RemoteHostMutationResult> UpdateAsync(
        string accountId,
        string hostId,
        RemoteHostMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateHostId(hostId);
        var normalized = await NormalizeRequestAsync(
            account,
            request,
            requireCreateRevision: false,
            cancellationToken);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE remote_hosts
            SET credential_id = $credentialId,
                display_name = $displayName,
                host_name = $hostName,
                port = $port,
                user_name = $userName,
                operating_system = $operatingSystem,
                default_root = $defaultRoot,
                notes = $notes,
                updated_at_utc = $updatedAtUtc,
                revision = revision + 1
            WHERE id = $id AND account_id = $accountId
              AND revision = $expectedRevision
              AND disabled_at_utc IS NULL;
            """;
        AddMutationParameters(command, account, normalized, now);
        command.Parameters.AddWithValue("$id", hostId);
        command.Parameters.AddWithValue(
            "$expectedRevision",
            request.ExpectedRevision);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1)
        {
            return new RemoteHostMutationResult(
                await ExistsAsync(
                    connection,
                    account,
                    hostId,
                    cancellationToken)
                    ? SshVaultMutationKind.Conflict
                    : SshVaultMutationKind.NotFound);
        }

        await SshCredentialVaultStore.AppendAuditAsync(
            connection,
            account,
            clientDeviceId: null,
            hostId,
            EmptyToNull(normalized.CredentialId),
            "host.update",
            $"{normalized.DisplayName} ({normalized.HostName}:{normalized.Port})",
            "updated",
            cancellationToken,
            (SqliteTransaction)transaction);
        await transaction.CommitAsync(cancellationToken);
        return new RemoteHostMutationResult(
            SshVaultMutationKind.Updated,
            await FindAsync(account, hostId, cancellationToken));
    }

    public async Task<RemoteHostMutationResult> PinHostKeyAsync(
        string accountId,
        string hostId,
        RemoteHostKeyPinRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateHostId(hostId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw new ArgumentException(
                "The host-key request uses an unsupported protocol version.");
        }

        var algorithm = SshCredentialVaultStore.ValidatePrintable(
            request.Algorithm,
            3,
            80,
            "host-key algorithm");
        var fingerprint = NormalizeFingerprint(request.Fingerprint);
        var expected = string.IsNullOrWhiteSpace(
            request.ExpectedCurrentFingerprint)
            ? string.Empty
            : NormalizeFingerprint(request.ExpectedCurrentFingerprint);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var current = await ReadCurrentPinAsync(
            connection,
            (SqliteTransaction)transaction,
            account,
            hostId,
            cancellationToken);
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RemoteHostMutationResult(SshVaultMutationKind.NotFound);
        }

        if (current.Value.Disabled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RemoteHostMutationResult(SshVaultMutationKind.Conflict);
        }

        var currentFingerprint = current.Value.Fingerprint ?? string.Empty;
        if (string.Equals(
                currentFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RemoteHostMutationResult(
                SshVaultMutationKind.Updated,
                await FindAsync(account, hostId, cancellationToken));
        }

        if (!string.Equals(
                currentFingerprint,
                expected,
                StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RemoteHostMutationResult(SshVaultMutationKind.Conflict);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText =
            """
            UPDATE remote_hosts
            SET host_key_algorithm = $algorithm,
                host_key_fingerprint = $fingerprint,
                updated_at_utc = $updatedAtUtc,
                revision = revision + 1
            WHERE id = $id AND account_id = $accountId
              AND COALESCE(host_key_fingerprint, '') = $expected
              AND disabled_at_utc IS NULL;
            """;
        update.Parameters.AddWithValue("$algorithm", algorithm);
        update.Parameters.AddWithValue("$fingerprint", fingerprint);
        update.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
        update.Parameters.AddWithValue("$id", hostId);
        update.Parameters.AddWithValue("$accountId", account);
        update.Parameters.AddWithValue("$expected", expected);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RemoteHostMutationResult(SshVaultMutationKind.Conflict);
        }

        await SshCredentialVaultStore.AppendAuditAsync(
            connection,
            account,
            clientDeviceId: null,
            hostId,
            credentialId: null,
            string.IsNullOrEmpty(currentFingerprint)
                ? "host-key.pin"
                : "host-key.replace",
            hostId,
            fingerprint,
            cancellationToken,
            (SqliteTransaction)transaction);
        await transaction.CommitAsync(cancellationToken);
        return new RemoteHostMutationResult(
            SshVaultMutationKind.Updated,
            await FindAsync(account, hostId, cancellationToken));
    }

    public async Task<bool> DisableAsync(
        string accountId,
        string hostId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        ValidateHostId(hostId);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE remote_hosts
            SET disabled_at_utc = $now,
                updated_at_utc = $now,
                revision = revision + 1
            WHERE id = $id AND account_id = $accountId
              AND disabled_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$id", hostId);
        command.Parameters.AddWithValue("$accountId", account);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 1)
        {
            await SshCredentialVaultStore.AppendAuditAsync(
                connection,
                account,
                clientDeviceId: null,
                hostId,
                credentialId: null,
                "host.disable",
                hostId,
                "disabled",
                cancellationToken,
                (SqliteTransaction)transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    private async Task<NormalizedRemoteHost> NormalizeRequestAsync(
        string accountId,
        RemoteHostMutationRequest request,
        bool requireCreateRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw new ArgumentException(
                "The remote-host request uses an unsupported protocol version.");
        }

        if ((requireCreateRevision && request.ExpectedRevision != 0)
            || (!requireCreateRevision && request.ExpectedRevision <= 0))
        {
            throw new ArgumentException(
                "The remote-host revision precondition is invalid.");
        }

        var credentialId = request.CredentialId?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(credentialId))
        {
            SshCredentialVaultStore.ValidateCredentialId(credentialId);
            if (await _credentials.FindAsync(
                    accountId,
                    credentialId,
                    cancellationToken: cancellationToken) is null)
            {
                throw new ArgumentException(
                    "The selected SSH credential does not exist, is revoked, or belongs to another account.");
            }
        }

        if (!Enum.IsDefined(request.OperatingSystem))
        {
            throw new ArgumentException(
                "The remote-host operating system is invalid.");
        }

        var hostName = NormalizeHostName(request.HostName);
        if (request.Port is < 1 or > 65535)
        {
            throw new ArgumentException(
                "The SSH port must be between 1 and 65,535.");
        }

        return new NormalizedRemoteHost(
            SshCredentialVaultStore.ValidatePrintable(
                request.DisplayName,
                1,
                120,
                "host label"),
            hostName,
            request.Port,
            SshCredentialVaultStore.ValidatePrintable(
                request.UserName,
                1,
                128,
                "SSH user name"),
            request.OperatingSystem,
            NormalizeRoot(request.DefaultRoot, request.OperatingSystem),
            credentialId,
            NormalizeNotes(request.Notes));
    }

    private static SqliteCommand CreateSelectCommand(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT h.id, h.display_name, h.host_name, h.port, h.user_name,
                   h.operating_system, h.default_root, h.host_key_algorithm,
                   h.host_key_fingerprint, h.credential_id, h.notes,
                   h.revision, h.disabled_at_utc, h.updated_at_utc,
                   CASE WHEN c.id IS NOT NULL AND c.revoked_at_utc IS NULL
                        THEN 1 ELSE 0 END
            FROM remote_hosts h
            LEFT JOIN ssh_credentials c
              ON c.id = h.credential_id AND c.account_id = h.account_id
            """;
        return command;
    }

    private static RemoteHostSummary ReadSummary(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            DisplayName = reader.GetString(1),
            HostName = reader.GetString(2),
            Port = reader.GetInt32(3),
            UserName = reader.GetString(4),
            OperatingSystem = ParseOperatingSystem(reader.GetString(5)),
            DefaultRoot = reader.GetString(6),
            HostKeyAlgorithm = reader.IsDBNull(7)
                ? null
                : reader.GetString(7),
            HostKeyFingerprint = reader.IsDBNull(8)
                ? null
                : reader.GetString(8),
            CredentialId = reader.IsDBNull(9)
                ? string.Empty
                : reader.GetString(9),
            Notes = reader.GetString(10),
            Revision = reader.GetInt64(11),
            IsDisabled = !reader.IsDBNull(12),
            UpdatedAtUtc = SshCredentialVaultStore.ParseTime(
                reader.GetString(13)),
            HasCredential = reader.GetInt32(14) == 1
        };

    private static void AddMutationParameters(
        SqliteCommand command,
        string accountId,
        NormalizedRemoteHost host,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue(
            "$credentialId",
            (object?)EmptyToNull(host.CredentialId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$displayName", host.DisplayName);
        command.Parameters.AddWithValue("$hostName", host.HostName);
        command.Parameters.AddWithValue("$port", host.Port);
        command.Parameters.AddWithValue("$userName", host.UserName);
        command.Parameters.AddWithValue(
            "$operatingSystem",
            host.OperatingSystem.ToString());
        command.Parameters.AddWithValue("$defaultRoot", host.DefaultRoot);
        command.Parameters.AddWithValue("$notes", host.Notes);
        command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        string accountId,
        string hostId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM remote_hosts WHERE id = $id AND account_id = $accountId;";
        command.Parameters.AddWithValue("$id", hostId);
        command.Parameters.AddWithValue("$accountId", accountId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<(string? Fingerprint, bool Disabled)?>
        ReadCurrentPinAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string accountId,
            string hostId,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT host_key_fingerprint, disabled_at_utc
            FROM remote_hosts
            WHERE id = $id AND account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$id", hostId);
        command.Parameters.AddWithValue("$accountId", accountId);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.IsDBNull(0) ? null : reader.GetString(0),
               !reader.IsDBNull(1))
            : null;
    }

    private static string NormalizeHostName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is <= 0 or > 253
            || normalized.Any(character =>
                char.IsControl(character)
                || char.IsWhiteSpace(character)
                || character is '/' or '\\' or '@'))
        {
            throw new ArgumentException(
                "The SSH hostname or IP address is malformed.");
        }

        return normalized.StartsWith('[') && normalized.EndsWith(']')
            ? normalized[1..^1]
            : normalized;
    }

    private static string NormalizeRoot(
        string value,
        RemoteHostOperatingSystem operatingSystem)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is <= 0 or > 2048
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The default remote root is empty or malformed.");
        }

        var posix = normalized.StartsWith("/", StringComparison.Ordinal);
        var windows = WindowsRootPattern().IsMatch(normalized);
        if (operatingSystem is RemoteHostOperatingSystem.Linux
                or RemoteHostOperatingSystem.MacOs
            ? !posix
            : operatingSystem == RemoteHostOperatingSystem.Windows
                ? !windows
                : !posix && !windows)
        {
            throw new ArgumentException(
                "The default remote root must be absolute for the selected operating system.");
        }

        return normalized;
    }

    private static string NormalizeNotes(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 2000
            || normalized.Any(character => character == '\0'))
        {
            throw new ArgumentException(
                "Connection notes exceed their 2,000-character limit or contain a NUL character.");
        }

        return normalized;
    }

    private static string NormalizeFingerprint(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!Sha256FingerprintPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "SSH host-key fingerprints must use the SHA256:base64 format.");
        }

        return normalized;
    }

    private static RemoteHostOperatingSystem ParseOperatingSystem(string value) =>
        Enum.TryParse<RemoteHostOperatingSystem>(
            value,
            ignoreCase: false,
            out var parsed)
            ? parsed
            : RemoteHostOperatingSystem.Unknown;

    private static void ValidateHostId(string hostId)
    {
        if (hostId.Length != 32 || !hostId.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                "The remote-host ID is malformed.",
                nameof(hostId));
        }
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex("^[A-Za-z]:[\\\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsRootPattern();

    [GeneratedRegex(
        "^SHA256:[A-Za-z0-9+/]{43}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Sha256FingerprintPattern();

    private sealed record NormalizedRemoteHost(
        string DisplayName,
        string HostName,
        int Port,
        string UserName,
        RemoteHostOperatingSystem OperatingSystem,
        string DefaultRoot,
        string CredentialId,
        string Notes);
}
