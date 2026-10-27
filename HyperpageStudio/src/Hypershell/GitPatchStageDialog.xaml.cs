using Hypershell.Models;
using Hypershell.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Hypershell;

public partial class GitPatchStageDialog : Window
{
    private readonly SourceControlViewModel _viewModel;

    public GitPatchStageDialog(
        SourceControlViewModel viewModel,
        GitDiffDocument diff)
    {
        InitializeComponent();
        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        PathText.Text = diff.Path;
        PatchTextBox.Text = diff.Patch;
        Loaded += (_, _) => PatchTextBox.Focus();
    }

    private async void StagePatchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(PatchTextBox.Text))
        {
            await _viewModel.ApplyPatchToIndexAsync(
                PatchTextBox.Text,
                reverse: false);
        }
    }

    private async void UnstagePatchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(PatchTextBox.Text))
        {
            await _viewModel.ApplyPatchToIndexAsync(
                PatchTextBox.Text,
                reverse: true);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs eventArgs) => Close();

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
