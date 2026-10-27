using Hypershell.Extensibility;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public enum ExtensionPackageStatus
{
    Validated,
    ApprovalRequired,
    Invalid,
    Incompatible,
    Duplicate
}

public enum ExtensionDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ExtensionDiagnostic(
    ExtensionDiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public string DisplayText => $"{Code}: {Message}";
}

public sealed class DiscoveredExtensionPackage
{
    public required string PackageDirectory { get; init; }
    public required string ManifestPath { get; init; }
    public ExtensionManifest? Manifest { get; internal set; }
    public string PackageSha256 { get; internal set; } = string.Empty;
    public ExtensionPackageStatus Status { get; internal set; }
    public IReadOnlyList<ExtensionDiagnostic> Diagnostics { get; internal set; } = [];

    public string DisplayName => Manifest?.DisplayName is { Length: > 0 } name
        ? name
        : Path.GetFileName(PackageDirectory);

    public string IdentityText => Manifest is null
        ? "Manifest identity unavailable"
        : $"{Manifest.Id}  •  v{Manifest.Version}";

    public string PublisherText => Manifest is null
        ? ManifestPath
        : $"{Manifest.Publisher}  •  {Manifest.Runtime}";

    public string DescriptionText => Manifest?.Description is { Length: > 0 } description
        ? description
        : "No description provided.";

    public string StatusText => Status switch
    {
        ExtensionPackageStatus.Validated => "VALIDATED · INACTIVE",
        ExtensionPackageStatus.ApprovalRequired => "APPROVAL REQUIRED",
        ExtensionPackageStatus.Incompatible => "INCOMPATIBLE",
        ExtensionPackageStatus.Duplicate => "DUPLICATE ID",
        _ => "INVALID"
    };

    public string StatusDetail => Status switch
    {
        ExtensionPackageStatus.Validated =>
            "Manifest and package resources passed discovery checks. Nothing has been activated or granted capabilities.",
        ExtensionPackageStatus.ApprovalRequired =>
            "The native package is structurally valid, but native code requires an explicit trust and capability approval flow.",
        ExtensionPackageStatus.Incompatible =>
            "This package targets a manifest contract that this Hypershell build cannot load.",
        ExtensionPackageStatus.Duplicate =>
            "More than one installed package declares this extension ID, so every copy is quarantined.",
        _ =>
            "The package is quarantined until its manifest and referenced resources pass validation."
    };

    public string CapabilitiesText
    {
        get
        {
            var capabilities = Manifest?.RequestedCapabilities ?? [];
            return capabilities.Count == 0
                ? "No capabilities requested"
                : string.Join(", ", capabilities);
        }
    }

    public string ContributionsText
    {
        get
        {
            if (Manifest?.Contributions is not { } contributions)
            {
                return "No readable contributions";
            }

            var parts = new List<string>();
            AddCount(parts, contributions.Libraries?.Count ?? 0, "library", "libraries");
            AddCount(parts, contributions.Panels?.Count ?? 0, "panel", "panels");
            AddCount(parts, contributions.DocumentFormats?.Count ?? 0, "document format", "document formats");
            AddCount(parts, contributions.Commands?.Count ?? 0, "command", "commands");
            AddCount(parts, contributions.AiTools?.Count ?? 0, "AI tool", "AI tools");
            return parts.Count == 0
                ? "No contributions declared"
                : string.Join("  •  ", parts);
        }
    }

    public bool HasDiagnostics => Diagnostics.Count > 0;

    private static void AddCount(
        ICollection<string> target,
        int count,
        string singular,
        string plural)
    {
        if (count > 0)
        {
            target.Add($"{count} {(count == 1 ? singular : plural)}");
        }
    }
}

public sealed record ExtensionPanelCandidate(
    DiscoveredExtensionPackage Package,
    WorkspacePanelRegistration Registration,
    string ViewResourcePath)
{
    public string PanelId => Registration.Contribution.Id;
    public bool IsWebView =>
        Package.Manifest?.Runtime == ExtensionRuntimeKind.WebView;
}

public sealed class ExtensionDiscoverySnapshot
{
    public required string RootDirectory { get; init; }
    public required DateTimeOffset ScannedAtUtc { get; init; }
    public IReadOnlyList<DiscoveredExtensionPackage> Packages { get; init; } = [];
    public IReadOnlyList<ExtensionLibraryCandidate> LibraryCandidates { get; init; } = [];
    public IReadOnlyList<ExtensionPanelCandidate> PanelCandidates { get; init; } = [];

    public int HyperlibEntryCount => LibraryCandidates.Sum(candidate =>
        candidate.EntryCount);

    public int ValidatedPackageCount => Packages.Count(package =>
        package.Status == ExtensionPackageStatus.Validated);

    public int ApprovalRequiredPackageCount => Packages.Count(package =>
        package.Status == ExtensionPackageStatus.ApprovalRequired);

    public int RejectedPackageCount => Packages.Count(package =>
        package.Status is ExtensionPackageStatus.Invalid
            or ExtensionPackageStatus.Incompatible
            or ExtensionPackageStatus.Duplicate);
}

public sealed class ExtensionDiscoveryService
{
    public const string ManifestFileName = "hypershell.extension.json";
    public const long MaximumManifestBytes = 1024 * 1024;
    public const long MaximumPackageBytes = 256L * 1024 * 1024;
    public const int MaximumPackageFiles = 4096;

    private static readonly Regex ExtensionIdPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)+$",
        RegexOptions.CultureInvariant);

    private static readonly Regex VersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ContributionIdPattern = new(
        "^[A-Za-z][A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant);

    private static readonly Regex FileExtensionPattern = new(
        "^\\.[A-Za-z0-9.+_-]+$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<ExtensionCapability> ElevatedCapabilities =
    [
        ExtensionCapability.FileRead,
        ExtensionCapability.FileWrite,
        ExtensionCapability.TerminalExecute,
        ExtensionCapability.Network,
        ExtensionCapability.Secrets
    ];

    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _jsonOptions =
        ExtensionContract.CreateJsonOptions();

    public ExtensionDiscoveryService(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public ExtensionDiscoverySnapshot Discover()
    {
        var packages = new List<DiscoveredExtensionPackage>();
        if (Directory.Exists(_rootDirectory))
        {
            try
            {
                foreach (var packageDirectory in Directory
                             .EnumerateDirectories(_rootDirectory)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    packages.Add(InspectPackageManifest(packageDirectory));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                packages.Add(new DiscoveredExtensionPackage
                {
                    PackageDirectory = _rootDirectory,
                    ManifestPath = Path.Combine(_rootDirectory, ManifestFileName),
                    Status = ExtensionPackageStatus.Invalid,
                    Diagnostics =
                    [
                        Error(
                            "EXTENSION_ROOT_UNREADABLE",
                            $"The extension directory could not be enumerated: {exception.Message}")
                    ]
                });
            }
        }

        QuarantineDuplicateIds(packages);
        var libraryCandidates =
            HyperlibContentPackageService.BuildCandidates(packages);
        var panelCandidates = BuildPanelCandidates(packages);
        return new ExtensionDiscoverySnapshot
        {
            RootDirectory = _rootDirectory,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Packages = packages,
            LibraryCandidates = libraryCandidates,
            PanelCandidates = panelCandidates
        };
    }

    /// <summary>
    /// Validates one package directory with the same manifest and static
    /// Hyperlib checks used by normal extension discovery. This entry point is
    /// intentionally side-effect free so package authoring and installation
    /// tools can inspect content before it reaches the active extension root.
    /// </summary>
    public DiscoveredExtensionPackage InspectPackage(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var package = InspectPackageManifest(Path.GetFullPath(packageDirectory));
        _ = HyperlibContentPackageService.BuildCandidates([package]);
        return package;
    }

    private DiscoveredExtensionPackage InspectPackageManifest(
        string packageDirectory)
    {
        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
        var diagnostics = new List<ExtensionDiagnostic>();
        var package = new DiscoveredExtensionPackage
        {
            PackageDirectory = packageDirectory,
            ManifestPath = manifestPath,
            Status = ExtensionPackageStatus.Invalid,
            Diagnostics = diagnostics
        };

        try
        {
            if ((File.GetAttributes(packageDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.Add(Error(
                    "PACKAGE_LINK_REJECTED",
                    "Package directories cannot be symbolic links or other reparse points."));
                return package;
            }

            if (!File.Exists(manifestPath))
            {
                diagnostics.Add(Error(
                    "MANIFEST_MISSING",
                    $"The package directory does not contain {ManifestFileName}."));
                return package;
            }

            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length > MaximumManifestBytes)
            {
                diagnostics.Add(Error(
                    "MANIFEST_TOO_LARGE",
                    $"The manifest exceeds the {MaximumManifestBytes / 1024:N0} KiB discovery limit."));
                return package;
            }

            package.Manifest = JsonSerializer.Deserialize<ExtensionManifest>(
                File.ReadAllText(manifestPath),
                _jsonOptions);
            if (package.Manifest is null)
            {
                diagnostics.Add(Error(
                    "MANIFEST_EMPTY",
                    "The manifest did not contain an extension object."));
                return package;
            }

            if (package.Manifest.SchemaVersion
                != ExtensionContract.CurrentManifestSchemaVersion)
            {
                package.Status = ExtensionPackageStatus.Incompatible;
                diagnostics.Add(Error(
                    "SCHEMA_VERSION_UNSUPPORTED",
                    $"Manifest schema {package.Manifest.SchemaVersion} is not supported; this build accepts schema {ExtensionContract.CurrentManifestSchemaVersion}."));
                return package;
            }

            ValidateManifest(packageDirectory, package.Manifest, diagnostics);
            if (!diagnostics.Any(diagnostic =>
                    diagnostic.Severity == ExtensionDiagnosticSeverity.Error))
            {
                try
                {
                    package.PackageSha256 = ComputePackageSha256(packageDirectory);
                }
                catch (InvalidDataException exception)
                {
                    diagnostics.Add(Error(
                        "PACKAGE_HASH_REJECTED",
                        exception.Message));
                }
            }

            package.Status = diagnostics.Any(diagnostic =>
                diagnostic.Severity == ExtensionDiagnosticSeverity.Error)
                ? ExtensionPackageStatus.Invalid
                : package.Manifest.Runtime == ExtensionRuntimeKind.NativeDotNet
                    ? ExtensionPackageStatus.ApprovalRequired
                    : ExtensionPackageStatus.Validated;
            return package;
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error(
                "MANIFEST_JSON_INVALID",
                exception.Message));
            return package;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            diagnostics.Add(Error(
                "PACKAGE_UNREADABLE",
                $"The package could not be inspected: {exception.Message}"));
            return package;
        }
    }

    private static void ValidateManifest(
        string packageDirectory,
        ExtensionManifest manifest,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        ValidateText(manifest.Id, "extension ID", 3, 128, diagnostics);
        if (!ExtensionIdPattern.IsMatch(manifest.Id ?? string.Empty))
        {
            diagnostics.Add(Error(
                "EXTENSION_ID_INVALID",
                "The extension ID must be lowercase, namespaced, and stable (for example, publisher.extension)."));
        }

        ValidateText(manifest.DisplayName, "display name", 1, 80, diagnostics);
        ValidateText(manifest.Publisher, "publisher", 1, 80, diagnostics);
        ValidateText(manifest.Description, "description", 0, 500, diagnostics);
        if (!VersionPattern.IsMatch(manifest.Version ?? string.Empty))
        {
            diagnostics.Add(Error(
                "VERSION_INVALID",
                "The extension version must use semantic version syntax such as 1.2.0 or 1.2.0-beta.1."));
        }

        var requestedCapabilities = manifest.RequestedCapabilities ?? [];
        if (requestedCapabilities.Count
            != requestedCapabilities.Distinct().Count())
        {
            diagnostics.Add(Error(
                "CAPABILITY_DUPLICATE",
                "requestedCapabilities contains duplicate values."));
        }

        var elevated = requestedCapabilities
            .Where(ElevatedCapabilities.Contains)
            .Distinct()
            .ToList();
        if (elevated.Count > 0)
        {
            diagnostics.Add(Warning(
                "CAPABILITY_REVIEW_REQUIRED",
                $"Elevated capabilities require explicit user approval before activation: {string.Join(", ", elevated)}."));
        }

        var entryPointRequired = manifest.Runtime is
            ExtensionRuntimeKind.WebView or ExtensionRuntimeKind.NativeDotNet;
        ValidatePackageResource(
            packageDirectory,
            manifest.EntryPoint,
            "runtime entry point",
            entryPointRequired,
            mustBeFile: true,
            diagnostics);
        if (manifest.Runtime == ExtensionRuntimeKind.NativeDotNet
            && !string.IsNullOrWhiteSpace(manifest.EntryPoint)
            && !string.Equals(
                Path.GetExtension(manifest.EntryPoint),
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error(
                "NATIVE_ENTRYPOINT_INVALID",
                "A NativeDotNet entry point must reference a .dll file."));
        }

        if (manifest.Contributions is not { } contributions)
        {
            diagnostics.Add(Error(
                "CONTRIBUTIONS_MISSING",
                "A contributions object is required."));
            return;
        }

        var contributionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libraries = contributions.Libraries ?? [];
        var panels = contributions.Panels ?? [];
        var documentFormats = contributions.DocumentFormats ?? [];
        var commands = contributions.Commands ?? [];
        var aiTools = contributions.AiTools ?? [];

        if (manifest.Runtime == ExtensionRuntimeKind.ContentOnly)
        {
            var unsupportedCapabilities = requestedCapabilities
                .Where(capability => capability is not
                    ExtensionCapability.LibraryContent
                    and not ExtensionCapability.DocumentProvider)
                .Distinct()
                .ToList();
            if (unsupportedCapabilities.Count > 0)
            {
                diagnostics.Add(Error(
                    "CONTENT_ONLY_CAPABILITY_INVALID",
                    $"ContentOnly packages cannot request executable or workspace capabilities: {string.Join(", ", unsupportedCapabilities)}."));
            }

            if (panels.Count + commands.Count + aiTools.Count > 0
                || documentFormats.Any(format =>
                    format?.Kind != DocumentContributionKind.TextEditor))
            {
                diagnostics.Add(Error(
                    "CONTENT_ONLY_CONTRIBUTION_INVALID",
                    "ContentOnly packages may contribute static libraries and TextEditor format metadata only."));
            }
        }

        RequireCapability(
            libraries.Count > 0,
            ExtensionCapability.LibraryContent,
            "library",
            requestedCapabilities,
            diagnostics);
        RequireCapability(
            panels.Count > 0,
            ExtensionCapability.WorkspacePanel,
            "workspace panel",
            requestedCapabilities,
            diagnostics);
        RequireCapability(
            documentFormats.Count > 0,
            ExtensionCapability.DocumentProvider,
            "document format",
            requestedCapabilities,
            diagnostics);
        RequireCapability(
            commands.Count > 0,
            ExtensionCapability.Commands,
            "command",
            requestedCapabilities,
            diagnostics);
        RequireCapability(
            aiTools.Count > 0,
            ExtensionCapability.AiTool,
            "AI tool",
            requestedCapabilities,
            diagnostics);

        foreach (var library in libraries)
        {
            if (library is null)
            {
                diagnostics.Add(Error("LIBRARY_NULL", "A library contribution cannot be null."));
                continue;
            }

            ValidateContributionIdentity(
                library.Id,
                library.DisplayName,
                contributionIds,
                diagnostics);
            ValidatePackageResource(
                packageDirectory,
                library.ContentPath,
                $"library '{library.Id}' content",
                required: true,
                mustBeFile: false,
                diagnostics);
            if (library.LanguageIds is null || library.LanguageIds.Count == 0)
            {
                diagnostics.Add(Error(
                    "LIBRARY_LANGUAGES_MISSING",
                    $"Library '{library.Id}' must declare at least one language ID."));
            }
            else if (library.LanguageIds.Any(string.IsNullOrWhiteSpace)
                     || library.LanguageIds.Count
                     != library.LanguageIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                diagnostics.Add(Error(
                    "LIBRARY_LANGUAGES_INVALID",
                    $"Library '{library.Id}' language IDs must be non-empty and unique."));
            }

            ValidateText(
                library.Provenance,
                $"library '{library.Id}' provenance",
                1,
                500,
                diagnostics);
            ValidateText(
                library.ReviewedBy,
                $"library '{library.Id}' reviewer",
                0,
                120,
                diagnostics);
            if (library.ReviewState == HyperlibReviewState.Reviewed
                && string.IsNullOrWhiteSpace(library.ReviewedBy))
            {
                diagnostics.Add(Error(
                    "LIBRARY_REVIEWER_MISSING",
                    $"Library '{library.Id}' claims Reviewed status but does not identify a reviewer."));
            }
            else if (library.ReviewState == HyperlibReviewState.Draft)
            {
                diagnostics.Add(Warning(
                    "LIBRARY_REVIEW_PENDING",
                    $"Library '{library.Id}' is publisher-declared Draft content. It remains visibly labeled when loaded."));
            }

            if (!string.IsNullOrWhiteSpace(library.SourceUrl)
                && !HyperlibContentPackageService.IsHttpUri(
                    library.SourceUrl))
            {
                diagnostics.Add(Error(
                    "LIBRARY_SOURCE_URL_INVALID",
                    $"Library '{library.Id}' sourceUrl must be an absolute HTTP or HTTPS URL."));
            }
        }

        foreach (var panel in panels)
        {
            if (panel is null)
            {
                diagnostics.Add(Error("PANEL_NULL", "A panel contribution cannot be null."));
                continue;
            }

            ValidateContributionIdentity(
                panel.Id,
                panel.DisplayName,
                contributionIds,
                diagnostics);
            ValidatePackageResource(
                packageDirectory,
                panel.ViewResource,
                $"panel '{panel.Id}' view resource",
                required: true,
                mustBeFile: true,
                diagnostics);
            if (panel.MessageApiVersion is < 1
                or > ExtensionPanelMessagingContract.CurrentVersion)
            {
                diagnostics.Add(Error(
                    "PANEL_MESSAGE_API_UNSUPPORTED",
                    $"Panel '{panel.Id}' requests message API version {panel.MessageApiVersion}; this build supports version {ExtensionPanelMessagingContract.CurrentVersion}."));
            }
            else if (panel.MessageApiVersion > 0
                     && manifest.Runtime != ExtensionRuntimeKind.WebView)
            {
                diagnostics.Add(Error(
                    "PANEL_MESSAGE_API_RUNTIME_INVALID",
                    $"Panel '{panel.Id}' can use host messaging only with the WebView runtime."));
            }
            else if (panel.MessageApiVersion > 0)
            {
                diagnostics.Add(Information(
                    "PANEL_MESSAGE_API_DECLARED",
                    $"Panel '{panel.Id}' declares host message API v{panel.MessageApiVersion}. "
                    + (requestedCapabilities.Contains(
                        ExtensionCapability.WorkspaceMetadata)
                        ? "Approved device-local state and read-only workspace context methods may be enabled."
                        : "Only handshake and device-local state methods may be enabled.")));
            }

            ValidatePanelPlacement(panel, diagnostics);
        }

        if (requestedCapabilities.Contains(
                ExtensionCapability.WorkspaceMetadata)
            && panels.All(panel => panel?.MessageApiVersion is null))
        {
            diagnostics.Add(Warning(
                "WORKSPACE_METADATA_UNUSED",
                "WorkspaceMetadata is requested, but no panel declares a host message API that can consume it."));
        }

        foreach (var documentFormat in documentFormats)
        {
            if (documentFormat is null)
            {
                diagnostics.Add(Error(
                    "DOCUMENT_FORMAT_NULL",
                    "A document format contribution cannot be null."));
                continue;
            }

            ValidateContributionIdentity(
                documentFormat.Id,
                documentFormat.DisplayName,
                contributionIds,
                diagnostics);
            if (documentFormat.FileExtensions is null
                || documentFormat.FileExtensions.Count == 0
                || documentFormat.FileExtensions.Any(extension =>
                    !FileExtensionPattern.IsMatch(extension ?? string.Empty))
                || documentFormat.FileExtensions.Count
                != documentFormat.FileExtensions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count())
            {
                diagnostics.Add(Error(
                    "DOCUMENT_EXTENSIONS_INVALID",
                    $"Document format '{documentFormat.Id}' must declare unique extensions such as .json or .cs."));
            }

            ValidateText(
                documentFormat.LanguageId,
                $"document format '{documentFormat.Id}' language ID",
                1,
                80,
                diagnostics);
            var resourceRequired = documentFormat.Kind
                is DocumentContributionKind.WebViewer
                    or DocumentContributionKind.NativeViewer;
            ValidatePackageResource(
                packageDirectory,
                documentFormat.ViewResource,
                $"document format '{documentFormat.Id}' view resource",
                resourceRequired,
                mustBeFile: true,
                diagnostics);
        }

        foreach (var command in commands)
        {
            if (command is null)
            {
                diagnostics.Add(Error("COMMAND_NULL", "A command contribution cannot be null."));
                continue;
            }

            ValidateContributionIdentity(
                command.Id,
                command.DisplayName,
                contributionIds,
                diagnostics);
            ValidateText(
                command.Handler,
                $"command '{command.Id}' handler",
                1,
                160,
                diagnostics);
        }

        foreach (var aiTool in aiTools)
        {
            if (aiTool is null)
            {
                diagnostics.Add(Error("AI_TOOL_NULL", "An AI tool contribution cannot be null."));
                continue;
            }

            ValidateContributionIdentity(
                aiTool.Id,
                aiTool.DisplayName,
                contributionIds,
                diagnostics);
            ValidateText(
                aiTool.Description,
                $"AI tool '{aiTool.Id}' description",
                1,
                500,
                diagnostics);
            ValidateText(
                aiTool.Handler,
                $"AI tool '{aiTool.Id}' handler",
                1,
                160,
                diagnostics);
            ValidatePackageResource(
                packageDirectory,
                aiTool.InputSchemaPath,
                $"AI tool '{aiTool.Id}' input schema",
                required: false,
                mustBeFile: true,
                diagnostics);
            if (!aiTool.RequiresUserConfirmation)
            {
                diagnostics.Add(Warning(
                    "AI_CONFIRMATION_NOT_REQUESTED",
                    $"AI tool '{aiTool.Id}' does not request confirmation; the host may still require it based on policy."));
            }
        }

        if (libraries.Count + panels.Count + documentFormats.Count
            + commands.Count + aiTools.Count == 0)
        {
            diagnostics.Add(Information(
                "NO_CONTRIBUTIONS",
                "The manifest is valid but does not currently contribute any library content, panels, formats, commands, or AI tools."));
        }
    }

    private static void ValidatePanelPlacement(
        WorkspacePanelContribution panel,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        var placement = panel.Placement;
        if (placement is null)
        {
            diagnostics.Add(Error(
                "PANEL_PLACEMENT_MISSING",
                $"Panel '{panel.Id}' requires a placement policy."));
            return;
        }

        var allowedSlots = placement.AllowedSlots ?? [];
        if (allowedSlots.Count == 0
            || allowedSlots.Count != allowedSlots.Distinct().Count()
            || !allowedSlots.Contains(placement.PreferredSlot))
        {
            diagnostics.Add(Error(
                "PANEL_SLOTS_INVALID",
                $"Panel '{panel.Id}' must declare unique allowed slots that include its preferred slot."));
        }

        if (placement.MinimumWidth < 160
            || placement.MaximumWidth > 1600
            || placement.MinimumWidth > placement.PreferredWidth
            || placement.PreferredWidth > placement.MaximumWidth)
        {
            diagnostics.Add(Error(
                "PANEL_WIDTHS_INVALID",
                $"Panel '{panel.Id}' widths must be ordered minimum, preferred, maximum and remain within 160–1600 pixels."));
        }

        if (placement.MinimumHeight < 120
            || placement.MaximumHeight > 1200
            || placement.MinimumHeight > placement.PreferredHeight
            || placement.PreferredHeight > placement.MaximumHeight)
        {
            diagnostics.Add(Error(
                "PANEL_HEIGHTS_INVALID",
                $"Panel '{panel.Id}' heights must be ordered minimum, preferred, maximum and remain within 120–1200 pixels."));
        }

        if (allowedSlots.Contains(WorkspacePanelSlot.Floating)
            && !placement.AllowFloating)
        {
            diagnostics.Add(Error(
                "PANEL_FLOATING_CONFLICT",
                $"Panel '{panel.Id}' allows the Floating slot but disables floating behavior."));
        }
        else if (placement.AllowFloating
                 && !allowedSlots.Contains(WorkspacePanelSlot.Floating))
        {
            diagnostics.Add(Warning(
                "PANEL_FLOATING_UNUSED",
                $"Panel '{panel.Id}' enables floating but does not list Floating as an allowed slot."));
        }

        if (placement.MayReplace is { } replacements
            && replacements.Count != replacements.Distinct().Count())
        {
            diagnostics.Add(Error(
                "PANEL_REPLACEMENT_DUPLICATE",
                $"Panel '{panel.Id}' contains duplicate replacement targets."));
        }
    }

    private static void ValidateContributionIdentity(
        string id,
        string displayName,
        ISet<string> contributionIds,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id.Length is < 2 or > 64
            || !ContributionIdPattern.IsMatch(id))
        {
            diagnostics.Add(Error(
                "CONTRIBUTION_ID_INVALID",
                "Contribution IDs must start with a letter and contain 2–64 letters, numbers, dots, underscores, or hyphens."));
        }
        else if (!contributionIds.Add(id))
        {
            diagnostics.Add(Error(
                "CONTRIBUTION_ID_DUPLICATE",
                $"Contribution ID '{id}' is declared more than once in the package."));
        }

        ValidateText(displayName, $"contribution '{id}' display name", 1, 80, diagnostics);
    }

    private static void RequireCapability(
        bool hasContributions,
        ExtensionCapability requiredCapability,
        string contributionType,
        IReadOnlyCollection<ExtensionCapability> requestedCapabilities,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        if (hasContributions && !requestedCapabilities.Contains(requiredCapability))
        {
            diagnostics.Add(Error(
                "CAPABILITY_MISSING",
                $"A {contributionType} contribution requires the {requiredCapability} capability."));
        }
    }

    private static void ValidateText(
        string? value,
        string fieldName,
        int minimumLength,
        int maximumLength,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        var length = value?.Length ?? 0;
        if (length < minimumLength || length > maximumLength)
        {
            diagnostics.Add(Error(
                "TEXT_LENGTH_INVALID",
                $"The {fieldName} must contain {minimumLength}–{maximumLength} characters."));
        }
    }

    private static void ValidatePackageResource(
        string packageDirectory,
        string? relativePath,
        string resourceName,
        bool required,
        bool mustBeFile,
        ICollection<ExtensionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (required)
            {
                diagnostics.Add(Error(
                    "RESOURCE_PATH_MISSING",
                    $"The {resourceName} path is required."));
            }

            return;
        }

        if (Path.IsPathRooted(relativePath)
            || relativePath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".."))
        {
            diagnostics.Add(Error(
                "RESOURCE_PATH_ESCAPES_PACKAGE",
                $"The {resourceName} must be a relative path contained by the package."));
            return;
        }

        try
        {
            var packageRoot = Path.GetFullPath(packageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var resourcePath = Path.GetFullPath(
                Path.Combine(packageRoot, relativePath));
            var containmentPrefix = packageRoot + Path.DirectorySeparatorChar;
            if (!resourcePath.StartsWith(
                    containmentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "RESOURCE_PATH_ESCAPES_PACKAGE",
                    $"The {resourceName} resolves outside its package directory."));
                return;
            }

            var exists = mustBeFile
                ? File.Exists(resourcePath)
                : File.Exists(resourcePath) || Directory.Exists(resourcePath);
            if (!exists)
            {
                diagnostics.Add(Error(
                    "RESOURCE_NOT_FOUND",
                    $"The {resourceName} does not exist at '{relativePath}'."));
                return;
            }

            if (ContainsReparsePoint(packageRoot, resourcePath))
            {
                diagnostics.Add(Error(
                    "RESOURCE_LINK_REJECTED",
                    $"The {resourceName} crosses a symbolic link or other reparse point."));
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                "RESOURCE_PATH_INVALID",
                $"The {resourceName} path is invalid: {exception.Message}"));
        }
    }

    private static bool ContainsReparsePoint(
        string packageRoot,
        string resourcePath)
    {
        var currentPath = packageRoot;
        foreach (var segment in Path.GetRelativePath(packageRoot, resourcePath)
                     .Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static string ComputePackageSha256(string packageDirectory)
    {
        var packageRoot = Path.GetFullPath(packageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = new List<(string FullPath, string RelativePath, long Length)>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(packageRoot);
        long totalBytes = 0;

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Package hashing rejected a symbolic link or other directory reparse point.");
            }

            foreach (var childDirectory in Directory
                         .EnumerateDirectories(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Package hashing rejected a symbolic link or other directory reparse point.");
                }

                pendingDirectories.Push(childDirectory);
            }

            foreach (var filePath in Directory
                         .EnumerateFiles(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Package hashing rejected a symbolic link or other file reparse point.");
                }

                var length = new FileInfo(filePath).Length;
                if (length > MaximumPackageBytes - totalBytes)
                {
                    throw new InvalidDataException(
                        $"The package exceeds the {MaximumPackageBytes / 1024 / 1024:N0} MiB discovery limit.");
                }

                totalBytes += length;
                files.Add((
                    filePath,
                    Path.GetRelativePath(packageRoot, filePath)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    length));
                if (files.Count > MaximumPackageFiles)
                {
                    throw new InvalidDataException(
                        $"The package exceeds the {MaximumPackageFiles:N0}-file discovery limit.");
                }

            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var lengthBytes = new byte[sizeof(long)];
        var buffer = new byte[81920];
        foreach (var file in files.OrderBy(
                     file => file.RelativePath,
                     StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData([0]);
            BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, file.Length);
            hash.AppendData(lengthBytes);
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void QuarantineDuplicateIds(
        IReadOnlyCollection<DiscoveredExtensionPackage> packages)
    {
        foreach (var duplicateGroup in packages
                     .Where(package => !string.IsNullOrWhiteSpace(package.Manifest?.Id))
                     .GroupBy(package => package.Manifest!.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var package in duplicateGroup)
            {
                package.Status = ExtensionPackageStatus.Duplicate;
                package.Diagnostics = package.Diagnostics
                    .Append(Error(
                        "EXTENSION_ID_DUPLICATE",
                        $"Extension ID '{duplicateGroup.Key}' is declared by {duplicateGroup.Count()} installed packages."))
                    .ToList();
            }
        }
    }

    private static IReadOnlyList<ExtensionPanelCandidate> BuildPanelCandidates(
        IEnumerable<DiscoveredExtensionPackage> packages)
    {
        var candidates = new List<ExtensionPanelCandidate>();
        var registry = WorkspacePanelRegistry.CreateWithBuiltIns();
        foreach (var package in packages.Where(package =>
                     package.Status == ExtensionPackageStatus.Validated
                     && package.Manifest?.Contributions?.Panels is not null))
        {
            var packageCandidates = new List<ExtensionPanelCandidate>();
            try
            {
                foreach (var panel in package.Manifest!.Contributions.Panels)
                {
                    var qualifiedContribution = new WorkspacePanelContribution
                    {
                        Id = $"{package.Manifest.Id}.{panel.Id}",
                        DisplayName = panel.DisplayName,
                        ViewResource = panel.ViewResource,
                        MessageApiVersion = panel.MessageApiVersion,
                        Placement = panel.Placement
                    };
                    var registration = new WorkspacePanelRegistration(
                        package.Manifest.Id,
                        qualifiedContribution);
                    registry.Register(registration);
                    packageCandidates.Add(new ExtensionPanelCandidate(
                        package,
                        registration,
                        Path.GetFullPath(Path.Combine(
                            package.PackageDirectory,
                            panel.ViewResource))));
                }

                candidates.AddRange(packageCandidates);
            }
            catch (ArgumentException exception)
            {
                package.Status = ExtensionPackageStatus.Invalid;
                package.Diagnostics = package.Diagnostics
                    .Append(Error(
                        "PANEL_REGISTRATION_INVALID",
                        exception.Message))
                    .ToList();
            }
            catch (InvalidOperationException exception)
            {
                package.Status = ExtensionPackageStatus.Duplicate;
                package.Diagnostics = package.Diagnostics
                    .Append(Error(
                        "PANEL_ID_DUPLICATE",
                        exception.Message))
                    .ToList();
            }
        }

        return candidates;
    }

    private static ExtensionDiagnostic Information(string code, string message) =>
        new(ExtensionDiagnosticSeverity.Information, code, message);

    private static ExtensionDiagnostic Warning(string code, string message) =>
        new(ExtensionDiagnosticSeverity.Warning, code, message);

    private static ExtensionDiagnostic Error(string code, string message) =>
        new(ExtensionDiagnosticSeverity.Error, code, message);
}
