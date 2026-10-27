using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

/// <summary>Checks the real Studio toolbar, code panes, and assistant layout in a private project.</summary>
public static class StudioPanelLayoutVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot)
    {
        var root = Path.Combine(Path.GetFullPath(outputDirectory), "studio-panels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var receipt = new Receipt { Fixture = root };
        var variables = new[] { SettingsService.ApplicationDataOverrideVariable, SettingsService.SharedMyNetDirectoryOverrideVariable,
            StandaloneStudioHost.BaseDataOverrideVariable, "HYPERLIB_ROOT" };
        var previous = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            var profile = Path.Combine(root, "profile");
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, profile);
            Environment.SetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable, Path.Combine(profile, "AtlasMyNet"));
            Environment.SetEnvironmentVariable(StandaloneStudioHost.BaseDataOverrideVariable, profile);
            Environment.SetEnvironmentVariable("HYPERLIB_ROOT", Path.Combine(root, "no-registered-library"));
            await RunStaAsync(async () =>
            {
                LoadResources(Path.GetFullPath(repositoryRoot));
                var settings = new SettingsService(profile);
                using var workspace = new MainWindowViewModel(settings, StandaloneStudioMode.HyperpageStudio);
                var tab = await workspace.CreateVisualPageProjectAsync(root, "Panel layout fixture", new HyperpageProjectCreationOptions())
                    ?? throw new InvalidOperationException(workspace.StatusMessage);
                Check(receipt, tab.HasAiAssistant, "The private project has the real attached assistant conversation without starting an AI request.");
                var document = await tab.CreateProjectDocumentAsync("css", "appearance-with-a-long-file-name.css")
                    ?? throw new InvalidOperationException(tab.StatusMessage);
                _ = await tab.CreateProjectDocumentAsync("javascript", "interactions-with-a-long-file-name.js")
                    ?? throw new InvalidOperationException(tab.StatusMessage);
                _ = await tab.CreateProjectDocumentAsync("csharp", "ApplicationBehavior.cs")
                    ?? throw new InvalidOperationException(tab.StatusMessage);
                const string draft = "/* Preserve this unfinished styling while rearranging panels. */";
                document.Content += "\n" + draft;
                const string prompt = "Keep this unsent assistant draft while the panel is hidden.";
                tab.AiAssistant!.Prompt = prompt;
                tab.OpenAiAssistant();
                var view = tab.StudioView;
                // Detached fixtures use the real button commands but never open helper HWNDs on the user's desktop.
                foreach (var name in new[] { "StudioLeftPanelEdgeRevealHost", "StudioRightPanelEdgeRevealHost", "AiAssistantBottomEdgeRevealHost" })
                    Named<Popup>(view, name).IsOpen = false;
                foreach (var size in new[] { new Size(960, 760), new Size(1440, 900) })
                {
                    foreach (var mode in new[] { "beside", "below", "documents" })
                    {
                        if (mode == "beside") tab.SplitHorizontalCommand.Execute(null);
                        else if (mode == "below") tab.SplitVerticalCommand.Execute(null);
                        else tab.DocumentOnlyCommand.Execute(null);
                        Layout(view, size);
                        Render(view, Path.Combine(root, $"studio-{mode}-{size.Width:0}.png"), size, 1, receipt);
                        VerifyToolbarAndCodePane(view, receipt, mode, size);
                    }
                }

                var testSize = new Size(1280, 850);
                tab.SplitHorizontalCommand.Execute(null);
                Layout(view, testSize);
                var paneHost = Named<ContentControl>(view, "PaneHost");
                var assistant = Named<Border>(view, "AiAssistantPanelHost");
                var panelRow = Named<RowDefinition>(view, "AiAssistantPanelRow");
                var splitterRow = Named<RowDefinition>(view, "AiAssistantSplitterRow");
                var hide = Named<Button>(view, "AiAssistantHideButton");
                var reopen = Named<Button>(view, "AiAssistantReopenButton");
                var central = Named<Border>(view, "CentralWorkspaceHost");
                Check(receipt, assistant.Visibility == Visibility.Visible && panelRow.ActualHeight > 100
                    && splitterRow.ActualHeight > 0 && ReferenceEquals(hide.Command, tab.ToggleAiAssistantCommand),
                    "The open assistant occupies actual layout rows and its hide control uses the existing toggle command.");

                // GridSplitter writes a local Height value; recreate that condition before every hide/show cycle.
                foreach (var resizedHeight in new[] { 180d, 340d, 220d })
                {
                    panelRow.Height = new GridLength(resizedHeight);
                    Layout(view, testSize);
                    var openPaneHeight = paneHost.ActualHeight;
                    var occupiedHeight = panelRow.ActualHeight + splitterRow.ActualHeight;
                    hide.Command.Execute(hide.CommandParameter);
                    Layout(view, testSize);
                    Check(receipt, !tab.IsAiAssistantOpen && panelRow.ActualHeight < 1 && splitterRow.ActualHeight < 1
                        && assistant.Visibility == Visibility.Collapsed
                        && paneHost.ActualHeight >= openPaneHeight + occupiedHeight - 2,
                        $"Hiding the {resizedHeight:0}px assistant reclaims its panel and splitter rows for the workspace without a blank gap.");
                    Check(receipt, tab.IsAiAssistantClosed && ReferenceEquals(reopen.Command, tab.ToggleAiAssistantCommand),
                        "The collapsed assistant exposes the existing reopen command without replacing its conversation.");
                    reopen.Command.Execute(reopen.CommandParameter);
                    Layout(view, testSize);
                    Check(receipt, tab.IsAiAssistantOpen && panelRow.ActualHeight > 100
                        && assistant.Visibility == Visibility.Visible && !tab.IsAiAssistantClosed,
                        "The existing reopen button restores a visible assistant after its row was resized and collapsed.");
                }
                hide.Command.Execute(hide.CommandParameter);
                foreach (var size in new[] { new Size(960, 760), new Size(1440, 900) })
                {
                    Layout(view, size);
                    var paneBounds = Bounds(paneHost, view);
                    var centralBounds = Bounds(central, view);
                    Check(receipt, Math.Abs(paneBounds.Bottom - centralBounds.Bottom) < 2
                        && panelRow.ActualHeight < 1 && splitterRow.ActualHeight < 1,
                        $"The workspace continues to fill the reclaimed assistant area at {size.Width:0}×{size.Height:0}.");
                    Render(view, Path.Combine(root, $"studio-assistant-collapsed-{size.Width:0}.png"), size, 1.5, receipt);
                }
                reopen.Command.Execute(reopen.CommandParameter);
                Layout(view, testSize);
                Check(receipt, tab.AiAssistant.Prompt == prompt && document.IsDirty && document.Content.Contains(draft, StringComparison.Ordinal),
                    "Resizing, hiding, and reopening the assistant preserves its unsent prompt and unfinished source text.");

                var canvasPane = Panes(tab.Layout.Root).Single(pane => pane.PaneContents.Contains("canvas"));
                Check(receipt, tab.MovePaneContent(document.ContentId, canvasPane.NodeId),
                    "The existing drag/drop layout route can move the long source tab into the Canvas pane.");
                var compact = new Size(960, 760);
                Layout(view, compact);
                var canvasTabs = Descendants<TabControl>(paneHost).Single(control => control.Items.OfType<TabItem>()
                    .Any(item => item.Tag as string == "canvas"));
                var canvasHeader = canvasTabs.Template.FindName("CanvasTabHeaderScrollViewer", canvasTabs) as ScrollViewer
                    ?? throw new InvalidOperationException("The Canvas pane needs its bounded header scroll viewer.");
                var movedTab = canvasTabs.Items.OfType<TabItem>().Single(item => item.Tag as string == document.ContentId);
                movedTab.BringIntoView();
                Layout(view, compact);
                var movedClose = ContentClose(view, document.ContentId);
                var movedCloseBounds = Bounds(movedClose, view);
                var viewport = Bounds(canvasHeader, view);
                Check(receipt, movedTab.ActualWidth <= canvasHeader.ViewportWidth + 1
                    && movedCloseBounds.Left >= viewport.Left - 1 && movedCloseBounds.Right <= viewport.Right + 1,
                    "A long document moved into the compact Canvas pane keeps its individual close control inside the scrollable header viewport.");
                var createDocument = Descendants<Button>(canvasTabs).Single(button => AutomationProperties.GetName(button) == "Create document");
                createDocument.BringIntoView();
                Layout(view, compact);
                Check(receipt, Bounds(createDocument, view).Left >= viewport.Left - 1
                    && Bounds(createDocument, view).Right <= viewport.Right + 1,
                    "The Canvas pane's add-document control remains reachable after scrolling past the moved long document tab.");
                movedTab.BringIntoView();
                Layout(view, compact);
                Render(view, Path.Combine(root, "studio-long-document-in-canvas.png"), compact, 1, receipt);
                var initialContents = Contents(tab.Layout.Root).ToHashSet(StringComparer.Ordinal);
                var closeDocument = ContentClose(view, document.ContentId);
                closeDocument.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(view, testSize);
                Check(receipt, Contents(tab.Layout.Root).ToHashSet(StringComparer.Ordinal)
                        .SetEquals(initialContents.Where(id => id != document.ContentId))
                    && tab.OpenProjectDocuments.Any(item => ReferenceEquals(item, document)) && document.IsDirty
                    && tab.CaptureProjectDocumentRecovery().Any(item => item.RelativePath == document.RelativePath && item.Content == document.Content),
                    "The real document-tab X closes only that tab, keeps its siblings, and retains the dirty source object and recovery text.");
                await tab.OpenProjectNodeAsync(new HyperpageProjectTreeNodeViewModel
                {
                    Name = Path.GetFileName(document.RelativePath), RelativePath = document.RelativePath, IsDirectory = false
                });
                Layout(view, testSize);
                Check(receipt, Contents(tab.Layout.Root).Contains(document.ContentId)
                    && tab.OpenProjectDocuments.Single(item => item.RelativePath == document.RelativePath) == document
                    && document.Content.Contains(draft, StringComparison.Ordinal) && document.IsDirty,
                    "Reopening the closed document from the project tree restores the same unfinished buffer.");
                ContentClose(view, "canvas").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(view, testSize);
                Check(receipt, !Contents(tab.Layout.Root).Contains("canvas") && Contents(tab.Layout.Root).Contains(document.ContentId)
                    && Descendants<TabControl>(paneHost).Count(control => control.Tag is string) == 1,
                    "The Canvas-tab X removes only Canvas and leaves the document pane usable.");
                while (Contents(tab.Layout.Root).FirstOrDefault() is { } contentId)
                {
                    var countBeforeClose = Contents(tab.Layout.Root).Count();
                    ContentClose(view, contentId).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Layout(view, testSize);
                    Check(receipt, Contents(tab.Layout.Root).Count() == countBeforeClose - 1,
                        "Each individual document close removes exactly one current tab without removing unrelated siblings.");
                }
                var openCanvas = Descendants<Button>(paneHost).Single(button => AutomationProperties.GetName(button) == "Open canvas");
                Check(receipt, tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane && tab.Layout.Root.PaneContents.Count == 0
                    && Descendants<TextBlock>(paneHost).Any(text => text.Text == "No open tabs")
                    && openCanvas.Command is not null && document.IsDirty && tab.AiAssistant.Prompt == prompt,
                    "Closing the final individual tab leaves the explicit empty workspace and its reopen action without dropping source or assistant drafts.");
                Render(view, Path.Combine(root, "studio-empty-tabs.png"), testSize, 1, receipt);
                openCanvas.Command!.Execute(openCanvas.CommandParameter);
                Layout(view, testSize);
                Check(receipt, tab.IsCanvasOnlyWorkspace && Contents(tab.Layout.Root).SequenceEqual(["canvas"]),
                    "The empty workspace's real Open canvas action restores the Canvas tab.");
                await tab.OpenProjectNodeAsync(new HyperpageProjectTreeNodeViewModel
                {
                    Name = Path.GetFileName(document.RelativePath), RelativePath = document.RelativePath, IsDirectory = false
                });
                tab.DocumentOnlyCommand.Execute(null);
                Layout(view, testSize);
                var codeClose = Descendants<Button>(paneHost).Single(button => AutomationProperties.GetName(button) == "Close code pane");
                codeClose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(view, testSize);
                Check(receipt, tab.IsCanvasOnlyWorkspace && document.IsDirty && document.Content.Contains(draft, StringComparison.Ordinal)
                    && tab.CaptureProjectDocumentRecovery().Any(item => item.RelativePath == document.RelativePath && item.Content == document.Content),
                    "The real code-pane close button returns to canvas while preserving its dirty source and recovery draft.");
                const string nativePageUrl = "http://127.0.0.1:54180/page/wizard-page";
                view.ShowRuntimePreview(nativePageUrl);
                var pendingPreview = typeof(VisualPageStudioView).GetField("_requestedRuntimePreviewUrl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Check(receipt, Contents(tab.Layout.Root).Contains("runtime-preview") && pendingPreview?.GetValue(view) as string == nativePageUrl,
                    "Opening Hyperlib content targets a lower runtime tab and retains its URL before WebView initialization.");
                await workspace.PrepareForShutdownAsync();
                var recovery = await new VisualPageStudioSessionService(settings.VisualPageStudioSessionPath).LoadAsync();
                Check(receipt, recovery.Projects.Single().ProjectDocuments.Any(item => item.RelativePath == document.RelativePath && item.Content == document.Content),
                    "The preserved source draft remains recoverable through the application's actual shutdown persistence.");
                await StudioTabCloseSemanticsVerifier.VerifyAsync(root);
            });
            receipt.Passed = true;
        }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await File.WriteAllTextAsync(Path.Combine(root, "studio-panel-layout.json"),
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STUDIO_PANEL_LAYOUT_OK|assertions:{receipt.Assertions.Count}|renders:{receipt.Renders.Count}|private-project|{root}");
    }

    private static void VerifyToolbarAndCodePane(VisualPageStudioView view, Receipt receipt, string mode, Size size)
    {
        var toolbar = Named<FrameworkElement>(view, "WorkspaceLayoutToolbar");
        var paneHost = Named<ContentControl>(view, "PaneHost");
        var toolbarBounds = Bounds(toolbar, view);
        var paneBounds = Bounds(paneHost, view);
        Check(receipt, toolbar.Visibility == Visibility.Visible && toolbar.ActualHeight > 20
            && toolbarBounds.Bottom <= paneBounds.Top + 1,
            $"At {size.Width:0}px in {mode} mode, canvas layout/display controls have their own row above editor content.");
        var codeCloses = Descendants<Button>(paneHost).Where(button => AutomationProperties.GetName(button) == "Close code pane").ToList();
        Check(receipt, codeCloses.Count == 1 && codeCloses[0].ActualWidth >= 24 && codeCloses[0].ActualHeight >= 24
            && !Overlaps(Bounds(codeCloses[0], view), toolbarBounds),
            $"At {size.Width:0}px in {mode} mode, the code close control remains visible and cannot be covered by canvas controls.");
        var tabs = Descendants<TabControl>(paneHost).Single(control => control.Tag is string
            && !control.Items.OfType<TabItem>().Any(item => item.Tag as string is "canvas"));
        var closeBounds = Bounds(codeCloses[0], view);
        Check(receipt, tabs.Items.OfType<TabItem>().Where(item => item.ActualWidth > 0).All(item =>
                !Overlaps(Bounds(item, view), closeBounds)),
            $"At {size.Width:0}px in {mode} mode, wrapped document tabs and the add tab do not overlap the code close control. "
            + $"Close={closeBounds}; tabs={tabs.ActualWidth:0.##}; headerBounds="
            + string.Join("; ", tabs.Items.OfType<TabItem>().Select(item => $"{item.Tag}:{Bounds(item, view)}")));
        var documentHeaders = Descendants<TabControl>(paneHost).Where(control => control.Tag is string)
            .SelectMany(control => control.Items.OfType<TabItem>()).Where(item => item.Tag is string).ToList();
        Check(receipt, documentHeaders.All(item => item.Header is Grid header
            && header.Children.OfType<Button>().SingleOrDefault(button => AutomationProperties.GetName(button) is "Close document tab" or "Close canvas tab") is { } close
            && header.Children.OfType<Button>().SingleOrDefault(button => AutomationProperties.GetName(button).StartsWith("Explain ", StringComparison.Ordinal)) is { } help
            && !Overlaps(Bounds(help, view), Bounds(close, view))
            && close.Opacity < .01 && close.ActualWidth >= 19 && close.ActualWidth <= 21
            && header.Children.OfType<TextBlock>().SingleOrDefault() is { } title
            && !Overlaps(Bounds(title, view), Bounds(close, view))
            && (AutomationProperties.GetName(close) is "Close document tab" or "Close canvas tab")
            && close.Tag as string == item.Tag as string),
            $"At {size.Width:0}px in {mode} mode, every Canvas/document tab reserves a hidden individual X without covering its title.");
        var closeStyle = (Style)view.Resources["StudioTabCloseButtonStyle"];
        Check(receipt, closeStyle.Triggers.OfType<DataTrigger>().Any(trigger =>
                trigger.Binding is System.Windows.Data.Binding binding && binding.Path?.Path == "IsMouseOver")
            && closeStyle.Triggers.OfType<DataTrigger>().Any(trigger =>
                trigger.Binding is System.Windows.Data.Binding binding && binding.Path?.Path == "IsKeyboardFocusWithin"),
            "Individual tab close controls reveal on tab hover and keyboard focus while retaining their reserved layout space.");
    }

    private static Button ContentClose(VisualPageStudioView view, string contentId) =>
        Descendants<Button>(Named<ContentControl>(view, "PaneHost")).Single(button =>
            button.Tag as string == contentId && AutomationProperties.GetName(button) is "Close document tab" or "Close canvas tab");
    private static IEnumerable<string> Contents(StudioLayoutNode node)
    {
        foreach (var content in node.PaneContents) yield return content;
        if (node.First is not null) foreach (var content in Contents(node.First)) yield return content;
        if (node.Second is not null) foreach (var content in Contents(node.Second)) yield return content;
    }
    private static IEnumerable<StudioLayoutNode> Panes(StudioLayoutNode node)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane) yield return node;
        if (node.First is not null) foreach (var child in Panes(node.First)) yield return child;
        if (node.Second is not null) foreach (var child in Panes(node.Second)) yield return child;
    }

    private static void Check(Receipt receipt, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        receipt.Assertions.Add(message);
    }
    private static T Named<T>(FrameworkElement element, string name) where T : class =>
        element.FindName(name) as T ?? throw new InvalidOperationException("Missing production layout element: " + name);
    private static Rect Bounds(FrameworkElement child, Visual ancestor) => child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
    private static bool Overlaps(Rect left, Rect right)
    {
        left.Intersect(right);
        return !left.IsEmpty && left.Width > 1 && left.Height > 1;
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
    private static void Layout(FrameworkElement view, Size size)
    {
        view.Width = size.Width; view.Height = size.Height;
        view.Measure(size); view.Arrange(new Rect(new Point(), size)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
    }
    private static void Render(FrameworkElement view, string path, Size size, double scale, Receipt receipt)
    {
        Layout(view, size);
        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
        receipt.Renders.Add(path);
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
        }) { IsBackground = true, Name = "Studio panel layout acceptance" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void LoadResources(string repository)
    {
        if (Application.Current is not null)
        {
            if (!Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Run panel verification in its own process.");
            return;
        }
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
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
        public string? Failure { get; set; }
    }
}
