using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Diagnostics;

internal static class HyperlibSessionVerifier
{
    public static async Task VerifyAsync(string outputRoot)
    {
        var root = Path.Combine(outputRoot, "native-session");
        var source = Path.Combine(root, "fixture-source");
        var library = Path.Combine(root, "library");
        var appDirectory = Path.Combine(library, "app");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(appDirectory);
        await File.WriteAllTextAsync(Path.Combine(source, "HyperLib.Host.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(source, "Program.cs"), """
            using System.Net;
            using System.Net.Sockets;
            using System.Text;
            var port = int.Parse(args[Array.IndexOf(args, "--port") + 1]);
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync() is { Length: > 0 }) { }
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nX-HyperLib-App: hyperlib\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
                await stream.WriteAsync(response);
            }
            """);
        var buildInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "build", "HyperLib.Host.csproj", "-c", "Release", "-o", appDirectory, "--nologo" })
            buildInfo.ArgumentList.Add(argument);
        using (var build = Process.Start(buildInfo) ?? throw new InvalidOperationException("Could not build the isolated Host fixture."))
        {
            var stdout = build.StandardOutput.ReadToEndAsync();
            var stderr = build.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await build.WaitForExitAsync(deadline.Token); }
            catch { if (!build.HasExited) build.Kill(entireProcessTree: true); throw; }
            Require(build.ExitCode == 0, "Isolated Host fixture build failed: " + await stdout + await stderr);
        }

        var definition = HyperlibMyNetRegistration.Create(library) ?? throw new InvalidOperationException("Host fixture registration failed.");
        definition.AllowExternalResources = true;
        var store = new AtlasMyNetAppStore(Path.Combine(root, "apps.json"), Path.Combine(root, "trust.json"), includeBuiltInHyperlib: false);
        store.Trust(definition);
        var inventory = new WindowsSocketInventoryService();
        using var previousWeb = new AtlasMyNetWebService();
        using var previousSession = new AtlasMyNetProcessService(store, inventory, previousWeb);
        using var web = new AtlasMyNetWebService();
        using var processes = new AtlasMyNetProcessService(store, inventory, web);
        var ownedFixtures = new List<AtlasMyNetAppRuntime>();
        var checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var previous = await previousSession.StartAsync(definition, deadline.Token);
            ownedFixtures.Add(previous);
            var discovered = await processes.FindRunningAsync(definition, cancellationToken: deadline.Token);
            Check(discovered is { StartedThisSession: false } && discovered.ProcessId == previous.ProcessId,
                "Read-only discovery adopted an independent Hyperlib Host.");

            using var viewModel = new AtlasMyNetViewModel(store, inventory, web, processes);
            var item = new AtlasMyNetAppItemViewModel(definition, store);
            item.SetStatus(AtlasMyNetAppStatus.Running, "Running", discovered);
            AtlasMyNetOpenAppRequest? opened = null;
            viewModel.OpenAppRequested += request => opened = request;
            await viewModel.OpenAppAsync(item);
            ownedFixtures.AddRange(processes.SessionRuntimes);
            Check(item.Runtime is { StartedThisSession: true } && item.Runtime.ProcessId != previous.ProcessId,
                "Opening Hyperlib reused an independent Host without attaching a current native session: " + item.ErrorMessage);
            var current = item.Runtime!;
            Check(current.Port > previous.Port && opened?.Url == current.Url && opened.TrustedHyperlibRoot == library,
                "The new native tab did not receive its separately managed Host and trusted library root.");
            Check(viewModel.IsHyperlibSessionRunning(current.Url) && !viewModel.IsHyperlibSessionRunning(previous.Url),
                "Native session privilege leaked to the independent Host.");
            Check(await processes.FindRunningAsync(definition, cancellationToken: deadline.Token) == current,
                "Refresh replaced the current native session with an independent Host on an earlier port.");
            var repeated = await Task.WhenAll(processes.StartAsync(definition, deadline.Token), processes.StartAsync(definition, deadline.Token));
            Check(repeated.All(runtime => runtime == current) && processes.SessionRuntimes.Count == 1,
                "Repeated Open actions launched extra Hosts instead of reusing the current session.");
            definition.AllowExternalResources = false;
            Check(await processes.StartAsync(definition, deadline.Token) == current,
                "Changing Web access replaced the current native session.");
            definition.AllowExternalResources = true;
            Check(await previousWeb.CheckHealthAsync(definition, previous.Port, deadline.Token),
                "Opening the native session disturbed the independent Host.");

            var untrustedStore = new AtlasMyNetAppStore(Path.Combine(root, "untrusted-apps.json"), Path.Combine(root, "untrusted-trust.json"), includeBuiltInHyperlib: false);
            using var untrustedWeb = new AtlasMyNetWebService();
            using var untrustedProcesses = new AtlasMyNetProcessService(untrustedStore, inventory, untrustedWeb);
            try { await untrustedProcesses.StartAsync(definition, deadline.Token); throw new InvalidOperationException("An untrusted Hyperlib definition received a managed session."); }
            catch (UnauthorizedAccessException) { checks++; }
            Check(untrustedProcesses.SessionRuntimes.Count == 0, "An untrusted start created a native session before rejecting it.");
            using var untrustedView = new AtlasMyNetViewModel(untrustedStore, inventory, untrustedWeb, untrustedProcesses);
            var untrustedItem = new AtlasMyNetAppItemViewModel(definition.Clone(), untrustedStore);
            untrustedItem.SetStatus(AtlasMyNetAppStatus.Running, "Running", discovered);
            AtlasMyNetOpenAppRequest? untrustedOpen = null;
            untrustedView.OpenAppRequested += request => untrustedOpen = request;
            await untrustedView.OpenAppAsync(untrustedItem);
            Check(untrustedOpen is { TrustedHyperlibRoot: null } && untrustedOpen.Url == previous.Url,
                "An untrusted discovered tab received native bridge privilege.");

            var ordinary = definition.Clone();
            ordinary.Id = "verifier.ordinary-app";
            ordinary.Ports = [previous.Port];
            var ordinaryRuntime = await untrustedProcesses.StartAsync(ordinary, deadline.Token);
            Check(!ordinaryRuntime.StartedThisSession && ordinaryRuntime.ProcessId == previous.ProcessId,
                "Ordinary MyNet apps no longer reuse their discovered endpoint.");
            Console.WriteLine($"HYPERLIB_SESSION_OK|checks:{checks}|independent-host-preserved|native-open-after-restart|managed-port-priority|no-external-bridge|ordinary-app-unchanged");
        }
        finally
        {
            // Only terminate the exact temporary fixture executables created by this verifier.
            foreach (var runtime in ownedFixtures.Concat(processes.SessionRuntimes).Concat(previousSession.SessionRuntimes).DistinctBy(runtime => runtime.ProcessId))
            {
                if (!inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity)) continue;
                Require(string.Equals(identity.ExecutablePath, Path.Combine(appDirectory, "HyperLib.Host.exe"), StringComparison.OrdinalIgnoreCase)
                    && identity.StartTimeUtc.HasValue && Math.Abs((identity.StartTimeUtc.Value - runtime.StartedAtUtc).TotalSeconds) <= 1,
                    "The Host fixture PID identity changed; cleanup refused to stop it.");
                using var process = Process.GetProcessById(runtime.ProcessId);
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
