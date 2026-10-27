using Hypershell.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class AtlasMyNetAppStore
{
    public const string PingLabAppId = "atlas.mynet.ping-lab";
    public const string PingLabOfflineRootEnvironmentVariable =
        "PINGLAB_OFFLINE_ROOT";

    private const string PingLabExecutableName =
        "PingLab.OfflineHost.exe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _appsPath;
    private readonly string _trustPath;
    private readonly string? _pingLabOfflineRoot;
    private readonly HashSet<string> _trustedFingerprints =
        new(StringComparer.OrdinalIgnoreCase);

    public AtlasMyNetAppStore(
        string appsPath,
        string trustPath,
        string? pingLabOfflineRoot = null)
    {
        _appsPath = Path.GetFullPath(appsPath);
        _trustPath = Path.GetFullPath(trustPath);
        _pingLabOfflineRoot = string.IsNullOrWhiteSpace(pingLabOfflineRoot)
            ? null
            : pingLabOfflineRoot.Trim();
    }

    public string AppsPath => _appsPath;
    public string TrustPath => _trustPath;

    public IReadOnlyList<AtlasMyNetAppDefinition> Load()
    {
        using var registryLock = AcquireRegistryLock();
        LoadTrust();
        AtlasMyNetAppDocument document;
        try
        {
            document = File.Exists(_appsPath)
                ? JsonSerializer.Deserialize<AtlasMyNetAppDocument>(
                    File.ReadAllText(_appsPath),
                    JsonOptions) ?? new AtlasMyNetAppDocument()
                : new AtlasMyNetAppDocument();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            throw new InvalidDataException(
                $"Atlas MyNet could not read '{_appsPath}': {exception.Message}",
                exception);
        }

        if (document.SchemaVersion != AtlasMyNetAppDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Atlas MyNet app schema {document.SchemaVersion} is not supported.");
        }

        var apps = (document.Apps ?? [])
            .Select(NormalizeAndValidate)
            .ToList();
        var pingLabIndex = apps.FindIndex(app => string.Equals(
            app.Id,
            PingLabAppId,
            StringComparison.OrdinalIgnoreCase));
        var pingLab = CreatePingLabDefinition();
        if (pingLabIndex < 0)
        {
            apps.Insert(0, pingLab);
            Trust(pingLab);
            Save(apps);
        }
        else if (ShouldRefreshPingLabLocation(apps[pingLabIndex], pingLab))
        {
            RevokeTrust(apps[pingLabIndex]);
            apps[pingLabIndex] = pingLab;
            Trust(pingLab);
            Save(apps);
        }

        // Installed first-party payloads use fixed paths; existing user registrations win.
        // Keep the normal command review/trust flow for newly discovered applications.
        var suiteChanged = false;
        var applicationDataRoot = Path.GetDirectoryName(Path.GetDirectoryName(_appsPath)!)!;
        foreach (var installed in InstalledSuiteComponents.Discover(AppContext.BaseDirectory, applicationDataRoot))
        {
            if (apps.Any(app => string.Equals(app.Id, installed.Id, StringComparison.OrdinalIgnoreCase))) continue;
            apps.Add(NormalizeAndValidate(installed));
            suiteChanged = true;
        }
        if (suiteChanged) Save(apps);
        return apps;
    }

    public void Save(IEnumerable<AtlasMyNetAppDefinition> apps)
    {
        using var registryLock = AcquireRegistryLock();
        var normalized = apps.Select(NormalizeAndValidate).ToList();
        var duplicate = normalized
            .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Atlas MyNet app ID '{duplicate.Key}' is duplicated.");
        }

        WriteAtomically(
            _appsPath,
            JsonSerializer.Serialize(
                new AtlasMyNetAppDocument { Apps = normalized },
                JsonOptions));
    }

    public List<AtlasMyNetAppDefinition> UpdateDefinitions(Action<List<AtlasMyNetAppDefinition>> update)
    {
        using var registryLock = AcquireRegistryLock();
        var latest = Load().Select(app => app.Clone()).ToList();
        update(latest);
        Save(latest);
        return latest;
    }

    public IReadOnlyList<AtlasMyNetAppDefinition> ReadImport(string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            var document = JsonSerializer.Deserialize<AtlasMyNetAppDocument>(
                File.ReadAllText(fullPath),
                JsonOptions) ?? throw new InvalidDataException(
                "The selected file does not contain an Atlas MyNet document.");
            if (document.SchemaVersion != AtlasMyNetAppDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Atlas MyNet app schema {document.SchemaVersion} is not supported.");
            }

            return (document.Apps ?? [])
                .Select(NormalizeAndValidate)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            throw new InvalidDataException(
                $"Atlas MyNet could not import '{fullPath}': {exception.Message}",
                exception);
        }
    }

    public void Export(string path, IEnumerable<AtlasMyNetAppDefinition> apps)
    {
        var document = new AtlasMyNetAppDocument
        {
            Apps = apps.Select(NormalizeAndValidate).ToList()
        };
        WriteAtomically(
            Path.GetFullPath(path),
            JsonSerializer.Serialize(document, JsonOptions));
    }

    public bool IsTrusted(AtlasMyNetAppDefinition definition)
    {
        if (!HasCommands(definition))
        {
            return true;
        }

        return _trustedFingerprints.Contains(CreateFingerprint(definition));
    }

    public void Trust(AtlasMyNetAppDefinition definition)
    {
        if (!HasCommands(definition))
        {
            return;
        }

        using var registryLock = AcquireRegistryLock();
        LoadTrust();
        _trustedFingerprints.Add(CreateFingerprint(definition));
        SaveTrust();
    }

    public void RevokeTrust(AtlasMyNetAppDefinition definition)
    {
        using var registryLock = AcquireRegistryLock();
        LoadTrust();
        _trustedFingerprints.Remove(CreateFingerprint(definition));
        SaveTrust();
    }

    public static bool HasCommands(AtlasMyNetAppDefinition definition) =>
        definition.Launch is not null
        || definition.Stop is { Mode: AtlasMyNetStopMode.Command };

    public static AtlasMyNetAppDefinition ValidateDefinition(
        AtlasMyNetAppDefinition definition) =>
        NormalizeAndValidate(definition);

    public static string CreateCommandReviewText(
        AtlasMyNetAppDefinition definition)
    {
        var builder = new StringBuilder();
        if (definition.Launch is { } launch)
        {
            builder.AppendLine("START");
            builder.AppendLine($"Executable: {launch.Executable}");
            builder.AppendLine($"Arguments: {string.Join(" ", launch.Arguments)}");
            builder.AppendLine($"Working directory: {launch.WorkingDirectory}");
            if (launch.Environment.Count > 0)
            {
                builder.AppendLine("Environment:");
                foreach (var pair in launch.Environment.OrderBy(
                             pair => pair.Key,
                             StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"  {pair.Key}={pair.Value}");
                }
            }
        }

        if (definition.Stop is { Mode: AtlasMyNetStopMode.Command } stop)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("STOP");
            builder.AppendLine($"Executable: {stop.Executable}");
            builder.AppendLine($"Arguments: {string.Join(" ", stop.Arguments)}");
            builder.AppendLine($"Working directory: {stop.WorkingDirectory}");
        }

        return builder.ToString().TrimEnd();
    }

    private AtlasMyNetAppDefinition CreatePingLabDefinition()
    {
        var appDirectory = ResolvePingLabAppDirectory();
        return new AtlasMyNetAppDefinition
        {
            Id = PingLabAppId,
            DisplayName = "Ping Lab",
            UrlTemplate = "http://127.0.0.1:{port}/",
            Ports = [.. Enumerable.Range(5197, 10)],
            Launch = new AtlasMyNetLaunchDefinition
            {
                Executable = Path.Combine(
                    appDirectory,
                    PingLabExecutableName),
                Arguments = ["--urls", "http://127.0.0.1:{port}"],
                WorkingDirectory = appDirectory
            },
            HealthCheck = new AtlasMyNetHealthCheckDefinition
            {
                Path = "/__pinglab_offline_status",
                ExpectedStatusCode = 200,
                HeaderName = "X-PingLab-Offline",
                HeaderValue = "true",
                TimeoutSeconds = 15
            },
            Stop = new AtlasMyNetStopDefinition
            {
                Mode = AtlasMyNetStopMode.ManagedProcess
            },
            AllowExternalResources = false
        };
    }

    private string ResolvePingLabAppDirectory()
    {
        var configuredRoot = _pingLabOfflineRoot
                             ?? Environment.GetEnvironmentVariable(
                                 PingLabOfflineRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return NormalizePingLabAppDirectory(configuredRoot);
        }

        string? repositorySibling = null;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            foreach (var ancestor in GetAncestors(seed))
            {
                if (!visited.Add(ancestor))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(Path.Combine(
                    ancestor,
                    "PingLab_Offline",
                    "app"));
                if (File.Exists(Path.Combine(candidate, PingLabExecutableName)))
                {
                    return candidate;
                }

                if (repositorySibling is null
                    && File.Exists(Path.Combine(
                        ancestor,
                        "src",
                        "Hypershell",
                        "Hypershell.csproj")))
                {
                    var parent = Directory.GetParent(ancestor);
                    if (parent is not null)
                    {
                        repositorySibling = Path.GetFullPath(Path.Combine(
                            parent.FullName,
                            "PingLab_Offline",
                            "app"));
                    }
                }
            }
        }

        return repositorySibling
               ?? Path.GetFullPath(Path.Combine(
                   AppContext.BaseDirectory,
                   "apps",
                   "PingLab_Offline",
                   "app"));
    }

    private static string NormalizePingLabAppDirectory(string configuredRoot)
    {
        var fullPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));
        if (string.Equals(
                Path.GetFileName(fullPath),
                PingLabExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(fullPath)
                   ?? throw new InvalidDataException(
                       "The configured Ping Lab executable has no parent directory.");
        }

        return string.Equals(
            Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
            "app",
            StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.GetFullPath(Path.Combine(fullPath, "app"));
    }

    private static IReadOnlyList<string> GetAncestors(string path)
    {
        var ancestors = new List<string>();
        DirectoryInfo? current;
        try
        {
            var fullPath = Path.GetFullPath(path);
            current = new DirectoryInfo(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return ancestors;
        }

        while (current is not null)
        {
            ancestors.Add(current.FullName);
            current = current.Parent;
        }

        return ancestors;
    }

    private static bool ShouldRefreshPingLabLocation(
        AtlasMyNetAppDefinition current,
        AtlasMyNetAppDefinition discovered)
    {
        if (!IsBuiltInPingLabContract(current)
            || current.Launch is null
            || discovered.Launch is null
            || PathsEqual(
                current.Launch.Executable,
                discovered.Launch.Executable))
        {
            return false;
        }

        return !File.Exists(current.Launch.Executable)
               && File.Exists(discovered.Launch.Executable);
    }

    private static bool IsBuiltInPingLabContract(
        AtlasMyNetAppDefinition definition) =>
        string.Equals(
            definition.Id,
            PingLabAppId,
            StringComparison.OrdinalIgnoreCase)
        && definition.Launch is { } launch
        && string.Equals(
            Path.GetFileName(launch.Executable),
            PingLabExecutableName,
            StringComparison.OrdinalIgnoreCase)
        && launch.Arguments.SequenceEqual(
            ["--urls", "http://127.0.0.1:{port}"])
        && definition.Stop is { Mode: AtlasMyNetStopMode.ManagedProcess }
        && definition.HealthCheck is
        {
            Path: "/__pinglab_offline_status",
            HeaderName: "X-PingLab-Offline",
            HeaderValue: "true"
        };

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    private static AtlasMyNetAppDefinition NormalizeAndValidate(
        AtlasMyNetAppDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var app = source.Clone();
        app.Id = (app.Id ?? string.Empty).Trim();
        app.DisplayName = (app.DisplayName ?? string.Empty).Trim();
        app.IconPath = (app.IconPath ?? string.Empty).Trim();
        app.UrlTemplate = (app.UrlTemplate ?? string.Empty).Trim();
        app.Ports = app.Ports.Distinct().Order().ToList();
        if (string.IsNullOrWhiteSpace(app.Id) || string.IsNullOrWhiteSpace(app.DisplayName))
            throw new InvalidDataException("Every MyNet app needs an ID and display name.");

        if (app.IsNative)
        {
            if (app.Launch is null)
                throw new InvalidDataException($"Native app '{app.DisplayName}' needs a start command.");
            if (app.Ports.Count != 0 || app.HealthCheck is not null)
                throw new InvalidDataException($"Native app '{app.DisplayName}' cannot define ports or an HTTP health check.");
            if (app.UrlTemplate.Length != 0 && app.UrlTemplate != "http://127.0.0.1:{port}/")
                throw new InvalidDataException($"Native app '{app.DisplayName}' cannot define a web URL.");
            app.UrlTemplate = string.Empty;
        }
        else
        {
            if (app.Ports.Any(port => port is < 1 or > 65535))
            {
                throw new InvalidDataException(
                    $"App '{app.DisplayName}' contains a port outside 1 through 65535.");
            }

            if (app.UrlTemplate.Contains("{port}", StringComparison.OrdinalIgnoreCase)
                && app.Ports.Count == 0)
            {
                throw new InvalidDataException(
                    $"App '{app.DisplayName}' uses {{port}} but defines no ports.");
            }

            var validationUrl = app.UrlTemplate.Replace(
                "{port}",
                (app.Ports.FirstOrDefault() is var port && port > 0 ? port : 80)
                .ToString(),
                StringComparison.OrdinalIgnoreCase);
            if (!Uri.TryCreate(validationUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException(
                    $"App '{app.DisplayName}' must use an absolute HTTP or HTTPS URL template.");
            }

            if (!NetworkAddressPolicy.IsLocalMachineUri(uri))
            {
                throw new InvalidDataException(
                    $"App '{app.DisplayName}' must target this Windows device. Public and remote URLs cannot be registered as local apps.");
            }

        }

        NormalizeLaunch(app.Launch, app.DisplayName, "start");
        if (app.Stop is { Mode: AtlasMyNetStopMode.Command } stop)
        {
            if (string.IsNullOrWhiteSpace(stop.Executable))
            {
                throw new InvalidDataException(
                    $"App '{app.DisplayName}' has a command stop mode without an executable.");
            }

            stop.Executable = stop.Executable.Trim();
            stop.WorkingDirectory = (stop.WorkingDirectory ?? string.Empty).Trim();
            stop.Arguments = (stop.Arguments ?? [])
                .Select(argument => argument ?? string.Empty)
                .ToList();
            ValidateCommandPaths(
                stop.Executable,
                stop.WorkingDirectory,
                app.DisplayName,
                "stop");
        }

        if (app.HealthCheck is { } health)
        {
            health.Path = string.IsNullOrWhiteSpace(health.Path)
                ? "/"
                : health.Path.Trim();
            health.HeaderName = (health.HeaderName ?? string.Empty).Trim();
            health.HeaderValue = (health.HeaderValue ?? string.Empty).Trim();
            health.TimeoutSeconds = Math.Clamp(health.TimeoutSeconds, 1, 120);
            if (health.ExpectedStatusCode is < 100 or > 599)
            {
                throw new InvalidDataException(
                    $"App '{app.DisplayName}' has an invalid health status code.");
            }
        }

        return app;
    }

    private static void NormalizeLaunch(
        AtlasMyNetLaunchDefinition? launch,
        string displayName,
        string operation)
    {
        if (launch is null)
        {
            return;
        }

        launch.Executable = (launch.Executable ?? string.Empty).Trim();
        launch.WorkingDirectory = (launch.WorkingDirectory ?? string.Empty).Trim();
        launch.Arguments = (launch.Arguments ?? [])
            .Select(argument => argument ?? string.Empty)
            .ToList();
        if (string.IsNullOrWhiteSpace(launch.Executable))
        {
            throw new InvalidDataException(
                $"App '{displayName}' has a {operation} definition without an executable.");
        }

        ValidateCommandPaths(
            launch.Executable,
            launch.WorkingDirectory,
            displayName,
            operation);
        var normalizedEnvironment = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in launch.Environment ?? [])
        {
            var name = (pair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains('=')
                || name.Contains('\0'))
            {
                throw new InvalidDataException(
                    $"App '{displayName}' has an invalid environment variable name.");
            }

            normalizedEnvironment[name] = pair.Value ?? string.Empty;
        }

        launch.Environment = normalizedEnvironment;
    }

    private static void ValidateCommandPaths(
        string executable,
        string workingDirectory,
        string displayName,
        string operation)
    {
        if (!Path.IsPathFullyQualified(executable))
        {
            throw new InvalidDataException(
                $"App '{displayName}' must use an absolute {operation} executable path.");
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && !Path.IsPathFullyQualified(workingDirectory))
        {
            throw new InvalidDataException(
                $"App '{displayName}' must use an absolute {operation} working directory.");
        }
    }

    private static string CreateFingerprint(AtlasMyNetAppDefinition definition)
    {
        // Preserve existing web-app approvals; app kind is part of native command trust.
        object commandIdentity = definition.IsNative
            ? new { definition.Id, definition.Launch, definition.Stop, IsNative = true }
            : (object)new { definition.Id, definition.Launch, definition.Stop };
        var payload = JsonSerializer.Serialize(commandIdentity, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private void LoadTrust()
    {
        _trustedFingerprints.Clear();
        try
        {
            if (!File.Exists(_trustPath))
            {
                return;
            }

            var document = JsonSerializer.Deserialize<AtlasMyNetTrustDocument>(
                File.ReadAllText(_trustPath),
                JsonOptions);
            if (document?.Fingerprints is null)
            {
                return;
            }

            if (document.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    $"Atlas MyNet trust schema {document.SchemaVersion} is not supported.");
            }

            foreach (var fingerprint in document.Fingerprints
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                _trustedFingerprints.Add(fingerprint.Trim());
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            throw new InvalidDataException(
                $"Atlas MyNet could not read its command trust file: {exception.Message}",
                exception);
        }
    }

    private void SaveTrust() =>
        WriteAtomically(
            _trustPath,
            JsonSerializer.Serialize(
                new AtlasMyNetTrustDocument
                {
                    Fingerprints = _trustedFingerprints
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                },
                JsonOptions));

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidDataException(
                            $"'{path}' does not have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private IDisposable AcquireRegistryLock()
    {
        var name = "Local\\Hypershell.MyNet.Registry." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(_appsPath.ToUpperInvariant())));
        var mutex = new Mutex(false, name);
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    throw new IOException("Another MyNet window is saving the app library. Try again.");
                }
            }
            catch (AbandonedMutexException)
            {
                // Windows grants ownership after a terminated writer; atomic files remain valid.
            }

            return new RegistryLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class RegistryLock(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    private sealed class AtlasMyNetTrustDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> Fingerprints { get; set; } = [];
    }
}
