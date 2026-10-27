using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Host;

/// <summary>The library is a navigation canvas, stored independently of its referenced canvases.</summary>
public sealed class LibraryStore(string dataRoot) : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetFullPath(dataRoot), "library.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRecoveryTime;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public JsonObject Read()
    {
        // Windows replacement can momentarily prevent a new handle opening even
        // when existing readers permit deletion. Serialize opens with replacement
        // so callers never see an in-progress rename or an absent first save.
        _gate.Wait();
        try { return ReadUnlocked(); }
        finally { _gate.Release(); }
    }

    private JsonObject ReadUnlocked()
    {
        if (!File.Exists(_path)) return new JsonObject { ["schemaVersion"] = 1, ["revision"] = 0, ["entries"] = new JsonArray() };
        try
        {
            // The shared gate keeps this handle's lifetime outside file replacement.
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var document = JsonNode.Parse(stream) as JsonObject
                ?? throw new IOException("Invalid library storage document.");
            Validate(document);
            return document;
        }
        catch (JsonException exception) { throw new IOException("Invalid library storage document.", exception); }
        catch (ApiException exception) { throw new IOException("Invalid library storage document.", exception); }
    }

    public async Task<JsonObject> Save(JsonObject library, int expectedRevision)
    {
        Validate(library);
        if (ProjectValidator.Integer(library, "revision") != expectedRevision)
            throw new ApiException(409, "The library revision does not match the save revision.");
        await _gate.WaitAsync();
        try
        {
            var current = ReadUnlocked();
            if (ProjectValidator.Integer(current, "revision") != expectedRevision)
                throw new ApiException(409, "Your Work Library has changed in another window. Reload the library before saving.");
            if (current["schemaVersion"]!.GetValue<int>() == 2 && library["schemaVersion"]!.GetValue<int>() == 1)
                throw new ApiException(409, "The home Work uses the current Work format. Reload Meridian before saving with an older interface.");
            if (current["schemaVersion"]!.GetValue<int>() == 2
                && current["project"]!["id"]!.GetValue<string>() != library["project"]!["id"]!.GetValue<string>())
                throw new ApiException(400, "The home Work identifier does not match the saved Work.");
            if (expectedRevision == int.MaxValue)
                throw new ApiException(409, "The library revision limit has been reached.");
            var recovery = File.Exists(RecoveryPath) ? ReadRecoveryUnlocked() : null;
            var saved = (JsonObject)library.DeepClone();
            saved["revision"] = expectedRevision + 1;
            if (saved["schemaVersion"]!.GetValue<int>() == 2)
                ((JsonObject)saved["project"]!)["revision"] = expectedRevision + 1;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, saved, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }
                // Atomic replacement also retains the previous committed hierarchy for recovery.
                if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
                else File.Move(temporary, _path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            var recoveryPath = RecoveryPath;
            if (File.Exists(recoveryPath))
            {
                if (recovery is not null && SameAuthoredContent((JsonObject)recovery["library"]!, library)) File.Delete(recoveryPath);
            }
            return saved;
        }
        finally { _gate.Release(); }
    }

    private string RecoveryPath => _path + ".recovery.json";
    private string CheckpointFolder => Path.Combine(Path.GetDirectoryName(_path)!, "library-checkpoints");
    private string CheckpointPath(string id) => Path.Combine(CheckpointFolder, ProjectValidator.SafeId(id) + ".json");

    public JsonObject ReadRecovery()
    {
        _gate.Wait();
        try { ReadUnlocked(); return RecoveryResponse(ReadRecoveryUnlocked()); }
        finally { _gate.Release(); }
    }

    private JsonObject ReadRecoveryUnlocked()
    {
        if (!File.Exists(RecoveryPath)) throw new ApiException(404, "No recovery draft is available.");
        var recovery = ReadSidecar(RecoveryPath);
        try
        {
            if (recovery["library"] is not JsonObject library) throw new ApiException(400, "Invalid library recovery document.");
            ValidateCanvas(library);
            if (ProjectValidator.Integer(library, "revision") != ProjectValidator.Integer(recovery, "baseRevision"))
                throw new ApiException(400, "Invalid library recovery revision.");
            ProjectValidator.Text(recovery, "savedAt", 64);
            return recovery;
        }
        catch (ApiException exception) { throw new IOException("Invalid library recovery storage document.", exception); }
    }

    private static JsonObject RecoveryResponse(JsonObject recovery)
    {
        var result = (JsonObject)recovery.DeepClone();
        result["project"] = result["library"]!["project"]!.DeepClone();
        return result;
    }

    public async Task<JsonObject> SaveRecovery(JsonObject library, int baseRevision, string? expectedSavedAt)
    {
        ValidateCanvas(library);
        if (ProjectValidator.Integer(library, "revision") != baseRevision)
            throw new ApiException(409, "The recovery draft revision does not match its base revision.");
        await _gate.WaitAsync();
        try
        {
            var current = ReadUnlocked();
            var currentRevision = ProjectValidator.Integer(current, "revision");
            var previous = File.Exists(RecoveryPath) ? ReadRecoveryUnlocked() : null;
            var previousToken = previous?["savedAt"]?.GetValue<string>();
            if (!string.Equals(expectedSavedAt, previousToken, StringComparison.Ordinal))
                throw new ApiException(409, "The recovery draft has changed in another window. Keep your current draft and review recovery before overwriting it.");
            if (baseRevision > currentRevision || previous is null && baseRevision != currentRevision
                || previous is not null && ProjectValidator.Integer(previous, "baseRevision") != baseRevision)
                throw new ApiException(409, "The recovery draft must retain its original base revision. Review the conflict or save a new Work copy.");
            if (current["schemaVersion"]!.GetValue<int>() == 2
                && current["project"]!["id"]!.GetValue<string>() != library["project"]!["id"]!.GetValue<string>())
                throw new ApiException(400, "The recovery Work identifier does not match the saved home Work.");
            var timestamp = DateTimeOffset.UtcNow;
            if (timestamp <= _lastRecoveryTime) timestamp = _lastRecoveryTime.AddTicks(1);
            if (DateTimeOffset.TryParse(previousToken, out var previousTime) && timestamp <= previousTime) timestamp = previousTime.AddTicks(1);
            _lastRecoveryTime = timestamp;
            var recovery = new JsonObject { ["library"] = library.DeepClone(), ["baseRevision"] = baseRevision, ["savedAt"] = timestamp.ToString("O") };
            await AtomicSidecarWrite(RecoveryPath, recovery);
            return RecoveryResponse(recovery);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteRecovery(int baseRevision, string savedAt)
    {
        await _gate.WaitAsync();
        try
        {
            ReadUnlocked();
            if (!File.Exists(RecoveryPath)) return;
            var current = ReadRecoveryUnlocked();
            if (ProjectValidator.Integer(current, "baseRevision") != baseRevision
                || !string.Equals(ProjectValidator.Text(current, "savedAt", 64), savedAt, StringComparison.Ordinal))
                throw new ApiException(409, "The recovery draft has changed. Review the current recovery before discarding it.");
            File.Delete(RecoveryPath);
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonObject> SaveCheckpoint(string name, JsonObject library)
    {
        ValidateCanvas(library);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ApiException(400, "A checkpoint name must contain 1 to 120 characters.");
        await _gate.WaitAsync();
        try
        {
            var current = ReadUnlocked();
            if (ProjectValidator.Integer(current, "revision") != ProjectValidator.Integer(library, "revision"))
                throw new ApiException(409, "The Work Library has changed since it was loaded. Keep your draft and reload before making a checkpoint.");
            var id = Guid.NewGuid().ToString("N");
            var metadata = new JsonObject { ["id"] = id, ["name"] = name.Trim(), ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["revision"] = library["revision"]!.DeepClone() };
            await AtomicSidecarWrite(CheckpointPath(id), new JsonObject { ["metadata"] = metadata.DeepClone(), ["library"] = library.DeepClone() });
            return metadata;
        }
        finally { _gate.Release(); }
    }

    public JsonObject[] ListCheckpoints()
    {
        _gate.Wait();
        try
        {
            ReadUnlocked();
            if (!Directory.Exists(CheckpointFolder)) return [];
            return Directory.EnumerateFiles(CheckpointFolder, "*.json")
                .Select(path => (JsonObject)ReadSidecar(path)["metadata"]!.DeepClone())
                .OrderByDescending(item => item["createdAt"]!.GetValue<string>()).ToArray();
        }
        finally { _gate.Release(); }
    }

    public JsonObject ReadCheckpoint(string id)
    {
        _gate.Wait();
        try
        {
            ReadUnlocked();
            var path = CheckpointPath(id);
            if (!File.Exists(path)) throw new ApiException(404, "Checkpoint not found.");
            var checkpoint = ReadSidecar(path);
            if (checkpoint["library"] is not JsonObject library) throw new IOException("Invalid library checkpoint storage document.");
            try { ValidateCanvas(library); }
            catch (ApiException exception) { throw new IOException("Invalid library checkpoint storage document.", exception); }
            return (JsonObject)library.DeepClone();
        }
        finally { _gate.Release(); }
    }

    private static JsonObject ReadSidecar(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new IOException("Invalid library storage document."); }
        catch (JsonException exception) { throw new IOException("Invalid library storage document.", exception); }
    }

    private static bool SameAuthoredContent(JsonObject left, JsonObject right)
    {
        var a = (JsonObject)left.DeepClone(); var b = (JsonObject)right.DeepClone();
        a.Remove("revision"); b.Remove("revision");
        if (a["project"] is JsonObject ap) { ap.Remove("revision"); ap.Remove("snapshots"); }
        if (b["project"] is JsonObject bp) { bp.Remove("revision"); bp.Remove("snapshots"); }
        return JsonNode.DeepEquals(a, b);
    }

    private static async Task AtomicSidecarWrite(string path, JsonObject document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static JsonArray Catalog(ProjectStore store)
    {
        var result = new JsonArray();
        foreach (var metadata in store.List())
        {
            var summary = (JsonObject)JsonSerializer.SerializeToNode(metadata)!;
            var project = store.Read(ProjectValidator.Text(summary, "id", 96));
            var canvases = ((JsonArray)project["canvases"]!).Cast<JsonObject>().ToArray();
            var nodes = ((JsonArray)project["nodes"]!).Cast<JsonObject>().ToArray();
            var roots = canvases.ToDictionary(canvas => ProjectValidator.Text(canvas, "rootId", 96), Work => ProjectValidator.Text(Work, "id", 96));
            var canvasIds = canvases.Select(canvas => ProjectValidator.Text(canvas, "id", 96)).ToHashSet(StringComparer.Ordinal);
            var counts = nodes.Where(node => ProjectValidator.Text(node, "kind", 32) != "canvas")
                .GroupBy(node => ProjectValidator.Text(node, "canvasId", 96)).ToDictionary(group => group.Key, group => group.Count());
            var canvasSummary = new JsonArray();
            foreach (var canvas in canvases)
            {
                var id = ProjectValidator.Text(canvas, "id", 96);
                canvasSummary.Add(new JsonObject
                {
                    ["id"] = id, ["rootId"] = canvas["rootId"]!.DeepClone(), ["title"] = canvas["title"]!.DeepClone(),
                    ["nodeCount"] = counts.GetValueOrDefault(id)
                });
            }
            var links = new JsonArray();
            var seen = new HashSet<(string, string)>();
            foreach (var node in nodes.Where(node => node["kind"]?.GetValue<string>() == "canvas-link"))
            {
                if (node["targetId"] is not JsonValue targetValue || !targetValue.TryGetValue<string>(out var target)) continue;
                var targetCanvas = roots.GetValueOrDefault(target) ?? (canvasIds.Contains(target) ? target : null);
                var sourceCanvas = ProjectValidator.Text(node, "canvasId", 96);
                if (targetCanvas is null || !seen.Add((sourceCanvas, targetCanvas))) continue;
                links.Add(new JsonObject { ["sourceCanvasId"] = sourceCanvas, ["targetCanvasId"] = targetCanvas });
            }
            result.Add(new JsonObject
            {
                ["id"] = project["id"]!.DeepClone(), ["title"] = project["title"]!.DeepClone(),
                ["revision"] = project["revision"]!.DeepClone(), ["updatedAt"] = summary["updatedAt"]!.DeepClone(),
                ["entryCanvasId"] = project["entryCanvasId"]!.DeepClone(), ["canvases"] = canvasSummary, ["links"] = links
            });
        }
        return result;
    }

    private static void Validate(JsonObject library)
    {
        if (ProjectValidator.Integer(library, "schemaVersion") == 2)
        {
            ValidateCanvas(library);
            return;
        }
        AllowedFields(library, ["schemaVersion", "revision", "entries"]);
        if (ProjectValidator.Integer(library, "schemaVersion") != 1) throw new ApiException(400, "Unsupported library schema version.");
        ProjectValidator.Integer(library, "revision");
        if (library["entries"] is not JsonArray entries || entries.Count > 10000)
            throw new ApiException(400, "Library entries must be an array with at most 10000 entries.");
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in entries)
        {
            if (item is not JsonObject entry) throw new ApiException(400, "Each library entry must be an object.");
            AllowedFields(entry, ["id", "kind", "title", "parentId", "projectId", "canvasId", "collapsed", "x", "y", "placement"]);
            var id = ProjectValidator.SafeId(ProjectValidator.Text(entry, "id", 96));
            var kind = ProjectValidator.Text(entry, "kind", 16);
            if (kind is not ("section" or "canvas")) throw new ApiException(400, "Library entry kind is unsupported.");
            ProjectValidator.Text(entry, "title");
            if (!entry.ContainsKey("parentId")) throw new ApiException(400, "Library entries require parentId, or null for top-level entries.");
            var parent = entry["parentId"] is null ? null : ProjectValidator.SafeId(ProjectValidator.Text(entry, "parentId", 96));
            if (!parents.TryAdd(id, parent)) throw new ApiException(400, "Library entry identifiers must be unique.");
            if (entry["collapsed"] is not JsonValue flag || !flag.TryGetValue<bool>(out _))
                throw new ApiException(400, "Library collapsed state must be Boolean.");
            if (entry.ContainsKey("placement") && (entry["placement"] is not JsonValue placement || !placement.TryGetValue<string>(out var placementKind)
                || placementKind is not ("near" or "free")))
                throw new ApiException(400, "Library placement must be near or free.");
            foreach (var coordinate in new[] { "x", "y" })
                if (entry[coordinate] is not JsonValue number || !number.TryGetValue<double>(out var value)
                    || !double.IsFinite(value) || Math.Abs(value) > 1e9)
                    throw new ApiException(400, "Library coordinates must be finite numbers between -1e9 and 1e9.");
            if (kind == "canvas")
            {
                ProjectValidator.SafeId(ProjectValidator.Text(entry, "projectId", 96));
                if (entry.ContainsKey("canvasId")) ProjectValidator.SafeId(ProjectValidator.Text(entry, "canvasId", 96));
            }
            else if (entry.ContainsKey("projectId") || entry.ContainsKey("canvasId"))
                throw new ApiException(400, "Sections cannot have Work references.");
        }
        // Iterative traversal permits deep nesting without overflowing the call stack.
        var complete = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in parents.Keys)
        {
            var path = new HashSet<string>(StringComparer.Ordinal);
            string? current = id;
            while (current is not null && !complete.Contains(current))
            {
                if (!path.Add(current)) throw new ApiException(400, "Library entries cannot contain a parent cycle.");
                if (!parents.TryGetValue(current, out current)) throw new ApiException(400, "A library parent entry does not exist.");
            }
            complete.UnionWith(path);
        }
    }

    private static void ValidateCanvas(JsonObject library)
    {
        AllowedFields(library, ["schemaVersion", "revision", "project", "canvasTargets", "discoveredTargets"]);
        if (ProjectValidator.Integer(library, "schemaVersion") != 2)
            throw new ApiException(400, "Unsupported library Work schema version.");
        var revision = ProjectValidator.Integer(library, "revision");
        if (library["project"] is not JsonObject project)
            throw new ApiException(400, "The Work Library requires an ordinary Meridian Work document.");
        // Legacy libraries admitted 10,000 entries in addition to their root and
        // introductory label. Migration preserves those identifiers at the limit.
        ProjectValidator.Validate(project, maximumNodes: 10002);
        if (ProjectValidator.Integer(project, "revision") != revision)
            throw new ApiException(400, "The library Work revision must match its storage revision.");
        if (library["canvasTargets"] is not JsonObject targets || targets.Count > 10000)
            throw new ApiException(400, "Work locations must be an object with at most 10000 locations.");
        var nodes = ((JsonArray)project["nodes"]!).Cast<JsonObject>()
            .ToDictionary(node => ProjectValidator.Text(node, "id", 96), StringComparer.Ordinal);
        foreach (var targetEntry in targets)
        {
            ProjectValidator.SafeId(targetEntry.Key);
            if (!nodes.TryGetValue(targetEntry.Key, out var node) || node["kind"]!.GetValue<string>() != "canvas-link")
                throw new ApiException(400, "An external Work location must belong to an existing Work node.");
            if (targetEntry.Value is not JsonObject target)
                throw new ApiException(400, "A Work location must contain a saved workspace identifier.");
            AllowedFields(target, ["projectId", "canvasId"]);
            ProjectValidator.SafeId(ProjectValidator.Text(target, "projectId", 96));
            if (target.ContainsKey("canvasId")) ProjectValidator.SafeId(ProjectValidator.Text(target, "canvasId", 96));
            if (node["canvasTarget"] is JsonObject canonical && !JsonNode.DeepEquals(canonical, target))
                throw new ApiException(400, "The saved Work location must match its Work node destination.");
        }
        if (library["discoveredTargets"] is not JsonArray discovered || discovered.Count > 20000)
            throw new ApiException(400, "Work discovery history must be an array with at most 20000 locations.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in discovered)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var key) || !seen.Add(key))
                throw new ApiException(400, "Work discovery history must contain unique saved locations.");
            var parts = key.Split(':');
            if (parts.Length != 2) throw new ApiException(400, "Invalid Work discovery location.");
            ProjectValidator.SafeId(parts[0]);
            if (parts[1] != "*") ProjectValidator.SafeId(parts[1]);
        }
    }

    private static void AllowedFields(JsonObject value, string[] allowed)
    {
        foreach (var field in value)
            if (!allowed.Contains(field.Key, StringComparer.Ordinal)) throw new ApiException(400, $"Unsupported library field '{field.Key}'.");
    }

    public void Dispose() => _gate.Dispose();
}
