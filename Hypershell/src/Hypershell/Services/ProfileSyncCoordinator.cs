using Hypershell.Models;
using Hypershell.Profiles;

namespace Hypershell.Services;

public sealed record ProfileSyncDownloadOutcome(
    string EndpointKey,
    string ProfileId,
    string ETag,
    long ServerRevision,
    string ProfileSha256,
    ProfileImportPreview Preview);

public enum ProfileSyncUploadOutcomeKind
{
    Unchanged,
    Created,
    Updated,
    Conflict
}

public sealed record ProfileSyncUploadOutcome(
    ProfileSyncUploadOutcomeKind Kind,
    RemoteProfileSummary? RemoteProfile = null,
    string LocalConflictPath = "",
    string RemoteConflictPath = "",
    ProfileImportPreview? RemoteConflictPreview = null,
    string RemoteConflictFailure = "");

public enum ProfileSyncDeleteOutcomeKind
{
    Deleted,
    Conflict
}

public sealed record ProfileSyncDeleteOutcome(
    ProfileSyncDeleteOutcomeKind Kind,
    RemoteProfileSummary? Tombstone = null);

public sealed class ProfileSyncCoordinator
{
    private readonly PortableProfileService _portableProfileService;
    private readonly ProfileSyncDeviceStateService _deviceStateService;

    public ProfileSyncCoordinator(
        PortableProfileService portableProfileService,
        ProfileSyncDeviceStateService deviceStateService)
    {
        _portableProfileService = portableProfileService;
        _deviceStateService = deviceStateService;
    }

    public Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
        IProfileSyncTransport transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return transport.ListAsync(cancellationToken);
    }

    public async Task<ProfileSyncDownloadOutcome> DownloadPreviewAsync(
        IProfileSyncTransport transport,
        string endpoint,
        string profileId,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        var fetched = await transport.FetchAsync(profileId, cancellationToken);
        var preview = _portableProfileService.InspectPayload(
            fetched.Payload,
            $"{endpointKey}/{ProfileSyncContract.RelativeProfilesPath}/{profileId}@{fetched.ServerRevision}",
            extensionCatalog,
            terminalProfiles);
        if (preview.Envelope is { } envelope
            && !string.Equals(
                envelope.Profile.Id,
                profileId,
                StringComparison.OrdinalIgnoreCase))
        {
            preview = AddPreviewError(
                preview,
                "PROFILE_SYNC_ID_MISMATCH",
                $"The requested remote ID '{profileId}' contained profile '{envelope.Profile.Id}'.");
        }

        return new ProfileSyncDownloadOutcome(
            endpointKey,
            profileId,
            fetched.ETag,
            fetched.ServerRevision,
            preview.Envelope?.ProfileSha256 ?? string.Empty,
            preview);
    }

    public void AcceptDownloadedProfile(
        ProfileSyncDownloadOutcome download,
        AppSettings currentSettings,
        ExtensionDiscoverySnapshot extensionCatalog)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(extensionCatalog);
        if (!download.Preview.CanImport
            || download.Preview.Envelope is not { } envelope
            || !string.Equals(
                currentSettings.PortableProfileId,
                download.ProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only a successfully saved remote profile can advance its local sync checkpoint.");
        }

        var currentSnapshot = _portableProfileService.CreateSnapshot(
            currentSettings,
            extensionCatalog);
        var currentHash = _portableProfileService.ComputeProfileSha256(
            currentSnapshot);
        if (!string.Equals(
                currentHash,
                envelope.ProfileSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The saved local profile no longer matches the downloaded remote revision.");
        }

        var previous = _deviceStateService.FindCheckpoint(
            download.EndpointKey,
            download.ProfileId);
        _deviceStateService.SaveCheckpoint(
            new ProfileSyncCheckpoint
            {
                EndpointKey = download.EndpointKey,
                ProfileId = download.ProfileId,
                ETag = download.ETag,
                ServerRevision = download.ServerRevision,
                ProfileSha256 = download.ProfileSha256,
                LastPulledAtUtc = DateTimeOffset.UtcNow,
                LastPushedAtUtc = previous?.LastPushedAtUtc,
                IsRemoteDeleted = false
            });
    }

    public async Task<ProfileSyncUploadOutcome> UploadAsync(
        IProfileSyncTransport transport,
        string endpoint,
        AppSettings currentSettings,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(extensionCatalog);
        ArgumentNullException.ThrowIfNull(terminalProfiles);

        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        var snapshot = _portableProfileService.CreateSnapshot(
            currentSettings,
            extensionCatalog);
        var envelope = _portableProfileService.CreateEnvelope(snapshot);
        var payload = _portableProfileService.SerializeEnvelope(envelope);
        var checkpoint = _deviceStateService.FindCheckpoint(
            endpointKey,
            snapshot.Id);
        if (checkpoint is { IsRemoteDeleted: false }
            && string.Equals(
                checkpoint.ProfileSha256,
                envelope.ProfileSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileSyncUploadOutcome(
                ProfileSyncUploadOutcomeKind.Unchanged);
        }

        var mutation = await transport.PutAsync(
            snapshot.Id,
            payload,
            checkpoint?.ETag,
            cancellationToken);
        if (mutation.Status == ProfileSyncMutationStatus.Conflict)
        {
            return await CreateConflictOutcomeAsync(
                transport,
                endpointKey,
                snapshot,
                payload,
                extensionCatalog,
                terminalProfiles,
                cancellationToken);
        }

        var remote = mutation.Profile
                     ?? throw new ProfileSyncException(
                         ProfileSyncFailureKind.Protocol,
                         "The server did not return uploaded profile metadata.");
        ValidateUploadedMetadata(snapshot, envelope, remote);
        _deviceStateService.SaveCheckpoint(
            new ProfileSyncCheckpoint
            {
                EndpointKey = endpointKey,
                ProfileId = snapshot.Id,
                ETag = remote.ETag,
                ServerRevision = remote.ServerRevision,
                ProfileSha256 = remote.ProfileSha256,
                LastPulledAtUtc = checkpoint?.LastPulledAtUtc,
                LastPushedAtUtc = DateTimeOffset.UtcNow,
                IsRemoteDeleted = false
            });
        return new ProfileSyncUploadOutcome(
            mutation.Status == ProfileSyncMutationStatus.Created
                ? ProfileSyncUploadOutcomeKind.Created
                : ProfileSyncUploadOutcomeKind.Updated,
            remote);
    }

    public async Task<ProfileSyncDeleteOutcome> DeleteAsync(
        IProfileSyncTransport transport,
        string endpoint,
        RemoteProfileSummary selectedProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(selectedProfile);
        if (selectedProfile.IsDeleted)
        {
            throw new InvalidOperationException(
                "The selected remote profile is already a tombstone.");
        }

        var result = await transport.DeleteAsync(
            selectedProfile.Id,
            selectedProfile.ETag,
            cancellationToken);
        if (result.Status == ProfileSyncMutationStatus.Conflict)
        {
            return new ProfileSyncDeleteOutcome(
                ProfileSyncDeleteOutcomeKind.Conflict);
        }

        var tombstone = result.Profile
                        ?? throw new ProfileSyncException(
                            ProfileSyncFailureKind.Protocol,
                            "The server did not return tombstone metadata.");
        if (!tombstone.IsDeleted
            || !string.Equals(
                tombstone.Id,
                selectedProfile.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The server returned a tombstone for the wrong profile.");
        }

        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        var previous = _deviceStateService.FindCheckpoint(
            endpointKey,
            selectedProfile.Id);
        _deviceStateService.SaveCheckpoint(
            new ProfileSyncCheckpoint
            {
                EndpointKey = endpointKey,
                ProfileId = selectedProfile.Id,
                ETag = tombstone.ETag,
                ServerRevision = tombstone.ServerRevision,
                ProfileSha256 = tombstone.ProfileSha256,
                LastPulledAtUtc = previous?.LastPulledAtUtc,
                LastPushedAtUtc = DateTimeOffset.UtcNow,
                IsRemoteDeleted = true
            });
        return new ProfileSyncDeleteOutcome(
            ProfileSyncDeleteOutcomeKind.Deleted,
            tombstone);
    }

    private async Task<ProfileSyncUploadOutcome> CreateConflictOutcomeAsync(
        IProfileSyncTransport transport,
        string endpointKey,
        PortableProfileSnapshot localSnapshot,
        ReadOnlyMemory<byte> localPayload,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles,
        CancellationToken cancellationToken)
    {
        var localPath = _deviceStateService.WriteConflictBundle(
            localSnapshot.DisplayName,
            "local",
            localPayload);
        try
        {
            var remote = await transport.FetchAsync(
                localSnapshot.Id,
                cancellationToken);
            var preview = _portableProfileService.InspectPayload(
                remote.Payload,
                $"{endpointKey}/{ProfileSyncContract.RelativeProfilesPath}/{localSnapshot.Id}@{remote.ServerRevision}",
                extensionCatalog,
                terminalProfiles);
            var remotePath = string.Empty;
            if (preview.CanImport
                && preview.Envelope is { } envelope
                && string.Equals(
                    envelope.Profile.Id,
                    localSnapshot.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                remotePath = _deviceStateService.WriteConflictBundle(
                    envelope.Profile.DisplayName,
                    "remote",
                    remote.Payload);
            }
            else if (preview.Envelope is { } mismatchedEnvelope
                     && !string.Equals(
                         mismatchedEnvelope.Profile.Id,
                         localSnapshot.Id,
                         StringComparison.OrdinalIgnoreCase))
            {
                preview = AddPreviewError(
                    preview,
                    "PROFILE_SYNC_ID_MISMATCH",
                    "The conflicting remote payload used a different profile ID.");
            }

            return new ProfileSyncUploadOutcome(
                ProfileSyncUploadOutcomeKind.Conflict,
                LocalConflictPath: localPath,
                RemoteConflictPath: remotePath,
                RemoteConflictPreview: preview);
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == ProfileSyncFailureKind.NotFound)
        {
            return new ProfileSyncUploadOutcome(
                ProfileSyncUploadOutcomeKind.Conflict,
                LocalConflictPath: localPath,
                RemoteConflictFailure:
                    "The current remote revision was no longer available.");
        }
        catch (ProfileSyncException exception)
        {
            return new ProfileSyncUploadOutcome(
                ProfileSyncUploadOutcomeKind.Conflict,
                LocalConflictPath: localPath,
                RemoteConflictFailure: exception.Message);
        }
    }

    private static ProfileImportPreview AddPreviewError(
        ProfileImportPreview preview,
        string code,
        string message) =>
        new()
        {
            SourcePath = preview.SourcePath,
            BundleBytes = preview.BundleBytes,
            Envelope = preview.Envelope,
            Diagnostics = preview.Diagnostics
                .Append(new ProfileImportDiagnostic(
                    ProfileImportDiagnosticSeverity.Error,
                    code,
                    message))
                .ToList()
        };

    private static void ValidateUploadedMetadata(
        PortableProfileSnapshot snapshot,
        PortableProfileEnvelope envelope,
        RemoteProfileSummary remote)
    {
        if (remote.IsDeleted
            || !string.Equals(
                remote.Id,
                snapshot.Id,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                remote.ProfileSha256,
                envelope.ProfileSha256,
                StringComparison.OrdinalIgnoreCase)
            || remote.ProfileRevision != snapshot.Revision)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The upload receipt did not identify the uploaded profile revision and hash.");
        }
    }
}
