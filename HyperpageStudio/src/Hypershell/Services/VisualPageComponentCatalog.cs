using System.IO;
using System.Text.RegularExpressions;
using Hypershell.Models;

namespace Hypershell.Services;

public enum VisualPageAttributeEditorKind
{
    Text,
    Boolean,
    Number,
    Url,
    Choice,
    TokenList,
    DateTime,
    Color,
    IdReference
}

public enum VisualPageCanvasRepresentation
{
    Native,
    Placeholder
}

public sealed class VisualPageAttributeDescriptor
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public VisualPageAttributeEditorKind EditorKind { get; init; }
    public IReadOnlyList<string> SuggestedValues { get; init; } = [];
    public bool RestrictToSuggestedValues { get; init; }
    public bool IsGlobal { get; init; }
}

public sealed class VisualPageComponentDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string MarkupName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ContentModel { get; init; } = "Flow content";
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public bool IsVoid { get; init; }
    public bool AllowsChildren { get; init; } = true;
    public bool IsStructural { get; init; }
    public bool RequiresAltText { get; init; }
    public bool RequiresAccessibleLabel { get; init; }
    public bool IsStandardHtmlElement { get; init; }
    public bool IsObsolete { get; init; }
    public bool IsDocumentOnly { get; init; }
    public int HtmlImportPriority { get; init; }
    public VisualPageCanvasRepresentation CanvasRepresentation { get; init; } =
        VisualPageCanvasRepresentation.Native;
    public IReadOnlyList<string> AllowedAttributes { get; init; } = [];
    public IReadOnlyList<VisualPageAttributeDescriptor> AttributeDefinitions { get; init; } = [];
    public IReadOnlyDictionary<string, string> DefaultAttributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> DefaultStyles { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public VisualPageLayoutSpec? DefaultLayout { get; init; }
    public VisualPageCodeClassification Classification { get; init; } =
        VisualPageCodeClassification.Declarative;
    public string DefaultText { get; init; } = string.Empty;
    public VisualPageElementAuthoringDescriptor Authoring { get; init; } =
        null!;

    public string SearchText => string.Join(
        ' ',
        new[]
            {
                DisplayName, Category, MarkupName, Description,
                Authoring.Badge, Authoring.PlacementGuidance,
                Authoring.ChildrenGuidance
            }
            .Concat(Keywords)
            .Concat(AllowedAttributes));
}

public sealed class VisualPageComponentCatalog
{
    private readonly IReadOnlyDictionary<string, VisualPageComponentDescriptor>
        _byId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<VisualPageComponentDescriptor>>
        _byHtmlTag;

    public VisualPageComponentCatalog()
    {
        Descriptors = CreateDescriptors();
        _byId = Descriptors.ToDictionary(
            descriptor => descriptor.Id,
            StringComparer.OrdinalIgnoreCase);
        _byHtmlTag = Descriptors
            .Where(descriptor =>
                descriptor.IsStandardHtmlElement || descriptor.IsObsolete)
            .GroupBy(descriptor => descriptor.MarkupName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VisualPageComponentDescriptor>)group
                    .OrderByDescending(descriptor => descriptor.HtmlImportPriority)
                    .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        CanonicalStandardHtmlElements = _byHtmlTag
            .Where(group => group.Value.Any(descriptor =>
                descriptor.IsStandardHtmlElement))
            .Select(group => group.Value.First(descriptor =>
                descriptor.IsStandardHtmlElement))
            .OrderBy(descriptor => descriptor.MarkupName, StringComparer.Ordinal)
            .ToList();
        StandardHtmlTags = Descriptors
            .Where(descriptor => descriptor.IsStandardHtmlElement)
            .Select(descriptor => descriptor.MarkupName)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);
        ObsoleteHtmlTags = Descriptors
            .Where(descriptor => descriptor.IsObsolete)
            .Select(descriptor => descriptor.MarkupName)
            .ToHashSet(
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<VisualPageComponentDescriptor> Descriptors { get; }
    public IReadOnlyList<VisualPageComponentDescriptor>
        CanonicalStandardHtmlElements
    { get; }
    public IReadOnlySet<string> StandardHtmlTags { get; }
    public IReadOnlySet<string> ObsoleteHtmlTags { get; }

    public VisualPageComponentDescriptor GetRequired(string descriptorId) =>
        _byId.TryGetValue(descriptorId, out var descriptor)
            ? descriptor
            : throw new VisualPageValidationException(
                $"Unknown element descriptor '{descriptorId}'.");

    public bool TryGet(
        string descriptorId,
        out VisualPageComponentDescriptor descriptor) =>
        _byId.TryGetValue(descriptorId, out descriptor!);

    public bool TryGetHtmlElement(
        string tagName,
        IReadOnlyDictionary<string, string>? attributes,
        out VisualPageComponentDescriptor descriptor)
    {
        if (!_byHtmlTag.TryGetValue(tagName, out var candidates))
        {
            descriptor = null!;
            return false;
        }

        if (tagName.Equals("input", StringComparison.OrdinalIgnoreCase)
            && attributes?.GetValueOrDefault("type") is { } inputType
            && _byId.TryGetValue(
                $"html.input.{inputType.Trim().ToLowerInvariant()}",
                out descriptor!))
        {
            return true;
        }

        descriptor = candidates[0];
        return true;
    }

    public IReadOnlyList<VisualPageAttributeDescriptor> GetAttributeDefinitions(
        string descriptorId,
        bool includeGlobals = true)
    {
        var descriptor = GetRequired(descriptorId);
        var definitions = includeGlobals
            ? descriptor.AttributeDefinitions.Concat(
                VisualPageAttributeCatalog.GlobalDefinitions)
            : descriptor.AttributeDefinitions;
        var quickAttributes = descriptor.Authoring.QuickAttributeNames
            .Select((name, index) => (name, index))
            .ToDictionary(
                value => value.name,
                value => value.index,
                StringComparer.OrdinalIgnoreCase);
        return definitions
            .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(value => quickAttributes.ContainsKey(value.Name) ? 0 : 1)
            .ThenBy(value => quickAttributes.GetValueOrDefault(
                value.Name,
                int.MaxValue))
            .ThenBy(value => value.IsGlobal)
            .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    public bool TryGetAttributeDefinition(
        VisualPageComponentDescriptor descriptor,
        string attributeName,
        out VisualPageAttributeDescriptor definition)
    {
        definition = descriptor.AttributeDefinitions.FirstOrDefault(value =>
            value.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            ?? (VisualPageAttributeCatalog.IsGlobalName(attributeName)
                ? VisualPageAttributeCatalog.Resolve(attributeName, isGlobal: true)
                : null!);
        return definition is not null;
    }

    public VisualPageElement CreateElement(string descriptorId) =>
        CreateElementCore(descriptorId, includeStarters: false, depth: 0);

    public VisualPageElement CreateElementForAuthoring(string descriptorId) =>
        CreateElementCore(descriptorId, includeStarters: true, depth: 0);

    private VisualPageElement CreateElementCore(
        string descriptorId,
        bool includeStarters,
        int depth)
    {
        if (depth > 12)
        {
            throw new VisualPageValidationException(
                "An element starter structure is recursively too deep.");
        }
        var descriptor = GetRequired(descriptorId);
        var element = new VisualPageElement
        {
            DescriptorId = descriptor.Id,
            Name = descriptor.DisplayName,
            Text = descriptor.DefaultText,
            Attributes = new Dictionary<string, string>(
                descriptor.DefaultAttributes,
                StringComparer.Ordinal),
            Styles = new Dictionary<string, string>(
                descriptor.DefaultStyles,
                StringComparer.Ordinal),
            Layout = descriptor.DefaultLayout is null
                ? []
                : new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
                {
                    [VisualPageBreakpoint.Base] = descriptor.DefaultLayout
                }
        };
        if (includeStarters)
        {
            foreach (var starter in descriptor.Authoring.StarterChildren)
            {
                element.Children.Add(CreateStarterElement(starter, depth + 1));
            }
        }
        return element;
    }

    private VisualPageElement CreateStarterElement(
        VisualPageElementStarter starter,
        int depth)
    {
        var element = CreateElementCore(
            starter.DescriptorId,
            includeStarters: starter.Children.Count == 0,
            depth);
        if (starter.Text is not null)
        {
            element.Text = starter.Text;
        }
        foreach (var attribute in starter.Attributes)
        {
            element.Attributes[attribute.Key] = attribute.Value;
        }
        if (starter.Children.Count > 0)
        {
            element.Children.Clear();
            foreach (var child in starter.Children)
            {
                element.Children.Add(CreateStarterElement(child, depth + 1));
            }
        }
        return element;
    }

    private static IReadOnlyList<VisualPageComponentDescriptor>
        CreateDescriptors()
    {
        var descriptors = new List<VisualPageComponentDescriptor>
        {
        Descriptor("core.page", "Page", "Layout", "main", structural: true,
            description: "Root responsive content surface for a generated component.",
            layout: Layout(display: "flex", direction: "column", gap: "1.5rem")),
        Descriptor("core.section", "Section", "Layout", "section", structural: true,
            attributes: ["aria-label", "id"],
            description: "Thematic section of a page.", standardHtml: true,
            layout: Layout(display: "flex", direction: "column", gap: "1rem")),
        Descriptor("core.container", "Container", "Layout", "div", structural: true,
            attributes: ["role", "aria-label"],
            description: "Generic flow-content container.", standardHtml: true,
            layout: Layout(display: "flex", direction: "column", gap: "1rem")),
        Descriptor("core.flex", "Flex layout", "Layout", "div", structural: true,
            description: "Responsive flexbox container for row or column layouts.",
            layout: Layout(display: "flex", direction: "row", gap: "1rem")),
        Descriptor("core.grid", "Grid layout", "Layout", "div", structural: true,
            description: "Responsive twelve-column grid container.",
            layout: Layout(display: "grid", columns: "repeat(12, minmax(0, 1fr))", gap: "1rem")),
        Descriptor("core.spacer", "Spacer", "Layout", "div",
            description: "Layout-only breathing room controlled with responsive constraints.",
            styles: new Dictionary<string, string> { ["min-height"] = "1rem" }),

        Descriptor("core.heading1", "Heading 1", "Typography", "h1",
            description: "Top-level heading.", standardHtml: true, defaultText: "Heading"),
        Descriptor("core.heading2", "Heading 2", "Typography", "h2",
            description: "Second-level heading.", standardHtml: true, defaultText: "Heading"),
        Descriptor("core.heading3", "Heading 3", "Typography", "h3",
            description: "Third-level heading.", standardHtml: true, defaultText: "Heading"),
        Descriptor("core.heading4", "Heading 4", "Typography", "h4",
            description: "Fourth-level heading.", standardHtml: true, defaultText: "Heading"),
        Descriptor("core.heading5", "Heading 5", "Typography", "h5",
            description: "Fifth-level heading.", standardHtml: true, defaultText: "Heading"),
        Descriptor("core.heading6", "Heading 6", "Typography", "h6",
            description: "Sixth-level heading.", standardHtml: true, defaultText: "Heading"),
        Descriptor("core.paragraph", "Paragraph", "Typography", "p",
            description: "Paragraph of phrasing content.", standardHtml: true, defaultText: "Paragraph text"),
        Descriptor("core.quote", "Block quote", "Typography", "blockquote",
            attributes: ["cite"], description: "Section quoted from another source.",
            standardHtml: true, defaultText: "Quoted text"),
        Descriptor("core.code", "Preformatted block", "Typography", "pre",
            description: "Block of preformatted text.", standardHtml: true, defaultText: "Preformatted text"),
        Descriptor("core.inline", "Inline text", "Typography", "span",
            description: "Generic phrasing-content container.", standardHtml: true, defaultText: "Inline text"),

        Descriptor("core.link", "Link", "Navigation", "a",
            attributes: ["href", "target", "download", "ping", "rel", "hreflang", "type", "referrerpolicy", "aria-label"],
            defaults: new Dictionary<string, string> { ["href"] = "#" },
            description: "Hyperlink to another location or resource.", standardHtml: true,
            defaultText: "Link"),
        Descriptor("core.nav", "Navigation", "Navigation", "nav", structural: true,
            attributes: ["aria-label"], accessibleLabel: true,
            description: "Section containing navigation links.", standardHtml: true),
        Descriptor("core.button", "Button", "Forms", "button",
            attributes: ["command", "commandfor", "disabled", "form", "formaction", "formenctype", "formmethod", "formnovalidate", "formtarget", "name", "popovertarget", "popovertargetaction", "type", "value", "aria-label"],
            defaults: new Dictionary<string, string> { ["type"] = "button" },
            description: "Interactive button control.", standardHtml: true,
            defaultText: "Button"),

        Descriptor("core.image", "Image", "Media", "img", isVoid: true,
            attributes: ["src", "alt", "srcset", "sizes", "crossorigin", "usemap", "ismap", "controls", "width", "height", "referrerpolicy", "decoding", "loading", "fetchpriority"],
            defaults: new Dictionary<string, string>
            {
                ["src"] = "assets/placeholder.svg",
                ["alt"] = string.Empty,
                ["loading"] = "lazy"
            },
            requiresAlt: true, description: "Responsive image.", standardHtml: true),
        Descriptor("core.picture", "Picture", "Media", "picture", structural: true,
            description: "Responsive image source container.", standardHtml: true),
        Descriptor("core.audio", "Audio", "Media", "audio", structural: true,
            attributes: ["src", "crossorigin", "preload", "autoplay", "loading", "loop", "muted", "controls"],
            defaults: new Dictionary<string, string>
            {
                ["controls"] = "true",
                ["preload"] = "metadata"
            },
            description: "Audio player.", standardHtml: true),
        Descriptor("core.video", "Video", "Media", "video", structural: true,
            attributes: ["src", "crossorigin", "poster", "preload", "autoplay", "playsinline", "loading", "loop", "muted", "controls", "width", "height"],
            defaults: new Dictionary<string, string>
            {
                ["controls"] = "true",
                ["preload"] = "metadata"
            },
            description: "Video player.", standardHtml: true),
        Descriptor("core.figure", "Figure", "Media", "figure", structural: true,
            description: "Self-contained figure with optional caption.", standardHtml: true),
        Descriptor("core.caption", "Figure caption", "Media", "figcaption",
            description: "Caption for a figure.", standardHtml: true, defaultText: "Figure caption"),

        Descriptor("core.list", "Unordered list", "Lists", "ul", structural: true,
            description: "Unordered list of items.", standardHtml: true),
        Descriptor("core.ordered-list", "Ordered list", "Lists", "ol", structural: true,
            attributes: ["reversed", "start", "type"], description: "Ordered list of items.", standardHtml: true),
        Descriptor("core.list-item", "List item", "Lists", "li", structural: true,
            attributes: ["value"], description: "Item in an ordered, unordered, or menu list.", standardHtml: true,
            defaultText: "List item"),

        Descriptor("core.form", "Form", "Forms", "EditForm", structural: true,
            attributes: ["Model", "OnValidSubmit"], accessibleLabel: true,
            description: "Typed Blazor EditForm with validation and named submit handling."),
        Descriptor("core.field", "Form field", "Forms", "div", structural: true,
            description: "Layout group for a label, input, help text, and validation message."),
        Descriptor("core.label", "Label", "Forms", "label", attributes: ["for"],
            description: "Caption for a form control.", standardHtml: true, defaultText: "Label"),
        Descriptor("core.input-text", "Text input", "Forms", "InputText", isVoid: true,
            attributes: ["id", "placeholder", "aria-label", "disabled"],
            accessibleLabel: true,
            description: "Typed Blazor text input."),
        Descriptor("core.input-number", "Number input", "Forms", "InputNumber", isVoid: true,
            attributes: ["id", "placeholder", "aria-label", "disabled"],
            accessibleLabel: true,
            description: "Typed Blazor numeric input."),
        Descriptor("core.input-checkbox", "Checkbox", "Forms", "InputCheckbox", isVoid: true,
            attributes: ["id", "aria-label", "disabled"], accessibleLabel: true,
            description: "Typed Blazor Boolean checkbox."),
        Descriptor("core.select", "Select", "Forms", "InputSelect", structural: true,
            attributes: ["id", "aria-label", "disabled"], accessibleLabel: true,
            description: "Typed Blazor selection control."),
        Descriptor("core.option", "Option", "Forms", "option",
            attributes: ["disabled", "label", "selected", "value"],
            description: "Option in a list box or combo box.", standardHtml: true,
            defaultText: "Option"),
        Descriptor("core.validation-message", "Validation message", "Validation", "ValidationMessage", isVoid: true,
            attributes: ["For"], description: "Validation message for a bound field."),
        Descriptor("core.validation-summary", "Validation summary", "Validation", "ValidationSummary", isVoid: true,
            description: "Summary of validation errors in the active EditForm."),

        Descriptor("core.condition", "Conditional region", "Logic", "vp:condition", structural: true,
            attributes: ["condition"], description: "Shows its subtree when a bounded C# condition is true."),
        Descriptor("core.loop", "Loop region", "Logic", "vp:loop", structural: true,
            attributes: ["items", "item"], description: "Repeats its subtree for a bounded C# collection expression."),
        Descriptor("custom.razor", "Custom Razor fragment", "Custom code", "vp:custom", structural: false,
            description: "Opaque reviewed Razor source that executes only in compiled preview.",
            classification: VisualPageCodeClassification.CustomRazor),
        Descriptor("custom.component", "Custom component", "Custom code", "DynamicComponent", structural: true,
            attributes: ["Type", "Parameters"],
            description: "Reviewed dynamic Blazor component reference and parameter map.",
            classification: VisualPageCodeClassification.CustomComponent),

        Descriptor("atlas.hero", "Atlas hero", "Atlas patterns", "section", structural: true,
            defaults: new Dictionary<string, string> { ["class"] = "atlas-hero" },
            description: "Atlas-branded responsive hero region.",
            layout: Layout(display: "grid", columns: "minmax(0, 7fr) minmax(16rem, 5fr)", gap: "clamp(1.5rem, 4vw, 4rem)")),
        Descriptor("atlas.card-grid", "Atlas card grid", "Atlas patterns", "section", structural: true,
            defaults: new Dictionary<string, string> { ["class"] = "atlas-card-grid" },
            description: "Auto-fitting Atlas card collection.",
            layout: Layout(display: "grid", columns: "repeat(auto-fit, minmax(16rem, 1fr))", gap: "1.25rem")),
        Descriptor("atlas.card", "Atlas card", "Atlas patterns", "article", structural: true,
            defaults: new Dictionary<string, string> { ["class"] = "atlas-card" },
            description: "Atlas-branded content card with a flexible vertical rhythm.",
            layout: Layout(display: "flex", direction: "column", gap: "0.75rem")),
        Descriptor("atlas.cta", "Atlas call to action", "Atlas patterns", "section", structural: true,
            defaults: new Dictionary<string, string> { ["class"] = "atlas-cta" },
            description: "Atlas call-to-action region for copy and primary actions.",
            layout: Layout(display: "flex", direction: "row", gap: "1rem")),
        Descriptor("atlas.editorial", "Atlas editorial section", "Atlas patterns", "article", structural: true,
            defaults: new Dictionary<string, string> { ["class"] = "atlas-editorial" },
            description: "Atlas long-form editorial content region.",
            layout: Layout(display: "flex", direction: "column", gap: "1.25rem"))
        };
        descriptors.AddRange(VisualPageHtmlComponentCatalog.CreateDescriptors());
        return descriptors;
    }

    private static VisualPageComponentDescriptor Descriptor(
        string id,
        string name,
        string category,
        string markup,
        bool structural = false,
        bool isVoid = false,
        bool requiresAlt = false,
        bool accessibleLabel = false,
        IReadOnlyList<string>? attributes = null,
        IReadOnlyDictionary<string, string>? defaults = null,
        IReadOnlyDictionary<string, string>? styles = null,
        VisualPageLayoutSpec? layout = null,
        string description = "",
        string contentModel = "Flow content",
        IReadOnlyList<string>? keywords = null,
        bool allowsChildren = true,
        bool standardHtml = false,
        bool obsolete = false,
        bool documentOnly = false,
        int htmlImportPriority = 0,
        VisualPageCanvasRepresentation canvasRepresentation =
            VisualPageCanvasRepresentation.Native,
        string defaultText = "",
        VisualPageCodeClassification classification =
            VisualPageCodeClassification.Declarative)
    {
        var allowedAttributes = attributes ?? [];
        var effectiveCanvas =
            VisualPageElementAuthoringCatalog.ResolveCanvasRepresentation(
                id,
                markup,
                canvasRepresentation);
        return new VisualPageComponentDescriptor
        {
            Id = id,
            DisplayName = name,
            Category = category,
            MarkupName = markup,
            Description = description,
            ContentModel = contentModel,
            Keywords = keywords ?? [],
            IsStructural = structural,
            IsVoid = isVoid,
            AllowsChildren = allowsChildren && !isVoid,
            RequiresAltText = requiresAlt,
            RequiresAccessibleLabel = accessibleLabel,
            IsStandardHtmlElement = standardHtml,
            IsObsolete = obsolete,
            IsDocumentOnly = documentOnly,
            HtmlImportPriority = htmlImportPriority,
            CanvasRepresentation = effectiveCanvas,
            AllowedAttributes = allowedAttributes,
            AttributeDefinitions = VisualPageAttributeCatalog.ResolveMany(
                allowedAttributes,
                markup,
                id),
            DefaultAttributes = defaults ??
                new Dictionary<string, string>(StringComparer.Ordinal),
            DefaultStyles = styles ??
                new Dictionary<string, string>(StringComparer.Ordinal),
            DefaultLayout = layout,
            Classification = classification,
            DefaultText = defaultText,
            Authoring = VisualPageElementAuthoringCatalog.Create(
                id,
                markup,
                category,
                structural,
                isVoid,
                allowsChildren && !isVoid,
                standardHtml,
                obsolete,
                documentOnly,
                defaultText,
                allowedAttributes,
                effectiveCanvas,
                classification)
        };
    }

    private static VisualPageLayoutSpec Layout(
        string display,
        string direction = "",
        string columns = "",
        string gap = "") =>
        new()
        {
            Display = display,
            FlexDirection = direction,
            GridTemplateColumns = columns,
            Gap = gap
        };
}

public sealed class VisualPageSchemaValidator
{
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex TypeNamePattern = new(
        "^(?:global::)?[A-Za-z_][A-Za-z0-9_.]*(?:\\[\\])?\\??$",
        RegexOptions.CultureInvariant);
    private static readonly Regex SlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex AttributeNamePattern = new(
        "^[A-Za-z_:][A-Za-z0-9_.:-]*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex CssPropertyPattern = new(
        "^(?:--[a-z0-9-]+|[a-z][a-z0-9-]*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ThemeTokenPattern = new(
        "^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        "^[a-fA-F0-9]{64}$",
        RegexOptions.CultureInvariant);
    private readonly VisualPageComponentCatalog _catalog;

    public VisualPageSchemaValidator(VisualPageComponentCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<VisualPageDiagnostic> ValidateProject(
        VisualPageProjectManifest project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new List<VisualPageDiagnostic>();
        if (project.SchemaVersion != VisualPageContract.ProjectSchemaVersion)
        {
            Error(diagnostics, "VP001", "Unsupported Hyperpage project schema version.");
        }

        if (!IsStableId(project.ProjectId))
        {
            Error(diagnostics, "VP002", "The project ID must be a stable 32-character GUID identity.");
        }

        if (!IsNamespace(project.RootNamespace))
        {
            Error(diagnostics, "VP003", "The root namespace is not a valid C# namespace.");
        }

        if (!string.Equals(
                project.GeneratorVersion,
                VisualPageContract.GeneratorVersion,
                StringComparison.Ordinal))
        {
            Error(diagnostics, "VP010", "The project generator version is unsupported.");
        }

        if (!string.IsNullOrWhiteSpace(project.Revision)
            && !Sha256Pattern.IsMatch(project.Revision))
        {
            Error(diagnostics, "VP011", "The project revision must be a SHA-256 identity.");
        }

        if (project.Foundation is null)
        {
            Error(diagnostics, "VP022", "The Hyperpage project foundation is missing.");
        }
        else
        {
            var foundation = project.Foundation;
            if (!Enum.IsDefined(foundation.MarkupLanguage)
                || !Enum.IsDefined(foundation.HostingTarget)
                || !Enum.IsDefined(foundation.SiteStructure)
                || !Enum.IsDefined(foundation.CodingMode)
                || !Enum.IsDefined(foundation.AiProvider))
            {
                Error(diagnostics, "VP023", "The Hyperpage project foundation contains an unsupported choice.");
            }
            if (foundation.SiteStructure == HyperpageSiteStructure.FixedPages
                && foundation.PlannedPageCount is < 1 or > 20)
            {
                Error(diagnostics, "VP024", "The planned fixed page count must be between 1 and 20.");
            }
            if (foundation.SiteStructure == HyperpageSiteStructure.DynamicSite
                && foundation.PlannedPageCount != 0)
            {
                Error(diagnostics, "VP025", "Dynamic sites must use a dynamic page-count marker of zero.");
            }
            if (foundation.CodingMode == HyperpageCodingMode.AiAssisted
                && foundation.AiProvider == HyperpageAiProvider.None)
            {
                Error(diagnostics, "VP026", "AI-assisted projects must name an initial AI provider.");
            }
            if (foundation.CodingMode == HyperpageCodingMode.Manual
                && foundation.AiProvider != HyperpageAiProvider.None)
            {
                Error(diagnostics, "VP027", "Manual projects cannot retain an automatic AI provider selection.");
            }
        }

        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in project.Pages)
        {
            if (!IsStableId(page.PageId) || !pageIds.Add(page.PageId))
            {
                Error(diagnostics, "VP004", $"Page ID '{page.PageId}' is invalid or duplicated.");
            }

            if (!Enum.IsDefined(page.FileType))
            {
                Error(diagnostics, "VP029", $"Page '{page.Name}' has an unsupported file type.");
            }

            if (!IsSafeRoute(page.Route))
            {
                Error(diagnostics, "VP012", $"Page route '{page.Route}' is not a safe Hyperpage route.");
            }
            else if (!routes.Add(page.Route))
            {
                Error(diagnostics, "VP005", $"Duplicate page route '{page.Route}'.");
            }

            if (!SlugPattern.IsMatch(page.Slug))
            {
                Error(diagnostics, "VP013", $"Page slug '{page.Slug}' is invalid.");
            }

            if (!string.IsNullOrWhiteSpace(page.Revision)
                && !Sha256Pattern.IsMatch(page.Revision))
            {
                Error(diagnostics, "VP014", $"Page '{page.Name}' has an invalid revision identity.");
            }

            ValidateRelativePath(page.DocumentPath, diagnostics, "VP006");
            ValidateRelativePath(page.ComponentPath, diagnostics, "VP007");
            if ((page.StarterTemplateId?.Length ?? 0) > 200
                || (page.StarterTemplateName?.Length ?? 0) > 300)
            {
                Error(diagnostics, "VP028", $"Page '{page.Name}' has invalid starter-template metadata.");
            }
        }

        ValidateRelativePath(project.RazorProjectPath, diagnostics, "VP008");
        ValidateRelativePath(project.LocalHostProjectPath, diagnostics, "VP009");
        ValidateRelativePath(project.Theme.AdditionalCssPath, diagnostics, "VP015");

        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in project.Assets)
        {
            if (!IsStableId(asset.AssetId) || !assetIds.Add(asset.AssetId))
            {
                Error(diagnostics, "VP016", $"Asset ID '{asset.AssetId}' is invalid or duplicated.");
            }
            ValidateRelativePath(asset.RelativePath, diagnostics, "VP017");
            if ((asset.Name?.Length ?? 0) > 120
                || (asset.AltText?.Length ?? 0) > 500)
            {
                Error(diagnostics, "VP030", $"Asset '{asset.RelativePath}' has invalid metadata.");
            }
            if (!string.IsNullOrWhiteSpace(asset.Sha256)
                && !Sha256Pattern.IsMatch(asset.Sha256))
            {
                Error(diagnostics, "VP018", $"Asset '{asset.RelativePath}' has an invalid SHA-256 identity.");
            }
        }

        var templateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in project.Templates)
        {
            if (!IsStableId(template.TemplateId) || !templateIds.Add(template.TemplateId))
            {
                Error(diagnostics, "VP019", $"Template ID '{template.TemplateId}' is invalid or duplicated.");
            }
            ValidateRelativePath(template.RelativePath, diagnostics, "VP020");
        }

        foreach (var token in project.Theme.Tokens)
        {
            if (!ThemeTokenPattern.IsMatch(token.Key) || !IsSafeCssValue(token.Value))
            {
                Error(diagnostics, "VP021", $"Theme token '{token.Key}' is not safe declarative CSS.");
            }
        }
        foreach (var (paths, scripts) in new[] { (project.SharedStylesheetPaths, false), (project.SharedJavaScriptPaths, true) })
        {
            if (paths is null) continue;
            if (paths.Count > 32) { Error(diagnostics, "VP031", "Use at most 32 shared web assets of each type."); continue; }
            foreach (var path in paths)
                try { _ = scripts ? VisualPageGenerator.SharedScriptUrl(path) : HyperpageStylesheetService.PublishedUrl(project, path); }
                catch (VisualPageValidationException error) { Error(diagnostics, "VP032", error.Message); }
        }
        return diagnostics;
    }

    public IReadOnlyList<VisualPageDiagnostic> ValidatePage(
        VisualPageDocument page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var diagnostics = new List<VisualPageDiagnostic>();
        if (page.SchemaVersion != VisualPageContract.PageSchemaVersion)
        {
            Error(diagnostics, "VP101", "Unsupported Hyperpage document schema version.");
        }

        if (!IsStableId(page.PageId) || !IsStableId(page.ProjectId))
        {
            Error(diagnostics, "VP102", "Project and page IDs must be stable 32-character GUID identities.");
        }

        if (!Enum.IsDefined(page.FileType))
        {
            Error(diagnostics, "VP137", "The page file type is unsupported.");
        }

        if (!IsSafeRoute(page.Route))
        {
            Error(diagnostics, "VP103", "The page route is not a safe Hyperpage route.");
        }

        if (!IdentifierPattern.IsMatch(page.ComponentName))
        {
            Error(diagnostics, "VP104", "The component name is not a valid C# identifier.");
        }

        if (string.IsNullOrWhiteSpace(page.Name) || page.Name.Length > 200
            || string.IsNullOrWhiteSpace(page.Title) || page.Title.Length > 300
            || page.Description.Length > 2_000)
        {
            Error(diagnostics, "VP136", "Page name, title, or description exceeds the metadata boundary.");
        }

        if (!SlugPattern.IsMatch(page.Slug))
        {
            Error(diagnostics, "VP113", "The page slug is invalid.");
        }

        if (!string.Equals(
                page.GeneratorVersion,
                VisualPageContract.GeneratorVersion,
                StringComparison.Ordinal))
        {
            Error(diagnostics, "VP114", "The page generator version is unsupported.");
        }

        if (!string.IsNullOrWhiteSpace(page.Revision)
            && !Sha256Pattern.IsMatch(page.Revision))
        {
            Error(diagnostics, "VP115", "The page revision must be a SHA-256 identity.");
        }

        var stateNames = new HashSet<string>(StringComparer.Ordinal);
        if (page.StylesheetPaths is null || page.StylesheetPaths.Count > 64)
        {
            Error(diagnostics, "VP137", "A page supports up to 64 explicit stylesheet references.");
        }
        else
        {
            foreach (var stylesheet in page.StylesheetPaths)
            {
                try
                {
                    HyperpageStylesheetService.NormalizeStylesheetPath(stylesheet);
                }
                catch (Exception exception) when (exception is VisualPageValidationException or ArgumentException)
                {
                    Error(diagnostics, "VP137", "Invalid page stylesheet: " + exception.Message);
                }
            }
        }
        var stateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in page.State)
        {
            if (!IsStableId(state.StateId) || !stateIds.Add(state.StateId))
            {
                Error(diagnostics, "VP116", $"State ID '{state.StateId}' is invalid or duplicated.");
            }
            if (!IdentifierPattern.IsMatch(state.Name))
            {
                Error(diagnostics, "VP105", $"State name '{state.Name}' is not a valid C# identifier.");
            }
            else if (!stateNames.Add(state.Name))
            {
                Error(diagnostics, "VP106", $"Duplicate state name '{state.Name}'.");
            }
            if (!TypeNamePattern.IsMatch(state.TypeName))
            {
                Error(diagnostics, "VP117", $"State type '{state.TypeName}' is not a bounded C# type name.");
            }
            if (!IsSafeCodeExpression(state.InitialValue))
            {
                Error(diagnostics, "VP118", $"State '{state.Name}' has an unsafe or multiline initializer.");
            }
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var bindingIds = new HashSet<string>(StringComparer.Ordinal);
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        ValidateElement(
            page.Root,
            null,
            0,
            ids,
            bindingIds,
            eventIds,
            ref count,
            diagnostics);
        return diagnostics;
    }

    public void ThrowIfInvalid(VisualPageDocument page)
    {
        var errors = ValidatePage(page)
            .Where(diagnostic => diagnostic.Severity ==
                                 VisualPageDiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new VisualPageValidationException(string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Code}: {error.Message}")));
        }
    }

    private void ValidateElement(
        VisualPageElement element,
        VisualPageElement? parent,
        int depth,
        HashSet<string> ids,
        HashSet<string> bindingIds,
        HashSet<string> eventIds,
        ref int count,
        List<VisualPageDiagnostic> diagnostics)
    {
        count++;
        if (count > VisualPageContract.MaximumElementCount)
        {
            Error(diagnostics, "VP107", "The page exceeds the maximum element count.");
            return;
        }

        if (depth > VisualPageContract.MaximumTreeDepth)
        {
            ElementError(diagnostics, "VP108", "The element tree is too deep.", element);
            return;
        }

        if (!IsStableId(element.ElementId) || !ids.Add(element.ElementId))
        {
            ElementError(diagnostics, "VP109", "Element IDs must be stable, unique GUID identities.", element);
        }

        if (!_catalog.TryGet(element.DescriptorId, out var descriptor))
        {
            ElementError(diagnostics, "VP110", $"Unknown descriptor '{element.DescriptorId}'.", element);
            return;
        }

        if (parent is null && element.DescriptorId != "core.page")
        {
            ElementError(diagnostics, "VP111", "The root element must use core.page.", element);
        }

        if (!descriptor.AllowsChildren && element.Children.Count > 0)
        {
            ElementError(diagnostics, "VP112", $"{descriptor.DisplayName} cannot contain children.", element);
        }

        if (parent is not null
            && _catalog.TryGet(parent.DescriptorId, out var parentDescriptor)
            && descriptor.Authoring.AllowedParentMarkupNames.Count > 0
            && !descriptor.Authoring.AllowedParentMarkupNames.Contains(
                parentDescriptor.MarkupName,
                StringComparer.OrdinalIgnoreCase))
        {
            ElementError(
                diagnostics,
                "VP139",
                $"{descriptor.DisplayName} must be placed inside "
                + string.Join(
                    ", ",
                    descriptor.Authoring.AllowedParentMarkupNames.Select(name =>
                        $"<{name}>"))
                + $", not <{parentDescriptor.MarkupName}>.",
                element);
        }

        if (descriptor.IsVoid && !string.IsNullOrWhiteSpace(element.Text))
        {
            ElementError(diagnostics, "VP137", $"{descriptor.DisplayName} cannot contain text.", element);
        }

        if (descriptor.IsObsolete)
        {
            Warning(diagnostics, "VP203", $"{descriptor.DisplayName} is obsolete HTML and should be replaced before publication.", element);
        }

        if (descriptor.IsDocumentOnly)
        {
            Warning(diagnostics, "VP204", $"{descriptor.DisplayName} belongs to a complete document shell; prefer the Page metadata and source tools inside a portable component.", element);
        }

        if (descriptor.RequiresAltText
            && (!element.Attributes.TryGetValue("alt", out var alt)
                || string.IsNullOrWhiteSpace(alt)))
        {
            Warning(diagnostics, "VP201", "Image alt text is required before publication.", element);
        }

        if (descriptor.RequiresAccessibleLabel
            && !HasAccessibleLabel(element))
        {
            Warning(diagnostics, "VP202", $"{descriptor.DisplayName} needs an accessible label.", element);
        }

        if (!string.IsNullOrWhiteSpace(element.CustomRazor)
            && element.DescriptorId != "custom.razor")
        {
            ElementError(diagnostics, "VP119", "Raw Razor is accepted only by an explicit Custom Razor Fragment node.", element);
        }

        foreach (var attribute in element.Attributes)
        {
            if (!AttributeNamePattern.IsMatch(attribute.Key))
            {
                ElementError(diagnostics, "VP120", $"Attribute name '{attribute.Key}' is invalid.", element);
            }
            else if (!IsAllowedAttribute(descriptor, attribute.Key))
            {
                ElementError(diagnostics, "VP121", $"Attribute '{attribute.Key}' is not supported by {descriptor.DisplayName}.", element);
            }
            else if (IsUrlAttribute(attribute.Key) && !IsSafeUrl(attribute.Value))
            {
                ElementError(diagnostics, "VP122", $"Attribute '{attribute.Key}' uses an unsafe URL.", element);
            }
            else if (_catalog.TryGetAttributeDefinition(
                         descriptor,
                         attribute.Key,
                         out var definition)
                     && !IsValidAttributeValue(definition, attribute.Value))
            {
                ElementError(
                    diagnostics,
                    "VP138",
                    $"Attribute '{attribute.Key}' has an invalid {definition.EditorKind.ToString().ToLowerInvariant()} value.",
                    element);
            }
        }

        foreach (var style in element.Styles)
        {
            if (!CssPropertyPattern.IsMatch(style.Key) || !IsSafeCssValue(style.Value))
            {
                ElementError(diagnostics, "VP123", $"Style '{style.Key}' is not safe declarative CSS.", element);
            }
        }

        foreach (var layout in element.Layout)
        {
            ValidateLayout(layout.Value, element, diagnostics);
        }

        foreach (var binding in element.Bindings)
        {
            if (!IsStableId(binding.BindingId) || !bindingIds.Add(binding.BindingId))
            {
                ElementError(diagnostics, "VP124", $"Binding ID '{binding.BindingId}' is invalid or duplicated.", element);
            }
            if (!IsSafeCodeExpression(binding.Expression))
            {
                ElementError(diagnostics, "VP125", "Binding expressions must be bounded, single-line C# expressions.", element);
            }
            if (binding.Kind == VisualPageBindingKind.Attribute
                && (!AttributeNamePattern.IsMatch(binding.Target)
                    || !IsAllowedAttribute(descriptor, binding.Target)))
            {
                ElementError(diagnostics, "VP126", $"Binding target '{binding.Target}' is not a supported attribute.", element);
            }
            if (binding.Format is { } format && !IsSafeSingleLine(format, allowEmpty: true))
            {
                ElementError(diagnostics, "VP127", "Binding formats must be single-line text.", element);
            }
        }

        foreach (var eventBinding in element.Events)
        {
            if (!IsStableId(eventBinding.EventId) || !eventIds.Add(eventBinding.EventId))
            {
                ElementError(diagnostics, "VP128", $"Event ID '{eventBinding.EventId}' is invalid or duplicated.", element);
            }
            if (!IdentifierPattern.IsMatch(eventBinding.HandlerName))
            {
                ElementError(diagnostics, "VP129", $"Event handler '{eventBinding.HandlerName}' is not a C# identifier.", element);
            }
            if (!string.IsNullOrWhiteSpace(eventBinding.ArgumentType)
                && !TypeNamePattern.IsMatch(eventBinding.ArgumentType))
            {
                ElementError(diagnostics, "VP130", $"Event argument type '{eventBinding.ArgumentType}' is invalid.", element);
            }
        }

        if (element.DescriptorId == "core.condition"
            && !IsSafeCodeExpression(element.Attributes.GetValueOrDefault("condition", string.Empty)))
        {
            ElementError(diagnostics, "VP131", "Conditional regions require a bounded, single-line expression.", element);
        }
        if (element.DescriptorId == "core.loop")
        {
            if (!IsSafeCodeExpression(element.Attributes.GetValueOrDefault("items", string.Empty)))
            {
                ElementError(diagnostics, "VP132", "Loop regions require a bounded, single-line items expression.", element);
            }
            if (!IdentifierPattern.IsMatch(element.Attributes.GetValueOrDefault("item", "item")))
            {
                ElementError(diagnostics, "VP133", "Loop item names must be C# identifiers.", element);
            }
        }

        foreach (var child in element.Children)
        {
            ValidateElement(
                child,
                element,
                depth + 1,
                ids,
                bindingIds,
                eventIds,
                ref count,
                diagnostics);
        }
    }

    private static bool HasAccessibleLabel(VisualPageElement element) =>
        element.Attributes.TryGetValue("aria-label", out var label)
        && !string.IsNullOrWhiteSpace(label)
        || element.Children.Any(child => child.DescriptorId == "core.label")
        || element.DescriptorId == "core.form"
           && element.Attributes.ContainsKey("Model");

    private static bool IsStableId(string value) =>
        Guid.TryParseExact(value, "N", out _);

    private static bool IsNamespace(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 256
        && value.Split('.', StringSplitOptions.None)
            .All(segment => IdentifierPattern.IsMatch(segment));

    private static bool IsSafeRoute(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 512
            || !value.StartsWith("/", StringComparison.Ordinal)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Any(char.IsControl))
        {
            return false;
        }

        return !value.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static bool IsAllowedAttribute(
        VisualPageComponentDescriptor descriptor,
        string name) =>
        descriptor.AllowedAttributes.Contains(name, StringComparer.OrdinalIgnoreCase)
        || VisualPageAttributeCatalog.IsGlobalName(name);

    private static bool IsUrlAttribute(string name) =>
        VisualPageAttributeCatalog.Resolve(name).EditorKind ==
        VisualPageAttributeEditorKind.Url;

    private static bool IsValidAttributeValue(
        VisualPageAttributeDescriptor definition,
        string value)
    {
        if (!IsSafeSingleLine(value, allowEmpty: true))
        {
            return false;
        }

        if (definition.EditorKind == VisualPageAttributeEditorKind.Boolean)
        {
            return string.IsNullOrEmpty(value)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                   || value.Equals(definition.Name, StringComparison.OrdinalIgnoreCase);
        }

        return !definition.RestrictToSuggestedValues
               || definition.SuggestedValues.Contains(
                   value,
                   StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSafeUrl(string value)
    {
        if (!IsSafeSingleLine(value, allowEmpty: false))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return !value.StartsWith("//", StringComparison.Ordinal);
        }

        if (absolute.Scheme is "http" or "https" or "mailto" or "tel")
        {
            return true;
        }

        return absolute.Scheme == "data"
               && value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
               && value.Contains(";base64,", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeCssValue(string value) =>
        IsSafeSingleLine(value, allowEmpty: false)
        && value.Length <= 4096
        && value.IndexOfAny(['{', '}', ';', '<', '>']) < 0
        && !value.Contains("@import", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("expression", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("javascript:", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeCodeExpression(string value) =>
        IsSafeSingleLine(value, allowEmpty: false)
        && value.Length <= 4096
        && value.IndexOfAny([';', '#']) < 0;

    private static bool IsSafeSingleLine(string value, bool allowEmpty) =>
        (allowEmpty || !string.IsNullOrWhiteSpace(value))
        && value.Length <= 8192
        && !value.Any(char.IsControl);

    private static void ValidateLayout(
        VisualPageLayoutSpec layout,
        VisualPageElement element,
        List<VisualPageDiagnostic> diagnostics)
    {
        var declarations = new[]
        {
            ("display", layout.Display),
            ("flex-direction", layout.FlexDirection),
            ("flex-wrap", layout.FlexWrap),
            ("grid-template-columns", layout.GridTemplateColumns),
            ("flex-basis", layout.FlexBasis),
            ("gap", layout.Gap),
            ("padding", layout.Padding),
            ("margin", layout.Margin),
            ("min-width", layout.MinWidth),
            ("max-width", layout.MaxWidth),
            ("min-height", layout.MinHeight),
            ("max-height", layout.MaxHeight),
            ("align-items", layout.AlignItems),
            ("justify-content", layout.JustifyContent),
            ("align-self", layout.AlignSelf)
        };
        foreach (var declaration in declarations.Where(value =>
                     !string.IsNullOrWhiteSpace(value.Item2)))
        {
            if (!IsSafeCssValue(declaration.Item2))
            {
                ElementError(
                    diagnostics,
                    "VP134",
                    $"Layout value '{declaration.Item1}' is not safe declarative CSS.",
                    element);
            }
        }
        if (layout.ColumnSpan is < 1 or > 12 || layout.RowSpan is < 1 or > 12)
        {
            ElementError(diagnostics, "VP135", "Grid spans must be between 1 and 12.", element);
        }
    }

    private static void ValidateRelativePath(
        string value,
        List<VisualPageDiagnostic> diagnostics,
        string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."
                                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            Error(diagnostics, code, $"'{value}' is not a safe project-relative path.");
        }
    }

    private static void Error(
        List<VisualPageDiagnostic> diagnostics,
        string code,
        string message) => diagnostics.Add(new VisualPageDiagnostic
        {
            Code = code,
            Severity = VisualPageDiagnosticSeverity.Error,
            Message = message
        });

    private static void ElementError(
        List<VisualPageDiagnostic> diagnostics,
        string code,
        string message,
        VisualPageElement element) => diagnostics.Add(new VisualPageDiagnostic
        {
            Code = code,
            Severity = VisualPageDiagnosticSeverity.Error,
            Message = message,
            ElementId = element.ElementId
        });

    private static void Warning(
        List<VisualPageDiagnostic> diagnostics,
        string code,
        string message,
        VisualPageElement element) => diagnostics.Add(new VisualPageDiagnostic
        {
            Code = code,
            Severity = VisualPageDiagnosticSeverity.Warning,
            Message = message,
            ElementId = element.ElementId
        });
}

public sealed class VisualPageValidationException : Exception
{
    public VisualPageValidationException(string message) : base(message)
    {
    }
}
