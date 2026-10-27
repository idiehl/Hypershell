namespace Hypershell.Profiles;

public sealed record LocalAiConversationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Id { get; init; } = string.Empty;

    public string ProfileId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public bool IsOpenOnThisDevice { get; init; } = true;

    public string ProviderId { get; init; } = string.Empty;

    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// Device-local project root selected for this conversation. Local paths
    /// do not roam because the same project may live elsewhere on another PC.
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    public string CompactedContext { get; init; } = string.Empty;

    public IReadOnlyList<AiConversationItem> Items { get; init; } = [];

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record ConversationRoamingSyncOutcome(
    int UploadedConversations,
    int DownloadedConversations,
    int UnchangedConversations,
    IReadOnlyList<string> ConflictPaths);
