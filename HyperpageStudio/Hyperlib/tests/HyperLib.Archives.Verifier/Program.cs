using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;

if (args.Length < 2) throw new ArgumentException("Provide an isolated fixture directory and the source Hyperlib root.");
var output = Path.GetFullPath(args[0]);
var source = Path.GetFullPath(args[1]);
var root = Path.Combine(output, "archives-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException("FAILED: " + description); checks++; }
async Task Reject(Func<Task> operation, string description)
{
    try { await operation(); }
    catch (Exception error) when (error is ContentValidationException or ArchivesDraftConflictException or ConflictException or IOException or OperationCanceledException) { checks++; return; }
    throw new InvalidOperationException("FAILED: expected rejection: " + description);
}
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
void Copy(string input, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(input)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    foreach (var child in Directory.EnumerateDirectories(input).Where(d => Path.GetFileName(d) is not ("bin" or "obj" or ".git")))
        Copy(child, Path.Combine(destination, Path.GetFileName(child)));
}
Copy(Path.Combine(source, "Starters", "standard"), Path.Combine(root, "Starters", "standard"));
Directory.CreateDirectory(Path.Combine(root, "Media"));
foreach (var name in new[] { "woodland.svg", "forest-detail.svg", "sample-tone.wav", "sample-motion.webm" })
    File.Copy(Path.Combine(source, "Media", name), Path.Combine(root, "Media", name));
await File.WriteAllTextAsync(Path.Combine(root, "Media", "private.svg"), "<svg>EXCLUDED_MEDIA_SENTINEL</svg>");
await File.WriteAllTextAsync(Path.Combine(root, "Media", "unreferenced.svg"), "<svg>UNREFERENCED_MEDIA_SENTINEL</svg>");
var initial = SeedLibrary.Create();
initial.Nodes.Add(new() { Id = "private-page", ParentId = "animal-atlas", WorkId = "animal-atlas", Type = "page", Title = "Private page", Order = 1 });
initial.Pages["private-page"] = new()
{
    Id = "private-page", WorkId = "animal-atlas", Title = "Private page",
    Blocks = [new() { Id = "private-text", Type = "text", Values = new() { ["text"] = ContentJson.Value("EXCLUDED_PAGE_SENTINEL") } },
        new() { Id = "private-image", Type = "image", Values = new() { ["src"] = ContentJson.Value("/media/private.svg") } }]
};
Directory.CreateDirectory(Path.Combine(root, "Content"));
await File.WriteAllTextAsync(Path.Combine(root, "Content", "library.json"), JsonSerializer.Serialize(initial, ContentJson.Options));
var store = new ContentStore(root);
await store.InitializeAsync();
await TemplateProjectScaffolder.EnsureAsync(root, root, "library", "HyperLib");
foreach (var work in initial.Nodes.Where(n => n.Type == "work"))
    await TemplateProjectScaffolder.EnsureAsync(root, Path.Combine(root, "Works", work.Id), work.Id, work.Title);
var workRoot = Path.Combine(root, "Works", "animal-atlas");
await File.WriteAllTextAsync(Path.Combine(workRoot, ".env"), "DO_NOT_EXPORT_CREDENTIAL_SENTINEL");
await File.WriteAllTextAsync(Path.Combine(workRoot, "src", "Pages", "appsettings.json"), "{\"key\":\"DO_NOT_EXPORT_CONFIG_SENTINEL\"}");
var counterPath = Path.Combine(workRoot, "src", "Pages", "wwwroot", "blocks", "counter.js");
await File.AppendAllTextAsync(counterPath, "\nimport './counter-helper.js';\n");
await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(counterPath)!, "counter-helper.js"), "export const fixtureHelper = true;\n");
var beforeContent = Hash(await File.ReadAllBytesAsync(Path.Combine(root, "Content", "library.json")));
var service = new ArchivesPublicationService(root, store);
var state = await store.ReadAsync();
var draft = await service.CreateDraftAsync("natural-world", state.Revision);
Check(draft.Nodes.Count == 4 && draft.SelectedNodeIds.Count == 4, "initial branch starts fully selected");
Check(!draft.Stale && Guid.TryParse(draft.PublisherId, out _) && draft.ReviewHash.Length == 64, "draft has stable publisher and reviewed snapshot");
var draftAgain = await new ArchivesPublicationService(root).GetDraftAsync(draft.Id);
Check(draftAgain.PublisherId == draft.PublisherId && draftAgain.ReviewHash == draft.ReviewHash, "draft survives service restart");
Check((await service.GetLatestDraftAsync("natural-world"))?.Id == draft.Id, "latest branch draft can be reopened");
await Reject(() => service.CreateDraftAsync("natural-world", state.Revision + 1), "stale content revision is rejected");
await Reject(() => service.UpdateSelectionAsync(draft.Id, draft.DraftRevision, ["exploring-wave"]), "out-of-branch selection rejected");
draft = await service.UpdateSelectionAsync(draft.Id, draft.DraftRevision, ["woodland-habitat"]);
Check(draft.SelectedNodeIds.SequenceEqual(new[] { "natural-world", "animal-atlas", "woodland-habitat" }), "leaf selection includes ancestors");
Check(!draft.SelectedNodeIds.Contains("private-page"), "excluded sibling stays unchecked");
await Reject(() => service.UpdateSelectionAsync(draft.Id, draft.DraftRevision - 1, []), "draft revision prevents lost selection");
await Reject(() => service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, "wrong"), "unreviewed hash rejected");
var package = await service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash);
var retry = await service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash);
Check(package.PackageSha256 == retry.PackageSha256 && package.ManifestSha256 == retry.ManifestSha256, "identical reviewed export is byte deterministic");
Check(package.Manifest.SyncMode == "upsert", "unchecked published objects are not implicitly deleted");
Check(package.Manifest.WorkIds.SequenceEqual(new[] { "animal-atlas" }), "only selected Work renderer is exported");
using (var zip = ZipFile.OpenRead(package.PackagePath))
{
    var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
    Check(names.Contains("manifest.json") && names.Contains("Content/library.json"), "manifest and selected canonical content present");
    Check(!names.Any(n => n.Contains(".hyperlib") || n.Contains("/Content/") || n.Contains("appsettings") || n.EndsWith(".env")), "private projections history and configuration omitted");
    Check(!names.Contains("Media/private.svg") && !names.Contains("Media/unreferenced.svg"), "excluded and unreferenced media omitted");
    Check(names.Contains("Media/woodland.svg") && names.Contains("Media/forest-detail.svg"), "selected media included");
    Check(names.Contains("Works/animal-atlas/src/Pages/wwwroot/blocks/counter.js")
        && names.Contains("Works/animal-atlas/src/Pages/wwwroot/blocks/counter-helper.js"), "nested JavaScript module dependency included");
    Check(!names.Any(n => n.StartsWith("Works/wave-lab/")), "other Work sources omitted");
    await using var contentStream = zip.GetEntry("Content/library.json")!.Open();
    using var reader = new StreamReader(contentStream);
    var content = await reader.ReadToEndAsync();
    Check(!content.Contains("EXCLUDED_PAGE_SENTINEL") && !content.Contains("private-page") && !content.Contains("exploring-wave"), "excluded page values and identities absent from transferred state");
    var selected = JsonSerializer.Deserialize<LibraryState>(content, ContentJson.Options)!;
    ContentRules.Validate(selected);
    Check(selected.Pages.Count == 1 && selected.Nodes.Count == 3, "selected state remains a valid full ancestor hierarchy");
    foreach (var file in package.Manifest.Files)
    {
        await using var stream = zip.GetEntry(file.Path)!.Open();
        using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
        Check(memory.Length == file.Length && Hash(memory.ToArray()) == file.Sha256, "manifest validates " + file.Path);
    }
}
Check(beforeContent == Hash(await File.ReadAllBytesAsync(Path.Combine(root, "Content", "library.json"))), "draft/export leave canonical content unchanged");
var originalCounter = await File.ReadAllTextAsync(counterPath);
await File.AppendAllTextAsync(counterPath, "// changed after review\n");
Check((await service.GetDraftAsync(draft.Id)).Stale, "source edits mark draft stale");
await Reject(() => service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash), "source changes reject export");
draft = await service.RefreshDraftAsync(draft.Id, draft.DraftRevision, state.Revision);
Check(!draft.Stale && !draft.SelectedNodeIds.Contains("private-page"), "refresh rebases source preserving exclusions");
await File.WriteAllTextAsync(counterPath, originalCounter);
draft = await service.RefreshDraftAsync(draft.Id, draft.DraftRevision, state.Revision);
state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Operations = [new() { Kind = "createNode", Id = "new-page", NodeType = "page", ParentId = "animal-atlas", Title = "New page" }] });
Check((await service.GetDraftAsync(draft.Id)).Stale, "content edits mark draft stale");
await Reject(() => service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash), "new content cannot enter an old reviewed export");
draft = await service.RefreshDraftAsync(draft.Id, draft.DraftRevision, state.Revision);
Check(draft.Nodes.Any(n => n.Id == "new-page") && !draft.SelectedNodeIds.Contains("new-page"), "new objects remain unchecked after refresh");
var pageDraft = await service.CreateDraftAsync("woodland-habitat", state.Revision);
Check(pageDraft.Nodes.Count == 1 && pageDraft.SelectedNodeIds.Count == 1 && pageDraft.IncludedNodeIds.Count == 3, "Page publication includes only necessary structural ancestors");
var empty = await service.UpdateSelectionAsync(pageDraft.Id, pageDraft.DraftRevision, []);
await Reject(() => service.ExportAsync(empty.Id, empty.DraftRevision, empty.ContentRevision, empty.ReviewHash), "empty selection cannot publish");
await Reject(() => service.GetDraftAsync("../escape"), "draft traversal rejected");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    await Reject(() => service.ExportAsync(draft.Id, draft.DraftRevision, draft.ContentRevision, draft.ReviewHash, canceled.Token), "cancellation leaves the publication inactive");
}
var projectPath = Path.Combine(workRoot, "src", "Pages", "Pages.csproj");
var projectText = await File.ReadAllTextAsync(projectPath);
await File.WriteAllTextAsync(projectPath, projectText.Replace("</Project>", "<Target Name=\"Unsafe\"><Exec Command=\"whoami\" /></Target></Project>"));
await Reject(() => service.CreateDraftAsync("natural-world", state.Revision), "custom MSBuild execution rejected");
await File.WriteAllTextAsync(projectPath, projectText);
var hostPath = Path.Combine(workRoot, "src", "Host", "Program.cs");
var hostText = await File.ReadAllTextAsync(hostPath);
await File.AppendAllTextAsync(hostPath, "// Custom startup would be lost by the fixed receiver Host.\n");
await Reject(() => service.CreateDraftAsync("natural-world", state.Revision), "custom renderer startup is explicitly unsupported");
await File.WriteAllTextAsync(hostPath, hostText);
var addedSource = Path.Combine(workRoot, "src", "Pages", "AddedAfterReview.cs");
await File.WriteAllTextAsync(addedSource, "namespace HyperLib.Work; public static class AddedAfterReview {}\n");
Check((await service.GetDraftAsync(draft.Id)).Stale, "new compiler source is detected after review");
File.Delete(addedSource);
var large = Path.Combine(workRoot, "src", "Pages", "Oversized.cs");
await using (var file = new FileStream(large, FileMode.CreateNew, FileAccess.Write)) file.SetLength(65L * 1024 * 1024);
await Reject(() => service.CreateDraftAsync("natural-world", state.Revision), "oversized source rejected before loading");
File.Delete(large);
var result = new { status = "HYPERLIB_ARCHIVES_EXPORT_OK", checks, fixtureRoot = root, package = package.PackagePath, packageSha256 = package.PackageSha256 };
await File.WriteAllTextAsync(Path.Combine(output, "archives-export-verification.json"), JsonSerializer.Serialize(result, ContentJson.Options));
Console.WriteLine(JsonSerializer.Serialize(result, ContentJson.Options));
