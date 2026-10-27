using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HyperLib.Core;

if (args.Length != 3) throw new ArgumentException("Pass Hyperlib source root, built HyperLib.Host.dll, and private output directory.");
var source = Path.GetFullPath(args[0]);
var hostDll = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var root = Path.Combine(output, "template-host-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
foreach (var directory in new[] { "src", "Starters" }) CopyTree(Path.Combine(source, directory), Path.Combine(root, directory));
File.Copy(Path.Combine(source, "hyperlib.app.json"), Path.Combine(root, "hyperlib.app.json"));
var checks = new List<string>();
void Check(bool valid, string name)
{
    if (!valid) throw new InvalidOperationException("FAILED: " + name);
    checks.Add(name);
    Console.WriteLine("PASS " + name);
}

var custom = await ReusableTemplateManager.CreateBlankAsync(root, "Runtime blank template");
var project = ReusableTemplateCatalog.GetProjectRoot(root, custom.Id);
await File.AppendAllTextAsync(Path.Combine(project, "Shared", "theme.css"), "\n/* independent-template-source */\n");
Directory.CreateDirectory(Path.Combine(project, "Assets"));
Directory.CreateDirectory(Path.Combine(project, "Styles"));
var assetBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 255, 0, 128, 254 };
await File.WriteAllBytesAsync(Path.Combine(project, "Assets", "source-image.png"), assetBytes);
await File.WriteAllTextAsync(Path.Combine(project, "Styles", "custom.css"), ".custom { color: #123456; }\n");
Check(ReusableTemplateCatalog.ReadDefinition(root, custom.Id).EmptyPageDefault, "blank template records intentional empty defaults");
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var origin = "http://127.0.0.1:" + port;
var info = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
    WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true
};
foreach (var argument in new[] { hostDll, "--root", root, "--port", port.ToString() }) info.ArgumentList.Add(argument);
using var host = Process.Start(info) ?? throw new InvalidOperationException("Host failed to start.");
var stdout = host.StandardOutput.ReadToEndAsync();
var stderr = host.StandardError.ReadToEndAsync();
using var client = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromMinutes(4) };
client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
client.DefaultRequestHeaders.Add("Origin", origin);
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        if (host.HasExited) throw new InvalidOperationException("Host exited: " + await stderr);
        try
        {
            using var health = await client.GetAsync("api/health", timeout.Token);
            if (health.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
                Check(json.RootElement.GetProperty("templateCatalogVersion").GetInt32() == 1, "gateway advertises reusable template support");
                break;
            }
        }
        catch (HttpRequestException) { }
        await Task.Delay(150, timeout.Token);
    }
    var catalog = await client.GetFromJsonAsync<List<ReusableTemplateInfo>>("api/base-templates", ContentJson.Options)
        ?? throw new InvalidOperationException("Gateway catalog is empty.");
    Check(catalog.Any(item => item.Id == custom.Id) && catalog.Any(item => item.Id == ReusableTemplateCatalog.BlankId), "gateway lists custom and blank starters");
    var state = (await client.GetFromJsonAsync<LibraryState>("api/state", ContentJson.Options))!;
    var workId = "template-probe-work";
    var pageId = "template-probe-home";
    var parent = state.Nodes.First(node => node.Type == "collection").Id;
    using var response = await client.PostAsJsonAsync("api/operations", new OperationBatch
    {
        ExpectedRevision = state.Revision, Description = "Verify custom template runtime",
        Operations =
        [
            new() { Kind = "createNode", Id = workId, NodeType = "work", ParentId = parent, Title = "Custom template Work", StarterId = custom.Id },
            new() { Kind = "createNode", Id = pageId, NodeType = "page", ParentId = workId, Title = "Home" }
        ]
    }, ContentJson.Options);
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    state = (await response.Content.ReadFromJsonAsync<LibraryState>(ContentJson.Options))!;
    Check(state.Pages[pageId].Blocks.Count == 0 && state.Pages[pageId].UsesEditableChrome, "gateway creates truly empty Home page");
    Check(File.ReadAllText(Path.Combine(root, "Works", workId, "Shared", "theme.css")).Contains("independent-template-source"), "gateway copies custom theme source");
    Check(File.ReadAllBytes(Path.Combine(root, "Works", workId, "Assets", "source-image.png")).SequenceEqual(assetBytes)
        && File.ReadAllText(Path.Combine(root, "Works", workId, "Styles", "custom.css")).Contains("#123456"), "template-to-Work copy retains binary Assets and authored Styles");
    using var runtimeResponse = await client.GetAsync("api/runtime/" + workId);
    var runtimeText = await runtimeResponse.Content.ReadAsStringAsync();
    if (!runtimeResponse.IsSuccessStatusCode) throw new InvalidOperationException(runtimeText);
    using var runtime = JsonDocument.Parse(runtimeText);
    var previewPath = runtime.RootElement.GetProperty("proxyPrefix").GetString() + "page/" + pageId + "?edit=0";
    var html = await client.GetStringAsync(previewPath);
    Check(html.Contains("hl-document") && html.Contains("data-region=\"main\"") && !html.Contains("hl-error"), "custom template compiles and renders through the gateway");
    Check(!html.Contains("hl-page-heading") && !html.Contains("data-block-id="), "blank renderer has no sample content or fallback heading");
    await File.WriteAllTextAsync(Path.Combine(output, "blank-runtime.html"), html);
    var copied = await ReusableTemplateManager.CreateFromWorkAsync(root, workId, "Captured Work design");
    Check(File.ReadAllText(Path.Combine(ReusableTemplateCatalog.GetProjectRoot(root, copied.Id), "Shared", "theme.css")).Contains("independent-template-source"), "capture Work preserves custom source");
    var capturedRoot = ReusableTemplateCatalog.GetProjectRoot(root, copied.Id);
    Check(File.ReadAllBytes(Path.Combine(capturedRoot, "Assets", "source-image.png")).SequenceEqual(assetBytes)
        && File.Exists(Path.Combine(capturedRoot, "Styles", "custom.css")), "captured Work template retains binary Assets and authored Styles");
    var independent = Path.Combine(root, "Works", workId, "Shared", "theme.css");
    var before = await File.ReadAllTextAsync(independent);
    await File.AppendAllTextAsync(Path.Combine(project, "Shared", "theme.css"), "\n/* newer template edit */\n");
    await TemplateProjectScaffolder.EnsureAsync(root, Path.Combine(root, "Works", workId), workId, "Custom template Work", starterId: custom.Id);
    Check(await File.ReadAllTextAsync(independent) == before, "template edits leave existing Work source independent");
    Console.WriteLine($"HYPERLIB_TEMPLATE_HOST_OK|assertions={checks.Count}|fixture={root}");
    await File.WriteAllTextAsync(Path.Combine(output, "template-host-result.json"), JsonSerializer.Serialize(new { checks, root, previewPath }, ContentJson.Options));
}
finally
{
    if (!host.HasExited) host.Kill(entireProcessTree: true);
    await host.WaitForExitAsync();
    await File.WriteAllTextAsync(Path.Combine(output, "template-host.stdout.log"), await stdout);
    await File.WriteAllTextAsync(Path.Combine(output, "template-host.stderr.log"), await stderr);
}

static void CopyTree(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (var file in Directory.EnumerateFiles(source))
    {
        if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Source contains a link.");
        File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }
    foreach (var directory in Directory.EnumerateDirectories(source))
    {
        if (Path.GetFileName(directory) is "bin" or "obj" or ".hyperlib" or "node_modules") continue;
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Source contains a link.");
        CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
