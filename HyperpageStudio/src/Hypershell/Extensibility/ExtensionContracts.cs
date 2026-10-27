using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Extensibility;

public static class ExtensionContract
{
    public const int CurrentManifestSchemaVersion = 1;

    public static JsonSerializerOptions CreateJsonOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(
                    namingPolicy: null,
                    allowIntegerValues: false)
            }
        };
}

public enum ExtensionRuntimeKind
{
    ContentOnly,
    WebView,
    NativeDotNet
}

public enum ExtensionCapability
{
    LibraryContent,
    WorkspacePanel,
    DocumentProvider,
    Commands,
    AiTool,
    WorkspaceMetadata,
    FileRead,
    FileWrite,
    TerminalExecute,
    Network,
    Secrets
}

public enum WorkspacePanelSlot
{
    Left,
    Right,
    Bottom,
    Center,
    Floating
}

public enum BuiltInWorkspaceSurface
{
    FileExplorer,
    CommandBuilder
}

public enum DocumentContributionKind
{
    TextEditor,
    WebViewer,
    NativeViewer
}

public enum HyperlibContentOrigin
{
    HumanAuthored,
    AiAssisted,
    AiGenerated,
    Imported
}

public enum HyperlibReviewState
{
    Draft,
    Reviewed
}

public sealed class ExtensionManifest
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonRequired]
    public int SchemaVersion { get; init; } =
        ExtensionContract.CurrentManifestSchemaVersion;

    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string Publisher { get; init; } = string.Empty;

    [JsonRequired]
    public string Version { get; init; } = "0.1.0";
    public string Description { get; init; } = string.Empty;

    [JsonRequired]
    public ExtensionRuntimeKind Runtime { get; init; } =
        ExtensionRuntimeKind.ContentOnly;
    public string EntryPoint { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<ExtensionCapability> RequestedCapabilities { get; init; } = [];

    [JsonRequired]
    public ExtensionContributions Contributions { get; init; } = new();
}

public sealed class ExtensionContributions
{
    public IReadOnlyList<LibraryContribution> Libraries { get; init; } = [];
    public IReadOnlyList<WorkspacePanelContribution> Panels { get; init; } = [];
    public IReadOnlyList<DocumentFormatContribution> DocumentFormats { get; init; } = [];
    public IReadOnlyList<ExtensionCommandContribution> Commands { get; init; } = [];
    public IReadOnlyList<AiToolContribution> AiTools { get; init; } = [];
}

public sealed class LibraryContribution
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string ContentPath { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<string> LanguageIds { get; init; } = [];

    [JsonRequired]
    public HyperlibContentOrigin ContentOrigin { get; init; } =
        HyperlibContentOrigin.HumanAuthored;

    [JsonRequired]
    public HyperlibReviewState ReviewState { get; init; } =
        HyperlibReviewState.Draft;

    [JsonRequired]
    public string Provenance { get; init; } = string.Empty;
    public string ReviewedBy { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
}

public sealed class WorkspacePanelContribution
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string ViewResource { get; init; } = string.Empty;

    public int? MessageApiVersion { get; init; }

    [JsonRequired]
    public PanelPlacementPolicy Placement { get; init; } = new();
}

public sealed class PanelPlacementPolicy
{
    [JsonRequired]
    public WorkspacePanelSlot PreferredSlot { get; init; } =
        WorkspacePanelSlot.Right;

    [JsonRequired]
    public IReadOnlyList<WorkspacePanelSlot> AllowedSlots { get; init; } =
        [WorkspacePanelSlot.Right];
    public IReadOnlyList<BuiltInWorkspaceSurface> MayReplace { get; init; } = [];

    [JsonRequired]
    public double MinimumWidth { get; init; } = 220;

    [JsonRequired]
    public double PreferredWidth { get; init; } = 360;

    [JsonRequired]
    public double MaximumWidth { get; init; } = 720;

    public double MinimumHeight { get; init; } = 180;
    public double PreferredHeight { get; init; } = 300;
    public double MaximumHeight { get; init; } = 720;

    [JsonRequired]
    public bool AllowUserMove { get; init; } = true;

    [JsonRequired]
    public bool AllowUserResize { get; init; } = true;

    [JsonRequired]
    public bool AllowFloating { get; init; }
}

public sealed class DocumentFormatContribution
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public DocumentContributionKind Kind { get; init; } =
        DocumentContributionKind.TextEditor;

    [JsonRequired]
    public IReadOnlyList<string> FileExtensions { get; init; } = [];

    [JsonRequired]
    public string LanguageId { get; init; } = "plaintext";
    public string ViewResource { get; init; } = string.Empty;
}

public sealed class ExtensionCommandContribution
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string Handler { get; init; } = string.Empty;
}

public sealed class AiToolContribution
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string Description { get; init; } = string.Empty;

    [JsonRequired]
    public string Handler { get; init; } = string.Empty;
    public string InputSchemaPath { get; init; } = string.Empty;

    [JsonRequired]
    public bool RequiresUserConfirmation { get; init; } = true;
}
