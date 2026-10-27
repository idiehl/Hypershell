using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record HyperlibTemplateObjectPreview(Uri Url, string Message);

/// <summary>Renders real template components against disposable sample content, outside every library and Work.</summary>
public sealed class HyperlibTemplateObjectPreviewService : IDisposable
{
    private const string Version = "object-preview-2";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuildGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".hyperlib", "node_modules", "output", "artifacts" };
    private readonly string _cacheRoot;
    private readonly string _sessionRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly object _processLock = new();
    private Process? _host;
    private WindowsProcessLifetimeJob? _job;
    private string? _activeFingerprint;
    private Uri? _origin;
    private bool _disposed;

    public HyperlibTemplateObjectPreviewService(string? cacheRoot = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "Hyperlib", "object-preview"));
        _sessionRoot = Path.Combine(_cacheRoot, "sessions", Guid.NewGuid().ToString("N"));
    }

    public Task<HyperlibTemplateObjectPreview> PreviewAsync(string libraryRoot, string groupId, string objectId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = new HyperlibWorkProjectService(libraryRoot);
        var projectRoot = catalog.GetCatalogProjectRoot(groupId);
        var item = catalog.GetCatalogObject(groupId, objectId);
        return PreviewCoreAsync(libraryRoot, projectRoot, item, cancellationToken);
    }

    /// <summary>Previews a Work's actual declared element and owned source without importing or changing its project.</summary>
    public Task<HyperlibTemplateObjectPreview> PreviewElementAsync(string libraryRoot, string projectRoot, string typeId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateValidator.ValidateId(typeId, "element");
        var library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        if (!project.Equals(library, StringComparison.OrdinalIgnoreCase)
            && !project.StartsWith(library + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ContentValidationException("The element preview project must belong to its source library.");
        var definition = TemplateObjectLibrary.ReadDefinition(project);
        var element = definition.Blocks.SingleOrDefault(block => block.Id == typeId)
            ?? throw new ContentValidationException("This element is no longer declared by the Work. Refresh its Elements collection.");
        var item = new TemplateLibraryObject
        {
            Id = typeId, GroupId = "current-work", Title = element.Title,
            Description = element.Description ?? "An editable element from this Work.",
            Category = element.Category ?? "Page elements", Kind = "element",
            CatalogId = element.CatalogId ?? "LOCAL-ELEMENT", Element = element, Definition = definition
        };
        return PreviewCoreAsync(library, project, item, cancellationToken);
    }

    private async Task<HyperlibTemplateObjectPreview> PreviewCoreAsync(string libraryRoot, string projectRoot,
        TemplateLibraryObject item, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var payload = CreatePayload(item);
            var snapshot = Capture(libraryRoot, projectRoot, item.Definition!);
            var fingerprint = Fingerprint(snapshot);
            var generation = Safe(_cacheRoot, Path.Combine("generations", fingerprint));
            var buildGate = BuildGates.GetOrAdd(generation, _ => new SemaphoreSlim(1, 1));
            await buildGate.WaitAsync(ct);
            try { await EnsureBuildAsync(generation, snapshot, ct); }
            finally { buildGate.Release(); }
            ct.ThrowIfCancellationRequested();
            if (_activeFingerprint != fingerprint || _host is null || _host.HasExited)
            {
                StopHost();
                await StartHostAsync(generation, fingerprint, ct);
            }
            Directory.CreateDirectory(_sessionRoot);
            var requestId = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Safe(_sessionRoot, requestId + ".json"), JsonSerializer.Serialize(payload, ContentJson.Options), ct);
            var address = new Uri(_origin!, "preview/" + requestId);
            using var response = await _http.GetAsync(address, ct);
            var html = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode || html.Contains("data-preview-error", StringComparison.Ordinal))
                throw new InvalidOperationException("The object's Razor preview could not render. " +
                    (html.Length > 1800 ? html[..1800] : html));
            return new(address, "Visual preview in a sample Page. Widgets become interactive when used in a Work.");
        }
        catch (OperationCanceledException) { StopHost(); throw; }
        finally { _gate.Release(); }
    }

    private static PreviewPayload CreatePayload(TemplateLibraryObject item)
    {
        var definition = HyperlibTemplateEditSession.CloneDefinition(item.Definition ?? throw new ContentValidationException("The object has no page schema."));
        foreach (var appearance in item.Appearance)
        {
            var field = ContentJson.Clone(appearance);
            field.DefaultValue = appearance.DefaultValue?.Clone();
            var index = definition.Appearance.FindIndex(candidate => candidate.Id == appearance.Id);
            if (index >= 0) definition.Appearance[index] = field;
            else definition.Appearance.Add(field);
        }
        var blocks = ContentJson.Clone(item.Blocks.Count > 0 ? item.Blocks : definition.DefaultBlocks);
        if (item.Element is { } element)
        {
            if (item.Blocks.Count == 0)
            {
                var region = definition.Regions.First(candidate => candidate.AllowedBlocks.Count == 0 || candidate.AllowedBlocks.Contains(element.Id));
                blocks = [new PageBlock { Id = "preview-object", Type = element.Id, Region = region.Id,
                    Values = element.Fields.Where(field => field.DefaultValue.HasValue)
                        .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal) }];
                foreach (var field in element.Fields.Where(field => (field.Type is "string" or "text")
                    && (field.Id is "title" or "text" or "description")))
                {
                    if (blocks[0].Values.TryGetValue(field.Id, out var value) && value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(value.GetString())) continue;
                    blocks[0].Values[field.Id] = ContentJson.Value(field.Id switch
                    {
                        "title" => "A heading for your ideas",
                        "description" => "A little context gives the next idea room to grow.",
                        _ => "Start with a thought, add a little detail, and shape it into something worth sharing."
                    });
                }
            }
        }
        TemplateValidator.ValidateDefinition(definition);
        var page = new PageDocument { Id = "object-preview", WorkId = "preview-work", TemplateId = definition.TemplateId,
            Title = item.Title, UsesEditableChrome = true, Blocks = blocks };
        TemplateValidator.ValidatePage(definition, page);
        return new PreviewPayload(definition, page, item.Title, item.Element is not null || item.Kind == "preset" || blocks.Count == 0);
    }

    private sealed record PreviewPayload(TemplateDefinition Template, PageDocument Page, string Title, bool SingleObject);
    private sealed record SnapshotFile(string RelativePath, byte[] Bytes);

    private static List<SnapshotFile> Capture(string libraryRoot, string projectRoot, TemplateDefinition definition)
    {
        var result = new List<SnapshotFile>();
        long bytes = 0;
        var count = 0;
        void Add(string path, string relative)
        {
            VisualPageProjectService.RejectReparsePoint(path);
            var info = new FileInfo(path);
            if (++count > 5000 || (bytes += info.Length) > 128L * 1024 * 1024)
                throw new ContentValidationException("The preview source exceeds its 5,000 file / 128 MiB limit.");
            result.Add(new(relative.Replace('\\', '/'), File.ReadAllBytes(path)));
        }
        foreach (var (source, destination) in new[]
        {
            (Path.Combine(libraryRoot, "src", "HyperLib.Core"), "sdk/HyperLib.Core"),
            (Path.Combine(libraryRoot, "src", "HyperLib.Runtime"), "sdk/HyperLib.Runtime"),
            (Path.Combine(projectRoot, "src", "Pages"), "project/Pages")
        })
        {
            if (!Directory.Exists(source)) throw new ContentValidationException("The Design Group is missing renderer source: " + source);
            foreach (var file in Enumerate(source)) Add(file, destination + "/" + Path.GetRelativePath(source, file));
        }
        // Local media is copied only when referenced by the schema or source, never the user's entire media collection.
        var references = JsonSerializer.Serialize(definition, ContentJson.Options) + "\n" + string.Join('\n', result
            .Where(file => Path.GetExtension(file.RelativePath) is ".razor" or ".css" or ".cs" or ".js")
            .Select(file => Encoding.UTF8.GetString(file.Bytes)));
        foreach (Match match in Regex.Matches(references, @"/media/([A-Za-z0-9_./% -]+)"))
        {
            var relative = Uri.UnescapeDataString(match.Groups[1].Value).TrimEnd();
            if (relative.Split('/').Any(part => part is ".." or "." || part.Length == 0)) continue;
            var source = Safe(Path.Combine(libraryRoot, "Media"), relative);
            var target = "host/wwwroot/media/" + relative;
            if (File.Exists(source) && result.All(file => file.RelativePath != target)) Add(source, target);
        }
        return result;
    }

    private static IEnumerable<string> Enumerate(string directory)
    {
        VisualPageProjectService.RejectReparsePoint(directory);
        foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
        { VisualPageProjectService.RejectReparsePoint(file); yield return file; }
        foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            if (!Excluded.Contains(Path.GetFileName(child)))
                foreach (var file in Enumerate(child)) yield return file;
    }

    private static string Fingerprint(IEnumerable<SnapshotFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(Version));
        hash.AppendData(Encoding.UTF8.GetBytes(HostProject + HostProgram + HostPage));
        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        { hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath)); hash.AppendData(file.Bytes); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task EnsureBuildAsync(string generation, List<SnapshotFile> files, CancellationToken ct)
    {
        var ready = Safe(generation, "ready.json");
        if (File.Exists(ready) && File.Exists(Safe(generation, "bin/Host.dll"))) return;
        foreach (var file in files)
        {
            var target = Safe(generation, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, file.Bytes, ct);
        }
        // Preserve source references only when their complete dependency remains inside this snapshot.
        foreach (var project in Directory.EnumerateFiles(Safe(generation, "project/Pages"), "*.csproj", SearchOption.AllDirectories))
        {
            var xml = XDocument.Load(project);
            foreach (var reference in xml.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value ?? "";
                if (include.EndsWith("HyperLib.Runtime.csproj", StringComparison.OrdinalIgnoreCase) || include == "__SDK_REF__")
                    reference.SetAttributeValue("Include", Path.GetRelativePath(Path.GetDirectoryName(project)!, Safe(generation, "sdk/HyperLib.Runtime/HyperLib.Runtime.csproj")));
                else if (include.EndsWith("HyperLib.Core.csproj", StringComparison.OrdinalIgnoreCase))
                    reference.SetAttributeValue("Include", Path.GetRelativePath(Path.GetDirectoryName(project)!, Safe(generation, "sdk/HyperLib.Core/HyperLib.Core.csproj")));
                else
                {
                    var dependency = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                    if (!dependency.StartsWith(Safe(generation, "project/Pages") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(dependency))
                        throw new ContentValidationException("The preview needs a project dependency outside the Design Group: " + include);
                }
            }
            xml.Save(project);
        }
        var host = Safe(generation, "host");
        Directory.CreateDirectory(host);
        await File.WriteAllTextAsync(Path.Combine(host, "Host.csproj"), HostProject, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "Program.cs"), HostProgram, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "ObjectPage.razor"), HostPage, ct);
        var dotnet = VisualPagePreviewBuildService.ResolveDotNetExecutable()
            ?? throw new InvalidOperationException("The .NET 10 SDK is required to preview template components.");
        var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest
        {
            ExecutablePath = dotnet, WorkingDirectory = generation,
            // Loopback previews serve the original static assets. Precompressing the complete dependency tree
            // adds no preview value and can overflow Windows' process-command limit in deep cache folders.
            // A global property also covers referenced Razor projects; it does not modify any source asset.
            Arguments = ["publish", Path.Combine(host, "Host.csproj"), "-c", "Release", "-o", Safe(generation, "bin"),
                "-p:CompressionEnabled=false", "--nologo", "-v:minimal"],
            Timeout = TimeSpan.FromMinutes(4), MaximumStandardOutputCharacters = 160_000, MaximumStandardErrorCharacters = 32_000
        }, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("The Design Group could not compile for preview. " +
            result.FailureMessage + "\n" + result.StandardOutput + "\n" + result.StandardError);
        await File.WriteAllTextAsync(ready, "{}", ct);
    }

    private async Task StartHostAsync(string generation, string fingerprint, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = new Uri($"http://127.0.0.1:{port}/");
        var output = Safe(generation, "bin");
        Directory.CreateDirectory(_sessionRoot);
        var start = new ProcessStartInfo(VisualPagePreviewBuildService.ResolveDotNetExecutable()!)
        { WorkingDirectory = output, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(output, "Host.dll"));
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add(origin.AbsoluteUri);
        start.Environment["HYPERLIB_OBJECT_PREVIEW_CONTENT"] = _sessionRoot;
        var pagesProject = XDocument.Load(Safe(generation, "project/Pages/Pages.csproj"));
        var assemblyName = pagesProject.Descendants("AssemblyName").FirstOrDefault()?.Value ?? "Pages";
        if (!Regex.IsMatch(assemblyName, "^[A-Za-z0-9_.-]+$"))
            throw new ContentValidationException("The component assembly name must be explicit for preview.");
        start.Environment["HYPERLIB_OBJECT_PREVIEW_ASSEMBLY"] = assemblyName;
        var process = new Process { StartInfo = start };
        try
        {
            lock (_processLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!process.Start()) throw new InvalidOperationException("The component preview could not start.");
                _host = process;
                _job ??= WindowsProcessLifetimeJob.Create();
                _job.Assign(process);
            }
            var logs = new StringBuilder();
            void Log(object sender, DataReceivedEventArgs e) { if (e.Data is not null) lock (logs) { if (logs.Length < 16000) logs.AppendLine(e.Data); } }
            process.OutputDataReceived += Log; process.ErrorDataReceived += Log;
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("The preview renderer stopped. " + logs);
                try
                {
                    using var response = await _http.GetAsync(new Uri(origin, "healthz"), ct);
                    if (response.IsSuccessStatusCode) { _origin = origin; _activeFingerprint = fingerprint; return; }
                }
                catch (HttpRequestException) { }
                await Task.Delay(200, ct);
            }
            throw new InvalidOperationException("The component preview did not become ready. " + logs);
        }
        catch { StopHost(); throw; }
    }

    private void StopHost()
    {
        lock (_processLock)
        {
            if (_host is { } process)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose(); _host = null;
            }
            _activeFingerprint = null; _origin = null;
        }
    }

    private static string Safe(string root, string relative)
        => VisualPageProjectService.ResolveProjectPath(root, relative);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel(); StopHost(); _job?.Dispose(); _job = null;
        _http.Dispose();
    }

    private const string HostProject = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><RootNamespace>HyperlibObjectPreview</RootNamespace><AssemblyName>Host</AssemblyName></PropertyGroup>
          <ItemGroup><ProjectReference Include="../project/Pages/Pages.csproj" /></ItemGroup>
        </Project>
        """;

    private const string HostProgram = """
        using System.Text.Json;
        using System.Net;
        using HyperLib.Core;
        using HyperLib.Runtime;
        using HyperlibObjectPreview;
        using Microsoft.AspNetCore.Components;
        using Microsoft.AspNetCore.Components.Web;
        using Microsoft.JSInterop;
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.Use(async (context, next) => {
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; script-src 'none'; frame-src 'none'; form-action 'none'; connect-src 'none'; base-uri 'self'";
            await next();
        });
        app.UseStaticFiles();
        app.MapStaticAssets();
        app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/preview-data/{id}/api/state", async (string id) => {
            var data = await ReadPreviewData(id);
            return data is null ? Results.NotFound() : Results.Json(SampleState(data), ContentJson.Options);
        });
        app.MapGet("/preview-data/{id}/api/renderer/page/{pageId}", async (string id, string pageId) => {
            var data = await ReadPreviewData(id);
            return data is not null && SampleState(data).Pages.TryGetValue(pageId, out var page)
                ? Results.Json(page, ContentJson.Options) : Results.NotFound();
        });
        app.MapGet("/preview/{id}", async (string id, HttpContext context) => {
            if (!Guid.TryParseExact(id, "N", out _)) return Results.NotFound();
            var file = Path.Combine(Environment.GetEnvironmentVariable("HYPERLIB_OBJECT_PREVIEW_CONTENT")!, id + ".json");
            if (!File.Exists(file)) return Results.NotFound();
            try {
                var raw = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(file), ContentJson.Options);
                var data = raw.Deserialize<PreviewData>(ContentJson.Options)!;
                var rawTemplate = raw.GetProperty("template");
                RestoreDefaults(rawTemplate, data.Template.Appearance, "appearance");
                foreach (var block in rawTemplate.GetProperty("blocks").EnumerateArray()) {
                    var schema = data.Template.Blocks.Single(item => item.Id == block.GetProperty("id").GetString());
                    RestoreDefaults(block, schema.Fields, "fields");
                }
                TemplateValidator.ValidateDefinition(data.Template);
                TemplateValidator.ValidatePage(data.Template, data.Page);
                var assembly = System.Reflection.Assembly.Load(Environment.GetEnvironmentVariable("HYPERLIB_OBJECT_PREVIEW_ASSEMBLY")!);
                var components = assembly.GetTypes().Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type)).ToDictionary(type => type.Name, StringComparer.Ordinal);
                var origin = $"http://127.0.0.1:{context.Connection.LocalPort}/";
                var options = new RendererOptions { PackageId = "preview-work", GenerationId = "preview", GatewayUrl = origin + "preview-data/" + id + "/", PathBase = "", Template = data.Template, Components = components };
                using var services = new ServiceCollection().AddLogging().AddSingleton(options)
                    .AddSingleton<RendererContentClient>()
                    .AddSingleton<NavigationManager>(new PreviewNavigation(origin)).AddSingleton<IJSRuntime, PreviewJs>()
                    .AddSingleton<IErrorBoundaryLogger, PreviewErrors>().BuildServiceProvider();
                await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
                var body = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ObjectPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Data"] = data }))).ToHtmlString());
                return Results.Content("<!doctype html><html lang='en'><head><base href='/'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>" + WebUtility.HtmlEncode(data.Title) + "</title><link rel='stylesheet' href='/_content/HyperLib.Runtime/renderer.css'><link rel='stylesheet' href='/Host.styles.css'></head><body>" + body + "</body></html>", "text/html");
            } catch (Exception error) { return Results.Content("<p data-preview-error>" + WebUtility.HtmlEncode(error.Message) + "</p>", "text/html", statusCode: 422); }
        });
        app.Run();
        static async Task<PreviewData?> ReadPreviewData(string id) {
            if (!Guid.TryParseExact(id, "N", out _)) return null;
            var file = Path.Combine(Environment.GetEnvironmentVariable("HYPERLIB_OBJECT_PREVIEW_CONTENT")!, id + ".json");
            return File.Exists(file) ? JsonSerializer.Deserialize<PreviewData>(await File.ReadAllTextAsync(file), ContentJson.Options) : null;
        }
        static LibraryState SampleState(PreviewData data) {
            var state = new LibraryState { LibraryId = "object-preview-library" };
            state.Nodes.Add(new() { Id = "preview-collection", Type = "collection", ParentId = state.LibraryId, Title = "Sample collection" });
            state.Nodes.Add(new() { Id = "preview-work", Type = "work", ParentId = "preview-collection", WorkId = "preview-work",
                Title = "Sample Work", ProjectPath = "Works/preview-work",
                Appearance = new() { ["workLandingPage"] = ContentJson.Value(data.Page.Id) } });
            var next = ContentJson.Clone(data.Page); next.Id = "preview-next"; next.Title = "Another Page";
            foreach (var page in new[] { data.Page, next }) {
                state.Nodes.Add(new() { Id = page.Id, Type = "page", ParentId = "preview-work", WorkId = "preview-work", Title = page.Title,
                    Order = state.Pages.Count });
                state.Pages[page.Id] = page;
            }
            return state;
        }
        static void RestoreDefaults(JsonElement parent, List<FieldDefinition> fields, string property) {
            if (!parent.TryGetProperty(property, out var rawFields)) return;
            foreach (var rawField in rawFields.EnumerateArray())
                if (rawField.TryGetProperty("defaultValue", out var value))
                    fields.Single(field => field.Id == rawField.GetProperty("id").GetString()).DefaultValue = value.Clone();
        }
        public sealed record PreviewData(TemplateDefinition Template, PageDocument Page, string Title, bool SingleObject);
        public sealed class PreviewNavigation : NavigationManager { public PreviewNavigation(string origin) => Initialize(origin, origin); protected override void NavigateToCore(string uri, bool forceLoad) { } }
        public sealed class PreviewJs : IJSRuntime {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException("Object previews render without scripting.");
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        }
        public sealed class PreviewErrors : IErrorBoundaryLogger { public ValueTask LogErrorAsync(Exception error) => throw new InvalidOperationException("The component could not render: " + error.Message, error); }
        """;

    private const string HostPage = """
        @namespace HyperlibObjectPreview
        @using Microsoft.AspNetCore.Components
        @using HyperLib.Runtime
        @using HyperLib.Core
        @inject RendererOptions Options
        <main class="hl-document @Options.VariantClass(null, null)" style="@Options.AppearanceStyle(null, null)" data-object-preview>
            @if (Data.SingleObject) {
                <header class="hl-catalog-context" style="margin:0 0 18px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:var(--hl-muted)">Sample page</header>
            }
            @foreach (var region in Data.Template.Regions) {
                <section class="hl-region" data-region="@region.Id" aria-label="@region.Title">
                    @foreach (var block in Data.Page.Blocks.Where(block => block.Region == region.Id)) {
                        var effective = Options.WithFieldDefaults(block);
                        <section class="hl-block @Options.BlockTypeClass(block) @RendererOptions.BlockSpanClass(effective)" style="@RendererOptions.BlockStyle(effective)" data-block-type="@block.Type">
                            <DynamicComponent Type="Options.Resolve(block.Type)" Parameters="@(new Dictionary<string, object> { ["Context"] = new BlockContext { Id = block.Id, Values = effective.Values } })" />
                        </section>
                    }
                </section>
            }
            @if (Data.SingleObject) {
                <footer class="hl-sources"><p>Continue the story here. Objects share the colors and typography of the Work that contains them.</p></footer>
            }
        </main>
        @code { [Parameter, EditorRequired] public PreviewData Data { get; set; } = null!; }
        """;
}
