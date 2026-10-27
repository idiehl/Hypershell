using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Composed, independently editable sections grouped by their job in an application.</summary>
public static class StudioApplicationSectionTemplates
{
    public static IReadOnlyList<VisualPageTemplateDocument> CreateTemplates() =>
    [
        S("workspace-navigation", "Workspace navigation bar", "Navigation", "Product identity, workspace links, search field, and account entry in a wrapping header.", WorkspaceNavigation()),
        S("context-navigation", "Record navigation and breadcrumb", "Navigation", "Breadcrumb trail and related record views with a clear current location.", ContextNavigation()),
        S("reading-footer", "Reading progress and page navigation", "Navigation", "A reading checkpoint with previous, contents, and next-page destinations.", ReadingFooter()),
        S("captioned-gallery", "Captioned media collection", "Media", "Three responsive image figures with editable captions and collection information.", CaptionedGallery()),
        S("video-transcript", "Video player and transcript", "Media", "An accessible video region paired with chapter markers and an expandable transcript.", VideoTranscript()),
        S("audio-playlist", "Audio playlist and episode notes", "Media", "An audio player, selected episode notes, and an ordered list of related recordings.", AudioPlaylist()),
        S("resource-grid", "Resource card collection", "Grid", "A responsive card grid for documents, tools, and shared project resources.", ResourceGrid()),
        S("capability-grid", "Capability comparison grid", "Grid", "Three application capabilities with consistent title, description, and availability information.", CapabilityGrid()),
        S("application-grid", "Application launcher grid", "Grid", "A set of named destinations with role descriptions and distinct launch links.", ApplicationGrid()),
        S("record-table", "Record register and summary", "Data", "A horizontally scrollable semantic table with record status, ownership, and update date.", RecordTable()),
        S("metric-summary", "Key metric summary", "Data", "Four compact metrics with labeled values, comparison periods, and explanatory notes.", MetricSummary()),
        S("record-details", "Record detail sheet", "Data", "A record heading, field/value sheet, related links, and notes panel.", RecordDetails()),
        S("horizontal-bars", "Category bar graph and data", "Graphs", "A labeled horizontal bar graph with its accessible source table; values are editable sample data.", HorizontalBars()),
        S("vertical-bars", "Period column graph and data", "Graphs", "A four-period column graph with visible values and a semantic source table.", VerticalBars()),
        S("composition-graph", "Composition graph and legend", "Graphs", "A stacked composition strip with labeled totals, legend, and source table.", CompositionGraph()),
        S("contact-intake", "Request intake form", "Forms", "Labeled name, email, and request fields with a draft action and help text.", RequestIntake()),
        S("account-preferences", "Account preference fields", "Forms", "Profile fields, locale selection, and notification choices grouped for account settings.", AccountPreferences()),
        S("survey-response", "Survey response form", "Forms", "A rating selector and open response area for a compact feedback survey.", SurveyResponse()),
        S("article-outline", "Article and section outline", "Content", "An article introduction and two content sections beside a linked section outline.", ArticleOutline()),
        S("question-answers", "Expandable question and answer list", "Content", "Three native disclosure rows for help content, requirements, and next steps.", QuestionAnswers()),
        S("search-results", "Search results and refinement", "Search", "Search query entry, result summaries, metadata, and pagination in one reusable section.", SearchResults()),
        S("faceted-search", "Search filters and results grid", "Search", "Keyword and type controls beside a small collection of filterable-result placeholders.", FacetedSearch()),
        S("plan-comparison", "Subscription plan comparison", "Commerce", "Three editable plan summaries with prices, included capabilities, and selection actions.", PlanComparison()),
        S("order-summary", "Order items and totals", "Commerce", "A semantic line-item table, subtotal and total breakdown, and checkout draft action.", OrderSummary()),
        S("team-directory", "Team member directory", "People", "Three contributor summaries with role, responsibility, and profile destinations.", TeamDirectory()),
        S("contributor-profile", "Contributor biography and work", "People", "Contributor identity and biography paired with a concise list of their work.", ContributorProfile()),
        S("notice-center", "Notification center", "Feedback", "Dated information, action-required, and success notices with explicit text labels.", NoticeCenter()),
        S("service-status", "Service availability summary", "Feedback", "Text-labeled component availability and a short incident history with timestamps.", ServiceStatus()),
        S("task-board", "Task board by stage", "Workflow", "Three responsive work stages with independent task cards, owners, and due labels.", TaskBoard()),
        S("milestone-timeline", "Project milestone timeline", "Workflow", "A dated sequence of completed, current, and upcoming milestones with next actions.", MilestoneTimeline())
    ];

    private static VisualPageTemplateDocument S(string slug, string name, string role, string description, VisualPageElement root) => new()
    {
        TemplateId = "app.section." + slug + ".v1", Name = name, Description = description,
        Scope = "section", StorageScope = "built-in", Library = "Application patterns",
        Category = role, CategoryGroup = role, UsageHint = "Add this section, then edit its content, links, and behavior for your application.",
        Tags = ["section", role.ToLowerInvariant(), "application", "responsive"],
        ThemeTokens = new(StringComparer.Ordinal) { ["color.background"] = "#f5f7fa", ["color.accent"] = "#275d77", ["color.text"] = "#172b3a" },
        Root = root
    };

    private static VisualPageElement WorkspaceNavigation() => E("html.header", "Workspace header", children:
    [
        E("core.link", "Workspace home", "Workspace", A(("href", "#workspace-home")), St(("font-size", "1.3rem"), ("font-weight", "750"))),
        Nav("Workspace destinations", L("Overview", "#workspace-overview"), L("Records", "#workspace-records"), L("Reports", "#workspace-reports")),
        E("html.input.search", "Search workspace", attributes: A(("aria-label", "Search workspace"), ("placeholder", "Search workspace"), ("name", "workspace-query")), styles: InputStyle()),
        L("Your account", "#workspace-account")
    ], styles: Panel(), layout: Row("1.1rem"));

    private static VisualPageElement ContextNavigation() => Section("Record views", "Stay oriented while reviewing a record.",
        Nav("Record breadcrumb", L("Workspace", "#workspace"), T("/"), L("Records", "#records"), T("/"), T("Record 024")),
        Nav("Record views", E("core.link", "Current overview view", "Overview", A(("href", "#record-overview"), ("aria-current", "page")), LinkStyle()),
            L("Activity", "#record-activity"), L("Files", "#record-files"), L("Discussion", "#record-discussion")));

    private static VisualPageElement ReadingFooter() => E("html.footer", "Reading footer", children:
    [
        T("CHAPTER 2 OF 6", "Reading position"), H("A useful checkpoint", 2),
        T("Before continuing, note one idea to carry into the next chapter."),
        Nav("Reading navigation", L("← Previous chapter", "#previous-chapter"), L("All contents", "#work-contents"), L("Next chapter →", "#next-chapter"))
    ], styles: Panel(), layout: Col("1rem", "clamp(1.25rem, 4vw, 3rem)"));

    private static VisualPageElement CaptionedGallery() => Section("Media collection", "Three views from a shared project. Replace the images and captions with your own.",
        Grid("Captioned figures", Figure("Field study", "Image 01 · Context and location"), Figure("Material detail", "Image 02 · Detail and observation"), Figure("Final arrangement", "Image 03 · Outcome and credit")),
        Nav("Media collection links", L("View all media", "#all-media"), L("Image credits", "#media-credits")));

    private static VisualPageElement VideoTranscript() => Section("Watch and read", "A player and companion text for the same material.",
        Grid("Video and chapters", E("core.figure", "Featured video", children:
        [
            E("core.video", "Featured video player", attributes: A(("controls", "true"), ("preload", "none"), ("poster", "assets/placeholder.svg"), ("aria-label", "Featured video; choose a video source")), styles: St(("width", "100%"), ("aspect-ratio", "16 / 9"), ("background", "#132b3b"), ("border-radius", "0.8rem"))),
            E("core.caption", "Video description", "Add a video source, captions, and a descriptive title.", styles: Muted())
        ], styles: St(("margin", "0"))), Card("Chapters", T("00:00 · Introduction"), T("02:15 · Demonstration"), T("05:30 · What to try next"))),
        Disclosure("Read the transcript", "Introduction: explain the subject in a few clear sentences. Demonstration: describe the visible action and key observations. Closing: list the next step and any resources mentioned."));

    private static VisualPageElement AudioPlaylist() => Section("Audio collection", "A selected recording with its notes and related episodes.",
        Grid("Player and playlist", Card("Episode 04 · Making useful connections",
            E("core.audio", "Selected episode player", attributes: A(("controls", "true"), ("preload", "none"), ("aria-label", "Selected episode; choose an audio source")), styles: St(("width", "100%"))),
            T("Add the audio source and replace these sample notes with a short episode summary."), L("Read episode transcript", "#episode-transcript")),
            Card("More recordings", List("Related episodes", "Episode 01 · Begin with a question", "Episode 02 · Gather the evidence", "Episode 03 · Compare and refine"))));

    private static VisualPageElement ResourceGrid() => Section("Shared resources", "A focused collection of useful project documents and tools.",
        Grid("Resource cards", Card("Project brief", Badge("Document"), T("Scope, purpose, and the decisions that guide the work."), L("Read the brief", "#project-brief")),
            Card("Asset library", Badge("Collection"), T("Approved images, downloads, and shared media."), L("Browse assets", "#asset-library")),
            Card("Working checklist", Badge("Tool"), T("A short checklist for preparing the next release."), L("Open checklist", "#working-checklist"))));

    private static VisualPageElement CapabilityGrid() => Section("What you can do", "Arrange application capabilities as a consistent, scannable collection.",
        Grid("Capability cards", Card("Collect", Badge("Available"), T("Keep related records and files in one shared location."), L("Explore records", "#records")),
            Card("Compare", Badge("Available"), T("See changes and differences across a selected set."), L("Open comparison", "#comparison")),
            Card("Publish", Badge("Requires review"), T("Prepare a reviewed view for the people who need it."), L("Review publishing", "#publishing"))));

    private static VisualPageElement ApplicationGrid() => Section("Your applications", "Choose a workspace by the task you need to complete.",
        Grid("Application destinations", AppCard("Library", "Collect and read documents.", "#library"), AppCard("Planner", "Arrange tasks and milestones.", "#planner"),
            AppCard("Reports", "Review totals and changes.", "#reports"), AppCard("Files", "Find shared assets and uploads.", "#files")));

    private static VisualPageElement RecordTable() => Section("Record register", "A compact operational view of the current collection.",
        RowBox("Register summary", Badge("3 sample records"), T("Updated 12 October · Example data"), L("Create record", "#create-record")),
        Table("Current records", ["Record", "Owner", "Status", "Updated"],
            ["Field survey", "M. Rivera", "In review", "12 Oct"], ["Asset inventory", "J. Chen", "Active", "10 Oct"], ["Release notes", "A. Patel", "Draft", "09 Oct"]));

    private static VisualPageElement MetricSummary() => Section("Activity at a glance", "Example totals for the current reporting period; replace with your own values.",
        Grid("Key metrics", Metric("128", "Active records", "+12 this month"), Metric("24", "Reviews completed", "6 awaiting review"),
            Metric("92%", "Completion rate", "Target 90%"), Metric("4 days", "Median turnaround", "Previous period: 5 days")),
        T("Reporting period: 1–30 September · Source: sample project data", "Metric source"));

    private static VisualPageElement RecordDetails() => Section("Record 024 · Field survey", "One record's key facts, supporting notes, and related resources.",
        Grid("Record fact sheet", Card("Details", Definition("Status", "In review", "Owner", "Morgan Rivera", "Created", "02 October", "Reference", "REC-024")),
            Card("Review notes", T("Confirm the source files and document the remaining uncertainty before approving this record."),
                Nav("Related record resources", L("Source files", "#record-files"), L("Change history", "#record-history")))));

    private static VisualPageElement HorizontalBars() => Section("Records by category", "Example counts for a single reporting period. Bar lengths use a scale of 0–80 records.",
        E("core.figure", "Category graph", children:
        [
            Bar("Research", 72, "90%", "#286980"), Bar("Design", 48, "60%", "#4597a0"), Bar("Operations", 32, "40%", "#78b6ae"),
            E("core.caption", "Graph scale", "Scale: 0–80 records · Source: sample data", styles: Muted())
        ], styles: St(("margin", "0")), layout: Col("1rem")),
        Table("Category graph data", ["Category", "Records"], ["Research", "72"], ["Design", "48"], ["Operations", "32"]));

    private static VisualPageElement VerticalBars() => Section("Completed work by period", "A four-period comparison. Each column is scaled against 40 completed items.",
        E("core.figure", "Period column graph", children:
        [
            E("core.container", "Columns", children:
            [Column("Week 1", "18", "45%"), Column("Week 2", "26", "65%"), Column("Week 3", "22", "55%"), Column("Week 4", "34", "85%")],
                styles: St(("height", "16rem"), ("border-bottom", "1px solid #91a5b5")), layout: new() { Display = "grid", GridTemplateColumns = "repeat(4, minmax(0, 1fr))", Gap = "clamp(0.5rem, 3vw, 2rem)", AlignItems = "end" }),
            E("core.caption", "Period graph note", "Example data · One column per week · Maximum axis value: 40", styles: Muted())
        ], styles: St(("margin", "0")), layout: Col("0.8rem")),
        Table("Period graph data", ["Period", "Completed items"], ["Week 1", "18"], ["Week 2", "26"], ["Week 3", "22"], ["Week 4", "34"]));

    private static VisualPageElement CompositionGraph() => Section("Work allocation", "An example composition of 100 planned hours. The labels communicate values as well as color.",
        E("core.figure", "Allocation graph", children:
        [
            E("core.container", "Stacked allocation strip", attributes: A(("role", "img"), ("aria-label", "Research 45 hours, delivery 35 hours, support 20 hours; 100 hours total")), styles: St(("display", "flex"), ("height", "3rem"), ("border-radius", "0.65rem"), ("overflow", "hidden")), children:
            [Swatch("Research share", "45%", "#286980"), Swatch("Delivery share", "35%", "#4597a0"), Swatch("Support share", "20%", "#bbd8d1")]),
            E("core.caption", "Allocation total", "100 planned hours · Research 45% · Delivery 35% · Support 20%", styles: Muted())
        ], styles: St(("margin", "0")), layout: Col("0.8rem")),
        Table("Allocation graph data", ["Work type", "Hours", "Share"], ["Research", "45", "45%"], ["Delivery", "35", "35%"], ["Support", "20", "20%"]));

    private static VisualPageElement RequestIntake() => Section("Start a request", "Collect enough detail to route a request to the right person.",
        Form("Request draft", Field("Your name", "request-name", "html.input.text", "name"), Field("Email address", "request-email", "html.input.email", "email"),
            Field("What do you need?", "request-details", "html.textarea", "details"), T("Draft layout: connect your application's submission action before use."), Button("Prepare request")));

    private static VisualPageElement AccountPreferences() => Section("Account preferences", "Group identity and notification choices in one settings section.",
        Form("Account settings", Grid("Account fields", Field("Display name", "account-display", "html.input.text", "display-name"), Select("Language", "account-language", "English", "Español", "Français")),
            E("html.fieldset", "Notification preferences", children:
            [E("html.legend", "Notification group label", "Notifications"), Check("account-updates", "Project updates"), Check("account-digest", "Weekly summary")], layout: Col("0.75rem"), styles: St(("border", "1px solid #cdd9e3"), ("border-radius", "0.6rem"), ("padding", "1rem"))),
            T("Example settings: add your application's save behavior."), Button("Save preferences")));

    private static VisualPageElement SurveyResponse() => Section("How was this experience?", "A short, focused response form with space for a useful comment.",
        Form("Experience survey", Select("Overall experience", "survey-rating", "Choose a rating", "1 · Difficult", "2 · Needs improvement", "3 · Good", "4 · Very good", "5 · Excellent"),
            Field("What should we improve?", "survey-comment", "html.textarea", "comment"), T("Responses are sample fields until a submission action is connected."), Button("Submit response")));

    private static VisualPageElement ArticleOutline() => Section("A clear starting point", "Explain the subject, then guide the reader through two related ideas.",
        Grid("Article with outline", E("html.article", "Article", children:
        [E("core.heading3", "First idea", "1. Gather what is known", A(("id", "article-gather"))), T("Begin with concrete observations. Record the source and distinguish what you know from what you expect."),
            E("core.heading3", "Second idea", "2. Compare the evidence", A(("id", "article-compare"))), T("Place related observations together. Look for a pattern and note any alternative explanation that still fits.")], layout: Col("1rem")),
            Card("In this section", Nav("Article outline", L("Gather what is known", "#article-gather"), L("Compare the evidence", "#article-compare")), T("Estimated reading time: 3 minutes"))));

    private static VisualPageElement QuestionAnswers() => Section("Questions and answers", "Use native disclosure controls to keep supporting information close at hand.",
        Disclosure("What do I need before starting?", "A named project, its purpose, and one concrete example are enough to begin. Add requirements specific to your application."),
        Disclosure("Can I return to this later?", "Explain the actual save and recovery behavior of your application here."),
        Disclosure("Where can I find more detail?", "Link to the relevant documentation, support page, or next step in your own project."));

    private static VisualPageElement SearchResults() => Section("Search the collection", "A query field and concise results with enough context to choose the right item.",
        RowBox("Search entry", Field("Search terms", "results-query", "html.input.search", "query"), Button("Search")),
        T("3 sample results for “field notes”"), Result("Field notes: observation checklist", "Guide · Updated 12 October", "A practical starting list for repeatable field observations.", "#result-observation"),
        Result("Field survey reference", "Document · Updated 09 October", "Definitions and source references used by the current survey.", "#result-survey"),
        Result("Notes from the first visit", "Record · Updated 02 October", "A short record of location, conditions, and initial findings.", "#result-visit"),
        Nav("Search result pages", L("Previous", "#results-previous"), E("core.inline", "Current result page", "Page 1 of 3"), L("Next", "#results-next")));

    private static VisualPageElement FacetedSearch() => Section("Find a resource", "Combine keyword and type choices with a compact result collection.",
        Grid("Filters and resources", Card("Refine your view", Field("Keyword", "facet-keyword", "html.input.search", "keyword"), Select("Resource type", "facet-type", "All types", "Documents", "Tools", "Media"), Button("Apply filters"), T("Connect filters to your collection data.")),
            E("core.container", "Resource results", children:
            [Result("Project handbook", "Document", "The shared reference for current work.", "#resource-handbook"), Result("Asset browser", "Tool", "Find and organize shared visual material.", "#resource-assets")], layout: Col("1rem"))));

    private static VisualPageElement PlanComparison() => Section("Choose the right capacity", "Example plans for comparing feature sets. Replace all prices and terms before publishing.",
        Grid("Plan cards", Plan("Individual", "$12 / month", "One workspace", "Personal library", "Standard export"),
            Plan("Team", "$29 / month", "Five workspaces", "Shared library", "Review tools"), Plan("Organization", "Custom pricing", "Flexible workspaces", "Managed access", "Dedicated onboarding")));

    private static VisualPageElement OrderSummary() => Section("Review your order", "Check quantities and the final amount before continuing.",
        Table("Order items", ["Item", "Quantity", "Amount"], ["Field notebook", "2", "$24.00"], ["Reference cards", "1", "$8.00"]),
        Grid("Totals and next step", Card("Order total", Definition("Subtotal", "$32.00", "Shipping", "$4.00", "Total", "$36.00")),
            Card("Before you continue", T("Example prices in USD. Add your actual shipping, tax, and payment behavior."), Button("Continue to checkout"))));

    private static VisualPageElement TeamDirectory() => Section("Meet the team", "Give each contributor a clear role and a useful destination.",
        Grid("Team profiles", Person("Morgan Rivera", "Research lead", "Coordinates evidence and source reviews.", "#person-morgan"),
            Person("Jamie Chen", "Design lead", "Turns shared requirements into useful experiences.", "#person-jamie"),
            Person("Ari Patel", "Delivery lead", "Keeps releases focused and well documented.", "#person-ari")));

    private static VisualPageElement ContributorProfile() => Section("Contributor profile", "Connect a person's background to the work they contribute.",
        Grid("Biography and contributions", Person("Morgan Rivera", "Research and documentation", "Morgan focuses on clear records, careful sourcing, and making complex material easier to explore.", "#contributor-contact"),
            Card("Selected contributions", List("Contributions", "Observation guide · Lead author", "Project handbook · Contributor", "Release review · Reviewer"), L("All contributions", "#contributor-work"))));

    private static VisualPageElement NoticeCenter() => Section("Notifications", "Text labels distinguish the message purpose without relying on color.",
        Notice("Action required", "A review is waiting", "12 October · Review the updated project brief before the next milestone.", "#review-brief"),
        Notice("Information", "A new resource is available", "11 October · The shared asset guide has been updated.", "#asset-guide"),
        Notice("Completed", "Your export is ready", "10 October · The reviewed report is available in the downloads area.", "#report-download"));

    private static VisualPageElement ServiceStatus() => Section("Service status", "An example availability summary with explicit component states.",
        Grid("Component states", Card("Library", Badge("Operational"), T("Last checked: 12 October, 09:00")), Card("Search", Badge("Operational"), T("Last checked: 12 October, 09:00")), Card("Exports", Badge("Scheduled maintenance"), T("Next window: 13 October, 02:00–03:00"))),
        H("Recent updates", 3), List("Status history", "12 October, 09:00 · All primary services available", "11 October, 16:30 · Export queue returned to normal"),
        T("Static example: connect this section to your own service monitoring source."));

    private static VisualPageElement TaskBoard() => Section("Work in progress", "Arrange tasks by their current stage and make ownership visible.",
        Grid("Task stages", Card("Planned · 2", Task("Review source list", "Morgan · Due 14 Oct"), Task("Prepare asset labels", "Jamie · Due 15 Oct")),
            Card("In progress · 1", Task("Draft the field guide", "Ari · Due 16 Oct")), Card("Complete · 1", Task("Agree on scope", "Team · Completed 10 Oct"))));

    private static VisualPageElement MilestoneTimeline() => Section("Project milestones", "A dated sequence keeps outcomes, status, and next actions together.",
        E("core.ordered-list", "Milestone sequence", children:
        [Milestone("01 · Scope agreed", "10 October · Complete", "The shared brief defines the audience and the first useful outcome."),
            Milestone("02 · Working draft", "17 October · In progress", "Prepare a reviewable draft and gather specific feedback."),
            Milestone("03 · Review and release", "24 October · Planned", "Resolve the review notes and document the release decision.")],
            styles: St(("margin", "0"), ("padding-left", "1.5rem")), layout: Col("1rem")), L("Review project plan", "#project-plan"));

    private static VisualPageElement Section(string title, string summary, params VisualPageElement[] children) => E("core.section", title,
        children: new[] { H(title, 2), T(summary, title + " introduction") }.Concat(children), styles: St(("color", "#172b3a"), ("background", "#f5f7fa"), ("border-radius", "1rem"), ("font-family", "Segoe UI, sans-serif")),
        layout: Col("1.3rem", "clamp(1.25rem, 4vw, 3rem)"));
    private static VisualPageElement H(string title, int level = 3) => E("core.heading" + level, title + " heading", title, styles: St(("margin", "0"), ("line-height", "1.2"), ("font-size", level == 2 ? "clamp(1.6rem, 3vw, 2.4rem)" : "1.15rem")));
    private static VisualPageElement T(string text, string? name = null) => E("core.paragraph", name ?? "Supporting text", text, styles: St(("margin", "0"), ("line-height", "1.65"), ("color", "#4a6171")));
    private static VisualPageElement L(string text, string href) => E("core.link", text + " link", text, A(("href", href)), LinkStyle());
    private static VisualPageElement Nav(string name, params VisualPageElement[] links) => E("core.nav", name, attributes: A(("aria-label", name)), children: links, layout: Row("0.9rem"));
    private static VisualPageElement Card(string title, params VisualPageElement[] content) => E("core.container", title + " panel", children: new[] { H(title) }.Concat(content), styles: Panel(), layout: Col("0.85rem", "1.2rem"));
    private static VisualPageElement Grid(string name, params VisualPageElement[] content) => E("core.grid", name, children: content, layout: new() { Display = "grid", GridTemplateColumns = "repeat(auto-fit, minmax(min(100%, 14rem), 1fr))", Gap = "1rem", AlignItems = "start" });
    private static VisualPageElement RowBox(string name, params VisualPageElement[] content) => E("core.container", name, children: content, layout: Row("1rem"));
    private static VisualPageElement Badge(string text) => E("core.inline", text + " status", text, styles: St(("display", "inline-block"), ("width", "fit-content"), ("padding", "0.3rem 0.6rem"), ("background", "#e0edf0"), ("color", "#24546a"), ("border-radius", "0.4rem"), ("font-size", "0.8rem"), ("font-weight", "650")));
    private static VisualPageElement Figure(string name, string caption) => E("core.figure", name + " figure", children:
    [E("core.image", name + " image", attributes: A(("src", "assets/placeholder.svg"), ("alt", name + " sample image; replace with your own media"), ("loading", "lazy")), styles: St(("width", "100%"), ("aspect-ratio", "4 / 3"), ("object-fit", "cover"), ("border-radius", "0.7rem"), ("background", "#dce6ed"))),
        E("core.caption", name + " caption", caption, styles: Muted())], styles: St(("margin", "0")), layout: Col("0.6rem"));
    private static VisualPageElement AppCard(string title, string description, string href) => Card(title, T(description), L("Open " + title, href));
    private static VisualPageElement Metric(string value, string label, string note) => E("core.container", label + " metric", children:
        [T(label, "Metric label"), E("core.paragraph", label + " value", value, styles: St(("font-size", "2.2rem"), ("font-weight", "700"), ("margin", "0"), ("color", "#1d526b"))), T(note, "Metric comparison")], styles: Panel(), layout: Col("0.55rem", "1.2rem"));
    private static VisualPageElement Definition(params string[] pairs) => E("html.dl", "Record fields", children: pairs.Select((value, index) => E(index % 2 == 0 ? "html.dt" : "html.dd", "Field " + (index / 2 + 1) + (index % 2 == 0 ? " label" : " value"), value, styles: St(("margin", "0"), ("font-weight", index % 2 == 0 ? "650" : "400"), ("padding-bottom", index % 2 == 0 ? "0" : "0.8rem")))), layout: Col("0.2rem"), styles: St(("margin", "0")));
    private static VisualPageElement Table(string caption, string[] headers, params string[][] rows) => E("core.container", caption + " scroll area", styles: St(("overflow-x", "auto"), ("width", "100%")), children:
    [E("html.table", caption + " table", styles: St(("width", "100%"), ("border-collapse", "collapse"), ("text-align", "left"), ("font-size", "0.9rem")), children:
        [E("html.caption", caption + " caption", caption, styles: St(("text-align", "left"), ("font-weight", "650"), ("padding", "0.75rem 0"))),
            E("html.thead", "Table heading group", children: [E("html.tr", "Column labels", children: headers.Select(text => E("html.th", text + " column", text, A(("scope", "col")), Cell(true))))]),
            E("html.tbody", "Table records", children: rows.Select((row, index) => E("html.tr", "Record row " + (index + 1), children: row.Select((text, column) => E(column == 0 ? "html.th" : "html.td", "Row " + (index + 1) + " " + headers[column], text, column == 0 ? A(("scope", "row")) : null, Cell(column == 0))))))])]);
    private static VisualPageElement Bar(string label, int value, string width, string color) => E("core.container", label + " graph row", children:
        [RowBox(label + " graph label", T(label), E("core.inline", label + " graph value", value.ToString())), E("core.container", label + " graph track", children:
            [E("core.container", label + " bar", styles: St(("width", width), ("height", "0.8rem"), ("background", color), ("border-radius", "0.4rem")))], styles: St(("background", "#e1e8ee"), ("border-radius", "0.4rem")))], layout: Col("0.4rem"));
    private static VisualPageElement Column(string label, string value, string height) => E("core.container", label + " column", children:
        [E("core.inline", label + " value", value), E("core.container", label + " bar", styles: St(("height", height), ("min-height", "1rem"), ("width", "100%"), ("background", "#398098"), ("border-radius", "0.4rem 0.4rem 0 0"))), E("core.inline", label + " label", label)],
        styles: St(("height", "100%"), ("text-align", "center")), layout: new() { Display = "flex", FlexDirection = "column", JustifyContent = "flex-end", Gap = "0.4rem", AlignItems = "center" });
    private static VisualPageElement Swatch(string name, string width, string color) => E("core.container", name, styles: St(("width", width), ("background", color)));
    private static VisualPageElement Disclosure(string question, string answer) => E("html.details", question + " disclosure", children:
        [E("html.summary", question + " question", question, styles: St(("cursor", "pointer"), ("font-weight", "650"))), T(answer, question + " answer")], styles: Panel(), layout: Col("0.8rem", "1rem"));
    private static VisualPageElement Form(string name, params VisualPageElement[] fields) => E("html.form", name, attributes: A(("aria-label", name)), children: fields, layout: Col("1rem"), styles: St(("max-width", "48rem")));
    private static VisualPageElement Field(string label, string id, string descriptor, string name) => E("core.container", label + " field", children:
        [E("core.label", label + " label", label, A(("for", id)), St(("font-weight", "600"))), E(descriptor, label + " input", attributes: A(("id", id), ("name", name), ("aria-label", label), ("placeholder", descriptor == "html.textarea" ? "Write your response" : label)), styles: InputStyle())], layout: Col("0.4rem"));
    private static VisualPageElement Select(string label, string id, params string[] options) => E("core.container", label + " field", children:
        [E("core.label", label + " label", label, A(("for", id)), St(("font-weight", "600"))), E("html.select", label + " selector", attributes: A(("id", id), ("name", id), ("aria-label", label)), styles: InputStyle(), children: options.Select((text, i) => E("core.option", text + " option", text, A(("value", i.ToString())))))], layout: Col("0.4rem"));
    private static VisualPageElement Check(string id, string text) => RowBox(text + " option", E("html.input.checkbox", text + " checkbox", attributes: A(("id", id), ("name", id), ("aria-label", text))), E("core.label", text + " label", text, A(("for", id))));
    private static VisualPageElement Button(string text) => E("core.button", text + " action", text, A(("type", "button")), St(("width", "fit-content"), ("padding", "0.75rem 1rem"), ("background", "#275d77"), ("color", "#ffffff"), ("border", "0"), ("border-radius", "0.6rem"), ("font-weight", "650")));
    private static VisualPageElement List(string name, params string[] items) => E("core.list", name, children: items.Select(text => E("core.list-item", text + " item", text)), styles: St(("margin", "0"), ("padding-left", "1.25rem")), layout: Col("0.6rem"));
    private static VisualPageElement Result(string title, string meta, string text, string href) => E("core.container", title + " result", children: [L(title, href), T(meta, title + " metadata"), T(text, title + " excerpt")], styles: Panel(), layout: Col("0.4rem", "1rem"));
    private static VisualPageElement Plan(string name, string price, params string[] features) => Card(name, E("core.paragraph", name + " price", price, styles: St(("font-size", "1.8rem"), ("font-weight", "700"), ("margin", "0"))), List(name + " features", features), Button("Choose " + name));
    private static VisualPageElement Person(string name, string role, string bio, string href) => Card(name, Badge(role), T(bio), L("View " + name, href));
    private static VisualPageElement Notice(string kind, string title, string detail, string href) => E("core.container", title + " notice", children: [Badge(kind), H(title), T(detail), L("View details", href)], styles: Panel(), layout: Col("0.65rem", "1rem"));
    private static VisualPageElement Task(string title, string detail) => E("core.container", title + " task", children: [H(title), T(detail)], styles: St(("background", "#eff4f7"), ("border-left", "3px solid #4597a0"), ("border-radius", "0.4rem")), layout: Col("0.5rem", "0.85rem"));
    private static VisualPageElement Milestone(string title, string status, string detail) => E("core.list-item", title + " milestone", children: [H(title), Badge(status), T(detail)], styles: Panel(), layout: Col("0.6rem", "1rem"));

    private static VisualPageElement E(string descriptor, string name, string text = "", Dictionary<string, string>? attributes = null,
        Dictionary<string, string>? styles = null, IEnumerable<VisualPageElement>? children = null, VisualPageLayoutSpec? layout = null) => new()
    {
        DescriptorId = descriptor, Name = name, Text = text, Attributes = attributes ?? new(StringComparer.Ordinal), Styles = styles ?? new(StringComparer.Ordinal),
        Children = children?.ToList() ?? [], Layout = layout is null ? [] : new() { [VisualPageBreakpoint.Base] = layout }
    };
    private static VisualPageLayoutSpec Row(string gap) => new() { Display = "flex", FlexDirection = "row", FlexWrap = "wrap", Gap = gap, AlignItems = "center", JustifyContent = "space-between" };
    private static VisualPageLayoutSpec Col(string gap, string padding = "") => new() { Display = "flex", FlexDirection = "column", Gap = gap, Padding = padding };
    private static Dictionary<string, string> A(params (string Name, string Value)[] pairs) => pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
    private static Dictionary<string, string> St(params (string Name, string Value)[] pairs) => pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
    private static Dictionary<string, string> Panel() => St(("background", "#ffffff"), ("border", "1px solid #d3dfe7"), ("border-radius", "0.8rem"), ("min-width", "0"));
    private static Dictionary<string, string> Muted() => St(("font-size", "0.85rem"), ("line-height", "1.6"), ("color", "#526b7b"));
    private static Dictionary<string, string> LinkStyle() => St(("color", "#215f7c"), ("font-weight", "650"), ("text-underline-offset", "0.2em"));
    private static Dictionary<string, string> InputStyle() => St(("box-sizing", "border-box"), ("width", "100%"), ("min-width", "0"), ("padding", "0.75rem"), ("background", "#ffffff"), ("color", "#172b3a"), ("border", "1px solid #a9bdcc"), ("border-radius", "0.5rem"), ("font", "inherit"));
    private static Dictionary<string, string> Cell(bool header) => St(("padding", "0.8rem"), ("border-bottom", "1px solid #d3dfe7"), ("background", header ? "#eaf1f5" : "#ffffff"), ("font-weight", header ? "650" : "400"));
}
