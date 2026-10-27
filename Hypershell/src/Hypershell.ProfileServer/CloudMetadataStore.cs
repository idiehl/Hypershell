using Microsoft.Data.Sqlite;

namespace Hypershell.ProfileServer;

public sealed record CloudMetadataStatus(
    int SchemaVersion,
    string DatabaseFileName,
    bool WriteAheadLoggingEnabled);

/// <summary>
/// Owns the versioned SQLite metadata foundation for cloud resources that are
/// intentionally kept outside the bounded profile-v1 document store.
/// </summary>
public sealed class CloudMetadataStore : IDisposable
{
    public const int CurrentSchemaVersion = 4;

    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _initialized;

    public CloudMetadataStore(ProfileServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        DataRoot = normalized.DataRoot;
        _databasePath = Path.Combine(DataRoot, "cloud-metadata.sqlite3");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 15
        }.ToString();
    }

    public string DataRoot { get; }

    public string BlobRoot => Path.Combine(DataRoot, "blobs");

    public async Task<CloudMetadataStatus> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (!_initialized)
            {
                Directory.CreateDirectory(DataRoot);
                CreateContentDirectories();
                await using var connection = OpenConnection();
                await connection.OpenAsync(cancellationToken);
                await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA journal_mode=WAL;",
                    cancellationToken);
                await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=15000;",
                    cancellationToken);
                await ExecuteNonQueryAsync(
                    connection,
                    """
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        version INTEGER NOT NULL PRIMARY KEY,
                        name TEXT NOT NULL,
                        applied_at_utc TEXT NOT NULL
                    ) STRICT;
                    """,
                    cancellationToken);

                var version = await ReadSchemaVersionAsync(
                    connection,
                    cancellationToken);
                if (version > CurrentSchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Cloud metadata schema {version} is newer than supported schema {CurrentSchemaVersion}.");
                }

                if (version < 1)
                {
                    await ApplyMigrationOneAsync(connection, cancellationToken);
                    version = 1;
                }

                if (version < 2)
                {
                    await ApplyMigrationTwoAsync(connection, cancellationToken);
                    version = 2;
                }

                if (version < 3)
                {
                    await ApplyMigrationThreeAsync(connection, cancellationToken);
                }

                if (version < 4)
                {
                    await ApplyMigrationFourAsync(connection, cancellationToken);
                }

                RestrictDatabaseFilesToOwner();
                _initialized = true;
            }

            return await InspectAsync(cancellationToken);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<CloudMetadataStatus> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var version = await ReadSchemaVersionAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var journalMode = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken))
            ?? string.Empty;
        return new CloudMetadataStatus(
            version,
            Path.GetFileName(_databasePath),
            string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase));
    }

    public SqliteConnection OpenConnection() => new(_connectionString);

    private void CreateContentDirectories()
    {
        foreach (var directory in new[]
                 {
                     BlobRoot,
                     Path.Combine(BlobRoot, "conversations"),
                     Path.Combine(BlobRoot, "packages"),
                     Path.Combine(BlobRoot, "releases"),
                     Path.Combine(DataRoot, "exports")
                 })
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
            }
        }
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ApplyMigrationOneAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            CREATE TABLE account_security (
                account_id TEXT NOT NULL PRIMARY KEY,
                password_hash TEXT,
                encrypted_totp_secret BLOB,
                encrypted_recovery_codes BLOB,
                failed_sign_in_count INTEGER NOT NULL DEFAULT 0,
                locked_until_utc TEXT,
                updated_at_utc TEXT NOT NULL
            ) STRICT;

            CREATE TABLE client_devices (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                platform TEXT NOT NULL,
                device_fingerprint TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                revoked_at_utc TEXT,
                UNIQUE(account_id, device_fingerprint)
            ) STRICT;
            CREATE INDEX ix_client_devices_account
                ON client_devices(account_id, revoked_at_utc);

            CREATE TABLE refresh_tokens (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                client_device_id TEXT NOT NULL,
                token_hash BLOB NOT NULL,
                issued_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                revoked_at_utc TEXT,
                replaced_by_id TEXT,
                FOREIGN KEY(client_device_id) REFERENCES client_devices(id)
                    ON DELETE CASCADE
            ) STRICT;
            CREATE INDEX ix_refresh_tokens_device
                ON refresh_tokens(client_device_id, revoked_at_utc);

            CREATE TABLE device_authorizations (
                device_code_hash BLOB NOT NULL PRIMARY KEY,
                user_code_hash BLOB NOT NULL UNIQUE,
                account_id TEXT,
                client_device_id TEXT,
                requested_name TEXT NOT NULL,
                requested_platform TEXT NOT NULL,
                device_fingerprint TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                approved_at_utc TEXT,
                consumed_at_utc TEXT,
                denied_at_utc TEXT
            ) STRICT;

            CREATE TABLE ssh_credentials (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                encrypted_private_key BLOB NOT NULL,
                encrypted_passphrase BLOB,
                public_key_fingerprint TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                rotated_at_utc TEXT
            ) STRICT;
            CREATE INDEX ix_ssh_credentials_account
                ON ssh_credentials(account_id);

            CREATE TABLE remote_hosts (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                credential_id TEXT,
                display_name TEXT NOT NULL,
                host_name TEXT NOT NULL,
                port INTEGER NOT NULL CHECK(port BETWEEN 1 AND 65535),
                user_name TEXT NOT NULL,
                operating_system TEXT NOT NULL,
                default_root TEXT NOT NULL,
                host_key_algorithm TEXT,
                host_key_fingerprint TEXT,
                notes TEXT NOT NULL DEFAULT '',
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                disabled_at_utc TEXT,
                FOREIGN KEY(credential_id) REFERENCES ssh_credentials(id)
                    ON DELETE SET NULL
            ) STRICT;
            CREATE INDEX ix_remote_hosts_account
                ON remote_hosts(account_id, disabled_at_utc);

            CREATE TABLE profile_device_bindings (
                account_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                remote_host_id TEXT NOT NULL,
                is_default INTEGER NOT NULL DEFAULT 0 CHECK(is_default IN (0, 1)),
                PRIMARY KEY(account_id, profile_id, remote_host_id),
                FOREIGN KEY(remote_host_id) REFERENCES remote_hosts(id)
                    ON DELETE CASCADE
            ) STRICT;

            CREATE TABLE conversations (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                title TEXT NOT NULL,
                revision INTEGER NOT NULL,
                compacted_context TEXT,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                deleted_at_utc TEXT
            ) STRICT;
            CREATE INDEX ix_conversations_profile
                ON conversations(account_id, profile_id, updated_at_utc DESC);

            CREATE TABLE conversation_items (
                conversation_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                role TEXT NOT NULL,
                kind TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                PRIMARY KEY(conversation_id, sequence),
                FOREIGN KEY(conversation_id) REFERENCES conversations(id)
                    ON DELETE CASCADE
            ) STRICT;

            CREATE TABLE packages (
                content_hash TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                package_id TEXT NOT NULL,
                version TEXT NOT NULL,
                byte_length INTEGER NOT NULL,
                blob_relative_path TEXT NOT NULL,
                is_official INTEGER NOT NULL CHECK(is_official IN (0, 1)),
                created_at_utc TEXT NOT NULL,
                UNIQUE(account_id, package_id, version)
            ) STRICT;

            CREATE TABLE profile_packages (
                account_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                package_id TEXT NOT NULL,
                version TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                is_enabled INTEGER NOT NULL CHECK(is_enabled IN (0, 1)),
                PRIMARY KEY(account_id, profile_id, package_id),
                FOREIGN KEY(content_hash) REFERENCES packages(content_hash)
            ) STRICT;

            CREATE TABLE releases (
                version TEXT NOT NULL PRIMARY KEY,
                channel TEXT NOT NULL,
                git_commit TEXT NOT NULL,
                manifest_json TEXT NOT NULL,
                installer_blob_path TEXT NOT NULL,
                portable_blob_path TEXT,
                published_at_utc TEXT NOT NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0 CHECK(is_pinned IN (0, 1))
            ) STRICT;

            INSERT INTO schema_migrations(version, name, applied_at_utc)
                VALUES (1, 'cloud-resource-foundation', $appliedAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyMigrationTwoAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            CREATE TABLE workspace_profile_roaming (
                account_id TEXT NOT NULL,
                profile_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                server_revision INTEGER NOT NULL,
                client_revision INTEGER NOT NULL,
                document_json TEXT NOT NULL,
                content_sha256 TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                deleted_at_utc TEXT,
                PRIMARY KEY(account_id, profile_id)
            ) STRICT;
            CREATE INDEX ix_workspace_profile_roaming_account
                ON workspace_profile_roaming(account_id, deleted_at_utc,
                                             updated_at_utc DESC);

            CREATE TABLE package_blobs (
                content_hash TEXT NOT NULL PRIMARY KEY,
                byte_length INTEGER NOT NULL,
                blob_relative_path TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            ) STRICT;

            CREATE TABLE account_packages (
                account_id TEXT NOT NULL,
                package_id TEXT NOT NULL,
                version TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                is_official INTEGER NOT NULL CHECK(is_official IN (0, 1)),
                created_at_utc TEXT NOT NULL,
                PRIMARY KEY(account_id, package_id, version),
                FOREIGN KEY(content_hash) REFERENCES package_blobs(content_hash)
            ) STRICT;
            CREATE INDEX ix_account_packages_account
                ON account_packages(account_id, package_id, version);

            INSERT INTO schema_migrations(version, name, applied_at_utc)
                VALUES (2, 'roaming-conversations-and-package-vault',
                        $appliedAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyMigrationThreeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            ALTER TABLE ssh_credentials
                ADD COLUMN envelope_format INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE ssh_credentials
                ADD COLUMN key_version INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE ssh_credentials
                ADD COLUMN revoked_at_utc TEXT;

            ALTER TABLE remote_hosts
                ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

            CREATE TABLE credential_release_authorizations (
                release_token_hash BLOB NOT NULL PRIMARY KEY,
                user_code_hash BLOB NOT NULL UNIQUE,
                account_id TEXT NOT NULL,
                credential_id TEXT NOT NULL,
                client_device_id TEXT NOT NULL,
                expected_key_version INTEGER NOT NULL,
                requested_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                approved_at_utc TEXT,
                consumed_at_utc TEXT,
                denied_at_utc TEXT,
                FOREIGN KEY(credential_id) REFERENCES ssh_credentials(id)
                    ON DELETE CASCADE,
                FOREIGN KEY(client_device_id) REFERENCES client_devices(id)
                    ON DELETE CASCADE
            ) STRICT;
            CREATE INDEX ix_credential_release_account
                ON credential_release_authorizations(
                    account_id, client_device_id, expires_at_utc);

            CREATE TABLE ssh_audit_events (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NOT NULL,
                client_device_id TEXT,
                remote_host_id TEXT,
                credential_id TEXT,
                action TEXT NOT NULL,
                target_summary TEXT NOT NULL,
                result TEXT NOT NULL,
                conversation_id TEXT,
                created_at_utc TEXT NOT NULL
            ) STRICT;
            CREATE INDEX ix_ssh_audit_account
                ON ssh_audit_events(account_id, created_at_utc DESC);

            INSERT INTO schema_migrations(version, name, applied_at_utc)
                VALUES (3, 'ssh-host-registry-and-envelope-vault',
                        $appliedAtUtc);
            """;
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyMigrationFourAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            CREATE TABLE release_companions (
                version TEXT NOT NULL PRIMARY KEY,
                manifest_json TEXT NOT NULL,
                installer_blob_path TEXT NOT NULL,
                FOREIGN KEY(version) REFERENCES releases(version) ON DELETE CASCADE
            ) STRICT;
            INSERT INTO schema_migrations(version, name, applied_at_utc)
                VALUES (4, 'public-complete-and-core-releases', $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void RestrictDatabaseFilesToOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var path in new[]
                 {
                     _databasePath,
                     _databasePath + "-wal",
                     _databasePath + "-shm"
                 })
        {
            if (File.Exists(path))
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _initializationGate.Dispose();
    }
}
