using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages;

public sealed class IndexModel : PageModel
{
    private readonly StorageCapacityService _storage;
    private readonly CloudMetadataStore _metadata;

    public IndexModel(
        StorageCapacityService storage,
        CloudMetadataStore metadata)
    {
        _storage = storage;
        _metadata = metadata;
    }

    public string AccountId { get; private set; } = string.Empty;

    public CloudStorageStatus Storage { get; private set; } = new();

    public string AvailableGiB =>
        (Storage.AvailableBytes / 1024d / 1024d / 1024d).ToString("0.0");

    public IReadOnlyList<AdminSection> Sections { get; } =
    [
        new("downloads", "↓", "Downloads", "Installers, portable builds, releases, and updates."),
        new("client-pcs", "▣", "Client PCs", "Approve and revoke signed-in Hypershell installations."),
        new("linked-hosts", "⌘", "Linked Hosts", "SSH targets, pinned host keys, and credentials."),
        new("profiles", "◎", "Profiles", "Workspace settings, projects, libraries, and conversations."),
        new("sessions", "◷", "Sessions", "Review and revoke active account sessions."),
        new("storage", "▰", "Storage", "Volume usage, reserves, packages, and repository data."),
        new("backups", "↺", "Backups", "Snapshots, encrypted exports, and restore checks.")
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AccountId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? string.Empty;
        var capacity = _storage.Inspect();
        var metadata = await _metadata.InspectAsync(cancellationToken);
        Storage = new CloudStorageStatus
        {
            TotalBytes = capacity.TotalBytes,
            AvailableBytes = capacity.AvailableBytes,
            ReservedBytes = capacity.ReservedBytes,
            CanAcceptWrites = capacity.CanAcceptWrites,
            MetadataSchemaVersion = metadata.SchemaVersion,
            CheckedAtUtc = capacity.CheckedAtUtc
        };
    }

    public sealed record AdminSection(
        string Slug,
        string Icon,
        string Title,
        string Description);
}
