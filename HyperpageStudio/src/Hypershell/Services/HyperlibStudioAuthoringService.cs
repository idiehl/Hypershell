using System.IO;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

public sealed record HyperlibStudioProject(string Name, string RootPath);
public sealed record HyperlibElementPreset(string Title, BlockDefinition Definition, string? SourceRelativePath);
public sealed record HyperlibTemplateCompatibility(bool Checked, int PageCount, int AppearanceCount, string Message);

public static class HyperlibStudioAuthoringService
{
    public static async Task<(TemplateDefinition Definition, VisualPageUserSourceSnapshot Snapshot)> LoadAsync(string root, string relativePath)
    {
        var snapshot = await new VisualPageUserSourceService().LoadAsync(root, relativePath);
        var definition = HyperlibTemplateEditSession.ParseDefinition(snapshot.Content);
        TemplateValidator.ValidateDefinition(definition);
        return (definition, snapshot);
    }

    public static async Task SaveAsync(string root, VisualPageUserSourceSnapshot snapshot, TemplateDefinition definition)
    {
        TemplateValidator.ValidateDefinition(definition);
        await CheckCompatibilityAsync(root, definition);
        await new VisualPageUserSourceService().SaveAsync(root, snapshot, JsonSerializer.Serialize(definition, ContentJson.Options));
    }

    public static async Task<HyperlibTemplateCompatibility> CheckCompatibilityAsync(string projectRoot, TemplateDefinition definition)
    {
        TemplateValidator.ValidateDefinition(definition);
        var directory = new DirectoryInfo(projectRoot);
        for (var depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
        {
            VisualPageProjectService.RejectReparsePoint(directory.FullName);
            var statePath = VisualPageProjectService.ResolveProjectPath(directory.FullName, "Content/library.json");
            if (!File.Exists(statePath)) continue;
            var state = JsonSerializer.Deserialize<LibraryState>(await File.ReadAllTextAsync(statePath), ContentJson.Options)
                ?? throw new VisualPageValidationException("The library content index is empty.");
            var isLibrary = string.Equals(Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar),
                directory.FullName.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            var work = isLibrary ? null : state.Nodes.FirstOrDefault(node => node.Type == "work"
                && node.ProjectPath?.Replace('\\', '/') == "Works/" + node.Id
                && string.Equals(VisualPageProjectService.ResolveProjectPath(directory.FullName, node.ProjectPath),
                    Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase));
            if (!isLibrary && work is null) continue;
            var pages = state.Pages.Values.Where(page => !isLibrary && page.WorkId == work!.Id).ToList();
            var appearances = state.Nodes.Where(node => isLibrary ? node.WorkId is null : node.WorkId == work!.Id)
                .Select(node => node.Appearance).ToList();
            if (isLibrary) appearances.Add(state.Appearance);
            TemplateValidator.EnsureCompatible(definition, pages, appearances);
            return new(true, pages.Count, appearances.Count,
                $"Compatible with {pages.Count} existing pages and {appearances.Count} appearance settings. Runtime activation still checks the latest content.");
        }
        return new(false, 0, 0, "No library content index is available here. The template is valid; HyperLib checks content when it activates the build.");
    }

    public static IReadOnlyList<HyperlibElementPreset> GetElementPresets(string root, TemplateDefinition definition)
    {
        var declared = definition.Blocks.Select(block => new HyperlibElementPreset(block.Title, HyperlibTemplateEditSession.CloneElement(block), FindComponentSource(root, block.Component)));
        var builtIn = StarterTemplate.Create().Blocks.Select(block => new HyperlibElementPreset(block.Title, block, FindComponentSource(root, block.Component)))
            .Where(preset => preset.SourceRelativePath is not null);
        return declared.Concat(builtIn).DistinctBy(preset => preset.Definition.Component).ToList();
    }

    public static string? FindComponentSource(string root, string component)
    {
        var name = component.Split('.').Last();
        var sourceRoot = VisualPageProjectService.ResolveProjectPath(root, "src/Pages");
        if (!Directory.Exists(sourceRoot)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", options).Take(2000))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.Ordinal)
                || Path.GetExtension(file) is not (".razor" or ".cs")) continue;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            VisualPageProjectService.ResolveProjectPath(root, relative);
            return relative;
        }
        return null;
    }

    public static IReadOnlyList<HyperlibStudioProject> DiscoverProjects(string projectRoot)
    {
        var directory = new DirectoryInfo(projectRoot);
        for(var depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
        {
            VisualPageProjectService.RejectReparsePoint(directory.FullName);
            var statePath = Path.Combine(directory.FullName, "Content", "library.json");
            var manifestPath = Path.Combine(directory.FullName, VisualPageProjectService.ProjectManifestFileName);
            if(!File.Exists(statePath) || !File.Exists(manifestPath)) continue;
            var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(statePath), ContentJson.Options);
            if(state is null) return [];
            var projects = new List<HyperlibStudioProject> { new("HyperLib presentation", directory.FullName) };
            foreach(var work in state.Nodes.Where(n => n.Type == "work"))
            {
                if(work.ProjectPath?.Replace('\\','/') != "Works/" + work.Id) continue;
                var candidate = VisualPageProjectService.ResolveProjectPath(directory.FullName, work.ProjectPath);
                if(File.Exists(Path.Combine(candidate, VisualPageProjectService.ProjectManifestFileName))) projects.Add(new(work.Title, candidate));
            }
            return projects;
        }
        return [];
    }
}
