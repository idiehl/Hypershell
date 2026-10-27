using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Hypershell.Services;

public enum StandaloneStudioMode { None, HyperpageStudio, HyperlibEditor }

/// <summary>Independent Studio entry points sharing the existing editor implementation.</summary>
public sealed class StandaloneStudioHost : IDisposable
{
    public const string BaseDataOverrideVariable = "HYPERSHELL_STUDIO_BASE_DATA";
    private readonly Mutex _instance;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _activationWait;
    private readonly bool _ownsInstance;

    public static StandaloneStudioMode ParseMode(IEnumerable<string> arguments)
    {
        var studio = arguments.Contains("--hyperpage-studio", StringComparer.OrdinalIgnoreCase);
        var editor = arguments.Contains("--hyperlib-editor", StringComparer.OrdinalIgnoreCase);
        return studio || editor ? StandaloneStudioMode.HyperpageStudio : StandaloneStudioMode.None;
    }

    public static string GetDisplayName(StandaloneStudioMode mode) => mode switch
    {
        StandaloneStudioMode.HyperpageStudio => "Hyperpage Studio",
        StandaloneStudioMode.HyperlibEditor => "Hyperpage Studio",
        _ => "Hypershell"
    };

    public static string GetAppUserModelId(StandaloneStudioMode mode) => mode switch
    {
        StandaloneStudioMode.HyperpageStudio => "Hypershell.HyperpageStudio",
        StandaloneStudioMode.HyperlibEditor => "Hypershell.HyperpageStudio",
        _ => "Hypershell.Desktop"
    };

    public static string GetArgument(StandaloneStudioMode mode) => "--hyperpage-studio";

    public static void ConfigureProcess(StandaloneStudioMode mode)
    {
        if (mode == StandaloneStudioMode.None) return;
        var original = new SettingsService(Environment.GetEnvironmentVariable(BaseDataOverrideVariable));
        var profile = Path.Combine(original.ApplicationDataDirectory, "Standalone", "hyperpage-studio");
        Directory.CreateDirectory(profile);
        // Seed preferences only. Original sessions, recovery, and account sign-in remain owned by Hypershell.
        foreach (var file in new[] { "settings.json", "ai-provider.device.json", "ai-provider-credential.device.bin" })
        {
            var source = Path.Combine(original.ApplicationDataDirectory, file);
            var destination = Path.Combine(profile, file);
            if (File.Exists(source) && !File.Exists(destination))
            {
                try { File.Copy(source, destination, overwrite: false); }
                catch (IOException) when (File.Exists(destination)) { } // Another first launch seeded it.
            }
        }
        Environment.SetEnvironmentVariable(BaseDataOverrideVariable, original.ApplicationDataDirectory);
        Environment.SetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable, original.AtlasMyNetDirectory);
        Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, profile);
        Environment.SetEnvironmentVariable("HYPERSHELL_APP_USER_MODEL_ID", GetAppUserModelId(mode));
        Environment.SetEnvironmentVariable("HYPERSHELL_RELAUNCH_COMMAND", $"\"{Environment.ProcessPath}\" {GetArgument(mode)}");
        Environment.SetEnvironmentVariable("HYPERSHELL_RELAUNCH_DISPLAY_NAME", GetDisplayName(mode));
        Environment.SetEnvironmentVariable("HYPERSHELL_RELAUNCH_ICON_RESOURCE", $"{Environment.ProcessPath},0");
    }

    public StandaloneStudioHost(StandaloneStudioMode mode)
    {
        var identity = GetAppUserModelId(mode) + "|" + new SettingsService().ApplicationDataDirectory.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        _instance = new Mutex(true, $"Local\\Hypershell.Studio.{hash}", out _ownsInstance);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\Hypershell.Studio.Activate.{hash}");
    }

    public bool IsPrimaryInstance => _ownsInstance;
    public void ActivateExisting() => _activation.Set();

    public void Attach(Window window)
    {
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) =>
            window.Dispatcher.BeginInvoke(() =>
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Show();
                window.Activate();
                SetForegroundWindow(new WindowInteropHelper(window).Handle);
            }), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _activationWait?.Unregister(null);
        _activation.Dispose();
        if (_ownsInstance) _instance.ReleaseMutex();
        _instance.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
