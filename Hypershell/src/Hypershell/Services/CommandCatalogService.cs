using Hypershell.Extensibility;
using Hypershell.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class CommandCatalogService
{
    public const string PowerShellCatalogId = "powershell-core";
    public static IReadOnlySet<string> UnpublishedCSharpCatalogIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csharp-basics",
            "csharp-language",
            "csharp-streams",
            "csharp-runtime",
            "csharp-concurrency",
            "csharp-networking"
        };
    public static IReadOnlySet<string> RetiredOfficialCatalogIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python-essentials",
            "javascript-essentials",
            "typescript-essentials",
            "java-essentials",
            "html-essentials",
            "css-essentials",
            "web-api-essentials"
        };
    private static readonly IReadOnlyDictionary<string, string>
        PowerShellFallbackSummaries =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConvertFrom-Json"] = "Converts a JSON-formatted string into PowerShell objects.",
                ["ConvertTo-Json"] = "Converts PowerShell objects into a JSON-formatted string.",
                ["Copy-Item"] = "Copies files, folders, registry keys, and other provider items to another location.",
                ["Export-Csv"] = "Writes objects to a CSV file by storing their property values as rows.",
                ["ForEach-Object"] = "Runs an operation once for each input object in the pipeline.",
                ["Get-ChildItem"] = "Lists the items and child items at one or more provider locations.",
                ["Get-Command"] = "Finds commands available to the current PowerShell session.",
                ["Get-Content"] = "Reads the content of a file or another provider-backed item.",
                ["Get-Help"] = "Displays help information about PowerShell commands and concepts.",
                ["Get-Location"] = "Returns the current working location.",
                ["Get-Member"] = "Shows the properties and methods available on an object.",
                ["Get-Process"] = "Gets information about processes running on the local computer.",
                ["Get-Service"] = "Gets services available on the computer.",
                ["Import-Csv"] = "Creates objects from the rows in a CSV file.",
                ["Move-Item"] = "Moves an item from one provider location to another.",
                ["New-Item"] = "Creates a new item such as a file, folder, or registry key.",
                ["Read-Host"] = "Reads a line of input from the user through the terminal host.",
                ["Remove-Item"] = "Deletes the selected provider item.",
                ["Select-Object"] = "Selects objects or particular properties from pipeline input.",
                ["Set-Location"] = "Changes the current working location.",
                ["Start-Process"] = "Starts one or more processes on the local computer.",
                ["Stop-Process"] = "Stops one or more running processes.",
                ["Test-Path"] = "Checks whether all elements of a provider path exist.",
                ["Where-Object"] = "Filters pipeline input using a property value or script condition.",
                ["Write-Host"] = "Writes formatted output directly to the terminal host.",
                ["Write-Output"] = "Sends objects to the next command in the pipeline."
            };
    private static readonly string[] BundledPowerShellCoreModules =
    [
        "CimCmdlets",
        "Microsoft.PowerShell.Diagnostics",
        "Microsoft.PowerShell.Host",
        "Microsoft.PowerShell.Management",
        "Microsoft.PowerShell.Security",
        "Microsoft.PowerShell.Utility",
        "Microsoft.WSMan.Management",
        "PSDiagnostics"
    ];

    private readonly SettingsService _settingsService;
    private readonly PowerShellGuideCatalog _powerShellGuideCatalog;
    private ExtensionDiscoverySnapshot? _extensionCatalog;
    private readonly ConcurrentDictionary<string, Lazy<Task<CommandGuideContent>>>
        _commandGuideCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<CommandDefinition>>>>
        _libraryEntryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<CommandGuideShard>>
        _guideShardCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CommandCatalogService(
        SettingsService settingsService,
        ExtensionDiscoverySnapshot? extensionCatalog = null)
    {
        _settingsService = settingsService;
        _extensionCatalog = extensionCatalog;
        _powerShellGuideCatalog = LoadBundledPowerShellGuideCatalog();
    }

    public string CommandLibraryDirectory => _settingsService.CommandLibraryDirectory;

    public void SetExtensionCatalog(ExtensionDiscoverySnapshot extensionCatalog)
    {
        ArgumentNullException.ThrowIfNull(extensionCatalog);
        _extensionCatalog = extensionCatalog;
    }

    public async Task<IReadOnlyList<CommandLibrary>> LoadAsync()
    {
        Directory.CreateDirectory(CommandLibraryDirectory);

        var bundledLibrariesTask = Task.Run(LoadBundledLibraries);
        var csharpFrameworkLibrariesTask = Task.Run(
            () => new CSharpFrameworkCatalogService().Discover());
        var dotNetToolchainLibrariesTask = Task.Run(
            () => new DotNetToolchainCatalogService().Discover());
        var powerShellCatalog = await Task.Run(LoadPowerShellCatalog);
        var bundledLibraries = await bundledLibrariesTask;
        var csharpFrameworkLibraries =
            await csharpFrameworkLibrariesTask;
        var dotNetToolchainLibraries =
            await dotNetToolchainLibrariesTask;
        var reservedLibraryIds = bundledLibraries
            .Select(library => library.Id)
            .Concat(csharpFrameworkLibraries.Select(library => library.Id))
            .Concat(dotNetToolchainLibraries.Select(library => library.Id))
            .Concat(UnpublishedCSharpCatalogIds)
            .Concat(RetiredOfficialCatalogIds)
            .Append(PowerShellCatalogId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contentPackageLibraries = await Task.Run(
            () => LoadContentPackageLibraries(
                _extensionCatalog?.LibraryCandidates ?? [],
                reservedLibraryIds));
        var reservedWithContentPackageIds = reservedLibraryIds
            .Concat(contentPackageLibraries.Select(library => library.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalLibraries = await Task.Run(
            () => LoadExternalLibraries(reservedWithContentPackageIds));

        var libraries = new List<CommandLibrary> { powerShellCatalog };
        libraries.AddRange(csharpFrameworkLibraries);
        libraries.AddRange(dotNetToolchainLibraries);
        libraries.AddRange(bundledLibraries);
        libraries.AddRange(contentPackageLibraries);
        libraries.AddRange(externalLibraries);
        CatalogRuntimeVersionService.Apply(libraries);
        return libraries;
    }

    public async Task<IReadOnlyList<CommandDefinition>> LoadLibraryEntriesAsync(
        CommandLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (!library.HasDeferredEntries)
        {
            return library.Commands;
        }

        var entries = await _libraryEntryCache.GetOrAdd(
                library.Id,
                _ => new Lazy<Task<IReadOnlyList<CommandDefinition>>>(
                    () => Task.Run(() => LoadDeferredEntries(library))))
            .Value;

        lock (library)
        {
            if (library.Commands.Count == 0)
            {
                library.Commands = entries.ToList();
                PrepareLibrary(
                    library,
                    library.SourcePath,
                    library.Origin);
            }

            return library.Commands;
        }
    }

    public async Task<CommandDefinition?> ResolveEntryReferenceAsync(
        CommandLibrary library,
        string entryId)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return null;
        }

        var reference = entryId.Trim();
        var entries = await LoadLibraryEntriesAsync(library);
        return entries.FirstOrDefault(command =>
            string.Equals(command.Id, reference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                command.SourceEntryId,
                reference,
                StringComparison.OrdinalIgnoreCase)
            || command.LegacyIds.Any(alias =>
                string.Equals(alias, reference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    $"{library.Id}:{alias}",
                    reference,
                    StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<CommandDefinition> LoadDeferredEntries(
        CommandLibrary library)
    {
        var descriptorPath = Path.GetFullPath(library.SourcePath);
        var descriptorDirectory = Path.GetDirectoryName(descriptorPath)
                                  ?? Environment.CurrentDirectory;
        var allowedRoot = Path.GetFullPath(descriptorDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var entries = new List<CommandDefinition>();
        var entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in library.EntryFiles)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException(
                    $"Library '{library.DisplayName}' contains an invalid entry shard path.");
            }

            var shardPath = Path.GetFullPath(Path.Combine(
                descriptorDirectory,
                relativePath));
            if (!shardPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetExtension(shardPath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(shardPath))
            {
                throw new InvalidDataException(
                    $"Library '{library.DisplayName}' entry shard '{relativePath}' is unavailable or escapes its catalog directory.");
            }

            var shard = JsonSerializer.Deserialize<CommandLibraryEntryShard>(
                File.ReadAllText(shardPath),
                _jsonOptions);
            var expectedShardLibraryId = string.IsNullOrWhiteSpace(
                library.SourceCatalogId)
                ? library.Id
                : library.SourceCatalogId;
            if (shard is null
                || shard.SchemaVersion != 2
                || !string.Equals(
                    shard.LibraryId,
                    expectedShardLibraryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Entry shard '{relativePath}' does not belong to schema-v2 library '{library.Id}'.");
            }

            foreach (var entry in shard.Entries ?? [])
            {
                var sourceEntryId = string.IsNullOrWhiteSpace(entry.Id)
                    ? entry.Name
                    : entry.Id.Trim();
                var entryId = library.Origin == CommandLibraryOrigin.ContentPackage
                              && !sourceEntryId.StartsWith(
                                  $"{library.Id}:",
                                  StringComparison.OrdinalIgnoreCase)
                    ? $"{library.Id}:{sourceEntryId}"
                    : sourceEntryId;
                if (!entryIds.Add(entryId))
                {
                    throw new InvalidDataException(
                        $"Library '{library.DisplayName}' repeats entry ID '{entryId}'.");
                }

                entry.Id = entryId;
                entry.SourceEntryId = sourceEntryId;
                if (!string.IsNullOrWhiteSpace(entry.GuideFile)
                    && !library.GuideFiles.Contains(
                        entry.GuideFile,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Entry '{entryId}' references undeclared guide shard '{entry.GuideFile}'.");
                }
                entries.Add(entry);
            }
        }

        if (library.DeclaredEntryCount > 0
            && entries.Count != library.DeclaredEntryCount)
        {
            throw new InvalidDataException(
                $"Library '{library.DisplayName}' declares {library.DeclaredEntryCount:N0} entries but its shards contain {entries.Count:N0}.");
        }

        return entries;
    }

    private CommandLibrary LoadPowerShellCatalog()
    {
        // The catalog has its own runspace so discovery never shares the WPF UI's
        // default PowerShell context or a terminal tab's state.
        using var runspace = CreatePowerShellCoreRunspace();
        using var versionShell = PowerShell.Create();
        versionShell.Runspace = runspace;
        var powerShellVersion = versionShell
            .AddScript("$PSVersionTable.PSVersion.ToString()")
            .Invoke()
            .FirstOrDefault()
            ?.BaseObject
            ?.ToString()
            ?? "embedded";
        var library = new CommandLibrary
        {
            SchemaVersion = 2,
            Id = PowerShellCatalogId,
            DisplayName = "PowerShell Core",
            Description = "Commands discovered from the embedded PowerShell engine and classified by their actual modules.",
            Language = "PowerShell",
            Category = string.Empty,
            Kind = CommandLibraryKind.Core,
            CanonicalName = "PowerShell Core Commands",
            Ecosystem = "PowerShell",
            TargetVersion = $"PowerShell {powerShellVersion}",
            SourceVersion = $"Embedded PowerShell {powerShellVersion}",
            SourceUrl = "https://learn.microsoft.com/powershell/",
            License = "MIT and Microsoft documentation terms",
            ExecutionTarget = ExecutionTarget.PowerShell,
            Origin = CommandLibraryOrigin.Runtime,
            CreateStandaloneProfile = true,
            ActiveRuntimeVersion = $"PowerShell {powerShellVersion}",
            UsesInstalledCompatibleIndex = true
        };

        var discovered = DiscoverPowerShellCoreCommands(runspace);

        foreach (var command in discovered.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
        {
            var moduleName = string.IsNullOrWhiteSpace(command.ModuleName) ? "PowerShell" : command.ModuleName;
            var definition = new CommandDefinition
            {
                Id = $"{PowerShellCatalogId}:{command.CommandType}:{moduleName}:{command.Name}",
                CatalogId = PowerShellCatalogId,
                Name = command.Name,
                DisplayName = command.Name,
                Description = $"{command.CommandType} · {moduleName}",
                ModuleName = moduleName,
                CommandType = command.CommandType.ToString(),
                SymbolKind = CommandSymbolKind.Command,
                CoverageTier = CommandCoverageTier.Indexed,
                BrowsePath =
                [
                    new CommandBrowsePathSegment
                    {
                        Kind = "Module",
                        Name = moduleName
                    },
                    new CommandBrowsePathSegment
                    {
                        Kind = "Command",
                        Name = command.Name
                    }
                ],
                Template = command.Name,
                ExecutionTarget = ExecutionTarget.PowerShell,
                // Parameter schemas are resolved only when a user selects a
                // command. PowerShell's all-command enumeration deliberately
                // returns lightweight metadata for performance.
                Parameters = []
            };

            library.Commands.Add(definition);
        }

        return library;
    }

    public Task<List<CommandParameterDefinition>> LoadParametersAsync(CommandDefinition command)
    {
        if (command.ExecutionTarget != ExecutionTarget.PowerShell || string.IsNullOrWhiteSpace(command.Name))
        {
            return Task.FromResult(new List<CommandParameterDefinition>());
        }

        return Task.Run(() => LoadParameters(command));
    }

    public Task<CommandGuideContent> LoadCommandGuideAsync(
        CommandDefinition command)
    {
        if (command.HasDeferredGuide)
        {
            var deferredCacheKey = string.IsNullOrWhiteSpace(command.Id)
                ? $"{command.CatalogId}:{command.Name}:guide"
                : $"{command.Id}:guide";
            return _commandGuideCache
                .GetOrAdd(
                    deferredCacheKey,
                    _ => new Lazy<Task<CommandGuideContent>>(
                        () => Task.Run(() =>
                            LoadDeferredCommandGuide(command))))
                .Value;
        }

        if (command.ExecutionTarget != ExecutionTarget.PowerShell
            || command.CompositionMode
            == CommandCompositionMode.Statement)
        {
            return Task.FromResult(BuildCatalogCommandGuide(command));
        }

        var cacheKey = string.IsNullOrWhiteSpace(command.Id)
            ? $"{command.CatalogId}:{command.Name}"
            : command.Id;
        return _commandGuideCache
            .GetOrAdd(
                cacheKey,
                _ => new Lazy<Task<CommandGuideContent>>(
                    () => Task.Run(() => LoadPowerShellCommandGuide(command))))
            .Value;
    }

    private CommandGuideContent LoadDeferredCommandGuide(
        CommandDefinition command)
    {
        var descriptorPath = Path.GetFullPath(command.SourceCatalogPath);
        var descriptorDirectory = Path.GetDirectoryName(descriptorPath)
                                  ?? Environment.CurrentDirectory;
        var allowedRoot = Path.GetFullPath(descriptorDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(command.GuideFile))
        {
            throw new InvalidDataException(
                $"Guide path for '{command.Id}' must be catalog-relative.");
        }

        var guidePath = Path.GetFullPath(Path.Combine(
            descriptorDirectory,
            command.GuideFile));
        if (!guidePath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetExtension(guidePath),
                ".json",
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(guidePath))
        {
            throw new InvalidDataException(
                $"Guide shard '{command.GuideFile}' is unavailable or escapes its catalog directory.");
        }

        var shard = _guideShardCache.GetOrAdd(
                guidePath,
                path => new Lazy<CommandGuideShard>(() =>
                    JsonSerializer.Deserialize<CommandGuideShard>(
                        File.ReadAllText(path),
                        _jsonOptions)
                    ?? throw new InvalidDataException(
                        $"Guide shard '{command.GuideFile}' is empty.")))
            .Value;
        var expectedShardLibraryId = string.IsNullOrWhiteSpace(
            command.SourceCatalogId)
            ? command.CatalogId
            : command.SourceCatalogId;
        if (shard.SchemaVersion != 2
            || !string.Equals(
                shard.LibraryId,
                expectedShardLibraryId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Guide shard '{command.GuideFile}' does not belong to '{command.CatalogId}'.");
        }

        var sourceEntryId = string.IsNullOrWhiteSpace(command.SourceEntryId)
            ? command.Id
            : command.SourceEntryId;
        var guide = shard.Guides.SingleOrDefault(candidate => string.Equals(
            candidate.EntryId,
            sourceEntryId,
            StringComparison.OrdinalIgnoreCase));
        if (guide is null)
        {
            throw new InvalidDataException(
                $"Guide shard '{command.GuideFile}' does not contain '{command.Id}'.");
        }

        lock (command)
        {
            command.GuideDefinition = guide.Definition;
            command.Parameters = guide.Parameters?.ToList() ?? [];
            command.Examples = guide.Examples?.ToList() ?? [];
            NormalizeExampleImagePaths(
                command.Examples,
                descriptorDirectory);
            command.IsGuideLoaded = true;
        }

        return BuildCatalogCommandGuide(command);
    }

    public string GetActionDescription(CommandDefinition command)
    {
        var (actionName, _) = GetCommandFacets(command);
        return ResolveActionDescription(command, actionName);
    }

    public string GetObjectDescription(CommandDefinition command)
    {
        var (_, objectName) = GetCommandFacets(command);
        return ResolveObjectDescription(command, objectName);
    }

    private static List<CommandParameterDefinition> LoadParameters(CommandDefinition definition)
    {
        using var runspace = CreatePowerShellCoreRunspace();
        var candidates = DiscoverPowerShellCoreCommands(runspace, definition.Name);

        var command = candidates.FirstOrDefault(candidate =>
                          string.Equals(candidate.Name, definition.Name, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(candidate.ModuleName ?? "PowerShell", definition.ModuleName, StringComparison.OrdinalIgnoreCase))
                      ?? candidates.FirstOrDefault(candidate => string.Equals(candidate.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
        return command is null ? [] : ReadParametersSafely(command);
    }

    private CommandGuideContent LoadPowerShellCommandGuide(
        CommandDefinition command)
    {
        var fallback = BuildCatalogCommandGuide(command);
        try
        {
            using var runspace = CreatePowerShellCoreRunspace();
            using var shell = PowerShell.Create();
            shell.Runspace = runspace;
            var escapedName = command.Name.Replace("'", "''", StringComparison.Ordinal);
            var escapedModule = command.ModuleName.Replace("'", "''", StringComparison.Ordinal);
            var script =
                $"$help = Get-Help -Name '{escapedName}' -ErrorAction SilentlyContinue; " +
                "if ($null -ne $help) { " +
                "$description = @($help.Description | ForEach-Object { $_.Text }) -join [Environment]::NewLine; " +
                $"$commandMetadata = @(Get-Command -Name '{escapedName}' -ErrorAction SilentlyContinue | Where-Object {{ [string]::IsNullOrWhiteSpace('{escapedModule}') -or $_.ModuleName -eq '{escapedModule}' }} | Select-Object -First 1)[0]; " +
                "$syntax = if ($null -ne $commandMetadata) { @($commandMetadata.ParameterSets | ForEach-Object { " +
                "$defaultLabel = if ($_.IsDefault) { ' (Default)' } else { '' }; " +
                "'# ' + $_.Name + $defaultLabel + [Environment]::NewLine + $commandMetadata.Name + ' ' + $_.ToString() " +
                "}) -join ([Environment]::NewLine + [Environment]::NewLine) } else { '' }; " +
                "if ([string]::IsNullOrWhiteSpace($syntax)) { $syntax = (($help.Syntax | Out-String -Width 4096).Trim()) }; " +
                "$examples = @($help.Examples.Example | ForEach-Object { " +
                "$remarks = @($_.Remarks | ForEach-Object { if ($null -ne $_.Text) { [string]$_.Text } else { [string]$_ } }) -join [Environment]::NewLine; " +
                "[pscustomobject]@{ Title = ([string]$_.Title).Trim(); Code = ([string]$_.Code).Trim(); Explanation = ([string]$remarks).Trim() } }); " +
                "$examplesJson = if ($examples.Count -gt 0) { $examples | ConvertTo-Json -Compress -Depth 5 -AsArray } else { '[]' }; " +
                "[pscustomobject]@{ Synopsis = [string]$help.Synopsis; Description = [string]$description; Syntax = [string]$syntax; ExamplesJson = [string]$examplesJson } }";
            var help = shell.AddScript(script).Invoke().FirstOrDefault();
            if (help is null)
            {
                return fallback;
            }

            var summary = help.Properties["Synopsis"]?.Value?.ToString()?.Trim()
                ?? string.Empty;
            var definition = help.Properties["Description"]?.Value?.ToString()?.Trim()
                ?? string.Empty;
            var syntax = help.Properties["Syntax"]?.Value?.ToString()?.Trim()
                ?? string.Empty;
            var examples = ParsePowerShellExamples(
                help.Properties["ExamplesJson"]?.Value?.ToString());
            var hasProseSummary = !string.IsNullOrWhiteSpace(summary)
                && !summary.TrimStart().StartsWith(
                    command.Name,
                    StringComparison.OrdinalIgnoreCase);
            var hasDescription = !string.IsNullOrWhiteSpace(definition);
            if (!hasProseSummary && !hasDescription)
            {
                return fallback with
                {
                    Syntax = string.IsNullOrWhiteSpace(syntax)
                        ? fallback.Syntax
                        : syntax,
                    SourceLabel = examples.Count > 0
                        ? fallback.IsFallback
                            ? "Local PowerShell help"
                            : fallback.SourceLabel
                        : fallback.SourceLabel,
                    Examples = examples.Count > 0
                        ? examples.Take(1).ToList()
                        : fallback.Examples
                };
            }

            if (!fallback.IsFallback)
            {
                return fallback with
                {
                    Syntax = string.IsNullOrWhiteSpace(syntax)
                        ? fallback.Syntax
                        : syntax,
                    Examples = fallback.Examples.Count > 0
                        ? fallback.Examples
                        : examples.Take(1).ToList()
                };
            }

            return fallback with
            {
                Summary = hasProseSummary ? summary : fallback.Summary,
                Definition = hasDescription
                    ? definition
                    : hasProseSummary
                        ? summary
                        : fallback.Definition,
                Syntax = string.IsNullOrWhiteSpace(syntax)
                    ? fallback.Syntax
                    : syntax,
                SourceLabel = "Local PowerShell help",
                IsFallback = false,
                Examples = examples.Take(1).ToList()
            };
        }
        catch
        {
            return fallback;
        }
    }

    private CommandGuideContent BuildCatalogCommandGuide(
        CommandDefinition command)
    {
        var (actionName, objectName) = GetCommandFacets(command);
        var actionDescription = ResolveActionDescription(
            command,
            actionName);
        var objectDescription = ResolveObjectDescription(
            command,
            objectName);
        var usesPowerShellParameterSets =
            command.ExecutionTarget == ExecutionTarget.PowerShell
            && command.CompositionMode
            != CommandCompositionMode.Statement;

        if (usesPowerShellParameterSets
            && _powerShellGuideCatalog.Commands.TryGetValue(
                command.Name,
                out var bundledGuide))
        {
            return new CommandGuideContent(
                bundledGuide.Summary,
                bundledGuide.Definition,
                actionName,
                actionDescription,
                objectName,
                objectDescription,
                string.IsNullOrWhiteSpace(bundledGuide.Syntax)
                    ? command.Name
                    : bundledGuide.Syntax,
                string.IsNullOrWhiteSpace(_powerShellGuideCatalog.SourceLabel)
                    ? "Microsoft Learn · PowerShell documentation"
                    : _powerShellGuideCatalog.SourceLabel,
                bundledGuide.SourceUrl,
                true,
                false,
                bundledGuide.Examples.Take(1).Select(CloneExample).ToList());
        }

        var summary = usesPowerShellParameterSets
            ? BuildFallbackSummary(command)
            : string.IsNullOrWhiteSpace(command.Description)
                ? $"{command.Name} is a .NET API member."
                : command.Description.Trim();
        var syntax = string.IsNullOrWhiteSpace(command.Template)
            ? command.Name
            : command.Template.Trim();
        var definition = string.IsNullOrWhiteSpace(command.GuideDefinition)
            ? summary
            : command.GuideDefinition.Trim();
        var sourceLabel = string.IsNullOrWhiteSpace(
            command.SourceLabelOverride)
            ? DotNetToolchainCatalogService.IsToolchainLibraryId(
                command.CatalogId)
                ? "Installed .NET toolchain help"
                : command.CompositionMode == CommandCompositionMode.Statement
                && command.ExecutionTarget == ExecutionTarget.PowerShell
                    ? "Installed .NET API metadata · PowerShell syntax"
                    : command.ExecutionTarget == ExecutionTarget.PowerShell
                        ? "Generated from PowerShell metadata"
                        : "C# library metadata"
            : command.SourceLabelOverride;
        return new CommandGuideContent(
            summary,
            definition,
            actionName,
            actionDescription,
            objectName,
            objectDescription,
            syntax,
            sourceLabel,
            command.DocumentationUrl,
            usesPowerShellParameterSets,
            usesPowerShellParameterSets,
            command.Examples.Take(1).Select(CloneExample).ToList(),
            GetSyntaxLanguage(command));
    }

    private static string GetSyntaxLanguage(CommandDefinition command) =>
        DotNetToolchainCatalogService.IsToolchainLibraryId(command.CatalogId)
            ? ".NET CLI"
            : command.PreferredShellKind switch
            {
                TerminalShellKind.Python => "Python",
                TerminalShellKind.NodeJs => "JavaScript",
                TerminalShellKind.TypeScript => "TypeScript",
                TerminalShellKind.Java => "Java",
                TerminalShellKind.Cpp => "C++",
                TerminalShellKind.External => "source",
                _ => command.ExecutionTarget == ExecutionTarget.PowerShell
                    ? "PowerShell"
                    : "C#"
            };

    private static (string Action, string Object) GetCommandFacets(
        CommandDefinition command)
    {
        if (!string.IsNullOrWhiteSpace(command.Action)
            || !string.IsNullOrWhiteSpace(command.ObjectName))
        {
            return (command.Action.Trim(), command.ObjectName.Trim());
        }

        var separator = command.Name.IndexOf('-');
        return separator > 0 && separator < command.Name.Length - 1
            ? (command.Name[..separator], command.Name[(separator + 1)..])
            : (string.Empty, string.Empty);
    }

    private string ResolveActionDescription(
        CommandDefinition command,
        string actionName)
    {
        if (!string.IsNullOrWhiteSpace(command.ActionDescription))
        {
            return command.ActionDescription.Trim();
        }

        if (!string.IsNullOrWhiteSpace(actionName)
            && _powerShellGuideCatalog.Actions.TryGetValue(
                actionName,
                out var description))
        {
            return description;
        }

        return string.IsNullOrWhiteSpace(actionName)
            ? string.Empty
            : $"“{SplitPascalCase(actionName)}” names the operation performed by this command. The complete definition below explains what that operation changes or returns.";
    }

    private string ResolveObjectDescription(
        CommandDefinition command,
        string objectName)
    {
        if (!string.IsNullOrWhiteSpace(command.ObjectDescription))
        {
            return command.ObjectDescription.Trim();
        }

        if (!string.IsNullOrWhiteSpace(objectName)
            && _powerShellGuideCatalog.Objects.TryGetValue(
                objectName,
                out var description))
        {
            return description;
        }

        return string.IsNullOrWhiteSpace(objectName)
            ? string.Empty
            : $"“{SplitPascalCase(objectName)}” names the resource, value, or concept that this command works with. The command definition below identifies its exact role.";
    }

    private static List<CommandUsageExample> ParsePowerShellExamples(
        string? examplesJson)
    {
        if (string.IsNullOrWhiteSpace(examplesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CommandUsageExample>>(
                       examplesJson,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       })
                   ?.Where(example =>
                       !string.IsNullOrWhiteSpace(example.Code)
                       || !string.IsNullOrWhiteSpace(example.Explanation))
                   .ToList()
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string BuildFallbackSummary(CommandDefinition command)
    {
        if (PowerShellFallbackSummaries.TryGetValue(
                command.Name,
                out var curatedSummary))
        {
            return curatedSummary;
        }

        var separator = command.Name.IndexOf('-');
        if (separator > 0 && separator < command.Name.Length - 1)
        {
            var verb = command.Name[..separator];
            var noun = SplitPascalCase(command.Name[(separator + 1)..]);
            var operation = verb switch
            {
                "Get" => "Gets",
                "Set" => "Sets",
                "New" => "Creates",
                "Add" => "Adds",
                "Remove" => "Removes",
                "Clear" => "Clears",
                "Copy" => "Copies",
                "Move" => "Moves",
                "Start" => "Starts",
                "Stop" => "Stops",
                "Test" => "Tests",
                "Invoke" => "Invokes",
                "Read" => "Reads",
                "Write" => "Writes",
                _ => $"{verb}s"
            };
            return $"{operation} {noun}. This command is provided by {command.ModuleName}; its installed syntax below shows the inputs it accepts.";
        }

        return $"{command.Name} is a {command.CommandType.ToLowerInvariant()} provided by {command.ModuleName}.";
    }

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "the selected object";
        }

        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0
                && char.IsUpper(value[index])
                && (char.IsLower(value[index - 1])
                    || (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                characters.Add(' ');
            }

            characters.Add(char.ToLowerInvariant(value[index]));
        }

        return new string([.. characters]);
    }

    private static List<CommandParameterDefinition> ReadParametersSafely(CommandInfo command)
    {
        Dictionary<string, ParameterMetadata>? parameters;
        try
        {
            parameters = command.Parameters;
        }
        catch (PSNotSupportedException)
        {
            // Native executables are real commands but PowerShell cannot infer a
            // parameter schema for every executable. Keep them discoverable and
            // let the user add arguments directly in the composed command field.
            return [];
        }
        catch (RuntimeException)
        {
            return [];
        }
        catch (NullReferenceException)
        {
            return [];
        }

        if (parameters is null)
        {
            return [];
        }

        var definitions = new List<CommandParameterDefinition>();
        foreach (var parameter in parameters.Values
                     .Where(parameter => parameter is not null)
                     .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var parameterSets = parameter.ParameterSets;
                definitions.Add(new CommandParameterDefinition
                {
                    Name = parameter.Name,
                    DisplayName = $"-{parameter.Name}",
                    Description = parameter.ParameterType?.FullName ?? "PowerShell parameter",
                    TypeName = parameter.ParameterType?.Name ?? "Object",
                    IsRequired = parameterSets?.Values.Any(parameterSet => parameterSet.IsMandatory) ?? false,
                    IsSwitch = parameter.ParameterType == typeof(SwitchParameter)
                });
            }
            catch (PSNotSupportedException)
            {
                // Retain every parameter PowerShell can describe and skip only
                // the unusual one whose metadata provider cannot be queried.
            }
            catch (RuntimeException)
            {
            }
            catch (NullReferenceException)
            {
            }
        }

        return definitions;
    }

    private static Runspace CreatePowerShellCoreRunspace()
    {
        var sessionState = InitialSessionState.CreateDefault2();
        var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        return runspace;
    }

    private static List<CommandInfo> DiscoverPowerShellCoreCommands(
        Runspace runspace,
        string? commandName = null)
    {
        var commands = new List<CommandInfo>();
        using (var importedShell = PowerShell.Create())
        {
            importedShell.Runspace = runspace;
            importedShell.AddCommand("Get-Command");
            if (!string.IsNullOrWhiteSpace(commandName))
            {
                importedShell.AddParameter("Name", commandName);
            }

            importedShell.AddParameter("ListImported");
            commands.AddRange(ReadCommandInfo(importedShell.Invoke()));
        }

        using (var bundledModuleShell = PowerShell.Create())
        {
            bundledModuleShell.Runspace = runspace;
            bundledModuleShell.AddCommand("Get-Command");
            if (!string.IsNullOrWhiteSpace(commandName))
            {
                bundledModuleShell.AddParameter("Name", commandName);
            }

            // Query metadata only for the modules bundled with the PowerShell
            // SDK. This does not import them into a terminal and does not expose
            // installed Windows/user modules such as SmbShare or Appx.
            bundledModuleShell.AddParameter("Module", BundledPowerShellCoreModules);
            commands.AddRange(ReadCommandInfo(bundledModuleShell.Invoke()));
        }

        return commands
            .DistinctBy(
                command =>
                    $"{command.CommandType}\u001F{command.ModuleName}\u001F{command.Name}",
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<CommandInfo> ReadCommandInfo(
        IEnumerable<PSObject> output) =>
        output
            .Select(result => result.BaseObject as CommandInfo)
            .Where(command => command is not null)
            .Cast<CommandInfo>();

    private List<CommandLibrary> LoadExternalLibraries(
        IReadOnlySet<string> reservedLibraryIds)
    {
        var libraries = new List<CommandLibrary>();
        var knownLibraryIds = new HashSet<string>(
            reservedLibraryIds,
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(CommandLibraryDirectory))
        {
            return libraries;
        }

        foreach (var path in Directory.EnumerateFiles(CommandLibraryDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var library = JsonSerializer.Deserialize<CommandLibrary>(File.ReadAllText(path), _jsonOptions);
                if (library is null
                    || string.IsNullOrWhiteSpace(library.Id)
                    || library.SchemaVersion is not (1 or 2))
                {
                    continue;
                }

                library.Id = library.Id.Trim();
                if (!knownLibraryIds.Add(library.Id))
                {
                    continue;
                }

                PrepareLibrary(
                    library,
                    path,
                    CommandLibraryOrigin.External);

                libraries.Add(library);
            }
            catch (JsonException)
            {
                // A malformed optional library should not prevent the workspace from opening.
            }
            catch (IOException)
            {
                // A library can be changed by the user while the app is starting.
            }
        }

        return libraries;
    }

    private static List<CommandLibrary> LoadContentPackageLibraries(
        IReadOnlyList<ExtensionLibraryCandidate> candidates,
        IReadOnlySet<string> reservedLibraryIds)
    {
        var libraries = new List<CommandLibrary>();
        var knownLibraryIds = new HashSet<string>(
            reservedLibraryIds,
            StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.OrderBy(
                     candidate => candidate.QualifiedLibraryId,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!knownLibraryIds.Add(candidate.QualifiedLibraryId))
            {
                continue;
            }

            var library = candidate.CreateLibrarySnapshot();
            library.SourceCatalogId = library.Id;
            library.Id = candidate.QualifiedLibraryId;
            library.DisplayName = candidate.Contribution.DisplayName;
            PrepareLibrary(
                library,
                candidate.ContentFilePath,
                CommandLibraryOrigin.ContentPackage);
            library.SourceExtensionId =
                candidate.Package.Manifest?.Id ?? string.Empty;
            library.SourceExtensionVersion =
                candidate.Package.Manifest?.Version ?? string.Empty;
            library.SourcePublisher =
                candidate.Package.Manifest?.Publisher ?? string.Empty;
            library.SourcePackageSha256 = candidate.Package.PackageSha256;
            library.ContentProvenance = candidate.Contribution.Provenance;
            library.ContentOrigin = candidate.Contribution.ContentOrigin;
            library.ContentReviewState = candidate.Contribution.ReviewState;
            library.ContentReviewedBy = candidate.Contribution.ReviewedBy;

            var sourceLabel =
                $"Hyperlib package · {library.SourcePublisher} · "
                + (library.ContentReviewState == HyperlibReviewState.Reviewed
                    ? $"reviewed by {library.ContentReviewedBy}"
                    : "publisher draft");
            foreach (var command in library.Commands)
            {
                command.SourceEntryId = command.Id;
                if (!command.Id.StartsWith(
                        $"{library.Id}:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    command.Id = $"{library.Id}:{command.Id}";
                }

                command.SourceLabelOverride = sourceLabel;
            }

            libraries.Add(library);
        }

        return libraries;
    }

    private List<CommandLibrary> LoadBundledLibraries()
    {
        var bundledLibraryDirectory =
            Path.Combine(AppContext.BaseDirectory, "Assets", "CommandLibraries");
        var libraries = new List<CommandLibrary>();
        if (!Directory.Exists(bundledLibraryDirectory))
        {
            return libraries;
        }

        foreach (var path in Directory.EnumerateFiles(
                     bundledLibraryDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                var library = JsonSerializer.Deserialize<CommandLibrary>(
                    File.ReadAllText(path),
                    _jsonOptions);
                if (library is null
                    || string.IsNullOrWhiteSpace(library.Id)
                    || library.SchemaVersion is not (1 or 2))
                {
                    continue;
                }

                if (UnpublishedCSharpCatalogIds.Contains(
                        library.Id.Trim())
                    || RetiredOfficialCatalogIds.Contains(
                        library.Id.Trim()))
                {
                    continue;
                }

                PrepareLibrary(
                    library,
                    path,
                    CommandLibraryOrigin.Bundled);
                if (libraries.Any(candidate => string.Equals(
                        candidate.Id,
                        library.Id,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                libraries.Add(library);
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return libraries;
    }

    private static void PrepareLibrary(
        CommandLibrary library,
        string sourcePath,
        CommandLibraryOrigin origin)
    {
        library.Id = library.Id.Trim();
        library.DisplayName = string.IsNullOrWhiteSpace(library.DisplayName)
            ? library.Id
            : library.DisplayName.Trim();
        library.Description = library.Description?.Trim() ?? string.Empty;
        library.Language = string.IsNullOrWhiteSpace(library.Language)
            ? library.ExecutionTarget switch
            {
                ExecutionTarget.CSharp => "C#",
                ExecutionTarget.PowerShell => "PowerShell",
                _ => "External"
            }
            : library.Language.Trim();
        library.Category = library.Category?.Trim() ?? string.Empty;
        library.CanonicalName = string.IsNullOrWhiteSpace(library.CanonicalName)
            ? library.DisplayName
            : library.CanonicalName.Trim();
        library.Ecosystem = library.Ecosystem?.Trim() ?? string.Empty;
        library.TargetVersion = library.TargetVersion?.Trim() ?? string.Empty;
        library.SourceVersion = library.SourceVersion?.Trim() ?? string.Empty;
        library.SourceUrl = library.SourceUrl?.Trim() ?? string.Empty;
        library.License = library.License?.Trim() ?? string.Empty;
        library.ParentLibraryId =
            library.ParentLibraryId?.Trim() ?? string.Empty;
        library.FrameworkName =
            library.FrameworkName?.Trim() ?? string.Empty;
        library.FrameworkVersion =
            library.FrameworkVersion?.Trim() ?? string.Empty;
        library.TargetFramework =
            library.TargetFramework?.Trim() ?? string.Empty;
        library.ExecutionSupportMessage =
            library.ExecutionSupportMessage?.Trim() ?? string.Empty;
        library.ContentVersion = string.IsNullOrWhiteSpace(library.ContentVersion)
            ? "1.0.0"
            : library.ContentVersion.Trim();
        library.Keywords = (library.Keywords ?? [])
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        library.EntryFiles = (library.EntryFiles ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        library.GuideFiles = (library.GuideFiles ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        library.LegacyIds = (library.LegacyIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        library.Commands = (library.Commands ?? [])
            .OfType<CommandDefinition>()
            .ToList();
        library.ActionDescriptions ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        library.ObjectDescriptions ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        library.Origin = origin;
        library.SourcePath = Path.GetFullPath(sourcePath);
        library.SourceCatalogId = string.IsNullOrWhiteSpace(
            library.SourceCatalogId)
            ? library.Id
            : library.SourceCatalogId;

        NormalizeExampleImagePaths(
            library,
            Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory);
        ApplyLibraryFacetDescriptions(library);
        foreach (var command in library.Commands)
        {
            command.Parameters = (command.Parameters ?? [])
                .OfType<CommandParameterDefinition>()
                .ToList();
            command.Examples = (command.Examples ?? [])
                .OfType<CommandUsageExample>()
                .ToList();
            command.Keywords = (command.Keywords ?? [])
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Select(keyword => keyword.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            command.LegacyIds = (command.LegacyIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            command.GuideFile = command.GuideFile?.Trim() ?? string.Empty;
            command.SourceCatalogPath = library.SourcePath;
            command.SourceCatalogId = library.SourceCatalogId;
            command.IsExecutionSupported = library.IsExecutionSupported;
            command.ExecutionSupportMessage =
                library.ExecutionSupportMessage;
            command.SourceEntryId = string.IsNullOrWhiteSpace(
                command.SourceEntryId)
                ? command.Id
                : command.SourceEntryId;
            command.IsGuideLoaded = string.IsNullOrWhiteSpace(
                command.GuideFile);
            if (library.SchemaVersion == 2
                && string.IsNullOrWhiteSpace(
                    command.SourceLabelOverride))
            {
                command.SourceLabelOverride = string.IsNullOrWhiteSpace(
                    library.SourceVersion)
                    ? library.DisplayName
                    : $"{library.DisplayName} · {library.SourceVersion}";
            }
            command.BrowsePath = (command.BrowsePath ?? [])
                .Where(segment => segment is not null
                                  && !string.IsNullOrWhiteSpace(segment.Name))
                .Select(segment => new CommandBrowsePathSegment
                {
                    Kind = string.IsNullOrWhiteSpace(segment.Kind)
                        ? "Category"
                        : segment.Kind.Trim(),
                    Name = segment.Name.Trim(),
                    DisplayName = segment.DisplayName?.Trim() ?? string.Empty
                })
                .ToList();
            command.CatalogId = library.Id;
            command.ExecutionTarget = library.ExecutionTarget;
            command.NamespaceName = command.NamespaceName?.Trim() ?? string.Empty;
            command.TypeName = command.TypeName?.Trim() ?? string.Empty;
            command.MemberName = command.MemberName?.Trim() ?? string.Empty;
            command.MemberKind = command.MemberKind?.Trim() ?? string.Empty;
            command.OverloadSignature =
                command.OverloadSignature?.Trim() ?? string.Empty;
            if (command.ExecutionTarget == ExecutionTarget.CSharp)
            {
                NormalizeCSharpCoordinates(command);
            }

            EnsureBrowsePath(library, command);

            command.Template = string.IsNullOrWhiteSpace(command.Template)
                ? command.Name
                : command.Template;
            command.DisplayName = string.IsNullOrWhiteSpace(command.DisplayName)
                ? command.Name
                : command.DisplayName;
            command.Id = string.IsNullOrWhiteSpace(command.Id)
                ? $"{library.Id}:{command.Name}"
                : command.Id;
        }
    }

    private static void EnsureBrowsePath(
        CommandLibrary library,
        CommandDefinition command)
    {
        if (command.BrowsePath.Count > 0)
        {
            return;
        }

        if (command.ExecutionTarget == ExecutionTarget.CSharp
            && !string.IsNullOrWhiteSpace(command.NamespaceName))
        {
            command.BrowsePath.Add(new CommandBrowsePathSegment
            {
                Kind = "Namespace",
                Name = command.NamespaceName
            });
            if (!string.IsNullOrWhiteSpace(command.TypeName))
            {
                command.BrowsePath.Add(new CommandBrowsePathSegment
                {
                    Kind = "Type",
                    Name = command.TypeName
                });
            }

            if (!string.IsNullOrWhiteSpace(command.MemberName))
            {
                command.BrowsePath.Add(new CommandBrowsePathSegment
                {
                    Kind = string.IsNullOrWhiteSpace(command.MemberKind)
                        ? "Member"
                        : command.MemberKind,
                    Name = command.MemberName
                });
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(command.ModuleName))
        {
            command.BrowsePath.Add(new CommandBrowsePathSegment
            {
                Kind = library.Kind == CommandLibraryKind.Core
                    || string.Equals(
                        command.ModuleName,
                        "Language",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        command.ModuleName,
                        "Built-ins",
                        StringComparison.OrdinalIgnoreCase)
                    ? "Category"
                    : "Module",
                Name = command.ModuleName.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(command.ObjectName))
        {
            command.BrowsePath.Add(new CommandBrowsePathSegment
            {
                Kind = "Feature",
                Name = command.ObjectName.Trim()
            });
        }

        command.BrowsePath.Add(new CommandBrowsePathSegment
        {
            Kind = command.SymbolKind == CommandSymbolKind.Other
                ? "Entry"
                : command.SymbolKind.ToString(),
            Name = string.IsNullOrWhiteSpace(command.DisplayName)
                ? command.Name
                : command.DisplayName
        });
    }

    private static void NormalizeCSharpCoordinates(
        CommandDefinition command)
    {
        if (command.ModuleName.StartsWith(
                "C# language",
                StringComparison.OrdinalIgnoreCase))
        {
            command.MemberName = string.IsNullOrWhiteSpace(command.MemberName)
                ? GetCSharpMemberToken(command.Name)
                : command.MemberName;
            command.MemberKind = string.IsNullOrWhiteSpace(command.MemberKind)
                ? "Language pattern"
                : command.MemberKind;
            return;
        }

        var memberSeparator = command.Name.LastIndexOf('.');
        var typeToken = memberSeparator > 0
            ? command.Name[..memberSeparator].Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(command.MemberName))
        {
            command.MemberName = GetCSharpMemberToken(command.Name);
        }

        if (string.IsNullOrWhiteSpace(command.TypeName)
            && !string.IsNullOrWhiteSpace(typeToken))
        {
            command.TypeName = typeToken;
        }

        if (string.IsNullOrWhiteSpace(command.NamespaceName)
            && !string.IsNullOrWhiteSpace(command.ModuleName))
        {
            var moduleSeparator = command.ModuleName.LastIndexOf('.');
            var moduleTail = moduleSeparator >= 0
                ? command.ModuleName[(moduleSeparator + 1)..]
                : command.ModuleName;
            if (!string.IsNullOrWhiteSpace(typeToken)
                && string.Equals(
                    RemoveGenericSuffix(moduleTail),
                    RemoveGenericSuffix(typeToken),
                    StringComparison.OrdinalIgnoreCase))
            {
                command.NamespaceName = moduleSeparator > 0
                    ? command.ModuleName[..moduleSeparator]
                    : string.Empty;
                command.TypeName = moduleTail;
            }
            else
            {
                command.NamespaceName = command.ModuleName;
            }
        }

        if (string.IsNullOrWhiteSpace(command.MemberKind))
        {
            command.MemberKind = command.Template?.Contains(
                "new ",
                StringComparison.Ordinal) == true
                ? "Constructor"
                : "Method";
        }
    }

    private static string GetCSharpMemberToken(string commandName)
    {
        var separator = commandName.LastIndexOf('.');
        var token = separator >= 0
            ? commandName[(separator + 1)..].Trim()
            : commandName.Trim();
        return RemoveGenericSuffix(token);
    }

    private static string RemoveGenericSuffix(string value)
    {
        var separator = value.IndexOf('<');
        return separator > 0 ? value[..separator].Trim() : value.Trim();
    }

    private PowerShellGuideCatalog LoadBundledPowerShellGuideCatalog()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "CommandGuides",
            "powershell.json");
        if (!File.Exists(path))
        {
            return new PowerShellGuideCatalog();
        }

        try
        {
            var catalog = JsonSerializer.Deserialize<PowerShellGuideCatalog>(
                              File.ReadAllText(path),
                              _jsonOptions)
                          ?? new PowerShellGuideCatalog();
            catalog.Actions = new Dictionary<string, string>(
                catalog.Actions,
                StringComparer.OrdinalIgnoreCase);
            catalog.Objects = new Dictionary<string, string>(
                catalog.Objects,
                StringComparer.OrdinalIgnoreCase);
            catalog.Commands =
                new Dictionary<string, PowerShellCommandGuideDefinition>(
                    catalog.Commands,
                    StringComparer.OrdinalIgnoreCase);
            return catalog;
        }
        catch (JsonException)
        {
            return new PowerShellGuideCatalog();
        }
        catch (IOException)
        {
            return new PowerShellGuideCatalog();
        }
    }

    private static void ApplyLibraryFacetDescriptions(CommandLibrary library)
    {
        library.ActionDescriptions = new Dictionary<string, string>(
            library.ActionDescriptions,
            StringComparer.OrdinalIgnoreCase);
        library.ObjectDescriptions = new Dictionary<string, string>(
            library.ObjectDescriptions,
            StringComparer.OrdinalIgnoreCase);
        foreach (var command in library.Commands)
        {
            if (string.IsNullOrWhiteSpace(command.ActionDescription)
                && library.ActionDescriptions.TryGetValue(
                    command.Action,
                    out var actionDescription))
            {
                command.ActionDescription = actionDescription;
            }

            if (string.IsNullOrWhiteSpace(command.ObjectDescription)
                && library.ObjectDescriptions.TryGetValue(
                    command.ObjectName,
                    out var objectDescription))
            {
                command.ObjectDescription = objectDescription;
            }
        }
    }

    private static CommandUsageExample CloneExample(CommandUsageExample example) =>
        new()
        {
            Title = example.Title,
            Code = example.Code,
            Explanation = example.Explanation,
            ImagePath = example.ImagePath,
            ImageAltText = example.ImageAltText
        };

    private static void NormalizeExampleImagePaths(
        CommandLibrary library,
        string libraryDirectory)
    {
        NormalizeExampleImagePaths(
            library.Commands.SelectMany(command => command.Examples),
            libraryDirectory);
    }

    private static void NormalizeExampleImagePaths(
        IEnumerable<CommandUsageExample> examples,
        string libraryDirectory)
    {
        foreach (var example in examples)
        {
            if (string.IsNullOrWhiteSpace(example.ImagePath)
                || Path.IsPathRooted(example.ImagePath))
            {
                continue;
            }

            example.ImagePath = Path.GetFullPath(
                Path.Combine(libraryDirectory, example.ImagePath));
        }
    }

}
