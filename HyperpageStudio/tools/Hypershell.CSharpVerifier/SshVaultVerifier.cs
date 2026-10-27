using Hypershell.ProfileServer;
using Hypershell.Profiles;
using Hypershell.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class SshVaultVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    public static async Task VerifyAsync(
        WebApplication app,
        string endpoint,
        ProvisionedProfileAccount aliceAccount,
        string bobAccessToken,
        AccountSecurityStore accountSecurity,
        string password,
        string totpSecret,
        TimeProvider clock,
        string dataRoot)
    {
        VerifyExternalMasterKeyBoundary(dataRoot);
        var aliceSession = await AuthorizeClientAsync(
            app,
            aliceAccount,
            "SSH Vault Verifier PC");
        using var alice = CreateClient(endpoint, aliceSession.AccessToken);
        using var bob = CreateClient(endpoint, bobAccessToken);
        var localVaultPath = Path.Combine(
            dataRoot,
            "verifier-client-ssh-vault.device.bin");
        var localVault = new DpapiSshCredentialStore(localVaultPath);

        var malformed = new SshCredentialMutationRequest
        {
            DisplayName = "Malformed verifier key",
            PrivateKeyUtf8 = "not an SSH private key"u8.ToArray()
        };
        using (var malformedResponse = await alice.PostAsJsonAsync(
                   HypershellCloudContract.RelativeSshCredentialsPath,
                   malformed))
        {
            Require(
                malformedResponse.StatusCode == HttpStatusCode.BadRequest,
                "The SSH vault accepted malformed private-key material.");
        }

        CryptographicOperations.ZeroMemory(malformed.PrivateKeyUtf8);

        const string passphrase = "Verifier SSH passphrase 2026!";
        var firstKey = CreatePrivateKey(passphrase);
        SshCredentialSummary credential;
        try
        {
            var createRequest = new SshCredentialMutationRequest
            {
                DisplayName = "Atlas root verifier",
                PrivateKeyUtf8 = firstKey,
                PassphraseUtf8 = Encoding.UTF8.GetBytes(passphrase)
            };
            using var response = await alice.PostAsJsonAsync(
                HypershellCloudContract.RelativeSshCredentialsPath,
                createRequest);
            var receipt = await response.Content.ReadFromJsonAsync<
                              SshCredentialMutationReceipt>()
                          ?? throw new InvalidOperationException(
                              "The SSH vault returned no enrollment receipt.");
            credential = receipt.Credential;
            Require(
                response.StatusCode == HttpStatusCode.Created
                && credential.KeyVersion == 1
                && credential.PublicKeyFingerprint.StartsWith(
                    "SHA256:",
                    StringComparison.Ordinal)
                && credential.Id.Length == 32,
                "The SSH vault did not enroll and fingerprint the private key.");
            CryptographicOperations.ZeroMemory(
                createRequest.PassphraseUtf8);
        }
        finally
        {
            // Retain firstKey until the one-time release is byte-compared.
        }

        var inventory = await alice.GetFromJsonAsync<SshCredentialList>(
            HypershellCloudContract.RelativeSshCredentialsPath);
        var bobInventory = await bob.GetFromJsonAsync<SshCredentialList>(
            HypershellCloudContract.RelativeSshCredentialsPath);
        Require(
            inventory?.Credentials.Single() == credential
            && bobInventory?.Credentials.Count == 0,
            "SSH credential metadata was not account-scoped.");

        var hostRequest = new RemoteHostMutationRequest
        {
            DisplayName = "Atlas verifier host",
            HostName = "167.71.179.90",
            Port = 22,
            UserName = "root",
            OperatingSystem = RemoteHostOperatingSystem.Linux,
            DefaultRoot = "/srv/hypershell",
            CredentialId = credential.Id,
            Notes = "Verifier-only linked host"
        };
        RemoteHostSummary host;
        using (var response = await alice.PostAsJsonAsync(
                   HypershellCloudContract.RelativeRemoteHostsPath,
                   hostRequest,
                   JsonOptions))
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            Require(
                response.StatusCode == HttpStatusCode.Created,
                $"The linked-host create request failed: {(int)response.StatusCode} {responseBody}");
            var receipt = await response.Content.ReadFromJsonAsync<
                              RemoteHostMutationReceipt>(JsonOptions)
                          ?? throw new InvalidOperationException(
                              "The linked-host endpoint returned no receipt.");
            host = receipt.Host;
            Require(
                host is
                {
                    Revision: 1,
                    HasCredential: true,
                    OperatingSystem: RemoteHostOperatingSystem.Linux
                }
                && host.DefaultRoot == "/srv/hypershell",
                $"The linked-host registry did not create the expected Linux host: status={(int)response.StatusCode}, revision={host.Revision}, credential={host.HasCredential}, os={host.OperatingSystem}, root='{host.DefaultRoot}'.");
        }

        using (var crossAccount = await bob.PostAsJsonAsync(
                   HypershellCloudContract.RelativeRemoteHostsPath,
                   hostRequest,
                   JsonOptions))
        {
            Require(
                crossAccount.StatusCode == HttpStatusCode.BadRequest,
                "Another account could bind a remote host to Alice's credential.");
        }

        await using (var strictClient = new HttpLinkedHostClient(
                         endpoint,
                         aliceSession.AccessToken))
        {
            var strictHosts = await strictClient.ListHostsAsync();
            var strictCredentials = await strictClient.ListCredentialsAsync();
            Require(
                strictHosts.Single().Id == host.Id
                && strictCredentials.Single().Id == credential.Id,
                "The strict desktop linked-host client did not validate the v2 inventories.");
        }

        var firstHostFingerprint = CreateFingerprint("host-key-one");
        var secondHostFingerprint = CreateFingerprint("host-key-two");
        using (var pin = await alice.PutAsJsonAsync(
                   HypershellCloudContract.RelativeRemoteHostsPath
                   + "/"
                   + host.Id
                   + "/host-key",
                   new RemoteHostKeyPinRequest
                   {
                       Algorithm = "ssh-ed25519",
                       Fingerprint = firstHostFingerprint
                   }))
        {
            Require(
                pin.IsSuccessStatusCode,
                "The first observed SSH host key was not pinned.");
        }

        using (var mismatch = await alice.PutAsJsonAsync(
                   HypershellCloudContract.RelativeRemoteHostsPath
                   + "/"
                   + host.Id
                   + "/host-key",
                   new RemoteHostKeyPinRequest
                   {
                       Algorithm = "ssh-ed25519",
                       Fingerprint = secondHostFingerprint
                   }))
        {
            Require(
                mismatch.StatusCode == HttpStatusCode.Conflict,
                "An unexpected SSH host-key change was silently accepted.");
        }

        using (var deliberateReplacement = await alice.PutAsJsonAsync(
                   HypershellCloudContract.RelativeRemoteHostsPath
                   + "/"
                   + host.Id
                   + "/host-key",
                   new RemoteHostKeyPinRequest
                   {
                       Algorithm = "ssh-ed25519",
                       Fingerprint = secondHostFingerprint,
                       ExpectedCurrentFingerprint = firstHostFingerprint
                   }))
        {
            Require(
                deliberateReplacement.IsSuccessStatusCode,
                "An explicit compare-before-replace host-key rotation failed.");
        }

        SshCredentialReleaseAuthorization release;
        using (var response = await alice.PostAsync(
                   HypershellCloudContract.RelativeSshCredentialsPath
                   + "/"
                   + credential.Id
                   + "/release",
                   content: null))
        {
            release = await response.Content.ReadFromJsonAsync<
                          SshCredentialReleaseAuthorization>()
                      ?? throw new InvalidOperationException(
                          "The SSH vault returned no release authorization.");
            Require(
                response.StatusCode == HttpStatusCode.Created
                && release.ReleaseToken.StartsWith(
                    "hsk_",
                    StringComparison.Ordinal)
                && release.VerificationUri.EndsWith(
                    "/CredentialRelease",
                    StringComparison.Ordinal),
                "The SSH release request was not bound to portal approval.");
        }

        using (var pending = await alice.PostAsJsonAsync(
                   HypershellCloudContract.RelativeCredentialReleasePath
                   + "/token",
                   new SshCredentialReleasePollRequest
                   {
                       ReleaseToken = release.ReleaseToken
                   }))
        {
            Require(
                pending.StatusCode == HttpStatusCode.Accepted,
                "An unapproved SSH release was not reported as pending.");
        }

        var releases = app.Services
            .GetRequiredService<CredentialReleaseService>();
        var pendingPortal = await releases.FindPendingAsync(
            aliceAccount.AccountId,
            release.UserCode);
        var invalidReauthentication = await accountSecurity.VerifyAsync(
            aliceAccount.AccountId,
            "incorrect SSH release password",
            AccountSecurityStore.GenerateTotpForTesting(
                totpSecret,
                clock.GetUtcNow()));
        Require(
            pendingPortal is
            {
                CredentialLabel: "Atlas root verifier",
                ClientDeviceName: "SSH Vault Verifier PC"
            }
            && !invalidReauthentication.Succeeded,
            "The portal could not identify the exact release target or rejected reauthentication was accepted.");
        var validReauthentication = await accountSecurity.VerifyAsync(
            aliceAccount.AccountId,
            password,
            AccountSecurityStore.GenerateTotpForTesting(
                totpSecret,
                clock.GetUtcNow()));
        Require(
            validReauthentication.Succeeded
            && await releases.ApproveAsync(
                aliceAccount.AccountId,
                release.UserCode),
            "Fresh password and TOTP verification did not approve the release.");

        using (var approved = await alice.PostAsJsonAsync(
                   HypershellCloudContract.RelativeCredentialReleasePath
                   + "/token",
                   new SshCredentialReleasePollRequest
                   {
                       ReleaseToken = release.ReleaseToken
                   }))
        {
            var released = await approved.Content.ReadFromJsonAsync<
                               SshCredentialReleaseResponse>()
                           ?? throw new InvalidOperationException(
                               "The approved release returned no credential.");
            try
            {
                Require(
                    approved.IsSuccessStatusCode
                    && released.CredentialId == credential.Id
                    && released.KeyVersion == 1
                    && released.PrivateKeyUtf8.AsSpan().SequenceEqual(firstKey)
                    && Encoding.UTF8.GetString(released.PassphraseUtf8)
                       == passphrase,
                    "The one-time release did not return the exact enrolled key to its approved PC.");
                localVault.Save(
                    endpoint,
                    aliceAccount.AccountId,
                    released);
                using var local = localVault.Load(
                    endpoint,
                    aliceAccount.AccountId,
                    credential.Id);
                Require(
                    local is not null
                    && local.KeyVersion == 1
                    && local.PrivateKeyUtf8.AsSpan().SequenceEqual(firstKey)
                    && Encoding.UTF8.GetString(local.PassphraseUtf8)
                       == passphrase
                    && File.Exists(localVaultPath)
                    && !FileContainsBytes(localVaultPath, firstKey)
                    && !FileContainsBytes(
                        localVaultPath,
                        Encoding.UTF8.GetBytes(passphrase)),
                    "The released SSH credential was not protected and restored through Windows DPAPI.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(released.PrivateKeyUtf8);
                CryptographicOperations.ZeroMemory(released.PassphraseUtf8);
            }
        }

        using (var replay = await alice.PostAsJsonAsync(
                   HypershellCloudContract.RelativeCredentialReleasePath
                   + "/token",
                   new SshCredentialReleasePollRequest
                   {
                       ReleaseToken = release.ReleaseToken
                   }))
        {
            Require(
                replay.StatusCode == HttpStatusCode.Conflict,
                "A one-time SSH credential release could be replayed.");
        }

        SshCredentialReleaseAuthorization staleRelease;
        using (var response = await alice.PostAsync(
                   HypershellCloudContract.RelativeSshCredentialsPath
                   + "/"
                   + credential.Id
                   + "/release",
                   content: null))
        {
            staleRelease = await response.Content.ReadFromJsonAsync<
                               SshCredentialReleaseAuthorization>()
                           ?? throw new InvalidOperationException(
                               "The stale-release verifier returned no authorization.");
        }

        var secondKey = CreatePrivateKey(passphrase: null);
        try
        {
            var rotateRequest = new SshCredentialMutationRequest
            {
                DisplayName = "Atlas root verifier rotated",
                PrivateKeyUtf8 = secondKey,
                ExpectedKeyVersion = 1
            };
            using var rotated = await alice.PutAsJsonAsync(
                HypershellCloudContract.RelativeSshCredentialsPath
                + "/"
                + credential.Id,
                rotateRequest);
            var rotatedReceipt = await rotated.Content.ReadFromJsonAsync<
                                     SshCredentialMutationReceipt>()
                                 ?? throw new InvalidOperationException(
                                     "The SSH rotation returned no receipt.");
            Require(
                rotated.IsSuccessStatusCode
                && rotatedReceipt.Credential.KeyVersion == 2,
                "The SSH credential did not rotate with a conditional key version.");

            using var staleRotation = await alice.PutAsJsonAsync(
                HypershellCloudContract.RelativeSshCredentialsPath
                + "/"
                + credential.Id,
                rotateRequest);
            Require(
                staleRotation.StatusCode == HttpStatusCode.Conflict,
                "A stale SSH credential rotation was accepted.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secondKey);
        }

        using (var changedRelease = await alice.PostAsJsonAsync(
                   HypershellCloudContract.RelativeCredentialReleasePath
                   + "/token",
                   new SshCredentialReleasePollRequest
                   {
                       ReleaseToken = staleRelease.ReleaseToken
                   }))
        {
            Require(
                changedRelease.StatusCode == HttpStatusCode.Forbidden
                || changedRelease.StatusCode == HttpStatusCode.Conflict,
                "Credential rotation did not invalidate an outstanding release.");
        }

        Require(
            !Directory.EnumerateFiles(
                    dataRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Any(path => FileContainsBytes(path, firstKey)
                             || FileContainsBytes(
                                 path,
                                 Encoding.UTF8.GetBytes(passphrase))
                             || FileContainsBytes(
                                 path,
                                 Encoding.UTF8.GetBytes(release.ReleaseToken))),
            "Raw SSH key, passphrase, or release token leaked into persistent server state.");

        using (var revoke = await alice.DeleteAsync(
                   HypershellCloudContract.RelativeSshCredentialsPath
                   + "/"
                   + credential.Id))
        {
            Require(
                revoke.StatusCode == HttpStatusCode.NoContent,
                "The SSH credential could not be revoked.");
        }

        var hostsAfterRevoke = await alice.GetFromJsonAsync<RemoteHostList>(
            HypershellCloudContract.RelativeRemoteHostsPath,
            JsonOptions);
        Require(
            hostsAfterRevoke?.Hosts.Single() is
            {
                HasCredential: false,
                HostKeyFingerprint: not null
            },
            "Credential revocation was not reflected by linked-host inventory.");
        localVault.Delete(
            endpoint,
            aliceAccount.AccountId,
            credential.Id);
        Require(
            localVault.Load(
                endpoint,
                aliceAccount.AccountId,
                credential.Id) is null,
            "The local DPAPI SSH credential could not be removed.");
        CryptographicOperations.ZeroMemory(firstKey);
    }

    private static void VerifyExternalMasterKeyBoundary(string dataRoot)
    {
        var externalKeyPath = Path.Combine(
            dataRoot,
            "externally-provisioned-ssh-vault-key.bin");
        var store = new SshVaultMasterKeyStore(new ProfileServerOptions
        {
            DataRoot = dataRoot,
            SshVaultMasterKeyPath = externalKeyPath,
            ManageSshVaultMasterKeyFile = false
        });
        try
        {
            _ = store.ReadOrCreate();
            throw new InvalidOperationException(
                "An absent external SSH vault key was silently auto-created.");
        }
        catch (FileNotFoundException)
        {
            Require(
                !File.Exists(externalKeyPath),
                "The external SSH vault boundary created a missing secret file.");
        }

        var provisionedKey = RandomNumberGenerator.GetBytes(
            SshVaultMasterKeyStore.MasterKeyBytes);
        try
        {
            File.WriteAllBytes(externalKeyPath, provisionedKey);
            var loaded = store.ReadOrCreate();
            try
            {
                Require(
                    loaded.AsSpan().SequenceEqual(provisionedKey),
                    "The externally provisioned SSH vault key was not read exactly.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(loaded);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(provisionedKey);
        }
    }

    private static async Task<CloudSessionResponse> AuthorizeClientAsync(
        WebApplication app,
        ProvisionedProfileAccount account,
        string name)
    {
        var service = app.Services
            .GetRequiredService<DeviceAuthorizationService>();
        var started = await service.StartAsync(
            new DeviceAuthorizationRequest
            {
                DeviceName = name,
                Platform = "Windows 11 x64",
                DeviceFingerprint =
                    "ssh-vault-verifier-device-fingerprint-2026"
            },
            new Uri("https://admin.atlasuniversalis.com"));
        _ = await service.ApproveAsync(account.AccountId, started.UserCode)
            ?? throw new InvalidOperationException(
                "The SSH vault verifier PC could not be approved.");
        var polled = await service.PollAsync(started.DeviceCode);
        return polled is
        {
            Kind: DeviceAuthorizationPollKind.Approved,
            Session: not null
        }
            ? polled.Session
            : throw new InvalidOperationException(
                "The SSH vault verifier PC did not receive a session.");
    }

    private static HttpClient CreateClient(string endpoint, string token)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(endpoint)
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static byte[] CreatePrivateKey(string? passphrase)
    {
        using var rsa = RSA.Create(2048);
        var pem = passphrase is null
            ? rsa.ExportPkcs8PrivateKeyPem()
            : rsa.ExportEncryptedPkcs8PrivateKeyPem(
                passphrase,
                new PbeParameters(
                    PbeEncryptionAlgorithm.Aes256Cbc,
                    HashAlgorithmName.SHA256,
                    100_000));
        return Encoding.UTF8.GetBytes(pem);
    }

    private static string CreateFingerprint(string marker)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(marker));
        try
        {
            return "SHA256:"
                   + Convert.ToBase64String(digest).TrimEnd('=');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static bool FileContainsBytes(string path, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64L * 1024 * 1024)
        {
            return false;
        }

        var payload = new byte[(int)stream.Length];
        stream.ReadExactly(payload);
        try
        {
            return payload.AsSpan().IndexOf(value) >= 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
