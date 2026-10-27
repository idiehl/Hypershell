using Hypershell.ViewModels;
using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly string? _userDataFolder;
    private readonly bool _isInPrivateModeEnabled;
    private CoreWebView2Environment? _popupEnvironment;
    private Task? _initializationTask;
    private ulong _activeNavigationId;
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isDisposed;

    public BrowserTabView(
        BrowserTabViewModel viewModel,
        string? userDataFolder = null,
        bool isInPrivateModeEnabled = false)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _userDataFolder = string.IsNullOrWhiteSpace(userDataFolder)
            ? null
            : Path.GetFullPath(userDataFolder);
        _isInPrivateModeEnabled = isInPrivateModeEnabled;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    public event EventHandler<CoreWebView2NewWindowRequestedEventArgs>? LocalNewWindowRequested;

    // A managed host may own its website windows. Ordinary browser tabs and
    // other local apps retain their existing external-link policy.
    internal Action<string>? ManagedWebsiteRequested { get; set; }

    // A managed app can register its document-created scripts before any page
    // loads. This remains a host callback, not a page-to-host message channel.
    internal Func<CoreWebView2, Task>? ConfigureAppHostAsync { get; set; }

    internal void ConfigurePopup(CoreWebView2Environment environment)
    {
        if (_initializationTask is not null)
        {
            throw new InvalidOperationException("Configure a pop-up before its browser starts.");
        }
        _popupEnvironment = environment;
    }

    internal async Task<CoreWebView2> GetInitializedCoreAsync()
    {
        await EnsureBrowserInitializedAsync();
        return Browser.CoreWebView2
            ?? throw new InvalidOperationException("The app window browser could not start.");
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await EnsureBrowserInitializedAsync();
    }

    private Task EnsureBrowserInitializedAsync() =>
        _initializationTask ??= InitializeBrowserAsync();

    private async Task InitializeBrowserAsync()
    {
        if (_isInitialized || _isInitializing || _isDisposed)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var environment = _popupEnvironment ?? (_userDataFolder is null
                ? await GetSharedEnvironmentAsync()
                : await CreateEnvironmentAsync(_userDataFolder));
            if (_isDisposed)
            {
                return;
            }

            if (_isInPrivateModeEnabled)
            {
                var controllerOptions = environment
                    .CreateCoreWebView2ControllerOptions();
                controllerOptions.IsInPrivateModeEnabled = true;
                await Browser.EnsureCoreWebView2Async(
                    environment,
                    controllerOptions);
            }
            else
            {
                await Browser.EnsureCoreWebView2Async(environment);
            }
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
            if (ConfigureAppHostAsync is { } configureAppHost)
            {
                await configureAppHost(core);
                if (_isDisposed)
                {
                    return;
                }
            }
            _isInitialized = true;
            // WebView2 attaches the opener to this controller before navigating it.
            if (_popupEnvironment is null)
            {
                Navigate(_viewModel.InitialAddress);
            }
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

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync(
        string? isolatedUserDataFolder = null)
    {
        var userDataFolder = isolatedUserDataFolder ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Hypershell",
            "browser");
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
    }

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
        if (_viewModel.ShowNavigationToolbar
            && Keyboard.Modifiers == ModifierKeys.Control && eventArgs.Key == Key.L)
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
                "Atlas MyNet blocked a non-web navigation from this local app.");
            return;
        }

        if (ShouldOpenExternally(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            if (ManagedWebsiteRequested is { } openWebsite) openWebsite(eventArgs.Uri);
            else ConfirmAndOpenExternal(eventArgs.Uri);
            return;
        }

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
                "Atlas MyNet blocked a non-web pop-up from this local app.");
        }
        else if (ShouldOpenExternally(eventArgs.Uri))
        {
            if (ManagedWebsiteRequested is { } openWebsite) openWebsite(eventArgs.Uri);
            else ConfirmAndOpenExternal(eventArgs.Uri);
        }
        else if (_viewModel.IsManagedApp
                 && Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri)
                 && uri.Scheme is "http" or "https"
                 && NetworkAddressPolicy.IsLocalMachineUri(uri)
                 && LocalNewWindowRequested is not null)
        {
            LocalNewWindowRequested.Invoke(sender, eventArgs);
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
                "Blocked by Atlas MyNet local-only policy",
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Loaded -= OnLoaded;
        if (Browser.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
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
