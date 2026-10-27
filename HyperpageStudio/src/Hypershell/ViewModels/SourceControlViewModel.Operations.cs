using Hypershell.Infrastructure;
using Hypershell.Models;
using System.Collections.ObjectModel;

namespace Hypershell.ViewModels;

public sealed partial class SourceControlViewModel
{
    private GitCommitInfo? _selectedCommit;
    private GitTagInfo? _selectedTag;
    private GitRemoteInfo? _selectedRemote;
    private GitStashInfo? _selectedStash;

    public ObservableCollection<GitTagInfo> Tags { get; } = [];
    public ObservableCollection<GitStashInfo> Stashes { get; } = [];

    public AsyncRelayCommand OpenCommitDiffCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenConflictCommand { get; private set; } = null!;
    public AsyncRelayCommand ResolveConflictCommand { get; private set; } = null!;

    public event EventHandler<GitConflictDocument>? ConflictRequested;

    public GitCommitInfo? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
            {
                RefreshOperationCommands();
            }
        }
    }

    public GitTagInfo? SelectedTag
    {
        get => _selectedTag;
        set => SetProperty(ref _selectedTag, value);
    }

    public GitRemoteInfo? SelectedRemote
    {
        get => _selectedRemote;
        set => SetProperty(ref _selectedRemote, value);
    }

    public GitStashInfo? SelectedStash
    {
        get => _selectedStash;
        set => SetProperty(ref _selectedStash, value);
    }

    public async Task CloneAsync(
        string source,
        string destinationIdentity,
        string branch = "")
    {
        var destination = ParseWorkspaceLocation(destinationIdentity);
        await RunAsync(async () =>
        {
            var receipt = await _git.CloneAsync(source, destination, branch);
            SetWorkspaceLocation(destination);
            await RefreshCoreAsync();
            StatusMessage = receipt.Message;
        });
    }

    public async Task InitializeBareAsync(string destinationIdentity)
    {
        var destination = ParseWorkspaceLocation(destinationIdentity);
        await RunAsync(async () =>
        {
            var receipt = await _git.InitializeAsync(destination, bare: true);
            StatusMessage = receipt.Message;
        });
    }

    public Task RenameBranchAsync(string currentName, string newName) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.RenameBranchAsync(
                WorkspaceLocation,
                currentName,
                newName));

    public Task DeleteBranchAsync(
        string name,
        bool force,
        bool destructiveConfirmed) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.DeleteBranchAsync(
                WorkspaceLocation,
                name,
                force,
                destructiveConfirmed));

    public Task CreateTagAsync(
        string name,
        string target,
        string message) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.CreateTagAsync(
                WorkspaceLocation,
                name,
                target,
                message));

    public Task DeleteTagAsync(
        string name,
        bool destructiveConfirmed) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.DeleteTagAsync(
                WorkspaceLocation,
                name,
                destructiveConfirmed));

    public Task AddRemoteAsync(string name, string url) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.AddRemoteAsync(
                WorkspaceLocation,
                name,
                url));

    public Task SetRemoteUrlAsync(
        string name,
        string url,
        bool pushUrl = false) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.SetRemoteUrlAsync(
                WorkspaceLocation,
                name,
                url,
                pushUrl));

    public Task RemoveRemoteAsync(
        string name,
        bool destructiveConfirmed) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.RemoveRemoteAsync(
                WorkspaceLocation,
                name,
                destructiveConfirmed));

    public Task CreateStashAsync(
        string message,
        bool includeUntracked) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.CreateStashAsync(
                WorkspaceLocation,
                message,
                includeUntracked));

    public Task ApplyStashAsync(string reference, bool pop) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.ApplyStashAsync(
                WorkspaceLocation,
                reference,
                pop));

    public Task DropStashAsync(
        string reference,
        bool destructiveConfirmed) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.DropStashAsync(
                WorkspaceLocation,
                reference,
                destructiveConfirmed));

    public Task MergeAsync(string reference) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.MergeAsync(
                WorkspaceLocation,
                reference));

    public Task ContinueMergeAsync() =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.ContinueMergeAsync(
                WorkspaceLocation));

    public Task AbortMergeAsync() =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.AbortMergeAsync(
                WorkspaceLocation));

    public Task StartRebaseAsync(string upstream) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.StartRebaseAsync(
                WorkspaceLocation,
                upstream));

    public Task ContinueRebaseAsync(GitRebaseAction action) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.ContinueRebaseAsync(
                WorkspaceLocation,
                action));

    public Task RevertAsync(string commitId) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.RevertAsync(
                WorkspaceLocation,
                commitId));

    public Task ResetAsync(
        string target,
        GitResetMode mode,
        bool destructiveConfirmed) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.ResetAsync(
                WorkspaceLocation,
                target,
                mode,
                destructiveConfirmed));

    public Task CherryPickAsync(string commitId) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.CherryPickAsync(
                WorkspaceLocation,
                commitId));

    public Task AbortCherryPickAsync() =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.AbortCherryPickAsync(
                WorkspaceLocation));

    public Task ForcePushAsync(
        string remote,
        string branch,
        bool destructiveConfirmed) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.PushAsync(
                WorkspaceLocation,
                remote,
                branch,
                forceWithLease: true,
                destructiveConfirmed: destructiveConfirmed));

    public Task ApplyPatchToIndexAsync(string patch, bool reverse) =>
        WorkspaceLocation is null
            ? Task.CompletedTask
            : RunMutationAsync(() => _git.ApplyPatchToIndexAsync(
                WorkspaceLocation,
                patch,
                reverse));

    public async Task<GitDiffDocument?> GetSelectedDiffAsync()
    {
        if (WorkspaceLocation is null || SelectedChange is null)
        {
            return null;
        }

        GitDiffDocument? document = null;
        await RunAsync(async () =>
        {
            document = await _git.GetDiffAsync(
                WorkspaceLocation,
                SelectedChange.Path,
                SelectedChange.IsStaged
                    ? GitDiffTarget.Index
                    : GitDiffTarget.WorkingTree);
        });
        return document;
    }

    private void InitializeOperationCommands()
    {
        OpenCommitDiffCommand = new AsyncRelayCommand(
            OpenSelectedCommitDiffAsync,
            () => CanUseRepository() && SelectedCommit is not null);
        OpenConflictCommand = new AsyncRelayCommand(
            OpenSelectedConflictAsync,
            () => CanUseRepository()
                  && SelectedChange is { IsConflict: true });
        ResolveConflictCommand = new AsyncRelayCommand(
            ResolveSelectedConflictAsync,
            () => CanUseRepository()
                  && SelectedChange is { IsConflict: true });
    }

    private async Task OpenSelectedCommitDiffAsync()
    {
        if (WorkspaceLocation is null || SelectedCommit is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var diff = await _git.GetCommitPatchAsync(
                WorkspaceLocation,
                SelectedCommit.CommitId);
            DiffRequested?.Invoke(this, diff);
            StatusMessage = $"Opened commit {SelectedCommit.CommitId[..Math.Min(12, SelectedCommit.CommitId.Length)]}.";
        });
    }

    private async Task OpenSelectedConflictAsync()
    {
        if (WorkspaceLocation is null || SelectedChange is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var conflict = await _git.GetConflictAsync(
                WorkspaceLocation,
                SelectedChange.Path);
            ConflictRequested?.Invoke(this, conflict);
            StatusMessage = $"Opened three-way conflict data for {conflict.Path}.";
        });
    }

    private async Task ResolveSelectedConflictAsync()
    {
        if (WorkspaceLocation is null || SelectedChange is null)
        {
            return;
        }

        await RunMutationAsync(() => _git.StageAsync(
            WorkspaceLocation,
            [SelectedChange.Path]));
    }

    private void ClearOperationState()
    {
        Tags.Clear();
        Stashes.Clear();
        SelectedCommit = null;
        SelectedTag = null;
        SelectedRemote = null;
        SelectedStash = null;
    }

    private void SelectOperationDefaults()
    {
        SelectedCommit = History.FirstOrDefault();
        SelectedTag = Tags.FirstOrDefault();
        SelectedRemote = Remotes.FirstOrDefault();
        SelectedStash = Stashes.FirstOrDefault();
    }

    private void RefreshOperationCommands()
    {
        OpenCommitDiffCommand?.RaiseCanExecuteChanged();
        OpenConflictCommand?.RaiseCanExecuteChanged();
        ResolveConflictCommand?.RaiseCanExecuteChanged();
    }

    private static WorkspaceLocation ParseWorkspaceLocation(string identity)
    {
        if (WorkspaceLocation.TryParse(identity, out var location))
        {
            return location;
        }

        return WorkspaceLocation.Local(identity);
    }
}
