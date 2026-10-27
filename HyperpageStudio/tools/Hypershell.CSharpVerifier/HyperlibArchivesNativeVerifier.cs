using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Services;

internal static class HyperlibArchivesNativeVerifier
{
    public static async Task VerifyAsync(string output, string source)
    {
        var root = Path.Combine(Path.GetFullPath(output), "native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = Environment.GetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable);
        Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, Path.Combine(root, "settings"));
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        async Task Reject(Func<Task> action, string expected)
        {
            try { await action(); throw new Exception("Unexpected acceptance: " + expected); }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ContentValidationException)
            { Check(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), "Wrong rejection: " + error.Message); }
        }
        try
        {
            var library = Path.Combine(root, "library"); Directory.CreateDirectory(library);
            Copy(Path.Combine(source, "Starters", "standard"), Path.Combine(library, "Starters", "standard"));
            Directory.CreateDirectory(Path.Combine(library, "Media"));
            foreach (var name in new[] { "woodland.svg", "forest-detail.svg", "sample-tone.wav", "sample-motion.webm" })
                File.Copy(Path.Combine(source, "Media", name), Path.Combine(library, "Media", name));
            var store = new ContentStore(library); await store.InitializeAsync();
            var state = await store.ReadAsync();
            await TemplateProjectScaffolder.EnsureAsync(library, library, "library", "Library");
            foreach (var work in state.Nodes.Where(n => n.Type == "work"))
                await TemplateProjectScaffolder.EnsureAsync(library, Path.Combine(library, "Works", work.Id), work.Id, work.Title);
            var publications = new ArchivesPublicationService(library, store);
            var draft = await publications.CreateDraftAsync("natural-world", state.Revision);
            var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var endpoint = "https://atlasuniversalis.com";
            var unconfigured = new HyperlibArchivesPublicationService(library, () => true);
            Check(!JsonSerializer.SerializeToElement(unconfigured.Status()).GetProperty("canSync").GetBoolean(), "An unconnected library enabled sync.");
            await Reject(() => Task.Run(() => HyperlibArchivesPublicationService.Configure(library, "https://example.org", secret)), "scoped Atlas");
            HyperlibArchivesPublicationService.Configure(library, endpoint, secret);
            Check(JsonSerializer.SerializeToElement(unconfigured.Status()).GetProperty("canSync").GetBoolean(), "Protected connection did not enable sync.");
            var protectedPath = HyperlibArchivesPublicationService.ConfigurationPath(library);
            Check(protectedPath.StartsWith(Path.Combine(root,"settings")), "The QA credential escaped its isolated settings directory.");
            Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(protectedPath)).Contains(secret), "A credential was saved in plaintext.");
            Check(!Directory.EnumerateFiles(library, "*.dpapi", SearchOption.AllDirectories).Any(), "Credential entered the portable library.");
            var sends = 0; var posts = 0; var manifests = new List<string>();
            Func<HttpMessageHandler> transport = () => new Handler(async request =>
            {
                sends++;
                Check(request.RequestUri!.GetLeftPart(UriPartial.Authority) == endpoint, "Transport changed publication host.");
                Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == secret, "Protected transport credential missing.");
                if (request.Method == HttpMethod.Get) return Json(new { app = "hyperlib-archives", schemaVersion = 1, releaseId = posts == 0 ? null : "fixture-release" });
                posts++;
                Check(request.RequestUri.AbsolutePath == "/Archives/api/publish", "Unexpected publication endpoint.");
                var bytes = await request.Content!.ReadAsByteArrayAsync();
                using var zip = new ZipArchive(new MemoryStream(bytes));
                using var manifestStream = zip.GetEntry("manifest.json")!.Open(); using var memory = new MemoryStream(); await manifestStream.CopyToAsync(memory);
                var hash = Convert.ToHexStringLower(SHA256.HashData(memory.ToArray()));
                manifests.Add(hash);
                Check(hash == request.Headers.GetValues("X-Archives-Manifest").Single(), "Outbound package differs from reviewed manifest.");
                Check(!Encoding.UTF8.GetString(bytes).Contains(secret), "Credential entered publication body.");
                var manifest = JsonSerializer.Deserialize<ArchivesManifest>(memory.ToArray(), ContentJson.Options)!;
                return Json(new { publisherId = Guid.Parse(manifest.PublisherId).ToString("N"), manifestSha256 = hash,
                    url = endpoint + "/Archives/" + Guid.Parse(manifest.PublisherId).ToString("N") + "/" + manifest.RootNodeId,
                    releaseId = "fixture-release", syncedAtUtc = DateTimeOffset.UtcNow });
            });
            var service = new HyperlibArchivesPublicationService(library, () => true) { TransportFactory = transport };
            JsonElement Parameters(ArchivesDraft value) => JsonSerializer.SerializeToElement(new { draftId = value.Id,
                expectedDraftRevision = value.DraftRevision, expectedContentRevision = value.ContentRevision, reviewHash = value.ReviewHash });
            var result = JsonSerializer.SerializeToElement(await service.SyncAsync(Parameters(draft), CancellationToken.None));
            Check(result.GetProperty("receipt").GetProperty("releaseId").GetString() == "fixture-release", "Receipt was lost.");
            Check((await publications.GetDraftAsync(draft.Id)).ReviewHash == draft.ReviewHash, "Sync changed the reviewed local draft.");
            await service.SyncAsync(Parameters(draft), CancellationToken.None);
            Check(posts == 2 && manifests.Distinct().Count() == 1, "Retry did not send an identical immutable publication.");
            var current = false;
            var detached = new HyperlibArchivesPublicationService(library, () => current) { TransportFactory = transport };
            var before = sends; await Reject(() => detached.SyncAsync(Parameters(draft), CancellationToken.None), "no longer attached");
            Check(sends == before, "Detached native session made a remote request.");
            draft = await publications.UpdateSelectionAsync(draft.Id, draft.DraftRevision, []);
            before = posts; await Reject(() => service.SyncAsync(Parameters(draft), CancellationToken.None), "at least one");
            Check(posts == before, "An empty selection reached the receiver.");
            var unauthorized = new HyperlibArchivesPublicationService(library, () => true) { TransportFactory = () => new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))) };
            await Reject(() => unauthorized.SyncAsync(Parameters(draft), CancellationToken.None), "not authorized");
            var incompatible = new HyperlibArchivesPublicationService(library, () => true) { TransportFactory = () => new Handler(_ => Task.FromResult(Json(new { app = "another-app", schemaVersion = 1 }))) };
            await Reject(() => incompatible.SyncAsync(Parameters(draft), CancellationToken.None), "publishing contract");
            Check((await store.ReadAsync()).Revision == state.Revision, "Publishing changed local library content.");
            await File.WriteAllTextAsync(Path.Combine(output, "native-publication-verification.json"), JsonSerializer.Serialize(new { passed = true, checks, root }, ContentJson.Options));
            Console.WriteLine($"HYPERLIB_ARCHIVES_NATIVE_OK checks={checks}");
        }
        finally { Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, settings); }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var child in Directory.EnumerateDirectories(source).Where(d => Path.GetFileName(d) is not ("bin" or "obj" or ".git")))
            Copy(child, Path.Combine(destination, Path.GetFileName(child)));
    }
}
