using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class HyperlibHeaderTemplatePreviewVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string libraryRoot)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var catalogue = new HyperlibWorkProjectService(libraryRoot).GetCatalog();
        HyperlibWorkTemplateChoice Choice(string component) => catalogue.First(item => item.GroupId == "blank" && item.Kind == "element"
            && (item.ComponentName == component || item.ComponentName.EndsWith("." + component, StringComparison.Ordinal)));
        var counter = Choice("CounterBlock"); var wave = Choice("WaveBlock"); var text = Choice("TextBlock"); var image = Choice("ImageBlock");
        HyperlibHeaderElementDefinition Template(string id, HyperlibWorkTemplateChoice choice, double x, double y, double width, double height,
            Dictionary<string, JsonElement> values) => new() { Id = id, Kind = "template", TemplateObjectId = choice.Id, TemplateTitle = choice.Title,
                X = x, Y = y, WidthPercent = width, Height = height, TemplateValues = new() { ["element"] = values } };
        var picture = Path.Combine(outputDirectory, "library-header-picture.svg");
        await File.WriteAllTextAsync(picture, "<svg xmlns='http://www.w3.org/2000/svg' width='220' height='130'><rect width='220' height='130' fill='#aed4ce'/><circle cx='170' cy='30' r='18' fill='#ffe7ae'/><path d='M0 130 65 28 140 130 185 70 220 130' fill='#3d7777'/></svg>", new UTF8Encoding(false));
        var firstCounter = Template("counter-one", counter, 3, 8, 43, 175, new() { ["label"] = ContentJson.Value("Library counter"), ["start"] = ContentJson.Value(7) });
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = true, FooterPlacement = "none", BackgroundColor = "#14252f",
            HeaderLayout = new() { Enabled = true, Blocks = [new() { Id = "template-identity-row", Height = 250, Elements = [firstCounter,
                new() { Id = "nested-box", Kind = "box", X = 50, Y = 5, WidthPercent = 47, Height = 220, FillColor = "#253e45", BorderWidth = 1,
                    Padding = 8, Children = [Template("counter-two", counter, 4, 8, 92, 160, new() { ["label"] = ContentJson.Value("Independent counter"), ["start"] = ContentJson.Value(9) })] }] },
                new() { Id = "template-tools-row", Height = 430, Elements = [Template("wave-tool", wave, 3, 5, 60, 350,
                    new() { ["frequency"] = ContentJson.Value(2), ["amplitude"] = ContentJson.Value(.5) }),
                    Template("library-text", text, 67, 5, 30, 125, new() { ["text"] = ContentJson.Value("Real library text, placed inside the reusable header.") }),
                    Template("library-image", image, 67, 39, 30, 240, new() { ["src"] = ContentJson.Value(picture), ["alt"] = ContentJson.Value("Owned mountain illustration"), ["caption"] = ContentJson.Value("A genuine image template") })] }] } };
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Actual header library objects",
            SelectedToolsetIds = [counter.Id, wave.Id, text.Id, image.Id], LandingTemplate = ContentJson.Clone(recipe), PageTemplate = ContentJson.Clone(recipe) };
        var cache = Path.Combine(outputDirectory, "preview-cache");
        using var service = new HyperlibGuidedPagePreviewService(cache);
        using var portable = new HyperlibGuidedPagePreviewService(cache);
        Process? process = null;
        try
        {
            var first = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            Check(first.Interactive && first.RequestId is not null, "Headers containing library objects use a genuine interactive component circuit.");
            process = Process.GetProcessById(service.OwnedProcessId ?? throw new InvalidOperationException("No owned preview renderer."));
            var ready = Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories).ToDictionary(path => path, File.GetLastWriteTimeUtc);
            recipe.HeaderLayout.Blocks[0].Height = 285; recipe.HeaderLayout.Blocks[0].Elements[1].WidthPercent = 45;
            var geometry = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            firstCounter.TemplateValues["element"]["label"] = ContentJson.Value("Updated actual template"); firstCounter.TemplateValues["element"]["start"] = ContentJson.Value(20);
            var fields = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            firstCounter.TemplateValues["element"]["label"] = ContentJson.Value("Stale field update");
            var stale = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            firstCounter.TemplateValues["element"]["label"] = ContentJson.Value("Latest field update");
            var latest = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            var firstRow = recipe.HeaderLayout.Blocks[0]; var toolsRow = recipe.HeaderLayout.Blocks[1];
            firstRow.Arrangement = "horizontal"; firstCounter.WidthPercent = 55;
            var nestedBox = firstRow.Elements[1]; nestedBox.Arrangement = "vertical"; nestedBox.WidthPercent = 45;
            nestedBox.Children[0].WidthPercent = 100;
            nestedBox.Children.Add(new() { Id = "box-following-text", Text = "Additional content stays inside this fixed-height Box.", Height = 80, WidthPercent = 100, FontSize = 16 });
            nestedBox.Children.Add(new() { Id = "box-following-image", Kind = "image", WidthMode = "pixels", WidthPixels = 128, Height = 96, MediaPath = picture, Alt = "Box illustration" });
            toolsRow.Arrangement = "vertical"; toolsRow.Height = 650;
            foreach (var element in toolsRow.Elements) element.WidthPercent = 100;
            var imageElement = toolsRow.Elements.Single(element => element.Id == "library-image");
            imageElement.WidthMode = "pixels"; imageElement.WidthPixels = 128; imageElement.Height = 96;
            recipe.HeaderLayout.Blocks.Add(new() { Id = "flow-media-row", Arrangement = "vertical", Height = 120, Padding = 4,
                Elements = [new() { Id = "flow-picture-one", Kind = "image", WidthMode = "pixels", WidthPixels = 128, Height = 96, MediaPath = picture, Alt = "First stacked illustration" },
                    new() { Id = "flow-picture-two", Kind = "image", WidthMode = "pixels", WidthPixels = 128, Height = 96, MediaPath = picture, Alt = "Second stacked illustration" }] });
            recipe.HeaderLayout.Blocks.Add(new() { Id = "after-flow-media-row", Arrangement = "horizontal", Height = 48,
                Elements = [new() { Id = "after-flow-text", Text = "The following row stays below stacked content.", WidthPercent = 100, FontSize = 16 }] });
            var flow = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            firstRow.Elements.Reverse();
            toolsRow.Elements.Reverse();
            var reordered = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            firstRow.Arrangement = "positioned"; toolsRow.Arrangement = "positioned";
            var positioned = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            firstRow.Arrangement = "horizontal"; toolsRow.Arrangement = "vertical";
            var restoredFlow = await service.PreviewAsync(libraryRoot, definition, recipe, "Interactive header library preview");
            Check(service.OwnedProcessId == process.Id && ready.All(item => File.GetLastWriteTimeUtc(item.Key) == item.Value)
                && Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories).Count() == ready.Count,
                "Header template field and geometry edits reuse their existing source build and owned renderer.");
            definition.LandingTemplate = ContentJson.Clone(recipe); definition.PageTemplate = ContentJson.Clone(recipe);
            var project = await new HyperlibWorkProjectService(libraryRoot).CreateAsync(outputDirectory, "owned-header-template-project", definition);
            var saved = await new HyperlibProjectPipelineService().ReadDefinitionAsync(project) ?? throw new InvalidOperationException("Missing saved template recipe.");
            var savedImage = HyperlibHeaderLayouts.Elements(saved.LandingTemplate.HeaderLayout).Single(item => item.Id == "library-image");
            Check(savedImage.TemplateValues["element"]["src"].GetString()!.StartsWith("/media/", StringComparison.Ordinal),
                "Images in library-template fields are imported into the portable project.");
            var reopened = await portable.PreviewAsync(Path.Combine(outputDirectory, "source-catalogue-unavailable"), saved,
                saved.LandingTemplate, "Portable real header objects", ownedProjectRoot: project);
            Check(reopened.Interactive, "A portable owned project retains real interactive header objects without its source catalogue.");
            var config = Path.Combine(outputDirectory, "header-template-browser.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { firstUrl = first.Url.AbsoluteUri, geometryRequestId = geometry.RequestId,
                fieldsRequestId = fields.RequestId, staleRequestId = stale.RequestId, latestRequestId = latest.RequestId,
                flowRequestId = flow.RequestId, reorderedRequestId = reordered.RequestId, positionedRequestId = positioned.RequestId, restoredFlowRequestId = restoredFlow.RequestId,
                reopenedUrl = reopened.Url.AbsoluteUri, ownedProjectRoot = project }, ContentJson.Options), new UTF8Encoding(false));
            var script = Path.Combine(AppContext.BaseDirectory, "verify-header-template-previews.cjs");
            if (!File.Exists(script)) throw new FileNotFoundException("Copy verify-header-template-previews.cjs beside the verifier.", script);
            var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest {
                ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe", Arguments = [script, config, outputDirectory],
                WorkingDirectory = outputDirectory, Timeout = TimeSpan.FromSeconds(90), MaximumStandardOutputCharacters = 20000, MaximumStandardErrorCharacters = 20000 });
            Check(result.ExitCode == 0, "Actual JS/C# tools, field editing, nested instances, media, same-document updates, and owned reopening passed browser checks. " + result.StandardOutput + result.StandardError);
        }
        finally { service.Dispose(); portable.Dispose(); }
        if (process is not null) { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); Check(process.HasExited, "The template verifier releases its owned renderer."); process.Dispose(); }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "header-template-preview-result.json"), JsonSerializer.Serialize(new { result = "HYPERLIB_HEADER_TEMPLATE_PREVIEW_OK", checks }, new JsonSerializerOptions { WriteIndented = true }));
        return "HYPERLIB_HEADER_TEMPLATE_PREVIEW_OK " + checks.Count;
    }
}
