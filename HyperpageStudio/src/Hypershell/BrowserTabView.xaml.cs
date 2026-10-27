using Hypershell.ViewModels;
using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell;

public partial class BrowserTabView : UserControl, IDisposable
{
    private static readonly object EnvironmentGate = new();
    private static Task<CoreWebView2Environment>? _sharedEnvironmentTask;

    private readonly BrowserTabViewModel _viewModel;
    private ulong _activeNavigationId;
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isDisposed;
    private ulong _hyperlibEventNavigationId;

    public BrowserTabView(BrowserTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await EnsureBrowserInitializedAsync();
    }

    private async Task EnsureBrowserInitializedAsync()
    {
        if (_isInitialized || _isInitializing || _isDisposed)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var environment = await GetSharedEnvironmentAsync();
            await Browser.EnsureCoreWebView2Async(environment);
            if (_isDisposed || Browser.CoreWebView2 is null)
            {
                return;
            }

            var core = Browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            if (_viewModel.HyperlibBridge is { } hyperlib)
            {
                core.WebMessageReceived += OnHyperlibWebMessageReceived;
                hyperlib.EventJson += OnHyperlibEvent;
            }
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += OnSourceChanged;
            core.DocumentTitleChanged += OnDocumentTitleChanged;
            core.HistoryChanged += OnHistoryChanged;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ProcessFailed += OnBrowserProcessFailed;
            if (_viewModel.NavigationPolicy is not null)
            {
                core.AddWebResourceRequestedFilter(
                    "*",
                    CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResourceRequested;
            }
            _isInitialized = true;
            Navigate(_viewModel.InitialAddress);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or NotSupportedException
                                         or COMException)
        {
            _viewModel.ReportError(
                $"The embedded browser could not start: {exception.Message}");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private static async Task<CoreWebView2Environment>
        GetSharedEnvironmentAsync()
    {
        Task<CoreWebView2Environment> environmentTask;
        lock (EnvironmentGate)
        {
            environmentTask = _sharedEnvironmentTask ??=
                CreateEnvironmentAsync();
        }

        try
        {
            return await environmentTask;
        }
        catch
        {
            lock (EnvironmentGate)
            {
                if (ReferenceEquals(_sharedEnvironmentTask, environmentTask))
                {
                    _sharedEnvironmentTask = null;
                }
            }

            throw;
        }
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataFolder = ResolveBrowserUserDataDirectory();
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
    }

    internal static string ResolveBrowserUserDataDirectory() =>
        Path.Combine(new SettingsService().ApplicationDataDirectory, "browser");

    private void Navigate(string? input)
    {
        if (!_isInitialized || Browser.CoreWebView2 is null)
        {
            return;
        }

        var address = BrowserAddressService.Normalize(input);
        _viewModel.AddressText = address;
        Browser.CoreWebView2.Navigate(address);
    }

    public void NavigateTo(string? input)
    {
        if (!_isInitialized || Browser.CoreWebView2 is null)
        {
            _viewModel.AddressText = input?.Trim() ?? string.Empty;
            return;
        }

        Navigate(input);
    }

    internal const string StudioPageCloseScript = """
        (() => {
          const close = window.__hypershellStudioClose = { state: "pending" };
          (async () => {
            try {
              if (typeof flushDraft !== "function" || typeof app !== "object") { close.state = "unavailable"; return; }
              await flushDraft();
              await app.mutation;
              close.state = app.draft || app.frameDirty || app.saving ? "blocked" : "ready";
            } catch { close.state = "blocked"; }
          })();
        })();
        """;

    public async Task<bool> TryPrepareForStudioCloseAsync()
    {
        if (!_viewModel.IsStudioContentPage || !_isInitialized || _viewModel.HasError || Browser.CoreWebView2 is null)
            return true;
        Browser.IsEnabled = false;
        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync(StudioPageCloseScript);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!_isDisposed && DateTime.UtcNow < deadline)
            {
                var response = await Browser.CoreWebView2.ExecuteScriptAsync("window.__hypershellStudioClose?.state ?? 'unavailable'");
                var state = JsonSerializer.Deserialize<string>(response);
                if (state == "ready") return true;
                if (state is "blocked" or "unavailable") return false;
                await Task.Delay(100);
            }
            return false;
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or JsonException)
        {
            return false;
        }
        finally { if (!_isDisposed) Browser.IsEnabled = true; }
    }

    private void BackClick(object sender, RoutedEventArgs eventArgs)
    {
        if (Browser.CanGoBack)
        {
            Browser.GoBack();
        }
    }

    private void ForwardClick(object sender, RoutedEventArgs eventArgs)
    {
        if (Browser.CanGoForward)
        {
            Browser.GoForward();
        }
    }

    private void HomeClick(object sender, RoutedEventArgs eventArgs) =>
        Navigate(_viewModel.HomeAddress);

    private void RefreshOrStopClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_isInitialized)
        {
            return;
        }

        if (_viewModel.IsLoading)
        {
            Browser.Stop();
        }
        else
        {
            Browser.Reload();
        }
    }

    private void GoClick(object sender, RoutedEventArgs eventArgs) =>
        Navigate(_viewModel.AddressText);

    private void AddressTextBoxKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter)
        {
            return;
        }

        Navigate(_viewModel.AddressText);
        eventArgs.Handled = true;
    }

    private void AddressTextBoxGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs) =>
        AddressTextBox.SelectAll();

    private void AddressTextBoxPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (AddressTextBox.IsKeyboardFocusWithin)
        {
            return;
        }

        eventArgs.Handled = true;
        AddressTextBox.Focus();
        AddressTextBox.SelectAll();
    }

    private void BrowserTabPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && eventArgs.Key == Key.L)
        {
            AddressTextBox.Focus();
            AddressTextBox.SelectAll();
            eventArgs.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control
            && eventArgs.Key == Key.R
            && _isInitialized)
        {
            Browser.Reload();
            eventArgs.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt && eventArgs.Key == Key.Left
            && Browser.CanGoBack)
        {
            Browser.GoBack();
            eventArgs.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Alt && eventArgs.Key == Key.Right
            && Browser.CanGoForward)
        {
            Browser.GoForward();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Escape && _viewModel.IsLoading)
        {
            Browser.Stop();
            eventArgs.Handled = true;
        }
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (ShouldBlockManagedNavigation(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            _viewModel.ReportError(
                "MyNet blocked a non-web navigation from this local app.");
            return;
        }

        if (ShouldOpenExternally(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            ConfirmAndOpenExternal(eventArgs.Uri);
            return;
        }

        // A cancelled navigation leaves the current HyperLib document attached.
        // Only an accepted navigation may cancel requests or change bridge access.
        _viewModel.HyperlibBridge?.NavigationChanged();
        if (Browser.CoreWebView2 is { } bridgeCore)
            bridgeCore.Settings.IsWebMessageEnabled = _viewModel.HyperlibBridge?.AcceptsOrigin(eventArgs.Uri) == true;
        _activeNavigationId = eventArgs.NavigationId;
        _viewModel.BeginNavigation(eventArgs.Uri);
    }

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (eventArgs.NavigationId != _activeNavigationId)
        {
            return;
        }

        RefreshPageState();
        _viewModel.CompleteNavigation(
            eventArgs.IsSuccess,
            eventArgs.IsSuccess
                ? null
                : $"Navigation failed: {eventArgs.WebErrorStatus}.");
    }

    private void OnSourceChanged(
        object? sender,
        CoreWebView2SourceChangedEventArgs eventArgs) =>
        RefreshPageState();

    private void OnDocumentTitleChanged(object? sender, object eventArgs) =>
        RefreshPageState();

    private void OnHistoryChanged(object? sender, object eventArgs) =>
        RefreshPageState();

    private void RefreshPageState()
    {
        if (Browser.CoreWebView2 is not { } core)
        {
            return;
        }

        _viewModel.UpdatePageState(
            core.Source,
            core.DocumentTitle,
            core.CanGoBack,
            core.CanGoForward);
    }

    private void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (ShouldBlockManagedNavigation(eventArgs.Uri))
        {
            _viewModel.ReportError(
                "MyNet blocked a non-web pop-up from this local app.");
        }
        else if (ShouldOpenExternally(eventArgs.Uri))
        {
            ConfirmAndOpenExternal(eventArgs.Uri);
        }
        else
        {
            Navigate(eventArgs.Uri);
        }
    }

    private void OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (_viewModel.NavigationPolicy is not { } policy
            || !Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Scheme is "about" or "data" or "blob"
            || uri.Scheme is "http" or "https" or "ws" or "wss"
            && (policy.AllowExternalResources
                || NetworkAddressPolicy.IsLocalMachineUri(uri)))
        {
            return;
        }

        eventArgs.Response = Browser.CoreWebView2.Environment
            .CreateWebResourceResponse(
                Stream.Null,
                403,
                "Blocked by MyNet local-only policy",
                "Content-Type: text/plain");
    }

    private bool ShouldBlockManagedNavigation(string? address) =>
        _viewModel.NavigationPolicy is not null
        && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme is not ("http" or "https" or "about" or "data" or "blob");

    private bool ShouldOpenExternally(string? address) =>
        _viewModel.NavigationPolicy is not null
        && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !NetworkAddressPolicy.IsLocalMachineUri(uri);

    private void ConfirmAndOpenExternal(string address)
    {
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"{_viewModel.NavigationPolicy?.DisplayName ?? "This local app"} wants to open:\n\n"
            + address
            + "\n\nOpen it in your default browser?",
            "External link",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(address)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or Win32Exception)
        {
            _viewModel.ReportError(
                $"Windows could not open the external link: {exception.Message}");
        }
    }

    private void OnBrowserProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs) =>
        _viewModel.ReportError(
            $"The browser process stopped: {eventArgs.ProcessFailedKind}.");

    private async void OnHyperlibWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (_viewModel.HyperlibBridge is not { } bridge || Browser.CoreWebView2 is not { } core) return;
        var navigation = _activeNavigationId;
        _hyperlibEventNavigationId = navigation;
        var reply = await bridge.HandleAsync(eventArgs.Source, core.Source, eventArgs.WebMessageAsJson);
        if (!_isDisposed && reply is not null && navigation == _activeNavigationId && bridge.AcceptsOrigin(core.Source))
            core.PostWebMessageAsJson(reply);
    }

    private void OnHyperlibEvent(string json)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_isDisposed && _hyperlibEventNavigationId == _activeNavigationId
                && Browser.CoreWebView2 is { } core && _viewModel.HyperlibBridge?.AcceptsOrigin(core.Source) == true)
                core.PostWebMessageAsJson(json);
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Loaded -= OnLoaded;
        if (_viewModel.HyperlibBridge is { } bridge) bridge.EventJson -= OnHyperlibEvent;
        if (Browser.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.WebMessageReceived -= OnHyperlibWebMessageReceived;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.SourceChanged -= OnSourceChanged;
            core.DocumentTitleChanged -= OnDocumentTitleChanged;
            core.HistoryChanged -= OnHistoryChanged;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.ProcessFailed -= OnBrowserProcessFailed;
            if (_viewModel.NavigationPolicy is not null)
            {
                core.WebResourceRequested -= OnWebResourceRequested;
            }
        }

        Browser.Dispose();
    }
}
