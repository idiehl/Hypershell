using Hypershell.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed partial class CppCompiledLanguageAdapter : ICompiledLanguageAdapter
{
    private const int MaximumSourceCharacters = 8 * 1024 * 1024;
    private static readonly TimeSpan CompilationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(30);
    private readonly BoundedProcessRunner _processRunner;

    public CppCompiledLanguageAdapter(BoundedProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new BoundedProcessRunner();
    }

    public string LanguageId => "cpp";

    public Task<CompiledLanguageExecutionResult> ExecuteDocumentAsync(
        CompiledLanguageExecutionRequest request,
        DiscoveredCompiledLanguageToolchain toolchain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var languageId = EditorLanguageCatalog.GetLanguage(
            request.LanguageId).Id;
        if (!string.Equals(
                languageId,
                LanguageId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Rejected(
                "The C++ adapter received a document in another language."));
        }

        var extension = Path.GetExtension(request.FilePath);
        if (string.IsNullOrWhiteSpace(request.FilePath)
            || !new[] { ".cpp", ".cc", ".cxx" }.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(Rejected(
                "Save the C++ document with a .cpp, .cc, or .cxx extension before running it."));
        }

        if (request.Content.Length > MaximumSourceCharacters)
        {
            return Task.FromResult(Rejected(
                "The C++ source exceeds the 8 MiB document-execution limit."));
        }

        var sourcePath = Path.GetFullPath(request.FilePath);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)
                              ?? Environment.CurrentDirectory;
        return ExecuteSourceAsync(
            request.Content,
            Path.GetFileName(sourcePath),
            ResolveWorkingDirectory(request.WorkingDirectory, sourceDirectory),
            sourceDirectory,
            toolchain,
            cancellationToken);
    }

    public Task<CompiledLanguageExecutionResult> ExecuteSnippetAsync(
        string source,
        string? workingDirectory,
        DiscoveredCompiledLanguageToolchain toolchain,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Task.FromResult(Rejected(
                "Enter a C++ statement before running it."));
        }

        if (source.Length > MaximumSourceCharacters)
        {
            return Task.FromResult(Rejected(
                "The C++ snippet exceeds the 8 MiB execution limit."));
        }

        var effectiveWorkingDirectory = ResolveWorkingDirectory(
            workingDirectory,
            Environment.CurrentDirectory);
        return ExecuteSourceAsync(
            BuildSnippetSource(source),
            "HypershellSnippet.cpp",
            effectiveWorkingDirectory,
            effectiveWorkingDirectory,
            toolchain,
            cancellationToken);
    }

    public static DiscoveredCompiledLanguageToolchain CreateToolchain(
        TerminalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var compilerPath = string.IsNullOrWhiteSpace(
            profile.CompilerExecutablePath)
            ? profile.ExecutablePath
            : profile.CompilerExecutablePath;
        var compilerName = Path.GetFileNameWithoutExtension(compilerPath);
        var toolchainId = compilerName.Equals(
            "cl",
            StringComparison.OrdinalIgnoreCase)
            ? "cpp-msvc"
            : compilerName.Contains(
                "g++",
                StringComparison.OrdinalIgnoreCase)
                ? "cpp-gcc"
                : "cpp-clang";
        var definition = CompiledLanguageToolchainRegistry.Definitions.Single(
            candidate => candidate.Id == toolchainId);
        var toolDefinition = LanguageToolRegistry.Definitions.Single(
            candidate => candidate.Id == definition.CompilerToolId);
        return new DiscoveredCompiledLanguageToolchain
        {
            Definition = definition,
            Compiler = new DiscoveredLanguageTool
            {
                Definition = toolDefinition,
                Availability = File.Exists(compilerPath)
                    ? LanguageToolAvailability.Available
                    : LanguageToolAvailability.Missing,
                ExecutablePath = compilerPath,
                StatusMessage = profile.AvailabilityMessage
            },
            StatusMessage = profile.AvailabilityMessage
        };
    }

    private async Task<CompiledLanguageExecutionResult> ExecuteSourceAsync(
        string content,
        string fileName,
        string workingDirectory,
        string includeDirectory,
        DiscoveredCompiledLanguageToolchain toolchain,
        CancellationToken cancellationToken)
    {
        if (toolchain.Compiler?.IsAvailable != true)
        {
            return new CompiledLanguageExecutionResult
            {
                Outcome = CompiledLanguageExecutionOutcome.Unavailable,
                LanguageId = LanguageId,
                ToolchainId = toolchain.Definition.Id,
                WorkingDirectory = workingDirectory,
                Message = toolchain.StatusMessage
            };
        }

        var executionRoot = Path.Combine(
            Path.GetTempPath(),
            "Hypershell",
            "compiled",
            "cpp",
            Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(executionRoot, "source");
        var outputDirectory = Path.Combine(executionRoot, "output");
        var sourcePath = Path.Combine(sourceDirectory, fileName);
        var executablePath = Path.Combine(
            outputDirectory,
            OperatingSystem.IsWindows()
                ? "hypershell-program.exe"
                : "hypershell-program");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await File.WriteAllTextAsync(
                sourcePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            var arguments = CreateCompilerArguments(
                toolchain,
                sourcePath,
                executablePath,
                outputDirectory,
                includeDirectory);
            var compilation = await _processRunner.RunAsync(
                new BoundedProcessRequest
                {
                    ExecutablePath = toolchain.Compiler.ExecutablePath,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    Timeout = CompilationTimeout,
                    MaximumStandardInputCharacters = 0,
                    MaximumStandardOutputCharacters = 1024 * 1024,
                    MaximumStandardErrorCharacters = 1024 * 1024
                },
                cancellationToken);
            if (compilation.TimedOut)
            {
                return Result(
                    CompiledLanguageExecutionOutcome.TimedOut,
                    toolchain,
                    workingDirectory,
                    "C++ compilation timed out.",
                    diagnostics: ReadLines(
                        compilation.StandardError,
                        compilation.FailureMessage));
            }

            if (!compilation.Started || compilation.OutputLimitExceeded)
            {
                return Result(
                    CompiledLanguageExecutionOutcome.Rejected,
                    toolchain,
                    workingDirectory,
                    "C++ compilation could not be started safely.",
                    diagnostics: ReadLines(
                        compilation.StandardError,
                        compilation.FailureMessage));
            }

            if (compilation.ExitCode != 0 || !File.Exists(executablePath))
            {
                return Result(
                    CompiledLanguageExecutionOutcome.CompilationFailed,
                    toolchain,
                    workingDirectory,
                    "C++ compilation failed. No program was launched.",
                    diagnostics: ReadLines(
                        compilation.StandardError,
                        compilation.StandardOutput));
            }

            var execution = await _processRunner.RunAsync(
                new BoundedProcessRequest
                {
                    ExecutablePath = executablePath,
                    WorkingDirectory = workingDirectory,
                    Timeout = ExecutionTimeout,
                    MaximumStandardInputCharacters = 0,
                    MaximumStandardOutputCharacters = 1024 * 1024,
                    MaximumStandardErrorCharacters = 1024 * 1024
                },
                cancellationToken);
            if (execution.TimedOut)
            {
                return Result(
                    CompiledLanguageExecutionOutcome.TimedOut,
                    toolchain,
                    workingDirectory,
                    "The compiled C++ program timed out.",
                    diagnostics: ReadLines(execution.StandardError));
            }

            if (!execution.Started || execution.OutputLimitExceeded)
            {
                return Result(
                    CompiledLanguageExecutionOutcome.Rejected,
                    toolchain,
                    workingDirectory,
                    "The compiled C++ program could not be launched safely.",
                    diagnostics: ReadLines(
                        execution.StandardError,
                        execution.FailureMessage));
            }

            var outcome = execution.ExitCode == 0
                ? CompiledLanguageExecutionOutcome.Succeeded
                : CompiledLanguageExecutionOutcome.RuntimeFailed;
            return Result(
                outcome,
                toolchain,
                workingDirectory,
                outcome == CompiledLanguageExecutionOutcome.Succeeded
                    ? "C++ build and execution completed."
                    : $"C++ exited with code {execution.ExitCode}.",
                output: ReadLines(execution.StandardOutput),
                warnings: ReadLines(
                    compilation.StandardError,
                    compilation.StandardOutput),
                diagnostics: ReadLines(execution.StandardError));
        }
        finally
        {
            try
            {
                if (Directory.Exists(executionRoot))
                {
                    Directory.Delete(executionRoot, recursive: true);
                }
            }
            catch
            {
                // Temporary build cleanup is best effort. No user source is
                // ever written beneath or deleted from this directory.
            }
        }
    }

    private static IReadOnlyList<string> CreateCompilerArguments(
        DiscoveredCompiledLanguageToolchain toolchain,
        string sourcePath,
        string executablePath,
        string outputDirectory,
        string includeDirectory)
    {
        if (toolchain.Definition.Id == "cpp-msvc")
        {
            return
            [
                "/nologo",
                "/std:c++20",
                "/EHsc",
                "/utf-8",
                $"/I{includeDirectory}",
                $"/Fo:{Path.Combine(outputDirectory, "hypershell-program.obj")}",
                $"/Fe:{executablePath}",
                sourcePath
            ];
        }

        return
        [
            "-std=c++20",
            "-Wall",
            "-Wextra",
            "-pedantic",
            "-I",
            includeDirectory,
            sourcePath,
            "-o",
            executablePath
        ];
    }

    private static string BuildSnippetSource(string source)
    {
        if (MainFunctionPattern().IsMatch(source))
        {
            return source;
        }

        var preamble = new List<string>();
        var statements = new List<string>();
        foreach (var line in source.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n'))
        {
            if (line.TrimStart().StartsWith('#'))
            {
                preamble.Add(line);
            }
            else
            {
                statements.Add(line);
            }
        }

        var builder = new StringBuilder();
        foreach (var line in preamble)
        {
            builder.AppendLine(line);
        }

        builder.AppendLine("int main()");
        builder.AppendLine("{");
        foreach (var line in statements)
        {
            builder.Append("    ").AppendLine(line);
        }

        builder.AppendLine("    return 0;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string ResolveWorkingDirectory(
        string? requested,
        string fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(requested)
                && Directory.Exists(requested))
            {
                return Path.GetFullPath(requested);
            }
        }
        catch
        {
        }

        return Path.GetFullPath(fallback);
    }

    private CompiledLanguageExecutionResult Rejected(string message) =>
        new()
        {
            Outcome = CompiledLanguageExecutionOutcome.Rejected,
            LanguageId = LanguageId,
            Message = message
        };

    private CompiledLanguageExecutionResult Result(
        CompiledLanguageExecutionOutcome outcome,
        DiscoveredCompiledLanguageToolchain toolchain,
        string workingDirectory,
        string message,
        IReadOnlyList<string>? output = null,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? diagnostics = null) =>
        new()
        {
            Outcome = outcome,
            LanguageId = LanguageId,
            ToolchainId = toolchain.Definition.Id,
            EntryPoint = "main",
            WorkingDirectory = workingDirectory,
            Output = output ?? [],
            Warnings = warnings ?? [],
            Diagnostics = diagnostics ?? [],
            Message = message
        };

    private static IReadOnlyList<string> ReadLines(params string[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries))
            .ToList();

    [GeneratedRegex(@"\bint\s+main\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex MainFunctionPattern();
}
