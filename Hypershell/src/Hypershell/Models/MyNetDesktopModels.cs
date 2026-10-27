namespace Hypershell.Models;

public enum MyNetDesktopBackgroundMode
{
    Surface,
    Transparent,
    Image
}

public sealed class MyNetDesktopIconPosition
{
    public double X { get; set; }
    public double Y { get; set; }

    public MyNetDesktopIconPosition Clone() => new() { X = X, Y = Y };
}

/// <summary>Device-local presentation preferences, separate from app registrations.</summary>
public sealed class MyNetDesktopState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public MyNetDesktopBackgroundMode BackgroundMode { get; set; }
    public string BackgroundImagePath { get; set; } = string.Empty;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 720;
    public Dictionary<string, MyNetDesktopIconPosition> Icons { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public MyNetDesktopState Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        BackgroundMode = BackgroundMode,
        BackgroundImagePath = BackgroundImagePath,
        Left = Left,
        Top = Top,
        Width = Width,
        Height = Height,
        Icons = Icons is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : Icons.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.Clone() ?? new MyNetDesktopIconPosition(),
                StringComparer.OrdinalIgnoreCase)
    };
}
