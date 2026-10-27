using System.Text.Json.Serialization;

namespace Hypershell.Profiles;

public sealed class ProfileRoamingDeviceState
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public IReadOnlyList<RoamingProfileCheckpoint> ProfileCheckpoints
        { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<RoamingConversationCheckpoint> ConversationCheckpoints
        { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<WorkspaceProjectDeviceMapping> ProjectMappings
        { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<LocalProfileRevision> LocalProfileRevisions
        { get; init; } = [];
}

public sealed record RoamingProfileCheckpoint
{
    public string EndpointKey { get; init; } = string.Empty;

    public string ProfileId { get; init; } = string.Empty;

    public string ETag { get; init; } = string.Empty;

    public long ServerRevision { get; init; }

    public string ContentSha256 { get; init; } = string.Empty;

    public string LocalFingerprint { get; init; } = string.Empty;

    public DateTimeOffset? LastPulledAtUtc { get; init; }

    public DateTimeOffset? LastPushedAtUtc { get; init; }

    public bool IsRemoteDeleted { get; init; }
}

public sealed record RoamingConversationCheckpoint
{
    public string EndpointKey { get; init; } = string.Empty;

    public string ConversationId { get; init; } = string.Empty;

    public string ETag { get; init; } = string.Empty;

    public long ServerRevision { get; init; }

    public long LastUploadedSequence { get; init; }

    public string LocalFingerprint { get; init; } = string.Empty;

    public DateTimeOffset? LastPulledAtUtc { get; init; }

    public DateTimeOffset? LastPushedAtUtc { get; init; }

    public bool IsRemoteDeleted { get; init; }
}

public sealed record WorkspaceProjectDeviceMapping
{
    public string ProfileId { get; init; } = string.Empty;

    public string ProjectId { get; init; } = string.Empty;

    public IReadOnlyList<string> LocalDirectories { get; init; } = [];

    public string RemoteWorkspaceUri { get; init; } = string.Empty;
}

public sealed record LocalProfileRevision
{
    public string ProfileId { get; init; } = string.Empty;

    public long Revision { get; init; }

    public string ContentFingerprint { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
