using Hypershell.Extensibility;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class ExtensionPanelStateException : Exception
{
    public ExtensionPanelStateException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ExtensionPanelStateStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _extensionId;
    private readonly string _panelId;
    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
        MaxDepth = 32
    };

    public ExtensionPanelStateStore(
        string stateRootDirectory,
        string extensionId,
        string panelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRootDirectory);
        ValidateIdentifier(extensionId, nameof(extensionId));
        ValidateIdentifier(panelId, nameof(panelId));

        _extensionId = extensionId;
        _panelId = panelId;
        var root = Path.GetFullPath(stateRootDirectory);
        var extensionDirectory = Path.GetFullPath(Path.Combine(root, extensionId));
        var rootPrefix = root.TrimEnd(
                             Path.DirectorySeparatorChar,
                             Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        if (!extensionDirectory.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The extension state path must remain inside its configured root.",
                nameof(extensionId));
        }

        _statePath = Path.Combine(extensionDirectory, $"{panelId}.json");
    }

    public string StatePath => _statePath;

    public async Task<JsonElement> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_statePath))
            {
                return CreateEmptyObject();
            }

            if (new FileInfo(_statePath).Length
                > ExtensionPanelMessagingContract.MaximumStateBytes + 4096)
            {
                throw new ExtensionPanelStateException(
                    "STATE_TOO_LARGE",
                    "The saved panel state exceeds its device-local quota.");
            }

            try
            {
                await using var stream = new FileStream(
                    _statePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var state = await JsonSerializer.DeserializeAsync<StoredPanelState>(
                    stream,
                    _jsonOptions,
                    cancellationToken);
                if (state is null
                    || state.SchemaVersion != CurrentSchemaVersion
                    || !string.Equals(
                        state.ExtensionId,
                        _extensionId,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        state.PanelId,
                        _panelId,
                        StringComparison.OrdinalIgnoreCase)
                    || state.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new ExtensionPanelStateException(
                        "STATE_INVALID",
                        "The saved panel state does not match this extension and panel.");
                }

                return state.Value.Clone();
            }
            catch (ExtensionPanelStateException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or NotSupportedException)
            {
                throw new ExtensionPanelStateException(
                    "STATE_UNAVAILABLE",
                    "The device-local panel state could not be read.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> WriteAsync(
        JsonElement value,
        CancellationToken cancellationToken = default)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ExtensionPanelStateException(
                "STATE_INVALID",
                "Panel state must be a JSON object.");
        }

        var valueBytes = Encoding.UTF8.GetByteCount(value.GetRawText());
        if (valueBytes > ExtensionPanelMessagingContract.MaximumStateBytes)
        {
            throw new ExtensionPanelStateException(
                "STATE_TOO_LARGE",
                $"Panel state cannot exceed {ExtensionPanelMessagingContract.MaximumStateBytes / 1024:N0} KiB.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_statePath)
                            ?? throw new ExtensionPanelStateException(
                                "STATE_UNAVAILABLE",
                                "The device-local panel state path is invalid.");
            Directory.CreateDirectory(directory);
            var state = new StoredPanelState
            {
                SchemaVersion = CurrentSchemaVersion,
                ExtensionId = _extensionId,
                PanelId = _panelId,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Value = value.Clone()
            };
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_statePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        state,
                        _jsonOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, _statePath, overwrite: true);
            }
            catch (ExtensionPanelStateException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
            {
                throw new ExtensionPanelStateException(
                    "STATE_UNAVAILABLE",
                    "The device-local panel state could not be saved.");
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            return valueBytes;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 192
            || value is "." or ".."
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "Extension state identifiers may contain only ASCII letters, numbers, dots, underscores, and hyphens.",
                parameterName);
        }
    }

    private sealed class StoredPanelState
    {
        public int SchemaVersion { get; set; }
        public string ExtensionId { get; set; } = string.Empty;
        public string PanelId { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public JsonElement Value { get; set; }
    }
}
