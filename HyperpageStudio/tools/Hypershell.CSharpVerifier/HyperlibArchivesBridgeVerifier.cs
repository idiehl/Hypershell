using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class HyperlibArchivesBridgeVerifier
{
    public static async Task VerifyAsync(string output)
    {
        var fixture = Path.Combine(Path.GetFullPath(output), "archives-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var previousSettings = Environment.GetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable);
        var assertions = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions.Add(message);
        }
        string? failure = null;
        try
        {
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, Path.Combine(fixture, "settings"));
            var library = Path.Combine(fixture, "library");
            Directory.CreateDirectory(Path.Combine(library, "app"));
            // Registration only: this fixture executable is never launched.
            await File.WriteAllBytesAsync(Path.Combine(library, "app", "HyperLib.Host.exe"), [0]);
            var registration = HyperlibMyNetRegistration.Create(library)
                ?? throw new InvalidOperationException("Synthetic installed host was not registered.");
            await using var host = new FixtureHost();
            await RunStaAsync(async () =>
            {
                var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources["HeaderIconButtonStyle"] = new Style(typeof(Button));
                application.Resources["CodeFontFamily"] = new FontFamily("Consolas");
                application.Resources["SecondaryBrush"] = Brushes.Gray;
                application.Resources["ForegroundBrush"] = Brushes.White;
                application.Resources["MutedForegroundBrush"] = Brushes.Gray;
                application.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                var configuration = new WorkspaceAiProviderConfigurationService(Path.Combine(fixture, "provider.json"));
                var contexts = new WorkspaceAiContextService(Path.Combine(fixture, "contexts.json"));
                var audit = new WorkspaceAiAuditService(Path.Combine(fixture, "audit.jsonl"));
                var operations = new WorkspaceAiOperationService(new WorkspaceAiPermissionBroker(Path.Combine(fixture, "permissions.json"), audit, contexts), audit);
                var sessionRunning = true;
                var unhandled = new List<Exception>();
                DispatcherUnhandledExceptionEventHandler onUnhandled = (_, args) => { unhandled.Add(args.Exception); args.Handled = true; };
                Dispatcher.CurrentDispatcher.UnhandledException += onUnhandled;
                try
                {
                    foreach (var webAccess in new[] { false, true })
                    {
                        registration.AllowExternalResources = webAccess;
                        var root = HyperlibMyNetRegistration.GetTrustedRoot(registration);
                        Check(root == library, $"Web access {webAccess}: native launch retains its trusted library root.");
                        using var bridge = new HyperlibNativeBridge(root!, host.Origin,
                            () => sessionRunning && HyperlibMyNetRegistration.GetTrustedRoot(registration) == library,
                            _ => throw new InvalidOperationException("The status fixture must not open Studio."),
                            new NoAiProvider(), configuration, new NoCredentials(), contexts, operations);
                        var model = new BrowserTabViewModel(host.Origin + "#natural-world", navigationPolicy:
                            new BrowserNavigationPolicy(registration.Id, "Archives bridge verification", host.Origin, webAccess), hyperlibBridge: bridge);
                        var window = new Window { Content = model.BrowserView, Width = 360, Height = 240,
                            Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false,
                            WindowStyle = WindowStyle.None, Opacity = 0 };
                        try
                        {
                            window.Show();
                            var browser = (WebView2)model.BrowserView.FindName("Browser");
                            await WaitUntilAsync(() => Task.FromResult(browser.CoreWebView2 is not null && !model.IsLoading && !model.HasError),
                                "The real BrowserTabView did not initialize and load its local document.");
                            var core = browser.CoreWebView2;
                            Check(BrowserTabView.ResolveBrowserUserDataDirectory().StartsWith(fixture, StringComparison.OrdinalIgnoreCase),
                                $"Web access {webAccess}: WebView2 uses only the fixture profile.");
                            await WaitUntilAsync(async () => await core.ExecuteScriptAsync("typeof window.fixtureRequest === 'function'") == "true",
                                "The fixture document was not ready.");
                            Check(core.Settings.IsWebMessageEnabled, $"Web access {webAccess}: the native channel is enabled after initial hash navigation.");
                            if (!webAccess)
                            {
                                var missing = await RequestAsync(core, "unconnected", "fixture-library");
                                Check(!missing.GetProperty("result").GetProperty("canSync").GetBoolean(), "The browser receives the unconnected status before configuration.");
                                Check(await core.ExecuteScriptAsync("document.querySelector('#sync').disabled") == "true", "An unconnected browser review keeps Sync disabled.");
                                HyperlibArchivesPublicationService.Configure(library, "https://atlasuniversalis.com", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
                                Check(HyperlibArchivesPublicationService.ConfigurationPath(library).StartsWith(fixture, StringComparison.OrdinalIgnoreCase),
                                    "The protected synthetic connection stays outside all user configuration and library data.");
                            }
                            var connected = await RequestAsync(core, "connected", "fixture-library");
                            Check(connected.GetProperty("error").ValueKind == JsonValueKind.Null && connected.GetProperty("result").GetProperty("canSync").GetBoolean(),
                                $"Web access {webAccess}: JavaScript receives canSync=true through the real BrowserTabView handler.");
                            Check(await core.ExecuteScriptAsync("document.querySelector('#sync').disabled") == "false",
                                $"Web access {webAccess}: the browser's Sync control becomes enabled.");

                            registration.AllowExternalResources = !webAccess;
                            var toggled = await RequestAsync(core, "toggled", "fixture-library");
                            Check(toggled.GetProperty("result").GetProperty("canSync").GetBoolean(),
                                $"Web access {webAccess}: changing the preference while the tab is open preserves the status roundtrip.");
                            registration.AllowExternalResources = webAccess;
                            await core.ExecuteScriptAsync("location.hash='science-lab'");
                            await WaitUntilAsync(async () => await core.ExecuteScriptAsync("location.hash") == "\"#science-lab\"", "Hash navigation did not finish.");
                            var hash = await RequestAsync(core, "new-hash", "fixture-library");
                            Check(hash.GetProperty("result").GetProperty("canSync").GetBoolean(), $"Web access {webAccess}: changing collections by hash preserves native replies.");

                            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            EventHandler<CoreWebView2NavigationStartingEventArgs> onNavigation = (_, args) =>
                            {
                                if (args.Uri.StartsWith("hypershell-verifier:", StringComparison.Ordinal)) cancelled.TrySetResult(args.Cancel);
                            };
                            core.NavigationStarting += onNavigation;
                            try
                            {
                                core.Navigate("hypershell-verifier://blocked");
                                Check(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10)), "MyNet cancels the non-web navigation before it can launch any handler.");
                            }
                            finally { core.NavigationStarting -= onNavigation; }
                            var afterCancel = await RequestAsync(core, "after-cancel", "fixture-library");
                            Check(core.Settings.IsWebMessageEnabled && afterCancel.GetProperty("result").GetProperty("canSync").GetBoolean(),
                                $"Web access {webAccess}: a cancelled navigation leaves the visible document's native connection functional.");

                            var wrongLibrary = await RequestAsync(core, "wrong-library", "other-library");
                            Check(wrongLibrary.GetProperty("result").ValueKind == JsonValueKind.Null
                                && wrongLibrary.GetProperty("error").GetString()!.Contains("identity"), "An unrelated library cannot read the publishing connection through the browser.");
                            var message = """{"channel":"hyperlib","id":"foreign","method":"archives.status","params":{"libraryId":"fixture-library"}}""";
                            var readsBefore = host.StateRequests;
                            Check(await bridge.HandleAsync("https://example.invalid/", core.Source, message) is null
                                && await bridge.HandleAsync(host.Origin + "frame", core.Source, message) is null
                                && await bridge.HandleAsync("http://127.0.0.1:1/", core.Source, message) is null,
                                "Foreign origins, other local ports, and child-document paths receive no bridge reply.");
                            Check(host.StateRequests == readsBefore, "Rejected origins do not query the library state.");
                            sessionRunning = false;
                            Check(!bridge.AcceptsOrigin(core.Source) && await bridge.HandleAsync(core.Source, core.Source, message) is null,
                                "Native capability ends when the tracked host session ends.");
                            sessionRunning = true;
                        }
                        finally { model.Dispose(); window.Close(); }
                    }
                    Check(unhandled.Count == 0, "The real browser roundtrips produce no unhandled dispatcher exceptions.");
                }
                finally { Dispatcher.CurrentDispatcher.UnhandledException -= onUnhandled; }
            }).WaitAsync(TimeSpan.FromSeconds(110));
            Check(host.StateRequests >= 11, "Status requests traverse the real loopback state-identity check.");
            Check(host.UnexpectedRequests == 0, "Verification only serves the local fixture and never invokes a publication endpoint.");
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, previousSettings);
            await File.WriteAllTextAsync(Path.Combine(fixture, "archives-bridge-verification.json"), JsonSerializer.Serialize(new
            { passed = failure is null, assertions, failure }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"HYPERLIB_ARCHIVES_BRIDGE_OK|checks:{assertions.Count}|actual-BrowserTabView-WebView2|{fixture}");
    }

    private static async Task<JsonElement> RequestAsync(CoreWebView2 core, string id, string libraryId)
    {
        await core.ExecuteScriptAsync($"window.fixtureRequest({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(libraryId)})");
        string? response = null;
        await WaitUntilAsync(async () =>
        {
            response = await core.ExecuteScriptAsync($"window.fixtureReplies[{JsonSerializer.Serialize(id)}] ?? null");
            return response != "null";
        }, $"The browser did not receive the native reply for '{id}'.");
        using var parsed = JsonDocument.Parse(response!);
        return parsed.RootElement.Clone();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(40);
        }
        throw new TimeoutException(failure);
    }

    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Archives native browser verification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class NoAiProvider : IWorkspaceAiProvider
    {
        public Task<WorkspaceAiProviderResponse> CreateResponseAsync(WorkspaceAiProviderConfiguration configuration,
            string apiKey, IReadOnlyList<JsonElement> input, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Archives status must not invoke an AI provider.");
    }

    private sealed class NoCredentials : IWorkspaceAiCredentialStore
    {
        public bool Exists => false;
        public string CredentialPath => "synthetic-unused";
        public WorkspaceAiStoredCredential? Load() => throw new InvalidOperationException("Archives status must not read AI credentials.");
        public void Save(string key) => throw new NotSupportedException();
        public void Delete() => throw new NotSupportedException();
    }

    private sealed class FixtureHost : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        private readonly List<Task> _connections = [];
        private int _stateRequests;
        private int _unexpectedRequests;
        public string Origin { get; }
        public int StateRequests => Volatile.Read(ref _stateRequests);
        public int UnexpectedRequests => Volatile.Read(ref _unexpectedRequests);
        public FixtureHost()
        {
            _listener.Start();
            Origin = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _server = ServeAsync();
        }
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _connections.Add(RespondAsync(client));
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task RespondAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token);
                    if (string.IsNullOrEmpty(request)) return; // A speculative browser socket can close without a request.
                    for (var lines = 0; lines < 100; lines++)
                        if (string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) break;
                    var path = request.Split(' ').ElementAtOrDefault(1);
                    string body;
                    var contentType = "text/html";
                    if (request.StartsWith("GET /api/state ", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref _stateRequests);
                        contentType = "application/json";
                        body = """{"libraryId":"fixture-library","revision":1,"nodes":[],"pages":{}}""";
                    }
                    else if (path is "/" or "/favicon.ico") body = """
                        <!doctype html><title>Archives bridge fixture</title><button id="sync" disabled>Sync to Archives</button>
                        <script>
                        window.fixtureReplies = {};
                        window.chrome.webview.addEventListener('message', ({ data }) => {
                          if (data.channel !== 'hyperlib' || !data.id) return;
                          window.fixtureReplies[data.id] = data;
                          document.querySelector('#sync').disabled = !data.result?.canSync;
                        });
                        window.fixtureRequest = (id, libraryId) => window.chrome.webview.postMessage({channel:'hyperlib', id, method:'archives.status', params:{libraryId}});
                        </script>
                        """;
                    else { Interlocked.Increment(ref _unexpectedRequests); body = "Unexpected fixture request"; }
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token);
                    await stream.WriteAsync(bytes, _stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (IOException) when (_stop.IsCancellationRequested) { }
            }
        }
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _server;
            await Task.WhenAll(_connections);
            _stop.Dispose();
        }
    }
}
