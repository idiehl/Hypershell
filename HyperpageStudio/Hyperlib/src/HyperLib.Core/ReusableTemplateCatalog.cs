using System.Text.Json;

namespace HyperLib.Core;

public sealed record ReusableTemplateInfo(string Id, string Title, string Description, string Category, bool IsBuiltIn)
{
    public string DemoTitle { get; init; } = "";
    public bool IsOriginal { get; init; }
    public string Background { get; init; } = "";
    public string Accent { get; init; } = "";
    public string Ink { get; init; } = "";
}

/// <summary>The shipped designs and complete local templates available to create independent Works.</summary>
public static class ReusableTemplateCatalog
{
    public const string BlankId = "blank";
    private const int MaximumJsonBytes = 32 * 1024 * 1024;

    public static IReadOnlyList<ReusableTemplateInfo> List(string libraryRoot)
    {
        var entries = new List<ReusableTemplateInfo>
        {
            new(BlankId, "Blank", "Start with an empty page and the standard elements.", "Essentials", true)
        };
        entries.AddRange(BaseTemplateCatalog.List().Select(FromBuiltIn));
        var directory = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, "Templates"));
        if (!Directory.Exists(directory)) return entries;
        foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(child).StartsWith('.')) continue;
            TemplatePathGuard.NoLinks(child);
            var metadata = Path.Combine(child, "template.json");
            if (!File.Exists(metadata)) continue;
            entries.Add(ReadCustom(libraryRoot, Path.GetFileName(child)));
        }
        return entries;
    }

    public static bool IsBuiltIn(string id) => id == BlankId || BaseTemplateCatalog.Contains(id);

    public static ReusableTemplateInfo Find(string libraryRoot, string id)
    {
        ContentRules.ValidateNodeId(id);
        if (id == BlankId) return new(id, "Blank", "Start with an empty page and the standard elements.", "Essentials", true);
        if (BaseTemplateCatalog.List().FirstOrDefault(item => item.Id == id) is { } builtIn)
            return FromBuiltIn(builtIn);
        return ReadCustom(libraryRoot, id);
    }

    private static ReusableTemplateInfo FromBuiltIn(BaseTemplateInfo item)
        => new(item.Id, item.Title, item.Description, item.Category, true)
        { DemoTitle = item.DemoTitle, IsOriginal = item.IsOriginal, Background = item.Background, Accent = item.Accent, Ink = item.Ink };

    public static string GetProjectRoot(string libraryRoot, string id)
    {
        ContentRules.ValidateNodeId(id);
        return TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, IsBuiltIn(id) ? "BaseTemplates" : "Templates", id, "Project"));
    }

    public static TemplateDefinition ReadDefinition(string libraryRoot, string id)
    {
        _ = Find(libraryRoot, id);
        var path = TemplatePathGuard.Within(libraryRoot, Path.Combine(GetProjectRoot(libraryRoot, id), "authoring.template.json"));
        if (File.Exists(path)) return ReadJson<TemplateDefinition>(path, value => TemplateValidator.ValidateDefinition(value));
        if (IsBuiltIn(id)) return CreateBuiltInDefinition(id);
        throw new ContentValidationException($"Reusable template '{id}' has no authoring definition.");
    }

    public static TemplateDefinition CreateBuiltInDefinition(string id)
    {
        if (id != BlankId) return BaseTemplateCatalog.CreateTemplate(id);
        var definition = StarterTemplate.Create();
        definition.Title = "Blank";
        definition.EmptyPageDefault = true;
        definition.DefaultBlocks = [];
        foreach (var field in definition.Blocks.SelectMany(block => block.Fields))
            if (field.Type is "string" or "text" && field.Id is "title" or "text" or "description" or "items")
                field.DefaultValue = ContentJson.Value("");
        var defaults = new Dictionary<string, string>
        {
            ["background"] = "#ffffff", ["accent"] = "#36617c", ["ink"] = "#202124",
            ["surface"] = "#f5f6f7", ["muted"] = "#687078", ["font"] = "system", ["headingFont"] = "system"
        };
        foreach (var field in definition.Appearance)
            if (defaults.TryGetValue(field.Id, out var value)) field.DefaultValue = ContentJson.Value(value);
        TemplateValidator.ValidateDefinition(definition);
        return definition;
    }

    private static ReusableTemplateInfo ReadCustom(string libraryRoot, string id)
    {
        ContentRules.ValidateNodeId(id);
        TemplateValidator.Require(!IsBuiltIn(id), "A local template cannot replace a built-in template identity.");
        var entryRoot = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, "Templates", id));
        var item = ReadJson<ReusableTemplateInfo>(Path.Combine(entryRoot, "template.json"), value =>
        {
            TemplateValidator.Require(value.Id == id && !value.IsBuiltIn, "The reusable template identity is invalid.");
            ContentRules.ValidateTitle(value.Title);
            TemplateValidator.Require(value.Description is { Length: <= 2000 } && value.Category is { Length: <= 100 }, "Invalid template metadata.");
        });
        TemplateValidator.Require(File.Exists(TemplatePathGuard.Within(libraryRoot, Path.Combine(entryRoot, "Project", "visual-page.project.json")))
            && File.Exists(TemplatePathGuard.Within(libraryRoot, Path.Combine(entryRoot, "Project", "authoring.template.json"))),
            $"Reusable template '{item.Title}' is incomplete.");
        return item;
    }

    internal static T ReadJson<T>(string path, Action<T>? validate = null)
    {
        TemplatePathGuard.NoLinks(path);
        TemplateValidator.Require(File.Exists(path), "The reusable template was not found. Refresh the template list.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        TemplateValidator.Require(stream.Length <= MaximumJsonBytes, "The template JSON exceeds the 32 MiB limit.");
        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(stream, ContentJson.Options)
                ?? throw new ContentValidationException("The reusable template file is empty.");
        }
        catch (JsonException error)
        {
            throw new ContentValidationException($"The reusable template file is malformed: {error.Message}");
        }
        validate?.Invoke(value);
        return value;
    }
}
