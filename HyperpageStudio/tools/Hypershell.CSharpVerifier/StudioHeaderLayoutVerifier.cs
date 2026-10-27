using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioHeaderLayoutVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalog = generator.GetCatalog();
        var legacy = catalog.Where(item => new[] { item.Title, item.Category, item.ComponentName }.Any(text => text.Contains("header", StringComparison.OrdinalIgnoreCase) || text.Contains("banner", StringComparison.OrdinalIgnoreCase))).ToArray();
        var presets = HyperlibHeaderLayouts.All.Select(item => {
            var model = HyperlibHeaderLayouts.Create(item.Id);
            HyperlibHeaderLayouts.Validate(model);
            var node = JsonSerializer.SerializeToNode(model, ContentJson.Options)!;
            RemoveIds(node);
            return new { item.Id, item.Title, item.Description, Rows = model.Blocks.Count, Elements = HyperlibHeaderLayouts.Elements(model).Count(),
                ShapeHash = Hash(node.ToJsonString(ContentJson.Options)) };
        }).ToArray();
        Require(presets.Length == 10 && presets.Select(item => item.ShapeHash).Distinct().Count() == 10 && legacy.Length > 0,
            "Audit the retained Core headers and provide ten structurally distinct editable header foundations.");
        await File.WriteAllTextAsync(Path.Combine(output, "header-library-audit.json"), JsonSerializer.Serialize(new { existingHeaderChoices = legacy,
            existingHeaderCount = legacy.Length, newEditableHeaders = presets }, ContentJson.Options), ct);
        var first = HyperlibHeaderLayouts.Create("boxed-identity");
        var box = first.Blocks[0].Elements[0];
        var textId = box.Children[0].Id;
        box.Children[0].Text = "Shared header identity";
        box.Children.Add(new() { Kind = "box", X = 62, Y = 8, WidthPercent = 32, Height = 90,
            Children = [new() { Text = "Nested editable label", WidthPercent = 100, FontSize = 15 }] });
        var nestedId = box.Children[^1].Children[0].Id;
        var image = Path.Combine(output, "header-image.svg");
        var imageBytes = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><rect width='80' height='60' fill='#65a88e'/></svg>");
        await File.WriteAllBytesAsync(image, imageBytes, ct);
        var video = Path.Combine(output, "header-video.webm");
        File.Copy(Path.Combine(sourceLibraryRoot, "Media", "sample-motion.webm"), video, true);
        var imageElement = new HyperlibHeaderElementDefinition { Kind = "image", X = 4, Y = 8, WidthPercent = 13, Height = 64, MediaPath = image, Alt = "Owned header illustration" };
        var videoElement = new HyperlibHeaderElementDefinition { Kind = "video", X = 70, Y = 8, WidthPercent = 26, Height = 100, MediaPath = video, Alt = "Owned header video" };
        first.Blocks.Add(new() { Height = 125, Elements = [imageElement, videoElement] });
        var body = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "RichTextBlock" && item.Kind == "element");
        var plainText = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "TextBlock" && item.Kind == "element");
        var oldHeader = catalog.First(item => item.GroupId == "blank" && item.ComponentName == "HeaderBlock" && item.Kind == "element");
        var normal = new HyperlibNamedPageTemplateDefinition { Id = "page-default", Name = "Main Page", Definition = new() { HeaderLayout = first, BodyTemplateId = body.Id,
            HeaderTemplateId = oldHeader.Id, HeaderStyle = new() { Text = "Legacy header must not duplicate the new builder" } } };
        var alternate = new HyperlibNamedPageTemplateDefinition { Id = "page-alt", Name = "Alternate", Definition = new() { HeaderLayout = HyperlibHeaderLayouts.Create("centered-title"), BodyTemplateId = body.Id } };
        var request = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Header foundations", Description = "", SelectedToolsetIds = [body.Id, plainText.Id, oldHeader.Id],
            LandingTemplate = ContentJson.Clone(normal.Definition), PageTemplate = normal.Definition, PageTemplates = [normal, alternate], DefaultPageTemplateId = normal.Id };
        var project = await generator.CreateAsync(Path.Combine(output, "header-project-" + Guid.NewGuid().ToString("N")), "site", request, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var service = new HyperlibProjectPipelineService();
        var pipeline = (await service.ReadAsync(project, ct))!;
        var imported = HyperlibHeaderLayouts.Elements(pipeline.PageTemplate.Definition.HeaderLayout).Where(item => item.Kind is "image" or "video").ToArray();
        Require(imported.All(item => item.MediaPath.StartsWith("/media/studio-header/", StringComparison.Ordinal) && File.Exists(Path.Combine(library, "Media", item.MediaPath[7..].Replace('/', Path.DirectorySeparatorChar))))
            && !File.ReadAllText(Path.Combine(project, ".studio/pipeline.json")).Contains(image, StringComparison.Ordinal), "Header SVG images and real WebM video must be independently owned media, without local-machine paths.");
        Require(pipeline.PageTemplate.Blocks.Count(item => item.Type == HyperlibHeaderLayouts.BlockType) == 2
            && !pipeline.PageTemplate.Blocks.Any(item => pipeline.Toolkit.Single(tool => tool.ChoiceId == oldHeader.Id).BlockTypeIds.Contains(item.Type))
            && pipeline.PageTemplate.Appearance["studioHeaderText"].GetString() == "", "An explicitly enabled builder must replace legacy header contents without duplicate title chrome.");
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        var originalLanding = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        foreach (var preset in HyperlibHeaderLayouts.All)
        {
            var recipe = new HyperlibPageTemplateDefinition { HeaderLayout = HyperlibHeaderLayouts.Create(preset.Id), IncludeNavigation = false };
            var composed = HyperlibProjectPipelineService.Compose(recipe, pipeline.Toolkit, TemplateObjectLibrary.ReadDefinition(project), "test");
            var page = ContentJson.Clone(originalLanding);
            page.Revision = state.Pages[page.Id].Revision;
            page.Blocks = composed.Blocks;
            state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Persist editable header foundation: " + preset.Title,
                Operations = [new() { Kind = "savePage", Page = page }] }, ct);
            Require((await store.ReadAsync(ct)).Pages[page.Id].Blocks.Count(item => item.Type == HyperlibHeaderLayouts.BlockType) == recipe.HeaderLayout.Blocks.Count,
                "Every one of the ten headers must pass actual content validation, journaling, and readback.");
        }
        var large = new HyperlibHeaderBlockDefinition { Height = 1000, Elements = Enumerable.Range(0, 180).Select(index => new HyperlibHeaderElementDefinition {
            Text = "Element " + index, FontSize = 12, WidthPercent = 9, X = index % 10 * 10, Y = index / 10 * 5 }).ToList() };
        var largeRecipe = new HyperlibPageTemplateDefinition { HeaderLayout = new() { Enabled = true, Blocks = [large] }, IncludeNavigation = false };
        var largeLayout = HyperlibProjectPipelineService.Compose(largeRecipe, pipeline.Toolkit, TemplateObjectLibrary.ReadDefinition(project), "large");
        Require(largeLayout.Blocks.Single().Values["layout"].GetString()!.Length > 4096 && largeLayout.Appearance.All(item => item.Value.GetRawText().Length <= 4096),
            "Large header structure must live in real Page blocks while all appearance values remain inside the unchanged 4 KiB limit.");
        var largePage = ContentJson.Clone(originalLanding); largePage.Blocks = largeLayout.Blocks; largePage.Revision = state.Pages[largePage.Id].Revision;
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Persist a header with 180 individually editable elements", Operations = [new() { Kind = "savePage", Page = largePage }] }, ct);
        Require(HyperlibHeaderLayouts.RenderSerializedBlock(state.Pages[largePage.Id].Blocks.Single().Values["layout"].GetString()!).Contains("Element 179", StringComparison.Ordinal),
            "A substantial header must pass content storage and the real shared renderer without raising Core limits.");
        originalLanding.Revision = state.Pages[originalLanding.Id].Revision;
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Restore the landing foundation", Operations = [new() { Kind = "savePage", Page = originalLanding }] }, ct);
        using var hosts = new HyperlibWorkPreviewHostService();
        var endpoint = await hosts.EnsureAsync(library, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) }; client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        var batch = new OperationBatch { ExpectedRevision = state.Revision, Description = "Create Pages from header foundations", Operations = [
            new() { Kind = "createNode", Id = "authored-header-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Authored" },
            new() { Kind = "createNode", Id = "empty-header-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Untouched" },
            new() { Kind = "createNode", Id = "alternate-header-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Alternate", Appearance = new() { ["studioPageTemplateId"] = ContentJson.Value(alternate.Id) } }] };
        state = await PostAsync(client, endpoint, batch, ct);
        var alternateSnapshot = JsonSerializer.Serialize(state.Pages["alternate-header-page"], ContentJson.Options);
        var authored = ContentJson.Clone(state.Pages["authored-header-page"]);
        var rowBlock = authored.Blocks.First(item => item.Type == HyperlibHeaderLayouts.BlockType);
        var row = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(rowBlock.Values["layout"].GetString()!, ContentJson.Options)!;
        Find(row, textId).Text = "Authored header text must survive";
        Find(row, nestedId).Text = "Authored nested text must survive removal";
        rowBlock.Values["layout"] = ContentJson.Value(JsonSerializer.Serialize(row, ContentJson.Options));
        authored.Sources.Add(new() { Id = "header-source", Title = "Header source", Url = "https://example.com/header", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [rowBlock.Id] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author independent header content", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        var next = (await service.ReadDefinitionAsync(project, ct))!;
        next.PageTemplate.HeaderLayout.Blocks[0].Height = 260;
        next.PageTemplate.HeaderLayout.Blocks[0].Elements[0].WidthPercent = 75;
        Find(next.PageTemplate.HeaderLayout.Blocks[0], textId).Text = "A changed shared label";
        next.PageTemplate.HeaderLayout.Blocks[0].Elements[0].Children[^1].Children.Clear();
        pipeline = await generator.UpdateFoundationAsync(project, next, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        var changedRow = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(state.Pages[authored.Id].Blocks.Single(item => item.Id == rowBlock.Id).Values["layout"].GetString()!, ContentJson.Options)!;
        Require(changedRow.Height == 260 && changedRow.Elements[0].WidthPercent == 75 && Find(changedRow, textId).Text == "Authored header text must survive"
            && Find(changedRow, nestedId).Text == "Authored nested text must survive removal", "Shared geometry must update while independent text and removed populated nested elements survive.");
        Require(JsonSerializer.Serialize(state.Pages["alternate-header-page"], ContentJson.Options) == alternateSnapshot && state.Pages[authored.Id].Sources.Single().BlockIds.Contains(rowBlock.Id),
            "Header propagation must preserve citations and leave other named-template Pages untouched.");
        next = (await service.ReadDefinitionAsync(project, ct))!; next.PageTemplate.HeaderLayout.Blocks.RemoveAt(0);
        pipeline = await generator.UpdateFoundationAsync(project, next, pipeline.Revision, state.Revision, ct); state = await store.ReadAsync(ct);
        Require(state.Pages[authored.Id].Blocks.Any(item => item.Id == rowBlock.Id && item.Region == "main")
            && !state.Pages["empty-header-page"].Blocks.Any(item => item.Id == rowBlock.Id), "Removing a row must retain populated per-Page content independently and remove untouched copies.");
        var lastEndpoint = await hosts.EnsureAsync(library, ct);
        using (var runtime = JsonDocument.Parse(await client.GetStringAsync(new Uri(lastEndpoint, "api/runtime/" + pipeline.WorkId), ct)))
        {
            var prefix = runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
            var html = await client.GetStringAsync(new Uri(lastEndpoint, prefix.TrimStart('/') + "page/empty-header-page"), ct);
            Require(html.Contains("studio-header-row", StringComparison.Ordinal) && html.Contains(imported[0].MediaPath, StringComparison.Ordinal)
                && html.Contains("<video", StringComparison.Ordinal), "The actual portable renderer must display the shared header component, owned image, and owned video.");
        }
        var bad = HyperlibHeaderLayouts.Create("simple-title"); bad.Blocks[0].Elements[0].Text = "<script>alert('unsafe')</script>";
        Require(!HyperlibHeaderLayouts.RenderHtml(bad).Contains("<script", StringComparison.Ordinal), "Header text must be encoded as text rather than executable markup.");
        bad.Blocks[0].Elements[0].FontFamily = "Arial;display:none";
        try { HyperlibHeaderLayouts.Validate(bad); throw new Exception("Unsafe CSS accepted."); } catch (InvalidDataException) { }
        var result = "STUDIO_HEADER_LAYOUTS_OK existing-library-audit|ten-distinct-editable-headers|ten-actual-content-saves|180-elements-existing-core-limits|no-html-in-appearance|owned-svg-video-media|legacy-header-replaced|actual-http-page-creation|nested-content-preserved|geometry-propagated|removed-authored-row-preserved|empty-row-removal|named-template-isolation|citations-preserved|actual-portable-renderer|html-css-safety";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-header-layouts-result.txt"), result + "\n" + project, ct);
        return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var source = Path.GetFullPath(Path.Combine(oldWorkRoot, "../..")); var protectedFiles = StudioGuidedUpgradeVerifier.HashTree(source);
        var library = Path.Combine(Path.GetFullPath(output), "old-background-copy-" + Guid.NewGuid().ToString("N"));
        if (!library.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use isolated QA output for header upgrade verification.");
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(source, library, ct);
        var project = Path.Combine(library, Path.GetRelativePath(source, oldWorkRoot));
        var before = await new ContentStore(library).ReadAsync(ct); var service = new HyperlibProjectPipelineService();
        var oldTemplate = TemplateObjectLibrary.ReadDefinition(project);
        var pipeline = (await service.ReadAsync(project, ct))!;
        using var hosts = new HyperlibWorkPreviewHostService(); using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var oldEndpoint = await hosts.EnsureAsync(library, ct);
        using (var api = JsonDocument.Parse(await client.GetStringAsync(new Uri(oldEndpoint, "api/studio/pipeline"), ct)))
            Require(!api.RootElement.GetProperty("pageTemplate").GetProperty("definition").TryGetProperty("headerLayout", out _), "Observe the actual shipped Step 1 gateway before its header upgrade.");
        var definition = (await service.ReadDefinitionAsync(project, ct))!;
        definition.PageTemplate.HeaderLayout = HyperlibHeaderLayouts.Create("two-row");
        pipeline = await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project, definition, pipeline.Revision, before.Revision, ct);
        var after = await new ContentStore(library).ReadAsync(ct);
        foreach (var page in before.Pages)
            foreach (var block in page.Value.Blocks.Where(block => oldTemplate.Blocks.Single(schema => schema.Id == block.Type).Fields.Any(field => field.Type is "string" or "text" or "url" or "media"
                && block.Values.TryGetValue(field.Id, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))))
                Require(after.Pages[page.Key].Blocks.Any(current => current.Id == block.Id && current.Type == block.Type && JsonSerializer.Serialize(current.Values, ContentJson.Options) == JsonSerializer.Serialize(block.Values, ContentJson.Options)),
                    "An actual old-project header upgrade must preserve every populated existing object.");
        Require(before.Pages.All(page => JsonSerializer.Serialize(page.Value.Sources, ContentJson.Options) == JsonSerializer.Serialize(after.Pages[page.Key].Sources, ContentJson.Options)),
            "An old-project header upgrade must preserve existing source citations.");
        var newEndpoint = await hosts.EnsureAsync(library, ct); Require(newEndpoint != oldEndpoint, "Header source installation must restart only its owned generated gateway.");
        using (var api = JsonDocument.Parse(await client.GetStringAsync(new Uri(newEndpoint, "api/studio/pipeline"), ct)))
            Require(api.RootElement.GetProperty("pageTemplate").GetProperty("definition").GetProperty("headerLayout").GetProperty("blocks").GetArrayLength() == 2,
                "The upgraded gateway must serialize the editable two-row header model.");
        Require(File.Exists(Path.Combine(project, "src/Pages/Blocks/StudioHeaderRowBlock.razor")) && Directory.Exists(Path.Combine(library, ".studio/upgrades")), "Upgrade source and backups must stay independently owned by the project.");
        var finalFiles = StudioGuidedUpgradeVerifier.HashTree(source);
        Require(protectedFiles.Count == finalFiles.Count && protectedFiles.All(item => finalFiles.TryGetValue(item.Key, out var hash) && hash == item.Value), "The previous deployed fixture must remain unchanged.");
        var result = "STUDIO_HEADER_UPGRADE_OK actual-oct16-background-project|old-api-observed|populated-objects-preserved|owned-source-backups|owned-gateway-restart|new-two-row-api|original-fixture-unchanged";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-header-upgrade-result.txt"), result + "\n" + project, ct); return result;
    }

    private static HyperlibHeaderElementDefinition Find(HyperlibHeaderBlockDefinition row, string id)
        => HyperlibHeaderLayouts.Elements(new() { Enabled = true, Blocks = [row] }).Single(item => item.Id == id);
    private static async Task<LibraryState> PostAsync(HttpClient client, Uri endpoint, OperationBatch batch, CancellationToken ct)
    { using var response = await client.PostAsync(new Uri(endpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(batch, ContentJson.Options), Encoding.UTF8, "application/json"), ct); var body = await response.Content.ReadAsStringAsync(ct); Require(response.IsSuccessStatusCode, "The actual header gateway write failed: " + body); return JsonSerializer.Deserialize<LibraryState>(body, ContentJson.Options)!; }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void RemoveIds(JsonNode node)
    { if (node is JsonObject obj) { obj.Remove("id"); foreach (var child in obj.ToArray()) if (child.Value is not null) RemoveIds(child.Value); } else if (node is JsonArray array) foreach (var child in array) if (child is not null) RemoveIds(child); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
