using Hypershell.Models;
using Hypershell.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell;

public partial class GitRepositoryOperationsDialog : Window
{
    private readonly SourceControlViewModel _viewModel;

    public GitRepositoryOperationsDialog(SourceControlViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        ResetModeComboBox.ItemsSource = Enum.GetValues<GitResetMode>();
        ResetModeComboBox.SelectedItem = GitResetMode.Mixed;
        CloneDestinationTextBox.Text = viewModel.WorkspaceLocation?.Identity
                                           ?? string.Empty;
        BareDestinationTextBox.Text = viewModel.WorkspaceLocation?.Identity
                                          ?? string.Empty;
    }

    private async void CreateBranchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (Require(NewBranchNameTextBox, "Enter a branch name.") is { } name)
        {
            await _viewModel.CreateBranchAsync(
                name,
                ValueOr(BranchStartPointTextBox, "HEAD"));
        }
    }

    private async void RenameBranchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedBranch is not { IsRemote: false } branch
            || Require(RenameBranchTextBox, "Select a local branch and enter its new name.") is not { } name)
        {
            return;
        }

        await _viewModel.RenameBranchAsync(branch.Name, name);
    }

    private async void DeleteBranchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedBranch is not { IsRemote: false } branch
            || !ConfirmDestructive(
                $"Permanently delete branch '{branch.Name}'?",
                "Delete branch"))
        {
            return;
        }

        await _viewModel.DeleteBranchAsync(
            branch.Name,
            force: true,
            destructiveConfirmed: true);
    }

    private async void MergeBranchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedBranch is { } branch)
        {
            await _viewModel.MergeAsync(branch.Name);
        }
    }

    private async void RebaseBranchClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedBranch is { } branch)
        {
            await _viewModel.StartRebaseAsync(branch.Name);
        }
    }

    private async void CreateTagClick(object sender, RoutedEventArgs eventArgs)
    {
        if (Require(TagNameTextBox, "Enter a tag name.") is { } name)
        {
            await _viewModel.CreateTagAsync(
                name,
                ValueOr(TagTargetTextBox, "HEAD"),
                TagMessageTextBox.Text.Trim());
        }
    }

    private async void DeleteTagClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedTag is not { } tag
            || !ConfirmDestructive(
                $"Permanently delete tag '{tag.Name}'?",
                "Delete tag"))
        {
            return;
        }

        await _viewModel.DeleteTagAsync(tag.Name, destructiveConfirmed: true);
    }

    private async void AddRemoteClick(object sender, RoutedEventArgs eventArgs)
    {
        var name = Require(RemoteNameTextBox, "Enter a remote name.");
        var url = Require(RemoteUrlTextBox, "Enter a remote URL.");
        if (name is not null && url is not null)
        {
            await _viewModel.AddRemoteAsync(name, url);
        }
    }

    private async void UpdateRemoteClick(object sender, RoutedEventArgs eventArgs)
    {
        var remote = _viewModel.SelectedRemote;
        var url = Require(RemoteUrlTextBox, "Select a remote and enter its URL.");
        if (remote is not null && url is not null)
        {
            await _viewModel.SetRemoteUrlAsync(
                remote.Name,
                url,
                PushUrlCheckBox.IsChecked == true);
        }
    }

    private async void RemoveRemoteClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedRemote is not { } remote
            || !ConfirmDestructive(
                $"Remove remote '{remote.Name}' from this repository?",
                "Remove remote"))
        {
            return;
        }

        await _viewModel.RemoveRemoteAsync(
            remote.Name,
            destructiveConfirmed: true);
    }

    private async void ForcePushClick(object sender, RoutedEventArgs eventArgs)
    {
        var remote = _viewModel.SelectedRemote?.Name
                     ?? ValueOr(RemoteNameTextBox, "origin");
        var branch = Require(ForceBranchTextBox, "Enter a branch to force-push.");
        if (branch is null
            || !ConfirmDestructive(
                $"Force-push '{branch}' to '{remote}' with lease? Remote commits may be replaced.",
                "Force push"))
        {
            return;
        }

        await _viewModel.ForcePushAsync(
            remote,
            branch,
            destructiveConfirmed: true);
    }

    private async void CreateStashClick(object sender, RoutedEventArgs eventArgs) =>
        await _viewModel.CreateStashAsync(
            StashMessageTextBox.Text.Trim(),
            IncludeUntrackedCheckBox.IsChecked == true);

    private async void ApplyStashClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedStash is { } stash)
        {
            await _viewModel.ApplyStashAsync(stash.Reference, pop: false);
        }
    }

    private async void PopStashClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedStash is { } stash)
        {
            await _viewModel.ApplyStashAsync(stash.Reference, pop: true);
        }
    }

    private async void DropStashClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedStash is not { } stash
            || !ConfirmDestructive(
                $"Permanently delete {stash.Reference}?",
                "Delete stash"))
        {
            return;
        }

        await _viewModel.DropStashAsync(
            stash.Reference,
            destructiveConfirmed: true);
    }

    private async void RevertCommitClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedCommit is { } commit)
        {
            await _viewModel.RevertAsync(commit.CommitId);
        }
    }

    private async void CherryPickClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedCommit is { } commit)
        {
            await _viewModel.CherryPickAsync(commit.CommitId);
        }
    }

    private async void ResetClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedCommit is not { } commit
            || ResetModeComboBox.SelectedItem is not GitResetMode mode
            || !ConfirmDestructive(
                $"Reset this repository to {commit.CommitId[..Math.Min(12, commit.CommitId.Length)]} using {mode} mode?",
                "Reset repository"))
        {
            return;
        }

        await _viewModel.ResetAsync(
            commit.CommitId,
            mode,
            destructiveConfirmed: true);
    }

    private async void ContinueMergeClick(object sender, RoutedEventArgs eventArgs) =>
        await _viewModel.ContinueMergeAsync();

    private async void AbortMergeClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ConfirmDestructive("Abort the active merge?", "Abort merge"))
        {
            await _viewModel.AbortMergeAsync();
        }
    }

    private async void ContinueRebaseClick(object sender, RoutedEventArgs eventArgs) =>
        await _viewModel.ContinueRebaseAsync(GitRebaseAction.Continue);

    private async void SkipRebaseClick(object sender, RoutedEventArgs eventArgs) =>
        await _viewModel.ContinueRebaseAsync(GitRebaseAction.Skip);

    private async void AbortRebaseClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ConfirmDestructive("Abort the active rebase?", "Abort rebase"))
        {
            await _viewModel.ContinueRebaseAsync(GitRebaseAction.Abort);
        }
    }

    private async void AbortCherryPickClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ConfirmDestructive("Abort the active cherry-pick?", "Abort cherry-pick"))
        {
            await _viewModel.AbortCherryPickAsync();
        }
    }

    private async void CloneClick(object sender, RoutedEventArgs eventArgs)
    {
        var source = Require(CloneSourceTextBox, "Enter a repository source.");
        var destination = Require(
            CloneDestinationTextBox,
            "Enter a destination workspace URI or local path.");
        if (source is not null && destination is not null)
        {
            await _viewModel.CloneAsync(
                source,
                destination,
                CloneBranchTextBox.Text.Trim());
        }
    }

    private async void InitializeBareClick(object sender, RoutedEventArgs eventArgs)
    {
        var destination = Require(
            BareDestinationTextBox,
            "Enter a bare repository destination.");
        if (destination is not null)
        {
            await _viewModel.InitializeBareAsync(destination);
        }
    }

    private string? Require(TextBox textBox, string message)
    {
        var value = textBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        DialogStatusText.Text = message;
        textBox.Focus();
        return null;
    }

    private static string ValueOr(TextBox textBox, string fallback) =>
        string.IsNullOrWhiteSpace(textBox.Text)
            ? fallback
            : textBox.Text.Trim();

    private bool ConfirmDestructive(string message, string title) =>
        MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

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
