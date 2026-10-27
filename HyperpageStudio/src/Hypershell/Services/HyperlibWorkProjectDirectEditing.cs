using System.IO;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed partial class HyperlibWorkProjectService
{
    private const string DirectEditorGeometryCss = """
        /* studio-direct-editor-geometry-v4 */
        .hl-document.studio-direct-page { min-height:480px; }
        .studio-direct-page .hl-region { align-items:start; align-content:start; }
        .studio-direct-page .hl-region>.hl-block { align-self:start; }
        .hl-document .studio-canvas-positioned[data-studio-sized="true"]:is(.hl-type-image,.hl-type-video)>figure { height:100%; min-height:0; display:flex; flex-direction:column; }
        .hl-document .studio-canvas-positioned[data-studio-sized="true"]:is(.hl-type-image,.hl-type-video)>figure>:is(img,video) { width:100%; height:100%; min-height:0; max-height:100%; flex:1 1 0; object-fit:contain; }
        .hl-document .studio-canvas-positioned[data-studio-sized="true"]:is(.hl-type-image,.hl-type-video)>figure>figcaption { flex:0 0 auto; }
        """;

    private static string RefreshDirectEditorGeometryCss(string theme)
    {
        // Earlier generated rules also stretched automatic-height media. Remove only their
        // exact managed text; user CSS surrounding these blocks remains byte-for-byte intact.
        var versionThree = DirectEditorGeometryCss.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("geometry-v4", "geometry-v3", StringComparison.Ordinal)
            .Replace(".hl-document.studio-direct-page { min-height:480px; }\n", "", StringComparison.Ordinal);
        var legacyMedia = versionThree.Replace("[data-studio-sized=\"true\"]", "", StringComparison.Ordinal);
        var versionTwo = legacyMedia.Replace("geometry-v3", "geometry-v2", StringComparison.Ordinal);
        var versionOne = legacyMedia.Replace("geometry-v3", "geometry-v1", StringComparison.Ordinal)
            .Replace(".studio-direct-page .hl-region { align-items:start; align-content:start; }\n", "", StringComparison.Ordinal)
            .Replace(".studio-direct-page .hl-region>.hl-block { align-self:start; }\n", "", StringComparison.Ordinal);
        var versionTwoEarly = versionTwo.Replace(".hl-region>.hl-block {", ".hl-region>.hl-block:not(.studio-canvas-positioned) {", StringComparison.Ordinal);
        foreach (var known in new[] { versionOne, versionTwo, versionTwoEarly, versionThree })
        {
            var normalized = known.Replace("\r\n", "\n", StringComparison.Ordinal);
            theme = theme.Replace(normalized, "", StringComparison.Ordinal)
                .Replace(normalized.Replace("\n", "\r\n", StringComparison.Ordinal), "", StringComparison.Ordinal);
        }
        if (theme.Contains("/* studio-direct-editor-geometry-v1 */", StringComparison.Ordinal)
            || theme.Contains("/* studio-direct-editor-geometry-v2 */", StringComparison.Ordinal)
            || theme.Contains("/* studio-direct-editor-geometry-v3 */", StringComparison.Ordinal))
            throw new InvalidDataException("This project's generated canvas styles have custom source. Preserve those changes before upgrading its canvas media sizing.");
        return theme.Contains("/* studio-direct-editor-geometry-v4 */", StringComparison.Ordinal) ? theme
            : theme + Environment.NewLine + DirectEditorGeometryCss;
    }

    private const string DirectEditorExtentScript = """
        /* studio-direct-editor-extents-v1 */
        (() => {
          if (window.__studioDirectEditorExtents) return;
          window.__studioDirectEditorExtents = true;
          let scheduled = 0;
          const observed = new Set();
          const minimums = new Map();
          const schedule = () => { if (!scheduled) scheduled = requestAnimationFrame(measure); };
          const resize = new ResizeObserver(schedule);
          function measure() {
            scheduled = 0;
            // The native editor owns temporary geometry while a pointer gesture is active.
            // It removes this class on commit/cancel, which schedules the final measurement.
            if (document.body?.classList.contains('studio-canvas-gesture')) return;
            const keep = new Set();
            for (const region of document.querySelectorAll('.hl-document .hl-region')) {
              const blocks = [...region.children].filter(element => element.matches('.hl-block[data-block-id]'));
              keep.add(region);
              let minimum = minimums.get(region);
              const currentMinimum = region.style.minHeight;
              if (!minimum || currentMinimum !== (minimum.applied ?? minimum.original)) {
                minimum = { original:currentMinimum, pixels:parseFloat(getComputedStyle(region).minHeight) || 0, applied:null };
                minimums.set(region, minimum);
              }
              let bottom = 0, positioned = false;
              const bounds = region.getBoundingClientRect();
              for (const block of blocks) {
                keep.add(block);
                const absolute = block.classList.contains('studio-canvas-positioned') || getComputedStyle(block).position === 'absolute';
                if (!absolute) continue;
                positioned = true;
                const rect = block.getBoundingClientRect();
                bottom = Math.max(bottom, rect.bottom - bounds.top - region.clientTop);
              }
              if (positioned && Number.isFinite(bottom)) {
                const baseline = /^\d+(?:\.\d+)?vh$/.test(minimum.original) ? parseFloat(minimum.original) * innerHeight / 100 : minimum.pixels;
                const needed = Math.ceil(Math.max(baseline, bottom, 0)) + 'px';
                if (region.style.minHeight !== needed) { minimum.applied = needed; region.style.minHeight = needed; }
              } else if (minimum.applied !== null) {
                region.style.minHeight = minimum.original; minimum.applied = null;
              }
            }
            for (const element of observed) if (!keep.has(element)) { resize.unobserve(element); observed.delete(element); }
            for (const region of minimums.keys()) if (!keep.has(region)) minimums.delete(region);
            for (const element of keep) if (!observed.has(element)) { observed.add(element); resize.observe(element); }
          }
          const start = () => {
            new MutationObserver(schedule).observe(document.body, { childList:true, subtree:true, characterData:true, attributes:true, attributeFilter:['class','style','src'] });
            document.addEventListener('load', schedule, true);
            document.addEventListener('loadedmetadata', schedule, true);
            window.addEventListener('resize', schedule);
            document.fonts?.ready.then(schedule);
            schedule();
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once:true });
          else start();
        })();
        """;

    private static string EnsureDirectEditorExtentScript(string script)
    {
        if (!script.Contains("/* studio-direct-editor-extents-v1 */", StringComparison.Ordinal))
            script += Environment.NewLine + DirectEditorExtentScript + Environment.NewLine;
        if (!script.Contains(StudioElementEditingRuntime.Marker, StringComparison.Ordinal))
            script += Environment.NewLine + StudioElementEditingRuntime.Script + Environment.NewLine;
        return script;
    }
    /// <summary>An empty editable document and a reusable empty Page, with the complete native element toolset.</summary>
    public static HyperlibWorkProjectDefinition BuildDirectEditingDefinition(string title)
    {
        ValidateTitle(title);
        static HyperlibPageTemplateDefinition Blank() => new()
        {
            DirectEditing = true, BackgroundColor = "#101820", AccentColor = "#65a88e", ContentWidth = "full",
            HeaderPlacement = "none", FooterPlacement = "none", NavigationPlacement = "none", IncludeNavigation = false,
            HeaderStyle = new() { Padding = 0 }, BodyStyle = new() { Padding = 0 }, NavigationStyle = new() { Padding = 0 },
            FooterStyle = new() { Padding = 0 }, ExtrasStyle = new() { Padding = 0 }
        };
        var page = Blank();
        return new()
        {
            PipelineVersion = 2, DirectEditing = true, WorkTitle = title.Trim(), Description = "", SelectedToolsetIds = [],
            LandingTemplate = Blank(), PageTemplate = page,
            PageTemplates = [new() { Id = "page-default", Name = "Content Page", Definition = page }], PageDefinitions = []
        };
    }

    private static string DirectEditingReadme(string title, string workId) => $$"""
        # {{title}}

        This independent Hyperlib Work opens directly in Hyperpage Studio's visual editor.
        The Work landing Page and its reusable Content Page begin empty. No setup wizard or initial Page count is required.

        ## Build and edit your Pages

        Open **Elements** on the right to add text, rich text, images, galleries, audio, video, citations, navigation, and interactive tools.
        Select an object on the actual Page to edit its **Properties**, or type directly into editable text.
        Drag an object's movement handle to place it on the canvas; drag its resize handles to adjust its size.
        Saved positions belong to that Page. Use Properties to return an object to document flow when appropriate.
        Edits use the revision-checked content journal; Undo and Redo remain available.

        Use **New Page** in Contents to add Pages as the Work grows. A Page owns its writing, media, tool values, and source citations.
        Complete page templates and reusable Header, Navigation, Body, and Footer designs remain in the template collection for reuse.
        Existing reusable template assignments and independently authored Page values are preserved.

        ## Files and portability

        - `Content/library.json` in the project library holds the Work hierarchy and Page documents. Keep its `.hyperlib` journal with it.
        - `authoring.template.json` declares the available components, their fields, and optional per-Page canvas geometry.
        - `.studio/pipeline.json` and `.studio/page-templates.json` retain reusable Page foundations and stable identities.
        - `src/Pages` and `Templates/StudioRuntime` contain this Work's owned components and renderer. Imported media belongs to `Media`.
        - `visual-page.project.json` records Studio's source files and layout preferences.

        Keep the entire project library together. Opening it in Studio starts its private local gateway automatically.
        To run that gateway yourself from the library folder:

        ```powershell
        dotnet run --project src/HyperLib.Host/HyperLib.Host.csproj -- --root . --port 54181
        ```

        Open `http://127.0.0.1:54181/studio-authoring/index.html` to edit this private Work. Its stable Work ID is `{{workId}}`.
        Creating, editing, or running the project does not publish anything. Use Studio's Publication tools when you are ready to publish the saved Work to Hyperlib.
        """;

    /// <summary>Upgrades only recognized, private generated support, preserving every Page, recipe and source customization.</summary>
    public async Task<bool> EnsureDirectEditorSupportAsync(string projectRoot, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(projectRoot);
        var library = Path.GetFullPath(Path.Combine(root, "../.."));
        if (!HyperlibWorkPreviewHostService.IsGeneratedLibrary(library)) return false;
        var pipeline = await new HyperlibProjectPipelineService().ReadAsync(root, ct);
        if (pipeline is null) return false;
        var templatePath = HyperlibProjectPipelineService.Guard(root, "authoring.template.json");
        var prior = await File.ReadAllTextAsync(templatePath, ct);
        var template = JsonSerializer.Deserialize<TemplateDefinition>(prior, ContentJson.Options)
            ?? throw new InvalidDataException("The project has no authoring template.");
        HyperlibProjectPipelineService.EnsureDirectEditorFields(template);
        HyperlibProjectPipelineService.EnsureAppearanceFields(template);
        TemplateValidator.ValidateDefinition(template);
        var current = JsonSerializer.Serialize(template, ContentJson.Options);
        // Validate generated support before writing the schema or replacing any owned SDK file.
        ValidateOwnedPipelineUpgrade(root);
        var dynamicPath = HyperlibProjectPipelineService.Guard(root, "Templates/StudioRuntime/HyperLib.Runtime/Components/DynamicPage.razor");
        var alreadyCurrent = (await File.ReadAllTextAsync(dynamicPath, ct)).Replace("\r\n", "\n", StringComparison.Ordinal).Trim()
            == GuidedDynamicPageComponent().Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        foreach (var runtimeRoot in new[] { Path.Combine(library, "src/HyperLib.Runtime"), Path.Combine(root, "Templates/StudioRuntime/HyperLib.Runtime") })
        {
            var script = await File.ReadAllTextAsync(HyperlibProjectPipelineService.Guard(runtimeRoot, "wwwroot/renderer.js"), ct);
            alreadyCurrent &= script.Contains("/* studio-direct-editor-extents-v1 */", StringComparison.Ordinal)
                && script.Contains(StudioElementEditingRuntime.Marker, StringComparison.Ordinal);
        }
        alreadyCurrent &= (await File.ReadAllTextAsync(HyperlibProjectPipelineService.Guard(root, "src/Pages/wwwroot/work-theme.css"), ct))
            .Contains("/* studio-direct-editor-geometry-v4 */", StringComparison.Ordinal);
        if (prior == current && alreadyCurrent) return false;
        var backup = HyperlibProjectPipelineService.Guard(library, ".studio/upgrades/direct-editor-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        await WriteAsync(backup, Path.GetRelativePath(library, templatePath), prior, ct);
        try
        {
            await UpgradeOwnedPipelineAsync(root, ct);
            await HyperlibProjectPipelineService.AtomicWriteAsync(templatePath, current, ct);
        }
        catch
        {
            await HyperlibProjectPipelineService.AtomicWriteAsync(templatePath, prior, CancellationToken.None);
            throw;
        }
        return true;
    }
}
