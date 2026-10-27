using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell;

public partial class HyperpagePageCreationDialog : Window
{
    public sealed record TemplateOption(
        string Name,
        VisualPageTemplateDocument? Template);

    public sealed record FileTypeOption(
        string Name,
        HyperpagePageFileType FileType,
        bool IsStylesheet = false);

    public HyperpagePageCreationDialog(
        IReadOnlyList<VisualPageTemplateDocument> templates,
        IReadOnlyList<HyperpagePageFileType> fileTypes,
        bool stylesheet = false)
    {
        TemplateOptions =
        [
            new TemplateOption("Blank page", null),
            .. templates.Select(template => new TemplateOption(
                template.Name,
                template))
        ];
        FileTypeOptions = fileTypes.Select(fileType => new FileTypeOption(
                FileTypeName(fileType),
                fileType))
            .ToList();
        if (FileTypeOptions.Count == 0)
        {
            FileTypeOptions =
            [
                new FileTypeOption(
                    "Project default",
                    HyperpagePageFileType.ProjectDefault)
            ];
        }
        FileTypeOptions.Add(new FileTypeOption(
            "CSS stylesheet (.css)", HyperpagePageFileType.ProjectDefault, true));

        InitializeComponent();
        DataContext = this;
        TemplateComboBox.SelectedIndex = 0;
        FileTypeComboBox.SelectedIndex = stylesheet ? FileTypeOptions.Count - 1 : FileTypeOptions
            .FindIndex(option => option.FileType ==
                                 HyperpagePageFileType.RazorPage) is var index
            && index >= 0
                ? index
                : 0;
        Loaded += (_, _) =>
        {
            PageNameTextBox.Focus();
            PageNameTextBox.SelectAll();
        };
    }

    public IReadOnlyList<TemplateOption> TemplateOptions { get; }

    public List<FileTypeOption> FileTypeOptions { get; private set; }

    public HyperpagePageCreationRequest? CreationRequest { get; private set; }
    public string? StylesheetFileName { get; private set; }
    public bool LinkStylesheetToCurrentPage => LinkStylesheetCheckBox.IsChecked == true;

    public static async Task<bool> ShowAndCreateAsync(
        Window owner,
        VisualPageStudioTabViewModel viewModel,
        bool stylesheet = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(viewModel);
        if (viewModel.IsBusy)
        {
            return false;
        }

        var dialog = new HyperpagePageCreationDialog(
            viewModel.PageCreationTemplates,
            viewModel.PageCreationFileTypes,
            stylesheet)
        {
            Owner = owner
        };
        dialog.StylesheetLocationText.Text = "Linked stylesheets are created in "
            + HyperpageDocumentTypeCatalog.DefaultDirectory(
                HyperpageDocumentTypeCatalog.GetRequired("css"), viewModel.Project)
            + ". Uncheck to create an unlinked document in the selected project folder.";
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (dialog.StylesheetFileName is { } fileName)
        {
            var document = await viewModel.CreateProjectDocumentAsync(
                "css", fileName, dialog.LinkStylesheetToCurrentPage);
            if (document is null)
            {
                MessageBox.Show(owner, viewModel.StatusMessage, "Create stylesheet",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return document is not null;
        }
        if (dialog.CreationRequest is null)
        {
            return false;
        }
        if (!viewModel.CanCreatePage)
        {
            MessageBox.Show(owner, "Save or undo the active page before creating another page.",
                "Create Page", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var created = await viewModel.CreatePageAsync(dialog.CreationRequest);
        if (!created)
        {
            MessageBox.Show(
                owner,
                viewModel.StatusMessage,
                "Create Page",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        return created;
    }

    private static string FileTypeName(HyperpagePageFileType fileType) =>
        fileType switch
        {
            HyperpagePageFileType.Html => "Static HTML (.html)",
            HyperpagePageFileType.Cshtml => "Razor markup (.cshtml)",
            HyperpagePageFileType.RazorPage => "Razor page (.razor)",
            _ => "Project default"
        };

    private void FileTypeSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (StylesheetSection is null)
        {
            return;
        }
        var stylesheet = FileTypeComboBox.SelectedItem is FileTypeOption { IsStylesheet: true };
        Title = stylesheet ? "Stylesheet" : "Page";
        HeadingText.Text = stylesheet ? "Stylesheet" : "Page";
        CreateButton.ToolTip = stylesheet ? "Create stylesheet" : "Create page";
        AutomationProperties.SetName(CreateButton, stylesheet ? "Create stylesheet" : "Create page");
        AutomationProperties.SetName(CloseButton, stylesheet ? "Cancel stylesheet creation" : "Cancel page creation");
        SubtitleText.Text = stylesheet ? "Plain CSS" : "Describe page";
        NameLabel.Text = stylesheet ? "FILE NAME" : "PAGE NAME";
        TemplateSection.IsEnabled = !stylesheet;
        TemplateSection.Opacity = stylesheet ? 0.4 : 1;
        PageDescriptionSection.Visibility = stylesheet ? Visibility.Collapsed : Visibility.Visible;
        StylesheetSection.Visibility = stylesheet ? Visibility.Visible : Visibility.Collapsed;
        MinHeight = stylesheet ? 360 : 520;
        Height = stylesheet ? 390 : 590;
        if (stylesheet && PageNameTextBox.Text == "New Page")
        {
            PageNameTextBox.Text = "site.css";
        }
        else if (!stylesheet && PageNameTextBox.Text == "site.css")
        {
            PageNameTextBox.Text = "New Page";
        }
        StatusText.Text = string.Empty;
    }

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void CreateClick(object sender, RoutedEventArgs eventArgs)
    {
        StatusText.Text = string.Empty;
        var name = PageNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 200
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText.Text = "Enter a valid page name.";
            PageNameTextBox.Focus();
            return;
        }
        if (FileTypeComboBox.SelectedItem is FileTypeOption { IsStylesheet: true })
        {
            if (!string.IsNullOrEmpty(Path.GetExtension(name))
                && !Path.GetExtension(name).Equals(".css", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith('.'))
            {
                StatusText.Text = "Use a plain .css file name, for example site.css.";
                return;
            }
            StylesheetFileName = name;
            DialogResult = true;
            return;
        }
        var description = DescriptionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            StatusText.Text = "Describe the page for the AI assistant.";
            DescriptionTextBox.Focus();
            return;
        }
        if (TemplateComboBox.SelectedItem is not TemplateOption template
            || FileTypeComboBox.SelectedItem is not FileTypeOption fileType)
        {
            StatusText.Text = "Choose a template and file type.";
            return;
        }

        CreationRequest = new HyperpagePageCreationRequest(
            name,
            "/" + VisualPageProjectService.MakeSlug(name),
            fileType.FileType,
            template.Template,
            description);
        DialogResult = true;
    }
}
