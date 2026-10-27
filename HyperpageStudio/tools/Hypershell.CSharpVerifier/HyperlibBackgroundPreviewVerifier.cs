using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class HyperlibBackgroundPreviewVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string libraryRoot)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); checks.Add(detail); }
        Check(HyperlibBackgroundDesigns.All.Count == 10 && HyperlibBackgroundDesigns.All.Select(item => item.Id).Distinct().Count() == 10,
            "The curated background catalogue contains exactly ten distinct designs.");
        var designs = new List<object>();
        foreach (var design in HyperlibBackgroundDesigns.All)
        {
            var css = HyperlibBackgroundDesigns.SurfaceCss(design.DefaultColor, new() { DesignId = design.Id });
            Check(JsonSerializer.Serialize(css, ContentJson.Options).Length < 3500,
                design.Title + ": the exact serialized CSS stays below the durable appearance-value boundary.");
            var html = HyperlibBackgroundDesigns.ThumbnailHtml(design.Id);
            var htmlPath = Path.Combine(outputDirectory, design.Id + ".html");
            await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false));
            designs.Add(new { design.Id, design.Title, design.DefaultColor, htmlPath,
                css });
        }
        Check(HyperlibBackgroundDesigns.All.Select(item => HyperlibBackgroundDesigns.SurfaceCss(item.DefaultColor, new() { DesignId = item.Id }))
            .Distinct(StringComparer.Ordinal).Count() == 10, "Every design has its own actual CSS or SVG geometry.");
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Background preview fixture", SelectedToolsetIds = [] };
        var recipe = new HyperlibPageTemplateDefinition { IncludeNavigation = false, HeaderPlacement = "none", FooterPlacement = "none",
            BackgroundColor = "#060e1f", BackgroundSurface = new() { DesignId = "atlas-night", BorderColor = "#7aa2c6", BorderWidth = 2, BorderRadius = 18 } };
        var cache = Path.Combine(outputDirectory, "preview-cache");
        var service = new HyperlibGuidedPagePreviewService(cache);
        Process? process = null;
        try
        {
            var first = await service.PreviewAsync(libraryRoot, definition, recipe, "Atlas night");
            process = Process.GetProcessById(service.OwnedProcessId ?? throw new InvalidOperationException("No owned preview process."));
            var processId = process.Id;
            var ready = Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.GetLastWriteTimeUtc);
            var pages = new List<object>();
            foreach (var design in HyperlibBackgroundDesigns.All)
            {
                recipe.BackgroundColor = design.DefaultColor; recipe.BackgroundSurface.DesignId = design.Id;
                var rendered = await service.PreviewAsync(libraryRoot, definition, recipe, design.Title);
                pages.Add(new { design.Id, design.Title, url = rendered.Url.AbsoluteUri });
            }
            Check(service.OwnedProcessId == processId && ready.All(item => File.GetLastWriteTimeUtc(item.Key) == item.Value)
                && Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories).Count() == ready.Count,
                "Switching all ten backgrounds reuses the same renderer without recompiling source.");
            var image = Path.Combine(outputDirectory, "chosen-background.png");
            await File.WriteAllBytesAsync(image, ImagePng(192, 112));
            recipe.BackgroundColor = "#1d3040";
            recipe.BackgroundSurface = new() { DesignId = "image", ImagePath = image, ImageFit = "cover", BorderColor = "#7aa2c6", BorderWidth = 6, BorderStyle = "dashed", BorderRadius = 24 };
            var imagePreview = await service.PreviewAsync(libraryRoot, definition, recipe, "User image background");
            var liveUpdates = new List<object>();
            foreach (var fit in new[] { "cover", "contain", "tile" })
            {
                recipe.BackgroundSurface.ImageFit = fit;
                liveUpdates.Add(new { mode = fit, color = recipe.BackgroundColor,
                    css = await service.PreviewBackgroundCssAsync(libraryRoot, recipe.BackgroundColor, recipe.BackgroundSurface) });
            }
            recipe.BackgroundSurface = new() { DesignId = "atlas-dense", BorderWidth = 3, BorderStyle = "dotted", BorderColor = "#cc9966", BorderRadius = 30 };
            foreach (var color in new[] { "#182b45", "#293e59", "#4a6278", "#9faeb8" })
                liveUpdates.Add(new { mode = "tint", color, css = await service.PreviewBackgroundCssAsync(libraryRoot, color, recipe.BackgroundSurface) });
            recipe.BackgroundSurface = new() { DesignId = "solid", BorderWidth = 0, BorderRadius = 0 };
            liveUpdates.Add(new { mode = "solid", color = "#abc8d3", css = await service.PreviewBackgroundCssAsync(libraryRoot, "#abc8d3", recipe.BackgroundSurface) });
            Check(service.OwnedProcessId == processId && ready.All(item => File.GetLastWriteTimeUtc(item.Key) == item.Value),
                "Color, border, and image-fit updates do not prepare or rebuild component source.");
            var request = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Owned image background", SelectedToolsetIds = [],
                LandingTemplate = new() { IncludeNavigation = false, BackgroundColor = "#1d3040", BackgroundSurface = new() {
                    DesignId = "image", ImagePath = image, ImageFit = "contain", BorderColor = "#7aa2c6", BorderWidth = 6, BorderStyle = "dashed", BorderRadius = 24 } },
                PageTemplate = new() { IncludeNavigation = false, BackgroundSurface = new() { DesignId = "atlas-night" } } };
            var ownedRoot = await new HyperlibWorkProjectService(libraryRoot).CreateAsync(outputDirectory, "owned-background-project", request);
            var saved = await new HyperlibProjectPipelineService().ReadDefinitionAsync(ownedRoot) ?? throw new InvalidOperationException("The saved background definition is missing.");
            Check(saved.LandingTemplate.BackgroundSurface.ImagePath.StartsWith("/media/", StringComparison.Ordinal)
                && saved.LandingTemplate.BackgroundSurface.ImageFit == "contain" && saved.LandingTemplate.BackgroundSurface.BorderWidth == 6,
                "The chosen image and border settings persist as project-owned portable media.");
            var missingCatalogue = Path.Combine(outputDirectory, "original-catalogue-unavailable");
            using var portableService = new HyperlibGuidedPagePreviewService(cache);
            var reopened = await portableService.PreviewAsync(missingCatalogue, saved, saved.LandingTemplate, "Reopened image background", ownedProjectRoot: ownedRoot);
            using var http = new HttpClient();
            var reopenedHtml = await http.GetStringAsync(reopened.Url);
            Check(reopenedHtml.Contains("background-size:contain", StringComparison.Ordinal) && reopenedHtml.Contains("border:6px dashed #7aa2c6", StringComparison.Ordinal),
                "A saved image background reopens with the original catalogue unavailable.");
            var liveConfiguration = new { designs, pages, imageUrl = imagePreview.Url.AbsoluteUri, reopenedUrl = reopened.Url.AbsoluteUri, liveUpdates };
            var configurationPath = Path.Combine(outputDirectory, "background-browser.json");
            await File.WriteAllTextAsync(configurationPath, JsonSerializer.Serialize(liveConfiguration, ContentJson.Options), new UTF8Encoding(false));
            var script = Path.Combine(AppContext.BaseDirectory, "verify-background-previews.cjs");
            if (!File.Exists(script)) throw new FileNotFoundException("Copy verify-background-previews.cjs beside the verifier for visual QA.", script);
            var result = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest {
                ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
                Arguments = [script, configurationPath, outputDirectory], WorkingDirectory = outputDirectory,
                Timeout = TimeSpan.FromSeconds(60), MaximumStandardOutputCharacters = 20000, MaximumStandardErrorCharacters = 20000 });
            Check(result.ExitCode == 0, "All ten real backgrounds, uploaded image fits, and immediate color/border updates passed browser checks. " + result.StandardOutput + result.StandardError);
        }
        finally { service.Dispose(); }
        if (process is not null)
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(process.HasExited, "The background verifier stops its owned renderer process."); process.Dispose();
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "background-preview-result.json"), JsonSerializer.Serialize(new { result = "HYPERLIB_BACKGROUND_PREVIEW_OK", checks }, new JsonSerializerOptions { WriteIndented = true }));
        return "HYPERLIB_BACKGROUND_PREVIEW_OK " + checks.Count;
    }

    private static byte[] ImagePng(int width, int height)
    {
        using var image = new MemoryStream(); image.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        void Chunk(string type, byte[] data)
        {
            var size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, data.Length); image.Write(size);
            var name = Encoding.ASCII.GetBytes(type); image.Write(name); image.Write(data);
            uint crc = uint.MaxValue;
            foreach (var value in name.Concat(data)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(size, ~crc); image.Write(size);
        }
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            for (var y = 0; y < height; y++)
            {
                zip.WriteByte(0);
                for (var x = 0; x < width; x++)
                {
                    var left = x < width / 2; var light = (x + y) / 20 % 2 == 0 ? 12 : 0;
                    zip.Write([(byte)((left ? 166 : 53) + light), (byte)((left ? 93 : 111) + light), (byte)((left ? 65 : 147) + light), 255]);
                }
            }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return image.ToArray();
    }
}
