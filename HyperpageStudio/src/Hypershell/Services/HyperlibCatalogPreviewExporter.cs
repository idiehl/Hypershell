using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HyperLib.Core;

namespace Hypershell.Services;

/// <summary>Freezes genuine Razor previews into portable, sandboxed documentation assets.</summary>
public sealed class HyperlibCatalogPreviewExporter(string cacheRoot) : IDisposable
{
    private readonly HyperlibTemplateObjectPreviewService _previews = new(cacheRoot);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    public async Task ExportAsync(string root, TemplateDesignGroup group, TemplateLibraryObject item, string outputDirectory, CancellationToken ct)
    {
        var preview = await _previews.PreviewAsync(root, group.Id, item.Id, ct);
        var html = await _http.GetStringAsync(preview.Url, ct);
        var documentBase = new Uri(preview.Url, "/");
        html = Regex.Replace(html, "<base\\s+href=['\"]/['\"]\\s*/?>", "", RegexOptions.IgnoreCase);
        if (!html.Contains("data-object-preview", StringComparison.Ordinal) || html.Contains("data-preview-error", StringComparison.Ordinal))
            throw new InvalidDataException("A genuine object preview was not produced for " + item.CatalogId);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "assets"));
        var assets = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalBytes = 0;
        async Task<string> CopyAsset(Uri address)
        {
            if (address.Scheme != preview.Url.Scheme || address.Authority != preview.Url.Authority)
                throw new InvalidDataException("Catalog preview assets must come from their isolated local renderer.");
            if (assets.TryGetValue(address.AbsoluteUri, out var existing)) return existing;
            if (assets.Count >= 256) throw new InvalidDataException("Too many assets in one preview.");
            var extension = Path.GetExtension(address.AbsolutePath).ToLowerInvariant();
            if (!new[] { ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".woff", ".woff2", ".ttf", ".ico", ".avif", ".wav", ".mp3", ".ogg", ".m4a", ".webm", ".mp4" }.Contains(extension))
                throw new InvalidDataException("Unsupported documentation preview asset: " + address.AbsolutePath);
            var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsolutePath)))[..20] + extension;
            assets[address.AbsoluteUri] = name;
            using var response = await _http.GetAsync(address, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidDataException($"Preview asset {address.AbsolutePath} returned {(int)response.StatusCode} for {item.CatalogId}.");
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            totalBytes += bytes.Length;
            if (totalBytes > 48 * 1024 * 1024) throw new InvalidDataException("Preview assets exceed 48 MiB.");
            if (extension == ".css")
            {
                var css = Encoding.UTF8.GetString(bytes);
                foreach (Match match in Regex.Matches(css, "(?:url\\(\\s*['\"]?(?<url>[^)'\"\\s]+)['\"]?\\s*\\)|@import\\s+['\"](?<url>[^'\"]+)['\"])").Cast<Match>().Reverse())
                {
                    var value = match.Groups["url"];
                    if (value.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || value.Value.StartsWith('#')) continue;
                    var target = new Uri(address, WebUtility.HtmlDecode(value.Value));
                    var replacement = await CopyAsset(target);
                    css = css[..value.Index] + replacement + css[(value.Index + value.Length)..];
                }
                bytes = Encoding.UTF8.GetBytes(css);
            }
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "assets", name), bytes, ct);
            return name;
        }
        foreach (Match match in Regex.Matches(html, "(?<attribute>href|src|poster)=(?<quote>['\"])(?<url>[^'\"]+)\\k<quote>", RegexOptions.IgnoreCase).Cast<Match>().Reverse())
        {
            var value = match.Groups["url"];
            if (value.Value.StartsWith('#') || value.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var address = new Uri(documentBase, WebUtility.HtmlDecode(value.Value));
            // Ordinary links remain links; asset references are frozen locally.
            var isAsset = match.Groups["attribute"].Value != "href" || address.AbsolutePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
            if (!isAsset) continue;
            if (address.Authority != preview.Url.Authority)
                continue; // The preview CSP intentionally blocks external media instead of downloading it.
            var replacement = "assets/" + await CopyAsset(address);
            html = html[..value.Index] + replacement + html[(value.Index + value.Length)..];
        }
        var policy = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; media-src 'self'; font-src 'self'; script-src 'none'; connect-src 'none'; form-action 'none'; base-uri 'none'\">";
        html = html.Replace("<head>", "<head>" + policy, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "index.html"), html, ct);
    }
    public void Dispose() { _previews.Dispose(); _http.Dispose(); }
}
