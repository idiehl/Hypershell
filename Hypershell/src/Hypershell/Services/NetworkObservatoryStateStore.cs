using Hypershell.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed record NetworkObservatoryComparison(
    NetworkObservatoryStateDocument State,
    IReadOnlyList<NetworkChangeSnapshot> NewChanges,
    bool BaselineCreated);

public sealed class NetworkObservatoryStateStore
{
    private const int MaximumHistoryEntries = 200;
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public NetworkObservatoryStateStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public NetworkObservatoryStateDocument? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<NetworkObservatoryStateDocument>(
                File.ReadAllText(_path),
                _jsonOptions);
            return document?.SchemaVersion
                   == NetworkObservatoryStateDocument.CurrentSchemaVersion
                ? document
                : null;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
        {
            return null;
        }
    }

    public void Save(NetworkObservatoryStateDocument document)
    {
        var directory = Path.GetDirectoryName(_path)
                        ?? throw new InvalidOperationException(
                            "The Network Observatory state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(document, _jsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    public static NetworkObservatoryComparison Compare(
        NetworkObservatoryStateDocument? previous,
        NetworkOverviewSnapshot overview,
        IReadOnlyList<NearbyNetworkDeviceSnapshot> devices,
        IReadOnlyList<NetworkExposureSnapshot> exposures)
    {
        var now = DateTimeOffset.UtcNow;
        var currentDeviceKeys = devices.Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currentListenerKeys = exposures.Select(CreateListenerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var certificates = exposures
            .Where(item => item.Certificate is not null)
            .GroupBy(CreateListenerKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Certificate!.Thumbprint,
                StringComparer.OrdinalIgnoreCase);
        var fingerprint = CreateConfigurationFingerprint(overview);
        var changes = new List<NetworkChangeSnapshot>();
        var baselineCreated = previous is null;
        if (previous is null)
        {
            changes.Add(CreateChange(
                now,
                NetworkChangeSeverity.Info,
                "Baseline",
                "Network baseline created",
                "Future refreshes will compare devices, listeners, gateways, DNS and TLS certificates with this observation."));
        }
        else
        {
            CompareKeys(
                previous.DeviceKeys,
                currentDeviceKeys,
                "Nearby devices",
                "New device observed",
                "Previously observed device not seen",
                changes,
                now,
                NetworkChangeSeverity.Notice);
            CompareKeys(
                previous.ListenerKeys,
                currentListenerKeys,
                "Listeners",
                "New local listener",
                "Local listener stopped",
                changes,
                now,
                NetworkChangeSeverity.Warning);
            if (!string.Equals(
                    previous.NetworkConfigurationFingerprint,
                    fingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(CreateChange(
                    now,
                    NetworkChangeSeverity.Warning,
                    "Network configuration",
                    "Gateway, DNS, interface or default route changed",
                    "Review Network Overview to confirm that the active network configuration is expected."));
            }

            foreach (var pair in certificates)
            {
                if (previous.CertificateThumbprints.TryGetValue(
                        pair.Key,
                        out var previousThumbprint)
                    && !string.Equals(
                        pair.Value,
                        previousThumbprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(CreateChange(
                        now,
                        NetworkChangeSeverity.Warning,
                        "TLS certificate",
                        "Listener certificate changed",
                        pair.Key));
                }
            }
        }

        foreach (var exposure in exposures.Where(item =>
                     item.Certificate is { DaysRemaining: <= 30 }))
        {
            var certificateChange = CreateChange(
                now,
                exposure.Certificate!.DaysRemaining < 0
                    ? NetworkChangeSeverity.Warning
                    : NetworkChangeSeverity.Notice,
                "TLS certificate",
                exposure.Certificate.DaysRemaining < 0
                    ? "Listener certificate expired"
                    : "Listener certificate expires soon",
                $"{exposure.Endpoint} · {exposure.Certificate.Subject} · {exposure.Certificate.NotAfter:yyyy-MM-dd}");
            var alreadyReported = previous?.History.Any(item =>
                string.Equals(
                    item.Category,
                    certificateChange.Category,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Summary,
                    certificateChange.Summary,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Detail,
                    certificateChange.Detail,
                    StringComparison.OrdinalIgnoreCase)) == true;
            if (!alreadyReported)
            {
                changes.Add(certificateChange);
            }
        }

        var history = (previous?.History ?? [])
            .Concat(changes)
            .OrderByDescending(item => item.DetectedAtUtc)
            .Take(MaximumHistoryEntries)
            .ToList();
        var current = new NetworkObservatoryStateDocument
        {
            CapturedAtUtc = now,
            DeviceKeys = currentDeviceKeys,
            ListenerKeys = currentListenerKeys,
            CertificateThumbprints = certificates,
            NetworkConfigurationFingerprint = fingerprint,
            History = history
        };
        return new NetworkObservatoryComparison(current, changes, baselineCreated);
    }

    public static string CreateConfigurationFingerprint(
        NetworkOverviewSnapshot overview)
    {
        var parts = overview.Interfaces
            .Where(item => item.Status
                == System.Net.NetworkInformation.OperationalStatus.Up)
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => string.Join(
                '|',
                item.Id,
                item.NetworkCategory,
                string.Join(',', item.Addresses.OrderBy(value => value)),
                string.Join(',', item.Gateways.OrderBy(value => value)),
                string.Join(',', item.DnsServers.OrderBy(value => value))))
            .Concat(overview.Routes
                .Where(item => item.IsDefault)
                .OrderBy(item => item.Summary, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Summary));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts)));
        return Convert.ToHexString(bytes);
    }

    private static void CompareKeys(
        IReadOnlyList<string> previous,
        IReadOnlyList<string> current,
        string category,
        string addedSummary,
        string removedSummary,
        ICollection<NetworkChangeSnapshot> changes,
        DateTimeOffset timestamp,
        NetworkChangeSeverity addedSeverity)
    {
        var previousSet = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentSet = current.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var added in currentSet.Except(previousSet))
        {
            changes.Add(CreateChange(
                timestamp,
                addedSeverity,
                category,
                addedSummary,
                added));
        }

        foreach (var removed in previousSet.Except(currentSet))
        {
            changes.Add(CreateChange(
                timestamp,
                NetworkChangeSeverity.Info,
                category,
                removedSummary,
                removed));
        }
    }

    private static string CreateListenerKey(NetworkExposureSnapshot item) =>
        $"{item.Transport}|{item.LocalAddress}|{item.LocalPort}|{item.ProcessName}";

    private static NetworkChangeSnapshot CreateChange(
        DateTimeOffset timestamp,
        NetworkChangeSeverity severity,
        string category,
        string summary,
        string detail) => new(
        Guid.NewGuid().ToString("N"),
        timestamp,
        severity,
        category,
        summary,
        detail);
}
