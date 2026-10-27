using Hypershell.Infrastructure;
using Hypershell.Profiles;
using System.Security.Cryptography;

namespace Hypershell.Services;

public enum ProfileSyncConnectionState
{
    Unconfigured,
    SignedOut,
    Connecting,
    SignedIn,
    Offline,
    Error
}

public sealed class ProfileSyncSessionService
    : ObservableObject,
      IDisposable
{
    private static readonly TimeSpan RefreshMargin =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartupConnectTimeout =
        TimeSpan.FromSeconds(6);

    private readonly ProfileSyncDeviceStateService _deviceState;
    private readonly IProfileSyncCredentialStore _credentialStore;
    private readonly Func<string, IProfileSessionClient> _clientFactory;
    private readonly Func<string, ICloudDeviceAuthorizationClient>
        _cloudClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProfileSyncDeviceConfiguration _configuration;
    private ProfileSessionResponse? _session;
    private ProfileSyncConnectionState _state;
    private string _statusText;
    private bool _isBusy;
    private bool _isDisposed;

    public ProfileSyncSessionService(
        ProfileSyncDeviceStateService deviceState,
        IProfileSyncCredentialStore credentialStore,
        Func<string, IProfileSessionClient>? clientFactory = null,
        TimeProvider? timeProvider = null,
        Func<string, ICloudDeviceAuthorizationClient>?
            cloudClientFactory = null)
    {
        _deviceState = deviceState;
        _credentialStore = credentialStore;
        _clientFactory = clientFactory
                         ?? (endpoint =>
                             new HttpProfileSessionClient(endpoint));
        _cloudClientFactory = cloudClientFactory
                              ?? (endpoint =>
                                  new HttpCloudDeviceAuthorizationClient(
                                      endpoint,
                                      timeProvider: timeProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _configuration = _deviceState.LoadConfiguration();
        _state = HasConfiguration(_configuration)
            ? ProfileSyncConnectionState.SignedOut
            : ProfileSyncConnectionState.Unconfigured;
        _statusText = !string.IsNullOrWhiteSpace(
                          _deviceState.LastLoadWarning)
            ? _deviceState.LastLoadWarning
            : _state == ProfileSyncConnectionState.Unconfigured
                ? "Profile sync is not configured on this device."
                : _credentialStore.Exists
                    ? "A protected account credential is available for automatic sign-in."
                    : "Profile sync is configured, but this device is signed out.";
    }

    public ProfileSyncDeviceConfiguration Configuration =>
        CloneConfiguration(_configuration);

    public ProfileSyncConnectionState State => _state;

    public bool IsBusy => _isBusy;

    public bool IsConfigured => HasConfiguration(_configuration);

    public bool IsSignedIn =>
        _session is not null
        && _session.ExpiresAtUtc > _timeProvider.GetUtcNow();

    public bool IsOffline =>
        _state == ProfileSyncConnectionState.Offline;

    public bool HasSavedCredential => _credentialStore.Exists;

    public string StatusText => _statusText;

    public string SessionIdentity => _session is not null && IsSignedIn
        ? $"SIGNED IN  •  {_session.AccountDisplayName} ({_session.AccountId})  •  expires {_session.ExpiresAtUtc.ToLocalTime():g}"
        : _state == ProfileSyncConnectionState.Offline
            ? $"OFFLINE  •  {_configuration.AccountId}  •  local profile remains available"
            : "Signed out — no profile session token is held by the application.";

    public string CredentialStatus => HasSavedCredential
        ? "A reusable account credential is protected by Windows DPAPI for this Windows user and rotates automatically."
        : "No reusable account credential is stored on this device.";

    public async Task<bool> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsConfigured)
        {
            return false;
        }

        ProfileSyncStoredCredential? credential;
        try
        {
            credential = _credentialStore.Load();
        }
        catch (ProfileSyncException exception)
        {
            var deletionWarning = TryDeleteCredential();
            SetState(
                ProfileSyncConnectionState.Error,
                $"Automatic profile sign-in was disabled: {exception.Message}"
                + deletionWarning);
            return false;
        }

        if (credential is null)
        {
            SetState(
                ProfileSyncConnectionState.SignedOut,
                "Profile sync is configured, but no protected account credential is saved on this device.");
            return false;
        }

        if (!CredentialMatches(credential, _configuration))
        {
            var deletionWarning = TryDeleteCredential();
            SetState(
                ProfileSyncConnectionState.SignedOut,
                "The protected credential belonged to a different account connection and was removed."
                + deletionWarning);
            return false;
        }

        using var startupTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        startupTimeout.CancelAfter(StartupConnectTimeout);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                SetBusy(true);
                SetState(
                    ProfileSyncConnectionState.Connecting,
                    "Signing in to the saved profile connection…");
                var restored = await RequestSessionFromCredentialAsync(
                    _configuration,
                    credential,
                    startupTimeout.Token);
                _session = restored.Session;
                SetState(
                    ProfileSyncConnectionState.SignedIn,
                    BuildSignedInStatus(
                        _session,
                        credential.Kind
                        == ProfileSyncCredentialKind.DeviceRefreshToken
                            ? "The rotating device credential restored this installation."
                              + restored.CredentialWarning
                            : "The legacy recovery key restored this installation; browser sign-in is recommended."));
                return true;
            }
            finally
            {
                SetBusy(false);
                _gate.Release();
            }
        }
        catch (ProfileSyncException exception)
        {
            HandleRestoreFailure(exception, cancellationToken);
            return false;
        }
    }

    public void SaveConfiguration(
        ProfileSyncDeviceConfiguration configuration)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        var normalized = NormalizeConfiguration(configuration);
        _gate.Wait();
        try
        {
            var identityChanged = !SameIdentity(
                normalized,
                _configuration);
            _deviceState.SaveConfiguration(normalized);
            _configuration = normalized;
            if (identityChanged)
            {
                _session = null;
                var deletionWarning = TryDeleteCredential();
                SetState(
                    IsConfigured
                        ? ProfileSyncConnectionState.SignedOut
                        : ProfileSyncConnectionState.Unconfigured,
                    "Saved the device connection. A prior saved credential was removed because the endpoint or account changed."
                    + deletionWarning);
                return;
            }

            SetState(
                IsConfigured
                    ? ProfileSyncConnectionState.SignedOut
                    : ProfileSyncConnectionState.Unconfigured,
                "Saved the endpoint and account ID in device-local configuration.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileSessionResponse> AuthorizeDeviceAsync(
        string serverEndpoint,
        string deviceName,
        string platform,
        Action<DeviceAuthorizationResponse> authorizationRequired,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(authorizationRequired);
        var endpoint = ProfileSyncEndpoint.Normalize(
            serverEndpoint?.Trim() ?? string.Empty).AbsoluteUri;
        var fingerprint = CreateDeviceFingerprint();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetBusy(true);
            _session = null;
            SetState(
                ProfileSyncConnectionState.Connecting,
                "Starting secure Atlas browser sign-in…");
            await using var client = _cloudClientFactory(endpoint);
            var challenge = await client.StartAsync(
                new DeviceAuthorizationRequest
                {
                    DeviceName = deviceName,
                    Platform = platform,
                    DeviceFingerprint = fingerprint
                },
                cancellationToken);
            SetState(
                ProfileSyncConnectionState.Connecting,
                "Waiting for browser sign-in to finish…");
            authorizationRequired(challenge);

            CloudSessionResponse? cloudSession = null;
            while (_timeProvider.GetUtcNow() < challenge.ExpiresAtUtc)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(challenge.PollIntervalSeconds),
                    _timeProvider,
                    cancellationToken);
                var poll = await client.PollAsync(
                    challenge.DeviceCode,
                    cancellationToken);
                switch (poll.Kind)
                {
                    case CloudDeviceAuthorizationPollKind.Pending:
                        continue;
                    case CloudDeviceAuthorizationPollKind.Approved:
                        cloudSession = poll.Session
                                       ?? throw new ProfileSyncException(
                                            ProfileSyncFailureKind.Protocol,
                                            "The account service completed sign-in without returning a session.");
                        break;
                    case CloudDeviceAuthorizationPollKind.Denied:
                        throw new ProfileSyncException(
                            ProfileSyncFailureKind.Authorization,
                            "The browser sign-in request was denied.");
                    case CloudDeviceAuthorizationPollKind.Expired:
                        throw new ProfileSyncException(
                            ProfileSyncFailureKind.Authentication,
                            "The browser sign-in request expired. Start again to retry.");
                    case CloudDeviceAuthorizationPollKind.Consumed:
                        throw new ProfileSyncException(
                            ProfileSyncFailureKind.Authentication,
                            "The browser sign-in request was already used.");
                    default:
                        throw new ProfileSyncException(
                            ProfileSyncFailureKind.Protocol,
                            "The account service rejected the browser sign-in request.");
                }

                break;
            }

            if (cloudSession is null)
            {
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Timeout,
                    "The browser sign-in request expired before completion.");
            }

            var configuration = new ProfileSyncDeviceConfiguration
            {
                ServerEndpoint = endpoint,
                AccountId = cloudSession.AccountId
            };
            _credentialStore.SaveDeviceCredential(
                configuration,
                cloudSession,
                fingerprint);
            _deviceState.SaveConfiguration(configuration);
            _configuration = configuration;
            _session = ToProfileSession(cloudSession);
            SetState(
                ProfileSyncConnectionState.SignedIn,
                BuildSignedInStatus(
                    _session,
                    "Automatic sign-in is protected by Windows DPAPI on this PC."));
            return _session;
        }
        catch (ProfileSyncException exception)
        {
            _session = null;
            SetState(
                exception.Kind is ProfileSyncFailureKind.Network
                    or ProfileSyncFailureKind.Timeout
                    ? ProfileSyncConnectionState.Offline
                    : ProfileSyncConnectionState.Error,
                $"Browser sign-in failed without changing cloud profile content: {exception.Message}");
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _session = null;
            var cancelled = new ProfileSyncException(
                ProfileSyncFailureKind.Cancelled,
                "Browser sign-in was cancelled.",
                exception);
            SetState(
                ProfileSyncConnectionState.SignedOut,
                "Browser sign-in was cancelled. No cloud profile content was changed.");
            throw cancelled;
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task<ProfileSessionResponse> SignInAsync(
        ProfileSyncDeviceConfiguration configuration,
        string accessKey,
        bool rememberAccessKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalized = NormalizeConfiguration(configuration);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetBusy(true);
            SetState(
                ProfileSyncConnectionState.Connecting,
                "Exchanging the access key for a short-lived profile session…");
            var session = await RequestSessionAsync(
                normalized,
                accessKey,
                cancellationToken);

            _deviceState.SaveConfiguration(normalized);
            _configuration = normalized;
            var credentialWarning = string.Empty;
            try
            {
                if (rememberAccessKey)
                {
                    _credentialStore.Save(normalized, accessKey);
                }
                else
                {
                    _credentialStore.Delete();
                }
            }
            catch (ProfileSyncException exception)
            {
                if (rememberAccessKey)
                {
                    var deletionWarning = TryDeleteCredential();
                    credentialWarning =
                        $" Windows could not protect the replacement key, so automatic sign-in is disabled: {exception.Message}"
                        + deletionWarning;
                }
                else
                {
                    credentialWarning =
                        $" Windows could not remove a previously protected key: {exception.Message}";
                }
            }

            _session = session;
            SetState(
                ProfileSyncConnectionState.SignedIn,
                BuildSignedInStatus(
                    session,
                    rememberAccessKey && string.IsNullOrEmpty(credentialWarning)
                        ? "The protected key will be used for automatic sign-in."
                        : "The session is active, but automatic sign-in is disabled.")
                + credentialWarning);
            return session;
        }
        catch (ProfileSyncException exception)
        {
            _session = null;
            SetState(
                exception.Kind is ProfileSyncFailureKind.Network
                    or ProfileSyncFailureKind.Timeout
                    ? ProfileSyncConnectionState.Offline
                    : ProfileSyncConnectionState.Error,
                $"Profile sign-in failed without changing the saved connection: {exception.Message}");
            throw;
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task SignOutAsync(
        bool forgetCredential,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetBusy(true);
            var session = _session;
            var remoteWarning = string.Empty;
            ProfileSyncStoredCredential? credential = null;
            try
            {
                credential = _credentialStore.Load();
            }
            catch (ProfileSyncException exception)
            {
                remoteWarning =
                    $" The protected device credential could not be read for remote revocation: {exception.Message}";
            }

            if (session is not null && IsConfigured)
            {
                try
                {
                    if (forgetCredential
                        && credential is
                        {
                            Kind: ProfileSyncCredentialKind.DeviceRefreshToken
                        }
                        && CredentialMatches(credential, _configuration))
                    {
                        await using var cloudClient =
                            _cloudClientFactory(
                                _configuration.ServerEndpoint);
                        await cloudClient.RevokeDeviceAsync(
                            session.AccessToken,
                            credential.ClientDeviceId,
                            cancellationToken);
                    }
                    else
                    {
                        await using var client =
                            _clientFactory(_configuration.ServerEndpoint);
                        await client.SignOutAsync(
                            session.AccessToken,
                            cancellationToken);
                    }
                }
                catch (ProfileSyncException exception)
                {
                    remoteWarning +=
                        $" The server could not confirm revocation: {exception.Message}";
                }
            }

            _session = null;
            var credentialWarning = string.Empty;
            if (forgetCredential)
            {
                credentialWarning = TryDeleteCredential();
            }

            SetState(
                IsConfigured
                    ? ProfileSyncConnectionState.SignedOut
                    : ProfileSyncConnectionState.Unconfigured,
                forgetCredential
                    ? "Signed out, cleared the in-memory session, revoked this client when reachable, and removed its protected credential."
                      + remoteWarning
                      + credentialWarning
                    : "The application session was closed; the protected device credential remains available for the next startup."
                      + remoteWarning);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_session is not null
                && _session.ExpiresAtUtc > now + RefreshMargin)
            {
                return _session.AccessToken;
            }

            ProfileSyncStoredCredential? credential = null;
            try
            {
                credential = _credentialStore.Load();
            }
            catch (ProfileSyncException exception)
            {
                var deletionWarning = TryDeleteCredential();
                if (_session is not null
                    && _session.ExpiresAtUtc > now)
                {
                    SetState(
                        ProfileSyncConnectionState.SignedIn,
                        $"The current session remains active, but its saved key is unavailable: {exception.Message}"
                        + deletionWarning);
                    return _session.AccessToken;
                }

                _session = null;
                SetState(
                    ProfileSyncConnectionState.Error,
                    exception.Message + deletionWarning);
                throw;
            }

            if (credential is null
                || !CredentialMatches(credential, _configuration))
            {
                if (_session is not null
                    && _session.ExpiresAtUtc > now)
                {
                    return _session.AccessToken;
                }

                _session = null;
                SetState(
                    ProfileSyncConnectionState.SignedOut,
                    "The profile session expired. Authorize this PC again because no protected credential is available.");
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Authentication,
                    "Sign in again before using remote profile actions.");
            }

            try
            {
                var priorSession = _session;
                var restored = await RequestSessionFromCredentialAsync(
                    _configuration,
                    credential,
                    cancellationToken);
                _session = restored.Session;
                SetState(
                    ProfileSyncConnectionState.SignedIn,
                    BuildSignedInStatus(
                        _session,
                        priorSession is null
                            ? "The protected credential restored the application session."
                              + restored.CredentialWarning
                            : "The application renewed its expiring profile session and rotated the device credential."
                              + restored.CredentialWarning));
                return _session.AccessToken;
            }
            catch (ProfileSyncException exception) when (
                exception.Kind is ProfileSyncFailureKind.Network
                    or ProfileSyncFailureKind.Timeout)
            {
                if (_session is not null
                    && _session.ExpiresAtUtc > now)
                {
                    SetState(
                        ProfileSyncConnectionState.SignedIn,
                        $"Session renewal is temporarily offline; the current token remains valid until {_session.ExpiresAtUtc.ToLocalTime():g}.");
                    return _session.AccessToken;
                }

                _session = null;
                SetState(
                    ProfileSyncConnectionState.Offline,
                    $"The local profile remains available, but the server session could not be renewed: {exception.Message}");
                throw;
            }
            catch (ProfileSyncException exception) when (
                exception.Kind is ProfileSyncFailureKind.Authentication
                    or ProfileSyncFailureKind.Authorization)
            {
                _session = null;
                var deletionWarning = TryDeleteCredential();
                SetState(
                    ProfileSyncConnectionState.SignedOut,
                    "The saved credential was rejected and removed. Sign in again."
                    + deletionWarning);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void InvalidateSession(string reason)
    {
        ThrowIfDisposed();
        _gate.Wait();
        try
        {
            _session = null;
            SetState(
                IsConfigured
                    ? ProfileSyncConnectionState.SignedOut
                    : ProfileSyncConnectionState.Unconfigured,
                reason);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _session = null;
        _gate.Dispose();
    }

    private async Task<ProfileSessionResponse> RequestSessionAsync(
        ProfileSyncDeviceConfiguration configuration,
        string accessKey,
        CancellationToken cancellationToken)
    {
        await using var client = _clientFactory(
            configuration.ServerEndpoint);
        return await client.SignInAsync(
            configuration.AccountId,
            accessKey,
            cancellationToken);
    }

    private async Task<CredentialSessionResult>
        RequestSessionFromCredentialAsync(
            ProfileSyncDeviceConfiguration configuration,
            ProfileSyncStoredCredential credential,
            CancellationToken cancellationToken)
    {
        if (credential.Kind == ProfileSyncCredentialKind.LegacyAccessKey)
        {
            return new CredentialSessionResult(
                await RequestSessionAsync(
                    configuration,
                    credential.AccessKey,
                    cancellationToken),
                string.Empty);
        }

        await using var client = _cloudClientFactory(
            configuration.ServerEndpoint);
        var cloudSession = await client.RefreshAsync(
            credential.RefreshToken,
            cancellationToken);
        if (!string.Equals(
                cloudSession.AccountId,
                configuration.AccountId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                cloudSession.ClientDeviceId,
                credential.ClientDeviceId,
                StringComparison.Ordinal))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The refreshed session did not match the saved account and client-device identity.");
        }

        var credentialWarning = string.Empty;
        try
        {
            _credentialStore.SaveDeviceCredential(
                configuration,
                cloudSession,
                credential.DeviceFingerprint);
        }
        catch (ProfileSyncException exception)
        {
            credentialWarning =
                $" Windows could not save the rotated credential, so automatic renewal is disabled: {exception.Message}"
                + TryDeleteCredential();
        }

        return new CredentialSessionResult(
            ToProfileSession(cloudSession),
            credentialWarning);
    }

    private void HandleRestoreFailure(
        ProfileSyncException exception,
        CancellationToken callerCancellation)
    {
        _session = null;
        if (callerCancellation.IsCancellationRequested)
        {
            throw exception;
        }

        if (exception.Kind is ProfileSyncFailureKind.Authentication
            or ProfileSyncFailureKind.Authorization)
        {
            var deletionWarning = TryDeleteCredential();
            SetState(
                ProfileSyncConnectionState.SignedOut,
                "Automatic sign-in was rejected. The saved credential was removed; sign in again."
                + deletionWarning);
            return;
        }

        if (exception.Kind is ProfileSyncFailureKind.Network
            or ProfileSyncFailureKind.Timeout
            or ProfileSyncFailureKind.Cancelled)
        {
            SetState(
                ProfileSyncConnectionState.Offline,
                "The profile server was unavailable during startup. Hypershell remains fully usable with its local profile.");
            return;
        }

        SetState(
            ProfileSyncConnectionState.Error,
            $"Automatic profile sign-in failed: {exception.Message}");
    }

    private string TryDeleteCredential()
    {
        try
        {
            _credentialStore.Delete();
            return string.Empty;
        }
        catch (ProfileSyncException exception)
        {
            return $" The saved credential may still exist: {exception.Message}";
        }
        finally
        {
            OnPropertyChanged(nameof(HasSavedCredential));
            OnPropertyChanged(nameof(CredentialStatus));
        }
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value)
        {
            return;
        }

        _isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
    }

    private void SetState(
        ProfileSyncConnectionState state,
        string statusText)
    {
        _state = state;
        _statusText = statusText;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Configuration));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(HasSavedCredential));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SessionIdentity));
        OnPropertyChanged(nameof(CredentialStatus));
    }

    private static ProfileSyncDeviceConfiguration NormalizeConfiguration(
        ProfileSyncDeviceConfiguration configuration)
    {
        var endpoint = configuration.ServerEndpoint?.Trim()
                       ?? string.Empty;
        var accountId = configuration.AccountId?.Trim()
                        ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint)
            && string.IsNullOrWhiteSpace(accountId))
        {
            return new ProfileSyncDeviceConfiguration();
        }

        return new ProfileSyncDeviceConfiguration
        {
            ServerEndpoint = ProfileSyncEndpoint.Normalize(
                endpoint).AbsoluteUri,
            AccountId = accountId
        };
    }

    private static ProfileSyncDeviceConfiguration CloneConfiguration(
        ProfileSyncDeviceConfiguration configuration) =>
        new()
        {
            SchemaVersion = configuration.SchemaVersion,
            ServerEndpoint = configuration.ServerEndpoint,
            AccountId = configuration.AccountId
        };

    private static bool HasConfiguration(
        ProfileSyncDeviceConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.ServerEndpoint)
        && !string.IsNullOrWhiteSpace(configuration.AccountId);

    private static bool SameIdentity(
        ProfileSyncDeviceConfiguration first,
        ProfileSyncDeviceConfiguration second) =>
        string.Equals(
            EndpointKey(first.ServerEndpoint),
            EndpointKey(second.ServerEndpoint),
            StringComparison.Ordinal)
        && string.Equals(
            first.AccountId,
            second.AccountId,
            StringComparison.OrdinalIgnoreCase);

    private static bool CredentialMatches(
        ProfileSyncStoredCredential credential,
        ProfileSyncDeviceConfiguration configuration) =>
        string.Equals(
            EndpointKey(credential.ServerEndpoint),
            EndpointKey(configuration.ServerEndpoint),
            StringComparison.Ordinal)
        && string.Equals(
            credential.AccountId,
            configuration.AccountId,
            StringComparison.OrdinalIgnoreCase);

    private static string EndpointKey(string endpoint) =>
        string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : ProfileSyncEndpoint.CreateEndpointKey(endpoint);

    private static string BuildSignedInStatus(
        ProfileSessionResponse session,
        string detail) =>
        $"Signed in as {session.AccountDisplayName}. The session expires {session.ExpiresAtUtc.ToLocalTime():g}. {detail}";

    private static ProfileSessionResponse ToProfileSession(
        CloudSessionResponse session) =>
        new()
        {
            AccountId = session.AccountId,
            AccountDisplayName = session.AccountDisplayName,
            AccessToken = session.AccessToken,
            ExpiresAtUtc = session.AccessTokenExpiresAtUtc
        };

    private static string CreateDeviceFingerprint()
    {
        var value = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return "hsi_" + value;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_isDisposed, this);

    private sealed record CredentialSessionResult(
        ProfileSessionResponse Session,
        string CredentialWarning);
}
