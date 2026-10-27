using Hypershell.Models;
using Hypershell.Profiles;
using Renci.SshNet.Common;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed record SshGitTransportRequest(
    string CommandText,
    string StandardInput,
    TimeSpan Timeout,
    int MaximumOutputCharacters);

public interface ISshGitCommandTransport
{
    Task<GitCommandResult> ExecuteAsync(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        SshGitTransportRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs a noninteractive command through a freshly authenticated SSH channel.
/// Output streams are drained with explicit character bounds and raw key bytes
/// remain inside <see cref="SshWorkspaceConnectionService"/>.
/// </summary>
public sealed class SshGitCommandTransport : ISshGitCommandTransport
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly SshWorkspaceConnectionService _connections;

    public SshGitCommandTransport(SshWorkspaceConnectionService connections)
    {
        _connections = connections
            ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<GitCommandResult> ExecuteAsync(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        SshGitTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.MaximumOutputCharacters));
        }

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await using var lease = await _connections.OpenTerminalAsync(
                host,
                credential,
                linkedSource.Token);
            using var command = lease.Client.CreateCommand(
                request.CommandText,
                Utf8);
            command.CommandTimeout = request.Timeout;
            var executeTask = command.ExecuteAsync(linkedSource.Token);
            var outputTask = ReadBoundedAsync(
                command.OutputStream,
                request.MaximumOutputCharacters,
                linkedSource.Token);
            var errorTask = ReadBoundedAsync(
                command.ExtendedOutputStream,
                Math.Min(request.MaximumOutputCharacters, 4 * 1024 * 1024),
                linkedSource.Token);
            await WriteInputAsync(
                command,
                request.StandardInput,
                linkedSource.Token);
            await Task.WhenAll(executeTask, outputTask, errorTask);
            var output = await outputTask;
            var error = await errorTask;
            return new GitCommandResult(
                true,
                command.ExitStatus,
                output.Text,
                error.Text,
                command.ExitSignal is null
                    ? string.Empty
                    : $"Remote Git was terminated by signal {command.ExitSignal}.",
                false,
                output.LimitExceeded || error.LimitExceeded);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return new GitCommandResult(
                true,
                null,
                string.Empty,
                string.Empty,
                "The remote Git command timed out.",
                true,
                false);
        }
        catch (Exception exception) when (exception is WorkspaceFileSystemException
                                          or SshException
                                          or IOException
                                          or InvalidOperationException
                                          or ObjectDisposedException)
        {
            return new GitCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                exception.Message,
                false,
                false);
        }
    }

    private static async Task WriteInputAsync(
        Renci.SshNet.SshCommand command,
        string input,
        CancellationToken cancellationToken)
    {
        await using var stream = command.CreateInputStream();
        if (!string.IsNullOrEmpty(input))
        {
            var bytes = Utf8.GetBytes(input);
            await stream.WriteAsync(bytes, cancellationToken);
        }
    }

    private static async Task<BoundedText> ReadBoundedAsync(
        Stream stream,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Utf8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        var result = new StringBuilder(Math.Min(maximumCharacters, 64 * 1024));
        var buffer = new char[4096];
        var exceeded = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var remaining = maximumCharacters - result.Length;
            if (remaining > 0)
            {
                result.Append(buffer, 0, Math.Min(remaining, read));
            }

            exceeded |= read > remaining;
        }

        return new BoundedText(result.ToString(), exceeded);
    }

    private sealed record BoundedText(string Text, bool LimitExceeded);
}

public sealed class SshGitCommandRunner : IGitCommandRunner
{
    private static readonly Regex VersionPattern = new(
        @"^git version (?<version>\S+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly RemoteHostSummary _host;
    private readonly SshCredentialSummary _credential;
    private readonly ISshGitCommandTransport _transport;
    private GitToolAvailability? _availability;

    public SshGitCommandRunner(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        ISshGitCommandTransport transport)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _credential = credential
            ?? throw new ArgumentNullException(nameof(credential));
        _transport = transport
            ?? throw new ArgumentNullException(nameof(transport));
    }

    public string RunnerId => $"git.ssh:{_host.Id}";

    public bool CanHandle(WorkspaceLocation location) =>
        location.IsSsh
        && string.Equals(
            location.Authority,
            _host.Id,
            StringComparison.OrdinalIgnoreCase);

    public async Task<GitToolAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        if (_availability is not null)
        {
            return _availability;
        }

        var result = await ExecuteCoreAsync(
            repository: null,
            ["--version"],
            string.Empty,
            readOnly: true,
            TimeSpan.FromSeconds(15),
            4096,
            cancellationToken);
        var match = VersionPattern.Match(result.StandardOutput.Trim());
        return _availability = result.Succeeded && match.Success
            ? new GitToolAvailability(
                true,
                "git",
                match.Groups["version"].Value,
                $"Git {match.Groups["version"].Value} is available on {_host.DisplayName}.")
            : new GitToolAvailability(
                false,
                "git",
                string.Empty,
                FirstError(
                    result.FailureMessage,
                    result.StandardError,
                    $"Git is unavailable on {_host.DisplayName}."));
    }

    public async Task<GitCommandResult> RunAsync(
        GitCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (invocation.Repository is not { } repository
            || !CanHandle(repository))
        {
            return new GitCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                "The SSH Git runner cannot execute outside its linked host.",
                false,
                false);
        }

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

        return await ExecuteCoreAsync(
            repository,
            invocation.Arguments,
            invocation.StandardInput,
            invocation.IsReadOnly,
            invocation.Timeout ?? TimeSpan.FromSeconds(60),
            invocation.MaximumOutputCharacters,
            cancellationToken);
    }

    private Task<GitCommandResult> ExecuteCoreAsync(
        WorkspaceLocation? repository,
        IReadOnlyList<string> arguments,
        string standardInput,
        bool readOnly,
        TimeSpan timeout,
        int maximumOutputCharacters,
        CancellationToken cancellationToken)
    {
        var commandText = _host.OperatingSystem ==
                          RemoteHostOperatingSystem.Windows
            ? BuildPowerShellCommand(repository, arguments, readOnly)
            : BuildPosixCommand(repository, arguments, readOnly);
        return _transport.ExecuteAsync(
            _host,
            _credential,
            new SshGitTransportRequest(
                commandText,
                standardInput,
                timeout,
                maximumOutputCharacters),
            cancellationToken);
    }

    public static string BuildPosixCommand(
        WorkspaceLocation? repository,
        IReadOnlyList<string> arguments,
        bool readOnly)
    {
        var tokens = new List<string>
        {
            "env",
            "LC_ALL=C",
            "LANG=C",
            "GIT_TERMINAL_PROMPT=0",
            "GCM_INTERACTIVE=auto",
            "GIT_EDITOR=true",
            "GIT_SEQUENCE_EDITOR=true"
        };
        if (readOnly)
        {
            tokens.Add("GIT_OPTIONAL_LOCKS=0");
        }

        tokens.Add("git");
        AddStableGitArguments(tokens);
        if (repository is not null)
        {
            tokens.Add("-C");
            tokens.Add(repository.Path);
        }

        tokens.AddRange(arguments);
        return string.Join(' ', tokens.Select(QuotePosix));
    }

    public static string BuildPowerShellCommand(
        WorkspaceLocation? repository,
        IReadOnlyList<string> arguments,
        bool readOnly)
    {
        var gitArguments = new List<string>();
        AddStableGitArguments(gitArguments);
        if (repository is not null)
        {
            gitArguments.Add("-C");
            gitArguments.Add(repository.Path);
        }

        gitArguments.AddRange(arguments);
        var environment = readOnly
            ? "$env:GIT_OPTIONAL_LOCKS='0';"
            : "$env:GIT_OPTIONAL_LOCKS=$null;";
        var script = "& { $env:LC_ALL='C'; $env:LANG='C'; "
                     + "$env:GIT_TERMINAL_PROMPT='0'; "
                     + "$env:GCM_INTERACTIVE='auto'; "
                     + "$env:GIT_EDITOR='true'; "
                     + "$env:GIT_SEQUENCE_EDITOR='true'; "
                     + environment
                     + " & git "
                     + string.Join(' ', gitArguments.Select(QuotePowerShellLiteral))
                     + "; exit $LASTEXITCODE }";
        var encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));
        return "powershell -NoLogo -NoProfile -NonInteractive -EncodedCommand "
               + encoded;
    }

    private static void AddStableGitArguments(ICollection<string> target)
    {
        target.Add("-c");
        target.Add("color.ui=false");
        target.Add("-c");
        target.Add("core.quotepath=false");
        target.Add("-c");
        target.Add("advice.detachedHead=false");
        target.Add("-c");
        target.Add("core.pager=cat");
    }

    private static string QuotePosix(string value) =>
        "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";

    private static string QuotePowerShellLiteral(string value) =>
        "'" + (value ?? string.Empty).Replace("'", "''") + "'";

    private static string FirstError(
        string failure,
        string standardError,
        string fallback)
    {
        var value = string.IsNullOrWhiteSpace(failure)
            ? standardError
            : failure;
        var line = (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? fallback : line.Trim();
    }
}
