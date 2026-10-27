using Hypershell.Models;
using Hypershell.Services;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

internal static class MyNetShutdownVerifier
{
    private const string FixtureSwitch = "--mynet-shutdown-fixture";

    public static async Task VerifyAsync(string root)
    {
        Directory.CreateDirectory(root);
        try
        {
            await VerifyOwnedHeadlessAsync(Path.Combine(root, "headless"));
            await VerifyCommandAsync(Path.Combine(root, "graceful"), "graceful");
            await VerifyCommandAsync(Path.Combine(root, "hanging"), "hang");
            await VerifyCommandAsync(Path.Combine(root, "failing"), "fail");
            await VerifyExternalSurvivesAsync(Path.Combine(root, "external"));
            await VerifyIdentityGuardsAsync(Path.Combine(root, "identity"));
            await VerifyPendingStartAsync(Path.Combine(root, "pending"));
            Console.WriteLine("MYNET_SHUTDOWN_OK|owned-headless|graceful-command|bounded-command-fallback|external-preserved|exact-process-identity|pending-start|shutdown-latch|already-exited|protected-processes");
        }
        finally
        {
            // All processes here come from fixture PID files beneath this private root.
            // Match creation time and executable before cleaning up a failed assertion.
            foreach (var pidFile in Directory.EnumerateFiles(root, "*.pid", SearchOption.AllDirectories))
            {
                try
                {
                    var fields = File.ReadAllLines(pidFile);
                    using var process = Process.GetProcessById(int.Parse(fields[0]));
                    if (process.Id != Environment.ProcessId && !process.HasExited
                        && process.StartTime.ToUniversalTime().Ticks == long.Parse(fields[1])
                        && string.Equals(process.MainModule?.FileName, fields[2], StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: false);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
        }
    }

    public static async Task RunFixtureAsync(string[] args)
    {
        var mode = args[0];
        var root = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(root);
        using var current = Process.GetCurrentProcess();
        File.WriteAllLines(Path.Combine(root, mode == "serve" ? "server.pid" : "stopper.pid"),
            [current.Id.ToString(), current.StartTime.ToUniversalTime().Ticks.ToString(), current.MainModule!.FileName]);
        if (mode == "stop")
        {
            switch (args[2])
            {
                case "graceful":
                    File.WriteAllText(Path.Combine(root, "stop.signal"), "stop");
                    return;
                case "fail":
                    Environment.ExitCode = 17;
                    return;
                case "hang":
                    await Task.Delay(TimeSpan.FromSeconds(90));
                    return;
                default:
                    throw new InvalidOperationException("Unknown fixture stop mode.");
            }
        }

        var listener = new TcpListener(IPAddress.Loopback, int.Parse(args[2]));
        listener.Start();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var stopping = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                if (File.Exists(Path.Combine(root, "stop.signal")))
                {
                    File.WriteAllText(Path.Combine(root, "graceful.exit"), "clean exit");
                    lifetime.Cancel();
                    return;
                }
                await Task.Delay(25, lifetime.Token);
            }
        });
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(lifetime.Token))) { }
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nready"), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lifetime.Cancel();
            listener.Stop();
            try { await stopping; }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task VerifyOwnedHeadlessAsync(string root)
    {
        using var fixture = new Fixture(root);
        var app = fixture.CreateApp();
        var runtime = await fixture.Service.StartAsync(app);
        using var child = Process.GetProcessById(runtime.ProcessId);
        _ = child.SafeHandle;
        Require(child.MainWindowHandle == IntPtr.Zero, "The owned fixture must be headless.");
        var results = await fixture.Service.StopSessionAppsForShutdownAsync(Definitions(app));
        RequireStopped(results, child, "Owned headless app did not stop during shutdown.");
        Require(!fixture.Service.HasRunningSessionApps(), "Stopped runtime remained tracked.");
        var startRejected = false;
        try { await fixture.Service.StartAsync(app); }
        catch (InvalidOperationException) { startRejected = true; }
        catch (OperationCanceledException) { startRejected = true; }
        Require(startRejected, "A new start escaped the shutdown latch.");
        fixture.Service.CancelShutdown();
        // A recently closed HTTP connection can keep the previous port in TIME_WAIT.
        // Restart with a fresh endpoint; this check concerns the shutdown latch.
        app = fixture.CreateApp(root: Path.Combine(root, "restarted"));
        runtime = await fixture.Service.StartAsync(app);
        using var restarted = Process.GetProcessById(runtime.ProcessId);
        _ = restarted.SafeHandle;
        Require(!restarted.HasExited, "Cancelling shutdown must permit starting again.");
        results = await fixture.Service.StopSessionAppsForShutdownAsync(Definitions(app));
        RequireStopped(results, restarted, "Restarted owned fixture was not stopped.");
    }

    private static async Task VerifyCommandAsync(string root, string mode)
    {
        using var fixture = new Fixture(root);
        var app = fixture.CreateApp(mode);
        var runtime = await fixture.Service.StartAsync(app);
        using var child = Process.GetProcessById(runtime.ProcessId);
        _ = child.SafeHandle;
        var timer = Stopwatch.StartNew();
        var results = await fixture.Service.StopSessionAppsForShutdownAsync(Definitions(app))
            .WaitAsync(TimeSpan.FromSeconds(15));
        timer.Stop();
        RequireStopped(results, child, $"The {mode} stop command prevented shutdown.");
        Require(timer.Elapsed < TimeSpan.FromSeconds(12), $"The {mode} stop command exceeded the bounded shutdown budget.");
        Require(File.Exists(Path.Combine(root, "stopper.pid")), $"The configured {mode} stop command was not attempted.");
        if (mode == "graceful")
        {
            Require(File.Exists(Path.Combine(root, "graceful.exit")) && child.ExitCode == 0,
                "A working configured stop command must let the app exit cleanly.");
        }
        else
        {
            Require(!File.Exists(Path.Combine(root, "graceful.exit")),
                "Fallback fixture unexpectedly exited via its graceful signal.");
        }
        var stopperPid = int.Parse(File.ReadAllLines(Path.Combine(root, "stopper.pid"))[0]);
        Require(!IsAlive(stopperPid), "The stop helper leaked after shutdown completed.");
    }

    private static async Task VerifyExternalSurvivesAsync(string root)
    {
        using var fixture = new Fixture(root);
        var external = fixture.CreateApp(null, "external-app");
        using var externalProcess = StartFixture(external.Launch!);
        await WaitUntilAsync(() => fixture.Web.CheckHealthAsync(external, external.Ports[0]));
        var discovered = await fixture.Service.FindRunningAsync(external);
        Require(discovered is { StartedThisSession: false } && discovered.ProcessId == externalProcess.Id,
            "The fixture listener should be discovered as externally started.");
        var ownedRoot = Path.Combine(root, "owned");
        var owned = fixture.CreateApp(null, "owned-app", ownedRoot);
        var runtime = await fixture.Service.StartAsync(owned);
        using var ownedProcess = Process.GetProcessById(runtime.ProcessId);
        _ = ownedProcess.SafeHandle;
        var results = await fixture.Service.StopSessionAppsForShutdownAsync(Definitions(external, owned));
        RequireStopped(results, ownedProcess, "Owned fixture did not stop beside an external listener.");
        Require(!externalProcess.HasExited && await fixture.Web.CheckHealthAsync(external, external.Ports[0]),
            "Shutdown stopped an untracked external service.");
        File.WriteAllText(Path.Combine(root, "stop.signal"), "stop");
        await externalProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task VerifyIdentityGuardsAsync(string root)
    {
        var inventory = new ControlledInventory();
        using var fixture = new Fixture(root, inventory);
        var app = fixture.CreateApp();
        using var child = StartFixture(app.Launch!);
        await WaitUntilAsync(() => fixture.Web.CheckHealthAsync(app, app.Ports[0]));
        var path = child.MainModule!.FileName;
        var started = new DateTimeOffset(child.StartTime.ToUniversalTime());
        void Reject(string explanation, string expectedPath, DateTimeOffset? expectedStart)
        {
            var result = fixture.Service.ForceStop(child.Id, expectedPath, expectedStart);
            Require(result.Outcome == AtlasMyNetStopOutcome.Rejected && !child.HasExited, explanation);
        }

        Reject("A stale creation time within one second must be rejected.", path, started.AddTicks(1));
        Reject("A different executable path must be rejected.", path + ".different", started);
        Reject("An absent executable path must be rejected.", string.Empty, started);
        Reject("An absent creation time must be rejected.", path, null);
        inventory.Transform = identity => identity with { IsCurrentUserOwned = false };
        Reject("A foreign-owned process must be rejected.", path, started);
        inventory.Transform = identity => identity with { ExecutablePath = path + ".different" };
        Reject("Inventory and the opened process executable must agree.", path + ".different", started);
        inventory.Transform = identity => identity with { StartTimeUtc = started.AddTicks(1) };
        Reject("Inventory and the opened process creation time must agree.", path, started.AddTicks(1));
        inventory.Transform = identity => identity with { ExecutablePath = string.Empty };
        Reject("Unavailable inventory identity must not authorize a stop.", path, started);
        inventory.Transform = null;

        foreach (var processId in new[] { 0, 4, Environment.ProcessId })
        {
            var result = fixture.Service.ForceStop(processId, path, started);
            Require(result.Outcome == AtlasMyNetStopOutcome.Rejected, "Self/system stop protection regressed.");
        }
        File.WriteAllText(Path.Combine(root, "stop.signal"), "stop");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var alreadyExited = fixture.Service.ForceStop(child.Id, path, started);
        Require(alreadyExited.Outcome == AtlasMyNetStopOutcome.AlreadyStopped,
            "An already exited process must count as successful shutdown.");
    }

    private static async Task VerifyPendingStartAsync(string root)
    {
        var inventory = new ControlledInventory { BlockNextCapture = true };
        using var fixture = new Fixture(root, inventory);
        var app = fixture.CreateApp();
        var starting = fixture.Service.StartAsync(app);
        await inventory.CaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = fixture.Service.StopSessionAppsForShutdownAsync(Definitions(app));
        inventory.ReleaseCapture.Set();
        AtlasMyNetAppRuntime? runtime = null;
        try { runtime = await starting.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        Require(!fixture.Service.HasRunningSessionApps(), "A pending start escaped the shutdown snapshot.");
        if (runtime is not null)
            Require(!IsAlive(runtime.ProcessId), "A concurrently started fixture survived shutdown.");
        Require(!File.Exists(Path.Combine(root, "server.pid"))
                || !IsAlive(int.Parse(File.ReadAllLines(Path.Combine(root, "server.pid"))[0])),
            "A pending start left a child alive after shutdown.");
    }

    private static IReadOnlyDictionary<string, AtlasMyNetAppDefinition> Definitions(params AtlasMyNetAppDefinition[] apps) =>
        apps.ToDictionary(app => app.Id, StringComparer.OrdinalIgnoreCase);

    private static void RequireStopped(IReadOnlyList<AtlasMyNetStopResult> results, Process child, string message)
    {
        Require(results.Count == 1 && results[0].ProcessId == child.Id
            && results[0].Outcome is AtlasMyNetStopOutcome.Stopped or AtlasMyNetStopOutcome.AlreadyStopped
            && child.HasExited, message);
    }

    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await condition()) return;
            await Task.Delay(40);
        }
        throw new InvalidOperationException("The private fixture endpoint did not become ready.");
    }

    private static Process StartFixture(AtlasMyNetLaunchDefinition launch)
    {
        var info = new ProcessStartInfo(launch.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = launch.WorkingDirectory
        };
        foreach (var argument in launch.Arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("The private fixture could not start.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly AtlasMyNetAppStore _store;
        public AtlasMyNetWebService Web { get; } = new();
        public AtlasMyNetProcessService Service { get; }

        public Fixture(string root, INetworkSocketInventoryService? inventory = null)
        {
            _root = root;
            Directory.CreateDirectory(root);
            _store = new AtlasMyNetAppStore(Path.Combine(root, "apps.json"), Path.Combine(root, "trust.json"),
                Path.Combine(root, "no-pinglab"), includeBuiltInHyperlib: false);
            Service = new AtlasMyNetProcessService(_store, inventory ?? new WindowsSocketInventoryService(), Web);
        }

        public AtlasMyNetAppDefinition CreateApp(string? stopMode = null, string id = "shutdown.fixture", string? root = null)
        {
            root ??= _root;
            Directory.CreateDirectory(root);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The verifier process path is unavailable.");
            var prefix = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? new List<string> { Assembly.GetExecutingAssembly().Location }
                : [];
            var app = new AtlasMyNetAppDefinition
            {
                Id = id,
                DisplayName = "Private shutdown fixture",
                Ports = [port],
                UrlTemplate = "http://127.0.0.1:{port}/",
                Launch = new AtlasMyNetLaunchDefinition
                {
                    Executable = executable,
                    WorkingDirectory = root,
                    Arguments = [.. prefix, FixtureSwitch, "serve", root, port.ToString()]
                },
                HealthCheck = new AtlasMyNetHealthCheckDefinition { TimeoutSeconds = 8 },
                Stop = stopMode is null ? null : new AtlasMyNetStopDefinition
                {
                    Mode = AtlasMyNetStopMode.Command,
                    Executable = executable,
                    WorkingDirectory = root,
                    Arguments = [.. prefix, FixtureSwitch, "stop", root, stopMode]
                }
            };
            _store.Trust(app);
            return app;
        }

        public void Dispose()
        {
            Service.Dispose();
            Web.Dispose();
        }
    }

    private sealed class ControlledInventory : INetworkSocketInventoryService
    {
        private readonly WindowsSocketInventoryService _actual = new();
        public Func<NetworkProcessIdentity, NetworkProcessIdentity>? Transform { get; set; }
        public bool BlockNextCapture { get; set; }
        public TaskCompletionSource CaptureEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseCapture { get; } = new(false);

        public IReadOnlyList<NetworkSocketSnapshot> Capture()
        {
            if (BlockNextCapture)
            {
                BlockNextCapture = false;
                CaptureEntered.TrySetResult();
                if (!ReleaseCapture.Wait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Pending-start fixture was not released.");
            }
            return _actual.Capture();
        }

        public bool TryGetProcessIdentity(int processId, out NetworkProcessIdentity identity)
        {
            if (!_actual.TryGetProcessIdentity(processId, out identity)) return false;
            if (Transform is not null) identity = Transform(identity);
            return true;
        }
    }
}
