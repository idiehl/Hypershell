using Hypershell.Profiles;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Hypershell.Services;

public enum ApplicationUpdateCheckKind
{
    TrustNotConfigured,
    Current,
    Available,
    Incompatible,
    ManualSuiteUpdate
}

public sealed record ApplicationUpdateCandidate(
    SignedHypershellReleaseManifest SignedManifest,
    HypershellReleaseManifest Manifest,
    HypershellReleaseArtifact Installer,
    string SignerCertificateSha256);

public sealed record ApplicationUpdateCheckOutcome(
    ApplicationUpdateCheckKind Kind,
    string Message,
    ApplicationUpdateCandidate? Candidate = null);

public sealed record ApplicationUpdateDownloadOutcome(
    ApplicationUpdateCandidate Candidate,
    string InstallerPath,
    bool UsedExistingDownload);

public sealed class ApplicationUpdateService
{
    private const int RetainedDownloadDirectories = 3;
    private const string SuiteUpdateMessage =
        "This installation includes Hypershell Complete components. Download the matching Complete installer from https://atlasuniversalis.com/Downloads to update the applications together.";

    private readonly string _updateRoot;
    private readonly HypershellReleaseTrustStore _trustStore;
    private readonly IAuthenticodeVerifier _authenticodeVerifier;
    private readonly Func<string, IHypershellReleaseClient> _clientFactory;
    private readonly HypershellReleaseManifestSecurity _security = new();
    private readonly Version _currentVersion;
    private readonly Version _currentWindowsVersion;
    private readonly bool _requireTrustedCertificateChain;
    private readonly Func<bool> _requiresSuiteInstaller;

    public ApplicationUpdateService(
        string updateRoot,
        HypershellReleaseTrustStore? trustStore = null,
        IAuthenticodeVerifier? authenticodeVerifier = null,
        Func<string, IHypershellReleaseClient>? clientFactory = null,
        Version? currentVersion = null,
        Version? currentWindowsVersion = null,
        bool requireTrustedCertificateChain = true,
        Func<bool>? requiresSuiteInstaller = null)
    {
        _updateRoot = Path.GetFullPath(updateRoot);
        _trustStore = trustStore ?? new HypershellReleaseTrustStore();
        _authenticodeVerifier = authenticodeVerifier
                                 ?? new WindowsAuthenticodeVerifier();
        _clientFactory = clientFactory
                         ?? (endpoint =>
                             new HttpHypershellReleaseClient(endpoint));
        _currentVersion = currentVersion
                          ?? Assembly.GetEntryAssembly()
                              ?.GetName()
                              .Version
                          ?? new Version(0, 0, 0);
        _currentWindowsVersion = currentWindowsVersion
                                 ?? Environment.OSVersion.Version;
        _requireTrustedCertificateChain =
            requireTrustedCertificateChain;
        _requiresSuiteInstaller = requiresSuiteInstaller
            ?? (() => DetectSuiteComponents(AppContext.BaseDirectory));
    }

    public string CurrentVersionText =>
        $"{_currentVersion.Major}.{_currentVersion.Minor}.{Math.Max(0, _currentVersion.Build)}";

    public bool IsTrustConfigured => _trustStore.IsConfigured;

    public async Task<ApplicationUpdateCheckOutcome> CheckAsync(
        string endpoint,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (_requiresSuiteInstaller())
        {
            return new ApplicationUpdateCheckOutcome(
                ApplicationUpdateCheckKind.ManualSuiteUpdate, SuiteUpdateMessage);
        }

        if (!_trustStore.IsConfigured)
        {
            return new ApplicationUpdateCheckOutcome(
                ApplicationUpdateCheckKind.TrustNotConfigured,
                "Stable updates are disabled until the Atlas release-signing certificate is bundled with Hypershell.");
        }

        await using var client = _clientFactory(endpoint);
        var signed = await client.GetLatestAsync(
            HypershellReleaseContract.StableChannel,
            accessToken,
            cancellationToken);
        VerifiedHypershellReleaseManifest verified;
        try
        {
            verified = _security.Verify(
                signed,
                _trustStore.SignerCertificateSha256,
                _requireTrustedCertificateChain);
        }
        catch (InvalidDataException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The update manifest was rejected: {exception.Message}",
                exception);
        }

        using (verified.SigningCertificate)
        {
            var manifest = verified.Manifest;
            var releaseVersion = ParseReleaseVersion(manifest.Version);
            if (releaseVersion <= _currentVersion)
            {
                return new ApplicationUpdateCheckOutcome(
                    ApplicationUpdateCheckKind.Current,
                    $"Hypershell {CurrentVersionText} is current on the stable channel.");
            }

            var minimumWindows = Version.Parse(
                manifest.MinimumWindowsVersion);
            if (_currentWindowsVersion < minimumWindows)
            {
                return new ApplicationUpdateCheckOutcome(
                    ApplicationUpdateCheckKind.Incompatible,
                    $"Hypershell {manifest.Version} requires Windows {minimumWindows} or newer.");
            }

            var installer = manifest.Artifacts.SingleOrDefault(
                artifact => artifact.Kind
                            == HypershellReleaseArtifactKind.Installer)
                ?? throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    "The stable release has no installer.");
            var candidate = new ApplicationUpdateCandidate(
                signed,
                manifest,
                installer,
                verified.SigningCertificateSha256);
            return new ApplicationUpdateCheckOutcome(
                ApplicationUpdateCheckKind.Available,
                $"Hypershell {manifest.Version} is available.",
                candidate);
        }
    }

    public async Task<ApplicationUpdateDownloadOutcome> DownloadAsync(
        ApplicationUpdateCandidate candidate,
        string endpoint,
        string accessToken,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureCoreUpdateAllowed();
        var versionDirectory = ResolveVersionDirectory(
            candidate.Manifest.Version);
        Directory.CreateDirectory(versionDirectory);
        var installerPath = Path.GetFullPath(
            Path.Combine(
                versionDirectory,
                candidate.Installer.FileName));
        EnsureContained(versionDirectory, installerPath);
        if (File.Exists(installerPath))
        {
            try
            {
                VerifyDownloadedInstaller(candidate, installerPath);
                return new ApplicationUpdateDownloadOutcome(
                    candidate,
                    installerPath,
                    UsedExistingDownload: true);
            }
            catch (Exception exception) when (exception is InvalidDataException
                                             or IOException
                                             or UnauthorizedAccessException)
            {
                File.Delete(installerPath);
            }
        }

        await using var client = _clientFactory(endpoint);
        await client.DownloadArtifactAsync(
            candidate.Manifest.Version,
            candidate.Installer,
            accessToken,
            installerPath,
            progress,
            cancellationToken);
        VerifyDownloadedInstaller(candidate, installerPath);
        WriteSignedManifest(candidate, versionDirectory);
        PruneOldDownloads(candidate.Manifest.Version);
        return new ApplicationUpdateDownloadOutcome(
            candidate,
            installerPath,
            UsedExistingDownload: false);
    }

    public Process StartInstallAndRestart(
        ApplicationUpdateDownloadOutcome download,
        int parentProcessId)
    {
        ArgumentNullException.ThrowIfNull(download);
        EnsureCoreUpdateAllowed();
        if (parentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        }

        VerifyDownloadedInstaller(
            download.Candidate,
            download.InstallerPath);
        var installedExecutable = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "Hypershell",
            "Hypershell.exe");
        var script =
            "$ErrorActionPreference='Stop';"
            + $"Wait-Process -Id {parentProcessId} -ErrorAction SilentlyContinue;"
            + "$msi=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\\msiexec.exe') "
            + $"-ArgumentList @('/i','{EscapePowerShellLiteral(download.InstallerPath)}','/passive','/norestart') "
            + "-Verb RunAs -Wait -PassThru;"
            + "if($msi.ExitCode -in @(0,3010)){"
            + $"$app='{EscapePowerShellLiteral(installedExecutable)}';"
            + "if(Test-Path -LiteralPath $app){Start-Process -FilePath $app}}";
        var encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));
        return Process.Start(
                   new ProcessStartInfo(
                       Path.Combine(
                           Environment.GetFolderPath(
                               Environment.SpecialFolder.System),
                           "WindowsPowerShell",
                           "v1.0",
                           "powershell.exe"))
                   {
                       UseShellExecute = false,
                       CreateNoWindow = true,
                       ArgumentList =
                       {
                           "-NoLogo",
                           "-NoProfile",
                           "-NonInteractive",
                           "-WindowStyle",
                           "Hidden",
                           "-EncodedCommand",
                           encoded
                       }
                   })
               ?? throw new InvalidOperationException(
                   "The update handoff process could not be started.");
    }

    internal static bool DetectSuiteComponents(string baseDirectory)
    {
        // Studio runs inside Components/Authoring/studio; the shell runs at the
        // installation root. Recognize either layout without changing registry
        // or loading application registrations and their user data.
        var directory = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        for (var depth = 0; directory is not null && depth <= 3; depth++, directory = directory.Parent)
        {
            var components = Path.Combine(directory.FullName, "Components");
            if (File.Exists(Path.Combine(components, "Authoring", "studio", "Hypershell.exe"))
                || File.Exists(Path.Combine(components, "Meridian", "Meridian.Host.exe")))
                return true;
        }
        return false;
    }

    private void EnsureCoreUpdateAllowed()
    {
        if (_requiresSuiteInstaller()) throw new InvalidOperationException(SuiteUpdateMessage);
    }

    private void VerifyDownloadedInstaller(
        ApplicationUpdateCandidate candidate,
        string path)
    {
        try
        {
            _security.VerifyArtifact(path, candidate.Installer);
            if (candidate.Installer.RequiresAuthenticode)
            {
                _ = _authenticodeVerifier.Verify(
                    path,
                    candidate.SignerCertificateSha256);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"The downloaded installer was rejected: {exception.Message}",
                exception);
        }
    }

    private void WriteSignedManifest(
        ApplicationUpdateCandidate candidate,
        string versionDirectory)
    {
        var path = Path.Combine(
            versionDirectory,
            ReleaseStoreFileName());
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(
                temporary,
                _security.SerializeEnvelope(candidate.SignedManifest));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private void PruneOldDownloads(string retainedVersion)
    {
        if (!Directory.Exists(_updateRoot))
        {
            return;
        }

        var directories = Directory.EnumerateDirectories(_updateRoot)
            .Select(path => new DirectoryInfo(path))
            .Where(directory => Version.TryParse(
                directory.Name.Split('-', 2)[0],
                out _))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ToList();
        foreach (var directory in directories
                     .Where(directory => !string.Equals(
                         directory.Name,
                         retainedVersion,
                         StringComparison.OrdinalIgnoreCase))
                     .Skip(RetainedDownloadDirectories - 1))
        {
            EnsureContained(_updateRoot, directory.FullName);
            directory.Delete(recursive: true);
        }
    }

    private string ResolveVersionDirectory(string version)
    {
        _ = ParseReleaseVersion(version);
        var path = Path.GetFullPath(Path.Combine(_updateRoot, version));
        EnsureContained(_updateRoot, path);
        return path;
    }

    private static Version ParseReleaseVersion(string version)
    {
        var core = version.Split('-', 2)[0];
        return Version.TryParse(core, out var parsed)
            ? parsed
            : throw new InvalidDataException(
                "The signed release version is malformed.");
    }

    private static void EnsureContained(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "An update path escaped its device-local staging root.");
        }
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string ReleaseStoreFileName() =>
        "release-manifest.json";
}
