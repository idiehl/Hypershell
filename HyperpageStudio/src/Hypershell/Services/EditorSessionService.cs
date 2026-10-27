using Hypershell.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class EditorSessionService
{
    public const int MaximumDocumentCount = 50;
    public const int MaximumRecoveryContentBytes = 8 * 1024 * 1024;

    private const long MaximumSessionFileBytes = 2L * 1024L * 1024L;
    private const long MaximumRecoveryFileBytes = 64L * 1024L * 1024L;

    private readonly string _sessionPath;
    private readonly string _recoveryDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public EditorSessionService(
        string sessionPath,
        string recoveryDirectory)
    {
        _sessionPath = Path.GetFullPath(sessionPath);
        _recoveryDirectory = Path.GetFullPath(recoveryDirectory);
    }

    public async Task<EditorSessionSnapshot> LoadSessionAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = new FileInfo(_sessionPath);
            if (!file.Exists || file.Length > MaximumSessionFileBytes)
            {
                return new EditorSessionSnapshot();
            }

            await using var stream = File.OpenRead(_sessionPath);
            var snapshot = await JsonSerializer.DeserializeAsync<EditorSessionSnapshot>(
                stream,
                _jsonOptions,
                cancellationToken);
            if (snapshot is null
                || snapshot.SchemaVersion is < 1
                    or > EditorSessionSnapshot.CurrentSchemaVersion)
            {
                return new EditorSessionSnapshot();
            }

            var documents = (snapshot.Documents ?? [])
                .Where(entry => IsValidSessionId(entry.SessionId))
                .DistinctBy(entry => entry.SessionId, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumDocumentCount)
                .ToList();
            return snapshot with
            {
                SchemaVersion = EditorSessionSnapshot.CurrentSchemaVersion,
                Documents = documents,
                ActiveDocumentSessionId = IsValidSessionId(
                    snapshot.ActiveDocumentSessionId)
                    ? snapshot.ActiveDocumentSessionId
                    : string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return new EditorSessionSnapshot();
        }
        catch (UnauthorizedAccessException)
        {
            return new EditorSessionSnapshot();
        }
        catch (JsonException)
        {
            return new EditorSessionSnapshot();
        }
    }

    public async Task<EditorRecoverySnapshot?> LoadRecoveryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRecoveryPath(sessionId, out var recoveryPath))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(recoveryPath);
            if (!file.Exists || file.Length > MaximumRecoveryFileBytes)
            {
                return null;
            }

            await using var stream = File.OpenRead(recoveryPath);
            var snapshot = await JsonSerializer.DeserializeAsync<EditorRecoverySnapshot>(
                stream,
                _jsonOptions,
                cancellationToken);
            if (snapshot is null
                || snapshot.SchemaVersion is < 1
                    or > EditorRecoverySnapshot.CurrentSchemaVersion
                || !string.Equals(
                    snapshot.SessionId,
                    sessionId,
                    StringComparison.OrdinalIgnoreCase)
                || snapshot.Content is null
                || Encoding.UTF8.GetByteCount(snapshot.Content) >
                    MaximumRecoveryContentBytes)
            {
                return null;
            }

            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<EditorSessionSaveResult> SaveAsync(
        EditorSessionSnapshot session,
        IReadOnlyCollection<EditorRecoverySnapshot> recoveries,
        CancellationToken cancellationToken = default)
    {
        var documents = (session.Documents ?? [])
            .Where(entry => IsValidSessionId(entry.SessionId))
            .DistinctBy(entry => entry.SessionId, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDocumentCount)
            .ToList();
        var normalizedSession = session with
        {
            SchemaVersion = EditorSessionSnapshot.CurrentSchemaVersion,
            SavedAtUtc = DateTimeOffset.UtcNow,
            ActiveDocumentSessionId = documents.Any(entry => string.Equals(
                entry.SessionId,
                session.ActiveDocumentSessionId,
                StringComparison.OrdinalIgnoreCase))
                ? session.ActiveDocumentSessionId
                : string.Empty,
            Documents = documents
        };
        var recoveryBySessionId = recoveries
            .Where(recovery => IsValidSessionId(recovery.SessionId))
            .GroupBy(
                recovery => recovery.SessionId,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(_recoveryDirectory);
        var savedRecoveryCount = 0;
        var skippedRecoverySessionIds = new List<string>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.HadUnsavedChanges
                || !recoveryBySessionId.TryGetValue(
                    document.SessionId,
                    out var recovery))
            {
                DeleteRecovery(document.SessionId);
                continue;
            }

            if (Encoding.UTF8.GetByteCount(recovery.Content) >
                MaximumRecoveryContentBytes)
            {
                DeleteRecovery(document.SessionId);
                skippedRecoverySessionIds.Add(document.SessionId);
                continue;
            }

            var normalizedRecovery = recovery with
            {
                SchemaVersion = EditorRecoverySnapshot.CurrentSchemaVersion,
                SavedAtUtc = DateTimeOffset.UtcNow,
                SessionId = document.SessionId
            };
            await WriteAtomicallyAsync(
                GetRecoveryPath(document.SessionId),
                JsonSerializer.Serialize(normalizedRecovery, _jsonOptions),
                cancellationToken);
            savedRecoveryCount++;
        }

        await WriteAtomicallyAsync(
            _sessionPath,
            JsonSerializer.Serialize(normalizedSession, _jsonOptions),
            cancellationToken);
        return new EditorSessionSaveResult(
            savedRecoveryCount,
            skippedRecoverySessionIds);
    }

    public void DeleteRecovery(string sessionId)
    {
        if (!TryGetRecoveryPath(sessionId, out var recoveryPath))
        {
            return;
        }

        try
        {
            if (File.Exists(recoveryPath))
            {
                File.Delete(recoveryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string GetRecoveryPath(string sessionId)
    {
        if (!TryGetRecoveryPath(sessionId, out var recoveryPath))
        {
            throw new ArgumentException("The editor session ID is invalid.", nameof(sessionId));
        }

        return recoveryPath;
    }

    private bool TryGetRecoveryPath(
        string sessionId,
        out string recoveryPath)
    {
        recoveryPath = string.Empty;
        if (!IsValidSessionId(sessionId))
        {
            return false;
        }

        recoveryPath = Path.Combine(
            _recoveryDirectory,
            $"{sessionId.ToLowerInvariant()}.json");
        return true;
    }

    private static bool IsValidSessionId(string? sessionId) =>
        Guid.TryParseExact(sessionId, "N", out _);

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The state path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
