using System.Text.Json;
using HyperLib.Core;

var assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    assertions++;
}
void Throws(Action action, string description)
{
    try { action(); }
    catch (ContentValidationException) { assertions++; return; }
    throw new InvalidOperationException("FAILED: expected validation rejection: " + description);
}
async Task Reject(Func<Task> action, string description)
{
    try { await action(); }
    catch (ContentValidationException) { assertions++; return; }
    throw new InvalidOperationException("FAILED: expected validation rejection: " + description);
}
OperationBatch Batch(LibraryState state, params ContentOperation[] operations)
    => new() { ExpectedRevision = state.Revision, Description = "Catalog verifier", Operations = operations.ToList() };
string Json<T>(T value) => JsonSerializer.Serialize(value, ContentJson.Options);

var catalog = BaseTemplateCatalog.List();
Check(catalog.Count == 17 && catalog.Select(info => info.Id).Distinct().Count() == 17, "exactly seventeen stable starter IDs");
Check(catalog.Count(info => info.IsOriginal) == 2 && catalog.Count(info => !info.IsOriginal) == 15, "two originals and fifteen new designs");
Check(catalog.Where(info => info.IsOriginal).Select(info => info.Id).SequenceEqual(new[] { "woodland-editorial", "wave-laboratory" }), "original design identities are preserved");
var arrangements = new HashSet<string>(StringComparer.Ordinal);
foreach (var info in catalog)
{
    var definition = BaseTemplateCatalog.CreateTemplate(info.Id);
    TemplateValidator.ValidateDefinition(definition);
    Check(definition.DefaultBlocks.Count >= 7, info.Id + " has a complete page arrangement");
    var page = BaseTemplateCatalog.CreateDemoPage(info.Id, "page-" + info.Id, "work-" + info.Id);
    TemplateValidator.ValidatePage(definition, page);
    Check(page.Blocks.Count == definition.DefaultBlocks.Count && page.Title == info.DemoTitle, info.Id + " creates a complete demo");
    Check(page.UsesEditableChrome, info.Id + " marks its explicit editable page chrome");
    Check(page.Blocks.Select(block => block.Id).SequenceEqual(definition.DefaultBlocks.Select(block => block.Id)), info.Id + " preserves block identities");
    Check(page.Blocks.First(block => block.Type is "header" or "banner").Values["title"].GetString() == page.Title, info.Id + " heading follows page title");
    var appearance = BaseTemplateCatalog.CreateAppearance(info.Id);
    TemplateValidator.ValidateValues(definition.Appearance, appearance, "catalog appearance");
    Check(appearance["variant"].GetString() == info.Id && appearance["background"].GetString() == info.Background, info.Id + " declares its actual design and full background");
    Check(definition.Blocks.All(block => new[] { "span", "font", "align", "color" }.All(id => block.Fields.Any(field => field.Id == id && !field.Required && field.DefaultValue is null))), info.Id + " supports optional inherited element styling");
    if (!info.IsOriginal)
        Check(arrangements.Add(string.Join('|', page.Blocks.Select(block => block.Type + ":" + (block.Values.TryGetValue("span", out var span) ? span.GetString() : "full")))), info.Id + " has a distinct structural arrangement");
    page.Blocks.First(block => block.Type is "header" or "banner").Values["title"] = ContentJson.Value("Changed copy");
    definition.DefaultBlocks.Clear();
    appearance["variant"] = ContentJson.Value("changed-copy");
    var other = BaseTemplateCatalog.CreateTemplate(info.Id);
    Check(other.DefaultBlocks.Count >= 7 && other.DefaultBlocks.First(block => block.Type is "header" or "banner").Values["title"].GetString() != "Changed copy", info.Id + " never mutates shared defaults");
}
var seeds = SeedLibrary.Create();
foreach (var (starter, pageId) in new[] { ("woodland-editorial", "woodland-habitat"), ("wave-laboratory", "exploring-wave") })
{
    var originals = seeds.Pages[pageId].Blocks;
    var defaults = BaseTemplateCatalog.CreateTemplate(starter).DefaultBlocks;
    Check(originals.All(block => Json(block) == Json(defaults.Single(item => item.Id == block.Id))), starter + " preserves all six original content blocks exactly");
}
Check(Json(BaseTemplateCatalog.CreateDemoPage("wave-laboratory", "demo-wave", "demo-work").Sources) == Json(seeds.Pages["exploring-wave"].Sources), "wave demo retains its source attribution");
Throws(() => BaseTemplateCatalog.CreateTemplate("not-a-starter"), "unknown starter");
Throws(() => BaseTemplateCatalog.CreateTemplate("../woodland-editorial"), "starter path traversal");
var invalid = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
invalid.DefaultBlocks[0].Values["font"] = ContentJson.Value("url(https://bad.invalid/font)");
Throws(() => TemplateValidator.ValidateDefinition(invalid), "arbitrary font CSS");
invalid = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
invalid.DefaultBlocks[0].Values["color"] = ContentJson.Value("red;position:fixed");
Throws(() => TemplateValidator.ValidateDefinition(invalid), "CSS color injection");
invalid = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
invalid.DefaultBlocks[0].Values["span"] = ContentJson.Value("twelve");
Throws(() => TemplateValidator.ValidateDefinition(invalid), "unsupported grid span");
invalid = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
invalid.DefaultBlocks[0].Values["align"] = ContentJson.Value("up");
Throws(() => TemplateValidator.ValidateDefinition(invalid), "unsupported alignment");
invalid = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
invalid.DefaultBlocks.Add(ContentJson.Clone(invalid.DefaultBlocks[0]));
Throws(() => TemplateValidator.ValidateDefinition(invalid), "duplicate default block ID");
invalid = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
invalid.DefaultBlocks[0].Region = "unowned";
Throws(() => TemplateValidator.ValidateDefinition(invalid), "unknown default region");
invalid = BaseTemplateCatalog.CreateTemplate("travel-atlas");
invalid.DefaultBlocks.First(block => block.Type == "map").Values["latitude"] = ContentJson.Value(100);
Throws(() => TemplateValidator.ValidateDefinition(invalid), "invalid map latitude");
invalid = BaseTemplateCatalog.CreateTemplate("product-launch");
invalid.DefaultBlocks.First(block => block.Type == "embed").Values["url"] = ContentJson.Value("javascript:alert(1)");
Throws(() => TemplateValidator.ValidateDefinition(invalid), "active URL in embed");

var testBase = args.Length == 0 ? Path.Combine(Path.GetTempPath(), "hyperlib-catalog-verifier") : Path.GetFullPath(args[0]);
var testRoot = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
var store = new ContentStore(testRoot) { TemplateResolver = _ => StarterTemplate.Create() };
await store.InitializeAsync();
var state = await store.ReadAsync();
Check(state.Pages.Count == 2, "catalog availability does not silently install demo pages");
var originalState = ContentJson.Clone(state);
var operations = new List<ContentOperation> { new() { Kind = "createNode", NodeType = "collection", Id = "catalog", Title = "Base Template Library", ParentId = state.LibraryId } };
foreach (var info in catalog)
{
    operations.Add(new() { Kind = "createNode", NodeType = "work", Id = "work-" + info.Id, ParentId = "catalog", Title = info.Title, StarterId = info.Id });
    operations.Add(new() { Kind = "createNode", NodeType = "page", Id = "page-" + info.Id, ParentId = "work-" + info.Id, Title = "My " + info.Title });
}
state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Create seventeen starter-backed Works and pages", Operations = operations });
Check(state.Pages.Count == 19 && state.Revision == 1, "full starter library creates atomically in one revision");
foreach (var info in catalog)
{
    var work = state.Nodes.Single(node => node.Id == "work-" + info.Id);
    var page = state.Pages["page-" + info.Id];
    Check(work.StarterId == info.Id && work.Appearance["variant"].GetString() == info.Id, info.Id + " stores selected provenance and design");
    Check(page.Blocks.Count == BaseTemplateCatalog.CreateTemplate(info.Id).DefaultBlocks.Count, info.Id + " same-batch page uses chosen starter despite unrelated resolver fallback");
    Check(page.Blocks.First(block => block.Type is "header" or "banner").Values["title"].GetString() == "My " + info.Title, info.Id + " same-batch heading uses requested title");
}
var committed = ContentJson.Clone(state);
state = await new ContentStore(testRoot).UndoAsync(state.Revision);
Check(state.Pages.Count == 2 && state.Nodes.Count == originalState.Nodes.Count, "entire starter batch undo survives restart");
state = await new ContentStore(testRoot).RedoAsync(state.Revision);
Check(state.Pages.Count == 19 && state.Nodes.Single(node => node.Id == "work-portfolio-grid").StarterId == "portfolio-grid", "redo restores starter identity and complete defaults");
Check(Json(state.Pages["page-portfolio-grid"].Blocks) == Json(committed.Pages["page-portfolio-grid"].Blocks), "redo preserves all block IDs and values");

store = new ContentStore(testRoot);
var unchanged = Json(await store.ReadAsync());
foreach (var operation in new[] {
    new ContentOperation { Kind = "createNode", NodeType = "work", Id = "invalid-starter", ParentId = "catalog", StarterId = "unknown" },
    new ContentOperation { Kind = "createNode", NodeType = "page", Id = "invalid-choice", ParentId = "work-portfolio-grid", StarterId = "recipe-kitchen" },
    new ContentOperation { Kind = "updateNode", Id = "work-portfolio-grid", StarterId = "recipe-kitchen" },
    new ContentOperation { Kind = "setAppearance", Id = "work-portfolio-grid", Appearance = new() { ["font"] = ContentJson.Value("unsafe-css") } }
}) await Reject(() => store.ApplyAsync(Batch(state, operation)), "invalid starter or declared appearance");
Check(Json(await store.ReadAsync()) == unchanged, "all rejected edits preserve the committed revision and content");
await Reject(() => store.ApplyAsync(Batch(state,
    new() { Kind = "createNode", NodeType = "work", Id = "rollback-work", ParentId = "catalog", StarterId = "recipe-kitchen" },
    new() { Kind = "createNode", NodeType = "page", Id = "rollback-page", ParentId = "rollback-work" },
    new() { Kind = "setAppearance", Id = "rollback-work", Appearance = new() { ["color"] = ContentJson.Value("undeclared") } })), "late failure rolls back the complete starter batch");
Check(Json(await store.ReadAsync()) == unchanged && !Directory.Exists(Path.Combine(testRoot, "Works", "rollback-work")), "rejected new Work leaves no source/content projection");

var ownedTemplate = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
ownedTemplate.Blocks.First(block => block.Id == "header").Fields.Add(new() { Id = "ownerNote", Type = "string", DefaultValue = ContentJson.Value("Owned source") });
ownedTemplate.DefaultBlocks = [new() { Id = "custom-owned-heading", Type = "header", Region = "main", Values = new() { ["title"] = ContentJson.Value("Owned title"), ["ownerNote"] = ContentJson.Value("Keep this source-owned field") } }];
store.TemplateResolver = id => id == "work-portfolio-grid" ? ownedTemplate : null;
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "createNode", NodeType = "page", Id = "owned-page", ParentId = "work-portfolio-grid", Title = "Fresh page" }));
Check(state.Pages["owned-page"].Blocks.Count == 1 && state.Pages["owned-page"].Blocks[0].Id == "custom-owned-heading", "existing Work prefers active source-owned defaults over catalog");
Check(state.Pages["owned-page"].Blocks[0].Values["title"].GetString() == "Fresh page" && state.Pages["owned-page"].Blocks[0].Values["ownerNote"].GetString() == "Keep this source-owned field", "active owned schema survives title substitution");
Check(ownedTemplate.DefaultBlocks[0].Values["title"].GetString() == "Owned title", "Add Page does not mutate the source-owned template");
var edited = ContentJson.Clone(state.Pages["owned-page"]);
edited.Blocks[0].Values["align"] = ContentJson.Value("right");
edited.Blocks[0].Values["font"] = ContentJson.Value("monospace");
edited.Blocks[0].Values["color"] = ContentJson.Value("#123456");
edited.Blocks[0].Values["span"] = ContentJson.Value("half");
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Id = edited.Id, Page = edited }));
Check(state.Pages[edited.Id].Blocks[0].Values["span"].GetString() == "half", "declared optional element styles persist");
edited = ContentJson.Clone(state.Pages["owned-page"]);
foreach (var field in new[] { "align", "font", "color", "span" }) edited.Blocks[0].Values.Remove(field);
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Id = edited.Id, Page = edited }));
Check(!state.Pages[edited.Id].Blocks[0].Values.ContainsKey("color"), "removing an optional override restores inheritance");
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "setAppearance", Id = "owned-page", Appearance = new() { ["background"] = ContentJson.Value("#abcdef") } }));
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "setAppearance", Id = "owned-page", Appearance = [] }));
Check(state.Nodes.Single(node => node.Id == "owned-page").Appearance.Count == 0, "appearance reset removes local background override");
var laterTitle = state.Pages["owned-page"].Title;
try { await store.UndoAsync(1); throw new InvalidOperationException("Stale undo should conflict"); }
catch (ConflictException) { assertions++; }
Check((await store.ReadAsync()).Pages["owned-page"].Title == laterTitle, "stale undo cannot erase later edits");

var legacyChromePage = ContentJson.Clone(state.Pages["woodland-habitat"]);
Check(!legacyChromePage.UsesEditableChrome, "original legacy page begins without editable chrome metadata");
legacyChromePage.UsesEditableChrome = true;
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Id = legacyChromePage.Id, Page = legacyChromePage }));
var olderClientPage = ContentJson.Clone(state.Pages[legacyChromePage.Id]);
olderClientPage.UsesEditableChrome = false;
olderClientPage.Title = "Saved by an older client";
var olderClientJson = Json(olderClientPage);
Check(!olderClientJson.Contains("usesEditableChrome", StringComparison.Ordinal), "older client payload omits editable chrome metadata");
olderClientPage = JsonSerializer.Deserialize<PageDocument>(olderClientJson, ContentJson.Options)!;
state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Id = olderClientPage.Id, Page = olderClientPage }));
Check(state.Pages[olderClientPage.Id].UsesEditableChrome && state.Pages[olderClientPage.Id].Title == olderClientPage.Title, "older client content save retains existing editable chrome metadata");
state = await store.UndoAsync(state.Revision);
Check(state.Pages[olderClientPage.Id].UsesEditableChrome && state.Pages[olderClientPage.Id].Title == legacyChromePage.Title, "undo restores content before the older client save");
state = await store.UndoAsync(state.Revision);
Check(!state.Pages[olderClientPage.Id].UsesEditableChrome, "undo snapshot can still restore legacy false metadata");

var legacyTemplate = StarterTemplate.Create();
Check(TemplateValidator.CreateDefaultPage("legacy", "work", "Legacy", legacyTemplate).Blocks.Count == 1, "templates without declared defaults retain legacy fallback");
Check(!TemplateValidator.CreateDefaultPage("legacy", "work", "Legacy", legacyTemplate).UsesEditableChrome && !Json(new PageDocument()).Contains("usesEditableChrome", StringComparison.Ordinal), "missing legacy chrome flag remains omitted to preserve old journal checksums");
var reusable = BaseTemplateCatalog.CreateTemplate("recipe-kitchen");
reusable.DefaultBlocks = [new() { Id = "reusable-custom-heading", Type = "header", Region = "main", Values = new() { ["title"] = ContentJson.Value("Custom reusable title"), ["text"] = ContentJson.Value("Custom reusable content") } }];
reusable.Appearance.Single(field => field.Id == "accent").DefaultValue = ContentJson.Value("#13579b");
var reusablePath = Path.Combine(testRoot, "BaseTemplates/recipe-kitchen/Project/authoring.template.json");
Directory.CreateDirectory(Path.GetDirectoryName(reusablePath)!);
await File.WriteAllTextAsync(reusablePath, Json(reusable));
state = await store.ApplyAsync(Batch(state,
    new ContentOperation { Kind = "createNode", NodeType = "work", Id = "reusable-copy", ParentId = "catalog", StarterId = "recipe-kitchen" },
    new ContentOperation { Kind = "createNode", NodeType = "page", Id = "reusable-page", ParentId = "reusable-copy", Title = "My reusable copy" }));
Check(state.Pages["reusable-page"].Blocks.Count == 1 && state.Pages["reusable-page"].Blocks[0].Values["text"].GetString() == "Custom reusable content", "new Work and Page use canonical edited reusable defaults in the same batch");
Check(state.Nodes.Single(node => node.Id == "reusable-copy").Appearance["accent"].GetString() == "#13579b", "new Work appearance follows canonical reusable defaults");
Check(state.Pages["reusable-page"].UsesEditableChrome, "canonical reusable page receives explicit editable chrome");
var beforeMalformed = Json(state);
await File.WriteAllTextAsync(reusablePath, "{broken");
await Reject(() => store.ApplyAsync(Batch(state, new ContentOperation { Kind = "createNode", NodeType = "work", Id = "invalid-reusable", ParentId = "catalog", StarterId = "recipe-kitchen" })), "malformed canonical reusable schema is not silently replaced by catalog defaults");
Check(Json(await store.ReadAsync()) == beforeMalformed, "malformed reusable schema makes no content change");
Console.WriteLine($"HYPERLIB_CATALOG_OK assertions={assertions} templates={catalog.Count} root={testRoot}");
