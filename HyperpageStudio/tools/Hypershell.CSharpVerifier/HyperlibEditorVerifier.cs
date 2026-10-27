using HyperLib.Core;
using Hypershell.Controls;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

/// <summary>Acceptance checks against a private library and profile; never starts a host or changes the source library.</summary>
public static class HyperlibEditorVerifier
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".hyperlib", ".hypershell", "bin", "obj", "node_modules", "app", "output", "artifacts", "publish", ".vs" };

    public static async Task VerifyAsync(string outputRoot, string sourceLibraryRoot)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLibraryRoot));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (SamePath(source, output) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Editor verification output must be outside the source library.");
        var fixture = Path.Combine(output, "editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var report = new Receipt { SourceLibrary = source, Fixture = fixture };
        var original = Fingerprint(source);
        try
        {
            var library = Path.Combine(fixture, "library");
            CopySource(source, library);
            Check(report, Fingerprint(library) == original, "The isolated fixture starts with the exact source authoring files.");
            await VerifyTemplatePipelineAsync(library, report);
            await RunStaAsync(async () =>
            {
                LoadResources();
                await VerifyDialogFocusAsync(library, fixture, report);
                await VerifyWorkspaceAsync(library, fixture, report);
                await VerifyEntryAsync(library, fixture, report);
            });
            Check(report, Fingerprint(source) == original, "All original source authoring files remain byte-for-byte unchanged.");
            report.Passed = true;
        }
        catch (Exception error) { report.Failure = error.ToString(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "hyperlib-editor.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"HYPERLIB_EDITOR_OK|assertions:{report.Assertions.Count}|renders:{report.Renders.Count}|private-copy|{fixture}");
    }

    private static async Task VerifyTemplatePipelineAsync(string library, Receipt report)
    {
        var catalog = new HyperlibStudioTemplateCatalogService();
        var before = ReadState(library);
        var beforeFiles = Fingerprint(library);
        var listed = await catalog.ListAsync(library);
        Check(report, listed.Any(item => item.Id == ReusableTemplateCatalog.BlankId)
            && listed.Count(item => item.IsBuiltIn) >= BaseTemplateCatalog.List().Count,
            "Template discovery offers the blank foundation alongside the existing built-in designs.");
        Check(report, Fingerprint(library) == beforeFiles, "Listing templates is read-only and does not prepare projects or caches.");

        var blank = await catalog.CreateBlankAsync(library, "Acceptance blank template");
        var blankDefinition = ReusableTemplateCatalog.ReadDefinition(library, blank.Id);
        var emptyPage = TemplateValidator.CreateDefaultPage("empty-page", "empty-work", "An empty page", blankDefinition);
        Check(report, !blank.IsBuiltIn && Directory.Exists(blank.ProjectRoot)
            && File.Exists(Path.Combine(blank.ProjectRoot, VisualPageProjectService.ProjectManifestFileName)),
            "Creating a blank reusable template creates a persistent editable project with its own identity.");
        Check(report, blankDefinition.DefaultBlocks.Count == 0 && emptyPage.Blocks.Count == 0 && emptyPage.UsesEditableChrome,
            "An intentional blank template creates no demo text, banner, header, or fallback content.");
        Check(report, JsonEqual(ReadState(library), before), "Creating a reusable template does not create a Work or alter library content.");

        var builtIn = listed.First(item => item.IsBuiltIn && item.Id != ReusableTemplateCatalog.BlankId);
        var builtInBefore = ReusableTemplateCatalog.ReadDefinition(library, builtIn.Id);
        var duplicate = await catalog.DuplicateAsync(library, builtIn.Id, "Acceptance copied design");
        var duplicateDefinition = ReusableTemplateCatalog.ReadDefinition(library, duplicate.Id);
        Check(report, duplicate.Id != builtIn.Id && !duplicate.IsBuiltIn
            && JsonEqual(duplicateDefinition.Blocks, builtInBefore.Blocks)
            && JsonEqual(duplicateDefinition.DefaultBlocks, builtInBefore.DefaultBlocks),
            "Duplicating a built-in preserves its element schemas and starting composition in a new reusable template.");

        var loaded = await HyperlibStudioAuthoringService.LoadAsync(blank.ProjectRoot, "authoring.template.json");
        var edit = new HyperlibTemplateEditSession(loaded.Definition);
        var text = edit.Draft.Blocks.First(block => block.Fields.Any(field => field.Id == "text" && field.Type == "text"));
        var region = edit.Draft.Regions.First(item => item.AllowedBlocks.Count == 0 || item.AllowedBlocks.Contains(text.Id));
        var defaultBlock = edit.AddDefaultBlock(text.Id, region.Id);
        defaultBlock.Values["text"] = ContentJson.Value("A reusable starting point");
        var accent = edit.Draft.Appearance.First(field => field.Id == "accent");
        accent.DefaultValue = ContentJson.Value("#2468ac");
        await HyperlibStudioAuthoringService.SaveAsync(blank.ProjectRoot, loaded.Snapshot, edit.CreateDefinition());
        var reopened = await HyperlibStudioAuthoringService.LoadAsync(blank.ProjectRoot, "authoring.template.json");
        Check(report, reopened.Definition.DefaultBlocks.Single().Values["text"].GetString() == "A reusable starting point"
            && reopened.Definition.Appearance.Single(field => field.Id == "accent").DefaultValue?.GetString() == "#2468ac",
            "Template composition and appearance edits survive saving and reopening the actual authoring document.");
        var asset = Path.Combine(blank.ProjectRoot, "Shared", "acceptance-binary.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        var assetBytes = new byte[] { 0, 255, 17, 13, 10, 128, 42 };
        await File.WriteAllBytesAsync(asset, assetBytes);
        var customCopy = await catalog.DuplicateAsync(library, blank.Id, "Acceptance independent copy");
        Check(report, JsonEqual(ReusableTemplateCatalog.ReadDefinition(library, customCopy.Id).DefaultBlocks, reopened.Definition.DefaultBlocks)
            && File.ReadAllBytes(Path.Combine(customCopy.ProjectRoot, "Shared", "acceptance-binary.dat")).SequenceEqual(assetBytes),
            "Duplicating an edited custom template preserves the authored composition and binary assets.");

        var store = new ContentStore(library);
        await store.InitializeAsync();
        store.TemplateResolver = id => ReadWorkDefinition(library, id);
        using var handler = new StoreGateway(store, library);
        using var client = new HttpClient(handler);
        using var service = new HyperlibStudioWorkspaceService(client);
        using var model = new HyperlibStudioWorkspaceViewModel(library,
            (_, _) => Task.FromResult(new Uri("http://127.0.0.1:54321/")), service);
        await model.LoadAsync();
        var revision = model.Revision;
        var parent = model.WorkParents.First().Id;
        var beforeLegacyAttempt = Fingerprint(library);
        handler.TemplateCatalogVersion = null;
        Check(report, !await model.CreateWorkAsync(parent, "Unsupported old-host Work", blank.Id)
            && model.ErrorMessage?.Contains("Restart HyperLib in MyNet", StringComparison.Ordinal) == true
            && handler.OperationRequests == 0,
            "An older gateway without reusable-template support is rejected before any write, with an actionable restart message.");
        Check(report, Fingerprint(library) == beforeLegacyAttempt && model.Revision == revision,
            "The rejected old-host attempt preserves the saved reusable template, content, and editor revision.");
        handler.TemplateCatalogVersion = 1;
        Check(report, await model.CreateWorkAsync(parent, "Acceptance first Work", blank.Id),
            "The native create-Work workflow accepts an edited custom reusable template: " + model.ErrorMessage);
        var firstWork = model.LastCreatedWork ?? throw new InvalidOperationException("New Work identity was not returned.");
        var firstState = await store.ReadAsync();
        var home = firstState.Pages.Values.Single(page => page.WorkId == firstWork.Id);
        Check(report, firstState.Revision == revision + 1 && firstState.Nodes.Count == before.Nodes.Count + 2
            && firstState.Nodes.Single(node => node.Id == firstWork.Id).StarterId == blank.Id
            && home.Blocks.Single().Values["text"].GetString() == "A reusable starting point",
            "A custom Work and its Home page commit together with the chosen template's starting content.");
        var firstRoot = Path.Combine(library, "Works", firstWork.Id);
        Check(report, File.Exists(Path.Combine(firstRoot, VisualPageProjectService.ProjectManifestFileName))
            && File.ReadAllBytes(Path.Combine(firstRoot, "Shared", "acceptance-binary.dat")).SequenceEqual(assetBytes)
            && (await store.ReadAsync()).Nodes.Single(node => node.Id == firstWork.Id).Appearance["accent"].GetString() == "#2468ac",
            "The created Work has independent project source, byte-preserved assets, and the template's appearance defaults.");
        var existingPage = JsonSerializer.Serialize(home, ContentJson.Options);
        var copiedDefinition = ReusableTemplateCatalog.ReadDefinition(library, customCopy.Id);

        reopened.Definition.DefaultBlocks.Single().Values["text"] = ContentJson.Value("Future reusable content");
        await HyperlibStudioAuthoringService.SaveAsync(blank.ProjectRoot, reopened.Snapshot, reopened.Definition);
        Check(report, ReadWorkDefinition(library, firstWork.Id)!.DefaultBlocks.Single().Values["text"].GetString() == "A reusable starting point"
            && JsonEqual(ReusableTemplateCatalog.ReadDefinition(library, customCopy.Id), copiedDefinition),
            "Editing a reusable template leaves existing Work source and other reusable copies independent.");
        Check(report, await model.CreatePageAsync(firstWork.Id, "Page after template edit"),
            "The original Work can create another Page after its reusable source template changes: " + model.ErrorMessage);
        var afterPage = await store.ReadAsync();
        Check(report, afterPage.Pages.Values.Single(page => page.Title == "Page after template edit").Blocks.Single().Values["text"].GetString() == "A reusable starting point",
            "Later Pages in an existing Work inherit that Work's own defaults rather than the updated reusable template.");

        var workEdit = await HyperlibStudioAuthoringService.LoadAsync(firstRoot, "authoring.template.json");
        workEdit.Definition.DefaultBlocks.Single().Values["text"] = ContentJson.Value("Work-local content");
        await HyperlibStudioAuthoringService.SaveAsync(firstRoot, workEdit.Snapshot, workEdit.Definition);
        Check(report, await model.CreatePageAsync(firstWork.Id, "Page after Work edit"),
            "Editing Work defaults permits another Page creation: " + model.ErrorMessage);
        var afterWorkEdit = await store.ReadAsync();
        Check(report, afterWorkEdit.Pages.Values.Single(page => page.Title == "Page after Work edit").Blocks.Single().Values["text"].GetString() == "Work-local content"
            && JsonSerializer.Serialize(afterWorkEdit.Pages[home.Id], ContentJson.Options) == existingPage,
            "New Work defaults affect later Pages while preserving existing Home content and revision.");
        Check(report, await model.CreatePageAsync(firstWork.Id, "Deliberately blank page", true),
            "A designed Work can explicitly create a blank content Page: " + model.ErrorMessage);
        Check(report, (await store.ReadAsync()).Pages.Values.Single(page => page.Title == "Deliberately blank page") is { Blocks.Count: 0, UsesEditableChrome: true },
            "The explicit blank Page choice bypasses the Work's populated defaults and suppresses legacy chrome.");
        Check(report, await model.CreateWorkAsync(parent, "Acceptance second Work", blank.Id),
            "The revised reusable template remains available for a second independent Work: " + model.ErrorMessage);
        var secondWork = model.LastCreatedWork!;
        Check(report, (await store.ReadAsync()).Pages.Values.Single(page => page.WorkId == secondWork.Id).Blocks.Single().Values["text"].GetString() == "Future reusable content",
            "A later Work receives the latest saved reusable defaults without borrowing another Work's edits.");

        var freshBlank = await catalog.CreateBlankAsync(library, "Acceptance truly empty");
        await model.RefreshAsync();
        Check(report, await model.CreateWorkAsync(parent, "Acceptance empty Work", freshBlank.Id),
            "A completely blank reusable template can create a Work: " + model.ErrorMessage);
        var emptyWork = model.LastCreatedWork!;
        var finalState = await new ContentStore(library).ReadAsync();
        Check(report, finalState.Pages.Values.Single(page => page.WorkId == emptyWork.Id) is { Blocks.Count: 0, UsesEditableChrome: true },
            "The persisted blank Work Home stays empty after reloading the atomic content store.");
        var fromWork = await catalog.CreateFromWorkAsync(library, firstWork.Id, "Acceptance saved Work design");
        Check(report, ReusableTemplateCatalog.ReadDefinition(library, fromWork.Id).DefaultBlocks.Single().Values["text"].GetString() == "Work-local content",
            "Saving a Work as a reusable template preserves its current local design defaults.");
        Check(report, JsonEqual(ReusableTemplateCatalog.ReadDefinition(library, builtIn.Id), builtInBefore),
            "Built-in definitions remain unchanged after template, Work, and Page authoring.");
        Check(report, before.Pages.All(pair => JsonEqual(pair.Value, finalState.Pages[pair.Key])),
            "All pre-existing content pages are preserved throughout the new authoring pipeline.");
        var durable = await new HyperlibStudioTemplateCatalogService().ListAsync(library);
        Check(report, new[] { blank.Id, duplicate.Id, customCopy.Id, freshBlank.Id, fromWork.Id }.All(id => durable.Any(item => item.Id == id)),
            "Every newly created reusable template is rediscovered by a fresh catalog instance.");

        const string localTemplateSource = "Templates/Presentation/SourceDiscovery.razor";
        foreach (var project in new[] { library, firstRoot, blank.ProjectRoot })
        {
            var sourcePath = Path.Combine(project, localTemplateSource);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            await File.WriteAllTextAsync(sourcePath, "<section>Local presentation source</section>");
            var snapshot = await service.ReadAsync(project);
            var sources = snapshot.SourceGroups.SelectMany(group => group.Items).Select(item => item.RelativePath).ToArray();
            Check(report, sources.Contains(localTemplateSource, StringComparer.OrdinalIgnoreCase),
                $"The {Path.GetFileName(project)} code browser includes ordinary local Templates source.");
            if (project == library)
                Check(report, !sources.Any(path => path.StartsWith("Templates/" + blank.Id + "/", StringComparison.OrdinalIgnoreCase)),
                    "The library code browser excludes independent reusable-template projects from its own source.");
        }
    }

    private static async Task VerifyWorkspaceAsync(string library, string fixture, Receipt report)
    {
        var before = Fingerprint(library);
        using var model = new HyperlibStudioWorkspaceViewModel(library);
        await model.LoadAsync();
        Check(report, !model.HasError, "The new template library loads into the native editor: " + model.ErrorMessage);
        Check(report, model.CanManageTemplates && !model.CanMutate,
            "Reusable template authoring is available without starting or connecting to a content host.");
        model.TemplateSearchText = "Acceptance independent copy";
        Check(report, model.Templates.Count == 1 && model.SelectedTemplate?.Title == "Acceptance independent copy",
            "Template search finds the saved custom design and selects its actual persistent identity.");
        var selected = model.SelectedTemplate!.Id;
        await model.RefreshAsync();
        Check(report, model.SelectedTemplate?.Id == selected && model.TemplateSearchText == "Acceptance independent copy",
            "Refreshing the template catalog preserves the search and selected reusable template.");
        model.TemplateSearchText = "";
        Check(report, Fingerprint(library) == before, "Catalog loading, search, and refresh leave library files unchanged.");
        var beforeContent = ReadState(library);
        Check(report, await model.CreateBlankTemplateAsync("Created by the native model") && model.SelectedTemplate?.Title == "Created by the native model",
            "The native create-template action persists and selects the new template without a gateway: " + model.ErrorMessage);
        var created = model.SelectedTemplate!.Id;
        Check(report, await model.GetTemplateProjectAsync(created) is { } opened && File.Exists(Path.Combine(opened, "authoring.template.json")),
            "The selected reusable template resolves to an editable source project.");
        Check(report, JsonEqual(beforeContent, ReadState(library)), "Native template creation leaves Works and Pages unchanged.");
        before = Fingerprint(library);
        using var view = new HyperlibStudioWorkspaceView(model);
        var tabs = Named<TabControl>(view, "WorkspaceTabs");
        var templateTab = Named<TabItem>(view, "TemplatesTab");
        Check(report, ReferenceEquals(tabs.SelectedItem, templateTab), "A library editor opens directly to its reusable Templates view.");
        foreach (var width in new[] { 760d, 1280d })
        {
            tabs.SelectedItem = templateTab;
            foreach (var scale in new[] { 1d, 1.5d })
            {
                var path = Path.Combine(fixture, $"hyperlib-editor-templates-{width:0}-{scale * 100:0}.png");
                Render(view, path, width, scale);
                report.Renders.Add(path);
            }
            var create = Named<Button>(view, "NewTemplateButton");
            var createBounds = create.TransformToAncestor(view).TransformBounds(new Rect(create.RenderSize));
            Check(report, create.Visibility == Visibility.Visible && create.IsEnabled && create.ActualWidth > 35 && create.ActualHeight > 20
                && createBounds.Left >= 0 && createBounds.Right <= view.ActualWidth + 1
                && createBounds.Top >= 0 && createBounds.Bottom <= view.ActualHeight + 1,
                $"At {width:0}px, New template remains visible, enabled, and inside the viewport.");
            foreach (var tab in tabs.Items.OfType<TabItem>().Where(item => item.Visibility == Visibility.Visible))
            {
                var bounds = tab.TransformToAncestor(view).TransformBounds(new Rect(tab.RenderSize));
                Check(report, tab.ActualWidth > 20 && bounds.Right <= view.ActualWidth + 1 && bounds.Left >= -1,
                    $"At {width:0}px, the {tab.Header} tab remains a visible navigation target.");
            }
        }
        Check(report, Fingerprint(library) == before, "Loading and rendering the actual editor does not write library source or content.");
    }

    private static async Task VerifyEntryAsync(string library, string fixture, Receipt report)
    {
        var environment = Environment.GetEnvironmentVariable("HYPERLIB_ROOT");
        var before = Fingerprint(library);
        try
        {
            Environment.SetEnvironmentVariable("HYPERLIB_ROOT", library);
            var settings = new SettingsService(Path.Combine(fixture, "private-profile"));
            var configuration = settings.Load();
            configuration.Application.CreateTerminalOnStartup = false;
            configuration.Explorer.StartupDirectory = fixture;
            settings.Save(configuration);
            using var workspace = new MainWindowViewModel(settings);
            await workspace.FileExplorer.WhenIdleAsync();
            var coreBrowser = workspace.CreateBrowser("about:blank");
            var editor = await workspace.OpenHyperlibEditorAsync();
            Check(report, editor is { IsHyperlibEditor: true } && SamePath(editor.ProjectRoot, library)
                && editor.Title == "Hyperlib Editor" && workspace.WorkspaceWindowTitle == "Hyperlib Editor"
                && ReferenceEquals(workspace.ActiveWorkspaceTab, editor),
                "The real Hyperlib Editor shortcut resolves the configured library and activates a dedicated titled editor.");
            Check(report, !editor!.HasUnsavedChanges && !editor.IsLeftToolPanelOpen && !editor.IsRightToolPanelOpen,
                "Entering the dedicated editor hides generic Studio panels without dirtying the project.");
            var shell = editor.StudioView;
            foreach (var size in new[] { new Size(760, 780), new Size(1440, 900) })
            {
                var imagePath = Path.Combine(fixture, $"hyperlib-editor-shell-{size.Width:0}.png");
                Render(shell, imagePath, size.Width, 1, size.Height);
                report.Renders.Add(imagePath);
                var central = Named<Border>(shell, "CentralWorkspaceHost");
                var left = Named<Border>(shell, "LeftToolPanelHost");
                var right = Named<Border>(shell, "RightToolPanelHost");
                var panelGrid = Named<Grid>(shell, "StudioPanelGrid");
                var manager = Descendants<HyperlibStudioWorkspaceView>(central).Single();
                var managerBounds = manager.TransformToAncestor(shell).TransformBounds(new Rect(manager.RenderSize));
                // A collapsed grid cell can still give its Border a pixel-rounded chrome width.
                // Assert allocated layout space, which determines whether panels steal the manager's viewport.
                Check(report, new[] { 0, 1, 3, 4 }.All(index => panelGrid.ColumnDefinitions[index].ActualWidth < 1)
                    && central.ActualWidth >= size.Width - 12
                    && manager.ActualWidth >= central.ActualWidth - 45 && manager.ActualHeight > size.Height * .75
                    && managerBounds.Left >= -1 && managerBounds.Right <= size.Width + 1
                    && managerBounds.Top >= -1 && managerBounds.Bottom <= size.Height + 1,
                    $"At {size.Width:0}x{size.Height:0}, the actual Studio shell gives the template manager the central workspace with both generic side panels collapsed. "
                    + $"Geometry: shell={shell.ActualWidth:0.###}x{shell.ActualHeight:0.###}; left={left.ActualWidth:0.###}; right={right.ActualWidth:0.###}; "
                    + $"columnWidths=[{string.Join(',', panelGrid.ColumnDefinitions.Select(column => column.ActualWidth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))}]; "
                    + $"central={central.ActualWidth:0.###}x{central.ActualHeight:0.###}; manager={manager.ActualWidth:0.###}x{manager.ActualHeight:0.###}; "
                    + $"managerBounds=({managerBounds.Left:0.###},{managerBounds.Top:0.###})..({managerBounds.Right:0.###},{managerBounds.Bottom:0.###}); "
                    + $"leftBorder={left.BorderThickness}; rightBorder={right.BorderThickness}.");
                Check(report, ReferenceEquals(Named<TabControl>(manager, "WorkspaceTabs").SelectedItem, Named<TabItem>(manager, "TemplatesTab"))
                    && !editor.HasUnsavedChanges,
                    $"At {size.Width:0}px, rendering the integrated shell retains the Templates start view without dirtying the library.");
                Check(report, central.Margin.Top == 0 && managerBounds.Top < 2
                    && Named<WrapPanel>(shell, "HyperlibToolbar").Visibility == Visibility.Collapsed
                    && !Named<Popup>(shell, "StudioLeftPanelEdgeRevealHost").IsOpen
                    && !Named<Popup>(shell, "StudioRightPanelEdgeRevealHost").IsOpen
                    && !Named<Popup>(shell, "AiAssistantBottomEdgeRevealHost").IsOpen,
                    $"At {size.Width:0}px, the focused editor reserves no generic toolbar row or tool-panel reveal controls.");
                var paneTabs = Descendants<TabControl>(Named<ContentControl>(shell, "PaneHost"))
                    .Single(control => control.Tag is string);
                Check(report, !paneTabs.AllowDrop && paneTabs.Items.Count == 1
                    && paneTabs.Template.FindName("HeaderPanel", paneTabs) is Panel { Visibility: Visibility.Collapsed },
                    $"At {size.Width:0}px, the focused object workspace has no duplicate Workspace tab, source-add tab, or docking gesture.");
            }
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var menu = XDocument.Load(Path.Combine(SourceRoot(), "src", "Hypershell", "MainWindow.xaml"));
            Check(report, menu.Descendants(presentation + "MenuItem").Count(item =>
                item.Attribute("Header")?.Value == "Hyperlib Editor" && item.Attribute("Click")?.Value == "OpenHyperlibEditorClick") == 1,
                "The application menu exposes one explicit Hyperlib Editor entry bound to its dedicated opening handler.");
            editor.HyperlibWorkspace!.DraftTitle = "Keep the unfinished creation title";
            var cssDraft = editor.UserCss + "\n/* Unfinished editor acceptance draft. */\n";
            editor.UserCss = cssDraft;
            var projectRoot = editor.ProjectRoot;
            var count = workspace.WorkspaceTabs.Count;
            var same = await workspace.OpenHyperlibEditorAsync();
            Check(report, ReferenceEquals(same, editor) && workspace.WorkspaceTabs.Count == count
                && same!.HyperlibWorkspace!.DraftTitle == "Keep the unfinished creation title" && same.ProjectRoot == projectRoot
                && same.UserCss == cssDraft && same.HasUnsavedChanges,
                "Repeated shortcut entry focuses the same editor and preserves unfinished creation and source drafts.");
            var manifest = await workspace.OpenVisualPageProjectAsync(Path.Combine(library, VisualPageProjectService.ProjectManifestFileName));
            Check(report, ReferenceEquals(manifest, editor) && editor.IsHyperlibEditor,
                "Opening the library manifest reuses the existing dedicated editor without changing its mode.");
            var workNode = ReadState(library).Nodes.First(node => node.Type == "work");
            var workEditor = await workspace.OpenVisualPageProjectAsync(Path.Combine(library, workNode.ProjectPath!, VisualPageProjectService.ProjectManifestFileName))
                ?? throw new InvalidOperationException("The real Work workspace could not be opened.");
            var contentPage = new BrowserTabViewModel("about:blank");
            contentPage.EnableStudioContentPage("Acceptance arrangement Page", "acceptance-page", workEditor.ProjectRoot);
            workspace.WorkspaceTabs.Add(contentPage);
            workspace.ActiveWorkspaceTab = contentPage;
            Check(report, editor.StudioObjectKind == "Template" && workEditor.StudioObjectKind == "Work"
                && contentPage.StudioObjectKind == "Page" && workspace.IsVisualPageStudioActive
                && workspace.WorkspaceWindowTitle == "Hyperlib"
                && workspace.StudioWorkspaceTabs.Cast<object>().ToHashSet().SetEquals([editor, workEditor, contentPage])
                && !workspace.StudioWorkspaceTabs.Contains(coreBrowser),
                "Studio object navigation includes Template, Work, and Page identities while the open core Browser stays outside that workspace.");
            var header = CreateHeaderSurface(menu, workspace);
            foreach (var width in new[] { 760d, 1440d })
            {
                var path = Path.Combine(fixture, $"studio-object-header-{width:0}.png");
                Render(header, path, width, 1, 46);
                report.Renders.Add(path);
                var objectTabs = Named<ListBox>(header, "StudioObjectTabs");
                Check(report, Named<Grid>(header, "CoreWorkspaceHeader").Visibility == Visibility.Collapsed
                    && Named<DockPanel>(header, "StudioWorkspaceHeader").Visibility == Visibility.Visible
                    && objectTabs.Items.Count == 3 && ReferenceEquals(objectTabs.SelectedItem, contentPage),
                    $"At {width:0}px, the production header hides generic tab creation and selects the active content Page in the object tabs.");
                foreach (var item in objectTabs.Items)
                {
                    var container = objectTabs.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    Check(report, container is { ActualWidth: > 60, ActualHeight: > 25 }
                        && Descendants<TextBlock>(container).Any(text => text.Text is "Template" or "Work" or "Page"),
                        $"At {width:0}px, each native object tab renders its explicit Template, Work, or Page role.");
                }
                var creation = Descendants<Button>(Named<DockPanel>(header, "StudioWorkspaceHeader"))
                    .Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "New Studio object");
                creation.ContextMenu!.PlacementTarget = creation;
                creation.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var visibleCreationItems = creation.ContextMenu!.Items.OfType<MenuItem>()
                    .Where(item => item.Visibility == Visibility.Visible).ToList();
                Check(report, visibleCreationItems.Select(item => item.Header?.ToString())
                    .SequenceEqual(["Template", "Work", "Write in Hyperlib"])
                    && visibleCreationItems.Last().Tag as string == "new-page"
                    && creation.ContextMenu.Items.OfType<MenuItem>()
                        .Where(item => item.Header?.ToString() is "Create project" or "Open project")
                        .All(item => item.Visibility == Visibility.Collapsed)
                    && workspace.IsHyperlibStudioActive,
                    "The focused creation menu creates Templates and Works and hands Page writing to Hyperlib through its existing routing identity.");
            }
            var pageCreation = menu.Descendants(presentation + "MenuItem").Single(item => item.Attribute("Tag")?.Value == "new-page");
            Check(report, pageCreation.Descendants(presentation + "Setter").Any(item =>
                item.Attribute("Property")?.Value == "Header" && item.Attribute("Value")?.Value == "Page"),
                "Ordinary Hyperpage Studio retains its Page creation label alongside the Hyperlib writer handoff.");
            workspace.ReturnToHypershell();
            Render(header, Path.Combine(fixture, "studio-return-core.png"), 760, 1, 46);
            Check(report, ReferenceEquals(workspace.ActiveWorkspaceTab, coreBrowser) && !workspace.IsVisualPageStudioActive
                && Named<Grid>(header, "CoreWorkspaceHeader").Visibility == Visibility.Visible
                && Named<DockPanel>(header, "StudioWorkspaceHeader").Visibility == Visibility.Collapsed
                && editor.UserCss == cssDraft && editor.HasUnsavedChanges && workspace.WorkspaceTabs.Contains(contentPage),
                "Returning to Hypershell restores the previous core tab and its chrome while every Studio object and unfinished source draft stays open.");
            workspace.ActiveWorkspaceTab = contentPage;
            Check(report, workspace.CloseWorkspaceTab(contentPage) && workspace.IsVisualPageStudioActive
                && ReferenceEquals(workspace.ActiveWorkspaceTab, workEditor) && editor.UserCss == cssDraft
                && !workspace.StudioWorkspaceTabs.Contains(contentPage),
                "Closing the active Page selects the neighboring Work in Studio and preserves unrelated unsaved Template source.");
            workspace.ActiveWorkspaceTab = editor;
            await workspace.PrepareForShutdownAsync();
            var saved = await new VisualPageStudioSessionService(settings.VisualPageStudioSessionPath).LoadAsync();
            var savedEditor = saved.Projects.Single(item => SamePath(item.ProjectRoot, library));
            Check(report, savedEditor.IsHyperlibEditor && savedEditor.HasUserSourceRecovery && savedEditor.UserCss == cssDraft,
                "The actual private session persistence records dedicated-editor mode together with recoverable unsaved source.");
            Check(report, Fingerprint(library) == before, "Editor entry and repeated focus leave source, templates, and content unchanged.");
        }
        finally { Environment.SetEnvironmentVariable("HYPERLIB_ROOT", environment); }
    }

    private static async Task VerifyDialogFocusAsync(string library, string fixture, Receipt report)
    {
        var before = Fingerprint(library);
        var reusable = (await new HyperlibStudioTemplateCatalogService().ListAsync(library)).First(item => !item.IsBuiltIn);
        var loadedTemplate = await HyperlibStudioAuthoringService.LoadAsync(reusable.ProjectRoot, "authoring.template.json");
        var templateDialog = new Hypershell.HyperlibTemplateEditorDialog(reusable.ProjectRoot, loadedTemplate.Definition,
            loadedTemplate.Snapshot, reusableTemplate: true);
        var templateSurface = (FrameworkElement)templateDialog.Content;
        var templateRender = Path.Combine(fixture, "template-element-library-dialog.png");
        Render(templateSurface, templateRender, 1056, 1, 744); report.Renders.Add(templateRender);
        var templateTabs = Descendants<TabControl>(templateSurface).First();
        Check(report, templateTabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString()).SequenceEqual(["Elements", "Advanced"])
            && (templateTabs.SelectedItem as TabItem)?.Tag as string == "elements",
            "A Template opens directly to its reusable element library; layout, locations, and appearance contract controls remain secondary.");
        var fields = Descendants<ListBox>(templateSurface).Single(control =>
            System.Windows.Automation.AutomationProperties.GetName(control) == "Element fields");
        Check(report, fields.Items.Count > 0 && fields.ActualHeight > 30
            && Descendants<TextBlock>(templateSurface).Any(text => text.Text == "Field definitions" && text.ActualHeight > 0),
            "Template field definitions are visible in the primary element workspace without expanding source or advanced schema panels.");
        templateDialog.SelectSection("defaults");
        Render(templateSurface, Path.Combine(fixture, "template-legacy-layout-advanced.png"), 1056, 1, 744);
        Check(report, (templateTabs.SelectedItem as TabItem)?.Tag as string == "advanced",
            "Existing Template layout presets remain reachable through their original section identity under Advanced.");

        var work = ReadState(library).Nodes.First(node => node.Type == "work");
        var workRoot = Path.Combine(library, work.ProjectPath!);
        var loadedWork = await HyperlibStudioAuthoringService.LoadAsync(workRoot, "authoring.template.json");
        var workDialog = new Hypershell.HyperlibTemplateEditorDialog(workRoot, loadedWork.Definition, loadedWork.Snapshot);
        var workSurface = (FrameworkElement)workDialog.Content;
        var workRender = Path.Combine(fixture, "work-page-structure-dialog.png");
        Render(workSurface, workRender, 1056, 1, 744); report.Renders.Add(workRender);
        var workTabs = Descendants<TabControl>(workSurface).First();
        Check(report, workTabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString())
            .SequenceEqual(["Page layout", "Element locations", "Special elements", "Advanced"])
            && (workTabs.SelectedItem as TabItem)?.Tag as string == "defaults",
            "A Work opens with Page layout and element locations as primary authoring tools alongside its special elements.");
        workDialog.SelectSection("locations");
        Render(workSurface, Path.Combine(fixture, "work-element-locations-dialog.png"), 1056, 1, 744);
        Check(report, (workTabs.SelectedItem as TabItem)?.Tag as string == "regions",
            "The Work location action selects its own layout regions through a compatible section alias.");
        workDialog.SelectSection("styles");
        Check(report, (workTabs.SelectedItem as TabItem)?.Tag as string == "advanced",
            "Low-level appearance field contracts stay Advanced while actual shared styling remains in the Work workspace.");
        Check(report, Fingerprint(library) == before,
            "Opening and navigating the refined Template and Work dialogs preserves every authoring and content file.");
    }

    private static TemplateDefinition? ReadWorkDefinition(string library, string workId)
    {
        TemplateValidator.ValidateId(workId, "work");
        var path = Path.Combine(library, "Works", workId, "authoring.template.json");
        return File.Exists(path) ? HyperlibTemplateEditSession.ParseDefinition(File.ReadAllText(path)) : null;
    }
    private static LibraryState ReadState(string library) => JsonSerializer.Deserialize<LibraryState>(
        File.ReadAllBytes(Path.Combine(library, "Content", "library.json")), ContentJson.Options)!;
    private static bool JsonEqual<T>(T left, T right) => JsonSerializer.Serialize(left, ContentJson.Options) == JsonSerializer.Serialize(right, ContentJson.Options);
    private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static void Check(Receipt report, bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        report.Assertions.Add(description);
    }
    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement
        => root.FindName(name) as T ?? throw new InvalidOperationException("Missing editor control: " + name);

    private static Grid CreateHeaderSurface(XDocument source, MainWindowViewModel workspace)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var markup = new XElement(presentation + "Grid",
            source.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            new XAttribute("Background", "{StaticResource PanelBrush}"),
            new XAttribute("UseLayoutRounding", true));
        foreach (var name in new[] { "CoreWorkspaceHeader", "StudioWorkspaceHeader" })
            markup.Add(new XElement(source.Descendants().Single(element => element.Attribute(xaml + "Name")?.Value == name)));
        // Render the actual production header with the private model; callbacks remain unhooked.
        markup.Descendants().Attributes("Click").Remove();
        markup.Descendants(presentation + "TabPanel").Attributes("IsItemsHost").Remove();
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal)
                && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        var surface = (Grid)XamlReader.Parse(markup.ToString());
        surface.DataContext = workspace;
        return surface;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Fixture links are not supported: " + entry);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                if (!Excluded.Contains(Path.GetFileName(entry))) foreach (var child in Files(entry)) yield return child;
            }
            else yield return entry;
        }
    }
    private static void CopySource(string source, string destination)
    {
        if (Directory.Exists(destination)) throw new InvalidOperationException("Fixture destination already exists.");
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Cannot copy a linked library.");
        Directory.CreateDirectory(destination);
        long bytes = 0;
        var count = 0;
        foreach (var file in Files(source))
        {
            if (++count > 30_000 || (bytes += new FileInfo(file).Length) > 768L * 1024 * 1024) throw new InvalidOperationException("Fixture exceeds bounded copy budget.");
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
    private static string Fingerprint(string root)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Files(root))
        {
            digest.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file) + "\n"));
            using var input = File.OpenRead(file);
            digest.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
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
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Hyperlib Editor acceptance" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private static void LoadResources()
    {
        if (Application.Current is not null)
        {
            if (!Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Run editor verification in its own process.");
            return;
        }
        var source = XDocument.Load(Path.Combine(SourceRoot(), "src", "Hypershell", "App.xaml")).Root!;
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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }
    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "Hypershell", "App.xaml"))) return directory.FullName;
        throw new FileNotFoundException("Run the verifier from the isolated Hypershell source checkout.");
    }
    private static void Render(FrameworkElement surface, string path, double width, double scale, double height = 780)
    {
        surface.Width = width; surface.Height = height;
        surface.Measure(new Size(width, surface.Height));
        surface.Arrange(new Rect(0, 0, width, surface.Height));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(surface.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
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

    /// <summary>Adapts the actual atomic store and scaffolder to the native HTTP client without opening a port.</summary>
    private sealed class StoreGateway(ContentStore store, string root) : HttpMessageHandler
    {
        public int? TemplateCatalogVersion { get; set; } = 1;
        public int OperationRequests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/health")
            {
                var response = Json(HttpStatusCode.OK, new { app = "hyperlib", root, schemaVersion = 1,
                    templateCatalogVersion = TemplateCatalogVersion, origin = request.RequestUri.GetLeftPart(UriPartial.Authority) });
                response.Headers.Add("X-HyperLib-App", "hyperlib");
                return response;
            }
            if (path == "/api/state") return Json(HttpStatusCode.OK, await store.ReadAsync(token));
            if (path == "/api/base-templates") return Json(HttpStatusCode.OK, ReusableTemplateCatalog.List(root));
            if (request.Method == HttpMethod.Post && path == "/api/operations")
            {
                OperationRequests++;
                if (!request.Headers.TryGetValues("X-HyperLib-Client", out var client) || client.SingleOrDefault() != "editor"
                    || !request.Headers.TryGetValues("Origin", out var origin) || origin.SingleOrDefault() != request.RequestUri.GetLeftPart(UriPartial.Authority))
                    return Json(HttpStatusCode.Forbidden, new { error = "Missing native gateway identity headers." });
                try
                {
                    var batch = JsonSerializer.Deserialize<OperationBatch>(await request.Content!.ReadAsStringAsync(token), ContentJson.Options)!;
                    var state = await store.ApplyAsync(batch, token);
                    foreach (var operation in batch.Operations.Where(operation => operation.Kind == "createNode" && operation.NodeType == "work"))
                    {
                        var work = state.Nodes.Single(node => node.Id == operation.Id);
                        await TemplateProjectScaffolder.EnsureAsync(root, Path.Combine(root, work.ProjectPath!), work.Id, work.Title, token, work.StarterId);
                    }
                    return Json(HttpStatusCode.OK, state);
                }
                catch (ConflictException error) { return Json(HttpStatusCode.Conflict, new { error = error.Message }); }
                catch (ContentValidationException error) { return Json(HttpStatusCode.BadRequest, new { error = error.Message }); }
            }
            return Json(HttpStatusCode.NotFound, new { error = "Unexpected acceptance request: " + path });
        }
        private static HttpResponseMessage Json<T>(HttpStatusCode status, T value) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(value, ContentJson.Options), Encoding.UTF8, "application/json") };
    }
}
