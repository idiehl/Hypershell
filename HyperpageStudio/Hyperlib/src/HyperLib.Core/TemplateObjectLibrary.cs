using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperLib.Core;

public sealed record TemplateDesignGroup(string Id, string Title, string Description, string Category,
    string? SourceTemplateId, string ProjectRoot);

public sealed class TemplateLibraryObject
{
    public required string Id { get; init; }
    public required string GroupId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Kind { get; init; }
    public required string CatalogId { get; init; }
    public string? SourceTemplateId { get; init; }
    public string? SourceLibrary { get; init; }
    public BlockDefinition? Element { get; init; }
    public List<PageBlock> Blocks { get; init; } = [];
    public List<TemplateRegion> Regions { get; init; } = [];
    public List<FieldDefinition> Appearance { get; init; } = [];
    public required TemplateDefinition Definition { get; init; }
}

/// <summary>Independent, editable Design Group snapshots. Reading or importing never changes a source template or Work.</summary>
public static class TemplateObjectLibrary
{
    private sealed record GroupMetadata(int SchemaVersion, string Id, string Title, string Description, string Category, string? SourceTemplateId);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public const string DirectoryName = "CoreLibrary";

    public static async Task EnsureCoreLibraryAsync(string libraryRoot, CancellationToken ct = default)
    {
        var root = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, DirectoryName, "DesignGroups"));
        var gate = Gates.GetOrAdd(root, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(root);
            foreach (var item in ReusableTemplateCatalog.List(libraryRoot))
            {
                ct.ThrowIfCancellationRequested();
                var destination = TemplatePathGuard.Within(libraryRoot, Path.Combine(root, item.Id));
                if (Directory.Exists(destination)) { _ = GetDesignGroup(libraryRoot, item.Id); continue; }
                var source = await ReusableTemplateManager.OpenProjectAsync(libraryRoot, item.Id, ct);
                await SnapshotAsync(libraryRoot, source, new(1, item.Id, item.Title, item.Description, item.Category, item.Id), ct);
            }
            await TemplatePathGuard.WriteNewAsync(Path.Combine(libraryRoot, DirectoryName, "README.md"), Encoding.UTF8.GetBytes(
                "# Core Template Library\n\nDesign Groups are independent copies of reusable Template projects. Browse their elements, configured examples, page layouts, colors, typography, and Work styles in Hyperlib Editor. Adding an object copies its required local source into the destination Template. Original Templates and existing Works are preserved. Group snapshots are not silently refreshed from their originals. Save a Template as a new Design Group to publish another collection.\n"), ct);
        }
        finally { gate.Release(); }
    }

    public static IReadOnlyList<TemplateDesignGroup> ListDesignGroups(string libraryRoot)
    {
        var root = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, DirectoryName, "DesignGroups"));
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root).Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Select(path => GetDesignGroup(libraryRoot, Path.GetFileName(path)))
            .OrderBy(group => group.Category, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static TemplateDesignGroup GetDesignGroup(string libraryRoot, string id)
    {
        ContentRules.ValidateNodeId(id);
        var root = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, DirectoryName, "DesignGroups", id));
        var metadata = ReusableTemplateCatalog.ReadJson<GroupMetadata>(Path.Combine(root, "group.json"), item =>
        {
            TemplateValidator.Require(item.SchemaVersion == 1 && item.Id == id, "The Design Group identity is invalid.");
            ContentRules.ValidateTitle(item.Title);
            TemplateValidator.Require(item.Description is { Length: <= 2000 } && item.Category is { Length: <= 100 }, "Invalid Design Group metadata.");
        });
        var project = TemplatePathGuard.Within(libraryRoot, Path.Combine(root, "Project"));
        TemplateValidator.Require(File.Exists(Path.Combine(project, "visual-page.project.json")), "The Design Group project is incomplete.");
        _ = ReadDefinition(project);
        return new(id, metadata.Title, metadata.Description, metadata.Category, metadata.SourceTemplateId, project);
    }

    public static async Task<TemplateDesignGroup> SaveDesignGroupAsync(string libraryRoot, string projectRoot, string title, CancellationToken ct = default)
    {
        ContentRules.ValidateTitle(title);
        TemplatePathGuard.Within(libraryRoot, projectRoot);
        _ = ReadDefinition(projectRoot);
        var id = "group-" + Guid.NewGuid().ToString("N");
        await SnapshotAsync(libraryRoot, projectRoot, new(1, id, title.Trim(), "A collection saved from a Template.", "My Design Groups", null), ct);
        return GetDesignGroup(libraryRoot, id);
    }

    /// <summary>Publishes a named seed once; independently edited Design Groups are never replaced by a later seed run.</summary>
    public static async Task<TemplateDesignGroup> RegisterDesignGroupAsync(string libraryRoot, string projectRoot, string id,
        string title, string description, string category, string? sourceTemplateId = null, CancellationToken ct = default)
    {
        ContentRules.ValidateNodeId(id);
        ContentRules.ValidateTitle(title);
        TemplateValidator.Require(description is { Length: <= 2000 } && category is { Length: <= 100 }, "Invalid Design Group metadata.");
        TemplatePathGuard.Within(libraryRoot, projectRoot);
        var destination = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, DirectoryName, "DesignGroups", id));
        if (Directory.Exists(destination)) return GetDesignGroup(libraryRoot, id);
        _ = ReadDefinition(projectRoot);
        await SnapshotAsync(libraryRoot, projectRoot, new(1, id, title.Trim(), description, category, sourceTemplateId), ct);
        return GetDesignGroup(libraryRoot, id);
    }

    public static string CatalogIdFor(string groupId, string objectId)
    {
        var readable = Regex.Replace(objectId.ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        if (readable.Length > 38) readable = readable[..38].TrimEnd('-');
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(groupId + "/" + objectId)))[..10];
        return "HLC-" + readable + "-" + identity;
    }

    public static TemplateDefinition ReadDefinition(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "authoring.template.json");
        // Keep an explicit JSON null distinct from an absent/inherited default.
        var raw = ReusableTemplateCatalog.ReadJson<JsonElement>(path);
        var definition = raw.Deserialize<TemplateDefinition>(ContentJson.Options)
            ?? throw new ContentValidationException("The Template definition is empty.");
        TemplateObjectCloning.RestoreDefaults(raw, definition);
        TemplateValidator.ValidateDefinition(definition);
        return definition;
    }

    public static Task<ReusableTemplateInfo> CreateTemplateFromObjectsAsync(string libraryRoot, string title,
        IEnumerable<TemplateObjectSelection> selections, CancellationToken ct = default)
        => TemplateObjectImporter.CreateTemplateFromObjectsAsync(libraryRoot, title, selections, ct);

    private static async Task SnapshotAsync(string libraryRoot, string source, GroupMetadata metadata, CancellationToken ct)
    {
        var groups = TemplatePathGuard.Within(libraryRoot, Path.Combine(libraryRoot, DirectoryName, "DesignGroups"));
        var staging = TemplatePathGuard.Within(libraryRoot, Path.Combine(groups, ".pending-" + Guid.NewGuid().ToString("N")));
        var target = Path.Combine(staging, "Project");
        Directory.CreateDirectory(target);
        // Capture first and verify again before publishing, so concurrent edits cannot produce a mixed group.
        var observed = SourceFingerprints(source);
        var definition = ReadDefinition(source);
        await TemplateProjectScaffolder.CopyProjectSourceAsync(libraryRoot, source, target, ct);
        await TemplateProjectScaffolder.CopyProjectManifestAsync(libraryRoot, source, target, "group-" + metadata.Id[..Math.Min(90, metadata.Id.Length)], metadata.Title, ct);
        await TemplatePathGuard.WriteNewAsync(Path.Combine(target, "authoring.template.json"), JsonSerializer.SerializeToUtf8Bytes(definition, ContentJson.Options), ct);
        if (!observed.SequenceEqual(SourceFingerprints(source)))
            throw new ContentValidationException("The source Template changed while its Design Group was being captured. Save it, then try again.");
        await TemplatePathGuard.WriteNewAsync(Path.Combine(staging, "group.json"), JsonSerializer.SerializeToUtf8Bytes(metadata, ContentJson.Options), ct);
        ct.ThrowIfCancellationRequested();
        var destination = TemplatePathGuard.Within(libraryRoot, Path.Combine(groups, metadata.Id));
        try { Directory.Move(staging, destination); }
        catch (IOException) when (Directory.Exists(destination)) { _ = GetDesignGroup(libraryRoot, metadata.Id); }
    }

    private static IReadOnlyList<string> SourceFingerprints(string root)
        => TemplatePathGuard.Files(root).Where(path => TemplateProjectScaffolder.IsProjectSource(Path.GetRelativePath(root, path))
            || Path.GetFileName(path) is "authoring.template.json" or "visual-page.project.json")
            .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToList();

    public static IReadOnlyList<TemplateLibraryObject> ListObjects(string libraryRoot, string groupId)
    {
        var group = GetDesignGroup(libraryRoot, groupId);
        var definition = ReadDefinition(group.ProjectRoot);
        var result = new List<TemplateLibraryObject>();
        TemplateLibraryObject Item(string id, string title, string description, string category, string kind,
            BlockDefinition? element = null, List<PageBlock>? blocks = null, List<TemplateRegion>? regions = null,
            List<FieldDefinition>? appearance = null, TemplateDefinition? schema = null)
            => new() { Id = id, GroupId = groupId, Title = title, Description = description, Category = NormalizeCategory(category), Kind = kind,
                CatalogId = kind == "element" && !string.IsNullOrWhiteSpace(element?.CatalogId) ? element.CatalogId! : CatalogIdFor(groupId, id),
                SourceTemplateId = element?.SourceTemplateId ?? group.SourceTemplateId, SourceLibrary = element?.SourceLibrary,
                Element = element is null ? null : TemplateObjectCloning.Element(element), Blocks = ContentJson.Clone(blocks ?? []),
                Regions = ContentJson.Clone(regions ?? []), Appearance = (appearance ?? []).Select(TemplateObjectCloning.Field).ToList(), Definition = TemplateObjectCloning.Definition(schema ?? definition) };
        foreach (var block in definition.Blocks)
            result.Add(Item("element-" + block.Id, block.Title, block.Description ?? "Reusable element with editable fields. Uses the destination Template's colors and typography.", Category(block), "element", block));
        foreach (var example in definition.DefaultBlocks)
        {
            var block = definition.Blocks.Single(item => item.Id == example.Type);
            var title = example.Values.TryGetValue("title", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()! : block.Title + " example";
            result.Add(Item("example-" + example.Id, title, "Configured " + block.Title.ToLowerInvariant() + " from " + group.Title + ".", Category(block), "preset", block, [example]));
        }
        if (definition.DefaultBlocks.Count > 0)
            result.Add(Item("page-layout", group.Title + " page layout", "Use this arrangement as the starting layout for new Pages. Includes its structural style; color and font choices stay independent.", "Page layouts", "layout", blocks: definition.DefaultBlocks, regions: definition.Regions,
                appearance: definition.Appearance.Where(field => field.Id == "variant").ToList()));
        foreach (var region in definition.Regions)
            result.Add(Item("region-" + region.Id, region.Title + " region", "A named region and its allowed elements.", "Structure & navigation", "region", regions: [region]));
        var typography = definition.Appearance.Where(field => field.Id is "font" or "headingFont" or "fontSize").ToList();
        if (typography.Count > 0) result.Add(Item("typography", group.Title + " typography", "Body font, heading font, and reading size.", "Typography & fonts", "typography", appearance: typography));
        var palette = definition.Appearance.Where(field => field.Type == "color").ToList();
        if (palette.Count > 0) result.Add(Item("palette", group.Title + " colors", "Page, text, accent, panel, and secondary colors.", "Colors & surfaces", "palette", appearance: palette));
        if (definition.Appearance.Count > 0)
            result.Add(Item("work-style", group.Title + " Work style", "The complete shared appearance: structural style, colors, typography, and custom defaults.", "Work styles", "style", appearance: definition.Appearance));
        if (group.SourceTemplateId == ReusableTemplateCatalog.BlankId)
        {
            foreach (var font in StarterTemplate.SafeFonts)
            {
                var fields = typography.Where(field => field.Id is "font" or "headingFont").Select(TemplateObjectCloning.Field).ToList();
                foreach (var field in fields) field.DefaultValue = ContentJson.Value(font);
                result.Add(Item("font-" + font, char.ToUpperInvariant(font[0]) + font[1..] + " fonts", "A reusable body and heading font choice.", "Typography & fonts", "typography", appearance: fields));
            }
            foreach (var (id, title, spans) in new[] {
                ("single-column", "Single column", new[] { "full", "full", "full" }),
                ("two-columns", "Two columns", new[] { "full", "half", "half" }),
                ("three-columns", "Three columns", new[] { "full", "third", "third", "third" }),
                ("sidebar", "Article with sidebar", new[] { "full", "two-thirds", "third" }),
                ("blocks", "Block layout", new[] { "full", "half", "half", "full" }) })
            {
                var schema = TemplateObjectCloning.Definition(definition);
                schema.DefaultBlocks = spans.Select((span, index) => new PageBlock { Id = "layout-" + index, Type = index == 0 ? "header" : "text", Region = "main",
                    Values = index == 0 ? new() { ["title"] = ContentJson.Value("Your page title") }
                        : new() { ["text"] = ContentJson.Value(index == 1 ? "A place for your main ideas." : "Add supporting details here."), ["span"] = ContentJson.Value(span) } }).ToList();
                result.Add(Item("layout-" + id, title, "A simple reusable starting arrangement. Replace the example text when writing a Page.", "Page layouts", "layout",
                    blocks: schema.DefaultBlocks, regions: schema.Regions, schema: schema));
            }
        }
        return result;
    }

    public static string NormalizeCategory(string category) => category switch
    {
        "Data display" => "Data",
        "Forms & input" => "Forms",
        "Feedback & status" => "Status",
        "Typography & fonts" => "Typography",
        "Widgets & tools" => "Tools",
        "Content blocks" or "Content & publishing" => "Text & content",
        "Structure & navigation" => "Navigation",
        "Layout & structure" or "Layouts & structure" or "Structure" or "Page layouts" => "Layouts & structure",
        "Actions & conversion" or "Marketing & conversion" => "Calls to action",
        "Discovery & collections" => "Cards & collections",
        _ => category
    };

    private static string Category(BlockDefinition block) => !string.IsNullOrWhiteSpace(block.Category) ? block.Category : Regex.Replace(block.Component.Split('.').Last(), @"^(?:Hl[0-9a-f]{12}_)+", "", RegexOptions.CultureInvariant) switch
    {
        "TextBlock" or "RichTextBlock" or "CitationBlock" => "Text & content",
        "ImageBlock" or "GalleryBlock" or "AudioBlock" or "VideoBlock" => "Media",
        "HeaderBlock" or "FooterBlock" or "BannerBlock" => "Structure & navigation",
        "DividerBlock" or "CalloutBlock" => "Decoration",
        _ => "Widgets & tools"
    };
}

internal static class TemplateObjectCloning
{
    public static FieldDefinition Field(FieldDefinition field)
    {
        var copy = ContentJson.Clone(field);
        copy.DefaultValue = field.DefaultValue?.Clone();
        return copy;
    }
    public static BlockDefinition Element(BlockDefinition element)
    {
        var copy = ContentJson.Clone(element);
        copy.Fields = element.Fields.Select(Field).ToList();
        return copy;
    }
    public static TemplateDefinition Definition(TemplateDefinition definition)
    {
        var copy = ContentJson.Clone(definition);
        copy.Blocks = definition.Blocks.Select(Element).ToList();
        copy.Appearance = definition.Appearance.Select(Field).ToList();
        return copy;
    }
    public static void RestoreDefaults(JsonElement raw, TemplateDefinition definition)
    {
        static void Restore(JsonElement parent, string property, List<FieldDefinition> fields)
        {
            if (!parent.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) return;
            var index = 0;
            foreach (var field in list.EnumerateArray())
            {
                if (index >= fields.Count) break;
                if (field.TryGetProperty("defaultValue", out var value)) fields[index].DefaultValue = value.Clone();
                index++;
            }
        }
        Restore(raw, "appearance", definition.Appearance);
        if (raw.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                if (index >= definition.Blocks.Count) break;
                Restore(block, "fields", definition.Blocks[index++].Fields);
            }
        }
    }
}
