using Hypershell.Extensibility;
using Hypershell.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Profiles;

public sealed class PortableProfileEnvelope
{
    [JsonRequired]
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } =
        "https://hypershell.dev/schemas/profile-bundle-v1.json";
    [JsonRequired]
    public string Format { get; init; } = ProfileContract.BundleFormat;
    [JsonRequired]
    public int BundleVersion { get; init; } =
        ProfileContract.CurrentBundleVersion;
    [JsonRequired]
    public DateTimeOffset ExportedAtUtc { get; init; } =
        DateTimeOffset.UtcNow;
    [JsonRequired]
    public string ExportedBy { get; init; } = "Hypershell";
    [JsonRequired]
    public string ProfileSha256 { get; init; } = string.Empty;
    [JsonRequired]
    public PortableProfileSnapshot Profile { get; init; } = new();
}

public sealed class PortableProfileSnapshot
{
    [JsonRequired]
    public int SchemaVersion { get; init; } =
        ProfileContract.CurrentPortableSchemaVersion;
    [JsonRequired]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired]
    public string DisplayName { get; init; } = "Default";
    [JsonRequired]
    public long Revision { get; init; }
    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    [JsonRequired]
    public PortableWorkspacePreferences WorkspacePreferences { get; init; } = new();
    [JsonRequired]
    public PortableConfigurationSnapshot Configuration { get; init; } = new();
    [JsonRequired]
    public IReadOnlyList<ProfileExtensionReference> Extensions { get; init; } = [];
    [JsonRequired]
    public IReadOnlyList<ProfileLibraryReference> Libraries { get; init; } = [];
    [JsonRequired]
    public IReadOnlyList<PortableWorkspaceReference> Workspaces { get; init; } = [];
}

public sealed class PortableConfigurationSnapshot
{
    [JsonRequired]
    public PortableApplicationPreferences Application { get; init; } = new();
    [JsonRequired]
    public PortableCommandBuilderPreferences CommandBuilder { get; init; } = new();
    [JsonRequired]
    public PortableExplorerPreferences Explorer { get; init; } = new();
    [JsonRequired]
    public string ActiveTerminalProfileId { get; init; } = "powershell";
    [JsonRequired]
    public IReadOnlyList<PortableTerminalConfigurationProfile> TerminalConfigurations
        { get; init; } = [];
    [JsonRequired]
    public string ActiveEnvironmentId { get; init; } = "powershell";
    [JsonRequired]
    public IReadOnlyList<PortableEnvironmentProfile> Environments { get; init; } = [];
    [JsonRequired]
    public WorkspaceLayoutSnapshot WorkspaceLayout { get; init; } = new();
}

public sealed class PortableApplicationPreferences
{
    [JsonRequired]
    public bool OpenFileExplorerOnStartup { get; init; } = true;
    [JsonRequired]
    public bool OpenCommandBuilderOnStartup { get; init; } = true;
    [JsonRequired]
    public bool CreateTerminalOnStartup { get; init; } = true;
    [JsonRequired]
    public bool ShowSaveStatusInTitleBar { get; init; } = true;
    [JsonRequired]
    public NewTabPlacement NewTabPlacement { get; init; } =
        NewTabPlacement.AfterLast;
}

public sealed class PortableCommandBuilderPreferences
{
    public string ActiveLanguage { get; init; } = string.Empty;
    [JsonRequired]
    public bool OpenCompatibleTerminalAutomatically { get; init; } = true;
    [JsonRequired]
    public bool OpenBuilderWhenPathsAreInserted { get; init; } = true;
}

public sealed class PortableExplorerPreferences
{
    [JsonRequired]
    public bool ShowHiddenItems { get; init; }
    [JsonRequired]
    public bool DirectoriesFirst { get; init; } = true;
}

public sealed class PortableTerminalConfigurationProfile
{
    [JsonRequired]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired]
    public string Name { get; init; } = "Terminal profile";
    [JsonRequired]
    public string Description { get; init; } = string.Empty;
    [JsonRequired]
    public IReadOnlyList<string> TargetTerminalProfileIds { get; init; } = [];
    [JsonRequired]
    public PortableTerminalSettings Settings { get; init; } = new();
    [JsonRequired]
    public string TabTitleTemplate { get; init; } = "{shell} {n}";
    [JsonRequired]
    public string TabColor { get; init; } = "#28313D";
}

public sealed class PortableTerminalSettings
{
    [JsonRequired]
    public string FontFamilyName { get; init; } = "VT323";
    [JsonRequired]
    public double FontSize { get; init; } = 15;
    [JsonRequired]
    public bool UseBoldText { get; init; }
    [JsonRequired]
    public bool UseItalicText { get; init; }
    [JsonRequired]
    public string ForegroundColor { get; init; } = "#E6EDF3";
    [JsonRequired]
    public string BackgroundColor { get; init; } = "#0D1117";
    [JsonRequired]
    public string AccentColor { get; init; } = "#58A6FF";
    [JsonRequired]
    public string ErrorColor { get; init; } = "#FF7B72";
    [JsonRequired]
    public bool ClearOnRun { get; init; }
    [JsonRequired]
    public bool EnterRunsCommand { get; init; } = true;
    [JsonRequired]
    public bool ShowTimestamps { get; init; }
    [JsonRequired]
    public TerminalTextBackdropMode TextBackdropMode { get; init; } =
        TerminalTextBackdropMode.CurrentInput;
    [JsonRequired]
    public double TextBackdropOpacity { get; init; } = 0.35;
}

public sealed class PortableEnvironmentProfile
{
    [JsonRequired]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired]
    public string Name { get; init; } = "Command profile";
    [JsonRequired]
    public string Description { get; init; } = string.Empty;
    [JsonRequired]
    public string Language { get; init; } = string.Empty;
    [JsonRequired]
    public IReadOnlyList<string> LibraryIds { get; init; } = [];
    [JsonRequired]
    public string SourceLibraryId { get; init; } = string.Empty;
}

public sealed class PortableWorkspacePreferences
{
    [JsonRequired]
    public string ThemeId { get; init; } = "hypershell-dark";
    [JsonRequired]
    public bool RestoreWorkspaceOnStart { get; init; } = true;
    [JsonRequired]
    public bool RestoreOpenDocuments { get; init; } = true;
    [JsonRequired]
    public bool PreferWorkspaceScopedAiAccess { get; init; } = true;
    [JsonRequired]
    public Dictionary<string, JsonElement> ExtensionSettings { get; init; } = [];
}

public sealed class ProfileExtensionReference
{
    [JsonRequired]
    public string ExtensionId { get; init; } = string.Empty;
    [JsonRequired]
    public string VersionRange { get; init; } = "*";
    [JsonRequired]
    public bool Enabled { get; init; } = true;
    [JsonRequired]
    public bool ActivateOnStartup { get; init; }
}

public sealed class ProfileLibraryReference
{
    [JsonRequired]
    public string ExtensionId { get; init; } = string.Empty;
    [JsonRequired]
    public string LibraryId { get; init; } = string.Empty;
    [JsonRequired]
    public string VersionRange { get; init; } = "*";
    [JsonRequired]
    public bool Enabled { get; init; } = true;
}

public sealed class PortableWorkspaceReference
{
    [JsonRequired]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired]
    public string DisplayName { get; init; } = "Workspace";
    [JsonRequired]
    public string RepositoryId { get; init; } = string.Empty;
    [JsonRequired]
    public string RepositorySubdirectory { get; init; } = string.Empty;
    [JsonRequired]
    public IReadOnlyList<string> OpenDocumentPaths { get; init; } = [];
    [JsonRequired]
    public WorkspaceLayoutSnapshot Layout { get; init; } = new();
}

public sealed class WorkspaceLayoutSnapshot
{
    public bool IsUserUnlocked { get; init; }
    public IReadOnlyList<PanelPlacementSnapshot> Panels { get; init; } = [];
    public IReadOnlyList<WorkspaceSideToolPlacementSnapshot> SideTools { get; init; } = [];

    public WorkspaceLayoutSnapshot Clone() =>
        new()
        {
            IsUserUnlocked = IsUserUnlocked,
            Panels = Panels.Select(panel => panel.Clone()).ToList(),
            SideTools = SideTools.Select(tool => tool.Clone()).ToList()
        };
}

public sealed class WorkspaceSideToolPlacementSnapshot
{
    public string ToolId { get; init; } = string.Empty;
    public int Order { get; init; }
    public bool IsVisible { get; init; } = true;

    public WorkspaceSideToolPlacementSnapshot Clone() =>
        new()
        {
            ToolId = ToolId,
            Order = Order,
            IsVisible = IsVisible
        };
}

public sealed class PanelPlacementSnapshot
{
    public string ExtensionId { get; init; } = string.Empty;
    public string PanelId { get; init; } = string.Empty;
    public WorkspacePanelSlot Slot { get; init; }
    public BuiltInWorkspaceSurface? Replaces { get; init; }
    public int Order { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool IsVisible { get; init; } = true;

    public PanelPlacementSnapshot Clone() =>
        new()
        {
            ExtensionId = ExtensionId,
            PanelId = PanelId,
            Slot = Slot,
            Replaces = Replaces,
            Order = Order,
            Width = Width,
            Height = Height,
            IsVisible = IsVisible
        };
}

// This state is intentionally excluded from cloud profile synchronization.
public sealed class DeviceLocalProfileState
{
    public int SchemaVersion { get; init; } =
        ProfileContract.CurrentDeviceStateSchemaVersion;
    public string DeviceId { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public Dictionary<string, string> WorkspaceLocalPaths { get; init; } = [];
    public IReadOnlyList<NativeExtensionApproval> NativeExtensionApprovals { get; init; } = [];
    public IReadOnlyList<WorkspaceAiApproval> WorkspaceAiApprovals { get; init; } = [];
    public IReadOnlyList<string> SecretReferenceIds { get; init; } = [];
}

public sealed class NativeExtensionApproval
{
    public string ExtensionId { get; init; } = string.Empty;
    public string ExtensionVersion { get; init; } = string.Empty;
    public string PackageSha256 { get; init; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; init; }
    public IReadOnlyList<ExtensionCapability> ApprovedCapabilities { get; init; } = [];
}

public enum WorkspaceAiTerminalExecutionPolicy
{
    ConfirmEveryCommand,
    ReviewOnly,
    AutoExecute
}

public sealed class WorkspaceAiApproval
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LocalRootPath { get; init; } = string.Empty;
    public string RootFingerprint { get; init; } = string.Empty;
    public bool AllowRead { get; init; } = true;
    public bool AllowWrite { get; init; }
    public bool AllowAutomaticFileAndEditorChanges { get; init; }
    public bool AllowTerminalRead { get; init; }
    public bool AllowTerminal { get; init; }
    public WorkspaceAiTerminalExecutionPolicy TerminalExecutionPolicy
    {
        get;
        init;
    } = WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand;
    public bool AllowNetwork { get; init; }
    public DateTimeOffset ApprovedAtUtc { get; init; }
}
