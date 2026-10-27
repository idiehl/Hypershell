using System.IO;
using System.Security.Cryptography;
using System.Text;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed partial class StudioProjectScaffoldService
{
    private static void NormalizeTemplateIdentities(StudioWebProjectBlueprint blueprint)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var template in blueprint.ReusableTemplates.Concat(blueprint.Pages.Select(page => page.Template!))
            .Distinct<VisualPageTemplateDocument>(ReferenceEqualityComparer.Instance))
        {
            if (!ids.TryGetValue(template.TemplateId, out var id))
                ids[template.TemplateId] = id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("studio-project-template:" + template.TemplateId)))[..32];
            template.TemplateId = id;
            template.StorageScope = "project";
            template.Library = "This project";
        }
    }

    private async Task WriteBlueprintAssetsAsync(string root, VisualPageProjectManifest project,
        StudioWebProjectBlueprint blueprint, CancellationToken ct)
    {
        project.SharedStylesheetPaths = ["src/Host/wwwroot/styles/site-foundation.css"];
        project.SharedJavaScriptPaths = ["src/Host/wwwroot/scripts/site-foundation.js"];
        await WriteAsync(root, project.SharedStylesheetPaths[0], blueprint.Css, ct);
        await WriteAsync(root, project.SharedJavaScriptPaths[0], blueprint.JavaScript, ct);
        long copiedBytes = 0;
        foreach (var asset in blueprint.AssetSources)
        {
            ct.ThrowIfCancellationRequested();
            var source = new FileInfo(asset.Value);
            VisualPageProjectService.RejectReparsePoint(source.FullName);
            if (!source.Exists || source.Length > 64L * 1024 * 1024 || (copiedBytes += source.Length) > 256L * 1024 * 1024)
                throw new VisualPageValidationException("A template asset is missing or exceeds the supported project package size.");
            var relative = BlueprintAssetPath(asset.Key);
            var destination = VisualPageProjectService.ResolveProjectPath(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = source.OpenRead())
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, ct);
            RegisterBlueprintAsset(project, root, relative);
        }
        foreach (var asset in blueprint.TextAssets)
        {
            var relative = BlueprintAssetPath(asset.Key);
            await WriteAsync(root, relative, asset.Value, ct);
            RegisterBlueprintAsset(project, root, relative);
        }
        var templates = new VisualPageTemplateService(_catalog);
        foreach (var template in blueprint.ReusableTemplates.DistinctBy(item => item.TemplateId))
        {
            template.SuggestedPageRoute = "";
            template.SuggestedPageName = template.Name;
            var relative = "Templates/" + VisualPageProjectService.MakeSlug(template.Name) + "-" + template.TemplateId[..8] + ".visual-page-template.json";
            await templates.SaveAsync(VisualPageProjectService.ResolveProjectPath(root, relative), template, ct);
            project.Templates.Add(new() { TemplateId = template.TemplateId, Name = template.Name, RelativePath = relative,
                Scope = "project", AssetIds = [], Dependencies = [] });
        }
        await StudioWebProjectNavigationIndex.WriteAsync(root, project, ct);
    }

    private static string BlueprintAssetPath(string key)
    {
        var path = key.Replace('\\', '/');
        if (!(path.StartsWith("assets/", StringComparison.Ordinal) || path.StartsWith("media/", StringComparison.Ordinal))
            || path.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new VisualPageValidationException("Template assets must use safe local asset paths.");
        return "src/Host/wwwroot/" + path;
    }

    private static void RegisterBlueprintAsset(VisualPageProjectManifest project, string root, string relative)
    {
        if (project.Assets.Any(item => item.RelativePath == relative)) return;
        var name = Path.GetFileNameWithoutExtension(relative);
        project.Assets.Add(new() { Name = name[..Math.Min(120, name.Length)], RelativePath = relative,
            MediaType = Path.GetExtension(relative).ToLowerInvariant() switch {
                ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif", ".webp" => "image/webp", ".mp4" => "video/mp4", ".webm" => "video/webm",
                _ => "application/octet-stream" }, Sha256 = VisualPageHashing.Sha256File(VisualPageProjectService.ResolveProjectPath(root, relative)) });
    }

    private static string BlueprintReadme(string name, StudioProjectKind kind, VisualPageProjectManifest project) => $$"""
        # {{name}}

        This is an editable {{(kind == StudioProjectKind.Razor ? "ASP.NET Core Razor Pages" : "HTML, CSS, and JavaScript")}} project created by Hyperpage Studio. Begin directly on the Canvas, using an empty page or your selected complete design. Existing reusable page layouts remain available for adding more Pages.

        ## Begin editing

        Open this folder in Hyperpage Studio. The upper tab holds the project; its Canvas and open files stay in the lower tabs. Select a Page and edit its text, images, layout, and styles. Use the Templates library when adding more Pages: this project owns a copy of each chosen reusable Page layout in `Templates/`. Each Page receives independent content. You can edit and save those local templates without changing the original template collection.

        ## Run the website

        Install the .NET 10 SDK for the local development host, then run:

        ```powershell
        dotnet run --project src/Host/Host.csproj
        ```

        Open the localhost address printed by the host. Studio's Run control uses its isolated preview host. {{(kind == StudioProjectKind.Html ? "To publish as a static website, deploy the contents of src/Host/wwwroot to a static web host. The .NET host is only needed for local development." : "To deploy the Razor Pages app, run dotnet publish src/Host/Host.csproj -c Release and deploy the published ASP.NET Core application.")}}

        ## Files and ownership

        - `Pages/*.visual-page.json` stores editable Page structure and content. Studio regenerates the corresponding {{(kind == StudioProjectKind.Razor ? ".cshtml" : ".html")}} and `.generated.css` files when you save.
        - `Templates/*.visual-page-template.json` stores the local reusable Page layouts. Choose these when creating another Page.
        - `src/Host/wwwroot/styles/site-foundation.css` contains shared backgrounds, layout refinements, and navigation styles. Edit it to change the site-wide presentation.
        - `src/Host/wwwroot/scripts/site-foundation.js` supplies browser navigation behavior. The managed `assets/studio-pages.json` index follows the project's current Page order when Pages are added, removed, or renamed; custom links remain explicit choices.
        - `src/Host/wwwroot/assets/*.custom.css` and `*.custom.js` are user-owned Page-specific styles and scripts. Studio preserves them when saving the Canvas.
        - Imported images and other media are copied under `src/Host/wwwroot/assets` or `media`, so the website does not depend on the original library.
        - `src/Host/Program.cs` and `Host.csproj` are user-owned server files. The `?` control beside an open file explains its role.

        ## Next steps

        Replace sample copy and placeholder images with your content. Add Pages from the saved layouts, check their routes, and inspect the site at desktop and narrow widths. Test keyboard navigation, image descriptions, and links from the bottom of a Page. Set up real form submissions or storage explicitly if the site needs them; a template's form is a starting layout. Hyperlib runtime components are available in Hyperlib projects; browser projects use the compatible native HTML template objects rather than silently freezing server tools.

        {{string.Join('\n', project.Pages.Select(page => "- " + page.Name + ": `" + page.Route + "`"))}}
        """;
}
