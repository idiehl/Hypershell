using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal static class AtlasMyNetVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "atlas-mynet");
        Directory.CreateDirectory(root);

        var appsPath = Path.Combine(root, "apps.json");
        var trustPath = Path.Combine(root, "trust.json");
        var pingLabRoot = Path.Combine(root, "PingLab_Offline");
        var pingLabAppDirectory = Path.Combine(pingLabRoot, "app");
        Directory.CreateDirectory(pingLabAppDirectory);
        var pingLabExecutable = Path.Combine(
            pingLabAppDirectory,
            "PingLab.OfflineHost.exe");
        File.WriteAllBytes(pingLabExecutable, []);
        var store = new AtlasMyNetAppStore(
            appsPath,
            trustPath,
            pingLabRoot);
        VerifyRegistryAndTrust(store, root, pingLabExecutable);
        await VerifyAddressPolicyAsync();
        await VerifySocketInventoryAsync();
        await VerifyWebDetectionAndProcessProtectionAsync(store);
        await VerifyTcpClientAsync();
        await VerifyUdpClientAsync();
        await NetworkObservatoryVerifier.VerifyAsync(root);

        Console.WriteLine("ATLAS_MYNET_OK|registry|trust|inventory|web|process-safety|tcp|udp|observatory");
    }

    private static void VerifyRegistryAndTrust(
        AtlasMyNetAppStore store,
        string root,
        string pingLabExecutable)
    {
        var definitions = store.Load();
        var pingLab = definitions.Single(definition => string.Equals(
            definition.Id,
            AtlasMyNetAppStore.PingLabAppId,
            StringComparison.OrdinalIgnoreCase));
        Require(
            pingLab.Ports.SequenceEqual(Enumerable.Range(5197, 10)),
            "Ping Lab must reserve ports 5197 through 5206.");
        Require(
            string.Equals(
                pingLab.Launch?.Executable,
                pingLabExecutable,
                StringComparison.OrdinalIgnoreCase),
            "Ping Lab must discover the configured offline host executable.");
        Require(
            string.Equals(
                pingLab.Launch?.WorkingDirectory,
                Path.GetDirectoryName(pingLabExecutable),
                StringComparison.OrdinalIgnoreCase),
            "Ping Lab must launch from its discovered package directory.");
        Require(
            pingLab.Launch?.Arguments.SequenceEqual(
                ["--urls", "http://127.0.0.1:{port}"]) == true,
            "Ping Lab launch arguments changed.");
        Require(
            pingLab.HealthCheck is
            {
                Path: "/__pinglab_offline_status",
                HeaderName: "X-PingLab-Offline",
                HeaderValue: "true"
            },
            "Ping Lab must use the offline-only health contract.");
        Require(store.IsTrusted(pingLab), "The built-in Ping Lab command must be trusted.");

        var stalePingLab = pingLab.Clone();
        stalePingLab.Launch!.Executable = Path.Combine(
            root,
            "retired-location",
            "app",
            "PingLab.OfflineHost.exe");
        stalePingLab.Launch.WorkingDirectory = Path.GetDirectoryName(
            stalePingLab.Launch.Executable)!;
        store.Save([stalePingLab]);
        store.Trust(stalePingLab);
        pingLab = store.Load().Single();
        Require(
            string.Equals(
                pingLab.Launch?.Executable,
                pingLabExecutable,
                StringComparison.OrdinalIgnoreCase)
            && store.IsTrusted(pingLab),
            "A missing built-in Ping Lab location was not migrated and retrusted.");

        var custom = new AtlasMyNetAppDefinition
        {
            Id = "verifier.local-app",
            DisplayName = "Verifier local app",
            UrlTemplate = "http://127.0.0.1:{port}/",
            Ports = [61231],
            Launch = new AtlasMyNetLaunchDefinition
            {
                Executable = Environment.ProcessPath
                             ?? throw new InvalidOperationException(
                                 "The verifier executable path is unavailable."),
                Arguments = ["--verifier-child", "{port}"],
                WorkingDirectory = root,
                Environment = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["ATLAS_MYNET_PORT"] = "{port}"
                }
            },
            Stop = new AtlasMyNetStopDefinition
            {
                Mode = AtlasMyNetStopMode.Command,
                Executable = Environment.ProcessPath!,
                Arguments = ["--verifier-stop", "{port}"],
                WorkingDirectory = root
            }
        };
        store.Save([pingLab, custom]);
        Require(!store.IsTrusted(custom), "New command definitions must start untrusted.");
        store.Trust(custom);
        Require(store.IsTrusted(custom), "Explicit trust was not persisted in memory.");
        var review = AtlasMyNetAppStore.CreateCommandReviewText(custom);
        Require(
            review.Contains("Executable:", StringComparison.Ordinal)
            && review.Contains("Arguments:", StringComparison.Ordinal)
            && review.Contains("Working directory:", StringComparison.Ordinal)
            && review.Contains("Environment:", StringComparison.Ordinal)
            && review.Contains("STOP", StringComparison.Ordinal),
            "The command review must expose start, stop, arguments, directory, and environment.");

        var changed = custom.Clone();
        changed.Launch!.Arguments.Add("changed");
        Require(
            !store.IsTrusted(changed),
            "Changing a command must invalidate its prior trust fingerprint.");

        var remote = custom.Clone();
        remote.Id = "verifier.remote-app";
        remote.UrlTemplate = "https://example.com/";
        remote.Ports = [];
        ExpectInvalidData(
            () => AtlasMyNetAppStore.ValidateDefinition(remote),
            "Remote URLs must not be accepted as local app definitions.");
        var relativeCommand = custom.Clone();
        relativeCommand.Id = "verifier.relative-command";
        relativeCommand.Launch!.Executable = "relative.exe";
        ExpectInvalidData(
            () => AtlasMyNetAppStore.ValidateDefinition(relativeCommand),
            "Imported commands must use reviewable absolute executable paths.");

        var exportPath = Path.Combine(root, "export.json");
        store.Export(exportPath, [custom]);
        var exportedJson = File.ReadAllText(exportPath);
        Require(
            !exportedJson.Contains("fingerprint", StringComparison.OrdinalIgnoreCase)
            && !exportedJson.Contains("trusted", StringComparison.OrdinalIgnoreCase),
            "Exported app JSON must not carry local trust decisions.");
        var importedStore = new AtlasMyNetAppStore(
            Path.Combine(root, "imported-apps.json"),
            Path.Combine(root, "imported-trust.json"));
        var imported = importedStore.ReadImport(exportPath).Single();
        Require(
            !importedStore.IsTrusted(imported),
            "Imported commands must remain disabled until explicitly trusted.");
    }

    private static Task VerifyAddressPolicyAsync()
    {
        Require(
            NetworkAddressPolicy.IsLocalMachineUri(
                new Uri("http://127.0.0.1:5197/")),
            "Loopback URLs must be treated as local.");
        Require(
            !NetworkAddressPolicy.IsPublicAddress(IPAddress.Parse("10.1.2.3"))
            && !NetworkAddressPolicy.IsPublicAddress(IPAddress.Parse("192.168.1.7"))
            && !NetworkAddressPolicy.IsPublicAddress(IPAddress.IPv6Loopback),
            "Private and loopback destinations must not be classified as public.");
        Require(
            NetworkAddressPolicy.IsPublicAddress(IPAddress.Parse("8.8.8.8"))
            && NetworkAddressPolicy.IsPublicAddress(
                IPAddress.Parse("2606:4700:4700::1111")),
            "Public IPv4 and IPv6 addresses must require confirmation.");
        return Task.CompletedTask;
    }

    private static async Task VerifySocketInventoryAsync()
    {
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var tcp6Listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        tcp6Listener.Start();
        using var udpListener = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var udp6Listener = new UdpClient(
            new IPEndPoint(IPAddress.IPv6Loopback, 0));
        try
        {
            var tcpPort = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            var tcp6Port = ((IPEndPoint)tcp6Listener.LocalEndpoint).Port;
            var udpPort = ((IPEndPoint)udpListener.Client.LocalEndPoint!).Port;
            var udp6Port = ((IPEndPoint)udp6Listener.Client.LocalEndPoint!).Port;
            var inventory = new WindowsSocketInventoryService();
            IReadOnlyList<NetworkSocketSnapshot> sockets = [];
            for (var attempt = 0; attempt < 10; attempt++)
            {
                sockets = inventory.Capture();
                if (sockets.Any(socket =>
                        socket.Transport == NetworkTransport.Tcp
                        && socket.AddressFamily == AddressFamily.InterNetwork
                        && socket.IsListener
                        && socket.LocalPort == tcpPort)
                    && sockets.Any(socket =>
                        socket.Transport == NetworkTransport.Udp
                        && socket.AddressFamily == AddressFamily.InterNetwork
                        && socket.LocalPort == udpPort)
                    && sockets.Any(socket =>
                        socket.Transport == NetworkTransport.Tcp
                        && socket.AddressFamily == AddressFamily.InterNetworkV6
                        && socket.IsListener
                        && socket.LocalPort == tcp6Port)
                    && sockets.Any(socket =>
                        socket.Transport == NetworkTransport.Udp
                        && socket.AddressFamily == AddressFamily.InterNetworkV6
                        && socket.LocalPort == udp6Port))
                {
                    break;
                }

                await Task.Delay(50);
            }

            var tcp = sockets.SingleOrDefault(socket =>
                socket.Transport == NetworkTransport.Tcp
                && socket.AddressFamily == AddressFamily.InterNetwork
                && socket.IsListener
                && socket.LocalPort == tcpPort);
            var udp = sockets.SingleOrDefault(socket =>
                socket.Transport == NetworkTransport.Udp
                && socket.AddressFamily == AddressFamily.InterNetwork
                && socket.LocalPort == udpPort);
            var tcp6 = sockets.SingleOrDefault(socket =>
                socket.Transport == NetworkTransport.Tcp
                && socket.AddressFamily == AddressFamily.InterNetworkV6
                && socket.IsListener
                && socket.LocalPort == tcp6Port);
            var udp6 = sockets.SingleOrDefault(socket =>
                socket.Transport == NetworkTransport.Udp
                && socket.AddressFamily == AddressFamily.InterNetworkV6
                && socket.LocalPort == udp6Port);
            var tcpRow = tcp ?? throw new InvalidOperationException(
                "The IPv4 TCP listener was not discovered.");
            var udpRow = udp ?? throw new InvalidOperationException(
                "The IPv4 UDP endpoint was not discovered.");
            var tcp6Row = tcp6 ?? throw new InvalidOperationException(
                "The IPv6 TCP listener was not discovered.");
            var udp6Row = udp6 ?? throw new InvalidOperationException(
                "The IPv6 UDP endpoint was not discovered.");
            Require(
                tcpRow.ProcessId == Environment.ProcessId
                && udpRow.ProcessId == Environment.ProcessId
                && tcp6Row.ProcessId == Environment.ProcessId
                && udp6Row.ProcessId == Environment.ProcessId,
                "Socket rows must identify the owning PID.");
            Require(
                tcpRow.IsCurrentUserOwned
                && udpRow.IsCurrentUserOwned
                && tcp6Row.IsCurrentUserOwned
                && udp6Row.IsCurrentUserOwned,
                "The verifier's sockets must be recognized as current-user owned.");
            Require(
                inventory.TryGetProcessIdentity(Environment.ProcessId, out var identity)
                && identity.IsCurrentUserOwned
                && identity.StartTimeUtc.HasValue,
                "Current-process identity must include ownership and start time.");
        }
        finally
        {
            tcpListener.Stop();
            tcp6Listener.Stop();
        }
    }

    private static async Task VerifyWebDetectionAndProcessProtectionAsync(
        AtlasMyNetAppStore store)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var serverCancellation = new CancellationTokenSource();
        var server = ServeHttpAsync(listener, serverCancellation.Token);
        using var web = new AtlasMyNetWebService();
        var inventory = new WindowsSocketInventoryService();
        using var processes = new AtlasMyNetProcessService(store, inventory, web);
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var socket = new NetworkSocketSnapshot(
                $"verifier:{port}",
                NetworkTransport.Tcp,
                AddressFamily.InterNetwork,
                "Listen",
                "127.0.0.1",
                port,
                "0.0.0.0",
                0,
                Environment.ProcessId,
                "Hypershell.CSharpVerifier",
                Environment.ProcessPath ?? string.Empty,
                Environment.UserName,
                string.Empty,
                IsCurrentUserOwned: true,
                ProcessStartTimeUtc: DateTimeOffset.UtcNow);
            var discovered = await web.ProbeListenerAsync(socket)
                             ?? throw new InvalidOperationException(
                                 "The bounded local HTTP probe found no app.");
            Require(
                discovered.DisplayName == "Atlas MyNet Verifier"
                && discovered.StatusCode == 200
                && discovered.Url == $"http://127.0.0.1:{port}/",
                "The local HTTP probe returned incorrect metadata.");

            var definition = new AtlasMyNetAppDefinition
            {
                Id = "verifier.health",
                DisplayName = "Verifier health",
                UrlTemplate = "http://127.0.0.1:{port}/",
                Ports = [port],
                HealthCheck = new AtlasMyNetHealthCheckDefinition
                {
                    Path = "/health",
                    ExpectedStatusCode = 200,
                    HeaderName = "X-PingLab-Offline",
                    HeaderValue = "true"
                }
            };
            Require(
                await web.CheckHealthAsync(definition, port),
                "The exact status/header health contract was not accepted.");

            var rejectSelf = processes.ForceStop(
                Environment.ProcessId,
                Environment.ProcessPath ?? string.Empty,
                null);
            var rejectSystem = processes.ForceStop(4, string.Empty, null);
            Require(
                rejectSelf.Outcome == AtlasMyNetStopOutcome.Rejected
                && rejectSystem.Outcome == AtlasMyNetStopOutcome.Rejected,
                "Atlas MyNet must reject self and protected-system process stops.");
        }
        finally
        {
            serverCancellation.Cancel();
            listener.Stop();
            await IgnoreExpectedCancellationAsync(server);
        }
    }

    private static async Task VerifyTcpClientAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = EchoTcpAsync(listener, cancellation.Token);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new NetworkClientTabViewModel(
            new NetworkClientLaunchRequest(
                NetworkTransport.Tcp,
                "127.0.0.1",
                port,
                AutoConnect: false));
        try
        {
            Require(
                !await client.RequiresPublicConfirmationAsync(cancellation.Token),
                "Loopback TCP must not require public-network confirmation.");
            await client.ConnectAsync(cancellation.Token);
            Require(client.IsConnected, $"TCP client failed: {client.StatusMessage}");
            client.LineEnding = NetworkLineEnding.None;
            client.InputText = "atlas tcp";
            await client.SendAsync(cancellation.Token);
            await WaitUntilAsync(
                () => client.Transcript.Any(entry =>
                    entry.Direction == "IN"
                    && entry.Text.Contains("atlas tcp", StringComparison.Ordinal)),
                cancellation.Token);

            client.PayloadFormat = NetworkPayloadFormat.Hex;
            client.InputText = "DE AD BE EF";
            await client.SendAsync(cancellation.Token);
            await WaitUntilAsync(
                () => client.Transcript.Any(entry =>
                    entry.Direction == "IN"
                    && entry.Text.Contains("DE AD BE EF", StringComparison.Ordinal)),
                cancellation.Token);
            Require(
                client.CreateTranscriptText().Contains("OUT", StringComparison.Ordinal),
                "TCP transcripts must be saveable as text.");
        }
        finally
        {
            client.Disconnect();
            listener.Stop();
            cancellation.Cancel();
            await IgnoreExpectedCancellationAsync(server);
        }
    }

    private static async Task VerifyUdpClientAsync()
    {
        using var udpServer = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = EchoUdpAsync(udpServer, cancellation.Token);
        var port = ((IPEndPoint)udpServer.Client.LocalEndPoint!).Port;
        using var client = new NetworkClientTabViewModel(
            new NetworkClientLaunchRequest(
                NetworkTransport.Udp,
                "127.0.0.1",
                port,
                AutoConnect: false));
        try
        {
            await client.ConnectAsync(cancellation.Token);
            Require(client.IsConnected, $"UDP client failed: {client.StatusMessage}");
            client.LineEnding = NetworkLineEnding.None;
            client.InputText = "atlas udp";
            await client.SendAsync(cancellation.Token);
            await WaitUntilAsync(
                () => client.Transcript.Any(entry =>
                    entry.Direction.StartsWith("IN ", StringComparison.Ordinal)
                    && entry.Text.Contains("atlas udp", StringComparison.Ordinal)),
                cancellation.Token);
        }
        finally
        {
            client.Disconnect();
            cancellation.Cancel();
            await IgnoreExpectedCancellationAsync(server);
        }
    }

    private static async Task ServeHttpAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(
                    cancellationToken);
                await using var stream = client.GetStream();
                var request = new byte[4096];
                _ = await stream.ReadAsync(request, cancellationToken);
                const string body =
                    "<!doctype html><title>Atlas MyNet Verifier</title><p>local</p>";
                var response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: text/html; charset=utf-8\r\n"
                    + "X-PingLab-Offline: true\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                    + "Connection: close\r\n\r\n"
                    + body);
                await stream.WriteAsync(response, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task EchoTcpAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                {
                    return;
                }

                await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task EchoUdpAsync(
        UdpClient server,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await server.ReceiveAsync(cancellationToken);
                await server.SendAsync(
                    received.Buffer,
                    received.RemoteEndPoint,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken);
        }
    }

    private static async Task IgnoreExpectedCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ExpectInvalidData(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
