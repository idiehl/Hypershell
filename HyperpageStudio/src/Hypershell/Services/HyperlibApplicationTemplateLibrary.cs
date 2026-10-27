using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

public sealed partial class HyperlibWorkProjectService
{
    public const string ApplicationTemplateGroup = "legacy-application-patterns-v1";
    internal static bool IsApplicationTemplateGroup(string id) => id.StartsWith(ApplicationTemplateGroup + "-", StringComparison.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<string>> ApplicationLibraries = new(StringComparer.OrdinalIgnoreCase);

    // New shipped patterns are materialized in an immutable private cache. Listing the catalog never
    // seeds or upgrades a user's source library, and old Work copies remain owned by their Work.
    private string CatalogLibraryRoot(string groupId) => IsApplicationTemplateGroup(groupId) ? ApplicationLibraryRoot() : _sourceRoot;

    private string ApplicationLibraryRoot() => ApplicationLibraries.GetOrAdd(_sourceRoot,
        source => new Lazy<string>(() => Task.Run(async () =>
        {
            var modules = HyperlibLegacyTemplateLibraryService.CreateModules().Where(module => IsApplicationTemplateGroup(module.GroupId)).ToArray();
            var signature = string.Join("\n", modules.Select(module => module.Razor + module.Css + JsonSerializer.Serialize(module.Definition, ContentJson.Options)));
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + signature)))[..16].ToLowerInvariant();
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "Tpl");
            RejectLinks(parent);
            Directory.CreateDirectory(parent);
            var cache = Path.Combine(parent, "v4-" + key);
            var complete = Path.Combine(cache, "catalog.ready");
            var started = DateTime.UtcNow;
            FileStream? gate = null;
            while (gate is null)
            {
                try { gate = new FileStream(Path.Combine(parent, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (DateTime.UtcNow - started < TimeSpan.FromMinutes(2)) { await Task.Delay(100); }
            }
            await using (gate)
            {
                if (File.Exists(complete)) { foreach (var id in modules.Select(module => module.GroupId).Distinct()) _ = TemplateObjectLibrary.GetDesignGroup(cache, id); return cache; }
                var staging = Path.Combine(parent, key + "-staging-" + Guid.NewGuid().ToString("N"));
                await CopyDirectoryAsync(Path.Combine(source, "Starters", "standard"), Path.Combine(staging, "Starters", "standard"), CancellationToken.None);
                await HyperlibLegacyTemplateLibraryService.EnsureLegacyDesignGroupsAsync(staging, onlyGroup: ApplicationTemplateGroup);
                await File.WriteAllTextAsync(Path.Combine(staging, "catalog.ready"), key);
                // Both paths are generated immediate children of the private cache root. Incomplete
                // generations are retained for diagnosis; a crash cannot expose a half-built catalog.
                if (Directory.Exists(cache)) Directory.Move(cache, Path.Combine(parent, key + "-incomplete-" + Guid.NewGuid().ToString("N")));
                Directory.Move(staging, cache);
            }
            return cache;
        }).GetAwaiter().GetResult())).Value;
}
