using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Complete application screens, named and grouped by the work they help a reader do.</summary>
public static class StudioApplicationPageTemplates
{
    public static IReadOnlyList<VisualPageTemplateDocument> CreateTemplates() =>
    [
        PTemplate("analytics-overview", "Analytics overview", "Graphs",
            "A reporting overview with summary metrics, a labeled comparison chart, channel performance, and a methodology note.",
            PAnalytics()),
        PTemplate("media-library", "Media asset library", "Media",
            "A media collection with image previews, file metadata, collection links, and a selected-asset detail card.",
            PMediaLibrary()),
        PTemplate("record-workspace", "Record management workspace", "Data",
            "A record register with search controls, a semantic data table, record details, and a clear review state.",
            PRecords()),
        PTemplate("project-board", "Project task board", "Workflow",
            "A three-stage project board with task cards, owners, priorities, milestones, and delivery notes.",
            PProjectBoard()),
        PTemplate("account-settings", "Account settings", "Forms",
            "An account settings screen with labeled profile fields, display preferences, notification choices, and a save action placeholder.",
            PAccount()),
        PTemplate("search-results", "Search results workspace", "Search",
            "A results page with a labeled query field, type filters, result summaries, useful search tips, and page navigation.",
            PSearch()),
        PTemplate("knowledge-article", "Knowledge base article", "Content",
            "A help article with in-page contents, numbered instructions, an example, troubleshooting disclosures, and related resources.",
            PKnowledge()),
        PTemplate("product-catalog", "Product catalog", "Commerce",
            "A storefront collection with category navigation, product cards, prices, product details, and delivery information.",
            PProducts()),
        PTemplate("team-directory", "Team member directory", "People",
            "A people directory with role filters, member cards, contact links, availability, and team responsibilities.",
            PTeam()),
        PTemplate("service-status", "Service status center", "Feedback",
            "A service health page with individual component states, an incident timeline, planned maintenance, and support information.",
            PStatus())
    ];

    private static VisualPageElement PAnalytics() => PPage("Analytics", "Understand the shape of your activity.",
        "Sample report · Last 30 days", "Overview", "Comparison", "Channels",
        PSection("overview", "Overview", "A small set of measures with an explicit comparison period.",
            PGrid("Summary metrics",
                PMetric("Page views", "24,860", "+12.4% versus the prior 30 days"),
                PMetric("Active readers", "8,420", "+6.8% versus the prior 30 days"),
                PMetric("Read completion", "72%", "+4 percentage points"))),
        PSection("comparison", "Weekly activity", "Sample page views, grouped by week. Each bar includes its exact value.",
            PPanel("Comparison plot",
                PBar("Week 1", "4,200 views", "47%"),
                PBar("Week 2", "5,400 views", "61%"),
                PBar("Week 3", "6,360 views", "71%"),
                PBar("Week 4", "8,900 views", "100%"))),
        PSection("channels", "Acquisition channels", "Use the table to compare the contribution of each channel.",
            PTable("Sample channel performance", ["Channel", "Readers", "Completion"],
                ["Direct", "3,460", "78%"], ["Search", "2,950", "71%"], ["Referrals", "2,010", "64%"]),
            PNote("How to read this report", "All values are illustrative. Connect a reporting source and define the measurement window before using this page as a live dashboard.")));

    private static VisualPageElement PMediaLibrary() => PPage("Media library", "Keep every asset close to its context.",
        "Collection / Field study", "Assets", "Details", "Collections",
        PSection("assets", "All assets", "A visual index with a useful name, format, and description for every item.",
            PSearchField("media-query", "Find an asset", "Search by name, subject, or format"),
            PGrid("Media assets",
                PAsset("Coastal light", "Image · 2400 × 1600 · 1.8 MB", "A sample image for the opening story."),
                PAsset("Studio notes", "Image · 1800 × 1800 · 960 KB", "A sample image for a process journal."),
                PAsset("Material detail", "Image · 2400 × 1600 · 1.2 MB", "A sample image for a collection detail."))),
        PSection("details", "Asset details", "Keep editorial and reuse information beside the collection.",
            PGrid("Asset metadata",
                PPanel("Selected asset", PH(3, "Coastal light"), PP("Selected for the collection introduction."),
                    PList("Usage information", "Alt text: Replace with a precise description of the uploaded image.", "Credit: Add creator and license.", "Used in: Collection introduction.")),
                PPanel("Asset readiness", PH(3, "Ready for review"), PP("Check image quality, credit, and alternative text before publication."), PLink("Review the collection", "#collections")))),
        PSection("collections", "Collections", "Group related assets by their purpose in the project.",
            PGrid("Collection folders", PCard("Opening story", "Hero images and supporting material for the introduction.", "#assets"),
                PCard("Process journal", "Working images, diagrams, and reference scans.", "#assets"),
                PCard("Published selections", "Assets approved for the current edition.", "#assets"))));

    private static VisualPageElement PRecords() => PPage("Records", "A shared register with room for the details.",
        "Operations / Register", "Register", "Record", "Review",
        PSection("register", "Record register", "Sample entries show how structured data and a detail view can work together.",
            PSearchField("record-query", "Find a record", "Search by record name or reference"),
            PTable("Example record register", ["Reference", "Record", "Owner", "State"],
                ["REC-1042", "Collection audit", "Jordan Lee", "In review"],
                ["REC-1043", "Source permissions", "Sam Patel", "Ready"],
                ["REC-1044", "Media inventory", "Alex Morgan", "Draft"])),
        PSection("record", "Collection audit", "REC-1042 · Updated in the current review cycle",
            PGrid("Record detail and context",
                PPanel("Record summary", PH(3, "Review summary"), PP("Compare the current collection against the agreed inventory and identify missing source details."),
                    PList("Record fields", "Owner: Jordan Lee", "Category: Content operations", "Priority: Normal", "Next action: Confirm missing credits")),
                PPanel("Related activity", PH(3, "Activity"), PList("Recent changes", "Draft created and assigned to the collection team.", "Reference list checked against the media inventory.", "Submitted for editorial review.")))),
        PSection("review", "Review checklist", "Use explicit completion criteria for each record.",
            PNote("Before closing a record", "Confirm the required fields, inspect related files, and record the decision. This template provides a register layout; storage and filtering can be added through your application tools.")));

    private static VisualPageElement PProjectBoard() => PPage("Project board", "Make the next useful step visible.",
        "Project / Collection refresh", "Board", "Milestones", "Notes",
        PSection("board", "Current cycle", "Three stages keep work, review, and completion distinct.",
            PGrid("Task columns",
                PPanel("Planned tasks", PH(2, "Planned · 2"),
                    PTask("Audit source links", "Jordan · Normal", "Confirm the destination and credit of each source."),
                    PTask("Prepare image captions", "Alex · Normal", "Add concise captions to the new collection images.")),
                PPanel("Active tasks", PH(2, "In progress · 2"),
                    PTask("Build the contents page", "Sam · High", "Organize the new edition into readable sections."),
                    PTask("Review navigation labels", "Jordan · Normal", "Make the route between related pages clear.")),
                PPanel("Completed tasks", PH(2, "Complete · 1"),
                    PTask("Agree the page inventory", "Team · Complete", "A first list of pages has been reviewed.")))),
        PSection("milestones", "Milestones", "Connect individual tasks to a visible delivery plan.",
            PTable("Project milestones", ["Milestone", "Outcome", "State"],
                ["Content inventory", "Page list agreed", "Complete"], ["Design review", "Shared templates reviewed", "In progress"], ["Publication review", "All pages checked", "Planned"])),
        PSection("notes", "Working agreements", "Keep the rules of the board short and practical.",
            PNote("One owner, one next action", "Every card should explain what changes and who will take the next step. Add drag behavior or a saved workflow when connecting this board to application data.")));

    private static VisualPageElement PAccount() => PPage("Account settings", "Choose how your profile appears and how you hear from us.",
        "Account / Preferences", "Profile", "Preferences", "Notifications",
        PSection("profile", "Profile", "Keep the information other people use to recognize you up to date.",
            PPanel("Profile fields", PGrid("Profile inputs",
                PField("profile-name", "Display name", "text", "Jordan Lee"),
                PField("profile-email", "Email address", "email", "jordan@example.com")),
                PField("profile-role", "Role or specialty", "text", "Editor and researcher"),
                PNote("Profile visibility", "Describe which profile fields are visible to other members of your application."))),
        PSection("preferences", "Preferences", "Use native controls for the choices people return to most often.",
            PGrid("Display preferences",
                PSelect("account-language", "Interface language", "English", "Español", "Français"),
                PSelect("account-density", "Content density", "Comfortable", "Compact"))),
        PSection("notifications", "Notifications", "Let people choose the updates that matter to them.",
            PPanel("Notification choices",
                PCheckbox("notice-replies", "Replies to my comments"),
                PCheckbox("notice-review", "Requests for review"),
                PCheckbox("notice-digest", "A periodic activity digest"),
                PE("core.button", "Save preferences action", "Save preferences", attributes: PA(("type", "button")), styles: PButtonStyles()),
                PP("Form layout only. Connect the save action to your account service before collecting or storing profile information.", true))));

    private static VisualPageElement PSearch() => PPage("Search", "Find the right page without losing the thread.",
        "Library / Search", "Results", "Refine", "Help",
        PSection("results", "Search the collection", "Sample results for “collection”.",
            PSearchField("site-query", "Search terms", "collection"),
            PGrid("Search results and facets",
                PPanel("Result list", PResult("Collection planning guide", "Guide · 6 min read", "A practical sequence for deciding what belongs in a new collection.", "#help"),
                    PResult("Media collection checklist", "Checklist · Updated this edition", "The required credits, captions, and file details for a reusable media library.", "#help"),
                    PResult("Organizing pages and parts", "Reference · 4 min read", "Use parts and chapters to make a growing work easier to navigate.", "#help")),
                PPanel("Result facets", PH(2, "Refine results"),
                    PList("Result types", "Guides · 8", "Reference pages · 12", "Media · 4"),
                    PLink("Refinement options", "#refine"))),
            PE("core.nav", "Result page navigation", attributes: PA(("aria-label", "Search result pages")), layout: PF("row", "0.75rem"), children:
                [PLink("Page 1", "#results"), PLink("Page 2", "#results"), PLink("Next page", "#results")])),
        PSection("refine", "Refinement options", "Make the scope of a search explicit.",
            PGrid("Search filters", PSelect("result-type", "Content type", "All types", "Guides", "Reference", "Media"),
                PSelect("result-order", "Sort results", "Most relevant", "Title", "Recently updated"))),
        PSection("help", "Search tips", "Give readers a path forward when the first query is too broad.",
            PNote("Try a more precise phrase", "Use a document title, a topic, or a distinctive term. Result filtering and query execution are application behaviors to connect to this layout.")));

    private static VisualPageElement PKnowledge() => PPage("Knowledge base", "Publish a collection with confidence.",
        "Guide / Publication", "Steps", "Troubleshooting", "Related",
        PSection("steps", "Prepare a collection for publication", "A repeatable checklist for the final editorial pass.",
            PE("core.nav", "Article contents", attributes: PA(("aria-label", "Article contents")), layout: PF("row", "0.75rem"), children:
                [PLink("Review the structure", "#structure"), PLink("Check the content", "#content"), PLink("Inspect the reader view", "#reader")]),
            PPanel("Publication instructions",
                PE("html.article", "Guide article", layout: PF("column", "1rem"), children:
                [
                    PH(2, "1. Review the structure", "structure"), PP("Open the contents view and confirm that every page has a clear title and a useful position. The landing page should offer an understandable route into the work."),
                    PH(2, "2. Check the content", "content"), PP("Read headings, captions, and link labels together. Every image needs alternative text and every borrowed source needs an appropriate credit."),
                    PNote("A useful link label", "Prefer “Read the source notes” to a label such as “Click here”. Describe the destination in the link itself."),
                    PH(2, "3. Inspect the reader view", "reader"), PP("Preview the complete work at a wide and a narrow width. Check the reading order, navigation, media, and the route to the next page.")
                ]))),
        PSection("troubleshooting", "Troubleshooting", "Short answers to common review questions.",
            PDisclosure("An image is missing", "Open the image properties and confirm that the selected file is included in the project. Then preview again and review its alternative text."),
            PDisclosure("A link opens the wrong page", "Check the link destination and verify it from the reader view. A descriptive label should match the destination.")),
        PSection("related", "Related resources", "Keep follow-up guidance near the end of the article.",
            PGrid("Related articles", PCard("Page organization", "Choose readable titles and a useful order.", "#structure"),
                PCard("Media preparation", "Review captions, source credits, and alternative text.", "#content"))));

    private static VisualPageElement PProducts() => PPage("Catalog", "Useful objects, clearly presented.",
        "Collection / Everyday tools", "Products", "Details", "Delivery",
        PSection("products", "The collection", "Sample products demonstrate images, pricing, and clear next steps.",
            PGrid("Catalog filters", PSelect("catalog-category", "Product category", "All products", "Desk tools", "Notebooks", "Storage"),
                PSelect("catalog-order", "Sort products", "Featured", "Name", "Price")),
            PGrid("Product cards", PProduct("Daily notebook", "$18", "A simple notebook for notes, sketches, and plans."),
                PProduct("Desk tray", "$28", "A compact place for the things you use every day."),
                PProduct("Reference cards", "$12", "A small set of cards for organizing a working idea."))),
        PSection("details", "Know what you are choosing", "Give people specific information before asking them to buy.",
            PGrid("Product details", PCard("Dimensions and materials", "List the measurements, material composition, and care instructions for each item.", "#products"),
                PCard("What is included", "Describe the quantity, included accessories, and any product variations.", "#products"))),
        PSection("delivery", "Delivery and returns", "Explain fulfillment in language a customer can use.",
            PNote("Before you place an order", "Add your real shipping regions, delivery estimates, and return policy. Product values are examples; connect purchasing and inventory before enabling transactions.")));

    private static VisualPageElement PTeam() => PPage("Team directory", "Find a person and understand their part in the work.",
        "People / Project team", "Members", "Responsibilities", "Contact",
        PSection("members", "Meet the team", "Names, roles, and a short description of the work each person supports.",
            PGrid("People filters", PSearchField("people-query", "Find a team member", "Search by name or specialty"),
                PSelect("people-role", "Team role", "All roles", "Editorial", "Research", "Design")),
            PGrid("Team member cards",
                PPerson("JL", "Jordan Lee", "Editorial lead", "Page organization, review, and publication.", "jordan@example.com"),
                PPerson("SP", "Sam Patel", "Researcher", "Source notes, evidence, and data preparation.", "sam@example.com"),
                PPerson("AM", "Alex Morgan", "Designer", "Reusable layouts, media, and accessible presentation.", "alex@example.com"))),
        PSection("responsibilities", "Who to ask", "Make ownership visible without making the directory complicated.",
            PTable("Team responsibilities", ["Need", "First contact"],
                ["Review a new page", "Editorial lead"], ["Check a source", "Researcher"], ["Adjust a shared layout", "Designer"])),
        PSection("contact", "Working together", "Use a clear route for questions that do not belong to a single role.",
            PNote("Shared questions", "Use the project's agreed contact channel and include a link to the relevant page. The people and email addresses above are sample content.")));

    private static VisualPageElement PStatus() => PPage("Service status", "A clear picture of what is working.",
        "Operations / Service health", "Components", "Incidents", "Maintenance",
        PSection("components", "Service components", "Example health states. Replace these values with your monitored services.",
            PPanel("Overall service summary", PBadge("All systems operational"), PH(2, "Everything is running normally"),
                PP("The status of each public-facing component appears below.")),
            PTable("Example component status", ["Component", "State", "Last review"],
                ["Website", "Operational", "Current review"], ["Search", "Operational", "Current review"], ["Media delivery", "Operational", "Current review"])),
        PSection("incidents", "Incident history", "Explain what happened, what changed, and what readers can expect.",
            PPanel("Most recent incident", PBadge("Resolved"), PH(2, "Delayed search indexing"),
                PList("Incident updates", "09:10 · Investigating an indexing delay for newly published pages.",
                    "09:35 · Processing resumed and the queue began to clear.", "10:05 · All queued pages indexed; incident resolved."),
                PP("Illustrative timeline, not a live incident report.", true))),
        PSection("maintenance", "Planned maintenance", "Keep planned interruptions separate from unplanned incidents.",
            PGrid("Maintenance and support", PCard("No maintenance scheduled", "Publish the expected impact and time window before planned service work.", "#components"),
                PCard("Need support?", "Add your support destination and the information people should include in a report.", "#incidents"))));

    private static VisualPageTemplateDocument PTemplate(string slug, string name, string role, string description, VisualPageElement root) => new()
    {
        TemplateId = "app.page." + slug + ".v1", Name = name, Description = description, Scope = "page",
        StorageScope = "built-in", Library = "Application patterns", Category = role, CategoryGroup = role,
        UsageHint = "Use as an editable application page. Sample content and controls can be connected to your project's data and actions.",
        Tags = ["page", "application", role.ToLowerInvariant(), "responsive", "editable"],
        SuggestedPageName = name, SuggestedPageRoute = "/" + slug, SuggestedPageTitle = name, SuggestedPageDescription = description,
        ThemeTokens = new(StringComparer.Ordinal) { ["color.background"] = "#f4f6fa", ["color.accent"] = "#2559a8", ["color.text"] = "#192a42" }, Root = root
    };

    private static VisualPageElement PPage(string label, string title, string eyebrow, string first, string second, string third,
        params VisualPageElement[] sections)
    {
        var destinations = sections.Select(section => "#" + section.Attributes["id"]).ToArray();
        return PE("core.page", label + " page", styles: PS(("background", "#f4f6fa"), ("color", "#192a42"),
                ("font-family", "Segoe UI, system-ui, sans-serif"), ("line-height", "1.6"), ("min-height", "100vh")), layout: PF("column", "0"), children:
        [
            PE("html.header", "Application header", styles: PS(("background", "#ffffff"), ("border-bottom", "1px solid #dbe3ee")),
                layout: PF("row", "1rem", "1rem clamp(1rem, 5vw, 5rem)", "space-between"), children:
                [PLink("Workspace", "#page-top"), PE("core.nav", label + " navigation", attributes: PA(("aria-label", "Page sections")), layout: PF("row", "1rem"), children:
                    [PLink(first, destinations[0]), PLink(second, destinations[1]), PLink(third, destinations[2])])]),
            PE("core.container", label + " workspace", attributes: PA(("id", "page-top")),
                styles: PS(("width", "100%"), ("max-width", "1200px"), ("box-sizing", "border-box"), ("margin", "0 auto")),
                layout: PF("column", "2rem", "clamp(1.25rem, 4vw, 3.5rem) clamp(1rem, 4vw, 3rem)"), children:
                [PE("html.header", "Page introduction", layout: PF("column", "0.65rem"), children:
                    [PE("core.paragraph", "Page context", eyebrow, styles: PS(("margin", "0"), ("text-transform", "uppercase"), ("letter-spacing", "0.12em"), ("font-size", "0.75rem"), ("font-weight", "700"), ("color", "#536b8b"))),
                     PH(1, title), PP(label + " · An editable application page")]), .. sections]),
            PE("html.footer", "Application footer", styles: PS(("background", "#eaf0f7"), ("border-top", "1px solid #dbe3ee")),
                layout: PF("row", "1rem", "1.25rem clamp(1rem, 5vw, 5rem)", "space-between"), children:
                [PP(label + " / Application workspace", true), PLink("Back to top", "#page-top")])
        ]);
    }

    private static VisualPageElement PSection(string id, string title, string intro, params VisualPageElement[] children) =>
        PE("core.section", title + " section", attributes: PA(("id", id)), layout: PF("column", "1rem"), children: [PH(2, title), PP(intro, true), .. children]);

    private static VisualPageElement PH(int level, string text, string? id = null) => PE("core.heading" + level, text + " heading", text,
        attributes: id is null ? null : PA(("id", id)), styles: PS(("margin", "0"), ("line-height", "1.22"),
            ("font-size", level == 1 ? "clamp(1.8rem, 4vw, 2.8rem)" : level == 2 ? "1.35rem" : "1.05rem"), ("letter-spacing", level == 1 ? "-0.035em" : "-0.01em")));

    private static VisualPageElement PP(string text, bool muted = false) => PE("core.paragraph", "Description", text,
        styles: PS(("margin", "0"), ("color", muted ? "#5d6e84" : "#30445f"), ("overflow-wrap", "anywhere")));

    private static VisualPageElement PGrid(string name, params VisualPageElement[] children) => PE("core.container", name,
        layout: new() { Display = "grid", GridTemplateColumns = "repeat(auto-fit, minmax(min(100%, 16rem), 1fr))", Gap = "1rem" }, children: children);

    private static VisualPageElement PPanel(string name, params VisualPageElement[] children) => PE("core.container", name,
        styles: PS(("background", "#ffffff"), ("border", "1px solid #dbe3ee"), ("border-radius", "12px"), ("min-width", "0")),
        layout: PF("column", "0.9rem", "1.25rem"), children: children);

    private static VisualPageElement PMetric(string label, string value, string comparison) => PPanel(label + " metric",
        PP(label, true), PE("core.paragraph", label + " value", value, styles: PS(("margin", "0"), ("font-size", "2.1rem"), ("font-weight", "700"), ("letter-spacing", "-0.04em"))), PP(comparison, true));

    private static VisualPageElement PBar(string label, string value, string width) => PE("core.container", label + " bar row", layout: PF("column", "0.45rem"), children:
    [
        PE("core.container", label + " labels", layout: PF("row", "0.5rem", justify: "space-between"), children: [PP(label), PP(value)]),
        PE("core.container", label + " track", styles: PS(("background", "#e9eef6"), ("border-radius", "4px")), children:
        [PE("core.container", label + " bar", attributes: PA(("aria-hidden", "true")), styles: PS(("width", width), ("height", "1.1rem"), ("background", "#4779bd"), ("border-radius", "4px")))])
    ]);

    private static VisualPageElement PNote(string title, string text) => PE("html.aside", title + " note",
        styles: PS(("background", "#eaf1fc"), ("border-left", "3px solid #4779bd"), ("border-radius", "6px")),
        layout: PF("column", "0.5rem", "1rem 1.2rem"), children: [PH(3, title), PP(text)]);

    private static VisualPageElement PTable(string title, string[] columns, params string[][] rows)
    {
        var headings = columns.Select(column => PE("html.th", column + " column", column,
            attributes: PA(("scope", "col")), styles: PS(("padding", "0.8rem 1rem"), ("background", "#edf2f8"), ("font-weight", "650"))));
        var tableRows = rows.Select((row, index) => PE("html.tr", "Record " + (index + 1), children:
            row.Select((cell, column) => PE(column == 0 ? "html.th" : "html.td", columns[column] + " value", cell,
                attributes: column == 0 ? PA(("scope", "row")) : null,
                styles: PS(("padding", "0.85rem 1rem"), ("border-top", "1px solid #e4eaf2"), ("font-weight", column == 0 ? "600" : "400"))))));
        var table = PE("html.table", title + " table", styles: PS(("width", "100%"), ("min-width", "34rem"), ("border-collapse", "collapse"), ("text-align", "left")), children:
        [
            PE("html.caption", title + " caption", title, styles: PS(("text-align", "left"), ("padding", "1rem"), ("font-weight", "650"), ("color", "#536b8b"))),
            PE("html.thead", title + " table head", children: [PE("html.tr", "Column headings", children: headings)]),
            PE("html.tbody", title + " table body", children: tableRows)
        ]);
        return PE("core.container", title + " table viewport", styles: PS(("overflow-x", "auto"), ("max-width", "100%"),
            ("border", "1px solid #dbe3ee"), ("border-radius", "10px"), ("background", "#ffffff")), children: [table]);
    }

    private static VisualPageElement PAsset(string title, string metadata, string description) => PPanel(title + " asset",
        PE("core.figure", title + " figure", styles: PS(("margin", "0")), layout: PF("column", "0.7rem"), children:
        [PE("core.image", title + " image", attributes: PA(("src", "assets/placeholder.svg"), ("alt", title + " sample asset; replace with your image"), ("loading", "lazy")),
            styles: PS(("width", "100%"), ("height", "auto"), ("aspect-ratio", "4 / 3"), ("object-fit", "cover"), ("background", "#dfe8f2"), ("border-radius", "7px"))),
         PE("core.caption", title + " caption", title, styles: PS(("font-weight", "650"), ("color", "#405772")))]), PP(metadata, true), PP(description));

    private static VisualPageElement PTask(string title, string owner, string text) => PE("html.article", title + " task",
        styles: PS(("padding", "1rem"), ("background", "#f7f9fc"), ("border", "1px solid #e1e8f2"), ("border-radius", "8px")),
        layout: PF("column", "0.55rem"), children: [PH(3, title), PP(text), PBadge(owner)]);

    private static VisualPageElement PBadge(string text) => PE("core.inline", "Status", text,
        styles: PS(("background", "#e5f3ed"), ("color", "#225a43"), ("padding", "0.25rem 0.6rem"), ("border-radius", "5px"), ("font-size", "0.8rem"), ("font-weight", "650"), ("width", "fit-content")));

    private static VisualPageElement PList(string name, params string[] items) => PE("core.list", name,
        styles: PS(("margin", "0"), ("padding-left", "1.3rem")), layout: PF("column", "0.55rem"),
        children: items.Select(item => PE("core.list-item", "List item", item, styles: PS(("color", "#40546f")))));

    private static VisualPageElement PLink(string text, string href) => PE("core.link", text + " link", text, attributes: PA(("href", href)),
        styles: PS(("color", "#2559a8"), ("font-weight", "600"), ("text-underline-offset", "0.2em")));

    private static VisualPageElement PCard(string title, string description, string href) => PPanel(title + " card", PH(3, title), PP(description), PLink("Explore " + title.ToLowerInvariant(), href));

    private static VisualPageElement PField(string id, string label, string type, string value) => PE("core.field", label + " field", layout: PF("column", "0.45rem"), children:
        [PE("core.label", label + " label", label, attributes: PA(("for", id)), styles: PS(("font-weight", "600"))),
         PE("html.input." + type, label + " input", attributes: PA(("id", id), ("name", id), ("value", value), ("aria-label", label)), styles: PInputStyles())]);

    private static VisualPageElement PSearchField(string id, string label, string placeholder) => PE("core.field", label + " search", layout: PF("column", "0.45rem"), children:
        [PE("core.label", label + " label", label, attributes: PA(("for", id)), styles: PS(("font-weight", "600"))),
         PE("html.input.search", label + " input", attributes: PA(("id", id), ("name", id), ("placeholder", placeholder), ("aria-label", label)), styles: PInputStyles())]);

    private static VisualPageElement PSelect(string id, string label, params string[] options)
    {
        var choices = options.Select((option, index) => PE("core.option", option + " option", option,
            attributes: PA(("value", index.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        return PE("core.field", label + " choice", layout: PF("column", "0.45rem"), children:
        [
            PE("core.label", label + " label", label, attributes: PA(("for", id)), styles: PS(("font-weight", "600"))),
            PE("html.select", label + " select", attributes: PA(("id", id), ("name", id), ("aria-label", label)), styles: PInputStyles(), children: choices)
        ]);
    }

    private static VisualPageElement PCheckbox(string id, string text) => PE("core.field", text + " preference", layout: PF("row", "0.65rem"), children:
        [PE("html.input.checkbox", text + " checkbox", attributes: PA(("id", id), ("name", id), ("aria-label", text))),
         PE("core.label", text + " label", text, attributes: PA(("for", id)))]);

    private static VisualPageElement PResult(string title, string metadata, string description, string href) => PE("html.article", title + " result",
        styles: PS(("border-bottom", "1px solid #e4eaf2"), ("padding-bottom", "1rem")), layout: PF("column", "0.5rem"), children:
        [PH(3, title), PP(metadata, true), PP(description), PLink("Read " + title.ToLowerInvariant(), href)]);

    private static VisualPageElement PDisclosure(string title, string body) => PE("html.details", title + " disclosure",
        styles: PS(("background", "#ffffff"), ("border", "1px solid #dbe3ee"), ("border-radius", "8px"), ("padding", "1rem")), children:
        [PE("html.summary", title + " summary", title, styles: PS(("font-weight", "650"), ("cursor", "pointer"))),
         PE("core.paragraph", title + " answer", body, styles: PS(("margin", "0.8rem 0 0"), ("color", "#40546f")))]);

    private static VisualPageElement PProduct(string title, string price, string description) => PPanel(title + " product",
        PE("core.container", title + " product illustration", attributes: PA(("role", "img"), ("aria-label", title + " illustration placeholder")),
            styles: PS(("aspect-ratio", "4 / 3"), ("border-radius", "8px"), ("background", "linear-gradient(145deg, #e7d9c7, #c5ad90)")), children:
            [PE("core.container", title + " product shape", styles: PS(("width", "45%"), ("height", "60%"), ("margin", "18% auto"), ("background", "#f9f3e9"), ("border", "1px solid #b6a48f"), ("box-shadow", "8px 10px 0 rgba(68, 51, 32, 0.12)")))]),
        PH(3, title), PBadge(price), PP(description), PLink("View " + title.ToLowerInvariant(), "#details"));

    private static VisualPageElement PPerson(string initials, string name, string role, string bio, string email) => PPanel(name + " member",
        PE("core.container", name + " avatar", initials, attributes: PA(("aria-hidden", "true")), styles: PS(("width", "3.5rem"), ("height", "3.5rem"),
            ("border-radius", "50%"), ("background", "#e3ebf8"), ("color", "#2559a8"), ("display", "grid"), ("place-items", "center"), ("font-weight", "700"))),
        PH(3, name), PBadge(role), PP(bio), PLink("Contact " + name, "mailto:" + email));

    private static Dictionary<string, string> PInputStyles() => PS(("padding", "0.8rem 0.9rem"), ("border", "1px solid #c6d2e3"), ("border-radius", "7px"),
        ("background", "#ffffff"), ("color", "#263c58"), ("font", "inherit"), ("min-width", "0"), ("width", "100%"), ("box-sizing", "border-box"));

    private static Dictionary<string, string> PButtonStyles() => PS(("padding", "0.8rem 1.2rem"), ("border", "0"), ("border-radius", "7px"),
        ("background", "#2559a8"), ("color", "#ffffff"), ("font", "inherit"), ("font-weight", "650"), ("width", "fit-content"), ("cursor", "pointer"));

    private static VisualPageElement PE(string descriptor, string name, string text = "", Dictionary<string, string>? attributes = null,
        Dictionary<string, string>? styles = null, VisualPageLayoutSpec? layout = null, IEnumerable<VisualPageElement>? children = null) => new()
    {
        DescriptorId = descriptor, Name = name, Text = text, Attributes = attributes ?? new(StringComparer.Ordinal),
        Styles = styles ?? new(StringComparer.Ordinal), Children = children?.ToList() ?? [],
        Layout = layout is null ? [] : new() { [VisualPageBreakpoint.Base] = layout }
    };

    private static VisualPageLayoutSpec PF(string direction, string gap, string padding = "", string justify = "") => new()
    { Display = "flex", FlexDirection = direction, FlexWrap = direction == "row" ? "wrap" : "", Gap = gap, Padding = padding, AlignItems = direction == "row" ? "center" : "", JustifyContent = justify };

    private static Dictionary<string, string> PA(params (string Key, string Value)[] values) => values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    private static Dictionary<string, string> PS(params (string Key, string Value)[] values) => values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
}
