using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Host;

public sealed class ProjectStore : IDisposable
{
    private readonly string _projectsRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FileStream _ownership;
    private DateTimeOffset _lastRecoveryTime;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ProjectStore(string dataRoot)
    {
        var root = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(root);
        _ownership = new FileStream(Path.Combine(root, ".host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _projectsRoot = Path.Combine(root, "projects");
        Directory.CreateDirectory(_projectsRoot);
    }

    public object[] List() => WithReadGate(() => Directory.EnumerateDirectories(_projectsRoot)
        .Where(path => File.Exists(Path.Combine(path, "project.json")))
        .Select(path =>
        {
            var record = ReadJson(Path.Combine(path, "project.json"));
            var project = (JsonObject)record["project"]!;
            return new
            {
                id = project["id"]!.GetValue<string>(), title = project["title"]!.GetValue<string>(),
                revision = project["revision"]!.GetValue<int>(), updatedAt = record["updatedAt"]!.GetValue<string>()
            };
        }).OrderByDescending(item => item.updatedAt).Cast<object>().ToArray());

    public JsonObject Read(string id) => WithReadGate(() => ReadUnlocked(id));

    private JsonObject ReadUnlocked(string id) => (JsonObject)ReadRecord(id)["project"]!.DeepClone();

    public async Task<JsonObject> Create(JsonObject project)
    {
        ProjectValidator.Validate(project);
        if (ProjectValidator.Integer(project, "revision") != 0)
            throw new ApiException(409, "New projects must start at revision zero. Import as a new project before saving.");
        var id = ProjectValidator.Text(project, "id", 96);
        await _gate.WaitAsync();
        try
        {
            var path = ProjectPath(id);
            if (File.Exists(path)) throw new ApiException(409, "A project with this identifier already exists. Save a copy with a new identifier.");
            var saved = (JsonObject)project.DeepClone();
            saved["revision"] = 1;
            await AtomicWrite(path, Record(saved));
            return saved;
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonObject> Save(string id, JsonObject project, int expectedRevision, bool requireNoRecovery = false)
    {
        ValidateIdentity(id, project);
        if (ProjectValidator.Integer(project, "revision") != expectedRevision)
            throw new ApiException(409, "The project revision does not match the save revision.");
        await _gate.WaitAsync();
        try
        {
            var current = ReadUnlocked(id);
            CheckRevision(current, expectedRevision);
            if (requireNoRecovery && File.Exists(RecoveryPath(id)))
                throw new ApiException(409, "The project has a recovery draft. Resolve it in the editor before applying a composition.");
            var saved = (JsonObject)project.DeepClone();
            saved["revision"] = checked(expectedRevision + 1);
            await AtomicWrite(ProjectPath(id), Record(saved));
            var recoveryPath = RecoveryPath(id);
            if (File.Exists(recoveryPath))
            {
                var recovery = ReadJson(recoveryPath);
                // Only discard a recovery whose exact contents have now been committed.
                // A later autosave can contain work newer than the explicit save request.
                if (SameAuthoredContent((JsonObject)recovery["project"]!, project)) File.Delete(recoveryPath);
            }
            return saved;
        }
        finally { _gate.Release(); }
    }

    // The reviewed candidate may incorporate a draft. Both exact baselines are checkpointed
    // under the same writer lock before replacing the saved revision and consuming the draft.
    public async Task<JsonObject> ApplyAuthoring(string id, JsonObject project, int expectedRevision, string? expectedRecoverySavedAt, string checkpointName)
    {
        ValidateIdentity(id, project);
        if (ProjectValidator.Integer(project, "revision") != expectedRevision) throw new ApiException(409, "Candidate revision must match the expected revision.");
        await _gate.WaitAsync();
        try
        {
            var current = ReadUnlocked(id); CheckRevision(current, expectedRevision);
            var recoveryPath = RecoveryPath(id);
            var recovery = File.Exists(recoveryPath) ? ReadJson(recoveryPath) : null;
            if (!string.Equals(expectedRecoverySavedAt, recovery?["savedAt"]?.GetValue<string>(), StringComparison.Ordinal))
                throw new ApiException(409, "The recovery draft changed after the candidate was prepared. Rebase the composition before applying it.");
            if (recovery is not null && ProjectValidator.Integer(recovery, "baseRevision") != expectedRevision)
                throw new ApiException(409, "A stale recovery draft must be reconciled before applying a composition.");
            async Task Checkpoint(JsonObject baseline, string suffix)
            {
                var checkpointId = Guid.NewGuid().ToString("N");
                await AtomicWrite(CheckpointPath(id, checkpointId), new JsonObject
                {
                    ["metadata"] = new JsonObject { ["id"] = checkpointId, ["name"] = checkpointName + suffix, ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["revision"] = expectedRevision },
                    ["project"] = baseline.DeepClone()
                });
            }
            await Checkpoint(current, " · saved");
            if (recovery is not null) await Checkpoint((JsonObject)recovery["project"]!, " · draft");
            var saved = (JsonObject)project.DeepClone(); saved["revision"] = checked(expectedRevision + 1);
            await AtomicWrite(ProjectPath(id), Record(saved));
            if (recovery is not null) File.Delete(recoveryPath);
            return saved;
        }
        finally { _gate.Release(); }
    }

    public JsonObject ReadRecovery(string id) => WithReadGate(() =>
    {
        ReadRecord(id);
        var path = RecoveryPath(id);
        if (!File.Exists(path)) throw new ApiException(404, "No recovery draft is available.");
        return ReadJson(path);
    });

    public async Task<JsonObject> SaveRecovery(string id, JsonObject project, int baseRevision, string? expectedSavedAt)
    {
        ValidateIdentity(id, project);
        if (ProjectValidator.Integer(project, "revision") != baseRevision)
            throw new ApiException(409, "The recovery draft revision does not match its base revision.");
        await _gate.WaitAsync();
        try
        {
            var currentRevision = ProjectValidator.Integer(ReadUnlocked(id), "revision");
            var recoveryPath = RecoveryPath(id);
            var previous = File.Exists(recoveryPath) ? ReadJson(recoveryPath) : null;
            var previousToken = previous?["savedAt"]?.GetValue<string>();
            if (!string.Equals(expectedSavedAt, previousToken, StringComparison.Ordinal))
                throw new ApiException(409, "The recovery draft has changed in another window. Keep your current draft and review recovery before overwriting it.");
            if (baseRevision > currentRevision
                || previous is null && baseRevision != currentRevision
                || previous is not null && ProjectValidator.Integer(previous, "baseRevision") != baseRevision)
                throw new ApiException(409, "The recovery draft must retain its original base revision. Review the conflict or save a new project copy.");
            var timestamp = DateTimeOffset.UtcNow;
            if (timestamp <= _lastRecoveryTime) timestamp = _lastRecoveryTime.AddTicks(1);
            if (DateTimeOffset.TryParse(previousToken, out var previousTime) && timestamp <= previousTime) timestamp = previousTime.AddTicks(1);
            _lastRecoveryTime = timestamp;
            var recovery = new JsonObject
            {
                ["project"] = project.DeepClone(), ["baseRevision"] = baseRevision,
                ["savedAt"] = timestamp.ToString("O")
            };
            await AtomicWrite(recoveryPath, recovery);
            return recovery;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteRecovery(string id, int baseRevision, string savedAt)
    {
        await _gate.WaitAsync();
        try
        {
            ReadRecord(id);
            var path = RecoveryPath(id);
            if (!File.Exists(path)) return;
            var current = ReadJson(path);
            if (ProjectValidator.Integer(current, "baseRevision") != baseRevision
                || !string.Equals(ProjectValidator.Text(current, "savedAt", 64), savedAt, StringComparison.Ordinal))
                throw new ApiException(409, "The recovery draft has changed. Review the current recovery before discarding it.");
            File.Delete(path);
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonObject> SaveCheckpoint(string id, string name, JsonObject project)
    {
        ValidateIdentity(id, project);
        await _gate.WaitAsync();
        try
        {
            CheckRevision(ReadUnlocked(id), ProjectValidator.Integer(project, "revision"));
            var checkpointId = Guid.NewGuid().ToString("N");
            var metadata = new JsonObject
            {
                ["id"] = checkpointId, ["name"] = name.Trim(), ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["revision"] = ProjectValidator.Integer(project, "revision")
            };
            await AtomicWrite(CheckpointPath(id, checkpointId), new JsonObject
            {
                ["metadata"] = metadata.DeepClone(), ["project"] = project.DeepClone()
            });
            return metadata;
        }
        finally { _gate.Release(); }
    }

    public JsonObject[] ListCheckpoints(string id) => WithReadGate(() =>
    {
        ReadRecord(id);
        var folder = Path.Combine(ProjectDirectory(id), "checkpoints");
        if (!Directory.Exists(folder)) return Array.Empty<JsonObject>();
        return Directory.EnumerateFiles(folder, "*.json")
            .Select(path => (JsonObject)ReadJson(path)["metadata"]!.DeepClone())
            .OrderByDescending(item => item["createdAt"]!.GetValue<string>()).ToArray();
    });

    public JsonObject ReadCheckpoint(string id, string checkpointId) => WithReadGate(() =>
    {
        ReadRecord(id);
        var path = CheckpointPath(id, checkpointId);
        if (!File.Exists(path)) throw new ApiException(404, "Checkpoint not found.");
        return (JsonObject)ReadJson(path)["project"]!.DeepClone();
    });

    private T WithReadGate<T>(Func<T> read)
    {
        // Windows can reject a new open while an atomic rename is pending, even
        // when existing readers permit deletion. Keep the whole read outside
        // replacement and recovery deletion, sharing the existing writer gate.
        _gate.Wait();
        try { return read(); }
        finally { _gate.Release(); }
    }

    private JsonObject ReadRecord(string id)
    {
        var path = ProjectPath(id);
        if (!File.Exists(path)) throw new ApiException(404, "Project not found.");
        return ReadJson(path);
    }
    private string ProjectDirectory(string id) => Path.Combine(_projectsRoot, ProjectValidator.SafeId(id));
    private string ProjectPath(string id) => Path.Combine(ProjectDirectory(id), "project.json");
    private string RecoveryPath(string id) => Path.Combine(ProjectDirectory(id), "recovery.json");
    private string CheckpointPath(string id, string checkpointId) => Path.Combine(ProjectDirectory(id), "checkpoints", ProjectValidator.SafeId(checkpointId) + ".json");
    private static JsonObject Record(JsonObject project) => new()
    {
        ["project"] = project.DeepClone(), ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
    };
    private static bool SameAuthoredContent(JsonObject left, JsonObject right)
    {
        var a = (JsonObject)left.DeepClone();
        var b = (JsonObject)right.DeepClone();
        // Revision and generated reference snapshots change during an explicit save.
        // All authored nodes, links, rules, dependencies and attachments must still match.
        a.Remove("revision"); b.Remove("revision");
        a.Remove("snapshots"); b.Remove("snapshots");
        return JsonNode.DeepEquals(a, b);
    }
    private static void ValidateIdentity(string id, JsonObject project)
    {
        ProjectValidator.SafeId(id);
        ProjectValidator.Validate(project);
        if (!string.Equals(id, ProjectValidator.Text(project, "id", 96), StringComparison.Ordinal))
            throw new ApiException(400, "The project identifier does not match the requested project.");
    }
    private static void CheckRevision(JsonObject current, int expected)
    {
        if (ProjectValidator.Integer(current, "revision") != expected)
            throw new ApiException(409, "This project has changed since it was loaded. Keep your draft and reload before saving.");
    }
    private static JsonObject ReadJson(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return JsonNode.Parse(stream) as JsonObject ?? throw new IOException("Invalid project storage document.");
    }
    private static async Task AtomicWrite(string path, JsonObject document)
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
    public void Dispose()
    {
        _ownership.Dispose();
        _gate.Dispose();
    }
}
