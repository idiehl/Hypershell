using System.Text.Json.Nodes;

namespace Meridian.Host;

/// <summary>Finite, declarative scalar systems. Keep in step with core/operations.ts.</summary>
internal static class NodeSystems
{
    public static readonly string[] Kinds = ["canvas", "container", "module", "spreadsheet", "media", "block", "text", "image", "document", "input", "number", "switch", "output", "joint", "junction", "reference", "canvas-link"];
    public static readonly string[] Operations = ["AND", "OR", "NOT", "NAND", "NOR", "XOR", "XNOR", "IDENTITY", "ADD", "SUBTRACT", "MULTIPLY", "DIVIDE", "MODULO", "POWER", "MIN", "MAX", "ABS", "NEGATE", "EQUAL", "GREATER", "LESS"];
    public static readonly string[] LinkKinds = ["visual", "functional", "numeric"];
    public static readonly string[] LinkRelations = ["causal", "chronological", "mutual", "cyclical", "generic"];
    private static readonly string[] BooleanOperations = ["AND", "OR", "NOT", "NAND", "NOR", "XOR", "XNOR"];
    private static readonly string[] Comparisons = ["EQUAL", "GREATER", "LESS"];
    private static string? Text(JsonObject node, string key) => node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static bool Scalar(JsonNode? node) => node is JsonValue v && (v.TryGetValue<bool>(out _) || v.TryGetValue<double>(out var number) && double.IsFinite(number));
    public static void ValidateNode(JsonObject node)
    {
        var kind = Text(node, "kind");
        if (!Kinds.Contains(kind)) throw new ApiException(400, "Unknown node kind.");
        if (node.ContainsKey("utilityVariant") && (kind != "junction" || Text(node, "utilityVariant") is not ("pivot" or "joint"))) throw new ApiException(400, "Utility sub-type must be Pivot or Joint on a routing node.");
        foreach (var name in new[] { "id", "canvasId", "title" })
            if (Text(node, name) is null) throw new ApiException(400, $"Node '{name}' must be text.");
        if (node.ContainsKey("tag") && (Text(node, "tag") is not { } tag || tag.Length > 8000))
            throw new ApiException(400, "Node tag must be text with at most 8000 characters.");
        if (!node.ContainsKey("parentId") || node["parentId"] is not null && Text(node, "parentId") is null) throw new ApiException(400, "Node parent must be an identifier or null.");
        foreach (var name in new[] { "text", "assetId", "operation", "targetId" })
            if (node.ContainsKey(name) && Text(node, name) is null) throw new ApiException(400, $"Node '{name}' must be text.");
        foreach (var name in new[] { "x", "y", "width", "height" })
            if (node[name] is not JsonValue coordinate || !coordinate.TryGetValue<double>(out var coordinateNumber) || !double.IsFinite(coordinateNumber)) throw new ApiException(400, "Node geometry must contain finite numbers.");
        if (node.ContainsKey("sizeMode") && Text(node, "sizeMode") is not ("auto" or "fixed")) throw new ApiException(400, "Node size mode must be auto or fixed.");
        if (node.ContainsKey("contentType") && Text(node, "contentType") is not ("text" or "asset" or "link")) throw new ApiException(400, "A node selects one primary content type: text, asset or link.");
        if (node.ContainsKey("childPosition") && Text(node, "childPosition") is not ("auto" or "manual")) throw new ApiException(400, "Child position must be auto or manual.");
        if (node.TryGetPropertyValue("childOffset", out var offsetValue))
        {
            if (offsetValue is not JsonObject offset || offset.Count != 2 || offset.Any(pair => pair.Key is not ("x" or "y"))
                || new[] { "x", "y" }.Any(axis => offset[axis] is not JsonValue coordinate || !coordinate.TryGetValue<double>(out var value) || !double.IsFinite(value) || Math.Abs(value) > 1e9))
                throw new ApiException(400, "Child surface offsets require finite bounded x and y coordinates.");
        }
        if (node.ContainsKey("rules"))
        {
            if (node["rules"] is not JsonObject rules) throw new ApiException(400, "Node rules must be an object.");
            foreach (var category in new[] { "nodeKinds", "linkKinds", "operations" })
                if (rules.ContainsKey(category) && (rules[category] is not JsonArray values || values.Any(value => value is not JsonValue token || !token.TryGetValue<string>(out _))))
                    throw new ApiException(400, "Node rules must contain arrays of text values.");
        }
        foreach (var flag in new[] { "collapsed", "closed", "exposed" })
            if (node.ContainsKey(flag) && (node[flag] is not JsonValue v || !v.TryGetValue<bool>(out _))) throw new ApiException(400, $"Node '{flag}' must be Boolean.");
        // Disclosure is independent of type: any tile may own spatial children.
        // Retain an authored edge preference even while placement is changed.
        if (node.ContainsKey("childLayout") && Text(node, "childLayout") is not ("contained" or "spatial")) throw new ApiException(400, "Child layout must be contained or spatial.");
        if (kind is "media" or "block" && Text(node, "childLayout") == "contained") throw new ApiException(400, "Media and Block nodes reveal their children outside the parent bounds.");
        if (node.ContainsKey("placement") && Text(node, "placement") is not ("free" or "near" or "edge")) throw new ApiException(400, "Child placement must be free, near or edge.");
        if (node.ContainsKey("attachSide") && Text(node, "attachSide") is not ("auto" or "top" or "right" or "bottom" or "left")) throw new ApiException(400, "Attachment side must be auto, top, right, bottom or left.");
        if (node.ContainsKey("valueType") && Text(node, "valueType") is not ("boolean" or "number")) throw new ApiException(400, "Value type must be boolean or number.");
        if (node.ContainsKey("valueType") && (kind == "input" && Text(node, "valueType") != "boolean" || kind == "number" && Text(node, "valueType") != "number")) throw new ApiException(400, "Input value type cannot contradict its node kind.");
        if (node.ContainsKey("value") && !Scalar(node["value"])) throw new ApiException(400, "Node value must be Boolean or a finite number.");
        if (kind == "number" && node.ContainsKey("value") && (node["value"] is not JsonValue number || !number.TryGetValue<double>(out _))) throw new ApiException(400, "Number inputs require a finite numeric value.");
        if (kind == "input" && node.ContainsKey("value") && (node["value"] is not JsonValue input || !input.TryGetValue<bool>(out _))) throw new ApiException(400, "Boolean inputs require a Boolean value.");
        if (kind == "joint" && !Operations.Contains(Text(node, "operation"))) throw new ApiException(400, "A joint requires a supported operation.");
        if (kind is "module" or "junction" && node.ContainsKey("operation")) throw new ApiException(400, "Routing junctions and module containers do not perform operations.");
        GridSystems.ValidateNode(node);
    }
    public static string? InputType(JsonObject node) => Text(node, "kind") switch
    {
        "switch" or "output" => Text(node, "valueType") ?? "boolean",
        "junction" => Text(node, "valueType"),
        "joint" => Text(node, "operation") == "IDENTITY" ? Text(node, "valueType") ?? "boolean" : BooleanOperations.Contains(Text(node, "operation")) ? "boolean" : "number",
        _ => null
    };
    public static int Arity(JsonObject node) => Text(node, "kind") == "joint" && Text(node, "operation") is not ("NOT" or "IDENTITY" or "ABS" or "NEGATE") ? 2 : 1;
    public static string? OutputType(JsonObject node, Dictionary<string, JsonObject> nodes, JsonObject snapshots, HashSet<string>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(Text(node, "id") ?? "")) return null;
        switch (Text(node, "kind"))
        {
            case "input": return "boolean";
            case "number": return "number";
            case "switch": case "output": return Text(node, "valueType") ?? "boolean";
            case "junction": return Text(node, "valueType");
            case "joint": return Comparisons.Contains(Text(node, "operation")) ? "boolean" : InputType(node);
            case "reference":
                var targetId = Text(node, "targetId");
                if (targetId is null) return null;
                if (nodes.TryGetValue(targetId, out var parent) && Text(parent, "canvasId") == Text(node, "canvasId"))
                {
                    var target = GridSystems.ResolveLiveReference(node, nodes);
                    return target is null ? null : OutputType(target, nodes, snapshots, seen);
                }
                if (GridSystems.ResolveSavedReference(node, snapshots) is JsonObject snapshot && snapshot["node"] is JsonObject saved)
                {
                    if (snapshot["value"] is JsonValue value) return value.TryGetValue<bool>(out _) ? "boolean" : value.TryGetValue<double>(out _) ? "number" : null;
                    if (Text(saved, "kind") == "reference") return Text(saved, "valueType");
                    return OutputType(saved, nodes, snapshots, seen);
                }
                return null;
            default: return null;
        }
    }
    public static void ValidateLinks(JsonObject project)
    {
        var nodes = ((JsonArray)project["nodes"]!).Cast<JsonObject>().ToDictionary(n => Text(n, "id")!);
        var limits = ValidateContainmentAndRules(project, nodes);
        GridSystems.ValidateCells(nodes);
        foreach (var snapshotEntry in (JsonObject)project["snapshots"]!)
        {
            var snapshot = (JsonObject)snapshotEntry.Value!;
            var saved = (JsonObject)snapshot["node"]!;
            if (Text(saved, "id") != snapshotEntry.Key) throw new ApiException(400, "Saved snapshot identity must match its reference key.");
            if (!snapshot.ContainsKey("value") || Text(snapshot, "savedAt") is null) throw new ApiException(400, "Saved snapshots require a value and saved timestamp.");
        }
        var ports = new HashSet<string>();
        var snappedPairs = new HashSet<string>();
        var edges = nodes.Keys.ToDictionary(id => id, _ => new List<string>());
        var incoming = nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var link in ((JsonArray)project["links"]!).Cast<JsonObject>())
        {
            var kind = Text(link, "kind");
            if (!LinkKinds.Contains(kind) || !LinkRelations.Contains(Text(link, "relation"))) throw new ApiException(400, "Unsupported connection type or relation.");
            if (link.ContainsKey("direction") && Text(link, "direction") is not ("forward" or "reverse" or "both" or "none")) throw new ApiException(400, "Connection direction must be forward, reverse, both or none.");
            var source = nodes[Text(link, "sourceId")!]; var target = nodes[Text(link, "targetId")!];
            if (link.ContainsKey("parentId") && (Text(link, "parentId") is not { } ownerId || !nodes.TryGetValue(ownerId, out var owner) || Text(owner, "canvasId") != Text(link, "canvasId")))
                throw new ApiException(400, "A connection owner must be an existing node in its own Work.");
            if (Text(source, "canvasId") != Text(target, "canvasId") || Text(source, "canvasId") != Text(link, "canvasId")) throw new ApiException(400, "Connections must stay within one Work.");
            SnapSystems.ValidateLink(link, nodes, snappedPairs);
            foreach (var endpoint in new[] { source, target })
                if (Text(endpoint, "parentId") is { } parentId && limits[parentId].TryGetValue("linkKinds", out var allowedLinks) && !allowedLinks.Contains(kind!))
                    throw new ApiException(400, "Connection kind is excluded by an inherited rule.");
            foreach (var anchor in new[] { "sourceAnchor", "targetAnchor" }) if (link.ContainsKey(anchor)) ValidatePoint(link[anchor]);
            if (link.ContainsKey("targetPort") && (link["targetPort"] is not JsonValue inputPort || !inputPort.TryGetValue<double>(out var portNumber) || !double.IsFinite(portNumber))) throw new ApiException(400, "Connection input port must be a number.");
            if (link.ContainsKey("points"))
            {
                if (link["points"] is not JsonArray points) throw new ApiException(400, "Connection route points must be an array.");
                foreach (var point in points) ValidatePoint(point);
            }
            if (kind == "visual") continue;
            var type = kind == "numeric" ? "number" : "boolean";
            var sourceType = OutputType(source, nodes, (JsonObject)project["snapshots"]!);
            if (InputType(target) != type || sourceType != type && !(sourceType is null && Text(source, "kind") == "reference")) throw new ApiException(400, "Computed connections require matching source and destination value types.");
            var port = link.ContainsKey("targetPort") ? ProjectValidator.Integer(link, "targetPort") : 0;
            if (port >= Arity(target) || !ports.Add(Text(target, "id") + ":" + port)) throw new ApiException(400, "Computed input ports must be valid and have one connection.");
            edges[Text(source, "id")!].Add(Text(target, "id")!); incoming[Text(target, "id")!]++;
        }
        foreach (var node in nodes.Values)
            if (Text(node, "kind") == "reference" && GridSystems.ResolveLiveReference(node, nodes) is { } original)
            { edges[Text(original, "id")!].Add(Text(node, "id")!); incoming[Text(node, "id")!]++; }
        var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.TryDequeue(out var id)) { visited++; foreach (var targetId in edges[id]) if (--incoming[targetId] == 0) queue.Enqueue(targetId); }
        if (visited != nodes.Count) throw new ApiException(400, "Computed feedback cycles are not supported.");
    }

    private static void ValidatePoint(JsonNode? node)
    {
        if (node is not JsonObject point || point["x"] is not JsonValue x || !x.TryGetValue<double>(out var px) || !double.IsFinite(px)
            || point["y"] is not JsonValue y || !y.TryGetValue<double>(out var py) || !double.IsFinite(py))
            throw new ApiException(400, "Connection route points must contain finite coordinates.");
    }

    // Linear parent traversal makes inherited permissions explicit without recursion on untrusted documents.
    private static Dictionary<string, Dictionary<string, HashSet<string>>> ValidateContainmentAndRules(JsonObject project, Dictionary<string, JsonObject> nodes)
    {
        var canvases = ((JsonArray)project["canvases"]!).Cast<JsonObject>().ToDictionary(canvas => Text(canvas, "id")!);
        var children = nodes.Keys.ToDictionary(id => id, _ => new List<string>());
        var queue = new Queue<string>();
        foreach (var canvas in canvases.Values)
        {
            var rootId = Text(canvas, "rootId")!;
            if (!nodes.TryGetValue(rootId, out var root) || Text(root, "kind") != "canvas" || root["parentId"] is not null || Text(root, "canvasId") != Text(canvas, "id"))
                throw new ApiException(400, "A Work must have one valid root.");
            queue.Enqueue(rootId);
        }
        foreach (var node in nodes.Values)
        {
            if (Text(node, "kind") == "canvas")
            {
                if (Text(canvases[Text(node, "canvasId")!], "rootId") != Text(node, "id")) throw new ApiException(400, "A Work has exactly one root.");
                continue;
            }
            var parentId = Text(node, "parentId");
            if (parentId is null || !nodes.TryGetValue(parentId, out var parent) || Text(parent, "canvasId") != Text(node, "canvasId"))
                throw new ApiException(400, "A node needs one parent in its own Work.");
            children[parentId].Add(Text(node, "id")!);
        }
        var limits = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        var allowed = new Dictionary<string, string[]> { ["nodeKinds"] = Kinds, ["linkKinds"] = LinkKinds, ["operations"] = Operations };
        while (queue.TryDequeue(out var id))
        {
            if (limits.ContainsKey(id)) throw new ApiException(400, "Containment cannot contain a cycle.");
            var node = nodes[id];
            var inherited = Text(node, "parentId") is { } parentId ? limits[parentId] : new Dictionary<string, HashSet<string>>();
            if (inherited.TryGetValue("nodeKinds", out var nodeKinds) && !nodeKinds.Contains(Text(node, "kind")!)) throw new ApiException(400, "Node kind is excluded by an inherited rule.");
            if (Text(node, "kind") == "joint" && inherited.TryGetValue("operations", out var operations) && !operations.Contains(Text(node, "operation")!)) throw new ApiException(400, "Joint operation is excluded by an inherited rule.");
            var effective = inherited.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (node.ContainsKey("rules"))
            {
                if (node["rules"] is not JsonObject rules) throw new ApiException(400, "Node rules must be an object.");
                foreach (var category in allowed)
                {
                    if (!rules.ContainsKey(category.Key)) continue;
                    if (rules[category.Key] is not JsonArray values) throw new ApiException(400, "A rule must be an array of supported values.");
                    var selected = new HashSet<string>();
                    foreach (var value in values)
                    {
                        if (value is not JsonValue token || !token.TryGetValue<string>(out var text) || !category.Value.Contains(text)) throw new ApiException(400, "A rule contains an unsupported value.");
                        if (inherited.TryGetValue(category.Key, out var parentRule) && !parentRule.Contains(text)) throw new ApiException(400, "A descendant rule cannot widen inherited permissions.");
                        selected.Add(text);
                    }
                    effective[category.Key] = selected;
                }
            }
            limits[id] = effective;
            foreach (var childId in children[id]) queue.Enqueue(childId);
        }
        if (limits.Count != nodes.Count) throw new ApiException(400, "Containment cannot contain a cycle.");
        return limits;
    }
}
