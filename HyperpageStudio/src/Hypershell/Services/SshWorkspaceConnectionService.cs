using Hypershell.Models;
using Hypershell.Profiles;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Hypershell.Services;

public sealed record SshHostKeyObservation(
    string Algorithm,
    string Fingerprint);

public sealed class SshWorkspaceClientLease : IAsyncDisposable
{
    private readonly PrivateKeyFile _privateKey;
    private bool _disposed;

    internal SshWorkspaceClientLease(
        SftpClient client,
        PrivateKeyFile privateKey)
    {
        Client = client;
        _privateKey = privateKey;
    }

    public SftpClient Client { get; }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        if (Client.IsConnected)
        {
            Client.Disconnect();
        }

        Client.Dispose();
        _privateKey.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class SshTerminalClientLease : IAsyncDisposable
{
    private readonly PrivateKeyFile _privateKey;
    private bool _disposed;

    internal SshTerminalClientLease(
        SshClient client,
        PrivateKeyFile privateKey)
    {
        Client = client;
        _privateKey = privateKey;
    }

    public SshClient Client { get; }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        if (Client.IsConnected)
        {
            Client.Disconnect();
        }

        Client.Dispose();
        _privateKey.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Materializes short-lived SSH.NET clients from the device-local DPAPI vault.
/// Raw private-key bytes never leave this service and are cleared immediately
/// after SSH.NET parses them.
/// </summary>
public sealed class SshWorkspaceConnectionService
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly ISshCredentialStore _credentialStore;
    private readonly Func<ProfileSyncDeviceConfiguration> _configuration;
    private readonly TimeSpan _connectionTimeout;

    public SshWorkspaceConnectionService(
        ISshCredentialStore credentialStore,
        Func<ProfileSyncDeviceConfiguration> configuration,
        TimeSpan? connectionTimeout = null)
    {
        _credentialStore = credentialStore;
        _configuration = configuration;
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<SshWorkspaceClientLease> OpenSftpAsync(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        CancellationToken cancellationToken = default)
    {
        ValidateHostAndCredential(host, credential);
        ValidatePinnedHostKey(host);

        var privateKey = LoadPrivateKey(credential);
        SftpClient? client = null;
        SshHostKeyObservation? observed = null;
        try
        {
            client = new SftpClient(CreateConnectionInfo(host, privateKey))
            {
                KeepAliveInterval = TimeSpan.FromSeconds(20),
                OperationTimeout = TimeSpan.FromSeconds(30)
            };
            client.HostKeyReceived += (_, eventArgs) =>
            {
                observed = ObserveHostKey(eventArgs);
                eventArgs.CanTrust = HostKeyMatches(host, observed);
            };
            await client.ConnectAsync(cancellationToken);
            return new SshWorkspaceClientLease(client, privateKey);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            client?.Dispose();
            privateKey.Dispose();
            ThrowConnectionFailure(host, observed, exception);
            throw;
        }
    }

    public async Task<SshTerminalClientLease> OpenTerminalAsync(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        CancellationToken cancellationToken = default)
    {
        ValidateHostAndCredential(host, credential);
        ValidatePinnedHostKey(host);

        var privateKey = LoadPrivateKey(credential);
        SshClient? client = null;
        SshHostKeyObservation? observed = null;
        try
        {
            client = new SshClient(CreateConnectionInfo(host, privateKey))
            {
                KeepAliveInterval = TimeSpan.FromSeconds(20)
            };
            client.HostKeyReceived += (_, eventArgs) =>
            {
                observed = ObserveHostKey(eventArgs);
                eventArgs.CanTrust = HostKeyMatches(host, observed);
            };
            await client.ConnectAsync(cancellationToken);
            return new SshTerminalClientLease(client, privateKey);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            client?.Dispose();
            privateKey.Dispose();
            ThrowConnectionFailure(host, observed, exception);
            throw;
        }
    }

    private PrivateKeyFile LoadPrivateKey(SshCredentialSummary credential)
    {
        var configuration = _configuration();
        using var local = _credentialStore.Load(
            configuration.ServerEndpoint,
            configuration.AccountId,
            credential.Id)
            ?? throw Failure(
                WorkspaceFailureKind.CredentialUnavailable,
                "This SSH credential has not been released to the current PC.");
        if (local.KeyVersion != credential.KeyVersion
            || !string.Equals(
                local.PublicKeyFingerprint,
                credential.PublicKeyFingerprint,
                StringComparison.Ordinal))
        {
            throw Failure(
                WorkspaceFailureKind.CredentialUnavailable,
                "The protected SSH credential on this PC is stale. Reauthenticate and release the current version.");
        }

        return CreatePrivateKey(local);
    }

    private ConnectionInfo CreateConnectionInfo(
        RemoteHostSummary host,
        PrivateKeyFile privateKey) =>
        new(
            host.HostName,
            host.Port,
            host.UserName,
            new PrivateKeyAuthenticationMethod(host.UserName, privateKey))
        {
            Timeout = _connectionTimeout
        };

    private static void ValidatePinnedHostKey(RemoteHostSummary host)
    {
        if (string.IsNullOrWhiteSpace(host.HostKeyAlgorithm)
            || string.IsNullOrWhiteSpace(host.HostKeyFingerprint))
        {
            throw Failure(
                WorkspaceFailureKind.HostKeyMismatch,
                "The SSH host key has not been pinned. Observe and verify it before connecting.");
        }
    }

    private static SshHostKeyObservation ObserveHostKey(HostKeyEventArgs eventArgs) =>
        new(
            eventArgs.HostKeyName,
            "SHA256:" + eventArgs.FingerPrintSHA256);

    private static bool HostKeyMatches(
        RemoteHostSummary host,
        SshHostKeyObservation observed) =>
        string.Equals(
            host.HostKeyAlgorithm,
            observed.Algorithm,
            StringComparison.Ordinal)
        && string.Equals(
            host.HostKeyFingerprint,
            observed.Fingerprint,
            StringComparison.Ordinal);

    private static void ThrowConnectionFailure(
        RemoteHostSummary host,
        SshHostKeyObservation? observed,
        Exception exception)
    {
        if (observed is not null && !HostKeyMatches(host, observed))
        {
            throw Failure(
                WorkspaceFailureKind.HostKeyMismatch,
                $"SSH host-key mismatch. Expected {host.HostKeyAlgorithm} {host.HostKeyFingerprint}; received {observed.Algorithm} {observed.Fingerprint}.",
                exception);
        }

        throw Failure(
            WorkspaceFailureKind.Connection,
            $"SSH connection to {host.DisplayName} failed: {exception.Message}",
            exception);
    }

    private static PrivateKeyFile CreatePrivateKey(LocalSshCredential local)
    {
        using var stream = new MemoryStream(
            local.PrivateKeyUtf8,
            writable: false);
        if (local.PassphraseUtf8.Length == 0)
        {
            return new PrivateKeyFile(stream);
        }

        string passphrase;
        try
        {
            passphrase = StrictUtf8.GetString(local.PassphraseUtf8);
        }
        catch (DecoderFallbackException exception)
        {
            throw Failure(
                WorkspaceFailureKind.CredentialUnavailable,
                "The protected SSH passphrase is not valid UTF-8.",
                exception);
        }

        return new PrivateKeyFile(stream, passphrase);
    }

    private static void ValidateHostAndCredential(
        RemoteHostSummary host,
        SshCredentialSummary credential)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(credential);
        if (host.IsDisabled)
        {
            throw Failure(
                WorkspaceFailureKind.Connection,
                "The linked SSH host is disabled.");
        }

        if (credential.IsRevoked
            || !string.Equals(
                host.CredentialId,
                credential.Id,
                StringComparison.Ordinal))
        {
            throw Failure(
                WorkspaceFailureKind.CredentialUnavailable,
                "The linked host does not reference an active SSH credential.");
        }
    }

    private static bool IsConnectionFailure(Exception exception) =>
        exception is SshException
            or SocketException
            or IOException
            or InvalidOperationException
            or ObjectDisposedException;

    private static WorkspaceFileSystemException Failure(
        WorkspaceFailureKind kind,
        string message,
        Exception? innerException = null) =>
        new(kind, message, innerException);
}
