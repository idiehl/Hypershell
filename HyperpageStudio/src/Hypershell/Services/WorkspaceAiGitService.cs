using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public enum WorkspaceAiGitInspectionKind
{
    Status,
    Diff,
    History,
    Commit,
    CommitPatch,
    Conflict,
    Branches,
    Tags,
    Remotes,
    Stashes
}

public enum WorkspaceAiGitMutationKind
{
    Initialize,
    Clone,
    Stage,
    Unstage,
    ApplyPatch,
    Commit,
    Checkout,
    CreateBranch,
    RenameBranch,
    DeleteBranch,
    CreateTag,
    DeleteTag,
    AddRemote,
    SetRemoteUrl,
    RemoveRemote,
    Fetch,
    Pull,
    Push,
    ForcePush,
    CreateStash,
    ApplyStash,
    DropStash,
    Merge,
    ContinueMerge,
    AbortMerge,
    Rebase,
    ContinueRebase,
    SkipRebase,
    AbortRebase,
    Revert,
    Reset,
    CherryPick,
    AbortCherryPick
}

public sealed record WorkspaceAiGitInspectionSnapshot
{
    public required WorkspaceAiGitInspectionKind Kind { get; init; }
    public required string WorkspaceUri { get; init; }
    public GitRepositoryStatus? Status { get; init; }
    public GitDiffDocument? Diff { get; init; }
    public GitConflictDocument? Conflict { get; init; }
    public GitCommitInfo? Commit { get; init; }
    public IReadOnlyList<GitCommitInfo> History { get; init; } = [];
    public IReadOnlyList<GitBranchInfo> Branches { get; init; } = [];
    public IReadOnlyList<GitTagInfo> Tags { get; init; } = [];
    public IReadOnlyList<GitRemoteInfo> Remotes { get; init; } = [];
    public IReadOnlyList<GitStashInfo> Stashes { get; init; } = [];
}

public sealed record WorkspaceAiGitMutationRequest
{
    public required WorkspaceAiGitMutationKind Kind { get; init; }
    public required string WorkspaceIdentity { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];
    public string Reference { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Remote { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Patch { get; init; } = string.Empty;
    public bool Flag { get; init; }
    public GitResetMode ResetMode { get; init; } = GitResetMode.Mixed;
}

public sealed record WorkspaceAiGitMutationPlan(
    WorkspaceLocation Location,
    WorkspaceAiGitMutationRequest Request,
    WorkspaceAiAuthorization Authorization,
    bool RequiresNetwork,
    bool IsDestructive);

/// <summary>
/// Provides semantic, provider-neutral Git operations to the AI host. The AI
/// never receives an arbitrary Git command surface: every operation is
/// validated, authorized against the active Context, routed through the same
/// local/SSH repository adapter as the Source Control panel, and audited
/// without recording patch contents, commit messages, or credentials.
/// </summary>
public sealed class WorkspaceAiGitService
{
    public const int MaximumPaths = 256;
    public const int MaximumPatchCharacters = 256 * 1024;

    private readonly IGitRepositoryService _git;
    private readonly WorkspaceAiPermissionBroker _broker;
    private readonly WorkspaceAiAuditService _audit;
    private readonly TimeProvider _timeProvider;

    public WorkspaceAiGitService(
        IGitRepositoryService git,
        WorkspaceAiPermissionBroker broker,
        WorkspaceAiAuditService audit,
        TimeProvider? timeProvider = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiGitInspectionSnapshot>>
        InspectAsync(
            string workspaceId,
            string workspaceIdentity,
            WorkspaceAiGitInspectionKind kind,
            string path = "",
            string reference = "",
            GitDiffTarget diffTarget = GitDiffTarget.WorkingTree,
            int maximum = 50,
            string conversationId = "",
            CancellationToken cancellationToken = default)
    {
        var authorization = AuthorizeLocation(
            workspaceId,
            workspaceIdentity,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.InspectGit);
        if (!authorization.IsAllowed)
        {
            Record(
                authorization,
                null,
                conversationId,
                WorkspaceAiAuditOutcome.Denied,
                authorization.Message);
            return Denied<WorkspaceAiGitInspectionSnapshot>(
                authorization.Message);
        }

        var location = AuthorizedLocation(authorization);
        try
        {
            maximum = Math.Clamp(maximum, 1, 200);
            var snapshot = new WorkspaceAiGitInspectionSnapshot
            {
                Kind = kind,
                WorkspaceUri = location.Identity,
                Status = kind == WorkspaceAiGitInspectionKind.Status
                    ? await _git.GetStatusAsync(location, cancellationToken)
                    : null,
                Diff = kind switch
                {
                    WorkspaceAiGitInspectionKind.Diff =>
                        await _git.GetDiffAsync(
                            location,
                            Required(path, nameof(path), 4096),
                            diffTarget,
                            reference,
                            cancellationToken),
                    WorkspaceAiGitInspectionKind.CommitPatch =>
                        await _git.GetCommitPatchAsync(
                            location,
                            Required(reference, nameof(reference), 512),
                            cancellationToken),
                    _ => null
                },
                Conflict = kind == WorkspaceAiGitInspectionKind.Conflict
                    ? await _git.GetConflictAsync(
                        location,
                        Required(path, nameof(path), 4096),
                        cancellationToken)
                    : null,
                Commit = kind == WorkspaceAiGitInspectionKind.Commit
                    ? await _git.GetCommitAsync(
                        location,
                        Required(reference, nameof(reference), 512),
                        cancellationToken)
                    : null,
                History = kind == WorkspaceAiGitInspectionKind.History
                    ? await _git.GetHistoryAsync(
                        location,
                        maximum,
                        path,
                        cancellationToken)
                    : [],
                Branches = kind == WorkspaceAiGitInspectionKind.Branches
                    ? await _git.ListBranchesAsync(location, cancellationToken)
                    : [],
                Tags = kind == WorkspaceAiGitInspectionKind.Tags
                    ? await _git.ListTagsAsync(location, cancellationToken)
                    : [],
                Remotes = kind == WorkspaceAiGitInspectionKind.Remotes
                    ? await _git.ListRemotesAsync(location, cancellationToken)
                    : [],
                Stashes = kind == WorkspaceAiGitInspectionKind.Stashes
                    ? await _git.ListStashesAsync(location, cancellationToken)
                    : []
            };
            Record(
                authorization,
                location,
                conversationId,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Git {kind} inspection completed.");
            return Succeeded(
                snapshot,
                $"Git {kind} inspection completed.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Record(
                authorization,
                location,
                conversationId,
                WorkspaceAiAuditOutcome.Failed,
                $"Git {kind} inspection failed: {exception.Message}");
            return Failed<WorkspaceAiGitInspectionSnapshot>(exception.Message);
        }
    }

    public WorkspaceAiOperationResult<WorkspaceAiGitMutationPlan>
        PrepareMutation(
            string workspaceId,
            WorkspaceAiGitMutationRequest request,
            string conversationId = "")
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            ValidateRequest(request);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or InvalidOperationException)
        {
            return Failed<WorkspaceAiGitMutationPlan>(exception.Message);
        }

        var destructive = IsDestructive(request.Kind);
        var operation = request.Kind is WorkspaceAiGitMutationKind.Clone
            or WorkspaceAiGitMutationKind.Initialize
            ? WorkspaceAiOperationKind.CreateGitRepository
            : destructive
                ? WorkspaceAiOperationKind.DestructiveGit
                : WorkspaceAiOperationKind.MutateGit;
        var capability = destructive
            ? WorkspaceAiCapability.Delete
            : WorkspaceAiCapability.Write;
        var authorization = AuthorizeLocation(
            workspaceId,
            request.WorkspaceIdentity,
            capability,
            operation);
        if (!authorization.IsAllowed)
        {
            Record(
                authorization,
                null,
                conversationId,
                WorkspaceAiAuditOutcome.Denied,
                authorization.Message);
            return Denied<WorkspaceAiGitMutationPlan>(authorization.Message);
        }

        var location = AuthorizedLocation(authorization);
        var requiresNetwork = RequiresNetwork(request);
        if (requiresNetwork)
        {
            var network = _broker.AuthorizeNetwork(workspaceId);
            if (!network.IsAllowed)
            {
                Record(
                    network,
                    location,
                    conversationId,
                    WorkspaceAiAuditOutcome.Denied,
                    network.Message);
                return Denied<WorkspaceAiGitMutationPlan>(network.Message);
            }
        }

        return Succeeded(
            new WorkspaceAiGitMutationPlan(
                location,
                request,
                authorization,
                requiresNetwork,
                destructive),
            "The Git operation is allowed by the selected Context.");
    }

    public void RecordPendingReview(
        WorkspaceAiGitMutationPlan plan,
        string conversationId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Record(
            plan.Authorization,
            plan.Location,
            conversationId,
            WorkspaceAiAuditOutcome.ConfirmationRequired,
            $"Git {plan.Request.Kind} is waiting for explicit review.");
    }

    public void RecordRejected(
        WorkspaceAiGitMutationRequest request,
        string workspaceId,
        string conversationId)
    {
        var prepared = PrepareMutation(workspaceId, request, conversationId);
        if (prepared.Value is not { } plan)
        {
            return;
        }

        Record(
            plan.Authorization,
            plan.Location,
            conversationId,
            WorkspaceAiAuditOutcome.Denied,
            $"The user rejected Git {request.Kind}.");
    }

    public async Task<WorkspaceAiOperationResult<GitOperationReceipt>>
        ExecuteMutationAsync(
            string workspaceId,
            WorkspaceAiGitMutationRequest request,
            string conversationId = "",
            CancellationToken cancellationToken = default)
    {
        var prepared = PrepareMutation(workspaceId, request, conversationId);
        if (!prepared.Succeeded || prepared.Value is not { } plan)
        {
            return new WorkspaceAiOperationResult<GitOperationReceipt>
            {
                Status = prepared.Status,
                Message = prepared.Message
            };
        }

        try
        {
            var receipt = await ExecuteCoreAsync(
                plan.Location,
                request,
                cancellationToken);
            Record(
                plan.Authorization,
                plan.Location,
                conversationId,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Git {request.Kind} completed.");
            return Succeeded(receipt, receipt.Message);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Record(
                plan.Authorization,
                plan.Location,
                conversationId,
                WorkspaceAiAuditOutcome.Failed,
                $"Git {request.Kind} failed: {exception.Message}");
            return Failed<GitOperationReceipt>(exception.Message);
        }
    }

    private Task<GitOperationReceipt> ExecuteCoreAsync(
        WorkspaceLocation location,
        WorkspaceAiGitMutationRequest request,
        CancellationToken cancellationToken) => request.Kind switch
    {
        WorkspaceAiGitMutationKind.Initialize => _git.InitializeAsync(
            location,
            request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.Clone => _git.CloneAsync(
            Required(request.Url, nameof(request.Url), 4096),
            location,
            request.Branch,
            cancellationToken),
        WorkspaceAiGitMutationKind.Stage => _git.StageAsync(
            location,
            RequiredPaths(request.Paths),
            cancellationToken),
        WorkspaceAiGitMutationKind.Unstage => _git.UnstageAsync(
            location,
            RequiredPaths(request.Paths),
            cancellationToken),
        WorkspaceAiGitMutationKind.ApplyPatch => _git.ApplyPatchToIndexAsync(
            location,
            Required(request.Patch, nameof(request.Patch), MaximumPatchCharacters,
                allowLineBreaks: true,
                trim: false),
            request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.Commit => _git.CommitAsync(
            location,
            Required(request.Message, nameof(request.Message), 1024 * 1024,
                allowLineBreaks: true),
            request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.Checkout => _git.CheckoutAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            cancellationToken),
        WorkspaceAiGitMutationKind.CreateBranch => _git.CreateBranchAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            Default(request.Target, "HEAD"),
            checkout: request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.RenameBranch => _git.RenameBranchAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            Required(request.NewName, nameof(request.NewName), 512),
            cancellationToken),
        WorkspaceAiGitMutationKind.DeleteBranch => _git.DeleteBranchAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            request.Flag,
            destructiveConfirmed: true,
            cancellationToken),
        WorkspaceAiGitMutationKind.CreateTag => _git.CreateTagAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            Default(request.Target, "HEAD"),
            request.Message,
            cancellationToken),
        WorkspaceAiGitMutationKind.DeleteTag => _git.DeleteTagAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            destructiveConfirmed: true,
            cancellationToken),
        WorkspaceAiGitMutationKind.AddRemote => _git.AddRemoteAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            Required(request.Url, nameof(request.Url), 4096),
            cancellationToken),
        WorkspaceAiGitMutationKind.SetRemoteUrl => _git.SetRemoteUrlAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            Required(request.Url, nameof(request.Url), 4096),
            request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.RemoveRemote => _git.RemoveRemoteAsync(
            location,
            Required(request.Name, nameof(request.Name), 512),
            destructiveConfirmed: true,
            cancellationToken),
        WorkspaceAiGitMutationKind.Fetch => _git.FetchAsync(
            location,
            Default(request.Remote, "--all"),
            prune: request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.Pull => _git.PullAsync(
            location,
            request.Remote,
            request.Branch,
            rebase: request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.Push => _git.PushAsync(
            location,
            Default(request.Remote, "origin"),
            request.Branch,
            setUpstream: request.Flag,
            cancellationToken: cancellationToken),
        WorkspaceAiGitMutationKind.ForcePush => _git.PushAsync(
            location,
            Default(request.Remote, "origin"),
            Required(request.Branch, nameof(request.Branch), 512),
            forceWithLease: true,
            destructiveConfirmed: true,
            cancellationToken: cancellationToken),
        WorkspaceAiGitMutationKind.CreateStash => _git.CreateStashAsync(
            location,
            request.Message,
            includeUntracked: request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.ApplyStash => _git.ApplyStashAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            pop: request.Flag,
            cancellationToken),
        WorkspaceAiGitMutationKind.DropStash => _git.DropStashAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            destructiveConfirmed: true,
            cancellationToken),
        WorkspaceAiGitMutationKind.Merge => _git.MergeAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            cancellationToken),
        WorkspaceAiGitMutationKind.ContinueMerge =>
            _git.ContinueMergeAsync(location, cancellationToken),
        WorkspaceAiGitMutationKind.AbortMerge =>
            _git.AbortMergeAsync(location, cancellationToken),
        WorkspaceAiGitMutationKind.Rebase => _git.StartRebaseAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            cancellationToken),
        WorkspaceAiGitMutationKind.ContinueRebase => _git.ContinueRebaseAsync(
            location,
            GitRebaseAction.Continue,
            cancellationToken),
        WorkspaceAiGitMutationKind.SkipRebase => _git.ContinueRebaseAsync(
            location,
            GitRebaseAction.Skip,
            cancellationToken),
        WorkspaceAiGitMutationKind.AbortRebase => _git.ContinueRebaseAsync(
            location,
            GitRebaseAction.Abort,
            cancellationToken),
        WorkspaceAiGitMutationKind.Revert => _git.RevertAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            cancellationToken),
        WorkspaceAiGitMutationKind.Reset => _git.ResetAsync(
            location,
            Required(request.Target, nameof(request.Target), 512),
            request.ResetMode,
            destructiveConfirmed: true,
            cancellationToken),
        WorkspaceAiGitMutationKind.CherryPick => _git.CherryPickAsync(
            location,
            Required(request.Reference, nameof(request.Reference), 512),
            cancellationToken),
        WorkspaceAiGitMutationKind.AbortCherryPick =>
            _git.AbortCherryPickAsync(location, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(
            nameof(request),
            request.Kind,
            "The requested Git mutation is unsupported.")
    };

    private WorkspaceAiAuthorization AuthorizeLocation(
        string workspaceId,
        string workspaceIdentity,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation) =>
        _broker.AuthorizePath(
            workspaceId,
            Required(
                workspaceIdentity,
                nameof(workspaceIdentity),
                2048),
            capability,
            operation,
            allowWorkspaceRoot: true);

    private static WorkspaceLocation AuthorizedLocation(
        WorkspaceAiAuthorization authorization)
    {
        var identity = authorization.IsRemote
            ? authorization.WorkspaceUri
            : authorization.FullPath;
        if (!WorkspaceLocation.TryParse(identity, out var location))
        {
            location = WorkspaceLocation.Local(identity);
        }

        return location;
    }

    private static void ValidateRequest(WorkspaceAiGitMutationRequest request)
    {
        _ = Required(
            request.WorkspaceIdentity,
            nameof(request.WorkspaceIdentity),
            2048);
        if (request.Paths.Count > MaximumPaths)
        {
            throw new ArgumentException(
                $"A Git operation accepts at most {MaximumPaths} paths.");
        }

        foreach (var path in request.Paths)
        {
            _ = Required(path, "path", 4096);
        }

        if (request.Patch.Length > MaximumPatchCharacters)
        {
            throw new ArgumentException(
                "The proposed Git patch exceeds the 256 KiB limit.");
        }

        if (request.Kind is WorkspaceAiGitMutationKind.Clone
            or WorkspaceAiGitMutationKind.AddRemote
            or WorkspaceAiGitMutationKind.SetRemoteUrl)
        {
            GitRemoteCredentialPolicy.EnsureCredentialFree(
                Required(request.Url, nameof(request.Url), 4096));
        }
    }

    private static IReadOnlyList<string> RequiredPaths(
        IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            throw new ArgumentException(
                "At least one repository-relative path is required.");
        }

        return paths;
    }

    private static bool RequiresNetwork(WorkspaceAiGitMutationRequest request) =>
        request.Kind is WorkspaceAiGitMutationKind.Fetch
            or WorkspaceAiGitMutationKind.Pull
            or WorkspaceAiGitMutationKind.Push
            or WorkspaceAiGitMutationKind.ForcePush
        || request.Kind == WorkspaceAiGitMutationKind.Clone
            && !LooksLikeLocalCloneSource(request.Url);

    private static bool LooksLikeLocalCloneSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (WorkspaceLocation.TryParse(source, out var workspace))
        {
            return workspace.IsLocal;
        }

        return Path.IsPathFullyQualified(source);
    }

    private static bool IsDestructive(WorkspaceAiGitMutationKind kind) =>
        kind is WorkspaceAiGitMutationKind.DeleteBranch
            or WorkspaceAiGitMutationKind.DeleteTag
            or WorkspaceAiGitMutationKind.RemoveRemote
            or WorkspaceAiGitMutationKind.ForcePush
            or WorkspaceAiGitMutationKind.DropStash
            or WorkspaceAiGitMutationKind.AbortMerge
            or WorkspaceAiGitMutationKind.SkipRebase
            or WorkspaceAiGitMutationKind.AbortRebase
            or WorkspaceAiGitMutationKind.Reset
            or WorkspaceAiGitMutationKind.AbortCherryPick;

    private void Record(
        WorkspaceAiAuthorization authorization,
        WorkspaceLocation? location,
        string conversationId,
        WorkspaceAiAuditOutcome outcome,
        string detail)
    {
        try
        {
            _audit.Record(new WorkspaceAiAuditEntry
            {
                TimestampUtc = _timeProvider.GetUtcNow(),
                WorkspaceId = authorization.WorkspaceId,
                ConversationId = conversationId ?? string.Empty,
                RemoteHostId = location is { IsSsh: true }
                    ? location.Authority
                    : string.Empty,
                WorkspaceUri = location?.Identity
                               ?? authorization.WorkspaceUri,
                Operation = authorization.Operation,
                Capability = authorization.Capability,
                Target = location?.Identity
                         ?? authorization.RelativePath,
                Outcome = outcome,
                Detail = detail
            });
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException)
        {
        }
    }

    private static string Required(
        string value,
        string parameterName,
        int maximumLength,
        bool allowLineBreaks = false,
        bool trim = true)
    {
        value ??= string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.IndexOf('\0') >= 0
            || value.Any(character => char.IsControl(character)
                && (!allowLineBreaks
                    || character is not '\r' and not '\n' and not '\t')))
        {
            throw new ArgumentException(
                $"{parameterName} is missing, malformed, or exceeds its limit.",
                parameterName);
        }

        return trim ? value.Trim() : value;
    }

    private static string Default(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsExpected(Exception exception) =>
        exception is GitRepositoryException
            or ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException;

    private static WorkspaceAiOperationResult<T> Succeeded<T>(
        T value,
        string message) => new()
    {
        Status = WorkspaceAiOperationStatus.Succeeded,
        Message = message,
        Value = value
    };

    private static WorkspaceAiOperationResult<T> Denied<T>(string message) =>
        new()
        {
            Status = WorkspaceAiOperationStatus.Denied,
            Message = message
        };

    private static WorkspaceAiOperationResult<T> Failed<T>(string message) =>
        new()
        {
            Status = WorkspaceAiOperationStatus.Failed,
            Message = message
        };
}
