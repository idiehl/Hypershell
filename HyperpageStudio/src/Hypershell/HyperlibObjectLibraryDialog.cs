using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HyperLib.Core;
using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hypershell;

/// <summary>Collects independently reusable objects into the current Template draft.</summary>
public sealed class HyperlibObjectLibraryDialog : Window
{
    private readonly string _libraryRoot;
    private readonly string? _targetProject;
    private readonly TemplateDefinition? _targetDraft;
    private readonly IReadOnlyList<TemplateImportPlan> _pendingPlans;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HyperlibTemplateObjectPreviewService _previewService = new();
    private CancellationTokenSource? _previewCancellation;
    private readonly ComboBox _groups = new() { DisplayMemberPath = nameof(GroupChoice.Title), MinHeight = 36 };
    private readonly ComboBox _categories = new() { MinHeight = 36 };
    private readonly TextBox _search = new() { MinHeight = 36, Padding = new(10, 7, 10, 7) };
    private readonly TextBox _newTemplateName = new() { MinHeight = 36, Padding = new(10, 7, 10, 7), MinWidth = 180, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ListBox _objects = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _collectionList = new() { DisplayMemberPath = nameof(ObjectChoice.Label), MaxHeight = 120, MinHeight = 45 };
    private readonly ObservableCollection<ObjectChoice> _collection = [];
    private readonly TextBlock _selectedTitle = Heading("Choose an object", 22), _selectedDescription = Note("Browse by Design Group or use case, then collect the pieces you want."), _source = Note("");
    private readonly TextBlock _status = Note("Loading the Core Library…"), _previewStatus = Note("Choose an object to preview it in a sample Page."), _count = Note("No objects selected");
    private readonly TextBox _catalogId = new() { IsReadOnly = true, BorderThickness = new(0), Background = Brushes.Transparent, FontFamily = new("Consolas"), FontSize = 11, Padding = new(0, 5, 0, 5), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _copyId = new() { Content = "Copy ID", Padding = new(8, 3, 8, 3), Margin = new(8, 0, 0, 0), IsEnabled = false, ToolTip = "Copy this object's catalog ID for the encyclopedia or another Template" };
    private readonly Button _collect = new() { Content = "Add to collection", Padding = new(15, 9, 15, 9), IsEnabled = false };
    private readonly Button _apply = new() { Content = "Add to Template", Padding = new(18, 10, 18, 10), IsEnabled = false };
    private readonly Border _previewHost = new() { CornerRadius = new(9), BorderThickness = new(1), Padding = new(1) };
    private readonly Grid _main = new();
    private readonly Grid _details = new();
    private readonly Border _browser = new() { Padding = new(14) };
    private IReadOnlyList<ObjectChoice> _allObjects = [];
    private WebView2? _preview;
    private Task? _previewInitialization;
    private Uri? _allowedPreview;
    private bool _loaded, _closed, _busy, _filtering;
    private long _previewSequence;

    public TemplateImportPlan? ImportPlan { get; private set; }
    public IReadOnlyList<TemplateObjectSelection> SelectedObjects { get; private set; } = [];
    public string NewTemplateName => _newTemplateName.Text.Trim();
    internal Func<string, IReadOnlyList<TemplateObjectSelection>, Task<string?>>? CreateTemplateAsync { get; set; }
    internal bool PreviewEnabled { get; set; } = true;
    internal string? PreviewBrowserProfilePath { get; set; }
    internal Task LoadForVerificationAsync() { _loaded = true; return LoadLibraryAsync(); }

    public HyperlibObjectLibraryDialog(string libraryRoot, string? targetProject = null, TemplateDefinition? targetDraft = null,
        IReadOnlyList<TemplateImportPlan>? pendingPlans = null)
    {
        if ((targetProject is null) != (targetDraft is null)) throw new ArgumentException("Supply both a target project and its draft, or neither.");
        _libraryRoot = libraryRoot; _targetProject = targetProject; _targetDraft = targetDraft is null ? null : HyperlibTemplateEditSession.CloneDefinition(targetDraft);
        _pendingPlans = pendingPlans ?? [];
        Title = targetDraft is null ? "New Template · Core Library" : "Core Library · " + targetDraft.Title;
        Width = 1200; Height = 840; MinWidth = 880; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme("AppBackgroundBrush", "#12161C"); Foreground = Theme("ForegroundBrush", "#E6EAF0");
        _previewHost.Background = Theme("PanelBrush", "#181D24"); _previewHost.BorderBrush = Theme("BorderBrush", "#303844");
        _previewHost.Child = new TextBlock { Text = "A real Page preview appears here.", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(20), Foreground = Theme("MutedForegroundBrush", "#929AA6") };
        Content = BuildView();
        AutomationProperties.SetName(_groups, "Design Group"); AutomationProperties.SetName(_categories, "Object category");
        AutomationProperties.SetName(_search, "Find library objects"); _search.ToolTip = "Find objects by catalog ID, name, type, use case, or Design Group";
        AutomationProperties.SetName(_catalogId, "Object catalog ID"); _catalogId.Foreground = Theme("MutedForegroundBrush", "#929AA6");
        _copyId.Click += (_, _) => { if (_objects.SelectedItem is not ObjectChoice selected) return; try { Clipboard.SetText(selected.Object.CatalogId); _status.Text = "Copied " + selected.Object.CatalogId + ". Paste this ID into search to find the object again."; } catch (Exception error) { _status.Text = "Could not copy the ID: " + error.Message; } };
        AutomationProperties.SetName(_newTemplateName, "New Template name"); _newTemplateName.TextChanged += (_, _) => UpdateCollection();
        AutomationProperties.SetName(_objects, "Library objects"); AutomationProperties.SetName(_collectionList, "Template collection");
        ScrollViewer.SetHorizontalScrollBarVisibility(_objects, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(_collectionList, ScrollBarVisibility.Disabled);
        var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 6, 3, 6));
        var objectTitle = new FrameworkElementFactory(typeof(TextBlock)); objectTitle.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Object.Title"));
        objectTitle.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); objectTitle.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(objectTitle);
        var objectGroup = new FrameworkElementFactory(typeof(TextBlock)); objectGroup.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ObjectChoice.Detail)));
        objectGroup.SetValue(TextBlock.FontSizeProperty, 11d); objectGroup.SetValue(TextBlock.ForegroundProperty, Theme("MutedForegroundBrush", "#929AA6"));
        objectGroup.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0)); objectGroup.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(objectGroup);
        _objects.ItemTemplate = new DataTemplate { VisualTree = row };
        _search.TextChanged += (_, _) => Filter(); _groups.SelectionChanged += (_, _) => Filter(); _categories.SelectionChanged += (_, _) => Filter();
        _objects.SelectionChanged += (_, _) => SelectionChanged();
        _objects.MouseDoubleClick += (_, _) => CollectSelected();
        _objects.PreviewKeyDown += (_, args) => { if (args.Key == Key.Enter) { CollectSelected(); args.Handled = true; } };
        _collect.Click += (_, _) => CollectSelected(); _apply.Click += ApplyClick;
        _collectionList.ItemsSource = _collection;
        UpdateCollection();
        Loaded += async (_, _) => { if (_loaded) return; _loaded = true; await LoadLibraryAsync(); };
        SizeChanged += (_, _) => UpdateLayoutMode();
        _main.SizeChanged += (_, _) => UpdateLayoutMode();
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        Closed += (_, _) =>
        {
            _closed = true; CancelPreview(); _lifetime.Cancel();
            _preview?.Dispose(); _preview = null; _previewService.Dispose(); _lifetime.Dispose();
        };
    }

    private FrameworkElement BuildView()
    {
        var outer = new DockPanel { Margin = new(22) };
        var heading = new StackPanel { Margin = new(0, 0, 0, 16) };
        heading.Children.Add(Heading("Core Template Library", 26));
        heading.Children.Add(Note(_targetDraft is null
            ? "Explore Design Groups. Collect elements, page layouts, and shared styles for a new Template."
            : "Explore Design Groups. Combine elements, page layouts, and shared styles in “" + _targetDraft.Title + "”."));
        DockPanel.SetDock(heading, Dock.Top); outer.Children.Add(heading);
        var footer = new DockPanel { Margin = new(0, 14, 0, 0) };
        if (_targetDraft is null)
        {
            var nameRow = new StackPanel { Margin = new(0, 0, 0, 12) };
            nameRow.Children.Add(Label("New Template name")); nameRow.Children.Add(_newTemplateName);
            DockPanel.SetDock(nameRow, Dock.Top); footer.Children.Add(nameRow);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Action("Cancel", (_, _) => Close())); actions.Children.Add(_apply);
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);

        _main.ColumnDefinitions.Add(new() { Width = new(292) }); _main.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _main.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); _main.RowDefinitions.Add(new() { Height = new(0) });
        _browser.Background = Theme("PanelBrush", "#181D24"); _browser.CornerRadius = new(9); _browser.Margin = new(0, 0, 18, 0);
        var browse = new DockPanel(); var filters = new StackPanel();
        filters.Children.Add(Heading("Browse", 16)); filters.Children.Add(_search);
        filters.Children.Add(Label("Design Group")); filters.Children.Add(_groups);
        filters.Children.Add(Label("Type / use case")); filters.Children.Add(_categories);
        filters.Margin = new(0, 0, 0, 12); DockPanel.SetDock(filters, Dock.Top); browse.Children.Add(filters); browse.Children.Add(_objects); _browser.Child = browse;
        _main.Children.Add(_browser);

        _details.RowDefinitions.Add(new() { Height = GridLength.Auto }); _details.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _details.RowDefinitions.Add(new() { Height = GridLength.Auto }); _details.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var selected = new DockPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(_collect, Dock.Right); _collect.VerticalAlignment = VerticalAlignment.Top; _collect.Margin = new(18, 0, 0, 0); selected.Children.Add(_collect);
        var identity = new StackPanel(); identity.Children.Add(_source); identity.Children.Add(_selectedTitle); identity.Children.Add(_selectedDescription);
        var idRow = new DockPanel { Margin = new(0, 6, 0, 0) }; DockPanel.SetDock(_copyId, Dock.Right); idRow.Children.Add(_copyId); idRow.Children.Add(_catalogId); identity.Children.Add(idRow);
        selected.Children.Add(identity); _details.Children.Add(selected);
        Grid.SetRow(_previewHost, 1); _details.Children.Add(_previewHost);
        Grid.SetRow(_previewStatus, 2); _previewStatus.Margin = new(0, 8, 0, 8); _details.Children.Add(_previewStatus);
        var basket = new Border { Padding = new(12), Background = Theme("PanelBrush", "#181D24"), CornerRadius = new(9) };
        var basketDock = new DockPanel(); var basketHeader = new DockPanel();
        var remove = Action("Remove", (_, _) => { if (_collectionList.SelectedItem is ObjectChoice selectedObject) _collection.Remove(selectedObject); UpdateCollection(); });
        DockPanel.SetDock(remove, Dock.Right); basketHeader.Children.Add(remove); basketHeader.Children.Add(_count);
        DockPanel.SetDock(basketHeader, Dock.Top); basketDock.Children.Add(basketHeader); basketDock.Children.Add(_collectionList); basket.Child = basketDock;
        Grid.SetRow(basket, 3); _details.Children.Add(basket); Grid.SetColumn(_details, 1); _main.Children.Add(_details);
        outer.Children.Add(_main); return outer;
    }

    private async Task LoadLibraryAsync()
    {
        try
        {
            await HyperlibExpandedCoreLibrary.EnsureAsync(_libraryRoot, _lifetime.Token);
            var groups = TemplateObjectLibrary.ListDesignGroups(_libraryRoot);
            _allObjects = groups.SelectMany(group => TemplateObjectLibrary.ListObjects(_libraryRoot, group.Id)
                .Select(item => new ObjectChoice(group.Id, group.Title, item))).ToArray();
            _filtering = true;
            _groups.ItemsSource = new[] { new GroupChoice("", "All Design Groups") }.Concat(groups.Select(group => new GroupChoice(group.Id, group.Title))).ToArray();
            _groups.SelectedIndex = 0;
            _categories.ItemsSource = new[] { "All objects" }.Concat(_allObjects.Select(item => item.Object.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToArray();
            _categories.SelectedIndex = 0; _filtering = false; Filter();
            _status.Text = $"{groups.Count} Design Groups · {_allObjects.Count} objects. Selections stay in your draft until you save.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) _status.Text = "Library unavailable: " + error.Message; }
    }

    private void Filter()
    {
        if (_filtering || !_loaded || _closed) return;
        var selected = _objects.SelectedItem as ObjectChoice;
        var group = (_groups.SelectedItem as GroupChoice)?.Id ?? "";
        var category = _categories.SelectedItem as string ?? "All objects"; var search = _search.Text.Trim();
        var exactId = _allObjects.Any(item => item.Object.CatalogId.Equals(search, StringComparison.OrdinalIgnoreCase) || item.Key.Equals(search, StringComparison.OrdinalIgnoreCase));
        var matches = _allObjects.Where(item => exactId
            ? item.Object.CatalogId.Equals(search, StringComparison.OrdinalIgnoreCase) || item.Key.Equals(search, StringComparison.OrdinalIgnoreCase)
            : (group.Length == 0 || item.GroupId == group)
            && (category == "All objects" || item.Object.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            && (search.Length == 0 || (item.Object.CatalogId + " " + item.Key + " " + item.Object.Title + " " + item.Object.Description + " " + item.Object.Category + " " + item.GroupTitle + " " + item.Object.Kind + " " + item.Object.SourceTemplateId + " " + item.Object.SourceLibrary).Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        _objects.ItemsSource = matches;
        _objects.SelectedItem = matches.FirstOrDefault(item => item.Key == selected?.Key) ?? matches.FirstOrDefault();
        if (matches.Length == 0) _status.Text = "No objects match. Try another category or search.";
    }

    private void SelectionChanged()
    {
        if (_closed) return;
        CancelPreview();
        if (_preview is not null) _preview.Visibility = Visibility.Hidden;
        if (_objects.SelectedItem is not ObjectChoice selected) { _collect.IsEnabled = false; _copyId.IsEnabled = false; _catalogId.Text = ""; _selectedTitle.Text = "No matching objects"; _selectedDescription.Text = "Try another category or search."; _source.Text = ""; _previewStatus.Text = "Choose an object to preview it in a sample Page."; return; }
        _selectedTitle.Text = selected.Object.Title; _selectedDescription.Text = selected.Object.Description;
        _catalogId.Text = selected.Object.CatalogId; _catalogId.ToolTip = selected.Key; _copyId.IsEnabled = true;
        _source.Text = selected.GroupTitle + " · " + selected.Object.Category;
        _previewStatus.Text = "Preparing a visual preview…";
        UpdateCollection();
        if (PreviewEnabled)
        {
            var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _previewCancellation = request;
            _ = ShowPreviewAsync(selected, _previewSequence, request);
        }
        else _previewStatus.Text = "Visual preview is disabled for this verification fixture.";
    }

    private void CancelPreview()
    {
        // Empty selections and closing invalidate the old request too. Detach before
        // cancellation; its async owner disposes it once all token use has finished.
        ++_previewSequence;
        var previous = _previewCancellation;
        _previewCancellation = null;
        previous?.Cancel();
    }

    private void CollectSelected()
    {
        if (_objects.SelectedItem is not ObjectChoice selected || _busy || _collection.Any(item => item.Key == selected.Key)) return;
        _collection.Add(selected); _collectionList.SelectedItem = selected; UpdateCollection();
        _status.Text = "Collected “" + selected.Object.Title + "”. Its required components are included automatically.";
    }

    private void UpdateCollection()
    {
        var present = _objects.SelectedItem is ObjectChoice selected && _collection.Any(item => item.Key == selected.Key);
        _collect.Content = present ? "In collection" : "Add to collection"; _collect.IsEnabled = !_busy && _objects.SelectedItem is ObjectChoice && !present;
        _apply.IsEnabled = !_busy && (_targetDraft is null ? NewTemplateName.Length > 0 : _collection.Count > 0);
        _apply.Content = _targetDraft is null ? "Create Template" : _collection.Count == 0 ? "Add to Template" : $"Add {_collection.Count} to Template";
        _count.Text = _collection.Count == 0 ? "Your collection · choose objects above" : $"Your collection · {_collection.Count} object{(_collection.Count == 1 ? "" : "s")}";
    }

    private async void ApplyClick(object sender, RoutedEventArgs args)
    {
        if (_busy || (_targetDraft is not null && _collection.Count == 0)) return;
        SelectedObjects = _collection.Select(item => new TemplateObjectSelection(item.GroupId, item.Object.Id)).ToArray();
        if (_targetDraft is null)
        {
            if (NewTemplateName.Length is 0 or > 200 || NewTemplateName.Any(char.IsControl))
            { _status.Text = "Titles need 1–200 printable characters."; return; }
            if (CreateTemplateAsync is null) { DialogResult = true; return; }
            SetBusy(true); _status.Text = "Creating your Template and collecting its components…";
            try
            {
                var error = await CreateTemplateAsync(NewTemplateName, SelectedObjects);
                if (!string.IsNullOrWhiteSpace(error)) _status.Text = error;
                else { SetBusy(false); DialogResult = true; }
            }
            catch (Exception error) { if (!_closed) _status.Text = error.Message; }
            finally { if (!_closed) SetBusy(false); }
            return;
        }
        SetBusy(true); _status.Text = "Preparing components and their dependencies…";
        try
        {
            ImportPlan = await TemplateObjectImporter.PrepareAsync(_libraryRoot, _targetProject!, _targetDraft,
                SelectedObjects, _pendingPlans, _lifetime.Token);
            SetBusy(false); DialogResult = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _status.Text = error.Message; }
        finally { if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        _busy = value; _main.IsEnabled = !value; _newTemplateName.IsEnabled = !value; UpdateCollection();
    }

    private async Task ShowPreviewAsync(ObjectChoice selected, long sequence, CancellationTokenSource request)
    {
        var cancellation = request.Token;
        try
        {
            await Task.Delay(250, cancellation);
            if (_closed || cancellation.IsCancellationRequested || sequence != _previewSequence) return;
            _previewStatus.Text = "Rendering this object in a sample Page…";
            var rendered = await _previewService.PreviewAsync(_libraryRoot, selected.GroupId, selected.Object.Id, cancellation);
            if (_closed || cancellation.IsCancellationRequested || sequence != _previewSequence) return;
            var preview = await EnsurePreviewBrowserAsync();
            if (_closed || cancellation.IsCancellationRequested || sequence != _previewSequence) return;
            _allowedPreview = rendered.Url; preview.CoreWebView2.Navigate(rendered.Url.AbsoluteUri); preview.Visibility = Visibility.Visible; _previewStatus.Text = rendered.Message;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed && sequence == _previewSequence) _previewStatus.Text = "Preview unavailable: " + error.Message; }
        finally
        {
            if (ReferenceEquals(_previewCancellation, request)) _previewCancellation = null;
            request.Dispose();
        }
    }

    private async Task<WebView2> EnsurePreviewBrowserAsync()
    {
        var preview = _preview;
        if (preview is null)
        {
            preview = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 24, 29, 36) };
            _preview = preview;
            _previewHost.Child = preview;
        }
        // A control can exist before its asynchronous browser initialization finishes.
        // Every selection awaits the same initialization before attempting navigation.
        var initialization = _previewInitialization ??= InitializePreviewBrowserAsync(preview);
        try { await initialization; }
        catch
        {
            if (!_closed && ReferenceEquals(_preview, preview))
            {
                _preview = null; _previewInitialization = null; _previewHost.Child = null;
                preview.Dispose();
            }
            throw;
        }
        return preview;
    }

    private async Task InitializePreviewBrowserAsync(WebView2 preview)
    {
        var profile = PreviewBrowserProfilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "HyperlibObjectPreview", "Browser");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
        if (_closed) return;
        await preview.EnsureCoreWebView2Async(environment);
        if (_closed) return;
        var core = preview.CoreWebView2 ?? throw new InvalidOperationException("The preview browser could not initialize.");
        core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.NavigationStarting += (_, args) =>
        {
            if (_allowedPreview is null || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
                || uri.Scheme != _allowedPreview.Scheme || uri.Host != _allowedPreview.Host || uri.Port != _allowedPreview.Port) args.Cancel = true;
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (_allowedPreview is not null && Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri)
                && uri.Scheme == _allowedPreview.Scheme && uri.Host == _allowedPreview.Host && uri.Port == _allowedPreview.Port) return;
            if (args.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Preview resources are local", "");
        };
    }

    private void UpdateLayoutMode()
    {
        var width = ActualWidth > 0 ? ActualWidth : (Content as FrameworkElement)?.ActualWidth ?? Width;
        var height = ActualHeight > 0 ? ActualHeight : (Content as FrameworkElement)?.ActualHeight ?? Height;
        var compact = width < 1020;
        _main.ColumnDefinitions[0].Width = new(compact ? 248 : 292);
        _browser.Margin = new(0, 0, compact ? 12 : 18, 0);
        _selectedDescription.Visibility = height < 730 ? Visibility.Collapsed : Visibility.Visible;
        _collectionList.MaxHeight = height < 730 ? 64 : 120;
    }

    private static Brush Theme(string key, string fallback) => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    private static TextBlock Heading(string text, double size) => new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 7) };
    private static TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = Theme("MutedForegroundBrush", "#929AA6"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 12, 3), VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Margin = new(0, 12, 0, 5), Foreground = Theme("MutedForegroundBrush", "#929AA6") };
    private static Button Action(string label, RoutedEventHandler click) { var button = new Button { Content = label, Padding = new(12, 8, 12, 8), Margin = new(0, 0, 8, 0) }; button.Click += click; return button; }
    private sealed record GroupChoice(string Id, string Title);
    private sealed record ObjectChoice(string GroupId, string GroupTitle, TemplateLibraryObject Object)
    {
        public string Key => GroupId + "/" + Object.Id;
        public string Detail => Object.Category + " · " + GroupTitle;
        public string Label => Object.Title + "\n" + Object.Category + " · " + GroupTitle;
    }
}
