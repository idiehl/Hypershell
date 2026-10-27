using Hypershell.Models;
using System.Diagnostics;
using System.IO;

namespace Hypershell.Services;

public sealed class MySqlTerminalSession : PersistentProcessTerminalSession
{
    public MySqlTerminalSession(TerminalProfile profile, string initialWorkingDirectory)
        : base(CreateStartInfo(profile, initialWorkingDirectory), profile.Name)
    {
    }

    protected override string BuildSubmission(string command, string marker)
    {
        var normalizedCommand = command.TrimEnd();
        if (!normalizedCommand.EndsWith(';')
            && !normalizedCommand.StartsWith('\\'))
        {
            normalizedCommand += ";";
        }

        return $"{normalizedCommand}\n" +
               $"SELECT CONCAT('{marker}', COALESCE(DATABASE(), ''));";
    }

    protected override bool TryParseCompletion(
        string line,
        string marker,
        out ProcessCommandCompletion completion)
    {
        completion = default;
        if (!line.StartsWith(marker, StringComparison.Ordinal))
        {
            return false;
        }

        completion = new ProcessCommandCompletion(0, null);
        return true;
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
        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
