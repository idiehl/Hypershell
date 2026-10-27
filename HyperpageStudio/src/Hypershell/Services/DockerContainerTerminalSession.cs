using Hypershell.Models;
using System.Diagnostics;

namespace Hypershell.Services;

public sealed class DockerContainerTerminalSession : PersistentProcessTerminalSession
{
    private readonly bool _usesCommandPrompt;

    public DockerContainerTerminalSession(TerminalProfile profile)
        : base(CreateStartInfo(profile), profile.Name)
    {
        _usesCommandPrompt = profile.Arguments.Any(argument =>
            argument.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase));
    }

    protected override string BuildSubmission(string command, string marker)
    {
        if (_usesCommandPrompt)
        {
            return $"{command}\r\n" +
                   "set \"__HYPERSHELL_EXIT=%ERRORLEVEL%\"\r\n" +
                   $"echo {marker}%__HYPERSHELL_EXIT%\t%CD%";
        }

        return $"{command}\n" +
               "__hypershell_exit=$?\n" +
               $"printf '\\n{marker}%s\\t%s\\n' \"$__hypershell_exit\" \"$PWD\"";
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

    private static ProcessStartInfo CreateStartInfo(TerminalProfile profile)
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
        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
