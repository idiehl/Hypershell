using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

/// <summary>
/// Shared persistent Node REPL transport. Language-specific subclasses only
/// decide how submitted source becomes executable JavaScript.
/// </summary>
public abstract class NodeReplTerminalSession :
    PersistentProcessTerminalSession
{
    protected NodeReplTerminalSession(
        TerminalProfile profile,
        string initialWorkingDirectory)
        : base(CreateStartInfo(profile, initialWorkingDirectory), profile.Name)
    {
    }

    protected virtual string SourceFileName => "<hypershell>";

    protected abstract string BuildExecutableSourceExpression(
        string sourceIdentifier);

    protected override string BuildSubmission(string command, string marker)
    {
        var encodedCommand = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(command));
        var markerLiteral = JsonSerializer.Serialize(marker);
        var fileNameLiteral = JsonSerializer.Serialize(SourceFileName);
        var executableSource = BuildExecutableSourceExpression("_hsSource");
        return
            "void (async () => { try { " +
            $"const _hsSource = Buffer.from('{encodedCommand}', 'base64').toString('utf8'); " +
            $"const _hsExecutableSource = {executableSource}; " +
            "const _hsValue = require('node:vm').runInThisContext(" +
            $"_hsExecutableSource, {{ filename: {fileNameLiteral} }}); " +
            "const _hsResolved = await _hsValue; " +
            "if (_hsResolved !== undefined) console.log(_hsResolved); " +
            "} catch (_hsError) { console.error(_hsError && _hsError.stack ? _hsError.stack : _hsError); " +
            $"}} finally {{ console.log({markerLiteral} + process.cwd()); }} }})()";
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

        completion = new ProcessCommandCompletion(
            0,
            line[marker.Length..]);
        return true;
    }

    protected override string? NormalizeOutputLine(string line)
    {
        var normalized = line.TrimStart();
        while (normalized.StartsWith("> ", StringComparison.Ordinal)
               || normalized.StartsWith("... ", StringComparison.Ordinal))
        {
            normalized = normalized[(normalized.IndexOf(' ') + 1)..];
        }

        if (string.Equals(normalized, "undefined", StringComparison.Ordinal)
            || normalized.StartsWith("Welcome to Node.js", StringComparison.Ordinal)
            || normalized.StartsWith("Type \".help\"", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized;
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

        startInfo.ArgumentList.Add("--interactive");
        startInfo.Environment["NODE_NO_WARNINGS"] = "1";
        startInfo.Environment["NODE_DISABLE_COLORS"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";
        return startInfo;
    }
}
