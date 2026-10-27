using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record StudioAuthoringLibraryItem(string Id, string Title, string Description, string Scope, string Category,
    string Collection, string Kind, bool Compatible = true, string? Reason = null,
    string? Type = null, string? Project = null, string? SourceTitle = null, string? AliasFor = null);
public sealed record StudioAuthoringLibraryPreview(string Url, string Title, string Message);
public sealed record StudioAuthoringLibraryChange(LibraryState State, string PageId, IReadOnlyList<string> AddedBlockIds);
public sealed record StudioAuthoringLibrarySavedTemplate(StudioAuthoringLibraryItem Item);

/// <summary>The authoring library reads the complete source collection and copies selections into one private Work.</summary>
public sealed class StudioAuthoringLibraryService : IDisposable
{
    private sealed record Entry(StudioAuthoringLibraryItem Item, string Source, string? ChoiceId = null, string? NativeType = null,
        string? Section = null, string? Preset = null, string? Complete = null, string? Saved = null, string? CatalogId = null);
    private sealed record Project(string Root, string Library, HyperlibProjectPipelineState Pipeline, TemplateDefinition Definition, LibraryState State);
    private sealed class SavedPage
    {
        public string Title { get; set; } = "";
        public string? SourceProject { get; set; }
        public string? SourceWorkId { get; set; }
        public Dictionary<string, JsonElement> Appearance { get; set; } = [];
        public List<SourceCitation> Sources { get; set; } = [];
    }
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _source;
    private readonly HyperlibTemplateObjectPreviewService _objects;
    private readonly HyperlibGuidedPagePreviewService _pages;
    private readonly StudioCompleteTemplateService _complete;
    private readonly StudioCatalogElementService _catalog;
    private IReadOnlyList<HyperlibWorkTemplateChoice>? _sourceCatalog;
    private bool _disposed;

    public StudioAuthoringLibraryService(string? sourceLibraryRoot = null, string? cacheRoot = null)
    {
        _source = new HyperlibWorkProjectService(sourceLibraryRoot).TemplateLibraryRoot;
        _objects = new(cacheRoot is null ? null : Path.Combine(cacheRoot, "objects"));
        _pages = new(cacheRoot is null ? null : Path.Combine(cacheRoot, "pages"));
        _complete = new(_source);
        // Keep internal cache segments short: Windows process creation still bounds the renderer's working-directory length.
        _catalog = new(_source, cacheRoot is null ? null : Path.Combine(cacheRoot, "ce"));
    }

    public async Task<IReadOnlyList<StudioAuthoringLibraryItem>> ListAsync(string projectRoot, CancellationToken ct = default)
        => (await ListIncludingAliasesAsync(projectRoot, ct)).Where(item => item.AliasFor is null).ToArray();

    /// <summary>Includes stable legacy routes hidden as duplicate aliases in the visible library.</summary>
    public async Task<IReadOnlyList<StudioAuthoringLibraryItem>> ListIncludingAliasesAsync(string projectRoot, CancellationToken ct = default)
        => (await EntriesAsync(projectRoot, ct)).Select(entry => entry.Item).ToArray();

    public StudioAuthoringLibraryItem DescribeCatalogElement(string descriptorId)
    {
        var item = _catalog.Describe(descriptorId);
        return item with { Type = "Element", SourceTitle = item.Title };
    }

    public async Task<StudioAuthoringLibraryPreview> PreviewAsync(string projectRoot, string itemId, CancellationToken ct = default)
    {
        var entry = await FindAsync(projectRoot, itemId, ct);
        if (entry.CatalogId is not null) return await _catalog.PreviewAsync(entry.CatalogId, ct);
        if (entry.Complete is not null)
        {
            var definition = _complete.BuildDefinition(entry.Complete, entry.Item.Title);
            var preview = await _pages.PreviewAsync(_source, definition, definition.LandingTemplate, entry.Item.Title, ct);
            return new(preview.Url.AbsoluteUri, entry.Item.Title, "Complete Page template. Add it as a content Page or to this Work's Landing Page.");
        }
        if (entry.Section is not null)
        {
            var definition = HyperlibWorkProjectService.BuildDirectEditingDefinition(entry.Item.Title);
            var recipe = SectionRecipe(entry.Section, entry.Preset!);
            definition.LandingTemplate = recipe;
            var preview = await _pages.PreviewAsync(_source, definition, recipe, entry.Item.Title, ct);
            return new(preview.Url.AbsoluteUri, entry.Item.Title, "Reusable section. Its content remains independently editable after adding it.");
        }
        var choice = entry.ChoiceId;
        if (entry.Saved is not null) choice = entry.Saved + ":page-layout";
        if (entry.NativeType is not null)
        {
            var project = await ReadAsync(projectRoot, ct);
            var native = await _objects.PreviewElementAsync(project.Library, project.Root, entry.NativeType, ct);
            return new(native.Url.AbsoluteUri, entry.Item.Title, native.Message);
        }
        if (choice is null) throw new InvalidDataException("This library item has no visual content.");
        var split = choice.IndexOf(':');
        var result = await _objects.PreviewAsync(entry.Source, choice[..split], choice[(split + 1)..], ct);
        return new(result.Url.AbsoluteUri, entry.Item.Title, result.Message);
    }

    public async Task<StudioAuthoringLibraryChange> AddAsync(string projectRoot, string itemId, string pageId, string? regionId,
        long expectedContentRevision, string? title = null, string? parentId = null, CancellationToken ct = default, double? insertionY = null,
        string? targetRole = null)
    {
        if (targetRole is not null and not ("landing" or "page"))
            throw new InvalidDataException("Choose a Landing or content Page destination.");
        if (insertionY.HasValue && (!double.IsFinite(insertionY.Value) || insertionY.Value < 0 || insertionY.Value > 100000))
            throw new InvalidDataException("The insertion position must be within this Page's canvas.");
        var root = Path.GetFullPath(projectRoot);
        var gate = Gates.GetOrAdd(root, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            var project = await ReadAsync(root, ct); CheckRevision(project.State, expectedContentRevision);
            var current = RequirePage(project, pageId);
            var entry = await FindAsync(root, itemId, ct);
            if (!entry.Item.Compatible) throw new InvalidDataException(entry.Item.Reason ?? "This item cannot be inserted into a Page.");
            var pageChoice = entry.Item.Scope == "page";
            if (targetRole == "landing" && !pageChoice)
                throw new InvalidDataException("Choose a complete Page template to create a Landing Page. Sections and modules add to the selected Page.");
            var landingTarget = pageChoice && targetRole == "landing";
            var landingInsertionY = landingTarget && current.Id == LandingId(project) ? insertionY : null;
            if (landingTarget && LandingId(project) is { } selectedLandingId)
            { pageId = selectedLandingId; current = RequirePage(project, pageId); }
            else if (landingTarget) _ = AvailableLandingId(project);
            HyperlibReusablePageTemplate? composed = null;
            HyperlibProjectToolkitItem? imported = null;
            var choices = new List<string>();
            if (entry.Complete is not null) choices.AddRange(_complete.BuildDefinition(entry.Complete, entry.Item.Title).SelectedToolsetIds);
            else if (entry.ChoiceId is not null) choices.Add(entry.ChoiceId);
            else if (entry.Saved is not null) choices.Add(entry.Saved + ":page-layout");
            if (choices.Count > 0)
            {
                var definition = (await new HyperlibProjectPipelineService().ReadDefinitionAsync(root, ct))!;
                definition.SelectedToolsetIds = definition.SelectedToolsetIds.Concat(choices).Distinct(StringComparer.Ordinal).ToList();
                await new HyperlibWorkProjectService(entry.Source).UpdateFoundationAsync(root, definition, project.Pipeline.Revision, expectedContentRevision, ct);
                project = await ReadAsync(root, ct); current = RequirePage(project, pageId);
                imported = project.Pipeline.Toolkit.Single(item => item.ChoiceId == choices[0]);
                if (entry.Complete is not null)
                {
                    var recipe = _complete.BuildDefinition(entry.Complete, entry.Item.Title).LandingTemplate;
                    composed = HyperlibProjectPipelineService.Compose(recipe, project.Pipeline.Toolkit, project.Definition, "library");
                }
            }
            var blocks = new List<PageBlock>();
            if (entry.CatalogId is not null)
            {
                var schema = await _catalog.PrepareInsertionAsync(root, project.Definition, entry.CatalogId, ct);
                blocks = [DefaultBlock(schema, "main")];
                project = await ReadAsync(root, ct); current = RequirePage(project, pageId);
                // Component installation never authorizes a newer Page revision.
                CheckRevision(project.State, expectedContentRevision);
            }
            else if (composed is not null) blocks = ContentJson.Clone(composed.Blocks);
            else if (imported is { Kind: "layout" }) blocks = ContentJson.Clone(imported.LayoutBlocks);
            else if (imported is not null)
                blocks = imported.BlockTypeIds.Select(type => DefaultBlock(project.Definition.Blocks.Single(block => block.Id == type), "main")).ToList();
            else if (entry.NativeType is not null) blocks = [DefaultBlock(project.Definition.Blocks.Single(block => block.Id == entry.NativeType), "main")];
            else if (entry.Section is not null)
            { blocks = await PrepareSectionAsync(project, entry.Section, entry.Preset!, ct); project = await ReadAsync(root, ct); current = RequirePage(project, pageId); }
            if (pageChoice && imported is { Kind: "layout" })
            {
                IReadOnlyList<PageBlock> originals;
                if (entry.Saved is not null) originals = ReusableTemplateCatalog.ReadDefinition(project.Library, entry.Saved).DefaultBlocks;
                else
                {
                    var choice = SourceCatalog().Single(item => item.Id == imported.ChoiceId);
                    originals = new HyperlibWorkProjectService(entry.Source).GetCatalogObject(choice.GroupId, choice.ObjectId).Blocks;
                }
                // Import/composition retain source order while remapping schema and (for complete Pages) block identities.
                if (originals.Count != blocks.Count) throw new InvalidDataException("The source Page layout changed. Refresh the library before adding it again.");
                foreach (var group in blocks.Select((block, index) => (Block: block, Region: originals[index].Region)).GroupBy(item => item.Region, StringComparer.Ordinal))
                {
                    var destination = project.Definition.Regions.SingleOrDefault(region => region.Id == group.Key);
                    if (destination is not null && (destination.AllowedBlocks.Count == 0 || group.All(item => destination.AllowedBlocks.Contains(item.Block.Type))))
                        foreach (var item in group) item.Block.Region = destination.Id;
                }
            }
            if (!pageChoice && (imported is { Kind: "layout" } || entry.Section is not null) && blocks.Count > 1)
            { blocks = [await PrepareModuleAsync(project, blocks, entry.Item.Title, ct)]; project = await ReadAsync(root, ct); current = RequirePage(project, pageId); }
            if (project.Definition.Blocks.Any(block => block.Component.Contains("StudioLibrarySection", StringComparison.Ordinal)))
                HyperlibNavigationLayouts.EnsureBlockDefinitions(project.Definition);
            if (pageChoice && imported is { Kind: "layout" })
                foreach (var region in imported.LayoutRegions.Where(region => blocks.Any(block => block.Region == region.Id)))
                    if (!project.Definition.Regions.Any(existing => existing.Id == region.Id)) project.Definition.Regions.Add(ContentJson.Clone(region));
            await SaveSchemaIfChangedAsync(project, ct);
            var appearance = composed?.Appearance ?? imported?.LayoutAppearance ?? [];
            var sourceIds = blocks.ToDictionary(block => block.Id, _ => "library-" + Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
            foreach (var block in blocks) block.Id = sourceIds[block.Id];
            var store = new ContentStore(project.Library);
            var latest = await store.ReadAsync(ct);
            if (latest.Revision != project.State.Revision) throw new ConflictException(project.State.Revision, latest.Revision);
            var operations = new List<ContentOperation>();
            var destinationId = current.Id;
            if (pageChoice)
            {
                var landingId = landingTarget ? LandingId(project) : null;
                destinationId = landingTarget ? landingId ?? AvailableLandingId(project) : "page-" + Guid.NewGuid().ToString("N");
                var name = string.IsNullOrWhiteSpace(title) ? landingId is not null ? latest.Pages[landingId].Title : entry.Item.Title : title.Trim(); ValidateTitle(name);
                var parent = landingTarget ? project.Pipeline.WorkId : parentId ?? project.Pipeline.WorkId;
                var parentNode = latest.Nodes.SingleOrDefault(node => node.Id == parent);
                if (parentNode is null || parentNode.Type == "page" || parentNode.Id != project.Pipeline.WorkId && parentNode.WorkId != project.Pipeline.WorkId)
                    throw new InvalidDataException("Choose a container within this Work for the new Page.");
                var sources = new List<SourceCitation>();
                if (entry.Saved is not null)
                {
                    var snapshot = await ReadSavedAsync(project.Library, entry.Saved, ct);
                    appearance = snapshot.Appearance;
                    sources = snapshot.Sources;
                    foreach (var citation in sources)
                    { citation.Id = "citation-" + Guid.NewGuid().ToString("N"); citation.BlockIds = citation.BlockIds.Where(sourceIds.ContainsKey).Select(id => sourceIds[id]).ToList(); }
                }
                if (landingId is not null)
                {
                    var page = ContentJson.Clone(latest.Pages[landingId]);
                    var hadContent = page.Blocks.Count > 0;
                    page.Title = name; page.UsesEditableChrome = true;
                    if (hadContent) OffsetLandingAddition(page, blocks, landingInsertionY);
                    page.Blocks.AddRange(blocks); page.Sources.AddRange(sources);
                    var existing = latest.Nodes.Single(node => node.Id == landingId);
                    var combined = ContentJson.Clone(appearance);
                    // Adding a template never erases a populated landing's authored theme or values.
                    if (hadContent) foreach (var pair in existing.Appearance) combined[pair.Key] = pair.Value.Clone();
                    operations.Add(new() { Kind = "savePage", Id = landingId, Page = page });
                    operations.Add(new() { Kind = "setAppearance", Id = landingId, Appearance = combined });
                }
                else operations.Add(new() { Kind = "createNode", Id = destinationId, NodeType = "page", ParentId = parent, Title = name,
                    Order = landingTarget ? LandingOrder(project) : null,
                    Appearance = ContentJson.Clone(appearance), Page = new() { Title = name, TemplateId = project.Definition.TemplateId, UsesEditableChrome = true, Blocks = blocks, Sources = sources } });
                if (landingTarget) AddLandingRoleOperations(project, destinationId, operations);
            }
            else if (blocks.Count > 0)
            {
                var page = ContentJson.Clone(current);
                var target = string.IsNullOrWhiteSpace(regionId) ? "main" : regionId;
                var region = project.Definition.Regions.SingleOrDefault(item => item.Id == target)
                    ?? throw new InvalidDataException("Choose an existing content region.");
                foreach (var block in blocks)
                {
                    if (region.AllowedBlocks.Count > 0 && !region.AllowedBlocks.Contains(block.Type)) throw new InvalidDataException("This content region does not accept the selected object.");
                    block.Region = region.Id;
                }
                PlaceBelow(page, blocks, region.Id, insertionY);
                page.Blocks.AddRange(blocks);
                operations.Add(new() { Kind = "savePage", Id = current.Id, Page = page });
            }
            else if (appearance.Count > 0)
            {
                var pageNode = latest.Nodes.Single(node => node.Id == current.Id);
                var combined = ContentJson.Clone(pageNode.Appearance);
                foreach (var pair in appearance) combined[pair.Key] = pair.Value.Clone();
                operations.Add(new() { Kind = "setAppearance", Id = current.Id, Appearance = combined });
            }
            else throw new InvalidDataException("This library item contains no insertable content.");
            var state = await store.ApplyAsync(new() { ExpectedRevision = latest.Revision, Description = "Add " + entry.Item.Title + " from the authoring library", Operations = operations }, ct);
            return new(state, destinationId, blocks.Select(block => block.Id).ToArray());
        }
        finally { gate.Release(); }
    }

    /// <summary>Opens or initializes the Work's one landing Page without creating a content Page in a selected container.</summary>
    public async Task<StudioAuthoringLibraryChange> CreateLandingAsync(string projectRoot, string? title,
        long expectedContentRevision, string? templateId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(title)) ValidateTitle(title);
        var root = Path.GetFullPath(projectRoot);
        var gate = Gates.GetOrAdd(root, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            var project = await ReadAsync(root, ct); CheckRevision(project.State, expectedContentRevision);
            var landingId = LandingId(project);
            var destinationId = landingId ?? AvailableLandingId(project);
            var current = landingId is null ? null : project.State.Pages[landingId];
            var hasContent = current?.Blocks.Count > 0;
            var name = hasContent ? current!.Title : string.IsNullOrWhiteSpace(title) ? current?.Title ?? "Landing page" : title.Trim();
            var operations = new List<ContentOperation>();
            var addedIds = new List<string>();
            if (!hasContent)
            {
                var layout = string.IsNullOrWhiteSpace(templateId) ? project.Pipeline.LandingTemplate
                    : project.Pipeline.PageTemplates.SingleOrDefault(item => item.Id == templateId)
                        ?? throw new InvalidDataException("Choose a reusable Page template from this Work.");
                var page = current is null ? new PageDocument { Id = destinationId, WorkId = project.Pipeline.WorkId,
                    TemplateId = project.Definition.TemplateId, UsesEditableChrome = true } : ContentJson.Clone(current);
                page.Title = name;
                foreach (var original in layout.Blocks)
                {
                    var block = ContentJson.Clone(original); block.Id = "landing-" + Guid.NewGuid().ToString("N");
                    page.Blocks.Add(block); addedIds.Add(block.Id);
                }
                var appearance = ContentJson.Clone(layout.Appearance);
                if (landingId is not null)
                {
                    // A blank landing may already have a user-selected background; retain its explicit values.
                    foreach (var pair in project.State.Nodes.Single(node => node.Id == landingId).Appearance)
                        appearance[pair.Key] = pair.Value.Clone();
                    operations.Add(new() { Kind = "savePage", Id = destinationId, Page = page });
                    operations.Add(new() { Kind = "setAppearance", Id = destinationId, Appearance = appearance });
                }
                else operations.Add(new() { Kind = "createNode", Id = destinationId, NodeType = "page",
                    ParentId = project.Pipeline.WorkId, Title = name, Order = LandingOrder(project), Page = page, Appearance = appearance });
            }
            AddLandingRoleOperations(project, destinationId, operations);
            if (operations.Count == 0) return new(project.State, destinationId, []);
            var state = await new ContentStore(project.Library).ApplyAsync(new() { ExpectedRevision = expectedContentRevision,
                Description = "Set Work landing Page", Operations = operations }, ct);
            return new(state, destinationId, addedIds);
        }
        finally { gate.Release(); }
    }

    public async Task<StudioAuthoringLibrarySavedTemplate> SavePageTemplateAsync(string projectRoot, string pageId, string title,
        long expectedContentRevision, CancellationToken ct = default)
    {
        ValidateTitle(title);
        var root = Path.GetFullPath(projectRoot); var gate = Gates.GetOrAdd(root, _ => new(1, 1)); await gate.WaitAsync(ct);
        ReusableTemplateInfo? created = null; string? createdRoot = null;
        try
        {
            var project = await ReadAsync(root, ct); CheckRevision(project.State, expectedContentRevision);
            var page = RequirePage(project, pageId);
            if (page.Blocks.Count == 0) throw new InvalidDataException("Add some content before saving this Page as a reusable template.");
            created = await ReusableTemplateManager.CreateFromWorkAsync(project.Library, project.Pipeline.WorkId, title.Trim(), ct);
            createdRoot = HyperlibProjectPipelineService.Guard(project.Library, "Templates/" + created.Id);
            var templateRoot = ReusableTemplateCatalog.GetProjectRoot(project.Library, created.Id);
            var definition = TemplateObjectLibrary.ReadDefinition(templateRoot);
            definition.DefaultBlocks = ContentJson.Clone(page.Blocks); definition.EmptyPageDefault = false;
            var node = project.State.Nodes.Single(item => item.Id == pageId);
            var work = project.State.Nodes.Single(item => item.Id == project.Pipeline.WorkId);
            var appearance = ContentJson.Clone(project.State.Appearance);
            foreach (var pair in work.Appearance) appearance[pair.Key] = pair.Value.Clone();
            foreach (var pair in node.Appearance) appearance[pair.Key] = pair.Value.Clone();
            foreach (var field in definition.Appearance) if (appearance.TryGetValue(field.Id, out var value)) field.DefaultValue = value.Clone();
            TemplateValidator.ValidateDefinition(definition);
            await HyperlibProjectPipelineService.AtomicWriteAsync(Path.Combine(templateRoot, "authoring.template.json"), JsonSerializer.Serialize(definition, ContentJson.Options), ct);
            await HyperlibProjectPipelineService.AtomicWriteAsync(HyperlibProjectPipelineService.Guard(templateRoot, ".studio/studio-saved-page.json"),
                JsonSerializer.Serialize(new SavedPage { Title = page.Title, SourceProject = work.Title, SourceWorkId = work.Id,
                    Appearance = appearance, Sources = ContentJson.Clone(page.Sources) }, ContentJson.Options), ct);
            CheckRevision(await new ContentStore(project.Library).ReadAsync(ct), expectedContentRevision);
            var item = new StudioAuthoringLibraryItem("saved:" + created.Id, created.Title, "A complete reusable copy of " + page.Title + ", including its layout, values and citations.",
                "page", "Saved Pages", "My templates", "layout", Type: "Page", Project: work.Title, SourceTitle: created.Title);
            createdRoot = null;
            return new(item);
        }
        catch
        {
            if (createdRoot is not null && Directory.Exists(createdRoot))
            { var checkedRoot = HyperlibProjectPipelineService.Guard(Path.GetFullPath(Path.Combine(root, "../..")), "Templates/" + created!.Id); if (checkedRoot == createdRoot) Directory.Delete(checkedRoot, true); }
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<Entry>> EntriesAsync(string projectRoot, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var project = await ReadAsync(projectRoot, ct); var result = new List<Entry>();
        var completePages = _complete.List().Where(item => item.SupportedKinds.Contains(StudioProjectKind.Hyperlib)).ToArray();
        foreach (var page in completePages)
            result.Add(new(new("page:" + page.Id, page.Title, page.Description, "page", page.Category, page.Collection, "layout",
                Type: "Page", Project: page.Project, SourceTitle: page.SourceTitle), _source, Complete: page.Id));
        foreach (var (section, presets) in new[] { ("header", HyperlibHeaderLayouts.All), ("navigation", HyperlibNavigationLayouts.All),
            ("body", HyperlibBodyLayouts.All), ("footer", HyperlibFooterLayouts.All) })
            foreach (var preset in presets) result.Add(new(new("section:" + section + ":" + preset.Id, preset.Title, preset.Description, "section",
                char.ToUpperInvariant(section[0]) + section[1..], "Studio sections", section), _source, Section: section, Preset: preset.Id));
        foreach (var choice in SourceCatalog())
        {
            if (choice.Kind == "region") continue;
            var scope = _complete.Metadata.CatalogScope(choice);
            var compatible = choice.Kind != "region";
            var completeId = choice.Kind == "layout" ? "hyperlib:" + choice.GroupId + (choice.ObjectId == "page-layout" ? "" : "/" + choice.ObjectId)
                : _complete.Metadata.LegacyTemplateId(choice) is { } visualId && scope == "page" ? "visual:" + visualId : null;
            var alias = scope == "page" && completeId is not null && completePages.Any(page => page.Id == completeId) ? "page:" + completeId : null;
            result.Add(new(new("object:" + choice.Id, choice.Title, choice.Description, scope, choice.Category, choice.GroupTitle,
                Kind(choice), compatible, compatible ? null : "This is a named-region definition. Choose a section layout or an element to place visible content.",
                Project: StudioTemplateMetadataService.ProjectForFamily(choice.GroupId), AliasFor: alias), _source, ChoiceId: choice.Id));
        }
        foreach (var block in project.Definition.Blocks.Where(block => block.Id is not ("studio-header-row" or "studio-body-row" or "studio-footer-row" or "studio-navigation-layout")))
            result.Add(new(new("element:" + block.Id, block.Title, block.Description ?? "An editable native Hyperlib object.", "element", block.Category ?? "Essentials",
                "Essentials", Kind(block.Component)), _source, NativeType: block.Id));
        foreach (var saved in ReusableTemplateCatalog.List(project.Library).Where(item => !item.IsBuiltIn))
        {
            var snapshotPath = Path.Combine(ReusableTemplateCatalog.GetProjectRoot(project.Library, saved.Id), ".studio", "studio-saved-page.json");
            if (File.Exists(snapshotPath))
            {
                var snapshot = await ReadSavedAsync(project.Library, saved.Id, ct);
                result.Add(new(new("saved:" + saved.Id, saved.Title, saved.Description, "page", "Saved Pages", "My templates", "layout",
                    Type: "Page", Project: snapshot.SourceProject, SourceTitle: saved.Title), project.Library, Saved: saved.Id));
            }
        }
        return result.Select(entry =>
        {
            if (entry.Item.SourceTitle is not null) return entry.Item.Scope == "element" ? entry : entry with { Item = entry.Item with {
                Category = StudioTemplateRoleTaxonomy.Categories.Contains(entry.Item.Category) ? entry.Item.Category
                    : StudioTemplateRoleTaxonomy.Resolve(entry.Item.Id, entry.Item.Title, entry.Item.Category, entry.Item.Scope, entry.Item.Kind) } };
            var metadata = _complete.Metadata.Describe(entry.Item.Id, entry.Item.Title, entry.Item.Scope, entry.Item.Project);
            return entry with { Item = entry.Item with { Title = metadata.Name, Type = metadata.Type, Project = metadata.Project, SourceTitle = entry.Item.Title,
                Category = entry.Item.Scope == "element" ? entry.Item.Category : metadata.Category
                    ?? StudioTemplateRoleTaxonomy.Resolve(entry.Item.Id, metadata.Name, entry.Item.Category, entry.Item.Scope, entry.Item.Kind) } };
        }).OrderBy(item => item.Item.Collection, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Item.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<HyperlibWorkTemplateChoice> SourceCatalog() => _sourceCatalog ??= new HyperlibWorkProjectService(_source).GetCatalog();
    private async Task<Entry> FindAsync(string projectRoot, string id, CancellationToken ct)
    {
        if (id.StartsWith("catalog:", StringComparison.Ordinal))
        {
            _ = await ReadAsync(projectRoot, ct);
            return new(DescribeCatalogElement(id), _source, CatalogId: id);
        }
        var entries = await EntriesAsync(projectRoot, ct);
        var entry = entries.SingleOrDefault(item => item.Item.Id == id)
            ?? throw new InvalidDataException("This library item is no longer available. Refresh the library.");
        // Same-scale aliases use the complete composition, including its outer theme and original region behavior.
        return entry.Item.AliasFor is { } alias
            ? entries.Single(item => item.Item.Id == alias && item.Item.Scope == entry.Item.Scope)
            : entry;
    }
    private static async Task<Project> ReadAsync(string projectRoot, CancellationToken ct)
    {
        var root = Path.GetFullPath(projectRoot); var library = Path.GetFullPath(Path.Combine(root, "../.."));
        if (!HyperlibWorkPreviewHostService.IsGeneratedLibrary(library)) throw new InvalidDataException("Open an independent Hyperlib Work to use its authoring library.");
        var pipeline = await new HyperlibProjectPipelineService().ReadAsync(root, ct) ?? throw new InvalidDataException("The Work's reusable template index is missing.");
        if (Path.GetFileName(root) != pipeline.WorkId) throw new InvalidDataException("The project identity does not match its private Work.");
        var definition = TemplateObjectLibrary.ReadDefinition(root); var state = await new ContentStore(library).ReadAsync(ct);
        return new(root, library, pipeline, definition, state);
    }
    private static PageDocument RequirePage(Project project, string id) => project.State.Pages.TryGetValue(id, out var page) && page.WorkId == project.Pipeline.WorkId
        ? page : throw new InvalidDataException("Choose a Page in this Work.");
    private static string? LandingId(Project project)
    {
        var work = project.State.Nodes.Single(node => node.Id == project.Pipeline.WorkId && node.Type == "work");
        var id = work.Appearance.TryGetValue("workLandingPage", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        bool IsOwnedPage(string? candidate) => candidate is not null && project.State.Pages.TryGetValue(candidate, out var page)
            && page.WorkId == work.Id && project.State.Nodes.Any(node => node.Id == candidate && node.Type == "page" && node.WorkId == work.Id);
        return IsOwnedPage(id) ? id : IsOwnedPage(project.Pipeline.LandingPageId) ? project.Pipeline.LandingPageId : null;
    }
    private static string AvailableLandingId(Project project)
    {
        var preferred = project.Pipeline.LandingPageId;
        if (string.IsNullOrWhiteSpace(preferred))
            throw new InvalidDataException("This Work's reserved Landing Page identity is missing. Preserve the project and repair its .studio/pipeline.json landingPageId before creating a Landing Page.");
        try
        {
            TemplateValidator.ValidateId(preferred, "landing Page");
            if (System.Text.RegularExpressions.Regex.IsMatch(preferred, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new InvalidDataException("This Work's reserved Landing Page identity is a reserved device name. Preserve the project and repair its landing identity.");
        }
        catch (ContentValidationException error)
        { throw new InvalidDataException("This Work's reserved Landing Page identity is invalid. Preserve the project and repair its .studio/pipeline.json landingPageId before creating a Landing Page.", error); }
        if (project.State.Nodes.Any(node => string.Equals(node.Id, preferred, StringComparison.OrdinalIgnoreCase))
            || project.State.Pages.ContainsKey(preferred) || string.Equals(project.State.LibraryId, preferred, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This Work's reserved Landing Page identity is already used by another item. Preserve the project and repair its landing identity before creating a Landing Page.");
        return preferred;
    }
    private static double LandingOrder(Project project) => project.State.Nodes
        .Where(node => node.ParentId == project.Pipeline.WorkId && node.Id != LandingId(project))
        .Select(node => node.Order).DefaultIfEmpty(0).Min() - 1;
    private static void AddLandingRoleOperations(Project project, string landingId, List<ContentOperation> operations)
    {
        var node = project.State.Nodes.SingleOrDefault(item => item.Id == landingId);
        if (node is not null)
        {
            var order = LandingOrder(project);
            if (node.ParentId != project.Pipeline.WorkId)
                operations.Add(new() { Kind = "moveNode", Id = landingId, ParentId = project.Pipeline.WorkId, Order = order });
            else if (node.Order != order) operations.Add(new() { Kind = "updateNode", Id = landingId, Order = order });
        }
        var work = project.State.Nodes.Single(item => item.Id == project.Pipeline.WorkId && item.Type == "work");
        if (!work.Appearance.TryGetValue("workLandingPage", out var value) || value.ValueKind != JsonValueKind.String || value.GetString() != landingId)
        {
            var appearance = ContentJson.Clone(work.Appearance); appearance["workLandingPage"] = ContentJson.Value(landingId);
            operations.Add(new() { Kind = "setAppearance", Id = work.Id, Appearance = appearance });
        }
    }
    private static void OffsetLandingAddition(PageDocument page, List<PageBlock> added, double? measuredInsertionY)
    {
        // Keep the imported composition and relative geometry intact while placing it after authored content.
        foreach (var group in added.GroupBy(block => block.Region, StringComparer.Ordinal))
        {
            var existing = page.Blocks.Where(block => block.Region == group.Key).ToArray();
            if (existing.Length == 0 || !existing.Concat(group).Any(Positioned)) continue;
            var end = existing.Select(block => Number(block, "studioY") + Math.Max(80, Number(block, "studioHeight"))).DefaultIfEmpty(0).Max() + 24;
            if (measuredInsertionY.HasValue) end = Math.Max(end, measuredInsertionY.Value);
            var first = group.Select(block => Number(block, "studioY")).DefaultIfEmpty(0).Min();
            foreach (var block in group)
            {
                if (Positioned(block)) block.Values["studioY"] = ContentJson.Value(Number(block, "studioY") - first + end);
                else if (existing.Any(Positioned))
                {
                    block.Values["studioPositioned"] = ContentJson.Value(true);
                    block.Values["studioY"] = ContentJson.Value(end);
                    block.Values.TryAdd("studioX", ContentJson.Value(0));
                    block.Values.TryAdd("studioWidth", ContentJson.Value(100));
                    block.Values.TryAdd("studioHeight", ContentJson.Value(0));
                    end += 264;
                }
            }
        }
        static bool Positioned(PageBlock block) => block.Values.TryGetValue("studioPositioned", out var value) && value.ValueKind == JsonValueKind.True;
        static double Number(PageBlock block, string field) => block.Values.TryGetValue(field, out var value) && value.TryGetDouble(out var number) ? number : 0;
    }
    private static void CheckRevision(LibraryState state, long expected) { if (state.Revision != expected) throw new ConflictException(expected, state.Revision); }
    private static void ValidateTitle(string title)
    { if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 160 || title.Any(char.IsControl)) throw new InvalidDataException("Use a title with 1–160 printable characters."); }
    private static PageBlock DefaultBlock(BlockDefinition schema, string region) => new() { Id = "source-" + Guid.NewGuid().ToString("N"), Type = schema.Id, Region = region,
        Values = schema.Fields.Where(field => field.DefaultValue.HasValue).ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal) };
    private static async Task SaveSchemaIfChangedAsync(Project project, CancellationToken ct)
    {
        var source = await HyperlibStudioAuthoringService.LoadAsync(project.Root, "authoring.template.json");
        ct.ThrowIfCancellationRequested();
        if (JsonSerializer.Serialize(source.Definition, ContentJson.Options) != JsonSerializer.Serialize(project.Definition, ContentJson.Options))
            await HyperlibStudioAuthoringService.SaveAsync(project.Root, source.Snapshot, project.Definition);
    }
    private static void PlaceBelow(PageDocument page, List<PageBlock> added, string region, double? insertionY)
    {
        var y = page.Blocks.Where(block => block.Region == region).Select(block => Numeric(block, "studioY") + Math.Max(80, Numeric(block, "studioHeight"))).DefaultIfEmpty(-24).Max() + 24;
        y = Math.Max(y, insertionY ?? 0);
        foreach (var block in added)
        {
            block.Values["studioPositioned"] = ContentJson.Value(true); block.Values["studioX"] = ContentJson.Value(0);
            block.Values["studioY"] = ContentJson.Value(y); block.Values["studioWidth"] = ContentJson.Value(100); block.Values["studioHeight"] = ContentJson.Value(0);
            y += 264;
        }
        static double Numeric(PageBlock block, string field) => block.Values.TryGetValue(field, out var value) && value.TryGetDouble(out var number) ? number : 0;
    }
    private static async Task<SavedPage> ReadSavedAsync(string library, string id, CancellationToken ct)
    {
        var path = HyperlibProjectPipelineService.Guard(ReusableTemplateCatalog.GetProjectRoot(library, id), ".studio/studio-saved-page.json");
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("The saved Page template exceeds the supported size.");
        return JsonSerializer.Deserialize<SavedPage>(await File.ReadAllTextAsync(path, ct), ContentJson.Options) ?? throw new InvalidDataException("The saved Page template is empty.");
    }
    private static string Kind(HyperlibWorkTemplateChoice choice) => choice.Kind is "palette" or "typography" or "style" ? choice.Kind : Kind(choice.ComponentName);
    private static string Kind(string component) => component.Split('.').Last() switch
    { "TextBlock" or "RichTextBlock" or "CitationBlock" => "text", "ImageBlock" or "GalleryBlock" => "image", "VideoBlock" => "video", "AudioBlock" => "audio",
        "HeaderBlock" or "FooterBlock" or "BannerBlock" => "layout", "WorkNavigationBlock" or "StudioWorkNavigation" or "StudioNavigationLinksBlock" or "StudioNavigationLayoutBlock" => "navigation", "" => "layout", _ => "tool" };
    private static HyperlibPageTemplateDefinition SectionRecipe(string section, string id)
    {
        var recipe = new HyperlibPageTemplateDefinition { HeaderPlacement = "none", FooterPlacement = "none", IncludeNavigation = false };
        switch (section)
        {
            case "header": recipe.HeaderPlacement = "top"; recipe.HeaderLayout = HyperlibHeaderLayouts.Create(id); break;
            case "navigation": recipe.IncludeNavigation = true; recipe.NavigationLayout = HyperlibNavigationLayouts.Create(id); break;
            case "body": recipe.BodyLayout = HyperlibBodyLayouts.Create(id); break;
            case "footer": recipe.FooterPlacement = "bottom"; recipe.FooterLayout = HyperlibFooterLayouts.Create(id); break;
            default: throw new InvalidDataException("Choose a supported section.");
        }
        return recipe;
    }
    private static async Task<List<PageBlock>> PrepareSectionAsync(Project project, string section, string preset, CancellationToken ct)
    {
        var recipe = SectionRecipe(section, preset);
        var layout = section switch { "header" => recipe.HeaderLayout, "navigation" => recipe.NavigationLayout, "body" => recipe.BodyLayout, _ => recipe.FooterLayout };
        var schemas = ContentJson.Clone(project.Definition);
        HyperlibNavigationLayouts.EnsureBlockDefinitions(schemas);
        var files = new List<TemplateImportFile>();
        var blocks = new List<PageBlock>();
        var presetTitle = (section switch { "header" => HyperlibHeaderLayouts.All, "navigation" => HyperlibNavigationLayouts.All,
            "body" => HyperlibBodyLayouts.All, _ => HyperlibFooterLayouts.All }).Single(item => item.Id == preset).Title;
        for (var rowIndex = 0; rowIndex < layout.Blocks.Count; rowIndex++)
        {
            var row = layout.Blocks[rowIndex]; row.Id = "section-row-" + rowIndex;
            var elements = HyperlibHeaderLayouts.Elements(new() { Enabled = true, Blocks = [row] }).ToArray();
            for (var index = 0; index < elements.Length; index++) { elements[index].Id = "section-element-" + index; elements[index].IsContentSlot = false; }
            var json = JsonSerializer.Serialize(row, ContentJson.Options);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(section + ":" + preset + ":" + json + ":" + HyperlibHeaderRenderingSource.ComponentSource)))[..16];
            var type = "library-section-" + hash;
            var component = "StudioLibrarySection" + hash;
            var schema = new BlockDefinition { Id = type, Component = component, Title = presetTitle + (layout.Blocks.Count > 1 ? " · Block " + (rowIndex + 1) : ""),
                Category = char.ToUpperInvariant(section[0]) + section[1..], Description = "An independent section. Edit its text, media and appearance here; move or resize it on the canvas." };
            var fieldCases = new StringBuilder();
            for (var index = 0; index < elements.Length; index++)
            {
                var element = elements[index]; var key = "part-" + index + "-";
                fieldCases.Append(JsonSerializer.Serialize(element.Id)).Append(" => ").Append(JsonSerializer.Serialize(key)).Append(" + name,");
                void Field(string id, string label, string fieldType, object value, double? min = null, double? max = null, params string[] options)
                    => schema.Fields.Add(new() { Id = key + id, Title = (string.IsNullOrWhiteSpace(element.SlotName) ? "Element " + (index + 1) : element.SlotName) + " · " + label,
                        Type = fieldType, DefaultValue = ContentJson.Value(value), Min = min, Max = max, Options = options.ToList() });
                if (element.Kind == "text")
                {
                    Field("text", "Text", "text", element.Text); Field("size", "Font size", "number", element.FontSize, 8, 120);
                    Field("color", "Text color", "color", element.TextColor);
                    Field("font", "Font", "select", element.FontFamily, null, null, "system-ui", "Arial", "Georgia", "Verdana", "Times New Roman", "Courier New");
                    Field("align", "Alignment", "select", element.Alignment, null, null, "left", "center", "right");
                }
                else if (element.Kind is "image" or "video")
                { Field("src", element.Kind == "image" ? "Image" : "Video", "media", element.MediaPath); Field("alt", "Description", "string", element.Alt); }
                else if (element.Kind == "box")
                {
                    if (!string.IsNullOrWhiteSpace(element.FillColor)) Field("fill", "Background", "color", element.FillColor);
                    Field("border", "Border width", "number", element.BorderWidth, 0, 20); Field("radius", "Corner radius", "number", element.BorderRadius, 0, 200);
                    Field("padding", "Padding", "number", element.Padding, 0, 200);
                }
                else if (element.Kind == "navigation" && element.Navigation is { } navigation)
                {
                    Field("home", "Show Work home", "boolean", navigation.IncludeHome); Field("home-label", "Home label", "string", navigation.HomeLabel);
                    Field("pages", "List Work Pages", "boolean", navigation.AutoPages); Field("previous-next", "Previous and next Pages", "boolean", navigation.ShowPreviousNext);
                    Field("align", "Alignment", "select", navigation.Alignment, null, null, "left", "center", "right");
                }
            }
            var source = HyperlibHeaderRenderingSource.ComponentSource
                .Replace("var json = Context.Text(\"layout\");", "var json = " + JsonSerializer.Serialize(json) + ";", StringComparison.Ordinal)
                .Replace("if (_row is not null)", "if (_row is not null) ApplyLibraryFields(_row);\n        if (_row is not null)", StringComparison.Ordinal)
                .Replace("builder.AddAttribute(21, \"class\", \"studio-header-text\");", "builder.AddAttribute(21, \"class\", \"studio-header-text\");\n                    builder.AddAttribute(23, \"data-field\", LibraryField(element.Id, \"text\"));", StringComparison.Ordinal)
                .Replace("private HyperlibHeaderBlockDefinition? _row;", "private HyperlibHeaderBlockDefinition? _row;\n" + SectionFieldCode(fieldCases.ToString()), StringComparison.Ordinal);
            var bytes = Encoding.UTF8.GetBytes(source);
            var relative = "src/Pages/Blocks/" + component + ".razor";
            var filePath = HyperlibProjectPipelineService.Guard(project.Root, relative);
            if (File.Exists(filePath) && !File.ReadAllBytes(filePath).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("This independent section's source has changed. Preserve it before adding this preset again.");
            if (!schemas.Blocks.Any(block => block.Id == type)) schemas.Blocks.Add(schema);
            files.Add(new(relative, bytes, Convert.ToHexString(SHA256.HashData(bytes))));
            blocks.Add(DefaultBlock(schema, "main"));
        }
        HyperlibProjectPipelineService.EnsureDirectEditorFields(schemas);
        foreach (var block in blocks)
            foreach (var field in schemas.Blocks.Single(schema => schema.Id == block.Type).Fields.Where(field => field.DefaultValue.HasValue))
                block.Values.TryAdd(field.Id, field.DefaultValue!.Value.Clone());
        TemplateValidator.ValidateDefinition(schemas);
        await HyperlibStudioAuthoringService.CheckCompatibilityAsync(project.Root, schemas);
        var definitionPath = HyperlibProjectPipelineService.Guard(project.Root, "authoring.template.json");
        var plan = new TemplateImportPlan { TargetProjectRoot = project.Root, Definition = schemas, Files = files, SourceFiles = [], TargetFiles = [], Summary = "Independent " + section + " section" };
        await TemplateObjectImporter.CommitAsync(project.Root, schemas, [plan], Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(definitionPath, ct))), ct);
        return blocks;
    }
    private static string SectionFieldCode(string cases) => """
        private static string? LibraryField(string id, string name) => id switch {
        """ + cases + " _ => null };\n" + """
        private bool LibraryFlag(string key, bool fallback) => string.Equals(Context.Text(key, fallback.ToString()), "true", StringComparison.OrdinalIgnoreCase);
        private void ApplyLibraryFields(HyperlibHeaderBlockDefinition row)
        {
            foreach (var element in HyperlibHeaderLayouts.Elements(new HyperlibHeaderLayoutDefinition { Enabled=true, Blocks=[row] }))
            {
                var key=LibraryField(element.Id, ""); if(key is null)continue;
                if(element.Kind=="text") {
                    element.Text=Context.Text(key+"text",element.Text); element.FontSize=Context.Number(key+"size",element.FontSize);
                    element.TextColor=Context.Text(key+"color",element.TextColor); element.FontFamily=Context.Text(key+"font",element.FontFamily);
                    element.Alignment=Context.Text(key+"align",element.Alignment);
                }
                else if(element.Kind is "image" or "video") { element.MediaPath=Context.Text(key+"src",element.MediaPath); element.Alt=Context.Text(key+"alt",element.Alt); }
                else if(element.Kind=="box") {
                    element.FillColor=Context.Text(key+"fill",element.FillColor); element.BorderWidth=Context.Number(key+"border",element.BorderWidth);
                    element.BorderRadius=Context.Number(key+"radius",element.BorderRadius); element.Padding=Context.Number(key+"padding",element.Padding);
                }
                else if(element.Kind=="navigation" && element.Navigation is { } nav) {
                    nav.IncludeHome=LibraryFlag(key+"home",nav.IncludeHome); nav.HomeLabel=Context.Text(key+"home-label",nav.HomeLabel);
                    nav.AutoPages=LibraryFlag(key+"pages",nav.AutoPages); nav.ShowPreviousNext=LibraryFlag(key+"previous-next",nav.ShowPreviousNext);
                    nav.Alignment=Context.Text(key+"align",nav.Alignment);
                }
            }
        }
        """;

    private static async Task<PageBlock> PrepareModuleAsync(Project project, List<PageBlock> originals, string title, CancellationToken ct)
    {
        var blocks = ContentJson.Clone(originals);
        var schema = new BlockDefinition { Title = title, Category = "Modules", Description = "An independent group. Edit each object's properties here; move or resize the whole module on the canvas." };
        var components = new List<string>();
        for (var index = 0; index < blocks.Count; index++)
        {
            var source = project.Definition.Blocks.Single(item => item.Id == blocks[index].Type);
            var values = source.Fields.Where(field => field.DefaultValue.HasValue).ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);
            foreach (var pair in blocks[index].Values) values[pair.Key] = pair.Value.Clone();
            foreach (var key in values.Keys.Where(key => key.StartsWith("studio", StringComparison.Ordinal)).ToArray()) values.Remove(key);
            blocks[index].Values = values;
            foreach (var field in source.Fields.Where(field => !field.Id.StartsWith("studio", StringComparison.Ordinal)))
            {
                var copy = ContentJson.Clone(field); copy.Id = "object-" + index + "-" + field.Id; copy.Title = source.Title + " · " + field.Title;
                if (values.TryGetValue(field.Id, out var value)) copy.DefaultValue = value.Clone(); schema.Fields.Add(copy);
            }
            components.Add("typeof(" + ComponentTypeName(project.Root, source.Component) + ")");
        }
        var json = JsonSerializer.Serialize(blocks, ContentJson.Options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("module-v1:" + json + ":" + string.Join(',', components))))[..16];
        schema.Id = "library-module-" + hash; schema.Component = "StudioLibraryModule" + hash;
        var sourceCode = """
            @using System.Text.Json
            @using HyperLib.Core
            @using HyperLib.Runtime
            @using Microsoft.AspNetCore.Components
            @using Microsoft.AspNetCore.Components.Rendering
            @using Microsoft.AspNetCore.Components.Web
            @RenderLibraryModule()
            @code {
                [Parameter, EditorRequired] public BlockContext Context { get; set; } = new();
            """ + "\nprivate static readonly PageBlock[] Originals = JsonSerializer.Deserialize<PageBlock[]>(" + JsonSerializer.Serialize(json) + ", ContentJson.Options)!;\n"
            + "private static readonly Type[] Components = [" + string.Join(',', components) + "];\n" + """
                private RenderFragment RenderLibraryModule() => builder => {
                    builder.OpenElement(0,"div"); builder.AddAttribute(1,"class","hl-region");
                    builder.AddAttribute(2,"style","max-width:100%;margin:0;align-content:start");
                    builder.AddAttribute(3,"data-studio-shared-template","true");
                    for(var index=0;index<Originals.Length;index++) {
                        var block=ContentJson.Clone(Originals[index]); var prefix="object-"+index+"-";
                        foreach(var pair in Context.Values) if(pair.Key.StartsWith(prefix,StringComparison.Ordinal))block.Values[pair.Key[prefix.Length..]]=pair.Value.Clone();
                        var instance=Context.Id+"--module-"+index;
                        builder.OpenElement(10,"section"); builder.SetKey(instance);
                        builder.AddAttribute(11,"class","hl-block hl-type-"+block.Type+" "+RendererOptions.BlockSpanClass(block));
                        builder.AddAttribute(12,"style",RendererOptions.BlockStyle(block));
                        builder.OpenComponent<ErrorBoundary>(13); builder.SetKey(instance+":"+JsonSerializer.Serialize(block.Values,ContentJson.Options));
                        var position=index;
                        builder.AddAttribute(14,"ChildContent",(RenderFragment)(child=>{
                            child.OpenComponent<DynamicComponent>(0); child.AddAttribute(1,"Type",Components[position]);
                            child.AddAttribute(2,"Parameters",new Dictionary<string,object>{["Context"]=new BlockContext{Id=instance,Editing=false,Values=block.Values}});child.CloseComponent();
                        }));
                        builder.AddAttribute(15,"ErrorContent",(RenderFragment<Exception>)(error=>child=>{child.OpenElement(0,"p");child.AddAttribute(1,"role","alert");child.AddContent(2,error.Message);child.CloseElement();}));
                        builder.CloseComponent(); builder.CloseElement();
                    }
                    builder.CloseElement();
                };
            }
            """;
        var definition = ContentJson.Clone(project.Definition);
        if (!definition.Blocks.Any(block => block.Id == schema.Id)) definition.Blocks.Add(schema);
        HyperlibProjectPipelineService.EnsureDirectEditorFields(definition);
        var bytes = Encoding.UTF8.GetBytes(sourceCode); var relative = "src/Pages/Blocks/" + schema.Component + ".razor";
        var plan = new TemplateImportPlan { TargetProjectRoot = project.Root, Definition = definition,
            Files = [new(relative, bytes, Convert.ToHexString(SHA256.HashData(bytes)))], SourceFiles = [], TargetFiles = [], Summary = "Independent multi-object module" };
        await HyperlibStudioAuthoringService.CheckCompatibilityAsync(project.Root, definition);
        await TemplateObjectImporter.CommitAsync(project.Root, definition, [plan], Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(project.Root, "authoring.template.json"), ct))), ct);
        return DefaultBlock(definition.Blocks.Single(block => block.Id == schema.Id), "main");
    }
    private static string ComponentTypeName(string root, string component)
    {
        if (component.Contains('.')) return component;
        var pages = HyperlibProjectPipelineService.Guard(root, "src/Pages");
        var sources = Directory.EnumerateFiles(pages, component + ".razor", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(pages, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")).ToArray();
        if (sources.Length != 1) throw new InvalidDataException("The module component source is missing or ambiguous: " + component);
        var path = HyperlibProjectPipelineService.Guard(root, Path.GetRelativePath(root, sources[0]));
        var source = File.ReadAllText(path);
        var declared = System.Text.RegularExpressions.Regex.Match(source, @"(?m)^\s*@namespace\s+([A-Za-z_][A-Za-z0-9_.]*)");
        if (declared.Success) return declared.Groups[1].Value + "." + component;
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(pages, "Pages.csproj"));
        var ns = project.Descendants().FirstOrDefault(element => element.Name.LocalName == "RootNamespace")?.Value ?? "HyperLib.Work";
        var folder = Path.GetDirectoryName(Path.GetRelativePath(pages, path));
        return ns + (string.IsNullOrWhiteSpace(folder) ? "" : "." + folder.Replace(Path.DirectorySeparatorChar, '.')) + "." + component;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _objects.Dispose(); _pages.Dispose(); _catalog.Dispose(); }
}
