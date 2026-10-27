using System.IO;
using System.Net.Http;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioHeaderTemplateVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalog = generator.GetCatalog();
        var callout = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "CalloutBlock" && item.Kind == "element");
        var counter = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "CounterBlock" && item.Kind == "element");
        var image = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "ImageBlock" && item.Kind == "element");
        var layout = catalog.First(item => item.Kind == "layout" && item.GroupId == "blank"
            && generator.GetCatalogObject(item.GroupId, item.ObjectId).Blocks.Count >= 2);
        var presets = catalog.Where(item => item.Kind == "preset" && item.ComponentName == "CalloutBlock")
            .GroupBy(item => item.GroupId).First(group => group.Count() >= 2).Take(2).ToArray();
        var protectedSource = StudioGuidedUpgradeVerifier.HashTree(Path.Combine(sourceLibraryRoot, "CoreLibrary", "DesignGroups", presets[0].GroupId));
        HyperlibHeaderElementDefinition Instance(HyperlibWorkTemplateChoice choice, string title, string text, double x = 0)
            => new() { Kind = "template", TemplateObjectId = choice.Id, TemplateTitle = choice.Title, X = x, WidthPercent = 48,
                TemplateValues = new() { ["element"] = new() { ["title"] = ContentJson.Value(title), ["text"] = ContentJson.Value(text) } } };
        var first = Instance(callout, "First independent instance", "First shared body");
        var second = Instance(callout, "Second independent instance", "Second shared body", 51);
        var mediaInput = Path.Combine(output, "owned-template-picture.svg");
        await File.WriteAllTextAsync(mediaInput, "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='40'><rect width='64' height='40' fill='#65a88e'/></svg>", ct);
        var nested = new HyperlibHeaderElementDefinition { Kind = "box", Height = 180, FillColor = "#193533", TemplateAppearance = new() { ["accent"] = ContentJson.Value("#e59a41") },
            Children = [new() { Kind = "template", TemplateObjectId = counter.Id, TemplateValues = new() { ["element"] = new() { ["label"] = ContentJson.Value("Nested live counter"), ["start"] = ContentJson.Value(7) } } },
                new() { Kind = "template", TemplateObjectId = image.Id, X = 60, WidthPercent = 35, Height = 80,
                    TemplateValues = new() { ["element"] = new() { ["src"] = ContentJson.Value(mediaInput), ["alt"] = ContentJson.Value("Owned nested picture") } } }] };
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = false, HeaderLayout = new() { Enabled = true, Blocks = [
            new() { Height = 180, Elements = [first, second] }, new() { Height = 200, Elements = [nested] },
            new() { Height = 280, Elements = [new() { Kind = "template", TemplateObjectId = layout.Id }] },
            new() { Height = 140, Elements = presets.Select((choice, index) => new HyperlibHeaderElementDefinition { Kind = "template", TemplateObjectId = choice.Id, X = index * 50, WidthPercent = 48 }).ToList() }] } };
        var request = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Header library components", Description = "", SelectedToolsetIds = [],
            LandingTemplate = ContentJson.Clone(recipe), PageTemplate = recipe, PageTemplates = [new() { Id = "page-default", Name = "Library header", Definition = recipe },
                new() { Id = "page-independent", Name = "Other layout", Definition = new() { IncludeNavigation = false, HeaderLayout = HyperlibHeaderLayouts.Create("simple-title") } }] };
        var project = await generator.CreateAsync(Path.Combine(output, "header-template-project-" + Guid.NewGuid().ToString("N")), "site", request, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var service = new HyperlibProjectPipelineService(); var pipeline = (await service.ReadAsync(project, ct))!;
        var template = TemplateObjectLibrary.ReadDefinition(project);
        Require(new[] { callout.Id, counter.Id, image.Id, layout.Id }.All(id => pipeline.SelectedToolsetIds.Contains(id)), "Nested header choices must automatically import into the owned toolkit.");
        var all = HyperlibHeaderLayouts.Elements(pipeline.PageTemplate.Definition.HeaderLayout).ToArray();
        Require(all.Where(item => item.Kind == "template").All(item => item.TemplateBlocks.Count > 0 && item.TemplateBlocks.All(block => template.Blocks.Any(schema => schema.Id == block.Type))),
            "Every visual library instance must hydrate actual owned block types, never an invisible placeholder.");
        var one = all.Single(item => item.Id == first.Id); var two = all.Single(item => item.Id == second.Id);
        Require(one.TemplateBlocks[0].Values["title"].GetString() == "First independent instance" && two.TemplateBlocks[0].Values["title"].GetString() == "Second independent instance"
            && one.TemplateBlocks[0].Id != two.TemplateBlocks[0].Id, "Two instances of the same object must keep independent field values and component IDs.");
        Require(all.Single(item => item.TemplateObjectId == layout.Id).TemplateBlocks.Count >= 2
            && pipeline.Toolkit.Single(item => item.ChoiceId == layout.Id).SourceBlockDefinitions.Keys.SequenceEqual(pipeline.Toolkit.Single(item => item.ChoiceId == layout.Id).LayoutBlocks.Select(block => block.Id)),
            "A whole layout must keep its multiple real components and stable original source keys for offline field editing.");
        foreach (var preset in presets)
        {
            var source = generator.GetCatalogObject(preset.GroupId, preset.ObjectId); var owned = pipeline.Toolkit.Single(item => item.ChoiceId == preset.Id);
            foreach (var (key, value) in source.Blocks[0].Values.Where(pair => pair.Value.ValueKind != JsonValueKind.String || !pair.Value.GetString()!.Contains("_content/", StringComparison.Ordinal)))
                Require(owned.SourceBlockDefinitions["element"].Fields.Single(field => field.Id == key).DefaultValue?.GetRawText() == value.GetRawText(),
                    "Configured preset mappings must preserve exact defaults even when another preset uses the same Razor component.");
        }
        var importedMedia = all.Single(item => item.TemplateObjectId == image.Id).TemplateBlocks[0].Values["src"].GetString()!;
        Require(importedMedia.StartsWith("/media/studio-header/", StringComparison.Ordinal) && File.Exists(Path.Combine(library, "Media", importedMedia[7..]))
            && !File.ReadAllText(Path.Combine(project, ".studio/pipeline.json")).Contains(mediaInput, StringComparison.Ordinal), "Nested Template media must be imported and portable without machine paths.");
        Require(HyperlibHeaderLayouts.TemplateStyle(nested.TemplateAppearance).Contains("--hl-accent:#e59a41", StringComparison.Ordinal), "A Box must support scoped palette variables inherited by its children.");
        var store = new ContentStore(library); var state = await store.ReadAsync(ct);
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Create assigned header-template QA Pages", Operations = [
            new() { Kind = "createNode", Id = "header-template-authored", NodeType = "page", ParentId = pipeline.WorkId, Title = "Authored", Page = new() { TemplateId = template.TemplateId, Blocks = ContentJson.Clone(pipeline.PageTemplate.Blocks) }, Appearance = ContentJson.Clone(pipeline.PageTemplate.Appearance) },
            new() { Kind = "createNode", Id = "header-template-other", NodeType = "page", ParentId = pipeline.WorkId, Title = "Other", Page = new() { TemplateId = template.TemplateId, Blocks = ContentJson.Clone(pipeline.PageTemplates[1].Blocks) }, Appearance = new() { ["studioPageTemplateId"] = ContentJson.Value("page-independent") } }] }, ct);
        var other = JsonSerializer.Serialize(state.Pages["header-template-other"], ContentJson.Options);
        var authored = ContentJson.Clone(state.Pages["header-template-authored"]); var rowBlock = authored.Blocks.First(block => block.Type == HyperlibHeaderLayouts.BlockType);
        var row = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(rowBlock.Values["layout"].GetString()!, ContentJson.Options)!;
        row.Elements.Single(item => item.Id == first.Id).TemplateBlocks[0].Values["title"] = ContentJson.Value("Independently authored title");
        rowBlock.Values["layout"] = ContentJson.Value(JsonSerializer.Serialize(row, ContentJson.Options));
        authored.Sources.Add(new() { Id = "template-source", Title = "Independent source", Url = "https://example.com", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [rowBlock.Id] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author one nested template field", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        var next = (await service.ReadDefinitionAsync(project, ct))!;
        var changed = HyperlibHeaderLayouts.Elements(next.PageTemplate.HeaderLayout).Single(item => item.Id == first.Id);
        changed.TemplateValues["element"]["title"] = ContentJson.Value("Revised shared title");
        changed.TemplateValues["element"]["text"] = ContentJson.Value("Revised shared body"); changed.WidthPercent = 46;
        pipeline = await generator.UpdateFoundationAsync(project, next, pipeline.Revision, state.Revision, ct); state = await store.ReadAsync(ct);
        var merged = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(state.Pages[authored.Id].Blocks.First(block => block.Id == rowBlock.Id).Values["layout"].GetString()!, ContentJson.Options)!.Elements.Single(item => item.Id == first.Id);
        Require(merged.TemplateBlocks[0].Values["title"].GetString() == "Independently authored title" && merged.TemplateBlocks[0].Values["text"].GetString() == "Revised shared body" && merged.WidthPercent == 46,
            "Three-way propagation must preserve an authored nested field while updating other defaults and geometry in that same component.");
        Require(JsonSerializer.Serialize(state.Pages["header-template-other"], ContentJson.Options) == other && state.Pages[authored.Id].Sources.Single().BlockIds.Contains(rowBlock.Id),
            "Changing a header must preserve citations and leave another named Page template untouched.");
        var stableIds = one.TemplateBlocks.Select(block => block.Id).ToArray();
        Require(HyperlibHeaderLayouts.Elements(pipeline.PageTemplate.Definition.HeaderLayout).Single(item => item.Id == first.Id).TemplateBlocks.Select(block => block.Id).SequenceEqual(stableIds), "Restaging must preserve per-instance component identity.");
        using var hosts = new HyperlibWorkPreviewHostService(); var endpoint = await hosts.EnsureAsync(library, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using (var runtime = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint, "api/runtime/" + pipeline.WorkId), ct)))
        {
            var prefix = runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
            var html = await client.GetStringAsync(new Uri(endpoint, prefix.TrimStart('/') + "page/" + pipeline.LandingPageId), ct);
            Require(html.Contains("First independent instance", StringComparison.Ordinal) && html.Contains("Second independent instance", StringComparison.Ordinal)
                && html.Contains("Nested live counter", StringComparison.Ordinal) && html.Contains("data-studio-shared-template", StringComparison.Ordinal), "The actual compiled Work must render real imported components nested inside header boxes.");
        }
        var invalid = ContentJson.Clone(pipeline.PageTemplate.Definition); HyperlibHeaderLayouts.Elements(invalid.HeaderLayout).Single(item => item.Id == first.Id).TemplateValues["element"]["unexpected"] = ContentJson.Value("rejected");
        try { HyperlibProjectPipelineService.Compose(invalid, pipeline.Toolkit, template, "invalid"); throw new Exception("Unknown field accepted."); } catch (ContentValidationException) { }
        invalid = ContentJson.Clone(pipeline.PageTemplate.Definition); HyperlibHeaderLayouts.Elements(invalid.HeaderLayout).Single(item => item.TemplateObjectId == counter.Id).TemplateValues["element"]["start"] = ContentJson.Value("not-a-number");
        try { HyperlibProjectPipelineService.Compose(invalid, pipeline.Toolkit, template, "invalid"); throw new Exception("Invalid typed field accepted."); } catch (ContentValidationException) { }
        Require(pipeline.PageTemplate.Appearance.All(pair => pair.Value.GetRawText().Length <= 4096) && pipeline.PageTemplate.Blocks.Where(block => block.Type == HyperlibHeaderLayouts.BlockType).All(block => block.Values["layout"].GetString()!.Length <= 200_000),
            "Header templates must use existing content storage limits without raising Core bounds.");
        var finalSource = StudioGuidedUpgradeVerifier.HashTree(Path.Combine(sourceLibraryRoot, "CoreLibrary", "DesignGroups", presets[0].GroupId));
        Require(protectedSource.Count == finalSource.Count && protectedSource.All(pair => finalSource.GetValueOrDefault(pair.Key) == pair.Value), "The original selected Core collection must remain unchanged.");
        var result = "STUDIO_HEADER_TEMPLATES_OK recursive-auto-import|real-owned-components|independent-values-ids|multiblock-layout|offline-source-keys|exact-configured-presets|portable-nested-media|scoped-box-appearance|actual-content-journal|per-field-preservation|other-default-propagation|geometry-propagation|named-template-isolation|citations|stable-component-identity|actual-compiled-reader|unknown-field-rejection|typed-field-rejection|unchanged-core-limits|source-library-unchanged";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-header-templates-result.txt"), result + "\n" + project, ct); return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output); var originalLibrary = Path.GetFullPath(Path.Combine(oldWorkRoot, "../.."));
        var protectedInputs = StudioGuidedUpgradeVerifier.HashTree(originalLibrary);
        var library = Path.Combine(Path.GetFullPath(output), "old-header-copy-" + Guid.NewGuid().ToString("N"));
        if (!library.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use isolated QA output for upgrade verification.");
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(originalLibrary, library, ct);
        var project = Path.Combine(library, Path.GetRelativePath(originalLibrary, oldWorkRoot));
        var service = new HyperlibProjectPipelineService(); var pipeline = (await service.ReadAsync(project, ct))!;
        var before = await new ContentStore(library).ReadAsync(ct); var originalComponent = File.ReadAllText(Path.Combine(project, "src/Pages/Blocks/StudioHeaderRowBlock.razor"));
        Require(originalComponent.Contains("RenderSerializedBlock", StringComparison.Ordinal), "The input must contain the actual prior shipped static header component.");
        using var hosts = new HyperlibWorkPreviewHostService(); using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var oldEndpoint = await hosts.EnsureAsync(library, ct);
        var definition = (await service.ReadDefinitionAsync(project, ct))!; var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var choice = generator.GetCatalog().First(item => item.GroupId == "blank" && item.ComponentName == "CalloutBlock" && item.Kind == "element");
        definition.PageTemplate.HeaderLayout.Enabled = true;
        definition.PageTemplate.HeaderLayout.Blocks.Add(new() { Height = 160, Elements = [new() { Kind = "template", TemplateObjectId = choice.Id,
            TemplateValues = new() { ["element"] = new() { ["title"] = ContentJson.Value("Upgraded real library component") } } }] });
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, before.Revision, ct);
        var after = await new ContentStore(library).ReadAsync(ct);
        foreach (var saved in before.Pages)
            Require(after.Pages.ContainsKey(saved.Key) && JsonSerializer.Serialize(saved.Value.Sources, ContentJson.Options) == JsonSerializer.Serialize(after.Pages[saved.Key].Sources, ContentJson.Options)
                && saved.Value.Blocks.All(block => after.Pages[saved.Key].Blocks.Any(current => current.Id == block.Id && current.Type == block.Type)), "An old header project upgrade must preserve every existing object identity and citation.");
        var newEndpoint = await hosts.EnsureAsync(library, ct); Require(newEndpoint != oldEndpoint, "Only the owned old gateway must restart after its actual source upgrade.");
        Require(File.ReadAllText(Path.Combine(project, "src/Pages/Blocks/StudioHeaderRowBlock.razor")).Contains("DynamicComponent", StringComparison.Ordinal)
            && Directory.GetFiles(Path.Combine(library, ".studio/upgrades"), "StudioHeaderRowBlock.razor", SearchOption.AllDirectories).Any(path => File.ReadAllText(path) == originalComponent), "The exact old header source must be backed up before installing the real component renderer.");
        using (var api = JsonDocument.Parse(await client.GetStringAsync(new Uri(newEndpoint, "api/studio/pipeline"), ct)))
            Require(api.RootElement.GetProperty("pageTemplate").GetProperty("definition").GetProperty("headerLayout").GetProperty("blocks").EnumerateArray()
                .Any(row => row.GetProperty("elements").EnumerateArray().Any(element => element.TryGetProperty("templateObjectId", out var id) && id.GetString() == choice.Id)), "The upgraded gateway must serialize the new nested Template element contract.");
        var finalInputs = StudioGuidedUpgradeVerifier.HashTree(originalLibrary);
        Require(protectedInputs.Count == finalInputs.Count && protectedInputs.All(pair => finalInputs.GetValueOrDefault(pair.Key) == pair.Value), "The original shipped header fixture must remain unchanged.");
        var result = "STUDIO_HEADER_TEMPLATE_UPGRADE_OK actual-prior-header-source|owned-source-upgrade|existing-identities-citations|old-component-backup|gateway-restart|new-template-api|original-fixture-unchanged";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-header-template-upgrade-result.txt"), result + "\n" + project, ct); return result;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
