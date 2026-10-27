using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Text;

internal static class WorkspaceDocumentVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "workspace-documents");
        var backingRoot = Path.Combine(root, "remote");
        var cacheRoot = Path.Combine(root, "cache");
        Directory.CreateDirectory(Path.Combine(backingRoot, "project"));
        var backingFile = Path.Combine(backingRoot, "project", "README.md");
        await File.WriteAllTextAsync(
            backingFile,
            "# Remote\n\nfirst version\n",
            new UTF8Encoding(false));

        await using var registry = new WorkspaceFileSystemProviderRegistry(
            new LocalWorkspaceFileSystemProvider());
        registry.Register(new MappedSshWorkspaceProvider(
            "fake-host",
            backingRoot));
        var workspaceDocuments = new WorkspaceDocumentService(
            registry,
            cacheRoot);
        var location = WorkspaceLocation.Ssh(
            "fake-host",
            "/project/README.md");
        var document = await DocumentTabViewModel.OpenAsync(
            location,
            new TextDocumentService(),
            workspaceDocuments);
        Require(
            document.IsRemoteDocument
            && document.WorkspaceUri == location.Identity
            && File.Exists(document.LocalWorkingFilePath)
            && document.Content.Contains("first version", StringComparison.Ordinal),
            "A remote document did not retain its workspace identity and bounded cache.");

        document.Content = "# Remote\n\nlocal edit\n";
        await File.WriteAllTextAsync(
            backingFile,
            "# Remote\n\nconcurrent edit\n",
            new UTF8Encoding(false));
        var conflict = await RequireThrowsAsync<WorkspaceFileSystemException>(
            () => document.SaveRemoteAsync(overwriteWithoutVersion: false));
        Require(
            conflict.Kind == WorkspaceFailureKind.Conflict
            && document.HasUnsavedChanges,
            "A concurrent remote change did not preserve unsaved editor content.");

        await document.SaveRemoteAsync(overwriteWithoutVersion: true);
        Require(
            !document.HasUnsavedChanges
            && (await File.ReadAllTextAsync(backingFile)).Contains(
                "local edit",
                StringComparison.Ordinal),
            "An explicit remote overwrite did not update the backing file.");

        var entry = document.CreateSessionEntry();
        document.Content = "# Remote\n\nrecovered edit\n";
        var recovery = document.CreateRecoverySnapshot();
        Require(
            entry.WorkspaceUri == location.Identity
            && string.IsNullOrEmpty(entry.FilePath)
            && recovery.WorkspaceVersion.Length > 0,
            "Remote editor recovery persisted a cache path instead of stable workspace identity.");

        var binaryLocation = WorkspaceLocation.Ssh(
            "fake-host",
            "/project/sample.pdf");
        var binaryPath = Path.Combine(backingRoot, "project", "sample.pdf");
        var binary = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        await File.WriteAllBytesAsync(binaryPath, binary);
        var cachedBinary = await workspaceDocuments.CacheFileAsync(
            binaryLocation,
            1024);
        Require(
            File.ReadAllBytes(cachedBinary.LocalPath).AsSpan().SequenceEqual(binary),
            "A remote binary cache changed the source bytes.");

        await registry.RemoveAsync(WorkspaceProviderIds.Ssh, "fake-host");
        var restored = await DocumentTabViewModel.RestoreAsync(
            entry with { HadUnsavedChanges = true },
            recovery,
            new TextDocumentService(),
            workspaceDocuments);
        Require(
            restored is
            {
                IsRemoteDocument: true,
                HasUnsavedChanges: true
            }
            && restored.WorkspaceUri == location.Identity
            && restored.Content.Contains("recovered edit", StringComparison.Ordinal),
            "Offline remote recovery did not retain unsaved content and remote identity.");

        Console.WriteLine(
            "WORKSPACE_DOCUMENT_OK|remote-identity|bounded-cache|compare-before-write|explicit-overwrite|offline-recovery|binary-cache");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task<TException> RequireThrowsAsync<TException>(
        Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name} was not thrown.");
    }

    internal sealed class MappedSshWorkspaceProvider
        : IWorkspaceFileSystemProvider
    {
        private readonly LocalWorkspaceFileSystemProvider _local = new();
        private readonly string _root;

        public MappedSshWorkspaceProvider(string authority, string root)
        {
            Authority = authority;
            _root = Path.GetFullPath(root);
        }

        public string ProviderId => WorkspaceProviderIds.Ssh;
        public string Authority { get; }
        public StringComparer PathComparer => StringComparer.Ordinal;

        public WorkspaceLocation Normalize(WorkspaceLocation location)
        {
            if (!location.IsSsh
                || !string.Equals(
                    location.Authority,
                    Authority,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceFileSystemException(
                    WorkspaceFailureKind.InvalidLocation,
                    "The fake SSH location belongs to another provider.");
            }

            return WorkspaceLocation.Ssh(Authority, location.Path);
        }

        public WorkspaceLocation Combine(
            WorkspaceLocation directory,
            string childName) =>
            WorkspaceLocation.Ssh(
                Authority,
                Normalize(directory).Path.TrimEnd('/') + "/" + childName);

        public WorkspaceLocation? GetParent(WorkspaceLocation location)
        {
            var path = Normalize(location).Path;
            var slash = path.TrimEnd('/').LastIndexOf('/');
            return slash <= 0
                ? null
                : WorkspaceLocation.Ssh(Authority, path[..slash]);
        }

        public async Task<IReadOnlyList<WorkspaceEntry>> ListAsync(
            WorkspaceLocation directory,
            CancellationToken cancellationToken = default) =>
            (await _local.ListAsync(Map(directory), cancellationToken))
            .Select(entry => entry with
            {
                Location = Combine(directory, entry.Name)
            })
            .ToList();

        public async Task<WorkspaceEntry?> StatAsync(
            WorkspaceLocation location,
            CancellationToken cancellationToken = default)
        {
            var entry = await _local.StatAsync(Map(location), cancellationToken);
            return entry is null ? null : entry with { Location = Normalize(location) };
        }

        public async Task<WorkspaceFileContent> ReadFileAsync(
            WorkspaceLocation location,
            long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            using var content = await _local.ReadFileAsync(
                Map(location),
                maximumBytes,
                cancellationToken);
            return new WorkspaceFileContent
            {
                Location = Normalize(location),
                Content = content.Content.ToArray(),
                Version = content.Version,
                LastWriteTimeUtc = content.LastWriteTimeUtc
            };
        }

        public Task CreateDirectoryAsync(
            WorkspaceLocation location,
            CancellationToken cancellationToken = default) =>
            _local.CreateDirectoryAsync(Map(location), cancellationToken);

        public Task CreateFileAsync(
            WorkspaceLocation location,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            _local.CreateFileAsync(Map(location), content, cancellationToken);

        public Task<string> ReplaceFileAsync(
            WorkspaceLocation location,
            ReadOnlyMemory<byte> content,
            WorkspaceWriteOptions options,
            CancellationToken cancellationToken = default) =>
            _local.ReplaceFileAsync(
                Map(location),
                content,
                options,
                cancellationToken);

        public Task RenameAsync(
            WorkspaceLocation location,
            string newName,
            CancellationToken cancellationToken = default) =>
            _local.RenameAsync(Map(location), newName, cancellationToken);

        public Task MoveAsync(
            WorkspaceLocation source,
            WorkspaceLocation destination,
            CancellationToken cancellationToken = default) =>
            _local.MoveAsync(
                Map(source),
                Map(destination),
                cancellationToken);

        public Task DeleteAsync(
            WorkspaceLocation location,
            WorkspaceDeleteConfirmation confirmation,
            CancellationToken cancellationToken = default) =>
            _local.DeleteAsync(
                Map(location),
                confirmation with
                {
                    ConfirmedLocationIdentity = Map(location).Identity
                },
                cancellationToken);

        public Task DownloadAsync(
            WorkspaceLocation source,
            Stream destination,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default) =>
            _local.DownloadAsync(
                Map(source),
                destination,
                progress,
                cancellationToken);

        public Task UploadAsync(
            Stream source,
            WorkspaceLocation destination,
            bool overwrite,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default) =>
            _local.UploadAsync(
                source,
                Map(destination),
                overwrite,
                progress,
                cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private WorkspaceLocation Map(WorkspaceLocation location)
        {
            var normalized = Normalize(location);
            var relative = normalized.Path.TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);
            var local = Path.GetFullPath(Path.Combine(_root, relative));
            var rootPrefix = Path.TrimEndingDirectorySeparator(_root)
                             + Path.DirectorySeparatorChar;
            if (!local.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(local, _root, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceFileSystemException(
                    WorkspaceFailureKind.InvalidLocation,
                    "The fake SSH path escaped its root.");
            }

            return WorkspaceLocation.Local(local);
        }
    }
}
