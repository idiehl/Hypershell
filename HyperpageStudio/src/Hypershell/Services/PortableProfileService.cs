using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public enum ProfileImportDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ProfileImportDiagnostic(
    ProfileImportDiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public string DisplayText => $"{Code}: {Message}";
}

public sealed class ProfileImportPreview
{
    public required string SourcePath { get; init; }
    public required long BundleBytes { get; init; }
    public PortableProfileEnvelope? Envelope { get; init; }
    public IReadOnlyList<ProfileImportDiagnostic> Diagnostics { get; init; } = [];

    public bool CanImport =>
        Envelope is not null
        && Diagnostics.All(diagnostic =>
            diagnostic.Severity != ProfileImportDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity == ProfileImportDiagnosticSeverity.Warning);
    public int ErrorCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity == ProfileImportDiagnosticSeverity.Error);
}

public sealed class PortableProfileService
{
    private const int MaximumJsonDepth = 64;
    private const int MaximumProfiles = 128;
    private const int MaximumReferences = 512;
    private const int MaximumLibrariesPerEnvironment = 256;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly Regex IdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ColorPattern = new(
        "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        "^[0-9A-Fa-f]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions CompactJsonOptions =
        CreateJsonOptions(writeIndented: false);
    private static readonly JsonSerializerOptions IndentedJsonOptions =
        CreateJsonOptions(writeIndented: true);

    public static IReadOnlyList<string> DeviceLocalExclusions { get; } =
    [
        "Extension approvals, package hashes, AI permissions, and other trust decisions",
        "Credentials, secret values, authentication state, and secret references",
        "Profile images and other device-local account presentation assets",
        "Terminal executables, arguments, initialization commands, and startup commands",
        "Absolute workspace, project, startup-directory, and terminal background-image paths",
        "Open documents, document contents, editor sessions, and recovery snapshots",
        "WebView browser data and extension panel runtime state"
    ];

    public PortableProfileSnapshot CreateSnapshot(
        AppSettings settings,
        ExtensionDiscoverySnapshot extensionCatalog,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(extensionCatalog);

        var extensions = BuildExtensionReferences(settings, extensionCatalog);
        var libraries = BuildLibraryReferences(settings, extensionCatalog);
        return new PortableProfileSnapshot
        {
            Id = NormalizeId(
                settings.PortableProfileId,
                Guid.NewGuid().ToString("N")),
            DisplayName = NormalizeDisplayName(
                displayName ?? settings.PortableProfileDisplayName),
            Revision = Math.Max(0, settings.PortableProfileRevision),
            UpdatedAtUtc = settings.PortableProfileUpdatedAtUtc,
            WorkspacePreferences = new PortableWorkspacePreferences(),
            Configuration = new PortableConfigurationSnapshot
            {
                Application = ToPortable(settings.Application),
                CommandBuilder = ToPortable(settings.CommandBuilder),
                Explorer = ToPortable(settings.Explorer),
                ActiveTerminalProfileId = settings.ActiveTerminalProfileId,
                TerminalConfigurations = settings.TerminalConfigurationProfiles
                    .Select(ToPortable)
                    .ToList(),
                ActiveEnvironmentId = settings.ActiveEnvironmentId,
                Environments = settings.Environments
                    .Select(ToPortable)
                    .ToList(),
                WorkspaceLayout = CreatePortableLayout(settings.WorkspaceLayout)
            },
            Extensions = extensions,
            Libraries = libraries,
            // Version 1 deliberately carries repository references only. The
            // current Explorer model contains local absolute paths, so it has
            // nothing that is safe to add here yet.
            Workspaces = []
        };
    }

    public PortableProfileEnvelope CreateEnvelope(
        PortableProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PortableProfileEnvelope
        {
            ExportedAtUtc = DateTimeOffset.UtcNow,
            ExportedBy = $"Hypershell {GetApplicationVersion()}",
            ProfileSha256 = ComputeProfileSha256(snapshot),
            Profile = snapshot
        };
    }

    public ReadOnlyMemory<byte> SerializeEnvelope(
        PortableProfileEnvelope envelope,
        bool writeIndented = true)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var validationDiagnostics = new List<ProfileImportDiagnostic>();
        ValidateEnvelope(envelope, validationDiagnostics);
        ValidateProfile(envelope.Profile, validationDiagnostics);
        var validationErrors = validationDiagnostics
            .Where(diagnostic =>
                diagnostic.Severity == ProfileImportDiagnosticSeverity.Error)
            .ToList();
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "The current profile cannot be serialized: "
                + string.Join(
                    " ",
                    validationErrors.Take(3).Select(error => error.Message)));
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            writeIndented ? IndentedJsonOptions : CompactJsonOptions);
        if (payload.LongLength > ProfileContract.MaximumBundleBytes)
        {
            throw new InvalidOperationException(
                $"The portable profile exceeds the {FormatBytes(ProfileContract.MaximumBundleBytes)} limit.");
        }

        return payload;
    }

    public PortableProfileEnvelope Export(
        string destinationPath,
        PortableProfileSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException(
                "Choose a destination for the portable profile.",
                nameof(destinationPath));
        }

        var fullPath = Path.GetFullPath(destinationPath);
        if (!string.Equals(
                Path.GetExtension(fullPath),
                ProfileContract.FileExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Portable profiles must use the {ProfileContract.FileExtension} extension.");
        }

        var envelope = CreateEnvelope(snapshot);
        var payload = SerializeEnvelope(envelope).ToArray();

        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException(
                            "The portable profile destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return envelope;
    }

    public ProfileImportPreview Inspect(
        string sourcePath,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException(
                "Choose a portable profile to inspect.",
                nameof(sourcePath));
        }

        ArgumentNullException.ThrowIfNull(extensionCatalog);
        ArgumentNullException.ThrowIfNull(terminalProfiles);

        var fullPath = Path.GetFullPath(sourcePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The selected portable profile could not be found.",
                fullPath);
        }

        var diagnostics = new List<ProfileImportDiagnostic>();
        if (!string.Equals(
                file.Extension,
                ProfileContract.FileExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                diagnostics,
                "PROFILE_FILE_EXTENSION",
                $"Choose a {ProfileContract.FileExtension} file.");
        }

        if (file.Length <= 0)
        {
            AddError(diagnostics, "PROFILE_EMPTY", "The selected file is empty.");
            return CreatePreview(fullPath, file.Length, null, diagnostics);
        }

        if (file.Length > ProfileContract.MaximumBundleBytes)
        {
            AddError(
                diagnostics,
                "PROFILE_TOO_LARGE",
                $"The bundle is larger than the {FormatBytes(ProfileContract.MaximumBundleBytes)} limit.");
            return CreatePreview(fullPath, file.Length, null, diagnostics);
        }

        return InspectPayloadCore(
            File.ReadAllBytes(fullPath),
            fullPath,
            extensionCatalog,
            terminalProfiles,
            diagnostics);
    }

    public ProfileImportPreview InspectPayload(
        ReadOnlyMemory<byte> payload,
        string sourceIdentifier,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentifier))
        {
            throw new ArgumentException(
                "Give the profile payload a source identifier.",
                nameof(sourceIdentifier));
        }

        ArgumentNullException.ThrowIfNull(extensionCatalog);
        ArgumentNullException.ThrowIfNull(terminalProfiles);
        return InspectPayloadCore(
            payload,
            sourceIdentifier.Trim(),
            extensionCatalog,
            terminalProfiles,
            []);
    }

    private ProfileImportPreview InspectPayloadCore(
        ReadOnlyMemory<byte> sourcePayload,
        string sourceIdentifier,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles,
        List<ProfileImportDiagnostic> diagnostics)
    {
        if (sourcePayload.Length <= 0)
        {
            AddError(diagnostics, "PROFILE_EMPTY", "The selected profile payload is empty.");
            return CreatePreview(sourceIdentifier, sourcePayload.Length, null, diagnostics);
        }

        if (sourcePayload.Length > ProfileContract.MaximumBundleBytes)
        {
            AddError(
                diagnostics,
                "PROFILE_TOO_LARGE",
                $"The profile payload is larger than the {FormatBytes(ProfileContract.MaximumBundleBytes)} limit.");
            return CreatePreview(sourceIdentifier, sourcePayload.Length, null, diagnostics);
        }

        PortableProfileEnvelope? envelope = null;
        try
        {
            var offset = sourcePayload.Span.StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF })
                ? 3
                : 0;
            var payload = sourcePayload[offset..];
            _ = StrictUtf8.GetString(payload.Span);
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaximumJsonDepth
                });
            ValidateRequiredJsonMembers(document.RootElement, diagnostics);
            envelope = JsonSerializer.Deserialize<PortableProfileEnvelope>(
                payload.Span,
                CompactJsonOptions);
            if (envelope is null)
            {
                AddError(
                    diagnostics,
                    "PROFILE_DESERIALIZATION",
                    "The bundle did not contain a readable profile envelope.");
            }
        }
        catch (DecoderFallbackException)
        {
            AddError(
                diagnostics,
                "PROFILE_UTF8",
                "Portable profiles must be valid UTF-8 JSON.");
        }
        catch (JsonException exception)
        {
            AddError(
                diagnostics,
                "PROFILE_JSON",
                $"The bundle is not valid profile JSON: {exception.Message}");
        }

        if (envelope is not null)
        {
            try
            {
                ValidateEnvelope(envelope, diagnostics);
                ValidateProfile(envelope.Profile, diagnostics);
                AddCompatibilityDiagnostics(
                    envelope.Profile,
                    extensionCatalog,
                    terminalProfiles,
                    diagnostics);
            }
            catch (Exception exception) when (exception is NullReferenceException
                                             or ArgumentNullException)
            {
                AddError(
                    diagnostics,
                    "PROFILE_NULL_MEMBER",
                    "A required profile object or collection is null.");
                envelope = null;
            }
        }

        return CreatePreview(
            sourceIdentifier,
            sourcePayload.Length,
            envelope,
            diagnostics);
    }

    public AppSettings CreateMergedSettings(
        AppSettings currentSettings,
        PortableProfileSnapshot importedProfile,
        WorkspacePanelRegistry workspacePanelRegistry)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(importedProfile);
        ArgumentNullException.ThrowIfNull(workspacePanelRegistry);

        var merged = CloneSettings(currentSettings);
        var configuration = importedProfile.Configuration;
        merged.PortableProfileId = importedProfile.Id;
        merged.PortableProfileDisplayName = importedProfile.DisplayName;
        merged.PortableProfileRevision = Math.Max(0, importedProfile.Revision);
        merged.PortableProfileUpdatedAtUtc = importedProfile.UpdatedAtUtc;
        merged.ProfileExtensions = new ObservableCollection<ProfileExtensionReference>(
            importedProfile.Extensions.Select(Clone));
        merged.ProfileLibraries = new ObservableCollection<ProfileLibraryReference>(
            importedProfile.Libraries.Select(Clone));

        Apply(configuration.Application, merged.Application);
        Apply(configuration.CommandBuilder, merged.CommandBuilder);
        // Only display behavior travels. Startup locations and project paths
        // remain exactly as they were on this device.
        merged.Explorer.ShowHiddenItems = configuration.Explorer.ShowHiddenItems;
        merged.Explorer.DirectoriesFirst = configuration.Explorer.DirectoriesFirst;
        merged.WorkspaceLayout = WorkspaceLayoutService.Normalize(
            CreatePortableLayout(configuration.WorkspaceLayout),
            workspacePanelRegistry);

        merged.TerminalConfigurationProfiles =
            new ObservableCollection<TerminalConfigurationProfile>(
                configuration.TerminalConfigurations.Select(imported =>
                    MergeTerminalConfiguration(
                        imported,
                        currentSettings.TerminalConfigurationProfiles)));
        ApplyTerminalConfigurationsToProfiles(merged);

        if (currentSettings.TerminalProfiles.Any(profile => string.Equals(
                profile.Id,
                configuration.ActiveTerminalProfileId,
                StringComparison.OrdinalIgnoreCase)))
        {
            merged.ActiveTerminalProfileId =
                configuration.ActiveTerminalProfileId;
        }

        merged.Environments = new ObservableCollection<EnvironmentProfile>(
            configuration.Environments.Select(ToRuntime));
        merged.ActiveEnvironmentId = merged.Environments.Any(environment =>
                string.Equals(
                    environment.Id,
                    configuration.ActiveEnvironmentId,
                    StringComparison.OrdinalIgnoreCase))
            ? configuration.ActiveEnvironmentId
            : merged.Environments.FirstOrDefault()?.Id ?? "powershell";
        return merged;
    }

    public string ComputeProfileSha256(PortableProfileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            CompactJsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static ProfileImportPreview CreatePreview(
        string sourcePath,
        long bundleBytes,
        PortableProfileEnvelope? envelope,
        IReadOnlyList<ProfileImportDiagnostic> diagnostics) =>
        new()
        {
            SourcePath = sourcePath,
            BundleBytes = bundleBytes,
            Envelope = envelope,
            Diagnostics = diagnostics
                .OrderByDescending(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToList()
        };

    private static JsonSerializerOptions CreateJsonOptions(bool writeIndented) =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented,
            MaxDepth = MaximumJsonDepth,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters =
            {
                new JsonStringEnumConverter(
                    namingPolicy: null,
                    allowIntegerValues: false)
            }
        };

    private static void ValidateRequiredJsonMembers(
        JsonElement root,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            AddError(
                diagnostics,
                "PROFILE_ROOT",
                "The bundle root must be a JSON object.");
            return;
        }

        foreach (var property in new[]
                 {
                     "format",
                     "bundleVersion",
                     "exportedAtUtc",
                     "profileSha256",
                     "profile"
                 })
        {
            if (!root.TryGetProperty(property, out _))
            {
                AddError(
                    diagnostics,
                    "PROFILE_REQUIRED_MEMBER",
                    $"The bundle is missing required member '{property}'.");
            }
        }

        if (!root.TryGetProperty("profile", out var profile)
            || profile.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in new[]
                 {
                     "schemaVersion",
                     "id",
                     "displayName",
                     "configuration"
                 })
        {
            if (!profile.TryGetProperty(property, out _))
            {
                AddError(
                    diagnostics,
                    "PROFILE_REQUIRED_MEMBER",
                $"The profile is missing required member '{property}'.");
            }
        }

        if (profile.TryGetProperty("configuration", out var configuration)
            && configuration.ValueKind == JsonValueKind.Object
            && configuration.TryGetProperty("workspaceLayout", out var layout))
        {
            ValidateRequiredLayoutMembers(layout, diagnostics);
        }

        if (profile.TryGetProperty("workspaces", out var workspaces)
            && workspaces.ValueKind == JsonValueKind.Array)
        {
            foreach (var workspace in workspaces.EnumerateArray())
            {
                if (workspace.ValueKind == JsonValueKind.Object
                    && workspace.TryGetProperty("layout", out var workspaceLayout))
                {
                    ValidateRequiredLayoutMembers(
                        workspaceLayout,
                        diagnostics);
                }
            }
        }
    }

    private static void ValidateRequiredLayoutMembers(
        JsonElement layout,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (layout.ValueKind != JsonValueKind.Object)
        {
            AddError(
                diagnostics,
                "PROFILE_REQUIRED_MEMBER",
                "A workspace layout must be a JSON object.");
            return;
        }

        foreach (var property in new[] { "isUserUnlocked", "panels" })
        {
            if (!layout.TryGetProperty(property, out _))
            {
                AddError(
                    diagnostics,
                    "PROFILE_REQUIRED_MEMBER",
                    $"A workspace layout is missing required member '{property}'.");
            }
        }

        if (!layout.TryGetProperty("panels", out var panels)
            || panels.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var panel in panels.EnumerateArray())
        {
            if (panel.ValueKind != JsonValueKind.Object)
            {
                AddError(
                    diagnostics,
                    "PROFILE_REQUIRED_MEMBER",
                    "Each workspace panel must be a JSON object.");
                continue;
            }

            foreach (var property in new[]
                     {
                         "extensionId",
                         "panelId",
                         "slot",
                         "replaces",
                         "order",
                         "width",
                         "height",
                         "isVisible"
                     })
            {
                if (!panel.TryGetProperty(property, out _))
                {
                    AddError(
                        diagnostics,
                        "PROFILE_REQUIRED_MEMBER",
                        $"A workspace panel is missing required member '{property}'.");
                }
            }
        }
    }

    private void ValidateEnvelope(
        PortableProfileEnvelope envelope,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (!string.Equals(
                envelope.Format,
                ProfileContract.BundleFormat,
                StringComparison.Ordinal))
        {
            AddError(
                diagnostics,
                "PROFILE_FORMAT",
                $"Expected format '{ProfileContract.BundleFormat}'.");
        }

        if (envelope.BundleVersion != ProfileContract.CurrentBundleVersion)
        {
            AddError(
                diagnostics,
                "PROFILE_BUNDLE_VERSION",
                $"Bundle version {envelope.BundleVersion} is not supported by this build.");
        }

        if (!Sha256Pattern.IsMatch(envelope.ProfileSha256))
        {
            AddError(
                diagnostics,
                "PROFILE_HASH_FORMAT",
                "The profile integrity hash is missing or malformed.");
        }
        else
        {
            var expectedHash = ComputeProfileSha256(envelope.Profile);
            if (!string.Equals(
                    expectedHash,
                    envelope.ProfileSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    diagnostics,
                    "PROFILE_HASH_MISMATCH",
                    "The portable profile content does not match its SHA-256 integrity hash.");
            }
        }
    }

    private static void ValidateProfile(
        PortableProfileSnapshot profile,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (profile.SchemaVersion != ProfileContract.CurrentPortableSchemaVersion)
        {
            AddError(
                diagnostics,
                "PROFILE_SCHEMA_VERSION",
                $"Profile schema {profile.SchemaVersion} is not supported by this build.");
        }

        ValidateId(profile.Id, "profile", diagnostics);
        ValidateText(profile.DisplayName, "profile display name", 120, diagnostics);
        if (profile.Revision < 0)
        {
            AddError(
                diagnostics,
                "PROFILE_REVISION",
                "Profile revision cannot be negative.");
        }

        if (profile.WorkspacePreferences.ExtensionSettings.Count > 0)
        {
            AddError(
                diagnostics,
                "PROFILE_EXTENSION_SETTINGS_RESERVED",
                "Extension-specific settings are reserved in schema v1 and must be empty.");
        }

        if (profile.Extensions.Count > MaximumReferences
            || profile.Libraries.Count > MaximumReferences
            || profile.Workspaces.Count > MaximumProfiles)
        {
            AddError(
                diagnostics,
                "PROFILE_REFERENCE_LIMIT",
                "The profile contains more extension, library, or workspace references than schema v1 permits.");
        }

        ValidateUnique(
            profile.Extensions,
            reference => reference.ExtensionId,
            "extension reference",
            diagnostics);
        foreach (var extension in profile.Extensions)
        {
            ValidateId(extension.ExtensionId, "extension", diagnostics);
            ValidateText(extension.VersionRange, "extension version range", 80, diagnostics);
        }

        ValidateUnique(
            profile.Libraries,
            reference => $"{reference.ExtensionId}/{reference.LibraryId}",
            "library reference",
            diagnostics);
        foreach (var library in profile.Libraries)
        {
            ValidateId(library.ExtensionId, "library extension", diagnostics);
            ValidateId(library.LibraryId, "library", diagnostics);
            ValidateText(library.VersionRange, "library version range", 80, diagnostics);
        }

        foreach (var workspace in profile.Workspaces)
        {
            ValidateId(workspace.Id, "workspace", diagnostics);
            ValidateText(workspace.DisplayName, "workspace display name", 120, diagnostics);
            ValidateText(workspace.RepositoryId, "workspace repository ID", 240, diagnostics);
            ValidateRelativePath(
                workspace.RepositorySubdirectory,
                "repository subdirectory",
                diagnostics);
            foreach (var documentPath in workspace.OpenDocumentPaths)
            {
                ValidateRelativePath(documentPath, "open document path", diagnostics);
            }
        }

        var configuration = profile.Configuration;
        ValidateId(
            configuration.ActiveTerminalProfileId,
            "active terminal profile",
            diagnostics);
        if (!Enum.IsDefined(configuration.Application.NewTabPlacement))
        {
            AddError(
                diagnostics,
                "PROFILE_NEW_TAB_PLACEMENT",
                "The new-tab placement value is not supported.");
        }

        ValidateText(
            configuration.CommandBuilder.ActiveLanguage,
            "active command language",
            80,
            diagnostics,
            allowEmpty: true);

        if (configuration.TerminalConfigurations.Count > MaximumProfiles
            || configuration.Environments.Count is < 1 or > MaximumProfiles)
        {
            AddError(
                diagnostics,
                "PROFILE_CONFIGURATION_LIMIT",
                "The profile must contain 1-128 command profiles and no more than 128 terminal configurations.");
        }

        ValidateUnique(
            configuration.TerminalConfigurations,
            terminal => terminal.Id,
            "terminal configuration",
            diagnostics);
        foreach (var terminal in configuration.TerminalConfigurations)
        {
            ValidateId(terminal.Id, "terminal configuration", diagnostics);
            ValidateText(terminal.Name, "terminal configuration name", 120, diagnostics);
            ValidateText(terminal.Description, "terminal configuration description", 1000, diagnostics, allowEmpty: true);
            ValidateText(terminal.TabTitleTemplate, "terminal tab title template", 240, diagnostics);
            ValidateColor(terminal.TabColor, "terminal tab color", diagnostics);
            if (terminal.TargetTerminalProfileIds.Count is < 1 or > 64)
            {
                AddError(
                    diagnostics,
                    "PROFILE_TERMINAL_TARGETS",
                    $"Terminal configuration '{terminal.Name}' must target 1-64 shell IDs.");
            }

            foreach (var target in terminal.TargetTerminalProfileIds)
            {
                ValidateId(target, "terminal target", diagnostics);
            }

            ValidateTerminalSettings(terminal.Settings, diagnostics);
        }

        ValidateUnique(
            configuration.Environments,
            environment => environment.Id,
            "command profile",
            diagnostics);
        foreach (var environment in configuration.Environments)
        {
            ValidateId(environment.Id, "command profile", diagnostics);
            ValidateText(environment.Name, "command profile name", 120, diagnostics);
            ValidateText(environment.Description, "command profile description", 1000, diagnostics, allowEmpty: true);
            ValidateText(environment.Language, "command profile language", 80, diagnostics);
            if (environment.LibraryIds.Count > MaximumLibrariesPerEnvironment)
            {
                AddError(
                    diagnostics,
                    "PROFILE_LIBRARY_LIMIT",
                    $"Command profile '{environment.Name}' contains too many library IDs.");
            }

            foreach (var libraryId in environment.LibraryIds)
            {
                ValidateId(libraryId, "command profile library", diagnostics);
            }

            if (!string.IsNullOrWhiteSpace(environment.SourceLibraryId))
            {
                ValidateId(environment.SourceLibraryId, "source library", diagnostics);
            }
        }

        if (!configuration.Environments.Any(environment => string.Equals(
                environment.Id,
                configuration.ActiveEnvironmentId,
                StringComparison.OrdinalIgnoreCase)))
        {
            AddError(
                diagnostics,
                "PROFILE_ACTIVE_COMMAND_PROFILE",
                "The active command profile ID does not identify an included command profile.");
        }

        ValidateLayout(configuration.WorkspaceLayout, diagnostics);
    }

    private static void ValidateTerminalSettings(
        PortableTerminalSettings settings,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        ValidateText(settings.FontFamilyName, "terminal font family", 120, diagnostics);
        if (!double.IsFinite(settings.FontSize) || settings.FontSize is < 8 or > 36)
        {
            AddError(
                diagnostics,
                "PROFILE_FONT_SIZE",
                "Terminal font size must be between 8 and 36.");
        }

        ValidateColor(settings.ForegroundColor, "terminal foreground color", diagnostics);
        ValidateColor(settings.BackgroundColor, "terminal background color", diagnostics);
        ValidateColor(settings.AccentColor, "terminal accent color", diagnostics);
        ValidateColor(settings.ErrorColor, "terminal error color", diagnostics);
        if (!Enum.IsDefined(settings.TextBackdropMode)
            || !double.IsFinite(settings.TextBackdropOpacity)
            || settings.TextBackdropOpacity is < 0 or > 1)
        {
            AddError(
                diagnostics,
                "PROFILE_TEXT_BACKDROP",
                "Terminal text-backdrop mode or opacity is invalid.");
        }
    }

    private static void ValidateLayout(
        WorkspaceLayoutSnapshot layout,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (layout.Panels.Count > MaximumProfiles)
        {
            AddError(
                diagnostics,
                "PROFILE_PANEL_LIMIT",
                "The workspace layout contains more than 128 panels.");
        }

        ValidateUnique(
            layout.Panels,
            panel => panel.PanelId,
            "workspace panel",
            diagnostics);
        foreach (var panel in layout.Panels)
        {
            ValidateId(panel.ExtensionId, "workspace panel owner", diagnostics);
            ValidateId(panel.PanelId, "workspace panel", diagnostics);
            if (!Enum.IsDefined(panel.Slot)
                || panel.Replaces is { } replaced && !Enum.IsDefined(replaced)
                || !double.IsFinite(panel.Width)
                || !double.IsFinite(panel.Height)
                || panel.Width is < 0 or > 4000
                || panel.Height is < 0 or > 4000)
            {
                AddError(
                    diagnostics,
                    "PROFILE_PANEL_PLACEMENT",
                    $"Workspace panel '{panel.PanelId}' has invalid placement or dimensions.");
            }
        }

        if (layout.IsUserUnlocked)
        {
            AddWarning(
                diagnostics,
                "PROFILE_LAYOUT_LOCKED_ON_IMPORT",
                "Workspace edit mode will be locked after import.");
        }

        if (layout.SideTools.Count > MaximumProfiles)
        {
            AddError(
                diagnostics,
                "PROFILE_SIDE_TOOL_LIMIT",
                "The workspace layout contains more than 128 side tools.");
        }

        ValidateUnique(
            layout.SideTools,
            tool => tool.ToolId,
            "workspace side tool",
            diagnostics);
        foreach (var tool in layout.SideTools)
        {
            ValidateId(tool.ToolId, "workspace side tool", diagnostics);
            if (tool.Order is < 0 or > 10_000)
            {
                AddError(
                    diagnostics,
                    "PROFILE_SIDE_TOOL_PLACEMENT",
                    $"Workspace side tool '{tool.ToolId}' has an invalid order.");
            }
        }
    }

    private static void AddCompatibilityDiagnostics(
        PortableProfileSnapshot profile,
        ExtensionDiscoverySnapshot extensionCatalog,
        IReadOnlyList<TerminalProfile> terminalProfiles,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        foreach (var reference in profile.Extensions.Where(reference => reference.Enabled))
        {
            var package = extensionCatalog.Packages.FirstOrDefault(candidate =>
                candidate.Manifest is { } manifest
                && string.Equals(
                    manifest.Id,
                    reference.ExtensionId,
                    StringComparison.OrdinalIgnoreCase)
                && VersionMatches(reference.VersionRange, manifest.Version));
            if (package is null)
            {
                AddWarning(
                    diagnostics,
                    "PROFILE_EXTENSION_MISSING",
                    $"Extension '{reference.ExtensionId}' ({reference.VersionRange}) is not installed on this device.");
            }
            else if (package.Status == ExtensionPackageStatus.ApprovalRequired)
            {
                AddWarning(
                    diagnostics,
                    "PROFILE_EXTENSION_APPROVAL_REQUIRED",
                    $"Extension '{reference.ExtensionId}' is installed but still requires a new device-local approval.");
            }
        }

        foreach (var reference in profile.Libraries.Where(reference => reference.Enabled))
        {
            var installed = extensionCatalog.LibraryCandidates.Any(candidate =>
                string.Equals(
                    candidate.Package.Manifest?.Id,
                    reference.ExtensionId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    candidate.Contribution.Id,
                    reference.LibraryId,
                    StringComparison.OrdinalIgnoreCase)
                && VersionMatches(
                    reference.VersionRange,
                    candidate.Package.Manifest?.Version ?? string.Empty));
            if (!installed)
            {
                AddWarning(
                    diagnostics,
                    "PROFILE_LIBRARY_MISSING",
                    $"Hyperlib contribution '{reference.ExtensionId}/{reference.LibraryId}' is not installed on this device.");
            }
        }

        var availableTerminalIds = terminalProfiles
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var targetId in profile.Configuration.TerminalConfigurations
                     .SelectMany(configuration => configuration.TargetTerminalProfileIds)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(targetId => !availableTerminalIds.Contains(targetId)))
        {
            AddWarning(
                diagnostics,
                "PROFILE_TERMINAL_TARGET_MISSING",
                $"Terminal target '{targetId}' is not available on this device; its configuration will remain dormant.");
        }

        if (profile.Workspaces.Count > 0)
        {
            AddWarning(
                diagnostics,
                "PROFILE_WORKSPACE_REFERENCES_DEFERRED",
                "Repository workspace references were validated but are not cloned by the local import stage.");
        }
    }

    private static IReadOnlyList<ProfileExtensionReference> BuildExtensionReferences(
        AppSettings settings,
        ExtensionDiscoverySnapshot extensionCatalog)
    {
        var references = settings.ProfileExtensions
            .Select(Clone)
            .ToDictionary(
                reference => reference.ExtensionId,
                StringComparer.OrdinalIgnoreCase);
        var selectedLibraryIds = settings.Environments
            .SelectMany(environment => environment.LibraryIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedExtensionIds = settings.WorkspaceLayout.Panels
            .Where(panel => !string.Equals(
                panel.ExtensionId,
                WorkspacePanelRegistry.CoreOwnerId,
                StringComparison.OrdinalIgnoreCase))
            .Select(panel => panel.ExtensionId)
            .Concat(extensionCatalog.LibraryCandidates
                .Where(candidate =>
                    selectedLibraryIds.Contains(candidate.QualifiedLibraryId))
                .Select(candidate =>
                    candidate.Package.Manifest?.Id ?? string.Empty))
            .Where(extensionId => !string.IsNullOrWhiteSpace(extensionId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in extensionCatalog.Packages.Where(package =>
                     package.Manifest is not null
                     && referencedExtensionIds.Contains(package.Manifest.Id)
                     && package.Status is ExtensionPackageStatus.Validated
                         or ExtensionPackageStatus.ApprovalRequired))
        {
            var manifest = package.Manifest!;
            references[manifest.Id] = new ProfileExtensionReference
            {
                ExtensionId = manifest.Id,
                VersionRange = manifest.Version,
                Enabled = true,
                ActivateOnStartup = settings.WorkspaceLayout.Panels.Any(panel =>
                    panel.IsVisible
                    && string.Equals(
                        panel.ExtensionId,
                        manifest.Id,
                        StringComparison.OrdinalIgnoreCase))
            };
        }

        return references.Values
            .OrderBy(reference => reference.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ProfileLibraryReference> BuildLibraryReferences(
        AppSettings settings,
        ExtensionDiscoverySnapshot extensionCatalog)
    {
        var references = settings.ProfileLibraries
            .Select(Clone)
            .ToDictionary(
                reference => $"{reference.ExtensionId}/{reference.LibraryId}",
                StringComparer.OrdinalIgnoreCase);
        var selectedLibraryIds = settings.Environments
            .SelectMany(environment => environment.LibraryIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in extensionCatalog.LibraryCandidates.Where(candidate =>
                     selectedLibraryIds.Contains(candidate.QualifiedLibraryId)
                     && candidate.Package.Manifest is not null))
        {
            var manifest = candidate.Package.Manifest!;
            var key = $"{manifest.Id}/{candidate.Contribution.Id}";
            references[key] = new ProfileLibraryReference
            {
                ExtensionId = manifest.Id,
                LibraryId = candidate.Contribution.Id,
                VersionRange = manifest.Version,
                Enabled = true
            };
        }

        return references.Values
            .OrderBy(reference => reference.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.LibraryId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AppSettings CloneSettings(AppSettings settings) =>
        new()
        {
            TypographyVersion = settings.TypographyVersion,
            InputBehaviorVersion = settings.InputBehaviorVersion,
            TerminalProfilesVersion = settings.TerminalProfilesVersion,
            CommandLibraryLayoutVersion = settings.CommandLibraryLayoutVersion,
            PortableProfileId = settings.PortableProfileId,
            PortableProfileDisplayName = settings.PortableProfileDisplayName,
            PortableProfileRevision = settings.PortableProfileRevision,
            PortableProfileUpdatedAtUtc = settings.PortableProfileUpdatedAtUtc,
            ProfileExtensions = new ObservableCollection<ProfileExtensionReference>(
                settings.ProfileExtensions.Select(Clone)),
            ProfileLibraries = new ObservableCollection<ProfileLibraryReference>(
                settings.ProfileLibraries.Select(Clone)),
            Application = settings.Application.Clone(),
            CommandBuilder = settings.CommandBuilder.Clone(),
            Explorer = settings.Explorer.Clone(),
            WorkspaceLayout = settings.WorkspaceLayout.Clone(),
            Terminal = settings.Terminal.Clone(),
            ActiveTerminalProfileId = settings.ActiveTerminalProfileId,
            TerminalProfiles = new ObservableCollection<TerminalProfile>(
                settings.TerminalProfiles.Select(profile => profile.Clone())),
            TerminalConfigurationProfiles =
                new ObservableCollection<TerminalConfigurationProfile>(
                    settings.TerminalConfigurationProfiles.Select(configuration =>
                        configuration.Clone())),
            ActiveEnvironmentId = settings.ActiveEnvironmentId,
            Environments = new ObservableCollection<EnvironmentProfile>(
                settings.Environments.Select(environment => environment.Clone()))
        };

    private static TerminalConfigurationProfile MergeTerminalConfiguration(
        PortableTerminalConfigurationProfile imported,
        IEnumerable<TerminalConfigurationProfile> currentConfigurations)
    {
        var local = currentConfigurations.FirstOrDefault(configuration =>
            string.Equals(
                configuration.Id,
                imported.Id,
                StringComparison.OrdinalIgnoreCase));
        var settings = new TerminalSettings
        {
            BackgroundImagePath = local?.Settings.BackgroundImagePath
                                  ?? string.Empty,
            StartupDirectory = local?.Settings.StartupDirectory
                               ?? string.Empty
        };
        Apply(imported.Settings, settings);
        return new TerminalConfigurationProfile
        {
            Id = imported.Id,
            Name = imported.Name,
            Description = imported.Description,
            TargetTerminalProfileIds = [.. imported.TargetTerminalProfileIds],
            Settings = settings,
            TabTitleTemplate = imported.TabTitleTemplate,
            TabColor = imported.TabColor,
            StartupCommand = local?.StartupCommand ?? string.Empty
        };
    }

    private static void ApplyTerminalConfigurationsToProfiles(
        AppSettings settings)
    {
        foreach (var profile in settings.TerminalProfiles)
        {
            var configuration = settings.TerminalConfigurationProfiles
                .LastOrDefault(candidate =>
                    candidate.TargetTerminalProfileIds.Contains(
                        profile.Id,
                        StringComparer.OrdinalIgnoreCase));
            if (configuration is null)
            {
                continue;
            }

            Apply(ToPortable(configuration.Settings), profile.Settings);
            profile.TabTitleTemplate = configuration.TabTitleTemplate;
            profile.TabColor = configuration.TabColor;
            // StartupCommand and every local path remain untouched.
        }

        settings.Terminal = settings.TerminalProfiles
            .FirstOrDefault(profile => string.Equals(
                profile.Id,
                "powershell",
                StringComparison.OrdinalIgnoreCase))
            ?.Settings
            ?? settings.Terminal;
    }

    private static PortableApplicationPreferences ToPortable(
        ApplicationPreferences source) =>
        new()
        {
            OpenFileExplorerOnStartup = source.OpenFileExplorerOnStartup,
            OpenCommandBuilderOnStartup = source.OpenCommandBuilderOnStartup,
            CreateTerminalOnStartup = source.CreateTerminalOnStartup,
            ShowSaveStatusInTitleBar = source.ShowSaveStatusInTitleBar,
            NewTabPlacement = source.NewTabPlacement
        };

    private static PortableCommandBuilderPreferences ToPortable(
        CommandBuilderPreferences source) =>
        new()
        {
            ActiveLanguage = source.ActiveLanguage,
            OpenCompatibleTerminalAutomatically =
                source.OpenCompatibleTerminalAutomatically,
            OpenBuilderWhenPathsAreInserted =
                source.OpenBuilderWhenPathsAreInserted
        };

    private static PortableExplorerPreferences ToPortable(
        ExplorerSettings source) =>
        new()
        {
            ShowHiddenItems = source.ShowHiddenItems,
            DirectoriesFirst = source.DirectoriesFirst
        };

    private static PortableTerminalConfigurationProfile ToPortable(
        TerminalConfigurationProfile source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            TargetTerminalProfileIds = [.. source.TargetTerminalProfileIds],
            Settings = ToPortable(source.Settings),
            TabTitleTemplate = source.TabTitleTemplate,
            TabColor = source.TabColor
        };

    private static PortableTerminalSettings ToPortable(
        TerminalSettings source) =>
        new()
        {
            FontFamilyName = source.FontFamilyName,
            FontSize = source.FontSize,
            UseBoldText = source.UseBoldText,
            UseItalicText = source.UseItalicText,
            ForegroundColor = source.ForegroundColor,
            BackgroundColor = source.BackgroundColor,
            AccentColor = source.AccentColor,
            ErrorColor = source.ErrorColor,
            ClearOnRun = source.ClearOnRun,
            EnterRunsCommand = source.EnterRunsCommand,
            ShowTimestamps = source.ShowTimestamps,
            TextBackdropMode = source.TextBackdropMode,
            TextBackdropOpacity = source.TextBackdropOpacity
        };

    private static PortableEnvironmentProfile ToPortable(
        EnvironmentProfile source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Language = source.Language,
            LibraryIds = [.. source.LibraryIds],
            SourceLibraryId = source.SourceLibraryId
        };

    private static EnvironmentProfile ToRuntime(
        PortableEnvironmentProfile source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Language = source.Language,
            LibraryIds = [.. source.LibraryIds],
            SourceLibraryId = source.SourceLibraryId
        };

    private static void Apply(
        PortableApplicationPreferences source,
        ApplicationPreferences target)
    {
        target.OpenFileExplorerOnStartup = source.OpenFileExplorerOnStartup;
        target.OpenCommandBuilderOnStartup = source.OpenCommandBuilderOnStartup;
        target.CreateTerminalOnStartup = source.CreateTerminalOnStartup;
        target.ShowSaveStatusInTitleBar = source.ShowSaveStatusInTitleBar;
        target.NewTabPlacement = source.NewTabPlacement;
    }

    private static void Apply(
        PortableCommandBuilderPreferences source,
        CommandBuilderPreferences target)
    {
        if (!string.IsNullOrWhiteSpace(source.ActiveLanguage))
        {
            target.ActiveLanguage =
                CommandLanguageRegistry.NormalizeDisplayName(
                    source.ActiveLanguage);
        }

        target.OpenCompatibleTerminalAutomatically =
            source.OpenCompatibleTerminalAutomatically;
        target.OpenBuilderWhenPathsAreInserted =
            source.OpenBuilderWhenPathsAreInserted;
    }

    private static void Apply(
        PortableTerminalSettings source,
        TerminalSettings target)
    {
        target.FontFamilyName = source.FontFamilyName;
        target.FontSize = source.FontSize;
        target.UseBoldText = source.UseBoldText;
        target.UseItalicText = source.UseItalicText;
        target.ForegroundColor = source.ForegroundColor;
        target.BackgroundColor = source.BackgroundColor;
        target.AccentColor = source.AccentColor;
        target.ErrorColor = source.ErrorColor;
        target.ClearOnRun = source.ClearOnRun;
        target.EnterRunsCommand = source.EnterRunsCommand;
        target.ShowTimestamps = source.ShowTimestamps;
        target.TextBackdropMode = source.TextBackdropMode;
        target.TextBackdropOpacity = source.TextBackdropOpacity;
    }

    private static WorkspaceLayoutSnapshot CreatePortableLayout(
        WorkspaceLayoutSnapshot source) =>
        new()
        {
            IsUserUnlocked = false,
            Panels = source.Panels.Select(panel => panel.Clone()).ToList(),
            SideTools = source.SideTools.Select(tool => tool.Clone()).ToList()
        };

    private static ProfileExtensionReference Clone(
        ProfileExtensionReference reference) =>
        new()
        {
            ExtensionId = reference.ExtensionId,
            VersionRange = reference.VersionRange,
            Enabled = reference.Enabled,
            ActivateOnStartup = reference.ActivateOnStartup
        };

    private static ProfileLibraryReference Clone(
        ProfileLibraryReference reference) =>
        new()
        {
            ExtensionId = reference.ExtensionId,
            LibraryId = reference.LibraryId,
            VersionRange = reference.VersionRange,
            Enabled = reference.Enabled
        };

    private static void ValidateId(
        string value,
        string label,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (!IdPattern.IsMatch(value ?? string.Empty))
        {
            AddError(
                diagnostics,
                "PROFILE_ID",
                $"The {label} ID '{value}' is empty or malformed.");
        }
    }

    private static void ValidateText(
        string value,
        string label,
        int maximumLength,
        ICollection<ProfileImportDiagnostic> diagnostics,
        bool allowEmpty = false)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value?.Length > maximumLength
            || value?.Any(character => char.IsControl(character)
                                       && character is not '\r' and not '\n' and not '\t') == true)
        {
            AddError(
                diagnostics,
                "PROFILE_TEXT",
                $"The {label} is empty, too long, or contains unsupported control characters.");
        }
    }

    private static void ValidateColor(
        string value,
        string label,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (!ColorPattern.IsMatch(value ?? string.Empty))
        {
            AddError(
                diagnostics,
                "PROFILE_COLOR",
                $"The {label} must be #RRGGBB or #RRGGBBAA.");
        }
    }

    private static void ValidateRelativePath(
        string value,
        string label,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathFullyQualified(value)
            || segments.Any(segment => segment == ".."))
        {
            AddError(
                diagnostics,
                "PROFILE_RELATIVE_PATH",
                $"The {label} must be a bounded repository-relative path.");
        }
    }

    private static void ValidateUnique<T>(
        IEnumerable<T> values,
        Func<T, string> identity,
        string label,
        ICollection<ProfileImportDiagnostic> diagnostics)
    {
        var duplicates = values
            .GroupBy(identity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var duplicate in duplicates)
        {
            AddError(
                diagnostics,
                "PROFILE_DUPLICATE_ID",
                $"Duplicate {label} '{duplicate}' is not allowed.");
        }
    }

    private static bool VersionMatches(string requested, string installed) =>
        string.Equals(requested, "*", StringComparison.Ordinal)
        || string.Equals(requested, installed, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeId(string value, string fallback) =>
        !string.IsNullOrEmpty(value) && IdPattern.IsMatch(value)
            ? value.Trim()
            : fallback;

    private static string NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized)
            ? "Default"
            : normalized.Length <= 120
                ? normalized
                : normalized[..120];
    }

    private static string GetApplicationVersion() =>
        typeof(PortableProfileService).Assembly.GetName().Version?.ToString()
        ?? "development";

    private static string FormatBytes(long bytes) =>
        $"{bytes / (1024d * 1024d):0.#} MiB";

    private static void AddError(
        ICollection<ProfileImportDiagnostic> diagnostics,
        string code,
        string message) =>
        diagnostics.Add(new ProfileImportDiagnostic(
            ProfileImportDiagnosticSeverity.Error,
            code,
            message));

    private static void AddWarning(
        ICollection<ProfileImportDiagnostic> diagnostics,
        string code,
        string message) =>
        diagnostics.Add(new ProfileImportDiagnostic(
            ProfileImportDiagnosticSeverity.Warning,
            code,
            message));
}
