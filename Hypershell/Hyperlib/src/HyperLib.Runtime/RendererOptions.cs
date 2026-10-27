using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using HyperLib.Core;

namespace HyperLib.Runtime;

public sealed class RendererOptions
{
    public required string PackageId { get; init; }
    public required string GenerationId { get; init; }
    public required string GatewayUrl { get; init; }
    public required string PathBase { get; init; }
    public required TemplateDefinition Template { get; init; }
    public required IReadOnlyDictionary<string, Type> Components { get; init; }
    public Type? ShellComponent { get; init; }
    public bool ReadOnly { get; init; }
    public Type Resolve(string blockId) => Components.TryGetValue(
        Template.Blocks.Single(b => b.Id == blockId).Component, out var type)
        ? type : throw new InvalidOperationException($"Block {blockId} has no compiled component.");

    public string Appearance(string name, LibraryState? state = null, LibraryNode? node = null)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while(node is not null && visited.Add(node.Id))
        {
            if(node.Appearance.TryGetValue(name,out var n)) return Value(n);
            node = state?.Nodes.FirstOrDefault(candidate => candidate.Id == node.ParentId);
        }
        if (state?.Appearance.TryGetValue(name, out var s) == true) return Value(s);
        return StandardAppearance.WithDefaults(Template.Appearance).FirstOrDefault(f => f.Id == name)?.DefaultValue is { } d ? Value(d) : "";
    }
    public string AppearanceStyle(LibraryState? state, LibraryNode? node)
    {
        var background = Color(Appearance("background", state, node), "#f5f3ed");
        var dark = Luminance(background) < .25;
        var accent = Color(Appearance("accent", state, node), "#2f645a");
        var accentInk = Luminance(accent) > .179 ? "#111111" : "#ffffff";
        var ink = Color(Appearance("ink", state, node), dark ? "#e8eee9" : "#253a32");
        var muted = Color(Appearance("muted", state, node), dark ? "#a7b9ae" : "#60756a");
        var panel = Color(Appearance("surface", state, node), dark ? "#ffffff0a" : "#ffffff99");
        var textSize = TextSize(Appearance("fontSize", state, node));
        return $"--hl-accent:{accent};--hl-accent-ink:{accentInk};--hl-paper:{background};--hl-ink:{ink};--hl-muted:{muted};--hl-panel:{panel};--hl-font:{FontFamily(Appearance("font", state, node))};--hl-heading-font:{FontFamily(Appearance("headingFont", state, node), FontFamily(Appearance("font", state, node)))};--hl-text-size:{textSize}px;color:var(--hl-ink);font-family:var(--hl-font);font-size:var(--hl-text-size)";
    }
    private static string TextSize(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
        && double.IsFinite(size) && size is >= StandardAppearance.MinimumFontSize and <= StandardAppearance.MaximumFontSize
        ? size.ToString("0.###", CultureInfo.InvariantCulture)
        : StandardAppearance.DefaultFontSize.ToString(CultureInfo.InvariantCulture);
    private static readonly HashSet<string> Variants = new(StringComparer.Ordinal)
    {
        "woodland-editorial", "wave-laboratory", "portfolio-grid", "field-journal", "architecture-studio",
        "course-notebook", "recipe-kitchen", "event-poster", "travel-atlas", "product-launch", "podcast-studio",
        "gallery-wall", "documentation-desk", "nonprofit-story", "wellness-retreat", "maker-workbench", "insight-dashboard"
    };
    public string VariantClass(LibraryState? state, LibraryNode? node)
    {
        var variant = Appearance("variant", state, node);
        return "hl-variant-" + (Variants.Contains(variant) ? variant : "woodland-editorial");
    }
    public static string FontFamily(string value, string fallback = "Georgia,'Times New Roman',serif") => value switch
    {
        "system" or "system-ui" or "system-ui,sans-serif" or "system-ui, sans-serif" => "system-ui,-apple-system,'Segoe UI',sans-serif",
        "serif" or "Georgia" or "Georgia,serif" or "Georgia, serif" or "Georgia,'Times New Roman',serif" => "Georgia,'Times New Roman',serif",
        "monospace" or "monospace,monospace" => "'Cascadia Code','Consolas',monospace",
        "humanist" => "'Trebuchet MS','Segoe UI',sans-serif",
        "geometric" => "'Century Gothic','Avenir Next',sans-serif",
        _ => fallback
    };
    /// <summary>Resolve one element's defaults without changing its saved content.</summary>
    public PageBlock WithFieldDefaults(PageBlock block)
    {
        var values = Template.Blocks.Single(definition => definition.Id == block.Type).Fields
            .Where(field => field.DefaultValue.HasValue)
            .ToDictionary(field => field.Id, field => field.DefaultValue!.Value, StringComparer.Ordinal);
        foreach (var pair in block.Values) values[pair.Key] = pair.Value;
        return new PageBlock { Id = block.Id, Type = block.Type, Region = block.Region, Values = values };
    }

    public string BlockTypeClass(PageBlock block)
    {
        var component = Template.Blocks.Single(definition => definition.Id == block.Type).Component.Split('.').Last();
        var visualType = component switch
        {
            "AudioBlock" => "audio", "VideoBlock" => "video", "ImageBlock" => "image",
            "RichTextBlock" => "richtext", "CitationBlock" => "citation", "BannerBlock" => "banner",
            _ => block.Type
        };
        return visualType == block.Type ? "hl-type-" + block.Type : $"hl-type-{block.Type} hl-type-{visualType}";
    }

    public static string BlockSpanClass(PageBlock block) => "hl-span-" + (BlockValue(block, "span") switch
    {
        "half" => "half", "third" => "third", "two-thirds" => "two-thirds", _ => "full"
    });
    public static string BlockStyle(PageBlock block)
    {
        var rules = new List<string>();
        var font = BlockValue(block, "font");
        if (font.Length > 0) rules.Add($"--hl-block-font:{FontFamily(font)};font-family:var(--hl-block-font)");
        var align = BlockValue(block, "align");
        if (align is "left" or "center" or "right" or "justify") rules.Add("text-align:" + align);
        var color = Color(BlockValue(block, "color"), "");
        if (color.Length > 0) rules.Add($"--hl-block-ink:{color};color:var(--hl-block-ink)");
        return string.Join(';', rules);
    }
    private static string BlockValue(PageBlock block, string name) => block.Values.TryGetValue(name, out var value) ? Value(value) : "";
    private static string Color(string value, string fallback) => value.Length is 4 or 5 or 7 or 9
        && value[0] == '#' && value.AsSpan(1).ToArray().All(Uri.IsHexDigit) ? value : fallback;
    private static double Luminance(string color)
    {
        var hex = color.Length is 4 or 5 ? string.Concat(color.Skip(1).Take(3).SelectMany(character => new[] { character, character })) : color.Substring(1, 6);
        static double Linear(int channel) { var value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(Convert.ToInt32(hex[..2], 16)) + .7152 * Linear(Convert.ToInt32(hex[2..4], 16)) + .0722 * Linear(Convert.ToInt32(hex[4..6], 16));
    }
    private static string Value(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
}

public sealed class RendererContentClient
{
    private readonly HttpClient _client;
    public RendererContentClient(RendererOptions options)
    {
        _client = new HttpClient { BaseAddress = new Uri(options.GatewayUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        if (Environment.GetEnvironmentVariable("HYPERLIB_GATEWAY_TOKEN") is { Length: > 0 } token)
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
    public async Task<PageDocument> GetPageAsync(string id) =>
        await _client.GetFromJsonAsync<PageDocument>("api/renderer/page/" + Uri.EscapeDataString(id), ContentJson.Options)
        ?? throw new InvalidOperationException("The page could not be loaded.");
    public async Task<LibraryState> GetStateAsync() =>
        await _client.GetFromJsonAsync<LibraryState>("api/state", ContentJson.Options)
        ?? throw new InvalidOperationException("The library could not be loaded.");
}
