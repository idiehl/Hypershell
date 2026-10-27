using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

/// <summary>Detached native integration; browser verification separately exercises the real writer/gateway.</summary>
public static class StudioHyperlibAuthoringIntegrationVerifier
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".hyperlib", ".git", "bin", "obj", "node_modules", "artifacts", ".vs" };

    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string privateWorkRoot)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "authoring-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var sourceLibrary = ResolveLibraryRoot(privateWorkRoot);
        var library = Path.Combine(fixture, "library");
        var sourceIndex = Hash(Path.Combine(sourceLibrary, "Content", "library.json"));
        CopyTree(sourceLibrary, library);
        var receipt = new Receipt { Fixture = fixture, Source = sourceLibrary };
        var variables = new[] { SettingsService.ApplicationDataOverrideVariable, SettingsService.SharedMyNetDirectoryOverrideVariable,
            StandaloneStudioHost.BaseDataOverrideVariable, "HYPERLIB_ROOT" };
        var previous = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            var profile = Path.Combine(fixture, "profile");
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, profile);
            Environment.SetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable, Path.Combine(profile, "AtlasMyNet"));
            Environment.SetEnvironmentVariable(StandaloneStudioHost.BaseDataOverrideVariable, profile);
            Environment.SetEnvironmentVariable("HYPERLIB_ROOT", library);
            await RunStaAsync(async () =>
            {
                LoadResources(Path.GetFullPath(repositoryRoot));
                var settings = new SettingsService(profile);
                var preferences = settings.Load();
                preferences.Application.CreateTerminalOnStartup = false;
                preferences.Explorer.StartupDirectory = fixture;
                settings.Save(preferences);
                using var shell = new MainWindowViewModel(settings, StandaloneStudioMode.HyperpageStudio);
                await shell.FileExplorer.WhenIdleAsync();
                await shell.InitializeAsync();
                var tab = await shell.OpenVisualPageProjectAsync(library) ?? throw new InvalidOperationException(shell.StatusMessage);
                var nested = await shell.OpenVisualPageProjectAsync(Path.Combine(tab.ProjectRoot, VisualPageProjectService.ProjectManifestFileName));
                Check(receipt, ReferenceEquals(tab, nested) && shell.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 1,
                    "Opening the outer Work folder and nested project manifest selects one project tab.");
                Check(receipt, tab.HasHyperlibAuthoring && !tab.IsHyperlibEditor && tab.HyperlibWorkspace!.HasLibraryContent,
                    "The project is the unified Studio with its own real Hyperlib content scope.");
                var view = tab.StudioView;
                foreach (var name in new[] { "StudioLeftPanelEdgeRevealHost", "StudioRightPanelEdgeRevealHost", "AiAssistantBottomEdgeRevealHost" })
                    if (view.FindName(name) is Popup popup) popup.IsOpen = false;
                tab.CanvasOnlyCommand.Execute(null);
                Layout(view, new(1100, 780));
                var writer = Descendants<StudioHyperlibAuthoringView>(view).Single();
                Check(receipt, ReferenceEquals(CachedWriter(view), writer) && !writer.IsLoaded
                    && tab.SaveHyperlibContentAsync?.Target == writer && tab.PrepareHyperlibContentToLeaveAsync?.Target == writer,
                    "Canvas contains the real native writer and project save/close routes target it without opening a host or HWND.");
                var dirtyTransition = typeof(StudioHyperlibAuthoringView).GetMethod("SetDirty", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("The writer's dirty-state transition is missing.");
                dirtyTransition.Invoke(writer, [true]);
                Check(receipt, writer.HasUnsavedContent && tab.HasUnsavedHyperlibContent && tab.HasUnsavedChanges,
                    "The native writer's draft event contributes to the upper project tab's unsaved state.");

                var document = await tab.CreateProjectDocumentAsync("css", "native-writer-draft.css")
                    ?? throw new InvalidOperationException(tab.StatusMessage);
                var path = VisualPageProjectService.ResolveProjectPath(tab.ProjectRoot, document.RelativePath);
                var diskBefore = File.ReadAllText(path);
                document.Content += "\n/* Keep this independent code draft while rearranging the writer. */\n";
                var expectedDraft = document.Content;
                foreach (var mode in new[] { "beside", "below", "documents", "canvas", "beside" })
                {
                    if (mode == "beside") tab.SplitHorizontalCommand.Execute(null);
                    else if (mode == "below") tab.SplitVerticalCommand.Execute(null);
                    else if (mode == "documents") tab.DocumentOnlyCommand.Execute(null);
                    else tab.CanvasOnlyCommand.Execute(null);
                    Layout(view, new(1100, 780));
                    Check(receipt, ReferenceEquals(CachedWriter(view), writer) && writer.HasUnsavedContent
                        && tab.HasUnsavedHyperlibContent && document.IsDirty && document.Content == expectedDraft,
                        $"The {mode} pane layout retains the same writer, content draft state and independent code buffer.");
                }
                tab.RequestPaneContent(document.ContentId);
                Layout(view, new(1100, 780));
                tab.RequestPaneContent("canvas");
                Layout(view, new(1100, 780));
                Check(receipt, ReferenceEquals(CachedWriter(view), writer) && Descendants<StudioHyperlibAuthoringView>(view).Single() == writer,
                    "Navigating between lower file and Canvas tabs reattaches the same native content writer.");
                var contentBefore = Hash(Path.Combine(library, "Content", "library.json"));
                Check(receipt, !await tab.SaveProjectAsync() && document.IsDirty && File.ReadAllText(path) == diskBefore,
                    "A writer that cannot confirm its content save prevents an independent source draft from being saved prematurely.");
                dirtyTransition.Invoke(writer, [false]);
                Check(receipt, !writer.HasUnsavedContent && !tab.HasUnsavedHyperlibContent && tab.HasUnsavedChanges,
                    "Clearing content dirty state leaves the independent code draft tracked by the project.");
                Check(receipt, await tab.SaveProjectAsync() && !document.IsDirty && File.ReadAllText(path) == expectedDraft,
                    "When the writer confirms a clean save, the project writes its independent code document successfully.");
                Check(receipt, Hash(Path.Combine(library, "Content", "library.json")) == contentBefore,
                    "Native layout and code saving do not fabricate or overwrite Hyperlib Page content.");

                foreach (var size in new[] { new Size(640, 760), new Size(1100, 800), new Size(1440, 900) })
                {
                    tab.CanvasOnlyCommand.Execute(null);
                    Layout(view, size);
                    var toolbar = view.FindName("HyperlibToolbar") as WrapPanel ?? throw new InvalidOperationException("The Studio workflow toolbar is missing.");
                    var phases = toolbar.Children.OfType<Button>().Where(button => button.Content is "Templates" or "Authoring" or "Publication").ToArray();
                    var bounds = phases.Select(button => Bounds(button, view)).ToArray();
                    Check(receipt, phases.Select(button => button.Content as string).SequenceEqual(["Templates", "Authoring", "Publication"])
                        && phases.All(button => button.ActualHeight >= 24 && button.ActualWidth >= 60)
                        && bounds.All(rectangle => rectangle.Left >= -1 && rectangle.Right <= size.Width + 1 && rectangle.Top >= -1 && rectangle.Bottom <= 51)
                        && bounds.SelectMany((left, index) => bounds.Skip(index + 1).Select(right => Overlaps(left, right))).All(overlap => !overlap),
                        $"At {size.Width:0}px Templates, Authoring, and Publication remain ordered, reachable, and separated inside their toolbar row.");
                    var screenshot = Path.Combine(fixture, $"authoring-native-{size.Width:0}.png");
                    Render(view, screenshot, size);
                    receipt.Renders.Add(screenshot);
                }
                Check(receipt, !writer.IsLoaded && typeof(StudioHyperlibAuthoringView).GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(writer) is null,
                    "Detached native verification never initializes a WebView, local host or visible helper window.");
                await shell.PrepareForShutdownAsync();
            });
            Check(receipt, sourceIndex == Hash(Path.Combine(sourceLibrary, "Content", "library.json")),
                "The input project content index remains byte-identical after verification on its private copy.");
            receipt.Passed = true;
        }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await File.WriteAllTextAsync(Path.Combine(fixture, "receipt.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STUDIO_AUTHORING_NATIVE_OK|checks:{receipt.Checks.Count}|renders:{receipt.Renders.Count}|{fixture}");
    }

    /// <summary>Browser QA calls the exact native direct-store boundary, without the gateway's PublishCommitted SSE notification.</summary>
    public static async Task<string> RestageForBrowserAsync(string privateWorkRoot)
    {
        var library = ResolveLibraryRoot(privateWorkRoot);
        if (!library.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Browser restaging requires a disposable QA project.");
        var service = new HyperlibStudioWorkspaceService();
        using (service)
        {
            var snapshot = await service.ReadAsync(privateWorkRoot);
            var root = snapshot.Context.ProjectRoot;
            var pipelineService = new HyperlibProjectPipelineService();
            var pipeline = await pipelineService.ReadAsync(root) ?? throw new InvalidDataException("This private project has no staged pipeline.");
            var recipe = await pipelineService.ReadDefinitionAsync(root) ?? throw new InvalidDataException("The staged recipes are missing.");
            var state = await new ContentStore(library).ReadAsync();
            recipe.PageTemplate.ContentAlignment = recipe.PageTemplate.ContentAlignment == "center" ? "left" : "center";
            recipe.PageTemplate.ContentWidth = recipe.PageTemplate.ContentWidth == "wide" ? "comfortable" : "wide";
            var updated = await pipelineService.UpdateDefinitionAsync(root, recipe, pipeline.Revision, state.Revision);
            return JsonSerializer.Serialize(new { result = "STUDIO_DIRECT_RESTAGING_OK", pipelineRevision = updated.Revision,
                contentRevision = (await new ContentStore(library).ReadAsync()).Revision });
        }
    }

    private static StudioHyperlibAuthoringView CachedWriter(VisualPageStudioView view) =>
        typeof(VisualPageStudioView).GetField("_studioAuthoring", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as StudioHyperlibAuthoringView
        ?? throw new InvalidOperationException("The native authoring surface was not retained.");
    private static string ResolveLibraryRoot(string root)
    {
        var path = Path.GetFullPath(root);
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        foreach (var candidate in new[] { path, Path.GetFullPath(Path.Combine(path, "../..")) })
            if (HyperlibWorkPreviewHostService.IsGeneratedLibrary(candidate)) return candidate;
        throw new InvalidOperationException("Native writer verification requires an independent generated Work project.");
    }
    private static void CopyTree(string source, string destination)
    {
        VisualPageProjectService.RejectReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        { VisualPageProjectService.RejectReparsePoint(file); File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
        foreach (var directory in Directory.EnumerateDirectories(source))
            if (!Excluded.Contains(Path.GetFileName(directory))) CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(Receipt receipt, bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); receipt.Checks.Add(message); }
    private static Rect Bounds(FrameworkElement child, Visual ancestor) => child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
    private static bool Overlaps(Rect left, Rect right)
    { left.Intersect(right); return !left.IsEmpty && left.Width > 1 && left.Height > 1; }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Layout(FrameworkElement view, Size size)
    {
        view.Width = size.Width; view.Height = size.Height;
        view.Measure(size); view.Arrange(new Rect(new Point(), size)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }
    private static void Render(FrameworkElement view, string path, Size size)
    {
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Studio writer detached integration" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void LoadResources(string repository)
    {
        if (Application.Current is not null)
        { if (!Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Run native authoring verification in its own process."); return; }
        var source = XDocument.Load(Path.Combine(repository, "src", "Hypershell", "App.xaml")).Root!;
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
    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string Fixture { get; set; } = "";
        public string Source { get; set; } = "";
        public List<string> Checks { get; } = [];
        public List<string> Renders { get; } = [];
        public string? Failure { get; set; }
    }
}
