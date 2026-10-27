using Hypershell.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public sealed class ProjectWorkItemService
{
    public const string MetadataDirectoryName = ".hypershell";
    public const string StoreFileName = "work-items.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string GetStorePath(ProjectWorkItemScope scope) =>
        Path.Combine(
            Path.GetFullPath(scope.RootDirectory),
            MetadataDirectoryName,
            StoreFileName);

    public ProjectWorkItemDocument Load(ProjectWorkItemScope scope)
    {
        var storePath = GetStorePath(scope);
        if (!File.Exists(storePath))
        {
            return CreateEmpty(scope);
        }

        try
        {
            var document = JsonSerializer.Deserialize<ProjectWorkItemDocument>(
                               File.ReadAllText(storePath),
                               _jsonOptions)
                           ?? throw new InvalidDataException(
                               "The work-item document was empty.");
            Normalize(document, scope);
            return document;
        }
        catch (Exception exception) when (exception is JsonException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Could not load '{storePath}': {exception.Message}",
                exception);
        }
    }

    public void Save(
        ProjectWorkItemScope scope,
        ProjectWorkItemDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rootDirectory = Path.GetFullPath(scope.RootDirectory);
        if (!Directory.Exists(rootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The project root '{rootDirectory}' is not available.");
        }

        var metadataDirectory = Path.Combine(
            rootDirectory,
            MetadataDirectoryName);
        Directory.CreateDirectory(metadataDirectory);
        Normalize(document, scope);
        foreach (var item in document.Items)
        {
            foreach (var attachment in item.Attachments)
            {
                ImportAttachment(
                    metadataDirectory,
                    item.Id,
                    attachment);
            }
        }

        document.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var storePath = Path.Combine(metadataDirectory, StoreFileName);
        var temporaryPath = $"{storePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, _jsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string ResolveAttachmentPath(
        ProjectWorkItemScope scope,
        ProjectWorkItemAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.RelativePath))
        {
            return attachment.SourcePath;
        }

        var metadataDirectory = Path.Combine(
            Path.GetFullPath(scope.RootDirectory),
            MetadataDirectoryName);
        return ResolveBoundedRelativePath(
            metadataDirectory,
            attachment.RelativePath);
    }

    public static string CreateFolderScopeId(string rootDirectory)
    {
        var canonical = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"folder-{Convert.ToHexString(digest)[..16].ToLowerInvariant()}";
    }

    private static ProjectWorkItemDocument CreateEmpty(
        ProjectWorkItemScope scope) =>
        new()
        {
            ProjectId = scope.Id,
            ProjectName = scope.Name
        };

    private static void Normalize(
        ProjectWorkItemDocument document,
        ProjectWorkItemScope scope)
    {
        if (document.SchemaVersion is < 1
            or > ProjectWorkItemDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Work-item schema version {document.SchemaVersion} is not supported.");
        }

        document.SchemaVersion = ProjectWorkItemDocument.CurrentSchemaVersion;
        document.ProjectId = scope.Id;
        document.ProjectName = scope.Name;
        document.Items ??= [];
        var normalizedItems = document.Items
            .Where(item => item is not null)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.CreatedAtUtc)
            .ToList();
        var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < normalizedItems.Count; index++)
        {
            var item = normalizedItems[index];
            if (string.IsNullOrWhiteSpace(item.Id)
                || !seenItemIds.Add(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
                seenItemIds.Add(item.Id);
            }

            item.Title = item.Title?.Trim() ?? string.Empty;
            item.Description ??= string.Empty;
            item.Order = index;
            if (item.CreatedAtUtc == default)
            {
                item.CreatedAtUtc = DateTimeOffset.UtcNow;
            }

            if (item.UpdatedAtUtc == default)
            {
                item.UpdatedAtUtc = item.CreatedAtUtc;
            }

            item.Attachments ??= [];
            var seenAttachmentIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var attachment in item.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.Id)
                    || !seenAttachmentIds.Add(attachment.Id))
                {
                    attachment.Id = Guid.NewGuid().ToString("N");
                    seenAttachmentIds.Add(attachment.Id);
                }

                attachment.FileName = string.IsNullOrWhiteSpace(
                    attachment.FileName)
                    ? Path.GetFileName(attachment.RelativePath)
                    : attachment.FileName.Trim();
                attachment.RelativePath = attachment.RelativePath
                    ?.Replace('\\', '/')
                    .Trim() ?? string.Empty;
            }
        }

        document.Items = normalizedItems;
    }

    private static void ImportAttachment(
        string metadataDirectory,
        string itemId,
        ProjectWorkItemAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.SourcePath))
        {
            if (!string.IsNullOrWhiteSpace(attachment.RelativePath))
            {
                _ = ResolveBoundedRelativePath(
                    metadataDirectory,
                    attachment.RelativePath);
            }

            return;
        }

        var sourcePath = Path.GetFullPath(attachment.SourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "A work-item attachment is no longer available.",
                sourcePath);
        }

        var safeName = SanitizeFileName(
            string.IsNullOrWhiteSpace(attachment.FileName)
                ? Path.GetFileName(sourcePath)
                : attachment.FileName);
        var relativePath = Path.Combine(
                "work-items",
                "attachments",
                itemId,
                $"{attachment.Id}-{safeName}")
            .Replace('\\', '/');
        var destinationPath = ResolveBoundedRelativePath(
            metadataDirectory,
            relativePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException(
                "The attachment destination did not have a parent directory."));
        if (!File.Exists(destinationPath))
        {
            File.Copy(sourcePath, destinationPath);
        }

        attachment.FileName = safeName;
        attachment.RelativePath = relativePath;
        attachment.SourcePath = string.Empty;
    }

    private static string ResolveBoundedRelativePath(
        string metadataDirectory,
        string relativePath)
    {
        if (Path.IsPathRooted(relativePath)
            || relativePath.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException(
                "A work-item attachment path must be relative to the project metadata directory.");
        }

        var canonicalRoot = Path.GetFullPath(metadataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(
            canonicalRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A work-item attachment path escapes the project metadata directory.");
        }

        return fullPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = string.Concat(fileName.Select(character =>
            invalid.Contains(character) ? '-' : character)).Trim();
        return string.IsNullOrWhiteSpace(sanitized)
            ? "attachment"
            : sanitized;
    }
}
