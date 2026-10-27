using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioBodyPreviewVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string libraryRoot)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var image = Path.Combine(outputDirectory, "body-picture.svg");
        await File.WriteAllTextAsync(image, "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='80'><rect width='120' height='80' fill='#a9d9cf'/><circle cx='90' cy='20' r='10' fill='#fff0b1'/><path d='M0 80 35 15 75 80 100 45 120 80' fill='#386d70'/></svg>", new UTF8Encoding(false));
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = false, FooterPlacement = "none", BackgroundColor = "#152b34",
            HeaderLayout = new() { Enabled = true, Blocks = [new() { Id = "body-row", Height = 70, Elements = [
                new() { Id = "body-text", Text = "Protected Header", WidthMode = "pixels", WidthPixels = 220, Height = 45 }] }] },
            BodyLayout = new() { Enabled = true, Blocks = [
                new() { Id = "body-row", Height = 320, Padding = 20, BorderWidth = 2, FillColor = "#1e3e46", Elements = [
                    new() { Id = "body-text", Text = "Build the Body", X = 5, Y = 7, WidthMode = "pixels", WidthPixels = 180, Height = 52, FillColor = "#426b6a", Padding = 8 },
                    new() { Id = "body-box", Kind = "box", X = 38, Y = 8, WidthPercent = 57, Height = 190, Padding = 14, BorderWidth = 2, FillColor = "#31545a", Children = [
                        new() { Id = "body-child", Text = "Nested Body", X = 4, Y = 7, WidthMode = "pixels", WidthPixels = 120, Height = 42, FillColor = "#4e7773" },
                        new() { Id = "body-image", Kind = "image", X = 3, Y = 50, WidthMode = "pixels", WidthPixels = 110, Height = 75, MediaPath = image, Alt = "Owned Body image" }] }] },
                new() { Id = "body-flow-row", Arrangement = "horizontal", Height = 100, Padding = 12, Elements = [
                    new() { Id = "body-flow", Text = "Side by side Body", WidthMode = "pixels", WidthPixels = 190, Height = 45, FillColor = "#426b6a" }] },
                new() { Id = "body-slot-row", Arrangement = "horizontal", Height = 130, Padding = 12, Elements = [
                    new() { Id = "body-text-slot", Kind = "text", IsContentSlot = true, SlotName = "Article title", SlotHint = "Write a title for each Page.", Text = "", WidthPercent = 55, Height = 96 },
                    new() { Id = "body-image-slot", Kind = "image", IsContentSlot = true, SlotName = "Page photograph", SlotHint = "Choose a photograph while adding content.", MediaPath = "", WidthPercent = 40, Height = 96 }] }
            ] } };
        var originalHeader = JsonSerializer.Serialize(recipe.HeaderLayout, ContentJson.Options);
        var originalNavigation = JsonSerializer.Serialize(recipe.NavigationLayout, ContentJson.Options);
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Body builder preview", SelectedToolsetIds = [],
            LandingTemplate = ContentJson.Clone(recipe), PageTemplate = ContentJson.Clone(recipe) };
        var slotRow=recipe.BodyLayout.Blocks.Single(row=>row.Id=="body-slot-row");
        var publicSlots=HyperlibHeaderLayouts.RenderBlock(slotRow,"body");
        Check(publicSlots.Contains("data-content-slot=\"true\"",StringComparison.Ordinal)&&!publicSlots.Contains("data-studio-slot-placeholder",StringComparison.Ordinal)
            &&!publicSlots.Contains("<strong>",StringComparison.Ordinal)&&!publicSlots.Contains("<img",StringComparison.Ordinal),"Published empty slots retain their structure without any preview hint or starter media.");
        using var staticService = new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory, "static-cache"));
        using var interactiveService = new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory, "interactive-cache"));
        var processes = new List<Process>();
        try
        {
            var pure = await staticService.PreviewAsync(libraryRoot, definition, recipe, "Body static preview");
            processes.Add(Process.GetProcessById(staticService.OwnedProcessId ?? throw new InvalidOperationException("The Body preview process is missing.")));
            Check(!pure.Interactive, "Ordinary Body rows use the actual static renderer without an interactive circuit.");
            var staticRecipe = ContentJson.Clone(recipe); staticRecipe.BodyLayout.Blocks[0].Elements[0].Text = "Body live text";
            staticRecipe.BodyLayout.Blocks[0].Elements[0].WidthPixels = 230;
            var bodyHtml = await staticService.PreviewHeaderHtmlAsync(libraryRoot, staticRecipe.BodyLayout, region: "body");
            var staticPatch = HyperlibGuidedPagePreviewService.HeaderUpdateScript(bodyHtml, "body");
            Check(bodyHtml.Contains("data-studio-region=\"body\"", StringComparison.Ordinal) && !bodyHtml.Contains("data-studio-region=\"header\"", StringComparison.Ordinal),
                "Static Body markup carries its own region and the same exact row renderer.");
            var catalog = new HyperlibWorkProjectService(libraryRoot).GetCatalog();
            HyperlibWorkTemplateChoice Choice(string name) => catalog.First(item => item.GroupId == "blank" && item.Kind == "element"
                && (item.ComponentName == name || item.ComponentName.EndsWith("." + name, StringComparison.Ordinal)));
            var counter = Choice("CounterBlock"); var wave = Choice("WaveBlock");
            recipe.BodyLayout.Blocks.Add(new() { Id = "body-tools-row", Height = 360, Elements = [
                new() { Id = "body-counter", Kind = "template", TemplateObjectId = counter.Id, TemplateTitle = counter.Title, X = 3, Y = 4,
                    WidthPercent = 38, Height = 190, TemplateValues = new() { ["element"] = new() { ["label"] = ContentJson.Value("Body counter"), ["start"] = ContentJson.Value(7) } } },
                new() { Id = "body-wave", Kind = "template", TemplateObjectId = wave.Id, TemplateTitle = wave.Title, X = 44, Y = 4,
                    WidthPercent = 53, Height = 310, TemplateValues = new() { ["element"] = new() { ["frequency"] = ContentJson.Value(2), ["amplitude"] = ContentJson.Value(.5) } } }
            ] });
            definition.SelectedToolsetIds = [counter.Id, wave.Id]; definition.LandingTemplate = ContentJson.Clone(recipe); definition.PageTemplate = ContentJson.Clone(recipe);
            var interactive = await interactiveService.PreviewAsync(libraryRoot, definition, recipe, "Body interactive preview");
            var process = Process.GetProcessById(interactiveService.OwnedProcessId ?? throw new InvalidOperationException("The Body interactive process is missing.")); processes.Add(process);
            Check(interactive.Interactive, "Body library tools render as actual interactive components.");
            recipe.BodyLayout.Blocks[0].Elements[0].X = 12; recipe.BodyLayout.Blocks[0].Elements[0].Y = 16;
            recipe.BodyLayout.Blocks[0].Elements[0].WidthPixels = 240; recipe.BodyLayout.Blocks[0].Elements[0].Height = 80;
            var committed = await interactiveService.PreviewAsync(libraryRoot, definition, recipe, "Body committed geometry");
            Check(interactiveService.OwnedProcessId == process.Id, "Body edits reuse the same owned component renderer.");
            Check(originalHeader == JsonSerializer.Serialize(recipe.HeaderLayout, ContentJson.Options)
                && originalNavigation == JsonSerializer.Serialize(recipe.NavigationLayout, ContentJson.Options), "Body edits leave Header and Navigation recipes unchanged.");
            var dragScript = Path.Combine(outputDirectory, "body-preview-drag.js"); await File.WriteAllTextAsync(dragScript, HyperlibHeaderPreviewDragSource.Script, new UTF8Encoding(false));
            var config = Path.Combine(outputDirectory, "body-preview-browser.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { staticUrl = pure.Url.AbsoluteUri, interactiveUrl = interactive.Url.AbsoluteUri,
                committedRequestId = committed.RequestId, staticPatch, dragScript }, ContentJson.Options), new UTF8Encoding(false));
            var script = Path.Combine(AppContext.BaseDirectory, "verify-body-preview.cjs");
            if (!File.Exists(script)) throw new FileNotFoundException("Copy verify-body-preview.cjs beside the verifier.", script);
            var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest { ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
                Arguments = [script, config, outputDirectory], WorkingDirectory = outputDirectory, Timeout = TimeSpan.FromSeconds(120),
                MaximumStandardOutputCharacters = 20000, MaximumStandardErrorCharacters = 20000 });
            Check(result.ExitCode == 0, "Body pointer geometry, static patches, component state and region protection pass real browser checks. " + result.StandardOutput + result.StandardError);
        }
        finally { staticService.Dispose(); interactiveService.Dispose(); }
        foreach (var process in processes) { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Check(process.HasExited, "The Body verifier releases its owned preview process."); process.Dispose(); }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "body-preview-result.json"), JsonSerializer.Serialize(new { result = "STUDIO_BODY_PREVIEW_OK", checks }, new JsonSerializerOptions { WriteIndented = true }));
        return "STUDIO_BODY_PREVIEW_OK " + checks.Count;
    }
}
