using Hypershell.Models;
using System.IO;
using System.Security.Cryptography;

namespace Hypershell.Services;

public sealed class LocalWorkspaceFileSystemProvider
    : IWorkspaceFileSystemProvider
{
    public string ProviderId => WorkspaceProviderIds.Local;

    public string Authority => string.Empty;

    public StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

    public WorkspaceLocation Normalize(WorkspaceLocation location)
    {
        RequireLocal(location);
        return WorkspaceLocation.Local(location.Path);
    }

    public WorkspaceLocation Combine(
        WorkspaceLocation directory,
        string childName)
    {
        var parent = Normalize(directory);
        ValidateChildName(childName);
        return WorkspaceLocation.Local(Path.Combine(parent.Path, childName));
    }

    public WorkspaceLocation? GetParent(WorkspaceLocation location)
    {
        var normalized = Normalize(location);
        var parent = Directory.GetParent(normalized.Path);
        return parent is null
            ? null
            : WorkspaceLocation.Local(parent.FullName);
    }

    public Task<IReadOnlyList<WorkspaceEntry>> ListAsync(
        WorkspaceLocation directory,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(directory);
        return Task.Run<IReadOnlyList<WorkspaceEntry>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new DirectoryInfo(normalized.Path);
                if (!info.Exists)
                {
                    throw Failure(
                        WorkspaceFailureKind.NotFound,
                        "The local directory no longer exists.");
                }

                var entries = new List<WorkspaceEntry>();
                foreach (var item in info.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Add(ToEntry(item));
                }

                return entries;
            },
            cancellationToken);
    }

    public Task<WorkspaceEntry?> StatAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        return Task.Run<WorkspaceEntry?>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(normalized.Path))
                {
                    return ToEntry(new DirectoryInfo(normalized.Path));
                }

                return File.Exists(normalized.Path)
                    ? ToEntry(new FileInfo(normalized.Path))
                    : null;
            },
            cancellationToken);
    }

    public async Task<WorkspaceFileContent> ReadFileAsync(
        WorkspaceLocation location,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        ValidateMaximumBytes(maximumBytes);
        var file = new FileInfo(normalized.Path);
        if (!file.Exists)
        {
            throw Failure(
                WorkspaceFailureKind.NotFound,
                "The local file no longer exists.");
        }

        if (file.Length > maximumBytes || file.Length > int.MaxValue)
        {
            throw Failure(
                WorkspaceFailureKind.SizeLimit,
                $"The file exceeds the {maximumBytes:N0}-byte read limit.");
        }

        var content = await File.ReadAllBytesAsync(
            normalized.Path,
            cancellationToken);
        return new WorkspaceFileContent
        {
            Location = normalized,
            Content = content,
            Version = Hash(content),
            LastWriteTimeUtc = file.LastWriteTimeUtc
        };
    }

    public Task CreateDirectoryAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(normalized.Path) || Directory.Exists(normalized.Path))
        {
            throw Failure(
                WorkspaceFailureKind.AlreadyExists,
                "A local item already exists at that location.");
        }

        Directory.CreateDirectory(normalized.Path);
        return Task.CompletedTask;
    }

    public async Task CreateFileAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        var parent = Path.GetDirectoryName(normalized.Path)
                     ?? throw Failure(
                         WorkspaceFailureKind.InvalidLocation,
                         "The local file has no parent directory.");
        Directory.CreateDirectory(parent);
        await using var stream = new FileStream(
            normalized.Path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<string> ReplaceFileAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(normalized.Path))
        {
            throw Failure(
                WorkspaceFailureKind.NotFound,
                "The local file no longer exists.");
        }

        if (!options.AllowOverwriteWithoutVersion)
        {
            if (string.IsNullOrWhiteSpace(options.ExpectedVersion))
            {
                throw Failure(
                    WorkspaceFailureKind.Conflict,
                    "A compare-before-write version is required.");
            }

            var currentVersion = await HashFileAsync(
                normalized.Path,
                cancellationToken);
            if (!string.Equals(
                    currentVersion,
                    options.ExpectedVersion,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    WorkspaceFailureKind.Conflict,
                    "The local file changed after it was opened.");
            }
        }

        var parent = Path.GetDirectoryName(normalized.Path)
                     ?? throw Failure(
                         WorkspaceFailureKind.InvalidLocation,
                         "The local file has no parent directory.");
        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(normalized.Path)}.{Guid.NewGuid():N}.hypershell-save");
        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                content.ToArray(),
                cancellationToken);
            File.Move(temporary, normalized.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return Hash(content.Span);
    }

    public Task RenameAsync(
        WorkspaceLocation location,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        ValidateChildName(newName);
        var parent = GetParent(normalized)
                     ?? throw Failure(
                         WorkspaceFailureKind.InvalidLocation,
                         "The local root cannot be renamed.");
        return MoveAsync(
            normalized,
            Combine(parent, newName),
            cancellationToken);
    }

    public Task MoveAsync(
        WorkspaceLocation source,
        WorkspaceLocation destination,
        CancellationToken cancellationToken = default)
    {
        var from = Normalize(source);
        var to = Normalize(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(to.Path) || Directory.Exists(to.Path))
        {
            throw Failure(
                WorkspaceFailureKind.AlreadyExists,
                "A local item already exists at the destination.");
        }

        if (Directory.Exists(from.Path))
        {
            Directory.Move(from.Path, to.Path);
        }
        else if (File.Exists(from.Path))
        {
            File.Move(from.Path, to.Path);
        }
        else
        {
            throw Failure(
                WorkspaceFailureKind.NotFound,
                "The local source no longer exists.");
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        WorkspaceLocation location,
        WorkspaceDeleteConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        ValidateDeletion(normalized, confirmation);
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(normalized.Path))
        {
            File.Delete(normalized.Path);
        }
        else if (Directory.Exists(normalized.Path))
        {
            Directory.Delete(normalized.Path, confirmation.Recursive);
        }
        else
        {
            throw Failure(
                WorkspaceFailureKind.NotFound,
                "The local item no longer exists.");
        }

        return Task.CompletedTask;
    }

    public async Task DownloadAsync(
        WorkspaceLocation source,
        Stream destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(source);
        await using var input = new FileStream(
            normalized.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await CopyWithProgressAsync(
            input,
            destination,
            progress,
            cancellationToken);
    }

    public async Task UploadAsync(
        Stream source,
        WorkspaceLocation destination,
        bool overwrite,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(destination);
        await using var output = new FileStream(
            normalized.Path,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await CopyWithProgressAsync(
            source,
            output,
            progress,
            cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static WorkspaceEntry ToEntry(FileSystemInfo info)
    {
        info.Refresh();
        var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);
        var length = !isDirectory && info is FileInfo file
            ? file.Length
            : 0;
        var version = $"meta:{length}:{info.LastWriteTimeUtc.Ticks}:{(int)info.Attributes}";
        return new WorkspaceEntry
        {
            Location = WorkspaceLocation.Local(info.FullName),
            Name = info.Name,
            IsDirectory = isDirectory,
            IsSymbolicLink = info.LinkTarget is not null,
            IsHidden = info.Attributes.HasFlag(FileAttributes.Hidden),
            Length = length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
            Version = version
        };
    }

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
            total += read;
            progress?.Report(total);
        }
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }

        return "sha256:"
               + Convert.ToHexString(hash.GetHashAndReset())
                   .ToLowerInvariant();
    }

    private static void RequireLocal(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!location.IsLocal || !string.IsNullOrEmpty(location.Authority))
        {
            throw Failure(
                WorkspaceFailureKind.InvalidLocation,
                "The local provider received a non-local workspace location.");
        }
    }

    private static void ValidateChildName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw Failure(
                WorkspaceFailureKind.InvalidLocation,
                "The local item name is malformed.");
        }
    }

    private static void ValidateMaximumBytes(long maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
    }

    private static void ValidateDeletion(
        WorkspaceLocation location,
        WorkspaceDeleteConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        if (!confirmation.ConfirmPermanentDeletion
            || !string.Equals(
                confirmation.ConfirmedLocationIdentity,
                location.Identity,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                WorkspaceFailureKind.AccessDenied,
                "Permanent deletion requires confirmation for this exact workspace location.");
        }

        if (GetRoot(location.Path) is { } root
            && string.Equals(
                Path.TrimEndingDirectorySeparator(location.Path),
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                WorkspaceFailureKind.AccessDenied,
                "A filesystem root cannot be deleted through the workspace provider.");
        }
    }

    private static string? GetRoot(string path) => Path.GetPathRoot(path);

    private static string Hash(ReadOnlySpan<byte> content) =>
        "sha256:"
        + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static WorkspaceFileSystemException Failure(
        WorkspaceFailureKind kind,
        string message,
        Exception? innerException = null) =>
        new(kind, message, innerException);
}
