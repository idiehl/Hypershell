using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyperLib.Core;

public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 64
    };
    public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Options), Options)!;
}

public sealed class LibraryState
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public string LibraryId { get; set; } = "hyperlib";
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.Ordinal)
    {
        ["library"] = "Library", ["collection"] = "Collection", ["series"] = "Series", ["work"] = "Work",
        ["volume"] = "Volume", ["part"] = "Part", ["chapter"] = "Chapter", ["page"] = "Page"
    };
    public List<LibraryNode> Nodes { get; set; } = [];
    public Dictionary<string, PageDocument> Pages { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonElement> Appearance { get; set; } = new(StringComparer.Ordinal);
}

public sealed class LibraryNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string? Cover { get; set; }
    public double Order { get; set; }
    public string? WorkId { get; set; }
    public string? ProjectPath { get; set; }
    /// <summary>The reusable starter chosen when this Work was created; its source template remains independently editable.</summary>
    public string? StarterId { get; set; }
    public string? DirectoryPath { get; set; }
    public Dictionary<string, JsonElement> Appearance { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PageDocument
{
    public string Id { get; set; } = "";
    public string WorkId { get; set; } = "";
    public string Title { get; set; } = "";
    public string TemplateId { get; set; } = "standard";
    // Omit false so pre-upgrade journal checksums remain byte-compatible on deserialization.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesEditableChrome { get; set; }
    public List<PageBlock> Blocks { get; set; } = [];
    public List<SourceCitation> Sources { get; set; } = [];
    public long Revision { get; set; }
}

public sealed class PageBlock
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "text";
    public string Region { get; set; } = "main";
    public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SourceCitation
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset RetrievedAt { get; set; }
    public string? Provider { get; set; }
    public string? Excerpt { get; set; }
    public bool Verified { get; set; }
    public List<string> BlockIds { get; set; } = [];
    public int? Start { get; set; }
    public int? End { get; set; }
}

public sealed class ContentOperation
{
    public string Kind { get; set; } = "";
    public string? Id { get; set; }
    public string? ParentId { get; set; }
    public string? NodeType { get; set; }
    public string? StarterId { get; set; }
    public string? Title { get; set; }
    public string? Cover { get; set; }
    public double? Order { get; set; }
    public PageDocument? Page { get; set; }
    public Dictionary<string, JsonElement>? Appearance { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}

public sealed class OperationBatch
{
    public long ExpectedRevision { get; set; } = -1;
    public string Description { get; set; } = "Edit content";
    public List<ContentOperation> Operations { get; set; } = [];
}

public sealed class MediaAsset
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>A readable, non-authoritative content snapshot beside a Work's source project.</summary>
public sealed class WorkContentSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public bool Authoritative { get; set; }
    public string LibraryId { get; set; } = "";
    public long LibraryRevision { get; set; }
    public string WorkId { get; set; } = "";
    public string LibraryRootRelativeToProject { get; set; } = "../..";
    public string CanonicalContentRelativeToProject { get; set; } = "../../Content/library.json";
    public string MediaRootRelativeToProject { get; set; } = "../../Media";
    public List<LibraryNode> Nodes { get; set; } = [];
    public Dictionary<string, PageDocument> Pages { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ContentPaths { get; set; } = new(StringComparer.Ordinal);
    public List<WorkMediaReference> ReferencedMedia { get; set; } = [];
}

public sealed class WorkMediaReference
{
    public string Url { get; set; } = "";
    public string LibraryRelativePath { get; set; } = "";
    public string WorkRelativePath { get; set; } = "";
}

public sealed class TemplateDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string TemplateId { get; set; } = "standard";
    public string Title { get; set; } = "Standard";
    public List<TemplateRegion> Regions { get; set; } = [];
    public List<BlockDefinition> Blocks { get; set; } = [];
    public List<FieldDefinition> Appearance { get; set; } = [];
    /// <summary>Ordered, independently cloned content for Add Page. Empty keeps the legacy first-block default.</summary>
    public List<PageBlock> DefaultBlocks { get; set; } = [];
    /// <summary>Keep new pages empty when no default blocks are declared, instead of the legacy first-block fallback.</summary>
    public bool EmptyPageDefault { get; set; }
}

public sealed class BaseTemplateInfo
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string DemoTitle { get; init; } = "";
    public bool IsOriginal { get; init; }
    public string Background { get; init; } = "";
    public string Accent { get; init; } = "";
    public string Ink { get; init; } = "";
}

public sealed class TemplateRegion
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> AllowedBlocks { get; set; } = [];
}

public sealed class BlockDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Component { get; set; } = "";
    public string Kind { get; set; } = "razor";
    public List<FieldDefinition> Fields { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CatalogId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceTemplateId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLibrary { get; set; }
}

public sealed class FieldDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public JsonElement? DefaultValue { get; set; }
    public List<string> Options { get; set; } = [];
    public double? Min { get; set; }
    public double? Max { get; set; }
}

public class ContentValidationException(string message) : Exception(message);
public sealed class ConflictException(long expectedRevision, long actualRevision)
    : Exception($"Content changed since revision {expectedRevision}. Reload revision {actualRevision} before applying this edit.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}
public sealed class ContentRecoveryException(string message, Exception? inner = null) : IOException(message, inner);
