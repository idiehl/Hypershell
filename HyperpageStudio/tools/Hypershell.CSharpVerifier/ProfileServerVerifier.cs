using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.ProfileServer;
using Hypershell.Profiles;
using Hypershell.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class ProfileServerVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "profile-server");
        Directory.CreateDirectory(root);
        var protectionFiles = CreateDataProtectionCertificate(root);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var options = new ProfileServerOptions
        {
            DataRoot = Path.Combine(root, "server-data"),
            SessionLifetime = TimeSpan.FromMinutes(10),
            MaximumAccountStorageBytes = 32L * 1024 * 1024,
            CredentialHashIterations = 100_000,
            RequireHttpsPortalCookies = false,
            DataProtectionCertificatePath = protectionFiles.CertificatePath,
            DataProtectionCertificatePasswordPath = protectionFiles.PasswordPath
        }.Normalize();
        var accounts = new ProfileAccountStore(options);
        var alice = await accounts.CreateAsync("alice.test", "Alice Test");
        var bob = await accounts.CreateAsync("bob.test", "Bob Test");
        Require(
            !File.ReadAllText(Path.Combine(options.DataRoot, "accounts.json"))
                .Contains(alice.AccessKey, StringComparison.Ordinal)
            && !File.ReadAllText(Path.Combine(options.DataRoot, "accounts.json"))
                .Contains(bob.AccessKey, StringComparison.Ordinal),
            "A plaintext account access key was stored in the account registry.");

        var portableProfiles = new PortableProfileService();
        var extensions = new ExtensionDiscoverySnapshot
        {
            RootDirectory = Path.Combine(root, "extensions"),
            ScannedAtUtc = clock.GetUtcNow()
        };
        var settings = CreateSettings(clock.GetUtcNow());
        var firstPayload = CreatePayload(
            portableProfiles,
            settings,
            extensions);

        var app = await StartServerAsync(options, clock);
        var endpoint = ResolveEndpoint(app);
        var cloudStore = app.Services.GetRequiredService<CloudMetadataStore>();
        var cloudStatus = await cloudStore.InspectAsync();
        Require(
            cloudStatus.SchemaVersion == CloudMetadataStore.CurrentSchemaVersion
            && cloudStatus.WriteAheadLoggingEnabled
            && File.Exists(Path.Combine(
                options.DataRoot,
                cloudStatus.DatabaseFileName))
            && Directory.Exists(Path.Combine(options.DataRoot, "blobs", "packages"))
            && Directory.Exists(Path.Combine(options.DataRoot, "blobs", "releases")),
            "The versioned cloud metadata and content-store foundation was not initialized.");
        var accountSecurity = app.Services
            .GetRequiredService<AccountSecurityStore>();
        var renameSource = await accounts.CreateAsync(
            "rename.source",
            "Rename verifier");
        const string renamePassword = "R3name!#";
        var renameEnrollment = await accountSecurity.SetPasswordAsync(
            renameSource.AccountId,
            renamePassword);
        var renameTotp = AccountSecurityStore.GenerateTotpForTesting(
            renameEnrollment.TotpSecret,
            clock.GetUtcNow());
        var renameService = new AccountIdentityRenameService(
            cloudStore,
            accounts);
        var renameResult = await renameService.RenameBeforeUseAsync(
            renameSource.AccountId,
            "rename.target");
        var renamedPassword = await accountSecurity.VerifyAsync(
            renameResult.AccountId,
            renamePassword,
            renameTotp);
        Require(
            renameResult is
            {
                PreviousAccountId: "rename.source",
                AccountId: "rename.target",
                SecurityEnrollmentMigrated: true
            }
            && renamedPassword.Succeeded
            && await accounts.FindAsync("rename.source") is null
            && await accounts.VerifyAccessKeyAsync(
                "rename.target",
                renameSource.AccessKey) is not null
            && !await accountSecurity.IsEnrolledAsync("rename.source"),
            "The before-use account rename did not preserve credentials and enrollment under the replacement login ID.");
        var shortPasswordRejected = false;
        try
        {
            _ = await accountSecurity.SetPasswordAsync(
                alice.AccountId,
                "A!b2#c3");
        }
        catch (ArgumentException exception) when (
            exception.ParamName == "password")
        {
            shortPasswordRejected = true;
        }

        Require(
            shortPasswordRejected,
            "The account security store accepted a password shorter than eight characters.");
        const string verifierPassword = "A!b2#c3$";
        var enrollment = await accountSecurity.SetPasswordAsync(
            alice.AccountId,
            verifierPassword);
        Require(
            enrollment.TotpSecret.Length == 32
            && enrollment.RecoveryCodes.Count == 10
            && enrollment.ProvisioningUri.StartsWith(
                "otpauth://totp/",
                StringComparison.Ordinal),
            "Password and TOTP enrollment did not return bounded recovery material.");
        var totp = AccountSecurityStore.GenerateTotpForTesting(
            enrollment.TotpSecret,
            clock.GetUtcNow());
        var invalidPassword = await accountSecurity.VerifyAsync(
            alice.AccountId,
            "incorrect verifier password",
            totp);
        var validTotp = await accountSecurity.VerifyAsync(
            alice.AccountId,
            verifierPassword,
            totp);
        var recoveryCode = enrollment.RecoveryCodes[0];
        var validRecovery = await accountSecurity.VerifyAsync(
            alice.AccountId,
            verifierPassword,
            recoveryCode);
        var reusedRecovery = await accountSecurity.VerifyAsync(
            alice.AccountId,
            verifierPassword,
            recoveryCode);
        Require(
            !invalidPassword.Succeeded
            && validTotp is { Succeeded: true, UsedRecoveryCode: false }
            && validRecovery is { Succeeded: true, UsedRecoveryCode: true }
            && !reusedRecovery.Succeeded,
            "Password, TOTP, or one-time recovery-code verification failed.");
        Require(
            !Directory.EnumerateFiles(
                    options.DataRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Any(path => FileContainsUtf8(path, verifierPassword)
                             || FileContainsUtf8(path, enrollment.TotpSecret)
                             || FileContainsUtf8(path, recoveryCode)),
            "Account password or second-factor material leaked into persistent state.");
        ProfileSessionResponse aliceSession = null!;
        RemoteProfileSummary aliceRevived = null!;
        RoamingCloudVerificationState roamingState = null!;
        try
        {
            using (var rawClient = new HttpClient
                   {
                       BaseAddress = new Uri(endpoint)
                   })
            {
                using var portalResponse = await rawClient.GetAsync("/");
                var portalHtml = await portalResponse.Content.ReadAsStringAsync();
                Require(
                    portalResponse.IsSuccessStatusCode
                    && portalResponse.RequestMessage?.RequestUri?.AbsolutePath
                        == "/Account/Login"
                    && portalHtml.Contains(
                        "Hypershell Admin",
                        StringComparison.Ordinal)
                    && portalHtml.Contains(
                        "Authenticator or recovery code",
                        StringComparison.Ordinal)
                    && !portalHtml.Contains(
                        "Create account",
                        StringComparison.OrdinalIgnoreCase),
                    "The private portal did not require and render account sign-in.");

                using var wrongMediaType = new StringContent(
                    "{}",
                    System.Text.Encoding.UTF8,
                    "text/plain");
                using var wrongMediaTypeResponse = await rawClient.PostAsync(
                    ProfileSessionContract.RelativeSessionPath,
                    wrongMediaType);
                Require(
                    wrongMediaTypeResponse.StatusCode
                    == System.Net.HttpStatusCode.UnsupportedMediaType,
                    "The sign-in endpoint accepted a non-JSON request.");

                using var unknownMember = new StringContent(
                    """
                    {
                      "protocolVersion": 1,
                      "accountId": "alice.test",
                      "accessKey": "hpk_invalid-access-key",
                      "unexpected": true
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json");
                using var unknownMemberResponse = await rawClient.PostAsync(
                    ProfileSessionContract.RelativeSessionPath,
                    unknownMember);
                Require(
                    unknownMemberResponse.StatusCode
                    == System.Net.HttpStatusCode.BadRequest,
                    "The sign-in endpoint accepted an unknown JSON member.");
            }

            await VerifyAdminPortalPagesAsync(
                endpoint,
                alice.AccountId,
                verifierPassword,
                totp);

            await ExpectFailureAsync(
                async () =>
                {
                    await using var invalidClient =
                        new HttpProfileSessionClient(endpoint);
                    await invalidClient.SignInAsync(
                        alice.AccountId,
                        "hpk_invalid-access-key");
                },
                ProfileSyncFailureKind.Authentication);

            await using (var sessionClient =
                         new HttpProfileSessionClient(endpoint))
            {
                aliceSession = await sessionClient.SignInAsync(
                    alice.AccountId,
                    alice.AccessKey);
            }

            Require(
                aliceSession.AccountId == alice.AccountId
                && aliceSession.AccessToken.StartsWith(
                    "hps_",
                    StringComparison.Ordinal),
                "The server did not issue the expected account-scoped session.");

            using (var cloudClient = new HttpClient
                   {
                       BaseAddress = new Uri(endpoint)
                   })
            {
                cloudClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        aliceSession.AccessToken);
                var storage = await cloudClient.GetFromJsonAsync<CloudStorageStatus>(
                    HypershellCloudContract.RelativeStoragePath);
                Require(
                    storage is
                    {
                        ProtocolVersion: HypershellCloudContract.CurrentProtocolVersion,
                        CanAcceptWrites: true,
                        MetadataSchemaVersion: CloudMetadataStore.CurrentSchemaVersion
                    }
                    && storage.AvailableBytes > storage.ReservedBytes,
                    "The authenticated cloud storage status was not available.");
            }

            await VerifyDeviceAuthorizationAsync(
                app,
                endpoint,
                alice,
                verifierPassword,
                totp,
                options.DataRoot);

            RemoteProfileSummary aliceCreated;
            await using (var aliceTransport = new HttpProfileSyncTransport(
                             endpoint,
                             aliceSession.AccessToken))
            {
                var created = await aliceTransport.PutAsync(
                    settings.PortableProfileId,
                    firstPayload,
                    expectedETag: null);
                Require(
                    created.Status == ProfileSyncMutationStatus.Created
                    && created.Profile is { ServerRevision: 1 },
                    "The live server did not conditionally create Alice's profile.");
                aliceCreated = created.Profile!;
                var listed = await aliceTransport.ListAsync();
                Require(
                    listed.Count == 1
                    && listed[0].Id == settings.PortableProfileId,
                    "Alice could not list her newly created profile.");
                var fetched = await aliceTransport.FetchAsync(
                    settings.PortableProfileId);
                Require(
                    fetched.Payload.Span.SequenceEqual(firstPayload.Span)
                    && fetched.ETag == aliceCreated.ETag,
                    "The live server did not return Alice's exact stored bundle and ETag.");
                var stale = await aliceTransport.PutAsync(
                    settings.PortableProfileId,
                    firstPayload,
                    "\"stale-etag\"");
                Require(
                    stale.Status == ProfileSyncMutationStatus.Conflict,
                    "The live server accepted a stale profile update.");
            }

            ConvertCurrentPayloadToLegacyLayout(options.DataRoot);
            var migratedAccounts = await app.Services
                .GetRequiredService<FileProfileStore>()
                .MigrateAllAccountsToCurrentStateAsync();
            await using (var aliceTransport = new HttpProfileSyncTransport(
                             endpoint,
                             aliceSession.AccessToken))
            {
                Require(
                    migratedAccounts == 1
                    && (await aliceTransport.ListAsync()).Count == 1,
                    "The server could not migrate a legacy settings payload.");
                var storedPayload = GetStoredProfilePayloads(options.DataRoot)
                    .Single();
                Require(
                    Path.GetFileName(storedPayload)
                        == "current-00000000000000000001.hyperprofile"
                    && !Directory.Exists(
                        Path.Combine(
                            Path.GetDirectoryName(storedPayload)!,
                            "revisions")),
                    "Legacy profile revisions were not migrated to one current payload.");
            }

            settings.Application.ShowSaveStatusInTitleBar = false;
            settings.PortableProfileRevision++;
            settings.PortableProfileUpdatedAtUtc =
                settings.PortableProfileUpdatedAtUtc.AddMinutes(1);
            var secondPayload = CreatePayload(
                portableProfiles,
                settings,
                extensions);
            RemoteProfileSummary aliceUpdated;
            await using (var aliceTransport = new HttpProfileSyncTransport(
                             endpoint,
                             aliceSession.AccessToken))
            {
                var updated = await aliceTransport.PutAsync(
                    settings.PortableProfileId,
                    secondPayload,
                    aliceCreated.ETag);
                Require(
                    updated.Status == ProfileSyncMutationStatus.Updated
                    && updated.Profile is { ServerRevision: 2 },
                    "The live server did not accept a current ETag update.");
                aliceUpdated = updated.Profile!;
                Require(
                    CountStoredProfilePayloads(options.DataRoot) == 1,
                    "Updating a profile retained a superseded settings payload.");
            }

            ProfileSessionResponse bobSession;
            await using (var sessionClient =
                         new HttpProfileSessionClient(endpoint))
            {
                bobSession = await sessionClient.SignInAsync(
                    bob.AccountId,
                    bob.AccessKey);
            }

            await using (var bobTransport = new HttpProfileSyncTransport(
                             endpoint,
                             bobSession.AccessToken))
            {
                Require(
                    (await bobTransport.ListAsync()).Count == 0,
                    "Bob could see Alice's profile metadata.");
                await ExpectFailureAsync(
                    () => bobTransport.FetchAsync(settings.PortableProfileId),
                    ProfileSyncFailureKind.NotFound);
                var bobCreated = await bobTransport.PutAsync(
                    settings.PortableProfileId,
                    firstPayload,
                    expectedETag: null);
                Require(
                    bobCreated.Status == ProfileSyncMutationStatus.Created
                    && bobCreated.Profile is { ServerRevision: 1 },
                    "Bob could not create an isolated profile with the same ID.");
            }

            roamingState = await RoamingCloudVerifier.VerifyAsync(
                app,
                endpoint,
                aliceSession.AccessToken,
                bobSession.AccessToken,
                root);
            await SshVaultVerifier.VerifyAsync(
                app,
                endpoint,
                alice,
                bobSession.AccessToken,
                accountSecurity,
                verifierPassword,
                enrollment.TotpSecret,
                clock,
                options.DataRoot);

            await using (var aliceTransport = new HttpProfileSyncTransport(
                             endpoint,
                             aliceSession.AccessToken))
            {
                var listed = await aliceTransport.ListAsync();
                Require(
                    listed.Count == 1
                    && listed[0].ProfileSha256 == aliceUpdated.ProfileSha256,
                    "Bob's isolated mutation changed Alice's profile metadata.");
                var deleted = await aliceTransport.DeleteAsync(
                    settings.PortableProfileId,
                    aliceUpdated.ETag);
                Require(
                    deleted.Status == ProfileSyncMutationStatus.Deleted
                    && deleted.Profile is
                    {
                        IsDeleted: true,
                        ServerRevision: 3
                    },
                    "The live server did not create Alice's tombstone.");
                Require(
                    CountStoredProfilePayloads(options.DataRoot) == 1,
                    "Deleting Alice's settings retained her prior payload.");
                await ExpectFailureAsync(
                    () => aliceTransport.FetchAsync(settings.PortableProfileId),
                    ProfileSyncFailureKind.NotFound);
                var blockedCreate = await aliceTransport.PutAsync(
                    settings.PortableProfileId,
                    secondPayload,
                    expectedETag: null);
                Require(
                    blockedCreate.Status == ProfileSyncMutationStatus.Conflict,
                    "A tombstone did not block an offline-style conditional create.");
                var revived = await aliceTransport.PutAsync(
                    settings.PortableProfileId,
                    secondPayload,
                    deleted.Profile!.ETag);
                Require(
                    revived.Status == ProfileSyncMutationStatus.Updated
                    && revived.Profile is
                    {
                        IsDeleted: false,
                        ServerRevision: 4
                    },
                    "A current tombstone ETag could not intentionally revive the profile.");
                aliceRevived = revived.Profile!;
            }

            await ExpectFailureAsync(
                async () =>
                {
                    var tampered = firstPayload.ToArray();
                    var marker = System.Text.Encoding.UTF8.GetBytes(
                        "Verifier server profile");
                    var markerIndex = tampered.AsSpan().IndexOf(marker);
                    Require(markerIndex >= 0, "The verifier could not locate its tamper marker.");
                    tampered[markerIndex] = (byte)'X';
                    await using var transport = new HttpProfileSyncTransport(
                        endpoint,
                        aliceSession.AccessToken);
                    await transport.PutAsync(
                        settings.PortableProfileId,
                        tampered,
                        aliceRevived.ETag);
                },
                ProfileSyncFailureKind.Protocol);

            clock.Advance(TimeSpan.FromMinutes(11));
            await ExpectFailureAsync(
                async () =>
                {
                    await using var expired = new HttpProfileSyncTransport(
                        endpoint,
                        aliceSession.AccessToken);
                    await expired.ListAsync();
                },
                ProfileSyncFailureKind.Authentication);
        }
        finally
        {
            await StopServerAsync(app);
        }

        app = await StartServerAsync(options, clock);
        endpoint = ResolveEndpoint(app);
        try
        {
            await ExpectFailureAsync(
                async () =>
                {
                    await using var oldSession = new HttpProfileSyncTransport(
                        endpoint,
                        aliceSession.AccessToken);
                    await oldSession.ListAsync();
                },
                ProfileSyncFailureKind.Authentication);

            ProfileSessionResponse restartedSession;
            await using (var sessionClient =
                         new HttpProfileSessionClient(endpoint))
            {
                restartedSession = await sessionClient.SignInAsync(
                    alice.AccountId,
                    alice.AccessKey);
            }

            await using (var transport = new HttpProfileSyncTransport(
                             endpoint,
                             restartedSession.AccessToken))
            {
                var persisted = (await transport.ListAsync()).Single();
                Require(
                    persisted.ETag == aliceRevived.ETag
                    && persisted.ServerRevision == 4,
                    "Profile content or ETag metadata did not survive a server restart.");
            }

            await RoamingCloudVerifier.VerifyPersistenceAsync(
                endpoint,
                restartedSession.AccessToken,
                roamingState);

            var rotated = await accounts.RotateAccessKeyAsync(alice.AccountId);
            await ExpectFailureAsync(
                async () =>
                {
                    await using var revoked = new HttpProfileSyncTransport(
                        endpoint,
                        restartedSession.AccessToken);
                    await revoked.ListAsync();
                },
                ProfileSyncFailureKind.Authentication);
            await ExpectFailureAsync(
                async () =>
                {
                    await using var oldKeyClient =
                        new HttpProfileSessionClient(endpoint);
                    await oldKeyClient.SignInAsync(
                        alice.AccountId,
                        alice.AccessKey);
                },
                ProfileSyncFailureKind.Authentication);

            ProfileSessionResponse rotatedSession;
            await using (var sessionClient =
                         new HttpProfileSessionClient(endpoint))
            {
                rotatedSession = await sessionClient.SignInAsync(
                    alice.AccountId,
                    rotated.AccessKey);
            }

            await accounts.SetDisabledAsync(alice.AccountId, disabled: true);
            await ExpectFailureAsync(
                async () =>
                {
                    await using var disabled = new HttpProfileSyncTransport(
                        endpoint,
                        rotatedSession.AccessToken);
                    await disabled.ListAsync();
                },
                ProfileSyncFailureKind.Authorization);
            await accounts.SetDisabledAsync(alice.AccountId, disabled: false);

            await using var finalSessionClient =
                new HttpProfileSessionClient(endpoint);
            var finalSession = await finalSessionClient.SignInAsync(
                alice.AccountId,
                rotated.AccessKey);
            await finalSessionClient.SignOutAsync(finalSession.AccessToken);
            await ExpectFailureAsync(
                async () =>
                {
                    await using var signedOut = new HttpProfileSyncTransport(
                        endpoint,
                        finalSession.AccessToken);
                    await signedOut.ListAsync();
                },
                ProfileSyncFailureKind.Authentication);

            var registryText = File.ReadAllText(
                Path.Combine(options.DataRoot, "accounts.json"));
            Require(
                !registryText.Contains(rotated.AccessKey, StringComparison.Ordinal)
                && !Directory.EnumerateFiles(
                        options.DataRoot,
                        "*",
                        SearchOption.AllDirectories)
                    .Any(path => FileContainsUtf8(
                        path,
                        rotated.AccessKey)),
                "A rotated account access key leaked into persistent server state.");
        }
        finally
        {
            await StopServerAsync(app);
        }

        await ReleaseDistributionVerifier.VerifyAsync(root);

        Console.WriteLine(
            "PROFILE_SERVER_OK|cloud-schema:3|storage-reserve|private-portal|admin-inventory-pages|password-totp-recovery|account-rename-before-use|device-authorization|refresh-rotation|device-revocation|hashed-keys|strict-sign-in|short-sessions|account-isolation|strict-payloads|conditional-revisions|tombstones|roaming-profiles|conversation-pagination|secret-redaction|package-vault|ssh-envelope-vault|credential-release-reauth|host-key-pinning|restart-persistence|rotation-disable-revocation|signed-releases");
    }

    private static async Task VerifyAdminPortalPagesAsync(
        string endpoint,
        string accountId,
        string password,
        string secondFactor)
    {
        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(endpoint)
        };
        using var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Require(
            loginPage.IsSuccessStatusCode
            && tokenMatch.Success
            && loginHtml.Contains("brand/atlas-emblem-96.png", StringComparison.Ordinal)
            && loginHtml.Contains("brand/favicon-32.png", StringComparison.Ordinal)
            && loginHtml.Contains("favicon.ico", StringComparison.Ordinal)
            && loginHtml.Contains("apple-touch-icon.png", StringComparison.Ordinal),
            "The portal login page did not issue an antiforgery token and branded icon metadata.");

        using var favicon = await client.GetAsync("/favicon.ico");
        using var vectorIcon = await client.GetAsync("/atlas-admin-mark.svg");
        using var emblem = await client.GetAsync("/brand/atlas-emblem-96.png");
        Require(
            favicon.IsSuccessStatusCode
            && favicon.Content.Headers.ContentType?.MediaType is "image/x-icon"
                or "image/vnd.microsoft.icon"
            && (await favicon.Content.ReadAsByteArrayAsync()).Length > 1024
            && vectorIcon.IsSuccessStatusCode
            && vectorIcon.Content.Headers.ContentType?.MediaType == "image/svg+xml"
            && emblem.IsSuccessStatusCode
            && emblem.Content.Headers.ContentType?.MediaType == "image/png",
            "The profile server did not serve its Atlas browser-tab icons.");

        using var loginContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.AccountId"] = accountId,
                ["Input.Password"] = password,
                ["Input.SecondFactor"] = secondFactor,
                ["ReturnUrl"] = "/",
                ["__RequestVerificationToken"] =
                    WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
            });
        using var loginResponse = await client.PostAsync(
            "/Account/Login",
            loginContent);
        Require(
            loginResponse.StatusCode == HttpStatusCode.Redirect
            && loginResponse.Headers.Location?.OriginalString == "/",
            "Valid portal credentials did not establish an authenticated browser session. "
            + $"Received {(int)loginResponse.StatusCode} {loginResponse.StatusCode} with Location '{loginResponse.Headers.Location?.OriginalString ?? "<none>"}'.");

        using var unsafeReturnResponse = await client.GetAsync(
            "/Account/Login?ReturnUrl=https%3A%2F%2Fevil.example%2Fsteal");
        Require(
            unsafeReturnResponse.StatusCode == HttpStatusCode.Redirect
            && unsafeReturnResponse.Headers.Location?.OriginalString == "/",
            "The portal login page accepted an external return URL.");

        var pages = new Dictionary<string, string>
        {
            ["/"] = "Hypershell control room",
            ["/Admin/downloads"] = "Downloads",
            ["/Admin/client-pcs"] = "Client PCs",
            ["/Admin/linked-hosts"] = "Linked Hosts",
            ["/Admin/profiles"] = "Profiles",
            ["/Admin/sessions"] = "Sessions",
            ["/Admin/storage"] = "Storage",
            ["/Admin/backups"] = "Backups"
        };
        foreach (var page in pages)
        {
            using var response = await client.GetAsync(page.Key);
            var html = await response.Content.ReadAsStringAsync();
            Require(
                response.IsSuccessStatusCode
                && html.Contains(page.Value, StringComparison.Ordinal)
                && response.Headers.CacheControl?.NoStore == true,
                $"The authenticated admin page '{page.Key}' did not render privately.");
        }
    }

    private static async Task VerifyDeviceAuthorizationAsync(
        WebApplication app,
        string endpoint,
        ProvisionedProfileAccount account,
        string password,
        string secondFactor,
        string dataRoot)
    {
        var service = app.Services
            .GetRequiredService<DeviceAuthorizationService>();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(endpoint)
        };
        var request = new DeviceAuthorizationRequest
        {
            DeviceName = "Verifier PC",
            Platform = "Windows 11 x64",
            DeviceFingerprint =
                "verifier-device-fingerprint-2026"
        };
        using var startResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeDeviceAuthorizationPath,
            request);
        var started = await startResponse.Content
            .ReadFromJsonAsync<DeviceAuthorizationResponse>()
            ?? throw new InvalidOperationException(
                "The device-authorization endpoint returned no document.");
        Require(
            startResponse.StatusCode
            == System.Net.HttpStatusCode.Created
            && started.DeviceCode.StartsWith("hsd_", StringComparison.Ordinal)
            && started.UserCode.Length == 9
            && started.VerificationUri
                == "https://admin.atlasuniversalis.com/Device",
            "The device-authorization endpoint did not create a bounded portal request.");

        var devicePath =
            $"/Device?userCode={Uri.EscapeDataString(started.UserCode)}";
        using var portalHandler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false
        };
        using var portalClient = new HttpClient(portalHandler)
        {
            BaseAddress = new Uri(endpoint)
        };
        using var unauthenticatedPortal = await portalClient.GetAsync(
            devicePath);
        var loginLocation = unauthenticatedPortal.Headers.Location
            ?.OriginalString;
        Require(
            unauthenticatedPortal.StatusCode == HttpStatusCode.Redirect
            && loginLocation is not null
            && Uri.TryCreate(
                loginLocation,
                UriKind.Absolute,
                out var loginUri)
            && loginUri.AbsolutePath == "/Account/Login"
            && Uri.UnescapeDataString(loginLocation).Contains(
                $"ReturnUrl={devicePath}",
                StringComparison.Ordinal),
            "The desktop sign-in page did not preserve its return address through portal authentication. "
            + $"Received {(int)unauthenticatedPortal.StatusCode} {unauthenticatedPortal.StatusCode} with Location '{loginLocation ?? "<none>"}'.");

        var tokenRequest = new DeviceTokenRequest
        {
            DeviceCode = started.DeviceCode
        };
        using var pendingResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeDeviceTokenPath,
            tokenRequest);
        Require(
            pendingResponse.StatusCode
            == System.Net.HttpStatusCode.Accepted,
            "A new device authorization was not reported as pending.");
        var pending = await service.FindPendingAsync(started.UserCode);
        Require(
            pending is
            {
                DeviceName: "Verifier PC",
                Platform: "Windows 11 x64"
            },
            "The portal could not inspect the pending client identity.");

        using var loginPage = await portalClient.GetAsync(loginLocation);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Require(
            loginPage.IsSuccessStatusCode
            && tokenMatch.Success
            && loginHtml.Contains(
                "name=\"ReturnUrl\"",
                StringComparison.Ordinal),
            "The redirected portal login did not preserve the desktop return target.");
        using var loginContent = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.AccountId"] = account.AccountId,
                ["Input.Password"] = password,
                ["Input.SecondFactor"] = secondFactor,
                ["ReturnUrl"] = devicePath,
                ["__RequestVerificationToken"] =
                    WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
            });
        using var loginResponse = await portalClient.PostAsync(
            "/Account/Login",
            loginContent);
        Require(
            loginResponse.StatusCode == HttpStatusCode.Redirect
            && loginResponse.Headers.Location?.OriginalString == devicePath,
            "Password and authenticator sign-in did not return to the pending desktop request.");
        using var completionResponse = await portalClient.GetAsync(devicePath);
        var completionHtml = await completionResponse.Content
            .ReadAsStringAsync();
        Require(
            completionResponse.IsSuccessStatusCode
            && completionHtml.Contains(
                "Hypershell is signed in",
                StringComparison.Ordinal)
            && await service.FindPendingAsync(started.UserCode) is null,
            "Password and authenticator sign-in did not automatically complete the desktop request.");

        using var issuedResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeDeviceTokenPath,
            tokenRequest);
        var issued = await issuedResponse.Content
            .ReadFromJsonAsync<CloudSessionResponse>()
            ?? throw new InvalidOperationException(
                "The device-token endpoint returned no session.");
        Require(
            issuedResponse.IsSuccessStatusCode
            && issued.ClientDeviceId.Length == 32
            && issued.AccessToken.StartsWith("hps_", StringComparison.Ordinal)
            && issued.RefreshToken.StartsWith("hsr_", StringComparison.Ordinal),
            "The browser-signed-in client did not receive access and refresh credentials.");

        using var consumedResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeDeviceTokenPath,
            tokenRequest);
        Require(
            consumedResponse.StatusCode
            == System.Net.HttpStatusCode.Conflict,
            "A consumed device code could be reused.");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                issued.AccessToken);
        var devices = await client.GetFromJsonAsync<ClientDeviceSummary[]>(
            HypershellCloudContract.RelativeClientDevicesPath);
        Require(
            devices is { Length: 1 }
            && devices[0].Id == issued.ClientDeviceId
            && devices[0].DisplayName == "Verifier PC"
            && devices[0].Platform == "Windows 11 x64",
            "The authenticated client-device inventory was not account scoped.");

        client.DefaultRequestHeaders.Authorization = null;
        using var refreshResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeRefreshTokenPath,
            new RefreshTokenRequest
            {
                RefreshToken = issued.RefreshToken
            });
        var refreshed = await refreshResponse.Content
            .ReadFromJsonAsync<CloudSessionResponse>()
            ?? throw new InvalidOperationException(
                "The refresh endpoint returned no rotated session.");
        Require(
            refreshResponse.IsSuccessStatusCode
            && refreshed.RefreshToken != issued.RefreshToken
            && refreshed.ClientDeviceId == issued.ClientDeviceId,
            "The client refresh credential did not rotate.");
        using var replayResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeRefreshTokenPath,
            new RefreshTokenRequest
            {
                RefreshToken = issued.RefreshToken
            });
        Require(
            replayResponse.StatusCode
            == System.Net.HttpStatusCode.Unauthorized,
            "A rotated refresh credential could be replayed.");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                refreshed.AccessToken);
        using var revokeResponse = await client.DeleteAsync(
            HypershellCloudContract.RelativeClientDevicesPath
            + "/"
            + issued.ClientDeviceId);
        Require(
            revokeResponse.StatusCode
            == System.Net.HttpStatusCode.NoContent,
            "The authorized client device could not be revoked.");
        using var revokedAccessResponse = await client.GetAsync(
            HypershellCloudContract.RelativeStoragePath);
        Require(
            revokedAccessResponse.StatusCode
            == System.Net.HttpStatusCode.Unauthorized,
            "Revoking a device did not invalidate its active access session.");
        client.DefaultRequestHeaders.Authorization = null;
        using var revokedRefreshResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeRefreshTokenPath,
            new RefreshTokenRequest
            {
                RefreshToken = refreshed.RefreshToken
            });
        Require(
            revokedRefreshResponse.StatusCode
            == System.Net.HttpStatusCode.Unauthorized,
            "Revoking a device did not invalidate its refresh credential.");

        var deniedRequest = new DeviceAuthorizationRequest
        {
            DeviceName = "Denied Verifier PC",
            Platform = "Windows 10 x64",
            DeviceFingerprint =
                "denied-verifier-device-fingerprint"
        };
        using var deniedStartResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeDeviceAuthorizationPath,
            deniedRequest);
        var deniedStart = await deniedStartResponse.Content
            .ReadFromJsonAsync<DeviceAuthorizationResponse>()
            ?? throw new InvalidOperationException(
                "The denied-device flow returned no authorization document.");
        Require(
            deniedStartResponse.StatusCode
            == System.Net.HttpStatusCode.Created
            && await service.DenyAsync(deniedStart.UserCode),
            "The portal could not deny a pending client device.");
        using var deniedPollResponse = await client.PostAsJsonAsync(
            HypershellCloudContract.RelativeDeviceTokenPath,
            new DeviceTokenRequest
            {
                DeviceCode = deniedStart.DeviceCode
            });
        Require(
            deniedPollResponse.StatusCode
            == System.Net.HttpStatusCode.Forbidden,
            "A denied device authorization was not rejected.");

        var sensitiveValues = new[]
        {
            started.DeviceCode,
            started.UserCode,
            issued.RefreshToken,
            refreshed.RefreshToken,
            deniedStart.DeviceCode,
            deniedStart.UserCode
        };
        Require(
            !Directory.EnumerateFiles(
                    dataRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Any(path => sensitiveValues.Any(
                    value => FileContainsUtf8(path, value))),
            "Raw device or refresh credentials leaked into persistent server state.");
    }

    private static bool FileContainsUtf8(string path, string value)
    {
        var needle = System.Text.Encoding.UTF8.GetBytes(value);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > int.MaxValue)
        {
            throw new InvalidDataException(
                "The verifier encountered an unexpectedly large server-state file.");
        }

        var payload = new byte[(int)stream.Length];
        stream.ReadExactly(payload);
        return payload.AsSpan().IndexOf(needle) >= 0;
    }

    private static (string CertificatePath, string PasswordPath)
        CreateDataProtectionCertificate(string root)
    {
        const string password = "hypershell-verifier-certificate";
        var certificatePath = Path.Combine(root, "data-protection.pfx");
        var passwordPath = Path.Combine(root, "data-protection.password");
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates
            .CertificateRequest(
                "CN=Hypershell Verifier Data Protection",
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates
                .X509KeyUsageExtension(
                    System.Security.Cryptography.X509Certificates
                        .X509KeyUsageFlags.KeyEncipherment,
                    critical: true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(2));
        File.WriteAllBytes(
            certificatePath,
            certificate.Export(
                System.Security.Cryptography.X509Certificates
                    .X509ContentType.Pfx,
                password));
        File.WriteAllText(passwordPath, password);
        return (certificatePath, passwordPath);
    }

    private static AppSettings CreateSettings(DateTimeOffset now)
    {
        var settings = new AppSettings
        {
            PortableProfileId = "profile-server-verifier",
            PortableProfileDisplayName = "Verifier server profile",
            PortableProfileRevision = 1,
            PortableProfileUpdatedAtUtc = now
        };
        settings.WorkspaceLayout = WorkspaceLayoutService.CreateDefault(
            WorkspacePanelRegistry.CreateWithBuiltIns());
        return settings;
    }

    private static ReadOnlyMemory<byte> CreatePayload(
        PortableProfileService profiles,
        AppSettings settings,
        ExtensionDiscoverySnapshot extensions) =>
        profiles.SerializeEnvelope(
            profiles.CreateEnvelope(
                profiles.CreateSnapshot(settings, extensions)));

    private static async Task<WebApplication> StartServerAsync(
        ProfileServerOptions options,
        TimeProvider clock)
    {
        var app = ProfileServerApplication.Build(
            options,
            [
                "--urls",
                "http://127.0.0.1:0",
                "--Logging:LogLevel:Default",
                "Warning",
                "--Logging:LogLevel:Microsoft.AspNetCore",
                "Warning"
            ],
            clock);
        await app.StartAsync();
        return app;
    }

    private static string ResolveEndpoint(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features
            .Get<IServerAddressesFeature>()
            ?.Addresses
            .Single(value => value.StartsWith(
                "http://127.0.0.1:",
                StringComparison.OrdinalIgnoreCase));
        return address?.TrimEnd('/') + "/"
               ?? throw new InvalidOperationException(
                   "The verifier could not resolve the loopback profile-server address.");
    }

    private static async Task StopServerAsync(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static async Task ExpectFailureAsync(
        Func<Task> action,
        ProfileSyncFailureKind expectedKind)
    {
        try
        {
            await action();
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == expectedKind)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected profile-server client failure '{expectedKind}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int CountStoredProfilePayloads(string dataRoot)
        => GetStoredProfilePayloads(dataRoot).Count;

    private static IReadOnlyList<string> GetStoredProfilePayloads(
        string dataRoot)
    {
        var root = Path.Combine(dataRoot, "profiles");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(
                    root,
                    $"*{ProfileContract.FileExtension}",
                    SearchOption.AllDirectories)
                .ToList()
            : [];
    }

    private static void ConvertCurrentPayloadToLegacyLayout(string dataRoot)
    {
        var payload = GetStoredProfilePayloads(dataRoot).Single();
        var profileDirectory = Path.GetDirectoryName(payload)
                               ?? throw new InvalidOperationException(
                                   "The verifier profile payload has no parent directory.");
        var revisionsDirectory = Path.Combine(profileDirectory, "revisions");
        Directory.CreateDirectory(revisionsDirectory);
        var legacyFileName = $"{1:D20}{ProfileContract.FileExtension}";
        var legacyPath = Path.Combine(revisionsDirectory, legacyFileName);
        File.Move(payload, legacyPath);

        var recordPath = Path.Combine(profileDirectory, "current.json");
        var record = JsonNode.Parse(File.ReadAllText(recordPath))?.AsObject()
                     ?? throw new InvalidDataException(
                         "The verifier profile record is malformed.");
        record["contentFile"] = $"revisions/{legacyFileName}";
        File.WriteAllText(
            recordPath,
            record.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
