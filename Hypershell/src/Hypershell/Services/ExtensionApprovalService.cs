using Hypershell.Extensibility;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class ExtensionDeviceState
{
    public int SchemaVersion { get; set; } = 1;
    public List<ExtensionPackageApproval> PackageApprovals { get; set; } = [];
}

public sealed class ExtensionPackageApproval
{
    public string ExtensionId { get; set; } = string.Empty;
    public string ExtensionVersion { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public IReadOnlyList<ExtensionCapability> ApprovedCapabilities { get; set; } = [];
}

public sealed class ExtensionApprovalService
{
    public const int CurrentSchemaVersion = 1;

    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };
    private ExtensionDeviceState _state;

    public ExtensionApprovalService(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
        _state = LoadState();
    }

    public bool IsWebPanelApproved(DiscoveredExtensionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (_gate)
        {
            return FindMatchingApproval(package) is { } approval
                   && approval.ApprovedCapabilities.Contains(
                       ExtensionCapability.WorkspacePanel);
        }
    }

    public IReadOnlySet<ExtensionCapability> GetApprovedCapabilities(
        DiscoveredExtensionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (_gate)
        {
            var capabilities = FindMatchingApproval(package)
                                   ?.ApprovedCapabilities
                                   ?.ToHashSet()
                               ?? new HashSet<ExtensionCapability>();
            if (package.Manifest?.Contributions.Panels.Any(panel =>
                    panel.MessageApiVersion
                    == ExtensionPanelMessagingContract.CurrentVersion)
                != true)
            {
                capabilities.Remove(ExtensionCapability.WorkspaceMetadata);
            }

            return capabilities;
        }
    }

    public ExtensionPackageApproval ApproveWebPanels(
        DiscoveredExtensionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!CanApproveWebPanels(package, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var manifest = package.Manifest!;
        var enablesPanelMetadata = manifest.Contributions.Panels.Any(panel =>
            panel.MessageApiVersion
            == ExtensionPanelMessagingContract.CurrentVersion);
        var capabilities = manifest.RequestedCapabilities
            .Where(capability =>
                capability == ExtensionCapability.WorkspacePanel
                || (capability == ExtensionCapability.WorkspaceMetadata
                    && enablesPanelMetadata))
            .Distinct()
            .ToList();
        if (!capabilities.Contains(ExtensionCapability.WorkspacePanel))
        {
            capabilities.Insert(0, ExtensionCapability.WorkspacePanel);
        }

        var approval = new ExtensionPackageApproval
        {
            ExtensionId = manifest.Id,
            ExtensionVersion = manifest.Version,
            PackageSha256 = package.PackageSha256,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            ApprovedCapabilities = capabilities
        };

        lock (_gate)
        {
            _state.PackageApprovals.RemoveAll(existing => string.Equals(
                existing.ExtensionId,
                manifest.Id,
                StringComparison.OrdinalIgnoreCase));
            _state.PackageApprovals.Add(approval);
            SaveState();
        }

        return approval;
    }

    public bool Revoke(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        lock (_gate)
        {
            var removed = _state.PackageApprovals.RemoveAll(approval =>
                string.Equals(
                    approval.ExtensionId,
                    extensionId,
                    StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                SaveState();
            }

            return removed;
        }
    }

    public bool CanApproveWebPanels(
        DiscoveredExtensionPackage package,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Status != ExtensionPackageStatus.Validated)
        {
            reason = "The package must pass discovery validation before it can be approved.";
            return false;
        }

        if (package.Manifest is not { } manifest)
        {
            reason = "The package does not have a readable manifest.";
            return false;
        }

        if (manifest.Runtime != ExtensionRuntimeKind.WebView)
        {
            reason = "This activation slice supports isolated WebView panels only.";
            return false;
        }

        if (manifest.Contributions.Panels.Count == 0)
        {
            reason = "The package does not contribute a workspace panel.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(package.PackageSha256))
        {
            reason = "The package could not be bound to a content hash.";
            return false;
        }

        if (manifest.Contributions.Panels.Any(panel =>
            {
                var extension = Path.GetExtension(panel.ViewResource);
                return !string.Equals(
                           extension,
                           ".html",
                           StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(
                           extension,
                           ".htm",
                           StringComparison.OrdinalIgnoreCase);
            }))
        {
            reason = "WebView panel resources must use an .html or .htm entry document.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private ExtensionPackageApproval? FindMatchingApproval(
        DiscoveredExtensionPackage package)
    {
        var manifest = package.Manifest;
        if (manifest is null || string.IsNullOrWhiteSpace(package.PackageSha256))
        {
            return null;
        }

        return _state.PackageApprovals.FirstOrDefault(approval =>
            string.Equals(
                approval.ExtensionId,
                manifest.Id,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                approval.ExtensionVersion,
                manifest.Version,
                StringComparison.Ordinal)
            && string.Equals(
                approval.PackageSha256,
                package.PackageSha256,
                StringComparison.OrdinalIgnoreCase)
            && approval.ApprovedCapabilities?.Contains(
                ExtensionCapability.WorkspacePanel) == true);
    }

    private ExtensionDeviceState LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return new ExtensionDeviceState();
            }

            var state = JsonSerializer.Deserialize<ExtensionDeviceState>(
                            File.ReadAllText(_statePath),
                            _jsonOptions)
                        ?? new ExtensionDeviceState();
            if (state.SchemaVersion != CurrentSchemaVersion)
            {
                return new ExtensionDeviceState();
            }

            state.PackageApprovals ??= [];
            state.PackageApprovals = state.PackageApprovals
                .Where(approval =>
                    !string.IsNullOrWhiteSpace(approval.ExtensionId)
                    && !string.IsNullOrWhiteSpace(approval.ExtensionVersion)
                    && approval.PackageSha256.Length == 64)
                .GroupBy(
                    approval => approval.ExtensionId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            return state;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            return new ExtensionDeviceState();
        }
    }

    private void SaveState()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(_statePath)
            ?? throw new InvalidOperationException(
                "The extension approval path does not have a parent directory."));
        _state.SchemaVersion = CurrentSchemaVersion;
        var temporaryPath = $"{_statePath}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(_state, _jsonOptions));
        File.Move(temporaryPath, _statePath, overwrite: true);
    }
}
