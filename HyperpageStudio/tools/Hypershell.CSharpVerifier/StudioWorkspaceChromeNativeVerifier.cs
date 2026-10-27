using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using HyperLib.Core;
using Hypershell;
using Hypershell.Controls;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

/// <summary>Actual native Studio pane, file tabs, and embedded authoring chrome in one private hidden HWND.</summary>
public static partial class StudioWorkspaceChromeNativeVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string libraryRoot)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "studio-workspace-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var receipt = new Receipt { Fixture = fixture };
        var environment = new[] { SettingsService.ApplicationDataOverrideVariable, SettingsService.SharedMyNetDirectoryOverrideVariable, "HYPERLIB_ROOT" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, Path.Combine(fixture, "profile"));
            Environment.SetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable, Path.Combine(fixture, "profile", "AtlasMyNet"));
            await RunStaAsync(async () => { LoadResources(repositoryRoot); await RunAsync(fixture, libraryRoot, receipt); });
            receipt.Passed = true;
        }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally
        {
            foreach (var pair in environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await File.WriteAllTextAsync(Path.Combine(fixture, "studio-workspace-chrome-native-verification.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STUDIO_WORKSPACE_CHROME_NATIVE_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }

    private static async Task RunAsync(string fixture, string sourceLibrary, Receipt receipt)
    {
        var project = await new HyperlibWorkProjectService(sourceLibrary).CreateAsync(fixture, "Workspace chrome acceptance",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Workspace chrome acceptance"));
        receipt.Project = project;
        var ownedLibrary = Path.GetFullPath(Path.Combine(project, "../.."));
        Environment.SetEnvironmentVariable("HYPERLIB_ROOT", ownedLibrary);
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projects = new VisualPageProjectService(validator, generator, catalog);
        var session = await projects.OpenAsync(project);
        using var preview = new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator, Path.Combine(fixture, "preview-cache"));
        using var localHost = new VisualPageLocalHostService(new BoundedProcessRunner());
        using var model = await VisualPageStudioTabViewModel.CreateAsync(session, projects, new VisualPageMutationEngine(validator), generator,
            validator, catalog, new VisualPageTemplateService(catalog, Path.Combine(fixture, "personal-templates")), new VisualPageDesignCanvasRenderer(catalog),
            preview, localHost, new VisualPageUserSourceService(), new AtlasArchivePublicationService(new BoundedProcessRunner(), projects, generator, validator, Path.Combine(fixture, "publications")));
        using var workHosts = new HyperlibWorkPreviewHostService();
        // MainWindow normally supplies this connection factory when it opens a Studio tab.
        // This standalone fixture uses the same real generated-Work host with an owned root.
        model.HyperlibWorkspace!.EndpointFactory = (root, cancellation) => workHosts.EnsureAsync(root, cancellation);
        model.CanvasOnlyCommand.Execute(null);
        // Exercise an actual legacy saved placement only for the protected authoring
        // inspector. No native floating tool or desktop Window is ever requested.
        model.SetToolPlacement("inspector", StudioToolHostKind.Floating,
            new StudioFloatingBounds { Left = -32000, Top = -32000, Width = 340, Height = 700 });
        using var library = new StudioAuthoringLibraryService(sourceLibrary, Path.Combine(fixture, "library-cache"));
        using var view = new VisualPageStudioView(model, Path.Combine(fixture, "authoring-browser-profile"), library);
        var nativeInspector = ((TabControl)view.FindName("LeftToolTabs")).Items.OfType<TabItem>().Single(item => Equals(item.Tag, "inspector"));
        var proxyGuard = typeof(VisualPageStudioView).GetMethod("IsAuthoringPanelProxy", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The authoring floating-placement guard is missing.");
        Check(receipt, (bool)proxyGuard.Invoke(view, new object[] { nativeInspector })!,
            "The actual native inspector is classified as a protected authoring-panel proxy before any saved placements are restored.");
        foreach (var popup in Descendants<Popup>(view)) popup.IsOpen = false;
        // Popup is not guaranteed to appear in the visual descendants before the
        // template is materialized. Suppress each named edge helper explicitly;
        // the unrelated assistant reveal binding must never open a fixture HWND.
        foreach (var name in new[] { "StudioLeftPanelEdgeRevealHost", "StudioRightPanelEdgeRevealHost", "AiAssistantBottomEdgeRevealHost" })
        {
            var popup = (Popup)view.FindName(name);
            System.Windows.Data.BindingOperations.ClearBinding(popup, Popup.IsOpenProperty);
            popup.IsOpen = false;
        }
        using var host = new HwndSource(new HwndSourceParameters("Hidden full Studio workspace chrome acceptance")
        {
            Width = 1500, Height = 1000, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
        });
        host.RootVisual = view;
        Layout(view, 1500, 1000);
        var writer = CachedWriter(view) ?? throw new InvalidOperationException("The actual native pane did not create its authoring control.");
        writer.StatusChanged += message => receipt.StatusMessages.Add(message);
        writer.WorkspaceActionRequested += action => receipt.Actions.Add(action);
        CoreWebView2? core = null;
        try
        {
            Safety(host.Handle, receipt, "attached");
            var lastNativeSample = DateTime.MinValue;
            await WaitAsync(async () =>
            {
                writer = CachedWriter(view) ?? writer;
                if (Core(writer) is not null) return true;
                if (DateTime.UtcNow - lastNativeSample > TimeSpan.FromSeconds(15))
                {
                    lastNativeSample = DateTime.UtcNow;
                    receipt.NativeDiagnostics = NativeDiagnostics(view, writer);
                    await File.WriteAllTextAsync(Path.Combine(fixture, "studio-workspace-chrome-initializing.json"),
                        JsonSerializer.Serialize(receipt.NativeDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
                }
                return false;
            }, "The full native Studio authoring WebView did not initialize.", 240);
            core = Core(writer)!;
            core.WebMessageReceived += (_, args) => receipt.Messages.Add(args.WebMessageAsJson);
            await WaitAsync(async () => writer.IsWorkspaceChromeReady && await TrueAsync(core, "window.__studioAuthoring?.state()?.currentGenerationReady === true && !document.querySelector('#studio-workspace-tabs')?.hidden"),
                "The actual native pane and embedded workspace chrome did not complete their handshake.", 240);
            Check(receipt, core.Environment.UserDataFolder.StartsWith(fixture, StringComparison.OrdinalIgnoreCase), "The complete native workspace uses a disposable authoring WebView profile.");
            Check(receipt, model.HasHyperlibAuthoring && !model.IsHyperlibEditor && ReferenceEquals(model.SaveHyperlibContentAsync?.Target, writer),
                "The full VisualPageStudioView contains the real Work editor and its original native save route.");
            var floating = (System.Collections.IDictionary)typeof(VisualPageStudioView).GetField("_floatingToolWindows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            Check(receipt, floating.Count == 0 && ReferenceEquals(nativeInspector.Parent, view.FindName("LeftToolTabs"))
                && model.Layout.ToolPlacements.Any(placement => placement.ToolId == "inspector" && placement.Host == StudioToolHostKind.Floating),
                "Restoring the real legacy floating inspector placement retains the single left Properties tool and creates no floating Window.");
            var dpi = VisualTreeHelper.GetDpi(view);
            receipt.DpiX = dpi.DpiScaleX; receipt.DpiY = dpi.DpiScaleY;
            await InspectAsync(view, writer, core, 1500, 1000, 1, fixture, receipt);
            await VerifyPrimaryFlyoutsAsync(view, model, writer, core, receipt);

            // The real metadata owner creates the document, then its existing layout API puts it
            // beside Authoring in the same pane. No browser/native bridge is mocked.
            var file = await model.CreateProjectDocumentAsync("css", "chrome-acceptance.css")
                ?? throw new InvalidOperationException(model.StatusMessage);
            Check(receipt, DockFileWithAuthoring(model, file.ContentId), "The real file buffer can be docked beside Authoring in its native pane.");
            SelectNativeAuthoring(view);
            await WaitAsync(async () => writer.IsWorkspaceChromeReady && await TrueAsync(core, "!!document.querySelector('[data-workspace-select=" + JsonSerializer.Serialize(file.ContentId) + "]') && window.__studioAuthoring.state().currentGenerationReady"),
                "The native file list was not mirrored into the embedded Authoring tab bar.", 120);
            Check(receipt, await TrueAsync(core, "(()=>{const id=" + JsonSerializer.Serialize(file.ContentId) + ";return document.querySelectorAll('[data-workspace-help]').length===1 && !!document.querySelector('#studio-workspace-help[data-workspace-help=\"canvas\"]') && !!document.querySelector('[data-workspace-close=\"'+CSS.escape(id)+'\"]') && !!document.querySelector('#studio-workspace-new');})()"),
                "The mirrored file retains its own close control while a single shared Help control explains the active Authoring workspace.");
            // This real click hides the originating WebView as soon as native selects the
            // file. Observe the native result first; a hidden WebView may defer its JS reply.
            var fileSelectionScript = core.ExecuteScriptAsync("document.querySelector('[data-workspace-select=" + JsonSerializer.Serialize(file.ContentId) + "]').click()");
            await WaitAsync(() => model.ActiveAuthoringContentId == file.ContentId && Descendants<TabControl>(view).Any(tabs => tabs.SelectedItem is TabItem { Tag: string id } && id == file.ContentId),
                "Selecting the mirrored file tab did not activate its real native document pane.", 60);
            Check(receipt, Descendants<TabControl>(view).Any(tabs => tabs.SelectedItem is TabItem { Tag: string id } && id == file.ContentId
                && tabs.Items.OfType<TabItem>().Any(tab => Equals(tab.Tag, "canvas"))), "The native header remains available while a source file is active.");
            VerifyNativeFileHelp(view, file.ContentId, receipt);
            var documentPath = VisualPageProjectService.ResolveProjectPath(project, file.RelativePath);
            file.Content += "\n/* Native buffer survives switching the workspace chrome. */\n";
            var draft = file.Content;
            SelectNativeAuthoring(view);
            await WaitAsync(async () => writer.IsWorkspaceChromeReady && await TrueAsync(core, "window.__studioAuthoring.state().currentGenerationReady && !document.querySelector('#studio-workspace-tabs').hidden"),
                "Returning through the native Authoring tab did not restore its embedded tab bar.", 120);
            await fileSelectionScript.WaitAsync(TimeSpan.FromSeconds(10));
            Check(receipt, ReferenceEquals(writer, typeof(VisualPageStudioView).GetField("_studioAuthoring", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view))
                && file.IsDirty && file.Content == draft && !File.ReadAllText(documentPath).Contains("Native buffer survives", StringComparison.Ordinal),
                "File selection and native Authoring fallback retain the same content writer and independent unsaved source buffer.");
            await core.ExecuteScriptAsync("document.querySelector('[data-workspace-close=" + JsonSerializer.Serialize(file.ContentId) + "]').click()");
            await WaitAsync(async () => !Panes(model.Layout.Root).Any(pane => pane.PaneContents.Contains(file.ContentId))
                && await TrueAsync(core, "!document.querySelector('[data-workspace-select=" + JsonSerializer.Serialize(file.ContentId) + "]')"), "Closing the mirrored file tab did not update its actual native pane.", 60);
            Check(receipt, file.IsDirty && file.Content == draft, "Closing a mirrored source tab preserves its unsaved native file buffer for reopening.");
            await model.OpenProjectNodeAsync(new HyperpageProjectTreeNodeViewModel { Name = file.FileName, RelativePath = file.RelativePath, IsDirectory = false });
            Check(receipt, model.TryGetProjectDocument(file.ContentId, out var reopened) && ReferenceEquals(file, reopened) && reopened!.Content == draft,
                "Reopening the closed project file restores the same unsaved native document object.");
            Check(receipt, DockFileWithAuthoring(model, file.ContentId), "The reopened native document can return beside Authoring in its actual pane.");
            SelectNativeAuthoring(view);
            await WaitAsync(async () => writer.IsWorkspaceChromeReady && await TrueAsync(core, "window.__studioAuthoring.state().currentGenerationReady"), "Reopened file did not return to the authoring workspace.", 120);
            await core.ExecuteScriptAsync("document.querySelector('[data-workspace-action=\"save\"]').click()");
            await WaitAsync(() => !file.IsDirty && File.ReadAllText(documentPath) == draft, "The web Save action did not use the actual native project and source save route.", 90);
            Check(receipt, receipt.Actions.Any(action => action.Action == "select" && action.ContentId == file.ContentId)
                && receipt.Actions.Any(action => action.Action == "close" && action.ContentId == file.ContentId) && receipt.Actions.Any(action => action.Action == "save"),
                "Mirrored select, close, and Save actions cross the real trusted WebView-to-native command bridge.");
            Check(receipt, await TrueAsync(core, "(()=>{const help=document.querySelector('[data-workspace-help]');return !!help && !!(help.title || help.getAttribute('aria-label'));})()"),
                "The native-provided tab help remains named and discoverable without opening a modal during isolated QA.");
            VerifyAnchorMapping(view, receipt);

            foreach (var width in new[] { 960, 640, 390, 1500 }) await InspectAsync(view, writer, core, width, 900, 1, fixture, receipt);
            foreach (var zoom in new[] { .8, 1.25, 1.0 }) await InspectAsync(view, writer, core, 1500, 1000, zoom, fixture, receipt);
            Check(receipt, !receipt.Messages.Any(message => message.Contains("\"type\":\"studio:error\"", StringComparison.Ordinal)),
                "The full native chrome, file actions, resize, and zoom sequence produces no authoring errors.");
            Safety(host.Handle, receipt, "complete");
        }
        catch (Exception failure)
        {
            receipt.Failure = failure.ToString();
            receipt.CatalogDiagnostics = CatalogDiagnostics(view, model);
            receipt.ActiveNativeContentId = model.ActiveAuthoringContentId;
            receipt.NativeDiagnostics = NativeDiagnostics(view, CachedWriter(view) ?? writer);
            receipt.PanelDiagnostics = PanelDiagnostics(view, CachedWriter(view) ?? writer);
            // Persist the failure before touching a browser which may currently be detached
            // behind a native source tab. Diagnostic capture must never hide the real error.
            await File.WriteAllTextAsync(Path.Combine(fixture, "studio-workspace-chrome-native-verification.json"),
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("STUDIO_WORKSPACE_CHROME_FAILURE|" + failure.Message);
            try
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                await using var nativeImage = File.Create(Path.Combine(fixture, "studio-workspace-chrome-native-failure.png")); encoder.Save(nativeImage);
            }
            catch (Exception error) { receipt.DiagnosticError = error.ToString(); }
            if (core is not null)
            {
                try
                {
                    receipt.Diagnostics = await core.ExecuteScriptAsync("({state:window.__studioAuthoring?.state(),text:document.body.innerText.slice(0,16000),width:innerWidth,height:innerHeight})")
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    await using var image = File.Create(Path.Combine(fixture, "studio-workspace-chrome-failure.png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception error) { receipt.DiagnosticError = error.ToString(); }
            }
            throw;
        }
        finally { host.RootVisual = null; }
    }

    private static async Task VerifyPrimaryFlyoutsAsync(VisualPageStudioView view, VisualPageStudioTabViewModel model,
        StudioHyperlibAuthoringView writer, CoreWebView2 core, Receipt receipt)
    {
        Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|primary-flyouts");
        await ChoosePanelAsync(core, "left", "contents");
        await ChoosePanelAsync(core, "right", "templates");
        Layout(view, 1500, 1000);
        Check(receipt, await TrueAsync(core, "(()=>{const l=document.querySelector('#studio-left-panel-selector'),r=document.querySelector('#studio-right-panel-selector');const ids=s=>[...s.options].map(o=>o.value);return !!l&&!!r&&['contents','properties','appearance','code','actions','native:project','native:page'].every(id=>ids(l).includes(id))&&['templates','native:elements'].every(id=>ids(r).includes(id))&&!ids(l).includes('native:elements')&&!ids(l).includes('native:templates')&&!ids(l).includes('native:styles')&&!ids(l).includes('native:events')&&!ids(r).includes('elements')&&!ids(r).includes('properties')&&ids(r).includes('native:language-builder');})()"),
            "The primary selectors expose one native Elements library and current Templates on the right, and Work contents, Page, Actions, Appearance and actual Properties on the left without obsolete duplicates.");
        VerifyUnavailablePanelFallback(view, receipt);
        Check(receipt, await TrueAsync(core, "(()=>{const visible=e=>!!e&&!e.hidden&&getComputedStyle(e).display!=='none'&&e.getClientRects().length;return [...document.querySelectorAll('#navigation .studio-left-tabs,#editor-panel .editor-tabs,#nav-toggle,#nav-expand,#studio-inspector-toggle')].every(e=>!visible(e))&&document.querySelector('#navigation').contains(document.querySelector('#properties-content'))&&!document.querySelector('#editor-panel').contains(document.querySelector('#properties-content'));})()"),
            "The configured workspace has no duplicate embedded tab bars or legacy reopen rails, and the actual property editor lives only in the left panel.");
        CheckNoDuplicatePanels(view, receipt, "both web tools selected");

        await core.ExecuteScriptAsync("(()=>{const scope=document.querySelector('#template-scope');scope.value='module';scope.dispatchEvent(new Event('change',{bubbles:true}));const search=document.querySelector('#library-search');search.value='Divider';search.dispatchEvent(new Event('input',{bubbles:true}));})()");
        await WaitAsync(async () => await TrueAsync(core, "document.querySelectorAll('[data-library-id]').length>0 && document.querySelector('#template-scope')?.value==='module' && document.querySelector('#library-search')?.value==='Divider'"),
            "The retained template library did not apply its existing scope and search controls in the primary left panel.", 30);
        await core.ExecuteScriptAsync("document.querySelector('[data-library-id]').click()");
        await WaitAsync(async () => await TrueAsync(core, "document.querySelector('#studio-library-preview')?.dataset.state==='ready' && document.querySelector('#studio-library-preview-frame')?.dataset.loaded==='true' && !!document.querySelector('#library-add') && !document.querySelector('#library-add').disabled"),
            "The retained library selection did not automatically render its actual module preview and Add action.", 240);
        await core.ExecuteScriptAsync("(()=>{const panel=document.querySelector('#panel-content');panel.scrollTop=48;window.__studioFlyoutQa={frame:document.querySelector('#studio-library-preview-frame'),src:document.querySelector('#studio-library-preview-frame').src,selected:document.querySelector('[data-library-id].selected')?.dataset.libraryId,scroll:panel.scrollTop,scope:document.querySelector('#template-scope').value,search:document.querySelector('#library-search').value};})()");
        Check(receipt, await TrueAsync(core, "!!window.__studioFlyoutQa.selected && document.querySelector('#library-add').textContent.trim()==='Add to Page' && !document.querySelector('#library-preview')"),
            "The primary Templates panel preserves the existing automatically previewed library cards and single Add to Page action.");

        await ChoosePanelAsync(core, "right", "native:language-builder");
        Layout(view, 1500, 1000);
        Check(receipt, NativePanelVisible(view, "right") && NativeSelected(view, "right") == "language-builder"
            && await TrueAsync(core, "getComputedStyle(document.querySelector('#editor-panel')).display==='none'"),
            "Choosing the native statement builder shows exactly the existing right native tool and hides the embedded right panel.");
        var panelGrid = (Grid)view.FindName("StudioPanelGrid");
        panelGrid.ColumnDefinitions[4].Width = new GridLength(385);
        Layout(view, 1500, 1000);
        Check(receipt, Math.Abs(((FrameworkElement)view.FindName("RightToolPanelHost")).ActualWidth - 385) < 2,
            "The original right native panel accepts the same resized width that its GridSplitter writes.");
        SelectNativePanel(view, "right", "authoring-templates");
        await WaitAsync(async () => await PanelSelectedAsync(core, "right", "templates"),
            "The native right tool dropdown did not return to the retained Templates panel.", 45);
        Layout(view, 1500, 1000);
        CheckNoDuplicatePanels(view, receipt, "native builder to Templates");
        await CheckRetainedLibraryAsync(core, receipt, "switching through the native right tool dropdown");

        await core.ExecuteScriptAsync("document.querySelector('#studio-right-panel-collapse').click()");
        await WaitAsync(async () => await TrueAsync(core, "getComputedStyle(document.querySelector('#editor-panel')).display==='none'"),
            "The subtle right boundary control did not collapse the web library.", 30);
        Layout(view, 1500, 1000);
        Check(receipt, !NativePanelVisible(view, "right"), "Collapsing a web tool leaves neither a duplicate native right column nor an empty reserved panel.");
        ClickReopen(view, "right");
        await WaitAsync(async () => await PanelSelectedAsync(core, "right", "templates"),
            "The traditional right edge reopen command did not restore the last web tool.", 45);
        await CheckRetainedLibraryAsync(core, receipt, "collapsing and reopening the right web library");

        await ChoosePanelAsync(core, "left", "properties");
        Check(receipt, await TrueAsync(core, "(()=>{const e=document.querySelector('#properties-content');return document.querySelector('#navigation').contains(e)&&!e.hidden&&getComputedStyle(e).display!=='none'&&document.querySelector('#studio-left-panel-selector').value==='properties';})()"),
            "The left panel selector opens the real selected-object Properties editor without creating a second inspector.");
        await ChoosePanelAsync(core, "left", "native:project");
        Layout(view, 1500, 1000);
        Check(receipt, NativePanelVisible(view, "left") && NativeSelected(view, "left") == "project"
            && await TrueAsync(core, "getComputedStyle(document.querySelector('#navigation')).display==='none'"),
            "Choosing Project shows only the original native project-file panel on the left.");
        var leftHide = (Button)view.FindName("StudioLeftPanelHideButton");
        leftHide.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(view, 1500, 1000);
        await WaitAsync(() => !NativePanelVisible(view, "left"), "The original left native boundary command did not collapse its current tool.", 20);
        ClickReopen(view, "left");
        Layout(view, 1500, 1000);
        await WaitAsync(() => NativePanelVisible(view, "left") && NativeSelected(view, "left") == "project",
            "The traditional left edge command did not restore its last native tool.", 20);
        Check(receipt, await TrueAsync(core, "getComputedStyle(document.querySelector('#navigation')).display==='none'"),
            "A native left tool reopens without exposing a second embedded Contents panel.");
        SelectNativePanel(view, "left", "authoring-contents");
        await WaitAsync(async () => await PanelSelectedAsync(core, "left", "contents"),
            "The native left selector did not restore Work contents.", 45);
        await core.ExecuteScriptAsync("document.querySelector('#studio-left-panel-collapse').click()");
        await WaitAsync(async () => await TrueAsync(core, "getComputedStyle(document.querySelector('#navigation')).display==='none'"),
            "The subtle left boundary command did not collapse Work contents.", 20);
        ClickReopen(view, "left");
        await WaitAsync(async () => await PanelSelectedAsync(core, "left", "contents"),
            "The traditional left edge command did not restore Work contents.", 45);

        await ChoosePanelAsync(core, "right", "native:language-builder");
        Layout(view, 1500, 1000);
        Check(receipt, Math.Abs(((FrameworkElement)view.FindName("RightToolPanelHost")).ActualWidth - 385) < 2,
            "The resized native right-panel width survives switching to retained web tools and back.");
        var rightHide = (Button)view.FindName("StudioRightPanelHideButton");
        rightHide.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(view, 1500, 1000);
        await WaitAsync(() => !NativePanelVisible(view, "right"), "The original right native boundary command did not collapse its current tool.", 20);
        ClickReopen(view, "right");
        Layout(view, 1500, 1000);
        await WaitAsync(() => NativePanelVisible(view, "right") && NativeSelected(view, "right") == "language-builder",
            "The traditional right edge command did not restore its last native tool.", 20);
        Check(receipt, await TrueAsync(core, "getComputedStyle(document.querySelector('#editor-panel')).display==='none'"),
            "A native right tool reopens without exposing a second embedded Templates panel.");
        SelectNativePanel(view, "right", "authoring-templates");
        await WaitAsync(async () => await PanelSelectedAsync(core, "right", "templates"),
            "The final native-to-Templates transition did not restore the primary web library.", 45);
        Layout(view, 1500, 1000);
        CheckNoDuplicatePanels(view, receipt, "completed native and web collapse/reopen round trips");
        receipt.PanelDiagnostics = PanelDiagnostics(view, writer);
        await WaitAsync(() => writer.IsWorkspaceChromeReady,
            "The completed primary-panel transitions did not acknowledge their final native-to-web chrome snapshot.", 15);
        Check(receipt, writer.IsWorkspaceChromeReady,
            "All primary panel transitions retain their acknowledged native-to-web chrome handshake.");
        receipt.PanelDiagnostics = PanelDiagnostics(view, writer);
        var openPopups = Descendants<Popup>(view).Where(popup => popup.IsOpen).ToArray();
        Check(receipt, openPopups.Length == 0,
            "Edge-popup HWND visibility remains suppressed in this hidden fixture. Open popups: "
                + string.Join(", ", openPopups.Select(popup => popup.Name + " child=" + popup.Child?.GetType().Name + " visible=" + popup.IsVisible)));
        await core.ExecuteScriptAsync("document.querySelector('#library-add').click()");
        await WaitAsync(async () => await TrueAsync(core, "(()=>{const s=window.__studioAuthoring.state(),p=document.querySelector('#properties-content');return !s.libraryBusy&&!s.libraryMutationPending&&s.currentGenerationReady&&s.workspaceLeftPanel?.selectedId==='properties'&&s.workspaceLeftPanel.isOpen&&!p.hidden&&!!p.querySelector('.block-name')&&document.querySelector('#studio-library-preview').hidden&&!document.querySelector('#page-view').inert;})()"),
            "Placing the selected module did not restore the editable Page and open its actual object inspector in the primary left panel.", 240);
        Check(receipt, await TrueAsync(core, "(()=>{const p=document.querySelector('#properties-content');return document.querySelector('#navigation').contains(p)&&!!p.querySelector('[data-field]')&&!document.querySelector('#editor-panel').contains(p)&&document.querySelector('#studio-left-panel-selector').value==='properties';})()"),
            "The real Add to Page route places the selected library module and exposes its editable fields in the single actual left Properties panel.");
        CheckNoDuplicatePanels(view, receipt, "placing a module and selecting its actual left inspector");

        await VerifyNativeCatalogAsync(view, model, writer, core, receipt);
        await VerifyAppearanceSelectionAsync(view, model, writer, core, receipt);
        await VerifyNestedCanvasAsync(view, model, writer, core, receipt);
        await ChoosePanelAsync(core, "left", "contents");
        await ChoosePanelAsync(core, "right", "templates");
    }

    private static async Task VerifyAppearanceSelectionAsync(VisualPageStudioView view, VisualPageStudioTabViewModel model,
        StudioHyperlibAuthoringView writer, CoreWebView2 core, Receipt receipt)
    {
        Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|appearance-selection");
        var library = Path.GetFullPath(Path.Combine(receipt.Project, "../.."));
        var store = new ContentStore(library);
        var content = await store.ReadAsync();
        var pageId = writer.CurrentNodeId ?? throw new InvalidOperationException("The Appearance fixture has no active Page.");
        var definition = TemplateObjectLibrary.ReadDefinition(receipt.Project);
        var candidates = content.Pages[pageId].Blocks.Where(block => definition.Blocks.Any(schema => schema.Id == block.Type
            && schema.Fields.Any(field => field.Id == "color" && field.Type == "color"))).ToArray();
        Check(receipt, candidates.Length >= 2,
            "The real native catalogue fixture contains two independently selectable saved objects with template-defined appearance fields.");
        var target = candidates[^1];
        var other = candidates.First(block => block.Id != target.Id);
        var legacyRevision = model.Revision;
        var beforeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(library, "Content", "library.json"))));
        await ChoosePanelAsync(core, "right", "templates");
        await ChoosePanelAsync(core, "left", "appearance");
        Layout(view, 1500, 1000);
        await WaitAsync(async () => await TrueAsync(core, "window.__studioAuthoring.state().currentGenerationReady && Math.abs(innerWidth-"
            + Browser(writer).ActualWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")<=3"),
            "The Appearance workspace did not settle before selection acceptance.", 30);
        Check(receipt, !NativePanelVisible(view, "left") && NativeSelected(view, "left") == "styles",
            "Authoring Appearance uses the actual Page's editor through the original native panel choice, without exposing the unrelated legacy CSS inspector.");
        await core.ExecuteScriptAsync("(()=>{const f=document.querySelector('#page-frame');if(!f?.contentDocument||!f.__studioCanvas)throw new Error('The real Page canvas is not ready');const q=window.__studioAppearanceQa={frame:f,document:f.contentDocument,controller:f.__studioCanvas,src:f.src,width:f.getBoundingClientRect().width,loads:0};f.addEventListener('load',()=>q.loads++);})()");
        var actionsBeforeSelection = receipt.Actions.Count;

        async Task ClickSavedObject(string id)
        {
            await core.ExecuteScriptAsync("(()=>{const f=document.querySelector('#page-frame'),e=[...f.contentDocument.querySelectorAll('[data-block-id]')].find(e=>e.dataset.blockId==="
                + JsonSerializer.Serialize(id) + ");if(!e)throw new Error('The saved object is absent from the real Page renderer');e.dispatchEvent(new f.contentWindow.MouseEvent('click',{bubbles:true,cancelable:true,view:f.contentWindow}));})()");
            await WaitAsync(async () => await TrueAsync(core, "(()=>{const id=" + JsonSerializer.Serialize(id)
                + ",p=document.querySelector('#properties-content'),s=window.__studioAuthoring.state(),f=document.querySelector('#page-frame');return s.workspaceLeftPanel?.selectedId==='appearance'&&s.workspaceLeftPanel.isOpen&&!p.hidden&&p.dataset.panel==='appearance'&&p.dataset.appearanceBlock===id&&[...f.contentDocument.querySelectorAll('[data-block-id]')].some(e=>e.dataset.blockId===id&&e.getAttribute('data-selected')==='true');})()"),
                "Selecting a saved object did not retain Appearance and update it to the actual selected Page object.", 20);
            // Native panel/chrome messages complete asynchronously. Let the real bridge finish
            // before asserting that it did not resize or replace the selected Page's canvas.
            await Task.Delay(180);
        }

        foreach (var selectedId in new[] { other.Id, target.Id })
        {
            await ClickSavedObject(selectedId);
            Check(receipt, await TrueAsync(core, "(()=>{const q=window.__studioAppearanceQa,f=document.querySelector('#page-frame'),s=window.__studioAuthoring.state();return s.workspaceLeftPanel.selectedId==='appearance'&&s.workspaceLeftPanel.isOpen&&f===q.frame&&f.contentDocument===q.document&&f.__studioCanvas===q.controller&&f.src===q.src&&q.loads===0&&Math.abs(f.getBoundingClientRect().width-q.width)<=1&&!f.inert&&s.currentGenerationReady&&!s.dirty;})()"),
                "Selecting saved object " + selectedId + " while Appearance is open keeps the same iframe, document, editing controller, source and canvas width without navigation, flicker-inducing panel swaps or a content mutation.");
        }
        Check(receipt, !receipt.Actions.Skip(actionsBeforeSelection).Any(action => action.Action == "panel-select")
            && beforeHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(library, "Content", "library.json"))))
            && model.Revision == legacyRevision,
            "Ordinary canvas selections neither request another native panel nor modify the saved Work or the unrelated legacy visual document.");
        Check(receipt, await TrueAsync(core, "(()=>{const p=document.querySelector('#properties-content');return !!p.querySelector('[data-field=\"color\"][type=\"color\"]')&&!p.querySelector('[data-field=\"text\"],textarea,[data-upload-field]');})()"),
            "Appearance exposes the selected object's real color picker and filters content and media editing out of that tool.");
        const string color = "#1b6f93";
        await core.ExecuteScriptAsync("(()=>{const e=document.querySelector('#properties-content [data-field=\"color\"][type=\"color\"]');if(!e)throw new Error('The selected object has no actual color picker');e.value='"
            + color + "';e.dispatchEvent(new Event('input',{bubbles:true}));})()");
        await WaitAsync(async () =>
        {
            var saved = await store.ReadAsync();
            return saved.Pages[pageId].Blocks.Single(block => block.Id == target.Id).Values.TryGetValue("color", out var value)
                && value.GetString() == color && await TrueAsync(core, "(()=>{const s=window.__studioAuthoring.state(),f=document.querySelector('#page-frame'),e=[...f.contentDocument.querySelectorAll('[data-block-id]')].find(e=>e.dataset.blockId==="
                + JsonSerializer.Serialize(target.Id) + ");return !s.dirty&&s.currentGenerationReady&&s.workspaceLeftPanel.selectedId==='appearance'&&!!e&&f.contentWindow.getComputedStyle(e).color==='rgb(27, 111, 147)';})()");
        }, "The actual Appearance color picker did not save into the selected Page block and update its rendered color.", 60);
        Check(receipt, model.Revision == legacyRevision && await TrueAsync(core, "document.querySelector('#properties-content [data-field=\"color\"]').value==='" + color + "'"),
            "Editing the actual color picker persists the selected Hyperlib object's appearance and leaves the legacy source document untouched.");

        var reloaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) => reloaded.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += NavigationCompleted;
        try { core.Reload(); Check(receipt, await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(45)), "A fresh actual authoring runtime reloads the saved Page successfully."); }
        finally { core.NavigationCompleted -= NavigationCompleted; }
        await WaitAsync(async () => writer.IsWorkspaceChromeReady && await TrueAsync(core, "window.__studioAuthoring?.state()?.currentGenerationReady===true && window.__studioAuthoring.state().workspaceLeftPanel?.selectedId==='appearance'"),
            "The freshly loaded native authoring runtime did not restore the chosen Appearance tool.", 120);
        await ClickSavedObject(target.Id);
        Check(receipt, await TrueAsync(core, "(()=>{const f=document.querySelector('#page-frame'),e=[...f.contentDocument.querySelectorAll('[data-block-id]')].find(e=>e.dataset.blockId==="
            + JsonSerializer.Serialize(target.Id) + ");return document.querySelector('#properties-content [data-field=\"color\"]').value==='" + color
            + "'&&f.contentWindow.getComputedStyle(e).color==='rgb(27, 111, 147)';})()"),
            "Reloading and reselecting the saved Page object restores both its actual Appearance field and persisted rendered color.");

        foreach (var width in new[] { 1500, 640 })
        {
            Layout(view, width, 900);
            await ChoosePanelAsync(core, "left", "appearance");
            await WaitAsync(async () => await TrueAsync(core, "Math.abs(innerWidth-" + Browser(writer).ActualWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ")<=3 && window.__studioAuthoring.state().currentGenerationReady"), "The Appearance tool did not settle at its actual native width.", 30);
            await core.ExecuteScriptAsync("document.querySelector('#studio-left-panel-collapse').click()");
            await WaitAsync(async () => await TrueAsync(core, "getComputedStyle(document.querySelector('#navigation')).display==='none'"),
                "The Appearance boundary control did not collapse its real panel.", 20);
            ClickReopen(view, "left");
            await WaitAsync(async () => await PanelSelectedAsync(core, "left", "appearance"),
                "The traditional left edge control did not reopen the selected Appearance tool.", 20);
            Check(receipt, !NativePanelVisible(view, "left") && await TrueAsync(core, "(()=>{const e=document.querySelector('#properties-content [data-field=\"color\"]'),p=document.querySelector('#properties-content'),s=window.__studioAuthoring.state();return s.workspaceLeftPanel.selectedId==='appearance'&&s.workspaceLeftPanel.isOpen&&!p.hidden&&e?.value==='"
                + color + "'&&p.dataset.panel==='appearance'&&p.dataset.appearanceBlock==='" + target.Id + "'&&document.documentElement.scrollWidth<=innerWidth+2;})()"),
                "At " + width + "px, Appearance collapse/reopen retains the selected saved object and color without a duplicate native inspector or horizontal clipping.");
            var path = Path.Combine(receipt.Fixture, "studio-appearance-selection-" + width + ".png");
            await using (var image = File.Create(path)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image).WaitAsync(TimeSpan.FromSeconds(15));
            receipt.Renders.Add(path);
        }
        Layout(view, 1500, 1000);
        await ChoosePanelAsync(core, "left", "properties");
        Check(receipt, await TrueAsync(core, "(()=>{const p=document.querySelector('#properties-content');return !p.hidden&&!!p.querySelector('.block-name')&&!!p.querySelector('[data-field]')&&document.querySelector('#studio-left-panel-selector').value==='properties';})()"),
            "Explicitly selecting Properties still opens the selected object's full editor after Appearance selection, editing, reload and collapse/reopen.");
    }

    private static async Task CheckRetainedLibraryAsync(CoreWebView2 core, Receipt receipt, string stage)
    {
        Check(receipt, await TrueAsync(core, "(()=>{const p=window.__studioFlyoutQa,f=document.querySelector('#studio-library-preview-frame');return !!p&&f===p.frame&&f.src===p.src&&document.querySelector('#studio-library-preview').dataset.state==='ready'&&document.querySelector('[data-library-id].selected')?.dataset.libraryId===p.selected&&document.querySelector('#template-scope')?.value===p.scope&&document.querySelector('#library-search')?.value===p.search&&Math.abs(document.querySelector('#panel-content').scrollTop-p.scroll)<=2&&!!document.querySelector('#library-add')&&!document.querySelector('#library-add').disabled;})()"),
            "The same preview iframe, chosen object, search, scope, scroll and Add action survive " + stage + ".");
    }

    private static async Task VerifyNativeCatalogAsync(VisualPageStudioView view, VisualPageStudioTabViewModel model,
        StudioHyperlibAuthoringView writer, CoreWebView2 core, Receipt receipt)
    {
        var list = (ListBox)view.FindName("CatalogElementList");
        var add = (Button)view.FindName("CatalogElementCommitButton");
        var panel = (Grid)view.FindName("CatalogElementPanel");
        var search = Descendants<TextBox>(panel).Single(control => AutomationProperties.GetName(control) == "Search every Studio element");
        var scopeSelector = Descendants<ComboBox>(panel).Single(control => ReferenceEquals(control.ItemsSource, model.ElementScopes));
        var category = Descendants<ComboBox>(panel).Single(control => ReferenceEquals(control.ItemsSource, model.ElementCategories));
        var library = Path.GetFullPath(Path.Combine(receipt.Project, "../.."));
        var index = Path.Combine(library, "Content", "library.json");
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var contentHash = Hash(index);
        var visualRevision = model.Revision;
        var store = new ContentStore(library);
        var before = await store.ReadAsync();
        var pageId = writer.CurrentNodeId ?? throw new InvalidOperationException("The native catalog fixture has no active Page.");
        var existing = before.Pages[pageId].Blocks.Select(block => block.Id).ToArray();
        await ChoosePanelAsync(core, "right", "native:elements");
        Layout(view, 1500, 1000);
        Check(receipt, NativePanelVisible(view, "right") && NativeSelected(view, "right") == "elements"
            && !NativePanelVisible(view, "left") && ReferenceEquals(list.Parent, panel),
            "The single right Elements option displays the original native list, while the actual selected Page inspector remains on the left.");
        Check(receipt, model.ElementScopes.Contains("Common") && model.ElementScopes.Contains("All HTML") && model.ElementScopes.Contains("Blazor & logic")
            && add.Content?.ToString() == "Add to Page" && add.Command == model.AddElementCommand,
            "The moved native catalogue retains Common, all HTML and Razor/Blazor scope choices and its actual Add to Page command.");

        void Select(string scope, string markup)
        {
            search.SetCurrentValue(TextBox.TextProperty, "");
            scopeSelector.SetCurrentValue(Selector.SelectedItemProperty, scope);
            category.SetCurrentValue(Selector.SelectedItemProperty, "All categories");
            view.UpdateLayout();
            var descriptor = model.FilteredElementDescriptors.First(item => item.MarkupName == markup);
            list.SetCurrentValue(Selector.SelectedItemProperty, null);
            list.SetCurrentValue(Selector.SelectedItemProperty, descriptor);
            view.UpdateLayout();
            if (model.SelectedDescriptor?.Id != descriptor.Id) throw new InvalidOperationException("The actual native list did not select its bound descriptor.");
        }
        async Task PreviewReady(string stage)
        {
            var id = "catalog:" + model.SelectedDescriptor!.Id;
            Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|catalog-preview-start|" + stage + "|" + id);
            await File.WriteAllTextAsync(Path.Combine(receipt.Fixture, "studio-workspace-progress.json"),
                JsonSerializer.Serialize(new { stage, id, status = "preparing", utc = DateTime.UtcNow }));
            await WaitAsync(async () =>
            {
                var rendererFailure = receipt.StatusMessages.LastOrDefault(message =>
                    message.Contains("could not compile for preview", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("directory name is invalid", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("error occurred trying to start process", StringComparison.OrdinalIgnoreCase));
                if (rendererFailure is not null)
                    throw new InvalidOperationException("The actual " + stage + " preview renderer reported its failure: " + rendererFailure);
                return await TrueAsync(core, "(()=>{const s=window.__studioAuthoring.state(),p=document.querySelector('#studio-library-preview');return s.catalogSelectedItemId==="
                + JsonSerializer.Serialize(id) + "&&s.libraryPreviewItemId===" + JsonSerializer.Serialize(id)
                + "&&p?.dataset.state==='ready'&&document.querySelector('#studio-library-preview-frame')?.dataset.loaded==='true';})()");
            },
                "The actual " + stage + " native selection did not automatically load its readonly live preview.", 240);
            Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|catalog-preview-ready|" + stage + "|" + id);
            await File.WriteAllTextAsync(Path.Combine(receipt.Fixture, "studio-workspace-progress.json"),
                JsonSerializer.Serialize(new { stage, id, status = "ready", utc = DateTime.UtcNow }));
            Check(receipt, Hash(index) == contentHash && model.Revision == visualRevision && !model.IsCatalogElementDraftActive,
                "Selecting and previewing " + stage + " changes neither the real ContentStore nor the legacy visual Page or inspector draft.");
        }

        Select("Common", "button");
        await PreviewReady("Common button");
        var commonId = model.SelectedDescriptor!.Id;
        var desiredCategory = model.SelectedDescriptor.Category;
        // Exercise the real bindings in the order a person uses them: search first,
        // then choose an available category from the newly populated dropdown.
        search.SetCurrentValue(TextBox.TextProperty, "button");
        view.UpdateLayout();
        var availableCategory = category.Items.OfType<string>().Single(item => item == desiredCategory);
        category.SetCurrentValue(Selector.SelectedItemProperty, availableCategory);
        view.UpdateLayout();
        await WaitAsync(() => model.ElementSearchText == "button" && model.SelectedElementCategory == availableCategory,
            "The actual native Search and Category controls did not update their existing bindings.", 15);
        search.SetCurrentValue(TextBox.TextProperty, "button ");
        view.UpdateLayout();
        await WaitAsync(() => model.ElementSearchText == "button " && model.SelectedElementCategory == availableCategory,
            "Changing the actual native search reset its selected Category.", 15);
        Check(receipt, model.ElementCategories.Distinct(StringComparer.Ordinal).Count() == model.ElementCategories.Count
            && model.FilteredElementDescriptors.All(item => item.Category == availableCategory),
            "Refreshing native search retains the chosen Category and never duplicates category entries through two-way binding reentrancy.");
        search.SetCurrentValue(TextBox.TextProperty, "button");
        view.UpdateLayout();
        Check(receipt, model.FilteredElementDescriptors.Count > 0
            && model.FilteredElementDescriptors.All(item => item.Category == availableCategory)
            && list.Items.OfType<VisualPageComponentDescriptor>().Any(item => item.Id == commonId),
            "The moved list keeps its original native search and Category filtering rather than introducing a second element library. "
                + $"Scope={scopeSelector.SelectedItem}; category={category.SelectedItem}; search={search.Text}; selected={model.SelectedDescriptor?.Id}; count={model.FilteredElementDescriptors.Count}.");

        var hide = (Button)view.FindName("StudioRightPanelHideButton");
        hide.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(view, 1500, 1000);
        await WaitAsync(() => !NativePanelVisible(view, "right"), "The real right native Elements edge control did not collapse.", 20);
        ClickReopen(view, "right");
        Layout(view, 1500, 1000);
        await WaitAsync(() => NativePanelVisible(view, "right") && NativeSelected(view, "right") == "elements", "The real edge control did not reopen the same native Elements list.", 20);
        Check(receipt, model.SelectedElementScope == "Common" && model.ElementSearchText == "button"
            && model.SelectedDescriptor?.Id == commonId && model.SelectedElementCategory != "All categories",
            "Collapsing and reopening the single Elements panel retains its scope, category, search and selected native descriptor.");

        Select("All HTML", "p");
        await PreviewReady("HTML paragraph");
        var htmlId = model.SelectedDescriptor!.Id;
        await core.ExecuteScriptAsync("document.querySelector('#studio-library-preview-close').click()");
        await WaitAsync(async () => await TrueAsync(core, "document.querySelector('#studio-library-preview').hidden"), "The native element preview could not be dismissed.", 20);
        Check(receipt, Hash(index) == contentHash && model.SelectedDescriptor?.Id == htmlId,
            "Closing an uncommitted native HTML preview preserves the Page byte for byte and retains the list selection.");

        Select("Blazor & logic", "InputCheckbox");
        await PreviewReady("Razor Checkbox");
        Select("Common", "button");
        Select("All HTML", "p");
        await PreviewReady("latest HTML paragraph after rapid selection");
        await Task.Delay(250);
        Check(receipt, await TrueAsync(core, "window.__studioAuthoring.state().libraryPreviewItemId===" + JsonSerializer.Serialize("catalog:" + model.SelectedDescriptor!.Id)),
            "The final native selection owns the main preview even after rapid Common and HTML changes.");
        await CaptureNativeCatalogProofAsync(view, writer, core, receipt, 1500, 1000);
        await CaptureNativeCatalogProofAsync(view, writer, core, receipt, 800, 900);
        Layout(view, 1500, 1000);
        await WaitAsync(async () => await TrueAsync(core, "Math.abs(innerWidth-" + Browser(writer).ActualWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ")<=3 && window.__studioAuthoring.state().currentGenerationReady"), "The native catalogue did not restore its wide workspace after the compact visual proof.", 30);

        var selectedId = model.SelectedDescriptor!.Id;
        Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|catalog-commit-start|" + selectedId);
        Check(receipt, add.IsEnabled && add.Command?.CanExecute(add.CommandParameter) == true,
            "The successfully previewed native element exposes its enabled real Add to Page control.");
        add.Command!.Execute(add.CommandParameter);
        await WaitAsync(() => !panel.IsEnabled, "The native element insertion did not protect its search and scope controls during the commit.", 15);
        await WaitAsync(async () => panel.IsEnabled && await TrueAsync(core, "(()=>{const s=window.__studioAuthoring.state(),p=document.querySelector('#properties-content');return !s.libraryBusy&&!s.libraryMutationPending&&s.currentGenerationReady&&s.workspaceLeftPanel?.selectedId==='properties'&&s.workspaceLeftPanel.isOpen&&!p.hidden&&!!p.querySelector('[data-field]')&&document.querySelector('#studio-library-preview').hidden&&!document.querySelector('#page-view').inert;})()"),
            "The actual native catalog Add command did not commit one element, restore the writer and open its real fields in left Properties.", 240);
        var after = await store.ReadAsync();
        Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|catalog-commit-complete|" + selectedId);
        var added = after.Pages[pageId].Blocks.Where(block => !existing.Contains(block.Id)).ToArray();
        ContentRules.Validate(after);
        Check(receipt, after.Pages[pageId].Blocks.Count == existing.Length + 1 && added.Length == 1
            && existing.All(id => after.Pages[pageId].Blocks.Any(block => block.Id == id)) && after.Revision > before.Revision,
            "The native catalogue Add command persists exactly one real Page block and preserves all previously placed template objects.");
        Check(receipt, await TrueAsync(core, "(()=>{const p=document.querySelector('#properties-content');return document.querySelectorAll('#properties-content').length===1&&document.querySelector('#navigation').contains(p)&&!document.querySelector('#editor-panel').contains(p)&&!!document.querySelector('#page-frame').contentDocument?.querySelector('[data-block-id=\""
                + added[0].Id + "\"]');})()") && model.SelectedDescriptor?.Id == selectedId,
            "The inserted native element renders on the actual current Page and uses the single left Properties editor while the right catalogue retains its selection.");
        Check(receipt, receipt.Messages.Any(message => message.Contains("catalog:" + selectedId, StringComparison.Ordinal)
            && message.Contains("studio:library-request", StringComparison.Ordinal))
            && receipt.Messages.Any(message => message.Contains("studio:catalog-commit-result", StringComparison.Ordinal)),
            "Native selection and Add travel through the real trusted preview, revision-checked library writer, and asynchronous completion bridge.");
        receipt.CatalogDiagnostics = CatalogDiagnostics(view, model);
    }

    private static async Task CaptureNativeCatalogProofAsync(VisualPageStudioView view, StudioHyperlibAuthoringView writer,
        CoreWebView2 core, Receipt receipt, int width, int height)
    {
        Layout(view, width, height);
        var browser = Browser(writer);
        await WaitAsync(async () => await TrueAsync(core, "Math.abs(innerWidth-" + (browser.ActualWidth / browser.ZoomFactor).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ")<=3 && document.querySelector('#studio-library-preview')?.dataset.state==='ready' && document.querySelector('#studio-library-preview-frame')?.dataset.loaded==='true'"),
            "The real browser and native catalogue did not reflow before their composite visual proof.", 30);
        view.UpdateLayout();
        var webBounds = Bounds(browser, view);
        var panelBounds = Bounds((FrameworkElement)view.FindName("RightToolPanelHost"), view);
        Check(receipt, NativePanelVisible(view, "right") && NativeSelected(view, "right") == "elements"
            && webBounds.Right <= panelBounds.Left + 2 && panelBounds.Right <= width + 2 && panelBounds.Top <= 2,
            $"At {width}px, the real moved native Elements list occupies only the right edge beside the real live preview without horizontal overlap.");
        using var stream = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream).WaitAsync(TimeSpan.FromSeconds(15));
        stream.Position = 0;
        var browserImage = new BitmapImage();
        browserImage.BeginInit(); browserImage.CacheOption = BitmapCacheOption.OnLoad; browserImage.StreamSource = stream; browserImage.EndInit(); browserImage.Freeze();
        // WPF cannot paint the WebView HWND into its visual bitmap. Composite its actual
        // CapturePreview image at the control's measured DIP bounds; every pixel comes
        // from the same current hidden native fixture and its real browser renderer.
        var nativeImage = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        nativeImage.Render(view);
        var combined = new DrawingVisual();
        using (var drawing = combined.RenderOpen())
        {
            drawing.DrawImage(nativeImage, new Rect(0, 0, width, height));
            drawing.DrawImage(browserImage, webBounds);
        }
        var composite = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        composite.Render(combined);
        var path = Path.Combine(receipt.Fixture, $"studio-native-elements-live-preview-{width}.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(composite));
        await using (var output = File.Create(path)) encoder.Save(output);
        receipt.Renders.Add(path);
        receipt.Samples.Add(new { nativeCatalogProof = path, width, height, browser = Rectangle(webBounds), nativeElements = Rectangle(panelBounds),
            browserSourceWidth = browserImage.PixelWidth, browserSourceHeight = browserImage.PixelHeight });
    }

    private static void VerifyUnavailablePanelFallback(VisualPageStudioView view, Receipt receipt)
    {
        var snapshot = typeof(VisualPageStudioView).GetMethod("GetAuthoringPanelSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The actual coordinated-panel snapshot provider is missing.");
        foreach (var left in new[] { true, false })
        {
            var field = typeof(VisualPageStudioView).GetField(left ? "_leftAuthoringPanel" : "_rightAuthoringPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var original = field.GetValue(view);
            try
            {
                // A previously selected native tool can disappear from these choices
                // after a real float/dock. Inject that stale selection without opening
                // an actual floating Window in this isolated hidden fixture.
                field.SetValue(view, "native:removed-fixture-tool");
                var current = (StudioWorkspacePanelSnapshot)snapshot.Invoke(view, new object[] { left })!;
                var expected = left ? "contents" : "templates";
                Check(receipt, current.SelectedId == expected && current.Options.Any(option => option.Id == expected),
                    "An unavailable " + (left ? "left" : "right") + " native-tool selection normalizes to the retained " + expected + " panel instead of emitting an invalid selector snapshot.");
            }
            finally { field.SetValue(view, original); }
        }
    }

    private static async Task ChoosePanelAsync(CoreWebView2 core, string side, string id)
    {
        await core.ExecuteScriptAsync("(()=>{const s=document.querySelector('#studio-" + side + "-panel-selector');if(!s||![...s.options].some(o=>o.value===" + JsonSerializer.Serialize(id) + "))throw new Error('Missing primary panel choice');s.value=" + JsonSerializer.Serialize(id) + ";s.dispatchEvent(new Event('change',{bubbles:true}));})()");
        await WaitAsync(async () => await PanelSelectedAsync(core, side, id), "The primary " + side + " selector did not select " + id + ".", 45);
    }
    private static Task<bool> PanelSelectedAsync(CoreWebView2 core, string side, string id)
    {
        var host = side == "left" ? "#navigation" : "#editor-panel";
        var native = id.StartsWith("native:", StringComparison.Ordinal);
        return TrueAsync(core, "(()=>{const s=document.querySelector('#studio-" + side + "-panel-selector'),h=document.querySelector('" + host + "'),p=window.__studioAuthoring.state().workspace" + (side == "left" ? "Left" : "Right") + "Panel;return p?.selectedId===" + JsonSerializer.Serialize(id) + " && p.isOpen===true && s?.value===" + JsonSerializer.Serialize(id)
            + " && (getComputedStyle(h).display" + (native ? "==='none'" : "!=='none'&&h.getBoundingClientRect().width>100") + ");})()");
    }
    private static bool NativePanelVisible(VisualPageStudioView view, string side)
    {
        var host = (FrameworkElement)view.FindName(side == "left" ? "LeftToolPanelHost" : "RightToolPanelHost");
        return host.Visibility == Visibility.Visible && host.ActualWidth > 100;
    }
    private static string? NativeSelected(VisualPageStudioView view, string side)
        => (((TabControl)view.FindName(side == "left" ? "LeftToolTabs" : "RightToolTabs")).SelectedItem as TabItem)?.Tag?.ToString();
    private static void SelectNativePanel(VisualPageStudioView view, string side, string tag)
    {
        var selector = (ComboBox)view.FindName(side == "left" ? "LeftToolSelector" : "RightToolSelector");
        selector.SetCurrentValue(Selector.SelectedItemProperty, selector.Items.OfType<TabItem>().Single(item => Equals(item.Tag, tag)));
        view.UpdateLayout();
    }
    private static void ClickReopen(VisualPageStudioView view, string side)
    {
        // The real Popup stays closed to avoid any desktop HWND. Its actual bound
        // button still exercises the same reopen command and remembered selection.
        var button = (Button)view.FindName(side == "left" ? "StudioLeftPanelReopenButton" : "StudioRightPanelReopenButton");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        view.UpdateLayout();
    }
    private static void CheckNoDuplicatePanels(VisualPageStudioView view, Receipt receipt, string stage)
    {
        Check(receipt, !NativePanelVisible(view, "left") && !NativePanelVisible(view, "right"),
            "Only the primary embedded left and right panels occupy workspace columns after " + stage + ".");
    }
    private static void VerifyNativeFileHelp(VisualPageStudioView view, string contentId, Receipt receipt)
    {
        var pane = Descendants<TabControl>(view).Single(tabs => tabs.SelectedItem is TabItem item && Equals(item.Tag, contentId));
        var helps = Descendants<Button>(pane).Where(button => AutomationProperties.GetName(button).StartsWith("Explain ", StringComparison.Ordinal)).ToArray();
        Check(receipt, helps.Length == 1 && helps[0].Focusable && KeyboardNavigation.GetIsTabStop(helps[0])
            && helps[0].ToolTip is not null && Equals(helps[0].Tag, contentId),
            "The native source-file fallback has one named keyboard-accessible Help control bound to its actual active file.");
        var bounds = Bounds(helps[0], pane);
        Check(receipt, bounds.Right <= pane.ActualWidth + 2 && bounds.Right >= pane.ActualWidth - 12 && bounds.Top >= 0 && bounds.Bottom <= 60,
            "The active native source file's single Help control aligns with the right edge of its workspace header.");
    }

    private static async Task InspectAsync(VisualPageStudioView view, StudioHyperlibAuthoringView writer, CoreWebView2 core,
        int width, int height, double zoom, string fixture, Receipt receipt)
    {
        Layout(view, width, height);
        var browser = Browser(writer); browser.ZoomFactor = zoom;
        await WaitAsync(async () => await TrueAsync(core, "Math.abs(innerWidth-" + (browser.ActualWidth / zoom).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")<=3 && window.__studioAuthoring.state().currentGenerationReady"),
            "The full native browser did not follow its actual DIP bounds and zoom.", 30);
        await ChoosePanelAsync(core, "left", "contents");
        await ChoosePanelAsync(core, "right", "templates");
        using var dom = JsonDocument.Parse(await core.ExecuteScriptAsync("(()=>{const rect=id=>{const e=document.querySelector(id),r=e.getBoundingClientRect();return{left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height,visible:!e.hidden&&getComputedStyle(e).display!=='none'};};const hit=value=>{const e=typeof value==='string'?document.querySelector(value):value;if(!e)return false;const r=e.getBoundingClientRect(),target=document.elementFromPoint(r.left+r.width/2,r.top+r.height/2);return r.width>0&&r.height>0&&(target===e||e.contains(target));};const help=document.querySelector('#studio-workspace-help');return{width:innerWidth,height:innerHeight,dpr:devicePixelRatio,overflow:document.documentElement.scrollWidth,main:rect('#main'),navigation:rect('#navigation'),editor:rect('#editor-panel'),files:rect('#studio-workspace-tabs'),tools:rect('#studio-workspace-tools'),libraryHeading:rect('#editor-panel .studio-panel-heading'),help:rect('#studio-workspace-help'),helpCount:document.querySelectorAll('[data-workspace-help]').length,helpActive:help.dataset.workspaceHelp===window.__studioAuthoring.state().workspaceActiveId,helpOutsideTab:!help.closest('.studio-workspace-tab'),helpKeyboard:help.tabIndex>=0&&!help.disabled,helpNamed:!!help.getAttribute('aria-label'),helpReceivesPointer:hit(help),rightSelectorReceivesPointer:hit('#studio-right-panel-selector'),rightCollapseReceivesPointer:hit('#studio-right-panel-collapse'),leftSelectorReceivesPointer:hit('#studio-left-panel-selector'),leftCollapseReceivesPointer:hit('#studio-left-panel-collapse'),actions:[...document.querySelectorAll('#studio-workspace-tools [data-workspace-action]')].map(e=>({id:e.dataset.workspaceAction,svg:!!e.querySelector('svg path,svg line,svg polyline,svg rect'),label:e.title||e.getAttribute('aria-label'),width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height,receivesPointer:hit(e)}))};})()"));
        var data = dom.RootElement;
        Rect pane = Bounds((FrameworkElement)view.FindName("PaneHost"), view), authoring = Bounds(writer, view), web = Bounds(browser, view);
        receipt.Samples.Add(new { width, height, zoom, pane = Rectangle(pane), authoring = Rectangle(authoring), browser = Rectangle(web), dom = data.Clone() });
        Check(receipt, Math.Abs(pane.Top) <= 1 && Math.Abs(authoring.Top) <= 1 && Math.Abs(web.Top) <= 1 && Math.Abs(web.Bottom - height) <= 2 && Math.Abs(web.Width - width) <= 2,
            $"At {width}px and {zoom:0.##} zoom, the actual native pane and WebView fill the workspace from its top without a standalone toolbar or file-header gap.");
        var main = data.GetProperty("main"); var files = data.GetProperty("files"); var tools = data.GetProperty("tools"); var editor = data.GetProperty("editor"); var heading = data.GetProperty("libraryHeading"); var help = data.GetProperty("help");
        Check(receipt, files.GetProperty("visible").GetBoolean() && Math.Abs(D(files,"left")-D(main,"left")) <= 2 && D(files,"top") <= 2 && D(files,"right") <= D(main,"right") + 2,
            $"At {width}px and {zoom:0.##} zoom, the Authoring/file tab bar begins at the actual canvas column's left edge and workspace top.");
        Check(receipt, tools.GetProperty("visible").GetBoolean() && D(tools,"left") >= D(editor,"left") - 2 && D(tools,"right") <= D(editor,"right") + 2
            && D(tools,"bottom") <= D(heading,"top") + 2 && D(tools,"height") >= 30,
            $"At {width}px and {zoom:0.##} zoom, all four workspace actions sit above the primary right-panel selector.");
        Check(receipt, data.GetProperty("helpCount").GetInt32() == 1 && data.GetProperty("helpActive").GetBoolean()
            && data.GetProperty("helpOutsideTab").GetBoolean() && data.GetProperty("helpKeyboard").GetBoolean()
            && data.GetProperty("helpNamed").GetBoolean() && data.GetProperty("helpReceivesPointer").GetBoolean()
            && Math.Abs(D(files,"right") - D(help,"right") - 6) <= 2 && D(help,"width") >= 24 && D(help,"height") >= 24,
            $"At {width}px and {zoom:0.##} zoom, exactly one active-document Help control is keyboard accessible and receives pointer hits at the central file bar's right edge.");
        var actions = data.GetProperty("actions").EnumerateArray().ToArray();
        Check(receipt, actions.Select(action=>action.GetProperty("id").GetString()).SequenceEqual(new[]{"layouts","authoring","publish","save"})
            && actions.All(action=>action.GetProperty("svg").GetBoolean() && !string.IsNullOrWhiteSpace(action.GetProperty("label").GetString()) && D(action,"width")>=24 && D(action,"height")>=24),
            $"At {width}px and {zoom:0.##} zoom, four distinct named native actions have real SVG line work and usable button bounds.");
        Check(receipt, data.GetProperty("rightSelectorReceivesPointer").GetBoolean() && data.GetProperty("rightCollapseReceivesPointer").GetBoolean()
            && actions.All(action => action.GetProperty("receivesPointer").GetBoolean()),
            $"At {width}px and {zoom:0.##} zoom, the right panel selector, all four workspace actions and its subtle collapse boundary receive actual pointer hits above the page status toolbar.");
        if (data.GetProperty("width").GetDouble() >= 800)
            Check(receipt, data.GetProperty("leftSelectorReceivesPointer").GetBoolean() && data.GetProperty("leftCollapseReceivesPointer").GetBoolean(),
                $"At {width}px and {zoom:0.##} zoom, the nonoverlapping left panel selector and collapse boundary receive actual pointer hits.");
        Check(receipt, data.GetProperty("overflow").GetDouble() <= data.GetProperty("width").GetDouble()+2,
            $"The complete native workspace has no horizontal clipping at {width}px and {zoom:0.##} zoom.");
        if (width > 960 && zoom <= 1)
            Check(receipt, Math.Abs(D(data.GetProperty("navigation"),"top")) <= 2, "The primary left Contents sidebar reaches the top of the full native workspace.");
        await core.ExecuteScriptAsync("(()=>{const s=window.__studioAuthoring.state(),panel=document.querySelector('#panel-content');window.__studioDrawerHitQa={left:s.workspaceLeftPanel.selectedId,right:s.workspaceRightPanel.selectedId,fields:JSON.stringify([...panel.querySelectorAll('input,select')].map(e=>[e.id,e.value])),scroll:panel.scrollTop,selected:document.querySelector('[data-library-id].selected')?.dataset.libraryId||null,frame:document.querySelector('#studio-library-preview-frame'),preview:s.libraryPreviewItemId};document.querySelector('#studio-right-panel-collapse').click();})()");
        await WaitAsync(async () => await TrueAsync(core, "getComputedStyle(document.querySelector('#editor-panel')).display==='none'"), "The primary right boundary control did not collapse its panel.", 15);
        if (data.GetProperty("width").GetDouble() < 800)
        {
            Check(receipt, await TrueAsync(core, "(()=>{const hit=id=>{const e=document.querySelector(id),r=e.getBoundingClientRect(),target=document.elementFromPoint(r.left+r.width/2,r.top+r.height/2);return r.width>0&&r.height>0&&(target===e||e.contains(target));};return hit('#studio-left-panel-selector')&&hit('#studio-left-panel-collapse');})()"),
                $"At {width}px and {zoom:0.##} zoom, collapsing the front right drawer exposes a pointer-accessible left selector and collapse boundary.");
            await core.ExecuteScriptAsync("document.querySelector('#studio-left-panel-collapse').click()");
            await WaitAsync(async () => await TrueAsync(core, "getComputedStyle(document.querySelector('#navigation')).display==='none'"),
                "The exposed narrow left drawer did not close through its own boundary.", 15);
            ClickReopen(view, "left");
            await WaitAsync(async () => await PanelSelectedAsync(core, "left", "contents"),
                "The traditional left edge command did not reopen the last tool after the narrow drawer hit test.", 15);
            Check(receipt, await TrueAsync(core, "(()=>{const e=document.querySelector('#studio-left-panel-selector'),r=e.getBoundingClientRect();return document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)===e;})()"),
                $"At {width}px and {zoom:0.##} zoom, the narrow left drawer reopens with its selected tool and receives pointer hits.");
        }
        ClickReopen(view, "right");
        await WaitAsync(async () => await PanelSelectedAsync(core, "right", "templates") && await TrueAsync(core, "getComputedStyle(document.querySelector('#studio-workspace-tools')).display!=='none'"), "The traditional right edge command did not reopen the library and its four actions together.", 15);
        Check(receipt, await TrueAsync(core, "(()=>{const q=window.__studioDrawerHitQa,s=window.__studioAuthoring.state(),panel=document.querySelector('#panel-content');return s.workspaceLeftPanel.selectedId===q.left&&s.workspaceRightPanel.selectedId===q.right&&JSON.stringify([...panel.querySelectorAll('input,select')].map(e=>[e.id,e.value]))===q.fields&&Math.abs(panel.scrollTop-q.scroll)<=2&&(document.querySelector('[data-library-id].selected')?.dataset.libraryId||null)===q.selected&&document.querySelector('#studio-library-preview-frame')===q.frame&&s.libraryPreviewItemId===q.preview;})()"),
            $"At {width}px and {zoom:0.##} zoom, native edge reopening retains both panel choices and the library filters, scroll, selection and preview identity.");
        Check(receipt, writer.IsWorkspaceChromeReady, "Panel collapse and reopen retain the native-to-web chrome acknowledgement.");
        var imagePath = Path.Combine(fixture, $"studio-workspace-chrome-{width}-{zoom:0.##}.png");
        await using (var image = File.Create(imagePath)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        receipt.Renders.Add(imagePath);
    }

    private static void VerifyAnchorMapping(VisualPageStudioView view, Receipt receipt)
    {
        var method = typeof(VisualPageStudioView).GetMethod("TranslateWorkspaceAnchor", BindingFlags.Static | BindingFlags.NonPublic);
        if (method is null) throw new InvalidOperationException("The actual native document-menu anchor conversion is missing.");
        var point = (Point)method.Invoke(null, new object[] { new StudioWorkspaceAnchor(100,20,40,30,1000,800), new Size(1250,1000) })!;
        Check(receipt, Math.Abs(point.X-125)<=.01 && Math.Abs(point.Y-62.5)<=.01,
            "The production document-menu anchor maps browser CSS coordinates to actual native DIP bounds at a different scale.");
    }
    private static bool DockFileWithAuthoring(VisualPageStudioTabViewModel model, string contentId)
    {
        var canvas = Panes(model.Layout.Root).Single(pane => pane.PaneContents.Contains("canvas"));
        var source = Panes(model.Layout.Root).Single(pane => pane.PaneContents.Contains(contentId));
        // Existing drag policy preserves at least one tab in its source pane. Open the normal
        // generated-source tab first, then use the same move and pane-close operations as the UI.
        if (source.NodeId != canvas.NodeId && source.PaneContents.Count == 1) model.RequestPaneContent("generated-razor");
        var moved = model.MovePaneContent(contentId, canvas.NodeId);
        if (source.NodeId != canvas.NodeId) model.ClosePane(source.NodeId);
        model.RequestPaneContent("canvas");
        return moved;
    }
    private static void SelectNativeAuthoring(VisualPageStudioView view)
    {
        // Layout rebuild replaces the TabControl. Materialize its template before
        // selecting the real native tab, exactly as a pointer user would encounter it.
        view.UpdateLayout();
        var pane = Descendants<TabControl>(view).Single(tabs => tabs.Items.OfType<TabItem>().Any(tab => Equals(tab.Tag, "canvas")));
        pane.SetCurrentValue(Selector.SelectedItemProperty, pane.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, "canvas")));
        view.UpdateLayout();
    }
    private static IEnumerable<StudioLayoutNode> Panes(StudioLayoutNode node)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane) { yield return node; yield break; }
        if (node.First is not null) foreach (var pane in Panes(node.First)) yield return pane;
        if (node.Second is not null) foreach (var pane in Panes(node.Second)) yield return pane;
    }
    private static double D(JsonElement value,string name)=>value.GetProperty(name).GetDouble();
    private static StudioHyperlibAuthoringView? CachedWriter(VisualPageStudioView view)=>typeof(VisualPageStudioView).GetField("_studioAuthoring",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(view) as StudioHyperlibAuthoringView;
    private static object NativeDiagnostics(VisualPageStudioView view,StudioHyperlibAuthoringView writer)=>new
    {
        view=new{view.IsLoaded,view.IsVisible,view.ActualWidth,view.ActualHeight,view.Visibility},
        writer=new{writer.IsLoaded,writer.IsVisible,writer.ActualWidth,writer.ActualHeight,writer.Visibility,parent=writer.Parent?.GetType().FullName,source=PresentationSource.FromVisual(writer)?.GetType().FullName},
        controls=Descendants<FrameworkElement>(view).Where(element=>element is ContentControl or TabControl || element.Name is "PaneHost" or "CentralWorkspaceHost" or "StudioPanelGrid")
            .Select(element=>new{type=element.GetType().Name,element.Name,element.IsLoaded,element.IsVisible,element.ActualWidth,element.ActualHeight,element.Visibility,
                tag=element.Tag?.ToString(),selected=element is TabControl tabs?(tabs.SelectedItem as TabItem)?.Tag?.ToString():null,
                content=element is ContentControl control?control.Content?.GetType().Name:null}).ToArray()
    };
    private static object CatalogDiagnostics(VisualPageStudioView view, VisualPageStudioTabViewModel model) => new
    {
        model.SelectedElementScope, model.SelectedElementCategory, model.ElementSearchText,
        selectedDescriptor = model.SelectedDescriptor?.Id, filteredCount = model.FilteredElementDescriptors.Count,
        categories = model.ElementCategories.ToArray(),
        controls = Descendants<FrameworkElement>((Grid)view.FindName("CatalogElementPanel"))
            .Where(control => control is TextBox or ComboBox or ListBox)
            .Select(control => new { type = control.GetType().Name, name = AutomationProperties.GetName(control),
                selected = control is Selector selector ? selector.SelectedItem is VisualPageComponentDescriptor descriptor ? descriptor.Id : selector.SelectedItem?.ToString() : null,
                text = control is TextBox text ? text.Text : null,
                itemCount = control is ItemsControl items ? (int?)items.Items.Count : null }).ToArray(),
        firstItems = model.FilteredElementDescriptors.Take(24).Select(item => new { item.Id, item.Category, item.MarkupName }).ToArray()
    };
    private static object PanelDiagnostics(VisualPageStudioView view, StudioHyperlibAuthoringView writer) => new
    {
        writer.IsWorkspaceChromeReady,
        owner = Window.GetWindow(view) is { } window ? new { window.IsVisible, window.WindowState } : null,
        popups = Descendants<Popup>(view).Select(popup => new
        {
            popup.Name, popup.IsOpen, popup.IsVisible, popup.IsLoaded,
            child = popup.Child?.GetType().FullName,
            placementTarget = popup.PlacementTarget is FrameworkElement target ? target.Name : null
        }).ToArray()
    };
    private static object Rectangle(Rect rect)=>new{rect.Left,rect.Top,rect.Width,rect.Height,rect.Right,rect.Bottom};
    private static Rect Bounds(FrameworkElement value, Visual ancestor)=>value.TransformToAncestor(ancestor).TransformBounds(new Rect(value.RenderSize));
    private static WebView2 Browser(StudioHyperlibAuthoringView view)=>(WebView2)typeof(StudioHyperlibAuthoringView).GetField("_browser",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
    private static CoreWebView2? Core(StudioHyperlibAuthoringView view)=>(typeof(StudioHyperlibAuthoringView).GetField("_browser",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(view) as WebView2)?.CoreWebView2;
    private static Task<bool> TrueAsync(CoreWebView2 core,string expression)=>IsTrueAsync(core,expression);
    private static async Task<bool> IsTrueAsync(CoreWebView2 core,string expression)=>await core.ExecuteScriptAsync(expression)=="true";
    private static void Check(Receipt receipt,bool condition,string assertion){if(!condition)throw new InvalidOperationException(assertion);receipt.Assertions.Add(assertion);}
    private static Task WaitAsync(Func<bool> predicate,string message,int seconds)=>WaitAsync(()=>Task.FromResult(predicate()),message,seconds);
    private static async Task WaitAsync(Func<Task<bool>> predicate,string message,int seconds){var until=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<until){if(await predicate())return;await Task.Delay(60);}throw new TimeoutException(message);}
    private static void Layout(FrameworkElement view,double width,double height){view.Width=width;view.Height=height;view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T found)yield return found;foreach(var value in Descendants<T>(child))yield return value;}}
    private static void Safety(nint hwnd,Receipt receipt,string stage){GetWindowThreadProcessId(GetForegroundWindow(),out var process);var hidden=!IsWindowVisible(hwnd);Check(receipt,hidden&&process!=Environment.ProcessId,"The full native fixture stays hidden and never takes desktop foreground during "+stage+".");}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository){if(Application.Current is not null)return;XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};}
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(nint hwnd);
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public string Project{get;set;}="";public string? ActiveNativeContentId{get;set;}public double DpiX{get;set;}public double DpiY{get;set;}public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public List<object> Samples{get;}=[];public List<string> Messages{get;}=[];public List<string> StatusMessages{get;}=[];public List<StudioWorkspaceAction> Actions{get;}=[];public object? NativeDiagnostics{get;set;}public object? CatalogDiagnostics{get;set;}public object? PanelDiagnostics{get;set;}public string? Diagnostics{get;set;}public string? DiagnosticError{get;set;}public string? Failure{get;set;}}
}
