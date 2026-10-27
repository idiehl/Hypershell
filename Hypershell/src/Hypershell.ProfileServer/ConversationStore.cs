using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public enum ConversationMutationKind
{
    Created,
    Updated,
    Deleted,
    Conflict
}

public sealed record ConversationMutation(
    ConversationMutationKind Kind,
    AiConversationSummary? Conversation = null);

public sealed partial class ConversationStore
{
    private const int MaximumListPageSize = 100;
    private const int MaximumActionSummaryCharacters = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResourceIdPattern();

    [GeneratedRegex(
        "(?:sk|hps)_[A-Za-z0-9_-]{20,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(
        "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----.*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PrivateKeyPattern();

    private readonly CloudMetadataStore _metadata;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public ConversationStore(CloudMetadataStore metadata)
    {
        _metadata = metadata;
    }

    public async Task<AiConversationListPage> ListAsync(
        string accountId,
        string? profileId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var profile = string.IsNullOrWhiteSpace(profileId)
            ? null
            : ProfileServerIdentity.ValidateProfileId(profileId);
        var boundedPageSize = Math.Clamp(pageSize, 1, MaximumListPageSize);
        var parsedCursor = DecodeCursor(cursor);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, c.profile_id, c.title, c.revision,
                   COALESCE(MAX(i.sequence), 0), c.created_at_utc,
                   c.updated_at_utc, c.deleted_at_utc
            FROM conversations c
            LEFT JOIN conversation_items i ON i.conversation_id = c.id
            WHERE c.account_id = $account
              AND ($profile IS NULL OR c.profile_id = $profile)
              AND ($cursorTime IS NULL
                   OR c.updated_at_utc < $cursorTime
                   OR (c.updated_at_utc = $cursorTime AND c.id < $cursorId))
            GROUP BY c.id
            ORDER BY c.updated_at_utc DESC, c.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account", account);
        command.Parameters.AddWithValue(
            "$profile",
            (object?)profile ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$cursorTime",
            parsedCursor is null
                ? DBNull.Value
                : parsedCursor.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$cursorId",
            parsedCursor?.Id ?? string.Empty);
        command.Parameters.AddWithValue("$limit", boundedPageSize + 1);
        var summaries = new List<AiConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(ReadSummary(reader));
        }

        var hasMore = summaries.Count > boundedPageSize;
        if (hasMore)
        {
            summaries.RemoveAt(summaries.Count - 1);
        }

        var nextCursor = hasMore && summaries.Count > 0
            ? EncodeCursor(new ConversationCursor(
                summaries[^1].UpdatedAtUtc,
                summaries[^1].Id))
            : string.Empty;
        return new AiConversationListPage
        {
            Conversations = summaries,
            NextCursor = nextCursor
        };
    }

    public async Task<AiConversationPage?> GetPageAsync(
        string accountId,
        string conversationId,
        long afterSequence,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var id = ValidateConversationId(conversationId);
        if (afterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        }

        var boundedPageSize = Math.Clamp(
            pageSize,
            1,
            HypershellCloudContract.MaximumConversationItemsPerPage);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await ReadConversationRowAsync(
            connection,
            transaction: null,
            account,
            id,
            cancellationToken);
        if (row is null)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT sequence, payload_json
            FROM conversation_items
            WHERE conversation_id = $id AND sequence > $after
            ORDER BY sequence
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$after", afterSequence);
        command.Parameters.AddWithValue("$limit", boundedPageSize + 1);
        var items = new List<AiConversationItem>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sequence = reader.GetInt64(0);
            var input = DeserializeItem(reader.GetString(1));
            items.Add(ToStoredItem(sequence, input));
        }

        var hasMore = items.Count > boundedPageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new AiConversationPage
        {
            Conversation = ToSummary(row),
            CompactedContext = row.CompactedContext,
            Items = items,
            NextAfterSequence = hasMore && items.Count > 0
                ? items[^1].Sequence
                : null
        };
    }

    public async Task<ConversationMutation> PutAsync(
        string accountId,
        string conversationId,
        AiConversationMutationRequest request,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var id = ValidateConversationId(conversationId);
        var normalized = Normalize(request);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken);
            var current = await ReadConversationRowAsync(
                connection,
                transaction,
                account,
                id,
                cancellationToken);
            var belongsToAnotherAccount = current is null
                                          && await ConversationIdExistsAsync(
                                              connection,
                                              transaction,
                                              id,
                                              cancellationToken);
            if (expectedETag is null
                ? current is not null || belongsToAnotherAccount
                : current is null
                  || !string.Equals(
                      CreateETag(current),
                      expectedETag,
                      StringComparison.Ordinal))
            {
                return new ConversationMutation(
                    ConversationMutationKind.Conflict);
            }

            if (current is not null
                && !string.Equals(
                    current.ProfileId,
                    normalized.ProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "A conversation cannot move between workspace profiles.");
            }

            var now = DateTimeOffset.UtcNow;
            var revision = checked((current?.Revision ?? 0) + 1);
            var compactedContext = string.IsNullOrEmpty(
                normalized.CompactedContext)
                ? current?.CompactedContext ?? string.Empty
                : normalized.CompactedContext;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = current is null
                    ? """
                    INSERT INTO conversations(
                        id, account_id, profile_id, title, revision,
                        compacted_context, created_at_utc, updated_at_utc,
                        deleted_at_utc)
                    VALUES($id, $account, $profile, $title, $revision,
                           $compacted, $createdAtUtc, $updatedAtUtc, NULL);
                    """
                    : """
                    UPDATE conversations
                    SET title = $title,
                        revision = $revision,
                        compacted_context = $compacted,
                        updated_at_utc = $updatedAtUtc,
                        deleted_at_utc = NULL
                    WHERE id = $id AND account_id = $account;
                    """;
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$account", account);
                command.Parameters.AddWithValue(
                    "$profile",
                    normalized.ProfileId);
                command.Parameters.AddWithValue("$title", normalized.Title);
                command.Parameters.AddWithValue("$revision", revision);
                command.Parameters.AddWithValue("$compacted", compactedContext);
                command.Parameters.AddWithValue(
                    "$createdAtUtc",
                    (current?.CreatedAtUtc ?? now).ToString("O"));
                command.Parameters.AddWithValue(
                    "$updatedAtUtc",
                    now.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var nextSequence = current?.LastSequence ?? 0;
            foreach (var item in normalized.Items)
            {
                nextSequence = checked(nextSequence + 1);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO conversation_items(
                        conversation_id, sequence, role, kind,
                        payload_json, created_at_utc)
                    VALUES($conversation, $sequence, $role, $kind,
                           $payload, $createdAtUtc);
                    """;
                command.Parameters.AddWithValue("$conversation", id);
                command.Parameters.AddWithValue("$sequence", nextSequence);
                command.Parameters.AddWithValue("$role", item.Role.ToString());
                command.Parameters.AddWithValue("$kind", item.Kind.ToString());
                command.Parameters.AddWithValue(
                    "$payload",
                    JsonSerializer.Serialize(item, JsonOptions));
                command.Parameters.AddWithValue(
                    "$createdAtUtc",
                    item.CreatedAtUtc.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var stored = await ReadConversationAsync(
                account,
                id,
                cancellationToken)
                         ?? throw new InvalidDataException(
                             "The conversation could not be read after storage.");
            return new ConversationMutation(
                current is null
                    ? ConversationMutationKind.Created
                    : ConversationMutationKind.Updated,
                ToSummary(stored));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ConversationMutation> DeleteAsync(
        string accountId,
        string conversationId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var id = ValidateConversationId(conversationId);
        ValidateStrongETag(expectedETag);
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken);
            var current = await ReadConversationRowAsync(
                connection,
                transaction,
                account,
                id,
                cancellationToken);
            if (current is null
                || !string.Equals(
                    CreateETag(current),
                    expectedETag,
                    StringComparison.Ordinal))
            {
                return new ConversationMutation(
                    ConversationMutationKind.Conflict);
            }

            var now = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE conversations
                SET revision = $revision,
                    updated_at_utc = $updatedAtUtc,
                    deleted_at_utc = $deletedAtUtc
                WHERE id = $id AND account_id = $account;
                """;
            command.Parameters.AddWithValue(
                "$revision",
                checked(current.Revision + 1));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$deletedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$account", account);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var stored = await ReadConversationAsync(
                account,
                id,
                cancellationToken)
                         ?? throw new InvalidDataException(
                             "The conversation tombstone could not be read.");
            return new ConversationMutation(
                ConversationMutationKind.Deleted,
                ToSummary(stored));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<ConversationRow?> ReadConversationAsync(
        string account,
        string id,
        CancellationToken cancellationToken)
    {
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        return await ReadConversationRowAsync(
            connection,
            transaction: null,
            account,
            id,
            cancellationToken);
    }

    private static async Task<ConversationRow?> ReadConversationRowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string account,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT c.id, c.profile_id, c.title, c.revision,
                   c.compacted_context, c.created_at_utc,
                   c.updated_at_utc, c.deleted_at_utc,
                   COALESCE(MAX(i.sequence), 0)
            FROM conversations c
            LEFT JOIN conversation_items i ON i.conversation_id = c.id
            WHERE c.id = $id AND c.account_id = $account
            GROUP BY c.id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$account", account);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRow(reader)
            : null;
    }

    private static async Task<bool> ConversationIdExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM conversations WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static ConversationRow ReadRow(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7)
                ? null
                : DateTimeOffset.Parse(reader.GetString(7)),
            reader.GetInt64(8));

    private static AiConversationSummary ReadSummary(SqliteDataReader reader) =>
        ToSummary(new ConversationRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            string.Empty,
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7)
                ? null
                : DateTimeOffset.Parse(reader.GetString(7)),
            reader.GetInt64(4)));

    private static AiConversationSummary ToSummary(ConversationRow row) =>
        new()
        {
            Id = row.Id,
            ProfileId = row.ProfileId,
            Title = row.Title,
            Revision = row.Revision,
            LastSequence = row.LastSequence,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            ETag = CreateETag(row),
            IsDeleted = row.DeletedAtUtc is not null
        };

    private static string CreateETag(ConversationRow row) =>
        $"\"r{row.Revision:x}-s{row.LastSequence:x}-{(row.DeletedAtUtc is null ? 'a' : 'd')}\"";

    private static AiConversationMutationRequest Normalize(
        AiConversationMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion
            || request.Items.Count
                > HypershellCloudContract.MaximumConversationItemsPerMutation)
        {
            throw new InvalidDataException(
                "The conversation mutation version or item count is invalid.");
        }

        var profileId = ProfileServerIdentity.ValidateProfileId(
            request.ProfileId);
        var title = NormalizeText(request.Title, 200, allowEmpty: false);
        var compacted = SanitizeText(
            request.CompactedContext,
            HypershellCloudContract.MaximumConversationCompactionCharacters,
            allowEmpty: true);
        if (compacted.Contains(
                "data:image/",
                StringComparison.OrdinalIgnoreCase)
            || compacted.Contains(
                "\"image_url\"",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Conversation compaction cannot contain screenshot or image payloads.");
        }

        var items = request.Items.Select(NormalizeItem).ToList();
        var normalized = new AiConversationMutationRequest
        {
            ProfileId = profileId,
            Title = title,
            CompactedContext = compacted,
            Items = items
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            normalized,
            JsonOptions);
        if (bytes.Length
            > HypershellCloudContract.MaximumConversationMutationBytes)
        {
            throw new InvalidDataException(
                "The conversation mutation exceeds its protocol limit.");
        }

        return normalized;
    }

    private static AiConversationItemInput NormalizeItem(
        AiConversationItemInput item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Enum.IsDefined(item.Role)
            || !Enum.IsDefined(item.Kind)
            || item.CreatedAtUtc == default
            || item.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10))
        {
            throw new InvalidDataException(
                "A conversation item has invalid role, kind, or time metadata.");
        }

        var textLimit = item.Kind == AiConversationItemKind.Message
            ? HypershellCloudContract.MaximumConversationTextCharacters
            : MaximumActionSummaryCharacters;
        var text = SanitizeText(item.Text, textLimit, allowEmpty: false);
        var actionName = NormalizeText(
            item.ActionName,
            120,
            allowEmpty: item.Kind != AiConversationItemKind.ActionSummary);
        var result = SanitizeText(
            item.Result,
            MaximumActionSummaryCharacters,
            allowEmpty: true);
        var targetUri = NormalizeTargetUri(item.TargetUri);
        var contentSha256 = NormalizeOptionalSha256(item.ContentSha256);
        if (item.Kind == AiConversationItemKind.FileReference
            && string.IsNullOrWhiteSpace(targetUri))
        {
            throw new InvalidDataException(
                "A file-reference conversation item requires a target URI.");
        }

        return new AiConversationItemInput
        {
            Role = item.Role,
            Kind = item.Kind,
            Text = text,
            ActionName = actionName,
            TargetUri = targetUri,
            Result = result,
            ContentSha256 = contentSha256,
            CreatedAtUtc = item.CreatedAtUtc.ToUniversalTime()
        };
    }

    private static AiConversationItemInput DeserializeItem(string json)
    {
        var item = JsonSerializer.Deserialize<AiConversationItemInput>(
                       json,
                       JsonOptions)
                   ?? throw new InvalidDataException(
                       "A stored conversation item was empty.");
        return NormalizeItem(item);
    }

    private static AiConversationItem ToStoredItem(
        long sequence,
        AiConversationItemInput input) =>
        new()
        {
            Sequence = sequence,
            Role = input.Role,
            Kind = input.Kind,
            Text = input.Text,
            ActionName = input.ActionName,
            TargetUri = input.TargetUri,
            Result = input.Result,
            ContentSha256 = input.ContentSha256,
            CreatedAtUtc = input.CreatedAtUtc
        };

    private static string SanitizeText(
        string value,
        int maximumCharacters,
        bool allowEmpty)
    {
        var normalized = NormalizeText(
            value,
            maximumCharacters,
            allowEmpty);
        normalized = PrivateKeyPattern().Replace(
            normalized,
            "[REDACTED_PRIVATE_KEY]");
        return TokenPattern().Replace(
            normalized,
            "[REDACTED_TOKEN]");
    }

    private static string NormalizeText(
        string value,
        int maximumCharacters,
        bool allowEmpty)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumCharacters
            || (!allowEmpty && string.IsNullOrWhiteSpace(normalized))
            || normalized.Any(character =>
                character == '\0'
                || char.IsControl(character)
                   && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidDataException(
                "Conversation text is empty, malformed, or exceeds its limit.");
        }

        return normalized;
    }

    private static string NormalizeTargetUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length > 2048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("local" or "ssh" or "workspace")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException(
                "Conversation file metadata contains an invalid workspace URI.");
        }

        return normalized;
    }

    private static string NormalizeOptionalSha256(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length > 0
            && (normalized.Length != 64
                || !normalized.All(char.IsAsciiHexDigit)))
        {
            throw new InvalidDataException(
                "Conversation file metadata contains a malformed SHA-256.");
        }

        return normalized;
    }

    private static string ValidateConversationId(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!ResourceIdPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "The conversation ID is malformed.",
                nameof(value));
        }

        return normalized;
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
                "A strong conversation ETag is required.");
        }
    }

    private static ConversationCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(
                normalized.Length + (4 - normalized.Length % 4) % 4,
                '=');
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length is <= 0 or > 512)
            {
                throw new FormatException();
            }

            var value = JsonSerializer.Deserialize<ConversationCursor>(
                            bytes,
                            JsonOptions)
                        ?? throw new FormatException();
            _ = ValidateConversationId(value.Id);
            if (value.UpdatedAtUtc == default)
            {
                throw new FormatException();
            }

            return value;
        }
        catch (Exception exception) when (exception is FormatException
                                         or JsonException
                                         or ArgumentException)
        {
            throw new ArgumentException(
                "The conversation cursor is malformed.",
                nameof(cursor),
                exception);
        }
    }

    private static string EncodeCursor(ConversationCursor cursor) =>
        Convert.ToBase64String(
                JsonSerializer.SerializeToUtf8Bytes(cursor, JsonOptions))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record ConversationCursor(
        DateTimeOffset UpdatedAtUtc,
        string Id);

    private sealed record ConversationRow(
        string Id,
        string ProfileId,
        string Title,
        long Revision,
        string CompactedContext,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? DeletedAtUtc,
        long LastSequence);
}
