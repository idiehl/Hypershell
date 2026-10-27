using System.Text.RegularExpressions;

namespace Hypershell.ProfileServer;

public sealed class ProfileServerOptions
{
    public const long DefaultMaximumAccountStorageBytes =
        512L * 1024 * 1024;
    public const long DefaultMinimumFreeStorageBytes =
        512L * 1024 * 1024;
    public const int DefaultCredentialHashIterations = 600_000;
    public const int DefaultStableReleaseRetentionCount = 3;

    public required string DataRoot { get; init; }
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public long MaximumAccountStorageBytes { get; init; } =
        DefaultMaximumAccountStorageBytes;
    public long MinimumFreeStorageBytes { get; init; } =
        DefaultMinimumFreeStorageBytes;
    public int CredentialHashIterations { get; init; } =
        DefaultCredentialHashIterations;
    public bool RequireHttpsPortalCookies { get; init; } = true;
    public Uri PublicPortalBaseUri { get; init; } =
        new("https://admin.atlasuniversalis.com", UriKind.Absolute);
    public IReadOnlyList<string> TrustedReleaseSignerSha256 { get; init; } =
        [];
    public int StableReleaseRetentionCount { get; init; } =
        DefaultStableReleaseRetentionCount;
    public string? DataProtectionCertificatePath { get; init; }
    public string? DataProtectionCertificatePasswordPath { get; init; }
    public string? SshVaultMasterKeyPath { get; init; }
    public bool ManageSshVaultMasterKeyFile { get; init; } = true;
    public string? BackupExportsRoot { get; init; }

    public ProfileServerOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(DataRoot))
        {
            throw new InvalidOperationException(
                "A profile-server data root is required.");
        }

        if (SessionLifetime < TimeSpan.FromMinutes(5)
            || SessionLifetime > TimeSpan.FromHours(24))
        {
            throw new InvalidOperationException(
                "Profile sessions must last between five minutes and 24 hours.");
        }

        if (MaximumAccountStorageBytes < 16L * 1024 * 1024
            || MaximumAccountStorageBytes > 100L * 1024 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "The per-account storage limit must be between 16 MiB and 100 GiB.");
        }

        if (MinimumFreeStorageBytes < 64L * 1024 * 1024
            || MinimumFreeStorageBytes > 100L * 1024 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "The minimum free-storage reserve must be between 64 MiB and 100 GiB.");
        }

        if (CredentialHashIterations < 100_000
            || CredentialHashIterations > 5_000_000)
        {
            throw new InvalidOperationException(
                "Credential hashing must use 100,000-5,000,000 PBKDF2 iterations.");
        }

        if (StableReleaseRetentionCount is < 1 or > 20)
        {
            throw new InvalidOperationException(
                "Stable release retention must keep between one and 20 unpinned releases.");
        }

        var trustedReleaseSigners = TrustedReleaseSignerSha256
            .Select(NormalizeSha256)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var certificatePath = NormalizeOptionalPath(
            DataProtectionCertificatePath);
        var certificatePasswordPath = NormalizeOptionalPath(
            DataProtectionCertificatePasswordPath);
        if ((certificatePath is null) != (certificatePasswordPath is null))
        {
            throw new InvalidOperationException(
                "Both data-protection certificate and password-file paths are required.");
        }

        if (!PublicPortalBaseUri.IsAbsoluteUri
            || (!string.Equals(
                    PublicPortalBaseUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                && (RequireHttpsPortalCookies
                    || !string.Equals(
                        PublicPortalBaseUri.Scheme,
                        Uri.UriSchemeHttp,
                        StringComparison.OrdinalIgnoreCase)))
            || !string.IsNullOrEmpty(PublicPortalBaseUri.UserInfo)
            || !string.IsNullOrEmpty(PublicPortalBaseUri.Query)
            || !string.IsNullOrEmpty(PublicPortalBaseUri.Fragment))
        {
            throw new InvalidOperationException(
                "The public portal origin must be an absolute HTTPS URI without credentials, query, or fragment.");
        }

        var publicPortalBaseUri = new UriBuilder(PublicPortalBaseUri)
        {
            Path = "/"
        }.Uri;
        var sshVaultMasterKeyPath = NormalizeOptionalPath(
            SshVaultMasterKeyPath)
            ?? Path.Combine(
                Path.GetFullPath(DataRoot),
                "secrets",
                "ssh-vault-master-key.bin");
        var backupExportsRoot = NormalizeOptionalPath(BackupExportsRoot)
                                ?? Path.Combine(
                                    Path.GetFullPath(DataRoot),
                                    "backup-exports");

        return new ProfileServerOptions
        {
            DataRoot = Path.GetFullPath(DataRoot),
            SessionLifetime = SessionLifetime,
            MaximumAccountStorageBytes = MaximumAccountStorageBytes,
            MinimumFreeStorageBytes = MinimumFreeStorageBytes,
            CredentialHashIterations = CredentialHashIterations,
            RequireHttpsPortalCookies = RequireHttpsPortalCookies,
            PublicPortalBaseUri = publicPortalBaseUri,
            TrustedReleaseSignerSha256 = trustedReleaseSigners,
            StableReleaseRetentionCount = StableReleaseRetentionCount,
            DataProtectionCertificatePath = certificatePath,
            DataProtectionCertificatePasswordPath = certificatePasswordPath,
            SshVaultMasterKeyPath = sshVaultMasterKeyPath,
            ManageSshVaultMasterKeyFile = ManageSshVaultMasterKeyFile,
            BackupExportsRoot = backupExportsRoot
        };
    }

    public static ProfileServerOptions FromEnvironment(
        string? explicitDataRoot = null)
    {
        var configuredRoot = explicitDataRoot
                             ?? Environment.GetEnvironmentVariable(
                                 "HYPERSHELL_PROFILE_SERVER_DATA")
                             ?? Path.Combine(
                                 AppContext.BaseDirectory,
                                 "profile-server-data");
        var sessionMinutes = ReadLongEnvironment(
            "HYPERSHELL_PROFILE_SESSION_MINUTES",
            30);
        var storageMiB = ReadLongEnvironment(
            "HYPERSHELL_PROFILE_ACCOUNT_STORAGE_MIB",
            DefaultMaximumAccountStorageBytes / (1024 * 1024));
        var freeStorageMiB = ReadLongEnvironment(
            "HYPERSHELL_MINIMUM_FREE_STORAGE_MIB",
            DefaultMinimumFreeStorageBytes / (1024 * 1024));
        var publicPortalOrigin =
            Environment.GetEnvironmentVariable(
                "HYPERSHELL_PUBLIC_PORTAL_ORIGIN")
            ?? "https://admin.atlasuniversalis.com";
        if (!Uri.TryCreate(
                publicPortalOrigin,
                UriKind.Absolute,
                out var publicPortalBaseUri))
        {
            throw new InvalidOperationException(
                "HYPERSHELL_PUBLIC_PORTAL_ORIGIN is not an absolute URI.");
        }

        var sshVaultMasterKeyPath =
            Environment.GetEnvironmentVariable(
                "HYPERSHELL_SSH_VAULT_MASTER_KEY_FILE");
        return new ProfileServerOptions
        {
            DataRoot = configuredRoot,
            SessionLifetime = TimeSpan.FromMinutes(sessionMinutes),
            MaximumAccountStorageBytes = checked(storageMiB * 1024 * 1024),
            MinimumFreeStorageBytes = checked(freeStorageMiB * 1024 * 1024),
            PublicPortalBaseUri = publicPortalBaseUri,
            TrustedReleaseSignerSha256 =
                ReadListEnvironment(
                    "HYPERSHELL_RELEASE_SIGNER_SHA256"),
            StableReleaseRetentionCount = checked((int)ReadLongEnvironment(
                "HYPERSHELL_STABLE_RELEASE_RETENTION",
                DefaultStableReleaseRetentionCount)),
            DataProtectionCertificatePath =
                Environment.GetEnvironmentVariable(
                    "HYPERSHELL_DATA_PROTECTION_CERTIFICATE"),
            DataProtectionCertificatePasswordPath =
                Environment.GetEnvironmentVariable(
                    "HYPERSHELL_DATA_PROTECTION_CERTIFICATE_PASSWORD_FILE"),
            SshVaultMasterKeyPath = sshVaultMasterKeyPath,
            ManageSshVaultMasterKeyFile =
                string.IsNullOrWhiteSpace(sshVaultMasterKeyPath),
            BackupExportsRoot = Environment.GetEnvironmentVariable(
                "HYPERSHELL_BACKUP_EXPORTS")
        }.Normalize();
    }

    private static string? NormalizeOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static string NormalizeSha256(string value)
    {
        var normalized = value
            ?.Replace(":", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant()
            ?? string.Empty;
        if (normalized.Length != 64
            || !normalized.All(char.IsAsciiHexDigit))
        {
            throw new InvalidOperationException(
                "HYPERSHELL_RELEASE_SIGNER_SHA256 contains a malformed SHA-256 certificate fingerprint.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> ReadListEnvironment(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? string.Empty)
        .Split(
            [';', ','],
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);

    private static long ReadLongEnvironment(string name, long fallback) =>
        long.TryParse(
            Environment.GetEnvironmentVariable(name),
            out var value)
            ? value
            : fallback;
}

internal static partial class ProfileServerIdentity
{
    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AccountIdPattern();

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdPattern();

    public static string NormalizeAccountId(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AccountIdPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Account IDs must contain 3-64 letters, numbers, periods, underscores, or hyphens.",
                nameof(value));
        }

        return normalized;
    }

    public static string ValidateProfileId(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!ProfileIdPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "The profile ID is empty or malformed.",
                nameof(value));
        }

        return normalized;
    }

    public static string StorageKey(string value) =>
        Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        value.ToLowerInvariant())))
            .ToLowerInvariant();
}
