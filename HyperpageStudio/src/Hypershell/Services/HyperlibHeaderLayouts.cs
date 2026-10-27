using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed record HyperlibHeaderPreset(string Id, string Title, string Description);

/// <summary>One portable header contract for the builder, previews, authored Pages, and published Works.</summary>
public static class HyperlibHeaderLayouts
{
    public const string BlockType = "studio-header-row";
    public const string BodyBlockType = "studio-body-row";
    public const string FooterBlockType = "studio-footer-row";
    public const string ComponentName = "StudioHeaderRowBlock";
    public static IReadOnlyList<HyperlibHeaderPreset> All { get; } = Array.AsReadOnly(new HyperlibHeaderPreset[] {
        new HyperlibHeaderPreset("simple-title", "Simple title", "A clear site title and short supporting line."),
        new("centered-title", "Centered title", "A centered identity with generous breathing room."),
        new("logo-title", "Logo and title", "A logo slot beside an editable title and supporting line."),
        new("split-heading", "Split heading", "A prominent title with a separate right-aligned description."),
        new("editorial-band", "Editorial band", "An understated upper label above a wide editorial title."),
        new("boxed-identity", "Boxed identity", "A nested identity card with its own border and padding."),
        new("two-row", "Two-row header", "A slim announcement row followed by the main identity."),
        new("image-banner", "Image banner", "A full-width image slot with a contrasting title card."),
        new("video-feature", "Video feature", "A video slot next to an editable site introduction."),
        new("three-part", "Three-part header", "A logo, central title, and independent supporting box.") });

    public static HyperlibHeaderLayoutDefinition Create(string id)
    {
        if (!All.Any(item => item.Id == id)) throw new InvalidDataException("Choose one of the ten header foundations.");
        var row = new HyperlibHeaderBlockDefinition { Height = 160 };
        var layout = new HyperlibHeaderLayoutDefinition { Enabled = true, Blocks = [row] };
        HyperlibHeaderElementDefinition Text(string value, double x, double y, double width, double size = 28, string alignment = "left")
            => new() { Text = value, X = x, Y = y, WidthPercent = width, FontSize = size, FontWeight = 600, Alignment = alignment };
        HyperlibHeaderElementDefinition Media(string kind, double x, double y, double width, double height)
            => new() { Kind = kind, X = x, Y = y, WidthPercent = width, Height = height, Alt = kind == "video" ? "Header video" : "Logo or header image", BorderRadius = 8 };
        switch (id)
        {
            case "simple-title": row.Elements = [Text("Your site", 4, 18, 92, 38), Text("A short description of your Work", 4, 58, 92, 18)]; break;
            case "centered-title": row.Height = 190; row.Elements = [Text("Your site", 8, 23, 84, 42, "center"), Text("A short description of your Work", 8, 63, 84, 18, "center")]; break;
            case "logo-title": row.Elements = [Media("image", 4, 22, 15, 84), Text("Your site", 23, 22, 73, 36), Text("A short description", 23, 60, 73, 18)]; break;
            case "split-heading": row.Elements = [Text("Your site", 4, 22, 48, 40), Text("Ideas, stories, and useful things", 58, 32, 38, 20, "right")]; break;
            case "editorial-band": row.Height = 200; row.Elements = [Text("A WORK IN PROGRESS", 4, 10, 92, 14), Text("Your site", 4, 38, 92, 48), Text("A short editorial introduction", 4, 76, 92, 18)]; break;
            case "boxed-identity":
                row.Height = 220;
                row.Elements = [new() { Kind = "box", X = 8, Y = 12, WidthPercent = 84, Height = 165, BorderWidth = 1, BorderRadius = 16, FillColor = "#152b35",
                    Children = [Text("Your site", 5, 16, 90, 34), Text("Your introduction", 5, 61, 90, 18)] }]; break;
            case "two-row":
                layout.Blocks.Insert(0, new() { Height = 48, FillColor = "#1e3b3a", Elements = [Text("A short announcement", 4, 20, 92, 15, "center")] });
                row.Elements = [Text("Your site", 4, 20, 92, 38), Text("A short supporting line", 4, 62, 92, 18)]; break;
            case "image-banner":
                row.Height = 280; row.Elements = [Media("image", 0, 0, 100, 280), new() { Kind = "box", X = 5, Y = 42, WidthPercent = 62, Height = 130,
                    FillColor = "#132a36", BorderRadius = 12, Children = [Text("Your site", 6, 18, 88, 36), Text("A short supporting line", 6, 64, 88, 18)] }]; break;
            case "video-feature": row.Height = 240; row.Elements = [Text("Your site", 4, 20, 47, 36), Text("Introduce your Work", 4, 58, 47, 18), Media("video", 56, 8, 40, 200)]; break;
            case "three-part": row.Elements = [Media("image", 3, 22, 13, 80), Text("Your site", 21, 28, 49, 34), new() { Kind = "box", X = 74, Y = 17,
                WidthPercent = 23, Height = 105, FillColor = "#183d39", BorderRadius = 10, Children = [Text("A useful note", 8, 28, 84, 17, "center")] }]; break;
        }
        Validate(layout);
        return layout;
    }

    public static void EnsureBlockDefinition(TemplateDefinition definition) => EnsureBlockDefinition(definition, BlockType, "Header");

    public static void EnsureBlockDefinition(TemplateDefinition definition, string blockType, string title)
    {
        if(blockType is not (BlockType or BodyBlockType or FooterBlockType))throw new InvalidDataException("Choose a supported Studio builder row type.");
        if (definition.Blocks.FirstOrDefault(item => item.Id == blockType) is { } existing)
        {
            if (existing.Component != ComponentName || !existing.Fields.Any(field => field.Id == "layout" && field.Type == "text"))
                throw new InvalidDataException("This project already uses the reserved Studio builder-row block ID for custom source.");
            if(!existing.Fields.Any(field=>field.Id=="studioRegion"))existing.Fields.Add(new(){Id="studioRegion",Title="Builder region",Type="string",DefaultValue=ContentJson.Value(blockType==BodyBlockType?"body":blockType==FooterBlockType?"footer":"header")});
            return;
        }
        definition.Blocks.Add(new() { Id = blockType, Title = title+" Block", Component = ComponentName, Category = title,
            Description = "A reusable builder Block with individually editable text, containers, media, and library objects.",
            Fields = [new() { Id = "layout", Title = title+" Block structure", Type = "text", DefaultValue = ContentJson.Value("") },new(){Id="studioRegion",Title="Builder region",Type="string",DefaultValue=ContentJson.Value(blockType==BodyBlockType?"body":blockType==FooterBlockType?"footer":"header")}] });
    }

    public static bool IsSlotContentField(FieldDefinition field) => field.Type is "text" or "string" or "url" or "media";

    public static IEnumerable<HyperlibHeaderElementDefinition> Elements(HyperlibHeaderLayoutDefinition layout)
    {
        foreach (var row in layout.Blocks)
            foreach (var element in Walk(row.Elements)) yield return element;
        static IEnumerable<HyperlibHeaderElementDefinition> Walk(IEnumerable<HyperlibHeaderElementDefinition> items)
        { foreach (var item in items) { yield return item; foreach (var child in Walk(item.Children)) yield return child; } }
    }

    public static void Validate(HyperlibHeaderLayoutDefinition layout, bool allowLocalMedia = false)
    {
        if (layout is null || layout.Blocks is null) throw new InvalidDataException("The header layout is missing.");
        if (!layout.Enabled && layout.Blocks.Count == 0) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var row in layout.Blocks)
        {
            if (row is null) throw new InvalidDataException("A header row is missing.");
            Id(row.Id); Range(row.Height, 24, 4000, "row height");
            Arrangement(row.Arrangement);
            Style(row.FillColor, row.BorderColor, row.BorderWidth, row.BorderStyle, row.BorderRadius, row.Padding);
            if (row.Elements is null) throw new InvalidDataException("A header row's elements are missing.");
            foreach (var element in row.Elements) Check(element, 0);
            var json = JsonSerializer.Serialize(row, ContentJson.Options);
            if (json.Length > 200_000 || ContentJson.Value(json).GetRawText().Length > 400_000)
                throw new InvalidDataException("This header row exceeds the existing content field limit. Split it into additional rows.");
        }
        void Id(string id) { TemplateValidator.ValidateId(id, "header element"); if (!ids.Add(id)) throw new InvalidDataException("Header row and element IDs must be unique."); }
        void Check(HyperlibHeaderElementDefinition element, int depth)
        {
            if (element is null || !seen.Add(element) || depth > 24) throw new InvalidDataException("Keep header boxes nested within the supported document depth, without circular references.");
            Id(element.Id);
            Arrangement(element.Arrangement);
            if (element.WidthMode is not ("percent" or "pixels")) throw new InvalidDataException("Choose percentage or pixel width.");
            Range(element.WidthPixels, 1, 4000, "element pixel width");
            if (element.PositionAlignment is not ("manual" or "left" or "center" or "right")) throw new InvalidDataException("Choose manual, left, centered, or right element placement.");
            if (element.Kind is not ("text" or "box" or "image" or "video" or "template" or "navigation")) throw new InvalidDataException("Add a text, box, image, video, navigation links, or library template element.");
            if(element.IsContentSlot&&element.Kind is not ("text" or "image" or "video" or "template"))throw new InvalidDataException("Content slots contain text, an image, video, or a library object. Keep Boxes and navigation structural.");
            if(element.SlotName is null||element.SlotName.Length>160||element.SlotName.Any(char.IsControl)||element.SlotHint is null||element.SlotHint.Length>1000||element.SlotHint.Any(char.IsControl))throw new InvalidDataException("Use a printable slot name up to 160 characters and guidance up to 1000 characters.");
            if (element.Kind == "navigation") HyperlibNavigationLayouts.Validate(element.Navigation);
            if (!double.IsFinite(element.WidthPercent) || element.WidthPercent <= 0 || element.WidthPercent > 100) throw new InvalidDataException("Element width must be above 0 and at most 100 percent.");
            Range(element.Height, 0, 4000, "element height"); Range(element.X, 0, 100, "horizontal position"); Range(element.Y, 0, 100, "vertical position");
            Range(element.FontSize, 8, 200, "text size");
            if (element.FontWeight is < 100 or > 900 || element.Alignment is not ("left" or "center" or "right")) throw new InvalidDataException("Choose a supported text weight and alignment.");
            if (!Regex.IsMatch(element.FontFamily ?? "", "^[a-zA-Z0-9 ,_-]{1,100}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Choose a standard header font family.");
            Color(element.TextColor, false);
            Style(element.FillColor, element.BorderColor, element.BorderWidth, element.BorderStyle, element.BorderRadius, element.Padding);
            if (element.Text is null || element.Text.Length > 200_000 || element.Alt is null || element.Alt.Length > 4000) throw new InvalidDataException("The header's text exceeds the supported content field size.");
            if (element.MediaFit is not ("contain" or "cover" or "fill")) throw new InvalidDataException("Choose Contain, Cover, or Fill for header media.");
            if (element.MediaPath is null || element.MediaPath.Length > 1024 || element.MediaPath.Any(char.IsControl)) throw new InvalidDataException("Choose a valid header media file.");
            if (element.MediaPath.Length > 0 && !SafeMedia(element.MediaPath)
                && !(allowLocalMedia && Path.IsPathFullyQualified(element.MediaPath) && !element.MediaPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Import header images and video into the project's Media folder.");
            if (element.Children is null || element.Kind != "box" && element.Children.Count > 0) throw new InvalidDataException("Only header boxes may contain other elements.");
            if (element.TemplateValues is null || element.TemplateBlocks is null || element.TemplateAppearance is null)
                throw new InvalidDataException("A header template's field values are missing.");
            _ = TemplateStyle(element.TemplateAppearance);
            if (element.Kind == "template" && (string.IsNullOrWhiteSpace(element.TemplateObjectId) || element.TemplateObjectId.Length > 240
                || element.TemplateObjectId.Any(char.IsControl))) throw new InvalidDataException("Choose a library object for this header element.");
            foreach (var child in element.Children) Check(child, depth + 1);
        }
    }

    private static void Range(double value, double minimum, double maximum, string title)
    { if (!double.IsFinite(value) || value < minimum || value > maximum) throw new InvalidDataException("Choose a supported " + title + "."); }
    private static void Arrangement(string value)
    { if (value is not ("positioned" or "horizontal" or "vertical")) throw new InvalidDataException("Choose a positioned, side-by-side, or stacked header arrangement."); }
    private static void Color(string value, bool transparent = true)
    { if (!(transparent && value == "") && !Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Choose a six-digit hexadecimal header color."); }
    private static void Style(string fill, string border, double width, string style, double radius, double padding)
    {
        Color(fill); Color(border, false); Range(width, 0, 20, "header border width"); Range(radius, 0, 200, "header corner radius"); Range(padding, 0, 200, "header padding");
        if (style is not ("none" or "solid" or "dashed" or "dotted" or "double")) throw new InvalidDataException("Choose a supported header border style.");
    }
    private static bool SafeMedia(string path) => path.Length <= 240 && path.StartsWith("/media/", StringComparison.Ordinal)
        && Regex.IsMatch(path[7..], "^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$", RegexOptions.CultureInvariant)
        && !path.Split('/').Any(part => part is "." or "..");
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Coordinate(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string H(string value) => WebUtility.HtmlEncode(value);
    private static string Surface(string fill, string border, double width, string style, double radius, double padding)
        => (fill.Length > 0 ? "background:" + fill + ";" : "") + "border:" + N(width) + "px " + style + " " + border + ";border-radius:" + N(radius) + "px;padding:" + N(padding) + "px;box-sizing:border-box;";

    public static string RowStyle(HyperlibHeaderBlockDefinition row)
        => (row.Arrangement == "positioned" ? "height:" : "height:auto;min-height:") + N(row.Height) + "px;"
            + Surface(row.FillColor, row.BorderColor, row.BorderWidth, row.BorderStyle, row.BorderRadius, row.Padding);

    public static string ContainerStyle(string arrangement)
    {
        Arrangement(arrangement);
        return arrangement == "positioned" ? "" : "display:flex;height:auto;flex-direction:" + (arrangement == "horizontal" ? "row;flex-wrap:wrap" : "column") + ";align-items:flex-start;";
    }

    public static string ContentClass(string arrangement)
    { Arrangement(arrangement); return "studio-header-arrangement-" + arrangement; }

    public static string ElementStyle(HyperlibHeaderElementDefinition element, string arrangement = "positioned")
    {
        var width = element.WidthMode == "pixels" ? N(element.WidthPixels) + "px" : N(element.WidthPercent) + "%";
        var geometry = arrangement == "positioned" ? "position:absolute;left:" + N(element.X) + "%;top:" + N(element.Y) + "%;"
            : "position:relative;left:auto;top:auto;flex:" + (arrangement == "horizontal" ? element.WidthMode == "pixels" ? "0 0 " + width : N(element.WidthPercent) + " 1 0px" : "0 0 auto") + ";";
        if (arrangement == "positioned")
            geometry += element.PositionAlignment switch { "left" => "left:0;", "center" => "left:50%;transform:translateX(-50%);", "right" => "left:auto;right:0;", _ => "" };
        else if (arrangement == "vertical")
            geometry += "align-self:" + (element.PositionAlignment == "center" ? "center" : element.PositionAlignment == "right" ? "flex-end" : "flex-start") + ";";
        else if (element.PositionAlignment == "center") geometry += "margin-left:auto;margin-right:auto;";
        else if (element.PositionAlignment == "right") geometry += "margin-left:auto;";
        return geometry + "width:" + width + ";max-width:" + (arrangement == "horizontal" && element.WidthMode != "pixels" ? width : "100%") + ";height:" + (element.Height > 0 ? N(element.Height) + "px" : "auto")
            + ";font-family:" + element.FontFamily + ";font-size:" + N(element.FontSize) + "px;font-weight:" + element.FontWeight + ";text-align:" + element.Alignment
            + ";color:" + element.TextColor + ";" + Surface(element.FillColor, element.BorderColor, element.BorderWidth, element.BorderStyle, element.BorderRadius, element.Padding)
            + (arrangement == "positioned" && element.Kind is "image" or "video" ? "max-height:100%;" : "")
            + (element.Height > 0 && (element.Kind == "template" || element.Kind == "box" && element.Arrangement != "positioned") ? "overflow:auto;" : "")
            + TemplateStyle(element.TemplateAppearance);
    }

    public static string MediaStyle(HyperlibHeaderElementDefinition element)
        => "object-fit:" + element.MediaFit + ";object-position:" + (element.PositionAlignment == "center" ? "center" : element.PositionAlignment == "right" ? "right" : "left")
            + " top;height:" + (element.Height > 0 ? "100%" : "auto");

    /// <summary>Only declared theme variables may cross a shared component's scoped boundary.</summary>
    public static string TemplateStyle(IReadOnlyDictionary<string, JsonElement> appearance)
    {
        if (appearance is null) throw new InvalidDataException("The scoped template appearance is missing.");
        var css = new StringBuilder();
        foreach (var (key, value) in appearance)
        {
            if (value.GetRawText().Length > 4096) throw new InvalidDataException("A scoped appearance field exceeds the existing content limit.");
            if (key == "fontSize" && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var size))
            { Range(size, 8, 200, "scoped text size"); css.Append("--hl-text-size:").Append(N(size)).Append("px;"); continue; }
            if (value.ValueKind != JsonValueKind.String) continue;
            var text = value.GetString() ?? "";
            var variable = key switch { "background" or "paper" => "--hl-paper", "ink" or "text" => "--hl-ink", "accent" => "--hl-accent",
                "surface" => "--hl-panel", "muted" => "--hl-muted", "border" => "--hl-border", "secondary" => "--hl-secondary", _ => "" };
            if (variable.Length > 0) { if (!Regex.IsMatch(text, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Choose a hexadecimal scoped template color."); css.Append(variable).Append(':').Append(text).Append(';'); }
            else if (key is "font" or "fontFamily" or "headingFont")
            {
                if (!Regex.IsMatch(text, "^[a-zA-Z0-9 ,_-]{1,100}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Choose a standard scoped font family.");
                var family = text switch { "system" => "system-ui,sans-serif", "humanist" => "Trebuchet MS,Segoe UI,sans-serif", "geometric" => "Century Gothic,Avenir Next,sans-serif", _ => text };
                css.Append(key == "headingFont" ? "--hl-heading-font:" : "--hl-font:").Append(family).Append(';');
            }
        }
        return css.ToString();
    }

    public static string RenderHtml(HyperlibHeaderLayoutDefinition layout, string region = "header", bool designPreview = false)
    { Validate(layout); if (Elements(layout).Any(element => element.Kind == "navigation")) throw new InvalidOperationException("Navigation links use the real interactive Page preview.");return layout.Enabled ? string.Concat(layout.Blocks.Select(row => RenderBlock(row, region, designPreview))) : ""; }
    public static string RenderSerializedBlock(string json, string region = "header", bool designPreview = false)
        => string.IsNullOrWhiteSpace(json) ? "" : RenderBlock(JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(json, ContentJson.Options)
            ?? throw new InvalidDataException("The layout row is empty."), region, designPreview);
    public static string RenderBlock(HyperlibHeaderBlockDefinition row, string region = "header", bool designPreview = false)
    {
        Validate(new() { Enabled = true, Blocks = [row] });
        if(Elements(new(){Enabled=true,Blocks=[row]}).Any(element=>element.Kind=="navigation"))throw new InvalidOperationException("Navigation links use the real interactive Page preview.");
        var output = new StringBuilder("<div class=\"studio-header-row\" data-studio-region=\"").Append(region is "body" or "navigation" or "footer" ? region : "header")
            .Append("\" data-header-row=\"").Append(H(row.Id)).Append("\" style=\"").Append(RowStyle(row))
            .Append("\"><div class=\"studio-header-row-content\" data-header-arrangement=\"").Append(row.Arrangement).Append("\" style=\"").Append(ContainerStyle(row.Arrangement)).Append("\">");
        foreach (var element in row.Elements) Render(element, output, row.Arrangement, designPreview);
        return output.Append("</div></div>").ToString();
    }
    private static void Render(HyperlibHeaderElementDefinition element, StringBuilder output, string arrangement, bool designPreview)
    {
        output.Append("<div class=\"studio-header-element studio-header-").Append(element.Kind).Append("\" data-header-element=\"").Append(H(element.Id))
            .Append("\" data-header-x=\"").Append(Coordinate(element.X)).Append("\" data-header-y=\"").Append(Coordinate(element.Y))
            .Append("\" data-header-width-mode=\"").Append(element.WidthMode).Append("\" data-header-width-percent=\"").Append(Coordinate(element.WidthPercent))
            .Append("\" data-header-width-pixels=\"").Append(Coordinate(element.WidthPixels)).Append("\" data-header-height=\"").Append(Coordinate(element.Height))
            .Append("\" data-header-alignment=\"").Append(element.PositionAlignment);
        if (element.IsContentSlot) output.Append("\" data-content-slot=\"true\" data-slot-name=\"").Append(H(element.SlotName));
        output.Append("\" style=\"")
            .Append(ElementStyle(element, arrangement)).Append("\">");
        var emptySlot = element.IsContentSlot && (element.Kind == "text" ? string.IsNullOrWhiteSpace(element.Text)
            : element.Kind is "image" or "video" && string.IsNullOrWhiteSpace(element.MediaPath));
        if (emptySlot)
        {
            if (designPreview) output.Append("<div data-studio-slot-placeholder=\"true\" style=\"box-sizing:border-box;min-height:36px;height:100%;padding:8px;outline:1px dashed currentColor;outline-offset:-2px;display:flex;flex-direction:column;justify-content:center;font:12px system-ui;overflow:hidden\"><strong>")
                .Append(H(string.IsNullOrWhiteSpace(element.SlotName) ? element.Kind == "text" ? "Text content" : element.Kind == "video" ? "Video content" : "Image content" : element.SlotName))
                .Append("</strong><span>").Append(H(string.IsNullOrWhiteSpace(element.SlotHint) ? "Add content in Authoring." : element.SlotHint)).Append("</span></div>");
        }
        else if (element.Kind == "text") output.Append("<div class=\"studio-header-text\">").Append(H(element.Text)).Append("</div>");
        else if (element.Kind == "box")
        {
            output.Append("<div class=\"studio-header-box-content\" data-header-arrangement=\"").Append(element.Arrangement).Append("\" style=\"").Append(ContainerStyle(element.Arrangement)).Append("\">");
            foreach (var child in element.Children) Render(child, output, element.Arrangement, designPreview);
            output.Append("</div>");
        }
        else if (element.MediaPath.Length == 0) output.Append("<div class=\"studio-header-media-placeholder\" role=\"img\" aria-label=\"").Append(H(element.Alt.Length > 0 ? element.Alt : element.Kind == "video" ? "Video" : "Image")).Append("\">").Append(element.Kind == "video" ? "Video" : "Image").Append("</div>");
        else if (element.Kind == "image") output.Append("<img src=\"").Append(H(element.MediaPath)).Append("\" alt=\"").Append(H(element.Alt)).Append("\" style=\"").Append(MediaStyle(element)).Append("\"/>");
        else output.Append("<video controls playsinline preload=\"metadata\" src=\"").Append(H(element.MediaPath)).Append("\" aria-label=\"").Append(H(element.Alt)).Append("\" style=\"").Append(MediaStyle(element)).Append("\"></video>");
        output.Append("</div>");
    }

    public static string MergeSerializedBlock(string current, string previous, string next)
    {
        if (string.IsNullOrWhiteSpace(current)) return current;
        var live = JsonNode.Parse(current)!.AsObject(); var prior = JsonNode.Parse(previous)!.AsObject(); var target = JsonNode.Parse(next)!.AsObject();
        var merged = MergeObject(live, prior, target, "elements");
        var row = JsonSerializer.Deserialize<HyperlibHeaderBlockDefinition>(merged, ContentJson.Options)!;
        Validate(new() { Enabled = true, Blocks = [row] });
        return JsonSerializer.Serialize(row, ContentJson.Options);
    }
    public static bool HasIndependentChanges(string current, string previous)
    { try { return !JsonNode.DeepEquals(JsonNode.Parse(current), JsonNode.Parse(previous)); } catch (JsonException) { return true; } }
    private static JsonObject MergeObject(JsonObject live, JsonObject prior, JsonObject target, string collection)
    {
        if (((target["kind"] is not null && !JsonNode.DeepEquals(target["kind"], prior["kind"]))
                || !JsonNode.DeepEquals(target["templateObjectId"], prior["templateObjectId"])) && !JsonNode.DeepEquals(live, prior))
            return (JsonObject)live.DeepClone();
        var output = (JsonObject)target.DeepClone();
        foreach (var property in live.Where(item => item.Key != collection))
        {
            prior.TryGetPropertyValue(property.Key, out var old);
            if (property.Key is "templateValues" or "templateAppearance" or "navigation" && property.Value is JsonObject liveFields && old is JsonObject oldFields && target[property.Key] is JsonObject nextFields)
                output[property.Key] = MergeFields(liveFields, oldFields, nextFields);
            else if (property.Key == "templateBlocks")
                output[property.Key] = MergeBlocks(property.Value as JsonArray ?? [], old as JsonArray ?? [], target[property.Key] as JsonArray ?? []);
            else if (old is null || !JsonNode.DeepEquals(property.Value, old)) output[property.Key] = property.Value?.DeepClone();
        }
        var oldItems = (prior[collection] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(item => item["id"]!.GetValue<string>(), StringComparer.Ordinal);
        var liveItems = (live[collection] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(item => item["id"]!.GetValue<string>(), StringComparer.Ordinal);
        var nextItems = (target[collection] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        var outputItems = new JsonArray();
        foreach (var item in nextItems)
        {
            var id = item["id"]!.GetValue<string>();
            outputItems.Add(liveItems.TryGetValue(id, out var current) && oldItems.TryGetValue(id, out var previous)
                ? MergeObject(current, previous, item, "children") : item.DeepClone());
        }
        foreach (var item in liveItems)
            if (!nextItems.Any(next => next["id"]!.GetValue<string>() == item.Key)
                && (!oldItems.TryGetValue(item.Key, out var previous) || !JsonNode.DeepEquals(item.Value, previous))) outputItems.Add(item.Value.DeepClone());
        output[collection] = outputItems;
        return output;
    }

    private static JsonObject MergeFields(JsonObject live, JsonObject prior, JsonObject target)
    {
        var result = (JsonObject)target.DeepClone();
        foreach (var (key, value) in live)
        {
            prior.TryGetPropertyValue(key, out var old);
            if (value is JsonObject nested && old is JsonObject oldNested && target[key] is JsonObject nextNested)
                result[key] = MergeFields(nested, oldNested, nextNested);
            else if(key=="customLinks"&&value is JsonArray liveLinks&&old is JsonArray oldLinks&&target[key] is JsonArray nextLinks)
                result[key]=MergeBlocks(liveLinks,oldLinks,nextLinks);
            else if (old is null || !JsonNode.DeepEquals(value, old)) result[key] = value?.DeepClone();
        }
        return result;
    }

    private static JsonArray MergeBlocks(JsonArray live, JsonArray prior, JsonArray target)
    {
        var result = new JsonArray();
        foreach (var block in target.OfType<JsonObject>())
        {
            var id = block["id"]?.GetValue<string>();
            var current = live.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<string>() == id);
            var previous = prior.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.GetValue<string>() == id);
            result.Add(current is not null && previous is not null ? MergeFields(current, previous, block) : block.DeepClone());
        }
        foreach (var block in live.OfType<JsonObject>())
            if (!target.OfType<JsonObject>().Any(item => item["id"]?.GetValue<string>() == block["id"]?.GetValue<string>())
                && !prior.OfType<JsonObject>().Any(item => JsonNode.DeepEquals(item, block))) result.Add(block.DeepClone());
        return result;
    }

    public const string Css = """
        /* studio-header-layouts-v1 */
        .hl-region[data-region="header"] > .hl-block:has(.studio-header-row) { grid-column:1/-1; width:100%; min-width:0; padding:0; }
        .studio-header-row { position:relative; width:100%; min-width:0; isolation:isolate; }
        .studio-header-row-content,.studio-header-box-content { position:relative; width:100%; height:100%; min-width:0; }
        .studio-header-element { position:absolute; min-width:0; margin:0; overflow-wrap:anywhere; }
        /* studio-header-arrangements-v1 */
        [data-header-arrangement="horizontal"] > .studio-header-element,[data-header-arrangement="vertical"] > .studio-header-element { position:relative; }
        .studio-header-box { position:absolute; }
        .studio-header-text { margin:0; white-space:pre-wrap; font:inherit; line-height:1.2; }
        /* studio-header-template-elements-v1 */
        .studio-header-template-content { display:grid; grid-template-columns:repeat(12,minmax(0,1fr)); gap:12px; width:100%; min-width:0; }
        .studio-header-template-block { grid-column:span 12; min-width:0; margin:0; }
        .studio-header-template-block.hl-span-half { grid-column:span 6; }
        .studio-header-template-block.hl-span-third { grid-column:span 4; }
        .studio-header-template-block.hl-span-two-thirds { grid-column:span 8; }
        .studio-header-template-block.hl-span-quarter { grid-column:span 3; }
        /* studio-header-positioned-media-v1 */
        .studio-header-element > img,.studio-header-element > video { display:block; width:100%!important; max-width:100%!important; height:100%; max-height:none; margin:0; border-radius:inherit; object-position:left top; }
        .studio-header-media-placeholder { display:flex; align-items:center; justify-content:center; width:100%; height:100%; min-height:48px; border:1px dashed currentColor; opacity:.65; font:14px system-ui; text-align:center; }
        """;
}
