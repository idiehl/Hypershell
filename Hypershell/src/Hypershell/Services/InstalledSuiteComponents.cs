using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

/// <summary>Discovers installer-owned payloads without importing development registrations.</summary>
internal static class InstalledSuiteComponents
{
    internal static IReadOnlyList<AtlasMyNetAppDefinition> Discover(string installationRoot, string applicationDataRoot)
    {
        var apps = new List<AtlasMyNetAppDefinition>();
        var components = Path.Combine(Path.GetFullPath(installationRoot), "Components");
        var authoring = Path.Combine(components, "Authoring");
        var studio = Path.Combine(authoring, "studio", "Hypershell.exe");
        if (IsOrdinaryFile(studio))
        {
            apps.Add(new AtlasMyNetAppDefinition
            {
                Id = "atlas.mynet.hyperpage-studio", DisplayName = "Hyperpage Studio", IsNative = true,
                UrlTemplate = string.Empty, ShowNavigationToolbar = false,
                IconPath = Path.Combine(authoring, "studio", "Assets", "Brand", "hypershell.ico"),
                Launch = new AtlasMyNetLaunchDefinition
                {
                    Executable = studio, WorkingDirectory = Path.GetDirectoryName(studio)!,
                    Arguments = ["--hyperpage-studio"],
                    Environment = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["HYPERSHELL_STUDIO_BASE_DATA"] = Path.GetFullPath(applicationDataRoot)
                    }
                },
                Stop = new() { Mode = AtlasMyNetStopMode.ManagedProcess }
            });
        }

        var seed = Path.Combine(authoring, "Hyperlib");
        var hyperlibHost = Path.Combine(seed, "app", "HyperLib.Host.exe");
        if (IsOrdinaryFile(hyperlibHost) && IsOrdinaryFile(Path.Combine(seed, "hyperlib.app.json")))
        {
            var library = Path.Combine(Path.GetFullPath(applicationDataRoot), "Libraries", "Hyperlib");
            SeedLibrary(seed, library);
            apps.Add(new AtlasMyNetAppDefinition
            {
                Id = "atlas.mynet.hyperlib", DisplayName = "Hyperlib", Ports = [54180, 54181, 54182, 54183],
                ShowNavigationToolbar = false,
                Launch = new()
                {
                    Executable = hyperlibHost, WorkingDirectory = library,
                    Arguments = ["--root", library, "--port", "{port}"]
                },
                HealthCheck = new() { Path = "/api/health" },
                Stop = new() { Mode = AtlasMyNetStopMode.ManagedProcess }
            });
        }

        var meridianRoot = Path.Combine(components, "Meridian");
        var meridian = Path.Combine(meridianRoot, "Meridian.Host.exe");
        if (IsOrdinaryFile(meridian))
        {
            apps.Add(new AtlasMyNetAppDefinition
            {
                Id = "atlas.meridian", DisplayName = "Atlas Meridian", Ports = [5240, 5241, 5242, 5243],
                IconPath = Path.Combine(meridianRoot, "assets", "brand", "meridian.ico"),
                ShowNavigationToolbar = false,
                Launch = new()
                {
                    Executable = meridian, WorkingDirectory = meridianRoot,
                    Arguments = ["--urls", "http://127.0.0.1:{port}", "--data-root", Path.Combine(applicationDataRoot, "AtlasMeridian")]
                },
                HealthCheck = new() { Path = "/__meridian_status", HeaderName = "X-Atlas-Meridian", HeaderValue = "true" },
                Stop = new() { Mode = AtlasMyNetStopMode.ManagedProcess }
            });
        }
        return apps;
    }

    private static bool IsOrdinaryFile(string path)
    {
        if (!File.Exists(path)) return false;
        RejectLinks(path);
        return true;
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Installed component paths cannot contain symbolic links or junctions.");
    }

    private static void SeedLibrary(string source, string destination)
    {
        RejectLinks(destination);
        // A library becomes user-owned on first creation. Upgrades never overlay it.
        if (Directory.Exists(destination)) return;
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".hyperlib-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            CopySeed(source, temporary, true);
            foreach (var directory in new[] { "Media", "Works", "Content" })
                Directory.CreateDirectory(Path.Combine(temporary, directory));
            try { Directory.Move(temporary, destination); }
            catch (IOException) when (Directory.Exists(destination)) { }
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static void CopySeed(string source, string destination, bool root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectLinks(entry);
            var name = Path.GetFileName(entry);
            if (root && name.Equals("app", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
                CopySeed(entry, target, false);
            }
            else File.Copy(entry, target, overwrite: false);
        }
    }
}
