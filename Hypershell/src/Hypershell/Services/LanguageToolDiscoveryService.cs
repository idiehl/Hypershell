using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed record LanguageToolExecutableResolution(
    LanguageToolAvailability Availability,
    string ExecutablePath,
    string Message);

public static class LanguageToolExecutableResolver
{
    private static readonly string[] NativeExtensions = [".exe", ".com"];
    private static readonly string[] CommandShimExtensions = [".cmd", ".bat"];

    public static LanguageToolExecutableResolution Resolve(
        IReadOnlyList<string> candidates,
        string? pathValue = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var searchDirectories = ParseSearchDirectories(
            pathValue ?? Environment.GetEnvironmentVariable("PATH"));

        foreach (var candidate in candidates.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var nativePath = FindCandidate(candidate.Trim(), searchDirectories, NativeExtensions);
            if (!string.IsNullOrWhiteSpace(nativePath))
            {
                return new LanguageToolExecutableResolution(
                    LanguageToolAvailability.Available,
                    nativePath,
                    $"Detected {nativePath}");
            }
        }

        foreach (var candidate in candidates.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var shimPath = FindCandidate(candidate.Trim(), searchDirectories, CommandShimExtensions);
            if (!string.IsNullOrWhiteSpace(shimPath))
            {
                return new LanguageToolExecutableResolution(
                    LanguageToolAvailability.UnsupportedCommandShim,
                    shimPath,
                    "A .cmd or .bat wrapper was found, but this host only launches native executables directly. No shell was started.");
            }
        }

        return new LanguageToolExecutableResolution(
            LanguageToolAvailability.Missing,
            string.Empty,
            "No supported native executable was found on PATH.");
    }

    private static string FindCandidate(
        string candidate,
        IReadOnlyList<string> searchDirectories,
        IReadOnlyList<string> allowedExtensions)
    {
        var extension = Path.GetExtension(candidate);
        var names = string.IsNullOrWhiteSpace(extension)
            ? allowedExtensions.Select(item => candidate + item)
            : allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                ? [candidate]
                : [];

        if (Path.IsPathRooted(candidate))
        {
            foreach (var name in names)
            {
                var resolved = TryResolveExistingFile(name);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return string.Empty;
        }

        foreach (var directory in searchDirectories)
        {
            foreach (var name in names)
            {
                string combined;
                try
                {
                    combined = Path.Combine(directory, name);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                var resolved = TryResolveExistingFile(combined);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }
        }

        return string.Empty;
    }

    private static string TryResolveExistingFile(string path)
    {
        try
        {
            return File.Exists(path) ? Path.GetFullPath(path) : string.Empty;
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> ParseSearchDirectories(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return [];
        }

        return pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Trim('"'))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class LanguageToolDiscoveryService
{
    private readonly IReadOnlyList<LanguageToolDefinition> _definitions;

    public LanguageToolDiscoveryService(
        IReadOnlyList<LanguageToolDefinition>? definitions = null)
    {
        _definitions = definitions ?? LanguageToolRegistry.Definitions;
    }

    public LanguageToolCatalogSnapshot Discover(string? pathValue = null)
    {
        var tools = _definitions
            .Select(definition => Discover(definition, pathValue))
            .OrderBy(tool => tool.Definition.Kind)
            .ThenBy(tool => tool.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LanguageToolCatalogSnapshot
        {
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Tools = tools
        };
    }

    private static DiscoveredLanguageTool Discover(
        LanguageToolDefinition definition,
        string? pathValue)
    {
        if (definition.IsHostBuiltIn)
        {
            return new DiscoveredLanguageTool
            {
                Definition = definition,
                Availability = LanguageToolAvailability.Available,
                StatusMessage = "Built into Hypershell; no external process is required."
            };
        }

        var resolution = LanguageToolExecutableResolver.Resolve(
            definition.ExecutableCandidates,
            pathValue);
        return new DiscoveredLanguageTool
        {
            Definition = definition,
            Availability = resolution.Availability,
            ExecutablePath = resolution.ExecutablePath,
            StatusMessage = resolution.Message
        };
    }
}
