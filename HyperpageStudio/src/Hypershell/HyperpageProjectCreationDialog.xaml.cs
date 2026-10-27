using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Hypershell.Models;
using Hypershell.Services;

namespace Hypershell;

public partial class HyperpageProjectCreationDialog : Window,
    INotifyPropertyChanged
{
    private readonly string _parentDirectory;
    private string _projectDirectoryPreview;
    private bool _useTemplates;

    public HyperpageProjectCreationDialog(
        string parentDirectory,
        IReadOnlyList<VisualPageTemplateDocument> pageTemplates)
    {
        _parentDirectory = Path.GetFullPath(parentDirectory);
        _projectDirectoryPreview = Path.Combine(
            _parentDirectory,
            "Hyperpage Project");
        PageTemplates = pageTemplates
            .Where(template => string.Equals(
                template.Scope,
                "page",
                StringComparison.Ordinal))
            .OrderBy(template => template.Category, StringComparer.Ordinal)
            .ThenBy(template => template.Name, StringComparer.Ordinal)
            .ToList();
        RebuildPageDrafts(1);

        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
            RefreshConditionalControls();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HyperpagePageDraft> PageDrafts { get; } = [];

    public IReadOnlyList<VisualPageTemplateDocument> PageTemplates { get; }

    public string ProjectDirectoryPreview
    {
        get => _projectDirectoryPreview;
        private set
        {
            if (string.Equals(
                    _projectDirectoryPreview,
                    value,
                    StringComparison.Ordinal))
            {
                return;
            }

            _projectDirectoryPreview = value;
            OnPropertyChanged();
        }
    }

    public bool UseTemplates
    {
        get => _useTemplates;
        private set
        {
            if (_useTemplates == value)
            {
                return;
            }

            _useTemplates = value;
            if (value && PageTemplates.Count > 0)
            {
                foreach (var draft in PageDrafts)
                {
                    draft.SelectedTemplate ??= PageTemplates[0];
                }
            }
            OnPropertyChanged();
        }
    }

    public string ProjectName { get; private set; } = string.Empty;

    public HyperpageProjectCreationOptions? CreationOptions { get; private set; }

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ProjectNameChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs eventArgs)
    {
        if (ProjectNameTextBox is null)
        {
            return;
        }

        var name = ProjectNameTextBox.Text.Trim();
        ProjectDirectoryPreview = Path.Combine(
            _parentDirectory,
            string.IsNullOrWhiteSpace(name) ? "(project name)" : name);
    }

    private void StructureChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (PageCountTextBox is null || DynamicSiteRadio is null)
        {
            return;
        }

        RefreshConditionalControls();
        RebuildPageDrafts(DynamicSiteRadio.IsChecked == true
            ? 1
            : ParsePageCountOrDefault());
    }

    private void PageCountChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs eventArgs)
    {
        if (PageCountTextBox is null
            || FixedPagesRadio?.IsChecked != true
            || !int.TryParse(PageCountTextBox.Text, out var count)
            || count is < 1 or > 20)
        {
            return;
        }

        RebuildPageDrafts(count);
    }

    private void TemplateChoiceChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (TemplatesYesRadio is null)
        {
            return;
        }

        UseTemplates = TemplatesYesRadio.IsChecked == true;
    }

    private void CodingModeChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (AiProviderPanel is null || AiCodingRadio is null)
        {
            return;
        }

        var enabled = AiCodingRadio.IsChecked == true;
        AiProviderPanel.IsEnabled = enabled;
        AiProviderPanel.Opacity = enabled ? 1 : 0.55;
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void CreateClick(object sender, RoutedEventArgs eventArgs)
    {
        StatusText.Text = string.Empty;
        var name = ProjectNameTextBox.Text.Trim();
        if (!TryValidateFolderName(name, out var nameError))
        {
            ShowError(nameError);
            ProjectNameTextBox.Focus();
            return;
        }

        var dynamicSite = DynamicSiteRadio.IsChecked == true;
        var pageCount = dynamicSite ? 1 : ParsePageCountOrDefault();
        if (!dynamicSite
            && (!int.TryParse(PageCountTextBox.Text, out pageCount)
                || pageCount is < 1 or > 20))
        {
            ShowError("Choose an initial page count from 1 through 20.");
            PageCountTextBox.Focus();
            return;
        }

        RebuildPageDrafts(pageCount);
        foreach (var draft in PageDrafts)
        {
            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                ShowError("Every initial page needs a name.");
                return;
            }
            if (UseTemplates && draft.SelectedTemplate is null)
            {
                ShowError("Choose a starter template for every initial page.");
                return;
            }
        }

        var aiAssisted = AiCodingRadio.IsChecked == true;
        var foundation = new HyperpageProjectFoundation
        {
            MarkupLanguage = HtmlRadio.IsChecked == true
                ? HyperpageMarkupLanguage.Html
                : HyperpageMarkupLanguage.RazorPages,
            HostingTarget = WebServerHostingRadio.IsChecked == true
                ? HyperpageHostingTarget.WebServer
                : HyperpageHostingTarget.Local,
            SiteStructure = dynamicSite
                ? HyperpageSiteStructure.DynamicSite
                : HyperpageSiteStructure.FixedPages,
            PlannedPageCount = dynamicSite ? 0 : pageCount,
            UsesTemplates = UseTemplates,
            CodingMode = aiAssisted
                ? HyperpageCodingMode.AiAssisted
                : HyperpageCodingMode.Manual,
            AiProvider = !aiAssisted
                ? HyperpageAiProvider.None
                : LocalAiProviderRadio.IsChecked == true
                    ? HyperpageAiProvider.Local
                    : HyperpageAiProvider.OpenAi,
            ClarifyingQuestionsFirst = true
        };
        CreationOptions = new HyperpageProjectCreationOptions
        {
            Foundation = foundation,
            Pages = PageDrafts.Select(draft =>
                new HyperpagePageCreationDefinition
                {
                    Name = draft.Name.Trim(),
                    Route = draft.Route.Trim(),
                    Template = UseTemplates
                        ? draft.SelectedTemplate
                        : null
                }).ToList()
        };
        ProjectName = name;
        DialogResult = true;
    }

    private void RefreshConditionalControls()
    {
        if (PageCountTextBox is not null && DynamicSiteRadio is not null)
        {
            var dynamicSite = DynamicSiteRadio.IsChecked == true;
            PageCountTextBox.IsEnabled = !dynamicSite;
            StructureHelpText.Text = dynamicSite
                ? "Start with one foundation page. Routes can be created later from the site's content or data model."
                : "Choose 1–20 initial pages. Each page will be created now with its own route and optional template.";
        }

        if (AiProviderPanel is not null && AiCodingRadio is not null)
        {
            var enabled = AiCodingRadio.IsChecked == true;
            AiProviderPanel.IsEnabled = enabled;
            AiProviderPanel.Opacity = enabled ? 1 : 0.55;
        }
    }

    private int ParsePageCountOrDefault() =>
        int.TryParse(PageCountTextBox?.Text, out var count)
        && count is >= 1 and <= 20
            ? count
            : Math.Max(1, PageDrafts.Count);

    private void RebuildPageDrafts(int requestedCount)
    {
        var count = Math.Clamp(requestedCount, 1, 20);
        while (PageDrafts.Count < count)
        {
            var index = PageDrafts.Count;
            var draft = new HyperpagePageDraft(index);
            if (UseTemplates && PageTemplates.Count > 0)
            {
                draft.SelectedTemplate = PageTemplates[0];
            }
            PageDrafts.Add(draft);
        }
        while (PageDrafts.Count > count)
        {
            PageDrafts.RemoveAt(PageDrafts.Count - 1);
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
    }

    private static bool TryValidateFolderName(
        string name,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Choose a valid project name without path separators or a trailing period.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class HyperpagePageDraft : INotifyPropertyChanged
{
    private readonly string _defaultName;
    private readonly string _defaultRoute;
    private string _lastSuggestedName = string.Empty;
    private string _lastSuggestedRoute = string.Empty;
    private string _name;
    private string _route;
    private VisualPageTemplateDocument? _selectedTemplate;

    public HyperpagePageDraft(int index)
    {
        _defaultName = index == 0 ? "Home" : $"Page {index + 1}";
        _defaultRoute = index == 0 ? "/" : $"/page-{index + 1}";
        _name = _defaultName;
        _route = _defaultRoute;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (string.Equals(_name, value, StringComparison.Ordinal))
            {
                return;
            }
            _name = value;
            OnPropertyChanged();
        }
    }

    public string Route
    {
        get => _route;
        set
        {
            if (string.Equals(_route, value, StringComparison.Ordinal))
            {
                return;
            }
            _route = value;
            OnPropertyChanged();
        }
    }

    public VisualPageTemplateDocument? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (ReferenceEquals(_selectedTemplate, value))
            {
                return;
            }

            var canReplaceName = string.IsNullOrWhiteSpace(Name)
                                 || string.Equals(
                                     Name,
                                     _defaultName,
                                     StringComparison.Ordinal)
                                 || string.Equals(
                                     Name,
                                     _lastSuggestedName,
                                     StringComparison.Ordinal);
            var canReplaceRoute = string.IsNullOrWhiteSpace(Route)
                                  || string.Equals(
                                      Route,
                                      _defaultRoute,
                                      StringComparison.Ordinal)
                                  || string.Equals(
                                      Route,
                                      _lastSuggestedRoute,
                                      StringComparison.Ordinal);
            _selectedTemplate = value;
            _lastSuggestedName = value?.SuggestedPageName?.Trim()
                                 ?? string.Empty;
            _lastSuggestedRoute = value?.SuggestedPageRoute?.Trim()
                                  ?? string.Empty;
            if (canReplaceName && !string.IsNullOrWhiteSpace(_lastSuggestedName))
            {
                Name = _lastSuggestedName;
            }
            if (canReplaceRoute && !string.IsNullOrWhiteSpace(_lastSuggestedRoute))
            {
                Route = _lastSuggestedRoute;
            }
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
