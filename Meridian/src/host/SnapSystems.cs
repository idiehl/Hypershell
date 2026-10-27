using System.Text.Json.Nodes;

namespace Meridian.Host;

/// <summary>Edge relationships are inert links; locked components are derived clusters.</summary>
internal static class SnapSystems
{
    private static string? Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static void ValidateLink(JsonObject link, Dictionary<string, JsonObject> nodes, HashSet<string> pairs)
    {
        if (!link.TryGetPropertyValue("snap", out var value)) return;
        if (value is not JsonObject snap || snap.Count != 1 || !snap.ContainsKey("locked")
            || snap["locked"] is not JsonValue flag || !flag.TryGetValue<bool>(out _))
            throw new ApiException(400, "Snapped edge settings contain only a Boolean locked flag.");
        if (Text(link, "kind") != "visual" || Text(link, "relation") != "generic" || Text(link, "direction") != "none"
            || new[] { "targetPort", "sourceAnchor", "targetAnchor", "points" }.Any(link.ContainsKey))
            throw new ApiException(400, "Snapped edges are undirected generic visual links without route points or ports.");
        var sourceId = Text(link, "sourceId")!; var targetId = Text(link, "targetId")!;
        var source = nodes[sourceId]; var target = nodes[targetId];
        if (sourceId == targetId || Text(source, "kind") == "canvas" || Text(target, "kind") == "canvas"
            || Text(source, "canvasId") != Text(target, "canvasId"))
            throw new ApiException(400, "Snapped edges need distinct non-root nodes in one Work.");
        var first = Ancestors(source, nodes); var second = Ancestors(target, nodes);
        if (first.Any(node => Text(node, "id") == targetId) || second.Any(node => Text(node, "id") == sourceId)
            || OwnsPlacement(first.FirstOrDefault()) || OwnsPlacement(second.FirstOrDefault())
            || TextOrNull(first.FirstOrDefault(Encloses)) != TextOrNull(second.FirstOrDefault(Encloses)))
            throw new ApiException(400, "Snapped edges need separate freely positioned nodes in the same containment space.");
        var ordered = new[] { sourceId, targetId }.Order(StringComparer.Ordinal).ToArray();
        var key = System.Text.Json.JsonSerializer.Serialize(ordered);
        if (!pairs.Add(key)) throw new ApiException(400, "Two nodes share at most one snapped edge relationship.");
    }

    private static string? TextOrNull(JsonObject? node) => node is null ? null : Text(node, "id");
    private static bool Encloses(JsonObject node) => Text(node, "kind") is "container" or "spreadsheet" or "module";
    private static bool OwnsPlacement(JsonObject? parent) => parent is not null && (Text(parent, "kind") == "spreadsheet"
        || Text(parent, "containerVariant") is "horizontal" or "vertical" or "grid" or "spreadsheet" or "window" or "gallery");
    private static List<JsonObject> Ancestors(JsonObject node, Dictionary<string, JsonObject> nodes)
    {
        var result = new List<JsonObject>(); var seen = new HashSet<string> { Text(node, "id")! };
        var parentId = Text(node, "parentId");
        while (parentId is not null && seen.Add(parentId) && nodes.TryGetValue(parentId, out var parent))
        { result.Add(parent); parentId = Text(parent, "parentId"); }
        return result;
    }
}
