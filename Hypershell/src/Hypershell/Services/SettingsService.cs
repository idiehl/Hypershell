using Hypershell.Models;
using Hypershell.Extensibility;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class SettingsService
{
    public const string ApplicationDataOverrideVariable =
        "HYPERSHELL_APP_DATA";
    public const string CommandProfileOverrideVariable =
        "HYPERSHELL_COMMAND_PROFILE";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsService(string? applicationDataDirectory = null)
    {
        var environmentDirectory = Environment.GetEnvironmentVariable(
            ApplicationDataOverrideVariable);
        ApplicationDataDirectory = applicationDataDirectory
                                   ?? (!string.IsNullOrWhiteSpace(
                                           environmentDirectory)
                                       ? Path.GetFullPath(environmentDirectory)
                                       : Path.Combine(
                                           Environment.GetFolderPath(
                                               Environment.SpecialFolder
                                                   .LocalApplicationData),
                                           "Hypershell"));
        SettingsPath = Path.Combine(ApplicationDataDirectory, "settings.json");
        ProfileAvatarPath = Path.Combine(
            ApplicationDataDirectory,
            "profile-avatar.device.png");
        CommandLibraryDirectory = Path.Combine(ApplicationDataDirectory, "command-libraries");
        ExtensionDirectory = Path.Combine(ApplicationDataDirectory, "extensions");
        ExtensionStagingDirectory = Path.Combine(
            ApplicationDataDirectory,
            "extension-staging");
        ExtensionRecoveryDirectory = Path.Combine(
            ApplicationDataDirectory,
            "extension-recovery");
        ExtensionStatePath = Path.Combine(ApplicationDataDirectory, "extensions.device.json");
        ExtensionWebViewDataDirectory = Path.Combine(
            ApplicationDataDirectory,
            "webview2",
            "extension-panels");
        ExtensionPanelStateDirectory = Path.Combine(
            ApplicationDataDirectory,
            "extension-panel-state");
        EditorSessionPath = Path.Combine(
            ApplicationDataDirectory,
            "editor-session.device.json");
        EditorRecoveryDirectory = Path.Combine(
            ApplicationDataDirectory,
            "editor-recovery");
        RemoteDocumentCacheDirectory = Path.Combine(
            ApplicationDataDirectory,
            "remote-document-cache");
        TerminalBackgroundDirectory = Path.Combine(ApplicationDataDirectory, "terminal-backgrounds");
        ProfileSyncConfigurationPath = Path.Combine(
            ApplicationDataDirectory,
            "profile-sync.device.json");
        ProfileSyncStatePath = Path.Combine(
            ApplicationDataDirectory,
            "profile-sync-checkpoints.device.json");
        ProfileSyncCredentialPath = Path.Combine(
            ApplicationDataDirectory,
            "profile-sync-credential.device.bin");
        SshCredentialVaultPath = Path.Combine(
            ApplicationDataDirectory,
            "ssh-credentials.device.bin");
        ProfileConflictDirectory = Path.Combine(
            ApplicationDataDirectory,
            "profile-conflicts");
        ProfileRoamingStatePath = Path.Combine(
            ApplicationDataDirectory,
            "profile-roaming.device.json");
        ConversationDirectory = Path.Combine(
            ApplicationDataDirectory,
            "conversations");
        ProfilePackageCacheDirectory = Path.Combine(
            ApplicationDataDirectory,
            "profile-package-cache");
        ApplicationUpdateDirectory = Path.Combine(
            ApplicationDataDirectory,
            "updates");
        WorkspaceAiPermissionPath = Path.Combine(
            ApplicationDataDirectory,
            "ai-permissions.device.json");
        WorkspaceAiAuditPath = Path.Combine(
            ApplicationDataDirectory,
            "ai-operations.audit.jsonl");
        WorkspaceAiProviderConfigurationPath = Path.Combine(
            ApplicationDataDirectory,
            "ai-provider.device.json");
        WorkspaceAiProviderCredentialPath = Path.Combine(
            ApplicationDataDirectory,
            "ai-provider-credential.device.bin");
        WorkspaceAiContextPath = Path.Combine(
            ApplicationDataDirectory,
            "ai-contexts.device.json");
        AtlasMyNetDirectory = Path.Combine(
            ApplicationDataDirectory,
            "AtlasMyNet");
        AtlasMyNetAppsPath = Path.Combine(AtlasMyNetDirectory, "apps.json");
        AtlasMyNetTrustPath = Path.Combine(
            AtlasMyNetDirectory,
            "trust.device.json");
        NetworkObservatoryStatePath = Path.Combine(
            AtlasMyNetDirectory,
            "network-observatory.device.json");
    }

    public string ApplicationDataDirectory { get; }
    public string SettingsPath { get; }
    public string ProfileAvatarPath { get; }
    public string CommandLibraryDirectory { get; }
    public string ExtensionDirectory { get; }
    public string ExtensionStagingDirectory { get; }
    public string ExtensionRecoveryDirectory { get; }
    public string ExtensionStatePath { get; }
    public string ExtensionWebViewDataDirectory { get; }
    public string ExtensionPanelStateDirectory { get; }
    public string EditorSessionPath { get; }
    public string EditorRecoveryDirectory { get; }
    public string RemoteDocumentCacheDirectory { get; }
    public string TerminalBackgroundDirectory { get; }
    public string ProfileSyncConfigurationPath { get; }
    public string ProfileSyncStatePath { get; }
    public string ProfileSyncCredentialPath { get; }
    public string SshCredentialVaultPath { get; }
    public string ProfileConflictDirectory { get; }
    public string ProfileRoamingStatePath { get; }
    public string ConversationDirectory { get; }
    public string ProfilePackageCacheDirectory { get; }
    public string ApplicationUpdateDirectory { get; }
    public string WorkspaceAiPermissionPath { get; }
    public string WorkspaceAiAuditPath { get; }
    public string WorkspaceAiProviderConfigurationPath { get; }
    public string WorkspaceAiProviderCredentialPath { get; }
    public string WorkspaceAiContextPath { get; }
    public string AtlasMyNetDirectory { get; }
    public string AtlasMyNetAppsPath { get; }
    public string AtlasMyNetTrustPath { get; }
    public string NetworkObservatoryStatePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var defaultSettings = new AppSettings();
                EnsureValidSettings(defaultSettings);
                ApplyStartupOverrides(defaultSettings);
                return defaultSettings;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), _jsonOptions) ?? new AppSettings();
            EnsureValidSettings(settings);
            ApplyStartupOverrides(settings);
            return settings;
        }
        catch
        {
            var fallbackSettings = new AppSettings();
            EnsureValidSettings(fallbackSettings);
            ApplyStartupOverrides(fallbackSettings);
            return fallbackSettings;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        var temporaryPath = $"{SettingsPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, _jsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }

    public string ImportTerminalBackground(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected background image could not be found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".gif")
        {
            throw new InvalidOperationException("Choose a PNG, JPEG, BMP, or GIF image.");
        }

        Directory.CreateDirectory(TerminalBackgroundDirectory);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (string.Equals(
                Path.GetDirectoryName(fullSourcePath),
                Path.GetFullPath(TerminalBackgroundDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return fullSourcePath;
        }

        using var stream = File.OpenRead(fullSourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant();
        var baseName = string.Concat(Path.GetFileNameWithoutExtension(fullSourcePath)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "background";
        }

        var destinationPath = Path.Combine(TerminalBackgroundDirectory, $"{baseName}-{hash}{extension}");
        if (!File.Exists(destinationPath))
        {
            File.Copy(fullSourcePath, destinationPath);
        }

        return destinationPath;
    }

    private static void EnsureValidSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PortableProfileId))
        {
            settings.PortableProfileId = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(settings.PortableProfileDisplayName))
        {
            settings.PortableProfileDisplayName = "Default";
        }

        settings.PortableProfileRevision = Math.Max(0, settings.PortableProfileRevision);
        if (settings.PortableProfileUpdatedAtUtc == default)
        {
            settings.PortableProfileUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        settings.ProfileExtensions ??= [];
        settings.ProfileLibraries ??= [];
        settings.Application ??= new ApplicationPreferences();
        settings.CommandBuilder ??= new CommandBuilderPreferences();
        settings.Explorer ??= new ExplorerSettings();
        settings.WorkspaceLayout = WorkspaceLayoutService.Normalize(
            settings.WorkspaceLayout,
            WorkspacePanelRegistry.CreateWithBuiltIns());
        settings.Terminal ??= new TerminalSettings();
        settings.TerminalConfigurationProfiles ??= [];
        if (settings.TypographyVersion < 3)
        {
            settings.Terminal.FontFamilyName = "VT323";
            settings.Terminal.FontSize = 15;
            settings.Terminal.UseBoldText = false;
            settings.TypographyVersion = 3;
        }

        if (settings.InputBehaviorVersion < 1)
        {
            settings.Terminal.EnterRunsCommand = true;
            settings.InputBehaviorVersion = 1;
        }

        TerminalProfileService.ReconcileProfiles(settings);
        if (string.IsNullOrWhiteSpace(settings.Explorer.StartupDirectory))
        {
            settings.Explorer.StartupDirectory = settings.Terminal.StartupDirectory;
        }

        settings.Explorer.Projects ??= [];
        foreach (var project in settings.Explorer.Projects)
        {
            project.Directories ??= [];
            project.Directories = new(
                project.Directories
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        foreach (var profile in settings.TerminalConfigurationProfiles)
        {
            profile.Settings ??= settings.Terminal.Clone();
            profile.TargetTerminalProfileIds ??= [];
        }

        if (settings.Environments is null || settings.Environments.Count == 0)
        {
            settings.Environments = new AppSettings().Environments;
        }

        // The original combined Engineering preset was only a convenience
        // profile. Library profiles and user-created profiles now replace it.
        for (var index = settings.Environments.Count - 1; index >= 0; index--)
        {
            if (string.Equals(settings.Environments[index].Id, "engineering", StringComparison.OrdinalIgnoreCase))
            {
                settings.Environments.RemoveAt(index);
            }
        }

        if (settings.Environments.Count == 0)
        {
            settings.Environments = new AppSettings().Environments;
        }

        if (!settings.Environments.Any(environment => environment.Id == settings.ActiveEnvironmentId))
        {
            settings.ActiveEnvironmentId = settings.Environments[0].Id;
        }

        if (string.IsNullOrWhiteSpace(settings.CommandBuilder.ActiveLanguage))
        {
            settings.CommandBuilder.ActiveLanguage =
                settings.Environments.FirstOrDefault(environment =>
                    string.Equals(
                        environment.Id,
                        settings.ActiveEnvironmentId,
                        StringComparison.OrdinalIgnoreCase))?.Language
                ?? "PowerShell";
        }

        settings.CommandBuilder.ActiveLanguage =
            CommandLanguageRegistry.NormalizeDisplayName(
                settings.CommandBuilder.ActiveLanguage);
    }

    private static void ApplyStartupOverrides(AppSettings settings)
    {
        var profileId = Environment.GetEnvironmentVariable(
            CommandProfileOverrideVariable);
        if (!string.IsNullOrWhiteSpace(profileId)
            && settings.Environments.Any(environment => string.Equals(
                environment.Id,
                profileId,
                StringComparison.OrdinalIgnoreCase)))
        {
            settings.ActiveEnvironmentId = profileId;
        }
    }
}
