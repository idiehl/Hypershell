using Hypershell.Profiles;
using Hypershell.ProfileServer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "Hypershell", "distribution-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var now = DateTimeOffset.UtcNow;
using var certificate = Certificate();
using var otherCertificate = Certificate();
var security = new HypershellReleaseManifestSecurity();
var options = new ProfileServerOptions
{
    DataRoot = Path.Combine(root, "data"),
    TrustedReleaseSignerSha256 = [Fingerprint(certificate), Fingerprint(otherCertificate)],
    RequireHttpsPortalCookies = false,
    PublicPortalBaseUri = new Uri("http://127.0.0.1"),
    CredentialHashIterations = 100_000
};
using var metadata = new CloudMetadataStore(options);
await metadata.InitializeAsync();
var store = new ReleaseStore(metadata, new StorageCapacityService(options), options);
Require((await store.ListPublicAsync()).Count == 0, "Empty public catalog");
var legacy = Bundle("1.0.0", complete: false);
await store.ImportBundleAsync(legacy, pinRelease: true);
Require(await store.GetPublicAsync("1.0.0") is null, "Legacy private release must stay private");

// Recreate the previous metadata schema in the isolated fixture, then exercise
// the actual upgrade with an existing private release already stored.
await using (var connection = metadata.OpenConnection())
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "DROP TABLE release_companions; DELETE FROM schema_migrations WHERE version = 4;";
    await command.ExecuteNonQueryAsync();
}
using (var migrating = new CloudMetadataStore(options))
{
    Require((await migrating.InitializeAsync()).SchemaVersion == 4, "Schema 3 to 4 migration");
}
Require((await store.GetAsync("1.0.0")) is not null, "Migration preserved private release");

var first = Bundle("1.0.1");
var unsignedCompleteEnvelope = security.DeserializeEnvelope(File.ReadAllBytes(Path.Combine(first, ReleaseStore.CompleteManifestFileName)));
var signedComplete = security.Verify(unsignedCompleteEnvelope, [Fingerprint(certificate)], false);
using (signedComplete.SigningCertificate)
{
    await Rejected(() =>
    {
        security.Sign(signedComplete.Manifest with
        {
            Artifacts = [signedComplete.Manifest.Artifacts.Single() with { RequiresAuthenticode = false }]
        }, certificate);
        return Task.CompletedTask;
    }, "Authenticode");
}
await store.ImportBundleAsync(first, pinRelease: false);
var published = await store.GetPublicAsync("1.0.1") ?? throw new Exception("Public suite missing");
Require((await store.GetLatestAsync("stable"))!.Artifacts.All(a => !a.Artifact.FileName.EndsWith(".exe")), "Legacy feed stays Core-only");
Require(published.Complete.Summary.Channel == "stable-complete", "Complete channel");
Require((await store.ImportBundleAsync(first, false)).Disposition == ReleaseImportDisposition.AlreadyPresent, "Idempotent pair import");

var mismatched = Bundle("1.0.2", childVersion: "1.0.9");
await Rejected(() => store.ImportBundleAsync(mismatched, false), "must match");
Require(await store.GetAsync("1.0.2") is null, "Rejected pair never created a private parent");
await Rejected(() => store.ImportBundleAsync(Bundle("1.0.2", childSigner: otherCertificate), false), "must match");
await Rejected(() => store.ImportBundleAsync(Bundle("1.0.2", childTime: now.AddSeconds(1)), false), "must match");
await Rejected(() => store.ImportBundleAsync(Bundle("1.0.2", childExtension: ".msi"), false), "must match");
var tampered = Bundle("1.0.2");
File.AppendAllText(Directory.GetFiles(tampered, "*.exe").Single(), "tampered");
await Rejected(() => store.ImportBundleAsync(tampered, false), "wrong size");
await Rejected(() => store.ImportBundleAsync(Bundle("1.0.0"), true), "immutable");
await Rejected(() => store.ImportBundleAsync(Bundle("1.0.1", complete: false), false), "immutable");

var app = ProfileServerApplication.Build(options,
    ["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"]);
await app.StartAsync();
try
{
    var endpoint = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(endpoint) };
    Require((await client.GetAsync("/Admin/downloads")).StatusCode == HttpStatusCode.Redirect, "Admin downloads remain protected");
    Require((await client.GetAsync("/" + HypershellCloudContract.RelativeReleasesPath + "/latest")).StatusCode == HttpStatusCode.Unauthorized, "Private API remains protected");
    Require((await client.GetAsync("/downloads/1.0.0/core/installer")).StatusCode == HttpStatusCode.NotFound, "Public route cannot expose private installer");
    Require((await client.GetAsync("/downloads/1.0.0/core/manifest")).StatusCode == HttpStatusCode.NotFound, "Public route cannot expose private manifest");
    Require((await client.GetAsync("/downloads/1.0.1/complete/portable")).StatusCode == HttpStatusCode.NotFound, "No invented Complete portable asset");
    Require((await client.GetAsync("/downloads/1.0.1/core/unknown")).StatusCode == HttpStatusCode.NotFound, "Unknown artifact rejected");
    foreach (var edition in new[] { "core", "complete" })
    {
        var expected = edition == "core" ? published.Core : published.Complete;
        var manifestBytes = await client.GetByteArrayAsync($"/downloads/1.0.1/{edition}/manifest");
        var envelope = security.DeserializeEnvelope(manifestBytes);
        var verified = security.Verify(envelope, [Fingerprint(certificate)], false);
        using (verified.SigningCertificate)
            Require(verified.Manifest.Channel == expected.Summary.Channel, "Public manifest verifies with correct edition");
        var downloaded = await client.GetByteArrayAsync($"/downloads/1.0.1/{edition}/installer");
        var expectedBytes = await File.ReadAllBytesAsync(expected.Artifacts[0].FullPath);
        Require(downloaded.SequenceEqual(expectedBytes), "Exact public installer bytes");
    }
    var page = await client.GetStringAsync("/downloads");
    Require(page.Contains("Hypershell Complete") && page.Contains("Hypershell Core") && page.Contains("/downloads/1.0.1/complete/installer"), "Public page displays both editions");
    Require(!page.Contains("1.0.0"), "Public page hides private inventory");
    var catalog = await client.GetStringAsync("/downloads/releases");
    Require(catalog.Contains("1.0.1") && !catalog.Contains("1.0.0") && !catalog.Contains(root, StringComparison.OrdinalIgnoreCase), "Public catalog exposes only public metadata");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

var completeBlob = published.Complete.Artifacts.Single().FullPath;
for (var patch = 2; patch <= 4; patch++) await store.ImportBundleAsync(Bundle($"1.0.{patch}"), false);
Require(await store.GetPublicAsync("1.0.1") is null, "Pruning removes companion visibility");
Require(!File.Exists(completeBlob), "Pruning removes unreferenced companion blob");
Require((await store.ListPublicAsync()).Select(r => r.Core.Summary.Version).SequenceEqual(new[] { "1.0.4", "1.0.3", "1.0.2" }), "Public retention tracks parent retention");
Require(await store.GetAsync("1.0.0") is not null, "Pinned private release retained");
await SuiteUpdateGuardVerifier.VerifyAsync(root);
Console.WriteLine("DISTRIBUTION_SERVER_OK|migration|private-releases-hidden|legacy-core-feed|paired-import|immutable|mismatch-rejected|tamper-rejected|public-page|public-manifests|exact-downloads|companion-retention");

string Bundle(string version, bool complete = true, string? childVersion = null,
    X509Certificate2? childSigner = null, DateTimeOffset? childTime = null, string childExtension = ".exe")
{
    var directory = Path.Combine(root, "bundles", version, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var corePath = Path.Combine(directory, $"Hypershell-Core-{version}-win-x64.msi");
    File.WriteAllText(corePath, "synthetic core installer " + version);
    var core = new HypershellReleaseManifest
    {
        Version = version, GitCommit = new string('a', 40), PublishedAtUtc = now.AddMinutes(int.Parse(version.Split('.')[2])),
        ReleaseNotes = "Distribution verification", Artifacts = [Artifact(corePath)]
    };
    File.WriteAllBytes(Path.Combine(directory, ReleaseStore.SignedManifestFileName), security.SerializeEnvelope(security.Sign(core, certificate, core.PublishedAtUtc)));
    if (complete)
    {
        var completePath = Path.Combine(directory, $"Hypershell-Complete-{version}-win-x64" + childExtension);
        File.WriteAllText(completePath, "synthetic complete installer " + version);
        var companion = core with
        {
            Version = childVersion ?? version, Channel = HypershellReleaseContract.CompleteStableChannel,
            PublishedAtUtc = childTime ?? core.PublishedAtUtc, Artifacts = [Artifact(completePath)]
        };
        File.WriteAllBytes(Path.Combine(directory, ReleaseStore.CompleteManifestFileName), security.SerializeEnvelope(security.Sign(companion, childSigner ?? certificate, companion.PublishedAtUtc)));
    }
    return directory;
}

static HypershellReleaseArtifact Artifact(string path) => new()
{
    Kind = HypershellReleaseArtifactKind.Installer, FileName = Path.GetFileName(path),
    ByteLength = new FileInfo(path).Length,
    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), RequiresAuthenticode = true
};
static string Fingerprint(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
static X509Certificate2 Certificate()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=Hypershell Distribution Verifier", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, true));
    return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static async Task Rejected(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidDataException error) when (error.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new InvalidOperationException("Expected rejection: " + message);
}
