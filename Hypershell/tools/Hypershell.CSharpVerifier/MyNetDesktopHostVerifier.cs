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
        VerifyTargetScopedActivation(verifierRoot);
        Console.WriteLine("MYNET_DESKTOP_HOST_OK|single-session|activation|isolated-data|restart|registered-launcher|stale-target|installation-scope|legacy-activation|dotnet-assembly-scope");
        return Task.CompletedTask;
    }

    private static void VerifyTargetScopedActivation(string verifierRoot)
    {
        var settings = new SettingsService(Path.Combine(verifierRoot, "mynet-target-activation"));
        var previous = Path.Combine(verifierRoot, "previous-installation", "Hypershell.exe");
        var installed = Path.Combine(verifierRoot, "current-installation", "Hypershell.exe");
        var previousAssembly = Path.ChangeExtension(previous, ".dll");
        var installedAssembly = Path.ChangeExtension(installed, ".dll");
        Directory.CreateDirectory(settings.AtlasMyNetDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(installed, "fixture only; never launched");
        File.WriteAllText(installedAssembly, "fixture only; never launched");
        File.WriteAllText(MyNetDesktopHost.GetLauncherRegistrationPath(settings),
            JsonSerializer.Serialize(new { schemaVersion = 1, executablePath = installed }));
        Require(MyNetDesktopHost.GetInstanceName(settings, previous, previousAssembly)
                != MyNetDesktopHost.GetInstanceName(settings, installed, installedAssembly),
            "Two installations sharing a profile must have different desktop sessions.");

        // Simulate a pre-update desktop's data-directory-only event. It must not
        // suppress startup or receive a registered installation's activation.
        var legacyIdentity = Path.GetFullPath(settings.AtlasMyNetDirectory)
            .TrimEnd('\\', '/').ToUpperInvariant();
        var legacyName = "Local\\Hypershell.MyNet.Desktop."
            + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(legacyIdentity)));
        using var legacyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, legacyName + ".activate");
        Require(!MyNetDesktopHost.TryActivate(settings),
            "An old global activation event must not impersonate the installed desktop.");
        Require(!legacyEvent.WaitOne(0), "Activation must leave the legacy desktop untouched.");

        Require(MyNetDesktopHost.TryOwn(settings, previous, previousAssembly, out var previousHost)
                && previousHost is not null,
            "The previous installation must be able to retain its desktop session.");
        using (previousHost!)
        {
            Require(!MyNetDesktopHost.TryActivate(settings),
                "A previous installation's desktop must not suppress startup of the registered installation.");
            Require(MyNetDesktopHost.TryOwn(settings, installed, installedAssembly, out var currentHost)
                    && currentHost is not null,
                "The installed desktop must start while previous windows remain open.");
            using (currentHost!)
            using (var previousActivated = new ManualResetEventSlim())
            using (var currentActivated = new ManualResetEventSlim())
            {
                previousHost!.Listen(previousActivated.Set);
                currentHost!.Listen(currentActivated.Set);
                Require(MyNetDesktopHost.TryActivate(settings),
                    "The registered installation must receive normal activation.");
                Require(currentActivated.Wait(TimeSpan.FromSeconds(5)),
                    "The current desktop must receive its activation event.");
                Require(!previousActivated.Wait(TimeSpan.FromMilliseconds(100)) && !legacyEvent.WaitOne(0),
                    "Launching the installed desktop must not activate previous or legacy desktops.");
                currentActivated.Reset();
                var duplicate = Task.Run(() =>
                {
                    var acquired = MyNetDesktopHost.TryOwn(settings, installed, installedAssembly, out var host);
                    host?.Dispose();
                    return acquired;
                });
                Require(!duplicate.GetAwaiter().GetResult() && currentActivated.Wait(TimeSpan.FromSeconds(5)),
                    "A duplicate launch must activate only the same installation's desktop.");
                Require(MyNetDesktopHost.TryActivate(settings, previous, previousAssembly)
                        && previousActivated.Wait(TimeSpan.FromSeconds(5)),
                    "The previous desktop must remain available to its own installation.");
            }
        }

        var dotnet = Path.Combine(verifierRoot, "runtime", "dotnet.exe");
        Require(MyNetDesktopHost.GetInstanceName(settings, dotnet, previousAssembly)
                != MyNetDesktopHost.GetInstanceName(settings, dotnet, installedAssembly),
            "dotnet-hosted desktop sessions must include their assembly path in the identity.");
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()!.Location;
        Require(MyNetDesktopHost.GetInstanceName(settings, dotnet)
                == MyNetDesktopHost.GetInstanceName(settings, dotnet, entryAssembly),
            "A dotnet host's default identity must use the actual entry assembly, including verifier hosts.");
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
