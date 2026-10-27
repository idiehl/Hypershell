using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperLib.Core;

public static partial class ContentRules
{
    private static readonly Dictionary<string, int> Ranks = new(StringComparer.Ordinal)
        { ["collection"] = 1, ["series"] = 2, ["work"] = 3, ["volume"] = 4, ["part"] = 5, ["chapter"] = 6, ["page"] = 7 };

    public static void Validate(LibraryState state, Func<string, TemplateDefinition?>? templateResolver = null)
    {
        TemplateValidator.Require(state.SchemaVersion == 1, "Unsupported library schema version.");
        TemplateValidator.ValidateId(state.LibraryId, "library");
        TemplateValidator.Require(state.Revision >= 0, "Library revision cannot be negative.");
        TemplateValidator.Require(state.Nodes is { Count: <= 100_000 } && state.Pages is not null, "Invalid library content.");
        TemplateValidator.Require(state.Nodes.All(node => node is not null) && state.Pages.Values.All(page => page is not null), "Nodes and pages cannot be null.");
        ValidateLabels(state.Labels);
        ValidateAppearance(state.Appearance);
        ValidateAppearanceSchema(templateResolver?.Invoke("library"), state.Appearance, "Library appearance");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { state.LibraryId };
        foreach (var node in state.Nodes)
        {
            ValidateNodeId(node.Id);
            TemplateValidator.Require(ids.Add(node.Id), $"Duplicate node ID '{node.Id}'.");
            TemplateValidator.Require(node.Type is not null && Ranks.ContainsKey(node.Type), $"Unknown node type '{node.Type}'.");
            TemplateValidator.Require(node.StarterId is null || node.Type == "work", "Only Works may reference a reusable template.");
            if (node.StarterId is not null) ValidateNodeId(node.StarterId);
            ValidateTitle(node.Title);
            TemplateValidator.Require(double.IsFinite(node.Order), "Node order must be finite.");
            TemplateValidator.Require(TemplateValidator.IsSafeUrl(node.Cover), "Cover must be a safe HTTP(S) or rooted media URL.");
            ValidateAppearance(node.Appearance);
        }
        var nodes = state.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var node in state.Nodes)
        {
            ValidateParent(state, node, nodes);
            var work = FindWork(state, node, nodes);
            ValidateAppearanceSchema(templateResolver?.Invoke(work?.Id ?? "library"), node.Appearance, $"appearance of '{node.Title}'");
            TemplateValidator.Require(node.WorkId == work?.Id, $"Incorrect Work reference on '{node.Id}'.");
            TemplateValidator.Require(node.Type != "work" || node.ProjectPath == $"Works/{node.Id}", "Work source paths are derived from stable IDs.");
            TemplateValidator.Require(node.Type == "work" || node.ProjectPath is null, "Only Works have source projects.");
            if (node.Type == "page")
            {
                TemplateValidator.Require(state.Pages.TryGetValue(node.Id, out var page), $"Page '{node.Id}' has no content document.");
                ValidatePageDocument(page!);
                TemplateValidator.Require(page!.Id == node.Id && page.WorkId == work?.Id && page.Title == node.Title,
                    $"Page document '{node.Id}' does not match its hierarchy node.");
                var definition = templateResolver?.Invoke(page.WorkId);
                if (definition is not null) TemplateValidator.ValidatePage(definition, page);
            }
        }
        TemplateValidator.Require(state.Pages.Keys.All(id => nodes.TryGetValue(id, out var node) && node.Type == "page"),
            "Every page document must have a page node inside a Work.");
    }

    internal static void DerivePaths(LibraryState state)
    {
        var nodes = state.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in state.Nodes) Derive(node);
        void Derive(LibraryNode node)
        {
            if (done.Contains(node.Id)) return;
            TemplateValidator.Require(visiting.Add(node.Id), "Hierarchy cycles are not allowed.");
            ValidateParent(state, node, nodes);
            var parentPath = "Library";
            if (node.ParentId != state.LibraryId)
            {
                var parent = nodes[node.ParentId!];
                Derive(parent);
                parentPath = parent.DirectoryPath!;
            }
            node.WorkId = FindWork(state, node, nodes)?.Id;
            node.ProjectPath = node.Type == "work" ? $"Works/{node.Id}" : null;
            node.DirectoryPath = $"{parentPath}/{Slug(node.Title)}--{node.Id}" + (node.Type == "page" ? ".json" : "");
            visiting.Remove(node.Id);
            done.Add(node.Id);
        }
    }

    internal static LibraryNode? FindWork(LibraryState state, LibraryNode node, Dictionary<string, LibraryNode>? nodes = null)
    {
        nodes ??= state.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            TemplateValidator.Require(visited.Add(node.Id), "Hierarchy cycles are not allowed.");
            if (node.Type == "work") return node;
            if (node.ParentId == state.LibraryId || node.ParentId is null) return null;
            if (!nodes.TryGetValue(node.ParentId, out node!)) throw new ContentValidationException("The parent node does not exist.");
        }
    }

    internal static void ValidatePageDocument(PageDocument page)
    {
        TemplateValidator.ValidateId(page.Id, "page");
        TemplateValidator.ValidateId(page.WorkId, "work");
        TemplateValidator.ValidateId(page.TemplateId, "template");
        ValidateTitle(page.Title);
        TemplateValidator.Require(page.Revision >= 0 && page.Blocks is { Count: <= 1000 } && page.Sources is { Count: <= 2000 }, "Invalid page content.");
        TemplateValidator.Require(page.Blocks.All(block => block is not null) && page.Sources.All(source => source is not null), "Blocks and sources cannot be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in page.Blocks)
        {
            TemplateValidator.ValidateId(block.Id, "block");
            TemplateValidator.ValidateId(block.Type, "block type");
            TemplateValidator.ValidateId(block.Region, "region");
            TemplateValidator.Require(ids.Add(block.Id), $"Duplicate block '{block.Id}'.");
            TemplateValidator.Require(block.Values is { Count: <= 200 }, "A block has too many fields.");
            foreach (var value in block.Values)
            {
                TemplateValidator.ValidateId(value.Key, "field");
                TemplateValidator.Require(value.Value.ValueKind != JsonValueKind.Undefined && value.Value.GetRawText().Length <= 400_000, "Invalid or oversized block value.");
            }
        }
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in page.Sources)
        {
            TemplateValidator.ValidateId(source.Id, "source");
            TemplateValidator.Require(sourceIds.Add(source.Id), $"Duplicate source '{source.Id}'.");
            TemplateValidator.Require(!string.IsNullOrWhiteSpace(source.Url) && TemplateValidator.IsSafeUrl(source.Url, false), "Sources must have an HTTP(S) URL.");
            TemplateValidator.Require(!string.IsNullOrWhiteSpace(source.Title) && source.Title.Length <= 1000, "Sources need a title of at most 1,000 characters.");
            TemplateValidator.Require(source.RetrievedAt != default, "Sources need a retrieval timestamp.");
            TemplateValidator.Require(source.Excerpt is null || source.Excerpt.Length <= 20_000, "Source excerpt exceeds 20,000 characters.");
            TemplateValidator.Require(source.BlockIds is not null && source.BlockIds.All(ids.Contains), "Source citation references a missing block.");
            TemplateValidator.Require((source.Start is null && source.End is null) || (source.Start >= 0 && source.End >= source.Start), "Source citation offsets are invalid.");
        }
    }

    internal static void ValidateTitle(string? title)
        => TemplateValidator.Require(!string.IsNullOrWhiteSpace(title) && title.Length <= 200 && !title.Any(char.IsControl), "Titles need 1–200 printable characters.");

    internal static void ValidateNodeId(string id)
    {
        TemplateValidator.ValidateId(id, "node");
        TemplateValidator.Require(!WindowsDevice().IsMatch(id), "Reserved Windows device names cannot be IDs.");
    }

    internal static void ValidateAppearance(Dictionary<string, JsonElement> appearance)
    {
        TemplateValidator.Require(appearance is { Count: <= 100 }, "Invalid appearance settings.");
        foreach (var item in appearance)
        {
            TemplateValidator.ValidateId(item.Key, "appearance field");
            TemplateValidator.Require(item.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null,
                "Appearance settings must be simple declared values, not executable code or nested objects.");
            TemplateValidator.Require(item.Value.GetRawText().Length <= 4096, "Appearance value is too long.");
        }
    }

    private static void ValidateAppearanceSchema(TemplateDefinition? template, Dictionary<string, JsonElement> appearance, string context)
    {
        if (template is null) return;
        TemplateValidator.ValidateDefinition(template);
        TemplateValidator.ValidateValues(StandardAppearance.WithDefaults(template.Appearance), appearance, context);
    }

    internal static void ValidateLabels(Dictionary<string, string> labels)
    {
        TemplateValidator.Require(labels is not null && labels.Count <= 8, "Invalid hierarchy labels.");
        foreach (var pair in labels)
            TemplateValidator.Require((pair.Key == "library" || Ranks.ContainsKey(pair.Key)) && !string.IsNullOrWhiteSpace(pair.Value)
                && pair.Value.Length <= 60 && !pair.Value.Any(char.IsControl), "Labels must name a known hierarchy level and have 1–60 printable characters.");
    }

    private static void ValidateParent(LibraryState state, LibraryNode node, Dictionary<string, LibraryNode> nodes)
    {
        TemplateValidator.Require(node.Type is not null, "Node type is required.");
        TemplateValidator.Require(Ranks.TryGetValue(node.Type, out var rank), $"Unknown node type '{node.Type}'.");
        if (node.ParentId == state.LibraryId)
        {
            TemplateValidator.Require(node.Type == "collection", "Only Collections may be direct children of the Library.");
            return;
        }
        TemplateValidator.Require(node.ParentId is not null && nodes.TryGetValue(node.ParentId, out _), $"Parent for '{node.Title}' does not exist.");
        var parent = nodes[node.ParentId!];
        // Collections may group existing Collections without changing any descendant identity.
        // DerivePaths and FindWork still detect cycles; lower hierarchy levels keep their rules.
        if (node.Type == "collection")
        {
            TemplateValidator.Require(parent.Type == "collection", "Collections must belong to the Library or another Collection.");
            return;
        }
        TemplateValidator.Require(Ranks.TryGetValue(parent.Type, out var parentRank) && parentRank < rank, "Hierarchy levels must follow Collection > Series > Work > Volume > Part > Chapter > Page.");
        if (node.Type == "series") TemplateValidator.Require(parent.Type == "collection", "Series must belong to a Collection.");
        if (node.Type == "work") TemplateValidator.Require(parent.Type is "collection" or "series", "Works must belong to a Collection or Series.");
        if (rank > 3) TemplateValidator.Require(parentRank >= 3, "Pages and their optional organizational levels must belong to a Work.");
    }

    private static string Slug(string title)
    {
        var slug = NonSlug().Replace(title.ToLowerInvariant(), "-").Trim('-');
        return (slug.Length == 0 ? "untitled" : slug)[..Math.Min(slug.Length == 0 ? 8 : slug.Length, 40)];
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)] private static partial Regex NonSlug();
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex WindowsDevice();
}
