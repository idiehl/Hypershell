using Hypershell.Profiles;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

/// <summary>
/// Persists complete displayable conversation history locally without an item
/// count cap. Cloud-sized continuation context is stored separately from the
/// complete ordered item list.
/// </summary>
public sealed class LocalAiConversationStore
{
    private const long MaximumDocumentBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _conflictDirectory;

    public LocalAiConversationStore(
        string directory,
        string conflictDirectory)
    {
        _directory = Path.GetFullPath(directory);
        _conflictDirectory = Path.GetFullPath(conflictDirectory);
    }

    public IReadOnlyList<LocalAiConversationDocument> Load(
        string profileId,
        bool openOnly = false)
    {
        ValidateId(profileId, "profile");
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            var documents = new List<LocalAiConversationDocument>();
            foreach (var path in Directory.EnumerateFiles(
                         _directory,
                         "*.conversation.json",
                         SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > MaximumDocumentBytes)
                {
                    continue;
                }

                try
                {
                    var document = JsonSerializer.Deserialize<
                                       LocalAiConversationDocument>(
                                       File.ReadAllBytes(path),
                                       JsonOptions)
                                   ?? throw new InvalidDataException(
                                       "The local conversation was empty.");
                    Validate(document);
                    if (string.Equals(
                            document.ProfileId,
                            profileId,
                            StringComparison.OrdinalIgnoreCase)
                        && (!openOnly || document.IsOpenOnThisDevice))
                    {
                        documents.Add(document);
                    }
                }
                catch (Exception exception) when (exception is
                                                  JsonException
                                                  or InvalidDataException
                                                  or IOException
                                                  or UnauthorizedAccessException
                                                  or ArgumentException)
                {
                    // A damaged history file is left in place for recovery and
                    // never blocks the remaining conversations from loading.
                }
            }

            return documents
                .OrderBy(document => document.CreatedAtUtc)
                .ThenBy(document => document.Id, StringComparer.Ordinal)
                .ToList();
        }
    }

    public void Save(LocalAiConversationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException(
                "The local conversation exceeds its 64 MiB document limit. Export or archive it before adding more history.");
        }

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            WriteAtomic(PathFor(document.Id), payload);
        }
    }

    public string WriteConflictCopy(
        LocalAiConversationDocument document,
        string role)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException(
                "The conversation conflict copy exceeds its local limit.");
        }

        lock (_gate)
        {
            Directory.CreateDirectory(_conflictDirectory);
            var path = Path.Combine(
                _conflictDirectory,
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Safe(document.Title)}-{Safe(role)}-{Guid.NewGuid():N}.conversation.json");
            WriteAtomic(path, payload);
            return path;
        }
    }

    public static string Fingerprint(LocalAiConversationDocument document)
    {
        var portable = new
        {
            document.Id,
            document.ProfileId,
            document.Title,
            document.WorkingDirectory,
            document.CompactedContext,
            document.Items
        };
        return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(
                        portable,
                        JsonOptions)))
            .ToLowerInvariant();
    }

    public static void Validate(LocalAiConversationDocument document)
    {
        if (document.SchemaVersion
            != LocalAiConversationDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "The local conversation schema is unsupported.");
        }

        ValidateId(document.Id, "conversation");
        ValidateId(document.ProfileId, "profile");
        if (string.IsNullOrWhiteSpace(document.Title)
            || document.Title.Length > 200
            || document.CompactedContext.Length
               > HypershellCloudContract.MaximumConversationCompactionCharacters
            || document.CreatedAtUtc == default
            || document.UpdatedAtUtc < document.CreatedAtUtc
            || document.UpdatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10))
        {
            throw new InvalidDataException(
                "The local conversation metadata is malformed.");
        }

        var hasProvider = !string.IsNullOrWhiteSpace(document.ProviderId);
        var hasModel = !string.IsNullOrWhiteSpace(document.ModelId);
        if (hasProvider != hasModel)
        {
            throw new InvalidDataException(
                "The local conversation model selection is incomplete.");
        }

        if (hasProvider)
        {
            try
            {
                _ = new WorkspaceAiSessionModelSelection(
                        document.ProviderId,
                        document.ModelId)
                    .ApplyTo(new WorkspaceAiProviderConfiguration());
            }
            catch (WorkspaceAiProviderConfigurationException exception)
            {
                throw new InvalidDataException(
                    "The local conversation model selection is malformed.",
                    exception);
            }
        }

        if (document.WorkingDirectory.Length > 2048
            || document.WorkingDirectory.IndexOf('\0') >= 0
            || document.WorkingDirectory.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "The local conversation working directory is malformed.");
        }

        long previousSequence = 0;
        foreach (var item in document.Items)
        {
            if (item.Sequence != previousSequence + 1
                || item.CreatedAtUtc == default
                || string.IsNullOrWhiteSpace(item.Text)
                || item.Text.Length
                   > HypershellCloudContract.MaximumConversationTextCharacters
                || !Enum.IsDefined(item.Role)
                || !Enum.IsDefined(item.Kind))
            {
                throw new InvalidDataException(
                    "The local conversation item sequence or content is malformed.");
            }

            previousSequence = item.Sequence;
        }
    }

    private string PathFor(string conversationId)
    {
        var fileIdentity = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(conversationId)))
            .ToLowerInvariant();
        return Path.Combine(
            _directory,
            $"{fileIdentity}.conversation.json");
    }

    private static void ValidateId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new InvalidDataException($"The {label} ID is malformed.");
        }
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> payload)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath)
                     ?? throw new InvalidOperationException(
                         "The conversation path has no parent directory.");
        Directory.CreateDirectory(parent);
        var temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       8192,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Safe(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Take(80)
            .Select(character => char.IsLetterOrDigit(character)
                || character is '-' or '_'
                ? character
                : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "conversation" : safe;
    }
}
