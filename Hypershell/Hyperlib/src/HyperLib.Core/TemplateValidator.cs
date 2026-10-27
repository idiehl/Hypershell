using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace HyperLib.Core;

public static partial class TemplateValidator
{
    private static readonly HashSet<string> FieldTypes = new(StringComparer.Ordinal)
        { "string", "text", "number", "boolean", "color", "media", "url", "select" };

    public static void ValidateDefinition(TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Require(definition.SchemaVersion == 1, "Unsupported template schema version.");
        ValidateId(definition.TemplateId, "template");
        Require(definition.Regions is { Count: > 0 and <= 100 }, "Templates need between 1 and 100 regions.");
        Require(definition.Blocks is { Count: > 0 and <= 200 }, "Templates need between 1 and 200 block definitions.");
        Require(definition.Regions.All(region => region is not null) && definition.Blocks.All(block => block is not null), "Template definitions cannot contain null entries.");
        var blockIds = UniqueIds(definition.Blocks.Select(block => block.Id), "block");
        UniqueIds(definition.Regions.Select(region => region.Id), "region");
        foreach (var region in definition.Regions)
        {
            Require(region.AllowedBlocks is not null, $"Region '{region.Id}' has no allowedBlocks list.");
            Require(region.AllowedBlocks.All(blockIds.Contains), $"Region '{region.Id}' references an unknown block.");
            Require(region.AllowedBlocks.Distinct(StringComparer.Ordinal).Count() == region.AllowedBlocks.Count,
                $"Region '{region.Id}' contains duplicate allowed blocks.");
        }
        foreach (var block in definition.Blocks)
        {
            Require(block.Kind is "razor" or "javascript", $"Block '{block.Id}' has unsupported kind '{block.Kind}'.");
            Require(!string.IsNullOrWhiteSpace(block.Component) && ComponentName().IsMatch(block.Component),
                $"Block '{block.Id}' needs a valid compiled component name.");
            Require(block.CatalogId is null || Regex.IsMatch(block.CatalogId, "^[A-Z0-9][A-Z0-9_-]{2,99}$", RegexOptions.CultureInvariant), "Catalog IDs need 3–100 uppercase letters, digits, hyphens or underscores.");
            Require(block.Category is null or { Length: <= 100 } && block.Description is null or { Length: <= 4000 }
                && block.SourceTemplateId is null or { Length: <= 200 } && block.SourceLibrary is null or { Length: <= 200 },
                $"Block '{block.Id}' has oversized catalog metadata.");
            ValidateFields(block.Fields, block.Id);
        }
        ValidateFields(definition.Appearance, "appearance");
        ValidateBlocks(definition, definition.DefaultBlocks, "template defaults");
    }

    public static void ValidatePage(TemplateDefinition definition, PageDocument page)
    {
        ValidateDefinition(definition);
        Require(page.TemplateId == definition.TemplateId,
            $"Page '{page.Title}' uses template '{page.TemplateId}', but the project declares '{definition.TemplateId}'.");
        ValidateBlocks(definition, page.Blocks, $"page '{page.Title}'");
    }

    private static void ValidateBlocks(TemplateDefinition definition, List<PageBlock> content, string context)
    {
        Require(content is { Count: <= 1000 }, "A page or template default may contain at most 1,000 blocks.");
        Require(content.All(block => block is not null), "Page blocks cannot be null.");
        UniqueIds(content.Select(block => block.Id), "page block");
        var regions = definition.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var blocks = definition.Blocks.ToDictionary(block => block.Id, StringComparer.Ordinal);
        foreach (var block in content)
        {
            Require(regions.TryGetValue(block.Region, out var region), $"Unknown region '{block.Region}' in {context}.");
            Require(blocks.TryGetValue(block.Type, out var schema), $"Unknown block '{block.Type}' in {context}.");
            Require(region!.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains(block.Type, StringComparer.Ordinal),
                $"Block '{block.Type}' is not allowed in region '{block.Region}'.");
            ValidateValues(schema!.Fields, block.Values, $"block '{block.Id}'");
        }
    }

    public static void EnsureCompatible(TemplateDefinition candidate, IEnumerable<PageDocument> pages)
    {
        ValidateDefinition(candidate);
        foreach (var page in pages) ValidatePage(candidate, page);
    }

    public static void EnsureCompatible(TemplateDefinition candidate, IEnumerable<PageDocument> pages,
        IEnumerable<Dictionary<string, JsonElement>> appearances)
    {
        EnsureCompatible(candidate, pages);
        foreach (var appearance in appearances) ValidateValues(StandardAppearance.WithDefaults(candidate.Appearance), appearance, "existing appearance settings");
    }

    public static void ValidateValues(IEnumerable<FieldDefinition> definitions, Dictionary<string, JsonElement> values, string context)
    {
        Require(values is not null && values.Count <= 200, $"Invalid fields for {context}.");
        var fields = definitions.ToDictionary(field => field.Id, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            Require(fields.TryGetValue(pair.Key, out var field), $"Unknown field '{pair.Key}' in {context}; removing used fields would lose content.");
            ValidateValue(field!, pair.Value, context);
        }
        foreach (var field in fields.Values)
            Require(!field.Required || values.ContainsKey(field.Id) || field.DefaultValue is not null,
                $"Required field '{field.Id}' is missing in {context}.");
    }

    public static PageDocument CreateDefaultPage(string id, string workId, string title, TemplateDefinition? definition = null)
    {
        definition ??= StarterTemplate.Create();
        ValidateDefinition(definition);
        if (definition.DefaultBlocks.Count > 0 || definition.EmptyPageDefault)
        {
            var page = new PageDocument
            {
                Id = id, WorkId = workId, Title = title, TemplateId = definition.TemplateId, UsesEditableChrome = true,
                Blocks = ContentJson.Clone(definition.DefaultBlocks)
            };
            var heading = page.Blocks.FirstOrDefault(block => block.Type is "header" or "banner");
            if (heading is not null && definition.Blocks.First(block => block.Id == heading.Type).Fields.Any(field => field.Id == "title"))
                heading.Values["title"] = ContentJson.Value(title);
            ValidatePage(definition, page);
            return page;
        }
        var region = definition.Regions[0];
        var block = definition.Blocks.First(item => region.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains(item.Id));
        return new PageDocument
        {
            Id = id, WorkId = workId, Title = title, TemplateId = definition.TemplateId,
            Blocks = [new PageBlock
            {
                Id = "block-" + Guid.NewGuid().ToString("N"), Type = block.Id, Region = region.Id,
                Values = block.Fields.Where(field => field.DefaultValue is not null)
                    .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal)
            }]
        };
    }

    public static void ValidateId(string? id, string label)
        => Require(id is not null && Identifier().IsMatch(id), $"Invalid {label} ID. Use 1–100 ASCII letters, digits, hyphens or underscores, beginning with a letter or digit.");

    public static bool IsSafeUrl(string? value, bool allowRelative = true)
    {
        if (string.IsNullOrEmpty(value)) return true;
        if (value.Any(char.IsControl) || value.Contains('\\')) return false;
        if (allowRelative && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
            return !value.Split('/').Any(part => Uri.UnescapeDataString(part) is ".." or ".");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static HashSet<string> UniqueIds(IEnumerable<string> ids, string context)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            ValidateId(id, context);
            Require(set.Add(id), $"Duplicate {context} ID '{id}'.");
        }
        return set;
    }

    private static void ValidateFields(List<FieldDefinition> fields, string context)
    {
        Require(fields is { Count: <= 200 }, $"Too many fields in '{context}'.");
        Require(fields.All(field => field is not null), $"Null field in '{context}'.");
        UniqueIds(fields.Select(field => field.Id), "field");
        foreach (var field in fields)
        {
            Require(field.Type is not null && FieldTypes.Contains(field.Type), $"Field '{field.Id}' has unsupported type '{field.Type}'.");
            Require(field.Options is not null && field.Options.Count <= 1000, $"Invalid options for '{field.Id}'.");
            Require(field.Type != "select" || field.Options.Count > 0, $"Select field '{field.Id}' needs options.");
            Require(field.Min is null || double.IsFinite(field.Min.Value), $"Invalid minimum for '{field.Id}'.");
            Require(field.Max is null || double.IsFinite(field.Max.Value), $"Invalid maximum for '{field.Id}'.");
            Require(field.Min is null || field.Max is null || field.Min <= field.Max, $"Inverted limits for '{field.Id}'.");
            if (field.DefaultValue is { } value) ValidateValue(field, value, $"default for '{context}'");
        }
    }

    private static void ValidateValue(FieldDefinition field, JsonElement value, string context)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            Require(!field.Required, $"Field '{field.Id}' is required in {context}.");
            return;
        }
        if (field.Type == "number")
        {
            Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed) && double.IsFinite(parsed),
                $"Field '{field.Id}' must be a finite number in {context}.");
            var number = value.GetDouble();
            Require((field.Min is null || number >= field.Min) && (field.Max is null || number <= field.Max),
                $"Field '{field.Id}' is outside its allowed range in {context}.");
        }
        else if (field.Type == "boolean")
            Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"Field '{field.Id}' must be a boolean in {context}.");
        else
        {
            Require(value.ValueKind == JsonValueKind.String, $"Field '{field.Id}' must be text in {context}.");
            var text = value.GetString()!;
            Require(text.Length <= 200_000, $"Field '{field.Id}' exceeds 200,000 characters.");
            if (field.Type is "url" or "media") Require(IsSafeUrl(text), $"Field '{field.Id}' is not a safe HTTP(S) or rooted media URL.");
            if (field.Type == "select") Require(field.Options.Contains(text, StringComparer.Ordinal), $"Field '{field.Id}' is not an allowed option.");
            if (field.Type == "color") Require(Color().IsMatch(text), $"Field '{field.Id}' must be a hexadecimal color.");
        }
    }

    internal static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new ContentValidationException(message);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentName();
    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex Color();
}

public static class StarterTemplate
{
    public static IReadOnlyList<string> SafeFonts { get; } = Array.AsReadOnly(new[] { "system", "serif", "monospace", "humanist", "geometric" });

    public static TemplateDefinition Create()
    {
        var definition = new TemplateDefinition
        {
        TemplateId = "standard", Title = "Flexible page",
        Regions = [new() { Id = "main", Title = "Page content", AllowedBlocks = ["text", "richtext", "image", "gallery", "audio", "video", "wave", "callout", "counter", "citation", "header", "footer", "banner", "divider", "form", "map", "embed", "tool"] }],
        Blocks =
        [
            new() { Id = "text", Title = "Text", Component = "TextBlock", Fields = [Field("text", "Text", "text", "Write something worth exploring.")] },
            new() { Id = "richtext", Title = "Rich text", Component = "RichTextBlock", Fields = [Field("text", "Markdown", "text", "Write **something** worth exploring.")] },
            new() { Id = "image", Title = "Image", Component = "ImageBlock", Fields = [Field("src", "Image", "media", ""), Field("alt", "Description", "string", ""), Field("caption", "Caption", "string", ""), Number("width", "Width (%)", 100, 40, 100)] },
            new() { Id = "gallery", Title = "Gallery", Component = "GalleryBlock", Fields = [Field("src1", "First image", "media", ""), Field("src2", "Second image", "media", ""), Field("caption", "Caption", "string", "")] },
            new() { Id = "audio", Title = "Audio", Component = "AudioBlock", Fields = [Field("src", "Audio", "media", ""), Field("caption", "Caption", "string", "")] },
            new() { Id = "video", Title = "Video", Component = "VideoBlock", Fields = [Field("src", "Video", "media", ""), Field("caption", "Caption", "string", "")] },
            new() { Id = "wave", Title = "Wave experiment", Component = "WaveBlock", Fields = [Number("frequency", "Frequency", 2, 0.1, 10), Number("amplitude", "Amplitude", 0.7, 0, 1)] },
            new() { Id = "callout", Title = "Callout", Component = "CalloutBlock", Fields = [Field("title", "Title", "string", "Worth noticing"), Field("text", "Text", "text", "Add a useful observation.")] },
            new() { Id = "counter", Title = "Interactive counter", Kind = "javascript", Component = "CounterBlock", Fields = [Field("label", "Label", "string", "Observations"), Number("start", "Starting value", 0, -1000000, 1000000)] },
            new() { Id = "citation", Title = "Source citation", Component = "CitationBlock", Fields = [Field("url", "Source URL", "url", ""), Field("title", "Source title", "string", "Source"), Field("text", "Note", "text", "")] },
            new() { Id = "header", Title = "Page header", Component = "HeaderBlock", Fields = [Field("eyebrow", "Eyebrow", "string", ""), Field("title", "Title", "string", "A new chapter"), Field("text", "Introduction", "text", "")] },
            new() { Id = "footer", Title = "Page footer", Component = "FooterBlock", Fields = [Field("text", "Text", "text", ""), Field("url", "Link", "url", ""), Field("label", "Link label", "string", "")] },
            new() { Id = "banner", Title = "Feature banner", Component = "BannerBlock", Fields = [Field("eyebrow", "Eyebrow", "string", ""), Field("title", "Title", "string", "Something worth discovering"), Field("text", "Text", "text", ""), Field("src", "Image", "media", ""), Field("alt", "Image description", "string", ""), Field("url", "Link", "url", ""), Field("label", "Link label", "string", "")] },
            new() { Id = "divider", Title = "Divider", Component = "DividerBlock", Fields = [Field("label", "Label", "string", ""), Select("style", "Style", ["line", "space", "dots"], "line")] },
            new() { Id = "form", Title = "Contact form", Component = "FormBlock", Fields = [Field("title", "Title", "string", "Start a conversation"), Field("description", "Description", "text", "A local form demonstration. Nothing is sent."), Field("nameLabel", "Name label", "string", "Name"), Field("emailLabel", "Email label", "string", "Email"), Field("messageLabel", "Message label", "string", "Message"), Field("submitLabel", "Button label", "string", "Preview message")] },
            new() { Id = "map", Title = "Location map", Component = "MapBlock", Fields = [Field("title", "Title", "string", "A place to explore"), Number("latitude", "Latitude", 45.52, -85, 85), Number("longitude", "Longitude", -122.68, -180, 180), Number("zoom", "Zoom", 11, 1, 18), Field("caption", "Caption", "text", "")] },
            new() { Id = "embed", Title = "External embed", Component = "EmbedBlock", Fields = [Field("url", "URL", "url", ""), Field("title", "Title", "string", "External resource"), Field("caption", "Caption", "text", "External content opens only when requested.")] },
            new() { Id = "tool", Title = "Interactive tool", Component = "ToolBlock", Kind = "javascript", Fields = [Field("title", "Title", "string", "Focus timer"), Select("mode", "Tool", ["timer", "calculator", "checklist"], "timer"), Number("initial", "Initial value", 300, 0, 3600), Field("items", "Checklist items", "text", "Choose a task\nMake a first attempt\nReview what changed")] }
        ],
        Appearance =
        [
            Field("accent", "Accent", "color", "#376954"), Field("background", "Full page background", "color", "#f5f3ed"),
            Field("ink", "Text color", "color", "#202b26"), Field("surface", "Panels", "color", "#ffffff"), Field("muted", "Secondary text", "color", "#647069"),
            Select("font", "Body font", SafeFonts, "system"), Select("headingFont", "Heading font", SafeFonts, "serif"),
            StandardAppearance.FontSize(),
            Select("variant", "Layout design", BaseTemplateCatalog.Ids, BaseTemplateCatalog.DefaultStarterId)
        ]
        };
        foreach (var block in definition.Blocks)
            block.Fields.AddRange([
                Select("span", "Layout width", ["full", "half", "third", "two-thirds"]),
                Select("font", "Font", SafeFonts), Select("align", "Alignment", ["left", "center", "right", "justify"]),
                new() { Id = "color", Title = "Text color", Type = "color" }
            ]);
        return definition;
    }

    private static FieldDefinition Field(string id, string title, string type, string value)
        => new() { Id = id, Title = title, Type = type, DefaultValue = ContentJson.Value(value) };
    private static FieldDefinition Number(string id, string title, double value, double min, double max)
        => new() { Id = id, Title = title, Type = "number", DefaultValue = ContentJson.Value(value), Min = min, Max = max };
    private static FieldDefinition Select(string id, string title, IEnumerable<string> options, string? value = null)
        => new() { Id = id, Title = title, Type = "select", Options = options.ToList(), DefaultValue = value is null ? null : ContentJson.Value(value) };
}
