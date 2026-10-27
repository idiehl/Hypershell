using Hypershell.Services;
using Hypershell;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using Hypershell.Models;

internal static class HyperlibNativeVerifier
{
    public static async Task VerifyAsync(string root)
    {
        var previousDataRoot = Environment.GetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable);
        try
        {
            var previewDataRoot = Path.Combine(root, "preview-settings");
            Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, previewDataRoot);
            Require(BrowserTabView.ResolveBrowserUserDataDirectory() == Path.Combine(previewDataRoot, "browser"),
                "The isolated app data root was not applied to the managed browser profile.");
        }
        finally { Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable, previousDataRoot); }
        var library = Path.Combine(root, "Library");
        Directory.CreateDirectory(Path.Combine(library, "Host"));
        await File.WriteAllTextAsync(Path.Combine(library, "Host", "Hyperlib.Host.csproj"), "<Project />");
        var app = HyperlibMyNetRegistration.Create(library) ?? throw new InvalidOperationException("HyperLib discovery failed.");
        Require(app.Launch!.Arguments.Contains("--root") && app.HealthCheck!.HeaderValue == "hyperlib", "Registration lost its root/health identity.");
        Require(HyperlibMyNetRegistration.GetTrustedRoot(app) == library, "The built-in launch was not recognized.");
        Require(HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(app, app.Launch.Executable)
            && HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(app, Path.Combine(library, "Host", "bin", "Debug", "net10.0", "HyperLib.Host.exe")),
            "The source-development dotnet launch or its compiled child was rejected.");
        var webEnabledApp = app.Clone();
        webEnabledApp.AllowExternalResources = true;
        Require(HyperlibMyNetRegistration.GetTrustedRoot(webEnabledApp) == library
            && HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(webEnabledApp, app.Launch.Executable)
            && HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(webEnabledApp, Path.Combine(library, "Host", "bin", "Debug", "net10.0", "HyperLib.Host.exe")),
            "Web access disabled native identity for a source-development Host or its compiled child.");
        var altered = app.Clone();
        altered.Launch!.Arguments.Add("--untrusted");
        Require(HyperlibMyNetRegistration.GetTrustedRoot(altered) is null, "Changed commands retained bridge privilege.");
        altered = app.Clone();
        altered.UrlTemplate = "http://127.0.0.1:9999/";
        Require(HyperlibMyNetRegistration.GetTrustedRoot(altered) is null, "An altered URL retained native bridge privilege.");
        try { HyperlibMyNetRegistration.ContainedProject(library, "../escape"); throw new Exception("A project escaped its library."); }
        catch (UnauthorizedAccessException) { }
        await VerifyHostTargetAsync(root);
        await HyperlibSessionVerifier.VerifyAsync(root);

        var fixture = """
            {"status":"completed","output":[
              {"type":"web_search_call","id":"ws1","status":"completed","action":{"type":"search","sources":[{"url":"https://example.org/article","title":"Research evidence"}]}},
              {"type":"message","role":"assistant","content":[{"type":"output_text","text":"Supported fact.","annotations":[{"type":"url_citation","url":"https://example.org/article","title":"Research evidence","start_index":0,"end_index":14}]}]}
            ]}
            """;
        var handler = new CaptureHandler(fixture);
        using var http = new HttpClient(handler);
        using var provider = new ResponsesWorkspaceAiProvider(http);
        var configuration = new WorkspaceAiProviderConfiguration
        { AllowCloudRequests = true, RequestProfile = HyperlibContentAiService.CreateProfile(true, true) };
        var result = await provider.CreateResponseAsync(configuration, "sk-fixture-not-a-real-key",
            [JsonSerializer.SerializeToElement(new { role = "user", content = "Research a subject." })]);
        Require(result.Sources?.Single().Title == "Research evidence" && result.Citations?.Single().EndIndex == 14,
            "Research citations or search provenance were lost.");
        using (var payload = JsonDocument.Parse(handler.Body))
        {
            var tools = payload.RootElement.GetProperty("tools").EnumerateArray().ToList();
            Require(tools.Any(tool => tool.GetProperty("type").GetString() == "web_search"), "Hosted research tool missing.");
            Require(tools.Where(tool => tool.GetProperty("type").GetString() == "function").All(tool =>
                tool.GetProperty("name").GetString() is "read_library" or "read_templates" or "read_base_templates" or "apply_content_operations" or "fetch_source"), "The content profile exposed code or terminal tools.");
            Require(tools.Any(tool => tool.TryGetProperty("name", out var name) && name.GetString() == "read_base_templates"),
                "The content assistant cannot discover reusable templates.");
            var contentTool = tools.Single(tool => tool.TryGetProperty("name", out var name) && name.GetString() == "apply_content_operations");
            Require(contentTool.GetProperty("parameters").GetProperty("properties").GetProperty("operations").GetProperty("items")
                .GetProperty("properties").TryGetProperty("starterId", out _), "The tool schema rejects a Work starter ID.");
            Require(payload.RootElement.GetProperty("include")[0].GetString() == "web_search_call.action.sources", "Search source inclusion missing.");
            Require(!payload.RootElement.GetProperty("instructions").GetString()!.Contains("SOURCE TRANSFER MODE"), "Coding instructions leaked into content profile.");
        }
        await provider.CreateResponseAsync(configuration with { RequestProfile = HyperlibContentAiService.CreateProfile(false, false) },
            "sk-fixture-not-a-real-key", [JsonSerializer.SerializeToElement(new { role = "user", content = "Write." })]);
        using (var payload = JsonDocument.Parse(handler.Body))
            Require(payload.RootElement.GetProperty("tools").EnumerateArray().All(tool =>
                tool.GetProperty("type").GetString() != "web_search" && tool.GetProperty("name").GetString() != "fetch_source"), "Internet tools bypassed the disabled capability.");
        try
        {
            await provider.CreateResponseAsync(configuration with
            {
                ProviderId = WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                Endpoint = WorkspaceAiProviderConfiguration.LlamaCppResponsesEndpoint,
                Model = WorkspaceAiModelCatalog.EngineerModelId
            }, "", [JsonSerializer.SerializeToElement(new { role = "user", content = "Research." })]);
            throw new Exception("Hosted search was sent to the local runtime.");
        }
        catch (WorkspaceAiProviderException exception) { Require(exception.Message.Contains("requires"), "Unexpected local capability failure."); }

        var configurationStore = new WorkspaceAiProviderConfigurationService(Path.Combine(root, "provider.json"));
        configurationStore.Save(new WorkspaceAiProviderConfiguration { AllowCloudRequests = true });
        var contexts = new WorkspaceAiContextService(Path.Combine(root, "contexts.json"));
        var audit = new WorkspaceAiAuditService(Path.Combine(root, "audit.jsonl"));
        var permissions = new WorkspaceAiPermissionBroker(Path.Combine(root, "permissions.json"), audit, contexts);
        var operations = new WorkspaceAiOperationService(permissions, audit);
        var credentials = new FixtureCredentials();
        var running = true;
        using (var bridge = new HyperlibNativeBridge(library, "http://127.0.0.1:5267/", () => running,
            _ => Task.FromResult(true), provider, configurationStore, credentials, contexts, operations))
        {
            Require(bridge.AcceptsOrigin("http://127.0.0.1:5267/work"), "The launched origin was rejected.");
            Require(!bridge.AcceptsOrigin("http://127.0.0.1:5268/") && !bridge.AcceptsOrigin("https://example.org/"), "A foreign origin received bridge access.");
            Require(await bridge.HandleAsync("http://127.0.0.1:5267/frame", "http://127.0.0.1:5267/", "{}") is null,
                "A child document received the top-level bridge.");
            Require(await bridge.HandleAsync("http://127.0.0.1:5267/", "http://127.0.0.1:5267/#work",
                """{"channel":"hyperlib","id":"hash-route","method":"ai.cancel","params":{}}""") is not null,
                "Client-side hash navigation disabled the same document's bridge.");
            running = false;
            Require(!bridge.AcceptsOrigin("http://127.0.0.1:5267/"), "Bridge privilege survived the launched process.");
        }
        var state = JsonSerializer.SerializeToElement(new { libraryId = "library", revision = 1, nodes = Array.Empty<object>(), pages = Array.Empty<object>() });
        var scripted = new SourceGuardProvider();
        var applied = false;
        var contentAi = new HyperlibContentAiService(scripted, configurationStore, credentials, contexts, operations,
            _ => Task.FromResult(state), (_, _) => { applied = true; return Task.FromResult(state); }, (_, _) => Task.FromResult(state),
            _ => Task.FromResult(JsonSerializer.SerializeToElement(Array.Empty<object>())));
        await contentAi.SendAsync("Write with sources", state, default, _ => { }, CancellationToken.None);
        Require(!applied && scripted.SawSourceRejection, "An invented page source was persisted as research.");

        var templateState = JsonDocument.Parse("""
            {"libraryId":"library","revision":42,"nodes":[],"pages":{"p":{"id":"p","workId":"w","title":"Page","templateId":"standard","usesEditableChrome":true,"revision":5,"blocks":[{"id":"header","type":"header","region":"main","values":{"title":"Existing header"}}],"sources":[]}}}
            """).RootElement.Clone();
        var templateProvider = new TemplateContentProvider();
        var catalogReads = 0;
        JsonElement appliedTemplateBatch = default;
        var templateAi = new HyperlibContentAiService(templateProvider, configurationStore, credentials, contexts, operations,
            _ => Task.FromResult(templateState), (batch, _) => { appliedTemplateBatch = batch.Clone(); return Task.FromResult(templateState); },
            (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { })),
            _ => { catalogReads++; return Task.FromResult(HyperLib.Core.ContentJson.Value(HyperLib.Core.BaseTemplateCatalog.List())); });
        await templateAi.SendAsync("Use a portfolio design and update the page text.", templateState, default, _ => { }, CancellationToken.None);
        Require(catalogReads == 1 && templateProvider.SawCatalog && templateProvider.SawSuccess,
            "Base template discovery or content operation dispatch failed.");
        var templateOperations = appliedTemplateBatch.GetProperty("operations").EnumerateArray().ToArray();
        Require(templateOperations[0].GetProperty("starterId").GetString() == "portfolio-grid", "The selected starter ID was not forwarded to the host.");
        Require(templateOperations[1].GetProperty("page").GetProperty("usesEditableChrome").GetBoolean(),
            "Omitting chrome metadata from an AI save restored legacy chrome.");
        Require(templateOperations[1].GetProperty("page").GetProperty("blocks")[0].GetProperty("id").GetString() == "header",
            "Saving page text lost the preserved default header.");
        Require(templateOperations[3].GetProperty("page").GetProperty("usesEditableChrome").GetBoolean(),
            "A create-and-save batch lost its supplied new-page chrome metadata.");

        if (HyperlibMyNetRegistration.Discover() is { } discovered
            && HyperlibMyNetRegistration.GetTrustedRoot(discovered) is { } packageRoot
            && File.Exists(Path.Combine(packageRoot, VisualPageProjectService.ProjectManifestFileName)))
        {
            var catalog = new VisualPageComponentCatalog();
            var validator = new VisualPageSchemaValidator(catalog);
            var projects = new VisualPageProjectService(validator, new VisualPageGenerator(catalog, validator), catalog);
            var discoveredProjects = HyperlibStudioAuthoringService.DiscoverProjects(packageRoot);
            Require(discoveredProjects.Count > 0, "The root HyperLib Studio project was not discovered.");
            foreach (var project in discoveredProjects)
            {
                var session = await projects.OpenAsync(project.RootPath);
                Require(!string.IsNullOrWhiteSpace(session.Project.HyperlibAuthoringPath), "A HyperLib project has no template authoring contract.");
                var template = await HyperlibStudioAuthoringService.LoadAsync(project.RootPath, session.Project.HyperlibAuthoringPath);
                Require(template.Definition.Blocks.Count > 0, "A HyperLib project opened without editable block declarations.");
            }
            Console.WriteLine($"HYPERLIB_STUDIO_OPEN_OK|projects:{discoveredProjects.Count}|contracts:validated");
        }

        Console.WriteLine("HYPERLIB_NATIVE_OK|registration|launch-trust|contained-projects|isolated-content-tools|search-gating|verified-citations|local-no-fallback|origin+frame+lifetime|invented-source-rejection|base-template-choice|editable-chrome-preserved");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static async Task VerifyHostTargetAsync(string outputRoot)
    {
        var checks = 0;
        void Check(bool condition, string description) { Require(condition, description); checks++; }
        var library = Path.Combine(outputRoot, "immutable-host-registration");
        var appRoot = Path.Combine(library, "app");
        var oldPath = Path.Combine(appRoot, "HyperLib.Host.exe");
        var firstPath = Path.Combine(appRoot, "releases", "first", "HyperLib.Host.exe");
        var secondPath = Path.Combine(appRoot, "releases", "second", "HyperLib.Host.exe");
        foreach (var path in new[] { oldPath, firstPath, secondPath })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [0]); // Registration fixtures are never executed.
        }
        var pointer = Path.Combine(appRoot, "host-target.json");
        Task PointTo(string value) => File.WriteAllTextAsync(pointer, JsonSerializer.Serialize(new { relativeExecutable = value }));
        var legacy = HyperlibMyNetRegistration.Create(library)!;
        Check(Path.GetFullPath(legacy.Launch!.Executable).Equals(oldPath, StringComparison.OrdinalIgnoreCase), "Missing host pointer did not preserve legacy discovery.");
        await PointTo("releases/first/HyperLib.Host.exe");
        var first = HyperlibMyNetRegistration.Create(library)!;
        Check(first.Launch!.Executable == firstPath && HyperlibMyNetRegistration.GetTrustedRoot(first) == library,
            "A contained immutable host target was not selected and trusted.");
        Check(HyperlibMyNetRegistration.GetTrustedRoot(legacy) is null, "Changing the host target retained old native bridge trust.");
        Check(HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(legacy, first), "A trusted legacy registration cannot migrate to its immutable release.");
        Check(HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(first, firstPath)
            && !HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(first, oldPath), "A stale host executable was accepted as the installed release.");
        foreach (var allowWebAccess in new[] { false, true })
        {
            var configured = first.Clone();
            configured.AllowExternalResources = allowWebAccess;
            var previous = legacy.Clone();
            previous.AllowExternalResources = allowWebAccess;
            Check(HyperlibMyNetRegistration.GetTrustedRoot(configured) == library,
                $"Web access {allowWebAccess} prevented attaching the native bridge to the installed Host.");
            Check(HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(configured, firstPath)
                && !HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(configured, oldPath),
                $"Web access {allowWebAccess} changed the exact installed Host identity check.");
            Check(HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(previous, configured)
                && HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(previous, first),
                $"Web access {allowWebAccess} blocked migration to the next built-in Host release.");

            (string Name, Action<AtlasMyNetAppDefinition> Apply)[] tampering =
            [
                ("arguments", value => value.Launch!.Arguments.Add("--untrusted")),
                ("executable", value => value.Launch!.Executable = Path.Combine(outputRoot, "other", "HyperLib.Host.exe")),
                ("working directory", value => value.Launch!.WorkingDirectory = outputRoot),
                ("environment", value => value.Launch!.Environment["UNTRUSTED_HOST"] = "true"),
                ("origin", value => value.UrlTemplate = "http://127.0.0.1:9999/"),
                ("ports", value => value.Ports = [9999]),
                ("health identity", value => value.HealthCheck!.HeaderValue = "other-app"),
                ("stop command", value => value.Stop!.Mode = AtlasMyNetStopMode.Command)
            ];
            foreach (var (name, apply) in tampering)
            {
                var changed = configured.Clone();
                apply(changed);
                Check(HyperlibMyNetRegistration.GetTrustedRoot(changed) is null
                    && !HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(changed, firstPath),
                    $"Changed {name} retained native Host identity with Web access {allowWebAccess}.");
                changed = previous.Clone();
                apply(changed);
                Check(!HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(changed, configured),
                    $"Changed {name} was trusted during release migration with Web access {allowWebAccess}.");
            }
        }
        var altered = legacy.Clone(); altered.Launch!.Arguments.Add("--altered");
        Check(!HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(altered, first), "Changed launch arguments were accepted during release migration.");
        altered = legacy.Clone(); altered.UrlTemplate = "http://127.0.0.1:9999/";
        Check(!HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(altered, first), "Changed origin was accepted during release migration.");
        await PointTo("releases/second/HyperLib.Host.exe");
        var second = HyperlibMyNetRegistration.Create(library)!;
        Check(HyperlibMyNetRegistration.GetTrustedRoot(first) is null && HyperlibMyNetRegistration.IsPreviousBuiltInLaunch(first, second),
            "Successive immutable release changes did not invalidate old bridge trust and permit trusted migration.");

        var oldRootEnvironment = Environment.GetEnvironmentVariable("HYPERLIB_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("HYPERLIB_ROOT", library);
            var store = new AtlasMyNetAppStore(Path.Combine(outputRoot, "host-apps.json"), Path.Combine(outputRoot, "host-trust.json"));
            store.Save([legacy]); store.Trust(legacy);
            var migrated = store.Load().Single(app => app.Id == HyperlibMyNetRegistration.AppId);
            Check(HyperlibMyNetRegistration.SameLaunch(migrated, second) && store.IsTrusted(migrated) && !store.IsTrusted(legacy),
                "MyNet did not migrate its already-trusted built-in launch to the installed release.");
            foreach (var allowWebAccess in new[] { false, true })
            {
                var permissionStore = new AtlasMyNetAppStore(Path.Combine(outputRoot, $"host-web-{allowWebAccess}-apps.json"),
                    Path.Combine(outputRoot, $"host-web-{allowWebAccess}-trust.json"));
                var previous = first.Clone();
                previous.AllowExternalResources = allowWebAccess;
                permissionStore.Save([previous]);
                permissionStore.Trust(previous);
                var updated = permissionStore.Load().Single(app => app.Id == HyperlibMyNetRegistration.AppId);
                Check(HyperlibMyNetRegistration.SameLaunch(updated, second)
                    && permissionStore.IsTrusted(updated) && !permissionStore.IsTrusted(previous),
                    $"MyNet did not migrate the trusted Host when Web access was {allowWebAccess}.");
                Check(updated.AllowExternalResources == allowWebAccess
                    && HyperlibMyNetRegistration.GetTrustedRoot(updated) == library,
                    $"Release migration lost Web access {allowWebAccess} or native bridge identity.");
                var reloaded = permissionStore.Load().Single(app => app.Id == HyperlibMyNetRegistration.AppId);
                Check(reloaded.AllowExternalResources == allowWebAccess
                    && HyperlibMyNetRegistration.MatchesRegisteredHostExecutable(reloaded, secondPath),
                    $"Web access {allowWebAccess} or installed Host identity did not survive registry reload.");
            }
            var untrustedStore = new AtlasMyNetAppStore(Path.Combine(outputRoot, "host-untrusted-apps.json"), Path.Combine(outputRoot, "host-untrusted-trust.json"));
            untrustedStore.Save([legacy]);
            var untouched = untrustedStore.Load().Single(app => app.Id == HyperlibMyNetRegistration.AppId);
            Check(HyperlibMyNetRegistration.SameLaunch(untouched, legacy) && !untrustedStore.IsTrusted(untouched),
                "An untrusted previous registration was silently trusted during migration.");
            using var web = new AtlasMyNetWebService();
            using var processes = new AtlasMyNetProcessService(store, new EmptySocketInventory(), web);
            var oldSocket = new NetworkSocketSnapshot("old-host", NetworkTransport.Tcp, AddressFamily.InterNetwork,
                "Listen", "127.0.0.1", second.Ports[0], "0.0.0.0", 0, 987654, "HyperLib.Host", oldPath, "fixture", "fixture", true, DateTimeOffset.UtcNow);
            Check(await processes.FindRunningAsync(second, [oldSocket]) is null,
                "MyNet adopted the previous Host release instead of leaving it running independently.");
            await PointTo("missing/HyperLib.Host.exe");
            Check(HyperlibMyNetRegistration.Create(library) is null, "Missing configured release silently fell back to the old Host.");
            try { await processes.StartAsync(second); throw new Exception("An invalid installed pointer launched an old registration."); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("host target", StringComparison.Ordinal), "Invalid host pointer returned an unrelated launch failure."); }
        }
        finally { Environment.SetEnvironmentVariable("HYPERLIB_ROOT", oldRootEnvironment); }

        foreach (var path in new[] { "../HyperLib.Host.exe", "https://example.org/HyperLib.Host.exe", "C:/elsewhere/HyperLib.Host.exe", "/outside/HyperLib.Host.exe", "releases/../HyperLib.Host.exe", "releases//HyperLib.Host.exe", "releases/first./HyperLib.Host.exe", "releases/first/other.exe" })
        {
            await PointTo(path);
            Check(HyperlibMyNetRegistration.Create(library) is null, "Unsafe configured host target was accepted: " + path);
        }
        await File.WriteAllTextAsync(pointer, "not json");
        Check(HyperlibMyNetRegistration.Create(library) is null, "Malformed installed pointer silently fell back to a legacy host.");
        await File.WriteAllTextAsync(pointer, new string(' ', 17 * 1024));
        Check(HyperlibMyNetRegistration.Create(library) is null, "Oversized installed pointer was accepted.");
        File.Delete(pointer); Directory.CreateDirectory(pointer);
        Check(HyperlibMyNetRegistration.Create(library) is null, "Directory masquerading as a host pointer was accepted.");
        Directory.Delete(pointer);
        var link = Path.Combine(appRoot, "linked");
        var linkChecked = false;
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(firstPath)!);
            await PointTo("linked/HyperLib.Host.exe");
            Check(HyperlibMyNetRegistration.Create(library) is null, "A symbolic host target received launch trust.");
            linkChecked = true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.ComponentModel.Win32Exception
            || error is IOException && (error.HResult & 0xFFFF) == 1314) { }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
        File.Delete(pointer);
        Check(Path.GetFullPath(HyperlibMyNetRegistration.Create(library)!.Launch!.Executable).Equals(oldPath, StringComparison.OrdinalIgnoreCase),
            "Removing the optional host pointer did not restore legacy discovery.");
        Console.WriteLine($"HYPERLIB_HOST_TARGET_OK|checks:{checks}|symbolic-target:{(linkChecked ? "rejected" : "unavailable")}|old-host-not-adopted|trusted-release-migration|web-access-independent-identity|web-access-preserved");
    }

    private sealed class EmptySocketInventory : INetworkSocketInventoryService
    {
        public IReadOnlyList<NetworkSocketSnapshot> Capture() => [];
        public bool TryGetProcessIdentity(int processId, out NetworkProcessIdentity identity) { identity = null!; return false; }
    }
    private sealed class CaptureHandler(string fixture) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class FixtureCredentials : IWorkspaceAiCredentialStore
    {
        public bool Exists => true;
        public string CredentialPath => "fixture-only";
        public WorkspaceAiStoredCredential? Load() => new("sk-fixture-not-a-real-key");
        public void Save(string key) => throw new NotSupportedException();
        public void Delete() => throw new NotSupportedException();
    }
    private sealed class SourceGuardProvider : IWorkspaceAiProvider
    {
        private int _calls;
        public bool SawSourceRejection { get; private set; }
        public Task<WorkspaceAiProviderResponse> CreateResponseAsync(WorkspaceAiProviderConfiguration configuration,
            string apiKey, IReadOnlyList<JsonElement> input, CancellationToken cancellationToken = default)
        {
            if (_calls++ == 0)
                return Task.FromResult(new WorkspaceAiProviderResponse([], "", [new("call1", "apply_content_operations",
                    """{"expectedRevision":1,"description":"draft","operations":[{"kind":"savePage","page":{"id":"p","sources":[{"url":"https://invented.invalid/made-up","verified":true}]}}]}""")], []));
            SawSourceRejection = input.Last().GetProperty("output").GetString()!.Contains("never invent", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new WorkspaceAiProviderResponse([], "No unverified source was saved.", [], []));
        }
    }
    private sealed class TemplateContentProvider : IWorkspaceAiProvider
    {
        private int _calls;
        public bool SawCatalog { get; private set; }
        public bool SawSuccess { get; private set; }
        public Task<WorkspaceAiProviderResponse> CreateResponseAsync(WorkspaceAiProviderConfiguration configuration,
            string apiKey, IReadOnlyList<JsonElement> input, CancellationToken cancellationToken = default)
        {
            if (_calls++ == 0)
                return Task.FromResult(new WorkspaceAiProviderResponse([], "", [new("templates", "read_base_templates", "{}")], []));
            using var output = JsonDocument.Parse(input.Last().GetProperty("output").GetString()!);
            if (_calls == 2)
            {
                SawCatalog = output.RootElement.GetArrayLength() == 17
                    && output.RootElement.EnumerateArray().Any(item => item.GetProperty("id").GetString() == "portfolio-grid");
                return Task.FromResult(new WorkspaceAiProviderResponse([], "", [new("edit", "apply_content_operations", """
                    {"expectedRevision":42,"description":"Choose a starter and edit content","operations":[
                      {"kind":"createNode","id":"new-work","parentId":"collection","nodeType":"work","title":"New portfolio","starterId":"portfolio-grid"},
                      {"kind":"savePage","id":"p","page":{"id":"p","workId":"w","title":"Page","templateId":"standard","revision":5,"blocks":[{"id":"header","type":"header","region":"main","values":{"title":"Existing header"}},{"id":"text","type":"text","region":"main","values":{"text":"Requested addition"}}],"sources":[]}},
                      {"kind":"createNode","id":"fresh","parentId":"new-work","nodeType":"page","title":"Fresh page"},
                      {"kind":"savePage","id":"fresh","page":{"id":"fresh","workId":"new-work","title":"Fresh page","templateId":"standard","usesEditableChrome":true,"revision":0,"blocks":[],"sources":[]}}
                    ]}
                    """)], []));
            }
            SawSuccess = output.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
            return Task.FromResult(new WorkspaceAiProviderResponse([], "The selected template and content were saved.", [], []));
        }
    }
}
