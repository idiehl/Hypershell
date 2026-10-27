using Hypershell.Models;
using Hypershell.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hypershell;

public partial class NewDocumentDialog : Window
{
    private readonly string _suggestedBaseName;
    private EditorLanguageDefinition? _previousLanguage;
    private bool _isInitializing = true;

    public NewDocumentDialog(
        string suggestedBaseName,
        IReadOnlyList<string> fontFamilies)
    {
        InitializeComponent();
        _suggestedBaseName = suggestedBaseName;
        LanguageComboBox.ItemsSource = EditorLanguageCatalog.Languages;
        EncodingComboBox.ItemsSource = DocumentFormatCatalog.EncodingOptions;
        LineEndingComboBox.ItemsSource =
            DocumentFormatCatalog.NewDocumentLineEndingOptions;
        IndentationComboBox.ItemsSource = new[] { "Spaces", "Tabs" };
        TabSizeComboBox.ItemsSource = new[] { 2, 4, 8 };
        FontFamilyComboBox.ItemsSource = fontFamilies;
        FontSizeComboBox.ItemsSource = new[] { 12, 13, 14, 15, 16, 18, 20, 22 };
        DocumentNameTextBox.Text = suggestedBaseName;
        LanguageComboBox.SelectedItem = EditorLanguageCatalog.Languages[0];
        EncodingComboBox.SelectedValue = DocumentEncodingKind.Utf8;
        LineEndingComboBox.SelectedValue = DocumentLineEndingKind.CrLf;
        ApplyLanguageDefaults(EditorLanguageCatalog.Languages[0], updateName: true);
        _isInitializing = false;
    }

    public NewDocumentRequest? Request { get; private set; }

    private void LanguageSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_isInitializing
            || LanguageComboBox.SelectedItem is not EditorLanguageDefinition language)
        {
            return;
        }

        ApplyLanguageDefaults(language, updateName: true);
    }

    private void ApplyLanguageDefaults(
        EditorLanguageDefinition language,
        bool updateName)
    {
        if (updateName)
        {
            DocumentNameTextBox.Text = CreateSuggestedName(
                DocumentNameTextBox.Text,
                _previousLanguage,
                language);
        }

        IndentationComboBox.SelectedItem = language.DefaultInsertSpaces
            ? "Spaces"
            : "Tabs";
        TabSizeComboBox.SelectedItem = language.DefaultTabSize;
        WordWrapCheckBox.IsChecked = language.DefaultWordWrap;
        MinimapCheckBox.IsChecked = true;
        LineNumbersCheckBox.IsChecked = true;
        BracketGuidesCheckBox.IsChecked = true;
        FormatOnTypeCheckBox.IsChecked = true;
        FormatOnPasteCheckBox.IsChecked = true;
        TrimTrailingWhitespaceCheckBox.IsChecked = true;
        FinalNewlineCheckBox.IsChecked = language.Id is not "plaintext" and not "markdown";
        FontSizeComboBox.SelectedItem = 14;
        FontFamilyComboBox.SelectedItem = "JetBrains Mono";
        if (FontFamilyComboBox.SelectedItem is null)
        {
            FontFamilyComboBox.SelectedItem = FontFamilyComboBox.Items
                .OfType<string>()
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(TabColorTextBox.Text))
        {
            TabColorTextBox.Text = "#1F3B58";
        }

        LanguageHintText.Text =
            $"Monaco mode: {language.Id} · Default extension: {language.DefaultExtension}";
        var templates = DocumentTemplateCatalog.GetTemplates(language.Id);
        TemplateComboBox.ItemsSource = templates;
        TemplateComboBox.SelectedItem = templates.First(template =>
            template.IsDefault);
        RefreshTemplateHint();
        ValidationText.Text = string.Empty;
        _previousLanguage = language;
        RefreshTabColorPreview();
    }

    private void TemplateSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs) => RefreshTemplateHint();

    private void RefreshTemplateHint()
    {
        if (TemplateHintText is null)
        {
            return;
        }

        TemplateHintText.Text =
            TemplateComboBox.SelectedItem is DocumentTemplateDefinition template
                ? template.Description
                : string.Empty;
    }

    private string CreateSuggestedName(
        string currentName,
        EditorLanguageDefinition? previousLanguage,
        EditorLanguageDefinition language)
    {
        var trimmedName = currentName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return _suggestedBaseName + language.DefaultExtension;
        }

        var extension = Path.GetExtension(trimmedName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return trimmedName + language.DefaultExtension;
        }

        if (previousLanguage is not null
            && string.Equals(
                extension,
                previousLanguage.DefaultExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(trimmedName)
                   + language.DefaultExtension;
        }

        return trimmedName;
    }

    private void CreateClick(object sender, RoutedEventArgs eventArgs)
    {
        var fileName = DocumentNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            ValidationText.Text = "Give the document a name before continuing.";
            DocumentNameTextBox.Focus();
            return;
        }

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ValidationText.Text = "Use a file name here, not a folder path.";
            DocumentNameTextBox.Focus();
            return;
        }

        if (LanguageComboBox.SelectedItem is not EditorLanguageDefinition language
            || TemplateComboBox.SelectedItem is not DocumentTemplateDefinition template
            || TabSizeComboBox.SelectedItem is not int tabSize
            || FontSizeComboBox.SelectedItem is not int fontSize
            || EncodingComboBox.SelectedValue is not DocumentEncodingKind encodingKind
            || LineEndingComboBox.SelectedValue is not DocumentLineEndingKind lineEndingKind)
        {
            ValidationText.Text =
                "Choose a language, file encoding, line ending, indentation size, and font size.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
        {
            fileName += language.DefaultExtension;
        }

        var fontFamily = FontFamilyComboBox.SelectedItem as string
                         ?? FontFamilyComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            ValidationText.Text = "Choose a font for the editor.";
            return;
        }

        if (TryCreateBrush(TabColorTextBox.Text) is null)
        {
            ValidationText.Text = "Choose a valid tab color.";
            return;
        }

        Request = new NewDocumentRequest(
            fileName,
            new DocumentEditorSettings
            {
                LanguageId = language.Id,
                TabSize = tabSize,
                InsertSpaces = string.Equals(
                    IndentationComboBox.SelectedItem as string,
                    "Spaces",
                    StringComparison.Ordinal),
                DetectIndentation = false,
                WordWrap = WordWrapCheckBox.IsChecked == true,
                MinimapEnabled = MinimapCheckBox.IsChecked == true,
                LineNumbersEnabled = LineNumbersCheckBox.IsChecked == true,
                BracketPairGuidesEnabled = BracketGuidesCheckBox.IsChecked == true,
                FormatOnType = FormatOnTypeCheckBox.IsChecked == true,
                FormatOnPaste = FormatOnPasteCheckBox.IsChecked == true,
                TrimTrailingWhitespaceOnSave =
                    TrimTrailingWhitespaceCheckBox.IsChecked == true,
                InsertFinalNewlineOnSave = FinalNewlineCheckBox.IsChecked == true,
                FontFamily = fontFamily,
                FontSize = fontSize,
                TabColor = TabColorTextBox.Text.Trim()
            },
            encodingKind,
            lineEndingKind,
            template.Content,
            template.Id);
        DialogResult = true;
    }

    private void ChooseTabColorClick(object sender, RoutedEventArgs eventArgs)
    {
        var picker = new ColorPickerDialog("DOCUMENT TAB COLOR", TabColorTextBox.Text)
        {
            Owner = this
        };
        if (picker.ShowDialog() == true)
        {
            TabColorTextBox.Text = picker.SelectedColor;
        }
    }

    private void TabColorTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        RefreshTabColorPreview();
    }

    private void RefreshTabColorPreview()
    {
        if (TabColorPreview is not null)
        {
            TabColorPreview.Background =
                TryCreateBrush(TabColorTextBox.Text) ?? Brushes.Transparent;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private static Brush? TryCreateBrush(string value)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!);
        }
        catch
        {
            return null;
        }
    }
}
