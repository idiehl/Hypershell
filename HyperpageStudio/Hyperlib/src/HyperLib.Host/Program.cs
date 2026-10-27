using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using HyperLib.Core;
using HyperLib.Runtime;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);
var root = FindRoot(builder.Configuration["root"] ?? Environment.GetEnvironmentVariable("HYPERLIB_ROOT"));
var port = int.TryParse(builder.Configuration["port"], out var requestedPort) ? requestedPort : 54180;
if (port is < 1024 or > 65535) throw new ArgumentException("Choose a port between 1024 and 65535.");
var origin = $"http://127.0.0.1:{port}";
builder.WebHost.UseUrls(origin);
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; });
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 65L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 65L * 1024 * 1024);
builder.Services.AddHttpForwarder();
var store = new ContentStore(root);
await store.InitializeAsync();
var archives = new ArchivesPublicationService(root, store);
await BaseTemplateLibraryInstaller.EnsureCatalogAsync(root);
if (builder.Configuration.GetValue<bool>("install-base-templates"))
{
    var installed = await BaseTemplateLibraryInstaller.InstallAsync(root, store);
    Console.WriteLine($"BASE_TEMPLATE_LIBRARY_INSTALLED revision={installed.Revision} templates=17 demoWorks=15");
    if (builder.Configuration.GetValue<bool>("prepare-only")) return;
}
await using var runtime = new HyperLibRuntimeSupervisor(root);
await runtime.EnsureProjectsAsync(await store.ReadAsync());
store.TemplateResolver = runtime.GetTemplate;
var events = new EventHub();
runtime.GenerationChanged += generation => events.Publish("runtime", generation);
runtime.StatusChanged += state => events.Publish("runtime-status", state);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(runtime);
var app = builder.Build();
using var proxyClient = new HttpMessageInvoker(new SocketsHttpHandler
{
    UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None,
    UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10)
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    // The host is deliberately local. Reject DNS rebinding and cross-origin writes.
    if (context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != port)
    {
        context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid local host." }); return;
    }
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not "GET" and not "HEAD")
    {
        var requestOrigin = context.Request.Headers.Origin.ToString();
        if (context.Request.Headers["X-HyperLib-Client"] != "editor" || requestOrigin.Length > 0 && requestOrigin != origin)
        {
            context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Use the HyperLib editor to change this library." }); return;
        }
    }
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (ConflictException error) { await Error(context, 409, error.Message); }
    catch (ArchivesDraftConflictException error) { await Error(context, 409, error.Message); }
    catch (ContentValidationException error) { await Error(context, 400, error.Message); }
    catch (KeyNotFoundException error) { await Error(context, 404, error.Message); }
    catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
    {
        app.Logger.LogError(error, "HyperLib request failed"); await Error(context, 503, error.Message);
    }
});
app.UseWebSockets();
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(webRoot)) webRoot = Path.Combine(root, "src", "HyperLib.Host", "wwwroot");
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(webRoot) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot) });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(root, "Media")), RequestPath = "/media",
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public,max-age=300"
});
app.MapGet("/api/health", (HttpContext context) =>
{
    context.Response.Headers["X-HyperLib-App"] = "hyperlib";
    return Results.Ok(new { app = "hyperlib", schemaVersion = 1, templateCatalogVersion = 1, root, origin });
});
app.MapGet("/api/state", async (CancellationToken ct) => Results.Json(await store.ReadAsync(ct), ContentJson.Options));
app.MapGet("/api/archives/drafts", async (string rootNodeId, CancellationToken ct) =>
{
    var draft = await archives.GetLatestDraftAsync(rootNodeId, ct);
    return draft is null ? Results.NotFound(new { error = "This branch has no publishing review yet." })
        : Results.Json(draft, ContentJson.Options);
});
app.MapGet("/api/archives/drafts/{id}", async (string id, CancellationToken ct) =>
    Results.Json(await archives.GetDraftAsync(id, ct), ContentJson.Options));
app.MapPost("/api/archives/drafts", async (ArchivesCreateRequest request, CancellationToken ct) =>
    Results.Json(await archives.CreateDraftAsync(request.RootNodeId, request.ExpectedContentRevision, ct), ContentJson.Options));
app.MapPut("/api/archives/drafts/{id}", async (string id, ArchivesSelectionRequest request, CancellationToken ct) =>
    Results.Json(await archives.UpdateSelectionAsync(id, request.ExpectedDraftRevision, request.SelectedNodeIds, ct), ContentJson.Options));
app.MapPost("/api/archives/drafts/{id}/refresh", async (string id, ArchivesRefreshRequest request, CancellationToken ct) =>
    Results.Json(await archives.RefreshDraftAsync(id, request.ExpectedDraftRevision, request.ExpectedContentRevision, ct), ContentJson.Options));
app.MapGet("/api/base-templates", () => Results.Json(ReusableTemplateCatalog.List(root), ContentJson.Options));
app.MapGet("/api/renderer/page/{id}", async (string id, CancellationToken ct) =>
{
    var state = await store.ReadAsync(ct);
    return state.Pages.TryGetValue(id, out var page) ? Results.Json(page, ContentJson.Options) : Results.NotFound(new { error = "Page not found." });
});
app.MapGet("/api/templates/{id}", (string id) => runtime.GetTemplate(id) is { } template
    ? Results.Json(template, ContentJson.Options) : Results.NotFound(new { error = "Template not found." }));
app.MapPost("/api/operations", async (OperationBatch batch, CancellationToken ct) =>
{
    if (batch.Operations is null || batch.Operations.Any(o => o is null)) throw new ContentValidationException("Operations cannot be null.");
    foreach (var operation in batch.Operations.Where(o => o.Kind == "createNode" && o.NodeType == "work" && o.Id is not null))
    {
        TemplateValidator.ValidateId(operation.Id, "work");
        if (Directory.Exists(store.ResolvePath("Works/" + operation.Id)))
            throw new ContentValidationException("This work has retained source files. Restore it with Undo or create a work with a new identity.");
    }
    var state = await store.ApplyAsync(batch, ct);
    await PublishCommitted(state);
    return Results.Json(state, ContentJson.Options);
});
app.MapPost("/api/undo", async (RevisionRequest request, CancellationToken ct) =>
{
    var state = await store.UndoAsync(request.ExpectedRevision, ct); await PublishCommitted(state);
    return Results.Json(state, ContentJson.Options);
});
app.MapPost("/api/redo", async (RevisionRequest request, CancellationToken ct) =>
{
    var state = await store.RedoAsync(request.ExpectedRevision, ct); await PublishCommitted(state);
    return Results.Json(state, ContentJson.Options);
});
app.MapPost("/api/media", async (HttpRequest request, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file") ?? throw new ContentValidationException("Choose a media file.");
    await using var input = file.OpenReadStream();
    return Results.Json(await store.ImportMediaAsync(input, file.FileName, file.ContentType, ct), ContentJson.Options);
}).DisableAntiforgery();
app.MapGet("/api/runtime/status", () => Results.Json(runtime.GetStatuses(), ContentJson.Options));
app.MapGet("/api/runtime/{id}", async (string id, CancellationToken ct) => Results.Json(await runtime.EnsureRuntimeAsync(id, origin, ct), ContentJson.Options));
app.MapPost("/api/runtime/{id}/lease", (string id) => { runtime.Lease(id); return Results.Ok(new { ok = true }); });
app.Map("/_runtime/{packageId}/{generationId}/{**path}", async (HttpContext context, IHttpForwarder forwarder, string packageId, string generationId) =>
{
    var generation = runtime.ResolveGeneration(packageId, generationId);
    if (generation is null) { context.Response.StatusCode = 404; return; }
    runtime.Lease(packageId);
    var error = await forwarder.SendAsync(context, generation.UpstreamUrl, proxyClient,
        new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(10) }, HttpTransformer.Default);
    if (error != ForwarderError.None) app.Logger.LogWarning("Renderer forward failed: {Error}", error);
});
app.MapGet("/api/events", async (HttpContext context) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    var (id, reader) = events.Subscribe();
    try
    {
        await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await foreach (var message in reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync(message, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    finally { events.Unsubscribe(id); }
});
app.Lifetime.ApplicationStopping.Register(events.Complete);
app.Logger.LogInformation("HyperLib {Root} listening at {Origin}", root, origin);
await app.RunAsync();

async Task PublishCommitted(LibraryState state)
{
    events.Publish("content", new { state.Revision });
    try { await runtime.EnsureProjectsAsync(state, app.Lifetime.ApplicationStopping); }
    catch (Exception error) when (error is IOException or InvalidOperationException or ContentValidationException or OperationCanceledException)
    {
        app.Logger.LogError(error, "Content revision {Revision} is saved; project setup needs attention", state.Revision);
        events.Publish("runtime-status", new { packageId = "library", state = "error", error = "Content is saved. Project setup: " + error.Message });
    }
}

static async Task Error(HttpContext context, int code, string message)
{
    if (context.Response.HasStarted) return;
    context.Response.StatusCode = code; await context.Response.WriteAsJsonAsync(new { error = message });
}
static string FindRoot(string? configured)
{
    if (!string.IsNullOrWhiteSpace(configured))
    {
        var result = Path.GetFullPath(configured);
        if (!File.Exists(Path.Combine(result, "hyperlib.app.json"))) throw new DirectoryNotFoundException("The selected directory is not a HyperLib app.");
        return result;
    }
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var candidate = new DirectoryInfo(start); candidate is not null; candidate = candidate.Parent)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "hyperlib.app.json"))) return candidate.FullName;
            if (File.Exists(Path.Combine(candidate.FullName, "Hyperlib", "hyperlib.app.json"))) return Path.Combine(candidate.FullName, "Hyperlib");
        }
    throw new DirectoryNotFoundException("Pass --root with the HyperLib directory.");
}
internal sealed record RevisionRequest(long ExpectedRevision);
internal sealed record ArchivesCreateRequest(string RootNodeId, long ExpectedContentRevision);
internal sealed record ArchivesSelectionRequest(long ExpectedDraftRevision, string[] SelectedNodeIds);
internal sealed record ArchivesRefreshRequest(long ExpectedDraftRevision, long ExpectedContentRevision);
internal sealed class EventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();
    public (Guid, ChannelReader<string>) Subscribe()
    {
        var id = Guid.NewGuid(); var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel; return (id, channel.Reader);
    }
    public void Publish(string kind, object value)
    {
        var message = $"event: {kind}\ndata: {JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))}\n\n";
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(message);
    }
    public void Unsubscribe(Guid id) { if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete(); }
    public void Complete() { foreach (var id in _subscribers.Keys) Unsubscribe(id); }
}
