using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Hypershell;

public partial class HyperpageAssetImportDialog : Window
{
    public HyperpageAssetImportDialog(string sourcePath)
    {
        InitializeComponent();
        AssetNameTextBox.Text = Path.GetFileNameWithoutExtension(sourcePath);
        Loaded += (_, _) =>
        {
            AssetNameTextBox.Focus();
            AssetNameTextBox.SelectAll();
        };
    }

    public string AssetName { get; private set; } = string.Empty;

    public string AltText { get; private set; } = string.Empty;

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

    private void ImportClick(object sender, RoutedEventArgs eventArgs)
    {
        var name = AssetNameTextBox.Text.Trim();
        if (name.Length is 0 or > 120)
        {
            StatusText.Text = "Name required";
            AssetNameTextBox.Focus();
            return;
        }

        AssetName = name;
        AltText = AltTextBox.Text.Trim();
        DialogResult = true;
    }
}
