using System.IO;
using System.Text.Json;

namespace Hypershell.Services;

public sealed class StudioProjectFileHelp
{
    public string RelativePath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string EditGuidance { get; init; } = string.Empty;
    public string GettingStarted { get; init; } = string.Empty;
    public string Ownership { get; init; } = "User-owned";
}

public sealed class StudioProjectGuide
{
    public int SchemaVersion { get; init; } = 1;
    public string ProjectKind { get; init; } = string.Empty;
    public List<StudioProjectFileHelp> Files { get; init; } = [];
}

/// <summary>Project-specific help, kept with the project so it remains useful outside Studio.</summary>
public sealed class StudioProjectFileHelpService
{
    public const string GuideRelativePath = ".studio/project-guide.json";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public StudioProjectFileHelp? GetHelp(string projectRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(relativePath))
            return null;
        var root = Path.GetFullPath(projectRoot);
        var requested = relativePath.Replace('\\', '/');
        if (requested.Equals("canvas", StringComparison.OrdinalIgnoreCase))
            return new StudioProjectFileHelp
            {
                RelativePath = "canvas", Title = "Page canvas", Ownership = "Visual authoring",
                Purpose = "Compose the active page visually. Its elements and page settings are saved in the page's visual document.",
                EditGuidance = "Select an element to edit its text, layout, links, and appearance. Add objects from Elements or Templates; generated markup follows the visual document.",
                GettingStarted = "Start with the page heading and introduction. Save the page, then use Preview to check the actual browser output."
            };
        try
        {
            var guidePath = VisualPageProjectService.ResolveProjectPath(root, GuideRelativePath);
            var info = new FileInfo(guidePath);
            if (info.Exists && info.Length <= 4 * 1024 * 1024)
            {
                var guide = JsonSerializer.Deserialize<StudioProjectGuide>(File.ReadAllText(guidePath), JsonOptions);
                var exact = guide?.Files.FirstOrDefault(item => item.RelativePath.Replace('\\', '/').Equals(requested, StringComparison.OrdinalIgnoreCase));
                if (exact is not null) return exact;
                if (requested is "razor" or "html" or "cshtml" or "generated-razor" or "generated-html")
                {
                    var generated = guide?.Files.FirstOrDefault(item => item.Ownership.StartsWith("Generated", StringComparison.OrdinalIgnoreCase)
                        && Path.GetExtension(item.RelativePath) is ".razor" or ".cshtml" or ".html");
                    if (generated is not null) return generated;
                }
            }
            var resolved = VisualPageProjectService.ResolveProjectPath(root, requested);
            return File.Exists(resolved) ? DescribeFile(requested) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or VisualPageValidationException or ArgumentException)
        {
            // An invalid optional guide must never make the source editor unusable.
            try
            {
                return File.Exists(VisualPageProjectService.ResolveProjectPath(root, requested)) ? DescribeFile(requested) : null;
            }
            catch (Exception fallback) when (fallback is IOException or UnauthorizedAccessException or VisualPageValidationException or ArgumentException)
            {
                return null;
            }
        }
    }

    public static StudioProjectFileHelp DescribeFile(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var name = Path.GetFileName(path);
        StudioProjectFileHelp Help(string title, string purpose, string guidance, string start, string ownership = "User-owned") => new()
        { RelativePath = path, Title = title, Purpose = purpose, EditGuidance = guidance, GettingStarted = start, Ownership = ownership };
        if (path == VisualPageProjectService.ProjectManifestFileName)
            return Help("Project map", "Records project identity, project type, page routes, theme, assets, and generated-file ownership.", "Studio maintains this manifest. Change page identity and routes in the page inspector; keep the manifest with your project.", "Open README.md for the project map, then edit a page in Canvas.", "Studio-managed");
        if (path.EndsWith(".visual-page.json", StringComparison.OrdinalIgnoreCase))
            return Help("Visual page source", "The authoritative editable element tree and page metadata used to generate this page's HTML or Razor markup.", "Use Canvas and the inspector to compose elements. If editing JSON outside Studio, keep descriptor IDs and revisions valid and reload the project before saving.", "Find the page's heading and introductory elements in Canvas, then save and preview.", "Visual authoring");
        if (path == GuideRelativePath)
            return Help("Project file guide", "Supplies the project-specific explanations displayed by the ? button next to an open file.", "Update the matching files entry when you introduce a new feature or document an additional file.", "Copy an existing entry and explain purpose, ownership, edit guidance, and the first useful step.");
        if (name == "README.md")
            return Help("Start here", "Explains how to run, customize, extend, and publish this exact starter, with a map of every source file.", "Keep setup and deployment notes current as your project grows.", "Follow Quick start, then the ordered customization steps.");
        if (path.EndsWith(".custom.css", StringComparison.OrdinalIgnoreCase))
            return Help("Page styles", "User-owned CSS loaded after the generated styles for this page. This is the safest place for page-specific styling.", "Change the :root design tokens, then adjust the named starter classes. Canvas saves preserve this file.", "Change --studio-accent, save, and preview; use the responsive section near the bottom to adjust narrow layouts.");
        if (path.EndsWith(".generated.css", StringComparison.OrdinalIgnoreCase) || name == "hyperpage-theme.css")
            return Help("Generated page styles", "CSS emitted from the visual document's styles, layout, and theme.", "Change visual styles in Canvas or add overrides to the matching .custom.css. Regeneration replaces this file.", "Open the matching .custom.css to make a persistent styling change.", "Generated — do not edit directly");
        if (path.EndsWith(".custom.js", StringComparison.OrdinalIgnoreCase))
            return Help("Page behavior", "Browser JavaScript for mobile navigation, theme preference, and the local interactive planner. It runs after the page loads.", "Edit this user-owned file to extend behavior. The planner stores data on this browser and does not transmit submissions.", "Find the named event handlers and local-storage key near the top; replace the demo planner with your feature.");
        if (path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return Help("Generated page markup", "The browser page generated from its visual page source. Its route is defined by the corresponding page entry.", "Compose structure and text in Canvas, style it in .custom.css, and add behavior in .custom.js. Direct edits here would be replaced by the next visual save.", "Open Canvas to change the page, or the matching .custom.css and .custom.js for code customization.", "Generated — do not edit directly");
        if (name == "Program.cs")
            return Help("Web host entry point", "Configures the HTTP server, static assets, routes, and health check. Razor starters also register server-rendered Razor Pages.", "Add application services and server endpoints here. Keep loopback hosting for local development; configure production separately.", "Run dotnet run --project src/Host/Host.csproj --urls http://127.0.0.1:5180, then open the printed address.");
        if (name == "Host.csproj")
            return Help("Web application project", "Defines the .NET 10 ASP.NET Core host. The starter uses the shared framework and needs no third-party runtime package.", "Add packages only for features you use. This is the project passed to dotnet build, run, and publish.", "Run dotnet build src/Host/Host.csproj to verify a change.");
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return Help("Local illustration", "A self-contained SVG image shipped with the starter; it needs no network service, account, or image download.", "Replace the image with your own local asset and update the Image element's URL and meaningful alternative text in Canvas.", "Use the page's Image element to set the replacement asset and its alt text.");
        if (name == "app.css" || name == "theme.css")
            return Help("Base theme", "Shared layout reset, color tokens, and typography used throughout the project.", "Put custom application styling in the page's .custom.css; use this file for deliberate project-wide foundations.", "Start with the page's .custom.css, where the starter's detailed styles are grouped by component.");
        if (path.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
            return Help("Razor page server logic", "A user-owned PageModel or supporting C# file for server-side page behavior.", "Keep HTTP handlers, validation, and server-only services here. Generated Canvas pages use Layout = null and do not automatically bind a PageModel; add @model only through a deliberately user-owned page.", "Read the commented PageModel example, then add a user-owned .cshtml page if server-rendered form handlers are required.");
        if (name == ".gitignore")
            return Help("Version-control exclusions", "Keeps compiled outputs, temporary Studio recovery/cache files, and private environment settings out of source control.", "Add only generated or local-only paths; retain visual documents and user-owned source files.", "Initialize Git in the project root, inspect the pending files, then make a baseline commit.");
        return Help(name, $"A project file at {path}.", "This file has no specialized guide entry yet. Check README.md and its references before changing it; add an entry to .studio/project-guide.json to document its role.", "Search for this file name in the project to locate its callers and references.");
    }

    internal static async Task WriteGuideAsync(string root, string kind, CancellationToken ct)
    {
        var paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith(".visual-page/", StringComparison.OrdinalIgnoreCase))
            .Append(GuideRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var guide = new StudioProjectGuide { ProjectKind = kind, Files = paths.Select(DescribeFile).ToList() };
        var destination = VisualPageProjectService.ResolveProjectPath(root, GuideRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(guide, JsonOptions), ct);
    }
}
