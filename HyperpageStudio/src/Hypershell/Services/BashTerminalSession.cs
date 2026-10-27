using Hypershell.Models;
using System.Diagnostics;
using System.IO;

namespace Hypershell.Services;

public sealed class BashTerminalSession : PersistentProcessTerminalSession
{
    public BashTerminalSession(TerminalProfile profile, string initialWorkingDirectory)
        : base(CreateStartInfo(profile, initialWorkingDirectory), profile.Name)
    {
    }

    protected override string BuildSubmission(string command, string marker) =>
        $"{command}\n" +
        "__hypershell_exit=$?\n" +
        $"printf '\\n{marker}%s\\t%s\\n' \"$__hypershell_exit\" \"$PWD\"";

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

    private static ProcessStartInfo CreateStartInfo(TerminalProfile profile, string initialWorkingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = profile.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (profile.ShellKind == TerminalShellKind.WslBash)
        {
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(profile.Distribution);
            if (!string.IsNullOrWhiteSpace(initialWorkingDirectory))
            {
                startInfo.ArgumentList.Add("--cd");
                startInfo.ArgumentList.Add(initialWorkingDirectory);
            }

            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("bash");
            startInfo.ArgumentList.Add("--noprofile");
            startInfo.ArgumentList.Add("--norc");
            return startInfo;
        }

        startInfo.WorkingDirectory = Directory.Exists(initialWorkingDirectory)
            ? initialWorkingDirectory
            : Environment.CurrentDirectory;
        startInfo.Environment["CHERE_INVOKING"] = "1";
        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
