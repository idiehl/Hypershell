using Hypershell.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public sealed class VisualPageStudioSessionSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ActiveProjectId { get; set; } = string.Empty;
    public List<VisualPageStudioSessionEntry> Projects { get; set; } = [];
    public StudioLayoutSnapshot? PersonalDefaultLayout { get; set; }
    public List<StudioLayoutSnapshot> NamedLayoutPresets { get; set; } = [];
}

public sealed class VisualPageStudioSessionEntry
{
    public bool IsHyperlibEditor { get; set; }
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string PageId { get; set; } = string.Empty;
    public string SavedProjectRevision { get; set; } = string.Empty;
    public string SavedPageRevision { get; set; } = string.Empty;
    public VisualPageDocument? RecoveryDocument { get; set; }
    public bool HasUserSourceRecovery { get; set; }
    public string UserCSharp { get; set; } = string.Empty;
    public string UserCss { get; set; } = string.Empty;
    public string UserJavaScript { get; set; } = string.Empty;
    public List<VisualPageStudioDocumentRecovery> ProjectDocuments { get; set; } = [];
    public bool HasSelectiveUserSourceRecovery { get; set; }
    public List<VisualPageStudioDocumentRecovery> UserSourceDocuments { get; set; } = [];
}

public sealed class VisualPageStudioDocumentRecovery
{
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string BaseContent { get; set; } = string.Empty;
    public string BaseSha256 { get; set; } = string.Empty;
    public string ViewState { get; set; } = string.Empty;
    public bool BaselineUnavailable { get; set; }
}

public sealed class VisualPageStudioSessionService
{
    public const int MaximumRestoredProjects = 12;
    public const int MaximumStoredProjects = 64;
    public const long MaximumSessionBytes = 32L * 1024 * 1024;
    public const int MaximumRecoveredDocumentsPerProject = 128;
    public const int MaximumRecoveredViewStateCharacters = 64 * 1024;
    private readonly string _path;

    public VisualPageStudioSessionService(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task<VisualPageStudioSessionSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length > MaximumSessionBytes)
            {
                return new VisualPageStudioSessionSnapshot();
            }
            await using var stream = info.OpenRead();
            var session = await JsonSerializer.DeserializeAsync<VisualPageStudioSessionSnapshot>(
                stream,
                VisualPageJson.Options,
                cancellationToken);
            if (session?.SchemaVersion != 1)
            {
                return new VisualPageStudioSessionSnapshot();
            }
            session.Projects = (session.Projects ?? [])
                .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.ProjectRoot)
                                && !string.IsNullOrWhiteSpace(entry.PageId))
                .Take(MaximumStoredProjects)
                .ToList();
            session.NamedLayoutPresets = (session.NamedLayoutPresets ?? []).Where(layout => layout is not null).ToList();
            foreach (var entry in session.Projects)
            {
                entry.ProjectDocuments = (entry.ProjectDocuments ?? [])
                    .Where(IsValidDocumentRecovery)
                    .DistinctBy(document => document.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumRecoveredDocumentsPerProject).ToList();
                entry.UserSourceDocuments = (entry.UserSourceDocuments ?? []).Where(IsValidDocumentRecovery)
                    .DistinctBy(document => document.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumRecoveredDocumentsPerProject).ToList();
            }
            return session;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException)
        {
            return new VisualPageStudioSessionSnapshot();
        }
    }

    public async Task SaveAsync(
        VisualPageStudioSessionSnapshot session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Projects is null || session.Projects.Count > MaximumStoredProjects || session.Projects.Any(entry => entry is null
            || string.IsNullOrWhiteSpace(entry.ProjectRoot) || string.IsNullOrWhiteSpace(entry.PageId)))
            throw new VisualPageValidationException("The Studio recovery session contains an invalid project entry.");
        session.SchemaVersion = 1;
        session.SavedAtUtc = DateTimeOffset.UtcNow;
        long recoveryBytes = 0;
        foreach (var entry in session.Projects)
        {
            if (entry.ProjectDocuments is null || entry.ProjectDocuments.Count > MaximumRecoveredDocumentsPerProject
                || entry.UserSourceDocuments is null || entry.UserSourceDocuments.Count > MaximumRecoveredDocumentsPerProject
                || entry.ProjectDocuments.Concat(entry.UserSourceDocuments).Any(document => !IsValidDocumentRecovery(document)))
                throw new VisualPageValidationException("A Studio document recovery draft is invalid or exceeds its size limit.");
            foreach (var document in entry.ProjectDocuments.Concat(entry.UserSourceDocuments))
            {
                recoveryBytes += Encoding.UTF8.GetByteCount(document.Content) + (long)Encoding.UTF8.GetByteCount(document.BaseContent)
                    + Encoding.UTF8.GetByteCount(document.ViewState);
                if (recoveryBytes > MaximumSessionBytes)
                    throw new VisualPageValidationException("The Studio document recovery drafts exceed 32 MB.");
            }
        }
        var content = JsonSerializer.Serialize(session, VisualPageJson.Options);
        if (Encoding.UTF8.GetByteCount(content) > MaximumSessionBytes)
        {
            throw new VisualPageValidationException(
                "The Hyperpage Studio recovery session exceeds 32 MB.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                content,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static bool IsValidDocumentRecovery(VisualPageStudioDocumentRecovery? document)
    {
        if (document is null || string.IsNullOrWhiteSpace(document.RelativePath)
            || document.RelativePath.Length > 512 || Path.IsPathRooted(document.RelativePath)
            || document.Content is null || document.BaseContent is null || document.BaseSha256 is null || document.ViewState is null
            || document.Content.Length > VisualPageUserSourceService.MaximumUserSourceCharacters
            || document.BaseContent.Length > VisualPageUserSourceService.MaximumUserSourceCharacters
            || document.ViewState.Length > MaximumRecoveredViewStateCharacters)
            return false;
        var segments = document.RelativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."
            || segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return false;
        return document.BaselineUnavailable
            ? document.BaseContent.Length == 0 && document.BaseSha256.Length == 0
            : string.Equals(document.BaseSha256, VisualPageHashing.Sha256Text(document.BaseContent), StringComparison.Ordinal);
    }
}
