using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Hypershell.Services;

public static class NetworkAddressPolicy
{
    public static bool IsLocalMachineUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return false;
        }

        if (uri.Scheme is "about" or "data" or "blob" or "file")
        {
            return true;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                uri.Host,
                Dns.GetHostName(),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(RemoveScope(uri.Host), out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Any(unicast => unicast.Address.Equals(address));
    }

    public static async Task<bool> IsPublicDestinationAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(RemoveScope(host), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }

        return addresses.Length > 0 && addresses.Any(IsPublicAddress);
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] == 10
                     || bytes[0] == 127
                     || bytes[0] == 169 && bytes[1] == 254
                     || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                     || bytes[0] == 192 && bytes[1] == 168
                     || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                     || bytes[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) != 0xFC;
        }

        return true;
    }

    private static string RemoveScope(string host)
    {
        var percentIndex = host.IndexOf('%', StringComparison.Ordinal);
        return percentIndex < 0 ? host : host[..percentIndex];
    }
}
