namespace HyperLib.Core;

/// <summary>A local, reviewable publication selection. Unchecked objects are not changed online.</summary>
public sealed class ArchivesDraft
{
    public string Id { get; set; } = "";
    public string PublisherId { get; set; } = "";
    public string RootNodeId { get; set; } = "";
    public string Title { get; set; } = "";
    public long ContentRevision { get; set; }
    public long DraftRevision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<LibraryNode> Nodes { get; set; } = [];
    public List<string> SelectedNodeIds { get; set; } = [];
    public List<string> IncludedNodeIds { get; set; } = [];
    public string ReviewHash { get; set; } = "";
    public bool Stale { get; set; }
    public string SyncMode { get; set; } = "upsert";
}

public sealed class ArchivesManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string RendererContract { get; set; } = "hyperlib-razor-v1";
    public string PublisherId { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string RootNodeId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public long DraftRevision { get; set; }
    public long SourceRevision { get; set; }
    public string ReviewHash { get; set; } = "";
    public string SyncMode { get; set; } = "upsert";
    public List<string> SelectedNodeIds { get; set; } = [];
    public List<string> AncestorNodeIds { get; set; } = [];
    public List<string> WorkIds { get; set; } = [];
    public string ContentSha256 { get; set; } = "";
    public List<ArchivesFileEntry> Files { get; set; } = [];
}

public sealed class ArchivesFileEntry
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed record ArchivesPackageResult(string DraftId, string PackagePath,
    string ManifestSha256, string PackageSha256, ArchivesManifest Manifest);

public sealed class ArchivesDraftConflictException(string message) : InvalidOperationException(message);
