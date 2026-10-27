namespace HyperLib.Core;

/// <summary>Optional renderer settings available to older Work contracts without rewriting their source.</summary>
public static class StandardAppearance
{
    public const double DefaultFontSize = 18;
    public const double MinimumFontSize = 12;
    public const double MaximumFontSize = 32;

    public static FieldDefinition FontSize() => new()
    {
        Id = "fontSize", Title = "Text size (px)", Type = "number",
        DefaultValue = ContentJson.Value(DefaultFontSize), Min = MinimumFontSize, Max = MaximumFontSize
    };

    /// <summary>Explicit template fields retain their types, defaults and constraints.</summary>
    public static IEnumerable<FieldDefinition> WithDefaults(IEnumerable<FieldDefinition> fields)
    {
        var hasFontSize = false;
        foreach (var field in fields)
        {
            hasFontSize |= field.Id == "fontSize";
            yield return field;
        }
        if (!hasFontSize) yield return FontSize();
    }
}
