using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;

if (args.Length == 2 && args[0] == "--ensure-core")
{
    await TemplateObjectLibrary.EnsureCoreLibraryAsync(Path.GetFullPath(args[1]));
    Console.WriteLine("HYPERLIB_CORE_LIBRARY_READY groups=" + TemplateObjectLibrary.ListDesignGroups(args[1]).Count);
    return;
}
if (args.Length < 2) throw new ArgumentException("Provide an isolated fixture directory and the source Hyperlib root.");
var root = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
var source = Path.GetFullPath(args[1]);
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    checks++;
}
async Task Reject(Func<Task> action, string description)
{
    try { await action(); }
    catch (ContentValidationException) { checks++; return; }
    throw new InvalidOperationException("FAILED: expected rejection: " + description);
}
string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Json<T>(T value) => JsonSerializer.Serialize(value, ContentJson.Options);
void CopyTree(string input, string output)
{
    Directory.CreateDirectory(output);
    foreach (var file in Directory.EnumerateFiles(input)) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
    foreach (var directory in Directory.EnumerateDirectories(input).Where(path => Path.GetFileName(path) is not ("bin" or "obj" or ".git")))
        CopyTree(directory, Path.Combine(output, Path.GetFileName(directory)));
}
CopyTree(Path.Combine(source, "Starters", "standard"), Path.Combine(root, "Starters", "standard"));
CopyTree(Path.Combine(source, "src", "HyperLib.Core"), Path.Combine(root, "src", "HyperLib.Core"));
CopyTree(Path.Combine(source, "src", "HyperLib.Runtime"), Path.Combine(root, "src", "HyperLib.Runtime"));
Directory.CreateDirectory(Path.Combine(root, "Content"));
await File.WriteAllTextAsync(Path.Combine(root, "Content", "library.json"), "{\"revision\":42,\"sentinel\":\"owned content\"}");
var contentHash = HashFile(Path.Combine(root, "Content", "library.json"));
await TemplateObjectLibrary.EnsureCoreLibraryAsync(root);
var groups = TemplateObjectLibrary.ListDesignGroups(root);
Check(groups.Count == 18, "all seventeen existing designs plus Blank become independent groups");
Check(groups.All(group => File.Exists(Path.Combine(group.ProjectRoot, "authoring.template.json"))), "all groups own authoring schema");
var first = groups.First(group => group.Id == "woodland-editorial");
Check(first.ProjectRoot.Contains("CoreLibrary"), "groups use Core Library storage");
var groupDefinition = TemplateObjectLibrary.ReadDefinition(first.ProjectRoot);
groupDefinition.Title = "Independently edited group";
await File.WriteAllTextAsync(Path.Combine(first.ProjectRoot, "authoring.template.json"), Json(groupDefinition));
await TemplateObjectLibrary.EnsureCoreLibraryAsync(root);
Check(TemplateObjectLibrary.ListDesignGroups(root).Count == 18 && TemplateObjectLibrary.ReadDefinition(first.ProjectRoot).Title == "Independently edited group", "idempotent migration preserves independently edited groups");
Check(ReusableTemplateCatalog.ReadDefinition(root, first.Id).Title != groupDefinition.Title, "standalone source is independent");
var objects = TemplateObjectLibrary.ListObjects(root, "blank");
Check(objects.Select(item => item.CatalogId).Distinct().Count() == objects.Count && objects.All(item => item.CatalogId.StartsWith("HLC-")), "catalog objects expose distinct stable readable IDs");
var initialTextCatalogId = objects.Single(item => item.Id == "element-text").CatalogId;
var blankGroup = TemplateObjectLibrary.GetDesignGroup(root, "blank");
var blankDefinition = TemplateObjectLibrary.ReadDefinition(blankGroup.ProjectRoot);
blankDefinition.Blocks.Single(block => block.Id == "text").Title = "Updated name";
await File.WriteAllTextAsync(Path.Combine(blankGroup.ProjectRoot, "authoring.template.json"), Json(blankDefinition));
Check(TemplateObjectLibrary.ListObjects(root, "blank").Single(item => item.Id == "element-text").CatalogId == initialTextCatalogId, "editing object content does not change its catalog ID");
var legacyBlockJson = Json(new BlockDefinition { Id = "plain", Title = "Plain", Component = "PlainBlock" });
Check(!legacyBlockJson.Contains("catalogId") && !legacyBlockJson.Contains("sourceTemplateId") && !legacyBlockJson.Contains("category"), "absent catalog metadata is omitted from legacy serialized definitions");
Check(objects.Any(item => item.Category == "Typography") && objects.Any(item => item.Category == "Colors & surfaces")
    && objects.Any(item => item.Kind == "layout") && objects.Any(item => item.Kind == "region"), "catalog exposes work and page building blocks");
Check(TemplateObjectLibrary.NormalizeCategory("Data display") == "Data" && TemplateObjectLibrary.NormalizeCategory("Structure & navigation") == "Navigation"
    && TemplateObjectLibrary.NormalizeCategory("Identity & people") == "Identity & people", "catalog categories normalize known aliases while preserving custom categories");
Check(objects.Count(item => item.Id.StartsWith("layout-")) == 5, "single, multi-column, sidebar and block layouts are available");
var target = await ReusableTemplateManager.CreateBlankAsync(root, "Target");
var targetRoot = ReusableTemplateCatalog.GetProjectRoot(root, target.Id);
var targetPath = Path.Combine(targetRoot, "authoring.template.json");
var targetDefinition = TemplateObjectLibrary.ReadDefinition(targetRoot);
targetDefinition.Blocks.Single(block => block.Id == "text").Fields.Add(new() { Id = "nullableNote", Title = "Optional note", Type = "text", DefaultValue = ContentJson.Value<object?>(null) });
var beforeTarget = HashFile(targetPath);
var sourceExample = TemplateObjectLibrary.ListObjects(root, "portfolio-grid").First(item => item.Kind == "preset" && item.Element?.Id == "banner");
var plan = await TemplateObjectImporter.PrepareAsync(root, targetRoot, targetDefinition, [new("portfolio-grid", sourceExample.Id)]);
Check(HashFile(targetPath) == beforeTarget, "preparing imports never changes destination");
Check(plan.Files.Count == 0, "identical stock components reuse existing dependencies");
Check(plan.Definition.Blocks.Count == targetDefinition.Blocks.Count + 1, "configured example creates an independently editable element");
Check(Json(plan.Definition.Appearance) == Json(targetDefinition.Appearance), "individual objects inherit destination theme");
Check(plan.Definition.Blocks.Single(block => block.Id == "text").Fields.Single(field => field.Id == "nullableNote").DefaultValue?.ValueKind == JsonValueKind.Null, "import preserves explicit null defaults in destination drafts");
await TemplateObjectImporter.CommitAsync(targetRoot, plan.Definition, [plan], beforeTarget);
Check(TemplateObjectLibrary.ReadDefinition(targetRoot).Blocks.Count == plan.Definition.Blocks.Count, "committed schema contains imported element");
Check(TemplateObjectLibrary.ReadDefinition(targetRoot).Blocks.Single(block => block.Id == "text").Fields.Single(field => field.Id == "nullableNote").DefaultValue?.ValueKind == JsonValueKind.Null, "saved explicit null defaults round-trip without becoming inheritance");
var duplicatePlan = await TemplateObjectImporter.PrepareAsync(root, targetRoot, plan.Definition, [new("portfolio-grid", sourceExample.Id)]);
Check(duplicatePlan.Definition.Blocks.Count == plan.Definition.Blocks.Count, "repeated identical selection is deduplicated");
var style = await TemplateObjectImporter.PrepareAsync(root, targetRoot, plan.Definition, [new("wave-laboratory", "palette"), new("wave-laboratory", "typography")]);
Check(style.Definition.Appearance.Single(field => field.Id == "background").DefaultValue?.GetString() == "#101a21", "palette is an explicit reusable object");
Check(style.Definition.Appearance.Single(field => field.Id == "variant").DefaultValue?.GetString() == "woodland-editorial", "palette and fonts do not silently import structural theme");
var interleavedStyle = await TemplateObjectImporter.PrepareAsync(root, targetRoot, plan.Definition,
    [new("blank", "palette"), new("wave-laboratory", "work-style"), new("blank", "typography")]);
Check(interleavedStyle.Definition.Appearance.Single(field => field.Id == "headingFont").DefaultValue?.GetString() == "system"
    && interleavedStyle.Definition.Appearance.Single(field => field.Id == "background").DefaultValue?.GetString() == "#101a21",
    "interleaved Design Group style choices apply in selection order");
await Reject(() => TemplateObjectImporter.PrepareAsync(root, targetRoot, style.Definition, [new("blank", "layout-two-columns"), new("blank", "layout-sidebar")]), "ambiguous two layouts");
var curated = await TemplateObjectLibrary.CreateTemplateFromObjectsAsync(root, "Curated cards", [new("portfolio-grid", sourceExample.Id)]);
var empty = await TemplateObjectLibrary.CreateTemplateFromObjectsAsync(root, "Empty collection", []);
var emptyDefinition = ReusableTemplateCatalog.ReadDefinition(root, empty.Id);
Check(emptyDefinition.Blocks.Count == 1 && emptyDefinition.Blocks[0].Id == "text" && emptyDefinition.DefaultBlocks.Count == 0 && emptyDefinition.EmptyPageDefault,
    "empty collection begins with only Text and an empty Page foundation");
var curatedRoot = ReusableTemplateCatalog.GetProjectRoot(root, curated.Id);
var curatedDefinition = TemplateObjectLibrary.ReadDefinition(curatedRoot);
Check(curatedDefinition.Blocks.Count == 1 && curatedDefinition.Blocks[0].Component == "BannerBlock", "new collection contains chosen objects instead of the entire starter palette");
var layout = await TemplateObjectLibrary.CreateTemplateFromObjectsAsync(root, "Two column notes", [new("blank", "layout-two-columns")]);
var layoutDefinition = ReusableTemplateCatalog.ReadDefinition(root, layout.Id);
Check(layoutDefinition.Blocks.Count == 2 && layoutDefinition.DefaultBlocks.Count == 3, "layout imports only its required element schemas");
Check(layoutDefinition.DefaultBlocks.Count(block => block.Values.TryGetValue("span", out var span) && span.GetString() == "half") == 2, "two-column arrangement remains functional");

var custom = await ReusableTemplateManager.CreateBlankAsync(root, "Custom sources");
var customRoot = ReusableTemplateCatalog.GetProjectRoot(root, custom.Id);
var customDefinition = TemplateObjectLibrary.ReadDefinition(customRoot);
customDefinition.Blocks.Add(new() { Id = "badge", Title = "Badge", Component = "BadgeBlock", Fields = [new() { Id = "text", Title = "Text", Type = "string", DefaultValue = ContentJson.Value("A reusable badge") },
    new() { Id = "src", Title = "Badge image", Type = "media", DefaultValue = ContentJson.Value("/_content/HyperLib.Work/widgets/badge.svg") }],
    CatalogId = "HLC-TEST-BADGE", Category = "Identity & people", Description = "A portable identity badge.", SourceTemplateId = "legacy.badge.v1", SourceLibrary = "Verification library" });
foreach (var region in customDefinition.Regions.Where(region => region.AllowedBlocks.Count > 0)) region.AllowedBlocks.Add("badge");
customDefinition.DefaultBlocks = [new() { Id = "badge-example", Type = "badge", Region = "main", Values = new() { ["src"] = ContentJson.Value("/_content/HyperLib.Work/widgets/badge.svg"), ["text"] = ContentJson.Value("A sample badge") } }];
await File.WriteAllTextAsync(Path.Combine(customRoot, "authoring.template.json"), Json(customDefinition));
await File.WriteAllTextAsync(Path.Combine(customRoot, "src/Pages/Blocks/BadgeBlock.razor"), "@inject IJSRuntime JS\n<strong class=\"badge\">@BadgeHelper.Label(Context.Text(\"text\"))</strong>\n@code { [Parameter] public BlockContext Context { get; set; } = new(); protected override async Task OnAfterRenderAsync(bool firstRender) { if(firstRender) await JS.InvokeAsync<IJSObjectReference>(\"import\", \"./_content/HyperLib.Work/widgets/badge.js\"); } }\n");
await File.WriteAllTextAsync(Path.Combine(customRoot, "src/Pages/Blocks/BadgeBlock.razor.css"), ".badge { border: 2px solid teal; padding: 4px; }\n");
await File.WriteAllTextAsync(Path.Combine(customRoot, "src/Pages/BadgeHelper.cs"), "namespace HyperLib.Work; public static class BadgeHelper { public static string Label(string text) => \"Badge: \" + text; }\n");
Directory.CreateDirectory(Path.Combine(customRoot, "src/Pages/wwwroot/widgets"));
await File.WriteAllTextAsync(Path.Combine(customRoot, "src/Pages/wwwroot/widgets/badge.js"), "export const badge = true;\n");
await File.WriteAllTextAsync(Path.Combine(customRoot, "src/Pages/wwwroot/widgets/badge.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"10\" /></svg>\n");
var savedGroup = await TemplateObjectLibrary.SaveDesignGroupAsync(root, customRoot, "Custom collection");
Check(savedGroup.Id.StartsWith("group-"), "user can save a Template as a new Design Group");
var describedBadge = TemplateObjectLibrary.ListObjects(root, savedGroup.Id).Single(item => item.Id == "element-badge");
Check(describedBadge.CatalogId == "HLC-TEST-BADGE" && describedBadge.Category == "Identity & people" && describedBadge.Description == "A portable identity badge."
    && describedBadge.SourceTemplateId == "legacy.badge.v1" && describedBadge.SourceLibrary == "Verification library", "declared metadata drives category, description, stable identity and provenance");
var sourceHash = HashFile(Path.Combine(customRoot, "src/Pages/Blocks/BadgeBlock.razor"));
var ownedTextHash = HashFile(Path.Combine(targetRoot, "src/Pages/Blocks/TextBlock.razor"));
var customPlan = await TemplateObjectImporter.PrepareAsync(root, targetRoot, TemplateObjectLibrary.ReadDefinition(targetRoot), [new(savedGroup.Id, "element-badge"), new(savedGroup.Id, "element-text"), new(savedGroup.Id, "element-image")]);
Check(customPlan.Files.Any(file => file.RelativePath.EndsWith("BadgeBlock.razor.css")) && customPlan.Files.Any(file => file.RelativePath.EndsWith("BadgeHelper.cs"))
    && customPlan.Files.Any(file => file.RelativePath.EndsWith("widgets/badge.js")), "custom object stages scoped CSS, helpers and JavaScript");
var importedBadge = customPlan.Definition.Blocks.Single(block => block.Id == "badge");
Check(importedBadge.Component.StartsWith("Hl") && importedBadge.Component.EndsWith("BadgeBlock"), "source component receives deterministic unique identity");
var importedImage = importedBadge.Fields.Single(field => field.Id == "src").DefaultValue!.Value.GetString()!;
Check(importedImage.Contains("/_content/HyperLib.Work/hyperlib-imports/") && customPlan.Files.Any(file => file.RelativePath == "src/Pages/wwwroot/" + importedImage.Split("/_content/HyperLib.Work/")[1]),
    "schema media defaults remap to the copied static asset");
Check(customPlan.Definition.Blocks.Any(block => block.Id != "text" && block.Component.StartsWith("Hl") && block.Component.EndsWith("TextBlock"))
    && customPlan.Definition.Blocks.Single(block => block.Id == "text").Component == "TextBlock", "same-named components are imported independently without replacing destination identity");
var razor = Encoding.UTF8.GetString(customPlan.Files.Single(file => file.RelativePath.EndsWith("BadgeBlock.razor")).Content);
Check(razor.Contains("@namespace HyperLib.Work.Imports.") && razor.Contains("_content/HyperLib.Work/hyperlib-imports/"), "namespace and static asset URLs follow imported module");
var nextPlan = await TemplateObjectImporter.PrepareAsync(root, targetRoot, customPlan.Definition, [new(savedGroup.Id, "element-badge")], [customPlan]);
Check(nextPlan.Files.Count == 0 && nextPlan.Definition.Blocks.Count == customPlan.Definition.Blocks.Count, "multiple Add actions reuse pending source and schema");
var mediaPreset = await TemplateObjectImporter.PrepareAsync(root, targetRoot, customPlan.Definition, [new(savedGroup.Id, "example-badge-example")], [customPlan]);
Check(mediaPreset.Definition.Blocks.Last().Fields.Single(field => field.Id == "src").DefaultValue!.Value.GetString() == importedImage, "configured preset media values remap to copied static asset");
var mediaLayout = await TemplateObjectImporter.PrepareAsync(root, targetRoot, customPlan.Definition, [new(savedGroup.Id, "page-layout")], [customPlan]);
Check(mediaLayout.Definition.DefaultBlocks.Single().Values["src"].GetString() == importedImage, "page layout media values remap to copied static asset");
var sourceDefinitionPath = Path.Combine(savedGroup.ProjectRoot, "authoring.template.json");
var sourceDefinitionText = await File.ReadAllTextAsync(sourceDefinitionPath);
await File.AppendAllTextAsync(sourceDefinitionPath, "\n");
var unchanged = HashFile(targetPath);
await Reject(() => TemplateObjectImporter.CommitAsync(targetRoot, customPlan.Definition, [customPlan], unchanged), "changed group rejects staged save");
Check(HashFile(targetPath) == unchanged && customPlan.Files.All(file => !File.Exists(Path.Combine(targetRoot, file.RelativePath))), "source conflict leaves destination schema and source unchanged");
await File.WriteAllTextAsync(sourceDefinitionPath, sourceDefinitionText);
var addedSourceFile = Path.Combine(savedGroup.ProjectRoot, "src/Pages/NewDependency.cs");
await File.WriteAllTextAsync(addedSourceFile, "// a dependency added after the import was staged\n");
await Reject(() => TemplateObjectImporter.CommitAsync(targetRoot, customPlan.Definition, [customPlan], unchanged), "new source dependency makes snapshot stale");
File.Delete(addedSourceFile);
var occupied = Path.Combine(targetRoot, customPlan.Files[0].RelativePath);
Directory.CreateDirectory(Path.GetDirectoryName(occupied)!);
await File.WriteAllTextAsync(occupied, "user-owned conflicting source");
await Reject(() => TemplateObjectImporter.CommitAsync(targetRoot, customPlan.Definition, [customPlan], unchanged), "dependency appeared at import destination");
Check(await File.ReadAllTextAsync(occupied) == "user-owned conflicting source" && HashFile(targetPath) == unchanged, "dependency collision does not overwrite source or authoring definition");
File.Delete(occupied);
var staleDefinitionHash = HashFile(targetPath);
await File.AppendAllTextAsync(targetPath, "\n");
await Reject(() => TemplateObjectImporter.CommitAsync(targetRoot, customPlan.Definition, [customPlan], staleDefinitionHash), "stale target authoring hash");
await TemplateObjectImporter.CommitAsync(targetRoot, customPlan.Definition, [customPlan], HashFile(targetPath));
Check(customPlan.Files.All(file => File.Exists(Path.Combine(targetRoot, file.RelativePath))), "complete source module commits with authoring definition");
Check(HashFile(Path.Combine(customRoot, "src/Pages/Blocks/BadgeBlock.razor")) == sourceHash, "import never mutates original source");
Check(HashFile(Path.Combine(targetRoot, "src/Pages/Blocks/TextBlock.razor")) == ownedTextHash, "same-named import preserves destination source bytes");
var combinedGroup = await TemplateObjectLibrary.SaveDesignGroupAsync(root, targetRoot, "Combined collection");
var combinedObjects = TemplateObjectLibrary.ListObjects(root, combinedGroup.Id);
Check(combinedObjects.Single(item => item.Kind == "element" && item.Element!.Component.StartsWith("Hl") && item.Element.Component.EndsWith("TextBlock")).Category == "Text & content",
    "imported Text retains its category when a collection is saved as a Design Group");
Check(combinedObjects.Single(item => item.Kind == "element" && item.Element!.Component.StartsWith("Hl") && item.Element.Component.EndsWith("ImageBlock")).Category == "Media",
    "imported Image retains its category when a collection is saved as a Design Group");
var missingSourceDefinition = TemplateObjectLibrary.ReadDefinition(savedGroup.ProjectRoot);
missingSourceDefinition.Blocks.Add(new() { Id = "missing", Title = "Missing component", Component = "MissingBlock" });
await File.WriteAllTextAsync(sourceDefinitionPath, Json(missingSourceDefinition));
await Reject(() => TemplateObjectImporter.PrepareAsync(root, targetRoot, customPlan.Definition, [new(savedGroup.Id, "element-missing")]), "undeclared component source is rejected before any destination write");
Check(HashFile(Path.Combine(root, "Content/library.json")) == contentHash, "all library work preserves authoritative content bytes");
Console.WriteLine($"HYPERLIB_OBJECTS_OK assertions={checks} root={root} importedProject={targetRoot}");
