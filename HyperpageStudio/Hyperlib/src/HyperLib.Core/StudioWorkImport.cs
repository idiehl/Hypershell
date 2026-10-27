using System.Security.Cryptography;
using System.Text.Json;

namespace HyperLib.Core;

/// <summary>A reviewed immutable Work branch and its owned code/media; no destination data is removed.</summary>
public sealed class StudioWorkImportSnapshot
{
    public string WorkId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public List<LibraryNode> Nodes { get; init; } = [];
    public Dictionary<string, PageDocument> Pages { get; init; } = new(StringComparer.Ordinal);
    public TemplateDefinition Template { get; init; } = new();
    public List<StudioWorkImportFile> Files { get; init; } = [];
}

public sealed class StudioWorkImportFile
{
    public string RelativePath { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public byte[] Bytes { get; init; } = [];
}

public sealed partial class ContentStore
{
    /// <summary>Publish one new Work under an atomic, revision checked normal journal transaction.</summary>
    public Task<LibraryState> ImportWorkSnapshotAsync(StudioWorkImportSnapshot snapshot, long expectedRevision,
        CancellationToken cancellationToken = default)
        => WithLockAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var loaded = await LoadOrInitializeAsync(cancellationToken);
            CheckRevision(expectedRevision, loaded.State.Revision);
            ContentRules.ValidateNodeId(snapshot.WorkId);
            ContentRules.ValidateTitle(snapshot.Title);
            TemplateValidator.ValidateDefinition(snapshot.Template);
            TemplateValidator.Require(snapshot.Nodes.Count is > 0 and <= 10000 && snapshot.Files.Count is > 0 and <= 10000,
                "The publication needs a complete Work branch and its owned source files.");
            var branch = ContentJson.Clone(snapshot.Nodes);
            var work = branch.SingleOrDefault(node => node.Id == snapshot.WorkId && node.Type == "work")
                ?? throw new ContentValidationException("The publication snapshot has no matching Work.");
            TemplateValidator.Require(branch.All(node => node.Id == work.Id || node.WorkId == work.Id), "Only this Work and its descendants can be published.");
            TemplateValidator.Require(branch.Count(node => node.Type == "work") == 1, "Publish one Work at a time.");
            var existingIds = loaded.State.Nodes.Select(node => node.Id).Append(loaded.State.LibraryId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            TemplateValidator.Require(branch.All(node => !existingIds.Contains(node.Id)),
                "This Work or one of its stable IDs is already present in Hyperlib. The existing publication is preserved. Publish to another library or create a separately identified Work.");
            var workRelative = "Works/" + snapshot.WorkId;
            var workRoot = ResolvePath(workRelative);
            TemplateValidator.Require(!Directory.Exists(workRoot) && !File.Exists(workRoot), "Hyperlib already has source files for this Work. No existing source project will be overwritten.");
            TemplateValidator.Require(snapshot.Files.Any(file => file.RelativePath == workRelative + "/authoring.template.json"), "The Work's editable template must be included.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            foreach (var file in snapshot.Files)
            {
                TemplateValidator.Require(file.RelativePath.StartsWith(workRelative + "/", StringComparison.Ordinal)
                    || file.RelativePath.StartsWith("Media/", StringComparison.Ordinal), "Publication files must belong to this Work or its referenced media.");
                _ = ResolvePath(file.RelativePath);
                TemplateValidator.Require(paths.Add(file.RelativePath) && (bytes += file.Bytes.LongLength) <= 512L * 1024 * 1024,
                    "Publication files are duplicated or exceed the 512 MiB safety limit.");
                TemplateValidator.Require(Convert.ToHexStringLower(SHA256.HashData(file.Bytes)) == file.Sha256, "A reviewed publication file failed its checksum.");
            }
            var sourceTemplate = JsonSerializer.Deserialize<TemplateDefinition>(snapshot.Files.Single(file => file.RelativePath == workRelative + "/authoring.template.json").Bytes, ContentJson.Options);
            TemplateValidator.Require(sourceTemplate is not null && JsonSerializer.Serialize(sourceTemplate, ContentJson.Options) == JsonSerializer.Serialize(snapshot.Template, ContentJson.Options),
                "The reviewed Page template does not match the Work's copied source contract.");
            const string collectionId = "studio-publications";
            var next = ContentJson.Clone(loaded.State);
            var collection = next.Nodes.SingleOrDefault(node => node.Id == collectionId);
            if (collection is null)
            {
                TemplateValidator.Require(next.LibraryId != collectionId && !branch.Any(node => node.Id == collectionId), "The Studio publication collection ID is already in use.");
                next.Nodes.Add(new() { Id = collectionId, Type = "collection", ParentId = next.LibraryId, Title = "Studio publications",
                    Order = next.Nodes.Where(node => node.ParentId == next.LibraryId).Select(node => node.Order).DefaultIfEmpty(-1).Max() + 1 });
            }
            else TemplateValidator.Require(collection.Type == "collection" && collection.ParentId == next.LibraryId && collection.Title == "Studio publications",
                "The Studio publications collection ID belongs to another object. No hierarchy was changed.");
            work.ParentId = collectionId;
            work.Order = next.Nodes.Where(node => node.ParentId == collectionId).Select(node => node.Order).DefaultIfEmpty(-1).Max() + 1;
            next.Nodes.AddRange(branch);
            foreach (var pair in snapshot.Pages)
            {
                TemplateValidator.Require(branch.Any(node => node.Id == pair.Key && node.Type == "page"), "A publication Page must have a node inside this Work.");
                next.Pages.Add(pair.Key, ContentJson.Clone(pair.Value));
            }
            ContentRules.DerivePaths(next);
            SetPageRevisions(loaded.State, next, checked(loaded.State.Revision + 1));
            next.Revision = checked(loaded.State.Revision + 1);
            // Validate only the imported Work against its reviewed source contract; unrelated Works keep their own schemas.
            ContentRules.Validate(next, id => id == snapshot.WorkId ? snapshot.Template : null);
            var createdFiles = new List<string>();
            try
            {
                // Work files use CreateNew. Content-addressed media may be reused only when byte identical.
                foreach (var file in snapshot.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = ResolvePath(file.RelativePath);
                    if (File.Exists(path))
                    {
                        TemplateValidator.Require(file.RelativePath.StartsWith("Media/", StringComparison.Ordinal)
                            && Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken))) == file.Sha256,
                            "A publication file conflicts with existing Hyperlib source or media. No existing file was overwritten.");
                        continue;
                    }
                    EnsureDirectory(Path.GetDirectoryName(path)!);
                    await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        createdFiles.Add(path); await stream.WriteAsync(file.Bytes, cancellationToken); await stream.FlushAsync(cancellationToken); stream.Flush(true);
                    }
                }
                var receiptPath = ResolvePath(workRelative + "/.studio/publication.json");
                TemplateValidator.Require(!File.Exists(receiptPath), "The imported Work already contains a publication receipt.");
                EnsureDirectory(Path.GetDirectoryName(receiptPath)!);
                await WriteJsonAtomicAsync(receiptPath, new { schemaVersion = 1, source = "Hyperpage Studio", snapshot.Fingerprint,
                    snapshot.WorkId, sourceRevision = snapshot.Pages.Values.Select(page => page.Revision).DefaultIfEmpty(0).Max(), publishedAtUtc = DateTimeOffset.UtcNow,
                    destinationLibraryId = next.LibraryId, destinationRevision = next.Revision }, cancellationToken, overwrite: false);
                createdFiles.Add(receiptPath);
                return await CommitAsync(loaded.State, next, "apply", "Publish from Studio: " + snapshot.Title, null, cancellationToken);
            }
            catch
            {
                // Once the normal journal commit exists, source files are necessary for replay and must remain.
                if (!File.Exists(JournalPath(next.Revision)))
                {
                    foreach (var path in createdFiles.AsEnumerable().Reverse()) if (File.Exists(path)) File.Delete(path);
                    var createdDirectories = createdFiles.SelectMany(path =>
                    {
                        var parents = new List<string>();
                        for (var parent = Path.GetDirectoryName(path); parent is not null && (parent == workRoot || parent.StartsWith(workRoot + Path.DirectorySeparatorChar, PathComparison)); parent = Path.GetDirectoryName(parent)) parents.Add(parent);
                        return parents;
                    }).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).OrderByDescending(path => path.Length);
                    foreach (var directory in createdDirectories)
                    {
                        RejectReparsePoints(directory);
                        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                    }
                }
                throw;
            }
        }, cancellationToken);
}
