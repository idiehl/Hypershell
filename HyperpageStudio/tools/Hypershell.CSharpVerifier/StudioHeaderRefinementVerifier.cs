using System.IO;
using System.Net.Http;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;

public static class StudioHeaderRefinementVerifier
{
    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var legacy = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>("{\"id\":\"legacy-row\",\"height\":160,\"elements\":[{\"id\":\"legacy-text\",\"kind\":\"text\",\"x\":13.125,\"y\":24.75,\"widthPercent\":61.375}]}", ContentJson.Options)!;
        Require(legacy.Arrangement == "positioned" && legacy.Elements[0].WidthMode == "percent" && legacy.Elements[0].PositionAlignment == "manual"
            && HyperlibHeaderLayouts.ElementStyle(legacy.Elements[0]).Contains("left:13.125%;top:24.75%;", StringComparison.Ordinal), "Old recipes must preserve exact positioned geometry without migration.");
        Require(HyperlibHeaderLayouts.All.All(preset => HyperlibHeaderLayouts.Create(preset.Id).Blocks.All(block => block.Arrangement == "positioned")), "Every existing editable preset must retain its established positioning.");
        var vm = new HyperlibWizardHeaderLayoutViewModel(); var block = vm.AddBlock();
        var text = vm.AddElement(block, "text"); var picture = vm.AddElement(block, "image"); var box = vm.AddElement(block, "box");
        var childA = vm.AddElement(block, "text", box); var childB = vm.AddElement(block, "text", box);
        Require(block.ArrangeSideBySide && box.Arrangement == "positioned" && picture.WidthMode == "pixels" && picture.WidthPixels == 128 && picture.Height == 96,
            "New Blocks, Boxes, and media must start with useful ordered flow and a fixed media size.");
        picture.SetPixelSize(143.375, 87.625); picture.PositionAlignment = "right"; picture.Alignment = "center";
        Require(picture.Width == 143.375 && picture.Height == 87.625 && picture.PositionAlignment != picture.Alignment, "Exact fractional pixel dimensions and element placement must remain independent of text alignment.");
        var flowGeometry = (picture.X, picture.Y, picture.WidthPixels, picture.Height); var structureChanges = 0; vm.StructureChanged += () => structureChanges++;
        block.ArrangeSideBySide = false;
        Require(block.IsPositioned && flowGeometry == (picture.X, picture.Y, picture.WidthPixels, picture.Height), "Unchecking Block Placement must enable free positioning without overwriting saved coordinates or sizes.");
        block.ArrangeSideBySide = true;
        Require(structureChanges == 0 && flowGeometry == (picture.X, picture.Y, picture.WidthPixels, picture.Height), "Changing the persisted arrangement must preserve geometry without rebuilding focused element controls.");
        vm.MoveElement(picture, -1);
        Require(block.Elements[0] == picture && flowGeometry == (picture.X, picture.Y, picture.WidthPixels, picture.Height), "Ordered flow must follow the actual list order without changing element size.");
        block.Arrangement = "positioned"; picture.X = 7.5; picture.Y = 10.25; text.X = 61.5; text.Y = 33.75;
        vm.MoveElement(picture, 1);
        Require(picture.X == 61.5 && picture.Y == 33.75 && text.X == 7.5 && text.Y == 10.25 && picture.WidthPixels == 143.375,
            "Reordering a positioned layout must visibly exchange adjacent positions while preserving its element sizes.");
        box.ArrangeSideBySide = true; vm.MoveElement(childB, -1);
        Require(box.Children[0] == childB && box.ArrangeSideBySide, "Boxes must support the same saved ordered arrangement and independent child reordering.");
        block.ArrangeSideBySide = true;
        var snapshot = vm.BuildDefinition(); var roundTrip = ContentJson.Clone(snapshot);
        Require(roundTrip.Blocks[0].Arrangement == "horizontal" && roundTrip.Blocks[0].Elements.Single(element => element.Id == box.Id).Arrangement == "horizontal"
            && roundTrip.Blocks[0].Elements.Single(element => element.Id == picture.Id).WidthPixels == 143.375, "Row and Box arrangements and precise sizes must survive serialized project recipes.");
        var css = HyperlibHeaderLayouts.ContainerStyle("horizontal") + HyperlibHeaderLayouts.ElementStyle(picture.BuildDefinition(), "horizontal");
        Require(css.Contains("flex-wrap:wrap", StringComparison.Ordinal) && css.Contains("flex:0 0 143.375px", StringComparison.Ordinal)
            && HyperlibHeaderLayouts.ElementStyle(text.BuildDefinition(), "horizontal").Contains("1 0px", StringComparison.Ordinal), "Fixed media must retain pixel size while fluid elements share available space and fixed items wrap.");
        Require(HyperlibHeaderLayouts.RowStyle(new() { Arrangement = "vertical", Height = 160 }).Contains("height:auto;min-height:160px", StringComparison.Ordinal)
            && HyperlibHeaderLayouts.ContainerStyle("horizontal").Contains("height:auto", StringComparison.Ordinal)
            && HyperlibHeaderLayouts.ElementStyle(box.BuildDefinition(), "horizontal").Contains("overflow:auto", StringComparison.Ordinal)
            && !HyperlibHeaderLayouts.RowStyle(legacy).Contains("height:auto", StringComparison.Ordinal),
            "Stacked or wrapped rows must grow past their minimum height, while fixed-height flowing Boxes contain overflow and legacy rows retain exact heights.");
        Require(HyperlibHeaderLayouts.ElementStyle(new() { Kind = "template", Height = 96, WidthMode = "pixels", WidthPixels = 128 }, "vertical").Contains("overflow:auto", StringComparison.Ordinal)
            && HyperlibHeaderLayouts.ElementStyle(new() { Kind = "template", Height = 96 }, "positioned").Contains("overflow:auto", StringComparison.Ordinal)
            && !HyperlibHeaderLayouts.ElementStyle(new() { Kind = "template", Height = 0 }, "vertical").Contains("overflow:auto", StringComparison.Ordinal),
            "An explicitly sized Template must contain its component captions and controls; auto-height Templates must keep natural growth.");
        var generator = new HyperlibWorkProjectService(sourceLibraryRoot);
        var counter = generator.GetCatalog().First(choice => choice.GroupId == "blank" && choice.ComponentName == "CounterBlock" && choice.Kind == "element");
        var counterElement = new HyperlibHeaderElementDefinition { Kind = "template", TemplateObjectId = counter.Id,
            TemplateValues = new() { ["element"] = new() { ["label"] = ContentJson.Value("Live ordered tool"), ["start"] = ContentJson.Value(3) } } };
        var imagePath = Path.Combine(output, "refinement-image.svg");
        await File.WriteAllTextAsync(imagePath, "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96'><rect width='128' height='96' fill='#65a88e'/></svg>", ct);
        var image = new HyperlibHeaderElementDefinition { Kind = "image", WidthMode = "pixels", WidthPixels = 143.375, Height = 87.625, MediaPath = imagePath, PositionAlignment = "right" };
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = false, HeaderLayout = new() { Enabled = true, Blocks = [new() { Arrangement = "horizontal", Height = 220,
            FillColor = "#def4e8", Elements = [new() { Text = "First flow item" }, image, new() { Kind = "box", Arrangement = "vertical", Height = 160, Children = [counterElement, new() { Text = "A lower child" }] }] }] } };
        var project = await generator.CreateAsync(Path.Combine(output, "header-refinement-" + Guid.NewGuid().ToString("N")), "site", new() { PipelineVersion = 2,
            WorkTitle = "Header flow and sizing", SelectedToolsetIds = [], LandingTemplate = ContentJson.Clone(recipe), PageTemplate = recipe,
            PageTemplates = [new() { Id = "page-default", Name = "Ordered layout", Definition = recipe }] }, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../..")); var service = new HyperlibProjectPipelineService(); var pipeline = (await service.ReadAsync(project, ct))!;
        var stored = pipeline.PageTemplate.Definition.HeaderLayout.Blocks[0]; var hydratedTool = stored.Elements[2].Children[0]; var stableBlockId = hydratedTool.TemplateBlocks[0].Id;
        Require(stored.Arrangement == "horizontal" && stored.Elements[2].Arrangement == "vertical" && stored.Elements[1].WidthPixels == 143.375
            && stored.Elements[1].MediaPath.StartsWith("/media/studio-header/", StringComparison.Ordinal), "Actual project creation must persist flow, exact sizes, nested tools, and independently owned media.");
        var store = new ContentStore(library); var state = await store.ReadAsync(ct); var authored = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        var header = authored.Blocks.Single(item => item.Type == HyperlibHeaderLayouts.BlockType);
        var authoredRow = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(header.Values["layout"].GetString()!, ContentJson.Options)!;
        authoredRow.Elements[1].Height = 83.25;
        authoredRow.Elements[2].Children[0].TemplateBlocks[0].Values["label"] = ContentJson.Value("Authored tool label");
        header.Values["layout"] = ContentJson.Value(JsonSerializer.Serialize(authoredRow, ContentJson.Options));
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Author independent flow size and nested tool text", Operations = [new() { Kind = "savePage", Page = authored }] }, ct);
        var next = (await service.ReadDefinitionAsync(project, ct))!; next.LandingTemplate.HeaderLayout.Blocks[0].Arrangement = "vertical";
        next.LandingTemplate.HeaderLayout.Blocks[0].Elements[1].WidthPixels = 176.5; next.LandingTemplate.HeaderLayout.Blocks[0].Elements[1].Height = 120;
        next.LandingTemplate.HeaderLayout.Blocks[0].Elements.Reverse();
        pipeline = await generator.UpdateFoundationAsync(project, next, pipeline.Revision, state.Revision, ct); state = await store.ReadAsync(ct);
        var changed = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(state.Pages[authored.Id].Blocks.Single(item => item.Id == header.Id).Values["layout"].GetString()!, ContentJson.Options)!;
        var changedImage = changed.Elements.Single(item => item.Id == image.Id); var changedTool = HyperlibHeaderLayouts.Elements(new() { Enabled = true, Blocks = [changed] }).Single(item => item.Id == counterElement.Id);
        Require(changed.Arrangement == "vertical" && changed.Elements[0].Kind == "box" && changedImage.WidthPixels == 176.5 && changedImage.Height == 83.25
            && changedTool.TemplateBlocks[0].Values["label"].GetString() == "Authored tool label" && changedTool.TemplateBlocks[0].Id == stableBlockId,
            "Flow and order changes must propagate while independent dimensions, authored nested values, and component identities survive.");
        var protectedAppearance = pipeline.PageTemplate.Appearance.All(pair => pair.Value.GetRawText().Length <= 4096);
        Require(protectedAppearance && File.ReadAllText(Path.Combine(project, "src/Pages/wwwroot/work-theme.css")).Contains("studio-header-arrangements-v1", StringComparison.Ordinal), "The generated stylesheet and bounded appearance contract must include the new arrangement behavior.");
        var invalid = ContentJson.Clone(recipe.HeaderLayout); invalid.Blocks[0].Arrangement = "unsafe-layout";
        try { HyperlibHeaderLayouts.Validate(invalid, true); throw new Exception("Unknown arrangement accepted."); } catch (InvalidDataException) { }
        var result = "STUDIO_HEADER_REFINEMENT_OK legacy-positioned-compatibility|ten-presets-preserved|new-flow-defaults|fixed-media-defaults|precise-fractional-sizing|independent-placement-text-alignment|unchecked-free-placement|no-focus-rebuild-on-toggle|flow-order|positioned-reorder|nested-box-flow|serialized-layout|responsive-fixed-wrap|flow-height-and-bounded-box-overflow|bounded-fixed-template-overflow|actual-project-media|actual-content-save|propagated-flow-order|authored-dimensions-preserved|nested-tool-state-identity|unchanged-appearance-limit|generated-styles|invalid-mode-rejected";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-header-refinement-result.txt"), result + "\n" + project, ct); return result;
    }

    public static async Task<string> VerifyUpgradeAsync(string output, string oldWorkRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output); var originalLibrary = Path.GetFullPath(Path.Combine(oldWorkRoot, "../.."));
        var protectedInputs = StudioGuidedUpgradeVerifier.HashTree(originalLibrary); var library = Path.Combine(Path.GetFullPath(output), "old-template-header-copy-" + Guid.NewGuid().ToString("N"));
        if (!library.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use isolated QA output for upgrade checks.");
        await StudioGuidedUpgradeVerifier.CopyTreeAsync(originalLibrary, library, ct); var project = Path.Combine(library, Path.GetRelativePath(originalLibrary, oldWorkRoot));
        var service = new HyperlibProjectPipelineService(); var pipeline = (await service.ReadAsync(project, ct))!; var before = await new ContentStore(library).ReadAsync(ct);
        var oldRows = pipeline.PageTemplate.Definition.HeaderLayout.Blocks.Select(block => ContentJson.Clone(block)).ToArray();
        Require(oldRows.All(block => block.Arrangement == "positioned"), "The old Template release must retain its original positioned geometry before an explicit change.");
        var oldComponent = File.ReadAllText(Path.Combine(project, "src/Pages/Blocks/StudioHeaderRowBlock.razor"));
        Require(oldComponent.Contains("DynamicComponent", StringComparison.Ordinal) && !oldComponent.Contains("Arrangement", StringComparison.Ordinal), "Use the actual shipped Template header component before flow support.");
        using var hosts = new HyperlibWorkPreviewHostService(); using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var oldEndpoint = await hosts.EnsureAsync(library, ct); var definition = (await service.ReadDefinitionAsync(project, ct))!;
        definition.PageTemplate.HeaderLayout.Blocks[0].Arrangement = "horizontal";
        pipeline = await new HyperlibWorkProjectService(sourceLibraryRoot).UpdateFoundationAsync(project, definition, pipeline.Revision, before.Revision, ct);
        var after = await new ContentStore(library).ReadAsync(ct);
        Require(before.Pages.All(pair => after.Pages.ContainsKey(pair.Key) && JsonSerializer.Serialize(pair.Value.Sources, ContentJson.Options) == JsonSerializer.Serialize(after.Pages[pair.Key].Sources, ContentJson.Options)), "An actual old-project flow upgrade must preserve Pages and source citations.");
        Require(Directory.GetFiles(Path.Combine(library, ".studio/upgrades"), "StudioHeaderRowBlock.razor", SearchOption.AllDirectories).Any(path => File.ReadAllText(path) == oldComponent)
            && File.ReadAllText(Path.Combine(project, "src/Pages/wwwroot/work-theme.css")).Contains("studio-header-arrangements-v1", StringComparison.Ordinal), "The old renderer must be backed up before installing matching flow source and styles.");
        var newEndpoint = await hosts.EnsureAsync(library, ct); Require(newEndpoint != oldEndpoint, "Only the owned gateway must restart to load the changed flow contract.");
        using (var api = JsonDocument.Parse(await client.GetStringAsync(new Uri(newEndpoint, "api/studio/pipeline"), ct)))
            Require(api.RootElement.GetProperty("pageTemplate").GetProperty("definition").GetProperty("headerLayout").GetProperty("blocks")[0].GetProperty("arrangement").GetString() == "horizontal", "The upgraded gateway must return the persisted row arrangement.");
        var finalInputs = StudioGuidedUpgradeVerifier.HashTree(originalLibrary);
        Require(protectedInputs.Count == finalInputs.Count && protectedInputs.All(pair => finalInputs.GetValueOrDefault(pair.Key) == pair.Value), "The original shipped Template fixture must remain unchanged.");
        var result = "STUDIO_HEADER_REFINEMENT_UPGRADE_OK actual-template-release|legacy-geometry-default|owned-source-backup|pages-citations-preserved|new-source-styles|owned-gateway-restart|new-flow-api|original-fixture-unchanged";
        await File.WriteAllTextAsync(Path.Combine(output, "studio-header-refinement-upgrade-result.txt"), result + "\n" + project, ct); return result;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
