using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Audits presentation metadata against the complete, original source catalogue without starting a renderer.</summary>
public static class StudioTemplateCatalogVerifier
{
    private sealed record Original(string Id, string Title, string Description, string Scope, string Category, string Collection, string Kind);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var sourceBefore = Snapshot(sourceLibraryRoot);
        var project = await new HyperlibWorkProjectService(sourceLibraryRoot).CreateAsync(Path.Combine(output, "catalog-" + Guid.NewGuid().ToString("N")[..8]),
            "Catalog naming fixture", HyperlibWorkProjectService.BuildDirectEditingDefinition("Catalog naming fixture"), ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!;
        using var service = new StudioAuthoringLibraryService(sourceLibraryRoot, Path.Combine(output, "c"));
        var original = Originals(sourceLibraryRoot, project).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var allItems = (await service.ListIncludingAliasesAsync(project, ct)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var items = (await service.ListAsync(project, ct)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output, "catalog-before.json"), JsonSerializer.Serialize(original, Json), ct);
        await File.WriteAllTextAsync(Path.Combine(output, "catalog-after.json"), JsonSerializer.Serialize(allItems, Json), ct);
        Check(allItems.Length > 900 && allItems.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == allItems.Length,
            "The full catalogue retains unique stable IDs for every original template and element.");
        Check(original.Select(item => item.Id).SequenceEqual(allItems.Select(item => item.Id)),
            "The source-derived before inventory and displayed after inventory contain exactly the same IDs; no object was lost or invented.");
        Check(original.Zip(allItems).All(pair => pair.First.Title == pair.Second.SourceTitle && pair.First.Description == pair.Second.Description
            && pair.First.Collection == pair.Second.Collection
            && pair.First.Kind == pair.Second.Kind && pair.Second.Compatible && pair.Second.Reason is null),
            "Every stable ID preserves its original title, description, collection, kind and compatibility while display categories are reorganized by application role.");
        var expectedScopes = ExpectedScopes(sourceLibraryRoot, original);
        Check(allItems.All(item => item.Scope == expectedScopes[item.Id]),
            "Every source object is classified by its original Page/Section structure; styled reusable objects are Modules and native primitives remain Elements.");
        Check(allItems.All(item => item.Type == (item.Scope switch { "page" => "Page", "section" => "Section", "module" => "Module", _ => "Element" })),
            "Type identifies the actual Page, Section, Module or Element placement semantics of every row.");
        Check(items.All(item => !string.IsNullOrWhiteSpace(item.Title) && item.Title == item.Title.Trim()
            && !item.Title.Any(char.IsControl) && item.Title.Length <= 160),
            "Every displayed name is a printable, trimmed, nonempty label of usable length.");
        Check(items.All(item => item.AliasFor is null) && allItems.Where(item => item.AliasFor is not null)
            .All(item => items.Any(canonical => canonical.Id == item.AliasFor && canonical.Title == item.Title && canonical.Project == item.Project)),
            "Hidden duplicate aliases retain their old IDs and resolve to one visible canonical source template with the same name and project.");
        var collisionSeeds = new[] { new StudioTemplateMetadata("c", "Header", "Section", null), new StudioTemplateMetadata("a", "Header", "Section", null),
            new StudioTemplateMetadata("b", "Header", "Section", null), new StudioTemplateMetadata("d", "Header", "Page", null),
            new StudioTemplateMetadata("e", "Header", "Section", "Atlas"), new StudioTemplateMetadata("f", "Header", "Section", "Other project") };
        var numbered = StudioTemplateMetadataService.AssignNames(collisionSeeds);
        Check(numbered.Single(item => item.Id == "a").Name == "Header" && numbered.Single(item => item.Id == "b").Name == "Header 2"
            && numbered.Single(item => item.Id == "c").Name == "Header 3" && numbered.Where(item => item.Id is "d" or "e" or "f").All(item => item.Name == "Header")
            && JsonSerializer.Serialize(numbered, Json) == JsonSerializer.Serialize(StudioTemplateMetadataService.AssignNames(collisionSeeds.Reverse()), Json),
            "True name collisions use stable source-ID order with contiguous numbers, independent of enumeration order, Type and Project.");
        var reservedSeeds = new[] { new StudioTemplateMetadata("a", "Header", "Section", null), new StudioTemplateMetadata("b", "Header", "Section", null),
            new StudioTemplateMetadata("c", "Header 2", "Section", null) };
        var reserved = StudioTemplateMetadataService.AssignNames(reservedSeeds);
        Check(reserved.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3
            && reserved.Single(item => item.Id == "b").Name == "Header 3" && reserved.Single(item => item.Id == "c").Name == "Header 2"
            && JsonSerializer.Serialize(reserved, Json) == JsonSerializer.Serialize(StudioTemplateMetadataService.AssignNames(reservedSeeds.Reverse()), Json),
            "A naturally numbered source name is reserved so generated collision suffixes never duplicate it.");
        var templates = items.Where(item => item.Scope is "page" or "section" or "module").ToArray();
        Check(templates.All(item => StudioTemplateRoleTaxonomy.Categories.Contains(item.Category)),
            "Every current template uses an application-role category, including the original catalog and saved layouts.");
        Check(templates.Any(item => item.Title == "Biography" && item.Category == "People"),
            "Biography is a People template, rather than a substring match for a graph.");
        Check(templates.Where(item => IsCurated(item.Id)).GroupBy(item => (Name: item.Title.ToUpperInvariant(), item.Type, item.Project)).All(group => group.Count() == 1),
            "Every visible curated Name, Type and Project tuple is unique after true aliases are hidden.");
        Check(templates.Where(item => IsCurated(item.Id)).All(item => !Regex.IsMatch(item.Title, @"^(?:Atlas(?:\s+Archive|\s+Universalis)?|Hyperlib|Hyperpage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            "Built-in template names omit Atlas, Hyperlib and Hyperpage branding prefixes.");
        Check(templates.Any(item => item.Project == "Atlas") && templates.Any(item => item.Project is null),
            "The catalogue distinguishes the real Atlas project from templates with no project attribution.");
        var atlasGroups = new HashSet<string>(StringComparer.Ordinal) { "atlas-admin-interface", "atlas-public-archives", "atlas-website-bio", "atlas-website-home",
            "atlas-website-projects", "atlas-website-services", "atlas-website-tools" };
        Check(templates.Where(item => atlasGroups.Any(group => item.Id == "page:hyperlib:" + group || item.Id.StartsWith("page:hyperlib:" + group + "/", StringComparison.Ordinal)
            || item.Id.StartsWith("object:" + group + ":", StringComparison.Ordinal)) || item.Id.StartsWith("page:visual:atlas.", StringComparison.Ordinal))
            .All(item => item.Project == "Atlas"), "Every known imported Atlas project template retains Atlas in its separate Project metadata.");
        Check(items.Where(item => item.Id.Contains("travel-atlas", StringComparison.Ordinal)).All(item => item.Project is null),
            "Travel Atlas source identity does not fabricate an Atlas project attribution.");
        using (var fresh = new StudioAuthoringLibraryService(sourceLibraryRoot, Path.Combine(output, "fresh")))
        {
            var again = (await fresh.ListAsync(project, ct)).OrderBy(item => item.Id, StringComparer.Ordinal);
            Check(JsonSerializer.Serialize(items, Json) == JsonSerializer.Serialize(again, Json),
                "Names, numbered collisions and project metadata are deterministic across fresh service instances.");
        }
        var complete = new StudioCompleteTemplateService(sourceLibraryRoot).List().Where(item => item.SupportedKinds.Contains(StudioProjectKind.Hyperlib)).ToArray();
        Check(complete.All(choice => items.Single(item => item.Id == "page:" + choice.Id) is var row && row.Title == choice.Title
            && row.SourceTitle == choice.SourceTitle && row.Type == choice.Type && row.Project == choice.Project),
            "The complete Page gallery and authoring sidebar use identical names and project metadata for the same stable IDs.");
        var aliases = items.Where(item => item.Id.StartsWith("page:hyperlib:", StringComparison.Ordinal) && !item.Id[14..].Contains('/'))
            .Select(item => (Page: item, Object: allItems.SingleOrDefault(other => other.Id == "object:" + item.Id[14..] + ":page-layout")))
            .Where(pair => pair.Object is not null).ToArray();
        Check(aliases.Length > 0 && aliases.All(pair => pair.Page.Title == pair.Object!.Title && pair.Page.Project == pair.Object.Project),
            "Page-layout aliases retain the same source-derived display name and project instead of acquiring duplicate numbers.");
        var readBefore = Snapshot(library);
        _ = await service.ListAsync(project, ct);
        Check(Equal(readBefore, Snapshot(library)), "Reading and filtering metadata changes no owned Work, source, content or saved-template file.");

        // Exercise the existing ID-based writer and save path with a deliberately brand-like user title.
        var store = new ContentStore(library); var state = await store.ReadAsync(ct);
        var added = await service.AddAsync(project, "element:text", pipeline.LandingPageId, "main", state.Revision, ct: ct);
        var textId = added.AddedBlockIds.Single(); state = added.State;
        var authored = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        authored.Blocks.Single(block => block.Id == textId).Values["text"] = ContentJson.Value("Preserved authored template content.");
        authored.Sources.Add(new() { Id = "catalog-source", Title = "Original citation", Url = "https://example.com/catalog", RetrievedAt = DateTimeOffset.UtcNow, BlockIds = [textId] });
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author the private naming fixture", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        const string aliasId = "object:insight-dashboard:page-layout";
        var aliasRow = allItems.Single(item => item.Id == aliasId);
        Check(aliasRow.AliasFor == "page:hyperlib:insight-dashboard" && !items.Any(item => item.Id == aliasId),
            "The legacy Dashboard object ID is retained as an explicit alias of one visible Page template.");
        var aliasSource = new HyperlibWorkProjectService(sourceLibraryRoot).GetCatalogObject("insight-dashboard", "page-layout");
        var aliasCopy = await service.AddAsync(project, aliasId, pipeline.LandingPageId, "main", state.Revision, "Legacy alias copy", ct: ct);
        state = aliasCopy.State;
        Check(aliasCopy.PageId != pipeline.LandingPageId && state.Pages[aliasCopy.PageId].Blocks.Count == aliasSource.Blocks.Count
            && state.Pages[aliasCopy.PageId].Blocks.Zip(aliasSource.Blocks).All(pair => pair.Second.Values.All(field => pair.First.Values.TryGetValue(field.Key, out var value) && value.GetRawText() == field.Value.GetRawText()))
            && JsonSerializer.Serialize(state.Pages[pipeline.LandingPageId].Blocks, ContentJson.Options) == JsonSerializer.Serialize(authored.Blocks, ContentJson.Options),
            "Adding a hidden legacy alias by its original ID still copies its exact authored values into a new independent Page and preserves the current Page.");
        const string savedTitle = "Atlas custom research Page";
        var saved = await service.SavePageTemplateAsync(project, pipeline.LandingPageId, savedTitle, state.Revision, ct);
        var savedRow = (await service.ListAsync(project, ct)).Single(item => item.Id == saved.Item.Id);
        Check(saved.Item.Title == savedTitle && savedRow.Title == savedTitle && savedRow.SourceTitle == savedTitle && savedRow.Type == "Page",
            "The user's exact custom saved title survives save and catalog refresh, including a brand-like word.");
        Check(savedRow.Project == "Catalog naming fixture", "A newly saved template records the actual source Work as its Project.");
        var copy = await service.AddAsync(project, savedRow.Id, pipeline.LandingPageId, "main", state.Revision, "Independent catalog copy", ct: ct);
        var clone = copy.State.Pages[copy.PageId]; var originalPage = copy.State.Pages[pipeline.LandingPageId];
        Check(copy.PageId != pipeline.LandingPageId && clone.Blocks.Count == authored.Blocks.Count
            && clone.Blocks.All(block => authored.Blocks.All(source => source.Id != block.Id))
            && clone.Blocks.Any(block => block.Values.TryGetValue("text", out var value) && value.GetString() == "Preserved authored template content.")
            && clone.Sources.Single().Title == "Original citation" && clone.Sources.Single().BlockIds.All(id => clone.Blocks.Any(block => block.Id == id))
            && JsonSerializer.Serialize(originalPage.Blocks, ContentJson.Options) == JsonSerializer.Serialize(authored.Blocks, ContentJson.Options),
            "Adding the saved template by its unchanged ID copies actual content and citation provenance into independent block IDs.");
        Check(Equal(sourceBefore, Snapshot(sourceLibraryRoot)), "All source-library assets, schemas, templates, projects and content remain byte-identical after the complete naming audit.");
        var inventory = original.Zip(allItems).Select(pair => new { id = pair.Second.Id, before = pair.First, after = pair.Second }).ToArray();
        var result = "STUDIO_TEMPLATE_CATALOG_OK|checks=" + checks.Count + "|items=" + items.Length + "|templates=" + templates.Length;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-template-catalog-result.json"), JsonSerializer.Serialize(new
        { result, checks, project, library, originalCount = original.Length, retainedCount = allItems.Length, displayedCount = items.Length, templates = templates.Length,
            projectCounts = templates.GroupBy(item => item.Project ?? "No project").ToDictionary(group => group.Key, group => group.Count()),
            sourceFileCount = sourceBefore.Count, savedId = savedRow.Id, savedRow, aliases = aliases.Select(pair => new { pageId = pair.Page.Id, objectId = pair.Object!.Id, name = pair.Page.Title }), inventory }, Json), ct);
        await File.WriteAllTextAsync(Path.Combine(output, "source-file-hashes.json"), JsonSerializer.Serialize(sourceBefore, Json), ct);
        return result;
    }

    private static bool IsCurated(string id)
    {
        if (id.StartsWith("section:", StringComparison.Ordinal) || id.StartsWith("page:visual:", StringComparison.Ordinal)) return true;
        var source = id.StartsWith("page:hyperlib:", StringComparison.Ordinal) ? id[14..].Split('/')[0]
            : id.StartsWith("object:", StringComparison.Ordinal) ? id[7..].Split(':')[0] : "";
        return source.Length > 0 && (ReusableTemplateCatalog.IsBuiltIn(source) || source.StartsWith("foundation-", StringComparison.Ordinal)
            || source.StartsWith("legacy-", StringComparison.Ordinal) || source.StartsWith("atlas-", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ExpectedScopes(string source, IEnumerable<Original> original)
    {
        var expected = original.ToDictionary(item => item.Id, item => item.Scope, StringComparer.Ordinal);
        var visuals = new VisualPageTemplateService(new VisualPageComponentCatalog()).BuiltInTemplates.ToDictionary(item => item.TemplateId, StringComparer.Ordinal);
        var service = new HyperlibWorkProjectService(source);
        foreach (var choice in service.GetCatalog().Where(item => item.Kind == "element"))
        {
            var sourceId = service.GetCatalogObject(choice.GroupId, choice.ObjectId).Element?.SourceTemplateId;
            expected["object:" + choice.Id] = sourceId is not null && visuals.TryGetValue(sourceId, out var visual)
                ? visual.Scope == "element" ? "module" : visual.Scope : "module";
        }
        return expected;
    }

    private static IReadOnlyList<Original> Originals(string source, string project)
    {
        var result = new List<Original>(); var service = new HyperlibWorkProjectService(source); var choices = service.GetCatalog();
        foreach (var visual in new VisualPageTemplateService(new VisualPageComponentCatalog()).BuiltInTemplates.Where(item => item.Scope == "page" && item.StorageScope == "built-in"))
            result.Add(new("page:visual:" + visual.TemplateId, visual.Name, visual.Description, "page", visual.Category, visual.Library, "layout"));
        var originals = ReusableTemplateCatalog.List(source);
        foreach (var choice in choices.Where(item => item.Kind == "layout" && item.GroupId != ReusableTemplateCatalog.BlankId))
        {
            if (service.GetCatalogObject(choice.GroupId, choice.ObjectId).Blocks.Count == 0) continue;
            var original = originals.SingleOrDefault(item => item.Id == choice.GroupId);
            result.Add(new("page:hyperlib:" + choice.GroupId + (choice.ObjectId == "page-layout" ? "" : "/" + choice.ObjectId),
                choice.ObjectId == "page-layout" ? choice.GroupTitle : choice.Title, choice.Description, "page", original?.Category ?? choice.Category,
                original is null ? "Core Template Library" : original.IsBuiltIn ? "Hyperlib collection" : "My templates", "layout"));
        }
        foreach (var (section, presets) in new[] { ("header", HyperlibHeaderLayouts.All), ("navigation", HyperlibNavigationLayouts.All), ("body", HyperlibBodyLayouts.All), ("footer", HyperlibFooterLayouts.All) })
            foreach (var preset in presets) result.Add(new("section:" + section + ":" + preset.Id, preset.Title, preset.Description, "section", char.ToUpperInvariant(section[0]) + section[1..], "Studio sections", section));
        foreach (var choice in choices.Where(item => item.Kind != "region"))
            result.Add(new("object:" + choice.Id, choice.Title, choice.Description,
                choice.Kind == "element" ? "element" : choice.Kind == "layout" && choice.ObjectId == "page-layout" ? "page" : "module",
                choice.Category, choice.GroupTitle, choice.Kind is "palette" or "typography" or "style" ? choice.Kind : Kind(choice.ComponentName)));
        foreach (var block in TemplateObjectLibrary.ReadDefinition(project).Blocks.Where(block => block.Id is not ("studio-header-row" or "studio-body-row" or "studio-footer-row" or "studio-navigation-layout")))
            result.Add(new("element:" + block.Id, block.Title, block.Description ?? "An editable native Hyperlib object.", "element", block.Category ?? "Essentials", "Essentials", Kind(block.Component)));
        return result;
    }
    private static string Kind(string component) => component.Split('.').Last() switch
    { "TextBlock" or "RichTextBlock" or "CitationBlock" => "text", "ImageBlock" or "GalleryBlock" => "image", "VideoBlock" => "video", "AudioBlock" => "audio",
        "HeaderBlock" or "FooterBlock" or "BannerBlock" => "layout", "WorkNavigationBlock" or "StudioWorkNavigation" or "StudioNavigationLinksBlock" or "StudioNavigationLayoutBlock" => "navigation", "" => "layout", _ => "tool" };
    private static Dictionary<string, string> Snapshot(string root)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".hyperlib", "bin", "obj", "app", "node_modules", "artifacts", "output", ".vs" };
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Read(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory)) files.Add(Path.GetRelativePath(root, file), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
            foreach (var child in Directory.EnumerateDirectories(directory).Where(path => !ignored.Contains(Path.GetFileName(path)))) Read(child);
        }
        Read(root); return files;
    }
    private static bool Equal(Dictionary<string, string> first, Dictionary<string, string> second) => first.Count == second.Count && first.All(pair => second.GetValueOrDefault(pair.Key) == pair.Value);
}
