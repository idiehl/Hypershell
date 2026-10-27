using Hypershell.Models;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Archived from the active Hypershell application. This prototype is retained
// for the future standalone Hyperlib command-library application.
namespace Hypershell.Services;

public sealed class PowerShellModuleCatalogService
{
    public const string CatalogPrefix = "powershell-module:";

    private const int CatalogSchemaVersion = 1;
    private const int ParameterCacheSchemaVersion = 1;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ParameterDiscoveryTimeout = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> CatalogFileExtensions =
    [
        ".psd1",
        ".psm1",
        ".dll",
        ".cdxml",
        ".ps1xml"
    ];

    private readonly SettingsService _settingsService;
    private readonly SemaphoreSlim _parameterCacheGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private PowerShellModuleParameterCache? _parameterCache;

    public PowerShellModuleCatalogService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ManagedModuleDirectory => _settingsService.PowerShellModuleDirectory;

    public async Task<IReadOnlyList<CommandLibrary>> LoadLibrariesAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ManagedModuleDirectory);
        var roots = GetModuleRoots();
        var fingerprint = await Task.Run(
            () => ComputeFingerprint(roots),
            cancellationToken);
        var cachedCatalog = TryLoadCatalog();
        PowerShellModuleCatalogDocument catalog;

        if (!forceRefresh
            && cachedCatalog is not null
            && cachedCatalog.SchemaVersion == CatalogSchemaVersion
            && string.Equals(cachedCatalog.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            catalog = cachedCatalog;
        }
        else
        {
            try
            {
                catalog = await DiscoverModulesAsync(roots, cancellationToken);
                catalog.SchemaVersion = CatalogSchemaVersion;
                catalog.Fingerprint = fingerprint;
                SaveCatalog(catalog);
            }
            catch when (cachedCatalog is not null)
            {
                // A stale inventory remains more useful than hiding every
                // module if Windows PowerShell discovery is temporarily blocked.
                catalog = cachedCatalog;
            }
        }

        return BuildLibraries(catalog, roots);
    }

    public async Task<List<CommandParameterDefinition>> LoadParametersAsync(
        CommandDefinition command,
        CancellationToken cancellationToken = default)
    {
        if (!command.IsPowerShellModuleCommand
            || string.IsNullOrWhiteSpace(command.ModulePath)
            || string.IsNullOrWhiteSpace(command.Name))
        {
            return [];
        }

        var cacheKey = BuildParameterCacheKey(command);
        await _parameterCacheGate.WaitAsync(cancellationToken);
        try
        {
            _parameterCache ??= TryLoadParameterCache()
                ?? new PowerShellModuleParameterCache
                {
                    SchemaVersion = ParameterCacheSchemaVersion
                };
            if (_parameterCache.Entries.TryGetValue(cacheKey, out var cachedParameters))
            {
                return CloneParameters(cachedParameters);
            }
        }
        finally
        {
            _parameterCacheGate.Release();
        }

        var discoveredParameters = await DiscoverParametersAsync(command, cancellationToken);

        await _parameterCacheGate.WaitAsync(cancellationToken);
        try
        {
            _parameterCache ??= new PowerShellModuleParameterCache
            {
                SchemaVersion = ParameterCacheSchemaVersion
            };
            _parameterCache.Entries[cacheKey] = CloneParameters(discoveredParameters);
            SaveParameterCache(_parameterCache);
        }
        finally
        {
            _parameterCacheGate.Release();
        }

        return discoveredParameters;
    }

    public static string GetDefaultManagedModuleDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypershell",
            "powershell-modules");

    public static string PrependManagedModulePath(string? currentPath)
    {
        var managedDirectory = GetDefaultManagedModuleDirectory();
        Directory.CreateDirectory(managedDirectory);
        var entries = new[] { managedDirectory }
            .Concat((currentPath ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(Path.PathSeparator, entries);
    }

    private IReadOnlyList<string> GetModuleRoots()
    {
        var programFilesDirectory =
            Environment.GetEnvironmentVariable("ProgramW6432")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesModules = Path.Combine(
            programFilesDirectory,
            "WindowsPowerShell",
            "Modules");
        var windowsModules = Path.Combine(
            GetWindowsDirectory(),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "Modules");

        return new[]
            {
                Path.GetFullPath(ManagedModuleDirectory),
                Path.GetFullPath(programFilesModules),
                Path.GetFullPath(windowsModules)
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<PowerShellModuleCatalogDocument> DiscoverModulesAsync(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>
        {
            // Windows PowerShell 5.1 wraps a top-level JSON array returned by
            // ConvertFrom-Json instead of enumerating it consistently. A
            // PSModulePath-style value is unambiguous on Windows.
            ["HYPERSHELL_MODULE_ROOTS"] = string.Join(Path.PathSeparator, roots)
        };
        var json = await RunWindowsPowerShellAsync(
            ModuleDiscoveryScript,
            environment,
            DiscoveryTimeout,
            cancellationToken);
        var catalog = DeserializeCatalog(json);

        // A newly initialized PowerShell module-analysis cache can briefly
        // return an empty inventory while another engine is starting. Never
        // cache that false result when module files demonstrably exist.
        if (catalog.Modules.Count == 0 && ContainsModuleCandidates(roots))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            json = await RunWindowsPowerShellAsync(
                ModuleDiscoveryScript,
                environment,
                DiscoveryTimeout,
                cancellationToken);
            catalog = DeserializeCatalog(json);
            if (catalog.Modules.Count == 0)
            {
                throw new InvalidOperationException(
                    "Windows PowerShell found module files but returned no module metadata.");
            }
        }

        return catalog;
    }

    private PowerShellModuleCatalogDocument DeserializeCatalog(string json)
    {
        var catalog = JsonSerializer.Deserialize<PowerShellModuleCatalogDocument>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Windows PowerShell returned an empty module catalog.");
        catalog.Modules ??= [];
        foreach (var module in catalog.Modules)
        {
            module.Commands ??= [];
        }

        return catalog;
    }

    private static bool ContainsModuleCandidates(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            try
            {
                if (Directory.EnumerateFiles(root, "*.psd1", SearchOption.AllDirectories).Any()
                    || Directory.EnumerateFiles(root, "*.psm1", SearchOption.AllDirectories).Any())
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private async Task<List<CommandParameterDefinition>> DiscoverParametersAsync(
        CommandDefinition command,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>
        {
            ["HYPERSHELL_MODULE_ROOTS"] = string.Join(Path.PathSeparator, GetModuleRoots()),
            ["HYPERSHELL_MODULE_PATH"] = command.ModulePath,
            ["HYPERSHELL_COMMAND_NAME"] = command.Name
        };
        var json = await RunWindowsPowerShellAsync(
            ParameterDiscoveryScript,
            environment,
            ParameterDiscoveryTimeout,
            cancellationToken);
        var result = JsonSerializer.Deserialize<PowerShellParameterDiscoveryResult>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Windows PowerShell returned no command metadata.");
        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Error)
                    ? $"Unable to inspect {command.Name}."
                    : result.Error);
        }

        return result.Parameters ?? [];
    }

    private IReadOnlyList<CommandLibrary> BuildLibraries(
        PowerShellModuleCatalogDocument catalog,
        IReadOnlyList<string> roots)
    {
        var libraries = new List<CommandLibrary>();
        foreach (var module in catalog.Modules
                     .Where(module => !string.IsNullOrWhiteSpace(module.Name))
                     .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase))
        {
            var libraryId = BuildCatalogId(module.Name);
            var sourceLabel = ResolveSourceLabel(module.Path, roots);
            var description = string.IsNullOrWhiteSpace(module.Description)
                ? $"Windows PowerShell 5.1 module from {sourceLabel}."
                : module.Description.Trim();
            var library = new CommandLibrary
            {
                Id = libraryId,
                DisplayName = module.Name,
                Description = description,
                ExecutionTarget = ExecutionTarget.PowerShell,
                IsPowerShellModule = true,
                Version = module.Version,
                SourcePath = module.Path,
                SourceLabel = sourceLabel
            };

            foreach (var exportedCommand in module.Commands
                         .Where(command => !string.IsNullOrWhiteSpace(command.Name))
                         .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(command => command.CommandType, StringComparer.OrdinalIgnoreCase))
            {
                var commandType = string.IsNullOrWhiteSpace(exportedCommand.CommandType)
                    ? "Cmdlet"
                    : exportedCommand.CommandType;
                library.Commands.Add(new CommandDefinition
                {
                    Id = $"{libraryId}:{commandType}:{exportedCommand.Name}",
                    CatalogId = libraryId,
                    Name = exportedCommand.Name,
                    DisplayName = exportedCommand.Name,
                    Description = $"{commandType} · {module.Name} v{module.Version}",
                    ModuleName = module.Name,
                    ModuleVersion = module.Version,
                    ModulePath = module.Path,
                    CommandType = commandType,
                    Template = exportedCommand.Name,
                    ExecutionTarget = ExecutionTarget.PowerShell,
                    IsPowerShellModuleCommand = true,
                    PreferredShellKind = TerminalShellKind.WindowsPowerShell,
                    Parameters = []
                });
            }

            libraries.Add(library);
        }

        return libraries;
    }

    private string ComputeFingerprint(IReadOnlyList<string> roots)
    {
        var builder = new StringBuilder();
        builder.Append(CatalogSchemaVersion).Append('|');
        foreach (var root in roots)
        {
            builder.Append(root.ToUpperInvariant()).Append('|');
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                             .Where(path => CatalogFileExtensions.Contains(Path.GetExtension(path)))
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var file = new FileInfo(path);
                    builder
                        .Append(path.ToUpperInvariant())
                        .Append(':')
                        .Append(file.Length)
                        .Append(':')
                        .Append(file.LastWriteTimeUtc.Ticks)
                        .Append('|');
                }
            }
            catch (IOException)
            {
                builder.Append("IO-ERROR|");
            }
            catch (UnauthorizedAccessException)
            {
                builder.Append("ACCESS-ERROR|");
            }
        }

        var powerShellPath = GetWindowsPowerShellPath();
        if (File.Exists(powerShellPath))
        {
            var executable = new FileInfo(powerShellPath);
            builder
                .Append(executable.Length)
                .Append(':')
                .Append(executable.LastWriteTimeUtc.Ticks);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private PowerShellModuleCatalogDocument? TryLoadCatalog()
    {
        try
        {
            if (!File.Exists(_settingsService.PowerShellModuleCatalogPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PowerShellModuleCatalogDocument>(
                File.ReadAllText(_settingsService.PowerShellModuleCatalogPath),
                _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveCatalog(PowerShellModuleCatalogDocument catalog)
    {
        SaveJsonAtomically(_settingsService.PowerShellModuleCatalogPath, catalog);
    }

    private PowerShellModuleParameterCache? TryLoadParameterCache()
    {
        try
        {
            if (!File.Exists(_settingsService.PowerShellModuleParameterCachePath))
            {
                return null;
            }

            var cache = JsonSerializer.Deserialize<PowerShellModuleParameterCache>(
                File.ReadAllText(_settingsService.PowerShellModuleParameterCachePath),
                _jsonOptions);
            return cache?.SchemaVersion == ParameterCacheSchemaVersion ? cache : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveParameterCache(PowerShellModuleParameterCache cache)
    {
        SaveJsonAtomically(_settingsService.PowerShellModuleParameterCachePath, cache);
    }

    private void SaveJsonAtomically<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, _jsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private static async Task<string> RunWindowsPowerShellAsync(
        string script,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var executablePath = GetWindowsPowerShellPath();
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Windows PowerShell 5.1 is required to inventory native Windows modules.",
                executablePath);
        }

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedCommand);
        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Windows PowerShell discovery could not start.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException(
                $"Windows PowerShell module discovery exceeded {timeout.TotalSeconds:F0} seconds.");
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError)
                    ? $"Windows PowerShell discovery exited with code {process.ExitCode}."
                    : standardError.Trim());
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError)
                    ? "Windows PowerShell returned no module metadata."
                    : standardError.Trim());
        }

        return standardOutput.Trim().TrimStart('\uFEFF');
    }

    private static string GetWindowsPowerShellPath() =>
        Path.Combine(
            GetWindowsDirectory(),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

    private static string GetWindowsDirectory()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return windowsDirectory;
        }

        return Path.GetDirectoryName(Environment.SystemDirectory)
            ?? throw new DirectoryNotFoundException("The Windows directory could not be resolved.");
    }

    private static string BuildCatalogId(string moduleName) =>
        $"{CatalogPrefix}{moduleName.Trim().ToLowerInvariant()}";

    private static string ResolveSourceLabel(string path, IReadOnlyList<string> roots)
    {
        if (roots.Count > 0 && IsUnderRoot(path, roots[0]))
        {
            return "Hypershell managed";
        }

        if (path.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                StringComparison.OrdinalIgnoreCase))
        {
            return "Windows shared";
        }

        return "Windows system";
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildParameterCacheKey(CommandDefinition command) =>
        $"{command.ModulePath}|{command.ModuleVersion}|{command.Name}".ToUpperInvariant();

    private static List<CommandParameterDefinition> CloneParameters(
        IEnumerable<CommandParameterDefinition> parameters) =>
        parameters.Select(parameter => new CommandParameterDefinition
        {
            Name = parameter.Name,
            DisplayName = parameter.DisplayName,
            Placeholder = parameter.Placeholder,
            Description = parameter.Description,
            TypeName = parameter.TypeName,
            IsRequired = parameter.IsRequired,
            IsSwitch = parameter.IsSwitch
        }).ToList();

    private const string ModuleDiscoveryScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        $WarningPreference = 'SilentlyContinue'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $roots = @($env:HYPERSHELL_MODULE_ROOTS -split ';' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $existingPaths = @($env:PSModulePath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $env:PSModulePath = (@($roots) + $existingPaths | Select-Object -Unique) -join ';'

        function Get-RootIndex([string]$path) {
            for ($index = 0; $index -lt $roots.Count; $index++) {
                $root = $roots[$index].TrimEnd('\') + '\'
                if ($path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $index
                }
            }
            return -1
        }

        $candidates = @()
        foreach ($module in @(Get-Module -ListAvailable)) {
            if ([string]::IsNullOrWhiteSpace($module.Path)) {
                continue
            }
            $rootIndex = Get-RootIndex $module.Path
            if ($rootIndex -lt 0) {
                continue
            }
            $candidates += [pscustomobject]@{
                RootIndex = $rootIndex
                Module = $module
            }
        }

        $modules = @()
        foreach ($group in @($candidates | Group-Object { $_.Module.Name })) {
            $selected = $group.Group |
                Sort-Object RootIndex, @{ Expression = { $_.Module.Version }; Descending = $true } |
                Select-Object -First 1
            $module = $selected.Module
            $commands = @()
            foreach ($entry in @($module.ExportedCommands.GetEnumerator() | Sort-Object Key)) {
                $commandType = 'Cmdlet'
                if ($null -ne $entry.Value -and $null -ne $entry.Value.CommandType) {
                    $commandType = $entry.Value.CommandType.ToString()
                }
                $commands += [pscustomobject]@{
                    Name = $entry.Key
                    CommandType = $commandType
                }
            }
            $modules += [pscustomobject]@{
                Name = $module.Name
                Version = $module.Version.ToString()
                ModuleType = $module.ModuleType.ToString()
                Path = $module.Path
                Description = $module.Description
                Author = $module.Author
                CompanyName = $module.CompanyName
                Commands = @($commands)
            }
        }

        [pscustomobject]@{
            SchemaVersion = 1
            Fingerprint = ''
            GeneratedAt = [DateTimeOffset]::UtcNow.ToString('O')
            PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            Modules = @($modules | Sort-Object Name)
        } | ConvertTo-Json -Depth 7 -Compress
        """;

    private const string ParameterDiscoveryScript = """
        $ErrorActionPreference = 'Stop'
        $WarningPreference = 'SilentlyContinue'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $roots = @($env:HYPERSHELL_MODULE_ROOTS -split ';' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $existingPaths = @($env:PSModulePath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $env:PSModulePath = (@($roots) + $existingPaths | Select-Object -Unique) -join ';'

        try {
            $loadedModule = Import-Module -Name $env:HYPERSHELL_MODULE_PATH -Force -DisableNameChecking -PassThru -ErrorAction Stop |
                Select-Object -First 1
            $command = Get-Command -Name $env:HYPERSHELL_COMMAND_NAME -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.ModuleName -eq $loadedModule.Name -or
                    $_.Source -eq $loadedModule.Name
                } |
                Select-Object -First 1
            if ($null -eq $command -and $loadedModule.ExportedCommands.ContainsKey($env:HYPERSHELL_COMMAND_NAME)) {
                $command = $loadedModule.ExportedCommands[$env:HYPERSHELL_COMMAND_NAME]
            }
            if ($null -eq $command) {
                throw "The command '$($env:HYPERSHELL_COMMAND_NAME)' was not exported after importing '$($loadedModule.Name)'."
            }

            $parameters = @()
            foreach ($parameter in @($command.Parameters.Values | Sort-Object Name -Unique)) {
                $isRequired = $false
                foreach ($parameterSet in @($parameter.ParameterSets.Values)) {
                    if ($parameterSet.IsMandatory) {
                        $isRequired = $true
                        break
                    }
                }
                $typeName = 'Object'
                $description = 'PowerShell parameter'
                if ($null -ne $parameter.ParameterType) {
                    $typeName = $parameter.ParameterType.Name
                    $description = $parameter.ParameterType.FullName
                }
                $parameters += [pscustomobject]@{
                    Name = $parameter.Name
                    DisplayName = "-$($parameter.Name)"
                    Placeholder = ''
                    Description = $description
                    TypeName = $typeName
                    IsRequired = $isRequired
                    IsSwitch = ($parameter.ParameterType -eq [System.Management.Automation.SwitchParameter])
                }
            }

            [pscustomobject]@{
                Success = $true
                Error = ''
                Parameters = @($parameters)
            } | ConvertTo-Json -Depth 6 -Compress
        }
        catch {
            [pscustomobject]@{
                Success = $false
                Error = $_.Exception.Message
                Parameters = @()
            } | ConvertTo-Json -Depth 6 -Compress
        }
        """;

    private sealed class PowerShellModuleCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAt { get; set; }
        public string PowerShellVersion { get; set; } = string.Empty;
        public List<PowerShellModuleRecord> Modules { get; set; } = [];
    }

    private sealed class PowerShellModuleRecord
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string ModuleType { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public List<PowerShellModuleCommandRecord> Commands { get; set; } = [];
    }

    private sealed class PowerShellModuleCommandRecord
    {
        public string Name { get; set; } = string.Empty;
        public string CommandType { get; set; } = string.Empty;
    }

    private sealed class PowerShellModuleParameterCache
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, List<CommandParameterDefinition>> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PowerShellParameterDiscoveryResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public List<CommandParameterDefinition> Parameters { get; set; } = [];
    }
}
