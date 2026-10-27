using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Collections.ObjectModel;

internal static class DesktopRoamingVerifier
{
    public static async Task VerifyAsync(
        string endpoint,
        string accessToken,
        string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "desktop-roaming");
        var firstRoot = Path.Combine(root, "first-device");
        var secondRoot = Path.Combine(root, "second-device");
        var firstProject = Path.Combine(firstRoot, "projects", "local-one");
        var secondProject = Path.Combine(secondRoot, "projects", "local-two");
        Directory.CreateDirectory(firstProject);
        Directory.CreateDirectory(secondProject);

        var firstState = new ProfileRoamingDeviceStateService(
            Path.Combine(firstRoot, "roaming.device.json"),
            Path.Combine(firstRoot, "conflicts"));
        var firstContexts = new WorkspaceAiContextService(
            Path.Combine(firstRoot, "contexts.device.json"));
        var profileService = new WorkspaceProfileRoamingService(
            firstContexts,
            firstState);
        var settings = CreateSettings(firstProject);
        var emptyCatalog = EmptyCatalog(Path.Combine(firstRoot, "extensions"));

        await using var transport = new HttpCloudRoamingTransport(
            endpoint,
            accessToken);
        var created = await profileService.UploadAsync(
            transport,
            endpoint,
            settings,
            emptyCatalog);
        Require(
            created.Kind == WorkspaceProfileRoamingSyncOutcomeKind.Created,
            "The desktop roaming coordinator did not create its adjunct.");
        var remote = await transport.GetWorkspaceProfileAsync(
            settings.PortableProfileId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(
            remote.Document);
        Require(
            remote.Document?.Projects.Single().WorkspaceLocationUri
                == "ssh://host-test/srv/hypershell/project"
            && !serialized.Contains(firstProject, StringComparison.OrdinalIgnoreCase),
            "A client-local project path entered the roaming profile payload.");

        var external = remote.Document! with
        {
            DisplayName = "Remote concurrent edit",
            ClientRevision = remote.Document.ClientRevision + 1,
            ClientUpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var externalMutation = await transport.PutWorkspaceProfileAsync(
            external,
            remote.Profile.ETag);
        Require(
            externalMutation.Status == CloudMutationStatus.Updated,
            "The concurrent roaming-profile update was not created.");
        settings.PortableProfileDisplayName = "Local concurrent edit";
        var conflict = await profileService.UploadAsync(
            transport,
            endpoint,
            settings,
            emptyCatalog);
        Require(
            conflict.Kind == WorkspaceProfileRoamingSyncOutcomeKind.Conflict
            && File.Exists(conflict.LocalConflictPath)
            && File.Exists(conflict.RemoteConflictPath),
            "A stale roaming-profile ETag did not preserve both conflict copies.");

        var secondState = new ProfileRoamingDeviceStateService(
            Path.Combine(secondRoot, "roaming.device.json"),
            Path.Combine(secondRoot, "conflicts"));
        var secondSettings = CreateSettings(secondProject);
        secondState.CaptureProjectMappings(
            secondSettings.PortableProfileId,
            secondSettings.Explorer.Projects);
        var secondProfileService = new WorkspaceProfileRoamingService(
            new WorkspaceAiContextService(
                Path.Combine(secondRoot, "contexts.device.json")),
            secondState);
        var applied = secondProfileService.Apply(
            secondSettings,
            external);
        Require(
            secondSettings.Explorer.Projects.Single().Directories.Count == 2
            && secondSettings.Explorer.Projects.Single().Directories.Contains(
                secondProject,
                StringComparer.OrdinalIgnoreCase)
            && secondSettings.Explorer.Projects.Single().Directories.Contains(
                "ssh://host-test/srv/hypershell/project",
                StringComparer.OrdinalIgnoreCase)
            && applied.UnmappedRemoteProjectIds.Count == 0
            && secondProfileService.CreateSnapshot(
                    secondSettings,
                    EmptyCatalog(Path.Combine(secondRoot, "extensions")))
                .Document.Projects.Single().WorkspaceLocationUri
               == "ssh://host-test/srv/hypershell/project",
            "Applying a roaming profile did not preserve both the second PC mapping and stable remote URI.");

        var firstConversations = new LocalAiConversationStore(
            Path.Combine(firstRoot, "conversations"),
            Path.Combine(firstRoot, "conflicts"));
        var conversation = CreateConversation(settings.PortableProfileId);
        firstConversations.Save(conversation);
        var conversationSync = new ConversationRoamingService(
            firstConversations,
            firstState);
        var pushed = await conversationSync.UploadAllAsync(
            transport,
            endpoint,
            settings.PortableProfileId);
        var unchanged = await conversationSync.UploadAllAsync(
            transport,
            endpoint,
            settings.PortableProfileId);
        Require(
            pushed.UploadedConversations == 1
            && unchanged.UnchangedConversations == 1,
            "Conversation upload was not incremental and idempotent.");

        var secondConversations = new LocalAiConversationStore(
            Path.Combine(secondRoot, "conversations"),
            Path.Combine(secondRoot, "conflicts"));
        var secondConversationSync = new ConversationRoamingService(
            secondConversations,
            secondState);
        var pulled = await secondConversationSync.DownloadAllAsync(
            transport,
            endpoint,
            settings.PortableProfileId);
        var restoredConversation = secondConversations
            .Load(settings.PortableProfileId)
            .Single(document => document.Id == conversation.Id);
        Require(
            pulled.DownloadedConversations == 1
            && restoredConversation.Items.Count == 205
            && restoredConversation.Items[4].Text.Contains(
                "[REDACTED_TOKEN]",
                StringComparison.Ordinal)
            && !restoredConversation.Items[4].Text.Contains(
                "sk_verifier",
                StringComparison.Ordinal),
            "Paginated conversation restore or client-side secret redaction failed.");
        var restartedConversationStore = new LocalAiConversationStore(
            Path.Combine(secondRoot, "conversations"),
            Path.Combine(secondRoot, "conflicts"));
        Require(
            restartedConversationStore.Load(settings.PortableProfileId)
                .Single(document => document.Id == conversation.Id)
                .Items.Count == 205,
            "Complete local conversation history did not survive restart.");

        var vaultPackages = await transport.ListPackagesAsync();
        var vault = vaultPackages.FirstOrDefault(package => !package.IsOfficial)
                    ?? throw new InvalidOperationException(
                        "The server verifier did not leave a private Hyperlib package in the vault.");
        var packageRoot = Path.Combine(secondRoot, "package-restore");
        var packageService = new HyperlibPackageService(
            Path.Combine(packageRoot, "extensions"),
            Path.Combine(packageRoot, "staging"),
            Path.Combine(packageRoot, "recovery"));
        var packageRoaming = new HyperlibPackageRoamingService(
            packageService,
            Path.Combine(packageRoot, "cache"));
        var packageOutcome = await packageRoaming.RestoreMissingAsync(
            transport,
            EmptyCatalog(Path.Combine(packageRoot, "extensions")),
            [
                new HyperlibPackageSelection
                {
                    PackageId = vault.PackageId,
                    Version = vault.Version,
                    ContentSha256 = vault.ContentSha256,
                    IsOfficial = false,
                    IsEnabled = true
                }
            ]);
        var restoredCatalog = new ExtensionDiscoveryService(
            Path.Combine(packageRoot, "extensions")).Discover();
        Require(
            packageOutcome.Downloaded == 1
            && restoredCatalog.Packages.Any(package => string.Equals(
                package.PackageSha256,
                vault.ContentSha256,
                StringComparison.OrdinalIgnoreCase)),
            "The desktop package coordinator did not restore and verify the private Hyperlib archive.");

        Console.WriteLine(
            "DESKTOP_ROAMING_OK|local-path-isolation|etag-conflict-copies|device-mappings|conversation-205-items|incremental-upload|secret-redaction|restart-persistence|private-package-restore|http-v2-transport");
    }

    private static AppSettings CreateSettings(string localProjectRoot)
    {
        var settings = new AppSettings
        {
            PortableProfileId = "desktop-roaming-profile",
            PortableProfileDisplayName = "Desktop roaming verifier"
        };
        settings.Explorer.Projects =
            new ObservableCollection<ProjectDefinition>
            {
                new()
                {
                    Id = "project:desktop-roaming",
                    Name = "Desktop roaming",
                    Directories = new ObservableCollection<string>
                    {
                        localProjectRoot,
                        "ssh://host-test/srv/hypershell/project"
                    }
                }
            };
        settings.Explorer.ActiveProjectId = "project:desktop-roaming";
        return settings;
    }

    private static LocalAiConversationDocument CreateConversation(
        string profileId)
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var items = Enumerable.Range(1, 205)
            .Select(index => new AiConversationItem
            {
                Sequence = index,
                Role = index % 2 == 0
                    ? AiConversationRole.Assistant
                    : AiConversationRole.User,
                Kind = AiConversationItemKind.Message,
                Text = index == 5
                    ? "Do not roam sk_verifier_abcdefghijklmnopqrstuvwxyz012345"
                    : $"Conversation item {index}",
                CreatedAtUtc = created.AddSeconds(index)
            })
            .ToList();
        return new LocalAiConversationDocument
        {
            Id = "conversation:desktop-roaming",
            ProfileId = profileId,
            Title = "Desktop roaming history",
            IsOpenOnThisDevice = true,
            CompactedContext = "[]",
            Items = items,
            CreatedAtUtc = created,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ExtensionDiscoverySnapshot EmptyCatalog(string root) =>
        new()
        {
            RootDirectory = root,
            ScannedAtUtc = DateTimeOffset.UtcNow
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
