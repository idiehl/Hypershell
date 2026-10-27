using Hypershell.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public sealed class AtlasMyNetProcessService : IDisposable
{
    private readonly AtlasMyNetAppStore _store;
    private readonly INetworkSocketInventoryService _inventory;
    private readonly AtlasMyNetWebService _web;
    private readonly AtlasMyNetNativeAppService _native;
    private readonly Dictionary<string, AtlasMyNetAppRuntime> _sessionRuntimes =
        new(StringComparer.OrdinalIgnoreCase);
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

    public IReadOnlyCollection<AtlasMyNetAppRuntime> SessionRuntimes =>
        _sessionRuntimes.Values.ToList();

    public AtlasMyNetAppRuntime? GetSessionRuntime(string appId)
    {
        PruneExitedRuntimes();
        return _sessionRuntimes.TryGetValue(appId, out var runtime)
            ? runtime
            : null;
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
        foreach (var port in ResolvePorts(app))
        {
            var listener = sockets.FirstOrDefault(socket =>
                socket.Transport == NetworkTransport.Tcp
                && socket.IsListener
                && socket.LocalPort == port);
            if (listener is null
                || !await _web.CheckHealthAsync(app, port, cancellationToken))
            {
                continue;
            }

            if (_sessionRuntimes.TryGetValue(app.Id, out var tracked)
                && tracked.ProcessId == listener.ProcessId)
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

    public async Task<AtlasMyNetAppRuntime> StartAsync(
        AtlasMyNetAppDefinition app,
        CancellationToken cancellationToken = default)
    {
        if (app.IsNative)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var nativeRuntime = await _native.StartAsync(app, startInfo =>
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Windows did not start {app.DisplayName}.");
                var runtime = new AtlasMyNetAppRuntime(app.Id, process.Id, startInfo.FileName,
                    new DateTimeOffset(process.StartTime.ToUniversalTime()), 0, string.Empty, true);
                _sessionRuntimes[app.Id] = runtime;
                return runtime;
            }, cancellationToken);
            var owned = GetSessionRuntime(app.Id);
            return owned?.ProcessId == nativeRuntime.ProcessId && owned.StartedAtUtc == nativeRuntime.StartedAtUtc
                ? owned : nativeRuntime;
        }
        PruneExitedRuntimes();
        using var operation = await AcquireAppOperationAsync(app.Id, cancellationToken);
        return await StartCoreAsync(app, cancellationToken);
    }

    private async Task<AtlasMyNetAppRuntime> StartCoreAsync(
        AtlasMyNetAppDefinition app,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (await FindRunningAsync(app, cancellationToken: cancellationToken)
            is { } existing)
        {
            return existing;
        }

        if (GetSessionRuntime(app.Id) is not null || ReadPersistedRuntime(app.Id) is not null)
        {
            throw new InvalidOperationException(
                $"{app.DisplayName} is still running but its health check has not passed. Stop it before starting another copy.");
        }

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

        using var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException(
                          $"Windows did not start {app.DisplayName}.");
        var startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
        var runtime = new AtlasMyNetAppRuntime(
            app.Id,
            process.Id,
            executable,
            startedAt,
            port,
            url,
            StartedThisSession: true);
        _sessionRuntimes[app.Id] = runtime;
        var runtimePath = GetOperationPath(app.Id, ".runtime.json");
        File.WriteAllText(runtimePath + ".tmp", JsonSerializer.Serialize(runtime));
        File.Move(runtimePath + ".tmp", runtimePath, overwrite: true);

        var timeoutSeconds = app.HealthCheck?.TimeoutSeconds ?? 15;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                _sessionRuntimes.Remove(app.Id);
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
        try
        {
            using var operation = await AcquireAppOperationAsync(app.Id, cancellationToken);
            var validation = ValidateProcess(runtime.ProcessId, runtime.ExecutablePath, runtime.StartedAtUtc);
            if (validation is not null)
            {
                return validation;
            }

            return await RequestAppStopCoreAsync(app, runtime, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.Failed,
                "The stop request was cancelled.", runtime.ProcessId);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException)
        {
            return new AtlasMyNetStopResult(AtlasMyNetStopOutcome.Failed,
                $"The stop request could not complete: {exception.Message}", runtime.ProcessId);
        }
    }

    private async Task<AtlasMyNetStopResult> RequestAppStopCoreAsync(
        AtlasMyNetAppDefinition app,
        AtlasMyNetAppRuntime runtime,
        CancellationToken cancellationToken)
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
                await stopProcess.WaitForExitAsync(cancellationToken);
                if (stopProcess.ExitCode != 0)
                {
                    return new AtlasMyNetStopResult(
                        AtlasMyNetStopOutcome.Failed,
                        $"The stop command exited with code {stopProcess.ExitCode}.",
                        runtime.ProcessId);
                }

                var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
                while (IsExpectedProcessStillRunning(runtime)
                       && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(150),
                        cancellationToken);
                }

                if (IsExpectedProcessStillRunning(runtime))
                {
                    return new AtlasMyNetStopResult(
                        AtlasMyNetStopOutcome.NeedsForce,
                        "The stop command completed, but the selected process is still running. Force stop is available as a separate action.",
                        runtime.ProcessId);
                }

                _sessionRuntimes.Remove(app.Id);
                return new AtlasMyNetStopResult(
                    AtlasMyNetStopOutcome.Stopped,
                    $"{app.DisplayName} stopped.",
                    runtime.ProcessId);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException
                                               or Win32Exception)
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
        var validation = ValidateProcess(
            processId,
            expectedExecutable,
            expectedStartTime);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
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
        catch (ArgumentException)
        {
            RemoveRuntimeForProcess(processId);
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                "The process was already stopped.",
                processId);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or Win32Exception
                                           or NotSupportedException)
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
        var validation = ValidateProcess(
            processId,
            expectedExecutable,
            expectedStartTime);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: false);
            process.WaitForExit(3000);
            RemoveRuntimeForProcess(processId);
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Stopped,
                "The selected process was force-stopped.",
                processId);
        }
        catch (ArgumentException)
        {
            RemoveRuntimeForProcess(processId);
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                "The process was already stopped.",
                processId);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or Win32Exception
                                           or NotSupportedException)
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
        return _sessionRuntimes.Count > 0;
    }

    public async Task<IReadOnlyList<AtlasMyNetStopResult>>
        StopSessionAppsGracefullyAsync(
            IReadOnlyDictionary<string, AtlasMyNetAppDefinition> definitions,
            CancellationToken cancellationToken = default)
    {
        PruneExitedRuntimes();
        var results = new List<AtlasMyNetStopResult>();
        foreach (var runtime in _sessionRuntimes.Values.ToList())
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

    public void LeaveSessionAppsRunning() => _sessionRuntimes.Clear();

    private AtlasMyNetStopResult? ValidateProcess(
        int processId,
        string expectedExecutable,
        DateTimeOffset? expectedStartTime)
    {
        if (processId is <= 0 or 4 || processId == Environment.ProcessId)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "Atlas MyNet will not stop this protected process.",
                processId);
        }

        if (!_inventory.TryGetProcessIdentity(processId, out var identity))
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                "The process is no longer running.",
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
                "The process start time is unavailable, so Atlas MyNet cannot rule out stale PID reuse.",
                processId);
        }

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

        if (expectedStartTime.HasValue
            && identity.StartTimeUtc.HasValue
            && Math.Abs((identity.StartTimeUtc.Value - expectedStartTime.Value).TotalSeconds)
            > 1)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.Rejected,
                "The PID was reused by a newer process and was not stopped.",
                processId);
        }

        return null;
    }

    private bool IsExpectedProcessStillRunning(AtlasMyNetAppRuntime runtime)
    {
        if (!_inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity)
            || !identity.StartTimeUtc.HasValue)
        {
            return false;
        }

        return Math.Abs(
            (identity.StartTimeUtc.Value - runtime.StartedAtUtc).TotalSeconds) <= 1;
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

    private async Task<FileStream> AcquireAppOperationAsync(string appId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var path = GetOperationPath(appId, ".lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // File ownership is released by Windows even if a launcher exits unexpectedly.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private string GetOperationPath(string appId, string suffix) =>
        Path.Combine(Path.GetDirectoryName(_store.AppsPath)!, "operations",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appId.ToUpperInvariant()))) + suffix);

    private AtlasMyNetAppRuntime? ReadPersistedRuntime(string appId)
    {
        var path = GetOperationPath(appId, ".runtime.json");
        if (!File.Exists(path)) return null;
        AtlasMyNetAppRuntime? runtime;
        try
        {
            runtime = JsonSerializer.Deserialize<AtlasMyNetAppRuntime>(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("MyNet's saved process identity could not be read. Review the running app before restarting it.", exception);
        }

        if (runtime is null || !string.Equals(runtime.AppId, appId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("MyNet's saved process identity is invalid.");
        }

        if (!_inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity))
        {
            try
            {
                using var process = Process.GetProcessById(runtime.ProcessId);
                if (!process.HasExited)
                {
                    throw new InvalidOperationException("The saved MyNet host still exists, but Windows could not verify its identity. Review it before restarting.");
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException("Windows could not verify the saved MyNet host. Review it before restarting.", exception);
            }

            return null;
        }

        if (identity.StartTimeUtc.HasValue
            && Math.Abs((identity.StartTimeUtc.Value - runtime.StartedAtUtc).TotalSeconds) > 1)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(identity.ExecutablePath)
            && !string.IsNullOrWhiteSpace(runtime.ExecutablePath)
            && !string.Equals(Path.GetFullPath(identity.ExecutablePath), Path.GetFullPath(runtime.ExecutablePath), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!identity.IsCurrentUserOwned || !identity.StartTimeUtc.HasValue
            || runtime.StartedAtUtc == DateTimeOffset.MinValue
            || string.IsNullOrWhiteSpace(identity.ExecutablePath)
            || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new InvalidOperationException("Windows could not verify the saved MyNet host's owner, executable, and start time. Review it before restarting.");
        }

        return runtime;
    }

    public AtlasMyNetAppRuntime? GetKnownRuntime(string appId) =>
        GetSessionRuntime(appId)
        ?? (ReadPersistedRuntime(appId) is { } runtime
            ? runtime with { StartedThisSession = false }
            : null);

    private void PruneExitedRuntimes()
    {
        foreach (var runtime in _sessionRuntimes.Values.ToList())
        {
            if (!_inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity)
                || identity.StartTimeUtc.HasValue
                && Math.Abs((identity.StartTimeUtc.Value - runtime.StartedAtUtc).TotalSeconds)
                > 1)
            {
                _sessionRuntimes.Remove(runtime.AppId);
            }
        }
    }

    private void RemoveRuntimeForProcess(int processId)
    {
        foreach (var appId in _sessionRuntimes
                     .Where(pair => pair.Value.ProcessId == processId)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _sessionRuntimes.Remove(appId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _sessionRuntimes.Clear();
    }
}
