using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using HyperLib.Core;

namespace HyperLib.Core;

public static class TemplateProjectScaffolder
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly HashSet<string> NonSourceDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "Content", "Library", "Media", "Works", "BaseTemplates", "Templates", "Starters", "app", "artifacts", "output", "dist" };
    private static readonly HashSet<string> CacheDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "bin", "obj", ".git", ".hyperlib", ".visual-page", ".hypershell", ".vs", "node_modules" };

    public static async Task EnsureAsync(string libraryRoot, string projectRoot, string packageId, string title, CancellationToken ct = default, string? starterId = null, bool exportingCatalog = false)
    {
        libraryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        projectRoot = TemplatePathGuard.Within(libraryRoot, projectRoot);
        TemplateValidator.ValidateId(packageId, "package");
        var gate = Gates.GetOrAdd(projectRoot, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var pathIdentity = OperatingSystem.IsWindows() ? projectRoot.ToUpperInvariant() : projectRoot;
            var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pathIdentity)));
            var lockPath = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, ".hyperlib", "scaffolding", key + ".lock"));
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            var elapsed = Stopwatch.StartNew();
            FileStream? projectLock = null;
            while (projectLock is null)
            {
                ct.ThrowIfCancellationRequested();
                TemplatePathGuard.NoLinks(lockPath);
                try { projectLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30)) { await Task.Delay(40, ct); }
            }
            await using (projectLock)
                await EnsureCoreAsync(libraryRoot, projectRoot, packageId, title, ct, starterId, exportingCatalog);
        }
        finally { gate.Release(); }
    }

    private static async Task EnsureCoreAsync(string libraryRoot, string projectRoot, string packageId, string title, CancellationToken ct, string? starterId, bool exportingCatalog)
    {
        TemplatePathGuard.Within(libraryRoot, projectRoot);
        var templatePath = TemplatePathGuard.Within(libraryRoot, Path.Combine(projectRoot, packageId == "library" ? "Presentation/authoring.template.json" : "authoring.template.json"));
        var manifestPath = TemplatePathGuard.Within(libraryRoot, Path.Combine(projectRoot, "visual-page.project.json"));
        // Once complete, a Work owns its source. Later template changes must not fill, replace, or resurrect files.
        if (File.Exists(manifestPath) && File.Exists(templatePath)) return;
        var starter = Path.Combine(libraryRoot, "Starters", "standard");
        if (starterId is not null)
        {
            _ = ReusableTemplateCatalog.Find(libraryRoot, starterId);
            var catalogProject = ReusableTemplateCatalog.GetProjectRoot(libraryRoot, starterId);
            if (!exportingCatalog && Directory.Exists(catalogProject)) starter = catalogProject;
        }
        if (!Directory.Exists(starter)) throw new DirectoryNotFoundException("The HyperLib standard starter is missing.");
        Directory.CreateDirectory(projectRoot);
        await CopyProjectSourceAsync(libraryRoot, starter, projectRoot, ct);
        if (WorkLandingTemplateCatalog.Contains(starterId))
        {
            await WriteNewAsync(Path.Combine(projectRoot, "src/Pages/Blocks/WorkLandingBlock.razor"), WorkLandingTemplateCatalog.ComponentSource, ct);
            await WriteNewAsync(Path.Combine(projectRoot, "src/Pages/Blocks/WorkLandingBlock.razor.css"), WorkLandingTemplateCatalog.ComponentCss, ct);
        }

        if (!File.Exists(templatePath))
        {
            var template = starterId is null ? StarterTemplate.Create()
                : exportingCatalog ? ReusableTemplateCatalog.CreateBuiltInDefinition(starterId)
                : ReusableTemplateCatalog.ReadDefinition(libraryRoot, starterId);
            if (packageId == "library")
            {
                template.Title = "HyperLib presentation";
                template.Appearance.Add(new() { Id = "subtitle", Title = "Introduction", Type = "text", DefaultValue = ContentJson.Value("Collect ideas, follow connections, and make something of your own.") });
                if (!template.Appearance.Any(f => f.Id == "font")) template.Appearance.Add(new() { Id = "font", Title = "Typography", Type = "select", Options = ["Georgia, serif", "system-ui, sans-serif"], DefaultValue = ContentJson.Value("Georgia, serif") });
            }
            await WriteNewAsync(templatePath, JsonSerializer.Serialize(template, ContentJson.Options), ct);
        }
        if (File.Exists(manifestPath)) return;
        if (!exportingCatalog && File.Exists(Path.Combine(starter, "visual-page.project.json")))
        {
            await CopyProjectManifestAsync(libraryRoot, starter, projectRoot, packageId, title, ct);
            return;
        }

        var projectId = Guid.NewGuid().ToString("N");
        var pageId = Guid.NewGuid().ToString("N");
        var rootId = Guid.NewGuid().ToString("N");
        var files = new Dictionary<string, string>
        {
            ["src/Pages/Generated/WorkPage.razor"] = "@namespace HyperLib.Work.Generated\n@page \"/presentation\"\n<div class=\"hyperlib-studio-shell\"></div>\n",
            ["src/Pages/Generated/WorkPage.razor.css"] = ".hyperlib-studio-shell { opacity:.6; }\n",
            ["src/Pages/Generated/WorkPage.designer.g.cs"] = "namespace HyperLib.Work.Generated; public partial class WorkPage { }\n",
            ["src/Pages/wwwroot/visual-page-theme.css"] = ":root { --accent: #65a88e; }\n"
        };
        foreach (var file in files) await WriteNewAsync(Path.Combine(projectRoot, file.Key), file.Value, ct);
        await WriteNewAsync(Path.Combine(projectRoot, "src/Pages/Generated/WorkPage.cs"), "// User-owned partial. Hyperpage Studio preserves this file.\nnamespace HyperLib.Work.Generated; public partial class WorkPage { }\n", ct);
        await WriteNewAsync(Path.Combine(projectRoot, "src/Pages/wwwroot/WorkPage.custom.css"), "/* Work-specific visual customization. */\n", ct);
        await WriteNewAsync(Path.Combine(projectRoot, "Shared/theme.css"), "/* Shared theme. */\n", ct);
        var page = new
        {
            schemaVersion = 1, projectId, pageId, name = "Presentation", componentName = "WorkPage", route = "/presentation", slug = "presentation",
            fileType = "RazorPage", title, description = "Studio-owned presentation shell. Author page content in HyperLib.", revision = "", generatorVersion = "1.0.0",
            root = new { elementId = rootId, descriptorId = "core.page", name = "Page shell", children = Array.Empty<object>() },
            generatedFiles = files.Select(f => new { relativePath = f.Key, sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(f.Value))) }).ToArray()
        };
        await WriteNewAsync(Path.Combine(projectRoot, "Pages/presentation.visual-page.json"), JsonSerializer.Serialize(page, ContentJson.Options), ct);
        var manifest = new
        {
            schemaVersion = 1, projectId, name = title, rootNamespace = "HyperLib.Work", generatorVersion = "1.0.0", revision = "",
            razorProjectPath = "src/Pages/Pages.csproj", localHostProjectPath = "src/Host/Host.csproj",
            foundation = new { markupLanguage = "RazorPages", hostingTarget = "Local", siteStructure = "FixedPages", plannedPageCount = 1, usesTemplates = true, codingMode = "Manual", aiProvider = "None" },
            pages = new[] { new { pageId, name = "Presentation", route = "/presentation", slug = "presentation", fileType = "RazorPage", documentPath = "Pages/presentation.visual-page.json", componentPath = "src/Pages/Generated/WorkPage.razor", revision = "" } },
            theme = new { tokens = new Dictionary<string,string>(), additionalCssPath = "Shared/theme.css" },
            hyperlibPackageId = packageId,
            hyperlibAuthoringPath = packageId == "library" ? "Presentation/authoring.template.json" : "authoring.template.json"
        };
        await WriteNewAsync(Path.Combine(projectRoot, "visual-page.project.json"), JsonSerializer.Serialize(manifest, ContentJson.Options), ct);
    }

    private static async Task WriteNewAsync(string path, string contents, CancellationToken ct)
        => await TemplatePathGuard.WriteNewAsync(path, Encoding.UTF8.GetBytes(contents), ct);

    internal static async Task CopyProjectSourceAsync(string libraryRoot, string starter, string projectRoot, CancellationToken ct)
    {
        foreach (var source in EnumerateProjectSources(starter, starter))
        {
            var relative = Path.GetRelativePath(starter, source);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase))) continue;
            if (!IsProjectSource(relative)) continue;
            var target = Path.Combine(projectRoot, relative);
            TemplatePathGuard.Within(libraryRoot, target);
            if (File.Exists(target)) continue;
            if (!Path.GetExtension(target).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await TemplatePathGuard.WriteNewAsync(target, await File.ReadAllBytesAsync(source, ct), ct);
                continue;
            }
            var text = await File.ReadAllTextAsync(source, ct);
            if (text.Contains("__SDK_REF__", StringComparison.Ordinal))
                text = text.Replace("__SDK_REF__", Path.GetRelativePath(Path.GetDirectoryName(target)!, Path.Combine(libraryRoot, "src", "HyperLib.Runtime", "HyperLib.Runtime.csproj")).Replace('\\', '/'), StringComparison.Ordinal);
            if (Path.GetExtension(target).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var xml = System.Xml.Linq.XDocument.Parse(text);
                foreach (var reference in xml.Descendants("ProjectReference"))
                    if (reference.Attribute("Include")?.Value.EndsWith("HyperLib.Runtime.csproj", StringComparison.Ordinal) == true)
                        reference.SetAttributeValue("Include", Path.GetRelativePath(Path.GetDirectoryName(target)!, Path.Combine(libraryRoot, "src", "HyperLib.Runtime", "HyperLib.Runtime.csproj")).Replace('\\', '/'));
                text = xml.ToString();
            }
            await WriteNewAsync(target, text, ct);
        }

    }

    internal static bool IsProjectSource(string relative)
    {
        var parts = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || NonSourceDirectories.Contains(parts[0]) || parts.Any(CacheDirectories.Contains)) return false;
        if (parts.Length > 1) return true;
        return parts[0] is not ("visual-page.project.json" or "authoring.template.json" or "hyperlib.app.json" or "template.json");
    }

    private static IEnumerable<string> EnumerateProjectSources(string root, string directory)
    {
        TemplatePathGuard.NoLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (!IsProjectSource(Path.GetRelativePath(root, entry))) continue;
            TemplatePathGuard.NoLinks(entry);
            if (Directory.Exists(entry))
            {
                foreach (var file in EnumerateProjectSources(root, entry)) yield return file;
            }
            else yield return entry;
        }
    }

    internal static async Task CopyProjectManifestAsync(string libraryRoot, string sourceRoot, string destinationRoot,
        string packageId, string title, CancellationToken ct)
    {
        var sourceManifest = TemplatePathGuard.Within(libraryRoot, Path.Combine(sourceRoot, "visual-page.project.json"));
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(sourceManifest, ct))?.AsObject()
            ?? throw new ContentValidationException("The source project manifest is empty.");
        var projectId = Guid.NewGuid().ToString("N");
        manifest["projectId"] = projectId;
        manifest["name"] = title;
        manifest["revision"] = "";
        manifest["hyperlibPackageId"] = packageId;
        manifest["hyperlibAuthoringPath"] = "authoring.template.json";
        if (manifest["pages"] is JsonArray pages)
            foreach (var page in pages)
            {
                if (page is not JsonObject entry || entry["documentPath"]?.GetValue<string>() is not { } relative) continue;
                var path = new ContentStore(destinationRoot).ResolvePath(relative);
                var document = JsonNode.Parse(await File.ReadAllTextAsync(path, ct))?.AsObject()
                    ?? throw new ContentValidationException("A source page document is empty.");
                var pageId = Guid.NewGuid().ToString("N");
                entry["pageId"] = pageId;
                entry["revision"] = "";
                document["projectId"] = projectId;
                document["pageId"] = pageId;
                document["revision"] = "";
                await TemplatePathGuard.ReplaceAsync(path, Encoding.UTF8.GetBytes(document.ToJsonString(ContentJson.Options)), ct);
            }
        await WriteNewAsync(Path.Combine(destinationRoot, "visual-page.project.json"), manifest.ToJsonString(ContentJson.Options), ct);
    }
}
