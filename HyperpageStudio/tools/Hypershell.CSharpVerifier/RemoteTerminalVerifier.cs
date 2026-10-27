using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Text;
using System.Text.RegularExpressions;

internal static partial class RemoteTerminalVerifier
{
    public static async Task VerifyAsync()
    {
        Require(
            RemoteTerminalProtocol.ResolveShellKind(
                RemoteHostOperatingSystem.Linux,
                RemoteTerminalShellKind.Auto)
            == RemoteTerminalShellKind.Bash,
            "Linux did not default to Bash.");
        Require(
            RemoteTerminalProtocol.ResolveShellKind(
                RemoteHostOperatingSystem.Windows,
                RemoteTerminalShellKind.Auto)
            == RemoteTerminalShellKind.PowerShell,
            "Windows did not default to PowerShell.");

        const string command = "printf 'hello remote'";
        const string marker = "abc123";
        var submission = RemoteTerminalProtocol.BuildSubmission(
            RemoteTerminalShellKind.Bash,
            command,
            "/srv/project with spaces",
            marker);
        Require(
            !submission.Contains(command, StringComparison.Ordinal)
            && submission.Contains(
                Convert.ToBase64String(Encoding.UTF8.GetBytes(command)),
                StringComparison.Ordinal)
            && submission.Contains("cd -- '/srv/project with spaces'", StringComparison.Ordinal),
            "The Bash protocol did not encode the command or quote the working directory.");

        var encodedDirectory = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("/srv/project"));
        Require(
            RemoteTerminalProtocol.TryParseCompletion(
                $"__HYPERSHELL_DONE_{marker}__|7|{encodedDirectory}",
                marker,
                out var exitCode,
                out var workingDirectory)
            && exitCode == 7
            && workingDirectory == "/srv/project",
            "The remote completion marker did not preserve exit code and working directory.");

        var transports = new List<FakeRemoteTerminalTransport>();
        using var session = new SshRemoteTerminalSession(
            "host-one",
            "Build Server",
            RemoteTerminalShellKind.Bash,
            "bash --noprofile --norc",
            () =>
            {
                var transport = new FakeRemoteTerminalTransport();
                transports.Add(transport);
                return transport;
            });

        await session.ReconnectAsync();
        Require(
            session.ConnectionState == RemoteTerminalConnectionState.Connected
            && transports.Count == 1
            && transports[0].ShellLaunchCommand == "bash --noprofile --norc",
            "The remote session did not establish its persistent shell channel.");

        var result = await session.ExecuteAsync("pwd", "/srv/project");
        Require(
            result.Output.SequenceEqual(["remote output"])
            && result.Errors.Count == 0
            && result.WorkingDirectory == "/srv/project/next"
            && transports[0].WriteCount == 1,
            "The persistent remote session did not return command output and state.");

        await session.ReconnectAsync();
        Require(
            transports.Count == 2
            && transports[0].IsDisposed
            && transports[1].IsConnected
            && session.ConnectionState == RemoteTerminalConnectionState.Connected,
            "Explicit reconnect did not replace the SSH shell channel cleanly.");

        using var secondSession = new SshRemoteTerminalSession(
            "host-one",
            "Build Server",
            RemoteTerminalShellKind.Bash,
            "bash --noprofile --norc",
            () => new FakeRemoteTerminalTransport());
        await secondSession.ReconnectAsync();
        Require(
            secondSession.ConnectionState == RemoteTerminalConnectionState.Connected
            && session.ConnectionState == RemoteTerminalConnectionState.Connected,
            "Independent remote terminal tabs did not retain independent connection state.");

        Console.WriteLine(
            "REMOTE_TERMINAL_OK|os-defaults|encoded-protocol|persistent-channel|cwd|reconnect|independent-sessions");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeRemoteTerminalTransport : IRemoteTerminalTransport
    {
        private readonly Queue<string> _lines = new();

        public bool IsConnected { get; private set; }

        public bool IsDisposed { get; private set; }

        public string ShellLaunchCommand { get; private set; } = string.Empty;

        public int WriteCount { get; private set; }

        public Task ConnectAsync(
            string shellLaunchCommand,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShellLaunchCommand = shellLaunchCommand;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConnected)
            {
                throw new InvalidOperationException("Fake transport is disconnected.");
            }

            WriteCount++;
            var match = MarkerRegex().Match(value);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    "The submission did not contain a begin marker.");
            }

            var marker = match.Groups["marker"].Value;
            var directory = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("/srv/project/next"));
            _lines.Enqueue($"__HYPERSHELL_BEGIN_{marker}__");
            _lines.Enqueue("remote output");
            _lines.Enqueue($"__HYPERSHELL_DONE_{marker}__|0|{directory}");
            return Task.CompletedTask;
        }

        public ValueTask<string?> ReadLineAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(
                _lines.Count == 0 ? null : _lines.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            IsDisposed = true;
            _lines.Clear();
            return ValueTask.CompletedTask;
        }
    }

    [GeneratedRegex("__HYPERSHELL_BEGIN_(?<marker>[a-f0-9]+)__")]
    private static partial Regex MarkerRegex();
}
