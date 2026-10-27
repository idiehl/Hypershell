namespace Hypershell.Models;

public enum StudioProjectKind
{
    Hyperlib,
    Razor,
    Html
}

public sealed class StudioProjectCreationRequest
{
    public StudioProjectKind Kind { get; init; } = StudioProjectKind.Hyperlib;
    public string Name { get; init; } = string.Empty;
    public string ParentDirectory { get; init; } = string.Empty;
    public HyperlibWorkProjectDefinition? Work { get; init; }
    public string? CompleteTemplateId { get; init; }
}
