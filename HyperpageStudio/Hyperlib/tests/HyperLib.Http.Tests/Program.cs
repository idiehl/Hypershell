using System.Net;
using System.Text;
using System.Text.Json;

// This suite intentionally performs no accepted mutations and never starts a renderer.
// It is safe to run against the isolated host while a separate UI verification is active.
var origin = args.Length > 0 ? new Uri(args[0]) : new Uri("http://127.0.0.1:54189");
if (origin.Host != "127.0.0.1" || origin.Scheme != "http") throw new ArgumentException("Pass a local HyperLib loopback origin.");
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15) };
var assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    assertions++;
}
async Task<JsonDocument> ReadJson(string path)
{
    using var response = await client.GetAsync(path);
    Check(response.StatusCode == HttpStatusCode.OK, path + " succeeds");
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
async Task RejectWrite(string? requestOrigin, bool clientHeader, HttpStatusCode expected, string description)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/operations")
    {
        Content = new StringContent("{\"expectedRevision\":-1,\"description\":\"Rejected verifier request\",\"operations\":[]}", Encoding.UTF8, "application/json")
    };
    if (requestOrigin is not null) request.Headers.TryAddWithoutValidation("Origin", requestOrigin);
    if (clientHeader) request.Headers.Add("X-HyperLib-Client", "editor");
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == expected, description + " (got " + (int)response.StatusCode + ")");
    Check(!response.Headers.Contains("Access-Control-Allow-Origin"), description + " has no permissive CORS");
}

using (var healthResponse = await client.GetAsync("/api/health"))
{
    Check(healthResponse.IsSuccessStatusCode, "health endpoint reachable");
    Check(healthResponse.Headers.TryGetValues("X-HyperLib-App", out var identity) && identity.Single() == "hyperlib", "native identity header is stable");
    Check(healthResponse.Headers.TryGetValues("X-Content-Type-Options", out var nosniff) && nosniff.Single() == "nosniff", "MIME sniffing disabled");
    using var health = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
    Check(health.RootElement.GetProperty("app").GetString() == "hyperlib", "health identity JSON matches native contract");
    Check(health.RootElement.GetProperty("origin").GetString() == origin.GetLeftPart(UriPartial.Authority), "health origin matches tested gateway");
}
using var state = await ReadJson("/api/state");
var document = state.RootElement;
Check(document.GetProperty("schemaVersion").GetInt32() == 1 && document.GetProperty("revision").GetInt64() >= 0, "state has version and revision");
var libraryId = document.GetProperty("libraryId").GetString();
Check(!string.IsNullOrWhiteSpace(libraryId) && document.GetProperty("nodes").ValueKind == JsonValueKind.Array, "state exposes virtual root and nodes");
var firstPage = document.GetProperty("pages").EnumerateObject().First();
using (var page = await ReadJson("/api/renderer/page/" + Uri.EscapeDataString(firstPage.Name)))
    Check(page.RootElement.GetProperty("id").GetString() == firstPage.Name && page.RootElement.GetProperty("blocks").ValueKind == JsonValueKind.Array, "renderer page endpoint returns exact page contract");
using (var template = await ReadJson("/api/templates/library"))
    Check(template.RootElement.GetProperty("regions").ValueKind == JsonValueKind.Array && template.RootElement.GetProperty("blocks").GetArrayLength() >= 10, "template endpoint exposes all starter block schemas");
using (var missing = await client.GetAsync("/api/renderer/page/no-such-verifier-page"))
    Check(missing.StatusCode == HttpStatusCode.NotFound, "missing page returns 404");

foreach (var forbidden in new[]
{
    "/src/HyperLib.Host/Program.cs", "/src/HyperLib.Core/ContentStore.cs", "/Content/library.json",
    "/.hyperlib/journal/00000000000000000000.json", "/visual-page.project.json", "/hyperlib.app.json",
    "/Works/animal-atlas/authoring.template.json", "/media/../Content/library.json", "/media/%2e%2e/Content/library.json"
})
{
    using var response = await client.GetAsync(forbidden);
    Check(response.StatusCode == HttpStatusCode.NotFound, "source/private path is not served: " + forbidden);
}
using (var shell = await client.GetAsync("/"))
{
    Check(shell.StatusCode == HttpStatusCode.OK && shell.Content.Headers.ContentType?.MediaType == "text/html", "browser authoring shell is available");
    Check((await shell.Content.ReadAsStringAsync()).Contains("id=\"edit-toggle\"", StringComparison.Ordinal), "shell includes Edit control");
}
using (var media = await client.GetAsync("/media/sample-tone.wav"))
    Check(media.StatusCode == HttpStatusCode.OK && media.Content.Headers.ContentType?.MediaType is "audio/wav" or "audio/x-wav" or "audio/wave", "seed audio is served with media MIME type");
using (var video = await client.GetAsync("/media/sample-motion.webm"))
    Check(video.StatusCode == HttpStatusCode.OK && video.Content.Headers.ContentType?.MediaType == "video/webm", "seed video is served with media MIME type");

foreach (var forgedHost in new[] { "attacker.invalid:" + origin.Port, "localhost:" + origin.Port, "127.0.0.1:" + (origin.Port + 1) })
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
    request.Headers.Host = forgedHost;
    using var response = await client.SendAsync(request);
    Check(response.StatusCode == HttpStatusCode.BadRequest, "forged Host rejected: " + forgedHost);
}
await RejectWrite("https://attacker.invalid", true, HttpStatusCode.Forbidden, "foreign-origin POST rejected even with editor header");
await RejectWrite("null", true, HttpStatusCode.Forbidden, "opaque-origin POST rejected");
await RejectWrite(origin.GetLeftPart(UriPartial.Authority), false, HttpStatusCode.Forbidden, "same-origin POST without editor header rejected");
await RejectWrite(null, false, HttpStatusCode.Forbidden, "simple cross-site form-style POST rejected");
await RejectWrite(origin.GetLeftPart(UriPartial.Authority), true, HttpStatusCode.Conflict, "authorized stale mutation is rejected as conflict");
await RejectWrite(null, true, HttpStatusCode.Conflict, "native header path reaches revision validation without mutating");
using (var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/operations"))
{
    preflight.Headers.Add("Origin", "https://attacker.invalid");
    preflight.Headers.Add("Access-Control-Request-Method", "POST");
    preflight.Headers.Add("Access-Control-Request-Headers", "x-hyperlib-client,content-type");
    using var response = await client.SendAsync(preflight);
    Check(response.StatusCode == HttpStatusCode.Forbidden && !response.Headers.Contains("Access-Control-Allow-Origin"), "cross-origin preflight rejected");
}
Console.WriteLine($"HYPERLIB_HTTP_OK|assertions:{assertions}|read-only|host-validation|csrf|private-source-paths|media|json-contract");
