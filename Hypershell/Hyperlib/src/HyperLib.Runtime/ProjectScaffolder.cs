using HyperLib.Core;

namespace HyperLib.Runtime;

/// <summary>Runtime compatibility entry point for the shared local project scaffolder.</summary>
public static class ProjectScaffolder
{
    public static Task EnsureAsync(string libraryRoot, string projectRoot, string packageId, string title,
        CancellationToken ct = default, string? starterId = null, bool exportingCatalog = false)
        => TemplateProjectScaffolder.EnsureAsync(libraryRoot, projectRoot, packageId, title, ct, starterId, exportingCatalog);
}