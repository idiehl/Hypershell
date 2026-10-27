using Hypershell.Models;
using Hypershell.Profiles;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Hypershell.Services;

public sealed class SshWorkspaceFileSystemProvider
    : IWorkspaceFileSystemProvider
{
    private readonly RemoteHostSummary _host;
    private readonly SshCredentialSummary _credential;
    private readonly SshWorkspaceConnectionService _connections;

    public SshWorkspaceFileSystemProvider(
        RemoteHostSummary host,
        SshCredentialSummary credential,
        SshWorkspaceConnectionService connections)
    {
        _host = host;
        _credential = credential;
        _connections = connections;
    }

    public string ProviderId => WorkspaceProviderIds.Ssh;

    public string Authority => _host.Id;

    public StringComparer PathComparer =>
        _host.OperatingSystem == RemoteHostOperatingSystem.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public WorkspaceLocation Normalize(WorkspaceLocation location)
    {
        RequireOwned(location);
        return WorkspaceLocation.Ssh(Authority, location.Path);
    }

    public WorkspaceLocation Combine(
        WorkspaceLocation directory,
        string childName)
    {
        var parent = Normalize(directory);
        ValidateChildName(childName);
        var separator = parent.Path.EndsWith('/') ? string.Empty : "/";
        return WorkspaceLocation.Ssh(
            Authority,
            parent.Path + separator + childName);
    }

    public WorkspaceLocation? GetParent(WorkspaceLocation location)
    {
        var normalized = Normalize(location);
        var path = normalized.Path.TrimEnd('/');
        if (path == "/"
            || (path.Length == 2 && path[1] == ':')
            || (path.Length == 3 && path[1] == ':' && path[2] == '/'))
        {
            return null;
        }

        var slash = path.LastIndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        var parent = slash == 0
            ? "/"
            : path[..slash];
        if (parent.Length == 2 && parent[1] == ':')
        {
            parent += "/";
        }

        return WorkspaceLocation.Ssh(Authority, parent);
    }

    public async Task<IReadOnlyList<WorkspaceEntry>> ListAsync(
        WorkspaceLocation directory,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(directory);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            var entries = new List<WorkspaceEntry>();
            await foreach (var item in lease.Client.ListDirectoryAsync(
                               normalized.Path,
                               cancellationToken))
            {
                if (item.Name is "." or "..")
                {
                    continue;
                }

                entries.Add(ToEntry(item));
            }

            return entries;
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "list the remote directory");
        }
    }

    public async Task<WorkspaceEntry?> StatAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            if (!await lease.Client.ExistsAsync(
                    normalized.Path,
                    cancellationToken))
            {
                return null;
            }

            var attributes = await lease.Client.GetAttributesAsync(
                normalized.Path,
                cancellationToken);
            return ToEntry(normalized, attributes);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "read remote item metadata");
        }
    }

    public async Task<WorkspaceFileContent> ReadFileAsync(
        WorkspaceLocation location,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        if (maximumBytes is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            var attributes = await lease.Client.GetAttributesAsync(
                normalized.Path,
                cancellationToken);
            if (!attributes.IsRegularFile || attributes.Size > maximumBytes)
            {
                throw Failure(
                    WorkspaceFailureKind.SizeLimit,
                    $"The remote file is not a regular file or exceeds the {maximumBytes:N0}-byte read limit.");
            }

            using var stream = new MemoryStream(
                checked((int)attributes.Size));
            await lease.Client.DownloadFileAsync(
                normalized.Path,
                stream,
                cancellationToken);
            var content = stream.ToArray();
            return new WorkspaceFileContent
            {
                Location = normalized,
                Content = content,
                Version = Hash(content),
                LastWriteTimeUtc = attributes.LastWriteTimeUtc
            };
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "read the remote file");
        }
    }

    public async Task CreateDirectoryAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            if (await lease.Client.ExistsAsync(
                    normalized.Path,
                    cancellationToken))
            {
                throw Failure(
                    WorkspaceFailureKind.AlreadyExists,
                    "A remote item already exists at that location.");
            }

            await lease.Client.CreateDirectoryAsync(
                normalized.Path,
                cancellationToken);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "create the remote directory");
        }
    }

    public async Task CreateFileAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            if (await lease.Client.ExistsAsync(
                    normalized.Path,
                    cancellationToken))
            {
                throw Failure(
                    WorkspaceFailureKind.AlreadyExists,
                    "A remote item already exists at that location.");
            }

            using var stream = new MemoryStream(content.ToArray(), writable: false);
            await lease.Client.UploadFileAsync(
                stream,
                normalized.Path,
                canOverride: false,
                uploadProgress: null,
                cancellationToken);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "create the remote file");
        }
    }

    public async Task<string> ReplaceFileAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            if (!await lease.Client.ExistsAsync(
                    normalized.Path,
                    cancellationToken))
            {
                throw Failure(
                    WorkspaceFailureKind.NotFound,
                    "The remote file no longer exists.");
            }

            if (!options.AllowOverwriteWithoutVersion)
            {
                if (string.IsNullOrWhiteSpace(options.ExpectedVersion))
                {
                    throw Failure(
                        WorkspaceFailureKind.Conflict,
                        "A compare-before-write version is required.");
                }

                var currentVersion = await HashRemoteFileAsync(
                    lease.Client,
                    normalized.Path,
                    cancellationToken);
                if (!string.Equals(
                        currentVersion,
                        options.ExpectedVersion,
                        StringComparison.Ordinal))
                {
                    throw Failure(
                        WorkspaceFailureKind.Conflict,
                        "The remote file changed after it was opened.");
                }
            }

            var parent = GetParent(normalized)
                         ?? throw Failure(
                             WorkspaceFailureKind.InvalidLocation,
                             "The remote root cannot be replaced as a file.");
            var temporary = Combine(
                parent,
                "."
                + GetName(normalized.Path)
                + "."
                + Guid.NewGuid().ToString("N")
                + ".hypershell-save");
            try
            {
                using var stream = new MemoryStream(
                    content.ToArray(),
                    writable: false);
                await lease.Client.UploadFileAsync(
                    stream,
                    temporary.Path,
                    canOverride: false,
                    uploadProgress: null,
                    cancellationToken);
                if (_host.OperatingSystem == RemoteHostOperatingSystem.Windows)
                {
                    await lease.Client.RenameFileAsync(
                        temporary.Path,
                        normalized.Path,
                        cancellationToken);
                }
                else
                {
                    await Task.Run(
                        () => lease.Client.RenameFile(
                            temporary.Path,
                            normalized.Path,
                            isPosix: true),
                        cancellationToken);
                }
            }
            finally
            {
                if (await lease.Client.ExistsAsync(
                        temporary.Path,
                        CancellationToken.None))
                {
                    await lease.Client.DeleteFileAsync(
                        temporary.Path,
                        CancellationToken.None);
                }
            }

            return Hash(content.Span);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "replace the remote file");
        }
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
                         "The remote root cannot be renamed.");
        return MoveAsync(
            normalized,
            Combine(parent, newName),
            cancellationToken);
    }

    public async Task MoveAsync(
        WorkspaceLocation source,
        WorkspaceLocation destination,
        CancellationToken cancellationToken = default)
    {
        var from = Normalize(source);
        var to = Normalize(destination);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            if (await lease.Client.ExistsAsync(to.Path, cancellationToken))
            {
                throw Failure(
                    WorkspaceFailureKind.AlreadyExists,
                    "A remote item already exists at the destination.");
            }

            await lease.Client.RenameFileAsync(
                from.Path,
                to.Path,
                cancellationToken);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "move the remote item");
        }
    }

    public async Task DeleteAsync(
        WorkspaceLocation location,
        WorkspaceDeleteConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(location);
        ValidateDeletion(normalized, confirmation);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            var attributes = await lease.Client.GetAttributesAsync(
                normalized.Path,
                cancellationToken);
            if (attributes.IsDirectory && !attributes.IsSymbolicLink)
            {
                if (confirmation.Recursive)
                {
                    await DeleteDirectoryTreeAsync(
                        lease.Client,
                        normalized.Path,
                        cancellationToken);
                }
                else
                {
                    await lease.Client.DeleteDirectoryAsync(
                        normalized.Path,
                        cancellationToken);
                }
            }
            else
            {
                await lease.Client.DeleteFileAsync(
                    normalized.Path,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "permanently delete the remote item");
        }
    }

    public async Task DownloadAsync(
        WorkspaceLocation source,
        Stream destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(source);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            IProgress<DownloadFileProgressReport>? adapter = progress is null
                ? null
                : new Progress<DownloadFileProgressReport>(report =>
                    progress.Report(checked((long)report.TotalBytesDownloaded)));
            await lease.Client.DownloadFileAsync(
                normalized.Path,
                destination,
                adapter!,
                cancellationToken);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "download the remote file");
        }
    }

    public async Task UploadAsync(
        Stream source,
        WorkspaceLocation destination,
        bool overwrite,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(destination);
        try
        {
            await using var lease = await _connections.OpenSftpAsync(
                _host,
                _credential,
                cancellationToken);
            IProgress<UploadFileProgressReport>? adapter = progress is null
                ? null
                : new Progress<UploadFileProgressReport>(report =>
                    progress.Report(checked((long)report.TotalBytesUploaded)));
            await lease.Client.UploadFileAsync(
                source,
                normalized.Path,
                overwrite,
                adapter!,
                cancellationToken);
        }
        catch (Exception exception) when (ShouldMap(exception))
        {
            throw Map(exception, "upload the remote file");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private WorkspaceEntry ToEntry(ISftpFile item) =>
        new()
        {
            Location = WorkspaceLocation.Ssh(Authority, item.FullName),
            Name = item.Name,
            IsDirectory = item.IsDirectory,
            IsSymbolicLink = item.IsSymbolicLink,
            IsHidden = item.Name.StartsWith(".", StringComparison.Ordinal),
            Length = item.IsRegularFile ? checked((long)item.Length) : 0,
            LastWriteTimeUtc = item.LastWriteTimeUtc,
            Version = MetadataVersion(
                checked((long)item.Length),
                item.LastWriteTimeUtc,
                item.Attributes)
        };

    private WorkspaceEntry ToEntry(
        WorkspaceLocation location,
        SftpFileAttributes attributes) =>
        new()
        {
            Location = location,
            Name = GetName(location.Path),
            IsDirectory = attributes.IsDirectory,
            IsSymbolicLink = attributes.IsSymbolicLink,
            IsHidden = GetName(location.Path).StartsWith(
                ".",
                StringComparison.Ordinal),
            Length = attributes.IsRegularFile
                ? checked((long)attributes.Size)
                : 0,
            LastWriteTimeUtc = attributes.LastWriteTimeUtc,
            Version = MetadataVersion(
                checked((long)attributes.Size),
                attributes.LastWriteTimeUtc,
                attributes)
        };

    private static async Task<string> HashRemoteFileAsync(
        SftpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var hash = SHA256.Create();
        using var sink = new CryptoStream(
            Stream.Null,
            hash,
            CryptoStreamMode.Write,
            leaveOpen: true);
        await client.DownloadFileAsync(path, sink, cancellationToken);
        sink.FlushFinalBlock();
        return "sha256:"
               + Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }

    private static async Task DeleteDirectoryTreeAsync(
        SftpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        await foreach (var child in client.ListDirectoryAsync(
                           path,
                           cancellationToken))
        {
            if (child.Name is "." or "..")
            {
                continue;
            }

            if (child.IsDirectory && !child.IsSymbolicLink)
            {
                await DeleteDirectoryTreeAsync(
                    client,
                    child.FullName,
                    cancellationToken);
            }
            else
            {
                await client.DeleteFileAsync(
                    child.FullName,
                    cancellationToken);
            }
        }

        await client.DeleteDirectoryAsync(path, cancellationToken);
    }

    private void RequireOwned(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!location.IsSsh
            || !string.Equals(
                location.Authority,
                Authority,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                WorkspaceFailureKind.InvalidLocation,
                "The SSH provider received a location for another host.");
        }
    }

    private static void ValidateChildName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains('/')
            || value.Contains('\\')
            || value.IndexOf('\0') >= 0)
        {
            throw Failure(
                WorkspaceFailureKind.InvalidLocation,
                "The remote item name is malformed.");
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
                StringComparison.Ordinal))
        {
            throw Failure(
                WorkspaceFailureKind.AccessDenied,
                "Permanent deletion requires confirmation for this exact remote location.");
        }

        if (location.Path == "/"
            || (location.Path.Length == 3
                && location.Path[1] == ':'
                && location.Path[2] == '/'))
        {
            throw Failure(
                WorkspaceFailureKind.AccessDenied,
                "A remote filesystem root cannot be deleted.");
        }
    }

    private static string GetName(string path)
    {
        var normalized = path.TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string MetadataVersion(
        long length,
        DateTime lastWriteTimeUtc,
        SftpFileAttributes attributes) =>
        $"meta:{length}:{lastWriteTimeUtc.Ticks}:{attributes.UserId}:{attributes.GroupId}";

    private static string Hash(ReadOnlySpan<byte> content) =>
        "sha256:"
        + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool ShouldMap(Exception exception) =>
        exception is not OperationCanceledException
        && exception is not WorkspaceFileSystemException;

    private static WorkspaceFileSystemException Map(
        Exception exception,
        string action) =>
        exception switch
        {
            SftpPathNotFoundException => Failure(
                WorkspaceFailureKind.NotFound,
                $"The remote path was not found while trying to {action}.",
                exception),
            SftpPermissionDeniedException => Failure(
                WorkspaceFailureKind.AccessDenied,
                $"The SSH account is not allowed to {action}.",
                exception),
            SshConnectionException or SocketException => Failure(
                WorkspaceFailureKind.Connection,
                $"The SSH connection was interrupted while trying to {action}.",
                exception),
            _ => Failure(
                WorkspaceFailureKind.Unsupported,
                $"The SSH host could not {action}: {exception.Message}",
                exception)
        };

    private static WorkspaceFileSystemException Failure(
        WorkspaceFailureKind kind,
        string message,
        Exception? innerException = null) =>
        new(kind, message, innerException);
}
