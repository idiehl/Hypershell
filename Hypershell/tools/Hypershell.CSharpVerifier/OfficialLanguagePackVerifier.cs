using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Diagnostics;

internal static class OfficialLanguagePackVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        VerifyRegistry();

        var settingsService = new SettingsService(
            Path.Combine(verifierRoot, "language-pack-settings"));
        var settings = settingsService.Load();
        settings.CommandBuilder.ActiveLanguage = "Python";
        settings.Environments.Add(new EnvironmentProfile
        {
            Id = "taxonomy-migration-probe",
            Name = "Taxonomy migration probe",
            Language = "Python",
            SourceLibraryId = "python-essentials",
            LibraryIds = ["python-essentials"]
        });
        var catalogService = new CommandCatalogService(settingsService);
        var descriptors = await catalogService.LoadAsync();
        var deferredOfficialCatalogs = descriptors
            .Where(library =>
                library.Origin == CommandLibraryOrigin.Bundled
                && library.SchemaVersion == 2)
            .ToList();
        Require(
            deferredOfficialCatalogs.Count >= 31
            && deferredOfficialCatalogs.All(library =>
                library.HasDeferredEntries
                && library.Commands.Count == 0
                && library.DeclaredEntryCount > 0),
            "Schema-v2 official catalogs eagerly loaded symbol shards during descriptor discovery.");

        var builder = new CommandBuilderViewModel(settings, settingsService);
        await builder.LoadLibrariesAsync(catalogService);
        var libraries = builder.Libraries;
        Require(
            libraries.All(library =>
                !library.Id.Contains("essentials", StringComparison.OrdinalIgnoreCase)
                && !library.DisplayName.Contains("essentials", StringComparison.OrdinalIgnoreCase)),
            "A retired Essentials collection remains visible in the official catalog.");

        var powerShellCore = GetLibrary(libraries, "powershell-core");
        var csharpCore = GetLibrary(libraries, "csharp-core");
        var pythonCore = GetLibrary(libraries, "python-core");
        var pythonStandardLibrary = GetLibrary(
            libraries,
            "python-standard-library");
        var javascriptCore = GetLibrary(libraries, "javascript-core");
        var nodeStandardLibrary = GetLibrary(
            libraries,
            "nodejs-standard-library");
        var typescriptCore = GetLibrary(libraries, "typescript-core");
        var typescriptCompilerApi = GetLibrary(
            libraries,
            "typescript-compiler-api");
        var javaCore = GetLibrary(libraries, "java-core");
        var javaStandardLibrary = GetLibrary(
            libraries,
            "java-se-standard-library");
        var cppCore = GetLibrary(libraries, "cpp-core");
        var cppStandardLibrary = GetLibrary(
            libraries,
            "cpp-standard-library");
        var htmlCore = GetLibrary(libraries, "html-core");
        var cssLibraries = GetLibraries(
            libraries,
            "css-core",
            "css-flexible-box-layout",
            "css-grid-layout",
            "css-fonts",
            "css-transitions",
            "css-animations",
            "css-color",
            "css-transforms");
        var webApiLibraries = GetLibraries(
            libraries,
            "dom-standard",
            "fetch-standard",
            "url-standard",
            "streams-standard",
            "web-storage-standard",
            "xhr-standard",
            "workers-standard",
            "canvas-standard",
            "web-crypto-standard",
            "html-timers",
            "html-navigation");
        var taxonomyLibraries = new[]
            {
                csharpCore,
                pythonCore,
                pythonStandardLibrary,
                javascriptCore,
                nodeStandardLibrary,
                typescriptCore,
                typescriptCompilerApi,
                javaCore,
                javaStandardLibrary,
                cppCore,
                cppStandardLibrary,
                htmlCore
            }
            .Concat(cssLibraries)
            .Concat(webApiLibraries)
            .ToList();
        await Task.WhenAll(taxonomyLibraries.Select(
            catalogService.LoadLibraryEntriesAsync));
        var popularLibraries = libraries
            .Where(library =>
                library.Origin == CommandLibraryOrigin.Bundled
                && library.SchemaVersion == 2
                && string.Equals(
                    library.Category,
                    "Popular libraries",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(library => library.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(library => library.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await VerifyPopularLibraryStartersAsync(
            popularLibraries,
            builder,
            catalogService,
            settings);
        var migratedFetchEntry = await catalogService.ResolveEntryReferenceAsync(
            GetLibrary(libraries, "fetch-standard"),
            "javascript.fetch-json");
        Require(
            migratedFetchEntry?.Id == "web.fetch-json",
            "A saved javascript.fetch-json entry reference did not migrate to the Fetch Standard.");
        var migratedTypeScriptGuide =
            await catalogService.ResolveEntryReferenceAsync(
                typescriptCompilerApi,
                "typescript.compiler-api.ts-createsourcefile.5065c8fd7a59");
        Require(
            migratedTypeScriptGuide?.Id ==
                "typescript.compiler-api.create-source-file",
            "A saved TypeScript 5.9.3 Compiler API entry reference did not migrate to its guided TypeScript 6 compatibility entry.");
        var deferredGuideEntries = taxonomyLibraries
            .SelectMany(library => library.Commands)
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        Require(
            deferredGuideEntries.Count > 0
            && deferredGuideEntries.All(command =>
                command.HasDeferredGuide
                && command.Parameters.Count == 0
                && command.Examples.Count == 0),
            "Selecting catalog indexes eagerly deserialized detailed guides or parameter metadata.");

        VerifyPowerShellCore(powerShellCore);
        VerifyTaxonomyLibrary(
            csharpCore,
            "C#",
            CommandLibraryKind.Core,
            TerminalShellKind.CSharp,
            expectedMinimumEntries: 140,
            isExecutionSupported: true,
            expectedExecutionTarget: ExecutionTarget.CSharp);
        VerifyTaxonomyLibrary(
            pythonCore,
            "Python",
            CommandLibraryKind.Core,
            TerminalShellKind.Python,
            expectedMinimumEntries: 425,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            pythonStandardLibrary,
            "Python",
            CommandLibraryKind.StandardLibrary,
            TerminalShellKind.Python,
            expectedMinimumEntries: 10_000,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            javascriptCore,
            "JavaScript",
            CommandLibraryKind.Core,
            TerminalShellKind.NodeJs,
            expectedMinimumEntries: 1_000,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            nodeStandardLibrary,
            "JavaScript",
            CommandLibraryKind.StandardLibrary,
            TerminalShellKind.NodeJs,
            expectedMinimumEntries: 1_700,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            typescriptCore,
            "TypeScript",
            CommandLibraryKind.Core,
            TerminalShellKind.TypeScript,
            expectedMinimumEntries: 67,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            typescriptCompilerApi,
            "TypeScript",
            CommandLibraryKind.PlatformApi,
            TerminalShellKind.TypeScript,
            expectedMinimumEntries: 6_779,
            isExecutionSupported: false);
        VerifyTaxonomyLibrary(
            javaCore,
            "Java",
            CommandLibraryKind.Core,
            TerminalShellKind.Java,
            expectedMinimumEntries: 110,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            javaStandardLibrary,
            "Java",
            CommandLibraryKind.StandardLibrary,
            TerminalShellKind.Java,
            expectedMinimumEntries: 50_000,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            cppCore,
            "C++",
            CommandLibraryKind.Core,
            TerminalShellKind.Cpp,
            expectedMinimumEntries: 280,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            cppStandardLibrary,
            "C++",
            CommandLibraryKind.StandardLibrary,
            TerminalShellKind.Cpp,
            expectedMinimumEntries: 1_700,
            isExecutionSupported: true);
        VerifyTaxonomyLibrary(
            htmlCore,
            "HTML",
            CommandLibraryKind.Core,
            TerminalShellKind.External,
            expectedMinimumEntries: 10,
            isExecutionSupported: false);
        foreach (var cssLibrary in cssLibraries)
        {
            VerifyTaxonomyLibrary(
                cssLibrary,
                "CSS",
                cssLibrary.Id == "css-core"
                    ? CommandLibraryKind.Core
                    : CommandLibraryKind.Specification,
                TerminalShellKind.External,
                expectedMinimumEntries: 1,
                isExecutionSupported: false);
        }
        foreach (var webApiLibrary in webApiLibraries)
        {
            VerifyTaxonomyLibrary(
                webApiLibrary,
                "Web APIs",
                CommandLibraryKind.PlatformApi,
                TerminalShellKind.External,
                expectedMinimumEntries: 1,
                isExecutionSupported: false);
        }
        VerifyCanonicalOwnership(
            pythonCore,
            pythonStandardLibrary,
            javascriptCore,
            nodeStandardLibrary,
            typescriptCore,
            typescriptCompilerApi,
            javaCore,
            javaStandardLibrary,
            cppCore,
            cppStandardLibrary);
        VerifyExpandedCoverage(
            javascriptCore,
            nodeStandardLibrary,
            typescriptCore,
            javaStandardLibrary,
            htmlCore,
            cssLibraries,
            webApiLibraries);

        var firstDeferredGuide = deferredGuideEntries[0];
        await catalogService.LoadCommandGuideAsync(firstDeferredGuide);
        Require(
            firstDeferredGuide.IsGuideLoaded
            && firstDeferredGuide.Examples.Count > 0
            && deferredGuideEntries.Skip(1).Any(command =>
                command.HasDeferredGuide),
            "Opening one entry did not preserve entry-level lazy guide materialization.");
        await Task.WhenAll(deferredGuideEntries
            .Where(command => command.HasDeferredGuide)
            .Select(catalogService.LoadCommandGuideAsync));
        Require(
            deferredGuideEntries.All(command =>
                command.IsGuideLoaded
                && command.Examples.Count > 0),
            "Detailed schema-v2 guide shards did not hydrate on demand.");
        VerifyPythonGuideExpansion(
            pythonCore,
            pythonStandardLibrary);
        VerifyJavaScriptNodeGuideExpansion(
            javascriptCore,
            nodeStandardLibrary);
        VerifyTypeScriptGuideExpansion(
            typescriptCore,
            typescriptCompilerApi);

        Require(
            builder.LibraryLanguageTabs.Select(tab => tab.Language)
                .SequenceEqual(
                    [
                        "PowerShell",
                        "C#",
                        "C++",
                        "Python",
                        "JavaScript",
                        "TypeScript",
                        "Java",
                        "HTML",
                        "CSS",
                        "Web APIs"
                    ]),
            "The official packs did not become first-class Command Lexicon language tabs.");
        Require(
            builder.Environments.Any(profile =>
                profile.SourceLibraryId == csharpCore.Id
                && profile.Language == "C#")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == pythonCore.Id
                && profile.Language == "Python")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == javascriptCore.Id
                && profile.Language == "JavaScript")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == typescriptCore.Id
                && profile.Language == "TypeScript")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == javaCore.Id
                && profile.Language == "Java")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == cppCore.Id
                && profile.Language == "C++")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == htmlCore.Id
                && profile.Language == "HTML")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == "css-core"
                && profile.Language == "CSS")
            && builder.Environments.Any(profile =>
                profile.SourceLibraryId == "dom-standard"
                && profile.Language == "Web APIs"),
            "The canonical official catalogs did not create selectable language surfaces.");
        var migrationProbe = builder.Environments.Single(profile =>
            profile.Id == "taxonomy-migration-probe");
        Require(
            migrationProbe.SourceLibraryId == "python-core"
            && migrationProbe.LibraryIds.SequenceEqual(["python-core"]),
            "A saved python-essentials selection did not migrate to Python Core.");

        VerifyBuilderSurfaces(
            builder,
            "C#",
            "csharp-core");
        VerifyBuilderSurfaces(
            builder,
            "Python",
            "python-core",
            "python-standard-library");
        VerifyBuilderSurfaces(
            builder,
            "JavaScript",
            "javascript-core",
            "nodejs-standard-library");
        VerifyBuilderSurfaces(
            builder,
            "TypeScript",
            "typescript-core",
            "typescript-compiler-api");
        VerifyBuilderSurfaces(
            builder,
            "Java",
            "java-core",
            "java-se-standard-library");
        VerifyBuilderSurfaces(
            builder,
            "C++",
            "cpp-core",
            "cpp-standard-library");

        TerminalProfileService.ReconcileProfiles(settings);
        var pythonProfile = settings.TerminalProfiles.Single(profile =>
            profile.ShellKind == TerminalShellKind.Python);
        var csharpProfile = settings.TerminalProfiles.Single(profile =>
            profile.ShellKind == TerminalShellKind.CSharp);
        var nodeProfile = settings.TerminalProfiles.Single(profile =>
            profile.ShellKind == TerminalShellKind.NodeJs);
        var typeScriptProfile = settings.TerminalProfiles.Single(profile =>
            profile.ShellKind == TerminalShellKind.TypeScript);
        var javaProfile = settings.TerminalProfiles.Single(profile =>
            profile.ShellKind == TerminalShellKind.Java);
        var cppProfile = settings.TerminalProfiles.Single(profile =>
            profile.ShellKind == TerminalShellKind.Cpp);
        Require(
            csharpProfile.IsAvailable,
            ".NET SDK is installed but the C# terminal profile was unavailable.");
        Require(
            pythonProfile.IsAvailable,
            "Python is installed but its built-in terminal profile was unavailable.");
        Require(
            nodeProfile.IsAvailable,
            "Node.js is installed but its built-in terminal profile was unavailable.");
        Require(
            typeScriptProfile.IsAvailable,
            "Node.js is installed with native type stripping but the TypeScript terminal profile was unavailable.");
        Require(
            !string.IsNullOrWhiteSpace(pythonStandardLibrary.ActiveRuntimeVersion)
            && pythonStandardLibrary.ActiveVersionLabel.Contains(
                pythonStandardLibrary.TargetVersion,
                StringComparison.OrdinalIgnoreCase)
            && (!pythonStandardLibrary.ActiveRuntimeVersion.Contains(
                    "3.14",
                    StringComparison.OrdinalIgnoreCase)
                || pythonStandardLibrary.UsesInstalledCompatibleIndex),
            "Python runtime-version selection did not expose an installed compatible index or bundled fallback.");
        Require(
            !string.IsNullOrWhiteSpace(nodeStandardLibrary.ActiveRuntimeVersion)
            && nodeStandardLibrary.ActiveVersionLabel.Contains(
                nodeStandardLibrary.TargetVersion,
                StringComparison.OrdinalIgnoreCase),
            "Node.js runtime-version selection did not expose its active index version.");
        if (javaProfile.IsAvailable)
        {
            Require(
                File.Exists(javaProfile.CompilerExecutablePath)
                && File.Exists(javaProfile.RuntimeExecutablePath)
                && !string.IsNullOrWhiteSpace(javaStandardLibrary.ActiveRuntimeVersion)
                && javaStandardLibrary.ActiveVersionLabel.Contains(
                    javaStandardLibrary.TargetVersion,
                    StringComparison.OrdinalIgnoreCase),
                "The available Java profile did not retain its toolchain paths and active catalog version.");
        }
        else
        {
            Require(
                javaProfile.AvailabilityMessage.Contains("JDK", StringComparison.OrdinalIgnoreCase)
                || javaProfile.AvailabilityMessage.Contains("javac", StringComparison.OrdinalIgnoreCase),
                "A missing Java toolchain did not explain the JDK requirement.");
        }
        if (cppProfile.IsAvailable)
        {
            Require(
                File.Exists(cppProfile.CompilerExecutablePath),
                "The available C++ profile did not retain its compiler path.");
        }
        else
        {
            Require(
                cppProfile.AvailabilityMessage.Contains(
                    "Clang",
                    StringComparison.OrdinalIgnoreCase)
                || cppProfile.AvailabilityMessage.Contains(
                    "GCC",
                    StringComparison.OrdinalIgnoreCase)
                || cppProfile.AvailabilityMessage.Contains(
                    "Visual Studio",
                    StringComparison.OrdinalIgnoreCase)
                || cppProfile.AvailabilityMessage.Contains(
                    "compiler",
                    StringComparison.OrdinalIgnoreCase),
                "A missing C++ toolchain did not explain the supported compiler choices.");
        }

        await VerifyCSharpCoreAsync(
            csharpCore,
            csharpProfile,
            verifierRoot);
        await VerifyLibrarySyntaxAsync(
            pythonCore,
            pythonProfile,
            verifierRoot);
        await VerifyLibrarySyntaxAsync(
            pythonStandardLibrary,
            pythonProfile,
            verifierRoot);
        await VerifyPythonExamplesAsync(
            pythonCore,
            pythonProfile,
            verifierRoot);
        await VerifyPythonExamplesAsync(
            pythonStandardLibrary,
            pythonProfile,
            verifierRoot);
        await VerifyLibrarySyntaxAsync(
            javascriptCore,
            nodeProfile,
            verifierRoot);
        await VerifyLibrarySyntaxAsync(
            nodeStandardLibrary,
            nodeProfile,
            verifierRoot);
        await VerifyNodeExamplesAsync(
            javascriptCore,
            nodeProfile,
            verifierRoot);
        await VerifyNodeExamplesAsync(
            nodeStandardLibrary,
            nodeProfile,
            verifierRoot);
        await VerifyLibrarySyntaxAsync(
            typescriptCore,
            typeScriptProfile,
            verifierRoot);
        await VerifyTypeScriptExamplesAsync(
            typescriptCore,
            typeScriptProfile,
            verifierRoot);
        if (javaProfile.IsAvailable)
        {
            await VerifyJavaLibraryAsync(
                javaCore,
                javaProfile,
                verifierRoot);
            await VerifyJavaLibraryAsync(
                javaStandardLibrary,
                javaProfile,
                verifierRoot);
            await VerifyJavaSessionAsync(javaProfile, verifierRoot);
        }
        if (cppProfile.IsAvailable)
        {
            await VerifyCppLibraryAsync(
                cppCore,
                cppProfile,
                verifierRoot);
            await VerifyCppLibraryAsync(
                cppStandardLibrary,
                cppProfile,
                verifierRoot);
            await VerifyCppSessionAsync(cppProfile, verifierRoot);
        }
        await VerifyPythonSessionAsync(pythonProfile, verifierRoot);
        await VerifyNodeSessionAsync(nodeProfile, verifierRoot);
        await VerifyTypeScriptSessionAsync(
            typeScriptProfile,
            verifierRoot);
        await VerifyWebGuidanceAsync(
            htmlCore,
            CombineLibraries("css-verifier", cssLibraries),
            CombineLibraries("web-api-verifier", webApiLibraries),
            nodeProfile,
            verifierRoot);
        await VerifyWebDocumentWorkflowAsync(verifierRoot);
        await JavaLanguageVerifier.VerifyAsync(verifierRoot);

        Console.WriteLine(
            $"OFFICIAL_LANGUAGE_PACKS_OK|csharp={csharpCore.Commands.Count}|python={pythonCore.Commands.Count + pythonStandardLibrary.Commands.Count}|javascript={javascriptCore.Commands.Count + nodeStandardLibrary.Commands.Count}|typescript={typescriptCore.Commands.Count + typescriptCompilerApi.Commands.Count}|java={javaCore.Commands.Count + javaStandardLibrary.Commands.Count}|cpp={cppCore.Commands.Count + cppStandardLibrary.Commands.Count}|html={htmlCore.Commands.Count}|css={cssLibraries.Sum(library => library.Commands.Count)}|web={webApiLibraries.Sum(library => library.Commands.Count)}|popular-libraries={popularLibraries.Count}|popular-recipes={popularLibraries.Sum(library => library.Commands.Count)}|java-jdk={(javaProfile.IsAvailable ? "available" : "capability-gated")}|cpp-toolchain={(cppProfile.IsAvailable ? "available" : "capability-gated")}");
    }

    private static async Task VerifyPopularLibraryStartersAsync(
        IReadOnlyList<CommandLibrary> popularLibraries,
        CommandBuilderViewModel builder,
        CommandCatalogService catalogService,
        AppSettings settings)
    {
        var expectedLanguages = CommandLanguageRegistry.Definitions
            .Select(definition => definition.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            popularLibraries.Count == 30
            && popularLibraries
                .GroupBy(library => library.Language, StringComparer.OrdinalIgnoreCase)
                .Count() == expectedLanguages.Count
            && popularLibraries
                .GroupBy(library => library.Language, StringComparer.OrdinalIgnoreCase)
                .All(group =>
                    expectedLanguages.Contains(group.Key)
                    && group.Count() == 3),
            "The popular-library starter layer does not contain exactly three collections for every featured language.");

        Require(
            popularLibraries.All(library =>
                (library.Kind is CommandLibraryKind.Module
                    or CommandLibraryKind.Package
                    or CommandLibraryKind.Framework)
                && library.IsStarterLibrary
                && !library.CreateStandaloneProfile
                && library.DeclaredEntryCount == 3
                && library.DeclaredIndexedCount == 0
                && library.DeclaredGuidedCount == 3
                && library.DeclaredVerifiedCount == 0
                && library.DeclaredGuideCount == 3
                && library.HasDeferredEntries
                && library.Commands.Count == 0
                && Uri.TryCreate(
                    library.SourceUrl,
                    UriKind.Absolute,
                    out var sourceUri)
                && sourceUri.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(library.TargetVersion)
                && !string.IsNullOrWhiteSpace(library.License)
                && !string.IsNullOrWhiteSpace(
                    library.ExecutionSupportMessage)),
            "A popular-library descriptor lost its provenance, license, starter, coverage, or dependency policy metadata.");

        await Task.WhenAll(popularLibraries.Select(
            catalogService.LoadLibraryEntriesAsync));
        var entries = popularLibraries
            .SelectMany(library => library.Commands)
            .ToList();
        Require(
            entries.Count == 90
            && entries.Select(entry => entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 90
            && entries.All(entry =>
                entry.CoverageTier == CommandCoverageTier.Guided
                && entry.HasDeferredGuide
                && entry.Parameters.Count == 0
                && entry.Examples.Count == 0
                && entry.BrowsePath.Count == 2
                && entry.BrowsePath[0].Kind == "Library"
                && entry.BrowsePath[1].Kind == "Recipe"
                && entry.IsExecutionSupported
                    == popularLibraries.Single(library =>
                        string.Equals(
                            library.Id,
                            entry.CatalogId,
                            StringComparison.OrdinalIgnoreCase))
                        .IsExecutionSupported),
            "Popular-library entries were not loaded as 90 unique, lazy, guided recipes with inherited execution policy.");

        await Task.WhenAll(entries.Select(
            catalogService.LoadCommandGuideAsync));
        Require(
            entries.All(entry =>
                entry.IsGuideLoaded
                && entry.Examples.Count == 1
                && entry.Examples[0].Code.Length > 0
                && entry.Parameters.All(parameter =>
                    !string.IsNullOrWhiteSpace(parameter.DisplayName)
                    && !string.IsNullOrWhiteSpace(parameter.Description)
                    && !string.IsNullOrWhiteSpace(parameter.DefaultValue))
                && !entry.Parameters.Any(parameter =>
                    RenderTemplate(entry).Contains(
                        $"{{{(string.IsNullOrWhiteSpace(parameter.Placeholder) ? parameter.Name : parameter.Placeholder)}}}",
                        StringComparison.Ordinal))),
            "Popular-library guide hydration left missing examples, incomplete inputs, or unresolved recipe placeholders.");

        var renderedById = entries.ToDictionary(
            entry => entry.Id,
            RenderTemplate,
            StringComparer.OrdinalIgnoreCase);
        Require(
            renderedById["dapper-popular.query-async"].Contains(
                "QueryAsync<User>(\"select *",
                StringComparison.Ordinal)
            && renderedById["newtonsoft-json-popular.query-jobject"]
                .Contains(
                    "SelectToken(\"users[0].name\")",
                    StringComparison.Ordinal)
            && renderedById["serilog-popular.write-structured-event"]
                .Contains(
                    "Log.Information(\"Processed job {JobId}\", 42)",
                    StringComparison.Ordinal)
            && renderedById["express-popular.json-server"].Contains(
                "Listening on port 3000",
                StringComparison.Ordinal)
            && !renderedById["express-popular.json-server"].Contains(
                "$3000",
                StringComparison.Ordinal)
            && renderedById["d3-popular.responsive-bars"].Contains(
                "const width = 640;",
                StringComparison.Ordinal)
            && renderedById["threejs-popular.create-scene"].Contains(
                "HemisphereLight",
                StringComparison.Ordinal)
            && renderedById["threejs-popular.create-scene"].Contains(
                "renderer.render(scene, camera);",
                StringComparison.Ordinal),
            "Popular-library default rendering regressed quoting, placeholder handling, or graphics setup.");

        Require(
            settings.CommandLibraryLayoutVersion >= 6,
            "The command-library profile layout did not migrate to the popular-library starter layer.");
        foreach (var language in expectedLanguages)
        {
            var languageLibraryIds = popularLibraries
                .Where(library => string.Equals(
                    library.Language,
                    language,
                    StringComparison.OrdinalIgnoreCase))
                .Select(library => library.Id)
                .ToList();
            var profile = builder.Environments.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Language,
                    language,
                    StringComparison.OrdinalIgnoreCase)
                && languageLibraryIds.All(id => candidate.LibraryIds.Contains(
                    id,
                    StringComparer.OrdinalIgnoreCase)));
            Require(
                profile is not null
                && profile.Name.Contains(
                    "popular libraries",
                    StringComparison.OrdinalIgnoreCase)
                && profile.Description.Contains(
                    "never installed automatically",
                    StringComparison.OrdinalIgnoreCase),
                $"{language} did not receive one clear default profile containing its three popular libraries.");
        }

    }

    private static void VerifyRegistry()
    {
        Require(
            CommandLanguageRegistry.ProfileLanguageOptions.SequenceEqual(
                ["PowerShell", "C#", "C++", "Python", "JavaScript", "TypeScript", "Java"]),
            "Executable language profiles are not ordered by the central registry.");
        Require(
            CommandLanguageRegistry.TryGet("node.js", out var javascript)
            && javascript.DisplayName == "JavaScript"
            && javascript.PreferredShellKind == TerminalShellKind.NodeJs,
            "The JavaScript aliases did not resolve to the Node.js adapter.");
        Require(
            CommandLanguageRegistry.CanExecute(
                "py",
                ExecutionTarget.External,
                TerminalShellKind.Python),
            "The Python external execution route is not registered.");
        Require(
            CommandLanguageRegistry.CanExecute(
                "TypeScript",
                ExecutionTarget.External,
                TerminalShellKind.TypeScript)
            && !CommandLanguageRegistry.CanExecute(
                "TypeScript",
                ExecutionTarget.External,
                TerminalShellKind.NodeJs),
            "TypeScript did not resolve exclusively to its trusted terminal adapter.");
        Require(
            CommandLanguageRegistry.TryGet("jdk", out var java)
            && java.DisplayName == "Java"
            && java.PreferredShellKind == TerminalShellKind.Java
            && CommandLanguageRegistry.CanExecute(
                "Java",
                ExecutionTarget.External,
                TerminalShellKind.Java)
            && !CommandLanguageRegistry.CanExecute(
                "Java",
                ExecutionTarget.External,
                TerminalShellKind.TypeScript),
            "Java did not resolve exclusively to its compiled JDK adapter.");
        Require(
            CommandLanguageRegistry.TryGet("cxx", out var cpp)
            && cpp.DisplayName == "C++"
            && cpp.PreferredShellKind == TerminalShellKind.Cpp
            && CommandLanguageRegistry.CanExecute(
                "C++",
                ExecutionTarget.External,
                TerminalShellKind.Cpp),
            "C++ did not resolve to its trusted compiled-language adapter.");
        Require(
            CommandLanguageRegistry.TryGet("HTML", out var html)
            && !html.SupportsTerminalExecution
            && html.PreferredShellKind == TerminalShellKind.External
            && html.PrimaryDestination == CommandBuilderPrimaryDestination.Editor
            && html.PreferredEditorLanguageId == "html"
            && html.CompositionUnitPlural == "elements"
            && html.ComposedTextHeading == "COMPOSED MARKUP"
            && CommandLanguageRegistry.TryGet("CSS", out var css)
            && !css.SupportsTerminalExecution
            && css.PreferredShellKind == TerminalShellKind.External
            && css.PrimaryDestination == CommandBuilderPrimaryDestination.Editor
            && css.PreferredEditorLanguageId == "css"
            && css.CompositionUnitPlural == "rules"
            && css.ComposedTextHeading == "COMPOSED STYLES"
            && CommandLanguageRegistry.TryGet("browser APIs", out var webApis)
            && webApis.DisplayName == "Web APIs"
            && !webApis.SupportsTerminalExecution
            && webApis.PrimaryDestination == CommandBuilderPrimaryDestination.Editor,
            "The editor-oriented web languages were not registered as non-terminal surfaces.");
        Require(
            CommandLanguageRegistry.TryGet("TypeScript", out var typeScript)
            && typeScript.SupportsTerminalExecution
            && typeScript.PrimaryDestination == CommandBuilderPrimaryDestination.Editor
            && typeScript.PreferredEditorLanguageId == "typescript"
            && CommandLanguageRegistry.TryGet("Java", out var editorFirstJava)
            && editorFirstJava.PrimaryDestination == CommandBuilderPrimaryDestination.Editor
            && CommandLanguageRegistry.TryGet("C++", out var editorFirstCpp)
            && editorFirstCpp.PrimaryDestination == CommandBuilderPrimaryDestination.Editor,
            "Source-oriented executable languages did not retain editor-first composition behavior.");
        Require(
            CommandLanguageRegistry.CanInsertIntoEditor("HTML", "html")
            && CommandLanguageRegistry.CanInsertIntoEditor("CSS", "css")
            && CommandLanguageRegistry.CanInsertIntoEditor("CSS", "scss")
            && CommandLanguageRegistry.CanInsertIntoEditor("Web APIs", "html")
            && CommandLanguageRegistry.CanInsertIntoEditor("Web APIs", "javascript")
            && CommandLanguageRegistry.CanInsertIntoEditor("Web APIs", "typescript")
            && !CommandLanguageRegistry.CanInsertIntoEditor("HTML", "javascript")
            && !CommandLanguageRegistry.CanInsertIntoEditor("Web APIs", "python"),
            "Web guidance did not retain its compatible-editor boundaries.");

        var ordered = CommandLanguageRegistry.OrderLanguages(
            [
                "Rust",
                "Web APIs",
                "CSS",
                "HTML",
                "Java",
                "TypeScript",
                "javascript",
                "Python",
                "C++",
                "PowerShell"
            ]);
        Require(
            ordered.SequenceEqual(
                [
                    "PowerShell",
                    "C++",
                    "Python",
                    "JavaScript",
                    "TypeScript",
                    "Java",
                    "HTML",
                    "CSS",
                    "Web APIs",
                    "Rust"
                ]),
            "Known and package-defined language tabs were not ordered consistently.");
    }

    private static CommandLibrary GetLibrary(
        IEnumerable<CommandLibrary> libraries,
        string libraryId) =>
        libraries.Single(library => string.Equals(
            library.Id,
            libraryId,
            StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<CommandLibrary> GetLibraries(
        IEnumerable<CommandLibrary> libraries,
        params string[] libraryIds) =>
        libraryIds.Select(id => GetLibrary(libraries, id)).ToList();

    private static void VerifyPowerShellCore(CommandLibrary library)
    {
        Require(
            library.SchemaVersion == 2
            && library.Origin == CommandLibraryOrigin.Runtime
            && library.Kind == CommandLibraryKind.Core
            && library.DisplayName == "PowerShell Core"
            && !string.IsNullOrWhiteSpace(library.CanonicalName)
            && !string.IsNullOrWhiteSpace(library.ActiveRuntimeVersion)
            && library.Commands.Count > 0,
            "PowerShell Core did not load as a runtime-versioned canonical catalog.");
        Require(
            library.Commands.All(command =>
                !string.IsNullOrWhiteSpace(command.ModuleName)
                && command.SymbolKind == CommandSymbolKind.Command
                && command.BrowsePath.Count >= 2
                && command.BrowsePath[0].Kind == "Module"
                && command.BrowsePath[0].Name == command.ModuleName),
            "PowerShell commands are not classified under their actual modules.");
    }

    private static CommandLibrary CombineLibraries(
        string id,
        IEnumerable<CommandLibrary> libraries) =>
        new()
        {
            Id = id,
            Commands = libraries
                .SelectMany(library => library.Commands)
                .ToList()
        };

    private static void VerifyBuilderSurfaces(
        CommandBuilderViewModel builder,
        string language,
        params string[] expectedLibraryIds)
    {
        var languageTab = builder.LibraryLanguageTabs.Single(tab =>
            string.Equals(
                tab.Language,
                language,
                StringComparison.OrdinalIgnoreCase));
        var surfaceIds = languageTab.Libraries
            .Select(selection => selection.Library.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            expectedLibraryIds.All(surfaceIds.Contains)
            && !surfaceIds.Overlaps(
                CommandCatalogService.RetiredOfficialCatalogIds),
            $"{language} did not expose its canonical Command Builder surfaces.");

        if (string.Equals(
                builder.SelectedInputSyntax,
                language,
                StringComparison.OrdinalIgnoreCase))
        {
            Require(
                builder.IsHierarchySurface,
                $"{language} did not activate the shared hierarchy navigator " +
                $"(input={builder.SelectedInputSyntax}, " +
                $"surface={builder.SelectedBuilderSurface?.LibraryId ?? "<none>"}, " +
                $"hierarchy={builder.IsHierarchySurface}).");
        }
    }

    private static void VerifyTaxonomyLibrary(
        CommandLibrary library,
        string expectedLanguage,
        CommandLibraryKind expectedKind,
        TerminalShellKind expectedShell,
        int expectedMinimumEntries,
        bool isExecutionSupported,
        ExecutionTarget expectedExecutionTarget = ExecutionTarget.External)
    {
        Require(
            library.SchemaVersion == 2
            && library.Origin == CommandLibraryOrigin.Bundled
            && library.Kind == expectedKind
            && library.CreateStandaloneProfile
            && library.EntryFiles.Count > 0
            && (library.DeclaredGuideCount > 0
                ? library.GuideFiles.Count > 0
                : library.GuideFiles.Count == 0)
            && library.Commands.Count == library.DeclaredEntryCount,
            $"{library.DisplayName} did not load as a complete schema-v2 canonical catalog.");
        Require(
            library.DeclaredIndexedCount
            + library.DeclaredGuidedCount
            + library.DeclaredVerifiedCount == library.DeclaredEntryCount
            && library.DeclaredGuideCount
            == library.DeclaredGuidedCount + library.DeclaredVerifiedCount
            && !string.IsNullOrWhiteSpace(library.CoverageSummary),
            $"{library.DisplayName} does not expose a consistent coverage report.");
        Require(
            string.Equals(
                library.Language,
                expectedLanguage,
                StringComparison.OrdinalIgnoreCase)
            && library.ExecutionTarget == expectedExecutionTarget
            && library.IsExecutionSupported == isExecutionSupported
            && (isExecutionSupported
                || !string.IsNullOrWhiteSpace(
                    library.ExecutionSupportMessage)),
            $"{library.DisplayName} did not retain its declared language and execution policy.");
        Require(
            !string.IsNullOrWhiteSpace(library.CanonicalName)
            && !string.IsNullOrWhiteSpace(library.Ecosystem)
            && !string.IsNullOrWhiteSpace(library.TargetVersion)
            && !string.IsNullOrWhiteSpace(library.SourceVersion)
            && !string.IsNullOrWhiteSpace(library.License)
            && !string.IsNullOrWhiteSpace(library.ActiveVersionLabel)
            && Uri.TryCreate(
                library.SourceUrl,
                UriKind.Absolute,
                out var sourceUri)
            && sourceUri.Scheme is "http" or "https",
            $"{library.DisplayName} is missing provenance, version, or licensing metadata.");
        Require(
            library.Commands.Count >= expectedMinimumEntries
            && library.Commands
                .Select(command => command.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == library.Commands.Count,
            $"{library.DisplayName} contains too few entries or duplicate command IDs.");

        foreach (var command in library.Commands)
        {
            Require(
                command.CatalogId == library.Id
                && command.ExecutionTarget == expectedExecutionTarget
                && command.PreferredShellKind == expectedShell
                && command.CompositionMode == CommandCompositionMode.Statement,
                $"{library.DisplayName} entry '{command.Id}' does not target its declared surface.");
            Require(
                !string.IsNullOrWhiteSpace(command.DisplayName)
                && !string.IsNullOrWhiteSpace(command.Description)
                && !string.IsNullOrWhiteSpace(command.GuideDefinition)
                && !string.IsNullOrWhiteSpace(command.Template)
                && command.BrowsePath.Count > 0
                && command.BrowsePath.All(segment =>
                    !string.IsNullOrWhiteSpace(segment.Kind)
                    && !string.IsNullOrWhiteSpace(segment.Name)),
                $"{library.DisplayName} entry '{command.Id}' is missing its index or hierarchy metadata.");
            Require(
                Uri.TryCreate(
                    command.DocumentationUrl,
                    UriKind.Absolute,
                    out var documentationUri)
                && documentationUri.Scheme is "http" or "https",
                $"{library.DisplayName} entry '{command.Id}' has no valid documentation URL.");
            Require(
                command.CoverageTier == CommandCoverageTier.Indexed
                || command.HasDeferredGuide
                || command.Examples.Count > 0,
                $"{library.DisplayName} entry '{command.Id}' claims guided coverage without a deferred or loaded guide.");

            foreach (var parameter in command.Parameters)
            {
                var placeholder = string.IsNullOrWhiteSpace(
                    parameter.Placeholder)
                    ? parameter.Name
                    : parameter.Placeholder;
                Require(
                    command.Template.Contains(
                        $"{{{placeholder}}}",
                        StringComparison.Ordinal),
                    $"{library.DisplayName} entry '{command.Id}' does not use parameter '{placeholder}'.");
            }
        }

        if (expectedKind == CommandLibraryKind.Core)
        {
            Require(
                library.Commands.All(command =>
                    !command.BrowsePath[0].Kind.Equals(
                        "Module",
                        StringComparison.OrdinalIgnoreCase)
                    && !command.BrowsePath[0].Kind.Equals(
                        "Package",
                        StringComparison.OrdinalIgnoreCase)
                    && !command.BrowsePath[0].Kind.Equals(
                        "Header",
                        StringComparison.OrdinalIgnoreCase)),
                $"{library.DisplayName} incorrectly owns an imported API.");
        }
        else if (expectedKind == CommandLibraryKind.StandardLibrary)
        {
            Require(
                library.Commands.All(command =>
                    command.BrowsePath[0].Kind.Equals(
                        "Module",
                        StringComparison.OrdinalIgnoreCase)
                    || command.BrowsePath[0].Kind.Equals(
                        "Header",
                        StringComparison.OrdinalIgnoreCase)),
                $"{library.DisplayName} is not rooted in canonical modules or headers.");
        }
    }

    private static void VerifyCanonicalOwnership(
        params CommandLibrary[] libraries)
    {
        var allEntries = libraries
            .SelectMany(library => library.Commands.Select(command => new
            {
                Library = library.Id,
                Command = command
            }))
            .ToList();
        Require(
            allEntries
                .Select(item => item.Command.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == allEntries.Count,
            "A public symbol has duplicate canonical owners across Core and standard libraries.");

        var pythonStandardLibrary = GetLibrary(
            libraries,
            "python-standard-library");
        var pythonModules = pythonStandardLibrary.Commands
            .Select(command => command.BrowsePath[0].Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            new[] { "dataclasses", "json", "pathlib", "urllib.request" }
                .All(pythonModules.Contains),
            "Python imported APIs were not moved into the standard library.");

        var javascriptCore = GetLibrary(libraries, "javascript-core");
        Require(
            javascriptCore.Commands.All(command =>
                !command.Id.Contains("fetch", StringComparison.OrdinalIgnoreCase)
                && !command.Id.Contains("read-file", StringComparison.OrdinalIgnoreCase)
                && !command.Id.Contains("console", StringComparison.OrdinalIgnoreCase)),
            "JavaScript Core still owns a host or platform API.");
        var nodeStandardLibrary = GetLibrary(
            libraries,
            "nodejs-standard-library");
        Require(
            nodeStandardLibrary.Commands.All(command =>
                command.BrowsePath[0].Name.StartsWith(
                    "node:",
                    StringComparison.OrdinalIgnoreCase)),
            "The Node.js Standard Library contains a browser or non-Node API.");

        var javaStandardLibrary = GetLibrary(
            libraries,
            "java-se-standard-library");
        Require(
            javaStandardLibrary.Commands.All(command =>
                command.BrowsePath.Count >= 3
                && command.BrowsePath[1].Kind.Equals(
                    "Package",
                    StringComparison.OrdinalIgnoreCase)
                && command.BrowsePath[2].Kind.Equals(
                    "Type",
                    StringComparison.OrdinalIgnoreCase)),
            "The Java SE hierarchy is missing module, package, or type coordinates.");
        Require(
            javaStandardLibrary.Commands.Any(command =>
                command.BrowsePath.Count >= 4
                && new[] { "Method", "Constructor", "Field" }.Contains(
                    command.BrowsePath[3].Kind,
                    StringComparer.OrdinalIgnoreCase)),
            "The Java SE hierarchy does not expose member coordinates.");
    }

    private static void VerifyExpandedCoverage(
        CommandLibrary javascriptCore,
        CommandLibrary nodeStandardLibrary,
        CommandLibrary typescriptCore,
        CommandLibrary javaStandardLibrary,
        CommandLibrary htmlCore,
        IReadOnlyList<CommandLibrary> cssLibraries,
        IReadOnlyList<CommandLibrary> webApiLibraries)
    {
        var forbiddenBrowserTypes = new HashSet<string>(
            ["Response", "Request", "Headers", "ReadableStream", "Crypto"],
            StringComparer.OrdinalIgnoreCase);
        Require(
            javascriptCore.Commands.All(command =>
                command.BrowsePath.All(segment =>
                    !forbiddenBrowserTypes.Contains(segment.Name))),
            "JavaScript Core contains a browser-platform API.");
        Require(
            nodeStandardLibrary.Commands.Count >= 1_700
            && nodeStandardLibrary.Commands.All(command =>
                command.BrowsePath[0].Name.StartsWith(
                    "node:",
                    StringComparison.OrdinalIgnoreCase))
            && nodeStandardLibrary.Commands.All(command =>
                !command.BrowsePath[0].Name.Contains(
                    "webcrypto",
                    StringComparison.OrdinalIgnoreCase)
                && !command.BrowsePath[0].Name.Contains(
                    "webstreams",
                    StringComparison.OrdinalIgnoreCase)),
            "The Node.js inventory is incomplete or contains browser surfaces.");
        Require(
            new[] { "Partial", "Awaited", "NoInfer", "ReturnType" }.All(
                utility => typescriptCore.Commands.Any(command =>
                    command.Name.Equals(utility, StringComparison.Ordinal)
                    && command.BrowsePath.Any(segment => segment.Name.Equals(
                        "Utility Types",
                        StringComparison.OrdinalIgnoreCase)))),
            "TypeScript Core is missing globally available utility types.");
        Require(
            javaStandardLibrary.Commands.Count >= 50_000
            && javaStandardLibrary.Commands
                .Select(command => command.BrowsePath[0].Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() >= 20,
            "The Java SE inventory does not span the public platform modules.");
        Require(
            htmlCore.Commands.Count(command =>
                command.SymbolKind == CommandSymbolKind.Element) >= 100
            && htmlCore.Commands.Count(command =>
                command.SymbolKind == CommandSymbolKind.Attribute) >= 150,
            "HTML Core does not expose the standard element and attribute indexes.");

        var cssIds = cssLibraries
            .Select(library => library.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            new[] { "css-color", "css-transforms", "css-grid-layout", "css-flexible-box-layout" }
                .All(cssIds.Contains)
            && cssLibraries.SelectMany(library => library.Commands).Any(command =>
                command.SymbolKind == CommandSymbolKind.Property),
            "CSS specification modules or their property indexes are missing.");

        var webIds = webApiLibraries
            .Select(library => library.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            new[]
            {
                "dom-standard",
                "fetch-standard",
                "url-standard",
                "streams-standard",
                "web-storage-standard",
                "workers-standard",
                "canvas-standard",
                "web-crypto-standard"
            }.All(webIds.Contains),
            "Web APIs were not split into their canonical standard surfaces.");
    }

    private static void VerifyPythonGuideExpansion(
        CommandLibrary pythonCore,
        CommandLibrary pythonStandardLibrary)
    {
        var coreGuides = pythonCore.Commands
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        var standardGuides = pythonStandardLibrary.Commands
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        Require(
            coreGuides.Count >= 28
            && coreGuides.Count(command =>
                command.CoverageTier == CommandCoverageTier.Verified) >= 28
            && coreGuides.All(command =>
                command.Parameters.Count > 0
                && command.Examples.Count > 0),
            "Python Core did not expose the expanded parameterized and verified guide set.");
        Require(
            standardGuides.Count >= 40
            && standardGuides.Count(command =>
                command.CoverageTier == CommandCoverageTier.Verified) >= 38
            && standardGuides.Count(command => command.Parameters.Count > 0) >= 37
            && standardGuides.All(command => command.Examples.Count > 0),
            "Python Standard Library did not expose the expanded guide and parameter coverage.");

        var requiredCoreIds = new[]
        {
            "python.core.len.function",
            "python.core.dict.get.method",
            "python.core.match-statement",
            "python.core.with-statement",
            "python.core.class-declaration"
        };
        Require(
            requiredCoreIds.All(id => pythonCore.Commands.Any(command =>
                command.Id.Equals(id, StringComparison.OrdinalIgnoreCase))),
            "Python Core is missing foundational built-in or language guides.");

        var guideCountsByModule = standardGuides
            .GroupBy(
                command => command.BrowsePath[0].Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var expectedModuleMinimums = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["pathlib"] = 5,
            ["os"] = 5,
            ["sys"] = 3,
            ["json"] = 4,
            ["datetime"] = 4,
            ["re"] = 4,
            ["collections"] = 4,
            ["itertools"] = 4,
            ["subprocess"] = 1,
            ["asyncio"] = 4
        };
        Require(
            expectedModuleMinimums.All(expected =>
                guideCountsByModule.TryGetValue(expected.Key, out var count)
                && count >= expected.Value),
            "Python foundational standard-library modules are missing their planned guide coverage.");
    }

    private static void VerifyJavaScriptNodeGuideExpansion(
        CommandLibrary javascriptCore,
        CommandLibrary nodeStandardLibrary)
    {
        var coreGuides = javascriptCore.Commands
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        var nodeGuides = nodeStandardLibrary.Commands
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        Require(
            coreGuides.Count >= 41
            && coreGuides.Count(command =>
                command.CoverageTier == CommandCoverageTier.Verified) >= 41
            && coreGuides.All(command =>
                command.Parameters.Count > 0
                && command.Examples.Count > 0),
            "JavaScript Core did not expose the expanded parameterized and verified guide set.");
        Require(
            nodeGuides.Count >= 42
            && nodeGuides.Count(command =>
                command.CoverageTier == CommandCoverageTier.Verified) >= 42
            && nodeGuides.All(command =>
                command.Parameters.Count > 0
                && command.Examples.Count > 0),
            "Node.js Standard Library did not expose the expanded parameterized and verified guide set.");

        var requiredCoreIds = new[]
        {
            "javascript.core.optional-chaining",
            "javascript.core.nullish-coalescing",
            "javascript.core.spread-syntax",
            "javascript.core.for-of",
            "javascript.core.async-await",
            "javascript.core.generator-function"
        };
        Require(
            requiredCoreIds.All(id => javascriptCore.Commands.Any(command =>
                command.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            && coreGuides.Any(command => command.BrowsePath.Any(segment =>
                segment.Name.Equals("Array", StringComparison.OrdinalIgnoreCase)))
            && coreGuides.Any(command => command.BrowsePath.Any(segment =>
                segment.Name.Equals("Object", StringComparison.OrdinalIgnoreCase)))
            && coreGuides.Any(command => command.BrowsePath.Any(segment =>
                segment.Name.Equals("Promise", StringComparison.OrdinalIgnoreCase))),
            "JavaScript Core is missing foundational syntax or built-in-object guides.");

        var guideCountsByModule = nodeGuides
            .GroupBy(
                command => command.BrowsePath[0].Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var expectedModuleMinimums = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["node:fs/promises"] = 8,
            ["node:path"] = 6,
            ["node:url"] = 2,
            ["node:os"] = 3,
            ["node:process"] = 3,
            ["node:util"] = 3,
            ["node:crypto"] = 3,
            ["node:buffer"] = 3,
            ["node:events"] = 3,
            ["node:assert"] = 2,
            ["node:timers/promises"] = 2,
            ["node:stream/promises"] = 1,
            ["node:zlib"] = 1,
            ["node:child_process"] = 1
        };
        Require(
            expectedModuleMinimums.All(expected =>
                guideCountsByModule.TryGetValue(expected.Key, out var count)
                && count >= expected.Value),
            "Foundational Node.js modules are missing their planned guide coverage.");
    }

    private static void VerifyTypeScriptGuideExpansion(
        CommandLibrary typescriptCore,
        CommandLibrary typescriptCompilerApi)
    {
        var coreGuides = typescriptCore.Commands
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        var compilerGuides = typescriptCompilerApi.Commands
            .Where(command => command.CoverageTier is
                CommandCoverageTier.Guided or CommandCoverageTier.Verified)
            .ToList();
        Require(
            coreGuides.Count >= 45
            && coreGuides.Count(command =>
                command.CoverageTier == CommandCoverageTier.Verified) >= 45
            && coreGuides.All(command =>
                command.Parameters.Count > 0
                && command.Examples.Count > 0),
            "TypeScript Core did not expose the expanded parameterized and verified guide set.");
        Require(
            compilerGuides.Count >= 33
            && compilerGuides.All(command =>
                command.CoverageTier == CommandCoverageTier.Guided
                && command.LegacyIds.Count > 0
                && command.Parameters.Count > 0
                && command.Examples.Count > 0),
            "The TypeScript Compiler API did not expose the expanded parameterized guide set.");
        Require(
            typescriptCore.TargetVersion == "TypeScript 7.0"
            && typescriptCompilerApi.TargetVersion ==
                "TypeScript 6.0.3 compatibility API"
            && typescriptCompilerApi.SourceVersion.Contains(
                "c967e03c8dbb4770f7e2f0b71b5d13593de679a41cc2b60266c4b69f1342a263",
                StringComparison.OrdinalIgnoreCase)
            && typescriptCompilerApi.Commands.Count >= 6_779
            && typescriptCompilerApi.Commands.Any(command =>
                command.Name == "ES2025"
                && command.BrowsePath.Any(segment =>
                    segment.Name == "ScriptTarget"))
            && compilerGuides.All(command =>
                !command.Template.Contains(
                    "from \"typescript\"",
                    StringComparison.Ordinal)
                && command.Examples.All(example =>
                    !example.Code.Contains(
                        "from \"typescript\"",
                        StringComparison.Ordinal))),
            "The current TypeScript 7 language surface or pinned TypeScript 6 compatibility API provenance is incorrect.");

        var requiredCoreIds = new[]
        {
            "typescript.core.conditional-type",
            "typescript.core.mapped-type",
            "typescript.core.template-literal-type",
            "typescript.core.type-predicate",
            "typescript.core.assertion-function",
            "typescript.core.function-overloads",
            "typescript.core.interface-merging",
            "typescript.core.variadic-tuple"
        };
        Require(
            requiredCoreIds.All(id => typescriptCore.Commands.Any(command =>
                command.Id.Equals(id, StringComparison.OrdinalIgnoreCase))),
            "TypeScript Core is missing foundational type-system, narrowing, or declaration guides.");

        var requiredCompilerIds = new[]
        {
            "typescript.compiler-api.create-source-file",
            "typescript.compiler-api.create-program",
            "typescript.compiler-api.create-printer",
            "typescript.compiler-api.transform",
            "typescript.compiler-api.program-get-type-checker",
            "typescript.compiler-api.checker-get-type-at-location",
            "typescript.compiler-api.get-pre-emit-diagnostics",
            "typescript.compiler-api.parse-json-config",
            "typescript.compiler-api.resolve-module-name",
            "typescript.compiler-api.create-watch-program"
        };
        Require(
            requiredCompilerIds.All(id =>
                typescriptCompilerApi.Commands.Any(command =>
                    command.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            && compilerGuides.All(command =>
                command.BrowsePath.Count >= 3
                && command.BrowsePath[0].Name.Equals(
                    "@typescript/typescript6",
                    StringComparison.OrdinalIgnoreCase)
                && command.BrowsePath[0].Kind.Equals(
                    "Package",
                    StringComparison.OrdinalIgnoreCase))
            && new[] { "NodeFactory", "Printer", "Program", "TypeChecker" }
                .All(typeName => compilerGuides.Any(command =>
                    command.BrowsePath.Any(segment => segment.Name.Equals(
                        typeName,
                        StringComparison.OrdinalIgnoreCase)))),
            "The TypeScript Compiler API is missing foundational guided surfaces or canonical package/type hierarchy.");
    }

    private static async Task VerifyPythonSessionAsync(
        TerminalProfile profile,
        string workingDirectory)
    {
        using var session = TerminalSessionFactory.Create(
            profile,
            workingDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await session.ExecuteAsync(
            "hs_value = 6 * 7\nprint(hs_value)",
            workingDirectory,
            timeout.Token);
        Require(
            first.Errors.Count == 0 && first.Output.Contains("42"),
            $"Python execution failed: {string.Join(" | ", first.Errors)}");

        var second = await session.ExecuteAsync(
            "print(hs_value + 1)",
            workingDirectory,
            timeout.Token);
        Require(
            second.Errors.Count == 0 && second.Output.Contains("43"),
            "The Python terminal did not preserve state between commands.");
    }

    private static async Task VerifyCSharpCoreAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        var source = string.Join(
            Environment.NewLine,
            library.Commands
                .Where(command => command.CoverageTier is
                    CommandCoverageTier.Guided or CommandCoverageTier.Verified)
                .Select(command =>
                    $"{{{Environment.NewLine}{RenderTemplate(command)}{Environment.NewLine}}}"));
        using var session = TerminalSessionFactory.Create(profile, workingDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await session.ExecuteAsync(
            source,
            workingDirectory,
            timeout.Token);
        Require(
            result.Errors.Count == 0,
            $"C# Core examples did not compile together: {string.Join(" | ", result.Errors)}");
    }

    private static async Task VerifyLibrarySyntaxAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        foreach (var command in library.Commands.Where(command =>
                     command.CoverageTier is CommandCoverageTier.Guided
                         or CommandCoverageTier.Verified))
        {
            await VerifySyntaxAsync(
                profile,
                RenderTemplate(command),
                workingDirectory,
                $"{command.Id} template");
            foreach (var example in command.Examples)
            {
                await VerifySyntaxAsync(
                    profile,
                    example.Code,
                    workingDirectory,
                    $"{command.Id} example '{example.Title}'");
            }
        }
    }

    private static async Task VerifyPythonExamplesAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        foreach (var command in library.Commands.Where(command =>
                     command.CoverageTier == CommandCoverageTier.Verified))
        {
            Require(
                command.Examples.Count > 0,
                $"Verified Python entry '{command.Id}' has no executable example.");
            foreach (var example in command.Examples)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = profile.ExecutablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory
                };
                foreach (var argument in profile.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(example.Code);

                using var process = new Process { StartInfo = startInfo };
                Require(
                    process.Start(),
                    $"Could not start Python for '{command.Id}'.");
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(12));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    throw new InvalidOperationException(
                        $"Python example '{command.Id} / {example.Title}' timed out.");
                }

                var output = await outputTask;
                var error = await errorTask;
                Require(
                    process.ExitCode == 0,
                    $"Python example '{command.Id} / {example.Title}' failed: " +
                    $"{(string.IsNullOrWhiteSpace(error) ? output : error).Trim()}");
            }
        }
    }

    private static async Task VerifyNodeExamplesAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        foreach (var command in library.Commands.Where(command =>
                     command.CoverageTier == CommandCoverageTier.Verified))
        {
            Require(
                command.Examples.Count > 0,
                $"Verified Node.js entry '{command.Id}' has no executable example.");
            foreach (var example in command.Examples)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = profile.ExecutablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory
                };
                foreach (var argument in profile.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add(example.Code);

                using var process = new Process { StartInfo = startInfo };
                Require(
                    process.Start(),
                    $"Could not start Node.js for '{command.Id}'.");
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(12));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    throw new InvalidOperationException(
                        $"Node.js example '{command.Id} / {example.Title}' timed out.");
                }

                var output = await outputTask;
                var error = await errorTask;
                Require(
                    process.ExitCode == 0,
                    $"Node.js example '{command.Id} / {example.Title}' failed: " +
                    $"{(string.IsNullOrWhiteSpace(error) ? output : error).Trim()}");
            }
        }
    }

    private static string RenderTemplate(CommandDefinition command)
    {
        var rendered = command.Template;
        foreach (var parameter in command.Parameters)
        {
            var placeholder = string.IsNullOrWhiteSpace(parameter.Placeholder)
                ? parameter.Name
                : parameter.Placeholder;
            rendered = rendered.Replace(
                $"{{{placeholder}}}",
                parameter.DefaultValue,
                StringComparison.Ordinal);
        }

        return rendered;
    }

    private static async Task VerifySyntaxAsync(
        TerminalProfile profile,
        string source,
        string workingDirectory,
        string context)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = profile.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in profile.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        switch (profile.ShellKind)
        {
            case TerminalShellKind.Python:
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(
                    "import sys; compile(sys.stdin.read(), '<hypershell-verifier>', 'exec')");
                break;
            case TerminalShellKind.NodeJs:
                startInfo.ArgumentList.Add("--check");
                startInfo.ArgumentList.Add("-");
                break;
            case TerminalShellKind.TypeScript:
                startInfo.ArgumentList.Add("--no-warnings");
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add(
                    "const source = require('node:fs').readFileSync(0, 'utf8'); " +
                    "const javascript = require('node:module').stripTypeScriptTypes(" +
                    "source, { mode: 'strip', sourceUrl: '<hypershell-verifier.ts>' }); " +
                    "new (require('node:vm').Script)(javascript, { filename: '<hypershell-verifier.ts>' });");
                break;
            default:
                throw new InvalidOperationException(
                    $"No syntax verifier is registered for {profile.ShellKind}.");
        }

        using var process = new Process { StartInfo = startInfo };
        Require(process.Start(), $"Could not start the {profile.Name} syntax verifier.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(source);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw new InvalidOperationException(
                $"{GetRuntimeDisplayName(profile)} syntax verification timed out for {context}.");
        }

        var output = await outputTask;
        var error = await errorTask;
        Require(
            process.ExitCode == 0,
            $"{GetRuntimeDisplayName(profile)} syntax failed for {context}: " +
            $"{(string.IsNullOrWhiteSpace(error) ? output : error).Trim()}");
    }

    private static string GetRuntimeDisplayName(TerminalProfile profile) =>
        CommandLanguageRegistry.GetRuntimeDisplayName(profile.ShellKind);

    private static async Task VerifyNodeSessionAsync(
        TerminalProfile profile,
        string workingDirectory)
    {
        using var session = TerminalSessionFactory.Create(
            profile,
            workingDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await session.ExecuteAsync(
            "const hsValue = 6 * 7; console.log(hsValue);",
            workingDirectory,
            timeout.Token);
        Require(
            first.Errors.Count == 0 && first.Output.Contains("42"),
            $"Node.js execution failed: {string.Join(" | ", first.Errors)}");

        var second = await session.ExecuteAsync(
            "hsValue + 1",
            workingDirectory,
            timeout.Token);
        Require(
            second.Errors.Count == 0 && second.Output.Contains("43"),
            "The Node.js terminal did not preserve state between commands.");
    }

    private static async Task VerifyTypeScriptSessionAsync(
        TerminalProfile profile,
        string workingDirectory)
    {
        using var session = TerminalSessionFactory.Create(
            profile,
            workingDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = await session.ExecuteAsync(
            "interface HypershellValue { value: number }\n" +
            "const hsTypedValue: HypershellValue = { value: 6 * 7 };\n" +
            "console.log(hsTypedValue.value);",
            workingDirectory,
            timeout.Token);
        Require(
            first.Errors.Count == 0 && first.Output.Contains("42"),
            $"TypeScript execution failed: {string.Join(" | ", first.Errors)}");

        var second = await session.ExecuteAsync(
            "const hsTypedNext: number = hsTypedValue.value + 1;\n" +
            "console.log(hsTypedNext);",
            workingDirectory,
            timeout.Token);
        Require(
            second.Errors.Count == 0 && second.Output.Contains("43"),
            "The TypeScript terminal did not preserve state between commands.");

        var unsupported = await session.ExecuteAsync(
            "enum HypershellStatus { Ready = 'ready' }",
            workingDirectory,
            timeout.Token);
        Require(
            unsupported.Errors.Any(error =>
                error.Contains(
                    "TypeScript",
                    StringComparison.OrdinalIgnoreCase)
                || error.Contains(
                    "ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX",
                    StringComparison.OrdinalIgnoreCase)),
            "Unsupported transform-required TypeScript syntax did not report a runner error.");

        var recovery = await session.ExecuteAsync(
            "console.log(hsTypedNext + 1);",
            workingDirectory,
            timeout.Token);
        Require(
            recovery.Errors.Count == 0 && recovery.Output.Contains("44"),
            "The TypeScript terminal did not recover after a rejected statement.");
    }

    private static async Task VerifyTypeScriptExamplesAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        foreach (var command in library.Commands)
        {
            foreach (var example in command.Examples)
            {
                using var session = TerminalSessionFactory.Create(
                    profile,
                    workingDirectory);
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                var result = await session.ExecuteAsync(
                    example.Code,
                    workingDirectory,
                    timeout.Token);
                Require(
                    result.Errors.Count == 0,
                    $"TypeScript example '{command.Id} / {example.Title}' failed: " +
                    string.Join(" | ", result.Errors));
            }
        }
    }

    private static async Task VerifyJavaLibraryAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        var adapter = new JavaCompiledLanguageAdapter();
        var toolchain = JavaCompiledLanguageAdapter.CreateToolchain(profile);
        foreach (var command in library.Commands.Where(command =>
                     command.CoverageTier is CommandCoverageTier.Guided
                         or CommandCoverageTier.Verified))
        {
            await VerifyJavaSnippetCompilesAsync(
                adapter,
                toolchain,
                RenderTemplate(command),
                workingDirectory,
                $"{command.Id} template");
            foreach (var example in command.Examples)
            {
                await VerifyJavaSnippetCompilesAsync(
                    adapter,
                    toolchain,
                    example.Code,
                    workingDirectory,
                    $"{command.Id} example '{example.Title}'");
            }
        }
    }

    private static async Task VerifyJavaSnippetCompilesAsync(
        JavaCompiledLanguageAdapter adapter,
        DiscoveredCompiledLanguageToolchain toolchain,
        string source,
        string workingDirectory,
        string context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var result = await adapter.ExecuteSnippetAsync(
            source,
            workingDirectory,
            toolchain,
            timeout.Token);
        Require(
            result.Outcome is CompiledLanguageExecutionOutcome.Succeeded
                or CompiledLanguageExecutionOutcome.RuntimeFailed,
            $"Java compilation failed for {context}: " +
            string.Join(" | ", result.Diagnostics.Append(result.Message)));
    }

    private static async Task VerifyJavaSessionAsync(
        TerminalProfile profile,
        string workingDirectory)
    {
        using var session = TerminalSessionFactory.Create(profile, workingDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var first = await session.ExecuteAsync(
            "var hsValue = 6 * 7; System.out.println(hsValue);",
            workingDirectory,
            timeout.Token);
        Require(
            first.Errors.Count == 0 && first.Output.Contains("42"),
            $"Java statement execution failed: {string.Join(" | ", first.Errors)}");

        var second = await session.ExecuteAsync(
            "System.out.println(hsValue);",
            workingDirectory,
            timeout.Token);
        Require(
            second.Errors.Count > 0,
            "The Java terminal unexpectedly retained state across isolated programs.");
    }

    private static async Task VerifyCppLibraryAsync(
        CommandLibrary library,
        TerminalProfile profile,
        string workingDirectory)
    {
        var adapter = new CppCompiledLanguageAdapter();
        var toolchain = CppCompiledLanguageAdapter.CreateToolchain(profile);
        foreach (var command in library.Commands.Where(command =>
                     command.CoverageTier is CommandCoverageTier.Guided
                         or CommandCoverageTier.Verified))
        {
            await VerifyCppSnippetAsync(
                adapter,
                toolchain,
                RenderTemplate(command),
                workingDirectory,
                $"{command.Id} template");
            foreach (var example in command.Examples)
            {
                await VerifyCppSnippetAsync(
                    adapter,
                    toolchain,
                    example.Code,
                    workingDirectory,
                    $"{command.Id} example '{example.Title}'");
            }
        }
    }

    private static async Task VerifyCppSnippetAsync(
        CppCompiledLanguageAdapter adapter,
        DiscoveredCompiledLanguageToolchain toolchain,
        string source,
        string workingDirectory,
        string context)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var result = await adapter.ExecuteSnippetAsync(
            source,
            workingDirectory,
            toolchain,
            timeout.Token);
        Require(
            result.Outcome == CompiledLanguageExecutionOutcome.Succeeded,
            $"C++ verification failed for {context}: " +
            string.Join(" | ", result.Diagnostics.Append(result.Message)));
    }

    private static async Task VerifyCppSessionAsync(
        TerminalProfile profile,
        string workingDirectory)
    {
        using var session = TerminalSessionFactory.Create(profile, workingDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var first = await session.ExecuteAsync(
            "#include <iostream>\nint hsValue = 6 * 7;\nstd::cout << hsValue << '\\n';",
            workingDirectory,
            timeout.Token);
        Require(
            first.Errors.Count == 0 && first.Output.Contains("42"),
            $"C++ statement execution failed: {string.Join(" | ", first.Errors)}");

        var second = await session.ExecuteAsync(
            "#include <iostream>\nstd::cout << hsValue << '\\n';",
            workingDirectory,
            timeout.Token);
        Require(
            second.Errors.Count > 0,
            "The C++ terminal unexpectedly retained state across isolated programs.");
    }

    private static async Task VerifyWebGuidanceAsync(
        CommandLibrary html,
        CommandLibrary css,
        CommandLibrary webApis,
        TerminalProfile nodeProfile,
        string workingDirectory)
    {
        foreach (var command in html.Commands.Where(command =>
                     command.CoverageTier is CommandCoverageTier.Guided
                         or CommandCoverageTier.Verified))
        {
            VerifyRenderedParameters(command, "HTML");
            Require(
                LooksLikeMarkup(RenderTemplate(command))
                && command.Examples.All(example => LooksLikeMarkup(example.Code)),
                $"HTML entry '{command.Id}' does not contain an insertable markup pattern.");
        }

        foreach (var command in css.Commands.Where(command =>
                     command.CoverageTier is CommandCoverageTier.Guided
                         or CommandCoverageTier.Verified))
        {
            VerifyRenderedParameters(command, "CSS");
            Require(
                HasBalancedBraces(RenderTemplate(command))
                && command.Examples.All(example => HasBalancedBraces(example.Code)),
                $"CSS entry '{command.Id}' contains unbalanced rule braces.");
        }

        foreach (var command in webApis.Commands.Where(command =>
                     command.CoverageTier is CommandCoverageTier.Guided
                         or CommandCoverageTier.Verified))
        {
            VerifyRenderedParameters(command, "Web APIs");
            await VerifySyntaxAsync(
                nodeProfile,
                RenderTemplate(command),
                workingDirectory,
                $"{command.Id} browser template");
            foreach (var example in command.Examples)
            {
                await VerifySyntaxAsync(
                    nodeProfile,
                    example.Code,
                    workingDirectory,
                    $"{command.Id} browser example '{example.Title}'");
            }
        }
    }

    private static async Task VerifyWebDocumentWorkflowAsync(string verifierRoot)
    {
        var htmlTemplates = DocumentTemplateCatalog.GetTemplates("html");
        var defaultHtml = htmlTemplates.Single(template => template.IsDefault);
        var selfContained = htmlTemplates.Single(template =>
            template.Id == "html-self-contained-app");
        Require(
            htmlTemplates.Count >= 3
            && defaultHtml.Id == "html-page"
            && defaultHtml.Content.Contains("styles.css", StringComparison.Ordinal)
            && defaultHtml.Content.Contains("app.js", StringComparison.Ordinal)
            && selfContained.Content.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase)
            && selfContained.Content.Contains("<style>", StringComparison.OrdinalIgnoreCase)
            && selfContained.Content.Contains("<script type=\"module\">", StringComparison.OrdinalIgnoreCase),
            "HTML document templates do not cover linked and immediately previewable pages.");

        var cssTemplate = DocumentTemplateCatalog.GetTemplates("css")
            .Single(template => template.IsDefault);
        var javascriptTemplate = DocumentTemplateCatalog.GetTemplates("javascript")
            .Single(template => template.IsDefault);
        var typescriptTemplate = DocumentTemplateCatalog.GetTemplates("typescript")
            .Single(template => template.IsDefault);
        Require(
            cssTemplate.Content.Contains(":root", StringComparison.Ordinal)
            && javascriptTemplate.Content.Contains("document.querySelector", StringComparison.Ordinal)
            && typescriptTemplate.Content.Contains("querySelector<", StringComparison.Ordinal)
            && typescriptTemplate.Description.Contains("compiled", StringComparison.OrdinalIgnoreCase),
            "The CSS, JavaScript, and TypeScript browser starter templates lost their intended roles.");

        var documentService = new TextDocumentService();
        var document = DocumentTabViewModel.CreateUntitled(
            new NewDocumentRequest(
                "index.html",
                EditorLanguageCatalog.CreateDefaultSettings("html"),
                InitialContent: selfContained.Content,
                TemplateId: selfContained.Id),
            documentService);
        Require(
            document.IsHtmlDocument
            && document.CanPreviewInBrowser
            && document.HasUnsavedChanges
            && document.Content == selfContained.Content,
            "A selected HTML starter template was not applied to the new document.");

        var outputDirectory = Path.Combine(verifierRoot, "web-document-workflow");
        var outputPath = Path.Combine(outputDirectory, "index.html");
        await document.SaveAsync(outputPath);
        var normalizedAddress = BrowserAddressService.Normalize(outputPath);
        Require(
            document.HasFilePath
            && !document.HasUnsavedChanges
            && File.Exists(outputPath)
            && new Uri(normalizedAddress).IsFile,
            "A saved HTML document did not become a local browser-preview address.");

        var javascriptDocument = DocumentTabViewModel.CreateUntitled(
            new NewDocumentRequest(
                "app.js",
                EditorLanguageCatalog.CreateDefaultSettings("javascript")),
            documentService);
        Require(
            !javascriptDocument.CanPreviewInBrowser,
            "A non-HTML document incorrectly exposed direct page preview.");
    }

    private static void VerifyRenderedParameters(
        CommandDefinition command,
        string language)
    {
        var rendered = RenderTemplate(command);
        foreach (var parameter in command.Parameters)
        {
            var placeholder = string.IsNullOrWhiteSpace(parameter.Placeholder)
                ? parameter.Name
                : parameter.Placeholder;
            Require(
                !rendered.Contains($"{{{placeholder}}}", StringComparison.Ordinal),
                $"{language} entry '{command.Id}' left parameter '{placeholder}' unresolved.");
        }
    }

    private static bool LooksLikeMarkup(string source) =>
        !string.IsNullOrWhiteSpace(source)
        && source.Contains('<')
        && source.Contains('>');

    private static bool HasBalancedBraces(string source) =>
        !string.IsNullOrWhiteSpace(source)
        && source.Count(character => character == '{')
        == source.Count(character => character == '}');

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
