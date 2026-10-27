namespace Hypershell.Models;

public enum GitDiffTarget
{
    WorkingTree,
    Index,
    Commit
}

public enum GitResetMode
{
    Soft,
    Mixed,
    Hard
}

public enum GitRebaseAction
{
    Continue,
    Skip,
    Abort
}

public sealed record GitToolAvailability(
    bool IsAvailable,
    string ExecutablePath,
    string Version,
    string Message)
{
    public bool CredentialManagerAvailable { get; init; }
    public string CredentialManagerVersion { get; init; } = string.Empty;
    public string CredentialHelper { get; init; } = string.Empty;
    public string CredentialMessage { get; init; } = string.Empty;
}

public sealed record GitRepositoryIdentity(
    WorkspaceLocation Location,
    WorkspaceLocation Root,
    bool IsBare,
    string GitDirectory);

public sealed record GitWorkingTreeEntry(
    string Path,
    string OriginalPath,
    char IndexCode,
    char WorkingTreeCode,
    bool IsUntracked,
    bool IsIgnored,
    bool IsConflict,
    string SubmoduleState)
{
    public bool IsStaged => !IsUntracked
                            && !IsIgnored
                            && IndexCode is not '.' and not ' ';

    public bool HasWorkingTreeChange => IsUntracked
                                        || WorkingTreeCode is not '.' and not ' ';
}

public sealed record GitRepositoryStatus(
    GitRepositoryIdentity Repository,
    string BranchName,
    string HeadCommitId,
    string Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<GitWorkingTreeEntry> Entries)
{
    public bool IsDetached => BranchName == "(detached)";
    public bool HasChanges => Entries.Count > 0;
    public bool HasConflicts => Entries.Any(entry => entry.IsConflict);
}

public sealed record GitDiffDocument(
    string Path,
    GitDiffTarget Target,
    string BaseRevision,
    string Patch,
    bool IsBinary,
    bool IsTruncated);

public sealed record GitConflictDocument(
    string Path,
    string BaseContent,
    string OursContent,
    string TheirsContent,
    bool IsBinary,
    bool IsTruncated);

public sealed record GitCommitInfo(
    string CommitId,
    IReadOnlyList<string> ParentIds,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject,
    string Body,
    IReadOnlyList<string> Decorations);

public sealed record GitBranchInfo(
    string Name,
    string FullName,
    string CommitId,
    string Upstream,
    bool IsCurrent,
    bool IsRemote);

public sealed record GitTagInfo(
    string Name,
    string TargetCommitId,
    string Subject);

public sealed record GitRemoteInfo(
    string Name,
    string FetchUrl,
    string PushUrl);

public sealed record GitStashInfo(
    string Reference,
    string CommitId,
    string Message);

public sealed record GitOperationReceipt(
    string Operation,
    string Message,
    string StandardOutput = "");

public sealed record GitCommandInvocation(
    WorkspaceLocation? Repository,
    IReadOnlyList<string> Arguments,
    string StandardInput = "",
    bool IsReadOnly = false,
    TimeSpan? Timeout = null,
    int MaximumOutputCharacters = 8 * 1024 * 1024);

public sealed record GitCommandResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string FailureMessage,
    bool TimedOut,
    bool OutputLimitExceeded)
{
    public bool Succeeded => Started
                             && ExitCode == 0
                             && !TimedOut
                             && !OutputLimitExceeded
                             && string.IsNullOrWhiteSpace(FailureMessage);
}
