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

public sealed record HyperlibGuidedPagePreviewResult(Uri Url, string Message, bool Interactive = false, string? RequestId = null);

/// <summary>Composes real imported template components in an isolated cached Page renderer. Recipe and style changes never rebuild component source.</summary>
public sealed partial class HyperlibGuidedPagePreviewService : IDisposable
{
    private const string Version = "guided-page-preview-2";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuildGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".hyperlib", "node_modules", "output", "artifacts" };
    private readonly string _cacheRoot;
    private readonly string _sessionRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _backgroundMediaGate = new(1, 1);
    private readonly Dictionary<string, string> _backgroundMediaCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _headerMediaGate = new(1, 1);
    private readonly Dictionary<string, string> _headerMediaCache = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly object _processLock = new();
    private Process? _host;
    private WindowsProcessLifetimeJob? _job;
    private string? _activeFingerprint;
    private Uri? _origin;
    private bool _disposed;

    public HyperlibGuidedPagePreviewService(string? cacheRoot = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "Hyperlib", "guided-page-preview"));
        _sessionRoot = Path.Combine(_cacheRoot, "sessions", Guid.NewGuid().ToString("N"));
    }

    private readonly Dictionary<string, PreparedPage> _prepared = new(StringComparer.Ordinal);
    internal int? OwnedProcessId { get { lock (_processLock) return _host is { HasExited: false } process ? process.Id : null; } }

    public async Task<HyperlibGuidedPagePreviewResult> PreviewAsync(string libraryRoot, HyperlibWorkProjectDefinition definition,
        HyperlibPageTemplateDefinition recipe, string title, CancellationToken cancellationToken = default,
        string? ownedProjectRoot = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            // Selection preparation is shared for all style edits. SDK compilation is keyed by actual
            // imported source bytes, so adding another object from the same module reuses the build.
            var preparation = await PrepareAsync(libraryRoot, definition, ownedProjectRoot, ct);
            var previewRecipe = ContentJson.Clone(recipe);
            var mediaRoot = ownedProjectRoot is null ? libraryRoot : Path.GetFullPath(Path.Combine(ownedProjectRoot, "../.."));
            await PrepareLocalLogosAsync(mediaRoot, previewRecipe, ct);
            await PrepareLocalBackgroundAsync(mediaRoot, previewRecipe.BackgroundSurface, ct);
            await PrepareLocalHeaderAsync(mediaRoot, previewRecipe.HeaderLayout, ct);
            await PrepareTemplateValuesMediaAsync(mediaRoot, previewRecipe.HeaderLayout, preparation.Toolkit, ct);
            await PrepareLocalHeaderAsync(mediaRoot, previewRecipe.NavigationLayout, ct);
            await PrepareTemplateValuesMediaAsync(mediaRoot, previewRecipe.NavigationLayout, preparation.Toolkit, ct);
            await PrepareLocalHeaderAsync(mediaRoot, previewRecipe.BodyLayout, ct);
            await PrepareTemplateValuesMediaAsync(mediaRoot, previewRecipe.BodyLayout, preparation.Toolkit, ct);
            await PrepareLocalHeaderAsync(mediaRoot, previewRecipe.FooterLayout, ct);
            await PrepareTemplateValuesMediaAsync(mediaRoot, previewRecipe.FooterLayout, preparation.Toolkit, ct);
            var template = ContentJson.Clone(preparation.Template);
            HyperlibHeaderLayouts.EnsureBlockDefinition(template);
            HyperlibHeaderLayouts.EnsureBlockDefinition(template, HyperlibHeaderLayouts.BodyBlockType, "Body block");
            HyperlibHeaderLayouts.EnsureBlockDefinition(template, HyperlibHeaderLayouts.FooterBlockType, "Footer block");
            HyperlibNavigationLayouts.EnsureBlockDefinitions(template);
            var composed = HyperlibProjectPipelineService.Compose(previewRecipe, preparation.Toolkit, template, "preview");
            var page = new PageDocument { Id = "guided-preview", WorkId = "preview-work", TemplateId = template.TemplateId,
                Title = title, UsesEditableChrome = true, Blocks = composed.Blocks };
            var node = new LibraryNode { Id = page.Id, Type = "page", Title = title, Appearance = composed.Appearance };
            var payload = new PreviewPayload(template, page, node, title);
            var snapshot = preparation.Snapshot;
            var fingerprint = preparation.Fingerprint;
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
            var interactive = HasInteractiveElements(previewRecipe);
            var address = new Uri(_origin!, (interactive ? "interactive/" : "preview/") + requestId);
            using var response = await _http.GetAsync(address, ct);
            var html = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode || html.Contains("data-preview-error", StringComparison.Ordinal))
                throw new InvalidOperationException("The Page could not render. " + (html.Length > 1800 ? html[..1800] : html));
            return new(address, "Live template preview · Empty slots receive content in Authoring.", interactive, requestId);
        }
        finally { _gate.Release(); }
    }

    public static bool HasTemplateElements(HyperlibHeaderLayoutDefinition layout)
        => layout.Enabled && HyperlibHeaderLayouts.Elements(layout).Any(element => element.Kind is "template" or "navigation");
    public static bool HasInteractiveElements(HyperlibPageTemplateDefinition recipe)
        => !string.IsNullOrWhiteSpace(recipe.CompleteTemplateObjectId)||HasTemplateElements(recipe.HeaderLayout)||recipe.NavigationLayout.Enabled||HasTemplateElements(recipe.BodyLayout)||HasTemplateElements(recipe.FooterLayout);

    /// <summary>Returns the identical full-render background CSS without preparing or compiling component source.</summary>
    public async Task<string> PreviewBackgroundCssAsync(string libraryRoot, string baseColor, HyperlibPageBackgroundDefinition surface,
        string? ownedProjectRoot = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var draft = ContentJson.Clone(surface);
        var mediaRoot = ownedProjectRoot is null ? libraryRoot : Path.GetFullPath(Path.Combine(ownedProjectRoot, "../.."));
        await PrepareLocalBackgroundAsync(mediaRoot, draft, linked.Token);
        return HyperlibBackgroundDesigns.SurfaceCss(baseColor, draft);
    }

    /// <summary>Uses the generated Work's exact shared renderer, without compiling source or navigating the current Page.</summary>
    public async Task<string> PreviewHeaderHtmlAsync(string libraryRoot, HyperlibHeaderLayoutDefinition layout,
        string? ownedProjectRoot = null, CancellationToken cancellationToken = default, string region = "header")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var draft = ContentJson.Clone(layout);
        if (HasTemplateElements(draft)) throw new InvalidOperationException("Library template objects use the interactive Page preview.");
        var mediaRoot = ownedProjectRoot is null ? libraryRoot : Path.GetFullPath(Path.Combine(ownedProjectRoot, "../.."));
        await PrepareLocalHeaderAsync(mediaRoot, draft, linked.Token);
        return HyperlibHeaderLayouts.RenderHtml(draft, region, designPreview: true);
    }

    /// <summary>Updates only owned header rows, preserving the document, other regions, and unchanged video elements.</summary>
    public static string HeaderUpdateScript(string html, string region = "header") => "(() => { const html=" + JsonSerializer.Serialize(html)
        + ";const region=" + JsonSerializer.Serialize(region == "body" ? "main" : region == "footer" ? "footer" : "header")
        + ";const blockType=" + JsonSerializer.Serialize(region == "body" ? "studio-body-row" : region == "footer" ? "studio-footer-row" : "studio-header-row") + ";" + """
        const header=document.querySelector('[data-guided-page-preview] > [data-region="'+region+'"]');
        if(!header)return false;
        const template=document.createElement('template');template.innerHTML=html;
        const rows=[...template.content.children];
        function sync(target,source){
          if(target.nodeType!==source.nodeType||target.nodeName!==source.nodeName){target.replaceWith(source.cloneNode(true));return;}
          if(target.nodeType===3){if(target.nodeValue!==source.nodeValue)target.nodeValue=source.nodeValue;return;}
          for(const attr of [...target.attributes])if(!source.hasAttribute(attr.name))target.removeAttribute(attr.name);
          for(const attr of source.attributes)if(target.getAttribute(attr.name)!==attr.value)target.setAttribute(attr.name,attr.value);
          const keyed=new Map([...target.children].filter(n=>n.hasAttribute('data-header-element')).map(n=>[n.getAttribute('data-header-element'),n]));
          const wanted=[...source.childNodes];
          for(let i=0;i<wanted.length;i++){
            const fresh=wanted[i],key=fresh.nodeType===1?fresh.getAttribute('data-header-element'):null;
            let current=key?keyed.get(key):target.childNodes[i];
            if(!current){current=fresh.cloneNode(true);target.append(current);}
            else{if(current!==target.childNodes[i])target.insertBefore(current,target.childNodes[i]||null);sync(current,fresh);}
          }
          while(target.childNodes.length>wanted.length)target.lastChild.remove();
        }
        const old=[...header.querySelectorAll(':scope > [data-block-type="'+blockType+'"]')];
        const kept=new Set();
        let anchor=header.querySelector(':scope > [data-block-type]');
        for(const row of rows){
          const id=row.getAttribute('data-header-row');
          let wrapper=old.find(n=>n.firstElementChild?.getAttribute('data-header-row')===id);
          if(!wrapper){wrapper=document.createElement('section');wrapper.className='hl-block';wrapper.dataset.blockType=blockType;wrapper.dataset.blockId=id;wrapper.append(row.cloneNode(true));}
          else sync(wrapper.firstElementChild,row);
          if(wrapper!==anchor)header.insertBefore(wrapper,anchor);
          anchor=wrapper.nextSibling;kept.add(wrapper);
        }
        for(const wrapper of old)if(!kept.has(wrapper))wrapper.remove();
        for(const empty of header.querySelectorAll(':scope > .studio-empty-region'))empty.hidden=rows.length>0;
        return true;
        })()
        """;

    private async Task PrepareLocalHeaderAsync(string libraryRoot, HyperlibHeaderLayoutDefinition layout, CancellationToken ct)
    {
        HyperlibHeaderLayouts.Validate(layout, allowLocalMedia: true);
        if (!layout.Enabled) return;
        async Task VisitAsync(IEnumerable<HyperlibHeaderElementDefinition> elements)
        {
            foreach (var element in elements)
            {
                ct.ThrowIfCancellationRequested();
                if (element.Kind is "image" or "video" && !string.IsNullOrWhiteSpace(element.MediaPath))
                    element.MediaPath = await PrepareHeaderMediaAsync(libraryRoot, element.MediaPath, element.Kind == "video", ct);
                await VisitAsync(element.Children);
            }
        }
        foreach (var row in layout.Blocks) await VisitAsync(row.Elements);
    }

    private async Task PrepareTemplateValuesMediaAsync(string libraryRoot, HyperlibHeaderLayoutDefinition layout,
        IReadOnlyList<HyperlibProjectToolkitItem> toolkit, CancellationToken ct)
    {
        foreach (var element in HyperlibHeaderLayouts.Elements(layout).Where(item => item.Kind == "template"))
        {
            var choice = toolkit.SingleOrDefault(item => item.ChoiceId == element.TemplateObjectId)
                ?? throw new InvalidDataException("Add this header's library object to its project toolset.");
            foreach (var (sourceId, values) in element.TemplateValues)
            {
                if (!choice.SourceBlockDefinitions.TryGetValue(sourceId, out var schema)) continue;
                foreach (var field in schema.Fields.Where(field => field.Type is "media" or "image" or "video" or "audio"))
                {
                    if (!values.TryGetValue(field.Id, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) continue;
                    var path = value.GetString()!;
                    if (!Path.IsPathFullyQualified(path) && !path.StartsWith("/media/", StringComparison.Ordinal)) continue;
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    var video = extension is ".mp4" or ".webm" or ".ogv";
                    var audio = extension is ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" or ".aac";
                    values[field.Id] = ContentJson.Value(await PrepareHeaderMediaAsync(libraryRoot, path, video, ct, audio));
                }
            }
        }
    }

    private async Task<string> PrepareHeaderMediaAsync(string libraryRoot, string value, bool video, CancellationToken ct, bool audio = false)
    {
        string? source = null;
        if (Path.IsPathFullyQualified(value) && !value.StartsWith("/", StringComparison.Ordinal)) source = value;
        else if (value.StartsWith("/media/", StringComparison.Ordinal)) source = Safe(Path.Combine(libraryRoot, "Media"), Uri.UnescapeDataString(value[7..]));
        if (source is null || !File.Exists(source)) throw new FileNotFoundException("The selected header media is unavailable. Choose it again.");
        VisualPageProjectService.RejectReparsePoint(source);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (audio ? extension is not (".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" or ".aac")
            : video ? extension is not (".mp4" or ".webm" or ".ogv") : extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".avif" or ".svg"))
            throw new InvalidDataException(video ? "Choose an MP4, WebM, or Ogg header video." : "Choose a PNG, JPEG, WebP, GIF, AVIF, or SVG header image.");
        var maximum = (video || audio ? 200L : 20L) * 1024 * 1024;
        await _headerMediaGate.WaitAsync(ct);
        try
        {
            var info = new FileInfo(source); info.Refresh();
            if (info.Length > maximum) throw new InvalidDataException(video ? "A header video must be smaller than 200 MiB." : "A header image must be smaller than 20 MiB.");
            var key = Path.GetFullPath(source).ToUpperInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            if (_headerMediaCache.TryGetValue(key, out var cached) && File.Exists(Safe(_sessionRoot, "media/" + cached))) return "/media/" + cached;
            var temporary = Safe(_sessionRoot, "media/" + Guid.NewGuid().ToString("N") + ".tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            try
            {
                await using var input = File.OpenRead(source);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = File.Create(temporary))
                {
                    var buffer = new byte[64 * 1024];
                    long length = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) != 0)
                    {
                        if ((length += read) > maximum) throw new InvalidDataException("The selected header media exceeds its size limit.");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }
                var name = Convert.ToHexStringLower(hash.GetHashAndReset()) + extension;
                var destination = Safe(_sessionRoot, "media/" + name);
                if (!File.Exists(destination)) File.Move(temporary, destination);
                if (_headerMediaCache.Count >= 128) _headerMediaCache.Remove(_headerMediaCache.Keys.First());
                _headerMediaCache[key] = name;
                return "/media/" + name;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _headerMediaGate.Release(); }
    }

    private async Task PrepareLocalBackgroundAsync(string libraryRoot, HyperlibPageBackgroundDefinition surface, CancellationToken ct)
    {
        if (surface is null) throw new InvalidDataException("The Page background settings are missing.");
        var value = surface.ImagePath;
        if (surface.DesignId != "image" || string.IsNullOrWhiteSpace(value)) return;
        string? source = null;
        if (Path.IsPathFullyQualified(value) && !value.StartsWith("/", StringComparison.Ordinal)) source = value;
        else if (value.StartsWith("/media/", StringComparison.Ordinal)) source = Safe(Path.Combine(libraryRoot, "Media"), Uri.UnescapeDataString(value[7..]));
        if (source is null || !File.Exists(source)) throw new FileNotFoundException("The selected background image is unavailable. Choose it again.");
        VisualPageProjectService.RejectReparsePoint(source);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".avif" or ".svg"))
            throw new InvalidDataException("Choose a PNG, JPEG, WebP, GIF, or AVIF background image.");
        if (new FileInfo(source).Length > 20L * 1024 * 1024) throw new InvalidDataException("A background image must be smaller than 20 MiB.");
        await _backgroundMediaGate.WaitAsync(ct);
        try
        {
            var info = new FileInfo(source); info.Refresh();
            if (info.Length > 20L * 1024 * 1024) throw new InvalidDataException("A background image must be smaller than 20 MiB.");
            var key = Path.GetFullPath(source).ToUpperInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            if (_backgroundMediaCache.TryGetValue(key, out var cachedName) && File.Exists(Safe(_sessionRoot, "media/" + cachedName)))
            { surface.ImagePath = "/media/" + cachedName; return; }
            var bytes = await File.ReadAllBytesAsync(source, ct);
            var name = Convert.ToHexStringLower(SHA256.HashData(bytes)) + extension;
            var destination = Safe(_sessionRoot, "media/" + name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllBytesAsync(temporary, bytes, ct); File.Move(temporary, destination, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (_backgroundMediaCache.Count >= 64) _backgroundMediaCache.Remove(_backgroundMediaCache.Keys.First());
            _backgroundMediaCache[key] = name;
            surface.ImagePath = "/media/" + name;
        }
        finally { _backgroundMediaGate.Release(); }
    }

    private async Task<PreparedPage> PrepareAsync(string libraryRoot, HyperlibWorkProjectDefinition draft,
        string? ownedProjectRoot, CancellationToken ct)
    {
        var source = Path.GetFullPath(libraryRoot);
        var ids = draft.SelectedToolsetIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (ownedProjectRoot is not null)
        {
            var ownedRoot = Path.GetFullPath(ownedProjectRoot);
            var ownedPipeline = await new HyperlibProjectPipelineService().ReadAsync(ownedRoot, ct);
            if (ownedPipeline is not null && ids.All(id => ownedPipeline.Toolkit.Any(item => item.ChoiceId == id)))
            {
                var ownedLibrary = Path.GetFullPath(Path.Combine(ownedRoot, "../.."));
                if (!File.Exists(Safe(ownedLibrary, ".studio/hyperlib-work.json")))
                    throw new InvalidDataException("The private project's identity is missing.");
                var schema = TemplateObjectLibrary.ReadDefinition(ownedRoot);
                var ownedIdentity = ownedRoot.ToUpperInvariant() + "\n" + ownedPipeline.Revision + "\n" + string.Join("\n", ids);
                var ownedKey = "owned-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ownedIdentity)));
                if (_prepared.TryGetValue(ownedKey, out var cached)) return cached;
                var ownedSnapshot = Capture(ownedLibrary, ownedRoot, schema);
                var preparedOwned = new PreparedPage(ownedLibrary, ownedRoot, schema,
                    ownedPipeline.Toolkit.Where(item => ids.Contains(item.ChoiceId, StringComparer.Ordinal)).ToList(),
                    ownedSnapshot, Fingerprint(ownedSnapshot));
                _prepared[ownedKey] = preparedOwned;
                return preparedOwned;
            }
        }
        var identity = source.ToUpperInvariant() + "\n" + string.Join("\n", ids);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        if (_prepared.TryGetValue(key, out var prepared)) return prepared;
        var drafts = Path.Combine(_sessionRoot, "drafts");
        Directory.CreateDirectory(drafts);
        // Generate only a disposable source composition. The source library and every user Work stay read-only.
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Page builder preview", SelectedToolsetIds = ids.ToList(),
            LandingTemplate = new() { IncludeNavigation = false }, PageTemplate = new() { IncludeNavigation = false } };
        var project = await new HyperlibWorkProjectService(source).CreateAsync(drafts, "page-" + key, definition, ct);
        var pipeline = await new HyperlibProjectPipelineService().ReadAsync(project, ct)
            ?? throw new InvalidDataException("The preview toolkit could not be prepared.");
        var privateRoot = Path.GetFullPath(Path.Combine(project, "../.."));
        var template = TemplateObjectLibrary.ReadDefinition(project);
        var snapshot = Capture(privateRoot, project, template);
        prepared = new(privateRoot, project, template, pipeline.Toolkit, snapshot, Fingerprint(snapshot));
        _prepared[key] = prepared;
        return prepared;
    }

    private async Task PrepareLocalLogosAsync(string libraryRoot, HyperlibPageTemplateDefinition recipe, CancellationToken ct)
    {
        foreach (var (_, style) in HyperlibProjectPipelineService.RegionStyles(recipe))
        {
            var value = style.LogoPath;
            if (string.IsNullOrWhiteSpace(value)) continue;
            string? source = null;
            if (Path.IsPathFullyQualified(value) && !value.StartsWith("/", StringComparison.Ordinal)) source = value;
            else if (value.StartsWith("/media/", StringComparison.Ordinal)) source = Safe(Path.Combine(libraryRoot, "Media"), Uri.UnescapeDataString(value[7..]));
            if (source is null || !File.Exists(source)) continue;
            VisualPageProjectService.RejectReparsePoint(source);
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg"))
                throw new InvalidDataException("Choose a PNG, JPEG, WebP, GIF, or SVG logo image.");
            if (new FileInfo(source).Length > 10 * 1024 * 1024) throw new InvalidDataException("A preview logo must be smaller than 10 MiB.");
            var bytes = await File.ReadAllBytesAsync(source, ct);
            var name = Convert.ToHexStringLower(SHA256.HashData(bytes)) + extension;
            var destination = Safe(_sessionRoot, "media/" + name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination)) await File.WriteAllBytesAsync(destination, bytes, ct);
            style.LogoPath = "/media/" + name;
        }
    }

    private sealed record PreparedPage(string LibraryRoot, string ProjectRoot, TemplateDefinition Template, List<HyperlibProjectToolkitItem> Toolkit, List<SnapshotFile> Snapshot, string Fingerprint);
    private sealed record PreviewPayload(TemplateDefinition Template, PageDocument Page, LibraryNode Node, string Title);
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
            var content = File.ReadAllBytes(path);
            if (relative.Replace('\\', '/') == "sdk/HyperLib.Runtime/wwwroot/renderer.js")
                content = Encoding.UTF8.GetBytes(ProtectSharedTemplateFields(Encoding.UTF8.GetString(content)));
            result.Add(new(relative.Replace('\\', '/'), content));
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
        // A saved project may predate the header builder. The isolated renderer uses today's same
        // portable renderer without changing that project's sources before an explicit foundation save.
        var assembly = typeof(HyperlibGuidedPagePreviewService).Assembly;
        foreach (var resource in new[] { "HyperlibWorkProjectDefinition", "HyperlibHeaderLayouts", "HyperlibNavigationLayouts" })
        {
            using var stream = assembly.GetManifestResourceStream("StudioPipeline." + resource + ".cs")
                ?? throw new InvalidOperationException("The header preview renderer source is missing: " + resource);
            using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            result.Add(new("host/StudioHeader/" + resource + ".cs", buffer.ToArray()));
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
        hash.AppendData(Encoding.UTF8.GetBytes(HostProject + HostProgram + HostPage + HostInteractiveApp + HostInteractiveRoutes
            + HostInteractivePage + HostInteractiveContext + HostInteractiveScript + HyperlibHeaderRenderingSource.ComponentSource
            + HyperlibNavigationRenderingSource.LayoutComponent + HyperlibNavigationRenderingSource.LinksComponent));
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
        await File.WriteAllTextAsync(Path.Combine(host, "PreviewApp.razor"), HostInteractiveApp, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "PreviewRoutes.razor"), HostInteractiveRoutes, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "InteractivePage.razor"), HostInteractivePage, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "PreviewContext.cs"), HostInteractiveContext, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "StudioHeaderRowBlock.razor"), "@namespace HyperlibGuidedPreview\n" + HyperlibHeaderRenderingSource.ComponentSource, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "StudioNavigationLayoutBlock.razor"), "@namespace HyperlibGuidedPreview\n" + HyperlibNavigationRenderingSource.LayoutComponent, ct);
        await File.WriteAllTextAsync(Path.Combine(host, "StudioNavigationLinksBlock.razor"), "@namespace HyperlibGuidedPreview\n" + HyperlibNavigationRenderingSource.LinksComponent, ct);
        Directory.CreateDirectory(Path.Combine(host, "wwwroot"));
        await File.WriteAllTextAsync(Path.Combine(host, "wwwroot", "guided-preview.js"), HostInteractiveScript, ct);
        // Immutable preview paths can exceed the legacy Windows process working-directory limit
        // used by the SDK's optional static-asset compression helper. Ordinary assets still publish.
        foreach (var project in Directory.EnumerateFiles(generation, "*.csproj", SearchOption.AllDirectories))
        {
            var xml = XDocument.Load(project);
            if (xml.Root is null) throw new InvalidDataException("A preview project file is empty.");
            foreach (var existing in xml.Descendants().Where(item => item.Name.LocalName == "CompressionEnabled").ToArray()) existing.Remove();
            var ns = xml.Root.Name.Namespace;
            xml.Root.Add(new XElement(ns + "PropertyGroup", new XElement(ns + "CompressionEnabled", "false")));
            xml.Save(project);
        }
        var dotnet = VisualPagePreviewBuildService.ResolveDotNetExecutable()
            ?? throw new InvalidOperationException("The .NET 10 SDK is required to preview template components.");
        var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest
        {
            ExecutablePath = dotnet, WorkingDirectory = generation,
            Arguments = ["publish", Path.Combine(host, "Host.csproj"), "-c", "Release", "-o", Safe(generation, "bin"), "--nologo", "-v:minimal"],
            EnvironmentVariables = new Dictionary<string, string?> { ["DOTNET_PROCESSOR_COUNT"] = "2" },
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
        start.Environment["HYPERLIB_GUIDED_PREVIEW_CONTENT"] = _sessionRoot;
        start.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
        var pagesProject = XDocument.Load(Safe(generation, "project/Pages/Pages.csproj"));
        var assemblyName = pagesProject.Descendants("AssemblyName").FirstOrDefault()?.Value ?? "Pages";
        if (!Regex.IsMatch(assemblyName, "^[A-Za-z0-9_.-]+$"))
            throw new ContentValidationException("The component assembly name must be explicit for preview.");
        start.Environment["HYPERLIB_GUIDED_PREVIEW_ASSEMBLY"] = assemblyName;
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

    /// <summary>A hidden wizard releases its renderer while retaining the imported-source cache.</summary>
    public void Suspend()
    {
        if (!_disposed) StopHost();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel(); StopHost(); _job?.Dispose(); _job = null;
        _http.Dispose();
    }

    private const string HostProject = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><RootNamespace>HyperlibGuidedPreview</RootNamespace><AssemblyName>Host</AssemblyName></PropertyGroup>
          <ItemGroup><ProjectReference Include="../project/Pages/Pages.csproj" /></ItemGroup>
        </Project>
        """;

    private const string HostProgram = """
        using System.Text.Json;
        using System.Net;
        using HyperLib.Core;
        using HyperLib.Runtime;
        using Hypershell.Services;
        using HyperlibGuidedPreview;
        using Microsoft.AspNetCore.Components;
        using Microsoft.AspNetCore.Components.Web;
        using Microsoft.JSInterop;
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddScoped<PreviewContext>();
        builder.Services.AddScoped(services => services.GetRequiredService<PreviewContext>().Options ?? throw new InvalidOperationException("The Page preview has not loaded its component schema."));
        builder.Services.AddScoped<RendererContentClient>();
        // Studio embeds this disposable loopback renderer from a different local
        // port. Use the explicit local-only CSP below instead of SAMEORIGIN.
        builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        var app = builder.Build();
        app.Use(async (context, next) => {
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; media-src 'self'; font-src 'self'; script-src 'self'; frame-src 'none'; form-action 'none'; connect-src 'self' ws://127.0.0.1:" + context.Connection.LocalPort + "; base-uri 'self'; frame-ancestors 'self' http://127.0.0.1:* http://localhost:*";
            await next();
        });
        app.UseRouting();
        app.UseAntiforgery();
        app.UseStaticFiles();
        app.MapStaticAssets();
        app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/media/{id}", (string id) => {
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-f0-9]{64}\\.(png|jpg|jpeg|webp|gif|avif|svg|mp4|webm|ogv|mp3|wav|ogg|m4a|flac|aac)$")) return Results.NotFound();
            var file = Path.Combine(Environment.GetEnvironmentVariable("HYPERLIB_GUIDED_PREVIEW_CONTENT")!, "media", id);
            if (!File.Exists(file)) return Results.NotFound();
            var mime = Path.GetExtension(id) switch { ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", ".avif" => "image/avif", ".svg" => "image/svg+xml", ".mp4" => "video/mp4", ".webm" => "video/webm", ".ogv" => "video/ogg", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".ogg" => "audio/ogg", ".m4a" => "audio/mp4", ".flac" => "audio/flac", ".aac" => "audio/aac", _ => "image/jpeg" };
            return Results.File(file, mime, enableRangeProcessing: true);
        });
        app.MapGet("/preview/{id}", async (string id, HttpContext context) => {
            if (!Guid.TryParseExact(id, "N", out _)) return Results.NotFound();
            var file = Path.Combine(Environment.GetEnvironmentVariable("HYPERLIB_GUIDED_PREVIEW_CONTENT")!, id + ".json");
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
                var assembly = System.Reflection.Assembly.Load(Environment.GetEnvironmentVariable("HYPERLIB_GUIDED_PREVIEW_ASSEMBLY")!);
                var components = assembly.GetTypes().Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type)).ToDictionary(type => type.Name, StringComparer.Ordinal);
                var origin = $"http://127.0.0.1:{context.Connection.LocalPort}/";
                var options = new RendererOptions { PackageId = "preview-work", GenerationId = "preview", GatewayUrl = origin, PathBase = "", Template = data.Template, Components = components };
                using var services = new ServiceCollection().AddLogging().AddSingleton(options)
                    .AddSingleton<NavigationManager>(new PreviewNavigation(origin)).AddSingleton<IJSRuntime, PreviewJs>()
                    .AddSingleton<IErrorBoundaryLogger, PreviewErrors>().BuildServiceProvider();
                await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
                var body = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ObjectPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Data"] = data }))).ToHtmlString());
                return Results.Content("<!doctype html><html lang='en'><head><base href='/'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>" + WebUtility.HtmlEncode(data.Title) + "</title><link rel='stylesheet' href='/_content/HyperLib.Runtime/renderer.css'><link rel='stylesheet' href='/_content/HyperLib.Work/work-theme.css'><link rel='stylesheet' href='/Host.styles.css'><style>html,body{margin:0;min-height:100%;background:transparent}.hl-document{box-sizing:border-box;min-height:100vh}.studio-staged-layout{padding:28px;background:var(--hl-paper)}.hl-region{min-width:0}.studio-staged-layout .hl-block{position:relative;min-height:44px}.studio-slot-placeholder{display:block;border:1px dashed color-mix(in srgb,var(--hl-ink) 22%,transparent);padding:12px;border-radius:6px;color:var(--hl-muted);font:12px system-ui;text-align:center}.studio-region-logo{width:auto!important;max-width:64px!important;max-height:64px!important;flex:0 0 auto;object-fit:contain}.studio-region-chrome{grid-column:1/-1;min-width:0;display:flex;align-items:center;gap:16px;justify-content:var(--studio-justify,flex-start)}.studio-region-chrome[data-logo-placement=right]{flex-direction:row-reverse}.studio-region-chrome[data-logo-placement=center]{flex-direction:column;justify-content:center;text-align:center}.studio-region-text{flex:1;min-width:0;overflow-wrap:anywhere;margin:0;white-space:pre-wrap;font:inherit}.studio-region-label{font:10px system-ui;letter-spacing:.08em;text-transform:uppercase;opacity:.5;margin:0 0 10px}.studio-empty-region{grid-column:1/-1;min-height:54px;border:1px dashed color-mix(in srgb,var(--hl-ink) 12%,transparent);border-radius:6px}" + HyperlibHeaderLayouts.Css + HyperlibNavigationLayouts.Css + ".studio-header-row:not(:has([data-header-element])){outline:1px dashed color-mix(in srgb,var(--hl-ink) 25%,transparent);outline-offset:-1px}.studio-header-row:not(:has([data-header-element])):after{content:'Header row - add text, a box, image, or video';position:absolute;inset:0;display:grid;place-items:center;pointer-events:none;font:12px system-ui;color:var(--hl-muted)}" + "</style></head><body>" + body + "<script src='/guided-preview.js'></script></body></html>", "text/html");
            } catch (Exception error) { return Results.Content("<p data-preview-error>" + WebUtility.HtmlEncode(error.Message) + "</p>", "text/html", statusCode: 422); }
        });
        app.MapGet("/session/{initialId}/api/state", async (string initialId) => Results.Json(PreviewDataLoader.State(await PreviewDataLoader.CurrentAsync(initialId)), ContentJson.Options));
        app.MapGet("/session/{initialId}/api/renderer/page/{pageId}", async (string initialId, string pageId) => {
            var data = await PreviewDataLoader.CurrentAsync(initialId);
            return pageId == data.Page.Id ? Results.Json(data.Page, ContentJson.Options) : Results.NotFound();
        });
        app.MapRazorComponents<PreviewApp>().AddInteractiveServerRenderMode(options => {
            // Cross-port embedding is needed only for private previews. Disable
            // WebSocket compression and retain the explicit loopback CSP policy.
            options.DisableWebSocketCompression = true;
            options.ContentSecurityFrameAncestorsPolicy = null;
        });
        app.Run();
        static void RestoreDefaults(JsonElement parent, List<FieldDefinition> fields, string property) {
            if (!parent.TryGetProperty(property, out var rawFields)) return;
            foreach (var rawField in rawFields.EnumerateArray())
                if (rawField.TryGetProperty("defaultValue", out var value))
                    fields.Single(field => field.Id == rawField.GetProperty("id").GetString()).DefaultValue = value.Clone();
        }
        public sealed record PreviewData(TemplateDefinition Template, PageDocument Page, LibraryNode Node, string Title, string RequestId = "");
        public sealed class PreviewNavigation : NavigationManager { public PreviewNavigation(string origin) => Initialize(origin, origin); protected override void NavigateToCore(string uri, bool forceLoad) { } }
        public sealed class PreviewJs : IJSRuntime {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException("Object previews render without scripting.");
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        }
        public sealed class PreviewErrors : IErrorBoundaryLogger { public ValueTask LogErrorAsync(Exception error) => throw new InvalidOperationException("The component could not render: " + error.Message, error); }
        """;

    private const string HostPage = """
        @namespace HyperlibGuidedPreview
        @using Microsoft.AspNetCore.Components
        @using HyperLib.Runtime
        @using HyperLib.Core
        @using Hypershell.Services
        @inject RendererOptions Options
        <main class="hl-document @(CompleteTemplate ? "" : "studio-staged-layout studio-guided-layout") @Options.VariantClass(null, Data.Node)" style="@(Options.AppearanceStyle(null, Data.Node) + (CompleteTemplate ? "" : ";" + Setting("studioBackgroundCss")))" data-guided-page-preview data-studio-width="@Setting("studioWidth")" data-studio-alignment="@Setting("studioAlignment")" data-studio-navigation="@Setting("studioNavigation")">
            @foreach (var region in Data.Template.Regions.Where(region => !CompleteTemplate || Data.Page.Blocks.Any(block => block.Region == region.Id))) {
                var prefix = region.Id switch { "header" => "Header", "navigation" => "Navigation", "footer" => "Footer", "sidebar" => "Extras", _ => "Body" };
                <section class="hl-region" data-region="@region.Id" aria-label="@region.Title" style="@(CompleteTemplate ? "" : Setting("studio" + prefix + "Css"))">
                    @if (!CompleteTemplate && (Setting("studio" + prefix + "Logo").Length > 0 || Setting("studio" + prefix + "Text").Length > 0)) {
                        <div class="studio-region-chrome" data-logo-placement="@Setting("studio" + prefix + "LogoPlacement")">
                            @if (Setting("studio" + prefix + "Logo").Length > 0 && Setting("studio" + prefix + "LogoPlacement") != "none") { <img class="studio-region-logo" src="@Setting("studio" + prefix + "Logo")" alt="Reusable logo" /> }
                            @if (Setting("studio" + prefix + "Text").Length > 0) { <p class="studio-region-text">@Setting("studio" + prefix + "Text")</p> }
                        </div>
                    }
                    @foreach (var block in Data.Page.Blocks.Where(block => block.Region == region.Id && block.Type != "studio-navigation")) {
                        var effective = Options.WithFieldDefaults(block);
                        <section @key="block.Id" class="hl-block @Options.BlockTypeClass(block) @RendererOptions.BlockSpanClass(effective)" style="@RendererOptions.BlockStyle(effective)" data-block-type="@block.Type" data-block-id="@block.Id">
                            @if (block.Type is "studio-header-row" or "studio-body-row" or "studio-footer-row") {
                                @if (Interactive) { <StudioHeaderRowBlock Context="@(new BlockContext { Id = block.Id, Editing = false, Values = effective.Values })"/> }
                                else { @((MarkupString)HyperlibHeaderLayouts.RenderSerializedBlock(effective.Values.TryGetValue("layout", out var layout) ? layout.GetString() ?? "" : "", block.Type == "studio-body-row" ? "body" : block.Type == "studio-footer-row" ? "footer" : "header", designPreview: true)) }
                            } else {
                                <DynamicComponent Type="Options.Resolve(block.Type)" Parameters="@(new Dictionary<string, object> { ["Context"] = new BlockContext { Id = block.Id, Values = effective.Values } })" />
                                @if (!CompleteTemplate && Empty(block)) { <span class="studio-slot-placeholder">@BlockTitle(block) · content slot</span> }
                            }
                        </section>
                    }
                    @if (!CompleteTemplate && !Data.Page.Blocks.Any(block => block.Region == region.Id && block.Type != "studio-navigation") && Setting("studio" + prefix + "Text").Length == 0 && Setting("studio" + prefix + "Logo").Length == 0) {
                        <div class="studio-empty-region" aria-label="@("Empty " + region.Title + " region")"></div>
                    }
                </section>
            }
        </main>
        @code {
            [Parameter, EditorRequired] public PreviewData Data { get; set; } = null!;
            [Parameter] public bool Interactive { get; set; }
            private bool CompleteTemplate => string.Equals(Setting("studioCompleteTemplate"), "true", StringComparison.OrdinalIgnoreCase);
            private string Setting(string key) => Data.Node.Appearance.TryGetValue(key, out var value) ? value.ToString() : "";
            private string BlockTitle(PageBlock block) => Data.Template.Blocks.First(item => item.Id == block.Type).Title;
            private bool Empty(PageBlock block) => !Data.Template.Blocks.First(item => item.Id == block.Type).Fields
                .Where(field => field.Type is "string" or "text" or "richtext" or "url" or "image" or "media")
                .Any(field => block.Values.TryGetValue(field.Id, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()));
        }
        """;
}
