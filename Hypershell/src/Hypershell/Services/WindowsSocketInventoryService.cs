using Hypershell.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Hypershell.Services;

public interface INetworkSocketInventoryService
{
    IReadOnlyList<NetworkSocketSnapshot> Capture();
    bool TryGetProcessIdentity(
        int processId,
        out NetworkProcessIdentity identity);
}

public sealed class WindowsSocketInventoryService : INetworkSocketInventoryService
{
    private const int AddressFamilyIpv4 = 2;
    private const int AddressFamilyIpv6 = 23;
    private const int ErrorInsufficientBuffer = 122;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;

    private readonly string _currentUserSid =
        WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;

    public IReadOnlyList<NetworkSocketSnapshot> Capture()
    {
        var rows = new List<RawSocketRow>();
        rows.AddRange(ReadTcp4());
        rows.AddRange(ReadTcp6());
        rows.AddRange(ReadUdp4());
        rows.AddRange(ReadUdp6());

        var processes = rows
            .Select(row => row.ProcessId)
            .Distinct()
            .ToDictionary(processId => processId, ResolveProcess);

        return rows
            .Select(row =>
            {
                var process = processes[row.ProcessId];
                var key = string.Join(
                    '|',
                    row.Transport,
                    row.AddressFamily,
                    row.State,
                    row.LocalAddress,
                    row.LocalPort,
                    row.RemoteAddress,
                    row.RemotePort,
                    row.ProcessId);
                return new NetworkSocketSnapshot(
                    key,
                    row.Transport,
                    row.AddressFamily,
                    row.State,
                    row.LocalAddress,
                    row.LocalPort,
                    row.RemoteAddress,
                    row.RemotePort,
                    row.ProcessId,
                    process.Name,
                    process.ExecutablePath,
                    process.OwnerName,
                    process.OwnerSid,
                    !string.IsNullOrWhiteSpace(_currentUserSid)
                    && string.Equals(
                        process.OwnerSid,
                        _currentUserSid,
                        StringComparison.OrdinalIgnoreCase),
                    process.StartTimeUtc);
            })
            .OrderByDescending(row => row.IsListener)
            .ThenBy(row => row.Transport)
            .ThenBy(row => row.LocalPort)
            .ThenBy(row => row.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProcessId)
            .ToList();
    }

    public bool TryGetProcessIdentity(
        int processId,
        out NetworkProcessIdentity identity)
    {
        var metadata = ResolveProcess(processId);
        identity = new NetworkProcessIdentity(
            processId,
            metadata.Name,
            metadata.ExecutablePath,
            metadata.OwnerName,
            metadata.OwnerSid,
            !string.IsNullOrWhiteSpace(_currentUserSid)
            && string.Equals(
                metadata.OwnerSid,
                _currentUserSid,
                StringComparison.OrdinalIgnoreCase),
            metadata.StartTimeUtc);
        return processId > 0
               && (!string.IsNullOrWhiteSpace(metadata.ExecutablePath)
                   || metadata.StartTimeUtc.HasValue);
    }

    private static IEnumerable<RawSocketRow> ReadTcp4()
    {
        using var table = GetTcpTable(
            AddressFamilyIpv4,
            TcpTableClass.OwnerPidAll);
        var count = Marshal.ReadInt32(table.Pointer);
        var rowPointer = IntPtr.Add(table.Pointer, sizeof(uint));
        var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
        for (var index = 0; index < count; index++)
        {
            var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPointer);
            yield return new RawSocketRow(
                NetworkTransport.Tcp,
                AddressFamily.InterNetwork,
                GetTcpState(row.State),
                new IPAddress(row.LocalAddress).ToString(),
                GetPort(row.LocalPort),
                new IPAddress(row.RemoteAddress).ToString(),
                GetPort(row.RemotePort),
                checked((int)row.ProcessId));
            rowPointer = IntPtr.Add(rowPointer, rowSize);
        }
    }

    private static IEnumerable<RawSocketRow> ReadTcp6()
    {
        using var table = GetTcpTable(
            AddressFamilyIpv6,
            TcpTableClass.OwnerPidAll);
        var count = Marshal.ReadInt32(table.Pointer);
        var rowPointer = IntPtr.Add(table.Pointer, sizeof(uint));
        var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
        for (var index = 0; index < count; index++)
        {
            var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPointer);
            yield return new RawSocketRow(
                NetworkTransport.Tcp,
                AddressFamily.InterNetworkV6,
                GetTcpState(row.State),
                new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(),
                GetPort(row.LocalPort),
                new IPAddress(row.RemoteAddress, row.RemoteScopeId).ToString(),
                GetPort(row.RemotePort),
                checked((int)row.ProcessId));
            rowPointer = IntPtr.Add(rowPointer, rowSize);
        }
    }

    private static IEnumerable<RawSocketRow> ReadUdp4()
    {
        using var table = GetUdpTable(
            AddressFamilyIpv4,
            UdpTableClass.OwnerPid);
        var count = Marshal.ReadInt32(table.Pointer);
        var rowPointer = IntPtr.Add(table.Pointer, sizeof(uint));
        var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();
        for (var index = 0; index < count; index++)
        {
            var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPointer);
            yield return new RawSocketRow(
                NetworkTransport.Udp,
                AddressFamily.InterNetwork,
                "Bound",
                new IPAddress(row.LocalAddress).ToString(),
                GetPort(row.LocalPort),
                string.Empty,
                0,
                checked((int)row.ProcessId));
            rowPointer = IntPtr.Add(rowPointer, rowSize);
        }
    }

    private static IEnumerable<RawSocketRow> ReadUdp6()
    {
        using var table = GetUdpTable(
            AddressFamilyIpv6,
            UdpTableClass.OwnerPid);
        var count = Marshal.ReadInt32(table.Pointer);
        var rowPointer = IntPtr.Add(table.Pointer, sizeof(uint));
        var rowSize = Marshal.SizeOf<MibUdp6RowOwnerPid>();
        for (var index = 0; index < count; index++)
        {
            var row = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(rowPointer);
            yield return new RawSocketRow(
                NetworkTransport.Udp,
                AddressFamily.InterNetworkV6,
                "Bound",
                new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(),
                GetPort(row.LocalPort),
                string.Empty,
                0,
                checked((int)row.ProcessId));
            rowPointer = IntPtr.Add(rowPointer, rowSize);
        }
    }

    private static NativeTableBuffer GetTcpTable(
        int addressFamily,
        TcpTableClass tableClass)
    {
        var size = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref size,
            true,
            addressFamily,
            tableClass,
            0);
        if (result != ErrorInsufficientBuffer || size <= 0)
        {
            throw new Win32Exception(
                result,
                "Windows could not determine the TCP endpoint table size.");
        }

        var buffer = new NativeTableBuffer(size);
        result = GetExtendedTcpTable(
            buffer.Pointer,
            ref size,
            true,
            addressFamily,
            tableClass,
            0);
        if (result != 0)
        {
            buffer.Dispose();
            throw new Win32Exception(
                result,
                "Windows could not read the TCP endpoint table.");
        }

        return buffer;
    }

    private static NativeTableBuffer GetUdpTable(
        int addressFamily,
        UdpTableClass tableClass)
    {
        var size = 0;
        var result = GetExtendedUdpTable(
            IntPtr.Zero,
            ref size,
            true,
            addressFamily,
            tableClass,
            0);
        if (result != ErrorInsufficientBuffer || size <= 0)
        {
            throw new Win32Exception(
                result,
                "Windows could not determine the UDP endpoint table size.");
        }

        var buffer = new NativeTableBuffer(size);
        result = GetExtendedUdpTable(
            buffer.Pointer,
            ref size,
            true,
            addressFamily,
            tableClass,
            0);
        if (result != 0)
        {
            buffer.Dispose();
            throw new Win32Exception(
                result,
                "Windows could not read the UDP endpoint table.");
        }

        return buffer;
    }

    private ProcessMetadata ResolveProcess(int processId)
    {
        if (processId <= 0)
        {
            return new ProcessMetadata(
                processId == 0 ? "System" : "Unknown",
                string.Empty,
                string.Empty,
                string.Empty,
                null);
        }

        var executablePath = string.Empty;
        var ownerName = string.Empty;
        var ownerSid = string.Empty;
        var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            false,
            processId);
        if (processHandle != IntPtr.Zero)
        {
            try
            {
                var capacity = 32768;
                var pathBuilder = new StringBuilder(capacity);
                if (QueryFullProcessImageName(
                        processHandle,
                        0,
                        pathBuilder,
                        ref capacity))
                {
                    executablePath = pathBuilder.ToString();
                }

                if (OpenProcessToken(processHandle, TokenQuery, out var token))
                {
                    try
                    {
                        using var identity = new WindowsIdentity(token);
                        ownerName = identity.Name ?? string.Empty;
                        ownerSid = identity.User?.Value ?? string.Empty;
                    }
                    finally
                    {
                        CloseHandle(token);
                    }
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        var name = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(executablePath);
        DateTimeOffset? startTime = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = string.IsNullOrWhiteSpace(name) ? process.ProcessName : name;
            startTime = new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or InvalidOperationException
                                           or Win32Exception
                                           or NotSupportedException)
        {
        }

        return new ProcessMetadata(
            string.IsNullOrWhiteSpace(name) ? $"PID {processId}" : name,
            executablePath,
            ownerName,
            ownerSid,
            startTime);
    }

    private static int GetPort(uint rawPort)
    {
        var bytes = BitConverter.GetBytes(rawPort);
        return (bytes[0] << 8) | bytes[1];
    }

    private static string GetTcpState(uint state) => state switch
    {
        1 => "Closed",
        2 => "Listen",
        3 => "SynSent",
        4 => "SynReceived",
        5 => "Established",
        6 => "FinWait1",
        7 => "FinWait2",
        8 => "CloseWait",
        9 => "Closing",
        10 => "LastAck",
        11 => "TimeWait",
        12 => "DeleteTcb",
        100 => "Bound",
        _ => $"State {state}"
    };

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        TcpTableClass tableClass,
        int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedUdpTable(
        IntPtr udpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        UdpTableClass tableClass,
        int reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        int flags,
        StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private enum TcpTableClass
    {
        BasicListener,
        BasicConnections,
        BasicAll,
        OwnerPidListener,
        OwnerPidConnections,
        OwnerPidAll,
        OwnerModuleListener,
        OwnerModuleConnections,
        OwnerModuleAll
    }

    private enum UdpTableClass
    {
        Basic,
        OwnerPid,
        OwnerModule
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint ProcessId;
    }

    private sealed class NativeTableBuffer : IDisposable
    {
        public NativeTableBuffer(int size)
        {
            Pointer = Marshal.AllocHGlobal(size);
        }

        public IntPtr Pointer { get; private set; }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    private sealed record RawSocketRow(
        NetworkTransport Transport,
        AddressFamily AddressFamily,
        string State,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort,
        int ProcessId);

    private sealed record ProcessMetadata(
        string Name,
        string ExecutablePath,
        string OwnerName,
        string OwnerSid,
        DateTimeOffset? StartTimeUtc);
}
