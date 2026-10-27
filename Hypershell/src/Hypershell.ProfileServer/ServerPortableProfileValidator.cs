using Hypershell.Profiles;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public sealed record ValidatedServerProfile(
    string Id,
    string DisplayName,
    long ProfileRevision,
    DateTimeOffset UpdatedAtUtc,
    string ProfileSha256,
    ReadOnlyMemory<byte> Payload);

public sealed class ProfilePayloadValidationException : Exception
{
    public ProfilePayloadValidationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed partial class ServerPortableProfileValidator
{
    private const int MaximumProfiles = 128;
    private const int MaximumReferences = 512;
    private const int MaximumLibrariesPerEnvironment = 256;
    private const int MaximumDocumentsPerWorkspace = 256;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(
        "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    [GeneratedRegex(
        "^[0-9A-Fa-f]{64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    public ValidatedServerProfile Validate(
        ReadOnlyMemory<byte> payload,
        string expectedProfileId)
    {
        var expectedId = ProfileServerIdentity.ValidateProfileId(
            expectedProfileId);
        if (payload.Length is <= 0
            || payload.Length > ProfileContract.MaximumBundleBytes)
        {
            throw new ProfilePayloadValidationException(
                $"A profile bundle must contain 1-{ProfileContract.MaximumBundleBytes:N0} bytes.");
        }

        ServerPortableProfileEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ServerPortableProfileEnvelope>(
                           payload.Span,
                           JsonOptions)
                       ?? throw new JsonException(
                           "The profile envelope was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProfilePayloadValidationException(
                $"The profile bundle is not strict protocol JSON: {exception.Message}",
                exception);
        }

        ValidateEnvelope(envelope, expectedId);
        return new ValidatedServerProfile(
            envelope.Profile.Id,
            envelope.Profile.DisplayName,
            envelope.Profile.Revision,
            envelope.Profile.UpdatedAtUtc,
            envelope.ProfileSha256.ToLowerInvariant(),
            payload.ToArray());
    }

    private static void ValidateEnvelope(
        ServerPortableProfileEnvelope envelope,
        string expectedProfileId)
    {
        if (envelope.Schema
            != "https://hypershell.dev/schemas/profile-bundle-v1.json"
            || envelope.Format != ProfileContract.BundleFormat
            || envelope.BundleVersion != ProfileContract.CurrentBundleVersion
            || envelope.ExportedAtUtc == default)
        {
            throw Invalid(
                "The profile envelope format, version, schema, or export time is unsupported.");
        }

        ValidateText(envelope.ExportedBy, "exportedBy", 200);
        if (!Sha256Pattern().IsMatch(envelope.ProfileSha256))
        {
            throw Invalid("The profile SHA-256 is missing or malformed.");
        }

        ValidateProfile(envelope.Profile, expectedProfileId);
        var canonicalProfile = JsonSerializer.SerializeToUtf8Bytes(
            envelope.Profile,
            JsonOptions);
        var expectedHash = Convert.ToHexString(
                SHA256.HashData(canonicalProfile))
            .ToLowerInvariant();
        if (!string.Equals(
                expectedHash,
                envelope.ProfileSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "The enclosed profile does not match its declared SHA-256.");
        }
    }

    private static void ValidateProfile(
        ServerPortableProfile profile,
        string expectedProfileId)
    {
        if (profile.SchemaVersion
            != ProfileContract.CurrentPortableSchemaVersion
            || profile.Revision < 0
            || profile.UpdatedAtUtc == default)
        {
            throw Invalid(
                "The portable profile schema, revision, or update time is invalid.");
        }

        ValidateId(profile.Id, "profile");
        if (!string.Equals(
                profile.Id,
                expectedProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "The URL profile ID does not match the enclosed profile ID.");
        }

        ValidateText(profile.DisplayName, "displayName", 120);
        ValidateText(
            profile.WorkspacePreferences.ThemeId,
            "workspace theme",
            120);
        if (profile.WorkspacePreferences.ExtensionSettings.Count != 0)
        {
            throw Invalid(
                "Extension settings are reserved and must be empty in schema v1.");
        }

        if (profile.Extensions.Count > MaximumReferences
            || profile.Libraries.Count > MaximumReferences
            || profile.Workspaces.Count > MaximumProfiles)
        {
            throw Invalid(
                "The profile exceeds its extension, library, or workspace count limit.");
        }

        ValidateUnique(
            profile.Extensions,
            item => item.ExtensionId,
            "extension reference");
        foreach (var extension in profile.Extensions)
        {
            ValidateId(extension.ExtensionId, "extension");
            ValidateText(extension.VersionRange, "version range", 80);
        }

        ValidateUnique(
            profile.Libraries,
            item => $"{item.ExtensionId}/{item.LibraryId}",
            "library reference");
        foreach (var library in profile.Libraries)
        {
            ValidateId(library.ExtensionId, "library extension");
            ValidateId(library.LibraryId, "library");
            ValidateText(library.VersionRange, "version range", 80);
        }

        ValidateConfiguration(profile.Configuration);
        ValidateUnique(profile.Workspaces, item => item.Id, "workspace");
        foreach (var workspace in profile.Workspaces)
        {
            ValidateId(workspace.Id, "workspace");
            ValidateText(workspace.DisplayName, "workspace name", 120);
            ValidateText(workspace.RepositoryId, "repository ID", 240);
            ValidateRelativePath(
                workspace.RepositorySubdirectory,
                "repository subdirectory");
            if (workspace.OpenDocumentPaths.Count > MaximumDocumentsPerWorkspace)
            {
                throw Invalid(
                    "A workspace contains more than 256 open-document paths.");
            }

            ValidateUnique(
                workspace.OpenDocumentPaths,
                path => path,
                "open-document path");
            foreach (var documentPath in workspace.OpenDocumentPaths)
            {
                ValidateRelativePath(documentPath, "open-document path");
            }

            ValidateLayout(workspace.Layout);
        }
    }

    private static void ValidateConfiguration(
        ServerPortableConfiguration configuration)
    {
        ValidateId(
            configuration.ActiveTerminalProfileId,
            "active terminal profile");
        ValidateText(
            configuration.CommandBuilder.ActiveLanguage,
            "active command language",
            80,
            allowEmpty: true);
        if (!Enum.IsDefined(configuration.Application.NewTabPlacement)
            || configuration.TerminalConfigurations.Count > MaximumProfiles
            || configuration.Environments.Count is < 1 or > MaximumProfiles)
        {
            throw Invalid(
                "The application mode or profile collection count is invalid.");
        }

        ValidateUnique(
            configuration.TerminalConfigurations,
            item => item.Id,
            "terminal configuration");
        foreach (var terminal in configuration.TerminalConfigurations)
        {
            ValidateId(terminal.Id, "terminal configuration");
            ValidateText(terminal.Name, "terminal configuration name", 120);
            ValidateText(
                terminal.Description,
                "terminal configuration description",
                1000,
                allowEmpty: true);
            ValidateText(
                terminal.TabTitleTemplate,
                "terminal title template",
                240);
            ValidateColor(terminal.TabColor, "terminal tab color");
            if (terminal.TargetTerminalProfileIds.Count is < 1 or > 64)
            {
                throw Invalid(
                    "A terminal configuration must target 1-64 shell IDs.");
            }

            ValidateUnique(
                terminal.TargetTerminalProfileIds,
                id => id,
                "terminal target");
            foreach (var target in terminal.TargetTerminalProfileIds)
            {
                ValidateId(target, "terminal target");
            }

            ValidateTerminalSettings(terminal.Settings);
        }

        ValidateUnique(
            configuration.Environments,
            item => item.Id,
            "command profile");
        foreach (var environment in configuration.Environments)
        {
            ValidateId(environment.Id, "command profile");
            ValidateText(environment.Name, "command profile name", 120);
            ValidateText(
                environment.Description,
                "command profile description",
                1000,
                allowEmpty: true);
            ValidateText(environment.Language, "command profile language", 80);
            if (environment.LibraryIds.Count > MaximumLibrariesPerEnvironment)
            {
                throw Invalid(
                    "A command profile contains more than 256 library IDs.");
            }

            ValidateUnique(
                environment.LibraryIds,
                id => id,
                "command-profile library");
            foreach (var libraryId in environment.LibraryIds)
            {
                ValidateId(libraryId, "command-profile library");
            }

            if (!string.IsNullOrWhiteSpace(environment.SourceLibraryId))
            {
                ValidateId(environment.SourceLibraryId, "source library");
            }
        }

        if (!configuration.Environments.Any(environment => string.Equals(
                environment.Id,
                configuration.ActiveEnvironmentId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid(
                "The active command profile is not present in the bundle.");
        }

        ValidateLayout(configuration.WorkspaceLayout);
    }

    private static void ValidateTerminalSettings(
        ServerPortableTerminalSettings settings)
    {
        ValidateText(settings.FontFamilyName, "terminal font family", 120);
        if (!double.IsFinite(settings.FontSize)
            || settings.FontSize is < 8 or > 36
            || !Enum.IsDefined(settings.TextBackdropMode)
            || !double.IsFinite(settings.TextBackdropOpacity)
            || settings.TextBackdropOpacity is < 0 or > 1)
        {
            throw Invalid(
                "Terminal font or backdrop settings are out of range.");
        }

        ValidateColor(settings.ForegroundColor, "terminal foreground");
        ValidateColor(settings.BackgroundColor, "terminal background");
        ValidateColor(settings.AccentColor, "terminal accent");
        ValidateColor(settings.ErrorColor, "terminal error");
    }

    private static void ValidateLayout(ServerWorkspaceLayout layout)
    {
        if (layout.Panels.Count > MaximumProfiles)
        {
            throw Invalid(
                "A workspace layout contains more than 128 panels.");
        }

        ValidateUnique(layout.Panels, panel => panel.PanelId, "workspace panel");
        foreach (var panel in layout.Panels)
        {
            ValidateId(panel.ExtensionId, "workspace panel owner");
            ValidateId(panel.PanelId, "workspace panel");
            if (!Enum.IsDefined(panel.Slot)
                || panel.Replaces is { } replaced && !Enum.IsDefined(replaced)
                || panel.Order is < 0 or > 10_000
                || !double.IsFinite(panel.Width)
                || !double.IsFinite(panel.Height)
                || panel.Width is < 0 or > 4000
                || panel.Height is < 0 or > 4000)
            {
                throw Invalid(
                    $"Panel '{panel.PanelId}' has invalid placement metadata.");
            }
        }

        if (layout.SideTools.Count > MaximumProfiles)
        {
            throw Invalid(
                "A workspace layout contains more than 128 side tools.");
        }

        ValidateUnique(
            layout.SideTools,
            tool => tool.ToolId,
            "workspace side tool");
        foreach (var tool in layout.SideTools)
        {
            ValidateId(tool.ToolId, "workspace side tool");
            if (tool.Order is < 0 or > 10_000)
            {
                throw Invalid(
                    $"Side tool '{tool.ToolId}' has invalid placement metadata.");
            }
        }
    }

    private static void ValidateId(string value, string label)
    {
        if (!IdPattern().IsMatch(value ?? string.Empty))
        {
            throw Invalid($"The {label} ID is empty or malformed.");
        }
    }

    private static void ValidateText(
        string value,
        string label,
        int maximumLength,
        bool allowEmpty = false)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value?.Length > maximumLength
            || value?.Any(character => char.IsControl(character)
                                       && character is not '\r' and not '\n' and not '\t') == true)
        {
            throw Invalid(
                $"The {label} is empty, too long, or contains unsupported control characters.");
        }
    }

    private static void ValidateColor(string value, string label)
    {
        if (!ColorPattern().IsMatch(value ?? string.Empty))
        {
            throw Invalid($"The {label} color is malformed.");
        }
    }

    private static void ValidateRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Length > 2048)
        {
            throw Invalid($"The {label} is too long.");
        }

        var segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathFullyQualified(value)
            || segments.Any(segment => segment == ".."))
        {
            throw Invalid($"The {label} must be repository-relative.");
        }
    }

    private static void ValidateUnique<T>(
        IEnumerable<T> values,
        Func<T, string> identity,
        string label)
    {
        if (values.GroupBy(identity, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw Invalid($"Duplicate {label} values are not allowed.");
        }
    }

    private static ProfilePayloadValidationException Invalid(string message) =>
        new(message);
}

internal enum ServerNewTabPlacement
{
    AfterLast,
    AfterCurrent
}

internal enum ServerTextBackdropMode
{
    Everything,
    CurrentInput,
    None
}

internal enum ServerWorkspacePanelSlot
{
    Left,
    Right,
    Bottom,
    Center,
    Floating
}

internal enum ServerBuiltInWorkspaceSurface
{
    FileExplorer,
    CommandBuilder
}

internal sealed class ServerPortableProfileEnvelope
{
    [JsonRequired]
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = string.Empty;

    [JsonRequired]
    public string Format { get; init; } = string.Empty;

    [JsonRequired]
    public int BundleVersion { get; init; }

    [JsonRequired]
    public DateTimeOffset ExportedAtUtc { get; init; }

    [JsonRequired]
    public string ExportedBy { get; init; } = string.Empty;

    [JsonRequired]
    public string ProfileSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public ServerPortableProfile Profile { get; init; } = new();
}

internal sealed class ServerPortableProfile
{
    [JsonRequired]
    public int SchemaVersion { get; init; }

    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public long Revision { get; init; }

    [JsonRequired]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonRequired]
    public ServerWorkspacePreferences WorkspacePreferences { get; init; } = new();

    [JsonRequired]
    public ServerPortableConfiguration Configuration { get; init; } = new();

    [JsonRequired]
    public IReadOnlyList<ServerExtensionReference> Extensions { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<ServerLibraryReference> Libraries { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<ServerWorkspaceReference> Workspaces { get; init; } = [];
}

internal sealed class ServerWorkspacePreferences
{
    [JsonRequired]
    public string ThemeId { get; init; } = string.Empty;

    [JsonRequired]
    public bool RestoreWorkspaceOnStart { get; init; }

    [JsonRequired]
    public bool RestoreOpenDocuments { get; init; }

    [JsonRequired]
    public bool PreferWorkspaceScopedAiAccess { get; init; }

    [JsonRequired]
    public Dictionary<string, JsonElement> ExtensionSettings { get; init; } = [];
}

internal sealed class ServerPortableConfiguration
{
    [JsonRequired]
    public ServerApplicationPreferences Application { get; init; } = new();

    [JsonRequired]
    public ServerCommandBuilderPreferences CommandBuilder { get; init; } = new();

    [JsonRequired]
    public ServerExplorerPreferences Explorer { get; init; } = new();

    [JsonRequired]
    public string ActiveTerminalProfileId { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<ServerTerminalConfiguration> TerminalConfigurations
        { get; init; } = [];

    [JsonRequired]
    public string ActiveEnvironmentId { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<ServerEnvironmentProfile> Environments { get; init; } = [];

    [JsonRequired]
    public ServerWorkspaceLayout WorkspaceLayout { get; init; } = new();
}

internal sealed class ServerApplicationPreferences
{
    [JsonRequired]
    public bool OpenFileExplorerOnStartup { get; init; }

    [JsonRequired]
    public bool OpenCommandBuilderOnStartup { get; init; }

    [JsonRequired]
    public bool CreateTerminalOnStartup { get; init; }

    [JsonRequired]
    public bool ShowSaveStatusInTitleBar { get; init; }

    [JsonRequired]
    public ServerNewTabPlacement NewTabPlacement { get; init; }
}

internal sealed class ServerCommandBuilderPreferences
{
    public string ActiveLanguage { get; init; } = string.Empty;

    [JsonRequired]
    public bool OpenCompatibleTerminalAutomatically { get; init; }

    [JsonRequired]
    public bool OpenBuilderWhenPathsAreInserted { get; init; }
}

internal sealed class ServerExplorerPreferences
{
    [JsonRequired]
    public bool ShowHiddenItems { get; init; }

    [JsonRequired]
    public bool DirectoriesFirst { get; init; }
}

internal sealed class ServerTerminalConfiguration
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public string Description { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<string> TargetTerminalProfileIds { get; init; } = [];

    [JsonRequired]
    public ServerPortableTerminalSettings Settings { get; init; } = new();

    [JsonRequired]
    public string TabTitleTemplate { get; init; } = string.Empty;

    [JsonRequired]
    public string TabColor { get; init; } = string.Empty;
}

internal sealed class ServerPortableTerminalSettings
{
    [JsonRequired]
    public string FontFamilyName { get; init; } = string.Empty;

    [JsonRequired]
    public double FontSize { get; init; }

    [JsonRequired]
    public bool UseBoldText { get; init; }

    [JsonRequired]
    public bool UseItalicText { get; init; }

    [JsonRequired]
    public string ForegroundColor { get; init; } = string.Empty;

    [JsonRequired]
    public string BackgroundColor { get; init; } = string.Empty;

    [JsonRequired]
    public string AccentColor { get; init; } = string.Empty;

    [JsonRequired]
    public string ErrorColor { get; init; } = string.Empty;

    [JsonRequired]
    public bool ClearOnRun { get; init; }

    [JsonRequired]
    public bool EnterRunsCommand { get; init; }

    [JsonRequired]
    public bool ShowTimestamps { get; init; }

    [JsonRequired]
    public ServerTextBackdropMode TextBackdropMode { get; init; }

    [JsonRequired]
    public double TextBackdropOpacity { get; init; }
}

internal sealed class ServerEnvironmentProfile
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public string Description { get; init; } = string.Empty;

    [JsonRequired]
    public string Language { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<string> LibraryIds { get; init; } = [];

    [JsonRequired]
    public string SourceLibraryId { get; init; } = string.Empty;
}

internal sealed class ServerExtensionReference
{
    [JsonRequired]
    public string ExtensionId { get; init; } = string.Empty;

    [JsonRequired]
    public string VersionRange { get; init; } = string.Empty;

    [JsonRequired]
    public bool Enabled { get; init; }

    [JsonRequired]
    public bool ActivateOnStartup { get; init; }
}

internal sealed class ServerLibraryReference
{
    [JsonRequired]
    public string ExtensionId { get; init; } = string.Empty;

    [JsonRequired]
    public string LibraryId { get; init; } = string.Empty;

    [JsonRequired]
    public string VersionRange { get; init; } = string.Empty;

    [JsonRequired]
    public bool Enabled { get; init; }
}

internal sealed class ServerWorkspaceReference
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string RepositoryId { get; init; } = string.Empty;

    [JsonRequired]
    public string RepositorySubdirectory { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<string> OpenDocumentPaths { get; init; } = [];

    [JsonRequired]
    public ServerWorkspaceLayout Layout { get; init; } = new();
}

internal sealed class ServerWorkspaceLayout
{
    [JsonRequired]
    public bool IsUserUnlocked { get; init; }

    [JsonRequired]
    public IReadOnlyList<ServerPanelPlacement> Panels { get; init; } = [];

    public IReadOnlyList<ServerWorkspaceSideToolPlacement> SideTools { get; init; } = [];
}

internal sealed class ServerWorkspaceSideToolPlacement
{
    [JsonRequired]
    public string ToolId { get; init; } = string.Empty;

    [JsonRequired]
    public int Order { get; init; }

    [JsonRequired]
    public bool IsVisible { get; init; }
}

internal sealed class ServerPanelPlacement
{
    [JsonRequired]
    public string ExtensionId { get; init; } = string.Empty;

    [JsonRequired]
    public string PanelId { get; init; } = string.Empty;

    [JsonRequired]
    public ServerWorkspacePanelSlot Slot { get; init; }

    [JsonRequired]
    public ServerBuiltInWorkspaceSurface? Replaces { get; init; }

    [JsonRequired]
    public int Order { get; init; }

    [JsonRequired]
    public double Width { get; init; }

    [JsonRequired]
    public double Height { get; init; }

    [JsonRequired]
    public bool IsVisible { get; init; }
}
