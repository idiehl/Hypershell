using Hypershell.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Hypershell.Services;

public sealed record NetworkCensusProgress(
    int Completed,
    int Total,
    string CurrentAddress)
{
    public int Percent => Total == 0
        ? 0
        : (int)Math.Round(Completed * 100d / Total);
}

public interface INetworkCensusService
{
    Task<IReadOnlyList<NetworkCensusHostSnapshot>> ScanAsync(
        string cidrText,
        IReadOnlyList<int> ports,
        IReadOnlyList<NetworkNeighborSnapshot> knownNeighbors,
        IProgress<NetworkCensusProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class NetworkCensusService : INetworkCensusService
{
    public const int MaximumPorts = 16;
    private const int MaximumConcurrentHosts = 12;
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromMilliseconds(350);

    public async Task<IReadOnlyList<NetworkCensusHostSnapshot>> ScanAsync(
        string cidrText,
        IReadOnlyList<int> ports,
        IReadOnlyList<NetworkNeighborSnapshot> knownNeighbors,
        IProgress<NetworkCensusProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!NetworkCidrUtility.TryParse(cidrText, out var cidr))
        {
            throw new ArgumentException("Select a valid connected IPv4 subnet.", nameof(cidrText));
        }

        if (cidr.AddressCount > NetworkCidrUtility.MaximumCensusAddresses)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cidrText),
                $"The census range is limited to {NetworkCidrUtility.MaximumCensusAddresses} addresses.");
        }

        if (!IsAttachedSubnet(cidr))
        {
            throw new InvalidOperationException(
                "The census range must contain an IPv4 address on an active local interface.");
        }

        var selectedPorts = ports
            .Where(port => port is > 0 and <= 65_535)
            .Distinct()
            .OrderBy(port => port)
            .ToList();
        if (selectedPorts.Count == 0 || selectedPorts.Count > MaximumPorts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ports),
                $"Choose between 1 and {MaximumPorts} TCP ports.");
        }

        var addresses = NetworkCidrUtility.EnumerateHostAddresses(cidr);
        var neighborMap = knownNeighbors
            .Where(item => !string.IsNullOrWhiteSpace(item.IpAddress))
            .GroupBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var results = new ConcurrentBag<NetworkCensusHostSnapshot>();
        using var concurrency = new SemaphoreSlim(
            MaximumConcurrentHosts,
            MaximumConcurrentHosts);
        var completed = 0;
        var tasks = addresses.Select(async address =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                await Task.Delay(15, cancellationToken);
                var pingResponded = await TryPingAsync(address, cancellationToken);
                var openPorts = new List<int>();
                foreach (var port in selectedPorts)
                {
                    if (await TryConnectAsync(address, port, cancellationToken))
                    {
                        openPorts.Add(port);
                    }
                }

                if (!pingResponded && openPorts.Count == 0)
                {
                    return;
                }

                var hostName = await TryResolveHostNameAsync(
                    address,
                    cancellationToken);
                neighborMap.TryGetValue(address.ToString(), out var neighbor);
                results.Add(new NetworkCensusHostSnapshot(
                    address.ToString(),
                    hostName,
                    neighbor?.MacAddress ?? string.Empty,
                    pingResponded,
                    openPorts,
                    DateTimeOffset.UtcNow));
            }
            finally
            {
                var current = Interlocked.Increment(ref completed);
                progress?.Report(new NetworkCensusProgress(
                    current,
                    addresses.Count,
                    address.ToString()));
                concurrency.Release();
            }
        });
        await Task.WhenAll(tasks);

        return results
            .OrderBy(item => NetworkCidrUtility.ToUInt32(
                IPAddress.Parse(item.Address)))
            .ToList();
    }

    public static IReadOnlyList<int> ParsePorts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Enter one or more TCP ports.");
        }

        var result = new List<int>();
        foreach (var part in value.Split(
                     [',', ';', ' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var port) || port is < 1 or > 65_535)
            {
                throw new FormatException($"'{part}' is not a valid TCP port.");
            }

            if (!result.Contains(port))
            {
                result.Add(port);
            }
        }

        if (result.Count > MaximumPorts)
        {
            throw new FormatException(
                $"Choose no more than {MaximumPorts} TCP ports per census.");
        }

        return result;
    }

    private static async Task<bool> TryPingAsync(
        IPAddress address,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 350)
                .WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception exception) when (exception is PingException
                                           or OperationCanceledException)
        {
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return false;
        }
    }

    private static async Task<bool> TryConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(TcpTimeout);
        try
        {
            using var client = new TcpClient(address.AddressFamily);
            await client.ConnectAsync(address, port, timeoutSource.Token);
            return client.Connected;
        }
        catch (Exception exception) when (exception is SocketException
                                           or OperationCanceledException)
        {
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return false;
        }
    }

    private static async Task<string> TryResolveHostNameAsync(
        IPAddress address,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(600));
        try
        {
            var entry = await Dns.GetHostEntryAsync(
                address.ToString(),
                timeoutSource.Token);
            return string.Equals(
                entry.HostName,
                address.ToString(),
                StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : entry.HostName;
        }
        catch (Exception exception) when (exception is SocketException
                                           or OperationCanceledException)
        {
            if (exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return string.Empty;
        }
    }

    private static bool IsAttachedSubnet(Ipv4Cidr cidr)
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up
                                    && item.NetworkInterfaceType
                                    != NetworkInterfaceType.Loopback))
        {
            try
            {
                if (adapter.GetIPProperties().UnicastAddresses.Any(item =>
                        item.Address.AddressFamily == AddressFamily.InterNetwork
                        && NetworkCidrUtility.Contains(cidr, item.Address)))
                {
                    return true;
                }
            }
            catch (NetworkInformationException)
            {
            }
        }

        return false;
    }
}
