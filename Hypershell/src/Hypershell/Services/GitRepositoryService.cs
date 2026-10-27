using Hypershell.Models;
using System.Globalization;
using System.IO;

namespace Hypershell.Services;

public interface IGitRepositoryService
{
    Task<GitToolAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);
    Task<GitRepositoryIdentity?> DiscoverAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> InitializeAsync(
        WorkspaceLocation location,
        bool bare = false,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CloneAsync(
        string source,
        WorkspaceLocation destination,
        string branch = "",
        CancellationToken cancellationToken = default);
    Task<GitRepositoryStatus> GetStatusAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitDiffDocument> GetDiffAsync(
        WorkspaceLocation location,
        string path,
        GitDiffTarget target,
        string baseRevision = "",
        CancellationToken cancellationToken = default);
    Task<GitDiffDocument> GetCommitPatchAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default);
    Task<GitConflictDocument> GetConflictAsync(
        WorkspaceLocation location,
        string path,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> StageAsync(
        WorkspaceLocation location,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> UnstageAsync(
        WorkspaceLocation location,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> ApplyPatchToIndexAsync(
        WorkspaceLocation location,
        string patch,
        bool reverse = false,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CommitAsync(
        WorkspaceLocation location,
        string message,
        bool amend = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CreateBranchAsync(
        WorkspaceLocation location,
        string name,
        string startPoint = "HEAD",
        bool checkout = true,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CheckoutAsync(
        WorkspaceLocation location,
        string reference,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> RenameBranchAsync(
        WorkspaceLocation location,
        string currentName,
        string newName,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> DeleteBranchAsync(
        WorkspaceLocation location,
        string name,
        bool force,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitTagInfo>> ListTagsAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CreateTagAsync(
        WorkspaceLocation location,
        string name,
        string target = "HEAD",
        string message = "",
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> DeleteTagAsync(
        WorkspaceLocation location,
        string name,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitRemoteInfo>> ListRemotesAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> AddRemoteAsync(
        WorkspaceLocation location,
        string name,
        string url,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> SetRemoteUrlAsync(
        WorkspaceLocation location,
        string name,
        string url,
        bool pushUrl = false,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> RemoveRemoteAsync(
        WorkspaceLocation location,
        string name,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> FetchAsync(
        WorkspaceLocation location,
        string remote = "--all",
        bool prune = true,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> PullAsync(
        WorkspaceLocation location,
        string remote = "",
        string branch = "",
        bool rebase = false,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> PushAsync(
        WorkspaceLocation location,
        string remote = "origin",
        string branch = "",
        bool setUpstream = false,
        bool forceWithLease = false,
        bool destructiveConfirmed = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitCommitInfo>> GetHistoryAsync(
        WorkspaceLocation location,
        int maximum = 100,
        string path = "",
        CancellationToken cancellationToken = default);
    Task<GitCommitInfo> GetCommitAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitStashInfo>> ListStashesAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CreateStashAsync(
        WorkspaceLocation location,
        string message,
        bool includeUntracked = true,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> ApplyStashAsync(
        WorkspaceLocation location,
        string reference,
        bool pop,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> DropStashAsync(
        WorkspaceLocation location,
        string reference,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> MergeAsync(
        WorkspaceLocation location,
        string reference,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> AbortMergeAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> ContinueMergeAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> StartRebaseAsync(
        WorkspaceLocation location,
        string upstream,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> ContinueRebaseAsync(
        WorkspaceLocation location,
        GitRebaseAction action,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> RevertAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> ResetAsync(
        WorkspaceLocation location,
        string target,
        GitResetMode mode,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> CherryPickAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default);
    Task<GitOperationReceipt> AbortCherryPickAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);
}

public sealed class GitRepositoryException : Exception
{
    public GitRepositoryException(
        string operation,
        string message,
        int? exitCode = null)
        : base(message)
    {
        Operation = operation;
        ExitCode = exitCode;
    }

    public string Operation { get; }
    public int? ExitCode { get; }
}

public sealed class GitRepositoryService : IGitRepositoryService
{
    public const int MaximumPathsPerOperation = 2048;
    public const int MaximumPatchCharacters = 8 * 1024 * 1024;

    private readonly IGitCommandRunner _runner;

    public GitRepositoryService(IGitCommandRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task<GitToolAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        _runner.GetAvailabilityAsync(cancellationToken);

    public async Task<GitRepositoryIdentity?> DiscoverAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        EnsureLocation(location);
        var root = await _runner.RunAsync(
            Command(location, ["rev-parse", "--path-format=absolute", "--show-toplevel"],
                readOnly: true),
            cancellationToken);
        if (!root.Succeeded)
        {
            return null;
        }

        var rootPath = FirstLine(root.StandardOutput);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var rootLocation = location.IsLocal
            ? WorkspaceLocation.Local(rootPath)
            : WorkspaceLocation.Ssh(location.Authority, rootPath);
        var details = await RunRequiredAsync(
            "discover repository",
            Command(rootLocation,
            [
                "rev-parse",
                "--path-format=absolute",
                "--absolute-git-dir",
                "--is-bare-repository"
            ], readOnly: true),
            cancellationToken);
        var lines = Lines(details.StandardOutput);
        if (lines.Count < 2)
        {
            throw new GitRepositoryException(
                "discover repository",
                "Git returned incomplete repository metadata.");
        }

        return new GitRepositoryIdentity(
            location,
            rootLocation,
            string.Equals(lines[1], "true", StringComparison.Ordinal),
            lines[0]);
    }

    public async Task<GitOperationReceipt> InitializeAsync(
        WorkspaceLocation location,
        bool bare = false,
        CancellationToken cancellationToken = default)
    {
        EnsureLocation(location);
        if (!location.IsLocal)
        {
            return await ReceiptAsync(
                "initialize repository",
                location,
                bare ? ["init", "--bare"] : ["init"],
                cancellationToken: cancellationToken);
        }

        Directory.CreateDirectory(location.Path);
        return await ReceiptAsync(
            "initialize repository",
            location,
            bare ? ["init", "--bare"] : ["init"],
            cancellationToken: cancellationToken);
    }

    public async Task<GitOperationReceipt> CloneAsync(
        string source,
        WorkspaceLocation destination,
        string branch = "",
        CancellationToken cancellationToken = default)
    {
        EnsureText(source, nameof(source), 4096);
        GitRemoteCredentialPolicy.EnsureCredentialFree(source);
        EnsureLocation(destination);
        if (destination.IsLocal
            && Directory.Exists(destination.Path)
            && Directory.EnumerateFileSystemEntries(destination.Path).Any())
        {
            throw new GitRepositoryException(
                "clone repository",
                "The clone destination must be absent or empty.");
        }

        var (parent, directoryName) = GetCloneDestination(destination);
        if (parent.IsLocal)
        {
            Directory.CreateDirectory(parent.Path);
        }

        var arguments = new List<string> { "clone", "--progress" };
        if (!string.IsNullOrWhiteSpace(branch))
        {
            EnsureText(branch, nameof(branch), 512);
            arguments.AddRange(["--branch", branch.Trim()]);
        }

        arguments.Add("--");
        arguments.Add(source.Trim());
        arguments.Add(directoryName);
        return ToReceipt(
            "clone repository",
            await RunRequiredAsync(
                "clone repository",
                Command(
                    parent,
                    arguments,
                    timeout: TimeSpan.FromMinutes(10)),
                cancellationToken));
    }

    private static (WorkspaceLocation Parent, string DirectoryName)
        GetCloneDestination(WorkspaceLocation destination)
    {
        if (destination.IsLocal)
        {
            var parentPath = Path.GetDirectoryName(destination.Path)
                             ?? throw new GitRepositoryException(
                                 "clone repository",
                                 "The clone destination has no parent directory.");
            var directoryName = Path.GetFileName(
                destination.Path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                throw new GitRepositoryException(
                    "clone repository",
                    "The clone destination must name a child directory.");
            }

            return (WorkspaceLocation.Local(parentPath), directoryName);
        }

        var normalized = destination.Path.TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        if (separator < 0
            || separator == normalized.Length - 1
            || (separator == 0 && normalized.Length == 1))
        {
            throw new GitRepositoryException(
                "clone repository",
                "The remote clone destination must name a child directory beneath an existing parent.");
        }

        var remoteParentPath = separator == 0 ? "/" : normalized[..separator];
        var childName = normalized[(separator + 1)..];
        return (
            WorkspaceLocation.Ssh(destination.Authority, remoteParentPath),
            childName);
    }

    public async Task<GitRepositoryStatus> GetStatusAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var result = await RunRequiredAsync(
            "read repository status",
            Command(repository.Root,
            [
                "status",
                "--porcelain=v2",
                "-z",
                "--branch",
                "--untracked-files=all"
            ], readOnly: true, maximumOutput: 16 * 1024 * 1024),
            cancellationToken);
        return ParseStatus(repository, result.StandardOutput);
    }

    public async Task<GitDiffDocument> GetDiffAsync(
        WorkspaceLocation location,
        string path,
        GitDiffTarget target,
        string baseRevision = "",
        CancellationToken cancellationToken = default)
    {
        EnsurePath(path);
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var arguments = new List<string>
        {
            "diff", "--no-ext-diff", "--binary", "--full-index", "--unified=3"
        };
        switch (target)
        {
            case GitDiffTarget.Index:
                arguments.Add("--cached");
                break;
            case GitDiffTarget.Commit:
                EnsureText(baseRevision, nameof(baseRevision), 512);
                arguments.Add(baseRevision.Trim());
                break;
        }

        arguments.Add("--");
        arguments.Add(path);
        var result = await RunRequiredAsync(
            "read diff",
            Command(repository.Root, arguments, readOnly: true,
                maximumOutput: 16 * 1024 * 1024),
            cancellationToken);
        var patch = result.StandardOutput;
        return new GitDiffDocument(
            path,
            target,
            baseRevision.Trim(),
            patch,
            patch.Contains("GIT binary patch", StringComparison.Ordinal)
            || patch.Contains("Binary files ", StringComparison.Ordinal),
            false);
    }

    public async Task<GitDiffDocument> GetCommitPatchAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        EnsureText(commitId, nameof(commitId), 512);
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var normalized = commitId.Trim();
        var result = await RunRequiredAsync(
            "inspect commit patch",
            Command(repository.Root,
            [
                "show",
                "--no-ext-diff",
                "--binary",
                "--full-index",
                "--format=fuller",
                "--date=iso-strict",
                "--unified=3",
                normalized,
                "--"
            ], readOnly: true, maximumOutput: 16 * 1024 * 1024),
            cancellationToken);
        var patch = result.StandardOutput;
        return new GitDiffDocument(
            normalized,
            GitDiffTarget.Commit,
            normalized,
            patch,
            patch.Contains("GIT binary patch", StringComparison.Ordinal)
            || patch.Contains("Binary files ", StringComparison.Ordinal),
            result.OutputLimitExceeded);
    }

    public async Task<GitConflictDocument> GetConflictAsync(
        WorkspaceLocation location,
        string path,
        CancellationToken cancellationToken = default)
    {
        EnsurePath(path);
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var stageTasks = new[]
        {
            ReadConflictStageAsync(repository.Root, 1, path, cancellationToken),
            ReadConflictStageAsync(repository.Root, 2, path, cancellationToken),
            ReadConflictStageAsync(repository.Root, 3, path, cancellationToken)
        };
        await Task.WhenAll(stageTasks);
        var stages = stageTasks.Select(task => task.Result).ToArray();
        if (stages.All(stage => !stage.Exists))
        {
            throw new GitRepositoryException(
                "inspect conflict",
                "Git did not report base, ours, or theirs content for this path.");
        }

        return new GitConflictDocument(
            path,
            stages[0].Content,
            stages[1].Content,
            stages[2].Content,
            stages.Any(stage => stage.Content.IndexOf('\0') >= 0),
            stages.Any(stage => stage.Truncated));
    }

    public Task<GitOperationReceipt> StageAsync(
        WorkspaceLocation location,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default) =>
        PathsReceiptAsync(
            "stage files",
            location,
            ["add"],
            paths,
            cancellationToken);

    public async Task<GitOperationReceipt> UnstageAsync(
        WorkspaceLocation location,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var safePaths = ValidatePaths(paths);
        var arguments = new List<string> { "restore", "--staged", "--" };
        arguments.AddRange(safePaths);
        var restored = await _runner.RunAsync(
            Command(repository.Root, arguments),
            cancellationToken);
        if (restored.Succeeded)
        {
            return ToReceipt("unstage files", restored);
        }

        // An unborn branch has no HEAD for `restore --staged`; remove only the
        // requested paths from the index while preserving working files.
        arguments = ["rm", "--cached", "-r", "--"];
        arguments.AddRange(safePaths);
        return ToReceipt(
            "unstage files",
            await RunRequiredAsync(
                "unstage files",
                Command(repository.Root, arguments),
                cancellationToken));
    }

    public async Task<GitOperationReceipt> ApplyPatchToIndexAsync(
        WorkspaceLocation location,
        string patch,
        bool reverse = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(patch)
            || patch.Length > MaximumPatchCharacters
            || patch.IndexOf('\0') >= 0)
        {
            throw new GitRepositoryException(
                "stage hunk",
                "The selected patch is empty, malformed, or exceeds the 8 MiB limit.");
        }

        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var arguments = new List<string>
        {
            "apply", "--cached", "--recount", "--whitespace=nowarn"
        };
        if (reverse)
        {
            arguments.Add("--reverse");
        }

        arguments.Add("-");
        return ToReceipt(
            reverse ? "unstage hunk" : "stage hunk",
            await RunRequiredAsync(
                reverse ? "unstage hunk" : "stage hunk",
                Command(repository.Root, arguments, standardInput: patch),
                cancellationToken));
    }

    public async Task<GitOperationReceipt> CommitAsync(
        WorkspaceLocation location,
        string message,
        bool amend = false,
        CancellationToken cancellationToken = default)
    {
        EnsureText(message, nameof(message), 1024 * 1024);
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var arguments = new List<string> { "commit", "--file=-" };
        if (amend)
        {
            arguments.Add("--amend");
        }

        return ToReceipt(
            amend ? "amend commit" : "create commit",
            await RunRequiredAsync(
                amend ? "amend commit" : "create commit",
                Command(repository.Root, arguments, standardInput: message.Trim() + "\n",
                    timeout: TimeSpan.FromMinutes(2)),
                cancellationToken));
    }

    public async Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var result = await RunRequiredAsync(
            "list branches",
            Command(repository.Root,
            [
                "for-each-ref",
                "--sort=refname",
                "--format=%(refname)%00%(objectname)%00%(upstream:short)%00%(HEAD)%00",
                "refs/heads",
                "refs/remotes"
            ], readOnly: true),
            cancellationToken);
        return ParseBranches(result.StandardOutput);
    }

    public Task<GitOperationReceipt> CreateBranchAsync(
        WorkspaceLocation location,
        string name,
        string startPoint = "HEAD",
        bool checkout = true,
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        EnsureText(startPoint, nameof(startPoint), 512);
        return ReceiptAsync(
            "create branch",
            location,
            checkout
                ? ["switch", "--create", name.Trim(), startPoint.Trim()]
                : ["branch", name.Trim(), startPoint.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> CheckoutAsync(
        WorkspaceLocation location,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureText(reference, nameof(reference), 512);
        return ReceiptAsync(
            "checkout branch",
            location,
            ["switch", reference.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> RenameBranchAsync(
        WorkspaceLocation location,
        string currentName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        EnsureText(currentName, nameof(currentName), 512);
        EnsureText(newName, nameof(newName), 512);
        return ReceiptAsync(
            "rename branch",
            location,
            ["branch", "--move", currentName.Trim(), newName.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> DeleteBranchAsync(
        WorkspaceLocation location,
        string name,
        bool force,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        if (force && !destructiveConfirmed)
        {
            throw ConfirmationRequired("force-delete branch");
        }

        return ReceiptAsync(
            "delete branch",
            location,
            ["branch", force ? "--delete-force" : "--delete", name.Trim()],
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GitTagInfo>> ListTagsAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var result = await RunRequiredAsync(
            "list tags",
            Command(repository.Root,
            [
                "for-each-ref",
                "--sort=refname",
                "--format=%(refname:short)%00%(*objectname)%00%(objectname)%00%(subject)%00",
                "refs/tags"
            ], readOnly: true),
            cancellationToken);
        return ParseTags(result.StandardOutput);
    }

    public Task<GitOperationReceipt> CreateTagAsync(
        WorkspaceLocation location,
        string name,
        string target = "HEAD",
        string message = "",
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        EnsureText(target, nameof(target), 512);
        var arguments = new List<string> { "tag" };
        if (!string.IsNullOrWhiteSpace(message))
        {
            EnsureText(message, nameof(message), 64 * 1024);
            arguments.AddRange(["--annotate", "--message", message.Trim()]);
        }

        arguments.Add(name.Trim());
        arguments.Add(target.Trim());
        return ReceiptAsync(
            "create tag",
            location,
            arguments,
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> DeleteTagAsync(
        WorkspaceLocation location,
        string name,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        EnsureConfirmed("delete tag", destructiveConfirmed);
        return ReceiptAsync(
            "delete tag",
            location,
            ["tag", "--delete", name.Trim()],
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GitRemoteInfo>> ListRemotesAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var names = await RunRequiredAsync(
            "list remotes",
            Command(repository.Root, ["remote"], readOnly: true),
            cancellationToken);
        var remotes = new List<GitRemoteInfo>();
        foreach (var name in Lines(names.StandardOutput))
        {
            var fetch = await RunRequiredAsync(
                "read remote URL",
                Command(repository.Root, ["remote", "get-url", name], readOnly: true),
                cancellationToken);
            var push = await RunRequiredAsync(
                "read remote push URL",
                Command(repository.Root,
                    ["remote", "get-url", "--push", name], readOnly: true),
                cancellationToken);
            remotes.Add(new GitRemoteInfo(
                name,
                FirstLine(fetch.StandardOutput),
                FirstLine(push.StandardOutput)));
        }

        return remotes;
    }

    public Task<GitOperationReceipt> AddRemoteAsync(
        WorkspaceLocation location,
        string name,
        string url,
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        EnsureText(url, nameof(url), 4096);
        GitRemoteCredentialPolicy.EnsureCredentialFree(url);
        return ReceiptAsync(
            "add remote",
            location,
            ["remote", "add", name.Trim(), url.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> SetRemoteUrlAsync(
        WorkspaceLocation location,
        string name,
        string url,
        bool pushUrl = false,
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        EnsureText(url, nameof(url), 4096);
        GitRemoteCredentialPolicy.EnsureCredentialFree(url);
        var arguments = new List<string> { "remote", "set-url" };
        if (pushUrl)
        {
            arguments.Add("--push");
        }

        arguments.Add(name.Trim());
        arguments.Add(url.Trim());
        return ReceiptAsync(
            "set remote URL",
            location,
            arguments,
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> RemoveRemoteAsync(
        WorkspaceLocation location,
        string name,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureText(name, nameof(name), 512);
        EnsureConfirmed("remove remote", destructiveConfirmed);
        return ReceiptAsync(
            "remove remote",
            location,
            ["remote", "remove", name.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> FetchAsync(
        WorkspaceLocation location,
        string remote = "--all",
        bool prune = true,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "fetch", "--progress" };
        if (prune)
        {
            arguments.Add("--prune");
        }

        if (!string.IsNullOrWhiteSpace(remote))
        {
            EnsureText(remote, nameof(remote), 512);
            arguments.Add(remote.Trim());
        }

        return ReceiptAsync(
            "fetch",
            location,
            arguments,
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> PullAsync(
        WorkspaceLocation location,
        string remote = "",
        string branch = "",
        bool rebase = false,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "pull", "--progress", rebase ? "--rebase" : "--no-rebase"
        };
        if (!string.IsNullOrWhiteSpace(remote))
        {
            EnsureText(remote, nameof(remote), 512);
            arguments.Add(remote.Trim());
        }

        if (!string.IsNullOrWhiteSpace(branch))
        {
            EnsureText(branch, nameof(branch), 512);
            arguments.Add(branch.Trim());
        }

        return ReceiptAsync(
            "pull",
            location,
            arguments,
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> PushAsync(
        WorkspaceLocation location,
        string remote = "origin",
        string branch = "",
        bool setUpstream = false,
        bool forceWithLease = false,
        bool destructiveConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        EnsureText(remote, nameof(remote), 512);
        if (forceWithLease)
        {
            EnsureConfirmed("force push", destructiveConfirmed);
        }

        var arguments = new List<string> { "push", "--progress" };
        if (setUpstream)
        {
            arguments.Add("--set-upstream");
        }

        if (forceWithLease)
        {
            arguments.Add("--force-with-lease");
        }

        arguments.Add(remote.Trim());
        if (!string.IsNullOrWhiteSpace(branch))
        {
            EnsureText(branch, nameof(branch), 512);
            arguments.Add(branch.Trim());
        }

        return ReceiptAsync(
            "push",
            location,
            arguments,
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetHistoryAsync(
        WorkspaceLocation location,
        int maximum = 100,
        string path = "",
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        maximum = Math.Clamp(maximum, 1, 1000);
        var arguments = new List<string>
        {
            "log",
            $"--max-count={maximum}",
            "--date=iso-strict",
            "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%s%x00%b%x00%D%x1e"
        };
        if (!string.IsNullOrWhiteSpace(path))
        {
            EnsurePath(path);
            arguments.Add("--");
            arguments.Add(path);
        }

        var result = await _runner.RunAsync(
            Command(repository.Root, arguments, readOnly: true,
                maximumOutput: 16 * 1024 * 1024),
            cancellationToken);
        if (!result.Succeeded
            && result.StandardError.Contains(
                "does not have any commits yet",
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        EnsureSucceeded("read history", result);
        return ParseCommits(result.StandardOutput);
    }

    public async Task<GitCommitInfo> GetCommitAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        EnsureText(commitId, nameof(commitId), 512);
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var result = await RunRequiredAsync(
            "read commit",
            Command(repository.Root,
            [
                "show", "--no-patch", "--date=iso-strict",
                "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%s%x00%b%x00%D%x1e",
                commitId.Trim()
            ], readOnly: true),
            cancellationToken);
        return ParseCommits(result.StandardOutput).SingleOrDefault()
               ?? throw new GitRepositoryException(
                   "read commit",
                   "Git did not return the requested commit.");
    }

    public async Task<IReadOnlyList<GitStashInfo>> ListStashesAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var result = await RunRequiredAsync(
            "list stashes",
            Command(repository.Root,
            ["stash", "list", "--format=%gd%x00%H%x00%gs%x1e"], readOnly: true),
            cancellationToken);
        return result.StandardOutput
            .Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.TrimStart('\r', '\n').Split('\0'))
            .Where(fields => fields.Length >= 3)
            .Select(fields => new GitStashInfo(
                fields[0], fields[1], fields[2].TrimEnd('\r', '\n')))
            .ToList();
    }

    public Task<GitOperationReceipt> CreateStashAsync(
        WorkspaceLocation location,
        string message,
        bool includeUntracked = true,
        CancellationToken cancellationToken = default)
    {
        EnsureText(message, nameof(message), 64 * 1024);
        var arguments = new List<string> { "stash", "push" };
        if (includeUntracked)
        {
            arguments.Add("--include-untracked");
        }

        arguments.AddRange(["--message", message.Trim()]);
        return ReceiptAsync(
            "create stash",
            location,
            arguments,
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> ApplyStashAsync(
        WorkspaceLocation location,
        string reference,
        bool pop,
        CancellationToken cancellationToken = default)
    {
        EnsureText(reference, nameof(reference), 512);
        return ReceiptAsync(
            pop ? "pop stash" : "apply stash",
            location,
            ["stash", pop ? "pop" : "apply", "--index", reference.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> DropStashAsync(
        WorkspaceLocation location,
        string reference,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureText(reference, nameof(reference), 512);
        EnsureConfirmed("drop stash", destructiveConfirmed);
        return ReceiptAsync(
            "drop stash",
            location,
            ["stash", "drop", reference.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> MergeAsync(
        WorkspaceLocation location,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureText(reference, nameof(reference), 512);
        return ReceiptAsync(
            "merge",
            location,
            ["merge", "--no-edit", reference.Trim()],
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> AbortMergeAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default) =>
        ReceiptAsync(
            "abort merge",
            location,
            ["merge", "--abort"],
            cancellationToken: cancellationToken);

    public Task<GitOperationReceipt> ContinueMergeAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default) =>
        ReceiptAsync(
            "continue merge",
            location,
            ["merge", "--continue"],
            cancellationToken: cancellationToken);

    public Task<GitOperationReceipt> StartRebaseAsync(
        WorkspaceLocation location,
        string upstream,
        CancellationToken cancellationToken = default)
    {
        EnsureText(upstream, nameof(upstream), 512);
        return ReceiptAsync(
            "start rebase",
            location,
            ["rebase", upstream.Trim()],
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> ContinueRebaseAsync(
        WorkspaceLocation location,
        GitRebaseAction action,
        CancellationToken cancellationToken = default) =>
        ReceiptAsync(
            $"{action.ToString().ToLowerInvariant()} rebase",
            location,
            ["rebase", action switch
            {
                GitRebaseAction.Continue => "--continue",
                GitRebaseAction.Skip => "--skip",
                GitRebaseAction.Abort => "--abort",
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            }],
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);

    public Task<GitOperationReceipt> RevertAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        EnsureText(commitId, nameof(commitId), 512);
        return ReceiptAsync(
            "revert commit",
            location,
            ["revert", "--no-edit", commitId.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> ResetAsync(
        WorkspaceLocation location,
        string target,
        GitResetMode mode,
        bool destructiveConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureText(target, nameof(target), 512);
        EnsureConfirmed("reset repository", destructiveConfirmed);
        return ReceiptAsync(
            "reset repository",
            location,
            ["reset", $"--{mode.ToString().ToLowerInvariant()}", target.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> CherryPickAsync(
        WorkspaceLocation location,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        EnsureText(commitId, nameof(commitId), 512);
        return ReceiptAsync(
            "cherry-pick commit",
            location,
            ["cherry-pick", commitId.Trim()],
            cancellationToken: cancellationToken);
    }

    public Task<GitOperationReceipt> AbortCherryPickAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default) =>
        ReceiptAsync(
            "abort cherry-pick",
            location,
            ["cherry-pick", "--abort"],
            cancellationToken: cancellationToken);

    private async Task<GitOperationReceipt> PathsReceiptAsync(
        string operation,
        WorkspaceLocation location,
        IReadOnlyList<string> prefix,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var repository = await RequireRepositoryAsync(location, cancellationToken);
        var arguments = prefix.ToList();
        arguments.Add("--");
        arguments.AddRange(ValidatePaths(paths));
        return ToReceipt(
            operation,
            await RunRequiredAsync(
                operation,
                Command(repository.Root, arguments),
                cancellationToken));
    }

    private async Task<ConflictStage> ReadConflictStageAsync(
        WorkspaceLocation repository,
        int stage,
        string path,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            Command(
                repository,
                ["show", $":{stage}:{path}"],
                readOnly: true,
                maximumOutput: 8 * 1024 * 1024),
            cancellationToken);
        return result.Succeeded
            ? new ConflictStage(
                true,
                result.StandardOutput,
                result.OutputLimitExceeded)
            : new ConflictStage(false, string.Empty, false);
    }

    private async Task<GitOperationReceipt> ReceiptAsync(
        string operation,
        WorkspaceLocation location,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var repository = arguments.FirstOrDefault() == "init"
            ? location
            : (await RequireRepositoryAsync(location, cancellationToken)).Root;
        var result = await RunRequiredAsync(
            operation,
            Command(repository, arguments, timeout: timeout),
            cancellationToken);
        return ToReceipt(operation, result);
    }

    private async Task<GitRepositoryIdentity> RequireRepositoryAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken)
        => await DiscoverAsync(location, cancellationToken)
           ?? throw new GitRepositoryException(
               "discover repository",
               "The selected workspace is not inside a Git repository.");

    private async Task<GitCommandResult> RunRequiredAsync(
        string operation,
        GitCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(invocation, cancellationToken);
        EnsureSucceeded(operation, result);
        return result;
    }

    private static void EnsureSucceeded(
        string operation,
        GitCommandResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var message = !string.IsNullOrWhiteSpace(result.FailureMessage)
            ? result.FailureMessage
            : !string.IsNullOrWhiteSpace(result.StandardError)
                ? FirstLine(result.StandardError)
                : !string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? FirstLine(result.StandardOutput)
                    : $"Git exited with code {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.";
        throw new GitRepositoryException(operation, message, result.ExitCode);
    }

    private static GitOperationReceipt ToReceipt(
        string operation,
        GitCommandResult result)
    {
        EnsureSucceeded(operation, result);
        var output = string.Join(
            "\n",
            new[] { result.StandardOutput.Trim(), result.StandardError.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return new GitOperationReceipt(
            operation,
            string.IsNullOrWhiteSpace(output)
                ? $"Git completed {operation}."
                : FirstLine(output),
            output);
    }

    private sealed record ConflictStage(
        bool Exists,
        string Content,
        bool Truncated);

    private static GitCommandInvocation Command(
        WorkspaceLocation repository,
        IReadOnlyList<string> arguments,
        bool readOnly = false,
        string standardInput = "",
        TimeSpan? timeout = null,
        int maximumOutput = 8 * 1024 * 1024) =>
        new(
            repository,
            arguments,
            standardInput,
            readOnly,
            timeout,
            maximumOutput);

    private static GitRepositoryStatus ParseStatus(
        GitRepositoryIdentity repository,
        string payload)
    {
        var branch = string.Empty;
        var oid = string.Empty;
        var upstream = string.Empty;
        var ahead = 0;
        var behind = 0;
        var entries = new List<GitWorkingTreeEntry>();
        var records = payload.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index].TrimStart('\r', '\n');
            if (record.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                oid = record[13..].Trim();
                if (oid == "(initial)")
                {
                    oid = string.Empty;
                }
                continue;
            }

            if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = record[14..].Trim();
                if (branch == "(detached)")
                {
                    branch = "(detached)";
                }
                continue;
            }

            if (record.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = record[18..].Trim();
                continue;
            }

            if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var token in record[12..].Split(
                             ' ',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith('+')
                        && int.TryParse(token.AsSpan(1), out var parsedAhead))
                    {
                        ahead = parsedAhead;
                    }
                    else if (token.StartsWith('-')
                             && int.TryParse(token.AsSpan(1), out var parsedBehind))
                    {
                        behind = parsedBehind;
                    }
                }
                continue;
            }

            if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                var fields = record.Split(' ', 9);
                if (fields.Length == 9)
                {
                    entries.Add(Tracked(fields[8], string.Empty, fields[1], fields[2]));
                }
                continue;
            }

            if (record.StartsWith("2 ", StringComparison.Ordinal))
            {
                var fields = record.Split(' ', 10);
                if (fields.Length == 10)
                {
                    var original = index + 1 < records.Length
                        ? records[++index]
                        : string.Empty;
                    entries.Add(Tracked(fields[9], original, fields[1], fields[2]));
                }
                continue;
            }

            if (record.StartsWith("u ", StringComparison.Ordinal))
            {
                var fields = record.Split(' ', 11);
                if (fields.Length == 11)
                {
                    entries.Add(Tracked(
                        fields[10], string.Empty, fields[1], fields[2], conflict: true));
                }
                continue;
            }

            if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                entries.Add(new GitWorkingTreeEntry(
                    record[2..], string.Empty, '?', '?', true, false, false,
                    string.Empty));
                continue;
            }

            if (record.StartsWith("! ", StringComparison.Ordinal))
            {
                entries.Add(new GitWorkingTreeEntry(
                    record[2..], string.Empty, '!', '!', false, true, false,
                    string.Empty));
            }
        }

        return new GitRepositoryStatus(
            repository,
            branch,
            oid,
            upstream,
            ahead,
            behind,
            entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList());
    }

    private static GitWorkingTreeEntry Tracked(
        string path,
        string original,
        string xy,
        string submodule,
        bool conflict = false) =>
        new(
            path,
            original,
            xy.Length > 0 ? xy[0] : '.',
            xy.Length > 1 ? xy[1] : '.',
            false,
            false,
            conflict,
            submodule);

    private static IReadOnlyList<GitBranchInfo> ParseBranches(string payload) =>
        payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\0'))
            .Where(fields => fields.Length >= 4)
            .Select(fields =>
            {
                var fullName = fields[0];
                var remote = fullName.StartsWith(
                    "refs/remotes/",
                    StringComparison.Ordinal);
                var prefix = remote ? "refs/remotes/" : "refs/heads/";
                return new GitBranchInfo(
                    fullName.StartsWith(prefix, StringComparison.Ordinal)
                        ? fullName[prefix.Length..]
                        : fullName,
                    fullName,
                    fields[1],
                    fields[2],
                    fields[3] == "*",
                    remote);
            })
            .ToList();

    private static IReadOnlyList<GitTagInfo> ParseTags(string payload) =>
        payload.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\0'))
            .Where(fields => fields.Length >= 4)
            .Select(fields => new GitTagInfo(
                fields[0],
                string.IsNullOrWhiteSpace(fields[1]) ? fields[2] : fields[1],
                fields[3]))
            .ToList();

    private static IReadOnlyList<GitCommitInfo> ParseCommits(string payload) =>
        payload.Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.TrimStart('\r', '\n').Split('\0'))
            .Where(fields => fields.Length >= 8)
            .Select(fields => new GitCommitInfo(
                fields[0],
                fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                fields[2],
                fields[3],
                DateTimeOffset.TryParse(
                    fields[4],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var authoredAt)
                    ? authoredAt
                    : DateTimeOffset.MinValue,
                fields[5],
                fields[6].TrimEnd('\r', '\n'),
                fields[7]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries
                                | StringSplitOptions.TrimEntries)))
            .ToList();

    private static IReadOnlyList<string> ValidatePaths(
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count is 0 or > MaximumPathsPerOperation)
        {
            throw new GitRepositoryException(
                "validate paths",
                $"Select between 1 and {MaximumPathsPerOperation} paths.");
        }

        var result = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            EnsurePath(path);
            result.Add(path);
        }

        return result;
    }

    private static void EnsureLocation(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (string.IsNullOrWhiteSpace(location.Path))
        {
            throw new GitRepositoryException(
                "validate repository",
                "A repository workspace location is required.");
        }
    }

    private static void EnsurePath(string path)
    {
        EnsureText(path, nameof(path), 4096);
        if (Path.IsPathFullyQualified(path))
        {
            throw new GitRepositoryException(
                "validate path",
                "Git file operations require a repository-relative path.");
        }
    }

    private static void EnsureText(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                $"{parameterName} is empty, malformed, or exceeds its limit.",
                parameterName);
        }
    }

    private static void EnsureConfirmed(string operation, bool confirmed)
    {
        if (!confirmed)
        {
            throw ConfirmationRequired(operation);
        }
    }

    private static GitRepositoryException ConfirmationRequired(
        string operation) =>
        new(operation, $"{operation} requires explicit destructive confirmation.");

    private static string FirstLine(string value) =>
        Lines(value).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<string> Lines(string value) =>
        (value ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToList();
}
