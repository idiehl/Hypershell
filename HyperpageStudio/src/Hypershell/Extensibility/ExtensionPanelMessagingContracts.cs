namespace Hypershell.Extensibility;

public static class ExtensionPanelMessagingContract
{
    public const string Protocol = "hypershell.panel";
    public const int CurrentVersion = 1;
    public const int MaximumInboundMessageBytes = 64 * 1024;
    public const int MaximumStateBytes = 48 * 1024;
    public const int MaximumRequestsPerMinute = 120;
    public const string ContextChangedEvent = "host.contextChanged";

    public static string CreateVirtualHostName(string packageSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageSha256);
        if (packageSha256.Length != 64
            || packageSha256.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "A panel virtual host requires a complete SHA-256 package hash.",
                nameof(packageSha256));
        }

        var prefix = packageSha256[..20];
        return $"ext-{prefix.ToLowerInvariant()}.hypershell.local";
    }
}

public sealed record ExtensionPanelActiveProjectContext(
    string Id,
    string Name);

public sealed record ExtensionPanelActiveTabContext(
    string Kind,
    string Title,
    bool HasUnsavedChanges,
    string LanguageId,
    string ShellKind);

public sealed record ExtensionPanelWorkspaceContext(
    ExtensionPanelActiveProjectContext? ActiveProject,
    int OpenTabCount,
    ExtensionPanelActiveTabContext? ActiveTab);

public sealed record ExtensionPanelThemeContext(
    string ColorScheme,
    string Background,
    string Panel,
    string Foreground,
    string MutedForeground,
    string Accent);

public sealed record ExtensionPanelHostContext(
    int SchemaVersion,
    ExtensionPanelWorkspaceContext Workspace,
    ExtensionPanelThemeContext Theme);
