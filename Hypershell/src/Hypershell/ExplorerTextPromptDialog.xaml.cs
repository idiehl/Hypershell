using System.Windows;
using System.Windows.Input;

namespace Hypershell;

public partial class ExplorerTextPromptDialog : Window
{
    public ExplorerTextPromptDialog(
        string heading,
        string prompt,
        string action,
        string initialValue = "")
    {
        InitializeComponent();
        Title = heading;
        HeadingText.Text = heading;
        PromptText.Text = prompt;
        AcceptButton.Content = action;
        ValueTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string Value { get; private set; } = string.Empty;

    private void AcceptClick(object sender, RoutedEventArgs eventArgs)
    {
        var value = ValueTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText.Text = "A value is required.";
            ValueTextBox.Focus();
            return;
        }

        Value = value;
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs) =>
        DialogResult = false;

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
