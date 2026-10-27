using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed partial class DotNetToolchainCatalogService
{
    public const string DotNetSdkLibraryId = "csharp-dotnet-sdk";
    public const string DotNetCliLibraryId = "csharp-dotnet-cli";

    public static bool IsToolchainLibraryId(string? libraryId) =>
        string.Equals(
            libraryId,
            DotNetSdkLibraryId,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            libraryId,
            DotNetCliLibraryId,
            StringComparison.OrdinalIgnoreCase);

    private static readonly string[] NestedCommandParents =
    [
        "build-server",
        "new",
        "nuget",
        "package",
        "reference",
        "sdk",
        "solution",
        "tool",
        "workload"
    ];

    public IReadOnlyList<CommandLibrary> Discover()
    {
        var dotnetExecutable = FindDotNetExecutable();
        if (dotnetExecutable is null)
        {
            return [];
        }

        var activeVersion = RunDotNet(dotnetExecutable, "--version")
            .Output
            .SplitLines()
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();
        if (string.IsNullOrWhiteSpace(activeVersion))
        {
            return [];
        }

        var installedVersions = ParseInstalledSdkVersions(
            RunDotNet(dotnetExecutable, "--list-sdks").Output);
        if (installedVersions.Count == 0)
        {
            installedVersions.Add(activeVersion);
        }

        var rootHelp = RunDotNet(dotnetExecutable, "--help").CombinedOutput;
        var rootCommands = ParseRootSdkCommands(rootHelp);
        var sdkLibrary = CreateSdkLibrary(
            dotnetExecutable,
            activeVersion,
            installedVersions,
            rootCommands);
        var cliLibrary = CreateCliLibrary(
            dotnetExecutable,
            activeVersion,
            installedVersions);
        return [sdkLibrary, cliLibrary];
    }

    private static CommandLibrary CreateSdkLibrary(
        string dotnetExecutable,
        string activeVersion,
        IReadOnlyList<string> installedVersions,
        IReadOnlyList<HelpCommand> rootCommands)
    {
        var dotnetRoot = Path.GetDirectoryName(dotnetExecutable)
                         ?? string.Empty;
        var sdkDirectory = Path.Combine(
            dotnetRoot,
            "sdk",
            activeVersion);
        var versions = string.Join(", ", installedVersions);
        var library = new CommandLibrary
        {
            Id = DotNetSdkLibraryId,
            DisplayName = ".NET SDK",
            Description =
                $"Installed .NET SDK toolchain. Active SDK: {activeVersion}. " +
                $"Installed SDKs: {versions}. Command groups are discovered " +
                "from the active SDK's own help output.",
            Language = "C#",
            Category = string.Empty,
            Kind = CommandLibraryKind.Sdk,
            FrameworkName = ".NET SDK",
            FrameworkVersion = activeVersion,
            ContentVersion = activeVersion,
            ExecutionTarget = ExecutionTarget.PowerShell,
            IsExecutionSupported = true,
            ExecutionSupportMessage =
                "Runs the installed dotnet SDK through a PowerShell terminal.",
            IsStarterLibrary = false,
            CreateStandaloneProfile = false,
            Keywords =
            [
                "dotnet",
                ".NET SDK",
                "SDK",
                "MSBuild",
                "project tooling",
                activeVersion,
                .. installedVersions
            ],
            Origin = CommandLibraryOrigin.Runtime,
            SourcePath = Directory.Exists(sdkDirectory)
                ? sdkDirectory
                : dotnetRoot,
            CatalogPriority = 200
        };

        foreach (var entry in rootCommands)
        {
            library.Commands.Add(CreateSdkCommand(
                activeVersion,
                entry.Group,
                entry.Name,
                entry.Signature,
                entry.Description,
                requiresArguments: false));
        }

        var availableParents = rootCommands
            .Select(command => command.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in NestedCommandParents.Where(
                     availableParents.Contains))
        {
            var help = RunDotNet(
                dotnetExecutable,
                parent,
                "--help").CombinedOutput;
            foreach (var subcommand in ParseCommandSection(help))
            {
                var commandPath = $"{parent} {subcommand.Name}";
                library.Commands.Add(CreateSdkCommand(
                    activeVersion,
                    parent,
                    commandPath,
                    subcommand.Signature,
                    subcommand.Description,
                    subcommand.RequiresArguments));
            }
        }

        library.Commands = library.Commands
            .DistinctBy(
                command => command.Name,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command.Action, StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                command => command.ObjectName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        return library;
    }

    private static CommandLibrary CreateCliLibrary(
        string dotnetExecutable,
        string activeVersion,
        IReadOnlyList<string> installedVersions)
    {
        var library = new CommandLibrary
        {
            Id = DotNetCliLibraryId,
            DisplayName = ".NET CLI",
            Description =
                "The installed dotnet driver for environment inspection and " +
                "running compiled .NET applications. SDK project commands are " +
                "kept in the separate .NET SDK surface.",
            Language = "C#",
            Category = string.Empty,
            Kind = CommandLibraryKind.CommandLineTool,
            FrameworkName = ".NET CLI",
            FrameworkVersion = activeVersion,
            ContentVersion = activeVersion,
            ExecutionTarget = ExecutionTarget.PowerShell,
            IsExecutionSupported = true,
            ExecutionSupportMessage =
                "Runs the installed dotnet driver through a PowerShell terminal.",
            IsStarterLibrary = false,
            CreateStandaloneProfile = false,
            Keywords =
            [
                "dotnet",
                ".NET CLI",
                "command line",
                "host",
                "driver",
                activeVersion,
                .. installedVersions
            ],
            Origin = CommandLibraryOrigin.Runtime,
            SourcePath = dotnetExecutable,
            CatalogPriority = 210
        };

        library.Commands.AddRange(
        [
            CreateCliCommand(
                activeVersion,
                "Environment information",
                "--help",
                "Print the installed dotnet driver's command-line help."),
            CreateCliCommand(
                activeVersion,
                "Environment information",
                "--info",
                "Display detailed .NET SDK, runtime, workload, host, and environment information."),
            CreateCliCommand(
                activeVersion,
                "Environment information",
                "--list-runtimes",
                "List installed .NET runtimes for the selected architecture.",
                acceptsArguments: true),
            CreateCliCommand(
                activeVersion,
                "Environment information",
                "--list-sdks",
                "List installed .NET SDKs for the selected architecture.",
                acceptsArguments: true),
            CreateCliCommand(
                activeVersion,
                "Environment information",
                "--version",
                "Display the .NET SDK version selected for the current directory."),
            CreateCliCommand(
                activeVersion,
                "Application execution",
                "<application.dll>",
                "Run a compiled framework-dependent .NET application.",
                acceptsArguments: true,
                requiresApplicationPath: true),
            CreateCliCommand(
                activeVersion,
                "Application execution",
                "exec",
                "Run a compiled .NET application with explicit runtime options.",
                acceptsArguments: true,
                requiresApplicationPath: true)
        ]);
        return library;
    }

    private static CommandDefinition CreateSdkCommand(
        string activeVersion,
        string group,
        string commandPath,
        string signature,
        string description,
        bool requiresArguments)
    {
        var normalizedGroup = group switch
        {
            "SDK commands" => "SDK commands",
            "Additional commands from bundled tools" =>
                "Bundled SDK tools",
            _ => group
        };
        var objectName = commandPath.Contains(' ')
            ? commandPath[(commandPath.IndexOf(' ') + 1)..]
            : commandPath;
        return new CommandDefinition
        {
            Id = $"{DotNetSdkLibraryId}:{ToId(commandPath)}",
            CatalogId = DotNetSdkLibraryId,
            Name = $"dotnet {commandPath}",
            DisplayName = $"dotnet {commandPath}",
            Description = description,
            GuideDefinition =
                $"{description} This command was discovered from the " +
                $"installed .NET SDK {activeVersion} help output.",
            ModuleName = $".NET SDK {activeVersion}",
            CommandType = "CLI",
            PreferredShellKind = TerminalShellKind.PowerShell,
            Action = normalizedGroup,
            ActionDescription = normalizedGroup switch
            {
                "SDK commands" =>
                    "Top-level commands reported under SDK commands by the installed dotnet help.",
                "Bundled SDK tools" =>
                    "Additional commands reported as bundled tools by the installed dotnet help.",
                _ =>
                    $"Subcommands reported by dotnet {group} --help."
            },
            ObjectName = objectName,
            ObjectDescription = description,
            DocumentationUrl =
                "https://learn.microsoft.com/dotnet/core/tools/dotnet",
            Keywords =
            [
                "dotnet",
                ".NET SDK",
                "CLI",
                group,
                signature,
                activeVersion
            ],
            Template = $"dotnet {commandPath} {{arguments}}",
            ExecutionTarget = ExecutionTarget.PowerShell,
            CompositionMode = CommandCompositionMode.Statement,
            Parameters =
            [
                CreateArgumentsParameter(requiresArguments)
            ],
            Examples =
            [
                new CommandUsageExample
                {
                    Title = $"Run dotnet {commandPath}",
                    Code = $"dotnet {commandPath}",
                    Explanation =
                        $"Invokes the command exposed by .NET SDK {activeVersion}."
                }
            ]
        };
    }

    private static CommandDefinition CreateCliCommand(
        string activeVersion,
        string group,
        string operation,
        string description,
        bool acceptsArguments = false,
        bool requiresApplicationPath = false)
    {
        var applicationOperation = string.Equals(
            operation,
            "<application.dll>",
            StringComparison.Ordinal);
        var commandText = applicationOperation
            ? "dotnet"
            : $"dotnet {operation}";
        var template = requiresApplicationPath
            ? applicationOperation
                ? "dotnet {application} {arguments}"
                : "dotnet exec {application} {arguments}"
            : acceptsArguments
                ? $"{commandText} {{arguments}}"
                : commandText;
        var parameters = new List<CommandParameterDefinition>();
        if (requiresApplicationPath)
        {
            parameters.Add(new CommandParameterDefinition
            {
                Name = "application",
                DisplayName = "Application path",
                Placeholder = "application",
                Description =
                    "Path to the compiled .NET application DLL.",
                TypeName = "Path",
                IsRequired = true,
                IncludeByDefault = true
            });
        }

        if (acceptsArguments)
        {
            parameters.Add(CreateArgumentsParameter(isRequired: false));
        }

        return new CommandDefinition
        {
            Id = $"{DotNetCliLibraryId}:{ToId(operation)}",
            CatalogId = DotNetCliLibraryId,
            Name = applicationOperation
                ? "dotnet <application.dll>"
                : commandText,
            DisplayName = applicationOperation
                ? "dotnet <application.dll>"
                : commandText,
            Description = description,
            GuideDefinition = description,
            ModuleName = $".NET CLI {activeVersion}",
            CommandType = "CLI",
            PreferredShellKind = TerminalShellKind.PowerShell,
            Action = group,
            ActionDescription = group switch
            {
                "Environment information" =>
                    "Driver-level options that inspect the selected SDK, installed runtimes, and local .NET environment.",
                _ =>
                    "Driver-level forms that execute a compiled .NET application."
            },
            ObjectName = operation,
            ObjectDescription = description,
            DocumentationUrl =
                "https://learn.microsoft.com/dotnet/core/tools/dotnet",
            Keywords =
            [
                "dotnet",
                ".NET CLI",
                "driver",
                group,
                operation,
                activeVersion
            ],
            Template = template,
            ExecutionTarget = ExecutionTarget.PowerShell,
            CompositionMode = CommandCompositionMode.Statement,
            Parameters = parameters,
            Examples =
            [
                new CommandUsageExample
                {
                    Title = operation,
                    Code = commandText,
                    Explanation = description
                }
            ]
        };
    }

    private static CommandParameterDefinition CreateArgumentsParameter(
        bool isRequired) =>
        new()
        {
            Name = "arguments",
            DisplayName = "Arguments",
            Placeholder = "arguments",
            Description =
                "Additional positional arguments and options, entered exactly " +
                "as accepted by the installed command.",
            TypeName = "CLI text",
            IsRequired = isRequired,
            IncludeByDefault = isRequired
        };

    private static List<string> ParseInstalledSdkVersions(string output) =>
        output
            .SplitLines()
            .Select(line => line.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => version!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<HelpCommand> ParseRootSdkCommands(string help)
    {
        var commands = new List<HelpCommand>();
        string? group = null;
        foreach (var line in help.SplitLines())
        {
            var trimmed = line.Trim();
            if (string.Equals(
                    trimmed,
                    "SDK commands:",
                    StringComparison.OrdinalIgnoreCase))
            {
                group = "SDK commands";
                continue;
            }

            if (string.Equals(
                    trimmed,
                    "Additional commands from bundled tools:",
                    StringComparison.OrdinalIgnoreCase))
            {
                group = "Additional commands from bundled tools";
                continue;
            }

            if (trimmed.EndsWith(':') && !string.IsNullOrWhiteSpace(group))
            {
                group = null;
                continue;
            }

            if (group is null
                || !TryParseCommandLine(line, out var command))
            {
                continue;
            }

            commands.Add(command with { Group = group });
        }

        return commands;
    }

    private static List<HelpCommand> ParseCommandSection(string help)
    {
        var commands = new List<HelpCommand>();
        var inCommands = false;
        foreach (var line in help.SplitLines())
        {
            var trimmed = line.Trim();
            if (string.Equals(
                    trimmed,
                    "Commands:",
                    StringComparison.OrdinalIgnoreCase))
            {
                inCommands = true;
                continue;
            }

            if (inCommands && trimmed.EndsWith(':'))
            {
                break;
            }

            if (inCommands
                && TryParseCommandLine(line, out var command))
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    private static bool TryParseCommandLine(
        string line,
        out HelpCommand command)
    {
        command = default;
        var match = CommandLinePattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var signature = match.Groups["signature"].Value.Trim();
        var description = match.Groups["description"].Value.Trim();
        var name = signature
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.TrimEnd(',');
        if (string.IsNullOrWhiteSpace(name)
            || name.StartsWith('-')
            || string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        command = new HelpCommand(
            string.Empty,
            name,
            signature,
            description,
            signature.Contains('<', StringComparison.Ordinal));
        return true;
    }

    private static string? FindDotNetExecutable()
    {
        var candidates = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFiles),
                    "dotnet",
                    "dotnet.exe"),
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFilesX86),
                    "dotnet",
                    "dotnet.exe")
            }
            .Concat(
                (Environment.GetEnvironmentVariable("PATH")
                 ?? string.Empty)
                .Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Select(path => Path.Combine(
                    path.Trim('"'),
                    "dotnet.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static DotNetCommandResult RunDotNet(
        string dotnetExecutable,
        params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return default;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return default;
            }

            Task.WaitAll(outputTask, errorTask);
            return new DotNetCommandResult(
                outputTask.Result,
                errorTask.Result,
                process.ExitCode);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            return default;
        }
    }

    private static string ToId(string value) =>
        IdCharacterPattern().Replace(
            value.Trim().ToLowerInvariant(),
            "-").Trim('-');

    [GeneratedRegex(
        @"^[ \t]{2}(?<signature>\S(?:.*?\S)?)\s{2,}(?<description>\S.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommandLinePattern();

    [GeneratedRegex(
        @"[^a-z0-9]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdCharacterPattern();

    private readonly record struct HelpCommand(
        string Group,
        string Name,
        string Signature,
        string Description,
        bool RequiresArguments);

    private readonly record struct DotNetCommandResult(
        string Output,
        string Error,
        int ExitCode)
    {
        public string CombinedOutput =>
            string.Join(
                Environment.NewLine,
                new[] { Output, Error }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}

file static class DotNetToolchainStringExtensions
{
    public static string[] SplitLines(this string? value) =>
        (value ?? string.Empty).Split(
            ["\r\n", "\n", "\r"],
            StringSplitOptions.RemoveEmptyEntries);
}
