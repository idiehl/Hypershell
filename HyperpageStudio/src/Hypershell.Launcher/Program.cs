using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hypershell.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        _ = SetCurrentProcessExplicitAppUserModelID(LaunchTarget.ApplicationUserModelId);
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LaunchTarget.RecursionEnvironmentVariable)))
                throw new InvalidDataException("The configured target is another launcher. Set executablePath to the published Hypershell application, not a launcher or shortcut.");

            var launcher = Environment.ProcessPath ?? throw new InvalidOperationException("Windows did not provide the launcher location.");
            var manifest = Path.Combine(AppContext.BaseDirectory, "launch-target.json");
            var target = LaunchTarget.Load(manifest, launcher);
            using var process = Process.Start(target.CreateStartInfo(args))
                ?? throw new InvalidOperationException("Windows did not start the configured Hypershell application.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or Win32Exception or System.Text.Json.JsonException)
        {
            _ = MessageBox(IntPtr.Zero,
                $"Hypershell could not start.\n\n{exception.Message}\n\nCheck launch-target.json beside this launcher or run the Hypershell installer again. No other build was selected.",
                "Hypershell launcher", 0x00000010 | 0x00010000);
            return 1;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string title, uint type);
}
