using Microsoft.Data.Sqlite;

namespace Hypershell.ProfileServer;

public sealed record AccountIdentityRenameResult(
    string PreviousAccountId,
    string AccountId,
    string DisplayName,
    int CredentialVersion,
    bool SecurityEnrollmentMigrated);

/// <summary>
/// Renames an account before any account-scoped cloud resources are created.
/// The profile server must be stopped while this maintenance operation runs.
/// </summary>
public sealed class AccountIdentityRenameService
{
    private readonly CloudMetadataStore _metadata;
    private readonly ProfileAccountStore _accounts;

    public AccountIdentityRenameService(
        CloudMetadataStore metadata,
        ProfileAccountStore accounts)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    }

    public async Task<AccountIdentityRenameResult> RenameBeforeUseAsync(
        string accountId,
        string newAccountId,
        CancellationToken cancellationToken = default)
    {
        var previousId = ProfileServerIdentity.NormalizeAccountId(accountId);
        var replacementId = ProfileServerIdentity.NormalizeAccountId(
            newAccountId);
        if (string.Equals(
                previousId,
                replacementId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The replacement account ID must differ from the current account ID.",
                nameof(newAccountId));
        }

        _ = await _accounts.FindAsync(previousId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Account '{previousId}' does not exist.");
        if (await _accounts.FindAsync(replacementId, cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                $"Account '{replacementId}' already exists.");
        }

        var legacyProfileDirectory = Path.Combine(
            _accounts.DataRoot,
            "profiles",
            ProfileServerIdentity.StorageKey(previousId));
        if (Directory.Exists(legacyProfileDirectory))
        {
            throw new InvalidOperationException(
                $"Account '{previousId}' already owns portable profile data. Rename it before uploading profiles.");
        }

        await _metadata.InitializeAsync(cancellationToken);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);

        var accountTables = await FindAccountScopedTablesAsync(
            connection,
            (SqliteTransaction)transaction,
            cancellationToken);
        foreach (var table in accountTables)
        {
            var replacementRows = await CountRowsAsync(
                connection,
                (SqliteTransaction)transaction,
                table,
                replacementId,
                cancellationToken);
            if (replacementRows != 0)
            {
                throw new InvalidOperationException(
                    $"The replacement account ID already owns data in '{table}'.");
            }

            if (string.Equals(
                    table,
                    "account_security",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existingRows = await CountRowsAsync(
                connection,
                (SqliteTransaction)transaction,
                table,
                previousId,
                cancellationToken);
            if (existingRows != 0)
            {
                throw new InvalidOperationException(
                    $"Account '{previousId}' already owns cloud data in '{table}'. Rename it before authorizing devices or creating cloud resources.");
            }
        }

        var migratedSecurityRows = await RenameSecurityEnrollmentAsync(
            connection,
            (SqliteTransaction)transaction,
            previousId,
            replacementId,
            cancellationToken);
        var registryRenamed = false;
        try
        {
            var renamed = await _accounts.RenameAsync(
                previousId,
                replacementId,
                cancellationToken);
            registryRenamed = true;
            await transaction.CommitAsync(cancellationToken);
            return new AccountIdentityRenameResult(
                previousId,
                renamed.AccountId,
                renamed.DisplayName,
                renamed.CredentialVersion,
                migratedSecurityRows == 1);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (!registryRenamed)
            {
                throw;
            }

            try
            {
                await _accounts.RenameAsync(
                    replacementId,
                    previousId,
                    CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "The account rename failed and its registry rollback also failed. Restore the most recent server backup before restarting the service.",
                    exception,
                    rollbackException);
            }

            throw;
        }
    }

    private static async Task<IReadOnlyList<string>> FindAccountScopedTablesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var candidates = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(reader.GetString(0));
        }

        await reader.DisposeAsync();
        foreach (var table in candidates)
        {
            await using var columns = connection.CreateCommand();
            columns.Transaction = transaction;
            columns.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)});";
            await using var columnReader = await columns.ExecuteReaderAsync(
                cancellationToken);
            while (await columnReader.ReadAsync(cancellationToken))
            {
                if (string.Equals(
                        columnReader.GetString(1),
                        "account_id",
                        StringComparison.OrdinalIgnoreCase))
                {
                    tables.Add(table);
                    break;
                }
            }
        }

        return tables;
    }

    private static async Task<long> CountRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT COUNT(*) FROM {QuoteIdentifier(table)} WHERE account_id = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> RenameSecurityEnrollmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string previousId,
        string replacementId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE account_security SET account_id = $replacementId, updated_at_utc = $updatedAtUtc WHERE account_id = $previousId;";
        command.Parameters.AddWithValue("$previousId", previousId);
        command.Parameters.AddWithValue("$replacementId", replacementId);
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
}
