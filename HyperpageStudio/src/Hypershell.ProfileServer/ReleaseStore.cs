using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.ProfileServer;

public enum ReleaseImportDisposition
{
    Created,
    AlreadyPresent
}

public sealed record ReleaseImportResult(
    ReleaseImportDisposition Disposition,
    HypershellReleaseSummary Release);

public sealed record StoredReleaseArtifact(
    HypershellReleaseArtifact Artifact,
    string FullPath);

public sealed record StoredHypershellRelease(
    HypershellReleaseSummary Summary,
    SignedHypershellReleaseManifest SignedManifest,
    IReadOnlyList<StoredReleaseArtifact> Artifacts);

public sealed class ReleaseStore
{
    public const string SignedManifestFileName = "release-manifest.json";

    private readonly CloudMetadataStore _metadata;
    private readonly StorageCapacityService _storage;
    private readonly ProfileServerOptions _options;
    private readonly HypershellReleaseManifestSecurity _security = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public ReleaseStore(
        CloudMetadataStore metadata,
        StorageCapacityService storage,
        ProfileServerOptions options)
    {
        _metadata = metadata;
        _storage = storage;
        _options = options.Normalize();
    }

    public async Task<ReleaseImportResult> ImportBundleAsync(
        string bundleDirectory,
        bool pinRelease,
        CancellationToken cancellationToken = default)
    {
        return await ImportBundleAsync(
            bundleDirectory,
            pinRelease,
            expectedGitCommit: null,
            cancellationToken);
    }

    public async Task<ReleaseImportResult> ImportBundleAsync(
        string bundleDirectory,
        bool pinRelease,
        string? expectedGitCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        var normalizedExpectedGitCommit = NormalizeExpectedGitCommit(
            expectedGitCommit);
        var bundleRoot = Path.GetFullPath(bundleDirectory);
        if (!Directory.Exists(bundleRoot))
        {
            throw new DirectoryNotFoundException(
                $"Release bundle '{bundleRoot}' does not exist.");
        }

        var signedManifestPath = Path.Combine(
            bundleRoot,
            SignedManifestFileName);
        var signedManifestBytes = ReadBoundedFile(
            signedManifestPath,
            HypershellReleaseContract.MaximumSignedManifestBytes);
        var signedManifest = _security.DeserializeEnvelope(
            signedManifestBytes);
        using var verified = new VerifiedReleaseLease(
            _security.Verify(
                signedManifest,
                _options.TrustedReleaseSignerSha256,
                requireTrustedCertificateChain: false));
        var manifest = verified.Value.Manifest;
        if (normalizedExpectedGitCommit is not null
            && !string.Equals(
                manifest.GitCommit,
                normalizedExpectedGitCommit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The signed release does not match the expected server repository commit.");
        }

        var artifacts = new List<(HypershellReleaseArtifact Metadata, string SourcePath)>();
        long pendingBytes = signedManifestBytes.LongLength;
        foreach (var artifact in manifest.Artifacts)
        {
            var sourcePath = Path.GetFullPath(
                Path.Combine(bundleRoot, artifact.FileName));
            EnsureContained(bundleRoot, sourcePath);
            _security.VerifyArtifact(sourcePath, artifact);
            pendingBytes = checked(pendingBytes + artifact.ByteLength);
            artifacts.Add((artifact, sourcePath));
        }

        if (!_storage.CanAccept(pendingBytes))
        {
            throw new IOException(
                "The release was rejected to preserve the server's configured free-space reserve.");
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var manifestJson = Encoding.UTF8.GetString(
                signedManifestBytes);
            await using var connection = _metadata.OpenConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken);
            var existing = await ReadReleaseRowAsync(
                connection,
                transaction,
                manifest.Version,
                cancellationToken);
            ReleaseImportDisposition disposition;
            if (existing is not null)
            {
                if (!string.Equals(
                        existing.ManifestJson,
                        manifestJson,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Release {manifest.Version} is immutable and already has different signed metadata.");
                }

                if (pinRelease && !existing.IsPinned)
                {
                    await SetPinnedAsync(
                        connection,
                        transaction,
                        manifest.Version,
                        cancellationToken);
                }

                disposition = ReleaseImportDisposition.AlreadyPresent;
            }
            else
            {
                var storedPaths = new Dictionary<
                    HypershellReleaseArtifactKind,
                    string>();
                foreach (var artifact in artifacts)
                {
                    storedPaths[artifact.Metadata.Kind] = StoreBlob(
                        artifact.SourcePath,
                        artifact.Metadata);
                }

                await InsertReleaseAsync(
                    connection,
                    transaction,
                    manifest,
                    manifestJson,
                    storedPaths,
                    pinRelease,
                    cancellationToken);
                disposition = ReleaseImportDisposition.Created;
            }

            await transaction.CommitAsync(cancellationToken);
            await PruneAsync(cancellationToken);
            var stored = await GetAsync(
                manifest.Version,
                cancellationToken)
                ?? throw new InvalidDataException(
                    "The imported release could not be read back.");
            return new ReleaseImportResult(
                disposition,
                stored.Summary);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<IReadOnlyList<HypershellReleaseSummary>> ListAsync(
        string? channel = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannel = NormalizeOptionalChannel(channel);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = normalizedChannel is null
            ? """
              SELECT version, channel, git_commit, manifest_json,
                     published_at_utc, is_pinned
              FROM releases
              ORDER BY published_at_utc DESC, version DESC;
              """
            : """
              SELECT version, channel, git_commit, manifest_json,
                     published_at_utc, is_pinned
              FROM releases
              WHERE channel = $channel
              ORDER BY published_at_utc DESC, version DESC;
              """;
        if (normalizedChannel is not null)
        {
            command.Parameters.AddWithValue("$channel", normalizedChannel);
        }

        var releases = new List<HypershellReleaseSummary>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            releases.Add(ReadSummary(reader));
        }

        return releases;
    }

    public async Task<StoredHypershellRelease?> GetLatestAsync(
        string channel,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannel = NormalizeRequiredChannel(channel);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version
            FROM releases
            WHERE channel = $channel
            ORDER BY published_at_utc DESC, version DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$channel", normalizedChannel);
        var version = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(version)
            ? null
            : await GetAsync(version, cancellationToken);
    }

    public async Task<StoredHypershellRelease?> GetAsync(
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidateVersion(version);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var row = await ReadReleaseRowAsync(
            connection,
            transaction: null,
            version,
            cancellationToken);
        return row is null ? null : Materialize(row);
    }

    private string StoreBlob(
        string sourcePath,
        HypershellReleaseArtifact artifact)
    {
        var directory = Path.Combine(
            _metadata.BlobRoot,
            "releases",
            artifact.Sha256[..2],
            artifact.Sha256);
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);
        var destination = Path.Combine(directory, artifact.FileName);
        if (File.Exists(destination))
        {
            _security.VerifyArtifact(destination, artifact);
            return ToRelativePath(destination);
        }

        var temporary = Path.Combine(
            directory,
            $".{artifact.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       128 * 1024,
                       FileOptions.SequentialScan))
            using (var target = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       128 * 1024,
                       FileOptions.WriteThrough))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: false);
            RestrictFile(destination);
            _security.VerifyArtifact(destination, artifact);
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

    private StoredHypershellRelease Materialize(ReleaseRow row)
    {
        var bytes = Encoding.UTF8.GetBytes(row.ManifestJson);
        var envelope = _security.DeserializeEnvelope(bytes);
        using var verified = new VerifiedReleaseLease(
            _security.Verify(
                envelope,
                _options.TrustedReleaseSignerSha256,
                requireTrustedCertificateChain: false));
        var manifest = verified.Value.Manifest;
        if (!string.Equals(
                manifest.Version,
                row.Version,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.Channel,
                row.Channel,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.GitCommit,
                row.GitCommit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stored release metadata does not match its signed manifest.");
        }

        var paths = new Dictionary<HypershellReleaseArtifactKind, string>
        {
            [HypershellReleaseArtifactKind.Installer] =
                row.InstallerBlobPath
        };
        if (!string.IsNullOrWhiteSpace(row.PortableBlobPath))
        {
            paths[HypershellReleaseArtifactKind.Portable] =
                row.PortableBlobPath;
        }

        var artifacts = manifest.Artifacts
            .Select(artifact =>
            {
                if (!paths.TryGetValue(artifact.Kind, out var relativePath))
                {
                    throw new InvalidDataException(
                        "A stored release is missing one of its signed artifact paths.");
                }

                var fullPath = ResolveRelativePath(relativePath);
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length != artifact.ByteLength)
                {
                    throw new InvalidDataException(
                        $"Stored release artifact '{artifact.FileName}' is missing or truncated.");
                }

                return new StoredReleaseArtifact(artifact, fullPath);
            })
            .ToList();
        return new StoredHypershellRelease(
            ToSummary(manifest, row.IsPinned),
            envelope,
            artifacts);
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var candidates = new List<ReleaseRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT version, channel, git_commit, manifest_json,
                       installer_blob_path, portable_blob_path,
                       published_at_utc, is_pinned
                FROM releases
                WHERE channel = $channel AND is_pinned = 0
                ORDER BY published_at_utc DESC, version DESC
                LIMIT -1 OFFSET $retain;
                """;
            command.Parameters.AddWithValue(
                "$channel",
                HypershellReleaseContract.StableChannel);
            command.Parameters.AddWithValue(
                "$retain",
                _options.StableReleaseRetentionCount);
            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(ReadRow(reader));
            }
        }

        foreach (var candidate in candidates)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM releases WHERE version = $version AND is_pinned = 0;";
            command.Parameters.AddWithValue("$version", candidate.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var candidate in candidates)
        {
            await DeleteBlobIfUnreferencedAsync(
                connection,
                candidate.InstallerBlobPath,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(candidate.PortableBlobPath))
            {
                await DeleteBlobIfUnreferencedAsync(
                    connection,
                    candidate.PortableBlobPath,
                    cancellationToken);
            }
        }
    }

    private async Task DeleteBlobIfUnreferencedAsync(
        SqliteConnection connection,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM releases
            WHERE installer_blob_path = $path OR portable_blob_path = $path;
            """;
        command.Parameters.AddWithValue("$path", relativePath);
        var count = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (count != 0)
        {
            return;
        }

        var fullPath = ResolveRelativePath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private async Task InsertReleaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HypershellReleaseManifest manifest,
        string manifestJson,
        IReadOnlyDictionary<HypershellReleaseArtifactKind, string> paths,
        bool isPinned,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO releases(
                version, channel, git_commit, manifest_json,
                installer_blob_path, portable_blob_path,
                published_at_utc, is_pinned)
            VALUES(
                $version, $channel, $gitCommit, $manifestJson,
                $installerPath, $portablePath,
                $publishedAtUtc, $isPinned);
            """;
        command.Parameters.AddWithValue("$version", manifest.Version);
        command.Parameters.AddWithValue("$channel", manifest.Channel);
        command.Parameters.AddWithValue("$gitCommit", manifest.GitCommit);
        command.Parameters.AddWithValue("$manifestJson", manifestJson);
        command.Parameters.AddWithValue(
            "$installerPath",
            paths[HypershellReleaseArtifactKind.Installer]);
        command.Parameters.AddWithValue(
            "$portablePath",
            paths.TryGetValue(
                HypershellReleaseArtifactKind.Portable,
                out var portablePath)
                ? portablePath
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$publishedAtUtc",
            manifest.PublishedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetPinnedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE releases SET is_pinned = 1 WHERE version = $version;";
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ReleaseRow?> ReadReleaseRowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT version, channel, git_commit, manifest_json,
                   installer_blob_path, portable_blob_path,
                   published_at_utc, is_pinned
            FROM releases
            WHERE version = $version;
            """;
        command.Parameters.AddWithValue("$version", version);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRow(reader)
            : null;
    }

    private static ReleaseRow ReadRow(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            DateTimeOffset.Parse(
                reader.GetString(6),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.GetInt64(7) != 0);

    private HypershellReleaseSummary ReadSummary(SqliteDataReader reader)
    {
        var envelope = _security.DeserializeEnvelope(
            Encoding.UTF8.GetBytes(reader.GetString(3)));
        using var verified = new VerifiedReleaseLease(
            _security.Verify(
                envelope,
                _options.TrustedReleaseSignerSha256,
                requireTrustedCertificateChain: false));
        var manifest = verified.Value.Manifest;
        if (manifest.Version != reader.GetString(0)
            || manifest.Channel != reader.GetString(1)
            || !string.Equals(
                manifest.GitCommit,
                reader.GetString(2),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Stored release inventory metadata failed signed-manifest validation.");
        }

        return ToSummary(manifest, reader.GetInt64(5) != 0);
    }

    private static HypershellReleaseSummary ToSummary(
        HypershellReleaseManifest manifest,
        bool isPinned) =>
        new()
        {
            Version = manifest.Version,
            Channel = manifest.Channel,
            GitCommit = manifest.GitCommit,
            PublishedAtUtc = manifest.PublishedAtUtc,
            ReleaseNotes = manifest.ReleaseNotes,
            IsPinned = isPinned,
            Artifacts = manifest.Artifacts
        };

    private static byte[] ReadBoundedFile(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"Release metadata '{info.Name}' is missing or exceeds its size limit.");
        }

        return File.ReadAllBytes(info.FullName);
    }

    private string ToRelativePath(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        EnsureContained(_metadata.DataRoot, normalized);
        return Path.GetRelativePath(_metadata.DataRoot, normalized)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ResolveRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "A release artifact path is malformed.");
        }

        var fullPath = Path.GetFullPath(
            Path.Combine(
                _metadata.DataRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureContained(_metadata.DataRoot, fullPath);
        return fullPath;
    }

    private static void EnsureContained(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(
                normalizedRoot,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A release path escaped its approved storage root.");
        }
    }

    private static string? NormalizeOptionalChannel(string? channel) =>
        string.IsNullOrWhiteSpace(channel)
            ? null
            : NormalizeRequiredChannel(channel);

    private static string NormalizeRequiredChannel(string channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant()
                         ?? string.Empty;
        if (normalized.Length is < 2 or > 32
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character == '-'))
        {
            throw new ArgumentException(
                "The release channel is malformed.",
                nameof(channel));
        }

        return normalized;
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || version.Length > 64
            || version.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '-'))
        {
            throw new ArgumentException(
                "The release version is malformed.",
                nameof(version));
        }
    }

    private static string? NormalizeExpectedGitCommit(
        string? expectedGitCommit)
    {
        if (string.IsNullOrWhiteSpace(expectedGitCommit))
        {
            return null;
        }

        var normalized = expectedGitCommit.Trim();
        if (normalized.Length is < 40 or > 64
            || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "The expected Git commit is malformed.",
                nameof(expectedGitCommit));
        }

        return normalized.ToLowerInvariant();
    }

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

    private sealed record ReleaseRow(
        string Version,
        string Channel,
        string GitCommit,
        string ManifestJson,
        string InstallerBlobPath,
        string? PortableBlobPath,
        DateTimeOffset PublishedAtUtc,
        bool IsPinned);

    private sealed class VerifiedReleaseLease : IDisposable
    {
        public VerifiedReleaseLease(
            VerifiedHypershellReleaseManifest value)
        {
            Value = value;
        }

        public VerifiedHypershellReleaseManifest Value { get; }

        public void Dispose() => Value.SigningCertificate.Dispose();
    }
}
