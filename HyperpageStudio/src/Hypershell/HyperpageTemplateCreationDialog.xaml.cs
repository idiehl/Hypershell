using Hypershell.Services;
using Hypershell.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hypershell;

public enum HyperpageTemplateCreationSource
{
    Code,
    Selection,
    Url
}

public partial class HyperpageTemplateCreationDialog : Window
{
    public sealed record SourceOption(
        string Name,
        HyperpageTemplateCreationSource Source)
    {
        public override string ToString() => Name;
    }

    public sealed record LibraryOption(string Name, bool Personal)
    {
        public override string ToString() => Name;
    }

    public sealed record ScopeOption(string Name, string Scope)
    {
        public override string ToString() => Name;
    }

    private readonly VisualPageStudioTabViewModel _viewModel;

    public HyperpageTemplateCreationDialog(
        VisualPageStudioTabViewModel viewModel,
        HyperpageTemplateCreationSource initialSource)
    {
        _viewModel = viewModel;
        SourceOptions =
        [
            new SourceOption("Template from code", HyperpageTemplateCreationSource.Code),
            new SourceOption("Template from selection", HyperpageTemplateCreationSource.Selection),
            new SourceOption("Template from URL", HyperpageTemplateCreationSource.Url)
        ];
        LibraryOptions =
        [
            new LibraryOption("This project", Personal: false),
            new LibraryOption("My templates", Personal: true)
        ];
        ScopeOptions =
        [
            new ScopeOption("Page", "page"),
            new ScopeOption("Section", "section"),
            new ScopeOption("Element", "element")
        ];

        InitializeComponent();
        DataContext = this;
        LibraryComboBox.SelectedIndex = 0;
        ScopeComboBox.SelectedIndex = 2;
        SourceComboBox.SelectedItem = SourceOptions.Single(option =>
            option.Source == initialSource);
        Loaded += OnLoaded;
    }

    public IReadOnlyList<SourceOption> SourceOptions { get; }

    public IReadOnlyList<LibraryOption> LibraryOptions { get; }

    public IReadOnlyList<ScopeOption> ScopeOptions { get; }

    public static void Show(
        Window owner,
        VisualPageStudioTabViewModel viewModel,
        HyperpageTemplateCreationSource initialSource =
            HyperpageTemplateCreationSource.Code)
    {
        var dialog = new HyperpageTemplateCreationDialog(
            viewModel,
            initialSource)
        {
            Owner = owner
        };
        dialog.Show();
        dialog.Activate();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs) =>
        InputTextBox.Focus();

    private void SourceSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (!IsLoaded && InputTextBox is null
            || SourceComboBox.SelectedItem is not SourceOption option)
        {
            return;
        }

        StatusText.Text = string.Empty;
        OutputTextBox.Text = string.Empty;
        DetailsPanel.Visibility = option.Source == HyperpageTemplateCreationSource.Url
            ? Visibility.Collapsed
            : Visibility.Visible;
        AnalyzeButton.Visibility = option.Source == HyperpageTemplateCreationSource.Url
            ? Visibility.Collapsed
            : Visibility.Visible;
        CreateButton.Content = option.Source == HyperpageTemplateCreationSource.Url
            ? "Submit"
            : "Create";
        CreateButton.ToolTip = option.Source == HyperpageTemplateCreationSource.Url
            ? "Submit URL"
            : "Create template";

        switch (option.Source)
        {
            case HyperpageTemplateCreationSource.Selection:
                InputLabel.Text = "SELECTED ELEMENTS";
                OutputLabel.Text = "CONVERTED OUTPUT";
                InputTextBox.AcceptsReturn = true;
                InputTextBox.TextWrapping = TextWrapping.NoWrap;
                InputTextBox.IsReadOnly = true;
                InputTextBox.Text = _viewModel.SelectedElementTemplateMarkup;
                TemplateNameTextBox.Text = _viewModel.SelectedElement is null
                    ? "Selection template"
                    : $"{_viewModel.SelectedElement.Name} template";
                DescriptionTextBox.Text = _viewModel.SelectedElement is null
                    ? string.Empty
                    : $"Reusable template created from {_viewModel.SelectedElement.Name}.";
                SelectScopeForSelection();
                break;
            case HyperpageTemplateCreationSource.Url:
                InputLabel.Text = "WEBSITE URL";
                OutputLabel.Text = "AI OUTPUT";
                InputTextBox.AcceptsReturn = false;
                InputTextBox.TextWrapping = TextWrapping.NoWrap;
                InputTextBox.IsReadOnly = false;
                InputTextBox.Text = "https://";
                break;
            default:
                InputLabel.Text = "CODE";
                OutputLabel.Text = "CONVERTED OUTPUT";
                InputTextBox.AcceptsReturn = true;
                InputTextBox.TextWrapping = TextWrapping.NoWrap;
                InputTextBox.IsReadOnly = false;
                InputTextBox.Text = string.Empty;
                TemplateNameTextBox.Text = "New template";
                DescriptionTextBox.Text = string.Empty;
                ScopeComboBox.SelectedIndex = 2;
                break;
        }
    }

    private void SelectScopeForSelection()
    {
        var markup = _viewModel.SelectedElementDescriptor?.MarkupName;
        ScopeComboBox.SelectedItem = ScopeOptions.First(option =>
            _viewModel.SelectedElement?.DescriptorId == "core.page"
                ? option.Scope == "page"
                : string.Equals(markup, "section", StringComparison.OrdinalIgnoreCase)
                    ? option.Scope == "section"
                    : option.Scope == "element");
    }

    private void AnalyzeClick(object sender, RoutedEventArgs eventArgs)
    {
        if (SourceComboBox.SelectedItem is not SourceOption option
            || option.Source == HyperpageTemplateCreationSource.Url)
        {
            return;
        }

        try
        {
            var analysis = _viewModel.AnalyzeTemplateCode(
                InputTextBox.Text,
                SelectedScope(),
                TemplateNameTextBox.Text);
            OutputTextBox.Text = analysis.Output;
            ShowStatus(analysis.Summary, error: false);
        }
        catch (Exception exception) when (exception is
            VisualPageValidationException or InvalidOperationException)
        {
            ShowStatus(exception.Message, error: true);
        }
    }

    private async void CreateClick(object sender, RoutedEventArgs eventArgs)
    {
        if (SourceComboBox.SelectedItem is not SourceOption option)
        {
            return;
        }

        if (option.Source == HyperpageTemplateCreationSource.Url)
        {
            await SubmitUrlAsync();
            return;
        }
        if (string.IsNullOrWhiteSpace(TemplateNameTextBox.Text))
        {
            ShowStatus("Enter a template name.", error: true);
            return;
        }

        SetBusy(true);
        var personal = (LibraryComboBox.SelectedItem as LibraryOption)?.Personal
                       == true;
        var created = option.Source == HyperpageTemplateCreationSource.Selection
            ? await _viewModel.SaveSelectedAsTemplateAsync(
                TemplateNameTextBox.Text,
                DescriptionTextBox.Text,
                personal)
            : await _viewModel.CreateTemplateFromCodeAsync(
                string.IsNullOrWhiteSpace(OutputTextBox.Text)
                    ? InputTextBox.Text
                    : OutputTextBox.Text,
                TemplateNameTextBox.Text,
                DescriptionTextBox.Text,
                SelectedScope(),
                personal);
        SetBusy(false);
        if (created)
        {
            Close();
            return;
        }
        ShowStatus(_viewModel.StatusMessage, error: true);
    }

    private async Task SubmitUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            ShowStatus("Paste a website URL.", error: true);
            return;
        }
        var permission = MessageBox.Show(
            this,
            "Confirm that you own this site or have permission to reuse its structure. Hyperpage Studio will create placeholder content and will not copy scripts, remote assets, captured CSS, form actions, or tracking markup.",
            "Website Template Permission",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (permission != MessageBoxResult.Yes)
        {
            ShowStatus("Website template creation was not submitted.", error: false);
            return;
        }

        SetBusy(true);
        ShowStatus("The selected AI model is analyzing the website structure…", error: false);
        OutputTextBox.Text = await _viewModel.StartWebTemplateCaptureAsync(
            InputTextBox.Text);
        SetBusy(false);
        ShowStatus(
            _viewModel.AiAssistant?.HasPendingReview == true
                ? "AI output is ready. Review the proposed template in the AI Assistant."
                : _viewModel.StatusMessage,
            error: false);
    }

    private string SelectedScope() =>
        (ScopeComboBox.SelectedItem as ScopeOption)?.Scope ?? "element";

    private void SetBusy(bool busy)
    {
        SourceComboBox.IsEnabled = !busy;
        DetailsPanel.IsEnabled = !busy;
        InputTextBox.IsEnabled = !busy;
        AnalyzeButton.IsEnabled = !busy;
        CreateButton.IsEnabled = !busy;
    }

    private void ShowStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(255, 123, 114))
            : (Brush)FindResource("MutedForegroundBrush");
    }

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseClick(object sender, RoutedEventArgs eventArgs) => Close();
}
