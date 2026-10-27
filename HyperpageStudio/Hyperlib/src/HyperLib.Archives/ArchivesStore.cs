using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using HyperLib.Runtime;

namespace HyperLib.Archives;

public sealed class ArchivesCatalog
{
    public Dictionary<string, PublishedLibrary> Libraries { get; set; } = new(StringComparer.Ordinal);
}
public sealed class PublishedLibrary
{
    public string PublisherId { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string ReleaseId { get; set; } = "";
    public Dictionary<string, LibraryNode> Nodes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Editions { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ArchivesReceipt> Receipts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Labels { get; set; } = [];
}
public sealed record ArchivesReceipt(string PublisherId, string ReleaseId, string ManifestSha256,
    string RootNodeId, int PageCount, int ObjectCount, DateTimeOffset SyncedAtUtc, string Url);
public sealed class ArchivesConflictException(string message) : InvalidOperationException(message);

/// <summary>Owner-authenticated imports become visible only after every selected renderer passes its build and smoke test.</summary>
public sealed class ArchivesStore : IAsyncDisposable
{
    public const long MaxPackageBytes = 128L * 1024 * 1024;
    private const long MaxExpandedBytes = 256L * 1024 * 1024;
    private static readonly Regex Digest = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Publisher = new("^[a-f0-9]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex LocalMediaReference = new("(^|[\\s\"'(=])/media/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string _root, _sdk, _gateway, _publicOrigin;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Lazy<Task<Edition>>> _editions = new(StringComparer.Ordinal);
    private ArchivesCatalog _catalog;
    public ArchivesCatalog Catalog => ReadCatalog();

    public ArchivesStore(string root, string sdk, string gateway, string publicOrigin)
    {
        _root = Path.GetFullPath(root); _sdk = Path.GetFullPath(sdk);
        _gateway = gateway.TrimEnd('/'); _publicOrigin = publicOrigin.TrimEnd('/');
        Directory.CreateDirectory(_root);
        var catalogPath = Path.Combine(_root, "catalog.json");
        _catalog = File.Exists(catalogPath) ? JsonSerializer.Deserialize<ArchivesCatalog>(File.ReadAllText(catalogPath), ContentJson.Options)
            ?? throw new InvalidDataException("The Archives catalog is empty.") : new();
    }
    public static string ValidatePublisher(string value)
    {
        var normalized = Guid.TryParse(value, out var id) ? id.ToString("N") : value;
        if (!Publisher.IsMatch(normalized)) throw new ContentValidationException("Invalid publisher identity.");
        return normalized;
    }
    public static string ValidateDigest(string value)
    {
        if (!Digest.IsMatch(value)) throw new ContentValidationException("Invalid publication digest.");
        return value;
    }
    public string? ReleaseFor(string publisherId) => ReadCatalog().Libraries.GetValueOrDefault(ValidatePublisher(publisherId))?.ReleaseId;
    public bool IsPublished(string publisherId, string editionId) =>
        ReadCatalog().Libraries.TryGetValue(publisherId, out var library) && library.Editions.Values.Contains(editionId, StringComparer.Ordinal);

    public Edition? LoadedEdition(string publisherId, string editionId)
        => _editions.TryGetValue(publisherId + "/" + editionId, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully
            ? lazy.Value.Result : null;

    private ArchivesCatalog ReadCatalog()
    {
        var path = Path.Combine(_root, "catalog.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<ArchivesCatalog>(File.ReadAllText(path), ContentJson.Options)
            ?? throw new InvalidDataException("The Archives catalog is empty.") : new();
    }

    private async Task<FileStream> AcquirePublicationLockAsync(CancellationToken ct)
    {
        var path = SafePath(_root, "publication.lock");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(200, ct); }
        }
    }

    public async Task<ArchivesReceipt> PublishAsync(Stream package, string manifestHash, string? expectedReleaseId, CancellationToken ct)
    {
        ValidateDigest(manifestHash);
        await _publishGate.WaitAsync(ct);
        var upload = Path.Combine(_root, "uploads", Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using var publicationLock = await AcquirePublicationLockAsync(ct);
            _catalog = ReadCatalog();
            Directory.CreateDirectory(Path.GetDirectoryName(upload)!);
            await using (var destination = new FileStream(upload, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyBoundedAsync(package, destination, MaxPackageBytes, ct);
            using var zip = ZipFile.OpenRead(upload);
            var manifestEntry = zip.GetEntry("manifest.json") ?? throw new ContentValidationException("The package has no manifest.");
            var bytes = await ReadBoundedAsync(manifestEntry, 2 * 1024 * 1024, ct);
            if (Hash(bytes) != manifestHash) throw new ContentValidationException("The manifest digest does not match the reviewed publication.");
            var manifest = JsonSerializer.Deserialize<ArchivesManifest>(bytes, ContentJson.Options) ?? throw new ContentValidationException("Invalid manifest.");
            manifest.PublisherId = ValidatePublisher(manifest.PublisherId);
            ValidateManifest(manifest);
            var previous = _catalog.Libraries.GetValueOrDefault(manifest.PublisherId);
            if (previous?.Receipts.TryGetValue(manifestHash, out var completed) == true) return completed;
            if (!string.Equals(previous?.ReleaseId ?? "", expectedReleaseId ?? "", StringComparison.Ordinal))
                throw new ArchivesConflictException("Archives changed since this sync began. Review the latest publication status and retry.");

            var editionRoot = EditionRoot(manifest.PublisherId, manifestHash);
            var originalRoot = Path.Combine(editionRoot, "original");
            Directory.CreateDirectory(originalRoot);
            await ExtractVerifiedAsync(zip, manifest, originalRoot, ct);
            var stateBytes = await File.ReadAllBytesAsync(Path.Combine(originalRoot, "Content", "library.json"), ct);
            if (Hash(stateBytes) != manifest.ContentSha256) throw new ContentValidationException("The selected content digest does not match.");
            var state = JsonSerializer.Deserialize<LibraryState>(stateBytes, ContentJson.Options) ?? throw new ContentValidationException("No selected content.");
            ValidateState(manifest, state);
            await File.WriteAllBytesAsync(Path.Combine(editionRoot, "manifest.json"), bytes, ct);

            var edition = await GetEditionAsync(manifest.PublisherId, manifestHash, ct);
            // No public pointer is written until every Work (including empty Works) and library presentation renders.
            foreach (var id in manifest.WorkIds.Prepend("library").Distinct(StringComparer.Ordinal))
            {
                try
                {
                    await edition.Runtime.EnsureRuntimeAsync(id, edition.Gateway, ct);
                    var status = edition.Runtime.GetStatuses().Single(item => item.PackageId == id);
                    if (status.State != "active" || !string.IsNullOrEmpty(status.Error))
                        throw new InvalidOperationException("The selected renderer did not pass publication validation.");
                }
                finally { await edition.Runtime.ReleaseRuntimeAsync(id); }
            }
            var next = ContentJson.Clone(_catalog);
            if (!next.Libraries.TryGetValue(manifest.PublisherId, out var target))
                next.Libraries[manifest.PublisherId] = target = new() { PublisherId = manifest.PublisherId, LibraryId = manifest.LibraryId };
            if (target.LibraryId != manifest.LibraryId) throw new ContentValidationException("The publisher identity belongs to another library.");
            var selected = manifest.SelectedNodeIds.ToHashSet(StringComparer.Ordinal);
            foreach (var node in state.Nodes)
            {
                if (!selected.Contains(node.Id) && target.Nodes.ContainsKey(node.Id)) continue;
                target.Nodes[node.Id] = node;
                target.Editions[node.Id] = manifestHash;
            }
            // A moved ancestor must not create a cycle or orphan in the combined public hierarchy.
            ValidateMergedHierarchy(target);
            target.Labels = state.Labels;
            target.ReleaseId = Guid.NewGuid().ToString("N");
            var receipt = new ArchivesReceipt(manifest.PublisherId, target.ReleaseId, manifestHash, manifest.RootNodeId,
                state.Pages.Count, selected.Count, DateTimeOffset.UtcNow,
                _publicOrigin + "/Archives/" + manifest.PublisherId + "/" + Uri.EscapeDataString(manifest.RootNodeId));
            target.Receipts[manifestHash] = receipt;
            var temporary = Path.Combine(_root, "catalog." + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, ContentJson.Options), ct);
            File.Move(temporary, Path.Combine(_root, "catalog.json"), overwrite: true);
            _catalog = next;
            return receipt;
        }
        finally { if (File.Exists(upload)) File.Delete(upload); _publishGate.Release(); }
    }

    public async Task<Edition> GetEditionAsync(string publisherId, string editionId, CancellationToken ct = default)
    {
        publisherId = ValidatePublisher(publisherId); ValidateDigest(editionId);
        var key = publisherId + "/" + editionId;
        var lazy = _editions.GetOrAdd(key, _ => new(() => LoadEditionAsync(publisherId, editionId)));
        try { return await lazy.Value.WaitAsync(ct); }
        catch { if (lazy.IsValueCreated && lazy.Value.IsFaulted) _editions.TryRemove(new KeyValuePair<string, Lazy<Task<Edition>>>(key, lazy)); throw; }
    }
    private async Task<Edition> LoadEditionAsync(string publisherId, string editionId)
    {
        var editionRoot = EditionRoot(publisherId, editionId);
        var original = Path.Combine(editionRoot, "original");
        var manifest = JsonSerializer.Deserialize<ArchivesManifest>(await File.ReadAllTextAsync(Path.Combine(editionRoot, "manifest.json")), ContentJson.Options)!;
        var rendering = Path.Combine(editionRoot, "rendering");
        var prefix = $"/Archives/_editions/{publisherId}/{editionId}";
        Directory.CreateDirectory(rendering);
        var prepared = Path.Combine(rendering, "archives-renderer.ready");
        if (!File.Exists(prepared))
        {
        foreach (var entry in manifest.Files)
        {
            var source = SafePath(original, entry.Path);
            var data = await File.ReadAllBytesAsync(source);
            if (data.LongLength != entry.Length || Hash(data) != entry.Sha256) throw new ContentValidationException("Stored publication integrity check failed.");
            if (IsText(entry.Path)) data = Encoding.UTF8.GetBytes(LocalMediaReference.Replace(Encoding.UTF8.GetString(data),
                match => match.Groups[1].Value + prefix + "/media/"));
            var destination = SafePath(rendering, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, data);
        }
        // Fixed SDK and host are deployed with the receiver. Uploaded build/startup logic is never executed.
        CopyTree(Path.Combine(_sdk, "src", "HyperLib.Core"), Path.Combine(rendering, "src", "HyperLib.Core"));
        CopyTree(Path.Combine(_sdk, "src", "HyperLib.Runtime"), Path.Combine(rendering, "src", "HyperLib.Runtime"));
        foreach (var id in manifest.WorkIds.Prepend("library").Distinct(StringComparer.Ordinal))
        {
            var projectRoot = id == "library" ? rendering : SafePath(rendering, "Works/" + id);
            CopyTree(Path.Combine(_sdk, "Starters", "standard", "src", "Host"), Path.Combine(projectRoot, "src", "Host"));
            var program = Path.Combine(projectRoot, "src", "Host", "Program.cs");
            var text = await File.ReadAllTextAsync(program);
            await File.WriteAllTextAsync(program, text.Replace("Template = template, Components = components", "ReadOnly = true, Template = template, Components = components", StringComparison.Ordinal));
            var sdkReference = Path.GetRelativePath(Path.Combine(projectRoot, "src", "Pages"), Path.Combine(rendering, "src", "HyperLib.Runtime", "HyperLib.Runtime.csproj")).Replace('\\', '/');
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "src", "Pages", "Pages.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk.Razor\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><RootNamespace>HyperLib.Work</RootNamespace><AssemblyName>HyperLib.Work</AssemblyName></PropertyGroup><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\"/><ProjectReference Include=\"" + sdkReference + "\"/></ItemGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "visual-page.project.json"), "{}");
        }
        await File.WriteAllTextAsync(prepared, manifest.RendererContract);
        }
        var state = JsonSerializer.Deserialize<LibraryState>(await File.ReadAllTextAsync(Path.Combine(rendering, "Content", "library.json")), ContentJson.Options)!;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var runtime = new HyperLibRuntimeSupervisor(rendering, prefix, token, watchSources: false);
        try { await runtime.EnsureProjectsAsync(state); }
        catch { await runtime.DisposeAsync(); throw; }
        return new Edition(publisherId, editionId, prefix, _gateway + prefix, rendering, token, state, runtime);
    }
    public sealed record Edition(string PublisherId, string Id, string Prefix, string Gateway, string Root,
        string GatewayToken, LibraryState State, HyperLibRuntimeSupervisor Runtime);

    private string EditionRoot(string publisherId, string editionId) => SafePath(_root, "editions/" + publisherId + "/" + editionId);
    private static void ValidateManifest(ArchivesManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.RendererContract != "hyperlib-razor-v1" || manifest.SyncMode != "upsert")
            throw new ContentValidationException("Unsupported publication contract.");
        TemplateValidator.ValidateId(manifest.LibraryId, "library");
        TemplateValidator.ValidateId(manifest.RootNodeId, "root");
        if (manifest.Files is not { Count: > 0 and <= 8192 } || manifest.SelectedNodeIds is not { Count: > 0 and <= 100_000 }
            || manifest.AncestorNodeIds is null || manifest.WorkIds is null)
            throw new ContentValidationException("Invalid publication file or object list.");
        ValidateDigest(manifest.ContentSha256); ValidateDigest(manifest.ReviewHash);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            ValidatePackagePath(file.Path);
            if (!paths.Add(file.Path) || file.Length < 0 || file.Length > 64L * 1024 * 1024 || (total += file.Length) > MaxExpandedBytes)
                throw new ContentValidationException("Duplicate or oversized publication file.");
            ValidateDigest(file.Sha256);
        }
        if (!paths.Contains("Content/library.json") || !paths.Contains("Presentation/authoring.template.json"))
            throw new ContentValidationException("Required publication files are missing.");
    }
    private static void ValidateState(ArchivesManifest manifest, LibraryState state)
    {
        ContentRules.Validate(state);
        if (state.LibraryId != manifest.LibraryId || state.Revision != manifest.SourceRevision)
            throw new ContentValidationException("The selected content does not match its manifest.");
        var exported = manifest.SelectedNodeIds.Concat(manifest.AncestorNodeIds).Where(id => id != state.LibraryId).ToHashSet(StringComparer.Ordinal);
        if (!exported.SetEquals(state.Nodes.Select(n => n.Id))) throw new ContentValidationException("The package includes unreviewed content.");
        if (!manifest.WorkIds.ToHashSet(StringComparer.Ordinal).SetEquals(state.Nodes.Where(n => n.Type == "work").Select(n => n.Id)))
            throw new ContentValidationException("The package Work list does not match its content.");
        if (state.Pages.Keys.Any(id => !manifest.SelectedNodeIds.Contains(id, StringComparer.Ordinal)))
            throw new ContentValidationException("The package includes an unchecked Page.");
    }
    private static void ValidateMergedHierarchy(PublishedLibrary library)
    {
        foreach (var node in library.Nodes.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            var parent = node.ParentId;
            while (parent != library.LibraryId)
            {
                if (parent is null || !visited.Add(parent) || !library.Nodes.TryGetValue(parent, out var ancestor))
                    throw new ContentValidationException("This update would break the published hierarchy.");
                parent = ancestor.ParentId;
            }
        }
    }
    private static async Task ExtractVerifiedAsync(ZipArchive zip, ArchivesManifest manifest, string destination, CancellationToken ct)
    {
        if (zip.Entries.Count != manifest.Files.Count + 1) throw new ContentValidationException("The package contains unlisted files.");
        var files = manifest.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (!seen.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new ContentValidationException("Duplicate archive paths and symbolic links are not supported.");
            if (entry.FullName == "manifest.json") continue;
            ValidatePackagePath(entry.FullName);
            if (!files.TryGetValue(entry.FullName, out var file) || entry.Length != file.Length) throw new ContentValidationException("Unlisted or changed package entry.");
            var data = await ReadBoundedAsync(entry, file.Length, ct);
            if (Hash(data) != file.Sha256) throw new ContentValidationException("Package file integrity check failed.");
            var target = SafePath(destination, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, data, ct);
        }
    }
    public static void ValidatePackagePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 400 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':')
            || path.Split('/').Any(p => p is "" or "." or ".." || p.StartsWith('.') || p.EndsWith('.') || p.EndsWith(' ')))
            throw new ContentValidationException("Invalid publication file path.");
        var catalogAsset = Regex.IsMatch(path, "^Works/[a-zA-Z0-9_-]+/src/Host/wwwroot/catalog/[a-zA-Z0-9_-]+/.+$", RegexOptions.CultureInvariant);
        var allowed = path is "Content/library.json" or "Presentation/authoring.template.json"
            || path.StartsWith("Media/", StringComparison.Ordinal)
            || path.StartsWith("src/Pages/", StringComparison.Ordinal) || path.StartsWith("Shared/", StringComparison.Ordinal)
            || Regex.IsMatch(path, "^Works/[a-zA-Z0-9_-]+/(authoring\\.template\\.json|src/Pages/.+|Shared/.+)$", RegexOptions.CultureInvariant)
            || catalogAsset;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (catalogAsset && extension is not (".html" or ".json" or ".css" or ".js" or ".mjs" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".avif" or ".svg" or ".ico"
            or ".woff" or ".woff2" or ".ttf" or ".otf" or ".mp3" or ".wav" or ".ogg" or ".m4a" or ".mp4" or ".webm" or ".pdf"))
            throw new ContentValidationException("Unsupported catalog static asset: " + path);
        if (!allowed || extension is ".props" or ".targets" or ".exe" or ".dll" or ".so" or ".pdb" or ".config"
            || (extension == ".csproj" && !path.EndsWith("src/Pages/Pages.csproj", StringComparison.Ordinal))
            || path.Split('/').Any(p => p is "obj" or "bin" or "node_modules"))
            throw new ContentValidationException("Unsupported publication file: " + path);
    }
    public static string SafePath(string root, string relative)
    {
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!full.StartsWith(basePath + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ContentValidationException("Publication path escaped its root.");
        var current = basePath;
        foreach (var part in new[] { "" }.Concat(Path.GetRelativePath(basePath, full).Split(Path.DirectorySeparatorChar)))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new ContentValidationException("Publication links are not supported.");
        }
        return full;
    }
    private static bool IsText(string path) => Path.GetExtension(path).ToLowerInvariant() is ".json" or ".razor" or ".cs" or ".css" or ".js" or ".mjs" or ".html" or ".svg";
    private static void CopyTree(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The receiver SDK is incomplete: " + source);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
            var target = SafePath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // The deployed SDK is immutable (0444 on Linux). File.Copy carries
            // that mode into the build tree, where host configuration is rewritten.
            if (File.Exists(target))
            {
                if (OperatingSystem.IsWindows()) File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
                else File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
            File.WriteAllBytes(target, File.ReadAllBytes(file));
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<byte[]> ReadBoundedAsync(ZipArchiveEntry entry, long maximum, CancellationToken ct)
    {
        if (entry.Length > maximum) throw new ContentValidationException("Oversized package entry.");
        await using var input = entry.Open(); using var output = new MemoryStream();
        await CopyBoundedAsync(input, output, maximum, ct); return output.ToArray();
    }
    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, CancellationToken ct)
    {
        var buffer = new byte[65536]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if ((total += read) > maximum) throw new ContentValidationException("The publication exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var edition in _editions.Values.Where(e => e.IsValueCreated))
            try { await (await edition.Value).Runtime.DisposeAsync(); } catch { }
        _publishGate.Dispose();
    }
}
