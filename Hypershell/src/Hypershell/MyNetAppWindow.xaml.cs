using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Hypershell;

public partial class MyNetAppWindow : Window
{
    private readonly BrowserTabViewModel _browser;
    private readonly AtlasMyNetAppDefinition _definition;
    private readonly SettingsService _settings;
    private CoreWebView2? _popupCore;
    private HwndSource? _windowSource;
    private CoreWebView2? _meridianCore;
    private CoreWebView2? _hyperlibCore;
    private readonly string _appOrigin;
    private bool _navigationDispatchPending;

    public MyNetAppWindow(
        AtlasMyNetOpenAppRequest request,
        SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !NetworkAddressPolicy.IsLocalMachineUri(uri))
        {
            throw new InvalidOperationException(
                "MyNet app windows require an HTTP or HTTPS address on this computer.");
        }

        InitializeComponent();
        _definition = request.Definition.Clone();
        _settings = settings;
        AppId = request.Definition.Id;
        _appOrigin = uri.GetLeftPart(UriPartial.Authority);
        var policy = new BrowserNavigationPolicy(
            request.Definition.Id,
            request.Definition.DisplayName,
            request.Url,
            request.Definition.AllowExternalResources);
        _browser = new BrowserTabViewModel(
            request.Url,
            navigationPolicy: policy,
            userDataFolder: GetBrowserDataDirectory(settings, AppId),
            isInPrivateModeEnabled: true,
            showNavigationToolbar: !IsHyperlib && request.Definition.ShowNavigationToolbar);
        DataContext = _browser;
        if (string.Equals(AppId, "atlas.meridian", StringComparison.OrdinalIgnoreCase))
        {
            AppIdentity.Visibility = Visibility.Collapsed;
            MeridianNavigation.Visibility = Visibility.Visible;
            _browser.UseDocumentTitle = true;
            _browser.BrowserView.ConfigureAppHostAsync = ConfigureMeridianChromeAsync;
            _browser.BrowserView.ManagedWebsiteRequested = OpenMeridianWebsite;
        }
        else if (IsHyperlib)
        {
            AppIdentity.Visibility = Visibility.Collapsed;
            MeridianNavigation.Visibility = Visibility.Visible;
            FileMenuButton.Visibility = Visibility.Collapsed;
            ToolsMenuButton.Visibility = Visibility.Collapsed;
            HyperlibIdentity.Visibility = Visibility.Visible;
            HyperlibEditToggle.Visibility = Visibility.Visible;
            PrivacyIndicator.Visibility = Visibility.Collapsed;
            PrivacyColumn.Width = new GridLength(0);
            ((RowDefinition)_browser.BrowserView.FindName("LoadingIndicatorRow")).Height = new GridLength(0);
            _browser.BrowserView.ConfigureAppHostAsync = ConfigureHyperlibChromeAsync;
        }
        BrowserHost.Content = _browser.BrowserView;
        _browser.BrowserView.LocalNewWindowRequested += LocalNewWindowRequested;
        TrySetAppIcon(request.Definition.IconPath);
    }

    public string AppId { get; }

    private bool IsHyperlib => string.Equals(
        AppId, "atlas.mynet.hyperlib", StringComparison.OrdinalIgnoreCase);

    private async Task ConfigureHyperlibChromeAsync(CoreWebView2 core)
    {
        _hyperlibCore = core;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += HyperlibChromeStateReceived;
        core.NavigationStarting += HyperlibNavigationStarting;
        // Install the marker before the app script and CSS run, including reloads.
        // A document-created script can precede the root element itself.
        var script = "(() => { if (window !== window.top || window.location.origin !== "
            + JsonSerializer.Serialize(_appOrigin)
            + ") return; window.__hyperlibNativeChrome = true; "
            + "const mark = () => { if (!document.documentElement) return false; "
            + "document.documentElement.classList.add('hyperlib-native-chrome'); return true; }; "
            + "if (!mark()) { const observer = new MutationObserver(() => { "
            + "if (mark()) observer.disconnect(); }); observer.observe(document, {childList:true}); } "
            + "window.dispatchEvent(new Event('hyperlib:native-chrome')); })();";
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        await core.ExecuteScriptAsync(script);
    }

    private bool IsHyperlibOrigin(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && string.Equals(uri.GetLeftPart(UriPartial.Authority), _appOrigin,
            StringComparison.OrdinalIgnoreCase);

    private void HyperlibNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Cancel) return;
        HyperlibEditToggle.IsEnabled = false;
        HyperlibEditToggle.IsChecked = false;
    }

    private void HyperlibChromeStateReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // Accept only presentation state from the top-level local app. Frame
        // messages and arbitrary native commands are not part of this bridge.
        if (_hyperlibCore is not { } core || !IsHyperlibOrigin(args.Source)
            || !IsHyperlibOrigin(core.Source))
        {
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var state = document.RootElement;
            if (state.ValueKind != JsonValueKind.Object
                || !state.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "hyperlib:chrome-state"
                || !state.TryGetProperty("editing", out var editing)
                || editing.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !state.TryGetProperty("canEdit", out var canEdit)
                || canEdit.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return;
            }
            HyperlibEditToggle.IsChecked = editing.GetBoolean();
            HyperlibEditToggle.IsEnabled = canEdit.GetBoolean();
        }
        catch (JsonException)
        {
            // Malformed page messages cannot affect the window's controls.
        }
    }

    private async void HyperlibEditClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_hyperlibCore is not { } core || !IsHyperlibOrigin(core.Source))
        {
            return;
        }
        var enabled = HyperlibEditToggle.IsChecked == true;
        await DispatchHyperlibEditAsync(core, enabled);
    }

    private async Task DispatchHyperlibEditAsync(CoreWebView2 core, bool enabled)
    {
        try
        {
            await core.ExecuteScriptAsync(
                "if (window.location.origin === " + JsonSerializer.Serialize(_appOrigin)
                + ") window.dispatchEvent(new CustomEvent('hyperlib:edit-mode',"
                + "{detail:{enabled:" + (enabled ? "true" : "false") + "}}));");
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ObjectDisposedException or COMException)
        {
            HyperlibEditToggle.IsEnabled = false;
            _browser.ReportError($"Edit Mode could not change: {exception.Message}");
        }
    }

    private async Task ConfigureMeridianChromeAsync(CoreWebView2 core)
    {
        _meridianCore = core;
        var script = "(() => { if (window === window.top && window.location.origin === "
            + JsonSerializer.Serialize(_appOrigin)
            + ") { window.__meridianNativeChrome = true; window.__meridianWebsiteBrowser = true; "
            + "window.dispatchEvent(new Event('meridian:native-chrome')); } })();";
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        await core.ExecuteScriptAsync(script);
    }

    private void OpenMeridianWebsite(string address)
    {
        // Only the active local Meridian document owns this callback. The
        // remote browser has a different profile and no local-app bridge.
        if (_meridianCore is not { } core
            || !Uri.TryCreate(core.Source, UriKind.Absolute, out var source)
            || !string.Equals(source.GetLeftPart(UriPartial.Authority), _appOrigin,
                StringComparison.OrdinalIgnoreCase)
            || !MeridianBrowserWindow.IsWebsiteAddress(address)) return;
        try
        {
            var website = new MeridianBrowserWindow(address, _settings)
            {
                Owner = this,
                Icon = Icon,
                ShowInTaskbar = ShowInTaskbar,
                ShowActivated = ShowActivated
            };
            if (!ShowInTaskbar)
            {
                website.WindowStartupLocation = WindowStartupLocation.Manual;
                website.Left = Left;
                website.Top = Top;
            }
            website.Show();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ObjectDisposedException or COMException)
        {
            _browser.ReportError($"The website window could not open: {exception.Message}");
        }
    }

    private async void CanvasBackClick(object sender, RoutedEventArgs eventArgs) =>
        await NavigateCanvasHistoryAsync("back");

    private async void CanvasForwardClick(object sender, RoutedEventArgs eventArgs) =>
        await NavigateCanvasHistoryAsync("forward");

    private async void WindowPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        var key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;
        if (!ShouldHandleCanvasHistoryShortcut(key, Keyboard.Modifiers, BrowserHasKeyboardFocus()))
        {
            return;
        }
        eventArgs.Handled = true;
        await NavigateCanvasHistoryAsync(key == Key.Left ? "back" : "forward");
    }

    internal bool ShouldHandleCanvasHistoryShortcut(
        Key key, ModifierKeys modifiers, bool contentHasKeyboardFocus) =>
        (_meridianCore is not null || _hyperlibCore is not null)
        && !contentHasKeyboardFocus
        && modifiers == ModifierKeys.Alt
        && key is Key.Left or Key.Right;

    private bool BrowserHasKeyboardFocus()
    {
        if (BrowserHost.IsKeyboardFocusWithin)
        {
            return true;
        }
        // WebView2's native child can own keyboard focus before WPF's inherited
        // focus state catches up. Leave those keys to the document, where node
        // disclosure takes precedence over the canvas-history shortcut.
        var focused = GetFocus();
        var window = new WindowInteropHelper(this).Handle;
        return focused != IntPtr.Zero && focused != window && IsChild(window, focused);
    }

    private async Task NavigateCanvasHistoryAsync(string direction)
    {
        if (_hyperlibCore is { } hyperlib)
        {
            if (direction == "back" && hyperlib.CanGoBack) hyperlib.GoBack();
            else if (direction == "forward" && hyperlib.CanGoForward) hyperlib.GoForward();
            return;
        }
        if (_navigationDispatchPending || _meridianCore is not { } core
            || (direction == "back" ? !core.CanGoBack : !core.CanGoForward))
        {
            return;
        }
        _navigationDispatchPending = true;
        try
        {
            await DispatchMeridianEventAsync(core, "meridian:navigate", "direction", direction);
        }
        finally
        {
            _navigationDispatchPending = false;
        }
    }

    private void FileMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        CanvasFileMenu.PlacementTarget = FileMenuButton;
        CanvasFileMenu.IsOpen = true;
    }

    private async void FileCommandClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_meridianCore is not { } core
            || sender is not MenuItem { Tag: string command }
            || command is not ("save" or "save-as" or "checkpoint" or "duplicate"
                or "import" or "history" or "preview" or "publish" or "export"))
        {
            return;
        }
        await DispatchMeridianEventAsync(core, "meridian:file-command", "command", command);
    }

    private void ToolsMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        CanvasToolsMenu.PlacementTarget = ToolsMenuButton;
        CanvasToolsMenu.IsOpen = true;
    }

    private async void ToolCommandClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_meridianCore is not { } core
            || sender is not MenuItem { Tag: string command }
            || command is not ("modules" or "composition"))
        {
            return;
        }
        await DispatchMeridianEventAsync(core, "meridian:tool-command", "command", command);
    }

    private async Task DispatchMeridianEventAsync(
        CoreWebView2 core, string eventName, string detailKey, string detailValue)
    {
        try
        {
            // Commands travel only from this native menu to the matching local
            // app document. No general web-message or native-execution API is exposed.
            await core.ExecuteScriptAsync(
                "if (window.location.origin === " + JsonSerializer.Serialize(_appOrigin)
                + ") window.dispatchEvent(new CustomEvent(" + JsonSerializer.Serialize(eventName)
                + ",{detail:{" + JsonSerializer.Serialize(detailKey) + ":"
                + JsonSerializer.Serialize(detailValue) + "}}));");
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ObjectDisposedException
                                         or COMException)
        {
            _browser.ReportError($"The Work command could not run: {exception.Message}");
        }
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessage);
    }

    private IntPtr WindowMessage(
        IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int getMinMaxInfo = 0x0024;
        if (message == getMinMaxInfo && lParam != IntPtr.Zero)
        {
            // Custom chrome uses the entire client rectangle. Keep that rectangle
            // inside this monitor's work area so the taskbar cannot obscure content.
            var monitor = MonitorFromWindow(window, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                bounds.MaxSize.X = info.Work.Right - info.Work.Left;
                bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                Marshal.StructureToPtr(bounds, lParam, false);
                // Let WPF's own handler retain the window's minimum and maximum
                // resize constraints after supplying the monitor work-area bounds.
            }
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private async void LocalNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        if (sender is not CoreWebView2 opener)
        {
            return;
        }

        using var deferral = eventArgs.GetDeferral();
        MyNetAppWindow? popup = null;
        try
        {
            popup = new MyNetAppWindow(
                new AtlasMyNetOpenAppRequest(_definition, eventArgs.Uri), _settings);
            popup._browser.UseDocumentTitle = true;
            popup._browser.BrowserView.ConfigurePopup(opener.Environment);
            popup.ShowInTaskbar = ShowInTaskbar;
            popup.ShowActivated = ShowActivated;
            if (!ShowInTaskbar)
            {
                popup.WindowStartupLocation = WindowStartupLocation.Manual;
                popup.Left = Left;
                popup.Top = Top;
            }
            popup.Show();
            var core = await popup._browser.BrowserView.GetInitializedCoreAsync()
                .WaitAsync(TimeSpan.FromSeconds(30));
            popup._popupCore = core;
            core.WindowCloseRequested += popup.PopupWindowCloseRequested;
            eventArgs.NewWindow = core;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ObjectDisposedException
                                         or COMException
                                         or TimeoutException)
        {
            popup?.Close();
            _browser.ReportError($"The app window could not open: {exception.Message}");
        }
    }

    private void PopupWindowCloseRequested(object? sender, object eventArgs) => Close();

    internal static string GetBrowserDataDirectory(
        SettingsService settings,
        string appId)
    {
        // App IDs are user-authored manifest data, so never use them as a path.
        var safeId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(appId.ToUpperInvariant())));
        return Path.Combine(settings.AtlasMyNetDirectory, "webview2", safeId);
    }

    private void TrySetAppIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(Path.GetFullPath(iconPath));
            var image = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            image.Freeze();
            Icon = image;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or ArgumentException
                                         or NotSupportedException
                                         or FileFormatException)
        {
            // A missing or unreadable custom icon must not prevent launch.
        }
    }

    private void MinimizeClick(object sender, RoutedEventArgs eventArgs) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeClick(object sender, RoutedEventArgs eventArgs)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs eventArgs) =>
        Close();

    private void WindowStateChanged(object? sender, EventArgs eventArgs)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void WindowClosed(object? sender, EventArgs eventArgs)
    {
        _windowSource?.RemoveHook(WindowMessage);
        _windowSource = null;
        _browser.BrowserView.LocalNewWindowRequested -= LocalNewWindowRequested;
        _browser.BrowserView.ManagedWebsiteRequested = null;
        if (_popupCore is not null)
        {
            _popupCore.WindowCloseRequested -= PopupWindowCloseRequested;
            _popupCore = null;
        }
        BrowserHost.Content = null;
        _browser.BrowserView.ConfigureAppHostAsync = null;
        _meridianCore = null;
        if (_hyperlibCore is not null)
        {
            _hyperlibCore.WebMessageReceived -= HyperlibChromeStateReceived;
            _hyperlibCore.NavigationStarting -= HyperlibNavigationStarting;
            _hyperlibCore = null;
        }
        _browser.Dispose();
    }
}
