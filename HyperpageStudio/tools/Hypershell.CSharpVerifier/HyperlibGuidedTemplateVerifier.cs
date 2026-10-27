using System.IO;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Exercises named reusable foundations against real independently generated content.</summary>
public static class HyperlibGuidedTemplateVerifier
{
    public static async Task VerifyAsync(string project, string sourceLibrary, string output, CancellationToken ct = default)
    {
        var service = new HyperlibProjectPipelineService();
        var generator = new HyperlibWorkProjectService(sourceLibrary);
        var pipeline = (await service.ReadAsync(project, ct))!;
        var definition = (await service.ReadDefinitionAsync(project, ct))!;
        Require(ReferenceEquals(definition.PageTemplate, definition.PageTemplates.Single(item => item.Id == definition.DefaultPageTemplateId).Definition),
            "The legacy in-memory PageTemplate alias must reference the named default recipe.");
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        var defaultRecipe = definition.PageTemplate;
        defaultRecipe.HeaderStyle = new() { BackgroundColor = "#14374a", TextColor = "#ffffff", BorderColor = "#559977",
            BorderWidth = 2, BorderRadius = 12, Padding = 28, FontFamily = "Georgia", FontSize = 26, Alignment = "center", Text = "Reusable site identity" };
        var logoFile = Path.Combine(output, "guided-owned-logo.png");
        var logoBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j6XkAAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(logoFile, logoBytes, ct);
        defaultRecipe.HeaderStyle.LogoPath = logoFile;
        defaultRecipe.HeaderStyle.LogoPlacement = "right";
        var gallery = new HyperlibNamedPageTemplateDefinition { Id = "page-gallery", Name = "Gallery", Definition = ContentJson.Clone(defaultRecipe) };
        gallery.Definition.BackgroundColor = "#e9edf3";
        gallery.Definition.ContentWidth = "wide";
        gallery.Definition.HeaderStyle.Text = "Gallery identity";
        gallery.Definition.NavigationStyle = new() { BackgroundColor = "#223344", TextColor = "#ffffff", BorderWidth = 1, BorderColor = "#334455", Padding = 12 };
        definition.PageTemplates.Add(gallery);
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        Require(pipeline.PageTemplates.Count == 2 && pipeline.PageTemplate.Id == pipeline.DefaultPageTemplateId,
            "Named templates and their default identity must persist independently.");
        var importedLogo = pipeline.PageTemplate.Definition.HeaderStyle.LogoPath;
        Require(importedLogo.StartsWith("/media/work-starter/", StringComparison.Ordinal)
            && File.ReadAllBytes(Path.Combine(library, "Media", importedLogo[7..].Replace('/', Path.DirectorySeparatorChar))).SequenceEqual(logoBytes),
            "A local reusable logo must become an independently owned project media file.");
        var galleryTemplate = HyperlibProjectPipelineService.GetPageTemplate(pipeline, gallery.Id);
        Require(galleryTemplate.Appearance["studioHeaderCss"].GetString()!.Contains("padding:28px", StringComparison.Ordinal)
            && galleryTemplate.Appearance["studioHeaderLogoPlacement"].GetString() == "right"
            && galleryTemplate.Appearance["studioNavigationCss"].GetString()!.Contains("background:#223344", StringComparison.Ordinal),
            "Region color, border, padding, font, alignment, and logo choices must reach actual renderer appearance.");
        var page = new PageDocument { TemplateId = TemplateObjectLibrary.ReadDefinition(project).TemplateId, UsesEditableChrome = true,
            Blocks = ContentJson.Clone(galleryTemplate.Blocks) };
        var appearance = ContentJson.Clone(galleryTemplate.Appearance);
        appearance["studioPageTemplateId"] = ContentJson.Value(gallery.Id);
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Create Page from chosen named template", Operations =
            [new() { Kind = "createNode", Id = "gallery-page", NodeType = "page", ParentId = pipeline.WorkId, Title = "Gallery article", Page = page, Appearance = appearance }] }, ct);
        var authored = ContentJson.Clone(state.Pages["gallery-page"]);
        var textSlot = authored.Blocks.First(block => TemplateObjectLibrary.ReadDefinition(project).Blocks.Single(schema => schema.Id == block.Type).Fields.Any(field => field.Id == "text"));
        textSlot.Values["text"] = ContentJson.Value("Independent gallery article text");
        authored.Sources.Add(new() { Id = "gallery-source", Title = "Source", Url = "https://example.com/gallery", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [textSlot.Id] });
        var ownAppearance = ContentJson.Clone(state.Nodes.Single(node => node.Id == "gallery-page").Appearance);
        ownAppearance["studioHeaderText"] = ContentJson.Value("Page-specific header override");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author content and a Page override", Operations =
            [new() { Kind = "savePage", Page = authored }, new() { Kind = "setAppearance", Id = "gallery-page", Appearance = ownAppearance }] }, ct);
        var untouchedDefault = state.Pages["first-page"].Blocks.Select(block => JsonSerializer.Serialize(block, ContentJson.Options)).ToArray();
        var beforeDefaultAppearance = JsonSerializer.Serialize(state.Nodes.Single(node => node.Id == "first-page").Appearance, ContentJson.Options);
        definition = (await service.ReadDefinitionAsync(project, ct))!;
        var nextGallery = definition.PageTemplates.Single(item => item.Id == gallery.Id).Definition;
        nextGallery.BackgroundColor = "#fbf7ed";
        nextGallery.ContentAlignment = "right";
        nextGallery.HeaderStyle.Text = "Updated shared gallery identity";
        nextGallery.HeaderStyle.Padding = 36;
        pipeline = await generator.UpdateFoundationAsync(project, definition, pipeline.Revision, state.Revision, ct);
        state = await store.ReadAsync(ct);
        var changed = state.Nodes.Single(node => node.Id == "gallery-page");
        Require(changed.Appearance["studioPageTemplateId"].GetString() == gallery.Id && changed.Appearance["background"].GetString() == "#fbf7ed"
            && changed.Appearance["studioHeaderCss"].GetString()!.Contains("padding:36px", StringComparison.Ordinal),
            "Only the assigned named foundation must propagate its new layout and style.");
        Require(changed.Appearance["studioHeaderText"].GetString() == "Page-specific header override"
            && state.Pages["gallery-page"].Blocks.Single(block => block.Id == textSlot.Id).Values["text"].GetString() == "Independent gallery article text"
            && state.Pages["gallery-page"].Sources.Single().BlockIds.SequenceEqual([textSlot.Id]),
            "Named foundation changes must preserve authored text, source links, and explicit per-Page chrome overrides.");
        Require(state.Pages["first-page"].Blocks.Select(block => JsonSerializer.Serialize(block, ContentJson.Options)).SequenceEqual(untouchedDefault)
            && JsonSerializer.Serialize(state.Nodes.Single(node => node.Id == "first-page").Appearance, ContentJson.Options) == beforeDefaultAppearance,
            "Updating a second named template must leave Pages assigned to the default template unchanged.");
        var legacyRecipe = ContentJson.Clone(pipeline.PageTemplate.Definition);
        legacyRecipe.ContentWidth = "full";
        pipeline = await service.UpdateTemplatesAsync(project, new() { ExpectedPipelineRevision = pipeline.Revision,
            ExpectedContentRevision = state.Revision, LandingTemplate = pipeline.LandingTemplate.Definition, PageTemplate = legacyRecipe }, ct);
        state = await store.ReadAsync(ct);
        Require(pipeline.PageTemplates.Count == 2 && pipeline.PageTemplate.Definition.ContentWidth == "full"
            && HyperlibProjectPipelineService.GetPageTemplate(pipeline, gallery.Id).Definition.ContentAlignment == "right",
            "An older single-template API request must update only the default and retain every other named template.");
        try { _ = HyperlibProjectPipelineService.GetPageTemplate(pipeline, "unknown-template"); throw new Exception("Unknown template accepted."); }
        catch (InvalidDataException) { }
        var stable = await File.ReadAllTextAsync(Path.Combine(project, ".studio/pipeline.json"), ct);
        try
        {
            await service.UpdateTemplatesAsync(project, new() { ExpectedPipelineRevision = pipeline.Revision, ExpectedContentRevision = state.Revision,
                LandingTemplate = pipeline.LandingTemplate.Definition, DefaultPageTemplateId = pipeline.DefaultPageTemplateId,
                PageTemplates = [new() { Id = pipeline.PageTemplate.Id, Name = pipeline.PageTemplate.Name, Definition = pipeline.PageTemplate.Definition }] }, ct);
            throw new Exception("In-use named template removal accepted.");
        }
        catch (InvalidDataException) { }
        Require(await File.ReadAllTextAsync(Path.Combine(project, ".studio/pipeline.json"), ct) == stable && (await store.ReadAsync(ct)).Revision == state.Revision,
            "Removing an in-use named foundation must fail without changing the project or its content.");
        var old = new HyperlibProjectPipelineState { PageTemplate = ContentJson.Clone(pipeline.PageTemplate), PageTemplates = [] };
        HyperlibProjectPipelineService.NormalizeTemplates(old);
        Require(old.PageTemplates.Count == 1 && old.DefaultPageTemplateId == "page-default" && ReferenceEquals(old.PageTemplate, old.PageTemplates[0]),
            "Older V2 projects must normalize in memory to one named default without an on-open source migration.");
        Require(File.ReadAllText(Path.Combine(project, "Templates/StudioRuntime/HyperLib.Runtime/Components/DynamicPage.razor")).Contains("studio-guided-layout", StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(library, "src/HyperLib.Host/Program.cs")).Contains("GetPageTemplate(stagedPipeline, selectedId)", StringComparison.Ordinal),
            "Explicit foundation saves must keep portable project-owned runtime and gateway code current.");
        Require(Directory.EnumerateFiles(Path.Combine(project, "Templates/StudioRuntime"), "*.csproj", SearchOption.AllDirectories)
            .All(path => File.ReadAllText(path).Contains("<CompressionEnabled>false</CompressionEnabled>", StringComparison.Ordinal)),
            "Owned SDK compression must remain disabled for immutable reader snapshots on Windows.");
        await File.WriteAllTextAsync(Path.Combine(output, "hyperlib-guided-template-result.txt"),
            "HYPERLIB_GUIDED_TEMPLATES_OK named-recipes|default-alias|independent-owned-logo|region-style-persistence|named-page-assignment|template-isolation|content-citation-preservation|chrome-three-way-preservation|legacy-api-preserves-other-templates|unknown-id-rejected|in-use-removal-protected|legacy-v2-normalization|explicit-generated-upgrade|owned-compression-disabled\n" + project, ct);
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
