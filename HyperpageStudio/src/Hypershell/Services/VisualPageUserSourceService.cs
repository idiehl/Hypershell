using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed class VisualPageUserSourceSnapshot
{
    public required string RelativePath { get; init; }
    public required string Content { get; set; }
    public required string Sha256 { get; set; }
}

public sealed class VisualPageUserSourceService
{
    public const int MaximumUserSourceCharacters = 2 * 1024 * 1024;

    public async Task<VisualPageUserSourceSnapshot> LoadAsync(
        string projectRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var path = VisualPageProjectService.ResolveProjectPath(
            projectRoot,
            relativePath);
        var content = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : string.Empty;
        if (content.Length > MaximumUserSourceCharacters)
        {
            throw new VisualPageValidationException(
                $"'{relativePath}' exceeds the 2 MB user-source limit.");
        }
        return new VisualPageUserSourceSnapshot
        {
            RelativePath = relativePath,
            Content = content,
            Sha256 = VisualPageHashing.Sha256Text(content)
        };
    }

    public async Task SaveAsync(
        string projectRoot,
        VisualPageUserSourceSnapshot snapshot,
        string newContent,
        CancellationToken cancellationToken = default)
    {
        if (newContent.Length > MaximumUserSourceCharacters)
        {
            throw new VisualPageValidationException(
                $"'{snapshot.RelativePath}' exceeds the 2 MB user-source limit.");
        }
        var path = VisualPageProjectService.ResolveProjectPath(
            projectRoot,
            snapshot.RelativePath);
        var diskContent = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : string.Empty;
        var diskHash = VisualPageHashing.Sha256Text(diskContent);
        if (!string.Equals(diskHash, snapshot.Sha256, StringComparison.Ordinal))
        {
            throw new VisualPageUserSourceDriftException(
                snapshot.RelativePath,
                snapshot.Sha256,
                diskHash);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                newContent,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        snapshot.Content = newContent;
        snapshot.Sha256 = VisualPageHashing.Sha256Text(newContent);
    }
}

public sealed class VisualPageUserSourceDriftException : Exception
{
    public VisualPageUserSourceDriftException(
        string relativePath,
        string expectedSha256,
        string actualSha256)
        : base($"'{relativePath}' changed outside Hyperpage Studio. Review it before saving.")
    {
        RelativePath = relativePath;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
    }

    public string RelativePath { get; }
    public string ExpectedSha256 { get; }
    public string ActualSha256 { get; }
}
