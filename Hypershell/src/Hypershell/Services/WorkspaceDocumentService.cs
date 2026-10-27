using Hypershell.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.Services;

public enum WorkspaceDocumentSaveOutcome
{
    Saved,
    Conflict,
    Failed
}

public sealed record WorkspaceDocumentSaveResult(
    WorkspaceDocumentSaveOutcome Outcome,
    string Message)
{
    public bool Succeeded => Outcome == WorkspaceDocumentSaveOutcome.Saved;
}

public sealed record WorkspaceDocumentSnapshot
{
    public required WorkspaceLocation Location { get; init; }

    public required string Text { get; init; }

    public required DocumentEncodingKind EncodingKind { get; init; }

    public required DocumentLineEndingKind LineEndingKind { get; init; }

    public required string Version { get; init; }

    public required string LocalWorkingPath { get; init; }

    public bool IsCachedFallback { get; init; }
}

public sealed record WorkspaceCachedFileSnapshot(
    WorkspaceLocation Location,
    string Version,
    string LocalPath);

public sealed class WorkspaceDocumentService
{
    public const long MaximumCachedBinaryBytes = 128L * 1024L * 1024L;
    private readonly WorkspaceFileSystemProviderRegistry _providers;
    private readonly string _cacheDirectory;

    public WorkspaceDocumentService(
        WorkspaceFileSystemProviderRegistry providers,
        string cacheDirectory)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    public static WorkspaceDocumentService CreateLocalOnly() =>
        new(
            new WorkspaceFileSystemProviderRegistry(
                new LocalWorkspaceFileSystemProvider()),
            Path.Combine(
                Path.GetTempPath(),
                "Hypershell",
                "remote-document-cache"));

    public async Task<WorkspaceDocumentSnapshot> LoadAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.Resolve(location);
        var normalized = provider.Normalize(location);
        using var content = await provider.ReadFileAsync(
            normalized,
            TextDocumentService.MaximumDocumentBytes,
            cancellationToken);
        var text = TextDocumentService.Decode(content.Content);
        var workingPath = normalized.IsLocal
            ? normalized.Path
            : await WriteCacheAsync(
                normalized,
                content.Content,
                cancellationToken);
        return new WorkspaceDocumentSnapshot
        {
            Location = normalized,
            Text = text.Text,
            EncodingKind = text.EncodingKind,
            LineEndingKind = text.LineEndingKind,
            Version = content.Version,
            LocalWorkingPath = workingPath
        };
    }

    public async Task<WorkspaceDocumentSnapshot?> LoadCachedAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        if (location.IsLocal)
        {
            return null;
        }

        var cachePath = GetCachePath(location);
        var file = new FileInfo(cachePath);
        if (!file.Exists || file.Length > TextDocumentService.MaximumDocumentBytes)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(cachePath, cancellationToken);
        var text = TextDocumentService.Decode(bytes);
        return new WorkspaceDocumentSnapshot
        {
            Location = location,
            Text = text.Text,
            EncodingKind = text.EncodingKind,
            LineEndingKind = text.LineEndingKind,
            Version = string.Empty,
            LocalWorkingPath = cachePath,
            IsCachedFallback = true
        };
    }

    public async Task<WorkspaceCachedFileSnapshot> CacheFileAsync(
        WorkspaceLocation location,
        long maximumBytes = MaximumCachedBinaryBytes,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.Resolve(location);
        var normalized = provider.Normalize(location);
        if (normalized.IsLocal)
        {
            var entry = await provider.StatAsync(normalized, cancellationToken)
                        ?? throw new WorkspaceFileSystemException(
                            WorkspaceFailureKind.NotFound,
                            "The local file no longer exists.");
            if (entry.Length > maximumBytes)
            {
                throw new WorkspaceFileSystemException(
                    WorkspaceFailureKind.SizeLimit,
                    $"The file exceeds the {maximumBytes:N0}-byte cache limit.");
            }

            return new WorkspaceCachedFileSnapshot(
                normalized,
                entry.Version,
                normalized.Path);
        }

        using var content = await provider.ReadFileAsync(
            normalized,
            maximumBytes,
            cancellationToken);
        var cachePath = await WriteCacheAsync(
            normalized,
            content.Content,
            cancellationToken);
        return new WorkspaceCachedFileSnapshot(
            normalized,
            content.Version,
            cachePath);
    }

    public async Task<WorkspaceDocumentSnapshot> SaveAsync(
        WorkspaceLocation location,
        string text,
        DocumentEncodingKind encodingKind,
        string expectedVersion,
        bool overwriteWithoutVersion,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.Resolve(location);
        var normalized = provider.Normalize(location);
        var bytes = TextDocumentService.Encode(text, encodingKind);
        if (bytes.LongLength > TextDocumentService.MaximumDocumentBytes)
        {
            throw new WorkspaceFileSystemException(
                WorkspaceFailureKind.SizeLimit,
                $"The document exceeds the {TextDocumentService.MaximumDocumentBytes / 1024 / 1024} MiB editor limit.");
        }

        var version = await provider.ReplaceFileAsync(
            normalized,
            bytes,
            new WorkspaceWriteOptions
            {
                ExpectedVersion = expectedVersion,
                AllowOverwriteWithoutVersion = overwriteWithoutVersion
            },
            cancellationToken);
        var workingPath = normalized.IsLocal
            ? normalized.Path
            : await WriteCacheAsync(normalized, bytes, cancellationToken);
        return new WorkspaceDocumentSnapshot
        {
            Location = normalized,
            Text = text,
            EncodingKind = encodingKind,
            LineEndingKind = TextDocumentService.DetectLineEnding(text),
            Version = version,
            LocalWorkingPath = workingPath
        };
    }

    public string GetCachePath(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var identityHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(location.Identity)))
            .ToLowerInvariant();
        var fileName = GetSafeFileName(location.Path);
        return Path.Combine(
            _cacheDirectory,
            identityHash[..2],
            $"{identityHash}-{fileName}");
    }

    private async Task<string> WriteCacheAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(location);
        var directory = Path.GetDirectoryName(cachePath)
                        ?? throw new InvalidOperationException(
                            "The remote document cache path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             useAsync: true))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return cachePath;
    }

    private static string GetSafeFileName(string providerPath)
    {
        var normalized = providerPath.TrimEnd('/', '\\');
        var slash = Math.Max(
            normalized.LastIndexOf('/'),
            normalized.LastIndexOf('\\'));
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            return "document.txt";
        }

        if (safe.Length <= 96)
        {
            return safe;
        }

        var extension = Path.GetExtension(safe);
        if (extension.Length > 16)
        {
            extension = extension[..16];
        }

        var stemLength = Math.Max(1, 96 - extension.Length);
        return safe[..stemLength] + extension;
    }
}
