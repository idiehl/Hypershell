using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

internal static class NetworkObservatoryVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        VerifyCidrAndPortBoundaries();
        VerifyExposureClassification();
        VerifyDnsResponseParsing();
        VerifyChangeTracking(verifierRoot);
        await VerifyStagedRefreshAndTimeoutAsync(verifierRoot);
        await VerifyLiveWindowsObservationAsync();
        await VerifyLiveRefreshPipelineAsync(verifierRoot);
        await VerifyBoundedCensusAsync();
        Console.WriteLine(
            "NETWORK_OBSERVATORY_OK|cidr|consent-boundary|firewall|dns-sd|changes|windows|refresh-staging|census");
    }

    private static void VerifyCidrAndPortBoundaries()
    {
        Require(
            NetworkCidrUtility.TryParse("192.168.44.19/24", out var cidr)
            && cidr.ToString() == "192.168.44.0/24"
            && NetworkCidrUtility.Contains(
                cidr,
                IPAddress.Parse("192.168.44.200"))
            && !NetworkCidrUtility.Contains(
                cidr,
                IPAddress.Parse("192.168.45.1")),
            "IPv4 census CIDRs must be canonical and bounded.");
        Require(
            NetworkCidrUtility.EnumerateHostAddresses(cidr).Count == 254,
            "A /24 census must exclude the network and broadcast addresses.");
        var bounded = NetworkCidrUtility.CreateCensusChoice(
            IPAddress.Parse("10.40.16.3"),
            16,
            "Verifier");
        Require(
            bounded.Cidr == "10.40.16.0/24" && bounded.WasBounded,
            "Large connected networks must be narrowed to a maximum /24 census range.");
        Require(
            NetworkCensusService.ParsePorts("22, 80 443;50443")
                .SequenceEqual([22, 80, 443, 50443]),
            "Explicit census ports were not parsed correctly.");
        ExpectFailure<FormatException>(
            () => NetworkCensusService.ParsePorts("1-1024"),
            "Census port ranges must remain disabled.");
        ExpectFailure<FormatException>(
            () => NetworkCensusService.ParsePorts(string.Join(
                ',',
                Enumerable.Range(1, NetworkCensusService.MaximumPorts + 1))),
            "The census port-count cap must be enforced.");
    }

    private static void VerifyExposureClassification()
    {
        var physical = CreateInterface(
            "Wi-Fi",
            "10.40.16.3/24",
            isVirtual: false,
            "Public");
        var virtualAdapter = CreateInterface(
            "vEthernet (WSL)",
            "172.30.192.1/20",
            isVirtual: true,
            string.Empty);
        var wildcard = CreateSocket("0.0.0.0", 50443, "System", string.Empty);
        var loopback = CreateSocket("127.0.0.1", 5197, "PingLab", "ping.exe");
        var virtualSocket = CreateSocket(
            "172.30.192.1",
            8080,
            "VirtualApp",
            "virtual.exe");
        Require(
            NetworkExposureAnalysisService.ClassifyBindScope(
                wildcard,
                [physical, virtualAdapter]) == NetworkBindScope.AllInterfaces
            && NetworkExposureAnalysisService.ClassifyBindScope(
                loopback,
                [physical, virtualAdapter]) == NetworkBindScope.LoopbackOnly
            && NetworkExposureAnalysisService.ClassifyBindScope(
                virtualSocket,
                [physical, virtualAdapter]) == NetworkBindScope.VirtualOrVpn,
            "Listener bind scopes were classified incorrectly.");

        var publicProfile = new NetworkFirewallProfileSnapshot(
            "Public",
            true,
            "Block",
            "Allow");
        var privateOnlyRule = new NetworkFirewallRuleSnapshot(
            "Device Portal private",
            "Allow",
            ["Private"],
            ["TCP"],
            ["50443"],
            ["Any"],
            ["LocalSubnet"],
            ["Any"],
            ["Any"]);
        var blocked = NetworkExposureAnalysisService.EvaluateFirewall(
            wildcard,
            ["Public"],
            [publicProfile],
            [privateOnlyRule]);
        Require(
            blocked.Signal == NetworkFirewallSignal.BlockedOrNoAllowRule,
            "A private-only firewall rule must not make a public-profile listener reachable.");

        var publicAllow = privateOnlyRule with
        {
            Name = "Device Portal public test",
            Profiles = ["Public"]
        };
        var allowed = NetworkExposureAnalysisService.EvaluateFirewall(
            wildcard,
            ["Public"],
            [publicProfile],
            [publicAllow]);
        Require(
            allowed.Signal == NetworkFirewallSignal.Allowed
            && allowed.Scope.Contains("local subnet", StringComparison.OrdinalIgnoreCase),
            "A matching scoped inbound allow rule was not recognized.");
        Require(
            NetworkExposureAnalysisService.PortSpecMatches(["50000-50500"], 50443)
            && !NetworkExposureAnalysisService.PortSpecMatches(["50000-50300"], 50443),
            "Firewall port ranges were not matched correctly.");
    }

    private static void VerifyDnsResponseParsing()
    {
        Require(
            NetworkDeviceCorrelationService.MaximumReverseDnsLookups == 24,
            "Reverse DNS enrichment must remain bounded per refresh.");
        using var stream = new MemoryStream();
        stream.Write(new byte[12]);
        WriteDnsName(stream, "_http._tcp.local");
        WriteUInt16(stream, 12);
        WriteUInt16(stream, 1);
        WriteUInt32(stream, 120);
        using var data = new MemoryStream();
        WriteDnsName(data, "Atlas._http._tcp.local");
        var dataBytes = data.ToArray();
        WriteUInt16(stream, (ushort)dataBytes.Length);
        stream.Write(dataBytes);
        var packet = stream.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6, 2), 1);
        var record = LocalNetworkDiscoveryService.ParseDnsPacket(packet).Single();
        Require(
            record.Type == 12
            && record.Name == "_http._tcp.local"
            && record.Value == "Atlas._http._tcp.local",
            "DNS-SD PTR records were not parsed correctly.");
    }

    private static void VerifyChangeTracking(string verifierRoot)
    {
        var statePath = Path.Combine(verifierRoot, "network-observatory.json");
        var store = new NetworkObservatoryStateStore(statePath);
        var overview = CreateOverview("8.8.8.8");
        var firstDevice = CreateDevice("ip:10.0.0.1", "10.0.0.1");
        var baseline = NetworkObservatoryStateStore.Compare(
            null,
            overview,
            [firstDevice],
            []);
        Require(
            baseline.BaselineCreated
            && baseline.NewChanges.Any(item => item.Category == "Baseline"),
            "The first observation must create a baseline without fabricating changes.");
        store.Save(baseline.State);
        var loaded = store.Load();
        Require(
            loaded is not null && loaded.DeviceKeys.Contains(firstDevice.Key),
            "The device-local observatory baseline did not round-trip.");

        var exposure = new NetworkExposureSnapshot(
            "listener",
            NetworkTransport.Tcp,
            "0.0.0.0",
            50443,
            "System",
            4,
            string.Empty,
            NetworkBindScope.AllInterfaces,
            NetworkFirewallSignal.Allowed,
            "local subnet only",
            "Private",
            NetworkExposureRisk.Elevated,
            null,
            "Verifier");
        var changed = NetworkObservatoryStateStore.Compare(
            loaded,
            CreateOverview("1.1.1.1"),
            [firstDevice, CreateDevice("ip:10.0.0.2", "10.0.0.2")],
            [exposure]);
        Require(
            changed.NewChanges.Any(item => item.Summary == "New device observed")
            && changed.NewChanges.Any(item => item.Summary == "New local listener")
            && changed.NewChanges.Any(item => item.Category == "Network configuration"),
            "Device, listener and network-configuration changes must be tracked.");

        var expiringExposure = exposure with
        {
            Certificate = new NetworkCertificateSnapshot(
                "CN=Verifier",
                "CN=Verifier",
                "AABBCCDD",
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(5),
                true,
                "Verifier")
        };
        var expiryFirst = NetworkObservatoryStateStore.Compare(
            changed.State,
            CreateOverview("1.1.1.1"),
            [firstDevice, CreateDevice("ip:10.0.0.2", "10.0.0.2")],
            [expiringExposure]);
        var expirySecond = NetworkObservatoryStateStore.Compare(
            expiryFirst.State,
            CreateOverview("1.1.1.1"),
            [firstDevice, CreateDevice("ip:10.0.0.2", "10.0.0.2")],
            [expiringExposure]);
        Require(
            expiryFirst.NewChanges.Any(item =>
                item.Summary == "Listener certificate expires soon")
            && !expirySecond.NewChanges.Any(item =>
                item.Summary == "Listener certificate expires soon"),
            "Certificate-expiry notices must not repeat on every refresh.");
    }

    private static async Task VerifyLiveWindowsObservationAsync()
    {
        var neighborLayout =
            WindowsNetworkObservationService.GetNeighborInteropLayoutForTesting();
        Require(
            neighborLayout == (88, 28, 32, 40),
            "The MIB_IPNET_ROW2 declaration no longer matches the Windows SDK layout.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var overview = await new WindowsNetworkObservationService().CaptureAsync(
            cancellation.Token);
        Require(
            !string.IsNullOrWhiteSpace(overview.HostName)
            && overview.Interfaces.Count > 0,
            "The Windows network overview must expose a host and at least one interface.");
        Require(
            overview.Interfaces.Any(item => item.Addresses.Count > 0),
            "The Windows network overview exposed no interface addresses.");
        Require(
            overview.Interfaces.Any(item =>
                item.Status == OperationalStatus.Up
                && !item.IsVirtual
                && !string.IsNullOrWhiteSpace(item.ProfileName)
                && item.NetworkCategory is "Public" or "Private" or "Domain"),
            "The native Network List Manager provider exposed no connected profile name/category: "
            + overview.Diagnostics);
        Require(
            overview.Routes.Count > 0
            && overview.FirewallProfiles.Count > 0
            && overview.FirewallRules.Count > 0,
            "Routes or effective firewall metadata were not captured: "
            + $"routes={overview.Routes.Count}, profiles={overview.FirewallProfiles.Count}, "
            + $"rules={overview.FirewallRules.Count}; {overview.Diagnostics}");
        Require(
            overview.Routes.Any(item => item.IsDefault)
            && overview.Routes.All(item => item.DestinationPrefix.Contains('/')),
            "The native route table did not yield a valid default/prefix route.");
        Require(
            overview.FirewallProfiles.Any(item => item.Name == "Public")
            && overview.FirewallRules.Any(item =>
                !string.IsNullOrWhiteSpace(item.Name)
                && item.Action is "Allow" or "Block"
                && item.Protocols.Count > 0),
            "The native firewall provider returned structurally invalid metadata.");
    }

    private static async Task VerifyStagedRefreshAndTimeoutAsync(string verifierRoot)
    {
        var overview = CreateOverview("8.8.8.8") with { Neighbors = [] };
        var discovery = new DeferredDiscoveryService();
        using (var viewModel = CreateRefreshViewModel(
                   verifierRoot,
                   "staged",
                   overview,
                   discovery,
                   TimeSpan.FromSeconds(2)))
        {
            var refresh = viewModel.RefreshAsync(forceDiscovery: true);
            await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Require(
                viewModel.Interfaces.Count == overview.Interfaces.Count
                && viewModel.LastUpdatedText.StartsWith(
                    "Core state updated",
                    StringComparison.Ordinal),
                "Core Windows network data must be visible before LAN discovery completes.");
            discovery.Complete(new LocalNetworkDiscoveryResult([], string.Empty));
            try
            {
                await refresh.WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch (TimeoutException exception)
            {
                throw new InvalidOperationException(
                    $"The staged refresh did not finish. Status: {viewModel.StatusMessage}",
                    exception);
            }
            Require(
                !viewModel.IsRefreshing
                && viewModel.StatusMessage.Contains(
                    "updated in",
                    StringComparison.OrdinalIgnoreCase),
                "A completed staged refresh did not restore the ready state: "
                + $"refreshing={viewModel.IsRefreshing}; {viewModel.StatusMessage}");
        }

        var stalledDiscovery = new DeferredDiscoveryService();
        using var timedViewModel = CreateRefreshViewModel(
            verifierRoot,
            "timeout",
            overview,
            stalledDiscovery,
            TimeSpan.FromMilliseconds(120));
        await timedViewModel.RefreshAsync(forceDiscovery: true)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Require(
            timedViewModel.Interfaces.Count == overview.Interfaces.Count
            && !timedViewModel.IsRefreshing
            && timedViewModel.StatusMessage.Contains(
                "exceeded",
                StringComparison.OrdinalIgnoreCase),
            "A stalled discovery must retain core data and release the refresh state.");
    }

    private static async Task VerifyLiveRefreshPipelineAsync(string verifierRoot)
    {
        using var viewModel = new NetworkObservatoryViewModel(
            new WindowsNetworkObservationService(),
            new LocalNetworkDiscoveryService(),
            new NetworkExposureAnalysisService(),
            new NetworkCensusService(),
            new WindowsSocketInventoryService(),
            new NetworkDeviceCorrelationService(),
            new NetworkObservatoryStateStore(Path.Combine(
                verifierRoot,
                "network-observatory-live-refresh.json")),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(10));
        await viewModel.RefreshAsync(forceDiscovery: true)
            .WaitAsync(TimeSpan.FromSeconds(22));
        Require(
            viewModel.Interfaces.Count > 0
            && !viewModel.IsRefreshing
            && viewModel.LastUpdatedText.StartsWith("Updated", StringComparison.Ordinal)
            && viewModel.StatusMessage.Contains(
                "updated in",
                StringComparison.OrdinalIgnoreCase)
            && viewModel.DataSourceText.Contains(
                "no Atlas server connection required",
                StringComparison.OrdinalIgnoreCase),
            "The live staged refresh pipeline did not complete cleanly: "
            + viewModel.StatusMessage);
    }

    private static NetworkObservatoryViewModel CreateRefreshViewModel(
        string verifierRoot,
        string name,
        NetworkOverviewSnapshot overview,
        ILocalNetworkDiscoveryService discovery,
        TimeSpan timeout) => new(
        new StubObservationService(overview),
        discovery,
        new StubExposureService(),
        new StubCensusService(),
        new StubSocketInventoryService(),
        new NetworkDeviceCorrelationService(),
        new NetworkObservatoryStateStore(Path.Combine(
            verifierRoot,
            $"network-observatory-{name}.json")),
        timeout,
        TimeSpan.FromMilliseconds(10));

    private static async Task VerifyBoundedCensusAsync()
    {
        var localAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == OperationalStatus.Up
                           && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(item => item.GetIPProperties().UnicastAddresses)
            .Select(item => item.Address)
            .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new InvalidOperationException(
                "No active IPv4 address is available for the census verifier.");
        var listener = new TcpListener(localAddress, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var results = await new NetworkCensusService().ScanAsync(
                $"{localAddress}/32",
                [port],
                [],
                cancellationToken: cancellation.Token);
            Require(
                results.Count == 1
                && results[0].Address == localAddress.ToString()
                && results[0].OpenPorts.Contains(port),
                "The bounded census did not find its selected local TCP listener.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static NetworkInterfaceSnapshot CreateInterface(
        string name,
        string address,
        bool isVirtual,
        string category) => new(
        name,
        name,
        name,
        NetworkInterfaceType.Ethernet,
        OperationalStatus.Up,
        1_000_000_000,
        isVirtual,
        "Verifier network",
        category,
        [address],
        [NetworkCidrUtility.FromAddress(
            IPAddress.Parse(address[..address.IndexOf('/')]),
            int.Parse(address[(address.IndexOf('/') + 1)..])).ToString()],
        [],
        [],
        []);

    private static NetworkSocketSnapshot CreateSocket(
        string address,
        int port,
        string process,
        string path) => new(
        $"{address}:{port}",
        NetworkTransport.Tcp,
        IPAddress.Parse(address).AddressFamily,
        "Listen",
        address,
        port,
        "0.0.0.0",
        0,
        100,
        process,
        path,
        "Verifier",
        string.Empty,
        true,
        DateTimeOffset.UtcNow);

    private static NetworkOverviewSnapshot CreateOverview(string dns) => new(
        DateTimeOffset.UtcNow,
        "Verifier",
        string.Empty,
        [new NetworkInterfaceSnapshot(
            "verify",
            "Verifier",
            "Verifier adapter",
            NetworkInterfaceType.Ethernet,
            OperationalStatus.Up,
            1_000_000_000,
            false,
            "Verifier network",
            "Private",
            ["10.0.0.5/24"],
            ["10.0.0.0/24"],
            ["10.0.0.1"],
            [dns],
            ["10.0.0.1"])],
        [new NetworkRouteSnapshot("0.0.0.0/0", "10.0.0.1", "Verifier", 25, "Dhcp")],
        [new NetworkFirewallProfileSnapshot("Private", true, "Block", "Allow")],
        [],
        [new NetworkNeighborSnapshot("10.0.0.1", "00-11-22-33-44-55", "Reachable", "Verifier", true)],
        string.Empty);

    private static NearbyNetworkDeviceSnapshot CreateDevice(
        string key,
        string address) => new(
        key,
        address,
        "Network device",
        "Observed",
        address,
        string.Empty,
        "Verifier",
        [],
        [NetworkDiscoverySource.NeighborCache],
        DateTimeOffset.UtcNow);

    private sealed class StubObservationService(NetworkOverviewSnapshot overview)
        : IWindowsNetworkObservationService
    {
        public Task<NetworkOverviewSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(overview);
    }

    private sealed class DeferredDiscoveryService : ILocalNetworkDiscoveryService
    {
        private readonly TaskCompletionSource<LocalNetworkDiscoveryResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LocalNetworkDiscoveryResult> DiscoverAsync(
            NetworkOverviewSnapshot overview,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            return await _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(LocalNetworkDiscoveryResult result) =>
            _completion.TrySetResult(result);
    }

    private sealed class StubExposureService : INetworkExposureAnalysisService
    {
        public Task<IReadOnlyList<NetworkExposureSnapshot>> AnalyzeAsync(
            NetworkOverviewSnapshot overview,
            IReadOnlyList<NetworkSocketSnapshot> sockets,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NetworkExposureSnapshot>>([]);
    }

    private sealed class StubCensusService : INetworkCensusService
    {
        public Task<IReadOnlyList<NetworkCensusHostSnapshot>> ScanAsync(
            string cidrText,
            IReadOnlyList<int> ports,
            IReadOnlyList<NetworkNeighborSnapshot> knownNeighbors,
            IProgress<NetworkCensusProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NetworkCensusHostSnapshot>>([]);
    }

    private sealed class StubSocketInventoryService : INetworkSocketInventoryService
    {
        public IReadOnlyList<NetworkSocketSnapshot> Capture() => [];

        public bool TryGetProcessIdentity(
            int processId,
            out NetworkProcessIdentity identity)
        {
            identity = default!;
            return false;
        }
    }

    private static void WriteDnsName(Stream stream, string name)
    {
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void ExpectFailure<TException>(
        Action action,
        string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
