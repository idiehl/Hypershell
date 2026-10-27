using Hypershell.Extensibility;
using Hypershell.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed class ExtensionLibraryCandidate
{
    private readonly string _contentJson;

    internal ExtensionLibraryCandidate(
        DiscoveredExtensionPackage package,
        LibraryContribution contribution,
        string contentFilePath,
        string qualifiedLibraryId,
        CommandLibrary library,
        string contentJson)
    {
        Package = package;
        Contribution = contribution;
        ContentFilePath = contentFilePath;
        QualifiedLibraryId = qualifiedLibraryId;
        EntryCount = library.EffectiveEntryCount;
        _contentJson = contentJson;
    }

    public DiscoveredExtensionPackage Package { get; }
    public LibraryContribution Contribution { get; }
    public string ContentFilePath { get; }
    public string QualifiedLibraryId { get; }
    public int EntryCount { get; }

    public string ReviewText => Contribution.ReviewState switch
    {
        HyperlibReviewState.Reviewed =>
            $"Publisher-declared {Contribution.ContentOrigin} content · reviewed by {Contribution.ReviewedBy}",
        _ =>
            $"Publisher-declared {Contribution.ContentOrigin} content · draft review state"
    };

    internal CommandLibrary CreateLibrarySnapshot() =>
        JsonSerializer.Deserialize<CommandLibrary>(
            _contentJson,
            HyperlibContentPackageService.JsonOptions)
        ?? throw new InvalidDataException(
            $"Validated Hyperlib content '{ContentFilePath}' could not be reconstructed.");
}

internal static class HyperlibContentPackageService
{
    public const string DirectoryIndexFileName = "hyperlib.json";
    public const long MaximumContentFileBytes = 16L * 1024 * 1024;
    public const int MaximumCommandsPerLibrary = 10_000;
    public const int MaximumEntriesPerShard = 10_000;
    public const int MaximumParametersPerCommand = 64;
    public const int MaximumExamplesPerCommand = 10;

    private static readonly Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    public static IReadOnlyList<ExtensionLibraryCandidate> BuildCandidates(
        IEnumerable<DiscoveredExtensionPackage> packages)
    {
        var candidates = new List<ExtensionLibraryCandidate>();
        foreach (var package in packages.Where(package =>
                     package.Status == ExtensionPackageStatus.Validated
                     && package.Manifest?.Runtime == ExtensionRuntimeKind.ContentOnly
                     && package.Manifest.Contributions.Libraries.Count > 0))
        {
            var diagnostics = package.Diagnostics.ToList();
            var packageCandidates = new List<ExtensionLibraryCandidate>();
            foreach (var contribution in package.Manifest!.Contributions.Libraries)
            {
                if (TryBuildCandidate(
                        package,
                        contribution,
                        diagnostics,
                        out var candidate)
                    && candidate is not null)
                {
                    packageCandidates.Add(candidate);
                }
            }

            package.Diagnostics = diagnostics;
            if (diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ExtensionDiagnosticSeverity.Error))
            {
                package.Status = ExtensionPackageStatus.Invalid;
                continue;
            }

            candidates.AddRange(packageCandidates);
        }

        return candidates;
    }

    private static bool TryBuildCandidate(
        DiscoveredExtensionPackage package,
        LibraryContribution contribution,
        ICollection<ExtensionDiagnostic> diagnostics,
        out ExtensionLibraryCandidate? candidate)
    {
        candidate = null;
        var manifest = package.Manifest!;
        var qualifiedLibraryId = $"{manifest.Id}.{contribution.Id}";
        string contentFilePath;
        try
        {
            var declaredPath = Path.GetFullPath(Path.Combine(
                package.PackageDirectory,
                contribution.ContentPath));
            contentFilePath = Directory.Exists(declaredPath)
                ? Path.Combine(declaredPath, DirectoryIndexFileName)
                : declaredPath;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            diagnostics.Add(Error(
                "HYPERLIB_PATH_INVALID",
                $"Library '{contribution.Id}' content path is invalid: {exception.Message}"));
            return false;
        }

        if (!File.Exists(contentFilePath))
        {
            diagnostics.Add(Error(
                "HYPERLIB_INDEX_MISSING",
                $"Library '{contribution.Id}' must point to a JSON file or a directory containing {DirectoryIndexFileName}."));
            return false;
        }

        if (!string.Equals(
                Path.GetExtension(contentFilePath),
                ".json",
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error(
                "HYPERLIB_FORMAT_UNSUPPORTED",
                $"Library '{contribution.Id}' content must use the JSON Hyperlib schema."));
            return false;
        }

        try
        {
            var contentInfo = new FileInfo(contentFilePath);
            if (contentInfo.Length > MaximumContentFileBytes)
            {
                diagnostics.Add(Error(
                    "HYPERLIB_FILE_TOO_LARGE",
                    $"Library '{contribution.Id}' exceeds the {MaximumContentFileBytes / 1024 / 1024:N0} MiB content-file limit."));
                return false;
            }

            var contentJson = File.ReadAllText(contentFilePath);
            var library = JsonSerializer.Deserialize<CommandLibrary>(
                contentJson,
                JsonOptions);
            if (library is null)
            {
                diagnostics.Add(Error(
                    "HYPERLIB_CONTENT_EMPTY",
                    $"Library '{contribution.Id}' did not contain a Hyperlib object."));
                return false;
            }

            ValidateLibrary(
                package,
                contribution,
                qualifiedLibraryId,
                contentFilePath,
                library,
                diagnostics);
            if (diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ExtensionDiagnosticSeverity.Error
                    && diagnostic.Code.StartsWith(
                        $"HYPERLIB[{contribution.Id}]",
                        StringComparison.Ordinal)))
            {
                return false;
            }

            diagnostics.Add(Information(
                $"HYPERLIB[{contribution.Id}].VALIDATED",
                $"Validated {library.EffectiveEntryCount:N0} guide and command entr{(library.EffectiveEntryCount == 1 ? "y" : "ies")} from '{Path.GetRelativePath(package.PackageDirectory, contentFilePath)}'."));
            candidate = new ExtensionLibraryCandidate(
                package,
                contribution,
                contentFilePath,
                qualifiedLibraryId,
                library,
                library.SchemaVersion == 2
                    ? JsonSerializer.Serialize(library, JsonOptions)
                    : contentJson);
            return true;
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error(
                $"HYPERLIB[{contribution.Id}].JSON_INVALID",
                exception.Message));
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                $"HYPERLIB[{contribution.Id}].UNREADABLE",
                $"The content file could not be read: {exception.Message}"));
            return false;
        }
    }

    private static void ValidateLibrary(
        DiscoveredExtensionPackage package,
        LibraryContribution contribution,
        string qualifiedLibraryId,
        string contentFilePath,
        CommandLibrary library,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        var prefix = $"HYPERLIB[{contribution.Id}]";
        if (library.SchemaVersion is not (1 or 2))
        {
            diagnostics.Add(Error(
                $"{prefix}.SCHEMA_UNSUPPORTED",
                $"Content schema {library.SchemaVersion} is unsupported; this build accepts schemas 1 and 2."));
        }

        if (!VersionPattern.IsMatch(library.ContentVersion ?? string.Empty))
        {
            diagnostics.Add(Error(
                $"{prefix}.VERSION_INVALID",
                "contentVersion must use semantic version syntax such as 1.2.0."));
        }

        var declaredLibraryId = library.Id?.Trim() ?? string.Empty;
        if (!string.Equals(
                declaredLibraryId,
                contribution.Id,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                declaredLibraryId,
                qualifiedLibraryId,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error(
                $"{prefix}.ID_MISMATCH",
                $"The content ID must be '{contribution.Id}' or its host-qualified ID '{qualifiedLibraryId}'."));
        }

        ValidateRequiredText(
            library.DisplayName,
            80,
            $"{prefix}.DISPLAY_NAME_INVALID",
            "displayName",
            diagnostics);
        ValidateRequiredText(
            library.Description,
            1000,
            $"{prefix}.DESCRIPTION_INVALID",
            "description",
            diagnostics);
        if (library.SchemaVersion == 2)
        {
            ValidateRequiredText(
                library.CanonicalName,
                160,
                $"{prefix}.CANONICAL_NAME_INVALID",
                "canonicalName",
                diagnostics);
            ValidateRequiredText(
                library.Ecosystem,
                120,
                $"{prefix}.ECOSYSTEM_INVALID",
                "ecosystem",
                diagnostics);
            if (!string.IsNullOrWhiteSpace(library.SourceUrl)
                && !IsHttpUri(library.SourceUrl))
            {
                diagnostics.Add(Error(
                    $"{prefix}.SOURCE_URL_INVALID",
                    "sourceUrl must be an absolute HTTP or HTTPS URL when supplied."));
            }
        }

        var languageId = NormalizeLanguageId(library.Language);
        var declaredLanguageIds = contribution.LanguageIds
            .Select(NormalizeLanguageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(languageId)
            || !declaredLanguageIds.Contains(languageId))
        {
            diagnostics.Add(Error(
                $"{prefix}.LANGUAGE_MISMATCH",
                $"The content language '{library.Language}' is not declared by the manifest contribution."));
        }

        if (library.ExecutionTarget == ExecutionTarget.PowerShell
            && !string.Equals(languageId, "powershell", StringComparison.OrdinalIgnoreCase)
            || library.ExecutionTarget == ExecutionTarget.CSharp
            && !string.Equals(languageId, "csharp", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error(
                $"{prefix}.EXECUTION_TARGET_MISMATCH",
                $"Execution target {library.ExecutionTarget} does not match language '{library.Language}'."));
        }

        if (library.ExecutionTarget == ExecutionTarget.External
            && library.IsExecutionSupported
            && (!CommandLanguageRegistry.TryGet(
                    library.Language,
                    out var externalLanguage)
                || !externalLanguage.SupportsTerminalExecution
                || externalLanguage.ExecutionTarget != ExecutionTarget.External))
        {
            diagnostics.Add(Error(
                $"{prefix}.EXTERNAL_EXECUTION_UNAVAILABLE",
                $"External-language content for '{library.Language}' must set " +
                "isExecutionSupported to false until a compatible host runner is registered."));
        }

        if (library.Kind is CommandLibraryKind.Framework
            or CommandLibraryKind.Runtime
            or CommandLibraryKind.Sdk
            or CommandLibraryKind.CommandLineTool)
        {
            diagnostics.Add(Error(
                $"{prefix}.KIND_RESERVED",
                $"A static content package cannot claim the installed/runtime kind {library.Kind}."));
        }

        library.Commands ??= [];
        library.EntryFiles ??= [];
        library.GuideFiles ??= [];
        IReadOnlyList<CommandDefinition> entries = library.Commands;
        if (library.SchemaVersion == 1)
        {
            if (library.Commands.Count is < 1 or > MaximumCommandsPerLibrary)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_COUNT_INVALID",
                    $"A schema-v1 Hyperlib contribution requires 1–{MaximumCommandsPerLibrary:N0} entries."));
                return;
            }
        }
        else
        {
            if (library.Commands.Count > 0 && library.EntryFiles.Count > 0)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_SOURCE_AMBIGUOUS",
                    "A schema-v2 library must use either inline commands or entryFiles, not both."));
                return;
            }

            if (library.EntryFiles.Count > 0)
            {
                entries = LoadEntryShardsForValidation(
                    package,
                    contentFilePath,
                    library,
                    prefix,
                    diagnostics);
                if (diagnostics.Any(diagnostic =>
                        diagnostic.Severity == ExtensionDiagnosticSeverity.Error
                        && diagnostic.Code.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    return;
                }
            }

            if (entries.Count == 0)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_COUNT_INVALID",
                    "A schema-v2 Hyperlib contribution requires inline entries or at least one entry shard."));
                return;
            }

            if (library.DeclaredEntryCount > 0
                && entries.Count != library.DeclaredEntryCount)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_COUNT_MISMATCH",
                    $"declaredEntryCount is {library.DeclaredEntryCount:N0}, but the contribution contains {entries.Count:N0} entries."));
                return;
            }

            library.DeclaredEntryCount = entries.Count;
            var indexedCount = entries.Count(entry =>
                entry.CoverageTier == CommandCoverageTier.Indexed);
            var guidedCount = entries.Count(entry =>
                entry.CoverageTier == CommandCoverageTier.Guided);
            var verifiedCount = entries.Count(entry =>
                entry.CoverageTier == CommandCoverageTier.Verified);
            var declaresCoverage = library.DeclaredIndexedCount > 0
                                   || library.DeclaredGuidedCount > 0
                                   || library.DeclaredVerifiedCount > 0;
            if (declaresCoverage
                && (library.DeclaredIndexedCount != indexedCount
                    || library.DeclaredGuidedCount != guidedCount
                    || library.DeclaredVerifiedCount != verifiedCount))
            {
                diagnostics.Add(Error(
                    $"{prefix}.COVERAGE_COUNT_MISMATCH",
                    "The declared Indexed, Guided, and Verified counts do not match the entry shards."));
                return;
            }

            library.DeclaredIndexedCount = indexedCount;
            library.DeclaredGuidedCount = guidedCount;
            library.DeclaredVerifiedCount = verifiedCount;

            var guides = library.GuideFiles.Count == 0
                ? new Dictionary<string, ValidatedGuide>(
                    StringComparer.OrdinalIgnoreCase)
                : LoadGuideShardsForValidation(
                    package,
                    contentFilePath,
                    library,
                    prefix,
                    diagnostics);
            if (diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ExtensionDiagnosticSeverity.Error
                    && diagnostic.Code.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return;
            }

            var referencedGuideIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                entry.GuideFile = entry.GuideFile?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(entry.GuideFile))
                {
                    continue;
                }

                if (entry.CoverageTier == CommandCoverageTier.Indexed)
                {
                    diagnostics.Add(Error(
                        $"{prefix}.GUIDE_TIER_INVALID",
                        $"Indexed entry '{entry.Id}' cannot declare a deferred guide."));
                    continue;
                }

                if (!library.GuideFiles.Contains(
                        entry.GuideFile,
                        StringComparer.OrdinalIgnoreCase)
                    || !guides.TryGetValue(entry.Id, out var guide)
                    || !string.Equals(
                        guide.RelativePath,
                        entry.GuideFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Error(
                        $"{prefix}.GUIDE_REFERENCE_INVALID",
                        $"Entry '{entry.Id}' does not resolve to its declared guide shard '{entry.GuideFile}'."));
                    continue;
                }

                referencedGuideIds.Add(entry.Id);
                entry.GuideDefinition = guide.Entry.Definition;
                entry.Parameters = guide.Entry.Parameters;
                entry.Examples = guide.Entry.Examples;
            }

            if (guides.Keys.Any(id => !referencedGuideIds.Contains(id)))
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_ORPHANED",
                    "Every deferred guide must be referenced by exactly one catalog entry."));
            }

            if (library.DeclaredGuideCount > 0
                && library.DeclaredGuideCount != guides.Count)
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_COUNT_MISMATCH",
                    $"declaredGuideCount is {library.DeclaredGuideCount:N0}, but the contribution contains {guides.Count:N0} guides."));
            }

            library.DeclaredGuideCount = guides.Count;
        }

        var stableEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            ValidateCommand(
                package,
                contribution,
                contentFilePath,
                entries[index],
                library.Language,
                library.ExecutionTarget,
                library.IsExecutionSupported,
                library.SchemaVersion,
                index,
                stableEntryIds,
                diagnostics);
        }
    }

    private static IReadOnlyList<CommandDefinition> LoadEntryShardsForValidation(
        DiscoveredExtensionPackage package,
        string descriptorPath,
        CommandLibrary library,
        string prefix,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        var entries = new List<CommandDefinition>();
        var descriptorDirectory = Path.GetDirectoryName(descriptorPath)
                                  ?? package.PackageDirectory;
        var allowedRoot = Path.GetFullPath(package.PackageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in library.EntryFiles)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath))
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_SHARD_PATH_INVALID",
                    "entryFiles must contain non-empty package-relative JSON paths."));
                continue;
            }

            string shardPath;
            try
            {
                shardPath = Path.GetFullPath(Path.Combine(
                    descriptorDirectory,
                    relativePath));
            }
            catch (Exception exception) when (exception is
                ArgumentException or NotSupportedException)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_SHARD_PATH_INVALID",
                    $"Entry shard '{relativePath}' is invalid: {exception.Message}"));
                continue;
            }

            if (!shardPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
                || !seenPaths.Add(shardPath)
                || !string.Equals(
                    Path.GetExtension(shardPath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(shardPath))
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_SHARD_UNAVAILABLE",
                    $"Entry shard '{relativePath}' is missing, duplicated, outside the package, or not JSON."));
                continue;
            }

            try
            {
                var info = new FileInfo(shardPath);
                if (info.Length > MaximumContentFileBytes)
                {
                    diagnostics.Add(Error(
                        $"{prefix}.ENTRY_SHARD_TOO_LARGE",
                        $"Entry shard '{relativePath}' exceeds {MaximumContentFileBytes / 1024 / 1024:N0} MiB."));
                    continue;
                }

                var shard = JsonSerializer.Deserialize<CommandLibraryEntryShard>(
                    File.ReadAllText(shardPath),
                    JsonOptions);
                if (shard is null
                    || shard.SchemaVersion != 2
                    || !string.Equals(
                        shard.LibraryId,
                        library.Id,
                        StringComparison.OrdinalIgnoreCase)
                    || shard.Entries.Count is < 1 or > MaximumEntriesPerShard)
                {
                    diagnostics.Add(Error(
                        $"{prefix}.ENTRY_SHARD_INVALID",
                        $"Entry shard '{relativePath}' must use schema 2, name library '{library.Id}', and contain 1–{MaximumEntriesPerShard:N0} entries."));
                    continue;
                }

                entries.AddRange(shard.Entries);
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_SHARD_JSON_INVALID",
                    $"Entry shard '{relativePath}' is invalid JSON: {exception.Message}"));
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    $"{prefix}.ENTRY_SHARD_UNREADABLE",
                    $"Entry shard '{relativePath}' could not be read: {exception.Message}"));
            }
        }

        return entries;
    }

    private sealed record ValidatedGuide(
        string RelativePath,
        CommandGuideShardEntry Entry);

    private static Dictionary<string, ValidatedGuide>
        LoadGuideShardsForValidation(
            DiscoveredExtensionPackage package,
            string descriptorPath,
            CommandLibrary library,
            string prefix,
            ICollection<ExtensionDiagnostic> diagnostics)
    {
        var guides = new Dictionary<string, ValidatedGuide>(
            StringComparer.OrdinalIgnoreCase);
        var descriptorDirectory = Path.GetDirectoryName(descriptorPath)
                                  ?? package.PackageDirectory;
        var allowedRoot = Path.GetFullPath(package.PackageDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in library.GuideFiles)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath))
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_SHARD_PATH_INVALID",
                    "guideFiles must contain non-empty package-relative JSON paths."));
                continue;
            }

            string shardPath;
            try
            {
                shardPath = Path.GetFullPath(Path.Combine(
                    descriptorDirectory,
                    relativePath));
            }
            catch (Exception exception) when (exception is
                ArgumentException or NotSupportedException)
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_SHARD_PATH_INVALID",
                    $"Guide shard '{relativePath}' is invalid: {exception.Message}"));
                continue;
            }

            if (!shardPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
                || !seenPaths.Add(shardPath)
                || !string.Equals(
                    Path.GetExtension(shardPath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(shardPath))
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_SHARD_UNAVAILABLE",
                    $"Guide shard '{relativePath}' is missing, duplicated, outside the package, or not JSON."));
                continue;
            }

            try
            {
                var info = new FileInfo(shardPath);
                if (info.Length > MaximumContentFileBytes)
                {
                    diagnostics.Add(Error(
                        $"{prefix}.GUIDE_SHARD_TOO_LARGE",
                        $"Guide shard '{relativePath}' exceeds {MaximumContentFileBytes / 1024 / 1024:N0} MiB."));
                    continue;
                }

                var shard = JsonSerializer.Deserialize<CommandGuideShard>(
                    File.ReadAllText(shardPath),
                    JsonOptions);
                if (shard is null
                    || shard.SchemaVersion != 2
                    || !string.Equals(
                        shard.LibraryId,
                        library.Id,
                        StringComparison.OrdinalIgnoreCase)
                    || shard.Guides.Count is < 1 or > MaximumEntriesPerShard)
                {
                    diagnostics.Add(Error(
                        $"{prefix}.GUIDE_SHARD_INVALID",
                        $"Guide shard '{relativePath}' must use schema 2, name library '{library.Id}', and contain 1–{MaximumEntriesPerShard:N0} guides."));
                    continue;
                }

                foreach (var guide in shard.Guides)
                {
                    if (guide is null
                        || string.IsNullOrWhiteSpace(guide.EntryId)
                        || !guides.TryAdd(
                            guide.EntryId,
                            new ValidatedGuide(relativePath, guide)))
                    {
                        diagnostics.Add(Error(
                            $"{prefix}.GUIDE_ID_INVALID",
                            $"Guide shard '{relativePath}' contains an empty or duplicated entryId."));
                    }
                }
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_SHARD_JSON_INVALID",
                    $"Guide shard '{relativePath}' is invalid JSON: {exception.Message}"));
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    $"{prefix}.GUIDE_SHARD_UNREADABLE",
                    $"Guide shard '{relativePath}' could not be read: {exception.Message}"));
            }
        }

        return guides;
    }

    private static void ValidateCommand(
        DiscoveredExtensionPackage package,
        LibraryContribution contribution,
        string contentFilePath,
        CommandDefinition? command,
        string libraryLanguage,
        ExecutionTarget libraryExecutionTarget,
        bool libraryExecutionSupported,
        int schemaVersion,
        int index,
        ISet<string> stableEntryIds,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        var prefix = $"HYPERLIB[{contribution.Id}].ENTRY[{index + 1}]";
        if (command is null)
        {
            diagnostics.Add(Error(
                $"{prefix}.NULL",
                "A Hyperlib entry cannot be null."));
            return;
        }

        ValidateRequiredText(
            command.Name,
            160,
            $"{prefix}.NAME_INVALID",
            "name",
            diagnostics);
        ValidateRequiredText(
            command.DisplayName,
            schemaVersion == 2 ? 300 : 160,
            $"{prefix}.DISPLAY_NAME_INVALID",
            "displayName",
            diagnostics);
        ValidateRequiredText(
            command.Description,
            1000,
            $"{prefix}.DESCRIPTION_INVALID",
            "description",
            diagnostics);
        ValidateRequiredText(
            command.GuideDefinition,
            6000,
            $"{prefix}.GUIDE_INVALID",
            "definition",
            diagnostics);
        ValidateRequiredText(
            command.Template,
            6000,
            $"{prefix}.TEMPLATE_INVALID",
            "template",
            diagnostics);

        var stableId = string.IsNullOrWhiteSpace(command.Id)
            ? command.Name?.Trim() ?? string.Empty
            : command.Id.Trim();
        if (!string.IsNullOrWhiteSpace(stableId)
            && !stableEntryIds.Add(stableId))
        {
            diagnostics.Add(Error(
                $"{prefix}.ID_DUPLICATE",
                $"Entry ID or name '{stableId}' is duplicated in this contribution."));
        }

        if (schemaVersion == 2)
        {
            command.LegacyIds ??= [];
            foreach (var alias in command.LegacyIds
                         .Where(alias => !string.IsNullOrWhiteSpace(alias))
                         .Select(alias => alias.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(
                        alias,
                        stableId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!stableEntryIds.Add(alias))
                {
                    diagnostics.Add(Error(
                        $"{prefix}.LEGACY_ID_DUPLICATE",
                        $"Legacy entry ID '{alias}' collides with another entry ID or migration alias."));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(command.DocumentationUrl)
            && !IsHttpUri(command.DocumentationUrl))
        {
            diagnostics.Add(Error(
                $"{prefix}.DOCUMENTATION_URL_INVALID",
                "documentationUrl must be an absolute HTTP or HTTPS URL when supplied."));
        }

        if (schemaVersion == 2)
        {
            command.BrowsePath ??= [];
            if (command.BrowsePath.Count is < 1 or > 12
                || command.BrowsePath.Any(segment => segment is null
                    || string.IsNullOrWhiteSpace(segment.Kind)
                    || string.IsNullOrWhiteSpace(segment.Name)))
            {
                diagnostics.Add(Error(
                    $"{prefix}.BROWSE_PATH_INVALID",
                    "Schema-v2 entries require a 1–12 segment browsePath with a kind and name for every segment."));
            }
        }

        if (libraryExecutionTarget == ExecutionTarget.External
            && libraryExecutionSupported
            && !CommandLanguageRegistry.CanExecute(
                libraryLanguage,
                libraryExecutionTarget,
                command.PreferredShellKind))
        {
            diagnostics.Add(Error(
                $"{prefix}.RUNTIME_TARGET_MISMATCH",
                $"The {libraryLanguage} entry must target its registered " +
                "Hypershell terminal adapter."));
        }

        command.Parameters ??= [];
        if (command.Parameters.Count > MaximumParametersPerCommand)
        {
            diagnostics.Add(Error(
                $"{prefix}.PARAMETER_COUNT_INVALID",
                $"An entry can declare at most {MaximumParametersPerCommand:N0} parameters."));
        }

        var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in command.Parameters)
        {
            if (parameter is null
                || string.IsNullOrWhiteSpace(parameter.Name)
                || !parameterNames.Add(parameter.Name.Trim())
                || string.IsNullOrWhiteSpace(parameter.DisplayName)
                || string.IsNullOrWhiteSpace(parameter.Description))
            {
                diagnostics.Add(Error(
                    $"{prefix}.PARAMETER_INVALID",
                    "Parameters require unique names, display names, and descriptions."));
                continue;
            }

            if (libraryExecutionTarget is ExecutionTarget.CSharp
                    or ExecutionTarget.External)
            {
                var marker = string.IsNullOrWhiteSpace(parameter.Placeholder)
                    ? parameter.Name
                    : parameter.Placeholder;
                if (!command.Template.Contains(
                        $"{{{marker}}}",
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(Error(
                        $"{prefix}.PLACEHOLDER_MISSING",
                        $"Template does not contain the parameter placeholder '{{{marker}}}'."));
                }
            }
        }

        command.Examples ??= [];
        var minimumExamples = schemaVersion == 2
                              && command.CoverageTier
                              == CommandCoverageTier.Indexed
            ? 0
            : 1;
        if (command.Examples.Count < minimumExamples
            || command.Examples.Count > MaximumExamplesPerCommand)
        {
            diagnostics.Add(Error(
                $"{prefix}.EXAMPLE_COUNT_INVALID",
                minimumExamples == 0
                    ? $"Indexed entries allow 0–{MaximumExamplesPerCommand} examples."
                    : $"Guided and verified entries require 1–{MaximumExamplesPerCommand} learning examples."));
            return;
        }

        for (var exampleIndex = 0; exampleIndex < command.Examples.Count; exampleIndex++)
        {
            var example = command.Examples[exampleIndex];
            if (example is null
                || string.IsNullOrWhiteSpace(example.Title)
                || example.Title.Length > 160
                || string.IsNullOrWhiteSpace(example.Code)
                || example.Code.Length > 64 * 1024
                || string.IsNullOrWhiteSpace(example.Explanation)
                || example.Explanation.Length > 6000)
            {
                diagnostics.Add(Error(
                    $"{prefix}.EXAMPLE[{exampleIndex + 1}].INVALID",
                    "Examples require a title, bounded code, and a substantive explanation."));
                continue;
            }

            ValidateExampleAsset(
                package,
                contentFilePath,
                example,
                $"{prefix}.EXAMPLE[{exampleIndex + 1}]",
                diagnostics);
        }
    }

    private static void ValidateExampleAsset(
        DiscoveredExtensionPackage package,
        string contentFilePath,
        CommandUsageExample example,
        string diagnosticPrefix,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(example.ImagePath))
        {
            return;
        }

        var relativePath = example.ImagePath;
        if (Path.IsPathRooted(relativePath)
            || relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".."))
        {
            diagnostics.Add(Error(
                $"{diagnosticPrefix}.IMAGE_PATH_INVALID",
                "Example images must use a package-contained relative path."));
            return;
        }

        try
        {
            var imagePath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(contentFilePath)!,
                relativePath));
            var packagePrefix = Path.GetFullPath(package.PackageDirectory)
                                    .TrimEnd(
                                        Path.DirectorySeparatorChar,
                                        Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
            if (!imagePath.StartsWith(
                    packagePrefix,
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(imagePath))
            {
                diagnostics.Add(Error(
                    $"{diagnosticPrefix}.IMAGE_NOT_FOUND",
                    $"Example image '{relativePath}' is missing or outside the package."));
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                $"{diagnosticPrefix}.IMAGE_PATH_INVALID",
                $"Example image path is invalid: {exception.Message}"));
        }
    }

    private static void ValidateRequiredText(
        string? value,
        int maximumLength,
        string code,
        string fieldName,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            diagnostics.Add(Error(
                code,
                $"{fieldName} is required and cannot exceed {maximumLength:N0} characters."));
        }
    }

    private static string NormalizeLanguageId(string? value) =>
        CommandLanguageRegistry.NormalizeId(value);

    internal static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase));

    private static ExtensionDiagnostic Information(string code, string message) =>
        new(ExtensionDiagnosticSeverity.Information, code, message);

    private static ExtensionDiagnostic Error(string code, string message) =>
        new(ExtensionDiagnosticSeverity.Error, code, message);
}
