namespace Hypershell.Models;

public sealed record EditorSessionSnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ActiveDocumentSessionId { get; init; } = string.Empty;
    public IReadOnlyList<EditorDocumentSessionEntry> Documents { get; init; } = [];
}

public sealed record EditorDocumentSessionEntry
{
    public string SessionId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string WorkspaceUri { get; init; } = string.Empty;
    public string WorkspaceVersion { get; init; } = string.Empty;
    public bool HadUnsavedChanges { get; init; }
    public DocumentEditorSettings EditorSettings { get; init; } = new();
    public string EditorViewState { get; init; } = string.Empty;
    public DocumentEncodingKind EncodingKind { get; init; } =
        DocumentEncodingKind.Utf8;
    public DocumentLineEndingKind LineEndingKind { get; init; } =
        DocumentLineEndingKind.CrLf;
}

public sealed record EditorRecoverySnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string SessionId { get; init; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Title { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string WorkspaceUri { get; init; } = string.Empty;
    public string WorkspaceVersion { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DocumentEditorSettings EditorSettings { get; init; } = new();
    public string EditorViewState { get; init; } = string.Empty;
    public DocumentEncodingKind EncodingKind { get; init; } =
        DocumentEncodingKind.Utf8;
    public DocumentLineEndingKind LineEndingKind { get; init; } =
        DocumentLineEndingKind.CrLf;
}

public sealed record EditorSessionSaveResult(
    int RecoveryCount,
    IReadOnlyList<string> SkippedRecoverySessionIds);
