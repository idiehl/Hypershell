using System.IO;
using System.Text.Json.Serialization;

namespace Hypershell.Models;

public static class VisualPageContract
{
    public const int ProjectSchemaVersion = 1;
    public const int PageSchemaVersion = 1;
    public const int LayoutSchemaVersion = 1;
    public const string GeneratorVersion = "1.0.0";
    public const int MaximumTreeDepth = 64;
    public const int MaximumElementCount = 10_000;
    public const int MaximumMutationOperations = 256;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageBreakpoint
{
    Base,
    Small,
    Medium,
    Large,
    ExtraLarge
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageBindingKind
{
    Text,
    Attribute,
    Value,
    Visibility,
    CssClass,
    Validation,
    LoopItems
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageStateKind
{
    Field,
    Parameter
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageEventKind
{
    Click,
    Change,
    Input,
    Submit,
    Focus,
    Blur,
    KeyDown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageCodeClassification
{
    Declarative,
    CustomCss,
    CustomCSharp,
    CustomJavaScript,
    CustomRazor,
    CustomComponent,
    LegacyHtml,
    PackageDependency
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HyperpageMarkupLanguage
{
    Html,
    RazorPages
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HyperpagePageFileType
{
    ProjectDefault,
    Html,
    Cshtml,
    RazorPage
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HyperpageHostingTarget
{
    Local,
    WebServer
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HyperpageSiteStructure
{
    FixedPages,
    DynamicSite
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HyperpageCodingMode
{
    Manual,
    AiAssisted
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HyperpageAiProvider
{
    None,
    OpenAi,
    Local
}

public sealed class HyperpageProjectFoundation
{
    public HyperpageMarkupLanguage MarkupLanguage { get; set; } =
        HyperpageMarkupLanguage.RazorPages;
    public HyperpageHostingTarget HostingTarget { get; set; } =
        HyperpageHostingTarget.Local;
    public HyperpageSiteStructure SiteStructure { get; set; } =
        HyperpageSiteStructure.FixedPages;
    public int PlannedPageCount { get; set; } = 1;
    public bool UsesTemplates { get; set; }
    public HyperpageCodingMode CodingMode { get; set; } =
        HyperpageCodingMode.Manual;
    public HyperpageAiProvider AiProvider { get; set; } =
        HyperpageAiProvider.None;
    public bool ClarifyingQuestionsFirst { get; set; } = true;
}

public sealed class VisualPageProjectManifest
{
    public int SchemaVersion { get; set; } = VisualPageContract.ProjectSchemaVersion;
    public string ProjectId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Hyperpage Project";
    public string RootNamespace { get; set; } = "VisualPages";
    public string GeneratorVersion { get; set; } = VisualPageContract.GeneratorVersion;
    public string Revision { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string RazorProjectPath { get; set; } = "src/Pages/Pages.csproj";
    public string LocalHostProjectPath { get; set; } = "src/Host/Host.csproj";
    public HyperpageProjectFoundation Foundation { get; set; } = new();
    public List<VisualPageProjectPage> Pages { get; set; } = [];
    public List<VisualPageAsset> Assets { get; set; } = [];
    public List<VisualPageTemplateReference> Templates { get; set; } = [];
    public VisualPageTheme Theme { get; set; } = new();
    public StudioLayoutSnapshot? LayoutOverride { get; set; }
    public string HyperlibPackageId { get; set; } = string.Empty;
    public string HyperlibAuthoringPath { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SharedStylesheetPaths { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SharedJavaScriptPaths { get; set; }
}

public sealed class VisualPageProjectPage
{
    public string PageId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Page";
    public string Route { get; set; } = "/";
    public string Slug { get; set; } = "page";
    public HyperpagePageFileType FileType { get; set; } =
        HyperpagePageFileType.ProjectDefault;
    public string DocumentPath { get; set; } = string.Empty;
    public string ComponentPath { get; set; } = string.Empty;
    public string StarterTemplateId { get; set; } = string.Empty;
    public string StarterTemplateName { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
}

public sealed class VisualPageAsset
{
    public string AssetId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/octet-stream";
    public string Sha256 { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? Path.GetFileNameWithoutExtension(RelativePath)
        : Name;
}

public sealed class VisualPageTemplateReference
{
    public string TemplateId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Template";
    public string Scope { get; set; } = "project";
    public string RelativePath { get; set; } = string.Empty;
    public List<string> AssetIds { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
}

public sealed class VisualPageTheme
{
    public Dictionary<string, string> Tokens { get; set; } = new(StringComparer.Ordinal);
    public string AdditionalCssPath { get; set; } = "Shared/theme.css";
}

public sealed class VisualPageDocument
{
    public int SchemaVersion { get; set; } = VisualPageContract.PageSchemaVersion;
    public string ProjectId { get; set; } = string.Empty;
    public string PageId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Page";
    public string ComponentName { get; set; } = "HomePage";
    public string Route { get; set; } = "/";
    public string Slug { get; set; } = "home";
    public HyperpagePageFileType FileType { get; set; } =
        HyperpagePageFileType.ProjectDefault;
    public string Title { get; set; } = "Page";
    public string Description { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string GeneratorVersion { get; set; } = VisualPageContract.GeneratorVersion;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public VisualPageElement Root { get; set; } = new()
    {
        DescriptorId = "core.page",
        Name = "Page"
    };
    public List<VisualPageStateDefinition> State { get; set; } = [];
    public List<string> StylesheetPaths { get; set; } = [];
    public List<VisualPageSourceMapping> SourceMappings { get; set; } = [];
    public List<VisualPageDiagnostic> Diagnostics { get; set; } = [];
    public List<VisualPageGeneratedFileHash> GeneratedFiles { get; set; } = [];
}

public sealed class VisualPageElement
{
    public string ElementId { get; set; } = Guid.NewGuid().ToString("N");
    public string DescriptorId { get; set; } = "core.container";
    public string Name { get; set; } = "Element";
    public string Text { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsHidden { get; set; }
    public string? CustomRazor { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Styles { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec> Layout { get; set; } = [];
    public List<VisualPageBinding> Bindings { get; set; } = [];
    public List<VisualPageEventBinding> Events { get; set; } = [];
    public List<VisualPageElement> Children { get; set; } = [];

    [JsonIgnore]
    public bool IsCustomCode => !string.IsNullOrWhiteSpace(CustomRazor)
                                || DescriptorId.StartsWith(
                                    "custom.",
                                    StringComparison.OrdinalIgnoreCase);
}

public sealed class VisualPageLayoutSpec
{
    public string Display { get; set; } = string.Empty;
    public string FlexDirection { get; set; } = string.Empty;
    public string FlexWrap { get; set; } = string.Empty;
    public string GridTemplateColumns { get; set; } = string.Empty;
    public int? ColumnSpan { get; set; }
    public int? RowSpan { get; set; }
    public string FlexBasis { get; set; } = string.Empty;
    public string Gap { get; set; } = string.Empty;
    public string Padding { get; set; } = string.Empty;
    public string Margin { get; set; } = string.Empty;
    public string MinWidth { get; set; } = string.Empty;
    public string MaxWidth { get; set; } = string.Empty;
    public string MinHeight { get; set; } = string.Empty;
    public string MaxHeight { get; set; } = string.Empty;
    public string AlignItems { get; set; } = string.Empty;
    public string JustifyContent { get; set; } = string.Empty;
    public string AlignSelf { get; set; } = string.Empty;
}

public sealed class VisualPageStateDefinition
{
    public string StateId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Value";
    public string TypeName { get; set; } = "string";
    public VisualPageStateKind Kind { get; set; }
    public string InitialValue { get; set; } = "string.Empty";
    public bool IsRequired { get; set; }
}

public sealed class VisualPageBinding
{
    public string BindingId { get; set; } = Guid.NewGuid().ToString("N");
    public VisualPageBindingKind Kind { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? Format { get; set; }
}

public sealed class VisualPageEventBinding
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public VisualPageEventKind Kind { get; set; }
    public string HandlerName { get; set; } = "HandleEvent";
    public string ArgumentType { get; set; } = string.Empty;
}

public sealed class VisualPageSourceMapping
{
    public string ElementId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed class VisualPageDiagnostic
{
    public string Code { get; set; } = string.Empty;
    public VisualPageDiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ElementId { get; set; }
    public string? RelativePath { get; set; }
    public int? Line { get; set; }

    [JsonIgnore]
    public string Summary => $"{Severity} {Code}: {Message}";
}

public sealed class VisualPageGeneratedFileHash
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPageMutationKind
{
    SetPageMetadata,
    InsertChild,
    RemoveElement,
    MoveElement,
    DuplicateElement,
    WrapElement,
    UnwrapElement,
    SetText,
    SetAttribute,
    RemoveAttribute,
    SetStyle,
    RemoveStyle,
    SetLayout,
    SetBinding,
    RemoveBinding,
    SetEvent,
    RemoveEvent,
    SetLocked,
    SetHidden,
    AddState,
    RemoveState,
    SetStylesheets
}

public sealed record VisualPageMutationSet(
    string ExpectedRevision,
    IReadOnlyList<VisualPageMutationOperation> Operations,
    string? Actor = null,
    string? Reason = null);

public sealed class VisualPageMutationOperation
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public VisualPageMutationKind Kind { get; set; }
    public string TargetElementId { get; set; } = string.Empty;
    public string ParentElementId { get; set; } = string.Empty;
    public int? Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool? Flag { get; set; }
    public VisualPageBreakpoint Breakpoint { get; set; }
    public VisualPageElement? Element { get; set; }
    public VisualPageLayoutSpec? Layout { get; set; }
    public VisualPageBinding? Binding { get; set; }
    public VisualPageEventBinding? Event { get; set; }
    public VisualPageStateDefinition? State { get; set; }
    public VisualPagePageMetadata? PageMetadata { get; set; }
    public List<string>? StylesheetPaths { get; set; }
}

public sealed class VisualPagePageMetadata
{
    public string Name { get; set; } = "Page";
    public string Route { get; set; } = "/";
    public string Slug { get; set; } = "page";
    public string Title { get; set; } = "Page";
    public string Description { get; set; } = string.Empty;
}

public sealed class VisualPageMutationResult
{
    public required VisualPageDocument Document { get; init; }
    public required string PreviousRevision { get; init; }
    public required string Revision { get; init; }
    public required IReadOnlyList<VisualPageSemanticChange> Changes { get; init; }
}

public sealed class VisualPageSemanticChange
{
    public string OperationId { get; set; } = string.Empty;
    public VisualPageMutationKind Kind { get; set; }
    public string ElementId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StudioLayoutNodeKind
{
    Pane,
    Split
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StudioSplitOrientation
{
    Horizontal,
    Vertical
}

public sealed class StudioLayoutSnapshot
{
    public int SchemaVersion { get; set; } = VisualPageContract.LayoutSchemaVersion;
    public string Name { get; set; } = "Default";
    public StudioLayoutNode Root { get; set; } = StudioLayoutNode.CreateDefault();
    public List<StudioToolPlacement> ToolPlacements { get; set; } = [];
    public bool IsLeftToolPanelOpen { get; set; } = true;
    public bool IsRightToolPanelOpen { get; set; } = true;
    public bool IsProjectOverride { get; set; }
}

public sealed class StudioLayoutNode
{
    public string NodeId { get; set; } = Guid.NewGuid().ToString("N");
    public StudioLayoutNodeKind Kind { get; set; }
    public StudioSplitOrientation Orientation { get; set; }
    public double FirstProportion { get; set; } = 0.62;
    public StudioLayoutNode? First { get; set; }
    public StudioLayoutNode? Second { get; set; }
    public List<string> PaneContents { get; set; } = [];
    public string ActiveContentId { get; set; } = string.Empty;

    public static StudioLayoutNode CreateDefault() => new()
    {
        Kind = StudioLayoutNodeKind.Pane,
        PaneContents = ["canvas"],
        ActiveContentId = "canvas"
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StudioToolHostKind
{
    HypershellLeft,
    HypershellRight,
    StudioLeft,
    StudioRight,
    CenterPane,
    Floating
}

public sealed class StudioToolPlacement
{
    public string ToolId { get; set; } = string.Empty;
    public StudioToolHostKind Host { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public StudioFloatingBounds? FloatingBounds { get; set; }
}

public sealed class StudioFloatingBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 520;
}

public sealed class AtlasArchivePageManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Slug { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "public";
    public string SourceProjectId { get; set; } = string.Empty;
    public string SourcePageId { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public List<VisualPageCodeClassification> Classifications { get; set; } = [];
    public List<VisualPageGeneratedFileHash> Artifacts { get; set; } = [];
}

public sealed class AtlasArchiveReleaseManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string ReleaseId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceCommit { get; set; } = string.Empty;
    public string PreviousReleaseId { get; set; } = string.Empty;
    public string CandidateLoopbackUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AtlasArchivePageManifest> Pages { get; set; } = [];
}

public sealed class AtlasArchivePublishReceipt
{
    public string ReceiptId { get; set; } = Guid.NewGuid().ToString("N");
    public string Slug { get; set; } = string.Empty;
    public string SourceProjectId { get; set; } = string.Empty;
    public string SourcePageId { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public string SourceCommit { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string ReviewHash { get; set; } = string.Empty;
    public string ReleaseId { get; set; } = string.Empty;
    public string PreviousReleaseId { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public List<VisualPageGeneratedFileHash> Artifacts { get; set; } = [];
}
