using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioCompleteTemplateVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string message) { Require(value, message); checks.Add(message); }
        var gallery = new StudioCompleteTemplateService(sourceLibraryRoot);
        var choices = gallery.List();
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        Check(choices.Count > 25 && choices.Any(item => item.Id.StartsWith("visual:", StringComparison.Ordinal))
            && choices.Any(item => item.Id.StartsWith("hyperlib:", StringComparison.Ordinal)), "The complete gallery combines original Hyperpage pages and full Hyperlib layouts.");
        Check(choices.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == choices.Count
            && !choices.Any(item => item.Id == "hyperlib:blank"), "Canonical gallery identities are unique and exclude the empty starter.");
        Check(choices.Where(item => item.Id.StartsWith("visual:", StringComparison.Ordinal)).All(item => item.SupportedKinds.Count == 3)
            && choices.Where(item => item.Id.StartsWith("hyperlib:", StringComparison.Ordinal)).All(item => item.SupportedKinds.SequenceEqual(new[] { StudioProjectKind.Hyperlib })),
            "Original Hyperpage pages advertise all three targets while arbitrary C# templates advertise their supported Hyperlib target.");
        var audited = new List<object>();
        foreach (var choice in choices)
        {
            var request = gallery.BuildDefinition(choice.Id, "Complete template audit");
            Check(request.PipelineVersion == 2 && request.PageDefinitions.Count == 0 && request.PageTemplates.Count == 1
                && request.PageTemplate.CompleteTemplateObjectId == request.LandingTemplate.CompleteTemplateObjectId,
                choice.Title + " prepares an independent landing and reusable Page foundation without a mandatory Page count.");
            var toolkit = new List<HyperlibProjectToolkitItem>();
            TemplateDefinition? sourceSchema = null;
            foreach (var id in request.SelectedToolsetIds)
            {
                var split = id.IndexOf(':');
                var item = generator.GetCatalogObject(id[..split], id[(split + 1)..]);
                sourceSchema ??= ContentJson.Clone(item.Definition);
                toolkit.Add(new() { ChoiceId = id, Title = item.Title, Kind = item.Kind,
                    BlockTypeIds = item.Kind == "layout" ? item.Blocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToList() : item.Element is null ? [] : [item.Element.Id],
                    LayoutBlocks = ContentJson.Clone(item.Blocks), LayoutRegions = ContentJson.Clone(item.Regions),
                    LayoutAppearance = item.Appearance.Where(field => field.DefaultValue.HasValue).ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal) });
            }
            var original = toolkit.Single(item => item.ChoiceId == request.LandingTemplate.CompleteTemplateObjectId);
            var composed = HyperlibProjectPipelineService.Compose(request.LandingTemplate, toolkit, sourceSchema!, "audit");
            var sourceBlocks = original.Kind == "layout" ? original.LayoutBlocks
                : original.BlockTypeIds.Select(type => new PageBlock { Type = type, Region = "main" }).ToList();
            Require(composed.Blocks.Count == sourceBlocks.Count, "Complete layouts retain every original block: " + choice.Title);
            for (var index = 0; index < sourceBlocks.Count; index++)
            {
                var expected = sourceSchema!.Blocks.Single(block => block.Id == sourceBlocks[index].Type).Fields.Where(field => field.DefaultValue.HasValue)
                    .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);
                foreach (var (key, value) in sourceBlocks[index].Values) expected[key] = value.Clone();
                Require(composed.Blocks[index].Region == sourceBlocks[index].Region
                    && expected.All(pair => composed.Blocks[index].Values.TryGetValue(pair.Key, out var actual) && pair.Value.GetRawText() == actual.GetRawText()),
                    "Complete layouts retain original region, configured text, media, and tool defaults: " + choice.Title);
            }
            Require(composed.Appearance["studioCompleteTemplate"].GetBoolean()
                && !composed.Blocks.Any(block => block.Type == "studio-navigation"), "Complete originals do not receive duplicate wizard chrome/navigation.");
            if (request.LandingTemplate.CompleteTemplateAppearanceId is {} styleId)
                Require(toolkit.Single(item => item.ChoiceId == styleId).LayoutAppearance.All(pair => composed.Appearance[pair.Key].GetRawText() == pair.Value.GetRawText()),
                    "Complete original colors/fonts and custom appearance defaults survive composition: " + choice.Title);
            audited.Add(new { choice.Id, choice.Title, blocks = composed.Blocks.Count, regions = composed.Blocks.Select(block => block.Region).Distinct().ToArray() });
        }
        Check(audited.Count == choices.Count, "Every listed complete design retains all actual default values, regions, and appearance through composition.");
        var selected = choices.First(item => item.Id == "hyperlib:insight-dashboard");
        var sourceDefinition = generator.GetCatalogObject("insight-dashboard", "page-layout").Definition;
        var sourceHash = StudioGuidedUpgradeVerifier.HashTree(generator.GetCatalogProjectRoot("insight-dashboard"));
        var project = await generator.CreateAsync(Path.Combine(output, "complete-project-" + Guid.NewGuid().ToString("N")), "site", gallery.BuildDefinition(selected.Id, "Complete Dashboard"), ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var service = new HyperlibProjectPipelineService();
        var pipeline = (await service.ReadAsync(project, ct))!;
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        Check(state.Pages.Count == 1 && state.Pages[pipeline.LandingPageId].Blocks.Count == sourceDefinition.DefaultBlocks.Count,
            "Fresh complete creation saves only the landing Page and all original content blocks.");
        Check(pipeline.LandingTemplate.Appearance["variant"].GetString() == sourceDefinition.Appearance.Single(field => field.Id == "variant").DefaultValue!.Value.GetString()
            && pipeline.LandingTemplate.Appearance["background"].GetString() == sourceDefinition.Appearance.Single(field => field.Id == "background").DefaultValue!.Value.GetString(),
            "The imported Work retains original structural variant and exact background color.");
        var runtime = await File.ReadAllTextAsync(Path.Combine(project, "Templates/StudioRuntime/HyperLib.Runtime/Components/DynamicPage.razor"), ct);
        Check(runtime.Contains("IsCompleteTemplate || IsDirectEditing ? \"\" : \"studio-staged-layout studio-guided-layout\"", StringComparison.Ordinal)
            && runtime.Contains("@Options.VariantClass", StringComparison.Ordinal), "The actual portable reader preserves original variants and disables staged layout chrome for complete templates.");

        // Save a real complete template from this private Work, then create another independent project from it.
        var saved = await ReusableTemplateManager.CreateFromWorkAsync(library, pipeline.WorkId, "Saved complete dashboard", ct);
        var savedGallery = new StudioCompleteTemplateService(library);
        Check(savedGallery.List().Any(item => item.Id == "hyperlib:" + saved.Id && !item.IsBuiltIn), "A saved complete Hyperlib template is included in the same gallery.");
        var savedProject = await new HyperlibWorkProjectService(library).CreateAsync(Path.Combine(output, "saved-project-" + Guid.NewGuid().ToString("N")), "site", savedGallery.BuildDefinition("hyperlib:" + saved.Id, "Saved dashboard copy"), ct);
        var savedPipeline = (await service.ReadAsync(savedProject, ct))!;
        var savedProgram = await File.ReadAllTextAsync(Path.Combine(savedProject, "../../src/HyperLib.Host/Program.cs"), ct);
        Check(Occurrences(savedProgram, "app.MapPut(\"/api/studio/page-templates\"") == 1
            && Occurrences(savedProgram, "var stagedPipeline = await studioPipeline.ReadAsync(StudioWorkRoot(), ct);") == 1
            && File.Exists(Path.Combine(savedProject, "../../src/HyperLib.Host/wwwroot/studio-authoring/app.css")),
            "Creating from a portable source library refreshes managed staging assets and installs exactly one generated gateway hook.");
        Check(savedPipeline.PageTemplate.Blocks.Count == pipeline.PageTemplate.Blocks.Count
            && savedPipeline.PageTemplate.Appearance["background"].GetRawText() == pipeline.PageTemplate.Appearance["background"].GetRawText(),
            "Saved complete templates copy full portable component source, content defaults, and appearance into a new Work.");
        var savedRoot = Path.GetFullPath(Path.Combine(savedProject, "../.."));
        using (var savedHosts = new HyperlibWorkPreviewHostService())
        using (var savedClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            var savedEndpoint = await savedHosts.EnsureAsync(savedRoot, ct);
            using var savedInfo = JsonDocument.Parse(await savedClient.GetStringAsync(new Uri(savedEndpoint, "api/runtime/" + savedPipeline.WorkId), ct));
            var savedUrl = new Uri(savedEndpoint, savedInfo.RootElement.GetProperty("proxyPrefix").GetString()!.TrimStart('/') + "page/" + savedPipeline.LandingPageId);
            var savedHtml = await savedClient.GetStringAsync(savedUrl, ct);
            Check(savedHtml.Contains("hl-counter", StringComparison.Ordinal) && savedHtml.Contains("hl-variant-insight-dashboard", StringComparison.Ordinal),
                "The actual saved-copy Work compiles and renders its original real components without duplicate generated support types.");
            var script = Path.Combine(AppContext.BaseDirectory, "verify-saved-complete-template.cjs");
            if (!File.Exists(script)) script = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tools/verify-saved-complete-template.cjs"));
            var run = await new BoundedProcessRunner().RunAsync(new() { ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
                Arguments = [script, savedUrl.AbsoluteUri, output], WorkingDirectory = output, Timeout = TimeSpan.FromSeconds(90), MaximumStandardOutputCharacters = 16000, MaximumStandardErrorCharacters = 16000 }, ct);
            Check(run.ExitCode == 0, "The saved complete template retains working independently imported JavaScript tool modules. " + run.StandardOutput + run.StandardError);
        }

        var savedLibrary = Path.GetFullPath(Path.Combine(savedProject, "../.."));
        var savedStore = new ContentStore(savedLibrary);
        var savedState = await savedStore.ReadAsync(ct);
        var savedSchema = TemplateObjectLibrary.ReadDefinition(savedProject);
        savedSchema.Regions.Add(new() { Id = "authored-custom", Title = "Independent authored region" });
        await File.WriteAllTextAsync(Path.Combine(savedProject, "authoring.template.json"), JsonSerializer.Serialize(savedSchema, ContentJson.Options), ct);
        var savedLanding = ContentJson.Clone(savedState.Pages[savedPipeline.LandingPageId]);
        var independent = ContentJson.Clone(savedLanding.Blocks.First(block => savedSchema.Blocks.Single(item => item.Id == block.Type).Fields.Any(field => field.Type == "text")));
        independent.Id = "independent-custom-region"; independent.Region = "authored-custom";
        savedLanding.Blocks.Add(independent);
        savedState = await savedStore.ApplyAsync(new() { ExpectedRevision = savedState.Revision, Description = "Author independent custom region", Operations = [new() { Kind = "savePage", Page = savedLanding }] }, ct);
        var scratch = (await service.ReadDefinitionAsync(savedProject, ct))!;
        scratch.LandingTemplate.CompleteTemplateObjectId = scratch.LandingTemplate.CompleteTemplateAppearanceId = null;
        scratch.PageTemplate.CompleteTemplateObjectId = scratch.PageTemplate.CompleteTemplateAppearanceId = null;
        await new HyperlibWorkProjectService(library).UpdateFoundationAsync(savedProject, scratch, savedPipeline.Revision, savedState.Revision, ct);
        savedState = await savedStore.ReadAsync(ct);
        Check(savedState.Pages[savedLanding.Id].Blocks.Any(block => block.Id == independent.Id && block.Region == independent.Region)
            && TemplateObjectLibrary.ReadDefinition(savedProject).Regions.Any(region => region.Id == independent.Region),
            "Explicit recipe changes preserve independent authored objects and their custom region schema.");

        using var hosts = new HyperlibWorkPreviewHostService();
        var endpoint = await hosts.EnsureAsync(library, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        state = await Post(client, endpoint, new() { ExpectedRevision = state.Revision, Description = "Clone complete Page foundations", Operations = [
            new() { Kind = "createNode", NodeType = "page", Id = "complete-first", ParentId = pipeline.WorkId, Title = "First complete Page" },
            new() { Kind = "createNode", NodeType = "page", Id = "complete-second", ParentId = pipeline.WorkId, Title = "Second complete Page" }
        ] }, ct);
        Check(new[] { "complete-first", "complete-second" }.All(id => state.Pages[id].Blocks.Count == sourceDefinition.DefaultBlocks.Count
            && state.Nodes.Single(node => node.Id == id).Appearance["studioCompleteTemplate"].GetBoolean()), "Actual gateway New Page independently clones the complete named foundation.");
        var page = ContentJson.Clone(state.Pages["complete-first"]);
        var schema = TemplateObjectLibrary.ReadDefinition(project);
        var editedBlock = page.Blocks.First(block => schema.Blocks.Single(item => item.Id == block.Type).Fields.Any(field => field.Type == "text"));
        var textField = schema.Blocks.Single(item => item.Id == editedBlock.Type).Fields.First(field => field.Type == "text").Id;
        editedBlock.Values[textField] = ContentJson.Value("Independent complete Page article");
        page.Sources.Add(new() { Id = "complete-source", Title = "Complete Page source", Url = "https://example.com/complete", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [editedBlock.Id] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author complete Page content", Operations = [new() { Kind = "savePage", Page = page }] }, ct);
        var secondOriginal = state.Pages["complete-second"].Blocks.Single(block => block.Id == editedBlock.Id).Values[textField].GetString();
        var layout = pipeline.Toolkit.Single(item => item.ChoiceId == pipeline.PageTemplate.Definition.CompleteTemplateObjectId);
        layout.LayoutBlocks.Single(block => block.Type == editedBlock.Type && block.Values.ContainsKey(textField)).Values[textField] = ContentJson.Value("Revised shared complete default");
        await HyperlibProjectPipelineService.SaveAsync(project, pipeline, ct);
        var definition = (await service.ReadDefinitionAsync(project, ct))!;
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        Check(state.Pages["complete-first"].Blocks.Single(block => block.Id == editedBlock.Id).Values[textField].GetString() == "Independent complete Page article"
            && state.Pages["complete-first"].Sources.Single().BlockIds.Contains(editedBlock.Id)
            && secondOriginal != "Revised shared complete default"
            && state.Pages["complete-second"].Blocks.Single(block => block.Id == editedBlock.Id).Values[textField].GetString() == "Revised shared complete default",
            "A real complete-default update preserves authored fields and citations while untouched Pages follow the revised shared default.");
        endpoint = await hosts.EnsureAsync(library, ct);
        using var info = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint, "api/runtime/" + pipeline.WorkId), ct));
        var prefix = info.RootElement.GetProperty("proxyPrefix").GetString()!;
        var url = new Uri(endpoint, prefix.TrimStart('/') + "page/" + pipeline.LandingPageId);
        var html = await client.GetStringAsync(url, ct);
        Check(html.Contains("hl-variant-insight-dashboard", StringComparison.Ordinal) && html.Contains("hl-counter", StringComparison.Ordinal)
            && !html.Contains("hl-document hl-variant-insight-dashboard studio-staged-layout", StringComparison.Ordinal), "The actual generated Host renders the original dashboard variant and real Counter component.");
        var after = StudioGuidedUpgradeVerifier.HashTree(generator.GetCatalogProjectRoot("insight-dashboard"));
        Check(sourceHash.Count == after.Count && sourceHash.All(pair => after.GetValueOrDefault(pair.Key) == pair.Value), "Gallery reads, imports, saved copies, and edits leave original collection source unchanged.");
        await File.WriteAllTextAsync(Path.Combine(output, "complete-gallery-audit.json"), JsonSerializer.Serialize(audited, ContentJson.Options), ct);
        var result = "STUDIO_COMPLETE_TEMPLATE_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-complete-template-result.json"), JsonSerializer.Serialize(new { result, checks, project, savedProject, actualReaderUrl = url.AbsoluteUri }, ContentJson.Options), ct);
        return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var original = Path.GetFullPath(Path.Combine(oldWorkRoot, "../.."));
        var originalHashes = StudioGuidedUpgradeVerifier.HashTree(original);
        var library = Path.Combine(output, "footer-release-copy-" + Guid.NewGuid().ToString("N"));
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(original, library, ct);
        var project = Path.Combine(library, "Works", Path.GetFileName(oldWorkRoot));
        var service = new HyperlibProjectPipelineService();
        var pipeline = (await service.ReadAsync(project, ct))!;
        var definition = (await service.ReadDefinitionAsync(project, ct))!;
        var store = new ContentStore(library);
        var before = await store.ReadAsync(ct);
        var pages = JsonSerializer.Serialize(before.Pages, ContentJson.Options);
        await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project, definition, pipeline.Revision, before.Revision, ct);
        var after = await store.ReadAsync(ct);
        Require(JsonSerializer.Serialize(after.Pages, ContentJson.Options) == pages, "An explicit old-Footer-release upgrade preserves all existing authored Pages, slots, media, and citations.");
        Require(Directory.Exists(Path.Combine(library, ".studio/upgrades")), "The explicit upgrade backs up previous generated support outside published Work source.");
        Require((await File.ReadAllTextAsync(Path.Combine(library, "src/HyperLib.Host/StudioPipeline/HyperlibProjectPipelineService.cs"), ct)).Contains("ComposeCompleteTemplate", StringComparison.Ordinal), "The old release receives its own complete-template-capable pipeline source.");
        var hashes = StudioGuidedUpgradeVerifier.HashTree(original);
        Require(originalHashes.Count == hashes.Count && originalHashes.All(pair => hashes.GetValueOrDefault(pair.Key) == pair.Value), "Upgrade QA does not change the original last-release fixture.");
        var result = "STUDIO_COMPLETE_TEMPLATE_UPGRADE_OK|authored-footer-body-slots-preserved|old-support-backed-up|owned-new-pipeline|original-fixture-unchanged";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-complete-template-upgrade-result.txt"), result + "\n" + project, ct);
        return result;
    }

    private static async Task<LibraryState> Post(HttpClient client, Uri endpoint, OperationBatch batch, CancellationToken ct)
    {
        using var response = await client.PostAsync(new Uri(endpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(batch, ContentJson.Options), Encoding.UTF8, "application/json"), ct);
        var raw = await response.Content.ReadAsStringAsync(ct); Require(response.IsSuccessStatusCode, raw);
        return JsonSerializer.Deserialize<LibraryState>(raw, ContentJson.Options)!;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static int Occurrences(string text, string marker) => (text.Length - text.Replace(marker, "", StringComparison.Ordinal).Length) / marker.Length;
}
