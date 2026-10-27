using Hypershell.Models;
using System.Diagnostics;
using System.IO;

namespace Hypershell.Services;

public sealed class CommandPromptTerminalSession : PersistentProcessTerminalSession
{
    private readonly string _initializationCommand;

    public CommandPromptTerminalSession(TerminalProfile profile, string initialWorkingDirectory)
        : base(CreateStartInfo(profile, initialWorkingDirectory), profile.Name)
    {
        _initializationCommand = profile.InitializationCommand;
    }

    protected override string BuildSubmission(string command, string marker)
    {
        var initialization = string.IsNullOrWhiteSpace(_initializationCommand)
            ? string.Empty
            : $"{_initializationCommand}\r\n";
        return initialization +
               $"{command}\r\n" +
               "set \"__HYPERSHELL_EXIT=%ERRORLEVEL%\"\r\n" +
               $"echo {marker}%__HYPERSHELL_EXIT%\t%CD%";
    }

    protected override bool TryParseCompletion(
        string line,
        string marker,
        out ProcessCommandCompletion completion)
    {
        completion = default;
        var markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var values = line[(markerIndex + marker.Length)..].Split('\t', 2);
        int? exitCode = values.Length > 0 && int.TryParse(values[0], out var parsedExitCode)
            ? parsedExitCode
            : null;
        completion = new ProcessCommandCompletion(
            exitCode,
            values.Length > 1 ? values[1] : null);
        return true;
    }

    protected override string? NormalizeOutputLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)
            || line.StartsWith("Microsoft Windows [Version ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("(c) Microsoft Corporation.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return line.StartsWith(' ') ? line[1..] : line;
    }

    private static ProcessStartInfo CreateStartInfo(
        TerminalProfile profile,
        string initialWorkingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = profile.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.Exists(initialWorkingDirectory)
                ? initialWorkingDirectory
                : Environment.CurrentDirectory
        };
        startInfo.Environment["PROMPT"] = " ";
        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
