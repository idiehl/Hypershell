using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Hypershell;

public partial class PdfDocumentView : UserControl, IDisposable
{
    private static readonly object EnvironmentGate = new();
    private static Task<CoreWebView2Environment>? _sharedEnvironmentTask;

    private readonly PdfDocumentTabViewModel _viewModel;
    private readonly Uri _documentUri;
    private bool _isInitialized;
    private bool _isInitializing;
    private bool _isDisposed;

    public PdfDocumentView(PdfDocumentTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _documentUri = new Uri(viewModel.FilePath);
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await EnsureInitializedAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized || _isInitializing || _isDisposed)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var environment = await GetSharedEnvironmentAsync();
            await PdfWebView.EnsureCoreWebView2Async(environment);
            if (_isDisposed || PdfWebView.CoreWebView2 is not { } core)
            {
                return;
            }

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ProcessFailed += OnProcessFailed;
            _isInitialized = true;
            NavigateToDocument();
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or NotSupportedException
                                         or COMException)
        {
            _viewModel.ReportError(
                $"The embedded PDF viewer could not start: {exception.Message}");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
    {
        Task<CoreWebView2Environment> environmentTask;
        lock (EnvironmentGate)
        {
            environmentTask = _sharedEnvironmentTask ??= CreateEnvironmentAsync();
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
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypershell",
            "PdfViewerWebView2");
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }

    private void NavigateToDocument()
    {
        if (!_isInitialized || PdfWebView.CoreWebView2 is null)
        {
            return;
        }

        _viewModel.ClearError();
        PdfWebView.CoreWebView2.Navigate(_documentUri.AbsoluteUri);
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri)
            && (string.Equals(
                    uri.AbsoluteUri,
                    _documentUri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase)
                || uri.Scheme == "about"))
        {
            return;
        }

        eventArgs.Cancel = true;
        OpenExternalAddress(eventArgs.Uri);
    }

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (eventArgs.IsSuccess)
        {
            _viewModel.ClearError();
            return;
        }

        _viewModel.ReportError(
            $"The PDF renderer could not load this document: {eventArgs.WebErrorStatus}.");
    }

    private void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        OpenExternalAddress(eventArgs.Uri);
    }

    private void OnProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs) =>
        _viewModel.ReportError(
            "The PDF viewer process stopped. Reopen the PDF tab to restart it.");

    private void OpenDocumentMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private void RefreshPdfClick(object sender, RoutedEventArgs eventArgs) =>
        NavigateToDocument();

    private void ZoomInClick(object sender, RoutedEventArgs eventArgs) =>
        PdfWebView.ZoomFactor = Math.Min(5, PdfWebView.ZoomFactor + 0.1);

    private void ActualSizeClick(object sender, RoutedEventArgs eventArgs) =>
        PdfWebView.ZoomFactor = 1;

    private void ZoomOutClick(object sender, RoutedEventArgs eventArgs) =>
        PdfWebView.ZoomFactor = Math.Max(0.25, PdfWebView.ZoomFactor - 0.1);

    private void OpenExternallyClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_viewModel.FilePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"Unable to open the PDF externally: {exception.Message}");
        }
    }

    private void ShowInExplorerClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(_viewModel.FilePath);
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"Unable to open File Explorer: {exception.Message}");
        }
    }

    private static void OpenExternalAddress(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Loaded -= OnLoaded;
        if (PdfWebView.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.ProcessFailed -= OnProcessFailed;
        }

        PdfWebView.Dispose();
    }
}
