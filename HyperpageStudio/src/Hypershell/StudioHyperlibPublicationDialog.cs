using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hypershell.Services;
using Microsoft.Win32;

namespace Hypershell;

/// <summary>Explicitly reviews and publishes a local Work copy; live-site publication remains a separate choice.</summary>
public sealed class StudioHyperlibPublicationDialog : Window
{
    private readonly string _projectRoot;
    private readonly StudioHyperlibWorkPublicationService _service = new();
    private readonly TextBox _destination = new() { MinHeight = 36 };
    private readonly TextBlock _reviewText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 23 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Button _publish = new() { Content = "Publish to Hyperlib", Padding = new(18, 10, 18, 10), IsEnabled = false };
    private readonly Button _review = new() { Content = "Review publication", Padding = new(16, 10, 16, 10) };
    private readonly Button _browse = new() { Content = "…", Width = 36, MinWidth = 36, Margin = new(8, 0, 0, 0), ToolTip = "Choose a Hyperlib reader library" };
    private StudioHyperlibWorkPublicationReview? _prepared;
    private bool _busy, _completed;
    public StudioHyperlibWorkPublicationResult? Result { get; private set; }
    public StudioHyperlibPublicationDialog(string workProjectRoot)
    {
        _projectRoot = Path.GetFullPath(workProjectRoot);
        Title = "Publish Work to Hyperlib"; Width = 720; Height = 590; MinWidth = 560; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme("AppBackgroundBrush", "#12161C"); Foreground = Theme("ForegroundBrush", "#E6EAF0");
        var outer = new DockPanel { Margin = new(24) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "Phase 4 · Publish", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme("SecondaryVividBrush", "#8DBBAA") });
        heading.Children.Add(new TextBlock { Text = "Send this Work to Hyperlib", FontSize = 25, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        heading.Children.Add(Note("Hyperlib receives an independent copy of the saved Work, its Pages, template code, and referenced media. Your Studio project remains available for continued authoring."));
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var close = new Button { Content = "Close", Padding = new(16, 10, 16, 10), Margin = new(0, 0, 8, 0) }; close.Click += (_, _) => Close();
        actions.Children.Add(close); actions.Children.Add(_publish); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        footer.Children.Add(new TextBlock { Text = "Live-site publication is a separate action in Hyperlib.", TextWrapping = TextWrapping.Wrap, MaxWidth = 230, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme("MutedForegroundBrush", "#98A4AE"), Margin = new(0, 0, 14, 0) });
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);
        var form = new StackPanel(); form.Children.Add(new TextBlock { Text = "Hyperlib reader library", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 7) });
        var location = new DockPanel(); DockPanel.SetDock(_browse, Dock.Right); location.Children.Add(_browse); location.Children.Add(_destination); form.Children.Add(location);
        _destination.Text = _service.GetDefaultDestination() ?? "";
        _destination.ToolTip = "Choose the Hyperlib root folder containing Content/library.json";
        System.Windows.Automation.AutomationProperties.SetName(_destination, "Hyperlib publication destination library");
        form.Children.Add(_review); _review.HorizontalAlignment = HorizontalAlignment.Left; _review.Margin = new(0, 12, 0, 18);
        form.Children.Add(new Border { Background = Theme("PanelBrush", "#1B232B"), BorderBrush = Theme("BorderBrush", "#303844"), BorderThickness = new(1), CornerRadius = new(7), Padding = new(18), Child = _reviewText });
        form.Children.Add(_status); outer.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = outer;
        _reviewText.Text = "Review the saved Work before publishing.";
        _destination.TextChanged += (_, _) => { if (!_busy) { _prepared = null; _publish.IsEnabled = false; _reviewText.Text = "Review this destination before publishing."; _status.Text = ""; } };
        _browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose the Hyperlib reader library", Multiselect = false };
            if (Directory.Exists(_destination.Text)) dialog.InitialDirectory = _destination.Text;
            if (dialog.ShowDialog(this) == true) _destination.Text = dialog.FolderName;
        };
        _review.Click += async (_, _) => await ReviewAsync();
        _publish.Click += async (_, _) => await PublishAsync();
        Loaded += async (_, _) => { if (_destination.Text.Length > 0) await ReviewAsync(); };
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
    }
    private async Task ReviewAsync()
    {
        if (_busy || _completed) return;
        SetBusy(true); _status.Text = "Reading the saved Work and checking the destination…";
        try
        {
            _prepared = await _service.ReviewAsync(_projectRoot, _destination.Text.Trim());
            _reviewText.Text = $"{_prepared.Title}\n{_prepared.PageCount} Pages · {_prepared.NodeCount} hierarchy objects\n{_prepared.SourceFileCount} owned source files · {_prepared.MediaFileCount} referenced media files\n{_prepared.Bytes / 1024d / 1024d:0.0} MiB\n\nDestination: Studio publications collection\nExisting Hyperlib Works, source files, and media are preserved.";
            _status.Text = _prepared.CanPublish ? "Ready to publish this saved snapshot." : _prepared.ConflictMessage;
        }
        catch (Exception error) { _prepared = null; _status.Text = error.Message; }
        finally { SetBusy(false); }
    }
    private async Task PublishAsync()
    {
        if (_busy || _completed || _prepared is not { CanPublish: true } prepared) return;
        SetBusy(true); _status.Text = "Publishing the reviewed Work to Hyperlib…";
        try
        {
            Result = await _service.PublishAsync(prepared); _completed = true;
            _status.Text = "Published to Hyperlib in the Studio publications collection.\n\nRestart the Hyperlib reader gateway: right-click Hyperlib in MyNet, choose Stop…, then launch Hyperlib again. Closing and reopening its window alone may reuse the existing gateway.";
            _publish.Content = "Published";
        }
        catch (Exception error) { _prepared = null; _status.Text = error.Message + " Review the destination again before retrying."; }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        _busy = busy; _destination.IsEnabled = !busy && !_completed; _browse.IsEnabled = !busy && !_completed;
        _review.IsEnabled = !busy && !_completed; _publish.IsEnabled = !busy && !_completed && _prepared is { CanPublish: true };
    }
    private static Brush Theme(string key, string fallback) => (Brush?)Application.Current?.TryFindResource(key) ?? (Brush)new BrushConverter().ConvertFromString(fallback)!;
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme("MutedForegroundBrush", "#98A4AE"), Margin = new(0, 10, 0, 0), LineHeight = 21 };
}
