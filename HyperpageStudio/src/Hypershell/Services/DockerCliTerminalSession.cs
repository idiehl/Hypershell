using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed class DockerCliTerminalSession : ITerminalSession
{
    private readonly string _dockerExecutable;
    private string _workingDirectory;
    private bool _isDisposed;

    public DockerCliTerminalSession(TerminalProfile profile, string initialWorkingDirectory)
    {
        _dockerExecutable = profile.ExecutablePath;
        _workingDirectory = Directory.Exists(initialWorkingDirectory)
            ? initialWorkingDirectory
            : Environment.CurrentDirectory;
    }

    public async Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            _workingDirectory = workingDirectory;
        }

        var dockerArguments = NormalizeDockerArguments(command);
        var startInfo = new ProcessStartInfo
        {
            FileName = _dockerExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _workingDirectory
        };
        foreach (var argument in ParseArguments(dockerArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = new TerminalExecutionResult { WorkingDirectory = _workingDirectory };
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Docker CLI could not be started.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            AddLines(result.Output, await outputTask);
            AddLines(result.Errors, await errorTask);
            if (process.ExitCode != 0 && result.Errors.Count == 0)
            {
                result.Errors.Add($"Docker command exited with code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return result;
    }

    public void Dispose()
    {
        _isDisposed = true;
    }

    private static string NormalizeDockerArguments(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Equals("docker", StringComparison.OrdinalIgnoreCase))
        {
            return "--help";
        }

        if (trimmed.StartsWith("docker ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[7..].TrimStart();
        }

        return trimmed;
    }

    private static IEnumerable<string> ParseArguments(string commandLine)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        foreach (var character in commandLine)
        {
            if (character is '"' or '\'')
            {
                if (quote == character)
                {
                    quote = null;
                }
                else if (quote is null)
                {
                    quote = character;
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (char.IsWhiteSpace(character) && quote is null)
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static void AddLines(ICollection<string> destination, string text)
    {
        foreach (var line in text
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            destination.Add(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The CLI may have exited while cancellation was being processed.
        }
    }
}
