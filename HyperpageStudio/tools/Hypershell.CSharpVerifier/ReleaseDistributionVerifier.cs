using Hypershell.ProfileServer;
using Hypershell.Profiles;
using Hypershell.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

internal static class ReleaseDistributionVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "release-distribution");
        Directory.CreateDirectory(root);
        var now = DateTimeOffset.UtcNow;
        using var certificate = CreateCodeSigningCertificate(now);
        var signerSha256 = Convert.ToHexString(
                SHA256.HashData(certificate.RawData))
            .ToLowerInvariant();
        var options = new ProfileServerOptions
        {
            DataRoot = Path.Combine(root, "server-data"),
            SessionLifetime = TimeSpan.FromMinutes(10),
            MaximumAccountStorageBytes = 32L * 1024 * 1024,
            MinimumFreeStorageBytes = 64L * 1024 * 1024,
            CredentialHashIterations = 100_000,
            RequireHttpsPortalCookies = false,
            PublicPortalBaseUri = new Uri("http://127.0.0.1"),
            TrustedReleaseSignerSha256 = [signerSha256],
            StableReleaseRetentionCount = 3
        }.Normalize();

        using var metadata = new CloudMetadataStore(options);
        await metadata.InitializeAsync();
        var releases = new ReleaseStore(
            metadata,
            new StorageCapacityService(options),
            options);
        var bundles = new Dictionary<string, ReleaseBundle>();
        for (var patch = 0; patch <= 4; patch++)
        {
            var version = $"1.0.{patch}";
            var bundle = CreateBundle(
                root,
                certificate,
                version,
                now.AddMinutes(patch),
                notes: $"Verifier release {version}");
            bundles[version] = bundle;
            var import = await releases.ImportBundleAsync(
                bundle.Directory,
                pinRelease: patch == 0);
            Require(
                import.Disposition == ReleaseImportDisposition.Created,
                $"Release {version} was not imported exactly once.");
        }

        var duplicate = await releases.ImportBundleAsync(
            bundles["1.0.4"].Directory,
            pinRelease: false);
        Require(
            duplicate.Disposition
            == ReleaseImportDisposition.AlreadyPresent,
            "An identical release import was not idempotent.");
        await ExpectFailureAsync(
            () => releases.ImportBundleAsync(
                bundles["1.0.4"].Directory,
                pinRelease: false,
                expectedGitCommit: new string('f', 40)),
            "expected server repository commit");
        var commitMatched = await releases.ImportBundleAsync(
            bundles["1.0.4"].Directory,
            pinRelease: false,
            expectedGitCommit: bundles["1.0.4"].GitCommit);
        Require(
            commitMatched.Disposition
            == ReleaseImportDisposition.AlreadyPresent,
            "A release matching the expected Git commit was not accepted.");
        var inventory = await releases.ListAsync(
            HypershellReleaseContract.StableChannel);
        Require(
            inventory.Select(release => release.Version)
                .SequenceEqual(
                    new[] { "1.0.4", "1.0.3", "1.0.2", "1.0.0" })
            && inventory.Single(release => release.Version == "1.0.0")
                .IsPinned,
            "Stable retention did not preserve the latest three plus the pinned release.");

        var immutableReplacement = CreateBundle(
            root,
            certificate,
            "1.0.4",
            now.AddMinutes(5),
            "Replacement content must fail");
        var blobCountBeforeImmutableRejection = Directory
            .EnumerateFiles(
                metadata.BlobRoot,
                "*",
                SearchOption.AllDirectories)
            .Count();
        await ExpectFailureAsync(
            () => releases.ImportBundleAsync(
                immutableReplacement.Directory,
                pinRelease: false),
            "immutable");
        Require(
            Directory.EnumerateFiles(
                    metadata.BlobRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Count() == blobCountBeforeImmutableRejection,
            "A rejected immutable release left unreferenced blobs behind.");

        var tampered = CreateBundle(
            root,
            certificate,
            "1.0.5",
            now.AddMinutes(6),
            "Tamper check");
        await File.AppendAllTextAsync(
            tampered.InstallerPath,
            "tampered");
        await ExpectFailureAsync(
            () => releases.ImportBundleAsync(
                tampered.Directory,
                pinRelease: false),
            "wrong size");

        using var untrustedCertificate = CreateCodeSigningCertificate(now);
        var untrusted = CreateBundle(
            root,
            untrustedCertificate,
            "1.0.6",
            now.AddMinutes(7),
            "Untrusted signer");
        await ExpectFailureAsync(
            () => releases.ImportBundleAsync(
                untrusted.Directory,
                pinRelease: false),
            "not trusted");

        using var wrongUsageCertificate = CreateNonCodeSigningCertificate(
            now);
        await ExpectFailureAsync(
            () =>
            {
                _ = CreateBundle(
                    root,
                    wrongUsageCertificate,
                    "1.0.7",
                    now.AddMinutes(8),
                    "Wrong certificate usage");
                return Task.CompletedTask;
            },
            "code-signing");

        var latest = await releases.GetLatestAsync(
            HypershellReleaseContract.StableChannel);
        Require(
            latest?.Summary.Version == "1.0.4"
            && latest.Artifacts.All(artifact =>
                File.Exists(artifact.FullPath)),
            "The latest retained release could not be materialized from content-addressed storage.");

        var accounts = new ProfileAccountStore(options);
        var account = await accounts.CreateAsync(
            "release.test",
            "Release verifier");
        var app = ProfileServerApplication.Build(
            options,
            [
                "--urls",
                "http://127.0.0.1:0",
                "--Logging:LogLevel:Default",
                "Warning",
                "--Logging:LogLevel:Microsoft.AspNetCore",
                "Warning"
            ]);
        await app.StartAsync();
        try
        {
            var endpoint = ResolveEndpoint(app);
            await VerifyAuthenticatedDownloadAsync(
                endpoint,
                account,
                bundles["1.0.4"]);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        await VerifyHttpReleaseClientAsync(
            root,
            bundles["1.0.4"],
            signerSha256);
        await VerifyApplicationUpdateServiceAsync(
            root,
            bundles["1.0.4"],
            signerSha256);
        VerifyUnsignedAuthenticodeRejected(
            bundles["1.0.4"].InstallerPath,
            signerSha256);

        Console.WriteLine(
            "RELEASE_DISTRIBUTION_OK|signed-manifest|signer-pin|expected-git-commit|code-signing-usage|content-addressed|immutable|tamper-rejected|retention:3-plus-pinned|authenticated-download|strict-http|atomic-download|desktop-update|authenticode-rejected");
    }

    private static async Task VerifyHttpReleaseClientAsync(
        string root,
        ReleaseBundle bundle,
        string signerSha256)
    {
        var security = new HypershellReleaseManifestSecurity();
        var signed = LoadSignedManifest(bundle);
        var envelopeBytes = security.SerializeEnvelope(signed);
        var verified = security.Verify(
            signed,
            [signerSha256],
            requireTrustedCertificateChain: false);
        using var signingCertificate = verified.SigningCertificate;
        var installer = verified.Manifest.Artifacts.Single(
            artifact => artifact.Kind
                        == HypershellReleaseArtifactKind.Installer);
        var installerBytes = await File.ReadAllBytesAsync(
            bundle.InstallerPath);
        var token = "hps_" + new string('a', 48);

        var latestRequests = new List<CapturedHttpRequest>();
        await using (var client = new HttpHypershellReleaseClient(
                         "https://updates.example.test/",
                         new CallbackHttpHandler(request =>
                         {
                             latestRequests.Add(new CapturedHttpRequest(
                                 request.RequestUri,
                                 request.Headers.Authorization?.Scheme));
                             return JsonResponse(envelopeBytes);
                         })))
        {
            var received = await client.GetLatestAsync("stable", token);
            Require(
                security.SerializeEnvelope(received)
                    .SequenceEqual(envelopeBytes)
                && latestRequests.Count == 1
                && latestRequests[0].RequestUri?.AbsolutePath.EndsWith(
                    "/releases/latest",
                    StringComparison.Ordinal) == true
                && latestRequests[0].AuthorizationScheme == "Bearer",
                "The desktop release client did not issue a strict authenticated latest-release request.");
        }

        await ExpectProfileSyncFailureAsync(
            async () =>
            {
                await using var client = new HttpHypershellReleaseClient(
                    "https://updates.example.test/",
                    new CallbackHttpHandler(_ => new HttpResponseMessage(
                        HttpStatusCode.Found)
                    {
                        Headers =
                        {
                            Location = new Uri(
                                "https://redirect.example.test/")
                        }
                    }));
                _ = await client.GetLatestAsync("stable", token);
            },
            "redirect");

        await ExpectProfileSyncFailureAsync(
            async () =>
            {
                await using var client = new HttpHypershellReleaseClient(
                    "https://updates.example.test/",
                    new CallbackHttpHandler(_ => new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "not json",
                            Encoding.UTF8,
                            "text/plain")
                    }));
                _ = await client.GetLatestAsync("stable", token);
            },
            "non-JSON");

        await ExpectProfileSyncFailureAsync(
            async () =>
            {
                await using var client = new HttpHypershellReleaseClient(
                    "https://updates.example.test/",
                    new CallbackHttpHandler(_ =>
                    {
                        var response = JsonResponse([1]);
                        response.Content.Headers.ContentLength =
                            HypershellReleaseContract
                                .MaximumSignedManifestBytes
                            + 1;
                        return response;
                    }));
                _ = await client.GetLatestAsync("stable", token);
            },
            "protocol limit");

        var downloadRoot = Path.Combine(root, "desktop-http-download");
        var destination = Path.Combine(
            downloadRoot,
            installer.FileName);
        await using (var client = new HttpHypershellReleaseClient(
                         "https://updates.example.test/",
                         new CallbackHttpHandler(_ =>
                             BinaryResponse(installerBytes))))
        {
            await client.DownloadArtifactAsync(
                verified.Manifest.Version,
                installer,
                token,
                destination,
                new InlineProgress<double>());
        }

        Require(
            File.ReadAllBytes(destination).SequenceEqual(installerBytes),
            "The desktop release client did not atomically materialize the verified installer bytes.");

        File.Delete(destination);
        var corruptedBytes = installerBytes.ToArray();
        corruptedBytes[^1] ^= 0x5a;
        await ExpectProfileSyncFailureAsync(
            async () =>
            {
                await using var client = new HttpHypershellReleaseClient(
                    "https://updates.example.test/",
                    new CallbackHttpHandler(_ =>
                        BinaryResponse(corruptedBytes)));
                await client.DownloadArtifactAsync(
                    verified.Manifest.Version,
                    installer,
                    token,
                    destination);
            },
            "SHA-256");
        Require(
            !File.Exists(destination)
            && !Directory.EnumerateFiles(
                    downloadRoot,
                    "*.download-*",
                    SearchOption.TopDirectoryOnly)
                .Any(),
            "A rejected update left a destination or partial download behind.");
    }

    private static async Task VerifyApplicationUpdateServiceAsync(
        string root,
        ReleaseBundle bundle,
        string signerSha256)
    {
        var emptyTrustPath = Path.Combine(
            root,
            "empty-trusted-signers.json");
        await File.WriteAllTextAsync(
            emptyTrustPath,
            "{\"schemaVersion\":1,\"signerCertificateSha256\":[]}");
        var unconfiguredFactoryCalls = 0;
        var unconfigured = new ApplicationUpdateService(
            Path.Combine(root, "unconfigured-updates"),
            new HypershellReleaseTrustStore(emptyTrustPath),
            new RecordingAuthenticodeVerifier(),
            _ =>
            {
                unconfiguredFactoryCalls++;
                throw new InvalidOperationException(
                    "The network must not be used without a trust pin.");
            },
            currentVersion: new Version(1, 0, 3),
            currentWindowsVersion: new Version(10, 0, 19045),
            requireTrustedCertificateChain: false);
        var unavailable = await unconfigured.CheckAsync(
            "https://updates.example.test/",
            "hps_" + new string('b', 48));
        Require(
            unavailable.Kind
            == ApplicationUpdateCheckKind.TrustNotConfigured
            && unconfiguredFactoryCalls == 0,
            "The updater contacted the network before a release signer was trusted.");

        var trustPath = Path.Combine(root, "trusted-signers.json");
        await File.WriteAllTextAsync(
            trustPath,
            "{\"schemaVersion\":1,\"signerCertificateSha256\":[\""
            + signerSha256
            + "\"]}");
        var signed = LoadSignedManifest(bundle);
        var installerBytes = await File.ReadAllBytesAsync(
            bundle.InstallerPath);
        var endpoint = new FakeReleaseEndpoint(
            signed,
            installerBytes);
        var authenticode = new RecordingAuthenticodeVerifier();
        var updateRoot = Path.Combine(root, "desktop-update-service");
        var service = new ApplicationUpdateService(
            updateRoot,
            new HypershellReleaseTrustStore(trustPath),
            authenticode,
            _ => endpoint.CreateClient(),
            currentVersion: new Version(1, 0, 3),
            currentWindowsVersion: new Version(10, 0, 19045),
            requireTrustedCertificateChain: false);
        var available = await service.CheckAsync(
            "https://updates.example.test/",
            "hps_" + new string('c', 48));
        Require(
            available.Kind == ApplicationUpdateCheckKind.Available
            && available.Candidate?.Manifest.Version == "1.0.4",
            "The desktop updater did not recognize a trusted newer stable release.");

        var candidate = available.Candidate
                        ?? throw new InvalidOperationException(
                            "The update candidate was missing.");
        var stagedPath = Path.Combine(
            updateRoot,
            candidate.Manifest.Version,
            candidate.Installer.FileName);
        Directory.CreateDirectory(
            Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllTextAsync(stagedPath, "stale tampered file");
        var downloaded = await service.DownloadAsync(
            candidate,
            "https://updates.example.test/",
            "hps_" + new string('c', 48));
        var reused = await service.DownloadAsync(
            candidate,
            "https://updates.example.test/",
            "hps_" + new string('c', 48));
        Require(
            !downloaded.UsedExistingDownload
            && reused.UsedExistingDownload
            && endpoint.DownloadCount == 1
            && authenticode.VerificationCount == 2
            && authenticode.LastExpectedSigner == signerSha256
            && File.ReadAllBytes(stagedPath).SequenceEqual(installerBytes)
            && File.Exists(Path.Combine(
                Path.GetDirectoryName(stagedPath)!,
                ReleaseStore.SignedManifestFileName)),
            "The desktop updater did not replace a bad staged installer and safely reuse the verified replacement.");
    }

    private static void VerifyUnsignedAuthenticodeRejected(
        string path,
        string signerSha256)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = new WindowsAuthenticodeVerifier().Verify(
                path,
                signerSha256);
        }
        catch (InvalidDataException exception)
        {
            Require(
                exception.Message.Contains(
                    "signature",
                    StringComparison.OrdinalIgnoreCase),
                "The unsigned installer was rejected for an unexpected reason.");
            return;
        }

        throw new InvalidOperationException(
            "Windows accepted an unsigned synthetic installer as an Authenticode-signed release.");
    }

    private static SignedHypershellReleaseManifest LoadSignedManifest(
        ReleaseBundle bundle) =>
        new HypershellReleaseManifestSecurity().DeserializeEnvelope(
            File.ReadAllBytes(Path.Combine(
                bundle.Directory,
                ReleaseStore.SignedManifestFileName)));

    private static HttpResponseMessage JsonResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        return response;
    }

    private static HttpResponseMessage BinaryResponse(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    private static async Task ExpectProfileSyncFailureAsync(
        Func<Task> action,
        string expectedMessage)
    {
        try
        {
            await action();
        }
        catch (ProfileSyncException exception)
        {
            Require(
                exception.Message.Contains(
                    expectedMessage,
                    StringComparison.OrdinalIgnoreCase),
                $"The desktop release client failed for the wrong reason: {exception.Message}");
            return;
        }

        throw new InvalidOperationException(
            "The desktop release client accepted a malformed response.");
    }

    private static async Task VerifyAuthenticatedDownloadAsync(
        string endpoint,
        ProvisionedProfileAccount account,
        ReleaseBundle latestBundle)
    {
        using var anonymous = new HttpClient();
        var unauthorized = await anonymous.GetAsync(
            endpoint
            + HypershellCloudContract.RelativeReleasesPath
            + "/latest?channel=stable");
        Require(
            unauthorized.StatusCode == HttpStatusCode.Unauthorized,
            "Release discovery was available without account authentication.");
        using var portalClient = new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            });
        var privatePortal = await portalClient.GetAsync(
            endpoint + "Admin/downloads");
        Require(
            privatePortal.StatusCode == HttpStatusCode.Redirect
            && privatePortal.Headers.Location?.AbsolutePath
            == "/Account/Login",
            "The release download portal was available without an authenticated administrator session.");

        ProfileSessionResponse session;
        await using (var sessionClient =
                     new HttpProfileSessionClient(endpoint))
        {
            session = await sessionClient.SignInAsync(
                account.AccountId,
                account.AccessKey);
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);
        var signedManifest = await client.GetFromJsonAsync<
            SignedHypershellReleaseManifest>(
            endpoint
            + HypershellCloudContract.RelativeReleasesPath
            + "/latest?channel=stable")
            ?? throw new InvalidOperationException(
                "The authenticated latest-release response was empty.");
        var security = new HypershellReleaseManifestSecurity();
        var manifestBytes = security.SerializeEnvelope(signedManifest);
        Require(
            manifestBytes.Length > 0,
            "The authenticated latest-release response was malformed.");

        var artifactResponse = await client.GetAsync(
            endpoint
            + HypershellCloudContract.RelativeReleasesPath
            + "/1.0.4/artifacts/installer");
        var downloadedBytes = await artifactResponse.Content
            .ReadAsByteArrayAsync();
        var expectedBytes = await File.ReadAllBytesAsync(
            latestBundle.InstallerPath);
        Require(
            artifactResponse.StatusCode == HttpStatusCode.OK
            && artifactResponse.Content.Headers.ContentDisposition?.FileNameStar
            == Path.GetFileName(latestBundle.InstallerPath)
            && downloadedBytes.SequenceEqual(expectedBytes),
            "The authenticated installer download did not return the signed artifact bytes.");
    }

    private static ReleaseBundle CreateBundle(
        string root,
        X509Certificate2 certificate,
        string version,
        DateTimeOffset publishedAtUtc,
        string notes)
    {
        var directory = Path.Combine(
            root,
            "bundles",
            version,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var installerName = $"Hypershell-{version}-win-x64.msi";
        var portableName = $"Hypershell-{version}-win-x64-portable.zip";
        var installerPath = Path.Combine(directory, installerName);
        var portablePath = Path.Combine(directory, portableName);
        File.WriteAllBytes(
            installerPath,
            System.Text.Encoding.UTF8.GetBytes(
                $"synthetic signed installer {version}"));
        File.WriteAllBytes(
            portablePath,
            System.Text.Encoding.UTF8.GetBytes(
                $"synthetic portable recovery {version}"));
        var manifest = new HypershellReleaseManifest
        {
            Version = version,
            Channel = HypershellReleaseContract.StableChannel,
            GitCommit = new string('a', 39) + version[^1],
            PublishedAtUtc = publishedAtUtc,
            MinimumWindowsVersion = "10.0.17763",
            ReleaseNotes = notes,
            Artifacts =
            [
                CreateArtifact(
                    HypershellReleaseArtifactKind.Installer,
                    installerPath,
                    requiresAuthenticode: true),
                CreateArtifact(
                    HypershellReleaseArtifactKind.Portable,
                    portablePath,
                    requiresAuthenticode: false)
            ]
        };
        var security = new HypershellReleaseManifestSecurity();
        var signed = security.Sign(
            manifest,
            certificate,
            publishedAtUtc);
        File.WriteAllBytes(
            Path.Combine(
                directory,
                ReleaseStore.SignedManifestFileName),
            security.SerializeEnvelope(signed));
        return new ReleaseBundle(
            directory,
            installerPath,
            portablePath,
            manifest.GitCommit);
    }

    private static HypershellReleaseArtifact CreateArtifact(
        HypershellReleaseArtifactKind kind,
        string path,
        bool requiresAuthenticode)
    {
        var bytes = File.ReadAllBytes(path);
        return new HypershellReleaseArtifact
        {
            Kind = kind,
            FileName = Path.GetFileName(path),
            ByteLength = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                .ToLowerInvariant(),
            RequiresAuthenticode = requiresAuthenticode
        };
    }

    private static X509Certificate2 CreateCodeSigningCertificate(
        DateTimeOffset now)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Hypershell Release Verifier",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        var usages = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.3", "Code Signing")
        };
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(usages, critical: true));
        return request.CreateSelfSigned(
            now.AddDays(-1),
            now.AddDays(30));
    }

    private static X509Certificate2 CreateNonCodeSigningCertificate(
        DateTimeOffset now)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Wrong Usage Release Verifier",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        var usages = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1", "Server Authentication")
        };
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(usages, critical: true));
        return request.CreateSelfSigned(
            now.AddDays(-1),
            now.AddDays(30));
    }

    private static async Task ExpectFailureAsync(
        Func<Task> action,
        string expectedMessage)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidDataException
                                          or InvalidOperationException
                                          or UnauthorizedAccessException)
        {
            Require(
                exception.Message.Contains(
                    expectedMessage,
                    StringComparison.OrdinalIgnoreCase),
                $"Release validation failed for the wrong reason: {exception.Message}");
            return;
        }

        throw new InvalidOperationException(
            "A malformed release bundle was accepted.");
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
                   "The release verifier could not resolve the loopback server address.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ReleaseBundle(
        string Directory,
        string InstallerPath,
        string PortablePath,
        string GitCommit);

    private sealed record CapturedHttpRequest(
        Uri? RequestUri,
        string? AuthorizationScheme);

    private sealed class CallbackHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        public void Report(T value)
        {
        }
    }

    private sealed class RecordingAuthenticodeVerifier
        : IAuthenticodeVerifier
    {
        public int VerificationCount { get; private set; }

        public string LastExpectedSigner { get; private set; } =
            string.Empty;

        public AuthenticodeVerificationResult Verify(
            string path,
            string expectedSignerCertificateSha256)
        {
            Require(
                File.Exists(path),
                "Authenticode verification received a missing installer.");
            VerificationCount++;
            LastExpectedSigner = expectedSignerCertificateSha256;
            return new AuthenticodeVerificationResult(
                expectedSignerCertificateSha256,
                "CN=Hypershell Release Verifier");
        }
    }

    private sealed class FakeReleaseEndpoint
    {
        private readonly SignedHypershellReleaseManifest _signedManifest;
        private readonly byte[] _installerBytes;

        public FakeReleaseEndpoint(
            SignedHypershellReleaseManifest signedManifest,
            byte[] installerBytes)
        {
            _signedManifest = signedManifest;
            _installerBytes = installerBytes;
        }

        public int DownloadCount { get; private set; }

        public IHypershellReleaseClient CreateClient() =>
            new Client(this);

        private sealed class Client(FakeReleaseEndpoint endpoint)
            : IHypershellReleaseClient
        {
            private bool _isDisposed;

            public Task<SignedHypershellReleaseManifest> GetLatestAsync(
                string channel,
                string accessToken,
                CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(endpoint._signedManifest);
            }

            public async Task DownloadArtifactAsync(
                string version,
                HypershellReleaseArtifact artifact,
                string accessToken,
                string destinationPath,
                IProgress<double>? progress = null,
                CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                endpoint.DownloadCount++;
                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllBytesAsync(
                    destinationPath,
                    endpoint._installerBytes,
                    cancellationToken);
                progress?.Report(1);
            }

            public ValueTask DisposeAsync()
            {
                _isDisposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
