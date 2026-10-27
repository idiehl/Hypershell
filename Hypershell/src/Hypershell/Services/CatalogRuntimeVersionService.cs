using Hypershell.Models;
using System.Diagnostics;
using System.IO;

namespace Hypershell.Services;

public static class CatalogRuntimeVersionService
{
    public static void Apply(IEnumerable<CommandLibrary> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var versions = DiscoverVersions();
        foreach (var library in libraries.Where(library =>
                     library.SchemaVersion == 2))
        {
            var languageId = CommandLanguageRegistry.NormalizeId(
                library.Language);
            if (!versions.TryGetValue(languageId, out var runtimeVersion))
            {
                continue;
            }

            library.ActiveRuntimeVersion = runtimeVersion;
            library.UsesInstalledCompatibleIndex = IsCompatible(
                library,
                runtimeVersion);
        }
    }

    private static IReadOnlyDictionary<string, string> DiscoverVersions()
    {
        var versions = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        AddVersion(
            versions,
            "python",
            TerminalProfileService.ResolveExecutable("python.exe")
            ?? TerminalProfileService.ResolveExecutable("python"));
        AddVersion(
            versions,
            "javascript",
            TerminalProfileService.ResolveExecutable("node.exe")
            ?? TerminalProfileService.ResolveExecutable("node"),
            "Node.js");
        if (versions.TryGetValue("javascript", out var nodeVersion))
        {
            versions["typescript"] = nodeVersion;
        }

        AddVersion(
            versions,
            "java",
            TerminalProfileService.ResolveExecutable("javac.exe")
            ?? TerminalProfileService.ResolveExecutable("javac"),
            "JDK");
        var cppCompiler = TerminalProfileService.ResolveExecutable("clang++.exe")
            ?? TerminalProfileService.ResolveExecutable("clang++")
            ?? TerminalProfileService.ResolveExecutable("g++.exe")
            ?? TerminalProfileService.ResolveExecutable("g++")
            ?? TerminalProfileService.ResolveExecutable("cl.exe")
            ?? TerminalProfileService.ResolveExecutable("cl");
        AddVersion(
            versions,
            "cpp",
            cppCompiler,
            cppCompiler is null
                ? "C++ compiler"
                : Path.GetFileNameWithoutExtension(cppCompiler));
        return versions;
    }

    private static void AddVersion(
        IDictionary<string, string> versions,
        string languageId,
        string? executablePath,
        string prefix = "")
    {
        if (string.IsNullOrWhiteSpace(executablePath)
            || !File.Exists(executablePath))
        {
            return;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath!);
            var version = info.ProductVersion ?? info.FileVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                return;
            }

            var normalized = version.Split(
                [' ', '+'],
                StringSplitOptions.RemoveEmptyEntries)[0];
            versions[languageId] = string.IsNullOrWhiteSpace(prefix)
                ? normalized
                : $"{prefix} {normalized}";
        }
        catch
        {
            // Version labels are informational. Runtime/toolchain availability
            // remains owned by the existing terminal and language-tool checks.
        }
    }

    private static bool IsCompatible(
        CommandLibrary library,
        string runtimeVersion)
    {
        var target = library.TargetVersion;
        var numericRuntime = new string(runtimeVersion
            .SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character == '.')
            .ToArray());
        if (library.Id is "python-core" or "python-standard-library")
        {
            return target.Contains("3.14", StringComparison.OrdinalIgnoreCase)
                   && numericRuntime.StartsWith(
                       "3.14",
                       StringComparison.OrdinalIgnoreCase);
        }

        if (library.Id == "nodejs-standard-library")
        {
            return target.Contains("24", StringComparison.OrdinalIgnoreCase)
                   && numericRuntime.StartsWith(
                       "24.",
                       StringComparison.OrdinalIgnoreCase);
        }

        if (library.Id is "java-core" or "java-se-standard-library")
        {
            return int.TryParse(
                       numericRuntime.Split('.')[0],
                       out var major)
                   && major >= 25;
        }

        return library.Id is "cpp-core" or "cpp-standard-library"
               && !string.IsNullOrWhiteSpace(runtimeVersion);
    }
}
