using Hypershell.Models;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

// Native apps have no local HTTP endpoint. Match the complete launch identity so
// multiple apps hosted by the same executable remain independent.
internal sealed class AtlasMyNetNativeAppService(
    AtlasMyNetAppStore store,
    INetworkSocketInventoryService inventory)
{
    internal AtlasMyNetAppRuntime? FindRunning(AtlasMyNetAppDefinition app)
    {
        if (!app.IsNative || app.Launch is not { } launch) return null;
        var executable = Path.GetFullPath(launch.Executable);
        var matches = new List<AtlasMyNetAppRuntime>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId
                        || !inventory.TryGetProcessIdentity(process.Id, out var identity)
                        || !identity.IsCurrentUserOwned || !identity.StartTimeUtc.HasValue
                        || string.IsNullOrWhiteSpace(identity.ExecutablePath)
                        || !string.Equals(Path.GetFullPath(identity.ExecutablePath), executable,
                            StringComparison.OrdinalIgnoreCase)
                        || !TryReadArguments(process.Id, out var arguments)
                        || !arguments.SequenceEqual(launch.Arguments, StringComparer.Ordinal)
                        || process.HasExited
                        || Math.Abs((new DateTimeOffset(process.StartTime.ToUniversalTime())
                            - identity.StartTimeUtc.Value).TotalSeconds) > 1)
                        continue;

                    matches.Add(new AtlasMyNetAppRuntime(app.Id, process.Id, identity.ExecutablePath,
                        identity.StartTimeUtc.Value, 0, string.Empty, StartedThisSession: false));
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or Win32Exception or ArgumentException or NotSupportedException)
                {
                    // Exited or inaccessible processes cannot establish an app identity.
                }
            }
        }
        return matches.OrderBy(runtime => runtime.StartedAtUtc).FirstOrDefault();
    }

    internal async Task<AtlasMyNetAppRuntime> StartAsync(
        AtlasMyNetAppDefinition app,
        Func<ProcessStartInfo, AtlasMyNetAppRuntime> startProcess,
        CancellationToken cancellationToken)
    {
        var operationRoot = Path.Combine(Path.GetDirectoryName(store.AppsPath)!, "operations");
        Directory.CreateDirectory(operationRoot);
        var operationStem = Path.Combine(operationRoot,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.Id.ToUpperInvariant()))));
        using var operation = await AcquireOperationAsync(operationStem + ".lock", cancellationToken);
        if (FindRunning(app) is { } existing)
        {
            await WaitForWindowAsync(app, existing, cancellationToken);
            return existing;
        }

        if (app.Launch is not { } launch)
            throw new InvalidOperationException($"{app.DisplayName} does not define a start command.");
        if (!store.IsTrusted(app))
            throw new UnauthorizedAccessException($"Review and trust {app.DisplayName}'s commands before starting it.");
        var executable = Path.GetFullPath(launch.Executable);
        if (!File.Exists(executable))
            throw new FileNotFoundException($"{app.DisplayName}'s executable could not be found.", executable);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(launch.WorkingDirectory)
                ? Path.GetDirectoryName(executable)! : Path.GetFullPath(launch.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (var argument in launch.Arguments) startInfo.ArgumentList.Add(argument);
        foreach (var pair in launch.Environment) startInfo.Environment[pair.Key] = pair.Value;
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = startProcess(startInfo);
        File.WriteAllText(operationStem + ".runtime.json.tmp", JsonSerializer.Serialize(runtime));
        File.Move(operationStem + ".runtime.json.tmp", operationStem + ".runtime.json", overwrite: true);
        await WaitForWindowAsync(app, runtime, cancellationToken);
        return runtime;
    }

    private async Task WaitForWindowAsync(AtlasMyNetAppDefinition app,
        AtlasMyNetAppRuntime runtime, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(runtime.ProcessId);
                if (process.HasExited)
                    throw new InvalidOperationException($"{app.DisplayName} exited before its window became ready.");
                if (!inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity)
                    || !identity.IsCurrentUserOwned || !identity.StartTimeUtc.HasValue
                    || Math.Abs((identity.StartTimeUtc.Value - runtime.StartedAtUtc).TotalSeconds) > 1
                    || !string.Equals(identity.ExecutablePath, runtime.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Windows could not verify {app.DisplayName}'s process identity.");
                var window = process.MainWindowHandle;
                if (window != IntPtr.Zero)
                {
                    if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
                    SetForegroundWindow(window);
                    return;
                }
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"{app.DisplayName} exited before its window became ready.", exception);
            }
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException($"{app.DisplayName} is running but its window has not appeared yet. Open it again to activate it.");
    }

    private static async Task<FileStream> AcquireOperationAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(35);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            { await Task.Delay(100, cancellationToken); }
        }
    }

    // Windows returns the command line in this process's buffer. No remote memory
    // reads, executable-name guesses, or substring matching are used.
    internal static bool TryReadArguments(int processId, out string[] arguments)
    {
        arguments = [];
        using var process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid) return false;
        _ = NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out var length);
        if (length < Marshal.SizeOf<UnicodeString>() || length > 1024 * 1024) return false;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryInformationProcess(process, 60, buffer, length, out _) < 0) return false;
            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            var offset = value.Buffer.ToInt64() - buffer.ToInt64();
            if (value.Length % 2 != 0 || offset < 0 || offset + value.Length > length) return false;
            var commandLine = Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
            if (string.IsNullOrWhiteSpace(commandLine)) return false;
            var argv = CommandLineToArgvW(commandLine, out var count);
            if (argv == IntPtr.Zero) return false;
            try
            {
                arguments = Enumerable.Range(1, Math.Max(0, count - 1))
                    .Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size)) ?? string.Empty)
                    .ToArray();
                return count > 0;
            }
            finally { _ = LocalFree(argv); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass,
        IntPtr information, int informationLength, out int returnLength);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
