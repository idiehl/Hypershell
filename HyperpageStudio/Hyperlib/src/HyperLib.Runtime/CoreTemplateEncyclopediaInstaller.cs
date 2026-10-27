using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;

namespace HyperLib.Runtime;

public sealed record CoreTemplateEncyclopediaOrigin(string CatalogId, string GroupId, string ObjectId, string TemplateId, string Title, string? SourceLibrary = null);
public sealed record CoreTemplateEncyclopediaCode(string Path, string Language, string Text);
public sealed class CoreTemplateEncyclopediaEntry
{
    public required string CatalogId { get; init; }
    public required string Title { get; set; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Kind { get; init; }
    public required string SourceFingerprint { get; init; }
    public List<CoreTemplateEncyclopediaOrigin> Origins { get; init; } = [];
    public List<CoreTemplateEncyclopediaCode> Code { get; init; } = [];
}

public sealed class CoreTemplateEncyclopediaPlan
{
    public required string LibraryRoot { get; init; }
    public required long ExpectedRevision { get; init; }
    public required IReadOnlyList<CoreTemplateEncyclopediaEntry> Entries { get; init; }
    public required IReadOnlyList<CoreTemplateEncyclopediaWork> Works { get; init; }
    public required OperationBatch Batch { get; init; }
    public required IReadOnlyDictionary<string, string> SourceFingerprints { get; init; }
    public bool AlreadyInstalled { get; init; }
}

public sealed record CoreTemplateEncyclopediaWork(string Id, string Title, string StagingDirectory, string Fingerprint);

/// <summary>Publishes complete, portable reference Works before one reversible, revision-checked hierarchy change.</summary>
public static class CoreTemplateEncyclopediaInstaller
{
    public const string CollectionId = "core-template-library";
    public const string DemoCollectionId = "demo-works";
    public const string Version = "core-template-encyclopedia-v1";
    private const string MarkerName = ".hyperlib/" + Version + ".json";
    private const string PendingMarkerName = ".hyperlib/" + Version + ".pending.json";
    private static readonly JsonSerializerOptions Json = new(ContentJson.Options) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    public delegate Task PreviewExporter(string libraryRoot, TemplateDesignGroup group, TemplateLibraryObject item, string outputDirectory, CancellationToken cancellationToken);

    public static async Task<CoreTemplateEncyclopediaPlan> PrepareAsync(string libraryRoot, ContentStore store, long expectedRevision,
        PreviewExporter exportPreview, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(exportPreview);
        libraryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        if (!string.Equals(libraryRoot, store.RootPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ContentValidationException("The catalog and content store must belong to the same library.");
        var state = await store.ReadAsync(ct);
        RequireRevision(expectedRevision, state.Revision);
        var marker = store.ResolvePath(MarkerName);
        if (File.Exists(marker))
        {
            var receipt = JsonSerializer.Deserialize<InstallationReceipt>(await File.ReadAllBytesAsync(marker, ct), Json)
                ?? throw new ContentValidationException("The catalog installation receipt is empty.");
            if (receipt.Version != Version || receipt.LibraryId != state.LibraryId)
                throw new ContentValidationException("The catalog installation receipt belongs to another library.");
            if (!state.Nodes.Any(n => n.Id == CollectionId && n.Type == "collection"))
                throw new ContentValidationException("The catalog installation was undone. Use Redo to restore its complete hierarchy.");
            return new() { LibraryRoot = libraryRoot, ExpectedRevision = state.Revision, Entries = [], Works = [], SourceFingerprints = new Dictionary<string, string>(),
                Batch = new() { ExpectedRevision = state.Revision }, AlreadyInstalled = true };
        }
        if (state.Nodes.Any(n => n.Id is CollectionId or DemoCollectionId))
        {
            var pendingPath = store.ResolvePath(PendingMarkerName);
            if (File.Exists(pendingPath))
            {
                var pending = JsonSerializer.Deserialize<InstallationReceipt>(await File.ReadAllBytesAsync(pendingPath, ct), Json);
                if (pending is not null && pending.Version == Version && pending.LibraryId == state.LibraryId && state.Revision >= pending.Revision
                    && state.Nodes.Any(n => n.Id == CollectionId && n.Type == "collection") && state.Nodes.Any(n => n.Id == DemoCollectionId && n.Type == "collection")
                    && pending.WorkIds.All(id => state.Nodes.Any(n => n.Id == id && n.Type == "work" && n.ParentId == CollectionId))
                    && pending.CatalogIds.All(id => state.Pages.TryGetValue(PageId(id), out var page) && pending.WorkIds.Contains(page.WorkId)
                        && page.Blocks.Any(block => block.Type == "text" && block.Values.TryGetValue("text", out var value) && value.GetString() == id)))
                {
                    // The immutable content commit survived; only the following receipt write was interrupted.
                    await WriteAsync(marker, JsonSerializer.Serialize(pending, Json), ct);
                    return new() { LibraryRoot = libraryRoot, ExpectedRevision = state.Revision, Entries = [], Works = [], SourceFingerprints = new Dictionary<string, string>(),
                        Batch = new() { ExpectedRevision = state.Revision }, AlreadyInstalled = true };
                }
            }
            throw new ContentValidationException("A catalog or Demo Works identity already exists without a matching installation receipt. Restore the receipt before installing.");
        }

        var groups = TemplateObjectLibrary.ListDesignGroups(libraryRoot).OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        if (groups.Count == 0) throw new ContentValidationException("The Core Library has no Design Groups to document.");
        var sourceFingerprints = groups.ToDictionary(group => group.ProjectRoot, group => Fingerprint(group.ProjectRoot), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var metadata = Path.Combine(Path.GetDirectoryName(group.ProjectRoot)!, "group.json");
            sourceFingerprints[metadata] = FileHash(metadata);
        }
        var representatives = new Dictionary<string, (TemplateDesignGroup Group, TemplateLibraryObject Item, CoreTemplateEncyclopediaEntry Entry)>(StringComparer.Ordinal);
        var allIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        foreach (var item in TemplateObjectLibrary.ListObjects(libraryRoot, group.Id).OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var catalogId = item.CatalogId;
            TemplateValidator.ValidateId(catalogId, "catalog object");
            var code = ReadCode(group, item);
            var identity = ObjectFingerprint(item, code);
            if (allIds.TryGetValue(catalogId, out var earlierIdentity) && earlierIdentity != identity)
                throw new ContentValidationException("Two different implementations share the catalog object ID " + catalogId + ". Give the changed module its own ID before publishing.");
            allIds[catalogId] = identity;
            var origin = new CoreTemplateEncyclopediaOrigin(catalogId, group.Id, item.Id, item.SourceTemplateId ?? group.SourceTemplateId ?? group.Id,
                group.Title, item.SourceLibrary);
            if (representatives.TryGetValue(identity, out var existing)) existing.Entry.Origins.Add(origin);
            else representatives.Add(identity, (group, item, new()
            {
                CatalogId = catalogId, Title = item.Title, Description = item.Description, Category = item.Category, Kind = item.Kind,
                SourceFingerprint = identity, Origins = [origin], Code = code
            }));
        }
        // A category can contain several configured examples with the same generic name.
        // Keep those browse rows distinguishable without forcing the reader to open every Page.
        foreach (var duplicates in representatives.Values.Select(value => value.Entry)
            .GroupBy(entry => entry.Category + "\n" + entry.Title, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var entry in duplicates)
                entry.Title += " · " + entry.Origins[0].Title;
        var entries = representatives.Values.Select(value => value.Entry).OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.CatalogId, StringComparer.Ordinal).ToList();
        var oldCollections = state.Nodes.Where(n => n.Type == "collection" && n.ParentId == state.LibraryId).OrderBy(n => n.Order).ThenBy(n => n.Id).ToList();
        var operations = new List<ContentOperation>
        {
            new() { Kind = "createNode", Id = CollectionId, NodeType = "collection", ParentId = state.LibraryId, Title = "Core Template Library", Order = 0 },
            new() { Kind = "createNode", Id = DemoCollectionId, NodeType = "collection", ParentId = state.LibraryId, Title = "Demo Works", Order = 1 }
        };
        foreach (var node in oldCollections) operations.Add(new() { Kind = "moveNode", Id = node.Id, ParentId = DemoCollectionId });
        var works = new List<CoreTemplateEncyclopediaWork>();
        var stagedByCategory = new Dictionary<string, string>(StringComparer.Ordinal);
        var definition = Definition();
        var categories = entries.Select(e => e.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (entries.Count + categories.Count + operations.Count > 2000)
            throw new ContentValidationException("This catalog exceeds the atomic content batch limit of 2,000 operations. Split its publication explicitly before installing.");
        var attempt = Guid.NewGuid().ToString("N");
        foreach (var category in categories)
        {
            var id = "hlc-category-" + Hash(Encoding.UTF8.GetBytes(category))[..20].ToLowerInvariant();
            if (state.Nodes.Any(n => n.Id == id) || Directory.Exists(store.ResolvePath("Works/" + id)))
                throw new ContentValidationException("The catalog category has retained source files. Restore or review that Work before installing: " + category);
            var staging = store.ResolvePath("Works/.pending-" + attempt + "-" + id);
            await ProjectScaffolder.EnsureAsync(libraryRoot, staging, id, category, ct, ReusableTemplateCatalog.BlankId);
            await WriteAsync(Path.Combine(staging, "authoring.template.json"), JsonSerializer.Serialize(definition, Json), ct);
            await WriteAsync(Path.Combine(staging, "src/Pages/Blocks/CatalogEntryBlock.razor"), CoreTemplateEncyclopediaRenderer.Component, ct);
            await WriteAsync(Path.Combine(staging, "src/Pages/Blocks/CatalogEntryBlock.razor.css"), CoreTemplateEncyclopediaRenderer.Styles, ct);
            stagedByCategory.Add(category, staging);
            operations.Add(new() { Kind = "createNode", Id = id, NodeType = "work", ParentId = CollectionId, Title = category,
                StarterId = ReusableTemplateCatalog.BlankId, Order = categories.IndexOf(category) });
            var pageOrder = 0;
            foreach (var entry in entries.Where(entry => entry.Category == category))
            {
                var pageId = PageId(entry.CatalogId);
                var page = new PageDocument { Id = pageId, WorkId = id, Title = entry.Title, TemplateId = definition.TemplateId, UsesEditableChrome = true,
                    Blocks = [new() { Id = "catalog-entry", Type = "text", Region = "main", Values = new() { ["text"] = ContentJson.Value(entry.CatalogId) } }] };
                TemplateValidator.ValidatePage(definition, page);
                operations.Add(new() { Kind = "createNode", Id = pageId, NodeType = "page", ParentId = id, Title = entry.Title, Order = pageOrder++, Page = page });
                await WriteAsync(Path.Combine(staging, "src/Host/wwwroot/catalog", entry.CatalogId, "entry.json"), JsonSerializer.Serialize(entry, Json), ct);
            }
        }
        // Consecutive objects from one group allow the exporter to reuse its compiled renderer.
        foreach (var representative in representatives.Values.OrderBy(value => value.Group.Id, StringComparer.Ordinal).ThenBy(value => value.Item.Id, StringComparer.Ordinal))
        {
            var preview = Path.Combine(stagedByCategory[representative.Entry.Category], "src/Host/wwwroot/catalog", representative.Entry.CatalogId, "preview");
            await exportPreview(libraryRoot, representative.Group, representative.Item, preview, ct);
            LibraryPathGuard.Within(libraryRoot, preview);
            var index = Path.Combine(preview, "index.html");
            if (!File.Exists(index) || new FileInfo(index).Length == 0)
                throw new ContentValidationException("The actual visual preview was not exported for " + representative.Entry.Title + ".");
        }
        CheckSources(sourceFingerprints);
        foreach (var category in categories)
        {
            var staging = stagedByCategory[category];
            var payload = LibraryPathGuard.Files(staging).Where(path => Path.GetRelativePath(staging, path).Replace('\\', '/').StartsWith("src/", StringComparison.Ordinal)).ToList();
            if (payload.Count > 8500 || payload.Sum(path => new FileInfo(path).Length) > 112L * 1024 * 1024)
                throw new ContentValidationException("The category '" + category + "' is too large for its renderer. Reduce duplicated preview assets before publishing.");
            var id = operations.Single(op => op.NodeType == "work" && op.Title == category).Id!;
            works.Add(new(id, category, staging, Fingerprint(staging)));
        }
        return new() { LibraryRoot = libraryRoot, ExpectedRevision = expectedRevision, Entries = entries, Works = works,
            SourceFingerprints = sourceFingerprints, Batch = new() { ExpectedRevision = expectedRevision, Description = "Publish Core Template Library and organize existing Collections under Demo Works", Operations = operations } };
    }

    public static async Task<LibraryState> CommitAsync(CoreTemplateEncyclopediaPlan plan, ContentStore store, CancellationToken ct = default)
    {
        if (!Path.GetFullPath(plan.LibraryRoot).Equals(Path.GetFullPath(store.RootPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ContentValidationException("This catalog plan belongs to another library.");
        var before = await store.ReadAsync(ct);
        RequireRevision(plan.ExpectedRevision, before.Revision);
        if (plan.AlreadyInstalled) return before;
        CheckSources(plan.SourceFingerprints);
        var receipt = new InstallationReceipt(Version, before.LibraryId, before.Revision + 1, plan.Entries.Select(e => e.CatalogId).ToList(), plan.Works.Select(w => w.Id).ToList());
        await WriteAsync(store.ResolvePath(PendingMarkerName), JsonSerializer.Serialize(receipt, Json), ct);
        var moved = new List<CoreTemplateEncyclopediaWork>();
        LibraryState? committed = null;
        try
        {
            foreach (var work in plan.Works)
            {
                ct.ThrowIfCancellationRequested();
                LibraryPathGuard.Within(plan.LibraryRoot, work.StagingDirectory);
                if (Fingerprint(work.StagingDirectory) != work.Fingerprint) throw new ContentValidationException("A prepared catalog Work changed. Prepare the catalog again.");
                var destination = store.ResolvePath("Works/" + work.Id);
                if (Directory.Exists(destination) || File.Exists(destination)) throw new ContentValidationException("The catalog destination now exists. Prepare the catalog again.");
                Directory.Move(work.StagingDirectory, destination);
                moved.Add(work);
            }
            CheckSources(plan.SourceFingerprints);
            committed = await store.ApplyAsync(plan.Batch, ct);
        }
        catch
        {
            // Roll back only directories this attempt published and only while their exact bytes remain unchanged.
            // The journal is the authority if a cancellation happened after committing content.
            var current = await store.ReadAsync(CancellationToken.None);
            if (!current.Nodes.Any(n => n.Id == CollectionId))
                foreach (var work in moved.AsEnumerable().Reverse())
                {
                    var destination = store.ResolvePath("Works/" + work.Id);
                    if (Directory.Exists(destination) && !Directory.Exists(work.StagingDirectory) && Fingerprint(destination) == work.Fingerprint)
                        Directory.Move(destination, work.StagingDirectory);
                }
            throw;
        }
        await WriteAsync(store.ResolvePath(MarkerName), JsonSerializer.Serialize(receipt, Json), CancellationToken.None);
        return committed;
    }

    public static async Task<LibraryState> InstallAsync(string libraryRoot, ContentStore store, long expectedRevision, PreviewExporter exportPreview, CancellationToken ct = default)
        => await CommitAsync(await PrepareAsync(libraryRoot, store, expectedRevision, exportPreview, ct), store, ct);

    public static TemplateDefinition Definition()
    {
        var definition = ReusableTemplateCatalog.CreateBuiltInDefinition(ReusableTemplateCatalog.BlankId);
        var text = definition.Blocks.Single(b => b.Id == "text");
        text.Title = "Catalog entry";
        text.Component = "CatalogEntryBlock";
        text.Fields = [new() { Id = "text", Title = "Reference ID", Type = "string", Required = true }];
        definition.Title = "Core Template Library reference";
        definition.Blocks = [text];
        definition.Regions = [new() { Id = "main", Title = "Entry", AllowedBlocks = ["text"] }];
        definition.DefaultBlocks = [];
        definition.EmptyPageDefault = true;
        TemplateValidator.ValidateDefinition(definition);
        return definition;
    }

    private static List<CoreTemplateEncyclopediaCode> ReadCode(TemplateDesignGroup group, TemplateLibraryObject item)
    {
        var files = LibraryPathGuard.Files(group.ProjectRoot).Order(StringComparer.Ordinal).ToList();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var referencedTypes = item.Blocks.Select(b => b.Type).ToHashSet(StringComparer.Ordinal);
        var componentNames = new Queue<string>(item.Element is null
            ? item.Definition.Blocks.Where(b => referencedTypes.Contains(b.Id)).Select(b => b.Component.Split('.').Last())
            : new[] { item.Element.Component.Split('.').Last() });
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (componentNames.TryDequeue(out var name))
        {
            if (!seen.Add(name)) continue;
            foreach (var file in files.Where(path => Path.GetFileName(path) == name + ".razor" || Path.GetFileName(path).StartsWith(name + ".razor.", StringComparison.Ordinal)))
            {
                selected.Add(file);
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"<([A-Z][A-Za-z0-9_]*)\b")) componentNames.Enqueue(match.Groups[1].Value);
            }
        }
        // Shared styles accompany visual modules. Scripts are included only when referenced by their component source.
        var componentSource = string.Join('\n', selected.Select(File.ReadAllText));
        foreach (var file in files.Where(path => path.Replace('\\', '/').Contains("/wwwroot/", StringComparison.Ordinal)))
        {
            var extension = Path.GetExtension(file);
            if (extension == ".css" && (item.Element is not null || item.Kind is "layout" or "style")
                || extension is ".js" or ".mjs" && componentSource.Contains(Path.GetFileName(file), StringComparison.Ordinal)) selected.Add(file);
        }
        var result = new List<CoreTemplateEncyclopediaCode>();
        foreach (var file in selected.Order(StringComparer.Ordinal))
        {
            if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new ContentValidationException("Catalog source file exceeds 2 MiB: " + Path.GetFileName(file));
            result.Add(new(Path.GetRelativePath(group.ProjectRoot, file).Replace('\\', '/'), Path.GetExtension(file).TrimStart('.'), File.ReadAllText(file)));
        }
        var payload = new { item.Kind, Element = item.Element is null ? null : new { item.Element.Id, item.Element.Component, item.Element.Kind, item.Element.Fields },
            item.Blocks, item.Regions, item.Appearance };
        result.Insert(0, new("object-definition.json", "json", JsonSerializer.Serialize(payload, Json)));
        if (result.Sum(source => Encoding.UTF8.GetByteCount(source.Text)) > 8 * 1024 * 1024)
            throw new ContentValidationException("The catalog documentation exceeds 8 MiB for " + item.Title + ". Split its source into smaller modules.");
        return result;
    }

    private static string ObjectFingerprint(TemplateLibraryObject item, List<CoreTemplateEncyclopediaCode> code)
    {
        // Group names and catalog IDs are provenance, not implementation. Preset values and styles remain significant.
        var payload = new { item.Kind, item.Category, Element = item.Element is null ? null : new { item.Element.Component, item.Element.Kind, item.Element.Fields },
            Blocks = item.Blocks.Select(b => new { b.Type, b.Region, b.Values }), item.Regions, item.Appearance,
            ReferencedElements = item.Element is null && item.Kind is "region" or "layout" ? item.Definition.Blocks.Select(b => new { b.Id, b.Component, b.Kind, b.Fields }).ToArray() : null,
            Code = code.Where(c => c.Path != "object-definition.json").Select(c => new { c.Path, Text = c.Text.Replace("\r\n", "\n", StringComparison.Ordinal) }) };
        return Hash(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
    }

    private static void RequireRevision(long expected, long actual) { if (expected != actual) throw new ConflictException(expected, actual); }
    private static string PageId(string catalogId) => "hlc-entry-" + Hash(Encoding.UTF8.GetBytes(catalogId))[..24].ToLowerInvariant();
    private static void CheckSources(IReadOnlyDictionary<string, string> fingerprints)
    {
        foreach (var pair in fingerprints)
            if ((Directory.Exists(pair.Key) ? Fingerprint(pair.Key) : File.Exists(pair.Key) ? FileHash(pair.Key) : "") != pair.Value)
                throw new ContentValidationException("A source Design Group changed. Prepare the catalog again.");
    }
    private static string Fingerprint(string directory) => Hash(Encoding.UTF8.GetBytes(string.Join('\n', LibraryPathGuard.Files(directory)
        .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') + ":" + FileHash(path)).Order(StringComparer.Ordinal))));
    private static string FileHash(string path) { LibraryPathGuard.NoLinks(path); return Hash(File.ReadAllBytes(path)); }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static Task WriteAsync(string path, string contents, CancellationToken ct)
        => LibraryPathGuard.ReplaceAsync(path, Encoding.UTF8.GetBytes(contents), ct);
    private sealed record InstallationReceipt(string Version, string LibraryId, long Revision, List<string> CatalogIds, List<string> WorkIds);
}
