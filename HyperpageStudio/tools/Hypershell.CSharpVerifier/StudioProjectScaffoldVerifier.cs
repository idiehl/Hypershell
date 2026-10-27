using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Creates real projects, builds the Studio preview snapshot, and exercises its running host.</summary>
public static class StudioProjectScaffoldVerifier
{
    public static async Task VerifyAsync(string outputDirectory)
    {
        var output = Path.Combine(Path.GetFullPath(outputDirectory), "web-starters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projects = new VisualPageProjectService(validator, generator, catalog);
        var scaffold = new StudioProjectScaffoldService();
        Check(VisualPageProjectService.ResolvePageFileType(new VisualPageProjectManifest(), HyperpagePageFileType.ProjectDefault)
            == HyperpagePageFileType.RazorPage, "existing component-library projects keep their Razor component default", checks);
        foreach (var kind in new[] { StudioProjectKind.Html, StudioProjectKind.Razor })
        {
            var root = await scaffold.CreateAsync(new StudioProjectCreationRequest
                { Kind = kind, Name = kind + " Starter", ParentDirectory = output });
            var session = await projects.OpenAsync(root);
            Check(session.Project.Pages.Count == 2, kind + " has both connected pages", checks);
            Check(session.Project.Pages.All(page => page.FileType == (kind == StudioProjectKind.Razor ? HyperpagePageFileType.Cshtml : HyperpagePageFileType.ProjectDefault)),
                kind + " uses the expected page type", checks);
            Check(!Directory.Exists(Path.Combine(root, "src", "Pages")), kind + " has a single host project without an unused component library", checks);
            var help = new StudioProjectFileHelpService();
            var guide = JsonSerializer.Deserialize<StudioProjectGuide>(await File.ReadAllTextAsync(Path.Combine(root, ".studio", "project-guide.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.StartsWith(".visual-page/")) continue;
                var entry = guide.Files.SingleOrDefault(item => item.RelativePath == relative);
                Check(entry is not null && !string.IsNullOrWhiteSpace(entry.Purpose) && !string.IsNullOrWhiteSpace(entry.EditGuidance)
                    && !string.IsNullOrWhiteSpace(entry.GettingStarted), kind + " guide covers " + relative, checks);
                Check(help.GetHelp(root, relative)?.Purpose == entry!.Purpose, kind + " contextual help resolves " + relative, checks);
            }
            Check(help.GetHelp(root, "canvas")?.Ownership == "Visual authoring", kind + " canvas help explains visual editing", checks);
            Check(help.GetHelp(root, "../outside.txt") is null, kind + " help rejects paths outside the project", checks);

            // Source changes and Canvas changes must coexist; generation may only replace its managed artifacts.
            var css = Path.Combine(root, "src", "Host", "wwwroot", "assets", "home.custom.css");
            var js = Path.Combine(root, "src", "Host", "wwwroot", "assets", "home.custom.js");
            await File.AppendAllTextAsync(css, "\n/* retained user style */\n");
            await File.AppendAllTextAsync(js, "\n// retained user behavior\n");
            var cssBefore = await File.ReadAllBytesAsync(css);
            var jsBefore = await File.ReadAllBytesAsync(js);
            var oldProject = session.Project.Revision;
            var oldPage = session.Page.Revision;
            Flatten(session.Page.Root).First(element => catalog.GetRequired(element.DescriptorId).MarkupName == "h1").Text = "A verified working starter";
            session.Page.Revision = VisualPageHashing.ComputePageRevision(session.Page, oldPage);
            await projects.SaveAsync(session, oldProject, oldPage);
            var cssAfter = await File.ReadAllBytesAsync(css);
            var jsAfter = await File.ReadAllBytesAsync(js);
            Check(cssBefore.SequenceEqual(cssAfter) && jsBefore.SequenceEqual(jsAfter), kind + " Canvas save preserves editable CSS and JavaScript", checks);
            Check(projects.InspectGeneratedDrift(session).Count == 0, kind + " Canvas save leaves no managed-source drift", checks);

            var stylesheetDirectory = HyperpageDocumentTypeCatalog.DefaultDirectory(HyperpageDocumentTypeCatalog.GetRequired("css"), session.Project);
            var scriptDirectory = HyperpageDocumentTypeCatalog.DefaultDirectory(HyperpageDocumentTypeCatalog.GetRequired("javascript"), session.Project);
            var codeDirectory = HyperpageDocumentTypeCatalog.DefaultDirectory(HyperpageDocumentTypeCatalog.GetRequired("csharp"), session.Project);
            Check(stylesheetDirectory == "src/Host/wwwroot/styles" && scriptDirectory == "src/Host/wwwroot/scripts" && codeDirectory == "src/Host/Code",
                kind + " new source defaults land in compiled or publicly hosted folders", checks);
            var stylesheetRelative = stylesheetDirectory + "/qa.css";
            var stylesheetFile = VisualPageProjectService.ResolveProjectPath(root, stylesheetRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(stylesheetFile)!);
            await File.WriteAllTextAsync(stylesheetFile, ".starter-source-check { color: green; }");
            session.Page.StylesheetPaths.Add(stylesheetRelative);
            oldProject = session.Project.Revision;
            oldPage = session.Page.Revision;
            session.Page.Revision = VisualPageHashing.ComputePageRevision(session.Page, oldPage);
            await projects.SaveAsync(session, oldProject, oldPage);
            Check(HyperpageStylesheetService.PublishedUrl(session.Project, stylesheetRelative) == "/styles/qa.css", kind + " new stylesheet has an actual web-host URL", checks);
            if (kind == StudioProjectKind.Razor)
            {
                var helperPath = VisualPageProjectService.ResolveProjectPath(root, codeDirectory + "/StarterCheck.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
                await File.WriteAllTextAsync(helperPath, "namespace " + session.Project.RootNamespace + "; public static class StarterCheck { public static string Value => \"compiled helper\"; }");
                var programPath = Path.Combine(root, "src", "Host", "Program.cs");
                var program = await File.ReadAllTextAsync(programPath);
                await File.WriteAllTextAsync(programPath, program.Replace("app.Run();", "app.MapGet(\"/api/source-check\", () => " + session.Project.RootNamespace + ".StarterCheck.Value);\napp.Run();"));
            }

            var homePageId = session.Page.PageId;
            var extra = await projects.AddPageAsync(session, "Extra verified page", "/extra");
            Check(VisualPageProjectService.ResolvePageFileType(extra.Project, extra.Page) ==
                (kind == StudioProjectKind.Razor ? HyperpagePageFileType.Cshtml : HyperpagePageFileType.Html), kind + " new default page uses the hosted page type", checks);
            var extraScript = Path.Combine(root, "src", "Host", "wwwroot", "assets", "extra-verified-page.custom.js");
            var scriptCheck = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest
                { ExecutablePath = ResolveNodeExecutable(), Arguments = ["--check", extraScript], WorkingDirectory = root, Timeout = TimeSpan.FromSeconds(15) });
            Check(scriptCheck.ExitCode == 0, kind + " new page has valid classic browser JavaScript", checks);
            session = await projects.OpenAsync(root, homePageId);

            using var builder = new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator, Path.Combine(output, "preview-" + kind));
            var build = await builder.BuildLatestAsync(session);
            Check(build.Outcome == VisualPageBuildOutcome.Succeeded, kind + " real Studio preview builds: " + build.Output, checks);
            await ExerciseHostAsync(build.SnapshotPath, kind, checks);
            await ExerciseJavaScriptAsync(js, output, kind, checks);
        }

        var protectedRoot = Path.Combine(output, "Existing");
        Directory.CreateDirectory(protectedRoot);
        await File.WriteAllTextAsync(Path.Combine(protectedRoot, "keep.txt"), "existing draft");
        await ExpectRejectedAsync(() => scaffold.CreateAsync(new StudioProjectCreationRequest { Kind = StudioProjectKind.Html, Name = "Existing", ParentDirectory = output }));
        Check(await File.ReadAllTextAsync(Path.Combine(protectedRoot, "keep.txt")) == "existing draft", "existing project is preserved", checks);
        foreach (var name in new[] { "../escape", "..", "CON", "LPT1.txt", "broken.", "invalid:name" })
        {
            await ExpectRejectedAsync(() => scaffold.CreateAsync(new StudioProjectCreationRequest { Kind = StudioProjectKind.Html, Name = name, ParentDirectory = output }));
            checks.Add("invalid name rejected: " + name);
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await scaffold.CreateAsync(new StudioProjectCreationRequest { Kind = StudioProjectKind.Razor, Name = "Cancelled", ParentDirectory = output }, cancelled.Token);
            throw new InvalidOperationException("Cancelled creation unexpectedly succeeded.");
        }
        catch (OperationCanceledException) { }
        Check(!Directory.Exists(Path.Combine(output, "Cancelled")) && !Directory.EnumerateDirectories(output, ".studio-create-*").Any(), "cancelled creation leaves no target or partial staging folder", checks);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "studio-project-scaffold-receipt.json"), JsonSerializer.Serialize(new { output, checks, count = checks.Count }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("STUDIO_PROJECT_SCAFFOLD_OK|" + checks.Count + " checks|" + output);
    }

    private static async Task ExerciseHostAsync(string snapshot, StudioProjectKind kind, List<string> checks)
    {
        var executable = VisualPagePreviewBuildService.ResolveDotNetExecutable() ?? throw new InvalidOperationException(".NET 10 SDK is required.");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = snapshot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "run", "--no-build", "--project", Path.Combine(snapshot, "src", "Host", "Host.csproj"), "--urls", "http://127.0.0.1:0" }) start.ArgumentList.Add(argument);
        using var host = Process.Start(start) ?? throw new InvalidOperationException("The starter host did not start.");
        var stderr = host.StandardError.ReadToEndAsync();
        try
        {
            string? url = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!timeout.IsCancellationRequested)
            {
                var line = await host.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null) break;
                var offset = line.IndexOf("http://127.0.0.1:", StringComparison.Ordinal);
                if (offset >= 0) { url = line[offset..].Trim().TrimEnd('/'); break; }
            }
            Check(url is not null, kind + " starts on an automatically assigned loopback port", checks);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var home = await client.GetStringAsync(url + "/");
            Check(home.Contains("A verified working starter") && home.Contains("id=\"plan-form\"") && home.Contains("id=\"plan-input\"") && home.Contains("href=\"/styles/qa.css\""), kind + " renders edited Canvas content, planner controls, and newly attached CSS", checks);
            var about = await client.GetStringAsync(url + "/about/");
            Check(about.Contains("Three good places to begin") && about.Contains("href=\"/\""), kind + " second page renders with home navigation", checks);
            var extra = await client.GetStringAsync(url + "/extra/");
            Check(extra.Contains("Extra verified page"), kind + " default added page is compiled and reachable", checks);
            Check(!home.Contains("blazor.web.js") && !about.Contains("blazor.web.js"), kind + " browser output has no component runtime dependency", checks);
            foreach (var asset in new[] { "/assets/studio-landscape.svg", "/assets/studio-mark.svg", "/assets/home.custom.css", "/assets/home.custom.js", "/assets/about.custom.css", "/assets/about.custom.js", "/assets/hyperpage-theme.css", "/styles/qa.css" })
            {
                using var response = await client.GetAsync(url + asset);
                Check(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsByteArrayAsync()).Length > 0, kind + " serves " + asset, checks);
            }
            using var missing = await client.GetAsync(url + "/not-a-page");
            Check(missing.StatusCode == HttpStatusCode.NotFound, kind + " reports missing routes as 404", checks);
            if (kind == StudioProjectKind.Razor)
            {
                var status = await client.GetStringAsync(url + "/api/status");
                Check(status.Contains("Your server is ready."), "Razor server endpoint works", checks);
                var source = await client.GetStringAsync(url + "/api/source-check");
                Check(source == "compiled helper", "new Razor C# source is compiled and executed", checks);
            }
        }
        finally
        {
            if (!host.HasExited) host.Kill(entireProcessTree: true);
            await host.WaitForExitAsync();
            await stderr;
        }
    }

    private static async Task ExerciseJavaScriptAsync(string script, string output, StudioProjectKind kind, List<string> checks)
    {
        var testPath = Path.Combine(output, "starter-browser-contract.cjs");
        await File.WriteAllTextAsync(testPath, JavaScriptHarness);
        var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest
        {
            ExecutablePath = ResolveNodeExecutable(), Arguments = [testPath, script], WorkingDirectory = output,
            Timeout = TimeSpan.FromSeconds(20)
        });
        Check(result.ExitCode == 0 && result.StandardOutput.Contains("STARTER_BROWSER_OK"), kind + " browser handlers support add, complete, reload, remove, theme, menu, invalid-storage and storage-failure behavior: " + result.StandardError + result.FailureMessage, checks);
    }

    private static string ResolveNodeExecutable()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(installed)) return installed;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory.Trim('"'), "node.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("Node.js is required for the starter's browser contract verification.");
    }

    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var element in Flatten(child)) yield return element;
    }

    private static void Check(bool condition, string message, List<string> checks)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks.Add(message);
    }

    private static async Task ExpectRejectedAsync(Func<Task<string>> action)
    {
        try { await action(); throw new InvalidOperationException("Unsafe creation was not rejected."); }
        catch (VisualPageValidationException) { }
    }

    private const string JavaScriptHarness = """
        const fs = require('node:fs');
        const vm = require('node:vm');
        const assert = require('node:assert/strict');
        const source = fs.readFileSync(process.argv[2], 'utf8');
        const store = new Map();
        function boot(failWrites = false) {
          class Node {
            constructor(tag = 'div') { this.tagName = tag; this.dataset = {}; this.children = []; this.events = {}; this.attributes = {}; this.value = ''; this.checked = false; this.disabled = false; this.textContent = ''; this.classList = { add: () => {} }; }
            addEventListener(name, fn) { this.events[name] = fn; }
            setAttribute(name, value) { this.attributes[name] = value; }
            append(...items) { this.children.push(...items); }
            replaceChildren(...items) { this.children = [...items]; }
            focus() { this.focused = true; }
            querySelectorAll() { return this.children; }
            fire(name) { this.events[name]?.({ preventDefault() {} }); }
          }
          const nodes = Object.fromEntries(['.theme-toggle', '.nav-toggle', '#site-nav', '#plan-form', '#plan-input', '#plan-list', '#plan-status', '#clear-completed'].map(key => [key, new Node()]));
          nodes['#site-nav'].append(new Node('a'));
          const doc = { documentElement: new Node('html'), querySelector: key => nodes[key] ?? null, createElement: tag => new Node(tag) };
          const context = { document: doc, localStorage: { getItem: key => store.get(key) ?? null, setItem: (key, value) => { if (failWrites) throw Error('unavailable'); store.set(key, value); } }, crypto: { randomUUID: () => String(Math.random()) }, console };
          vm.runInNewContext(source, context);
          return { nodes, doc };
        }
        let app = boot();
        app.nodes['#plan-input'].value = '<img onerror="alert(1)"> safely treated as text';
        app.nodes['#plan-form'].fire('submit');
        assert.equal(app.nodes['#plan-list'].children.length, 1);
        assert.equal(app.nodes['#plan-list'].children[0].children[1].textContent, '<img onerror="alert(1)"> safely treated as text');
        app.nodes['#plan-list'].children[0].children[0].checked = true;
        app.nodes['#plan-list'].children[0].children[0].fire('change');
        assert.match(app.nodes['#plan-status'].textContent, /^1 of 1/);
        app = boot(); assert.equal(app.nodes['#plan-list'].children.length, 1);
        assert.equal(app.nodes['#plan-list'].children[0].children[0].checked, true);
        app.nodes['#clear-completed'].fire('click'); assert.equal(app.nodes['#plan-list'].children.length, 0);
        app.nodes['#plan-input'].value = 'Second step'; app.nodes['#plan-form'].fire('submit');
        app.nodes['#plan-list'].children[0].children[2].fire('click'); assert.equal(app.nodes['#plan-list'].children.length, 0);
        app.nodes['.theme-toggle'].fire('click'); assert.equal(app.doc.documentElement.dataset.theme, 'dark');
        app.nodes['.nav-toggle'].fire('click'); assert.equal(app.nodes['#site-nav'].dataset.open, 'true');
        app.nodes['#site-nav'].children[0].fire('click'); assert.equal(app.nodes['#site-nav'].dataset.open, 'false');
        app = boot(); assert.equal(app.doc.documentElement.dataset.theme, 'dark');
        store.set('studio-starter.plan.v1', '{broken'); app = boot(); assert.equal(app.nodes['#plan-list'].children.length, 0);
        store.set('studio-starter.plan.v1', JSON.stringify([{ id: 'bad', text: 1 }, { id: 'ok', text: 'Recovered', done: false }]));
        app = boot(); assert.equal(app.nodes['#plan-list'].children.length, 1);
        app = boot(true); app.nodes['#plan-input'].value = 'Volatile'; app.nodes['#plan-form'].fire('submit');
        assert.match(app.nodes['#plan-status'].textContent, /storage is unavailable/);
        console.log('STARTER_BROWSER_OK');
        """;
}
