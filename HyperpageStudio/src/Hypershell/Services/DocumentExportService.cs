using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed class DocumentExportService
{
    public const int MaximumInputCharacters = 8 * 1024 * 1024;
    public const int MaximumArtifactBytes = 64 * 1024 * 1024;

    public async Task<DocumentExportArtifact> CreateArtifactAsync(
        IDocumentProvider provider,
        DocumentExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Content.Length > MaximumInputCharacters)
        {
            throw new InvalidDataException(
                $"Document export is limited to {MaximumInputCharacters:N0} in-memory characters.");
        }

        var declaredFormat = provider.Definition.ExportFormats.FirstOrDefault(format =>
            string.Equals(format.Id, request.FormatId, StringComparison.OrdinalIgnoreCase));
        if (declaredFormat is null)
        {
            throw new NotSupportedException(
                $"{provider.Definition.DisplayName} does not support the requested export format.");
        }

        var artifact = await provider.ExportAsync(request, cancellationToken);
        if (!string.Equals(
                artifact.Format.Id,
                declaredFormat.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The document provider returned a different export format than requested.");
        }

        if (artifact.Content.Length == 0)
        {
            throw new InvalidDataException("The document provider returned an empty export artifact.");
        }

        if (artifact.Content.Length > MaximumArtifactBytes)
        {
            throw new InvalidDataException(
                $"The export artifact exceeds the {MaximumArtifactBytes / 1024 / 1024} MiB safety limit.");
        }

        return artifact;
    }

    public async Task<string> WriteArtifactAsync(
        DocumentExportArtifact artifact,
        string outputPath,
        string sourcePath = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(
                Path.GetExtension(fullPath),
                artifact.Format.FileExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The selected path must use the {artifact.Format.FileExtension} extension.");
        }

        if (!string.IsNullOrWhiteSpace(sourcePath)
            && string.Equals(
                fullPath,
                Path.GetFullPath(sourcePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Export cannot overwrite the active source document. Choose a different path.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The export path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.hypershell-export");
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                artifact.Content,
                cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return fullPath;
    }
}
