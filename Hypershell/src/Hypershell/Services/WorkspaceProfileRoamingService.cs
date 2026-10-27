using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed record WorkspaceProfileRoamingSnapshot(
    WorkspaceProfileRoamingDocument Document,
    string LocalFingerprint);

public sealed record WorkspaceProfileRoamingApplyResult(
    WorkspaceAiContextRoamingImportResult AiContexts,
    IReadOnlyList<string> UnmappedRemoteProjectIds,
    IReadOnlyList<HyperlibPackageSelection> PackageSelections);

public enum WorkspaceProfileRoamingSyncOutcomeKind
{
    Unchanged,
    Created,
    Updated,
    Conflict
}

public sealed record WorkspaceProfileRoamingSyncOutcome(
    WorkspaceProfileRoamingSyncOutcomeKind Kind,
    WorkspaceProfileRoamingSummary? RemoteProfile = null,
    string LocalConflictPath = "",
    string RemoteConflictPath = "");

public sealed record ProfileCloudSyncOutcome(
    ProfileSyncUploadOutcome Settings,
    WorkspaceProfileRoamingSyncOutcome WorkspaceProfile,
    HyperlibPackageRoamingOutcome Packages,
    ConversationRoamingSyncOutcome Conversations);

public sealed record ProfileCloudRestoreOutcome(
    WorkspaceProfileRoamingApplyResult WorkspaceProfile,
    HyperlibPackageRoamingOutcome Packages,
    ConversationRoamingSyncOutcome Conversations);

/// <summary>
/// Projects portable v2 adjunct state without serializing Windows paths or
/// local trust decisions. Device mappings are retained independently.
/// </summary>
public sealed class WorkspaceProfileRoamingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    private readonly WorkspaceAiContextService _contextService;
    private readonly ProfileRoamingDeviceStateService _deviceState;

    public WorkspaceProfileRoamingService(
        WorkspaceAiContextService contextService,
        ProfileRoamingDeviceStateService deviceState)
    {
        _contextService = contextService;
        _deviceState = deviceState;
    }

    public WorkspaceProfileRoamingSnapshot CreateSnapshot(
        AppSettings settings,
        ExtensionDiscoverySnapshot extensionCatalog)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(extensionCatalog);
        _deviceState.CaptureProjectMappings(
            settings.PortableProfileId,
            settings.Explorer.Projects);
        var projects = settings.Explorer.Projects.Select(project => new
            WorkspaceProjectReference
            {
                Id = project.Id,
                DisplayName = project.Name,
                WorkspaceLocationUri = project.Directories.FirstOrDefault(
                    IsRoamingWorkspaceUri)
                    ?? _deviceState.GetProjectWorkspaceUri(
                        settings.PortableProfileId,
                        project.Id)
            })
            .ToList();
        var packages = BuildPackageSelections(settings, extensionCatalog);
        var preliminary = new WorkspaceProfileRoamingDocument
        {
            ProfileId = settings.PortableProfileId,
            DisplayName = settings.PortableProfileDisplayName,
            ClientRevision = 0,
            ClientUpdatedAtUtc = DateTimeOffset.UnixEpoch,
            SelectedAiContextId = _contextService.SelectedContextId,
            AiContexts = _contextService.ExportRoamingDefinitions(),
            Projects = projects,
            Packages = packages
        };
        var fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(
            preliminary,
            JsonOptions));
        var revision = _deviceState.AdvanceLocalRevision(
            preliminary.ProfileId,
            fingerprint);
        return new WorkspaceProfileRoamingSnapshot(
            preliminary with
            {
                ClientRevision = revision.Revision,
                ClientUpdatedAtUtc = revision.UpdatedAtUtc
            },
            fingerprint);
    }

    public WorkspaceProfileRoamingApplyResult Apply(
        AppSettings settings,
        WorkspaceProfileRoamingDocument document)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(
                settings.PortableProfileId,
                document.ProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A roaming adjunct can only be applied to its matching portable profile.");
        }

        _deviceState.CaptureProjectMappings(
            settings.PortableProfileId,
            settings.Explorer.Projects);
        _deviceState.ApplyRoamingProjectReferences(
            settings.PortableProfileId,
            document.Projects);
        var contextResult = _contextService.ImportRoamingDefinitions(
            document.AiContexts,
            document.SelectedAiContextId);
        var projects = new ObservableCollection<ProjectDefinition>();
        var unmapped = new List<string>();
        foreach (var remote in document.Projects)
        {
            var localDirectories = _deviceState.GetProjectDirectories(
                document.ProfileId,
                remote.Id);
            var directories = localDirectories.ToList();
            if (!string.IsNullOrWhiteSpace(remote.WorkspaceLocationUri))
            {
                directories.Add(remote.WorkspaceLocationUri);
            }

            if (directories.Count == 0)
            {
                unmapped.Add(remote.Id);
            }

            projects.Add(new ProjectDefinition
            {
                Id = remote.Id,
                Name = remote.DisplayName,
                Directories = new ObservableCollection<string>(
                    directories.Distinct(StringComparer.OrdinalIgnoreCase))
            });
        }

        settings.PortableProfileDisplayName = document.DisplayName;
        settings.Explorer.Projects = projects;
        if (!projects.Any(project => string.Equals(
                project.Id,
                settings.Explorer.ActiveProjectId,
                StringComparison.OrdinalIgnoreCase)))
        {
            settings.Explorer.ActiveProjectId = projects
                .FirstOrDefault(project => project.Directories.Count > 0)
                ?.Id
                ?? string.Empty;
        }

        MergePackageSelections(settings, document.Packages);
        return new WorkspaceProfileRoamingApplyResult(
            contextResult,
            unmapped,
            document.Packages.ToList());
    }

    public async Task<WorkspaceProfileRoamingSyncOutcome> UploadAsync(
        ICloudRoamingTransport transport,
        string endpoint,
        AppSettings settings,
        ExtensionDiscoverySnapshot extensionCatalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var snapshot = CreateSnapshot(settings, extensionCatalog);
        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        var checkpoint = _deviceState.FindProfileCheckpoint(
            endpointKey,
            snapshot.Document.ProfileId);
        if (checkpoint is { IsRemoteDeleted: false }
            && string.Equals(
                checkpoint.LocalFingerprint,
                snapshot.LocalFingerprint,
                StringComparison.Ordinal))
        {
            return new WorkspaceProfileRoamingSyncOutcome(
                WorkspaceProfileRoamingSyncOutcomeKind.Unchanged);
        }

        var mutation = await transport.PutWorkspaceProfileAsync(
            snapshot.Document,
            checkpoint?.ETag,
            cancellationToken);
        if (mutation.Status == CloudMutationStatus.Conflict)
        {
            return await CreateConflictAsync(
                transport,
                snapshot,
                cancellationToken);
        }

        var remote = mutation.Profile
                     ?? throw new ProfileSyncException(
                         ProfileSyncFailureKind.Protocol,
                         "The account service returned no roaming-profile receipt.");
        _deviceState.SaveProfileCheckpoint(new RoamingProfileCheckpoint
        {
            EndpointKey = endpointKey,
            ProfileId = remote.ProfileId,
            ETag = remote.ETag,
            ServerRevision = remote.ServerRevision,
            ContentSha256 = remote.ContentSha256,
            LocalFingerprint = snapshot.LocalFingerprint,
            LastPulledAtUtc = checkpoint?.LastPulledAtUtc,
            LastPushedAtUtc = DateTimeOffset.UtcNow,
            IsRemoteDeleted = false
        });
        return new WorkspaceProfileRoamingSyncOutcome(
            mutation.Status == CloudMutationStatus.Created
                ? WorkspaceProfileRoamingSyncOutcomeKind.Created
                : WorkspaceProfileRoamingSyncOutcomeKind.Updated,
            remote);
    }

    public async Task<WorkspaceProfileRoamingApplyResult> DownloadAndApplyAsync(
        ICloudRoamingTransport transport,
        string endpoint,
        AppSettings settings,
        ExtensionDiscoverySnapshot extensionCatalog,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var resource = await transport.GetWorkspaceProfileAsync(
            profileId,
            cancellationToken);
        var document = resource.Document
                       ?? throw new ProfileSyncException(
                           ProfileSyncFailureKind.NotFound,
                           "The roaming workspace profile is a tombstone.");
        var applied = Apply(settings, document);
        var snapshot = CreateSnapshot(settings, extensionCatalog);
        _deviceState.SaveProfileCheckpoint(new RoamingProfileCheckpoint
        {
            EndpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint),
            ProfileId = profileId,
            ETag = resource.Profile.ETag,
            ServerRevision = resource.Profile.ServerRevision,
            ContentSha256 = resource.Profile.ContentSha256,
            LocalFingerprint = snapshot.LocalFingerprint,
            LastPulledAtUtc = DateTimeOffset.UtcNow,
            IsRemoteDeleted = false
        });
        return applied;
    }

    private async Task<WorkspaceProfileRoamingSyncOutcome>
        CreateConflictAsync(
            ICloudRoamingTransport transport,
            WorkspaceProfileRoamingSnapshot local,
            CancellationToken cancellationToken)
    {
        var localBytes = JsonSerializer.SerializeToUtf8Bytes(
            local.Document,
            JsonOptions);
        var localPath = _deviceState.WriteConflictJson(
            local.Document.DisplayName,
            "local",
            localBytes);
        var remotePath = string.Empty;
        try
        {
            var remote = await transport.GetWorkspaceProfileAsync(
                local.Document.ProfileId,
                cancellationToken);
            remotePath = _deviceState.WriteConflictJson(
                remote.Profile.DisplayName,
                "remote",
                JsonSerializer.SerializeToUtf8Bytes(remote, JsonOptions));
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == ProfileSyncFailureKind.NotFound)
        {
        }

        return new WorkspaceProfileRoamingSyncOutcome(
            WorkspaceProfileRoamingSyncOutcomeKind.Conflict,
            LocalConflictPath: localPath,
            RemoteConflictPath: remotePath);
    }

    private static IReadOnlyList<HyperlibPackageSelection>
        BuildPackageSelections(
            AppSettings settings,
            ExtensionDiscoverySnapshot extensionCatalog)
    {
        var enabledExtensions = settings.ProfileExtensions
            .Where(reference => reference.Enabled)
            .Select(reference => reference.ExtensionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabledLibraries = settings.Environments
            .SelectMany(environment => environment.LibraryIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in extensionCatalog.LibraryCandidates.Where(
                     candidate => enabledLibraries.Contains(
                         candidate.QualifiedLibraryId)))
        {
            if (candidate.Package.Manifest is { } manifest)
            {
                enabledExtensions.Add(manifest.Id);
            }
        }

        return extensionCatalog.Packages
            .Where(package =>
                package.Status == ExtensionPackageStatus.Validated
                && package.Manifest?.Runtime
                == ExtensionRuntimeKind.ContentOnly
                && package.PackageSha256.Length == 64)
            .Select(package => new HyperlibPackageSelection
            {
                PackageId = package.Manifest!.Id,
                Version = package.Manifest.Version,
                ContentSha256 = package.PackageSha256.ToLowerInvariant(),
                IsOfficial = false,
                IsEnabled = enabledExtensions.Contains(package.Manifest.Id)
            })
            .OrderBy(package => package.PackageId, StringComparer.Ordinal)
            .ToList();
    }

    private static void MergePackageSelections(
        AppSettings settings,
        IReadOnlyList<HyperlibPackageSelection> packages)
    {
        var references = settings.ProfileExtensions.ToDictionary(
            reference => reference.ExtensionId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            references[package.PackageId] = new ProfileExtensionReference
            {
                ExtensionId = package.PackageId,
                VersionRange = package.Version,
                Enabled = package.IsEnabled,
                ActivateOnStartup = references.TryGetValue(
                    package.PackageId,
                    out var existing)
                    && existing.ActivateOnStartup
            };
        }

        settings.ProfileExtensions =
            new ObservableCollection<ProfileExtensionReference>(
                references.Values.OrderBy(
                    reference => reference.ExtensionId,
                    StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsRoamingWorkspaceUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "ssh" or "workspace"
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    private static string Hash(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
}
