using Hypershell.Services;
using System.Text.Json;

internal static class MyNetDesktopHostVerifier
{
    public static Task VerifyAsync(string verifierRoot)
    {
        // Mutex ownership belongs to the current thread. Keep ownership and disposal
        // in this synchronous scope; use worker threads only for competing clients.
        var settings = new SettingsService(Path.Combine(verifierRoot, "mynet-host", "primary"));
        var equivalent = new SettingsService(settings.ApplicationDataDirectory + Path.DirectorySeparatorChar);
        var isolated = new SettingsService(Path.Combine(verifierRoot, "mynet-host", "independent"));
        Require(MyNetDesktopHost.GetInstanceName(settings) == MyNetDesktopHost.GetInstanceName(equivalent),
            "Equivalent app-data paths must resolve to the same desktop session.");
        Require(MyNetDesktopHost.GetInstanceName(settings) != MyNetDesktopHost.GetInstanceName(isolated),
            "Independent app-data directories must have distinct desktop sessions.");
        Require(!MyNetDesktopHost.TryActivate(settings),
            "A desktop without an owner must not report successful activation.");
        Require(MyNetDesktopHost.TryOwn(settings, out var owned) && owned is not null,
            "The first desktop client must acquire the session.");
        using (var host = owned!)
        {
            var activated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Listen(() => activated.TrySetResult(true));
            var second = Task.Run(() =>
            {
                var acquired = MyNetDesktopHost.TryOwn(equivalent, out var duplicate);
                duplicate?.Dispose();
                return acquired;
            });
            Require(!second.GetAwaiter().GetResult(),
                "A second desktop client must activate the existing session instead of owning it.");
            Require(activated.Task.Wait(TimeSpan.FromSeconds(5)),
                "The existing desktop must receive the second client's activation event.");

            Require(MyNetDesktopHost.TryOwn(isolated, out var separate) && separate is not null,
                "An independent app-data directory must be able to run a separate desktop.");
            separate!.Dispose();
        }

        Require(MyNetDesktopHost.TryOwn(settings, out var restarted) && restarted is not null,
            "Closing the owning desktop must release the session for a new client.");
        restarted!.Dispose();
        VerifyLaunchRouting(verifierRoot);
        Console.WriteLine("MYNET_DESKTOP_HOST_OK|single-session|activation|isolated-data|restart|registered-launcher|stale-target");
        return Task.CompletedTask;
    }

    private static void VerifyLaunchRouting(string verifierRoot)
    {
        var settings = new SettingsService(Path.Combine(verifierRoot, "mynet-launch-routing"));
        var registration = MyNetDesktopHost.GetLauncherRegistrationPath(settings);
        var installedDirectory = Path.Combine(verifierRoot, "mynet-installed-version");
        Directory.CreateDirectory(installedDirectory);
        Directory.CreateDirectory(settings.AtlasMyNetDirectory);
        var installed = Path.Combine(installedDirectory, "Hypershell.exe");
        File.WriteAllText(installed, "fixture only; never launched");
        File.WriteAllText(Path.ChangeExtension(installed, ".dll"), "fixture only; never launched");
        File.WriteAllText(registration, JsonSerializer.Serialize(new { schemaVersion = 1, executablePath = installed }));
        var previousBuild = Path.Combine(verifierRoot, "old-build", "Hypershell.exe");
        var start = MyNetDesktopHost.CreateStartInfo(settings, previousBuild, Path.ChangeExtension(previousBuild, ".dll"));
        Require(start.FileName == installed && start.WorkingDirectory == installedDirectory,
            "Hypershell must launch the installed MyNet build, not the older main executable.");
        Require(!start.UseShellExecute && start.ArgumentList.SequenceEqual(new[] { "--mynet-desktop" }),
            "The installed desktop must receive only the fixed standalone argument.");
        Require(start.Environment[SettingsService.ApplicationDataOverrideVariable] == settings.ApplicationDataDirectory,
            "Opening MyNet must preserve the current profile and its app registry.");

        File.WriteAllText(registration, "{invalid json");
        RequireRejected(() => MyNetDesktopHost.CreateStartInfo(settings, previousBuild, previousBuild),
            "An invalid registration must not silently start an older build.");
        File.WriteAllText(registration, JsonSerializer.Serialize(new { schemaVersion = 2, executablePath = installed }));
        RequireRejected(() => MyNetDesktopHost.CreateStartInfo(settings, previousBuild, previousBuild),
            "An unknown registration version must not execute a target.");
        File.WriteAllText(registration, JsonSerializer.Serialize(new { schemaVersion = "1", executablePath = installed }));
        RequireRejected(() => MyNetDesktopHost.CreateStartInfo(settings, previousBuild, previousBuild),
            "A malformed registration version must surface a repairable error.");
        File.WriteAllText(registration, JsonSerializer.Serialize(new { schemaVersion = 1, executablePath = "Hypershell.exe" }));
        RequireRejected(() => MyNetDesktopHost.CreateStartInfo(settings, previousBuild, previousBuild),
            "A relative registration must not resolve through the current working directory.");
        File.WriteAllText(registration, JsonSerializer.Serialize(new { schemaVersion = 1, executablePath = installed }));
        File.Delete(installed);
        RequireRejected(() => MyNetDesktopHost.CreateStartInfo(settings, previousBuild, previousBuild),
            "A removed MyNet installation must surface a repairable error instead of using a stale fallback.");
        File.Delete(registration);
        if (typeof(Hypershell.App).Assembly.GetType("Hypershell.MyNetDesktopWindow") is not null)
        {
            var assembly = Path.Combine(verifierRoot, "development build", "Hypershell.dll");
            var development = MyNetDesktopHost.CreateStartInfo(settings, "dotnet.exe", assembly);
            Require(development.FileName == "dotnet.exe"
                    && development.ArgumentList.SequenceEqual(new[] { assembly, "--mynet-desktop" }),
                "An unregistered development build must continue to support dotnet-hosted launch.");
        }
        else
        {
            try { MyNetDesktopHost.CreateStartInfo(settings, previousBuild, previousBuild); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("A main-only shell must report a missing standalone registration instead of relaunching itself.");
        }
    }

    private static void RequireRejected(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
