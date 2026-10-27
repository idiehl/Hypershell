using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Hypershell.Controls;

public partial class MarkdownPreviewControl : UserControl
{
    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment =
        new(CreateEnvironmentAsync);

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata(string.Empty, OnPreviewPropertyChanged));

    public static readonly DependencyProperty DocumentTitleProperty =
        DependencyProperty.Register(
            nameof(DocumentTitle),
            typeof(string),
            typeof(MarkdownPreviewControl),
            new PropertyMetadata("Document", OnPreviewPropertyChanged));

    private readonly DispatcherTimer _renderTimer;
    private readonly string _previewFilePath;
    private readonly Uri _previewFileUri;
    private bool _isInitializing;
    private bool _isReady;
    private bool _isDisposed;
    private string _lastRenderedHtml = string.Empty;

    public MarkdownPreviewControl()
    {
        InitializeComponent();
        var previewDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypershell",
            "MarkdownPreviewCache");
        Directory.CreateDirectory(previewDirectory);
        _previewFilePath = Path.Combine(
            previewDirectory,
            $"{Guid.NewGuid():N}.html");
        _previewFileUri = new Uri(_previewFilePath);
        _renderTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Background,
            OnRenderTimerTick,
            Dispatcher);
        _renderTimer.Stop();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string DocumentTitle
    {
        get => (string)GetValue(DocumentTitleProperty);
        set => SetValue(DocumentTitleProperty, value);
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await EnsureInitializedAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _renderTimer.Stop();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        if (PreviewWebView.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.ProcessFailed -= OnProcessFailed;
        }

        PreviewWebView.Dispose();
        TryDeletePreviewFile(_previewFilePath);
        TryDeletePreviewFile(_previewFilePath + ".tmp");
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isReady || _isInitializing || _isDisposed)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var environment = await SharedEnvironment.Value;
            await PreviewWebView.EnsureCoreWebView2Async(environment);
            if (_isDisposed || PreviewWebView.CoreWebView2 is not { } core)
            {
                return;
            }

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ProcessFailed += OnProcessFailed;
            _isReady = true;
            RenderPreview();
        }
        catch (Exception exception)
        {
            PreviewStatusOverlay.Visibility = Visibility.Visible;
            PreviewStatusText.Text =
                $"Markdown preview could not start. {exception.Message}";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private static void OnPreviewPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is MarkdownPreviewControl preview)
        {
            preview.ScheduleRender();
        }
    }

    private void ScheduleRender()
    {
        if (_isDisposed)
        {
            return;
        }

        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void OnRenderTimerTick(object? sender, EventArgs eventArgs)
    {
        _renderTimer.Stop();
        RenderPreview();
    }

    private void RenderPreview()
    {
        if (!_isReady || PreviewWebView.CoreWebView2 is null)
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(DocumentTitle)
            ? "Document"
            : DocumentTitle;
        var html = Encoding.UTF8.GetString(
            StandaloneHtmlDocumentBuilder.FromMarkdown(
                title,
                Markdown ?? string.Empty));
        if (string.Equals(html, _lastRenderedHtml, StringComparison.Ordinal))
        {
            return;
        }

        _lastRenderedHtml = html;
        PreviewStatusOverlay.Visibility = Visibility.Visible;
        PreviewStatusText.Text = "Updating preview…";
        try
        {
            var temporaryPath = _previewFilePath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                html,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _previewFilePath, overwrite: true);
            PreviewWebView.CoreWebView2.Navigate(
                $"{_previewFileUri.AbsoluteUri}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        }
        catch (Exception exception)
        {
            PreviewWebView.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = $"Markdown preview could not update. {exception.Message}";
        }
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri)
            && uri.IsFile
            && string.Equals(
                Path.GetFullPath(uri.LocalPath),
                Path.GetFullPath(_previewFilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        eventArgs.Cancel = true;
        OpenExternalLink(eventArgs.Uri);
    }

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        PreviewStatusOverlay.Visibility = eventArgs.IsSuccess
            ? Visibility.Collapsed
            : Visibility.Visible;
        PreviewWebView.Visibility = eventArgs.IsSuccess
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!eventArgs.IsSuccess)
        {
            PreviewStatusText.Text =
                $"Markdown preview failed to render: {eventArgs.WebErrorStatus}.";
        }
    }

    private void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        OpenExternalLink(eventArgs.Uri);
    }

    private void OnProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        PreviewStatusOverlay.Visibility = Visibility.Visible;
        PreviewWebView.Visibility = Visibility.Collapsed;
        PreviewStatusText.Text =
            "The Markdown preview process stopped. Reopen the document tab to restart it.";
    }

    private static void OpenExternalLink(string? address)
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

    private static void TryDeletePreviewFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypershell",
            "MarkdownPreviewWebView2");
        Directory.CreateDirectory(userDataDirectory);
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataDirectory);
    }
}
