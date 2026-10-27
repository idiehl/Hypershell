using System.Text.Json.Serialization;

namespace Hypershell.Models;

public sealed class ProjectWorkItemDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectWorkItem> Items { get; set; } = [];
}

public sealed class ProjectWorkItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsStarred { get; set; }
    public int Order { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectWorkItemAttachment> Attachments { get; set; } = [];

    public ProjectWorkItem Clone() =>
        new()
        {
            Id = Id,
            Title = Title,
            Description = Description,
            IsStarred = IsStarred,
            Order = Order,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            Attachments = Attachments
                .Select(attachment => attachment.Clone())
                .ToList()
        };
}

public sealed class ProjectWorkItemAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    public ProjectWorkItemAttachment Clone() =>
        new()
        {
            Id = Id,
            FileName = FileName,
            RelativePath = RelativePath,
            SourcePath = SourcePath
        };
}

public sealed record ProjectWorkItemScope(
    string Id,
    string Name,
    string RootDirectory,
    bool IsSavedProject);
