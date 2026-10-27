using Hypershell.Profiles;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Hypershell.ProfileServer;

public sealed record PublicHypershellRelease(
    StoredHypershellRelease Core,
    StoredHypershellRelease Complete);

public sealed partial class ReleaseStore
{
    public const string CompleteManifestFileName = "complete-manifest.json";

    // A companion is an explicit public distribution declaration. Legacy releases
    // without one stay private even when their channel is named "stable".
    public async Task<IReadOnlyList<PublicHypershellRelease>> ListPublicAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.version FROM releases r
            INNER JOIN release_companions c ON c.version = r.version
            WHERE r.channel = $channel
            ORDER BY r.published_at_utc DESC, r.version DESC;
            """;
        command.Parameters.AddWithValue("$channel", HypershellReleaseContract.StableChannel);
        var versions = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) versions.Add(reader.GetString(0));
        }
        var releases = new List<PublicHypershellRelease>();
        foreach (var version in versions)
        {
            var release = await GetPublicAsync(version, cancellationToken);
            if (release is not null) releases.Add(release);
        }
        return releases;
    }

    public async Task<PublicHypershellRelease?> GetPublicAsync(
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidateVersion(version);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var companion = await ReadCompanionAsync(connection, null, version, cancellationToken);
        if (companion is null) return null;
        var core = await GetAsync(version, cancellationToken);
        if (core is null || core.Summary.Channel != HypershellReleaseContract.StableChannel) return null;
        var envelope = _security.DeserializeEnvelope(Encoding.UTF8.GetBytes(companion.ManifestJson));
        using var completeVerified = new VerifiedReleaseLease(_security.Verify(
            envelope, _options.TrustedReleaseSignerSha256, requireTrustedCertificateChain: false));
        using var coreVerified = new VerifiedReleaseLease(_security.Verify(
            core.SignedManifest, _options.TrustedReleaseSignerSha256, requireTrustedCertificateChain: false));
        ValidateCompanion(coreVerified.Value, completeVerified.Value);
        var artifact = completeVerified.Value.Manifest.Artifacts.Single();
        var fullPath = ResolveRelativePath(companion.InstallerBlobPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != artifact.ByteLength)
            throw new InvalidDataException("The Complete installer is missing or truncated.");
        return new PublicHypershellRelease(core, new StoredHypershellRelease(
            ToSummary(completeVerified.Value.Manifest, core.Summary.IsPinned), envelope,
            [new StoredReleaseArtifact(artifact, fullPath)]));
    }

    private IncomingCompanion? ReadIncomingCompanion(
        string bundleRoot,
        VerifiedHypershellReleaseManifest core)
    {
        var path = Path.Combine(bundleRoot, CompleteManifestFileName);
        if (!File.Exists(path)) return null;
        var bytes = ReadBoundedFile(path, HypershellReleaseContract.MaximumSignedManifestBytes);
        var envelope = _security.DeserializeEnvelope(bytes);
        using var verified = new VerifiedReleaseLease(_security.Verify(
            envelope, _options.TrustedReleaseSignerSha256, requireTrustedCertificateChain: false));
        ValidateCompanion(core, verified.Value);
        var artifact = verified.Value.Manifest.Artifacts.Single();
        var artifactPath = Path.GetFullPath(Path.Combine(bundleRoot, artifact.FileName));
        EnsureContained(bundleRoot, artifactPath);
        _security.VerifyArtifact(artifactPath, artifact);
        return new IncomingCompanion(Encoding.UTF8.GetString(bytes), artifact, artifactPath);
    }

    private static void ValidateCompanion(
        VerifiedHypershellReleaseManifest core,
        VerifiedHypershellReleaseManifest complete)
    {
        var parent = core.Manifest;
        var child = complete.Manifest;
        var coreInstaller = parent.Artifacts.Single(a => a.Kind == HypershellReleaseArtifactKind.Installer);
        if (parent.Channel != HypershellReleaseContract.StableChannel
            || child.Channel != HypershellReleaseContract.CompleteStableChannel
            || parent.Version != child.Version
            || !string.Equals(parent.GitCommit, child.GitCommit, StringComparison.OrdinalIgnoreCase)
            || parent.PublishedAtUtc != child.PublishedAtUtc
            || parent.Architecture != child.Architecture
            || parent.MinimumWindowsVersion != child.MinimumWindowsVersion
            || parent.ReleaseNotes != child.ReleaseNotes
            || core.SigningCertificateSha256 != complete.SigningCertificateSha256
            || child.Artifacts.Count != 1
            || child.Artifacts[0].Kind != HypershellReleaseArtifactKind.Installer
            || !child.Artifacts[0].RequiresAuthenticode
            || !child.Artifacts[0].FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !coreInstaller.RequiresAuthenticode
            || !coreInstaller.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            || parent.Artifacts.Any(a => string.Equals(a.FileName, child.Artifacts[0].FileName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The Complete companion must match its stable Core release identity, time, compatibility and signer, and contain one signed EXE installer.");
        }
    }

    private static async Task ValidateExistingCompanionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string version,
        IncomingCompanion? incoming,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCompanionAsync(connection, transaction, version, cancellationToken);
        if (!string.Equals(existing?.ManifestJson, incoming?.ManifestJson, StringComparison.Ordinal))
            throw new InvalidDataException("A release's public Complete companion is immutable; publish a new version instead.");
    }

    private async Task InsertCompanionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string version,
        IncomingCompanion companion,
        CancellationToken cancellationToken)
    {
        var blobPath = StoreBlob(companion.SourcePath, companion.Artifact);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO release_companions(version, manifest_json, installer_blob_path)
            VALUES ($version, $manifest, $path);
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$manifest", companion.ManifestJson);
        command.Parameters.AddWithValue("$path", blobPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CompanionRow?> ReadCompanionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT manifest_json, installer_blob_path FROM release_companions WHERE version = $version;";
        command.Parameters.AddWithValue("$version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CompanionRow(reader.GetString(0), reader.GetString(1)) : null;
    }

    private sealed record CompanionRow(string ManifestJson, string InstallerBlobPath);
    private sealed record IncomingCompanion(string ManifestJson, HypershellReleaseArtifact Artifact, string SourcePath);
}
