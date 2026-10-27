using Hypershell.Models;
using Hypershell.Services;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

internal static class MyNetDesktopProcessVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "mynet-desktop-process");
        Directory.CreateDirectory(root);
        await VerifyOperationLeaseAsync(Path.Combine(root, "leases"));
        await VerifyConcurrentStartAsync(Path.Combine(root, "healthy"));
        await VerifyUnhealthyRestartGuardAsync(Path.Combine(root, "unhealthy"));
        Console.WriteLine("MYNET_DESKTOP_PROCESS_OK|operation-lease|cancellation|single-launch|unhealthy-restart-guard");
    }

    public static async Task RunFakeHostAsync(string[] args)
    {
        string? Option(string name)
        {
            var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        if (!int.TryParse(Option("--port"), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidDataException("The fake MyNet host requires --port with a valid local port.");
        }

        var responseCode = int.TryParse(Option("--response-code"), out var parsedCode) ? parsedCode : 200;
        if (Option("--count-file") is { } countPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(countPath))!);
            File.AppendAllText(countPath, Environment.ProcessId + Environment.NewLine);
        }

        await Task.Delay(500);
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await using var stream = client.GetStream();
                var request = new byte[4096];
                _ = await stream.ReadAsync(request, timeout.Token);
                const string body = "MyNet desktop verifier";
                var response = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 {responseCode} {(responseCode == 200 ? "OK" : "Service Unavailable")}\r\n"
                    + "Content-Type: text/plain\r\n"
                    + "X-MyNet-Verifier: true\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                    + "Connection: close\r\n\r\n" + body);
                await stream.WriteAsync(response, timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                // A health client can disconnect when its own bounded probe expires.
            }
        }
    }

    private static async Task VerifyOperationLeaseAsync(string root)
    {
        Directory.CreateDirectory(root);
        var store = CreateStore(root);
        using var web = new AtlasMyNetWebService();
        using var processes = new AtlasMyNetProcessService(store, new WindowsSocketInventoryService(), web);
        var app = new AtlasMyNetAppDefinition { Id = "verifier.operation-lease", DisplayName = "Lease verifier" };
        using var current = Process.GetCurrentProcess();
        var runtime = new AtlasMyNetAppRuntime(app.Id, Environment.ProcessId,
            Environment.ProcessPath ?? string.Empty, new DateTimeOffset(current.StartTime.ToUniversalTime()),
            1, "http://127.0.0.1:1/", false);
        var directory = Path.Combine(root, "operations");
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.Id.ToUpperInvariant()))) + ".lock");
        using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            var blocked = processes.RequestAppStopAsync(app, runtime, cancellation.Token);
            await Task.Delay(150);
            Require(!blocked.IsCompleted, "A lifecycle request must wait for an existing app operation lease.");
            var independent = app.Clone();
            independent.Id += ".independent";
            var separate = await processes.RequestAppStopAsync(independent, runtime);
            Require(separate.Outcome == AtlasMyNetStopOutcome.Rejected,
                "A lease for one app must not block another app's protected-process validation.");
            var cancelled = await blocked;
            Require(cancelled.Outcome == AtlasMyNetStopOutcome.Failed
                    && cancelled.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase),
                "Cancelling a stop request that is waiting for the app lease must return an explicit failed result.");
        }

        var afterRelease = await processes.RequestAppStopAsync(app, runtime);
        Require(afterRelease.Outcome == AtlasMyNetStopOutcome.Rejected,
            "A released operation lease must allow the next request to reach process validation.");

        var unavailableRoot = Path.Combine(root, "unavailable-operation-directory");
        Directory.CreateDirectory(unavailableRoot);
        File.WriteAllText(Path.Combine(unavailableRoot, "operations"), "A file prevents operation directory creation.");
        using var unavailableWeb = new AtlasMyNetWebService();
        using var unavailable = new AtlasMyNetProcessService(CreateStore(unavailableRoot),
            new WindowsSocketInventoryService(), unavailableWeb);
        var failed = await unavailable.RequestAppStopAsync(app, runtime);
        Require(failed.Outcome == AtlasMyNetStopOutcome.Failed
                && failed.Message.Contains("could not complete", StringComparison.OrdinalIgnoreCase),
            "An operation-directory I/O failure must return a failed stop result instead of escaping the UI caller.");
    }

    private static async Task VerifyConcurrentStartAsync(string root)
    {
        Directory.CreateDirectory(root);
        var firstStore = CreateStore(root);
        var secondStore = CreateStore(root);
        _ = firstStore.Load();
        _ = secondStore.Load();
        var app = CreateFakeApp("verifier.concurrent-start", root, responseCode: 200, timeoutSeconds: 5);
        firstStore.Trust(app);
        _ = secondStore.Load();
        using var firstWeb = new AtlasMyNetWebService();
        using var secondWeb = new AtlasMyNetWebService();
        using var first = new AtlasMyNetProcessService(firstStore, new WindowsSocketInventoryService(), firstWeb);
        using var second = new AtlasMyNetProcessService(secondStore, new WindowsSocketInventoryService(), secondWeb);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var starts = await Task.WhenAll(first.StartAsync(app, cancellation.Token), second.StartAsync(app, cancellation.Token));
            Require(starts[0].ProcessId == starts[1].ProcessId,
                "Two launchers starting the same local app must resolve to a single process.");
            Require(starts.Count(runtime => runtime.StartedThisSession) == 1,
                "Exactly one client must own the launched process; the second must discover it.");
            Require(File.ReadAllLines(Path.Combine(root, "launch-count.txt")).Length == 1,
                "Concurrent launcher clients must execute the app start command exactly once.");
        }
        finally
        {
            StopChildren(first, first.SessionRuntimes.Concat(second.SessionRuntimes));
        }
    }

    private static async Task VerifyUnhealthyRestartGuardAsync(string root)
    {
        Directory.CreateDirectory(root);
        var firstStore = CreateStore(root);
        var secondStore = CreateStore(root);
        _ = firstStore.Load();
        var app = CreateFakeApp("verifier.unhealthy-start", root, responseCode: 503, timeoutSeconds: 1);
        firstStore.Trust(app);
        _ = secondStore.Load();
        using var firstWeb = new AtlasMyNetWebService();
        using var secondWeb = new AtlasMyNetWebService();
        using var first = new AtlasMyNetProcessService(firstStore, new WindowsSocketInventoryService(), firstWeb);
        using var second = new AtlasMyNetProcessService(secondStore, new WindowsSocketInventoryService(), secondWeb);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        AtlasMyNetAppRuntime? child = null;
        try
        {
            await ExpectAsync<TimeoutException>(() => first.StartAsync(app, cancellation.Token),
                "A host responding with failed health must report a startup timeout.");
            child = first.SessionRuntimes.SingleOrDefault(runtime => runtime.AppId == app.Id)
                    ?? throw new InvalidOperationException("The failed-health host must retain its verified process identity.");
            first.Dispose();
            var error = await ExpectAsync<InvalidOperationException>(() => second.StartAsync(app, cancellation.Token),
                "A different launcher must refuse to start a second copy of an unhealthy running host.");
            Require(error.Message.Contains("still running", StringComparison.OrdinalIgnoreCase),
                "A persisted running identity must provide an explicit restart guard.");
            Require(File.ReadAllLines(Path.Combine(root, "launch-count.txt")).Length == 1,
                "A failed health check followed by a new launcher must not duplicate the start command.");
        }
        finally
        {
            var tracked = first.SessionRuntimes.Concat(second.SessionRuntimes).ToList();
            if (child is not null)
            {
                tracked.Add(child);
            }

            StopChildren(second, tracked);
        }
    }

    private static AtlasMyNetAppStore CreateStore(string root) => new(
        Path.Combine(root, "apps.json"), Path.Combine(root, "trust.json"), Path.Combine(root, "PingLab_Offline"));

    private static AtlasMyNetAppDefinition CreateFakeApp(string id, string root, int responseCode, int timeoutSeconds)
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("The verifier executable path is unavailable.");
        var arguments = new List<string>();
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add(Assembly.GetExecutingAssembly().Location);
        }

        arguments.AddRange(["--fake-mynet-host", "--port", "{port}", "--count-file",
            Path.Combine(root, "launch-count.txt"), "--response-code", responseCode.ToString(CultureInfo.InvariantCulture)]);
        var port = FindAvailablePort();
        return new AtlasMyNetAppDefinition
        {
            Id = id,
            DisplayName = "MyNet verifier host",
            UrlTemplate = "http://127.0.0.1:{port}/",
            Ports = [port],
            Launch = new AtlasMyNetLaunchDefinition { Executable = executable, Arguments = arguments },
            HealthCheck = new AtlasMyNetHealthCheckDefinition
            {
                Path = "/health", ExpectedStatusCode = 200,
                HeaderName = "X-MyNet-Verifier", HeaderValue = "true", TimeoutSeconds = timeoutSeconds
            },
            Stop = new AtlasMyNetStopDefinition { Mode = AtlasMyNetStopMode.ManagedProcess }
        };
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void StopChildren(AtlasMyNetProcessService service, IEnumerable<AtlasMyNetAppRuntime> runtimes)
    {
        foreach (var runtime in runtimes.DistinctBy(value => value.ProcessId).ToList())
        {
            var result = service.ForceStop(runtime.ProcessId, runtime.ExecutablePath, runtime.StartedAtUtc);
            Require(result.Outcome is AtlasMyNetStopOutcome.Stopped or AtlasMyNetStopOutcome.AlreadyStopped,
                $"The verifier child process could not be cleaned up: {result.Message}");
        }
    }

    private static async Task<TException> ExpectAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

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
