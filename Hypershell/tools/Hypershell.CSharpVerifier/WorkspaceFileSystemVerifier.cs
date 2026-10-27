using Hypershell.Models;
using Hypershell.Services;
using System.Security.Cryptography;
using System.Text;

internal static class WorkspaceFileSystemVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "workspace-filesystems");
        Directory.CreateDirectory(root);
        var local = new LocalWorkspaceFileSystemProvider();
        await using var registry = new WorkspaceFileSystemProviderRegistry(
            local);

        var localRoot = WorkspaceLocation.Local(root);
        var localRoundTrip = WorkspaceLocation.Parse(localRoot.Identity);
        Require(
            WorkspaceLocation.SameIdentity(localRoot, localRoundTrip)
            && localRoot.Identity.StartsWith(
                "local:///",
                StringComparison.Ordinal),
            "Local workspace identities did not round-trip canonically.");

        var localDevice = ExplorerDeviceOption.ThisPc(localRoot);
        Require(
            localDevice.DisplayName
                == Environment.MachineName.Trim().Replace('-', ' ')
            && string.IsNullOrEmpty(localDevice.StateText),
            "The local Explorer device must use the Windows device name without a redundant LOCAL badge.");

        var posix = WorkspaceLocation.Ssh(
            "host-id",
            "/srv/hypershell/Project Files/π");
        var posixRoundTrip = WorkspaceLocation.Parse(posix.Identity);
        var windows = WorkspaceLocation.Ssh(
            "windows-host",
            "c:\\Users\\ihigg\\source\\..\\project");
        Require(
            WorkspaceLocation.SameIdentity(posix, posixRoundTrip)
            && posix.Identity ==
                "ssh://host-id/srv/hypershell/Project%20Files/%CF%80"
            && windows.Path == "C:/Users/ihigg/project"
            && windows.Identity ==
                "ssh://windows-host/C:/Users/ihigg/project",
            "SSH workspace identities or path normalization are unstable.");

        RequireThrows<ArgumentException>(() =>
            WorkspaceLocation.Ssh("host-id", "/../etc/passwd"));
        RequireThrows<WorkspaceFileSystemException>(() =>
            registry.Resolve(posix));

        var directory = local.Combine(localRoot, "folder");
        await local.CreateDirectoryAsync(directory);
        RequireThrows<WorkspaceFileSystemException>(() =>
            local.Combine(localRoot, "..\\escape"));

        var file = local.Combine(directory, "sample.txt");
        var initial = Encoding.UTF8.GetBytes("first version\n");
        await local.CreateFileAsync(file, initial);
        using var opened = await local.ReadFileAsync(file, 1024);
        Require(
            Encoding.UTF8.GetString(opened.Content) == "first version\n"
            && opened.Version.StartsWith("sha256:", StringComparison.Ordinal),
            "The local provider did not return bounded content and a content version.");
        await RequireThrowsAsync<WorkspaceFileSystemException>(() =>
            local.ReadFileAsync(file, 2));

        var replacement = Encoding.UTF8.GetBytes("second version\n");
        var nextVersion = await local.ReplaceFileAsync(
            file,
            replacement,
            new WorkspaceWriteOptions
            {
                ExpectedVersion = opened.Version
            });
        await File.WriteAllTextAsync(file.Path, "external edit\n");
        var conflict = await RequireThrowsAsync<WorkspaceFileSystemException>(
            () => local.ReplaceFileAsync(
                file,
                replacement,
                new WorkspaceWriteOptions
                {
                    ExpectedVersion = nextVersion
                }));
        Require(
            conflict.Kind == WorkspaceFailureKind.Conflict,
            "A local concurrent edit did not produce a version conflict.");

        _ = await local.ReplaceFileAsync(
            file,
            replacement,
            new WorkspaceWriteOptions
            {
                AllowOverwriteWithoutVersion = true
            });
        Require(
            !Directory.EnumerateFiles(
                    directory.Path,
                    "*.hypershell-save")
                .Any(),
            "An atomic local replace left a staging file behind.");

        await local.RenameAsync(file, "renamed.txt");
        var renamed = local.Combine(directory, "renamed.txt");
        Require(
            await local.StatAsync(file) is null
            && await local.StatAsync(renamed) is { IsDirectory: false },
            "The local provider rename did not preserve the file identity.");

        var bulk = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 17);
        var uploaded = local.Combine(directory, "upload.bin");
        using (var input = new MemoryStream(bulk, writable: false))
        {
            await local.UploadAsync(input, uploaded, overwrite: false);
        }

        using var downloaded = new MemoryStream();
        await local.DownloadAsync(uploaded, downloaded);
        Require(
            downloaded.ToArray().AsSpan().SequenceEqual(bulk),
            "The local provider upload/download stream changed content.");
        CryptographicOperations.ZeroMemory(bulk);

        var noConfirmation = await RequireThrowsAsync<
            WorkspaceFileSystemException>(() => local.DeleteAsync(
            directory,
            new WorkspaceDeleteConfirmation
            {
                ConfirmedLocationIdentity = directory.Identity,
                ConfirmPermanentDeletion = false,
                Recursive = true
            }));
        Require(
            noConfirmation.Kind == WorkspaceFailureKind.AccessDenied,
            "The local provider accepted an unconfirmed permanent deletion.");
        await local.DeleteAsync(
            directory,
            new WorkspaceDeleteConfirmation
            {
                ConfirmedLocationIdentity = directory.Identity,
                ConfirmPermanentDeletion = true,
                Recursive = true
            });
        Require(
            !Directory.Exists(directory.Path),
            "The confirmed recursive local deletion did not complete.");

        Console.WriteLine(
            "WORKSPACE_FILESYSTEM_OK|canonical-uri|local-provider|bounded-read|compare-before-write|atomic-replace|rename|stream-transfer|confirmed-delete");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name} was not thrown.");
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
}
