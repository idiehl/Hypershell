using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

/// <summary>Keeps collected objects in an editor draft until the author saves the Template.</summary>
public static class HyperlibTemplateObjectAuthoringService
{
    public static async Task<VisualPageUserSourceSnapshot> CommitAsync(string projectRoot,
        VisualPageUserSourceSnapshot snapshot, TemplateDefinition definition,
        IReadOnlyList<TemplateImportPlan> plans, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (JsonSerializer.Serialize(definition, ContentJson.Options).Length > VisualPageUserSourceService.MaximumUserSourceCharacters)
            throw new VisualPageValidationException("This collection exceeds the Template editor's 2 MB definition limit. Split it into Design Groups.");
        if (plans.Count == 0)
        {
            await HyperlibStudioAuthoringService.SaveAsync(projectRoot, snapshot, definition);
            return snapshot;
        }

        if (!string.Equals(snapshot.RelativePath.Replace('\\', '/'), "authoring.template.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Open a Template or Work to add objects from the Core Library.");
        var sources = new VisualPageUserSourceService();
        var current = await sources.LoadAsync(projectRoot, snapshot.RelativePath);
        if (!string.Equals(current.Sha256, snapshot.Sha256, StringComparison.Ordinal))
            throw new VisualPageUserSourceDriftException(snapshot.RelativePath, snapshot.Sha256, current.Sha256);

        await HyperlibStudioAuthoringService.CheckCompatibilityAsync(projectRoot, definition);
        var path = VisualPageProjectService.ResolveProjectPath(projectRoot, snapshot.RelativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        // Check text again after compatibility reads, then let the Core transaction guard
        // these exact bytes and every captured dependency before publishing the schema.
        current = await sources.LoadAsync(projectRoot, snapshot.RelativePath);
        if (!string.Equals(current.Sha256, snapshot.Sha256, StringComparison.Ordinal))
            throw new VisualPageUserSourceDriftException(snapshot.RelativePath, snapshot.Sha256, current.Sha256);
        await TemplateObjectImporter.CommitAsync(projectRoot, definition, plans,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), cancellationToken);
        return await sources.LoadAsync(projectRoot, snapshot.RelativePath);
    }

    public static string? FindLibraryRoot(string projectRoot)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(projectRoot));
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            VisualPageProjectService.RejectReparsePoint(directory.FullName);
            if (File.Exists(Path.Combine(directory.FullName, "Content", "library.json"))
                && Directory.Exists(Path.Combine(directory.FullName, "Starters", "standard")))
                return directory.FullName;
        }
        return null;
    }
}
