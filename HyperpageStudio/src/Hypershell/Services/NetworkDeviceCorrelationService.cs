using Hypershell.Models;
using System.Net;
using System.Net.Sockets;

namespace Hypershell.Services;

public sealed class NetworkDeviceCorrelationService
{
    internal const int MaximumReverseDnsLookups = 24;
    private static readonly TimeSpan ReverseDnsLookupTimeout =
        TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ReverseDnsPassTimeout =
        TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<NearbyNetworkDeviceSnapshot>> CorrelateAsync(
        NetworkOverviewSnapshot overview,
        IReadOnlyList<NetworkDiscoveryObservation> observations,
        IReadOnlyList<NetworkCensusHostSnapshot>? censusHosts = null,
        CancellationToken cancellationToken = default)
    {
        var gatewayAddresses = overview.Interfaces
            .SelectMany(item => item.Gateways)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builders = new Dictionary<string, DeviceBuilder>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var neighbor in overview.Neighbors)
        {
            var key = CreateDeviceKey(neighbor.IpAddress, neighbor.MacAddress);
            var builder = GetOrCreate(builders, key);
            builder.Address = neighbor.IpAddress;
            builder.MacAddress = neighbor.MacAddress;
            builder.InterfaceAlias = neighbor.InterfaceAlias;
            builder.IsGateway = neighbor.IsRouter
                                || gatewayAddresses.Contains(neighbor.IpAddress);
            builder.Sources.Add(NetworkDiscoverySource.NeighborCache);
            builder.LastSeenUtc = overview.CapturedAtUtc;
        }

        foreach (var observation in observations)
        {
            var matchingNeighbor = overview.Neighbors.FirstOrDefault(item =>
                string.Equals(
                    item.IpAddress,
                    observation.Address,
                    StringComparison.OrdinalIgnoreCase));
            var macAddress = FirstUseful(
                observation.MacAddress,
                matchingNeighbor?.MacAddress);
            var key = CreateDeviceKey(observation.Address, macAddress);
            var builder = GetOrCreate(builders, key);
            builder.Address = FirstUseful(builder.Address, observation.Address);
            builder.MacAddress = FirstUseful(builder.MacAddress, macAddress);
            builder.InterfaceAlias = FirstUseful(
                builder.InterfaceAlias,
                matchingNeighbor?.InterfaceAlias);
            builder.Names.Add(observation.DisplayName);
            builder.Sources.Add(observation.Source);
            builder.LastSeenUtc = builder.LastSeenUtc > observation.ObservedAtUtc
                ? builder.LastSeenUtc
                : observation.ObservedAtUtc;
            AddService(builder.Services, observation.ServiceType, observation.Port);
            if (!string.IsNullOrWhiteSpace(observation.Detail))
            {
                builder.Details.Add(observation.Detail);
            }
        }

        if (censusHosts is not null)
        {
            foreach (var host in censusHosts)
            {
                var matchingNeighbor = overview.Neighbors.FirstOrDefault(item =>
                    string.Equals(
                        item.IpAddress,
                        host.Address,
                        StringComparison.OrdinalIgnoreCase));
                var macAddress = FirstUseful(
                    host.MacAddress,
                    matchingNeighbor?.MacAddress);
                var key = CreateDeviceKey(host.Address, macAddress);
                var builder = GetOrCreate(builders, key);
                builder.Address = host.Address;
                builder.MacAddress = FirstUseful(builder.MacAddress, macAddress);
                builder.InterfaceAlias = FirstUseful(
                    builder.InterfaceAlias,
                    matchingNeighbor?.InterfaceAlias);
                builder.Names.Add(host.HostName);
                builder.Sources.Add(NetworkDiscoverySource.Census);
                builder.LastSeenUtc = host.ObservedAtUtc;
                foreach (var port in host.OpenPorts)
                {
                    AddService(builder.Services, GuessServiceFromPort(port), port);
                }
            }
        }

        await ResolveMissingNamesAsync(builders.Values, cancellationToken);
        return builders.Values
            .Where(item => !string.IsNullOrWhiteSpace(item.Address)
                           || !string.IsNullOrWhiteSpace(item.MacAddress))
            .Select(BuildSnapshot)
            .OrderByDescending(item => item.Role == "Router / gateway")
            .ThenBy(item => IpSortKey(item.Address), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DeviceBuilder GetOrCreate(
        IDictionary<string, DeviceBuilder> builders,
        string key)
    {
        if (builders.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = new DeviceBuilder { Key = key };
        builders.Add(key, created);
        return created;
    }

    private static async Task ResolveMissingNamesAsync(
        IEnumerable<DeviceBuilder> builders,
        CancellationToken cancellationToken)
    {
        using var passCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        passCancellation.CancelAfter(ReverseDnsPassTimeout);
        var passToken = passCancellation.Token;
        using var concurrency = new SemaphoreSlim(6, 6);
        var tasks = builders
            .Where(item => item.Names.All(string.IsNullOrWhiteSpace)
                           && IPAddress.TryParse(item.Address, out _))
            .OrderByDescending(item => item.IsGateway)
            .ThenByDescending(item => item.Sources.Contains(
                NetworkDiscoverySource.NeighborCache))
            .ThenBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumReverseDnsLookups)
            .Select(async item =>
            {
                await concurrency.WaitAsync(passToken);
                try
                {
                    using var timeoutSource =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            passToken);
                    timeoutSource.CancelAfter(ReverseDnsLookupTimeout);
                    try
                    {
                        var entry = await Dns.GetHostEntryAsync(
                                item.Address,
                                timeoutSource.Token)
                            .WaitAsync(timeoutSource.Token);
                        if (!string.IsNullOrWhiteSpace(entry.HostName)
                            && !string.Equals(
                                entry.HostName,
                                item.Address,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            item.Names.Add(entry.HostName);
                        }
                    }
                    catch (SocketException)
                    {
                    }
                    catch (OperationCanceledException) when (
                        !cancellationToken.IsCancellationRequested)
                    {
                    }
                }
                finally
                {
                    concurrency.Release();
                }
            });
        try
        {
            await Task.WhenAll(tasks).WaitAsync(passToken);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            // Reverse DNS is optional enrichment. Keep the bounded set of
            // names already resolved when the per-refresh budget expires.
        }
    }

    private static NearbyNetworkDeviceSnapshot BuildSnapshot(DeviceBuilder builder)
    {
        var name = builder.Names
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => LooksLikeIdentifier(value))
            .ThenBy(value => value.Length)
            .FirstOrDefault();
        var role = InferRole(builder);
        var displayName = !string.IsNullOrWhiteSpace(name)
            ? name
            : builder.IsGateway
                ? "Local gateway"
                : $"Device {builder.Address}";
        var confidence = builder.IsGateway
            ? "Confirmed by active route"
            : builder.Sources.Any(source => source is NetworkDiscoverySource.Mdns
                or NetworkDiscoverySource.Ssdp
                or NetworkDiscoverySource.WsDiscovery)
                ? "Advertised by device"
                : builder.Sources.Contains(NetworkDiscoverySource.Census)
                    ? "Responded to authorized census"
                    : "Observed in neighbor cache";
        return new NearbyNetworkDeviceSnapshot(
            builder.Key,
            displayName,
            role,
            confidence,
            builder.Address,
            builder.MacAddress,
            builder.InterfaceAlias,
            builder.Services
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            builder.Sources.OrderBy(source => source).ToList(),
            builder.LastSeenUtc == default
                ? DateTimeOffset.UtcNow
                : builder.LastSeenUtc);
    }

    private static string InferRole(DeviceBuilder builder)
    {
        if (builder.IsGateway)
        {
            return "Router / gateway";
        }

        var text = string.Join(
            ' ',
            builder.Names.Concat(builder.Services).Concat(builder.Details))
            .ToLowerInvariant();
        if (ContainsAny(text, "_ipp", "printer", "pdl-datastream", "printbasic"))
        {
            return "Printer";
        }

        if (ContainsAny(text, "camera", "_rtsp", "networkvideotransmitter"))
        {
            return "Camera / video device";
        }

        if (ContainsAny(text, "googlecast", "airplay", "_raop", "roku", "smarttv", "mediarenderer"))
        {
            return "Media / casting device";
        }

        if (ContainsAny(text, "home-assistant", "_hap", "_mqtt", "smarthome", "iot"))
        {
            return "Home automation / IoT";
        }

        if (ContainsAny(text, "_smb", "nas", "fileserver"))
        {
            return "File server / NAS";
        }

        if (ContainsAny(text, "_ssh", "_workstation", "rdp", "3389", "computer"))
        {
            return "Computer / server";
        }

        if (ContainsAny(text, "phone", "iphone", "android", "ipad", "tablet"))
        {
            return "Phone / tablet";
        }

        return "Network device";
    }

    private static void AddService(
        ISet<string> services,
        string serviceType,
        int port)
    {
        var service = NormalizeServiceType(serviceType);
        if (string.IsNullOrWhiteSpace(service) && port <= 0)
        {
            return;
        }

        services.Add(port > 0
            ? $"{FirstUseful(service, "service")} :{port}"
            : service);
    }

    private static string NormalizeServiceType(string value)
    {
        var normalized = value.Trim().TrimEnd('.');
        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6];
        }

        return normalized;
    }

    private static string GuessServiceFromPort(int port) => port switch
    {
        22 => "SSH",
        53 => "DNS",
        80 => "HTTP",
        443 => "HTTPS",
        445 => "SMB",
        3389 => "RDP",
        50080 => "Device Portal HTTP",
        50443 => "Device Portal HTTPS",
        _ => "TCP"
    };

    private static string CreateDeviceKey(string address, string macAddress) =>
        !string.IsNullOrWhiteSpace(macAddress)
            ? "mac:" + macAddress
            : !string.IsNullOrWhiteSpace(address)
                ? "ip:" + address
                : "unknown:" + Guid.NewGuid().ToString("N");

    private static bool LooksLikeIdentifier(string value) =>
        value.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)
        || value.Length > 80;

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string FirstUseful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static string IpSortKey(string value) =>
        IPAddress.TryParse(value, out var address)
            ? Convert.ToHexString(address.GetAddressBytes())
            : value;

    private sealed class DeviceBuilder
    {
        public string Key { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string InterfaceAlias { get; set; } = string.Empty;
        public bool IsGateway { get; set; }
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Details { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<NetworkDiscoverySource> Sources { get; } = [];
        public DateTimeOffset LastSeenUtc { get; set; }
    }
}
