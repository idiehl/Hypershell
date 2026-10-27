using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HyperLib.Core;

/// <summary>Freezes a reviewed, selected branch without exposing the private content store or its history.</summary>
public sealed class ArchivesPublicationService
{
    public const long MaximumPackageBytes = 256L * 1024 * 1024;
    public const long MaximumZipBytes = 128L * 1024 * 1024;
    public const int MaximumPackageFiles = 8192;
    private const long MaximumFileBytes = 64L * 1024 * 1024;
    private readonly string _root;
    private readonly string _directory;
    private readonly ContentStore _store;
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".css", ".js", ".mjs", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".otf", ".mp3", ".wav", ".ogg", ".m4a", ".mp4", ".webm", ".pdf" };
    private static readonly Regex Quoted = new("[\\\"'](?<path>[^\\\"'\\r\\n]+)[\\\"']", RegexOptions.CultureInvariant);
    private static readonly Regex Media = new("(?:^|[\\s(\\\"'=])(?<path>/media/[^\\s\\\"'<>?#)\\]]+)", RegexOptions.CultureInvariant);
    private static readonly Regex LoopbackMedia = new("https?://(?:127\\.0\\.0\\.1|localhost|\\[::1\\])(?::[0-9]+)?(?=/media/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public ArchivesPublicationService(string libraryRoot, ContentStore? store = null)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        TemplatePathGuard.NoLinks(_root);
        _directory = PathFor(".hyperlib/archives");
        _store = store ?? new ContentStore(_root);
    }

    public Task<ArchivesDraft> CreateDraftAsync(string rootNodeId, long expectedContentRevision, CancellationToken ct = default)
        => LockedAsync(async () =>
        {
            var state = await _store.ReadAsync(ct);
            CheckContentRevision(state, expectedContentRevision);
            var snapshot = BranchSnapshot(state, rootNodeId, out var branch);
            var now = DateTimeOffset.UtcNow;
            var stored = new StoredDraft
            {
                State = snapshot,
                Draft = new ArchivesDraft
                {
                    Id = Guid.NewGuid().ToString("N"), PublisherId = await PublisherIdAsync(ct),
                    RootNodeId = rootNodeId, Title = branch.Single(n => n.Id == rootNodeId).Title,
                    ContentRevision = state.Revision, DraftRevision = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
                    Nodes = branch, SelectedNodeIds = branch.Select(n => n.Id).ToList()
                }
            };
            stored.SourceFiles = Capture(snapshot, snapshot.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal), ct).Fingerprints;
            UpdateReview(stored);
            await EnsureStillCurrentAsync(stored, ct);
            await SaveAsync(stored, ct);
            return ContentJson.Clone(stored.Draft);
        }, ct);

    public Task<ArchivesDraft> GetDraftAsync(string draftId, CancellationToken ct = default)
        => LockedAsync(async () => await DescribeAsync(await LoadAsync(draftId, ct), ct), ct);

    public Task<IReadOnlyList<ArchivesDraft>> ListDraftsAsync(CancellationToken ct = default)
        => LockedAsync<IReadOnlyList<ArchivesDraft>>(async () =>
        {
            var drafts = new List<ArchivesDraft>();
            var directory = PathFor(".hyperlib/archives/drafts");
            if (!Directory.Exists(directory)) return drafts;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                var id = Path.GetFileNameWithoutExtension(file);
                drafts.Add(await DescribeAsync(await LoadAsync(id, ct), ct));
            }
            return drafts.OrderByDescending(d => d.UpdatedAtUtc).ToList();
        }, ct);

    public async Task<ArchivesDraft?> GetLatestDraftAsync(string rootNodeId, CancellationToken ct = default)
        => (await ListDraftsAsync(ct)).FirstOrDefault(draft => draft.RootNodeId == rootNodeId);

    public Task<ArchivesDraft> UpdateSelectionAsync(string draftId, long expectedDraftRevision,
        IReadOnlyCollection<string> selectedNodeIds, CancellationToken ct = default)
        => LockedAsync(async () =>
        {
            var stored = await LoadAsync(draftId, ct);
            CheckDraftRevision(stored, expectedDraftRevision);
            var branchIds = stored.Draft.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            TemplateValidator.Require(selectedNodeIds is not null && selectedNodeIds.Count <= branchIds.Count
                && selectedNodeIds.All(branchIds.Contains), "The selection contains an object outside this draft.");
            var selected = Ancestors(stored.State, selectedNodeIds!);
            stored.Draft.SelectedNodeIds = stored.Draft.Nodes.Where(n => selected.Contains(n.Id)).Select(n => n.Id).ToList();
            stored.Draft.DraftRevision = checked(stored.Draft.DraftRevision + 1);
            stored.Draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
            UpdateReview(stored);
            await SaveAsync(stored, ct);
            return await DescribeAsync(stored, ct);
        }, ct);

    public Task<ArchivesDraft> RefreshDraftAsync(string draftId, long expectedDraftRevision,
        long expectedContentRevision, CancellationToken ct = default)
        => LockedAsync(async () =>
        {
            var stored = await LoadAsync(draftId, ct);
            CheckDraftRevision(stored, expectedDraftRevision);
            var state = await _store.ReadAsync(ct);
            CheckContentRevision(state, expectedContentRevision);
            stored.State = BranchSnapshot(state, stored.Draft.RootNodeId, out var branch);
            var previous = stored.Draft.SelectedNodeIds.ToHashSet(StringComparer.Ordinal);
            // A refresh never silently opts newly-created objects into a publication.
            var selected = Ancestors(stored.State, branch.Where(n => previous.Contains(n.Id)).Select(n => n.Id));
            stored.Draft.Nodes = branch;
            stored.Draft.SelectedNodeIds = branch.Where(n => selected.Contains(n.Id)).Select(n => n.Id).ToList();
            stored.Draft.Title = branch.Single(n => n.Id == stored.Draft.RootNodeId).Title;
            stored.Draft.ContentRevision = state.Revision;
            stored.Draft.DraftRevision = checked(stored.Draft.DraftRevision + 1);
            stored.Draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
            stored.SourceFiles = Capture(stored.State, stored.State.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal), ct).Fingerprints;
            UpdateReview(stored);
            await EnsureStillCurrentAsync(stored, ct);
            await SaveAsync(stored, ct);
            return ContentJson.Clone(stored.Draft);
        }, ct);

    public Task<ArchivesPackageResult> ExportAsync(string draftId, long expectedDraftRevision,
        long expectedContentRevision, string expectedReviewHash, CancellationToken ct = default)
        => LockedAsync(async () =>
        {
            var stored = await LoadAsync(draftId, ct);
            CheckDraftRevision(stored, expectedDraftRevision);
            if (stored.Draft.ContentRevision != expectedContentRevision || stored.Draft.ReviewHash != expectedReviewHash)
                throw new ArchivesDraftConflictException("The reviewed Archives selection changed. Review the current draft before publishing.");
            TemplateValidator.Require(stored.Draft.SelectedNodeIds.Count > 0, "Select at least one object to publish.");
            await EnsureStillCurrentAsync(stored, ct);
            var included = stored.Draft.IncludedNodeIds.ToHashSet(StringComparer.Ordinal);
            var selectedState = SelectState(stored.State, included);
            var capture = Capture(stored.State, included, ct);
            var stateBytes = JsonBytes(selectedState);
            capture.Files["Content/library.json"] = new(stateBytes, "content");
            CheckBounds(capture.Files);
            var manifest = new ArchivesManifest
            {
                PublisherId = stored.Draft.PublisherId, LibraryId = stored.State.LibraryId,
                RootNodeId = stored.Draft.RootNodeId, DraftId = stored.Draft.Id,
                DraftRevision = stored.Draft.DraftRevision, SourceRevision = stored.Draft.ContentRevision,
                ReviewHash = stored.Draft.ReviewHash, SelectedNodeIds = [.. stored.Draft.SelectedNodeIds],
                AncestorNodeIds = stored.Draft.IncludedNodeIds.Except(stored.Draft.SelectedNodeIds, StringComparer.Ordinal).ToList(),
                WorkIds = selectedState.Nodes.Where(n => n.Type == "work").Select(n => n.Id).Order(StringComparer.Ordinal).ToList(),
                ContentSha256 = Hash(stateBytes),
                Files = capture.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ArchivesFileEntry
                    { Path = pair.Key, Length = pair.Value.Bytes.LongLength, Sha256 = Hash(pair.Value.Bytes), Role = pair.Value.Role }).ToList()
            };
            var manifestBytes = JsonBytes(manifest);
            var manifestHash = Hash(manifestBytes);
            var package = PathFor($".hyperlib/archives/packages/{stored.Draft.Id}-{manifestHash}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(package)!);
            var temporary = package + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await AddZipEntryAsync(zip, "manifest.json", manifestBytes, ct);
                    foreach (var pair in capture.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                        await AddZipEntryAsync(zip, pair.Key, pair.Value.Bytes, ct);
                }
                TemplateValidator.Require(new FileInfo(temporary).Length <= MaximumZipBytes,
                    "The compressed Archives publication exceeds 128 MiB. Publish a smaller branch.");
                await EnsureStillCurrentAsync(stored, ct);
                TemplatePathGuard.NoLinks(package);
                File.Move(temporary, package, overwrite: true);
                return new ArchivesPackageResult(stored.Draft.Id, package, manifestHash, Hash(await File.ReadAllBytesAsync(package, ct)), manifest);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, ct);

    private CaptureResult Capture(LibraryState snapshot, HashSet<string> included, CancellationToken ct)
    {
        var result = new CaptureResult();
        long inputBytes = 0;
        var selectedState = SelectState(snapshot, included);
        AddProject("", "Presentation/authoring.template.json");
        foreach (var work in selectedState.Nodes.Where(n => n.Type == "work"))
            AddProject("Works/" + work.Id + "/", "authoring.template.json");
        InspectMedia(JsonSerializer.Serialize(selectedState, ContentJson.Options));
        CheckBounds(result.Files);
        result.Fingerprints = result.Fingerprints.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        return result;

        void AddProject(string prefix, string definitionPath)
        {
            ct.ThrowIfCancellationRequested();
            ValidateHost(prefix);
            var definitionRelative = prefix + definitionPath;
            var definitionBytes = ReadInput(definitionRelative);
            var definition = JsonSerializer.Deserialize<TemplateDefinition>(definitionBytes, ContentJson.Options)
                ?? throw new ContentValidationException("An Archives renderer template is empty.");
            TemplateValidator.ValidateDefinition(definition);
            var workId = prefix.Length == 0 ? "library" : prefix.Split('/')[1];
            TemplateValidator.EnsureCompatible(definition, selectedState.Pages.Values.Where(p => p.WorkId == workId));
            result.Files.Add(definitionRelative, new(Encoding.UTF8.GetBytes(Normalize(Encoding.UTF8.GetString(definitionBytes))), "template"));
            InspectMedia(Encoding.UTF8.GetString(definitionBytes));
            var pagesRelative = prefix + "src/Pages/";
            var projectBytes = ReadInput(pagesRelative + "Pages.csproj");
            ValidateProject(projectBytes, PathFor(pagesRelative + "Pages.csproj"));
            for (var directory = PathFor(pagesRelative); directory is not null; directory = Path.GetDirectoryName(directory))
            {
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                    TemplateValidator.Require(!File.Exists(Path.Combine(directory, name)), "Archives does not support inherited build customizations: " + name);
                if (directory == _root) break;
            }
            var sdkReference = prefix.Length == 0 ? "../HyperLib.Runtime/HyperLib.Runtime.csproj" : "../../../../src/HyperLib.Runtime/HyperLib.Runtime.csproj";
            result.Files.Add(pagesRelative + "Pages.csproj", new(Encoding.UTF8.GetBytes(StandardProject(sdkReference)), "project"));
            foreach (var file in TemplatePathGuard.Files(PathFor(pagesRelative)).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(_root, file).Replace('\\', '/');
                var local = relative[pagesRelative.Length..];
                if (local == "Pages.csproj" || local.StartsWith("wwwroot/", StringComparison.Ordinal)) continue;
                var extension = Path.GetExtension(local);
                if (extension is not (".razor" or ".cs" or ".css"))
                {
                    if (extension is ".props" or ".targets" or ".csproj")
                        throw new ContentValidationException("Archives does not support additional project files or build hooks: " + relative);
                    continue;
                }
                var bytes = ReadInput(relative);
                var text = Normalize(Encoding.UTF8.GetString(bytes));
                result.Files.Add(relative, new(Encoding.UTF8.GetBytes(text), "source"));
                InspectMedia(text);
                InspectAssets(text, relative, pagesRelative);
            }
            CaptureCatalogEntries(prefix, workId, definition);
        }

        void CaptureCatalogEntries(string prefix, string workId, TemplateDefinition definition)
        {
            var catalogTypes = definition.Blocks.Where(block => block.Component == "CatalogEntryBlock").Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
            if (catalogTypes.Count == 0) return;
            var ids = selectedState.Pages.Values.Where(page => page.WorkId == workId).SelectMany(page => page.Blocks)
                .Where(block => catalogTypes.Contains(block.Type)).Select(block => block.Values.TryGetValue("text", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (var id in ids)
            {
                TemplateValidator.ValidateId(id!, "catalog entry");
                var relativeRoot = prefix + "src/Host/wwwroot/catalog/" + id + "/";
                var directory = PathFor(relativeRoot);
                TemplateValidator.Require(Directory.Exists(directory), "The selected catalog entry is missing its portable documentation: " + id);
                var files = TemplatePathGuard.Files(directory).Order(StringComparer.Ordinal).ToList();
                TemplateValidator.Require(files.Count <= 256 && files.Sum(path => new FileInfo(path).Length) <= 32L * 1024 * 1024,
                    "A catalog entry exceeds its portable limit of 256 files or 32 MiB: " + id);
                TemplateValidator.Require(File.Exists(Path.Combine(directory, "entry.json")) && File.Exists(Path.Combine(directory, "preview", "index.html")),
                    "The selected catalog entry needs both its documentation and preview: " + id);
                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file);
                    TemplateValidator.Require(extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || AssetExtensions.Contains(extension),
                        "Catalog assets must be portable static files: " + Path.GetFileName(file));
                    var relative = Path.GetRelativePath(_root, file).Replace('\\', '/');
                    if (!result.Files.ContainsKey(relative)) result.Files.Add(relative, new(ReadInput(relative), "asset"));
                }
            }
        }

        void ValidateHost(string prefix)
        {
            var hostRoot = PathFor(prefix + "src/Host");
            var standardHost = PathFor("Starters/standard/src/Host");
            TemplateValidator.Require(Directory.Exists(hostRoot) && Directory.Exists(standardHost), "The standard renderer Host is missing.");
            var sourceFiles = TemplatePathGuard.Files(hostRoot).Where(path => Path.GetExtension(path) is ".cs" or ".razor" or ".css" or ".js" or ".csproj")
                .Where(path => !(Path.GetRelativePath(hostRoot, path).Replace('\\', '/').StartsWith("wwwroot/catalog/", StringComparison.Ordinal)
                    && Path.GetExtension(path) is ".css" or ".js"))
                .Order(StringComparer.Ordinal).ToList();
            foreach (var file in sourceFiles)
            {
                var local = Path.GetRelativePath(hostRoot, file).Replace('\\', '/');
                var standard = TemplatePathGuard.Within(standardHost, Path.Combine(standardHost, local));
                var relative = Path.GetRelativePath(_root, file).Replace('\\', '/');
                var bytes = ReadInput(relative);
                var matches = File.Exists(standard);
                if (matches)
                {
                    var text = Encoding.UTF8.GetString(bytes);
                    var baseline = File.ReadAllText(standard);
                    try { matches = local.EndsWith(".csproj", StringComparison.Ordinal)
                        ? XNode.DeepEquals(XDocument.Parse(text), XDocument.Parse(baseline))
                        : text.Replace("\r\n", "\n", StringComparison.Ordinal) == baseline.Replace("\r\n", "\n", StringComparison.Ordinal); }
                    catch (System.Xml.XmlException) { matches = false; }
                }
                TemplateValidator.Require(matches,
                    "Archives uses the standard renderer Host. Move custom rendering into Work components before publishing: " + relative);
            }
            var expected = TemplatePathGuard.Files(standardHost).Where(path => Path.GetExtension(path) is ".cs" or ".razor" or ".css" or ".js" or ".csproj")
                .Select(path => Path.GetRelativePath(standardHost, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
            TemplateValidator.Require(expected.SetEquals(sourceFiles.Select(path => Path.GetRelativePath(hostRoot, path).Replace('\\', '/'))),
                "Archives uses the complete standard renderer Host; restore missing renderer Host files before publishing.");
        }

        byte[] ReadInput(string relative)
        {
            var path = PathFor(relative);
            if (!File.Exists(path)) throw new ContentValidationException("A required Archives source or asset is missing: " + relative);
            var info = new FileInfo(path);
            TemplateValidator.Require(info.Length <= MaximumFileBytes, "An Archives source or media file exceeds 64 MiB: " + relative);
            TemplateValidator.Require(result.Fingerprints.Count < MaximumPackageFiles && inputBytes + info.Length <= MaximumPackageBytes,
                "The Archives source snapshot exceeds 8,192 files or 256 MiB. Publish a smaller branch.");
            var bytes = File.ReadAllBytes(path);
            TemplateValidator.Require(bytes.LongLength <= MaximumFileBytes, "An Archives source or media file exceeds 64 MiB: " + relative);
            if (result.Fingerprints.All(f => f.Path != relative))
            {
                inputBytes += bytes.LongLength;
                TemplateValidator.Require(inputBytes <= MaximumPackageBytes, "The Archives source snapshot exceeds 256 MiB.");
                result.Fingerprints.Add(new() { Path = relative, Length = bytes.LongLength, Sha256 = Hash(bytes) });
            }
            return bytes;
        }

        void InspectMedia(string text)
        {
            foreach (Match match in Media.Matches(Normalize(text)))
            {
                var url = match.Groups["path"].Value;
                var relative = "Media/" + SafeRelative(Uri.UnescapeDataString(url[7..]));
                if (result.Files.ContainsKey(relative)) continue;
                var bytes = ReadInput(relative);
                result.Files.Add(relative, new(bytes, "media"));
            }
        }

        void InspectAssets(string text, string currentRelative, string pagesRelative)
        {
            var paths = Quoted.Matches(text).Select(match => match.Groups["path"].Value)
                .Concat(Regex.Matches(text, "url\\(\\s*(?<path>[^)'\\\"\\s]+)\\s*\\)", RegexOptions.CultureInvariant).Select(match => match.Groups["path"].Value));
            foreach (var raw in paths)
            {
                var value = Normalize(raw);
                if (value.StartsWith("/media/", StringComparison.Ordinal) || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    || value.Any(char.IsWhiteSpace) || value.Contains('@') || value.Contains('{') || value.Contains('$')) continue;
                if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https") continue;
                value = value.Split('?', '#')[0];
                if (!AssetExtensions.Contains(Path.GetExtension(value))) continue;
                string relative;
                const string contentMarker = "_content/HyperLib.Work/";
                if (value.TrimStart('.', '/').StartsWith(contentMarker, StringComparison.Ordinal))
                    relative = pagesRelative + "wwwroot/" + SafeRelative(value.TrimStart('.', '/')[contentMarker.Length..]);
                else if (value.StartsWith("_content/HyperLib.Runtime/", StringComparison.Ordinal)
                    || value.StartsWith("/_content/HyperLib.Runtime/", StringComparison.Ordinal)) continue;
                else
                {
                    if (value.StartsWith('/')) throw new ContentValidationException("Archives cannot resolve a custom rooted asset URL: " + value);
                    var adjacent = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(PathFor(currentRelative))!, Uri.UnescapeDataString(value)));
                    TemplatePathGuard.Within(PathFor(pagesRelative), adjacent);
                    relative = File.Exists(adjacent) ? Path.GetRelativePath(_root, adjacent).Replace('\\', '/')
                        : pagesRelative + "wwwroot/" + SafeRelative(value.TrimStart('.', '/'));
                }
                if (result.Files.ContainsKey(relative)) continue;
                var bytes = ReadInput(relative);
                result.Files.Add(relative, new(bytes, "asset"));
                if (Path.GetExtension(relative) is ".css" or ".js" or ".mjs")
                {
                    var assetText = Normalize(Encoding.UTF8.GetString(bytes));
                    result.Files[relative] = new(Encoding.UTF8.GetBytes(assetText), "asset");
                    InspectMedia(assetText);
                    InspectAssets(assetText, relative, pagesRelative);
                }
            }
        }
    }

    private static LibraryState BranchSnapshot(LibraryState state, string rootNodeId, out List<LibraryNode> branch)
    {
        var root = state.Nodes.SingleOrDefault(node => node.Id == rootNodeId)
            ?? throw new ContentValidationException("Choose an existing Collection, Work, or Page to publish.");
        var ids = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var children = state.Nodes.ToLookup(n => n.ParentId, StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(root.Id);
        while (pending.TryDequeue(out var id)) foreach (var child in children[id]) if (ids.Add(child.Id)) pending.Enqueue(child.Id);
        branch = state.Nodes.Where(n => ids.Contains(n.Id)).Select(ContentJson.Clone).ToList();
        return SelectState(state, Ancestors(state, ids));
    }

    private static LibraryState SelectState(LibraryState state, HashSet<string> included)
    {
        var result = new LibraryState
        {
            SchemaVersion = state.SchemaVersion, Revision = state.Revision, LibraryId = state.LibraryId,
            Labels = ContentJson.Clone(state.Labels), Appearance = ContentJson.Clone(state.Appearance),
            Nodes = state.Nodes.Where(n => included.Contains(n.Id)).Select(ContentJson.Clone).ToList(),
            Pages = state.Pages.Where(pair => included.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => ContentJson.Clone(pair.Value), StringComparer.Ordinal)
        };
        foreach (var node in result.Nodes) node.DirectoryPath = null;
        // Normalize legacy local media URLs in both page values and appearance without copying unrelated objects.
        result = JsonSerializer.Deserialize<LibraryState>(Normalize(JsonSerializer.Serialize(result, ContentJson.Options)), ContentJson.Options)!;
        ContentRules.Validate(result);
        return result;
    }

    private static HashSet<string> Ancestors(LibraryState state, IEnumerable<string> ids)
    {
        var nodes = state.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selected in ids)
        {
            var current = selected;
            while (current != state.LibraryId && included.Add(current))
            {
                if (!nodes.TryGetValue(current, out var node)) throw new ContentValidationException("A publication ancestor is missing.");
                current = node.ParentId ?? throw new ContentValidationException("A publication ancestor is missing.");
            }
        }
        return included;
    }

    private static void UpdateReview(StoredDraft stored)
    {
        var included = Ancestors(stored.State, stored.Draft.SelectedNodeIds);
        stored.Draft.IncludedNodeIds = stored.State.Nodes.Where(n => included.Contains(n.Id)).Select(n => n.Id).ToList();
        stored.Draft.Stale = false;
        stored.Draft.ReviewHash = Hash(JsonBytes(new
        {
            stored.Draft.Id, stored.Draft.PublisherId, stored.Draft.RootNodeId, stored.Draft.ContentRevision,
            stored.Draft.DraftRevision, Selected = stored.Draft.SelectedNodeIds.Order(StringComparer.Ordinal),
            StateHash = Hash(JsonBytes(stored.State)), Sources = stored.SourceFiles
        }));
    }

    private async Task<ArchivesDraft> DescribeAsync(StoredDraft stored, CancellationToken ct)
    {
        var result = ContentJson.Clone(stored.Draft);
        try { await EnsureStillCurrentAsync(stored, ct); }
        catch (Exception error) when (error is ArchivesDraftConflictException or ContentValidationException or IOException or UnauthorizedAccessException) { result.Stale = true; }
        return result;
    }

    private async Task EnsureStillCurrentAsync(StoredDraft stored, CancellationToken ct)
    {
        var current = await _store.ReadAsync(ct);
        if (current.Revision != stored.Draft.ContentRevision)
            throw new ArchivesDraftConflictException("HyperLib content changed. Refresh and review this Archives draft before publishing.");
        var actual = Capture(stored.State, stored.State.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal), ct).Fingerprints;
        if (!JsonBytes(actual).AsSpan().SequenceEqual(JsonBytes(stored.SourceFiles)))
            throw new ArchivesDraftConflictException("A template, component, or media file changed. Refresh and review this Archives draft before publishing.");
    }

    private async Task<string> PublisherIdAsync(CancellationToken ct)
    {
        var path = PathFor(".hyperlib/archives/publisher-id.txt");
        if (!File.Exists(path)) await TemplatePathGuard.WriteNewAsync(path, Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("D")), ct);
        var value = (await File.ReadAllTextAsync(path, ct)).Trim();
        if (!Guid.TryParseExact(value, "D", out _)) throw new ContentValidationException("The Archives publisher identity is invalid. Restore its original identity file before publishing.");
        return value;
    }

    private async Task<StoredDraft> LoadAsync(string id, CancellationToken ct)
    {
        ValidateDraftId(id);
        var path = PathFor(".hyperlib/archives/drafts/" + id + ".json");
        if (!File.Exists(path)) throw new KeyNotFoundException("The Archives draft does not exist.");
        TemplateValidator.Require(new FileInfo(path).Length <= 48L * 1024 * 1024, "The Archives draft is too large.");
        var stored = JsonSerializer.Deserialize<StoredDraft>(await File.ReadAllBytesAsync(path, ct), ContentJson.Options)
            ?? throw new ContentValidationException("The Archives draft is empty.");
        TemplateValidator.Require(stored.Draft.Id == id && stored.Draft.ContentRevision == stored.State.Revision, "The Archives draft is invalid.");
        ContentRules.Validate(stored.State);
        var expected = stored.Draft.ReviewHash;
        UpdateReview(stored);
        TemplateValidator.Require(stored.Draft.ReviewHash == expected, "The Archives draft failed its review integrity check.");
        return stored;
    }

    private Task SaveAsync(StoredDraft stored, CancellationToken ct)
        => TemplatePathGuard.ReplaceAsync(PathFor(".hyperlib/archives/drafts/" + stored.Draft.Id + ".json"), JsonBytes(stored), ct);

    private async Task<T> LockedAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(".hyperlib/archives/store.lock");
        var timer = Stopwatch.StartNew();
        FileStream? handle = null;
        while (handle is null)
        {
            ct.ThrowIfCancellationRequested();
            TemplatePathGuard.NoLinks(path);
            try { handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(30)) { await Task.Delay(40, ct); }
        }
        await using (handle) return await action();
    }

    private string PathFor(string relative) => TemplatePathGuard.Within(_root, Path.Combine(_root, SafeRelative(relative.TrimEnd('/'))));
    private static string SafeRelative(string relative)
    {
        TemplateValidator.Require(relative.Length is > 0 and <= 1000 && !Path.IsPathRooted(relative)
            && !relative.Contains('\\') && !relative.Contains(':') && !relative.Any(char.IsControl)
            && !relative.Split('/').Any(part => part is "" or "." or ".."), "An Archives file path is not safe.");
        return relative;
    }
    private static void ValidateDraftId(string id)
        => TemplateValidator.Require(Guid.TryParseExact(id, "N", out _), "The Archives draft identity is invalid.");
    private static void CheckDraftRevision(StoredDraft stored, long expected)
    {
        if (stored.Draft.DraftRevision != expected) throw new ArchivesDraftConflictException("This Archives draft was edited elsewhere. Reload it before changing the selection.");
    }
    private static void CheckContentRevision(LibraryState state, long expected)
    {
        if (state.Revision != expected) throw new ConflictException(expected, state.Revision);
    }
    private static void CheckBounds(Dictionary<string, CapturedFile> files)
    {
        TemplateValidator.Require(files.Count <= MaximumPackageFiles && files.Values.Sum(f => (long)f.Bytes.Length) <= MaximumPackageBytes,
            "The Archives publication exceeds 8,192 files or 256 MiB. Publish a smaller branch.");
        TemplateValidator.Require(files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Count,
            "Archives file paths must remain unique on every supported operating system.");
        foreach (var path in files.Keys)
            TemplateValidator.Require(path.Length <= 400 && !path.Split('/').Any(part => part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(' ')),
                "Archives cannot publish hidden, ambiguous, or oversized file paths: " + path);
    }
    private static string Normalize(string text) => LoopbackMedia.Replace(text, "");
    private static byte[] JsonBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, ContentJson.Options);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task AddZipEntryAsync(ZipArchive zip, string path, byte[] bytes, CancellationToken ct)
    {
        var entry = zip.CreateEntry(SafeRelative(path), CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var target = entry.Open();
        await target.WriteAsync(bytes, ct);
    }

    private void ValidateProject(byte[] bytes, string projectPath)
    {
        XDocument document;
        try { document = XDocument.Parse(Encoding.UTF8.GetString(bytes)); }
        catch (Exception error) when (error is System.Xml.XmlException or ArgumentException) { throw new ContentValidationException("The Work project is not valid XML."); }
        var root = document.Root;
        TemplateValidator.Require(root?.Name == "Project" && (string?)root.Attribute("Sdk") == "Microsoft.NET.Sdk.Razor"
            && root.Attributes().Count() == 1, "Archives supports the standard Razor project SDK only.");
        var allowedProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TargetFramework"] = "net10.0", ["Nullable"] = "enable", ["ImplicitUsings"] = "enable",
            ["RootNamespace"] = "HyperLib.Work", ["AssemblyName"] = "HyperLib.Work"
        };
        foreach (var group in root!.Elements())
        {
            TemplateValidator.Require(!group.HasAttributes && group.Name.LocalName is "PropertyGroup" or "ItemGroup", "Archives does not support project imports, conditions, or build hooks.");
            foreach (var item in group.Elements())
            {
                if (group.Name.LocalName == "PropertyGroup")
                    TemplateValidator.Require(!item.HasAttributes && !item.HasElements && allowedProperties.TryGetValue(item.Name.LocalName, out var expected)
                        && item.Value.Trim() == expected, "Archives does not support custom project build properties: " + item.Name.LocalName);
                else
                    TemplateValidator.Require(!item.HasElements && item.Attributes().Count() == 1 && item.Attribute("Include") is { } include
                        && (item.Name.LocalName == "FrameworkReference" && include.Value == "Microsoft.AspNetCore.App"
                            || item.Name.LocalName == "ProjectReference" && (include.Value == "__SDK_REF__"
                                || include.Value.Replace('\\', '/').EndsWith("/HyperLib.Runtime/HyperLib.Runtime.csproj", StringComparison.Ordinal))),
                        "Archives does not support external packages, assemblies, or project references.");
            }
        }
        foreach (var property in allowedProperties.Keys)
            TemplateValidator.Require(root.Descendants(property).Count() == 1, "Archives requires the standard Razor project property: " + property);
        var references = root.Descendants("ProjectReference").ToList();
        TemplateValidator.Require(references.Count == 1 && root.Descendants("FrameworkReference").Count() == 1,
            "Archives requires exactly the shared HyperLib runtime and ASP.NET framework references.");
        var referencePath = references[0].Attribute("Include")!.Value;
        if (referencePath != "__SDK_REF__")
        {
            var resolved = Path.GetFullPath(referencePath.Replace('\\', '/'), Path.GetDirectoryName(projectPath)!);
            TemplateValidator.Require(string.Equals(resolved, PathFor("src/HyperLib.Runtime/HyperLib.Runtime.csproj"),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                "Archives cannot publish a Work that references an external HyperLib runtime.");
        }
    }

    private static string StandardProject(string runtimeReference) => $"""
        <Project Sdk="Microsoft.NET.Sdk.Razor">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><RootNamespace>HyperLib.Work</RootNamespace><AssemblyName>HyperLib.Work</AssemblyName></PropertyGroup>
          <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><ProjectReference Include="{runtimeReference}" /></ItemGroup>
        </Project>
        """;

    private sealed class StoredDraft
    {
        public StoredDraft() { }
        public ArchivesDraft Draft { get; set; } = new();
        public LibraryState State { get; set; } = new();
        public List<ArchivesFileEntry> SourceFiles { get; set; } = [];
    }
    private sealed record CapturedFile(byte[] Bytes, string Role);
    private sealed class CaptureResult
    {
        public Dictionary<string, CapturedFile> Files { get; } = new(StringComparer.Ordinal);
        public List<ArchivesFileEntry> Fingerprints { get; set; } = [];
    }
}
