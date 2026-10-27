using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

/// <summary>Explicit page stylesheet references and the design canvas's local-only static origin.</summary>
public static class HyperpageStylesheetService
{
    public const string CanvasOrigin = "https://hyperpage-canvas.invalid";
    public const int MaximumCanvasResourceBytes = 8 * 1024 * 1024;

    public static string NormalizeStylesheetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new VisualPageValidationException("A stylesheet reference needs a project-relative path.");
        }
        var path = relativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            || !path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase)
            || !(path.StartsWith("src/Host/wwwroot/", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("src/Pages/wwwroot/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new VisualPageValidationException(
                "Choose a plain .css file inside src/Host/wwwroot or src/Pages/wwwroot. Collocated .razor.css uses build-time isolation.");
        }
        return path;
    }

    public static string PublishedUrl(VisualPageProjectManifest project, string relativePath)
    {
        var path = NormalizeStylesheetPath(relativePath);
        const string host = "src/Host/wwwroot/";
        const string pages = "src/Pages/wwwroot/";
        if (VisualPageProjectService.ResolvePageFileType(project, HyperpagePageFileType.ProjectDefault) != HyperpagePageFileType.RazorPage
            && path.StartsWith(pages, StringComparison.OrdinalIgnoreCase))
        {
            throw new VisualPageValidationException(
                "This project publishes CSS from src/Host/wwwroot. Choose a stylesheet in the web host's folder.");
        }
        var url = path.StartsWith(host, StringComparison.OrdinalIgnoreCase)
            ? "/" + path[host.Length..]
            : "/_content/" + project.RootNamespace + "/" + path[pages.Length..];
        return string.Join('/', url.Split('/').Select(Uri.EscapeDataString));
    }

    public static bool TryResolveCanvasPath(
        VisualPageProjectManifest project,
        Uri uri,
        out string relativePath,
        out string contentType)
    {
        relativePath = contentType = string.Empty;
        if (!uri.IsAbsoluteUri
            || !uri.GetLeftPart(UriPartial.Authority).Equals(CanvasOrigin, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || Uri.UnescapeDataString(uri.OriginalString).Split('/', '\\').Any(segment => segment is "." or ".."))
        {
            return false;
        }
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (path.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return false;
        }
        contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            _ => string.Empty
        };
        if (contentType.Length == 0)
        {
            return false;
        }
        var libraryPrefix = "_content/" + project.RootNamespace + "/";
        if (path.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase)
            && VisualPageProjectService.ResolvePageFileType(project, HyperpagePageFileType.ProjectDefault) != HyperpagePageFileType.RazorPage)
        {
            return false;
        }
        relativePath = path.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase)
            ? "src/Pages/wwwroot/" + path[libraryPrefix.Length..]
            : "src/Host/wwwroot/" + path;
        return true;
    }
}
