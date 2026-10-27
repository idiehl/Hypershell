using HyperLib.Core;
using Hypershell.Controls;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

/// <summary>Independent acceptance checks against private copies; never connects to a live HyperLib host.</summary>
public static class HyperlibStudioWorkspaceVerifier
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".hyperlib", "bin", "obj", "node_modules", "app", "output", "publish", ".vs" };

    public static async Task VerifyAsync(string outputRoot, string sourceLibraryRoot)
    {
        var source = Path.GetFullPath(sourceLibraryRoot);
        var output = Path.GetFullPath(outputRoot);
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Verification output must be outside the source library.");
        var fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var report = new Receipt { SourceLibrary = source, Fixture = fixture };
        var sourceBefore = FingerprintAuthoring(source);
        try
        {
            var copiedLibrary = Path.Combine(fixture, "library");
            CopyAuthoring(source, copiedLibrary);
            var initial = ReadState(copiedLibrary);
            Check(report, initial.Nodes.Count(node => node.Type == "work") == 17,
                "Copied live fixture contains the original two Works and fifteen starter Works.");
            Check(report, initial.Pages.Count == 18, "Copied live fixture preserves all eighteen current pages.");
            Check(report, FingerprintAuthoring(copiedLibrary) == sourceBefore,
                "The private authoring fixture matches source files before verification.");
            await VerifyWorkspaceAsync(copiedLibrary, fixture, initial, report);
            Check(report, FingerprintAuthoring(source) == sourceBefore,
                "The source library's authoring files are unchanged by verification.");
            report.Passed = true;
        }
        catch (Exception exception)
        {
            report.Failure = exception.ToString();
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "hyperlib-studio-workspace.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"HYPERLIB_STUDIO_WORKSPACE_OK|assertions:{report.Assertions.Count}|renders:{report.Renders.Count}|private-copy|{fixture}");
    }

    private static async Task VerifyWorkspaceAsync(string library, string fixture, LibraryState initial, Receipt report)
    {
        await VerifyDiscoveryAsync(library, fixture, initial, report);
        await VerifyTemplateSessionsAsync(library, fixture, initial, report);
        await VerifyGatewayAsync(library, fixture, report);
        await RunStaAsync(() => VerifyViewModelsAsync(library, fixture, report));
    }

    private static async Task VerifyViewModelsAsync(string library, string fixture, Receipt report)
    {
        var modelRoot = Path.Combine(fixture, "model-library");
        CopyAuthoring(library, modelRoot);
        var initial = ReadState(modelRoot);
        var store = new ContentStore(modelRoot);
        using var handler = new GatewayHandler(store, modelRoot);
        using var client = new HttpClient(handler);
        using var service = new HyperlibStudioWorkspaceService(client);
        var endpointCalls = 0;
        Task<Uri> Connect(string _, CancellationToken token)
        { endpointCalls++; return Task.FromResult(new Uri("http://127.0.0.1:54321/")); }
        using var model = new HyperlibStudioWorkspaceViewModel(modelRoot, Connect, service);
        var before = FingerprintAuthoring(modelRoot);
        await model.LoadAsync();
        Check(report, !model.HasError && model.IsLibraryRoot && model.Works.Count == 17
            && model.AvailableTemplates.Count(item => item.IsBuiltIn) == 18
            && model.AvailableTemplates.Any(item => item.Id == ReusableTemplateCatalog.BlankId),
            "The workspace viewmodel loads all Works and eighteen reusable foundations, including blank, without a gateway connection.");
        Check(report, endpointCalls == 0 && handler.Requests.Count == 0 && FingerprintAuthoring(modelRoot) == before
            && !Directory.Exists(Path.Combine(modelRoot, ".hyperlib")), "Viewmodel loading creates no host, journal, cache, or filesystem mutation.");
        var first = model.Works.First();
        model.SelectedWork = first;
        model.SelectedPage = model.Pages.First();
        var pageId = model.SelectedPage.Id;
        model.DraftTitle = "Keep this unsaved title";
        await model.RefreshAsync();
        Check(report, model.SelectedWork?.Id == first.Id && model.SelectedPage?.Id == pageId && model.DraftTitle == "Keep this unsaved title",
            "Read-only refresh preserves selected Work/page and the unsaved title draft.");
        var otherPage = initial.Pages.Values.First(page => page.WorkId != first.Id);
        model.SearchText = otherPage.Title;
        Check(report, model.Works.Any(work => work.Id == otherPage.WorkId)
            && model.SelectedWork?.Id == otherPage.WorkId && model.Pages.Any(page => page.Id == otherPage.Id),
            "Searching a different Work's page title exposes its owning Work and the matching content page.");
        model.SearchText = "";
        model.SelectedWork = model.Works.First(work => work.Id == first.Id);
        model.SelectedPage = model.Pages.First(page => page.Id == pageId);
        string? openedProject = null;
        string? openedContent = null;
        string? openedSource = null;
        model.OpenWorkProjectRequested += value => openedProject = value;
        model.OpenContentRequested += value => openedContent = value;
        model.OpenRelativeCodeRequested += value => openedSource = value;
        model.OpenWorkCommand.Execute(first with { ProjectRoot = Path.Combine(fixture, "not-the-work") });
        model.OpenContentCommand.Execute(model.SelectedPage);
        var source = model.SourceGroups.SelectMany(group => group.Items).First();
        model.OpenSourceCommand.Execute(source);
        Check(report, openedProject == Path.Combine(modelRoot, "Works", first.Id) && openedContent == pageId
            && openedSource == source.RelativePath, "Navigation events carry trusted canonical Work paths, exact page IDs, and known source paths.");
        openedSource = null;
        model.OpenSourceCommand.Execute(new HyperlibStudioSource("Unknown", "../escape.cs"));
        Check(report, openedSource is null, "Unknown source paths cannot be dispatched to the code editor.");

        LoadApplicationResources();
        using (var view = new HyperlibStudioWorkspaceView(model))
        {
            Check(report, ReferenceEquals(view.DataContext, model), "The actual workspace control receives the public native viewmodel.");
            VerifyHubRender(view, model, "library", fixture, report);
            await VerifyAppearanceDraftUiAsync(view, model, fixture, report);
        }
        Check(report, endpointCalls == 0 && handler.Requests.Count == 0,
            "Constructing, measuring, switching tabs, and rendering the hub never starts preview or a gateway.");

        using var workModel = new HyperlibStudioWorkspaceViewModel(Path.Combine(modelRoot, "Works", first.Id), Connect, service);
        await workModel.LoadAsync();
        Check(report, !workModel.HasError && !workModel.IsLibraryRoot && workModel.Works.Count == 1
            && workModel.Pages.All(page => page.WorkId == first.Id), "A Work viewmodel is scoped to its own content and source.");
        using (var view = new HyperlibStudioWorkspaceView(workModel)) VerifyHubRender(view, workModel, "work", fixture, report);
        using var standalone = new HyperlibStudioWorkspaceViewModel(Path.Combine(fixture, "standalone-work"), Connect, service);
        await standalone.LoadAsync();
        Check(report, !standalone.HasError && !standalone.HasLibraryContent && !standalone.CanMutate
            && standalone.EditTemplateCommand.CanExecute(null) && !standalone.CreatePageCommand.CanExecute(null)
            && !standalone.PreviewCommand.CanExecute(null), "Standalone Works keep template editing while unavailable library mutations and preview are disabled.");
        using (var view = new HyperlibStudioWorkspaceView(standalone)) VerifyHubRender(view, standalone, "standalone", fixture, report);
        await VerifyTemplateDialogAsync(Path.Combine(modelRoot, "Works", first.Id), fixture, report);

        await store.InitializeAsync();
        await VerifyWorkAppearanceAsync(model, store, handler, modelRoot, first.Id, report);
        await VerifyWorkOrganizationAsync(model, store, handler, modelRoot, first.Id, fixture, report);
        var revision = model.Revision;
        Check(report, await model.CreateWorkAsync(model.WorkParents.First().Id, "Viewmodel atomic Work", "portfolio-grid"),
            "The native New Work action successfully creates an independent Work and first page.");
        Check(report, model.Revision == revision + 1 && handler.Batches.Last().Operations.Count == 2
            && handler.Batches.Last().Operations[0].StarterId == "portfolio-grid" && model.SelectedPage is not null
            && model.SelectedPage.WorkId == model.SelectedWork?.Id, "New Work selection and its starter-filled page update only after the atomic response.");
        var selectedWorkId = model.SelectedWork!.Id;
        var selectedPageId = model.SelectedPage!.Id;
        model.DraftTitle = "Preserved after conflict";
        var external = await store.ApplyAsync(new OperationBatch
        { ExpectedRevision = model.Revision, Operations = [new() { Kind = "updateNode", Id = selectedWorkId, Title = "External rename" }] });
        var editRevision = model.Revision;
        handler.RuntimePackage = selectedWorkId;
        var freshPreview = await model.PreviewAsync(selectedPageId);
        Check(report, freshPreview?.ContentRevision == external.Revision && model.Revision == editRevision
            && freshPreview.Url.Query.Contains("revision=" + external.Revision, StringComparison.Ordinal),
            "Preview fetches the latest content revision while preserving the older edit revision used for conflict protection.");
        var posts = handler.MutationRequests;
        Check(report, !await model.RenameAsync(selectedWorkId, "Stale rename") && model.HasError
            && model.ErrorMessage!.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
            && model.StatusMessage.Contains("Refresh", StringComparison.OrdinalIgnoreCase),
            "A stale viewmodel mutation produces a visible refresh-and-review conflict message.");
        Check(report, handler.MutationRequests == posts + 1 && model.DraftTitle == "Preserved after conflict"
            && model.SelectedWork?.Id == selectedWorkId && model.SelectedPage?.Id == selectedPageId
            && JsonEqual(external, await store.ReadAsync()), "Conflict handling preserves the draft and selections, never retries, and retains the other editor's content.");
        await model.RefreshAsync();
        Check(report, !model.HasError && model.Revision == external.Revision && model.DraftTitle == "Preserved after conflict"
            && model.SelectedWork?.Id == selectedWorkId && model.SelectedPage?.Id == selectedPageId,
            "Explicit refresh resolves conflict state while retaining the user's draft and navigation.");
    }

    private static void VerifyHubRender(HyperlibStudioWorkspaceView view, HyperlibStudioWorkspaceViewModel model,
        string scenario, string fixture, Receipt report)
    {
        var tabs = Named<TabControl>(view, "WorkspaceTabs");
        var expectedHeaders = model.IsLibraryRoot
            ? new[] { "Template", "Work", "Page" }
            : model.HasLibraryContent ? new[] { "Work", "Page" } : new[] { "Template" };
        Check(report, tabs.Items.OfType<TabItem>().Where(tab => tab.Visibility == Visibility.Visible)
            .Select(tab => tab.Header?.ToString()).SequenceEqual(expectedHeaders),
            scenario + ": primary navigation exposes only the applicable Template, Work, and Page objects.");
        Check(report, Named<TabItem>(view, "CodeTab").Visibility == Visibility.Collapsed
            && Named<TabItem>(view, "PreviewTab").Visibility == Visibility.Collapsed,
            scenario + ": source and preview remain available through deliberate actions without adding primary tabs.");
        var defaultTab = model.IsLibraryRoot ? "TemplatesTab" : model.HasLibraryContent ? "WorkTab" : "TemplateTab";
        Check(report, ReferenceEquals(tabs.SelectedItem, Named<TabItem>(view, defaultTab)),
            scenario + ": the initial view opens element libraries for a library, Work Contents for a Work, and Elements for an independent Template.");
        Check(report, Named<TextBlock>(view, "WorkspaceTitle").Text == (model.IsLibraryRoot ? "Hyperlib Editor" : model.ScopeTitle),
            scenario + ": the visible heading identifies the currently edited library or Work.");
        var sections = new List<string>();
        view.TemplateSectionRequested += sections.Add;
        foreach (var name in new[] { "ElementsButton", "StylesButton" })
            Named<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(report, sections.SequenceEqual(["elements", "styles"]),
            scenario + ": Template elements and advanced appearance contracts retain their distinct editing sections.");
        if (model.HasLibraryContent)
        {
            var workSections = Named<TabControl>(view, "WorkSectionTabs");
            Check(report, workSections.Items.OfType<TabItem>().Select(tab => tab.Header?.ToString()).SequenceEqual(["Contents", "Structure", "Appearance"])
                && ReferenceEquals(workSections.SelectedItem, Named<TabItem>(view, "WorkContentsTab")),
                scenario + ": Work design separates its hierarchy, Page structure, and shared appearance and opens with Contents.");
            Check(report, new[] { "WorkLayoutButton", "WorkRegionsButton", "WorkElementsButton" }
                .Select(name => Named<Button>(view, name).Tag?.ToString()).SequenceEqual(["defaults", "regions", "elements"]),
                scenario + ": Page layout, element locations, and special elements identify the owning Work's matching design sections.");
            var writerTargets = new List<string>();
            Action<string> writerRequested = writerTargets.Add;
            model.OpenContentRequested += writerRequested;
            try
            {
                view.RequestAction("new-page");
                Named<Button>(view, "NewPageButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(report, writerTargets.SequenceEqual([model.SelectedWork!.Id, model.SelectedWork.Id])
                    && Named<Button>(view, "NewPageButton").Content?.ToString() == "Write in Studio",
                    scenario + ": creating Page content hands off to the selected Work in Hyperlib instead of opening a native Page creation form.");
            }
            finally { model.OpenContentRequested -= writerRequested; }
        }
        foreach (var width in new[] { 760d, 1280d })
        foreach (var tabName in model.IsLibraryRoot
            ? new[] { "TemplatesTab", "WorkTab", "PagesTab", "CodeTab" }
            : model.HasLibraryContent ? new[] { "WorkTab", "PagesTab", "CodeTab" } : new[] { "TemplateTab", "CodeTab" })
        {
            var tab = Named<TabItem>(view, tabName);
            tabs.SelectedItem = tab;
            foreach (var scale in new[] { 1d, 1.5d })
            {
                var path = Path.Combine(fixture, $"hub-{scenario}-{tabName}-{width:0}-{scale * 100:0}.png");
                Render(view, path, width, scale);
                report.Renders.Add(path);
            }
            foreach (var item in tabs.Items.OfType<TabItem>().Where(item => item.Visibility == Visibility.Visible))
            {
                var bounds = item.TransformToAncestor(view).TransformBounds(new Rect(item.RenderSize));
                Check(report, item.ActualWidth > 20 && bounds.Left >= -1 && bounds.Right <= view.ActualWidth + 1
                    && bounds.Top >= -1 && bounds.Bottom <= view.ActualHeight + 1,
                    $"{scenario} {width:0} {tabName}: {item.Header} tab remains visible and within the viewport.");
            }
            Check(report, Named<Border>(view, "PreviewHost").Child is TextBlock,
                scenario + ": inactive preview remains a lightweight placeholder rather than a running WebView.");
            if (tabName == "WorkTab")
                Check(report, Named<ListBox>(view, "WorkContentsList").ActualHeight >= 150,
                    $"{scenario} {width:0}: the Work hierarchy retains at least 150px for visible content rows.");
            Check(report, Named<Border>(view, "Navigator").Visibility ==
                (model.IsLibraryRoot && (tabName is "PagesTab" or "WorkTab") ? Visibility.Visible : Visibility.Collapsed),
                scenario + ": Work and Page browsing share the selected Work while Template and source views keep their own scope.");
        }
        tabs.SelectedItem = Named<TabItem>(view, "PagesTab");
        var navigator = Named<Border>(view, "Navigator");
        Check(report, navigator.Visibility == (model.IsLibraryRoot ? Visibility.Visible : Visibility.Collapsed),
            scenario + ": the content Work navigator appears only at library scope.");
        tabs.SelectedItem = Named<TabItem>(view, "PreviewTab");
        Check(report, navigator.Visibility == (model.IsLibraryRoot ? Visibility.Visible : Visibility.Collapsed)
            && Named<Border>(view, "PreviewHost").Child is TextBlock,
            scenario + ": preview navigation retains Work selection without starting a renderer until requested.");
        if (!model.HasLibraryContent)
            Check(report, Named<Button>(view, "NewWorkButton").Visibility == Visibility.Collapsed
                && Named<Button>(view, "NewPageButton").Visibility == Visibility.Collapsed,
                "Standalone source projects do not present disconnected content creation actions.");
        view.RequestAction(model.IsLibraryRoot ? "library" : model.HasLibraryContent ? "work" : "template");
        Check(report, ReferenceEquals(tabs.SelectedItem, Named<TabItem>(view, defaultTab)),
            scenario + ": object navigation returns to its natural workspace through the public Studio action route.");
    }

    private static async Task VerifyWorkAppearanceAsync(HyperlibStudioWorkspaceViewModel model, ContentStore store,
        GatewayHandler handler, string library, string workId, Receipt report)
    {
        var before = await store.ReadAsync();
        var sourceRoot = Path.Combine(library, before.Nodes.Single(node => node.Id == workId).ProjectPath!);
        var sourceBefore = FingerprintAuthoring(sourceRoot, includeContent: false);
        var design = await model.GetWorkDesignAsync(workId)
            ?? throw new InvalidOperationException("The owning Work appearance contract could not be loaded.");
        Check(report, design.ContentRevision == model.Revision && design.WorkId == workId
            && design.Definition.Appearance.Any(field => field.Id == "fontSize" && field.Type == "number"),
            "A Work appearance draft captures the current revision and includes bounded text size for older Templates.");
        var appearance = design.Appearance.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        appearance["background"] = ContentJson.Value("#234567");
        appearance["font"] = ContentJson.Value(design.Definition.Appearance.Single(field => field.Id == "font").Options.First());
        appearance["fontSize"] = ContentJson.Value(22);
        Check(report, await model.SaveWorkAppearanceAsync(design, appearance),
            "Saving a Work's base color, font, and text size succeeds through the real native gateway: " + model.ErrorMessage);
        var saved = await store.ReadAsync();
        Check(report, saved.Revision == before.Revision + 1 && handler.Batches.Last() is { Operations.Count: 1 } batch
            && batch.ExpectedRevision == design.ContentRevision && batch.Operations[0].Kind == "setAppearance"
            && batch.Operations[0].Id == workId
            && JsonEqual(saved.Nodes.Single(node => node.Id == workId).Appearance, appearance),
            "One revision-protected operation saves the complete Work appearance without dropping other values.");
        Check(report, before.Pages.All(pair => JsonEqual(pair.Value, saved.Pages[pair.Key]))
            && before.Nodes.Where(node => node.Id != workId).All(node => JsonEqual(node, saved.Nodes.Single(item => item.Id == node.Id))),
            "Shared Work appearance leaves every Page arrangement and sibling Work unchanged.");
        Check(report, FingerprintAuthoring(sourceRoot, includeContent: false) == sourceBefore,
            "Saving Work appearance preserves its Template and component source while the content projections record the new shared appearance.");
        Check(report, await model.UndoAsync()
            && JsonEqual((await store.ReadAsync()).Nodes.Single(node => node.Id == workId).Appearance, before.Nodes.Single(node => node.Id == workId).Appearance),
            "A single Undo restores the Work's previous shared appearance.");
        Check(report, await model.RedoAsync()
            && JsonEqual((await store.ReadAsync()).Nodes.Single(node => node.Id == workId).Appearance, appearance),
            "A single Redo restores the Work's saved colors and typography.");
        var stale = await model.GetWorkDesignAsync(workId) ?? throw new InvalidOperationException("Missing refreshed appearance draft.");
        var invalid = appearance.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        invalid["fontSize"] = ContentJson.Value(StandardAppearance.MaximumFontSize + 1);
        var posts = handler.MutationRequests;
        Check(report, !await model.SaveWorkAppearanceAsync(stale, invalid) && handler.MutationRequests == posts,
            "Text size outside the supported bounds is rejected before a gateway write.");
        var external = await store.ApplyAsync(new OperationBatch { ExpectedRevision = stale.ContentRevision,
            Operations = [new() { Kind = "updateNode", Id = workId, Title = "Appearance conflict external edit" }] });
        appearance["background"] = ContentJson.Value("#765432");
        posts = handler.MutationRequests;
        Check(report, !await model.SaveWorkAppearanceAsync(stale, appearance) && handler.MutationRequests == posts + 1
            && model.Revision == stale.ContentRevision && JsonEqual(external, await store.ReadAsync())
            && appearance["background"].GetString() == "#765432",
            "A stale Work appearance save makes one attempt, preserves the unsaved form values, and retains the other editor's content.");
        await model.RefreshAsync();
        Check(report, !model.HasError && model.Revision == external.Revision,
            "Explicit refresh resolves the Work appearance conflict before another change can be made.");
    }

    private static async Task VerifyWorkOrganizationAsync(HyperlibStudioWorkspaceViewModel model, ContentStore store,
        GatewayHandler handler, string library, string workId, string fixture, Receipt report)
    {
        model.SearchText = "";
        model.SelectedWork = model.Works.Single(work => work.Id == workId);
        var original = await store.ReadAsync();
        var sourceRoot = Path.Combine(library, original.Nodes.Single(node => node.Id == workId).ProjectPath!);
        var sourceBefore = FingerprintAuthoring(sourceRoot, includeContent: false);
        Check(report, model.WorkContents.All(item => item.Node.WorkId == workId && item.Type is "volume" or "part" or "chapter" or "page")
            && model.WorkContents.Count(item => item.IsPage) == original.Pages.Values.Count(page => page.WorkId == workId),
            "Work Contents exposes the selected Work's existing containers and Pages without sibling content.");
        Check(report, model.GetContainerParents("volume").Select(parent => parent.Id).SequenceEqual(new[] { workId })
            && model.GetContainerParents("unsupported").Count == 0,
            "Volumes begin inside the selected Work and unsupported container types offer no parent choices.");
        var parentId = workId;
        string? chapterId = null;
        foreach (var (type, depth) in new[] { ("volume", 0), ("part", 1), ("chapter", 2) })
        {
            var before = await store.ReadAsync();
            var posts = handler.MutationRequests;
            Check(report, await model.CreateContainerAsync(parentId, type, "Verifier " + type),
                "The native Work organization action creates a " + type + " through the gateway: " + model.ErrorMessage);
            var item = model.SelectedWorkContent ?? throw new InvalidOperationException("The new container was not selected.");
            var created = await store.ReadAsync();
            Check(report, created.Revision == before.Revision + 1 && handler.MutationRequests == posts + 1
                && handler.Batches.Last() is { Operations.Count: 1 } batch && batch.ExpectedRevision == before.Revision
                && batch.Operations[0].Kind == "createNode" && batch.Operations[0].NodeType == type
                && batch.Operations[0].ParentId == parentId && item.Depth == depth && item.Node.ParentId == parentId,
                "One revision-protected operation creates the " + type + " at its actual nested position.");
            Check(report, JsonEqual(before.Pages, created.Pages)
                && FingerprintAuthoring(sourceRoot, includeContent: false) == sourceBefore
                && before.Nodes.Where(node => node.WorkId != workId).All(node => JsonEqual(node, created.Nodes.Single(current => current.Id == node.Id))),
                "Creating a " + type + " preserves all Page content, reusable source, and other Works.");
            var createdId = item.Id;
            Check(report, await model.UndoAsync() && !model.WorkContents.Any(content => content.Id == createdId)
                && (await store.ReadAsync()).Nodes.All(node => node.Id != createdId),
                "A single Undo removes the new " + type + " from the Work hierarchy.");
            Check(report, await model.RedoAsync() && model.WorkContents.Any(content => content.Id == createdId && content.Depth == depth),
                "A single Redo restores the new " + type + " and its nested position.");
            model.SelectedWorkContent = model.WorkContents.Single(content => content.Id == createdId);
            await model.RefreshAsync();
            Check(report, model.SelectedWorkContent?.Id == createdId,
                "Read-only refresh keeps the selected " + type + " in its owning Work.");
            parentId = createdId;
            if (type == "chapter") chapterId = createdId;
        }
        Check(report, model.GetContainerParents("part").All(parent => parent.Type is "work" or "volume")
            && model.GetContainerParents("chapter").All(parent => parent.Type is "work" or "volume" or "part")
            && model.GetContainerParents("chapter").Any(parent => parent.Type == "part")
            && model.PageParents.Any(parent => parent.Id == chapterId),
            "Container and Page parent choices follow Collection > Series > Work > Volume > Part > Chapter > Page while keeping optional levels optional.");
        var current = await store.ReadAsync();
        var requests = handler.Requests.Count;
        var pageId = current.Pages.Values.First(page => page.WorkId == workId).Id;
        var siblingWorkId = current.Nodes.First(node => node.Type == "work" && node.Id != workId).Id;
        Check(report, !await model.CreateContainerAsync(chapterId!, "part", "Invalid rank")
            && !await model.CreateContainerAsync(pageId, "chapter", "Invalid page parent")
            && !await model.CreateContainerAsync(siblingWorkId, "chapter", "Outside selected Work")
            && !await model.CreateContainerAsync(workId, "page", "Use the writer")
            && !await model.CreateContainerAsync(workId, "part", "\nInvalid title")
            && handler.Requests.Count == requests && JsonEqual(current, await store.ReadAsync()),
            "Invalid hierarchy ranks, page parents, sibling Works, unsupported types, and invalid titles are rejected before any connection or write.");
        model.SearchText = "Verifier chapter";
        Check(report, model.Works.Count == 1 && model.SelectedWork?.Id == workId
            && model.WorkContents.Any(item => item.Id == chapterId),
            "Finding a Chapter title keeps its owning Work and its full hierarchy visible.");
        model.SearchText = "";
        model.ClearError();
        using var scopedClient = new HttpClient(handler, disposeHandler: false);
        using var scopedService = new HyperlibStudioWorkspaceService(scopedClient);
        using var scoped = new HyperlibStudioWorkspaceViewModel(sourceRoot, model.EndpointFactory, scopedService);
        await scoped.LoadAsync();
        Check(report, scoped.WorkContents.All(item => item.Node.WorkId == workId)
            && scoped.GetContainerParents("chapter").All(parent => parent.WorkId == workId)
            && scoped.WorkContents.Any(item => item.Id == chapterId && item.Depth == 2),
            "Opening a Work directly retains its scoped nested contents and valid parent choices.");
        Check(report, !await scoped.CreateContainerAsync(siblingWorkId, "part", "Scoped sibling")
            && handler.Requests.Count == requests && JsonEqual(current, await store.ReadAsync()),
            "A Work-scoped workspace cannot create a container inside a sibling Work.");
        const string nestedPageId = "verifier-writer-nested-page";
        await store.ApplyAsync(new OperationBatch { ExpectedRevision = current.Revision,
            Operations = [new() { Kind = "createNode", Id = nestedPageId, ParentId = chapterId, NodeType = "page", Title = "Writer-created Page" }] });
        await model.RefreshAsync();
        var nestedPage = model.WorkContents.Single(item => item.Id == nestedPageId);
        model.SelectedWorkContent = nestedPage;
        Check(report, nestedPage.IsPage && nestedPage.Depth == 3 && model.SelectedPage?.Id == nestedPageId
            && model.WorkContents.IndexOf(nestedPage) > model.WorkContents.IndexOf(model.WorkContents.Single(item => item.Id == chapterId)),
            "A Page created by the writer appears beneath its Chapter and selecting it keeps exact Page identity.");
        using (var hierarchy = new HyperlibStudioWorkspaceView(model))
        {
            hierarchy.RequestAction("work");
            foreach (var width in new[] { 760d, 1280d })
            {
                var path = Path.Combine(fixture, $"work-nested-contents-{width:0}.png");
                Render(hierarchy, path, width, 1);
                var list = Named<ListBox>(hierarchy, "WorkContentsList");
                list.ScrollIntoView(nestedPage);
                // This disconnected render has no PresentationSource for BringIntoView;
                // exercise its real scrolling viewport directly before checking the final row.
                Descendants<ScrollViewer>(list).First().ScrollToEnd();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                Render(hierarchy, path, width, 1);
                report.Renders.Add(path);
                var row = list.ItemContainerGenerator.ContainerFromItem(nestedPage) as ListBoxItem;
                var bounds = row?.TransformToAncestor(list).TransformBounds(new Rect(row.RenderSize));
                Check(report, list.ActualHeight >= 150 && row is { ActualHeight: > 20 }
                    && bounds is { } visible && visible.Top >= -1 && visible.Bottom <= list.ActualHeight + 1,
                    $"At {width:0}px, the selected writer-created Page is visibly rendered inside its Work hierarchy. "
                    + $"Viewport={list.ActualHeight:0.##}; row={row?.ActualHeight:0.##}; bounds={bounds}.");
            }
        }
        var selectedId = model.SelectedWorkContent?.Id;
        model.DraftTitle = "Keep this container draft";
        var editRevision = model.Revision;
        var external = await store.ApplyAsync(new OperationBatch { ExpectedRevision = editRevision,
            Operations = [new() { Kind = "updateNode", Id = chapterId, Title = "Writer-renamed Chapter" }] });
        var mutationPosts = handler.MutationRequests;
        Check(report, !await model.CreateContainerAsync(workId, "part", model.DraftTitle)
            && handler.MutationRequests == mutationPosts + 1 && model.Revision == editRevision
            && model.SelectedWorkContent?.Id == selectedId && model.DraftTitle == "Keep this container draft"
            && JsonEqual(external, await store.ReadAsync()),
            "A stale container creation makes one guarded attempt, preserves the draft and selection, and leaves the writer's revision untouched.");
        await model.RefreshAsync();
        Check(report, !model.HasError && model.Revision == external.Revision
            && model.SelectedWorkContent?.Id == selectedId && model.DraftTitle == "Keep this container draft",
            "Explicit refresh resolves a container conflict while preserving the unfinished draft and selected Page.");
    }

    private static async Task VerifyAppearanceDraftUiAsync(HyperlibStudioWorkspaceView view,
        HyperlibStudioWorkspaceViewModel model, string fixture, Receipt report)
    {
        view.RequestAction("work");
        Named<TabControl>(view, "WorkSectionTabs").SelectedItem = Named<TabItem>(view, "WorkAppearanceTab");
        await WaitForAppearanceAsync(view);
        var first = model.SelectedWork!;
        var other = model.Works.First(work => work.Id != first.Id);
        TextBox TextSize(HyperlibStudioWorkspaceView surface) => Descendants<TextBox>(Named<StackPanel>(surface, "WorkAppearancePanel"))
            .Single(text => System.Windows.Automation.AutomationProperties.GetName(text) == "Text size (px)");
        TextSize(view).Text = "unfinished size";
        Check(report, model.HasUnsavedAppearanceChanges && !Named<Button>(view, "SaveWorkAppearanceButton").IsEnabled
            && Named<Button>(view, "ReloadWorkAppearanceButton").Visibility == Visibility.Visible,
            "An unfinished text-size edit stays visible, marks the Work draft unsaved, and prevents an invalid save.");
        model.SelectedWork = other;
        await WaitForAppearanceAsync(view);
        model.SelectedWork = model.Works.Single(work => work.Id == first.Id);
        await WaitForAppearanceAsync(view);
        await model.RefreshAsync();
        Check(report, TextSize(view).Text == "unfinished size" && model.HasUnsavedAppearanceChanges,
            "Switching Works and explicitly refreshing preserve the owning Work's raw unfinished appearance draft.");
        view.Dispose();
        using var recreated = new HyperlibStudioWorkspaceView(model);
        recreated.RequestAction("work");
        Named<TabControl>(recreated, "WorkSectionTabs").SelectedItem = Named<TabItem>(recreated, "WorkAppearanceTab");
        await WaitForAppearanceAsync(recreated);
        var path = Path.Combine(fixture, "work-appearance-recreated-draft.png");
        Render(recreated, path, 1280, 1);
        report.Renders.Add(path);
        Check(report, TextSize(recreated).Text == "unfinished size" && model.HasUnsavedAppearanceChanges
            && !Named<Button>(recreated, "SaveWorkAppearanceButton").IsEnabled,
            "Rebuilding the native workspace preserves the unsaved appearance draft and its validation error.");
        model.DiscardAppearanceDrafts();
        Check(report, !model.HasUnsavedAppearanceChanges,
            "An explicit draft discard clears the Work appearance close guard without saving content.");
    }

    private static async Task WaitForAppearanceAsync(HyperlibStudioWorkspaceView view)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (Named<StackPanel>(view, "WorkAppearancePanel").Children.Count > 0) return;
            await Task.Delay(10);
        }
        throw new InvalidOperationException("The native Work appearance form did not finish loading.");
    }

    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement
        => root.FindName(name) as T ?? throw new InvalidOperationException("Missing actual workspace control: " + name);

    private static async Task VerifyTemplateDialogAsync(string workRoot, string fixture, Receipt report)
    {
        var before = FingerprintAuthoring(workRoot);
        var loaded = await HyperlibStudioAuthoringService.LoadAsync(workRoot, "authoring.template.json");
        foreach (var reusable in new[] { false, true })
        {
            var dialog = new Hypershell.HyperlibTemplateEditorDialog(workRoot, loaded.Definition, loaded.Snapshot, reusableTemplate: reusable);
            try
            {
                var content = (FrameworkElement)dialog.Content;
                dialog.Content = null;
                var surface = new Border { Background = dialog.Background, Child = content };
                System.Windows.Documents.TextElement.SetForeground(surface, dialog.Foreground);
                var draft = (HyperlibTemplateEditSession)typeof(Hypershell.HyperlibTemplateEditorDialog)
                    .GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
                var tabs = (TabControl)typeof(Hypershell.HyperlibTemplateEditorDialog)
                    .GetField("_tabs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
                Check(report, tabs.Items.OfType<TabItem>().Select(tab => tab.Header?.ToString()).SequenceEqual(reusable
                    ? new[] { "Elements", "Advanced" }
                    : new[] { "Page layout", "Element locations", "Special elements", "Advanced" }),
                    reusable ? "The Template editor leads with reusable Elements and keeps structural controls in Advanced."
                        : "The Work editor exposes Page layout, Element locations, and Special elements as its primary design tools.");
                Check(report, (tabs.SelectedItem as TabItem)?.Tag?.ToString() == (reusable ? "elements" : "defaults"),
                    "The " + (reusable ? "Template" : "Work") + " editor opens its natural authoring surface by default.");
                foreach (var size in new[] { new Size(850, 650), new Size(1100, 800) })
                foreach (var section in new[] { "elements", "defaults", "styles", "regions" })
                {
                    dialog.SelectSection(section);
                    foreach (var scale in new[] { 1d, 1.5d })
                    {
                        var path = Path.Combine(fixture, $"{(reusable ? "template" : "work-elements")}-{section}-{size.Width:0}-{scale * 100:0}.png");
                        Render(surface, path, size.Width, scale, size.Height);
                        report.Renders.Add(path);
                    }
                    Check(report, (tabs.SelectedItem as TabItem)?.Tag?.ToString() == section
                        || (tabs.SelectedItem as TabItem)?.Content is TabControl nested
                            && (nested.SelectedItem as TabItem)?.Tag?.ToString() == section,
                        $"Template dialog {size.Width:0}: the {section} shortcut selects the corresponding editor.");
                    var save = Descendants<Button>(surface).Single(button => button.Content?.ToString() == (reusable ? "_Save Template" : "_Save Work structure"));
                    var bounds = save.TransformToAncestor(surface).TransformBounds(new Rect(save.RenderSize));
                    Check(report, save.IsEnabled && save.ActualWidth > 50 && save.ActualHeight > 20
                        && bounds.Top >= 0 && bounds.Bottom <= surface.ActualHeight + 1 && bounds.Right <= surface.ActualWidth + 1,
                        $"Template dialog {size.Width:0} {section}: a valid untouched template can be saved and its primary action remains visible.");
                    if (section == "elements")
                    {
                        var advanced = Descendants<Expander>(surface).Single(expander => expander.Header?.ToString()
                            == (reusable ? "Component source" : "Advanced element definition"));
                        Check(report, !advanced.IsExpanded,
                            $"Element editor {size.Width:0}: component source remains secondary to the object's authoring tools.");
                        // Defining fields is the Template's main authoring task; Work-specific contracts remain advanced.
                        advanced.IsExpanded = !reusable;
                        surface.UpdateLayout();
                        var fields = Descendants<ListBox>(surface).Single(list =>
                            System.Windows.Automation.AutomationProperties.GetName(list) == "Element fields");
                        var row = fields.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                        var viewport = Descendants<ScrollContentPresenter>(fields).FirstOrDefault();
                        var rowBounds = row?.TransformToAncestor(surface).TransformBounds(new Rect(row.RenderSize)) ?? Rect.Empty;
                        var viewportBounds = viewport?.TransformToAncestor(surface).TransformBounds(new Rect(viewport.RenderSize)) ?? Rect.Empty;
                        Check(report, row is { ActualHeight: > 10 } && viewport is not null
                            && rowBounds.Top >= viewportBounds.Top - 1 && rowBounds.Bottom <= viewportBounds.Bottom + 1
                            && rowBounds.Left >= 0 && rowBounds.Right <= surface.ActualWidth + 1
                            && rowBounds.Top >= 0 && rowBounds.Bottom <= surface.ActualHeight + 1,
                            $"{(reusable ? "Template" : "Work")} dialog {size.Width:0}: the exposed field list shows a complete selectable row below its compact toolbar.");
                        advanced.IsExpanded = false;
                    }
                    Check(report, JsonEqual(draft.CreateDefinition(), loaded.Definition)
                        && !(bool)typeof(Hypershell.HyperlibTemplateEditorDialog).GetField("_dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!,
                        $"Template dialog {size.Width:0} {section}: control initialization and navigation do not alter defaults or mark the draft dirty.");
                }
            }
            finally
            {
                // A failed assertion must never open the dialog's discard-confirmation UI.
                typeof(Hypershell.HyperlibTemplateEditorDialog).GetField("_dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(dialog, false);
                dialog.Close();
            }
        }
        Check(report, FingerprintAuthoring(workRoot) == before, "Opening, browsing, rendering, and closing the real template dialog changes no source or content files.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static async Task VerifyDiscoveryAsync(string library, string fixture, LibraryState state, Receipt report)
    {
        using var service = new HyperlibStudioWorkspaceService();
        var before = FingerprintAuthoring(library);
        var root = await service.ReadAsync(library);
        Check(report, root.Context.IsLibraryRoot && root.Context.HasLibraryContent && root.Context.WorkId is null
            && root.Context.LibraryRoot == library && root.Context.LibraryId == state.LibraryId,
            "Opening the copied root resolves library-wide context without starting a host.");
        Check(report, root.Nodes.Count(node => node.Type == "work") == 17 && root.Nodes.Count(node => node.Type == "page") == 18
            && root.Nodes.Select(node => node.Id).Distinct().Count() == root.Nodes.Count,
            "The root navigator exposes all seventeen Works and eighteen actual content pages once.");
        Check(report, root.Nodes.Where(node => node.Type == "page").All(node => node.Breadcrumb.Contains(" / ", StringComparison.Ordinal)),
            "Content pages have hierarchy breadcrumbs rather than source-file paths.");
        var firstWork = state.Nodes.First(node => node.Type == "work");
        var workRoot = Path.Combine(library, firstWork.ProjectPath!);
        var work = await service.ReadAsync(workRoot);
        Check(report, !work.Context.IsLibraryRoot && work.Context.HasLibraryContent && work.Context.WorkId == firstWork.Id
            && work.Context.Title == firstWork.Title && work.Context.LibraryRoot == library,
            "Opening a Work resolves its identity and owning library independently of the root project.");
        Check(report, work.Nodes.All(node => node.WorkId == firstWork.Id)
            && work.Nodes.Count(node => node.Type == "page") == state.Pages.Values.Count(page => page.WorkId == firstWork.Id),
            "A Work navigator contains only its own hierarchy and content pages.");
        Check(report, root.SourceGroups.SelectMany(group => group.Items).All(item => !item.RelativePath.StartsWith("Works/", StringComparison.OrdinalIgnoreCase)
            && !item.RelativePath.StartsWith("BaseTemplates/", StringComparison.OrdinalIgnoreCase)),
            "The root's source groups do not flatten nested Work or reusable starter projects.");
        Check(report, work.SourceGroups.Any(group => group.Title == "Elements" && group.Items.Count > 0)
            && work.SourceGroups.Any(group => group.Title == "Styles" && group.Items.Count > 0)
            && work.SourceGroups.Any(group => group.Title == "Behavior" && group.Items.Count > 0),
            "Work-owned elements, styles, and behavior have distinct source groups.");
        Check(report, FingerprintAuthoring(library) == before && !Directory.Exists(Path.Combine(library, ".hyperlib")),
            "Read-only context and source discovery write no content, journal, cache, or source files.");
        foreach (var relative in new[] { ".hyperlib/cache/sentinel.cs", "obj/nested/sentinel.razor", "src/Pages/obj/sentinel.css", "bin/sentinel.js" })
        {
            var path = Path.Combine(workRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "VERIFIER_CACHE_SHOULD_NOT_APPEAR");
        }
        var cached = await service.ReadAsync(workRoot);
        Check(report, cached.SourceGroups.SelectMany(group => group.Items).All(item => !item.RelativePath.Contains("sentinel", StringComparison.Ordinal)),
            "Build and runtime cache artifacts do not appear as editable project source.");

        var standalone = Path.Combine(fixture, "standalone-work");
        CopyAuthoring(workRoot, standalone);
        var standaloneBefore = FingerprintAuthoring(standalone);
        var detached = await service.ReadAsync(standalone);
        Check(report, detached.Context.WorkId == firstWork.Id && !detached.Context.HasLibraryContent
            && detached.Context.LibraryRoot is null && !string.IsNullOrWhiteSpace(detached.Context.ContentUnavailableReason)
            && detached.SourceGroups.SelectMany(group => group.Items).Any(item => item.RelativePath == "authoring.template.json"),
            "A standalone Work retains editable template/source and explicitly reports unavailable parent-library content.");
        var endpointCalls = 0;
        await ExpectAsync<InvalidOperationException>(() => service.ConnectAsync(detached.Context, (_, _) =>
        { endpointCalls++; return Task.FromResult(new Uri("http://127.0.0.1:54321/")); }));
        Check(report, endpointCalls == 0 && FingerprintAuthoring(standalone) == standaloneBefore
            && !File.Exists(Path.Combine(standalone, "Content", "library.json")) && !Directory.Exists(Path.Combine(standalone, ".hyperlib")),
            "Opening or connecting a standalone Work never reseeds it as a new library or starts an unrelated host.");

        var invalid = Path.Combine(fixture, "invalid-library");
        Directory.CreateDirectory(Path.Combine(invalid, "Content"));
        File.Copy(Path.Combine(library, VisualPageProjectService.ProjectManifestFileName), Path.Combine(invalid, VisualPageProjectService.ProjectManifestFileName));
        var badState = Path.Combine(invalid, "Content", "library.json");
        await File.WriteAllTextAsync(badState, "{ malformed-library-json");
        await ExpectAsync<JsonException>(() => service.ReadAsync(invalid));
        Check(report, await File.ReadAllTextAsync(badState) == "{ malformed-library-json" && !Directory.Exists(Path.Combine(invalid, ".hyperlib")),
            "Malformed existing library content produces an error and is never repaired through automatic reseeding.");
    }

    private static async Task VerifyGatewayAsync(string library, string fixture, Receipt report)
    {
        var gatewayRoot = Path.Combine(fixture, "gateway-library");
        CopyAuthoring(library, gatewayRoot);
        var store = new ContentStore(gatewayRoot);
        await store.InitializeAsync();
        using var handler = new GatewayHandler(store, gatewayRoot);
        using var client = new HttpClient(handler);
        using var service = new HyperlibStudioWorkspaceService(client);
        var snapshot = await service.ReadAsync(gatewayRoot);
        var context = snapshot.Context;
        var endpoint = new Uri("http://127.0.0.1:54321/");
        string? requestedRoot = null;
        Check(report, await service.ConnectAsync(context, (root, _) => { requestedRoot = root; return Task.FromResult(endpoint); }) == endpoint
            && requestedRoot == gatewayRoot, "Gateway startup receives the resolved library root and validates the returned local endpoint.");
        foreach (var invalidEndpoint in new[] { "https://127.0.0.1:54321/", "http://localhost:54321/", "http://127.0.0.2:54321/", "http://127.0.0.1:54321/sub/", "http://user@127.0.0.1:54321/", "http://127.0.0.1:54321/?x=1" })
        {
            var before = handler.Requests.Count;
            await ExpectAsync<InvalidOperationException>(() => service.VerifyEndpointAsync(context, new Uri(invalidEndpoint)));
            Check(report, handler.Requests.Count == before, "Rejects untrusted endpoint before HTTP: " + invalidEndpoint);
        }
        var collection = snapshot.State!.Nodes.First(node => node.Type == "collection");
        OperationBatch WorkAndPage(string workId, long revision) => new()
        {
            ExpectedRevision = revision, Description = "Verifier creates one Work with its first page",
            Operations = [new() { Kind = "createNode", Id = workId, ParentId = collection.Id, NodeType = "work", Title = "Verifier Work", StarterId = "portfolio-grid" },
                new() { Kind = "createNode", Id = workId + "-page", ParentId = workId, NodeType = "page", Title = "Verifier page" }]
        };
        var batch = WorkAndPage("verifier-work", snapshot.State.Revision);
        handler.HealthRoot = library;
        await ExpectAsync<InvalidOperationException>(() => service.ApplyAsync(context, endpoint, batch));
        handler.HealthRoot = gatewayRoot;
        handler.IncludeIdentityHeader = false;
        await ExpectAsync<InvalidOperationException>(() => service.ApplyAsync(context, endpoint, batch));
        handler.IncludeIdentityHeader = true;
        handler.HealthApplication = "other-app";
        await ExpectAsync<InvalidOperationException>(() => service.ApplyAsync(context, endpoint, batch));
        handler.HealthApplication = "hyperlib";
        Check(report, handler.MutationRequests == 0, "Wrong library root, missing identity marker, and wrong application health block mutations before POST.");
        var created = await service.ApplyAsync(context, endpoint, batch);
        Check(report, created.Revision == batch.ExpectedRevision + 1 && created.Pages.ContainsKey("verifier-work-page")
            && created.Nodes.Any(node => node.Id == "verifier-work" && node.StarterId == "portfolio-grid")
            && created.Pages["verifier-work-page"].Blocks.Count > 1 && handler.Batches.Single().Operations.Count == 2,
            "Work creation and its first starter-filled page travel as one atomic gateway operation batch.");
        var undone = await service.UndoAsync(context, endpoint, created.Revision);
        Check(report, !undone.Pages.ContainsKey("verifier-work-page") && undone.Nodes.All(node => node.Id != "verifier-work"),
            "One gateway Undo removes both the new Work and its first page.");
        var redone = await service.RedoAsync(context, endpoint, undone.Revision);
        Check(report, redone.Pages.ContainsKey("verifier-work-page") && redone.Nodes.Any(node => node.Id == "verifier-work"),
            "Gateway Redo restores the complete Work-and-page batch.");

        var operationsBefore = handler.MutationRequests;
        async Task<bool> Race(string title)
        {
            try
            {
                await service.ApplyAsync(context, endpoint, new OperationBatch
                { ExpectedRevision = redone.Revision, Description = title, Operations = [new() { Kind = "updateNode", Id = "verifier-work", Title = title }] });
                return true;
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict) { return false; }
        }
        var race = await Task.WhenAll(Race("Concurrent first"), Race("Concurrent second"));
        var latest = await store.ReadAsync();
        Check(report, race.Count(success => success) == 1 && latest.Revision == redone.Revision + 1
            && handler.MutationRequests == operationsBefore + 2,
            "Concurrent writes with the same revision yield exactly one success and one conflict without automatic retry.");
        await ExpectAsync<HttpRequestException>(() => service.UndoAsync(context, endpoint, redone.Revision));
        Check(report, JsonEqual(latest, await store.ReadAsync()), "A stale Undo cannot erase a newer editor's change.");
        var badBatch = WorkAndPage("verifier-invalid", latest.Revision);
        badBatch.Operations[1].ParentId = "missing-parent";
        await ExpectAsync<HttpRequestException>(() => service.ApplyAsync(context, endpoint, badBatch));
        Check(report, JsonEqual(latest, await store.ReadAsync()), "A rejected second operation leaves no partial Work, page, or revision change.");

        var workNode = snapshot.State.Nodes.First(node => node.Type == "work");
        var work = await service.ReadAsync(Path.Combine(gatewayRoot, workNode.ProjectPath!));
        var siblingPage = latest.Pages.Values.First(page => page.WorkId != workNode.Id);
        operationsBefore = handler.MutationRequests;
        await ExpectAsync<InvalidOperationException>(() => service.ApplyAsync(work.Context, endpoint, new OperationBatch
        { ExpectedRevision = latest.Revision, Operations = [new() { Kind = "updateNode", Id = siblingPage.Id, Title = "Must stay unchanged" }] }));
        Check(report, handler.MutationRequests == operationsBefore, "A Work context rejects mutations to existing sibling content before POST.");
        await ExpectAsync<InvalidOperationException>(() => service.GetContentUrlAsync(work.Context, latest, endpoint, siblingPage.Id));
        var contentUrl = await service.GetContentUrlAsync(work.Context, latest, endpoint, workNode.Id);
        Check(report, contentUrl.GetLeftPart(UriPartial.Authority) == endpoint.GetLeftPart(UriPartial.Authority)
            && contentUrl.Fragment == "#" + workNode.Id, "Content navigation stays on the verified gateway and selected Work.");
        var otherWork = await service.ApplyAsync(work.Context, endpoint, WorkAndPage("verifier-work-from-work", latest.Revision));
        Check(report, otherWork.Pages.ContainsKey("verifier-work-from-work-page"),
            "The explicit New Work action can atomically create a separate Work and first page from a Work context.");
        var ownPage = otherWork.Pages.Values.First(page => page.WorkId == workNode.Id);
        handler.RuntimePackage = workNode.Id;
        var preview = await service.PreviewAsync(work.Context, otherWork, endpoint, ownPage.Id);
        Check(report, preview.PackageId == workNode.Id && preview.PageId == ownPage.Id
            && preview.Url.AbsolutePath == $"/_runtime/{workNode.Id}/verifier-generation/page/{ownPage.Id}"
            && preview.Url.Query == $"?edit=0&revision={otherWork.Revision}" && preview.ContentRevision == otherWork.Revision,
            "Page preview resolves the exact owning renderer generation and a read-only content route at the latest revision.");
        handler.RuntimeError = "A candidate build failed; the last working version remains visible.";
        preview = await service.PreviewAsync(work.Context, otherWork, endpoint, ownPage.Id);
        Check(report, preview.Status == "failed" && preview.Diagnostics == handler.RuntimeError
            && preview.GenerationId == "verifier-generation", "Preview preserves its healthy generation while surfacing a candidate compilation failure.");
        handler.InvalidProxyPrefix = true;
        await ExpectAsync<InvalidDataException>(() => service.PreviewAsync(work.Context, otherWork, endpoint, ownPage.Id));
        Check(report, true, "A renderer response cannot redirect preview outside its exact package/generation path.");
    }

    private static async Task VerifyTemplateSessionsAsync(string library, string fixture, LibraryState state, Receipt report)
    {
        var catalog = BaseTemplateCatalog.List();
        Check(report, catalog.Count == 17, "The editor covers all seventeen permanent starter designs.");
        foreach (var starter in catalog)
        {
            var original = BaseTemplateCatalog.CreateTemplate(starter.Id);
            var session = new HyperlibTemplateEditSession(original);
            session.Validate();
            Check(report, JsonEqual(original, session.CreateDefinition()), starter.Id + ": untouched edit-session round trip preserves every field and ordered default block.");
            var inherited = session.Draft.Blocks.SelectMany(block => block.Fields).First(field => field.Id == "font");
            Check(report, HyperlibTemplateEditSession.DefaultMode(inherited) == HyperlibDefaultMode.Inherit,
                starter.Id + ": inherited element typography remains absent rather than an invalid empty option.");
            session.Draft.Title += " verifier";
            Check(report, original.Title != session.Draft.Title && JsonEqual(original.DefaultBlocks, session.CreateDefinition().DefaultBlocks),
                starter.Id + ": changing the draft title preserves the original template and new-page arrangement.");
            var beforeOrder = session.Draft.DefaultBlocks.Select(block => block.Id).ToArray();
            session.MoveDefaultBlock(beforeOrder[0], 1);
            Check(report, session.Draft.DefaultBlocks[1].Id == beforeOrder[0]
                && session.Draft.DefaultBlocks[0].Id == beforeOrder[1], starter.Id + ": default arrangement reorder moves the intended stable block.");
            var firstType = original.DefaultBlocks[0].Type;
            Expect<ContentValidationException>(() => session.RemoveElement(firstType));
            Check(report, session.Draft.Blocks.Any(block => block.Id == firstType), starter.Id + ": an in-use default element cannot be silently removed.");
            var duplicate = session.DuplicateElement(firstType);
            var originalBlock = original.Blocks.Single(block => block.Id == firstType);
            Check(report, duplicate.Id != firstType && duplicate.Component == originalBlock.Component
                && JsonEqual(duplicate.Fields, originalBlock.Fields), starter.Id + ": element duplicate has a distinct ID and preserves its functional component and schema.");
            var region = session.Draft.Regions.First(region => region.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains(duplicate.Id));
            var added = session.AddDefaultBlock(duplicate.Id, region.Id);
            Check(report, session.Draft.DefaultBlocks.Select(block => block.Id).Distinct().Count() == session.Draft.DefaultBlocks.Count,
                starter.Id + ": a new default element receives a unique identity.");
            session.RemoveDefaultBlock(added.Id);
            session.RemoveElement(duplicate.Id);
            session.Validate();
            var emptied = new HyperlibTemplateEditSession(original);
            foreach (var id in emptied.Draft.DefaultBlocks.Select(block => block.Id).ToArray()) emptied.RemoveDefaultBlock(id);
            var emptyRoundTrip = HyperlibTemplateEditSession.ParseDefinition(JsonSerializer.Serialize(emptied.CreateDefinition(), ContentJson.Options));
            var emptyPage = TemplateValidator.CreateDefaultPage("empty-acceptance", "empty-work", "Intentionally empty", emptyRoundTrip);
            Check(report, emptyPage.Blocks.Count == 0 && emptyPage.UsesEditableChrome,
                starter.Id + ": removing every starting element and reopening keeps new Pages intentionally blank instead of restoring fallback content.");
        }

        var modes = new FieldDefinition { Id = "test", Title = "Test", Type = "string" };
        HyperlibTemplateEditSession.SetDefault(modes, HyperlibDefaultMode.Value, "");
        Check(report, HyperlibTemplateEditSession.DefaultMode(modes) == HyperlibDefaultMode.Value
            && modes.DefaultValue!.Value.GetString() == "", "An intentional empty string remains distinct from an inherited default.");
        HyperlibTemplateEditSession.SetDefault(modes, HyperlibDefaultMode.Null, "ignored");
        Check(report, HyperlibTemplateEditSession.DefaultMode(modes) == HyperlibDefaultMode.Null,
            "An explicit null remains a distinct edit-session default mode.");
        HyperlibTemplateEditSession.SetDefault(modes, HyperlibDefaultMode.Inherit, "ignored");
        Check(report, modes.DefaultValue is null, "Resetting a field default restores inheritance.");
        var commaTemplate = StarterTemplate.Create();
        commaTemplate.Appearance.Add(new FieldDefinition
        {
            Id = "comma-font", Title = "Comma font", Type = "select",
            Options = ["Georgia, serif", "system-ui, sans-serif"], DefaultValue = ContentJson.Value("Georgia, serif")
        });
        var commaSession = new HyperlibTemplateEditSession(commaTemplate);
        Check(report, commaSession.CreateDefinition().Appearance.Last().Options.SequenceEqual(["Georgia, serif", "system-ui, sans-serif"]),
            "Options containing commas remain complete values, including CSS font stacks.");
        var explicitNull = new FieldDefinition { Id = "nullable-note", Title = "Nullable note", Type = "string", DefaultValue = ContentJson.Value<object?>(null) };
        commaTemplate.Appearance.Add(explicitNull);
        var nullSession = new HyperlibTemplateEditSession(commaTemplate);
        var nullRoundTrip = nullSession.CreateDefinition().Appearance.Last();
        Check(report, HyperlibTemplateEditSession.DefaultMode(nullRoundTrip) == HyperlibDefaultMode.Null,
            "Constructing and exporting an edit session preserves explicit JSON null rather than converting it into inheritance.");
        var parsed = HyperlibTemplateEditSession.ParseDefinition(JsonSerializer.Serialize(nullSession.CreateDefinition(), ContentJson.Options));
        Check(report, HyperlibTemplateEditSession.DefaultMode(parsed.Appearance.Last()) == HyperlibDefaultMode.Null,
            "Template JSON parse/serialize preserves explicit null field defaults.");
        Expect<FormatException>(() => HyperlibTemplateEditSession.ParseValue("number", "NaN"));
        Expect<FormatException>(() => HyperlibTemplateEditSession.ParseValue("boolean", "yes"));
        Check(report, HyperlibTemplateEditSession.ParseValue("number", "1.25").GetDouble() == 1.25,
            "Structured numeric defaults parse invariantly and reject non-finite values; booleans reject ambiguous text.");

        foreach (var work in state.Nodes.Where(node => node.Type == "work"))
        {
            var workRoot = Path.Combine(library, work.ProjectPath!);
            var loaded = await HyperlibStudioAuthoringService.LoadAsync(workRoot, "authoring.template.json");
            var original = loaded.Definition;
            var session = new HyperlibTemplateEditSession(original);
            session.Draft.Title += " first save";
            await HyperlibStudioAuthoringService.SaveAsync(workRoot, loaded.Snapshot, session.CreateDefinition());
            session.Draft.Title += " second save";
            await HyperlibStudioAuthoringService.SaveAsync(workRoot, loaded.Snapshot, session.CreateDefinition());
            var reopened = await HyperlibStudioAuthoringService.LoadAsync(workRoot, "authoring.template.json");
            Check(report, JsonEqual(reopened.Definition, session.CreateDefinition())
                && JsonEqual(reopened.Definition.DefaultBlocks, original.DefaultBlocks),
                work.Id + ": two consecutive saves and reopening retain all default content and schema fields.");
            var compatibility = await HyperlibStudioAuthoringService.CheckCompatibilityAsync(workRoot, reopened.Definition);
            Check(report, compatibility.Checked && compatibility.PageCount == state.Pages.Values.Count(page => page.WorkId == work.Id),
                work.Id + ": compatibility preflight checks precisely the owning Work's current pages.");
        }
        var firstWork = state.Nodes.First(node => node.Type == "work");
        var firstRoot = Path.Combine(library, firstWork.ProjectPath!);
        var stale = await HyperlibStudioAuthoringService.LoadAsync(firstRoot, "authoring.template.json");
        var usedType = state.Pages.Values.First(page => page.WorkId == firstWork.Id).Blocks[0].Type;
        var incompatible = ContentJson.Clone(stale.Definition);
        incompatible.Blocks.RemoveAll(block => block.Id == usedType);
        incompatible.DefaultBlocks.RemoveAll(block => block.Type == usedType);
        foreach (var region in incompatible.Regions) region.AllowedBlocks.Remove(usedType);
        var sourcePath = Path.Combine(firstRoot, "authoring.template.json");
        var beforeRejectedSave = await File.ReadAllTextAsync(sourcePath);
        await ExpectAsync<ContentValidationException>(() => HyperlibStudioAuthoringService.SaveAsync(firstRoot, stale.Snapshot, incompatible));
        Check(report, await File.ReadAllTextAsync(sourcePath) == beforeRejectedSave,
            "Removing an element used by existing content is rejected before altering the source template.");
        var external = ContentJson.Clone(stale.Definition);
        external.Title = "External editor change must survive";
        var externalText = JsonSerializer.Serialize(external, ContentJson.Options);
        await File.WriteAllTextAsync(sourcePath, externalText);
        await ExpectAsync<VisualPageUserSourceDriftException>(() => HyperlibStudioAuthoringService.SaveAsync(firstRoot, stale.Snapshot, stale.Definition));
        Check(report, await File.ReadAllTextAsync(sourcePath) == externalText,
            "A stale template editor cannot overwrite a newer external source edit.");
        Check(report, JsonEqual(ReadState(library), state), "Template authoring changes no page documents or library hierarchy.");
    }

    private static bool JsonEqual<T>(T left, T right) => JsonElement.DeepEquals(ContentJson.Value(left), ContentJson.Value(right));

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private static LibraryState ReadState(string library) => JsonSerializer.Deserialize<LibraryState>(
        File.ReadAllBytes(Path.Combine(library, "Content", "library.json")), ContentJson.Options)
        ?? throw new InvalidOperationException("Fixture library content is missing.");

    private static void Check(Receipt report, bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.Assertions.Add(description);
    }

    private static IEnumerable<string> AuthoringFiles(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Fixture source must not traverse filesystem links: " + entry);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(entry)))
                    foreach (var nested in AuthoringFiles(entry)) yield return nested;
            }
            else yield return entry;
        }
    }

    private static void CopyAuthoring(string source, string destination)
    {
        if (Directory.Exists(destination)) throw new InvalidOperationException("Fixture destination already exists.");
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Fixture source cannot be a filesystem link.");
        Directory.CreateDirectory(destination);
        long copied = 0;
        var count = 0;
        foreach (var file in AuthoringFiles(source))
        {
            if (++count > 20_000 || (copied += new FileInfo(file).Length) > 512L * 1024 * 1024)
                throw new InvalidOperationException("Authoring fixture exceeds the verifier's bounded copy budget.");
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static string FingerprintAuthoring(string root, bool includeContent = true)
    {
        using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in AuthoringFiles(root))
        {
            var relative = Path.GetRelativePath(root, file);
            if (!includeContent && relative.StartsWith("Content" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            combined.AppendData(System.Text.Encoding.UTF8.GetBytes(relative + "\n"));
            using var stream = File.OpenRead(file);
            combined.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(combined.GetHashAndReset());
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
                try { await action(); completion.TrySetResult(); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "HyperLib Studio workspace verifier" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void LoadApplicationResources()
    {
        if (Application.Current is not null)
        {
            if (!Application.Current.Dispatcher.CheckAccess())
                throw new InvalidOperationException("Run this verifier in its own process with a private WPF application.");
            return;
        }
        var sourceRoot = FindSourceRoot();
        var source = XDocument.Load(Path.Combine(sourceRoot, "src", "Hypershell", "App.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary",
            source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal))
                attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static string FindSourceRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "Hypershell", "App.xaml"))) return directory.FullName;
        throw new FileNotFoundException("Run from the isolated Hypershell source checkout so App.xaml can be verified.");
    }

    private static void Render(FrameworkElement surface, string path, double width, double scale, double height = 780)
    {
        surface.Width = width;
        surface.Height = height;
        surface.Measure(new Size(width, surface.Height));
        surface.Arrange(new Rect(0, 0, width, surface.Height));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(surface.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string SourceLibrary { get; init; } = "";
        public string Fixture { get; init; } = "";
        public string? Failure { get; set; }
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
    }

    /// <summary>Exercises the real gateway client and real atomic store without opening a listening socket.</summary>
    private sealed class GatewayHandler(ContentStore store, string root) : HttpMessageHandler
    {
        private readonly object _sync = new();
        public string HealthRoot { get; set; } = root;
        public string HealthApplication { get; set; } = "hyperlib";
        public bool IncludeIdentityHeader { get; set; } = true;
        public int MutationRequests { get; private set; }
        public List<string> Requests { get; } = [];
        public List<OperationBatch> Batches { get; } = [];
        public string? RuntimePackage { get; set; }
        public string? RuntimeError { get; set; }
        public bool InvalidProxyPrefix { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (_sync) Requests.Add(request.Method + " " + path);
            if (path == "/api/health")
            {
                var response = Json(HttpStatusCode.OK, new
                {
                    app = HealthApplication, schemaVersion = 1, root = HealthRoot,
                    origin = request.RequestUri.GetLeftPart(UriPartial.Authority)
                });
                if (IncludeIdentityHeader) response.Headers.Add("X-HyperLib-App", "hyperlib");
                return response;
            }
            if (request.Method == HttpMethod.Get && path == "/api/state")
                return Json(HttpStatusCode.OK, await store.ReadAsync(cancellationToken));
            if (request.Method == HttpMethod.Get && path == "/api/base-templates")
                return Json(HttpStatusCode.OK, BaseTemplateCatalog.List());
            if (request.Method == HttpMethod.Get && path == "/api/runtime/status")
                return Json(HttpStatusCode.OK, new[] { new { packageId = RuntimePackage, state = RuntimeError is null ? "active" : "failed", error = RuntimeError } });
            if (request.Method == HttpMethod.Get && path == "/api/runtime/" + RuntimePackage)
                return Json(HttpStatusCode.OK, new
                {
                    packageId = RuntimePackage, generationId = "verifier-generation",
                    proxyPrefix = InvalidProxyPrefix ? "/untrusted/" : $"/_runtime/{RuntimePackage}/verifier-generation/"
                });
            if (request.Method == HttpMethod.Post && path is "/api/operations" or "/api/undo" or "/api/redo")
            {
                lock (_sync) MutationRequests++;
                if (!request.Headers.TryGetValues("X-HyperLib-Client", out var client) || client.SingleOrDefault() != "editor")
                    return Json(HttpStatusCode.Forbidden, new { error = "Expected the native editor request marker." });
                if (!request.Headers.TryGetValues("Origin", out var origin) || origin.SingleOrDefault() != request.RequestUri.GetLeftPart(UriPartial.Authority))
                    return Json(HttpStatusCode.Forbidden, new { error = "Expected the verified gateway origin." });
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                try
                {
                    if (path == "/api/operations")
                    {
                        var batch = JsonSerializer.Deserialize<OperationBatch>(body, ContentJson.Options)!;
                        lock (_sync) Batches.Add(ContentJson.Clone(batch));
                        return Json(HttpStatusCode.OK, await store.ApplyAsync(batch, cancellationToken));
                    }
                    using var revision = JsonDocument.Parse(body);
                    var expectedRevision = revision.RootElement.GetProperty("expectedRevision").GetInt64();
                    return Json(HttpStatusCode.OK, path == "/api/undo"
                        ? await store.UndoAsync(expectedRevision, cancellationToken)
                        : await store.RedoAsync(expectedRevision, cancellationToken));
                }
                catch (ConflictException exception) { return Json(HttpStatusCode.Conflict, new { error = exception.Message }); }
                catch (ContentValidationException exception) { return Json(HttpStatusCode.BadRequest, new { error = exception.Message }); }
            }
            return Json(HttpStatusCode.NotFound, new { error = "Unexpected fixture request: " + path });
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T value) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, ContentJson.Options), System.Text.Encoding.UTF8, "application/json")
        };
    }
}
