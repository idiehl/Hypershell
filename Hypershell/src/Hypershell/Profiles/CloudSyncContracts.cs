namespace Hypershell.Profiles;

public enum CloudMutationStatus
{
    Created,
    Updated,
    Deleted,
    Conflict
}

public sealed record WorkspaceProfileCloudMutationResult(
    CloudMutationStatus Status,
    WorkspaceProfileRoamingSummary? Profile = null);

public sealed record ConversationCloudMutationResult(
    CloudMutationStatus Status,
    AiConversationSummary? Conversation = null);

public interface ICloudRoamingTransport : IAsyncDisposable
{
    Task<IReadOnlyList<WorkspaceProfileRoamingSummary>>
        ListWorkspaceProfilesAsync(
            CancellationToken cancellationToken = default);

    Task<WorkspaceProfileRoamingResource> GetWorkspaceProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceProfileCloudMutationResult> PutWorkspaceProfileAsync(
        WorkspaceProfileRoamingDocument profile,
        string? expectedETag,
        CancellationToken cancellationToken = default);

    Task<WorkspaceProfileCloudMutationResult> DeleteWorkspaceProfileAsync(
        string profileId,
        string expectedETag,
        CancellationToken cancellationToken = default);

    Task<AiConversationListPage> ListConversationsAsync(
        string? profileId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AiConversationPage> GetConversationPageAsync(
        string conversationId,
        long afterSequence,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ConversationCloudMutationResult> PutConversationAsync(
        string conversationId,
        AiConversationMutationRequest mutation,
        string? expectedETag,
        CancellationToken cancellationToken = default);

    Task<ConversationCloudMutationResult> DeleteConversationAsync(
        string conversationId,
        string expectedETag,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HyperlibVaultPackageSummary>> ListPackagesAsync(
        CancellationToken cancellationToken = default);

    Task<HyperlibVaultPackageSummary> UploadPackageAsync(
        string packageId,
        string version,
        string contentSha256,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<string> DownloadPackageAsync(
        string contentSha256,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
