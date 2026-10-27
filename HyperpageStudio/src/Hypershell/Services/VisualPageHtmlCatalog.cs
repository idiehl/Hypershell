using Hypershell.Models;

namespace Hypershell.Services;

internal static class VisualPageAttributeCatalog
{
    private static readonly HashSet<string> BooleanNames = new(
        [
            "allowfullscreen", "alpha", "async", "autofocus", "autoplay",
            "checked", "compact", "controls", "default", "defer", "disabled",
            "formnovalidate", "headingreset", "hidden", "inert", "ismap",
            "itemscope", "loop", "multiple", "muted", "nomodule", "noresize",
            "novalidate", "open", "playsinline", "readonly", "required",
            "reversed", "selected", "shadowrootclonable",
            "shadowrootcustomelementregistry", "shadowrootdelegatesfocus",
            "shadowrootserializable", "truespeed"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UrlNames = new(
        [
            "action", "cite", "data", "formaction", "href", "itemid",
            "poster", "src"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> NumberNames = new(
        [
            "colspan", "height", "high", "low", "maxlength", "minlength",
            "optimum", "rowspan", "size", "span", "start", "tabindex",
            "width", "headingoffset"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TokenListNames = new(
        [
            "accept", "allow", "as", "blocking", "class", "commandfor",
            "controlslist", "for", "headers", "imagesizes", "imagesrcset",
            "itemprop", "itemref", "itemtype", "part", "ping", "rel",
            "sandbox", "sizes", "srcset"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IdReferenceNames = new(
        [
            "commandfor", "form", "list", "popovertarget", "aria-controls",
            "aria-describedby", "aria-labelledby"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> GlobalNames = new(
        [
            "accesskey", "autocapitalize", "autocorrect", "autofocus",
            "class", "contenteditable", "dir", "draggable", "enterkeyhint",
            "exportparts", "headingoffset", "headingreset", "hidden", "id",
            "inert", "inputmode", "is", "itemid", "itemprop", "itemref",
            "itemscope", "itemtype", "lang", "nonce", "part", "popover",
            "role", "slot", "spellcheck", "tabindex", "title", "translate",
            "writingsuggestions"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] CommonAriaNames =
    [
        "aria-label", "aria-labelledby", "aria-describedby", "aria-controls",
        "aria-current", "aria-expanded", "aria-haspopup", "aria-hidden",
        "aria-invalid", "aria-live", "aria-pressed", "aria-required",
        "aria-selected"
    ];

    public static IReadOnlyList<VisualPageAttributeDescriptor> GlobalDefinitions { get; } =
        GlobalNames.OrderBy(value => value, StringComparer.Ordinal)
            .Concat(CommonAriaNames)
            .Select(name => Resolve(name, isGlobal: true))
            .ToList();

    public static bool IsGlobalName(string name) =>
        GlobalNames.Contains(name)
        || name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<VisualPageAttributeDescriptor> ResolveMany(
        IEnumerable<string> names,
        string? elementName = null,
        string? descriptorId = null) => names
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.Ordinal)
        .Select(name => ResolveForElement(
            name,
            elementName,
            descriptorId))
        .ToList();

    private static VisualPageAttributeDescriptor ResolveForElement(
        string name,
        string? elementName,
        string? descriptorId)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var tag = elementName?.Trim().ToLowerInvariant() ?? string.Empty;
        var inputType = descriptorId?.StartsWith(
            "html.input.",
            StringComparison.OrdinalIgnoreCase) == true
                ? descriptorId["html.input.".Length..].ToLowerInvariant()
                : null;
        if (normalized == "type")
        {
            return tag switch
            {
                "input" => Choice(
                    normalized,
                    VisualPageHtmlComponentCatalog.InputTypes,
                    restrict: true,
                    isGlobal: false),
                "button" => Choice(
                    normalized,
                    ["button", "submit", "reset"],
                    restrict: true,
                    isGlobal: false),
                "ol" => Choice(
                    normalized,
                    ["1", "a", "A", "i", "I"],
                    restrict: true,
                    isGlobal: false),
                "script" => Choice(
                    normalized,
                    ["module", "importmap", "speculationrules"],
                    restrict: false,
                    isGlobal: false),
                "menuitem" => Choice(
                    normalized,
                    ["command", "checkbox", "radio"],
                    restrict: true,
                    isGlobal: false),
                _ => Define(
                    normalized,
                    VisualPageAttributeEditorKind.Text,
                    isGlobal: false)
            };
        }
        if (normalized == "value")
        {
            var numericValue = tag is "meter" or "progress" or "li"
                               || inputType is "number" or "range";
            return Define(
                normalized,
                numericValue
                    ? VisualPageAttributeEditorKind.Number
                    : VisualPageAttributeEditorKind.Text,
                isGlobal: false);
        }
        if (normalized is "min" or "max" or "step")
        {
            var numericRange = tag is "meter" or "progress"
                               || inputType is "number" or "range";
            return Define(
                normalized,
                numericRange
                    ? VisualPageAttributeEditorKind.Number
                    : VisualPageAttributeEditorKind.Text,
                isGlobal: false);
        }
        if (normalized is "cols" or "rows")
        {
            return Define(
                normalized,
                tag == "frameset"
                    ? VisualPageAttributeEditorKind.Text
                    : VisualPageAttributeEditorKind.Number,
                isGlobal: false);
        }
        if (normalized == "loop" && tag == "marquee")
        {
            return Define(
                normalized,
                VisualPageAttributeEditorKind.Number,
                isGlobal: false);
        }
        if (normalized == "for")
        {
            return Define(
                normalized,
                tag == "output"
                    ? VisualPageAttributeEditorKind.TokenList
                    : VisualPageAttributeEditorKind.IdReference,
                isGlobal: false);
        }
        return Resolve(normalized, isGlobal: false);
    }

    public static VisualPageAttributeDescriptor Resolve(
        string name,
        bool isGlobal = false)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized switch
        {
            "autocomplete" => Choice(normalized, ["on", "off", "name", "email", "username", "current-password", "new-password", "street-address", "postal-code", "tel", "url"], false, isGlobal),
            "autocapitalize" => Choice(normalized, ["none", "sentences", "words", "characters"], true, isGlobal),
            "autocorrect" => Choice(normalized, ["on", "off"], true, isGlobal),
            "closedby" => Choice(normalized, ["any", "closerequest", "none"], true, isGlobal),
            "colorspace" => Choice(normalized, ["limited-srgb", "display-p3"], true, isGlobal),
            "command" => Choice(normalized, ["toggle-popover", "show-popover", "hide-popover", "close", "request-close", "show-modal"], false, isGlobal),
            "contenteditable" => Choice(normalized, ["true", "false", "plaintext-only"], true, isGlobal),
            "crossorigin" => Choice(normalized, ["anonymous", "use-credentials"], true, isGlobal),
            "decoding" => Choice(normalized, ["auto", "sync", "async"], true, isGlobal),
            "dir" => Choice(normalized, ["auto", "ltr", "rtl"], true, isGlobal),
            "draggable" => Choice(normalized, ["true", "false"], true, isGlobal),
            "enctype" or "formenctype" => Choice(normalized, ["application/x-www-form-urlencoded", "multipart/form-data", "text/plain"], true, isGlobal),
            "enterkeyhint" => Choice(normalized, ["enter", "done", "go", "next", "previous", "search", "send"], true, isGlobal),
            "fetchpriority" => Choice(normalized, ["auto", "high", "low"], true, isGlobal),
            "formmethod" or "method" => Choice(normalized, ["get", "post", "dialog"], true, isGlobal),
            "hidden" => Choice(normalized, ["true", "false", "until-found"], false, isGlobal),
            "http-equiv" => Choice(normalized, ["content-type", "default-style", "refresh", "x-ua-compatible", "content-security-policy"], false, isGlobal),
            "inputmode" => Choice(normalized, ["none", "text", "tel", "email", "url", "numeric", "decimal", "search"], true, isGlobal),
            "kind" => Choice(normalized, ["subtitles", "captions", "descriptions", "chapters", "metadata"], true, isGlobal),
            "loading" => Choice(normalized, ["eager", "lazy"], true, isGlobal),
            "popover" => Choice(normalized, ["auto", "manual", "hint"], true, isGlobal),
            "popovertargetaction" => Choice(normalized, ["toggle", "show", "hide"], true, isGlobal),
            "preload" => Choice(normalized, ["none", "metadata", "auto"], true, isGlobal),
            "referrerpolicy" => Choice(normalized, ["no-referrer", "no-referrer-when-downgrade", "origin", "origin-when-cross-origin", "same-origin", "strict-origin", "strict-origin-when-cross-origin", "unsafe-url"], true, isGlobal),
            "scope" => Choice(normalized, ["row", "col", "rowgroup", "colgroup"], true, isGlobal),
            "shadowrootmode" => Choice(normalized, ["open", "closed"], true, isGlobal),
            "shadowrootslotassignment" => Choice(normalized, ["named", "manual"], true, isGlobal),
            "shape" => Choice(normalized, ["circle", "default", "poly", "rect"], true, isGlobal),
            "spellcheck" or "writingsuggestions" => Choice(normalized, ["true", "false"], true, isGlobal),
            "target" or "formtarget" => Choice(normalized, ["_self", "_blank", "_parent", "_top"], false, isGlobal),
            "translate" => Choice(normalized, ["yes", "no"], true, isGlobal),
            "type" => Define(normalized, VisualPageAttributeEditorKind.Text, isGlobal),
            "wrap" => Choice(normalized, ["soft", "hard"], true, isGlobal),
            "datetime" => Define(normalized, VisualPageAttributeEditorKind.DateTime, isGlobal),
            "color" => Define(normalized, VisualPageAttributeEditorKind.Color, isGlobal),
            _ when BooleanNames.Contains(normalized) => Define(normalized, VisualPageAttributeEditorKind.Boolean, isGlobal, ["true", "false"]),
            _ when UrlNames.Contains(normalized) => Define(normalized, VisualPageAttributeEditorKind.Url, isGlobal),
            _ when IdReferenceNames.Contains(normalized) => Define(normalized, VisualPageAttributeEditorKind.IdReference, isGlobal),
            _ when NumberNames.Contains(normalized) => Define(normalized, VisualPageAttributeEditorKind.Number, isGlobal),
            _ when TokenListNames.Contains(normalized) => Define(normalized, VisualPageAttributeEditorKind.TokenList, isGlobal),
            _ => Define(normalized, VisualPageAttributeEditorKind.Text, isGlobal)
        };
    }

    private static VisualPageAttributeDescriptor Choice(
        string name,
        IReadOnlyList<string> values,
        bool restrict,
        bool isGlobal) => new()
        {
            Name = name,
            DisplayName = FriendlyName(name),
            Description = Describe(name),
            EditorKind = VisualPageAttributeEditorKind.Choice,
            SuggestedValues = values,
            RestrictToSuggestedValues = restrict,
            IsGlobal = isGlobal
        };

    private static VisualPageAttributeDescriptor Define(
        string name,
        VisualPageAttributeEditorKind kind,
        bool isGlobal,
        IReadOnlyList<string>? values = null) => new()
        {
            Name = name,
            DisplayName = FriendlyName(name),
            Description = Describe(name),
            EditorKind = kind,
            SuggestedValues = values ?? [],
            IsGlobal = isGlobal
        };

    private static string FriendlyName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized switch
        {
            "accept-charset" => "Accepted character sets",
            "accesskey" => "Access key",
            "allowfullscreen" => "Allow full screen",
            "aria-controls" => "ARIA controls",
            "aria-current" => "ARIA current item",
            "aria-describedby" => "ARIA described by",
            "aria-expanded" => "ARIA expanded",
            "aria-haspopup" => "ARIA has popup",
            "aria-hidden" => "ARIA hidden",
            "aria-invalid" => "ARIA invalid",
            "aria-label" => "ARIA label",
            "aria-labelledby" => "ARIA labelled by",
            "aria-live" => "ARIA live updates",
            "aria-pressed" => "ARIA pressed",
            "aria-required" => "ARIA required",
            "aria-selected" => "ARIA selected",
            "autocapitalize" => "Auto-capitalize",
            "autocomplete" => "Auto-complete",
            "autocorrect" => "Auto-correct",
            "autofocus" => "Auto-focus",
            "autoplay" => "Auto-play",
            "bgcolor" => "Background color",
            "closedby" => "Closed by",
            "codebase" => "Code base URL",
            "colorspace" => "Color space",
            "colspan" => "Column span",
            "commandfor" => "Command for",
            "contenteditable" => "Content editable",
            "controlslist" => "Playback controls list",
            "crossorigin" => "Cross-origin mode",
            "datetime" => "Date and time",
            "dirname" => "Direction field name",
            "enctype" => "Form encoding",
            "enterkeyhint" => "Enter key hint",
            "exportparts" => "Exported parts",
            "fetchpriority" => "Fetch priority",
            "formaction" => "Form action",
            "formenctype" => "Form encoding override",
            "formmethod" => "Form method",
            "formnovalidate" => "Skip form validation",
            "formtarget" => "Form target",
            "frameborder" => "Frame border",
            "headingoffset" => "Heading offset",
            "headingreset" => "Heading reset",
            "hreflang" => "Linked resource language",
            "hspace" => "Horizontal spacing",
            "http-equiv" => "HTTP equivalent",
            "imagesizes" => "Image sizes",
            "imagesrcset" => "Image source set",
            "inputmode" => "Input mode",
            "ismap" => "Server-side image map",
            "itemid" => "Item ID",
            "itemprop" => "Item property",
            "itemref" => "Item references",
            "itemscope" => "Item scope",
            "itemtype" => "Item type",
            "keytype" => "Key type",
            "marginheight" => "Margin height",
            "marginwidth" => "Margin width",
            "maxlength" => "Maximum length",
            "minlength" => "Minimum length",
            "nomodule" => "No module",
            "noresize" => "Prevent resizing",
            "novalidate" => "Skip validation",
            "playsinline" => "Play inline",
            "popovertarget" => "Popover target",
            "popovertargetaction" => "Popover target action",
            "readonly" => "Read only",
            "referrerpolicy" => "Referrer policy",
            "rowspan" => "Row span",
            "shadowrootclonable" => "Shadow root clonable",
            "shadowrootcustomelementregistry" =>
                "Shadow root custom-element registry",
            "shadowrootdelegatesfocus" => "Shadow root delegates focus",
            "shadowrootmode" => "Shadow root mode",
            "shadowrootserializable" => "Shadow root serializable",
            "shadowrootslotassignment" => "Shadow root slot assignment",
            "srcdoc" => "Inline HTML source",
            "srclang" => "Source language",
            "srcset" => "Source set",
            "tabindex" => "Tab index",
            "usemap" => "Image map",
            "vspace" => "Vertical spacing",
            "writingsuggestions" => "Writing suggestions",
            _ when normalized.StartsWith("aria-", StringComparison.Ordinal) =>
                "ARIA " + FormatFriendlyWords(normalized[5..]),
            _ => FormatFriendlyWords(normalized)
        };
    }

    private static string FormatFriendlyWords(string value) => string.Join(
        ' ',
        value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string Describe(string name) => name switch
    {
        "accept" => "Limits the file types a file picker should accept.",
        "accept-charset" => "Chooses character encodings for form submission.",
        "action" => "Sets the address that receives submitted form data.",
        "allow" => "Grants selected browser features to embedded content.",
        "alt" => "Provides a text alternative when visual content is unavailable.",
        "aria-label" => "Gives the element an accessible name when visible text does not.",
        "aria-labelledby" => "Uses one or more element IDs as the accessible label.",
        "aria-describedby" => "Uses one or more element IDs as the accessible description.",
        "async" => "Loads and runs the script without blocking document parsing.",
        "autocomplete" => "Controls browser-assisted completion for this field or form.",
        "autofocus" => "Requests focus when the page or dialog opens.",
        "autoplay" => "Requests playback as soon as the media can start.",
        "charset" => "Declares the document character encoding.",
        "checked" => "Starts a checkbox or radio control in its checked state.",
        "cite" => "Links to a source that explains or identifies the quotation or edit.",
        "class" => "Assigns reusable CSS class names.",
        "closedby" => "Chooses which user actions may close a dialog.",
        "cols" => "Sets the visible text columns, or legacy frame columns.",
        "colspan" => "Sets how many table columns this cell spans.",
        "content" => "Stores the machine-readable value for this metadata entry.",
        "controls" => "Shows the browser's playback controls.",
        "coords" => "Defines the coordinates of an image-map hotspot.",
        "crossorigin" => "Chooses how cross-origin credentials are sent.",
        "data" => "Sets the resource address loaded by an object element.",
        "datetime" => "Stores a machine-readable date or time.",
        "decoding" => "Hints whether image decoding should be synchronous or asynchronous.",
        "default" => "Marks this timed-text track as the default choice.",
        "defer" => "Runs an external script after document parsing finishes.",
        "dir" => "Sets the text direction.",
        "dirname" => "Submits the writing direction alongside this field's value.",
        "disabled" => "Makes the control unavailable and prevents user interaction.",
        "download" => "Suggests downloading the linked resource, optionally with a filename.",
        "enctype" => "Chooses how form data is encoded for submission.",
        "fetchpriority" => "Hints the relative fetch priority of this resource.",
        "for" => "Connects this element to another element by ID.",
        "form" => "Associates the control with a form by ID.",
        "formaction" => "Overrides the parent form's submission address for this control.",
        "formenctype" => "Overrides form-data encoding for this submit control.",
        "formmethod" => "Overrides the submission method for this submit control.",
        "formnovalidate" => "Skips form validation when this control submits the form.",
        "formtarget" => "Chooses where the submitted response opens.",
        "headers" => "Lists table-header IDs associated with this data cell.",
        "height" => "Sets the element's intrinsic height in CSS pixels.",
        "hidden" => "Hides the element, optionally until browser find reveals it.",
        "high" => "Sets the start of the meter's high-value range.",
        "href" => "Sets the linked destination.",
        "hreflang" => "Describes the language of the linked resource.",
        "http-equiv" => "Chooses a document-processing instruction represented by metadata.",
        "id" => "Assigns a unique page identifier used by links, labels, CSS, and code.",
        "integrity" => "Provides a hash used to verify a fetched resource.",
        "kind" => "Chooses the purpose of a timed-text track.",
        "label" => "Provides a user-facing label for an option group or track.",
        "list" => "Connects an input to a datalist by ID.",
        "loading" => "Chooses eager or lazy loading.",
        "loop" => "Repeats media playback when it reaches the end.",
        "low" => "Sets the end of the meter's low-value range.",
        "max" => "Sets the maximum permitted or measured value.",
        "maxlength" => "Sets the maximum number of text characters.",
        "media" => "Limits a resource or source to a media query.",
        "method" => "Chooses the HTTP method used for form submission.",
        "min" => "Sets the minimum permitted or measured value.",
        "minlength" => "Sets the minimum number of text characters.",
        "multiple" => "Allows more than one value, option, or file.",
        "muted" => "Starts media with audio muted.",
        "name" => "Assigns the control or element's submitted or referenced name.",
        "nomodule" => "Runs the script only in browsers without module support.",
        "nonce" => "Adds a Content Security Policy nonce supplied by the host.",
        "novalidate" => "Disables built-in validation for form submission.",
        "open" => "Starts the details disclosure or dialog in its open state.",
        "optimum" => "Sets the meter value considered ideal.",
        "pattern" => "Sets a regular-expression rule for the field's value.",
        "placeholder" => "Shows a short input hint when the field is empty.",
        "popover" => "Turns the element into a popover and chooses its behavior.",
        "popovertarget" => "Connects this control to a popover by ID.",
        "popovertargetaction" => "Chooses whether the control toggles, shows, or hides its popover.",
        "poster" => "Sets the image shown before video playback begins.",
        "preload" => "Hints how much media the browser should load in advance.",
        "readonly" => "Prevents editing while keeping the value usable and submittable.",
        "referrerpolicy" => "Controls referrer information sent while fetching or navigating.",
        "rel" => "Describes the relationship to the linked resource.",
        "required" => "Requires a value before the form can be submitted.",
        "reversed" => "Numbers an ordered list in descending order.",
        "rows" => "Sets the visible text rows, or legacy frame rows.",
        "rowspan" => "Sets how many table rows this cell spans.",
        "sandbox" => "Applies restrictions and selected permissions to an iframe.",
        "scope" => "Identifies which table cells a header describes.",
        "selected" => "Starts this option as the selected choice.",
        "shape" => "Chooses the geometry of an image-map hotspot.",
        "size" => "Sets the control's visible size or number of choices.",
        "sizes" => "Describes expected rendered image sizes for source selection.",
        "span" => "Sets how many table columns belong to this column definition.",
        "src" => "Sets the source resource address.",
        "srcdoc" => "Provides inline HTML for an iframe.",
        "srclang" => "Declares the language of a timed-text track.",
        "srcset" => "Lists responsive image-source candidates.",
        "step" => "Sets the permitted interval between numeric or date/time values.",
        "target" => "Chooses where a linked page or response opens.",
        "title" => "Adds advisory text, commonly shown as a tooltip.",
        "type" => "Chooses the element's specific behavior or resource type.",
        "value" => "Sets the submitted, machine-readable, or measured value.",
        "width" => "Sets the element's intrinsic width in CSS pixels.",
        "wrap" => "Chooses how textarea line wrapping is submitted.",
        _ => $"Sets {FriendlyName(name).ToLowerInvariant()} for this element."
    };
}

internal static class VisualPageHtmlComponentCatalog
{
    public static IReadOnlySet<string> ConformingTags { get; } =
        new HashSet<string>(
        [
            "a", "abbr", "address", "area", "article", "aside", "audio", "b",
            "base", "bdi", "bdo", "blockquote", "body", "br", "button",
            "canvas", "caption", "cite", "code", "col", "colgroup", "data",
            "datalist", "dd", "del", "details", "dfn", "dialog", "div", "dl",
            "dt", "em", "embed", "fieldset", "figcaption", "figure", "footer",
            "form", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header",
            "hgroup", "hr", "html", "i", "iframe", "img", "input", "ins",
            "kbd", "label", "legend", "li", "link", "main", "map", "mark",
            "menu", "meta", "meter", "nav", "noscript", "object", "ol",
            "optgroup", "option", "output", "p", "picture", "pre", "progress",
            "q", "rp", "rt", "ruby", "s", "samp", "script", "search",
            "section", "select", "selectedcontent", "slot", "small", "source",
            "span", "strong", "style", "sub", "summary", "sup", "table",
            "tbody", "td", "template", "textarea", "tfoot", "th", "thead",
            "time", "title", "tr", "track", "u", "ul", "var", "video", "wbr"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> InputTypes { get; } =
    [
        "button", "checkbox", "color", "date", "datetime-local", "email",
        "file", "hidden", "image", "month", "number", "password", "radio",
        "range", "reset", "search", "submit", "tel", "text", "time",
        "url", "week"
    ];

    private static readonly string[] InputAttributes =
    [
        "accept", "alpha", "alt", "autocomplete", "checked", "colorspace",
        "dirname", "disabled", "form", "formaction", "formenctype", "formmethod",
        "formnovalidate", "formtarget", "height", "list", "max", "maxlength",
        "min", "minlength", "multiple", "name", "pattern", "placeholder",
        "popovertarget", "popovertargetaction", "readonly", "required", "size",
        "src", "step", "type", "value", "width", "aria-label"
    ];

    public static IReadOnlyList<VisualPageComponentDescriptor> CreateDescriptors()
    {
        var descriptors = new List<VisualPageComponentDescriptor>
        {
            Html("html", "HTML document root", "HTML · Document", "Root element of a complete HTML document.", structural: true, documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("head", "Document head", "HTML · Document", "Container for document metadata.", structural: true, documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("body", "Document body", "HTML · Document", "Body of a complete HTML document.", structural: true, documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("base", "Base URL", "HTML · Metadata", "Document base URL and default navigation target.", isVoid: true, attributes: ["href", "target"], documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("link", "Metadata link", "HTML · Metadata", "External metadata resource such as a stylesheet or icon.", isVoid: true, attributes: ["href", "crossorigin", "rel", "as", "media", "hreflang", "type", "sizes", "imagesrcset", "imagesizes", "referrerpolicy", "integrity", "blocking", "color", "disabled", "fetchpriority"], documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("meta", "Metadata", "HTML · Metadata", "Machine-readable document metadata.", isVoid: true, attributes: ["name", "http-equiv", "content", "charset", "media"], documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("title", "Document title", "HTML · Metadata", "Title of a complete HTML document.", defaultText: "Document title", documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("style", "Embedded style", "HTML · Metadata", "Embedded CSS. Prefer the project CSS pane for reusable styles.", attributes: ["media", "blocking", "nonce"], documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder, classification: VisualPageCodeClassification.CustomCss),
            Html("script", "Embedded script", "HTML · Scripting", "Embedded or external script. Custom code runs only in compiled preview.", attributes: ["src", "type", "nomodule", "async", "defer", "crossorigin", "integrity", "referrerpolicy", "blocking", "fetchpriority", "nonce"], canvas: VisualPageCanvasRepresentation.Placeholder, classification: VisualPageCodeClassification.CustomJavaScript),
            Html("noscript", "No-script fallback", "HTML · Scripting", "Fallback content shown when scripting is unavailable.", structural: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("template", "HTML template", "HTML · Scripting", "Inert template content for later instantiation.", structural: true, attributes: ["for", "shadowrootmode", "shadowrootdelegatesfocus", "shadowrootserializable", "shadowrootslotassignment", "shadowrootclonable", "shadowrootcustomelementregistry"], canvas: VisualPageCanvasRepresentation.Placeholder),

            Html("main", "Main content", "HTML · Sections", "Dominant content of the document.", structural: true),
            Html("article", "Article", "HTML · Sections", "Self-contained reusable or syndicatable composition.", structural: true),
            Html("aside", "Aside", "HTML · Sections", "Tangential or sidebar content.", structural: true),
            Html("header", "Header", "HTML · Sections", "Introductory or navigational content for a page or section.", structural: true),
            Html("footer", "Footer", "HTML · Sections", "Footer for a page or section.", structural: true),
            Html("address", "Contact address", "HTML · Sections", "Contact information for a page or article.", structural: true),
            Html("hgroup", "Heading group", "HTML · Sections", "Heading and related subheading or tagline content.", structural: true),
            Html("hr", "Thematic break", "HTML · Sections", "Thematic break between paragraph-level topics.", isVoid: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("search", "Search region", "HTML · Sections", "Container for search or filtering controls.", structural: true),

            Html("abbr", "Abbreviation", "HTML · Text", "Abbreviation or acronym.", attributes: ["title"], defaultText: "Abbreviation"),
            Html("b", "Bring attention", "HTML · Text", "Keywords or text brought to attention without added importance.", defaultText: "Text"),
            Html("bdi", "Direction isolation", "HTML · Text", "Isolates text directionality from surrounding content.", defaultText: "Isolated text"),
            Html("bdo", "Direction override", "HTML · Text", "Overrides text directionality.", attributes: ["dir"], defaultText: "Directional text"),
            Html("br", "Line break", "HTML · Text", "Line break in text.", isVoid: true, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("cite", "Work citation", "HTML · Text", "Title of a cited creative work.", defaultText: "Work title"),
            Html("code", "Inline code", "HTML · Text", "Fragment of computer code.", defaultText: "code"),
            Html("data", "Machine-readable data", "HTML · Text", "Human text paired with a machine-readable value.", attributes: ["value"], defaultText: "Data"),
            Html("del", "Deleted text", "HTML · Text", "Content removed from the document.", attributes: ["cite", "datetime"], defaultText: "Deleted text"),
            Html("dfn", "Definition", "HTML · Text", "Defining instance of a term.", attributes: ["title"], defaultText: "Term"),
            Html("em", "Emphasis", "HTML · Text", "Stress emphasis.", defaultText: "Emphasized text"),
            Html("i", "Alternate voice", "HTML · Text", "Text in an alternate voice or mood.", defaultText: "Alternate voice"),
            Html("ins", "Inserted text", "HTML · Text", "Content added to the document.", attributes: ["cite", "datetime"], defaultText: "Inserted text"),
            Html("kbd", "Keyboard input", "HTML · Text", "User input such as a keyboard command.", defaultText: "Ctrl+K"),
            Html("mark", "Highlight", "HTML · Text", "Text highlighted for relevance.", defaultText: "Highlighted text"),
            Html("q", "Inline quotation", "HTML · Text", "Short inline quotation.", attributes: ["cite"], defaultText: "Quotation"),
            Html("ruby", "Ruby annotation", "HTML · Text", "Ruby annotations for pronunciation or meaning.", structural: true),
            Html("rt", "Ruby text", "HTML · Text", "Ruby annotation text.", defaultText: "Annotation"),
            Html("rp", "Ruby fallback parenthesis", "HTML · Text", "Fallback parentheses around ruby text.", defaultText: "("),
            Html("s", "Inaccurate text", "HTML · Text", "Text no longer accurate or relevant.", defaultText: "Inaccurate text"),
            Html("samp", "Sample output", "HTML · Text", "Sample output from a program.", defaultText: "Program output"),
            Html("small", "Side comment", "HTML · Text", "Side comments such as legal text or attribution.", defaultText: "Side comment"),
            Html("strong", "Strong importance", "HTML · Text", "Text with strong importance or urgency.", defaultText: "Important text"),
            Html("sub", "Subscript", "HTML · Text", "Subscript text.", defaultText: "sub"),
            Html("summary", "Disclosure summary", "HTML · Interactive", "Caption for a details disclosure.", defaultText: "Details"),
            Html("sup", "Superscript", "HTML · Text", "Superscript text.", defaultText: "sup"),
            Html("time", "Date or time", "HTML · Text", "Human-readable date or time with a machine-readable value.", attributes: ["datetime"], defaultText: "Date or time"),
            Html("u", "Annotation", "HTML · Text", "Unarticulated text annotation.", defaultText: "Annotated text"),
            Html("var", "Variable", "HTML · Text", "Variable name in a technical or mathematical expression.", defaultText: "variable"),
            Html("wbr", "Word-break opportunity", "HTML · Text", "Optional line-break opportunity.", isVoid: true, canvas: VisualPageCanvasRepresentation.Placeholder),

            Html("area", "Image-map area", "HTML · Media", "Hyperlink region inside an image map.", isVoid: true, attributes: ["alt", "coords", "shape", "href", "target", "download", "ping", "rel", "referrerpolicy"], canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("canvas", "Bitmap canvas", "HTML · Media", "Scriptable bitmap drawing surface.", structural: true, attributes: ["width", "height"], defaultText: "Canvas fallback"),
            Html("embed", "Embedded resource", "HTML · Media", "Embedded external resource.", isVoid: true, attributes: ["src", "type", "width", "height"], canvas: VisualPageCanvasRepresentation.Placeholder, classification: VisualPageCodeClassification.CustomComponent),
            Html("iframe", "Inline frame", "HTML · Media", "Nested browsing context. It remains inert on the design canvas.", attributes: ["src", "srcdoc", "name", "sandbox", "allow", "allowfullscreen", "width", "height", "referrerpolicy", "loading", "aria-label"], allowsChildren: false, accessibleLabel: true, canvas: VisualPageCanvasRepresentation.Placeholder, classification: VisualPageCodeClassification.CustomComponent),
            Html("map", "Image map", "HTML · Media", "Image map containing clickable areas.", structural: true, attributes: ["name"]),
            Html("object", "Embedded object", "HTML · Media", "Image, nested document, or external object with fallback content.", structural: true, attributes: ["data", "type", "name", "form", "width", "height"], canvas: VisualPageCanvasRepresentation.Placeholder, classification: VisualPageCodeClassification.CustomComponent),
            Html("source", "Media source", "HTML · Media", "Alternative image, audio, or video source.", isVoid: true, attributes: ["type", "media", "src", "srcset", "sizes", "width", "height"], canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("track", "Timed text track", "HTML · Media", "Captions, subtitles, or metadata for audio and video.", isVoid: true, attributes: ["default", "kind", "label", "src", "srclang"], canvas: VisualPageCanvasRepresentation.Placeholder),

            Html("dl", "Description list", "HTML · Lists", "Association list of terms and descriptions.", structural: true),
            Html("dt", "Description term", "HTML · Lists", "Term in a description list.", defaultText: "Term"),
            Html("dd", "Description details", "HTML · Lists", "Description corresponding to a term.", structural: true, defaultText: "Description"),
            Html("menu", "Command menu", "HTML · Lists", "List of commands represented by list items.", structural: true),

            Html("form", "HTML form", "HTML · Forms", "Native user-submittable form. Use Blazor Form for typed validation.", structural: true, attributes: ["accept-charset", "action", "autocomplete", "enctype", "method", "name", "novalidate", "rel", "target"], accessibleLabel: true),
            Html("datalist", "Data list", "HTML · Forms", "Suggested options for an input control.", structural: true),
            Html("details", "Disclosure", "HTML · Interactive", "Interactive disclosure widget.", structural: true, attributes: ["name", "open"]),
            Html("dialog", "Dialog", "HTML · Interactive", "Dialog box or window.", structural: true, attributes: ["open", "closedby"], accessibleLabel: true),
            Html("fieldset", "Field set", "HTML · Forms", "Group of related form controls.", structural: true, attributes: ["disabled", "form", "name"]),
            Html("legend", "Field-set legend", "HTML · Forms", "Caption for a field set.", defaultText: "Form group"),
            Html("input", "Input", "HTML · Forms", "Native input control with a selectable type.", isVoid: true, attributes: InputAttributes, defaults: new Dictionary<string, string> { ["type"] = "text", ["aria-label"] = "Text input" }, accessibleLabel: true, importPriority: 100),
            Html("optgroup", "Option group", "HTML · Forms", "Labeled group of select options.", structural: true, attributes: ["disabled", "label"]),
            Html("output", "Output", "HTML · Forms", "Calculated output value.", attributes: ["for", "form", "name"], defaultText: "Output"),
            Html("progress", "Progress", "HTML · Forms", "Progress indicator.", attributes: ["value", "max"], defaults: new Dictionary<string, string> { ["max"] = "100", ["value"] = "0" }, defaultText: "0%"),
            Html("meter", "Meter", "HTML · Forms", "Gauge within a known range.", attributes: ["value", "min", "max", "low", "high", "optimum"], defaults: new Dictionary<string, string> { ["min"] = "0", ["max"] = "100", ["value"] = "50" }, defaultText: "50%"),
            Html("select", "HTML select", "HTML · Forms", "Native list box or combo-box control.", structural: true, attributes: ["autocomplete", "disabled", "form", "multiple", "name", "required", "size", "aria-label"], accessibleLabel: true),
            Html("selectedcontent", "Selected content", "HTML · Forms", "Mirrors the selected option in a customizable select button.", allowsChildren: false, canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("textarea", "Text area", "HTML · Forms", "Native multiline text control.", attributes: ["autocomplete", "cols", "dirname", "disabled", "form", "maxlength", "minlength", "name", "placeholder", "readonly", "required", "rows", "wrap", "aria-label"], accessibleLabel: true, defaultText: "Text area"),

            Html("caption", "Table caption", "HTML · Tables", "Caption for a table.", defaultText: "Table caption"),
            Html("col", "Table column", "HTML · Tables", "Column definition in a column group.", isVoid: true, attributes: ["span"], canvas: VisualPageCanvasRepresentation.Placeholder),
            Html("colgroup", "Table column group", "HTML · Tables", "Group of table columns.", structural: true, attributes: ["span"]),
            Html("table", "Table", "HTML · Tables", "Tabular data container.", structural: true),
            Html("tbody", "Table body", "HTML · Tables", "Group of body rows.", structural: true),
            Html("td", "Table cell", "HTML · Tables", "Data cell in a table row.", structural: true, attributes: ["colspan", "rowspan", "headers"], defaultText: "Cell"),
            Html("tfoot", "Table footer", "HTML · Tables", "Group of footer rows.", structural: true),
            Html("th", "Table header cell", "HTML · Tables", "Header cell in a table row.", structural: true, attributes: ["colspan", "rowspan", "headers", "scope", "abbr"], defaultText: "Heading"),
            Html("thead", "Table head", "HTML · Tables", "Group of heading rows.", structural: true),
            Html("tr", "Table row", "HTML · Tables", "Row of table cells.", structural: true),

            Html("slot", "Web-component slot", "HTML · Web components", "Named insertion point in a shadow tree.", structural: true, attributes: ["name"])
        };

        foreach (var type in InputTypes)
        {
            var inputDisplayName = InputTypeDisplayName(type);
            descriptors.Add(Html(
                "input",
                inputDisplayName,
                "HTML · Input types",
                $"Native {inputDisplayName.ToLowerInvariant()} represented by <input type=\"{type}\">.",
                isVoid: true,
                attributes: InputAttributesFor(type),
                defaults: InputDefaultsFor(type),
                accessibleLabel: type is not "hidden",
                requiresAlt: type is "image",
                descriptorId: $"html.input.{type}"));
        }

        descriptors.AddRange(CreateObsoleteDescriptors());

        return descriptors;
    }

    private static string InputTypeDisplayName(string type) => type switch
    {
        "button" => "Button input",
        "checkbox" => "Checkbox",
        "color" => "Color picker",
        "date" => "Date input",
        "datetime-local" => "Local date and time input",
        "email" => "Email input",
        "file" => "File picker",
        "hidden" => "Hidden value",
        "image" => "Image submit button",
        "month" => "Month input",
        "number" => "Number input",
        "password" => "Password input",
        "radio" => "Radio button",
        "range" => "Range slider",
        "reset" => "Reset button",
        "search" => "Search input",
        "submit" => "Submit button",
        "tel" => "Telephone input",
        "text" => "Text input",
        "time" => "Time input",
        "url" => "URL input",
        "week" => "Week input",
        _ => $"{type.Replace('-', ' ')} input"
    };

    private static IReadOnlyList<VisualPageComponentDescriptor>
        CreateObsoleteDescriptors() =>
    [
        Html("applet", "Legacy applet", "HTML · Legacy", "Obsolete embedded Java applet; migrate to object or embed.", structural: true, attributes: ["align", "alt", "archive", "code", "codebase", "height", "hspace", "name", "object", "vspace", "width"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("acronym", "Legacy acronym", "HTML · Legacy", "Obsolete acronym annotation; migrate to abbr.", defaultText: "Acronym", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("bgsound", "Legacy background sound", "HTML · Legacy", "Obsolete background audio; migrate to audio.", isVoid: true, attributes: ["src", "loop", "balance", "volume"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("dir", "Legacy directory list", "HTML · Legacy", "Obsolete directory list; migrate to ul.", structural: true, attributes: ["compact"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("frame", "Legacy frame", "HTML · Legacy", "Obsolete frame; migrate to iframe and CSS.", isVoid: true, attributes: ["src", "name", "scrolling", "noresize", "frameborder", "marginheight", "marginwidth"], documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("frameset", "Legacy frame set", "HTML · Legacy", "Obsolete frame document layout; migrate to normal responsive layout.", structural: true, attributes: ["cols", "rows"], documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("noframes", "Legacy no-frames fallback", "HTML · Legacy", "Obsolete fallback for frame documents.", structural: true, documentOnly: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("isindex", "Legacy index prompt", "HTML · Legacy", "Obsolete single-field search prompt; migrate to form and input.", isVoid: true, attributes: ["prompt", "action"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("keygen", "Legacy key generator", "HTML · Legacy", "Obsolete key-pair generator.", isVoid: true, attributes: ["autofocus", "challenge", "disabled", "form", "keytype", "name"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("listing", "Legacy listing", "HTML · Legacy", "Obsolete preformatted listing; migrate to pre and code.", defaultText: "Legacy listing", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("menuitem", "Legacy menu item", "HTML · Legacy", "Obsolete context-menu command.", attributes: ["type", "label", "icon", "disabled", "checked", "radiogroup", "default"], allowsChildren: false, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("nextid", "Legacy next ID", "HTML · Legacy", "Obsolete identifier hint; use stable IDs.", isVoid: true, attributes: ["n"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("noembed", "Legacy no-embed fallback", "HTML · Legacy", "Obsolete embed fallback; migrate to object fallback content.", structural: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("param", "Legacy object parameter", "HTML · Legacy", "Obsolete object parameter; migrate to object data.", isVoid: true, attributes: ["name", "value"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("plaintext", "Legacy plain text", "HTML · Legacy", "Obsolete raw-text remainder; migrate to a text/plain response.", defaultText: "Legacy plain text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("rb", "Legacy ruby base", "HTML · Legacy", "Obsolete ruby base wrapper; use direct ruby content.", structural: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("rtc", "Legacy ruby text container", "HTML · Legacy", "Obsolete ruby annotation group; use nested ruby content.", structural: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("strike", "Legacy strike-through", "HTML · Legacy", "Obsolete strike-through text; migrate to del or s.", defaultText: "Struck text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("xmp", "Legacy example text", "HTML · Legacy", "Obsolete raw example text; migrate to pre and code.", defaultText: "Legacy example", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("basefont", "Legacy base font", "HTML · Legacy", "Obsolete document font defaults; migrate to CSS.", isVoid: true, attributes: ["color", "face", "size"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("big", "Legacy large text", "HTML · Legacy", "Obsolete larger text presentation; migrate to semantic markup and CSS.", defaultText: "Large text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("blink", "Legacy blinking text", "HTML · Legacy", "Obsolete blinking text presentation.", defaultText: "Blinking text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("center", "Legacy centered content", "HTML · Legacy", "Obsolete centered layout; migrate to CSS alignment.", structural: true, canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("font", "Legacy font", "HTML · Legacy", "Obsolete font presentation; migrate to CSS.", attributes: ["color", "face", "size"], defaultText: "Styled text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("marquee", "Legacy marquee", "HTML · Legacy", "Obsolete scrolling content; migrate to accessible motion and CSS.", structural: true, attributes: ["behavior", "bgcolor", "direction", "height", "hspace", "loop", "scrollamount", "scrolldelay", "truespeed", "vspace", "width"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("multicol", "Legacy multi-column", "HTML · Legacy", "Obsolete multi-column layout; migrate to CSS columns or grid.", structural: true, attributes: ["cols", "gutter", "width"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("nobr", "Legacy no-break", "HTML · Legacy", "Obsolete no-wrap presentation; migrate to white-space CSS.", defaultText: "Non-breaking text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("spacer", "Legacy spacer", "HTML · Legacy", "Obsolete spacing element; migrate to CSS margin, padding, or gap.", isVoid: true, attributes: ["type", "size", "width", "height", "align"], canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true),
        Html("tt", "Legacy teletype text", "HTML · Legacy", "Obsolete monospace presentation; migrate to kbd, var, code, or samp.", defaultText: "Monospace text", canvas: VisualPageCanvasRepresentation.Placeholder, obsolete: true)
    ];

    private static VisualPageComponentDescriptor Html(
        string tag,
        string displayName,
        string category,
        string description,
        bool structural = false,
        bool isVoid = false,
        bool allowsChildren = true,
        IReadOnlyList<string>? attributes = null,
        IReadOnlyDictionary<string, string>? defaults = null,
        bool accessibleLabel = false,
        bool requiresAlt = false,
        bool documentOnly = false,
        int importPriority = 10,
        string? descriptorId = null,
        string defaultText = "",
        VisualPageCanvasRepresentation canvas = VisualPageCanvasRepresentation.Native,
        bool obsolete = false,
        VisualPageCodeClassification classification = VisualPageCodeClassification.Declarative)
    {
        var id = descriptorId ?? $"html.{tag}";
        var displayCategory = FriendlyCategory(category);
        var allowedAttributes = attributes ?? [];
        var effectiveClassification = obsolete
            ? VisualPageCodeClassification.LegacyHtml
            : classification;
        var effectiveCanvas =
            VisualPageElementAuthoringCatalog.ResolveCanvasRepresentation(
                id,
                tag,
                canvas);
        return new VisualPageComponentDescriptor
        {
            Id = id,
            DisplayName = displayName,
            Category = displayCategory,
            MarkupName = tag,
            Description = description,
            ContentModel = isVoid || !allowsChildren ? "Empty" : "HTML content",
            Keywords = [tag, "HTML"],
            IsVoid = isVoid,
            AllowsChildren = allowsChildren && !isVoid,
            IsStructural = structural,
            RequiresAccessibleLabel = accessibleLabel,
            RequiresAltText = requiresAlt,
            IsStandardHtmlElement = !obsolete,
            IsObsolete = obsolete,
            IsDocumentOnly = documentOnly,
            HtmlImportPriority = importPriority,
            CanvasRepresentation = effectiveCanvas,
            AllowedAttributes = allowedAttributes,
            AttributeDefinitions = VisualPageAttributeCatalog.ResolveMany(
                allowedAttributes,
                tag,
                id),
            DefaultAttributes = defaults ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Classification = effectiveClassification,
            DefaultText = defaultText,
            Authoring = VisualPageElementAuthoringCatalog.Create(
                id,
                tag,
                displayCategory,
                structural,
                isVoid,
                allowsChildren && !isVoid,
                !obsolete,
                obsolete,
                documentOnly,
                defaultText,
                allowedAttributes,
                effectiveCanvas,
                effectiveClassification)
        };
    }

    private static string FriendlyCategory(string category) => category switch
    {
        "HTML · Document" or "HTML · Metadata" => "Document setup",
        "HTML · Scripting" => "Scripts & templates",
        "HTML · Sections" => "Page sections",
        "HTML · Text" => "Text",
        "HTML · Interactive" => "Interactive",
        "HTML · Media" => "Media",
        "HTML · Lists" => "Lists",
        "HTML · Forms" => "Forms",
        "HTML · Input types" => "Input controls",
        "HTML · Tables" => "Tables",
        "HTML · Web components" => "Web components",
        "HTML · Legacy" => "Legacy HTML",
        _ => category
    };

    private static IReadOnlyList<string> InputAttributesFor(string type)
    {
        var attributes = new List<string> { "form", "name", "type" };
        if (type != "hidden")
        {
            attributes.AddRange(["aria-label", "disabled"]);
        }
        attributes.AddRange(type switch
        {
            "button" => ["value", "popovertarget", "popovertargetaction"],
            "checkbox" or "radio" => ["checked", "required", "value"],
            "color" => ["alpha", "autocomplete", "colorspace", "list", "value"],
            "date" or "datetime-local" or "month" or "time" or "week" =>
                ["autocomplete", "list", "max", "min", "readonly", "required", "step", "value"],
            "email" =>
                ["autocomplete", "dirname", "list", "maxlength", "minlength", "multiple", "pattern", "placeholder", "readonly", "required", "size", "value"],
            "file" => ["accept", "multiple", "required"],
            "hidden" => ["dirname", "value"],
            "image" =>
                ["alt", "formaction", "formenctype", "formmethod", "formnovalidate", "formtarget", "height", "src", "width"],
            "number" =>
                ["autocomplete", "list", "max", "min", "placeholder", "readonly", "required", "step", "value"],
            "password" =>
                ["autocomplete", "dirname", "maxlength", "minlength", "pattern", "placeholder", "readonly", "required", "size", "value"],
            "range" => ["autocomplete", "list", "max", "min", "step", "value"],
            "reset" => ["value"],
            "search" or "tel" or "text" or "url" =>
                ["autocomplete", "dirname", "list", "maxlength", "minlength", "pattern", "placeholder", "readonly", "required", "size", "value"],
            "submit" =>
                ["formaction", "formenctype", "formmethod", "formnovalidate", "formtarget", "popovertarget", "popovertargetaction", "value"],
            _ => []
        });
        return attributes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> InputDefaultsFor(
        string type)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = type
        };
        if (type != "hidden")
        {
            defaults["aria-label"] = $"{InputTypeLabel(type)} input";
        }
        switch (type)
        {
            case "button":
                defaults["value"] = "Button";
                break;
            case "color":
                defaults["value"] = "#000000";
                break;
            case "image":
                defaults["alt"] = "Image button";
                defaults["src"] = "assets/placeholder.svg";
                break;
            case "number":
                defaults["value"] = "0";
                break;
            case "range":
                defaults["min"] = "0";
                defaults["max"] = "100";
                defaults["value"] = "50";
                break;
            case "reset":
                defaults["value"] = "Reset";
                break;
            case "submit":
                defaults["value"] = "Submit";
                break;
        }
        return defaults;
    }

    private static string InputTypeLabel(string type) => type switch
    {
        "datetime-local" => "Local date and time",
        "tel" => "Telephone",
        "url" => "Web address",
        _ => char.ToUpperInvariant(type[0]) + type[1..]
    };
}
