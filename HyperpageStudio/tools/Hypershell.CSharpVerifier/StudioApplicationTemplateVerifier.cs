using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Exercises the shipped application catalog through its real AST, metadata, import, and content-store paths.</summary>
public static class StudioApplicationTemplateVerifier
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var fixture = Path.Combine(output, "ap-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fixture);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var sourceBefore = Snapshot(sourceLibraryRoot);
        var sourceApplicationFoldersBefore = ApplicationSourceFolders(sourceLibraryRoot);

        var additions = StudioApplicationPageTemplates.CreateTemplates()
            .Concat(StudioApplicationSectionTemplates.CreateTemplates())
            .Concat(StudioApplicationModuleTemplates.CreateTemplates()).ToArray();
        var allVisual = new VisualPageTemplateService(new VisualPageComponentCatalog()).BuiltInTemplates;
        Check(additions.Length == 90 && additions.Count(item => item.Scope == "page") == 10
            && additions.Count(item => item.Scope == "section") == 30 && additions.Count(item => item.Scope == "element") == 50,
            "The new collection contains exactly 10 Pages, 30 Sections and 50 Modules.");
        Check(additions.All(item => item.Library == "Application patterns" && item.StorageScope == "built-in"
            && item.TemplateId.StartsWith("app.", StringComparison.Ordinal))
            && additions.Select(item => item.TemplateId).Distinct(StringComparer.Ordinal).Count() == 90,
            "Every new pattern has a unique stable shipped identity in the Application patterns library.");
        Check(additions.All(item => allVisual.Count(available => available.TemplateId == item.TemplateId) == 1)
            && allVisual.Select(item => item.TemplateId).Distinct(StringComparer.Ordinal).Count() == allVisual.Count,
            "All 90 patterns are registered exactly once alongside the existing templates without ID collisions.");
        Check(additions.All(item => StudioTemplateRoleTaxonomy.Categories.Contains(item.Category)
            && item.CategoryGroup == item.Category && !string.IsNullOrWhiteSpace(item.Description)
            && item.Name.Length is > 3 and <= 160 && !item.Name.StartsWith("Atlas", StringComparison.OrdinalIgnoreCase)),
            "New labels describe functions with separate Page/Section/Module scale, functional categories, and no branding prefixes.");
        Check(new[] { "Navigation", "Media", "Grid", "Data", "Graphs", "Forms", "Content", "Search", "Commerce", "People", "Feedback", "Workflow" }
                .All(category => additions.Any(item => item.Category == category)),
            "The collection covers navigation, media, grids, data, graphs, forms, content, search, commerce, people, feedback and workflow jobs.");

        var components = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(components);
        var generator = new VisualPageGenerator(components, validator);
        var conversions = new Dictionary<string, HyperlibLegacyTemplateModule>(StringComparer.Ordinal);
        var generatedInventory = new List<object>();
        foreach (var template in additions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var root = VisualPageJson.Clone(template.Root);
                if (root.DescriptorId != "core.page") root = new() { DescriptorId = "core.page", Name = "Validation surface", Children = [root] };
                var page = new VisualPageDocument
                {
                    ProjectId = "11111111111111111111111111111111", PageId = "22222222222222222222222222222222",
                    Name = template.Name, Title = template.Name, Route = "/pattern", Slug = "pattern", ComponentName = "ApplicationPattern",
                    FileType = HyperpagePageFileType.Html, Root = root
                };
                validator.ThrowIfInvalid(page);
                var generated = generator.Generate(new() { ProjectId = page.ProjectId, Name = template.Name, RootNamespace = "ApplicationPatterns" }, page);
                if (generated.Artifacts.Count == 0 || !generated.Artifacts.Any(artifact => artifact.Content.Contains('<')))
                    throw new InvalidOperationException("The template produced no document markup.");
                var first = HyperlibLegacyTemplateLibraryService.ConvertForAuthoring(template);
                var second = HyperlibLegacyTemplateLibraryService.ConvertForAuthoring(template);
                if (first.Razor != second.Razor || first.Css != second.Css
                    || JsonSerializer.Serialize(first.Definition, ContentJson.Options) != JsonSerializer.Serialize(second.Definition, ContentJson.Options))
                    throw new InvalidOperationException("Portable conversion is not deterministic across fresh AST identities.");
                if (first.Definition.Fields.Count > 200 || first.Definition.Fields.Count == 0
                    || !first.Razor.Contains("data-vp-id", StringComparison.Ordinal))
                    throw new InvalidOperationException("The portable component lacks bounded editable fields or individual element identities.");
                conversions.Add(template.TemplateId, first);
                generatedInventory.Add(new
                {
                    id = template.TemplateId, template.Name, template.Scope, template.Category,
                    elements = Flatten(template.Root).Count(), fields = first.Definition.Fields.Count,
                    component = first.Definition.Component, razorSha256 = HashText(first.Razor), cssSha256 = HashText(first.Css),
                    generatedFiles = generated.Artifacts.Select(artifact => new { artifact.RelativePath, artifact.Sha256 }).ToArray()
                });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new InvalidOperationException("Application template '" + template.TemplateId + "' failed AST/generator/conversion validation: " + error.Message, error); }
        }
        Check(conversions.Count == 90, "Every new AST validates, generates real HTML and converts deterministically to an editable portable Hyperlib component.");

        var source = new HyperlibWorkProjectService(sourceLibraryRoot);
        var rawCatalog = source.GetCatalog();
        Check(rawCatalog.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == rawCatalog.Count,
            "The application group and previous source catalog retain globally unique object identities.");
        Check(rawCatalog.Count(item => IsApplicationGroup(item.GroupId) && item.Kind == "element") == 90,
            "The functional application groups contain exactly ninety source objects without duplicated template identities.");
        var cachedProjects = conversions.Values.Select(module => module.GroupId).Distinct(StringComparer.Ordinal)
            .ToDictionary(group => group, group => Path.GetFullPath(source.GetCatalogProjectRoot(group)), StringComparer.Ordinal);
        var cachedProject = cachedProjects.First().Value;
        var cacheBoundary = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "Tpl")) + Path.DirectorySeparatorChar;
        Check(cachedProjects.Values.All(path => path.StartsWith(cacheBoundary, StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith(Path.GetFullPath(sourceLibraryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(path, "authoring.template.json"))),
            "Every shipped functional application group is materialized in the private immutable cache, outside the source library.");
        Check(cachedProjects.Values.All(path => TemplateObjectLibrary.ReadDefinition(path).Blocks.Count <= 12),
            "Functional cache groups bound their component inventories rather than repeatedly cloning one monolithic ninety-template schema.");
        var cacheFilesBefore = cachedProjects.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value), StringComparer.Ordinal);
        var complete = new StudioCompleteTemplateService(sourceLibraryRoot).List();
        var completeNew = complete.Where(item => item.Id.StartsWith("visual:app.page.", StringComparison.Ordinal)).ToArray();
        Check(completeNew.Length == 10 && completeNew.All(item => item.Type == "Page" && item.Project is null
            && item.SupportedKinds.Contains(StudioProjectKind.Hyperlib) && item.SupportedKinds.Contains(StudioProjectKind.Razor) && item.SupportedKinds.Contains(StudioProjectKind.Html)
            && item.Category == additions.Single(template => "visual:" + template.TemplateId == item.Id).Category),
            "All ten complete Page choices expose the correct functional role and support Hyperlib, Razor and HTML with no invented Project attribution.");

        var project = await source.CreateAsync(fixture, "site", HyperlibWorkProjectService.BuildDirectEditingDefinition("Application pattern verification"), ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var pipeline = (await new HyperlibProjectPipelineService().ReadAsync(project, ct))!;
        var store = new ContentStore(library);
        var state = await store.ReadAsync(ct);
        var schema = TemplateObjectLibrary.ReadDefinition(project);
        var sentinel = new PageDocument
        {
            Title = "Preserved authored Page", TemplateId = schema.TemplateId, UsesEditableChrome = true,
            Blocks = [new() { Id = "application-sentinel-text", Type = "text", Region = "main", Values = new() { ["text"] = ContentJson.Value("Keep this existing authored Page byte-for-byte unchanged.") } }]
        };
        state = await store.ApplyAsync(new()
        {
            ExpectedRevision = state.Revision, Description = "Create private application catalog fixtures",
            Operations =
            [
                new() { Kind = "createNode", Id = "application-preserved", NodeType = "page", ParentId = pipeline.WorkId, Title = sentinel.Title, Page = sentinel },
                new() { Kind = "createNode", Id = "application-patterns", NodeType = "page", ParentId = pipeline.WorkId, Title = "Section and module samples",
                    Page = new() { Title = "Section and module samples", TemplateId = schema.TemplateId, UsesEditableChrome = true } }
            ]
        }, ct);
        var preservedPage = JsonSerializer.Serialize(state.Pages["application-preserved"], ContentJson.Options);
        using var service = new StudioAuthoringLibraryService(sourceLibraryRoot, Path.Combine(fixture, "c"));
        var visible = await service.ListAsync(project, ct);
        var includingAliases = await service.ListIncludingAliasesAsync(project, ct);
        var expectedRows = additions.ToDictionary(item => item.TemplateId, item => item.Scope == "page"
            ? "page:visual:" + item.TemplateId
            : "object:" + conversions[item.TemplateId].GroupId + ":element-legacy-" + Slug(item.TemplateId), StringComparer.Ordinal);
        var rows = additions.Select(template => visible.Single(item => item.Id == expectedRows[template.TemplateId])).ToArray();
        Check(rows.Length == 90 && rows.Count(item => item.Scope == "page") == 10 && rows.Count(item => item.Scope == "section") == 30
            && rows.Count(item => item.Scope == "module") == 50 && rows.All(item => item.AliasFor is null && item.Compatible && item.Project is null),
            "The live authoring library exposes exactly 90 canonical additions with real Page/Section/Module placement semantics and compatibility.");
        Check(visible.Count(item => item.Collection.StartsWith("Application patterns", StringComparison.Ordinal)) == 90,
            "The collection has exactly ninety visible additions and no accidental blank palette, typography or style templates.");
        Check(additions.All(template => rows.Single(item => item.Id == expectedRows[template.TemplateId]) is var item
            && item.Category == template.Category && item.Type == (template.Scope == "page" ? "Page" : template.Scope == "section" ? "Section" : "Module")),
            "Every sidebar row matches its actual scale and functional role, including media, grids, data and graphs.");
        Check(additions.Where(item => item.Scope == "page").All(template => includingAliases.Single(item => item.Id == "object:"
            + conversions[template.TemplateId].GroupId + ":element-legacy-" + Slug(template.TemplateId)).AliasFor == expectedRows[template.TemplateId]),
            "Complete-Page object aliases resolve to one canonical Page row instead of adding duplicate templates.");
        Check(visible.Where(item => item.Scope is "page" or "section" or "module").All(item => StudioTemplateRoleTaxonomy.Categories.Contains(item.Category)),
            "The entire existing and new template sidebar is categorized by application role rather than its former style labels.");

        var imported = new List<object>();
        var importedBlockIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pageCopies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var template in additions.OrderBy(item => item.Scope == "page" ? 0 : 1).ThenBy(item => item.TemplateId, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var change = await service.AddAsync(project, expectedRows[template.TemplateId], "application-patterns", "main", state.Revision,
                template.Scope == "page" ? "Sample · " + template.Name : null, pipeline.WorkId, ct,
                insertionY: template.Scope == "page" ? null : state.Pages["application-patterns"].Blocks.Count * 1100,
                targetRole: template.Scope == "page" ? "page" : null);
            state = change.State;
            if (change.AddedBlockIds.Count != 1 || !state.Pages[change.PageId].Blocks.Any(block => block.Id == change.AddedBlockIds[0]))
                throw new InvalidOperationException("The actual import did not commit one independent portable pattern for " + template.TemplateId);
            if (template.Scope == "page")
            {
                if (change.PageId is "application-patterns" or "application-preserved" || change.PageId == pipeline.LandingPageId)
                    throw new InvalidOperationException("A complete Page template overwrote an existing destination.");
                pageCopies.Add(template.TemplateId, change.PageId);
            }
            else if (change.PageId != "application-patterns") throw new InvalidOperationException("A Section or Module unexpectedly created a Page.");
            var block = state.Pages[change.PageId].Blocks.Single(item => item.Id == change.AddedBlockIds[0]);
            importedBlockIds.Add(template.TemplateId, block.Id);
            imported.Add(new { id = template.TemplateId, rowId = expectedRows[template.TemplateId], pageId = change.PageId, blockId = block.Id, type = block.Type });
        }
        Check(imported.Count == 90 && pageCopies.Count == 10 && state.Pages["application-patterns"].Blocks.Count == 80,
            "All 90 choices commit through the production Add to Page writer: ten independent Pages and eighty independently selectable Section/Module blocks.");
        Check(preservedPage == JsonSerializer.Serialize(state.Pages["application-preserved"], ContentJson.Options),
            "Importing the complete collection preserves the existing authored Page, its values and its block identity byte-for-byte.");
        schema = TemplateObjectLibrary.ReadDefinition(project);
        TemplateValidator.ValidateDefinition(schema);
        TemplateValidator.EnsureCompatible(schema, state.Pages.Values.Where(page => page.WorkId == pipeline.WorkId),
            state.Nodes.Where(node => node.WorkId == pipeline.WorkId).Select(node => node.Appearance));
        Check(schema.Blocks.Count <= 200 && state.Pages["application-patterns"].Blocks.All(block => schema.Blocks.Any(definition => definition.Id == block.Type)),
            "All new definitions coexist within one supported Work schema and every saved block has its actual declared component.");
        var componentFiles = Directory.EnumerateFiles(Path.Combine(project, "src", "Pages"), "*.razor", SearchOption.AllDirectories).ToArray();
        Check(conversions.Values.All(module => componentFiles.Any(file => File.ReadAllText(file).Contains("Source: " + module.SourceTemplateId + " (Application patterns)", StringComparison.Ordinal)
            && File.ReadAllText(file).Contains("data-vp-id", StringComparison.Ordinal))),
            "Every committed pattern has its owned generated Razor component with source provenance and individual canvas element identities.");

        var firstPageTemplate = additions.First(item => item.Scope == "page");
        var originalPageId = pageCopies[firstPageTemplate.TemplateId];
        var originalPageBefore = JsonSerializer.Serialize(state.Pages[originalPageId], ContentJson.Options);
        var repeated = await service.AddAsync(project, expectedRows[firstPageTemplate.TemplateId], "application-patterns", "main", state.Revision,
            "Independent repeat copy", pipeline.WorkId, ct, targetRole: "page");
        state = repeated.State;
        Check(repeated.PageId != originalPageId && state.Pages[repeated.PageId].Blocks.All(block => state.Pages[originalPageId].Blocks.All(old => old.Id != block.Id))
            && originalPageBefore == JsonSerializer.Serialize(state.Pages[originalPageId], ContentJson.Options),
            "Reusing a new complete Page creates fresh saved block identities without changing the previous copy.");
        var landing = await service.AddAsync(project, expectedRows[firstPageTemplate.TemplateId], "application-patterns", "main", state.Revision,
            "Application landing", pipeline.WorkId, ct, targetRole: "landing");
        state = landing.State;
        var landingNode = state.Nodes.Single(node => node.Id == landing.PageId);
        Check(landing.PageId == pipeline.LandingPageId && landingNode.ParentId == pipeline.WorkId
            && state.Pages[landing.PageId].Blocks.Count == 1
            && state.Nodes.Single(node => node.Id == pipeline.WorkId).Appearance["workLandingPage"].GetString() == landing.PageId
            && originalPageBefore == JsonSerializer.Serialize(state.Pages[originalPageId], ContentJson.Options),
            "A new complete Page can occupy the dedicated root Landing slot while its independent content Page remains unchanged.");
        var reloaded = await store.ReadAsync(ct);
        Check(JsonSerializer.Serialize(reloaded, ContentJson.Options) == JsonSerializer.Serialize(state, ContentJson.Options),
            "All imports, fresh Page copies, and the explicit Landing destination survive a direct content-store reload.");

        // One real Work renderer compiles the entire imported component set together. No mock preview markup is used.
        var renderPages = pageCopies.Select(pair => new RenderProbe(pair.Value, additions.Single(item => item.TemplateId == pair.Key).Name,
            ExpectedTexts(additions.Single(item => item.TemplateId == pair.Key)), [importedBlockIds[pair.Key]])).ToList();
        renderPages.Add(new(repeated.PageId, "Independent repeat copy", ExpectedTexts(firstPageTemplate), repeated.AddedBlockIds.ToArray()));
        renderPages.Add(new(landing.PageId, "Application landing", ExpectedTexts(firstPageTemplate), landing.AddedBlockIds.ToArray()));
        renderPages.Add(new("application-patterns", "Section and module samples",
            additions.Where(item => item.Scope != "page").SelectMany(ExpectedTexts).Distinct(StringComparer.Ordinal).ToArray(),
            additions.Where(item => item.Scope != "page").Select(item => importedBlockIds[item.TemplateId]).ToArray()));
        string runtimeUrl;
        object[] renderedPages;
        using (var hosts = new HyperlibWorkPreviewHostService())
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        {
            var endpoint = await hosts.EnsureAsync(library, ct);
            runtimeUrl = new Uri(endpoint, "api/runtime/" + Uri.EscapeDataString(pipeline.WorkId)).AbsoluteUri;
            using var runtimeResponse = await http.GetAsync(runtimeUrl, ct);
            var runtimeText = await runtimeResponse.Content.ReadAsStringAsync(ct);
            using var runtime = JsonDocument.Parse(runtimeText);
            Check(runtimeResponse.IsSuccessStatusCode && runtime.RootElement.TryGetProperty("proxyPrefix", out var prefix)
                && !string.IsNullOrWhiteSpace(prefix.GetString()),
                "The real Work runtime compiles all 90 imported application components together with the existing direct editor.");
            var proxyPrefix = runtime.RootElement.GetProperty("proxyPrefix").GetString()!;
            var rendered = new List<object>();
            foreach (var probe in renderPages)
            {
                var route = proxyPrefix.TrimStart('/') + "page/" + Uri.EscapeDataString(probe.Id) + "?edit=1";
                var url = new Uri(endpoint, route);
                using var response = await http.GetAsync(url, ct);
                var html = await response.Content.ReadAsStringAsync(ct);
                var decoded = WebUtility.HtmlDecode(html);
                if (!response.IsSuccessStatusCode || html.Contains("data-preview-error", StringComparison.Ordinal)
                    || !html.Contains("data-vp-id", StringComparison.Ordinal)
                    || !probe.BlockIds.All(id => html.Contains("data-block-id=\"" + id + "\"", StringComparison.Ordinal))
                    || !probe.ExpectedTexts.All(text => decoded.Contains(text, StringComparison.Ordinal)))
                    throw new InvalidOperationException("The real rendered application Page '" + probe.Title + "' did not display every expected independent block and its actual authored text.");
                rendered.Add(new { id = probe.Id, title = probe.Title, url = url.AbsoluteUri, route,
                    expectedTexts = probe.ExpectedTexts, blockIds = probe.BlockIds, htmlSha256 = HashText(html) });
            }
            renderedPages = rendered.ToArray();
            Check(renderedPages.Length == 13,
                "All ten Page designs, a fresh duplicate, the dedicated Landing Page and the eighty-pattern canvas render actual authored content without preview errors.");
            await File.WriteAllTextAsync(Path.Combine(fixture, "application-template-render-config.json"), JsonSerializer.Serialize(new
            {
                project, library, workId = pipeline.WorkId, runtimeUrl, endpoint = endpoint.AbsoluteUri,
                canvasId = "application-patterns", canvasUrl = new Uri(endpoint, proxyPrefix.TrimStart('/') + "page/application-patterns?edit=1").AbsoluteUri,
                pages = renderedPages
            }, Json), ct);
        }
        Check(cachedProjects.All(pair => Equal(cacheFilesBefore[pair.Key], Snapshot(pair.Value))),
            "Importing every pattern copies owned components without modifying any immutable functional source cache.");
        Check(sourceApplicationFoldersBefore.SequenceEqual(ApplicationSourceFolders(sourceLibraryRoot), StringComparer.OrdinalIgnoreCase)
            && Equal(sourceBefore, Snapshot(sourceLibraryRoot)),
            "Catalog reads and all imports leave the original source files byte-identical and never seed the new group into the user's library.");

        var result = "STUDIO_APPLICATION_TEMPLATES_OK|checks=" + checks.Count + "|templates=90|pages=10|sections=30|modules=50";
        var inventoryPath = Path.Combine(fixture, "application-template-inventory.json");
        await File.WriteAllTextAsync(inventoryPath, JsonSerializer.Serialize(generatedInventory, Json), ct);
        await File.WriteAllTextAsync(Path.Combine(output, "studio-application-template-result.json"), JsonSerializer.Serialize(new
        {
            result, passed = true, checks, fixture, project, library, pageId = "application-patterns", landingId = landing.PageId,
            projectRoot = project, workId = pipeline.WorkId, canvasPageId = "application-patterns",
            pages = additions.Where(template => template.Scope == "page").Select(template => new
            {
                templateId = template.TemplateId, pageId = pageCopies[template.TemplateId], title = template.Name,
                blockIds = new[] { importedBlockIds[template.TemplateId] }, expectedTexts = ExpectedTexts(template)
            }).ToArray(),
            components = additions.Where(template => template.Scope != "page").Select(template => new
            {
                templateId = template.TemplateId, scope = template.Scope == "element" ? "module" : template.Scope, title = template.Name,
                blockId = importedBlockIds[template.TemplateId], expectedTexts = ExpectedTexts(template),
                component = schema.Blocks.Single(definition => definition.Id == state.Pages["application-patterns"].Blocks
                    .Single(block => block.Id == importedBlockIds[template.TemplateId]).Type).Component
            }).ToArray(),
            cachedProject, cachedProjects, inventory = inventoryPath, imported, completePages = pageCopies, rows, definitions = schema.Blocks.Count,
            runtimeUrl, renderedPages, renderConfiguration = Path.Combine(fixture, "application-template-render-config.json"),
            originalSourcePreserved = true, privateCachePreserved = true, sourceBeforeCount = sourceBefore.Count
        }, Json), ct);
        return result + "|fixture=" + fixture;
    }

    private static string Slug(string id) => Regex.Replace(id, "[^a-zA-Z0-9]+", "-").ToLowerInvariant().Trim('-');
    private static bool IsApplicationGroup(string group) => group.StartsWith(HyperlibWorkProjectService.ApplicationTemplateGroup + "-", StringComparison.Ordinal);
    private static string[] ApplicationSourceFolders(string sourceRoot)
        => new[] { Path.Combine(sourceRoot, "CoreLibrary", "DesignGroups"), Path.Combine(sourceRoot, ".hyperlib", "catalog-seeds", "legacy-v1") }
            .Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories)
            .Where(path => Path.GetFileName(path).StartsWith(HyperlibWorkProjectService.ApplicationTemplateGroup, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    private sealed record RenderProbe(string Id, string Title, string[] ExpectedTexts, string[] BlockIds);
    private static string[] ExpectedTexts(VisualPageTemplateDocument template) => Flatten(template.Root)
        .Where(element => !string.IsNullOrWhiteSpace(element.Text) && element.Text.Length >= 8 && element.Text.Length <= 180
            && !element.Text.Contains('\n') && !element.Text.Contains('\r'))
        .Select(element => element.Text).Distinct(StringComparer.Ordinal).Take(3).ToArray();
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement element)
    {
        yield return element;
        foreach (var child in element.Children) foreach (var descendant in Flatten(child)) yield return descendant;
    }
    private static Dictionary<string, string> Snapshot(string root)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".hyperlib", "bin", "obj", "app", "node_modules", "artifacts", "output", ".vs" };
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Read(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory)) files.Add(Path.GetRelativePath(root, file), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
            foreach (var child in Directory.EnumerateDirectories(directory).Where(path => !ignored.Contains(Path.GetFileName(path)))) Read(child);
        }
        Read(root);
        return files;
    }
    private static bool Equal(Dictionary<string, string> first, Dictionary<string, string> second)
        => first.Count == second.Count && first.All(pair => second.GetValueOrDefault(pair.Key) == pair.Value);
}
