using Hypershell.Extensibility;
using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell.Controls;

public partial class ExtensionPanelView : UserControl, IDisposable
{
    private readonly string _userDataDirectory;
    private readonly string _virtualHostName;
    private readonly ExtensionPanelMessageHost? _messageHost;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Point? _dragStartPoint;
    private bool _isCustomizationEnabled;
    private bool _isInitializing;
    private bool _isDisposed;

    public ExtensionPanelView(
        ExtensionPanelCandidate candidate,
        string webViewDataDirectory,
        string panelStateDirectory,
        IReadOnlyCollection<ExtensionCapability> approvedCapabilities,
        Func<ExtensionPanelHostContext> contextProvider)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(webViewDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(panelStateDirectory);
        ArgumentNullException.ThrowIfNull(approvedCapabilities);
        ArgumentNullException.ThrowIfNull(contextProvider);
        InitializeComponent();

        Candidate = candidate;
        PanelTitleText.Text = candidate.Registration.Contribution.DisplayName;
        _virtualHostName = ExtensionPanelMessagingContract.CreateVirtualHostName(
            candidate.Package.PackageSha256);
        _userDataDirectory = Path.Combine(
            Path.GetFullPath(webViewDataDirectory),
            CreateStorageDirectoryName(candidate));
        if (candidate.Registration.Contribution.MessageApiVersion
            == ExtensionPanelMessagingContract.CurrentVersion)
        {
            _messageHost = new ExtensionPanelMessageHost(
                candidate,
                approvedCapabilities,
                _virtualHostName,
                panelStateDirectory,
                contextProvider);
            SecurityBadgeText.Text = "HOST API V1";
            SecurityBadgeText.ToolTip =
                "Allowlisted host messaging is enabled for this approved package. File, terminal, network, secret, native-code, and AI APIs remain unavailable.";
        }

        Loaded += OnLoaded;
    }

    public ExtensionPanelCandidate Candidate { get; }

    public bool IsCustomizationEnabled
    {
        get => _isCustomizationEnabled;
        set
        {
            if (_isCustomizationEnabled == value)
            {
                return;
            }

            _isCustomizationEnabled = value;
            if (!value)
            {
                _dragStartPoint = null;
            }

            UpdateCustomizationPresentation();
        }
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? DragRequested;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetimeCancellation.Cancel();
        Loaded -= OnLoaded;
        if (PanelWebView.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.PermissionRequested -= OnPermissionRequested;
            core.DownloadStarting -= OnDownloadStarting;
            core.WebResourceRequested -= OnWebResourceRequested;
            core.ProcessFailed -= OnProcessFailed;
            if (_messageHost is not null)
            {
                core.WebMessageReceived -= OnWebMessageReceived;
            }
        }

        PanelWebView.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_isDisposed || _isInitializing || PanelWebView.CoreWebView2 is not null)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var entryPoint = Path.GetFullPath(Candidate.ViewResourcePath);
            var packageDirectory = Path.GetFullPath(Candidate.Package.PackageDirectory);
            if (!File.Exists(entryPoint)
                || !IsPathInsideDirectory(entryPoint, packageDirectory))
            {
                throw new FileNotFoundException(
                    "The approved panel entry document is missing or outside its package.",
                    entryPoint);
            }

            var currentPackageHash = await Task.Run(
                () => ExtensionDiscoveryService.ComputePackageSha256(
                    packageDirectory),
                _lifetimeCancellation.Token);
            if (!string.Equals(
                    currentPackageHash,
                    Candidate.Package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The package content changed after approval. Refresh discovery and review the new hash before reopening this panel.");
            }

            Directory.CreateDirectory(_userDataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataDirectory);
            await PanelWebView.EnsureCoreWebView2Async(environment);
            if (_isDisposed || PanelWebView.CoreWebView2 is not { } core)
            {
                return;
            }

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = _messageHost is not null;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                _virtualHostName,
                packageDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;
            core.PermissionRequested += OnPermissionRequested;
            core.DownloadStarting += OnDownloadStarting;
            core.WebResourceRequested += OnWebResourceRequested;
            core.ProcessFailed += OnProcessFailed;
            if (_messageHost is not null)
            {
                core.WebMessageReceived += OnWebMessageReceived;
            }
            core.AddWebResourceRequestedFilter(
                "*",
                CoreWebView2WebResourceContext.All);

            var relativePath = Path.GetRelativePath(packageDirectory, entryPoint)
                .Replace('\\', '/');
            var encodedPath = string.Join(
                '/',
                relativePath.Split('/').Select(Uri.EscapeDataString));
            core.Navigate($"https://{_virtualHostName}/{encodedPath}");
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (Exception exception)
        {
            ShowStatus($"This extension panel could not start. {exception.Message}");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!IsLocalPanelUri(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            PanelTitleText.ToolTip =
                "External navigation was blocked. This approved panel is local-only.";
            return;
        }

        _messageHost?.ResetSession();
    }

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (eventArgs.IsSuccess)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        ShowStatus($"The local panel could not be loaded ({eventArgs.WebErrorStatus}).");
    }

    private void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        PanelTitleText.ToolTip =
            "Opening a new browser window was blocked for this local panel.";
    }

    private static void OnPermissionRequested(
        object? sender,
        CoreWebView2PermissionRequestedEventArgs eventArgs)
    {
        eventArgs.State = CoreWebView2PermissionState.Deny;
        eventArgs.Handled = true;
    }

    private static void OnDownloadStarting(
        object? sender,
        CoreWebView2DownloadStartingEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        eventArgs.Handled = true;
    }

    private void OnWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (IsLocalPanelUri(eventArgs.Request.Uri)
            || PanelWebView.CoreWebView2 is not { } core)
        {
            return;
        }

        eventArgs.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes("Blocked by Hypershell")),
            403,
            "Forbidden",
            "Content-Type: text/plain; charset=utf-8");
    }

    private void OnProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        ShowStatus("The extension panel process stopped unexpectedly. Close and reopen the panel to restart it.");
    }

    private async void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (_isDisposed
            || _messageHost is null
            || PanelWebView.CoreWebView2 is not { } core)
        {
            return;
        }

        try
        {
            var outcome = await _messageHost.HandleAsync(
                eventArgs.Source,
                eventArgs.WebMessageAsJson,
                _lifetimeCancellation.Token);
            if (outcome is null || _isDisposed)
            {
                return;
            }

            core.PostWebMessageAsJson(outcome.ResponseJson);
            if (!outcome.IsSuccess)
            {
                SecurityBadgeText.ToolTip =
                    $"The last panel message was rejected: {outcome.Diagnostic}";
            }
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch
        {
            SecurityBadgeText.ToolTip =
                "A panel message could not be processed. No host capability was granted.";
        }
    }

    public void NotifyHostContextChanged()
    {
        if (_isDisposed
            || _messageHost is null
            || PanelWebView.CoreWebView2 is not { } core)
        {
            return;
        }

        try
        {
            if (_messageHost.TryCreateContextChangedEvent() is { } eventJson)
            {
                core.PostWebMessageAsJson(eventJson);
            }
        }
        catch
        {
            SecurityBadgeText.ToolTip =
                "A host context event could not be delivered to this panel.";
        }
    }

    private void CloseClick(object sender, RoutedEventArgs eventArgs)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PanelDragSurfacePreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (!CanUserDragPanel())
        {
            return;
        }

        _dragStartPoint = eventArgs.GetPosition(this);
    }

    private void PanelDragSurfacePreviewMouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        if (_dragStartPoint is not { } startPoint)
        {
            return;
        }

        if (eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            _dragStartPoint = null;
            return;
        }

        var currentPoint = eventArgs.GetPosition(this);
        if (Math.Abs(currentPoint.X - startPoint.X)
                < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - startPoint.Y)
                < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;
        eventArgs.Handled = true;
        DragRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PanelDragSurfacePreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        _dragStartPoint = null;
    }

    private bool CanUserDragPanel() =>
        _isCustomizationEnabled
        && Candidate.Registration.Contribution.Placement.AllowUserMove;

    private void UpdateCustomizationPresentation()
    {
        var canMove = CanUserDragPanel();
        DragHandleText.Visibility = canMove
            ? Visibility.Visible
            : Visibility.Collapsed;
        PanelDragSurface.Cursor = canMove
            ? Cursors.SizeAll
            : Cursors.Arrow;
        PanelDragSurface.ToolTip = canMove
            ? "Drag this panel to another allowed workspace host."
            : null;
    }

    private bool IsLocalPanelUri(string uriText) =>
        Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, _virtualHostName, StringComparison.OrdinalIgnoreCase);

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var directoryWithSeparator = directory.TrimEnd(
                                         Path.DirectorySeparatorChar,
                                         Path.AltDirectorySeparatorChar)
                                     + Path.DirectorySeparatorChar;
        return path.StartsWith(
            directoryWithSeparator,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStorageDirectoryName(
        ExtensionPanelCandidate candidate)
    {
        var identifier = string.Concat(candidate.PanelId.Select(character =>
            char.IsLetterOrDigit(character) ? character : '-'));
        var hashPrefix = candidate.Package.PackageSha256.Length >= 16
            ? candidate.Package.PackageSha256[..16]
            : candidate.Package.PackageSha256;
        return $"{identifier}-{hashPrefix}";
    }
}
