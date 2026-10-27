using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

public static class StudioRecoveryVerifier
{
    public static async Task VerifyAsync(string outputRoot)
    {
        var root = Path.Combine(Path.GetFullPath(outputRoot), "studio-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var assertions = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions.Add(message);
        }
        var fixture = new Fixture(root);
        var sessionService = new VisualPageStudioSessionService(Path.Combine(root, "recovery.json"));
        var original = await fixture.CreateTabAsync(create: true);
        var paths = new List<string>();
        foreach (var (type, name) in new[] { ("css", "extra.css"), ("javascript", "extra.js"), ("csharp", "Extra.cs") })
        {
            var document = await original.CreateProjectDocumentAsync(type, name)
                ?? throw new InvalidOperationException("Could not create recovery fixture: " + original.StatusMessage);
            paths.Add(document.RelativePath);
        }
        Check(await original.SaveProjectAsync(), "Initial additional source documents and layout save successfully.");
        foreach (var document in original.OpenProjectDocuments)
        {
            document.Content += "\n// Unsaved recovery " + document.LanguageId;
            document.ViewState = "{\"cursor\":17}";
        }
        var drafts = original.CaptureProjectDocumentRecovery();
        Check(drafts.Count == 3, "All dirty additional CSS, JavaScript, and C# documents enter the recovery snapshot.");
        var codePane = Panes(original.Layout.Root).First(pane => pane.PaneContents.Any(id => id.StartsWith(HyperpageProjectDocumentIds.Prefix, StringComparison.Ordinal)));
        Check(original.ClosePane(codePane.NodeId) && original.CaptureProjectDocumentRecovery().Count == 3,
            "Closing a code pane retains its dirty document drafts for recovery.");
        await sessionService.SaveAsync(Snapshot(original));
        original.Dispose();

        var loaded = await sessionService.LoadAsync();
        using (var restored = await fixture.CreateTabAsync())
        {
            Check(await restored.RestoreProjectDocumentRecoveryAsync(loaded.Projects.Single().ProjectDocuments) == 3,
                "Saved recovery reloads all additional source drafts into a fresh Studio tab.");
            Check(restored.OpenProjectDocuments.All(document => document.IsDirty && document.IsRecovered
                    && document.Content == drafts.Single(draft => draft.RelativePath == document.RelativePath).Content
                    && document.ViewState == "{\"cursor\":17}"),
                "Recovered documents preserve unsaved text, dirty state, and editor cursor state.");
            Check(paths.All(path => Panes(restored.Layout.Root).Any(pane => pane.PaneContents.Contains(HyperpageProjectDocumentIds.FromPath(path)))),
                "Recovered drafts reopen visible document tabs even when their old pane had been closed.");
            Check(await restored.SaveProjectAsync() && restored.OpenProjectDocuments.All(document => !document.IsDirty && !document.HasRecoveryNotice),
                "Saving recovered documents clears recovery notices after the exact text reaches disk.");
            Check(drafts.All(draft => File.ReadAllText(Path.Combine(fixture.ProjectRoot, draft.RelativePath)) == draft.Content),
                "Recovered CSS, JavaScript, and C# edits are written to their original files.");

            foreach (var document in restored.OpenProjectDocuments) document.Content += "\n// Second unsaved edit";
            await sessionService.SaveAsync(Snapshot(restored));
        }
        var changedPath = Path.Combine(fixture.ProjectRoot, paths[0]);
        var missingPath = Path.Combine(fixture.ProjectRoot, paths[1]);
        const string outsideEdit = "/* External editor wins until the recovered draft is reconciled. */";
        await File.WriteAllTextAsync(changedPath, outsideEdit);
        File.Delete(missingPath);
        loaded = await sessionService.LoadAsync();
        using (var conflicted = await fixture.CreateTabAsync())
        {
            Check(await conflicted.RestoreProjectDocumentRecoveryAsync(loaded.Projects.Single().ProjectDocuments) == 3,
                "External source edits and deletion do not prevent unsaved draft recovery.");
            Check(conflicted.OpenProjectDocuments.Single(document => document.RelativePath == paths[0]).HasRecoveryConflict
                && conflicted.OpenProjectDocuments.Single(document => document.RelativePath == paths[1]).RecoveryNotice.Contains("missing", StringComparison.Ordinal),
                "Recovery visibly identifies changed and missing source files.");
            Check(!await conflicted.SaveProjectAsync() && File.ReadAllText(changedPath) == outsideEdit && !File.Exists(missingPath),
                "Saving a recovered conflict cannot overwrite an external edit or recreate deleted source.");
            await sessionService.SaveAsync(Snapshot(conflicted));
        }
        loaded = await sessionService.LoadAsync();
        using (var secondRestart = await fixture.CreateTabAsync())
        {
            Check(await secondRestart.RestoreProjectDocumentRecoveryAsync(loaded.Projects.Single().ProjectDocuments) == 3
                && secondRestart.OpenProjectDocuments.Count(document => document.HasRecoveryConflict) == 2,
                "Changed-source and deleted-source drafts survive a second shutdown and recovery.");
            Check(secondRestart.CaptureProjectDocumentRecovery().Single(draft => draft.RelativePath == paths[0]).BaseContent != outsideEdit,
                "Repeated recovery retains the original disk baseline instead of trusting the later external edit.");
        }

        // The originally empty source case must not silently recreate a deleted file just because its hash equals empty text.
        using (var emptySource = await fixture.CreateTabAsync())
        {
            var recovery = new VisualPageStudioDocumentRecovery
            {
                RelativePath = "recovered-empty.js", Content = "const recovered = true;", BaseContent = "",
                BaseSha256 = VisualPageHashing.Sha256Text("")
            };
            Check(await emptySource.RestoreProjectDocumentRecoveryAsync([recovery]) == 1
                && !await emptySource.SaveProjectAsync() && !File.Exists(Path.Combine(fixture.ProjectRoot, recovery.RelativePath)),
                "A recovered draft from an originally empty, now-missing source cannot recreate it during Save.");
        }

        // Replay the exact interleaving after asynchronous disk I/O, before the editor's completion notification.
        var racePath = "save-race.js";
        await File.WriteAllTextAsync(Path.Combine(fixture.ProjectRoot, racePath), "baseline");
        var sources = new VisualPageUserSourceService();
        var baseline = await sources.LoadAsync(fixture.ProjectRoot, racePath);
        var race = new HyperpageProjectDocumentViewModel(baseline, "javascript", false) { Content = "first edit" };
        var submitted = race.Content;
        await sources.SaveAsync(fixture.ProjectRoot, baseline, submitted);
        race.Content = "new typing while Save completes";
        typeof(HyperpageProjectDocumentViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(race, [submitted]);
        Check(race.IsDirty && File.ReadAllText(Path.Combine(fixture.ProjectRoot, racePath)) == "first edit"
            && race.Content == "new typing while Save completes",
            "Typing during an asynchronous save remains dirty and is not mistaken for text already written to disk.");

        var valid = drafts[0];
        var invalidSnapshot = new VisualPageStudioSessionSnapshot { Projects = [new()
        {
            ProjectRoot = fixture.ProjectRoot, ProjectId = loaded.Projects[0].ProjectId, PageId = loaded.Projects[0].PageId,
            ProjectDocuments =
            [
                valid,
                new() { RelativePath = "../escape.js", Content = "escape", BaseSha256 = VisualPageHashing.Sha256Text("") },
                new() { RelativePath = "bad-hash.js", Content = "bad", BaseSha256 = "invalid" },
                new() { RelativePath = "huge.js", Content = new string('x', VisualPageUserSourceService.MaximumUserSourceCharacters + 1), BaseSha256 = VisualPageHashing.Sha256Text("") }
            ]
        }] };
        var invalidFile = Path.Combine(root, "invalid-recovery.json");
        await File.WriteAllTextAsync(invalidFile, JsonSerializer.Serialize(invalidSnapshot, VisualPageJson.Options));
        var filtered = await new VisualPageStudioSessionService(invalidFile).LoadAsync();
        Check(filtered.Projects.Single().ProjectDocuments.Count == 1,
            "Recovery rejects traversal paths, inconsistent baseline hashes, and oversized drafts without discarding a valid sibling.");
        var refused = false;
        try { await sessionService.SaveAsync(invalidSnapshot); }
        catch (VisualPageValidationException) { refused = true; }
        Check(refused, "Session saving refuses invalid or oversized recovery content before replacing the prior snapshot.");
        using (var protectedTab = await fixture.CreateTabAsync())
        {
            var manifest = File.ReadAllText(Path.Combine(fixture.ProjectRoot, VisualPageProjectService.ProjectManifestFileName));
            Check(await protectedTab.RestoreProjectDocumentRecoveryAsync([new()
            {
                RelativePath = VisualPageProjectService.ProjectManifestFileName, BaseContent = manifest,
                BaseSha256 = VisualPageHashing.Sha256Text(manifest), Content = "{}"
            }]) == 0, "Recovery cannot turn a Studio-managed project manifest into an editable document.");
            var retained = protectedTab.CaptureProjectDocumentRecovery();
            using var next = await fixture.CreateTabAsync();
            Check(retained.Count == 1 && await next.RestoreProjectDocumentRecoveryAsync(retained) == 0
                && next.CaptureProjectDocumentRecovery().Single().Content == "{}",
                "A valid draft whose path is now protected stays in recovery across repeated captures without becoming editable.");
            Check(next.HasUnsavedChanges && !await next.SaveProjectAsync(),
                "Pending-only recovered drafts keep the tab dirty and cannot be silently discarded by the close dialog's Save action.");
        }
        var oldFile = Path.Combine(root, "legacy-recovery.json");
        await File.WriteAllTextAsync(oldFile, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, projects = new[] { new { projectRoot = fixture.ProjectRoot, pageId = loaded.Projects[0].PageId } }
        }, VisualPageJson.Options));
        Check((await new VisualPageStudioSessionService(oldFile).LoadAsync()).Projects.Single().ProjectDocuments.Count == 0,
            "Older Studio session snapshots without additional-document recovery still load.");
        await File.WriteAllTextAsync(oldFile, "{\"schemaVersion\":1,\"projects\":null,\"namedLayoutPresets\":null}");
        Check((await new VisualPageStudioSessionService(oldFile).LoadAsync()).Projects.Count == 0,
            "A malformed null project collection loads as an empty recoverable session.");
        await File.WriteAllTextAsync(oldFile, "{\"schemaVersion\":1,\"projects\":[null]}");
        Check((await new VisualPageStudioSessionService(oldFile).LoadAsync()).Projects.Count == 0,
            "Null project entries are ignored instead of crashing startup recovery.");
        var previousSnapshot = await File.ReadAllTextAsync(Path.Combine(root, "recovery.json"));
        refused = false;
        try { await sessionService.SaveAsync(new() { Projects = [null!] }); }
        catch (VisualPageValidationException) { refused = true; }
        Check(refused && await File.ReadAllTextAsync(Path.Combine(root, "recovery.json")) == previousSnapshot,
            "Invalid in-memory project entries cannot replace the existing recovery file.");
        await RunStaAsync(async () =>
        {
            await VerifyAppPersistenceAsync(root, fixture.ProjectRoot, Check);
            await VerifyCompanionAndUnavailableRecoveryAsync(root, Check);
            RenderNotices(root, Check);
        });
        await File.WriteAllTextAsync(Path.Combine(root, "studio-recovery.json"), JsonSerializer.Serialize(new { passed = true, assertions }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"STUDIO_RECOVERY_OK|assertions:{assertions.Count}|renders:2|{root}");
    }

    private static VisualPageStudioSessionSnapshot Snapshot(VisualPageStudioTabViewModel tab) => new()
    {
        ActiveProjectId = tab.ProjectId,
        Projects = [new()
        {
            ProjectRoot = tab.ProjectRoot, ProjectId = tab.ProjectId, PageId = tab.PageId,
            SavedProjectRevision = tab.SavedProjectRevision, SavedPageRevision = tab.SavedPageRevision,
            RecoveryDocument = VisualPageJson.Clone(tab.Document), ProjectDocuments = tab.CaptureProjectDocumentRecovery().ToList()
        }]
    };

    private static async Task VerifyAppPersistenceAsync(string root, string projectRoot, Action<bool, string> check)
    {
        var settings = new SettingsService(Path.Combine(root, "private-profile"));
        var configuration = settings.Load();
        configuration.Application.CreateTerminalOnStartup = false;
        configuration.Explorer.StartupDirectory = root;
        settings.Save(configuration);
        string path;
        string draft;
        using (var workspace = new MainWindowViewModel(settings))
        {
            await workspace.FileExplorer.WhenIdleAsync();
            var tab = await workspace.OpenVisualPageProjectAsync(projectRoot)
                ?? throw new InvalidOperationException("Could not open recovery fixture in the application.");
            var document = await tab.CreateProjectDocumentAsync("css", "shutdown.css")
                ?? throw new InvalidOperationException(tab.StatusMessage);
            path = document.RelativePath;
            draft = document.Content + "\n/* Keep this unfinished application draft. */";
            document.Content = draft;
            await workspace.PrepareForShutdownAsync();
            var saved = await new VisualPageStudioSessionService(settings.VisualPageStudioSessionPath).LoadAsync();
            check(saved.Projects.Single().ProjectDocuments.Single(item => item.RelativePath == path).Content == draft,
                "The application's real shutdown persistence writes an additional CSS draft to ProjectDocuments.");
            check(File.ReadAllText(Path.Combine(projectRoot, path)) != draft,
                "Shutdown recovery preserves unfinished CSS without implicitly saving the source file.");
        }
        using (var reopened = new MainWindowViewModel(settings))
        {
            await reopened.FileExplorer.WhenIdleAsync();
            var restore = typeof(MainWindowViewModel).GetMethod("RestoreVisualPageSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var count = await (Task<int>)restore.Invoke(reopened, [CancellationToken.None])!;
            var document = reopened.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Single().OpenProjectDocuments.Single(item => item.RelativePath == path);
            check(count == 1 && document.IsDirty && document.IsRecovered && document.Content == draft,
                "The application's real startup restore path reopens the unsaved additional CSS draft.");
        }
    }

    private static async Task VerifyCompanionAndUnavailableRecoveryAsync(string outputRoot, Action<bool, string> check)
    {
        var root = Path.Combine(outputRoot, "companion-lifecycle");
        Directory.CreateDirectory(root);
        var fixture = new Fixture(root);
        using (var initial = await fixture.CreateTabAsync(create: true)) { }
        var settings = new SettingsService(Path.Combine(root, "private-profile"));
        var configuration = settings.Load();
        configuration.Application.CreateTerminalOnStartup = false;
        configuration.Explorer.StartupDirectory = root;
        settings.Save(configuration);
        var sessions = new VisualPageStudioSessionService(settings.VisualPageStudioSessionPath);
        string cssPath, additionalPath, originalId;
        const string additionalDraft = "/* Unsaved additional stylesheet */";
        using (var workspace = new MainWindowViewModel(settings))
        {
            await workspace.FileExplorer.WhenIdleAsync();
            var tab = (await workspace.OpenVisualPageProjectAsync(fixture.ProjectRoot))!;
            originalId = tab.ProjectId;
            cssPath = Path.Combine(fixture.ProjectRoot, VisualPageProjectService.UserCssPath(tab.Project, tab.Document));
            var document = (await tab.CreateProjectDocumentAsync("css", "extra-lifecycle.css"))!;
            additionalPath = document.RelativePath;
            document.Content = additionalDraft;
            await workspace.PrepareForShutdownAsync();
            var saved = (await sessions.LoadAsync()).Projects.Single();
            check(saved.HasSelectiveUserSourceRecovery && !saved.HasUserSourceRecovery && saved.UserSourceDocuments.Count == 0,
                "Dirty additional documents do not capture or replay clean built-in companion sources.");
        }
        const string externalCleanCss = "/* Latest external edit to an otherwise clean companion */";
        await File.WriteAllTextAsync(cssPath, externalCleanCss);
        string companionDraft;
        using (var workspace = new MainWindowViewModel(settings))
        {
            await workspace.FileExplorer.WhenIdleAsync();
            await RestoreWorkspaceAsync(workspace);
            var tab = workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Single();
            check(tab.UserCss == externalCleanCss && await tab.SaveProjectAsync() && File.ReadAllText(cssPath) == externalCleanCss,
                "Restoring and saving an additional draft preserves an unrelated external edit to clean companion CSS.");
            companionDraft = externalCleanCss + "\n/* Unfinished companion edit */";
            tab.UserCss = companionDraft;
            tab.OpenProjectDocuments.Single(item => item.RelativePath == additionalPath).Content += "\n/* Retain through missing project */";
            await workspace.PrepareForShutdownAsync();
            var recovery = (await sessions.LoadAsync()).Projects.Single().UserSourceDocuments.Single();
            check(recovery.BaseContent == externalCleanCss && recovery.BaseSha256 == VisualPageHashing.Sha256Text(externalCleanCss)
                && !recovery.BaselineUnavailable,
                "Dirty built-in CSS records its actual original disk baseline before shutdown.");
        }
        const string newerCss = "/* Newer external companion source */";
        await File.WriteAllTextAsync(cssPath, newerCss);
        using (var workspace = new MainWindowViewModel(settings))
        {
            await workspace.FileExplorer.WhenIdleAsync();
            await RestoreWorkspaceAsync(workspace);
            var tab = workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Single();
            check(tab.UserCss == companionDraft && tab.GetUserSourceRecoveryNotice("css").Contains("outside Studio", StringComparison.Ordinal)
                && !await tab.SaveProjectAsync() && File.ReadAllText(cssPath) == newerCss,
                "Recovered built-in CSS retains its draft while refusing to overwrite a newer external source.");
            await workspace.PrepareForShutdownAsync();
        }
        var hidden = Path.Combine(root, "project-unavailable");
        MoveFixture(fixture.ProjectRoot, hidden, root);
        try
        {
            using var unavailable = new MainWindowViewModel(settings);
            await unavailable.FileExplorer.WhenIdleAsync();
            check(await RestoreWorkspaceAsync(unavailable) == 0, "A temporarily unavailable project is deferred during app startup.");
            await unavailable.PrepareForShutdownAsync();
            var retained = (await sessions.LoadAsync()).Projects.Single();
            check(retained.ProjectId == originalId && retained.ProjectDocuments.Single().Content.Contains("Retain through missing project", StringComparison.Ordinal)
                && retained.UserSourceDocuments.Single().BaseContent == externalCleanCss,
                "Shutdown retains unavailable project drafts and original companion baselines instead of dropping them.");
        }
        finally { MoveFixture(hidden, fixture.ProjectRoot, root); }
        using (var returned = new MainWindowViewModel(settings))
        {
            await returned.FileExplorer.WhenIdleAsync();
            check(await RestoreWorkspaceAsync(returned) == 1, "A returned project reopens from its retained recovery entry.");
            var tab = returned.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Single();
            check(tab.UserCss == companionDraft && tab.OpenProjectDocuments.Single(item => item.RelativePath == additionalPath).IsRecovered,
                "Both additional and built-in drafts survive the unavailable-project cycle.");
            await returned.PrepareForShutdownAsync();
        }
        // A replacement at the same path must not consume the original project's recovery identity.
        MoveFixture(fixture.ProjectRoot, hidden, root);
        using (var replacement = await fixture.CreateTabAsync(create: true)) { }
        var replacementRoot = Path.Combine(root, "replacement-project");
        try
        {
            using var replaced = new MainWindowViewModel(settings);
            await replaced.FileExplorer.WhenIdleAsync();
            check(await RestoreWorkspaceAsync(replaced) == 0, "A different project at the old path does not receive the original drafts.");
            await replaced.OpenVisualPageProjectAsync(fixture.ProjectRoot);
            await replaced.PrepareForShutdownAsync();
            var saved = await sessions.LoadAsync();
            check(saved.Projects.Count == 2 && saved.Projects.Any(entry => entry.ProjectId == originalId && entry.ProjectDocuments.Count != 0),
                "Opening a replacement project retains the original recovery entry by project and page identity.");
        }
        finally
        {
            MoveFixture(fixture.ProjectRoot, replacementRoot, root);
            MoveFixture(hidden, fixture.ProjectRoot, root);
        }
        using (var legacy = await fixture.CreateTabAsync())
        {
            var disk = legacy.UserCss;
            legacy.RestoreUserSourceRecovery(legacy.UserCSharp, "/* Legacy draft with unknown baseline */", legacy.UserJavaScript);
            check(legacy.CaptureUserSourceRecovery().Single().BaselineUnavailable && !await legacy.SaveProjectAsync()
                && File.ReadAllText(cssPath) == disk,
                "Legacy source drafts stay copyable and cannot gain fabricated overwrite authority from the current disk version.");
            using var legacyAgain = await fixture.CreateTabAsync();
            await legacyAgain.RestoreUserSourceRecoveryAsync(legacy.CaptureUserSourceRecovery());
            check(legacyAgain.CaptureUserSourceRecovery().Single().BaselineUnavailable && !await legacyAgain.SaveProjectAsync(),
                "Unknown legacy baselines remain unknown and save-blocked after another recovery cycle.");
        }
    }

    private static Task<int> RestoreWorkspaceAsync(MainWindowViewModel workspace) =>
        (Task<int>)typeof(MainWindowViewModel).GetMethod("RestoreVisualPageSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(workspace, [CancellationToken.None])!;

    private static void MoveFixture(string source, string target, string fixtureRoot)
    {
        var boundary = Path.GetFullPath(fixtureRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(source).StartsWith(boundary, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(target).StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A fixture move escaped the isolated verification directory.");
        Directory.Move(source, target);
    }

    private static IEnumerable<StudioLayoutNode> Panes(StudioLayoutNode node)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane) yield return node;
        if (node.First is not null)
            foreach (var pane in Panes(node.First)) yield return pane;
        if (node.Second is not null)
            foreach (var pane in Panes(node.Second)) yield return pane;
    }

    private sealed class Fixture(string root)
    {
        public string ProjectRoot { get; } = Path.Combine(root, "project");
        public async Task<VisualPageStudioTabViewModel> CreateTabAsync(bool create = false)
        {
            var catalog = new VisualPageComponentCatalog();
            var validator = new VisualPageSchemaValidator(catalog);
            var generator = new VisualPageGenerator(catalog, validator);
            var projects = new VisualPageProjectService(validator, generator, catalog);
            var session = create ? await projects.CreateAsync(ProjectRoot, "Recovery fixture", "Recovery.Fixture") : await projects.OpenAsync(ProjectRoot);
            return await VisualPageStudioTabViewModel.CreateAsync(session, projects, new VisualPageMutationEngine(validator), generator,
                validator, catalog, new VisualPageTemplateService(catalog, Path.Combine(root, "personal-templates")),
                new VisualPageDesignCanvasRenderer(catalog),
                new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator, Path.Combine(root, "preview-cache")),
                new VisualPageLocalHostService(new BoundedProcessRunner()), new VisualPageUserSourceService(),
                new AtlasArchivePublicationService(new BoundedProcessRunner(), projects, generator, validator, Path.Combine(root, "publication")));
        }
    }

    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void RenderNotices(string root, Action<bool, string> check)
    {
        var source = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "src", "Hypershell", "App.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary",
            source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var document = new HyperpageProjectDocumentViewModel(new() { RelativePath = "recovered.js", Content = "", Sha256 = VisualPageHashing.Sha256Text("") }, "javascript", false);
        typeof(HyperpageProjectDocumentViewModel).GetMethod("RestoreRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(document,
            [new VisualPageStudioDocumentRecovery { RelativePath = "recovered.js", Content = "const recovered = true;", BaseSha256 = VisualPageHashing.Sha256Text("") }, true, false]);
        foreach (var width in new[] { 480d, 1080d })
        {
            var notice = (FrameworkElement)typeof(VisualPageStudioView).GetMethod("CreateDocumentRecoveryNotice", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [document])!;
            var surface = new DockPanel { Width = width, Height = 320, Resources = resources, Background = (Brush)resources["AppBackgroundBrush"] };
            DockPanel.SetDock(notice, Dock.Top); surface.Children.Add(notice);
            var editor = new TextBox { Text = document.Content, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Code"), Padding = new Thickness(16), BorderThickness = new Thickness(0) };
            surface.Children.Add(editor);
            surface.Measure(new Size(width, 320)); surface.Arrange(new Rect(0, 0, width, 320)); surface.UpdateLayout();
            surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            surface.UpdateLayout();
            check(notice.Visibility == Visibility.Visible && notice.ActualHeight > 32 && notice.ActualHeight < 160 && editor.ActualHeight > 160,
                $"The recovery notice and Copy draft action remain visible at {width}px while leaving usable editor space.");
            var bitmap = new RenderTargetBitmap((int)width, 320, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(root, $"recovery-{width:0}.png")); encoder.Save(output);
        }
    }
}
