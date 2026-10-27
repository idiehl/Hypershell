using Hypershell.Models;
using System.IO;
using System.Text.Json;

namespace Hypershell.Services;

public abstract class BuiltInDocumentProvider : IDocumentProvider
{
    protected BuiltInDocumentProvider(DocumentProviderDefinition definition)
    {
        Definition = definition;
    }

    public DocumentProviderDefinition Definition { get; }

    public abstract DocumentReadResult Read(DocumentReadRequest request);

    public DocumentMutationResult Replace(DocumentReplaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Offset < 0
            || request.Length < 0
            || request.Offset > request.Content.Length
            || request.Length > request.Content.Length - request.Offset)
        {
            return RejectedMutation(
                request.Content,
                "The requested replacement range is outside the document.");
        }

        var content = request.Content.Remove(request.Offset, request.Length)
            .Insert(request.Offset, request.Replacement ?? string.Empty);
        return ValidateMutation(
            request.Content,
            content,
            request.LanguageId,
            request.FilePath,
            "Content replaced through the typed document provider.");
    }

    public DocumentMutationResult Insert(DocumentInsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Offset < 0 || request.Offset > request.Content.Length)
        {
            return RejectedMutation(
                request.Content,
                "The requested insertion point is outside the document.");
        }

        var content = request.Content.Insert(
            request.Offset,
            request.Text ?? string.Empty);
        return ValidateMutation(
            request.Content,
            content,
            request.LanguageId,
            request.FilePath,
            "Content inserted through the typed document provider.");
    }

    public abstract Task<DocumentExportArtifact> ExportAsync(
        DocumentExportRequest request,
        CancellationToken cancellationToken = default);

    protected virtual DocumentMutationResult ValidateMutation(
        string originalContent,
        string content,
        string languageId,
        string filePath,
        string successMessage) =>
        new()
        {
            Succeeded = true,
            Content = content,
            Message = successMessage
        };

    protected static DocumentMutationResult RejectedMutation(
        string originalContent,
        string message,
        IReadOnlyList<DocumentProviderDiagnostic>? diagnostics = null) =>
        new()
        {
            Succeeded = false,
            Content = originalContent,
            Message = message,
            Diagnostics = diagnostics ?? []
        };

    protected static DocumentExportArtifact Artifact(
        DocumentExportRequest request,
        byte[] content)
    {
        var format = DocumentExportFormatCatalog.StandardDocumentExports
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                request.FormatId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException(
                $"Export format '{request.FormatId}' is not supported.");
        var baseName = Path.GetFileNameWithoutExtension(request.Title);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Document";
        }

        return new DocumentExportArtifact
        {
            Format = format,
            Content = content,
            SuggestedFileName = baseName + format.FileExtension,
            Message = $"Created {format.DisplayName}."
        };
    }
}

public sealed class TextDocumentProvider : BuiltInDocumentProvider
{
    public const string ProviderId = "core.document.text";

    public TextDocumentProvider()
        : base(new DocumentProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Code and text provider",
            Operations = DocumentProviderOperation.Read
                         | DocumentProviderOperation.Replace
                         | DocumentProviderOperation.Insert
                         | DocumentProviderOperation.Export,
            ExportFormats = DocumentExportFormatCatalog.StandardDocumentExports,
            Description =
                "Host-owned text operations with standalone HTML and Word export."
        })
    {
    }

    public override DocumentReadResult Read(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lineCount = request.Content.Length == 0
            ? 0
            : request.Content.Count(character => character == '\n') + 1;
        return new DocumentReadResult
        {
            Succeeded = true,
            Summary = $"{lineCount:N0} line(s) · {request.Content.Length:N0} character(s)"
        };
    }

    public override Task<DocumentExportArtifact> ExportAsync(
        DocumentExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var content = request.FormatId.ToLowerInvariant() switch
        {
            "html" => StandaloneHtmlDocumentBuilder.FromPreformatted(
                request.Title,
                request.Content,
                request.LanguageId),
            "docx" => WordprocessingDocumentExporter.Create(
                request.Title,
                string.Equals(
                    request.LanguageId,
                    "plaintext",
                    StringComparison.OrdinalIgnoreCase)
                    ? StructuredDocumentParser.ParsePlainText(request.Content)
                    : [new StructuredDocumentBlock(
                        StructuredDocumentBlockKind.Code,
                        request.Content)]),
            _ => throw new NotSupportedException(
                $"Export format '{request.FormatId}' is not supported.")
        };
        return Task.FromResult(Artifact(request, content));
    }
}

public sealed class MarkdownDocumentProvider : BuiltInDocumentProvider
{
    public const string ProviderId = "core.document.markdown";

    public MarkdownDocumentProvider()
        : base(new DocumentProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Markdown document provider",
            Operations = DocumentProviderOperation.Read
                         | DocumentProviderOperation.Replace
                         | DocumentProviderOperation.Insert
                         | DocumentProviderOperation.Export,
            LanguageIds = ["markdown"],
            FileExtensions = [".md", ".markdown", ".mdown", ".mkd", ".mdx"],
            ExportFormats = DocumentExportFormatCatalog.StandardDocumentExports,
            Description =
                "Heading-aware Markdown operations with sanitized HTML and structured Word export."
        })
    {
    }

    public override DocumentReadResult Read(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var blocks = StructuredDocumentParser.ParseMarkdown(request.Content);
        var headings = blocks
            .Where(block => block.Kind == StructuredDocumentBlockKind.Heading)
            .Select((block, index) => new DocumentOutlineItem(
                $"heading-{index + 1}",
                block.Text,
                "Heading",
                block.Level,
                block.Offset,
                block.Length))
            .ToList();
        return new DocumentReadResult
        {
            Succeeded = true,
            Summary = $"{blocks.Count:N0} block(s) · {headings.Count:N0} heading(s)",
            Outline = headings
        };
    }

    public override Task<DocumentExportArtifact> ExportAsync(
        DocumentExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var content = request.FormatId.ToLowerInvariant() switch
        {
            "html" => StandaloneHtmlDocumentBuilder.FromMarkdown(
                request.Title,
                request.Content),
            "docx" => WordprocessingDocumentExporter.Create(
                request.Title,
                StructuredDocumentParser.ParseMarkdown(request.Content)),
            _ => throw new NotSupportedException(
                $"Export format '{request.FormatId}' is not supported.")
        };
        return Task.FromResult(Artifact(request, content));
    }
}

public sealed class JsonDocumentProvider : BuiltInDocumentProvider
{
    public const string ProviderId = "core.document.json";
    private const int MaximumOutlineItems = 2048;
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64
    };

    public JsonDocumentProvider()
        : base(new DocumentProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Structured JSON provider",
            Operations = DocumentProviderOperation.Read
                         | DocumentProviderOperation.Replace
                         | DocumentProviderOperation.Insert
                         | DocumentProviderOperation.Export,
            LanguageIds = ["json"],
            FileExtensions = [".json", ".jsonc"],
            ExportFormats = DocumentExportFormatCatalog.StandardDocumentExports,
            Description =
                "Validated JSON/JSONC operations with bounded structural outlines and document export."
        })
    {
    }

    public override DocumentReadResult Read(DocumentReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var document = JsonDocument.Parse(request.Content, ParseOptions);
            var outline = new List<DocumentOutlineItem>();
            var truncated = false;
            AddOutline(document.RootElement, "$", 0, outline, ref truncated);
            var diagnostics = truncated
                ? new[]
                {
                    new DocumentProviderDiagnostic(
                        DocumentProviderDiagnosticSeverity.Warning,
                        "JSON_OUTLINE_TRUNCATED",
                        $"The structural outline is limited to {MaximumOutlineItems:N0} items.")
                }
                : [];
            return new DocumentReadResult
            {
                Succeeded = true,
                Summary = $"Valid JSON · {outline.Count:N0} structural item(s)",
                Outline = outline,
                Diagnostics = diagnostics
            };
        }
        catch (JsonException exception)
        {
            var diagnostic = CreateJsonDiagnostic(exception);
            return new DocumentReadResult
            {
                Succeeded = false,
                Summary = "JSON is not currently valid.",
                Diagnostics = [diagnostic]
            };
        }
    }

    public override Task<DocumentExportArtifact> ExportAsync(
        DocumentExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.Content, ParseOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "JSON export requires a valid source document.",
                exception);
        }

        byte[] content;
        using (document)
        {
            var normalized = JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
            content = request.FormatId.ToLowerInvariant() switch
            {
                "html" => StandaloneHtmlDocumentBuilder.FromPreformatted(
                    request.Title,
                    normalized,
                    "JSON"),
                "docx" => WordprocessingDocumentExporter.Create(
                    request.Title,
                    [new StructuredDocumentBlock(
                        StructuredDocumentBlockKind.Code,
                        normalized)]),
                _ => throw new NotSupportedException(
                    $"Export format '{request.FormatId}' is not supported.")
            };
        }

        return Task.FromResult(Artifact(request, content));
    }

    protected override DocumentMutationResult ValidateMutation(
        string originalContent,
        string content,
        string languageId,
        string filePath,
        string successMessage)
    {
        var analysis = Read(new DocumentReadRequest(content, languageId, filePath));
        return analysis.Succeeded
            ? new DocumentMutationResult
            {
                Succeeded = true,
                Content = content,
                Message = successMessage,
                Diagnostics = analysis.Diagnostics
            }
            : RejectedMutation(
                originalContent,
                "The mutation was rejected because it would leave invalid JSON.",
                analysis.Diagnostics);
    }

    private static void AddOutline(
        JsonElement element,
        string path,
        int level,
        ICollection<DocumentOutlineItem> outline,
        ref bool truncated)
    {
        if (outline.Count >= MaximumOutlineItems)
        {
            truncated = true;
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (outline.Count >= MaximumOutlineItems)
                    {
                        truncated = true;
                        return;
                    }

                    var propertyPath = $"{path}.{property.Name}";
                    outline.Add(new DocumentOutlineItem(
                        propertyPath,
                        property.Name,
                        property.Value.ValueKind.ToString(),
                        level));
                    AddOutline(
                        property.Value,
                        propertyPath,
                        level + 1,
                        outline,
                        ref truncated);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (outline.Count >= MaximumOutlineItems)
                    {
                        truncated = true;
                        return;
                    }

                    var itemPath = $"{path}[{index}]";
                    outline.Add(new DocumentOutlineItem(
                        itemPath,
                        $"[{index}]",
                        item.ValueKind.ToString(),
                        level));
                    AddOutline(item, itemPath, level + 1, outline, ref truncated);
                    index++;
                }

                break;
        }
    }

    private static DocumentProviderDiagnostic CreateJsonDiagnostic(
        JsonException exception) =>
        new(
            DocumentProviderDiagnosticSeverity.Error,
            "JSON_INVALID",
            $"{exception.Message} (line {(exception.LineNumber ?? 0) + 1}, byte {(exception.BytePositionInLine ?? 0) + 1})");
}
