using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public enum HyperlibVaultImportDisposition
{
    Created,
    AlreadyPresent
}

public sealed record HyperlibVaultImportResult(
    HyperlibVaultImportDisposition Disposition,
    HyperlibVaultPackageSummary Package);

public sealed record StoredHyperlibVaultPackage(
    HyperlibVaultPackageSummary Package,
    string FullPath,
    string DownloadFileName);

/// <summary>
/// Stores inert personal Hyperlib archives by their deterministic unpacked
/// content hash. Executable extensions are deliberately excluded from this
/// vault and every downloaded archive is revalidated by the desktop before it
/// is activated.
/// </summary>
public sealed partial class HyperlibVaultStore
{
    public const string ArchiveExtension = ".hyperlib";
    public const int MaximumArchiveEntries = 4096;
    public const int MaximumManifestBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [GeneratedRegex(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(
        "^[A-Za-z][A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ContributionIdPattern();

    private readonly CloudMetadataStore _metadata;
    private readonly StorageCapacityService _storage;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public HyperlibVaultStore(
        CloudMetadataStore metadata,
        StorageCapacityService storage)
    {
        _metadata = metadata;
        _storage = storage;
    }

    public async Task<IReadOnlyList<HyperlibVaultPackageSummary>> ListAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.package_id, p.version, p.content_hash,
                   b.byte_length, p.is_official, p.created_at_utc
            FROM account_packages p
            JOIN package_blobs b ON b.content_hash = p.content_hash
            WHERE p.account_id = $account
            ORDER BY p.package_id, p.version;
            """;
        command.Parameters.AddWithValue("$account", account);
        var packages = new List<HyperlibVaultPackageSummary>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            packages.Add(ReadSummary(reader));
        }

        return packages;
    }

    public async Task<StoredHyperlibVaultPackage?> GetAsync(
        string accountId,
        string contentSha256,
        CancellationToken cancellationToken = default)
    {
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var hash = ValidateContentHash(contentSha256);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.package_id, p.version, p.content_hash,
                   b.byte_length, p.is_official, p.created_at_utc,
                   b.blob_relative_path
            FROM account_packages p
            JOIN package_blobs b ON b.content_hash = p.content_hash
            WHERE p.account_id = $account AND p.content_hash = $hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account", account);
        command.Parameters.AddWithValue("$hash", hash);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var package = ReadSummary(reader);
        var fullPath = ResolveRelativePath(reader.GetString(6));
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != package.ByteLength)
        {
            throw new InvalidDataException(
                "The stored Hyperlib archive is missing or truncated.");
        }

        return new StoredHyperlibVaultPackage(
            package,
            fullPath,
            $"{package.PackageId}-{package.Version}{ArchiveExtension}");
    }

    public async Task<HyperlibVaultImportResult> ImportAsync(
        string accountId,
        string packageId,
        string version,
        string contentSha256,
        Stream archiveStream,
        long? declaredLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        var account = ProfileServerIdentity.NormalizeAccountId(accountId);
        var expectedPackageId = ValidatePackageId(packageId);
        var expectedVersion = ValidateVersion(version);
        var expectedHash = ValidateContentHash(contentSha256);
        if (declaredLength is not long archiveLength
            || archiveLength <= 0
            || archiveLength
               > HypershellCloudContract.MaximumPersonalPackageBytes)
        {
            throw new InvalidDataException(
                "The Hyperlib archive has a missing or invalid Content-Length.");
        }

        if (!_storage.CanAccept(archiveLength))
        {
            throw new IOException(
                "The Hyperlib upload was rejected to preserve the configured free-space reserve.");
        }

        var operationRoot = Path.Combine(
            _metadata.DataRoot,
            "staging",
            "packages",
            Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(operationRoot, "upload.hyperlib");
        var packageRoot = Path.Combine(operationRoot, "package");
        Directory.CreateDirectory(operationRoot);
        RestrictDirectory(operationRoot);
        try
        {
            var archiveBytes = await WriteBoundedArchiveAsync(
                archiveStream,
                archivePath,
                cancellationToken);
            if (archiveBytes != archiveLength)
            {
                throw new InvalidDataException(
                    "The Hyperlib upload length did not match Content-Length.");
            }

            ExtractArchive(archivePath, packageRoot);
            var manifest = ValidateContentOnlyManifest(packageRoot);
            if (!string.Equals(
                    manifest.Id,
                    expectedPackageId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    manifest.Version,
                    expectedVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The route identity does not match the Hyperlib manifest.");
            }

            var actualHash = ComputePackageSha256(packageRoot);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(expectedHash)))
            {
                throw new InvalidDataException(
                    "The Hyperlib content hash does not match its route identity.");
            }

            await _mutationGate.WaitAsync(cancellationToken);
            try
            {
                await using var connection = _metadata.OpenConnection();
                await connection.OpenAsync(cancellationToken);
                await using var transaction = (SqliteTransaction)await connection
                    .BeginTransactionAsync(cancellationToken);
                var existing = await ReadAccountPackageAsync(
                    connection,
                    transaction,
                    account,
                    expectedPackageId,
                    expectedVersion,
                    cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(
                            existing.ContentSha256,
                            expectedHash,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "A personal package version is immutable and already maps to different content.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return new HyperlibVaultImportResult(
                        HyperlibVaultImportDisposition.AlreadyPresent,
                        existing);
                }

                var blob = await ReadBlobAsync(
                    connection,
                    transaction,
                    expectedHash,
                    cancellationToken);
                if (blob is null)
                {
                    var relativePath = StoreBlob(
                        archivePath,
                        expectedHash,
                        archiveBytes);
                    await InsertBlobAsync(
                        connection,
                        transaction,
                        expectedHash,
                        archiveBytes,
                        relativePath,
                        cancellationToken);
                }
                else
                {
                    var fullPath = ResolveRelativePath(blob.RelativePath);
                    var info = new FileInfo(fullPath);
                    if (!info.Exists || info.Length != blob.ByteLength)
                    {
                        throw new InvalidDataException(
                            "The content-addressed Hyperlib blob is missing or truncated.");
                    }
                }

                var createdAtUtc = DateTimeOffset.UtcNow;
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText =
                        """
                        INSERT INTO account_packages(
                            account_id, package_id, version, content_hash,
                            is_official, created_at_utc)
                        VALUES($account, $packageId, $version, $hash, 0,
                               $createdAtUtc);
                        """;
                    command.Parameters.AddWithValue("$account", account);
                    command.Parameters.AddWithValue(
                        "$packageId",
                        expectedPackageId);
                    command.Parameters.AddWithValue("$version", expectedVersion);
                    command.Parameters.AddWithValue("$hash", expectedHash);
                    command.Parameters.AddWithValue(
                        "$createdAtUtc",
                        createdAtUtc.ToString("O"));
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return new HyperlibVaultImportResult(
                    HyperlibVaultImportDisposition.Created,
                    new HyperlibVaultPackageSummary
                    {
                        PackageId = expectedPackageId,
                        Version = expectedVersion,
                        ContentSha256 = expectedHash,
                        ByteLength = archiveBytes,
                        IsOfficial = false,
                        CreatedAtUtc = createdAtUtc
                    });
            }
            finally
            {
                _mutationGate.Release();
            }
        }
        finally
        {
            TryDeleteDirectory(operationRoot);
        }
    }

    private static async Task<long> WriteBoundedArchiveAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read > HypershellCloudContract.MaximumPersonalPackageBytes
                       - total)
            {
                throw new InvalidDataException(
                    "The Hyperlib archive exceeds the 256 MiB upload limit.");
            }

            total += read;
            await destination.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
        return total;
    }

    private static void ExtractArchive(
        string archivePath,
        string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var root = NormalizeDirectory(destinationDirectory);
        var containmentPrefix = root + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;
        var entryCount = 0;
        long totalBytes = 0;
        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            entryCount++;
            if (entryCount > MaximumArchiveEntries)
            {
                throw new InvalidDataException(
                    $"The archive exceeds the {MaximumArchiveEntries:N0}-entry limit.");
            }

            var archiveName = entry.FullName.Replace('\\', '/');
            var segments = archiveName.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
            if (string.IsNullOrWhiteSpace(archiveName)
                || archiveName.StartsWith("/", StringComparison.Ordinal)
                || segments.Length == 0
                || segments.Any(segment =>
                    segment is "." or ".."
                    || segment.Contains(':')
                    || segment.EndsWith(' ')
                    || segment.EndsWith('.')))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' is not a safe portable path.");
            }

            var isDirectory = archiveName.EndsWith("/", StringComparison.Ordinal);
            var relativeName = archiveName.TrimEnd('/');
            if (!seen.Add(relativeName))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' duplicates another path.");
            }

            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' is a symbolic link or reparse point.");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(
                root,
                relativeName.Replace('/', Path.DirectorySeparatorChar)));
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
            if (fileCount > MaximumArchiveEntries
                || entry.Length < 0
                || entry.Length
                > HypershellCloudContract.MaximumPersonalPackageBytes
                  - totalBytes)
            {
                throw new InvalidDataException(
                    "The archive exceeds the package file-count or uncompressed-size limit.");
            }

            totalBytes += entry.Length;
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath) ?? root);
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

    private static VaultExtensionManifest ValidateContentOnlyManifest(
        string packageRoot)
    {
        var manifestPath = Path.Combine(
            packageRoot,
            "hypershell.extension.json");
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "The archive must contain a bounded hypershell.extension.json at its root.");
        }

        VaultExtensionManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<VaultExtensionManifest>(
                           File.ReadAllBytes(manifestPath),
                           ManifestJsonOptions)
                       ?? throw new InvalidDataException(
                           "The Hyperlib manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The Hyperlib manifest is malformed or contains unknown fields.",
                exception);
        }

        if (manifest.SchemaVersion != 1
            || !PackageIdPattern().IsMatch(manifest.Id)
            || !VersionPattern().IsMatch(manifest.Version)
            || !ValidText(manifest.DisplayName, 80)
            || !ValidText(manifest.Publisher, 80)
            || !ValidText(manifest.Description, 500, allowEmpty: true)
            || !string.Equals(
                manifest.Runtime,
                "ContentOnly",
                StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            throw new InvalidDataException(
                "The Hyperlib manifest identity, version, or ContentOnly runtime is invalid.");
        }

        var capabilities = manifest.RequestedCapabilities;
        if (capabilities.Count != capabilities.Distinct(StringComparer.Ordinal).Count()
            || !capabilities.Contains("LibraryContent", StringComparer.Ordinal)
            || capabilities.Any(capability =>
                capability is not ("LibraryContent" or "DocumentProvider")))
        {
            throw new InvalidDataException(
                "A personal Hyperlib package may request only LibraryContent and TextEditor document metadata.");
        }

        var contributions = manifest.Contributions;
        if (contributions.Libraries.Count == 0
            || contributions.Panels.Count > 0
            || contributions.Commands.Count > 0
            || contributions.AiTools.Count > 0
            || contributions.DocumentFormats.Any(format =>
                !string.Equals(format.Kind, "TextEditor", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "A personal Hyperlib package must contribute static libraries and may add TextEditor metadata only.");
        }

        var contributionIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var library in contributions.Libraries)
        {
            if (!ContributionIdPattern().IsMatch(library.Id)
                || !contributionIds.Add(library.Id)
                || !ValidText(library.DisplayName, 80)
                || library.LanguageIds.Count == 0
                || library.LanguageIds.Any(language =>
                    !ValidText(language, 80))
                || library.LanguageIds.Count
                   != library.LanguageIds.Distinct(
                       StringComparer.OrdinalIgnoreCase).Count()
                || !ValidText(library.Provenance, 500)
                || library.ContentOrigin is not
                    ("HumanAuthored" or "AiAssisted" or "AiGenerated" or "Imported")
                || library.ReviewState is not ("Draft" or "Reviewed")
                || library.ReviewState == "Reviewed"
                   && !ValidText(library.ReviewedBy, 120))
            {
                throw new InvalidDataException(
                    "A Hyperlib library contribution contains invalid identity or provenance metadata.");
            }

            ValidateContentDirectory(packageRoot, library.ContentPath);
            if (!string.IsNullOrWhiteSpace(library.SourceUrl)
                && (!Uri.TryCreate(
                        library.SourceUrl,
                        UriKind.Absolute,
                        out var sourceUri)
                    || sourceUri.Scheme is not ("http" or "https")
                    || !string.IsNullOrEmpty(sourceUri.UserInfo)))
            {
                throw new InvalidDataException(
                    "A Hyperlib source URL must be credential-free HTTP or HTTPS.");
            }
        }

        return manifest;
    }

    private static void ValidateContentDirectory(
        string packageRoot,
        string contentPath)
    {
        if (string.IsNullOrWhiteSpace(contentPath)
            || Path.IsPathRooted(contentPath)
            || contentPath.Contains(':')
            || contentPath.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(
                "A Hyperlib content path must be package-relative.");
        }

        var root = NormalizeDirectory(packageRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, contentPath));
        if (!candidate.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(candidate))
        {
            throw new InvalidDataException(
                "A Hyperlib content path is missing or escapes the package root.");
        }
    }

    private static string ComputePackageSha256(string packageDirectory)
    {
        var root = NormalizeDirectory(packageDirectory);
        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new PackageFile(
                path,
                Path.GetRelativePath(root, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                new FileInfo(path).Length))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
        if (files.Count > MaximumArchiveEntries
            || files.Sum(file => file.Length)
               > HypershellCloudContract.MaximumPersonalPackageBytes)
        {
            throw new InvalidDataException(
                "The extracted package exceeds the file-count or size limit.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var lengthBytes = new byte[sizeof(long)];
        var buffer = new byte[81920];
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData([0]);
            BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, file.Length);
            hash.AppendData(lengthBytes);
            using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private string StoreBlob(
        string sourcePath,
        string hash,
        long byteLength)
    {
        var directory = Path.Combine(
            _metadata.BlobRoot,
            "packages",
            hash[..2]);
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);
        var destination = Path.Combine(directory, hash + ArchiveExtension);
        if (File.Exists(destination))
        {
            var existing = new FileInfo(destination);
            if (existing.Length != byteLength)
            {
                throw new InvalidDataException(
                    "An orphaned Hyperlib blob conflicts with the uploaded archive.");
            }

            return ToRelativePath(destination);
        }

        var temporary = Path.Combine(
            directory,
            $".{hash}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, temporary, overwrite: false);
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: false);
            RestrictFile(destination);
            return ToRelativePath(destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task InsertBlobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string hash,
        long byteLength,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO package_blobs(
                content_hash, byte_length, blob_relative_path, created_at_utc)
            VALUES($hash, $byteLength, $path, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$byteLength", byteLength);
        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PackageBlobRow?> ReadBlobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string hash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT byte_length, blob_relative_path
            FROM package_blobs
            WHERE content_hash = $hash;
            """;
        command.Parameters.AddWithValue("$hash", hash);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PackageBlobRow(reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    private static async Task<HyperlibVaultPackageSummary?>
        ReadAccountPackageAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string account,
            string packageId,
            string version,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT p.package_id, p.version, p.content_hash,
                   b.byte_length, p.is_official, p.created_at_utc
            FROM account_packages p
            JOIN package_blobs b ON b.content_hash = p.content_hash
            WHERE p.account_id = $account
              AND p.package_id = $packageId
              AND p.version = $version;
            """;
        command.Parameters.AddWithValue("$account", account);
        command.Parameters.AddWithValue("$packageId", packageId);
        command.Parameters.AddWithValue("$version", version);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadSummary(reader)
            : null;
    }

    private static HyperlibVaultPackageSummary ReadSummary(
        SqliteDataReader reader) => new()
        {
            PackageId = reader.GetString(0),
            Version = reader.GetString(1),
            ContentSha256 = reader.GetString(2),
            ByteLength = reader.GetInt64(3),
            IsOfficial = reader.GetInt64(4) != 0,
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(5))
        };

    private string ToRelativePath(string fullPath) =>
        Path.GetRelativePath(_metadata.DataRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    private string ResolveRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':'))
        {
            throw new InvalidDataException(
                "A stored Hyperlib blob path is malformed.");
        }

        var root = NormalizeDirectory(_metadata.DataRoot);
        var fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A stored Hyperlib blob path escapes the data root.");
        }

        return fullPath;
    }

    private static string ValidatePackageId(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!PackageIdPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "The Hyperlib package ID is malformed.",
                nameof(value));
        }

        return normalized;
    }

    private static string ValidateVersion(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!VersionPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "The Hyperlib package version is malformed.",
                nameof(value));
        }

        return normalized;
    }

    private static string ValidateContentHash(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length != 64
            || !normalized.All(char.IsAsciiHexDigit)
            || !string.Equals(
                normalized,
                normalized.ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Hyperlib content SHA-256 is malformed.",
                nameof(value));
        }

        return normalized;
    }

    private static bool ValidText(
        string value,
        int maximumLength,
        bool allowEmpty = false) =>
        value is not null
        && value.Length <= maximumLength
        && (allowEmpty || !string.IsNullOrWhiteSpace(value))
        && !value.Any(character => character == '\0' || char.IsControl(character));

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & unixFileTypeMask;
        return unixMode == unixSymbolicLink
               || (entry.ExternalAttributes
                   & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
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

    private sealed record PackageFile(
        string FullPath,
        string RelativePath,
        long Length);

    private sealed record PackageBlobRow(
        long ByteLength,
        string RelativePath);

    private sealed record VaultExtensionManifest
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = string.Empty;

        public int SchemaVersion { get; init; }

        public string Id { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Publisher { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Runtime { get; init; } = string.Empty;

        public string EntryPoint { get; init; } = string.Empty;

        public IReadOnlyList<string> RequestedCapabilities { get; init; } = [];

        public VaultContributions Contributions { get; init; } = new();
    }

    private sealed record VaultContributions
    {
        public IReadOnlyList<VaultLibraryContribution> Libraries { get; init; } = [];

        public IReadOnlyList<JsonElement> Panels { get; init; } = [];

        public IReadOnlyList<VaultDocumentFormatContribution> DocumentFormats { get; init; } = [];

        public IReadOnlyList<JsonElement> Commands { get; init; } = [];

        public IReadOnlyList<JsonElement> AiTools { get; init; } = [];
    }

    private sealed record VaultLibraryContribution
    {
        public string Id { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string ContentPath { get; init; } = string.Empty;

        public IReadOnlyList<string> LanguageIds { get; init; } = [];

        public string ContentOrigin { get; init; } = string.Empty;

        public string ReviewState { get; init; } = string.Empty;

        public string Provenance { get; init; } = string.Empty;

        public string ReviewedBy { get; init; } = string.Empty;

        public string SourceUrl { get; init; } = string.Empty;
    }

    private sealed record VaultDocumentFormatContribution
    {
        public string Id { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public IReadOnlyList<string> FileExtensions { get; init; } = [];

        public string LanguageId { get; init; } = string.Empty;

        public string ViewResource { get; init; } = string.Empty;
    }
}
