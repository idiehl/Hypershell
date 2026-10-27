using System.Windows;
using System.Windows.Input;

namespace Hypershell;

public enum RemoteDocumentConflictResolution
{
    Cancel,
    Reload,
    Overwrite,
    SaveCopy
}

public partial class RemoteDocumentConflictDialog : Window
{
    public RemoteDocumentConflictDialog(
        string documentName,
        string location,
        string detail)
    {
        InitializeComponent();
        DocumentNameText.Text = documentName;
        LocationText.Text = location;
        DetailText.Text = detail;
    }

    public RemoteDocumentConflictResolution Resolution { get; private set; }

    private void ReloadClick(object sender, RoutedEventArgs eventArgs) =>
        Complete(RemoteDocumentConflictResolution.Reload);

    private void OverwriteClick(object sender, RoutedEventArgs eventArgs) =>
        Complete(RemoteDocumentConflictResolution.Overwrite);

    private void SaveCopyClick(object sender, RoutedEventArgs eventArgs) =>
        Complete(RemoteDocumentConflictResolution.SaveCopy);

    private void CancelClick(object sender, RoutedEventArgs eventArgs) =>
        Complete(RemoteDocumentConflictResolution.Cancel);

    private void Complete(RemoteDocumentConflictResolution resolution)
    {
        Resolution = resolution;
        DialogResult = resolution != RemoteDocumentConflictResolution.Cancel;
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
}
