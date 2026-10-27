using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed partial class HyperlibWorkProjectService
{
    /// <summary>Revisits shopping and staging while retaining all existing component source and authored content.</summary>
    public async Task<HyperlibProjectPipelineState> UpdateFoundationAsync(string projectRoot, HyperlibWorkProjectDefinition definition,
        long expectedPipelineRevision, long expectedContentRevision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition = ContentJson.Clone(definition);
        IncludeHeaderToolset(definition);
        if (definition.PipelineVersion != 2 || definition.SelectedToolsetIds is null || definition.SelectedToolsetIds.Count > 180)
            throw new InvalidDataException("Choose at most 180 objects for the project toolkit.");
        var selectedIds = definition.SelectedToolsetIds.Distinct(StringComparer.Ordinal).ToArray();
        var service = new HyperlibProjectPipelineService();
        var savedPipeline = await service.ReadAsync(projectRoot, ct) ?? throw new InvalidDataException("This project has no staged templates.");
        var owned = savedPipeline.Toolkit.ToDictionary(item => item.ChoiceId, StringComparer.Ordinal);
        // A portable project can restage or narrow its owned toolkit even after its original collection is gone.
        // Only adding an unowned choice needs access to an external source catalogue.
        var catalogue = selectedIds.Any(id => !owned.ContainsKey(id)) ? GetCatalog() : [];
        var sourceItems = selectedIds.Select(id => owned.TryGetValue(id, out var item)
            ? new HyperlibWorkTemplateChoice(id, item.Title, item.Description, item.Category, item.Kind,
                ParseChoice(id).GroupId, ParseChoice(id).ObjectId, item.GroupTitle, "")
            : catalogue.SingleOrDefault(item => item.Id == id)
                ?? throw new InvalidDataException("An object is no longer in the template collection. Refresh it before changing the project's toolkit.")).ToArray();
        var validationToolkit = sourceItems.Select(item => new HyperlibProjectToolkitItem { ChoiceId = item.Id, Kind = item.Kind }).ToArray();
        if (savedPipeline.Revision != expectedPipelineRevision) throw new ConflictException(expectedPipelineRevision, savedPipeline.Revision);
        var currentState = await new ContentStore(Path.GetFullPath(Path.Combine(projectRoot, "../.."))).ReadAsync(ct);
        if (currentState.Revision != expectedContentRevision) throw new ConflictException(expectedContentRevision, currentState.Revision);
        ValidateOwnedPipelineUpgrade(projectRoot);
        definition = ContentJson.Clone(definition);
        await ImportRecipeLogosAsync(projectRoot, definition, ct);
        var namedRecipes = HyperlibProjectPipelineService.NamedTemplates(definition);
        HyperlibProjectPipelineService.ValidateRecipe(definition.LandingTemplate, validationToolkit);
        foreach (var recipe in namedRecipes) HyperlibProjectPipelineService.ValidateRecipe(recipe.Definition, validationToolkit);
        var result = await service.UpdatePreparedTemplatesAsync(projectRoot, new() { ExpectedPipelineRevision = expectedPipelineRevision,
            ExpectedContentRevision = expectedContentRevision, LandingTemplate = definition.LandingTemplate, PageTemplate = definition.PageTemplate,
            PageTemplates = namedRecipes, DefaultPageTemplateId = definition.DefaultPageTemplateId },
            async (pipeline, template, cancellationToken) =>
            {
                var libraryRoot = Path.GetFullPath(Path.Combine(projectRoot, "../.."));
                var existing = pipeline.Toolkit.ToDictionary(item => item.ChoiceId, StringComparer.Ordinal);
                var nextToolkit = new List<HyperlibProjectToolkitItem>();
                foreach (var choice in sourceItems)
                {
                    if (existing.TryGetValue(choice.Id, out var retained))
                    { if (retained.SourceBlockDefinitions.Count == 0) retained.SourceBlockDefinitions = HeaderSourceDefinitions(retained.Kind, retained.BlockTypeIds, retained.LayoutBlocks, template);
                        nextToolkit.Add(retained); continue; }
                    var privateGroup = Within(libraryRoot, "CoreLibrary/DesignGroups/" + choice.GroupId);
                    if (!Directory.Exists(privateGroup))
                    {
                        var sourceGroup = Path.Combine(CatalogLibraryRoot(choice.GroupId), "CoreLibrary", "DesignGroups", choice.GroupId);
                        if (Directory.Exists(sourceGroup)) await CopyDirectoryAsync(sourceGroup, privateGroup, cancellationToken);
                        else
                        {
                            var starter = ReusableTemplateCatalog.Find(_sourceRoot, choice.GroupId);
                            if (!starter.IsBuiltIn && !Directory.Exists(Path.Combine(libraryRoot, "Templates", choice.GroupId)))
                                await CopyDirectoryAsync(Path.Combine(_sourceRoot, "Templates", choice.GroupId), Path.Combine(libraryRoot, "Templates", choice.GroupId), cancellationToken);
                            var sourceProject = await ReusableTemplateManager.OpenProjectAsync(libraryRoot, choice.GroupId, cancellationToken);
                            await TemplateObjectLibrary.RegisterDesignGroupAsync(libraryRoot, sourceProject, starter.Id, starter.Title, starter.Description,
                                starter.Category, starter.Id, cancellationToken);
                        }
                    }
                    var sourceObject = TemplateObjectLibrary.ListObjects(libraryRoot, choice.GroupId).SingleOrDefault(item => item.Id == choice.ObjectId)
                        ?? throw new InvalidDataException("This project owns an earlier copy of that Design Group. Save the updated group under a new name before adding its new objects.");
                    var priorIds = template.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
                    var templatePath = HyperlibProjectPipelineService.Guard(projectRoot, "authoring.template.json");
                    var raw = await File.ReadAllBytesAsync(templatePath, cancellationToken);
                    var plan = await TemplateObjectImporter.PrepareAsync(libraryRoot, projectRoot, template, [ParseChoice(choice.Id)], ct: cancellationToken);
                    var types = await ImportedTypesAsync(libraryRoot, projectRoot, template, plan.Definition, choice, cancellationToken);
                    await TemplateObjectImporter.CommitAsync(projectRoot, plan.Definition, [plan], Convert.ToHexStringLower(SHA256.HashData(raw)), cancellationToken);
                    template = plan.Definition;
                    var layoutBlocks = choice.Kind == "layout" ? ContentJson.Clone(template.DefaultBlocks) : [];
                    if (choice.Kind == "layout") types = layoutBlocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToList();
                    nextToolkit.Add(new() { ChoiceId = choice.Id, Title = choice.Title, Description = choice.Description, Kind = choice.Kind,
                        Category = choice.Category, GroupTitle = choice.GroupTitle, BlockTypeIds = types, LayoutBlocks = layoutBlocks,
                        LayoutRegions = choice.Kind == "layout" ? ContentJson.Clone(template.Regions) : [],
                        SourceBlockDefinitions = HeaderSourceDefinitions(choice.Kind, types, layoutBlocks, template),
                        LayoutAppearance = sourceObject.Appearance.Where(field => field.DefaultValue.HasValue)
                            .ToDictionary(field => field.Id, field => template.Appearance.Last(item => item.Id == field.Id).DefaultValue!.Value.Clone(), StringComparer.Ordinal) });
                }
                // Removing a shop selection never removes schema/source that existing content references.
                if (!HeaderRecipes(definition).Any(recipe => !string.IsNullOrWhiteSpace(recipe.CompleteTemplateObjectId)))
                {
                    var neededRegions = currentState.Pages.Values.Where(page => page.WorkId == pipeline.WorkId)
                        .SelectMany(page => page.Blocks).Select(block => block.Region).ToHashSet(StringComparer.Ordinal);
                    var retainedRegions = template.Regions.Where(region => neededRegions.Contains(region.Id)
                        && region.Id is not ("header" or "sidebar" or "main" or "footer")).Select(region => ContentJson.Clone(region)).ToList();
                    template.Regions = [new() { Id = "header", Title = "Header" }, new() { Id = "sidebar", Title = "Side navigation" },
                        new() { Id = "main", Title = "Page content" }, new() { Id = "footer", Title = "Footer" }];
                    template.Regions.AddRange(retainedRegions);
                }
                template.DefaultBlocks.Clear();
                template.EmptyPageDefault = true;
                pipeline.SelectedToolsetIds = selectedIds.ToList();
                pipeline.Toolkit = nextToolkit;
                await ImportHeaderTemplateValuesAsync(projectRoot, definition, nextToolkit, template, cancellationToken);
                HyperlibProjectPipelineService.EnsureDirectEditorFields(template);
                await CoalesceGeneratedSharedSupportAsync(projectRoot, cancellationToken);
                if (sourceItems.Any(item => !owned.ContainsKey(item.Id))) pipeline.SourceLibraryRoot = _sourceRoot;
                await DisableOwnedProjectCompressionAsync(projectRoot, cancellationToken);
                return template;
            }, ct);
        await UpgradeOwnedPipelineAsync(projectRoot, ct);
        return result;
    }

    private async Task<string> CreatePipelineAsync(string parentDirectory, string name, HyperlibWorkProjectDefinition request, CancellationToken ct)
    {
        ValidateFolderName(name);
        ValidateTitle(request.WorkTitle);
        request = ContentJson.Clone(request);
        IncludeHeaderToolset(request);
        if (request.SelectedToolsetIds is null || request.SelectedToolsetIds.Count > 180)
            throw new InvalidDataException("Choose at most 180 objects for this project's toolkit.");
        var selectedIds = request.SelectedToolsetIds.Distinct(StringComparer.Ordinal).ToArray();
        var catalogue = GetCatalog();
        var selected = selectedIds.Select(id => catalogue.SingleOrDefault(item => item.Id == id)
            ?? throw new InvalidDataException("An object is no longer in the template collection. Refresh the collection and choose it again.")).ToArray();
        foreach (var id in selectedIds) _ = ParseChoice(id);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));
        RejectLinks(parent);
        var destination = Within(parent, name);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("A project already exists at this location. Choose a new folder name.");
        if (SamePath(destination, _sourceRoot) || destination.StartsWith(_sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Create the independent project outside the source template library.");
        Directory.CreateDirectory(parent);
        var staging = Within(parent, ".studio-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await CopyDirectoryAsync(Path.Combine(_sourceRoot, "Starters", "standard"), Path.Combine(staging, "Starters", "standard"), ct);
            foreach (var sdk in new[] { "HyperLib.Core", "HyperLib.Runtime", "HyperLib.Host" })
                await CopyDirectoryAsync(Path.Combine(_sourceRoot, "src", sdk), Path.Combine(staging, "src", sdk), ct);
            await WriteAsync(staging, "hyperlib.app.json", JsonSerializer.Serialize(new { schemaVersion = 1, id = "hyperlib-work", title = request.WorkTitle }, ContentJson.Options), ct);
            foreach (var groupId in selected.Select(item => item.GroupId).Distinct(StringComparer.Ordinal))
            {
                var source = Path.Combine(CatalogLibraryRoot(groupId), "CoreLibrary", "DesignGroups", groupId);
                if (Directory.Exists(source)) await CopyDirectoryAsync(source, Path.Combine(staging, "CoreLibrary", "DesignGroups", groupId), ct);
                else
                {
                    _ = ReusableTemplateCatalog.Find(_sourceRoot, groupId);
                    if (!ReusableTemplateCatalog.IsBuiltIn(groupId))
                        await CopyDirectoryAsync(Path.Combine(_sourceRoot, "Templates", groupId), Path.Combine(staging, "Templates", groupId), ct);
                }
            }
            var workId = "work-" + Guid.NewGuid().ToString("N")[..16];
            var workRoot = Within(staging, "Works/" + workId);
            await TemplateProjectScaffolder.EnsureAsync(staging, staging, "library", request.WorkTitle, ct);
            await TemplateProjectScaffolder.EnsureAsync(staging, workRoot, workId, request.WorkTitle, ct, ReusableTemplateCatalog.BlankId);
            var studioLayout = VisualPageProjectService.CreateDefaultStudioLayout();
            studioLayout.IsRightToolPanelOpen = false;
            studioLayout.IsLeftToolPanelOpen = true;
            if (request.DirectEditing) studioLayout.IsLeftToolPanelOpen = false;
            studioLayout.IsProjectOverride = true;
            var manifestPath = Path.Combine(workRoot, "visual-page.project.json");
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, ct))!.AsObject();
            manifest["layoutOverride"] = JsonSerializer.SerializeToNode(studioLayout, ContentJson.Options);
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(ContentJson.Options), new UTF8Encoding(false), ct);
            if (selected.Any(item => !Directory.Exists(Path.Combine(staging, "CoreLibrary", "DesignGroups", item.GroupId))))
                await TemplateObjectLibrary.EnsureCoreLibraryAsync(staging, ct);
            var template = TemplateObjectLibrary.ReadDefinition(workRoot);
            template.DefaultBlocks.Clear();
            template.EmptyPageDefault = true;
            var toolkit = new List<HyperlibProjectToolkitItem>();
            var templatePath = Path.Combine(workRoot, "authoring.template.json");
            foreach (var choice in selected)
            {
                var sourceObject = TemplateObjectLibrary.ListObjects(staging, choice.GroupId).Single(item => item.Id == choice.ObjectId);
                var priorIds = template.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
                var bytes = await File.ReadAllBytesAsync(templatePath, ct);
                var plan = await TemplateObjectImporter.PrepareAsync(staging, workRoot, template, [ParseChoice(choice.Id)], ct: ct);
                var types = await ImportedTypesAsync(staging, workRoot, template, plan.Definition, choice, ct);
                await TemplateObjectImporter.CommitAsync(workRoot, plan.Definition, [plan], Convert.ToHexStringLower(SHA256.HashData(bytes)), ct);
                template = plan.Definition;
                var layoutBlocks = choice.Kind == "layout" ? ContentJson.Clone(template.DefaultBlocks) : [];
                if (choice.Kind == "layout") types = layoutBlocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToList();
                toolkit.Add(new() { ChoiceId = choice.Id, Title = choice.Title, Description = choice.Description, Kind = choice.Kind,
                    Category = choice.Category, GroupTitle = choice.GroupTitle, BlockTypeIds = types, LayoutBlocks = layoutBlocks,
                    LayoutRegions = choice.Kind == "layout" ? ContentJson.Clone(template.Regions) : [],
                    SourceBlockDefinitions = HeaderSourceDefinitions(choice.Kind, types, layoutBlocks, template),
                    LayoutAppearance = sourceObject.Appearance.Where(field => field.DefaultValue.HasValue)
                        .ToDictionary(field => field.Id, field => template.Appearance.Last(item => item.Id == field.Id).DefaultValue!.Value.Clone(), StringComparer.Ordinal) });
            }
            // Four stable regions are the staging coordinate system, independent of any borrowed example layout.
            if (!HeaderRecipes(request).Any(recipe => !string.IsNullOrWhiteSpace(recipe.CompleteTemplateObjectId)))
                template.Regions = [new() { Id = "header", Title = "Header" }, new() { Id = "sidebar", Title = "Side navigation" },
                    new() { Id = "main", Title = "Page content" }, new() { Id = "footer", Title = "Footer" }];
            template.DefaultBlocks.Clear();
            template.EmptyPageDefault = true;
            template.Appearance.RemoveAll(field => field.Id is "workLandingPage" or "studioAlignment" or "studioWidth" or "studioNavigation");
            template.Appearance.AddRange([new() { Id = "workLandingPage", Title = "Work landing Page", Type = "string" },
                new() { Id = "studioAlignment", Title = "Content alignment", Type = "select", Options = ["left", "center", "right"], DefaultValue = ContentJson.Value("left") },
                new() { Id = "studioWidth", Title = "Content width", Type = "select", Options = ["comfortable", "wide", "full"], DefaultValue = ContentJson.Value("comfortable") },
                new() { Id = "studioNavigation", Title = "Navigation position", Type = "select", Options = ["top", "left", "right", "bottom", "none"], DefaultValue = ContentJson.Value("top") }]);
            template.Blocks.Add(new() { Id = "studio-navigation", Title = "Work navigation", Component = "WorkNavigationBlock", Category = "Navigation",
                Description = "Automatically lists Pages in this Work, using their stable IDs.", Fields = [
                    new() { Id = "items", Title = "Manual Pages", Type = "text", DefaultValue = ContentJson.Value("") },
                    new() { Id = "current", Title = "Current Page", Type = "string", DefaultValue = ContentJson.Value("") },
                    new() { Id = "autoPages", Title = "Follow Work Pages", Type = "boolean", DefaultValue = ContentJson.Value(true) }] });
            request = ContentJson.Clone(request);
            await ImportRecipeLogosAsync(workRoot, request, ct);
            await ImportHeaderTemplateValuesAsync(workRoot, request, toolkit, template, ct);
            HyperlibProjectPipelineService.EnsureAppearanceFields(template);
            HyperlibHeaderLayouts.EnsureBlockDefinition(template);
            HyperlibProjectPipelineService.EnsureDirectEditorFields(template);
            var landing = HyperlibProjectPipelineService.Compose(request.LandingTemplate, toolkit, template, "landing");
            var namedRecipes = HyperlibProjectPipelineService.NamedTemplates(request);
            var pageTemplates = namedRecipes.Select(item => { var result = HyperlibProjectPipelineService.Compose(item.Definition, toolkit, template,
                HyperlibProjectPipelineService.TemplateRole(item.Id)); result.Id = item.Id; result.Name = item.Name.Trim(); return result; }).ToList();
            var page = pageTemplates.Single(item => item.Id == request.DefaultPageTemplateId);
            HyperlibProjectPipelineService.EnsureDirectEditorFields(template);
            template.DefaultBlocks = ContentJson.Clone(page.Blocks);
            TemplateValidator.ValidateDefinition(template);
            await WriteAsync(workRoot, "authoring.template.json", JsonSerializer.Serialize(template, ContentJson.Options), ct);
            await WriteAsync(workRoot, "src/Pages/Blocks/WorkNavigationBlock.razor", NavigationComponent, ct);
            await InstallHeaderRendererAsync(workRoot, ct);
            await CoalesceGeneratedSharedSupportAsync(workRoot, ct);
            await WriteAsync(workRoot, "src/Pages/wwwroot/work-theme.css", NavigationCss + Environment.NewLine + StagedLayoutCss + Environment.NewLine + HyperlibHeaderLayouts.Css + Environment.NewLine + HyperlibNavigationLayouts.Css + Environment.NewLine + DirectEditorGeometryCss, ct);
            var appPath = Path.Combine(workRoot, "src", "Host", "Components", "App.razor");
            var appText = await File.ReadAllTextAsync(appPath, ct);
            await File.WriteAllTextAsync(appPath, appText.Replace("<HeadOutlet", "<link rel=\"stylesheet\" href=\"_content/HyperLib.Work/work-theme.css\" /><HeadOutlet", StringComparison.Ordinal), new UTF8Encoding(false), ct);
            var landingId = "landing-" + workId;
            var state = new LibraryState { LibraryId = "library-" + Guid.NewGuid().ToString("N")[..12] };
            state.Nodes.Add(new() { Id = "project-collection", Type = "collection", ParentId = state.LibraryId, Title = request.WorkTitle });
            var work = new LibraryNode { Id = workId, Type = "work", ParentId = "project-collection", Title = request.WorkTitle,
                StarterId = ReusableTemplateCatalog.BlankId, Appearance = DefaultAppearance(template) };
            foreach (var setting in page.Appearance) work.Appearance[setting.Key] = setting.Value.Clone();
            work.Appearance["workLandingPage"] = ContentJson.Value(landingId);
            state.Nodes.Add(work);
            state.Nodes.Add(new() { Id = landingId, Type = "page", ParentId = workId, Title = "Work landing", Order = -1,
                Appearance = ContentJson.Clone(landing.Appearance) });
            state.Pages[landingId] = new() { Id = landingId, WorkId = workId, Title = "Work landing", TemplateId = template.TemplateId,
                UsesEditableChrome = true, Blocks = ContentJson.Clone(landing.Blocks) };
            await WriteAsync(staging, "Content/library.json", JsonSerializer.Serialize(state, ContentJson.Options), ct);
            var store = new ContentStore(staging) { TemplateResolver = package => package == workId ? template : null };
            await store.InitializeAsync(ct);
            state = await store.ReadAsync(ct);
            ContentRules.Validate(state, package => package == workId ? template : null);
            var pipeline = new HyperlibProjectPipelineState { DirectEditing = request.DirectEditing, WorkId = workId, LandingPageId = landingId, WorkTitle = request.WorkTitle, SourceLibraryRoot = _sourceRoot,
                SelectedToolsetIds = selectedIds.ToList(), Toolkit = toolkit, LandingTemplate = landing, PageTemplate = page,
                PageTemplates = pageTemplates, DefaultPageTemplateId = request.DefaultPageTemplateId };
            await HyperlibProjectPipelineService.SaveAsync(workRoot, pipeline, ct);
            await WriteAsync(workRoot, ".studio/wizard.json", JsonSerializer.Serialize(new { pipelineVersion = 2, workTitle = request.WorkTitle,
                selectedToolsetIds = selectedIds, landingTemplate = request.LandingTemplate, pageTemplate = page.Definition,
                pageTemplates = request.PageTemplates, defaultPageTemplateId = request.DefaultPageTemplateId }, ContentJson.Options), ct);
            await WriteAsync(staging, ".studio/hyperlib-work.json", JsonSerializer.Serialize(new { schemaVersion = 2, projectKind = "Hyperlib", pipelineVersion = 2,
                workId, projectPath = "Works/" + workId, landingPageId = landingId }, ContentJson.Options), ct);
            await WriteAsync(staging, "src/HyperLib.Runtime/Components/BrowsePage.razor", BrowseComponent, ct);
            var runtime = GuidedDynamicPageComponent();
            await WriteAsync(staging, "src/HyperLib.Runtime/Components/DynamicPage.razor", runtime, ct);
            var rendererScript = Path.Combine(staging, "src/HyperLib.Runtime/wwwroot/renderer.js");
            await File.WriteAllTextAsync(rendererScript, EnsureDirectEditorExtentScript(HyperlibGuidedPagePreviewService.ProtectSharedTemplateFields(await File.ReadAllTextAsync(rendererScript, ct))), new UTF8Encoding(false), ct);
            // A published Work keeps its staged renderer contract even when the reader's SDK differs.
            // This source snapshot belongs to the Work and never replaces the destination reader runtime.
            foreach (var sdk in new[] { "HyperLib.Core", "HyperLib.Runtime" })
                await CopyDirectoryAsync(Path.Combine(staging, "src", sdk), Path.Combine(workRoot, "Templates", "StudioRuntime", sdk), ct);
            var pagesProject = Path.Combine(workRoot, "src", "Pages", "Pages.csproj");
            var projectXml = System.Xml.Linq.XDocument.Load(pagesProject);
            foreach (var reference in projectXml.Descendants("ProjectReference"))
                if (reference.Attribute("Include")?.Value.EndsWith("HyperLib.Runtime.csproj", StringComparison.OrdinalIgnoreCase) == true)
                    reference.SetAttributeValue("Include", Path.GetRelativePath(Path.GetDirectoryName(pagesProject)!,
                        Path.Combine(workRoot, "Templates", "StudioRuntime", "HyperLib.Runtime", "HyperLib.Runtime.csproj")).Replace('\\', '/'));
            await File.WriteAllTextAsync(pagesProject, projectXml.ToString(), new UTF8Encoding(false), ct);
            await DisableOwnedProjectCompressionAsync(workRoot, ct);
            await CopyReferencedMediaAsync(state, template, staging, ct);
            await InstallAuthoringHostAsync(staging, ct);
            await WriteAsync(workRoot, "README.md", PipelineReadme(request.WorkTitle, workId), ct);
            if (request.DirectEditing)
                await WriteAsync(workRoot, "README.md", DirectEditingReadme(request.WorkTitle, workId), ct);
            await WriteAsync(staging, "README.md", PipelineRootReadme(request.WorkTitle, workId), ct);
            await WriteFileGuideAsync(workRoot, template, state, workId, ct);
            await ExtendPipelineFileGuideAsync(workRoot, ct);
            ct.ThrowIfCancellationRequested();
            RejectLinks(parent);
            Directory.Move(staging, destination);
            return Within(destination, "Works/" + workId);
        }
        catch
        {
            if (Directory.Exists(staging)) { _ = Within(parent, Path.GetFileName(staging)); Directory.Delete(staging, true); }
            throw;
        }
    }

    private static IEnumerable<HyperlibPageTemplateDefinition> HeaderRecipes(HyperlibWorkProjectDefinition definition)
        => new[] { definition.LandingTemplate, definition.PageTemplate }.Concat(definition.PageTemplates.Select(item => item.Definition));

    private static void IncludeHeaderToolset(HyperlibWorkProjectDefinition definition)
    {
        if (definition.SelectedToolsetIds is null) throw new InvalidDataException("The project's toolkit is missing.");
        definition.SelectedToolsetIds = definition.SelectedToolsetIds
            .Concat(HeaderRecipes(definition).SelectMany(recipe => new[] { recipe.CompleteTemplateObjectId, recipe.CompleteTemplateAppearanceId }).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!))
            .Concat(HeaderRecipes(definition)
            .SelectMany(recipe => new[]{recipe.HeaderLayout,recipe.NavigationLayout,recipe.BodyLayout,recipe.FooterLayout}.SelectMany(HyperlibHeaderLayouts.Elements)).Where(element => element.Kind == "template")
            .Select(element => element.TemplateObjectId)).Distinct(StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, BlockDefinition> HeaderSourceDefinitions(string kind, List<string> types,
        List<PageBlock> layoutBlocks, TemplateDefinition template)
        => kind == "layout" ? layoutBlocks.ToDictionary(block => block.Id, block => ContentJson.Clone(template.Blocks.Single(schema => schema.Id == block.Type)), StringComparer.Ordinal)
            : kind is "element" or "preset" ? new() { ["element"] = ContentJson.Clone(template.Blocks.Single(schema => schema.Id == types.Single())) } : [];

    /// <summary>Owned Studio modules share these exact generated support types; importing their second copy would duplicate C# definitions.</summary>
    private static async Task CoalesceGeneratedSharedSupportAsync(string workRoot, CancellationToken ct)
    {
        var imported = Within(workRoot, "src/Pages/Imported");
        if (!Directory.Exists(imported)) return;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        foreach (var file in new[] { "HyperlibWorkProjectDefinition.cs", "HyperlibHeaderLayouts.cs", "HyperlibNavigationLayouts.cs" })
            expected[file] = Normalize(await PipelineSourceAsync(file, ct));
        var equivalent = new List<string>();
        foreach (var file in EnumerateSourceFiles(imported).Where(file => expected.ContainsKey(Path.GetFileName(file))))
        {
            var source = Normalize(await File.ReadAllTextAsync(file, ct));
            if (!source.Contains("namespace Hypershell.Models;", StringComparison.Ordinal)
                && !source.Contains("namespace Hypershell.Services;", StringComparison.Ordinal)) continue;
            // The importer deterministically aliases Razor component names, including the three
            // string constants in these helpers. Undo only those known aliases for equivalence;
            // every other byte must still match the canonical generated support.
            var comparable = System.Text.RegularExpressions.Regex.Replace(source,
                @"\b(?:Hl[0-9a-f]{12}_)+(StudioHeaderRowBlock|StudioNavigationLinksBlock|StudioNavigationLayoutBlock)\b", "$1",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            var name = Path.GetFileName(file);
            var priorGeneratedModel = name == "HyperlibWorkProjectDefinition.cs" && new[] {
                "B6676D43F6F8B341A2021FC6BF664AD2AC91F540E119FD118AA82C05E25A9B60", "043FBBD0A7F6796CF65C64B59BE9987AD60481B1A2C57660092456CD0DFEA7CA", // Exact shipped Footer DTO; new complete-choice properties are optional.
                "AB9CFB5E63B65CC043C8A1B0B3386704D91F5BA6EDEB202AB173E07350D07A1D" // Exact shipped Body DTO; the current canonical DTO is a compatible superset.
            }.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(comparable))), StringComparer.Ordinal);
            if (comparable != expected[name] && !priorGeneratedModel)
                throw new InvalidDataException("This saved template has customized shared Studio support: " + Path.GetFileName(file)
                    + ". Preserve its source and give customized helper types their own namespace before importing it as a complete template.");
            equivalent.Add(file);
        }
        // Validate every candidate before changing any copy. The source library and unrelated module files are untouched.
        foreach (var file in equivalent) { ct.ThrowIfCancellationRequested(); _ = Within(workRoot, Path.GetRelativePath(workRoot, file)); File.Delete(file); }
    }

    private static async Task<List<string>> ImportedTypesAsync(string libraryRoot, string workRoot, TemplateDefinition prior,
        TemplateDefinition imported, HyperlibWorkTemplateChoice choice, CancellationToken ct)
    {
        if (choice.Kind == "layout") return imported.DefaultBlocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToList();
        if (choice.Kind is not ("element" or "preset")) return [];
        var added = imported.Blocks.Where(block => !prior.Blocks.Any(old => old.Id == block.Id)).ToArray();
        if (added.Length == 1) return [added[0].Id];
        // A configured preset may reuse an earlier identical schema. Resolve that exact schema,
        // including rewritten dependencies and defaults, rather than guessing by component name.
        var probe = ContentJson.Clone(prior);
        probe.Blocks = [new() { Id = "studio-mapping-probe", Title = "Import mapping probe", Component = "StudioMappingProbe" }];
        probe.DefaultBlocks = [];
        foreach (var region in probe.Regions) region.AllowedBlocks = [];
        var expected = await TemplateObjectImporter.PrepareAsync(libraryRoot, workRoot, probe, [ParseChoice(choice.Id)], ct: ct);
        var source = expected.Definition.Blocks.Single(block => block.Id != "studio-mapping-probe");
        var match = imported.Blocks.SingleOrDefault(candidate => Equivalent(candidate, source))
            ?? throw new InvalidDataException("The imported library object's exact component schema could not be resolved.");
        return [match.Id];
        static bool Equivalent(BlockDefinition candidate, BlockDefinition source)
        { var copy = ContentJson.Clone(source); copy.Id = candidate.Id; copy.Title = candidate.Title;
            return JsonSerializer.Serialize(copy, ContentJson.Options) == JsonSerializer.Serialize(candidate, ContentJson.Options); }
    }

    private static async Task ImportHeaderTemplateValuesAsync(string workRoot, HyperlibWorkProjectDefinition definition,
        IReadOnlyList<HyperlibProjectToolkitItem> toolkit, TemplateDefinition template, CancellationToken ct)
    {
        foreach (var recipe in HeaderRecipes(definition))
            foreach (var element in new[]{recipe.HeaderLayout,recipe.NavigationLayout,recipe.BodyLayout,recipe.FooterLayout}.SelectMany(HyperlibHeaderLayouts.Elements).Where(element => element.Kind == "template"))
            {
                var choice = toolkit.Single(item => item.ChoiceId == element.TemplateObjectId);
                var schemas = choice.SourceBlockDefinitions.Count > 0 ? choice.SourceBlockDefinitions
                    : HeaderSourceDefinitions(choice.Kind, choice.BlockTypeIds, choice.LayoutBlocks, template);
                foreach (var (key, fields) in element.TemplateValues)
                {
                    if (!schemas.TryGetValue(key, out var schema)) throw new InvalidDataException("The selected library template no longer has this field group.");
                    foreach (var field in schema.Fields.Where(field => field.Type == "media"))
                        if (fields.TryGetValue(field.Id, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                            && Path.IsPathFullyQualified(value.GetString()!) && !value.GetString()!.StartsWith("/", StringComparison.Ordinal))
                            fields[field.Id] = ContentJson.Value(await HeaderMediaUrlAsync(value.GetString()!, "template", workRoot, ct));
                }
            }
    }

    private static async Task ImportRecipeLogosAsync(string workRoot, HyperlibWorkProjectDefinition definition, CancellationToken ct)
    {
        var recipes = new[] { definition.LandingTemplate, definition.PageTemplate }
            .Concat(definition.PageTemplates.Select(item => item.Definition));
        foreach (var recipe in recipes)
        {
            if (recipe.BackgroundSurface is null || recipe.BackgroundSurface.ImagePath is null)
                throw new InvalidDataException("Choose a valid Page background before saving its template.");
            HyperlibHeaderLayouts.Validate(recipe.HeaderLayout, allowLocalMedia: true);
            HyperlibHeaderLayouts.Validate(recipe.NavigationLayout, allowLocalMedia: true);
            HyperlibHeaderLayouts.Validate(recipe.BodyLayout, allowLocalMedia: true);
            HyperlibHeaderLayouts.Validate(recipe.FooterLayout, allowLocalMedia: true);
            foreach (var element in new[]{recipe.HeaderLayout,recipe.NavigationLayout,recipe.BodyLayout,recipe.FooterLayout}.SelectMany(HyperlibHeaderLayouts.Elements))
                if (!string.IsNullOrWhiteSpace(element.MediaPath) && !element.MediaPath.StartsWith("/media/", StringComparison.Ordinal))
                    element.MediaPath = await HeaderMediaUrlAsync(element.MediaPath, element.Kind, workRoot, ct);
            if (!string.IsNullOrWhiteSpace(recipe.BackgroundSurface.ImagePath) && !recipe.BackgroundSurface.ImagePath.StartsWith("/media/", StringComparison.Ordinal))
                recipe.BackgroundSurface.ImagePath = await ImageUrlAsync(recipe.BackgroundSurface.ImagePath, workRoot, ct);
            foreach (var (_, style) in HyperlibProjectPipelineService.RegionStyles(recipe))
                if (!string.IsNullOrWhiteSpace(style.LogoPath) && !style.LogoPath.StartsWith("/media/", StringComparison.Ordinal))
                    style.LogoPath = await ImageUrlAsync(style.LogoPath, workRoot, ct);
        }
    }

    private static async Task<string> HeaderMediaUrlAsync(string input, string kind, string workRoot, CancellationToken ct)
    {
        var source = Path.GetFullPath(input);
        RejectLinks(source);
        var info = new FileInfo(source);
        var video = new[] { ".mp4", ".webm", ".ogv" }.Contains(info.Extension, StringComparer.OrdinalIgnoreCase);
        var audio = new[] { ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".aac" }.Contains(info.Extension, StringComparer.OrdinalIgnoreCase);
        var document = new[] { ".pdf", ".txt", ".csv", ".json" }.Contains(info.Extension, StringComparer.OrdinalIgnoreCase);
        var image = ImageExtensions.Contains(info.Extension) || info.Extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
        if (!info.Exists || (kind == "video" ? !video : kind == "image" ? !image : !video && !image && !audio && !document)
            || info.Length > (video || audio || document ? 64L : 32L) * 1024 * 1024)
            throw new InvalidDataException("Choose a supported image under 32 MiB, or video, audio, PDF, or text document under 64 MiB.");
        var bytes = await File.ReadAllBytesAsync(source, ct);
        var filename = Convert.ToHexStringLower(SHA256.HashData(bytes))[..24] + info.Extension.ToLowerInvariant();
        foreach (var target in new[] { Within(workRoot, "src/Pages/wwwroot/header-media/" + filename),
            Within(Path.GetFullPath(Path.Combine(workRoot, "../..")), "Media/studio-header/" + filename) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) await File.WriteAllBytesAsync(target, bytes, ct);
            else if (!(await File.ReadAllBytesAsync(target, ct)).SequenceEqual(bytes)) throw new IOException("An existing owned header asset has conflicting bytes.");
        }
        return "/media/studio-header/" + filename;
    }

    private static async Task<string> PipelineSourceAsync(string file, CancellationToken ct)
    {
        await using var stream = typeof(HyperlibWorkProjectService).Assembly.GetManifestResourceStream("StudioPipeline." + file)
            ?? throw new InvalidDataException("The Studio installation is missing its header source package.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task InstallHeaderRendererAsync(string workRoot, CancellationToken ct)
    {
        foreach (var file in new[] { "HyperlibWorkProjectDefinition.cs", "HyperlibHeaderLayouts.cs", "HyperlibNavigationLayouts.cs" })
            await WriteAsync(workRoot, "src/Pages/StudioHeader/" + file, await PipelineSourceAsync(file, ct), ct);
        await WriteAsync(workRoot, "src/Pages/Blocks/StudioHeaderRowBlock.razor", HyperlibHeaderRenderingSource.ComponentSource, ct);
        await WriteAsync(workRoot,"src/Pages/Blocks/StudioNavigationLayoutBlock.razor",HyperlibNavigationRenderingSource.LayoutComponent,ct);
        await WriteAsync(workRoot,"src/Pages/Blocks/StudioNavigationLinksBlock.razor",HyperlibNavigationRenderingSource.LinksComponent,ct);
    }

    private const string HeaderRowComponent = """
        @using Hypershell.Services
        @((MarkupString)HyperlibHeaderLayouts.RenderSerializedBlock(Context.Text("layout")))
        @code { [Parameter, EditorRequired] public BlockContext Context { get; set; } = new(); }
        """;

    private static string GuidedDynamicPageComponent(bool elementEditing = true)
    {
        var runtime = DynamicPageComponent.Replace("<main class=\"hl-document @Options.VariantClass(_state, _pageNode ?? _work)\" style=\"@AppearanceStyle\">",
            "<main class=\"hl-document @Options.VariantClass(_state, _pageNode ?? _work) @(IsCompleteTemplate ? \"\" : \"studio-staged-layout studio-guided-layout\")\" style=\"@AppearanceStyle\" data-studio-alignment=\"@Options.Appearance(\"studioAlignment\",_state,_pageNode ?? _work)\" data-studio-width=\"@Options.Appearance(\"studioWidth\",_state,_pageNode ?? _work)\" data-studio-navigation=\"@Options.Appearance(\"studioNavigation\",_state,_pageNode ?? _work)\">", StringComparison.Ordinal);
        runtime = runtime.Replace("region => _page.Blocks.Any(block => block.Region == region.Id)",
            "region => _page.Blocks.Any(block => block.Region == region.Id) || HasChrome(region.Id)", StringComparison.Ordinal);
        runtime = runtime.Replace("<section class=\"hl-region\" data-region=\"@region.Id\" aria-label=\"@region.Title\">", """
                    <section class="hl-region" data-region="@region.Id" aria-label="@region.Title" style="@RegionStyle(region.Id)">
                        @if (HasChrome(region.Id))
                        {
                            <div class="studio-region-chrome" data-logo-placement="@Chrome(region.Id, "LogoPlacement")">
                                @if (Chrome(region.Id, "Logo") is { Length: > 0 } logo && Chrome(region.Id, "LogoPlacement") != "none") { <img class="studio-region-logo" src="@logo" alt="" /> }
                                @if (Chrome(region.Id, "Text") is { Length: > 0 } text) { <p class="studio-region-text">@text</p> }
                            </div>
                        }
        """, StringComparison.Ordinal);
        runtime = runtime.Replace("style=\"@RendererOptions.BlockStyle(effectiveBlock)\"", "style=\"@(RendererOptions.BlockStyle(effectiveBlock) + (block.Type == \"studio-navigation\" ? \";\" + Chrome(\"navigation\", \"Css\") : \"\"))\"", StringComparison.Ordinal);
        runtime = runtime.Replace("var effectiveBlock = Options.WithFieldDefaults(block);", """
                            var effectiveBlock = Options.WithFieldDefaults(block);
                            @if (block.Type == "studio-navigation" && (!string.IsNullOrWhiteSpace(Chrome("navigation", "Text")) || !string.IsNullOrWhiteSpace(Chrome("navigation", "Logo"))))
                            {
                                <div class="studio-region-chrome" data-logo-placement="@Chrome("navigation", "LogoPlacement")" style="@Chrome("navigation", "Css")">
                                    @if (Chrome("navigation", "Logo") is { Length: > 0 } navigationLogo && Chrome("navigation", "LogoPlacement") != "none") { <img class="studio-region-logo" src="@navigationLogo" alt="" /> }
                                    @if (Chrome("navigation", "Text") is { Length: > 0 } navigationText) { <p class="studio-region-text">@navigationText</p> }
                                </div>
                            }
        """, StringComparison.Ordinal);
        runtime = runtime.Replace("private string AppearanceStyle =>", """
            private string RegionName(string region) => region switch { "header" => "Header", "footer" => "Footer", "main" => "Body", "navigation" => "Navigation", _ => "Extras" };
            private string Chrome(string region, string field) => Options.Appearance("studio" + RegionName(region) + field, _state, _pageNode ?? _work);
            private bool IsCompleteTemplate => string.Equals(Options.Appearance("studioCompleteTemplate", _state, _pageNode ?? _work), "true", StringComparison.OrdinalIgnoreCase);
            private string RegionStyle(string region) => IsCompleteTemplate ? "" : Chrome(region, "Css");
            private bool HasChrome(string region) => !IsCompleteTemplate && !(region == "header" && string.Equals(Options.Appearance("studioHeaderBuilder", _state, _pageNode ?? _work), "true", StringComparison.OrdinalIgnoreCase))
                && (!string.IsNullOrWhiteSpace(Chrome(region, "Text")) || !string.IsNullOrWhiteSpace(Chrome(region, "Logo"))
                || region is "header" or "footer" && string.Equals(Chrome(region, "Enabled"), "true", StringComparison.OrdinalIgnoreCase));
            private string AppearanceStyle =>
        """, StringComparison.Ordinal);
        runtime = runtime.Replace("Options.AppearanceStyle(_state, _pageNode ?? _work);",
            "Options.AppearanceStyle(_state, _pageNode ?? _work) + (IsCompleteTemplate || IsDirectEditing ? \"\" : \";\" + Options.Appearance(\"studioBackgroundCss\", _state, _pageNode ?? _work));", StringComparison.Ordinal);
        runtime = runtime.Replace("@(IsCompleteTemplate ? \"\" : \"studio-staged-layout studio-guided-layout\")",
            "@(IsCompleteTemplate || IsDirectEditing ? \"\" : \"studio-staged-layout studio-guided-layout\") @(IsDirectEditing ? \"studio-direct-page\" : \"\")", StringComparison.Ordinal);
        runtime = runtime.Replace("region => _page.Blocks.Any(block => block.Region == region.Id) || HasChrome(region.Id)",
            "region => _page.Blocks.Any(block => block.Region == region.Id) || HasChrome(region.Id) || (Editing && region.Id == \"main\")", StringComparison.Ordinal);
        runtime = runtime.Replace("private string RegionStyle(string region) => IsCompleteTemplate ? \"\" : Chrome(region, \"Css\");", """
            private string RegionStyle(string region)
            {
                var style = IsCompleteTemplate || IsDirectEditing ? "" : Chrome(region, "Css");
                var positioned = _page?.Blocks.Where(block => block.Region == region && Positioned(block)).ToArray() ?? [];
                if (positioned.Length > 0)
                {
                    var height = positioned.Max(block => Geometry(block, "studioY", 0, 0, 100000)
                        + Math.Max(Geometry(block, "studioHeight", 0, 0, 100000), 80));
                    style += ";position:relative;box-sizing:border-box;min-height:" + Number(Math.Max(320, height)) + "px";
                }
                else if (Editing && IsDirectEditing && region == "main") style += ";position:relative;min-height:480px";
                return style;
            }
            """, StringComparison.Ordinal);
        runtime = runtime.Replace("RendererOptions.BlockStyle(effectiveBlock) + (block.Type", "RendererOptions.BlockStyle(effectiveBlock) + GeometryStyle(effectiveBlock) + (block.Type", StringComparison.Ordinal);
        runtime = runtime.Replace("@RendererOptions.BlockSpanClass(effectiveBlock)\"", "@RendererOptions.BlockSpanClass(effectiveBlock) @(Positioned(effectiveBlock) ? \"studio-canvas-positioned\" : \"\")\"", StringComparison.Ordinal);
        runtime = runtime.Replace("data-block-type=\"@block.Type\"", "data-block-type=\"@block.Type\" data-studio-sized=\"@(Geometry(effectiveBlock, \"studioHeight\", 0, 0, 100000) > 0 ? \"true\" : null)\"", StringComparison.Ordinal);
        runtime = runtime.Replace("private string AppearanceStyle =>", """
            private bool IsDirectEditing => string.Equals(Options.Appearance("studioDirectEditing", _state, _pageNode ?? _work), "true", StringComparison.OrdinalIgnoreCase);
            private static string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            private static bool Positioned(PageBlock block) => block.Values.TryGetValue("studioPositioned", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;
            private static double Geometry(PageBlock block, string key, double fallback, double min, double max)
                => block.Values.TryGetValue(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number
                    && value.TryGetDouble(out var number) && double.IsFinite(number) ? Math.Clamp(number, min, max) : fallback;
            private static string GeometryStyle(PageBlock block)
            {
                if (!Positioned(block)) return "";
                var width = Geometry(block, "studioWidth", 100, 1, 100);
                var x = Math.Min(Geometry(block, "studioX", 0, 0, 100), 100 - width);
                var height = Geometry(block, "studioHeight", 0, 0, 100000);
                return ";position:absolute;box-sizing:border-box;margin:0;left:" + Number(x) + "%;top:" + Number(Geometry(block, "studioY", 0, 0, 100000))
                    + "px;width:" + Number(width) + "%;max-width:100%;" + (height > 0 ? "height:" + Number(height) + "px;" : "");
            }
            private string AppearanceStyle =>
            """, StringComparison.Ordinal);
        if (elementEditing)
            runtime = runtime.Replace("data-block-type=\"@block.Type\"", "data-block-type=\"@block.Type\" data-studio-elements=\"@(effectiveBlock.Values.TryGetValue(\"studioElements\", out var elementEdits) && elementEdits.ValueKind == System.Text.Json.JsonValueKind.String ? elementEdits.GetString() : null)\"", StringComparison.Ordinal);
        return runtime;
    }

    private static string LegacyCreatePageHook() => CreatePageFromStagedLayout.Replace("item.NodeType == \"page\"))", "item.NodeType == \"page\" && item.Page is null))", StringComparison.Ordinal)
            .Replace("        if (operation.Page is not null) continue;\n", "", StringComparison.Ordinal).Replace("""
                var selectedId = operation.Appearance?.TryGetValue("studioPageTemplateId", out var selected) == true
                    && selected.ValueKind == JsonValueKind.String ? selected.GetString() : null;
                var selectedTemplate = HyperlibProjectPipelineService.GetPageTemplate(stagedPipeline, selectedId);
                operation.Page = new() { TemplateId = template.TemplateId, UsesEditableChrome = true, Blocks = ContentJson.Clone(selectedTemplate.Blocks) };
                var appearance = ContentJson.Clone(selectedTemplate.Appearance);
                appearance["studioPageTemplateId"] = ContentJson.Value(selectedTemplate.Id);
        """, """
                operation.Page = new() { TemplateId = template.TemplateId, UsesEditableChrome = true, Blocks = ContentJson.Clone(stagedPipeline.PageTemplate.Blocks) };
                var appearance = ContentJson.Clone(stagedPipeline.PageTemplate.Appearance);
        """, StringComparison.Ordinal);

    /// <summary>Only explicit foundation saves upgrade recognizable project-owned generated code; custom source is never overwritten.</summary>
    private static void ValidateOwnedPipelineUpgrade(string projectRoot)
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(projectRoot, "../.."));
        var programPath = HyperlibProjectPipelineService.Guard(libraryRoot, "src/HyperLib.Host/Program.cs");
        var program = File.ReadAllText(programPath);
        var hookStart = program.IndexOf("var stagedPipeline = await studioPipeline.ReadAsync(StudioWorkRoot(), ct);", StringComparison.Ordinal);
        var hookEnd = program.IndexOf("var state = await store.ApplyAsync(batch, ct);", hookStart >= 0 ? hookStart : 0, StringComparison.Ordinal);
        if (hookStart < 0 || hookEnd <= hookStart || !program.Contains("app.MapPut(\"/api/studio/page-templates\"", StringComparison.Ordinal))
            throw new InvalidDataException("This project's gateway has custom source. Preserve it and restore the generated Studio pipeline hook before saving a new foundation.");
        static string Normalize(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var oldHook = LegacyCreatePageHook();
        var existingHook = Normalize(program[hookStart..hookEnd]);
        if (existingHook != Normalize(CreatePageFromStagedLayout) && existingHook != Normalize(oldHook))
            throw new InvalidDataException("This project's new-Page pipeline hook has custom source. Preserve those changes before upgrading its foundation.");
        foreach (var (file, priorHashes) in new[] {
            ("HyperlibWorkProjectDefinition.cs", new[] { "9962833050930075F8C500790A8F93EB8FB8BA822D091DC2B291B0A5718E8571", "8FE34B5A8E8820697A298D6678947A9DF511B576BDC17A0BF1093A61A5AF4FCB", "E5DEDDE875AD03BFB58A03AB65938E7AFFB1BF9052D7A6CDCF5B10404C179480", "96B14B09B8C6B39C3D5A0B6826DB183079C5D08D6D9925BC34E8FEEA47E233F2", "E539174B5B219AF533AD32E255FF8BA094EF54FF1FBC4F6D02489F069B388391", "ACDA097F8D0D42C63B0E09AD8775893D3AE78BF942EC09EF796BB7BC2C614AF6", "C488FABC37B98AE2DDE08A30131C2CE93DD3E677C4069EA8B63AE95D86CD0BAD", "AB9CFB5E63B65CC043C8A1B0B3386704D91F5BA6EDEB202AB173E07350D07A1D", "B6676D43F6F8B341A2021FC6BF664AD2AC91F540E119FD118AA82C05E25A9B60", "043FBBD0A7F6796CF65C64B59BE9987AD60481B1A2C57660092456CD0DFEA7CA" }),
            ("HyperlibProjectPipelineService.cs", new[] { "6ED2D56C25F7C8DF65C3792EBEFB2C68DF9E88FBDF6DCF87D7035FD6E78E6856", "40E8176F2C0A514C20664555CDC26CA088CCFA694F3D9A3B7D6F0E7050FC92A4", "637CE7D69E4051A5AF0C7FB7056CFE33724436E058745C652E58F86E5E296D52", "EDB9BF9A7F4A791FFB65D19EB1C99BB0CDE729E17B83B6AE6A5E48009641A014", "0E91AB5D8E2455EBA6A3755A7EECECFBAD6BE0EB2391778BA47B669486C341A6", "5299CCE66C2F783CF0322084915E897C6AA8A9B9C35F87C967A8372EAA277659", "7A82DBB9582E88B44B79A8D79DE57DAD7CE9C5BABDCD09A1B71C4C7F95317554", "6C7973B65B39103A5C20D4988E41BE4E416053A4B188588361DC6403A14BB8CF", "A75A22EEC3BBB5A524298FDA67AE4C091F59F65A62274EDCB54AE187E3A74102", "C98AAD22100B5A57858242E00134DA08860E7B16C25D9694DADDC7F4D0EDDF42" }),
            ("HyperlibBackgroundDesigns.cs", Array.Empty<string>()), ("HyperlibHeaderLayouts.cs", new[] { "C10D084B5064EAEFFECF1BDC7D411134ED916006F1CEF2B54709CA97A9D0D02F", "A0AECD70C1511C59834F7D150A19EB58B89141CDB54AB653CF85CB46C0D648A9", "D30DA25783BED32C9316DF49630B5615352E209485FB28E10CE8A1F61A9F8C00", "B96583E67C2CB0CDCC8D05BF6731C140923A05847F3AB1B6D5A2E4F9CB759E20", "DB2A6832E142ECDCFB4D46FBED1D08140D914E4391425E18EE98C000B6F9961B", "18C6D212BB8CBDCE3378E237C7E66FC43289B2F25A81488054099A77207D9192" }), ("HyperlibNavigationLayouts.cs",Array.Empty<string>()) })
        {
            using var stream = typeof(HyperlibWorkProjectService).Assembly.GetManifestResourceStream("StudioPipeline." + file)
                ?? throw new InvalidDataException("The Studio installation is missing its pipeline source package.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var currentSource = Normalize(reader.ReadToEnd());
            var existingPath = HyperlibProjectPipelineService.Guard(libraryRoot, "src/HyperLib.Host/StudioPipeline/" + file);
            if ((file is "HyperlibBackgroundDesigns.cs" or "HyperlibHeaderLayouts.cs" or "HyperlibNavigationLayouts.cs") && !File.Exists(existingPath)) continue;
            var existingSource = Normalize(File.ReadAllText(existingPath));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(existingSource)));
            if (existingSource != currentSource && !priorHashes.Contains(hash, StringComparer.Ordinal))
                throw new InvalidDataException("This project's generated pipeline support file has custom source: " + file + ". Preserve those changes before upgrading its foundation.");
        }
        foreach (var file in new[] { "HyperlibWorkProjectDefinition.cs", "HyperlibHeaderLayouts.cs", "HyperlibNavigationLayouts.cs" })
        {
            var owned = HyperlibProjectPipelineService.Guard(projectRoot, "src/Pages/StudioHeader/" + file);
            if (!File.Exists(owned)) continue;
            using var stream = typeof(HyperlibWorkProjectService).Assembly.GetManifestResourceStream("StudioPipeline." + file)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var ownedSource = Normalize(File.ReadAllText(owned));
            var known = file == "HyperlibWorkProjectDefinition.cs" ? new[] { "96B14B09B8C6B39C3D5A0B6826DB183079C5D08D6D9925BC34E8FEEA47E233F2", "E539174B5B219AF533AD32E255FF8BA094EF54FF1FBC4F6D02489F069B388391", "ACDA097F8D0D42C63B0E09AD8775893D3AE78BF942EC09EF796BB7BC2C614AF6", "C488FABC37B98AE2DDE08A30131C2CE93DD3E677C4069EA8B63AE95D86CD0BAD", "AB9CFB5E63B65CC043C8A1B0B3386704D91F5BA6EDEB202AB173E07350D07A1D", "B6676D43F6F8B341A2021FC6BF664AD2AC91F540E119FD118AA82C05E25A9B60", "043FBBD0A7F6796CF65C64B59BE9987AD60481B1A2C57660092456CD0DFEA7CA" }
                : file == "HyperlibHeaderLayouts.cs" ? new[] { "C10D084B5064EAEFFECF1BDC7D411134ED916006F1CEF2B54709CA97A9D0D02F", "A0AECD70C1511C59834F7D150A19EB58B89141CDB54AB653CF85CB46C0D648A9", "D30DA25783BED32C9316DF49630B5615352E209485FB28E10CE8A1F61A9F8C00", "B96583E67C2CB0CDCC8D05BF6731C140923A05847F3AB1B6D5A2E4F9CB759E20", "DB2A6832E142ECDCFB4D46FBED1D08140D914E4391425E18EE98C000B6F9961B", "18C6D212BB8CBDCE3378E237C7E66FC43289B2F25A81488054099A77207D9192" } : Array.Empty<string>();
            var ownedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownedSource)));
            if (ownedSource != Normalize(reader.ReadToEnd()) && !known.Contains(ownedHash, StringComparer.Ordinal))
                throw new InvalidDataException("This project's generated header source has custom changes. Preserve them before updating its reusable header.");
        }
        var headerComponent = HyperlibProjectPipelineService.Guard(projectRoot, "src/Pages/Blocks/StudioHeaderRowBlock.razor");
        if (File.Exists(headerComponent) && Normalize(File.ReadAllText(headerComponent)) != Normalize(HeaderRowComponent)
            && Normalize(File.ReadAllText(headerComponent)) != Normalize(HyperlibHeaderRenderingSource.ComponentSource)
            && !new[] { "C5C928798658B08982C956F74232024D9CAFB7A513DAF3B2C83544386670EE9C", "87F8A750CDC3B135B7D26EEE0BD114910A647F76A021CFD39CA4174E5CDC0814", "C4549EC507BF5975F679AC25C161B1F1F0B6695A2720394F876F75D21965251A", "30863EF0F3045ACE9E3BC218D55FB65BA0C4C6260A3024F478F99CFA2A97B7C7", "2E08FBC5E68E134C72AC5E20D19640433FDBE3F07911C8C189DA327882C0BC2A" }
                .Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(File.ReadAllText(headerComponent))))), StringComparer.Ordinal))
            throw new InvalidDataException("This project's generated header component has custom changes. Preserve them before updating its reusable header.");
        foreach(var (file,expected) in new[]{("StudioNavigationLayoutBlock.razor",HyperlibNavigationRenderingSource.LayoutComponent),("StudioNavigationLinksBlock.razor",HyperlibNavigationRenderingSource.LinksComponent)})
        {var path=HyperlibProjectPipelineService.Guard(projectRoot,"src/Pages/Blocks/"+file);if(!File.Exists(path))continue;var owned=Normalize(File.ReadAllText(path));var priorHash=file=="StudioNavigationLinksBlock.razor"?"A1D89E6C638B86B1CBD0919311DFA0F34E5E55CD79AB534C5E3F27DFB9E3EC23":"FA2246EA2E8F252F55F2832D22AE89987B56E98F065C2D3617DED0630733D901";
            if(owned!=Normalize(expected)&&Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owned)))!=priorHash)throw new InvalidDataException("This project's generated Navigation component has custom source: "+file);}
        foreach (var runtimePath in new[] { HyperlibProjectPipelineService.Guard(projectRoot, "Templates/StudioRuntime/HyperLib.Runtime/Components/DynamicPage.razor"),
            HyperlibProjectPipelineService.Guard(libraryRoot, "src/HyperLib.Runtime/Components/DynamicPage.razor") })
        {
            var runtime = Normalize(File.ReadAllText(runtimePath));
            var runtimeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runtime)));
            if (runtime != Normalize(GuidedDynamicPageComponent()) && runtime != Normalize(GuidedDynamicPageComponent(false)) && runtime != Normalize(LegacyStagedDynamicPageComponent())
                && runtimeHash != "C282E8BCA4F445D56D1F298E32E2D064D230F30BF02ABEDECDFA5F0007294171"
                && runtimeHash != "782BCFE9A490B5BDF489588E71C184901FFFDA585CD958B9BDE9B0FCAD1EA1D5"
                && runtimeHash != "4AB61C25F5E701B7D3C9D0ECF2520E51E8F3D66EFE5E174A54880279A7B4141E"
                && runtimeHash != "38BF4288839D5951430A9EE4A45B51661E75FE4DBF86B9DF314E1E068AA460FB"
                && runtimeHash != "56A3DA3666FED45E6B47DC9D0D5907E87F2C6A8A447ABD45629BB993F2533F78"
                && runtimeHash != "7B5C1FFA6FF4E4A212080A8BA690F0C7606CA8C8B554D0A95346649F1A96332C"
                && runtimeHash != "50F62238F3EA26FBFB47C6F4F8C88BEA686E73DBAC79E5F26880A36AE22886A0")
                throw new InvalidDataException("This project's staged renderer has custom source. Preserve those changes before upgrading its generated foundation.");
        }
    }

    private static string LegacyStagedDynamicPageComponent() => DynamicPageComponent.Replace(
        "<main class=\"hl-document @Options.VariantClass(_state, _pageNode ?? _work)\" style=\"@AppearanceStyle\">",
        "<main class=\"hl-document studio-staged-layout @Options.VariantClass(_state, _pageNode ?? _work)\" style=\"@AppearanceStyle\" data-studio-alignment=\"@Options.Appearance(\"studioAlignment\",_state,_pageNode ?? _work)\" data-studio-width=\"@Options.Appearance(\"studioWidth\",_state,_pageNode ?? _work)\" data-studio-navigation=\"@Options.Appearance(\"studioNavigation\",_state,_pageNode ?? _work)\">", StringComparison.Ordinal);

    private static async Task UpgradeOwnedPipelineAsync(string projectRoot, CancellationToken ct)
    {
        var libraryRoot = Path.GetFullPath(Path.Combine(projectRoot, "../.."));
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var programPath = HyperlibProjectPipelineService.Guard(libraryRoot, "src/HyperLib.Host/Program.cs");
        var program = await File.ReadAllTextAsync(programPath, ct);
        var start = program.IndexOf("var stagedPipeline = await studioPipeline.ReadAsync(StudioWorkRoot(), ct);", StringComparison.Ordinal);
        var end = program.IndexOf("var state = await store.ApplyAsync(batch, ct);", start, StringComparison.Ordinal);
        replacements[programPath] = program[..start] + CreatePageFromStagedLayout + "\n    " + program[end..];
        foreach (var file in new[] { "HyperlibWorkProjectDefinition.cs", "HyperlibProjectPipelineService.cs", "HyperlibBackgroundDesigns.cs", "HyperlibHeaderLayouts.cs", "HyperlibNavigationLayouts.cs" })
        {
            await using var stream = typeof(HyperlibWorkProjectService).Assembly.GetManifestResourceStream("StudioPipeline." + file)
                ?? throw new InvalidDataException("The Studio installation is missing its generated source package.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            replacements[HyperlibProjectPipelineService.Guard(libraryRoot, "src/HyperLib.Host/StudioPipeline/" + file)] = await reader.ReadToEndAsync(ct);
        }
        foreach (var file in new[] { "HyperlibWorkProjectDefinition.cs", "HyperlibHeaderLayouts.cs", "HyperlibNavigationLayouts.cs" })
            replacements[HyperlibProjectPipelineService.Guard(projectRoot, "src/Pages/StudioHeader/" + file)] = await PipelineSourceAsync(file, ct);
        replacements[HyperlibProjectPipelineService.Guard(projectRoot, "src/Pages/Blocks/StudioHeaderRowBlock.razor")] = HyperlibHeaderRenderingSource.ComponentSource;
        replacements[HyperlibProjectPipelineService.Guard(projectRoot,"src/Pages/Blocks/StudioNavigationLayoutBlock.razor")]=HyperlibNavigationRenderingSource.LayoutComponent;
        replacements[HyperlibProjectPipelineService.Guard(projectRoot,"src/Pages/Blocks/StudioNavigationLinksBlock.razor")]=HyperlibNavigationRenderingSource.LinksComponent;
        foreach (var root in new[] { Path.Combine(libraryRoot, "src/HyperLib.Runtime"), Path.Combine(projectRoot, "Templates/StudioRuntime/HyperLib.Runtime") })
        {
            replacements[HyperlibProjectPipelineService.Guard(root, "Components/DynamicPage.razor")] = GuidedDynamicPageComponent();
            var script = HyperlibProjectPipelineService.Guard(root, "wwwroot/renderer.js");
            replacements[script] = EnsureDirectEditorExtentScript(HyperlibGuidedPagePreviewService.ProtectSharedTemplateFields(await File.ReadAllTextAsync(script, ct)));
        }
        var themePath = HyperlibProjectPipelineService.Guard(projectRoot, "src/Pages/wwwroot/work-theme.css");
        var oldTheme = await File.ReadAllTextAsync(themePath, ct);
        var refreshedTheme = oldTheme.Contains("/* studio-guided-regions-v2 */", StringComparison.Ordinal) ? oldTheme : oldTheme + Environment.NewLine + StagedLayoutCss;
        replacements[themePath] = refreshedTheme.Contains("/* studio-header-positioned-media-v1 */", StringComparison.Ordinal) ? refreshedTheme
            : refreshedTheme + Environment.NewLine + HyperlibHeaderLayouts.Css;
        if(!replacements[themePath].Contains("/* studio-navigation-layouts-v1 */",StringComparison.Ordinal))replacements[themePath]+=Environment.NewLine+HyperlibNavigationLayouts.Css;
        replacements[themePath] = RefreshDirectEditorGeometryCss(replacements[themePath]);
        var changed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in replacements)
        {
            var existing = File.Exists(pair.Key) ? await File.ReadAllTextAsync(pair.Key, ct) : null;
            if (existing != pair.Value) changed[pair.Key] = existing;
        }
        if (changed.Count == 0) return;
        var backup = HyperlibProjectPipelineService.Guard(libraryRoot, ".studio/upgrades/" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var pair in changed.Where(pair => pair.Value is not null))
            await WriteAsync(backup, Path.GetRelativePath(libraryRoot, pair.Key), pair.Value!, ct);
        try { foreach (var pair in changed) await HyperlibProjectPipelineService.AtomicWriteAsync(pair.Key, replacements[pair.Key], ct); }
        catch
        {
            foreach (var pair in changed)
                if (pair.Value is null) File.Delete(pair.Key);
                else await HyperlibProjectPipelineService.AtomicWriteAsync(pair.Key, pair.Value, CancellationToken.None);
            throw;
        }
    }

    private static async Task InstallAuthoringHostAsync(string libraryRoot, CancellationToken ct)
    {
        var hostRoot = Path.Combine(libraryRoot, "src", "HyperLib.Host");
        foreach (var file in new[] { "HyperlibWorkProjectDefinition.cs", "HyperlibProjectPipelineService.cs", "HyperlibBackgroundDesigns.cs", "HyperlibHeaderLayouts.cs", "HyperlibNavigationLayouts.cs" })
        {
            await using var stream = typeof(HyperlibWorkProjectService).Assembly.GetManifestResourceStream("StudioPipeline." + file)
                ?? throw new InvalidDataException("The Studio installation is missing the project pipeline source package.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await WriteAsync(hostRoot, "StudioPipeline/" + file, await reader.ReadToEndAsync(ct), ct);
        }
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "StudioHyperlibAuthoring");
        if (!Directory.Exists(assetRoot))
            for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Hypershell", "Assets", "StudioHyperlibAuthoring");
                if (Directory.Exists(candidate)) { assetRoot = candidate; break; }
            }
        // This is an unpublished creation staging directory. A portable source library may already
        // contain Studio's managed UI; refresh only these managed copies instead of copying them twice.
        var targetAssets = Path.Combine(hostRoot, "wwwroot", "studio-authoring");
        foreach (var file in EnumerateSourceFiles(assetRoot))
        {
            ct.ThrowIfCancellationRequested(); RejectLinks(file);
            var target = Within(targetAssets, Path.GetRelativePath(assetRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        var programPath = Path.Combine(hostRoot, "Program.cs");
        var program = await File.ReadAllTextAsync(programPath, ct);
        if (!program.Contains("using Hypershell.Services;", StringComparison.Ordinal)) program = "using Hypershell.Services;\n" + program;
        const string pipelineMarker = "app.MapPut(\"/api/studio/page-templates\"";
        if (program.Contains(pipelineMarker, StringComparison.Ordinal))
        {
            static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            var endpointStart = program.IndexOf("var studioPipeline = new HyperlibProjectPipelineService();", StringComparison.Ordinal);
            var operationsStart = program.IndexOf("app.MapPost(\"/api/operations\"", endpointStart >= 0 ? endpointStart : 0, StringComparison.Ordinal);
            var hookStart = program.IndexOf("var stagedPipeline = await studioPipeline.ReadAsync(StudioWorkRoot(), ct);", StringComparison.Ordinal);
            var hookEnd = program.IndexOf("var state = await store.ApplyAsync(batch, ct);", hookStart >= 0 ? hookStart : 0, StringComparison.Ordinal);
            if (endpointStart < 0 || operationsStart <= endpointStart || hookStart < 0 || hookEnd <= hookStart
                || Normalize(program[endpointStart..operationsStart]) != Normalize(PipelineEndpoints)
                || Normalize(program[hookStart..hookEnd]) != Normalize(CreatePageFromStagedLayout)
                    && Normalize(program[hookStart..hookEnd]) != Normalize(LegacyCreatePageHook())
                || program.IndexOf(pipelineMarker, program.IndexOf(pipelineMarker, StringComparison.Ordinal) + pipelineMarker.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException("This source library's Studio gateway has custom pipeline hooks. Keep the original source and use an unmodified portable Studio gateway to create this Work.");
            // Only a recognizable older generated Page hook is updated, within this new unpublished copy.
            program = program[..hookStart] + CreatePageFromStagedLayout + "\n    " + program[hookEnd..];
        }
        else
        {
            program = program.Replace("app.MapPost(\"/api/operations\", async (OperationBatch batch, CancellationToken ct) =>",
                PipelineEndpoints + "\napp.MapPost(\"/api/operations\", async (OperationBatch batch, CancellationToken ct) =>", StringComparison.Ordinal);
            program = program.Replace("var state = await store.ApplyAsync(batch, ct);", CreatePageFromStagedLayout + "\n    var state = await store.ApplyAsync(batch, ct);", StringComparison.Ordinal);
        }
        await File.WriteAllTextAsync(programPath, program, new UTF8Encoding(false), ct);
    }

    private static async Task DisableOwnedProjectCompressionAsync(string workRoot, CancellationToken ct)
    {
        // .NET 10 starts a compression helper from each Razor project's directory. Windows cannot
        // start that process from deeply nested immutable reader snapshots. The SDK's outer StaticWebAssets
        // target imports compression only when CompressionEnabled=true; ordinary static assets remain active.
        foreach (var project in EnumerateSourceFiles(workRoot).Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var xml = System.Xml.Linq.XDocument.Load(project);
            if (xml.Root is null) throw new InvalidDataException("A project file is empty.");
            var values = xml.Descendants().Where(value => value.Name.LocalName == "CompressionEnabled").ToArray();
            if (values.Length == 1 && values[0].Attribute("Condition") is null && values[0].Value == "false") continue;
            foreach (var value in values) value.Remove();
            var ns = xml.Root.Name.Namespace;
            xml.Root.Add(new System.Xml.Linq.XElement(ns + "PropertyGroup", new System.Xml.Linq.XElement(ns + "CompressionEnabled", "false")));
            await File.WriteAllTextAsync(project, xml.ToString(), new UTF8Encoding(false), ct);
        }
    }

    private const string PipelineEndpoints = """
        var studioPipeline = new HyperlibProjectPipelineService();
        string StudioWorkRoot()
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".studio", "hyperlib-work.json")));
            var id = marker.RootElement.GetProperty("workId").GetString()!;
            TemplateValidator.ValidateId(id, "work");
            return HyperlibProjectPipelineService.Guard(root, "Works/" + id);
        }
        app.MapGet("/api/studio/pipeline", async (CancellationToken ct) =>
            Results.Json(await studioPipeline.ReadAsync(StudioWorkRoot(), ct), ContentJson.Options));
        app.MapPut("/api/studio/page-templates", async (HyperlibPageTemplatesUpdateRequest request, CancellationToken ct) =>
        {
            var updated = await studioPipeline.UpdateTemplatesAsync(StudioWorkRoot(), request, ct);
            await PublishCommitted(await store.ReadAsync(ct));
            return Results.Json(updated, ContentJson.Options);
        });
        """;

    private const string CreatePageFromStagedLayout = """
        var stagedPipeline = await studioPipeline.ReadAsync(StudioWorkRoot(), ct);
        if (stagedPipeline is not null)
        {
            foreach (var operation in batch.Operations.Where(item => item.Kind == "createNode" && item.NodeType == "page"))
            {
                var before = await store.ReadAsync(ct);
                var parent = before.Nodes.FirstOrDefault(node => node.Id == operation.ParentId);
                var belongs = operation.ParentId == stagedPipeline.WorkId || parent?.WorkId == stagedPipeline.WorkId;
                if (!belongs) continue;
                var template = TemplateObjectLibrary.ReadDefinition(StudioWorkRoot());
                var selectedId = operation.Appearance?.TryGetValue("studioPageTemplateId", out var selected) == true
                    && selected.ValueKind == JsonValueKind.String ? selected.GetString() : null;
                var selectedTemplate = HyperlibProjectPipelineService.GetPageTemplate(stagedPipeline, selectedId);
                if (operation.Page is not null) continue;
                operation.Page = new() { TemplateId = template.TemplateId, UsesEditableChrome = true, Blocks = ContentJson.Clone(selectedTemplate.Blocks) };
                var appearance = ContentJson.Clone(selectedTemplate.Appearance);
                appearance["studioPageTemplateId"] = ContentJson.Value(selectedTemplate.Id);
                if (operation.Appearance is not null) foreach (var setting in operation.Appearance) appearance[setting.Key] = setting.Value;
                operation.Appearance = appearance;
            }
        }
        """;

    private const string StagedLayoutCss = """
        /* studio-guided-regions-v2 */
        .studio-region-chrome { grid-column:1/-1; min-width:0; display:flex; align-items:center; justify-content:var(--studio-justify,flex-start); gap:16px; }
        .studio-region-chrome[data-logo-placement="center"] { flex-direction:column; text-align:center; }
        .studio-region-chrome[data-logo-placement="right"] { flex-direction:row-reverse; }
        .studio-region-logo { width:auto!important; max-width:64px!important; max-height:64px!important; flex:0 0 auto; object-fit:contain; }
        .studio-region-text { flex:1; min-width:0; overflow-wrap:anywhere; margin:0; white-space:pre-wrap; font:inherit; }
        .studio-staged-layout { background:var(--hl-paper); min-height:70vh; display:grid; grid-template-columns:minmax(0,1fr); gap:24px; max-width:980px; margin-inline:auto; }
        .studio-staged-layout[data-studio-width="wide"] { max-width:1280px; }
        .studio-staged-layout[data-studio-width="full"] { max-width:none; }
        .studio-staged-layout[data-studio-alignment="center"] [data-region="main"] { text-align:center; }
        .studio-staged-layout[data-studio-alignment="right"] [data-region="main"] { text-align:right; }
        .studio-staged-layout [data-region="header"] { grid-area:header; }
        .studio-staged-layout [data-region="sidebar"] { grid-area:sidebar; }
        .studio-staged-layout [data-region="main"] { grid-area:main; }
        .studio-staged-layout [data-region="footer"] { grid-area:footer; }
        .studio-staged-layout { grid-template-areas:"header" "sidebar" "main" "footer"; }
        .studio-staged-layout[data-studio-navigation="left"] { grid-template-columns:220px minmax(0,1fr); grid-template-areas:"header header" "sidebar main" "footer footer"; }
        .studio-staged-layout[data-studio-navigation="right"] { grid-template-columns:minmax(0,1fr) 220px; grid-template-areas:"header header" "main sidebar" "footer footer"; }
        .studio-staged-layout [data-region="sidebar"] .hl-work-navigation { margin:0; padding:0; border:0; }
        .studio-staged-layout [data-region="sidebar"] .hl-work-navigation ul { flex-direction:column; }
        .studio-staged-layout [data-region="sidebar"] .hl-work-navigation a { display:block; }
        @media(max-width:760px) { .studio-staged-layout[data-studio-navigation] { grid-template-columns:minmax(0,1fr); grid-template-areas:"header" "sidebar" "main" "footer"; } }
        """;

    private static string PipelineReadme(string title, string workId) => $$"""
        # {{title}}

        This project separates the tools you chose, the reusable layouts you staged, and the content you author. It has **no prewritten content Pages**. The Work landing is an empty editable scaffold, not a finished Page. The original Core Template Library and your other projects remain independent.

        ## Phase 1 — Shop your toolkit

        Choose **1 · Library** in the Studio toolbar to revisit the collection. Your project owns the exact objects and source modules selected from the Core Template Library, including saved Templates, Design Groups, and their dependencies. The project's toolkit is recorded in `.studio/pipeline.json`; objects retain their original collection names. In **3 · Authoring**, use the writer's **Elements** panel to choose a collected object, then populate its fields. Elements may be placed in header, side navigation, main content, or footer regions. Removing a collection choice hides it from the palette while retaining the source and any existing authored objects that use it.

        ## Phase 2 — Stage reusable Page templates

        Build the **Work landing** first, one step at a time: **Background → Header → Navigation → Body → Footer → Extras**. Each step offers only relevant tools and a live Page preview. Set colors, typography, border, padding, alignment, reusable chrome text, and a logo. Imported logos are copied into this independent project's Media folder.

        Repeat these steps for as many named reusable **Page templates** as your site needs, then finish building and enter Authoring. These recipes live in `.studio/page-templates.json`. The builder defines repeating structure and visual chrome; article text and Page media remain empty until Authoring. No content Page count is chosen in advance.

        **Background** offers a plain color, your own picture, or ten curated surfaces. Pictures support **Cover**, **Contain**, and **Tile** and are copied into this project's Media folder. Set the outer Page border's color, style, thickness, and corner radius here; these controls style the whole Page rather than its individual content regions. The saved `backgroundSurface` recipe and base background color stay with each named template, so subsequent Pages can reuse the same foundation.

        **Header** offers ten editable foundations or an empty builder. Add rows, set their heights, then add text, boxes, images, or video. Element widths and horizontal/vertical positions are percentages of their containing row or box; element heights are pixels, with zero meaning automatic. Boxes can contain more elements. Select each element to set its text, font, colors, border, spacing, or media. Imported header media belongs to this project. Save the reusable header to update assigned Pages: untouched geometry follows the shared layout, while independently authored text, media, and citations remain with each Page. Removing a populated row retains its authored content as an independent Page object.

        **Block Placement** checked arranges elements **side by side**. Uncheck it to position elements freely using the X and Y controls within their containing Block or Box. New Blocks begin side by side; new Boxes begin with free positioning. Reordering moves the actual preview in either mode. Existing saved layouts retain their arrangement until you change it. Width can use responsive percentages or exact pixels; new images and videos begin at **128 × 96 px**. Element placement (left, center, or right) is separate from text alignment. Changing arrangements preserves each element's saved size and free-position coordinates.

        With **Block Placement** unchecked, drag an element directly in Studio's live Page builder; interactive objects provide a drag grip so their controls remain usable. The X and Y fields provide fine adjustments. Coordinates **0, 0** start at the containing Block or Box's inner padding edge, preserving its chosen border and padding. Oversized images and video fit within that positioned content area, and contained media aligns to its selected left, center, or right position along the top edge. A completed drop creates one Undo step; moving the pointer does not create intermediate saved changes.

        **Navigation** follows Header. Start from one of six editable layouts or create your own Blocks, Boxes, text, media, and Navigation links. Automatic links follow the Work's real Pages, including Pages added later; the Home link uses the Work landing Page. Custom links can use `page:PAGE-ID`, a real `#anchor`, a rooted path, or an HTTP(S), mailto, or tel address. Set each links element's spacing, direction, alignment, normal/hover/active colors, and narrow-screen Menu behavior. Sticky mode keeps the Navigation region visible while the Page body scrolls. Other projects and the source collection stay independent.

        **Body** defines a reusable layout, toolset, and content slots. Start empty or choose an editable foundation, then arrange structural Blocks and nested Boxes. Add text, image, video, or real library Template slots where future content belongs. Give each slot a clear name and optional guidance; these labels explain the layout in the private builder and are not reader content. Set sizes, alignment, colors, padding, and borders here; write the actual Page text and choose its media later in Authoring. With Block Placement unchecked, drag or resize elements in the live builder; one completed gesture creates one Undo step. Existing saved elements keep their values unless you explicitly edit them. Each Page receives its own independent slot values. Saving the reusable layout updates assigned Pages while preserving authored fields, media, and citations; removing a populated Block retains its authored content as an independent Page object.

        **Footer** uses the same Block and nested element builder at the bottom of the Page. Choose a foundation or add your own text, Boxes, media, and library objects. Its Page-navigation element can provide Home and Previous/Next links that follow the Work's actual Page order, without showing the full Page list. The first Page omits Previous and the last omits Next. Keep this footer on a reusable Page template so later Pages inherit its layout. Disabling the Footer builder restores retained legacy footer choices; independently authored footer objects and values remain protected during template updates.

        Choose **Template** from **+ Element** or **+ Inside box** to browse the whole library. Visual objects and layouts become real editable components, including interactive tools. Colors and typography create a styled Box at the chosen location. Body content slots choose the object and its structural settings here; their actual text and media are filled per Page in Authoring. Each instance has independent values. Required component source, scripts, styles, and imported media are copied into this project, so its reusable layouts remain portable. Later template updates follow unchanged fields while retaining independently authored Page overrides.

        Choose **2 · Staging** to revisit the landing or any named reusable template. Layout changes propagate only to Pages assigned to that template. Authored text, media values, independent objects, and source citations are preserved; a populated slot removed from the template becomes independent Page content. Unchanged structural defaults follow the new recipe; an explicit per-Page override stays with its Page. The source template collection is never changed by these edits. Undo remains available for content changes. A moved project can restage its owned toolkit even if the original collection is unavailable; adding new collection objects requires access to their source library.

        ## Phase 3 — Author the Work in Hyperpage Studio

        If you chose **Begin with a template**, this project opens directly in Authoring with the complete original page design, example content, colors, typography, media, and real interactive objects retained. Edit the landing Page first, then use **New Page** to clone an independent copy of its reusable Content Page foundation. Each Page owns its edited text, media, tool values, and citations. The step-by-step scratch wizard is optional for this route; its empty-slot styling is not applied over the original design. Imported components and their local dependencies belong to this project, while the original template remains unchanged.

        1. Open this folder in Hyperpage Studio, then choose **3 · Authoring**. The lower **Authoring** tab contains the Work outline, live Page preview, and content tools.
        2. Select **Work landing** and begin adding its actual title, introduction, logo, pictures, and other content.
        3. Choose **New Page** in the writer's left navigation whenever you need another Page. Choose a named Page template; each new Page independently clones that template. Its `studioPageTemplateId` assignment is retained so later foundation changes update only the matching Pages. You never need to decide the Page count in advance.
        4. Add optional Volumes, Parts, and Chapters in the outline; move and rename items using their hierarchy controls. Stable IDs keep existing navigation connected.
        5. Choose a Page, select an object, and edit its fields or type directly into supported preview fields. Import pictures through the media picker so this project owns them.
        6. Save and check the preview. Revision checks prevent one editing session from silently overwriting another. Undo/Redo operate on durable content changes.

        ## Phase 4 — Publication

        1. Save your Pages and source edits. Studio requires an acknowledged content save before opening a publication review.
        2. Choose **4 · Publication**, or the writer's **Publish this Work to Hyperlib** button.
        3. Select the destination Hyperlib library folder. The registered local reader library is offered when available. Review the destination, saved Work identity, Page count, source files, media, and any conflicts.
        4. Publish the **whole saved Work**. Its hierarchy, Pages, owned source, staged renderer SDK, and referenced media are copied into the destination. This release does not offer selective branch publication or silently overwrite an existing publication. Duplicate stable IDs or an existing destination Work folder stop the operation.
        5. Restart the Hyperlib gateway after publishing so it registers the new Work: in MyNet, right-click **Hyperlib**, choose **Stop…**, then launch Hyperlib again. Closing and reopening only the reader window reuses its existing gateway. Your open Studio project remains the independent authoring source.

        Publication to the local Hyperlib library is separate from publishing to a live site. The live site's publishing workflow is a later, explicit operation. Creating, staging, saving, or previewing this project publishes nothing automatically, and this local publication action does not deploy a site.

        ## Files and responsibilities

        | File | Purpose |
        | --- | --- |
        | `visual-page.project.json` | Opens this entire Work as one Studio project. |
        | `authoring.template.json` | Declares compiled toolkit objects, field schemas, stable regions, and the current new-Page recipe. |
        | `.studio/pipeline.json` | Chosen toolkit, reusable layouts, revision, Work identity, and landing identity. |
        | `.studio/page-templates.json` | Readable copy of the landing and named reusable Page layout recipes. Edit them through Studio so existing content stays safe. |
        | `Templates/StudioRuntime` | Project-owned rendering SDK source. Publication retains these staged-layout and Work landing behaviors without replacing the destination reader runtime. |
        | `Content/work.json` and `Content/Pages/*.json` | Readable projections; edit via the Content workspace, not by overwriting the projection. |
        | `src/Pages/Imported/*` | Project-owned compiled source modules copied from selected Design Groups. |
        | `src/Pages/wwwroot/work-theme.css` | Active shared styling and responsive staged-layout rules. |
        | `src/Pages/Blocks/WorkNavigationBlock.razor` | Automatically follows the Work's current Pages using stable IDs. |
        | `src/Pages/Blocks/StudioHeaderRowBlock.razor` | Shared live renderer for Header, Navigation, Body, and Footer Blocks with nested editable elements. |
        | `src/Pages/Generated/*` | Presentation shell managed by Studio; author real Work content in the Content workspace. |
        | `../../Media` | Project-owned imported images, video, audio, and documents. |
        | `../../Content/library.json` | Durable readable library state, backed by the private journal in `../../.hyperlib`. |

        Every supported file's **?** explains its specific responsibility. Save custom code in user-owned component, CSS, and JavaScript files. Keep the `BlockContext` contract when editing an object's Razor component. The preview host rebuilds the project copy; it does not edit the Core Template Library. If a source file is open while Staging changes it, its old editor snapshot cannot overwrite the newer file: saving stops and asks you to review the change.

        ## Run and build

        Requires .NET 10. Hyperpage Studio starts the private loopback preview automatically. To run independently, from the **outer project folder**:

        ```powershell
        dotnet run --project src/HyperLib.Host/HyperLib.Host.csproj -- --root . --port 54181
        ```

        The authoring workspace is at `http://127.0.0.1:54181/studio-authoring/index.html`. To build this Work's portable renderer:

        ```powershell
        dotnet build Works/{{workId}}/src/Host/Host.csproj
        ```

        Back up or move the **whole outer project folder**, including `Works`, `Content`, `Media`, `.studio`, `.hyperlib`, and `src`. Copying only this nested Work folder omits its private runtime and content journal. A new computer needs .NET 10 and can reopen the Work through `visual-page.project.json`.
        """;

    private static string PipelineRootReadme(string title, string workId) => $$"""
        # {{title}} — independent Hyperlib project

        Open `Works/{{workId}}` in Hyperpage Studio. Its README explains the four phases, selected toolkit, guided landing and named Page templates, content editing, preview, and publication.

        Keep this **entire folder** together. `Content` and `.hyperlib` contain durable content and undo history; `Media` holds owned uploads; `Works` holds your editable project; `src` is the copied portable runtime and gateway. No original library data is shared or changed.

        ```powershell
        dotnet run --project src/HyperLib.Host/HyperLib.Host.csproj -- --root . --port 54181
        ```

        Open `http://127.0.0.1:54181/studio-authoring/index.html` to author this private project's content. Creating the project or running the preview does not publish to another library or a live site.
        """;

    private static async Task ExtendPipelineFileGuideAsync(string workRoot, CancellationToken ct)
    {
        var path = Path.Combine(workRoot, ".studio", "project-guide.json");
        var guide = JsonSerializer.Deserialize<StudioProjectGuide>(await File.ReadAllTextAsync(path, ct), ContentJson.Options)!;
        foreach (var relative in new[] { ".studio/pipeline.json", ".studio/page-templates.json", ".studio/wizard.json" })
        {
            guide.Files.RemoveAll(file => file.RelativePath == relative);
            guide.Files.Add(new() { RelativePath = relative, Title = relative == ".studio/page-templates.json" ? "Reusable Page templates" : "Project pipeline",
                Purpose = relative == ".studio/page-templates.json" ? "Records the Work landing and named reusable Page layouts, their styles, and the default Page-template identity."
                    : "Records the project's chosen toolkit, staged layouts, stable Work and landing IDs, and pipeline revision.",
                EditGuidance = "Change reusable templates through Studio's layout controls. They preserve authored Page content and enforce revision checks.",
                GettingStarted = "Choose 3 · Authoring: fill the Work landing, then choose New Page when you need another Page. Use the writer's Elements panel to choose from your selected toolkit.", Ownership = "project configuration" });
        }
        await WriteAsync(workRoot, ".studio/project-guide.json", JsonSerializer.Serialize(guide, ContentJson.Options), ct);
    }
}
