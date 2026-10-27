using Hypershell.Infrastructure;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace Hypershell.ViewModels;

public sealed class LinkedHostSettingsViewModel
    : ObservableObject,
      IDisposable
{
    private readonly MainWindowViewModel _mainWindow;
    private LinkedHostSettingsItem? _selectedHost;
    private SshCredentialSettingsItem? _selectedCredential;
    private string _credentialDisplayName = string.Empty;
    private string _credentialFilePath = string.Empty;
    private string _hostDisplayName = string.Empty;
    private string _hostName = string.Empty;
    private int _hostPort = 22;
    private string _hostUserName = string.Empty;
    private RemoteHostOperatingSystem _hostOperatingSystem =
        RemoteHostOperatingSystem.Unknown;
    private string _hostDefaultRoot = "/";
    private string _hostCredentialId = string.Empty;
    private string _hostNotes = string.Empty;
    private string _hostKeyAlgorithm = "ssh-ed25519";
    private string _hostKeyFingerprint = string.Empty;
    private string _pendingReleaseCode = string.Empty;
    private string _pendingReleaseUri = string.Empty;
    private string _status =
        "Sign in, then refresh linked hosts and encrypted SSH credentials.";
    private bool _isBusy;
    private bool _disposed;

    public LinkedHostSettingsViewModel(MainWindowViewModel mainWindow)
    {
        _mainWindow = mainWindow;
        _mainWindow.ProfileSyncSession.PropertyChanged +=
            OnProfileSessionPropertyChanged;
    }

    public ObservableCollection<LinkedHostSettingsItem> Hosts { get; } = [];

    public ObservableCollection<SshCredentialSettingsItem> Credentials { get; }
        = [];

    public IReadOnlyList<RemoteHostOperatingSystem> OperatingSystems { get; } =
        Enum.GetValues<RemoteHostOperatingSystem>();

    public LinkedHostSettingsItem? SelectedHost
    {
        get => _selectedHost;
        set
        {
            if (SetProperty(ref _selectedHost, value))
            {
                LoadSelectedHost();
                NotifyActions();
            }
        }
    }

    public SshCredentialSettingsItem? SelectedCredential
    {
        get => _selectedCredential;
        set
        {
            if (SetProperty(ref _selectedCredential, value))
            {
                if (value is not null)
                {
                    CredentialDisplayName = value.DisplayName;
                }

                NotifyActions();
            }
        }
    }

    public string CredentialDisplayName
    {
        get => _credentialDisplayName;
        set => SetProperty(ref _credentialDisplayName, value ?? string.Empty);
    }

    public string CredentialFilePath
    {
        get => _credentialFilePath;
        set => SetProperty(ref _credentialFilePath, value ?? string.Empty);
    }

    public string HostDisplayName
    {
        get => _hostDisplayName;
        set => SetProperty(ref _hostDisplayName, value ?? string.Empty);
    }

    public string HostName
    {
        get => _hostName;
        set => SetProperty(ref _hostName, value ?? string.Empty);
    }

    public int HostPort
    {
        get => _hostPort;
        set => SetProperty(ref _hostPort, value);
    }

    public string HostUserName
    {
        get => _hostUserName;
        set => SetProperty(ref _hostUserName, value ?? string.Empty);
    }

    public RemoteHostOperatingSystem HostOperatingSystem
    {
        get => _hostOperatingSystem;
        set
        {
            if (SetProperty(ref _hostOperatingSystem, value)
                && string.IsNullOrWhiteSpace(HostDefaultRoot))
            {
                HostDefaultRoot = value == RemoteHostOperatingSystem.Windows
                    ? "C:\\"
                    : "/";
            }
        }
    }

    public string HostDefaultRoot
    {
        get => _hostDefaultRoot;
        set => SetProperty(ref _hostDefaultRoot, value ?? string.Empty);
    }

    public string HostCredentialId
    {
        get => _hostCredentialId;
        set => SetProperty(ref _hostCredentialId, value ?? string.Empty);
    }

    public string HostNotes
    {
        get => _hostNotes;
        set => SetProperty(ref _hostNotes, value ?? string.Empty);
    }

    public string HostKeyAlgorithm
    {
        get => _hostKeyAlgorithm;
        set => SetProperty(ref _hostKeyAlgorithm, value ?? string.Empty);
    }

    public string HostKeyFingerprint
    {
        get => _hostKeyFingerprint;
        set => SetProperty(ref _hostKeyFingerprint, value ?? string.Empty);
    }

    public string PendingReleaseCode
    {
        get => _pendingReleaseCode;
        private set
        {
            if (SetProperty(ref _pendingReleaseCode, value))
            {
                OnPropertyChanged(nameof(HasPendingRelease));
            }
        }
    }

    public string PendingReleaseUri
    {
        get => _pendingReleaseUri;
        private set => SetProperty(ref _pendingReleaseUri, value);
    }

    public bool HasPendingRelease =>
        !string.IsNullOrWhiteSpace(PendingReleaseCode);

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyActions();
            }
        }
    }

    public bool CanUse =>
        !IsBusy && _mainWindow.ProfileSyncSession.IsSignedIn;

    public bool CanRotateCredential =>
        CanUse && SelectedCredential is { IsRevoked: false };

    public bool CanUnlockCredential =>
        CanRotateCredential && SelectedCredential is { IsUnlocked: false };

    public bool CanRevokeCredential => CanRotateCredential;

    public bool CanSaveHost => CanUse;

    public bool CanDisableHost =>
        CanUse && SelectedHost is { IsDisabled: false };

    public bool CanPinHostKey => CanDisableHost;

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default,
        bool preserveNewHostEditor = false)
    {
        if (!CanUse)
        {
            return;
        }

        var selectedHostId = SelectedHost?.Id;
        var selectedCredentialId = SelectedCredential?.Id;
        IsBusy = true;
        Status = "Refreshing account-linked SSH hosts and credentials…";
        try
        {
            var snapshot = await _mainWindow.LoadLinkedHostInventoryAsync(
                cancellationToken);
            Hosts.Clear();
            foreach (var host in snapshot.Hosts)
            {
                Hosts.Add(new LinkedHostSettingsItem(host));
            }

            Credentials.Clear();
            foreach (var credential in snapshot.Credentials)
            {
                Credentials.Add(new SshCredentialSettingsItem(
                    credential,
                    snapshot.LocallyUnlockedCredentialIds.Contains(
                        credential.Id)));
            }

            SelectedHost = preserveNewHostEditor && selectedHostId is null
                ? null
                : Hosts.FirstOrDefault(item =>
                      string.Equals(
                          item.Id,
                          selectedHostId,
                          StringComparison.Ordinal))
                  ?? Hosts.FirstOrDefault(item => !item.IsDisabled)
                  ?? Hosts.FirstOrDefault();
            SelectedCredential = Credentials.FirstOrDefault(item =>
                                     string.Equals(
                                         item.Id,
                                         selectedCredentialId,
                                         StringComparison.Ordinal))
                                 ?? Credentials.FirstOrDefault(item =>
                                     !item.IsRevoked)
                                 ?? Credentials.FirstOrDefault();
            Status = preserveNewHostEditor && selectedHostId is null
                ? $"Ready to add a host. Loaded {Credentials.Count} available SSH credential(s)."
                : $"Loaded {Hosts.Count} linked host(s) and {Credentials.Count} SSH credential(s).";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"Linked-host refresh failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StoreCredentialAsync(
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        if (!CanUse)
        {
            return;
        }

        IsBusy = true;
        Status = "Validating, encrypting, and storing the SSH credential…";
        try
        {
            _ = await _mainWindow.StoreSshCredentialAsync(
                CredentialDisplayName,
                CredentialFilePath,
                passphrase,
                cancellationToken);
            CredentialFilePath = string.Empty;
            Status = "SSH credential stored. Release it separately on this PC before connecting.";
            IsBusy = false;
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"SSH credential enrollment failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginNewCredential()
    {
        SelectedCredential = null;
        CredentialDisplayName = string.Empty;
        CredentialFilePath = string.Empty;
        Status = "Choose an OpenSSH private-key file. The key is encrypted before it is stored.";
    }

    public async Task RotateSelectedCredentialAsync(
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        if (!CanRotateCredential || SelectedCredential is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Conditionally rotating the SSH credential…";
        try
        {
            _ = await _mainWindow.RotateSshCredentialAsync(
                SelectedCredential.Credential,
                CredentialDisplayName,
                CredentialFilePath,
                passphrase,
                cancellationToken);
            CredentialFilePath = string.Empty;
            Status = "SSH credential rotated. This PC must reauthenticate before using the new key.";
            IsBusy = false;
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"SSH credential rotation failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UnlockSelectedCredentialAsync(
        Action<SshCredentialReleaseAuthorization> authorizationRequired,
        CancellationToken cancellationToken = default)
    {
        if (!CanUnlockCredential || SelectedCredential is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Requesting a one-time credential release…";
        try
        {
            var outcome = await _mainWindow.UnlockSshCredentialAsync(
                SelectedCredential.Id,
                authorization =>
                {
                    PendingReleaseCode = authorization.UserCode;
                    PendingReleaseUri = authorization.VerificationUri
                                        + "?userCode="
                                        + Uri.EscapeDataString(
                                            authorization.UserCode);
                    Status =
                        "Re-enter your password and authenticator code in the portal. Hypershell will finish automatically.";
                    authorizationRequired(authorization);
                },
                cancellationToken);
            Status =
                $"Credential v{outcome.KeyVersion} is protected by Windows DPAPI for this user.";
            IsBusy = false;
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"SSH credential release failed: {exception.Message}";
        }
        finally
        {
            PendingReleaseCode = string.Empty;
            PendingReleaseUri = string.Empty;
            IsBusy = false;
        }
    }

    public async Task RevokeSelectedCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanRevokeCredential || SelectedCredential is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Revoking the SSH credential and removing its local copy…";
        try
        {
            await _mainWindow.RevokeSshCredentialAsync(
                SelectedCredential.Id,
                cancellationToken);
            IsBusy = false;
            await RefreshAsync(cancellationToken);
            Status = "SSH credential revoked. Existing server release requests are denied.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"SSH credential revocation failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginNewHost()
    {
        SelectedHost = null;
        HostDisplayName = string.Empty;
        HostName = string.Empty;
        HostPort = 22;
        HostUserName = string.Empty;
        HostOperatingSystem = RemoteHostOperatingSystem.Unknown;
        HostDefaultRoot = "/";
        HostCredentialId = string.Empty;
        HostNotes = string.Empty;
        HostKeyAlgorithm = "ssh-ed25519";
        HostKeyFingerprint = string.Empty;
        Status = "Enter the SSH host connection details, then save.";
    }

    public void ReportPresentationFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Status = exception is OperationCanceledException
            ? "Linked-host refresh timed out. The editor remains available; retry refresh when the server is reachable."
            : $"Linked-host settings stayed open, but refresh failed: {exception.Message}";
        IsBusy = false;
    }

    public async Task SaveHostAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanSaveHost)
        {
            return;
        }

        IsBusy = true;
        Status = SelectedHost is null
            ? "Creating the linked SSH host…"
            : "Updating the linked SSH host with a revision precondition…";
        try
        {
            var request = new RemoteHostMutationRequest
            {
                DisplayName = HostDisplayName,
                HostName = HostName,
                Port = HostPort,
                UserName = HostUserName,
                OperatingSystem = HostOperatingSystem,
                DefaultRoot = HostDefaultRoot,
                CredentialId = HostCredentialId,
                Notes = HostNotes,
                ExpectedRevision = SelectedHost?.Revision ?? 0
            };
            _ = SelectedHost is null
                ? await _mainWindow.CreateLinkedHostAsync(
                    request,
                    cancellationToken)
                : await _mainWindow.UpdateLinkedHostAsync(
                    SelectedHost.Id,
                    request,
                    cancellationToken);
            IsBusy = false;
            await RefreshAsync(cancellationToken);
            Status = "Linked host saved.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"Linked host save failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PinSelectedHostKeyAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanPinHostKey || SelectedHost is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Comparing and saving the SSH host-key fingerprint…";
        try
        {
            _ = await _mainWindow.PinLinkedHostKeyAsync(
                SelectedHost.Id,
                new RemoteHostKeyPinRequest
                {
                    Algorithm = HostKeyAlgorithm,
                    Fingerprint = HostKeyFingerprint,
                    ExpectedCurrentFingerprint =
                        SelectedHost.Host.HostKeyFingerprint ?? string.Empty
                },
                cancellationToken);
            IsBusy = false;
            await RefreshAsync(cancellationToken);
            Status = "SSH host-key pin saved. Future connections must match it exactly.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"SSH host-key pin was not changed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisableSelectedHostAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanDisableHost || SelectedHost is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _mainWindow.DisableLinkedHostAsync(
                SelectedHost.Id,
                cancellationToken);
            IsBusy = false;
            await RefreshAsync(cancellationToken);
            Status = "Linked host disabled.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Status = $"The linked host could not be disabled: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadSelectedHost()
    {
        if (SelectedHost is null)
        {
            return;
        }

        var host = SelectedHost.Host;
        HostDisplayName = host.DisplayName;
        HostName = host.HostName;
        HostPort = host.Port;
        HostUserName = host.UserName;
        HostOperatingSystem = host.OperatingSystem;
        HostDefaultRoot = host.DefaultRoot;
        HostCredentialId = host.CredentialId;
        HostNotes = host.Notes;
        HostKeyAlgorithm = host.HostKeyAlgorithm ?? "ssh-ed25519";
        HostKeyFingerprint = host.HostKeyFingerprint ?? string.Empty;
    }

    private void OnProfileSessionPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ProfileSyncSessionService.IsSignedIn)
            or nameof(ProfileSyncSessionService.State)
            or nameof(ProfileSyncSessionService.IsBusy))
        {
            NotifyActions();
            if (!_mainWindow.ProfileSyncSession.IsSignedIn)
            {
                Status = "Sign in before managing linked SSH hosts.";
            }
        }
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanUse));
        OnPropertyChanged(nameof(CanRotateCredential));
        OnPropertyChanged(nameof(CanUnlockCredential));
        OnPropertyChanged(nameof(CanRevokeCredential));
        OnPropertyChanged(nameof(CanSaveHost));
        OnPropertyChanged(nameof(CanDisableHost));
        OnPropertyChanged(nameof(CanPinHostKey));
    }

    private static bool IsExpected(Exception exception) =>
        exception is ProfileSyncException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or OperationCanceledException;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mainWindow.ProfileSyncSession.PropertyChanged -=
            OnProfileSessionPropertyChanged;
    }
}

public sealed class LinkedHostSettingsItem
{
    public LinkedHostSettingsItem(RemoteHostSummary host)
    {
        Host = host;
    }

    public RemoteHostSummary Host { get; }

    public string Id => Host.Id;

    public string DisplayName => Host.DisplayName;

    public long Revision => Host.Revision;

    public bool IsDisabled => Host.IsDisabled;

    public string ConnectionText =>
        $"{Host.UserName}@{Host.HostName}:{Host.Port}  ·  {Host.DefaultRoot}";

    public string SecurityText => Host.IsDisabled
        ? "DISABLED"
        : string.IsNullOrWhiteSpace(Host.HostKeyFingerprint)
            ? "HOST KEY NOT PINNED"
            : $"{Host.HostKeyAlgorithm}  {Host.HostKeyFingerprint}";
}

public sealed class SshCredentialSettingsItem
{
    public SshCredentialSettingsItem(
        SshCredentialSummary credential,
        bool isUnlocked)
    {
        Credential = credential;
        IsUnlocked = isUnlocked;
    }

    public SshCredentialSummary Credential { get; }

    public string Id => Credential.Id;

    public string DisplayName => Credential.DisplayName;

    public int KeyVersion => Credential.KeyVersion;

    public bool IsRevoked => Credential.IsRevoked;

    public bool IsUnlocked { get; }

    public string IdentityText =>
        $"v{KeyVersion}  ·  {Credential.PublicKeyFingerprint}";

    public string StateText => IsRevoked
        ? "REVOKED"
        : IsUnlocked
            ? "READY ON THIS PC"
            : "PORTAL RELEASE REQUIRED";
}
