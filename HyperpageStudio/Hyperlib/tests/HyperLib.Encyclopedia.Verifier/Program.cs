using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using HyperLib.Core;
using HyperLib.Runtime;
using HyperLib.Archives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
var checks = new List<string>();
void Check(bool result, string name) { if (!result) throw new InvalidOperationException(name); checks.Add(name); }
string Json<T>(T value) => JsonSerializer.Serialize(value, ContentJson.Options);
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
async Task Throws<T>(Func<Task> action, string name) where T : Exception { try { await action(); } catch (T) { Check(true, name); return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + name); }
void Copy(string from, string to)
{
    if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new IOException("Fixture source contains a link.");
    Directory.CreateDirectory(to);
    foreach (var file in Directory.EnumerateFiles(from))
    {
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Fixture source contains a link.");
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }
    foreach (var directory in Directory.EnumerateDirectories(from))
        if (Path.GetFileName(directory) is not ("bin" or "obj" or ".git" or ".hyperlib" or "node_modules")) Copy(directory, Path.Combine(to, Path.GetFileName(directory)));
}
Copy(Path.Combine(source, "src/HyperLib.Core"), Path.Combine(root, "src/HyperLib.Core"));
Copy(Path.Combine(source, "src/HyperLib.Runtime"), Path.Combine(root, "src/HyperLib.Runtime"));
Copy(Path.Combine(source, "Starters"), Path.Combine(root, "Starters"));
var store = new ContentStore(root);
await store.InitializeAsync();
var original = await store.ReadAsync();
await using var runtime = new HyperLibRuntimeSupervisor(root, watchSources: false);
await runtime.EnsureProjectsAsync(original);
var sourceHashes = Directory.GetFiles(Path.Combine(root, "Works"), "*", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "Content" + Path.DirectorySeparatorChar)).ToDictionary(p => p, Hash);
var pageBytes = original.Pages.ToDictionary(p => p.Key, p => Json(p.Value));
foreach (var id in new[] { "test-a", "test-b", "test-c" })
{
    var project = Path.Combine(root, ".hyperlib", "test-groups", id);
    await ProjectScaffolder.EnsureAsync(root, project, id, id, starterId: ReusableTemplateCatalog.BlankId);
    var definition = ReusableTemplateCatalog.CreateBuiltInDefinition(ReusableTemplateCatalog.BlankId);
    definition.Blocks = [definition.Blocks.Single(b => b.Id == "text")];
    definition.Regions = [new() { Id = "main", Title = "Main", AllowedBlocks = ["text"] }];
    definition.Appearance = [];
    definition.DefaultBlocks = [];
    if (id == "test-c") definition.Blocks[0].Fields.Single(f => f.Id == "text").DefaultValue = ContentJson.Value("A distinct useful default");
    await File.WriteAllTextAsync(Path.Combine(project, "authoring.template.json"), Json(definition));
    await TemplateObjectLibrary.RegisterDesignGroupAsync(root, project, id, "Origin " + id, "Verifier origin", "Verifier");
}
var exportCalls = 0;
// This controlled exporter verifies publication/staging, not visual fidelity. The production exporter has its own real-Razor checks.
Task Export(string library, TemplateDesignGroup group, TemplateLibraryObject item, string directory, CancellationToken ct)
{
    exportCalls++;
    Directory.CreateDirectory(directory);
    return File.WriteAllTextAsync(Path.Combine(directory, "index.html"), "<!doctype html><html><body><p>Encyclopedia pipeline fixture</p></body></html>", ct);
}
var stale = await CoreTemplateEncyclopediaInstaller.PrepareAsync(root, store, original.Revision, Export);
Check((await store.ReadAsync()).Revision == original.Revision, "Preparing catalog does not mutate content");
Check(stale.Entries.Count < TemplateObjectLibrary.ListDesignGroups(root).Sum(g => TemplateObjectLibrary.ListObjects(root, g.Id).Count), "Identical implementations share one entry");
Check(stale.Entries.Any(e => e.Origins.Count > 1), "Deduplicated entry retains every origin and alias ID");
Check(stale.Entries.Count(e => e.Kind == "element") == 2, "Different field defaults retain distinct modules");
var current = await store.ApplyAsync(new() { ExpectedRevision = original.Revision, Operations = [new() { Kind = "updateNode", Id = original.Nodes.First().Id, Title = "An owner edit" }] });
await Throws<ConflictException>(() => CoreTemplateEncyclopediaInstaller.CommitAsync(stale, store), "Stale content revision rejects publication");
Check(stale.Works.All(w => !Directory.Exists(Path.Combine(root, "Works", w.Id))), "Stale publication writes no category destinations");
original = current;
pageBytes = original.Pages.ToDictionary(p => p.Key, p => Json(p.Value));
var plan = await CoreTemplateEncyclopediaInstaller.PrepareAsync(root, store, current.Revision, Export);
Check(plan.Entries.Select(e => e.CatalogId).Distinct().Count() == plan.Entries.Count, "Every documented object has a unique stable ID");
Check(plan.Works.Select(w => w.Title).SequenceEqual(plan.Works.Select(w => w.Title).Order(StringComparer.OrdinalIgnoreCase)), "Category Works are alphabetical");
var sourceFile = Path.Combine(TemplateObjectLibrary.GetDesignGroup(root, "test-a").ProjectRoot, "src/Pages/Blocks/TextBlock.razor");
var sourceBefore = await File.ReadAllTextAsync(sourceFile);
await File.AppendAllTextAsync(sourceFile, "\n<!-- changed -->");
await Throws<ContentValidationException>(() => CoreTemplateEncyclopediaInstaller.CommitAsync(plan, store), "Changed module source blocks prepared publication");
await File.WriteAllTextAsync(sourceFile, sourceBefore);
Check(plan.Works.All(w => !Directory.Exists(Path.Combine(root, "Works", w.Id))), "Source conflict writes no category destinations");
var occupied = Path.Combine(root, "Works", plan.Works.Last().Id);
Directory.CreateDirectory(occupied);
var foreignFile = Path.Combine(occupied, "owner.txt");
await File.WriteAllTextAsync(foreignFile, "Preserve this concurrently created project");
await Throws<ContentValidationException>(() => CoreTemplateEncyclopediaInstaller.CommitAsync(plan, store), "Concurrent destination creation blocks publication");
Check(await File.ReadAllTextAsync(foreignFile) == "Preserve this concurrently created project", "Conflicting destination content is preserved");
Check(Directory.Exists(plan.Works[0].StagingDirectory) && !Directory.Exists(Path.Combine(root, "Works", plan.Works[0].Id)), "Earlier published source directories roll back to staging after conflict");
File.Delete(foreignFile);
Directory.Delete(occupied);
current = await CoreTemplateEncyclopediaInstaller.CommitAsync(plan, store);
Check(current.Revision == original.Revision + 1, "Catalog and hierarchy migration commit as one revision");
Check(current.Pages.Count == original.Pages.Count + plan.Entries.Count, "Every distinct module has one real Page");
Check(original.Nodes.Where(n => n.ParentId == original.LibraryId).All(n => current.Nodes.Single(v => v.Id == n.Id).ParentId == CoreTemplateEncyclopediaInstaller.DemoCollectionId), "All original Collections move intact under Demo Works");
Check(original.Nodes.All(n => current.Nodes.Any(v => v.Id == n.Id && v.Type == n.Type && v.WorkId == n.WorkId && v.ProjectPath == n.ProjectPath)), "Original IDs, types, Work identities and project paths survive");
Check(pageBytes.All(pair => Json(current.Pages[pair.Key]) == pair.Value), "Original Page documents remain byte-equivalent");
Check(sourceHashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value), "All original Work source bytes survive");
ContentRules.Validate(current);
Check(true, "Nested Collections validate through normal content rules");
var savedCalls = exportCalls;
var again = await CoreTemplateEncyclopediaInstaller.InstallAsync(root, store, current.Revision, Export);
Check(again.Revision == current.Revision && exportCalls == savedCalls, "Repeated installation is a no-op and does not regenerate previews");
await Throws<ContentValidationException>(() => store.ApplyAsync(new() { ExpectedRevision = current.Revision, Operations = [new() { Kind = "moveNode", Id = CoreTemplateEncyclopediaInstaller.DemoCollectionId, ParentId = original.Nodes.First(n => n.Type == "collection").Id }] }), "Nested Collection cycles are rejected");
var undone = await store.UndoAsync(current.Revision);
Check(undone.Nodes.Count == original.Nodes.Count && original.Nodes.All(n => undone.Nodes.Single(v => v.Id == n.Id).ParentId == n.ParentId), "One Undo restores original hierarchy");
Check(plan.Works.All(w => File.Exists(Path.Combine(root, "Works", w.Id, "visual-page.project.json"))), "Undo retains complete source projects for recovery");
current = await store.RedoAsync(undone.Revision);
Check(current.Nodes.Count == again.Nodes.Count && current.Pages.Count == again.Pages.Count, "Redo restores all catalog Works and Pages");
Check((await new ContentStore(root).ReadAsync()).Revision == current.Revision, "Cold content store reload validates committed hierarchy");
var marker = Path.Combine(root, ".hyperlib", CoreTemplateEncyclopediaInstaller.Version + ".json");
File.Move(marker, marker + ".saved");
again = await CoreTemplateEncyclopediaInstaller.InstallAsync(root, store, current.Revision, Export);
Check(again.Revision == current.Revision && File.Exists(marker), "Interrupted final receipt write recovers without a second content commit");

var publishingWork = plan.Works.Single(w => w.Title == "Text & content");
var publishingPages = current.Pages.Values.Where(p => p.WorkId == publishingWork.Id).OrderBy(p => p.Id).ToList();
var selectedId = publishingPages[0].Blocks[0].Values["text"].GetString()!;
var excludedId = publishingPages[1].Blocks[0].Values["text"].GetString()!;
var assetRoot = Path.Combine(root, "Works", publishingWork.Id, "src/Host/wwwroot/catalog", selectedId);
await File.WriteAllTextAsync(Path.Combine(assetRoot, "preview", "example.css"), "body{color:#123456}");
var archives = new ArchivesPublicationService(root, store);
var draft = await archives.CreateDraftAsync(publishingWork.Id, current.Revision);
draft = await archives.UpdateSelectionAsync(draft.Id, draft.DraftRevision, [publishingPages[0].Id]);
var package = await archives.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash);
using (var zip = ZipFile.OpenRead(package.PackagePath))
{
    var names = zip.Entries.Select(e => e.FullName).ToList();
    var prefix = "Works/" + publishingWork.Id + "/src/Host/wwwroot/catalog/";
    Check(names.Contains(prefix + selectedId + "/entry.json") && names.Contains(prefix + selectedId + "/preview/index.html") && names.Contains(prefix + selectedId + "/preview/example.css"), "Archives includes selected entry metadata, HTML preview and static dependencies");
    Check(!names.Any(name => name.StartsWith(prefix + excludedId + "/", StringComparison.Ordinal)), "Archives excludes unchecked catalog entry assets");
    Check(!names.Any(name => name.EndsWith("src/Host/Program.cs", StringComparison.Ordinal)), "Catalog asset export does not upload host startup code");
    foreach (var name in names.Where(name => name != "manifest.json")) ArchivesStore.ValidatePackagePath(name);
    Check(true, "Receiver allowlist accepts the complete selected catalog package");
}
foreach (var invalid in new[] { "Works/demo/src/Host/Program.cs", "Works/demo/src/Host/wwwroot/catalog/HLC-A/code.cs", "Works/demo/src/Host/wwwroot/catalog/HLC-A/view.razor",
    "Works/demo/src/Host/wwwroot/catalog/HLC-A/../../Program.cs", "Works/demo/src/Host/wwwroot/catalog/HLC-A/program.dll", "Works/demo/src/Host/wwwroot/catalog/HLC-A/object.csproj" })
    await Throws<ContentValidationException>(() => { ArchivesStore.ValidatePackagePath(invalid); return Task.CompletedTask; }, "Receiver rejects unsafe catalog path: " + Path.GetFileName(invalid));
var unsafeAsset = Path.Combine(assetRoot, "preview", "download.exe");
await File.WriteAllTextAsync(unsafeAsset, "not executable; verifier sentinel");
await Throws<ContentValidationException>(() => archives.CreateDraftAsync(publishingWork.Id, current.Revision), "Catalog export rejects executable asset extensions");
File.Delete(unsafeAsset);
var tooLarge = Path.Combine(assetRoot, "preview", "oversized.png");
await using (var oversized = new FileStream(tooLarge, FileMode.CreateNew, FileAccess.Write)) oversized.SetLength(32L * 1024 * 1024 + 1);
await Throws<ContentValidationException>(() => archives.CreateDraftAsync(publishingWork.Id, current.Revision), "Catalog export rejects an oversized entry before copying bytes");
File.Delete(tooLarge);
await File.AppendAllTextAsync(Path.Combine(assetRoot, "preview", "example.css"), "\nbody{background:white}");
Check((await archives.GetDraftAsync(draft.Id)).Stale, "Changed catalog preview assets invalidate a reviewed publication");

if (args.Contains("--render"))
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
    await using var gateway = builder.Build();
    gateway.MapGet("/api/state", () => Results.Json(current, ContentJson.Options));
    gateway.MapGet("/api/renderer/page/{id}", (string id) => Results.Json(current.Pages[id], ContentJson.Options));
    await gateway.StartAsync();
    await runtime.EnsureProjectsAsync(current);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    foreach (var work in plan.Works)
    {
        var generation = await runtime.EnsureRuntimeAsync(work.Id, "http://127.0.0.1:" + port);
        Check(generation.ProxyPrefix is not null, "Actual catalog Work renderer builds: " + work.Title);
        var page = current.Pages.Values.First(p => p.WorkId == work.Id);
        var body = await http.GetStringAsync(generation.UpstreamUrl + generation.ProxyPrefix + "page/" + page.Id);
        await File.WriteAllTextAsync(Path.Combine(output, work.Id + ".html"), body);
        Check(body.Contains("From template") && body.Contains("Reference") && body.Contains("<details") && body.Contains(" sandbox"), "Actual Page renders metadata, collapsed code and isolated preview: " + work.Title);
        Check(body.Contains("&quot;kind&quot;") && !body.Contains("This component could not render") && (work.Title != "Text & content" || body.Contains("&lt;p")), "Actual object code is escaped rather than executed: " + work.Title);
        var catalogId = page.Blocks[0].Values["text"].GetString();
        var preview = await http.GetStringAsync(generation.UpstreamUrl + generation.ProxyPrefix + "catalog/" + catalogId + "/preview/index.html");
        Check(preview.Contains("Encyclopedia pipeline fixture"), "Bundled preview survives real renderer publish: " + work.Title);
        await runtime.ReleaseRuntimeAsync(work.Id);
    }
    await gateway.StopAsync();
}
var result = new { status = "HYPERLIB_ENCYCLOPEDIA_OK", assertions = checks.Count, checks, fixture = root, entryCount = plan.Entries.Count,
    origins = plan.Entries.Sum(e => e.Origins.Count), categories = plan.Works.Count, revision = current.Revision, rendererVerified = args.Contains("--render") };
await File.WriteAllTextAsync(Path.Combine(output, "encyclopedia-verification.json"), Json(result));
Console.WriteLine(Json(result));
