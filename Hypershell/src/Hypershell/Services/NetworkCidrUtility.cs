using Hypershell.Models;
using System.Net;
using System.Net.Sockets;

namespace Hypershell.Services;

public readonly record struct Ipv4Cidr(
    IPAddress NetworkAddress,
    int PrefixLength)
{
    public uint NetworkValue => NetworkCidrUtility.ToUInt32(NetworkAddress);
    public uint Mask => PrefixLength == 0
        ? 0
        : uint.MaxValue << (32 - PrefixLength);
    public uint BroadcastValue => NetworkValue | ~Mask;
    public int AddressCount => PrefixLength >= 32
        ? 1
        : checked((int)Math.Min(1UL << (32 - PrefixLength), int.MaxValue));

    public override string ToString() => $"{NetworkAddress}/{PrefixLength}";
}

public static class NetworkCidrUtility
{
    public const int MaximumCensusAddresses = 256;
    public const int MinimumCensusPrefixLength = 24;

    public static bool TryParse(string? value, out Ipv4Cidr cidr)
    {
        cidr = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out var prefixLength)
            || prefixLength is < 0 or > 32)
        {
            return false;
        }

        var mask = prefixLength == 0
            ? 0
            : uint.MaxValue << (32 - prefixLength);
        cidr = new Ipv4Cidr(
            FromUInt32(ToUInt32(address) & mask),
            prefixLength);
        return true;
    }

    public static Ipv4Cidr FromAddress(IPAddress address, int prefixLength)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("An IPv4 address is required.", nameof(address));
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        var mask = prefixLength == 0
            ? 0
            : uint.MaxValue << (32 - prefixLength);
        return new Ipv4Cidr(
            FromUInt32(ToUInt32(address) & mask),
            prefixLength);
    }

    public static NetworkCensusSubnetChoice CreateCensusChoice(
        IPAddress address,
        int actualPrefixLength,
        string interfaceName)
    {
        var boundedPrefix = Math.Max(
            actualPrefixLength,
            MinimumCensusPrefixLength);
        var cidr = FromAddress(address, boundedPrefix);
        return new NetworkCensusSubnetChoice(
            cidr.ToString(),
            interfaceName,
            address.ToString(),
            boundedPrefix != actualPrefixLength);
    }

    public static bool Contains(Ipv4Cidr cidr, IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return (ToUInt32(address) & cidr.Mask) == cidr.NetworkValue;
    }

    public static IReadOnlyList<IPAddress> EnumerateHostAddresses(Ipv4Cidr cidr)
    {
        if (cidr.AddressCount > MaximumCensusAddresses)
        {
            throw new InvalidOperationException(
                $"A census range may contain at most {MaximumCensusAddresses} addresses.");
        }

        var first = cidr.NetworkValue;
        var last = cidr.BroadcastValue;
        if (cidr.PrefixLength <= 30 && last > first)
        {
            first++;
            last--;
        }

        var result = new List<IPAddress>();
        for (var value = first; value <= last; value++)
        {
            result.Add(FromUInt32(value));
            if (value == uint.MaxValue)
            {
                break;
            }
        }

        return result;
    }

    public static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            throw new ArgumentException("An IPv4 address is required.", nameof(address));
        }

        return ((uint)bytes[0] << 24)
               | ((uint)bytes[1] << 16)
               | ((uint)bytes[2] << 8)
               | bytes[3];
    }

    public static IPAddress FromUInt32(uint value) => new(
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ]);
}
