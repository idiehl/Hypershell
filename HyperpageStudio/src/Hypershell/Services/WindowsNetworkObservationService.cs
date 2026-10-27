using Hypershell.Models;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Hypershell.Services;

public interface IWindowsNetworkObservationService
{
    Task<NetworkOverviewSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads Windows network state without invoking PowerShell. This deliberately
/// uses read-only operating-system APIs so the observatory works when script
/// execution is disabled and never changes firewall or adapter configuration.
/// </summary>
public sealed class WindowsNetworkObservationService
    : IWindowsNetworkObservationService
{
    private const ushort AddressFamilyUnspecified = 0;
    private const ushort AddressFamilyIpv4 = 2;
    private const ushort AddressFamilyIpv6 = 23;
    private static readonly Guid NetworkConnectionInterfaceId = new(
        "DCB00005-570F-4A9B-8D69-199FDBA5723B");

    public Task<NetworkOverviewSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) => Task.Run(
        () => Capture(cancellationToken),
        cancellationToken);

    private static NetworkOverviewSnapshot Capture(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<string>();
        var connectionProfiles = CaptureConnectionProfiles(
            cancellationToken,
            diagnostics);
        var firewall = CaptureFirewall(cancellationToken, diagnostics);
        var interfaces = CaptureInterfaces(
            firewall.ActiveProfileNames,
            connectionProfiles);
        var interfaceAliases = CreateInterfaceAliasMap();
        var routes = CaptureRoutes(
            interfaceAliases,
            cancellationToken,
            diagnostics);
        var gateways = interfaces
            .SelectMany(item => item.Gateways)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var neighbors = CaptureNeighbors(
            interfaceAliases,
            gateways,
            cancellationToken,
            diagnostics);

        return new NetworkOverviewSnapshot(
            DateTimeOffset.UtcNow,
            Dns.GetHostName(),
            IPGlobalProperties.GetIPGlobalProperties().DomainName,
            interfaces,
            routes,
            firewall.Profiles,
            firewall.Rules,
            neighbors,
            diagnostics.Count == 0
                ? string.Empty
                : string.Join(" · ", diagnostics));
    }

    private static IReadOnlyList<NetworkInterfaceSnapshot> CaptureInterfaces(
        IReadOnlyList<string> activeProfileNames,
        IReadOnlyDictionary<string, ConnectionProfile> connectionProfiles)
    {
        var activeCategories = string.Join(", ", activeProfileNames);
        var result = new List<NetworkInterfaceSnapshot>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(adapter => adapter.NetworkInterfaceType
                         != NetworkInterfaceType.Loopback))
        {
            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var addresses = properties.UnicastAddresses
                .Where(item => !IPAddress.IsLoopback(item.Address))
                .Select(item => $"{item.Address}/{item.PrefixLength}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (adapter.OperationalStatus != OperationalStatus.Up
                && addresses.Count == 0)
            {
                continue;
            }

            var isVirtual = IsVirtual(adapter);
            connectionProfiles.TryGetValue(
                NormalizeAdapterId(adapter.Id),
                out var connectionProfile);
            var ipv4Subnets = properties.UnicastAddresses
                .Where(item => item.Address.AddressFamily
                                   == AddressFamily.InterNetwork
                               && !IPAddress.IsLoopback(item.Address))
                .Select(item => NetworkCidrUtility
                    .FromAddress(item.Address, item.PrefixLength)
                    .ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Add(new NetworkInterfaceSnapshot(
                adapter.Id,
                adapter.Name,
                adapter.Description,
                adapter.NetworkInterfaceType,
                adapter.OperationalStatus,
                adapter.Speed,
                isVirtual,
                connectionProfile?.Name ?? string.Empty,
                connectionProfile?.Category
                ?? (adapter.OperationalStatus == OperationalStatus.Up && !isVirtual
                    ? activeCategories
                    : string.Empty),
                addresses,
                ipv4Subnets,
                properties.GatewayAddresses
                    .Select(item => item.Address.ToString())
                    .Where(value => !IsUnspecified(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                properties.DnsAddresses
                    .Select(item => item.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                properties.DhcpServerAddresses
                    .Select(item => item.ToString())
                    .Where(value => !IsUnspecified(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()));
        }

        return result
            .OrderByDescending(item => item.Status == OperationalStatus.Up)
            .ThenBy(item => item.IsVirtual)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<uint, string> CreateInterfaceAliasMap()
    {
        var result = new Dictionary<uint, string>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var ipv4Index = properties.GetIPv4Properties()?.Index;
                var ipv6Index = properties.GetIPv6Properties()?.Index;
                if (ipv4Index is > 0)
                {
                    result[(uint)ipv4Index.Value] = adapter.Name;
                }

                if (ipv6Index is > 0)
                {
                    result[(uint)ipv6Index.Value] = adapter.Name;
                }
            }
            catch (NetworkInformationException)
            {
                // A transient adapter can disappear between enumeration calls.
            }
        }

        return result;
    }

    private static IReadOnlyList<NetworkRouteSnapshot> CaptureRoutes(
        IReadOnlyDictionary<uint, string> interfaceAliases,
        CancellationToken cancellationToken,
        ICollection<string> diagnostics)
    {
        IntPtr table = IntPtr.Zero;
        try
        {
            var error = GetIpForwardTable2(AddressFamilyUnspecified, out table);
            if (error != 0 || table == IntPtr.Zero)
            {
                diagnostics.Add($"Route table unavailable (Win32 {error})");
                return [];
            }

            var count = unchecked((uint)Marshal.ReadInt32(table));
            if (count > 1_000_000)
            {
                diagnostics.Add("Route table returned an invalid row count");
                return [];
            }

            var rows = new List<NetworkRouteSnapshot>((int)count);
            var rowSize = Marshal.SizeOf<MibIpForwardRow2>();
            var firstRowOffset = IntPtr.Size == 8 ? 8 : 4;
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pointer = IntPtr.Add(
                    table,
                    firstRowOffset + checked((int)index * rowSize));
                var row = Marshal.PtrToStructure<MibIpForwardRow2>(pointer);
                var destination = ToIpAddress(row.DestinationPrefix.Prefix);
                if (destination is null)
                {
                    continue;
                }

                var nextHop = ToIpAddress(row.NextHop)?.ToString()
                              ?? string.Empty;
                rows.Add(new NetworkRouteSnapshot(
                    $"{destination}/{row.DestinationPrefix.PrefixLength}",
                    nextHop,
                    interfaceAliases.GetValueOrDefault(
                        row.InterfaceIndex,
                        $"Interface {row.InterfaceIndex}"),
                    unchecked((int)row.Metric),
                    FormatRouteProtocol(row.Protocol)));
            }

            return rows
                .GroupBy(
                    item => $"{item.DestinationPrefix}|{item.NextHop}|{item.InterfaceAlias}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.RouteMetric).First())
                .OrderByDescending(item => item.IsDefault)
                .ThenBy(item => item.InterfaceAlias, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RouteMetric)
                .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Add($"Route table unavailable: {exception.Message}");
            return [];
        }
        finally
        {
            if (table != IntPtr.Zero)
            {
                FreeMibTable(table);
            }
        }
    }

    private static IReadOnlyList<NetworkNeighborSnapshot> CaptureNeighbors(
        IReadOnlyDictionary<uint, string> interfaceAliases,
        ISet<string> gateways,
        CancellationToken cancellationToken,
        ICollection<string> diagnostics)
    {
        IntPtr table = IntPtr.Zero;
        try
        {
            var error = GetIpNetTable2(AddressFamilyUnspecified, out table);
            if (error != 0 || table == IntPtr.Zero)
            {
                diagnostics.Add($"Neighbor cache unavailable (Win32 {error})");
                return [];
            }

            var count = unchecked((uint)Marshal.ReadInt32(table));
            if (count > 1_000_000)
            {
                diagnostics.Add("Neighbor cache returned an invalid row count");
                return [];
            }

            var rows = new List<NetworkNeighborSnapshot>((int)count);
            var rowSize = Marshal.SizeOf<MibIpNetRow2>();
            var firstRowOffset = IntPtr.Size == 8 ? 8 : 4;
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pointer = IntPtr.Add(
                    table,
                    firstRowOffset + checked((int)index * rowSize));
                var row = Marshal.PtrToStructure<MibIpNetRow2>(pointer);
                var address = ToIpAddress(row.Address);
                if (address is null
                    || address.Equals(IPAddress.Any)
                    || address.Equals(IPAddress.IPv6Any)
                    || IPAddress.IsLoopback(address)
                    || IsMulticast(address))
                {
                    continue;
                }

                var macAddress = FormatPhysicalAddress(row);
                var state = FormatNeighborState(row.State);
                if (row.State == 0 && string.IsNullOrWhiteSpace(macAddress))
                {
                    continue;
                }

                var ipAddress = address.ToString();
                rows.Add(new NetworkNeighborSnapshot(
                    ipAddress,
                    macAddress,
                    state,
                    interfaceAliases.GetValueOrDefault(
                        row.InterfaceIndex,
                        $"Interface {row.InterfaceIndex}"),
                    gateways.Contains(ipAddress)));
            }

            return rows
                .GroupBy(
                    item => $"{item.IpAddress}|{item.InterfaceAlias}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => ParseIpSortKey(item.IpAddress))
                .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Add($"Neighbor cache unavailable: {exception.Message}");
            return [];
        }
        finally
        {
            if (table != IntPtr.Zero)
            {
                FreeMibTable(table);
            }
        }
    }

    private static IReadOnlyDictionary<string, ConnectionProfile>
        CaptureConnectionProfiles(
            CancellationToken cancellationToken,
            ICollection<string> diagnostics)
    {
        object? manager = null;
        object? connections = null;
        try
        {
            var managerType = Type.GetTypeFromProgID("NetworkListManager")
                              ?? Type.GetTypeFromCLSID(new Guid(
                                  "DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (managerType is null)
            {
                diagnostics.Add("Windows Network List Manager API is unavailable");
                return new Dictionary<string, ConnectionProfile>(
                    StringComparer.OrdinalIgnoreCase);
            }

            manager = Activator.CreateInstance(managerType);
            if (manager is null)
            {
                diagnostics.Add("Windows Network List Manager could not be opened");
                return new Dictionary<string, ConnectionProfile>(
                    StringComparer.OrdinalIgnoreCase);
            }

            if (manager is not INetworkListManagerCom managerView)
            {
                diagnostics.Add("Windows Network List Manager interface is unavailable");
                return new Dictionary<string, ConnectionProfile>(
                    StringComparer.OrdinalIgnoreCase);
            }

            connections = managerView.GetNetworkConnections();
            if (connections is not IEnumerable enumerable)
            {
                diagnostics.Add("Windows network connections could not be enumerated");
                return new Dictionary<string, ConnectionProfile>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, ConnectionProfile>(
                StringComparer.OrdinalIgnoreCase);
            string? firstConnectionError = null;
            foreach (var connection in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var connectionPointer = IntPtr.Zero;
                var networkPointer = IntPtr.Zero;
                var connectionStage = "adapter ID";
                try
                {
                    connectionPointer = QueryComInterface(
                        connection,
                        NetworkConnectionInterfaceId);
                    var adapterGuid = InvokeComGuidMethod(
                        connectionPointer,
                        methodSlot: 12);
                    var adapterId = adapterGuid.ToString("D");
                    connectionStage = "network";
                    networkPointer = InvokeComInterfaceMethod(
                        connectionPointer,
                        methodSlot: 7);
                    if (string.IsNullOrWhiteSpace(adapterId)
                        || networkPointer == IntPtr.Zero)
                    {
                        continue;
                    }

                    connectionStage = "network name";
                    var profileName = InvokeComStringMethod(
                        networkPointer,
                        methodSlot: 7);
                    connectionStage = "network category";
                    var category = InvokeComInt32Method(
                        networkPointer,
                        methodSlot: 18) switch
                    {
                        0 => "Public",
                        1 => "Private",
                        2 => "Domain",
                        _ => string.Empty
                    };
                    result[NormalizeAdapterId(adapterId)] = new ConnectionProfile(
                        profileName,
                        category);
                }
                catch (Exception exception) when (exception is COMException
                                                   or TargetInvocationException
                                                   or InvalidCastException
                                                   or MissingMethodException)
                {
                    // A stale connection can disappear while NLM is enumerated.
                    firstConnectionError ??=
                        $"{connectionStage}: "
                        + (exception.InnerException?.Message ?? exception.Message);
                }
                finally
                {
                    if (networkPointer != IntPtr.Zero)
                    {
                        Marshal.Release(networkPointer);
                    }

                    if (connectionPointer != IntPtr.Zero)
                    {
                        Marshal.Release(connectionPointer);
                    }

                    ReleaseComObject(connection);
                }
            }

            if (result.Count == 0)
            {
                diagnostics.Add(firstConnectionError is null
                    ? "Network List Manager reported no connected profiles"
                    : $"Network profile details unavailable: {firstConnectionError}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is COMException
                                           or TargetInvocationException
                                           or InvalidCastException
                                           or InvalidOperationException
                                           or MissingMethodException)
        {
            diagnostics.Add(
                $"Network profile names unavailable: {exception.Message}");
            return new Dictionary<string, ConnectionProfile>(
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            ReleaseComObject(connections);
            ReleaseComObject(manager);
        }
    }

    private static FirewallCapture CaptureFirewall(
        CancellationToken cancellationToken,
        ICollection<string> diagnostics)
    {
        object? policy = null;
        object? rulesCollection = null;
        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is null)
            {
                diagnostics.Add("Windows Firewall policy API is unavailable");
                return FirewallCapture.Empty;
            }

            policy = Activator.CreateInstance(policyType);
            if (policy is null)
            {
                diagnostics.Add("Windows Firewall policy could not be opened");
                return FirewallCapture.Empty;
            }

            var activeProfileMask = ReadComInt32(
                policy,
                "CurrentProfileTypes");
            var activeProfileNames = FormatFirewallProfiles(
                activeProfileMask,
                useAnyForAll: false);
            var profiles = new List<NetworkFirewallProfileSnapshot>();
            foreach (var profile in FirewallProfileDefinitions)
            {
                profiles.Add(new NetworkFirewallProfileSnapshot(
                    profile.Name,
                    ReadComBoolean(policy, "FirewallEnabled", profile.Value),
                    FormatFirewallAction(ReadComInt32(
                        policy,
                        "DefaultInboundAction",
                        profile.Value)),
                    FormatFirewallAction(ReadComInt32(
                        policy,
                        "DefaultOutboundAction",
                        profile.Value))));
            }

            rulesCollection = ReadComValue(policy, "Rules");
            if (rulesCollection is not IEnumerable enumerable)
            {
                diagnostics.Add("Windows Firewall rules could not be enumerated");
                return new FirewallCapture(
                    activeProfileNames,
                    profiles,
                    []);
            }

            var rules = new List<NetworkFirewallRuleSnapshot>();
            var unreadableRules = 0;
            foreach (var rule in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!ReadComBoolean(rule, "Enabled")
                        || ReadComInt32(rule, "Direction") != 1)
                    {
                        continue;
                    }

                    var protocol = ReadComInt32(rule, "Protocol");
                    rules.Add(new NetworkFirewallRuleSnapshot(
                        ReadComString(rule, "Name"),
                        FormatFirewallAction(ReadComInt32(rule, "Action")),
                        FormatFirewallProfiles(
                            ReadComInt32(rule, "Profiles"),
                            useAnyForAll: true),
                        [FormatFirewallProtocol(protocol)],
                        SplitComList(ReadComString(rule, "LocalPorts")),
                        SplitComList(ReadComString(rule, "LocalAddresses")),
                        SplitComList(ReadComString(rule, "RemoteAddresses")),
                        SingleComValue(ReadComString(rule, "ApplicationName")),
                        SingleComValue(ReadComString(rule, "ServiceName"))));
                }
                catch (Exception exception) when (exception is COMException
                                                   or TargetInvocationException
                                                   or InvalidCastException
                                                   or FormatException)
                {
                    unreadableRules++;
                }
                finally
                {
                    ReleaseComObject(rule);
                }
            }

            if (unreadableRules > 0)
            {
                diagnostics.Add(
                    $"{unreadableRules} firewall rules had unreadable properties");
            }

            return new FirewallCapture(
                activeProfileNames,
                profiles.OrderBy(item => item.Name).ToList(),
                rules
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is COMException
                                           or TargetInvocationException
                                           or InvalidCastException
                                           or InvalidOperationException
                                           or UnauthorizedAccessException)
        {
            diagnostics.Add($"Windows Firewall metadata unavailable: {exception.Message}");
            return FirewallCapture.Empty;
        }
        finally
        {
            ReleaseComObject(rulesCollection);
            ReleaseComObject(policy);
        }
    }

    private static object? ReadComValue(
        object target,
        string propertyName,
        params object[] indexArguments) => target.GetType().InvokeMember(
        propertyName,
        BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
        binder: null,
        target,
        indexArguments.Length == 0 ? null : indexArguments);

    private static IntPtr QueryComInterface(object value, Guid interfaceId)
    {
        var unknown = Marshal.GetIUnknownForObject(value);
        try
        {
            var result = Marshal.QueryInterface(
                unknown,
                in interfaceId,
                out var interfacePointer);
            Marshal.ThrowExceptionForHR(result);
            return interfacePointer;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private static Guid InvokeComGuidMethod(
        IntPtr interfacePointer,
        int methodSlot)
    {
        var method = GetComMethod<GetGuidMethod>(
            interfacePointer,
            methodSlot);
        var result = method(interfacePointer, out var value);
        Marshal.ThrowExceptionForHR(result);
        return value;
    }

    private static IntPtr InvokeComInterfaceMethod(
        IntPtr interfacePointer,
        int methodSlot)
    {
        var method = GetComMethod<GetInterfaceMethod>(
            interfacePointer,
            methodSlot);
        var result = method(interfacePointer, out var value);
        Marshal.ThrowExceptionForHR(result);
        return value;
    }

    private static string InvokeComStringMethod(
        IntPtr interfacePointer,
        int methodSlot)
    {
        var method = GetComMethod<GetStringMethod>(
            interfacePointer,
            methodSlot);
        var result = method(interfacePointer, out var value);
        Marshal.ThrowExceptionForHR(result);
        if (value == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringBSTR(value);
        }
        finally
        {
            Marshal.FreeBSTR(value);
        }
    }

    private static int InvokeComInt32Method(
        IntPtr interfacePointer,
        int methodSlot)
    {
        var method = GetComMethod<GetInt32Method>(
            interfacePointer,
            methodSlot);
        var result = method(interfacePointer, out var value);
        Marshal.ThrowExceptionForHR(result);
        return value;
    }

    private static TDelegate GetComMethod<TDelegate>(
        IntPtr interfacePointer,
        int methodSlot)
        where TDelegate : Delegate
    {
        var virtualTable = Marshal.ReadIntPtr(interfacePointer);
        var methodPointer = Marshal.ReadIntPtr(
            virtualTable,
            checked(methodSlot * IntPtr.Size));
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(methodPointer);
    }

    private static string ReadComString(
        object target,
        string propertyName) => Convert.ToString(
        ReadComValue(target, propertyName)) ?? string.Empty;

    private static int ReadComInt32(
        object target,
        string propertyName,
        params object[] indexArguments) => Convert.ToInt32(
        ReadComValue(target, propertyName, indexArguments));

    private static bool ReadComBoolean(
        object target,
        string propertyName,
        params object[] indexArguments) => Convert.ToBoolean(
        ReadComValue(target, propertyName, indexArguments));

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private static IReadOnlyList<string> FormatFirewallProfiles(
        int profileMask,
        bool useAnyForAll)
    {
        const int allProfiles = int.MaxValue;
        if (useAnyForAll && profileMask == allProfiles)
        {
            return ["Any"];
        }

        var result = FirewallProfileDefinitions
            .Where(item => (profileMask & item.Value) != 0)
            .Select(item => item.Name)
            .ToList();
        return result.Count == 0 ? ["Any"] : result;
    }

    private static string FormatFirewallAction(int action) => action == 0
        ? "Block"
        : "Allow";

    private static string FormatFirewallProtocol(int protocol) => protocol switch
    {
        6 => "TCP",
        17 => "UDP",
        256 => "Any",
        _ => protocol.ToString()
    };

    private static IReadOnlyList<string> SplitComList(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();

    private static IReadOnlyList<string> SingleComValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    private static readonly (int Value, string Name)[] FirewallProfileDefinitions =
    [
        (1, "Domain"),
        (2, "Private"),
        (4, "Public")
    ];

    private sealed record ConnectionProfile(string Name, string Category);

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface INetworkListManagerCom
    {
        [DispId(3)]
        [return: MarshalAs(UnmanagedType.Interface)]
        object GetNetworkConnections();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetGuidMethod(IntPtr instance, out Guid value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInterfaceMethod(IntPtr instance, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetStringMethod(IntPtr instance, out IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInt32Method(IntPtr instance, out int value);


    private sealed record FirewallCapture(
        IReadOnlyList<string> ActiveProfileNames,
        IReadOnlyList<NetworkFirewallProfileSnapshot> Profiles,
        IReadOnlyList<NetworkFirewallRuleSnapshot> Rules)
    {
        public static FirewallCapture Empty { get; } = new([], [], []);
    }

    private static IPAddress? ToIpAddress(SockaddrInet address)
    {
        if (address.Family == AddressFamilyIpv4)
        {
            return new IPAddress(BitConverter.GetBytes(address.Ipv4Address));
        }

        if (address.Family != AddressFamilyIpv6)
        {
            return null;
        }

        var bytes = new byte[16];
        BitConverter.GetBytes(address.Ipv6Part1).CopyTo(bytes, 0);
        BitConverter.GetBytes(address.Ipv6Part2).CopyTo(bytes, 8);
        return new IPAddress(bytes, address.Ipv6ScopeId);
    }

    private static string FormatPhysicalAddress(MibIpNetRow2 row)
    {
        var bytes = new List<byte>(32);
        bytes.AddRange(BitConverter.GetBytes(row.PhysicalAddressPart1));
        bytes.AddRange(BitConverter.GetBytes(row.PhysicalAddressPart2));
        bytes.AddRange(BitConverter.GetBytes(row.PhysicalAddressPart3));
        bytes.AddRange(BitConverter.GetBytes(row.PhysicalAddressPart4));
        var length = Math.Min((int)row.PhysicalAddressLength, bytes.Count);
        if (length == 0 || bytes.Take(length).All(value => value == 0))
        {
            return string.Empty;
        }

        return string.Join(
            "-",
            bytes.Take(length).Select(value => value.ToString("X2")));
    }

    private static string FormatNeighborState(int state) => state switch
    {
        0 => "Unreachable",
        1 => "Incomplete",
        2 => "Probe",
        3 => "Delay",
        4 => "Stale",
        5 => "Reachable",
        6 => "Permanent",
        _ => $"State {state}"
    };

    private static string FormatRouteProtocol(int protocol) => protocol switch
    {
        1 => "Other",
        2 => "Local",
        3 => "NetMgmt",
        4 => "Icmp",
        5 => "Egp",
        8 => "Rip",
        13 => "Ospf",
        14 => "Bgp",
        10002 => "AutoStatic",
        10006 => "Static",
        10007 => "StaticNonDod",
        _ => protocol.ToString()
    };

    private static bool IsVirtual(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType is NetworkInterfaceType.Tunnel
            or NetworkInterfaceType.Ppp)
        {
            return true;
        }

        var text = $"{adapter.Name} {adapter.Description}";
        return new[]
        {
            "virtual", "hyper-v", "wsl", "docker", "vpn", "wireguard",
            "tailscale", "loopback", "tap", "tun"
        }.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAdapterId(string value) =>
        Guid.TryParse(value, out var adapterId)
            ? adapterId.ToString("D")
            : value.Trim().Trim('{', '}');

    private static bool IsUnspecified(string value) =>
        value is "0.0.0.0" or "::" or "::0";

    private static bool IsMulticast(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6Multicast;
        }

        var first = address.GetAddressBytes()[0];
        return first is >= 224 and <= 239;
    }

    private static string ParseIpSortKey(string value) =>
        IPAddress.TryParse(value, out var address)
            ? Convert.ToHexString(address.GetAddressBytes())
            : value;

    internal static (
        int RowSize,
        int InterfaceIndexOffset,
        int InterfaceLuidOffset,
        int PhysicalAddressOffset) GetNeighborInteropLayoutForTesting() =>
        (
            Marshal.SizeOf<MibIpNetRow2>(),
            Marshal.OffsetOf<MibIpNetRow2>(nameof(MibIpNetRow2.InterfaceIndex)).ToInt32(),
            Marshal.OffsetOf<MibIpNetRow2>(nameof(MibIpNetRow2.InterfaceLuid)).ToInt32(),
            Marshal.OffsetOf<MibIpNetRow2>(nameof(MibIpNetRow2.PhysicalAddressPart1)).ToInt32());

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpForwardTable2(
        ushort addressFamily,
        out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpNetTable2(
        ushort addressFamily,
        out IntPtr table);

    [DllImport("iphlpapi.dll")]
    private static extern void FreeMibTable(IntPtr memory);

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    private struct SockaddrInet
    {
        [FieldOffset(0)]
        public ushort Family;

        [FieldOffset(4)]
        public uint Ipv4Address;

        [FieldOffset(8)]
        public ulong Ipv6Part1;

        [FieldOffset(16)]
        public ulong Ipv6Part2;

        [FieldOffset(24)]
        public uint Ipv6ScopeId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct IpAddressPrefix
    {
        [FieldOffset(0)]
        public SockaddrInet Prefix;

        [FieldOffset(28)]
        public byte PrefixLength;
    }

    [StructLayout(LayoutKind.Explicit, Size = 104)]
    private struct MibIpForwardRow2
    {
        [FieldOffset(0)]
        public ulong InterfaceLuid;

        [FieldOffset(8)]
        public uint InterfaceIndex;

        [FieldOffset(12)]
        public IpAddressPrefix DestinationPrefix;

        [FieldOffset(44)]
        public SockaddrInet NextHop;

        [FieldOffset(72)]
        public byte SitePrefixLength;

        [FieldOffset(76)]
        public uint ValidLifetime;

        [FieldOffset(80)]
        public uint PreferredLifetime;

        [FieldOffset(84)]
        public uint Metric;

        [FieldOffset(88)]
        public int Protocol;

        [FieldOffset(92)]
        public byte Loopback;

        [FieldOffset(93)]
        public byte AutoconfigureAddress;

        [FieldOffset(94)]
        public byte Publish;

        [FieldOffset(95)]
        public byte Immortal;

        [FieldOffset(96)]
        public uint Age;

        [FieldOffset(100)]
        public int Origin;
    }

    // Windows SDK netioapi.h: SOCKADDR_INET, NET_IFINDEX, NET_LUID,
    // IF_MAX_PHYS_ADDRESS_LENGTH (32), length, state, flags, reachability.
    [StructLayout(LayoutKind.Explicit, Size = 88)]
    private struct MibIpNetRow2
    {
        [FieldOffset(0)]
        public SockaddrInet Address;

        [FieldOffset(28)]
        public uint InterfaceIndex;

        [FieldOffset(32)]
        public ulong InterfaceLuid;

        [FieldOffset(40)]
        public ulong PhysicalAddressPart1;

        [FieldOffset(48)]
        public ulong PhysicalAddressPart2;

        [FieldOffset(56)]
        public ulong PhysicalAddressPart3;

        [FieldOffset(64)]
        public ulong PhysicalAddressPart4;

        [FieldOffset(72)]
        public uint PhysicalAddressLength;

        [FieldOffset(76)]
        public int State;

        [FieldOffset(80)]
        public byte Flags;

        [FieldOffset(84)]
        public uint ReachabilityTime;
    }
}
