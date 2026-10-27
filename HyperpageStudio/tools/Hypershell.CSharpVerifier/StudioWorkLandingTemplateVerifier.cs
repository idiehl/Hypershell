using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Services;

/// <summary>Compiles all shipped Work entrances together, then inspects their actual responsive reader output.</summary>
public static class StudioWorkLandingTemplateVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, string repositoryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var fixture = Path.Combine(Path.GetFullPath(output), "wl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fixture);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var sourceBefore = Snapshot(sourceLibraryRoot);
        var definitions = WorkLandingTemplateCatalog.List();
        Check(definitions.Count == 10 && definitions.Count(item => WorkLandingTemplateCatalog.Project(item.Id) == "Atlas") == 3,
            "The collection contains ten distinct complete Work entrances, including three Atlas designs.");
        Check(definitions.All(item => item.Category == "Landing page" && !item.Title.Contains("Atlas", StringComparison.OrdinalIgnoreCase))
            && definitions.Select(item => item.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10,
            "All names are unique and descriptive, with Landing page category and separate Atlas project attribution.");
        var gallery = new StudioCompleteTemplateService(sourceLibraryRoot).List();
        Check(definitions.All(item => gallery.Any(choice => choice.Id == "hyperlib:" + item.Id && choice.Type == "Page"
            && choice.Project == WorkLandingTemplateCatalog.Project(item.Id) && choice.Category == "Landing page")),
            "Every new design is an actual complete Page in the unified gallery with its proper Type and Project.");
        foreach (var item in definitions)
        {
            var schema = WorkLandingTemplateCatalog.Create(item.Id);
            TemplateValidator.ValidateDefinition(schema);
            Check(schema.Blocks.Count == 1 && schema.DefaultBlocks.Count == 1 && schema.Blocks[0].Component == "WorkLandingBlock",
                item.Title + " has one complete editable entrance and does not duplicate primitive Elements in the catalog.");
        }
        var project = await new HyperlibWorkProjectService(sourceLibraryRoot).CreateAsync(fixture, "s",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Landing collection verification"), ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!;
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        using var catalog = new StudioAuthoringLibraryService(sourceLibraryRoot, Path.Combine(fixture, "c"));
        var importedPages = new List<string>();
        var landingBlocks = new List<PageBlock>();
        foreach (var item in definitions)
        {
            var change = await catalog.AddAsync(project, "page:hyperlib:" + item.Id, pipeline.LandingPageId, "main", state.Revision,
                item.Title, pipeline.WorkId, ct, targetRole: "page");
            state = change.State;
            importedPages.Add(change.PageId);
            Check(change.PageId != pipeline.LandingPageId && state.Pages[change.PageId].Blocks.Count == 1,
                item.Title + " imports through the production complete-template path with an independent real block.");
            landingBlocks.AddRange(ContentJson.Clone(state.Pages[change.PageId].Blocks));
        }
        // This source package predates the landing variants: importing it must not invalidate any of the ten existing Pages.
        var priorVariants = importedPages.ToDictionary(id => id, id => state.Nodes.Single(node => node.Id == id).Appearance["variant"].GetRawText());
        var dashboard = await catalog.AddAsync(project, "page:hyperlib:insight-dashboard", pipeline.LandingPageId, "main", state.Revision,
            "Ordinary content after landing templates", pipeline.WorkId, ct, targetRole: "page");
        state = dashboard.State;
        importedPages.Add(dashboard.PageId);
        var merged = TemplateObjectLibrary.ReadDefinition(project);
        Check(priorVariants.All(pair => state.Nodes.Single(node => node.Id == pair.Key).Appearance["variant"].GetRawText() == pair.Value)
            && definitions.All(item => merged.Appearance.Single(field => field.Id == "variant").Options.Contains(item.Id)),
            "An older ordinary Dashboard template preserves every existing landing variant and all newly declared appearance options.");
        TemplateValidator.EnsureCompatible(merged, state.Pages.Values.Where(page => page.WorkId == pipeline.WorkId),
            state.Nodes.Where(node => node.WorkId == pipeline.WorkId).Select(node => node.Appearance));
        var schemaAfter = TemplateObjectLibrary.ReadDefinition(project);
        var landing = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        landing.Blocks = landingBlocks;
        landing.UsesEditableChrome = true;
        var emptyPage = new PageDocument { TemplateId = schemaAfter.TemplateId, UsesEditableChrome = true };
        state = await store.ApplyAsync(new()
        {
            ExpectedRevision = state.Revision, Description = "Arrange independent landing designs with a real nested Work",
            Operations = [.. importedPages.Select(id => new ContentOperation { Kind = "deleteNode", Id = id }),
                new() { Kind = "savePage", Page = landing },
                new() { Kind = "createNode", Id = "fixture-volume", NodeType = "volume", ParentId = pipeline.WorkId, Title = "Volume One", Order = 10 },
                new() { Kind = "createNode", Id = "fixture-chapter", NodeType = "chapter", ParentId = "fixture-volume", Title = "Chapter One", Order = 1 },
                new() { Kind = "createNode", Id = "fixture-nested-first", NodeType = "page", ParentId = "fixture-chapter", Title = "First nested Page", Order = 1, Page = ContentJson.Clone(emptyPage) },
                new() { Kind = "createNode", Id = "fixture-nested-second", NodeType = "page", ParentId = "fixture-chapter", Title = "Second nested Page", Order = 2, Page = ContentJson.Clone(emptyPage) },
                new() { Kind = "createNode", Id = "fixture-root-last", NodeType = "page", ParentId = pipeline.WorkId, Title = "A Page outside the Chapter", Order = 20, Page = ContentJson.Clone(emptyPage) }]
        }, ct);
        var expected = new[] { new { id = "fixture-nested-first", title = "First nested Page", group = "Volume One / Chapter One" },
            new { id = "fixture-nested-second", title = "Second nested Page", group = "Volume One / Chapter One" },
            new { id = "fixture-root-last", title = "A Page outside the Chapter", group = "" } };
        using var hosts = new HyperlibWorkPreviewHostService();
        var endpoint = await hosts.EnsureAsync(library, ct);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        using var response = await http.GetAsync(new Uri(endpoint, "api/runtime/" + Uri.EscapeDataString(pipeline.WorkId)), ct);
        var runtimeText = await response.Content.ReadAsStringAsync(ct);
        Check(response.IsSuccessStatusCode, "The real project renderer compiles all ten generated components together: " + runtimeText);
        using var runtime = JsonDocument.Parse(runtimeText);
        var prefix = runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
        var url = new Uri(endpoint, prefix + "page/" + Uri.EscapeDataString(pipeline.LandingPageId) + "?edit=0");
        var config = Path.Combine(fixture, "browser-config.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { url = url.AbsoluteUri, output = Path.GetFullPath(output), fixture,
            workTitle = "Landing collection verification", landingId = pipeline.LandingPageId, workId = pipeline.WorkId, expected,
            designs = definitions.Select(item => new { id = item.Id, title = item.Title, project = WorkLandingTemplateCatalog.Project(item.Id), background = item.Background }).ToArray() }, ContentJson.Options), ct);
        var script = Path.Combine(repositoryRoot, "tools", "verify-work-landing-templates.cjs");
        var browserTask = new BoundedProcessRunner().RunAsync(new()
        {
            ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
            Arguments = [script, config], WorkingDirectory = fixture, Timeout = TimeSpan.FromMinutes(6),
            MaximumStandardOutputCharacters = 30000, MaximumStandardErrorCharacters = 30000
        }, ct);
        var ready = Path.Combine(fixture, "browser-ready.json");
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(ready) && !browserTask.IsCompleted && started.Elapsed < TimeSpan.FromMinutes(3)) await Task.Delay(100, ct);
        Check(File.Exists(ready), "All ten actual landing components rendered and reached the live-content checkpoint. "
            + (browserTask.IsCompleted ? (await browserTask).StandardError : ""));
        Directory.CreateDirectory(Path.Combine(library, "Media"));
        await File.WriteAllTextAsync(Path.Combine(library, "Media", "landing-cover.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"640\" height=\"640\" viewBox=\"0 0 640 640\"><rect width=\"640\" height=\"640\" fill=\"#123456\"/><circle cx=\"320\" cy=\"320\" r=\"180\" fill=\"#c8963e\"/></svg>", ct);
        state = await store.ReadAsync(ct);
        landing = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        landing.Blocks[0].Values["brand"] = ContentJson.Value("A personally edited brand");
        landing.Blocks[0].Values["text"] = ContentJson.Value("An introduction edited after the renderer was already running.");
        landing.Blocks[0].Values["cover"] = ContentJson.Value("/media/landing-cover.svg");
        landing.Blocks[0].Values["coverAlt"] = ContentJson.Value("Locally owned gold circle cover");
        state = await store.ApplyAsync(new()
        {
            ExpectedRevision = state.Revision, Description = "Change real Pages and owned landing fields while the renderer is live",
            Operations = [new() { Kind = "updateNode", Id = "fixture-nested-second", Title = "Renamed nested Page", Order = 0 },
                new() { Kind = "createNode", Id = "fixture-added-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "A newly added Page", Order = 15, Page = ContentJson.Clone(emptyPage) },
                new() { Kind = "savePage", Page = landing }]
        }, ct);
        await File.WriteAllTextAsync(Path.Combine(fixture, "state-updated.json"), JsonSerializer.Serialize(new { expected = new[] {
            new { id = "fixture-nested-second", title = "Renamed nested Page", group = "Volume One / Chapter One" },
            new { id = "fixture-nested-first", title = "First nested Page", group = "Volume One / Chapter One" },
            new { id = "fixture-added-page", title = "A newly added Page", group = "" },
            new { id = "fixture-root-last", title = "A Page outside the Chapter", group = "" } }, revision = state.Revision }, ContentJson.Options), ct);
        var browser = await browserTask;
        Check(browser.ExitCode == 0, "Actual reader browser acceptance: " + browser.StandardOutput + browser.StandardError);
        var browserReport = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(output, "work-landing-templates-browser-result.json"), ct));
        Check(browserReport.GetProperty("passed").GetBoolean(), "Browser proof confirms responsive design, exact live links, inline fields and newly added content.");
        var sourceAfter = Snapshot(sourceLibraryRoot);
        Check(sourceBefore.Count == sourceAfter.Count && sourceBefore.All(pair => sourceAfter.GetValueOrDefault(pair.Key) == pair.Value),
            "All source-library files remain byte-identical; generated templates, imports, media and content edits belong only to the disposable project.");
        var status = "STUDIO_WORK_LANDING_TEMPLATES_OK|checks=" + checks.Count + "|designs=10|renders=" + browserReport.GetProperty("screenshots").GetArrayLength();
        await File.WriteAllTextAsync(Path.Combine(output, "studio-work-landing-templates-result.json"), JsonSerializer.Serialize(new { result = status, checks,
            fixture, project, library, url = url.AbsoluteUri, sourceFiles = sourceBefore.Count, designs = definitions, browser = browserReport }, ContentJson.Options), ct);
        return status;
    }

    private static Dictionary<string, string> Snapshot(string root)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".hyperlib", "bin", "obj", "app", "node_modules", "artifacts", "output", ".vs" };
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string directory)
        {
            VisualPageProjectService.RejectReparsePoint(directory);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                VisualPageProjectService.RejectReparsePoint(file);
                result.Add(Path.GetRelativePath(root, file), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
            }
            foreach (var child in Directory.EnumerateDirectories(directory).Where(path => !excluded.Contains(Path.GetFileName(path)))) Visit(child);
        }
        Visit(root);
        return result;
    }
}
