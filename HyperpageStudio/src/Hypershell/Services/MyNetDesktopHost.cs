using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

/// <summary>A separate GUI process, with one desktop per local app-data directory.</summary>
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

    internal static string GetInstanceName(SettingsService settings) =>
        "Local\\Hypershell.MyNet.Desktop."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(settings.AtlasMyNetDirectory).TrimEnd('\\', '/').ToUpperInvariant())));

    public static void OpenDesktop(SettingsService settings)
    {
        if (TryActivate(settings))
        {
            return;
        }

        var start = CreateStartInfo(settings, Environment.ProcessPath
            ?? throw new InvalidOperationException("The Hypershell executable path is unavailable."),
            typeof(App).Assembly.Location);
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
        if (registeredExecutable is null
            && string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet",
                StringComparison.OrdinalIgnoreCase))
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
        if (!EventWaitHandle.TryOpenExisting(GetInstanceName(settings) + ".activate", out var activate))
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
    {
        host = null;
        var name = GetInstanceName(settings);
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
                if (TryActivate(settings))
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
