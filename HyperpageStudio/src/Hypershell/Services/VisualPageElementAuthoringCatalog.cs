using Hypershell.Models;

namespace Hypershell.Services;

public enum VisualPageElementTextEditorKind
{
    None,
    SingleLine,
    MultiLine,
    Code
}

public sealed class VisualPageElementStarter
{
    public required string DescriptorId { get; init; }
    public string? Text { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<VisualPageElementStarter> Children { get; init; } = [];
}

public sealed class VisualPageElementAuthoringDescriptor
{
    public required string Badge { get; init; }
    public required string PlacementGuidance { get; init; }
    public required string ChildrenGuidance { get; init; }
    public required string CanvasGuidance { get; init; }
    public required string TextEditorLabel { get; init; }
    public VisualPageElementTextEditorKind TextEditorKind { get; init; }
    public IReadOnlyList<string> QuickAttributeNames { get; init; } = [];
    public IReadOnlyList<string> SuggestedChildDescriptorIds { get; init; } = [];
    public IReadOnlyList<VisualPageElementStarter> StarterChildren { get; init; } = [];
    public IReadOnlyList<string> AllowedParentMarkupNames { get; init; } = [];
    public bool IsCommon { get; init; }
    public bool SupportsBindings { get; init; } = true;
    public bool SupportsEvents { get; init; }
    public string Warning { get; init; } = string.Empty;
}

internal static class VisualPageElementAuthoringCatalog
{
    private static readonly HashSet<string> CommonHtmlTags = new(
        [
            "a", "article", "aside", "audio", "blockquote", "br", "button",
            "details", "dialog", "div", "figure", "footer", "form", "h1",
            "h2", "h3", "header", "hr", "iframe", "img", "input", "label",
            "main", "nav", "ol", "p", "picture", "pre", "progress", "section",
            "select", "span", "strong", "table", "textarea", "ul", "video"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CommonInputDescriptorIds = new(
        [
            "html.input.checkbox", "html.input.date", "html.input.email",
            "html.input.file", "html.input.number", "html.input.radio",
            "html.input.search", "html.input.submit", "html.input.text"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ConstrainedCanvasTags = new(
        [
            "area", "audio", "base", "body", "caption", "col", "colgroup",
            "datalist", "details", "dl", "fieldset", "head", "hgroup", "html",
            "link", "map", "menu", "meta", "ol", "optgroup", "option",
            "picture", "ruby", "selectedcontent", "source", "table", "tbody",
            "td", "tfoot", "th", "thead", "title", "tr", "track", "ul",
            "video"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> MultilineTextTags = new(
        ["blockquote", "canvas", "dd", "p", "pre", "textarea"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CodeTextTags = new(
        ["script", "style"],
        StringComparer.OrdinalIgnoreCase);

    public static VisualPageCanvasRepresentation ResolveCanvasRepresentation(
        string descriptorId,
        string markupName,
        VisualPageCanvasRepresentation requested)
    {
        if (requested == VisualPageCanvasRepresentation.Placeholder
            || ConstrainedCanvasTags.Contains(markupName)
            || descriptorId.Equals(
                "html.input.hidden",
                StringComparison.OrdinalIgnoreCase))
        {
            return VisualPageCanvasRepresentation.Placeholder;
        }
        return requested;
    }

    public static VisualPageElementAuthoringDescriptor Create(
        string descriptorId,
        string markupName,
        string category,
        bool isStructural,
        bool isVoid,
        bool allowsChildren,
        bool isStandardHtml,
        bool isObsolete,
        bool isDocumentOnly,
        string defaultText,
        IReadOnlyList<string> allowedAttributes,
        VisualPageCanvasRepresentation canvasRepresentation,
        VisualPageCodeClassification classification)
    {
        var tag = markupName.ToLowerInvariant();
        var starters = CreateStarters(descriptorId, tag);
        var suggestedChildren = SuggestedChildren(descriptorId, tag, isStructural)
            .Concat(starters.Select(value => value.DescriptorId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var textKind = ResolveTextEditorKind(
            tag,
            isStructural,
            isVoid,
            allowsChildren,
            descriptorId,
            defaultText);
        var warning = isObsolete
            ? "This obsolete element is available for compatibility and import repair. New pages should use the suggested modern replacement."
            : isDocumentOnly
                ? "This element belongs to a complete HTML document shell. Portable Studio pages normally use Page details, CSS, or code files instead."
                : classification != VisualPageCodeClassification.Declarative
                    ? "This element enters the reviewed-code publication lane and runs only in compiled preview."
                    : string.Empty;
        return new VisualPageElementAuthoringDescriptor
        {
            Badge = ResolveBadge(
                descriptorId,
                isStandardHtml,
                isObsolete,
                isDocumentOnly,
                classification),
            PlacementGuidance = PlacementGuidance(tag, category),
            ChildrenGuidance = ChildrenGuidance(
                tag,
                isStructural,
                isVoid),
            CanvasGuidance = canvasRepresentation ==
                             VisualPageCanvasRepresentation.Placeholder
                ? "Shown as a labelled, selectable placeholder on the design canvas; compiled preview renders the real element."
                : "Rendered directly on the safe design canvas with selection, drag, and resize controls.",
            TextEditorKind = textKind,
            TextEditorLabel = TextEditorLabel(tag, textKind),
            QuickAttributeNames = QuickAttributes(tag, allowedAttributes),
            SuggestedChildDescriptorIds = suggestedChildren,
            StarterChildren = starters,
            AllowedParentMarkupNames = AllowedParentMarkupNames(tag),
            IsCommon = !isObsolete
                       && !isDocumentOnly
                       && (CommonHtmlTags.Contains(tag)
                           || IsCommonStudioObject(descriptorId))
                       && (!descriptorId.StartsWith(
                               "html.input.",
                               StringComparison.OrdinalIgnoreCase)
                           || CommonInputDescriptorIds.Contains(descriptorId)),
            SupportsBindings = !isObsolete && !isDocumentOnly,
            SupportsEvents = !isObsolete
                             && !isDocumentOnly
                             && classification ==
                             VisualPageCodeClassification.Declarative
                             && tag is not "base" and not "head" and not "html"
                                 and not "link" and not "meta" and not "script"
                                 and not "style" and not "title",
            Warning = warning
        };
    }

    private static string ResolveBadge(
        string descriptorId,
        bool isStandardHtml,
        bool isObsolete,
        bool isDocumentOnly,
        VisualPageCodeClassification classification)
    {
        if (isObsolete)
        {
            return "LEGACY HTML";
        }
        if (isDocumentOnly)
        {
            return "DOCUMENT SHELL";
        }
        if (classification != VisualPageCodeClassification.Declarative)
        {
            return "REVIEWED CODE";
        }
        if (descriptorId.StartsWith("atlas.", StringComparison.OrdinalIgnoreCase))
        {
            return "ATLAS";
        }
        if (isStandardHtml)
        {
            return "HTML";
        }
        if (descriptorId.StartsWith("core.", StringComparison.OrdinalIgnoreCase))
        {
            return "STUDIO";
        }
        return "COMPONENT";
    }

    private static VisualPageElementTextEditorKind ResolveTextEditorKind(
        string tag,
        bool isStructural,
        bool isVoid,
        bool allowsChildren,
        string descriptorId,
        string defaultText)
    {
        if (isVoid || !allowsChildren
                   || descriptorId is "custom.razor" or "custom.component")
        {
            return VisualPageElementTextEditorKind.None;
        }
        if (CodeTextTags.Contains(tag))
        {
            return VisualPageElementTextEditorKind.Code;
        }
        if (isStructural && string.IsNullOrWhiteSpace(defaultText))
        {
            return VisualPageElementTextEditorKind.None;
        }
        return MultilineTextTags.Contains(tag)
            ? VisualPageElementTextEditorKind.MultiLine
            : VisualPageElementTextEditorKind.SingleLine;
    }

    private static string TextEditorLabel(
        string tag,
        VisualPageElementTextEditorKind kind)
    {
        if (kind == VisualPageElementTextEditorKind.None)
        {
            return "Child content";
        }
        return tag switch
        {
            "option" => "Option label",
            "summary" => "Summary text",
            "textarea" => "Initial text",
            "title" => "Document title",
            "script" => "JavaScript source",
            "style" => "CSS source",
            "img" or "area" => "Alternative text",
            _ => "Text content"
        };
    }

    private static string PlacementGuidance(string tag, string category) =>
        tag switch
        {
            "html" => "Use only as the root of a complete HTML document.",
            "head" or "body" => "Place directly inside <html> in a complete document.",
            "base" or "link" or "meta" or "style" or "title" =>
                "Place in a complete document's <head>; use Page details or project files for normal Studio pages.",
            "li" => "Place inside <ul>, <ol>, or <menu>.",
            "dt" or "dd" => "Place inside <dl>.",
            "figcaption" => "Place as the first or last child of <figure>.",
            "source" => "Place inside <picture>, <audio>, or <video>.",
            "track" => "Place inside <audio> or <video>.",
            "area" => "Place inside <map>.",
            "caption" or "colgroup" or "thead" or "tbody" or "tfoot" =>
                "Place directly inside <table>.",
            "col" => "Place inside <colgroup>.",
            "tr" => "Place inside <thead>, <tbody>, or <tfoot>.",
            "td" or "th" => "Place inside <tr>.",
            "option" => "Place inside <select>, <datalist>, or <optgroup>.",
            "optgroup" => "Place inside <select>.",
            "legend" => "Place as the first child of <fieldset>.",
            "summary" => "Place as the first child of <details>.",
            "selectedcontent" => "Place inside the leading <button> of a customizable <select>.",
            "rt" or "rp" => "Place inside <ruby>.",
            "slot" => "Use in a shadow-tree template or custom component.",
            _ when category.Contains("Text", StringComparison.OrdinalIgnoreCase)
                || category.Equals("Typography", StringComparison.OrdinalIgnoreCase) =>
                "Place wherever inline or flow text is allowed.",
            _ => "Place inside the selected container or section."
        };

    private static string ChildrenGuidance(
        string tag,
        bool isStructural,
        bool isVoid)
    {
        if (isVoid)
        {
            return "This is an empty element; edit it through properties rather than child content.";
        }
        return tag switch
        {
            "head" => "Document metadata such as title, meta, link, style, and script.",
            "html" => "A document head followed by a document body.",
            "body" => "Visible flow content for the document.",
            "ul" or "ol" or "menu" => "List-item (<li>) elements.",
            "dl" => "Term (<dt>) and description (<dd>) groups.",
            "table" => "Caption, column groups, and row groups in table order.",
            "thead" or "tbody" or "tfoot" => "Table rows (<tr>).",
            "tr" => "Header or data cells (<th> or <td>).",
            "colgroup" => "Column definitions (<col>), unless span defines the group directly.",
            "select" => "Options, option groups, and an optional customizable button.",
            "datalist" or "optgroup" => "Option (<option>) elements.",
            "picture" => "Source choices followed by one fallback image.",
            "audio" or "video" => "Source and track elements plus fallback text.",
            "details" => "A summary followed by the revealed content.",
            "fieldset" => "A leading legend followed by related form controls and help text.",
            "figure" => "Self-contained media or content with an optional figure caption.",
            "map" => "Image-map areas plus any supporting flow content.",
            "ruby" => "Base text with ruby-text and fallback-parenthesis elements.",
            _ when isStructural => "Flow elements arranged as child nodes.",
            _ => "Text and inline elements."
        };
    }

    private static IReadOnlyList<string> QuickAttributes(
        string tag,
        IReadOnlyList<string> allowed)
    {
        var preferred = tag switch
        {
            "a" => new[] { "href", "target", "rel", "download" },
            "area" => new[] { "alt", "href", "shape", "coords" },
            "audio" or "video" => new[] { "src", "controls", "preload", "autoplay", "muted" },
            "button" => new[] { "type", "disabled", "name", "value", "popovertarget" },
            "form" => new[] { "action", "method", "autocomplete", "name", "novalidate" },
            "iframe" => new[] { "src", "aria-label", "loading", "sandbox", "allow" },
            "img" => new[] { "src", "alt", "width", "height", "loading", "srcset" },
            "input" => new[] { "type", "name", "value", "placeholder", "required", "disabled" },
            "link" => new[] { "rel", "href", "as", "media", "type" },
            "meta" => new[] { "name", "content", "http-equiv", "charset" },
            "ol" => new[] { "start", "reversed", "type" },
            "select" => new[] { "name", "required", "multiple", "size", "disabled" },
            "source" => new[] { "src", "srcset", "media", "type", "sizes" },
            "td" or "th" => new[] { "colspan", "rowspan", "headers", "scope" },
            "textarea" => new[] { "name", "placeholder", "rows", "required", "readonly" },
            "track" => new[] { "src", "kind", "srclang", "label", "default" },
            _ => Array.Empty<string>()
        };
        return preferred
            .Where(name => allowed.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Concat(allowed)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<string> SuggestedChildren(
        string descriptorId,
        string tag,
        bool isStructural)
    {
        if (!isStructural)
        {
            return [];
        }
        return tag switch
        {
            "section" or "article" or "aside" or "header" or "footer" or "main" =>
                ["core.heading2", "core.paragraph", "core.container"],
            "nav" => ["core.link", "core.list"],
            "figure" => ["core.image", "core.caption"],
            "ul" or "ol" or "menu" => ["core.list-item"],
            "form" or "fieldset" => ["core.label", "html.input.text", "core.button"],
            "table" => ["html.caption", "html.thead", "html.tbody"],
            "thead" or "tbody" or "tfoot" => ["html.tr"],
            "tr" => ["html.th", "html.td"],
            "colgroup" => ["html.col"],
            "dl" => ["html.dt", "html.dd"],
            "map" => ["html.area"],
            "ruby" => ["core.inline", "html.rt", "html.rp"],
            "select" or "datalist" or "optgroup" => ["core.option"],
            "picture" => ["html.source", "core.image"],
            "audio" or "video" => ["html.source", "html.track"],
            "details" => ["html.summary", "core.paragraph"],
            _ when descriptorId.StartsWith("core.", StringComparison.OrdinalIgnoreCase) =>
                ["core.heading2", "core.paragraph", "core.container"],
            _ => ["core.paragraph", "core.container"]
        };
    }

    private static IReadOnlyList<string> AllowedParentMarkupNames(string tag) =>
        tag switch
        {
            "base" or "title" => ["head"],
            "body" or "head" => ["html"],
            "caption" or "colgroup" or "thead" or "tbody" or "tfoot" =>
                ["table"],
            "col" => ["colgroup"],
            "dd" or "dt" => ["dl", "div"],
            "figcaption" => ["figure"],
            "frame" or "noframes" => ["frameset"],
            "legend" => ["fieldset", "optgroup"],
            "li" => ["ul", "ol", "menu"],
            "menuitem" => ["menu"],
            "option" =>
                ["select", "datalist", "optgroup", "div", "InputSelect"],
            "optgroup" => ["select", "div", "InputSelect"],
            "param" => ["object"],
            "rb" or "rp" or "rt" or "rtc" => ["ruby"],
            "selectedcontent" => ["button"],
            "source" => ["picture", "audio", "video"],
            "summary" => ["details"],
            "td" or "th" => ["tr"],
            "tr" => ["table", "thead", "tbody", "tfoot"],
            "track" => ["audio", "video"],
            _ => []
        };

    private static IReadOnlyList<VisualPageElementStarter> CreateStarters(
        string descriptorId,
        string tag)
    {
        if (descriptorId.Equals("core.section", StringComparison.OrdinalIgnoreCase)
            || descriptorId.Equals("html.section", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                Starter("core.heading2", "Section heading"),
                Starter("core.paragraph", "Add section copy here.")
            ];
        }
        return tag switch
        {
            "nav" => [Starter("core.link", "Navigation link")],
            "article" =>
            [
                Starter("core.heading2", "Article heading"),
                Starter("core.paragraph", "Add article copy here.")
            ],
            "hgroup" =>
            [
                Starter("core.heading2", "Heading"),
                Starter("core.paragraph", "Supporting heading or tagline")
            ],
            "picture" =>
            [
                Starter("html.source", attributes: new Dictionary<string, string>
                {
                    ["media"] = "(min-width: 768px)"
                }),
                Starter("core.image")
            ],
            "audio" => [Starter("html.source")],
            "video" => [Starter("html.source"), Starter("html.track")],
            "figure" => [Starter("core.image"), Starter("core.caption")],
            "ul" or "ol" or "menu" => [Starter("core.list-item")],
            "dl" => [Starter("html.dt"), Starter("html.dd")],
            "map" => [Starter("html.area")],
            "ruby" =>
            [
                Starter("core.inline", "漢"),
                Starter("html.rp", "("),
                Starter("html.rt", "kan"),
                Starter("html.rp", ")")
            ],
            "details" => [Starter("html.summary"), Starter("core.paragraph", "Details content")],
            "dialog" =>
            [
                Starter("core.heading2", "Dialog title"),
                Starter("core.paragraph", "Dialog content"),
                Starter("core.button", "Close")
            ],
            "fieldset" =>
            [
                Starter("html.legend"),
                Starter("core.label", "Field label"),
                Starter("html.input.text")
            ],
            "form" =>
            [
                Starter("core.label", "Field label"),
                Starter("html.input.text"),
                Starter("core.button", "Submit", new Dictionary<string, string>
                {
                    ["type"] = "submit"
                })
            ],
            "datalist" or "optgroup" or "select" =>
            [
                Starter("core.option", "First option"),
                Starter("core.option", "Second option")
            ],
            "colgroup" => [Starter("html.col")],
            "thead" =>
            [
                Starter("html.tr", children:
                [
                    Starter("html.th", "Heading 1"),
                    Starter("html.th", "Heading 2")
                ])
            ],
            "tbody" or "tfoot" =>
            [
                Starter("html.tr", children:
                [
                    Starter("html.td", "Cell 1"),
                    Starter("html.td", "Cell 2")
                ])
            ],
            "tr" =>
            [
                Starter("html.td", "Cell 1"),
                Starter("html.td", "Cell 2")
            ],
            "table" =>
            [
                Starter("html.caption", "Table caption"),
                Starter("html.thead", children:
                [
                    Starter("html.tr", children:
                    [
                        Starter("html.th", "Heading 1"),
                        Starter("html.th", "Heading 2")
                    ])
                ]),
                Starter("html.tbody", children:
                [
                    Starter("html.tr", children:
                    [
                        Starter("html.td", "Cell 1"),
                        Starter("html.td", "Cell 2")
                    ])
                ])
            ],
            "search" =>
            [
                Starter("html.input.search", attributes: new Dictionary<string, string>
                {
                    ["aria-label"] = "Search"
                }),
                Starter("core.button", "Search", new Dictionary<string, string>
                {
                    ["type"] = "submit"
                })
            ],
            _ => []
        };
    }

    private static VisualPageElementStarter Starter(
        string descriptorId,
        string? text = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<VisualPageElementStarter>? children = null) => new()
        {
            DescriptorId = descriptorId,
            Text = text,
            Attributes = attributes ??
                         new Dictionary<string, string>(StringComparer.Ordinal),
            Children = children ?? []
        };

    private static bool IsCommonStudioObject(string descriptorId) =>
        descriptorId is
            "core.flex" or
            "core.grid" or
            "core.spacer" or
            "core.field" or
            "core.form" or
            "core.input-text" or
            "core.input-number" or
            "core.input-checkbox" or
            "core.select" or
            "core.validation-message" or
            "core.validation-summary";
}
