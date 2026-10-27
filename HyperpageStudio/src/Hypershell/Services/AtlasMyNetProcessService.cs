using Hypershell.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Hypershell.Services;

public sealed class AtlasMyNetProcessService : IDisposable
{
    private readonly AtlasMyNetAppStore _store;
    private readonly INetworkSocketInventoryService _inventory;
    private readonly AtlasMyNetWebService _web;
    private readonly AtlasMyNetNativeAppService _native;
    private readonly Dictionary<string, AtlasMyNetAppRuntime> _sessionRuntimes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _runtimeGate = new();
    private readonly SemaphoreSlim _hyperlibStartGate = new(1, 1);
    private bool _isShuttingDown;
    private bool _isDisposed;

    public AtlasMyNetProcessService(
        AtlasMyNetAppStore store,
        INetworkSocketInventoryService inventory,
        AtlasMyNetWebService web)
    {
        _store = store;
        _inventory = inventory;
        _web = web;
        _native = new AtlasMyNetNativeAppService(store, inventory);
    }

    public IReadOnlyCollection<AtlasMyNetAppRuntime> SessionRuntimes
    {
        get { lock (_runtimeGate) return _sessionRuntimes.Values.ToList(); }
    }

    public AtlasMyNetAppRuntime? GetSessionRuntime(string appId)
    {
        PruneExitedRuntimes();
        lock (_runtimeGate)
            return _sessionRuntimes.TryGetValue(appId, out var runtime) ? runtime : null;
    }

    public async Task<AtlasMyNetAppRuntime?> FindRunningAsync(
        AtlasMyNetAppDefinition app,
        IReadOnlyList<NetworkSocketSnapshot>? sockets = null,
        CancellationToken cancellationToken = default)
    {
        if (app.IsNative)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var native = _native.FindRunning(app);
            var owned = GetSessionRuntime(app.Id);
            return native is not null && owned?.ProcessId == native.ProcessId
                && owned.StartedAtUtc == native.StartedAtUtc ? owned : native;
        }
        sockets ??= await Task.Run(_inventory.Capture, cancellationToken);
        var tracked = GetSessionRuntime(app.Id);
        var ports = ResolvePorts(app);
        if (app.Id == HyperlibMyNetRegistration.AppId && tracked is not null)
            ports = ports.OrderByDescending(port => port == tracked.Port).ToArray();
        foreach (var port in ports)
        {
            var listener = sockets.FirstOrDefault(socket =>
                socket.Transport == NetworkTransport.Tcp
                && socket.IsListener
                && socket.LocalPort == port);
            if (listener is null
                || app.Id == HyperlibMyNetRegistration.AppId
                    && !HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(app, listener.ExecutablePath)
                || !await _web.CheckHealthAsync(app, port, cancellationToken))
            {
                continue;
            }

            if (tracked is not null && tracked.Port == port && IsExpectedProcessStillRunning(tracked)
                && (tracked.ProcessId == listener.ProcessId
                    || app.Id == HyperlibMyNetRegistration.AppId
                    && IsHyperlibChild(app, listener.ExecutablePath)))
            {
                return tracked;
            }

            return new AtlasMyNetAppRuntime(
                app.Id,
                listener.ProcessId,
                listener.ExecutablePath,
                listener.ProcessStartTimeUtc ?? DateTimeOffset.MinValue,
                port,
                AtlasMyNetWebService.ResolveUrl(app, port),
                StartedThisSession: false);
        }

        return null;
    }

    private static bool IsHyperlibChild(AtlasMyNetAppDefinition app, string? executablePath)
    {
        var root = HyperlibMyNetRegistration.GetTrustedRoot(app);
        if (root is null || string.IsNullOrWhiteSpace(executablePath)
            || !string.Equals(Path.GetFileName(executablePath), "HyperLib.Host.exe", StringComparison.OrdinalIgnoreCase)) return false;
        return Path.GetFullPath(executablePath).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AtlasMyNetAppRuntime> StartAsync(
        AtlasMyNetAppDefinition app,
        CancellationToken cancellationToken = default)
    {
        if (app.IsNative)
        {
            ThrowIfStartingIsUnavailable();
            var nativeRuntime = await _native.StartAsync(app, startInfo =>
            {
                lock (_runtimeGate)
                {
                    ThrowIfStartingIsUnavailable();
                    cancellationToken.ThrowIfCancellationRequested();
                    using var process = Process.Start(startInfo)
                        ?? throw new InvalidOperationException($"Windows did not start {app.DisplayName}.");
                    var runtime = new AtlasMyNetAppRuntime(app.Id, process.Id, startInfo.FileName,
                        new DateTimeOffset(process.StartTime.ToUniversalTime()), 0, string.Empty, true);
                    _sessionRuntimes[app.Id] = runtime;
                    return runtime;
                }
            }, cancellationToken);
            var owned = GetSessionRuntime(app.Id);
            return owned?.ProcessId == nativeRuntime.ProcessId && owned.StartedAtUtc == nativeRuntime.StartedAtUtc
                ? owned : nativeRuntime;
        }
        ThrowIfStartingIsUnavailable();
        if (app.Id != HyperlibMyNetRegistration.AppId)
            return await StartCoreAsync(app, cancellationToken);
        // Two Open actions must share the same native session rather than launch competing Hosts.
        await _hyperlibStartGate.WaitAsync(cancellationToken);
        try { return await StartCoreAsync(app, cancellationToken); }
        finally { _hyperlibStartGate.Release(); }
    }

    private async Task<AtlasMyNetAppRuntime> StartCoreAsync(
        AtlasMyNetAppDefinition app,
        CancellationToken cancellationToken)
    {
        ThrowIfStartingIsUnavailable();
        if (app.Id == HyperlibMyNetRegistration.AppId && HyperlibMyNetRegistration.GetTrustedRoot(app) is null)
            throw new InvalidOperationException("The installed HyperLib host target changed or is invalid. Reload MyNet after repairing the host installation.");
        var existing = await FindRunningAsync(app, cancellationToken: cancellationToken);
        ThrowIfStartingIsUnavailable();
        if (existing is not null && (app.Id != HyperlibMyNetRegistration.AppId || existing.StartedThisSession))
        {
            return existing;
        }

        // A Host left running by an earlier Hypershell session remains independent. Start a
        // separately owned Host on a free port before attaching privileged native features.

        if (app.Launch is not { } launch)
        {
            throw new InvalidOperationException(
                $"{app.DisplayName} does not define a start command.");
        }

        if (!_store.IsTrusted(app))
        {
            throw new UnauthorizedAccessException(
                $"Review and trust {app.DisplayName}'s commands before starting it.");
        }

        var executable = Path.GetFullPath(launch.Executable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"{app.DisplayName}'s executable could not be found.",
                executable);
        }

        var port = await SelectAvailablePortAsync(app, cancellationToken);
        var url = AtlasMyNetWebService.ResolveUrl(app, port);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(launch.WorkingDirectory)
                ? Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
                : Path.GetFullPath(launch.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(ReplacePort(argument, port));
        }

        foreach (var pair in launch.Environment)
        {
            startInfo.Environment[pair.Key] = ReplacePort(pair.Value, port);
        }

        Process process;
        AtlasMyNetAppRuntime runtime;
        lock (_runtimeGate)
        {
            // Starting and taking the shutdown snapshot share this lock: an in-flight
            // health/port probe cannot launch an untracked process after close begins.
            ThrowIfStartingIsUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
            process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException($"Windows did not start {app.DisplayName}.");
            runtime = new AtlasMyNetAppRuntime(
                app.Id, process.Id, executable,
                new DateTimeOffset(process.StartTime.ToUniversalTime()), port, url,
                StartedThisSession: true);
            _sessionRuntimes[app.Id] = runtime;
        }

        using var startedProcess = process;

        var timeoutSeconds = app.HealthCheck?.TimeoutSeconds ?? 15;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                RemoveRuntime(runtime);
                throw new InvalidOperationException(
                    $"{app.DisplayName} exited before its local endpoint became ready.");
            }

            if (await _web.CheckHealthAsync(app, port, cancellationToken))
            {
                return runtime;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }

        throw new TimeoutException(
            $"{app.DisplayName} did not become ready at {url} within {timeoutSeconds} seconds.");
    }

    public async Task<AtlasMyNetStopResult> RequestAppStopAsync(
        AtlasMyNetAppDefinition app,
        AtlasMyNetAppRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        if (app.Stop is { Mode: AtlasMyNetStopMode.None })
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                $"{app.DisplayName} does not define a stop action.",
                runtime.ProcessId);
        }

        if (app.Stop is { Mode: AtlasMyNetStopMode.Command } stop)
        {
            if (!_store.IsTrusted(app))
            {
                return new AtlasMyNetStopResult(
                    AtlasMyNetStopOutcome.Rejected,
                    $"Review and trust {app.DisplayName}'s stop command first.",
                    runtime.ProcessId);
            }

            try
            {
                using var targetProcess = Process.GetProcessById(runtime.ProcessId);
                var validation = ValidateProcess(targetProcess, runtime.ExecutablePath, runtime.StartedAtUtc);
                if (validation is not null) return validation;

                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(stop.Executable),
                    WorkingDirectory = string.IsNullOrWhiteSpace(stop.WorkingDirectory)
                        ? Path.GetDirectoryName(Path.GetFullPath(stop.Executable))
                          ?? Environment.CurrentDirectory
                        : Path.GetFullPath(stop.WorkingDirectory),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                foreach (var argument in stop.Arguments)
                {
                    startInfo.ArgumentList.Add(ReplacePort(argument, runtime.Port));
                }

                using var stopProcess = Process.Start(startInfo)
                                        ?? throw new InvalidOperationException(
                                            "Windows did not start the stop command.");
                using var commandTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                commandTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await stopProcess.WaitForExitAsync(commandTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    // This is the command process we just created, held by its original
                    // handle; never leave a hung shutdown helper behind.
                    try
                    {
                        StopOwnedCommand(stopProcess);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException
                                                       or Win32Exception or NotSupportedException)
                    {
                        return new AtlasMyNetStopResult(
                            AtlasMyNetStopOutcome.Rejected,
                            $"The stop command could not be terminated. The app was left running because that command may still act on it. {exception.Message}",
                            runtime.ProcessId);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return new AtlasMyNetStopResult(
                        AtlasMyNetStopOutcome.NeedsForce,
                        "The stop command timed out.", runtime.ProcessId);
                }
                if (stopProcess.ExitCode != 0)
                {
                    return new AtlasMyNetStopResult(
                        AtlasMyNetStopOutcome.Failed,
                        $"The stop command exited with code {stopProcess.ExitCode}.",
                        runtime.ProcessId);
                }

                using var exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                exitTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await targetProcess.WaitForExitAsync(exitTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new AtlasMyNetStopResult(
                        AtlasMyNetStopOutcome.NeedsForce,
                        "The stop command completed, but the selected process is still running. Force stop is available as a separate action.",
                        runtime.ProcessId);
                }

                RemoveRuntime(runtime);
                return new AtlasMyNetStopResult(
                    AtlasMyNetStopOutcome.Stopped,
                    $"{app.DisplayName} stopped.",
                    runtime.ProcessId);
            }
            catch (ArgumentException) when (IsProcessKnownExited(runtime.ProcessId))
            {
                RemoveRuntime(runtime);
                return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.AlreadyStopped,
                    "The process was already stopped.", runtime.ProcessId);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException
                                               or Win32Exception
                                               or NotSupportedException or ArgumentException)
            {
                return new AtlasMyNetStopResult(
                    AtlasMyNetStopOutcome.Failed,
                    $"The stop command failed: {exception.Message}",
                    runtime.ProcessId);
            }
        }

        var graceful = await RequestGracefulStopAsync(
            runtime.ProcessId,
            runtime.ExecutablePath,
            runtime.StartedAtUtc,
            cancellationToken);
        return app.IsNative && graceful.Outcome == AtlasMyNetStopOutcome.NeedsForce
            ? graceful with { Message = $"Close was requested. {app.DisplayName} is still open; check its window for a save prompt." }
            : graceful;
    }

    public async Task<AtlasMyNetStopResult> RequestGracefulStopAsync(
        int processId,
        string expectedExecutable,
        DateTimeOffset? expectedStartTime,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsProtectedProcess(processId)) return ProtectedProcessResult(processId);
            using var process = Process.GetProcessById(processId);
            var validation = ValidateProcess(process, expectedExecutable, expectedStartTime);
            if (validation is not null) return validation;
            if (process.HasExited)
            {
                RemoveRuntimeForProcess(processId);
                return new AtlasMyNetStopResult(
                    AtlasMyNetStopOutcome.AlreadyStopped,
                    "The process was already stopped.",
                    processId);
            }

            if (process.CloseMainWindow())
            {
                using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                waitSource.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await process.WaitForExitAsync(waitSource.Token);
                    RemoveRuntimeForProcess(processId);
                    return new AtlasMyNetStopResult(
                        AtlasMyNetStopOutcome.Stopped,
                        "The process closed normally.",
                        processId);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                }
            }

            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.NeedsForce,
                "The process did not close normally. Force stop is available as a separate action.",
                processId);
        }
        catch (ArgumentException) when (IsProcessKnownExited(processId))
        {
            RemoveRuntimeForProcess(processId);
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                "The process was already stopped.",
                processId);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or Win32Exception
                                           or NotSupportedException or ArgumentException)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Failed,
                $"The process could not be closed: {exception.Message}",
                processId);
        }
    }

    public AtlasMyNetStopResult ForceStop(
        int processId,
        string expectedExecutable,
        DateTimeOffset? expectedStartTime)
    {
        try
        {
            if (IsProtectedProcess(processId)) return ProtectedProcessResult(processId);
            using var process = Process.GetProcessById(processId);
            var validation = ValidateProcess(process, expectedExecutable, expectedStartTime);
            if (validation is not null) return validation;
            process.Kill(entireProcessTree: false);
            if (!process.WaitForExit(3000))
                return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.Failed,
                    "The process did not exit after force stop.", processId);
            RemoveRuntimeForProcess(processId);
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Stopped,
                "The selected process was force-stopped.",
                processId);
        }
        catch (ArgumentException) when (IsProcessKnownExited(processId))
        {
            RemoveRuntimeForProcess(processId);
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                "The process was already stopped.",
                processId);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or Win32Exception
                                           or NotSupportedException or ArgumentException)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Failed,
                $"The process could not be force-stopped: {exception.Message}",
                processId);
        }
    }

    public bool HasRunningSessionApps()
    {
        PruneExitedRuntimes();
        lock (_runtimeGate) return _sessionRuntimes.Count > 0;
    }

    public async Task<IReadOnlyList<AtlasMyNetStopResult>>
        StopSessionAppsGracefullyAsync(
            IReadOnlyDictionary<string, AtlasMyNetAppDefinition> definitions,
            CancellationToken cancellationToken = default)
    {
        PruneExitedRuntimes();
        var results = new List<AtlasMyNetStopResult>();
        foreach (var runtime in SessionRuntimes)
        {
            if (!definitions.TryGetValue(runtime.AppId, out var app))
            {
                results.Add(await RequestGracefulStopAsync(
                    runtime.ProcessId,
                    runtime.ExecutablePath,
                    runtime.StartedAtUtc,
                    cancellationToken));
                continue;
            }

            results.Add(await RequestAppStopAsync(app, runtime, cancellationToken));
        }

        return results;
    }

    public async Task<IReadOnlyList<AtlasMyNetStopResult>> StopSessionAppsForShutdownAsync(
        IReadOnlyDictionary<string, AtlasMyNetAppDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        List<AtlasMyNetAppRuntime> runtimes;
        lock (_runtimeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _isShuttingDown = true;
            runtimes = _sessionRuntimes.Values.Where(runtime => runtime.StartedThisSession).ToList();
        }

        var results = new List<AtlasMyNetStopResult>();
        foreach (var runtime in runtimes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTrackedRuntime(runtime)) continue;
            var identity = ValidateRuntime(runtime);
            if (identity is not null)
            {
                if (identity.Outcome == AtlasMyNetStopOutcome.AlreadyStopped) RemoveRuntime(runtime);
                results.Add(identity);
                continue;
            }

            definitions.TryGetValue(runtime.AppId, out var app);
            var mayForceRejectedStop = app?.Stop is { Mode: AtlasMyNetStopMode.None }
                || app?.Stop is { Mode: AtlasMyNetStopMode.Command } && !_store.IsTrusted(app);
            AtlasMyNetStopResult result;
            using (var gracefulTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                gracefulTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    result = app is not null
                        ? await RequestAppStopAsync(app, runtime, gracefulTimeout.Token)
                        : await RequestGracefulStopAsync(runtime.ProcessId, runtime.ExecutablePath,
                            runtime.StartedAtUtc, gracefulTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = new AtlasMyNetStopResult(AtlasMyNetStopOutcome.NeedsForce,
                        "The app did not finish stopping in time.", runtime.ProcessId);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (runtime.Port > 0 // Native editor save prompts must never be bypassed by shell shutdown.
                && result.Outcome is not AtlasMyNetStopOutcome.Stopped and not AtlasMyNetStopOutcome.AlreadyStopped
                && (result.Outcome != AtlasMyNetStopOutcome.Rejected || mayForceRejectedStop)
                && IsTrackedRuntime(runtime))
            {
                // Even a disabled/untrusted stop command does not revoke ownership of
                // our launched process. ForceStop revalidates identity before killing it.
                result = await Task.Run(() => ForceStop(runtime.ProcessId, runtime.ExecutablePath,
                    runtime.StartedAtUtc), cancellationToken);
            }
            if (result.Outcome is AtlasMyNetStopOutcome.Stopped or AtlasMyNetStopOutcome.AlreadyStopped)
                RemoveRuntime(runtime);
            results.Add(result);
        }
        return results;
    }

    public void CancelShutdown()
    {
        lock (_runtimeGate) _isShuttingDown = false;
    }

    public void LeaveSessionAppsRunning()
    {
        lock (_runtimeGate) _sessionRuntimes.Clear();
    }

    private void ThrowIfStartingIsUnavailable()
    {
        lock (_runtimeGate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isShuttingDown)
                throw new InvalidOperationException("Hypershell is closing. Local apps cannot be started until closing is cancelled.");
        }
    }

    private static bool IsProtectedProcess(int processId) =>
        processId is <= 0 or 4 || processId == Environment.ProcessId;

    private static AtlasMyNetStopResult ProtectedProcessResult(int processId) =>
        new(AtlasMyNetStopOutcome.Rejected, "MyNet will not stop this protected process.", processId);

    private static void StopOwnedCommand(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: false);
        if (!process.WaitForExit(3000))
            throw new InvalidOperationException("The stop command did not exit after cancellation.");
    }

    private static bool IsProcessKnownExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private AtlasMyNetStopResult? ValidateRuntime(AtlasMyNetAppRuntime runtime)
    {
        try
        {
            if (IsProtectedProcess(runtime.ProcessId)) return ProtectedProcessResult(runtime.ProcessId);
            using var process = Process.GetProcessById(runtime.ProcessId);
            return ValidateProcess(process, runtime.ExecutablePath, runtime.StartedAtUtc);
        }
        catch (ArgumentException) when (IsProcessKnownExited(runtime.ProcessId))
        {
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.AlreadyStopped,
                "The process was already stopped.", runtime.ProcessId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException or ArgumentException)
        {
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.Rejected,
                $"The process identity could not be verified: {exception.Message}", runtime.ProcessId);
        }
    }

    private AtlasMyNetStopResult? ValidateProcess(
        Process process,
        string expectedExecutable,
        DateTimeOffset? expectedStartTime)
    {
        var processId = process.Id;
        if (IsProtectedProcess(processId)) return ProtectedProcessResult(processId);

        // Pin this process object before querying the inventory. Keep its handle open
        // through CloseMainWindow/Kill so PID reuse cannot change the selected process.
        _ = process.SafeHandle;
        if (process.HasExited)
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.AlreadyStopped,
                "The process was already stopped.", processId);

        if (!_inventory.TryGetProcessIdentity(processId, out var identity))
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "The running process identity is unavailable, so it was not stopped.",
                processId);
        }

        if (!identity.IsCurrentUserOwned)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "Only processes owned by the current Windows account can be stopped.",
                processId);
        }

        if (!expectedStartTime.HasValue
            || expectedStartTime.Value == DateTimeOffset.MinValue
            || !identity.StartTimeUtc.HasValue)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "The process start time is unavailable, so MyNet cannot rule out stale PID reuse.",
                processId);
        }

        if (string.IsNullOrWhiteSpace(expectedExecutable))
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.Rejected,
                "The expected executable path is unavailable, so the process was not stopped.", processId);

        if (!string.IsNullOrWhiteSpace(expectedExecutable)
            && !string.IsNullOrWhiteSpace(identity.ExecutablePath)
            && !string.Equals(
                Path.GetFullPath(identity.ExecutablePath),
                Path.GetFullPath(expectedExecutable),
                StringComparison.OrdinalIgnoreCase))
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "The PID now belongs to a different executable and was not stopped.",
                processId);
        }

        if (!string.IsNullOrWhiteSpace(expectedExecutable)
            && string.IsNullOrWhiteSpace(identity.ExecutablePath))
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "Windows did not expose the current executable path, so the process identity could not be verified.",
                processId);
        }

        if (identity.StartTimeUtc.Value != expectedStartTime.Value
            || new DateTimeOffset(process.StartTime.ToUniversalTime()) != expectedStartTime.Value)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "The PID was reused by a newer process and was not stopped.",
                processId);
        }

        var actualExecutable = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(actualExecutable)
            || !string.Equals(Path.GetFullPath(actualExecutable), Path.GetFullPath(expectedExecutable),
                StringComparison.OrdinalIgnoreCase))
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.Rejected,
                "The opened process executable did not match the expected app.", processId);

        return null;
    }

    private bool IsExpectedProcessStillRunning(AtlasMyNetAppRuntime runtime)
    {
        if (!_inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity)
            || !identity.StartTimeUtc.HasValue)
        {
            return false;
        }

        return identity.StartTimeUtc.Value == runtime.StartedAtUtc;
    }

    private async Task<int> SelectAvailablePortAsync(
        AtlasMyNetAppDefinition app,
        CancellationToken cancellationToken)
    {
        var sockets = await Task.Run(_inventory.Capture, cancellationToken);
        var occupied = sockets
            .Where(socket => socket.Transport == NetworkTransport.Tcp)
            .Select(socket => socket.LocalPort)
            .ToHashSet();
        foreach (var port in ResolvePorts(app))
        {
            if (occupied.Contains(port))
            {
                continue;
            }

            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
            }
            finally
            {
                listener?.Stop();
            }
        }

        throw new InvalidOperationException(
            $"No configured port is available for {app.DisplayName}.");
    }

    private static IReadOnlyList<int> ResolvePorts(AtlasMyNetAppDefinition app)
    {
        if (app.Ports.Count > 0)
        {
            return app.Ports;
        }

        return Uri.TryCreate(app.UrlTemplate, UriKind.Absolute, out var uri)
            ? [uri.Port]
            : [];
    }

    private static string ReplacePort(string value, int port) =>
        value.Replace(
            "{port}",
            port.ToString(),
            StringComparison.OrdinalIgnoreCase);

    private void PruneExitedRuntimes()
    {
        foreach (var runtime in SessionRuntimes)
        {
            try
            {
                using var process = Process.GetProcessById(runtime.ProcessId);
                if (process.HasExited
                    || new DateTimeOffset(process.StartTime.ToUniversalTime()) != runtime.StartedAtUtc)
                    RemoveRuntime(runtime);
            }
            catch (ArgumentException) { RemoveRuntime(runtime); }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Missing permission/metadata is not evidence that the app exited.
            }
        }
    }

    private bool IsTrackedRuntime(AtlasMyNetAppRuntime runtime)
    {
        lock (_runtimeGate)
            return _sessionRuntimes.TryGetValue(runtime.AppId, out var current)
                && current == runtime && current.StartedThisSession;
    }

    private void RemoveRuntime(AtlasMyNetAppRuntime runtime)
    {
        lock (_runtimeGate)
        {
            if (_sessionRuntimes.TryGetValue(runtime.AppId, out var current) && current == runtime)
                _sessionRuntimes.Remove(runtime.AppId);
        }
    }

    private void RemoveRuntimeForProcess(int processId)
    {
        lock (_runtimeGate)
        {
            foreach (var appId in _sessionRuntimes
                         .Where(pair => pair.Value.ProcessId == processId)
                         .Select(pair => pair.Key)
                         .ToList())
                _sessionRuntimes.Remove(appId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_runtimeGate)
        {
            _isDisposed = true;
            _sessionRuntimes.Clear();
        }
    }
}
