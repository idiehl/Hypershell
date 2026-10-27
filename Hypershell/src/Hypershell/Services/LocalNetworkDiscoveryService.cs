using Hypershell.Models;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Hypershell.Services;

public sealed record LocalNetworkDiscoveryResult(
    IReadOnlyList<NetworkDiscoveryObservation> Observations,
    string Diagnostics);

public interface ILocalNetworkDiscoveryService
{
    Task<LocalNetworkDiscoveryResult> DiscoverAsync(
        NetworkOverviewSnapshot overview,
        CancellationToken cancellationToken = default);
}

public sealed class LocalNetworkDiscoveryService
    : ILocalNetworkDiscoveryService
{
    private static readonly IPEndPoint MdnsEndpoint = new(
        IPAddress.Parse("224.0.0.251"),
        5353);
    private static readonly IPEndPoint SsdpEndpoint = new(
        IPAddress.Parse("239.255.255.250"),
        1900);
    private static readonly IPEndPoint WsDiscoveryEndpoint = new(
        IPAddress.Parse("239.255.255.250"),
        3702);

    private static readonly string[] CommonMdnsServiceTypes =
    [
        "_http._tcp.local",
        "_https._tcp.local",
        "_ssh._tcp.local",
        "_smb._tcp.local",
        "_workstation._tcp.local",
        "_device-info._tcp.local",
        "_ipp._tcp.local",
        "_printer._tcp.local",
        "_pdl-datastream._tcp.local",
        "_googlecast._tcp.local",
        "_airplay._tcp.local",
        "_raop._tcp.local",
        "_rtsp._tcp.local",
        "_home-assistant._tcp.local",
        "_mqtt._tcp.local"
    ];

    public async Task<LocalNetworkDiscoveryResult> DiscoverAsync(
        NetworkOverviewSnapshot overview,
        CancellationToken cancellationToken = default)
    {
        var localAddresses = GetPhysicalIpv4Addresses(overview);
        if (localAddresses.Count == 0)
        {
            return new LocalNetworkDiscoveryResult(
                [],
                "No active physical IPv4 interface is available for local discovery.");
        }

        var diagnostics = new List<string>();
        var observations = new List<NetworkDiscoveryObservation>();
        var tasks = localAddresses.Select(async localAddress =>
        {
            var mdnsResults = new List<NetworkDiscoveryObservation>();
            var ssdpResults = new List<NetworkDiscoveryObservation>();
            var wsDiscoveryResults = new List<NetworkDiscoveryObservation>();
            await Task.WhenAll(
                TryDiscoverAsync(
                    "mDNS",
                    () => DiscoverMdnsAsync(localAddress, cancellationToken),
                    mdnsResults,
                    diagnostics,
                    cancellationToken),
                TryDiscoverAsync(
                    "SSDP",
                    () => DiscoverSsdpAsync(localAddress, cancellationToken),
                    ssdpResults,
                    diagnostics,
                    cancellationToken),
                TryDiscoverAsync(
                    "WS-Discovery",
                    () => DiscoverWsAsync(localAddress, cancellationToken),
                    wsDiscoveryResults,
                    diagnostics,
                    cancellationToken));
            return mdnsResults
                .Concat(ssdpResults)
                .Concat(wsDiscoveryResults)
                .ToList();
        });

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
        {
            observations.AddRange(result);
        }

        return new LocalNetworkDiscoveryResult(
            observations
                .GroupBy(CreateObservationKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            string.Join(" · ", diagnostics.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<IPAddress> GetPhysicalIpv4Addresses(
        NetworkOverviewSnapshot overview) => overview.Interfaces
        .Where(item => item.Status == OperationalStatus.Up && !item.IsVirtual)
        .SelectMany(item => item.Addresses)
        .Select(value => value.Split('/')[0])
        .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
        .Where(address => address is
            { AddressFamily: AddressFamily.InterNetwork })
        .Cast<IPAddress>()
        .Where(address => !IPAddress.IsLoopback(address))
        .Distinct()
        .ToList();

    private static async Task TryDiscoverAsync(
        string label,
        Func<Task<IReadOnlyList<NetworkDiscoveryObservation>>> action,
        ICollection<NetworkDiscoveryObservation> target,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var observation in await action())
            {
                target.Add(observation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SocketException
                                           or IOException
                                           or XmlException
                                           or InvalidDataException)
        {
            lock (diagnostics)
            {
                diagnostics.Add($"{label} unavailable: {exception.Message}");
            }
        }
    }

    private static async Task<IReadOnlyList<NetworkDiscoveryObservation>>
        DiscoverMdnsAsync(
            IPAddress localAddress,
            CancellationToken cancellationToken)
    {
        var typeResponses = await QueryUdpAsync(
            localAddress,
            MdnsEndpoint,
            BuildDnsQuery(["_services._dns-sd._udp.local"], 12),
            TimeSpan.FromMilliseconds(700),
            cancellationToken);
        var serviceTypes = typeResponses
            .SelectMany(response => ParseDnsPacket(response.Buffer)
                .Where(record => record.Type == 12)
                .Select(record => record.Value))
            .Concat(CommonMdnsServiceTypes)
            .Where(value => value.Contains("._tcp.local", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("._udp.local", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizeDnsName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        if (serviceTypes.Count == 0)
        {
            return [];
        }

        var responses = await QueryUdpAsync(
            localAddress,
            MdnsEndpoint,
            BuildDnsQuery(serviceTypes, 12),
            TimeSpan.FromMilliseconds(1100),
            cancellationToken);
        var parsed = responses
            .Select(response => new DnsResponse(
                response.RemoteEndPoint.Address,
                ParseDnsPacket(response.Buffer)))
            .ToList();
        var allRecords = parsed.SelectMany(response => response.Records).ToList();
        var addresses = allRecords
            .Where(record => record.Type is 1 or 28
                             && !string.IsNullOrWhiteSpace(record.Value))
            .GroupBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Value).First(),
                StringComparer.OrdinalIgnoreCase);
        var srvRecords = allRecords
            .Where(record => record.Type == 33)
            .GroupBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var result = new List<NetworkDiscoveryObservation>();
        foreach (var response in parsed)
        {
            foreach (var pointer in response.Records.Where(record => record.Type == 12))
            {
                var serviceType = NormalizeDnsName(pointer.Name);
                if (!serviceTypes.Contains(serviceType, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var instance = NormalizeDnsName(pointer.Value);
                srvRecords.TryGetValue(instance, out var srv);
                var address = srv is not null
                              && addresses.TryGetValue(srv.Value, out var resolved)
                    ? resolved
                    : response.SourceAddress.ToString();
                var displayName = instance.EndsWith(
                    "." + serviceType,
                    StringComparison.OrdinalIgnoreCase)
                    ? instance[..^(serviceType.Length + 1)]
                    : instance;
                result.Add(new NetworkDiscoveryObservation(
                    NetworkDiscoverySource.Mdns,
                    address,
                    string.Empty,
                    displayName,
                    serviceType,
                    srv?.Port ?? 0,
                    srv is null
                        ? serviceType
                        : $"{serviceType} · {srv.Value}:{srv.Port}",
                    DateTimeOffset.UtcNow));
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<NetworkDiscoveryObservation>>
        DiscoverSsdpAsync(
            IPAddress localAddress,
            CancellationToken cancellationToken)
    {
        var request = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n"
            + "HOST: 239.255.255.250:1900\r\n"
            + "MAN: \"ssdp:discover\"\r\n"
            + "MX: 1\r\n"
            + "ST: ssdp:all\r\n"
            + "USER-AGENT: Hypershell-AtlasMyNet/1.0\r\n\r\n");
        var responses = await QueryUdpAsync(
            localAddress,
            SsdpEndpoint,
            request,
            TimeSpan.FromMilliseconds(1300),
            cancellationToken);
        var result = new List<NetworkDiscoveryObservation>();
        foreach (var response in responses)
        {
            var text = Encoding.UTF8.GetString(response.Buffer);
            var headers = ParseHttpHeaders(text);
            headers.TryGetValue("ST", out var serviceType);
            headers.TryGetValue("USN", out var usn);
            headers.TryGetValue("SERVER", out var server);
            headers.TryGetValue("LOCATION", out var location);
            var port = Uri.TryCreate(location, UriKind.Absolute, out var uri)
                ? uri.Port
                : 0;
            result.Add(new NetworkDiscoveryObservation(
                NetworkDiscoverySource.Ssdp,
                response.RemoteEndPoint.Address.ToString(),
                string.Empty,
                FirstUseful(usn, serviceType, "SSDP device"),
                serviceType ?? string.Empty,
                port,
                string.Join(" · ", new[] { server, location }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
                DateTimeOffset.UtcNow));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NetworkDiscoveryObservation>>
        DiscoverWsAsync(
            IPAddress localAddress,
            CancellationToken cancellationToken)
    {
        var messageId = $"urn:uuid:{Guid.NewGuid():D}";
        var request = Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:a="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                        xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery">
              <s:Header>
                <a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>
                <a:MessageID>{messageId}</a:MessageID>
                <a:ReplyTo><a:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:Address></a:ReplyTo>
                <a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>
              </s:Header>
              <s:Body><d:Probe /></s:Body>
            </s:Envelope>
            """);
        var responses = await QueryUdpAsync(
            localAddress,
            WsDiscoveryEndpoint,
            request,
            TimeSpan.FromMilliseconds(1300),
            cancellationToken);
        var result = new List<NetworkDiscoveryObservation>();
        foreach (var response in responses)
        {
            using var reader = XmlReader.Create(
                new MemoryStream(response.Buffer),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 256 * 1024
                });
            var document = XDocument.Load(reader, LoadOptions.None);
            var matches = document.Descendants()
                .Where(element => element.Name.LocalName == "ProbeMatch")
                .ToList();
            foreach (var match in matches)
            {
                var types = FindDescendantValue(match, "Types");
                var scopes = FindDescendantValue(match, "Scopes");
                var xaddrs = FindDescendantValue(match, "XAddrs");
                var endpoint = match.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "Address")
                    ?.Value ?? string.Empty;
                var firstAddress = xaddrs.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? string.Empty;
                var port = Uri.TryCreate(firstAddress, UriKind.Absolute, out var uri)
                    ? uri.Port
                    : 0;
                result.Add(new NetworkDiscoveryObservation(
                    NetworkDiscoverySource.WsDiscovery,
                    response.RemoteEndPoint.Address.ToString(),
                    string.Empty,
                    FindWsDisplayName(scopes, endpoint),
                    types,
                    port,
                    string.Join(" · ", new[] { types, firstAddress }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    DateTimeOffset.UtcNow));
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<UdpReceiveResult>> QueryUdpAsync(
        IPAddress localAddress,
        IPEndPoint target,
        byte[] payload,
        TimeSpan responseWindow,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true);
        client.Client.SetSocketOption(
            SocketOptionLevel.IP,
            SocketOptionName.MulticastTimeToLive,
            2);
        client.Client.Bind(new IPEndPoint(localAddress, 0));
        await client.SendAsync(payload, target, cancellationToken);

        var deadline = DateTimeOffset.UtcNow + responseWindow;
        var results = new List<UdpReceiveResult>();
        while (DateTimeOffset.UtcNow < deadline && results.Count < 128)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutSource.CancelAfter(remaining);
            try
            {
                var response = await client.ReceiveAsync(timeoutSource.Token);
                if (response.Buffer.Length is > 0 and <= 65_507)
                {
                    results.Add(response);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return results;
    }

    private static byte[] BuildDnsQuery(
        IReadOnlyList<string> names,
        ushort queryType)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[12]);
        foreach (var name in names)
        {
            WriteDnsName(stream, name);
            WriteUInt16(stream, queryType);
            WriteUInt16(stream, 0x8001); // IN with the unicast-response bit.
        }

        var payload = stream.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4, 2), (ushort)names.Count);
        return payload;
    }

    private static void WriteDnsName(Stream stream, string name)
    {
        foreach (var label in NormalizeDnsName(name).Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63)
            {
                throw new InvalidDataException("A DNS label has an invalid length.");
            }

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

    public static IReadOnlyList<DnsResourceRecord> ParseDnsPacket(byte[] packet)
    {
        if (packet.Length < 12)
        {
            return [];
        }

        var questionCount = ReadUInt16(packet, 4);
        var answerCount = ReadUInt16(packet, 6);
        var authorityCount = ReadUInt16(packet, 8);
        var additionalCount = ReadUInt16(packet, 10);
        var offset = 12;
        for (var index = 0; index < questionCount; index++)
        {
            _ = ReadDnsName(packet, ref offset);
            EnsureRemaining(packet, offset, 4);
            offset += 4;
        }

        var records = new List<DnsResourceRecord>();
        var count = answerCount + authorityCount + additionalCount;
        for (var index = 0; index < count; index++)
        {
            var name = NormalizeDnsName(ReadDnsName(packet, ref offset));
            EnsureRemaining(packet, offset, 10);
            var type = ReadUInt16(packet, offset);
            var dataLength = ReadUInt16(packet, offset + 8);
            offset += 10;
            EnsureRemaining(packet, offset, dataLength);
            var dataOffset = offset;
            var value = string.Empty;
            var port = 0;
            switch (type)
            {
                case 1 when dataLength == 4:
                    value = new IPAddress(packet.AsSpan(offset, 4)).ToString();
                    break;
                case 28 when dataLength == 16:
                    value = new IPAddress(packet.AsSpan(offset, 16)).ToString();
                    break;
                case 12:
                    value = NormalizeDnsName(ReadDnsName(packet, ref dataOffset));
                    break;
                case 16:
                    value = ParseTxtRecord(packet.AsSpan(offset, dataLength));
                    break;
                case 33 when dataLength >= 7:
                    port = ReadUInt16(packet, offset + 4);
                    dataOffset = offset + 6;
                    value = NormalizeDnsName(ReadDnsName(packet, ref dataOffset));
                    break;
            }

            records.Add(new DnsResourceRecord(name, type, value, port));
            offset += dataLength;
        }

        return records;
    }

    private static string ReadDnsName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var position = offset;
        var jumped = false;
        var visited = new HashSet<int>();
        for (var depth = 0; depth < 64; depth++)
        {
            EnsureRemaining(packet, position, 1);
            var length = packet[position];
            if (length == 0)
            {
                position++;
                if (!jumped)
                {
                    offset = position;
                }

                return string.Join('.', labels);
            }

            if ((length & 0xC0) == 0xC0)
            {
                EnsureRemaining(packet, position, 2);
                var pointer = ((length & 0x3F) << 8) | packet[position + 1];
                if (!visited.Add(pointer) || pointer >= packet.Length)
                {
                    throw new InvalidDataException("A DNS compression pointer is invalid.");
                }

                if (!jumped)
                {
                    offset = position + 2;
                    jumped = true;
                }

                position = pointer;
                continue;
            }

            if ((length & 0xC0) != 0 || length > 63)
            {
                throw new InvalidDataException("A DNS label is invalid.");
            }

            position++;
            EnsureRemaining(packet, position, length);
            labels.Add(Encoding.UTF8.GetString(packet, position, length));
            position += length;
            if (!jumped)
            {
                offset = position;
            }
        }

        throw new InvalidDataException("A DNS name exceeded the recursion limit.");
    }

    private static string ParseTxtRecord(ReadOnlySpan<byte> data)
    {
        var values = new List<string>();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (offset + length > data.Length)
            {
                break;
            }

            values.Add(Encoding.UTF8.GetString(data.Slice(offset, length)));
            offset += length;
        }

        return string.Join("; ", values);
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        EnsureRemaining(data, offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    }

    private static void EnsureRemaining(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > data.Length)
        {
            throw new InvalidDataException("A discovery response was truncated.");
        }
    }

    private static string NormalizeDnsName(string value) =>
        value.Trim().TrimEnd('.');

    private static Dictionary<string, string> ParseHttpHeaders(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                     .Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return result;
    }

    private static string FindDescendantValue(XElement element, string localName) =>
        element.Descendants()
            .FirstOrDefault(candidate => candidate.Name.LocalName == localName)
            ?.Value.Trim() ?? string.Empty;

    private static string FindWsDisplayName(string scopes, string endpoint)
    {
        foreach (var value in scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!value.Contains("/name/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = value[(value.LastIndexOf('/') + 1)..];
            return Uri.UnescapeDataString(name);
        }

        return FirstUseful(endpoint, "WS-Discovery device");
    }

    private static string FirstUseful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private static string CreateObservationKey(NetworkDiscoveryObservation item) =>
        $"{item.Source}|{item.Address}|{item.DisplayName}|{item.ServiceType}|{item.Port}";

    private sealed record DnsResponse(
        IPAddress SourceAddress,
        IReadOnlyList<DnsResourceRecord> Records);

    public sealed record DnsResourceRecord(
        string Name,
        ushort Type,
        string Value,
        int Port);
}
