using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed partial class CSharpFrameworkCatalogService
{
    public const string DotNet8LibraryId = "csharp-dotnet-8";
    public const string DotNet9LibraryId = "csharp-dotnet-9";
    public const string DotNet10LibraryId = "csharp-dotnet-10";
    public const string DotNetFrameworkLibraryId =
        "csharp-dotnet-framework";

    public IReadOnlyList<CommandLibrary> Discover()
    {
        var libraries = new List<CommandLibrary>();
        var dotNet10 = DiscoverDotNet(10, DotNet10LibraryId);
        if (dotNet10 is not null)
        {
            libraries.Add(dotNet10);
        }

        var dotNet9 = DiscoverDotNet(9, DotNet9LibraryId);
        if (dotNet9 is not null)
        {
            libraries.Add(dotNet9);
        }

        var dotNet8 = DiscoverDotNet(8, DotNet8LibraryId);
        if (dotNet8 is not null)
        {
            libraries.Add(dotNet8);
        }

        var dotNetFramework = DiscoverLatestDotNetFramework();
        if (dotNetFramework is not null)
        {
            libraries.Add(dotNetFramework);
        }

        DiscoverInstalledOfficialSurfaces(libraries);

        return libraries
            .OrderBy(library => library.CatalogPriority)
            .ThenBy(library => library.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsIndexedManagedApiLibrary(
        CommandLibrary? library) =>
        library is
        {
            ExecutionTarget: ExecutionTarget.CSharp,
            Kind: CommandLibraryKind.Framework
        }
        && !string.IsNullOrWhiteSpace(library.TargetFramework)
        && (library.ApiAssemblyPaths.Any(File.Exists)
            || (Directory.Exists(library.SourcePath)
                && Directory.EnumerateFiles(
                    library.SourcePath,
                    "*.dll",
                    SearchOption.AllDirectories).Any()));

    public static bool IsIndexedDotNetFramework(
        CommandLibrary? library) => IsIndexedManagedApiLibrary(library);

    private static CommandLibrary? DiscoverDotNet(
        int majorVersion,
        string libraryId)
    {
        var runtimeDirectory = Path.GetDirectoryName(
            typeof(object).Assembly.Location);
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            return null;
        }

        var runtimeVersion = new DirectoryInfo(
            runtimeDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)).Name;
        var dotnetRoot = Directory.GetParent(runtimeDirectory)
            ?.Parent
            ?.Parent
            ?.FullName;
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return null;
        }

        var packRoot = Path.Combine(
            dotnetRoot,
            "packs",
            "Microsoft.NETCore.App.Ref");
        var referenceDirectory = ResolveVersionedReferenceDirectory(
            packRoot,
            runtimeVersion,
            $"net{majorVersion}.0",
            majorVersion);
        if (referenceDirectory is null)
        {
            return null;
        }

        var referencePackVersion = Directory.GetParent(referenceDirectory)
            ?.Parent
            ?.Name
            ?? $"{majorVersion}.0";
        var isCurrentRuntime = Environment.Version.Major == majorVersion;
        var executionSupportMessage = isCurrentRuntime
            ? $"Supported directly by the built-in .NET {majorVersion} C# runner."
            : $"Indexed from the installed .NET {majorVersion} reference pack " +
              $"and executed by the backward-compatible .NET {Environment.Version.Major} runner.";
        return new CommandLibrary
        {
            SchemaVersion = 2,
            Id = libraryId,
            DisplayName = $".NET {majorVersion} Base Class Libraries",
            Description =
                $"Installed .NET {majorVersion} reference surface with isolated " +
                "namespace, type, member, and overload metadata.",
            Language = "C#",
            Kind = CommandLibraryKind.Framework,
            CanonicalName = $"Microsoft.NETCore.App.Ref {referencePackVersion}",
            Ecosystem = ".NET",
            TargetVersion = $".NET {majorVersion}",
            SourceVersion = referencePackVersion,
            SourceUrl = "https://learn.microsoft.com/dotnet/api/",
            License = "Microsoft .NET reference-pack license",
            FrameworkName = ".NET",
            FrameworkVersion = referencePackVersion,
            TargetFramework = $"net{majorVersion}.0",
            ContentVersion = referencePackVersion,
            Category = string.Empty,
            IsStarterLibrary = majorVersion == Environment.Version.Major,
            CreateStandaloneProfile = false,
            Keywords =
            [
                "csharp",
                "dotnet",
                $"net{majorVersion}",
                "framework",
                "reference assemblies"
            ],
            ExecutionTarget = ExecutionTarget.CSharp,
            IsExecutionSupported = true,
            ExecutionSupportMessage = executionSupportMessage,
            ReferenceAssemblyCount = CountReferenceAssemblies(
                referenceDirectory),
            Origin = CommandLibraryOrigin.Runtime,
            SourcePath = referenceDirectory,
            ActiveRuntimeVersion = $".NET {Environment.Version}",
            UsesInstalledCompatibleIndex = true,
            ApiAssemblyPaths = EnumerateAssemblies(
                referenceDirectory,
                SearchOption.TopDirectoryOnly),
            ResolverAssemblyPaths = EnumerateAssemblies(
                referenceDirectory,
                SearchOption.TopDirectoryOnly),
            MetadataCoreAssemblyName = "System.Runtime",
            CatalogPriority = majorVersion switch
            {
                10 => 100,
                9 => 110,
                8 => 120,
                _ => 190
            }
        };
    }

    private static CommandLibrary? DiscoverLatestDotNetFramework()
    {
        var referenceRoot = new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFilesX86),
                    "Reference Assemblies",
                    "Microsoft",
                    "Framework",
                    ".NETFramework"),
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFiles),
                    "Reference Assemblies",
                    "Microsoft",
                    "Framework",
                    ".NETFramework")
            }
            .FirstOrDefault(Directory.Exists);
        if (referenceRoot is null)
        {
            return null;
        }

        var candidate = Directory.EnumerateDirectories(referenceRoot)
            .Select(path => new
            {
                Path = path,
                Label = new DirectoryInfo(path).Name,
                Version = TryParseFrameworkVersion(
                    new DirectoryInfo(path).Name)
            })
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        if (candidate?.Version is null)
        {
            return null;
        }

        var version = candidate.Version;
        var versionLabel = version.Build > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
        return new CommandLibrary
        {
            SchemaVersion = 2,
            Id = DotNetFrameworkLibraryId,
            DisplayName = $".NET Framework {versionLabel} Base Class Libraries",
            Description =
                $"Classic Windows .NET Framework {versionLabel} reference " +
                "surface installed on this machine.",
            Language = "C#",
            Kind = CommandLibraryKind.Framework,
            CanonicalName = $".NET Framework {versionLabel} Reference Assemblies",
            Ecosystem = ".NET Framework",
            TargetVersion = $".NET Framework {versionLabel}",
            SourceVersion = versionLabel,
            SourceUrl = "https://learn.microsoft.com/dotnet/api/",
            License = "Microsoft .NET Framework reference-assembly license",
            FrameworkName = ".NET Framework",
            FrameworkVersion = versionLabel,
            TargetFramework = ToDotNetFrameworkTarget(version),
            ContentVersion = version.Build > 0
                ? versionLabel
                : $"{versionLabel}.0",
            Category = string.Empty,
            IsStarterLibrary = false,
            CreateStandaloneProfile = false,
            Keywords =
            [
                "csharp",
                "dotnet framework",
                "windows",
                "framework",
                "reference assemblies"
            ],
            ExecutionTarget = ExecutionTarget.CSharp,
            IsExecutionSupported = false,
            ExecutionSupportMessage =
                "Reference pack detected. A .NET Framework runner adapter " +
                "must be added before these APIs can execute in Command Builder.",
            ReferenceAssemblyCount = CountReferenceAssemblies(candidate.Path),
            Origin = CommandLibraryOrigin.Runtime,
            SourcePath = candidate.Path,
            ActiveRuntimeVersion = $".NET Framework {versionLabel}",
            UsesInstalledCompatibleIndex = true,
            ApiAssemblyPaths = EnumerateAssemblies(
                candidate.Path,
                SearchOption.TopDirectoryOnly),
            ResolverAssemblyPaths = EnumerateAssemblies(
                candidate.Path,
                SearchOption.AllDirectories),
            MetadataCoreAssemblyName = "mscorlib",
            CatalogPriority = 900
        };
    }

    private static string? ResolveVersionedReferenceDirectory(
        string packRoot,
        string preferredVersion,
        string targetFramework,
        int requiredMajor)
    {
        var exact = Path.Combine(
            packRoot,
            preferredVersion,
            "ref",
            targetFramework);
        if (Directory.Exists(exact))
        {
            return exact;
        }

        if (!Directory.Exists(packRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(packRoot)
            .Select(path => new
            {
                Path = path,
                Version = Version.TryParse(
                    new DirectoryInfo(path).Name,
                    out var version)
                    ? version
                    : null
            })
            .Where(candidate => candidate.Version?.Major == requiredMajor)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => Path.Combine(
                candidate.Path,
                "ref",
                targetFramework))
            .FirstOrDefault(Directory.Exists);
    }

    private static Version? TryParseFrameworkVersion(string label)
    {
        var normalized = label.Trim();
        if (normalized.StartsWith('v'))
        {
            normalized = normalized[1..];
        }

        return Version.TryParse(normalized, out var version)
            ? version
            : null;
    }

    private static string ToDotNetFrameworkTarget(Version version)
    {
        var digits = version.Build > 0
            ? $"{version.Major}{version.Minor}{version.Build}"
            : $"{version.Major}{version.Minor}";
        return $"net{digits}";
    }

    private static int CountReferenceAssemblies(string directory) =>
        Directory.EnumerateFiles(
                directory,
                "*.dll",
                SearchOption.AllDirectories)
            .Count();

    private static List<string> EnumerateAssemblies(
        string directory,
        SearchOption searchOption) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.dll", searchOption)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
}
