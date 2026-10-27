using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>The durable boundary between a selected toolkit, reusable layouts, and authored Page content.</summary>
public sealed class HyperlibProjectPipelineState
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DirectEditing { get; set; }
    public int SchemaVersion { get; set; } = 2;
    public long Revision { get; set; }
    public int Phase { get; set; } = 3;
    public string WorkId { get; set; } = "";
    public string LandingPageId { get; set; } = "";
    public string WorkTitle { get; set; } = "";
    public string? SourceLibraryRoot { get; set; }
    public List<string> SelectedToolsetIds { get; set; } = [];
    public List<HyperlibProjectToolkitItem> Toolkit { get; set; } = [];
    public HyperlibReusablePageTemplate LandingTemplate { get; set; } = new();
    public HyperlibReusablePageTemplate PageTemplate { get; set; } = new();
    public List<HyperlibReusablePageTemplate> PageTemplates { get; set; } = [];
    public string DefaultPageTemplateId { get; set; } = "page-default";
}

public sealed class HyperlibProjectToolkitItem
{
    public string ChoiceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "element";
    public string Category { get; set; } = "";
    public string GroupTitle { get; set; } = "";
    public List<string> BlockTypeIds { get; set; } = [];
    public List<PageBlock> LayoutBlocks { get; set; } = [];
    public List<TemplateRegion> LayoutRegions { get; set; } = [];
    public Dictionary<string, BlockDefinition> SourceBlockDefinitions { get; set; } = [];
    public Dictionary<string, JsonElement> LayoutAppearance { get; set; } = [];
}

public sealed class HyperlibReusablePageTemplate
{
    public string Id { get; set; } = "page-default";
    public string Name { get; set; } = "Content Page";
    public HyperlibPageTemplateDefinition Definition { get; set; } = new();
    public List<PageBlock> Blocks { get; set; } = [];
    public Dictionary<string, JsonElement> Appearance { get; set; } = [];
}

public sealed class HyperlibPageTemplatesUpdateRequest
{
    public long ExpectedPipelineRevision { get; set; }
    public long ExpectedContentRevision { get; set; }
    public HyperlibPageTemplateDefinition LandingTemplate { get; set; } = new();
    public HyperlibPageTemplateDefinition PageTemplate { get; set; } = new();
    public List<HyperlibNamedPageTemplateDefinition>? PageTemplates { get; set; }
    public string? DefaultPageTemplateId { get; set; }
}

public sealed class HyperlibProjectPipelineService
{
    public const string PipelineRelativePath = ".studio/pipeline.json";
    public const string TemplatesRelativePath = ".studio/page-templates.json";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPipelineProject(string projectRoot) => File.Exists(Path.Combine(projectRoot, PipelineRelativePath));

    public async Task<HyperlibProjectPipelineState?> ReadAsync(string projectRoot, CancellationToken ct = default)
    {
        var path = Guard(projectRoot, PipelineRelativePath);
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize<HyperlibProjectPipelineState>(await File.ReadAllTextAsync(path, ct), ContentJson.Options)
            ?? throw new InvalidDataException("The project pipeline file is empty.");
        if (state.SchemaVersion != 2) throw new InvalidDataException("Unsupported project pipeline version.");
        NormalizeTemplates(state);
        return state;
    }

    public async Task<HyperlibWorkProjectDefinition?> ReadDefinitionAsync(string projectRoot, CancellationToken ct = default)
    {
        var state = await ReadAsync(projectRoot, ct);
        if (state is null) return null;
        var templates = state.PageTemplates.Select(item => new HyperlibNamedPageTemplateDefinition { Id = item.Id, Name = item.Name,
            Definition = ContentJson.Clone(item.Definition) }).ToList();
        return new() { PipelineVersion = 2, DirectEditing = state.DirectEditing, WorkTitle = state.WorkTitle, Description = "",
            SelectedToolsetIds = state.SelectedToolsetIds.ToList(), LandingTemplate = ContentJson.Clone(state.LandingTemplate.Definition),
            PageTemplates = templates, DefaultPageTemplateId = state.DefaultPageTemplateId,
            PageTemplate = templates.Single(item => item.Id == state.DefaultPageTemplateId).Definition, PageDefinitions = [] };
    }

    public async Task<HyperlibProjectPipelineState> UpdateDefinitionAsync(string projectRoot, HyperlibWorkProjectDefinition definition,
        long expectedPipelineRevision, long expectedContentRevision, CancellationToken ct = default)
    {
        var state = await ReadAsync(projectRoot, ct) ?? throw new InvalidDataException("This project has no staged templates.");
        if (!definition.SelectedToolsetIds.ToHashSet(StringComparer.Ordinal).SetEquals(state.SelectedToolsetIds))
            throw new InvalidDataException("Keep this project's selected toolkit when restaging its Page templates. Add new reusable objects through the project's library tools first.");
        return await UpdateTemplatesAsync(projectRoot, new() { ExpectedPipelineRevision = expectedPipelineRevision,
            ExpectedContentRevision = expectedContentRevision, LandingTemplate = definition.LandingTemplate, PageTemplate = definition.PageTemplate,
            PageTemplates = definition.PageTemplates.Count > 0 ? definition.PageTemplates : null, DefaultPageTemplateId = definition.DefaultPageTemplateId }, ct);
    }

    /// <summary>Updates shared layouts without deleting, replacing, or moving authored content into the template.</summary>
    public Task<HyperlibProjectPipelineState> UpdateTemplatesAsync(string projectRoot, HyperlibPageTemplatesUpdateRequest request,
        CancellationToken ct = default) => UpdatePreparedTemplatesAsync(projectRoot, request, null, ct);

    public async Task<HyperlibProjectPipelineState> UpdatePreparedTemplatesAsync(string projectRoot, HyperlibPageTemplatesUpdateRequest request,
        Func<HyperlibProjectPipelineState, TemplateDefinition, CancellationToken, Task<TemplateDefinition>>? prepareToolkit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fullRoot = Path.GetFullPath(projectRoot);
        var gate = Gates.GetOrAdd(fullRoot, _ => new(1, 1));
        await gate.WaitAsync(ct);
        string? preparedDefinitionPath = null;
        string? preparedDefinitionText = null;
        try
        {
            await using var projectLock = await AcquireProjectLockAsync(fullRoot, ct);
            var pipeline = await ReadAsync(fullRoot, ct) ?? throw new InvalidDataException("This project does not have staged Page templates.");
            if (pipeline.Revision != request.ExpectedPipelineRevision) throw new ConflictException(request.ExpectedPipelineRevision, pipeline.Revision);
            var definitionPath = Guard(fullRoot, "authoring.template.json");
            var oldDefinitionText = await File.ReadAllTextAsync(definitionPath, ct);
            var definition = JsonSerializer.Deserialize<TemplateDefinition>(oldDefinitionText, ContentJson.Options)!;
            var libraryRoot = Path.GetFullPath(Path.Combine(fullRoot, "../.."));
            _ = Guard(libraryRoot, ".studio/hyperlib-work.json");
            if (!File.Exists(Path.Combine(libraryRoot, ".studio/hyperlib-work.json"))) throw new InvalidDataException("The project's private library marker is missing.");
            var store = new ContentStore(libraryRoot) { TemplateResolver = package => package == pipeline.WorkId ? definition : null };
            var content = await store.ReadAsync(ct);
            if (content.Revision != request.ExpectedContentRevision) throw new ConflictException(request.ExpectedContentRevision, content.Revision);
            if (prepareToolkit is not null)
            {
                preparedDefinitionPath = definitionPath;
                preparedDefinitionText = oldDefinitionText;
                try { definition = await prepareToolkit(pipeline, definition, ct); }
                catch { await AtomicWriteAsync(definitionPath, oldDefinitionText, CancellationToken.None); throw; }
            }
            var landing = Compose(request.LandingTemplate, pipeline.Toolkit, definition, "landing");
            var recipes = request.PageTemplates ?? pipeline.PageTemplates.Select(item => new HyperlibNamedPageTemplateDefinition { Id = item.Id, Name = item.Name,
                Definition = item.Id == pipeline.DefaultPageTemplateId ? request.PageTemplate : ContentJson.Clone(item.Definition) }).ToList();
            var defaultId = request.DefaultPageTemplateId ?? pipeline.DefaultPageTemplateId;
            ValidateNamedTemplates(recipes, defaultId);
            var pages = recipes.Select(item => { var composed = Compose(item.Definition, pipeline.Toolkit, definition, TemplateRole(item.Id));
                composed.Id = item.Id; composed.Name = item.Name.Trim(); return composed; }).ToList();
            var page = pages.Single(item => item.Id == defaultId);
            EnsureAppearanceFields(definition);
            var operations = new List<ContentOperation>();
            foreach (var document in content.Pages.Values.Where(item => item.WorkId == pipeline.WorkId))
            {
                var isLanding = document.Id == pipeline.LandingPageId;
                var node = content.Nodes.Single(item => item.Id == document.Id);
                var assignedId = node.Appearance.TryGetValue("studioPageTemplateId", out var assigned) && assigned.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(assigned.GetString()) ? assigned.GetString()! : pipeline.DefaultPageTemplateId;
                var oldLayout = isLanding ? pipeline.LandingTemplate : GetPageTemplate(pipeline, assignedId);
                var newLayout = isLanding ? landing : pages.SingleOrDefault(item => item.Id == assignedId)
                    ?? throw new InvalidDataException("A Page uses the template '" + oldLayout.Name + "'. Keep it while those Pages use it.");
                if ((isLanding || node.Appearance.ContainsKey("studioPageTemplateId"))
                    && JsonSerializer.Serialize(oldLayout.Blocks, ContentJson.Options) == JsonSerializer.Serialize(newLayout.Blocks, ContentJson.Options)
                    && JsonSerializer.Serialize(oldLayout.Appearance, ContentJson.Options) == JsonSerializer.Serialize(newLayout.Appearance, ContentJson.Options))
                    continue;
                var merged = ContentJson.Clone(document);
                var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                merged.Blocks = MergeLayout(document.Blocks, oldLayout.Blocks, newLayout.Blocks, definition, renamed);
                foreach (var source in merged.Sources)
                    source.BlockIds = source.BlockIds.Select(id => renamed.GetValueOrDefault(id, id)).ToList();
                merged.UsesEditableChrome = true;
                operations.Add(new() { Kind = "savePage", Page = merged });
                var appearance = ContentJson.Clone(node.Appearance);
                foreach (var setting in newLayout.Appearance)
                    if (!appearance.TryGetValue(setting.Key, out var authoredSetting)
                        || oldLayout.Appearance.TryGetValue(setting.Key, out var priorSetting) && priorSetting.GetRawText() == authoredSetting.GetRawText())
                        appearance[setting.Key] = setting.Value.Clone();
                if (!isLanding) appearance["studioPageTemplateId"] = ContentJson.Value(newLayout.Id);
                operations.Add(new() { Kind = "setAppearance", Id = node.Id, Appearance = appearance });
            }
            definition.DefaultBlocks = ContentJson.Clone(page.Blocks);
            definition.EmptyPageDefault = true;
            TemplateValidator.EnsureCompatible(definition, content.Pages.Values);
            // Existing block types and fields remain declared, including ones removed from a shared layout.
            // The content journal is authoritative; a failed optimistic write restores the prior default recipe.
            await AtomicWriteAsync(definitionPath, JsonSerializer.Serialize(definition, ContentJson.Options), ct);
            try
            {
                if (operations.Count > 0) await store.ApplyAsync(new() { ExpectedRevision = content.Revision,
                    Description = "Update shared Page templates while preserving authored content", Operations = operations }, ct);
            }
            catch
            {
                await AtomicWriteAsync(definitionPath, oldDefinitionText, CancellationToken.None);
                throw;
            }
            pipeline.LandingTemplate = landing;
            pipeline.PageTemplate = page;
            pipeline.PageTemplates = pages;
            pipeline.DefaultPageTemplateId = defaultId;
            pipeline.Revision++;
            preparedDefinitionPath = null;
            await SaveAsync(fullRoot, pipeline, CancellationToken.None);
            return pipeline;
        }
        catch
        {
            if (preparedDefinitionPath is not null) await AtomicWriteAsync(preparedDefinitionPath, preparedDefinitionText!, CancellationToken.None);
            throw;
        }
        finally { gate.Release(); }
    }

    public static async Task SaveAsync(string projectRoot, HyperlibProjectPipelineState state, CancellationToken ct = default)
    {
        NormalizeTemplates(state);
        await AtomicWriteAsync(Guard(projectRoot, PipelineRelativePath), JsonSerializer.Serialize(state, ContentJson.Options), ct);
        await AtomicWriteAsync(Guard(projectRoot, TemplatesRelativePath), JsonSerializer.Serialize(new { schemaVersion = 2, revision = state.Revision,
            workLanding = state.LandingTemplate, contentPage = state.PageTemplate, pageTemplates = state.PageTemplates, defaultPageTemplateId = state.DefaultPageTemplateId }, ContentJson.Options), ct);
    }

    public static HyperlibReusablePageTemplate Compose(HyperlibPageTemplateDefinition recipe,
        IReadOnlyList<HyperlibProjectToolkitItem> toolkit, TemplateDefinition definition, string role)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe = ContentJson.Clone(recipe);
        if(recipe.NavigationLayout.Enabled||new[]{recipe.HeaderLayout,recipe.BodyLayout,recipe.FooterLayout}.SelectMany(HyperlibHeaderLayouts.Elements).Any(element=>element.Kind=="navigation"))HyperlibNavigationLayouts.EnsureBlockDefinitions(definition);
        ResolveHeaderTemplates(recipe, toolkit, definition);
        ValidateRecipe(recipe, toolkit);
        if (!string.IsNullOrWhiteSpace(recipe.CompleteTemplateObjectId))
            return ComposeCompleteTemplate(recipe, toolkit, definition, role);
        if (recipe.HeaderLayout.Enabled) HyperlibHeaderLayouts.EnsureBlockDefinition(definition);
        if (recipe.BodyLayout.Enabled) HyperlibHeaderLayouts.EnsureBlockDefinition(definition, HyperlibHeaderLayouts.BodyBlockType, "Body");
        if (recipe.FooterLayout.Enabled) HyperlibHeaderLayouts.EnsureBlockDefinition(definition, HyperlibHeaderLayouts.FooterBlockType, "Footer");
        var result = new HyperlibReusablePageTemplate { Definition = ContentJson.Clone(recipe) };
        if (!string.IsNullOrWhiteSpace(recipe.BackgroundTemplateId))
            foreach (var setting in toolkit.Single(item => item.ChoiceId == recipe.BackgroundTemplateId).LayoutAppearance)
                result.Appearance[setting.Key] = setting.Value.Clone();
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string? choiceId, string region, string slot)
        {
            if (string.IsNullOrWhiteSpace(choiceId)) return;
            var item = toolkit.Single(choice => choice.ChoiceId == choiceId);
            if (item.Kind is not ("element" or "preset")) throw new InvalidDataException("Place elements or configured elements in layout slots.");
            foreach (var type in item.BlockTypeIds)
            {
                var schema = definition.Blocks.Single(block => block.Id == type);
                var key = slot + "-" + slots.GetValueOrDefault(slot);
                slots[slot] = slots.GetValueOrDefault(slot) + 1;
                result.Blocks.Add(new() { Id = "stage-" + role + "-" + key + "-" + TypeKey(type), Type = type, Region = region, Values = EmptyContentValues(schema) });
            }
        }
        if (recipe.LayoutTemplateId is { Length: > 0 } layoutId)
        {
            var layout = toolkit.Single(choice => choice.ChoiceId == layoutId);
            if (layout.Kind != "layout") throw new InvalidDataException("The selected layout is not a Page layout.");
            foreach (var original in layout.LayoutBlocks)
            {
                if (recipe.HeaderLayout.Enabled && (original.Region.Contains("header", StringComparison.OrdinalIgnoreCase)
                    || definition.Blocks.Single(schema => schema.Id == original.Type).Component.Contains("Header", StringComparison.OrdinalIgnoreCase))) continue;
                if (recipe.FooterLayout.Enabled && (original.Region.Contains("footer", StringComparison.OrdinalIgnoreCase)
                    || definition.Blocks.Single(schema => schema.Id == original.Type).Component.Contains("Footer", StringComparison.OrdinalIgnoreCase))) continue;
                var block = ContentJson.Clone(original);
                block.Id = "stage-" + role + "-layout-" + result.Blocks.Count + "-" + TypeKey(block.Type);
                block.Region = original.Region.Contains("header", StringComparison.OrdinalIgnoreCase) ? "header"
                    : original.Region.Contains("footer", StringComparison.OrdinalIgnoreCase) ? "footer"
                    : original.Region.Contains("aside", StringComparison.OrdinalIgnoreCase) || original.Region.Contains("sidebar", StringComparison.OrdinalIgnoreCase) ? "sidebar" : "main";
                if(recipe.BodyLayout.Enabled&&block.Region=="main")continue;
                block.Values = EmptyContentValues(definition.Blocks.Single(schema => schema.Id == block.Type));
                var blockSchema = definition.Blocks.Single(schema => schema.Id == block.Type);
                foreach (var setting in original.Values)
                    if (blockSchema.Fields.Any(field => field.Id == setting.Key && field.Type is "select" or "number" or "boolean" or "color"))
                        block.Values[setting.Key] = setting.Value.Clone();
                result.Blocks.Add(block);
            }
            foreach (var setting in layout.LayoutAppearance) result.Appearance[setting.Key] = setting.Value.Clone();
        }
        if (recipe.HeaderPlacement == "top")
        {
            if (recipe.HeaderLayout.Enabled)
                foreach (var row in recipe.HeaderLayout.Blocks)
                    result.Blocks.Add(new() { Id = "stage-" + role + "-header-" + TypeKey(row.Id), Type = HyperlibHeaderLayouts.BlockType, Region = "header",
                        Values = new() { ["layout"] = ContentJson.Value(JsonSerializer.Serialize(row, ContentJson.Options)) } });
            else Add(recipe.HeaderTemplateId, "header", "header");
        }
        if (recipe.BodyLayout.Enabled)
            foreach(var row in recipe.BodyLayout.Blocks)
                result.Blocks.Add(new(){Id="stage-"+role+"-body-"+TypeKey(row.Id),Type=HyperlibHeaderLayouts.BodyBlockType,Region="main",
                    Values=new(){["layout"]=ContentJson.Value(JsonSerializer.Serialize(row,ContentJson.Options)),["studioRegion"]=ContentJson.Value("body")}});
        if (!recipe.BodyLayout.Enabled && !string.IsNullOrWhiteSpace(recipe.BodyLayoutTemplateId))
        {
            var bodyLayout = toolkit.Single(choice => choice.ChoiceId == recipe.BodyLayoutTemplateId);
            if (bodyLayout.Kind != "layout") throw new InvalidDataException("Choose a body layout from the layout collection.");
            foreach (var original in bodyLayout.LayoutBlocks)
            {
                var schema = definition.Blocks.Single(block => block.Id == original.Type);
                var component = schema.Component.Split('.').Last();
                if (original.Region.Contains("header", StringComparison.OrdinalIgnoreCase) || original.Region.Contains("footer", StringComparison.OrdinalIgnoreCase)
                    || original.Region.Contains("aside", StringComparison.OrdinalIgnoreCase) || original.Region.Contains("sidebar", StringComparison.OrdinalIgnoreCase)
                    || component.Contains("Header", StringComparison.OrdinalIgnoreCase) || component.Contains("Footer", StringComparison.OrdinalIgnoreCase)
                    || component.Contains("Navigation", StringComparison.OrdinalIgnoreCase)) continue;
                var copy = ContentJson.Clone(original);
                copy.Id = "stage-" + role + "-body-layout-" + result.Blocks.Count + "-" + TypeKey(copy.Type);
                copy.Region = "main";
                copy.Values = EmptyContentValues(schema);
                foreach (var setting in original.Values)
                    if (schema.Fields.Any(field => field.Id == setting.Key && field.Type is "select" or "number" or "boolean" or "color"))
                        copy.Values[setting.Key] = setting.Value.Clone();
                result.Blocks.Add(copy);
            }
        }
        if(!recipe.BodyLayout.Enabled)Add(recipe.BodyTemplateId, "main", "body");
        foreach (var item in recipe.RepeatingObjects.Where(item=>(!recipe.BodyLayout.Enabled||item.Region!="main")&&(!recipe.FooterLayout.Enabled||item.Region!="footer")).OrderBy(item => item.Order)) Add(item.TemplateObjectId, item.Region,
            "repeat-" + TypeKey(string.IsNullOrWhiteSpace(item.Id) ? item.TemplateObjectId : item.Id));
        if (recipe.FooterPlacement == "bottom")
        {
            if(recipe.FooterLayout.Enabled)
                foreach(var row in recipe.FooterLayout.Blocks)
                    result.Blocks.Add(new(){Id="stage-"+role+"-footer-"+TypeKey(row.Id),Type=HyperlibHeaderLayouts.FooterBlockType,Region="footer",
                        Values=new(){["layout"]=ContentJson.Value(JsonSerializer.Serialize(row,ContentJson.Options)),["studioRegion"]=ContentJson.Value("footer")}});
            else Add(recipe.FooterTemplateId, "footer", "footer");
        }
        if (recipe.IncludeNavigation && recipe.NavigationPlacement != "none")
        {
            if(recipe.NavigationLayout.Enabled)
            {
                if(!definition.Regions.Any(region=>region.Id=="navigation"))definition.Regions.Insert(Math.Min(1,definition.Regions.Count),new(){Id="navigation",Title="Navigation"});
                var json=JsonSerializer.Serialize(recipe.NavigationLayout,ContentJson.Options);
                if(json.Length>200_000||ContentJson.Value(json).GetRawText().Length>400_000)throw new InvalidDataException("This Navigation layout exceeds the existing content field limit. Use fewer elements in this shared bar.");
                result.Blocks.Add(new(){Id="stage-"+role+"-navigation-layout",Type=HyperlibNavigationLayouts.LayoutBlockType,Region="navigation",Values=new(){["layout"]=ContentJson.Value(json),["sticky"]=ContentJson.Value(recipe.NavigationSticky)}});
            }
            else
            {
                var region = recipe.NavigationPlacement is "left" or "right" ? "sidebar" : recipe.NavigationPlacement == "bottom" ? "footer" : "header";
                Add(recipe.NavigationTemplateId, region, "navigation-object");
                result.Blocks.Add(new() { Id = "stage-" + role + "-navigation", Type = "studio-navigation", Region = region,
                    Values = new() { ["autoPages"] = ContentJson.Value(true), ["current"] = ContentJson.Value(""), ["items"] = ContentJson.Value("") } });
            }
        }
        foreach (var setting in Appearance(recipe)) result.Appearance[setting.Key] = setting.Value.Clone();
        TemplateValidator.ValidatePage(definition, new() { Id = "stage", WorkId = "stage", Title = "Staged layout", TemplateId = definition.TemplateId, Blocks = result.Blocks });
        return result;
    }

    private static HyperlibReusablePageTemplate ComposeCompleteTemplate(HyperlibPageTemplateDefinition recipe,
        IReadOnlyList<HyperlibProjectToolkitItem> toolkit, TemplateDefinition definition, string role)
    {
        var selected = toolkit.Single(item => item.ChoiceId == recipe.CompleteTemplateObjectId);
        if (selected.Kind is not ("layout" or "element" or "preset")) throw new InvalidDataException("Choose a complete page layout or a complete Hyperpage component.");
        EnsureAppearanceFields(definition);
        foreach (var region in selected.LayoutRegions.Where(region => selected.LayoutBlocks.Any(block => block.Region == region.Id)))
            if (!definition.Regions.Any(existing => existing.Id == region.Id)) definition.Regions.Add(ContentJson.Clone(region));
        var result = new HyperlibReusablePageTemplate { Definition = ContentJson.Clone(recipe) };
        if (!string.IsNullOrWhiteSpace(recipe.CompleteTemplateAppearanceId))
            foreach (var (key, value) in toolkit.Single(item => item.ChoiceId == recipe.CompleteTemplateAppearanceId).LayoutAppearance)
                result.Appearance[key] = value.Clone();
        else
        {
            // The original Hyperpage component scopes its full token theme internally; its outer canvas must match it too.
            result.Appearance["background"] = ContentJson.Value(recipe.BackgroundColor);
            result.Appearance["accent"] = ContentJson.Value(recipe.AccentColor);
        }
        foreach (var (key, value) in selected.LayoutAppearance) result.Appearance[key] = value.Clone();
        var originals = selected.Kind == "layout" ? selected.LayoutBlocks
            : selected.BlockTypeIds.Select(type => new PageBlock { Id = "complete", Type = type, Region = "main" }).ToList();
        if (originals.Count == 0) throw new InvalidDataException("The selected complete template contains no page content.");
        foreach (var original in originals)
        {
            var schema = definition.Blocks.Single(item => item.Id == original.Type);
            var block = ContentJson.Clone(original);
            block.Id = "complete-" + role + "-" + TypeKey(original.Id + ":" + original.Type);
            var values = schema.Fields.Where(field => field.DefaultValue.HasValue)
                .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);
            foreach (var (key, value) in original.Values) values[key] = value.Clone();
            block.Values = values;
            result.Blocks.Add(block);
        }
        result.Appearance["studioCompleteTemplate"] = ContentJson.Value(true);
        // Gallery originals own their complete layout. Wizard chrome and empty-slot styling stay dormant.
        foreach (var region in new[] { "Header", "Body", "Navigation", "Footer", "Extras" })
        {
            result.Appearance["studio" + region + "Enabled"] = ContentJson.Value(false);
            result.Appearance["studio" + region + "Css"] = ContentJson.Value("");
            result.Appearance["studio" + region + "Text"] = ContentJson.Value("");
            result.Appearance["studio" + region + "Logo"] = ContentJson.Value("");
        }
        TemplateValidator.ValidatePage(definition, new() { Id = "complete", WorkId = "complete", Title = "Complete template", TemplateId = definition.TemplateId, Blocks = result.Blocks });
        return result;
    }

    /// <summary>Hydrates real portable components from owned imports; no source collection access is required.</summary>
    public static void ResolveHeaderTemplates(HyperlibPageTemplateDefinition recipe,
        IReadOnlyList<HyperlibProjectToolkitItem> toolkit, TemplateDefinition definition, bool allowLocalMedia = false)
    {
        foreach (var element in new[]{recipe.HeaderLayout,recipe.NavigationLayout,recipe.BodyLayout,recipe.FooterLayout}.SelectMany(HyperlibHeaderLayouts.Elements).Where(item => item.Kind == "template"))
        {
            var selected = toolkit.SingleOrDefault(item => item.ChoiceId == element.TemplateObjectId)
                ?? throw new InvalidDataException("Add this header's library object to the project toolkit before staging it.");
            if (selected.Kind is not ("element" or "preset" or "layout"))
                throw new InvalidDataException("Use a visual element, configured element, or layout inside a header. Apply colors and typography to a Box.");
            var originals = selected.Kind == "layout" ? selected.LayoutBlocks
                : selected.BlockTypeIds.Select(type => new PageBlock { Id = "element", Type = type, Region = "header" }).ToList();
            if (originals.Count == 0) throw new InvalidDataException("This library object has no visual components to place in a header.");
            if (element.TemplateValues.Keys.Any(key => !originals.Any(block => block.Id == key)))
                throw new InvalidDataException("A header template field refers to a source block that is no longer present.");
            element.TemplateBlocks = [];
            foreach (var original in originals)
            {
                var schema = definition.Blocks.SingleOrDefault(item => item.Id == original.Type)
                    ?? throw new InvalidDataException("A header library object's component schema is missing from the owned project.");
                if (schema.Id is HyperlibHeaderLayouts.BlockType or HyperlibHeaderLayouts.BodyBlockType or HyperlibHeaderLayouts.FooterBlockType or HyperlibNavigationLayouts.LayoutBlockType) throw new InvalidDataException("A builder row cannot recursively contain itself as a library component.");
                var values = schema.Fields.Where(field => field.DefaultValue.HasValue)
                    .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);
                foreach (var (key, value) in original.Values) values[key] = value.Clone();
                if (element.TemplateValues.TryGetValue(original.Id, out var overrides))
                    foreach (var (key, value) in overrides) values[key] = value.Clone();
                var validation = ContentJson.Clone(values);
                if (allowLocalMedia)
                    foreach (var field in schema.Fields.Where(field => field.Type == "media"))
                        if (validation.TryGetValue(field.Id, out var value) && value.ValueKind == JsonValueKind.String
                            && Path.IsPathFullyQualified(value.GetString()!) && !value.GetString()!.StartsWith("/", StringComparison.Ordinal)) validation[field.Id] = ContentJson.Value("");
                TemplateValidator.ValidateValues(schema.Fields, validation, "header template '" + selected.Title + "'");
                element.TemplateBlocks.Add(new() { Id = "ht-" + TypeKey(element.Id + ":" + original.Id), Type = original.Type, Region = "header", Values = values });
            }
            element.TemplateTitle = selected.Title;
            var appearance = ContentJson.Clone(selected.LayoutAppearance);
            foreach (var (key, value) in element.TemplateAppearance) appearance[key] = value.Clone();
            element.TemplateAppearance = appearance;
        }
        HyperlibHeaderLayouts.Validate(recipe.HeaderLayout, allowLocalMedia);
        HyperlibHeaderLayouts.Validate(recipe.NavigationLayout, allowLocalMedia);
        HyperlibHeaderLayouts.Validate(recipe.BodyLayout, allowLocalMedia);
        HyperlibHeaderLayouts.Validate(recipe.FooterLayout, allowLocalMedia);
    }

    public static void ValidateRecipe(HyperlibPageTemplateDefinition recipe, IReadOnlyList<HyperlibProjectToolkitItem> toolkit)
    {
        if (recipe.RepeatingObjects is null || recipe.RepeatingObjects.Count > 100) throw new InvalidDataException("A layout supports at most 100 repeating elements.");
        foreach (var color in new[] { recipe.BackgroundColor, recipe.AccentColor })
            if (!Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Choose a six-digit hexadecimal layout color.");
        _ = HyperlibBackgroundDesigns.SurfaceCss(recipe.BackgroundColor, recipe.BackgroundSurface);
        HyperlibHeaderLayouts.Validate(recipe.HeaderLayout);
        HyperlibHeaderLayouts.Validate(recipe.NavigationLayout);
        HyperlibHeaderLayouts.Validate(recipe.BodyLayout);
        HyperlibHeaderLayouts.Validate(recipe.FooterLayout);
        if (recipe.ContentAlignment is not ("left" or "center" or "right") || recipe.ContentWidth is not ("comfortable" or "wide" or "full")
            || recipe.HeaderPlacement is not ("top" or "none") || recipe.FooterPlacement is not ("bottom" or "none")
            || recipe.NavigationPlacement is not ("top" or "left" or "right" or "bottom" or "none")) throw new InvalidDataException("Choose one of the supported layout positions.");
        foreach (var style in RegionStyles(recipe)) ValidateRegionStyle(style.Style);
        var placementIds = recipe.RepeatingObjects.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id).ToArray();
        foreach (var id in placementIds) TemplateValidator.ValidateId(id, "template placement");
        if (placementIds.Distinct(StringComparer.Ordinal).Count() != placementIds.Length) throw new InvalidDataException("Repeating template placement IDs must be unique.");
        var ids = new[] { recipe.CompleteTemplateObjectId, recipe.CompleteTemplateAppearanceId, recipe.BackgroundTemplateId, recipe.LayoutTemplateId, recipe.BodyLayoutTemplateId, recipe.HeaderTemplateId, recipe.FooterTemplateId, recipe.NavigationTemplateId, recipe.BodyTemplateId }
            .Where(id => !string.IsNullOrWhiteSpace(id)).Concat(recipe.RepeatingObjects.Select(item => item.TemplateObjectId))
            .Concat(new[]{recipe.HeaderLayout,recipe.NavigationLayout,recipe.BodyLayout,recipe.FooterLayout}.SelectMany(HyperlibHeaderLayouts.Elements).Where(item => item.Kind == "template").Select(item => item.TemplateObjectId));
        if (ids.Any(id => !toolkit.Any(item => item.ChoiceId == id))) throw new InvalidDataException("A staged layout can only use objects in this project's selected toolkit.");
        if (recipe.RepeatingObjects.Any(item => item.Region is not ("header" or "main" or "sidebar" or "footer") || item.Order < 0 || item.Order > 1000))
            throw new InvalidDataException("Repeating elements need a supported region and order.");
    }

    public static Dictionary<string, JsonElement> Appearance(HyperlibPageTemplateDefinition recipe)
    {
        var dark = (Convert.ToInt32(recipe.BackgroundColor.Substring(1, 2), 16) * .299 + Convert.ToInt32(recipe.BackgroundColor.Substring(3, 2), 16) * .587
            + Convert.ToInt32(recipe.BackgroundColor.Substring(5, 2), 16) * .114) < 140;
        var result = new Dictionary<string, JsonElement> { ["background"] = ContentJson.Value(recipe.BackgroundColor), ["accent"] = ContentJson.Value(recipe.AccentColor),
            ["ink"] = ContentJson.Value(dark ? "#f2f5f4" : "#202b26"), ["muted"] = ContentJson.Value(dark ? "#b5c3bc" : "#56645d"),
            ["surface"] = ContentJson.Value(dark ? "#22312a" : "#ffffff"), ["studioAlignment"] = ContentJson.Value(recipe.ContentAlignment),
            ["studioWidth"] = ContentJson.Value(recipe.ContentWidth), ["studioNavigation"] = ContentJson.Value(recipe.NavigationPlacement),
            ["studioBackgroundCss"] = ContentJson.Value(HyperlibBackgroundDesigns.SurfaceCss(recipe.BackgroundColor, recipe.BackgroundSurface)),
            ["studioBackgroundImage"] = ContentJson.Value(recipe.BackgroundSurface.ImagePath),
            ["studioHeaderBuilder"] = ContentJson.Value(recipe.HeaderLayout.Enabled), ["studioCompleteTemplate"] = ContentJson.Value(false) };
        if (recipe.DirectEditing) result["studioDirectEditing"] = ContentJson.Value(true);
        result["studioNavigationBuilder"]=ContentJson.Value(recipe.NavigationLayout.Enabled);
        result["studioBodyBuilder"]=ContentJson.Value(recipe.BodyLayout.Enabled);
        result["studioFooterBuilder"]=ContentJson.Value(recipe.FooterLayout.Enabled);
        foreach (var (name, style) in RegionStyles(recipe))
        {
            result["studio" + name + "Enabled"] = ContentJson.Value(name == "Header" ? recipe.HeaderPlacement == "top"
                : name == "Footer" ? recipe.FooterPlacement == "bottom" : name == "Navigation" ? recipe.IncludeNavigation && recipe.NavigationPlacement != "none" : false);
            var builder=name=="Header"&&recipe.HeaderLayout.Enabled||name=="Navigation"&&recipe.NavigationLayout.Enabled||name=="Body"&&recipe.BodyLayout.Enabled||name=="Footer"&&recipe.FooterLayout.Enabled;
            result["studio" + name + "Css"] = ContentJson.Value((name is "Navigation" or "Body" or "Footer")&&builder?"":RegionCss(style));
            result["studio" + name + "Text"] = ContentJson.Value(builder ? "" : style.Text);
            result["studio" + name + "Logo"] = ContentJson.Value(builder ? "" : style.LogoPath);
            result["studio" + name + "LogoPlacement"] = ContentJson.Value(style.LogoPlacement);
        }
        return result;
    }

    public static string TemplateRole(string id) => id == "page-default" ? "page" : "page-" + TypeKey(id);

    public static void NormalizeTemplates(HyperlibProjectPipelineState state)
    {
        if (state.PageTemplates is not { Count: > 0 })
        {
            state.PageTemplate.Id = "page-default";
            state.PageTemplates = [state.PageTemplate];
            state.DefaultPageTemplateId = "page-default";
        }
        if (string.IsNullOrWhiteSpace(state.DefaultPageTemplateId)) state.DefaultPageTemplateId = state.PageTemplates[0].Id;
        state.PageTemplate = GetPageTemplate(state, state.DefaultPageTemplateId);
    }

    public static HyperlibReusablePageTemplate GetPageTemplate(HyperlibProjectPipelineState state, string? id = null)
        => state.PageTemplates.SingleOrDefault(item => item.Id == (id ?? state.DefaultPageTemplateId))
            ?? throw new InvalidDataException("Choose an existing Page template before creating this Page.");

    public static List<HyperlibNamedPageTemplateDefinition> NamedTemplates(HyperlibWorkProjectDefinition definition)
    {
        var result = definition.PageTemplates is { Count: > 0 } ? definition.PageTemplates
            : [new HyperlibNamedPageTemplateDefinition { Id = "page-default", Name = "Content Page", Definition = definition.PageTemplate }];
        ValidateNamedTemplates(result, definition.DefaultPageTemplateId);
        return result;
    }

    public static void ValidateNamedTemplates(IReadOnlyList<HyperlibNamedPageTemplateDefinition> templates, string defaultId)
    {
        if (templates.Count < 1) throw new InvalidDataException("Create at least one reusable Page template.");
        foreach (var item in templates)
        {
            TemplateValidator.ValidateId(item.Id, "Page template");
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100 || item.Name.Any(char.IsControl))
                throw new InvalidDataException("Name each Page template with 1–100 printable characters.");
        }
        if (templates.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != templates.Count)
            throw new InvalidDataException("Page template IDs must be unique.");
        if (!templates.Any(item => item.Id == defaultId)) throw new InvalidDataException("Choose a default Page template from this project.");
    }

    public static IEnumerable<(string Name, HyperlibTemplateRegionStyle Style)> RegionStyles(HyperlibPageTemplateDefinition recipe)
        => [("Header", recipe.HeaderStyle), ("Body", recipe.BodyStyle), ("Navigation", recipe.NavigationStyle),
            ("Footer", recipe.FooterStyle), ("Extras", recipe.ExtrasStyle)];

    public static void EnsureAppearanceFields(TemplateDefinition definition)
    {
        void Ensure(string id, string title, string type = "string")
        { if (!definition.Appearance.Any(field => field.Id == id)) definition.Appearance.Add(new() { Id = id, Title = title, Type = type }); }
        Ensure("studioPageTemplateId", "Reusable Page template");
        Ensure("studioCompleteTemplate", "Complete original page template", "boolean");
        Ensure("studioDirectEditing", "Direct page editing", "boolean");
        Ensure("studioBackgroundCss", "Whole Page background and border");
        Ensure("studioBackgroundImage", "Whole Page background image", "media");
        Ensure("studioHeaderBuilder", "Structured header rows", "boolean");
        Ensure("studioNavigationBuilder", "Structured Navigation", "boolean");
        Ensure("studioBodyBuilder", "Structured Body", "boolean");
        Ensure("studioFooterBuilder", "Structured Footer", "boolean");
        foreach (var name in new[] { "Header", "Body", "Navigation", "Footer", "Extras" })
        {
            Ensure("studio" + name + "Enabled", name + " enabled", "boolean");
            Ensure("studio" + name + "Css", name + " visual style");
            Ensure("studio" + name + "Text", name + " reusable text");
            Ensure("studio" + name + "Logo", name + " logo", "media");
            Ensure("studio" + name + "LogoPlacement", name + " logo placement");
        }
    }

    /// <summary>Optional per-Page geometry. Missing values preserve the original document flow.</summary>
    public static void EnsureDirectEditorFields(TemplateDefinition definition)
    {
        foreach (var block in definition.Blocks)
        {
            void Number(string id, string title, double value, double min, double max)
            {
                var existing = block.Fields.SingleOrDefault(field => field.Id == id);
                if (existing is not null)
                {
                    if (existing.Type != "number" || existing.Min != min || existing.Max != max)
                        throw new InvalidDataException("This component has an incompatible reserved Studio field: " + block.Title + " / " + id);
                    return;
                }
                block.Fields.Add(new() { Id = id, Title = title, Type = "number", Min = min, Max = max, DefaultValue = ContentJson.Value(value) });
            }
            var positioned = block.Fields.SingleOrDefault(field => field.Id == "studioPositioned");
            if (positioned is not null && positioned.Type != "boolean")
                throw new InvalidDataException("This component has an incompatible reserved Studio position field: " + block.Title);
            if (positioned is null) block.Fields.Add(new() { Id = "studioPositioned", Title = "Free placement", Type = "boolean", DefaultValue = ContentJson.Value(false) });
            Number("studioX", "Horizontal position (%)", 0, 0, 100);
            Number("studioY", "Vertical position (px)", 0, 0, 100000);
            Number("studioWidth", "Width (%)", 100, 1, 100);
            Number("studioHeight", "Height (px, 0 = automatic)", 0, 0, 100000);
            var elements = block.Fields.SingleOrDefault(field => field.Id == "studioElements");
            if (elements is not null && elements.Type != "text")
                throw new InvalidDataException("This component has an incompatible reserved Studio element field: " + block.Title);
            // Optional Page-local edits preserve compiled component source and sibling content.
            if (elements is null) block.Fields.Add(new() { Id = "studioElements", Title = "Element edits", Type = "text" });
        }
    }

    private static void ValidateRegionStyle(HyperlibTemplateRegionStyle style)
    {
        if (style is null) throw new InvalidDataException("A reusable region style is missing.");
        foreach (var color in new[] { style.BackgroundColor, style.TextColor, style.BorderColor })
            if (!string.IsNullOrEmpty(color) && !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("Choose a six-digit hexadecimal region color.");
        if (!double.IsFinite(style.BorderWidth) || style.BorderWidth is < 0 or > 20 || !double.IsFinite(style.BorderRadius)
            || style.BorderRadius is < 0 or > 200 || !double.IsFinite(style.Padding) || style.Padding is < 0 or > 200
            || !double.IsFinite(style.FontSize) || style.FontSize is < 8 or > 100)
            throw new InvalidDataException("Choose supported border, padding, and font sizes.");
        if (style.Alignment is not ("left" or "center" or "right") || style.LogoPlacement is not ("left" or "center" or "right" or "none"))
            throw new InvalidDataException("Choose a supported alignment and logo placement.");
        if (!Regex.IsMatch(style.FontFamily ?? "", "^[a-zA-Z0-9 ,_-]{1,100}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Choose a standard font family.");
        if (style.Text is null || style.Text.Length > 500 || style.Text.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t'))
            throw new InvalidDataException("Reusable chrome text supports at most 500 characters.");
        // Native saves import local files before composition; portable gateway requests may use only project media.
        if (!string.IsNullOrEmpty(style.LogoPath) && (!style.LogoPath.StartsWith("/media/", StringComparison.Ordinal)
            || !TemplateValidator.IsSafeUrl(style.LogoPath) || style.LogoPath.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '/' or '-' or '_' or '.'))))
            throw new InvalidDataException("Import the logo into the project's Media folder before saving this layout.");
    }

    public static string RegionCss(HyperlibTemplateRegionStyle style)
    {
        ValidateRegionStyle(style);
        static string Number(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return (style.BackgroundColor.Length > 0 ? "background:" + style.BackgroundColor + ";--hl-paper:" + style.BackgroundColor + ";" : "")
            + (style.TextColor.Length > 0 ? "color:" + style.TextColor + ";--hl-ink:" + style.TextColor + ";" : "")
            + "border:" + Number(style.BorderWidth) + "px solid " + (style.BorderColor.Length > 0 ? style.BorderColor : "transparent")
            + ";border-radius:" + Number(style.BorderRadius) + "px;padding:" + Number(style.Padding) + "px;font-family:" + style.FontFamily
            + ";--hl-font:" + style.FontFamily + ";--hl-heading-font:" + style.FontFamily + ";font-size:" + Number(style.FontSize) + "px;text-align:" + style.Alignment
            + ";--studio-justify:" + (style.Alignment == "center" ? "center" : style.Alignment == "right" ? "flex-end" : "flex-start") + ";";
    }

    public static Dictionary<string, JsonElement> EmptyContentValues(BlockDefinition schema) => schema.Fields.ToDictionary(field => field.Id,
        field => field.Type is "string" or "text" or "url" or "media" ? ContentJson.Value("")
            : field.DefaultValue?.Clone() ?? (field.Type == "number" ? ContentJson.Value(field.Min ?? 0)
            : field.Type == "boolean" ? ContentJson.Value(false) : field.Type == "color" ? ContentJson.Value("#65a88e")
            : ContentJson.Value(field.Options.FirstOrDefault() ?? "")), StringComparer.Ordinal);

    public static List<PageBlock> MergeLayout(List<PageBlock> content, List<PageBlock> previous, List<PageBlock> next, TemplateDefinition definition,
        Dictionary<string, string>? renamed = null)
    {
        var oldIds = previous.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        var retained = content.Where(block => !oldIds.Contains(block.Id) && !next.Any(slot => slot.Id == block.Id && slot.Type == block.Type))
            .Select(block => ContentJson.Clone(block)).ToList();
        var output = new List<PageBlock>();
        foreach (var staged in next)
        {
            var old = content.FirstOrDefault(block => block.Id == staged.Id && block.Type == staged.Type);
            var copy = ContentJson.Clone(staged);
            var previousSlot = previous.FirstOrDefault(block => block.Id == staged.Id && block.Type == staged.Type);
            if (old is not null)
                foreach (var value in old.Values)
                {
                    if (staged.Type is HyperlibHeaderLayouts.BlockType or HyperlibHeaderLayouts.BodyBlockType or HyperlibHeaderLayouts.FooterBlockType && value.Key == "layout" && value.Value.ValueKind == JsonValueKind.String
                        && previousSlot is not null && previousSlot.Values.TryGetValue("layout", out var priorHeader) && copy.Values.TryGetValue("layout", out var nextHeader))
                    {
                        copy.Values["layout"] = ContentJson.Value(HyperlibHeaderLayouts.MergeSerializedBlock(value.Value.GetString()!, priorHeader.GetString()!, nextHeader.GetString()!));
                        continue;
                    }
                    if(staged.Type==HyperlibNavigationLayouts.LayoutBlockType&&value.Key=="layout"&&value.Value.ValueKind==JsonValueKind.String&&previousSlot is not null
                        &&previousSlot.Values.TryGetValue("layout",out var oldNavigation)&&copy.Values.TryGetValue("layout",out var newNavigation))
                    {copy.Values["layout"]=ContentJson.Value(HyperlibNavigationLayouts.MergeSerializedLayout(value.Value.GetString()!,oldNavigation.GetString()!,newNavigation.GetString()!));continue;}
                    // Untouched structural defaults follow the new recipe (for example half → third spans).
                    // Authored values and explicit per-Page overrides retain their own independent content.
                    var untouched = previousSlot is not null && previousSlot.Values.TryGetValue(value.Key, out var prior)
                        && prior.GetRawText() == value.Value.GetRawText();
                    if (!untouched || !copy.Values.ContainsKey(value.Key)) copy.Values[value.Key] = value.Value.Clone();
                }
            output.Add(copy);
        }
        foreach (var old in content.Where(candidate => oldIds.Contains(candidate.Id) && !next.Any(slot => slot.Id == candidate.Id && slot.Type == candidate.Type)))
        {
            var schema = definition.Blocks.Single(block => block.Id == old.Type);
            var previousSlot = previous.Single(item => item.Id == old.Id);
            var hasAuthoredContent = old.Type is HyperlibHeaderLayouts.BlockType or HyperlibHeaderLayouts.BodyBlockType or HyperlibHeaderLayouts.FooterBlockType or HyperlibNavigationLayouts.LayoutBlockType
                ? old.Values.TryGetValue("layout", out var liveHeader) && previousSlot.Values.TryGetValue("layout", out var previousHeader)
                    && HyperlibHeaderLayouts.HasIndependentChanges(liveHeader.GetString()!, previousHeader.GetString()!)
                : schema.Fields.Any(field => field.Type is "string" or "text" or "url" or "media"
                && old.Values.TryGetValue(field.Id, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                || old.Region=="main"&&old.Values.Any(value=>!previousSlot.Values.TryGetValue(value.Key,out var prior)||prior.GetRawText()!=value.Value.GetRawText());
            if (!hasAuthoredContent) continue;
            var preserved = ContentJson.Clone(old);
            if (next.Any(block => block.Id == preserved.Id)) preserved.Id = "content-" + Guid.NewGuid().ToString("N");
            if (renamed is not null) renamed[old.Id] = preserved.Id;
            preserved.Region = "main";
            retained.Add(preserved);
        }
        output.AddRange(retained);
        return output;
    }

    private static string TypeKey(string type) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(type)))[..8];

    public static string Guard(string root, string relative)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var result = Path.GetFullPath(Path.Combine(full, relative));
        if (!result.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The path escapes this project.");
        for (var current = result; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Project paths cannot contain links or junctions."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
        return result;
    }

    public static async Task AtomicWriteAsync(string path, string text, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task<FileStream> AcquireProjectLockAsync(string projectRoot, CancellationToken ct)
    {
        var path = Guard(projectRoot, ".studio/pipeline.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(15)) { await Task.Delay(50, ct); }
        }
    }
}
