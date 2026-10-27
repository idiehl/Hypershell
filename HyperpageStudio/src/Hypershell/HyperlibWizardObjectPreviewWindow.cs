using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hypershell.Models;
using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hypershell;

/// <summary>Real component preview runs against disposable sample content, outside the user's Work.</summary>
public sealed class HyperlibWizardObjectPreviewWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HyperlibTemplateObjectPreviewService _service = new();
    private readonly TextBlock _status = new() { Text = "Preparing the actual object preview…", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Border _host = new() { MinHeight = 320 };
    private WebView2? _browser;
    private Uri? _allowed;
    public HyperlibWizardObjectPreviewWindow(string libraryRoot, HyperlibWorkTemplateChoice selected)
    {
        Title = selected.Title + " · Library preview"; Width = 920; Height = 720; MinWidth = 560; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush?)Application.Current?.TryFindResource("AppBackgroundBrush") ?? Brushes.Black;
        Foreground = (Brush?)Application.Current?.TryFindResource("ForegroundBrush") ?? Brushes.White;
        var view = new DockPanel { Margin = new(22) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = selected.Title, FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = selected.Description, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); view.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 10, 0, 0) };
        var close = new Button { Content = "Close", Padding = new(18, 9, 18, 9), Margin = new(16, 0, 0, 0) }; close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); view.Children.Add(footer); view.Children.Add(_host); Content = view;
        Loaded += async (_, _) => await PreviewAsync(libraryRoot, selected);
        Closed += (_, _) => { _lifetime.Cancel(); _browser?.Dispose(); _service.Dispose(); _lifetime.Dispose(); };
    }
    private async Task PreviewAsync(string libraryRoot, HyperlibWorkTemplateChoice selected)
    {
        try
        {
            var preview = await _service.PreviewAsync(libraryRoot, selected.GroupId, selected.ObjectId, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _allowed = preview.Url;
            _browser = new WebView2(); _host.Child = _browser;
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "HyperlibObjectPreview", "Browser");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (_lifetime.IsCancellationRequested) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_lifetime.IsCancellationRequested) return;
            var core = _browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.NavigationStarting += (_, args) => { if (!IsAllowed(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) => { if (!IsAllowed(args.Request.Uri) && !args.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Local preview only", ""); };
            core.Navigate(preview.Url.AbsoluteUri); _status.Text = preview.Message + " Sample content belongs only to this preview.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) _status.Text = "This object could not be previewed: " + error.Message; }
    }
    private bool IsAllowed(string address) => _allowed is not null && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == _allowed.Scheme && uri.Host == _allowed.Host && uri.Port == _allowed.Port;
}
