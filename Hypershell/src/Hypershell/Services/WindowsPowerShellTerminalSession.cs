using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed class WindowsPowerShellTerminalSession : PersistentProcessTerminalSession
{
    public WindowsPowerShellTerminalSession(
        TerminalProfile profile,
        string initialWorkingDirectory)
        : base(CreateStartInfo(profile, initialWorkingDirectory), profile.Name)
    {
    }

    protected override string BuildSubmission(string command, string marker)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        return
            $"$__hs_source=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedCommand}')); " +
            "$__hs_exit=0; $global:LASTEXITCODE=$null; " +
            "try { Invoke-Expression $__hs_source; $__hs_ok=$?; " +
            "if ($LASTEXITCODE -is [int]) { $__hs_exit=$LASTEXITCODE } " +
            "elseif (-not $__hs_ok) { $__hs_exit=1 } } " +
            "catch { [Console]::Error.WriteLine(($_ | Out-String)); $__hs_exit=1 } " +
            $"finally {{ [Console]::Out.WriteLine('{marker}' + $__hs_exit + \"`t\" + (Get-Location).Path) }}";
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

        var values = line[marker.Length..].Split('\t', 2);
        int? exitCode = values.Length > 0 && int.TryParse(values[0], out var parsedExitCode)
            ? parsedExitCode
            : null;
        completion = new ProcessCommandCompletion(
            exitCode,
            values.Length > 1 ? values[1] : null);
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
