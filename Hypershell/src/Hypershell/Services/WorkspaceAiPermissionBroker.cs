using Hypershell.Models;
using Hypershell.Profiles;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public enum WorkspaceAiCapability
{
    Read,
    Write,
    Delete,
    TerminalRead,
    Terminal,
    Network
}

public enum WorkspaceAiOperationKind
{
    ApproveWorkspace,
    RevokeWorkspace,
    ListDirectory,
    SearchText,
    ReadText,
    ReadOpenDocument,
    ReadTerminalState,
    WriteText,
    ReplaceOpenDocument,
    DeleteText,
    StageTerminal,
    ExecuteTerminal,
    NetworkRequest,
    InspectGit,
    CreateGitRepository,
    MutateGit,
    DestructiveGit
}

public enum WorkspaceAiAuthorizationOutcome
{
    Allowed,
    Denied,
    ConfirmationRequired
}

public sealed record WorkspaceAiWorkspaceDescriptor(
    string WorkspaceId,
    string DisplayName,
    string RootPath,
    bool IsAvailable = true,
    string WorkspaceUri = "");

public sealed record WorkspaceAiAuthorization
{
    public required WorkspaceAiAuthorizationOutcome Outcome { get; init; }
    public required WorkspaceAiCapability Capability { get; init; }
    public required WorkspaceAiOperationKind Operation { get; init; }
    public required string WorkspaceId { get; init; }
    public string RootPath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string WorkspaceUri { get; init; } = string.Empty;
    public bool AllowsCreate { get; init; }
    public bool AllowsEdit { get; init; }
    public required string Message { get; init; }

    public bool IsAllowed => Outcome == WorkspaceAiAuthorizationOutcome.Allowed;
    public bool IsRemote => !string.IsNullOrWhiteSpace(WorkspaceUri);
}

public sealed class WorkspaceAiDeviceState
{
    public int SchemaVersion { get; set; } =
        WorkspaceAiPermissionBroker.CurrentSchemaVersion;
    public List<WorkspaceAiApproval> WorkspaceApprovals { get; set; } = [];
}

public sealed class WorkspaceAiPermissionBroker
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumApprovals = 256;

    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly WorkspaceAiAuditService _audit;
    private readonly WorkspaceAiContextService? _contextService;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = System.Text.Json.Serialization
            .JsonUnmappedMemberHandling.Disallow
    };
    private WorkspaceAiDeviceState _state;

    public WorkspaceAiPermissionBroker(
        string statePath,
        WorkspaceAiAuditService audit,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(audit);
        _statePath = Path.GetFullPath(statePath);
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _state = LoadState();
    }

    public WorkspaceAiPermissionBroker(
        string statePath,
        WorkspaceAiAuditService audit,
        WorkspaceAiContextService contextService,
        TimeProvider? timeProvider = null)
        : this(statePath, audit, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextService);
        _contextService = contextService;
    }

    public string StatePath => _statePath;

    public string LastLoadWarning { get; private set; } = string.Empty;

    public IReadOnlyList<WorkspaceAiApproval> GetApprovals()
    {
        lock (_gate)
        {
            return _state.WorkspaceApprovals
                .Select(CloneApproval)
                .OrderBy(approval => approval.WorkspaceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public WorkspaceAiApproval Approve(
        string workspaceId,
        string rootPath,
        bool allowRead,
        bool allowWrite,
        bool allowTerminal,
        bool allowNetwork = false,
        string? displayName = null,
        bool allowTerminalRead = false,
        WorkspaceAiTerminalExecutionPolicy terminalExecutionPolicy =
            WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
        bool allowAutomaticFileAndEditorChanges = false)
    {
        ValidateWorkspaceId(workspaceId);
        if (!allowRead
            && !allowWrite
            && !allowTerminalRead
            && !allowTerminal
            && !allowNetwork)
        {
            throw new InvalidOperationException(
                "Select at least one capability before approving the workspace.");
        }

        if (!Enum.IsDefined(terminalExecutionPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminalExecutionPolicy),
                "The terminal execution policy is unsupported.");
        }

        var normalizedRoot = NormalizeRoot(rootPath);
        EnsureNoReparseTraversal(normalizedRoot, normalizedRoot);
        var approval = new WorkspaceAiApproval
        {
            WorkspaceId = workspaceId,
            DisplayName = NormalizeDisplayName(displayName, normalizedRoot),
            LocalRootPath = normalizedRoot,
            RootFingerprint = CreateRootFingerprint(normalizedRoot),
            AllowRead = allowRead || allowWrite,
            AllowWrite = allowWrite,
            AllowAutomaticFileAndEditorChanges = allowWrite
                && allowAutomaticFileAndEditorChanges,
            AllowTerminalRead = allowTerminalRead,
            AllowTerminal = allowTerminal,
            TerminalExecutionPolicy = allowTerminal
                ? terminalExecutionPolicy
                : WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            AllowNetwork = allowNetwork,
            ApprovedAtUtc = _timeProvider.GetUtcNow()
        };

        _audit.Record(new WorkspaceAiAuditEntry
        {
            TimestampUtc = _timeProvider.GetUtcNow(),
            WorkspaceId = workspaceId,
            Operation = WorkspaceAiOperationKind.ApproveWorkspace,
            Capability = WorkspaceAiCapability.Read,
            Outcome = WorkspaceAiAuditOutcome.Authorized,
            Target = ".",
            Detail = BuildCapabilitySummary(approval)
        });

        lock (_gate)
        {
            var priorState = _state;
            _state = CloneState(_state);
            _state.WorkspaceApprovals.RemoveAll(existing =>
                string.Equals(
                    existing.WorkspaceId,
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    existing.LocalRootPath,
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase));
            if (_state.WorkspaceApprovals.Count >= MaximumApprovals)
            {
                throw new InvalidOperationException(
                    $"At most {MaximumApprovals} workspace approvals can be retained on one device.");
            }

            _state.WorkspaceApprovals.Add(approval);
            try
            {
                SaveState();
            }
            catch
            {
                _state = priorState;
                throw;
            }
        }

        TryRecord(new WorkspaceAiAuditEntry
        {
            TimestampUtc = _timeProvider.GetUtcNow(),
            WorkspaceId = workspaceId,
            Operation = WorkspaceAiOperationKind.ApproveWorkspace,
            Capability = WorkspaceAiCapability.Read,
            Outcome = WorkspaceAiAuditOutcome.Succeeded,
            Target = ".",
            Detail = BuildCapabilitySummary(approval)
        });
        return CloneApproval(approval);
    }

    public bool Revoke(string workspaceId)
    {
        ValidateWorkspaceId(workspaceId);
        bool removed;
        lock (_gate)
        {
            var priorState = _state;
            _state = CloneState(_state);
            removed = _state.WorkspaceApprovals.RemoveAll(approval =>
                string.Equals(
                    approval.WorkspaceId,
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                try
                {
                    SaveState();
                }
                catch
                {
                    _state = priorState;
                    throw;
                }
            }
        }

        if (removed)
        {
            TryRecord(new WorkspaceAiAuditEntry
            {
                TimestampUtc = _timeProvider.GetUtcNow(),
                WorkspaceId = workspaceId,
                Operation = WorkspaceAiOperationKind.RevokeWorkspace,
                Capability = WorkspaceAiCapability.Read,
                Outcome = WorkspaceAiAuditOutcome.Succeeded,
                Target = ".",
                Detail = "Device-local workspace approval revoked."
            });
        }

        return removed;
    }

    public WorkspaceAiAuthorization AuthorizePath(
        string workspaceId,
        string relativePath,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation,
        bool allowWorkspaceRoot = false)
    {
        if (capability is WorkspaceAiCapability.Terminal
            or WorkspaceAiCapability.Network)
        {
            throw new ArgumentException(
                "Path authorization supports read and write capabilities only.",
                nameof(capability));
        }

        if (WorkspaceLocation.TryParse(relativePath, out var workspaceLocation))
        {
            if (workspaceLocation.IsLocal)
            {
                relativePath = workspaceLocation.Path;
            }
            else
            {
                var remoteContext = _contextService?.GetContext(workspaceId);
                return remoteContext is null
                    ? Denied(
                        workspaceId,
                        capability,
                        operation,
                        "Remote workspace access requires an AI Context with a matching workspace URI scope.")
                    : AuthorizeContextWorkspaceLocation(
                        remoteContext,
                        workspaceLocation,
                        capability,
                        operation,
                        allowWorkspaceRoot);
            }
        }

        var context = _contextService?.GetContext(workspaceId);
        if (context is not null)
        {
            return AuthorizeContextPath(
                context,
                relativePath,
                capability,
                operation,
                allowWorkspaceRoot);
        }

        var approval = FindApproval(workspaceId);
        if (approval is null)
        {
            return Denied(
                workspaceId,
                capability,
                operation,
                "This workspace has no device-local AI approval.");
        }

        if (!HasCapability(approval, capability))
        {
            return Denied(
                workspaceId,
                capability,
                operation,
                $"The workspace approval does not grant {capability.ToString().ToLowerInvariant()} access.");
        }

        if (!TryValidateRoot(approval, out var rootFailure))
        {
            return Denied(
                workspaceId,
                capability,
                operation,
                rootFailure);
        }

        try
        {
            var isAbsolute = Path.IsPathFullyQualified(relativePath);
            var normalizedRelative = isAbsolute
                ? relativePath
                : NormalizeRelativePath(
                    relativePath,
                    allowWorkspaceRoot);
            var fullPath = isAbsolute
                ? Path.GetFullPath(normalizedRelative)
                : normalizedRelative == "."
                    ? approval.LocalRootPath
                    : Path.GetFullPath(Path.Combine(
                        approval.LocalRootPath,
                        normalizedRelative));
            if (!IsWithinRoot(approval.LocalRootPath, fullPath))
            {
                return Denied(
                    workspaceId,
                    capability,
                    operation,
                    "The requested path escapes the approved workspace root.");
            }

            EnsureNoReparseTraversal(approval.LocalRootPath, fullPath);
            var canonicalRelative = string.Equals(
                approval.LocalRootPath,
                fullPath,
                StringComparison.OrdinalIgnoreCase)
                ? "."
                : Path.GetRelativePath(
                    approval.LocalRootPath,
                    fullPath);
            return new WorkspaceAiAuthorization
            {
                Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
                Capability = capability,
                Operation = operation,
                WorkspaceId = approval.WorkspaceId,
                RootPath = approval.LocalRootPath,
                FullPath = fullPath,
                RelativePath = canonicalRelative,
                AllowsCreate = capability == WorkspaceAiCapability.Write,
                AllowsEdit = capability == WorkspaceAiCapability.Write,
                Message = "The operation is inside the approved workspace scope."
            };
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            return Denied(
                workspaceId,
                capability,
                operation,
                $"The requested path is not a safe workspace target: {exception.Message}");
        }
    }

    public WorkspaceAiAuthorization AuthorizeTerminal(
        string workspaceId,
        bool execute,
        bool userConfirmed,
        string? terminalTarget = null)
    {
        var operation = execute
            ? WorkspaceAiOperationKind.ExecuteTerminal
            : WorkspaceAiOperationKind.StageTerminal;
        WorkspaceLocation? terminalLocation = null;
        if (WorkspaceLocation.TryParse(terminalTarget, out var parsedTerminal))
        {
            terminalLocation = parsedTerminal;
        }

        var context = _contextService?.GetContext(workspaceId);
        if (context is not null)
        {
            WorkspaceAiAuthorization scoped;
            if (terminalLocation is { IsSsh: true } remoteTerminal)
            {
                var remoteRoot = FindMatchingWorkspaceRoot(
                    context.Terminal,
                    remoteTerminal);
                if (remoteRoot is null)
                {
                    return Denied(
                        workspaceId,
                        WorkspaceAiCapability.Terminal,
                        operation,
                        "The remote terminal is outside this context's Terminal workspace scope.");
                }

                scoped = AuthorizeUsingWorkspaceUriPermission(
                    context,
                    remoteTerminal,
                    remoteRoot,
                    WorkspaceAiCapability.Terminal,
                    operation,
                    allowWorkspaceRoot: true);
            }
            else
            {
                var requested = string.IsNullOrWhiteSpace(terminalTarget)
                    ? ContextPermissionRoot(context, context.Terminal)
                    : terminalLocation is { IsLocal: true }
                        ? terminalLocation.Path
                        : terminalTarget;
                scoped = AuthorizeUsingContextPermission(
                    context,
                    context.Terminal,
                    requested,
                    WorkspaceAiCapability.Terminal,
                    operation,
                    allowPermissionRoot: true);
            }

            if (!scoped.IsAllowed)
            {
                return scoped;
            }

            if (execute && context.AskBeforeChanges && !userConfirmed)
            {
                return scoped with
                {
                    Outcome = WorkspaceAiAuthorizationOutcome.ConfirmationRequired,
                    Message = "This context asks before terminal commands are executed."
                };
            }

            return scoped with
            {
                Message = execute
                    ? "The selected context authorizes terminal execution."
                    : "The selected context authorizes terminal staging."
            };
        }

        var approval = FindApproval(workspaceId);
        if (approval is null)
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.Terminal,
                operation,
                "This workspace has no device-local AI approval.");
        }

        if (!approval.AllowTerminal)
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.Terminal,
                operation,
                "The workspace approval does not grant terminal access.");
        }

        if (!TryValidateRoot(approval, out var rootFailure))
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.Terminal,
                operation,
                rootFailure);
        }

        if (terminalLocation is { IsSsh: true })
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.Terminal,
                operation,
                "Remote terminal access requires an AI Context with a matching workspace URI scope.");
        }

        var terminalPath = approval.LocalRootPath;
        var terminalRelativePath = ".";
        if (!string.IsNullOrWhiteSpace(terminalTarget))
        {
            try
            {
                terminalPath = Path.GetFullPath(
                    terminalLocation is { IsLocal: true }
                        ? terminalLocation.Path
                        : terminalTarget);
                if (!Directory.Exists(terminalPath)
                    || !IsWithinRoot(approval.LocalRootPath, terminalPath))
                {
                    return Denied(
                        workspaceId,
                        WorkspaceAiCapability.Terminal,
                        operation,
                        "The selected terminal is not rooted inside this approved workspace.");
                }

                EnsureNoReparseTraversal(approval.LocalRootPath, terminalPath);
                terminalRelativePath = string.Equals(
                    approval.LocalRootPath,
                    terminalPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? "."
                    : Path.GetRelativePath(approval.LocalRootPath, terminalPath);
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or IOException
                                               or UnauthorizedAccessException
                                               or NotSupportedException)
            {
                return Denied(
                    workspaceId,
                    WorkspaceAiCapability.Terminal,
                    operation,
                    $"The selected terminal is not a safe workspace target: {exception.Message}");
            }
        }

        if (execute
            && approval.TerminalExecutionPolicy
                == WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand
            && !userConfirmed)
        {
            return new WorkspaceAiAuthorization
            {
                Outcome = WorkspaceAiAuthorizationOutcome.ConfirmationRequired,
                Capability = WorkspaceAiCapability.Terminal,
                Operation = operation,
                WorkspaceId = approval.WorkspaceId,
                RootPath = approval.LocalRootPath,
                FullPath = terminalPath,
                RelativePath = terminalRelativePath,
                Message = "Every AI-requested terminal execution requires a fresh user confirmation."
            };
        }

        return new WorkspaceAiAuthorization
        {
            Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
            Capability = WorkspaceAiCapability.Terminal,
            Operation = operation,
            WorkspaceId = approval.WorkspaceId,
            RootPath = approval.LocalRootPath,
            FullPath = terminalPath,
            RelativePath = terminalRelativePath,
            Message = execute
                ? approval.TerminalExecutionPolicy switch
                {
                    WorkspaceAiTerminalExecutionPolicy.ReviewOnly =>
                        "The reviewed terminal operation is authorized without a second native confirmation.",
                    WorkspaceAiTerminalExecutionPolicy.AutoExecute =>
                        "The standing device-local policy authorizes terminal execution without per-command review.",
                    _ =>
                        "The confirmed terminal operation is authorized. The shell still runs with the Windows user's permissions."
                }
                : "The command may be staged without execution."
        };
    }

    public WorkspaceAiTerminalExecutionPolicy GetTerminalExecutionPolicy(
        string workspaceId)
    {
        var context = _contextService?.GetContext(workspaceId);
        if (context is not null)
        {
            return context.AskBeforeChanges
                ? WorkspaceAiTerminalExecutionPolicy.ReviewOnly
                : WorkspaceAiTerminalExecutionPolicy.AutoExecute;
        }

        var approval = FindApproval(workspaceId);
        return approval is { AllowTerminal: true }
            ? approval.TerminalExecutionPolicy
            : WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand;
    }

    public bool AllowsAutomaticFileAndEditorChanges(string workspaceId)
    {
        var context = _contextService?.GetContext(workspaceId);
        if (context is not null)
        {
            return !context.AskBeforeChanges;
        }

        var approval = FindApproval(workspaceId);
        return approval is
        {
            AllowWrite: true,
            AllowAutomaticFileAndEditorChanges: true
        };
    }

    public WorkspaceAiAuthorization AuthorizeTerminalRead(
        string workspaceId,
        string workingDirectory)
    {
        if (WorkspaceLocation.TryParse(
                workingDirectory,
                out var workspaceLocation)
            && workspaceLocation.IsLocal)
        {
            workingDirectory = workspaceLocation.Path;
        }

        var context = _contextService?.GetContext(workspaceId);
        if (context is not null)
        {
            if (WorkspaceLocation.TryParse(
                    workingDirectory,
                    out var remoteTerminal)
                && remoteTerminal.IsSsh)
            {
                var root = FindMatchingWorkspaceRoot(
                    context.Terminal,
                    remoteTerminal);
                return root is null
                    ? Denied(
                        workspaceId,
                        WorkspaceAiCapability.TerminalRead,
                        WorkspaceAiOperationKind.ReadTerminalState,
                        "The remote terminal is outside this context's Terminal workspace scope.")
                    : AuthorizeUsingWorkspaceUriPermission(
                        context,
                        remoteTerminal,
                        root,
                        WorkspaceAiCapability.TerminalRead,
                        WorkspaceAiOperationKind.ReadTerminalState,
                        allowWorkspaceRoot: true);
            }

            return AuthorizeUsingContextPermission(
                context,
                context.Terminal,
                workingDirectory,
                WorkspaceAiCapability.TerminalRead,
                WorkspaceAiOperationKind.ReadTerminalState,
                allowPermissionRoot: true);
        }

        var approval = FindApproval(workspaceId);
        if (approval is null)
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.TerminalRead,
                WorkspaceAiOperationKind.ReadTerminalState,
                "This workspace has no device-local AI approval.");
        }

        if (!approval.AllowTerminalRead)
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.TerminalRead,
                WorkspaceAiOperationKind.ReadTerminalState,
                "The workspace approval does not grant live terminal-state reads.");
        }

        if (!TryValidateRoot(approval, out var rootFailure))
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.TerminalRead,
                WorkspaceAiOperationKind.ReadTerminalState,
                rootFailure);
        }

        try
        {
            var fullPath = Path.GetFullPath(workingDirectory);
            if (!Directory.Exists(fullPath)
                || !IsWithinRoot(approval.LocalRootPath, fullPath))
            {
                return Denied(
                    workspaceId,
                    WorkspaceAiCapability.TerminalRead,
                    WorkspaceAiOperationKind.ReadTerminalState,
                    "The active terminal is not rooted inside this approved workspace.");
            }

            EnsureNoReparseTraversal(approval.LocalRootPath, fullPath);
            return new WorkspaceAiAuthorization
            {
                Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
                Capability = WorkspaceAiCapability.TerminalRead,
                Operation = WorkspaceAiOperationKind.ReadTerminalState,
                WorkspaceId = approval.WorkspaceId,
                RootPath = approval.LocalRootPath,
                FullPath = fullPath,
                RelativePath = string.Equals(
                    approval.LocalRootPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? "."
                    : Path.GetRelativePath(approval.LocalRootPath, fullPath),
                Message = "The live terminal draft and bounded transcript may be read for this approved root."
            };
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            return Denied(
                workspaceId,
                WorkspaceAiCapability.TerminalRead,
                WorkspaceAiOperationKind.ReadTerminalState,
                $"The active terminal is not a safe workspace target: {exception.Message}");
        }
    }

    public static string CreateWorkspaceId(string prefix, string rootPath)
    {
        var safePrefix = new string((prefix ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-'
                    ? character
                    : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(safePrefix))
        {
            safePrefix = "workspace";
        }

        if (safePrefix.Length > 80)
        {
            safePrefix = safePrefix[..80];
        }

        var normalized = Path.GetFullPath(rootPath.Trim())
            .ToUpperInvariant();
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
        return $"{safePrefix}:{hash[..16]}";
    }

    public static string CreateWorkspaceUriId(
        string prefix,
        string workspaceUri)
    {
        if (!WorkspaceLocation.TryParse(workspaceUri, out var location))
        {
            throw new ArgumentException(
                "The workspace URI is malformed.",
                nameof(workspaceUri));
        }

        var safePrefix = new string((prefix ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-'
                    ? character
                    : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(safePrefix))
        {
            safePrefix = "workspace";
        }

        if (safePrefix.Length > 80)
        {
            safePrefix = safePrefix[..80];
        }

        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(location.Identity)))
            .ToLowerInvariant();
        return $"{safePrefix}:{hash[..16]}";
    }

    public WorkspaceAiAuthorization AuthorizeNetwork(string workspaceId)
    {
        var context = _contextService?.GetContext(workspaceId);
        if (context is not null)
        {
            return context.AllowInternet
                ? new WorkspaceAiAuthorization
                {
                    Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
                    Capability = WorkspaceAiCapability.Network,
                    Operation = WorkspaceAiOperationKind.NetworkRequest,
                    WorkspaceId = context.Id,
                    Message = "The selected context allows direct internet tools."
                }
                : Denied(
                    workspaceId,
                    WorkspaceAiCapability.Network,
                    WorkspaceAiOperationKind.NetworkRequest,
                    "The selected context does not allow direct internet tools.");
        }

        var approval = FindApproval(workspaceId);
        return approval is { AllowNetwork: true }
            ? new WorkspaceAiAuthorization
            {
                Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
                Capability = WorkspaceAiCapability.Network,
                Operation = WorkspaceAiOperationKind.NetworkRequest,
                WorkspaceId = approval.WorkspaceId,
                RootPath = approval.LocalRootPath,
                Message = "The workspace approval allows network access."
            }
            : Denied(
                workspaceId,
                WorkspaceAiCapability.Network,
                WorkspaceAiOperationKind.NetworkRequest,
                "Internet access is disabled for the selected AI context.");
    }

    private WorkspaceAiAuthorization AuthorizeContextPath(
        WorkspaceAiContextDefinition context,
        string requestedPath,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation,
        bool allowWorkspaceRoot)
    {
        if (operation == WorkspaceAiOperationKind.WriteText)
        {
            var editCandidate = AuthorizeUsingContextPermission(
                context,
                context.Edit,
                requestedPath,
                capability,
                operation,
                allowWorkspaceRoot) with
            {
                AllowsEdit = true
            };
            if (editCandidate.IsAllowed && File.Exists(editCandidate.FullPath))
            {
                return editCandidate;
            }

            var createCandidate = AuthorizeUsingContextPermission(
                context,
                context.Create,
                requestedPath,
                capability,
                operation,
                allowWorkspaceRoot) with
            {
                AllowsCreate = true
            };
            if (createCandidate.IsAllowed
                && (File.Exists(createCandidate.FullPath)
                    || Directory.Exists(createCandidate.FullPath)))
            {
                return Denied(
                    context.Id,
                    capability,
                    operation,
                    "The target already exists and is outside this context's Edit scope.");
            }

            return createCandidate;
        }

        var permission = operation switch
        {
            WorkspaceAiOperationKind.ReplaceOpenDocument => context.Edit,
            WorkspaceAiOperationKind.DeleteText => context.Delete,
            WorkspaceAiOperationKind.CreateGitRepository => context.Create,
            WorkspaceAiOperationKind.MutateGit => context.Edit,
            WorkspaceAiOperationKind.DestructiveGit => context.Delete,
            WorkspaceAiOperationKind.ReadTerminalState => context.Terminal,
            _ => context.Read
        };
        return AuthorizeUsingContextPermission(
            context,
            permission,
            requestedPath,
            capability,
            operation,
            allowWorkspaceRoot);
    }

    private static WorkspaceAiAuthorization AuthorizeContextWorkspaceLocation(
        WorkspaceAiContextDefinition context,
        WorkspaceLocation requested,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation,
        bool allowWorkspaceRoot)
    {
        if (operation == WorkspaceAiOperationKind.WriteText)
        {
            var editRoot = FindMatchingWorkspaceRoot(
                context.Edit,
                requested);
            var createRoot = FindMatchingWorkspaceRoot(
                context.Create,
                requested);
            if (editRoot is null && createRoot is null)
            {
                return Denied(
                    context.Id,
                    capability,
                    operation,
                    "The remote target is outside this context's Create and Edit workspace scopes.");
            }

            return AuthorizeUsingWorkspaceUriPermission(
                    context,
                    requested,
                    editRoot ?? createRoot!,
                    capability,
                    operation,
                    allowWorkspaceRoot)
                with
            {
                AllowsCreate = createRoot is not null,
                AllowsEdit = editRoot is not null
            };
        }

        var permission = operation switch
        {
            WorkspaceAiOperationKind.ReplaceOpenDocument => context.Edit,
            WorkspaceAiOperationKind.DeleteText => context.Delete,
            WorkspaceAiOperationKind.CreateGitRepository => context.Create,
            WorkspaceAiOperationKind.MutateGit => context.Edit,
            WorkspaceAiOperationKind.DestructiveGit => context.Delete,
            WorkspaceAiOperationKind.ReadTerminalState => context.Terminal,
            _ => context.Read
        };
        var root = FindMatchingWorkspaceRoot(permission, requested);
        return root is null
            ? Denied(
                context.Id,
                capability,
                operation,
                "The remote target is outside this context's matching workspace scope.")
            : AuthorizeUsingWorkspaceUriPermission(
                context,
                requested,
                root,
                capability,
                operation,
                allowWorkspaceRoot);
    }

    private static WorkspaceAiAuthorization AuthorizeUsingWorkspaceUriPermission(
        WorkspaceAiContextDefinition context,
        WorkspaceLocation requested,
        WorkspaceLocation root,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation,
        bool allowWorkspaceRoot)
    {
        if (context.RequiresRiskWarning
            && !context.RiskWarningAcknowledged)
        {
            return Denied(
                context.Id,
                capability,
                operation,
                "Review and save this Context once on this PC before using its unrestricted permissions.");
        }

        if (!allowWorkspaceRoot
            && WorkspaceLocation.SameIdentity(root, requested))
        {
            return Denied(
                context.Id,
                capability,
                operation,
                "A file or child directory inside the remote workspace is required.");
        }

        return new WorkspaceAiAuthorization
        {
            Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
            Capability = capability,
            Operation = operation,
            WorkspaceId = context.Id,
            RootPath = root.Identity,
            FullPath = requested.Identity,
            RelativePath = requested.Identity,
            WorkspaceUri = requested.Identity,
            Message = "The operation is inside this context's remote workspace scope."
        };
    }

    private static WorkspaceLocation? FindMatchingWorkspaceRoot(
        WorkspaceAiContextPathPermission permission,
        WorkspaceLocation requested)
    {
        if (!requested.IsSsh)
        {
            return null;
        }

        if (permission.Mode == WorkspaceAiContextAccessMode.FullAccess)
        {
            var rootPath = requested.Path.Length >= 2
                           && requested.Path[1] == ':'
                ? requested.Path[..2] + "/"
                : "/";
            return WorkspaceLocation.Ssh(requested.Authority, rootPath);
        }

        return permission.WorkspaceUris
            .Select(value => WorkspaceLocation.TryParse(value, out var root)
                ? root
                : null)
            .Where(root => root is { IsSsh: true }
                           && string.Equals(
                               root.Authority,
                               requested.Authority,
                               StringComparison.OrdinalIgnoreCase)
                           && IsWithinWorkspaceRoot(root, requested))
            .OrderByDescending(root => root!.Path.Length)
            .FirstOrDefault();
    }

    private static bool IsWithinWorkspaceRoot(
        WorkspaceLocation root,
        WorkspaceLocation target)
    {
        var comparison = root.Path.Length >= 2 && root.Path[1] == ':'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(root.Path, target.Path, comparison))
        {
            return true;
        }

        var prefix = root.Path.EndsWith('/')
            ? root.Path
            : root.Path + "/";
        return target.Path.StartsWith(prefix, comparison);
    }

    private static WorkspaceAiAuthorization AuthorizeUsingContextPermission(
        WorkspaceAiContextDefinition context,
        WorkspaceAiContextPathPermission permission,
        string requestedPath,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation,
        bool allowPermissionRoot)
    {
        if (context.RequiresRiskWarning
            && !context.RiskWarningAcknowledged)
        {
            return Denied(
                context.Id,
                capability,
                operation,
                "Review and save this Context once on this PC before using its unrestricted permissions.");
        }

        try
        {
            var value = (requestedPath ?? string.Empty).Trim();
            var root = ContextPermissionRoot(context, permission);
            if (string.IsNullOrWhiteSpace(value) || value == ".")
            {
                if (!allowPermissionRoot)
                {
                    throw new ArgumentException(
                        "A file or directory path is required.");
                }

                if (string.IsNullOrWhiteSpace(root))
                {
                    throw new ArgumentException(
                        "Choose a working directory in the AI conversation before using a relative workspace path.");
                }

                value = root;
            }

            if (!Path.IsPathFullyQualified(value)
                && string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException(
                    "Choose a working directory in the AI conversation before using a relative workspace path.");
            }

            var fullPath = Path.IsPathFullyQualified(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(root, value));
            EnsureLocalDevicePath(fullPath);
            if (permission.Mode == WorkspaceAiContextAccessMode.ChooseLocation
                && !IsWithinRoot(root, fullPath))
            {
                return Denied(
                    context.Id,
                    capability,
                    operation,
                    "The requested path is outside this context's chosen location.");
            }

            var traversalRoot = permission.Mode
                                == WorkspaceAiContextAccessMode.ChooseLocation
                ? root
                : Path.GetPathRoot(fullPath)
                  ?? throw new IOException("The target has no local drive root.");
            EnsureNoReparseTraversal(traversalRoot, fullPath);
            return new WorkspaceAiAuthorization
            {
                Outcome = WorkspaceAiAuthorizationOutcome.Allowed,
                Capability = capability,
                Operation = operation,
                WorkspaceId = context.Id,
                RootPath = root,
                FullPath = fullPath,
                // Context tools accept absolute paths so independent action
                // scopes cannot accidentally remap one another.
                RelativePath = fullPath,
                Message = permission.Mode == WorkspaceAiContextAccessMode.FullAccess
                    ? "The operation is allowed by this context's full-device scope."
                    : "The operation is inside this context's chosen location."
            };
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            return Denied(
                context.Id,
                capability,
                operation,
                $"The requested path is not allowed by this context: {exception.Message}");
        }
    }

    private static string ContextPermissionRoot(
        WorkspaceAiContextDefinition context,
        WorkspaceAiContextPathPermission permission) =>
        permission.Mode == WorkspaceAiContextAccessMode.ChooseLocation
            ? permission.Location
            : context.DefaultSaveDirectory;

    private static bool TryValidateContextRoot(
        string root,
        out string failure)
    {
        try
        {
            EnsureLocalDevicePath(root);
            if (!Directory.Exists(root))
            {
                failure = "The context's terminal location is unavailable.";
                return false;
            }

            EnsureNoReparseTraversal(
                Path.GetPathRoot(root) ?? root,
                root);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            failure = $"The context's terminal location is unavailable: {exception.Message}";
            return false;
        }
    }

    private static void EnsureLocalDevicePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)
            || root.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Only paths on local device drives are supported.");
        }

        var tail = fullPath[root.Length..];
        if (tail.Contains(':'))
        {
            throw new NotSupportedException(
                "Alternate data streams are not supported.");
        }

        var drive = new DriveInfo(root);
        if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
        {
            throw new NotSupportedException(
                "Only ready local fixed drives are included in Full Access.");
        }
    }

    private WorkspaceAiApproval? FindApproval(string workspaceId)
    {
        if (!IsValidWorkspaceId(workspaceId))
        {
            return null;
        }

        lock (_gate)
        {
            var approval = _state.WorkspaceApprovals.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.WorkspaceId,
                    workspaceId,
                    StringComparison.OrdinalIgnoreCase));
            return approval is null ? null : CloneApproval(approval);
        }
    }

    private bool TryValidateRoot(
        WorkspaceAiApproval approval,
        out string failure)
    {
        try
        {
            if (!Directory.Exists(approval.LocalRootPath))
            {
                failure = "The approved workspace root is unavailable on this device.";
                return false;
            }

            EnsureNoReparseTraversal(
                approval.LocalRootPath,
                approval.LocalRootPath);
            if (!string.Equals(
                    CreateRootFingerprint(approval.LocalRootPath),
                    approval.RootFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                failure = "The workspace root identity changed after approval. Review and approve it again.";
                return false;
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            failure = $"The approved workspace root is no longer safe to use: {exception.Message}";
            return false;
        }
    }

    private WorkspaceAiDeviceState LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return new WorkspaceAiDeviceState();
            }

            var info = new FileInfo(_statePath);
            if (info.Length is <= 0 or > 1024 * 1024)
            {
                throw new InvalidDataException(
                    "The AI permission file has an invalid size.");
            }

            var state = JsonSerializer.Deserialize<WorkspaceAiDeviceState>(
                            File.ReadAllText(_statePath),
                            _jsonOptions)
                        ?? throw new JsonException(
                            "The AI permission file was empty.");
            if (state.SchemaVersion != CurrentSchemaVersion)
            {
                throw new JsonException(
                    $"AI permission schema {state.SchemaVersion} is unsupported.");
            }

            state.WorkspaceApprovals ??= [];
            var valid = state.WorkspaceApprovals
                .Where(IsStoredApprovalValid)
                .GroupBy(
                    approval => approval.WorkspaceId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Take(MaximumApprovals)
                .ToList();
            if (valid.Count != state.WorkspaceApprovals.Count)
            {
                LastLoadWarning =
                    "One or more malformed or duplicate AI workspace approvals were ignored.";
            }

            state.WorkspaceApprovals = valid;
            return state;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException
                                         or InvalidDataException
                                         or NotSupportedException)
        {
            LastLoadWarning =
                $"AI workspace approvals could not be loaded and were disabled: {exception.Message}";
            return new WorkspaceAiDeviceState();
        }
    }

    private void SaveState()
    {
        var directory = Path.GetDirectoryName(_statePath)
                        ?? throw new InvalidOperationException(
                            "The AI permission path has no parent directory.");
        Directory.CreateDirectory(directory);
        _state.SchemaVersion = CurrentSchemaVersion;
        var temporaryPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                _state,
                _jsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsStoredApprovalValid(WorkspaceAiApproval? approval) =>
        approval is not null
        && IsValidWorkspaceId(approval.WorkspaceId)
        && !string.IsNullOrWhiteSpace(approval.LocalRootPath)
        && Path.IsPathFullyQualified(approval.LocalRootPath)
        && approval.RootFingerprint is { Length: 64 }
        && approval.RootFingerprint.All(Uri.IsHexDigit)
        && Enum.IsDefined(approval.TerminalExecutionPolicy)
        && (!approval.AllowAutomaticFileAndEditorChanges
            || approval.AllowWrite)
        && (approval.AllowRead
            || approval.AllowWrite
            || approval.AllowTerminalRead
            || approval.AllowTerminal
            || approval.AllowNetwork);

    private static string NormalizeRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootPath.Trim()));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                "The workspace root does not exist on this device.");
        }

        return fullPath;
    }

    private static string NormalizeDisplayName(
        string? displayName,
        string normalizedRoot)
    {
        var value = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = normalizedRoot;
        }

        if (value.Length > 96
            || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "AI root labels must contain 1-96 printable characters.",
                nameof(displayName));
        }

        return value;
    }

    private static string NormalizeRelativePath(
        string relativePath,
        bool allowWorkspaceRoot)
    {
        var value = (relativePath ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value) || value == ".")
        {
            if (allowWorkspaceRoot)
            {
                return ".";
            }

            throw new ArgumentException(
                "A relative path inside the workspace is required.");
        }

        if (Path.IsPathFullyQualified(value)
            || Path.IsPathRooted(value)
            || value.Contains(':'))
        {
            throw new ArgumentException(
                "Absolute paths and alternate data streams are not accepted by workspace AI operations.");
        }

        return value;
    }

    private static bool IsWithinRoot(string rootPath, string fullPath)
    {
        if (string.Equals(
                rootPath,
                fullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(rootPath)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparseTraversal(
        string rootPath,
        string fullPath)
    {
        RejectReparsePoint(rootPath);
        var relative = Path.GetRelativePath(rootPath, fullPath);
        if (relative == ".")
        {
            return;
        }

        var current = rootPath;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }

            RejectReparsePoint(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "Symbolic links, junctions, and other reparse points are outside the approved workspace boundary.");
        }
    }

    private static string CreateRootFingerprint(string rootPath)
    {
        var directory = new DirectoryInfo(rootPath);
        directory.Refresh();
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                "The workspace root is unavailable.");
        }

        var material = string.Join(
            "\n",
            directory.FullName.ToUpperInvariant(),
            directory.CreationTimeUtc.Ticks.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            (Path.GetPathRoot(directory.FullName) ?? string.Empty)
                .ToUpperInvariant());
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }

    private static bool HasCapability(
        WorkspaceAiApproval approval,
        WorkspaceAiCapability capability) =>
        capability switch
        {
            WorkspaceAiCapability.Read => approval.AllowRead,
            WorkspaceAiCapability.Write => approval.AllowWrite,
            WorkspaceAiCapability.Delete => false,
            WorkspaceAiCapability.TerminalRead => approval.AllowTerminalRead,
            WorkspaceAiCapability.Terminal => approval.AllowTerminal,
            WorkspaceAiCapability.Network => approval.AllowNetwork,
            _ => false
        };

    private static WorkspaceAiAuthorization Denied(
        string workspaceId,
        WorkspaceAiCapability capability,
        WorkspaceAiOperationKind operation,
        string message) =>
        new()
        {
            Outcome = WorkspaceAiAuthorizationOutcome.Denied,
            Capability = capability,
            Operation = operation,
            WorkspaceId = workspaceId ?? string.Empty,
            Message = message
        };

    private static void ValidateWorkspaceId(string workspaceId)
    {
        if (!IsValidWorkspaceId(workspaceId))
        {
            throw new ArgumentException(
                "Workspace IDs must contain 1-160 letters, digits, periods, underscores, colons, or hyphens.",
                nameof(workspaceId));
        }
    }

    private static bool IsValidWorkspaceId(string? workspaceId) =>
        !string.IsNullOrWhiteSpace(workspaceId)
        && workspaceId.Length <= 160
        && workspaceId.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or ':' or '-');

    private static WorkspaceAiApproval CloneApproval(
        WorkspaceAiApproval approval) =>
        new()
        {
            WorkspaceId = approval.WorkspaceId,
            DisplayName = approval.DisplayName,
            LocalRootPath = approval.LocalRootPath,
            RootFingerprint = approval.RootFingerprint,
            AllowRead = approval.AllowRead,
            AllowWrite = approval.AllowWrite,
            AllowAutomaticFileAndEditorChanges =
                approval.AllowAutomaticFileAndEditorChanges,
            AllowTerminalRead = approval.AllowTerminalRead,
            AllowTerminal = approval.AllowTerminal,
            TerminalExecutionPolicy = approval.TerminalExecutionPolicy,
            AllowNetwork = approval.AllowNetwork,
            ApprovedAtUtc = approval.ApprovedAtUtc
        };

    private static WorkspaceAiDeviceState CloneState(
        WorkspaceAiDeviceState state) =>
        new()
        {
            SchemaVersion = state.SchemaVersion,
            WorkspaceApprovals = state.WorkspaceApprovals
                .Select(CloneApproval)
                .ToList()
        };

    private void TryRecord(WorkspaceAiAuditEntry entry)
    {
        try
        {
            _audit.Record(entry);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException
                                         or JsonException)
        {
        }
    }

    private static string BuildCapabilitySummary(
        WorkspaceAiApproval approval)
    {
        var capabilities = new List<string>();
        if (approval.AllowRead)
        {
            capabilities.Add("read");
        }

        if (approval.AllowWrite)
        {
            capabilities.Add(
                approval.AllowAutomaticFileAndEditorChanges
                    ? "write-auto-apply"
                    : "write-reviewed");
        }

        if (approval.AllowTerminalRead)
        {
            capabilities.Add("terminal-read");
        }

        if (approval.AllowTerminal)
        {
            capabilities.Add(approval.TerminalExecutionPolicy switch
            {
                WorkspaceAiTerminalExecutionPolicy.ReviewOnly =>
                    "terminal-review-only",
                WorkspaceAiTerminalExecutionPolicy.AutoExecute =>
                    "terminal-auto-execute",
                _ => "terminal-confirm-every-command"
            });
        }

        if (approval.AllowNetwork)
        {
            capabilities.Add("network");
        }

        return $"Approved capabilities: {string.Join(", ", capabilities)}.";
    }
}
