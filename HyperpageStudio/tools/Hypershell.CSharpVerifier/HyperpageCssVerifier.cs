using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;

internal static class HyperpageCssVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        Directory.CreateDirectory(verifierRoot);
        var receipts = new List<object>();
        foreach (var html in new[] { false, true })
        {
            var root = Path.Combine(verifierRoot, html ? "html-project" : "razor-project");
            var catalog = new VisualPageComponentCatalog();
            var validator = new VisualPageSchemaValidator(catalog);
            var generator = new VisualPageGenerator(catalog, validator);
            var projectService = new VisualPageProjectService(validator, generator, catalog);
            var session = html
                ? await projectService.CreateConfiguredAsync(root, "CSS HTML", new HyperpageProjectCreationOptions
                {
                    Foundation = new HyperpageProjectFoundation { MarkupLanguage = HyperpageMarkupLanguage.Html }
                }, "Css.Html")
                : await projectService.CreateAsync(root, "CSS Razor", "Css.Razor");
            // Existing projects predate stylesheetPaths; preserve recorded revisions and defaults.
            var oldPagePath = Path.Combine(root, session.Project.Pages[0].DocumentPath.Replace('/', Path.DirectorySeparatorChar));
            var oldPage = JsonNode.Parse(await File.ReadAllTextAsync(oldPagePath))!.AsObject();
            oldPage.Remove("stylesheetPaths");
            await File.WriteAllTextAsync(oldPagePath, oldPage.ToJsonString(VisualPageJson.Options));
            session = await projectService.OpenAsync(root, session.Page.PageId);
            Require(session.Page.StylesheetPaths.Count == 0, "An existing project without stylesheetPaths did not reopen unchanged.");
            using var tab = await CreateTabAsync(session, projectService, catalog, validator, generator, verifierRoot);
            var document = await tab.CreateProjectDocumentAsync("css", "motion theme.css", linkStylesheetToCurrentPage: true);
            Require(document is not null && tab.IsStylesheetLinked(document), "Creating plain CSS did not link the explicit checkbox choice.");
            var css = document!;
            var url = HyperpageStylesheetService.PublishedUrl(tab.Project, css.RelativePath);
            Require(url.Contains("motion%20theme.css", StringComparison.Ordinal), "CSS filenames were not URL escaped.");
            var unlinked = await tab.CreateProjectDocumentAsync("css", "unused.css");
            Require(unlinked is not null && !tab.IsStylesheetLinked(unlinked), "Unrequested stylesheets were linked globally.");
            Require(tab.SetStylesheetLinked(css, true) && tab.StylesheetPaths.Count == 1, "Linking CSS was not idempotent.");

            Require(tab.ApplyMutation(new VisualPageMutationSet(tab.Document.Revision,
                [new VisualPageMutationOperation { Kind = VisualPageMutationKind.SetAttribute,
                    TargetElementId = tab.Document.Root.ElementId, Name = "class", Value = "css-probe" }])), "CSS probe class failed.");
            var stylesheetDirectory = Path.GetDirectoryName(Path.Combine(root, css.RelativePath.Replace('/', Path.DirectorySeparatorChar)))!;
            var images = Path.Combine(stylesheetDirectory, "..", "images");
            Directory.CreateDirectory(images);
            var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5XcAAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(Path.Combine(images, "pixel.png"), pixel);
            await File.WriteAllTextAsync(Path.Combine(stylesheetDirectory, "tokens.css"), ".css-probe { --css-probe: rgb(12, 34, 56); }");
            css.Content = "@import './tokens.css';\n@import 'https://external.invalid/never.css';\n"
                + ".css-probe { color: var(--css-probe) !important; background-image: url('../images/pixel.png'); }\n"
                + "/* </style><script>globalThis.cssEscaped=true</script> */";
            tab.UserCss = ".css-probe { --companion-css: present; }";
            var uri = new Uri(HyperpageStylesheetService.CanvasOrigin + url);
            Require(tab.TryGetCanvasResource(uri, out var currentCss, out var mime)
                    && Encoding.UTF8.GetString(currentCss) == css.Content && mime.StartsWith("text/css", StringComparison.Ordinal),
                "Canvas did not resolve the unsaved CSS buffer at its published URL.");
            Require(tab.CanvasHtml.Contains(uri.AbsoluteUri, StringComparison.Ordinal)
                    && !tab.CanvasHtml.Contains("globalThis.cssEscaped", StringComparison.Ordinal),
                "Canvas CSS linking lost its URL or interpolated untrusted CSS into HTML.");
            await VerifyResourceBoundariesAsync(tab, verifierRoot, html ? "html" : "razor");
            var browser = await VerifyInBrowserAsync(tab, css, verifierRoot, html ? "html" : "razor");
            Require(await tab.SaveProjectAsync(), "Saving linked CSS failed: " + tab.StatusMessage);
            var reopenedSession = await projectService.OpenAsync(root, tab.Document.PageId);
            using (var reopened = await CreateTabAsync(reopenedSession, projectService, catalog, validator, generator, verifierRoot))
            {
                Require(reopened.StylesheetPaths.SequenceEqual(tab.StylesheetPaths)
                    && reopened.TryGetCanvasResource(uri, out var diskCss, out _)
                    && Encoding.UTF8.GetString(diskCss) == css.Content,
                    "Saved CSS/page references did not survive reopening.");
            }
            Require(tab.GeneratedRazor.Contains("href=\"" + url + "\"", StringComparison.Ordinal),
                "Generated markup omitted the linked plain CSS URL.");

            using (var preview = new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator,
                Path.Combine(verifierRoot, html ? "html-preview" : "razor-preview")))
            {
                var build = await preview.BuildLatestAsync(reopenedSession);
                Require(build.Outcome == VisualPageBuildOutcome.Succeeded, "Linked CSS preview build failed: " + build.Output);
                Require((await File.ReadAllTextAsync(Path.Combine(build.SnapshotPath,
                    VisualPageProjectService.GeneratedMarkupPath(tab.Project, tab.Document).Replace('/', Path.DirectorySeparatorChar))))
                        .Contains("href=\"" + url + "\"", StringComparison.Ordinal)
                    && (await File.ReadAllTextAsync(Path.Combine(build.SnapshotPath, css.RelativePath.Replace('/', Path.DirectorySeparatorChar)))) == css.Content,
                    "Isolated preview/export sources did not retain the stylesheet reference and bytes.");
                using var runtimeHost = new VisualPageLocalHostService(new BoundedProcessRunner());
                var runtime = await runtimeHost.StartAsync(build, new VisualPageHostRequest { TrustCustomCode = true, Port = 0 });
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var publishedCss = new Uri(new Uri(runtime.Url), url);
                Require(await client.GetStringAsync(publishedCss) == css.Content
                    && (await client.GetStringAsync(new Uri(publishedCss, "tokens.css"))).Contains("--css-probe", StringComparison.Ordinal)
                    && (await client.GetByteArrayAsync(new Uri(publishedCss, "../images/pixel.png"))).SequenceEqual(pixel),
                    "Published preview did not serve linked CSS and its relative dependencies at the generated URL.");
            }

            tab.SelectedProjectNode = FindNode(tab.ProjectTree, css.RelativePath);
            Require(!await tab.RenameSelectedProjectNodeAsync("renamed.css"), "Renaming linked CSS broke page references.");
            Require(tab.SetStylesheetLinked(css, false), "Unlink failed.");
            Require(!await tab.RenameSelectedProjectNodeAsync("renamed.css"), "Renaming after unsaved unlink broke the saved page.");
            tab.UndoCommand.Execute(null);
            Require(tab.IsStylesheetLinked(css), "Undo did not restore the stylesheet reference.");
            tab.RedoCommand.Execute(null);
            Require(!tab.IsStylesheetLinked(css), "Redo did not remove the stylesheet reference.");
            Require(await tab.SaveProjectAsync() && await tab.RenameSelectedProjectNodeAsync("renamed.css"),
                "Saved unlinked CSS could not be renamed: " + tab.StatusMessage);
            while (tab.UndoCommand.CanExecute(null))
            {
                tab.UndoCommand.Execute(null);
                Require(!tab.StylesheetPaths.Contains(css.RelativePath, StringComparer.OrdinalIgnoreCase)
                    && !tab.StylesheetPaths.Any(path => path.EndsWith("motion theme.css", StringComparison.Ordinal)),
                    "Undo restored a missing stylesheet after a filesystem rename.");
            }
            Require(await tab.SaveProjectAsync(), "Final fixture save failed.");

            if (!html)
            {
                foreach (var fileType in new[] { HyperpagePageFileType.Html, HyperpagePageFileType.Cshtml })
                {
                    Require(await tab.CreatePageAsync(new HyperpagePageCreationRequest(
                        "CSS " + fileType, "/css-" + fileType.ToString().ToLowerInvariant(), fileType, null, string.Empty)),
                        "Mixed page creation failed: " + tab.StatusMessage);
                    Require(tab.SetStylesheetLinked(css, true) && tab.GeneratedRazor.Contains(url.Replace("motion%20theme.css", "renamed.css"), StringComparison.Ordinal)
                        && await tab.SaveProjectAsync(), "Mixed markup CSS links failed: " + tab.StatusMessage);
                }
            }
            receipts.Add(new { foundation = html ? "html" : "razor", fileName = css.FileName, browser,
                checks = "manual-create,explicit-link,idempotent,live-buffer,relative-import,image,companion,save,reopen,preview-build,rename-guard,undo,resource-boundaries" });
            Console.WriteLine("HYPERPAGE_CSS_CASE_OK|" + (html ? "html" : "razor"));
        }
        await File.WriteAllTextAsync(Path.Combine(verifierRoot, "hyperpage-css.json"), JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("HYPERPAGE_CSS_OK|2-foundations|3-markup-types|live-browser|explicit-page-links|save-reopen|preview-build|local-css-imports|relative-images|blocked-external|rename-safe");
    }

    private static async Task<VisualPageStudioTabViewModel> CreateTabAsync(VisualPageProjectSession session,
        VisualPageProjectService projectService, VisualPageComponentCatalog catalog,
        VisualPageSchemaValidator validator, VisualPageGenerator generator, string verifierRoot) =>
        await VisualPageStudioTabViewModel.CreateAsync(session, projectService, new VisualPageMutationEngine(validator),
            generator, validator, catalog, new VisualPageTemplateService(catalog, Path.Combine(verifierRoot, "personal-templates")),
            new VisualPageDesignCanvasRenderer(catalog),
            new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator, Path.Combine(verifierRoot, "tab-preview")),
            new VisualPageLocalHostService(new BoundedProcessRunner()), new VisualPageUserSourceService(),
            new AtlasArchivePublicationService(new BoundedProcessRunner(), projectService, generator, validator,
                Path.Combine(verifierRoot, "publications")));

    private static async Task VerifyResourceBoundariesAsync(VisualPageStudioTabViewModel tab, string verifierRoot, string suffix)
    {
        foreach (var input in new[]
        {
            "https://external.invalid/styles/site.css", "file:///C:/Windows/win.ini",
            HyperpageStylesheetService.CanvasOrigin + "/../escape.css",
            HyperpageStylesheetService.CanvasOrigin + "/%2e%2e/escape.css",
            HyperpageStylesheetService.CanvasOrigin + "/styles/%2e%2e%5cescape.css",
            HyperpageStylesheetService.CanvasOrigin + "/styles/secret.cs",
            HyperpageStylesheetService.CanvasOrigin + "/styles/payload.js",
            HyperpageStylesheetService.CanvasOrigin + "/styles/site.css%3aextra"
        })
        {
            Require(!tab.TryGetCanvasResource(new Uri(input), out _, out _), "Canvas resource boundary allowed " + input);
        }
        Require(!VisualPageStudioView.IsCanvasResourceContextAllowed(CoreWebView2WebResourceContext.Script)
                && !VisualPageStudioView.IsCanvasResourceContextAllowed(CoreWebView2WebResourceContext.Document)
                && !VisualPageStudioView.IsCanvasResourceContextAllowed(CoreWebView2WebResourceContext.XmlHttpRequest),
            "Design canvas allowed executable/request contexts.");
        var invalid = VisualPageJson.Clone(tab.Document);
        invalid.StylesheetPaths = [null!];
        Require(new VisualPageSchemaValidator(new VisualPageComponentCatalog()).ValidatePage(invalid)
            .Any(diagnostic => diagnostic.Severity == VisualPageDiagnosticSeverity.Error), "Null stylesheet entries bypassed schema validation.");
        invalid.StylesheetPaths = null!;
        Require(new VisualPageSchemaValidator(new VisualPageComponentCatalog()).ValidatePage(invalid)
            .Any(diagnostic => diagnostic.Severity == VisualPageDiagnosticSeverity.Error), "Null stylesheet list bypassed schema validation.");

        var outside = Path.Combine(verifierRoot, "outside-" + suffix);
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.css"), "body { color: red; }");
        var junction = Path.Combine(tab.ProjectRoot, "src", "Host", "wwwroot", "escape-junction");
        var junctionResult = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest
        {
            ExecutablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = ["/c", "mklink", "/J", junction, outside],
            WorkingDirectory = verifierRoot, Timeout = TimeSpan.FromSeconds(10)
        });
        Require(junctionResult.ExitCode == 0, "Could not prepare the isolated junction boundary test: " + junctionResult.StandardError);
        try
        {
            Require(!tab.TryGetCanvasResource(new Uri(HyperpageStylesheetService.CanvasOrigin + "/escape-junction/secret.css"), out _, out _),
                "Canvas served CSS through a project junction escape.");
        }
        finally
        {
            // Remove only the fixture junction itself, never its target or contents.
            Directory.Delete(junction, recursive: false);
        }
    }

    private static Task<object> VerifyInBrowserAsync(VisualPageStudioTabViewModel tab,
        HyperpageProjectDocumentViewModel css, string verifierRoot, string suffix)
    {
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                var view = new WebView2();
                var window = new Window { Width = 960, Height = 700, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Content = view };
                try
                {
                    window.Show();
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(verifierRoot, "webview-" + suffix));
                    await view.EnsureCoreWebView2Async(environment);
                    var core = view.CoreWebView2;
                    var loadedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var blockedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += (_, args) =>
                    {
                        if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri.Scheme is "data" or "about") return;
                        if (VisualPageStudioView.IsCanvasResourceContextAllowed(args.ResourceContext)
                            && tab.TryGetCanvasResource(uri, out var bytes, out var mime))
                        {
                            loadedResources.Add(uri.AbsolutePath);
                            args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK",
                                "Content-Type: " + mime + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nAccess-Control-Allow-Origin: *");
                        }
                        else
                        {
                            blockedResources.Add(uri.AbsoluteUri);
                            args.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Blocked", "Content-Type: text/plain");
                        }
                    };
                    async Task<JsonElement> NavigateAndMeasure()
                    {
                        var navigation = new TaskCompletionSource<bool>();
                        void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs args) => navigation.TrySetResult(args.IsSuccess);
                        core.NavigationCompleted += Complete;
                        try
                        {
                            core.NavigateToString(tab.CanvasHtml);
                            Require(await navigation.Task.WaitAsync(TimeSpan.FromSeconds(20)), "Isolated CSS browser navigation failed.");
                        }
                        finally { core.NavigationCompleted -= Complete; }
                        var result = await core.ExecuteScriptAsync("(() => { const style=getComputedStyle(document.querySelector('.css-probe')); return { color:style.color, image:style.backgroundImage, companion:style.getPropertyValue('--companion-css').trim(), escaped:!!globalThis.cssEscaped }; })()");
                        return JsonDocument.Parse(result).RootElement.Clone();
                    }
                    var first = await NavigateAndMeasure();
                    Require(first.GetProperty("color").GetString() == "rgb(12, 34, 56)"
                        && first.GetProperty("companion").GetString() == "present"
                        && first.GetProperty("image").GetString()!.Contains("/images/pixel.png", StringComparison.Ordinal)
                        && !first.GetProperty("escaped").GetBoolean(), "Plain CSS/import/companion styles failed in real Chromium: " + first);
                    Require(loadedResources.Any(path => path.EndsWith("/tokens.css", StringComparison.Ordinal))
                            && loadedResources.Any(path => path.EndsWith("/images/pixel.png", StringComparison.Ordinal))
                            && blockedResources.Contains("https://external.invalid/never.css"),
                        "Relative imports/images did not load or external CSS escaped the canvas boundary.");
                    css.Content += "\n.css-probe { color: rgb(71, 82, 93) !important; }";
                    var edited = await NavigateAndMeasure();
                    Require(edited.GetProperty("color").GetString() == "rgb(71, 82, 93)", "Unsaved plain CSS did not update in the real browser.");
                    Require(tab.SetStylesheetLinked(css, false), "Browser unlink failed.");
                    var unlinked = await NavigateAndMeasure();
                    Require(unlinked.GetProperty("color").GetString() != "rgb(71, 82, 93)", "Unlinked CSS continued styling the page.");
                    Require(tab.SetStylesheetLinked(css, true), "Browser relink failed.");
                    completion.TrySetResult(new { initial = first, edited, unlinked, loadedResources, blockedResources });
                }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally
                {
                    view.Dispose();
                    window.Close();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Hyperpage CSS isolated Chromium verification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static HyperpageProjectTreeNodeViewModel? FindNode(IEnumerable<HyperpageProjectTreeNodeViewModel> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return node;
            if (FindNode(node.Children, path) is { } child) return child;
        }
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
