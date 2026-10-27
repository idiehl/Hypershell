using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

// Preserve the existing CLI alias while testing the replacement against real source
// XAML/styles. This isolated window does not construct application sessions or WebViews.
internal static class WorkspacePopupAnchorVerifier
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Verify()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyCore(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "In-window sidebar controls verifier" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyCore()
    {
        var checks = 0;
        void Require(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException(message);
        }
        var foreground = GetForegroundWindow();
        var sourceDirectory = FindSourceDirectory();
        LoadResources(sourceDirectory);
        var surface = CreateSurface(XDocument.Load(Path.Combine(sourceDirectory, "MainWindow.xaml")));
        var left = (Border)surface.FindName("FileExplorerEdgeRevealHost");
        var right = (Border)surface.FindName("CommandBuilderEdgeRevealHost");
        var leftButton = (Button)surface.FindName("FileExplorerReopenButton");
        var rightButton = (Button)surface.FindName("CommandBuilderReopenButton");
        var tabs = (TabControl)surface.FindName("WorkspaceTabs");
        var state = new SidebarState();
        surface.DataContext = state;
        tabs.Items.Add(new TabItem { Header = "Layout verification", Content = new Border() });
        tabs.SelectedIndex = 0;
        var dock = new DockPanel { LastChildFill = true };
        var leftPanel = new Border { Width = 170, Background = Brushes.Transparent };
        var rightPanel = new Border { Width = 180, Background = Brushes.Transparent };
        DockPanel.SetDock(leftPanel, Dock.Left); DockPanel.SetDock(rightPanel, Dock.Right);
        dock.Children.Add(leftPanel); dock.Children.Add(rightPanel); dock.Children.Add(surface);
        void UpdatePanels(object? sender, PropertyChangedEventArgs args)
        {
            leftPanel.Visibility = state.ShouldShowFileExplorerReopenButton ? Visibility.Collapsed : Visibility.Visible;
            rightPanel.Visibility = state.ShouldShowCommandBuilderReopenButton ? Visibility.Collapsed : Visibility.Visible;
        }
        state.PropertyChanged += UpdatePanels;
        UpdatePanels(null, new PropertyChangedEventArgs(null));
        var owner = new Window
        {
            Title = "Invisible in-window sidebar fixture", Content = dock,
            Left = 80, Top = 70, Width = 1100, Height = 600,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            UseLayoutRounding = true, SnapsToDevicePixels = true, IsHitTestVisible = false
        };
        void CheckLayout(string label)
        {
            Pump();
            var presentation = PresentationSource.FromVisual(owner);
            Require(presentation is HwndSource, $"{label}: owner HWND was not created.");
            Require(!Descendants<Popup>(surface).Any(), $"{label}: sidebar surface still contains a popup.");
            foreach (var (host, button, shown, isRight) in new[]
            {
                (left, leftButton, state.ShouldShowFileExplorerReopenButton, false),
                (right, rightButton, state.ShouldShowCommandBuilderReopenButton, true)
            })
            {
                Require(ReferenceEquals(PresentationSource.FromVisual(button), presentation),
                    $"{label}: sidebar button has a separate presentation source/HWND.");
                Require(host.Visibility == (shown ? Visibility.Visible : Visibility.Collapsed),
                    $"{label}: sidebar visibility stopped following its source.");
                Require(BindingOperations.GetBindingExpression(host, UIElement.VisibilityProperty) is { ParentBinding.Mode: BindingMode.OneWay },
                    $"{label}: one-way sidebar visibility binding was lost.");
                if (!shown) continue;
                var hostBounds = host.TransformToAncestor(surface).TransformBounds(new Rect(host.RenderSize));
                var buttonBounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
                var edge = isRight ? surface.ActualWidth : 0;
                Require(Near(isRight ? hostBounds.Right : hostBounds.Left, edge) && Near(hostBounds.Top, 0),
                    $"{label}: sidebar host drifted from the workspace title edge: {hostBounds}.");
                Require(buttonBounds.Width >= 28 && buttonBounds.Height >= 28 && buttonBounds.Top >= 0
                    && buttonBounds.Bottom <= 46 && buttonBounds.Left >= 0 && buttonBounds.Right <= surface.ActualWidth + .5,
                    $"{label}: button escapes the title region: {buttonBounds}.");
                Require(hostBounds.Contains(buttonBounds), $"{label}: button escapes its clipped host.");
                var screen = button.PointToScreen(new Point());
                var expected = surface.PointToScreen(buttonBounds.TopLeft);
                Require(Near(screen.X, expected.X) && Near(screen.Y, expected.Y),
                    $"{label}: screen coordinates no longer follow WPF layout.");
            }
            var content = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
            var contentBounds = content.TransformToAncestor(surface).TransformBounds(new Rect(content.RenderSize));
            Require(contentBounds.Top >= 46 && Near(contentBounds.Left, 0) && Near(contentBounds.Right, surface.ActualWidth),
                $"{label}: toggles intrude on or narrow the native document region: {contentBounds}.");
        }
        try
        {
            owner.Show(); CheckLayout("initial");
            foreach (var button in new[] { leftButton, rightButton })
            {
                Require(button.Focusable && KeyboardNavigation.GetIsTabStop(button), "A reopen control is no longer keyboard accessible.");
                Require(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) && button.ToolTip is not null,
                    "A reopen control lost its accessible name or tooltip.");
                Require(button.Command?.CanExecute(button.CommandParameter) == true, "A reopen command is not enabled.");
                var peer = new ButtonAutomationPeer(button);
                Require(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "The reopen button has no accessible Invoke action.");
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke(); Pump();
            }
            Require(!state.ShouldShowFileExplorerReopenButton && !state.ShouldShowCommandBuilderReopenButton
                && state.LeftInvocations == 1 && state.RightInvocations == 1,
                "Invoking each actual button did not execute its bound sidebar command.");
            CheckLayout("both opened through automation");
            foreach (var width in new[] { 720d, 1440d, 930d, 1800d, 790d, 1100d })
            {
                owner.Width = width; owner.Height = width < 1000 ? 450 : 760;
                owner.Left = 60 + width % 91; owner.Top = 50 + width % 67;
                foreach (var flags in new[] { 0, 1, 3, 2, 0, 3 })
                {
                    state.Set((flags & 1) != 0, (flags & 2) != 0);
                    CheckLayout($"width={width}, flags={flags}");
                }
            }
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            _ = ShowWindow(ownerHandle, 7); Pump(); // SW_SHOWMINNOACTIVE
            Require(owner.WindowState == WindowState.Minimized, "The fixture did not minimize.");
            Require(ReferenceEquals(PresentationSource.FromVisual(leftButton), PresentationSource.FromVisual(owner)),
                "Minimization separated a button from its owner HWND.");
            state.Set(false, false); state.Set(true, true); Pump();
            _ = ShowWindow(ownerHandle, 4); // SW_SHOWNOACTIVATE
            CheckLayout("restore after toggling while minimized");
            owner.Hide(); Pump();
            Require(!leftButton.IsVisible && !rightButton.IsVisible, "Hidden owner retained a visible sidebar button.");
            owner.Show(); CheckLayout("show after hide");
            foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
            {
                surface.LayoutTransform = new ScaleTransform(scale, scale);
                CheckLayout($"layout scale={scale}");
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale),
                    (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                Require(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "Scaled rendering did not produce a bitmap.");
            }
            surface.LayoutTransform = Transform.Identity; CheckLayout("scale reset");
            var previewPath = Environment.GetEnvironmentVariable("HYPERSHELL_SIDEBAR_PREVIEW");
            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                // Render the actual styled chrome, never capture the user's desktop.
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), 80, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath))!);
                using var stream = File.Create(previewPath); encoder.Save(stream);
                Console.WriteLine($"WORKSPACE_SIDEBAR_PREVIEW {Path.GetFullPath(previewPath)}");
            }
            owner.Close(); Pump();
            Require(PresentationSource.FromVisual(leftButton) is null && PresentationSource.FromVisual(rightButton) is null,
                "Closing the owner left a sidebar control connected to a presentation source.");
            Require(GetForegroundWindow() == foreground, "The invisible fixture changed the foreground window.");
            Console.WriteLine($"WORKSPACE_POPUP_ANCHOR_OK checks={checks}|actual-source-xaml|single-owner-hwnd|resize-move-toggle|minimize-restore|hidden-owner|close-cleanup|automation-invoke|keyboard-accessible|nonactivating");
            Console.WriteLine("WORKSPACE_SIDEBAR_SCALE_SCOPE 100/125/150/200 percent layout and bitmap rendering; physical monitor DPI changes are not simulated.");
        }
        finally { state.PropertyChanged -= UpdatePanels; owner.Close(); }
    }

    private static Grid CreateSurface(XDocument source)
    {
        var original = source.Descendants(Presentation + "Grid").Single(element => (string?)element.Attribute(Xaml + "Name") == "WorkspaceSurface");
        var root = new XElement(original); CopyNamespaces(source.Root!, root);
        var tabs = root.Descendants(Presentation + "TabControl").Single(element => (string?)element.Attribute(Xaml + "Name") == "WorkspaceTabs");
        tabs.Attribute("ItemsSource")?.Remove(); tabs.Attribute("SelectedItem")?.Remove();
        tabs.Elements(Presentation + "TabControl.Resources").Remove();
        tabs.Elements(Presentation + "TabControl.ItemContainerStyle").Remove();
        tabs.Elements(Presentation + "TabControl.ItemTemplate").Remove();
        root.Descendants().Where(element => element.Name.LocalName.EndsWith(".ContextMenu", StringComparison.Ordinal)).Remove();
        var events = new HashSet<string>(StringComparer.Ordinal)
        { "Click", "SelectionChanged", "ContextMenuOpening", "MouseLeftButtonDown", "PreviewKeyDown", "LostKeyboardFocus", "Loaded", "Unloaded", "DataContextChanged" };
        root.DescendantsAndSelf().Attributes().Where(attribute => events.Contains(attribute.Name.LocalName)).Remove();
        Qualify(root); return (Grid)XamlReader.Parse(root.ToString());
    }
    private static void LoadResources(string sourceDirectory)
    {
        if (Application.Current is not null) throw new InvalidOperationException("Run the sidebar verifier in its dedicated process.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var source = XDocument.Load(Path.Combine(sourceDirectory, "App.xaml"));
        var resources = new XElement(Presentation + "ResourceDictionary"); CopyNamespaces(source.Root!, resources);
        resources.Add(source.Root!.Element(Presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        resources.Descendants().Attributes("Click").Remove(); Qualify(resources);
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
    }
    private static void CopyNamespaces(XElement source, XElement target)
    {
        foreach (var declaration in source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (target.Attribute(declaration.Name) is null) target.Add(new XAttribute(declaration));
    }
    private static void Qualify(XElement root)
    {
        foreach (var declaration in root.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (declaration.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !declaration.Value.Contains(";assembly=", StringComparison.Ordinal))
                declaration.Value += ";assembly=Hypershell";
        foreach (var element in root.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static string FindSourceDirectory()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "src", "Hypershell");
                if (File.Exists(Path.Combine(path, "MainWindow.xaml"))) return path;
            }
        throw new FileNotFoundException("Cannot find MainWindow.xaml from the verifier working directory.");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) <= .6;
    public sealed class SidebarState : INotifyPropertyChanged
    {
        public bool ShouldShowFileExplorerReopenButton { get; private set; } = true;
        public bool ShouldShowCommandBuilderReopenButton { get; private set; } = true;
        public int LeftInvocations { get; private set; }
        public int RightInvocations { get; private set; }
        public ICommand ToggleFileExplorerCommand { get; }
        public ICommand ToggleCommandBuilderCommand { get; }
        public SidebarState()
        {
            ToggleFileExplorerCommand = new ActionCommand(() => { LeftInvocations++; Set(!ShouldShowFileExplorerReopenButton, ShouldShowCommandBuilderReopenButton); });
            ToggleCommandBuilderCommand = new ActionCommand(() => { RightInvocations++; Set(ShouldShowFileExplorerReopenButton, !ShouldShowCommandBuilderReopenButton); });
        }
        public void Set(bool left, bool right)
        {
            ShouldShowFileExplorerReopenButton = left; ShouldShowCommandBuilderReopenButton = right;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsVisualPageStudioActive => false;
        public bool ShowSaveStatusInTitleBar => false;
        public bool HasProfileAvatarImage => false;
        public string ProfileAvatarInitials => "HS";
        public string ProfileSyncTitleToolTip => "Layout verification";
    }
    private sealed class ActionCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
