using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

internal static class ProfilePortabilityVerifier
{
    public static Task VerifyAsync(string verifierRoot)
    {
        var profileRoot = Path.Combine(verifierRoot, "profile-portability");
        Directory.CreateDirectory(profileRoot);
        var service = new PortableProfileService();
        var emptyExtensionCatalog = new ExtensionDiscoverySnapshot
        {
            RootDirectory = Path.Combine(profileRoot, "extensions"),
            ScannedAtUtc = DateTimeOffset.UtcNow
        };

        var source = CreateDeviceSettings(profileRoot, "source-device");
        source.PortableProfileId = "profile-portability-verifier";
        source.PortableProfileDisplayName = "Verifier profile";
        source.PortableProfileRevision = 7;
        source.Application.ShowSaveStatusInTitleBar = false;
        source.CommandBuilder.OpenCompatibleTerminalAutomatically = false;
        source.Explorer.ShowHiddenItems = true;
        source.Explorer.DirectoriesFirst = false;
        source.TerminalConfigurationProfiles[0].Settings.FontSize = 19;
        source.TerminalConfigurationProfiles[0].Settings.ForegroundColor = "#AABBCC";
        source.TerminalConfigurationProfiles[0].TabColor = "#112233";
        source.Environments =
        [
            new EnvironmentProfile
            {
                Id = "portable-command-profile",
                Name = "Portable command profile",
                Description = "Verification profile",
                Language = "PowerShell",
                LibraryIds = ["vendor.extension.library-one"],
                SourceLibraryId = "vendor.extension.library-one"
            }
        ];
        source.ActiveEnvironmentId = "portable-command-profile";
        source.ProfileExtensions =
        [
            new ProfileExtensionReference
            {
                ExtensionId = "vendor.extension",
                VersionRange = "1.0.0",
                Enabled = true,
                ActivateOnStartup = true
            }
        ];
        source.ProfileLibraries =
        [
            new ProfileLibraryReference
            {
                ExtensionId = "vendor.extension",
                LibraryId = "library-one",
                VersionRange = "1.0.0",
                Enabled = true
            }
        ];

        var snapshot = service.CreateSnapshot(source, emptyExtensionCatalog);
        Require(
            snapshot.Configuration.WorkspaceLayout.IsUserUnlocked == false,
            "Portable workspace layout remained in edit mode.");
        Require(
            snapshot.Configuration.Explorer.ShowHiddenItems
            && !snapshot.Configuration.Explorer.DirectoriesFirst,
            "Portable Explorer display preferences were not captured.");

        var bundlePath = Path.Combine(profileRoot, "verifier.hyperprofile");
        var envelope = service.Export(bundlePath, snapshot);
        Require(File.Exists(bundlePath), "Portable profile export did not create a file.");
        Require(
            string.Equals(
                envelope.ProfileSha256,
                service.ComputeProfileSha256(snapshot),
                StringComparison.OrdinalIgnoreCase),
            "Portable profile hash was not deterministic.");
        Require(
            !Directory.EnumerateFiles(profileRoot, "*.tmp").Any(),
            "Atomic profile export left a temporary file behind.");

        var payload = File.ReadAllText(bundlePath);
        foreach (var forbiddenValue in new[]
                 {
                     "source-device-local-root",
                     "source-device-project-root",
                     "source-device-shell.exe",
                     "source-device-initialization-command",
                     "source-device-startup-command",
                     "source-device-background.png"
                 })
        {
            Require(
                !payload.Contains(forbiddenValue, StringComparison.Ordinal),
                $"Device-local value '{forbiddenValue}' leaked into the portable payload.");
        }

        var preview = service.Inspect(
            bundlePath,
            emptyExtensionCatalog,
            source.TerminalProfiles.ToList());
        Require(preview.CanImport, "A valid portable profile failed inspection.");
        Require(
            preview.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "PROFILE_EXTENSION_MISSING")
            && preview.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "PROFILE_LIBRARY_MISSING"),
            "Missing portable dependencies were not reported as compatibility warnings.");

        var target = CreateDeviceSettings(profileRoot, "target-device");
        var merged = service.CreateMergedSettings(
            target,
            preview.Envelope!.Profile,
            WorkspacePanelRegistry.CreateWithBuiltIns());
        Require(
            merged.Application.ShowSaveStatusInTitleBar == false
            && merged.CommandBuilder.OpenCompatibleTerminalAutomatically == false,
            "Portable application or Command Builder preferences were not merged.");
        Require(
            merged.Explorer.ShowHiddenItems
            && !merged.Explorer.DirectoriesFirst,
            "Portable Explorer display preferences were not merged.");
        Require(
            merged.Explorer.StartupDirectory.Contains(
                "target-device-local-root",
                StringComparison.Ordinal)
            && merged.Explorer.Projects.Single().Directories.Single().Contains(
                "target-device-project-root",
                StringComparison.Ordinal)
            && merged.Explorer.UseAsTerminalStartupDirectory,
            "Target-device Explorer paths or path policy were overwritten.");
        var mergedTerminal = merged.TerminalProfiles.Single(profile =>
            profile.Id == "powershell");
        var mergedConfiguration = merged.TerminalConfigurationProfiles.Single();
        Require(
            mergedTerminal.Settings.FontSize == 19
            && mergedTerminal.Settings.ForegroundColor == "#AABBCC"
            && mergedConfiguration.TabColor == "#112233",
            "Portable terminal appearance did not merge.");
        Require(
            mergedTerminal.ExecutablePath.Contains(
                "target-device-shell.exe",
                StringComparison.Ordinal)
            && mergedTerminal.InitializationCommand.Contains(
                "target-device-initialization-command",
                StringComparison.Ordinal)
            && mergedTerminal.StartupCommand.Contains(
                "target-device-startup-command",
                StringComparison.Ordinal)
            && mergedTerminal.Settings.BackgroundImagePath.Contains(
                "target-device-background.png",
                StringComparison.Ordinal)
            && mergedConfiguration.StartupCommand.Contains(
                "target-device-configuration-command",
                StringComparison.Ordinal)
            && mergedConfiguration.Settings.StartupDirectory.Contains(
                "target-device-configuration-root",
                StringComparison.Ordinal),
            "Target-device terminal executable, command, image, or directory state was overwritten.");
        Require(
            merged.ActiveEnvironmentId == "portable-command-profile"
            && merged.Environments.Single().LibraryIds.Single()
                == "vendor.extension.library-one",
            "Portable command profiles were not merged.");

        var tamperedPath = Path.Combine(profileRoot, "tampered.hyperprofile");
        File.WriteAllText(
            tamperedPath,
            payload.Replace(
                "Verifier profile",
                "Tampered profile",
                StringComparison.Ordinal));
        var tampered = service.Inspect(
            tamperedPath,
            emptyExtensionCatalog,
            source.TerminalProfiles.ToList());
        Require(
            !tampered.CanImport
            && tampered.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "PROFILE_HASH_MISMATCH"),
            "Tampered profile content was not rejected by its integrity hash.");

        var unknownMemberPath = Path.Combine(profileRoot, "unknown.hyperprofile");
        File.WriteAllText(
            unknownMemberPath,
            payload.Replace(
                "{",
                "{\n  \"unexpectedMember\": true,",
                StringComparison.Ordinal));
        var unknownMember = service.Inspect(
            unknownMemberPath,
            emptyExtensionCatalog,
            source.TerminalProfiles.ToList());
        Require(
            !unknownMember.CanImport
            && unknownMember.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "PROFILE_JSON"),
            "An unmapped profile member was not rejected.");

        var missingMemberPath = Path.Combine(profileRoot, "missing.hyperprofile");
        var missingMemberRoot = JsonNode.Parse(payload)!.AsObject();
        missingMemberRoot["profile"]!["configuration"]!["explorer"]!
            .AsObject()
            .Remove("directoriesFirst");
        File.WriteAllText(
            missingMemberPath,
            missingMemberRoot.ToJsonString());
        var missingMember = service.Inspect(
            missingMemberPath,
            emptyExtensionCatalog,
            source.TerminalProfiles.ToList());
        Require(
            !missingMember.CanImport
            && missingMember.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "PROFILE_JSON"),
            "A missing required profile member was not rejected.");

        var oversizedPath = Path.Combine(profileRoot, "oversized.hyperprofile");
        using (var stream = new FileStream(
                   oversizedPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.SetLength(ProfileContract.MaximumBundleBytes + 1);
        }

        var oversized = service.Inspect(
            oversizedPath,
            emptyExtensionCatalog,
            source.TerminalProfiles.ToList());
        Require(
            !oversized.CanImport
            && oversized.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "PROFILE_TOO_LARGE"),
            "An oversized profile bundle was not rejected before parsing.");

        Console.WriteLine(
            "PROFILE_PORTABILITY_OK|roundtrip|tamper-rejected|strict-members|device-state-preserved|bounded-import");
        return Task.CompletedTask;
    }

    private static AppSettings CreateDeviceSettings(
        string root,
        string deviceName)
    {
        var localRoot = Path.Combine(root, $"{deviceName}-local-root");
        var projectRoot = Path.Combine(root, $"{deviceName}-project-root");
        var terminalSettings = new TerminalSettings
        {
            FontFamilyName = "VT323",
            FontSize = 15,
            ForegroundColor = "#E6EDF3",
            BackgroundColor = "#0D1117",
            AccentColor = "#58A6FF",
            ErrorColor = "#FF7B72",
            StartupDirectory = localRoot,
            BackgroundImagePath = Path.Combine(
                root,
                $"{deviceName}-background.png")
        };
        var terminalProfile = new TerminalProfile
        {
            Id = "powershell",
            Name = "PowerShell",
            ShellKind = TerminalShellKind.PowerShell,
            ExecutablePath = Path.Combine(root, $"{deviceName}-shell.exe"),
            InitializationCommand = $"{deviceName}-initialization-command",
            StartupCommand = $"{deviceName}-startup-command",
            IsBuiltIn = false,
            Settings = terminalSettings,
            TabTitleTemplate = "PowerShell {n}",
            TabColor = "#28374A"
        };
        var configurationSettings = terminalSettings.Clone();
        configurationSettings.StartupDirectory = Path.Combine(
            root,
            $"{deviceName}-configuration-root");
        configurationSettings.BackgroundImagePath = Path.Combine(
            root,
            $"{deviceName}-configuration-background.png");
        return new AppSettings
        {
            Application = new ApplicationPreferences(),
            CommandBuilder = new CommandBuilderPreferences(),
            Explorer = new ExplorerSettings
            {
                StartupDirectory = localRoot,
                UseAsTerminalStartupDirectory = true,
                ShowHiddenItems = false,
                DirectoriesFirst = true,
                ActiveProjectId = "device-project",
                Projects =
                [
                    new ProjectDefinition
                    {
                        Id = "device-project",
                        Name = "Device project",
                        Directories = [projectRoot]
                    }
                ]
            },
            WorkspaceLayout = new WorkspaceLayoutSnapshot
            {
                IsUserUnlocked = true,
                Panels = []
            },
            Terminal = terminalSettings,
            ActiveTerminalProfileId = "powershell",
            TerminalProfiles = [terminalProfile],
            TerminalConfigurationProfiles =
            [
                new TerminalConfigurationProfile
                {
                    Id = "shared-terminal",
                    Name = "Shared terminal",
                    Description = "Portable appearance with local launch state",
                    TargetTerminalProfileIds = ["powershell"],
                    Settings = configurationSettings,
                    TabTitleTemplate = "Shell {n}",
                    TabColor = "#28313D",
                    StartupCommand =
                        $"{deviceName}-configuration-command"
                }
            ],
            ActiveEnvironmentId = "device-command-profile",
            Environments =
            [
                new EnvironmentProfile
                {
                    Id = "device-command-profile",
                    Name = "Device command profile",
                    Language = "PowerShell",
                    LibraryIds = ["powershell-core"],
                    SourceLibraryId = "powershell-core"
                }
            ]
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
