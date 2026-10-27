using Hypershell.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public static class TerminalProfileService
{
    public const int CurrentVersion = 7;

    public static void ReconcileProfiles(AppSettings settings)
    {
        settings.Terminal ??= new TerminalSettings();
        settings.TerminalProfiles ??= [];

        var existingProfiles = settings.TerminalProfiles.ToList();
        foreach (var profile in existingProfiles)
        {
            profile.Settings ??= settings.Terminal.Clone();
            profile.Arguments ??= [];
        }

        var discoveredProfiles = DiscoverBuiltInProfiles(settings.Terminal);
        var orderedProfiles = new List<TerminalProfile>();
        foreach (var discoveredProfile in discoveredProfiles)
        {
            var existingProfile = existingProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, discoveredProfile.Id, StringComparison.OrdinalIgnoreCase));
            if (existingProfile is null)
            {
                orderedProfiles.Add(discoveredProfile);
                continue;
            }

            MergeDiscoveredProfile(existingProfile, discoveredProfile);
            orderedProfiles.Add(existingProfile);
        }

        foreach (var customProfile in existingProfiles.Where(profile => !profile.IsBuiltIn))
        {
            customProfile.IsAvailable = customProfile.ShellKind switch
            {
                TerminalShellKind.PowerShell or TerminalShellKind.DeveloperPowerShell => true,
                TerminalShellKind.AzureCloudShell => false,
                _ => ResolveExecutable(customProfile.ExecutablePath) is not null
            };
            customProfile.AvailabilityMessage = customProfile.IsAvailable
                ? "Custom shell profile"
                : "The configured shell executable could not be found.";
            orderedProfiles.Add(customProfile);
        }

        foreach (var profile in orderedProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.TabTitleTemplate))
            {
                profile.TabTitleTemplate = $"{profile.Name} {{n}}";
            }

            if (string.IsNullOrWhiteSpace(profile.TabColor))
            {
                profile.TabColor = "#28313D";
            }
        }

        settings.TerminalProfiles = new ObservableCollection<TerminalProfile>(orderedProfiles);
        var powerShellProfile = settings.TerminalProfiles.First(profile => profile.Id == "powershell");
        settings.Terminal = powerShellProfile.Settings;
        settings.TerminalProfilesVersion = CurrentVersion;

        if (!settings.TerminalProfiles.Any(profile =>
                profile.IsAvailable
                && string.Equals(profile.Id, settings.ActiveTerminalProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.ActiveTerminalProfileId = powerShellProfile.Id;
        }
    }

    public static string? ResolveExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        var expandedExecutable = Environment.ExpandEnvironmentVariables(executable.Trim('"'));
        if (Path.IsPathRooted(expandedExecutable))
        {
            return File.Exists(expandedExecutable) ? Path.GetFullPath(expandedExecutable) : null;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), expandedExecutable);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries and continue discovery.
            }
        }

        return null;
    }

    private static void MergeDiscoveredProfile(
        TerminalProfile existingProfile,
        TerminalProfile discoveredProfile)
    {
        existingProfile.Name = discoveredProfile.Name;
        existingProfile.ShellKind = discoveredProfile.ShellKind;
        existingProfile.ExecutablePath = discoveredProfile.ExecutablePath;
        existingProfile.CompilerExecutablePath =
            discoveredProfile.CompilerExecutablePath;
        existingProfile.RuntimeExecutablePath =
            discoveredProfile.RuntimeExecutablePath;
        existingProfile.Arguments = [.. discoveredProfile.Arguments];
        existingProfile.Distribution = discoveredProfile.Distribution;
        existingProfile.InitializationCommand = discoveredProfile.InitializationCommand;
        existingProfile.IsBuiltIn = true;
        existingProfile.IsAvailable = discoveredProfile.IsAvailable;
        existingProfile.AvailabilityMessage = discoveredProfile.AvailabilityMessage;
        existingProfile.Settings ??= discoveredProfile.Settings;

        if (string.IsNullOrWhiteSpace(existingProfile.TabTitleTemplate))
        {
            existingProfile.TabTitleTemplate = discoveredProfile.TabTitleTemplate;
        }

        if (string.IsNullOrWhiteSpace(existingProfile.TabColor))
        {
            existingProfile.TabColor = discoveredProfile.TabColor;
        }
    }

    private static List<TerminalProfile> DiscoverBuiltInProfiles(TerminalSettings baselineSettings)
    {
        var profiles = new List<TerminalProfile>
        {
            CreateProfile(
                "powershell",
                "PowerShell",
                TerminalShellKind.PowerShell,
                baselineSettings,
                "#28374A",
                true,
                "Embedded PowerShell 7 engine",
                settings: baselineSettings)
        };

        var windowsPowerShell = FindWindowsPowerShell();
        profiles.Add(CreateProfile(
            "windows-powershell",
            "Windows PowerShell",
            TerminalShellKind.WindowsPowerShell,
            baselineSettings,
            "#243B55",
            windowsPowerShell is not null,
            windowsPowerShell ?? "Windows PowerShell 5.1 was not detected.",
            windowsPowerShell ?? string.Empty,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-NoExit", "-Command", "-"]));

        var commandPrompt = FindWindowsExecutable("cmd.exe");
        profiles.Add(CreateProfile(
            "command-prompt",
            "Command Prompt",
            TerminalShellKind.CommandPrompt,
            baselineSettings,
            "#3A3A3A",
            commandPrompt is not null,
            commandPrompt ?? "Command Prompt was not detected.",
            commandPrompt ?? string.Empty,
            ["/D", "/Q"]));

        var dockerExecutable = ResolveExecutable("docker.exe")
            ?? FindFirstExisting(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Docker",
                    "Docker",
                    "resources",
                    "bin",
                    "docker.exe"));
        var dockerStatus = dockerExecutable is null
            ? new ProcessResult(false, string.Empty, "Docker Desktop was not detected.")
            : RunProcess(
                dockerExecutable,
                ["version", "--format", "{{.Client.Version}}|{{.Server.Version}}"],
                3500);
        var dockerMessage = dockerExecutable is null
            ? "Install Docker Desktop to enable this shell."
            : dockerStatus.Success
                ? $"Docker client/server {dockerStatus.Output.Trim().Replace('|', '/')}"
                : "Docker CLI found; start Docker Desktop to reach the engine.";
        profiles.Add(CreateProfile(
            "docker-cli",
            "Docker CLI",
            TerminalShellKind.DockerCli,
            baselineSettings,
            "#214761",
            dockerExecutable is not null,
            dockerMessage,
            dockerExecutable ?? string.Empty));

        if (dockerExecutable is not null && dockerStatus.Success)
        {
            profiles.AddRange(DiscoverDockerContainers(dockerExecutable, baselineSettings));
        }

        profiles.Add(CreateProfile(
            "azure-cloud-shell",
            "Azure Cloud Shell",
            TerminalShellKind.AzureCloudShell,
            baselineSettings,
            "#145C72",
            false,
            "Remote Azure authentication adapter is not configured yet."));

        var wslExecutable = FindWindowsExecutable("wsl.exe");
        if (wslExecutable is not null)
        {
            foreach (var distribution in DiscoverWslDistributions(wslExecutable))
            {
                var profile = CreateProfile(
                    $"wsl-{ToIdSegment(distribution)}",
                    distribution,
                    TerminalShellKind.WslBash,
                    baselineSettings,
                    "#2A3A2D",
                    true,
                    $"WSL distribution · {distribution}",
                    wslExecutable);
                profile.Distribution = distribution;
                profiles.Add(profile);
            }
        }

        var gitBash = FindGitBash();
        profiles.Add(CreateProfile(
            "git-bash",
            "Git Bash",
            TerminalShellKind.GitBash,
            baselineSettings,
            "#34472A",
            gitBash is not null,
            gitBash ?? "Git for Windows was not detected.",
            gitBash ?? string.Empty,
            ["--noprofile", "--norc"]));

        var pythonExecutable = ResolveExecutable("python.exe") ?? ResolveExecutable("py.exe");
        var pythonArguments = pythonExecutable is not null
            && string.Equals(Path.GetFileName(pythonExecutable), "py.exe", StringComparison.OrdinalIgnoreCase)
                ? new[] { "-3" }
                : [];
        profiles.Add(CreateProfile(
            "python",
            "Python",
            TerminalShellKind.Python,
            baselineSettings,
            "#463B21",
            pythonExecutable is not null,
            pythonExecutable ?? "Python was not detected.",
            pythonExecutable ?? string.Empty,
            pythonArguments));

        var nodeExecutable = ResolveExecutable("node.exe")
            ?? ResolveExecutable("node");
        profiles.Add(CreateProfile(
            "nodejs",
            "Node.js",
            TerminalShellKind.NodeJs,
            baselineSettings,
            "#31543A",
            nodeExecutable is not null,
            nodeExecutable ?? "Node.js was not detected.",
            nodeExecutable ?? string.Empty));

        var typeScriptStatus = nodeExecutable is null
            ? new ProcessResult(
                false,
                string.Empty,
                "Node.js was not detected.")
            : RunProcess(
                nodeExecutable,
                [
                    "--no-warnings",
                    "-e",
                    "const { stripTypeScriptTypes } = require('node:module'); " +
                    "if (typeof stripTypeScriptTypes !== 'function') process.exit(2); " +
                    "const output = stripTypeScriptTypes('const value: number = 1;', { mode: 'strip' }); " +
                    "if (!output.includes('const value')) process.exit(3); " +
                    "process.stdout.write(process.version);"
                ],
                3500);
        profiles.Add(CreateProfile(
            "typescript",
            "TypeScript",
            TerminalShellKind.TypeScript,
            baselineSettings,
            "#2F5F8A",
            typeScriptStatus.Success,
            typeScriptStatus.Success
                ? $"Node.js {typeScriptStatus.Output.Trim()} native type stripping · execution does not type-check"
                : "Node.js 22.13 or newer with native TypeScript type stripping is required.",
            nodeExecutable ?? string.Empty));

        var javaRuntimeExecutable = ResolveExecutable("java.exe")
            ?? ResolveExecutable("java");
        var javaCompilerExecutable = ResolveExecutable("javac.exe")
            ?? ResolveExecutable("javac");
        var javaRuntimeStatus = javaRuntimeExecutable is null
            ? new ProcessResult(false, string.Empty, "Java was not detected.")
            : RunProcess(javaRuntimeExecutable, ["-version"], 3500);
        var javaCompilerStatus = javaCompilerExecutable is null
            ? new ProcessResult(false, string.Empty, "javac was not detected.")
            : RunProcess(javaCompilerExecutable, ["-version"], 3500);
        var javaVersionText = string.Join(
            ' ',
            javaCompilerStatus.Output,
            javaCompilerStatus.Error).Trim();
        var javaMajorVersion = ParseJavaMajorVersion(javaVersionText);
        var javaAvailable = javaRuntimeExecutable is not null
                            && javaCompilerExecutable is not null
                            && javaRuntimeStatus.Success
                            && javaCompilerStatus.Success
                            && javaMajorVersion >= 17;
        var javaProfile = CreateProfile(
            "java",
            "Java",
            TerminalShellKind.Java,
            baselineSettings,
            "#7A3E25",
            javaAvailable,
            javaAvailable
                ? $"JDK {javaMajorVersion} · isolated Java 17 document and snippet builds"
                : javaRuntimeExecutable is null || javaCompilerExecutable is null
                    ? "Install a JDK 17 or newer to compile and run Java."
                    : javaMajorVersion is > 0 and < 17
                        ? $"JDK {javaMajorVersion} was detected; Hypershell Java execution requires JDK 17 or newer."
                        : "The detected Java compiler/runtime did not pass the JDK capability check.",
            javaCompilerExecutable ?? string.Empty);
        javaProfile.CompilerExecutablePath =
            javaCompilerExecutable ?? string.Empty;
        javaProfile.RuntimeExecutablePath =
            javaRuntimeExecutable ?? string.Empty;
        profiles.Add(javaProfile);

        var cppCompilerExecutable = ResolveExecutable("clang++.exe")
            ?? ResolveExecutable("clang++")
            ?? ResolveExecutable("g++.exe")
            ?? ResolveExecutable("g++")
            ?? ResolveExecutable("cl.exe")
            ?? ResolveExecutable("cl");
        var cppCompilerName = Path.GetFileNameWithoutExtension(
            cppCompilerExecutable ?? string.Empty);
        var cppCompilerStatus = cppCompilerExecutable is null
            ? new ProcessResult(false, string.Empty, "No C++ compiler was detected.")
            : RunProcess(
                cppCompilerExecutable,
                string.Equals(
                    cppCompilerName,
                    "cl",
                    StringComparison.OrdinalIgnoreCase)
                    ? ["/Bv"]
                    : ["--version"],
                3500);
        var cppProfile = CreateProfile(
            "cpp",
            "C++",
            TerminalShellKind.Cpp,
            baselineSettings,
            "#31536B",
            cppCompilerExecutable is not null && cppCompilerStatus.Success,
            cppCompilerExecutable is null
                ? "Install Clang, GCC, or use a Visual Studio developer environment to compile C++."
                : cppCompilerStatus.Success
                    ? $"{cppCompilerName} · isolated C++20 document and snippet builds"
                    : $"{cppCompilerName} was found but did not pass its compiler capability check.",
            cppCompilerExecutable ?? string.Empty);
        cppProfile.CompilerExecutablePath = cppCompilerExecutable ?? string.Empty;
        profiles.Add(cppProfile);

        var dotnetExecutable = ResolveExecutable("dotnet.exe")
            ?? ResolveExecutable("dotnet");
        var dotnetSdkStatus = dotnetExecutable is null
            ? new ProcessResult(false, string.Empty, ".NET SDK was not detected.")
            : RunProcess(dotnetExecutable, ["--list-sdks"], 3500);
        var dotnetSdkVersions = dotnetSdkStatus.Success
            ? SplitLines(dotnetSdkStatus.Output)
                .Select(line => line.Split(' ', 2)[0])
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .ToList()
            : [];
        var cSharpAvailable = dotnetExecutable is not null && dotnetSdkVersions.Count > 0;
        profiles.Add(CreateProfile(
            "csharp",
            "C#",
            TerminalShellKind.CSharp,
            baselineSettings,
            "#512B58",
            cSharpAvailable,
            cSharpAvailable
                ? $".NET SDK {dotnetSdkVersions[^1]} · compiled C# snippets"
                : "Install the .NET SDK to run C# library statements.",
            dotnetExecutable ?? string.Empty));

        var mySql = FindMySql();
        profiles.Add(CreateProfile(
            "mysql",
            "MySQL",
            TerminalShellKind.MySql,
            baselineSettings,
            "#23495C",
            mySql is not null,
            mySql ?? "The MySQL command-line client was not detected.",
            mySql ?? string.Empty,
            ["--batch", "--raw", "--unbuffered", "--skip-column-names"]));

        profiles.AddRange(DiscoverVisualStudioProfiles(baselineSettings, commandPrompt));
        return profiles;
    }

    private static TerminalProfile CreateProfile(
        string id,
        string name,
        TerminalShellKind shellKind,
        TerminalSettings baselineSettings,
        string tabColor,
        bool isAvailable,
        string availabilityMessage,
        string executablePath = "",
        IEnumerable<string>? arguments = null,
        TerminalSettings? settings = null)
    {
        return new TerminalProfile
        {
            Id = id,
            Name = name,
            ShellKind = shellKind,
            ExecutablePath = executablePath,
            Arguments = arguments?.ToList() ?? [],
            IsBuiltIn = true,
            IsAvailable = isAvailable,
            AvailabilityMessage = availabilityMessage,
            TabTitleTemplate = $"{name} {{n}}",
            TabColor = tabColor,
            Settings = settings ?? baselineSettings.Clone()
        };
    }

    private static IEnumerable<TerminalProfile> DiscoverDockerContainers(
        string dockerExecutable,
        TerminalSettings baselineSettings)
    {
        var result = RunProcess(
            dockerExecutable,
            ["ps", "--format", "{{.ID}}\t{{.Names}}\t{{.Image}}"],
            3500);
        if (!result.Success)
        {
            return [];
        }

        var profiles = new List<TerminalProfile>();
        foreach (var line in SplitLines(result.Output))
        {
            var values = line.Split('\t', 3);
            if (values.Length < 3)
            {
                continue;
            }

            var containerId = values[0];
            var containerName = values[1];
            var imageName = values[2];
            var shell = DiscoverDockerShell(dockerExecutable, containerId);
            var arguments = new List<string> { "exec", "-i", containerId, shell };
            if (shell.EndsWith("bash", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--noprofile");
                arguments.Add("--norc");
            }
            else if (shell.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("/D");
                arguments.Add("/Q");
            }

            var profile = CreateProfile(
                $"docker-container-{ToIdSegment(containerName)}",
                $"Docker · {containerName}",
                TerminalShellKind.DockerContainer,
                baselineSettings,
                "#20576B",
                true,
                $"{imageName} · {shell}",
                dockerExecutable,
                arguments);
            profile.Distribution = containerName;
            profiles.Add(profile);
        }

        return profiles;
    }

    private static string DiscoverDockerShell(string dockerExecutable, string containerId)
    {
        var result = RunProcess(
            dockerExecutable,
            [
                "exec",
                containerId,
                "sh",
                "-c",
                "if command -v bash >/dev/null 2>&1; then command -v bash; else command -v sh; fi"
            ],
            2500);
        var shell = SplitLines(result.Output).FirstOrDefault();
        return result.Success && !string.IsNullOrWhiteSpace(shell)
            ? shell
            : "cmd.exe";
    }

    private static IEnumerable<TerminalProfile> DiscoverVisualStudioProfiles(
        TerminalSettings baselineSettings,
        string? commandPrompt)
    {
        var vsWhere = FindFirstExisting(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe"));
        if (vsWhere is null)
        {
            return [];
        }

        var result = RunProcess(
            vsWhere,
            ["-all", "-prerelease", "-products", "*", "-format", "json", "-utf8"],
            5000);
        if (!result.Success)
        {
            return [];
        }

        var profiles = new List<TerminalProfile>();
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            foreach (var installation in document.RootElement.EnumerateArray())
            {
                var path = GetJsonString(installation, "installationPath");
                var instanceId = GetJsonString(installation, "instanceId");
                var version = GetJsonString(installation, "installationVersion");
                var displayName = GetJsonString(installation, "displayName");
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(instanceId))
                {
                    continue;
                }

                var isPrerelease = installation.TryGetProperty("isPrerelease", out var prereleaseValue)
                    && prereleaseValue.ValueKind == JsonValueKind.True;
                var majorVersion = version.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "VS";
                var suffix = isPrerelease ? " [Preview]" : string.Empty;
                var devCommandScript = Path.Combine(path, "Common7", "Tools", "VsDevCmd.bat");
                var devPowerShellModule = Path.Combine(
                    path,
                    "Common7",
                    "Tools",
                    "Microsoft.VisualStudio.DevShell.dll");

                if (commandPrompt is not null && File.Exists(devCommandScript))
                {
                    var profile = CreateProfile(
                        $"vs-{ToIdSegment(instanceId)}-cmd",
                        $"Developer Command Prompt for VS {majorVersion}{suffix}",
                        TerminalShellKind.CommandPrompt,
                        baselineSettings,
                        "#4A3C26",
                        true,
                        $"{displayName} · {devCommandScript}",
                        commandPrompt,
                        ["/D", "/Q"]);
                    profile.InitializationCommand = $"call \"{devCommandScript}\" -no_logo";
                    profiles.Add(profile);
                }

                if (File.Exists(devPowerShellModule))
                {
                    var profile = CreateProfile(
                        $"vs-{ToIdSegment(instanceId)}-powershell",
                        $"Developer PowerShell for VS {majorVersion}{suffix}",
                        TerminalShellKind.DeveloperPowerShell,
                        baselineSettings,
                        "#4C3355",
                        true,
                        $"{displayName} · {devPowerShellModule}");
                    var escapedModulePath = devPowerShellModule.Replace("'", "''", StringComparison.Ordinal);
                    var escapedInstallationPath = path.Replace("'", "''", StringComparison.Ordinal);
                    profile.InitializationCommand =
                        $"Import-Module '{escapedModulePath}'; " +
                        $"Enter-VsDevShell -VsInstallPath '{escapedInstallationPath}' " +
                        "-Arch amd64 -HostArch amd64 -SkipAutomaticLocation";
                    profiles.Add(profile);
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return profiles;
    }

    private static string? FindWindowsPowerShell()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return FindFirstExisting(
            Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            ResolveExecutable("powershell.exe"));
    }

    private static string? FindWindowsExecutable(string fileName)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return FindFirstExisting(
            Path.Combine(systemDirectory, fileName),
            ResolveExecutable(fileName));
    }

    private static string? FindGitBash()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var gitExecutable = ResolveExecutable("git.exe");
        var gitRootFromPath = gitExecutable is null
            ? null
            : Directory.GetParent(Path.GetDirectoryName(gitExecutable) ?? string.Empty)?.FullName;
        return FindFirstExisting(
            Path.Combine(programFiles, "Git", "bin", "bash.exe"),
            Path.Combine(localAppData, "Programs", "Git", "bin", "bash.exe"),
            gitRootFromPath is null ? null : Path.Combine(gitRootFromPath, "bin", "bash.exe"));
    }

    private static string? FindMySql()
    {
        var fromPath = ResolveExecutable("mysql.exe");
        if (fromPath is not null)
        {
            return fromPath;
        }

        try
        {
            var mySqlRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MySQL");
            if (Directory.Exists(mySqlRoot))
            {
                return Directory
                    .EnumerateDirectories(mySqlRoot, "MySQL Server *", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => Path.Combine(path, "bin", "mysql.exe"))
                    .FirstOrDefault(File.Exists);
            }
        }
        catch
        {
            // Treat inaccessible install directories as unavailable.
        }

        return null;
    }

    private static IEnumerable<string> DiscoverWslDistributions(string wslExecutable)
    {
        var result = RunProcess(wslExecutable, ["--list", "--quiet"], 5000);
        if (!result.Success)
        {
            return [];
        }

        return SplitLines(result.Output.Replace("\0", string.Empty, StringComparison.Ordinal))
            .Where(distribution =>
                !distribution.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProcessResult RunProcess(
        string fileName,
        IEnumerable<string> arguments,
        int timeoutMilliseconds)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(false, string.Empty, "The process could not be started.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process may have ended between the timeout and kill.
                }

                return new ProcessResult(false, string.Empty, "The process timed out.");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            return new ProcessResult(process.ExitCode == 0, output, error);
        }
        catch (Exception exception)
        {
            return new ProcessResult(false, string.Empty, exception.Message);
        }
    }

    private static string? FindFirstExisting(params string?[] candidates)
    {
        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Environment.ExpandEnvironmentVariables(candidate!))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int ParseJavaMajorVersion(string value)
    {
        var match = Regex.Match(
            value,
            @"\b(?:javac\s+)?(?<major>\d+)(?:\.\d+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
               && int.TryParse(match.Groups["major"].Value, out var major)
            ? major
            : 0;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ToIdSegment(string value)
    {
        var characters = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }

    private readonly record struct ProcessResult(bool Success, string Output, string Error);
}
