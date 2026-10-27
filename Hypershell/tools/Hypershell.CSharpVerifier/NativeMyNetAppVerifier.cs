using Hypershell.Models;
using Hypershell.Services;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

internal static class NativeMyNetAppVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "native-mynet");
        Directory.CreateDirectory(root);
        var store = CreateStore(root);
        var firstApp = CreateApp(root, "studio", cancelClose: true);
        var secondApp = CreateApp(root, "editor", cancelClose: false);
        store.Export(Path.Combine(root, "native.json"), [firstApp, secondApp]);
        var imported = store.ReadImport(Path.Combine(root, "native.json"));
        Require(imported.Count == 2 && imported.All(app => app.IsNative && app.Ports.Count == 0 && app.UrlTemplate.Length == 0),
            "Native manifests must round-trip without URL or port requirements.");
        Require(firstApp.Clone().IsNative, "Cloning must preserve native app kind.");

        var invalid = firstApp.Clone();
        invalid.Launch = null;
        Expect<InvalidDataException>(() => store.Export(Path.Combine(root, "invalid.json"), [invalid]), "A native app needs a launch command.");
        invalid = firstApp.Clone();
        invalid.Ports = [5123];
        Expect<InvalidDataException>(() => store.Export(Path.Combine(root, "invalid.json"), [invalid]), "Native apps cannot carry web ports.");
        invalid = firstApp.Clone();
        invalid.HealthCheck = new();
        Expect<InvalidDataException>(() => store.Export(Path.Combine(root, "invalid.json"), [invalid]), "Native apps cannot carry HTTP health checks.");
        invalid = firstApp.Clone();
        invalid.IsNative = false;
        invalid.UrlTemplate = "https://example.com/";
        Expect<InvalidDataException>(() => store.Export(Path.Combine(root, "invalid.json"), [invalid]), "Remote web apps must still be rejected.");
        VerifyExistingWebTrust(root, firstApp);
        await VerifySharedRegistryAsync(Path.Combine(root, "shared-registry"), firstApp, secondApp);

        using var web = new AtlasMyNetWebService();
        using var first = new AtlasMyNetProcessService(store, new WindowsSocketInventoryService(), web);
        using var fresh = new AtlasMyNetProcessService(store, new WindowsSocketInventoryService(), web);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var owned = new List<AtlasMyNetAppRuntime>();
        try
        {
            await ExpectAsync<UnauthorizedAccessException>(() => first.StartAsync(firstApp, cancellation.Token),
                "Native commands require explicit trust before execution.");
            store.Trust(firstApp);
            store.Trust(secondApp);
            var changed = firstApp.Clone();
            changed.IsNative = false;
            Require(!store.IsTrusted(changed), "Changing native command kind must invalidate trust.");
            changed = firstApp.Clone();
            changed.Launch!.Arguments.Add("changed");
            Require(!store.IsTrusted(changed), "Changing native arguments must invalidate trust.");

            var starts = await Task.WhenAll(first.StartAsync(firstApp, cancellation.Token), fresh.StartAsync(firstApp, cancellation.Token));
            owned.AddRange(starts);
            Require(starts[0].ProcessId == starts[1].ProcessId, "Concurrent native Open must launch only one process.");
            Require(starts.All(runtime => runtime.Port == 0 && runtime.Url.Length == 0), "Native runtimes do not have browser endpoints.");
            var editor = await first.StartAsync(secondApp, cancellation.Token);
            owned.Add(editor);
            Require(editor.ProcessId != starts[0].ProcessId, "Two modes of the same executable must have separate identities.");
            Require((await fresh.FindRunningAsync(firstApp))?.ProcessId == starts[0].ProcessId
                && (await fresh.FindRunningAsync(secondApp))?.ProcessId == editor.ProcessId,
                "A fresh launcher must discover the right native mode without socket listeners.");
            var reopen = await fresh.StartAsync(firstApp, cancellation.Token);
            Require(reopen.ProcessId == starts[0].ProcessId, "Reopening must activate the existing native app.");
            Require(File.ReadAllLines(Path.Combine(root, "studio.launches")).Length == 1,
                "Activation must not relaunch an existing native process.");

            var cancelled = await first.RequestAppStopAsync(firstApp, starts[0], cancellation.Token);
            Require(cancelled.Outcome == AtlasMyNetStopOutcome.NeedsForce
                && (await first.FindRunningAsync(firstApp)) is not null,
                "A cancelled native close must leave the process alive and must never force-stop it.");

            // The authoring host has a shutdown method; verify its automatic web-host
            // force cleanup cannot override a native editor's save/cancel response.
            var shutdown = typeof(AtlasMyNetProcessService).GetMethod("StopSessionAppsForShutdownAsync");
            if (shutdown is not null)
            {
                var definitions = new Dictionary<string, AtlasMyNetAppDefinition> { [firstApp.Id] = firstApp, [secondApp.Id] = secondApp };
                var shutdownTask = (Task<IReadOnlyList<AtlasMyNetStopResult>>)shutdown.Invoke(first, [definitions, cancellation.Token])!;
                _ = await shutdownTask;
                Require((await first.FindRunningAsync(firstApp)) is not null,
                    "Authoring-shell shutdown must preserve a native editor that cancels closing.");
                typeof(AtlasMyNetProcessService).GetMethod("CancelShutdown")!.Invoke(first, null);
            }

            File.WriteAllText(Path.Combine(root, "studio.allow-close"), "saved");
            var stopped = await first.RequestAppStopAsync(firstApp, starts[0], cancellation.Token);
            Require(stopped.Outcome is AtlasMyNetStopOutcome.Stopped or AtlasMyNetStopOutcome.AlreadyStopped,
                "A native app accepting CloseMainWindow must stop gracefully.");
            Require(await fresh.FindRunningAsync(firstApp) is null, "Closed native apps must disappear from discovery.");
        }
        finally
        {
            foreach (var runtime in owned.Concat(first.SessionRuntimes).Concat(fresh.SessionRuntimes).DistinctBy(runtime => runtime.ProcessId))
                _ = first.ForceStop(runtime.ProcessId, runtime.ExecutablePath, runtime.StartedAtUtc);
        }
        Console.WriteLine("NATIVE_MYNET_OK|manifest|shared-registry|web-trust-compatible|native-trust|distinct-modes|concurrent-open|activation|cancel-close|graceful-close");
    }

    public static void RunFakeWindow(string[] args)
    {
        string Option(string name) => args[Array.IndexOf(args, name) + 1];
        var root = Option("--native-root");
        var identity = Option("--native-identity");
        File.AppendAllText(Path.Combine(root, identity + ".launches"), Environment.ProcessId + Environment.NewLine);
        var thread = new Thread(() =>
        {
            var application = new Application();
            var window = new Window { Title = "MyNet native verifier " + identity, Width = 280, Height = 100 };
            window.Closing += (_, eventArgs) => eventArgs.Cancel = args.Contains("--cancel-close")
                && !File.Exists(Path.Combine(root, identity + ".allow-close"));
            application.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static AtlasMyNetAppStore CreateStore(string root)
    {
        var constructor = typeof(AtlasMyNetAppStore).GetConstructor([typeof(string), typeof(string), typeof(string), typeof(bool)]);
        if (constructor is not null)
            return (AtlasMyNetAppStore)constructor.Invoke([Path.Combine(root, "apps.json"), Path.Combine(root, "trust.json"), Path.Combine(root, "no-builtins"), false]);
        return new AtlasMyNetAppStore(Path.Combine(root, "apps.json"), Path.Combine(root, "trust.json"), Path.Combine(root, "no-builtins"));
    }

    private static AtlasMyNetAppDefinition CreateApp(string root, string identity, bool cancelClose)
    {
        var executable = Environment.ProcessPath!;
        var arguments = new List<string>();
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(Assembly.GetExecutingAssembly().Location);
        arguments.AddRange(["--fake-native-mynet-window", "--native-root", root, "--native-identity", identity]);
        if (cancelClose) arguments.Add("--cancel-close");
        return new AtlasMyNetAppDefinition
        {
            Id = "verifier.native." + identity, DisplayName = "Native " + identity, IsNative = true, UrlTemplate = "",
            Launch = new AtlasMyNetLaunchDefinition { Executable = executable, Arguments = arguments },
            Stop = new AtlasMyNetStopDefinition { Mode = AtlasMyNetStopMode.ManagedProcess }
        };
    }

    private static void VerifyExistingWebTrust(string root, AtlasMyNetAppDefinition source)
    {
        var app = source.Clone();
        app.IsNative = false;
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var oldPayload = JsonSerializer.Serialize(new { app.Id, app.Launch, app.Stop }, options);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldPayload)));
        var oldRoot = Path.Combine(root, "legacy-trust");
        Directory.CreateDirectory(oldRoot);
        File.WriteAllText(Path.Combine(oldRoot, "trust.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, Fingerprints = new[] { fingerprint } }));
        var store = CreateStore(oldRoot);
        _ = store.Load();
        Require(store.IsTrusted(app), "Existing web command approvals must remain valid after adding native support.");
    }

    private static async Task VerifySharedRegistryAsync(string root, AtlasMyNetAppDefinition studio, AtlasMyNetAppDefinition editor)
    {
        Directory.CreateDirectory(root);
        var first = CreateStore(root);
        var second = CreateStore(root);
        _ = first.Load();
        _ = second.Load();
        var retained = studio.Clone();
        retained.Id = "verifier.native.retained-toolbar";
        retained.ShowNavigationToolbar = false;
        first.UpdateDefinitions(apps => apps.Add(retained));
        await Task.WhenAll(
            Task.Run(() => first.UpdateDefinitions(apps => apps.Add(studio.Clone()))),
            Task.Run(() => second.UpdateDefinitions(apps => apps.Add(editor.Clone()))));
        await Task.WhenAll(Task.Run(() => first.Trust(studio)), Task.Run(() => second.Trust(editor)));
        var latest = first.Load();
        Require(latest.Any(app => app.Id == studio.Id) && latest.Any(app => app.Id == editor.Id)
            && !latest.Single(app => app.Id == retained.Id).ShowNavigationToolbar,
            "Independent editors must merge registry updates and retain existing toolbar preferences.");
        Require(first.IsTrusted(studio) && first.IsTrusted(editor),
            "Independent editors must merge command trust without dropping each other's approvals.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException(message); }
    private static async Task ExpectAsync<T>(Func<Task> action, string message) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException(message); }
}
