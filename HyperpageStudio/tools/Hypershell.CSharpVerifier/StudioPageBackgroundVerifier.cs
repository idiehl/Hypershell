using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Exercises real saved surfaces, project-owned image import, named Page propagation, and the portable gateway.</summary>
public static class StudioPageBackgroundVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        Require(HyperlibBackgroundDesigns.All.Count == 10 && HyperlibBackgroundDesigns.All.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == 10,
            "Step 1 must expose exactly ten distinct curated background designs.");
        var designs = HyperlibBackgroundDesigns.All.Select(item => HyperlibBackgroundDesigns.SurfaceCss("#13293d", new() { DesignId = item.Id })).ToArray();
        Require(designs.Distinct(StringComparer.Ordinal).Count() == 10 && designs.All(css => css.Contains("background", StringComparison.Ordinal)),
            "Every curated choice must produce its own actual renderable Page surface.");
        var oldRecipe = JsonSerializer.Deserialize<HyperlibPageTemplateDefinition>("{\"backgroundColor\":\"#112233\"}", ContentJson.Options)!;
        Require(oldRecipe.BackgroundSurface.DesignId == "solid" && oldRecipe.BackgroundSurface.BorderWidth == 0,
            "Older recipes must retain their plain background and no border without a migration.");
        foreach (var invalid in new HyperlibPageBackgroundDefinition[] {
            new() { DesignId = "unknown-background" }, new() { BorderWidth = double.NaN }, new() { BorderWidth = 21 },
            new() { BorderRadius = 201 }, new() { BorderColor = "red;display:none" }, new() { BorderStyle = "url(evil)" },
            new() { ImageFit = "stretch" }, new() { DesignId = "image", ImagePath = "https://example.com/image.png" },
            new() { DesignId = "image", ImagePath = "/media/../outside.png" } })
        {
            try { _ = HyperlibBackgroundDesigns.SurfaceCss("#112233", invalid); throw new InvalidOperationException("An unsafe background recipe was accepted."); }
            catch (InvalidDataException) { }
        }
        var photoPath = Path.Combine(output, "background-source.png");
        var photoBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j6XkAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(photoPath, photoBytes, ct);
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var body = generator.GetCatalog().First(item => item.GroupId == "blank" && item.ComponentName == "RichTextBlock" && item.Kind == "element");
        var normal = new HyperlibNamedPageTemplateDefinition { Id = "page-default", Name = "Reading Page", Definition = new() {
            BodyTemplateId = body.Id, BackgroundColor = "#13293d", BackgroundSurface = new() { BorderWidth = 2, BorderRadius = 16, BorderColor = "#65a88e" } } };
        var gallery = new HyperlibNamedPageTemplateDefinition { Id = "page-photo", Name = "Photo Page", Definition = new() {
            BodyTemplateId = body.Id, BackgroundColor = "#eef1f5", BackgroundSurface = new() { DesignId = "image", ImagePath = photoPath,
                ImageFit = "cover", BorderColor = "#225577", BorderWidth = 3, BorderStyle = "dashed", BorderRadius = 10 } } };
        var request = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Background foundations", Description = "",
            SelectedToolsetIds = [body.Id], LandingTemplate = new() { BackgroundColor = "#060e1f", BackgroundSurface = new() { DesignId = "atlas-night" } },
            PageTemplates = [normal, gallery], PageTemplate = normal.Definition, DefaultPageTemplateId = normal.Id };
        var project = await generator.CreateAsync(Path.Combine(output, "background-project-" + Guid.NewGuid().ToString("N")), "site", request, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var service = new HyperlibProjectPipelineService();
        var pipeline = (await service.ReadAsync(project, ct))!;
        var photoTemplate = HyperlibProjectPipelineService.GetPageTemplate(pipeline, gallery.Id);
        var imageUrl = photoTemplate.Definition.BackgroundSurface.ImagePath;
        Require(imageUrl.StartsWith("/media/work-starter/", StringComparison.Ordinal)
            && File.ReadAllBytes(Path.Combine(library, "Media", imageUrl[7..].Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(photoBytes)
            && !File.ReadAllText(Path.Combine(project, ".studio/pipeline.json")).Contains(photoPath, StringComparison.Ordinal),
            "Background photographs must be independently imported and persisted without machine-local paths.");
        var photoCss = photoTemplate.Appearance["studioBackgroundCss"].GetString()!;
        Require(photoCss.Contains(imageUrl, StringComparison.Ordinal) && photoCss.Contains("cover", StringComparison.Ordinal)
            && !photoCss.Contains("linear-gradient", StringComparison.Ordinal) && photoCss.Contains("dashed", StringComparison.Ordinal)
            && photoCss.Contains("10px", StringComparison.Ordinal),
            "The whole Page surface must use the raw selected photograph, fit, border style, and radius without an obscuring overlay.");
        foreach (var fit in new[] { "contain", "tile" })
        {
            var surface = ContentJson.Clone(photoTemplate.Definition.BackgroundSurface);
            surface.ImageFit = fit;
            var css = HyperlibBackgroundDesigns.SurfaceCss("#112233", surface);
            Require(fit == "tile" ? css.Contains("repeat", StringComparison.Ordinal) && !css.Contains("no-repeat", StringComparison.Ordinal)
                : css.Contains("contain", StringComparison.Ordinal), "Photo fit choices must produce their intended CSS behavior.");
        }
        // This exercises the real 4 KiB appearance-value boundary, rather than only accepting valid CSS.
        // Large inline SVG designs previously passed preview checks but could not enter the content journal.
        var surfaceStore = new ContentStore(library);
        var surfaceState = await surfaceStore.ReadAsync(ct);
        var originalLandingAppearance = ContentJson.Clone(surfaceState.Nodes.Single(item => item.Id == pipeline.LandingPageId).Appearance);
        foreach (var design in HyperlibBackgroundDesigns.All)
        {
            var recipe = ContentJson.Clone(pipeline.LandingTemplate.Definition);
            recipe.BackgroundColor = design.DefaultColor;
            recipe.BackgroundSurface = new() { DesignId = design.Id };
            var appearance = ContentJson.Clone(originalLandingAppearance);
            foreach (var setting in HyperlibProjectPipelineService.Appearance(recipe)) appearance[setting.Key] = setting.Value.Clone();
            Require(appearance["studioBackgroundCss"].GetRawText().Length <= 4096,
                "The serialized " + design.Id + " background must fit the actual 4 KiB appearance-value limit.");
            surfaceState = await surfaceStore.ApplyAsync(new() { ExpectedRevision = surfaceState.Revision,
                Description = "Verify saved curated Page background: " + design.Id,
                Operations = [new() { Kind = "setAppearance", Id = pipeline.LandingPageId, Appearance = appearance }] }, ct);
            var reopened = await surfaceStore.ReadAsync(ct);
            Require(reopened.Nodes.Single(item => item.Id == pipeline.LandingPageId).Appearance["studioBackgroundCss"].GetRawText()
                == appearance["studioBackgroundCss"].GetRawText(),
                "The " + design.Id + " background must survive ContentStore validation, journaling, and readback.");
        }
        await surfaceStore.ApplyAsync(new() { ExpectedRevision = surfaceState.Revision, Description = "Restore the original landing surface after curated-design verification",
            Operations = [new() { Kind = "setAppearance", Id = pipeline.LandingPageId, Appearance = originalLandingAppearance }] }, ct);
        using var hosts = new HyperlibWorkPreviewHostService();
        var endpoint = await hosts.EnsureAsync(library, ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        var state = await new ContentStore(library).ReadAsync(ct);
        var batch = new OperationBatch { ExpectedRevision = state.Revision, Description = "Create Pages from distinct background foundations", Operations = [
            new() { Kind = "createNode", Id = "plain-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Reading", Appearance = new() { ["studioPageTemplateId"] = ContentJson.Value(normal.Id) } },
            new() { Kind = "createNode", Id = "photo-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Photo", Appearance = new() { ["studioPageTemplateId"] = ContentJson.Value(gallery.Id) } }] };
        using var created = await client.PostAsync(new Uri(endpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(batch, ContentJson.Options), Encoding.UTF8, "application/json"), ct);
        var createdText = await created.Content.ReadAsStringAsync(ct);
        Require(created.IsSuccessStatusCode, "The portable gateway must compile with its embedded background helper and create Pages: " + createdText);
        state = JsonSerializer.Deserialize<LibraryState>(createdText, ContentJson.Options)!;
        var node = state.Nodes.Single(item => item.Id == "photo-page");
        Require(node.Appearance["studioBackgroundCss"].GetString() == photoCss && node.Appearance["studioBackgroundImage"].GetString() == imageUrl,
            "Actual HTTP Page creation must apply the selected named template's saved surface.");
        var authored = ContentJson.Clone(state.Pages[node.Id]);
        var text = authored.Blocks.Single(block => TemplateObjectLibrary.ReadDefinition(project).Blocks.Single(schema => schema.Id == block.Type).Fields.Any(field => field.Id == "text"));
        text.Values["text"] = ContentJson.Value("Keep my article while its backdrop changes.");
        var ownAppearance = ContentJson.Clone(node.Appearance);
        ownAppearance["background"] = ContentJson.Value("#aabbcc");
        state = await new ContentStore(library).ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author content and a local base-color override", Operations = [
            new() { Kind = "savePage", Page = authored }, new() { Kind = "setAppearance", Id = node.Id, Appearance = ownAppearance }] }, ct);
        var untouched = JsonSerializer.Serialize(state.Pages["plain-page"], ContentJson.Options);
        var next = (await service.ReadDefinitionAsync(project, ct))!;
        var changed = next.PageTemplates.Single(item => item.Id == gallery.Id).Definition;
        changed.BackgroundColor = "#20344a";
        changed.BackgroundSurface.DesignId = "blueprint-grid";
        changed.BackgroundSurface.BorderStyle = "dotted";
        pipeline = await generator.UpdateFoundationAsync(project, next, pipeline.Revision, state.Revision, ct);
        state = await new ContentStore(library).ReadAsync(ct);
        var changedNode = state.Nodes.Single(item => item.Id == "photo-page");
        Require(changedNode.Appearance["background"].GetString() == "#aabbcc" && changedNode.Appearance["studioBackgroundCss"].GetString()!.Contains("dotted", StringComparison.Ordinal)
            && state.Pages[node.Id].Blocks.Single(block => block.Id == text.Id).Values["text"].GetString() == "Keep my article while its backdrop changes.",
            "Shared surface changes must preserve article content and existing Page-specific base colors.");
        Require(JsonSerializer.Serialize(state.Pages["plain-page"], ContentJson.Options) == untouched,
            "Changing one named template's backdrop must not alter Pages using another template.");
        Require(File.Exists(Path.Combine(library, "src/HyperLib.Host/StudioPipeline/HyperlibBackgroundDesigns.cs"))
            && File.ReadAllText(Path.Combine(project, "Templates/StudioRuntime/HyperLib.Runtime/Components/DynamicPage.razor")).Contains("Options.Appearance(\"studioBackgroundCss\"", StringComparison.Ordinal),
            "Portable projects and published Works must carry their actual background rendering implementation.");
        var result = "STUDIO_PAGE_BACKGROUNDS_OK ten-curated-distinct-designs|all-ten-4k-appearance-bound|all-ten-content-journal-readback|old-recipe-default|unsafe-surface-rejection|owned-photo-import|raw-photo-no-overlay|cover-contain-tile|canvas-border-style-radius|portable-gateway-build|actual-http-template-surface|named-surface-propagation|article-preserved|page-color-override-preserved|other-template-untouched|portable-helper-runtime";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-page-backgrounds-result.txt"), result + "\n" + project, ct);
        return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var originalWork = Path.GetFullPath(oldWorkRoot);
        var originalRoot = Path.GetFullPath(Path.Combine(originalWork, "../.."));
        var protectedInputs = StudioGuidedUpgradeVerifier.HashTree(originalRoot);
        var library = Path.Combine(Path.GetFullPath(output), "old-guided-copy-" + Guid.NewGuid().ToString("N"));
        if (!library.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Run background upgrade verification inside QA output.");
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(originalRoot, library, ct);
        var project = Path.Combine(library, Path.GetRelativePath(originalRoot, originalWork));
        var service = new HyperlibProjectPipelineService();
        var before = await new ContentStore(library).ReadAsync(ct);
        var pipeline = (await service.ReadAsync(project, ct))!;
        Require(pipeline.PageTemplates.Count >= 2 && !File.Exists(Path.Combine(library, "src/HyperLib.Host/StudioPipeline/HyperlibBackgroundDesigns.cs")),
            "The upgrade fixture must be an actual prior guided project without the new background helper.");
        var originalSupport = await File.ReadAllTextAsync(Path.Combine(library, "src/HyperLib.Host/StudioPipeline/HyperlibProjectPipelineService.cs"), ct);
        using var hosts = new HyperlibWorkPreviewHostService();
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        var oldEndpoint = await hosts.EnsureAsync(library, ct);
        using (var oldApi = JsonDocument.Parse(await client.GetStringAsync(new Uri(oldEndpoint, "api/studio/pipeline"), ct)))
            Require(!oldApi.RootElement.GetProperty("pageTemplate").GetProperty("definition").TryGetProperty("backgroundSurface", out _),
                "Observe the actual previous gateway's pre-background API before upgrading.");
        var photoPath = Path.Combine(output, "upgrade-background.png");
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j6XkAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(photoPath, bytes, ct);
        var next = (await service.ReadDefinitionAsync(project, ct))!;
        next.LandingTemplate.BackgroundSurface = new() { DesignId = "atlas-night", BorderWidth = 2, BorderColor = "#556677" };
        next.PageTemplate.BackgroundSurface = new() { DesignId = "image", ImagePath = photoPath, ImageFit = "contain", BorderWidth = 4, BorderColor = "#334455", BorderStyle = "double", BorderRadius = 18 };
        pipeline = await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project, next, pipeline.Revision, before.Revision, ct);
        var after = await new ContentStore(library).ReadAsync(ct);
        foreach (var saved in before.Pages)
            Require(after.Pages.TryGetValue(saved.Key, out var page)
                && JsonSerializer.Serialize(page.Blocks, ContentJson.Options) == JsonSerializer.Serialize(saved.Value.Blocks, ContentJson.Options)
                && JsonSerializer.Serialize(page.Sources, ContentJson.Options) == JsonSerializer.Serialize(saved.Value.Sources, ContentJson.Options),
                "Adding Page surfaces to a shipped guided project must preserve all existing content, media fields, and citations.");
        Require(Directory.GetFiles(Path.Combine(library, ".studio/upgrades"), "HyperlibProjectPipelineService.cs", SearchOption.AllDirectories)
            .Any(path => File.ReadAllText(path) == originalSupport), "The exact prior support source must be backed up outside the published Work.");
        var newEndpoint = await hosts.EnsureAsync(library, ct);
        Require(newEndpoint != oldEndpoint, "Adding background support must restart only the copied project's owned gateway.");
        using (var newApi = JsonDocument.Parse(await client.GetStringAsync(new Uri(newEndpoint, "api/studio/pipeline"), ct)))
            Require(newApi.RootElement.GetProperty("pageTemplate").GetProperty("definition").GetProperty("backgroundSurface").GetProperty("designId").GetString() == "image",
                "The restarted actual API must serialize the upgraded background recipe.");
        var create = new OperationBatch { ExpectedRevision = after.Revision, Description = "Create a Page using upgraded photograph styling", Operations = [
            new() { Kind = "createNode", Id = "upgraded-background-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Background upgrade" }] };
        using var created = await client.PostAsync(new Uri(newEndpoint, "api/operations"), new StringContent(JsonSerializer.Serialize(create, ContentJson.Options), Encoding.UTF8, "application/json"), ct);
        var body = await created.Content.ReadAsStringAsync(ct);
        Require(created.IsSuccessStatusCode, "The restarted gateway must create a Page from its new image background: " + body);
        var state = JsonSerializer.Deserialize<LibraryState>(body, ContentJson.Options)!;
        var node = state.Nodes.Single(item => item.Id == "upgraded-background-page");
        var imageUrl = pipeline.PageTemplate.Definition.BackgroundSurface.ImagePath;
        Require(node.Appearance["studioBackgroundImage"].GetString() == imageUrl && node.Appearance["studioBackgroundCss"].GetString()!.Contains("contain", StringComparison.Ordinal),
            "New Pages in an upgraded project must independently clone its saved whole-Page background and border.");
        using (var runtime = JsonDocument.Parse(await client.GetStringAsync(new Uri(newEndpoint, "api/runtime/" + pipeline.WorkId), ct)))
        {
            var prefix = runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
            var html = await client.GetStringAsync(new Uri(newEndpoint, prefix.TrimStart('/') + "page/" + node.Id), ct);
            Require(html.Contains("studio-guided-layout", StringComparison.Ordinal) && html.Contains(imageUrl, StringComparison.Ordinal)
                && html.Contains("background-size:contain", StringComparison.Ordinal) && html.Contains("border:4px double #334455", StringComparison.Ordinal),
                "The actual upgraded Work renderer must apply the background and outer border to its document root.");
        }
        var finalInput = StudioGuidedUpgradeVerifier.HashTree(originalRoot);
        Require(protectedInputs.Count == finalInput.Count && protectedInputs.All(pair => finalInput.TryGetValue(pair.Key, out var hash) && hash == pair.Value),
            "The previous deployed fixture must remain byte-for-byte unchanged.");
        var result = "STUDIO_PAGE_BACKGROUND_UPGRADE_OK actual-oct15-input|old-api-observed|prior-support-backup|owned-helper-installed|existing-content-preserved|owned-gateway-restarted|new-api-observed|new-page-inherits-photo-border|actual-work-renderer-styled|original-fixture-unchanged";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-page-background-upgrade-result.txt"), result + "\n" + project, ct);
        return result;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
