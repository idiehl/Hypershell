using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Meridian.Host;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ProjectValidator.MaxRequestBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = ProjectValidator.MaxRequestBytes);
var dataRoot = builder.Configuration["data-root"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasMeridian");
builder.Services.AddSingleton(new ProjectStore(dataRoot));
builder.Services.AddSingleton(new LibraryStore(dataRoot));
builder.Services.AddSingleton(new ModuleStore(dataRoot));
var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        var remote = context.Connection.RemoteIpAddress;
        var hostname = context.Request.Host.Host.Trim('[', ']');
        if (remote is null || !IPAddress.IsLoopback(remote)
            || !(hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(hostname, out var hostAddress) && IPAddress.IsLoopback(hostAddress)))
            throw new ApiException(403, "Meridian's local host accepts loopback requests only.");

        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            var origin = context.Request.Headers.Origin.ToString();
            var expectedOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
            if (origin.Length > 0 && !string.Equals(origin, expectedOrigin, StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.Request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase))
                throw new ApiException(403, "Writes must originate from this Meridian window.");
            if (context.Request.Path != "/api/import" && !context.Request.HasJsonContentType())
                throw new ApiException(415, "This endpoint requires application/json.");
        }

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.Headers.CacheControl = "no-store";
        await next(context);
    }
    catch (ApiException exception) { await Error(context, exception.Status, exception.Message); }
    catch (BadHttpRequestException exception) { await Error(context, exception.StatusCode, "Invalid or oversized request."); }
    catch (System.Text.Json.JsonException) { await Error(context, 400, "Invalid JSON document."); }
    catch (InvalidDataException exception) { await Error(context, 400, exception.Message); }
    catch (IOException exception)
    {
        app.Logger.LogError(exception, "Meridian storage operation failed");
        await Error(context, 503, "The project could not be saved or read. Your current editor content has not been discarded.");
    }
    catch (UnauthorizedAccessException exception)
    {
        app.Logger.LogError(exception, "Meridian storage access denied");
        await Error(context, 503, "Meridian cannot access its project storage directory.");
    }
});

app.MapGet("/__meridian_status", (HttpContext context) =>
{
    context.Response.Headers["X-Atlas-Meridian"] = "true";
    return Results.Json(new { application = "atlas-meridian", version = 1, status = "ready", capabilities = new[] { "authoring-save-guard-v1", "library-v1", "library-canvas-v2", "modules-v1", "numeric-systems-v1", "authoring-recovery-guard-v1", "spatial-children-v1", "link-disclosure-v1", "selection-snippets-v1", "rule-impact-v1", "grid-cells-v1", "node-hub-v1", "smart-links-v1", "hover-readability-v1", "balanced-text-v1" } });
});
app.MapGet("/api/modules", (ModuleStore store) => Results.Json(store.List()));
app.MapGet("/api/modules/{id}", (string id, ModuleStore store) => Results.Json(store.Read(id)));
app.MapPost("/api/modules", async (HttpRequest request, ModuleStore store) =>
{
    var body = await ReadObject(request);
    var module = body["module"] as JsonObject ?? throw new ApiException(400, "A module object is required.");
    return Results.Json(await store.Create(module), statusCode: 201);
});
app.MapGet("/api/library", (LibraryStore store) => Results.Json(store.Read()));
app.MapGet("/api/library/catalog", (ProjectStore store) => Results.Json(LibraryStore.Catalog(store)));
app.MapPut("/api/library", async (HttpRequest request, LibraryStore store) =>
{
    var body = await ReadObject(request);
    var library = body["library"] as JsonObject ?? throw new ApiException(400, "A library object is required.");
    return Results.Json(await store.Save(library, ProjectValidator.Integer(body, "expectedRevision")));
});
app.MapGet("/api/library/recovery", (LibraryStore store) => Results.Json(store.ReadRecovery()));
app.MapPut("/api/library/recovery", async (HttpRequest request, LibraryStore store) =>
{
    var body = await ReadObject(request);
    var library = body["library"] as JsonObject ?? throw new ApiException(400, "A library object is required.");
    return Results.Json(await store.SaveRecovery(library, ProjectValidator.Integer(body, "baseRevision"), OptionalToken(body, "expectedSavedAt")));
});
app.MapDelete("/api/library/recovery", async (HttpRequest request, LibraryStore store) =>
{
    var body = await ReadObject(request);
    await store.DeleteRecovery(ProjectValidator.Integer(body, "baseRevision"), ProjectValidator.Text(body, "savedAt", 64));
    return Results.NoContent();
});
app.MapGet("/api/library/checkpoints", (LibraryStore store) => Results.Json(store.ListCheckpoints()));
app.MapPost("/api/library/checkpoints", async (HttpRequest request, LibraryStore store) =>
{
    var body = await ReadObject(request);
    var library = body["library"] as JsonObject ?? throw new ApiException(400, "A library object is required.");
    return Results.Json(await store.SaveCheckpoint(ProjectValidator.Text(body, "name", 120), library), statusCode: 201);
});
app.MapGet("/api/library/checkpoints/{checkpointId}", (string checkpointId, LibraryStore store) => Results.Json(store.ReadCheckpoint(checkpointId)));
app.MapGet("/api/projects", (ProjectStore store) => store.List());
app.MapGet("/api/projects/{id}", (string id, ProjectStore store) => Results.Json(store.Read(id)));
app.MapPost("/api/projects", async (HttpRequest request, ProjectStore store) =>
    Results.Json(await store.Create(await ReadObject(request)), statusCode: 201));
app.MapPut("/api/projects/{id}", async (string id, HttpRequest request, ProjectStore store) =>
{
    var body = await ReadObject(request);
    return Results.Json(await store.Save(id, RequiredProject(body), ProjectValidator.Integer(body, "expectedRevision"), OptionalBoolean(body, "requireNoRecovery")));
});
app.MapPost("/api/projects/{id}/authoring", async (string id, HttpRequest request, ProjectStore store) =>
{
    var body = await ReadObject(request);
    if (!body.ContainsKey("expectedRecoverySavedAt")) throw new ApiException(400, "An explicit recovery token or null is required.");
    return Results.Json(await store.ApplyAuthoring(id, RequiredProject(body), ProjectValidator.Integer(body, "expectedRevision"), OptionalToken(body, "expectedRecoverySavedAt"), ProjectValidator.Text(body, "checkpointName", 100)));
});
app.MapPut("/api/projects/{id}/recovery", async (string id, HttpRequest request, ProjectStore store) =>
{
    var body = await ReadObject(request);
    return Results.Json(await store.SaveRecovery(id, RequiredProject(body), ProjectValidator.Integer(body, "baseRevision"), OptionalToken(body, "expectedSavedAt")));
});
app.MapGet("/api/projects/{id}/recovery", (string id, ProjectStore store) => Results.Json(store.ReadRecovery(id)));
app.MapDelete("/api/projects/{id}/recovery", async (string id, HttpRequest request, ProjectStore store) =>
{
    var body = await ReadObject(request);
    await store.DeleteRecovery(id, ProjectValidator.Integer(body, "baseRevision"), ProjectValidator.Text(body, "savedAt", 64));
    return Results.NoContent();
});
app.MapPost("/api/projects/{id}/checkpoints", async (string id, HttpRequest request, ProjectStore store) =>
{
    var body = await ReadObject(request);
    return Results.Json(await store.SaveCheckpoint(id, ProjectValidator.Text(body, "name", 120), RequiredProject(body)), statusCode: 201);
});
app.MapGet("/api/projects/{id}/checkpoints", (string id, ProjectStore store) => Results.Json(store.ListCheckpoints(id)));
app.MapGet("/api/projects/{id}/checkpoints/{checkpointId}", (string id, string checkpointId, ProjectStore store) =>
    Results.Json(store.ReadCheckpoint(id, checkpointId)));
app.MapPost("/api/import", async (HttpRequest request) =>
{
    if (!request.HasFormContentType) throw new ApiException(415, "Import requires multipart/form-data with a file field.");
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? throw new ApiException(400, "Choose a .meridian project file.");
    if (!file.FileName.EndsWith(".meridian", StringComparison.OrdinalIgnoreCase))
        throw new ApiException(400, "Project files must use the .meridian extension.");
    if (file.Length > ProjectValidator.MaxRequestBytes) throw new ApiException(413, "The project package is too large.");
    await using var stream = file.OpenReadStream();
    return Results.Json(ProjectPackage.Import(stream));
});
app.MapPost("/api/export", async (HttpRequest request) =>
{
    var project = await ReadObject(request);
    ProjectValidator.Validate(project);
    return Results.File(ProjectPackage.Export(project), "application/octet-stream", "project.meridian");
});

var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (!Directory.Exists(webRoot)) webRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../dist"));
if (Directory.Exists(webRoot))
{
    var files = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}
else
{
    app.MapGet("/", () => Results.Text("Meridian assets are missing. Run npm run build, then restart the host.", statusCode: 503));
}
app.Run();

static async Task<JsonObject> ReadObject(HttpRequest request) =>
    (await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted)) as JsonObject
    ?? throw new ApiException(400, "A JSON object is required.");
static JsonObject RequiredProject(JsonObject body) => body["project"] as JsonObject
    ?? throw new ApiException(400, "A project object is required.");
static string? OptionalToken(JsonObject body, string name) => body[name] is null ? null : ProjectValidator.Text(body, name, 64);
static bool OptionalBoolean(JsonObject body, string name)
{
    if (!body.TryGetPropertyValue(name, out var value)) return false;
    if (value is JsonValue flag && flag.TryGetValue<bool>(out var result)) return result;
    throw new ApiException(400, $"'{name}' must be Boolean.");
}
static async Task Error(HttpContext context, int status, string message)
{
    if (context.Response.HasStarted) return;
    context.Response.Clear();
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}
