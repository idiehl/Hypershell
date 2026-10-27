using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;

if (args.Length < 3) throw new ArgumentException("Provide isolated output, source Hyperlib root, and the published receiver DLL.");
var output = Path.Combine(Path.GetFullPath(args[0]), "receiver-" + Guid.NewGuid().ToString("N"));
var source = Path.GetFullPath(args[1]);
var receiverDll = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var fixture = Path.Combine(output, "library");
// Windows CreateProcess requires a short working directory even when managed
// file APIs support longer paths; edition snapshots add two content hashes.
var durable = OperatingSystem.IsWindows()
    ? Path.Combine(Path.GetPathRoot(output)!, "hl-archives-qa", Guid.NewGuid().ToString("N")[..12])
    : Path.Combine(output, "receiver-data");
var sdk = Path.Combine(Path.GetDirectoryName(receiverDll)!, "sdk");
var keyFile = Path.Combine(output, "publisher.sha256");
var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
await File.WriteAllTextAsync(keyFile, Hash(Encoding.UTF8.GetBytes(token)));
var port = FreePort();
var origin = $"http://127.0.0.1:{port}";
var checkNames = new List<string>();
Process? process = null;
Task? logs = null;
Process? peerProcess = null;
Task? peerLogs = null;
HttpClient? peerHttp = null;
int starts = 0;
using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromMinutes(8) };
var ownedProcesses = new HashSet<int>();

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    checkNames.Add(description);
    Console.WriteLine("PASS " + description);
}
string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
void Copy(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var path in Directory.EnumerateFiles(from)) File.Copy(path, Path.Combine(to, Path.GetFileName(path)));
    foreach (var path in Directory.EnumerateDirectories(from).Where(d => Path.GetFileName(d) is not ("bin" or "obj" or ".git"))) Copy(path, Path.Combine(to, Path.GetFileName(path)));
}
async Task<(Process Process, Task Logs)> StartInstance(int instancePort, HttpClient client)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        WorkingDirectory = Path.GetDirectoryName(receiverDll)!, RedirectStandardError = true, RedirectStandardOutput = true };
    foreach (var argument in new[] { receiverDll, "--root", durable, "--port", instancePort.ToString(), "--public-origin", origin, "--token-hash-file", keyFile, "--sdk-root", sdk }) start.ArgumentList.Add(argument);
    start.Environment["ATLAS_ARCHIVES_FIXTURE_SECRET"] = "fixture-only-must-not-reach-renderer";
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    var running = Process.Start(start) ?? throw new InvalidOperationException("Receiver did not start.");
    ownedProcesses.Add(running.Id);
    var logPath = Path.Combine(output, "receiver-" + ++starts + ".log");
    var pendingLogs = Task.Run(async () =>
    {
        var values = await Task.WhenAll(running.StandardOutput.ReadToEndAsync(), running.StandardError.ReadToEndAsync());
        await File.WriteAllTextAsync(logPath, string.Join(Environment.NewLine, values));
    });
    var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
    while (DateTimeOffset.UtcNow < deadline && !running.HasExited)
    {
        try { using var response = await client.GetAsync("/Archives/healthz"); if (response.IsSuccessStatusCode) return (running, pendingLogs); }
        catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    if (!running.HasExited) running.Kill(entireProcessTree: true);
    await pendingLogs.WaitAsync(TimeSpan.FromSeconds(30));
    running.Dispose();
    throw new InvalidOperationException("Receiver did not become healthy; inspect " + logPath);
}
async Task StartReceiver() { (process, logs) = await StartInstance(port, http); }
void CollectRendererIds()
{
    if (!Directory.Exists(durable)) return;
    foreach (var file in Directory.EnumerateFiles(durable, "active.json", SearchOption.AllDirectories))
    {
        try { using var json = JsonDocument.Parse(File.ReadAllText(file)); if (json.RootElement.TryGetProperty("processId", out var id)) ownedProcesses.Add(id.GetInt32()); }
        catch (Exception error) when (error is IOException or JsonException) { }
    }
}
async Task StopReceiver()
{
    CollectRendererIds();
    if (process is not null)
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        process.Dispose(); process = null;
    }
    if (logs is not null) { await logs.WaitAsync(TimeSpan.FromSeconds(30)); logs = null; }
}
async Task<(HttpStatusCode Status, string Body)> Get(string path, bool authorized = false, HttpClient? client = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    if (authorized) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await (client ?? http).SendAsync(request);
    return (response.StatusCode, await response.Content.ReadAsStringAsync());
}
async Task<(HttpStatusCode Status, string Body)> Publish(TestPackage package, string expected = "", bool authorized = true, string? manifestHash = null, HttpClient? client = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/Archives/api/publish");
    if (authorized) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Add("X-Archives-Manifest", manifestHash ?? package.ManifestHash);
    request.Headers.Add("X-Archives-Expected-Release", expected);
    request.Content = new StreamContent(File.OpenRead(package.Path));
    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    using var response = await (client ?? http).SendAsync(request);
    return (response.StatusCode, await response.Content.ReadAsStringAsync());
}
string Value(string json, string name) { using var document = JsonDocument.Parse(json); return document.RootElement.GetProperty(name).GetString()!; }
async Task<TestPackage> Export(ArchivesPublicationService service, ContentStore content, string node)
{
    var state = await content.ReadAsync();
    var draft = await service.CreateDraftAsync(node, state.Revision);
    var package = await service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash);
    return new(package.PackagePath, package.ManifestSha256, package.Manifest);
}
Dictionary<string, byte[]> ReadZip(string path)
{
    using var zip = ZipFile.OpenRead(path);
    return zip.Entries.ToDictionary(entry => entry.FullName, entry => { using var memory = new MemoryStream(); using var stream = entry.Open(); stream.CopyTo(memory); return memory.ToArray(); }, StringComparer.Ordinal);
}
TestPackage Rewrite(TestPackage original, Action<Dictionary<string, byte[]>> mutate, bool resign = true)
{
    var files = ReadZip(original.Path); mutate(files);
    var manifest = JsonSerializer.Deserialize<ArchivesManifest>(files["manifest.json"], ContentJson.Options)!;
    if (resign)
    {
        manifest.DraftRevision++;
        manifest.ReviewHash = Hash(Guid.NewGuid().ToByteArray());
        manifest.Files = files.Where(pair => pair.Key != "manifest.json").OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ArchivesFileEntry { Path = pair.Key, Length = pair.Value.Length, Sha256 = Hash(pair.Value), Role = "fixture" }).ToList();
        manifest.ContentSha256 = Hash(files["Content/library.json"]);
        files["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, ContentJson.Options);
    }
    var path = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N") + ".zip");
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        foreach (var (name, bytes) in files) { using var entry = zip.CreateEntry(name).Open(); entry.Write(bytes); }
    return new(path, Hash(files["manifest.json"]), manifest);
}

try
{
    Check(File.Exists(receiverDll) && Directory.Exists(sdk), "published receiver and bundled SDK exist");
    Copy(Path.Combine(source, "Starters", "standard"), Path.Combine(fixture, "Starters", "standard"));
    Directory.CreateDirectory(Path.Combine(fixture, "Media"));
    foreach (var file in new[] { "woodland.svg", "forest-detail.svg", "sample-tone.wav", "sample-motion.webm" }) File.Copy(Path.Combine(source, "Media", file), Path.Combine(fixture, "Media", file));
    var state = SeedLibrary.Create();
    state.Nodes.Single(node => node.Id == "natural-world").Cover = "https://cdn.example/media/external-cover.svg";
    foreach (var (id, title, value) in new[] { ("second-public-page", "Second page", "SECOND_PUBLIC_BODY"), ("never-publish", "Unselected sibling", "NEVER_PUBLISH_SENTINEL") })
    {
        state.Nodes.Add(new() { Id = id, ParentId = "animal-atlas", WorkId = "animal-atlas", Type = "page", Title = title, Order = 1 });
        state.Pages[id] = new() { Id = id, WorkId = "animal-atlas", Title = title, Blocks = [new() { Id = id + "-text", Type = "text", Region = "main", Values = new() { ["text"] = ContentJson.Value(value) } }] };
    }
    Directory.CreateDirectory(Path.Combine(fixture, "Content"));
    await File.WriteAllTextAsync(Path.Combine(fixture, "Content", "library.json"), JsonSerializer.Serialize(state, ContentJson.Options));
    var content = new ContentStore(fixture); await content.InitializeAsync();
    await TemplateProjectScaffolder.EnsureAsync(fixture, fixture, "library", "Hyperlib");
    foreach (var work in state.Nodes.Where(node => node.Type == "work")) await TemplateProjectScaffolder.EnsureAsync(fixture, Path.Combine(fixture, "Works", work.Id), work.Id, work.Title);
    var pages = Path.Combine(fixture, "Works", "animal-atlas", "src", "Pages");
    await File.AppendAllTextAsync(Path.Combine(pages, "Blocks", "TextBlock.razor"), """

<p class="archives-fixture" data-readonly="@((!Context.Editing).ToString())" data-child-secret="@(Environment.GetEnvironmentVariable("ATLAS_ARCHIVES_FIXTURE_SECRET") is null ? "scrubbed" : "leaked")">ARCHIVES_CUSTOM_RAZOR</p>
""");
    await File.WriteAllTextAsync(Path.Combine(pages, "Blocks", "TextBlock.razor.css"), ".archives-fixture { color: #123456; --archives-scoped-fixture: 1; }\n.archives-fixture-media { background-image: url('/media/woodland.svg'); }\n");
    await File.AppendAllTextAsync(Path.Combine(pages, "wwwroot", "blocks", "counter.js"), "\nimport './counter-helper.js';\n");
    await File.WriteAllTextAsync(Path.Combine(pages, "wwwroot", "blocks", "counter-helper.js"), "import './media-reference.mjs';\nexport const fixtureDependency = 'ARCHIVES_NESTED_MODULE';\n");
    await File.WriteAllTextAsync(Path.Combine(pages, "wwwroot", "blocks", "media-reference.mjs"), "export const local = '/media/woodland.svg'; export const external = 'https://cdn.example/media/example.png';\n");
    var authoringHash = Hash(await File.ReadAllBytesAsync(Path.Combine(fixture, "Content", "library.json")));
    var exporter = new ArchivesPublicationService(fixture, content);
    var first = await Export(exporter, content, "woodland-habitat");
    var publisher = Guid.Parse(first.Manifest.PublisherId).ToString("N");
    var editionPrefix = "/Archives/_editions/" + publisher + "/" + first.ManifestHash;
    await StartReceiver();
    Check((await Get("/Archives")).Status == HttpStatusCode.OK, "empty public reader shell is available");
    Check((await Get("/Archives/api/status")).Status == HttpStatusCode.Unauthorized, "publisher status requires authentication");
    Check((await Publish(first, authorized: false)).Status == HttpStatusCode.Unauthorized, "publication requires authentication");
    Check((await Get("/Archives/api/status", true)).Status == HttpStatusCode.OK, "valid protected local publisher credential is accepted");
    Check((await Get("/Archives/api/catalog")).Body.Contains("\"libraries\": []", StringComparison.Ordinal), "new receiver exposes an empty public catalog");
    Check((await Publish(first, manifestHash: new string('0', 64))).Status == HttpStatusCode.BadRequest, "wrong reviewed manifest hash is rejected");
    var tampered = Rewrite(first, entries => entries["Media/woodland.svg"] = Encoding.UTF8.GetBytes("tampered"), resign: false);
    Check((await Publish(tampered)).Status == HttpStatusCode.BadRequest, "file integrity mismatch is rejected");
    var traversal = Rewrite(first, entries => entries["../escaped.txt"] = Encoding.UTF8.GetBytes("escape"));
    Check((await Publish(traversal)).Status == HttpStatusCode.BadRequest && !File.Exists(Path.Combine(durable, "escaped.txt")), "ZIP traversal is rejected without an escaped file");

    var pending = Publish(first);
    await Task.Delay(150);
    if (!pending.IsCompleted)
    {
        Check(!(await Get("/Archives/api/catalog")).Body.Contains("woodland-habitat", StringComparison.Ordinal), "building candidate is absent from the public catalog");
        Check((await Get(editionPrefix + "/media/woodland.svg")).Status == HttpStatusCode.NotFound, "building candidate media is not public");
    }
    var published = await pending;
    Check(published.Status == HttpStatusCode.OK, "selected package publishes after actual Razor build: " + published.Body);
    var smokeFiles = Directory.GetFiles(Path.Combine(durable, "editions", publisher, first.ManifestHash, "rendering", ".hyperlib", "runtime"), "active.json", SearchOption.AllDirectories);
    var smokePids = smokeFiles.Select(file => { using var json = JsonDocument.Parse(File.ReadAllText(file)); return json.RootElement.GetProperty("processId").GetInt32(); }).ToArray();
    foreach (var id in smokePids) ownedProcesses.Add(id);
    bool IsStopped(int id) { try { using var candidate = Process.GetProcessById(id); return candidate.HasExited; } catch (ArgumentException) { return true; } }
    var stopDeadline = DateTimeOffset.UtcNow.AddSeconds(3);
    while (!smokePids.All(IsStopped) && DateTimeOffset.UtcNow < stopDeadline) await Task.Delay(50);
    Check(smokePids.Length == first.Manifest.WorkIds.Count + 1 && smokePids.All(IsStopped), "each validation-only Work/library process is released before publication completes");
    var firstRelease = Value(published.Body, "releaseId");
    var catalog = await Get("/Archives/api/catalog");
    Check(catalog.Status == HttpStatusCode.OK && catalog.Body.Contains("woodland-habitat") && catalog.Body.Contains("animal-atlas") && catalog.Body.Contains("natural-world"), "public hierarchy preserves Page, Work, and ancestor collection");
    Check(!catalog.Body.Contains("never-publish") && !catalog.Body.Contains("second-public-page") && !catalog.Body.Contains("science-lab"), "unselected siblings and other branches stay private");
    Check(catalog.Body.Contains("https://cdn.example/media/external-cover.svg"), "absolute external cover URL is preserved exactly");
    Check((await Get("/Archives/" + publisher + "/woodland-habitat")).Status == HttpStatusCode.OK, "public deep link serves the reader shell");
    Check((await Get("/Archives/" + publisher + "/never-publish")).Status == HttpStatusCode.NotFound, "unpublished reader deep link is rejected");
    Check((await Get(editionPrefix + "/api/state")).Status == HttpStatusCode.NotFound, "immutable content gateway requires its internal renderer credential");
    Check((await Get(editionPrefix + "/api/renderer/page/woodland-habitat")).Status == HttpStatusCode.NotFound, "renderer page API is not an anonymous content export");
    var view = await Get("/Archives/api/view/" + publisher + "/woodland-habitat");
    Check(view.Status == HttpStatusCode.OK, "published Page resolves its own Work renderer");
    var pageUrl = Value(view.Body, "url");
    var html = await Get(pageUrl + "?edit=1");
    await File.WriteAllTextAsync(Path.Combine(output, "published-page.html"), html.Body);
    Check(html.Status == HttpStatusCode.OK && html.Body.Contains("ARCHIVES_CUSTOM_RAZOR") && html.Body.Contains("A Woodland Habitat"), "actual custom Razor renders in the public Page");
    Check(html.Body.Contains("data-readonly=\"True\"") && !html.Body.Contains("contenteditable=\"true\""), "edit query cannot enable authoring in the public renderer");
    Check(html.Body.Contains("data-child-secret=\"scrubbed\"") && !html.Body.Contains("leaked"), "receiver environment prefix is scrubbed from child renderers");
    Check(html.Body.Contains("#376954", StringComparison.OrdinalIgnoreCase), "saved Work color remains in the rendered design");
    Check(html.Body.Contains(editionPrefix + "/media/woodland.svg"), "page media references use the immutable edition prefix");
    var media = await Get(editionPrefix + "/media/woodland.svg");
    Check(media.Status == HttpStatusCode.OK && media.Body == await File.ReadAllTextAsync(Path.Combine(fixture, "Media", "woodland.svg")), "original selected media bytes are served");
    var runtimePrefix = pageUrl[..pageUrl.IndexOf("page/woodland-habitat", StringComparison.Ordinal)];
    var module = await Get(runtimePrefix + "_content/HyperLib.Work/blocks/counter-helper.js");
    Check(module.Status == HttpStatusCode.OK && module.Body.Contains("ARCHIVES_NESTED_MODULE"), "nested JavaScript dependency is published through the real renderer");
    var styles = await Get(runtimePrefix + "_content/HyperLib.Work/HyperLib.Work.bundle.scp.css");
    Check(styles.Status == HttpStatusCode.OK && styles.Body.Contains("--archives-scoped-fixture"), "actual Razor scoped CSS is preserved");
    Check(styles.Body.Contains(editionPrefix + "/media/woodland.svg"), "root-relative CSS media references use the edition prefix");
    var mediaModule = await Get(runtimePrefix + "_content/HyperLib.Work/blocks/media-reference.mjs");
    Check(mediaModule.Status == HttpStatusCode.OK && mediaModule.Body.Contains(editionPrefix + "/media/woodland.svg")
        && mediaModule.Body.Contains("https://cdn.example/media/example.png"), "JavaScript module media references preserve external URLs and rewrite local media");
    var runtimeScript = await Get(runtimePrefix + "_framework/blazor.web.js");
    Check(runtimeScript.Status == HttpStatusCode.OK && runtimeScript.Body.Length > 1000, "interactive Blazor client asset is available through the edition proxy");
    if (args.Length >= 5)
    {
        var browserStart = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { Path.GetFullPath(args[3]), origin + "/Archives/" + publisher + "/woodland-habitat", output, Path.GetFullPath(args[4]) }) browserStart.ArgumentList.Add(argument);
        using var browser = Process.Start(browserStart) ?? throw new InvalidOperationException("Browser verifier did not start.");
        try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch { if (!browser.HasExited) browser.Kill(entireProcessTree: true); throw; }
        Check(browser.ExitCode == 0, "actual browser verifies interactive counter, readonly behavior, CSS, media and wide/compact layout");
    }
    var duplicate = await Publish(first);
    Check(duplicate.Status == HttpStatusCode.OK && Value(duplicate.Body, "releaseId") == firstRelease, "exact publication retry is idempotent");

    var second = await Export(exporter, content, "second-public-page");
    Check((await Publish(second, "stale-release")).Status == HttpStatusCode.Conflict, "stale release cannot overwrite a newer publication");
    var badBuild = Rewrite(second, entries => entries["Works/animal-atlas/src/Pages/Blocks/TextBlock.razor"] = Encoding.UTF8.GetBytes("@code { this cannot compile; }"));
    var failed = await Publish(badBuild, firstRelease);
    Check(failed.Status == HttpStatusCode.ServiceUnavailable, "invalid Razor build is rejected before catalog activation");
    var afterFailure = await Get("/Archives/api/status?publisherId=" + publisher, true);
    Check(Value(afterFailure.Body, "releaseId") == firstRelease && (await Get(pageUrl)).Body.Contains("ARCHIVES_CUSTOM_RAZOR"), "failed candidate retains the previous catalog and working page");
    Check((await Get("/Archives/_editions/" + publisher + "/" + badBuild.ManifestHash + "/media/woodland.svg")).Status == HttpStatusCode.NotFound, "failed candidate assets never become public");
    var peerPort = FreePort();
    peerHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{peerPort}"), Timeout = TimeSpan.FromMinutes(8) };
    (peerProcess, peerLogs) = await StartInstance(peerPort, peerHttp);
    Check(Value((await Get("/Archives/api/status?publisherId=" + publisher, true, peerHttp)).Body, "releaseId") == firstRelease, "second receiver opens the same current durable catalog");
    var nextPending = Publish(second, firstRelease);
    await Task.Delay(150);
    var competing = Publish(Rewrite(second, _ => { }), firstRelease, client: peerHttp);
    var next = await nextPending;
    Check(next.Status == HttpStatusCode.OK, "second selected Page can publish after a rejected candidate: " + next.Body);
    var secondRelease = Value(next.Body, "releaseId");
    Check((await competing).Status == HttpStatusCode.Conflict, "cross-process publication lock rejects the stale competing candidate");
    Check(Value((await Get("/Archives/api/status?publisherId=" + publisher, true, peerHttp)).Body, "releaseId") == secondRelease,
        "already-running receiver observes another process's new catalog without restart");
    peerProcess.Kill(entireProcessTree: true); await peerProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
    peerProcess.Dispose(); peerProcess = null; await peerLogs.WaitAsync(TimeSpan.FromSeconds(30)); peerLogs = null;
    peerHttp.Dispose(); peerHttp = null;
    catalog = await Get("/Archives/api/catalog");
    Check(catalog.Body.Contains("woodland-habitat") && catalog.Body.Contains("second-public-page") && !catalog.Body.Contains("never-publish"), "partial upsert retains previously published unchecked Pages");
    Check((await Get(pageUrl)).Body.Contains("A Woodland Habitat"), "previous immutable Page remains accessible after another Page sync");
    Check(authoringHash == Hash(await File.ReadAllBytesAsync(Path.Combine(fixture, "Content", "library.json"))), "publication does not modify authoritative local content");
    await StopReceiver(); await StartReceiver();
    Check(Value((await Get("/Archives/api/status?publisherId=" + publisher, true)).Body, "releaseId") == secondRelease, "public catalog survives receiver restart");
    var restoredView = await Get("/Archives/api/view/" + publisher + "/woodland-habitat");
    Check(restoredView.Status == HttpStatusCode.OK && (await Get(Value(restoredView.Body, "url") + "?edit=1")).Body.Contains("data-readonly=\"True\""), "restart restores the original read-only immutable renderer");
    Check(!(await Get("/Archives/api/catalog")).Body.Contains("NEVER_PUBLISH_SENTINEL"), "unselected content is never exposed by the public catalog");
    await StopReceiver();
    Check(ownedProcesses.All(id => { try { using var candidate = Process.GetProcessById(id); return candidate.HasExited; } catch (ArgumentException) { return true; } }), "all owned receiver and renderer processes stopped");
    var receipt = new { status = "HYPERLIB_ARCHIVES_RECEIVER_OK", checks = checkNames.Count, output, durableRoot = durable, checkNames, receiverProcessIds = ownedProcesses.Order().ToArray() };
    await File.WriteAllTextAsync(Path.Combine(output, "receiver-verification.json"), JsonSerializer.Serialize(receipt, ContentJson.Options));
    Console.WriteLine(JsonSerializer.Serialize(receipt, ContentJson.Options));
}
finally
{
    if (peerProcess is not null) { if (!peerProcess.HasExited) { peerProcess.Kill(entireProcessTree: true); await peerProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); } peerProcess.Dispose(); }
    if (peerLogs is not null) await peerLogs.WaitAsync(TimeSpan.FromSeconds(30));
    peerHttp?.Dispose();
    await StopReceiver();
}

static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
sealed record TestPackage(string Path, string ManifestHash, ArchivesManifest Manifest);
