using Hypershell.Models;
using Hypershell.Services;
using System.Text;

namespace Hyperlib.Module;

/// <summary>
/// Archived session-loading prototype for the future Hyperlib application.
/// This file is outside the Hypershell project and is not compiled there.
/// </summary>
public sealed class PowerShellModuleSessionLoader
{
    private readonly HashSet<string> _confirmedLoadedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfirmedLoaded(string modulePath) =>
        !string.IsNullOrWhiteSpace(modulePath)
        && _confirmedLoadedPaths.Contains(modulePath);

    public async Task<IReadOnlySet<string>?> ProbeAsync(
        ITerminalSession session,
        string workingDirectory,
        IEnumerable<PowerShellModuleReference> modules,
        CancellationToken cancellationToken = default)
    {
        var references = Normalize(modules);
        if (references.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var marker = $"__HYPERLIB_MODULE_LOADED_{Guid.NewGuid():N}_";
        var script = new StringBuilder();
        for (var index = 0; index < references.Count; index++)
        {
            var module = references[index];
            var name = EscapeLiteral(module.Name);
            var path = EscapeLiteral(module.Path);
            script.AppendLine(
                $"if (@(Get-Module -Name '{name}' | Where-Object {{ $_.Path -ieq '{path}' }}).Count -gt 0) " +
                $"{{ Write-Output '{marker}{index}' }}");
        }

        try
        {
            var result = await session.ExecuteAsync(
                script.ToString(),
                workingDirectory,
                cancellationToken);
            if (result.Errors.Count > 0)
            {
                return null;
            }

            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < references.Count; index++)
            {
                var path = references[index].Path;
                if (result.Output.Any(line =>
                        string.Equals(line.Trim(), $"{marker}{index}", StringComparison.Ordinal)))
                {
                    loadedPaths.Add(path);
                    _confirmedLoadedPaths.Add(path);
                }
                else
                {
                    _confirmedLoadedPaths.Remove(path);
                }
            }

            return loadedPaths;
        }
        catch
        {
            return null;
        }
    }

    public async Task<PowerShellModuleLoadResult> EnsureLoadedAsync(
        ITerminalSession session,
        string workingDirectory,
        IEnumerable<PowerShellModuleReference> modules,
        CancellationToken cancellationToken = default)
    {
        var references = Normalize(modules);
        if (references.Count == 0)
        {
            return new PowerShellModuleLoadResult(true, [], []);
        }

        var importMarker = $"__HYPERLIB_MODULE_IMPORTED_{Guid.NewGuid():N}_";
        var script = new StringBuilder();
        for (var index = 0; index < references.Count; index++)
        {
            var module = references[index];
            var name = EscapeLiteral(module.Name);
            var path = EscapeLiteral(module.Path);
            script.AppendLine($"$__hyperlibModulePath_{index} = '{path}'");
            script.AppendLine(
                $"if (@(Get-Module -Name '{name}' | Where-Object {{ $_.Path -ieq $__hyperlibModulePath_{index} }}).Count -eq 0) " +
                $"{{ Import-Module -Name $__hyperlibModulePath_{index} -Global -DisableNameChecking -ErrorAction Stop | Out-Null; " +
                $"Write-Output '{importMarker}{index}' }}");
            script.AppendLine(
                $"if (@(Get-Module -Name '{name}' | Where-Object {{ $_.Path -ieq $__hyperlibModulePath_{index} }}).Count -eq 0) " +
                $"{{ throw 'PowerShell did not load {EscapeLiteral(module.Name)} from the catalogued path.' }}");
        }

        try
        {
            var result = await session.ExecuteAsync(
                script.ToString(),
                workingDirectory,
                cancellationToken);
            if (result.Errors.Count > 0)
            {
                return new PowerShellModuleLoadResult(
                    false,
                    [],
                    result.Errors);
            }

            foreach (var module in references)
            {
                _confirmedLoadedPaths.Add(module.Path);
            }

            var imported = references
                .Where((_, index) => result.Output.Any(line =>
                    string.Equals(line.Trim(), $"{importMarker}{index}", StringComparison.Ordinal)))
                .ToList();
            return new PowerShellModuleLoadResult(
                true,
                imported,
                []);
        }
        catch (Exception exception)
        {
            return new PowerShellModuleLoadResult(
                false,
                [],
                [exception.Message]);
        }
    }

    private static List<PowerShellModuleReference> Normalize(
        IEnumerable<PowerShellModuleReference> modules) =>
        modules
            .Where(module =>
                !string.IsNullOrWhiteSpace(module.Name)
                && !string.IsNullOrWhiteSpace(module.Path))
            .DistinctBy(module => module.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string EscapeLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed record PowerShellModuleLoadResult(
    bool Succeeded,
    IReadOnlyList<PowerShellModuleReference> ImportedModules,
    IReadOnlyList<string> Errors);

public enum PowerShellModuleRuntimeState
{
    Installed,
    Checking,
    ReadyForAutoImport,
    Loaded,
    RequiresCompatibleTerminal,
    ImportFailed
}

public sealed record PowerShellModuleCommandMetadata(
    string ModuleName,
    string ModuleVersion,
    string ModulePath,
    bool IsPowerShellModuleCommand);

public sealed record PowerShellModuleLibraryMetadata(
    bool IsPowerShellModule,
    string Version,
    string SourcePath,
    string SourceLabel);
