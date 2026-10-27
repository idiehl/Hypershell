using HyperLib.Core;
using HyperLib.Runtime;

var requestedRoot = args.FirstOrDefault() ?? "Hyperlib";
var sourceRoot = Path.GetFullPath(requestedRoot);
for(var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); !Directory.Exists(Path.Combine(sourceRoot,"Starters")) && directory is not null; directory = directory.Parent)
    sourceRoot = Path.GetFullPath(Path.Combine(directory.FullName, requestedRoot));
var fixture = Path.Combine(Path.GetTempPath(), "HyperLib.Runtime.Verifier", Guid.NewGuid().ToString("N"));
foreach(var folder in new[] { "Starters", "src/HyperLib.Core", "src/HyperLib.Runtime" }) Copy(Path.Combine(sourceRoot,folder),Path.Combine(fixture,folder));
var state = new LibraryState
{
    Nodes = [new() { Id="test-work", Type="work", Title="Runtime verification", WorkId="test-work", ProjectPath="Works/test-work", Appearance = new() { ["accent"]=ContentJson.Value("#336699") } }],
    Pages = new() { ["test-page"] = TemplateValidator.CreateDefaultPage("test-page","test-work","Runtime verification") }
};
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var gateway = builder.Build();
gateway.MapGet("/api/state",()=>state);
gateway.MapGet("/api/renderer/page/{id}",(string id)=>state.Pages[id]);
await gateway.StartAsync();
var gatewayUrl = gateway.Urls.Single();
var assertions=0;
HyperLibRuntimeSupervisor? runtime = null;
try
{
    var appearanceTemplate = StarterTemplate.Create();
    var textDefinition = appearanceTemplate.Blocks.Single(block => block.Id == "text");
    textDefinition.Fields.RemoveAll(field => field.Id is "span" or "font" or "align" or "color");
    textDefinition.Fields.AddRange([
        new() { Id = "span", Type = "select", DefaultValue = ContentJson.Value("half"), Options = ["full", "half"] },
        new() { Id = "font", Type = "select", DefaultValue = ContentJson.Value("monospace"), Options = ["monospace", "system"] },
        new() { Id = "align", Type = "select", DefaultValue = ContentJson.Value("center"), Options = ["left", "center"] },
        new() { Id = "color", Type = "color", DefaultValue = ContentJson.Value("#112233") }
    ]);
    var options = new RendererOptions { PackageId = "test-work", GenerationId = "fixture", GatewayUrl = gatewayUrl, PathBase = "", Template = appearanceTemplate, Components = new Dictionary<string, Type>() };
    var savedBlock = new PageBlock { Id = "inherited", Type = "text", Region = "main" };
    var effectiveBlock = options.WithFieldDefaults(savedBlock);
    Require(RendererOptions.BlockSpanClass(effectiveBlock) == "hl-span-half", "Inherited width was not applied to the element wrapper.");
    Require(RendererOptions.BlockStyle(effectiveBlock).Contains("text-align:center") && RendererOptions.BlockStyle(effectiveBlock).Contains("#112233")
        && RendererOptions.BlockStyle(effectiveBlock).Contains("Cascadia Code"), "Inherited font, alignment, or color was not rendered.");
    Require(savedBlock.Values.Count == 0, "Rendering defaults changed authoritative page content.");
    savedBlock.Values["align"] = ContentJson.Value("left");
    Require(RendererOptions.BlockStyle(options.WithFieldDefaults(savedBlock)).Contains("text-align:left"), "Explicit page values lost priority over template defaults.");
    var duplicatedVideo = ContentJson.Clone(appearanceTemplate.Blocks.Single(block => block.Id == "video"));
    duplicatedVideo.Id = "lesson-video"; appearanceTemplate.Blocks.Add(duplicatedVideo);
    Require(options.BlockTypeClass(new() { Type = "lesson-video" }).Contains("hl-type-video"), "A reusable video element lost its component styling.");
    runtime = new(fixture); await runtime.EnsureProjectsAsync(state);
    var legacyAppearancePath = Path.Combine(fixture, "Works/test-work/authoring.template.json");
    var legacyAppearanceSchema = System.Text.Json.JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(legacyAppearancePath), ContentJson.Options)!;
    legacyAppearanceSchema.Appearance.RemoveAll(field => field.Id == "fontSize");
    var legacyAppearanceSource = System.Text.Json.JsonSerializer.Serialize(legacyAppearanceSchema, ContentJson.Options);
    File.WriteAllText(legacyAppearancePath, legacyAppearanceSource);
    state.Nodes.Single(node => node.Id == "test-work").Appearance["fontSize"] = ContentJson.Value(22);
    await runtime.EnsureProjectsAsync(state);
    Require(runtime.GetTemplate("test-work")?.Appearance.Single(field => field.Id == "fontSize").DefaultValue?.GetDouble() == 18
        && File.ReadAllText(legacyAppearancePath) == legacyAppearanceSource,
        "Reading an older Work contract must expose standard text size without rewriting its independent source.");
    var first = await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    using var http = new HttpClient();
    var text = await http.GetStringAsync(first.UpstreamUrl+first.ProxyPrefix+"page/test-page");
    Require(text.Contains("Runtime verification"),"The renderer did not consume dynamic content.");
    Require(text.Contains("--hl-text-size:22px"), "An older compiled Work did not render its saved base text size.");
    state.Pages["test-page"].Title="Content changed without a build";
    state.Nodes.Single(node => node.Id == "test-work").Appearance["fontSize"] = ContentJson.Value(24);
    await runtime.EnsureProjectsAsync(state);
    var same = await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    Require(first.GenerationId==same.GenerationId,"Content edits triggered a code build.");
    text=await http.GetStringAsync(same.UpstreamUrl+same.ProxyPrefix+"page/test-page");
    Require(text.Contains("Content changed without a build"),"The renderer served stale content.");
    Require(text.Contains("--hl-text-size:24px"), "Changing Work text size did not reach its renderer without a source build.");
    var codePath=Path.Combine(fixture,"Works/test-work/src/Pages/Blocks/TextBlock.razor");
    var good=File.ReadAllText(codePath);
    File.WriteAllText(codePath,good+"\n@code { this cannot compile; }");
    var failed=await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    Require(failed.GenerationId==first.GenerationId && runtime.GetStatuses().Single(s=>s.PackageId=="test-work").State=="active-with-errors","Failed build replaced the last working renderer.");
    await runtime.DisposeAsync(); runtime=new(fixture); await runtime.EnsureProjectsAsync(state);
    var restarted=await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    Require(restarted.GenerationId==first.GenerationId,"Restart failed to recover the last working compiled release.");
    File.WriteAllText(codePath,good+"\n@* Valid changed code *@");
    var changed=await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    Require(changed.GenerationId!=first.GenerationId,"Valid template changes did not activate.");
    File.WriteAllText(codePath,good+"\n@* Retry this unchanged saved source after cancellation *@");
    using (var canceledBuild = new CancellationTokenSource())
    {
        var canceled = false;
        void CancelCandidate(RuntimeStatus status)
        {
            if (status.PackageId == "test-work" && status.State == "building") canceledBuild.Cancel();
        }
        runtime.StatusChanged += CancelCandidate;
        try { await runtime.EnsureRuntimeAsync("test-work", gatewayUrl, canceledBuild.Token); }
        catch (OperationCanceledException) when (canceledBuild.IsCancellationRequested) { canceled = true; }
        finally { runtime.StatusChanged -= CancelCandidate; }
        Require(canceled && runtime.GetRuntime("test-work")?.GenerationId == changed.GenerationId,
            "Canceling a replacement build did not retain the previous renderer.");
        var retried = await runtime.EnsureRuntimeAsync("test-work", gatewayUrl);
        Require(retried.GenerationId != changed.GenerationId,
            "Retrying the exact same saved source after cancellation kept the obsolete renderer.");
    }
    var activated=new List<string>();
    runtime.GenerationChanged += generation => { if(generation.PackageId=="test-work") lock(activated) activated.Add(generation.GenerationId); };
    File.WriteAllText(codePath,good+"\n@* superseded edit *@");
    var pending=runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    await Task.Delay(350);
    File.WriteAllText(codePath,good+"\n@* newest edit *@");
    await pending;
    var newest=await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    foreach(var id in activated)
    {
        var snapshot=Path.Combine(fixture,$".hyperlib/runtime/test-work/{id}/source/Works/test-work/src/Pages/Blocks/TextBlock.razor");
        Require(!File.ReadAllText(snapshot).Contains("superseded edit"),"An obsolete build activated after a newer source save.");
    }
    Require(File.ReadAllText(Path.Combine(fixture,$".hyperlib/runtime/test-work/{newest.GenerationId}/source/Works/test-work/src/Pages/Blocks/TextBlock.razor")).Contains("newest edit"),"Latest saved source was not activated.");
    var schemaPath=Path.Combine(fixture,"Works/test-work/authoring.template.json");
    var schema=System.Text.Json.JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(schemaPath),ContentJson.Options)!;
    schema.Appearance.RemoveAll(f=>f.Id=="accent");
    File.WriteAllText(schemaPath,System.Text.Json.JsonSerializer.Serialize(schema,ContentJson.Options));
    var incompatible=await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    Require(incompatible.GenerationId==newest.GenerationId,"Incompatible appearance schema replaced a working release.");
    Require(!SafeRichText.Render("<script>alert(1)</script> **bold** [unsafe](javascript:alert(1))").Contains("<script>",StringComparison.Ordinal),"Rich text permitted raw executable markup.");
    var presentationTemplates = Path.Combine(fixture, "Templates", "presentation-parts");
    Directory.CreateDirectory(presentationTemplates);
    var presentationFragment = Path.Combine(presentationTemplates, "navigation.html");
    File.WriteAllText(presentationFragment, "<nav>Library presentation source</nav>");
    var libraryBefore = await runtime.EnsureRuntimeAsync("library", gatewayUrl);
    var reusable = await ReusableTemplateManager.CreateBlankAsync(fixture, "Independent reusable template");
    var reusableProject = ReusableTemplateCatalog.GetProjectRoot(fixture, reusable.Id);
    File.AppendAllText(Path.Combine(reusableProject, "Shared", "theme.css"), "\n/* catalog-only change */\n");
    var pendingRoot = Path.Combine(fixture, "Templates", ".pending-verifier", "Project");
    Directory.CreateDirectory(pendingRoot);
    File.WriteAllText(Path.Combine(pendingRoot, "partial-source.cs"), "Incomplete reusable template source");
    var incompleteCatalog = Path.Combine(fixture, "Templates", "incomplete-catalog");
    Directory.CreateDirectory(incompleteCatalog);
    File.WriteAllText(Path.Combine(incompleteCatalog, "template.json"), "malformed catalog metadata");
    var libraryAfterCatalog = await runtime.EnsureRuntimeAsync("library", gatewayUrl);
    Require(libraryAfterCatalog.GenerationId == libraryBefore.GenerationId,
        "Creating or editing reusable catalog templates rebuilt the unrelated library presentation.");
    var librarySnapshot = Path.Combine(fixture, ".hyperlib", "runtime", "library", libraryBefore.GenerationId, "source", "Templates");
    Require(File.Exists(Path.Combine(librarySnapshot, "presentation-parts", "navigation.html"))
        && !Directory.Exists(Path.Combine(librarySnapshot, reusable.Id))
        && !Directory.Exists(Path.Combine(librarySnapshot, "incomplete-catalog"))
        && !Directory.Exists(Path.Combine(librarySnapshot, ".pending-verifier")),
        "Library source snapshots did not distinguish presentation fragments from reusable catalog packages.");
    File.AppendAllText(presentationFragment, "\n<p>Changed library presentation</p>");
    var libraryAfterSource = await runtime.EnsureRuntimeAsync("library", gatewayUrl);
    Require(libraryAfterSource.GenerationId != libraryBefore.GenerationId,
        "A genuine library presentation template edit did not rebuild the library renderer.");
    var latestLibrarySnapshot = Path.Combine(fixture, ".hyperlib", "runtime", "library", libraryAfterSource.GenerationId, "source", "Templates");
    Require(File.ReadAllText(Path.Combine(latestLibrarySnapshot, "presentation-parts", "navigation.html")).Contains("Changed library presentation")
        && !Directory.Exists(Path.Combine(latestLibrarySnapshot, reusable.Id))
        && !Directory.Exists(Path.Combine(latestLibrarySnapshot, "incomplete-catalog"))
        && !Directory.Exists(Path.Combine(latestLibrarySnapshot, ".pending-verifier")),
        "The new library generation copied reusable catalog projects or missed its own changed template source.");
    // A Work may own ordinary template source with the same filenames as catalog metadata.
    schema.Appearance.Add(new() { Id = "accent", Type = "color", DefaultValue = ContentJson.Value("#336699") });
    File.WriteAllText(schemaPath,System.Text.Json.JsonSerializer.Serialize(schema,ContentJson.Options));
    var workTemplates = Path.Combine(fixture, "Works", "test-work", "Templates", "owned", "Project");
    Directory.CreateDirectory(workTemplates);
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(workTemplates)!, "template.json"), "{}");
    File.WriteAllText(Path.Combine(workTemplates, "visual-page.project.json"), "{}");
    File.WriteAllText(Path.Combine(workTemplates, "authoring.template.json"), "{}");
    var workWithTemplates = await runtime.EnsureRuntimeAsync("test-work", gatewayUrl);
    Require(File.Exists(Path.Combine(fixture, ".hyperlib", "runtime", "test-work", workWithTemplates.GenerationId, "source", "Works", "test-work", "Templates", "owned", "Project", "authoring.template.json")),
        "Library catalog filtering removed legitimate Work-owned template source.");
    Console.WriteLine($"HYPERLIB_RUNTIME_OK|assertions:{assertions}|dynamic-content|no-content-build|last-working|restart-recovery|latest-save|canceled-build-retry|catalog-isolation|work-template-source|appearance-compatibility|rich-text");
}
finally
{
    if(runtime is not null) await runtime.DisposeAsync();
    await gateway.StopAsync();
    Console.WriteLine("Runtime fixture: "+fixture);
}
void Require(bool success,string message) { assertions++; if(!success)throw new InvalidOperationException(message); }
static void Copy(string source,string target)
{
    if(File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException("Fixture sources must not contain filesystem links.");
    Directory.CreateDirectory(target);
    foreach(var file in Directory.EnumerateFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
    foreach(var child in Directory.EnumerateDirectories(source))if(Path.GetFileName(child) is not ("bin" or "obj" or ".git"))Copy(child,Path.Combine(target,Path.GetFileName(child)));
}
