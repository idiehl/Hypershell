using Hypershell.Models;
using Hypershell.Services;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

internal static class VisualPageHostingVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var projectRoot = Path.Combine(verifierRoot, "host-project");
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projectService = new VisualPageProjectService(
            validator,
            generator,
            catalog);
        var session = await projectService.CreateAsync(
            projectRoot,
            "Hosted Verifier",
            "Hosted.Verifier");
        using var preview = new VisualPagePreviewBuildService(
            new BoundedProcessRunner(),
            generator,
            Path.Combine(verifierRoot, "preview-cache"));

        var invalidSourcePath =
            $"src/Pages/Generated/{session.Page.ComponentName}.razor.cs";
        var failed = await preview.BuildLatestAsync(
            session,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [invalidSourcePath] = "namespace Hosted.Verifier.Generated; public partial class HomePage { this is not valid C#; }"
            });
        Require(failed.Outcome == VisualPageBuildOutcome.Failed
                && failed.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == VisualPageDiagnosticSeverity.Error),
            "The isolated preview build did not return compiler diagnostics for unsaved source overrides.");

        var replacedBuild = preview.BuildLatestAsync(session);
        await Task.Delay(25);
        var latestBuild = preview.BuildLatestAsync(session);
        var first = await replacedBuild;
        var build = await latestBuild;
        Require(first.Outcome == VisualPageBuildOutcome.Cancelled
                && build.Outcome == VisualPageBuildOutcome.Succeeded
                && Directory.Exists(build.SnapshotPath)
                && string.Equals(
                    build.Revision,
                    session.Page.Revision,
                    StringComparison.Ordinal),
            $"Latest-build-wins cancellation or the successful isolated snapshot build failed. First={first.Outcome}; latest={build.Outcome}; output={build.Output}");

        using var host = new VisualPageLocalHostService(
            new BoundedProcessRunner());
        await ExpectValidationAsync(
            () => host.StartAsync(
                build,
                new VisualPageHostRequest
                {
                    AdapterAddress = "127.0.0.1",
                    Port = 0,
                    TrustCustomCode = false
                }),
            "Local hosting started without the explicit ambient-code trust acknowledgement.");
        await ExpectValidationAsync(
            () => host.StartAsync(
                build,
                new VisualPageHostRequest
                {
                    AdapterAddress = "192.0.2.25",
                    Port = 45025,
                    AllowLan = false,
                    TrustCustomCode = true
                }),
            "A non-loopback bind was accepted without the explicit LAN switch.");

        using (var collision = new TcpListener(IPAddress.Loopback, 0))
        {
            collision.Start();
            var collisionPort = ((IPEndPoint)collision.LocalEndpoint).Port;
            await ExpectValidationAsync(
                () => host.StartAsync(
                    build,
                    new VisualPageHostRequest
                    {
                        AdapterAddress = "127.0.0.1",
                        Port = collisionPort,
                        TrustCustomCode = true
                    }),
                "A loopback port collision was not rejected before process launch.");
        }

        var lanAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus
                              == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address));
        if (lanAddress is not null)
        {
            using var collision = new TcpListener(lanAddress, 0);
            collision.Start();
            var collisionPort = ((IPEndPoint)collision.LocalEndpoint).Port;
            await ExpectValidationAsync(
                () => host.StartAsync(
                    build,
                    new VisualPageHostRequest
                    {
                        AdapterAddress = lanAddress.ToString(),
                        Port = collisionPort,
                        AllowLan = true,
                        TrustCustomCode = true
                    }),
                "Explicit active-adapter LAN validation did not reach the bounded port-collision guard.");
        }

        var runtime = await host.StartAsync(
            build,
            new VisualPageHostRequest
            {
                AdapterAddress = "127.0.0.1",
                Port = 0,
                AllowLan = false,
                TrustCustomCode = true
            });
        using (var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        })
        {
            var health = await client.GetStringAsync(runtime.Url + "healthz");
            Require(health.Contains("healthy", StringComparison.OrdinalIgnoreCase),
                "The managed loopback host did not expose its exact health contract.");
        }
        Require(runtime.Url.StartsWith(
                    "http://127.0.0.1:",
                    StringComparison.Ordinal)
                && !runtime.IsLanExposed
                && host.Runtime?.ProcessId == runtime.ProcessId,
            "Default local hosting was not confined to the managed loopback process.");

        var definition = host.CreateAtlasMyNetDefinition(session, runtime);
        Require(definition.Id == $"visual-page.{session.Project.ProjectId}"
                && definition.UrlTemplate == "http://127.0.0.1:{port}/"
                && definition.Ports.SequenceEqual([runtime.Port])
                && definition.Launch is
                {
                    Arguments.Count: 3,
                    WorkingDirectory: not null
                }
                && definition.Launch.Arguments[2]
                    == "http://127.0.0.1:{port}"
                && definition.HealthCheck is { Path: "/healthz" }
                && definition.Stop is
                {
                    Mode: AtlasMyNetStopMode.ManagedProcess
                },
            "Atlas MyNet registration lost the exact launch, health, port, or managed-stop contract.");
        var store = new AtlasMyNetAppStore(
            Path.Combine(verifierRoot, "atlas-mynet", "apps.json"),
            Path.Combine(verifierRoot, "atlas-mynet", "trust.json"),
            projectRoot);
        host.RegisterWithAtlasMyNet(store, definition);
        Require(store.Load().Any(app =>
                app.Id == definition.Id
                && app.UrlTemplate == definition.UrlTemplate),
            "The generated local host was not registered with Atlas MyNet.");

        Require(typeof(VisualPageLocalHostService)
                    .GetMethods()
                    .All(method => !method.Name.Contains(
                        "Firewall",
                        StringComparison.OrdinalIgnoreCase)),
            "Local hosting unexpectedly exposed an automatic firewall action.");

        var processId = runtime.ProcessId;
        await host.StopAsync();
        Require(host.Runtime is null && !IsProcessRunning(processId),
            "The managed local host or its process tree survived Stop.");

        Console.WriteLine(
            "VISUAL_PAGE_HOSTING_OK|dotnet10-build|diagnostic-map|latest-build-wins|explicit-trust|loopback-default|port-collision|explicit-lan-adapter|health|atlas-mynet|managed-stop|no-firewall");
    }

    private static async Task ExpectValidationAsync(
        Func<Task<VisualPageHostRuntime>> action,
        string message)
    {
        try
        {
            _ = await action();
        }
        catch (VisualPageValidationException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
