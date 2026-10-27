using Hypershell.Models;
using System.Diagnostics;

namespace Hypershell.Services;

public abstract class PersistentProcessTerminalSession : ITerminalSession
{
    private readonly ProcessStartInfo _startInfo;
    private readonly string _displayName;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly object _stateLock = new();
    private Process? _process;
    private TerminalExecutionResult? _activeResult;
    private string? _activeMarker;
    private TaskCompletionSource<ProcessCommandCompletion>? _activeCompletion;
    private bool _isDisposed;

    protected PersistentProcessTerminalSession(ProcessStartInfo startInfo, string displayName)
    {
        _startInfo = startInfo;
        _displayName = displayName;
    }

    public async Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var result = new TerminalExecutionResult();
            var marker = $"__HYPERSHELL_DONE_{Guid.NewGuid():N}__";
            var completion = new TaskCompletionSource<ProcessCommandCompletion>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_stateLock)
            {
                _activeResult = result;
                _activeMarker = marker;
                _activeCompletion = completion;
            }

            try
            {
                EnsureProcessStarted();
                var submission = BuildSubmission(command, marker);
                await _process!.StandardInput.WriteLineAsync(submission);
                await _process.StandardInput.FlushAsync(cancellationToken);

                var processCompletion = await completion.Task.WaitAsync(cancellationToken);
                result.WorkingDirectory = processCompletion.WorkingDirectory;
                if (processCompletion.ExitCode is not null
                    && processCompletion.ExitCode != 0
                    && result.Errors.Count == 0)
                {
                    result.Errors.Add($"{_displayName} command exited with code {processCompletion.ExitCode}.");
                }

                // Standard output and error are separate pipes. Give the final
                // error event a moment to reach the current command result.
                await Task.Delay(40, CancellationToken.None);
                return result;
            }
            finally
            {
                lock (_stateLock)
                {
                    _activeResult = null;
                    _activeMarker = null;
                    _activeCompletion = null;
                }
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    protected abstract string BuildSubmission(string command, string marker);

    protected abstract bool TryParseCompletion(
        string line,
        string marker,
        out ProcessCommandCompletion completion);

    private void EnsureProcessStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        _process?.Dispose();
        _process = new Process
        {
            StartInfo = _startInfo,
            EnableRaisingEvents = true
        };
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;

        if (!_process.Start())
        {
            throw new InvalidOperationException($"{_displayName} could not be started.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (eventArgs.Data is not { } line)
        {
            return;
        }

        var normalizedLine = NormalizeOutputLine(line);
        if (normalizedLine is null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_activeResult is null || _activeMarker is null || _activeCompletion is null)
            {
                return;
            }

            if (TryParseCompletion(normalizedLine, _activeMarker, out var completion))
            {
                _activeCompletion.TrySetResult(completion);
                return;
            }

            _activeResult.Output.Add(normalizedLine);
        }
    }

    protected virtual string? NormalizeOutputLine(string line) => line;

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (eventArgs.Data is not { Length: > 0 } line)
        {
            return;
        }

        lock (_stateLock)
        {
            _activeResult?.Errors.Add(line);
        }
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        lock (_stateLock)
        {
            _activeCompletion?.TrySetException(
                new InvalidOperationException($"{_displayName} exited before the command completed."));
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_process is not null)
        {
            _process.OutputDataReceived -= OnOutputDataReceived;
            _process.ErrorDataReceived -= OnErrorDataReceived;
            _process.Exited -= OnProcessExited;

            try
            {
                _process.StandardInput.Close();
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch
            {
                // The shell may already be shutting down.
            }

            _process.Dispose();
        }

        _executionGate.Dispose();
    }

    protected readonly record struct ProcessCommandCompletion(int? ExitCode, string? WorkingDirectory);
}
