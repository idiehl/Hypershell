using Hypershell.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public interface IGitCommandRunner
{
    string RunnerId { get; }
    bool CanHandle(WorkspaceLocation location);
    Task<GitToolAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);
    Task<GitCommandResult> RunAsync(
        GitCommandInvocation invocation,
        CancellationToken cancellationToken = default);
}

public sealed class LocalGitCommandRunner : IGitCommandRunner
{
    private static readonly Regex VersionPattern = new(
        @"^git version (?<version>\S+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly BoundedProcessRunner _processRunner;
    private readonly string _configuredExecutablePath;
    private GitToolAvailability? _availability;

    public LocalGitCommandRunner(
        string? executablePath = null,
        BoundedProcessRunner? processRunner = null)
    {
        _configuredExecutablePath = executablePath?.Trim() ?? string.Empty;
        _processRunner = processRunner ?? new BoundedProcessRunner();
    }

    public string RunnerId => "git.local-cli";

    public bool CanHandle(WorkspaceLocation location) => location.IsLocal;

    public async Task<GitToolAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        if (_availability is not null)
        {
            return _availability;
        }

        var path = ResolveExecutablePath(_configuredExecutablePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return _availability = new GitToolAvailability(
                false,
                string.Empty,
                string.Empty,
                "Git for Windows was not found. Install Git or configure its absolute executable path.");
        }

        var probe = await _processRunner.RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = path,
                Arguments = ["--version"],
                Timeout = TimeSpan.FromSeconds(10),
                MaximumStandardInputCharacters = 0,
                MaximumStandardOutputCharacters = 4096,
                MaximumStandardErrorCharacters = 4096,
                EnvironmentVariables = StableEnvironment(readOnly: true)
            },
            cancellationToken);
        var match = VersionPattern.Match(probe.StandardOutput.Trim());
        var gitAvailable = probe is
            {
                Started: true,
                ExitCode: 0,
                TimedOut: false,
                OutputLimitExceeded: false
            }
            && match.Success;
        if (!gitAvailable)
        {
            return _availability = new GitToolAvailability(
                false,
                path,
                string.Empty,
                string.IsNullOrWhiteSpace(probe.FailureMessage)
                    ? FirstError(probe.StandardError, "Git could not be started.")
                    : probe.FailureMessage)
            {
                CredentialMessage =
                    "Git Credential Manager cannot be checked until Git is available."
            };
        }

        var credential = await ProbeCredentialManagerAsync(
            path,
            cancellationToken);
        return _availability = new GitToolAvailability(
            true,
            path,
            match.Groups["version"].Value,
            $"Git {match.Groups["version"].Value} is available.")
        {
            CredentialManagerAvailable = credential.IsAvailable,
            CredentialManagerVersion = credential.Version,
            CredentialHelper = credential.Helper,
            CredentialMessage = credential.Message
        };
    }

    public async Task<GitCommandResult> RunAsync(
        GitCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var availability = await GetAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            return new GitCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                availability.Message,
                false,
                false);
        }

        var arguments = new List<string>
        {
            "-c", "color.ui=false",
            "-c", "core.quotepath=false",
            "-c", "advice.detachedHead=false",
            "-c", "core.pager=cat"
        };
        var workingDirectory = Environment.CurrentDirectory;
        if (invocation.Repository is { } repository)
        {
            if (!repository.IsLocal)
            {
                return new GitCommandResult(
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    "The local Git runner cannot execute against a remote workspace URI.",
                    false,
                    false);
            }

            if (!Directory.Exists(repository.Path))
            {
                return new GitCommandResult(
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    "The local Git working directory does not exist.",
                    false,
                    false);
            }

            arguments.Add("-C");
            arguments.Add(repository.Path);
            workingDirectory = repository.Path;
        }

        arguments.AddRange(invocation.Arguments);
        var result = await _processRunner.RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = availability.ExecutablePath,
                Arguments = arguments,
                StandardInput = invocation.StandardInput,
                WorkingDirectory = workingDirectory,
                Timeout = invocation.Timeout ?? TimeSpan.FromSeconds(60),
                MaximumStandardInputCharacters = Math.Max(
                    invocation.StandardInput.Length,
                    8 * 1024 * 1024),
                MaximumStandardOutputCharacters =
                    invocation.MaximumOutputCharacters,
                MaximumStandardErrorCharacters = Math.Min(
                    invocation.MaximumOutputCharacters,
                    4 * 1024 * 1024),
                EnvironmentVariables = StableEnvironment(invocation.IsReadOnly)
            },
            cancellationToken);
        return new GitCommandResult(
            result.Started,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.FailureMessage,
            result.TimedOut,
            result.OutputLimitExceeded);
    }

    private static IReadOnlyDictionary<string, string?> StableEnvironment(
        bool readOnly) => new Dictionary<string, string?>
    {
        ["LC_ALL"] = "C",
        ["LANG"] = "C",
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "auto",
        ["GIT_EDITOR"] = "true",
        ["GIT_SEQUENCE_EDITOR"] = "true",
        ["GIT_OPTIONAL_LOCKS"] = readOnly ? "0" : null
    };

    private async Task<GitCredentialProbe> ProbeCredentialManagerAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        var versionProbe = await _processRunner.RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = executablePath,
                Arguments = ["credential-manager", "--version"],
                Timeout = TimeSpan.FromSeconds(10),
                MaximumStandardInputCharacters = 0,
                MaximumStandardOutputCharacters = 4096,
                MaximumStandardErrorCharacters = 4096,
                EnvironmentVariables = StableEnvironment(readOnly: true)
            },
            cancellationToken);
        var helperProbe = await _processRunner.RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = executablePath,
                Arguments =
                [
                    "config",
                    "--show-origin",
                    "--get-all",
                    "credential.helper"
                ],
                Timeout = TimeSpan.FromSeconds(10),
                MaximumStandardInputCharacters = 0,
                MaximumStandardOutputCharacters = 4096,
                MaximumStandardErrorCharacters = 4096,
                EnvironmentVariables = StableEnvironment(readOnly: true)
            },
            cancellationToken);
        var version = FirstLine(
            string.IsNullOrWhiteSpace(versionProbe.StandardOutput)
                ? versionProbe.StandardError
                : versionProbe.StandardOutput);
        var helper = ParseCredentialHelpers(helperProbe.StandardOutput);
        var available = versionProbe is
        {
            Started: true,
            ExitCode: 0,
            TimedOut: false,
            OutputLimitExceeded: false
        } && !string.IsNullOrWhiteSpace(version);
        var message = available
            ? string.IsNullOrWhiteSpace(helper)
                ? $"Git Credential Manager {version} is installed, but no credential.helper is configured. Configure the manager helper before using HTTPS remotes."
                : $"Git Credential Manager {version} is ready ({helper})."
            : "Git Credential Manager was not found. Install Git for Windows with Git Credential Manager before using authenticated HTTPS remotes.";
        return new GitCredentialProbe(available, version, helper, message);
    }

    private static string ParseCredentialHelpers(string output)
    {
        var helpers = (output ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line[(line.LastIndexOf('\t') + 1)..].Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return string.Join(", ", helpers);
    }

    private static string ResolveExecutablePath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)
            && Path.IsPathFullyQualified(configured)
            && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var candidates = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            try
            {
                candidates.Add(Path.Combine(directory, "git.exe"));
            }
            catch (ArgumentException)
            {
            }
        }

        AddCandidate(candidates, Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        AddCandidate(candidates, Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Git", "cmd", "git.exe");
        AddCandidate(candidates, Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe");
        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static void AddCandidate(
        ICollection<string> candidates,
        string root,
        params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        candidates.Add(parts.Aggregate(root, Path.Combine));
    }

    private static string FirstError(string value, string fallback)
    {
        var line = (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? fallback : line.Trim();
    }

    private static string FirstLine(string value) =>
        (value ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault()?.Trim() ?? string.Empty;

    private sealed record GitCredentialProbe(
        bool IsAvailable,
        string Version,
        string Helper,
        string Message);
}
