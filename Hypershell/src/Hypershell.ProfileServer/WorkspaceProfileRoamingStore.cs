using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public enum WorkspaceProfileRoamingMutationKind
{
    Created,
    Updated,
    Deleted,
    Conflict
}

public sealed record WorkspaceProfileRoamingMutation(
    WorkspaceProfileRoamingMutationKind Kind,
    WorkspaceProfileRoamingSummary? Profile = null);

public sealed partial class WorkspaceProfileRoamingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    private static readonly HashSet<string> ReasoningEfforts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "none", "low", "medium", "high", "xhigh", "max"
        };

    private static readonly HashSet<string> TextVerbosities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "low", "medium", "high"
        };

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResourceIdPattern();

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    private readonly CloudMetadataStore _metadata;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public WorkspaceProfileRoamingStore(CloudMetadataStore metadata)
    {
        _metadata = metadata;
    }

    public async Task<IReadOnlyList<WorkspaceProfileRoamingSummary>> ListAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT profile_id, display_name, server_revision,
                   client_revision, content_sha256, created_at_utc,
                   updated_at_utc, deleted_at_utc
            FROM workspace_profile_roaming
            WHERE account_id = $account
            ORDER BY deleted_at_utc IS NOT NULL, display_name, profile_id;
            """;
        command.Parameters.AddWithValue("$account", account);
        var profiles = new List<WorkspaceProfileRoamingSummary>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(ReadSummary(reader));
        }

        return profiles;
    }

    public async Task<WorkspaceProfileRoamingResource?> GetAsync(
        string accountId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var row = await ReadRowAsync(
            ProfileServerIdentity.NormalizeAccountId(accountId),
            ProfileServerIdentity.ValidateProfileId(profileId),
            cancellationToken);
        return row is null ? null : Materialize(row);
    }

    public async Task<WorkspaceProfileRoamingMutation> PutAsync(
        string accountId,
        string profileId,
        WorkspaceProfileRoamingDocument document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var profile = ProfileServerIdentity.ValidateProfileId(profileId);
        Validate(document, profile);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > HypershellCloudContract.MaximumWorkspaceProfileBytes)
        {
            throw new InvalidDataException(
                "The roaming workspace profile exceeds its protocol limit.");
        }

        var contentSha256 = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken);
            var current = await ReadRowAsync(
                connection,
                transaction,
                account,
                profile,
                cancellationToken);
            if (expectedETag is null
                ? current is not null
                : current is null
                  || !string.Equals(
                      CreateETag(current),
                      expectedETag,
                      StringComparison.Ordinal))
            {
                return new WorkspaceProfileRoamingMutation(
                    WorkspaceProfileRoamingMutationKind.Conflict);
            }

            var now = DateTimeOffset.UtcNow;
            var revision = checked((current?.ServerRevision ?? 0) + 1);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO workspace_profile_roaming(
                    account_id, profile_id, display_name, server_revision,
                    client_revision, document_json, content_sha256,
                    created_at_utc, updated_at_utc, deleted_at_utc)
                VALUES($account, $profile, $displayName, $serverRevision,
                       $clientRevision, $documentJson, $contentSha256,
                       $createdAtUtc, $updatedAtUtc, NULL)
                ON CONFLICT(account_id, profile_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    server_revision = excluded.server_revision,
                    client_revision = excluded.client_revision,
                    document_json = excluded.document_json,
                    content_sha256 = excluded.content_sha256,
                    updated_at_utc = excluded.updated_at_utc,
                    deleted_at_utc = NULL;
                """;
            command.Parameters.AddWithValue("$account", account);
            command.Parameters.AddWithValue("$profile", profile);
            command.Parameters.AddWithValue(
                "$displayName",
                document.DisplayName.Trim());
            command.Parameters.AddWithValue("$serverRevision", revision);
            command.Parameters.AddWithValue(
                "$clientRevision",
                document.ClientRevision);
            command.Parameters.AddWithValue("$documentJson", json);
            command.Parameters.AddWithValue("$contentSha256", contentSha256);
            command.Parameters.AddWithValue(
                "$createdAtUtc",
                (current?.CreatedAtUtc ?? now).ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var stored = await GetAsync(account, profile, cancellationToken)
                         ?? throw new InvalidDataException(
                             "The roaming profile could not be read after storage.");
            return new WorkspaceProfileRoamingMutation(
                current is null
                    ? WorkspaceProfileRoamingMutationKind.Created
                    : WorkspaceProfileRoamingMutationKind.Updated,
                stored.Profile);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<WorkspaceProfileRoamingMutation> DeleteAsync(
        string accountId,
        string profileId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var profile = ProfileServerIdentity.ValidateProfileId(profileId);
        ValidateStrongETag(expectedETag);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken);
            var current = await ReadRowAsync(
                connection,
                transaction,
                account,
                profile,
                cancellationToken);
            if (current is null
                || !string.Equals(
                    CreateETag(current),
                    expectedETag,
                    StringComparison.Ordinal))
            {
                return new WorkspaceProfileRoamingMutation(
                    WorkspaceProfileRoamingMutationKind.Conflict);
            }

            var now = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE workspace_profile_roaming
                SET server_revision = $revision,
                    updated_at_utc = $updatedAtUtc,
                    deleted_at_utc = $deletedAtUtc
                WHERE account_id = $account AND profile_id = $profile;
                """;
            command.Parameters.AddWithValue(
                "$revision",
                checked(current.ServerRevision + 1));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$deletedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$account", account);
            command.Parameters.AddWithValue("$profile", profile);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var stored = await GetAsync(account, profile, cancellationToken)
                         ?? throw new InvalidDataException(
                             "The roaming profile tombstone could not be read.");
            return new WorkspaceProfileRoamingMutation(
                WorkspaceProfileRoamingMutationKind.Deleted,
                stored.Profile);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<WorkspaceProfileRow?> ReadRowAsync(
        string account,
        string profile,
        CancellationToken cancellationToken)
    {
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        return await ReadRowAsync(
            connection,
            transaction: null,
            account,
            profile,
            cancellationToken);
    }

    private static async Task<WorkspaceProfileRow?> ReadRowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string account,
        string profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT profile_id, display_name, server_revision,
                   client_revision, document_json, content_sha256,
                   created_at_utc, updated_at_utc, deleted_at_utc
            FROM workspace_profile_roaming
            WHERE account_id = $account AND profile_id = $profile;
            """;
        command.Parameters.AddWithValue("$account", account);
        command.Parameters.AddWithValue("$profile", profile);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRow(reader)
            : null;
    }

    private static WorkspaceProfileRoamingResource Materialize(
        WorkspaceProfileRow row)
    {
        var bytes = Encoding.UTF8.GetBytes(row.DocumentJson);
        var hash = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        if (!string.Equals(
                hash,
                row.ContentSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Stored roaming-profile content failed its integrity check.");
        }

        var document = JsonSerializer.Deserialize<
                           WorkspaceProfileRoamingDocument>(
                           bytes,
                           JsonOptions)
                       ?? throw new InvalidDataException(
                           "Stored roaming-profile content was empty.");
        Validate(document, row.ProfileId);
        return new WorkspaceProfileRoamingResource
        {
            Profile = ToSummary(row),
            Document = row.DeletedAtUtc is null ? document : null
        };
    }

    private static WorkspaceProfileRoamingSummary ToSummary(
        WorkspaceProfileRow row) =>
        new()
        {
            ProfileId = row.ProfileId,
            DisplayName = row.DisplayName,
            ServerRevision = row.ServerRevision,
            ClientRevision = row.ClientRevision,
            UpdatedAtUtc = row.UpdatedAtUtc,
            ContentSha256 = row.ContentSha256,
            ETag = CreateETag(row),
            IsDeleted = row.DeletedAtUtc is not null
        };

    private static WorkspaceProfileRoamingSummary ReadSummary(
        SqliteDataReader reader) => ToSummary(new WorkspaceProfileRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            string.Empty,
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7)
                ? null
                : DateTimeOffset.Parse(reader.GetString(7))));

    private static WorkspaceProfileRow ReadRow(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8)
                ? null
                : DateTimeOffset.Parse(reader.GetString(8)));

    private static string CreateETag(WorkspaceProfileRow row) =>
        $"\"r{row.ServerRevision:x}-{row.ContentSha256}-{(row.DeletedAtUtc is null ? 'a' : 'd')}\"";

    private static void Validate(
        WorkspaceProfileRoamingDocument document,
        string expectedProfileId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion
            || !string.Equals(
                document.ProfileId,
                expectedProfileId,
                StringComparison.OrdinalIgnoreCase)
            || document.ClientRevision < 0
            || document.ClientUpdatedAtUtc == default
            || document.ClientUpdatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10)
            || !ValidText(document.DisplayName, 120)
            || document.AiContexts.Count
                > HypershellCloudContract.MaximumAiContextsPerProfile
            || document.Projects.Count
                > HypershellCloudContract.MaximumProjectsPerProfile
            || document.Packages.Count
                > HypershellCloudContract.MaximumPackagesPerProfile)
        {
            throw new InvalidDataException(
                "The roaming workspace profile has invalid identity, revision, time, or collection metadata.");
        }

        ValidateUnique(document.AiContexts, context => context.Id, "AI context");
        foreach (var context in document.AiContexts)
        {
            if (!ResourceIdPattern().IsMatch(context.Id)
                || !ValidText(context.Name, 120)
                || !ValidText(context.Model, 120)
                || !ReasoningEfforts.Contains(context.ReasoningEffort)
                || !TextVerbosities.Contains(context.TextVerbosity)
                || !Enum.IsDefined(context.ComputerControl))
            {
                throw new InvalidDataException(
                    "A roaming AI context contains invalid identity or behavior metadata.");
            }

            foreach (var permission in new[]
                     {
                         context.Read,
                         context.Create,
                         context.Edit,
                         context.Delete,
                         context.Terminal
                     })
            {
                ValidateWorkspaceUris(permission.WorkspaceUris);
                if (permission.AllowFullAccess
                    && permission.WorkspaceUris.Count > 0)
                {
                    throw new InvalidDataException(
                        "A full-access AI permission cannot also contain workspace URI scopes.");
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    context.DefaultSaveWorkspaceUri))
            {
                ValidateWorkspaceUris([context.DefaultSaveWorkspaceUri]);
            }
        }

        if (document.AiContexts.Count > 0
            && !document.AiContexts.Any(context => string.Equals(
                context.Id,
                document.SelectedAiContextId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The selected AI context is not included in the roaming profile.");
        }

        ValidateUnique(document.Projects, project => project.Id, "project");
        foreach (var project in document.Projects)
        {
            if (!ResourceIdPattern().IsMatch(project.Id)
                || !ValidText(project.DisplayName, 120)
                || !ValidText(project.DefaultBranch, 200, allowEmpty: true))
            {
                throw new InvalidDataException(
                    "A roaming project contains invalid identity metadata.");
            }

            if (!string.IsNullOrWhiteSpace(project.WorkspaceLocationUri))
            {
                ValidateWorkspaceUris([project.WorkspaceLocationUri]);
            }

            ValidateOptionalGitUri(project.GitOriginUri);
        }

        ValidateUnique(
            document.Packages,
            package => package.PackageId,
            "Hyperlib package selection");
        foreach (var package in document.Packages)
        {
            if (!ResourceIdPattern().IsMatch(package.PackageId)
                || !VersionPattern().IsMatch(package.Version)
                || package.ContentSha256.Length != 64
                || !package.ContentSha256.All(char.IsAsciiHexDigit)
                || !string.Equals(
                    package.ContentSha256,
                    package.ContentSha256.ToLowerInvariant(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A Hyperlib package selection contains invalid identity metadata.");
            }
        }
    }

    private static void ValidateWorkspaceUris(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!seen.Add(value)
                || value.Length > 2048
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("ssh" or "workspace")
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidDataException(
                    "A roaming workspace URI is malformed or device-local.");
            }
        }
    }

    private static void ValidateOptionalGitUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Length > 2048
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("ssh" or "https" or "git")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException(
                "A roaming Git origin URI is malformed or contains credentials.");
        }
    }

    private static void ValidateStrongETag(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            || value.Length is < 3 or > 258
            || value[0] != '"'
            || value[^1] != '"'
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "A strong roaming-profile ETag is required.");
        }
    }

    private static bool ValidText(
        string value,
        int maximumLength,
        bool allowEmpty = false) =>
        value is not null
        && value.Length <= maximumLength
        && (allowEmpty || !string.IsNullOrWhiteSpace(value))
        && !value.Any(character =>
            character == '\0'
            || char.IsControl(character)
               && character is not ('\r' or '\n' or '\t'));

    private static void ValidateUnique<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string label)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values.Any(value => !keys.Add(keySelector(value))))
        {
            throw new InvalidDataException(
                $"The roaming profile contains a duplicate {label}.");
        }
    }

    private sealed record WorkspaceProfileRow(
        string ProfileId,
        string DisplayName,
        long ServerRevision,
        long ClientRevision,
        string DocumentJson,
        string ContentSha256,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? DeletedAtUtc);
}
