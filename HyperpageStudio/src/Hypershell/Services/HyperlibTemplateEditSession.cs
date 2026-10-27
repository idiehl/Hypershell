using System.Globalization;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

public enum HyperlibDefaultMode { Inherit, Value, Null }

/// <summary>A lossless draft of a Work's authoring contract; it never edits page content.</summary>
public sealed class HyperlibTemplateEditSession
{
    public HyperlibTemplateEditSession(TemplateDefinition definition) => Draft = CloneDefinition(definition);
    public TemplateDefinition Draft { get; }

    public TemplateDefinition CreateDefinition() => CloneDefinition(Draft);
    public void Validate() => TemplateValidator.ValidateDefinition(Draft);

    public BlockDefinition AddElement(BlockDefinition preset)
    {
        var block = CloneElement(preset);
        block.Id = NextId(preset.Id, Draft.Blocks.Select(item => item.Id));
        Draft.Blocks.Add(block);
        if (Draft.Regions.FirstOrDefault() is { AllowedBlocks.Count: > 0 } first)
            first.AllowedBlocks.Add(block.Id);
        return block;
    }

    public BlockDefinition DuplicateElement(string id)
    {
        var original = Draft.Blocks.Single(block => block.Id == id);
        var block = CloneElement(original);
        block.Id = NextId(original.Id, Draft.Blocks.Select(item => item.Id));
        block.Title += " copy";
        Draft.Blocks.Add(block);
        foreach (var region in Draft.Regions.Where(region => region.AllowedBlocks.Contains(id)))
            region.AllowedBlocks.Add(block.Id);
        return block;
    }

    public void RemoveElement(string id)
    {
        if (Draft.DefaultBlocks.Any(block => block.Type == id))
            throw new ContentValidationException("Remove this element from New page defaults before removing its definition.");
        if (Draft.Regions.FirstOrDefault(region => region.AllowedBlocks.Count == 1 && region.AllowedBlocks[0] == id) is { } restrictedRegion)
            throw new ContentValidationException($"Choose another allowed element for '{restrictedRegion.Title}', or enable Allow all, before removing its only allowed element.");
        Draft.Blocks.RemoveAll(block => block.Id == id);
        foreach (var region in Draft.Regions) region.AllowedBlocks.Remove(id);
    }

    public PageBlock AddDefaultBlock(string type, string region)
    {
        var schema = Draft.Blocks.Single(block => block.Id == type);
        var target = Draft.Regions.Single(item => item.Id == region);
        if (target.AllowedBlocks.Count > 0 && !target.AllowedBlocks.Contains(type))
            throw new ContentValidationException($"{schema.Title} is not allowed in {target.Title}.");
        var block = new PageBlock
        {
            Id = NextId(type, Draft.DefaultBlocks.Select(item => item.Id)), Type = type, Region = region,
            Values = schema.Fields.Where(field => field.DefaultValue.HasValue)
                .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal)
        };
        Draft.DefaultBlocks.Add(block);
        return block;
    }

    public void RemoveDefaultBlock(string id)
    {
        if (Draft.DefaultBlocks.RemoveAll(block => block.Id == id) > 0 && Draft.DefaultBlocks.Count == 0)
            Draft.EmptyPageDefault = true;
    }
    public void MoveDefaultBlock(string id, int delta)
    {
        var index = Draft.DefaultBlocks.FindIndex(block => block.Id == id);
        if (index < 0) return;
        var destination = Math.Clamp(index + delta, 0, Draft.DefaultBlocks.Count - 1);
        var block = Draft.DefaultBlocks[index];
        Draft.DefaultBlocks.RemoveAt(index);
        Draft.DefaultBlocks.Insert(destination, block);
    }

    public TemplateRegion AddRegion()
    {
        var region = new TemplateRegion { Id = NextId("region", Draft.Regions.Select(item => item.Id)), Title = "New region" };
        Draft.Regions.Add(region);
        return region;
    }

    public void RemoveRegion(string id)
    {
        if (Draft.Regions.Count == 1) throw new ContentValidationException("Keep at least one content region.");
        if (Draft.DefaultBlocks.Any(block => block.Region == id))
            throw new ContentValidationException("Move the default blocks out of this region before removing it.");
        Draft.Regions.RemoveAll(region => region.Id == id);
    }

    public static FieldDefinition AddCustomField(ICollection<FieldDefinition> fields)
    {
        var field = new FieldDefinition { Id = NextId("field", fields.Select(item => item.Id)), Title = "New field", Type = "string" };
        fields.Add(field);
        return field;
    }

    public static IReadOnlyList<FieldDefinition> SupportedAppearancePresets() => ContentJson.Clone(StarterTemplate.Create().Appearance);
    public static IReadOnlyList<FieldDefinition> SupportedElementStylePresets() =>
        ContentJson.Clone(StarterTemplate.Create().Blocks[0].Fields.Where(field => IsSupportedElementStyle(field.Id)).ToList());
    public static bool IsSupportedElementStyle(string id) => id is "span" or "font" or "align" or "color";

    public static void AddSupportedField(ICollection<FieldDefinition> fields, FieldDefinition preset)
    {
        if (fields.Any(field => field.Id == preset.Id)) return;
        fields.Add(CloneField(preset));
    }

    public static TemplateDefinition ParseDefinition(string json)
    {
        var definition = JsonSerializer.Deserialize<TemplateDefinition>(json, ContentJson.Options)
            ?? throw new ContentValidationException("The HyperLib template is empty.");
        using var document = JsonDocument.Parse(json);
        static void RestoreDefaults(JsonElement parent, string property, List<FieldDefinition> fields)
        {
            if (!parent.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
            var index = 0;
            foreach (var value in values.EnumerateArray())
            {
                if (index >= fields.Count) break;
                if (value.TryGetProperty("defaultValue", out var raw)) fields[index].DefaultValue = raw.Clone();
                index++;
            }
        }
        RestoreDefaults(document.RootElement, "appearance", definition.Appearance);
        if (document.RootElement.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                if (index >= definition.Blocks.Count) break;
                RestoreDefaults(block, "fields", definition.Blocks[index++].Fields);
            }
        }
        return definition;
    }

    public static TemplateDefinition CloneDefinition(TemplateDefinition definition)
    {
        var clone = ContentJson.Clone(definition);
        clone.Blocks = definition.Blocks.Select(CloneElement).ToList();
        clone.Appearance = definition.Appearance.Select(CloneField).ToList();
        return clone;
    }

    public static BlockDefinition CloneElement(BlockDefinition block)
    {
        var clone = ContentJson.Clone(block);
        clone.Fields = block.Fields.Select(CloneField).ToList();
        return clone;
    }

    private static FieldDefinition CloneField(FieldDefinition field)
    {
        var clone = ContentJson.Clone(field);
        clone.DefaultValue = field.DefaultValue?.Clone();
        return clone;
    }

    public static HyperlibDefaultMode DefaultMode(FieldDefinition field) => field.DefaultValue is not { } value
        ? HyperlibDefaultMode.Inherit : value.ValueKind == JsonValueKind.Null ? HyperlibDefaultMode.Null : HyperlibDefaultMode.Value;
    public static string DefaultText(FieldDefinition field) => field.DefaultValue is { } value && value.ValueKind != JsonValueKind.Null
        ? ValueText(value) : "";
    public static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();

    public static void SetDefault(FieldDefinition field, HyperlibDefaultMode mode, string text) => field.DefaultValue = mode switch
    {
        HyperlibDefaultMode.Inherit => null,
        HyperlibDefaultMode.Null => ContentJson.Value<object?>(null),
        _ => ParseValue(field.Type, text)
    };

    public static JsonElement ParseValue(string type, string text) => type switch
    {
        "number" => ContentJson.Value(ParseFiniteNumber(text)),
        "boolean" => ContentJson.Value(bool.TryParse(text, out var value) ? value
            : throw new FormatException("Use true or false for a boolean value.")),
        _ => ContentJson.Value(text)
    };

    public static JsonElement InitialValue(FieldDefinition field) => field.DefaultValue?.Clone() ?? (field.Type switch
    {
        "number" => ContentJson.Value(Math.Max(field.Min ?? double.MinValue, Math.Min(field.Max ?? double.MaxValue, 0d))),
        "boolean" => ContentJson.Value(false),
        "color" => ContentJson.Value("#A3C7BD"),
        "select" => ContentJson.Value(field.Options.FirstOrDefault() ?? ""),
        _ => ContentJson.Value("")
    });

    private static double ParseFiniteNumber(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new FormatException("Enter a finite number, using a period for decimals.");
        return number;
    }

    private static string NextId(string basis, IEnumerable<string> existing)
    {
        var used = existing.ToHashSet(StringComparer.Ordinal);
        var stem = string.IsNullOrEmpty(basis) ? "element" : basis[..Math.Min(90, basis.Length)];
        if (!used.Contains(stem)) return stem;
        for (var index = 2; ; index++)
        {
            var candidate = stem + "-" + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
