namespace Hypershell.Models;

public enum DocumentEncodingKind
{
    Utf8,
    Utf8WithBom,
    Utf16LittleEndian,
    Utf16BigEndian,
    Utf32LittleEndian,
    Utf32BigEndian
}

public enum DocumentLineEndingKind
{
    Preserve,
    CrLf,
    Lf,
    Cr
}

public enum DocumentEditorViewMode
{
    Edit,
    Split,
    Preview
}

public sealed record DocumentEncodingOption(
    DocumentEncodingKind Kind,
    string DisplayName);

public sealed record DocumentLineEndingOption(
    DocumentLineEndingKind Kind,
    string DisplayName);

public static class DocumentFormatCatalog
{
    public static IReadOnlyList<DocumentEncodingOption> EncodingOptions { get; } =
    [
        new(DocumentEncodingKind.Utf8, "UTF-8"),
        new(DocumentEncodingKind.Utf8WithBom, "UTF-8 with BOM"),
        new(DocumentEncodingKind.Utf16LittleEndian, "UTF-16 LE"),
        new(DocumentEncodingKind.Utf16BigEndian, "UTF-16 BE"),
        new(DocumentEncodingKind.Utf32LittleEndian, "UTF-32 LE"),
        new(DocumentEncodingKind.Utf32BigEndian, "UTF-32 BE")
    ];

    public static IReadOnlyList<DocumentLineEndingOption> LineEndingOptions { get; } =
    [
        new(DocumentLineEndingKind.Preserve, "Preserve mixed"),
        new(DocumentLineEndingKind.CrLf, "CRLF"),
        new(DocumentLineEndingKind.Lf, "LF"),
        new(DocumentLineEndingKind.Cr, "CR")
    ];

    public static IReadOnlyList<DocumentLineEndingOption> NewDocumentLineEndingOptions { get; } =
        LineEndingOptions
            .Where(option => option.Kind != DocumentLineEndingKind.Preserve)
            .ToList();

    public static string GetEncodingDisplayName(DocumentEncodingKind kind) =>
        EncodingOptions.FirstOrDefault(option => option.Kind == kind)?.DisplayName
        ?? "UTF-8";

    public static string GetLineEndingDisplayName(DocumentLineEndingKind kind) =>
        LineEndingOptions.FirstOrDefault(option => option.Kind == kind)?.DisplayName
        ?? "CRLF";
}

public sealed record DocumentEditorSettings
{
    public string LanguageId { get; init; } = "plaintext";
    public int TabSize { get; init; } = 4;
    public bool InsertSpaces { get; init; } = true;
    public bool DetectIndentation { get; init; }
    public bool WordWrap { get; init; }
    public bool MinimapEnabled { get; init; } = true;
    public bool LineNumbersEnabled { get; init; } = true;
    public bool BracketPairGuidesEnabled { get; init; } = true;
    public bool FormatOnType { get; init; } = true;
    public bool FormatOnPaste { get; init; } = true;
    public bool TrimTrailingWhitespaceOnSave { get; init; } = true;
    public bool InsertFinalNewlineOnSave { get; init; } = true;
    public string FontFamily { get; init; } = "JetBrains Mono";
    public int FontSize { get; init; } = 14;
    public string TabColor { get; init; } = "#1F3B58";
}

public sealed record NewDocumentRequest(
    string FileName,
    DocumentEditorSettings EditorSettings,
    DocumentEncodingKind EncodingKind = DocumentEncodingKind.Utf8,
    DocumentLineEndingKind LineEndingKind = DocumentLineEndingKind.CrLf,
    string InitialContent = "",
    string TemplateId = "blank");

public sealed record DocumentTemplateDefinition(
    string Id,
    string DisplayName,
    string Description,
    string LanguageId,
    string Content,
    bool IsDefault = false);

public sealed record EditorLanguageDefinition(
    string Id,
    string DisplayName,
    string DefaultExtension,
    int DefaultTabSize,
    bool DefaultInsertSpaces,
    bool DefaultWordWrap,
    IReadOnlyList<string> FileExtensions);
