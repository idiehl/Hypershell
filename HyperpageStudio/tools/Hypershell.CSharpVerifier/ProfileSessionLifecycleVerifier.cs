using Hypershell.Profiles;
using Hypershell.Services;
using System.Text;

internal static class ProfileSessionLifecycleVerifier
{
    private const string Endpoint =
        "https://profiles.example.test/production/";
    private const string AccountId = "lifecycle.test";
    private const string AccessKey =
        "hpk_lifecycle-key-must-remain-dpapi-protected";

    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(
            verifierRoot,
            "profile-session-lifecycle");
        Directory.CreateDirectory(root);
        var deviceState = new ProfileSyncDeviceStateService(
            Path.Combine(root, "profile-sync.device.json"),
            Path.Combine(root, "profile-sync-checkpoints.device.json"),
            Path.Combine(root, "conflicts"));
        var configuration = new ProfileSyncDeviceConfiguration
        {
            ServerEndpoint = Endpoint,
            AccountId = AccountId
        };
        deviceState.SaveConfiguration(configuration);

        var credentialPath = Path.Combine(
            root,
            "profile-sync-credential.device.bin");
        var credentialStore =
            new DpapiProfileSyncCredentialStore(credentialPath);
        credentialStore.Save(configuration, AccessKey);
        Require(
            credentialStore.Exists,
            "The protected profile credential was not created.");
        var protectedBytes = File.ReadAllBytes(credentialPath);
        Require(
            !Contains(protectedBytes, Encoding.UTF8.GetBytes(AccessKey))
            && !Contains(protectedBytes, Encoding.UTF8.GetBytes(AccountId)),
            "The DPAPI credential file contains plaintext account material.");
        var recovered = credentialStore.Load();
        Require(
            recovered is not null
            && recovered.ServerEndpoint == Endpoint
            && recovered.AccountId == AccountId
            && recovered.AccessKey == AccessKey,
            "The current Windows user could not recover the protected credential.");

        var clock = new VerifierTimeProvider(
            new DateTimeOffset(
                2026,
                8,
                2,
                12,
                0,
                0,
                TimeSpan.Zero));
        var authority = new ScriptedSessionAuthority(clock);
        using var session = new ProfileSyncSessionService(
            deviceState,
            credentialStore,
            _ => authority.CreateClient(),
            clock);

        Require(
            await session.InitializeAsync()
            && session.IsSignedIn
            && session.State == ProfileSyncConnectionState.SignedIn
            && session.HasSavedCredential
            && authority.SignInCount == 1,
            "A protected credential did not restore an application session.");
        var firstToken = await session.GetAccessTokenAsync();
        Require(
            firstToken == "hps_lifecycle-1",
            "The restored session token was not returned.");

        clock.Advance(TimeSpan.FromMinutes(9));
        var renewedToken = await session.GetAccessTokenAsync();
        Require(
            renewedToken == "hps_lifecycle-2"
            && authority.SignInCount == 2,
            "An expiring session was not renewed with the protected key.");

        session.InvalidateSession(
            "Synthetic server restart invalidated the bearer token.");
        authority.NextFailure = ProfileSyncFailureKind.Network;
        Require(
            !await session.InitializeAsync()
            && session.State == ProfileSyncConnectionState.Offline
            && session.HasSavedCredential,
            "An offline startup did not preserve the protected credential and local-first state.");

        authority.NextFailure = null;
        var restoredToken = await session.GetAccessTokenAsync();
        Require(
            restoredToken == "hps_lifecycle-3"
            && session.IsSignedIn,
            "The saved credential did not recover after the server returned.");

        await session.SignOutAsync(forgetCredential: false);
        Require(
            !session.IsSignedIn
            && session.HasSavedCredential
            && authority.SignOutCount == 1,
            "Application shutdown semantics did not retain the protected key while revoking the session.");
        Require(
            await session.InitializeAsync()
            && authority.SignInCount == 4,
            "The retained key did not restore the next application session.");

        await session.SignOutAsync(forgetCredential: true);
        Require(
            !session.IsSignedIn
            && !session.HasSavedCredential
            && !File.Exists(credentialPath)
            && authority.SignOutCount == 2,
            "Explicit sign-out did not revoke the session and forget the saved key.");

        credentialStore.Save(configuration, AccessKey);
        authority.NextFailure = ProfileSyncFailureKind.Authentication;
        Require(
            !await session.InitializeAsync()
            && !session.HasSavedCredential
            && session.State == ProfileSyncConnectionState.SignedOut,
            "A server-rejected saved key was not removed from the device.");

        Require(
            !Directory.EnumerateFiles(
                    root,
                    "*",
                    SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                    path,
                    credentialPath,
                    StringComparison.OrdinalIgnoreCase))
                .Any(path => Contains(
                    File.ReadAllBytes(path),
                    Encoding.UTF8.GetBytes(AccessKey))),
            "The profile access key leaked outside the DPAPI credential boundary.");

        await VerifyDeviceAuthorizationAsync(root, clock);
    }

    private static async Task VerifyDeviceAuthorizationAsync(
        string root,
        VerifierTimeProvider clock)
    {
        var deviceRoot = Path.Combine(root, "browser-authorized-device");
        Directory.CreateDirectory(deviceRoot);
        var deviceState = new ProfileSyncDeviceStateService(
            Path.Combine(deviceRoot, "profile-sync.device.json"),
            Path.Combine(
                deviceRoot,
                "profile-sync-checkpoints.device.json"),
            Path.Combine(deviceRoot, "conflicts"));
        var credentialPath = Path.Combine(
            deviceRoot,
            "profile-sync-credential.device.bin");
        var credentialStore =
            new DpapiProfileSyncCredentialStore(credentialPath);
        var authority = new ScriptedCloudAuthority(clock);
        DeviceAuthorizationResponse? observedChallenge = null;

        using (var initialSession = new ProfileSyncSessionService(
                   deviceState,
                   credentialStore,
                   clientFactory: _ => authority.CreateSessionClient(),
                   timeProvider: clock,
                   cloudClientFactory: _ => authority.CreateClient()))
        {
            var approved = await initialSession.AuthorizeDeviceAsync(
                Endpoint,
                "Verifier PC",
                "Windows verifier",
                challenge => observedChallenge = challenge);
            Require(
                observedChallenge is not null
                && observedChallenge.UserCode == "ABCD-1234"
                && approved.AccountId == AccountId
                && initialSession.IsSignedIn
                && authority.StartCount == 1
                && authority.PollCount == 1,
                "Browser sign-in did not establish the initial cloud session.");
            await initialSession.SignOutAsync(forgetCredential: false);
            Require(
                !initialSession.IsSignedIn
                && credentialStore.Exists
                && authority.SessionCloseCount == 1,
                "Closing Hypershell did not retain the protected credential for automatic sign-in on the next launch.");
        }

        var firstCredential = credentialStore.Load();
        Require(
            firstCredential is
            {
                Kind: ProfileSyncCredentialKind.DeviceRefreshToken,
                ClientDeviceId.Length: 32
            }
            && firstCredential.RefreshToken == authority.CurrentRefreshToken
            && firstCredential.DeviceFingerprint.StartsWith(
                "hsi_",
                StringComparison.Ordinal),
            "The signed-in PC did not persist its rotating credential identity.");
        var firstRefreshToken = firstCredential!.RefreshToken;
        var protectedBytes = File.ReadAllBytes(credentialPath);
        Require(
            !Contains(
                protectedBytes,
                Encoding.UTF8.GetBytes(firstRefreshToken))
            && !Contains(
                protectedBytes,
                Encoding.UTF8.GetBytes(AccountId)),
            "The browser-authorized DPAPI file contains plaintext account material.");

        using (var restoredSession = new ProfileSyncSessionService(
                   deviceState,
                   credentialStore,
                   clientFactory: _ => authority.CreateSessionClient(),
                   timeProvider: clock,
                   cloudClientFactory: _ => authority.CreateClient()))
        {
            Require(
                await restoredSession.InitializeAsync()
                && restoredSession.IsSignedIn
                && authority.RefreshCount == 1
                && authority.LastRefreshToken == firstRefreshToken,
                "A browser-signed-in PC did not restore and rotate its cloud session at startup.");
            var secondCredential = credentialStore.Load();
            Require(
                secondCredential is
                {
                    Kind: ProfileSyncCredentialKind.DeviceRefreshToken
                }
                && secondCredential.RefreshToken
                == authority.CurrentRefreshToken
                && secondCredential.RefreshToken != firstRefreshToken,
                "Startup restoration did not replace the one-time refresh credential.");

            var secondRefreshToken = secondCredential!.RefreshToken;
            clock.Advance(TimeSpan.FromMinutes(9));
            _ = await restoredSession.GetAccessTokenAsync();
            Require(
                authority.RefreshCount == 2
                && authority.LastRefreshToken == secondRefreshToken
                && credentialStore.Load()?.RefreshToken
                == authority.CurrentRefreshToken,
                "An expiring cloud session did not rotate its refresh credential.");

            await restoredSession.SignOutAsync(forgetCredential: true);
            Require(
                authority.RevokeCount == 1
                && !credentialStore.Exists
                && !restoredSession.IsSignedIn,
                "Revoking this PC did not invalidate the remote device and remove its DPAPI credential.");
        }

        authority.KeepPending = true;
        using var cancellationSession = new ProfileSyncSessionService(
            deviceState,
            credentialStore,
            timeProvider: clock,
            cloudClientFactory: _ => authority.CreateClient());
        using var cancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            _ = await cancellationSession.AuthorizeDeviceAsync(
                Endpoint,
                "Cancelled verifier PC",
                "Windows verifier",
                _ => { },
                cancellation.Token);
            throw new InvalidOperationException(
                "A cancelled device authorization unexpectedly succeeded.");
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == ProfileSyncFailureKind.Cancelled)
        {
            Require(
                cancellationSession.State
                == ProfileSyncConnectionState.SignedOut,
                "Cancellation did not return the profile session to a signed-out state.");
        }
    }

    private static bool Contains(
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> value) =>
        payload.IndexOf(value) >= 0;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ScriptedSessionAuthority
    {
        private readonly TimeProvider _timeProvider;

        public ScriptedSessionAuthority(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public int SignInCount { get; private set; }
        public int SignOutCount { get; private set; }
        public ProfileSyncFailureKind? NextFailure { get; set; }

        public IProfileSessionClient CreateClient() =>
            new ScriptedSessionClient(this);

        private Task<ProfileSessionResponse> SignInAsync(
            string accountId,
            string accessKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextFailure is { } failure)
            {
                NextFailure = null;
                return Task.FromException<ProfileSessionResponse>(
                    new ProfileSyncException(
                        failure,
                        $"Synthetic {failure} sign-in failure."));
            }

            if (accountId != AccountId || accessKey != AccessKey)
            {
                return Task.FromException<ProfileSessionResponse>(
                    new ProfileSyncException(
                        ProfileSyncFailureKind.Authentication,
                        "Synthetic invalid credentials."));
            }

            SignInCount++;
            return Task.FromResult(
                new ProfileSessionResponse
                {
                    AccountId = AccountId,
                    AccountDisplayName = "Lifecycle verifier",
                    AccessToken = $"hps_lifecycle-{SignInCount}",
                    ExpiresAtUtc = _timeProvider.GetUtcNow()
                        .AddMinutes(10)
                });
        }

        private Task SignOutAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!accessToken.StartsWith(
                    "hps_lifecycle-",
                    StringComparison.Ordinal))
            {
                return Task.FromException(
                    new ProfileSyncException(
                        ProfileSyncFailureKind.Authentication,
                        "Synthetic invalid session token."));
            }

            SignOutCount++;
            return Task.CompletedTask;
        }

        private sealed class ScriptedSessionClient
            : IProfileSessionClient
        {
            private readonly ScriptedSessionAuthority _authority;

            public ScriptedSessionClient(
                ScriptedSessionAuthority authority)
            {
                _authority = authority;
            }

            public Task<ProfileSessionResponse> SignInAsync(
                string accountId,
                string accessKey,
                CancellationToken cancellationToken = default) =>
                _authority.SignInAsync(
                    accountId,
                    accessKey,
                    cancellationToken);

            public Task SignOutAsync(
                string accessToken,
                CancellationToken cancellationToken = default) =>
                _authority.SignOutAsync(
                    accessToken,
                    cancellationToken);

            public ValueTask DisposeAsync() =>
                ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedCloudAuthority
    {
        private const string ClientDeviceId =
            "0123456789abcdef0123456789abcdef";

        private readonly TimeProvider _timeProvider;
        private int _issuedSessionCount;

        public ScriptedCloudAuthority(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public int StartCount { get; private set; }
        public int PollCount { get; private set; }
        public int RefreshCount { get; private set; }
        public int RevokeCount { get; private set; }
        public int SessionCloseCount { get; private set; }
        public bool KeepPending { get; set; }
        public string CurrentRefreshToken { get; private set; } =
            string.Empty;
        public string LastRefreshToken { get; private set; } =
            string.Empty;

        public ICloudDeviceAuthorizationClient CreateClient() =>
            new ScriptedCloudClient(this);

        public IProfileSessionClient CreateSessionClient() =>
            new ScriptedProfileSessionClient(this);

        private Task<DeviceAuthorizationResponse> StartAsync(
            DeviceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                request.DeviceName.Length > 0
                && request.Platform.Length > 0
                && request.DeviceFingerprint.StartsWith(
                    "hsi_",
                    StringComparison.Ordinal),
                "The desktop did not provide bounded device metadata.");
            StartCount++;
            return Task.FromResult(
                new DeviceAuthorizationResponse
                {
                    DeviceCode = "hsd_" + new string('d', 44),
                    UserCode = "ABCD-1234",
                    VerificationUri =
                        "https://profiles.example.test/Device",
                    ExpiresAtUtc = _timeProvider.GetUtcNow()
                        .AddMinutes(10),
                    PollIntervalSeconds = 2
                });
        }

        private Task<CloudDeviceAuthorizationPollResult> PollAsync(
            string deviceCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                deviceCode.StartsWith("hsd_", StringComparison.Ordinal),
                "The desktop polled with the wrong device code.");
            PollCount++;
            return Task.FromResult(
                KeepPending
                    ? new CloudDeviceAuthorizationPollResult(
                        CloudDeviceAuthorizationPollKind.Pending)
                    : new CloudDeviceAuthorizationPollResult(
                        CloudDeviceAuthorizationPollKind.Approved,
                        IssueSession()));
        }

        private Task<CloudSessionResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRefreshToken = refreshToken;
            if (!string.Equals(
                    refreshToken,
                    CurrentRefreshToken,
                    StringComparison.Ordinal))
            {
                return Task.FromException<CloudSessionResponse>(
                    new ProfileSyncException(
                        ProfileSyncFailureKind.Authentication,
                        "Synthetic refresh credential reuse."));
            }

            RefreshCount++;
            return Task.FromResult(IssueSession());
        }

        private Task RevokeAsync(
            string accessToken,
            string clientDeviceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                accessToken.StartsWith("hps_", StringComparison.Ordinal)
                && clientDeviceId == ClientDeviceId,
                "The desktop attempted to revoke the wrong cloud device.");
            RevokeCount++;
            return Task.CompletedTask;
        }

        private Task CloseSessionAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                accessToken.StartsWith("hps_", StringComparison.Ordinal),
                "The desktop attempted to close the wrong cloud session.");
            SessionCloseCount++;
            return Task.CompletedTask;
        }

        private CloudSessionResponse IssueSession()
        {
            _issuedSessionCount++;
            CurrentRefreshToken =
                $"hsr_{_issuedSessionCount:D4}{new string('r', 44)}";
            return new CloudSessionResponse
            {
                AccountId = AccountId,
                AccountDisplayName = "Lifecycle verifier",
                ClientDeviceId = ClientDeviceId,
                AccessToken =
                    $"hps_{_issuedSessionCount:D4}{new string('a', 44)}",
                RefreshToken = CurrentRefreshToken,
                AccessTokenExpiresAtUtc = _timeProvider.GetUtcNow()
                    .AddMinutes(10),
                RefreshTokenExpiresAtUtc = _timeProvider.GetUtcNow()
                    .AddDays(30)
            };
        }

        private sealed class ScriptedCloudClient
            : ICloudDeviceAuthorizationClient
        {
            private readonly ScriptedCloudAuthority _authority;

            public ScriptedCloudClient(
                ScriptedCloudAuthority authority)
            {
                _authority = authority;
            }

            public Task<DeviceAuthorizationResponse> StartAsync(
                DeviceAuthorizationRequest request,
                CancellationToken cancellationToken = default) =>
                _authority.StartAsync(request, cancellationToken);

            public Task<CloudDeviceAuthorizationPollResult> PollAsync(
                string deviceCode,
                CancellationToken cancellationToken = default) =>
                _authority.PollAsync(deviceCode, cancellationToken);

            public Task<CloudSessionResponse> RefreshAsync(
                string refreshToken,
                CancellationToken cancellationToken = default) =>
                _authority.RefreshAsync(refreshToken, cancellationToken);

            public Task RevokeDeviceAsync(
                string accessToken,
                string clientDeviceId,
                CancellationToken cancellationToken = default) =>
                _authority.RevokeAsync(
                    accessToken,
                    clientDeviceId,
                    cancellationToken);

            public ValueTask DisposeAsync() =>
                ValueTask.CompletedTask;
        }

        private sealed class ScriptedProfileSessionClient
            : IProfileSessionClient
        {
            private readonly ScriptedCloudAuthority _authority;

            public ScriptedProfileSessionClient(
                ScriptedCloudAuthority authority)
            {
                _authority = authority;
            }

            public Task<ProfileSessionResponse> SignInAsync(
                string accountId,
                string accessKey,
                CancellationToken cancellationToken = default) =>
                Task.FromException<ProfileSessionResponse>(
                    new InvalidOperationException(
                        "The browser credential should restore through refresh rotation."));

            public Task SignOutAsync(
                string accessToken,
                CancellationToken cancellationToken = default) =>
                _authority.CloseSessionAsync(
                    accessToken,
                    cancellationToken);

            public ValueTask DisposeAsync() =>
                ValueTask.CompletedTask;
        }
    }

    private sealed class VerifierTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public VerifierTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            _utcNow = _utcNow.Add(duration);
        }
    }
}
