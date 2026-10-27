using HyperLib.Core;
using Hypershell;
using Hypershell.Services;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

/// <summary>Exercises the real collection controls against an isolated library, without starting preview hosts.</summary>
public static class HyperlibObjectLibraryVerifier
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".hyperlib", ".hypershell", "bin", "obj", "node_modules", "app", "output", "artifacts", "publish", ".vs" };

    public static async Task VerifyAsync(string outputRoot, string sourceLibraryRoot)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLibraryRoot));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (source.Equals(output, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Object-library verification must use output outside the source library.");
        var fixture = Path.Combine(output, "object-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var report = new Receipt { Fixture = fixture };
        var original = Fingerprint(source);
        try
        {
            var library = Path.Combine(fixture, "library"); CopySource(source, library);
            await TemplateObjectLibrary.EnsureCoreLibraryAsync(library);
            var target = await ReusableTemplateManager.CreateBlankAsync(library, "Object collection verification");
            var targetRoot = ReusableTemplateCatalog.GetProjectRoot(library, target.Id);
            await HyperlibExpandedCoreLibrary.EnsureAsync(library);
            var migrated = Fingerprint(library);
            await RunStaAsync(async () =>
            {
                LoadResources();
                await VerifyPreviewCancellationAsync(library, report);
                await VerifyBrowserAsync(library, fixture, report);
                await VerifyEditorDraftAsync(library, targetRoot, fixture, report);
            });
            Check(report, Fingerprint(library) == migrated, "Browsing, collecting, filtering, and staging imports leave all persisted library and Template files unchanged.");
            await VerifyCommitAdapterAsync(library, report);
            Check(report, Fingerprint(source) == original, "The user's source library remains byte-for-byte unchanged.");
            report.Passed = true;
        }
        catch (Exception error) { report.Failure = error.ToString(); throw; }
        finally { await File.WriteAllTextAsync(Path.Combine(fixture, "hyperlib-object-library.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine($"HYPERLIB_OBJECT_LIBRARY_OK|assertions:{report.Assertions.Count}|renders:{report.Renders.Count}|private-copy|{fixture}");
    }

    private static async Task VerifyPreviewCancellationAsync(string library, Receipt report)
    {
        // Load without scheduling work, then exercise the real enabled-preview event path.
        // Keep every selection/cancellation on one STA turn so no debounce continuation
        // can begin compiling a preview before its request has been cancelled.
        var browser = new HyperlibObjectLibraryDialog(library) { PreviewEnabled = false };
        await browser.LoadForVerificationAsync();
        var objects = Field<ListBox>(browser, "_objects");
        var groups = Field<ComboBox>(browser, "_groups");
        var categories = Field<ComboBox>(browser, "_categories");
        var search = Field<TextBox>(browser, "_search");
        var basket = Field<ListBox>(browser, "_collectionList");
        var collect = Field<Button>(browser, "_collect");
        var previewStatus = Field<TextBlock>(browser, "_previewStatus");
        var service = Field<HyperlibTemplateObjectPreviewService>(browser, "_previewService");
        var sessionRoot = Field<string>(service, "_sessionRoot");
        var tokens = new List<CancellationToken>();
        void CaptureRequest()
        {
            var cancellation = Field<CancellationTokenSource?>(browser, "_previewCancellation")
                ?? throw new InvalidOperationException("Selecting an object did not start an enabled preview request.");
            var token = cancellation.Token;
            if (!tokens.Contains(token)) tokens.Add(token);
        }
        Check(report, objects.Items.Count > 3 && groups.Items.Count > 2 && categories.Items.Count > 2,
            "The cancellation regression uses a populated library with real object, group, and category controls.");
        collect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        browser.PreviewEnabled = true;
        objects.SelectedIndex = 1;
        CaptureRequest();
        search.Text = "no-cancellation-regression-match-7192";
        Check(report, objects.SelectedItem is null && tokens[^1].IsCancellationRequested && basket.Items.Count == 1,
            "Filtering away a selected object cancels its active preview and preserves the collection.");

        // This restore raised ObjectDisposedException in the reported production crash:
        // the empty result had disposed its CTS but left it in the selection field.
        search.Text = "";
        CaptureRequest();
        Check(report, objects.SelectedItem is not null && tokens.Count == 2,
            "Restoring results after an empty search starts a new preview without cancelling a disposed request.");
        for (var index = 0; index < 4; index++)
        {
            search.Text = "no-cancellation-regression-match-" + index;
            search.Text = "still-no-cancellation-regression-match-" + index;
            groups.SelectedIndex = index % 2 + 1;
            categories.SelectedIndex = index % 2 + 1;
            Check(report, objects.SelectedItem is null && tokens[^1].IsCancellationRequested,
                "Repeated empty filtering safely retires preview request " + (index + 1) + ".");
            groups.SelectedIndex = 0; categories.SelectedIndex = 0; search.Text = "";
            CaptureRequest();
        }
        for (var index = 0; index < 12; index++)
        {
            objects.SelectedIndex = index % 3 + 1;
            CaptureRequest();
        }
        Check(report, tokens.Take(tokens.Count - 1).All(token => token.IsCancellationRequested) && basket.Items.Count == 1,
            "Rapid object changes cancel every superseded preview while keeping collected objects intact.");
        groups.SelectedIndex = 1;
        CaptureRequest();
        groups.SelectedIndex = 0;
        CaptureRequest();
        Check(report, objects.SelectedItem is not null && tokens.Take(tokens.Count - 1).All(token => token.IsCancellationRequested),
            "Changing Design Groups safely replaces the preview even when the list clears its selection internally.");
        search.Text = "no-cancellation-regression-match-before-close";
        var finalStatus = previewStatus.Text;
        browser.Close();
        Check(report, Field<bool>(browser, "_closed") && tokens.All(token => token.IsCancellationRequested),
            "Closing immediately after clearing the selection cancels outstanding work without reusing a disposed CTS.");
        // A posted control event after close must not recreate a request or mutate status.
        search.Text = "";
        await Task.Delay(350);
        Check(report, previewStatus.Text == finalStatus && Field<object?>(browser, "_preview") is null,
            "Cancelled preview continuations cannot replace the empty-selection status or create a WebView after close.");
        Check(report, Field<object?>(service, "_host") is null && !Directory.Exists(sessionRoot),
            "The enabled-preview cancellation regression launches no preview host and writes no preview session.");
    }

    private static async Task VerifyCommitAdapterAsync(string library, Receipt report)
    {
        var target = await ReusableTemplateManager.CreateBlankAsync(library, "Commit adapter verification");
        var root = ReusableTemplateCatalog.GetProjectRoot(library, target.Id);
        var loaded = await HyperlibStudioAuthoringService.LoadAsync(root, "authoring.template.json");
        var originalHash = loaded.Snapshot.Sha256;
        var donor = await ReusableTemplateManager.CreateBlankAsync(library, "Adapter source verification");
        var donorRoot = ReusableTemplateCatalog.GetProjectRoot(library, donor.Id);
        var donorSource = await HyperlibStudioAuthoringService.LoadAsync(donorRoot, "authoring.template.json");
        var component = HyperlibTemplateEditSession.CloneElement(donorSource.Definition.Blocks.First(block => block.Component == "TextBlock"));
        component.Id = "adapter-badge"; component.Title = "Adapter badge"; component.Component = "AdapterBadge";
        donorSource.Definition.Blocks.Add(component);
        File.Copy(Path.Combine(donorRoot, "src", "Pages", "Blocks", "TextBlock.razor"), Path.Combine(donorRoot, "src", "Pages", "Blocks", "AdapterBadge.razor"));
        await HyperlibStudioAuthoringService.SaveAsync(donorRoot, donorSource.Snapshot, donorSource.Definition);
        var group = await TemplateObjectLibrary.SaveDesignGroupAsync(library, donorRoot, "Adapter component source");
        var item = TemplateObjectLibrary.ListObjects(library, group.Id).Single(item => item.Kind == "element" && item.Element?.Id == "adapter-badge");
        var plan = await TemplateObjectImporter.PrepareAsync(library, root, loaded.Definition, [new(group.Id, item.Id)]);
        Check(report, plan.Files.Count > 0, "The adapter acceptance import includes real component dependencies.");
        var edited = HyperlibTemplateEditSession.CloneDefinition(plan.Definition);
        var existingId = loaded.Definition.Blocks.First().Id;
        edited.Blocks.Single(block => block.Id == existingId).Title = "Edited after collecting objects";
        var snapshot = await HyperlibTemplateObjectAuthoringService.CommitAsync(root, loaded.Snapshot, edited, [plan]);
        var reopened = await HyperlibStudioAuthoringService.LoadAsync(root, "authoring.template.json");
        Check(report, snapshot.Sha256 != originalHash && snapshot.Sha256 == reopened.Snapshot.Sha256
            && reopened.Definition.Blocks.Single(block => block.Id == existingId).Title == "Edited after collecting objects",
            "Saving collected objects publishes later draft edits and returns the actual updated source snapshot.");
        Check(report, plan.Files.All(file => File.Exists(Path.Combine(root, file.RelativePath))
            && SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file.RelativePath))).SequenceEqual(SHA256.HashData(file.Content))),
            "An explicit save copies the prepared component dependencies with their exact bytes.");
        reopened.Definition.Blocks.Single(block => block.Id == existingId).Title = "Edited again after saving";
        snapshot = await HyperlibTemplateObjectAuthoringService.CommitAsync(root, snapshot, reopened.Definition, []);
        reopened = await HyperlibStudioAuthoringService.LoadAsync(root, "authoring.template.json");
        Check(report, reopened.Snapshot.Sha256 == snapshot.Sha256
            && reopened.Definition.Blocks.Single(block => block.Id == existingId).Title == "Edited again after saving",
            "A second save without imports uses the updated snapshot and preserves the collected source.");

        var conflicted = await ReusableTemplateManager.CreateBlankAsync(library, "Conflict adapter verification");
        var conflictRoot = ReusableTemplateCatalog.GetProjectRoot(library, conflicted.Id);
        var conflictSource = await HyperlibStudioAuthoringService.LoadAsync(conflictRoot, "authoring.template.json");
        var conflictPlan = await TemplateObjectImporter.PrepareAsync(library, conflictRoot, conflictSource.Definition, [new(group.Id, item.Id)]);
        await File.AppendAllTextAsync(Path.Combine(conflictRoot, "authoring.template.json"), "\n");
        var beforeConflict = Fingerprint(conflictRoot);
        var rejected = false;
        try { await HyperlibTemplateObjectAuthoringService.CommitAsync(conflictRoot, conflictSource.Snapshot, conflictPlan.Definition, [conflictPlan]); }
        catch (VisualPageUserSourceDriftException) { rejected = true; }
        Check(report, rejected && Fingerprint(conflictRoot) == beforeConflict,
            "An external schema edit rejects saving before any imported dependency or authoring file is written.");
    }

    private static async Task VerifyBrowserAsync(string library, string fixture, Receipt report)
    {
        var browser = new HyperlibObjectLibraryDialog(library) { PreviewEnabled = false };
        await browser.LoadForVerificationAsync();
        var surface = (FrameworkElement)browser.Content;
        Render(surface, Path.Combine(fixture, "core-library-wide.png"), 1200, 840, report);
        var groups = Named<ComboBox>(surface, "Design Group");
        var categories = Named<ComboBox>(surface, "Object category");
        var search = Named<TextBox>(surface, "Find library objects");
        var objects = Named<ListBox>(surface, "Library objects");
        var basket = Named<ListBox>(surface, "Template collection");
        var name = Named<TextBox>(surface, "New Template name");
        var collect = Button(surface, "Add to collection");
        var create = Button(surface, "Create Template");
        var remove = Button(surface, "Remove");
        var total = objects.Items.Count;
        Check(report, groups.Items.Count > BaseTemplateCatalog.List().Count && total > 50,
            "The Core browser offers all migrated designs and individual modular objects.");
        Check(report, categories.Items.Count >= 5 && categories.Items.OfType<string>().Any(value => value.Contains("Typography", StringComparison.OrdinalIgnoreCase)),
            "Categories expose shared typography alongside page elements, layouts, and other use cases.");
        Check(report, !create.IsEnabled && basket.Items.Count == 0, "An unnamed new Template cannot be created accidentally.");
        name.Text = "  A considered collection  ";
        Check(report, create.IsEnabled && browser.NewTemplateName == "A considered collection", "Naming a Template enables an intentional empty foundation before choosing objects.");

        var first = objects.SelectedItem ?? throw new InvalidOperationException("The catalog has no selected object.");
        var firstObject = Property<TemplateLibraryObject>(first, "Object");
        var catalogId = Named<TextBox>(surface, "Object catalog ID");
        Check(report, catalogId.IsReadOnly && catalogId.Text == firstObject.CatalogId && Button(surface, "Copy ID").IsEnabled,
            "The selected object's stable catalog ID is visible, selectable, and available to copy.");
        groups.SelectedItem = groups.Items.Cast<object>().First(item => Property<string>(item, "Id") is { Length: > 0 } id && id != firstObject.GroupId);
        categories.SelectedItem = categories.Items.OfType<string>().First(category => category != "All objects" && category != firstObject.Category);
        search.Text = firstObject.CatalogId.ToLowerInvariant();
        Check(report, objects.Items.Count > 0 && objects.Items.Cast<object>().All(item => Property<TemplateLibraryObject>(item, "Object").CatalogId == firstObject.CatalogId)
            && catalogId.Text == firstObject.CatalogId,
            "An exact catalog ID finds its object case-insensitively even when unrelated group and category filters are selected.");
        search.Text = ""; groups.SelectedIndex = 0; categories.SelectedIndex = 0; objects.SelectedItem = first;
        collect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        collect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(report, basket.Items.Count == 1 && !collect.IsEnabled && collect.Content?.ToString() == "In collection",
            "Adding an object updates the collection and a repeated add cannot duplicate it.");
        categories.SelectedItem = firstObject.Category;
        Check(report, objects.Items.Count > 0 && objects.Items.Cast<object>().All(item => Property<TemplateLibraryObject>(item, "Object").Category == firstObject.Category),
            "The type/use-case filter limits actual objects to the chosen category.");
        var groupId = Property<string>(first, "GroupId");
        groups.SelectedItem = groups.Items.Cast<object>().Single(item => Property<string>(item, "Id") == groupId);
        Check(report, objects.Items.Count > 0 && objects.Items.Cast<object>().All(item => Property<string>(item, "GroupId") == groupId),
            "The Design Group filter narrows the same collection without discarding the category.");
        search.Text = firstObject.Title;
        Check(report, objects.Items.Count > 0, "A chosen object's title can be found within its Design Group and category.");
        search.Text = "no-object-with-this-unique-query-5182";
        Check(report, objects.Items.Count == 0 && basket.Items.Count == 1, "An empty search preserves the user's collected object.");
        search.Text = ""; groups.SelectedIndex = 0; categories.SelectedIndex = 0;
        Check(report, objects.Items.Count == total && basket.Items.Count == 1, "Clearing filters restores the catalog and retains the independent collection.");
        basket.SelectedIndex = 0; remove.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(report, basket.Items.Count == 0 && create.IsEnabled, "Removing the final object keeps the explicitly named blank Template available.");
        objects.SelectedItem = first;
        collect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        browser.CreateTemplateAsync = (_, _) => Task.FromResult<string?>("This combination needs another layout choice.");
        create.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(report, basket.Items.Count == 1 && create.IsEnabled && browser.NewTemplateName == "A considered collection"
            && Descendants<TextBlock>(surface).Any(text => text.Text == "This combination needs another layout choice."),
            "A rejected Template creation stays in the browser with its name and collected objects intact for correction.");
        Render(surface, Path.Combine(fixture, "core-library-compact.png"), 900, 720, report);
        CheckVisibleWithin(report, surface, create, "Create Template remains visible in the compact collection browser.");
        CheckVisibleWithin(report, surface, remove, "Collection removal remains visible in the compact browser.");
        CheckVisibleWithin(report, surface, collect, "The selected object's collect action remains visible in the compact browser.");
        Check(report, objects.ActualHeight >= 100 && basket.ActualHeight >= 35, "Compact browsing retains usable object and collection lists.");
        browser.Close();
    }

    private static async Task VerifyEditorDraftAsync(string library, string targetRoot, string fixture, Receipt report)
    {
        var source = await HyperlibStudioAuthoringService.LoadAsync(targetRoot, "authoring.template.json");
        var targetBefore = Fingerprint(targetRoot);
        var nullableDefinition = HyperlibTemplateEditSession.CloneDefinition(source.Definition);
        nullableDefinition.Blocks.First().Fields.First().DefaultValue = ContentJson.Value<object?>(null);
        var nullBrowser = new HyperlibObjectLibraryDialog(library, targetRoot, nullableDefinition) { PreviewEnabled = false };
        Check(report, Field<TemplateDefinition>(nullBrowser, "_targetDraft").Blocks.First().Fields.First().DefaultValue is { ValueKind: JsonValueKind.Null },
            "Opening the object browser preserves explicit null defaults in the Template draft.");
        nullBrowser.Close();
        var editor = new HyperlibTemplateEditorDialog(targetRoot, source.Definition, source.Snapshot, reusableTemplate: true);
        var surface = (FrameworkElement)editor.Content;
        Render(surface, Path.Combine(fixture, "template-library-entry.png"), 1100, 800, report);
        Check(report, Button(surface, "Browse Core Library").IsEnabled && Button(surface, "Save as Design Group").IsEnabled,
            "An independent Template exposes object shopping and publishing a saved Design Group.");
        var label = Named<TextBox>(surface, "Element name");
        label.Text = "An element edited before shopping";
        var session = Field<HyperlibTemplateEditSession>(editor, "_session");
        var editedId = session.Draft.Blocks.First(block => block.Title == label.Text).Id;
        var group = TemplateObjectLibrary.ListDesignGroups(library).First(item => item.Id != "blank" && item.SourceTemplateId is not null);
        var item = TemplateObjectLibrary.ListObjects(library, group.Id).First(item => item.Kind == "element");
        var plan = await TemplateObjectImporter.PrepareAsync(library, targetRoot, session.CreateDefinition(), [new(group.Id, item.Id)]);
        editor.AddLibraryObjects(plan);
        session = Field<HyperlibTemplateEditSession>(editor, "_session");
        Check(report, session.Draft.Blocks.Any(block => block.Id == editedId && block.Title == "An element edited before shopping"),
            "Adding library objects preserves edits already made in the Template draft.");
        Check(report, Field<List<TemplateImportPlan>>(editor, "_importPlans").Count == 1 && !Button(surface, "Save as Design Group").IsEnabled,
            "Imported source remains staged and publishing a Design Group waits for an explicit Template save.");
        Check(report, Fingerprint(targetRoot) == targetBefore, "Adding the prepared collection changes no Template source or schema before Save.");
        // Suppress the interactive discard prompt only after proving the draft never touched disk.
        typeof(HyperlibTemplateEditorDialog).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(editor, false);
        editor.Close();
    }

    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement
        => Descendants<T>(root).Single(item => AutomationProperties.GetName(item) == name);
    private static Button Button(DependencyObject root, string label)
        => Descendants<Button>(root).Single(item => item.Content?.ToString() == label);
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var descendant in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return descendant;
    }
    private static void CheckVisibleWithin(Receipt report, FrameworkElement surface, FrameworkElement control, string message)
    {
        surface.UpdateLayout();
        var bounds = control.TransformToAncestor(surface).TransformBounds(new Rect(control.RenderSize));
        Check(report, control.Visibility == Visibility.Visible && bounds.Width > 20 && bounds.Height > 20 && bounds.Left >= 0 && bounds.Top >= 0
            && bounds.Right <= surface.ActualWidth + 1 && bounds.Bottom <= surface.ActualHeight + 1, message);
    }
    private static void Check(Receipt report, bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); report.Assertions.Add(message); }
    private static IEnumerable<string> Files(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.OrdinalIgnoreCase))
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
        Directory.CreateDirectory(destination); long bytes = 0; var count = 0;
        foreach (var file in Files(source))
        {
            if (++count > 30_000 || (bytes += new FileInfo(file).Length) > 768L * 1024 * 1024) throw new InvalidOperationException("Fixture exceeds bounded copy budget.");
            var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }
    private static string Fingerprint(string root)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Files(root)) { digest.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file) + "\n")); using var input = File.OpenRead(file); digest.AppendData(SHA256.HashData(input)); }
        return Convert.ToHexString(digest.GetHashAndReset());
    }
    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () => { try { await action(); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Hyperlib object-library verification" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void LoadResources()
    {
        if (Application.Current is not null) return;
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Hypershell", "App.xaml"))) directory = directory.Parent;
        if (directory is null) throw new FileNotFoundException("Run from the Hypershell source checkout.");
        var source = XDocument.Load(Path.Combine(directory.FullName, "src", "Hypershell", "App.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary", source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }
    private static void Render(FrameworkElement surface, string path, double width, double height, Receipt report)
    {
        surface.Width = width - surface.Margin.Left - surface.Margin.Right;
        surface.Height = height - surface.Margin.Top - surface.Margin.Bottom;
        surface.Measure(new(width, height)); surface.Arrange(new(0, 0, width, height)); surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(Application.Current.TryFindResource("AppBackgroundBrush") as Brush ?? Brushes.Black, null, new Rect(0, 0, width, height));
        bitmap.Render(backdrop); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output); report.Renders.Add(path);
    }
    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string Fixture { get; init; } = "";
        public string? Failure { get; set; }
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
    }
}
