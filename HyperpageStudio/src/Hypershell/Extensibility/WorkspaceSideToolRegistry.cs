namespace Hypershell.Extensibility;

public enum WorkspaceSideToolKind
{
    FileExplorer,
    ProjectWorkItems,
    SourceControl,
    AtlasMyNet,
    CommandBuilder,
    WorkspaceAi,
    NetworkObservatory
}

public sealed record WorkspaceSideToolDefinition(
    string Id,
    string DisplayName,
    string ShortDisplayName,
    string Glyph,
    string GlyphFontFamily,
    BuiltInWorkspaceSurface HostSurface,
    WorkspaceSideToolKind Kind,
    int DefaultOrder);

public static class WorkspaceSideToolRegistry
{
    public const string FileExplorerToolId = "hypershell.tool.file-explorer";
    public const string ProjectsToolId = "hypershell.tool.projects";
    // Retained for saved layouts and callers from earlier versions.
    public const string ProjectWorkItemsToolId =
        "hypershell.tool.project-work-items";
    public const string SourceControlToolId =
        "hypershell.tool.source-control";
    public const string AtlasMyNetToolId =
        "hypershell.tool.atlas-mynet";
    public const string CommandBuilderToolId = "hypershell.tool.command-builder";
    public const string WorkspaceAiToolId = "hypershell.tool.workspace-ai";
    public const string NetworkObservatoryToolId =
        "hypershell.tool.network-observatory";

    public static IReadOnlyList<WorkspaceSideToolDefinition> Definitions { get; } =
    [
        new(
            FileExplorerToolId,
            "File Explorer",
            "Explorer",
            "\uE8B7",
            "Segoe Fluent Icons",
            BuiltInWorkspaceSurface.FileExplorer,
            WorkspaceSideToolKind.FileExplorer,
            0),
        new(
            ProjectsToolId,
            "Projects",
            "Projects",
            "\uE8F1",
            "Segoe Fluent Icons",
            BuiltInWorkspaceSurface.FileExplorer,
            WorkspaceSideToolKind.ProjectWorkItems,
            1),
        new(
            AtlasMyNetToolId,
            "MyNet",
            "MyNet",
            "\uE968",
            "Segoe Fluent Icons",
            BuiltInWorkspaceSurface.FileExplorer,
            WorkspaceSideToolKind.AtlasMyNet,
            2),
        new(
            CommandBuilderToolId,
            "Command Builder",
            "Builder",
            "\uE756",
            "Segoe Fluent Icons",
            BuiltInWorkspaceSurface.CommandBuilder,
            WorkspaceSideToolKind.CommandBuilder,
            0),
        new(
            WorkspaceAiToolId,
            "AI Workspace",
            "AI",
            "AI",
            "JetBrains Mono",
            BuiltInWorkspaceSurface.CommandBuilder,
            WorkspaceSideToolKind.WorkspaceAi,
            1),
        new(
            NetworkObservatoryToolId,
            "Network Observatory",
            "Observe",
            "\uE968",
            "Segoe Fluent Icons",
            BuiltInWorkspaceSurface.CommandBuilder,
            WorkspaceSideToolKind.NetworkObservatory,
            2)
    ];

    public static string NormalizeToolId(string toolId) =>
        string.Equals(toolId, ProjectWorkItemsToolId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolId, SourceControlToolId, StringComparison.OrdinalIgnoreCase)
            ? ProjectsToolId
            : toolId;

    public static WorkspaceSideToolDefinition GetRequired(string toolId) =>
        Definitions.FirstOrDefault(definition => string.Equals(
            definition.Id,
            NormalizeToolId(toolId),
            StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException(
            $"Workspace side tool '{toolId}' is not registered.");
}
