using System.IO;
using System.Text;
using System.Text.Json;
using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Creates complete, portable starter projects without changing an existing folder.</summary>
public sealed partial class StudioProjectScaffoldService
{
    private readonly string? _templateLibraryRoot;
    private readonly VisualPageComponentCatalog _catalog = new();

    public StudioProjectScaffoldService(string? templateLibraryRoot = null)
    {
        _templateLibraryRoot = templateLibraryRoot ?? Environment.GetEnvironmentVariable("HYPERLIB_ROOT");
    }

    public async Task<string> CreateAsync(StudioProjectCreationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind)) throw new VisualPageValidationException("Choose Hyperlib, Razor Pages, or HTML.");
        var name = ValidateName(request.Name);
        if (string.IsNullOrWhiteSpace(request.ParentDirectory))
            throw new VisualPageValidationException("Choose the folder that will contain the new project.");
        var parent = Path.GetFullPath(request.ParentDirectory);
        RejectParentLinks(parent);
        var destination = Path.Combine(parent, name);
        if (Path.Exists(destination)) throw new VisualPageValidationException($"'{name}' already exists. Load it or choose a different project name.");
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(parent);
        if (request.Kind == StudioProjectKind.Hyperlib)
        {
            if (string.IsNullOrWhiteSpace(_templateLibraryRoot) || !Directory.Exists(_templateLibraryRoot))
                throw new VisualPageValidationException("The Hyperlib template library could not be found. Choose a valid library before creating a Work.");
            return await new HyperlibWorkProjectService(_templateLibraryRoot).CreateAsync(parent, name,
                string.IsNullOrWhiteSpace(request.CompleteTemplateId)
                    ? request.Work ?? new HyperlibWorkProjectDefinition { WorkTitle = name }
                    : new StudioCompleteTemplateService(_templateLibraryRoot).BuildDefinition(request.CompleteTemplateId, name), ct);
        }

        var blueprint = request.Work is not null || !string.IsNullOrWhiteSpace(request.CompleteTemplateId)
            ? await new StudioWebProjectTemplateAdapter(_catalog, _templateLibraryRoot)
                .BuildAsync(name, request.Kind, request.Work, request.CompleteTemplateId, ct) : null;
        if (blueprint is not null) NormalizeTemplateIdentities(blueprint);

        var staging = Path.Combine(parent, ".studio-create-" + Guid.NewGuid().ToString("N"));
        try
        {
            await CreateWebStarterAsync(staging, name, request.Kind, ct, blueprint);
            ct.ThrowIfCancellationRequested();
            RejectParentLinks(parent);
            if (Path.Exists(destination)) throw new VisualPageValidationException($"'{name}' was created by another process. Choose another name.");
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            // Only remove the private, freshly generated staging directory, never the user's target.
            if (Directory.Exists(staging))
            {
                var expectedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
                if (Path.GetDirectoryName(Path.GetFullPath(staging)) == expectedParent
                    && Path.GetFileName(staging).StartsWith(".studio-create-", StringComparison.Ordinal))
                {
                    VisualPageProjectService.RejectReparsePoint(staging);
                    Directory.Delete(staging, true);
                }
            }
        }
    }

    private static string ValidateName(string value)
    {
        var name = (value ?? string.Empty).Trim();
        var stem = name.Split('.')[0];
        if (name.Length is < 1 or > 120 || name is "." or ".." || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\')
            || new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9'))
            throw new VisualPageValidationException("Use a project name of 1–120 characters without path separators, reserved Windows names, or a trailing period.");
        return name;
    }

    private static void RejectParentLinks(string parent)
    {
        for (var current = parent; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) || File.Exists(current)) VisualPageProjectService.RejectReparsePoint(current);
    }

    private async Task CreateWebStarterAsync(string root, string name, StudioProjectKind kind, CancellationToken ct,
        StudioWebProjectBlueprint? blueprint = null)
    {
        var validator = new VisualPageSchemaValidator(_catalog);
        var generator = new VisualPageGenerator(_catalog, validator);
        var service = new VisualPageProjectService(validator, generator, _catalog);
        var definitions = blueprint?.Pages ?? new[]
        {
            new HyperpagePageCreationDefinition { Name = "Home", Route = "/", Template = Template(name, kind, false) },
            new HyperpagePageCreationDefinition { Name = "About", Route = "/about", Template = Template(name, kind, true) }
        };
        var session = await service.CreateConfiguredAsync(root, name, new HyperpageProjectCreationOptions
        {
            // Start with browser-compatible elements. Razor conversion below uses true .cshtml pages.
            Foundation = new HyperpageProjectFoundation
            {
                MarkupLanguage = HyperpageMarkupLanguage.Html, HostingTarget = HyperpageHostingTarget.Local,
                SiteStructure = HyperpageSiteStructure.FixedPages, PlannedPageCount = definitions.Count,
                UsesTemplates = true, CodingMode = HyperpageCodingMode.Manual
            }, Pages = definitions
        }, cancellationToken: ct);

        if (kind == StudioProjectKind.Razor)
        {
            session.Project.Foundation.MarkupLanguage = HyperpageMarkupLanguage.RazorPages;
            session.Project.RazorProjectPath = session.Project.LocalHostProjectPath;
        }
        if (blueprint is not null) await WriteBlueprintAssetsAsync(root, session.Project, blueprint, ct);
        foreach (var entry in session.Project.Pages)
        {
            var pagePath = VisualPageProjectService.ResolveProjectPath(root, entry.DocumentPath);
            var page = JsonSerializer.Deserialize<VisualPageDocument>(await File.ReadAllTextAsync(pagePath, ct), VisualPageJson.Options)!;
            if (kind == StudioProjectKind.Razor)
            {
                var oldMarkup = VisualPageProjectService.ResolveProjectPath(root, entry.ComponentPath);
                File.Delete(oldMarkup); // A new staging-only HTML artifact, superseded by its .cshtml equivalent.
                page.FileType = HyperpagePageFileType.Cshtml;
                entry.FileType = page.FileType;
                entry.ComponentPath = VisualPageProjectService.GeneratedMarkupPath(session.Project, page);
            }
            var generation = generator.Generate(session.Project, page);
            page.SourceMappings = generation.SourceMappings.Select(mapping =>
            {
                mapping.RelativePath = generation.Artifacts[0].RelativePath;
                return mapping;
            }).ToList();
            page.GeneratedFiles = generation.Artifacts.Select(artifact => new VisualPageGeneratedFileHash
                { RelativePath = artifact.RelativePath, Sha256 = artifact.Sha256 }).ToList();
            page.Revision = VisualPageHashing.ComputePageRevision(page, string.Empty);
            entry.Revision = page.Revision;
            foreach (var artifact in generation.Artifacts) await WriteAsync(root, artifact.RelativePath, artifact.Content, ct);
            await WriteAsync(root, entry.DocumentPath, JsonSerializer.Serialize(page, VisualPageJson.Options), ct);
            await WriteAsync(root, VisualPageProjectService.UserCssPath(session.Project, page),
                blueprint is null ? StarterCss : "/* Page-specific styles. Shared foundation: /styles/site-foundation.css. */\n", ct);
            await WriteAsync(root, VisualPageProjectService.UserJavaScriptPath(session.Project, page),
                blueprint is null ? StarterJavaScript : "// Page-specific behavior. Shared navigation: /scripts/site-foundation.js.\n", ct);
        }
        await WriteAsync(root, "src/Host/Program.cs", kind == StudioProjectKind.Razor ? RazorProgram : HtmlProgram, ct);
        await WriteAsync(root, "src/Host/Host.csproj", WebProject, ct);
        await WriteAsync(root, "src/Host/wwwroot/assets/studio-landscape.svg", LandscapeSvg, ct);
        await WriteAsync(root, "src/Host/wwwroot/assets/studio-mark.svg", MarkSvg, ct);
        session.Project.Assets.AddRange(new[] { "studio-landscape.svg", "studio-mark.svg" }.Select(file => new VisualPageAsset
        {
            Name = Path.GetFileNameWithoutExtension(file), RelativePath = "src/Host/wwwroot/assets/" + file,
            MediaType = "image/svg+xml", AltText = file.Contains("landscape") ? "Abstract landscape with a green sun and flowing hills" : "Studio starter mark",
            Sha256 = VisualPageHashing.Sha256File(Path.Combine(root, "src", "Host", "wwwroot", "assets", file))
        }));
        session.Project.Revision = VisualPageHashing.ComputeProjectRevision(session.Project);
        await WriteAsync(root, VisualPageProjectService.ProjectManifestFileName, JsonSerializer.Serialize(session.Project, VisualPageJson.Options), ct);
        await WriteAsync(root, "README.md", blueprint is null ? Readme(name, kind, session.Project) : BlueprintReadme(name, kind, session.Project), ct);
        await StudioProjectFileHelpService.WriteGuideAsync(root, kind.ToString(), ct);
        // Reopen the final model through Studio's normal loader; malformed visual starters never become a published project.
        var reopened = await service.OpenAsync(root, cancellationToken: ct);
        if (service.InspectGeneratedDrift(reopened).Count != 0)
            throw new VisualPageValidationException("The starter generated an inconsistent page. The new project was not created.");
    }

    private VisualPageTemplateDocument Template(string name, StudioProjectKind kind, bool about)
    {
        var homeUrl = "/";
        var aboutUrl = kind == StudioProjectKind.Html ? "/about/" : "/about";
        var header = Element("header", "Site header", children:
        [
            Element("a", "Site brand", name, [("href", homeUrl), ("class", "brand")]),
            Element("button", "Mobile navigation toggle", "Menu", [("type", "button"), ("class", "nav-toggle"), ("aria-controls", "site-nav"), ("aria-expanded", "false")]),
            Element("nav", "Primary navigation", attributes: [("id", "site-nav"), ("aria-label", "Primary"), ("class", "site-nav")], children:
            [
                Element("a", "Home link", "Home", [("href", homeUrl), ("aria-current", about ? "false" : "page")]),
                Element("a", "About link", "About", [("href", aboutUrl), ("aria-current", about ? "page" : "false")]),
                Element("a", "Planner link", "Try the planner", [("href", "/#planner")]),
                Element("button", "Theme preference", "Switch theme", [("type", "button"), ("class", "theme-toggle"), ("aria-pressed", "false")])
            ])
        ]);
        var intro = Element("div", "Hero copy", attributes: [("class", "hero-copy")], children:
        [
            Element("p", "Eyebrow", about ? "A useful foundation" : "Your next idea starts here", [("class", "eyebrow")]),
            Element("h1", "Page heading", about ? $"Make {name} your own." : "A clear space for your next big idea."),
            Element("p", "Introduction", about ? "This project is small by design: readable source, local assets, and a real working baseline. Shape it around the people you want to reach." : "A complete, responsive starter with thoughtful details, accessible navigation, and a little room to experiment. Your content and your next chapter belong here.", [("class", "lede")]),
            Element("div", "Hero actions", attributes: [("class", "actions")], children:
            [
                Element("a", "Primary call to action", about ? "Back to the planner" : "Make a plan", [("href", "/#planner"), ("class", "button primary")]),
                Element("a", "Secondary call to action", about ? "Explore the features" : "Meet the project", [("href", about ? "/#features" : aboutUrl), ("class", "button secondary")])
            ])
        ]);
        var hero = Element("section", "Hero section", attributes: [("class", "hero"), ("aria-label", "Introduction")], children:
        [
            intro,
            Element("figure", "Hero illustration", attributes: [("class", "hero-art")], children:
            [
                Element("img", "Local landscape illustration", attributes: [("src", "/assets/studio-landscape.svg"), ("alt", "An abstract landscape with a green sun and flowing blue hills"), ("width", "720"), ("height", "560")]),
                Element("figcaption", "Illustration caption", "Built locally. Ready for your own perspective.")
            ])
        ]);
        hero.Layout[VisualPageBreakpoint.Base] = new() { Display = "flex", FlexDirection = "column", Gap = "2rem" };
        hero.Layout[VisualPageBreakpoint.Large] = new() { Display = "grid", GridTemplateColumns = "minmax(0, 1.1fr) minmax(0, 1fr)", Gap = "3rem", AlignItems = "center" };
        var cards = Element("section", "Feature cards", attributes: [("id", "features"), ("class", "features"), ("aria-label", "Starter features")], children:
        [
            Card("01", "Start with a working page", "Both pages, their navigation, and local artwork are ready. Replace the copy and structure in Canvas to make the experience yours."),
            Card("02", "Make the details yours", "A readable stylesheet holds the color palette, typography, cards, and responsive behavior. Change one token and the whole page responds."),
            Card("03", "Build one feature at a time", "Try the local planner below, then use its event handlers as the starting point for your own interaction. The README shows you where to begin.")
        ]);
        cards.Layout[VisualPageBreakpoint.Base] = new() { Display = "grid", GridTemplateColumns = "repeat(auto-fit, minmax(min(100%, 16rem), 1fr))", Gap = "1.2rem" };
        var content = new List<VisualPageElement> { header, hero, cards };
        if (!about) content.Add(Planner());
        else content.Add(Element("section", "Customization steps", attributes: [("class", "about-section"), ("aria-label", "Next steps")], children:
        [
            Element("h2", "Next steps heading", "Three good places to begin"),
            Element("ol", "Next steps", children:
            [
                Element("li", "Content step", "Give the Home page a real title and introduction in Canvas, then replace the illustration with your own image."),
                Element("li", "Style step", "Open home.custom.css, choose your palette, and preview at desktop and phone widths."),
                Element("li", "Behavior step", "Open home.custom.js to adapt the planner. Read README.md before adding hosting, storage, or deployment features.")
            ])
        ]));
        content.Add(Element("footer", "Site footer", attributes: [("class", "site-footer")], children:
        [
            Element("p", "Footer brand", name),
            Element("a", "Footer top link", "Back to top", [("href", "#top")]),
            Element("p", "Footer note", "Made with care. Built to grow.")
        ]));
        return new VisualPageTemplateDocument
        {
            Name = "Studio web application", Scope = "page", Library = "Studio starters", SuggestedPageTitle = about ? $"About · {name}" : name,
            SuggestedPageDescription = $"A responsive {name} website, ready to customize.",
            Root = new VisualPageElement { DescriptorId = "core.page", Name = about ? "About page" : "Home page", Attributes = new(StringComparer.Ordinal) { ["id"] = "top", ["class"] = "studio-site" }, Children = content }
        };
    }

    private VisualPageElement Card(string number, string title, string text) => Element("article", title, attributes: [("class", "feature-card")], children:
    [
        Element("p", "Feature number", number, [("class", "feature-number")]), Element("h2", "Feature title", title), Element("p", "Feature description", text)
    ]);

    private VisualPageElement Planner() => Element("section", "Local planner", attributes: [("id", "planner"), ("class", "planner"), ("aria-labelledby", "planner-heading")], children:
    [
        Element("div", "Planner introduction", children:
        [
            Element("p", "Planner eyebrow", "Try a working feature", [("class", "eyebrow")]), Element("h2", "Planner heading", "One small step, then another.", [("id", "planner-heading")]),
            Element("p", "Planner explanation", "Add a next step to your personal plan. It stays in this browser; you can check it off or remove it whenever you like.")
        ]),
        Element("form", "Planner form", attributes: [("id", "plan-form"), ("class", "plan-form"), ("aria-label", "Add a task")], children:
        [
            Element("label", "Task label", "What would you like to work on?", [("for", "plan-input")]),
            Element("div", "Task entry row", attributes: [("class", "input-row")], children:
            [
                Element("input", "Task input", attributes: [("id", "plan-input"), ("name", "task"), ("type", "text"), ("maxlength", "180"), ("required", "true"), ("placeholder", "Give your idea a first step"), ("autocomplete", "off")]),
                Element("button", "Add task", "Add step", [("type", "submit"), ("class", "button primary")])
            ])
        ]),
        Element("p", "Planner status", "Your plan is empty. Add your first step above.", [("id", "plan-status"), ("role", "status"), ("aria-live", "polite")]),
        Element("ul", "Planner tasks", attributes: [("id", "plan-list"), ("class", "plan-list"), ("aria-label", "Your next steps")]),
        Element("button", "Clear completed tasks", "Clear completed steps", [("id", "clear-completed"), ("type", "button"), ("class", "button secondary")]),
        Element("noscript", "JavaScript alternative", "The page and links work without JavaScript. Enable JavaScript to use the local planner, menu toggle, and theme preference.")
    ]);

    private VisualPageElement Element(string tag, string name, string text = "", (string Key, string Value)[]? attributes = null, VisualPageElement[]? children = null)
    {
        var attrs = (attributes ?? []).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (!_catalog.TryGetHtmlElement(tag, attrs, out var descriptor)) throw new VisualPageValidationException($"The starter element '{tag}' is unavailable.");
        return new VisualPageElement { DescriptorId = descriptor.Id, Name = name, Text = text, Attributes = attrs, Children = (children ?? []).ToList() };
    }

    private static async Task WriteAsync(string root, string path, string value, CancellationToken ct)
    {
        var destination = VisualPageProjectService.ResolveProjectPath(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, value, new UTF8Encoding(false), ct);
    }
}
