using Hypershell.Extensibility;
using Hypershell.Profiles;
using System.IO;

namespace Hypershell.Services;

public sealed record HyperlibPackageRoamingOutcome(
    int Uploaded,
    int Downloaded,
    int AlreadyAvailable,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> MissingOfficialPackages)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>
/// Roams only inert, validated ContentOnly Hyperlib packages. Installed
/// packages are never replaced automatically when an identity has different
/// content on the current PC.
/// </summary>
public sealed class HyperlibPackageRoamingService
{
    private readonly HyperlibPackageService _packages;
    private readonly string _cacheDirectory;

    public HyperlibPackageRoamingService(
        HyperlibPackageService packages,
        string cacheDirectory)
    {
        _packages = packages;
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    public async Task<HyperlibPackageRoamingOutcome> UploadMissingAsync(
        ICloudRoamingTransport transport,
        ExtensionDiscoverySnapshot catalog,
        IReadOnlyList<HyperlibPackageSelection> selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selections);
        var remote = await transport.ListPackagesAsync(cancellationToken);
        var conflicts = new List<string>();
        var missingOfficial = new List<string>();
        var uploaded = 0;
        var available = 0;

        foreach (var selection in selections
                     .OrderBy(item => item.PackageId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection.IsOfficial)
            {
                missingOfficial.Add(Identity(selection));
                continue;
            }

            var exactRemote = remote.FirstOrDefault(item =>
                SameIdentity(item, selection)
                && string.Equals(
                    item.ContentSha256,
                    selection.ContentSha256,
                    StringComparison.OrdinalIgnoreCase));
            if (exactRemote is not null)
            {
                available++;
                continue;
            }

            if (remote.Any(item => SameIdentity(item, selection)))
            {
                conflicts.Add(
                    $"{Identity(selection)} already exists in the private vault with different content.");
                continue;
            }

            var local = catalog.Packages.FirstOrDefault(package =>
                package.Status == ExtensionPackageStatus.Validated
                && package.Manifest?.Runtime
                   == ExtensionRuntimeKind.ContentOnly
                && string.Equals(
                    package.Manifest.Id,
                    selection.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    package.Manifest.Version,
                    selection.Version,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    package.PackageSha256,
                    selection.ContentSha256,
                    StringComparison.OrdinalIgnoreCase));
            if (local is null)
            {
                conflicts.Add(
                    $"{Identity(selection)} is selected but its verified local package is unavailable.");
                continue;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var archivePath = Path.Combine(
                _cacheDirectory,
                $"{selection.ContentSha256}{HyperlibPackageService.ArchiveExtension}");
            var archive = _packages.Pack(
                local.PackageDirectory,
                archivePath);
            if (!string.Equals(
                    archive.PackageSha256,
                    selection.ContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Packing {Identity(selection)} changed its declared content hash.");
            }

            _ = await transport.UploadPackageAsync(
                selection.PackageId,
                selection.Version,
                selection.ContentSha256,
                archive.ArchivePath,
                cancellationToken);
            uploaded++;
        }

        return new HyperlibPackageRoamingOutcome(
            uploaded,
            Downloaded: 0,
            available,
            conflicts,
            missingOfficial);
    }

    public async Task<HyperlibPackageRoamingOutcome> RestoreMissingAsync(
        ICloudRoamingTransport transport,
        ExtensionDiscoverySnapshot catalog,
        IReadOnlyList<HyperlibPackageSelection> selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selections);
        var remote = await transport.ListPackagesAsync(cancellationToken);
        var conflicts = new List<string>();
        var missingOfficial = new List<string>();
        var downloaded = 0;
        var available = 0;

        foreach (var selection in selections
                     .OrderBy(item => item.PackageId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installed = catalog.Packages.FirstOrDefault(package =>
                string.Equals(
                    package.Manifest?.Id,
                    selection.PackageId,
                    StringComparison.OrdinalIgnoreCase));
            if (installed is not null
                && string.Equals(
                    installed.Manifest?.Version,
                    selection.Version,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    installed.PackageSha256,
                    selection.ContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                available++;
                continue;
            }

            if (installed is not null)
            {
                conflicts.Add(
                    $"{Identity(selection)} was not restored because this PC has the same package ID with different content or version.");
                continue;
            }

            if (selection.IsOfficial)
            {
                missingOfficial.Add(Identity(selection));
                continue;
            }

            var vaultPackage = remote.FirstOrDefault(item =>
                SameIdentity(item, selection)
                && string.Equals(
                    item.ContentSha256,
                    selection.ContentSha256,
                    StringComparison.OrdinalIgnoreCase));
            if (vaultPackage is null)
            {
                conflicts.Add(
                    $"{Identity(selection)} is not available in the private package vault.");
                continue;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var archivePath = Path.Combine(
                _cacheDirectory,
                $"{selection.ContentSha256}{HyperlibPackageService.ArchiveExtension}");
            await transport.DownloadPackageAsync(
                selection.ContentSha256,
                archivePath,
                cancellationToken);
            var preview = _packages.InspectInstallSource(archivePath);
            if (!string.Equals(
                    preview.ExtensionId,
                    selection.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    preview.Version,
                    selection.Version,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    preview.PackageSha256,
                    selection.ContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The downloaded archive for {Identity(selection)} failed identity or hash verification.");
            }

            _ = _packages.Install(archivePath, allowUpdate: false);
            downloaded++;
        }

        return new HyperlibPackageRoamingOutcome(
            Uploaded: 0,
            downloaded,
            available,
            conflicts,
            missingOfficial);
    }

    private static bool SameIdentity(
        HyperlibVaultPackageSummary remote,
        HyperlibPackageSelection selection) =>
        string.Equals(
            remote.PackageId,
            selection.PackageId,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            remote.Version,
            selection.Version,
            StringComparison.OrdinalIgnoreCase);

    private static string Identity(HyperlibPackageSelection selection) =>
        $"{selection.PackageId}@{selection.Version}";
}
