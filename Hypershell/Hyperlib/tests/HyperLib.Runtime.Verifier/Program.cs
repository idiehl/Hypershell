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
    runtime = new(fixture); await runtime.EnsureProjectsAsync(state);
    var first = await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    using var http = new HttpClient();
    var text = await http.GetStringAsync(first.UpstreamUrl+first.ProxyPrefix+"page/test-page");
    Require(text.Contains("Runtime verification"),"The renderer did not consume dynamic content.");
    state.Pages["test-page"].Title="Content changed without a build";
    await runtime.EnsureProjectsAsync(state);
    var same = await runtime.EnsureRuntimeAsync("test-work",gatewayUrl);
    Require(first.GenerationId==same.GenerationId,"Content edits triggered a code build.");
    text=await http.GetStringAsync(same.UpstreamUrl+same.ProxyPrefix+"page/test-page");
    Require(text.Contains("Content changed without a build"),"The renderer served stale content.");
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
    Console.WriteLine($"HYPERLIB_RUNTIME_OK|assertions:{assertions}|dynamic-content|no-content-build|last-working|restart-recovery|latest-save|appearance-compatibility|rich-text");
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
