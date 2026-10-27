using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HyperLib.Core;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hypershell.Controls;

/// <summary>The content-aware starting point for a library or one independent Work project.</summary>
public partial class HyperlibStudioWorkspaceView : UserControl, IDisposable
{
    private readonly HyperlibStudioWorkspaceViewModel _model;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private WebView2? _preview;
    private Uri? _previewEndpoint;
    private string? _previewUrl;
    private string? _previewPage;
    private bool _previewTickBusy;
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private long _previewRequestId;
    private bool _disposed;
    private bool _initializedNavigation;
    private TabItem? _returnTab;
    private HyperlibStudioWorkDesign? _workDesign;
    private readonly Dictionary<string, JsonElement> _workAppearance = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _appearanceErrors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _appearanceTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HyperlibStudioAppearanceDraft> _workDrafts;
    private long _workDesignRequest;
    private string? _appearanceWorkId;
    private bool _appearanceDirty;
    public event Action<string>? TemplateSectionRequested;
    public event Action<string>? ProjectOpenRequested;

    public HyperlibStudioWorkspaceView(HyperlibStudioWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        _model = viewModel;
        _workDrafts = viewModel.AppearanceDrafts;
        DataContext = viewModel;
        _model.PropertyChanged += ModelChanged;
        _model.PreviewRequested += PreviewRequested;
        _model.DiscardAppearanceDraftsRequested += DiscardAppearanceDrafts;
        _previewTimer.Tick += PreviewTick;
        SizeChanged += (_, _) => UpdateLayoutForWidth();
        IsVisibleChanged += (_, _) => UpdatePreviewLease();
        TemplatesList.Loaded += (_, _) => { if (_model.SelectedTemplate is { } template) TemplatesList.ScrollIntoView(template); };
        UpdateSummary();
        UpdateLayoutForWidth();
        if (viewModel.SelectedWork is not null) _ = LoadWorkAppearanceAsync();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        UpdateSummary();
        UpdateLayoutForWidth();
        if (args.PropertyName == nameof(HyperlibStudioWorkspaceViewModel.SelectedTemplate) && _model.SelectedTemplate is { } template)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_disposed && _model.SelectedTemplate?.Id == template.Id) TemplatesList.ScrollIntoView(template);
            }));
        if (args.PropertyName == nameof(HyperlibStudioWorkspaceViewModel.SelectedWorkContent) && _model.SelectedWorkContent is { } content)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_disposed && _model.SelectedWorkContent?.Id == content.Id) WorkContentsList.ScrollIntoView(content);
            }));
        if (args.PropertyName == nameof(HyperlibStudioWorkspaceViewModel.SelectedWork)
            && (_appearanceWorkId != _model.SelectedWork?.Id || !_appearanceDirty))
            _ = LoadWorkAppearanceAsync();
    }

    private void UpdateSummary()
    {
        if (!_initializedNavigation && _model.Context is not null)
        {
            _initializedNavigation = true;
            WorkspaceTabs.SelectedItem = _model.IsLibraryRoot ? TemplatesTab : _model.HasLibraryContent ? WorkTab : TemplateTab;
        }
        WorkspaceTitle.Text = _model.IsLibraryRoot ? "Hyperlib Editor" : _model.ScopeTitle;
        WorkspaceDescription.Text = _model.ScopeDescription;
        TemplateSummary.Text = _model.TemplateSummary;
        ScopeLabel.Text = _model.IsLibraryRoot ? "ELEMENT LIBRARIES" : _model.HasLibraryContent ? "HYPERLIB · WORK" : "HYPERLIB · TEMPLATE";
        TemplatesTab.Visibility = _model.IsLibraryRoot ? Visibility.Visible : Visibility.Collapsed;
        TemplateTab.Visibility = !_model.IsLibraryRoot && !_model.HasLibraryContent ? Visibility.Visible : Visibility.Collapsed;
        WorkTab.Visibility = PagesTab.Visibility = _model.HasLibraryContent ? Visibility.Visible : Visibility.Collapsed;
        EmptyPagesNote.Visibility = _model.Pages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPagesNote.Text = _model.HasLibraryContent
            ? _model.SelectedWork is null ? "Choose a Work to browse its Pages." : "No Pages match. Open this Work in Studio to create and write a Page."
            : "Content belongs to the original library. This independent project can still edit its template and source.";
        PageSummary.Text = _model.SelectedPage is null ? "Choose a Page to open in Studio."
            : "Write text and add media in Studio using this Work's elements and page structure.";
        NewWorkButton.IsEnabled = !_model.IsBusy && _model.WorkParents.Count > 0;
        NewPageButton.IsEnabled = WorkWriterButton.IsEnabled = _model.SelectedWork is { } work && _model.OpenContentCommand.CanExecute(work);
        NewPartButton.IsEnabled = NewChapterButton.IsEnabled = _model.CanMutate && _model.SelectedWork is not null;
        WorkLayoutButton.IsEnabled = WorkRegionsButton.IsEnabled = WorkElementsButton.IsEnabled = !_model.IsBusy && _model.SelectedWork is not null;
        OpenWorkContentButton.IsEnabled = _model.SelectedWorkContent is { } content && _model.OpenContentCommand.CanExecute(content.Node);
        RenameWorkContentButton.IsEnabled = _model.CanMutate && _model.SelectedWorkContent is not null;
        EmptyWorkContentsNote.Visibility = _model.HasWorkContents ? Visibility.Collapsed : Visibility.Visible;
        PageSearchBox.Visibility = _model.IsLibraryRoot ? Visibility.Collapsed : Visibility.Visible;
        CaptureWorkTemplateButton.IsEnabled = _model.CanManageTemplates && _model.TemplateSourceWorks.Count > 0;
        EmptyTemplatesNote.Visibility = _model.HasTemplates ? Visibility.Collapsed : Visibility.Visible;
        TemplateDetailPanel.Visibility = _model.SelectedTemplate is null ? Visibility.Collapsed : Visibility.Visible;
        EditSavedTemplateButton.IsEnabled = DuplicateTemplateButton.IsEnabled = _model.CanManageTemplates && _model.SelectedTemplate is not null;
        UseTemplateButton.IsEnabled = _model.CanMutate && _model.SelectedTemplate is not null && _model.WorkParents.Count > 0;
        EditSavedTemplateButton.Content = _model.SelectedTemplate?.IsBuiltIn == true ? "Customize element library" : "Edit element library";
        OpenTemplateSourceProjectButton.IsEnabled = _model.CanManageTemplates && _model.SelectedTemplate is not null;
        OpenTemplateSourceProjectButton.Content = _model.SelectedTemplate?.IsBuiltIn == true ? "Copy source workspace" : "Source workspace";
        TemplateOwnershipNote.Text = _model.SelectedTemplate?.IsBuiltIn == true
            ? "Built-in element library. Customize creates your own editable copy."
            : "Your reusable element library. Works keep their own copies of these tools.";
        SaveWorkAppearanceButton.IsEnabled = _appearanceDirty && _appearanceErrors.Count == 0 && _model.CanMutate && _workDesign is not null;
        ReloadWorkAppearanceButton.Visibility = _appearanceDirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLayoutForWidth()
    {
        if (_model is null) return;
        var pagesSelected = WorkspaceTabs.SelectedItem == PagesTab;
        var showNavigator = _model.IsLibraryRoot && _model.HasLibraryContent
            && (pagesSelected || WorkspaceTabs.SelectedItem == WorkTab || WorkspaceTabs.SelectedItem == PreviewTab);
        Navigator.Visibility = showNavigator ? Visibility.Visible : Visibility.Collapsed;
        var stacked = showNavigator && ActualWidth is > 0 and < 780;
        var compactWork = WorkspaceTabs.SelectedItem == WorkTab
            && (ActualWidth is > 0 and < 780 || ActualHeight is > 0 and < 820);
        WorkspaceDescription.Visibility = WorkDescription.Visibility = WorkTemplateSummary.Visibility =
            compactWork ? Visibility.Collapsed : Visibility.Visible;
        WorkContentsHelp.Visibility = compactWork ? Visibility.Collapsed : Visibility.Visible;
        WorkIdentityCard.Padding = compactWork ? new Thickness(14, 9, 14, 9) : new Thickness(16, 12, 16, 12);
        WorkIdentityActions.Margin = compactWork ? new Thickness(0, 4, 0, 0) : new Thickness(0, 10, 0, 0);
        NavigatorHeading.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        var compactWorkNavigator = stacked && WorkspaceTabs.SelectedItem == WorkTab;
        NavigatorHeader.Visibility = compactWorkNavigator ? Visibility.Collapsed : Visibility.Visible;
        NavigatorSearchBox.Visibility = compactWorkNavigator ? Visibility.Collapsed : Visibility.Visible;
        NavigatorColumn.Width = new GridLength(showNavigator && !stacked ? 245 : 0);
        NavigatorRow.Height = stacked ? new GridLength(compactWorkNavigator ? 64 : 112) : new GridLength(1, GridUnitType.Star);
        DetailRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumnSpan(Navigator, stacked ? 2 : 1);
        Grid.SetRow(WorkspaceTabs, stacked ? 1 : 0);
        Navigator.Margin = stacked ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 14, 0);
        WorksList.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        CompactWorksSelector.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        var compactTemplates = ActualWidth is > 0 and < 780;
        TemplateBrowserColumn.Width = compactTemplates ? new GridLength(0) : new GridLength(280);
        TemplateBrowserRow.Height = compactTemplates ? new GridLength(112) : new GridLength(1, GridUnitType.Star);
        TemplateDetailRow.Height = compactTemplates ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumnSpan(TemplateBrowser, compactTemplates ? 2 : 1);
        Grid.SetRow(TemplateDetailPanel, compactTemplates ? 2 : 1);
        TemplateBrowser.Margin = compactTemplates ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 14, 0);
        TemplatesList.Visibility = compactTemplates ? Visibility.Collapsed : Visibility.Visible;
        CompactTemplatesSelector.Visibility = compactTemplates ? Visibility.Visible : Visibility.Collapsed;
        NewWorkButton.Visibility = _model.HasLibraryContent && WorkspaceTabs.SelectedItem == WorkTab ? Visibility.Visible : Visibility.Collapsed;
        NewPageButton.Visibility = _model.HasLibraryContent && pagesSelected ? Visibility.Visible : Visibility.Collapsed;
        UndoContentButton.Visibility = RedoContentButton.Visibility = _model.HasLibraryContent
            && (pagesSelected || WorkspaceTabs.SelectedItem == WorkTab) ? Visibility.Visible : Visibility.Collapsed;
        LibraryButton.Visibility = _model.HasLibraryContent && !_model.IsLibraryRoot ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Route the Studio object's compact creation menu into this workspace.</summary>
    public void RequestAction(string kind)
    {
        switch (kind)
        {
            case "new-template": WorkspaceTabs.SelectedItem = TemplatesTab; _ = ShowTemplateDialogAsync("blank"); break;
            case "new-work": WorkspaceTabs.SelectedItem = WorkTab; ShowCreateWorkDialog(); break;
            case "new-page": WorkspaceTabs.SelectedItem = PagesTab; OpenWorkWriter(); break;
            case "library":
                if (_model.IsLibraryRoot) WorkspaceTabs.SelectedItem = TemplatesTab;
                else if (_model.Context?.LibraryRoot is { } root) ProjectOpenRequested?.Invoke(root);
                break;
            case "template": WorkspaceTabs.SelectedItem = _model.IsLibraryRoot ? TemplatesTab : TemplateTab; break;
            case "work": WorkspaceTabs.SelectedItem = WorkTab; break;
            case "page": WorkspaceTabs.SelectedItem = PagesTab; break;
        }
    }

    private async Task LoadWorkAppearanceAsync(bool discardDraft = false)
    {
        var request = ++_workDesignRequest;
        if (_workDesign is { } previous && _appearanceDirty && !discardDraft)
            _workDrafts[previous.WorkId] = new(previous, new(_workAppearance), new(_appearanceTexts), new(_appearanceErrors));
        if (discardDraft && _appearanceWorkId is { } discarded) _workDrafts.Remove(discarded);
        _appearanceWorkId = _model.SelectedWork?.Id;
        _workDesign = null; _appearanceDirty = false; _workAppearance.Clear(); _appearanceErrors.Clear(); _appearanceTexts.Clear();
        WorkAppearancePanel.Children.Clear(); SaveWorkAppearanceButton.IsEnabled = false;
        ReloadWorkAppearanceButton.Visibility = Visibility.Collapsed;
        WorkAppearanceStatus.Text = _appearanceWorkId is null ? "Choose a Work to set its shared appearance." : "Loading shared appearance…";
        WorkTemplateSummary.Text = "";
        UpdateAppearanceDraftState();
        if (_appearanceWorkId is not { } workId) return;
        try
        {
            _workDrafts.TryGetValue(workId, out var draft);
            var design = draft?.Design ?? await _model.GetWorkDesignAsync(workId);
            if (_disposed || request != _workDesignRequest || design is null) return;
            _workDesign = design;
            foreach (var pair in draft?.Values ?? design.Appearance) _workAppearance[pair.Key] = pair.Value.Clone();
            if (draft is not null)
            {
                foreach (var pair in draft.Texts) _appearanceTexts[pair.Key] = pair.Value;
                foreach (var pair in draft.Errors) _appearanceErrors[pair.Key] = pair.Value;
                _appearanceDirty = true;
            }
            WorkTemplateSummary.Text = "Element library: " + design.TemplateTitle;
            var basic = new[] { "background", "surface", "accent", "ink", "muted", "font", "headingFont", "fontSize" };
            foreach (var id in basic)
                if (design.Definition.Appearance.FirstOrDefault(field => field.Id == id) is { } field)
                    AddAppearanceControl(field, design.EffectiveAppearance.TryGetValue(id, out var value) ? value : HyperlibTemplateEditSession.InitialValue(field));
            WorkAppearanceStatus.Text = _appearanceErrors.Values.FirstOrDefault() ?? (_appearanceDirty ? "Unsaved Work appearance changes."
                : WorkAppearancePanel.Children.Count == 0
                ? "This Template exposes no shared appearance controls." : "These defaults apply throughout the Work.");
            ReloadWorkAppearanceButton.Visibility = _appearanceDirty ? Visibility.Visible : Visibility.Collapsed;
            SaveWorkAppearanceButton.IsEnabled = _appearanceDirty && _appearanceErrors.Count == 0 && _model.CanMutate;
            UpdateAppearanceDraftState();
        }
        catch (Exception error)
        {
            if (!_disposed && request == _workDesignRequest) WorkAppearanceStatus.Text = error.Message;
        }
    }

    private void AddAppearanceControl(FieldDefinition field, JsonElement initial)
    {
        var row = new Grid { Margin = new(0, 0, 0, 10), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock { Text = field.Id == "fontSize" ? "Text size (px)" : field.Title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 12, 0) };
        row.Children.Add(label);
        FrameworkElement editor;
        var raw = _appearanceTexts.TryGetValue(field.Id, out var draftText) ? draftText : HyperlibTemplateEditSession.ValueText(initial);
        if (field.Type == "select")
        {
            var choices = new ComboBox { ItemsSource = field.Options, SelectedItem = raw, MinWidth = 160 };
            choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is string selected) SetAppearanceValue(field, selected); };
            editor = choices;
        }
        else
        {
            var text = new TextBox { Text = raw, MinWidth = 110, Padding = new(8, 5, 8, 5) };
            text.TextChanged += (_, _) => SetAppearanceValue(field, text.Text);
            if (field.Type == "color")
            {
                var group = new DockPanel();
                var pick = new Button { Content = "◼", MinWidth = 38, Margin = new(8, 0, 0, 0), Padding = new(8, 5, 8, 5), ToolTip = "Choose " + field.Title.ToLowerInvariant() };
                var swatch = new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, RadiusX = 2, RadiusY = 2 };
                pick.Content = swatch;
                AutomationProperties.SetName(pick, "Choose " + field.Title);
                void Swatch()
                {
                    var value = text.Text;
                    if (value.Length == 9) value = value[..7];
                    try { swatch.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
                    catch { swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedForegroundBrush"); }
                }
                text.TextChanged += (_, _) => Swatch(); Swatch();
                pick.Click += (_, _) =>
                {
                    var value = text.Text.Length == 9 ? text.Text[..7] : text.Text;
                    var picker = new ColorPickerDialog(field.Title, value) { Owner = Window.GetWindow(this) };
                    if (picker.ShowDialog() == true) text.Text = picker.SelectedColor;
                };
                DockPanel.SetDock(pick, Dock.Right); group.Children.Add(pick); group.Children.Add(text); editor = group;
            }
            else editor = text;
            AutomationProperties.SetName(text, field.Title);
        }
        AutomationProperties.SetName(editor, field.Title);
        Grid.SetColumn(editor, 1); row.Children.Add(editor); WorkAppearancePanel.Children.Add(row);
    }

    private void SetAppearanceValue(FieldDefinition field, string text)
    {
        _appearanceTexts[field.Id] = text;
        try
        {
            var value = HyperlibTemplateEditSession.ParseValue(field.Type, text);
            TemplateValidator.ValidateValues([field], new() { [field.Id] = value }, field.Title);
            _workAppearance[field.Id] = value; _appearanceErrors.Remove(field.Id);
        }
        catch (Exception error) { _appearanceErrors[field.Id] = error.Message; }
        _appearanceDirty = true;
        if (_workDesign is { } design)
            _workDrafts[design.WorkId] = new(design, new(_workAppearance), new(_appearanceTexts), new(_appearanceErrors));
        UpdateAppearanceDraftState();
        ReloadWorkAppearanceButton.Visibility = Visibility.Visible;
        WorkAppearanceStatus.Text = _appearanceErrors.Values.FirstOrDefault() ?? "Unsaved Work appearance changes.";
        SaveWorkAppearanceButton.IsEnabled = _model.CanMutate && _appearanceErrors.Count == 0;
    }

    private async void SaveWorkAppearanceClick(object sender, RoutedEventArgs args)
    {
        if (_workDesign is not { } design || !_appearanceDirty || _appearanceErrors.Count != 0) return;
        if (await _model.SaveWorkAppearanceAsync(design, _workAppearance))
        {
            _workDrafts.Remove(design.WorkId);
            if (_appearanceWorkId == design.WorkId) await LoadWorkAppearanceAsync(discardDraft: true);
            else UpdateAppearanceDraftState();
        }
        else WorkAppearanceStatus.Text = _model.ErrorMessage ?? _model.StatusMessage;
    }

    private async void ReloadWorkAppearanceClick(object sender, RoutedEventArgs args)
    {
        if (_model.IsBusy) return;
        await _model.RefreshAsync();
        await LoadWorkAppearanceAsync(discardDraft: true);
    }
    private void UpdateAppearanceDraftState() => _model.SetAppearanceDraftState(_appearanceDirty || _workDrafts.Count != 0);
    public void DiscardAppearanceDrafts()
    {
        _workDrafts.Clear(); _appearanceDirty = false; _appearanceTexts.Clear(); _appearanceErrors.Clear();
        UpdateAppearanceDraftState();
        if (!_disposed) _ = LoadWorkAppearanceAsync(discardDraft: true);
    }

    private void NewWorkClick(object sender, RoutedEventArgs args) => ShowCreateWorkDialog();
    private void OpenWorkWriterClick(object sender, RoutedEventArgs args) => OpenWorkWriter();
    private void OpenWorkWriter()
    {
        if (_model.SelectedWork is { } work && _model.OpenContentCommand.CanExecute(work))
            _model.OpenContentCommand.Execute(work);
    }
    private void NewContainerClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string type }) ShowCreateContainerDialog(type);
    }
    private void ShowCreateContainerDialog(string initialType)
    {
        if (!_model.CanMutate || _model.SelectedWork is null) return;
        var dialog = CreateDialog("New container", out var body, out var footer);
        using var cancellation = new CancellationTokenSource();
        var closed = false;
        dialog.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
        body.Children.Add(Note("Organize this Work's Pages into volumes, parts, and chapters. Pages are written inside them in Studio."));
        body.Children.Add(Label("Kind"));
        var kinds = new[] { "Part", "Chapter", "Volume" };
        var kind = new ComboBox { ItemsSource = kinds, Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(kind, "Container kind"); body.Children.Add(kind);
        body.Children.Add(Label("Name"));
        var name = new TextBox { Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(name, "New container name"); body.Children.Add(name);
        body.Children.Add(Label("Place in"));
        var parent = new ComboBox { DisplayMemberPath = "Breadcrumb", Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(parent, "Container parent"); body.Children.Add(parent);
        void UpdateParents()
        {
            if (kind.SelectedItem is not string selectedKind) return;
            var parents = _model.GetContainerParents(selectedKind.ToLowerInvariant());
            var preferredId = (parent.SelectedItem as HyperlibStudioNode)?.Id ?? _model.SelectedWorkContent?.Id;
            parent.ItemsSource = parents;
            parent.SelectedItem = parents.FirstOrDefault(item => item.Id == preferredId)
                ?? parents.FirstOrDefault(item => item.Id == _model.SelectedWorkContent?.Node.ParentId)
                ?? parents.FirstOrDefault(item => item.Id == _model.SelectedWork?.Id) ?? parents.FirstOrDefault();
        }
        kind.SelectionChanged += (_, _) => UpdateParents();
        kind.SelectedItem = kinds.FirstOrDefault(item => item.Equals(initialType, StringComparison.OrdinalIgnoreCase)) ?? kinds[0];
        var error = Note(""); error.SetResourceReference(TextBlock.ForegroundProperty, "AttentionBrush"); body.Children.Add(error);
        var create = new Button { Content = "Create container", IsDefault = true, Padding = new(18, 9, 18, 9) };
        create.Click += async (_, _) =>
        {
            if (kind.SelectedItem is not string selectedKind || parent.SelectedItem is not HyperlibStudioNode selected || string.IsNullOrWhiteSpace(name.Text))
            { error.Text = "Enter a name and choose a location."; return; }
            create.IsEnabled = false;
            try
            {
                if (await _model.CreateContainerAsync(selected.Id, selectedKind.ToLowerInvariant(), name.Text, cancellation.Token))
                { if (!closed) dialog.DialogResult = true; }
                else if (!closed) error.Text = _model.ErrorMessage ?? _model.StatusMessage;
            }
            catch (Exception exception) { _model.ReportExternalError(exception.Message); if (!closed) error.Text = exception.Message; }
            finally { if (!closed) create.IsEnabled = true; }
        };
        footer.Children.Add(create); dialog.Loaded += (_, _) => name.Focus(); dialog.ShowDialog();
    }
    private void ShowCreateWorkDialog(string? templateId = null)
    {
        var parents = _model.WorkParents;
        if (parents.Count == 0 || _model.IsBusy) return;
        var dialog = CreateDialog("New Work", out var body, out var footer);
        using var cancellation = new CancellationTokenSource();
        var closed = false;
        dialog.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
        body.Children.Add(Note("A container for Pages and chapters, with its own page structure, element library, and shared appearance."));
        body.Children.Add(Label("Name"));
        var name = new TextBox { Text = _model.DraftTitle, Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(name, "New Work name");
        body.Children.Add(name);
        body.Children.Add(Label("Collection or series"));
        var parent = new ComboBox { ItemsSource = parents, DisplayMemberPath = "Breadcrumb", SelectedIndex = 0, Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(parent, "Work collection or series");
        body.Children.Add(parent);
        body.Children.Add(Label("Template element library"));
        var starter = new ComboBox { ItemsSource = _model.AvailableTemplates, DisplayMemberPath = "Title", SelectedItem = _model.AvailableTemplates.FirstOrDefault(item => item.Id == (templateId ?? _model.SelectedTemplate?.Id ?? "blank")) ?? _model.AvailableTemplates.FirstOrDefault(), Margin = new(0, 0, 0, 10) };
        AutomationProperties.SetName(starter, "Work element library");
        body.Children.Add(starter);
        var description = Note((starter.SelectedItem as HyperlibStudioTemplate)?.Description ?? "");
        starter.SelectionChanged += (_, _) => description.Text = (starter.SelectedItem as HyperlibStudioTemplate)?.Description ?? "";
        body.Children.Add(description);
        var error = Note(""); error.SetResourceReference(TextBlock.ForegroundProperty, "AttentionBrush"); body.Children.Add(error);
        var create = new Button { Content = "Create", IsDefault = true, Padding = new(18, 9, 18, 9) };
        create.Click += async (_, _) =>
        {
            if (parent.SelectedItem is not HyperlibStudioNode selected || string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter a name and choose a location."; return; }
            create.IsEnabled = false; _model.DraftTitle = name.Text;
            try
            {
                var success = starter.SelectedItem is HyperlibStudioTemplate design && await _model.CreateWorkAsync(selected.Id, name.Text, design.Id, cancellation.Token);
                if (success && !closed) { dialog.DialogResult = true; _model.OpenLastCreatedWork(); }
                else if (!closed) error.Text = _model.ErrorMessage ?? _model.StatusMessage;
            }
            catch (Exception exception) { _model.ReportExternalError(exception.Message); if (!closed) error.Text = exception.Message; }
            finally { if (!closed) create.IsEnabled = true; }
        };
        footer.Children.Add(create); dialog.Loaded += (_, _) => name.Focus(); dialog.ShowDialog();
    }

    private async void NewTemplateClick(object sender, RoutedEventArgs args) => await ShowTemplateDialogAsync("blank");
    private async void OpenCoreLibraryClick(object sender, RoutedEventArgs args) => await ShowObjectCollectionAsync();
    private async void DuplicateTemplateClick(object sender, RoutedEventArgs args) => await ShowTemplateDialogAsync("duplicate");
    private async void CaptureWorkTemplateClick(object sender, RoutedEventArgs args) => await ShowTemplateDialogAsync("work");
    private void UseTemplateClick(object sender, RoutedEventArgs args)
    {
        if (_model.SelectedTemplate is { } template) ShowCreateWorkDialog(template.Id);
    }

    private async Task ShowTemplateDialogAsync(string source, bool openSource = false)
    {
        if (!_model.CanManageTemplates) return;
        if (source == "blank") { await ShowObjectCollectionAsync(); return; }
        var original = _model.SelectedTemplate;
        if (source == "duplicate" && original is null || source == "work" && _model.TemplateSourceWorks.Count == 0) return;
        var title = source == "blank" ? "New template" : source == "work" ? "Template from work" : "Customize template";
        var dialog = CreateDialog(title, out var body, out var footer);
        using var cancellation = new CancellationTokenSource();
        var closed = false;
        dialog.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
        body.Children.Add(Note(source == "blank" ? "Start an element library and add reusable text boxes, media, widgets, and tools."
            : source == "work" ? "Capture a Work's current elements and design as a reusable Template. Existing Pages stay in their Work."
            : "Create an independent element library, ready to customize for future Works."));
        body.Children.Add(Label("Name"));
        var name = new TextBox { Text = source == "duplicate" ? original!.Title + " copy" : "", Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(name, "New template name");
        body.Children.Add(name);
        ComboBox? work = null;
        if (source == "work")
        {
            body.Children.Add(Label("Work"));
            work = new ComboBox { ItemsSource = _model.TemplateSourceWorks, DisplayMemberPath = "Title", SelectedItem = _model.SelectedWork ?? _model.TemplateSourceWorks.FirstOrDefault(), Margin = new(0, 0, 0, 12) };
            AutomationProperties.SetName(work, "Work to save as template");
            body.Children.Add(work);
        }
        var error = Note(""); error.SetResourceReference(TextBlock.ForegroundProperty, "AttentionBrush"); body.Children.Add(error);
        var create = new Button { Content = "Create template", IsDefault = true, Padding = new(18, 9, 18, 9) };
        create.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter a template name."; return; }
            create.IsEnabled = false;
            try
            {
                var success = source switch
                {
                    "duplicate" => await _model.DuplicateTemplateAsync(original!.Id, name.Text, cancellation.Token),
                    "work" => work?.SelectedItem is HyperlibStudioNode node && await _model.CreateTemplateFromWorkAsync(node.Id, name.Text, cancellation.Token),
                    _ => await _model.CreateBlankTemplateAsync(name.Text, cancellation.Token)
                };
                if (success && !closed) dialog.DialogResult = true;
                else if (!closed) error.Text = _model.ErrorMessage ?? _model.StatusMessage;
            }
            catch (Exception exception) { _model.ReportExternalError(exception.Message); if (!closed) error.Text = exception.Message; }
            finally { if (!closed) create.IsEnabled = true; }
        };
        footer.Children.Add(create);
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        if (dialog.ShowDialog() != true || _disposed) return;
        WorkspaceTabs.SelectedItem = TemplatesTab;
        Execute(_model.OpenTemplateProjectCommand);
    }

    private async Task ShowObjectCollectionAsync()
    {
        if (!_model.CanManageTemplates || _model.Context?.LibraryRoot is not { } libraryRoot) return;
        try
        {
            var browser = new HyperlibObjectLibraryDialog(libraryRoot)
            {
                Owner = Window.GetWindow(this),
                CreateTemplateAsync = async (title, objects) =>
                {
                    if (_disposed) return "This workspace has closed. Open the Core Library again.";
                    return await _model.CreateTemplateFromObjectsAsync(title, objects)
                        ? null : _model.ErrorMessage ?? _model.StatusMessage ?? "The Template could not be created.";
                }
            };
            if (browser.ShowDialog() != true || _disposed) return;
            WorkspaceTabs.SelectedItem = TemplatesTab;
            Execute(_model.OpenTemplateProjectCommand);
        }
        catch (Exception error) { _model.ReportExternalError(error.Message); }
    }

    private async void EditSavedTemplateClick(object sender, RoutedEventArgs args) => await CustomizeOrEditTemplateAsync();
    private async void TemplateDoubleClick(object sender, MouseButtonEventArgs args) => await CustomizeOrEditTemplateAsync();
    private async void TemplateKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        await CustomizeOrEditTemplateAsync();
    }
    private Task CustomizeOrEditTemplateAsync() => _model.SelectedTemplate?.IsBuiltIn == true
        ? ShowTemplateDialogAsync("duplicate") : EditSelectedTemplateAsync();
    private async void OpenTemplateSourceProjectClick(object sender, RoutedEventArgs args)
    {
        if (_model.SelectedTemplate?.IsBuiltIn == true) await ShowTemplateDialogAsync("duplicate", true);
        else Execute(_model.OpenTemplateProjectCommand);
    }
    private Task EditSelectedTemplateAsync()
    { Execute(_model.OpenTemplateProjectCommand); return Task.CompletedTask; }

    private void RenameWorkClick(object sender, RoutedEventArgs args) => Rename(_model.SelectedWork);
    private void RenamePageClick(object sender, RoutedEventArgs args) => Rename(_model.SelectedPage);
    private void RenameWorkContentClick(object sender, RoutedEventArgs args) => Rename(_model.SelectedWorkContent?.Node);
    private void Rename(HyperlibStudioNode? node)
    {
        if (node is null || _model.IsBusy) return;
        var dialog = CreateDialog("Rename " + node.Type, out var body, out var footer);
        using var cancellation = new CancellationTokenSource();
        var closed = false;
        dialog.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
        body.Children.Add(Label("Name")); var name = new TextBox { Text = node.Title, Margin = new(0, 0, 0, 10) }; body.Children.Add(name);
        AutomationProperties.SetName(name, "New name");
        var error = Note(""); error.SetResourceReference(TextBlock.ForegroundProperty, "AttentionBrush"); body.Children.Add(error);
        var save = new Button { Content = "Rename", IsDefault = true, Padding = new(18, 9, 18, 9) };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var success = await _model.RenameAsync(node.Id, name.Text, cancellation.Token);
                if (success && !closed) dialog.DialogResult = true;
                else if (!closed) error.Text = _model.ErrorMessage ?? _model.StatusMessage;
            }
            catch (Exception exception) { _model.ReportExternalError(exception.Message); if (!closed) error.Text = exception.Message; }
            finally { if (!closed) save.IsEnabled = true; }
        };
        footer.Children.Add(save); dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); }; dialog.ShowDialog();
    }

    private Window CreateDialog(string title, out StackPanel body, out StackPanel footer)
    {
        var dialog = new Window { Title = title, Width = 530, MinWidth = 400, SizeToContent = SizeToContent.Height, MaxHeight = 760, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize };
        var outer = new StackPanel { Margin = new(24) };
        outer.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        body = new StackPanel(); outer.Children.Add(body);
        footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new(18, 9, 18, 9), Margin = new(0, 0, 8, 0) }; footer.Children.Add(cancel); outer.Children.Add(footer);
        dialog.Content = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; return dialog;
    }
    private static TextBlock Label(string value) => new() { Text = value, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 6) };
    private static TextBlock Note(string value) { var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) }; text.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundBrush"); return text; }
    private static void Execute(ICommand command, object? parameter = null) { if (command.CanExecute(parameter)) command.Execute(parameter); }
    private void WorkDoubleClick(object sender, MouseButtonEventArgs args) => Execute(_model.OpenWorkCommand);
    private void PageDoubleClick(object sender, MouseButtonEventArgs args) => Execute(_model.OpenContentCommand, _model.SelectedPage);
    private void WorkKeyDown(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { Execute(_model.OpenWorkCommand); args.Handled = true; } }
    private void PageKeyDown(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { Execute(_model.OpenContentCommand, _model.SelectedPage); args.Handled = true; } }
    private void WorkContentDoubleClick(object sender, MouseButtonEventArgs args) => OpenSelectedWorkContent();
    private void WorkContentKeyDown(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { OpenSelectedWorkContent(); args.Handled = true; } }
    private void OpenSelectedWorkContent()
    {
        if (_model.SelectedWorkContent is { } content) Execute(_model.OpenContentCommand, content.Node);
    }
    private void SourceClick(object sender, RoutedEventArgs args) { if (sender is Button { Tag: HyperlibStudioSource source } && _model.OpenSourceCommand.CanExecute(source)) _model.OpenSourceCommand.Execute(source); }
    private void TemplateSectionClick(object sender, RoutedEventArgs args) { if (sender is Button { Tag: string section }) TemplateSectionRequested?.Invoke(section); }
    private async void WorkSectionClick(object sender, RoutedEventArgs args)
    {
        if (_model.IsBusy || sender is not Button { Tag: string section } || _model.SelectedWork?.ProjectRoot is not { } root) return;
        try
        {
            using var service = new HyperlibStudioWorkspaceService();
            var context = await service.ReadAsync(root);
            var loaded = await HyperlibStudioAuthoringService.LoadAsync(root, context.Context.AuthoringRelativePath);
            if (_disposed) return;
            var dialog = new HyperlibTemplateEditorDialog(root, loaded.Definition, loaded.Snapshot,
                relativePath =>
                {
                    if (root.Equals(_model.Context?.ProjectRoot, StringComparison.OrdinalIgnoreCase))
                        ExecuteSource(relativePath);
                    else _model.OpenWorkCommand.Execute(null);
                }) { Owner = Window.GetWindow(this) };
            dialog.SelectSection(section); dialog.ShowDialog();
            if (!_disposed) { await _model.RefreshAsync(); if (!_appearanceDirty) await LoadWorkAppearanceAsync(); }
        }
        catch (Exception error) { if (!_disposed) _model.ReportExternalError(error.Message); }
    }
    private void ExecuteSource(string relativePath)
    {
        var source = _model.SourceGroups.SelectMany(group => group.Items).FirstOrDefault(item => item.RelativePath == relativePath);
        if (source is not null && _model.OpenSourceCommand.CanExecute(source)) _model.OpenSourceCommand.Execute(source);
    }
    private void ShowPagesClick(object sender, RoutedEventArgs args) => WorkspaceTabs.SelectedItem = PagesTab;
    private void ShowSourceClick(object sender, RoutedEventArgs args)
    {
        if (_model.IsLibraryRoot) { Execute(_model.OpenWorkCommand); return; }
        WorkspaceTabs.SelectedItem = CodeTab;
    }
    private void BackToObjectClick(object sender, RoutedEventArgs args)
        => WorkspaceTabs.SelectedItem = _returnTab ?? (_model.HasLibraryContent ? WorkTab : TemplateTab);
    private void LibraryClick(object sender, RoutedEventArgs args) { if (_model.Context?.LibraryRoot is { } root) ProjectOpenRequested?.Invoke(root); }
    private async void PreviewClick(object sender, RoutedEventArgs args) { WorkspaceTabs.SelectedItem = PreviewTab; await _model.PreviewAsync(); }
    private void WorkspaceTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, WorkspaceTabs)) return;
        if (WorkspaceTabs.SelectedItem == CodeTab || WorkspaceTabs.SelectedItem == PreviewTab)
            if (args.RemovedItems.OfType<TabItem>().FirstOrDefault(tab => tab != CodeTab && tab != PreviewTab) is { } previous)
                _returnTab = previous;
        UpdatePreviewLease();
        UpdateLayoutForWidth();
    }
    private void UpdatePreviewLease()
    {
        if (!_disposed && IsVisible && _preview is not null && WorkspaceTabs.SelectedItem == PreviewTab) _previewTimer.Start();
        else _previewTimer.Stop();
    }
    private async void PreviewTick(object? sender, EventArgs args)
    {
        if (_previewTickBusy || _disposed || _model.IsBusy) return;
        _previewTickBusy = true;
        try { await _model.KeepPreviewAliveAsync(); await _model.PreviewAsync(_previewPage); }
        finally { _previewTickBusy = false; }
    }
    private async void PreviewRequested(HyperlibStudioPreview result)
    {
        if (_disposed) return;
        var requestId = ++_previewRequestId;
        await _previewGate.WaitAsync();
        try
        {
            if (_disposed || requestId != _previewRequestId) return;
            PreviewStatus.Text = string.IsNullOrWhiteSpace(result.Diagnostics) ? result.Status : result.Status + " · " + result.Diagnostics;
            if (_preview is null)
            {
                _preview = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 18, 22, 28) };
                PreviewHost.Child = _preview;
                var profile = Path.Combine(new SettingsService().ApplicationDataDirectory, "hyperlib-studio-preview");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                if (_disposed) return;
                await _preview.EnsureCoreWebView2Async(environment);
                if (_disposed) return;
                _preview.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                _preview.CoreWebView2.Settings.IsWebMessageEnabled = false;
                _preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _preview.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                _preview.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                _preview.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
                _preview.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) || _previewEndpoint is null
                        || target.GetLeftPart(UriPartial.Authority) != _previewEndpoint.GetLeftPart(UriPartial.Authority)) args.Cancel = true;
                };
            }
            if (_disposed || requestId != _previewRequestId) return;
            _previewEndpoint = result.Endpoint;
            if (_previewUrl != result.Url.AbsoluteUri)
            {
                _previewUrl = result.Url.AbsoluteUri; _previewPage = result.PageId;
                _preview.CoreWebView2.Navigate(result.Url.AbsoluteUri);
            }
            UpdatePreviewLease();
        }
        catch (Exception exception)
        {
            _preview?.Dispose(); _preview = null; _previewUrl = null;
            if (!_disposed) { PreviewHost.Child = Note("Preview could not start. Choose Load preview to retry."); PreviewStatus.Text = "Preview unavailable: " + exception.Message; }
        }
        finally { _previewGate.Release(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _model.PropertyChanged -= ModelChanged; _model.PreviewRequested -= PreviewRequested;
        _model.DiscardAppearanceDraftsRequested -= DiscardAppearanceDrafts;
        _previewTimer.Stop(); _previewTimer.Tick -= PreviewTick; _preview?.Dispose(); _preview = null;
    }
}
