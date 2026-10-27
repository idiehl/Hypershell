using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HyperLib.Core;
using HyperLib.Runtime;

var sourceRoot = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "Hyperlib"));
var outputRoot = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "hyperlib-library-verifier"));
Directory.CreateDirectory(outputRoot);
var fixture = Path.Combine(outputRoot, "fixture-" + Guid.NewGuid().ToString("N"));
var checks = new List<CheckResult>();
var skipped = new List<string>();
void Check(bool passed, string description, string? detail = null) => checks.Add(new(description, passed, detail));
string Json<T>(T value) => JsonSerializer.Serialize(value, ContentJson.Options);
T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), ContentJson.Options)!;
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Project(string id) => id == "library" ? fixture : Path.Combine(fixture, "Works", id);
string TemplatePath(string id) => Path.Combine(Project(id), id == "library" ? "Presentation/authoring.template.json" : "authoring.template.json");
TemplateDefinition Template(string id) => Read<TemplateDefinition>(TemplatePath(id));
OperationBatch Batch(LibraryState state, params ContentOperation[] operations) => new() { ExpectedRevision = state.Revision, Description = "Library integration verifier", Operations = operations.ToList() };
async Task Write<T>(string path, T value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, Json(value)); }

void CopyTree(string from, string to, bool skipContent = false)
{
    Directory.CreateDirectory(to);
    foreach (var file in Directory.EnumerateFiles(from))
    {
        if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Unexpected source link: " + file);
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
    }
    foreach (var directory in Directory.EnumerateDirectories(from))
    {
        var name = Path.GetFileName(directory);
        if (name is "bin" or "obj" or ".git" or ".hyperlib" || skipContent && name == "Content") continue;
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Unexpected source link: " + directory);
        CopyTree(directory, Path.Combine(to, name), skipContent);
    }
}

try
{
    // Only source is copied. Every content mutation below belongs to a fresh, isolated fixture.
    CopyTree(Path.Combine(sourceRoot, "src"), Path.Combine(fixture, "src"));
    CopyTree(Path.Combine(sourceRoot, "Starters"), Path.Combine(fixture, "Starters"));
    CopyTree(Path.Combine(sourceRoot, "Presentation"), Path.Combine(fixture, "Presentation"));
    foreach (var directory in new[] { "Pages", "Shared", "Templates" })
        if (Directory.Exists(Path.Combine(sourceRoot, directory))) CopyTree(Path.Combine(sourceRoot, directory), Path.Combine(fixture, directory));
    if (File.Exists(Path.Combine(sourceRoot, "visual-page.project.json"))) File.Copy(Path.Combine(sourceRoot, "visual-page.project.json"), Path.Combine(fixture, "visual-page.project.json"));
    foreach (var id in new[] { "animal-atlas", "wave-lab" })
        CopyTree(Path.Combine(sourceRoot, "Works", id), Project(id), skipContent: true);
    if (File.Exists(Path.Combine(sourceRoot, "hyperlib.app.json"))) File.Copy(Path.Combine(sourceRoot, "hyperlib.app.json"), Path.Combine(fixture, "hyperlib.app.json"));

    var store = new ContentStore(fixture);
    await store.InitializeAsync();
    var state = await store.ReadAsync();
    Check(state.Pages.Count == 2, "fresh fixture has exactly the two original pages");
    // Preserve a content edit and a source schema edit across the installation.
    var page = ContentJson.Clone(state.Pages["woodland-habitat"]);
    page.Blocks[0].Values["text"] = ContentJson.Value("An owner-authored observation that the installer must preserve.");
    state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "savePage", Id = page.Id, Page = page }));
    var woodlandTemplate = Template("animal-atlas");
    woodlandTemplate.Blocks.Single(block => block.Id == "text").Fields.Single(field => field.Id == "text").Title = "Owner field label";
    await Write(TemplatePath("animal-atlas"), woodlandTemplate);
    var legacyFont = Template("library").Appearance.FirstOrDefault(field => field.Id == "font")?.DefaultValue;
    var before = ContentJson.Clone(state);

    const string stockShell = "@namespace HyperLib.Work.Generated\n@page \"/presentation\"\n<div class=\"hyperlib-studio-shell\"><p class=\"hl-kicker\">HYPERLIB</p></div>\n";
    const string customShell = stockShell + "<p class=\"my-shell\">Owner-customized shell</p>\n";
    foreach (var id in new[] { "library", "animal-atlas", "wave-lab" })
    {
        var project = Project(id);
        Directory.CreateDirectory(Path.Combine(project, "src/Pages/Generated"));
        await File.WriteAllTextAsync(Path.Combine(project, "src/Pages/Generated/WorkPage.razor"), id == "wave-lab" ? customShell : stockShell);
        var documentPath = Path.Combine(project, "Pages/presentation.visual-page.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(documentPath))!;
        document["root"]!["children"] = new JsonArray(JsonSerializer.SerializeToNode(new { elementId = "stock-kicker", descriptorId = "core.text", text = "HYPERLIB" }, ContentJson.Options));
        await File.WriteAllTextAsync(documentPath, document.ToJsonString(ContentJson.Options));
    }

    state = await BaseTemplateLibraryInstaller.InstallAsync(fixture, store);
    Check(state.Revision == before.Revision + 1, "installation is a single content revision");
    Check(state.Pages.Count == before.Pages.Count + 15 && state.Nodes.Count(node => node.Type == "work") == 17, "installer adds fifteen demo Works and pages");
    Check(state.Nodes.Count(node => node.ParentId == BaseTemplateLibraryInstaller.CollectionId && node.Type == "work") == 15, "all fifteen demos belong to Base Template Library");
    var exported = Directory.GetDirectories(Path.Combine(fixture, "BaseTemplates"));
    Check(exported.Length == 17, "seventeen reusable catalog directories are exported");
    foreach (var info in BaseTemplateCatalog.List())
    {
        var package = Path.Combine(fixture, "BaseTemplates", info.Id);
        var definition = Read<TemplateDefinition>(Path.Combine(package, "authoring.template.json"));
        var defaultPage = Read<PageDocument>(Path.Combine(package, "default-page.json"));
        TemplateValidator.ValidatePage(definition, defaultPage);
        Check(File.Exists(Path.Combine(package, "Project/visual-page.project.json")) && File.Exists(Path.Combine(package, "Project/src/Host/Host.csproj")), info.Id + " includes an editable Studio project and valid default page");
        Check(Directory.Exists(Path.Combine(package, "Project/Shared")), info.Id + " includes shared project styles");
    }
    foreach (var original in before.Pages.Values)
    {
        var installed = state.Pages[original.Id];
        Check(original.Blocks.All(block => Json(installed.Blocks.Single(item => item.Id == block.Id)) == Json(block)), original.Id + " preserves every original block identity and value");
        Check(Json(installed.Sources) == Json(original.Sources) && installed.Title == original.Title, original.Id + " preserves sources and title");
        Check(installed.Blocks.Count(block => block.Type == "header") == 1 && installed.Blocks.Count(block => block.Type == "footer") == 1, original.Id + " gains exactly one editable header and footer");
    }
    Check(Template("animal-atlas").Blocks.Single(block => block.Id == "text").Fields.Single(field => field.Id == "text").Title == "Owner field label", "schema upgrade preserves owner field labels");
    Check(Template("library").Appearance.Single(field => field.Id == "font").DefaultValue?.GetRawText() == legacyFont?.GetRawText(), "library legacy font default is retained");
    foreach (var id in new[] { "library", "animal-atlas" })
    {
        var shell = await File.ReadAllTextAsync(Path.Combine(Project(id), "src/Pages/Generated/WorkPage.razor"));
        var document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Project(id), "Pages/presentation.visual-page.json")))!;
        Check(!shell.Contains("HYPERLIB", StringComparison.Ordinal) && document["root"]!["children"]!.AsArray().Count == 0, id + " removes only the known stock kicker in source and Studio document");
        Check(File.Exists(Path.Combine(fixture, ".hyperlib/recovery/base-template-library-v1", id, "src/Pages/Generated/WorkPage.razor")), id + " retains a recovery copy of the stock shell");
    }
    Check(await File.ReadAllTextAsync(Path.Combine(Project("wave-lab"), "src/Pages/Generated/WorkPage.razor")) == customShell, "customized shell remains byte-for-byte unchanged");

    var installedJson = Json(state);
    var second = await BaseTemplateLibraryInstaller.InstallAsync(fixture, store);
    Check(Json(second) == installedJson, "repeating the install is content-idempotent");
    var marker = Path.Combine(fixture, ".hyperlib/base-template-library-v1.json");
    var markerText = await File.ReadAllTextAsync(marker);
    state = await new ContentStore(fixture).UndoAsync(state.Revision);
    Check(state.Pages.Count == before.Pages.Count && state.Nodes.Count == before.Nodes.Count, "undo removes every demo and restores original counts");
    Check(before.Pages.All(pair => Json(state.Pages[pair.Key].Blocks) == Json(pair.Value.Blocks) && Json(state.Pages[pair.Key].Sources) == Json(pair.Value.Sources)), "undo restores original page blocks and citations exactly");
    var undone = Json(state);
    Check(Json(await BaseTemplateLibraryInstaller.InstallAsync(fixture, new ContentStore(fixture))) == undone, "normal startup/install does not silently reverse the user's undo");
    // Record the rendering implication without starting an app or browser.
    var dynamicPage = await File.ReadAllTextAsync(Path.Combine(sourceRoot, "src/HyperLib.Runtime/Components/DynamicPage.razor"));
    var headerFallbackRequiresEmptyDefaults = dynamicPage.Contains("Options.Template.DefaultBlocks.Count == 0", StringComparison.Ordinal);
    var undoHasNoHeader = state.Pages.Values.All(p => !p.UsesEditableChrome && p.Blocks.All(b => b.Type is not "header" and not "banner"));
    Check(!(headerFallbackRequiresEmptyDefaults && undoHasNoHeader && Template("animal-atlas").DefaultBlocks.Count > 0), "undo retains visible headings for legacy original pages", "Source contract audit: migrated template defaults remain after content undo.");
    state = await new ContentStore(fixture).RedoAsync(state.Revision);
    Check(state.Pages.Count == 17 && state.Pages["woodland-habitat"].Blocks.Count == before.Pages["woodland-habitat"].Blocks.Count + 2, "redo restores the full demo collection and editable headings");

    // Simulate interruption after the durable content commit but before the sidecar marker.
    File.Delete(marker);
    try
    {
        var recovered = await BaseTemplateLibraryInstaller.InstallAsync(fixture, new ContentStore(fixture));
        Check(recovered.Revision == state.Revision && File.Exists(marker), "installer recovers a missing marker after a completed content commit");
    }
    catch (Exception error) { Check(false, "installer recovers a missing marker after a completed content commit", error.GetType().Name + ": " + error.Message); }
    await File.WriteAllTextAsync(marker, markerText);

    await using var runtime = new HyperLibRuntimeSupervisor(fixture);
    await runtime.EnsureProjectsAsync(state);
    Check(runtime.GetStatuses().Count == 18, "runtime registers root presentation plus seventeen Work projects without launching them");
    var projectIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var work in state.Nodes.Where(node => node.Type == "work"))
    {
        var project = Project(work.Id);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(project, "visual-page.project.json")))!;
        Check(projectIds.Add(manifest["projectId"]!.GetValue<string>()), work.Id + " owns a unique Studio project identity");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(project, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var xml = XDocument.Load(file);
            foreach (var reference in xml.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")!.Value.Replace('/', Path.DirectorySeparatorChar);
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, include));
                Check(File.Exists(resolved) && resolved.StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), work.Id + " project reference resolves inside its library", resolved);
            }
        }
    }
    var firstWorkSource = Path.Combine(Project("demo-portfolio-grid"), "src/Pages/Blocks/HeaderBlock.razor");
    var otherWorkSource = Path.Combine(Project("demo-field-journal"), "src/Pages/Blocks/HeaderBlock.razor");
    var catalogSource = Path.Combine(fixture, "BaseTemplates/portfolio-grid/Project/src/Pages/Blocks/HeaderBlock.razor");
    var otherHash = Hash(otherWorkSource); var catalogHash = Hash(catalogSource);
    await File.AppendAllTextAsync(firstWorkSource, "\n@* Owner modification preserved by future scaffolding. *@\n");
    var changedHash = Hash(firstWorkSource);
    await runtime.EnsureProjectsAsync(state);
    Check(Hash(firstWorkSource) == changedHash && Hash(otherWorkSource) == otherHash && Hash(catalogSource) == catalogHash, "Work source edits persist and remain independent from sibling Works and reusable source");

    var selectedTemplate = Template("demo-portfolio-grid");
    selectedTemplate.DefaultBlocks = [new() { Id = "owned-default", Type = "header", Region = "main", Values = new() { ["title"] = ContentJson.Value("Owner template heading"), ["text"] = ContentJson.Value("Owner template content") } }];
    await Write(TemplatePath("demo-portfolio-grid"), selectedTemplate);
    store = new ContentStore(fixture) { TemplateResolver = id => Template(id) };
    state = await store.ApplyAsync(Batch(state, new ContentOperation { Kind = "createNode", Id = "owned-content-copy", NodeType = "page", ParentId = "demo-portfolio-grid", Title = "An independent page" }));
    Check(state.Pages["owned-content-copy"].Blocks.Count == 1 && state.Pages["owned-content-copy"].Blocks[0].Values["text"].GetString() == "Owner template content", "Add Page reuses the independently edited Work template defaults");
    await runtime.EnsureProjectsAsync(state);
    Check(Template("demo-portfolio-grid").DefaultBlocks.Count == 1, "project synchronization preserves independently edited template defaults");
    var restarted = await new ContentStore(fixture).ReadAsync();
    Check(Json(restarted.Pages["owned-content-copy"]) == Json(state.Pages["owned-content-copy"]), "content edits and default copies persist after reopening the store");

    var reusableRoot = Path.Combine(fixture, "BaseTemplates/portfolio-grid/Project");
    var reusableCss = Path.Combine(reusableRoot, "Shared/theme.css");
    await File.AppendAllTextAsync(reusableCss, "\n/* Reusable source customization */\n");
    var binary = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 255, 0, 128, 254, 233, 172 };
    var binaryRelative = "src/Pages/wwwroot/fixture-binary.png";
    await File.WriteAllBytesAsync(Path.Combine(reusableRoot, binaryRelative), binary);
    var reusableSchemaPath = Path.Combine(reusableRoot, "authoring.template.json");
    var reusableSchema = Read<TemplateDefinition>(reusableSchemaPath);
    reusableSchema.DefaultBlocks = [new() { Id = "reusable-owner-default", Type = "header", Region = "main", Values = new() { ["title"] = ContentJson.Value("Reusable heading"), ["text"] = ContentJson.Value("Reusable schema customization") } }];
    reusableSchema.Appearance.Single(field => field.Id == "accent").DefaultValue = ContentJson.Value("#13579b");
    await Write(reusableSchemaPath, reusableSchema);
    state = await store.ApplyAsync(Batch(state,
        new ContentOperation { Kind = "createNode", Id = "dynamic-copy", NodeType = "work", ParentId = BaseTemplateLibraryInstaller.CollectionId, Title = "My copied starter", StarterId = "portfolio-grid" },
        new ContentOperation { Kind = "createNode", Id = "dynamic-copy-page", NodeType = "page", ParentId = "dynamic-copy", Title = "Fresh from the starter" }));
    await runtime.EnsureProjectsAsync(state);
    Check(runtime.GetStatuses().Count == 19 && state.Pages["dynamic-copy-page"].Blocks.Count == 1, "dynamic selected-starter Work creates and registers a valid independent project");
    Check(state.Pages["dynamic-copy-page"].Blocks[0].Values["text"].GetString() == "Reusable schema customization" && state.Nodes.Single(node => node.Id == "dynamic-copy").Appearance["accent"].GetString() == "#13579b", "same-batch creation uses edited reusable schema and appearance");
    Check(Json(Template("dynamic-copy")) == Json(reusableSchema), "new Work receives an independent copy of the canonical reusable schema");
    Check((await File.ReadAllTextAsync(Path.Combine(Project("dynamic-copy"), "Shared/theme.css"))).Contains("Reusable source customization", StringComparison.Ordinal), "new Work copies customized reusable project CSS");
    Check((await File.ReadAllBytesAsync(Path.Combine(Project("dynamic-copy"), binaryRelative))).SequenceEqual(binary), "new Work copies binary project assets without transcoding");
    var dynamicManifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Project("dynamic-copy"), "visual-page.project.json")))!;
    Check(projectIds.Add(dynamicManifest["projectId"]!.GetValue<string>()) && dynamicManifest["hyperlibPackageId"]!.GetValue<string>() == "dynamic-copy", "dynamic Work gets fresh Studio identity and correct package mapping");
    reusableSchema.DefaultBlocks[0].Values["text"] = ContentJson.Value("Later reusable change");
    await Write(reusableSchemaPath, reusableSchema);
    await runtime.EnsureProjectsAsync(state);
    Check(Template("dynamic-copy").DefaultBlocks[0].Values["text"].GetString() == "Reusable schema customization", "later edits to reusable schema do not modify existing Work copies");

    for (var attempt = 0; attempt < 4; attempt++)
    {
        var concurrentRoot = Path.Combine(fixture, "Works", "concurrent-" + attempt);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ProjectScaffolder.EnsureAsync(fixture, concurrentRoot, "concurrent-" + attempt, "Concurrent copy", starterId: "portfolio-grid")));
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(concurrentRoot, "visual-page.project.json")))!;
        var document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(concurrentRoot, "Pages/presentation.visual-page.json")))!;
        Check(manifest["projectId"]!.GetValue<string>() == document["projectId"]!.GetValue<string>() && manifest["pages"]![0]!["pageId"]!.GetValue<string>() == document["pageId"]!.GetValue<string>(), "concurrent scaffold requests retain consistent Studio identities, attempt " + attempt);
    }
    var canceledRoot = Path.Combine(fixture, "Works", "canceled-copy");
    try
    {
        await ProjectScaffolder.EnsureAsync(fixture, canceledRoot, "canceled-copy", "Canceled copy", new CancellationToken(true), "portfolio-grid");
        Check(false, "canceled scaffold honors cancellation");
    }
    catch (OperationCanceledException) { Check(true, "canceled scaffold honors cancellation"); }
    await ProjectScaffolder.EnsureAsync(fixture, canceledRoot, "canceled-copy", "Canceled copy", starterId: "portfolio-grid");
    Check((await File.ReadAllBytesAsync(Path.Combine(canceledRoot, binaryRelative))).SequenceEqual(binary), "retry after canceled setup retains complete binary assets");
    Check(Directory.EnumerateFiles(canceledRoot, "*.csproj", SearchOption.AllDirectories).All(file => XDocument.Load(file).Root?.Name == "Project"), "retry after canceled setup has complete project files");
    await runtime.EnsureProjectsAsync(before);
    var retainedState = (LibraryState)typeof(HyperLibRuntimeSupervisor).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
    Check(retainedState.Revision == state.Revision, "late synchronization of an older revision cannot regress renderer compatibility state");

    var linkDestination = Path.Combine(fixture, "Works", "linked-destination", "src", "Pages");
    var linkTarget = Path.Combine(outputRoot, "link-target-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.GetDirectoryName(linkDestination)!);
    Directory.CreateDirectory(linkTarget);
    var createdLink = false;
    try { Directory.CreateSymbolicLink(linkDestination, linkTarget); createdLink = true; }
    catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { skipped.Add("Nested reparse-point test unavailable: " + error.Message); }
    if (createdLink)
    {
        try
        {
            await ProjectScaffolder.EnsureAsync(fixture, Path.Combine(fixture, "Works", "linked-destination"), "linked-destination", "Link rejection", starterId: "portfolio-grid");
            Check(false, "nested destination links are rejected before any outside write");
        }
        catch (IOException) { Check(!Directory.EnumerateFileSystemEntries(linkTarget).Any(), "nested destination links are rejected before any outside write"); }
        finally { Directory.Delete(linkDestination); }
    }

    var preeditedRoot = Path.Combine(outputRoot, "preedited-catalog-" + Guid.NewGuid().ToString("N"));
    CopyTree(Path.Combine(sourceRoot, "src"), Path.Combine(preeditedRoot, "src"));
    CopyTree(Path.Combine(sourceRoot, "Starters"), Path.Combine(preeditedRoot, "Starters"));
    var editedBeforeInstall = BaseTemplateCatalog.CreateTemplate("portfolio-grid");
    editedBeforeInstall.Blocks.Single(block => block.Id == "header").Fields.Add(new() { Id = "requiredOwnerField", Type = "string", Required = true });
    editedBeforeInstall.DefaultBlocks = [new() { Id = "owner-demo-heading", Type = "header", Region = "main", Values = new() { ["title"] = ContentJson.Value("Owner demo"), ["requiredOwnerField"] = ContentJson.Value("Preserve this reusable default") } }];
    editedBeforeInstall.Appearance.Single(field => field.Id == "accent").DefaultValue = ContentJson.Value("#123abc");
    await Write(Path.Combine(preeditedRoot, "BaseTemplates/portfolio-grid/Project/authoring.template.json"), editedBeforeInstall);
    var preeditedStore = new ContentStore(preeditedRoot);
    await preeditedStore.InitializeAsync();
    var preeditedState = await BaseTemplateLibraryInstaller.InstallAsync(preeditedRoot, preeditedStore);
    Check(preeditedState.Pages.Count == 17 && preeditedState.Pages["demo-portfolio-grid-page"].Blocks.Count == 1 && preeditedState.Pages["demo-portfolio-grid-page"].Blocks[0].Values["requiredOwnerField"].GetString() == "Preserve this reusable default", "initial demo installation uses edited canonical defaults including required custom fields");
    Check(preeditedState.Nodes.Single(node => node.Id == "demo-portfolio-grid").Appearance["accent"].GetString() == "#123abc", "initial demo installation preserves edited canonical appearance defaults");

    var outsideProject = Path.Combine(outputRoot, "outside-project-" + Guid.NewGuid().ToString("N"));
    try
    {
        await ProjectScaffolder.EnsureAsync(fixture, outsideProject, "escaped", "Escaped fixture project", starterId: "portfolio-grid");
        Check(false, "scaffolder rejects a project destination outside its library", "It wrote a project to the verifier-owned sibling directory.");
    }
    catch (ContentValidationException) { Check(!Directory.Exists(outsideProject), "scaffolder rejects a project destination outside its library before writing"); }
    catch (ArgumentException) { Check(!Directory.Exists(outsideProject), "scaffolder rejects a project destination outside its library before writing"); }
    catch (IOException) { Check(!Directory.Exists(outsideProject), "scaffolder rejects a project destination outside its library before writing"); }
}
catch (Exception error)
{
    Check(false, "verifier completed all scenarios", error.ToString());
}

var result = new { passed = checks.All(check => check.Passed), assertions = checks.Count, passedAssertions = checks.Count(check => check.Passed), fixture, sourceRoot,
    applicationsLaunched = false, checkedAtUtc = DateTimeOffset.UtcNow, skipped, checks };
var report = Path.Combine(outputRoot, "library-verification.json");
await File.WriteAllTextAsync(report, Json(result));
foreach (var check in checks.Where(check => !check.Passed)) Console.WriteLine("FAILED: " + check.Description + "\n" + check.Detail);
Console.WriteLine($"HYPERLIB_LIBRARY_{(result.passed ? "OK" : "FAILED")} assertions={checks.Count} passed={result.passedAssertions} report={report} fixture={fixture}");
return result.passed ? 0 : 1;

sealed record CheckResult(string Description, bool Passed, string? Detail);
