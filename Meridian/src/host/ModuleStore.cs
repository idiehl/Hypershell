using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Meridian.Host;

/// <summary>Immutable local module snapshots. ProjectStore owns the data-root process lock.</summary>
public sealed partial class ModuleStore
{
    public const int MaxModules = 1000;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly string[] NodeKinds = NodeSystems.Kinds;
    private static readonly string[] Gates = NodeSystems.Operations;
    private static readonly string[] LinkKinds = NodeSystems.LinkKinds;
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex TimestampPattern();

    public ModuleStore(string dataRoot)
    {
        _root = Path.Combine(Path.GetFullPath(dataRoot), "modules");
        Directory.CreateDirectory(_root);
    }

    public JsonObject[] List()
    {
        _gate.Wait();
        try
        {
            var paths = Directory.EnumerateFiles(_root, "*.json").Take(MaxModules + 1).ToArray();
            if (paths.Length > MaxModules) throw new ApiException(503, "The local module library exceeds its 1000-entry limit.");
            return paths.Select(path => Summary(ReadFile(path)))
                .OrderByDescending(item => DateTimeOffset.Parse(item["createdAt"]!.GetValue<string>(), CultureInfo.InvariantCulture)).ToArray();
        }
        finally { _gate.Release(); }
    }

    public JsonObject Read(string id)
    {
        var path = ModulePath(id);
        _gate.Wait();
        try
        {
            if (!File.Exists(path)) throw new ApiException(404, "Stored module not found.");
            return ReadFile(path);
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonObject> Create(JsonObject module)
    {
        Validate(module);
        var saved = (JsonObject)module.DeepClone();
        var path = ModulePath(ProjectValidator.Text(saved, "id", 96));
        await _gate.WaitAsync();
        try
        {
            if (File.Exists(path)) throw new ApiException(409, "This stored module already exists. Save a new module with a fresh identifier.");
            if (Directory.EnumerateFiles(_root, "*.json").Take(MaxModules).Count() >= MaxModules)
                throw new ApiException(409, "The local module library has reached its 1000-entry limit.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, saved);
                    if (stream.Length > ProjectValidator.MaxExpandedBytes) throw new ApiException(413, "The stored module exceeds 80 MiB.");
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return saved;
        }
        finally { _gate.Release(); }
    }

    private string ModulePath(string id)
    {
        ProjectValidator.SafeId(id);
        if (!Guid.TryParseExact(id, "D", out _)) throw new ApiException(400, "A stored module identifier must be a hyphenated GUID.");
        return Path.Combine(_root, id.ToLowerInvariant() + ".json");
    }

    private static JsonObject ReadFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > ProjectValidator.MaxExpandedBytes) throw new ApiException(400, "Stored module exceeds the size limit.");
            var module = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new ApiException(400, "Stored module is malformed.");
            Validate(module);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), module["id"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                throw new ApiException(400, "Stored module identifier does not match its file.");
            return module;
        }
        catch (Exception exception) when (exception is JsonException or ApiException)
        {
            throw new ApiException(503, $"A stored module is damaged or unsupported ({Path.GetFileName(path)}). Its file has been preserved.");
        }
    }

    private static JsonObject Summary(JsonObject module)
    {
        var project = (JsonObject)module["project"]!;
        var nodes = ((JsonArray)project["nodes"]!).Cast<JsonObject>().Where(node => node["kind"]!.GetValue<string>() != "canvas").ToArray();
        return new JsonObject
        {
            ["id"] = module["id"]!.DeepClone(), ["name"] = module["name"]!.DeepClone(),
            ["createdAt"] = module["createdAt"]!.DeepClone(), ["rootId"] = module["rootId"]!.DeepClone(),
            ["nodeCount"] = nodes.Length, ["linkCount"] = ((JsonArray)project["links"]!).Count,
            ["contents"] = new JsonArray(nodes.GroupBy(node => node["kind"]!.GetValue<string>()).OrderBy(group => group.Key)
                .Select(group => (JsonNode)new JsonObject { ["kind"] = group.Key, ["count"] = group.Count() }).ToArray())
        };
    }

    public static void Validate(JsonObject module)
    {
        if (ProjectValidator.Integer(module, "schemaVersion") != 1) throw new ApiException(400, "Unsupported stored module schema.");
        var id = ProjectValidator.Text(module, "id", 96);
        if (!Guid.TryParseExact(id, "D", out _)) throw new ApiException(400, "A stored module identifier must be a hyphenated GUID.");
        var name = ProjectValidator.Text(module, "name", 160);
        if (name != name.Trim()) throw new ApiException(400, "A stored module name must be trimmed.");
        var createdAt = ProjectValidator.Text(module, "createdAt", 64);
        if (!TimestampPattern().IsMatch(createdAt) || !DateTimeOffset.TryParse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ApiException(400, "A stored module creation time must be an ISO timestamp with a timezone.");
        var rootId = ProjectValidator.Text(module, "rootId", 96);
        var project = module["project"] as JsonObject ?? throw new ApiException(400, "A stored module must contain a project.");
        ProjectValidator.Validate(project);
        foreach (var value in (JsonArray)project["dependencies"]!)
        {
            if (value is not JsonObject dependency || StringValue(dependency, "canvasId") is null || StringValue(dependency, "title") is null
                || dependency["required"] is not JsonValue required || !required.TryGetValue<bool>(out _))
                throw new ApiException(400, "A module Work dependency must identify its Work, title, and required flag.");
        }
        var canvases = (JsonArray)project["canvases"]!;
        if (canvases.Count != 1 || ((JsonObject)project["snapshots"]!).Count != 0)
            throw new ApiException(400, "A stored module must contain one Work and no saved reference snapshots.");
        var canvas = (JsonObject)canvases[0]!;
        var canvasId = ProjectValidator.Text(canvas, "id", 96);
        var canvasRootId = ProjectValidator.Text(canvas, "rootId", 96);
        var infrastructure = new HashSet<string>(StringComparer.Ordinal) { project["id"]!.GetValue<string>(), canvasId, canvasRootId };
        var nodes = ((JsonArray)project["nodes"]!).Cast<JsonObject>().ToDictionary(node => ProjectValidator.Text(node, "id", 96), StringComparer.Ordinal);
        if (!nodes.TryGetValue(canvasRootId, out var canvasRoot) || canvasRoot["kind"]!.GetValue<string>() != "canvas"
            || !canvasRoot.ContainsKey("parentId") || canvasRoot["parentId"] is not null)
            throw new ApiException(400, "A stored module Work needs one valid root.");
        if (!nodes.TryGetValue(rootId, out var root) || root["kind"]!.GetValue<string>() != "module" || StringValue(root, "parentId") != canvasRootId)
            throw new ApiException(400, "The module root must be a module node directly beneath the Work root.");

        // Validate identity and parentage before walking; reachability then rejects cycles and unrelated branches in linear time.
        var ids = new HashSet<string>(StringComparer.Ordinal) { project["id"]!.GetValue<string>() };
        foreach (var entity in new[] { (JsonArray)project["canvases"]!, (JsonArray)project["nodes"]!, (JsonArray)project["links"]!, (JsonArray)project["assets"]! }.SelectMany(array => array))
            if (!ids.Add(ProjectValidator.Text((JsonObject)entity!, "id", 96))) throw new ApiException(400, "Module object identifiers must be unique across the project.");
        var children = nodes.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            var nodeId = node["id"]!.GetValue<string>();
            if (StringValue(node, "canvasId") != canvasId) throw new ApiException(400, "All module nodes must belong to its Work.");
            ValidateNode(node);
            if (StringValue(node, "targetId") is { } referencedId && infrastructure.Contains(referencedId)
                || node["dependencyIds"] is JsonArray dependencies && dependencies.Any(value => infrastructure.Contains(value!.GetValue<string>())))
                throw new ApiException(400, "Module contents cannot depend on its storage Work.");
            if (nodeId == canvasRootId) continue;
            var parentId = StringValue(node, "parentId");
            if (node["kind"]!.GetValue<string>() == "canvas" || parentId is null || !children.TryGetValue(parentId, out var siblings))
                throw new ApiException(400, "Every module node must have one valid parent.");
            siblings.Add(nodeId);
        }
        if (children[canvasRootId].Count != 1 || children[canvasRootId][0] != rootId)
            throw new ApiException(400, "A module snapshot cannot include nodes outside its module branch.");
        var ordered = new List<string> { canvasRootId };
        for (var index = 0; index < ordered.Count; index++) ordered.AddRange(children[ordered[index]]);
        if (ordered.Count != nodes.Count) throw new ApiException(400, "Module containment contains a cycle or a disconnected branch.");
        ValidateRulesAndLinks(project, nodes, ordered);
    }

    private static string? StringValue(JsonObject owner, string name) => owner[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void ValidateNode(JsonObject node)
    {
        if (StringValue(node, "title") is null) throw new ApiException(400, "A module node title must be text.");
        foreach (var name in new[] { "text", "assetId", "operation", "targetId" })
            if (node.ContainsKey(name) && StringValue(node, name) is null) throw new ApiException(400, $"Node '{name}' must be text.");
        foreach (var name in new[] { "collapsed", "closed", "exposed" })
            if (node.ContainsKey(name) && (node[name] is not JsonValue value || !value.TryGetValue<bool>(out _)))
                throw new ApiException(400, $"Node '{name}' must be Boolean.");
        if (StringValue(node, "kind") == "joint" && !Gates.Contains(StringValue(node, "operation")))
            throw new ApiException(400, "A module joint needs a supported operation.");
    }

    private static void ValidateRulesAndLinks(JsonObject project, Dictionary<string, JsonObject> nodes, List<string> ordered)
    {
        var limits = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
        var allowed = new Dictionary<string, string[]> { ["nodeKinds"] = NodeKinds, ["linkKinds"] = LinkKinds, ["operations"] = Gates };
        foreach (var id in ordered)
        {
            var node = nodes[id];
            var inherited = StringValue(node, "parentId") is { } parentId ? limits[parentId] : [];
            if (inherited.TryGetValue("nodeKinds", out var kinds) && !kinds.Contains(StringValue(node, "kind")!))
                throw new ApiException(400, "A module node is excluded by an inherited rule.");
            if (StringValue(node, "kind") == "joint" && inherited.TryGetValue("operations", out var operations) && !operations.Contains(StringValue(node, "operation")!))
                throw new ApiException(400, "A module joint operation is excluded by an inherited rule.");
            var effective = inherited.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (node.ContainsKey("rules"))
            {
                if (node["rules"] is not JsonObject rules) throw new ApiException(400, "Module rules must be an object.");
                foreach (var rule in rules)
                {
                    if (!allowed.TryGetValue(rule.Key, out var choices) || rule.Value is not JsonArray values)
                        throw new ApiException(400, "A module rule is unsupported.");
                    var selected = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var value in values)
                        if (value is not JsonValue token || !token.TryGetValue<string>(out var text) || !choices.Contains(text)
                            || inherited.TryGetValue(rule.Key, out var parentRule) && !parentRule.Contains(text))
                            throw new ApiException(400, "A module rule cannot widen inherited permissions or contain unsupported values.");
                        else selected.Add(text);
                    effective[rule.Key] = selected;
                }
            }
            limits[id] = effective;
            if (StringValue(node, "kind") == "canvas-link" && StringValue(node, "targetId") is { } targetId
                && nodes.TryGetValue(targetId, out var target) && StringValue(target, "kind") != "canvas")
                throw new ApiException(400, "A Work link must target a Work root.");
        }
        var adjacency = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var incoming = nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var ports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in ((JsonArray)project["links"]!).Cast<JsonObject>())
        {
            var sourceId = StringValue(link, "sourceId")!; var targetId = StringValue(link, "targetId")!;
            var source = nodes[sourceId]; var target = nodes[targetId];
            if (StringValue(source, "kind") == "canvas" || StringValue(target, "kind") == "canvas")
                throw new ApiException(400, "Module links must stay inside its packing container.");
            if (StringValue(link, "parentId") is { } ownerId && StringValue(nodes[ownerId], "kind") == "canvas")
                throw new ApiException(400, "Module connection owners must stay inside its packing container.");
            var kind = StringValue(link, "kind");
            if (!LinkKinds.Contains(kind) || !NodeSystems.LinkRelations.Contains(StringValue(link, "relation")))
                throw new ApiException(400, "A module link kind or relation is unsupported.");
            // A node's own rules constrain its children; endpoint links inherit only its ancestors' rules.
            foreach (var endpoint in new[] { source, target })
                if (StringValue(endpoint, "parentId") is { } parentId && limits[parentId].TryGetValue("linkKinds", out var inherited) && !inherited.Contains(kind!))
                    throw new ApiException(400, "A module link is excluded by an inherited rule.");
            foreach (var anchor in new[] { "sourceAnchor", "targetAnchor" })
                if (link.ContainsKey(anchor)) ValidatePoint(link[anchor]);
            if (link.ContainsKey("points"))
            {
                if (link["points"] is not JsonArray points || points.Count > 10000) throw new ApiException(400, "Link points must be a bounded array.");
                foreach (var point in points) ValidatePoint(point);
            }
            if (kind == "visual") continue;
            var maximum = NodeSystems.Arity(target);
            var port = link.ContainsKey("targetPort") ? ProjectValidator.Integer(link, "targetPort") : 0;
            if (port >= maximum || !ports.Add(targetId + ":" + port)) throw new ApiException(400, "Computed input ports must be valid and have one connection.");
            adjacency[sourceId].Add(targetId); incoming[targetId]++;
        }
        foreach (var node in nodes.Values)
            if (StringValue(node, "kind") == "reference" && GridSystems.ResolveLiveReference(node, nodes) is { } original)
            {
                var id = StringValue(node, "id")!; adjacency[StringValue(original, "id")!].Add(id); incoming[id]++;
            }
        var queue = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.TryDequeue(out var id))
        {
            visited++;
            foreach (var target in adjacency[id]) if (--incoming[target] == 0) queue.Enqueue(target);
        }
        if (visited != nodes.Count) throw new ApiException(400, "Functional feedback cycles are not supported in modules.");
    }

    private static void ValidatePoint(JsonNode? value)
    {
        if (value is not JsonObject point) throw new ApiException(400, "Link geometry must contain points.");
        foreach (var coordinate in new[] { "x", "y" })
            if (point[coordinate] is not JsonValue number || !number.TryGetValue<double>(out var scalar) || !double.IsFinite(scalar))
                throw new ApiException(400, "Link points must contain finite coordinates.");
    }
}
