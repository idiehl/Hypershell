using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Services;

/// <summary>Builds and renders actual catalog Razor in a private library and cache.</summary>
public static class HyperlibObjectPreviewVerifier
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".hyperlib", "node_modules", "output", "artifacts", "app" };

    public static async Task VerifyAsync(string outputRoot, string sourceLibraryRoot)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLibraryRoot));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Object-preview verification must use an output directory outside the library.");
        var fixture = Path.Combine(output, "object-preview-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(fixture, "library");
        var assertions = new List<string>();
        var before = Fingerprint(source);
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            assertions.Add(description);
        }
        Directory.CreateDirectory(library);
        foreach (var relative in new[] { "src/HyperLib.Core", "src/HyperLib.Runtime", "Starters/standard" })
            Copy(Path.Combine(source, relative), Path.Combine(library, relative));
        var template = await ReusableTemplateManager.CreateBlankAsync(library, "Preview dependencies");
        var project = ReusableTemplateCatalog.GetProjectRoot(library, template.Id);
        var blocks = Path.Combine(project, "src", "Pages", "Blocks");
        await File.WriteAllTextAsync(Path.Combine(blocks, "PreviewNested.razor"), """
            @inject RendererOptions Options
            <div class="preview-nested"><PreviewHelper Text="@Context.Text("text")" /><img src="@Context.Text("src")" alt="Preview media" /></div>
            <span data-default-null>@(Options.Template.Blocks.Single(block => block.Id == "preview-nested").Fields.Single(field => field.Id == "optional").DefaultValue is { } value && value.ValueKind == System.Text.Json.JsonValueKind.Null ? "Explicit null preserved" : "Null default lost")</span>
            @code { [Parameter, EditorRequired] public BlockContext Context { get; set; } = new(); }
            """);
        await File.WriteAllTextAsync(Path.Combine(blocks, "PreviewHelper.razor"), """
            <strong data-custom-helper>@Text</strong>
            @code { [Parameter] public string Text { get; set; } = ""; }
            """);
        await File.WriteAllTextAsync(Path.Combine(blocks, "PreviewNested.razor.css"), ".preview-nested { border: 7px solid rgb(31, 99, 72); padding: 23px; }");
        var assetDirectory = Path.Combine(project, "src", "Pages", "wwwroot", "preview");
        Directory.CreateDirectory(assetDirectory);
        await File.WriteAllTextAsync(Path.Combine(assetDirectory, "helper.js"), "export const previewDependency = 'retained';");
        Directory.CreateDirectory(Path.Combine(library, "Media"));
        await File.WriteAllTextAsync(Path.Combine(library, "Media", "object-preview.svg"), "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 30'><rect width='100' height='30' fill='#1f6348'/></svg>");
        var definition = ReusableTemplateCatalog.ReadDefinition(library, template.Id);
        definition.Blocks.Add(new BlockDefinition { Id = "preview-nested", Title = "Nested preview object", Component = "PreviewNested",
            Fields = [new FieldDefinition { Id = "text", Title = "Text", Type = "string", DefaultValue = ContentJson.Value("Actual nested Razor content") },
                new FieldDefinition { Id = "src", Title = "Image", Type = "string", DefaultValue = ContentJson.Value("/media/object-preview.svg") },
                new FieldDefinition { Id = "optional", Title = "Optional", Type = "text", DefaultValue = ContentJson.Value<object?>(null) }] });
        foreach (var region in definition.Regions.Where(region => region.AllowedBlocks.Count > 0)) region.AllowedBlocks.Add("preview-nested");
        await File.WriteAllTextAsync(Path.Combine(project, "authoring.template.json"), JsonSerializer.Serialize(definition, ContentJson.Options));
        var group = await TemplateObjectLibrary.SaveDesignGroupAsync(library, project, "Preview dependency collection");
        definition.DefaultBlocks = [new PageBlock { Id = "preview-left", Type = "text", Region = "main",
            Values = new() { ["text"] = ContentJson.Value("Left column sample"), ["span"] = ContentJson.Value("half") } },
            new PageBlock { Id = "preview-right", Type = "text", Region = "main",
                Values = new() { ["text"] = ContentJson.Value("Right column sample"), ["span"] = ContentJson.Value("half") } }];
        await File.WriteAllTextAsync(Path.Combine(project, "authoring.template.json"), JsonSerializer.Serialize(definition, ContentJson.Options));
        var layoutGroup = await TemplateObjectLibrary.SaveDesignGroupAsync(library, project, "Preview layout collection");
        var libraryBefore = Fingerprint(library);
        var cache = Path.Combine(fixture, "preview-cache");
        var service = new HyperlibTemplateObjectPreviewService(cache);
        Uri? origin = null;
        try
        {
            var preview = await service.PreviewAsync(library, group.Id, "element-preview-nested");
            origin = new Uri(preview.Url.GetLeftPart(UriPartial.Authority));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await http.GetAsync(preview.Url);
            var html = await response.Content.ReadAsStringAsync();
            Check(response.IsSuccessStatusCode && html.Contains("Actual nested Razor content") && html.Contains("data-custom-helper"),
                "The object preview executes the real custom Razor component and its nested dependency.");
            Check(html.Contains("Explicit null preserved") && !html.Contains("Null default lost"),
                "Explicit null field defaults survive cloning and serialization into the separate Razor preview process.");
            Check(html.Contains("A place for your ideas") && html.Contains("data-object-preview") && html.Contains("data-block-type=\"preview-nested\""),
                "An individual object renders inside generic Page context with the production block wrappers.");
            Check(response.Headers.TryGetValues("Content-Security-Policy", out var policies) && policies.Single().Contains("script-src 'none'"),
                "The visual preview disables scripting, forms, frames, and outbound connections.");
            Check((await http.GetStringAsync(new Uri(origin, "/_content/HyperLib.Runtime/renderer.css"))).Contains(".hl-region"),
                "The preview serves the actual production page and component stylesheet.");
            var scopedBundle = await http.GetStringAsync(new Uri(origin, "/Host.styles.css"));
            var importedCss = Regex.Matches(scopedBundle, "@import\\s+['\"]([^'\"]+)['\"]")
                .Cast<Match>().Select(match => match.Groups[1].Value).ToList();
            var allScopedCss = scopedBundle;
            foreach (var path in importedCss) allScopedCss += await http.GetStringAsync(new Uri(origin, "/" + path.TrimStart('/')));
            Check(allScopedCss.Contains("rgb(31, 99, 72)") && allScopedCss.Contains(".preview-nested["),
                "Custom isolated Razor CSS is compiled, bundled, and served with scoped selectors.");
            Check((await http.GetStringAsync(new Uri(origin, "/_content/HyperLib.Work/preview/helper.js"))).Contains("previewDependency"),
                "Custom component static dependencies are retained in the compiled asset output.");
            Check((await http.GetStringAsync(new Uri(origin, "/media/object-preview.svg"))).Contains("#1f6348"),
                "Referenced library media is copied into the preview cache and resolves locally.");
            Check((await http.GetAsync(new Uri(origin, "/api/state"))).StatusCode == HttpStatusCode.NotFound,
                "The object renderer has no library content API or mutation gateway.");
            var readyBefore = Directory.GetFiles(cache, "ready.json", SearchOption.AllDirectories).Length;
            var fontPreview = await service.PreviewAsync(library, group.Id, "typography");
            var fontHtml = await http.GetStringAsync(fontPreview.Url);
            Check(fontPreview.Url.Port == preview.Url.Port && Directory.GetFiles(cache, "ready.json", SearchOption.AllDirectories).Length == readyBefore,
                "Browsing another object in the same source generation reuses its compiled renderer.");
            Check(fontHtml.Contains("--hl-font:system-ui") && fontHtml.Contains("A place for your ideas"),
                "Typography has a visible generic Page sample even when the source Template has an empty layout.");
            var textPreview = await service.PreviewAsync(library, group.Id, "element-text");
            var textHtml = await http.GetStringAsync(textPreview.Url);
            Check(textHtml.Contains("Start with a thought, add a little detail")
                && TemplateObjectLibrary.ReadDefinition(group.ProjectRoot).Blocks.Single(block => block.Id == "text")
                    .Fields.Single(field => field.Id == "text").DefaultValue?.GetString() == "",
                "Empty text elements receive useful sample copy only in the synthetic preview; saved/imported defaults remain empty.");
            var layoutPreview = await service.PreviewAsync(library, layoutGroup.Id, "page-layout");
            var layoutHtml = await http.GetStringAsync(layoutPreview.Url);
            Check(layoutHtml.Contains("Left column sample") && layoutHtml.Contains("Right column sample")
                && Regex.Matches(layoutHtml, "hl-span-half").Count == 2 && !layoutHtml.Contains("A place for your ideas"),
                "A layout preview renders its complete real arrangement, including both column spans.");
            Check(Fingerprint(library) == libraryBefore, "Previewing objects and typography never writes the private library or its source projects.");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await service.PreviewAsync(library, group.Id, "typography", cancelled.Token); throw new InvalidOperationException("Cancellation was ignored."); }
            catch (OperationCanceledException) { Check(true, "A canceled preview request cannot start another build or host."); }
            await File.WriteAllTextAsync(Path.Combine(fixture, "nested-object.html"), html);
            await File.WriteAllTextAsync(Path.Combine(fixture, "typography.html"), fontHtml);
            await File.WriteAllTextAsync(Path.Combine(fixture, "layout.html"), layoutHtml);
        }
        finally { service.Dispose(); }
        if (origin is not null)
        {
            var stopped = false;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            for (var attempt = 0; attempt < 10 && !stopped; attempt++)
            {
                try { await http.GetAsync(new Uri(origin, "/healthz")); await Task.Delay(100); }
                catch (HttpRequestException) { stopped = true; }
                catch (TaskCanceledException) { stopped = true; }
            }
            Check(stopped, "Closing the preview service terminates its owned renderer process.");
        }
        Check(Fingerprint(source) == before, "The user's source library stays byte-for-byte unchanged throughout verification.");
        await File.WriteAllTextAsync(Path.Combine(fixture, "hyperlib-object-preview.json"),
            JsonSerializer.Serialize(new { passed = true, source, fixture, assertions }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"HYPERLIB_OBJECT_PREVIEW_OK|assertions:{assertions.Count}|actual-razor|{fixture}");
    }

    private static void Copy(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Fixture source links are unsupported.");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Fixture source links are unsupported.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var child in Directory.EnumerateDirectories(source))
            if (!Excluded.Contains(Path.GetFileName(child))) Copy(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    private static string Fingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Visit(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Fixture fingerprints reject links.");
                if (Directory.Exists(path)) { if (!Excluded.Contains(Path.GetFileName(path))) Visit(path); }
                else { hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path))); hash.AppendData(File.ReadAllBytes(path)); }
            }
        }
        Visit(root);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
