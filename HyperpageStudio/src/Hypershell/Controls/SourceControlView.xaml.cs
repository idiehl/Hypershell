using Hypershell.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Hypershell.Controls;

public partial class SourceControlView : UserControl
{
    public SourceControlView()
    {
        InitializeComponent();
    }

    private void OpenRepositoryOperationsClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not SourceControlViewModel viewModel)
        {
            return;
        }

        var dialog = new GitRepositoryOperationsDialog(viewModel)
        {
            Owner = Window.GetWindow(this)
        };
        _ = dialog.ShowDialog();
    }

    private async void OpenPatchStagingClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not SourceControlViewModel viewModel)
        {
            return;
        }

        var diff = await viewModel.GetSelectedDiffAsync();
        if (diff is null || string.IsNullOrWhiteSpace(diff.Patch))
        {
            return;
        }

        var dialog = new GitPatchStageDialog(viewModel, diff)
        {
            Owner = Window.GetWindow(this)
        };
        _ = dialog.ShowDialog();
    }
}
