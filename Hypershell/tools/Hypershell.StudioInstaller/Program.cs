using Hypershell.Models;
using Hypershell.Services;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 2 && args[0] == "--prepare-projects-folder")
{
    PrepareProjectFolderAt(DefaultStudioRoot(), new SecurityIdentifier(args[1]));
    Console.WriteLine("STUDIO_PROJECTS_FOLDER_READY|" + Path.Combine(DefaultStudioRoot(), "Projects"));
    return;
}

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] is not ("--verify-only" or "--launch-existing"))
    throw new ArgumentException("Usage: Hypershell.StudioInstaller <studioAppDirectory> <applicationDataDirectory> [--verify-only|--launch-existing]");
var studioDirectory = AbsoluteDirectory(args[0]);
var baseDataDirectory = AbsoluteDirectory(args[1]);
var myNetAppDirectory = AbsoluteDirectory(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "HypershellAppDirectory").Value!);
foreach (var directory in new[] { studioDirectory, myNetAppDirectory })
    foreach (var file in new[] { "Hypershell.exe", "Hypershell.dll" })
        if (!File.Exists(Path.Combine(directory, file))) throw new FileNotFoundException("A verified app file is missing.", Path.Combine(directory, file));
var studioIcon = Path.Combine(studioDirectory, "Assets", "Brand", "hypershell.ico");
if (!File.Exists(studioIcon) && args.Length != 3)
    throw new FileNotFoundException("The Studio publish must include its application icon before registration.", studioIcon);
if (!File.Exists(studioIcon)) studioIcon = Path.Combine(myNetAppDirectory, "Assets", "MyNet", "mynet.ico");
if (!File.Exists(studioIcon)) throw new FileNotFoundException("An icon is required for the Studio manifest fixture.", studioIcon);

var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
if (args.Length == 3 && args[2] == "--launch-existing")
{
    await LaunchExistingAsync();
    return;
}
var fixture = Path.Combine(Path.GetTempPath(), "Hypershell", "studio-installer-fixture", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var fixtureRegistry = Path.Combine(fixture, "AtlasMyNet");
Directory.CreateDirectory(fixtureRegistry);
var fixtureStore = NewStore(fixtureRegistry);
var fixtureInstallRoot = Path.Combine(fixture, "installed-studio");
var fixtureParentAcl = new DirectoryInfo(fixture).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
var currentUserSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows account could not be identified.");
PrepareProjectFolderAt(fixtureInstallRoot, currentUserSid);
PrepareProjectFolderAt(fixtureInstallRoot, currentUserSid);
var fixtureProjects = new DirectoryInfo(Path.Combine(fixtureInstallRoot, "Projects"));
Require(fixtureProjects.Exists && fixtureProjects.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
    .OfType<FileSystemAccessRule>().Any(rule => rule.IdentityReference.Equals(currentUserSid)
        && rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify
        && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)),
    "Projects must grant the installing account inherited write access to its project files.");
Require(new DirectoryInfo(fixture).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access) == fixtureParentAcl,
    "Project setup must preserve parent directory permissions.");
var sentinel = new AtlasMyNetAppDefinition
{
    Id = "installer.fixture.existing", DisplayName = "Existing app preserved", UrlTemplate = "http://127.0.0.1:5123/",
    Launch = new AtlasMyNetLaunchDefinition { Executable = Path.Combine(studioDirectory, "Hypershell.exe"), Arguments = ["--fixture-do-not-run"] },
    Stop = new AtlasMyNetStopDefinition { Mode = AtlasMyNetStopMode.None }, ShowNavigationToolbar = false
};
var legacyEditor = sentinel.Clone();
legacyEditor.Id = "atlas.mynet.hyperlib-editor";
legacyEditor.DisplayName = "Hyperlib Editor";
var reader = sentinel.Clone();
reader.Id = "atlas.mynet.hyperlib";
reader.DisplayName = "Hyperlib reader preserved";
fixtureStore.Save([sentinel, legacyEditor, reader]);
fixtureStore.Trust(sentinel);
Install(fixture, verifyFixture: true);
Install(fixture, verifyFixture: true);
var fixtureAfter = fixtureStore.Load();
Require(fixtureAfter.Count(app => app.Id is "atlas.mynet.hyperpage-studio" or "atlas.mynet.hyperlib-editor") == 1
    && fixtureAfter.Any(app => app.Id == "atlas.mynet.hyperpage-studio") && fixtureAfter.Any(app => app.Id == reader.Id),
    "Repeated registration must retain one unified Studio app and preserve the Hyperlib reader.");
Require(JsonSerializer.Serialize(fixtureAfter.Single(app => app.Id == sentinel.Id), options) == JsonSerializer.Serialize(sentinel, options)
    && fixtureStore.IsTrusted(sentinel), "Registration must preserve unrelated apps and command trust.");
Require(Directory.GetDirectories(Path.Combine(fixtureRegistry, "backups")).All(directory =>
    File.Exists(Path.Combine(directory, "apps.json")) && File.Exists(Path.Combine(directory, "trust.device.json"))),
    "Every registration must back up the previous app registry and trust file.");
Console.WriteLine("STUDIO_INSTALLER_FIXTURE_OK|unified-native-app|reader-preserved|legacy-authoring-entry-removed|trust|unrelated-app|idempotent|backup|projects-folder|scoped-write-permission");

if (args.Length == 3)
{
    Console.WriteLine(JsonSerializer.Serialize(new AtlasMyNetAppDocument { Apps = CreateDefinitions(baseDataDirectory) }, options));
    Console.WriteLine("VERIFY_ONLY|No live registry was changed.");
    return;
}
await EnsureInstalledProjectsFolderAsync(currentUserSid);
Install(baseDataDirectory, verifyFixture: false);
Console.WriteLine("STUDIO_MYNET_REGISTERED|Hyperpage Studio|unified-authoring");

async Task LaunchExistingAsync()
{
    var store = NewStore(Path.Combine(baseDataDirectory, "AtlasMyNet"));
    var registryHash = SHA256.HashData(File.ReadAllBytes(store.AppsPath));
    var trustHash = SHA256.HashData(File.ReadAllBytes(store.TrustPath));
    var registered = store.ReadImport(store.AppsPath);
    // Load() can refresh built-ins. Hydrate trust using its existing read-only
    // implementation so this launch-only command never edits the registration.
    var loadTrust = typeof(AtlasMyNetAppStore).GetMethod("LoadTrust", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("This verified MyNet assembly does not expose the expected trust reader.");
    loadTrust.Invoke(store, null);
    var expected = CreateDefinitions(baseDataDirectory);
    var apps = expected.Select(definition =>
    {
        var app = registered.Single(value => value.Id == definition.Id);
        Require(app.IsNative && app.Ports.Count == 0 && app.UrlTemplate.Length == 0 && app.HealthCheck is null
            && app.Stop?.Mode == AtlasMyNetStopMode.ManagedProcess && app.Launch is not null,
            $"{definition.DisplayName} is not registered as the expected native app.");
        Require(string.Equals(Path.GetFullPath(app.Launch!.Executable), definition.Launch!.Executable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFullPath(app.Launch.WorkingDirectory), studioDirectory, StringComparison.OrdinalIgnoreCase)
            && app.Launch.Arguments.SequenceEqual(definition.Launch.Arguments, StringComparer.Ordinal)
            && app.Launch.Environment.Count == definition.Launch.Environment.Count
            && definition.Launch.Environment.All(pair => app.Launch.Environment.TryGetValue(pair.Key, out var value)
                && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase)),
            $"{definition.DisplayName}'s registered command differs from the installed Studio app and data profile.");
        Require(store.IsTrusted(app), $"{definition.DisplayName}'s exact registered commands are not trusted.");
        return app;
    }).ToList();
    var inventory = new WindowsSocketInventoryService();
    using var web = new AtlasMyNetWebService();
    using var service = new AtlasMyNetProcessService(store, inventory, web);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var receipts = new List<AtlasMyNetAppRuntime>();
    try
    {
        foreach (var app in apps)
        {
            var runtime = await service.StartAsync(app, cancellation.Token);
            var reopened = await service.StartAsync(app, cancellation.Token);
            Require(runtime.ProcessId == reopened.ProcessId && runtime.StartedAtUtc == reopened.StartedAtUtc,
                $"Reopening {app.DisplayName} did not activate the same process.");
            Require(inventory.TryGetProcessIdentity(runtime.ProcessId, out var identity)
                && identity.IsCurrentUserOwned && identity.StartTimeUtc.HasValue
                && Math.Abs((identity.StartTimeUtc.Value - runtime.StartedAtUtc).TotalSeconds) <= 1
                && string.Equals(identity.ExecutablePath, runtime.ExecutablePath, StringComparison.OrdinalIgnoreCase),
                $"{app.DisplayName}'s running process identity could not be verified.");
            using var process = Process.GetProcessById(runtime.ProcessId);
            Console.WriteLine("STUDIO_NATIVE_LAUNCH|" + JsonSerializer.Serialize(new
            {
                app.Id, app.DisplayName, runtime.ProcessId, runtime.ExecutablePath, runtime.StartedAtUtc,
                WindowTitle = process.MainWindowTitle, WindowHandle = process.MainWindowHandle.ToInt64(),
                ReopenedSameProcess = true, IsCurrentUserOwned = true, LeftRunning = true
            }));
            receipts.Add(runtime);
        }
        Require(receipts.Select(runtime => runtime.ProcessId).Distinct().Count() == 1,
            "The unified Studio must run as one native process.");
    }
    finally
    {
        service.LeaveSessionAppsRunning();
        var unchanged = registryHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(store.AppsPath)))
            && trustHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(store.TrustPath)));
        Console.WriteLine("STUDIO_LAUNCH_REGISTRY|" + (unchanged ? "unchanged" : "changed-during-app-startup"));
    }
}

List<AtlasMyNetAppDefinition> CreateDefinitions(string dataDirectory)
{
    var executable = Path.Combine(studioDirectory, "Hypershell.exe");
    return new[] { ("hyperpage-studio", "Hyperpage Studio") }
        .Select(entry => new AtlasMyNetAppDefinition
        {
            Id = "atlas.mynet." + entry.Item1,
            DisplayName = entry.Item2,
            IsNative = true,
            IconPath = studioIcon,
            UrlTemplate = string.Empty,
            Ports = [],
            Launch = new AtlasMyNetLaunchDefinition
            {
                Executable = executable,
                Arguments = ["--" + entry.Item1],
                WorkingDirectory = studioDirectory,
                Environment = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["HYPERSHELL_APP_DATA"] = dataDirectory,
                    ["HYPERSHELL_STUDIO_BASE_DATA"] = dataDirectory,
                    ["HYPERSHELL_SHARED_MYNET_DIRECTORY"] = Path.Combine(dataDirectory, "AtlasMyNet")
                }
            },
            Stop = new AtlasMyNetStopDefinition { Mode = AtlasMyNetStopMode.ManagedProcess },
            ShowNavigationToolbar = false
        }).ToList();
}

void Install(string dataDirectory, bool verifyFixture)
{
    var registry = Path.Combine(dataDirectory, "AtlasMyNet");
    Directory.CreateDirectory(registry);
    var store = NewStore(registry);
    // Use the store's registry mutex around the backup and its reentrant API calls.
    using var mutex = new Mutex(false, "Local\\Hypershell.MyNet.Registry." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(store.AppsPath.ToUpperInvariant()))));
    var acquired = false;
    try
    {
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another MyNet window is updating the app library.");
        var originalApps = File.Exists(store.AppsPath) ? File.ReadAllText(store.AppsPath) : null;
        var originalTrust = File.Exists(store.TrustPath) ? File.ReadAllText(store.TrustPath) : null;
        var existing = originalApps is null ? [] : store.ReadImport(store.AppsPath).ToList();
        var native = CreateDefinitions(dataDirectory).Select(AtlasMyNetAppStore.ValidateDefinition).ToList();
        var backup = Path.Combine(registry, "backups", "studio-port-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        if (originalApps is not null) File.WriteAllText(Path.Combine(backup, "apps.json"), originalApps, new UTF8Encoding(false));
        if (originalTrust is not null) File.WriteAllText(Path.Combine(backup, "trust.device.json"), originalTrust, new UTF8Encoding(false));
        try
        {
            store.UpdateDefinitions(latest =>
            {
                // Keep every pre-existing definition byte-for-byte equivalent after
                // normalization, including any built-in location the store refreshed.
                foreach (var original in existing)
                {
                    var index = latest.FindIndex(app => string.Equals(app.Id, original.Id, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) latest[index] = original.Clone();
                    else latest.Add(original.Clone());
                }
                foreach (var app in native)
                {
                    var index = latest.FindIndex(value => string.Equals(value.Id, app.Id, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) latest[index] = app.Clone();
                    else latest.Add(app.Clone());
                }
                latest.RemoveAll(app => string.Equals(app.Id, "atlas.mynet.hyperlib-editor", StringComparison.OrdinalIgnoreCase));
            });
            foreach (var app in native) store.Trust(app);
            var installed = store.ReadImport(store.AppsPath);
            foreach (var app in native)
                Require(installed.Count(value => value.Id == app.Id) == 1 && store.IsTrusted(app), "Each native command must be registered and trusted exactly once.");
            Require(installed.All(app => app.Id != "atlas.mynet.hyperlib-editor"), "The separate authoring app entry must be removed.");
            foreach (var original in existing.Where(app => app.Id != "atlas.mynet.hyperlib-editor" && native.All(value => value.Id != app.Id)))
                Require(JsonSerializer.Serialize(installed.Single(app => app.Id == original.Id), options) == JsonSerializer.Serialize(original, options),
                    "An unrelated app definition changed during registration.");
            if (originalTrust is not null)
            {
                using var beforeTrust = JsonDocument.Parse(originalTrust);
                using var afterTrust = JsonDocument.Parse(File.ReadAllText(store.TrustPath));
                var fingerprints = afterTrust.RootElement.GetProperty("Fingerprints").EnumerateArray().Select(value => value.GetString()).ToHashSet();
                Require(beforeTrust.RootElement.GetProperty("Fingerprints").EnumerateArray().All(value => fingerprints.Contains(value.GetString())),
                    "Existing command trust must be preserved.");
            }
            if (!verifyFixture) Console.WriteLine("BACKUP|" + backup);
        }
        catch
        {
            Restore(store.AppsPath, originalApps);
            Restore(store.TrustPath, originalTrust);
            throw;
        }
    }
    finally { if (acquired) mutex.ReleaseMutex(); }
}

AtlasMyNetAppStore NewStore(string registry)
{
    var appsPath = Path.Combine(registry, "apps.json");
    var document = File.Exists(appsPath)
        ? JsonSerializer.Deserialize<AtlasMyNetAppDocument>(File.ReadAllText(appsPath), options) : null;
    var pingLabExecutable = document?.Apps.FirstOrDefault(app => app.Id == AtlasMyNetAppStore.PingLabAppId)?.Launch?.Executable;
    return new AtlasMyNetAppStore(appsPath, Path.Combine(registry, "trust.device.json"),
        string.IsNullOrWhiteSpace(pingLabExecutable) ? null : pingLabExecutable);
}
static string AbsoluteDirectory(string value)
{
    if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("App and data directories must be absolute paths.");
    return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
}
static void Restore(string path, string? content)
{
    if (content is null) { if (File.Exists(path)) File.Delete(path); return; }
    File.WriteAllText(path + ".restore.tmp", content, new UTF8Encoding(false));
    File.Move(path + ".restore.tmp", path, overwrite: true);
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

static string DefaultStudioRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hyperpage Studio");

static void PrepareProjectFolderAt(string studioRoot, SecurityIdentifier accountSid)
{
    var projects = Path.Combine(studioRoot, "Projects");
    RejectDirectoryLinks(projects);
    Directory.CreateDirectory(projects);
    RejectDirectoryLinks(projects);
    var directory = new DirectoryInfo(projects);
    var permissions = directory.GetAccessControl();
    var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    var existing = permissions.GetAccessRules(true, false, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>();
    if (!existing.Any(rule => rule.IdentityReference.Equals(accountSid) && rule.AccessControlType == AccessControlType.Allow
        && (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify
        && rule.InheritanceFlags == inheritance && rule.PropagationFlags == PropagationFlags.None))
    {
        permissions.AddAccessRule(new FileSystemAccessRule(accountSid, FileSystemRights.Modify, inheritance, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(permissions);
    }
}

static void RejectDirectoryLinks(string path)
{
    for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
        if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Studio Projects directory cannot be created through a symbolic link or junction.");
}

static async Task EnsureInstalledProjectsFolderAsync(SecurityIdentifier accountSid)
{
    try { PrepareProjectFolderAt(DefaultStudioRoot(), accountSid); }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("STUDIO_PROJECTS_ELEVATION|Windows administrator approval is required to create the Projects folder and grant the installing account write access only inside it.");
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Installer path is unavailable."))
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--prepare-projects-folder");
        start.ArgumentList.Add(accountSid.Value);
        using var elevated = Process.Start(start) ?? throw new IOException("The Projects folder setup could not start.");
        await elevated.WaitForExitAsync();
        if (elevated.ExitCode != 0) throw new IOException("Administrator setup of the Studio Projects folder failed.");
        PrepareProjectFolderAt(DefaultStudioRoot(), accountSid);
    }
    Console.WriteLine("STUDIO_PROJECTS_FOLDER_READY|" + Path.Combine(DefaultStudioRoot(), "Projects"));
}
