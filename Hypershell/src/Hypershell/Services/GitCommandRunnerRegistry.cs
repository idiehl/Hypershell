using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>
/// Routes a Git operation to the execution surface that owns the workspace
/// URI. Local paths remain on the installed Git CLI; each linked SSH host has
/// an independently replaceable runner backed by its pinned host identity and
/// DPAPI-protected credential.
/// </summary>
public sealed class GitCommandRunnerRegistry : IGitCommandRunner
{
    private readonly object _gate = new();
    private readonly IGitCommandRunner _localRunner;
    private readonly Dictionary<string, IGitCommandRunner> _sshRunners =
        new(StringComparer.OrdinalIgnoreCase);

    public GitCommandRunnerRegistry(IGitCommandRunner localRunner)
    {
        _localRunner = localRunner
            ?? throw new ArgumentNullException(nameof(localRunner));
    }

    public string RunnerId => "git.workspace-router";

    public bool CanHandle(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.IsLocal)
        {
            return _localRunner.CanHandle(location);
        }

        lock (_gate)
        {
            return location.IsSsh
                   && _sshRunners.TryGetValue(
                       location.Authority,
                       out var runner)
                   && runner.CanHandle(location);
        }
    }

    public Task<GitToolAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        _localRunner.GetAvailabilityAsync(cancellationToken);

    public Task<GitCommandResult> RunAsync(
        GitCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var runner = Resolve(invocation.Repository);
        return runner is null
            ? Task.FromResult(new GitCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                MissingRunnerMessage(invocation.Repository),
                false,
                false))
            : runner.RunAsync(invocation, cancellationToken);
    }

    public void RegisterSsh(string hostId, IGitCommandRunner runner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentNullException.ThrowIfNull(runner);
        var probe = WorkspaceLocation.Ssh(hostId, "/");
        if (!runner.CanHandle(probe))
        {
            throw new ArgumentException(
                "The Git runner does not own the supplied linked-host identity.",
                nameof(runner));
        }

        lock (_gate)
        {
            _sshRunners[probe.Authority] = runner;
        }
    }

    public void RemoveSsh(string hostId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        var authority = WorkspaceLocation.Ssh(hostId, "/").Authority;
        lock (_gate)
        {
            _sshRunners.Remove(authority);
        }
    }

    public void ClearSsh()
    {
        lock (_gate)
        {
            _sshRunners.Clear();
        }
    }

    private IGitCommandRunner? Resolve(WorkspaceLocation? location)
    {
        if (location is null || location.IsLocal)
        {
            return _localRunner;
        }

        lock (_gate)
        {
            return location.IsSsh
                   && _sshRunners.TryGetValue(location.Authority, out var runner)
                ? runner
                : null;
        }
    }

    private static string MissingRunnerMessage(WorkspaceLocation? location) =>
        location is { IsSsh: true }
            ? "The linked host is not ready for Git. Refresh linked devices and unlock its SSH credential."
            : "No Git command runner is registered for this workspace.";
}
