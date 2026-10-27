using System.Net.NetworkInformation;

namespace Hypershell.Models;

public sealed record NetworkInterfaceSnapshot(
    string Id,
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    OperationalStatus Status,
    long Speed,
    bool IsVirtual,
    string ProfileName,
    string NetworkCategory,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Ipv4Subnets,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    IReadOnlyList<string> DhcpServers)
{
    public string KindText => IsVirtual
        ? $"{InterfaceType} · virtual/VPN"
        : InterfaceType.ToString();
    public string StatusText => Status == OperationalStatus.Up
        ? $"Up · {FormatSpeed(Speed)}"
        : Status.ToString();
    public string ProfileText => string.IsNullOrWhiteSpace(NetworkCategory)
        ? "Profile unavailable"
        : string.IsNullOrWhiteSpace(ProfileName)
            ? NetworkCategory
            : $"{NetworkCategory} · {ProfileName}";
    public string AddressesText => JoinOrDash(Addresses);
    public string SubnetsText => JoinOrDash(Ipv4Subnets);
    public string GatewaysText => JoinOrDash(Gateways);
    public string DnsText => JoinOrDash(DnsServers);
    public string DhcpText => JoinOrDash(DhcpServers);

    private static string FormatSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000d:0.#} Gbps",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000d:0.#} Mbps",
        >= 1_000 => $"{bitsPerSecond / 1_000d:0.#} Kbps",
        > 0 => $"{bitsPerSecond} bps",
        _ => "speed unknown"
    };

    private static string JoinOrDash(IReadOnlyList<string> values) =>
        values.Count == 0 ? "—" : string.Join(" · ", values);
}

public sealed record NetworkRouteSnapshot(
    string DestinationPrefix,
    string NextHop,
    string InterfaceAlias,
    int RouteMetric,
    string Protocol)
{
    public bool IsDefault => DestinationPrefix is "0.0.0.0/0" or "::/0";
    public string Summary =>
        $"{DestinationPrefix} → {NextHop} · {InterfaceAlias} · metric {RouteMetric}";
}

public sealed record NetworkFirewallProfileSnapshot(
    string Name,
    bool Enabled,
    string DefaultInboundAction,
    string DefaultOutboundAction)
{
    public string Summary =>
        $"{Name}: {(Enabled ? "on" : "off")} · inbound {DefaultInboundAction}";
}

public sealed record NetworkFirewallRuleSnapshot(
    string Name,
    string Action,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Protocols,
    IReadOnlyList<string> LocalPorts,
    IReadOnlyList<string> LocalAddresses,
    IReadOnlyList<string> RemoteAddresses,
    IReadOnlyList<string> Programs,
    IReadOnlyList<string> Services);

public sealed record NetworkNeighborSnapshot(
    string IpAddress,
    string MacAddress,
    string State,
    string InterfaceAlias,
    bool IsRouter);

public sealed record NetworkOverviewSnapshot(
    DateTimeOffset CapturedAtUtc,
    string HostName,
    string DomainName,
    IReadOnlyList<NetworkInterfaceSnapshot> Interfaces,
    IReadOnlyList<NetworkRouteSnapshot> Routes,
    IReadOnlyList<NetworkFirewallProfileSnapshot> FirewallProfiles,
    IReadOnlyList<NetworkFirewallRuleSnapshot> FirewallRules,
    IReadOnlyList<NetworkNeighborSnapshot> Neighbors,
    string Diagnostics)
{
    public IReadOnlyList<string> ConnectedIpv4Subnets => Interfaces
        .Where(item => item.Status == OperationalStatus.Up)
        .SelectMany(item => item.Ipv4Subnets)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

public enum NetworkDiscoverySource
{
    NeighborCache,
    Mdns,
    Ssdp,
    WsDiscovery,
    Census
}

public sealed record NetworkDiscoveryObservation(
    NetworkDiscoverySource Source,
    string Address,
    string MacAddress,
    string DisplayName,
    string ServiceType,
    int Port,
    string Detail,
    DateTimeOffset ObservedAtUtc);

public sealed record NearbyNetworkDeviceSnapshot(
    string Key,
    string DisplayName,
    string Role,
    string Confidence,
    string Address,
    string MacAddress,
    string InterfaceAlias,
    IReadOnlyList<string> Services,
    IReadOnlyList<NetworkDiscoverySource> Sources,
    DateTimeOffset LastSeenUtc)
{
    public string ServicesText => Services.Count == 0
        ? "No advertised services"
        : string.Join(" · ", Services);
    public string SourcesText => string.Join(
        " · ",
        Sources.Select(FormatSource));
    public string IdentityText => string.IsNullOrWhiteSpace(MacAddress)
        ? Address
        : $"{Address} · {MacAddress}";
    public string LastSeenText => $"Seen {LastSeenUtc.ToLocalTime():HH:mm:ss}";

    private static string FormatSource(NetworkDiscoverySource source) => source switch
    {
        NetworkDiscoverySource.NeighborCache => "neighbor cache",
        NetworkDiscoverySource.Mdns => "mDNS",
        NetworkDiscoverySource.Ssdp => "SSDP",
        NetworkDiscoverySource.WsDiscovery => "WS-Discovery",
        NetworkDiscoverySource.Census => "census",
        _ => source.ToString()
    };
}

public enum NetworkBindScope
{
    LoopbackOnly,
    LanAddress,
    AllInterfaces,
    VirtualOrVpn,
    Unknown
}

public enum NetworkFirewallSignal
{
    Allowed,
    BlockedOrNoAllowRule,
    FirewallDisabled,
    Unknown
}

public enum NetworkExposureRisk
{
    Local,
    Low,
    Review,
    Elevated
}

public sealed record NetworkCertificateSnapshot(
    string Subject,
    string Issuer,
    string Thumbprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsTrusted,
    string TrustStatus)
{
    public int DaysRemaining => (int)Math.Floor((NotAfter - DateTimeOffset.Now).TotalDays);
    public string Summary =>
        $"TLS · {(IsTrusted ? "trusted" : "untrusted")} · expires {NotAfter:yyyy-MM-dd}";
}

public sealed record NetworkExposureSnapshot(
    string Key,
    NetworkTransport Transport,
    string LocalAddress,
    int LocalPort,
    string ProcessName,
    int ProcessId,
    string ExecutablePath,
    NetworkBindScope BindScope,
    NetworkFirewallSignal FirewallSignal,
    string FirewallScope,
    string ActiveProfiles,
    NetworkExposureRisk Risk,
    NetworkCertificateSnapshot? Certificate,
    string Explanation)
{
    public string Endpoint => NetworkSocketSnapshot.FormatEndpoint(
        LocalAddress,
        LocalPort);
    public string ProtocolText => Transport.ToString().ToUpperInvariant();
    public string ProcessText => $"{ProcessName} · PID {ProcessId}";
    public string Classification => BindScope switch
    {
        NetworkBindScope.LoopbackOnly => "Loopback only",
        NetworkBindScope.VirtualOrVpn => "Virtual/VPN network",
        _ when FirewallSignal == NetworkFirewallSignal.Allowed => "LAN reachable",
        _ when FirewallSignal == NetworkFirewallSignal.BlockedOrNoAllowRule =>
            "Firewall blocked",
        _ when FirewallSignal == NetworkFirewallSignal.FirewallDisabled =>
            "LAN reachable · firewall off",
        _ => "LAN capable · firewall unknown"
    };
    public string FirewallText => FirewallSignal switch
    {
        NetworkFirewallSignal.Allowed => $"Allowed · {FirewallScope}",
        NetworkFirewallSignal.BlockedOrNoAllowRule => "No matching inbound allow",
        NetworkFirewallSignal.FirewallDisabled => "Windows Firewall disabled",
        _ => "Firewall result unavailable"
    };
    public string CertificateText => Certificate?.Summary ?? "No TLS certificate observed";
}

public sealed record NetworkServiceCandidateSnapshot(
    string Name,
    string Purpose,
    string Status,
    string Endpoint,
    string Guidance,
    bool IsRunning);

public sealed record NetworkCensusSubnetChoice(
    string Cidr,
    string InterfaceName,
    string LocalAddress,
    bool WasBounded)
{
    public string DisplayName => WasBounded
        ? $"{Cidr} · {InterfaceName} · bounded range"
        : $"{Cidr} · {InterfaceName}";
}

public sealed record NetworkCensusHostSnapshot(
    string Address,
    string HostName,
    string MacAddress,
    bool RespondedToPing,
    IReadOnlyList<int> OpenPorts,
    DateTimeOffset ObservedAtUtc)
{
    public string DisplayName => string.IsNullOrWhiteSpace(HostName)
        ? Address
        : HostName;
    public string IdentityText => string.IsNullOrWhiteSpace(MacAddress)
        ? Address
        : $"{Address} · {MacAddress}";
    public string ReachabilityText => RespondedToPing
        ? "ICMP response"
        : "Found through selected TCP port";
    public string OpenPortsText => OpenPorts.Count == 0
        ? "No selected TCP ports responded"
        : "Open: " + string.Join(", ", OpenPorts);
}

public enum NetworkChangeSeverity
{
    Info,
    Notice,
    Warning
}

public sealed record NetworkChangeSnapshot(
    string Id,
    DateTimeOffset DetectedAtUtc,
    NetworkChangeSeverity Severity,
    string Category,
    string Summary,
    string Detail)
{
    public string TimestampText => DetectedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string SeverityText => Severity.ToString().ToUpperInvariant();
}

public sealed class NetworkObservatoryStateDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public List<string> DeviceKeys { get; set; } = [];
    public List<string> ListenerKeys { get; set; } = [];
    public Dictionary<string, string> CertificateThumbprints { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string NetworkConfigurationFingerprint { get; set; } = string.Empty;
    public List<NetworkChangeSnapshot> History { get; set; } = [];
}
