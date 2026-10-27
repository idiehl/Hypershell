using System.Text.Json;
using HyperLib.Core;
using HyperLib.Runtime;
using Hypershell.Services;

if (args.Length < 2) throw new ArgumentException("Usage: CatalogTool seed|install|inventory <libraryRoot> [expectedRevision]");
var root = Path.GetFullPath(args[1]);
if (!Directory.Exists(Path.Combine(root, "Starters", "standard"))) throw new DirectoryNotFoundException("A complete Hyperlib root is required.");
if (args[0] == "verify-legacy")
{
    if (args.Length < 3) throw new ArgumentException("A separate verifier output directory is required.");
    await HyperlibLegacyTemplateLibraryVerifier.VerifyAsync(args[2], root);
    return;
}
if (args[0] == "preview")
{
    if (args.Length < 5) throw new ArgumentException("Preview requires group ID, object ID, and output directory.");
    using var exporter = new HyperlibCatalogPreviewExporter(Path.Combine(Path.GetDirectoryName(root)!, "catalog-preview-cache"));
    var group = TemplateObjectLibrary.GetDesignGroup(root, args[2]);
    var item = TemplateObjectLibrary.ListObjects(root, group.Id).Single(item=>item.Id==args[3]);
    await exporter.ExportAsync(root, group, item, Path.GetFullPath(args[4]), CancellationToken.None);
    Console.WriteLine("CATALOG_PREVIEW_OK " + item.CatalogId);
    return;
}
if (args[0] == "seed")
{
    await TemplateObjectLibrary.EnsureCoreLibraryAsync(root);
    await HyperlibFoundationCatalog.EnsureAsync(root);
    await HyperlibLegacyTemplateLibraryService.EnsureLegacyDesignGroupsAsync(root);
    await HyperlibAtlasWebsiteCatalog.EnsureAsync(root);
}
else if (args[0] is "install" or "install-prebuilt")
{
    if (args.Length < 3 || !long.TryParse(args[2], out var revision)) throw new ArgumentException("An explicit expected revision is required.");
    var prebuilt = args[0] == "install-prebuilt" ? args.Length > 3 ? Path.GetFullPath(args[3]) : throw new ArgumentException("A verified reference library is required.") : null;
    var previewDirectories = prebuilt is null ? null : Directory.EnumerateDirectories(Path.Combine(prebuilt, "Works"), "hlc-category-*")
        .SelectMany(work => Directory.EnumerateDirectories(Path.Combine(work, "src", "Host", "wwwroot", "catalog")))
        .ToDictionary(path=>Path.GetFileName(path), StringComparer.Ordinal);
    using var exporter = new HyperlibCatalogPreviewExporter(Path.Combine(Path.GetDirectoryName(root)!, "catalog-preview-cache"));
    var store = new ContentStore(root);
    var result = await CoreTemplateEncyclopediaInstaller.InstallAsync(root, store, revision,
        async (library, group, item, directory, ct) => {
            Console.WriteLine($"PREVIEW {item.CatalogId} {group.Title} / {item.Title}");
            if (previewDirectories is null) await exporter.ExportAsync(library, group, item, directory, ct);
            else
            {
                if (!previewDirectories.TryGetValue(item.CatalogId, out var source)) throw new InvalidDataException("Verified preview missing: " + item.CatalogId);
                var expected = JsonSerializer.Deserialize<CoreTemplateEncyclopediaEntry>(await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(directory)!, "entry.json"), ct), ContentJson.Options)!;
                var verified = JsonSerializer.Deserialize<CoreTemplateEncyclopediaEntry>(await File.ReadAllBytesAsync(Path.Combine(source, "entry.json"), ct), ContentJson.Options)!;
                if (expected.SourceFingerprint != verified.SourceFingerprint || expected.CatalogId != verified.CatalogId)
                    throw new InvalidDataException("The object changed since its preview was verified: " + item.CatalogId);
                var previewRoot = Path.Combine(source, "preview");
                foreach (var file in Directory.EnumerateFiles(previewRoot, "*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked preview files are not supported.");
                    var destination = Path.Combine(directory, Path.GetRelativePath(previewRoot, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var input = File.OpenRead(file);
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                    await input.CopyToAsync(output, ct);
                }
            }
        });
    Console.WriteLine(JsonSerializer.Serialize(new { result.Revision, nodes=result.Nodes.Count, pages=result.Pages.Count }, ContentJson.Options));
}
else if (args[0] != "inventory") throw new ArgumentException("Unknown command.");
var groups = TemplateObjectLibrary.ListDesignGroups(root);
var objects = groups.SelectMany(g => TemplateObjectLibrary.ListObjects(root, g.Id)).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new { groups = groups.Count, entries = objects.Length, elementDefinitions = objects.Count(x=>x.Kind == "element"),
    kinds = objects.GroupBy(x=>x.Kind).ToDictionary(g=>g.Key,g=>g.Count()), categories = objects.GroupBy(x=>x.Category).ToDictionary(g=>g.Key,g=>g.Count()) }, ContentJson.Options));
