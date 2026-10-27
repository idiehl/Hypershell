using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

/// <summary>
/// Writes a bounded, inspectable trace for each prompt beneath the selected
/// local project. Failures in this observer never interrupt the AI cycle.
/// </summary>
public sealed partial class WorkspaceAiRunDiagnosticSession : IDisposable
{
    public const string RelativeRoot = ".hypershell/ai-runs";
    private const int MaximumEventCharacters = 1024 * 1024;
    private const long MaximumEventFileBytes = 24L * 1024 * 1024;
    private const int MaximumRetainedRuns = 30;
    private static readonly TimeSpan MaximumRetention = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32
    };

    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly string _summaryPath;
    private readonly string _eventsPath;
    private readonly string _responsePath;
    private readonly string _conversationId;
    private readonly string _contextId;
    private readonly string _providerId;
    private readonly string _modelId;
    private readonly string _workingDirectory;
    private readonly WorkspaceAiTaskRoute _route;
    private int _eventCount;
    private bool _completed;
    private bool _disposed;

    private WorkspaceAiRunDiagnosticSession(
        string runDirectory,
        string conversationId,
        string contextId,
        string providerId,
        string modelId,
        string workingDirectory,
        WorkspaceAiTaskRoute route,
        string prompt)
    {
        RunDirectory = runDirectory;
        _summaryPath = Path.Combine(runDirectory, "summary.json");
        _eventsPath = Path.Combine(runDirectory, "events.jsonl");
        _responsePath = Path.Combine(runDirectory, "response.md");
        _conversationId = conversationId;
        _contextId = contextId;
        _providerId = providerId;
        _modelId = modelId;
        _workingDirectory = workingDirectory;
        _route = route;

        File.WriteAllText(
            Path.Combine(runDirectory, "prompt.md"),
            SanitizeAndBound(prompt, MaximumEventCharacters),
            new UTF8Encoding(false));
        WriteSummary("running", string.Empty);
        Record("cycle_started", new
        {
            route = route.Name,
            availableTools = route.ToolNames,
            workingDirectory
        });
    }

    public string RunDirectory { get; }

    public static WorkspaceAiRunDiagnosticSession? TryStart(
        string workingDirectory,
        string conversationId,
        string contextId,
        string providerId,
        string modelId,
        WorkspaceAiTaskRoute route,
        string prompt)
    {
        try
        {
            var normalized = WorkspaceAiWorkingDirectory.Normalize(
                workingDirectory);
            if (normalized.Length == 0
                || WorkspaceLocation.TryParse(normalized, out var location)
                   && location.IsSsh)
            {
                return null;
            }

            var root = Path.Combine(
                normalized,
                ".hypershell",
                "ai-runs");
            Directory.CreateDirectory(root);
            EnsureGitIgnore(root);
            EnsureReadme(root);
            Prune(root);
            var runName =
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{SafeId(conversationId)}-{Guid.NewGuid():N}";
            if (runName.Length > 88)
            {
                runName = runName[..88];
            }

            var runDirectory = Path.Combine(root, runName);
            Directory.CreateDirectory(runDirectory);
            return new WorkspaceAiRunDiagnosticSession(
                runDirectory,
                conversationId,
                contextId,
                providerId,
                modelId,
                normalized,
                route,
                prompt);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or JsonException)
        {
            return null;
        }
    }

    public void Record(string eventType, object? details)
    {
        if (_disposed || string.IsNullOrWhiteSpace(eventType))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (File.Exists(_eventsPath)
                    && new FileInfo(_eventsPath).Length
                       >= MaximumEventFileBytes)
                {
                    return;
                }

                var serialized = JsonSerializer.Serialize(
                    details,
                    JsonOptions);
                serialized = SanitizeAndBound(
                    serialized,
                    MaximumEventCharacters);
                using var detailDocument = JsonDocument.Parse(serialized);
                var line = JsonSerializer.Serialize(new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    sequence = ++_eventCount,
                    type = eventType.Trim(),
                    details = detailDocument.RootElement.Clone()
                });
                File.AppendAllText(
                    _eventsPath,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or JsonException
                                         or ArgumentException)
        {
        }
    }

    public void Complete(
        string status,
        string response,
        string error = "")
    {
        if (_disposed || _completed)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _stopwatch.Stop();
                File.WriteAllText(
                    _responsePath,
                    SanitizeAndBound(response, MaximumEventCharacters),
                    new UTF8Encoding(false));
                WriteSummary(status, error);
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or JsonException
                                         or ArgumentException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_completed)
        {
            Complete("interrupted", string.Empty,
                "The work cycle ended before a final response was recorded.");
        }

        _disposed = true;
    }

    private void WriteSummary(string status, string error)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status,
            startedAtUtc = _startedAtUtc,
            completedAtUtc = status == "running"
                ? (DateTimeOffset?)null
                : DateTimeOffset.UtcNow,
            elapsedMilliseconds = status == "running"
                ? (long?)null
                : _stopwatch.ElapsedMilliseconds,
            conversationId = _conversationId,
            contextId = _contextId,
            providerId = _providerId,
            modelId = _modelId,
            workingDirectory = _workingDirectory,
            taskRoute = _route.Name,
            availableTools = _route.ToolNames,
            eventCount = _eventCount,
            error = SanitizeAndBound(error, 64 * 1024)
        }, JsonOptions);
        WriteAtomic(_summaryPath, Encoding.UTF8.GetBytes(payload));
    }

    private static void EnsureReadme(string root)
    {
        var path = Path.Combine(root, "README.md");
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(
            path,
            """
            # Hypershell AI run details

            Each child directory contains the prompt, final response, summary, and bounded chronological events for one AI work cycle. Secrets and screenshot payloads are redacted. Runs are temporary: Hypershell keeps the newest 30 and removes entries older than 14 days.

            This directory is diagnostic output, not project source. Its nested .gitignore keeps the run bundles out of Git status while leaving them visible on disk.
            """,
            new UTF8Encoding(false));
    }

    private static void EnsureGitIgnore(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(
            path,
            "# Local Hypershell AI diagnostic bundles\n*\n",
            new UTF8Encoding(false));
    }

    private static void Prune(string root)
    {
        var now = DateTimeOffset.UtcNow;
        var directories = Directory.EnumerateDirectories(root)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .ToList();
        foreach (var info in directories.Skip(MaximumRetainedRuns).Concat(
                     directories.Where(info =>
                         now - info.CreationTimeUtc > MaximumRetention))
                 .DistinctBy(info => info.FullName))
        {
            try
            {
                info.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void WriteAtomic(string path, byte[] payload)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temporary, payload);
        File.Move(temporary, path, overwrite: true);
    }

    private static string SafeId(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Where(char.IsAsciiLetterOrDigit)
            .Take(12)
            .ToArray());
        return safe.Length == 0 ? "conversation" : safe;
    }

    private static string SanitizeAndBound(string? value, int maximum)
    {
        var result = (value ?? string.Empty).Replace("\0", string.Empty);
        result = PrivateKeyPattern().Replace(
            result,
            "[REDACTED_PRIVATE_KEY]");
        result = BearerPattern().Replace(
            result,
            "$1[REDACTED_TOKEN]");
        result = TokenPattern().Replace(
            result,
            "[REDACTED_TOKEN]");
        result = DataImagePattern().Replace(
            result,
            "data:image/[SCREENSHOT_PAYLOAD_OMITTED]");
        return result.Length <= maximum
            ? result
            : result[..Math.Max(0, maximum - 24)] + "\n[DIAGNOSTIC_TRUNCATED]";
    }

    [GeneratedRegex(
        "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----.*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(
        "((?:Authorization|authorization)[\\\"']?\\s*[:=]\\s*[\\\"']?Bearer\\s+)[A-Za-z0-9._~+/-]{16,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(
        "(?:sk|hps|qwen|dashscope)[-_][A-Za-z0-9_-]{16,}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(
        "data:image/[^;,\\\"]+(?:;base64)?,[A-Za-z0-9+/=]{128,}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DataImagePattern();
}
