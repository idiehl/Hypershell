using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Small application patterns, grouped by the job they perform rather than their visual treatment.</summary>
public static class StudioApplicationModuleTemplates
{
    public static IReadOnlyList<VisualPageTemplateDocument> CreateTemplates() =>
    [
        MTemplate("record-breadcrumb", "Record breadcrumb", "Navigation",
            "A compact location trail for a record inside a collection.",
            MNode("core.nav", "Record location", attributes: MAttributes(("aria-label", "Record breadcrumb")), layout: MFlex(), children:
            [MLink("Records", "#records"), MText("/"), MText("Record 024", true)])),
        MTemplate("reading-pagination", "Previous and next record", "Navigation",
            "Two directional links with the neighboring record titles.",
            MNode("core.nav", "Adjacent records", attributes: MAttributes(("aria-label", "Previous and next record")), layout: MFlex("space-between"), children:
            [MCol(MLink("← Previous record", "#previous"), MText("Understanding the system")), MCol(MLink("Next record →", "#next"), MText("Putting it into practice"))])),
        MTemplate("anchor-shortcuts", "In-page jump links", "Navigation",
            "Named anchor shortcuts for a long document or application record.",
            MNode("core.nav", "In-page shortcuts", attributes: MAttributes(("aria-label", "On this page")), layout: MFlex(), children:
            [MText("Jump to", true), MLink("Details", "#details"), MLink("Activity", "#activity"), MLink("Files", "#files")])),
        MTemplate("return-link", "Return link with context", "Navigation",
            "A return destination with a short explanation of the parent view.",
            MCol(MLink("← Back to collection", "#collection"), MText("Return to all records and saved filters."))),
        MTemplate("resource-download", "Resource download link", "Navigation",
            "A descriptive downloadable-resource link and file metadata.",
            MCard(MLink("Download project brief", "#project-brief"), MText("PDF · 1.4 MB · Version 2"))),

        MTemplate("captioned-image", "Captioned landscape image", "Media",
            "A full-width image with editable alternative text and caption.",
            MNode("core.figure", "Landscape figure", styles: MStyles(("margin", "0")), children:
            [MImage("Landscape image", "A replaceable landscape image", "16 / 9"), MNode("core.caption", "Image caption", "A clear caption identifying the scene and its source.", styles: MMuted())])),
        MTemplate("video-caption", "Video player and caption", "Media",
            "A native video player with a title and transcript destination; attach your video source.",
            MCol(MText("Process walkthrough", true), MNode("core.video", "Video player", attributes: MAttributes(("controls", "true"), ("preload", "none"), ("aria-label", "Process walkthrough video")), styles: MStyles(("width", "100%"), ("aspect-ratio", "16 / 9"), ("background", "#152638"), ("border-radius", "0.6rem"))), MLink("Read the transcript", "#transcript"))),
        MTemplate("audio-caption", "Audio player and transcript", "Media",
            "A native audio player paired with episode context and a transcript link; attach an audio source.",
            MCard(MText("Field recording · 04:32", true), MNode("core.audio", "Audio recording", attributes: MAttributes(("controls", "true"), ("preload", "none"), ("aria-label", "Field recording audio")), styles: MStyles(("width", "100%"))), MLink("Open transcript", "#audio-transcript"))),
        MTemplate("attachment-row", "File attachment row", "Media",
            "One attached file with its format, size, and open destination.",
            MCard(MRow(MCol(MText("Research-notes.pdf", true), MText("Document · 240 KB")), MLink("Open file →", "#attachment")))),
        MTemplate("image-pair", "Side-by-side image pair", "Media",
            "Two labeled images for a reference/result or before/after comparison.",
            MNode("core.container", "Image comparison", layout: MGrid("repeat(auto-fit, minmax(min(100%, 9rem), 1fr))"), children:
            [MCol(MImage("Reference image", "Reference state", "4 / 3"), MText("Reference", true)), MCol(MImage("Result image", "Result state", "4 / 3"), MText("Result", true))])),
        MTemplate("portrait-figure", "Portrait image and credit", "Media",
            "A portrait-oriented media frame with a visible creator credit.",
            MNode("core.figure", "Portrait figure", styles: MStyles(("margin", "0"), ("max-width", "22rem")), children:
            [MImage("Portrait image", "A replaceable portrait image", "3 / 4"), MNode("core.caption", "Creator credit", "Image title · Creator name", styles: MMuted())])),

        MTemplate("split-detail", "Two-column detail pair", "Grid",
            "A compact responsive pair for related context and supporting detail.",
            MNode("core.container", "Related details", layout: MGrid("repeat(auto-fit, minmax(min(100%, 10rem), 1fr))"), children:
            [MCard(MText("Overview", true), MText("A concise explanation of the item.")), MCard(MText("Key detail", true), MText("One useful fact that supports the overview."))])),
        MTemplate("paired-metrics", "Paired comparison metrics", "Grid",
            "Two equally weighted values with distinct labels for a compact comparison.",
            MNode("core.container", "Paired metrics", layout: MGrid("repeat(auto-fit, minmax(min(100%, 8rem), 1fr))"), children:
            [MCard(MText("Current period"), MValue("128")), MCard(MText("Previous period"), MValue("112"))])),
        MTemplate("label-value-grid", "Label and value grid", "Grid",
            "A narrow-label, flexible-value arrangement for one metadata record.",
            MNode("core.container", "Record metadata", layout: MGrid("minmax(0, 1fr) minmax(0, 2fr)"), children:
            [MText("Reference", true), MText("REC-024"), MText("Owner", true), MText("Design team")])),
        MTemplate("paired-actions", "Equal action pair", "Grid",
            "Two related actions with matching visual weight and responsive wrapping.",
            MRow(MButton("Save draft"), MButton("Preview changes"))),

        MTemplate("metric-value", "Metric with reporting period", "Data",
            "A labeled primary metric with a precise reporting interval.",
            MCard(MText("Completed records", true), MValue("248"), MText("Sample data · Current month"))),
        MTemplate("metric-change", "Metric with period change", "Data",
            "A value and text-labeled change relative to an explicit comparison period.",
            MCard(MText("Weekly completions", true), MRow(MValue("84"), MBadge("↑ 12%")), MText("Sample data · Compared with previous week"))),
        MTemplate("key-value", "Single record property", "Data",
            "One readable property label and value, suitable for a record detail list.",
            MRow(MText("Last reviewed", true), MText("24 October 2026"))),
        MTemplate("record-summary", "Compact record summary", "Data",
            "Record identity, owner, and status in one reusable summary.",
            MCard(MRow(MText("REC-024 · Launch checklist", true), MBadge("In review")), MText("Owner: Operations · Updated 2 hours ago"))),
        MTemplate("sample-data-row", "Three-field data row", "Data",
            "A responsive labeled row for a small record or table alternative.",
            MNode("core.container", "Dataset record", layout: MGrid("repeat(auto-fit, minmax(min(100%, 7rem), 1fr))"), children:
            [MCol(MText("Region", true), MText("North")), MCol(MText("Records", true), MText("128")), MCol(MText("Status", true), MText("Reviewed"))])),
        MTemplate("dataset-reference", "Dataset source reference", "Data",
            "A dataset link with format, refresh date, and descriptive source information.",
            MCard(MLink("Quarterly observations dataset", "#dataset"), MText("CSV · 640 rows · Updated 24 October 2026"), MText("Source: Example research collection"))),

        MTemplate("category-bars", "Category comparison bars", "Graphs",
            "Two labeled horizontal bars with visible sample values and a shared scale.",
            MCard(MText("Sample category comparison · Scale 0–100", true), MBar("Completed", 72), MBar("In progress", 28))),
        MTemplate("spark-columns", "Seven-period spark columns", "Graphs",
            "A compact seven-period trend with its sample values available as text.",
            MCard(MText("Weekly volume", true), MNode("core.container", "Sample weekly columns", attributes: MAttributes(("role", "img"), ("aria-label", "Sample volumes: 24, 38, 31, 52, 45, 62, 74")), styles: MStyles(("height", "5rem"), ("align-items", "end")), layout: MGrid("repeat(7, minmax(0, 1fr))"), children: new[] { 24, 38, 31, 52, 45, 62, 74 }.Select(MColumn)), MText("Sample values: 24 · 38 · 31 · 52 · 45 · 62 · 74"))),
        MTemplate("goal-progress", "Goal progress meter", "Graphs",
            "An accessible native meter plus a visible numerator and target.",
            MCard(MText("Review target", true), MNode("html.meter", "Review progress meter", attributes: MAttributes(("min", "0"), ("max", "100"), ("value", "68"), ("aria-label", "68 of 100 records reviewed")), styles: MStyles(("width", "100%"), ("height", "1.4rem"))), MText("Sample data · 68 of 100 records reviewed"))),
        MTemplate("share-ring", "Share ring with legend", "Graphs",
            "A two-part sample proportion ring with an explicit text legend.",
            MCard(MRow(MNode("core.container", "Sample share ring", attributes: MAttributes(("role", "img"), ("aria-label", "Sample allocation: research 65 percent, production 35 percent")), styles: MStyles(("width", "5rem"), ("height", "5rem"), ("flex-shrink", "0"), ("border-radius", "50%"), ("background", "conic-gradient(#356a9d 0 65%, #dbe5ef 65% 100%)")), children: [MNode("core.container", "Ring center", styles: MStyles(("width", "3rem"), ("height", "3rem"), ("margin", "1rem"), ("border-radius", "50%"), ("background", "#ffffff")))]), MCol(MText("Sample allocation", true), MText("Research · 65%"), MText("Production · 35%"))))),
        MTemplate("interval-range", "Range and midpoint graphic", "Graphs",
            "A compact interval graphic with its lower bound, midpoint, and upper bound in text.",
            MCard(MText("Sample observed range", true), MNode("core.container", "Interval track", attributes: MAttributes(("role", "img"), ("aria-label", "Range from 20 to 80, midpoint 50")), styles: MStyles(("height", "0.65rem"), ("border-radius", "999px"), ("background", "linear-gradient(to right, #e3eaf1 0 20%, #356a9d 20% 49%, #162d43 49% 51%, #356a9d 51% 80%, #e3eaf1 80% 100%)"))), MText("Lower 20 · Midpoint 50 · Upper 80"))),

        MTemplate("email-field", "Email field with guidance", "Forms",
            "A labeled native email input with a linked help description.",
            MField("Email address", "module-email", "html.input.email", "name@example.com", "Use an address you check regularly.")),
        MTemplate("search-field", "Labeled search field", "Forms",
            "A native search input with a visible label and descriptive placeholder.",
            MField("Search records", "module-search", "html.input.search", "Title, reference, or keyword", "Search within this collection.")),
        MTemplate("quantity-field", "Quantity input with bounds", "Forms",
            "A labeled native number field with editable minimum, maximum, and step.",
            MCol(MNode("core.label", "Quantity label", "Quantity", attributes: MAttributes(("for", "module-quantity"))), MNode("html.input.number", "Quantity input", attributes: MAttributes(("id", "module-quantity"), ("name", "quantity"), ("value", "1"), ("min", "1"), ("max", "99"), ("step", "1"), ("aria-label", "Quantity")), styles: MInputStyles()))),
        MTemplate("sort-selector", "Sort order selector", "Forms",
            "A labeled native select control for three common sorting choices.",
            MCol(MNode("core.label", "Sort label", "Sort records", attributes: MAttributes(("for", "module-sort"))), MNode("html.select", "Sort selector", attributes: MAttributes(("id", "module-sort"), ("name", "sort"), ("aria-label", "Sort records")), styles: MInputStyles(), children:
            [MNode("core.option", "Recently updated option", "Recently updated", attributes: MAttributes(("value", "updated"))), MNode("core.option", "Name option", "Name A–Z", attributes: MAttributes(("value", "name"))), MNode("core.option", "Created option", "Date created", attributes: MAttributes(("value", "created")))]))),
        MTemplate("consent-checkbox", "Consent checkbox and policy", "Forms",
            "A native checkbox with a visible consent label and policy destination.",
            MCol(MRow(MNode("html.input.checkbox", "Consent checkbox", attributes: MAttributes(("id", "module-consent"), ("name", "consent"), ("aria-label", "I agree to the usage policy"))), MNode("core.label", "Consent label", "I agree to the usage policy", attributes: MAttributes(("for", "module-consent")))), MLink("Read usage policy", "#policy"))),
        MTemplate("preference-checkbox", "Notification preference", "Forms",
            "A native checkbox setting with a label and concise explanation.",
            MCard(MRow(MNode("html.input.checkbox", "Notification setting", attributes: MAttributes(("id", "module-notifications"), ("name", "notifications"), ("checked", "true"), ("aria-label", "Receive project updates"))), MNode("core.label", "Notification label", "Receive project updates", attributes: MAttributes(("for", "module-notifications")))), MText("Get a summary when records in this project change."))),

        MTemplate("heading-summary", "Heading and summary", "Content",
            "A concise heading and supporting paragraph to introduce a small content region.",
            MCol(MNode("core.heading3", "Module heading", "A clear section title", styles: MStyles(("margin", "0"), ("font-size", "1.25rem"))), MText("Explain what the reader can find or accomplish in this part of the page."))),
        MTemplate("quote-credit", "Quotation with source credit", "Content",
            "A short quotation and explicitly labeled attribution.",
            MNode("core.figure", "Quotation figure", styles: MStyles(("margin", "0"), ("padding", "0.5rem 0 0.5rem 1rem"), ("border-left", "3px solid #356a9d")), children:
            [MNode("core.quote", "Quotation", "Good tools make the next step clear.", styles: MStyles(("margin", "0"), ("font-size", "1.15rem"))), MNode("core.caption", "Quotation attribution", "Example quotation · Contributor name", styles: MMuted())])),
        MTemplate("definition-entry", "Term and definition", "Content",
            "One glossary term paired with its plain-language definition.",
            MCard(MText("Reusable module", true), MText("A small arrangement of elements that performs one clear job within a page."))),
        MTemplate("code-example", "Code example with filename", "Content",
            "A filename label and a horizontally scrollable, editable preformatted code sample.",
            MCard(MText("example.js", true), MNode("core.code", "Code sample", "const title = \"Project overview\";\nconsole.log(title);", styles: MStyles(("margin", "0"), ("padding", "0.85rem"), ("background", "#142538"), ("color", "#e8f1fa"), ("border-radius", "0.5rem"), ("overflow-x", "auto"), ("font-size", "0.85rem"), ("white-space", "pre"))))),
        MTemplate("publication-details", "Publication details", "Content",
            "A compact metadata line for author, revision date, and reading time.",
            MRow(MText("By Contributor name", true), MText("Updated 24 October 2026"), MText("6 minute read"))),

        MTemplate("search-result", "Search result excerpt", "Search",
            "A result title, readable location, and short contextual excerpt.",
            MCard(MLink("Working with reusable layouts", "#search-result"), MText("Guides / Getting started"), MText("Find the right arrangement, add your content, and refine individual elements as your project grows."))),
        MTemplate("active-filter", "Active filter chip", "Search",
            "A removable-filter control with the current label and an accessible removal action.",
            MRow(MBadge("Status: Reviewed"), MNode("core.button", "Remove reviewed filter", "Remove filter", attributes: MAttributes(("type", "button"), ("aria-label", "Remove Status: Reviewed filter")), styles: MButtonStyles()))),
        MTemplate("result-count", "Result count summary", "Search",
            "A result-range announcement and the active query in plain language.",
            MNode("core.container", "Search result summary", attributes: MAttributes(("role", "status")), layout: MColumnLayout(), children:
            [MText("Showing 1–20 of 124 results", true), MText("Query: reusable layouts · All records")])),

        MTemplate("price-label", "Price and billing interval", "Commerce",
            "A currency amount with an explicit billing interval and tax note.",
            MCard(MRow(MValue("$24"), MText("per month")), MText("Example price · Tax calculated separately"))),
        MTemplate("stock-status", "Availability and lead time", "Commerce",
            "A text-labeled stock state with a clear dispatch estimate.",
            MCol(MBadge("Available"), MText("Example item · Dispatch in 2–3 business days"))),
        MTemplate("order-total", "Order total breakdown", "Commerce",
            "A compact subtotal, shipping amount, and emphasized final total.",
            MCard(MRow(MText("Subtotal"), MText("$48.00")), MRow(MText("Shipping"), MText("$5.00")), MRow(MText("Total", true), MText("$53.00", true)), MText("Example order · USD"))),

        MTemplate("contributor-byline", "Contributor byline", "People",
            "A compact initials avatar with contributor name and role.",
            MRow(MAvatar("JS", "Contributor initials"), MCol(MText("Jordan Smith", true), MText("Research editor")))),
        MTemplate("assignee-pair", "Assigned people pair", "People",
            "Two labeled people for a compact owner and reviewer assignment.",
            MCard(MRow(MAvatar("AM", "Owner initials"), MCol(MText("Owner", true), MText("Alex Morgan"))), MRow(MAvatar("RL", "Reviewer initials"), MCol(MText("Reviewer", true), MText("Riley Lee"))))),

        MTemplate("record-status", "Record status and reason", "Feedback",
            "A status label with a plain-language explanation that does not depend on color.",
            MCard(MBadge("Needs review"), MText("Two required details are waiting for a reviewer."))),
        MTemplate("empty-result", "Empty result with next step", "Feedback",
            "A compact no-results message with a clear route back to the full collection.",
            MCard(MText("No matching records", true), MText("Try a broader phrase or remove one of the active filters."), MLink("View all records", "#records"))),
        MTemplate("inline-notice", "Inline information notice", "Feedback",
            "A concise informational message with an optional detail destination.",
            MNode("core.container", "Information notice", attributes: MAttributes(("role", "note")), styles: MStyles(("padding", "1rem"), ("border-left", "3px solid #356a9d"), ("background", "#edf4fa"), ("color", "#203c55"), ("border-radius", "0.4rem")), layout: MColumnLayout(), children:
            [MText("A newer version is available", true), MText("Review the changes before updating this record."), MLink("Read version notes", "#version-notes")])),

        MTemplate("task-checkbox", "Task checkbox and due date", "Workflow",
            "One native task checkbox paired with an editable due date label.",
            MCard(MRow(MNode("html.input.checkbox", "Task completion", attributes: MAttributes(("id", "module-task"), ("aria-label", "Complete review the project brief"))), MNode("core.label", "Task label", "Review the project brief", attributes: MAttributes(("for", "module-task")))), MText("Due 28 October 2026 · Assigned to Operations"))),
        MTemplate("step-position", "Current step indicator", "Workflow",
            "An accessible progress indicator with the current step and next action in text.",
            MCard(MText("Step 2 of 4 · Review details", true), MNode("html.progress", "Setup progress", attributes: MAttributes(("max", "4"), ("value", "2"), ("aria-label", "Setup step 2 of 4")), styles: MStyles(("width", "100%"), ("height", "0.65rem"))), MText("Next: Confirm the information")))
    ];

    private static VisualPageTemplateDocument MTemplate(string slug, string name, string category, string description, VisualPageElement root) => new()
    {
        TemplateId = "app.module." + slug + ".v1", Name = name, Scope = "element", StorageScope = "built-in",
        Library = "Application patterns", Category = category, CategoryGroup = category, Description = description,
        UsageHint = "Add this module to a block or page, then edit its individual elements. " + description,
        Tags = ["module", category.ToLowerInvariant(), "application", slug], Root = root
    };

    private static VisualPageElement MNode(string descriptor, string name, string text = "", Dictionary<string, string>? attributes = null,
        Dictionary<string, string>? styles = null, VisualPageLayoutSpec? layout = null, IEnumerable<VisualPageElement>? children = null) => new()
    {
        DescriptorId = descriptor, Name = name, Text = text,
        Attributes = attributes ?? [], Styles = styles ?? [],
        Layout = layout is null ? [] : new() { [VisualPageBreakpoint.Base] = layout }, Children = children?.ToList() ?? []
    };

    private static VisualPageElement MText(string text, bool strong = false) => MNode("core.paragraph", text, text,
        styles: strong ? MStyles(("margin", "0"), ("font-weight", "650"), ("line-height", "1.5")) : MMuted());
    private static VisualPageElement MValue(string text) => MNode("core.paragraph", "Metric value", text,
        styles: MStyles(("margin", "0"), ("font-size", "clamp(1.7rem, 5vw, 2.35rem)"), ("font-weight", "750"), ("line-height", "1.1"), ("font-variant-numeric", "tabular-nums")));
    private static VisualPageElement MLink(string text, string href) => MNode("core.link", text, text, attributes: MAttributes(("href", href)),
        styles: MStyles(("color", "#285d8f"), ("font-weight", "600"), ("text-underline-offset", "0.2em")));
    private static VisualPageElement MButton(string text) => MNode("core.button", text, text, attributes: MAttributes(("type", "button")), styles: MButtonStyles());
    private static VisualPageElement MRow(params VisualPageElement[] children) => MNode("core.container", "Module row", layout: MFlex("space-between"), children: children);
    private static VisualPageElement MCol(params VisualPageElement[] children) => MNode("core.container", "Module details", layout: MColumnLayout(), children: children);
    private static VisualPageElement MCard(params VisualPageElement[] children) => MNode("core.container", "Module panel", styles: MStyles(
        ("box-sizing", "border-box"), ("max-width", "100%"), ("min-width", "0"), ("background", "#ffffff"), ("color", "#203248"),
        ("border", "1px solid #dce5ee"), ("border-radius", "0.75rem")), layout: new() { Display = "flex", FlexDirection = "column", Gap = "0.65rem", Padding = "1rem" }, children: children);
    private static VisualPageElement MBadge(string text) => MNode("core.inline", "Status label", text, styles: MStyles(
        ("display", "inline-block"), ("width", "fit-content"), ("padding", "0.25rem 0.6rem"), ("border-radius", "999px"),
        ("background", "#e8f1f8"), ("color", "#245785"), ("font-size", "0.8rem"), ("font-weight", "650")));
    private static VisualPageElement MImage(string name, string alt, string aspect) => MNode("core.image", name,
        attributes: MAttributes(("src", "assets/placeholder.svg"), ("alt", alt)), styles: MStyles(("display", "block"), ("width", "100%"),
        ("max-width", "100%"), ("aspect-ratio", aspect), ("object-fit", "cover"), ("border-radius", "0.6rem"), ("background", "#dfe9f2")));
    private static VisualPageElement MAvatar(string initials, string label) => MNode("core.inline", label, initials,
        attributes: MAttributes(("role", "img"), ("aria-label", label)), styles: MStyles(("display", "inline-grid"), ("place-items", "center"),
        ("width", "2.6rem"), ("height", "2.6rem"), ("flex-shrink", "0"), ("border-radius", "50%"), ("background", "#e5edf6"), ("color", "#2b537c"), ("font-weight", "700")));
    private static VisualPageElement MBar(string label, int value) => MCol(MRow(MText(label), MText(value + " / 100", true)),
        MNode("core.container", label + " track", styles: MStyles(("height", "0.5rem"), ("background", "#e5edf4"), ("border-radius", "999px")), children:
        [MNode("core.container", label + " sample bar", styles: MStyles(("height", "100%"), ("width", value + "%"), ("background", "#356a9d"), ("border-radius", "999px")))]));
    private static VisualPageElement MColumn(int value) => MNode("core.container", "Sample volume " + value, styles: MStyles(
        ("height", value + "%"), ("min-height", "2px"), ("background", "#356a9d"), ("border-radius", "3px 3px 0 0")));
    private static VisualPageElement MField(string label, string id, string descriptor, string placeholder, string help) => MCol(
        MNode("core.label", label + " label", label, attributes: MAttributes(("for", id))),
        MNode(descriptor, label + " input", attributes: MAttributes(("id", id), ("name", id), ("placeholder", placeholder), ("aria-label", label), ("aria-describedby", id + "-help")), styles: MInputStyles()),
        MNode("core.paragraph", label + " guidance", help, attributes: MAttributes(("id", id + "-help")), styles: MMuted()));
    private static VisualPageLayoutSpec MColumnLayout() => new() { Display = "flex", FlexDirection = "column", Gap = "0.45rem" };
    private static VisualPageLayoutSpec MFlex(string justify = "flex-start") => new() { Display = "flex", FlexDirection = "row", FlexWrap = "wrap", Gap = "0.75rem", AlignItems = "center", JustifyContent = justify };
    private static VisualPageLayoutSpec MGrid(string columns) => new() { Display = "grid", GridTemplateColumns = columns, Gap = "0.85rem" };
    private static Dictionary<string, string> MMuted() => MStyles(("margin", "0"), ("color", "#5d6c7d"), ("font-size", "0.875rem"), ("line-height", "1.55"));
    private static Dictionary<string, string> MButtonStyles() => MStyles(("padding", "0.6rem 0.85rem"), ("border", "1px solid #bdcede"), ("border-radius", "0.5rem"), ("background", "#f5f9fd"), ("color", "#285379"), ("font-weight", "600"));
    private static Dictionary<string, string> MInputStyles() => MStyles(("box-sizing", "border-box"), ("width", "100%"), ("min-width", "0"), ("padding", "0.7rem"), ("border", "1px solid #bdcede"), ("border-radius", "0.5rem"), ("background", "#ffffff"), ("color", "#203248"));
    private static Dictionary<string, string> MAttributes(params (string Key, string Value)[] values) => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
    private static Dictionary<string, string> MStyles(params (string Key, string Value)[] values) => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
}
