using System.Text.Json.Serialization;

namespace Hypershell.Profiles;

public static class ProfileSyncContract
{
    public const int CurrentProtocolVersion = 1;
    public const string RelativeProfilesPath = "api/hypershell/v1/profiles";
    public const string ProfileMediaType =
        "application/vnd.hypershell.profile+json";
    public const long MaximumListResponseBytes = 1024 * 1024;
    public const long MaximumReceiptBytes = 64 * 1024;
    public const int MaximumRemoteProfiles = 1000;
    public const string ServerRevisionHeader =
        "X-Hypershell-Server-Revision";
}

public sealed class ProfileSyncListDocument
{
    [JsonRequired]
    public int ProtocolVersion { get; init; } =
        ProfileSyncContract.CurrentProtocolVersion;

    [JsonRequired]
    public IReadOnlyList<RemoteProfileSummary> Profiles { get; init; } = [];
}

public sealed class RemoteProfileSummary
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public long ServerRevision { get; init; }

    [JsonRequired]
    public long ProfileRevision { get; init; }

    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonRequired]
    public string ProfileSha256 { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("etag")]
    public string ETag { get; init; } = string.Empty;

    [JsonRequired]
    public bool IsDeleted { get; init; }
}

public sealed class ProfileSyncMutationReceipt
{
    [JsonRequired]
    public int ProtocolVersion { get; init; } =
        ProfileSyncContract.CurrentProtocolVersion;

    [JsonRequired]
    public RemoteProfileSummary Profile { get; init; } = new();
}

public sealed record ProfileSyncFetchResult(
    string ProfileId,
    string ETag,
    long ServerRevision,
    ReadOnlyMemory<byte> Payload);

public enum ProfileSyncMutationStatus
{
    Created,
    Updated,
    Deleted,
    Conflict
}

public sealed record ProfileSyncMutationResult(
    ProfileSyncMutationStatus Status,
    RemoteProfileSummary? Profile = null);

public enum ProfileSyncFailureKind
{
    Configuration,
    Authentication,
    Authorization,
    NotFound,
    Conflict,
    Protocol,
    Network,
    Timeout,
    Cancelled,
    RemoteError
}

public sealed class ProfileSyncException : Exception
{
    public ProfileSyncException(
        ProfileSyncFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ProfileSyncFailureKind Kind { get; }
}

public interface IProfileSyncTransport : IAsyncDisposable
{
    Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<ProfileSyncFetchResult> FetchAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<ProfileSyncMutationResult> PutAsync(
        string profileId,
        ReadOnlyMemory<byte> profilePayload,
        string? expectedETag,
        CancellationToken cancellationToken = default);

    Task<ProfileSyncMutationResult> DeleteAsync(
        string profileId,
        string expectedETag,
        CancellationToken cancellationToken = default);
}

public sealed class ProfileSyncDeviceConfiguration
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public string ServerEndpoint { get; init; } = string.Empty;

    [JsonRequired]
    // Keep the v1 wire name so existing device-local connection files migrate
    // from the former display-label wording without storing a second field.
    [JsonPropertyName("accountLabel")]
    public string AccountId { get; init; } = string.Empty;
}

public sealed class ProfileSyncDeviceState
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public IReadOnlyList<ProfileSyncCheckpoint> Checkpoints { get; init; } = [];
}

public sealed class ProfileSyncCheckpoint
{
    [JsonRequired]
    public string EndpointKey { get; init; } = string.Empty;

    [JsonRequired]
    public string ProfileId { get; init; } = string.Empty;

    [JsonRequired]
    public string ETag { get; init; } = string.Empty;

    [JsonRequired]
    public long ServerRevision { get; init; }

    [JsonRequired]
    public string ProfileSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public DateTimeOffset? LastPulledAtUtc { get; init; }

    [JsonRequired]
    public DateTimeOffset? LastPushedAtUtc { get; init; }

    [JsonRequired]
    public bool IsRemoteDeleted { get; init; }
}
