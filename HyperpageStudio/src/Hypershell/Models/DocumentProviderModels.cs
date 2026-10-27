namespace Hypershell.Models;

[Flags]
public enum DocumentProviderOperation
{
    None = 0,
    Read = 1,
    Replace = 2,
    Insert = 4,
    Export = 8
}

public enum DocumentProviderDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public enum DocumentExportOutcome
{
    Exported,
    Unavailable,
    Rejected,
    Failed,
    Cancelled
}

public sealed record DocumentProviderDiagnostic(
    DocumentProviderDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? Offset = null,
    int? Length = null);

public sealed record DocumentOutlineItem(
    string Id,
    string DisplayName,
    string Kind,
    int Level,
    int? Offset = null,
    int? Length = null);

public sealed record DocumentExportFormat(
    string Id,
    string DisplayName,
    string FileExtension,
    string MediaType,
    bool IsBinary);

public static class DocumentExportFormatCatalog
{
    public static DocumentExportFormat Html { get; } = new(
        "html",
        "Standalone HTML document",
        ".html",
        "text/html",
        false);

    public static DocumentExportFormat WordDocument { get; } = new(
        "docx",
        "Microsoft Word document",
        ".docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        true);

    public static IReadOnlyList<DocumentExportFormat> StandardDocumentExports { get; } =
        [Html, WordDocument];
}

public sealed record DocumentProviderDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required DocumentProviderOperation Operations { get; init; }
    public IReadOnlyList<string> LanguageIds { get; init; } = [];
    public IReadOnlyList<string> FileExtensions { get; init; } = [];
    public IReadOnlyList<DocumentExportFormat> ExportFormats { get; init; } = [];
    public string Description { get; init; } = string.Empty;
}

public sealed record DocumentReadRequest(
    string Content,
    string LanguageId,
    string FilePath = "");

public sealed record DocumentReadResult
{
    public required bool Succeeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<DocumentOutlineItem> Outline { get; init; } = [];
    public IReadOnlyList<DocumentProviderDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record DocumentReplaceRequest(
    string Content,
    int Offset,
    int Length,
    string Replacement,
    string LanguageId,
    string FilePath = "");

public sealed record DocumentInsertRequest(
    string Content,
    int Offset,
    string Text,
    string LanguageId,
    string FilePath = "");

public sealed record DocumentMutationResult
{
    public required bool Succeeded { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<DocumentProviderDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record DocumentExportRequest
{
    public required string Content { get; init; }
    public required string LanguageId { get; init; }
    public required string Title { get; init; }
    public required string FormatId { get; init; }
    public string FilePath { get; init; } = string.Empty;
}

public sealed record DocumentExportArtifact
{
    public required DocumentExportFormat Format { get; init; }
    public required byte[] Content { get; init; }
    public string SuggestedFileName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record DocumentExportResult
{
    public required DocumentExportOutcome Outcome { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string FormatId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool Succeeded => Outcome == DocumentExportOutcome.Exported;
}

public sealed record DocumentProviderRegistration
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ProviderId { get; init; }
    public required string LanguageId { get; init; }
    public IReadOnlyList<string> FileExtensions { get; init; } = [];
    public bool IsBuiltIn { get; init; }
    public bool IsActive { get; init; }
    public string OwnerId { get; init; } = "core";
    public string StatusMessage { get; init; } = string.Empty;
}

public sealed record ResolvedDocumentProvider
{
    public required IDocumentProvider Provider { get; init; }
    public required DocumentProviderRegistration Registration { get; init; }
    public required string LanguageId { get; init; }
}

public sealed record DocumentProviderCatalogSnapshot
{
    public DateTimeOffset RefreshedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<DocumentProviderRegistration> Registrations { get; init; } = [];

    public int ActiveBuiltInCount => Registrations.Count(registration =>
        registration.IsBuiltIn && registration.IsActive);
    public int ActiveExtensionCount => Registrations.Count(registration =>
        !registration.IsBuiltIn && registration.IsActive);
    public int DormantExtensionCount => Registrations.Count(registration =>
        !registration.IsBuiltIn && !registration.IsActive);
}

public interface IDocumentProvider
{
    DocumentProviderDefinition Definition { get; }

    DocumentReadResult Read(DocumentReadRequest request);
    DocumentMutationResult Replace(DocumentReplaceRequest request);
    DocumentMutationResult Insert(DocumentInsertRequest request);
    Task<DocumentExportArtifact> ExportAsync(
        DocumentExportRequest request,
        CancellationToken cancellationToken = default);
}
