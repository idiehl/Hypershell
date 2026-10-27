using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public sealed class PythonTerminalSession : PersistentProcessTerminalSession
{
    public PythonTerminalSession(TerminalProfile profile, string initialWorkingDirectory)
        : base(CreateStartInfo(profile, initialWorkingDirectory), profile.Name)
    {
    }

    protected override string BuildSubmission(string command, string marker)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        var script =
            "import base64, os, traceback\n" +
            "try:\n" +
            $"    _hs_source = base64.b64decode('{encodedCommand}').decode('utf-8')\n" +
            "    try:\n" +
            "        _hs_code = compile(_hs_source, '<hypershell>', 'single')\n" +
            "    except SyntaxError as _hs_compile_error:\n" +
            "        if 'multiple statements' not in str(_hs_compile_error):\n" +
            "            raise\n" +
            "        _hs_code = compile(_hs_source, '<hypershell>', 'exec')\n" +
            "    exec(_hs_code, globals())\n" +
            "except BaseException:\n" +
            "    traceback.print_exc()\n" +
            "finally:\n" +
            $"    print('{marker}' + os.getcwd(), flush=True)\n";
        return $"exec({JsonSerializer.Serialize(script)})";
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

        completion = new ProcessCommandCompletion(0, line[marker.Length..]);
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
            RedirectStandardError = true,
            WorkingDirectory = Directory.Exists(initialWorkingDirectory)
                ? initialWorkingDirectory
                : Environment.CurrentDirectory
        };

        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("import sys; sys.ps1=''; sys.ps2=''");
        return startInfo;
    }
}
