namespace Hypershell.Profiles;

using System.Text.Json.Serialization;

public static class HypershellCloudContract
{
    public const int CurrentProtocolVersion = 2;
    public const string RelativeStoragePath =
        "api/hypershell/v2/storage";
    public const string RelativeClientDevicesPath =
        "api/hypershell/v2/client-devices";
    public const string RelativeRemoteHostsPath =
        "api/hypershell/v2/remote-hosts";
    public const string RelativeSshCredentialsPath =
        "api/hypershell/v2/ssh-credentials";
    public const string RelativeCredentialReleasePath =
        "api/hypershell/v2/credential-releases";
    public const string RelativeWorkspaceProfilesPath =
        "api/hypershell/v2/workspace-profiles";
    public const string RelativeConversationsPath =
        "api/hypershell/v2/conversations";
    public const string RelativePackagesPath =
        "api/hypershell/v2/packages";
    public const string RelativeReleasesPath =
        "api/hypershell/v2/releases";
    public const string RelativeDeviceAuthorizationPath =
        "api/hypershell/v2/auth/device";
    public const string RelativeDeviceTokenPath =
        "api/hypershell/v2/auth/device/token";
    public const string RelativeRefreshTokenPath =
        "api/hypershell/v2/auth/refresh";
    public const string HyperlibPackageMediaType =
        "application/vnd.hypershell.hyperlib+zip";

    public const int MaximumDeviceAuthorizationRequestBytes = 16 * 1024;
    public const int MaximumDeviceTokenRequestBytes = 8 * 1024;
    public const int MaximumDeviceAuthorizationResponseBytes = 16 * 1024;
    public const int MaximumCloudSessionResponseBytes = 32 * 1024;
    public const int MaximumCloudErrorResponseBytes = 16 * 1024;
    public const int MaximumWorkspaceProfileBytes = 1024 * 1024;
    public const int MaximumWorkspaceProfileListBytes = 1024 * 1024;
    public const int MaximumAiContextsPerProfile = 128;
    public const int MaximumProjectsPerProfile = 512;
    public const int MaximumPackagesPerProfile = 1024;
    public const int MaximumConversationMutationBytes = 1024 * 1024;
    public const int MaximumConversationPageBytes = 2 * 1024 * 1024;
    public const int MaximumConversationItemsPerMutation = 100;
    public const int MaximumConversationItemsPerPage = 200;
    public const int MaximumConversationTextCharacters = 128 * 1024;
    public const int MaximumConversationCompactionCharacters = 512 * 1024;
    public const long MaximumPersonalPackageBytes = 256L * 1024 * 1024;
    public const int MaximumPackageListBytes = 1024 * 1024;
    public const int MaximumRemoteHostMutationBytes = 64 * 1024;
    public const int MaximumRemoteHostListBytes = 1024 * 1024;
    public const int MaximumSshCredentialMutationBytes = 2 * 1024 * 1024;
    public const int MaximumSshCredentialListBytes = 1024 * 1024;
    public const int MaximumCredentialReleaseRequestBytes = 16 * 1024;
    public const int MaximumCredentialReleaseResponseBytes = 2 * 1024 * 1024;
}

public sealed record DeviceAuthorizationRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string DeviceName { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public string DeviceFingerprint { get; init; } = string.Empty;
}

public sealed record DeviceAuthorizationResponse
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string DeviceCode { get; init; } = string.Empty;

    public string UserCode { get; init; } = string.Empty;

    public string VerificationUri { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public int PollIntervalSeconds { get; init; } = 5;
}

public sealed record DeviceTokenRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string DeviceCode { get; init; } = string.Empty;
}

public sealed record RefreshTokenRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record CloudSessionResponse
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string AccountId { get; init; } = string.Empty;

    public string AccountDisplayName { get; init; } = string.Empty;

    public string ClientDeviceId { get; init; } = string.Empty;

    public string AccessToken { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;

    public DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}

public sealed record CloudStorageStatus
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public long TotalBytes { get; init; }

    public long AvailableBytes { get; init; }

    public long ReservedBytes { get; init; }

    public bool CanAcceptWrites { get; init; }

    public int MetadataSchemaVersion { get; init; }

    public DateTimeOffset CheckedAtUtc { get; init; }
}

public enum RemoteHostOperatingSystem
{
    Unknown,
    Linux,
    MacOs,
    Windows
}

public sealed record ClientDeviceSummary
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset LastSeenAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }
}

public sealed record RemoteHostSummary
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string HostName { get; init; } = string.Empty;

    public int Port { get; init; } = 22;

    public string UserName { get; init; } = string.Empty;

    public RemoteHostOperatingSystem OperatingSystem { get; init; }

    public string DefaultRoot { get; init; } = string.Empty;

    public string? HostKeyAlgorithm { get; init; }

    public string? HostKeyFingerprint { get; init; }

    public bool HasCredential { get; init; }

    public string CredentialId { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public long Revision { get; init; }

    public bool IsDisabled { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record RemoteHostMutationRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string DisplayName { get; init; } = string.Empty;

    public string HostName { get; init; } = string.Empty;

    public int Port { get; init; } = 22;

    public string UserName { get; init; } = string.Empty;

    public RemoteHostOperatingSystem OperatingSystem { get; init; }

    public string DefaultRoot { get; init; } = string.Empty;

    public string CredentialId { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public long ExpectedRevision { get; init; }
}

public sealed record RemoteHostKeyPinRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string Algorithm { get; init; } = string.Empty;

    public string Fingerprint { get; init; } = string.Empty;

    public string ExpectedCurrentFingerprint { get; init; } = string.Empty;
}

public sealed record RemoteHostMutationReceipt
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public RemoteHostSummary Host { get; init; } = new();
}

public sealed record RemoteHostList
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public IReadOnlyList<RemoteHostSummary> Hosts { get; init; } = [];
}

public sealed record SshCredentialSummary
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string PublicKeyFingerprint { get; init; } = string.Empty;

    public int KeyVersion { get; init; }

    public bool IsRevoked { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? RotatedAtUtc { get; init; }
}

/// <summary>
/// Carries SSH secret bytes only across the authenticated enrollment boundary.
/// Callers must clear both arrays as soon as the request has been serialized.
/// </summary>
public sealed record SshCredentialMutationRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string DisplayName { get; init; } = string.Empty;

    public byte[] PrivateKeyUtf8 { get; init; } = [];

    public byte[] PassphraseUtf8 { get; init; } = [];

    public int ExpectedKeyVersion { get; init; }
}

public sealed record SshCredentialMutationReceipt
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public SshCredentialSummary Credential { get; init; } = new();
}

public sealed record SshCredentialList
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public IReadOnlyList<SshCredentialSummary> Credentials { get; init; } = [];
}

public sealed record SshCredentialReleaseAuthorization
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string ReleaseToken { get; init; } = string.Empty;

    public string UserCode { get; init; } = string.Empty;

    public string VerificationUri { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public int PollIntervalSeconds { get; init; } = 5;
}

public sealed record SshCredentialReleasePollRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string ReleaseToken { get; init; } = string.Empty;
}

/// <summary>
/// One-time credential material returned only to its approved client device.
/// Consumers must immediately protect it with Windows DPAPI and clear the arrays.
/// </summary>
public sealed record SshCredentialReleaseResponse
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string CredentialId { get; init; } = string.Empty;

    public int KeyVersion { get; init; }

    public string PublicKeyFingerprint { get; init; } = string.Empty;

    public byte[] PrivateKeyUtf8 { get; init; } = [];

    public byte[] PassphraseUtf8 { get; init; } = [];
}

public enum RoamingAiComputerControlMode
{
    Off,
    Hypershell,
    Desktop
}

public sealed record RoamingAiPathPermission
{
    public bool AllowFullAccess { get; init; }

    public IReadOnlyList<string> WorkspaceUris { get; init; } = [];
}

public sealed record RoamingAiContextDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsBuiltIn { get; init; }

    public RoamingAiPathPermission Read { get; init; } = new();

    public RoamingAiPathPermission Create { get; init; } = new();

    public RoamingAiPathPermission Edit { get; init; } = new();

    public RoamingAiPathPermission Delete { get; init; } = new();

    public RoamingAiPathPermission Terminal { get; init; } = new();

    public string Model { get; init; } = string.Empty;

    public string ReasoningEffort { get; init; } = "medium";

    public string TextVerbosity { get; init; } = "medium";

    public string DefaultSaveWorkspaceUri { get; init; } = string.Empty;

    public bool AskBeforeChanges { get; init; } = true;

    public bool AllowInternet { get; init; }

    public bool AllowLiveAssistance { get; init; }

    public RoamingAiComputerControlMode ComputerControl { get; init; }

    [JsonIgnore]
    public bool RequiresDeviceConfirmation =>
        !AskBeforeChanges
        || AllowInternet
        || ComputerControl != RoamingAiComputerControlMode.Off
        || Read.AllowFullAccess
        || Create.AllowFullAccess
        || Edit.AllowFullAccess
        || Delete.AllowFullAccess
        || Terminal.AllowFullAccess;
}

public sealed record WorkspaceProjectReference
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string WorkspaceLocationUri { get; init; } = string.Empty;

    public string GitOriginUri { get; init; } = string.Empty;

    public string DefaultBranch { get; init; } = string.Empty;
}

public sealed record HyperlibPackageSelection
{
    public string PackageId { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ContentSha256 { get; init; } = string.Empty;

    public bool IsOfficial { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed record WorkspaceProfileRoamingDocument
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string ProfileId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public long ClientRevision { get; init; }

    public DateTimeOffset ClientUpdatedAtUtc { get; init; }

    public string SelectedAiContextId { get; init; } = string.Empty;

    public IReadOnlyList<RoamingAiContextDefinition> AiContexts { get; init; }
        = [];

    public IReadOnlyList<WorkspaceProjectReference> Projects { get; init; } =
        [];

    public IReadOnlyList<HyperlibPackageSelection> Packages { get; init; } =
        [];
}

public sealed record WorkspaceProfileRoamingSummary
{
    public string ProfileId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public long ServerRevision { get; init; }

    public long ClientRevision { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string ContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("etag")]
    public string ETag { get; init; } = string.Empty;

    public bool IsDeleted { get; init; }
}

public sealed record WorkspaceProfileRoamingList
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public IReadOnlyList<WorkspaceProfileRoamingSummary> Profiles { get; init; }
        = [];
}

public sealed record WorkspaceProfileRoamingResource
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public WorkspaceProfileRoamingSummary Profile { get; init; } = new();

    public WorkspaceProfileRoamingDocument? Document { get; init; }
}

public sealed record WorkspaceProfileRoamingMutationReceipt
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public WorkspaceProfileRoamingSummary Profile { get; init; } = new();
}

public enum AiConversationRole
{
    User,
    Assistant,
    Host
}

public enum AiConversationItemKind
{
    Message,
    ActionSummary,
    FileReference
}

public record AiConversationItemInput
{
    public AiConversationRole Role { get; init; }

    public AiConversationItemKind Kind { get; init; }

    public string Text { get; init; } = string.Empty;

    public string ActionName { get; init; } = string.Empty;

    public string TargetUri { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string ContentSha256 { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record AiConversationItem : AiConversationItemInput
{
    public long Sequence { get; init; }
}

public sealed record AiConversationMutationRequest
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public string ProfileId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string CompactedContext { get; init; } = string.Empty;

    public IReadOnlyList<AiConversationItemInput> Items { get; init; } = [];
}

public sealed record AiConversationSummary
{
    public string Id { get; init; } = string.Empty;

    public string ProfileId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public long Revision { get; init; }

    public long LastSequence { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonPropertyName("etag")]
    public string ETag { get; init; } = string.Empty;

    public bool IsDeleted { get; init; }
}

public sealed record AiConversationListPage
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public IReadOnlyList<AiConversationSummary> Conversations { get; init; } =
        [];

    public string NextCursor { get; init; } = string.Empty;
}

public sealed record AiConversationPage
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public AiConversationSummary Conversation { get; init; } = new();

    public string CompactedContext { get; init; } = string.Empty;

    public IReadOnlyList<AiConversationItem> Items { get; init; } = [];

    public long? NextAfterSequence { get; init; }
}

public sealed record AiConversationMutationReceipt
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public AiConversationSummary Conversation { get; init; } = new();
}

public sealed record HyperlibVaultPackageSummary
{
    public string PackageId { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ContentSha256 { get; init; } = string.Empty;

    public long ByteLength { get; init; }

    public bool IsOfficial { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record HyperlibVaultPackageList
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public IReadOnlyList<HyperlibVaultPackageSummary> Packages { get; init; } =
        [];
}

public sealed record HyperlibVaultUploadReceipt
{
    public int ProtocolVersion { get; init; } =
        HypershellCloudContract.CurrentProtocolVersion;

    public HyperlibVaultPackageSummary Package { get; init; } = new();
}
