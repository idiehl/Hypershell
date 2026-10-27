using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hypershell.Services;

/// <summary>
/// Keeps child processes inside a Windows job that terminates every member when
/// Hypershell closes, including forced or unexpected parent termination.
/// </summary>
internal sealed class WindowsProcessLifetimeJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private SafeFileHandle? _handle;

    private WindowsProcessLifetimeJob(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static WindowsProcessLifetimeJob Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Crash-safe local model hosting requires Windows job objects.");
        }

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(
                error,
                "Windows could not create the local-model process boundary.");
        }

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        var informationLength = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var informationPointer = Marshal.AllocHGlobal(informationLength);
        try
        {
            Marshal.StructureToPtr(
                information,
                informationPointer,
                fDeleteOld: false);
            if (!SetInformationJobObject(
                    handle,
                    JobObjectInformationType.ExtendedLimitInformation,
                    informationPointer,
                    checked((uint)informationLength)))
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(
                    error,
                    "Windows could not configure the local-model process boundary.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(informationPointer);
        }

        return new WindowsProcessLifetimeJob(handle);
    }

    public void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var handle = _handle
                     ?? throw new ObjectDisposedException(
                         nameof(WindowsProcessLifetimeJob));
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                "The local-model process exited before Hypershell could secure it.");
        }

        if (!AssignProcessToJobObject(handle, process.Handle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not secure the local-model process.");
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        JobObjectInformationType informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeFileHandle job,
        IntPtr process);

    private enum JobObjectInformationType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
