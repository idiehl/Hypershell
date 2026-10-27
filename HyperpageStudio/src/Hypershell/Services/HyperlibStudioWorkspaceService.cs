using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

public sealed record HyperlibStudioContext(string ProjectRoot, string? LibraryRoot, string? LibraryId,
    string? WorkId, string Title, string AuthoringRelativePath, bool IsLibraryRoot,
    bool HasLibraryContent, string? ContentUnavailableReason);
public sealed record HyperlibStudioNode(string Id, string Title, string Type, string? ParentId,
    string? WorkId, string? ProjectRoot, string Breadcrumb);
public sealed record HyperlibStudioSource(string Title, string RelativePath);
public sealed record HyperlibStudioSourceGroup(string Title, IReadOnlyList<HyperlibStudioSource> Items);
public sealed record HyperlibStudioSnapshot(HyperlibStudioContext Context, LibraryState? State,
    IReadOnlyList<HyperlibStudioNode> Nodes, IReadOnlyList<HyperlibStudioSourceGroup> SourceGroups);
public sealed record HyperlibStudioPreview(Uri Url, Uri Endpoint, Uri ContentUrl, string PackageId,
    string GenerationId, string? PageId, string Status, string? Diagnostics)
{
    public long ContentRevision { get; init; }
}

/// <summary>Reads project context without starting a host; all content writes go through the library gateway.</summary>
public sealed class HyperlibStudioWorkspaceService : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private const int MaxJsonBytes = 32 * 1024 * 1024;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Works", "BaseTemplates", "Starters", "Content", "Library", "Media", "bin", "obj", "node_modules",
        ".git", ".hyperlib", ".visual-page", ".hypershell", ".vs", "app", "artifacts", "output", "dist"
    };

    public HyperlibStudioWorkspaceService(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task<HyperlibStudioSnapshot> ReadAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(File.Exists(projectRoot) ? Path.GetDirectoryName(projectRoot)! : projectRoot);
        var manifestPath = Resolve(root, VisualPageProjectService.ProjectManifestFileName);
        using var manifest = await ReadDocumentAsync(manifestPath, cancellationToken);
        var data = manifest.RootElement;
        var packageId = String(data, "hyperlibPackageId");
        var authoring = String(data, "hyperlibAuthoringPath");
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(authoring))
            throw new InvalidOperationException("This project does not declare a HyperLib template.");
        TemplateValidator.ValidateId(packageId, "package");
        _ = Resolve(root, authoring);
        var title = String(data, "name") ?? new DirectoryInfo(root).Name;
        var context = new HyperlibStudioContext(root, null, null, packageId == "library" ? null : packageId,
            title, authoring, false, false,
            "This independent project can edit its template and source. Open it inside its original HyperLib library to edit content.");
        LibraryState? state = null;
        var directory = new DirectoryInfo(root);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            VisualPageProjectService.RejectReparsePoint(directory.FullName);
            var statePath = Resolve(directory.FullName, "Content/library.json");
            if (!File.Exists(statePath)) continue;
            using var document = await ReadDocumentAsync(statePath, cancellationToken);
            var candidate = document.Deserialize<LibraryState>(ContentJson.Options)
                ?? throw new InvalidDataException("The library content index is empty.");
            ContentRules.Validate(candidate);
            var isRoot = SamePath(root, directory.FullName);
            var work = candidate.Nodes.FirstOrDefault(node => node.Type == "work"
                && node.Id == packageId && SamePath(Resolve(directory.FullName, node.ProjectPath!), root));
            if (!isRoot && work is null) continue;
            if (isRoot && packageId != "library")
                throw new InvalidDataException("The library project must identify the library presentation package.");
            // A template or arbitrary nested project never inherits another Work's content authority.
            state = candidate;
            context = context with { LibraryRoot = NormalizeRoot(directory.FullName), LibraryId = candidate.LibraryId,
                WorkId = work?.Id, Title = work?.Title ?? title, IsLibraryRoot = isRoot,
                HasLibraryContent = true, ContentUnavailableReason = null };
            break;
        }
        return new(context, state, BuildNodes(context, state), DiscoverSources(context));
    }

    public IReadOnlyList<HyperlibStudioNode> BuildNodes(HyperlibStudioContext context, LibraryState? state)
    {
        if (state is null || !context.HasLibraryContent) return [];
        ContentRules.Validate(state);
        if (state.LibraryId != context.LibraryId) throw new InvalidDataException("The library identity changed.");
        var map = state.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        return state.Nodes.Where(node => context.IsLibraryRoot || node.WorkId == context.WorkId)
            .OrderBy(node => node.Order).ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .Select(node => new HyperlibStudioNode(node.Id, node.Title, node.Type, node.ParentId, node.WorkId,
                node.WorkId is { } id ? Resolve(context.LibraryRoot!, "Works/" + id) : null, Breadcrumb(node))).ToArray();

        string Breadcrumb(LibraryNode node)
        {
            var parts = new List<string> { node.Title };
            var current = node;
            while (current.ParentId is { } parent && map.TryGetValue(parent, out current!)) parts.Add(current.Title);
            parts.Add("Library");
            parts.Reverse();
            return string.Join(" / ", parts);
        }
    }

    public async Task<Uri> ConnectAsync(HyperlibStudioContext context,
        Func<string, CancellationToken, Task<Uri>> endpointFactory, CancellationToken cancellationToken = default)
    {
        RequireContent(context);
        var endpoint = await endpointFactory(context.LibraryRoot!, cancellationToken);
        await VerifyEndpointAsync(context, endpoint, cancellationToken);
        return endpoint;
    }

    public async Task VerifyEndpointAsync(HyperlibStudioContext context, Uri endpoint, CancellationToken cancellationToken = default,
        bool requireReusableTemplates = false)
    {
        RequireContent(context);
        ValidateEndpoint(endpoint);
        using var response = await _client.GetAsync(new Uri(endpoint, "api/health"), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        EnsureResponseOrigin(response, endpoint);
        if (!response.Headers.TryGetValues("X-HyperLib-App", out var markers) || !markers.Contains("hyperlib", StringComparer.Ordinal))
            throw new InvalidOperationException("The local server is not a HyperLib gateway.");
        using var health = await ReadResponseAsync(response, cancellationToken);
        if (String(health.RootElement, "app") != "hyperlib"
            || String(health.RootElement, "root") is not { } root || !SamePath(root, context.LibraryRoot!)
            || String(health.RootElement, "origin") != endpoint.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("The HyperLib gateway belongs to a different library.");
        if (requireReusableTemplates && (!health.RootElement.TryGetProperty("templateCatalogVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var supportedVersion) || supportedVersion < 1))
            throw new InvalidOperationException("HyperLib is still running an older version. Restart HyperLib in MyNet, then try creating this Work again. Your template is saved.");
    }

    public async Task<LibraryState> ApplyAsync(HyperlibStudioContext context, Uri endpoint, OperationBatch batch,
        CancellationToken cancellationToken = default)
    {
        await ValidateOperationScopeAsync(context, batch, cancellationToken);
        var requiresTemplates = batch.Operations.Any(operation => operation.Kind == "createNode"
            && operation.NodeType == "work" && operation.StarterId is { } starterId && !BaseTemplateCatalog.Contains(starterId));
        await VerifyEndpointAsync(context, endpoint, cancellationToken, requiresTemplates);
        return await PostStateAsync(context, endpoint, "api/operations", batch, cancellationToken);
    }

    private static async Task ValidateOperationScopeAsync(HyperlibStudioContext context, OperationBatch batch,
        CancellationToken cancellationToken)
    {
        RequireContent(context);
        if (context.IsLibraryRoot) return;
        using var document = await ReadDocumentAsync(Resolve(context.LibraryRoot!, "Content/library.json"), cancellationToken);
        var state = document.Deserialize<LibraryState>(ContentJson.Options) ?? throw new InvalidDataException("The library index is empty.");
        ContentRules.Validate(state);
        if (state.LibraryId != context.LibraryId) throw new InvalidDataException("The library identity changed.");
        var owned = state.Nodes.Where(node => node.WorkId == context.WorkId).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var workParents = state.Nodes.Where(node => node.Type is "collection" or "series").Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var operation in batch.Operations)
        {
            if (operation.Kind == "createNode")
            {
                var allowedParent = operation.NodeType == "work" ? workParents.Contains(operation.ParentId ?? "") : owned.Contains(operation.ParentId ?? "");
                if (!allowedParent) throw new InvalidOperationException("Create content inside this Work, or choose a library collection for a new Work.");
                if (operation.Id is not null) owned.Add(operation.Id);
            }
            else if (operation.Kind is "updateNode" or "moveNode" or "deleteNode" or "savePage" or "setAppearance")
            {
                var id = operation.Kind == "savePage" ? operation.Page?.Id : operation.Id;
                if (!owned.Contains(id ?? "") || operation.Kind == "moveNode" && !owned.Contains(operation.ParentId ?? ""))
                    throw new InvalidOperationException("This change targets content outside the current Work.");
            }
            else throw new InvalidOperationException("This operation is only available from the library project.");
        }
    }

    public async Task<LibraryState> UndoAsync(HyperlibStudioContext context, Uri endpoint, long revision,
        CancellationToken cancellationToken = default)
    {
        await VerifyEndpointAsync(context, endpoint, cancellationToken);
        return await PostStateAsync(context, endpoint, "api/undo", new { expectedRevision = revision }, cancellationToken);
    }

    public async Task<LibraryState> RedoAsync(HyperlibStudioContext context, Uri endpoint, long revision,
        CancellationToken cancellationToken = default)
    {
        await VerifyEndpointAsync(context, endpoint, cancellationToken);
        return await PostStateAsync(context, endpoint, "api/redo", new { expectedRevision = revision }, cancellationToken);
    }

    public async Task<Uri> GetContentUrlAsync(HyperlibStudioContext context, LibraryState state, Uri endpoint,
        string? nodeId = null, CancellationToken cancellationToken = default)
    {
        await VerifyEndpointAsync(context, endpoint, cancellationToken);
        var id = nodeId ?? context.WorkId ?? "library";
        if (id != "library") RequireNode(context, state, id);
        return new Uri(endpoint, "#" + Uri.EscapeDataString(id));
    }

    public async Task<HyperlibStudioPreview> PreviewAsync(HyperlibStudioContext context, LibraryState state, Uri endpoint,
        string? pageId = null, CancellationToken cancellationToken = default)
    {
        await VerifyEndpointAsync(context, endpoint, cancellationToken);
        // Preview can display newer content without silently advancing an editor's mutation revision.
        using (var stateResponse = await _client.GetAsync(new Uri(endpoint, "api/state"), cancellationToken))
        {
            await EnsureSuccessAsync(stateResponse, cancellationToken);
            EnsureResponseOrigin(stateResponse, endpoint);
            using var current = await ReadResponseAsync(stateResponse, cancellationToken);
            state = current.Deserialize<LibraryState>(ContentJson.Options) ?? throw new InvalidDataException("The library response was empty.");
            ContentRules.Validate(state);
            if (state.LibraryId != context.LibraryId) throw new InvalidDataException("The gateway returned another library.");
        }
        var page = pageId is null ? null : RequireNode(context, state, pageId);
        if (page is not null && page.Type != "page") throw new InvalidOperationException("Choose a content page to preview.");
        var package = page?.WorkId ?? context.WorkId ?? "library";
        using var response = await _client.GetAsync(new Uri(endpoint, "api/runtime/" + Uri.EscapeDataString(package)), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        EnsureResponseOrigin(response, endpoint);
        using var generation = await ReadResponseAsync(response, cancellationToken);
        var generationId = String(generation.RootElement, "generationId") ?? "";
        TemplateValidator.ValidateId(generationId, "runtime generation");
        var expectedPrefix = $"/_runtime/{Uri.EscapeDataString(package)}/{Uri.EscapeDataString(generationId)}/";
        if (String(generation.RootElement, "packageId") != package || String(generation.RootElement, "proxyPrefix") != expectedPrefix)
            throw new InvalidDataException("The renderer returned an unexpected project address.");
        var route = page is null ? "browse/" + Uri.EscapeDataString(context.WorkId ?? state.LibraryId) : "page/" + Uri.EscapeDataString(page.Id);
        var status = "active";
        string? diagnostics = null;
        // A renderer can return its last healthy generation while a new build reports errors.
        using var statusResponse = await _client.GetAsync(new Uri(endpoint, "api/runtime/status"), cancellationToken);
        await EnsureSuccessAsync(statusResponse, cancellationToken);
        EnsureResponseOrigin(statusResponse, endpoint);
        using var statuses = await ReadResponseAsync(statusResponse, cancellationToken);
        foreach (var item in statuses.RootElement.EnumerateArray())
            if (String(item, "packageId") == package) { status = String(item, "state") ?? status; diagnostics = String(item, "error"); break; }
        return new(new Uri(endpoint, expectedPrefix + route + "?edit=0&revision=" + state.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), endpoint,
            new Uri(endpoint, "#" + Uri.EscapeDataString(page?.Id ?? context.WorkId ?? "library")),
            package, generationId, page?.Id, status, diagnostics) { ContentRevision = state.Revision };
    }

    public async Task KeepPreviewAliveAsync(HyperlibStudioContext context, HyperlibStudioPreview preview,
        CancellationToken cancellationToken = default)
    {
        await VerifyEndpointAsync(context, preview.Endpoint, cancellationToken);
        if (!context.IsLibraryRoot && preview.PackageId != context.WorkId)
            throw new InvalidOperationException("The preview belongs to a different Work.");
        TemplateValidator.ValidateId(preview.PackageId, "package");
        using var request = WriteRequest(preview.Endpoint, "api/runtime/" + Uri.EscapeDataString(preview.PackageId) + "/lease", new { });
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        EnsureResponseOrigin(response, preview.Endpoint);
    }

    private async Task<LibraryState> PostStateAsync<T>(HyperlibStudioContext context, Uri endpoint, string path,
        T body, CancellationToken cancellationToken)
    {
        using var request = WriteRequest(endpoint, path, body);
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        EnsureResponseOrigin(response, endpoint);
        using var document = await ReadResponseAsync(response, cancellationToken);
        var state = document.Deserialize<LibraryState>(ContentJson.Options) ?? throw new InvalidDataException("The library response was empty.");
        ContentRules.Validate(state);
        if (state.LibraryId != context.LibraryId) throw new InvalidDataException("The gateway returned another library.");
        return state;
    }

    private static HttpRequestMessage WriteRequest<T>(Uri endpoint, string path, T value)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, path))
        { Content = JsonContent.Create(value, options: ContentJson.Options) };
        request.Headers.Add("X-HyperLib-Client", "editor");
        request.Headers.Add("Origin", endpoint.GetLeftPart(UriPartial.Authority));
        return request;
    }

    private static LibraryNode RequireNode(HyperlibStudioContext context, LibraryState state, string id)
    {
        if (state.LibraryId != context.LibraryId) throw new InvalidOperationException("The library identity changed. Refresh this project.");
        return state.Nodes.FirstOrDefault(node => node.Id == id && (context.IsLibraryRoot || node.WorkId == context.WorkId))
            ?? throw new InvalidOperationException("The item is not part of this project's content.");
    }

    private static void RequireContent(HyperlibStudioContext context)
    {
        if (!context.HasLibraryContent || context.LibraryRoot is null)
            throw new InvalidOperationException(context.ContentUnavailableReason ?? "This project has no library content context.");
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1"
            || endpoint.Port < 1 || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/"
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("HyperLib requires an exact local loopback gateway address.");
    }

    private static void EnsureResponseOrigin(HttpResponseMessage response, Uri endpoint)
    {
        if (response.RequestMessage?.RequestUri is { } actual && actual.GetLeftPart(UriPartial.Authority) != endpoint.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("HyperLib gateway redirects are not supported.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = $"HyperLib request failed ({(int)response.StatusCode}).";
        try
        {
            using var error = await ReadResponseAsync(response, cancellationToken);
            message = String(error.RootElement, "error") ?? message;
        }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.Conflict)
            message = "Content changed in another editor. Refresh before trying again. " + message;
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(MaxJsonBytes, cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static async Task<JsonDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        // Keep the old file handle readable while another editor atomically replaces the index.
        // A Studio refresh must never prevent the gateway from committing a content revision.
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length > MaxJsonBytes) throw new InvalidDataException("The project content index is too large.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken);
        return JsonDocument.Parse(bytes);
    }

    private static IReadOnlyList<HyperlibStudioSourceGroup> DiscoverSources(HyperlibStudioContext context)
    {
        var groups = new Dictionary<string, List<HyperlibStudioSource>>
        { ["Design"] = [], ["Elements"] = [], ["Styles"] = [], ["Behavior"] = [], ["Project"] = [] };
        Add(context.AuthoringRelativePath, "Design", "Element schema");
        foreach (var relative in EnumerateSources(context.ProjectRoot, context.ProjectRoot, 0, context.IsLibraryRoot).Take(2000))
        {
            if (relative.Equals(context.AuthoringRelativePath, StringComparison.OrdinalIgnoreCase)) continue;
            // Shared SDK implementation is not source belonging to the current presentation project.
            if (relative.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
                && !relative.StartsWith("src/Pages/", StringComparison.OrdinalIgnoreCase)
                && !relative.StartsWith("src/Host/", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
                && !relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith("visual-page-theme.css", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".visual-page.json", StringComparison.OrdinalIgnoreCase)) continue;
            var ext = Path.GetExtension(relative).ToLowerInvariant();
            var group = ext switch
            {
                ".css" or ".scss" or ".sass" or ".less" => "Styles",
                ".js" or ".ts" or ".cs" => "Behavior",
                ".razor" or ".cshtml" or ".html" => relative.Contains("/Blocks/") || relative.Contains("/Components/") ? "Elements" : "Design",
                ".json" or ".csproj" or ".props" or ".targets" or ".md" => "Project",
                _ => null
            };
            if (group is not null) Add(relative, group, Path.GetFileName(relative));
        }
        return groups.Select(group => new HyperlibStudioSourceGroup(group.Key,
            group.Value.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();

        void Add(string relative, string group, string title)
        {
            relative = relative.Replace('\\', '/');
            if (File.Exists(Resolve(context.ProjectRoot, relative))) groups[group].Add(new(title, relative));
        }
    }

    private static IEnumerable<string> EnumerateSources(string root, string directory, int depth, bool isLibraryRoot)
    {
        if (depth > 12) yield break;
        foreach (var file in Directory.EnumerateFiles(directory))
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedDirectories.Contains(Path.GetFileName(child)) || Path.GetFileName(child).StartsWith('.')) continue;
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
            // The root catalog owns independent projects. Ordinary Templates folders remain editable source.
            if (isLibraryRoot && Path.GetRelativePath(root, directory).Equals("Templates", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(child, "template.json"))) continue;
            if (Path.GetRelativePath(root, directory).Equals("src", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(child) is not ("Pages" or "Host")) continue;
            foreach (var relative in EnumerateSources(root, child, depth + 1, isLibraryRoot)) yield return relative;
        }
    }

    private static string Resolve(string root, string relative) => VisualPageProjectService.ResolveProjectPath(root, relative);
    private static string NormalizeRoot(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        VisualPageProjectService.RejectReparsePoint(root);
        for (var ancestor = Directory.GetParent(root); ancestor is not null; ancestor = ancestor.Parent)
            VisualPageProjectService.RejectReparsePoint(ancestor.FullName);
        return root;
    }
    private static bool SamePath(string left, string right) => string.Equals(NormalizeRoot(left), NormalizeRoot(right), StringComparison.OrdinalIgnoreCase);
    private static string? String(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
