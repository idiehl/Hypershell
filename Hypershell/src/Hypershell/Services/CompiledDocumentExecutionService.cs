using Hypershell.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public interface ICompiledLanguageAdapter
{
    string LanguageId { get; }

    Task<CompiledLanguageExecutionResult> ExecuteDocumentAsync(
        CompiledLanguageExecutionRequest request,
        DiscoveredCompiledLanguageToolchain toolchain,
        CancellationToken cancellationToken = default);
}

public sealed class CompiledDocumentExecutionService
{
    private readonly IReadOnlyDictionary<string, ICompiledLanguageAdapter> _adapters;
    private readonly CompiledLanguageToolchainDiscoveryService _toolchainDiscovery;

    public CompiledDocumentExecutionService(
        IEnumerable<ICompiledLanguageAdapter>? adapters = null,
        CompiledLanguageToolchainDiscoveryService? toolchainDiscovery = null)
    {
        _adapters = (adapters ??
                [new JavaCompiledLanguageAdapter(), new CppCompiledLanguageAdapter()])
            .ToDictionary(
                adapter => adapter.LanguageId,
                StringComparer.OrdinalIgnoreCase);
        _toolchainDiscovery = toolchainDiscovery
                              ?? new CompiledLanguageToolchainDiscoveryService();
    }

    public bool SupportsLanguage(string? languageId)
    {
        var normalized = EditorLanguageCatalog.GetLanguage(languageId).Id;
        return _adapters.ContainsKey(normalized);
    }

    public Task<CompiledLanguageExecutionResult> ExecuteAsync(
        CompiledLanguageExecutionRequest request,
        LanguageToolCatalogSnapshot catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        var languageId = EditorLanguageCatalog.GetLanguage(request.LanguageId).Id;
        if (!_adapters.TryGetValue(languageId, out var adapter))
        {
            return Task.FromResult(new CompiledLanguageExecutionResult
            {
                Outcome = CompiledLanguageExecutionOutcome.Rejected,
                LanguageId = languageId,
                Message = $"Hypershell does not have a compiled-document adapter for {languageId}."
            });
        }

        var toolchain = _toolchainDiscovery.FindForLanguage(languageId, catalog);
        if (toolchain?.IsAvailable != true)
        {
            return Task.FromResult(new CompiledLanguageExecutionResult
            {
                Outcome = CompiledLanguageExecutionOutcome.Unavailable,
                LanguageId = languageId,
                ToolchainId = toolchain?.Definition.Id ?? string.Empty,
                Message = toolchain?.StatusMessage
                          ?? $"No compiled-language toolchain is registered for {languageId}."
            });
        }

        return adapter.ExecuteDocumentAsync(
            request,
            toolchain,
            cancellationToken);
    }
}

public sealed partial class JavaCompiledLanguageAdapter : ICompiledLanguageAdapter
{
    private const int MaximumSourceCharacters = 8 * 1024 * 1024;
    private static readonly TimeSpan CompilationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(30);
    private readonly BoundedProcessRunner _processRunner;

    public JavaCompiledLanguageAdapter(BoundedProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new BoundedProcessRunner();
    }

    public string LanguageId => "java";

    public Task<CompiledLanguageExecutionResult> ExecuteDocumentAsync(
        CompiledLanguageExecutionRequest request,
        DiscoveredCompiledLanguageToolchain toolchain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(
                EditorLanguageCatalog.GetLanguage(request.LanguageId).Id,
                LanguageId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Rejected(
                "The Java adapter received a document in another language."));
        }

        if (string.IsNullOrWhiteSpace(request.FilePath)
            || !string.Equals(
                Path.GetExtension(request.FilePath),
                ".java",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Rejected(
                "Save the Java document with a .java extension before running it."));
        }

        if (request.Content.Length > MaximumSourceCharacters)
        {
            return Task.FromResult(Rejected(
                "The Java source exceeds the 8 MiB document-execution limit."));
        }

        var fullPath = Path.GetFullPath(request.FilePath);
        var fileName = Path.GetFileName(fullPath);
        var packageName = ReadPackageName(request.Content);
        var entryPoint = ResolveEntryPoint(
            request.Content,
            Path.GetFileNameWithoutExtension(fileName),
            packageName);
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            return Task.FromResult(Rejected(
                "Hypershell could not find a top-level Java class, record, or enum to launch."));
        }

        var sourceDirectory = Path.GetDirectoryName(fullPath)
                              ?? Environment.CurrentDirectory;
        var sourceRoot = ResolveSourceRoot(sourceDirectory, packageName);
        return ExecuteSourceAsync(
            request.Content,
            fileName,
            packageName,
            sourceRoot,
            entryPoint,
            ResolveWorkingDirectory(request.WorkingDirectory, sourceDirectory),
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
            return Task.FromResult(Rejected("Enter a Java statement before running it."));
        }

        if (source.Length > MaximumSourceCharacters)
        {
            return Task.FromResult(Rejected(
                "The Java snippet exceeds the 8 MiB execution limit."));
        }

        var effectiveWorkingDirectory = ResolveWorkingDirectory(
            workingDirectory,
            Environment.CurrentDirectory);
        return ExecuteSourceAsync(
            BuildSnippetSource(source),
            "HypershellSnippet.java",
            string.Empty,
            effectiveWorkingDirectory,
            "HypershellSnippet",
            effectiveWorkingDirectory,
            toolchain,
            cancellationToken);
    }

    public static DiscoveredCompiledLanguageToolchain CreateToolchain(
        TerminalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var definition = CompiledLanguageToolchainRegistry.Definitions.Single(candidate =>
            candidate.LanguageId == "java");
        var compilerPath = string.IsNullOrWhiteSpace(profile.CompilerExecutablePath)
            ? profile.ExecutablePath
            : profile.CompilerExecutablePath;
        var runtimePath = profile.RuntimeExecutablePath;
        return new DiscoveredCompiledLanguageToolchain
        {
            Definition = definition,
            Compiler = CreateProfileTool(
                definition.CompilerToolId,
                "Java compiler",
                LanguageToolKind.Compiler,
                compilerPath),
            Runtime = CreateProfileTool(
                definition.RuntimeToolId,
                "Java runtime",
                LanguageToolKind.Runtime,
                runtimePath),
            StatusMessage = profile.AvailabilityMessage
        };
    }

    private async Task<CompiledLanguageExecutionResult> ExecuteSourceAsync(
        string content,
        string fileName,
        string packageName,
        string additionalSourceRoot,
        string entryPoint,
        string workingDirectory,
        DiscoveredCompiledLanguageToolchain toolchain,
        CancellationToken cancellationToken)
    {
        if (toolchain.Compiler?.IsAvailable != true
            || toolchain.Runtime?.IsAvailable != true)
        {
            return Unavailable(toolchain);
        }

        var executionRoot = Path.Combine(
            Path.GetTempPath(),
            "Hypershell",
            "compiled",
            "java",
            Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(executionRoot, "source");
        var outputDirectory = Path.Combine(executionRoot, "classes");
        var packageDirectory = string.IsNullOrWhiteSpace(packageName)
            ? sourceRoot
            : Path.Combine(sourceRoot, packageName.Replace('.', Path.DirectorySeparatorChar));
        var sourcePath = Path.Combine(packageDirectory, fileName);
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await File.WriteAllTextAsync(
                sourcePath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            var compilerArguments = new List<string>(
                toolchain.Compiler.Definition.Arguments)
            {
                "-encoding",
                "UTF-8",
                "--release",
                "17",
                "-proc:none",
                "-d",
                outputDirectory,
                "-sourcepath",
                BuildSourcePath(sourceRoot, additionalSourceRoot),
                sourcePath
            };
            var compilation = await _processRunner.RunAsync(
                new BoundedProcessRequest
                {
                    ExecutablePath = toolchain.Compiler.ExecutablePath,
                    Arguments = compilerArguments,
                    WorkingDirectory = workingDirectory,
                    Timeout = CompilationTimeout,
                    MaximumStandardInputCharacters = 0,
                    MaximumStandardOutputCharacters = 1024 * 1024,
                    MaximumStandardErrorCharacters = 1024 * 1024
                },
                cancellationToken);
            if (compilation.TimedOut)
            {
                return TimedOut(toolchain, entryPoint, workingDirectory, compilation);
            }

            if (!compilation.Started || compilation.OutputLimitExceeded)
            {
                return RejectedProcess(
                    toolchain,
                    entryPoint,
                    workingDirectory,
                    compilation,
                    "Java compilation could not be started safely.");
            }

            if (compilation.ExitCode != 0)
            {
                return new CompiledLanguageExecutionResult
                {
                    Outcome = CompiledLanguageExecutionOutcome.CompilationFailed,
                    LanguageId = LanguageId,
                    ToolchainId = toolchain.Definition.Id,
                    EntryPoint = entryPoint,
                    WorkingDirectory = workingDirectory,
                    Diagnostics = ReadLines(
                        compilation.StandardError,
                        compilation.StandardOutput),
                    Message = "Java compilation failed. No program was launched."
                };
            }

            var runtimeArguments = new List<string>(
                toolchain.Runtime.Definition.Arguments)
            {
                "-cp",
                outputDirectory,
                entryPoint
            };
            var execution = await _processRunner.RunAsync(
                new BoundedProcessRequest
                {
                    ExecutablePath = toolchain.Runtime.ExecutablePath,
                    Arguments = runtimeArguments,
                    WorkingDirectory = workingDirectory,
                    Timeout = ExecutionTimeout,
                    MaximumStandardInputCharacters = 0,
                    MaximumStandardOutputCharacters = 1024 * 1024,
                    MaximumStandardErrorCharacters = 1024 * 1024
                },
                cancellationToken);
            if (execution.TimedOut)
            {
                return TimedOut(toolchain, entryPoint, workingDirectory, execution);
            }

            if (!execution.Started || execution.OutputLimitExceeded)
            {
                return RejectedProcess(
                    toolchain,
                    entryPoint,
                    workingDirectory,
                    execution,
                    "The compiled Java program could not be launched safely.");
            }

            var output = ReadLines(execution.StandardOutput);
            var diagnostics = ReadLines(execution.StandardError);
            if (execution.ExitCode != 0)
            {
                return new CompiledLanguageExecutionResult
                {
                    Outcome = CompiledLanguageExecutionOutcome.RuntimeFailed,
                    LanguageId = LanguageId,
                    ToolchainId = toolchain.Definition.Id,
                    EntryPoint = entryPoint,
                    WorkingDirectory = workingDirectory,
                    Output = output,
                    Diagnostics = diagnostics,
                    Message = $"Java exited with code {execution.ExitCode}."
                };
            }

            return new CompiledLanguageExecutionResult
            {
                Outcome = CompiledLanguageExecutionOutcome.Succeeded,
                LanguageId = LanguageId,
                ToolchainId = toolchain.Definition.Id,
                EntryPoint = entryPoint,
                WorkingDirectory = workingDirectory,
                Output = output.Count == 0
                    ? ["Java program completed successfully."]
                    : output,
                Warnings = ReadLines(compilation.StandardError),
                Diagnostics = diagnostics,
                Message = $"Java compiled and ran {entryPoint}."
            };
        }
        finally
        {
            TryDeleteExecutionRoot(executionRoot);
        }
    }

    private static string BuildSnippetSource(string source) =>
        $$"""
        import java.io.*;
        import java.net.*;
        import java.net.http.*;
        import java.nio.charset.StandardCharsets;
        import java.nio.file.*;
        import java.time.*;
        import java.util.*;
        import java.util.concurrent.*;
        import java.util.function.*;
        import java.util.stream.*;

        public final class HypershellSnippet {
            public static void main(String[] args) throws Exception {
                {{source}}
            }
        }
        """;

    private static DiscoveredLanguageTool CreateProfileTool(
        string id,
        string displayName,
        LanguageToolKind kind,
        string executablePath) =>
        new()
        {
            Definition = new LanguageToolDefinition
            {
                Id = id,
                DisplayName = displayName,
                Kind = kind,
                LanguageIds = ["java"],
                ExecutableCandidates = [executablePath]
            },
            Availability = File.Exists(executablePath)
                ? LanguageToolAvailability.Available
                : LanguageToolAvailability.Missing,
            ExecutablePath = executablePath,
            StatusMessage = File.Exists(executablePath)
                ? $"Detected {executablePath}"
                : $"The configured {displayName.ToLowerInvariant()} was not found."
        };

    private static string ReadPackageName(string source)
    {
        var match = PackageDeclarationRegex().Match(source);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ResolveEntryPoint(
        string source,
        string fileBaseName,
        string packageName)
    {
        var typeNames = TopLevelTypeRegex()
            .Matches(source)
            .Select(match => match.Groups[1].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var selected = typeNames.FirstOrDefault(name => string.Equals(
                           name,
                           fileBaseName,
                           StringComparison.Ordinal))
                       ?? typeNames.FirstOrDefault();
        return string.IsNullOrWhiteSpace(selected)
            ? string.Empty
            : string.IsNullOrWhiteSpace(packageName)
                ? selected
                : $"{packageName}.{selected}";
    }

    private static string ResolveSourceRoot(
        string sourceDirectory,
        string packageName)
    {
        var root = Path.GetFullPath(sourceDirectory);
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return root;
        }

        var segments = packageName.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cursor = new DirectoryInfo(root);
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            if (!string.Equals(
                    cursor.Name,
                    segments[index],
                    StringComparison.OrdinalIgnoreCase)
                || cursor.Parent is null)
            {
                return root;
            }

            cursor = cursor.Parent;
        }

        return cursor.FullName;
    }

    private static string ResolveWorkingDirectory(
        string? requested,
        string fallback) =>
        !string.IsNullOrWhiteSpace(requested) && Directory.Exists(requested)
            ? Path.GetFullPath(requested)
            : Directory.Exists(fallback)
                ? Path.GetFullPath(fallback)
                : Environment.CurrentDirectory;

    private static string BuildSourcePath(params string[] roots) =>
        string.Join(
            Path.PathSeparator,
            roots
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ReadLines(params string[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries))
            .ToList();

    private static CompiledLanguageExecutionResult Rejected(string message) =>
        new()
        {
            Outcome = CompiledLanguageExecutionOutcome.Rejected,
            LanguageId = "java",
            Message = message
        };

    private static CompiledLanguageExecutionResult Unavailable(
        DiscoveredCompiledLanguageToolchain toolchain) =>
        new()
        {
            Outcome = CompiledLanguageExecutionOutcome.Unavailable,
            LanguageId = "java",
            ToolchainId = toolchain.Definition.Id,
            Message = toolchain.StatusMessage
        };

    private static CompiledLanguageExecutionResult TimedOut(
        DiscoveredCompiledLanguageToolchain toolchain,
        string entryPoint,
        string workingDirectory,
        BoundedProcessResult process) =>
        new()
        {
            Outcome = CompiledLanguageExecutionOutcome.TimedOut,
            LanguageId = "java",
            ToolchainId = toolchain.Definition.Id,
            EntryPoint = entryPoint,
            WorkingDirectory = workingDirectory,
            Message = process.FailureMessage
        };

    private static CompiledLanguageExecutionResult RejectedProcess(
        DiscoveredCompiledLanguageToolchain toolchain,
        string entryPoint,
        string workingDirectory,
        BoundedProcessResult process,
        string fallbackMessage) =>
        new()
        {
            Outcome = CompiledLanguageExecutionOutcome.Rejected,
            LanguageId = "java",
            ToolchainId = toolchain.Definition.Id,
            EntryPoint = entryPoint,
            WorkingDirectory = workingDirectory,
            Message = string.IsNullOrWhiteSpace(process.FailureMessage)
                ? fallbackMessage
                : process.FailureMessage
        };

    private static void TryDeleteExecutionRoot(string path)
    {
        try
        {
            var expectedRoot = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "Hypershell",
                "compiled",
                "java"));
            var candidate = Path.GetFullPath(path);
            if (candidate.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(candidate))
            {
                Directory.Delete(candidate, recursive: true);
            }
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            // A compiler can briefly retain a class file. The build directory
            // is isolated under the application temp root and can be reclaimed later.
        }
    }

    [GeneratedRegex(
        @"(?m)^\s*package\s+([A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\s*;")]
    private static partial Regex PackageDeclarationRegex();

    [GeneratedRegex(
        @"(?m)^\s*(?:(?:public|protected|private|abstract|final|sealed|non-sealed|static|strictfp)\s+)*(?:class|record|enum)\s+([A-Za-z_$][\w$]*)\b")]
    private static partial Regex TopLevelTypeRegex();
}
