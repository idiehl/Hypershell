using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Services;

public static class StudioAuthoringLibraryVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var sourceIndex = Path.Combine(sourceLibraryRoot, "Content", "library.json"); var sourceHash = Hash(sourceIndex);
        var project = await new HyperlibWorkProjectService(sourceLibraryRoot).CreateAsync(Path.Combine(output, "library-project-" + Guid.NewGuid().ToString("N")), "site",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Authoring library verification"), ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        using var service = new StudioAuthoringLibraryService(sourceLibraryRoot, Path.Combine(output, "preview-cache"));
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!;
        var store = new ContentStore(library); var state = await store.ReadAsync(ct); var pageId = pipeline.LandingPageId;
        var items = await service.ListAsync(project, ct);
        Check(items.Count >= 900 && items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == items.Count,
            "The sidebar exposes the full core collection with unique stable identities, rather than only the project's previous shopping selections.");
        Check(new[] { "page", "section", "module", "element" }.All(scope => items.Any(item => item.Scope == scope && item.Compatible)),
            "Complete Pages, sections, modules, and individual elements are distinct usable library scopes.");
        Check(items.Count(item => item.Id.StartsWith("section:header:", StringComparison.Ordinal)) == HyperlibHeaderLayouts.All.Count
            && items.Count(item => item.Id.StartsWith("section:navigation:", StringComparison.Ordinal)) == HyperlibNavigationLayouts.All.Count
            && items.Count(item => item.Id.StartsWith("section:body:", StringComparison.Ordinal)) == HyperlibBodyLayouts.All.Count
            && items.Count(item => item.Id.StartsWith("section:footer:", StringComparison.Ordinal)) == HyperlibFooterLayouts.All.Count,
            "All existing Header, Navigation, Body, and Footer presets remain available as section templates.");
        Check(items.Any(item => item.Id == "element:text") && items.Any(item => item.Id == "element:studio-navigation")
            && !items.Any(item => item.Id == "element:studio-header-row"),
            "The complete local Elements collection includes real Work navigation while hiding only internal layout protocol objects.");
        Check(items.Single(item => item.Id == "object:blank:layout-two-columns").Scope == "module",
            "A small multi-object column arrangement is a reusable Module, while complete page-layout entries remain Pages.");
        var beforeBlank = Directory.Exists(Path.Combine(library, "Templates")) ? Directory.EnumerateDirectories(Path.Combine(library, "Templates")).Count() : 0;
        var blankRejected = false;
        try { await service.SavePageTemplateAsync(project, pageId, "Empty template", state.Revision, ct); }
        catch (InvalidDataException error) when (error.Message.Contains("Add some content", StringComparison.Ordinal)) { blankRejected = true; }
        Check(blankRejected && (!Directory.Exists(Path.Combine(library, "Templates")) || Directory.EnumerateDirectories(Path.Combine(library, "Templates")).Count() == beforeBlank),
            "Saving an empty Page stops before publishing an unusable template package.");
        var added = await service.AddAsync(project, "element:text", pageId, "main", state.Revision, ct: ct, insertionY: 0);
        state = added.State; var textId = added.AddedBlockIds.Single();
        var authored = ContentJson.Clone(state.Pages[pageId]); authored.Blocks.Single(block => block.Id == textId).Values["text"] = ContentJson.Value("Keep this authored paragraph and its independent Page values.");
        authored.Sources.Add(new() { Id = "library-source", Title = "Preserved source", Url = "https://example.com/source", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [textId] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author independent content before library imports", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        var staleHash = Hash(Path.Combine(project, "authoring.template.json")); var stale = false;
        try { await service.AddAsync(project, "section:header:simple-title", pageId, "main", state.Revision - 1, ct: ct); }
        catch (ConflictException) { stale = true; }
        Check(stale && Hash(Path.Combine(project, "authoring.template.json")) == staleHash,
            "A stale library action is rejected before changing content, schema, or component source.");
        added = await service.AddAsync(project, "section:header:simple-title", pageId, "main", state.Revision, ct: ct, insertionY: 420);
        state = added.State; var headerId = added.AddedBlockIds.Single();
        var definition = TemplateObjectLibrary.ReadDefinition(project); var header = state.Pages[pageId].Blocks.Single(block => block.Id == headerId);
        var headerSchema = definition.Blocks.Single(block => block.Id == header.Type);
        Check(header.Type.StartsWith("library-section-", StringComparison.Ordinal) && headerSchema.Fields.Any(field => field.Id == "part-0-text")
            && !headerSchema.Fields.Any(field => field.Id == "layout") && header.Values["studioY"].GetDouble() >= 420,
            "A section becomes an independent editable object with public content fields, no raw layout JSON, and measured placement below occupied content.");
        var headerSource = await File.ReadAllTextAsync(Path.Combine(project, "src", "Pages", "Blocks", headerSchema.Component + ".razor"), ct);
        Check(headerSource.Contains("LibraryField(element.Id, \"text\")", StringComparison.Ordinal) && headerSource.Contains("ApplyLibraryFields", StringComparison.Ordinal),
            "Section text binds real declared fields in the component used for inline editing and Properties.");
        authored = ContentJson.Clone(state.Pages[pageId]); authored.Blocks.Single(block => block.Id == headerId).Values["part-0-text"] = ContentJson.Value("A personally edited section heading");
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Edit an independent section's declared inline field", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        foreach (var section in new[] { "section:navigation:simple-links", "section:body:media-and-text", "section:footer:page-navigation" })
        { added = await service.AddAsync(project, section, pageId, "main", state.Revision, ct: ct, insertionY: 800 + state.Pages[pageId].Blocks.Count * 300); state = added.State; }
        Check(state.Pages[pageId].Blocks.Single(block => block.Id == textId).Values["text"].GetString()!.StartsWith("Keep this authored", StringComparison.Ordinal)
            && state.Pages[pageId].Sources.Single().BlockIds.SequenceEqual([textId]),
            "Adding all section families preserves the current Page's existing writing, block identities, and citations.");
        added = await service.AddAsync(project, "object:blank:layout-two-columns", pageId, "main", state.Revision, ct: ct, insertionY: 2400);
        state = added.State; var moduleId = added.AddedBlockIds.Single(); definition = TemplateObjectLibrary.ReadDefinition(project);
        var module = state.Pages[pageId].Blocks.Single(block => block.Id == moduleId); var moduleSchema = definition.Blocks.Single(block => block.Id == module.Type);
        Check(module.Type.StartsWith("library-module-", StringComparison.Ordinal) && moduleSchema.Fields.Any(field => field.Id == "object-1-span"),
            "A multi-object Module is one independent canvas group whose original child spans and editable Properties remain declared.");
        var sourceCatalog = new HyperlibWorkProjectService(sourceLibraryRoot).GetCatalog();
        var preset = sourceCatalog.First(item => item.Kind == "preset" && item.ComponentName == "CounterBlock");
        var original = new HyperlibWorkProjectService(sourceLibraryRoot).GetCatalogObject(preset.GroupId, preset.ObjectId);
        added = await service.AddAsync(project, "object:" + preset.Id, pageId, "main", state.Revision, ct: ct, insertionY: 2900);
        state = added.State; var configured = state.Pages[pageId].Blocks.Single(block => block.Id == added.AddedBlockIds.Single());
        Check(original.Blocks.Single().Values.All(pair => configured.Values.TryGetValue(pair.Key, out var value) && value.GetRawText() == pair.Value.GetRawText()),
            "Configured object presets retain their actual authored defaults instead of reverting to generic filler.");
        var sourceScript = Path.Combine(new HyperlibWorkProjectService(sourceLibraryRoot).GetCatalogProjectRoot(preset.GroupId), "src", "Pages", "wwwroot", "blocks", "counter.js");
        Check(File.Exists(sourceScript) && Directory.EnumerateFiles(Path.Combine(project, "src", "Pages", "wwwroot"), "counter.js", SearchOption.AllDirectories)
                .Any(path => Hash(path) == Hash(sourceScript)),
            "A real interactive preset's exact local script is available in the owned project, whether copied or safely reused.");
        var preserved = ContentJson.Clone(state.Pages[pageId]);
        added = await service.AddAsync(project, "page:hyperlib:insight-dashboard", pageId, "main", state.Revision, "Independent Dashboard", ct: ct);
        state = added.State; var dashboardId = added.PageId;
        Check(dashboardId != pageId && state.Pages[dashboardId].Blocks.Count > 1 && state.Nodes.Single(node => node.Id == dashboardId).ParentId == pipeline.WorkId
            && JsonSerializer.Serialize(state.Pages[pageId].Blocks, ContentJson.Options) == JsonSerializer.Serialize(preserved.Blocks, ContentJson.Options)
            && JsonSerializer.Serialize(state.Pages[pageId].Sources, ContentJson.Options) == JsonSerializer.Serialize(preserved.Sources, ContentJson.Options),
            "A complete template creates a new Page in the Work and preserves all existing content on the Page that was open.");
        definition = TemplateObjectLibrary.ReadDefinition(project);
        Check(state.Pages[dashboardId].Blocks.All(block => definition.Regions.Any(region => region.Id == block.Region)),
            "Aliased regions from a complete imported Page are durable in the on-disk schema before content is committed.");
        var dashboardChoiceId = new StudioCompleteTemplateService(sourceLibraryRoot).BuildDefinition("hyperlib:insight-dashboard", "Dashboard").LandingTemplate.CompleteTemplateObjectId;
        var dashboardChoice = sourceCatalog.Single(item => item.Id == dashboardChoiceId);
        var dashboardOriginal = new HyperlibWorkProjectService(sourceLibraryRoot).GetCatalogObject(dashboardChoice.GroupId, dashboardChoice.ObjectId);
        Check(state.Pages[dashboardId].Blocks.Select(block => block.Region).SequenceEqual(dashboardOriginal.Blocks.Select(block => block.Region)),
            "A complete gallery Page reuses compatible original regions rather than shifting content below an empty main canvas.");
        var saved = await service.SavePageTemplateAsync(project, pageId, "My reusable authored Page", state.Revision, ct);
        var savedId = saved.Item.Id;
        Check((await service.ListAsync(project, ct)).Any(item => item.Id == savedId && item.Scope == "page" && item.Collection == "My templates"),
            "Saving the current Page creates a durable project-owned template that appears in My templates.");
        added = await service.AddAsync(project, savedId, pageId, "main", state.Revision, "My independent saved copy", ct: ct);
        state = added.State; var savedCloneId = added.PageId; var clone = state.Pages[savedCloneId];
        Check(clone.Blocks.Count == preserved.Blocks.Count && clone.Blocks.All(block => preserved.Blocks.All(source => source.Id != block.Id))
            && clone.Blocks.Any(block => block.Values.TryGetValue("text", out var value) && value.GetString()!.StartsWith("Keep this authored", StringComparison.Ordinal))
            && clone.Blocks.Any(block => block.Values.TryGetValue("part-0-text", out var value) && value.GetString() == "A personally edited section heading"),
            "A saved Page template clones actual current content and section overrides into new independent block identities.");
        Check(clone.Blocks.Select(block => block.Region).SequenceEqual(preserved.Blocks.Select(block => block.Region))
            && clone.Blocks.Zip(preserved.Blocks).All(pair => new[] { "studioPositioned", "studioX", "studioY", "studioWidth", "studioHeight" }
                .All(field => pair.First.Values.GetValueOrDefault(field).GetRawText() == pair.Second.Values.GetValueOrDefault(field).GetRawText())),
            "Reusing a saved Page retains compatible original regions and exact canvas geometry, without inserting an empty main region above the clone.");
        Check(clone.Sources.Count == 1 && clone.Sources[0].BlockIds.All(id => clone.Blocks.Any(block => block.Id == id))
            && clone.Sources[0].BlockIds.All(id => id != textId), "Saved-template citations follow the cloned block IDs and never point back into the source Page.");
        Check(Hash(sourceIndex) == sourceHash, "The source Hyperlib content index remains byte-identical after catalog, imports, and saving templates.");
        var previews = new List<object>();
        var currentItems = await service.ListAsync(project, ct);
        var visualPage = currentItems.First(item => item.Id.StartsWith("page:visual:", StringComparison.Ordinal)).Id;
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            foreach (var id in new[] { "element:text", "element:studio-navigation", "element:studio-navigation-links", "section:header:simple-title", "object:" + preset.Id, visualPage }.Where(id => currentItems.Any(item => item.Id == id)))
            {
                var preview = await service.PreviewAsync(project, id, ct); using var response = await client.GetAsync(preview.Url, ct);
                var html = await response.Content.ReadAsStringAsync(ct);
                Check(response.IsSuccessStatusCode && html.Contains('<') && !html.Contains("data-preview-error", StringComparison.Ordinal), "The real " + id + " preview renders from owned or original component source.");
                previews.Add(new { id, preview.Url });
            }
        using (var hosts = new HyperlibWorkPreviewHostService())
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            var endpoint = await hosts.EnsureAsync(library, ct);
            using var runtimeResponse = await client.GetAsync(new Uri(endpoint, "api/runtime/" + pipeline.WorkId), ct);
            var runtimeText = await runtimeResponse.Content.ReadAsStringAsync(ct); using var runtime = JsonDocument.Parse(runtimeText);
            Check(runtimeResponse.IsSuccessStatusCode && runtime.RootElement.TryGetProperty("proxyPrefix", out var prefix) && !string.IsNullOrWhiteSpace(prefix.GetString()),
                "The actual Work runtime compiles the independent section adapters, multi-object Module, imported tool, complete Page, and saved Page clone together.");
            var route = new Uri(endpoint, runtime.RootElement.GetProperty("proxyPrefix").GetString()!.TrimStart('/') + "page/" + pageId + "?edit=1");
            var html = await client.GetStringAsync(route, ct);
            Check(html.Contains("A personally edited section heading", StringComparison.Ordinal) && html.Contains("data-field=\"part-0-text\"", StringComparison.Ordinal),
                "The real compiled Page displays the independently edited section field with its actual inline-editing binding.");
        }
        var result = "STUDIO_AUTHORING_LIBRARY_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-authoring-library-result.json"), JsonSerializer.Serialize(new { result, checks, project, library, pageId,
            dashboardId, savedCloneId, savedId, headerId, moduleId, previews }, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }
}
