using Hypershell.Extensibility;
using System.Text.Json.Serialization;

namespace Hypershell.Models;

public enum ExecutionTarget
{
    PowerShell,
    CSharp,
    External
}

public enum CommandCompositionMode
{
    Auto,
    Pipeline,
    Statement
}

public enum CommandLibraryOrigin
{
    Bundled,
    External,
    ContentPackage,
    Runtime
}

public enum CommandLibraryKind
{
    Content,
    // Language remains valid for schema-v1 packages. New official catalogs use
    // Core so the library's provenance is explicit instead of implying that a
    // mixed collection represents the whole language.
    Language,
    Core,
    StandardLibrary,
    Specification,
    PlatformApi,
    Runtime,
    Framework,
    Sdk,
    CommandLineTool,
    Module,
    Package,
    Extension
}

public enum CommandCoverageTier
{
    Indexed,
    Guided,
    Verified
}

public enum CommandSymbolKind
{
    Syntax,
    Keyword,
    Operator,
    Literal,
    Statement,
    BuiltIn,
    Command,
    Function,
    Class,
    Struct,
    Interface,
    Enum,
    Constructor,
    Method,
    Property,
    Event,
    Field,
    Constant,
    Macro,
    Element,
    Attribute,
    Selector,
    AtRule,
    Api,
    Overload,
    Other
}

public sealed class CommandBrowsePathSegment
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore]
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName)
        ? Name
        : DisplayName;
}

public sealed class CommandLibraryEntryShard
{
    public int SchemaVersion { get; set; } = 2;
    public string LibraryId { get; set; } = string.Empty;
    public List<CommandDefinition> Entries { get; set; } = [];
}

public sealed class CommandGuideShard
{
    public int SchemaVersion { get; set; } = 2;
    public string LibraryId { get; set; } = string.Empty;
    public List<CommandGuideShardEntry> Guides { get; set; } = [];
}

public sealed class CommandGuideShardEntry
{
    public string EntryId { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public List<CommandParameterDefinition> Parameters { get; set; } = [];
    public List<CommandUsageExample> Examples { get; set; } = [];
}

public sealed class CommandParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TypeName { get; set; } = "String";
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsSwitch { get; set; }
    public bool IncludeByDefault { get; set; }
}

public sealed class CommandUsageExample
{
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string ImageAltText { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);
}

public sealed class CommandDefinition
{
    public string Id { get; set; } = string.Empty;
    public string CatalogId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("definition")]
    public string GuideDefinition { get; set; } = string.Empty;

    public string ModuleName { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public TerminalShellKind PreferredShellKind { get; set; } = TerminalShellKind.PowerShell;
    // Optional facets let JSON libraries describe a command without relying on
    // a particular naming convention. PowerShell commands are inferred from
    // their Verb-Noun name when these are not supplied.
    public string Action { get; set; } = string.Empty;
    public string ActionDescription { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectDescription { get; set; } = string.Empty;

    // C# APIs need structural coordinates rather than PowerShell-style
    // Action/Object facets. These fields are optional so existing and external
    // schema-v1 libraries remain compatible; the catalog derives sensible
    // values from ModuleName and Name when an older entry omits them.
    public string NamespaceName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string MemberKind { get; set; } = string.Empty;
    public string OverloadSignature { get; set; } = string.Empty;
    public string DocumentationUrl { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public CommandSymbolKind SymbolKind { get; set; } = CommandSymbolKind.Other;
    public CommandCoverageTier CoverageTier { get; set; } =
        CommandCoverageTier.Guided;
    public List<CommandBrowsePathSegment> BrowsePath { get; set; } = [];
    public List<string> LegacyIds { get; set; } = [];
    public string GuideFile { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceLabelOverride { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceCatalogPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceCatalogId { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceEntryId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsGuideLoaded { get; set; }

    [JsonIgnore]
    public bool IsExecutionSupported { get; set; } = true;

    [JsonIgnore]
    public string ExecutionSupportMessage { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasDeferredGuide => !string.IsNullOrWhiteSpace(GuideFile)
                                    && !IsGuideLoaded;

    public string Template { get; set; } = string.Empty;
    public ExecutionTarget ExecutionTarget { get; set; } = ExecutionTarget.PowerShell;
    public CommandCompositionMode CompositionMode { get; set; } =
        CommandCompositionMode.Auto;
    public List<CommandParameterDefinition> Parameters { get; set; } = [];
    public List<CommandUsageExample> Examples { get; set; } = [];

    [JsonIgnore]
    public string SearchText => string.Join(
        ' ',
        new[]
        {
            DisplayName,
            Name,
            Description,
            GuideDefinition,
            ModuleName,
            Action,
            ObjectName,
            NamespaceName,
            TypeName,
            MemberName,
            MemberKind,
            OverloadSignature,
            SymbolKind.ToString(),
            CoverageTier.ToString(),
            string.Join(' ', BrowsePath.Select(segment =>
                $"{segment.Kind} {segment.Name} {segment.DisplayName}")),
            string.Join(' ', Keywords),
            string.Join(' ', Parameters.Select(parameter =>
                $"{parameter.Name} {parameter.DisplayName} {parameter.TypeName} {parameter.Description}"))
        });
}

public sealed class CommandLibrary
{
    public int SchemaVersion { get; set; } = 1;
    public string ContentVersion { get; set; } = "1.0.0";
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public CommandLibraryKind Kind { get; set; } = CommandLibraryKind.Content;
    public string CanonicalName { get; set; } = string.Empty;
    public string Ecosystem { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string ParentLibraryId { get; set; } = string.Empty;
    public string FrameworkName { get; set; } = string.Empty;
    public string FrameworkVersion { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public bool IsExecutionSupported { get; set; } = true;
    public string ExecutionSupportMessage { get; set; } = string.Empty;
    public int ReferenceAssemblyCount { get; set; }
    public bool IsStarterLibrary { get; set; }
    public bool CreateStandaloneProfile { get; set; } = true;
    public List<string> Keywords { get; set; } = [];
    public ExecutionTarget ExecutionTarget { get; set; } = ExecutionTarget.PowerShell;
    public Dictionary<string, string> ActionDescriptions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ObjectDescriptions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int DeclaredEntryCount { get; set; }
    public int DeclaredIndexedCount { get; set; }
    public int DeclaredGuidedCount { get; set; }
    public int DeclaredVerifiedCount { get; set; }
    public List<string> EntryFiles { get; set; } = [];
    public int DeclaredGuideCount { get; set; }
    public List<string> GuideFiles { get; set; } = [];
    public List<string> LegacyIds { get; set; } = [];
    public List<CommandDefinition> Commands { get; set; } = [];

    [JsonIgnore]
    public bool HasDeferredEntries => EntryFiles.Count > 0 && Commands.Count == 0;

    [JsonIgnore]
    public int EffectiveEntryCount => Commands.Count > 0
        ? Commands.Count
        : DeclaredEntryCount;

    [JsonIgnore]
    public int EffectiveGuideCount => DeclaredGuideCount > 0
        ? DeclaredGuideCount
        : Commands.Count(command => command.CoverageTier is
            CommandCoverageTier.Guided or CommandCoverageTier.Verified);

    [JsonIgnore]
    public string CoverageSummary
    {
        get
        {
            var indexed = DeclaredIndexedCount;
            var guided = DeclaredGuidedCount;
            var verified = DeclaredVerifiedCount;
            if (indexed + guided + verified == 0 && Commands.Count > 0)
            {
                indexed = Commands.Count(command =>
                    command.CoverageTier == CommandCoverageTier.Indexed);
                guided = Commands.Count(command =>
                    command.CoverageTier == CommandCoverageTier.Guided);
                verified = Commands.Count(command =>
                    command.CoverageTier == CommandCoverageTier.Verified);
            }
            else if (indexed + guided + verified == 0)
            {
                guided = DeclaredGuideCount;
                indexed = Math.Max(0, DeclaredEntryCount - guided);
            }

            return $"{indexed:N0} indexed · {guided:N0} guided · {verified:N0} verified";
        }
    }

    [JsonIgnore]
    public string ActiveRuntimeVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public bool UsesInstalledCompatibleIndex { get; set; }

    [JsonIgnore]
    public string ActiveVersionLabel
    {
        get
        {
            var target = string.IsNullOrWhiteSpace(TargetVersion)
                ? "Unversioned catalog"
                : TargetVersion;
            if (string.IsNullOrWhiteSpace(ActiveRuntimeVersion))
            {
                return $"{target} · bundled baseline index";
            }

            return UsesInstalledCompatibleIndex
                ? $"{target} index · installed {ActiveRuntimeVersion}"
                : $"{target} bundled index · installed {ActiveRuntimeVersion}";
        }
    }

    [JsonIgnore]
    public CommandLibraryOrigin Origin { get; set; } = CommandLibraryOrigin.External;

    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceExtensionId { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceCatalogId { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourceExtensionVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourcePublisher { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourcePackageSha256 { get; set; } = string.Empty;

    [JsonIgnore]
    public string ContentProvenance { get; set; } = string.Empty;

    [JsonIgnore]
    public HyperlibContentOrigin ContentOrigin { get; set; } =
        HyperlibContentOrigin.HumanAuthored;

    [JsonIgnore]
    public HyperlibReviewState ContentReviewState { get; set; } =
        HyperlibReviewState.Draft;

    [JsonIgnore]
    public string ContentReviewedBy { get; set; } = string.Empty;

    // Runtime-discovered managed surfaces may expose only part of a directory
    // while still needing the rest of that framework or package to resolve
    // metadata dependencies. These paths are deliberately not persisted in
    // external command-library JSON.
    [JsonIgnore]
    public List<string> ApiAssemblyPaths { get; set; } = [];

    [JsonIgnore]
    public List<string> ResolverAssemblyPaths { get; set; } = [];

    [JsonIgnore]
    public string MetadataCoreAssemblyName { get; set; } = string.Empty;

    [JsonIgnore]
    public int CatalogPriority { get; set; } = int.MaxValue;
}
