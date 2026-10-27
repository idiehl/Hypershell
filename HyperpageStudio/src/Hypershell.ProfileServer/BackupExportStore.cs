using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public sealed record BackupExportSummary(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string FileName,
    long ByteLength,
    string Sha256,
    DateTimeOffset? RestoreValidatedAtUtc,
    string SnapshotId);

public sealed record BackupExportInventory(
    IReadOnlyList<BackupExportSummary> Exports,
    int RejectedManifestCount,
    string StatusMessage);

public sealed record VerifiedBackupExport(
    BackupExportSummary Summary,
    Stream Content);

/// <summary>
/// Reads root-generated, GPG-encrypted recovery bundles from a dedicated
/// read-only mount. The web process never creates a backup and never receives
/// the recovery private key. Length and SHA-256 are rechecked before download.
/// </summary>
public sealed partial class BackupExportStore
{
    private const int MaximumExports = 256;
    private const int MaximumManifestBytes = 64 * 1024;
    private const long MaximumExportBytes = 100L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _root;

    public BackupExportStore(ProfileServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = options.Normalize().BackupExportsRoot
                ?? throw new InvalidOperationException(
                    "The backup export root is unavailable.");
    }

    public async Task<BackupExportInventory> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
        {
            return new BackupExportInventory(
                [],
                0,
                "The encrypted export directory has not been provisioned.");
        }

        var summaries = new List<BackupExportSummary>();
        var rejected = 0;
        foreach (var path in Directory.EnumerateFiles(
                     _root,
                     "hypershell-recovery-*.manifest.json",
                     SearchOption.TopDirectoryOnly)
                 .OrderByDescending(File.GetLastWriteTimeUtc)
                 .Take(MaximumExports + 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summaries.Count >= MaximumExports)
            {
                rejected++;
                continue;
            }

            try
            {
                summaries.Add(await ReadManifestAsync(path, cancellationToken));
            }
            catch (Exception exception) when (exception is
                                              IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or JsonException
                                              or ArgumentException)
            {
                rejected++;
            }
        }

        summaries.Sort((left, right) =>
            right.CreatedAtUtc.CompareTo(left.CreatedAtUtc));
        var status = rejected == 0
            ? $"{summaries.Count} encrypted recovery export(s) available."
            : $"{summaries.Count} export(s) available; {rejected} malformed or incomplete manifest(s) rejected.";
        return new BackupExportInventory(summaries, rejected, status);
    }

    public async Task<VerifiedBackupExport?> OpenVerifiedAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!ExportIdPattern().IsMatch(id ?? string.Empty)
            || !Directory.Exists(_root))
        {
            return null;
        }

        var manifestPath = ContainedPath($"{id}.manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var summary = await ReadManifestAsync(
            manifestPath,
            cancellationToken);
        var contentPath = ContainedPath(summary.FileName);
        var stream = new FileStream(
            contentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length != summary.ByteLength)
            {
                throw new InvalidDataException(
                    "The encrypted recovery export length no longer matches its manifest.");
            }

            var hash = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(hash),
                    System.Text.Encoding.ASCII.GetBytes(summary.Sha256)))
            {
                throw new InvalidDataException(
                    "The encrypted recovery export SHA-256 no longer matches its manifest.");
            }

            stream.Position = 0;
            return new VerifiedBackupExport(summary, stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task<BackupExportSummary> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists
            || info.LinkTarget is not null
            || info.Length is <= 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "The backup export manifest is missing, linked, empty, or oversized.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var manifest = JsonSerializer.Deserialize<BackupExportManifest>(
                           bytes,
                           JsonOptions)
                       ?? throw new InvalidDataException(
                           "The backup export manifest is empty.");
        if (manifest.SchemaVersion != 1
            || !ExportIdPattern().IsMatch(manifest.Id)
            || !string.Equals(
                manifest.FileName,
                $"{manifest.Id}.tar.gz.gpg",
                StringComparison.Ordinal)
            || manifest.Size is <= 0 or > MaximumExportBytes
            || !Sha256Pattern().IsMatch(manifest.Sha256)
            || manifest.CreatedAtUtc < new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            || manifest.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5)
            || manifest.RestoreValidatedAtUtc is null
            || manifest.RestoreValidatedAtUtc > manifest.CreatedAtUtc.AddMinutes(5)
            || (!string.IsNullOrWhiteSpace(manifest.SnapshotId)
                && !SnapshotIdPattern().IsMatch(manifest.SnapshotId)))
        {
            throw new InvalidDataException(
                "The backup export manifest contains invalid metadata.");
        }

        var expectedManifestName = $"{manifest.Id}.manifest.json";
        if (!string.Equals(
                Path.GetFileName(path),
                expectedManifestName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The backup export manifest name does not match its identity.");
        }

        var contentPath = ContainedPath(manifest.FileName);
        var content = new FileInfo(contentPath);
        if (!content.Exists
            || content.LinkTarget is not null
            || content.Length != manifest.Size)
        {
            throw new InvalidDataException(
                "The encrypted recovery export is missing, linked, or has the wrong length.");
        }

        return new BackupExportSummary(
            manifest.Id,
            manifest.CreatedAtUtc,
            manifest.FileName,
            manifest.Size,
            manifest.Sha256,
            manifest.RestoreValidatedAtUtc,
            manifest.SnapshotId ?? string.Empty);
    }

    private string ContainedPath(string fileName)
    {
        var root = Path.GetFullPath(_root)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, fileName));
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The backup export path escaped its configured root.");
        }

        return candidate;
    }

    [GeneratedRegex(
        "^hypershell-recovery-[0-9]{8}T[0-9]{6}Z$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExportIdPattern();

    [GeneratedRegex(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex(
        "^[A-Za-z0-9_-]{1,128}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotIdPattern();

    private sealed class BackupExportManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("createdAtUtc")]
        public DateTimeOffset CreatedAtUtc { get; init; }

        [JsonPropertyName("fileName")]
        public string FileName { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;

        [JsonPropertyName("restoreValidatedAtUtc")]
        public DateTimeOffset? RestoreValidatedAtUtc { get; init; }

        [JsonPropertyName("snapshotId")]
        public string? SnapshotId { get; init; }
    }
}
