using Hypershell.Models;
using System.IO;
using System.Text.Json;

namespace Hypershell.Services;

/// <summary>Discovers the portable library without storing machine-specific paths in it.</summary>
public static class HyperlibMyNetRegistration
{
    public const string AppId = "atlas.mynet.hyperlib";

    public static AtlasMyNetAppDefinition? Discover()
    {
        var configured = Environment.GetEnvironmentVariable("HYPERLIB_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Create(configured);
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                var found = Create(Path.Combine(directory.FullName, "Hyperlib"));
                if (found is not null) return found;
            }
        }
        return null;
    }

    public static AtlasMyNetAppDefinition? Create(string root) => CreateCore(root, useHostTarget: true);

    private static AtlasMyNetAppDefinition? CreateCore(string root, bool useHostTarget)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) return null;
        string? executable = null;
        var pointer = Path.Combine(root, "app", "host-target.json");
        if (useHostTarget && HasPathEntry(pointer))
        {
            // An installed pointer is authoritative. A damaged or unsafe target must not launch an older host silently.
            executable = ReadHostTarget(root, pointer);
            if (executable is null) return null;
        }
        else executable = new[] { "app/Hyperlib.Host.exe", "Hyperlib.Host.exe", "Host/bin/Release/net10.0/Hyperlib.Host.exe" }
            .Select(path => Path.Combine(root, path)).FirstOrDefault(File.Exists);
        var arguments = new List<string>();
        if (executable is null)
        {
            var project = new[] { "src/HyperLib.Host/HyperLib.Host.csproj", "Host/Hyperlib.Host.csproj", "Hyperlib.Host/Hyperlib.Host.csproj" }
                .Select(path => Path.Combine(root, path)).FirstOrDefault(File.Exists);
            if (project is null) return null;
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (!File.Exists(executable)) return null;
            arguments.AddRange(["run", "--project", project, "--no-launch-profile", "--"]);
        }
        arguments.AddRange(["--root", root, "--port", "{port}"]);
        return new AtlasMyNetAppDefinition
        {
            Id = AppId, DisplayName = "HyperLib", Ports = [.. Enumerable.Range(5267, 10)],
            UrlTemplate = "http://127.0.0.1:{port}/", AllowExternalResources = false,
            Launch = new AtlasMyNetLaunchDefinition { Executable = executable, WorkingDirectory = root, Arguments = arguments },
            HealthCheck = new AtlasMyNetHealthCheckDefinition
            { Path = "/api/health", HeaderName = "X-HyperLib-App", HeaderValue = "hyperlib", ExpectedStatusCode = 200, TimeoutSeconds = 90 },
            Stop = new AtlasMyNetStopDefinition { Mode = AtlasMyNetStopMode.ManagedProcess }
        };
    }

    public static string? GetTrustedRoot(AtlasMyNetAppDefinition definition)
    {
        if (definition.Id != AppId || definition.Launch is not { } launch) return null;
        var expected = Create(launch.WorkingDirectory);
        // Web access controls public resources and links, not the identity of the local host.
        // The native bridge separately enforces the launched loopback origin and session.
        return expected is not null && SameLaunch(definition, expected)
            && definition.UrlTemplate == expected.UrlTemplate
            && definition.Ports.SequenceEqual(expected.Ports)
            && definition.HealthCheck is { Path: "/api/health", HeaderName: "X-HyperLib-App", HeaderValue: "hyperlib", ExpectedStatusCode: 200 }
            ? Path.GetFullPath(launch.WorkingDirectory) : null;
    }

    public static bool SameLaunch(AtlasMyNetAppDefinition left, AtlasMyNetAppDefinition right) =>
        JsonSerializer.Serialize(left.Launch) == JsonSerializer.Serialize(right.Launch)
        && JsonSerializer.Serialize(left.Stop) == JsonSerializer.Serialize(right.Stop);

    /// <summary>Recognizes an already-trusted built-in registration when only its installed host release changed.</summary>
    public static bool IsPreviousBuiltInLaunch(AtlasMyNetAppDefinition previous, AtlasMyNetAppDefinition current)
    {
        if (previous.Id != AppId || previous.Launch is not { } launch || GetTrustedRoot(current) is not { } root
            || previous.UrlTemplate != current.UrlTemplate || !previous.Ports.SequenceEqual(current.Ports)
            || JsonSerializer.Serialize(previous.HealthCheck) != JsonSerializer.Serialize(current.HealthCheck)) return false;
        try
        {
            if (CreateCore(root, useHostTarget: false) is { } legacy && SameLaunch(previous, legacy)) return true;
            var oldExecutable = Path.GetFullPath(launch.Executable);
            var appRoot = Path.Combine(root, "app") + Path.DirectorySeparatorChar;
            if (!oldExecutable.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(oldExecutable), "HyperLib.Host.exe", StringComparison.OrdinalIgnoreCase)) return false;
            RejectLinks(oldExecutable);
            var expected = current.Clone();
            expected.Launch!.Executable = launch.Executable;
            return SameLaunch(previous, expected);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>Do not adopt an older installed Host merely because it answers the same health check.</summary>
    public static bool MatchesRegisteredHostExecutable(AtlasMyNetAppDefinition definition, string? executablePath)
    {
        if (GetTrustedRoot(definition) is not { } root || definition.Launch is not { } launch || string.IsNullOrWhiteSpace(executablePath)) return false;
        try
        {
            var actual = Path.GetFullPath(executablePath);
            if (string.Equals(actual, Path.GetFullPath(launch.Executable), StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(Path.GetFileName(launch.Executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
            // dotnet run may expose its compiled child as the socket owner.
            var projectIndex = launch.Arguments.IndexOf("--project");
            if (projectIndex < 0 || projectIndex + 1 >= launch.Arguments.Count) return false;
            var project = Path.GetFullPath(launch.Arguments[projectIndex + 1]);
            var output = Path.Combine(Path.GetDirectoryName(project)!, "bin") + Path.DirectorySeparatorChar;
            return actual.StartsWith(output, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(actual), "HyperLib.Host.exe", StringComparison.OrdinalIgnoreCase)
                && actual.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private static string? ReadHostTarget(string root, string pointer)
    {
        try
        {
            RejectLinks(pointer);
            if (!File.Exists(pointer) || new FileInfo(pointer).Length > 16 * 1024) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(pointer), new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("relativeExecutable", out var entry) || entry.ValueKind != JsonValueKind.String) return null;
            var relative = entry.GetString()!;
            var parts = relative.Replace('\\', '/').Split('/');
            if (relative.Length is 0 or > 400 || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Any(char.IsControl)
                || parts.Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' '))
                || !string.Equals(parts[^1], "HyperLib.Host.exe", StringComparison.OrdinalIgnoreCase)) return null;
            var appRoot = Path.GetFullPath(Path.Combine(root, "app")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(appRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase)) return null;
            RejectLinks(target);
            return File.Exists(target) ? target : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException) { return null; }
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Linked HyperLib host targets are not supported.");
        }
    }

    private static bool HasPathEntry(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public static string ContainedProject(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The project is outside this library.");
        for (var current = new DirectoryInfo(path); current is not null && current.FullName.Length >= root.Length; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new UnauthorizedAccessException("Linked project directories are not supported by the HyperLib bridge.");
        return path;
    }
}
