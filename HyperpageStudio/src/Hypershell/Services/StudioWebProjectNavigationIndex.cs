using System.IO;
using System.Text;
using System.Text.Json;
using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Public route inventory owned by Studio, refreshed with the project manifest when Pages change.</summary>
public static class StudioWebProjectNavigationIndex
{
    public const string RelativePath = "src/Host/wwwroot/assets/studio-pages.json";
    public const string PublicPath = "/assets/studio-pages.json";

    public static bool IsEnabled(VisualPageProjectManifest project)
        => project.SharedJavaScriptPaths?.Any(path => path.EndsWith("/scripts/site-foundation.js", StringComparison.Ordinal)) == true;

    public static string Serialize(VisualPageProjectManifest project) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        pages = project.Pages.Select(page => new
        {
            id = page.PageId,
            name = page.Name,
            route = PublicRoute(project, page)
        })
    }, VisualPageJson.Options);

    public static async Task WriteAsync(string root, VisualPageProjectManifest project, CancellationToken ct = default)
    {
        var path = VisualPageProjectService.ResolveProjectPath(root, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, Serialize(project), new UTF8Encoding(false), ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string PublicRoute(VisualPageProjectManifest project, VisualPageProjectPage page)
    {
        var route = page.Route == "/" ? "/" : "/" + page.Route.Trim('/');
        var html = page.FileType == HyperpagePageFileType.Html
            || page.FileType == HyperpagePageFileType.ProjectDefault && project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.Html;
        return html && route != "/" ? route + "/" : route;
    }
}
