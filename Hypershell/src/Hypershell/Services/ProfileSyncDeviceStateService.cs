using Hypershell.Profiles;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed partial class ProfileSyncDeviceStateService
{
    private const long MaximumStateBytes = 512 * 1024;
    private const int MaximumCheckpoints = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AccountIdPattern();

    private readonly object _gate = new();
    private ProfileSyncDeviceState _state;

    public ProfileSyncDeviceStateService(
        string configurationPath,
        string statePath,
        string conflictDirectory)
    {
        ConfigurationPath = Path.GetFullPath(configurationPath);
        StatePath = Path.GetFullPath(statePath);
        ConflictDirectory = Path.GetFullPath(conflictDirectory);
        _state = LoadState();
    }

    public string ConfigurationPath { get; }
    public string StatePath { get; }
    public string ConflictDirectory { get; }
    public string LastLoadWarning { get; private set; } = string.Empty;

    public ProfileSyncDeviceConfiguration LoadConfiguration()
    {
        lock (_gate)
        {
            if (!File.Exists(ConfigurationPath))
            {
                return new ProfileSyncDeviceConfiguration();
            }

            try
            {
                var payload = ReadBounded(ConfigurationPath);
                var configuration =
                    JsonSerializer.Deserialize<ProfileSyncDeviceConfiguration>(
                        payload,
                        JsonOptions)
                    ?? throw new JsonException(
                        "The device configuration was empty.");
                ValidateConfiguration(configuration);
                return configuration;
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or JsonException
                                             or ProfileSyncException)
            {
                LastLoadWarning =
                    $"The device-local sync configuration could not be loaded: {exception.Message}";
                return new ProfileSyncDeviceConfiguration();
            }
        }
    }

    public void SaveConfiguration(ProfileSyncDeviceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateConfiguration(configuration);
        lock (_gate)
        {
            WriteAtomic(ConfigurationPath, configuration);
        }
    }

    public ProfileSyncCheckpoint? FindCheckpoint(
        string endpoint,
        string profileId)
    {
        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        lock (_gate)
        {
            return _state.Checkpoints.FirstOrDefault(checkpoint =>
                string.Equals(
                    checkpoint.EndpointKey,
                    endpointKey,
                    StringComparison.Ordinal)
                && string.Equals(
                    checkpoint.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SaveCheckpoint(ProfileSyncCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateCheckpoint(checkpoint);
        lock (_gate)
        {
            var checkpoints = _state.Checkpoints
                .Where(existing =>
                    !string.Equals(
                        existing.EndpointKey,
                        checkpoint.EndpointKey,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        existing.ProfileId,
                        checkpoint.ProfileId,
                        StringComparison.OrdinalIgnoreCase))
                .Append(checkpoint)
                .OrderByDescending(existing =>
                    existing.LastPushedAtUtc
                    ?? existing.LastPulledAtUtc
                    ?? DateTimeOffset.MinValue)
                .Take(MaximumCheckpoints)
                .ToList();
            _state = new ProfileSyncDeviceState
            {
                Checkpoints = checkpoints
            };
            WriteAtomic(StatePath, _state);
        }
    }

    public string WriteConflictBundle(
        string profileDisplayName,
        string role,
        ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is <= 0
            || payload.Length > ProfileContract.MaximumBundleBytes)
        {
            throw new InvalidOperationException(
                "A conflict copy must contain one bounded portable profile bundle.");
        }

        var safeName = ToSafeFileName(profileDisplayName, "profile");
        var safeRole = ToSafeFileName(role, "copy");
        Directory.CreateDirectory(ConflictDirectory);
        var fileName =
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeName}-{safeRole}-{Guid.NewGuid():N}";
        if (fileName.Length > 180)
        {
            fileName = fileName[..180];
        }

        var path = Path.Combine(
            ConflictDirectory,
            fileName + ProfileContract.FileExtension);
        WriteAtomicBytes(path, payload.Span);
        return path;
    }

    private ProfileSyncDeviceState LoadState()
    {
        if (!File.Exists(StatePath))
        {
            return new ProfileSyncDeviceState();
        }

        try
        {
            var payload = ReadBounded(StatePath);
            var state = JsonSerializer.Deserialize<ProfileSyncDeviceState>(
                            payload,
                            JsonOptions)
                        ?? throw new JsonException(
                            "The device sync state was empty.");
            if (state.SchemaVersion != ProfileSyncDeviceState.CurrentSchemaVersion
                || state.Checkpoints.Count > MaximumCheckpoints)
            {
                throw new JsonException(
                    "The device sync-state schema or checkpoint count is unsupported.");
            }

            foreach (var checkpoint in state.Checkpoints)
            {
                ValidateCheckpoint(checkpoint);
            }

            return state;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException
                                         or ProfileSyncException)
        {
            LastLoadWarning =
                $"The device-local sync checkpoint file could not be loaded: {exception.Message}";
            return new ProfileSyncDeviceState();
        }
    }

    private byte[] ReadBounded(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumStateBytes)
        {
            throw new JsonException(
                $"Device sync state must be between 1 and {MaximumStateBytes:N0} bytes.");
        }

        return File.ReadAllBytes(path);
    }

    private static void ValidateConfiguration(
        ProfileSyncDeviceConfiguration configuration)
    {
        if (configuration.SchemaVersion
            != ProfileSyncDeviceConfiguration.CurrentSchemaVersion)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                $"Device sync configuration schema {configuration.SchemaVersion} is unsupported.");
        }

        var hasEndpoint = !string.IsNullOrWhiteSpace(
            configuration.ServerEndpoint);
        var hasAccountId = !string.IsNullOrWhiteSpace(
            configuration.AccountId);
        if (hasEndpoint)
        {
            _ = ProfileSyncEndpoint.Normalize(configuration.ServerEndpoint);
        }

        if (hasEndpoint != hasAccountId)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "A profile server endpoint and account ID must be configured together.");
        }

        if (hasAccountId
            && !AccountIdPattern().IsMatch(configuration.AccountId))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The account ID must contain 3-64 letters, numbers, periods, underscores, or hyphens.");
        }
    }

    private static void ValidateCheckpoint(ProfileSyncCheckpoint checkpoint)
    {
        if (string.IsNullOrWhiteSpace(checkpoint.EndpointKey)
            || !Uri.TryCreate(
                checkpoint.EndpointKey,
                UriKind.Absolute,
                out _)
            || string.IsNullOrWhiteSpace(checkpoint.ProfileId)
            || checkpoint.ProfileId.Length > 128
            || checkpoint.ServerRevision < 1
            || checkpoint.ProfileSha256.Length != 64
            || !checkpoint.ProfileSha256.All(Uri.IsHexDigit)
            || !IsStrongETag(checkpoint.ETag))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "A device sync checkpoint was malformed.");
        }
    }

    private static bool IsStrongETag(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
        && value.Length is >= 3 and <= 258
        && value[0] == '"'
        && value[^1] == '"'
        && !value.Any(character => char.IsControl(character));

    private static void WriteAtomic<T>(string path, T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.LongLength > MaximumStateBytes)
        {
            throw new InvalidOperationException(
                "The device sync state exceeds its bounded file size.");
        }

        WriteAtomicBytes(path, payload);
    }

    private static void WriteAtomicBytes(string path, ReadOnlySpan<byte> payload)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException(
                            "The device sync-state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ToSafeFileName(string value, string fallback)
    {
        var safe = new string((value ?? string.Empty)
            .Trim()
            .Select(character =>
                Path.GetInvalidFileNameChars().Contains(character)
                    ? '-'
                    : char.IsLetterOrDigit(character)
                        || character is '-' or '_'
                        ? character
                        : '-')
            .ToArray())
            .Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(safe)
            ? fallback
            : safe.Length <= 80
                ? safe
                : safe[..80];
    }
}
