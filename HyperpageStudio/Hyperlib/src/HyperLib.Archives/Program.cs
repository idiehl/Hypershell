using System.Net;
using System.Security.Cryptography;
using System.Text;
using HyperLib.Archives;
using HyperLib.Core;
using Microsoft.AspNetCore.StaticFiles;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);
var root = Path.GetFullPath(builder.Configuration["root"] ?? throw new ArgumentException("Pass --root for the Archives data directory."));
var port = int.TryParse(builder.Configuration["port"], out var requested) ? requested : 5290;
if (port is < 1024 or > 65535) throw new ArgumentException("Invalid Archives port.");
var origin = $"http://127.0.0.1:{port}";
var publicOrigin = builder.Configuration["public-origin"] ?? "https://atlasuniversalis.com";
if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var publicUri) || (publicUri.Scheme != "https" && !publicUri.IsLoopback)
    || publicUri.AbsolutePath != "/" || publicUri.Query.Length > 0 || publicUri.Fragment.Length > 0 || publicUri.UserInfo.Length > 0)
    throw new ArgumentException("The public Archives origin must use HTTPS.");
var hashPath = builder.Configuration["token-hash-file"] ?? throw new ArgumentException("Pass --token-hash-file; raw credentials do not belong in the service environment.");
var tokenHash = Convert.FromHexString(ArchivesStore.ValidateDigest((await File.ReadAllTextAsync(hashPath)).Trim().ToLowerInvariant()));
var sdk = Path.GetFullPath(builder.Configuration["sdk-root"] ?? Path.Combine(AppContext.BaseDirectory, "sdk"));
builder.WebHost.UseUrls(origin);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ArchivesStore.MaxPackageBytes);
builder.Services.AddHttpForwarder();
await using var store = new ArchivesStore(root, sdk, origin, publicOrigin);
var app = builder.Build();
using var proxy = new HttpMessageInvoker(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false,
    AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(10) });
var types = new FileExtensionContentTypeProvider();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (ArchivesConflictException error) { await Error(context, 409, error.Message); }
    catch (Exception error) when (error is ContentValidationException or InvalidDataException or System.Text.Json.JsonException or FormatException)
    { await Error(context, 400, error.Message); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
    {
        app.Logger.LogError(error, "Archives operation failed");
        await Error(context, 503, "Archives could not complete this operation. The previous publication is still available.");
    }
});
app.UseWebSockets();
app.MapGet("/Archives/healthz", () => Results.Json(new { status = "healthy", app = "hyperlib-archives", schemaVersion = 1 }));
app.MapGet("/Archives/api/status", (HttpContext context, string? publisherId) => Authorized(context)
    ? Results.Json(new { app = "hyperlib-archives", schemaVersion = 1, releaseId = publisherId is null ? null : store.ReleaseFor(publisherId), publicOrigin })
    : Results.Unauthorized());
app.MapPost("/Archives/api/publish", async (HttpContext context) =>
{
    if (!Authorized(context)) return Results.Unauthorized();
    if (context.Request.ContentType != "application/zip") return Results.BadRequest(new { error = "Use a reviewed Hyperlib publication package." });
    var receipt = await store.PublishAsync(context.Request.Body, context.Request.Headers["X-Archives-Manifest"].ToString(),
        context.Request.Headers["X-Archives-Expected-Release"].ToString(), context.RequestAborted);
    return Results.Json(receipt, ContentJson.Options);
});
app.MapGet("/Archives/api/catalog", () =>
{
    var catalog = store.Catalog;
    return Results.Json(new { libraries = catalog.Libraries.Values.Select(library => new {
        library.PublisherId, library.LibraryId, library.ReleaseId, library.Labels,
        nodes = library.Nodes.Values.OrderBy(node => node.Order).Select(node => new {
            node.Id, node.ParentId, node.Type, node.Title, node.Order,
            cover = node.Cover?.StartsWith("/media/", StringComparison.OrdinalIgnoreCase) == true
                ? $"/Archives/_editions/{library.PublisherId}/{library.Editions[node.Id]}" + node.Cover
                : node.Cover
        })
    }) }, ContentJson.Options);
});
app.MapGet("/Archives/api/view/{publisherId}/{nodeId}", async (string publisherId, string nodeId, CancellationToken ct) =>
{
    var catalog = store.Catalog;
    if (!catalog.Libraries.TryGetValue(publisherId, out var library) || !library.Nodes.TryGetValue(nodeId, out var node)) return Results.NotFound();
    var edition = await store.GetEditionAsync(publisherId, library.Editions[nodeId], ct);
    var runtime = await edition.Runtime.EnsureRuntimeAsync(node.WorkId ?? "library", edition.Gateway, ct);
    return Results.Json(new { node.Id, node.Title, node.Type, url = runtime.ProxyPrefix + (node.Type == "page" ? "page/" : "browse/") + Uri.EscapeDataString(node.Id) });
});
app.MapGet("/Archives/_editions/{publisherId}/{editionId}/api/state", (HttpContext context, string publisherId, string editionId) =>
{
    var edition = store.LoadedEdition(publisherId, editionId);
    return edition is not null && Internal(context, edition) ? Results.Json(edition.State, ContentJson.Options) : Results.NotFound();
});
app.MapGet("/Archives/_editions/{publisherId}/{editionId}/api/renderer/page/{id}", (HttpContext context, string publisherId, string editionId, string id) =>
{
    var edition = store.LoadedEdition(publisherId, editionId);
    return edition is not null && Internal(context, edition) && edition.State.Pages.TryGetValue(id, out var page) ? Results.Json(page, ContentJson.Options) : Results.NotFound();
});
app.MapGet("/Archives/_editions/{publisherId}/{editionId}/media/{**path}", async (string publisherId, string editionId, string path, CancellationToken ct) =>
{
    if (!store.IsPublished(publisherId, editionId)) return Results.NotFound();
    var edition = await store.GetEditionAsync(publisherId, editionId, ct);
    var file = ArchivesStore.SafePath(Path.Combine(edition.Root, "Media"), path);
    if (!File.Exists(file) || !types.TryGetContentType(file, out var contentType)) return Results.NotFound();
    return Results.File(file, contentType, enableRangeProcessing: true);
});
app.Map("/Archives/_editions/{publisherId}/{editionId}/_runtime/{packageId}/{generationId}/{**path}",
    async (HttpContext context, IHttpForwarder forwarder, string publisherId, string editionId, string packageId, string generationId) =>
{
    if (!store.IsPublished(publisherId, editionId)) { context.Response.StatusCode = 404; return; }
    var edition = await store.GetEditionAsync(publisherId, editionId, context.RequestAborted);
    var generation = edition.Runtime.ResolveGeneration(packageId, generationId);
    if (generation is null) { context.Response.StatusCode = 404; return; }
    edition.Runtime.Lease(packageId);
    var error = await forwarder.SendAsync(context, generation.UpstreamUrl, proxy,
        new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromHours(1) }, HttpTransformer.Default);
    if (error != ForwarderError.None) app.Logger.LogWarning("Archives renderer proxy: {Error}", error);
});
app.MapGet("/Archives/assets/{file}", (string file) =>
{
    var contentType = file switch
    {
        "archives.css" => "text/css",
        "archives.js" => "text/javascript",
        "atlas-mark.svg" => "image/svg+xml",
        "favicon.ico" => "image/x-icon",
        "site.webmanifest" => "application/manifest+json",
        "atlas-emblem.png" or "atlas-emblem-96.png" or "favicon-32.png" or "favicon-16.png"
            or "apple-touch-icon.png" or "icon-192.png" or "icon-512.png" => "image/png",
        _ => null
    };
    return contentType is null ? Results.NotFound()
        : Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", file), contentType);
});
app.MapGet("/Archives", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), "text/html"));
app.MapGet("/Archives/{publisherId}/{nodeId}", (string publisherId, string nodeId) =>
    store.Catalog.Libraries.TryGetValue(publisherId, out var library) && (nodeId == library.LibraryId || library.Nodes.ContainsKey(nodeId))
        ? Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), "text/html") : Results.NotFound());
await app.RunAsync();

bool Authorized(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length is < 40 or > 600) return false;
    return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(header[7..])), tokenHash);
}
static bool Internal(HttpContext context, ArchivesStore.Edition edition)
{
    var header = context.Request.Headers.Authorization.ToString();
    return context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes("Bearer " + edition.GatewayToken));
}
static async Task Error(HttpContext context, int code, string message)
{
    if (context.Response.HasStarted) return;
    context.Response.StatusCode = code; await context.Response.WriteAsJsonAsync(new { error = message });
}
