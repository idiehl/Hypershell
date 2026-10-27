using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class HyperlibHeaderPreviewDragVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string libraryRoot, string browserScriptName = "verify-header-preview-drag.cjs")
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var picture = Path.Combine(outputDirectory, "drag-picture.svg");
        await File.WriteAllTextAsync(picture, "<svg xmlns='http://www.w3.org/2000/svg' width='220' height='130'><rect width='220' height='130' fill='#aed4ce'/><circle cx='170' cy='30' r='18' fill='#ffe7ae'/><path d='M0 130 65 28 140 130 185 70 220 130' fill='#3d7777'/></svg>", new UTF8Encoding(false));
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = false, FooterPlacement = "none", BackgroundColor = "#14252f",
            HeaderLayout = new() { Enabled = true, Blocks = [
                new() { Id = "drag-row", Height = 380, Padding = 20, BorderWidth = 2, FillColor = "#18353b", Elements = [
                    new() { Id = "drag-text", Text = "Move this text", X = 8, Y = 10, WidthMode = "pixels", WidthPixels = 150, Height = 48, FillColor = "#355f61", Padding = 8, FontSize = 18 },
                    new() { Id = "drag-box", Kind = "box", X = 45, Y = 8, WidthPercent = 43, Height = 220, Padding = 18, BorderWidth = 2, FillColor = "#29494c", Children = [
                        new() { Id = "nested-text", Text = "Nested child", X = 6, Y = 10, WidthMode = "pixels", WidthPixels = 120, Height = 40, FontSize = 16, FillColor = "#456c6a" }] },
                    new() { Id = "media-box", Kind = "box", X = 3, Y = 64, WidthMode = "pixels", WidthPixels = 300, Height = 100, Padding = 10, BorderWidth = 1, FillColor = "#3c555a", Children = [
                        new() { Id = "padded-image", Kind = "image", X = 0, Y = 0, WidthMode = "pixels", WidthPixels = 100, Height = 100, MediaPath = picture, MediaFit = "contain", Alt = "Padded drag image" }] }] },
                new() { Id = "horizontal-row", Arrangement = "horizontal", Height = 100, Padding = 12, Elements = [
                    new() { Id = "horizontal-text", Text = "Side by side", WidthMode = "pixels", WidthPixels = 170, Height = 45, FillColor = "#355f61", FontSize = 18 }] },
                new() { Id = "vertical-row", Arrangement = "vertical", Height = 100, Padding = 12, Elements = [
                    new() { Id = "vertical-text", Text = "Stacked flow", WidthMode = "pixels", WidthPixels = 170, Height = 45, FillColor = "#355f61", FontSize = 18 }] },
                new() { Id = "alignment-row", Height = 110, Padding = 12, Elements = [
                    new() { Id = "aligned-text", Text = "Right aligned", PositionAlignment = "right", X = 1, Y = 5, WidthMode = "pixels", WidthPixels = 170, Height = 45, FillColor = "#355f61", FontSize = 18 },
                    new() { Id = "auto-text", Text = "Natural height text", X = 3, Y = 65, WidthPercent = 40, Height = 0, FontSize = 16 }] },
                new() { Id = "padding-floor-row", Height = 200, Elements = [new() { Id = "padding-floor-box", Kind = "box", WidthMode = "pixels", WidthPixels = 160,
                    Height = 100, Padding = 18, BorderWidth = 2, X = 10, Y = 10, FillColor = "#355f61" }] }
            ] } };
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Header pointer movement",
            SelectedToolsetIds = [], LandingTemplate = ContentJson.Clone(recipe), PageTemplate = ContentJson.Clone(recipe) };
        using var staticService = new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory, "static-preview-cache"));
        using var interactiveService = new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory, "interactive-preview-cache"));
        Process? staticProcess = null, interactiveProcess = null;
        try
        {
            var pure = await staticService.PreviewAsync(libraryRoot, definition, recipe, "Static header dragging");
            var floorRecipe=ContentJson.Clone(recipe);var floorBox=floorRecipe.HeaderLayout.Blocks.Single(row=>row.Id=="padding-floor-row").Elements.Single();
            floorBox.WidthPixels=40;floorBox.Height=40;
            var floor=await staticService.PreviewAsync(libraryRoot,definition,floorRecipe,"Saved padded Box minimum");
            Check(!pure.Interactive, "Ordinary header elements use the real static renderer for pointer movement.");
            staticProcess = Process.GetProcessById(staticService.OwnedProcessId ?? throw new InvalidOperationException("The static preview renderer is missing."));
            var catalog = new HyperlibWorkProjectService(libraryRoot).GetCatalog();
            HyperlibWorkTemplateChoice Choice(string component) => catalog.First(item => item.GroupId == "blank" && item.Kind == "element"
                && (item.ComponentName == component || item.ComponentName.EndsWith("." + component, StringComparison.Ordinal)));
            var counter = Choice("CounterBlock"); var wave = Choice("WaveBlock");
            recipe.HeaderLayout.Blocks.Add(new() { Id = "interactive-tools-row", Height = 370, Elements = [
                new() { Id = "drag-counter", Kind = "template", TemplateObjectId = counter.Id, TemplateTitle = counter.Title, X = 3, Y = 5,
                    WidthMode = "pixels", WidthPixels = 330, Height = 175, TemplateValues = new() { ["element"] = new() {
                        ["label"] = ContentJson.Value("Counter state survives movement"), ["start"] = ContentJson.Value(7) } } },
                new() { Id = "drag-wave", Kind = "template", TemplateObjectId = wave.Id, TemplateTitle = wave.Title, X = 43, Y = 6,
                    WidthPercent = 54, Height = 310, TemplateValues = new() { ["element"] = new() {
                        ["frequency"] = ContentJson.Value(2), ["amplitude"] = ContentJson.Value(.5) } } }
            ] });
            definition.SelectedToolsetIds = [counter.Id, wave.Id];
            definition.LandingTemplate = ContentJson.Clone(recipe); definition.PageTemplate = ContentJson.Clone(recipe);
            var interactive = await interactiveService.PreviewAsync(libraryRoot, definition, recipe, "Interactive header dragging");
            Check(interactive.Interactive, "A header containing real library tools uses the existing interactive circuit.");
            interactiveProcess = Process.GetProcessById(interactiveService.OwnedProcessId ?? throw new InvalidOperationException("The interactive renderer is missing."));
            recipe.HeaderLayout.Blocks[0].Elements[0].X = 35; recipe.HeaderLayout.Blocks[0].Elements[0].Y = 30;
            recipe.HeaderLayout.Blocks[0].Elements[0].PositionAlignment = "manual";
            var committed = await interactiveService.PreviewAsync(libraryRoot, definition, recipe, "Interactive committed movement");
            recipe.HeaderLayout.Blocks[0].Elements[0].X = 8; recipe.HeaderLayout.Blocks[0].Elements[0].Y = 10;
            recipe.HeaderLayout.Blocks[0].Elements[0].WidthPixels = 220; recipe.HeaderLayout.Blocks[0].Elements[0].Height = 80;
            var resized = await interactiveService.PreviewAsync(libraryRoot, definition, recipe, "Interactive committed resize");
            recipe.NavigationLayout = HyperlibNavigationLayouts.Create("simple-links");
            recipe.IncludeNavigation = true; recipe.NavigationPlacement = "top";
            recipe.NavigationLayout.Blocks[0].Arrangement = "positioned";
            recipe.NavigationLayout.Blocks[0].Height = 160;
            recipe.NavigationLayout.Blocks[0].Elements[0].Id = "navigation-links";
            recipe.NavigationLayout.Blocks[0].Elements[0].WidthPercent = 70;
            recipe.NavigationLayout.Blocks[0].Elements[0].Height = 80;
            recipe.BodyStyle.Text = "Body-only region marker";
            var navigation = await interactiveService.PreviewAsync(libraryRoot, definition, recipe, "Navigation builder resize");
            Check(interactiveService.OwnedProcessId == interactiveProcess.Id, "Committed pointer geometry reuses the same renderer and component source.");
            var dragScript = Path.Combine(outputDirectory, "header-preview-drag.js");
            await File.WriteAllTextAsync(dragScript, HyperlibHeaderPreviewDragSource.Script, new UTF8Encoding(false));
            var config = Path.Combine(outputDirectory, "header-preview-drag-browser.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { staticUrl = pure.Url.AbsoluteUri,
                interactiveUrl = interactive.Url.AbsoluteUri, committedRequestId = committed.RequestId, resizeRequestId = resized.RequestId,
                navigationUrl = navigation.Url.AbsoluteUri, floorUrl = floor.Url.AbsoluteUri, dragScript }, ContentJson.Options), new UTF8Encoding(false));
            var script = Path.Combine(AppContext.BaseDirectory, browserScriptName);
            if (!File.Exists(script)) throw new FileNotFoundException("Copy verify-header-preview-drag.cjs beside the verifier.", script);
            var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest {
                ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe", Arguments = [script, config, outputDirectory],
                WorkingDirectory = outputDirectory, Timeout = TimeSpan.FromSeconds(120), MaximumStandardOutputCharacters = 20000, MaximumStandardErrorCharacters = 20000 });
            Check(result.ExitCode == 0, "Real pointer drags, nested/padded bounds, cancellation, flow prevention, stale capabilities and tool state passed browser checks. " + result.StandardOutput + result.StandardError);
        }
        finally { staticService.Dispose(); interactiveService.Dispose(); }
        foreach (var process in new[] { staticProcess, interactiveProcess }.OfType<Process>())
        { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Check(process.HasExited, "The pointer verifier releases its owned preview process."); process.Dispose(); }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "header-preview-drag-result.json"), JsonSerializer.Serialize(new { result = "HYPERLIB_HEADER_PREVIEW_DRAG_OK", checks }, new JsonSerializerOptions { WriteIndented = true }));
        return "HYPERLIB_HEADER_PREVIEW_DRAG_OK " + checks.Count;
    }
}
