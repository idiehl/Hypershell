using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioBodyVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string message) { Require(condition, message); checks.Add(message); }
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalog = generator.GetCatalog();
        var textChoice = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "TextBlock" && item.Kind == "element");
        var counterChoice = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "CounterBlock" && item.Kind == "element");
        var imageChoice = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "ImageBlock" && item.Kind == "element");
        var shapeHashes = HyperlibBodyLayouts.All.Select(preset => {
            var layout = HyperlibBodyLayouts.Create(preset.Id); HyperlibHeaderLayouts.Validate(layout);
            var node = JsonSerializer.SerializeToNode(layout, ContentJson.Options)!; RemoveIds(node);
            return node.ToJsonString(ContentJson.Options);
        }).ToArray();
        Check(shapeHashes.Length == 6 && shapeHashes.Distinct(StringComparer.Ordinal).Count() == 6,
            "Six Body foundations contain distinct editable Block and Box structures.");

        var mainRecipe = new HyperlibPageTemplateDefinition {
            HeaderLayout = HyperlibHeaderLayouts.Create("simple-title"), NavigationLayout = HyperlibNavigationLayouts.Create("simple-links"),
            BodyTemplateId = textChoice.Id, BodyStyle = new() { Text = "Legacy Body chrome", BackgroundColor = "#192f34" }
        };
        var alternate = new HyperlibNamedPageTemplateDefinition { Id = "body-alternate", Name = "Alternate", Definition = ContentJson.Clone(mainRecipe) };
        var project = await generator.CreateAsync(Path.Combine(output, "body-project-" + Guid.NewGuid().ToString("N")), "site", new() {
            PipelineVersion = 2, WorkTitle = "Body builder verification", SelectedToolsetIds = [textChoice.Id],
            LandingTemplate = ContentJson.Clone(mainRecipe), PageTemplate = mainRecipe,
            PageTemplates = [new() { Id = "page-default", Name = "Body Page", Definition = mainRecipe }, alternate]
        }, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var service = new HyperlibProjectPipelineService();
        var pipeline = (await service.ReadAsync(project, ct))!;
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        var legacyPage = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        var legacyBlock = legacyPage.Blocks.Single(block => block.Type == pipeline.Toolkit.Single(item => item.ChoiceId == textChoice.Id).BlockTypeIds[0]);
        legacyBlock.Values["text"] = ContentJson.Value("Original authored Body content survives enabling the builder.");
        legacyPage.Sources.Add(new() { Id = "body-source", Title = "Authored Body source", Url = "https://example.com/body", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [legacyBlock.Id] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author the existing Body before enabling its builder", Operations = [new() { Kind = "savePage", Page = legacyPage }] }, ct);
        var priorChrome = JsonSerializer.Serialize(pipeline.LandingTemplate.Blocks.Where(block => block.Region != "main"), ContentJson.Options);

        var imagePath = Path.Combine(output, "body-picture.svg");
        await File.WriteAllTextAsync(imagePath, "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='80'><rect width='120' height='80' fill='#65a88e'/></svg>", ct);
        var body = new HyperlibHeaderLayoutDefinition { Enabled = true, Blocks = [
            new() { Id = "body-feature-row", Arrangement = "horizontal", Height = 270, Padding = 12, Elements = [
                new() { Id = "body-box", Kind = "box", Arrangement = "positioned", WidthPercent = 45, Height = 236, Padding = 12, BorderWidth = 1, Children = [
                    new() { Id = "body-native-image", Kind = "image", WidthMode = "pixels", WidthPixels = 128, Height = 96, MediaPath = imagePath, Alt = "Owned Body illustration" },
                    new() { Id = "body-text", Text = "Shared Body field", X = 0, Y = 58, FontSize = 18, WidthPercent = 100 }
                ] },
                new() { Id = "body-counter-one", Kind = "template", TemplateObjectId = counterChoice.Id, WidthPercent = 27.5,
                    TemplateValues = new() { ["element"] = new() { ["start"] = ContentJson.Value(5) } } },
                new() { Id = "body-counter-two", Kind = "template", TemplateObjectId = counterChoice.Id, WidthPercent = 27.5,
                    TemplateValues = new() { ["element"] = new() { ["start"] = ContentJson.Value(9) } } }
            ] },
            new() { Id = "body-picture-row", Arrangement = "horizontal", Height = 160, Elements = [
                new() { Id = "body-library-image", Kind = "template", TemplateObjectId = imageChoice.Id, WidthMode = "pixels", WidthPixels = 180, Height = 150,
                    TemplateValues = new() { ["element"] = new() { ["src"] = ContentJson.Value(imagePath), ["alt"] = ContentJson.Value("Library Body illustration") } } }
            ] }
        ] };
        var definition = (await service.ReadDefinitionAsync(project, ct))!;
        definition.LandingTemplate.BodyLayout = ContentJson.Clone(body);
        definition.PageTemplate.BodyLayout = ContentJson.Clone(body);
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        Check(pipeline.LandingTemplate.Blocks.Count(block => block.Type == HyperlibHeaderLayouts.BodyBlockType) == 2
            && pipeline.LandingTemplate.Blocks.Where(block => block.Type == HyperlibHeaderLayouts.BodyBlockType).All(block => block.Region == "main" && block.Values["studioRegion"].GetString() == "body"),
            "Body Blocks use their own stable type and main-region context while sharing the actual row renderer.");
        Check(state.Pages[pipeline.LandingPageId].Blocks.Any(block => block.Id == legacyBlock.Id && block.Values["text"].GetString()!.StartsWith("Original authored Body", StringComparison.Ordinal))
            && state.Pages[pipeline.LandingPageId].Sources.Single().BlockIds.Contains(legacyBlock.Id),
            "Enabling the Body builder retains authored legacy objects and source citations independently.");
        Check(JsonSerializer.Serialize(pipeline.LandingTemplate.Blocks.Where(block => block.Region != "main"), ContentJson.Options) == priorChrome,
            "Body changes leave the existing Header and Navigation foundation unchanged.");
        Check(pipeline.LandingTemplate.Appearance["studioBodyBuilder"].GetBoolean() && pipeline.LandingTemplate.Appearance["studioBodyText"].GetString() == ""
            && pipeline.LandingTemplate.Appearance["studioBodyCss"].GetString() == "" && pipeline.LandingTemplate.Definition.BodyTemplateId == textChoice.Id,
            "An enabled Body builder suppresses legacy main choices and duplicate chrome while retaining its recoverable recipe IDs.");
        Check(pipeline.Toolkit.Any(item => item.ChoiceId == counterChoice.Id) && pipeline.Toolkit.Any(item => item.ChoiceId == imageChoice.Id),
            "Nested Body library components automatically import their actual portable toolkit dependencies.");
        var stagedElements = HyperlibHeaderLayouts.Elements(pipeline.LandingTemplate.Definition.BodyLayout).ToArray();
        var nativeImage = stagedElements.Single(element => element.Id == "body-native-image").MediaPath;
        var importedImage = stagedElements.Single(element => element.Id == "body-library-image").TemplateValues["element"]["src"].GetString()!;
        Check(new[] { nativeImage, importedImage }.All(path => path.StartsWith("/media/", StringComparison.Ordinal) && File.Exists(Path.Combine(library, "Media", path[7..]))),
            "Native and library-field Body pictures are independently owned project media.");
        var savedLanding = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        foreach (var preset in HyperlibBodyLayouts.All) {
            var composed = HyperlibProjectPipelineService.Compose(new() { IncludeNavigation = false, BodyLayout = HyperlibBodyLayouts.Create(preset.Id) }, pipeline.Toolkit, TemplateObjectLibrary.ReadDefinition(project), "body-preset");
            var page = ContentJson.Clone(savedLanding); page.Revision = state.Pages[page.Id].Revision; page.Blocks = composed.Blocks; page.Sources.Clear();
            state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Persist actual Body foundation " + preset.Title, Operations = [new() { Kind = "savePage", Page = page }] }, ct);
            Check((await store.ReadAsync(ct)).Pages[page.Id].Blocks.Count(block => block.Type == HyperlibHeaderLayouts.BodyBlockType) == composed.Blocks.Count,
                preset.Title + " passes actual content validation, journaling, and readback.");
        }
        savedLanding.Revision = state.Pages[savedLanding.Id].Revision;
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Restore Body fixture", Operations = [new() { Kind = "savePage", Page = savedLanding }] }, ct);

        using var hosts = new HyperlibWorkPreviewHostService();
        var endpoint = await hosts.EnsureAsync(library, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        state = await Post(client, endpoint, new() { ExpectedRevision = state.Revision, Description = "Create Pages assigned to Body foundations", Operations = [
            new() { Kind = "createNode", NodeType = "page", Id = "body-content-page", ParentId = pipeline.WorkId, Title = "Body content Page" },
            new() { Kind = "createNode", NodeType = "page", Id = "body-other-page", ParentId = pipeline.WorkId, Title = "Other foundation", Appearance = new() { ["studioPageTemplateId"] = ContentJson.Value(alternate.Id) } }
        ] }, ct);
        Check(state.Pages["body-content-page"].Blocks.Count(block => block.Type == HyperlibHeaderLayouts.BodyBlockType) == 2
            && !state.Pages["body-content-page"].Blocks.Any(block => block.Id == legacyBlock.Id),
            "A newly created Page uses the selected reusable Body without cloning another Page's authored legacy content.");
        var alternateBefore = JsonSerializer.Serialize(state.Pages["body-other-page"], ContentJson.Options);
        var authored = ContentJson.Clone(state.Pages["body-content-page"]);
        var rowBlock = authored.Blocks.First(block => block.Type == HyperlibHeaderLayouts.BodyBlockType);
        var row = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(rowBlock.Values["layout"].GetString()!, ContentJson.Options)!;
        var authoredText = row.Elements[0].Children.Single(element => element.Id == "body-text"); authoredText.Text = "Independently authored Body text";
        rowBlock.Values["layout"] = ContentJson.Value(JsonSerializer.Serialize(row, ContentJson.Options));
        authored.Sources.Add(new() { Id = "body-page-source", Title = "Body Page source", Url = "https://example.com/body-page", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [rowBlock.Id] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author one nested Body field", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        definition = (await service.ReadDefinitionAsync(project, ct))!;
        var sharedText = HyperlibHeaderLayouts.Elements(definition.PageTemplate.BodyLayout).Single(element => element.Id == "body-text");
        sharedText.Text = "New shared Body default"; sharedText.FontSize = 21;
        definition.PageTemplate.BodyLayout.Blocks.Add(new() { Id = "body-added-row", Height = 80, Arrangement = "horizontal", Elements = [new() { Id = "body-added-text", Text = "New reusable Body section", FontSize = 18 }] });
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        var mergedRow = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(state.Pages[authored.Id].Blocks.Single(block => block.Id == rowBlock.Id).Values["layout"].GetString()!, ContentJson.Options)!;
        var mergedText = mergedRow.Elements[0].Children.Single(element => element.Id == "body-text");
        Check(mergedText.Text == "Independently authored Body text" && mergedText.FontSize == 21
            && state.Pages[authored.Id].Blocks.Count(block => block.Type == HyperlibHeaderLayouts.BodyBlockType) == 3,
            "Shared Body updates preserve authored nested text while untouched styles and newly added Blocks propagate.");
        Check(JsonSerializer.Serialize(state.Pages["body-other-page"], ContentJson.Options) == alternateBefore
            && state.Pages[authored.Id].Sources.Single().BlockIds.Contains(rowBlock.Id),
            "Body updates isolate another named Page template and retain authored citations.");
        endpoint = await hosts.EnsureAsync(library, ct);
        using (var runtime = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint, "api/runtime/" + pipeline.WorkId), ct))) {
            var url = new Uri(endpoint, runtime.RootElement.GetProperty("proxyPrefix").GetString()!.TrimStart('/') + "page/" + pipeline.LandingPageId);
            var html = await client.GetStringAsync(url, ct);
            Check(html.Contains("Original authored Body", StringComparison.Ordinal) && html.Contains("data-studio-region=\"body\"", StringComparison.Ordinal)
                && html.Contains("body-counter-one", StringComparison.Ordinal) && !html.Contains("Legacy Body chrome", StringComparison.Ordinal),
                "The actual generated Host renders Body components and preserved content without duplicate legacy chrome.");
            var config = Path.Combine(output, "body-browser.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { url = url.AbsoluteUri, project }, ContentJson.Options), ct);
            var script = Path.Combine(AppContext.BaseDirectory, "verify-studio-body.cjs");
            if (!File.Exists(script)) script = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tools/verify-studio-body.cjs"));
            Require(File.Exists(script), "Copy the actual Body browser verifier beside the verifier executable.");
            var run = await new BoundedProcessRunner().RunAsync(new() { ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe", Arguments = [script, config, output], WorkingDirectory = output,
                Timeout = TimeSpan.FromSeconds(90), MaximumStandardOutputCharacters = 20000, MaximumStandardErrorCharacters = 20000 }, ct);
            Check(run.ExitCode == 0, "Actual Body component interaction and nested geometry passed browser checks. " + run.StandardOutput + run.StandardError);
        }
        definition = (await service.ReadDefinitionAsync(project, ct))!;
        definition.PageTemplate.BodyLayout.Blocks.RemoveAll(item => item.Id == "body-feature-row");
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        Check(state.Pages[authored.Id].Blocks.Any(block => block.Id == rowBlock.Id && block.Region == "main")
            && state.Pages[authored.Id].Sources.Single().BlockIds.Contains(rowBlock.Id),
            "Removing a populated Body Block preserves its independently authored content and citations as a Page object.");
        Check(pipeline.PageTemplate.Appearance.All(item => item.Value.GetRawText().Length <= 4096), "Body layout data stays in content Blocks without expanding Core appearance limits.");
        var result = "STUDIO_BODY_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-body-result.json"), JsonSerializer.Serialize(new { result, checks, project }, ContentJson.Options), ct);
        return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var original = Path.GetFullPath(Path.Combine(oldWorkRoot, "../..")); var hashes = StudioGuidedUpgradeVerifier.HashTree(original);
        var library = Path.Combine(Path.GetFullPath(output), "old-navigation-copy-" + Guid.NewGuid().ToString("N"));
        Require(library.Contains("qa", StringComparison.OrdinalIgnoreCase), "Use an isolated QA upgrade directory.");
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(original, library, ct);
        var project = Path.Combine(library, Path.GetRelativePath(original, oldWorkRoot));
        var service = new HyperlibProjectPipelineService(); var pipeline = (await service.ReadAsync(project, ct))!;
        var previousPipeline=ContentJson.Clone(pipeline);var previousSchema=TemplateObjectLibrary.ReadDefinition(project);
        var store = new ContentStore(library); var before = await store.ReadAsync(ct);
        var definition = (await service.ReadDefinitionAsync(project, ct))!;
        Require(!definition.LandingTemplate.BodyLayout.Enabled && !definition.PageTemplate.BodyLayout.Enabled, "Use an actually shipped project predating the Body builder.");
        definition.LandingTemplate.BodyLayout = HyperlibBodyLayouts.Create("two-columns");
        definition.PageTemplate.BodyLayout = HyperlibBodyLayouts.Create("article-and-aside");
        pipeline = await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project, definition, pipeline.Revision, before.Revision, ct);
        var after = await store.ReadAsync(ct);
        Require(before.Pages.All(pair => after.Pages.ContainsKey(pair.Key)
            && pair.Value.Blocks.Where(block => MustPreserve(pair.Key,block))
                .All(block => after.Pages[pair.Key].Blocks.Any(current => current.Id == block.Id))
            && JsonSerializer.Serialize(pair.Value.Sources, ContentJson.Options) == JsonSerializer.Serialize(after.Pages[pair.Key].Sources, ContentJson.Options)),
            "The old project's authored objects, Header, Navigation, and citations survive enabling the Body builder.");
        bool MustPreserve(string pageId,PageBlock block)
        {
            if(block.Region!="main")return true;
            var node=before.Nodes.Single(item=>item.Id==pageId);
            var assigned=node.Appearance.TryGetValue("studioPageTemplateId",out var choice)&&choice.ValueKind==JsonValueKind.String?choice.GetString():null;
            var previous=pageId==previousPipeline.LandingPageId?previousPipeline.LandingTemplate:HyperlibProjectPipelineService.GetPageTemplate(previousPipeline,assigned);
            var prior=previous.Blocks.SingleOrDefault(item=>item.Id==block.Id&&item.Type==block.Type);
            if(prior is null||block.Values.Any(value=>!prior.Values.TryGetValue(value.Key,out var old)||old.GetRawText()!=value.Value.GetRawText()))return true;
            var schema=previousSchema.Blocks.Single(item=>item.Id==block.Type);
            return schema.Fields.Any(field=>field.Type is "string" or "text" or "url" or "media"&&block.Values.TryGetValue(field.Id,out var value)
                &&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString()));
        }
        Require(pipeline.LandingTemplate.Blocks.Count(block => block.Type == HyperlibHeaderLayouts.BodyBlockType) == 1
            && TemplateObjectLibrary.ReadDefinition(project).Blocks.Any(block => block.Id == HyperlibHeaderLayouts.BodyBlockType && block.Component == HyperlibHeaderLayouts.ComponentName),
            "The actual prior project acquires the Body schema and shared row renderer.");
        Require(Directory.Exists(Path.Combine(library, ".studio/upgrades")), "An explicit Body upgrade keeps prior generated support source in private backups.");
        using var hosts = new HyperlibWorkPreviewHostService(); var endpoint = await hosts.EnsureAsync(library, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var runtime = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint, "api/runtime/" + pipeline.WorkId), ct));
        var html = await client.GetStringAsync(new Uri(endpoint, runtime.RootElement.GetProperty("proxyPrefix").GetString()!.TrimStart('/') + "page/" + pipeline.LandingPageId), ct);
        Require(html.Contains("data-studio-region=\"body\"", StringComparison.Ordinal), "The upgraded real gateway compiles and renders Body Blocks.");
        var final = StudioGuidedUpgradeVerifier.HashTree(original);
        Require(final.Count == hashes.Count && hashes.All(pair => final.GetValueOrDefault(pair.Key) == pair.Value), "The previously shipped source fixture remains unchanged.");
        var result = "STUDIO_BODY_UPGRADE_OK|checks=5";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-body-upgrade-result.txt"), result + "\n" + project, ct);
        return result;
    }

    private static async Task<LibraryState> Post(HttpClient client, Uri endpoint, OperationBatch batch, CancellationToken ct)
    { using var response = await client.PostAsync(new Uri(endpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(batch, ContentJson.Options), Encoding.UTF8, "application/json"), ct); var raw = await response.Content.ReadAsStringAsync(ct); Require(response.IsSuccessStatusCode, raw); return JsonSerializer.Deserialize<LibraryState>(raw, ContentJson.Options)!; }
    private static void RemoveIds(JsonNode node)
    { if (node is JsonObject obj) { obj.Remove("id"); foreach (var pair in obj.ToArray()) if (pair.Value is not null) RemoveIds(pair.Value); } else if (node is JsonArray array) foreach (var item in array) if (item is not null) RemoveIds(item); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
