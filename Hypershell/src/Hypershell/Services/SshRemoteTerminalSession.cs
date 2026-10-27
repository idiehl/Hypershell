using Hypershell.Models;
using Hypershell.Profiles;
using Renci.SshNet;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public enum RemoteTerminalConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted,
    Disposed
}

public sealed record RemoteTerminalConnectionStateChangedEventArgs(
    RemoteTerminalConnectionState State,
    string Diagnostic);

public interface IRemoteTerminalSession : ITerminalSession
{
    string RemoteHostId { get; }

    string RemoteHostDisplayName { get; }

    RemoteTerminalShellKind ShellKind { get; }

    RemoteTerminalConnectionState ConnectionState { get; }

    string ConnectionDiagnostic { get; }

    event EventHandler<RemoteTerminalConnectionStateChangedEventArgs>?
        ConnectionStateChanged;

    Task ReconnectAsync(CancellationToken cancellationToken = default);
}

public interface IRemoteTerminalTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(
        string shellLaunchCommand,
        CancellationToken cancellationToken = default);

    Task WriteLineAsync(
        string value,
        CancellationToken cancellationToken = default);

    ValueTask<string?> ReadLineAsync(
        CancellationToken cancellationToken = default);
}

public sealed class SshRemoteTerminalTransport : IRemoteTerminalTransport
{
    private readonly RemoteHostSummary _host;
    private readonly SshCredentialSummary _credential;
    private readonly SshWorkspaceConnectionService _connections;
    private SshTerminalClientLease? _lease;
    private ShellStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _disposed;

    public SshRemoteTerminalTransport(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        SshWorkspaceConnectionService connections)
    {
        _host = host;
        _credential = credential;
        _connections = connections;
    }

    public bool IsConnected => !_disposed
        && _lease?.Client.IsConnected == true
        && _stream?.CanRead == true
        && _stream.CanWrite;

    public async Task ConnectAsync(
        string shellLaunchCommand,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected)
        {
            return;
        }

        await ResetAsync();
        _lease = await _connections.OpenTerminalAsync(
            _host,
            _credential,
            cancellationToken);
        try
        {
            _stream = _lease.Client.CreateShellStreamNoTerminal(64 * 1024);
            _reader = new StreamReader(
                _stream,
                new UTF8Encoding(false, false),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            _writer = new StreamWriter(
                _stream,
                new UTF8Encoding(false, false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            if (!string.IsNullOrWhiteSpace(shellLaunchCommand))
            {
                await WriteLineAsync(shellLaunchCommand, cancellationToken);
            }
        }
        catch
        {
            await ResetAsync();
            throw;
        }
    }

    public async Task WriteLineAsync(
        string value,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writer is null || !IsConnected)
        {
            throw new IOException("The SSH terminal channel is not connected.");
        }

        await _writer.WriteLineAsync(value.AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    public ValueTask<string?> ReadLineAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_reader is null || !IsConnected)
        {
            throw new IOException("The SSH terminal channel is not connected.");
        }

        return _reader.ReadLineAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ResetAsync();
    }

    private async ValueTask ResetAsync()
    {
        _writer?.Dispose();
        _writer = null;
        _reader?.Dispose();
        _reader = null;
        _stream?.Dispose();
        _stream = null;
        if (_lease is not null)
        {
            await _lease.DisposeAsync();
            _lease = null;
        }
    }
}

public static class RemoteTerminalProtocol
{
    public static RemoteTerminalShellKind ResolveShellKind(
        RemoteHostOperatingSystem operatingSystem,
        RemoteTerminalShellKind requested) =>
        requested != RemoteTerminalShellKind.Auto
            ? requested
            : operatingSystem == RemoteHostOperatingSystem.Windows
                ? RemoteTerminalShellKind.PowerShell
                : RemoteTerminalShellKind.Bash;

    public static string GetDefaultShellCommand(
        RemoteTerminalShellKind shellKind) =>
        shellKind switch
        {
            RemoteTerminalShellKind.Bash => "bash --noprofile --norc",
            RemoteTerminalShellKind.PowerShell =>
                "powershell.exe -NoLogo -NoProfile -NoExit -Command -",
            _ => throw new ArgumentOutOfRangeException(
                nameof(shellKind),
                shellKind,
                "Resolve the automatic remote shell before creating a terminal channel.")
        };

    public static string BuildSubmission(
        RemoteTerminalShellKind shellKind,
        string command,
        string? workingDirectory,
        string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        return shellKind switch
        {
            RemoteTerminalShellKind.Bash => BuildBashSubmission(
                encoded,
                workingDirectory,
                marker),
            RemoteTerminalShellKind.PowerShell => BuildPowerShellSubmission(
                encoded,
                workingDirectory,
                marker),
            _ => throw new ArgumentOutOfRangeException(nameof(shellKind))
        };
    }

    public static bool ContainsBeginMarker(string line, string marker) =>
        line.Contains($"__HYPERSHELL_BEGIN_{marker}__", StringComparison.Ordinal);

    public static bool TryParseCompletion(
        string line,
        string marker,
        out int exitCode,
        out string workingDirectory)
    {
        exitCode = 0;
        workingDirectory = string.Empty;
        var prefix = $"__HYPERSHELL_DONE_{marker}__|";
        var start = line.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var parts = line[(start + prefix.Length)..].Trim().Split('|', 2);
        if (parts.Length != 2
            || !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out exitCode))
        {
            return false;
        }

        try
        {
            workingDirectory = Encoding.UTF8.GetString(
                Convert.FromBase64String(parts[1]));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BuildBashSubmission(
        string encodedCommand,
        string? workingDirectory,
        string marker)
    {
        var execution =
            $"__hs_cmd=\"$(printf %s '{encodedCommand}' | base64 -d)\"; " +
            "eval \"$__hs_cmd\"; __hs_ec=$?";
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            execution =
                $"if cd -- {QuoteBash(workingDirectory)}; then {execution}; " +
                "else __hs_ec=$?; fi";
        }

        return
            $"printf '\\n__HYPERSHELL_BEGIN_{marker}__\\n'; " +
            execution + "; " +
            "__hs_pwd=\"$(printf %s \"$PWD\" | base64 | tr -d '\\r\\n')\"; " +
            $"printf '\\n__HYPERSHELL_DONE_{marker}__|%s|%s\\n' \"$__hs_ec\" \"$__hs_pwd\"";
    }

    private static string BuildPowerShellSubmission(
        string encodedCommand,
        string? workingDirectory,
        string marker)
    {
        var location = string.IsNullOrWhiteSpace(workingDirectory)
            ? "$__hs_location_ok=$true; "
            : "$__hs_location_ok=$true; try { " +
              $"Set-Location -LiteralPath {QuotePowerShell(workingDirectory)} -ErrorAction Stop " +
              "} catch { Write-Error $_; $__hs_location_ok=$false }; ";
        return
            $"Write-Output '__HYPERSHELL_BEGIN_{marker}__'; " +
            location +
            "$__hs_ec=1; if ($__hs_location_ok) { " +
            "$global:LASTEXITCODE = 0; " +
            $"$__hs_cmd=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedCommand}')); " +
            "& ([scriptblock]::Create($__hs_cmd)); " +
            "$__hs_ec=if ($?) { if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 } } else { if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) { $LASTEXITCODE } else { 1 } } }; " +
            "$__hs_pwd=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Location).Path)); " +
            $"Write-Output ('__HYPERSHELL_DONE_{marker}__|{{0}}|{{1}}' -f $__hs_ec,$__hs_pwd)";
    }

    private static string QuoteBash(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string QuotePowerShell(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

public sealed class SshRemoteTerminalSession : IRemoteTerminalSession
{
    private readonly Func<IRemoteTerminalTransport> _transportFactory;
    private readonly string _shellLaunchCommand;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRemoteTerminalTransport? _transport;
    private RemoteTerminalConnectionState _connectionState =
        RemoteTerminalConnectionState.Disconnected;
    private string _connectionDiagnostic = "Not connected.";
    private bool _disposed;

    public SshRemoteTerminalSession(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        RemoteTerminalShellKind requestedShellKind,
        string shellLaunchCommand,
        SshWorkspaceConnectionService connections)
        : this(
            host.Id,
            host.DisplayName,
            RemoteTerminalProtocol.ResolveShellKind(
                host.OperatingSystem,
                requestedShellKind),
            shellLaunchCommand,
            () => new SshRemoteTerminalTransport(
                host,
                credential,
                connections))
    {
    }

    public SshRemoteTerminalSession(
        string remoteHostId,
        string remoteHostDisplayName,
        RemoteTerminalShellKind shellKind,
        string shellLaunchCommand,
        Func<IRemoteTerminalTransport> transportFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteHostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteHostDisplayName);
        if (shellKind == RemoteTerminalShellKind.Auto)
        {
            throw new ArgumentException(
                "The remote shell kind must be resolved before creating a session.",
                nameof(shellKind));
        }

        RemoteHostId = remoteHostId;
        RemoteHostDisplayName = remoteHostDisplayName;
        ShellKind = shellKind;
        _shellLaunchCommand = string.IsNullOrWhiteSpace(shellLaunchCommand)
            ? RemoteTerminalProtocol.GetDefaultShellCommand(shellKind)
            : shellLaunchCommand.Trim();
        _transportFactory = transportFactory
            ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    public string RemoteHostId { get; }

    public string RemoteHostDisplayName { get; }

    public RemoteTerminalShellKind ShellKind { get; }

    public RemoteTerminalConnectionState ConnectionState => _connectionState;

    public string ConnectionDiagnostic => _connectionDiagnostic;

    public event EventHandler<RemoteTerminalConnectionStateChangedEventArgs>?
        ConnectionStateChanged;

    public async Task ReconnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await ResetTransportAsync();
            await ConnectAsync(reconnecting: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await EnsureConnectedAsync(cancellationToken);
            var marker = Guid.NewGuid().ToString("N");
            var submission = RemoteTerminalProtocol.BuildSubmission(
                ShellKind,
                command,
                workingDirectory,
                marker);
            var result = new TerminalExecutionResult();
            var began = false;
            try
            {
                await _transport!.WriteLineAsync(submission, cancellationToken);
                while (true)
                {
                    var line = await _transport.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        throw new IOException(
                            "The SSH terminal channel closed before the command completed.");
                    }

                    line = line.TrimEnd('\r');
                    if (!began)
                    {
                        began = RemoteTerminalProtocol.ContainsBeginMarker(
                            line,
                            marker);
                        continue;
                    }

                    if (RemoteTerminalProtocol.TryParseCompletion(
                            line,
                            marker,
                            out var exitCode,
                            out var currentDirectory))
                    {
                        result.WorkingDirectory = currentDirectory;
                        if (exitCode != 0)
                        {
                            result.Errors.Add(
                                $"Remote command exited with code {exitCode}.");
                        }

                        SetState(
                            RemoteTerminalConnectionState.Connected,
                            $"Connected to {RemoteHostDisplayName}.");
                        return result;
                    }

                    result.Output.Add(line);
                }
            }
            catch
            {
                await ResetTransportAsync();
                SetState(
                    RemoteTerminalConnectionState.Faulted,
                    $"Connection to {RemoteHostDisplayName} was interrupted. Reconnect and try again.");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetTransportAsync().AsTask().GetAwaiter().GetResult();
        SetState(RemoteTerminalConnectionState.Disposed, "Session closed.");
        _gate.Dispose();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_transport?.IsConnected == true)
        {
            return;
        }

        await ConnectAsync(reconnecting: false, cancellationToken);
    }

    private async Task ConnectAsync(
        bool reconnecting,
        CancellationToken cancellationToken)
    {
        SetState(
            reconnecting
                ? RemoteTerminalConnectionState.Reconnecting
                : RemoteTerminalConnectionState.Connecting,
            reconnecting
                ? $"Reconnecting to {RemoteHostDisplayName}…"
                : $"Connecting to {RemoteHostDisplayName}…");
        try
        {
            _transport = _transportFactory();
            await _transport.ConnectAsync(
                _shellLaunchCommand,
                cancellationToken);
            SetState(
                RemoteTerminalConnectionState.Connected,
                $"Connected to {RemoteHostDisplayName}.");
        }
        catch (Exception exception)
        {
            await ResetTransportAsync();
            SetState(RemoteTerminalConnectionState.Faulted, exception.Message);
            throw;
        }
    }

    private async ValueTask ResetTransportAsync()
    {
        if (_transport is null)
        {
            return;
        }

        await _transport.DisposeAsync();
        _transport = null;
    }

    private void SetState(
        RemoteTerminalConnectionState state,
        string diagnostic)
    {
        if (_connectionState == state
            && string.Equals(
                _connectionDiagnostic,
                diagnostic,
                StringComparison.Ordinal))
        {
            return;
        }

        _connectionState = state;
        _connectionDiagnostic = diagnostic;
        ConnectionStateChanged?.Invoke(
            this,
            new RemoteTerminalConnectionStateChangedEventArgs(
                state,
                diagnostic));
    }
}
