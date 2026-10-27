using Hypershell.Extensibility;
using System.IO;
using System.IO.Compression;

namespace Hypershell.Services;

public enum HyperlibPackageInstallDisposition
{
    Install,
    Update,
    AlreadyInstalled
}

public sealed record HyperlibPackageInstallPreview(
    string SourcePath,
    string ExtensionId,
    string DisplayName,
    string Version,
    string PackageSha256,
    HyperlibPackageInstallDisposition Disposition,
    string ExistingVersion,
    string ExistingPackageSha256)
{
    public bool RequiresUpdateConfirmation =>
        Disposition == HyperlibPackageInstallDisposition.Update;
}

public sealed record HyperlibPackageInstallResult(
    HyperlibPackageInstallDisposition Disposition,
    string ExtensionId,
    string DisplayName,
    string Version,
    string PackageSha256,
    string InstallDirectory,
    string RecoveryDirectory);

public sealed record HyperlibPackageArchiveResult(
    string ExtensionId,
    string DisplayName,
    string Version,
    string PackageSha256,
    string ArchivePath,
    int FileCount,
    long UncompressedBytes);

public sealed record HyperlibPackageRemovalResult(
    string ExtensionId,
    string DisplayName,
    string Version,
    string RecoveryDirectory);

/// <summary>
/// Creates and installs inert Hyperlib archives. The archive contract is
/// deliberately limited to validated ContentOnly packages; executable WebView
/// and native extension distribution remains outside this trust boundary.
/// </summary>
public sealed class HyperlibPackageService
{
    public const string ArchiveExtension = ".hyperlib";

    private readonly string _extensionRoot;
    private readonly string _stagingRoot;
    private readonly string _recoveryRoot;
    private readonly object _operationGate = new();

    public HyperlibPackageService(
        string extensionRoot,
        string stagingRoot,
        string recoveryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryRoot);
        _extensionRoot = NormalizeDirectory(extensionRoot);
        _stagingRoot = NormalizeDirectory(stagingRoot);
        _recoveryRoot = NormalizeDirectory(recoveryRoot);
    }

    public HyperlibPackageInstallPreview InspectInstallSource(
        string sourcePath)
    {
        lock (_operationGate)
        {
            using var prepared = PrepareSource(sourcePath);
            var existing = FindExistingPackage(prepared.Package);
            var disposition = existing is null
                ? HyperlibPackageInstallDisposition.Install
                : string.Equals(
                    existing.PackageSha256,
                    prepared.Package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase)
                    ? HyperlibPackageInstallDisposition.AlreadyInstalled
                    : HyperlibPackageInstallDisposition.Update;
            var manifest = prepared.Package.Manifest!;
            return new HyperlibPackageInstallPreview(
                Path.GetFullPath(sourcePath),
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                prepared.Package.PackageSha256,
                disposition,
                existing?.Manifest?.Version ?? string.Empty,
                existing?.PackageSha256 ?? string.Empty);
        }
    }

    public HyperlibPackageInstallResult Install(
        string sourcePath,
        bool allowUpdate)
    {
        lock (_operationGate)
        {
            using var prepared = PrepareSource(sourcePath);
            var manifest = prepared.Package.Manifest!;
            Directory.CreateDirectory(_extensionRoot);
            var destination = Path.Combine(_extensionRoot, manifest.Id);
            var existing = FindExistingPackage(prepared.Package);
            if (existing is not null
                && string.Equals(
                    existing.PackageSha256,
                    prepared.Package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new HyperlibPackageInstallResult(
                    HyperlibPackageInstallDisposition.AlreadyInstalled,
                    manifest.Id,
                    manifest.DisplayName,
                    manifest.Version,
                    prepared.Package.PackageSha256,
                    existing.PackageDirectory,
                    string.Empty);
            }

            var destinationExists = Directory.Exists(destination);
            if ((existing is not null || destinationExists) && !allowUpdate)
            {
                throw new InvalidOperationException(
                    $"Hyperlib package '{manifest.Id}' is already installed with different content. Confirm the update before replacing it.");
            }

            if (existing is not null
                && !string.Equals(
                    existing.PackageDirectory,
                    destination,
                    StringComparison.OrdinalIgnoreCase)
                && destinationExists)
            {
                throw new InvalidOperationException(
                    $"Cannot update '{manifest.Id}' because both its current folder and canonical destination exist. Remove the duplicate package folders first.");
            }

            var previousDirectory = existing?.PackageDirectory
                                    ?? (destinationExists
                                        ? destination
                                        : string.Empty);
            var recoveryDirectory = string.Empty;
            if (!string.IsNullOrWhiteSpace(previousDirectory))
            {
                recoveryDirectory = CreateRecoveryPath(
                    manifest.Id,
                    existing?.Manifest?.Version ?? "unknown");
                Directory.CreateDirectory(_recoveryRoot);
                Directory.Move(previousDirectory, recoveryDirectory);
            }

            try
            {
                Directory.Move(prepared.PackageDirectory, destination);
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(recoveryDirectory)
                    && Directory.Exists(recoveryDirectory)
                    && !Directory.Exists(previousDirectory))
                {
                    Directory.Move(recoveryDirectory, previousDirectory);
                }

                throw;
            }

            return new HyperlibPackageInstallResult(
                string.IsNullOrWhiteSpace(previousDirectory)
                    ? HyperlibPackageInstallDisposition.Install
                    : HyperlibPackageInstallDisposition.Update,
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                prepared.Package.PackageSha256,
                destination,
                recoveryDirectory);
        }
    }

    public HyperlibPackageArchiveResult Pack(
        string sourceDirectory,
        string destinationPath)
    {
        lock (_operationGate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
            var source = NormalizeDirectory(sourceDirectory);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    $"The Hyperlib source folder does not exist: {source}");
            }

            var package = ValidateContentOnlyPackage(source);
            var destination = EnsureArchiveExtension(
                Path.GetFullPath(destinationPath));
            if (IsContainedBy(source, destination))
            {
                throw new InvalidOperationException(
                    "Save the archive outside the source package folder so it cannot include itself.");
            }

            var files = EnumeratePackageFiles(source);
            var destinationDirectory = Path.GetDirectoryName(destination)
                                       ?? throw new InvalidOperationException(
                                           "The archive destination has no parent directory.");
            Directory.CreateDirectory(destinationDirectory);
            var temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.ReadWrite,
                           FileShare.None))
                using (var archive = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           leaveOpen: false))
                {
                    foreach (var file in files)
                    {
                        var entry = archive.CreateEntry(
                            file.RelativePath,
                            CompressionLevel.Optimal);
                        entry.LastWriteTime = new DateTimeOffset(
                            1980,
                            1,
                            1,
                            0,
                            0,
                            0,
                            TimeSpan.Zero);
                        using var input = new FileStream(
                            file.FullPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read);
                        using var output = entry.Open();
                        input.CopyTo(output);
                    }
                }

                File.Move(temporaryPath, destination, overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            var manifest = package.Manifest!;
            return new HyperlibPackageArchiveResult(
                manifest.Id,
                manifest.DisplayName,
                manifest.Version,
                package.PackageSha256,
                destination,
                files.Count,
                files.Sum(file => file.Length));
        }
    }

    public HyperlibPackageRemovalResult Remove(
        DiscoveredExtensionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (_operationGate)
        {
            var packageDirectory = NormalizeDirectory(package.PackageDirectory);
            EnsureDirectChild(_extensionRoot, packageDirectory);
            if (!Directory.Exists(packageDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The installed package folder no longer exists: {packageDirectory}");
            }

            var extensionId = package.Manifest?.Id
                              ?? Path.GetFileName(packageDirectory);
            var displayName = package.Manifest?.DisplayName ?? extensionId;
            var version = package.Manifest?.Version ?? "unknown";
            Directory.CreateDirectory(_recoveryRoot);
            var recoveryDirectory = CreateRecoveryPath(extensionId, version);
            Directory.Move(packageDirectory, recoveryDirectory);
            return new HyperlibPackageRemovalResult(
                extensionId,
                displayName,
                version,
                recoveryDirectory);
        }
    }

    private PreparedPackage PrepareSource(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Directory.CreateDirectory(_stagingRoot);
        var operationDirectory = Path.Combine(
            _stagingRoot,
            Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(operationDirectory, "package");
        Directory.CreateDirectory(operationDirectory);
        try
        {
            if (Directory.Exists(sourcePath))
            {
                CopyPackageDirectory(
                    Path.GetFullPath(sourcePath),
                    packageDirectory);
            }
            else if (File.Exists(sourcePath))
            {
                var archivePath = Path.GetFullPath(sourcePath);
                if (!string.Equals(
                        Path.GetExtension(archivePath),
                        ArchiveExtension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Choose a {ArchiveExtension} archive or a package folder.");
                }

                ExtractArchive(archivePath, packageDirectory);
            }
            else
            {
                throw new FileNotFoundException(
                    "The selected Hyperlib package source could not be found.",
                    sourcePath);
            }

            var package = ValidateContentOnlyPackage(packageDirectory);
            return new PreparedPackage(
                operationDirectory,
                packageDirectory,
                package);
        }
        catch
        {
            TryDeleteDirectory(operationDirectory);
            throw;
        }
    }

    private DiscoveredExtensionPackage ValidateContentOnlyPackage(
        string packageDirectory)
    {
        var package = new ExtensionDiscoveryService(
                Path.GetDirectoryName(packageDirectory)
                ?? packageDirectory)
            .InspectPackage(packageDirectory);
        if (package.Status != ExtensionPackageStatus.Validated)
        {
            throw new InvalidDataException(BuildValidationFailure(package));
        }

        if (package.Manifest?.Runtime != ExtensionRuntimeKind.ContentOnly)
        {
            throw new InvalidDataException(
                "The .hyperlib lifecycle accepts ContentOnly packages only. WebView and native extensions require a separate trust and distribution workflow.");
        }

        if (package.Manifest.Contributions.Libraries.Count == 0)
        {
            throw new InvalidDataException(
                "A .hyperlib package must contribute at least one validated Hyperlib library.");
        }

        if (string.IsNullOrWhiteSpace(package.PackageSha256))
        {
            throw new InvalidDataException(
                "The package passed validation but did not produce a content hash.");
        }

        return package;
    }

    private DiscoveredExtensionPackage? FindExistingPackage(
        DiscoveredExtensionPackage sourcePackage)
    {
        var extensionId = sourcePackage.Manifest!.Id;
        if (!Directory.Exists(_extensionRoot))
        {
            return null;
        }

        var snapshot = new ExtensionDiscoveryService(_extensionRoot).Discover();
        var matches = snapshot.Packages
            .Where(candidate => string.Equals(
                candidate.Manifest?.Id,
                extensionId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"More than one installed folder declares '{extensionId}'. Resolve the duplicate packages before installing an update.");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        var canonicalDirectory = Path.Combine(_extensionRoot, extensionId);
        return Directory.Exists(canonicalDirectory)
            ? new ExtensionDiscoveryService(_extensionRoot)
                .InspectPackage(canonicalDirectory)
            : null;
    }

    private static List<PackageFile> EnumeratePackageFiles(
        string packageDirectory)
    {
        var root = NormalizeDirectory(packageDirectory);
        var files = new List<PackageFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            RejectReparsePoint(directory);
            foreach (var child in Directory
                         .EnumerateDirectories(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparsePoint(child);
                pending.Push(child);
            }

            foreach (var filePath in Directory
                         .EnumerateFiles(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                RejectReparsePoint(filePath);
                var length = new FileInfo(filePath).Length;
                if (length > ExtensionDiscoveryService.MaximumPackageBytes
                             - totalBytes)
                {
                    throw new InvalidDataException(
                        $"The package exceeds the {ExtensionDiscoveryService.MaximumPackageBytes / 1024 / 1024:N0} MiB limit.");
                }

                totalBytes += length;
                files.Add(new PackageFile(
                    filePath,
                    Path.GetRelativePath(root, filePath)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    length));
                if (files.Count > ExtensionDiscoveryService.MaximumPackageFiles)
                {
                    throw new InvalidDataException(
                        $"The package exceeds the {ExtensionDiscoveryService.MaximumPackageFiles:N0}-file limit.");
                }
            }
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static void CopyPackageDirectory(
        string sourceDirectory,
        string destinationDirectory)
    {
        var files = EnumeratePackageFiles(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in files)
        {
            var destinationPath = Path.Combine(
                destinationDirectory,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath)
                ?? destinationDirectory);
            File.Copy(file.FullPath, destinationPath, overwrite: false);
        }
    }

    private static void ExtractArchive(
        string archivePath,
        string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = NormalizeDirectory(destinationDirectory);
        var containmentPrefix = destinationRoot + Path.DirectorySeparatorChar;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;
        long totalBytes = 0;
        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entryCount = 0;
        foreach (var entry in archive.Entries)
        {
            entryCount++;
            if (entryCount > ExtensionDiscoveryService.MaximumPackageFiles)
            {
                throw new InvalidDataException(
                    $"The archive exceeds the {ExtensionDiscoveryService.MaximumPackageFiles:N0}-entry limit.");
            }

            var archiveName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(archiveName)
                || archiveName.StartsWith("/", StringComparison.Ordinal)
                || archiveName.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => segment is "." or ".."))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' is not a safe package-relative path.");
            }

            var isDirectory = archiveName.EndsWith("/", StringComparison.Ordinal);
            var relativeName = archiveName.TrimEnd('/');
            if (!seenPaths.Add(relativeName))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' duplicates another path.");
            }

            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' is a symbolic link or reparse point.");
            }

            string destinationPath;
            try
            {
                destinationPath = Path.GetFullPath(Path.Combine(
                    destinationRoot,
                    relativeName.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' has an invalid path.",
                    exception);
            }

            if (!destinationPath.StartsWith(
                    containmentPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' escapes the package root.");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            fileCount++;
            if (fileCount > ExtensionDiscoveryService.MaximumPackageFiles
                || entry.Length > ExtensionDiscoveryService.MaximumPackageBytes
                                  - totalBytes)
            {
                throw new InvalidDataException(
                    "The archive exceeds Hypershell's package file-count or uncompressed-size limit.");
            }

            totalBytes += entry.Length;
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath)
                ?? destinationRoot);
            using var input = entry.Open();
            using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            input.CopyTo(output);
            if (output.Length != entry.Length)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' did not extract to its declared size.");
            }
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & unixFileTypeMask;
        return unixMode == unixSymbolicLink
               || (entry.ExternalAttributes
                   & (int)FileAttributes.ReparsePoint) != 0;
    }

    private string CreateRecoveryPath(string extensionId, string version)
    {
        var safeId = SanitizePathSegment(extensionId, "package");
        var safeVersion = SanitizePathSegment(version, "unknown");
        var baseName = $"{safeId}-{safeVersion}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var candidate = Path.Combine(_recoveryRoot, baseName);
        return !Directory.Exists(candidate)
            ? candidate
            : Path.Combine(
                _recoveryRoot,
                $"{baseName}-{Guid.NewGuid():N}");
    }

    private static string BuildValidationFailure(
        DiscoveredExtensionPackage package)
    {
        var diagnostics = package.Diagnostics
            .Where(diagnostic =>
                diagnostic.Severity == ExtensionDiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.DisplayText)
            .Take(8)
            .ToList();
        return diagnostics.Count == 0
            ? "The selected package did not pass Hyperlib validation."
            : "The selected package did not pass Hyperlib validation:\n\n"
              + string.Join("\n", diagnostics);
    }

    private static void EnsureDirectChild(string root, string candidate)
    {
        var parent = Path.GetDirectoryName(candidate);
        if (!string.Equals(
                NormalizeDirectory(parent ?? string.Empty),
                NormalizeDirectory(root),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only a direct child of Hypershell's extension folder can be removed by the package manager.");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Package content cannot contain symbolic links or reparse points: {path}");
        }
    }

    private static bool IsContainedBy(string directory, string path)
    {
        var root = NormalizeDirectory(directory)
                   + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(
            root,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureArchiveExtension(string path) =>
        string.Equals(
            Path.GetExtension(path),
            ArchiveExtension,
            StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ArchiveExtension;

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

    private static string SanitizePathSegment(
        string value,
        string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string((value ?? string.Empty)
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PackageFile(
        string FullPath,
        string RelativePath,
        long Length);

    private sealed class PreparedPackage : IDisposable
    {
        public PreparedPackage(
            string operationDirectory,
            string packageDirectory,
            DiscoveredExtensionPackage package)
        {
            OperationDirectory = operationDirectory;
            PackageDirectory = packageDirectory;
            Package = package;
        }

        public string OperationDirectory { get; }
        public string PackageDirectory { get; }
        public DiscoveredExtensionPackage Package { get; }

        public void Dispose() => TryDeleteDirectory(OperationDirectory);
    }
}
