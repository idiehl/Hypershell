using System.IO;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Editable Body foundations built from the shared row and recursive element contract.</summary>
public static class HyperlibBodyLayouts
{
    public const string BlockType = HyperlibHeaderLayouts.BodyBlockType;
    public static IReadOnlyList<HyperlibHeaderPreset> All { get; } = Array.AsReadOnly(new HyperlibHeaderPreset[] {
        new("single-column", "Single column", "A heading and a generous content area."),
        new("two-columns", "Two columns", "Two editable content boxes side by side."),
        new("article-and-aside", "Article and aside", "A wide reading area with a smaller supporting box."),
        new("media-and-text", "Media and text", "An image beside a heading and text."),
        new("card-grid", "Three cards", "Three independent boxes for related content."),
        new("feature-and-sections", "Feature and sections", "An introductory block followed by two content sections.") });

    public static void EnsureBlockDefinition(TemplateDefinition template)
        => HyperlibHeaderLayouts.EnsureBlockDefinition(template, BlockType, "Body");

    public static HyperlibHeaderLayoutDefinition Create(string id)
    {
        if (!All.Any(item => item.Id == id)) throw new InvalidDataException("Choose an available Body layout.");
        var result = new HyperlibHeaderLayoutDefinition { Enabled = true };
        switch (id)
        {
            case "single-column":
                result.Blocks.Add(Row(90, Text("Heading", 32, 600)));
                result.Blocks.Add(Row(260, Box(100, 236, "Content", "Add text, media, or a template object.")));
                break;
            case "two-columns":
                result.Blocks.Add(Row(280, Box(50, 252, "First section", "Build this section."), Box(50, 252, "Second section", "Build this section.")));
                break;
            case "article-and-aside":
                result.Blocks.Add(Row(360, Box(70, 332, "Main content", "A spacious reading area."), Box(30, 332, "Alongside", "Related information or tools.")));
                break;
            case "media-and-text":
                result.Blocks.Add(Row(280, new() { Kind = "image", IsContentSlot = true, SlotName = "Lead image", WidthPercent = 40, Height = 248, Alt = "" }, Box(60, 248, "Heading", "Place text alongside your image.")));
                break;
            case "card-grid":
                result.Blocks.Add(Row(250, Box(33.333, 222, "First card", "Add content."), Box(33.333, 222, "Second card", "Add content."), Box(33.333, 222, "Third card", "Add content.")));
                break;
            case "feature-and-sections":
                result.Blocks.Add(Row(180, Box(100, 152, "Featured section", "Introduce the content below.")));
                result.Blocks.Add(Row(250, Box(50, 222, "First section", "Add content."), Box(50, 222, "Second section", "Add content.")));
                break;
        }
        return result;
    }

    private static HyperlibHeaderBlockDefinition Row(double height, params HyperlibHeaderElementDefinition[] elements)
        => new() { Height = height, Arrangement = "horizontal", Padding = 12, Elements = elements.ToList() };
    private static HyperlibHeaderElementDefinition Text(string text, double size = 18, int weight = 400)
        => new() { IsContentSlot = true, SlotName = text, Text = "", FontSize = size, FontWeight = weight, WidthPercent = 100, Height = 0 };
    private static HyperlibHeaderElementDefinition Box(double width, double height, string title, string description)
        => new() { Kind = "box", Arrangement = "vertical", WidthPercent = width, Height = height, Padding = 16,
            BorderWidth = 1, BorderRadius = 10, Children = [Text(title+" heading", 24, 600), new() { IsContentSlot = true, SlotName = title+" text", SlotHint = description, Text = "", FontSize = 18 }] };
}
