using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class HyperlibGuidedPagePreviewVerifier
{
    public static async Task<string> RunAsync(string outputDirectory, string libraryRoot)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<string>();
        void Check(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); checks.Add(detail); }
        var currentDocument = new Uri("http://127.0.0.1:53210/preview/0123456789abcdef0123456789abcdef");
        Check(HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(currentDocument, currentDocument + "#section")
            && !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(currentDocument, "http://127.0.0.1:53210/#section")
            && !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(currentDocument, "http://127.0.0.1:53210/page/other")
            && !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(currentDocument, currentDocument + "?other=true")
            && !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(currentDocument, "https://example.invalid/"),
            "Native preview navigation permits current-document anchors and rejects host-root, other-route, query, and external navigation.");
        var catalog = new HyperlibWorkProjectService(libraryRoot).GetCatalog();
        var header = catalog.First(item => item.GroupId == "blank" && item.Kind == "element" && item.ComponentName.EndsWith("HeaderBlock", StringComparison.Ordinal));
        var body = catalog.First(item => item.GroupId == "blank" && item.Kind == "element" && item.ComponentName.EndsWith("TextBlock", StringComparison.Ordinal));
        var image = catalog.First(item => item.GroupId == "blank" && item.Kind == "element" && item.ComponentName.EndsWith("ImageBlock", StringComparison.Ordinal));
        var ids = new[] { header.Id, body.Id, image.Id };
        var definition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Guided preview fixture", SelectedToolsetIds = ids.ToList() };
        var recipe = new HyperlibPageTemplateDefinition { BackgroundColor = "#162635", HeaderTemplateId = header.Id, BodyTemplateId = body.Id,
            IncludeNavigation = true, HeaderStyle = new() { BackgroundColor = "#223344", TextColor = "#ffffff", Text = "A reusable site header", Padding = 24, BorderWidth = 2, BorderColor = "#44aa88", LogoPlacement = "right" },
            RepeatingObjects = [new() { Id = "preview-image", TemplateObjectId = image.Id, Region = "main", Order = 0 }] };
        var cache = Path.Combine(outputDirectory, "preview-cache");
        var service = new HyperlibGuidedPagePreviewService(cache);
        Uri? origin = null;
        Process? finalProcess = null;
        try
        {
            var empty = await service.PreviewAsync(libraryRoot,
                new() { PipelineVersion = 2, SelectedToolsetIds = [] },
                new() { BackgroundColor = "#162635", IncludeNavigation = false }, "Choose your background");
            using var emptyClient = new HttpClient();
            var emptyHtml = await emptyClient.GetStringAsync(empty.Url);
            Check(emptyHtml.Contains("data-guided-page-preview", StringComparison.Ordinal)
                && !emptyHtml.Contains("data-block-type=", StringComparison.Ordinal),
                "The first background step shows a real empty Page before any object is collected.");
            var first = await service.PreviewAsync(libraryRoot, definition, recipe, "Work landing");
            origin = first.Url;
            using var http = new HttpClient();
            var html = await http.GetStringAsync(first.Url);
            Check(html.Contains("data-guided-page-preview", StringComparison.Ordinal), "The page builder uses an actual Razor renderer.");
            Check(html.Contains("A reusable site header", StringComparison.Ordinal), "Repeated header text is rendered in the composed Page.");
            Check(html.Contains("#162635", StringComparison.Ordinal), "The chosen background color is applied to the full Page.");
            Check(html.Contains("data-block-type", StringComparison.Ordinal) && html.Contains("content slot", StringComparison.Ordinal), "Real imported components render alongside empty content slots.");
            Check(!html.Contains("data-block-type=\"studio-navigation\"", StringComparison.Ordinal)
                && recipe.IncludeNavigation,
                "Guided previews omit synthetic Page navigation without changing the saved recipe's navigation preference; authored template navigation remains eligible to render.");
            var readyFiles = Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.GetLastWriteTimeUtc);
            recipe.BackgroundColor = "#391827";
            recipe.HeaderStyle.Text = "Header revised while typing";
            recipe.HeaderStyle.Padding = 40;
            var second = await service.PreviewAsync(libraryRoot, definition, recipe, "Work landing updated");
            var updated = await http.GetStringAsync(second.Url);
            Check(first.Url.GetLeftPart(UriPartial.Authority) == second.Url.GetLeftPart(UriPartial.Authority), "Style and text changes reuse the same owned renderer process.");
            Check(updated.Contains("#391827", StringComparison.Ordinal) && updated.Contains("Header revised while typing", StringComparison.Ordinal), "The newest recipe updates background, header text, and styling.");
            Check(readyFiles.All(pair => File.GetLastWriteTimeUtc(pair.Key) == pair.Value)
                && Directory.EnumerateFiles(Path.Combine(cache, "generations"), "ready.json", SearchOption.AllDirectories).Count() == readyFiles.Count,
                "Typing and styling never recompile the component source.");
            using var suspendedProcess = Process.GetProcessById(service.OwnedProcessId
                ?? throw new InvalidOperationException("The preview did not retain its owned renderer process."));
            service.Suspend();
            await suspendedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            var hiddenHostRunning = true;
            using var hiddenProbe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try { await hiddenProbe.GetAsync(new Uri(second.Url, "/healthz")); }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { hiddenHostRunning = false; }
            Check(suspendedProcess.HasExited && !hiddenHostRunning, "Hiding the wizard releases its owned loopback renderer.");
            var resumed = await service.PreviewAsync(libraryRoot, definition, recipe, "Builder resumed");
            Check((await http.GetStringAsync(resumed.Url)).Contains("Header revised while typing", StringComparison.Ordinal)
                && readyFiles.All(pair => File.GetLastWriteTimeUtc(pair.Key) == pair.Value),
                "Returning to the wizard restores the current design without recompiling its source.");
            var logo = Path.Combine(outputDirectory, "preview-logo.png");
            await File.WriteAllBytesAsync(logo, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOCkAAAAASUVORK5CYII="));
            recipe.HeaderStyle.LogoPath = logo;
            var third = await service.PreviewAsync(libraryRoot, definition, recipe, "With local logo");
            origin = third.Url;
            finalProcess = Process.GetProcessById(service.OwnedProcessId
                ?? throw new InvalidOperationException("The resumed preview renderer is missing."));
            var logoHtml = await http.GetStringAsync(third.Url);
            Check(logoHtml.Contains("/media/", StringComparison.Ordinal), "A local logo is copied into the isolated preview instead of exposing its filesystem path.");
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "preview-url.txt"), third.Url.AbsoluteUri);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "preview.html"), logoHtml);
            var node = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe";
            var script = Path.Combine(AppContext.BaseDirectory, "verify-guided-preview.cjs");
            // A copied script is optional for standalone service checks; the root verifier places it beside the binary.
            if (File.Exists(script))
            {
                var rendered = await new BoundedProcessRunner().RunAsync(new BoundedProcessRequest { ExecutablePath = node,
                    Arguments = [script, third.Url.AbsoluteUri, outputDirectory], WorkingDirectory = outputDirectory,
                    Timeout = TimeSpan.FromSeconds(45), MaximumStandardOutputCharacters = 12000, MaximumStandardErrorCharacters = 12000 });
                Check(rendered.ExitCode == 0, "The live full-Page preview passed browser layout and local logo checks: " + rendered.StandardOutput + rendered.StandardError);
            }
            var ownedDefinition = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Owned preview fixture",
                SelectedToolsetIds = ids.ToList(), LandingTemplate = ContentJson.Clone(recipe), PageTemplate = ContentJson.Clone(recipe) };
            var ownedWork = await new HyperlibWorkProjectService(libraryRoot).CreateAsync(outputDirectory, "owned-preview-project", ownedDefinition);
            var savedDefinition = await new HyperlibProjectPipelineService().ReadDefinitionAsync(ownedWork)
                ?? throw new InvalidOperationException("The saved private project definition is missing.");
            Check(savedDefinition.LandingTemplate.HeaderStyle.LogoPath.StartsWith("/media/", StringComparison.Ordinal),
                "The saved reusable header owns an imported project logo.");
            using var portableService = new HyperlibGuidedPagePreviewService(Path.Combine(outputDirectory, "portable-preview-cache"));
            var savedPreview = await portableService.PreviewAsync(libraryRoot, savedDefinition, savedDefinition.LandingTemplate,
                "Reopened private project", ownedProjectRoot: ownedWork);
            var savedHtml = await http.GetStringAsync(savedPreview.Url);
            var logoMatch = System.Text.RegularExpressions.Regex.Match(savedHtml, "src=\"(?<path>/media/[a-f0-9]{64}\\.[a-z]+)\"");
            Check(logoMatch.Success && (await http.GetByteArrayAsync(new Uri(savedPreview.Url, logoMatch.Groups["path"].Value))).Length > 0,
                "Reopening with the original catalogue reads the logo from the private project's Media folder.");
            var missingSource = Path.Combine(outputDirectory, "unavailable-original-catalogue");
            var portablePreview = await portableService.PreviewAsync(missingSource, savedDefinition, savedDefinition.LandingTemplate,
                "Portable owned project", ownedProjectRoot: ownedWork);
            Check((await http.GetStringAsync(portablePreview.Url)).Contains("Header revised while typing", StringComparison.Ordinal),
                "An owned toolkit renders its Page design even when the original catalogue is unavailable.");
            var hiddenLogo = ContentJson.Clone(savedDefinition.LandingTemplate);
            hiddenLogo.HeaderStyle.LogoPlacement = "none";
            var hiddenPreview = await portableService.PreviewAsync(missingSource, savedDefinition, hiddenLogo,
                "Hidden reusable logo", ownedProjectRoot: ownedWork);
            Check(!(await http.GetStringAsync(hiddenPreview.Url)).Contains("class=\"studio-region-logo\"", StringComparison.Ordinal),
                "Choosing no logo placement hides the stored logo in the live preview.");
        }
        finally { service.Dispose(); }
        if (origin is not null)
        {
            if (finalProcess is not null) await finalProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var stopped = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var running = true;
            try { await stopped.GetAsync(new Uri(origin, "/healthz")); }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { running = false; }
            Check(finalProcess?.HasExited == true && !running, "Disposing the builder stops its owned loopback renderer.");
            finalProcess?.Dispose();
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "guided-preview-result.json"), JsonSerializer.Serialize(new { status = "GUIDED_PREVIEW_OK", checks }, new JsonSerializerOptions { WriteIndented = true }));
        return "GUIDED_PREVIEW_OK " + checks.Count;
    }
}
