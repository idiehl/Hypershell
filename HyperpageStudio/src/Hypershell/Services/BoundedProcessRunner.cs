using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed class BoundedProcessRunner
{
    public async Task<BoundedProcessResult> RunAsync(
        BoundedProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumStandardInputCharacters < 0
            || request.StandardInput.Length > request.MaximumStandardInputCharacters)
        {
            return Failed("The formatter input exceeds the configured safety limit.");
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            return Failed("The process timeout must be greater than zero.");
        }

        if (!TryResolveNativeExecutable(request.ExecutablePath, out var executablePath, out var validationError))
        {
            return Failed(validationError);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = ResolveWorkingDirectory(request.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in request.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Key)
                || variable.Key.IndexOf('=') >= 0)
            {
                return Failed("A process environment-variable name is invalid.");
            }

            startInfo.Environment[variable.Key] = variable.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return Failed("The process did not start.");
            }
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Failed($"The process could not start: {exception.Message}");
        }

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var executionSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var token = executionSource.Token;
        var outputLimitState = new ProcessOutputLimitState();
        var standardOutputTask = ReadBoundedAsync(
            process.StandardOutput,
            request.MaximumStandardOutputCharacters,
            "standard output",
            outputLimitState,
            executionSource,
            token);
        var standardErrorTask = ReadBoundedAsync(
            process.StandardError,
            request.MaximumStandardErrorCharacters,
            "standard error",
            outputLimitState,
            executionSource,
            token);
        var standardInputTask = WriteInputAsync(
            process.StandardInput,
            request.StandardInput,
            token);

        try
        {
            await Task.WhenAll(
                process.WaitForExitAsync(token),
                standardInputTask,
                standardOutputTask,
                standardErrorTask);
            return new BoundedProcessResult
            {
                Started = true,
                ExitCode = process.ExitCode,
                StandardOutput = standardOutputTask.Result,
                StandardError = standardErrorTask.Result
            };
        }
        catch (ProcessOutputLimitException exception)
        {
            TryTerminate(process);
            await WaitAfterTerminationAsync(process);
            return new BoundedProcessResult
            {
                Started = true,
                OutputLimitExceeded = true,
                ExitCode = TryGetExitCode(process),
                FailureMessage = exception.Message
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            await WaitAfterTerminationAsync(process);
            if (!string.IsNullOrWhiteSpace(outputLimitState.Message))
            {
                return new BoundedProcessResult
                {
                    Started = true,
                    OutputLimitExceeded = true,
                    ExitCode = TryGetExitCode(process),
                    FailureMessage = outputLimitState.Message
                };
            }

            return new BoundedProcessResult
            {
                Started = true,
                TimedOut = timeoutSource.IsCancellationRequested,
                ExitCode = TryGetExitCode(process),
                FailureMessage = timeoutSource.IsCancellationRequested
                    ? $"The process exceeded its {request.Timeout.TotalSeconds:0.#}-second timeout."
                    : "The process was cancelled."
            };
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            await WaitAfterTerminationAsync(process);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            TryTerminate(process);
            await WaitAfterTerminationAsync(process);
            return new BoundedProcessResult
            {
                Started = true,
                ExitCode = TryGetExitCode(process),
                FailureMessage = $"The process stream failed: {exception.Message}"
            };
        }
    }

    internal static bool TryResolveNativeExecutable(
        string path,
        out string executablePath,
        out string error)
    {
        executablePath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            error = "The executable path must be absolute.";
            return false;
        }

        try
        {
            executablePath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The executable path is invalid.";
            return false;
        }

        var extension = Path.GetExtension(executablePath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only native .exe and .com tools may be launched directly.";
            return false;
        }

        if (!File.Exists(executablePath))
        {
            error = "The executable no longer exists at its detected path.";
            return false;
        }

        return true;
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        string streamName,
        ProcessOutputLimitState outputLimitState,
        CancellationTokenSource executionSource,
        CancellationToken cancellationToken)
    {
        if (maximumCharacters < 0)
        {
            outputLimitState.Set($"The {streamName} safety limit is invalid.");
            executionSource.Cancel();
            throw new ProcessOutputLimitException($"The {streamName} safety limit is invalid.");
        }

        var builder = new StringBuilder(Math.Min(maximumCharacters, 16 * 1024));
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return builder.ToString();
            }

            if (builder.Length + read > maximumCharacters)
            {
                outputLimitState.Set(
                    $"The process exceeded the {streamName} safety limit.");
                executionSource.Cancel();
                throw new ProcessOutputLimitException(
                    $"The process exceeded the {streamName} safety limit.");
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static async Task WriteInputAsync(
        StreamWriter writer,
        string input,
        CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(input.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            writer.Close();
        }
    }

    private static string ResolveWorkingDirectory(string requestedDirectory)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(requestedDirectory)
                   && Directory.Exists(requestedDirectory)
                ? Path.GetFullPath(requestedDirectory)
                : Environment.CurrentDirectory;
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Environment.CurrentDirectory;
        }
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

    private static async Task WaitAfterTerminationAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or TimeoutException)
        {
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static BoundedProcessResult Failed(string message) =>
        new() { FailureMessage = message };

    private sealed class ProcessOutputLimitException : Exception
    {
        public ProcessOutputLimitException(string message)
            : base(message)
        {
        }
    }

    private sealed class ProcessOutputLimitState
    {
        private readonly object _sync = new();
        private string _message = string.Empty;

        public string Message
        {
            get
            {
                lock (_sync)
                {
                    return _message;
                }
            }
        }

        public void Set(string message)
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(_message))
                {
                    _message = message;
                }
            }
        }
    }
}
