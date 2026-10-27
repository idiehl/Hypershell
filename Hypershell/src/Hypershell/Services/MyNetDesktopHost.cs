using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

/// <summary>A separate GUI process, with one desktop per profile and installation.</summary>
public sealed class MyNetDesktopHost : IDisposable
{
    private readonly Mutex _ownership;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _listener;
    private bool _disposed;

    private MyNetDesktopHost(Mutex ownership, EventWaitHandle activate)
    {
        _ownership = ownership;
        _activate = activate;
    }

    internal static string GetInstanceName(
        SettingsService settings, string? executablePath = null, string? assemblyPath = null)
    {
        executablePath ??= CurrentExecutable;
        assemblyPath ??= CurrentAssembly;
        var identity = NormalizePath(settings.AtlasMyNetDirectory) + "\0" + NormalizePath(executablePath);
        // dotnet.exe hosts many unrelated assemblies; it cannot identify a desktop alone.
        if (IsDotnetHost(executablePath)) identity += "\0" + NormalizePath(assemblyPath);
        return "Local\\Hypershell.MyNet.Desktop."
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static string CurrentExecutable => Environment.ProcessPath
        ?? throw new InvalidOperationException("The Hypershell executable path is unavailable.");

    private static string CurrentAssembly =>
        System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } path
            ? path : typeof(App).Assembly.Location;

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant();

    private static bool IsDotnetHost(string executablePath) =>
        string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase);

    public static void OpenDesktop(SettingsService settings)
    {
        // Resolve the registered target before activation. A previous desktop can
        // remain open during an update without stealing launches of the new one.
        var start = CreateStartInfo(settings, CurrentExecutable, typeof(App).Assembly.Location);
        var targetAssembly = IsDotnetHost(start.FileName)
            ? start.ArgumentList[0] : Path.ChangeExtension(start.FileName, ".dll");
        if (TryActivate(settings, start.FileName, targetAssembly))
        {
            return;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("MyNet could not be started.");
    }

    public static string GetLauncherRegistrationPath(SettingsService settings) =>
        Path.Combine(settings.AtlasMyNetDirectory, "desktop-launcher.device.json");

    internal static ProcessStartInfo CreateStartInfo(
        SettingsService settings, string currentExecutable, string assemblyPath)
    {
        // The desktop can be installed independently of the main Hypershell build.
        // A device-local registration keeps both launch surfaces on the same version.
        var registeredExecutable = ReadRegisteredExecutable(settings);
        var executable = registeredExecutable ?? currentExecutable;
        if (registeredExecutable is null
            && typeof(App).Assembly.GetType("Hypershell.MyNetDesktopWindow") is null)
        {
            throw new InvalidOperationException(
                "MyNet has not been installed for this Hypershell profile. Install the MyNet desktop launcher first.");
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(registeredExecutable ?? assemblyPath)
                ?? AppContext.BaseDirectory
        };
        if (registeredExecutable is null && IsDotnetHost(executable))
        {
            start.ArgumentList.Add(assemblyPath);
        }

        start.ArgumentList.Add("--mynet-desktop");
        start.Environment[SettingsService.ApplicationDataOverrideVariable] = settings.ApplicationDataDirectory;
        return start;
    }

    private static string? ReadRegisteredExecutable(SettingsService settings)
    {
        var path = GetLauncherRegistrationPath(settings);
        if (!File.Exists(path)) return null;

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1
                || !root.TryGetProperty("executablePath", out var executableProperty)
                || executableProperty.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The MyNet launcher registration is invalid. Reinstall the MyNet desktop launcher.");
            }

            var executable = executableProperty.GetString() ?? string.Empty;
            if (!Path.IsPathFullyQualified(executable)
                || !string.Equals(Path.GetFileName(executable), "Hypershell.exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(executable)
                || !File.Exists(Path.ChangeExtension(executable, ".dll")))
            {
                throw new InvalidDataException("The installed MyNet app could not be found. Reinstall the MyNet desktop launcher.");
            }

            return Path.GetFullPath(executable);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The MyNet launcher registration is invalid. Reinstall the MyNet desktop launcher.", exception);
        }
    }

    public static bool TryActivate(SettingsService settings)
    {
        var registered = ReadRegisteredExecutable(settings);
        return TryActivate(settings, registered ?? CurrentExecutable,
            registered is null ? CurrentAssembly : Path.ChangeExtension(registered, ".dll"));
    }

    internal static bool TryActivate(SettingsService settings, string executablePath, string assemblyPath)
    {
        if (!EventWaitHandle.TryOpenExisting(GetInstanceName(settings, executablePath, assemblyPath) + ".activate", out var activate))
        {
            return false;
        }

        using (activate)
        {
            activate.Set();
        }

        return true;
    }

    public static bool TryOwn(SettingsService settings, out MyNetDesktopHost? host)
        => TryOwn(settings, CurrentExecutable, CurrentAssembly, out host);

    internal static bool TryOwn(
        SettingsService settings, string executablePath, string assemblyPath, out MyNetDesktopHost? host)
    {
        host = null;
        // A desktop owns the running installation, even if registration changes
        // after startup. Existing windows and their drafts can stay alive.
        var name = GetInstanceName(settings, executablePath, assemblyPath);
        var ownership = new Mutex(false, name);
        bool acquired;
        try
        {
            acquired = ownership.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            ownership.Dispose();
            // Another process may be between acquiring ownership and creating its event.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (TryActivate(settings, executablePath, assemblyPath))
                {
                    break;
                }

                Thread.Sleep(40);
            }

            return false;
        }

        try
        {
            host = new MyNetDesktopHost(ownership,
                new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate"));
            return true;
        }
        catch
        {
            ownership.ReleaseMutex();
            ownership.Dispose();
            throw;
        }
    }

    public void Listen(Action activate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _listener?.Unregister(null);
        _listener = ThreadPool.RegisterWaitForSingleObject(_activate,
            (_, _) => { if (!_disposed) activate(); }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener?.Unregister(null);
        _activate.Dispose();
        _ownership.ReleaseMutex();
        _ownership.Dispose();
    }
}
