using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Upgrades a copied, actually shipped V2 project and verifies its running gateway is replaced.</summary>
public static class StudioGuidedUpgradeVerifier
{
    public static async Task<string> RunAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        var sourceWork = Path.GetFullPath(oldWorkRoot);
        var sourceRoot = Path.GetFullPath(Path.Combine(sourceWork, "../.."));
        if (!HyperlibWorkPreviewHostService.IsGeneratedLibrary(sourceRoot)) throw new InvalidDataException("Select an actual generated V2 Work fixture.");
        var protectedInputs = HashTree(sourceRoot);
        var copy = Path.Combine(Path.GetFullPath(output), "old-v2-copy-" + Guid.NewGuid().ToString("N"));
        if (!copy.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Upgrade fixtures must be isolated in a QA directory.");
        await CopyTreeAsync(sourceRoot, copy, ct);
        var project = Path.Combine(copy, Path.GetRelativePath(sourceRoot, sourceWork));
        var originalPipeline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(project, ".studio/pipeline.json"), ct));
        using (originalPipeline) Require(!originalPipeline.RootElement.TryGetProperty("pageTemplates", out _), "The input must predate named Page-template support.");
        var oldProgram = await File.ReadAllTextAsync(Path.Combine(copy, "src/HyperLib.Host/Program.cs"), ct);
        Require(!oldProgram.Contains("GetPageTemplate(stagedPipeline", StringComparison.Ordinal), "The input gateway must actually use the old single-template hook.");
        var before = await new ContentStore(copy).ReadAsync(ct);
        var snapshot = before.Pages.ToDictionary(pair => pair.Key, pair => ContentJson.Clone(pair.Value), StringComparer.Ordinal);
        var pipelineService = new HyperlibProjectPipelineService();
        var pipeline = (await pipelineService.ReadAsync(project, ct))!;
        using var host = new HyperlibWorkPreviewHostService();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        var oldEndpoint = await host.EnsureAsync(copy, ct);
        using (var oldResponse = JsonDocument.Parse(await client.GetStringAsync(new Uri(oldEndpoint, "api/studio/pipeline"), ct)))
            Require(!oldResponse.RootElement.TryGetProperty("pageTemplates", out _), "The first running gateway must expose its actual shipped old pipeline API.");
        var definition = (await pipelineService.ReadDefinitionAsync(project, ct))!;
        var gallery = new HyperlibNamedPageTemplateDefinition { Id = "upgrade-gallery", Name = "Upgrade Gallery", Definition = ContentJson.Clone(definition.PageTemplate) };
        gallery.Definition.BackgroundColor = "#e8edf5";
        gallery.Definition.HeaderStyle = new() { Text = "Upgraded gallery header", BackgroundColor = "#20344a", TextColor = "#ffffff", Padding = 32, Alignment = "center" };
        gallery.Definition.ContentWidth = "wide";
        definition.PageTemplates.Add(gallery);
        pipeline = await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project, definition, pipeline.Revision, before.Revision, ct);
        var afterUpgrade = await new ContentStore(copy).ReadAsync(ct);
        foreach (var saved in snapshot)
        {
            Require(afterUpgrade.Pages.TryGetValue(saved.Key, out var current), "An existing Page disappeared during a foundation upgrade.");
            Require(JsonSerializer.Serialize(current!.Blocks, ContentJson.Options) == JsonSerializer.Serialize(saved.Value.Blocks, ContentJson.Options)
                && JsonSerializer.Serialize(current.Sources, ContentJson.Options) == JsonSerializer.Serialize(saved.Value.Sources, ContentJson.Options),
                "An actual V2 upgrade must preserve existing authored blocks, media values, and source citations.");
        }
        var backups = Directory.GetFiles(Path.Combine(copy, ".studio/upgrades"), "Program.cs", SearchOption.AllDirectories);
        Require(backups.Any(path => File.ReadAllText(path) == oldProgram), "An explicit upgrade must retain the exact old gateway source outside the published Work.");
        Require(File.ReadAllText(Path.Combine(project, "Templates/StudioRuntime/HyperLib.Runtime/Components/DynamicPage.razor")).Contains("studio-guided-layout", StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(project, "src/Pages/wwwroot/work-theme.css")).Contains("grid-column:1/-1", StringComparison.Ordinal),
            "The copied old Work must acquire the guided renderer and full-width reusable chrome styles.");
        var newEndpoint = await host.EnsureAsync(copy, ct);
        Require(newEndpoint != oldEndpoint, "A generated source change must replace the owned gateway with a fresh loopback endpoint.");
        Require(await host.EnsureAsync(copy, ct) == newEndpoint, "An unchanged generated gateway must be reused after the one required restart.");
        using (var newResponse = JsonDocument.Parse(await client.GetStringAsync(new Uri(newEndpoint, "api/studio/pipeline"), ct)))
            Require(newResponse.RootElement.GetProperty("pageTemplates").GetArrayLength() == 2,
                "The restarted actual gateway must expose both named templates, not a stale in-memory old contract.");
        var currentState = JsonSerializer.Deserialize<LibraryState>(await client.GetStringAsync(new Uri(newEndpoint, "api/state"), ct), ContentJson.Options)!;
        var operations = new OperationBatch { ExpectedRevision = currentState.Revision, Description = "Prove the upgraded actual gateway chooses a named template", Operations =
            [new() { Kind = "createNode", Id = "upgraded-gallery-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Upgraded gallery Page",
                Appearance = new() { ["studioPageTemplateId"] = ContentJson.Value(gallery.Id) } }] };
        using var response = await client.PostAsync(new Uri(newEndpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(operations, ContentJson.Options), Encoding.UTF8, "application/json"), ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        Require(response.IsSuccessStatusCode, "The upgraded gateway failed to create a named-template Page: " + responseText);
        var committed = JsonSerializer.Deserialize<LibraryState>(responseText, ContentJson.Options)!;
        var node = committed.Nodes.Single(node => node.Id == "upgraded-gallery-page");
        var selected = pipeline.PageTemplates.Single(item => item.Id == gallery.Id);
        Require(node.Appearance["studioPageTemplateId"].GetString() == gallery.Id && node.Appearance["background"].GetString() == "#e8edf5"
            && node.Appearance["studioHeaderText"].GetString() == "Upgraded gallery header"
            && committed.Pages[node.Id].Blocks.Select(block => block.Id).SequenceEqual(selected.Blocks.Select(block => block.Id)),
            "An actual HTTP creation must use the chosen Gallery recipe; the old hook would silently clone the default instead.");
        operations.ExpectedRevision = committed.Revision;
        operations.Operations[0].Id = "unknown-template-page";
        operations.Operations[0].Appearance!["studioPageTemplateId"] = ContentJson.Value("unknown-template");
        operations.Operations[0].Page = ContentJson.Clone(committed.Pages[node.Id]);
        using var invalid = await client.PostAsync(new Uri(newEndpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(operations, ContentJson.Options), Encoding.UTF8, "application/json"), ct);
        Require(!invalid.IsSuccessStatusCode && !(await new ContentStore(copy).ReadAsync(ct)).Pages.ContainsKey("unknown-template-page"),
            "The real upgraded gateway must reject an unknown template even when a caller supplies an explicit Page payload.");
        var stableFingerprintEndpoint = await host.EnsureAsync(copy, ct);
        Require(stableFingerprintEndpoint == newEndpoint, "Content edits alone must not restart an unchanged gateway.");
        var inputsAfter = HashTree(sourceRoot);
        Require(protectedInputs.Count == inputsAfter.Count && protectedInputs.All(pair => inputsAfter.TryGetValue(pair.Key, out var hash) && hash == pair.Value),
            "Upgrade verification must not mutate the original shipped project fixture.");
        var result = "STUDIO_GUIDED_UPGRADE_OK actual-shipped-v2-input|old-api-observed|explicit-foundation-upgrade|authored-content-media-citations-preserved|original-source-backed-up|owned-guided-sdk|source-fingerprint-gateway-restart|new-api-observed|actual-http-named-template-creation|unknown-explicit-payload-rejected|unchanged-gateway-reused|original-fixture-unchanged";
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "studio-guided-upgrade-result.txt"), result + "\n" + project + "\n" + oldEndpoint + " -> " + newEndpoint, ct);
        return result;
    }

    private static bool SkipDirectory(string name) => name is "bin" or "obj" or ".git" or "node_modules" or "runtime" or "scaffolding";
    internal static Dictionary<string, string> HashTree(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Visit(root);
        return result;
        void Visit(string current)
        {
            VisualPageProjectService.RejectReparsePoint(current);
            foreach (var path in Directory.EnumerateFiles(current))
            {
                if (Path.GetFileName(path) == "store.lock") continue;
                VisualPageProjectService.RejectReparsePoint(path);
                result[Path.GetRelativePath(root, path)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            }
            foreach (var path in Directory.EnumerateDirectories(current)) if (!SkipDirectory(Path.GetFileName(path))) Visit(path);
        }
    }
    internal static async Task CopyTreeAsync(string root, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        await Visit(root);
        async Task Visit(string current)
        {
            VisualPageProjectService.RejectReparsePoint(current);
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(root, current)));
            foreach (var path in Directory.EnumerateFiles(current))
            {
                if (Path.GetFileName(path) == "store.lock") continue;
                VisualPageProjectService.RejectReparsePoint(path);
                var target = Path.Combine(destination, Path.GetRelativePath(root, path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = File.OpenRead(path);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
            foreach (var path in Directory.EnumerateDirectories(current)) if (!SkipDirectory(Path.GetFileName(path))) await Visit(path);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
