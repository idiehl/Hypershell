using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Hypershell.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hypershell;

public partial class VisualPageStudioView : UserControl, IDisposable
{
    private const string PaneContentDragFormat =
        "Hypershell.VisualPageStudio.PaneContent";
    private static readonly object EnvironmentGate = new();
    private static Task<CoreWebView2Environment>? _environmentTask;
    private readonly VisualPageStudioTabViewModel _viewModel;
    private readonly string? _authoringWebViewProfileDirectory;
    private readonly StudioAuthoringLibraryService? _authoringLibraryService;
    private readonly List<WebView2> _webViews = [];
    private readonly Dictionary<Window, FloatingToolWindow> _floatingToolWindows = [];
    private readonly Dictionary<string, MonacoEditorControl> _sourceEditors =
        new(StringComparer.OrdinalIgnoreCase);
    private WebView2? _canvas;
    private WebView2? _runtimePreview;
    private readonly List<HyperlibStudioWorkspaceView> _hyperlibViews = [];
    private StudioHyperlibAuthoringView? _studioAuthoring;
    private TabControl? _authoringPaneTabs;
    private string? _authoringPaneNodeId;
    private bool _workspaceChromeRefreshQueued;
    private bool _workspaceActionInFlight;
    private string _runtimeOrigin = string.Empty;
    private string _requestedRuntimePreviewUrl = string.Empty;
    private bool _isDisposed;
    private bool _rebuilding;
    private bool _toolPlacementsRestored;
    private bool _coordinatingPanels;
    private bool _authoringPanelsInitialized;
    private string _leftAuthoringPanel = "contents";
    private string _rightAuthoringPanel = "templates";
    private bool _leftAuthoringPanelOpen = true;
    private bool _rightAuthoringPanelOpen = true;
    private bool? _leftNativePanelVisible;
    private bool? _rightNativePanelVisible;
    private GridLength _leftNativePanelWidth = new(300);
    private GridLength _rightNativePanelWidth = new(340);
    private Window? _panelOwner;
    private bool _catalogElementCommitInFlight;

    public VisualPageStudioView(VisualPageStudioTabViewModel viewModel,
        string? authoringWebViewProfileDirectory = null, StudioAuthoringLibraryService? authoringLibraryService = null)
    {
        InitializeComponent();
        ConfigureEdgeRevealPopups();
        _viewModel = viewModel;
        _authoringWebViewProfileDirectory = authoringWebViewProfileDirectory;
        _authoringLibraryService = authoringLibraryService;
        DataContext = viewModel;
        ConfigureAuthoringPanels();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _viewModel.CanvasInvalidated += OnCanvasInvalidated;
        _viewModel.LayoutChanged += OnLayoutChanged;
        _viewModel.RuntimePreviewRequested += OnRuntimePreviewRequested;
        _viewModel.PaneRequested += OnPaneRequested;
        _viewModel.ProjectDocumentInsertionRequested +=
            OnProjectDocumentInsertionRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CatalogElementPreviewRequested += PreviewCatalogElement;
        _viewModel.CatalogElementCommitRequested += CommitCatalogElement;
        if (_viewModel.HyperlibWorkspace is { } hyperlib)
        {
            hyperlib.OpenRelativeCodeRequested += OpenHyperlibSource;
            hyperlib.EditTemplateRequested += OpenHyperlibTemplate;
        }
        if (_viewModel.AiAssistant is { } assistant)
        {
            assistant.Messages.CollectionChanged +=
                OnHyperpageAiMessagesChanged;
        }
        ApplyAiAssistantLayout();
        IsVisibleChanged += OnStudioVisibilityChanged;
        RebuildPaneLayout();
    }

    private void ConfigureEdgeRevealPopups()
    {
        StudioLeftPanelEdgeRevealHost.PlacementTarget = StudioPanelGrid;
        StudioLeftPanelEdgeRevealHost.CustomPopupPlacementCallback =
            PlacePopupAtLeftEdge;
        StudioRightPanelEdgeRevealHost.PlacementTarget = StudioPanelGrid;
        StudioRightPanelEdgeRevealHost.CustomPopupPlacementCallback =
            PlacePopupAtRightEdge;
        AiAssistantBottomEdgeRevealHost.PlacementTarget = StudioPanelGrid;
        AiAssistantBottomEdgeRevealHost.CustomPopupPlacementCallback =
            PlacePopupAtBottomEdge;
        StudioPanelGrid.SizeChanged += OnStudioPanelGridSizeChanged;
    }

    private async void EditHyperlibTemplateClick(object sender, RoutedEventArgs e)
        => await EditHyperlibTemplateAsync(_viewModel.StudioObjectKind == "Work" ? "defaults" : "elements");

    private void OpenHyperlibTemplate() => _ = EditHyperlibTemplateAsync(_viewModel.StudioObjectKind == "Work" ? "defaults" : "elements");

    private async Task EditHyperlibTemplateAsync(string section)
    {
        try
        {
            var loaded = await HyperlibStudioAuthoringService.LoadAsync(_viewModel.ProjectRoot, _viewModel.Project.HyperlibAuthoringPath);
            var dialog = new HyperlibTemplateEditorDialog(_viewModel.ProjectRoot, loaded.Definition, loaded.Snapshot, OpenHyperlibSource,
                reusableTemplate: _viewModel.StudioObjectKind == "Template") { Owner = Window.GetWindow(this) };
            dialog.SelectSection(section);
            dialog.ShowDialog();
            await _viewModel.RefreshProjectTreeAsync();
            if (_viewModel.HyperlibWorkspace is { } workspace) await workspace.RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, _viewModel.StudioObjectKind == "Work" ? "Work structure" : "Template elements", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OpenHyperlibSource(string relativePath)
    {
        try
        {
            _ = VisualPageProjectService.ResolveProjectPath(_viewModel.ProjectRoot, relativePath);
            await _viewModel.OpenProjectNodeAsync(new HyperpageProjectTreeNodeViewModel
            {
                Name = Path.GetFileName(relativePath), RelativePath = relativePath, IsDirectory = false,
                IsProtected = relativePath.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
            });
        }
        catch (Exception exception) { _viewModel.HyperlibWorkspace?.ReportExternalError(exception.Message); }
    }

    private void OpenHyperlibWorkspaceClick(object sender, RoutedEventArgs args) => _viewModel.RequestPaneContent("canvas");
    private async void OpenHyperlibLibraryPhaseClick(object sender, RoutedEventArgs args) => await OpenHyperlibStagingAsync(false);
    private async void OpenHyperlibStagingPhaseClick(object sender, RoutedEventArgs args) => await OpenHyperlibStagingAsync(true);
    private async void OpenHyperlibPublicationPhaseClick(object sender, RoutedEventArgs args) => await OpenHyperlibPublicationAsync();

    public void RequestHyperlibAction(string kind)
    {
        if (_isDisposed) return;
        if (!_viewModel.HasHyperlibAuthoring)
        {
            if (kind == "new-page") _ = ShowCreatePageDialogAsync();
            else if (kind == "new-template") ShowCreateTemplateDialog(HyperpageTemplateCreationSource.Code);
            return;
        }
        if (kind == "new-work") { _viewModel.RequestStudioNewProject(); return; }
        if (kind == "new-page") { _ = NewHyperlibPageAsync(); return; }
        _viewModel.RequestPaneContent("hyperlib-design");
        (_hyperlibViews.FirstOrDefault(view => view.IsVisible) ?? _hyperlibViews.FirstOrDefault())?.RequestAction(kind);
    }

    private FrameworkElement CreateHyperlibAuthoring()
    {
        if (_studioAuthoring is null)
        {
            _studioAuthoring = new StudioHyperlibAuthoringView(_viewModel.HyperlibWorkspace!,
                _authoringWebViewProfileDirectory, _authoringLibraryService);
            _studioAuthoring.DraftStateChanged += dirty => _viewModel.SetHyperlibContentDirty(dirty);
            _studioAuthoring.StatusChanged += message => _viewModel.ReportStatus(message);
            _studioAuthoring.ContentChanged += () => _ = _viewModel.HyperlibWorkspace!.RefreshAsync();
            _studioAuthoring.NewWorkRequested += () => _viewModel.RequestStudioNewProject();
            _studioAuthoring.PageTemplatesRequested += () => _ = OpenHyperlibStagingAsync(true);
            _studioAuthoring.PublicationRequested += () => _ = OpenHyperlibPublicationAsync();
            _studioAuthoring.WorkspaceActionRequested += request => _ = HandleWorkspaceActionAsync(request);
            _studioAuthoring.WorkspaceChromeReadyChanged += _ => { UpdateAuthoringPaneHeader(); ApplyAuthoringPanelLayout(); };
            _viewModel.SaveHyperlibContentAsync = _studioAuthoring.SaveAsync;
            _viewModel.PrepareHyperlibContentToLeaveAsync = _studioAuthoring.PrepareToLeaveAsync;
        }
        if (_studioAuthoring.Parent is ContentControl parent) parent.Content = null;
        return _studioAuthoring;
    }

    private void ScheduleWorkspaceChromeRefresh()
    {
        if (_isDisposed || _workspaceChromeRefreshQueued || !_viewModel.HasHyperlibAuthoring || _viewModel.IsHyperlibEditor) return;
        _workspaceChromeRefreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _workspaceChromeRefreshQueued = false;
            if (!_isDisposed) RefreshWorkspaceChrome();
        }, DispatcherPriority.Background);
    }

    private void RefreshWorkspaceChrome()
    {
        if (_studioAuthoring is null || _authoringPaneTabs is null || _authoringPaneNodeId is null) return;
        var tabs = _authoringPaneTabs.Items.OfType<TabItem>().Where(tab => tab.Tag is string).Select(tab =>
        {
            var id = (string)tab.Tag;
            return new StudioWorkspaceChromeTab(id, PaneTitle(id), true, "About " + PaneTitle(id));
        }).ToArray();
        _studioAuthoring.SetWorkspaceChrome(new StudioWorkspaceChromeSnapshot(tabs,
            (_authoringPaneTabs.SelectedItem as TabItem)?.Tag as string ?? "canvas", _viewModel.SaveCommand.CanExecute(null),
            GetAuthoringPanelSnapshot(true), GetAuthoringPanelSnapshot(false)));
        UpdateAuthoringPaneHeader();
        ApplyAuthoringPanelLayout();
    }

    private void UpdateAuthoringPaneHeader()
    {
        if (_authoringPaneTabs is null || _isDisposed) return;
        var proxyReady = _studioAuthoring?.IsWorkspaceChromeReady == true
            && ((_authoringPaneTabs.SelectedItem as TabItem)?.Tag as string) == "canvas";
        _authoringPaneTabs.Style = (Style)Resources[proxyReady ? "StudioDockedToolTabControlStyle" : "StudioCanvasTabControlStyle"];
        _authoringPaneTabs.Padding = proxyReady ? new Thickness(0) : new Thickness(4);
    }

    private bool UsesAuthoringPanels => _viewModel.HasHyperlibAuthoring && !_viewModel.IsHyperlibEditor
        && _studioAuthoring?.IsWorkspaceChromeReady == true
        && ((_authoringPaneTabs?.SelectedItem as TabItem)?.Tag as string) == "canvas";

    private void ConfigureAuthoringPanels()
    {
        if (!_viewModel.HasHyperlibAuthoring || _viewModel.IsHyperlibEditor) return;
        // Reuse the native, sorted HTML/Razor catalog rather than retaining two Elements libraries.
        var elements = LeftToolTabs.Items.OfType<TabItem>().Single(tab => (tab.Tag as string) == "elements");
        LeftToolTabs.Items.Remove(elements);
        RightToolTabs.Items.Insert(0, elements);
        var obsoleteTemplates = LeftToolTabs.Items.OfType<TabItem>().FirstOrDefault(tab => (tab.Tag as string) == "templates");
        if (obsoleteTemplates is not null) LeftToolTabs.Items.Remove(obsoleteTemplates);
        foreach (var id in new[] { "page", "events", "styles", "inspector" })
        {
            var tool = RightToolTabs.Items.OfType<TabItem>().Single(tab => (tab.Tag as string) == id);
            RightToolTabs.Items.Remove(tool);
            LeftToolTabs.Items.Add(tool);
        }
        LeftToolTabs.Items.Insert(0, new TabItem { Header = "Contents", Tag = "authoring-contents" });
        LeftToolTabs.Items.Add(new TabItem { Header = "Code", Tag = "authoring-code" });
        RightToolTabs.Items.Insert(0, new TabItem { Header = "Templates", Tag = "authoring-templates" });
        // The legacy inspectors describe the markup model. Authoring choices instead use the
        // selected Page object retained by the canvas controller, including nested template nodes.
        LeftToolTabs.SelectionChanged += AuthoringToolSelectionChanged;
        RightToolTabs.SelectionChanged += AuthoringToolSelectionChanged;
    }

    private static string PanelId(TabItem tab) => tab.Tag is string tag && tag.StartsWith("authoring-", StringComparison.Ordinal)
        ? tag[10..] : (tab.Tag as string) switch
        {
            "inspector" => "properties",
            "styles" => "appearance",
            "events" => "actions",
            _ => "native:" + (tab.Tag as string ?? tab.Header?.ToString() ?? "")
        };

    private bool IsAuthoringPanelProxy(TabItem tab) => _viewModel.HasHyperlibAuthoring && !_viewModel.IsHyperlibEditor
        && !PanelId(tab).StartsWith("native:", StringComparison.Ordinal);

    private bool IsAuthoringPanelHome(TabItem tab) => _viewModel.UsesUnifiedAuthoringCatalog
        && (tab.Tag as string) is "elements" or "page" or "events" or "styles" or "inspector";

    private StudioWorkspacePanelSnapshot GetAuthoringPanelSnapshot(bool left)
    {
        var tabs = left ? LeftToolTabs : RightToolTabs;
        var options = tabs.Items.OfType<TabItem>().Select(tab => new StudioWorkspacePanelOption(PanelId(tab), tab.Header?.ToString() ?? ""))
            .DistinctBy(option => option.Id).ToArray();
        var selected = left ? _leftAuthoringPanel : _rightAuthoringPanel;
        if (!options.Any(option => option.Id == selected))
        {
            // Native tools may move to a floating window or the opposite edge. Keep the
            // authoring selector valid while that tool retains its original floating behavior.
            selected = options.FirstOrDefault(option => option.Id == (left ? "contents" : "templates"))?.Id
                ?? options.FirstOrDefault()?.Id ?? "";
            if (left) _leftAuthoringPanel = selected; else _rightAuthoringPanel = selected;
        }
        return new StudioWorkspacePanelSnapshot(selected,
            left ? _leftAuthoringPanelOpen : _rightAuthoringPanelOpen, options);
    }

    private void AuthoringToolSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_coordinatingPanels || !_authoringPanelsInitialized || !ReferenceEquals(e.Source, sender) || sender is not TabControl tabs
            || tabs.SelectedItem is not TabItem tab) return;
        var id = PanelId(tab);
        if (!UsesAuthoringPanels && !id.StartsWith("native:", StringComparison.Ordinal))
        {
            SelectAuthoringPanel(ReferenceEquals(tabs, LeftToolTabs), id, true);
            _viewModel.RequestPaneContent("canvas");
            return;
        }
        if (UsesAuthoringPanels) SelectAuthoringPanel(ReferenceEquals(tabs, LeftToolTabs), id, true);
    }

    private void SelectAuthoringPanel(bool left, string id, bool open)
    {
        if (id == "properties") left = true;
        if (!GetAuthoringPanelSnapshot(left).Options.Any(option => option.Id == id)) return;
        if (left) { _leftAuthoringPanel = id; _leftAuthoringPanelOpen = open; }
        else { _rightAuthoringPanel = id; _rightAuthoringPanelOpen = open; }
        ApplyAuthoringPanelLayout();
        ScheduleWorkspaceChromeRefresh();
        if (!left && open && id == "native:elements") PreviewCatalogElement(_viewModel.SelectedDescriptor?.Id);
    }

    private async void PreviewCatalogElement(string? descriptorId)
    {
        if (_isDisposed || !_viewModel.UsesUnifiedAuthoringCatalog || _catalogElementCommitInFlight) return;
        try
        {
            _viewModel.RequestPaneContent("canvas");
            if (_studioAuthoring is null) _ = CreateHyperlibAuthoring();
            CatalogElementCommitButton.SetCurrentValue(IsEnabledProperty, false);
            var item = await _studioAuthoring!.PreviewCatalogElementAsync(descriptorId);
            if (!_catalogElementCommitInFlight && descriptorId == _viewModel.SelectedDescriptor?.Id)
            {
                CatalogElementCommitButton.SetCurrentValue(IsEnabledProperty, item?.Compatible == true);
                CatalogElementCommitButton.SetCurrentValue(ToolTipProperty, item?.Compatible == false ? item.Reason : "Add element to the current Page");
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException)
        { _viewModel.ReportStatus("The element preview could not load: " + error.Message); }
    }

    private async void CommitCatalogElement(string descriptorId)
    {
        if (_isDisposed || !_viewModel.UsesUnifiedAuthoringCatalog || _catalogElementCommitInFlight) return;
        _catalogElementCommitInFlight = true;
        CatalogElementPanel.SetCurrentValue(IsEnabledProperty, false);
        CatalogElementCommitButton.SetCurrentValue(IsEnabledProperty, false);
        CatalogElementList.SetCurrentValue(IsEnabledProperty, false);
        try
        {
            _viewModel.RequestPaneContent("canvas");
            if (_studioAuthoring is null) _ = CreateHyperlibAuthoring();
            await _studioAuthoring!.CommitCatalogElementAsync(descriptorId);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException)
        { _viewModel.ReportStatus("The element could not be added: " + error.Message); }
        finally
        {
            _catalogElementCommitInFlight = false;
            CatalogElementPanel.SetCurrentValue(IsEnabledProperty, true);
            CatalogElementList.SetCurrentValue(IsEnabledProperty, true);
            CatalogElementCommitButton.SetCurrentValue(IsEnabledProperty, _viewModel.AddElementCommand.CanExecute(null));
        }
    }

    private void ToggleAuthoringPanel(bool left)
    {
        if (left) _leftAuthoringPanelOpen = !_leftAuthoringPanelOpen;
        else _rightAuthoringPanelOpen = !_rightAuthoringPanelOpen;
        ApplyAuthoringPanelLayout();
        ScheduleWorkspaceChromeRefresh();
    }

    private void StudioLeftPanelToggleClick(object sender, RoutedEventArgs e)
    {
        if (UsesAuthoringPanels) ToggleAuthoringPanel(true);
        else _viewModel.ToggleLeftToolPanelCommand.Execute(null);
    }

    private void StudioRightPanelToggleClick(object sender, RoutedEventArgs e)
    {
        if (UsesAuthoringPanels) ToggleAuthoringPanel(false);
        else _viewModel.ToggleRightToolPanelCommand.Execute(null);
    }

    private async void AuthoringNativeActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action })
            await HandleWorkspaceActionAsync(new StudioWorkspaceAction(action, null, null));
    }

    private void SetLogicalPanelOpen(bool left, bool open)
    {
        if ((left ? _viewModel.IsLeftToolPanelOpen : _viewModel.IsRightToolPanelOpen) != open)
            (left ? _viewModel.ToggleLeftToolPanelCommand : _viewModel.ToggleRightToolPanelCommand).Execute(null);
    }

    private void ApplyAuthoringPanelLayout()
    {
        if (_coordinatingPanels || !_viewModel.HasHyperlibAuthoring || _viewModel.IsHyperlibEditor) return;
        _coordinatingPanels = true;
        try
        {
            var coordinated = UsesAuthoringPanels;
            if (coordinated) _authoringPanelsInitialized = true;
            foreach (var left in new[] { true, false })
            {
                var tabs = left ? LeftToolTabs : RightToolTabs;
                var selected = left ? _leftAuthoringPanel : _rightAuthoringPanel;
                var logicalOpen = left ? _leftAuthoringPanelOpen : _rightAuthoringPanelOpen;
                var native = selected.StartsWith("native:", StringComparison.Ordinal);
                if (coordinated)
                {
                    var tab = tabs.Items.OfType<TabItem>().FirstOrDefault(item => PanelId(item) == selected);
                    if (tab is not null) tabs.SelectedItem = tab;
                    SetLogicalPanelOpen(left, logicalOpen);
                }
                else if (tabs.SelectedItem is TabItem item && !PanelId(item).StartsWith("native:", StringComparison.Ordinal))
                {
                    tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(candidate => PanelId(candidate).StartsWith("native:", StringComparison.Ordinal));
                }
                var modelOpen = left ? _viewModel.IsLeftToolPanelOpen : _viewModel.IsRightToolPanelOpen;
                var actualOpen = modelOpen && (!coordinated || native);
                var host = left ? LeftToolPanelHost : RightToolPanelHost;
                host.SetCurrentValue(VisibilityProperty, actualOpen ? Visibility.Visible : Visibility.Collapsed);
                var splitter = left ? LeftToolPanelSplitter : RightToolPanelSplitter;
                splitter.SetCurrentValue(VisibilityProperty, actualOpen ? Visibility.Visible : Visibility.Collapsed);
                var hide = left ? StudioLeftPanelHideButton : StudioRightPanelHideButton;
                hide.SetCurrentValue(VisibilityProperty, actualOpen ? Visibility.Visible : Visibility.Collapsed);
                var column = StudioPanelGrid.ColumnDefinitions[left ? 0 : 4];
                var wasVisible = left ? _leftNativePanelVisible : _rightNativePanelVisible;
                if (!actualOpen)
                {
                    if (wasVisible != false && column.ActualWidth > 0)
                    {
                        // The logical toggle can already have driven the bound Width to zero,
                        // while ActualWidth still describes the panel before the next layout pass.
                        var retainedWidth = column.Width.IsAbsolute && column.Width.Value > 0
                            ? column.Width : new GridLength(column.ActualWidth);
                        if (left) _leftNativePanelWidth = retainedWidth;
                        else _rightNativePanelWidth = retainedWidth;
                    }
                    column.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                }
                else if (wasVisible == false)
                {
                    var retainedWidth = left ? _leftNativePanelWidth : _rightNativePanelWidth;
                    column.SetCurrentValue(ColumnDefinition.WidthProperty, retainedWidth.Value > 0
                        ? retainedWidth : new GridLength(left ? 300 : 340));
                }
                if (left) _leftNativePanelVisible = actualOpen; else _rightNativePanelVisible = actualOpen;
                StudioPanelGrid.ColumnDefinitions[left ? 1 : 3].SetCurrentValue(ColumnDefinition.WidthProperty,
                    new GridLength(actualOpen ? 5 : 0));
                var popup = left ? StudioLeftPanelEdgeRevealHost : StudioRightPanelEdgeRevealHost;
                popup.SetCurrentValue(Popup.IsOpenProperty, IsVisible && Window.GetWindow(this) is { IsVisible: true, WindowState: not WindowState.Minimized }
                    && (coordinated ? !logicalOpen : !modelOpen));
            }
            AuthoringNativePanelActions.Visibility = coordinated && _rightAuthoringPanelOpen && _rightAuthoringPanel.StartsWith("native:", StringComparison.Ordinal)
                ? Visibility.Visible : Visibility.Collapsed;
            AuthoringNativeSaveButton.IsEnabled = _viewModel.SaveCommand.CanExecute(null);
        }
        finally { _coordinatingPanels = false; }
    }

    private async Task HandleWorkspaceActionAsync(StudioWorkspaceAction request)
    {
        if (_isDisposed || _workspaceActionInFlight || _studioAuthoring?.IsWorkspaceChromeReady != true
            || _authoringPaneTabs is null || _authoringPaneNodeId is null) return;
        var tabs = _authoringPaneTabs.Items.OfType<TabItem>().ToArray();
        var requestedTab = tabs.FirstOrDefault(tab => (tab.Tag as string) == request.ContentId);
        if ((request.Action is "select" or "close" or "help") && requestedTab is null) return;
        _workspaceActionInFlight = true;
        try
        {
            switch (request.Action)
            {
                case "select":
                    _authoringPaneTabs.SelectedItem = requestedTab;
                    break;
                case "close":
                    if (request.ContentId == "canvas" && !await _studioAuthoring.SaveAsync()) return;
                    _viewModel.ClosePaneContent(_authoringPaneNodeId, request.ContentId!);
                    break;
                case "help": ShowPaneFileHelp(request.ContentId!); break;
                case "new":
                    if (request.Anchor is { } anchor)
                        ShowNewDocumentMenu(_studioAuthoring, TranslateWorkspaceAnchor(anchor, _studioAuthoring.RenderSize));
                    break;
                case "layouts": await OpenHyperlibStagingAsync(true); break;
                case "authoring": _viewModel.RequestPaneContent("canvas"); break;
                case "publish": await OpenHyperlibPublicationAsync(); break;
                case "save":
                    if (_viewModel.SaveCommand.CanExecute(null)) _viewModel.SaveCommand.Execute(null);
                    break;
                case "panel-select": SelectAuthoringPanel(request.Side == "left", request.ContentId!, true); break;
                case "panel-toggle": ToggleAuthoringPanel(request.Side == "left"); break;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        { _viewModel.ReportStatus("The workspace action could not complete: " + error.Message); }
        finally { _workspaceActionInFlight = false; ScheduleWorkspaceChromeRefresh(); }
    }

    private static Point TranslateWorkspaceAnchor(StudioWorkspaceAnchor anchor, Size surface)
    {
        // Web coordinates are CSS pixels. Convert against the actual browser surface rather than the
        // monitor DPI, so Windows scaling, WebView zoom, and compact drawers keep the menu at its button.
        var width = Math.Max(0, surface.Width);
        var height = Math.Max(0, surface.Height);
        return new Point(Math.Clamp(anchor.Left / anchor.ViewportWidth * width, 0, width),
            Math.Clamp((anchor.Top + anchor.Height) / anchor.ViewportHeight * height, 0, height));
    }

    public async Task OpenHyperlibContentAsync(string id)
    {
        if (!_viewModel.HasHyperlibAuthoring) return;
        _viewModel.RequestPaneContent("canvas");
        if (_studioAuthoring is null) _ = CreateHyperlibAuthoring();
        await _studioAuthoring!.NavigateToAsync(id);
    }

    private async Task NewHyperlibPageAsync()
    {
        _viewModel.RequestPaneContent("canvas");
        if (_studioAuthoring is null) _ = CreateHyperlibAuthoring();
        await _studioAuthoring!.NewPageAsync();
    }

    private async Task OpenHyperlibStagingAsync(bool startAtStaging)
    {
        if (_viewModel.HyperlibWorkspace?.Context is not { } context) return;
        if (_studioAuthoring is not null && !await _studioAuthoring.PrepareToLeaveAsync()) return;
        if (_viewModel.HasUnsavedChanges && !await _viewModel.SaveProjectAsync())
        {
            MessageBox.Show(Window.GetWindow(this), _viewModel.StatusMessage,
                "Save before staging", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var service = new HyperlibProjectPipelineService();
            var pipeline = await service.ReadAsync(context.ProjectRoot);
            var definition = await service.ReadDefinitionAsync(context.ProjectRoot);
            if (pipeline is null || definition is null) { _viewModel.RequestPaneContent("hyperlib-design"); return; }
            var content = await new HyperLib.Core.ContentStore(context.LibraryRoot!).ReadAsync();
            var catalogRoot = pipeline.SourceLibraryRoot is { } originalCatalog
                && Directory.Exists(originalCatalog) ? originalCatalog : context.LibraryRoot!;
            var dialog = new HyperlibProjectStagingDialog(catalogRoot, definition,
                async updated =>
                {
                    await new HyperlibWorkProjectService(catalogRoot).UpdateFoundationAsync(context.ProjectRoot, updated, pipeline.Revision, content.Revision);
                    await _viewModel.HyperlibWorkspace.RefreshAsync();
                }, startAtStaging, pipeline.Toolkit, context.ProjectRoot) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && _studioAuthoring is not null)
            {
                await _studioAuthoring.ReloadProjectHostAsync();
                await _studioAuthoring.NavigateToAsync(context.WorkId ?? "library");
            }
        }
        catch (Exception error) { _viewModel.HyperlibWorkspace.ReportExternalError(error.Message); }
    }

    private async Task OpenHyperlibPublicationAsync()
    {
        if (_viewModel.HyperlibWorkspace?.Context is not { } context) return;
        if (_studioAuthoring is not null && !await _studioAuthoring.SaveAsync()) return;
        if (_viewModel.HasUnsavedChanges && !await _viewModel.SaveProjectAsync())
        {
            MessageBox.Show(Window.GetWindow(this), _viewModel.StatusMessage,
                "Save before publication", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new StudioHyperlibPublicationDialog(context.ProjectRoot) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private FrameworkElement CreateHyperlibWorkspace()
    {
        var view = new HyperlibStudioWorkspaceView(_viewModel.HyperlibWorkspace!);
        view.TemplateSectionRequested += section => _ = EditHyperlibTemplateAsync(section);
        view.ProjectOpenRequested += path => _viewModel.OpenHyperlibProject(new HyperlibStudioProject("Library", path));
        _hyperlibViews.Add(view);
        return view;
    }

    private void OpenHyperlibChildProjectClick(object sender, RoutedEventArgs e)
    {
        if(sender is Button { Tag: HyperlibStudioProject project }) _viewModel.OpenHyperlibProject(project);
    }

    private void OnStudioPanelGridSizeChanged(
        object sender,
        SizeChangedEventArgs eventArgs)
    {
        RefreshEdgeRevealPopups();
    }

    private void RefreshEdgeRevealPopups()
    {
        RefreshPopupPlacement(StudioLeftPanelEdgeRevealHost);
        RefreshPopupPlacement(StudioRightPanelEdgeRevealHost);
        RefreshPopupPlacement(AiAssistantBottomEdgeRevealHost);
    }

    private static CustomPopupPlacement[] PlacePopupAtLeftEdge(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        return [new CustomPopupPlacement(new Point(0, 0), PopupPrimaryAxis.Vertical)];
    }

    private static CustomPopupPlacement[] PlacePopupAtRightEdge(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        return
        [
            new CustomPopupPlacement(
                new Point(Math.Max(0, targetSize.Width - popupSize.Width), 0),
                PopupPrimaryAxis.Vertical)
        ];
    }

    private static CustomPopupPlacement[] PlacePopupAtBottomEdge(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        return
        [
            new CustomPopupPlacement(
                new Point(0, Math.Max(0, targetSize.Height - popupSize.Height)),
                PopupPrimaryAxis.Horizontal)
        ];
    }

    private static void RefreshPopupPlacement(Popup popup)
    {
        if (!popup.IsOpen)
        {
            return;
        }

        var offset = popup.HorizontalOffset;
        popup.HorizontalOffset = offset + 0.1;
        popup.HorizontalOffset = offset;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (Window.GetWindow(this) is { } owner && !ReferenceEquals(owner, _panelOwner))
        {
            DetachPanelOwner();
            _panelOwner = owner;
            owner.StateChanged += OnStudioOwnerChanged;
            owner.LocationChanged += OnStudioOwnerChanged;
            owner.IsVisibleChanged += OnStudioVisibilityChanged;
        }
        // Reloading an existing project tab must preserve both selected tool panels.
        if (!_toolPlacementsRestored)
        {
            RestoreToolPlacements();
            ActivateTool(_viewModel.SelectedTool);
        }
        await InitializeWebViewsAsync();
        ApplyAiAssistantLayout();
        ScrollHyperpageAiMessagesToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        StudioLeftPanelEdgeRevealHost.SetCurrentValue(Popup.IsOpenProperty, false);
        StudioRightPanelEdgeRevealHost.SetCurrentValue(Popup.IsOpenProperty, false);
        DetachPanelOwner();
    }

    private void OnStudioVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => ApplyAuthoringPanelLayout();
    private void OnStudioOwnerChanged(object? sender, EventArgs e) { ApplyAuthoringPanelLayout(); RefreshEdgeRevealPopups(); }
    private void DetachPanelOwner()
    {
        if (_panelOwner is not { } owner) return;
        owner.StateChanged -= OnStudioOwnerChanged;
        owner.LocationChanged -= OnStudioOwnerChanged;
        owner.IsVisibleChanged -= OnStudioVisibilityChanged;
        _panelOwner = null;
    }

    private void PageTreeSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> eventArgs)
    {
        if (eventArgs.NewValue is VisualPageTreeNodeViewModel node)
        {
            _viewModel.SelectElement(node.ElementId);
        }
    }

    private void ProjectTreeSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> eventArgs)
    {
        if (eventArgs.NewValue is HyperpageProjectTreeNodeViewModel node)
        {
            _viewModel.SelectedProjectNode = node;
        }
    }

    private async void ProjectTreeMouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (_viewModel.SelectedProjectNode is not { } node)
        {
            return;
        }
        eventArgs.Handled = true;
        await _viewModel.OpenProjectNodeAsync(node);
    }

    private async void OpenProjectNodeClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        await _viewModel.OpenSelectedProjectNodeAsync();
    }

    private void NewProjectDocumentClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (sender is FrameworkElement placementTarget)
        {
            ShowNewDocumentMenu(placementTarget);
        }
    }

    private async void NewProjectFolderClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        var dialog = new ExplorerTextPromptDialog(
            "New folder",
            "Folder name",
            "Create",
            "Folder")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.CreateProjectDirectoryAsync(dialog.Value);
        }
    }

    private async void RenameProjectNodeClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (_viewModel.SelectedProjectNode is not { CanRename: true } node)
        {
            _viewModel.ReportStatus(
                "Select a user-owned file or folder before renaming it.");
            return;
        }
        var dialog = new ExplorerTextPromptDialog(
            "Rename",
            node.IsDirectory ? "Folder name" : "File name",
            "Rename",
            node.Name)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.RenameSelectedProjectNodeAsync(dialog.Value);
        }
    }

    private async void RefreshProjectTreeClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        await _viewModel.RefreshProjectTreeAsync();
    }

    private async void ImportAssetClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Hyperpage asset",
            Filter = "Supported assets|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.avif;*.svg;*.ico;*.woff;*.woff2;*.ttf|All files|*.*",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var metadataDialog = new HyperpageAssetImportDialog(dialog.FileName)
            {
                Owner = Window.GetWindow(this)
            };
            if (metadataDialog.ShowDialog() == true)
            {
                await _viewModel.ImportAssetFileAsync(
                    dialog.FileName,
                    metadataDialog.AssetName,
                    metadataDialog.AltText);
            }
        }
    }

    private async void ImportMarkupClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import bounded HTML or Razor",
            Filter = "HTML and Razor|*.html;*.htm;*.razor|HTML|*.html;*.htm|Razor|*.razor",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            await _viewModel.ImportMarkupFileAsync(dialog.FileName);
        }
    }

    private void ProjectPageSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (eventArgs.AddedItems.Count == 0
            || DataContext is not VisualPageStudioTabViewModel viewModel
            || !viewModel.OpenSelectedPageCommand.CanExecute(null))
        {
            return;
        }

        viewModel.OpenSelectedPageCommand.Execute(null);
    }

    private async void CreatePageClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        await ShowCreatePageDialogAsync();
    }

    private async Task ShowCreatePageDialogAsync()
    {
        if (Window.GetWindow(this) is { } owner)
        {
            await HyperpagePageCreationDialog.ShowAndCreateAsync(
                owner,
                _viewModel);
        }
    }

    private void CreateTemplateClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        ShowCreateTemplateDialog(HyperpageTemplateCreationSource.Code);
    }

    private void CreateTemplateFromSelectionClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        ShowCreateTemplateDialog(HyperpageTemplateCreationSource.Selection);
    }

    private void ShowCreateTemplateDialog(
        HyperpageTemplateCreationSource initialSource)
    {
        if (Window.GetWindow(this) is { } owner)
        {
            HyperpageTemplateCreationDialog.Show(
                owner,
                _viewModel,
                initialSource);
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        ScheduleWorkspaceChromeRefresh();
        if (!_coordinatingPanels && UsesAuthoringPanels
            && eventArgs.PropertyName is nameof(VisualPageStudioTabViewModel.IsLeftToolPanelOpen) or nameof(VisualPageStudioTabViewModel.IsRightToolPanelOpen))
        {
            if (eventArgs.PropertyName == nameof(VisualPageStudioTabViewModel.IsLeftToolPanelOpen))
                _leftAuthoringPanelOpen = _viewModel.IsLeftToolPanelOpen;
            else _rightAuthoringPanelOpen = _viewModel.IsRightToolPanelOpen;
            ApplyAuthoringPanelLayout();
        }
        if (string.Equals(
                eventArgs.PropertyName,
                nameof(VisualPageStudioTabViewModel.SelectedTool),
                StringComparison.Ordinal))
        {
            ActivateTool(_viewModel.SelectedTool);
            return;
        }
        if (string.Equals(
                eventArgs.PropertyName,
                nameof(VisualPageStudioTabViewModel.IsAiAssistantOpen),
                StringComparison.Ordinal))
        {
            ApplyAiAssistantLayout();
            return;
        }
        if (string.Equals(
                eventArgs.PropertyName,
                nameof(VisualPageStudioTabViewModel.ActiveAuthoringContentId),
                StringComparison.Ordinal)
            && _viewModel.HasActiveAuthoringDocument)
        {
            ActivateLanguageAuthoringPanels();
        }
    }

    private void ActivateLanguageAuthoringPanels()
    {
        if (FindToolTab("language-library") is { Parent: TabControl left })
        {
            left.SelectedItem = FindToolTab("language-library");
        }
        if (FindToolTab("language-builder") is { Parent: TabControl right })
        {
            right.SelectedItem = FindToolTab("language-builder");
        }
    }

    private void ApplyAiAssistantLayout()
    {
        AiAssistantSplitterRow.SetCurrentValue(
            RowDefinition.HeightProperty,
            _viewModel.AiAssistantSplitterHeight);
        AiAssistantPanelRow.SetCurrentValue(
            RowDefinition.HeightProperty,
            _viewModel.AiAssistantPanelHeight);
    }

    private void ActivateTool(VisualPageStudioToolDefinition? tool)
    {
        if (tool is null)
        {
            return;
        }
        if (tool.Id == "code")
        {
            _viewModel.RequestPaneContent(
                _viewModel.SupportsCSharpSource
                    ? "csharp"
                    : "generated-razor");
            return;
        }
        if (tool.Id == "ai")
        {
            _viewModel.OpenAiAssistant();
            ScrollHyperpageAiMessagesToEnd();
            return;
        }
        if (FindToolTab(tool.Id) is not { } tab)
        {
            return;
        }
        if (tab.Parent is TabControl tabs)
        {
            tabs.SelectedItem = tab;
        }
        var floating = _floatingToolWindows.FirstOrDefault(pair =>
            ReferenceEquals(pair.Value.Tab, tab));
        if (floating.Key is not null)
        {
            floating.Key.Activate();
        }
    }

    private void HyperpageAiPromptPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not Key.Enter and not Key.Return
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            || _viewModel.AiAssistant is not { } assistant)
        {
            return;
        }

        eventArgs.Handled = true;
        if (assistant.SendCommand.CanExecute(null))
        {
            assistant.SendCommand.Execute(null);
        }
        else
        {
            assistant.ExplainSendAvailability();
        }
    }

    private async void ApproveHyperpageAiReviewClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var assistant = _viewModel.AiAssistant;
        if (assistant?.PendingReview is not { } review
            || !assistant.CanReview)
        {
            return;
        }

        if (review.Kind == WorkspaceAiPendingActionKind.TerminalCommand
            && !review.RequiresNativeConfirmation)
        {
            await assistant.ApprovePendingAsync(
                userConfirmedExecution: false);
            return;
        }

        var consequence = review.Kind switch
        {
            WorkspaceAiPendingActionKind.VisualPagePreviewBuild =>
                "This builds an isolated project snapshot. Project C#, packages, and MSBuild targets run with your Windows permissions; it does not host or publish the page.",
            WorkspaceAiPendingActionKind.VisualPageMutation =>
                "This applies only the displayed semantic operations to the exact Hyperpage revision and leaves the project unsaved.",
            WorkspaceAiPendingActionKind.WorkspaceTextDelete =>
                "This permanently deletes the displayed file.",
            WorkspaceAiPendingActionKind.GitOperation =>
                "This executes the exact displayed Git operation through Hypershell's repository adapter.",
            WorkspaceAiPendingActionKind.OpenDocumentReplace =>
                "This replaces the live editor content through its typed provider and leaves the document unsaved.",
            _ when review.ExecutesCommand =>
                "This executes the displayed command in a normal shell with your Windows permissions.",
            WorkspaceAiPendingActionKind.TerminalCommand =>
                "This stages the displayed command without executing it.",
            _ =>
                "This atomically creates or replaces the reviewed workspace text file."
        };
        var owner = Window.GetWindow(this);
        var prompt = "Approve the exact AI action shown in the review card?\n\n"
                     + $"Target: {review.Target}\n\n"
                     + consequence
                     + "\n\nIf the content or target changed after review, "
                     + "Hypershell will reject the stale action.";
        var title = review.ExecutesCommand
            ? review.Kind == WorkspaceAiPendingActionKind.VisualPagePreviewBuild
                ? "Trust and build Hyperpage preview"
                : "Confirm AI terminal execution"
            : "Approve AI workspace action";
        var result = owner is null
            ? MessageBox.Show(
                prompt,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                prompt,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await assistant.ApprovePendingAsync(
                userConfirmedExecution: review.RequiresNativeConfirmation);
        }
    }

    private async void RejectHyperpageAiReviewClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.AiAssistant is { CanReview: true } assistant)
        {
            await assistant.RejectPendingAsync();
        }
    }

    private void OnHyperpageAiMessagesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs) =>
        ScrollHyperpageAiMessagesToEnd();

    private void HyperpageAiMessagesScrollViewerIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.NewValue is true)
        {
            ScrollHyperpageAiMessagesToEnd();
        }
    }

    private void ScrollHyperpageAiMessagesToEnd()
    {
        if (!IsLoaded
            || !HyperpageAiMessagesScrollViewer.IsVisible)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => HyperpageAiMessagesScrollViewer.ScrollToEnd());
    }

    private void FloatToolTabClick(object sender, RoutedEventArgs eventArgs)
    {
        if (GetContextToolTab(sender) is { } tab)
        {
            FloatToolTab(tab, bounds: null, updateModel: true);
        }
    }

    private void DockToolLeftClick(object sender, RoutedEventArgs eventArgs)
    {
        if (GetContextToolTab(sender) is { } tab)
        {
            DockToolTab(tab, LeftToolTabs, StudioToolHostKind.StudioLeft, updateModel: true);
        }
    }

    private void DockToolRightClick(object sender, RoutedEventArgs eventArgs)
    {
        if (GetContextToolTab(sender) is { } tab)
        {
            DockToolTab(tab, RightToolTabs, StudioToolHostKind.StudioRight, updateModel: true);
        }
    }

    private void RestoreToolPlacements()
    {
        if (_toolPlacementsRestored)
        {
            return;
        }
        _toolPlacementsRestored = true;
        var selectedLeftTool = LeftToolTabs.SelectedItem;
        var selectedRightTool = RightToolTabs.SelectedItem;
        foreach (var placement in _viewModel.Layout.ToolPlacements
                     .Where(value => value.IsVisible)
                     .OrderBy(value => value.Order))
        {
            if (FindToolTab(placement.ToolId) is not { } tab)
            {
                continue;
            }
            // Old placements can describe the former native inspector. Authoring owns
            // these surfaces now; retain their home selectors instead of restoring a stale window.
            if (IsAuthoringPanelProxy(tab) || IsAuthoringPanelHome(tab)) continue;
            switch (placement.Host)
            {
                case StudioToolHostKind.StudioLeft:
                case StudioToolHostKind.HypershellLeft:
                    DockToolTab(
                        tab,
                        LeftToolTabs,
                        StudioToolHostKind.StudioLeft,
                        updateModel: false);
                    break;
                case StudioToolHostKind.StudioRight:
                case StudioToolHostKind.HypershellRight:
                    DockToolTab(
                        tab,
                        RightToolTabs,
                        StudioToolHostKind.StudioRight,
                        updateModel: false);
                    break;
                case StudioToolHostKind.Floating:
                    FloatToolTab(tab, placement.FloatingBounds, updateModel: false);
                    break;
            }
        }
        if (selectedLeftTool is not null && LeftToolTabs.Items.Contains(selectedLeftTool))
        {
            LeftToolTabs.SelectedItem = selectedLeftTool;
        }
        if (selectedRightTool is not null && RightToolTabs.Items.Contains(selectedRightTool))
        {
            RightToolTabs.SelectedItem = selectedRightTool;
        }
    }

    private void FloatToolTab(
        TabItem tab,
        StudioFloatingBounds? bounds,
        bool updateModel)
    {
        if (IsAuthoringPanelProxy(tab) || IsAuthoringPanelHome(tab)) return;
        var existing = _floatingToolWindows.FirstOrDefault(pair =>
            ReferenceEquals(pair.Value.Tab, tab));
        if (existing.Key is not null)
        {
            existing.Key.Activate();
            return;
        }
        var returnHost = ReferenceEquals(tab.Parent, LeftToolTabs)
            ? StudioToolHostKind.StudioLeft
            : StudioToolHostKind.StudioRight;
        RemoveToolTab(tab);
        ScheduleWorkspaceChromeRefresh();
        var floatingTabs = new TabControl
        {
            Style = (Style)Resources["StudioTabControlStyle"],
            Background = (Brush)FindResource("AppBackgroundBrush"),
            BorderThickness = new Thickness(0)
        };
        floatingTabs.Items.Add(tab);
        floatingTabs.SelectedItem = tab;
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(5)
        };
        var dockLeft = new Button
        {
            Style = (Style)Resources["StudioToolButtonStyle"],
            Content = "Dock left",
            Margin = new Thickness(2),
            Padding = new Thickness(8, 3, 8, 3)
        };
        var dockRight = new Button
        {
            Style = (Style)Resources["StudioToolButtonStyle"],
            Content = "Dock right",
            Margin = new Thickness(2),
            Padding = new Thickness(8, 3, 8, 3)
        };
        toolbar.Children.Add(dockLeft);
        toolbar.Children.Add(dockRight);
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(floatingTabs);
        var owner = Window.GetWindow(this);
        var window = new Window
        {
            Owner = owner,
            // Floating content no longer inherits the Studio's logical-tree context.
            DataContext = _viewModel,
            Title = $"Hyperpage Studio · {tab.Header}",
            Width = Math.Max(260, bounds?.Width ?? 380),
            Height = Math.Max(220, bounds?.Height ?? 560),
            MinWidth = 240,
            MinHeight = 180,
            Background = (Brush)FindResource("AppBackgroundBrush"),
            Content = root,
            WindowStartupLocation = bounds is null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.Manual
        };
        if (bounds is not null)
        {
            window.Left = bounds.Left;
            window.Top = bounds.Top;
        }
        _floatingToolWindows[window] = new FloatingToolWindow(
            tab,
            floatingTabs,
            returnHost);
        dockLeft.Click += (_, _) => DockFloatingTool(
            window,
            LeftToolTabs,
            StudioToolHostKind.StudioLeft);
        dockRight.Click += (_, _) => DockFloatingTool(
            window,
            RightToolTabs,
            StudioToolHostKind.StudioRight);
        window.LocationChanged += OnFloatingToolBoundsChanged;
        window.SizeChanged += OnFloatingToolBoundsChanged;
        window.Closed += OnFloatingToolClosed;
        window.Show();
        if (updateModel && ToolId(tab) is { } toolId)
        {
            _viewModel.SetToolPlacement(
                toolId,
                StudioToolHostKind.Floating,
                WindowBounds(window));
        }
    }

    private void DockFloatingTool(
        Window window,
        TabControl target,
        StudioToolHostKind host)
    {
        if (!_floatingToolWindows.Remove(window, out var floating))
        {
            return;
        }
        floating.Host.Items.Remove(floating.Tab);
        window.LocationChanged -= OnFloatingToolBoundsChanged;
        window.SizeChanged -= OnFloatingToolBoundsChanged;
        window.Closed -= OnFloatingToolClosed;
        window.Content = null;
        window.Close();
        DockToolTab(floating.Tab, target, host, updateModel: true);
    }

    private void OnFloatingToolClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not Window window
            || !_floatingToolWindows.Remove(window, out var floating))
        {
            return;
        }
        window.LocationChanged -= OnFloatingToolBoundsChanged;
        window.SizeChanged -= OnFloatingToolBoundsChanged;
        floating.Host.Items.Remove(floating.Tab);
        window.Content = null;
        if (_isDisposed)
        {
            return;
        }
        var target = floating.ReturnHost == StudioToolHostKind.StudioLeft
            ? LeftToolTabs
            : RightToolTabs;
        DockToolTab(
            floating.Tab,
            target,
            floating.ReturnHost,
            updateModel: true);
    }

    private void OnFloatingToolBoundsChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not Window window
            || window.WindowState != WindowState.Normal
            || !_floatingToolWindows.TryGetValue(window, out var floating)
            || ToolId(floating.Tab) is not { } toolId)
        {
            return;
        }
        _viewModel.UpdateFloatingToolBounds(toolId, WindowBounds(window));
    }

    private void DockToolTab(
        TabItem tab,
        TabControl target,
        StudioToolHostKind host,
        bool updateModel)
    {
        if (IsAuthoringPanelProxy(tab)) return;
        RemoveToolTab(tab);
        if (!target.Items.Contains(tab))
        {
            target.Items.Add(tab);
        }
        target.SelectedItem = tab;
        if (updateModel && ToolId(tab) is { } toolId)
        {
            _viewModel.SetToolPlacement(toolId, host);
        }
        ScheduleWorkspaceChromeRefresh();
    }

    private static void RemoveToolTab(TabItem tab)
    {
        if (tab.Parent is TabControl tabs)
        {
            tabs.Items.Remove(tab);
        }
    }

    private TabItem? FindToolTab(string toolId) =>
        LeftToolTabs.Items.OfType<TabItem>()
            .Concat(RightToolTabs.Items.OfType<TabItem>())
            .Concat(_floatingToolWindows.Values.Select(value => value.Tab))
            .FirstOrDefault(tab => ToolIds(tab).Contains(
                toolId,
                StringComparer.Ordinal));

    private TabItem? GetContextToolTab(object sender)
    {
        if (sender is not MenuItem { Parent: ContextMenu contextMenu })
        {
            return null;
        }
        var tabs = contextMenu.PlacementTarget switch
        {
            TabControl tabControl => tabControl,
            ComboBox selector when ReferenceEquals(selector, LeftToolSelector) =>
                LeftToolTabs,
            ComboBox selector when ReferenceEquals(selector, RightToolSelector) =>
                RightToolTabs,
            _ => null
        };
        if (tabs?.SelectedItem is TabItem tab
            && ToolIds(tab).Count > 0)
        {
            if (IsAuthoringPanelHome(tab)) return null;
            if (_viewModel.HasHyperlibAuthoring && !PanelId(tab).StartsWith("native:", StringComparison.Ordinal)) return null;
            return tab;
        }
        return null;
    }

    private static IReadOnlyList<string> ToolIds(TabItem tab) =>
        (tab.Tag as string ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string? ToolId(TabItem tab)
    {
        var ids = ToolIds(tab);
        return ids.FirstOrDefault(id => string.Equals(
                   id,
                   _viewModel.SelectedTool?.Id,
                   StringComparison.Ordinal))
               ?? ids.FirstOrDefault();
    }

    private static StudioFloatingBounds WindowBounds(Window window) => new()
    {
        Left = window.Left,
        Top = window.Top,
        Width = window.ActualWidth > 0 ? window.ActualWidth : window.Width,
        Height = window.ActualHeight > 0 ? window.ActualHeight : window.Height
    };

    private void StudioPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var editingText = eventArgs.OriginalSource is
            System.Windows.Controls.Primitives.TextBoxBase
            or ComboBox { IsEditable: true };
        if (control && eventArgs.Key == Key.S)
        {
            Execute(_viewModel.SaveCommand);
            eventArgs.Handled = true;
        }
        else if (editingText)
        {
            return;
        }
        else if (control && eventArgs.Key == Key.Z)
        {
            Execute(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? _viewModel.RedoCommand
                : _viewModel.UndoCommand);
            eventArgs.Handled = true;
        }
        else if (control && eventArgs.Key == Key.Y)
        {
            Execute(_viewModel.RedoCommand);
            eventArgs.Handled = true;
        }
        else if (control && eventArgs.Key == Key.C)
        {
            Execute(_viewModel.CopyCommand);
            eventArgs.Handled = true;
        }
        else if (control && eventArgs.Key == Key.X)
        {
            Execute(_viewModel.CutCommand);
            eventArgs.Handled = true;
        }
        else if (control && eventArgs.Key == Key.V)
        {
            Execute(_viewModel.PasteCommand);
            eventArgs.Handled = true;
        }
        else if (control && eventArgs.Key == Key.D)
        {
            Execute(_viewModel.DuplicateCommand);
            eventArgs.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
                 && eventArgs.Key == Key.Up)
        {
            Execute(_viewModel.MoveUpCommand);
            eventArgs.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
                 && eventArgs.Key == Key.Down)
        {
            Execute(_viewModel.MoveDownCommand);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            Execute(_viewModel.SelectParentCommand);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Delete)
        {
            Execute(_viewModel.DeleteCommand);
            eventArgs.Handled = true;
        }
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void RebuildPaneLayout()
    {
        if (_rebuilding || _isDisposed)
        {
            return;
        }
        _rebuilding = true;
        try
        {
            DisposeWebViews();
            _authoringPaneTabs = null;
            _authoringPaneNodeId = null;
            _sourceEditors.Clear();
            PaneHost.Content = CreateLayoutNode(_viewModel.Layout.Root);
            RefreshWorkspaceChrome();
        }
        finally
        {
            _rebuilding = false;
        }
        if (IsLoaded)
        {
            _ = InitializeWebViewsAsync();
        }
    }

    private FrameworkElement CreateLayoutNode(StudioLayoutNode node)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane)
        {
            return CreatePane(node);
        }

        var first = node.First ?? new StudioLayoutNode
        {
            Kind = StudioLayoutNodeKind.Pane,
            PaneContents = ["canvas"],
            ActiveContentId = "canvas"
        };
        var second = node.Second ?? new StudioLayoutNode
        {
            Kind = StudioLayoutNodeKind.Pane,
            PaneContents = ["generated-razor"],
            ActiveContentId = "generated-razor"
        };
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(11, 15, 20)) };
        var proportion = Math.Clamp(node.FirstProportion, 0.15, 0.85);
        if (node.Orientation == StudioSplitOrientation.Horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(proportion, GridUnitType.Star),
                MinWidth = 180
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1 - proportion, GridUnitType.Star),
                MinWidth = 180
            });
            var firstView = CreateLayoutNode(first);
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            var secondView = CreateLayoutNode(second);
            splitter.DragCompleted += (_, _) =>
            {
                var total = firstView.ActualWidth + secondView.ActualWidth;
                if (total > 0)
                {
                    _viewModel.SetSplitProportion(
                        node.NodeId,
                        firstView.ActualWidth / total);
                }
            };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(secondView, 2);
            grid.Children.Add(firstView);
            grid.Children.Add(splitter);
            grid.Children.Add(secondView);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(proportion, GridUnitType.Star),
                MinHeight = 130
            });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1 - proportion, GridUnitType.Star),
                MinHeight = 130
            });
            var firstView = CreateLayoutNode(first);
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            var secondView = CreateLayoutNode(second);
            splitter.DragCompleted += (_, _) =>
            {
                var total = firstView.ActualHeight + secondView.ActualHeight;
                if (total > 0)
                {
                    _viewModel.SetSplitProportion(
                        node.NodeId,
                        firstView.ActualHeight / total);
                }
            };
            Grid.SetRow(splitter, 1);
            Grid.SetRow(secondView, 2);
            grid.Children.Add(firstView);
            grid.Children.Add(splitter);
            grid.Children.Add(secondView);
        }
        return grid;
    }

    private FrameworkElement CreatePane(StudioLayoutNode node)
    {
        if (node.PaneContents.Count == 0)
        {
            return CreateEmptyPane();
        }
        var isCanvasPane = node.PaneContents.Contains(
            "canvas",
            StringComparer.Ordinal);
        var focusedWorkspace = _viewModel.IsHyperlibEditor && isCanvasPane
            && node.PaneContents.Count <= 1;
        var tabs = new TabControl
        {
            Style = (Style)Resources[focusedWorkspace
                ? "StudioDockedToolTabControlStyle"
                : isCanvasPane && !_viewModel.IsHyperlibEditor
                    ? "StudioCanvasTabControlStyle"
                    : "StudioTabControlStyle"],
            Background = (Brush)FindResource("AppBackgroundBrush"),
            Padding = focusedWorkspace ? new Thickness(0) : new Thickness(4, 4, isCanvasPane ? 4 : 34, 4),
            BorderThickness = new Thickness(0),
            Tag = node.NodeId,
            AllowDrop = !_viewModel.IsHyperlibEditor
        };
        tabs.Drop += OnPaneDrop;
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedItem is TabItem { Tag: string selectedContent })
            {
                _viewModel.SelectPaneContent(node.NodeId, selectedContent);
                ScheduleWorkspaceChromeRefresh();
            }
        };
        if (isCanvasPane && _viewModel.HasHyperlibAuthoring && !_viewModel.IsHyperlibEditor)
        {
            _authoringPaneTabs = tabs;
            _authoringPaneNodeId = node.NodeId;
        }
        IEnumerable<string> contents = node.PaneContents;
        foreach (var contentId in contents)
        {
            var tab = new TabItem
            {
                Header = CreatePaneTabHeader(node.NodeId, contentId),
                Tag = contentId,
                Content = CreatePaneContent(contentId)
            };
            if (isCanvasPane && !_viewModel.IsHyperlibEditor)
            {
                tab.SetBinding(MaxWidthProperty, new Binding(nameof(ScrollViewer.ViewportWidth))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollViewer), 1)
                });
            }
            else if (!isCanvasPane)
            {
                tab.SetBinding(MaxWidthProperty, new Binding(nameof(ActualWidth))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WrapPanel), 1)
                });
            }
            tabs.Items.Add(tab);
            if (string.Equals(
                    contentId,
                    node.ActiveContentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                tabs.SelectedItem = tab;
            }
        }
        if (!isCanvasPane && !_viewModel.IsHyperlibEditor)
        {
            tabs.Items.Add(CreateNewDocumentTab());
        }
        if (tabs.SelectedItem is null && tabs.Items.Count > 0)
        {
            tabs.SelectedIndex = 0;
        }
        if (isCanvasPane)
        {
            return tabs;
        }

        var pane = new Grid();
        pane.Children.Add(tabs);
        var paneHelp = new Button
        {
            Style = (Style)Resources["StudioToolbarIconButtonStyle"], Content = "?", Width = 28, Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 38, 0), ToolTip = "About this document"
        };
        AutomationProperties.SetName(paneHelp, "Explain active document");
        paneHelp.Click += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            if (tabs.SelectedItem is TabItem { Tag: string contentId }) ShowPaneFileHelp(contentId);
        };
        Panel.SetZIndex(paneHelp, 20);
        pane.Children.Add(paneHelp);
        var close = new Button
        {
            Style = (Style)Resources["StudioPaneCloseButtonStyle"],
            Content = new TextBlock
            {
                Text = "×",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 5, 0),
            ToolTip = "Close this code pane"
        };
        AutomationProperties.SetName(close, "Close code pane");
        close.Click += (_, _) => _viewModel.ClosePane(node.NodeId);
        Panel.SetZIndex(close, 20);
        pane.Children.Add(close);
        return pane;
    }

    private FrameworkElement CreateEmptyPane()
    {
        var actions = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(new TextBlock
        {
            Text = "No open tabs",
            Foreground = (Brush)FindResource("MutedForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var canvas = new Button
        {
            Content = "Open canvas",
            Command = _viewModel.CanvasOnlyCommand,
            Style = (Style)Resources["StudioToolButtonStyle"]
        };
        AutomationProperties.SetName(canvas, "Open canvas");
        buttons.Children.Add(canvas);
        var create = new Button
        {
            Content = new TextBlock { Text = "+", FontSize = 18 },
            Style = (Style)Resources["StudioIconButtonStyle"],
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "New document"
        };
        AutomationProperties.SetName(create, "Create document");
        create.Click += (_, _) => ShowNewDocumentMenu(create);
        buttons.Children.Add(create);
        actions.Children.Add(buttons);
        return new Border { Background = (Brush)FindResource("AppBackgroundBrush"), Child = actions };
    }

    private TabItem CreateNewDocumentTab()
    {
        var tab = new TabItem
        {
            Header = new TextBlock
            {
                Text = "+",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(5, 0, 5, 1)
            },
            ToolTip = "New document",
            IsTabStop = false
        };
        AutomationProperties.SetName(tab, "New document");
        tab.PreviewMouseLeftButtonDown += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            ShowNewDocumentMenu(tab);
        };
        return tab;
    }

    private void ShowNewDocumentMenu(FrameworkElement placementTarget, Point? workspaceAnchor = null)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        if (workspaceAnchor is { } anchor)
        {
            menu.Placement = PlacementMode.RelativePoint;
            menu.HorizontalOffset = anchor.X;
            menu.VerticalOffset = anchor.Y;
        }
        var open = new MenuItem { Header = "Open project file…", ToolTip = "Open an existing file from this project" };
        open.Click += async (_, _) => await OpenProjectFileAsync();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        if (_viewModel.HasHyperlibAuthoring)
        {
            var contentPage = new MenuItem { Header = "Content Page…", ToolTip = "Create a Page from this Work's reusable Page template" };
            contentPage.Click += async (_, _) => await NewHyperlibPageAsync();
            menu.Items.Add(contentPage);
            menu.Items.Add(new Separator());
        }
        if (!_viewModel.HasHyperlibAuthoring)
        {
            var page = new MenuItem { Header = "Page…", ToolTip = "Create a page in this web project" };
            page.Click += async (_, _) => await ShowCreatePageDialogAsync();
            menu.Items.Add(page);
        }
        menu.Items.Add(CreateNewDocumentMenuItem(HyperpageDocumentTypeCatalog.GetRequired("css")));
        menu.Items.Add(new Separator());
        foreach (var sectionName in new[] { "Styles", "Code" })
        {
            var section = new MenuItem { Header = sectionName };
            foreach (var definition in HyperpageDocumentTypeCatalog.Definitions
                         .Where(definition => definition.Section.Equals(
                             sectionName,
                             StringComparison.Ordinal)))
            {
                section.Items.Add(CreateNewDocumentMenuItem(definition));
            }
            menu.Items.Add(section);
        }
        menu.IsOpen = true;
    }

    private async Task OpenProjectFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open project file", InitialDirectory = _viewModel.ProjectRoot,
            Filter = "Project source files|*.md;*.json;*.html;*.cshtml;*.razor;*.css;*.js;*.ts;*.cs;*.svg;*.txt;*.xml;*.csproj;*.ps1|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var relativePath = Path.GetRelativePath(_viewModel.ProjectRoot, dialog.FileName).Replace('\\', '/');
        try
        {
            VisualPageProjectService.ResolveProjectPath(_viewModel.ProjectRoot, relativePath);
            await _viewModel.OpenProjectNodeAsync(new HyperpageProjectTreeNodeViewModel
            {
                Name = Path.GetFileName(relativePath), RelativePath = relativePath, IsDirectory = false
            });
        }
        catch (VisualPageValidationException)
        {
            MessageBox.Show(Window.GetWindow(this), "Choose a file inside this project's folder.", "Open project file", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SelectedProjectFileHelpClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var path = _viewModel.SelectedProjectNode is { IsDirectory: false } selected
            ? selected.RelativePath : "README.md";
        ShowProjectFileHelp(path);
    }

    private void ShowPaneFileHelp(string contentId)
    {
        if (_viewModel.TryGetProjectDocument(contentId, out var document) && document is not null)
        {
            ShowProjectFileHelp(document.RelativePath);
            return;
        }
        var project = _viewModel.Project;
        var page = _viewModel.Document;
        var path = contentId switch
        {
            "generated-razor" => VisualPageProjectService.GeneratedMarkupPath(project, page),
            "generated-css" => _viewModel.ActivePageFileType == HyperpagePageFileType.RazorPage
                ? $"src/Pages/Generated/{page.ComponentName}.razor.css" : $"src/Host/wwwroot/assets/{page.Slug}.generated.css",
            "generated-csharp" => $"src/Pages/Generated/{page.ComponentName}.designer.g.cs",
            "css" => VisualPageProjectService.UserCssPath(project, page),
            "js" => VisualPageProjectService.UserJavaScriptPath(project, page),
            "csharp" => VisualPageProjectService.UserCSharpPath(project, page),
            "document" => project.Pages.FirstOrDefault(item => item.PageId == page.PageId)?.DocumentPath ?? "",
            _ => contentId
        };
        ShowProjectFileHelp(path);
    }

    private void ShowProjectFileHelp(string relativePath)
    {
        var help = relativePath == "canvas" && _viewModel.HasHyperlibAuthoring
            ? new StudioProjectFileHelp
            {
                RelativePath = "Authoring", Title = "Your Hyperlib Work", Ownership = "Work and Page content",
                Purpose = "Compose the Work landing page and its content pages using the reusable templates copied into this project.",
                EditGuidance = "Select the Work landing page or create a content Page from one of your named Page templates. Insert objects, write text, and add media. Templates opens the guided builder: Background, Header, Body, Navigation, Footer, and Extras. Changes to a shared template preserve the content of Pages that use it.",
                GettingStarted = "Begin with the Work landing layout, then add Pages one at a time, choosing the appropriate Page template. Save and preview your content before using Publication to copy the completed Work into Hyperlib. README.md explains the builder, file map, and private preview host."
            }
            : new StudioProjectFileHelpService().GetHelp(_viewModel.ProjectRoot, relativePath);
        help ??= new StudioProjectFileHelp
        {
            RelativePath = relativePath, Title = PaneTitle(relativePath), Ownership = "Studio view",
            Purpose = relativePath == "runtime-preview" ? "Displays the running project in its actual web host." : relativePath == "build-output" ? "Shows compiler diagnostics and preview build results." : "Displays generated output for the active page.",
            EditGuidance = "Use the project's source files and visual workspace to make changes, then save and preview.",
            GettingStarted = "Open README.md for the project-specific file map and next steps."
        };
        new StudioFileHelpWindow(help) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private MenuItem CreateNewDocumentMenuItem(
        HyperpageDocumentTypeDefinition definition)
    {
        var item = new MenuItem
        {
            Header = definition.Id == "css" ? "CSS stylesheet (.css)…" : definition.DisplayName,
            ToolTip = definition.Description
        };
        item.Click += async (_, _) =>
            await ShowCreateProjectDocumentDialogAsync(definition);
        return item;
    }

    private async Task ShowCreateProjectDocumentDialogAsync(
        HyperpageDocumentTypeDefinition definition)
    {
        if (definition.Id == "css" && !_viewModel.HasHyperlibAuthoring && Window.GetWindow(this) is { } owner)
        {
            await HyperpagePageCreationDialog.ShowAndCreateAsync(owner, _viewModel, stylesheet: true);
            return;
        }
        var dialog = new ExplorerTextPromptDialog(
            "New " + definition.DisplayName,
            "File name",
            "Create",
            definition.DefaultBaseName + definition.Extension)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.CreateProjectDocumentAsync(
                definition.Id,
                dialog.Value);
        }
    }

    private FrameworkElement CreatePaneTabHeader(string nodeId, string contentId)
    {
        _viewModel.TryGetProjectDocument(contentId, out var projectDocument);
        var header = new TextBlock
        {
            Text = PaneTitle(contentId),
            Tag = contentId,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(3, 1, 3, 1),
            ToolTip = projectDocument?.RelativePath
                      ?? "Drag this pane tab to another Studio pane."
        };
        if (projectDocument is not null)
        {
            header.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(HyperpageProjectDocumentViewModel.Title))
                {
                    Source = projectDocument,
                    Mode = BindingMode.OneWay
                });
        }
        header.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(TabItem.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(TabItem), 1)
        });
        if (_viewModel.IsHyperlibEditor) return header;
        header.PreviewMouseMove += (_, eventArgs) =>
        {
            if (eventArgs.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var data = new DataObject();
            data.SetData(PaneContentDragFormat, contentId);
            DragDrop.DoDragDrop(header, data, DragDropEffects.Move);
            eventArgs.Handled = true;
        };
        var tabHeader = new Grid { Tag = contentId };
        tabHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tabHeader.Children.Add(header);
        var close = new Button
        {
            Style = (Style)Resources["StudioTabCloseButtonStyle"],
            Content = new TextBlock { Text = "×", FontSize = 14 },
            Tag = contentId,
            ToolTip = "Close " + PaneTitle(contentId)
        };
        AutomationProperties.SetName(close, contentId == "canvas" ? "Close canvas tab" : "Close document tab");
        close.Click += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            _viewModel.ClosePaneContent(nodeId, contentId);
        };
        Grid.SetColumn(close, 1);
        tabHeader.Children.Add(close);
        return tabHeader;
    }

    private void ActivePaneFileHelpClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DependencyObject? current = sender as DependencyObject;
        while (current is not null && current is not TabControl) current = VisualTreeHelper.GetParent(current);
        if (current is TabControl { SelectedItem: TabItem { Tag: string contentId } }) ShowPaneFileHelp(contentId);
    }

    private void OnPaneDrop(object sender, DragEventArgs eventArgs)
    {
        if (sender is not TabControl { Tag: string nodeId }
            || !eventArgs.Data.GetDataPresent(PaneContentDragFormat)
            || eventArgs.Data.GetData(PaneContentDragFormat) is not string contentId)
        {
            return;
        }
        eventArgs.Effects = _viewModel.MovePaneContent(contentId, nodeId)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private FrameworkElement CreatePaneContent(string contentId)
    {
        if (_viewModel.TryGetProjectDocument(contentId, out var projectDocument)
            && projectDocument is not null)
        {
            return CreateProjectDocumentEditor(contentId, projectDocument);
        }
        return contentId switch
        {
            "canvas" => _viewModel.HasHyperlibAuthoring ? CreateHyperlibAuthoring() : CreateCanvas(),
            "hyperlib-design" => CreateHyperlibWorkspace(),
            "runtime-preview" => CreateRuntimePreview(),
            "generated-razor" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.GeneratedRazor),
                "html",
                readOnly: true),
            "generated-css" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.GeneratedCss),
                "css",
                readOnly: true),
            "generated-csharp" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.GeneratedCSharp),
                "csharp",
                readOnly: true),
            "csharp" when !_viewModel.SupportsCSharpSource =>
                CreateSourceUnavailable(
                    "This project uses HTML. Add browser behavior in JavaScript, or create a host-side C# file from the document menu."),
            "csharp" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.UserCSharp),
                "csharp",
                readOnly: false),
            "css" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.UserCss),
                "css",
                readOnly: false),
            "javascript" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.UserJavaScript),
                "javascript",
                readOnly: false),
            "document" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.DocumentJson),
                "json",
                readOnly: true),
            "build-output" => CreateEditor(
                contentId,
                nameof(VisualPageStudioTabViewModel.BuildOutput),
                "plaintext",
                readOnly: true),
            _ => new TextBlock
            {
                Text = $"Pane '{contentId}' is not available.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(12)
            }
        };
    }

    private static FrameworkElement CreateSourceUnavailable(string message) =>
        new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(139, 148, 158)),
                TextWrapping = TextWrapping.Wrap
            }
        };

    private WebView2 CreateCanvas()
    {
        var webView = CreateWebView();
        _canvas = webView;
        return webView;
    }

    private WebView2 CreateRuntimePreview()
    {
        var webView = CreateWebView();
        _runtimePreview = webView;
        return webView;
    }

    private WebView2 CreateWebView()
    {
        var view = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 11, 15, 20)
        };
        _webViews.Add(view);
        return view;
    }

    private FrameworkElement CreateEditor(
        string contentId,
        string path,
        string languageId,
        bool readOnly)
    {
        var editor = new MonacoEditorControl
        {
            IsReadOnly = readOnly,
            LanguageId = languageId,
            EditorOptions = EditorLanguageCatalog.CreateDefaultSettings(
                languageId,
                detectIndentation: true) with
            {
                WordWrap = false
            }
        };
        editor.SetBinding(MonacoEditorControl.TextProperty, new Binding(path)
        {
            Source = _viewModel,
            Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
            UpdateSourceTrigger = readOnly
                ? UpdateSourceTrigger.Default
                : UpdateSourceTrigger.PropertyChanged
        });
        editor.SaveRequested += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            if (_viewModel.SaveCommand.CanExecute(null))
            {
                _viewModel.SaveCommand.Execute(null);
            }
        };
        editor.NewDocumentRequested += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            ShowNewDocumentMenu(editor);
        };
        if (!readOnly)
        {
            _sourceEditors[contentId] = editor;
            var surface = new DockPanel();
            var notice = CreateRecoveryNoticeSurface(() => _viewModel.GetUserSourceContent(contentId), out var text);
            void RefreshNotice()
            {
                text.Text = _viewModel.GetUserSourceRecoveryNotice(contentId);
                notice.Visibility = string.IsNullOrEmpty(text.Text) ? Visibility.Collapsed : Visibility.Visible;
            }
            PropertyChangedEventHandler changed = (_, args) =>
            {
                if (args.PropertyName == nameof(VisualPageStudioTabViewModel.UserSourceRecoveryVersion)) RefreshNotice();
            };
            surface.Loaded += (_, _) => { _viewModel.PropertyChanged += changed; RefreshNotice(); };
            surface.Unloaded += (_, _) => _viewModel.PropertyChanged -= changed;
            RefreshNotice();
            DockPanel.SetDock(notice, Dock.Top); surface.Children.Add(notice); surface.Children.Add(editor);
            return surface;
        }
        return editor;
    }

    private FrameworkElement CreateProjectDocumentEditor(
        string contentId,
        HyperpageProjectDocumentViewModel document)
    {
        var editor = new MonacoEditorControl
        {
            DataContext = document,
            IsReadOnly = document.IsReadOnly,
            LanguageId = document.MonacoLanguageId,
            EditorOptions = document.EditorOptions
        };
        editor.SetBinding(
            MonacoEditorControl.TextProperty,
            new Binding(nameof(HyperpageProjectDocumentViewModel.Content))
            {
                Source = document,
                Mode = document.IsReadOnly
                    ? BindingMode.OneWay
                    : BindingMode.TwoWay,
                UpdateSourceTrigger = document.IsReadOnly
                    ? UpdateSourceTrigger.Default
                    : UpdateSourceTrigger.PropertyChanged
            });
        editor.SetBinding(
            MonacoEditorControl.ViewStateProperty,
            new Binding(nameof(HyperpageProjectDocumentViewModel.ViewState))
            {
                Source = document,
                Mode = BindingMode.TwoWay
            });
        editor.SaveRequested += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            if (_viewModel.SaveCommand.CanExecute(null))
            {
                _viewModel.SaveCommand.Execute(null);
            }
        };
        editor.NewDocumentRequested += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            ShowNewDocumentMenu(editor);
        };
        if (!document.IsReadOnly)
        {
            _sourceEditors[contentId] = editor;
        }
        FrameworkElement content = editor;
        if (document.LanguageId == "css" && !document.IsReadOnly)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            var link = new CheckBox
            {
                Content = "Use on this page",
                IsEnabled = _viewModel.CanUseStylesheet(document),
                IsChecked = _viewModel.IsStylesheetLinked(document),
                Margin = new Thickness(12, 9, 12, 9),
                ToolTip = "Link this plain CSS file to the current page only. Save to include it in preview and export."
            };
            AutomationProperties.SetName(link, "Use stylesheet on this page");
            var updating = false;
            void RefreshLink()
            {
                updating = true;
                link.IsEnabled = !_viewModel.IsBusy && _viewModel.CanUseStylesheet(document);
                link.IsChecked = _viewModel.IsStylesheetLinked(document);
                updating = false;
            }
            link.Click += (_, _) =>
            {
                if (!updating)
                {
                    _viewModel.SetStylesheetLinked(document, link.IsChecked == true);
                    RefreshLink();
                }
            };
            PropertyChangedEventHandler changed = (_, args) =>
            {
                if (args.PropertyName is nameof(VisualPageStudioTabViewModel.StylesheetPaths)
                    or nameof(VisualPageStudioTabViewModel.IsBusy))
                {
                    RefreshLink();
                }
            };
            grid.Loaded += (_, _) =>
            {
                _viewModel.PropertyChanged += changed;
                RefreshLink();
            };
            grid.Unloaded += (_, _) => _viewModel.PropertyChanged -= changed;
            grid.Children.Add(link);
            Grid.SetRow(editor, 1);
            grid.Children.Add(editor);
            content = grid;
        }
        var surface = new DockPanel();
        var recovery = CreateDocumentRecoveryNotice(document);
        DockPanel.SetDock(recovery, Dock.Top);
        surface.Children.Add(recovery);
        surface.Children.Add(content);
        return surface;
    }

    internal static FrameworkElement CreateDocumentRecoveryNotice(HyperpageProjectDocumentViewModel document)
    {
        var notice = CreateRecoveryNoticeSurface(() => document.Content, out var text);
        notice.SetBinding(VisibilityProperty, new Binding(nameof(HyperpageProjectDocumentViewModel.HasRecoveryNotice))
        { Source = document, Converter = new BooleanToVisibilityConverter() });
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(HyperpageProjectDocumentViewModel.RecoveryNotice)) { Source = document });
        return notice;
    }

    private static Border CreateRecoveryNoticeSurface(Func<string> content, out TextBlock text)
    {
        var notice = new Border { Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        notice.SetResourceReference(Border.BackgroundProperty, "PanelRaisedBrush");
        notice.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var row = new DockPanel { LastChildFill = true };
        var copy = new Button { Content = "Copy draft", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(copy, "Copy recovered document text");
        copy.ToolTip = "Copy the full recovered text to paste into another document";
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(content()); }
            catch (ExternalException) { copy.ToolTip = "The clipboard is busy. Select the draft text and copy it, or try again."; }
        };
        DockPanel.SetDock(copy, Dock.Right);
        row.Children.Add(copy);
        text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundBrush");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        row.Children.Add(text);
        notice.Child = row;
        return notice;
    }

    private async Task InitializeWebViewsAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        try
        {
            var environment = await GetEnvironmentAsync();
            if (_canvas is { CoreWebView2: null } canvas)
            {
                await canvas.EnsureCoreWebView2Async(environment);
                ConfigureCanvas(canvas);
                NavigateCanvas();
            }
            if (_runtimePreview is { CoreWebView2: null } preview)
            {
                await preview.EnsureCoreWebView2Async(environment);
                ConfigureRuntimePreview(preview);
                var requestedUrl = string.IsNullOrWhiteSpace(_requestedRuntimePreviewUrl)
                    ? _viewModel.RuntimePreviewUrl : _requestedRuntimePreviewUrl;
                if (!string.IsNullOrWhiteSpace(requestedUrl))
                {
                    NavigateRuntimePreview(requestedUrl);
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or NotSupportedException
                                         or COMException)
        {
            _viewModel.ReportStatus(
                $"The Hyperpage Studio web surface could not start: {exception.Message}");
        }
    }

    private static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (EnvironmentGate)
        {
            return _environmentTask ??= CreateEnvironmentAsync();
        }
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        // Reuse the legacy WebView profile so the product rename preserves browser state.
        var userData = Path.Combine(
            new SettingsService().ApplicationDataDirectory,
            "visual-page-studio");
        Directory.CreateDirectory(userData);
        return CoreWebView2Environment.CreateAsync(userDataFolder: userData);
    }

    private void ConfigureCanvas(WebView2 canvas)
    {
        var core = canvas.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.WebMessageReceived += OnCanvasWebMessageReceived;
        core.NavigationStarting += OnCanvasNavigationStarting;
        core.NewWindowRequested += OnCanvasNewWindowRequested;
        core.ProcessFailed += OnCanvasProcessFailed;
        core.AddWebResourceRequestedFilter(
            "*",
            CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnCanvasWebResourceRequested;
    }

    private void ConfigureRuntimePreview(WebView2 preview)
    {
        var core = preview.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += OnRuntimeNavigationStarting;
        core.NewWindowRequested += OnRuntimeNewWindowRequested;
        core.ProcessFailed += OnRuntimeProcessFailed;
    }

    private void NavigateCanvas()
    {
        if (_canvas?.CoreWebView2 is null)
        {
            return;
        }
        _canvas.NavigateToString(_viewModel.CanvasHtml);
    }

    private void NavigateRuntimePreview(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp)
        {
            return;
        }
        _requestedRuntimePreviewUrl = url;
        _runtimeOrigin = uri.GetLeftPart(UriPartial.Authority);
        _runtimePreview?.CoreWebView2?.Navigate(url);
    }

    private void OnCanvasWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using var document = JsonDocument.Parse(eventArgs.TryGetWebMessageAsString());
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString();
            var elementId = root.TryGetProperty("elementId", out var element)
                ? element.GetString()
                : null;
            if (kind == "select" && elementId is not null)
            {
                SelectCanvasElement(elementId);
            }
            else if (kind == "move"
                     && elementId is not null
                     && root.TryGetProperty("parentElementId", out var parent)
                     && parent.GetString() is { } parentId
                     && root.TryGetProperty("index", out var index))
            {
                _viewModel.MoveElement(elementId, parentId, index.GetInt32());
            }
            else if (kind == "resize"
                     && elementId is not null
                     && root.TryGetProperty("deltaX", out var delta)
                     && root.TryGetProperty("startWidth", out var width))
            {
                _viewModel.ResizeElement(
                    elementId,
                    delta.GetDouble(),
                    width.GetDouble());
            }
            else if (kind == "zoom"
                     && root.TryGetProperty("delta", out var zoomDelta))
            {
                _viewModel.AdjustCanvasZoom(zoomDelta.GetDouble());
            }
            else if (kind == "command"
                     && root.TryGetProperty("command", out var command)
                     && command.GetString() is { } commandName)
            {
                _viewModel.ExecuteCanvasCommand(commandName);
            }
            else if (kind == "context" && elementId is not null)
            {
                SelectCanvasElement(elementId);
                var x = root.TryGetProperty("x", out var xValue)
                    ? xValue.GetDouble()
                    : 0;
                var y = root.TryGetProperty("y", out var yValue)
                    ? yValue.GetDouble()
                    : 0;
                OpenCanvasContextMenu(x, y);
            }
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidOperationException or FormatException)
        {
            _viewModel.ReportStatus(
                $"The canvas ignored an invalid interaction message: {exception.Message}");
        }
    }

    private void SelectCanvasElement(string elementId)
    {
        _viewModel.SelectElement(elementId);
        _viewModel.SelectedTool = _viewModel.StudioTools.FirstOrDefault(tool =>
            string.Equals(tool.Id, "inspector", StringComparison.Ordinal));
    }

    private void OpenCanvasContextMenu(double x, double y)
    {
        if (_canvas is null)
        {
            return;
        }
        var menu = new ContextMenu
        {
            DataContext = _viewModel,
            PlacementTarget = _canvas,
            Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint,
            HorizontalOffset = x,
            VerticalOffset = y
        };
        var selected = _viewModel.SelectedElement;
        var descriptor = _viewModel.SelectedElementDescriptor;
        menu.Items.Add(new MenuItem
        {
            Header = selected is null || descriptor is null
                ? "Selected element"
                : $"{selected.Name}  <{descriptor.MarkupName}>",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        });
        AddMenu(menu, "Undo", _viewModel.UndoCommand, "Ctrl+Z");
        AddMenu(menu, "Redo", _viewModel.RedoCommand, "Ctrl+Y");
        menu.Items.Add(new Separator());
        AddMenu(menu, "Edit properties", _viewModel.OpenInspectorCommand);
        if (_viewModel.SelectedElementAllowsChildren)
        {
            var addChild = new MenuItem { Header = "Add child" };
            foreach (var childDescriptor in
                     _viewModel.SelectedElementSuggestedChildren)
            {
                addChild.Items.Add(new MenuItem
                {
                    Header = $"{childDescriptor.DisplayName}  <{childDescriptor.MarkupName}>",
                    Command = _viewModel.AddDescriptorCommand,
                    CommandParameter = childDescriptor.Id,
                    ToolTip = childDescriptor.Description
                });
            }
            if (addChild.Items.Count == 0)
            {
                addChild.IsEnabled = false;
            }
            menu.Items.Add(addChild);
        }
        menu.Items.Add(new Separator());
        AddMenu(menu, "Cut", _viewModel.CutCommand);
        AddMenu(menu, "Copy", _viewModel.CopyCommand);
        AddMenu(
            menu,
            _viewModel.SelectedElementAllowsChildren
                ? "Paste inside"
                : "Paste after",
            _viewModel.PasteCommand);
        AddMenu(menu, "Duplicate", _viewModel.DuplicateCommand);
        menu.Items.Add(new Separator());
        if (_viewModel.SelectedElementSupportsBindings)
        {
            AddMenu(menu, "Add state binding", _viewModel.QuickAddBindingCommand);
        }
        if (_viewModel.SelectedElementSupportsEvents)
        {
            AddMenu(menu, "Add event handler", _viewModel.AddEventCommand);
        }
        if (_viewModel.SelectedElementSupportsBindings
            || _viewModel.SelectedElementSupportsEvents)
        {
            menu.Items.Add(new Separator());
        }
        AddMenu(menu, "Wrap in container", _viewModel.WrapCommand);
        AddMenu(menu, "Unwrap", _viewModel.UnwrapCommand);
        AddMenu(menu, "Select parent", _viewModel.SelectParentCommand);
        AddMenu(menu, "Move up", _viewModel.MoveUpCommand);
        AddMenu(menu, "Move down", _viewModel.MoveDownCommand);
        AddMenu(menu, "Lock / unlock", _viewModel.ToggleLockCommand);
        AddMenu(menu, "Hide / show", _viewModel.ToggleHiddenCommand);
        menu.Items.Add(new Separator());
        var saveTemplate = new MenuItem
        {
            Header = "Save selection as template",
            IsEnabled = selected is not null
        };
        saveTemplate.Click += CreateTemplateFromSelectionClick;
        menu.Items.Add(saveTemplate);
        AddMenu(menu, "Open generated source", _viewModel.OpenSourceCommand);
        menu.Items.Add(new Separator());
        AddMenu(menu, "Delete", _viewModel.DeleteCommand);
        menu.IsOpen = true;
    }

    private static void AddMenu(
        ItemsControl menu,
        string header,
        ICommand command,
        string inputGestureText = "") => menu.Items.Add(new MenuItem
        {
            Header = header,
            Command = command,
            InputGestureText = inputGestureText
        });

    private void OnCanvasNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!IsAllowedCanvasNavigation(eventArgs.Uri, eventArgs.IsUserInitiated))
        {
            eventArgs.Cancel = true;
        }
    }

    private static bool IsAllowedCanvasNavigation(
        string uri,
        bool isUserInitiated) =>
        uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
        || !isUserInitiated
        && uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);

    private static void OnCanvasNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
    }

    private void OnCanvasWebResourceRequested(
        object? sender,
        CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (!Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var uri)
            || uri.Scheme is "about" or "data")
        {
            return;
        }
        if (_canvas?.CoreWebView2?.Environment is { } environment)
        {
            if (IsCanvasResourceContextAllowed(eventArgs.ResourceContext)
                && _viewModel.TryGetCanvasResource(uri, out var content, out var contentType))
            {
                eventArgs.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(content, writable: false), 200, "OK",
                    "Content-Type: " + contentType
                    + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nAccess-Control-Allow-Origin: *");
                return;
            }
            eventArgs.Response = environment.CreateWebResourceResponse(
                Stream.Null,
                403,
                "Blocked by Hyperpage Studio design-canvas policy",
                "Content-Type: text/plain");
        }
    }

    public static bool IsCanvasResourceContextAllowed(CoreWebView2WebResourceContext context) =>
        context is CoreWebView2WebResourceContext.Stylesheet
            or CoreWebView2WebResourceContext.Image or CoreWebView2WebResourceContext.Font;

    private void OnCanvasProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        _viewModel.ReportStatus(
            "The design canvas process stopped. Hypershell is rebuilding the sanitized canvas.");
        Dispatcher.BeginInvoke(RebuildPaneLayout);
    }

    private void OnRuntimeNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_runtimeOrigin)
            || !Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri)
            || !string.Equals(
                uri.GetLeftPart(UriPartial.Authority),
                _runtimeOrigin,
                StringComparison.OrdinalIgnoreCase))
        {
            eventArgs.Cancel = true;
        }
    }

    private static void OnRuntimeNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
    }

    private void OnRuntimeProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        _viewModel.ReportStatus(
            "The isolated runtime preview process stopped; rebuild or restart the host to continue.");
    }

    private void OnCanvasInvalidated(object? sender, EventArgs eventArgs) =>
        NavigateCanvas();

    private void OnLayoutChanged(object? sender, EventArgs eventArgs) =>
        RebuildPaneLayout();

    private void OnRuntimePreviewRequested(object? sender, string url)
    {
        NavigateRuntimePreview(url);
        _viewModel.RequestPaneContent("runtime-preview");
        NavigateRuntimePreview(url);
    }

    public void ShowRuntimePreview(string url) => OnRuntimePreviewRequested(this, url);

    private void OnPaneRequested(object? sender, string paneId) =>
        SelectPane(paneId);

    private void OnProjectDocumentInsertionRequested(
        object? sender,
        HyperpageDocumentInsertionRequest request)
    {
        if (!_sourceEditors.TryGetValue(request.ContentId, out var editor)
            || !editor.InsertText(request.Text))
        {
            _viewModel.AppendToActiveAuthoringDocument(request.Text);
            return;
        }
        editor.FocusEditor();
    }

    private void SelectPane(string paneId)
    {
        if (FindTabControl(PaneHost, paneId) is not { } match)
        {
            return;
        }
        match.Control.SelectedItem = match.Item;
    }

    private static TabMatch? FindTabControl(DependencyObject root, string paneId)
    {
        if (root is TabControl tabs)
        {
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                if (string.Equals(item.Tag as string, paneId, StringComparison.OrdinalIgnoreCase))
                {
                    return new TabMatch(tabs, item);
                }
            }
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var match = FindTabControl(
                VisualTreeHelper.GetChild(root, index),
                paneId);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    private string PaneTitle(string contentId) => contentId switch
    {
        "canvas" => _viewModel.HasHyperlibAuthoring ? "Authoring" : "Canvas",
        "hyperlib-design" => "Project design",
        "runtime-preview" => "Runtime Preview",
        "generated-razor" => _viewModel.GeneratedMarkupLabel,
        "generated-css" => "Generated CSS",
        "generated-csharp" => "Designer C#",
        "csharp" => "C#",
        "css" => "CSS",
        "javascript" => "JavaScript",
        "document" => "Visual Document",
        "build-output" => "Build",
        _ => _viewModel.PaneTitle(contentId) is { Length: > 0 } title
            ? title
            : contentId
    };

    private void DisposeWebViews()
    {
        foreach (var workspace in _hyperlibViews) workspace.Dispose();
        _hyperlibViews.Clear();
        foreach (var view in _webViews)
        {
            if (view.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= OnCanvasWebMessageReceived;
                core.NavigationStarting -= OnCanvasNavigationStarting;
                core.NewWindowRequested -= OnCanvasNewWindowRequested;
                core.ProcessFailed -= OnCanvasProcessFailed;
                core.WebResourceRequested -= OnCanvasWebResourceRequested;
                core.NavigationStarting -= OnRuntimeNavigationStarting;
                core.NewWindowRequested -= OnRuntimeNewWindowRequested;
                core.ProcessFailed -= OnRuntimeProcessFailed;
            }
            view.Dispose();
        }
        _webViews.Clear();
        _canvas = null;
        _runtimePreview = null;
    }

    public void Dispose()
    {
        _studioAuthoring?.Dispose();
        _studioAuthoring = null;
        _viewModel.SaveHyperlibContentAsync = null;
        _viewModel.PrepareHyperlibContentToLeaveAsync = null;
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        IsVisibleChanged -= OnStudioVisibilityChanged;
        DetachPanelOwner();
        StudioPanelGrid.SizeChanged -= OnStudioPanelGridSizeChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _viewModel.CanvasInvalidated -= OnCanvasInvalidated;
        _viewModel.LayoutChanged -= OnLayoutChanged;
        _viewModel.RuntimePreviewRequested -= OnRuntimePreviewRequested;
        _viewModel.PaneRequested -= OnPaneRequested;
        _viewModel.ProjectDocumentInsertionRequested -=
            OnProjectDocumentInsertionRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CatalogElementPreviewRequested -= PreviewCatalogElement;
        _viewModel.CatalogElementCommitRequested -= CommitCatalogElement;
        if (_viewModel.HyperlibWorkspace is { } hyperlib)
        {
            hyperlib.OpenRelativeCodeRequested -= OpenHyperlibSource;
            hyperlib.EditTemplateRequested -= OpenHyperlibTemplate;
        }
        if (_viewModel.AiAssistant is { } assistant)
        {
            assistant.Messages.CollectionChanged -=
                OnHyperpageAiMessagesChanged;
        }
        foreach (var window in _floatingToolWindows.Keys.ToList())
        {
            window.Close();
        }
        _floatingToolWindows.Clear();
        DisposeWebViews();
        PaneHost.Content = null;
    }

    private sealed record TabMatch(TabControl Control, TabItem Item);

    private sealed record FloatingToolWindow(
        TabItem Tab,
        TabControl Host,
        StudioToolHostKind ReturnHost);
}

public sealed class VisualPageStudioDetachedWindow : Window
{
    private readonly VisualPageStudioView _view;
    private readonly VisualPageStudioTabViewModel _viewModel;

    public VisualPageStudioDetachedWindow(
        VisualPageStudioTabViewModel viewModel,
        Window owner)
    {
        _viewModel = viewModel;
        Owner = owner;
        Title = $"Hyperpage Studio · {viewModel.Title}";
        Width = 1400;
        Height = 900;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));
        _view = new VisualPageStudioView(viewModel);
        Content = _view;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Closed -= OnClosed;
        _view.Dispose();
        Content = null;
        _viewModel.IsDetached = false;
    }
}
