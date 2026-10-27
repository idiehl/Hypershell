using System.IO;
using System.Text;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

/// <summary>
/// Portable, field-editable counterparts of the actual Atlas public, Archives and Admin interfaces.
/// The seed owns only its staging projects; registering never replaces an existing Design Group.
/// </summary>
public static class HyperlibAtlasWebsiteCatalog
{
    private sealed record Module(string Id, string Title, string Category, string Source, string Markup,
        List<FieldDefinition> Fields, string Code = "")
    {
        public string Component => "Atlas" + string.Concat(Id.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Block";
    }
    private sealed record Group(string Id, string Title, string Description, string[] Modules, string[] Layout);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static int ComponentCount => Modules.Count;
    public static IReadOnlyList<string> GroupIds => Groups.Select(group => group.Id).ToArray();

    public static async Task EnsureAsync(string libraryRoot, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            libraryRoot = Path.GetFullPath(libraryRoot);
            foreach (var group in Groups)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(Path.Combine(libraryRoot, TemplateObjectLibrary.DirectoryName, "DesignGroups", group.Id)))
                {
                    _ = TemplateObjectLibrary.GetDesignGroup(libraryRoot, group.Id);
                    continue;
                }
                var project = Path.Combine(libraryRoot, ".hyperlib", "catalog-seeds", "atlas-website-v1", group.Id);
                await TemplateProjectScaffolder.EnsureAsync(libraryRoot, project, group.Id, group.Title, ct);
                var modules = group.Modules.Select(id => Modules.Single(module => module.Id == id)).ToArray();
                var definition = new TemplateDefinition
                {
                    TemplateId = group.Id, Title = group.Title,
                    Regions = [new() { Id = "main", Title = "Page", AllowedBlocks = modules.Select(module => module.Id).ToList() }],
                    Blocks = modules.Select(module => new BlockDefinition
                    {
                        Id = module.Id, Title = module.Title, Component = module.Component, Fields = module.Fields,
                        CatalogId = "HLC-ATLAS-" + module.Id.ToUpperInvariant(), Category = module.Category,
                        Description = "Portable " + module.Title + ". Editable content, local behavior and Atlas styling. Source: " + module.Source,
                        SourceLibrary = "Atlas Universalis website", SourceTemplateId = "atlas.website." + module.Id + ".v1"
                    }).ToList(),
                    Appearance = Appearance(),
                    DefaultBlocks = group.Layout.Select((id, index) => new PageBlock
                    {
                        Id = "atlas-section-" + index, Type = id, Region = "main",
                        Values = Modules.Single(module => module.Id == id).Fields.Where(field => field.DefaultValue.HasValue)
                            .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal)
                    }).ToList()
                };
                TemplateValidator.ValidateDefinition(definition);
                await WriteAsync(project, "authoring.template.json", JsonSerializer.Serialize(definition, ContentJson.Options), ct);
                await WriteAsync(project, "src/Pages/Atlas/AtlasSurface.razor", Surface, ct);
                await WriteAsync(project, "src/Pages/Atlas/AtlasSurface.razor.css", Css, ct);
                await WriteAsync(project, "src/Pages/Atlas/_Imports.razor", "@using HyperLib.Work.Atlas\n", ct);
                await WriteAsync(project, "src/Pages/wwwroot/atlas/atlas.js", JavaScript, ct);
                foreach (var module in modules)
                    await WriteAsync(project, "src/Pages/Atlas/" + module.Component + ".razor", RenderModule(module), ct);
                foreach (var image in new[] { "atlas-emblem-96.png", "atlas-emblem-512.png" })
                {
                    var source = Path.Combine(AppContext.BaseDirectory, "Assets", "HyperlibCatalog", image);
                    if (!File.Exists(source)) throw new FileNotFoundException("The bundled Atlas Design Group artwork is missing.", source);
                    var destination = Path.Combine(project, "src", "Pages", "wwwroot", "atlas", image);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(source, ct), ct);
                }
                await WriteAsync(project, "ATLAS-PROVENANCE.md", Provenance + "\n\nThis group contains:\n" +
                    string.Join('\n', modules.Select(module => "- HLC-ATLAS-" + module.Id.ToUpperInvariant() + ": " + module.Source)), ct);
                await TemplateObjectLibrary.RegisterDesignGroupAsync(libraryRoot, project, group.Id, group.Title,
                    group.Description, "Atlas website", "atlas.website.v1", ct);
            }
        }
        finally { Gate.Release(); }
    }

    private static Task WriteAsync(string root, string relative, string value, CancellationToken ct)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, value, new UTF8Encoding(false), ct);
    }
    private static List<FieldDefinition> Appearance() =>
    [
        F("background", "Page color", "#070d1a", "color"), F("surface", "Panel color", "#101b35", "color"),
        F("ink", "Text color", "#e8ecf1", "color"), F("muted", "Secondary text", "#bcc5d0", "color"),
        F("accent", "Accent", "#c8963e", "color"),
        new() { Id = "font", Title = "Body font", Type = "select", Options = StarterTemplate.SafeFonts.ToList(), DefaultValue = ContentJson.Value("geometric") },
        new() { Id = "headingFont", Title = "Heading font", Type = "select", Options = StarterTemplate.SafeFonts.ToList(), DefaultValue = ContentJson.Value("geometric") },
        StandardAppearance.FontSize()
    ];
    private static FieldDefinition F(string id, string title, string value, string type = "text")
        => new() { Id = id, Title = title, Type = type, DefaultValue = ContentJson.Value(value) };
    private static Module M(string id, string title, string category, string source, string markup, List<FieldDefinition> fields, string code = "")
        => new(id, title, category, source, markup, fields, code);
    private static string RenderModule(Module module) => "@namespace HyperLib.Work.Atlas\n<AtlasSurface>\n" + module.Markup + "\n</AtlasSurface>\n@code {\n" +
        "[Parameter, EditorRequired] public BlockContext Context { get; set; } = new();\n" +
        "private string T(string name, string fallback = \"\") => Context.Text(name, fallback);\n" +
        "private string[] Lines(string name) => T(name).Split('\\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(100).ToArray();\n" +
        "private static string Part(string value, int index) { var parts = value.Split('|', StringSplitOptions.TrimEntries); return parts.Length > index ? parts[index] : string.Empty; }\n" +
        "private static string Link(string value) => value.StartsWith('#') || value.StartsWith(\"./\", StringComparison.Ordinal) || (value.StartsWith('/') && !value.StartsWith(\"//\", StringComparison.Ordinal)) || (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is \"http\" or \"https\" or \"mailto\") ? value : \"#\";\n" +
        module.Code + "\n}\n";

    private static readonly string[] PublicModules = ["navigation", "hero", "starfield", "map-grid", "emblem", "section-heading", "principles", "principle-card", "services", "service-card", "action-banner", "button-bar", "footer"];
    private static readonly string[] PeopleModules = ["navigation", "section-heading", "portrait", "timeline", "timeline-entry", "social-links", "project-card", "project-grid", "command-block", "footer"];
    private static readonly Group[] Groups =
    [
        new("atlas-website-home", "Atlas · Home & identity", "The current Atlas home: understated navigation, celestial hero, core principles and shared identity.", PublicModules, ["navigation", "hero", "principles", "footer"]),
        new("atlas-website-services", "Atlas · Services", "Service cards, an editorial introduction and a clear next action in the Atlas visual language.", PublicModules, ["navigation", "section-heading", "services", "action-banner", "footer"]),
        new("atlas-website-bio", "Atlas · People & biography", "Portrait, biographical timeline and contact links adapted from the current Bio page.", PeopleModules, ["navigation", "portrait", "timeline", "social-links", "footer"]),
        new("atlas-website-projects", "Atlas · Project Lab", "Project cards with source and launch links, status labels and copyable build commands.", PeopleModules, ["navigation", "section-heading", "project-grid", "command-block", "footer"]),
        new("atlas-website-tools", "Atlas · Forms & planning", "Local scope planning, accessible forms and result panels based on LaunchScope.", ["navigation", "scope-planner", "risk-badge", "result-summary", "form-field", "contact-form", "footer"], ["navigation", "scope-planner", "footer"]),
        new("atlas-admin-interface", "Atlas · Admin workspace", "The Admin shell, status strips, inventories, forms and approval patterns, using local demonstration data.", ["admin-header", "status-strip", "metric", "module-card", "inventory", "inventory-row", "notice", "empty-state", "login-form", "code-entry", "approval", "inline-editor", "download-card", "footer"], ["admin-header", "status-strip", "module-card", "inventory", "notice", "footer"]),
        new("atlas-public-archives", "Atlas · Archives reader", "The current public Archives hierarchy, search, cards, breadcrumbs and reader layout with editable local data.", ["navigation", "breadcrumbs", "search-tree", "collection-card", "collection-grid", "reader-heading", "reader-workspace", "footer"], ["navigation", "breadcrumbs", "reader-workspace", "footer"])
    ];

    private static readonly List<Module> Modules =
    [
        M("navigation", "Atlas navigation", "Structure & navigation", "Atlas.Web/Pages/Shared/_Layout.cshtml:24; site.css:93", """
            <header class="site-header" data-atlas-header>
              <a class="brand" href="@Link(T("home"))"><img src="./_content/HyperLib.Work/atlas/atlas-emblem-96.png" width="32" height="32" alt="" /><span data-field="title">@T("title")</span></a>
              <nav class="desktop-nav" aria-label="Primary">@foreach(var line in Lines("links")) { <a href="@Link(Part(line,1))">@Part(line,0)</a> }</nav>
              <details class="mobile-nav"><summary>Menu</summary><nav aria-label="Primary">@foreach(var line in Lines("links")) { <a href="@Link(Part(line,1))">@Part(line,0)</a> }</nav></details>
            </header>
            """, [F("title", "Brand", "Atlas Universalis"), F("home", "Home link", "#"), F("links", "Links · label | address", "Home|#\nPrinciples|#principles\nServices|#services\nBio|#bio\nProject Lab|#projects\nArchives|#archives")]),
        M("hero", "Atlas celestial hero", "Headers & heroes", "Atlas.Web/Pages/Index.cshtml:7; site.css:226", """
            <section class="landing-hero">
              <div class="hero-background" aria-hidden="true"><div class="starfield"></div><div class="map-grid"></div><img class="hero-emblem" src="./_content/HyperLib.Work/atlas/atlas-emblem-512.png" alt="" /></div>
              <div class="hero-content"><h1 data-field="title">@T("title")</h1><div class="star-divider" aria-hidden="true">✦</div><p class="eyebrow" data-field="kicker">@T("kicker")</p><p class="lede" data-field="text">@T("text")</p></div>
              <a class="scroll-cue" href="@Link(T("href"))" aria-label="Continue to page content">↓</a>
            </section>
            """, [F("title", "Title", "ATLAS UNIVERSALIS"), F("kicker", "Kicker", "Custom software strategy & delivery"), F("text", "Introduction", "We build custom solutions around the unique challenges our clients face."), F("href", "Next section", "#principles")]),
        M("starfield", "Atlas animated starfield", "Decoration", "Atlas.Web/wwwroot/css/site.css:292-336", """
            <section class="decoration-preview"><div class="starfield" aria-hidden="true"></div><p class="eyebrow" data-field="title">@T("title")</p><h2 data-field="text">@T("text")</h2></section>
            """, [F("title", "Overline", "Celestial field"), F("text", "Foreground text", "A little room for wonder.")]),
        M("map-grid", "Atlas cartographic backdrop", "Decoration", "Atlas.Web/wwwroot/css/site.css:243-290; Admin/admin.css:84", """
            <section class="decoration-preview"><div class="map-grid" aria-hidden="true"></div><p class="eyebrow" data-field="title">@T("title")</p><h2 data-field="text">@T("text")</h2></section>
            """, [F("title", "Overline", "Maps & connections"), F("text", "Foreground text", "Find the shape of an idea.")]),
        M("emblem", "Atlas emblem", "Identity & people", "Atlas.Web/wwwroot/brand/icon-512.png; _Layout.cshtml:31", """
            <figure class="emblem-panel"><img src="./_content/HyperLib.Work/atlas/atlas-emblem-512.png" width="256" height="256" alt="@T("alt")" /><figcaption data-field="title">@T("title")</figcaption></figure>
            """, [F("title", "Caption", "Atlas Universalis"), F("alt", "Image description", "Atlas Universalis emblem")]),
        M("section-heading", "Atlas editorial heading", "Text & content", "Atlas.Web/Pages/Services.cshtml:8; Bio.cshtml:9; ProjectLab.cshtml:10", """
            <header class="page-heading"><p class="eyebrow" data-field="kicker">@T("kicker")</p><h1 data-field="title">@T("title")</h1><p class="lede" data-field="text">@T("text")</p></header>
            """, [F("kicker", "Kicker", "Explore Atlas"), F("title", "Heading", "Precision, creativity, and execution."), F("text", "Introduction", "A clear point of view, with space to explore the details.")]),
        M("principles", "Atlas principles grid", "Content blocks", "Atlas.Web/Pages/Index.cshtml:39-59; site.css:497", """
            <section id="principles" class="content-section"><p class="eyebrow" data-field="kicker">@T("kicker")</p><h2 data-field="title">@T("title")</h2><p class="lede" data-field="text">@T("text")</p><div class="three-grid">@foreach(var line in Lines("items")) { <article class="principle-card"><h3>@Part(line,0)</h3><p>@Part(line,1)</p></article> }</div></section>
            """, [F("kicker", "Kicker", "Core values"), F("title", "Title", "Many challenges. One solution mindset."), F("text", "Introduction", "Research, insight, analysis, and close observation of the problem itself."), F("items", "Principles · title | description", "Precision|Understand the problem before choosing the tools.\nCreativity|Look beyond the obvious answer.\nExecution|Build a useful result, then refine it.")]),
        M("principle-card", "Atlas principle card", "Cards & collections", "Atlas.Web/Pages/Index.cshtml:47; site.css:550", """
            <article class="principle-card"><p class="eyebrow" data-field="kicker">@T("kicker")</p><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p></article>
            """, [F("kicker", "Number or label", "01"), F("title", "Title", "Start with understanding"), F("text", "Description", "Observe the real problem and the people working with it.")]),
        M("services", "Atlas services grid", "Cards & collections", "Atlas.Web/Pages/Services.cshtml:17-59; site.css:933", """
            <section id="services" class="content-section"><h2 data-field="title">@T("title")</h2><div class="two-grid">@foreach(var line in Lines("items")) { <article class="service-card"><h3>@Part(line,0)</h3><p>@Part(line,1)</p></article> }</div></section>
            """, [F("title", "Title", "Systems that fit reality"), F("items", "Services · title | description", "Software strategy|Translate an operating challenge into a focused technical plan.\nCustom applications|Build software around the way your team works.\nWorkflow automation|Connect the repeated steps that consume time.\nData & insight|Make complex information useful and understandable.\nInfrastructure|Create a reliable foundation for delivery.\nLong-term support|Keep improving as the work evolves.")]),
        M("service-card", "Atlas service card", "Cards & collections", "Atlas.Web/Pages/Services.cshtml:18; site.css:939", """
            <article class="service-card"><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p><a class="text-link" href="@Link(T("href"))" data-field="label">@T("label") →</a></article>
            """, [F("title", "Title", "Custom applications"), F("text", "Description", "Purpose-built tools for the way your team actually works."), F("label", "Link label", "Explore the service"), F("href", "Link", "#services")]),
        M("action-banner", "Atlas action banner", "Actions & conversion", "Atlas.Web/Pages/Services.cshtml:62-69; site.css:961", """
            <aside class="action-banner"><div><h2 data-field="title">@T("title")</h2><p data-field="text">@T("text")</p></div><a class="primary" href="@Link(T("href"))" data-field="label">@T("label")</a></aside>
            """, [F("title", "Title", "Bring us the challenge."), F("text", "Description", "Start with the outcome you need and the constraints you face."), F("label", "Action", "Start a conversation"), F("href", "Address", "#contact")]),
        M("button-bar", "Atlas action pair", "Actions & conversion", "Atlas.Web/wwwroot/css/site.css:172-212; LaunchScope.cshtml:81", """
            <div class="actions"><a class="primary" href="@Link(T("href"))" data-field="title">@T("title")</a><a class="secondary" href="@Link(T("secondaryHref"))" data-field="secondary">@T("secondary")</a></div>
            """, [F("title", "Primary label", "Explore"), F("href", "Primary address", "#content"), F("secondary", "Secondary label", "Learn more"), F("secondaryHref", "Secondary address", "#details")]),
        M("footer", "Atlas page footer", "Structure & navigation", "HyperLib.Archives/wwwroot/index.html:65; archives.css:264", """
            <footer class="atlas-footer"><span data-field="title">@T("title")</span><span data-field="text">@T("text")</span></footer>
            """, [F("title", "Name", "Atlas Universalis"), F("text", "Footer note", "Created in Hyperlib. Shared with the world.")]),
        M("portrait", "Atlas portrait introduction", "Identity & people", "Atlas.Web/Pages/Bio.cshtml:16-33; site.css:609", """
            <section id="bio" class="portrait"><img src="@(string.IsNullOrWhiteSpace(T("src")) ? "./_content/HyperLib.Work/atlas/atlas-emblem-512.png" : Link(T("src")))" alt="@T("alt")" /><div><p class="eyebrow" data-field="kicker">@T("kicker")</p><h2 data-field="title">@T("title")</h2><p data-field="text">@T("text")</p></div></section>
            """, [F("kicker", "Role", "Founder biography"), F("title", "Name", "Introduce the person behind the work"), F("text", "Biography", "Connect experience, interests, and the work you are building today."), F("src", "Portrait", "", "media"), F("alt", "Portrait description", "Atlas emblem; replace with your own portrait")]),
        M("timeline", "Atlas biography timeline", "Text & content", "Atlas.Web/Pages/Bio.cshtml:35-65; site.css:688", """
            <section class="panel"><h2 data-field="title">@T("title")</h2><ol class="timeline">@foreach(var line in Lines("items")) { <li><strong>@Part(line,0)</strong><span>@Part(line,1)</span><p>@Part(line,2)</p></li> }</ol></section>
            """, [F("title", "Title", "Experience & direction"), F("items", "Entries · date | heading | description", "Beginning|Learn the foundations|Develop a careful understanding of systems.\nExploration|Connect disciplines|Bring creative and technical perspectives together.\nToday|Build useful things|Turn ideas into tools people can use.")]),
        M("timeline-entry", "Atlas timeline entry", "Text & content", "Atlas.Web/Pages/Bio.cshtml:38; site.css:696", """
            <article class="timeline-entry"><strong data-field="date">@T("date")</strong><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p></article>
            """, [F("date", "Date", "2026"), F("title", "Heading", "A new chapter"), F("text", "Description", "Describe the milestone and why it mattered.")]),
        M("social-links", "Atlas contact links", "Identity & people", "Atlas.Web/Pages/Bio.cshtml:67-104; site.css:724", """
            <section id="contact" class="panel"><h2 data-field="title">@T("title")</h2><ul class="social-links">@foreach(var line in Lines("items")) { <li><a href="@Link(Part(line,1))"><span aria-hidden="true">↗</span>@Part(line,0)</a></li> }</ul></section>
            """, [F("title", "Heading", "Connect"), F("items", "Links · label | address", "Email|mailto:hello@example.com\nPortfolio|#projects\nAbout|#bio")]),
        M("project-card", "Atlas project card", "Cards & collections", "Atlas.Web/Pages/ProjectLab.cshtml:21-37; site.css:792", """
            <article class="project-card"><div class="card-heading"><h3 data-field="title">@T("title")</h3><span class="badge" data-field="status">@T("status")</span></div><p data-field="text">@T("text")</p><div class="actions"><a class="secondary" href="@Link(T("source"))">View source</a><a class="primary" href="@Link(T("launch"))">Explore project</a></div><pre><code data-field="command">@T("command")</code></pre><button type="button" class="quiet" data-atlas-copy>Copy command</button><span role="status" data-copy-status></span></article>
            """, [F("title", "Project", "A useful project"), F("status", "Status", "In development"), F("text", "Summary", "Explain the problem this project solves and how to get started."), F("source", "Source link", "#source"), F("launch", "Project link", "#project"), F("command", "Build command", "dotnet run")]),
        M("project-grid", "Atlas Project Lab grid", "Cards & collections", "Atlas.Web/Pages/ProjectLab.cshtml:20-116; site.css:785", """
            <section id="projects" class="content-section"><h2 data-field="title">@T("title")</h2><div class="two-grid">@foreach(var line in Lines("items")) { <article class="project-card"><div class="card-heading"><h3>@Part(line,0)</h3><span class="badge">@Part(line,1)</span></div><p>@Part(line,2)</p><a class="text-link" href="@Link(Part(line,3))">Explore project →</a></article> }</div></section>
            """, [F("title", "Heading", "Explore apps, codebases, and build paths."), F("items", "Projects · title | status | summary | address", "Atlas Forge|Design laboratory|A workspace for exploring interface ideas.|#forge\nAtlas Meridian|Desktop workspace|Map systems and ideas with rich content.|#meridian\nAtlas Apply|Applications|Organize an application workflow.|#apply\nInfrastructure|Platform|Build a reliable delivery foundation.|#platform")]),
        M("command-block", "Atlas copyable command", "Code & reference", "Atlas.Web/Pages/ProjectLab.cshtml:34-36; site.css:858", """
            <section class="command-panel"><div class="card-heading"><h3 data-field="title">@T("title")</h3><button class="quiet" type="button" data-atlas-copy>Copy</button></div><pre><code data-field="code">@T("code")</code></pre><span role="status" data-copy-status></span><p data-field="text">@T("text")</p></section>
            """, [F("title", "Heading", "Get started"), F("code", "Command or code", "dotnet restore\ndotnet run"), F("text", "Explanation", "Run these commands in the project directory.")]),
        M("scope-planner", "Atlas launch scope planner", "Widgets & tools", "Atlas.Web/Pages/LaunchScope.cshtml; LaunchScope.cshtml.cs:33-155", """
            <section class="content-section"><p class="eyebrow">Launch scope</p><h2 data-field="title">@T("title")</h2><p data-field="text">@T("text")</p><div class="two-grid"><form class="panel form-stack" @onsubmit="Plan" @onsubmit:preventDefault><label>Tool name<input required maxlength="80" @bind="_name" /></label><label>Primary audience<input required maxlength="120" @bind="_audience" /></label><label>Problem to solve<textarea required maxlength="240" @bind="_problem"></textarea></label><label>Core input<input required maxlength="140" @bind="_input" /></label><label>Expected output<input required maxlength="140" @bind="_output" /></label><label>Experience mode<select @bind="_mode"><option value="public">Public with no sign-in</option><option value="capture">Optional lead capture</option><option value="signin">Signed-in experience</option><option value="saved">Saved account data</option></select></label><button class="primary" type="submit">Build scope</button></form><div class="panel" aria-live="polite">@if(!_planned) { <h3>Your scope, in one place</h3><p>Describe one input and one useful result. The plan is calculated locally.</p> } else { <span class="badge">@Risk</span><h3>@_name</h3><p>@_problem</p><h4>Build first</h4><ul><li>One public page for @_audience.</li><li>Ask for @_input and return @_output.</li><li>Add a worked example and one clear next action.</li></ul><h4>Keep for later</h4><ul><li>Saved histories and personalized dashboards.</li><li>Integrations, automation and background processing.</li>@if(_mode is "signin" or "saved") { <li>Mandatory sign-in before the first useful result.</li> }</ul><p class="notice">@Delivery</p> }</div></div></section>
            """, [F("title", "Heading", "Turn one useful idea into a focused launch."), F("text", "Introduction", "Choose the smallest complete workflow that gives a visitor a useful result.")], """
            private string _name = "", _audience = "", _problem = "", _input = "", _output = "", _mode = "public";
            private bool _planned;
            private void Plan() => _planned = new[] { _name, _audience, _problem, _input, _output }.All(value => !string.IsNullOrWhiteSpace(value));
            private string Risk => _mode switch { "public" => "Low delivery risk", "capture" => "Low to medium delivery risk", "saved" => "High delivery risk", _ => "Medium delivery risk" };
            private string Delivery => _mode is "public" or "capture" ? "Build a single submit-and-result flow before adding more product surface." : "Move account-specific behavior into a later phase to keep the first release focused.";
            """),
        M("risk-badge", "Atlas risk status", "Feedback & status", "Atlas.Web/Pages/LaunchScope.cshtml:113; site.css:1141", """
            <aside class="status-note"><span class="badge" data-field="title">@T("title")</span><p data-field="text">@T("text")</p></aside>
            """, [F("title", "Status", "Low delivery risk"), F("text", "Explanation", "One clear input, one useful result, and one next action.")]),
        M("result-summary", "Atlas result summary", "Data display", "Atlas.Web/Pages/LaunchScope.cshtml:118-174", """
            <section class="panel"><h3 data-field="title">@T("title")</h3><div class="two-grid">@foreach(var line in Lines("items")) { <article class="preview-block"><span class="eyebrow">@Part(line,0)</span><p>@Part(line,1)</p></article> }</div><p class="notice" data-field="text">@T("text")</p></section>
            """, [F("title", "Title", "Your next step"), F("items", "Results · label | value", "Audience|One clearly defined group\nInput|The smallest useful set of information\nOutput|A clear recommendation\nScope|One complete workflow"), F("text", "Next action", "Test the workflow with someone who needs it.")]),
        M("form-field", "Atlas labeled input", "Forms & input", "Atlas.Web/Pages/LaunchScope.cshtml:22-77; Admin/admin.css:395", """
            <label class="form-field"><span data-field="title">@T("title")</span><input placeholder="@T("placeholder")" @bind="_value" /><small data-field="text">@T("text")</small></label>
            """, [F("title", "Label", "Project name"), F("placeholder", "Placeholder", "A short, memorable name"), F("text", "Help", "Use the name people will recognize.")], "private string _value = string.Empty;"),
        M("contact-form", "Atlas inquiry draft", "Forms & input", "Atlas.Web/Pages/Account/Create.cshtml:17; LaunchScope form styling", """
            <section id="contact" class="panel"><h2 data-field="title">@T("title")</h2><p data-field="text">@T("text")</p><form class="form-stack" @onsubmit="Preview" @onsubmit:preventDefault><label>Name<input required @bind="_name" autocomplete="name" /></label><label>Email<input required type="email" @bind="_email" autocomplete="email" /></label><label>Message<textarea required @bind="_message"></textarea></label><button class="primary" type="submit">Preview inquiry</button></form>@if(_ready) { <div class="notice" role="status"><strong>Draft ready for @_name</strong><p>@_message</p><small>Reply address: @_email. This draft stays in the page.</small></div> }</section>
            """, [F("title", "Title", "Start a conversation"), F("text", "Introduction", "Prepare a concise inquiry. This local form previews your draft without sending it.")], "private string _name = \"\", _email = \"\", _message = \"\"; private bool _ready; private void Preview() => _ready = !string.IsNullOrWhiteSpace(_name) && !string.IsNullOrWhiteSpace(_email) && !string.IsNullOrWhiteSpace(_message);"),
        M("admin-header", "Atlas Admin header", "Structure & navigation", "Hypershell.ProfileServer/Pages/Shared/_Layout.cshtml:20", """
            <header class="site-header"><a class="brand" href="@Link(T("href"))"><img src="./_content/HyperLib.Work/atlas/atlas-emblem-96.png" width="32" height="32" alt="" /><span><strong data-field="title">@T("title")</strong><small data-field="text">@T("text")</small></span></a><span class="badge" data-field="status">@T("status")</span></header>
            """, [F("title", "Brand", "Atlas Universalis"), F("text", "Workspace", "Control room"), F("status", "State", "Local workspace"), F("href", "Home link", "#")]),
        M("status-strip", "Atlas status strip", "Data display", "Hypershell.ProfileServer/Pages/Index.cshtml:13; Storage.cshtml:13; admin.css:257", """
            <dl class="status-strip">@foreach(var line in Lines("items")) { <div><dt>@Part(line,0)</dt><dd>@Part(line,1)</dd></div> }</dl>
            """, [F("items", "Metrics · label | value", "Workspace|Ready\nCollections|12\nPages|48\nLast update|Today")]),
        M("metric", "Atlas metric panel", "Data display", "Hypershell.ProfileServer/wwwroot/css/admin.css:277", """
            <article class="metric"><p class="eyebrow" data-field="title">@T("title")</p><strong data-field="value">@T("value")</strong><p data-field="text">@T("text")</p></article>
            """, [F("title", "Label", "Published pages"), F("value", "Value", "48"), F("text", "Context", "Across the selected collections")]),
        M("module-card", "Atlas module launcher", "Cards & collections", "Hypershell.ProfileServer/Pages/Index.cshtml:19; admin.css:320", """
            <a class="module-card" href="@Link(T("href"))"><span class="module-icon" aria-hidden="true" data-field="icon">@T("icon")</span><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p></a>
            """, [F("icon", "Symbol", "◇"), F("title", "Title", "Collections"), F("text", "Description", "Organize Works and Pages into a useful library."), F("href", "Address", "#collections")]),
        M("inventory", "Atlas editable inventory", "Data display", "Hypershell.ProfileServer/Pages/Profiles.cshtml:20-62; admin.css:675", """
            <section class="panel"><h2 data-field="title">@T("title")</h2><label class="search-field">Filter inventory<input type="search" @bind="_query" @bind:event="oninput" placeholder="Find an item…" /></label>@foreach(var line in Lines("items").Where(line => line.Contains(_query, StringComparison.OrdinalIgnoreCase))) { <article class="inventory-row"><div><h3>@Part(line,0)</h3><p>@Part(line,1)</p><small>@Part(line,2)</small></div><span class="badge">@Part(line,3)</span></article> }</section>
            """, [F("title", "Heading", "Workspace inventory"), F("items", "Items · name | description | metadata | status", "Field journal|Notes and observations|Updated today|Ready\nProject atlas|A collection of ideas|Updated yesterday|Draft\nReference desk|Useful sources|12 entries|Ready")], "private string _query = string.Empty;"),
        M("inventory-row", "Atlas inventory row", "Data display", "Hypershell.ProfileServer/Pages/ClientDevices.cshtml:27; admin.css:680", """
            <article class="inventory-row"><div><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p><code data-field="metadata">@T("metadata")</code></div><span class="badge" data-field="status">@T("status")</span></article>
            """, [F("title", "Name", "Research notebook"), F("text", "Description", "A collection of current observations."), F("metadata", "Metadata", "Revision 4 · Updated today"), F("status", "Status", "Ready")]),
        M("notice", "Atlas notice", "Feedback & status", "Hypershell.ProfileServer/wwwroot/css/admin.css:541", """
            <aside class="notice" role="status"><strong data-field="title">@T("title")</strong><p data-field="text">@T("text")</p></aside>
            """, [F("title", "Heading", "Ready to continue"), F("text", "Message", "Your local changes are ready to review.")]),
        M("empty-state", "Atlas empty state", "Feedback & status", "Hypershell.ProfileServer/Pages/Downloads.cshtml:15; admin.css:769", """
            <section class="empty-state"><span class="module-icon" aria-hidden="true" data-field="icon">@T("icon")</span><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p><a class="secondary" href="@Link(T("href"))" data-field="label">@T("label")</a></section>
            """, [F("icon", "Symbol", "◇"), F("title", "Heading", "A fresh space"), F("text", "Message", "Add the first item to begin building this collection."), F("label", "Action", "Get started"), F("href", "Address", "#start")]),
        M("login-form", "Atlas sign-in form pattern", "Forms & input", "Hypershell.ProfileServer/Pages/Account/Login.cshtml; admin.css:377", """
            <section class="login-card"><p class="eyebrow" data-field="kicker">@T("kicker")</p><h2 data-field="title">@T("title")</h2><p data-field="text">@T("text")</p><form class="form-stack" @onsubmit="Validate" @onsubmit:preventDefault><label>Email<input type="email" required autocomplete="off" @bind="_email" /></label><label>Password<input type="password" required minlength="8" autocomplete="off" @bind="_password" /></label><button type="submit" class="primary">Check form</button></form>@if(_message.Length > 0) { <p role="status" class="notice">@_message</p> }</section>
            """, [F("kicker", "Kicker", "Account form pattern"), F("title", "Heading", "Welcome back"), F("text", "Help", "Local validation preview. Use sample details; this template does not authenticate or store credentials.")], "private string _email = \"\", _password = \"\", _message = \"\"; private void Validate() { _message = _email.Contains('@') && _password.Length >= 8 ? \"The sample fields are valid. Connect your own authentication handler to use this form.\" : \"Enter an email and a password with at least eight characters.\"; _password = string.Empty; }"),
        M("code-entry", "Atlas one-time code entry", "Forms & input", "Hypershell.ProfileServer/Pages/Device.cshtml:24; admin.css:559", """
            <section class="panel"><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p><form class="code-entry" @onsubmit="Check" @onsubmit:preventDefault><label>Sample code<input @bind="_code" maxlength="9" placeholder="ABCD-EFGH" autocomplete="off" /></label><button class="secondary" type="submit">Check format</button></form><p role="status">@_message</p></section>
            """, [F("title", "Heading", "Enter a code"), F("text", "Help", "This local pattern checks the four-letter, hyphen, four-letter format.")], "private string _code = \"\", _message = \"\"; private void Check() => _message = System.Text.RegularExpressions.Regex.IsMatch(_code, \"^[A-Za-z]{4}-[A-Za-z]{4}$\") ? \"Code format is valid.\" : \"Use four letters, a hyphen, then four letters.\";"),
        M("approval", "Atlas decision panel", "Widgets & tools", "Hypershell.ProfileServer/Pages/CredentialRelease.cshtml:45-70; admin.css:571", """
            <section class="panel"><p class="eyebrow" data-field="kicker">@T("kicker")</p><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p><div class="device-request"><span class="module-icon" aria-hidden="true">◇</span><div><strong data-field="subject">@T("subject")</strong><p data-field="detail">@T("detail")</p></div></div><div class="actions"><button type="button" class="primary" @onclick="() => _decision = true">Approve locally</button><button type="button" class="secondary" @onclick="() => _decision = false">Decline</button><button type="button" class="quiet" @onclick="() => _decision = null">Reset</button></div>@if(_decision.HasValue) { <p class="notice" role="status">@(_decision.Value ? "Approved in this page." : "Declined in this page.")</p> }</section>
            """, [F("kicker", "Kicker", "Review a request"), F("title", "Heading", "Choose the next step"), F("text", "Explanation", "A local decision pattern. No account, device, or credential is changed."), F("subject", "Request title", "Example workspace"), F("detail", "Request details", "Review the details before making a choice.")], "private bool? _decision;"),
        M("inline-editor", "Atlas expandable editor", "Forms & input", "Hypershell.ProfileServer/Pages/AdminSection.cshtml:102-116; admin.css:744", """
            <details class="inline-editor"><summary data-field="title">@T("title")</summary><label>Local note<textarea @bind="_note" placeholder="@T("placeholder")"></textarea></label><button class="secondary" type="button" @onclick="() => _saved = _note">Preview change</button>@if(_saved.Length > 0) { <p class="notice" role="status">@_saved</p> }</details>
            """, [F("title", "Disclosure label", "Edit supporting details"), F("placeholder", "Placeholder", "Write a short note…")], "private string _note = \"\", _saved = \"\";"),
        M("download-card", "Atlas release card", "Cards & collections", "Hypershell.ProfileServer/Pages/Downloads.cshtml:23-40", """
            <article class="module-card"><span class="module-icon" aria-hidden="true">↓</span><h3 data-field="title">@T("title")</h3><span class="badge" data-field="version">@T("version")</span><p data-field="text">@T("text")</p><a class="secondary" href="@Link(T("href"))" data-field="label">@T("label")</a></article>
            """, [F("title", "Release title", "Project release"), F("version", "Version & channel", "1.0 · Stable"), F("text", "Release notes", "Describe what is included and who the release is for."), F("label", "Link label", "View release"), F("href", "Release address", "#release")]),
        M("breadcrumbs", "Atlas breadcrumb trail", "Structure & navigation", "HyperLib.Archives/wwwroot/archives.js:43-54; archives.css:223", """
            <nav class="breadcrumbs" aria-label="Breadcrumb">@foreach(var line in Lines("items")) { <a href="@Link(Part(line,1))">@Part(line,0)</a> }</nav>
            """, [F("items", "Trail · label | address", "Archives|#archives\nNatural world|#nature\nField notes|#notes")]),
        M("search-tree", "Atlas searchable hierarchy", "Structure & navigation", "HyperLib.Archives/wwwroot/index.html:43-53; archives.js:11-29", """
            <aside class="panel"><p class="eyebrow" data-field="title">@T("title")</p><label class="search-field">Find a title<input type="search" @bind="_query" @bind:event="oninput" placeholder="Search the library…" /></label><nav class="archive-tree" aria-label="Library hierarchy">@foreach(var group in Lines("items").GroupBy(line => Part(line,0))) { var matching = group.Where(line => line.Contains(_query, StringComparison.OrdinalIgnoreCase)).ToArray(); if(matching.Length > 0) { <details open><summary>@group.Key</summary>@foreach(var line in matching) { <a href="@Link(Part(line,2))">@Part(line,1)</a> }</details> } }@if(!Lines("items").Any(line => line.Contains(_query, StringComparison.OrdinalIgnoreCase))) { <p role="status">No matching titles.</p> }</nav></aside>
            """, [F("title", "Heading", "Explore the Archives"), F("items", "Hierarchy · collection | page title | address", "Natural world|Woodland habitat|#woodland\nNatural world|Field observations|#field\nDesign notebook|Systems and patterns|#systems\nDesign notebook|Useful tools|#tools")], "private string _query = string.Empty;"),
        M("collection-card", "Atlas collection card", "Cards & collections", "HyperLib.Archives/wwwroot/archives.js:30-35; archives.css:256", """
            <a class="collection-card" href="@Link(T("href"))">@if(T("src").Length > 0) { <img src="@Link(T("src"))" alt="@T("alt")" loading="lazy" /> }<div><span class="eyebrow" data-field="kind">@T("kind")</span><h3 data-field="title">@T("title")</h3><p data-field="text">@T("text")</p></div></a>
            """, [F("kind", "Object type", "Collection"), F("title", "Title", "Natural world"), F("text", "Description or count", "A collection of observations and ideas."), F("href", "Address", "#nature"), F("src", "Cover image", "", "media"), F("alt", "Cover description", "")]),
        M("collection-grid", "Atlas collection grid", "Cards & collections", "HyperLib.Archives/wwwroot/archives.js:44-58; archives.css:255", """
            <section id="archives" class="content-section"><h2 data-field="title">@T("title")</h2><div class="two-grid">@foreach(var line in Lines("items")) { <a class="collection-card" href="@Link(Part(line,3))"><div><span class="eyebrow">@Part(line,0)</span><h3>@Part(line,1)</h3><p>@Part(line,2)</p></div></a> }</div></section>
            """, [F("title", "Title", "Explore the collections"), F("items", "Cards · type | title | summary | address", "Collection|Natural world|Habitats, observations, and field notes.|#nature\nWork|Design notebook|Systems, patterns, and ideas.|#design\nWork|Sound laboratory|Experiments in sound and space.|#sound\nPage|Getting started|A short path into the library.|#start")]),
        M("reader-heading", "Atlas reader introduction", "Headers & heroes", "HyperLib.Archives/wwwroot/index.html:56; archives.js:46", """
            <header class="reader-heading"><p class="eyebrow" data-field="kicker">@T("kicker")</p><h1 data-field="title">@T("title")</h1><p class="lede" data-field="text">@T("text")</p></header>
            """, [F("kicker", "Object type", "Public library"), F("title", "Title", "Curiosity, collected."), F("text", "Introduction", "Collections, Works and Pages from Hyperlib.")]),
        M("reader-workspace", "Atlas local reader workspace", "Layouts & structure", "HyperLib.Archives/wwwroot/index.html:33-65; archives.js hierarchy/view behavior", """
            <section class="reader-workspace"><div class="archive-toolbar"><span class="eyebrow" data-field="kicker">@T("kicker")</span><button type="button" class="quiet" @onclick="() => _showTree = !_showTree" aria-expanded="@_showTree">@(_showTree ? "Hide library" : "Show library")</button></div><div class="reader-columns @(_showTree ? "" : "tree-hidden")">@if(_showTree) { <aside class="panel"><label class="search-field">Find a page<input type="search" @bind="_query" @bind:event="oninput" placeholder="Search the library…" /></label><nav class="archive-tree" aria-label="Local pages">@foreach(var line in Lines("items").Where(line => line.Contains(_query, StringComparison.OrdinalIgnoreCase))) { <button class="tree-item" type="button" @onclick="() => _selected = line">@Part(line,0)</button> }</nav></aside> }<article class="reader-body"><p class="eyebrow">@(_selected is null ? T("kicker") : "Page")</p><h2>@(_selected is null ? T("title") : Part(_selected,0))</h2><p class="reader-text">@(_selected is null ? T("text") : Part(_selected,1))</p>@if(_selected is not null) { <button class="secondary" type="button" @onclick="() => _selected = null">Back to introduction</button> }</article></div></section>
            """, [F("kicker", "Kicker", "Public library"), F("title", "Introduction title", "Curiosity, collected."), F("text", "Introduction", "Open a page from the library to read it here. This portable reader uses the content you supply and runs without a server API."), F("items", "Pages · title | content", "Woodland habitat|A woodland is a network of organisms, light, water, and seasonal change. Record the relationships you observe.\nDesign notebook|Begin with the work someone is trying to accomplish. Build the smallest useful pattern, test it, and refine it.\nSound laboratory|Change one variable at a time and listen carefully to the result. Keep notes that help you repeat the experiment.")], "private string _query = string.Empty; private string? _selected; private bool _showTree = true;")
    ];

    private const string Surface = """
        @namespace HyperLib.Work.Atlas
        @implements IAsyncDisposable
        @inject IJSRuntime JS
        <div class="atlas-kit" @ref="_root">@ChildContent</div>
        @code {
          [Parameter] public RenderFragment? ChildContent { get; set; }
          private ElementReference _root;
          private IJSObjectReference? _module;
          protected override async Task OnAfterRenderAsync(bool firstRender) {
            if (!firstRender) return;
            try { _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/HyperLib.Work/atlas/atlas.js"); await _module.InvokeVoidAsync("mount", _root); }
            catch (JSDisconnectedException) { }
          }
          public async ValueTask DisposeAsync() {
            if (_module is null) return;
            try { await _module.InvokeVoidAsync("dispose", _root); await _module.DisposeAsync(); } catch (JSDisconnectedException) { }
          }
        }
        """;

    private const string JavaScript = """
        // Portable counterpart of Atlas.Web's scroll header and Project Lab command panels.
        // No fetch, global IDs, account handlers, or external resources.
        const instances = new WeakMap();
        export function mount(root) {
          if (instances.has(root)) return;
          const controller = new AbortController(), signal = controller.signal;
          const update = () => root.querySelectorAll('[data-atlas-header]').forEach(header => header.classList.toggle('is-scrolled', scrollY > 60));
          window.addEventListener('scroll', update, {passive:true, signal}); update();
          root.addEventListener('click', async event => {
            const button = event.target.closest('[data-atlas-copy]'); if (!button || !root.contains(button)) return;
            const container = button.closest('.command-panel,.project-card'), code = container?.querySelector('pre code'), status = container?.querySelector('[data-copy-status]');
            if (!code) return;
            try { await navigator.clipboard.writeText(code.textContent); if(status) status.textContent = 'Copied.'; }
            catch { const range = document.createRange(); range.selectNodeContents(code); const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range); if(status) status.textContent = 'Command selected. Use your copy shortcut.'; }
          }, {signal});
          instances.set(root, controller);
        }
        export function dispose(root) { instances.get(root)?.abort(); instances.delete(root); }
        """;

    private const string Provenance = """
        # Atlas Universalis portable Design Groups

        Source design: Atlas.Web at a8c9608cd54d58e525961ce0ad0ca4ecad58c24a,
        Pages/Shared/_Layout.cshtml, Index, Services, Bio, ProjectLab, LaunchScope,
        wwwroot/css/atlas.tokens.css and site.css. Admin patterns derive from
        Hypershell.ProfileServer/Pages and wwwroot/css/admin.css. Archives patterns
        derive from HyperLib.Archives/wwwroot/index.html, archives.css and archives.js.

        The bundled 96px and 512px artwork is the approved current Atlas emblem.
        The portable source uses semantic Razor components, isolated local CSS,
        browser-native controls and a local ES module. Atlas hosting, MVC PageModel,
        tag helpers, authentication, credential stores and publication APIs are not
        dependencies. Form samples only validate or preview in-memory page state.
        LaunchScope retains the original deterministic scope/risk rules. Archives
        reading and filtering operate on explicitly supplied local content.

        Typography uses the destination Work's font selection and system fallbacks;
        it never requests Google Fonts. Individual objects inherit Work colors;
        import the Atlas palette or Work style to reproduce the navy/gold design.
        Responsive navigation remains accessible on small screens. Decorative
        animation respects reduced-motion preference. Root-relative Atlas asset
        references have been replaced by local RCL assets so cross-template imports
        carry their media and JavaScript with them.
        """;

    // Scoped counterparts retain the actual site's restrained geometry, gold accents,
    // celestial layers, card surfaces and typography, while inheriting a Work's tokens.
    private const string Css = """
        .atlas-kit{--atlas-bg:var(--hl-paper,#070d1a);--atlas-text:var(--hl-ink,#e8ecf1);--atlas-muted:var(--hl-muted,#bcc5d0);--atlas-gold:var(--hl-accent,#c8963e);--atlas-panel:var(--hl-panel,#101b35);--atlas-line:color-mix(in srgb,var(--atlas-muted) 24%,transparent);color:var(--atlas-text);font-family:var(--hl-font,'Segoe UI',sans-serif);line-height:1.65;min-width:0;isolation:isolate}
        .atlas-kit ::deep *{box-sizing:border-box}.atlas-kit ::deep h1,.atlas-kit ::deep h2,.atlas-kit ::deep h3{color:var(--atlas-text);font-family:var(--hl-heading-font,inherit);font-weight:400;line-height:1.25;overflow-wrap:anywhere}.atlas-kit ::deep h1{font-size:clamp(1.85rem,4vw,3.2rem);letter-spacing:.07em}.atlas-kit ::deep h2{font-size:clamp(1.45rem,3vw,2.2rem);letter-spacing:.04em}.atlas-kit ::deep h3{font-size:1.04rem;letter-spacing:.07em}.atlas-kit ::deep p{color:var(--atlas-muted);overflow-wrap:anywhere}.atlas-kit ::deep a{color:inherit;text-decoration:none}.atlas-kit ::deep a:hover{color:var(--atlas-gold)}.atlas-kit ::deep button,.atlas-kit ::deep input,.atlas-kit ::deep select,.atlas-kit ::deep textarea{font:inherit}.atlas-kit ::deep button{cursor:pointer}.atlas-kit ::deep a:focus-visible,.atlas-kit ::deep button:focus-visible,.atlas-kit ::deep summary:focus-visible{outline:2px solid var(--atlas-gold);outline-offset:4px}
        .atlas-kit ::deep .eyebrow{color:var(--atlas-gold);font-size:.63rem;letter-spacing:.24em;text-transform:uppercase;font-weight:500}.atlas-kit ::deep .lede{max-width:52rem;font-size:1rem;line-height:1.8}.atlas-kit ::deep .content-section,.atlas-kit ::deep .page-heading,.atlas-kit ::deep .reader-heading{padding:clamp(1.25rem,4vw,3rem) 0}.atlas-kit ::deep .page-heading{max-width:55rem}.atlas-kit ::deep .two-grid,.atlas-kit ::deep .three-grid{display:grid;gap:1.1rem;align-items:start}.atlas-kit ::deep .two-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.atlas-kit ::deep .three-grid{grid-template-columns:repeat(3,minmax(0,1fr));margin-top:2rem}
        .atlas-kit ::deep .site-header{display:flex;align-items:center;justify-content:space-between;gap:1.5rem;min-height:74px;padding:1rem 1.2rem;border-bottom:1px solid var(--atlas-line);background:color-mix(in srgb,var(--atlas-bg) 86%,transparent);backdrop-filter:blur(16px);transition:box-shadow .2s}.atlas-kit ::deep .site-header.is-scrolled{box-shadow:0 8px 24px #0002}.atlas-kit ::deep .brand{display:flex;align-items:center;gap:.75rem;font-size:.7rem;letter-spacing:.16em;text-transform:uppercase}.atlas-kit ::deep .brand img{object-fit:contain;flex:none}.atlas-kit ::deep .brand small{display:block;color:var(--atlas-muted);font-size:.58rem;letter-spacing:.16em}.atlas-kit ::deep .desktop-nav{display:flex;flex-wrap:wrap;gap:1.2rem;font-size:.68rem}.atlas-kit ::deep .mobile-nav{display:none;position:relative}.atlas-kit ::deep .mobile-nav nav{display:grid;gap:.7rem;padding:1rem;background:var(--atlas-bg);border:1px solid var(--atlas-line)}.atlas-kit ::deep .mobile-nav summary{cursor:pointer;font-size:.75rem}
        .atlas-kit ::deep .landing-hero{min-height:520px;position:relative;display:grid;place-items:center;isolation:isolate;overflow:hidden;text-align:center;padding:5rem 1rem;background:var(--atlas-bg);border-radius:12px}.atlas-kit ::deep .hero-background{position:absolute;inset:0;z-index:-1;pointer-events:none}.atlas-kit ::deep .hero-content{max-width:54rem;position:relative}.atlas-kit ::deep .hero-content h1{font-size:clamp(1.8rem,5vw,3.25rem);font-weight:300;letter-spacing:.2em}.atlas-kit ::deep .hero-content .lede{max-width:42rem;margin:1.6rem auto}.atlas-kit ::deep .hero-emblem{position:absolute;width:min(505px,75%);height:auto;top:50%;left:50%;transform:translate(-50%,-47%);opacity:.085;filter:saturate(.07) brightness(1.18) contrast(.86);mix-blend-mode:screen;mask-image:radial-gradient(ellipse,#000 26%,transparent 95%)}.atlas-kit ::deep .star-divider{display:flex;gap:1rem;align-items:center;justify-content:center;color:var(--atlas-gold);margin:1.8rem auto}.atlas-kit ::deep .star-divider:before,.atlas-kit ::deep .star-divider:after{content:'';height:1px;width:70px;background:linear-gradient(90deg,transparent,var(--atlas-gold),transparent)}.atlas-kit ::deep .scroll-cue{position:absolute;bottom:1rem;color:var(--atlas-gold);font-size:1.5rem}.atlas-kit ::deep .starfield{position:absolute;inset:0;pointer-events:none;background:radial-gradient(circle at 12% 18%,#ebf1ff 0 1px,transparent 2px),radial-gradient(circle at 72% 10%,#dee7f6 0 1px,transparent 2px),radial-gradient(circle at 84% 33%,#e2ebff 0 1.2px,transparent 2px),radial-gradient(circle at 24% 42%,#dae5f7b3 0 1px,transparent 2px),radial-gradient(circle at 6% 72%,#dae5f7bf 0 1px,transparent 2px),radial-gradient(circle at 92% 66%,#e1eafa99 0 1px,transparent 2px),radial-gradient(circle at 37% 86%,#cddbf699 0 1px,transparent 2px),radial-gradient(circle at 58% 63%,#e6eefc 0 1px,transparent 2px),radial-gradient(circle at 77% 47%,#ecf3ff 0 1px,transparent 2px);animation:atlas-twinkle 9s ease-in-out infinite alternate}.atlas-kit ::deep .map-grid{position:absolute;inset:0;pointer-events:none;opacity:.55;background:repeating-radial-gradient(circle at 50% 50%,#8499bf14 0 1px,transparent 2px 46px),repeating-linear-gradient(90deg,transparent 0 6.95%,#8499bf12 7%,transparent 7.06% 14%),repeating-linear-gradient(0deg,transparent 0 8.95%,#8499bf12 9%,transparent 9.06% 18%),radial-gradient(circle at 50% 48%,#c4a96e18,transparent 68%)}.atlas-kit ::deep .decoration-preview{position:relative;overflow:hidden;isolation:isolate;min-height:240px;padding:3rem 2rem;background:var(--atlas-bg);border-radius:12px}.atlas-kit ::deep .decoration-preview>*:not(.starfield):not(.map-grid){position:relative}.atlas-kit ::deep .emblem-panel{text-align:center;margin:0;padding:2rem;background:var(--atlas-bg);border-radius:12px}.atlas-kit ::deep .emblem-panel img{object-fit:contain;max-width:100%;height:auto}.atlas-kit ::deep .emblem-panel figcaption{letter-spacing:.15em;text-transform:uppercase;font-size:.75rem;color:var(--atlas-gold)}@keyframes atlas-twinkle{from{opacity:.4}to{opacity:.85}}
        .atlas-kit ::deep .panel,.atlas-kit ::deep .service-card,.atlas-kit ::deep .project-card,.atlas-kit ::deep .module-card,.atlas-kit ::deep .collection-card,.atlas-kit ::deep .login-card{display:block;border:1px solid var(--atlas-line);border-radius:14px;background:linear-gradient(145deg,color-mix(in srgb,var(--atlas-panel) 90%,transparent),var(--atlas-panel));box-shadow:0 14px 36px #0002;min-width:0}.atlas-kit ::deep .panel,.atlas-kit ::deep .service-card,.atlas-kit ::deep .project-card,.atlas-kit ::deep .module-card{padding:1.5rem}.atlas-kit ::deep .principle-card{padding:1.5rem 0;border-top:1px solid color-mix(in srgb,var(--atlas-gold) 45%,transparent)}.atlas-kit ::deep .principle-card h3,.atlas-kit ::deep .service-card h3,.atlas-kit ::deep .module-card h3{text-transform:uppercase;font-size:.85rem;letter-spacing:.13em}.atlas-kit ::deep .module-card{min-height:210px;transition:transform .18s,border-color .18s}.atlas-kit ::deep .module-card:hover,.atlas-kit ::deep .collection-card:hover{transform:translateY(-2px);border-color:var(--atlas-gold)}.atlas-kit ::deep .module-icon{color:var(--atlas-gold);font-size:1.7rem}.atlas-kit ::deep .card-heading{display:flex;align-items:start;justify-content:space-between;gap:1rem}.atlas-kit ::deep .card-heading h3{margin-top:0}.atlas-kit ::deep .badge{display:inline-block;border:1px solid var(--atlas-line);border-radius:99px;padding:.25rem .65rem;font-size:.64rem;color:var(--atlas-gold);white-space:normal}.atlas-kit ::deep .collection-card{overflow:hidden}.atlas-kit ::deep .collection-card>div{padding:1.5rem}.atlas-kit ::deep .collection-card>img{width:100%;height:170px;object-fit:cover}.atlas-kit ::deep .collection-card h3{margin:1rem 0}.atlas-kit ::deep .collection-card p{font-size:.8rem}
        .atlas-kit ::deep .actions{display:flex;flex-wrap:wrap;gap:.65rem;align-items:center;margin:1rem 0}.atlas-kit ::deep .primary,.atlas-kit ::deep .secondary,.atlas-kit ::deep .quiet{display:inline-flex;justify-content:center;align-items:center;min-height:40px;border-radius:8px;padding:.7rem 1rem;font-size:.68rem;letter-spacing:.1em;text-transform:uppercase;text-align:center}.atlas-kit ::deep .primary{border:1px solid var(--atlas-gold);background:var(--atlas-gold);color:var(--hl-accent-ink,#111);font-weight:600}.atlas-kit ::deep .primary:hover{filter:brightness(1.12);color:var(--hl-accent-ink,#111)}.atlas-kit ::deep .secondary{border:1px solid var(--atlas-line);background:transparent;color:var(--atlas-text)}.atlas-kit ::deep .quiet{border:1px solid transparent;background:transparent;color:var(--atlas-gold)}.atlas-kit ::deep .action-banner{display:flex;align-items:center;justify-content:space-between;gap:2rem;border:1px solid var(--atlas-line);padding:2rem;border-radius:12px;background:var(--atlas-panel)}.atlas-kit ::deep .action-banner h2{font-size:1.6rem}.atlas-kit ::deep .text-link{color:var(--atlas-gold);font-size:.8rem}.atlas-kit ::deep .atlas-footer{display:flex;justify-content:space-between;flex-wrap:wrap;gap:.8rem;padding:1.5rem 0;border-top:1px solid var(--atlas-line);font-size:.65rem;color:var(--atlas-muted)}.atlas-kit ::deep .atlas-footer span:first-child{text-transform:uppercase;letter-spacing:.12em}
        .atlas-kit ::deep .portrait{display:grid;grid-template-columns:160px minmax(0,1fr);gap:2rem;padding:2rem;border:1px solid var(--atlas-line);border-radius:14px;background:var(--atlas-panel);align-items:center}.atlas-kit ::deep .portrait>img{width:160px;height:160px;object-fit:cover;border-radius:50%}.atlas-kit ::deep .timeline{list-style:none;padding:0}.atlas-kit ::deep .timeline li,.atlas-kit ::deep .timeline-entry{border-left:1px solid var(--atlas-gold);padding:0 0 1.4rem 1.2rem;margin:.7rem 0}.atlas-kit ::deep .timeline strong,.atlas-kit ::deep .timeline-entry>strong{display:block;color:var(--atlas-gold);font-size:.72rem;letter-spacing:.12em}.atlas-kit ::deep .timeline span{font-weight:500}.atlas-kit ::deep .social-links{list-style:none;padding:0;display:flex;gap:1.5rem;flex-wrap:wrap}.atlas-kit ::deep .social-links span{margin-right:.5rem;color:var(--atlas-gold)}.atlas-kit ::deep pre{white-space:pre-wrap;overflow-wrap:anywhere;background:color-mix(in srgb,var(--atlas-bg) 80%,transparent);padding:1.2rem;border:1px solid var(--atlas-line);border-radius:8px;font: .8rem/1.6 Consolas,monospace;color:var(--atlas-text)}.atlas-kit ::deep .command-panel{padding:1.5rem;border:1px solid var(--atlas-line);border-radius:12px}.atlas-kit ::deep [data-copy-status]{display:inline-block;margin-left:.5rem;font-size:.75rem;color:var(--atlas-gold)}
        .atlas-kit ::deep label{display:grid;gap:.45rem;font-size:.75rem;color:var(--atlas-muted)}.atlas-kit ::deep input,.atlas-kit ::deep select,.atlas-kit ::deep textarea{width:100%;min-width:0;min-height:44px;padding:.7rem;border:1px solid var(--atlas-line);border-radius:8px;background:var(--atlas-bg);color:var(--atlas-text)}.atlas-kit ::deep input:focus,.atlas-kit ::deep select:focus,.atlas-kit ::deep textarea:focus{outline:2px solid color-mix(in srgb,var(--atlas-gold) 60%,transparent);outline-offset:1px}.atlas-kit ::deep textarea{min-height:110px;resize:vertical}.atlas-kit ::deep .form-stack{display:grid;gap:1rem}.atlas-kit ::deep .form-field{padding:1.2rem}.atlas-kit ::deep .form-field small{font-size:.7rem}.atlas-kit ::deep .login-card{max-width:460px;margin:auto;padding:2rem}.atlas-kit ::deep .login-card:before{content:'';display:block;width:42px;height:1px;background:var(--atlas-gold);margin-bottom:1.5rem}.atlas-kit ::deep .code-entry{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:end;gap:.7rem}.atlas-kit ::deep .code-entry input{font-family:Consolas,monospace;letter-spacing:.16em;text-transform:uppercase}.atlas-kit ::deep .notice{padding:1rem;border:1px solid color-mix(in srgb,var(--atlas-gold) 40%,transparent);border-radius:8px;background:color-mix(in srgb,var(--atlas-gold) 9%,transparent);margin:1rem 0}.atlas-kit ::deep .notice p{margin:.35rem 0 0}.atlas-kit ::deep .preview-block{padding:1rem;background:color-mix(in srgb,var(--atlas-bg) 65%,transparent);border-radius:8px}.atlas-kit ::deep .status-note{padding:1rem}.atlas-kit ::deep .device-request{display:flex;align-items:center;gap:1rem;padding:1rem;border:1px solid var(--atlas-line);border-radius:12px}.atlas-kit ::deep .inline-editor{padding:1.2rem;border:1px solid var(--atlas-line);border-radius:8px;background:var(--atlas-panel)}.atlas-kit ::deep .inline-editor summary{cursor:pointer;color:var(--atlas-gold)}.atlas-kit ::deep .inline-editor label{margin:1rem 0}
        .atlas-kit ::deep .status-strip{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:1px;border:1px solid var(--atlas-line);border-radius:12px;background:var(--atlas-line);overflow:hidden;margin:0}.atlas-kit ::deep .status-strip>div{padding:1.2rem;background:var(--atlas-panel)}.atlas-kit ::deep .status-strip dt{font-size:.62rem;letter-spacing:.17em;text-transform:uppercase;color:var(--atlas-muted)}.atlas-kit ::deep .status-strip dd{margin:.5rem 0 0;font-size:1rem}.atlas-kit ::deep .metric{padding:1.5rem;border:1px solid var(--atlas-line);border-radius:12px;background:var(--atlas-panel)}.atlas-kit ::deep .metric>strong{font-size:2.3rem;font-weight:300}.atlas-kit ::deep .inventory-row{display:flex;justify-content:space-between;align-items:center;gap:1rem;padding:1.1rem 0;border-bottom:1px solid var(--atlas-line)}.atlas-kit ::deep .inventory-row h3,.atlas-kit ::deep .inventory-row p{margin:0 0 .4rem}.atlas-kit ::deep .inventory-row small,.atlas-kit ::deep .inventory-row code{color:var(--atlas-muted);font-size:.72rem}.atlas-kit ::deep .empty-state{display:grid;justify-items:center;gap:.7rem;padding:3rem 1rem;text-align:center;border:1px dashed var(--atlas-line);border-radius:12px}.atlas-kit ::deep .empty-state h3,.atlas-kit ::deep .empty-state p{margin:0}
        .atlas-kit ::deep .breadcrumbs{display:flex;gap:.65rem;flex-wrap:wrap;padding:1rem 0;font-size:.75rem;border-bottom:1px solid var(--atlas-line)}.atlas-kit ::deep .breadcrumbs a+a:before{content:'/';margin-right:.65rem;color:var(--atlas-muted)}.atlas-kit ::deep .archive-toolbar{display:flex;align-items:center;justify-content:space-between;gap:1rem;padding-bottom:1rem;border-bottom:1px solid var(--atlas-line)}.atlas-kit ::deep .reader-columns{display:grid;grid-template-columns:240px minmax(0,1fr);gap:2rem;padding:1.5rem 0}.atlas-kit ::deep .reader-columns.tree-hidden{grid-template-columns:minmax(0,1fr)}.atlas-kit ::deep .archive-tree{display:grid;gap:.5rem;margin-top:1rem}.atlas-kit ::deep .archive-tree details{padding:.5rem 0;border-bottom:1px solid var(--atlas-line)}.atlas-kit ::deep .archive-tree summary{cursor:pointer;color:var(--atlas-gold);font-size:.78rem}.atlas-kit ::deep .archive-tree a,.atlas-kit ::deep .tree-item{display:block;padding:.6rem .8rem;color:var(--atlas-muted);font-size:.78rem;text-align:left;border:0;background:transparent}.atlas-kit ::deep .tree-item:hover{color:var(--atlas-gold);background:var(--atlas-panel)}.atlas-kit ::deep .reader-body{padding:1rem;min-width:0}.atlas-kit ::deep .reader-text{white-space:pre-line;line-height:1.9}
        @media(max-width:800px){.atlas-kit ::deep .desktop-nav{display:none}.atlas-kit ::deep .mobile-nav{display:block}.atlas-kit ::deep .three-grid,.atlas-kit ::deep .two-grid,.atlas-kit ::deep .reader-columns{grid-template-columns:1fr}.atlas-kit ::deep .action-banner{align-items:start;flex-direction:column}.atlas-kit ::deep .portrait{grid-template-columns:1fr}.atlas-kit ::deep .portrait>img{width:96px;height:96px}.atlas-kit ::deep .landing-hero{min-height:430px}.atlas-kit ::deep .hero-content h1{letter-spacing:.12em}.atlas-kit ::deep .site-header{gap:.6rem;padding:1rem .65rem}.atlas-kit ::deep .brand{font-size:.6rem}.atlas-kit ::deep .code-entry{grid-template-columns:1fr}.atlas-kit ::deep .inventory-row{align-items:start;flex-wrap:wrap}}
        @media(prefers-reduced-motion:reduce){.atlas-kit ::deep *{animation:none!important;transition:none!important;scroll-behavior:auto!important}.atlas-kit ::deep .module-card:hover,.atlas-kit ::deep .collection-card:hover{transform:none}}
        """;
}
