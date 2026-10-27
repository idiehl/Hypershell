using Hypershell.Models;
using Hypershell.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Hypershell;

public partial class NewTerminalDialog : Window
{
    public NewTerminalDialog(
        IReadOnlyList<TerminalProfile> profiles,
        TerminalProfile selectedProfile,
        IReadOnlyList<string> fontFamilies)
    {
        InitializeComponent();
        ShellProfileComboBox.ItemsSource = profiles;
        FontFamilyComboBox.ItemsSource = fontFamilies;
        ShellProfileComboBox.SelectedItem = profiles.Contains(selectedProfile)
            ? selectedProfile
            : profiles.FirstOrDefault();
    }

    public NewTerminalDialog(
        TerminalTabViewModel terminal,
        IReadOnlyList<string> fontFamilies)
    {
        InitializeComponent();
        ShellProfileComboBox.ItemsSource = new[] { terminal.Profile };
        FontFamilyComboBox.ItemsSource = fontFamilies;
        ShellProfileComboBox.SelectedItem = terminal.Profile;
        ShellProfileComboBox.IsEnabled = false;
        LoadTerminalConfiguration(terminal);
        Title = "Tab Settings";
        DialogTitleText.Text = "Tab Settings";
        DialogSubtitleText.Text = "Customize only this running tab; its saved terminal profile stays unchanged.";
        CreateButton.Content = "Apply";
        StartupCommandHintText.Text =
            "Launch commands do not rerun in an existing session. Change saved startup scripts in Settings.";
        ProfileDefaultsHintText.Text =
            "These changes apply only to this tab. Use Settings → Terminal Profiles for future tabs.";
    }

    public TerminalLaunchRequest? LaunchRequest { get; private set; }

    private void ShellProfileSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (ShellProfileComboBox.SelectedItem is not TerminalProfile profile)
        {
            return;
        }

        TitleTemplateTextBox.Text = profile.TabTitleTemplate;
        TabColorTextBox.Text = profile.TabColor;
        BackgroundColorTextBox.Text = profile.Settings.BackgroundColor;
        BackgroundImagePathTextBox.Text = profile.Settings.BackgroundImagePath;
        FontFamilyComboBox.SelectedItem = profile.Settings.FontFamilyName;
        if (FontFamilyComboBox.SelectedItem is null)
        {
            FontFamilyComboBox.Text = profile.Settings.FontFamilyName;
        }

        StartupCommandTextBox.Text = profile.StartupCommand;
        ValidationText.Text = string.Empty;
        RefreshColorPreviews();
    }

    private void ColorTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        RefreshColorPreviews();
    }

    private void RefreshColorPreviews()
    {
        if (TabColorPreview is null || BackgroundColorPreview is null)
        {
            return;
        }

        TabColorPreview.Background = TryCreateBrush(TabColorTextBox.Text) ?? Brushes.Transparent;
        BackgroundColorPreview.Background = TryCreateBrush(BackgroundColorTextBox.Text) ?? Brushes.Transparent;
    }

    private void ChooseTabColorClick(object sender, RoutedEventArgs eventArgs)
    {
        ChooseColor("TAB COLOR", TabColorTextBox);
    }

    private void ChooseTerminalColorClick(object sender, RoutedEventArgs eventArgs)
    {
        ChooseColor("TERMINAL COLOR", BackgroundColorTextBox);
    }

    private void ChooseColor(string purpose, TextBox target)
    {
        var picker = new ColorPickerDialog(purpose, target.Text)
        {
            Owner = this
        };

        if (picker.ShowDialog() == true)
        {
            target.Text = picker.SelectedColor;
        }
    }

    private void ChooseBackgroundImageClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose this terminal's background image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            BackgroundImagePathTextBox.Text = dialog.FileName;
        }
    }

    private void ClearBackgroundImageClick(object sender, RoutedEventArgs eventArgs)
    {
        BackgroundImagePathTextBox.Text = string.Empty;
    }

    private void CreateClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ShellProfileComboBox.SelectedItem is not TerminalProfile profile)
        {
            ValidationText.Text = "Choose a shell before creating the tab.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TitleTemplateTextBox.Text))
        {
            ValidationText.Text = "Give the tab a name before continuing.";
            TitleTemplateTextBox.Focus();
            return;
        }

        if (TryCreateBrush(TabColorTextBox.Text) is null)
        {
            ValidationText.Text = "Enter a valid tab color, such as #28374A.";
            TabColorTextBox.Focus();
            return;
        }

        if (TryCreateBrush(BackgroundColorTextBox.Text) is null)
        {
            ValidationText.Text = "Enter a valid terminal background color, such as #0D1117.";
            BackgroundColorTextBox.Focus();
            return;
        }

        var backgroundImagePath = BackgroundImagePathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(backgroundImagePath)
            && !File.Exists(backgroundImagePath))
        {
            ValidationText.Text = "The selected background image is no longer available.";
            return;
        }

        var fontFamilyName = FontFamilyComboBox.SelectedItem as string
            ?? FontFamilyComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fontFamilyName))
        {
            ValidationText.Text = "Choose a font for the terminal.";
            return;
        }

        LaunchRequest = new TerminalLaunchRequest
        {
            Profile = profile,
            TitleTemplate = TitleTemplateTextBox.Text.Trim(),
            TabColor = TabColorTextBox.Text.Trim(),
            BackgroundColor = BackgroundColorTextBox.Text.Trim(),
            BackgroundImagePath = backgroundImagePath,
            FontFamilyName = fontFamilyName,
            StartupCommand = StartupCommandTextBox.Text.Trim(),
            SaveAsProfileDefaults = false
        };
        DialogResult = true;
    }

    private void LoadTerminalConfiguration(TerminalTabViewModel terminal)
    {
        TitleTemplateTextBox.Text = terminal.Title;
        TabColorTextBox.Text = terminal.TabColor;
        BackgroundColorTextBox.Text = terminal.Settings.BackgroundColor;
        BackgroundImagePathTextBox.Text = terminal.Settings.BackgroundImagePath;
        FontFamilyComboBox.SelectedItem = terminal.Settings.FontFamilyName;
        if (FontFamilyComboBox.SelectedItem is null)
        {
            FontFamilyComboBox.Text = terminal.Settings.FontFamilyName;
        }

        StartupCommandTextBox.Text = terminal.StartupCommand;
        SaveDefaultsCheckBox.IsChecked = false;
        ValidationText.Text = string.Empty;
        RefreshColorPreviews();
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
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
