using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hypershell.Models;
using Hypershell.Services;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("Expected app directory, application-data directory, manifest, and optional --apply / expected registry hash.");
            var appDirectory = Path.GetFullPath(args[0]);
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var path = Path.Combine(appDirectory, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            return Run(appDirectory, Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), args.Skip(3).ToArray());
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string appDirectory, string dataDirectory, string manifest, string[] options)
    {
        var assemblyPath = Path.Combine(appDirectory, "Hypershell.dll");
        var liveAssemblyHash = Hash(File.ReadAllBytes(assemblyPath));
        if (liveAssemblyHash != Hash(File.ReadAllBytes(typeof(AtlasMyNetAppStore).Assembly.Location)))
            throw new InvalidOperationException("The loaded importer does not match the specified live Hypershell assembly.");
        var settings = new SettingsService(dataDirectory);
        var instanceName = "Local\\Hypershell.MyNet.Desktop." + Hash(Encoding.UTF8.GetBytes(
            Path.GetFullPath(settings.AtlasMyNetDirectory).TrimEnd('\\', '/').ToUpperInvariant()));
        if (!EventWaitHandle.TryOpenExisting(instanceName + ".activate", out var desktopEvent))
            throw new InvalidOperationException("No running MyNet desktop owns this settings directory. Refusing to guess a profile.");
        desktopEvent.Dispose(); // Verify ownership only; do not activate or operate the UI.

        var registryPath = settings.AtlasMyNetAppsPath;
        var trustPath = settings.AtlasMyNetTrustPath;
        if (!File.Exists(registryPath) || !File.Exists(trustPath))
            throw new InvalidOperationException("The existing registry and trust file must both be present.");
        var lockName = "Local\\Hypershell.MyNet.Registry." + Hash(Encoding.UTF8.GetBytes(registryPath.ToUpperInvariant()));
        using var mutex = new Mutex(false, lockName);
        var held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("MyNet is currently saving its registry. Try again.");
            var registryBytes = File.ReadAllBytes(registryPath);
            var trustBytes = File.ReadAllBytes(trustPath);
            var registryHash = Hash(registryBytes);
            var before = JsonNode.Parse(registryBytes) as JsonObject ?? throw new InvalidDataException("Invalid registry.");
            var beforeDocument = JsonSerializer.Deserialize<AtlasMyNetAppDocument>(registryBytes, JsonOptions)
                ?? throw new InvalidDataException("Invalid registry document.");
            var pingLab = beforeDocument.Apps.SingleOrDefault(item => item.Id.Equals(AtlasMyNetAppStore.PingLabAppId, StringComparison.OrdinalIgnoreCase));
            if (pingLab?.Launch is null || !File.Exists(pingLab.Launch.Executable))
                throw new InvalidOperationException("Existing Ping Lab must have a valid executable; importer seeding/refresh is not allowed.");
            // Point built-in discovery at its existing path: Load cannot refresh that definition.
            var store = new AtlasMyNetAppStore(registryPath, trustPath, Path.GetDirectoryName(pingLab.Launch.Executable));
            var existing = store.ReadImport(registryPath); // Validation without Load's seeding behavior.
            if (existing.Count != beforeDocument.Apps.Count || existing.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != existing.Count)
                throw new InvalidDataException("Existing registry IDs must be unique.");
            foreach (var entry in existing)
            {
                var raw = FindApp(before, entry.Id);
                if (!JsonNode.DeepEquals(raw, JsonSerializer.SerializeToNode(entry, JsonOptions)))
                    throw new InvalidOperationException($"Importer normalization would alter existing app {entry.Id}; refusing unrelated changes.");
            }
            var imported = store.ReadImport(manifest);
            if (imported.Count != 1 || imported[0].Id != "atlas.meridian")
                throw new InvalidDataException("This helper imports exactly one atlas.meridian definition.");
            var definition = imported[0];
            if (definition.Launch is null || !File.Exists(definition.Launch.Executable))
                throw new InvalidDataException("The Meridian package executable does not exist.");
            var apply = options.Contains("--apply", StringComparer.Ordinal);
            var expectedIndex = Array.IndexOf(options, "--expected-registry-sha256");
            if (apply && (expectedIndex < 0 || expectedIndex + 1 >= options.Length || !registryHash.Equals(options[expectedIndex + 1], StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Apply requires the unchanged registry SHA-256 from a preflight run.");
            string? backup = null;
            if (apply)
            {
                backup = registryPath + ".pre-meridian-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".bak";
                File.Copy(registryPath, backup, overwrite: false);
                store.UpdateDefinitions(latest =>
                {
                    var index = latest.FindIndex(item => item.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) latest[index] = definition.Clone();
                    else latest.Add(definition.Clone());
                });
                var after = JsonNode.Parse(File.ReadAllBytes(registryPath)) as JsonObject ?? throw new InvalidDataException("Registry verification failed.");
                foreach (var entry in existing.Where(item => !item.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
                    if (!JsonNode.DeepEquals(FindApp(before, entry.Id), FindApp(after, entry.Id)))
                        throw new InvalidOperationException($"Unrelated definition changed: {entry.Id}. Backup: {backup}");
                if (!JsonNode.DeepEquals(FindApp(after, definition.Id), JsonSerializer.SerializeToNode(definition, JsonOptions)))
                    throw new InvalidOperationException("Registered Meridian does not match the validated manifest.");
                if (!trustBytes.AsSpan().SequenceEqual(File.ReadAllBytes(trustPath)))
                    throw new InvalidOperationException("Trust file changed during registration. No trust repair was attempted.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = apply ? "registered" : "preflight", assemblyPath, liveAssemblySha256 = liveAssemblyHash,
                registryPath, registryBeforeSha256 = registryHash,
                registryAfterSha256 = Hash(File.ReadAllBytes(registryPath)), trustPath,
                trustBeforeSha256 = Hash(trustBytes), trustAfterSha256 = Hash(File.ReadAllBytes(trustPath)),
                backup, preservedAppIds = existing.Where(item => item.Id != definition.Id).Select(item => item.Id).ToArray(),
                importedAppId = definition.Id, commandReview = AtlasMyNetAppStore.CreateCommandReviewText(definition),
                commandTrusted = apply ? store.IsTrusted(definition) : (bool?)null,
                desktopProfileVerified = true
            }, JsonOptions));
            return 0;
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static JsonNode FindApp(JsonObject document, string id) =>
        ((JsonArray)document["Apps"]!).Single(item => item!["Id"]!.GetValue<string>().Equals(id, StringComparison.OrdinalIgnoreCase))!;
}
