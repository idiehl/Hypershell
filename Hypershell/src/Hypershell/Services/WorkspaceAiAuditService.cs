using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public enum WorkspaceAiAuditOutcome
{
    Authorized,
    Denied,
    ConfirmationRequired,
    Succeeded,
    Failed
}

public sealed record WorkspaceAiAuditEntry
{
    public int SchemaVersion { get; init; } =
        WorkspaceAiAuditService.CurrentSchemaVersion;
    public DateTimeOffset TimestampUtc { get; init; }
    public string WorkspaceId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string RemoteHostId { get; init; } = string.Empty;
    public string WorkspaceUri { get; init; } = string.Empty;
    public WorkspaceAiOperationKind Operation { get; init; }
    public WorkspaceAiCapability Capability { get; init; }
    public string Target { get; init; } = string.Empty;
    public WorkspaceAiAuditOutcome Outcome { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class WorkspaceAiAuditService
{
    public const int CurrentSchemaVersion = 1;
    public const long DefaultMaximumLogBytes = 2L * 1024L * 1024L;

    private readonly object _gate = new();
    private readonly string _auditPath;
    private readonly long _maximumLogBytes;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    public WorkspaceAiAuditService(
        string auditPath,
        long maximumLogBytes = DefaultMaximumLogBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditPath);
        if (maximumLogBytes < 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLogBytes),
                "The audit-log limit must be at least 4 KiB.");
        }

        _auditPath = Path.GetFullPath(auditPath);
        _maximumLogBytes = maximumLogBytes;
    }

    public string AuditPath => _auditPath;

    public string PreviousAuditPath => _auditPath + ".previous";

    public void Record(WorkspaceAiAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var normalized = entry with
        {
            WorkspaceId = Limit(entry.WorkspaceId, 160),
            ConversationId = Limit(entry.ConversationId, 160),
            RemoteHostId = Limit(entry.RemoteHostId, 160),
            WorkspaceUri = Limit(entry.WorkspaceUri, 2048),
            Target = Limit(entry.Target, 512),
            Detail = Limit(entry.Detail, 512)
        };
        var line = JsonSerializer.Serialize(normalized, _jsonOptions);
        var encodedLength = Encoding.UTF8.GetByteCount(line) + 1;
        if (encodedLength > 16 * 1024)
        {
            throw new InvalidDataException(
                "The AI audit record exceeds its size limit.");
        }

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_auditPath)
                            ?? throw new InvalidOperationException(
                                "The AI audit path has no parent directory.");
            Directory.CreateDirectory(directory);
            if (File.Exists(_auditPath)
                && new FileInfo(_auditPath).Length + encodedLength
                > _maximumLogBytes)
            {
                File.Move(
                    _auditPath,
                    PreviousAuditPath,
                    overwrite: true);
            }

            using var stream = new FileStream(
                _auditPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true));
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    public IReadOnlyList<WorkspaceAiAuditEntry> ReadRecent(int maximum = 50)
    {
        maximum = Math.Clamp(maximum, 1, 200);
        lock (_gate)
        {
            if (!File.Exists(_auditPath))
            {
                return [];
            }

            try
            {
                var info = new FileInfo(_auditPath);
                if (info.Length > _maximumLogBytes)
                {
                    return [];
                }

                var entries = new Queue<WorkspaceAiAuditEntry>(maximum);
                foreach (var line in File.ReadLines(_auditPath))
                {
                    if (string.IsNullOrWhiteSpace(line)
                        || Encoding.UTF8.GetByteCount(line) > 16 * 1024)
                    {
                        continue;
                    }

                    try
                    {
                        var entry = JsonSerializer.Deserialize<WorkspaceAiAuditEntry>(
                            line,
                            _jsonOptions);
                        if (entry is null
                            || entry.SchemaVersion != CurrentSchemaVersion)
                        {
                            continue;
                        }

                        if (entries.Count == maximum)
                        {
                            entries.Dequeue();
                        }

                        entries.Enqueue(entry);
                    }
                    catch (JsonException)
                    {
                    }
                }

                return entries.Reverse().ToList();
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    private static string Limit(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
