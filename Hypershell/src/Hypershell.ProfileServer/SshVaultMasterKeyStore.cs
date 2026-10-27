using System.Security.Cryptography;

namespace Hypershell.ProfileServer;

/// <summary>
/// Owns the server-held wrapping key used by the SSH credential vault. The key
/// is a separate, owner-readable file and is never stored in SQLite.
/// </summary>
public sealed class SshVaultMasterKeyStore
{
    public const int MasterKeyBytes = 32;

    private readonly object _gate = new();
    private readonly string _keyPath;
    private readonly bool _manageKeyFile;

    public SshVaultMasterKeyStore(ProfileServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        _keyPath = normalized.SshVaultMasterKeyPath
                   ?? throw new InvalidOperationException(
                       "The SSH vault master-key path is unavailable.");
        _manageKeyFile = normalized.ManageSshVaultMasterKeyFile;
    }

    public string KeyFileName => Path.GetFileName(_keyPath);

    public byte[] ReadOrCreate()
    {
        lock (_gate)
        {
            RejectReparsePoint(_keyPath);
            if (!File.Exists(_keyPath))
            {
                if (!_manageKeyFile)
                {
                    throw new FileNotFoundException(
                        "The externally provisioned SSH vault master-key file does not exist.",
                        _keyPath);
                }

                CreateKeyFile();
            }

            if (_manageKeyFile)
            {
                RestrictToOwner(_keyPath, isDirectory: false);
            }
            else
            {
                ValidateExternalKeyPermissions(_keyPath);
            }
            var key = File.ReadAllBytes(_keyPath);
            if (key.Length != MasterKeyBytes)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException(
                    "The SSH vault master-key file must contain exactly 32 bytes.");
            }

            return key;
        }
    }

    private void CreateKeyFile()
    {
        var directory = Path.GetDirectoryName(_keyPath)
                        ?? throw new InvalidOperationException(
                            "The SSH vault master-key path has no parent directory.");
        Directory.CreateDirectory(directory);
        RejectReparsePoint(directory);
        RestrictToOwner(directory, isDirectory: true);

        var key = RandomNumberGenerator.GetBytes(MasterKeyBytes);
        try
        {
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode =
                    UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            try
            {
                using var stream = new FileStream(_keyPath, streamOptions);
                stream.Write(key);
                stream.Flush(flushToDisk: true);
            }
            catch (IOException) when (File.Exists(_keyPath))
            {
                // Another initializing process won the CreateNew race. Its
                // exact key length and permissions are validated by the caller.
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "The SSH vault master-key path cannot be a reparse point or symbolic link.");
            }
        }
    }

    private static void RestrictToOwner(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            isDirectory
                ? UnixFileMode.UserRead
                  | UnixFileMode.UserWrite
                  | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void ValidateExternalKeyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode unsafePermissions =
            UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute;
        if ((mode & unsafePermissions) != 0)
        {
            throw new UnauthorizedAccessException(
                "The externally provisioned SSH vault master key is writable by its group or accessible to other users.");
        }

        if ((mode & (UnixFileMode.UserRead | UnixFileMode.GroupRead)) == 0)
        {
            throw new UnauthorizedAccessException(
                "The externally provisioned SSH vault master key is not readable by the service identity.");
        }
    }
}
