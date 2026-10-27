using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperLib.Core;

/// <summary>
/// A single-writer, cross-process JSON store. Immutable journal records are the commit point;
/// Content/library.json, Library, and each Work's Content directory are readable projections.
/// User source projects and imported media are never removed by hierarchy edits or undo.
/// </summary>
public sealed class ContentStore
{
    private const int MaximumStateBytes = 32 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LoadedStore? _cachedStore;
    private List<JournalFileStamp> _cachedJournalFiles = [];
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".avif"] = "image/avif", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".m4a"] = "audio/mp4", [".pdf"] = "application/pdf"
    };

    public ContentStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    }

    public string RootPath { get; }
    public long MaximumMediaBytes { get; init; } = 64L * 1024 * 1024;
    public string? LastRecoveryMessage { get; private set; }
    /// <summary>Resolves the active (not unbuilt source) template for a Work ID.</summary>
    public Func<string, TemplateDefinition?>? TemplateResolver { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
        => await WithLockAsync(async () => { await LoadOrInitializeAsync(cancellationToken); return true; }, cancellationToken);

    public Task<LibraryState> ReadAsync(CancellationToken cancellationToken = default)
        => WithLockAsync(async () => ContentJson.Clone((await LoadOrInitializeAsync(cancellationToken)).State), cancellationToken);

    public Task<LibraryState> ApplyAsync(OperationBatch batch, CancellationToken cancellationToken = default)
        => WithLockAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(batch);
            var loaded = await LoadOrInitializeAsync(cancellationToken);
            CheckRevision(batch.ExpectedRevision, loaded.State.Revision);
            TemplateValidator.Require(batch.Operations is { Count: > 0 and <= 2000 }, "A batch needs between 1 and 2,000 operations.");
            TemplateValidator.Require(batch.Operations.All(operation => operation is not null), "Operations cannot be null.");
            TemplateValidator.Require(batch.Description is { Length: <= 500 }, "Operation description is too long.");
            var next = ContentJson.Clone(loaded.State);
            var existingWorks = loaded.State.Nodes.Where(node => node.Type == "work").Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var templates = new Dictionary<string, TemplateDefinition?>(StringComparer.Ordinal);
            var starters = new Dictionary<string, TemplateDefinition>(StringComparer.Ordinal);
            TemplateDefinition ResolveStarter(string starterId)
            {
                if (!starters.TryGetValue(starterId, out var template)) starters[starterId] = template = ReadStarter(starterId);
                return template;
            }
            TemplateDefinition? ResolveNextTemplate(string workId)
            {
                if (templates.TryGetValue(workId, out var cached)) return cached;
                var work = next.Nodes.FirstOrDefault(node => node.Id == workId && node.Type == "work");
                // A new Work has no active renderer yet. Its chosen catalog contract must also
                // apply to pages and appearance created later in this same atomic batch.
                var definition = work?.StarterId is not null && !existingWorks.Contains(workId)
                    ? ResolveStarter(work.StarterId)
                    : TemplateResolver?.Invoke(workId) ?? ReadWorkTemplate(workId) ?? (work?.StarterId is not null
                        ? ResolveStarter(work.StarterId) : StarterTemplate.Create());
                templates[workId] = definition;
                return definition;
            }
            foreach (var operation in batch.Operations) ApplyOperation(next, operation, ResolveNextTemplate, ResolveStarter);
            ContentRules.DerivePaths(next);
            SetPageRevisions(loaded.State, next, loaded.State.Revision + 1);
            next.Revision = checked(loaded.State.Revision + 1);
            ContentRules.Validate(next, ResolveNextTemplate);
            return await CommitAsync(loaded.State, next, "apply", batch.Description, null, cancellationToken);
        }, cancellationToken);

    public Task<LibraryState> UndoAsync(long expectedRevision, CancellationToken cancellationToken = default)
        => WithLockAsync(async () =>
        {
            var loaded = await LoadOrInitializeAsync(cancellationToken);
            CheckRevision(expectedRevision, loaded.State.Revision);
            TemplateValidator.Require(loaded.Undo.Count > 0, "There is nothing to undo.");
            var target = loaded.Undo[^1];
            var next = ContentJson.Clone(target.Before!);
            SetPageRevisions(loaded.State, next, loaded.State.Revision + 1);
            next.Revision = checked(loaded.State.Revision + 1);
            ContentRules.DerivePaths(next);
            // Restoring data remains possible after a template change; compatibility is a renderer concern.
            ContentRules.Validate(next);
            return await CommitAsync(loaded.State, next, "undo", "Undo: " + target.Description, target.TransactionId, cancellationToken);
        }, cancellationToken);

    public Task<LibraryState> RedoAsync(long expectedRevision, CancellationToken cancellationToken = default)
        => WithLockAsync(async () =>
        {
            var loaded = await LoadOrInitializeAsync(cancellationToken);
            CheckRevision(expectedRevision, loaded.State.Revision);
            TemplateValidator.Require(loaded.Redo.Count > 0, "There is nothing to redo.");
            var target = loaded.Redo[^1];
            var next = ContentJson.Clone(target.After);
            SetPageRevisions(loaded.State, next, loaded.State.Revision + 1);
            next.Revision = checked(loaded.State.Revision + 1);
            ContentRules.DerivePaths(next);
            ContentRules.Validate(next);
            return await CommitAsync(loaded.State, next, "redo", "Redo: " + target.Description, target.TransactionId, cancellationToken);
        }, cancellationToken);

    public async Task<MediaAsset> ImportMediaAsync(Stream input, string fileName, string? contentType = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        TemplateValidator.Require(!string.IsNullOrWhiteSpace(fileName) && fileName.Length <= 200
            && fileName == Path.GetFileName(fileName) && fileName.IndexOfAny(['/', '\\', ':']) < 0 && !fileName.Any(char.IsControl),
            "Media filenames cannot contain paths or control characters.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        TemplateValidator.Require(MediaTypes.TryGetValue(extension, out var expectedType), "This media type is not supported. Import images, audio, video, or PDF files.");
        TemplateValidator.Require(string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream"
            || string.Equals(contentType.Split(';')[0].Trim(), expectedType, StringComparison.OrdinalIgnoreCase), "Media content type does not match its extension.");
        TemplateValidator.Require(MaximumMediaBytes > 0, "Media size limit must be positive.");
        return await WithLockAsync(async () =>
        {
            var temporary = ResolvePath($"Media/.import-{Guid.NewGuid():N}.tmp");
            long length = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    var buffer = new byte[81920];
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        length += count;
                        TemplateValidator.Require(length <= MaximumMediaBytes, $"Media exceeds the {MaximumMediaBytes / 1024 / 1024} MiB limit.");
                        hash.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    }
                    TemplateValidator.Require(length > 0, "Empty media files cannot be imported.");
                    await output.FlushAsync(cancellationToken);
                    output.Flush(true);
                }
                ValidateMediaSignature(temporary, extension);
                var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
                var relative = $"Media/{digest}{extension}";
                var destination = ResolvePath(relative);
                if (!File.Exists(destination)) File.Move(temporary, destination);
                else File.Delete(temporary);
                var asset = new MediaAsset { Id = digest, FileName = fileName, RelativePath = relative,
                    Url = "/media/" + digest + extension, ContentType = expectedType!, Length = length, Sha256 = digest };
                await WriteJsonAtomicAsync(ResolvePath($"Media/{digest}{extension}.metadata.json"), asset, cancellationToken);
                return asset;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, cancellationToken);
    }

    /// <summary>Resolves a portable path beneath this root, rejecting traversal and reparse points.</summary>
    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        TemplateValidator.Require(!Path.IsPathRooted(relativePath) && !relativePath.Contains(':')
            && !relativePath.Split(['/', '\\']).Any(part => part is ".." or "."), "Paths must stay inside the HyperLib root.");
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        TemplateValidator.Require(fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, PathComparison), "Path escapes the HyperLib root.");
        RejectReparsePoints(fullPath);
        return fullPath;
    }

    private TemplateDefinition ReadStarter(string starterId)
        => ReusableTemplateCatalog.ReadDefinition(RootPath, starterId);

    private TemplateDefinition? ReadWorkTemplate(string workId)
    {
        TemplateValidator.ValidateId(workId, "work");
        var path = ResolvePath(workId == "library" ? "Presentation/authoring.template.json" : $"Works/{workId}/authoring.template.json");
        return File.Exists(path) ? ReusableTemplateCatalog.ReadJson<TemplateDefinition>(path, TemplateValidator.ValidateDefinition) : null;
    }

    private async Task<T> WithLockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectory(RootPath);
            foreach (var relative in new[] { ".hyperlib", ".hyperlib/journal", "Content", "Media", "Library" }) EnsureDirectory(ResolvePath(relative));
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            return await operation();
        }
        finally { _gate.Release(); }
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(ResolvePath(".hyperlib/store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (stopwatch.Elapsed < TimeSpan.FromSeconds(15)) { await Task.Delay(40, cancellationToken); }
        }
    }

    private async Task<LoadedStore> LoadOrInitializeAsync(CancellationToken cancellationToken)
    {
        var directory = ResolvePath(".hyperlib/journal");
        var paths = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
        {
            var projectionPath = ResolvePath("Content/library.json");
            var state = File.Exists(projectionPath)
                ? await ReadJsonAsync<LibraryState>(projectionPath, cancellationToken)
                : SeedLibrary.Create();
            ContentRules.DerivePaths(state);
            ContentRules.Validate(state);
            var record = new JournalRecord { Kind = "initialize", Description = "Initialize library", After = state, Revision = state.Revision, StateHash = StateHash(state) };
            await WriteJsonAtomicAsync(JournalPath(record.Revision), record, cancellationToken, overwrite: false);
            await WriteProjectionsAsync(state, null, cancellationToken);
            return new LoadedStore(state, [], []);
        }
        var fileStamps = paths.Select(path =>
        {
            RejectReparsePoints(path);
            var info = new FileInfo(path);
            return new JournalFileStamp(path, info.Length, info.LastWriteTimeUtc.Ticks);
        }).ToList();
        if (_cachedStore is not null && fileStamps.SequenceEqual(_cachedJournalFiles))
        {
            await RepairProjectionIfNeededAsync(_cachedStore.State, cancellationToken);
            return _cachedStore;
        }
        var canAppend = _cachedStore is not null && fileStamps.Count > _cachedJournalFiles.Count
            && fileStamps.Take(_cachedJournalFiles.Count).SequenceEqual(_cachedJournalFiles);
        LibraryState? current = canAppend ? _cachedStore!.State : null;
        var undo = canAppend ? new List<JournalRecord>(_cachedStore!.Undo) : [];
        var redo = canAppend ? new List<JournalRecord>(_cachedStore!.Redo) : [];
        try
        {
            foreach (var path in paths.Skip(canAppend ? _cachedJournalFiles.Count : 0))
            {
                RejectReparsePoints(path);
                var record = await ReadJsonAsync<JournalRecord>(path, cancellationToken);
                if (record.SchemaVersion != 1 || record.After is null || record.After.Revision != record.Revision || Path.GetFileName(path) != $"{record.Revision:D20}.json")
                    throw new ContentRecoveryException($"Invalid committed journal record '{Path.GetFileName(path)}'. Preserve the journal and restore a known backup.");
                if (record.StateHash != StateHash(record.After)) throw new ContentRecoveryException("Committed content checksum does not match. No content has been overwritten.");
                ContentRules.Validate(record.After);
                if (current is null)
                {
                    if (record.Kind != "initialize") throw new ContentRecoveryException("The initial journal record is missing.");
                }
                else
                {
                    if (record.Revision != current.Revision + 1 || record.Before?.Revision != current.Revision || StateHash(record.Before!) != StateHash(current))
                        throw new ContentRecoveryException("The committed journal has a missing revision. No content has been overwritten.");
                    switch (record.Kind)
                    {
                        case "apply": undo.Add(record); redo.Clear(); break;
                        case "undo":
                            if (undo.Count == 0 || undo[^1].TransactionId != record.TargetTransactionId) throw new ContentRecoveryException("Undo journal is inconsistent.");
                            redo.Add(undo[^1]); undo.RemoveAt(undo.Count - 1); break;
                        case "redo":
                            if (redo.Count == 0 || redo[^1].TransactionId != record.TargetTransactionId) throw new ContentRecoveryException("Redo journal is inconsistent.");
                            undo.Add(redo[^1]); redo.RemoveAt(redo.Count - 1); break;
                        default: throw new ContentRecoveryException("Unknown committed journal operation.");
                    }
                }
                current = record.After;
            }
        }
        catch (Exception error) when (error is JsonException or ContentValidationException)
        {
            throw new ContentRecoveryException("A committed journal record is damaged. The store stopped without overwriting data; restore the affected record from backup.", error);
        }
        var loaded = new LoadedStore(current!, undo, redo);
        await RepairProjectionIfNeededAsync(loaded.State, cancellationToken);
        _cachedStore = loaded;
        _cachedJournalFiles = fileStamps;
        return loaded;
    }

    private async Task RepairProjectionIfNeededAsync(LibraryState state, CancellationToken cancellationToken)
    {
        var path = ResolvePath("Content/library.json");
        var needsRepair = !File.Exists(path);
        if (!needsRepair)
        {
            try
            {
                var materialized = await File.ReadAllBytesAsync(path, cancellationToken);
                needsRepair = !materialized.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(state, ContentJson.Options));
            }
            catch (IOException) { needsRepair = true; }
        }
        var indexPath = ResolvePath(".hyperlib/projections.json");
        if (!File.Exists(indexPath)) needsRepair = true;
        else
        {
            try
            {
                var index = await ReadJsonAsync<ProjectionIndex>(indexPath, cancellationToken);
                needsRepair |= index.SchemaVersion != 2 || index.Revision != state.Revision;
                if (!needsRepair)
                    needsRepair = state.Nodes.Where(node => node.Type == "work").Any(work => !File.Exists(ResolvePath($"Works/{work.Id}/Content/work.json")))
                        || index.WorkPaths.Values.Any(path => !File.Exists(ResolvePath(path)) && !Directory.Exists(ResolvePath(path)));
            }
            catch (JsonException) { needsRepair = true; }
        }
        if (needsRepair)
        {
            await WriteProjectionsAsync(state, null, cancellationToken);
            LastRecoveryMessage = $"Recovered readable content and hierarchy projections from committed revision {state.Revision}.";
        }
    }

    private async Task<LibraryState> CommitAsync(LibraryState before, LibraryState after, string kind, string description,
        string? target, CancellationToken cancellationToken)
    {
        TemplateValidator.Require(JsonSerializer.SerializeToUtf8Bytes(after, ContentJson.Options).Length <= MaximumStateBytes,
            "Library content exceeds the 32 MiB JSON limit. Keep large media in the media library.");
        var record = new JournalRecord { Kind = kind, Description = description, Before = before, After = after,
            Revision = after.Revision, TargetTransactionId = target, StateHash = StateHash(after) };
        // Commit the complete record first. All subsequent materialization is safely replayable.
        await WriteJsonAtomicAsync(JournalPath(record.Revision), record, cancellationToken, overwrite: false);
        try { await WriteProjectionsAsync(after, before, CancellationToken.None); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ContentValidationException)
        {
            LastRecoveryMessage = $"Revision {after.Revision} is committed; a projection will be repaired on the next read: {error.Message}";
        }
        return ContentJson.Clone(after);
    }

    private void ApplyOperation(LibraryState state, ContentOperation operation, Func<string, TemplateDefinition?> resolveTemplate,
        Func<string, TemplateDefinition> resolveStarter)
    {
        ArgumentNullException.ThrowIfNull(operation);
        TemplateValidator.Require(operation.StarterId is null || operation.Kind == "createNode" && operation.NodeType == "work",
            "A starter may only be selected when creating a Work; edit its owned template in Studio afterward.");
        LibraryNode Find() => state.Nodes.FirstOrDefault(node => node.Id == operation.Id)
            ?? throw new ContentValidationException($"Node '{operation.Id}' does not exist.");
        switch (operation.Kind)
        {
            case "createNode":
            {
                var id = string.IsNullOrWhiteSpace(operation.Id) ? Guid.NewGuid().ToString("N") : operation.Id;
                ContentRules.ValidateNodeId(id);
                TemplateValidator.Require(!state.Nodes.Any(node => string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase)) && id != state.LibraryId, "The node ID already exists.");
                var node = new LibraryNode { Id = id, Type = operation.NodeType ?? "page", ParentId = operation.ParentId ?? state.LibraryId,
                    Title = operation.Title ?? operation.Page?.Title ?? DefaultTitle(operation.NodeType), Cover = operation.Cover,
                    StarterId = operation.NodeType == "work" ? operation.StarterId ?? BaseTemplateCatalog.DefaultStarterId : null,
                    Order = operation.Order ?? (state.Nodes.Where(node => node.ParentId == (operation.ParentId ?? state.LibraryId)).Select(node => node.Order).DefaultIfEmpty(-1).Max() + 1),
                    Appearance = operation.Appearance is null ? [] : ContentJson.Clone(operation.Appearance) };
                if (node.Type == "work")
                {
                    var appearance = resolveStarter(node.StarterId!).Appearance.Where(field => field.DefaultValue is not null)
                        .ToDictionary(field => field.Id, field => field.DefaultValue!.Value.Clone(), StringComparer.Ordinal);
                    foreach (var pair in node.Appearance) appearance[pair.Key] = pair.Value;
                    node.Appearance = appearance;
                }
                state.Nodes.Add(node);
                ContentRules.DerivePaths(state);
                if (node.Type == "page")
                {
                    TemplateValidator.Require(node.WorkId is not null, "A page must belong to a Work.");
                    var page = operation.Page is null ? TemplateValidator.CreateDefaultPage(id, node.WorkId!, node.Title, resolveTemplate(node.WorkId!)) : ContentJson.Clone(operation.Page);
                    page.Id = id; page.WorkId = node.WorkId!; page.Title = node.Title;
                    state.Pages.Add(id, page);
                }
                else TemplateValidator.Require(operation.Page is null, "Only page nodes may include page content.");
                break;
            }
            case "updateNode":
            {
                var node = Find();
                TemplateValidator.Require(operation.NodeType is null || operation.NodeType == node.Type, "Node types cannot be changed.");
                if (operation.Title is not null) node.Title = operation.Title;
                if (operation.Cover is not null) node.Cover = operation.Cover.Length == 0 ? null : operation.Cover;
                if (operation.Order is not null) node.Order = operation.Order.Value;
                if (operation.Appearance is not null) node.Appearance = ContentJson.Clone(operation.Appearance);
                if (node.Type == "page") state.Pages[node.Id].Title = node.Title;
                break;
            }
            case "moveNode":
            {
                var node = Find();
                var originalPageWorks = state.Pages.ToDictionary(pair => pair.Key, pair => pair.Value.WorkId, StringComparer.Ordinal);
                TemplateValidator.Require(operation.ParentId is not null, "Move needs a parent ID.");
                node.ParentId = operation.ParentId;
                if (operation.Order is not null) node.Order = operation.Order.Value;
                ContentRules.DerivePaths(state);
                TemplateValidator.Require(state.Nodes.Where(item => item.Type == "page").All(page => originalPageWorks[page.Id] == page.WorkId),
                    "Pages cannot move between Works because their template contracts may differ. Move the complete Work instead.");
                break;
            }
            case "deleteNode":
            {
                var node = Find();
                var ids = new HashSet<string>(StringComparer.Ordinal) { node.Id };
                bool grew;
                do
                {
                    grew = false;
                    foreach (var child in state.Nodes) if (child.ParentId is not null && ids.Contains(child.ParentId)) grew |= ids.Add(child.Id);
                } while (grew);
                state.Nodes.RemoveAll(item => ids.Contains(item.Id));
                foreach (var id in ids) state.Pages.Remove(id);
                break;
            }
            case "savePage":
            {
                TemplateValidator.Require(operation.Page is not null, "Save page needs a page document.");
                var id = operation.Id ?? operation.Page!.Id;
                var node = state.Nodes.FirstOrDefault(item => item.Id == id && item.Type == "page") ?? throw new ContentValidationException("Page does not exist.");
                var page = ContentJson.Clone(operation.Page!);
                TemplateValidator.Require(page.Id == id && page.WorkId == node.WorkId, "Page identity and Work cannot be changed by saving content.");
                CheckRevision(page.Revision, state.Pages[id].Revision);
                // Older clients omit this authoring metadata; a content save cannot revert it.
                page.UsesEditableChrome |= state.Pages[id].UsesEditableChrome;
                node.Title = page.Title;
                state.Pages[id] = page;
                break;
            }
            case "setAppearance":
                TemplateValidator.Require(operation.Appearance is not null, "Appearance settings are required.");
                if (operation.Id is null || operation.Id == state.LibraryId) state.Appearance = ContentJson.Clone(operation.Appearance!);
                else Find().Appearance = ContentJson.Clone(operation.Appearance!);
                break;
            case "setLabels":
                TemplateValidator.Require(operation.Labels is not null, "Labels are required.");
                foreach (var label in operation.Labels!) state.Labels[label.Key] = label.Value;
                break;
            default: throw new ContentValidationException($"Unknown operation kind '{operation.Kind}'.");
        }
    }

    private async Task WriteProjectionsAsync(LibraryState state, LibraryState? previous, CancellationToken cancellationToken)
    {
        ProjectionIndex? oldIndex = null;
        var indexPath = ResolvePath(".hyperlib/projections.json");
        if (File.Exists(indexPath))
        {
            try { oldIndex = await ReadJsonAsync<ProjectionIndex>(indexPath, cancellationToken); }
            catch (JsonException) { }
        }
        if (oldIndex is null && previous is not null) oldIndex = IndexFor(previous);
        var desired = IndexFor(state);
        // Retire stale top-level paths instead of deleting data. This also protects files a user
        // placed inside an old projection; source code always lives separately in Works/{id}.
        if (oldIndex is not null)
        {
            var obsolete = oldIndex.Paths.Where(pair => !desired.Paths.TryGetValue(pair.Key, out var value) || value != pair.Value)
                .Concat(oldIndex.WorkPaths.Where(pair => !desired.WorkPaths.TryGetValue(pair.Key, out var value) || value != pair.Value))
                .Select(pair => pair.Value).OrderBy(path => path.Count(character => character == '/')).ToList();
            var retiredParents = new List<string>();
            foreach (var relative in obsolete)
            {
                var segments = relative.Split('/');
                TemplateValidator.Require(relative.StartsWith("Library/", StringComparison.Ordinal)
                    || segments.Length >= 3 && segments[0] == "Works" && segments[2] == "Content", "Invalid content projection path.");
                if (retiredParents.Any(parent => relative.StartsWith(parent + "/", StringComparison.Ordinal))) continue;
                var path = ResolvePath(relative);
                if (!Directory.Exists(path) && !File.Exists(path)) continue;
                var retiredDirectory = ResolvePath($".hyperlib/retired/{state.Revision:D20}");
                EnsureDirectory(retiredDirectory);
                var destination = Path.Combine(retiredDirectory, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
                RejectReparsePoints(destination);
                if (Directory.Exists(path)) { Directory.Move(path, destination); retiredParents.Add(relative); }
                else File.Move(path, destination);
            }
        }
        foreach (var node in state.Nodes.OrderBy(node => node.DirectoryPath!.Count(character => character == '/')))
        {
            var path = ResolvePath(node.DirectoryPath!);
            if (node.Type == "page")
            {
                EnsureDirectory(Path.GetDirectoryName(path)!);
                await WriteJsonAtomicAsync(path, state.Pages[node.Id], cancellationToken);
            }
            else
            {
                EnsureDirectory(path);
                await WriteJsonAtomicAsync(Path.Combine(path, ".node.json"), node, cancellationToken);
            }
        }
        await WriteWorkProjectionsAsync(state, desired, cancellationToken);
        await WriteJsonAtomicAsync(ResolvePath("Content/library.json"), state, cancellationToken);
        await WriteJsonAtomicAsync(indexPath, desired, cancellationToken);
    }

    private static ProjectionIndex IndexFor(LibraryState state)
    {
        var works = state.Nodes.Where(node => node.Type == "work").ToDictionary(node => node.Id, StringComparer.Ordinal);
        return new()
        {
            SchemaVersion = 2, Revision = state.Revision,
            Paths = state.Nodes.ToDictionary(node => node.Id, node => node.DirectoryPath!, StringComparer.Ordinal),
            WorkPaths = state.Nodes.Where(node => node.WorkId is not null).ToDictionary(node => node.Id, node =>
            {
                var work = works[node.WorkId!];
                var insideWork = node.Id == work.Id ? "" : node.DirectoryPath![work.DirectoryPath!.Length..];
                return $"Works/{work.Id}/Content{insideWork}";
            }, StringComparer.Ordinal)
        };
    }

    private async Task WriteWorkProjectionsAsync(LibraryState state, ProjectionIndex index, CancellationToken cancellationToken)
    {
        foreach (var work in state.Nodes.Where(node => node.Type == "work"))
        {
            var nodes = state.Nodes.Where(node => node.WorkId == work.Id).ToList();
            var pages = state.Pages.Where(pair => pair.Value.WorkId == work.Id).ToDictionary(StringComparer.Ordinal);
            var prefix = $"Works/{work.Id}/";
            foreach (var node in nodes.OrderBy(node => index.WorkPaths[node.Id].Count(character => character == '/')))
            {
                var path = ResolvePath(index.WorkPaths[node.Id]);
                if (node.Type == "page")
                {
                    EnsureDirectory(Path.GetDirectoryName(path)!);
                    await WriteJsonAtomicAsync(path, pages[node.Id], cancellationToken);
                }
                else
                {
                    EnsureDirectory(path);
                    await WriteJsonAtomicAsync(Path.Combine(path, ".node.json"), node, cancellationToken);
                }
            }
            var snapshot = new WorkContentSnapshot
            {
                LibraryId = state.LibraryId, LibraryRevision = state.Revision, WorkId = work.Id,
                Nodes = nodes, Pages = pages,
                ContentPaths = nodes.ToDictionary(node => node.Id, node => index.WorkPaths[node.Id][prefix.Length..], StringComparer.Ordinal),
                ReferencedMedia = FindReferencedMedia(nodes, pages.Values)
            };
            await WriteJsonAtomicAsync(ResolvePath($"{prefix}Content/work.json"), snapshot, cancellationToken);
        }
    }

    private static List<WorkMediaReference> FindReferencedMedia(IEnumerable<LibraryNode> nodes, IEnumerable<PageDocument> pages)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            InspectText(node.Cover);
            foreach (var value in node.Appearance.Values) InspectValue(value);
        }
        foreach (var page in pages)
            foreach (var block in page.Blocks)
                foreach (var value in block.Values.Values) InspectValue(value);
        return paths.Order(StringComparer.Ordinal).Select(path => new WorkMediaReference
        {
            Url = "/media/" + path, LibraryRelativePath = "Media/" + path, WorkRelativePath = "../../Media/" + path
        }).ToList();

        void InspectValue(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String) InspectText(value.GetString());
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) InspectValue(item);
            else if (value.ValueKind == JsonValueKind.Object) foreach (var item in value.EnumerateObject()) InspectValue(item.Value);
        }
        void InspectText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsLoopback) text = uri.AbsolutePath;
            foreach (Match match in Regex.Matches(text, "(?:^|[\\s(\\\"'=])(/media/[^\\s\\\"'<>?#)\\]]+)", RegexOptions.CultureInvariant))
            {
                var path = Uri.UnescapeDataString(match.Groups[1].Value[7..]);
                if (path.Length > 0 && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl)
                    && !path.Split('/').Any(part => part is "." or ".." or "")) paths.Add(path);
            }
        }
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken, bool overwrite = true)
    {
        RejectReparsePoints(path);
        EnsureDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, ContentJson.Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            RejectReparsePoints(path);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumStateBytes * 3L) throw new ContentRecoveryException("JSON file exceeds the supported size limit.");
        return await JsonSerializer.DeserializeAsync<T>(stream, ContentJson.Options, cancellationToken)
            ?? throw new ContentRecoveryException($"Empty JSON document '{Path.GetFileName(path)}'.");
    }

    private void EnsureDirectory(string path)
    {
        RejectReparsePoints(path);
        Directory.CreateDirectory(path);
        RejectReparsePoints(path);
    }

    private void RejectReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        TemplateValidator.Require(fullPath.Equals(RootPath, PathComparison) || fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, PathComparison), "Path escapes the HyperLib root.");
        for (var candidate = fullPath; candidate is not null; candidate = Path.GetDirectoryName(candidate))
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
                TemplateValidator.Require((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0, "Symbolic links and reparse points are not allowed in content paths.");
            if (candidate.Equals(RootPath, PathComparison)) break;
        }
    }

    private string JournalPath(long revision) => ResolvePath($".hyperlib/journal/{revision:D20}.json");
    private static string StateHash(LibraryState state) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state, ContentJson.Options)));
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static void CheckRevision(long expected, long actual) { if (expected != actual) throw new ConflictException(expected, actual); }
    private static string DefaultTitle(string? type) => type switch { "collection" => "Untitled collection", "series" => "Untitled series", "work" => "Untitled work", "volume" => "Untitled volume", "part" => "Untitled part", "chapter" => "Untitled chapter", _ => "Untitled page" };

    private static void SetPageRevisions(LibraryState before, LibraryState after, long revision)
    {
        foreach (var page in after.Pages.Values)
        {
            if (!before.Pages.TryGetValue(page.Id, out var prior)) { page.Revision = revision; continue; }
            var comparison = ContentJson.Clone(page);
            comparison.Revision = prior.Revision;
            page.Revision = JsonSerializer.Serialize(comparison, ContentJson.Options) == JsonSerializer.Serialize(prior, ContentJson.Options) ? prior.Revision : revision;
        }
    }

    private static void ValidateMediaSignature(string path, string extension)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        var count = stream.Read(header);
        header = header[..count];
        var valid = extension switch
        {
            ".png" => header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => header.StartsWith(new byte[] { 255, 216, 255 }),
            ".gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
            ".webp" => count >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
            ".wav" => count >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8),
            ".pdf" => header.StartsWith("%PDF-"u8),
            ".ogg" => header.StartsWith("OggS"u8),
            ".webm" => header.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
            ".mp3" => header.StartsWith("ID3"u8) || count >= 2 && header[0] == 255 && (header[1] & 0xe0) == 0xe0,
            ".mp4" or ".m4a" or ".avif" => count >= 12 && header[4..8].SequenceEqual("ftyp"u8),
            _ => false
        };
        TemplateValidator.Require(valid, "The media file signature does not match its extension.");
    }

    private sealed class JournalRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public long Revision { get; set; }
        public string Kind { get; set; } = "";
        public string Description { get; set; } = "";
        public string StateHash { get; set; } = "";
        public string? TargetTransactionId { get; set; }
        public LibraryState? Before { get; set; }
        public LibraryState After { get; set; } = new();
    }
    private sealed class ProjectionIndex
    {
        public int SchemaVersion { get; set; }
        public long Revision { get; set; }
        public Dictionary<string, string> Paths { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> WorkPaths { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed record LoadedStore(LibraryState State, List<JournalRecord> Undo, List<JournalRecord> Redo);
    private sealed record JournalFileStamp(string Path, long Length, long ModifiedTicks);
}
