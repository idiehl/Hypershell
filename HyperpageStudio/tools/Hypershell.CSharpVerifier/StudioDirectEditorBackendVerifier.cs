using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Real independent Work creation and durable direct-editor schema; browser QA exercises actual gestures separately.</summary>
public static class StudioDirectEditorBackendVerifier
{
    public static async Task<string> VerifyUpgradeAsync(string output, string priorProjectRoot, string sourceLibraryRoot, CancellationToken ct = default)
    {
        var sourceProject = Path.GetFullPath(priorProjectRoot);
        var sourceLibrary = Path.GetFullPath(Path.Combine(sourceProject, "../.."));
        if (!HyperlibWorkPreviewHostService.IsGeneratedLibrary(sourceLibrary)) throw new InvalidDataException("Upgrade verification needs an owned generated Work.");
        var fixture = Path.Combine(Path.GetFullPath(output), "direct-upgrade-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(fixture, "library");
        var sourceHash = StudioGuidedUpgradeVerifier.HashTree(sourceLibrary);
        Copy(sourceLibrary, library);
        var project = Path.Combine(library, Path.GetRelativePath(sourceLibrary, sourceProject));
        var preserved = new[] { Path.Combine(library, "Content", "library.json"), Path.Combine(project, ".studio", "pipeline.json"),
            Path.Combine(project, ".studio", "page-templates.json"), Path.Combine(project, ".studio", "wizard.json") }
            .Where(File.Exists).ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var service = new HyperlibWorkProjectService(sourceLibraryRoot);
        Check(!TemplateObjectLibrary.ReadDefinition(project).Blocks.Any(block => block.Fields.Any(field => field.Id == "studioElements")),
            "The upgrade fixture comes from a previous release without per-element editing support.");
        Check(await service.EnsureDirectEditorSupportAsync(project, ct), "The recognized previous release upgrades its private owned schema and renderer.");
        Check(preserved.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
            "Opening a previous project leaves its content journal, wizard recipes, reusable foundations, and template assignments byte-identical.");
        Check(Directory.EnumerateFiles(Path.Combine(library, ".studio", "upgrades"), "authoring.template.json", SearchOption.AllDirectories).Any(),
            "The previous authoring schema is backed up before the owned support upgrade.");
        Check(TemplateObjectLibrary.ReadDefinition(project).Blocks.All(block => block.Fields.Any(field => field.Id == "studioPositioned")),
            "Every existing element gains optional geometry without modifying any saved Page values.");
        Check(TemplateObjectLibrary.ReadDefinition(project).Blocks.All(block => block.Fields.Count(field => field.Id == "studioElements" && field.Type == "text") == 1),
            "Every previous component gains one optional Page-local element-edit field without changing its existing schema values.");
        foreach (var runtime in new[] { Path.Combine(library, "src", "HyperLib.Runtime"), Path.Combine(project, "Templates", "StudioRuntime", "HyperLib.Runtime") })
        {
            var component = await File.ReadAllTextAsync(Path.Combine(runtime, "Components", "DynamicPage.razor"), ct);
            var script = await File.ReadAllTextAsync(Path.Combine(runtime, "wwwroot", "renderer.js"), ct);
            Check(component.Contains("data-studio-elements", StringComparison.Ordinal)
                && script.Contains("/* studio-element-edits-v1 */", StringComparison.Ordinal)
                && script.Contains("window.hyperlibStudioElements", StringComparison.Ordinal),
                "The previous owned renderer gains element metadata and its matching runtime in " + Path.GetRelativePath(library, runtime) + ".");
        }
        Check(!await service.EnsureDirectEditorSupportAsync(project, ct), "A second open recognizes the upgraded support and performs no further writes.");
        var sourceAfter = StudioGuidedUpgradeVerifier.HashTree(sourceLibrary);
        Check(sourceHash.Count == sourceAfter.Count && sourceHash.All(pair => sourceAfter.TryGetValue(pair.Key, out var hash) && hash == pair.Value),
            "The input previous-release project remains byte-identical because upgrade QA edits only its disposable copy.");
        var result = "STUDIO_DIRECT_EDITOR_UPGRADE_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(fixture, "receipt.json"), JsonSerializer.Serialize(new { result, checks, project, library }, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
        static void Copy(string source, string destination)
        {
            VisualPageProjectService.RejectReparsePoint(source); Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source)) { VisualPageProjectService.RejectReparsePoint(file); File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
            foreach (var directory in Directory.EnumerateDirectories(source))
                if (Path.GetFileName(directory) is not ("bin" or "obj" or ".git" or "node_modules" or ".hyperlib")) Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    public static async Task<string> RunAsync(string output, string sourceLibraryRoot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var sourceContent = Path.Combine(sourceLibraryRoot, "Content", "library.json");
        var sourceHash = File.Exists(sourceContent) ? Hash(sourceContent) : null;
        var request = HyperlibWorkProjectService.BuildDirectEditingDefinition("Direct editor verification");
        Check(request.DirectEditing && request.PipelineVersion == 2 && request.SelectedToolsetIds.Count == 0
            && request.PageDefinitions.Count == 0 && request.PageTemplates.Count == 1,
            "Scratch prepares a direct-editing Work and a reusable empty Page without shopping, a wizard, or a Page count.");
        Check(new[] { request.LandingTemplate, request.PageTemplate }.All(recipe => recipe.DirectEditing && !recipe.IncludeNavigation
            && recipe.HeaderPlacement == "none" && recipe.FooterPlacement == "none" && recipe.BodyStyle.Padding == 0),
            "The empty canvas does not receive synthetic navigation, header/footer chrome, or content slots.");
        var service = new HyperlibWorkProjectService(sourceLibraryRoot);
        var catalogBefore = service.GetCatalog().Select(choice => choice.Id).ToArray();
        var galleryBefore = new StudioCompleteTemplateService(sourceLibraryRoot).List().Select(choice => choice.Id).ToArray();
        var project = await service.CreateAsync(Path.Combine(output, "direct-project-" + Guid.NewGuid().ToString("N")), "site", request, ct);
        var library = Path.GetFullPath(Path.Combine(project, "../.."));
        var pipeline = await new HyperlibProjectPipelineService().ReadAsync(project, ct) ?? throw new InvalidDataException("The project pipeline is missing.");
        var definition = TemplateObjectLibrary.ReadDefinition(project);
        var store = new ContentStore(library) { TemplateResolver = id => id == pipeline.WorkId ? definition : null };
        var state = await store.ReadAsync(ct);
        Check(pipeline.DirectEditing && pipeline.LandingTemplate.Blocks.Count == 0 && pipeline.PageTemplate.Blocks.Count == 0
            && state.Pages.Count == 1 && state.Pages[pipeline.LandingPageId].Blocks.Count == 0,
            "Fresh creation opens one genuinely empty landing Page with reusable foundations kept independently.");
        Check(definition.Blocks.Count >= 16 && new[] { "TextBlock", "RichTextBlock", "ImageBlock", "GalleryBlock", "AudioBlock", "VideoBlock" }
            .All(name => definition.Blocks.Any(block => block.Component.Split('.').Last() == name)),
            "The blank project owns the real Hyperlib text, rich text, media and interactive element schemas.");
        var fields = new[] { "studioPositioned", "studioX", "studioY", "studioWidth", "studioHeight" };
        Check(definition.Blocks.All(block => fields.All(id => block.Fields.Count(field => field.Id == id) == 1))
            && definition.Blocks.All(block => block.Fields.Single(field => field.Id == "studioY").Max == 100000
                && block.Fields.Single(field => field.Id == "studioWidth").Min == 1),
            "Every owned element declares bounded optional per-Page canvas geometry exactly once.");
        Check(pipeline.LandingTemplate.Appearance["studioDirectEditing"].GetBoolean()
            && !pipeline.LandingTemplate.Appearance["studioHeaderEnabled"].GetBoolean()
            && !pipeline.LandingTemplate.Appearance["studioFooterEnabled"].GetBoolean(),
            "The saved landing appearance records direct editing without false complete-template identity.");
        var manifest = JsonSerializer.Deserialize<VisualPageProjectManifest>(await File.ReadAllTextAsync(Path.Combine(project, "visual-page.project.json"), ct), ContentJson.Options)!;
        Check(manifest.LayoutOverride is { IsLeftToolPanelOpen: false, IsRightToolPanelOpen: false },
            "The native project panels start collapsed so Contents and Elements/Properties are not duplicated.");
        Check((await File.ReadAllTextAsync(Path.Combine(project, "README.md"), ct)).Contains("opens directly", StringComparison.Ordinal),
            "The generated README explains direct visual editing and portable project ownership.");
        var runtimePath = Path.Combine(project, "Templates", "StudioRuntime", "HyperLib.Runtime", "Components", "DynamicPage.razor");
        var runtime = await File.ReadAllTextAsync(runtimePath, ct);
        Check(runtime.Contains("GeometryStyle(effectiveBlock)", StringComparison.Ordinal)
            && runtime.Contains("position:absolute", StringComparison.Ordinal)
            && runtime.Contains("IsCompleteTemplate || IsDirectEditing", StringComparison.Ordinal) && runtime.Contains("studio-direct-page", StringComparison.Ordinal),
            "The project-owned runtime preserves canvas positions in reopened and published Pages without applying wizard layout to direct Pages.");
        Check(runtime.Contains("Options.AppearanceStyle(_state, _pageNode ?? _work) + (IsCompleteTemplate || IsDirectEditing", StringComparison.Ordinal),
            "Direct Pages use their current public appearance instead of overriding background edits with frozen wizard CSS.");
        var ownedScript = await File.ReadAllTextAsync(Path.Combine(project, "Templates", "StudioRuntime", "HyperLib.Runtime", "wwwroot", "renderer.js"), ct);
        var originalScript = await File.ReadAllTextAsync(Path.Combine(sourceLibraryRoot, "src", "HyperLib.Runtime", "wwwroot", "renderer.js"), ct);
        Check(ownedScript.StartsWith(HyperlibGuidedPagePreviewService.ProtectSharedTemplateFields(originalScript), StringComparison.Ordinal)
            && ownedScript.Contains("/* studio-direct-editor-extents-v1 */", StringComparison.Ordinal) && ownedScript.Contains("ResizeObserver", StringComparison.Ordinal),
            "The owned renderer retains its original source and appends a read-only extent observer for naturally sized media and tools.");
        var theme = await File.ReadAllTextAsync(Path.Combine(project, "src", "Pages", "wwwroot", "work-theme.css"), ct);
        Check(theme.Contains(".studio-direct-page .hl-region", StringComparison.Ordinal) && theme.Contains("align-content:start", StringComparison.Ordinal),
            "A direct Page keeps natural text and content rows compact within its taller empty canvas.");
        Check(theme.Contains(".hl-document.studio-direct-page { min-height:480px; }", StringComparison.Ordinal)
            && runtime.Contains("Editing && IsDirectEditing && region == \"main\") style += \";position:relative;min-height:480px\"", StringComparison.Ordinal),
            "Direct canvas minimums use bounded pixels instead of feeding viewport-height units back into an automatically sized frame.");
        Check(!await service.EnsureDirectEditorSupportAsync(project, ct), "Opening a current project is an idempotent no-write support check.");
        var saved = ContentJson.Clone(state.Pages[pipeline.LandingPageId]);
        var textSchema = definition.Blocks.First(block => block.Component.Split('.').Last() == "TextBlock");
        var text = new PageBlock { Id = "direct-text", Type = textSchema.Id, Region = "main", Values = textSchema.Fields.Where(field => field.DefaultValue.HasValue)
            .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal) };
        text.Values["text"] = ContentJson.Value("Independent inline content");
        text.Values["studioPositioned"] = ContentJson.Value(true); text.Values["studioX"] = ContentJson.Value(7.5);
        text.Values["studioY"] = ContentJson.Value(48); text.Values["studioWidth"] = ContentJson.Value(52); text.Values["studioHeight"] = ContentJson.Value(136);
        saved.Blocks.Add(text);
        state = await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Save direct canvas content and geometry",
            Operations = [new() { Kind = "savePage", Page = saved }] }, ct);
        var reloaded = await new ContentStore(library) { TemplateResolver = id => id == pipeline.WorkId ? definition : null }.ReadAsync(ct);
        var restored = reloaded.Pages[pipeline.LandingPageId].Blocks.Single(block => block.Id == text.Id);
        Check(restored.Values["text"].GetString() == "Independent inline content" && restored.Values["studioX"].GetDouble() == 7.5
            && restored.Values["studioY"].GetDouble() == 48 && restored.Values["studioHeight"].GetDouble() == 136,
            "The real content journal saves and reloads independent inline values and direct canvas geometry together.");
        Check((await new HyperlibProjectPipelineService().ReadAsync(project, ct))!.PageTemplate.Blocks.Count == 0,
            "Editing a Page does not contaminate the reusable empty Content Page foundation.");
        var stateHash = Hash(Path.Combine(library, "Content", "library.json"));
        var templateHash = Hash(Path.Combine(project, "authoring.template.json"));
        var originalRuntime = await File.ReadAllTextAsync(runtimePath, ct);
        await File.AppendAllTextAsync(runtimePath, "\n@* User source customization: retain me. *@\n", Encoding.UTF8, ct);
        var customHash = Hash(runtimePath);
        var rejected = false;
        try { await service.EnsureDirectEditorSupportAsync(project, ct); }
        catch (InvalidDataException error) when (error.Message.Contains("custom source", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected && Hash(runtimePath) == customHash && Hash(Path.Combine(project, "authoring.template.json")) == templateHash
            && Hash(Path.Combine(library, "Content", "library.json")) == stateHash,
            "A customized generated renderer is rejected before any source, schema, or Page data is overwritten.");
        await File.WriteAllTextAsync(runtimePath, originalRuntime, new UTF8Encoding(false), ct);
        // Leave a clean empty Page for the browser to exercise the actual Add/inline/drag/resize tools.
        state = await store.ReadAsync(ct);
        var empty = ContentJson.Clone(state.Pages[pipeline.LandingPageId]); empty.Blocks.Clear();
        await store.ApplyAsync(new() { ExpectedRevision = state.Revision, Description = "Prepare direct editor browser fixture",
            Operations = [new() { Kind = "savePage", Page = empty }] }, ct);
        Check(service.GetCatalog().Select(choice => choice.Id).SequenceEqual(catalogBefore)
            && new StudioCompleteTemplateService(sourceLibraryRoot).List().Select(choice => choice.Id).SequenceEqual(galleryBefore),
            "The complete page gallery and every reusable section/object remain available without source-library mutation.");
        Check(sourceHash is null || Hash(sourceContent) == sourceHash, "The original Hyperlib content index remains byte-identical.");
        var result = "STUDIO_DIRECT_EDITOR_BACKEND_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-direct-editor-backend-result.json"),
            JsonSerializer.Serialize(new { result, checks, project, library, landingPageId = pipeline.LandingPageId }, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }
}
