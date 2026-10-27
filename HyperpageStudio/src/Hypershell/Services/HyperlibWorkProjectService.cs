using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Creates an independent portable library around one Work; catalogue reads never change the source library.</summary>
public sealed partial class HyperlibWorkProjectService
{
    private readonly string _sourceRoot;
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".hyperlib", ".vs", "node_modules", "artifacts", "output" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif" };

    public HyperlibWorkProjectService(string? sourceLibraryRoot = null)
    {
        _sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLibraryRoot ?? FindSourceLibrary()));
        RejectLinks(_sourceRoot);
    }

    public string TemplateLibraryRoot => _sourceRoot;

    public IReadOnlyList<HyperlibWorkStarterChoice> GetTemplates() => ReusableTemplateCatalog.List(_sourceRoot)
        .Select(item => new HyperlibWorkStarterChoice(item.Id, item.Title, item.Description, item.Category, item.Background, item.Accent)).ToArray();

    public IReadOnlyList<HyperlibWorkTemplateChoice> GetCatalog()
    {
        var result = new List<HyperlibWorkTemplateChoice>();
        var groups = TemplateObjectLibrary.ListDesignGroups(_sourceRoot).Where(group => !IsApplicationTemplateGroup(group.Id))
            .Concat(TemplateObjectLibrary.ListDesignGroups(ApplicationLibraryRoot()).Where(group => IsApplicationTemplateGroup(group.Id))).ToArray();
        foreach (var group in groups)
            foreach (var item in TemplateObjectLibrary.ListObjects(CatalogLibraryRoot(group.Id), group.Id))
                result.Add(new(group.Id + ":" + item.Id, item.Title, item.Description, item.Category,
                    item.Kind, group.Id, item.Id, group.Title, ComponentName(item.Element?.Component ?? "")));
        // Saved Templates need not have been published as Design Groups yet. Read their own
        // complete schema without initializing or changing anything in the user's source library.
        foreach (var starter in ReusableTemplateCatalog.List(_sourceRoot).Where(item => !groups.Any(group => group.Id == item.Id)))
            {
                var definition = ReusableTemplateCatalog.ReadDefinition(_sourceRoot, starter.Id);
                foreach (var element in definition.Blocks)
                    result.Add(new(starter.Id + ":element-" + element.Id, element.Title, element.Description ?? "Reusable " + element.Title.ToLowerInvariant() + ".",
                        TemplateObjectLibrary.NormalizeCategory(element.Category ?? "Elements"), "element", starter.Id, "element-" + element.Id, starter.Title, ComponentName(element.Component)));
                foreach (var example in definition.DefaultBlocks)
                {
                    var element = definition.Blocks.Single(item => item.Id == example.Type);
                    var title = example.Values.TryGetValue("title", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                        ? value.GetString()! : element.Title + " example";
                    result.Add(new(starter.Id + ":example-" + example.Id, title, "Configured " + element.Title + " from " + starter.Title + ".",
                        TemplateObjectLibrary.NormalizeCategory(element.Category ?? "Elements"), "preset", starter.Id, "example-" + example.Id, starter.Title, ComponentName(element.Component)));
                }
                if (definition.DefaultBlocks.Count > 0)
                    result.Add(new(starter.Id + ":page-layout", starter.Title + " page layout", starter.Description, "Layouts & structure", "layout", starter.Id, "page-layout", starter.Title, ""));
                foreach (var region in definition.Regions)
                    result.Add(new(starter.Id + ":region-" + region.Id, region.Title + " region", "Reusable named region and its allowed elements.", "Navigation", "region", starter.Id, "region-" + region.Id, starter.Title, ""));
                if (definition.Appearance.Any(field => field.Id is "font" or "headingFont" or "fontSize"))
                    result.Add(new(starter.Id + ":typography", starter.Title + " typography", "Body and heading typography.", "Typography", "typography", starter.Id, "typography", starter.Title, ""));
                if (definition.Appearance.Any(field => field.Type == "color"))
                    result.Add(new(starter.Id + ":palette", starter.Title + " colors", "Page, text, surface, and accent colors.", "Colors & surfaces", "palette", starter.Id, "palette", starter.Title, ""));
                if (definition.Appearance.Count > 0)
                    result.Add(new(starter.Id + ":work-style", starter.Title + " Work style", "Shared structure, colors, typography, and appearance defaults.", "Work styles", "style", starter.Id, "work-style", starter.Title, ""));
            }
        return result;
    }

    public string GetCatalogProjectRoot(string groupId)
    {
        TemplateValidator.ValidateId(groupId, "design group");
        if (IsApplicationTemplateGroup(groupId)) return TemplateObjectLibrary.GetDesignGroup(ApplicationLibraryRoot(), groupId).ProjectRoot;
        if (Directory.Exists(Path.Combine(_sourceRoot, "CoreLibrary", "DesignGroups", groupId)))
            return TemplateObjectLibrary.GetDesignGroup(_sourceRoot, groupId).ProjectRoot;
        return ReusableTemplateCatalog.GetProjectRoot(_sourceRoot, groupId);
    }

    public TemplateLibraryObject GetCatalogObject(string groupId, string objectId)
    {
        if (IsApplicationTemplateGroup(groupId))
            return TemplateObjectLibrary.ListObjects(ApplicationLibraryRoot(), groupId).SingleOrDefault(item => item.Id == objectId)
                ?? throw new InvalidDataException("The application pattern is no longer available.");
        if (Directory.Exists(Path.Combine(_sourceRoot, "CoreLibrary", "DesignGroups", groupId)))
            return TemplateObjectLibrary.ListObjects(_sourceRoot, groupId).SingleOrDefault(item => item.Id == objectId)
                ?? throw new InvalidDataException("The object is no longer in this collection.");
        var choice = GetCatalog().SingleOrDefault(item => item.Id == groupId + ":" + objectId)
            ?? throw new InvalidDataException("The object is no longer in this collection.");
        var definition = ReusableTemplateCatalog.ReadDefinition(_sourceRoot, groupId);
        var element = choice.Kind == "element" ? definition.Blocks.Single(item => "element-" + item.Id == objectId)
            : choice.Kind == "preset" ? definition.Blocks.Single(item => item.Id == definition.DefaultBlocks.Single(block => "example-" + block.Id == objectId).Type) : null;
        var blocks = choice.Kind == "layout" ? definition.DefaultBlocks
            : choice.Kind == "preset" ? definition.DefaultBlocks.Where(block => "example-" + block.Id == objectId).ToList() : [];
        var regions = choice.Kind == "layout" ? definition.Regions
            : choice.Kind == "region" ? definition.Regions.Where(region => "region-" + region.Id == objectId).ToList() : [];
        var appearance = choice.Kind == "palette" ? definition.Appearance.Where(field => field.Type == "color").ToList()
            : choice.Kind == "typography" ? definition.Appearance.Where(field => field.Id is "font" or "headingFont" or "fontSize").ToList()
            : choice.Kind == "style" ? definition.Appearance : choice.Kind == "layout" ? definition.Appearance.Where(field => field.Id == "variant").ToList() : [];
        return new() { Id = objectId, GroupId = groupId, Title = choice.Title, Description = choice.Description, Kind = choice.Kind,
            Category = choice.Category, CatalogId = TemplateObjectLibrary.CatalogIdFor(groupId, objectId), SourceTemplateId = groupId,
            Element = element, Blocks = blocks, Regions = regions, Appearance = appearance, Definition = definition };
    }

    public async Task<string> CreateAsync(string parentDirectory, string name, HyperlibWorkProjectDefinition definition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.PipelineVersion == 2) return await CreatePipelineAsync(parentDirectory, name, definition, ct);
        if (definition.PipelineVersion != 1) throw new InvalidDataException("Unsupported Hyperlib project pipeline version.");
        Validate(definition);
        ValidateFolderName(name);
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
            await WriteAsync(staging, "hyperlib.app.json", JsonSerializer.Serialize(new { schemaVersion = 1, id = "hyperlib-work", title = definition.WorkTitle }, ContentJson.Options), ct);
            var workId = "work-" + Guid.NewGuid().ToString("N")[..16];
            var workRoot = Within(staging, "Works/" + workId);
            var neededGroups = AllPages(definition).SelectMany(SelectedIds).Select(ParseChoice).Select(item => item.GroupId).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var group in neededGroups)
            {
                var source = Path.Combine(CatalogLibraryRoot(group), "CoreLibrary", "DesignGroups", group);
                if (Directory.Exists(source)) await CopyDirectoryAsync(source, Path.Combine(staging, "CoreLibrary", "DesignGroups", group), ct);
                else
                {
                    _ = ReusableTemplateCatalog.Find(_sourceRoot, group);
                    if (!ReusableTemplateCatalog.IsBuiltIn(group))
                        await CopyDirectoryAsync(Path.Combine(_sourceRoot, "Templates", group), Path.Combine(staging, "Templates", group), ct);
                }
            }
            if (!ReusableTemplateCatalog.IsBuiltIn(definition.TemplateId))
                await CopyDirectoryAsync(Path.Combine(_sourceRoot, "Templates", definition.TemplateId), Path.Combine(staging, "Templates", definition.TemplateId), ct);
            await TemplateProjectScaffolder.EnsureAsync(staging, staging, "library", definition.WorkTitle, ct);
            await TemplateProjectScaffolder.EnsureAsync(staging, workRoot, workId, definition.WorkTitle, ct, definition.TemplateId);
            // Initialize missing object snapshots only inside this new private project, never in the live library.
            if (neededGroups.Any(group => !Directory.Exists(Path.Combine(staging, "CoreLibrary", "DesignGroups", group))))
                await TemplateObjectLibrary.EnsureCoreLibraryAsync(staging, ct);
            var templatePath = Path.Combine(workRoot, "authoring.template.json");
            var template = TemplateObjectLibrary.ReadDefinition(workRoot);
            var pageBlocks = new List<List<PageBlock>>();
            var choices = new Dictionary<string, (TemplateLibraryObject Object, string Type)>(StringComparer.Ordinal);
            foreach (var page in AllPages(definition))
            {
                var blocks = ContentJson.Clone(template.DefaultBlocks);
                if (!string.IsNullOrWhiteSpace(page.LayoutChoiceId))
                {
                    var imported = await ImportAsync(page.LayoutChoiceId, template, ct);
                    template = imported;
                    blocks = ContentJson.Clone(template.DefaultBlocks);
                }
                foreach (var id in SelectedIds(page).Where(id => id != page.LayoutChoiceId).Distinct(StringComparer.Ordinal))
                {
                    if (choices.ContainsKey(id)) continue;
                    var selection = ParseChoice(id);
                    var item = TemplateObjectLibrary.ListObjects(staging, selection.GroupId).SingleOrDefault(item => item.Id == selection.ObjectId)
                        ?? throw new InvalidDataException("A selected template object no longer exists. Refresh the wizard and select it again.");
                    if (item.Element is null) throw new InvalidDataException("Choose an element or configured element for this section.");
                    var previousIds = template.Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
                    template = await ImportAsync(id, template, ct);
                    var importedBlock = template.Blocks.LastOrDefault(block => !previousIds.Contains(block.Id))
                        ?? template.Blocks.LastOrDefault(block => ComponentName(block.Component) == ComponentName(item.Element.Component))
                        ?? throw new InvalidDataException("The selected component did not provide an editable block.");
                    choices.Add(id, (item, importedBlock.Id));
                }
                pageBlocks.Add(blocks);
            }
            template.Appearance.RemoveAll(field => field.Id == "workLandingPage");
            template.Appearance.Add(new() { Id = "workLandingPage", Title = "Work landing Page", Type = "string" });
            template.Appearance.RemoveAll(field => field.Id == "workBackgroundImage");
            template.Appearance.Add(new() { Id = "workBackgroundImage", Title = "Full page background image", Type = "media" });
            template.Blocks.Add(new() { Id = "work-navigation", Title = "Work page navigation", Component = "WorkNavigationBlock", Category = "Navigation",
                Fields = [new() { Id = "items", Title = "Pages (title | stable ID)", Type = "text" }, new() { Id = "current", Title = "Current page ID", Type = "string" },
                    new() { Id = "autoPages", Title = "Automatically include all Work Pages", Type = "boolean", DefaultValue = ContentJson.Value(true) }] });
            foreach (var region in template.Regions) if (region.AllowedBlocks.Count > 0) region.AllowedBlocks.Add("work-navigation");
            await WriteAsync(workRoot, "src/Pages/Blocks/WorkNavigationBlock.razor", NavigationComponent, ct);
            var pageIds = AllPages(definition).Select((_, index) => index == 0 ? "landing-" + workId : "page-" + (index).ToString("00") + "-" + Guid.NewGuid().ToString("N")[..8]).ToArray();
            var state = new LibraryState { LibraryId = "library-" + Guid.NewGuid().ToString("N")[..12], Nodes = [] };
            var collectionId = "project-collection";
            state.Nodes.Add(new() { Id = collectionId, Type = "collection", ParentId = state.LibraryId, Title = definition.WorkTitle });
            var workNode = new LibraryNode { Id = workId, Type = "work", ParentId = collectionId, Title = definition.WorkTitle, StarterId = definition.TemplateId,
                Appearance = DefaultAppearance(template) };
            workNode.Appearance["workLandingPage"] = ContentJson.Value(pageIds[0]);
            state.Nodes.Add(workNode);
            var pages = AllPages(definition).ToArray();
            var navigation = string.Join('\n', pages.Select((page, index) => (index == 0 ? definition.WorkTitle : page.Title) + "|" + pageIds[index]));
            if (definition.LandingPage.IncludeNavigation)
            {
                template.DefaultBlocks.RemoveAll(block => block.Type == "work-navigation");
                template.DefaultBlocks.Add(new() { Id = "default-work-navigation", Type = "work-navigation", Region = NavigationRegionFor(template, template.DefaultBlocks),
                    Values = new() { ["items"] = ContentJson.Value(navigation), ["current"] = ContentJson.Value("") } });
            }
            for (var index = 0; index < pages.Length; index++)
            {
                var page = pages[index];
                var blocks = pageBlocks[index];
                if (blocks.Count == 0)
                {
                    blocks.Add(NewBlock(template.Blocks.First(block => ComponentName(block.Component) == "HeaderBlock").Id, "main"));
                    blocks.Add(NewBlock(template.Blocks.First(block => ComponentName(block.Component) == "TextBlock").Id, "main"));
                }
                ReplaceSection(page.HeaderChoiceId, ["HeaderBlock", "BannerBlock"], index == 0 ? definition.WorkTitle : page.Title, page.Description, true);
                ReplaceSection(page.BodyChoiceId, ["TextBlock", "RichTextBlock"], page.Title,
                    string.IsNullOrWhiteSpace(page.Description) ? "This is your starting point. Replace this text with your own story, then add objects from the template collection in Hyperpage Studio." : page.Description, false);
                var images = page.Images.ToList();
                if (!string.IsNullOrWhiteSpace(page.ImagePath)) images.Add(new() { Path = page.ImagePath, Alt = page.ImageAlt });
                if (images.Count == 0 && (index == 0 || !string.IsNullOrWhiteSpace(page.ImageChoiceId)))
                {
                    var illustration = "Media/work-starter/" + pageIds[index] + ".svg";
                    await WriteAsync(staging, illustration, Illustration(index == 0 ? definition.WorkTitle : page.Title, page.Accent ?? "#65a88e"), ct);
                    images.Add(new() { Path = "/media/work-starter/" + pageIds[index] + ".svg", Alt = "Abstract geometric starter illustration", Caption = "Replace this starter illustration with your own image." });
                }
                if (images.Count > 0)
                {
                    var imageInsertionIndex = blocks.FindIndex(block => IsComponent(block, "ImageBlock", "GalleryBlock"));
                    if (imageInsertionIndex < 0) imageInsertionIndex = Math.Max(1, blocks.FindIndex(block => IsComponent(block, "TextBlock", "RichTextBlock")) + 1);
                    var imageRegion = blocks.FirstOrDefault(block => IsComponent(block, "ImageBlock", "GalleryBlock"))?.Region
                        ?? blocks.FirstOrDefault(block => IsComponent(block, "TextBlock", "RichTextBlock"))?.Region
                        ?? blocks.FirstOrDefault()?.Region;
                    blocks.RemoveAll(block => IsComponent(block, "ImageBlock", "GalleryBlock"));
                    imageInsertionIndex = Math.Min(imageInsertionIndex, blocks.Count);
                    var imageType = page.ImageChoiceId is { Length: > 0 } imageId ? choices[imageId].Type : template.Blocks.First(block => ComponentName(block.Component) == "ImageBlock").Id;
                    for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
                    {
                        var image = images[imageIndex];
                        var url = await ImageUrlAsync(image.Path, workRoot, ct);
                        var imageBlock = NewBlock(imageType, SectionRegionFor(template, imageType, imageRegion));
                        SetField(imageBlock, "src", url); SetField(imageBlock, "src1", url);
                        SetField(imageBlock, "alt", image.Alt); SetField(imageBlock, "caption", image.Caption);
                        if (ComponentName(template.Blocks.Single(block => block.Id == imageType).Component) == "GalleryBlock" && imageIndex + 1 < images.Count)
                            SetField(imageBlock, "src2", await ImageUrlAsync(images[++imageIndex].Path, workRoot, ct));
                        blocks.Insert(imageInsertionIndex++, imageBlock);
                    }
                }
                foreach (var specialId in page.SpecialChoiceIds)
                {
                    var selected = choices[specialId];
                    var footerIndex = blocks.FindIndex(block => IsComponent(block, "FooterBlock"));
                    var block = NewBlock(selected.Type, SectionRegionFor(template, selected.Type, footerIndex >= 0 ? blocks[footerIndex].Region : blocks.LastOrDefault()?.Region));
                    // Importer already places rewritten preset values into this block type's field defaults.
                    // Reusing the source preset here would undo local dependency/asset rewrites.
                    if (footerIndex >= 0) blocks.Insert(footerIndex, block); else blocks.Add(block);
                }
                if (page.IncludeNavigation)
                    blocks.Add(new() { Id = "navigation-" + pageIds[index], Type = "work-navigation", Region = NavigationRegionFor(template, blocks),
                        Values = new() { ["items"] = ContentJson.Value(navigation), ["current"] = ContentJson.Value(pageIds[index]) } });
                var node = new LibraryNode { Id = pageIds[index], Type = "page", ParentId = workId, Title = index == 0 ? "Work landing · " + definition.WorkTitle[..Math.Min(170, definition.WorkTitle.Length)] : page.Title, Order = index,
                    Appearance = new(StringComparer.Ordinal) };
                if (page.LayoutChoiceId is { Length: > 0 } layoutId)
                    foreach (var field in TemplateObjectLibrary.ListObjects(staging, ParseChoice(layoutId).GroupId).Single(item => item.Id == ParseChoice(layoutId).ObjectId).Appearance)
                        if (field.DefaultValue is { } value) node.Appearance[field.Id] = value.Clone();
                if (!string.IsNullOrWhiteSpace(page.Background))
                {
                    node.Appearance["background"] = ContentJson.Value(page.Background);
                    ApplyContrast(node.Appearance, page.Background);
                }
                if (!string.IsNullOrWhiteSpace(page.Accent)) node.Appearance["accent"] = ContentJson.Value(page.Accent);
                if (!string.IsNullOrWhiteSpace(page.BackgroundImagePath)) node.Appearance["workBackgroundImage"] = ContentJson.Value(await ImageUrlAsync(page.BackgroundImagePath, workRoot, ct));
                state.Nodes.Add(node);
                state.Pages[node.Id] = new() { Id = node.Id, WorkId = workId, Title = node.Title, TemplateId = template.TemplateId, UsesEditableChrome = true, Blocks = blocks };

                void ReplaceSection(string? choiceId, string[] componentNames, string title, string text, bool header)
                {
                    var existing = blocks.FirstOrDefault(block => IsComponent(block, componentNames));
                    if (!string.IsNullOrWhiteSpace(choiceId))
                    {
                        var targetRegion = existing?.Region ?? blocks.FirstOrDefault()?.Region;
                        blocks.RemoveAll(block => IsComponent(block, componentNames));
                        existing = NewBlock(choices[choiceId].Type, SectionRegionFor(template, choices[choiceId].Type, targetRegion));
                        if (header) blocks.Insert(0, existing); else blocks.Insert(Math.Min(1, blocks.Count), existing);
                    }
                    if (existing is null) return;
                    if (header) { SetField(existing, "title", title); SetField(existing, "eyebrow", index == 0 ? "Work" : definition.WorkTitle); }
                    SetField(existing, "text", text);
                }
                bool IsComponent(PageBlock block, params string[] names) => names.Contains(ComponentName(template.Blocks.Single(item => item.Id == block.Type).Component), StringComparer.Ordinal);
                void SetField(PageBlock block, string field, object value)
                {
                    if (template.Blocks.Single(item => item.Id == block.Type).Fields.Any(item => item.Id == field))
                        block.Values[field] = value is JsonElement element ? element.Clone() : ContentJson.Value(value);
                }
            }
            TemplateValidator.ValidateDefinition(template);
            await WriteAsync(workRoot, "authoring.template.json", JsonSerializer.Serialize(template, ContentJson.Options), ct);
            await WriteAsync(staging, "Content/library.json", JsonSerializer.Serialize(state, ContentJson.Options), ct);
            var store = new ContentStore(staging) { TemplateResolver = package => package == workId ? template : null };
            await store.InitializeAsync(ct);
            state = await store.ReadAsync(ct);
            ContentRules.Validate(state, package => package == workId ? template : null);
            await WriteAsync(staging, "src/HyperLib.Runtime/Components/BrowsePage.razor", BrowseComponent, ct);
            var dynamicPagePath = Path.Combine(staging, "src", "HyperLib.Runtime", "Components", "DynamicPage.razor");
            var dynamicPage = DynamicPageComponent;
            dynamicPage = dynamicPage.Replace("private string AppearanceStyle => Options.AppearanceStyle(_state, _pageNode ?? _work);",
                "private string AppearanceStyle => Options.AppearanceStyle(_state, _pageNode ?? _work) + BackgroundImageStyle;\n    private string BackgroundImageStyle { get { var image = Options.Appearance(\"workBackgroundImage\", _state, _pageNode ?? _work); return image.StartsWith(\"/media/work-starter/\", StringComparison.Ordinal) && image.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '/' or '-' or '.') ? \";background-image:linear-gradient(color-mix(in srgb,var(--hl-paper) 88%,transparent),color-mix(in srgb,var(--hl-paper) 88%,transparent)),url('\" + image + \"');background-size:cover;background-position:center\" : \"\"; } }", StringComparison.Ordinal);
            await File.WriteAllTextAsync(dynamicPagePath, dynamicPage, new UTF8Encoding(false), ct);
            await CopyReferencedMediaAsync(state, template, staging, ct);
            await WriteAsync(workRoot, "src/Pages/wwwroot/work-theme.css", NavigationCss, ct);
            var appPath = Path.Combine(workRoot, "src", "Host", "Components", "App.razor");
            var appSource = await File.ReadAllTextAsync(appPath, ct);
            appSource = appSource.Replace("<HeadOutlet", "<link rel=\"stylesheet\" href=\"_content/HyperLib.Work/work-theme.css\" /><HeadOutlet", StringComparison.Ordinal);
            await File.WriteAllTextAsync(appPath, appSource, new UTF8Encoding(false), ct);
            await WriteAsync(staging, ".studio/hyperlib-work.json", JsonSerializer.Serialize(new { schemaVersion = 1, projectKind = "Hyperlib", workId, projectPath = "Works/" + workId, landingPageId = pageIds[0] }, ContentJson.Options), ct);
            await WriteAsync(workRoot, ".studio/wizard.json", JsonSerializer.Serialize(definition, ContentJson.Options), ct);
            var readme = Readme(definition, workId, pageIds, pages);
            await WriteAsync(workRoot, "README.md", readme, ct);
            await WriteAsync(staging, "README.md", RootReadme(definition, workId), ct);
            await WriteFileGuideAsync(workRoot, template, state, workId, ct);
            ct.ThrowIfCancellationRequested();
            RejectLinks(parent);
            Directory.Move(staging, destination);
            return Within(destination, "Works/" + workId);

            async Task<TemplateDefinition> ImportAsync(string id, TemplateDefinition current, CancellationToken cancellationToken)
            {
                var choice = ParseChoice(id);
                var raw = await File.ReadAllBytesAsync(templatePath, cancellationToken);
                var plan = await TemplateObjectImporter.PrepareAsync(staging, workRoot, current, [choice], ct: cancellationToken);
                await TemplateObjectImporter.CommitAsync(workRoot, plan.Definition, [plan], Convert.ToHexStringLower(SHA256.HashData(raw)), cancellationToken);
                return plan.Definition;
            }
        }
        catch
        {
            // Only this invocation's unpublished staging folder is removed; an existing destination is never touched.
            if (Directory.Exists(staging)) { _ = Within(parent, Path.GetFileName(staging)); Directory.Delete(staging, true); }
            throw;
        }
    }

    private static IEnumerable<HyperlibWorkPageDefinition> AllPages(HyperlibWorkProjectDefinition definition)
        => new[] { definition.LandingPage }.Concat(definition.PageDefinitions);
    private static IEnumerable<string> SelectedIds(HyperlibWorkPageDefinition page)
        => new[] { page.LayoutChoiceId, page.HeaderChoiceId, page.BodyChoiceId, page.ImageChoiceId }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Concat(page.SpecialChoiceIds);
    private static TemplateObjectSelection ParseChoice(string id)
    {
        var parts = id.Split(':', 2);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Select a template object from the current catalogue.");
        TemplateValidator.ValidateId(parts[0], "design group"); TemplateValidator.ValidateId(parts[1], "template object");
        return new(parts[0], parts[1]);
    }
    private static string ComponentName(string value) => Regex.Replace(value.Split('.').Last(), "^(?:Hl[0-9a-f]{12}_)+", "", RegexOptions.CultureInvariant);
    private static PageBlock NewBlock(string type, string region) => new() { Id = "block-" + Guid.NewGuid().ToString("N")[..16], Type = type, Region = region };
    private static string RegionFor(TemplateDefinition template, string type) => template.Regions.First(region => region.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains(type)).Id;
    private static string SectionRegionFor(TemplateDefinition template, string type, string? preferred)
        => template.Regions.FirstOrDefault(region => region.Id == preferred && (region.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains(type)))?.Id ?? RegionFor(template, type);
    private static string NavigationRegionFor(TemplateDefinition template, IEnumerable<PageBlock> blocks)
    {
        var used = blocks.Select(block => block.Region).ToHashSet(StringComparer.Ordinal);
        return template.Regions.LastOrDefault(region => used.Contains(region.Id) && (region.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains("work-navigation")))?.Id
            ?? RegionFor(template, "work-navigation");
    }
    private static Dictionary<string, JsonElement> DefaultAppearance(TemplateDefinition template)
        => template.Appearance.Where(field => field.DefaultValue.HasValue).ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);
    private static void ApplyContrast(Dictionary<string, JsonElement> appearance, string background)
    {
        static double Linear(int value) { var channel = value / 255.0; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
        var luminance = .2126 * Linear(Convert.ToInt32(background.Substring(1, 2), 16)) + .7152 * Linear(Convert.ToInt32(background.Substring(3, 2), 16)) + .0722 * Linear(Convert.ToInt32(background.Substring(5, 2), 16));
        var dark = luminance < .179;
        appearance["ink"] = ContentJson.Value(dark ? "#f2f5f4" : "#202b26");
        appearance["muted"] = ContentJson.Value(dark ? "#b5c3bc" : "#56645d");
        appearance["surface"] = ContentJson.Value(dark ? "#22312a" : "#ffffff");
    }
    private static void Validate(HyperlibWorkProjectDefinition definition)
    {
        ValidateTitle(definition.WorkTitle);
        if (definition.Description.Length > 2000 || definition.PageDefinitions.Count is < 1 or > 50 || definition.LandingPage is null)
            throw new InvalidDataException("Create a Work with 1–50 Pages and a description of at most 2,000 characters.");
        TemplateValidator.ValidateId(definition.TemplateId, "starter");
        foreach (var page in AllPages(definition))
        {
            ValidateTitle(page.Title);
            if (page.Description.Length > 20000 || page.Images.Count > 30 || page.SpecialChoiceIds.Count > 30)
                throw new InvalidDataException("A Page supports a 20,000 character introduction, 30 images, and 30 special elements.");
            foreach (var color in new[] { page.Background, page.Accent })
                if (!string.IsNullOrWhiteSpace(color) && !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Use a six-digit hex color, such as #65a88e.");
            foreach (var id in SelectedIds(page)) _ = ParseChoice(id);
        }
    }
    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || title.Any(char.IsControl)) throw new InvalidDataException("Titles need 1–200 printable characters.");
    }
    private static void ValidateFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ')
            || Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Choose a short folder name without reserved Windows characters.");
    }
    private static string Within(string root, string relative)
    {
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var result = Path.GetFullPath(Path.Combine(boundary, relative));
        if (!SamePath(boundary, result) && !result.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The project path escapes its folder.");
        RejectLinks(result);
        return result;
    }
    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken ct)
    {
        RejectLinks(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The source template package is incomplete: " + source);
        Directory.CreateDirectory(destination);
        long copied = 0;
        await CopyAsync(source);
        async Task CopyAsync(string directory)
        {
            RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                RejectLinks(entry);
                if (Directory.Exists(entry)) { if (!IgnoredDirectories.Contains(Path.GetFileName(entry))) await CopyAsync(entry); continue; }
                var info = new FileInfo(entry);
                copied += info.Length;
                if (info.Length > 64L * 1024 * 1024 || copied > 256L * 1024 * 1024) throw new IOException("Split this large template package into smaller Design Groups before creating the Work.");
                var target = Within(destination, Path.GetRelativePath(source, entry));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, ct);
            }
        }
    }
    private static async Task WriteAsync(string root, string relative, string contents, CancellationToken ct)
    {
        var path = Within(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false), ct);
    }
    private static async Task<string> ImageUrlAsync(string input, string workRoot, CancellationToken ct)
    {
        if (input.StartsWith("/media/work-starter/", StringComparison.Ordinal) && !input.Contains("..", StringComparison.Ordinal)) return input;
        var source = Path.GetFullPath(input);
        RejectLinks(source);
        var info = new FileInfo(source);
        if (!info.Exists || !ImageExtensions.Contains(info.Extension) || info.Length > 32L * 1024 * 1024)
            throw new InvalidDataException("Choose a PNG, JPEG, WebP, GIF, or AVIF image under 32 MiB.");
        var bytes = await File.ReadAllBytesAsync(source, ct);
        var filename = Convert.ToHexStringLower(SHA256.HashData(bytes))[..20] + info.Extension.ToLowerInvariant();
        var target = Within(workRoot, "src/Pages/wwwroot/starter-images/" + filename);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target)) await File.WriteAllBytesAsync(target, bytes, ct);
        var media = Within(Path.GetFullPath(Path.Combine(workRoot, "../..")), "Media/work-starter/" + filename);
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        if (!File.Exists(media)) await File.WriteAllBytesAsync(media, bytes, ct);
        return "/media/work-starter/" + filename;
    }
    private async Task CopyReferencedMediaAsync(LibraryState state, TemplateDefinition template, string targetRoot, CancellationToken ct)
    {
        var strings = state.Pages.Values.SelectMany(page => page.Blocks).SelectMany(block => block.Values.Values)
            .Concat(template.Blocks.SelectMany(block => block.Fields).Where(field => field.DefaultValue.HasValue).Select(field => field.DefaultValue!.Value));
        foreach (var relativeUrl in strings.Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!)
            .Where(value => value.StartsWith("/media/", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal))
        {
            var relative = "Media/" + Uri.UnescapeDataString(relativeUrl[7..]);
            var target = Within(targetRoot, relative);
            if (File.Exists(target)) continue;
            var source = Within(_sourceRoot, relative);
            if (!File.Exists(source)) throw new FileNotFoundException("The selected template needs an image or media file that is missing from the collection.", source);
            if (new FileInfo(source).Length > 64L * 1024 * 1024) throw new IOException("A template media dependency exceeds 64 MiB.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, ct);
        }
    }
    private static string Illustration(string title, string accent) => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1200 640\" role=\"img\" aria-labelledby=\"title\"><title id=\"title\">" + WebUtility.HtmlEncode(title)
        + " — starter illustration</title><rect width=\"1200\" height=\"640\" fill=\"#101c24\"/><circle cx=\"930\" cy=\"150\" r=\"240\" fill=\"" + accent
        + "\" opacity=\".26\"/><path d=\"M-60 630L450 170 800 530 1260 70V700H-60Z\" fill=\"" + accent + "\" opacity=\".65\"/><circle cx=\"440\" cy=\"315\" r=\"130\" fill=\"none\" stroke=\"#d1f0df\" stroke-width=\"2\"/><path d=\"M310 315H570M440 185V445\" stroke=\"#d1f0df\" stroke-width=\"2\"/></svg>";
    private static string Readme(HyperlibWorkProjectDefinition definition, string workId, string[] pageIds, HyperlibWorkPageDefinition[] pages) => $$"""
        # {{definition.WorkTitle}}

        {{definition.Description}}

        This is an independent Hyperlib **Work containing Pages**, created from your wizard selections. It includes a working landing Page, {{definition.PageDefinitions.Count}} content Pages, reusable components, local media, a shared visual theme, and a private Hyperlib gateway/runtime. All source and data belong to this project. The original template collection remains independent.

        ## Begin here

        1. Open `visual-page.project.json` in Hyperpage Studio. The upper tab represents this whole project; the lower tabs represent its files and preview.
        2. Choose **Work** to see the landing Page; choose any **Page** to edit its objects. The landing is a normal editable Page named `Work landing · {{definition.WorkTitle}}`.
        3. Replace the introductory text, then select a block to edit its fields. Insert additional objects from the template collection and drag them into the desired order. `span` controls full, half, third, or two-thirds column width.
        4. Add photographs from your device. The wizard copied images into `../../Media/work-starter` and source snapshots into `src/Pages/wwwroot/starter-images`; their originals are untouched. Add descriptive alt text and captions.
        5. Use the file **?** control for the purpose and editing guidance for each source file. Start with the Work/Page content controls, `authoring.template.json`, and `src/Pages/wwwroot/work-theme.css`.

        ## Run a local preview

        Install the .NET 10 SDK. Hyperpage Studio starts this project's private preview automatically. To run it independently, open a terminal in the **outer project folder** (two levels above this README), then run:

        ```powershell
        dotnet run --project src/HyperLib.Host/HyperLib.Host.csproj -- --root . --port 54180
        ```

        The host binds only to `http://127.0.0.1:54180`. Open the Work in the local reader, or open it in Studio and use Preview. The first preview compiles the renderer; later source changes build a new generation and retain the last working preview if a build fails. Restore may download pinned NuGet dependencies on a fresh device.

        To check the renderer source without starting a server, run from this Work folder:

        ```powershell
        dotnet build src/Host/Host.csproj
        ```

        ## File and folder map

        | Path | Purpose | Begin by |
        | --- | --- | --- |
        | `visual-page.project.json` | Studio project identity and generated presentation shell. | Load it in Studio; avoid editing stable IDs manually. |
        | `authoring.template.json` | The Work's regions, allowed object types, editable fields, appearance, and defaults for future Pages. | Add/adjust fields and objects in the template editor, then build preview. |
        | `src/Pages/Blocks/*.razor` | Compiled reusable objects. Each accepts `BlockContext`. | Modify one small component and preview. |
        | `src/Pages/Imported/` | Independent snapshots of selected template modules and their dependencies. | Edit your copies freely; they do not change the original collection. |
        | `src/Pages/Blocks/WorkNavigationBlock.razor` | Accessible page list and previous/next navigation. | New Pages are included automatically; disable autoPages and edit items for a custom menu. |
        | `src/Pages/wwwroot/starter-images/` | Source copies of photos owned by this Work. | The gateway serves their canonical media copies through `/media/work-starter/<file>` URLs. |
        | `../../Media/work-starter/` | Photos and starter illustrations served by the private gateway. | Add or replace media through Studio; these images travel with the project. |
        | `src/Pages/wwwroot/work-theme.css` | Active shared visual customization, linked into the host. | Add purposeful spacing, typography, and responsive rules. |
        | `Shared/theme.css` | Extra authoring theme source. | Copy the rules you want active into work-theme.css. |
        | `Content/work.json` and `Content/Pages/` | Readable Work snapshots produced by the store. | Inspect these; use Studio for content edits. They are projections, not the authoritative journal. |
        | `../../Content/library.json` | Canonical readable index of this private Work and Pages. | Inspect hierarchy and appearance. Save content through Studio for conflict checks and undo. |
        | `../../.hyperlib/journal/` | Durable content history and recovery. | Keep it with the project; do not hand-edit committed entries. |
        | `../../src/HyperLib.Core`, `Runtime`, `Host` | Portable local content store, renderer, and gateway SDK source. | Keep these folders for standalone builds; edit them only for intentional runtime changes. |
        | `.studio/wizard.json` | Your original wizard choices for reference. | Compare with the current edited project; changing this does not rebuild your Work. |

        ## Pages and navigation

        {{string.Join('\n', pages.Select((page, index) => "- **" + (index == 0 ? "Work landing" : page.Title) + "** — stable Page ID `" + pageIds[index] + "`."))}}

        The Work ID is `{{workId}}`. Navigation uses stable Page IDs, so renaming a Page does not break its links. The navigation block lists this landing and the content Pages, highlights the current Page, and provides previous/next links. It automatically includes Pages added later; disable `autoPages` and customize `items` when you want a curated menu. External embeds and maps remain opt-in objects; forms are local demonstrations until you implement a backend.

        ## Make it yours

        - Set Work-wide appearance for a consistent foundation, then override background/accent per Page when needed.
        - Build a component in `src/Pages/Blocks`, expose its fields in `authoring.template.json`, and insert that object into a Page.
        - Keep authored code outside `Generated/`; Studio may regenerate its presentation shell.
        - Preserve the whole **outer project folder** when moving, backing up, or sharing. Its private content, journal, media, templates, and SDK travel together.
        - Before publishing, replace all starter content, review accessibility and mobile layouts, and verify external citations and links. Publishing to an existing Hyperlib library is a separate explicit action.
        """;
    private static string RootReadme(HyperlibWorkProjectDefinition definition, string workId) => $$"""
        # {{definition.WorkTitle}} — complete Hyperlib project

        {{definition.Description}}

        This folder contains one independent Work, its editable landing Page and {{definition.PageDefinitions.Count}} content Pages, selected template components, local photographs, durable content history, and the source of its private local gateway/renderer. Keep the whole folder together when moving, sharing, or backing up the project.

        ## Open and begin

        Open this folder in Hyperpage Studio, or select `Works/{{workId}}/visual-page.project.json`. Both open the same project tab. The detailed authoring guide is `Works/{{workId}}/README.md`.

        Begin in the **Work** and **Page** editors: replace the landing introduction, customize each Page's configured objects, and insert more objects from the template collection. Your wizard choices are saved in `Works/{{workId}}/.studio/wizard.json` for reference. They do not silently regenerate edited source.

        ## Preview independently

        With the .NET 10 SDK installed, run these commands from this folder:

        ```powershell
        dotnet build Works/{{workId}}/src/Host/Host.csproj
        dotnet run --project src/HyperLib.Host/HyperLib.Host.csproj -- --root . --port 54180
        ```

        Open `http://127.0.0.1:54180` and choose your Work. Hyperpage Studio can start the same private gateway automatically for an embedded preview. The first renderer preview compiles the Work; later edits build new immutable generations while retaining the last healthy preview. A fresh machine may restore pinned NuGet dependencies.

        ## What lives here

        | Folder | Purpose and safe starting point |
        | --- | --- |
        | `Works/{{workId}}` | Your editable Work template and components. Start with its README, authoring.template.json, Blocks, and active work-theme.css. |
        | `Content/library.json` | Readable canonical index of the hierarchy, Page documents, and appearance. Save through Studio so revisions and history remain consistent. |
        | `Library/` | Readable hierarchy projections for inspection and export; maintained by the content store. |
        | `Media/work-starter/` | Owned photographs, background images, and starter illustrations served through rooted `/media/work-starter/` URLs. |
        | `.hyperlib/journal/` | Durable edit history and recovery. Keep this with the project and avoid hand-editing committed records. |
        | `CoreLibrary/DesignGroups/` | Independent snapshots of only the selected object groups. Their originals remain in the source template collection. |
        | `Starters/standard/` | Local foundation used when the private gateway needs another presentation shell or Page source. |
        | `src/HyperLib.Core/` | Content model, validation, transactional store, and template import APIs. Most projects can leave this SDK source as supplied. |
        | `src/HyperLib.Runtime/` | Page rendering, field defaults, local runtime supervision, responsive CSS, and the editable Work landing bridge. |
        | `src/HyperLib.Host/` | Loopback gateway, revision-safe editing endpoints, owned media serving, and renderer proxy. Change this only for intentional host behavior. |
        | `.studio/hyperlib-work.json` | Maps this outer folder to its Work project and landing Page. Preserve its stable identities. |

        ## Extend safely

        Add Pages and template objects in Hyperpage Studio, then preview after each small change. Navigation objects use stable IDs and automatically include new Work Pages; disable `autoPages` and edit `items` if you want a curated menu. Background overrides set readable light/dark text defaults; imported background photographs use a quiet theme overlay.

        Add a new reusable component in `Works/{{workId}}/src/Pages/Blocks/`, expose its fields in the Work's `authoring.template.json`, and insert it into a Page. Preserve the `BlockContext` parameter and keep hand-written code outside `Generated/`.

        Forms are local demonstrations until you build an actual submission backend. Review mobile layouts, alt text, citations, and external links before publishing. Importing/publishing this Work into another existing library is a separate explicit action; this project's content and original template collection remain independent.
        """;

    private static async Task WriteFileGuideAsync(string workRoot, TemplateDefinition template, LibraryState state, string workId, CancellationToken ct)
    {
        var files = EnumerateSourceFiles(workRoot).Select(path =>
        {
            var relative = Path.GetRelativePath(workRoot, path).Replace('\\', '/');
            var generated = relative.Contains("/Generated/", StringComparison.OrdinalIgnoreCase);
            var content = relative.StartsWith("Content/", StringComparison.Ordinal);
            var component = template.Blocks.FirstOrDefault(block => block.Component.Split('.').Last() == Path.GetFileNameWithoutExtension(path));
            var page = state.Pages.Values.FirstOrDefault(page => page.WorkId == workId && Path.GetFileNameWithoutExtension(path) == page.Id);
            var purpose = relative == "authoring.template.json" ? "Declares editable object types, regions, fields, shared appearance, and defaults for future Pages."
                : relative == "visual-page.project.json" ? "Loads this entire Work as a project in Hyperpage Studio and identifies its presentation shell."
                : relative == "README.md" ? "Explains how to edit, build, preview, move, and extend this generated Work."
                : page is not null ? "Readable content snapshot for ‘" + page.Title + "’, containing " + page.Blocks.Count + " configured objects. Its stable Page ID is " + page.Id + "."
                : content ? "Readable projection of this Work's durable content. Studio uses the private library journal as its authority."
                : relative.Contains("WorkNavigationBlock", StringComparison.Ordinal) ? "Renders the Work's accessible Page menu and previous/next links from stable Page IDs."
                : component is not null ? "The ‘" + component.Title + "’ template object. " + (component.Description ?? "Renders an editable " + ComponentName(component.Component).Replace("Block", "", StringComparison.Ordinal).ToLowerInvariant() + " object on the Work landing or a Page.")
                : relative.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ? "Reusable compiled Razor object or presentation component used by this Work."
                : relative.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? "Visual styling for this Work or a reusable component."
                : relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? "Defines a local .NET project and its portable SDK references."
                : relative.Contains("starter-images/", StringComparison.Ordinal) ? "Local media copied or generated for this Work; used through a static asset URL."
                : relative == ".studio/wizard.json" ? "Records the initial wizard selections; this is a reference, not a rebuild command."
                : "Source, asset, or configuration file supporting the Work's local renderer.";
            var usages = component is null ? "" : string.Join(", ", state.Pages.Values.Where(page => page.Blocks.Any(block => block.Type == component.Id)).Select(page => page.Title));
            var firstFields = component is null ? "" : string.Join(", ", component.Fields.Where(field => field.Id is not ("span" or "font" or "align" or "color")).Take(5).Select(field => field.Title + " (" + field.Id + ")"));
            return new { relativePath = relative, title = component is null ? Path.GetFileName(path) : component.Title + " · " + Path.GetFileName(path), purpose,
                editGuidance = content ? "Edit content in the Work/Page controls so revision checks, undo, and snapshots stay consistent."
                    : generated ? "Edit user-owned component files or the Studio canvas; this generated shell may be recreated."
                    : component is not null ? "Choose this object in the Work/Page editor and begin with " + firstFields + ". Edit this Razor file to change how those fields render; preserve the BlockContext parameter."
                    : "Edit this project's copy, save, and use Preview to check the result. The source template collection is independent.",
                gettingStarted = component is not null && usages.Length > 0 ? "Already used by: " + usages + ". Open one of these Pages and select its ‘" + component.Title + "’ object to see your change."
                    : page is not null ? "Open ‘" + page.Title + "’ in the Work/Page editor, select a block, and change its text or media fields."
                    : relative == "authoring.template.json" ? "Start with a block's fields/defaultValue and the Work's appearance. Adding an object needs a matching compiled Razor component."
                    : relative.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? "Open the Work/Page preview and adjust one spacing, color, or responsive rule. work-theme.css is the active shared stylesheet."
                    : "Read README.md, open the Work landing or a Page, and change one small piece before adding more objects.",
                ownership = content ? "content projection" : generated ? "generated" : "user-owned" };
        }).ToArray();
        await WriteAsync(workRoot, ".studio/project-guide.json", JsonSerializer.Serialize(new { schemaVersion = 1, projectKind = "Hyperlib", files }, ContentJson.Options), ct);
    }
    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            RejectLinks(entry);
            if (Directory.Exists(entry)) { if (!IgnoredDirectories.Contains(Path.GetFileName(entry))) foreach (var file in EnumerateSourceFiles(entry)) yield return file; }
            else yield return entry;
        }
    }
    private const string NavigationComponent = """
        @namespace HyperLib.Work.Blocks
        @using HyperLib.Core
        @using HyperLib.Runtime
        @inject RendererContentClient Content
        @inject RendererOptions Options
        @inject NavigationManager Navigation
        <nav class="hl-work-navigation" aria-label="Pages in this Work">
            <ul>@foreach (var item in Items) { <li><a href="@("page/" + Uri.EscapeDataString(item.Id))" aria-current="@(item.Id == Current ? "page" : null)">@item.Title</a></li> }</ul>
            <div class="hl-work-page-step">@if (CurrentIndex > 0) { <a rel="prev" href="@("page/" + Uri.EscapeDataString(Items[CurrentIndex - 1].Id))">← @Items[CurrentIndex - 1].Title</a> } @if (CurrentIndex >= 0 && CurrentIndex < Items.Count - 1) { <a rel="next" href="@("page/" + Uri.EscapeDataString(Items[CurrentIndex + 1].Id))">@Items[CurrentIndex + 1].Title →</a> }</div>
        </nav>
        @code {
            [Parameter, EditorRequired] public BlockContext Context { get; set; } = default!;
            private string Current { get; set; } = "";
            private List<(string Title,string Id)> Items { get; set; } = [];
            private int CurrentIndex => Items.FindIndex(item=>item.Id==Current);
            protected override async Task OnParametersSetAsync()
            {
                var state=await Content.GetStateAsync();
                var work=state.Nodes.FirstOrDefault(node=>node.Id==Options.PackageId && node.Type=="work");
                var landing=work?.Appearance.TryGetValue("workLandingPage",out var value)==true?value.GetString():null;
                Current=Context.Text("current");
                var path=new Uri(Navigation.Uri).AbsolutePath;
                if(path.Contains("/page/",StringComparison.Ordinal))Current=Uri.UnescapeDataString(path[(path.LastIndexOf("/page/",StringComparison.Ordinal)+6)..]);
                if(Current.Length==0)Current=landing??"";
                Items=string.Equals(Context.Text("autoPages","true"),"false",StringComparison.OrdinalIgnoreCase)
                    ?Context.Text("items").Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Split('|',2)).Where(parts=>parts.Length==2).Select(parts=>(parts[0],parts[1])).ToList()
                    :state.Nodes.Where(node=>node.WorkId==Options.PackageId && node.Type=="page").OrderBy(node=>node.Order).Select(node=>(node.Id==landing?work?.Title??node.Title:node.Title,node.Id)).ToList();
            }
        }
        """;
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Project and template paths cannot contain symbolic links or junctions."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static string FindSourceLibrary()
    {
        var configured = Environment.GetEnvironmentVariable("HYPERLIB_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "Hyperlib");
                if (File.Exists(Path.Combine(candidate, "hyperlib.app.json"))) return candidate;
                if (File.Exists(Path.Combine(directory.FullName, "hyperlib.app.json"))) return directory.FullName;
            }
        throw new DirectoryNotFoundException("The template collection is unavailable. Set HYPERLIB_ROOT to the local Hyperlib folder.");
    }
    private const string NavigationCss = """
        .hl-work-navigation { margin-top: 24px; padding-top: 28px; border-top: 1px solid color-mix(in srgb,var(--hl-ink) 20%,transparent); }
        .hl-work-navigation ul { display:flex; flex-wrap:wrap; gap:10px; padding:0; margin:0; list-style:none; }
        .hl-work-navigation a { display:inline-block; padding:10px 16px; text-decoration:none; color:var(--hl-ink); border-radius:8px; background:var(--hl-panel); font:500 14px/1.5 system-ui,sans-serif; }
        .hl-work-navigation a:hover,.hl-work-navigation a:focus-visible { outline:2px solid var(--hl-accent); outline-offset:2px; }
        .hl-work-navigation a[aria-current=page] { background:var(--hl-accent); color:var(--hl-accent-ink); }
        .hl-work-page-step { display:flex; justify-content:space-between; flex-wrap:wrap; gap:12px; margin-top:18px; }
        @media(max-width:640px) { .hl-work-navigation ul { flex-direction:column; } .hl-work-navigation a { width:100%; box-sizing:border-box; } }
        """;
    private const string BrowseComponent = """
        @page "/browse/{NodeId}"
        @page "/browse"
        @rendermode InteractiveServer
        @inject RendererOptions Options
        @inject RendererContentClient Content
        @inject IJSRuntime JS
        @if (_landing is { Length: > 0 }) { <DynamicPage PageId="@_landing" /> }
        else { <section class="hl-browse" style="@Options.AppearanceStyle(_state,_node)">@if(Options.ShellComponent is { } shell){<DynamicComponent Type="shell" />}<p class="hl-kicker">@(_node?.Type ?? "Work")</p><h1>@(_node?.Title ?? "Your Work")</h1><p class="hl-intro">@(Options.Appearance("subtitle",_state,_node))</p></section> }
        @code {
            [Parameter] public string? NodeId { get; set; }
            private LibraryState? _state; private LibraryNode? _node; private string? _landing;
            protected override async Task OnParametersSetAsync(){_state=await Content.GetStateAsync();_node=_state.Nodes.FirstOrDefault(node=>node.Id==NodeId);_landing=_node?.Appearance.TryGetValue("workLandingPage",out var value)==true?value.GetString():null;}
            protected override async Task OnAfterRenderAsync(bool firstRender){await JS.InvokeVoidAsync("hyperlibRenderer.ready",Options.GenerationId);}
        }
        """;
    private const string DynamicPageComponent = """
        @page "/page/{PageId}"
        @rendermode InteractiveServer
        @implements IAsyncDisposable
        @inject RendererOptions Options
        @inject RendererContentClient Content
        @inject IJSRuntime JS
        <main class="hl-document @Options.VariantClass(_state, _pageNode ?? _work)" style="@AppearanceStyle">
            @if (_error is not null) { <p role="alert" class="hl-error">@_error</p> }
            else if (_page is not null)
            {
                @if (Options.ShellComponent is { } shell) { <DynamicComponent Type="shell" /> }
                @foreach (var region in Options.Template.Regions.Where(region => _page.Blocks.Any(block => block.Region == region.Id)))
                {
                    <section class="hl-region" data-region="@region.Id" aria-label="@region.Title">
                        @foreach (var block in _page.Blocks.Where(b => b.Region == region.Id))
                        {
                            var effectiveBlock = Options.WithFieldDefaults(block);
                            <section @key="block.Id" class="hl-block @Options.BlockTypeClass(block) @RendererOptions.BlockSpanClass(effectiveBlock)" style="@RendererOptions.BlockStyle(effectiveBlock)" data-block-id="@block.Id" data-block-type="@block.Type" aria-label="@BlockTitle(block)">
                                <ErrorBoundary @key="(block.Id,_page.Revision)"><ChildContent><DynamicComponent Type="Options.Resolve(block.Type)" Parameters="Parameters(effectiveBlock)" /></ChildContent><ErrorContent Context="error"><p role="alert">This component could not render: @error.Message</p></ErrorContent></ErrorBoundary>
                            </section>
                        }
                    </section>
                }
                @if (_page.Sources.Count > 0) { <aside class="hl-sources"><h2>Sources</h2><ol>@foreach(var source in _page.Sources){<li><a href="@source.Url" target="_blank" rel="noopener noreferrer">@source.Title</a></li>}</ol></aside> }
            }
            else { <p class="hl-loading">Opening Page…</p> }
        </main>
        @code {
            [Parameter] public string PageId { get; set; } = "";
            [SupplyParameterFromQuery(Name="edit")] public string? Edit { get; set; }
            private PageDocument? _page; private LibraryState? _state; private LibraryNode? _work; private LibraryNode? _pageNode; private string? _error;
            private DotNetObjectReference<DynamicPage>? _reference; private bool _refreshBridge=true;
            private bool Editing => !Options.ReadOnly && Edit=="1";
            private string AppearanceStyle => Options.AppearanceStyle(_state, _pageNode ?? _work);
            protected override async Task OnParametersSetAsync()=>await LoadAsync();
            private async Task LoadAsync(){try{_page=await Content.GetPageAsync(PageId);TemplateValidator.ValidatePage(Options.Template,_page);_state=await Content.GetStateAsync();_work=_state.Nodes.FirstOrDefault(node=>node.Id==_page.WorkId);_pageNode=_state.Nodes.FirstOrDefault(node=>node.Id==PageId);_error=null;}catch(Exception error){_error=error.Message;}_refreshBridge=true;}
            protected override async Task OnAfterRenderAsync(bool firstRender){if(!_refreshBridge)return;_refreshBridge=false;_reference??=DotNetObjectReference.Create(this);await JS.InvokeVoidAsync("hyperlibRenderer.connect",_reference,Editing,PageId,Options.GenerationId);}
            [JSInvokable] public async Task Refresh(){await LoadAsync();await InvokeAsync(StateHasChanged);}
            private string BlockTitle(PageBlock block)=>Options.Template.Blocks.FirstOrDefault(definition=>definition.Id==block.Type)?.Title??block.Type;
            private IDictionary<string,object> Parameters(PageBlock block)=>new Dictionary<string,object>{{"Context",new BlockContext{Id=block.Id,Editing=Editing,Values=block.Values}}};
            public ValueTask DisposeAsync(){_reference?.Dispose();return ValueTask.CompletedTask;}
        }
        """;
}
