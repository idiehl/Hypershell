using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace Hypershell.ViewModels;

public sealed partial class SourceControlViewModel : ObservableObject
{
    private readonly IGitRepositoryService _git;
    private WorkspaceLocation? _workspaceLocation;
    private GitRepositoryStatus? _repositoryStatus;
    private SourceControlChangeViewModel? _selectedChange;
    private GitBranchInfo? _selectedBranch;
    private string _commitMessage = string.Empty;
    private string _statusMessage = "Choose a local or linked-device workspace.";
    private string _gitToolStatus = "Git availability has not been checked.";
    private string _gitCredentialStatus =
        "Credential helper availability has not been checked.";
    private bool _isBusy;

    public SourceControlViewModel(IGitRepositoryService git)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanUseWorkspace);
        InitializeCommand = new AsyncRelayCommand(InitializeAsync, () =>
            CanUseWorkspace() && !HasRepository);
        StageSelectedCommand = new AsyncRelayCommand(
            StageSelectedAsync,
            () => SelectedChange is { CanStage: true } && !IsBusy);
        UnstageSelectedCommand = new AsyncRelayCommand(
            UnstageSelectedAsync,
            () => SelectedChange is { CanUnstage: true } && !IsBusy);
        StageAllCommand = new AsyncRelayCommand(
            StageAllAsync,
            () => Changes.Any(change => change.CanStage) && !IsBusy);
        CommitCommand = new AsyncRelayCommand(
            () => CommitAsync(amend: false),
            CanCommit);
        AmendCommand = new AsyncRelayCommand(
            () => CommitAsync(amend: true),
            CanCommit);
        FetchCommand = new AsyncRelayCommand(FetchAsync, CanUseRepository);
        PullCommand = new AsyncRelayCommand(PullAsync, CanUseRepository);
        PushCommand = new AsyncRelayCommand(PushAsync, CanUseRepository);
        CheckoutBranchCommand = new AsyncRelayCommand(
            CheckoutSelectedBranchAsync,
            () => CanUseRepository()
                  && SelectedBranch is { IsRemote: false, IsCurrent: false });
        OpenDiffCommand = new AsyncRelayCommand(
            OpenSelectedDiffAsync,
            () => CanUseRepository() && SelectedChange is not null);
        InitializeOperationCommands();
    }

    public ObservableCollection<SourceControlChangeViewModel> Changes { get; } = [];
    public ObservableCollection<GitBranchInfo> Branches { get; } = [];
    public ObservableCollection<GitCommitInfo> History { get; } = [];
    public ObservableCollection<GitRemoteInfo> Remotes { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand InitializeCommand { get; }
    public AsyncRelayCommand StageSelectedCommand { get; }
    public AsyncRelayCommand UnstageSelectedCommand { get; }
    public AsyncRelayCommand StageAllCommand { get; }
    public AsyncRelayCommand CommitCommand { get; }
    public AsyncRelayCommand AmendCommand { get; }
    public AsyncRelayCommand FetchCommand { get; }
    public AsyncRelayCommand PullCommand { get; }
    public AsyncRelayCommand PushCommand { get; }
    public AsyncRelayCommand CheckoutBranchCommand { get; }
    public AsyncRelayCommand OpenDiffCommand { get; }

    public event EventHandler<GitDiffDocument>? DiffRequested;

    public WorkspaceLocation? WorkspaceLocation => _workspaceLocation;
    public GitRepositoryStatus? RepositoryStatus => _repositoryStatus;
    public bool HasWorkspace => WorkspaceLocation is not null;
    public bool HasRepository => RepositoryStatus is not null;
    public bool HasChanges => Changes.Count > 0;
    public bool HasConflicts => RepositoryStatus?.HasConflicts == true;
    public string RepositoryRoot => RepositoryStatus?.Repository.Root.Identity
                                    ?? WorkspaceLocation?.Identity
                                    ?? string.Empty;
    public string BranchName => RepositoryStatus?.BranchName ?? string.Empty;
    public string Upstream => RepositoryStatus?.Upstream ?? string.Empty;
    public string AheadBehindText => RepositoryStatus is null
        ? string.Empty
        : RepositoryStatus.Ahead == 0 && RepositoryStatus.Behind == 0
            ? string.IsNullOrWhiteSpace(RepositoryStatus.Upstream)
                ? "No upstream"
                : "Up to date"
            : $"↑{RepositoryStatus.Ahead}  ↓{RepositoryStatus.Behind}";
    public string ChangeCountText => Changes.Count == 1
        ? "1 uncommitted change"
        : $"{Changes.Count} uncommitted changes";

    public SourceControlChangeViewModel? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (SetProperty(ref _selectedChange, value))
            {
                RefreshCommands();
            }
        }
    }

    public GitBranchInfo? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                CheckoutBranchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            if (SetProperty(ref _commitMessage, value))
            {
                CommitCommand.RaiseCanExecuteChanged();
                AmendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string GitToolStatus
    {
        get => _gitToolStatus;
        private set => SetProperty(ref _gitToolStatus, value);
    }

    public string GitCredentialStatus
    {
        get => _gitCredentialStatus;
        private set => SetProperty(ref _gitCredentialStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            RefreshCommands();
        }
    }

    public void SetWorkspaceLocation(WorkspaceLocation? location)
    {
        if (WorkspaceLocation is not null
            && location is not null
            && Hypershell.Models.WorkspaceLocation.SameIdentity(
                WorkspaceLocation,
                location))
        {
            return;
        }

        _workspaceLocation = location;
        _repositoryStatus = null;
        Changes.Clear();
        Branches.Clear();
        History.Clear();
        Remotes.Clear();
        ClearOperationState();
        SelectedChange = null;
        SelectedBranch = null;
        StatusMessage = location is null
            ? "Choose a local or linked-device workspace."
            : "Refresh to discover a Git repository.";
        RaiseRepositoryProperties();
        RefreshCommands();
    }

    public async Task RefreshAsync()
    {
        if (WorkspaceLocation is null || IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            GitToolAvailability? availability = null;
            if (WorkspaceLocation.IsLocal)
            {
                availability = await _git.GetAvailabilityAsync();
                GitToolStatus = availability.Message;
                GitCredentialStatus = availability.CredentialMessage;
            }
            else
            {
                GitToolStatus =
                    "Git is checked on the linked host when this workspace is used.";
                GitCredentialStatus =
                    "Remote HTTPS credentials use that host's configured credential helper; droplet repositories use the linked-host SSH credential.";
            }

            var discovered = await _git.DiscoverAsync(WorkspaceLocation);
            if (discovered is null)
            {
                _repositoryStatus = null;
                Changes.Clear();
                Branches.Clear();
                History.Clear();
                Remotes.Clear();
                ClearOperationState();
                StatusMessage = availability is { IsAvailable: false }
                    ? availability.Message
                    : "No Git repository found for this workspace.";
                RaiseRepositoryProperties();
                return;
            }

            var statusTask = _git.GetStatusAsync(discovered.Root);
            var branchesTask = _git.ListBranchesAsync(discovered.Root);
            var historyTask = _git.GetHistoryAsync(discovered.Root, 50);
            var remotesTask = _git.ListRemotesAsync(discovered.Root);
            var tagsTask = _git.ListTagsAsync(discovered.Root);
            var stashesTask = _git.ListStashesAsync(discovered.Root);
            await Task.WhenAll(
                statusTask,
                branchesTask,
                historyTask,
                remotesTask,
                tagsTask,
                stashesTask);
            _repositoryStatus = await statusTask;
            Replace(Changes, _repositoryStatus.Entries.Select(entry =>
                new SourceControlChangeViewModel(entry)));
            Replace(Branches, await branchesTask);
            Replace(History, await historyTask);
            Replace(Remotes, await remotesTask);
            Replace(Tags, await tagsTask);
            Replace(Stashes, await stashesTask);
            SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
            SelectedChange = Changes.FirstOrDefault();
            SelectOperationDefaults();
            StatusMessage = _repositoryStatus.HasConflicts
                ? "Resolve conflicts, stage the resolutions, then continue the active Git operation."
                : string.Empty;
            RaiseRepositoryProperties();
        });
    }

    public async Task CreateBranchAsync(string name, string startPoint = "HEAD")
    {
        if (WorkspaceLocation is null)
        {
            return;
        }

        await RunMutationAsync(
            () => _git.CreateBranchAsync(
                WorkspaceLocation,
                name,
                startPoint,
                checkout: true));
    }

    private async Task InitializeAsync()
    {
        if (WorkspaceLocation is null)
        {
            return;
        }

        await RunMutationAsync(() => _git.InitializeAsync(WorkspaceLocation));
    }

    private async Task StageSelectedAsync()
    {
        if (WorkspaceLocation is null || SelectedChange is null)
        {
            return;
        }

        await RunMutationAsync(() => _git.StageAsync(
            WorkspaceLocation,
            [SelectedChange.Path]));
    }

    private async Task UnstageSelectedAsync()
    {
        if (WorkspaceLocation is null || SelectedChange is null)
        {
            return;
        }

        await RunMutationAsync(() => _git.UnstageAsync(
            WorkspaceLocation,
            [SelectedChange.Path]));
    }

    private async Task StageAllAsync()
    {
        if (WorkspaceLocation is null)
        {
            return;
        }

        var paths = Changes.Where(change => change.CanStage)
            .Select(change => change.Path)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (paths.Count > 0)
        {
            await RunMutationAsync(() => _git.StageAsync(
                WorkspaceLocation,
                paths));
        }
    }

    private async Task CommitAsync(bool amend)
    {
        if (WorkspaceLocation is null)
        {
            return;
        }

        var message = CommitMessage;
        await RunMutationAsync(() => _git.CommitAsync(
            WorkspaceLocation,
            message,
            amend));
        CommitMessage = string.Empty;
    }

    private async Task FetchAsync()
    {
        if (WorkspaceLocation is not null)
        {
            await RunMutationAsync(() => _git.FetchAsync(WorkspaceLocation));
        }
    }

    private async Task PullAsync()
    {
        if (WorkspaceLocation is not null)
        {
            await RunMutationAsync(() => _git.PullAsync(WorkspaceLocation));
        }
    }

    private async Task PushAsync()
    {
        if (WorkspaceLocation is null || RepositoryStatus is null)
        {
            return;
        }

        var remote = string.IsNullOrWhiteSpace(RepositoryStatus.Upstream)
            ? Remotes.FirstOrDefault()?.Name ?? "origin"
            : RepositoryStatus.Upstream.Split('/')[0];
        await RunMutationAsync(() => _git.PushAsync(
            WorkspaceLocation,
            remote,
            RepositoryStatus.BranchName,
            setUpstream: string.IsNullOrWhiteSpace(RepositoryStatus.Upstream)));
    }

    private async Task CheckoutSelectedBranchAsync()
    {
        if (WorkspaceLocation is null || SelectedBranch is null)
        {
            return;
        }

        await RunMutationAsync(() => _git.CheckoutAsync(
            WorkspaceLocation,
            SelectedBranch.Name));
    }

    private async Task OpenSelectedDiffAsync()
    {
        if (WorkspaceLocation is null || SelectedChange is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var target = SelectedChange.IsStaged
                ? GitDiffTarget.Index
                : GitDiffTarget.WorkingTree;
            var diff = await _git.GetDiffAsync(
                WorkspaceLocation,
                SelectedChange.Path,
                target);
            DiffRequested?.Invoke(this, diff);
            StatusMessage = string.IsNullOrWhiteSpace(diff.Patch)
                ? "No text diff is available for this change."
                : $"Opened {target.ToString().ToLowerInvariant()} diff for {diff.Path}.";
        });
    }

    private async Task RunMutationAsync(
        Func<Task<GitOperationReceipt>> operation)
    {
        await RunAsync(async () =>
        {
            GitOperationReceipt? receipt = null;
            try
            {
                receipt = await operation();
            }
            finally
            {
                // A merge, rebase, or cherry-pick can legitimately return a
                // non-zero exit code after changing repository state. Refresh
                // even on failure so conflicts and recovery controls appear
                // immediately instead of requiring a manual refresh.
                await RefreshCoreAsync();
            }

            StatusMessage = receipt.Message;
        });
    }

    private async Task RefreshCoreAsync()
    {
        if (WorkspaceLocation is null)
        {
            return;
        }

        var status = await _git.GetStatusAsync(WorkspaceLocation);
        _repositoryStatus = status;
        Replace(Changes, status.Entries.Select(entry =>
            new SourceControlChangeViewModel(entry)));
        Replace(Branches, await _git.ListBranchesAsync(WorkspaceLocation));
        Replace(History, await _git.GetHistoryAsync(WorkspaceLocation, 50));
        Replace(Remotes, await _git.ListRemotesAsync(WorkspaceLocation));
        Replace(Tags, await _git.ListTagsAsync(WorkspaceLocation));
        Replace(Stashes, await _git.ListStashesAsync(WorkspaceLocation));
        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsCurrent);
        SelectedChange = Changes.FirstOrDefault();
        SelectOperationDefaults();
        RaiseRepositoryProperties();
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is GitRepositoryException
                                         or ArgumentException
                                         or InvalidOperationException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUseWorkspace() => WorkspaceLocation is not null && !IsBusy;
    private bool CanUseRepository() => HasRepository && !IsBusy;
    private bool CanCommit() => CanUseRepository()
                                && !string.IsNullOrWhiteSpace(CommitMessage)
                                && Changes.Any(change => change.IsStaged);

    private void RaiseRepositoryProperties()
    {
        OnPropertyChanged(nameof(RepositoryStatus));
        OnPropertyChanged(nameof(HasWorkspace));
        OnPropertyChanged(nameof(HasRepository));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(RepositoryRoot));
        OnPropertyChanged(nameof(BranchName));
        OnPropertyChanged(nameof(Upstream));
        OnPropertyChanged(nameof(AheadBehindText));
        OnPropertyChanged(nameof(ChangeCountText));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        InitializeCommand.RaiseCanExecuteChanged();
        StageSelectedCommand.RaiseCanExecuteChanged();
        UnstageSelectedCommand.RaiseCanExecuteChanged();
        StageAllCommand.RaiseCanExecuteChanged();
        CommitCommand.RaiseCanExecuteChanged();
        AmendCommand.RaiseCanExecuteChanged();
        FetchCommand.RaiseCanExecuteChanged();
        PullCommand.RaiseCanExecuteChanged();
        PushCommand.RaiseCanExecuteChanged();
        CheckoutBranchCommand.RaiseCanExecuteChanged();
        OpenDiffCommand.RaiseCanExecuteChanged();
        RefreshOperationCommands();
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

public sealed class SourceControlChangeViewModel
{
    public SourceControlChangeViewModel(GitWorkingTreeEntry entry)
    {
        Entry = entry;
    }

    public GitWorkingTreeEntry Entry { get; }
    public string Path => Entry.Path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public bool IsStaged => Entry.IsStaged;
    public bool CanStage => !Entry.IsIgnored && Entry.HasWorkingTreeChange;
    public bool CanUnstage => Entry.IsStaged;
    public bool IsConflict => Entry.IsConflict;
    public string StateGlyph => Entry.IsConflict
        ? "!"
        : Entry.IsUntracked
            ? "U"
            : Entry.IsStaged && Entry.HasWorkingTreeChange
                ? $"{Entry.IndexCode}{Entry.WorkingTreeCode}"
                : Entry.IsStaged
                    ? Entry.IndexCode.ToString()
                    : Entry.WorkingTreeCode.ToString();
}
