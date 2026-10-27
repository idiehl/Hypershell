using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class HyperlibHeaderPreviewVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string libraryRoot)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        Check(HyperlibHeaderLayouts.All.Count == 10, "Ten editable header foundations are available.");
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Header builder fixture", SelectedToolsetIds = [] };
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = false, FooterPlacement = "none",
            BackgroundColor = "#101d28", HeaderLayout = HyperlibHeaderLayouts.Create("simple-title") };
        var cache = Path.Combine(outputDirectory, "preview-cache");
        using var service = new HyperlibGuidedPagePreviewService(cache);
        using var portableService = new HyperlibGuidedPagePreviewService(cache);
        Process? ownedProcess = null;
        try
        {
            var pages = new List<object>();
            foreach (var preset in HyperlibHeaderLayouts.All)
            {
                recipe.HeaderLayout = HyperlibHeaderLayouts.Create(preset.Id);
                var preview = await service.PreviewAsync(libraryRoot, definition, recipe, preset.Title);
                pages.Add(new { preset.Id, preset.Title, rows = recipe.HeaderLayout.Blocks.Count, url = preview.Url.AbsoluteUri });
                ownedProcess ??= Process.GetProcessById(service.OwnedProcessId ?? throw new InvalidOperationException("The header renderer did not start."));
            }
            var ready = Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.GetLastWriteTimeUtc);
            var processId = service.OwnedProcessId;
            Check(processId == ownedProcess!.Id && ready.Count == 1, "All ten foundations reuse one isolated renderer and component build.");
            var image = Path.Combine(outputDirectory, "header-picture.svg");
            await File.WriteAllTextAsync(image, "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='140' viewBox='0 0 240 140'><rect width='240' height='140' fill='#a5c7c0'/><circle cx='172' cy='38' r='20' fill='#ffe8ac'/><path d='M0 140 62 48 126 140 179 75 240 140' fill='#366f74'/></svg>", new UTF8Encoding(false));
            var video = Path.Combine(outputDirectory, "header-motion.webm");
            var sampleVideo = Path.Combine(libraryRoot, "Media", "sample-motion.webm");
            if (!File.Exists(sampleVideo)) throw new FileNotFoundException("The read-only sample video is required for actual media QA.", sampleVideo);
            File.Copy(sampleVideo, video, overwrite: true);
            var nestedText = new HyperlibHeaderElementDefinition { Id = "nested-title", Text = "A shared identity", X = 8, Y = 12, WidthPercent = 84,
                FontSize = 30, FontWeight = 700, TextColor = "#f5eee1" };
            var row = new HyperlibHeaderBlockDefinition { Id = "main-header", Height = 260, Padding = 0, BorderWidth = 2, BorderRadius = 12,
                FillColor = "#142c37", Elements = [new() { Id = "identity-box", Kind = "box", X = 3, Y = 8, WidthPercent = 49, Height = 205,
                    BorderWidth = 1, BorderColor = "#87b6aa", BorderRadius = 12, FillColor = "#1f4149",
                    Children = [nestedText, new() { Id = "nested-note", Text = "Built from rows and reusable elements", X = 8, Y = 57, WidthPercent = 84, FontSize = 17 }] },
                    new() { Id = "header-picture", Kind = "image", X = 56, Y = 8, WidthPercent = 18, Height = 165, MediaPath = image, Alt = "Mountain illustration", MediaFit = "cover" },
                    new() { Id = "header-video", Kind = "video", X = 77, Y = 8, WidthPercent = 20, Height = 165, MediaPath = video, Alt = "Sample motion" }] };
            recipe.HeaderLayout = new() { Enabled = true, Blocks = [new() { Id = "announcement-row", Height = 52, FillColor = "#244b43",
                Elements = [new() { Id = "announcement-text", Text = "A useful announcement", X = 4, Y = 18, WidthPercent = 92, FontSize = 16, Alignment = "center" }] }, row] };
            var composed = await service.PreviewAsync(libraryRoot, definition, recipe, "Custom header builder");
            var updates = new List<object>();
            foreach (var (size, x, y, width, height) in new[] { (28d, 4d, 10d, 88d, 270d), (34d, 7d, 14d, 82d, 300d), (24d, 10d, 18d, 78d, 280d) })
            {
                nestedText.FontSize = size; nestedText.X = x; nestedText.Y = y; nestedText.WidthPercent = width;
                nestedText.FontFamily = "Georgia, serif"; nestedText.FontWeight = 500; nestedText.Text = "A living header <safe>";
                row.Height = height; row.BorderStyle = "dashed"; row.BorderWidth = 3; row.BorderColor = "#c0a677";
                var html = await service.PreviewHeaderHtmlAsync(libraryRoot, recipe.HeaderLayout);
                updates.Add(new { size, x, y, width, height, script = HyperlibGuidedPagePreviewService.HeaderUpdateScript(html) });
            }
            // Structural edits use the same immediate path and stable IDs, without replacing the Page.
            recipe.HeaderLayout.Blocks.Add(new() { Id = "last-row", Height = 46, FillColor = "#2c414a",
                Elements = [new() { Id = "last-note", Text = "Another header row", X = 4, Y = 10, WidthPercent = 92, FontSize = 15 }] });
            var structureScript = HyperlibGuidedPagePreviewService.HeaderUpdateScript(await service.PreviewHeaderHtmlAsync(libraryRoot, recipe.HeaderLayout));
            Check(service.OwnedProcessId == processId && ready.All(item => File.GetLastWriteTimeUtc(item.Key) == item.Value),
                "Text, typography, positions, row dimensions, and nested media updates do not rebuild or restart the renderer.");
            var mediaFiles = Directory.EnumerateFiles(Path.Combine(cache, "sessions"), "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "media").ToDictionary(path => path, File.GetLastWriteTimeUtc);
            await service.PreviewHeaderHtmlAsync(libraryRoot, recipe.HeaderLayout);
            Check(mediaFiles.All(item => File.GetLastWriteTimeUtc(item.Key) == item.Value), "Repeated live edits reuse immutable imported header media.");
            var request = ContentJson.Clone(definition); request.LandingTemplate = ContentJson.Clone(recipe);
            request.PageTemplate = new() { IncludeNavigation = false, HeaderLayout = ContentJson.Clone(recipe.HeaderLayout) };
            var ownedRoot = await new HyperlibWorkProjectService(libraryRoot).CreateAsync(outputDirectory, "owned-header-project", request);
            var saved = await new HyperlibProjectPipelineService().ReadDefinitionAsync(ownedRoot) ?? throw new InvalidOperationException("The saved header recipe is missing.");
            Check(HyperlibHeaderLayouts.Elements(saved.LandingTemplate.HeaderLayout).Where(item => item.Kind is "image" or "video")
                .All(item => item.MediaPath.StartsWith("/media/", StringComparison.Ordinal)), "Saved header images and video belong to the portable project.");
            var reopened = await portableService.PreviewAsync(Path.Combine(outputDirectory, "missing-catalogue"), saved,
                saved.LandingTemplate, "Portable header reopened", ownedProjectRoot: ownedRoot);
            using var http = new HttpClient();
            Check((await http.GetStringAsync(reopened.Url)).Contains("data-header-row=\"main-header\"", StringComparison.Ordinal),
                "An owned header project reopens with its original catalogue unavailable.");
            var config = Path.Combine(outputDirectory, "header-browser.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { pages, composedUrl = composed.Url.AbsoluteUri,
                reopenedUrl = reopened.Url.AbsoluteUri, updates, structureScript }, ContentJson.Options), new UTF8Encoding(false));
            var script = Path.Combine(AppContext.BaseDirectory, "verify-header-previews.cjs");
            if (!File.Exists(script)) throw new FileNotFoundException("Copy verify-header-previews.cjs beside the verifier.", script);
            var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest {
                ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
                Arguments = [script, config, outputDirectory], WorkingDirectory = outputDirectory, Timeout = TimeSpan.FromSeconds(90),
                MaximumStandardOutputCharacters = 20000, MaximumStandardErrorCharacters = 20000 });
            Check(result.ExitCode == 0, "Actual header media, ten foundations, nested geometry, immediate editing, and narrow rendering passed browser checks. " + result.StandardOutput + result.StandardError);
        }
        finally { service.Dispose(); portableService.Dispose(); }
        if (ownedProcess is not null)
        {
            await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(ownedProcess.HasExited, "The verifier releases its owned preview process."); ownedProcess.Dispose();
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "header-preview-result.json"), JsonSerializer.Serialize(new { result = "HYPERLIB_HEADER_PREVIEW_OK", checks }, new JsonSerializerOptions { WriteIndented = true }));
        return "HYPERLIB_HEADER_PREVIEW_OK " + checks.Count;
    }
}
