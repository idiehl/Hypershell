using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed partial class CSharpFrameworkCatalogService
{
    public const string AspNetCoreLibraryId = "csharp-aspnet-core-10";
    public const string WinRtLibraryId = "csharp-winrt";
    public const string WindowsAppSdkLibraryId = "csharp-windows-app-sdk";
    public const string WinUi3LibraryId = "csharp-winui-3";
    public const string WpfLibraryId = "csharp-wpf-10";
    public const string WindowsFormsLibraryId = "csharp-windows-forms-10";
    public const string PowerShellSdkLibraryId = "csharp-powershell-sdk";
    public const string WmiCimLibraryId = "csharp-wmi-cim";
    public const string MicrosoftExtensionsLibraryId =
        "csharp-microsoft-extensions";
    public const string RoslynLibraryId = "csharp-roslyn";
    public const string MsBuildLibraryId = "csharp-msbuild";
    public const string NuGetClientLibraryId = "csharp-nuget-client-sdk";
    public const string WebView2LibraryId = "csharp-webview2";
    public const string SystemCommandLineLibraryId =
        "csharp-system-commandline";
    public const string WcfLibraryId = "csharp-wcf";
    public const string WorkflowFoundationLibraryId =
        "csharp-workflow-foundation";
    public const string MefLibraryId = "csharp-mef";
    public const string TplDataflowLibraryId = "csharp-tpl-dataflow";
    public const string CommunityToolkitMvvmLibraryId =
        "csharp-communitytoolkit-mvvm";

    private static void DiscoverInstalledOfficialSurfaces(
        ICollection<CommandLibrary> libraries)
    {
        var dotNet10 = libraries.FirstOrDefault(library =>
            string.Equals(
                library.Id,
                DotNet10LibraryId,
                StringComparison.OrdinalIgnoreCase));
        var bclResolver = dotNet10?.ResolverAssemblyPaths ?? [];
        var dotnetRoot = ResolveDotNetRoot();

        DiscoverSharedFrameworkSurfaces(
            libraries,
            dotnetRoot,
            bclResolver);
        DiscoverWindowsAndPackageSurfaces(
            libraries,
            bclResolver,
            dotNet10?.TargetFramework ?? "net10.0");
        DiscoverSdkComponentSurfaces(
            libraries,
            dotnetRoot,
            bclResolver);
    }

    private static void DiscoverSharedFrameworkSurfaces(
        ICollection<CommandLibrary> libraries,
        string? dotnetRoot,
        IReadOnlyCollection<string> bclResolver)
    {
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return;
        }

        var aspNetDirectory = ResolveLatestPackReferenceDirectory(
            dotnetRoot,
            "Microsoft.AspNetCore.App.Ref",
            "net10.0",
            10);
        if (aspNetDirectory is not null)
        {
            var aspNetAssemblies = EnumerateAssemblies(
                aspNetDirectory,
                SearchOption.TopDirectoryOnly);
            var aspNetVersion = GetPackVersion(aspNetDirectory);
            AddManagedSurface(
                libraries,
                AspNetCoreLibraryId,
                "ASP.NET Core 10",
                "ASP.NET Core",
                aspNetVersion,
                "net10.0",
                1600,
                aspNetDirectory,
                aspNetAssemblies,
                CombineResolverPaths(aspNetAssemblies, bclResolver));

            var extensionsAssemblies = aspNetAssemblies
                .Where(path => Path.GetFileName(path).StartsWith(
                    "Microsoft.Extensions.",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            AddManagedSurface(
                libraries,
                MicrosoftExtensionsLibraryId,
                "Microsoft.Extensions",
                "Microsoft.Extensions",
                aspNetVersion,
                "net10.0",
                1200,
                aspNetDirectory,
                extensionsAssemblies,
                CombineResolverPaths(aspNetAssemblies, bclResolver));
        }

        var desktopDirectory = ResolveLatestPackReferenceDirectory(
            dotnetRoot,
            "Microsoft.WindowsDesktop.App.Ref",
            "net10.0",
            10);
        if (desktopDirectory is null)
        {
            return;
        }

        var desktopAssemblies = EnumerateAssemblies(
            desktopDirectory,
            SearchOption.TopDirectoryOnly);
        var desktopVersion = GetPackVersion(desktopDirectory);
        var wpfAssemblies = desktopAssemblies
            .Where(IsWpfAssembly)
            .ToList();
        AddManagedSurface(
            libraries,
            WpfLibraryId,
            "WPF 10",
            "Windows Presentation Foundation",
            desktopVersion,
            "net10.0-windows",
            700,
            desktopDirectory,
            wpfAssemblies,
            CombineResolverPaths(desktopAssemblies, bclResolver));

        var windowsFormsAssemblies = desktopAssemblies
            .Where(IsWindowsFormsAssembly)
            .ToList();
        AddManagedSurface(
            libraries,
            WindowsFormsLibraryId,
            "Windows Forms 10",
            "Windows Forms",
            desktopVersion,
            "net10.0-windows",
            800,
            desktopDirectory,
            windowsFormsAssemblies,
            CombineResolverPaths(desktopAssemblies, bclResolver));
    }

    private static void DiscoverWindowsAndPackageSurfaces(
        ICollection<CommandLibrary> libraries,
        IReadOnlyCollection<string> bclResolver,
        string defaultTargetFramework)
    {
        var runtimeResolver = EnumerateAssemblies(
            AppContext.BaseDirectory,
            SearchOption.TopDirectoryOnly);

        var winRtPackage = ResolveLatestPackageVersionDirectory(
            "microsoft.windows.sdk.net.ref");
        var winRtDirectory = ResolveAssetDirectory(
            winRtPackage,
            "lib",
            "net10.0",
            "net9.0",
            "net8.0",
            "net6.0");
        var winRtAssemblies = winRtDirectory is null
            ? []
            : EnumerateAssemblies(
                winRtDirectory,
                SearchOption.TopDirectoryOnly);
        if (winRtDirectory is not null)
        {
            AddManagedSurface(
                libraries,
                WinRtLibraryId,
                "Windows Runtime",
                "WinRT",
                GetPackageVersion(winRtPackage),
                GetTargetFramework(winRtDirectory, defaultTargetFramework),
                300,
                winRtDirectory,
                winRtAssemblies,
                CombineResolverPaths(
                    winRtAssemblies,
                    bclResolver,
                    runtimeResolver));
        }

        var windowsAppPackage = ResolveLatestPackageVersionDirectory(
            "microsoft.windowsappsdk");
        var windowsAppDirectory = ResolveBestNetAssetDirectory(
            windowsAppPackage,
            "lib");
        if (windowsAppDirectory is not null)
        {
            var windowsAppAssemblies = EnumerateAssemblies(
                windowsAppDirectory,
                SearchOption.TopDirectoryOnly);
            var windowsAppResolver = CombineResolverPaths(
                windowsAppAssemblies,
                winRtAssemblies,
                bclResolver,
                runtimeResolver);
            var packageVersion = GetPackageVersion(windowsAppPackage);
            var targetFramework = GetTargetFramework(
                windowsAppDirectory,
                defaultTargetFramework);
            AddManagedSurface(
                libraries,
                WindowsAppSdkLibraryId,
                "Windows App SDK",
                "Windows App SDK",
                packageVersion,
                targetFramework,
                500,
                windowsAppDirectory,
                windowsAppAssemblies,
                windowsAppResolver);
            AddManagedSurface(
                libraries,
                WinUi3LibraryId,
                "WinUI 3",
                "WinUI 3",
                packageVersion,
                targetFramework,
                600,
                windowsAppDirectory,
                windowsAppAssemblies.Where(path => string.Equals(
                    Path.GetFileName(path),
                    "Microsoft.WinUI.dll",
                    StringComparison.OrdinalIgnoreCase)),
                windowsAppResolver);
        }

        DiscoverPowerShellAndManagementSurfaces(
            libraries,
            bclResolver,
            runtimeResolver,
            defaultTargetFramework);
        DiscoverGeneralPackageSurfaces(
            libraries,
            bclResolver,
            runtimeResolver,
            defaultTargetFramework);
    }

    private static void DiscoverPowerShellAndManagementSurfaces(
        ICollection<CommandLibrary> libraries,
        IReadOnlyCollection<string> bclResolver,
        IReadOnlyCollection<string> runtimeResolver,
        string defaultTargetFramework)
    {
        var automationPackage = ResolveLatestPackageVersionDirectory(
            "system.management.automation");
        var automationDirectory = ResolveAssetDirectory(
            automationPackage,
            "ref",
            "net10.0",
            "net9.0",
            "net8.0");
        var automationAssemblies = automationDirectory is null
            ? []
            : EnumerateAssemblies(
                automationDirectory,
                SearchOption.TopDirectoryOnly);
        if (automationDirectory is not null)
        {
            AddManagedSurface(
                libraries,
                PowerShellSdkLibraryId,
                "PowerShell SDK",
                "PowerShell SDK",
                GetPackageVersion(automationPackage),
                GetTargetFramework(
                    automationDirectory,
                    defaultTargetFramework),
                400,
                automationDirectory,
                automationAssemblies,
                CombineResolverPaths(
                    automationAssemblies,
                    bclResolver,
                    runtimeResolver));
        }

        var managementPackage = ResolveLatestPackageVersionDirectory(
            "system.management");
        var managementDirectory = ResolveAssetDirectory(
            managementPackage,
            "lib",
            "net10.0",
            "net9.0",
            "net8.0",
            "net7.0",
            "net6.0");
        var managementAssemblies = managementDirectory is null
            ? []
            : EnumerateAssemblies(
                managementDirectory,
                SearchOption.TopDirectoryOnly);

        var cimPackage = ResolveLatestPackageVersionDirectory(
            "microsoft.management.infrastructure");
        var cimDirectory = ResolveAssetDirectory(
            cimPackage,
            "ref",
            "net8.0",
            "net6.0",
            "netstandard2.0",
            "netstandard1.6",
            "net451");
        var cimAssemblies = cimDirectory is null
            ? []
            : EnumerateAssemblies(cimDirectory, SearchOption.TopDirectoryOnly);
        var managementResolver = CombineResolverPaths(
            managementAssemblies,
            cimAssemblies,
            automationAssemblies,
            bclResolver,
            runtimeResolver);
        AddManagedSurface(
            libraries,
            WmiCimLibraryId,
            "WMI and CIM",
            "WMI and CIM",
            GetPackageVersion(managementPackage)
            ?? GetPackageVersion(cimPackage),
            managementDirectory is not null
                ? GetTargetFramework(
                    managementDirectory,
                    defaultTargetFramework)
                : defaultTargetFramework,
            1100,
            managementDirectory ?? cimDirectory ?? string.Empty,
            managementAssemblies.Concat(cimAssemblies),
            managementResolver);
    }

    private static void DiscoverGeneralPackageSurfaces(
        ICollection<CommandLibrary> libraries,
        IReadOnlyCollection<string> bclResolver,
        IReadOnlyCollection<string> runtimeResolver,
        string defaultTargetFramework)
    {
        var webViewPackage = ResolveLatestPackageVersionDirectory(
            "microsoft.web.webview2");
        var webViewDirectory = ResolveAssetDirectory(
            webViewPackage,
            "lib",
            "net10.0-windows",
            "net8.0-windows",
            "net6.0-windows",
            "net462");
        if (webViewDirectory is not null)
        {
            var webViewAssemblies = EnumerateAssemblies(
                webViewDirectory,
                SearchOption.TopDirectoryOnly);
            var frameworkResolver = ResolveLatestDotNetFrameworkResolver();
            AddManagedSurface(
                libraries,
                WebView2LibraryId,
                "WebView2",
                "WebView2",
                GetPackageVersion(webViewPackage),
                GetTargetFramework(
                    webViewDirectory,
                    defaultTargetFramework),
                1700,
                webViewDirectory,
                webViewAssemblies,
                CombineResolverPaths(
                    webViewAssemblies,
                    frameworkResolver,
                    bclResolver,
                    runtimeResolver),
                webViewDirectory.EndsWith(
                    "net462",
                    StringComparison.OrdinalIgnoreCase)
                    ? "mscorlib"
                    : "System.Runtime");
        }

        var mefPackage = ResolveLatestPackageVersionDirectory(
            "system.componentmodel.composition");
        var mefDirectory = ResolveAssetDirectory(
            mefPackage,
            "lib",
            "net10.0",
            "net9.0",
            "net8.0",
            "net7.0",
            "net6.0");
        if (mefDirectory is not null)
        {
            var assemblies = EnumerateAssemblies(
                mefDirectory,
                SearchOption.TopDirectoryOnly);
            AddManagedSurface(
                libraries,
                MefLibraryId,
                "MEF",
                "Managed Extensibility Framework",
                GetPackageVersion(mefPackage),
                GetTargetFramework(mefDirectory, defaultTargetFramework),
                3600,
                mefDirectory,
                assemblies,
                CombineResolverPaths(
                    assemblies,
                    bclResolver,
                    runtimeResolver));
        }

        var toolkitPackage = ResolveLatestPackageVersionDirectory(
            "communitytoolkit.mvvm");
        var toolkitDirectory = ResolveAssetDirectory(
            toolkitPackage,
            "lib",
            "net10.0",
            "net9.0",
            "net8.0-windows10.0.17763",
            "net8.0",
            "net6.0");
        if (toolkitDirectory is not null)
        {
            var assemblies = EnumerateAssemblies(
                toolkitDirectory,
                SearchOption.TopDirectoryOnly);
            AddManagedSurface(
                libraries,
                CommunityToolkitMvvmLibraryId,
                "CommunityToolkit.Mvvm",
                "CommunityToolkit.Mvvm",
                GetPackageVersion(toolkitPackage),
                GetTargetFramework(toolkitDirectory, defaultTargetFramework),
                3800,
                toolkitDirectory,
                assemblies,
                CombineResolverPaths(
                    assemblies,
                    bclResolver,
                    runtimeResolver));
        }

        var dataflowAssembly = bclResolver.FirstOrDefault(path =>
            string.Equals(
                Path.GetFileName(path),
                "System.Threading.Tasks.Dataflow.dll",
                StringComparison.OrdinalIgnoreCase));
        if (dataflowAssembly is not null)
        {
            AddManagedSurface(
                libraries,
                TplDataflowLibraryId,
                "TPL Dataflow",
                "TPL Dataflow",
                Environment.Version.ToString(),
                defaultTargetFramework,
                3700,
                Path.GetDirectoryName(dataflowAssembly) ?? string.Empty,
                [dataflowAssembly],
                bclResolver);
        }

        DiscoverWcfSurface(
            libraries,
            bclResolver,
            runtimeResolver,
            defaultTargetFramework);
        DiscoverWorkflowFoundationSurface(libraries);
    }

    private static void DiscoverWcfSurface(
        ICollection<CommandLibrary> libraries,
        IReadOnlyCollection<string> bclResolver,
        IReadOnlyCollection<string> runtimeResolver,
        string defaultTargetFramework)
    {
        var classicFramework = DiscoverLatestDotNetFramework();
        var classicAssemblies = classicFramework?.ApiAssemblyPaths
            .Where(path => Path.GetFileName(path).StartsWith(
                "System.ServiceModel",
                StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        if (classicFramework is not null && classicAssemblies.Count > 0)
        {
            AddManagedSurface(
                libraries,
                WcfLibraryId,
                "Windows Communication Foundation",
                "WCF",
                classicFramework.FrameworkVersion,
                classicFramework.TargetFramework,
                3400,
                classicFramework.SourcePath,
                classicAssemblies,
                classicFramework.ResolverAssemblyPaths,
                "mscorlib");
            return;
        }

        var packageIds = new[]
        {
            "system.servicemodel.primitives",
            "system.servicemodel.http",
            "system.servicemodel.duplex",
            "system.servicemodel.nettcp",
            "system.servicemodel.security",
            "system.servicemodel.syndication"
        };
        var packageRoots = packageIds
            .Select(ResolveLatestPackageVersionDirectory)
            .Where(path => path is not null)
            .Cast<string>()
            .ToList();
        var assetDirectories = packageRoots
            .Select(path => ResolveAssetDirectory(
                path,
                "ref",
                "net10.0",
                "net9.0",
                "net8.0",
                "net7.0",
                "net6.0")
                ?? ResolveAssetDirectory(
                    path,
                    "lib",
                    "net10.0",
                    "net9.0",
                    "net8.0",
                    "net7.0",
                    "net6.0"))
            .Where(path => path is not null)
            .Cast<string>()
            .ToList();
        var assemblies = assetDirectories
            .SelectMany(path => EnumerateAssemblies(
                path,
                SearchOption.TopDirectoryOnly))
            .ToList();
        AddManagedSurface(
            libraries,
            WcfLibraryId,
            "Windows Communication Foundation",
            "WCF",
            packageRoots.Select(GetPackageVersion)
                .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version)),
            assetDirectories.Count > 0
                ? GetTargetFramework(
                    assetDirectories[0],
                    defaultTargetFramework)
                : defaultTargetFramework,
            3400,
            assetDirectories.FirstOrDefault() ?? string.Empty,
            assemblies,
            CombineResolverPaths(
                assemblies,
                bclResolver,
                runtimeResolver));
    }

    private static void DiscoverWorkflowFoundationSurface(
        ICollection<CommandLibrary> libraries)
    {
        var classicFramework = DiscoverLatestDotNetFramework();
        if (classicFramework is null)
        {
            return;
        }

        var assemblies = classicFramework.ApiAssemblyPaths
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return name.Contains(
                           "Activities",
                           StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith(
                           "System.Workflow",
                           StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        AddManagedSurface(
            libraries,
            WorkflowFoundationLibraryId,
            "Windows Workflow Foundation",
            "Windows Workflow Foundation",
            classicFramework.FrameworkVersion,
            classicFramework.TargetFramework,
            3500,
            classicFramework.SourcePath,
            assemblies,
            classicFramework.ResolverAssemblyPaths,
            "mscorlib");
    }

    private static void DiscoverSdkComponentSurfaces(
        ICollection<CommandLibrary> libraries,
        string? dotnetRoot,
        IReadOnlyCollection<string> bclResolver)
    {
        var sdkDirectory = ResolveLatestSdkDirectory(dotnetRoot, 10);
        if (sdkDirectory is null)
        {
            return;
        }

        var sdkAssemblies = EnumerateAssemblies(
            sdkDirectory,
            SearchOption.TopDirectoryOnly);
        var sdkVersion = new DirectoryInfo(sdkDirectory).Name;
        var sdkResolver = CombineResolverPaths(sdkAssemblies, bclResolver);
        AddManagedSurface(
            libraries,
            MsBuildLibraryId,
            "MSBuild",
            "MSBuild",
            sdkVersion,
            "net10.0",
            1400,
            sdkDirectory,
            sdkAssemblies.Where(path => Path.GetFileName(path).StartsWith(
                "Microsoft.Build",
                StringComparison.OrdinalIgnoreCase)),
            sdkResolver);
        AddManagedSurface(
            libraries,
            NuGetClientLibraryId,
            "NuGet Client SDK",
            "NuGet Client SDK",
            sdkVersion,
            "net10.0",
            1500,
            sdkDirectory,
            sdkAssemblies.Where(path => Path.GetFileName(path).StartsWith(
                "NuGet.",
                StringComparison.OrdinalIgnoreCase)),
            sdkResolver);
        AddManagedSurface(
            libraries,
            SystemCommandLineLibraryId,
            "System.CommandLine",
            "System.CommandLine",
            sdkVersion,
            "net10.0",
            1900,
            sdkDirectory,
            sdkAssemblies.Where(path => Path.GetFileName(path).StartsWith(
                "System.CommandLine",
                StringComparison.OrdinalIgnoreCase)),
            sdkResolver);

        var roslynDirectory = Path.Combine(
            sdkDirectory,
            "Roslyn",
            "bincore");
        if (Directory.Exists(roslynDirectory))
        {
            var roslynResolver = EnumerateAssemblies(
                roslynDirectory,
                SearchOption.TopDirectoryOnly);
            AddManagedSurface(
                libraries,
                RoslynLibraryId,
                "Roslyn",
                "Roslyn",
                sdkVersion,
                "net10.0",
                1300,
                roslynDirectory,
                roslynResolver.Where(path => Path.GetFileName(path).StartsWith(
                    "Microsoft.CodeAnalysis",
                    StringComparison.OrdinalIgnoreCase)),
                CombineResolverPaths(
                    roslynResolver,
                    sdkAssemblies,
                    bclResolver));
        }
    }

    private static void AddManagedSurface(
        ICollection<CommandLibrary> libraries,
        string id,
        string displayName,
        string frameworkName,
        string? version,
        string targetFramework,
        int priority,
        string sourcePath,
        IEnumerable<string> apiAssemblyPaths,
        IEnumerable<string> resolverAssemblyPaths,
        string metadataCoreAssemblyName = "System.Runtime")
    {
        if (libraries.Any(library => string.Equals(
                library.Id,
                id,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var primaryAssemblies = apiAssemblyPaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (primaryAssemblies.Count == 0)
        {
            return;
        }

        var resolvedVersion = string.IsNullOrWhiteSpace(version)
            ? "installed"
            : version;
        libraries.Add(new CommandLibrary
        {
            Id = id,
            DisplayName = displayName,
            Description = $"Installed {displayName} managed API surface.",
            Language = "C#",
            Category = string.Empty,
            Kind = CommandLibraryKind.Framework,
            FrameworkName = frameworkName,
            FrameworkVersion = resolvedVersion,
            TargetFramework = targetFramework,
            ContentVersion = resolvedVersion,
            IsStarterLibrary = false,
            CreateStandaloneProfile = false,
            Keywords =
            [
                "csharp",
                frameworkName,
                displayName,
                "managed api"
            ],
            ExecutionTarget = ExecutionTarget.CSharp,
            IsExecutionSupported = false,
            ExecutionSupportMessage =
                "Class catalog available. This surface needs its framework or " +
                "package reference added to the C# runner before execution.",
            ReferenceAssemblyCount = primaryAssemblies.Count,
            Origin = CommandLibraryOrigin.Runtime,
            SourcePath = sourcePath,
            ApiAssemblyPaths = primaryAssemblies,
            ResolverAssemblyPaths = resolverAssemblyPaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MetadataCoreAssemblyName = metadataCoreAssemblyName,
            CatalogPriority = priority
        });
    }

    private static List<string> CombineResolverPaths(
        params IEnumerable<string>[] pathGroups) =>
        pathGroups
            .SelectMany(paths => paths)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? ResolveDotNetRoot()
    {
        var runtimeDirectory = Path.GetDirectoryName(
            typeof(object).Assembly.Location);
        return string.IsNullOrWhiteSpace(runtimeDirectory)
            ? null
            : Directory.GetParent(runtimeDirectory)
                ?.Parent
                ?.Parent
                ?.FullName;
    }

    private static string? ResolveLatestPackReferenceDirectory(
        string dotnetRoot,
        string packName,
        string targetFramework,
        int majorVersion)
    {
        var packRoot = Path.Combine(dotnetRoot, "packs", packName);
        if (!Directory.Exists(packRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(packRoot)
            .Select(path => new
            {
                Path = path,
                Version = ParseVersion(new DirectoryInfo(path).Name)
            })
            .Where(item => item.Version?.Major == majorVersion)
            .OrderByDescending(item => item.Version)
            .Select(item => Path.Combine(
                item.Path,
                "ref",
                targetFramework))
            .FirstOrDefault(Directory.Exists);
    }

    private static string GetPackVersion(string referenceDirectory) =>
        Directory.GetParent(referenceDirectory)?.Parent?.Name ?? "installed";

    private static string? ResolveLatestSdkDirectory(
        string? dotnetRoot,
        int majorVersion)
    {
        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            return null;
        }

        var sdkRoot = Path.Combine(dotnetRoot, "sdk");
        return Directory.Exists(sdkRoot)
            ? Directory.EnumerateDirectories(sdkRoot)
                .Select(path => new
                {
                    Path = path,
                    Version = ParseVersion(new DirectoryInfo(path).Name)
                })
                .Where(item => item.Version?.Major == majorVersion)
                .OrderByDescending(item => item.Version)
                .Select(item => item.Path)
                .FirstOrDefault()
            : null;
    }

    private static string? ResolveLatestPackageVersionDirectory(
        string packageId)
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var versionRoot = Path.Combine(
            packageRoot,
            packageId.ToLowerInvariant());
        if (!Directory.Exists(versionRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(versionRoot)
            .Select(path => new
            {
                Path = path,
                Version = ParseVersion(new DirectoryInfo(path).Name)
            })
            .OrderByDescending(item => item.Version)
            .ThenByDescending(
                item => item.Path,
                StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .FirstOrDefault();
    }

    private static Version? ParseVersion(string value)
    {
        var numeric = value.Split('-', 2)[0];
        return Version.TryParse(numeric, out var version)
            ? version
            : null;
    }

    private static string? ResolveAssetDirectory(
        string? packageVersionDirectory,
        string assetKind,
        params string[] preferredFrameworks)
    {
        if (string.IsNullOrWhiteSpace(packageVersionDirectory))
        {
            return null;
        }

        var assetRoot = Path.Combine(packageVersionDirectory, assetKind);
        if (!Directory.Exists(assetRoot))
        {
            return null;
        }

        foreach (var framework in preferredFrameworks)
        {
            var candidate = Path.Combine(assetRoot, framework);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return ResolveBestNetAssetDirectory(
            packageVersionDirectory,
            assetKind);
    }

    private static string? ResolveBestNetAssetDirectory(
        string? packageVersionDirectory,
        string assetKind)
    {
        if (string.IsNullOrWhiteSpace(packageVersionDirectory))
        {
            return null;
        }

        var assetRoot = Path.Combine(packageVersionDirectory, assetKind);
        return Directory.Exists(assetRoot)
            ? Directory.EnumerateDirectories(assetRoot)
                .Where(path => new DirectoryInfo(path).Name.StartsWith(
                    "net",
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    path => new DirectoryInfo(path).Name,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
    }

    private static string? GetPackageVersion(
        string? packageVersionDirectory) =>
        string.IsNullOrWhiteSpace(packageVersionDirectory)
            ? null
            : new DirectoryInfo(packageVersionDirectory).Name;

    private static string GetTargetFramework(
        string assetDirectory,
        string fallback) =>
        string.IsNullOrWhiteSpace(assetDirectory)
            ? fallback
            : new DirectoryInfo(assetDirectory).Name;

    private static IReadOnlyCollection<string>
        ResolveLatestDotNetFrameworkResolver()
    {
        var framework = DiscoverLatestDotNetFramework();
        return framework?.ResolverAssemblyPaths ?? [];
    }

    private static bool IsWpfAssembly(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("ReachFramework", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("System.Printing", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("System.Windows.", StringComparison.OrdinalIgnoreCase)
                  && !name.StartsWith(
                      "System.Windows.Forms",
                      StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   name,
                   "WindowsBase",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsFormsAssembly(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(
                   "System.Windows.Forms",
                   StringComparison.OrdinalIgnoreCase)
               || name.StartsWith(
                   "System.Drawing",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   name,
                   "Accessibility",
                   StringComparison.OrdinalIgnoreCase);
    }
}
