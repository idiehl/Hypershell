using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed class CSharpTerminalSession : ITerminalSession
{
    private const string ProjectName = "HypershellSnippet";
    private readonly string _dotnetExecutable;

    public CSharpTerminalSession(TerminalProfile profile)
    {
        _dotnetExecutable = profile.ExecutablePath;
    }

    public async Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new TerminalExecutionResult
        {
            WorkingDirectory = ResolveWorkingDirectory(workingDirectory)
        };
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            "Hypershell",
            "csharp",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(projectDirectory);
        try
        {
            var projectPath = Path.Combine(projectDirectory, $"{ProjectName}.csproj");
            var sourcePath = Path.Combine(projectDirectory, "Program.cs");
            await File.WriteAllTextAsync(
                projectPath,
                BuildProjectFile(),
                Encoding.UTF8,
                cancellationToken);
            await File.WriteAllTextAsync(
                sourcePath,
                BuildProgram(command),
                Encoding.UTF8,
                cancellationToken);

            var build = await RunProcessAsync(
                _dotnetExecutable,
                [
                    "build",
                    projectPath,
                    "--configuration",
                    "Release",
                    "--nologo",
                    "--verbosity",
                    "quiet",
                    "--disable-build-servers"
                ],
                projectDirectory,
                cancellationToken);
            if (build.ExitCode != 0)
            {
                AddLines(result.Errors, build.StandardOutput);
                AddLines(result.Errors, build.StandardError);
                if (result.Errors.Count == 0)
                {
                    result.Errors.Add($"C# compilation failed with exit code {build.ExitCode}.");
                }

                return result;
            }

            var assemblyPath = Path.Combine(
                projectDirectory,
                "bin",
                "Release",
                "net10.0",
                $"{ProjectName}.dll");
            var execution = await RunProcessAsync(
                _dotnetExecutable,
                [assemblyPath],
                result.WorkingDirectory,
                cancellationToken);
            AddLines(result.Output, execution.StandardOutput);
            AddLines(result.Errors, execution.StandardError);
            if (execution.ExitCode != 0 && result.Errors.Count == 0)
            {
                result.Errors.Add($"C# snippet exited with code {execution.ExitCode}.");
            }
            else if (execution.ExitCode == 0 && result.Output.Count == 0)
            {
                result.Output.Add("C# snippet completed successfully.");
            }

            return result;
        }
        finally
        {
            TryDeleteDirectory(projectDirectory);
        }
    }

    public void Dispose()
    {
    }

    private static string BuildProjectFile() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <LangVersion>14.0</LangVersion>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
            <NuGetAudit>false</NuGetAudit>
            <AssemblyName>HypershellSnippet</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    private static string BuildProgram(string command) =>
        $$"""
        #nullable enable
        using System;
        using System.Buffers;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Globalization;
        using System.IO;
        using System.IO.Compression;
        using System.Linq;
        using System.Net;
        using System.Net.Http;
        using System.Net.Http.Headers;
        using System.Net.Http.Json;
        using System.Net.Sockets;
        using System.Reflection;
        using System.Runtime.InteropServices;
        using System.Security.Cryptography;
        using System.Text;
        using System.Text.Json;
        using System.Text.RegularExpressions;
        using System.Threading;
        using System.Threading.Channels;
        using System.Threading.Tasks;

        {{command}}
        """;

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {Path.GetFileName(executable)}.");
        }

        try
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(
                process.ExitCode,
                await standardOutputTask,
                await standardErrorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static string ResolveWorkingDirectory(string? workingDirectory) =>
        !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : Environment.CurrentDirectory;

    private static void AddLines(ICollection<string> destination, string text)
    {
        foreach (var line in text.Split(
                     ["\r\n", "\n"],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            destination.Add(line);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // The OS can briefly retain compiler files. They are isolated in the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup failure must not hide the snippet result.
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
