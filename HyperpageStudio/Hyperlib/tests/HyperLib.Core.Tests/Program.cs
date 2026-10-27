using System.Text;
using System.Text.Json;
using HyperLib.Core;

var testBase = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "Hyperlib", ".hyperlib", "core-tests");
var root = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var store = new ContentStore(root);
var assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    assertions++;
}
async Task Throws<T>(Func<Task> operation, string description) where T : Exception
{
    try { await operation(); }
    catch (T) { assertions++; return; }
    throw new InvalidOperationException("FAILED: expected " + typeof(T).Name + ": " + description);
}
void ThrowsSync<T>(Action operation, string description) where T : Exception
{
    try { operation(); }
    catch (T) { assertions++; return; }
    throw new InvalidOperationException("FAILED: expected " + typeof(T).Name + ": " + description);
}
OperationBatch Batch(LibraryState state, params ContentOperation[] operations)
    => new() { ExpectedRevision = state.Revision, Description = "Verifier transaction", Operations = operations.ToList() };
async Task<WorkContentSnapshot> WorkSnapshot(ContentStore contentStore, string workId)
    => JsonSerializer.Deserialize<WorkContentSnapshot>(await File.ReadAllTextAsync(contentStore.ResolvePath($"Works/{workId}/Content/work.json")), ContentJson.Options)!;

await store.InitializeAsync();
var state = await store.ReadAsync();
Check(state.Revision == 0 && state.Pages.Count == 2, "new library contains exactly two seed pages");
Check(state.Nodes.Count(node => node.Type == "work") == 2 && state.Nodes.Count(node => node.Type == "collection") == 2, "seed Works and Collections are distinct");
Check(state.Nodes.Single(node => node.Id == "exploring-wave").ParentId == "sound", "full optional hierarchy is seeded");
Check(state.Nodes.Single(node => node.Id == "woodland-habitat").ParentId == "animal-atlas", "seed also demonstrates a minimal hierarchy");
Check(state.Nodes.All(node => node.Type == "page" ? File.Exists(store.ResolvePath(node.DirectoryPath!)) : Directory.Exists(store.ResolvePath(node.DirectoryPath!))), "hierarchy physically projects folders and page documents");
var animalSnapshot = await WorkSnapshot(store, "animal-atlas");
var waveSnapshot = await WorkSnapshot(store, "wave-lab");
Check(!animalSnapshot.Authoritative && animalSnapshot.WorkId == "animal-atlas" && animalSnapshot.Pages.Count == 1
    && animalSnapshot.Pages.ContainsKey("woodland-habitat") && animalSnapshot.Nodes.All(node => node.WorkId == "animal-atlas"), "Work source root has its own non-authoritative content snapshot");
Check(File.Exists(store.ResolvePath("Works/animal-atlas/" + animalSnapshot.ContentPaths["woodland-habitat"])), "minimal Work page physically resides beneath Work source root");
Check(waveSnapshot.ContentPaths["exploring-wave"].Split('/').Length == 5
    && File.Exists(store.ResolvePath("Works/wave-lab/" + waveSnapshot.ContentPaths["exploring-wave"])), "Work projection preserves nested Volume Part Chapter folders");
Check(animalSnapshot.ReferencedMedia.Select(media => media.Url).Order().SequenceEqual(new[] { "/media/forest-detail.svg", "/media/woodland.svg" }), "Work snapshot declares only its own referenced central media");
Check(waveSnapshot.ReferencedMedia.Any(media => media.Url == "/media/sample-tone.wav" && media.WorkRelativePath == "../../Media/sample-tone.wav")
    && waveSnapshot.ReferencedMedia.Any(media => media.Url == "/media/sample-motion.webm"), "media manifest records portable relative references without duplicating assets");
await store.InitializeAsync();
Check((await store.ReadAsync()).Pages.Count == 2, "initialization is idempotent");

var structure = new List<ContentOperation>();
for (var work = 0; work < 3; work++)
{
    var workId = "generated-work-" + work;
    structure.Add(new ContentOperation { Kind = "createNode", Id = workId, ParentId = "natural-world", NodeType = "work", Title = "Research " + work });
    for (var part = 0; part < 2; part++)
    {
        var partId = workId + "-part-" + part;
        structure.Add(new ContentOperation { Kind = "createNode", Id = partId, ParentId = workId, NodeType = "part", Title = "Section " + part });
        for (var chapter = 0; chapter < 5; chapter++)
            structure.Add(new ContentOperation { Kind = "createNode", Id = partId + "-chapter-" + chapter, ParentId = partId, NodeType = "chapter", Title = "Chapter " + (part * 5 + chapter + 1) });
    }
}
state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Create three Works with ten chapters each", Operations = structure });
Check(state.Nodes.Count(node => node.Type == "chapter") == 31 && state.Pages.Count == 2, "large structural batch creates three Works and thirty chapters without extra pages");
foreach (var generatedWork in state.Nodes.Where(node => node.Type == "work" && node.Id.StartsWith("generated-work-", StringComparison.Ordinal)))
{
    var generatedParts = state.Nodes.Where(node => node.ParentId == generatedWork.Id && node.Type == "part").ToArray();
    Check(generatedParts.Length == 2 && generatedParts.All(part => state.Nodes.Count(node => node.ParentId == part.Id && node.Type == "chapter") == 5),
        "each requested Work has ten chapters divided into two parts");
}
Check(state.Nodes.Where(node => node.Type == "work").All(node => node.ProjectPath == "Works/" + node.Id), "Work project locations use stable IDs");
state = await new ContentStore(root).UndoAsync(state.Revision);
Check(state.Nodes.Count == 10, "whole AI-style batch undoes after process restart");
state = await new ContentStore(root).RedoAsync(state.Revision);
Check(state.Nodes.Count == 49, "whole batch redoes after process restart");
state = await store.UndoAsync(state.Revision);

state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "createNode", Id = "new-page", NodeType = "page", ParentId = "animal-atlas" }));
Check(state.Pages["new-page"].Blocks.Count == 1 && state.Pages["new-page"].Blocks[0].Values["text"].GetString()!.Length > 0, "no-code page creation supplies starter defaults");
await Throws<ContentValidationException>(() => store.RedoAsync(state.Revision), "new content invalidates redo history");
var beforeInvalid = state.Revision;
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "createNode", Id = "must-not-commit", NodeType = "chapter", ParentId = "animal-atlas", Title = "Transient" },
    new ContentOperation { Kind = "createNode", Id = "orphan-page", NodeType = "page", ParentId = "natural-world", Title = "Invalid page" })), "invalid batch is rejected atomically");
state = await store.ReadAsync();
Check(state.Revision == beforeInvalid && state.Nodes.All(node => node.Id != "must-not-commit"), "failed batch commits neither partial content nor revision");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "moveNode", Id = "new-page", ParentId = "wave-lab" })), "cross-Work page moves are blocked");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "moveNode", Id = "animal-atlas", ParentId = "new-page" })), "cycle and hierarchy inversion are blocked");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "createNode", Id = "CON", NodeType = "work", ParentId = "natural-world" })), "reserved device IDs rejected");

var page = ContentJson.Clone(state.Pages["new-page"]);
page.Title = "A cited observation";
page.Blocks[0].Values["text"] = ContentJson.Value("Woodlands contain multiple vegetation layers.");
page.Sources.Add(new() { Id = "reference-one", Url = "https://example.org/woodlands", Title = "Woodland reference", RetrievedAt = DateTimeOffset.UtcNow, Provider = "verifier", Verified = true, BlockIds = [page.Blocks[0].Id] });
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Id = page.Id, Page = page }));
Check(state.Pages[page.Id].Revision == state.Revision && state.Nodes.Single(node => node.Id == page.Id).Title == page.Title, "page save synchronizes title and revision");
var updatedWorkSnapshot = await WorkSnapshot(store, "animal-atlas");
Check(updatedWorkSnapshot.LibraryRevision == state.Revision && updatedWorkSnapshot.Pages[page.Id].Title == page.Title
    && File.Exists(store.ResolvePath("Works/animal-atlas/" + updatedWorkSnapshot.ContentPaths[page.Id])), "Work content snapshot and page projection update with committed edits");
Check((await new ContentStore(root).ReadAsync()).Pages[page.Id].Sources.Single().Url == "https://example.org/woodlands", "source evidence survives reload");
var stalePage = ContentJson.Clone(page);
stalePage.Blocks[0].Values["text"] = ContentJson.Value("This stale draft must not replace the saved observation.");
await Throws<ConflictException>(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Page = stalePage })), "stale page body is rejected even with current library revision");
Check((await store.ReadAsync()).Pages[page.Id].Blocks[0].Values["text"].GetString() == "Woodlands contain multiple vegetation layers.", "newer content survives attempted stale-page overwrite");
var corruptedCitation = ContentJson.Clone(state.Pages[page.Id]);
corruptedCitation.Sources[0].Url = "javascript:alert(1)";
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Page = corruptedCitation })), "active citation URLs rejected");
var incompatibleContent = ContentJson.Clone(state.Pages[page.Id]);
incompatibleContent.Blocks[0].Values["unknown"] = ContentJson.Value("not a template field");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Page = incompatibleContent })), "content may only edit declared template fields");

var simultaneousRevision = state.Revision;
async Task<bool> Compete(string title)
{
    try { await new ContentStore(root).ApplyAsync(new() { ExpectedRevision = simultaneousRevision, Operations = [new ContentOperation { Kind = "updateNode", Id = "natural-world", Title = title }] }); return true; }
    catch (ConflictException exception) { Check(exception.ActualRevision == simultaneousRevision + 1, "conflict reports actual revision"); return false; }
}
var writers = await Task.WhenAll(Compete("Natural World One"), Compete("Natural World Two"));
Check(writers.Count(won => won) == 1, "concurrent writers serialize and exactly one stale writer conflicts");
state = await store.ReadAsync();
Check(state.Revision == simultaneousRevision + 1, "concurrent transaction increments once");
var beforeStaleUndo = ContentJson.Clone(state);
await Throws<ConflictException>(() => store.UndoAsync(simultaneousRevision), "stale undo cannot erase a later edit");
Check(JsonSerializer.Serialize(await store.ReadAsync(), ContentJson.Options) == JsonSerializer.Serialize(beforeStaleUndo, ContentJson.Options), "rejected stale undo preserves every later change");

var oldDirectory = store.ResolvePath(state.Nodes.Single(node => node.Id == "animal-atlas").DirectoryPath!);
File.WriteAllText(Path.Combine(oldDirectory, "personal-note.txt"), "Preserve this user file.");
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "updateNode", Id = "animal-atlas", Title = "Renamed Animal Atlas" }));
var newDirectory = store.ResolvePath(state.Nodes.Single(node => node.Id == "animal-atlas").DirectoryPath!);
Check(!Directory.Exists(oldDirectory) && Directory.Exists(newDirectory), "rename updates physical hierarchy");
Check(Directory.EnumerateFiles(store.ResolvePath(".hyperlib/retired"), "personal-note.txt", SearchOption.AllDirectories).Any(), "projection retirement preserves extra user files");
state = await store.UndoAsync(state.Revision);
Check(Directory.Exists(oldDirectory), "undo reconstructs the old hierarchy path");

var codeDirectory = store.ResolvePath("Works/animal-atlas");
Directory.CreateDirectory(codeDirectory);
File.WriteAllText(Path.Combine(codeDirectory, "source-sentinel.txt"), "Independent source code survives undoable deletion.");
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "deleteNode", Id = "natural-world" }));
Check(!state.Pages.ContainsKey("woodland-habitat") && state.Pages.Count == 1, "subtree deletion removes its content atomically");
Check(File.Exists(Path.Combine(codeDirectory, "source-sentinel.txt")), "subtree deletion preserves Work code");
Check(!Directory.Exists(Path.Combine(codeDirectory, "Content")), "deleted Work content projection retires without removing its code root");
state = await store.UndoAsync(state.Revision);
Check(state.Pages.ContainsKey("woodland-habitat") && state.Pages.ContainsKey("new-page"), "undo restores descendants and page content");
Check((await WorkSnapshot(store, "animal-atlas")).Pages.Count == 2 && File.Exists(Path.Combine(codeDirectory, "source-sentinel.txt")), "undo restores Work-local documents beside preserved code");

state = await store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setLabels", Labels = new() { ["collection"] = "Category", ["work"] = "Book", ["part"] = "Section" } },
    new ContentOperation { Kind = "setAppearance", Id = "animal-atlas", Appearance = new() { ["accent"] = ContentJson.Value("#aabbcc") } }));
Check(state.Labels["work"] == "Book" && state.Nodes.Single(node => node.Id == "animal-atlas").Appearance["accent"].GetString() == "#aabbcc", "labels and appearance save together");
Check(state.Nodes.Single(node => node.Id == "animal-atlas").Type == "work", "display aliases do not change canonical types");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = "animal-atlas", Appearance = new() { ["undeclared-css"] = ContentJson.Value("display:none") } })), "undeclared appearance controls are rejected");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = "animal-atlas", Appearance = new() { ["accent"] = ContentJson.Value("red; background:url(https://example.org)") } })), "appearance color values follow declared type constraints");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "updateNode", Id = "animal-atlas", Appearance = new() { ["accent"] = ContentJson.Value(42) } })), "updateNode cannot bypass appearance type validation");
var customLibraryTemplate = StarterTemplate.Create();
customLibraryTemplate.Appearance.Add(new() { Id = "root-mode", Type = "select", Options = ["calm", "compact"], DefaultValue = ContentJson.Value("calm") });
var customWorkTemplate = StarterTemplate.Create();
customWorkTemplate.Appearance.Add(new() { Id = "work-scale", Type = "number", Min = 1, Max = 3, DefaultValue = ContentJson.Value(1) });
store.TemplateResolver = id => id == "library" ? customLibraryTemplate : customWorkTemplate;
state = await store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = state.LibraryId, Appearance = new() { ["root-mode"] = ContentJson.Value("compact") } },
    new ContentOperation { Kind = "setAppearance", Id = "science-lab", Appearance = new() { ["root-mode"] = ContentJson.Value("calm") } },
    new ContentOperation { Kind = "setAppearance", Id = "new-page", Appearance = new() { ["work-scale"] = ContentJson.Value(2) } }));
Check(state.Appearance["root-mode"].GetString() == "compact" && state.Nodes.Single(node => node.Id == "new-page").Appearance["work-scale"].GetInt32() == 2,
    "root and Collections use library appearance schema while Pages use owning Work schema");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = "new-page", Appearance = new() { ["root-mode"] = ContentJson.Value("calm") } })), "Work descendants cannot use library-only controls");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = "science-lab", Appearance = new() { ["root-mode"] = ContentJson.Value("unsupported") } })), "appearance select options are enforced");
await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = "new-page", Appearance = new() { ["work-scale"] = ContentJson.Value(9) } })), "appearance numeric bounds are enforced");

var legacyWorkTemplate = ContentJson.Clone(customWorkTemplate);
legacyWorkTemplate.Appearance.RemoveAll(field => field.Id == "fontSize");
var legacySchema = JsonSerializer.Serialize(legacyWorkTemplate, ContentJson.Options);
store.TemplateResolver = id => id == "library" ? customLibraryTemplate : legacyWorkTemplate;
var workAppearance = ContentJson.Clone(state.Nodes.Single(node => node.Id == "animal-atlas").Appearance);
workAppearance["fontSize"] = ContentJson.Value(22);
var existingPages = JsonSerializer.Serialize(state.Pages, ContentJson.Options);
state = await store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "setAppearance", Id = "animal-atlas", Appearance = workAppearance }));
Check(state.Nodes.Single(node => node.Id == "animal-atlas").Appearance["fontSize"].GetInt32() == 22
    && JsonSerializer.Serialize(state.Pages, ContentJson.Options) == existingPages,
    "an older Work accepts standard text size without changing existing Page content or revisions");
Check(JsonSerializer.Serialize(legacyWorkTemplate, ContentJson.Options) == legacySchema,
    "text size normalization never changes the independent saved Work schema");
TemplateValidator.EnsureCompatible(legacyWorkTemplate, state.Pages.Values,
    state.Nodes.Where(node => node.WorkId == "animal-atlas").Select(node => node.Appearance));
Check(true, "an older renderer contract remains compatible after saving standard text size");
foreach (var invalidSize in new[] { ContentJson.Value(11), ContentJson.Value(33), ContentJson.Value("22;display:none") })
    await Throws<ContentValidationException>(() => store.ApplyAsync(Batch(state,
        new ContentOperation { Kind = "setAppearance", Id = "animal-atlas", Appearance = new() { ["fontSize"] = invalidSize } })),
        "standard text size rejects out-of-range or nonnumeric values atomically");
var declaredSize = new FieldDefinition { Id = "fontSize", Type = "number", Min = 14, Max = 24, DefaultValue = ContentJson.Value(20) };
Check(ReferenceEquals(StandardAppearance.WithDefaults([declaredSize]).Single(), declaredSize),
    "an explicit template text size retains its own defaults and constraints");
state = await store.UndoAsync(state.Revision);
Check(!state.Nodes.Single(node => node.Id == "animal-atlas").Appearance.ContainsKey("fontSize"),
    "Work text size can be undone independently from its Page content");
state = await store.RedoAsync(state.Revision);
Check(state.Nodes.Single(node => node.Id == "animal-atlas").Appearance["fontSize"].GetInt32() == 22,
    "redo restores the committed Work text size");

await File.WriteAllTextAsync(store.ResolvePath("Content/library.json"), "{interrupted projection");
await File.WriteAllTextAsync(store.ResolvePath(".hyperlib/projections.json"), "{}");
await File.WriteAllTextAsync(store.ResolvePath(".hyperlib/journal/99999999999999999999.json.tmp-interrupted"), "partial record");
var recoveredStore = new ContentStore(root);
var recovered = await recoveredStore.ReadAsync();
Check(recovered.Revision == state.Revision && recovered.Pages.Count == state.Pages.Count, "corrupt projections and incomplete journal temporary recover committed content");
Check(recoveredStore.LastRecoveryMessage is not null, "recovery is observable");
Check(JsonSerializer.Deserialize<LibraryState>(await File.ReadAllTextAsync(store.ResolvePath("Content/library.json")), ContentJson.Options)!.Revision == state.Revision, "recovery materializes valid JSON");
File.Delete(store.ResolvePath("Content/library.json"));
Check((await new ContentStore(root).ReadAsync()).Revision == state.Revision, "missing content projection reconstructs from journal");
File.Delete(store.ResolvePath("Works/wave-lab/Content/work.json"));
await new ContentStore(root).ReadAsync();
Check((await WorkSnapshot(store, "wave-lab")).LibraryRevision == state.Revision, "missing Work-local snapshot recovers from existing journal without changing revision");
var missingWorkPage = store.ResolvePath("Works/wave-lab/" + (await WorkSnapshot(store, "wave-lab")).ContentPaths["exploring-wave"]);
File.Delete(missingWorkPage);
await new ContentStore(root).ReadAsync();
Check(File.Exists(missingWorkPage), "missing nested Work page document repairs automatically");

var validPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j6S8AAAAASUVORK5CYII=");
var imported = await store.ImportMediaAsync(new MemoryStream(validPng), "sample.png", "image/png");
Check(imported.Length == validPng.Length && File.Exists(store.ResolvePath(imported.RelativePath)) && imported.Url.StartsWith("/media/", StringComparison.Ordinal), "media is content-addressed and rooted");
var importedAgain = await store.ImportMediaAsync(new MemoryStream(validPng), "different-name.png", "image/png");
Check(imported.Id == importedAgain.Id, "duplicate media content is deduplicated");
await Throws<ContentValidationException>(() => store.ImportMediaAsync(new MemoryStream(validPng), "../escape.png"), "media path traversal rejected");
await Throws<ContentValidationException>(() => store.ImportMediaAsync(new MemoryStream(validPng), "sample.png", "text/html"), "media MIME mismatch rejected");
await Throws<ContentValidationException>(() => store.ImportMediaAsync(new MemoryStream(Encoding.UTF8.GetBytes("<html>")), "sample.png", "image/png"), "media content signature checked");
await Throws<ContentValidationException>(() => new ContentStore(root) { MaximumMediaBytes = 8 }.ImportMediaAsync(new MemoryStream(validPng), "oversize.png"), "media size limit enforced while streaming");
Check(!Directory.EnumerateFiles(store.ResolvePath("Media"), ".import-*").Any(), "failed imports leave no temporary file");
ThrowsSync<ContentValidationException>(() => store.ResolvePath("../outside.json"), "root traversal rejected");
ThrowsSync<ContentValidationException>(() => store.ResolvePath("C:\\outside.json"), "absolute paths rejected");
ThrowsSync<ContentValidationException>(() => store.ResolvePath("Media/stream:alternate"), "alternate data streams rejected");

var template = StarterTemplate.Create();
TemplateValidator.EnsureCompatible(template, state.Pages.Values);
Check(true, "starter template validates all stored page content");
var renamedTemplate = ContentJson.Clone(template);
renamedTemplate.Blocks[0].Fields[0].Title = "Renamed label";
TemplateValidator.EnsureCompatible(renamedTemplate, state.Pages.Values);
Check(true, "stable field IDs allow display label changes");
var removedField = ContentJson.Clone(template);
removedField.Blocks.Single(block => block.Id == "text").Fields.Clear();
ThrowsSync<ContentValidationException>(() => TemplateValidator.EnsureCompatible(removedField, state.Pages.Values), "removing a used field blocks activation");
var incompatibleType = ContentJson.Clone(template);
incompatibleType.Blocks.Single(block => block.Id == "text").Fields[0].Type = "number";
incompatibleType.Blocks.Single(block => block.Id == "text").Fields[0].DefaultValue = ContentJson.Value(0);
ThrowsSync<ContentValidationException>(() => TemplateValidator.EnsureCompatible(incompatibleType, state.Pages.Values), "incompatible field types block activation");
var additive = ContentJson.Clone(template);
additive.Blocks[0].Fields.Add(new() { Id = "new-field", Type = "string", Required = true, DefaultValue = ContentJson.Value("Default") });
TemplateValidator.EnsureCompatible(additive, state.Pages.Values);
Check(true, "required additive fields with defaults preserve older pages");
var duplicateFields = ContentJson.Clone(template);
duplicateFields.Blocks[0].Fields.Add(ContentJson.Clone(duplicateFields.Blocks[0].Fields[0]));
ThrowsSync<ContentValidationException>(() => TemplateValidator.ValidateDefinition(duplicateFields), "duplicate field IDs rejected");
var incompatibleAppearanceTemplate = ContentJson.Clone(template);
incompatibleAppearanceTemplate.Appearance.RemoveAll(field => field.Id == "accent");
ThrowsSync<ContentValidationException>(() => TemplateValidator.EnsureCompatible(incompatibleAppearanceTemplate, [],
    [new Dictionary<string, JsonElement> { ["accent"] = ContentJson.Value("#aabbcc") }]), "removing a used appearance control blocks activation");

// Damage a separate test library's immutable commit record; do not silently replace durable history.
var damagedRoot = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
var damaged = new ContentStore(damagedRoot);
await damaged.InitializeAsync();
var journal = damaged.ResolvePath(".hyperlib/journal/00000000000000000000.json");
var alteredJournal = (await File.ReadAllTextAsync(journal)).Replace("A Woodland Habitat", "A Silently Altered Title", StringComparison.Ordinal);
await File.WriteAllTextAsync(journal, alteredJournal);
await Throws<ContentRecoveryException>(() => damaged.ReadAsync(), "committed checksum damage stops without overwriting");
Check((await File.ReadAllTextAsync(journal)).Contains("A Silently Altered Title", StringComparison.Ordinal), "recovery preserves damaged evidence for repair");

// A source checkout/export can carry saved readable content without local history artifacts.
var portableRoot = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(portableRoot, "Content"));
await File.WriteAllTextAsync(Path.Combine(portableRoot, "Content", "library.json"), JsonSerializer.Serialize(state, ContentJson.Options));
var portableStore = new ContentStore(portableRoot);
await portableStore.InitializeAsync();
var portableState = await portableStore.ReadAsync();
Check(portableState.Revision == state.Revision && portableState.Pages.Count == 3 && portableState.Pages["new-page"].Title == "A cited observation", "journal-free import retains saved content, not seed defaults");
Check(portableState.Labels["work"] == "Book" && portableState.Nodes.Single(node => node.Id == "animal-atlas").Appearance["accent"].GetString() == "#aabbcc", "portable import retains labels and appearance");
Check(File.Exists(portableStore.ResolvePath($".hyperlib/journal/{state.Revision:D20}.json")), "portable import initializes history at its existing revision");
Check(portableState.Nodes.Where(node => node.Type == "page").All(node => File.Exists(portableStore.ResolvePath(node.DirectoryPath!))), "portable import reconstructs physical hierarchy");
Check((await WorkSnapshot(portableStore, "animal-atlas")).Pages.ContainsKey("new-page"), "portable whole-library import also reconstructs Work-local content");
await Throws<ContentValidationException>(() => portableStore.UndoAsync(portableState.Revision), "imported snapshot has no invented undo history");
var malformedRoot = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(malformedRoot, "Content"));
var malformedProjection = Path.Combine(malformedRoot, "Content", "library.json");
await File.WriteAllTextAsync(malformedProjection, "{malformed saved content");
await Throws<JsonException>(() => new ContentStore(malformedRoot).InitializeAsync(), "malformed portable content stops initialization");
Check(await File.ReadAllTextAsync(malformedProjection) == "{malformed saved content" && !Directory.EnumerateFiles(Path.Combine(malformedRoot, ".hyperlib", "journal")).Any(), "malformed imported content is preserved without seeding");

Console.WriteLine($"HYPERLIB_CORE_OK|assertions:{assertions}|seed-pages:2|atomic-batch|concurrent-writers|undo-redo|journal-recovery|media-boundaries|template-compatibility|citations");
Console.WriteLine("VERIFIER_ARTIFACTS=" + root);
