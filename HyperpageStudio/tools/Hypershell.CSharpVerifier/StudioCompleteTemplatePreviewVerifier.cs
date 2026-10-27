using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

public static class StudioCompleteTemplatePreviewVerifier
{
    public static async Task<string> RunAsync(string output, string libraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var gallery = new StudioCompleteTemplateService(libraryRoot);
        var visual = gallery.List().First(item => item.Id.StartsWith("visual:", StringComparison.Ordinal) && item.Collection == "Atlas Archive");
        var coding = gallery.List().Single(item => item.Id == "visual:atlas.archive.page.coding.v1");
        var choices = new[] { (Name: "original-atlas", Choice: visual), (Name: "original-coding", Choice: coding),
            (Name: "original-dashboard", Choice: gallery.List().Single(item => item.Id == "hyperlib:insight-dashboard")) };
        var checks = new List<string>();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        foreach (var (name, choice) in choices)
        {
            using var preview = new HyperlibGuidedPagePreviewService(Path.Combine(output, "preview-cache"));
            var definition = gallery.BuildDefinition(choice.Id, choice.Title);
            var result = await preview.PreviewAsync(libraryRoot, definition, definition.LandingTemplate, choice.Title, ct);
            Require(result.Interactive && result.Url.AbsolutePath.StartsWith("/interactive/", StringComparison.Ordinal), "Complete page originals use the actual interactive component host.");
            using var response = await http.GetAsync(result.Url, ct);
            var policies = response.Headers.TryGetValues("Content-Security-Policy", out var values) ? string.Join("; ", values) : "";
            Require(response.IsSuccessStatusCode && !response.Headers.Contains("X-Frame-Options")
                && policies.Contains("frame-ancestors 'self' http://127.0.0.1:* http://localhost:*", StringComparison.Ordinal)
                && policies.Split("frame-ancestors", StringSplitOptions.None).Length == 2,
                "Complete previews permit the different-port Studio parent while retaining an explicit local-only ancestor policy.");
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(ct));
            Require(html.Contains("guided-preview.js", StringComparison.Ordinal) && html.Contains("blazor.web.js", StringComparison.Ordinal), "The private complete preview loads its protected interactive runtime.");
            Require(!html.Contains(".hl-document{box-sizing:border-box;min-height:100vh;padding:28px}", StringComparison.Ordinal)
                && html.Contains(".studio-staged-layout{padding:28px}", StringComparison.Ordinal), "Served interactive source scopes wizard padding away from complete original canvases.");
            await File.WriteAllTextAsync(Path.Combine(output, name + "-served-preview.html"), html, ct);
            var expected = choice.Id.StartsWith("visual:", StringComparison.Ordinal)
                ? Texts(gallery.GetVisualTemplate(choice.Id).Root).Where(text => text.Length > 4).Take(3).ToArray()
                : new[] { "Active projects", "Finished projects", "Experiments" };
            if (name == "original-dashboard")
            {
                // Configured names are part of the original source data; do not hard-code a newer redesign into this QA.
                var original = new HyperlibWorkProjectService(libraryRoot).GetCatalogObject("insight-dashboard", "page-layout");
                expected = original.Blocks.Where(block => original.Definition.Blocks.Single(schema => schema.Id == block.Type).Component.EndsWith("CounterBlock", StringComparison.Ordinal))
                    .Select(block => block.Values["label"].GetString()!).ToArray();
            }
            Require(expected.Length > 0 && expected.All(html.Contains), "Complete preview retains actual configured original fields: " + choice.Title);
            var configPath = Path.Combine(output, name + "-preview.json");
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { name, url = result.Url.AbsoluteUri, expectedTexts = expected,
                counter = name == "original-dashboard", expectedBackground = choice.Background }, ContentJson.Options), ct);
            var script = Path.Combine(AppContext.BaseDirectory, "verify-complete-template-preview.cjs");
            if (!File.Exists(script)) script = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tools/verify-complete-template-preview.cjs"));
            var run = await new BoundedProcessRunner().RunAsync(new() { ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
                Arguments = [script, configPath, output], WorkingDirectory = output, Timeout = TimeSpan.FromSeconds(90), MaximumStandardOutputCharacters = 16000, MaximumStandardErrorCharacters = 16000 }, ct);
            Require(run.ExitCode == 0, run.StandardOutput + run.StandardError);
            checks.Add(choice.Title + ": actual original fields, responsive Runtime spacing, complete canvas color, no wizard placeholders, and private interactive browser proof. " + run.StandardOutput.Trim());
        }
        var status = "STUDIO_COMPLETE_TEMPLATE_PREVIEW_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-complete-template-preview-result.json"), JsonSerializer.Serialize(new { result = status, checks }, ContentJson.Options), ct);
        return status;
    }
    private static IEnumerable<string> Texts(VisualPageElement root)
    {
        if (!string.IsNullOrWhiteSpace(root.Text)) yield return root.Text;
        foreach (var child in root.Children) foreach (var text in Texts(child)) yield return text;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
