using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed class LanguageServerProcessHost : IAsyncDisposable
{
    private const int MaximumDiagnosticCharacters = 64 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Task? _monitorTask;
    private Task<string>? _diagnosticTask;
    private bool _stopRequested;
    private bool _isDisposed;

    public LanguageServerProcessState State { get; private set; } =
        LanguageServerProcessState.Stopped;
    public string StatusMessage { get; private set; } =
        "Stopped. Language servers are never started automatically.";
    public Stream? InputStream => _process is { HasExited: false }
        ? _process.StandardInput.BaseStream
        : null;
    public Stream? OutputStream => _process is { HasExited: false }
        ? _process.StandardOutput.BaseStream
        : null;

    public event EventHandler<LanguageServerStateChangedEventArgs>? StateChanged;

    public async Task StartAsync(
        LanguageServerLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("A language server is already running in this host.");
            }

            ValidateTool(request.Tool);
            SetState(LanguageServerProcessState.Starting, $"Starting {request.Tool.Definition.DisplayName}…");
            var startInfo = new ProcessStartInfo
            {
                FileName = request.Tool.ExecutablePath,
                WorkingDirectory = ResolveWorkingDirectory(request.WorkingDirectory),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in request.Tool.Definition.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    throw new InvalidOperationException("The language-server process did not start.");
                }
            }
            catch
            {
                process.Dispose();
                SetState(LanguageServerProcessState.Faulted, "The language server could not start.");
                throw;
            }

            _process = process;
            _stopRequested = false;
            _diagnosticTask = ReadDiagnosticsAsync(process.StandardError);
            _monitorTask = MonitorProcessAsync(process, request.Tool.Definition.DisplayName);
            SetState(LanguageServerProcessState.Running, $"{request.Tool.Definition.DisplayName} is running.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is null)
            {
                SetState(
                    LanguageServerProcessState.Stopped,
                    "Stopped. Language servers are never started automatically.");
                return;
            }

            var process = _process;
            _stopRequested = true;
            SetState(LanguageServerProcessState.Stopping, "Stopping the language server…");
            try
            {
                process.StandardInput.Close();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);
            }
            catch (TimeoutException)
            {
                TryTerminate(process);
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }

            _process = null;
            process.Dispose();
            SetState(
                LanguageServerProcessState.Stopped,
                "Stopped. Language servers are never started automatically.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            await StopAsync();
            if (_monitorTask is not null)
            {
                await _monitorTask;
            }
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private async Task MonitorProcessAsync(Process process, string displayName)
    {
        int exitCode;
        try
        {
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            ObjectDisposedException or
            System.Runtime.InteropServices.COMException)
        {
            // StopAsync owns process disposal. On Windows the monitor can resume
            // after the process exits but after that disposal invalidates the
            // native handle, so there is no unsolicited exit left to report.
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_process, process) || _stopRequested)
            {
                return;
            }

            var diagnostic = _diagnosticTask is null
                ? string.Empty
                : await _diagnosticTask;
            _process = null;
            process.Dispose();
            SetState(
                exitCode == 0
                    ? LanguageServerProcessState.Stopped
                    : LanguageServerProcessState.Faulted,
                exitCode == 0
                    ? $"{displayName} stopped."
                    : string.IsNullOrWhiteSpace(diagnostic)
                        ? $"{displayName} exited with code {exitCode}."
                        : $"{displayName} exited with code {exitCode}: {diagnostic}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateTool(DiscoveredLanguageTool tool)
    {
        if (!tool.IsAvailable
            || tool.Definition.Kind != LanguageToolKind.LanguageServer
            || tool.Definition.IsHostBuiltIn)
        {
            throw new InvalidOperationException("The selected tool is not an available external language server.");
        }

        if (!BoundedProcessRunner.TryResolveNativeExecutable(
                tool.ExecutablePath,
                out _,
                out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private static string ResolveWorkingDirectory(string requestedDirectory) =>
        !string.IsNullOrWhiteSpace(requestedDirectory)
        && Directory.Exists(requestedDirectory)
            ? Path.GetFullPath(requestedDirectory)
            : Environment.CurrentDirectory;

    private static async Task<string> ReadDiagnosticsAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[2048];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                break;
            }

            var remaining = MaximumDiagnosticCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }
        }

        var value = builder.ToString()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return value;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void SetState(LanguageServerProcessState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(this, new LanguageServerStateChangedEventArgs(state, message));
    }
}
