using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Profiles;

public static class HypershellReleaseContract
{
    public const int CurrentManifestSchemaVersion = 1;
    public const int CurrentEnvelopeSchemaVersion = 1;
    public const string ProductId = "com.atlasuniversalis.hypershell";
    public const string StableChannel = "stable";
    public const string CompleteStableChannel = "stable-complete";
    public const string WindowsX64Architecture = "win-x64";
    public const int MaximumSignedManifestBytes = 256 * 1024;
    public const int MaximumManifestBytes = 128 * 1024;
    public const long MaximumInstallerBytes = 2L * 1024 * 1024 * 1024;
    public const long MaximumPortableBytes = 2L * 1024 * 1024 * 1024;
}

public enum HypershellReleaseArtifactKind
{
    Installer,
    Portable
}

public sealed record HypershellReleaseArtifact
{
    public HypershellReleaseArtifactKind Kind { get; init; }

    public string FileName { get; init; } = string.Empty;

    public long ByteLength { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public bool RequiresAuthenticode { get; init; }
}

public sealed record HypershellReleaseManifest
{
    public int SchemaVersion { get; init; } =
        HypershellReleaseContract.CurrentManifestSchemaVersion;

    public string ProductId { get; init; } =
        HypershellReleaseContract.ProductId;

    public string Version { get; init; } = string.Empty;

    public string Channel { get; init; } =
        HypershellReleaseContract.StableChannel;

    public string Architecture { get; init; } =
        HypershellReleaseContract.WindowsX64Architecture;

    public string GitCommit { get; init; } = string.Empty;

    public DateTimeOffset PublishedAtUtc { get; init; }

    public string MinimumWindowsVersion { get; init; } = "10.0.17763";

    public string ReleaseNotes { get; init; } = string.Empty;

    public IReadOnlyList<HypershellReleaseArtifact> Artifacts { get; init; } =
        [];
}

public sealed record SignedHypershellReleaseManifest
{
    public int SchemaVersion { get; init; } =
        HypershellReleaseContract.CurrentEnvelopeSchemaVersion;

    public string ManifestBase64 { get; init; } = string.Empty;

    public string SignatureAlgorithm { get; init; } = string.Empty;

    public string SigningCertificateBase64 { get; init; } = string.Empty;

    public string SignatureBase64 { get; init; } = string.Empty;
}

public sealed record HypershellReleaseSummary
{
    public string Version { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public string GitCommit { get; init; } = string.Empty;

    public DateTimeOffset PublishedAtUtc { get; init; }

    public string ReleaseNotes { get; init; } = string.Empty;

    public bool IsPinned { get; init; }

    public IReadOnlyList<HypershellReleaseArtifact> Artifacts { get; init; } =
        [];
}

public sealed record VerifiedHypershellReleaseManifest(
    HypershellReleaseManifest Manifest,
    byte[] ManifestBytes,
    X509Certificate2 SigningCertificate,
    string SigningCertificateSha256);

public sealed partial class HypershellReleaseManifestSecurity
{
    private const string RsaAlgorithm = "RSA-PKCS1-SHA256";
    private const string EcdsaAlgorithm = "ECDSA-SHA256";
    private const string CodeSigningEnhancedKeyUsage =
        "1.3.6.1.5.5.7.3.3";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(
        "^[0-9a-fA-F]{7,64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex GitCommitPattern();

    [GeneratedRegex(
        "^[a-z][a-z0-9-]{1,31}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();

    public SignedHypershellReleaseManifest Sign(
        HypershellReleaseManifest manifest,
        X509Certificate2 signingCertificate,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(signingCertificate);
        ValidateManifest(manifest, nowUtc ?? DateTimeOffset.UtcNow);
        if (!signingCertificate.HasPrivateKey)
        {
            throw new InvalidDataException(
                "The release-signing certificate has no private key.");
        }

        ValidateSigningCertificateUsage(signingCertificate);

        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            JsonOptions);
        if (manifestBytes.Length
            > HypershellReleaseContract.MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "The release manifest exceeds its protocol limit.");
        }

        string algorithm;
        byte[] signature;
        using (var rsa = signingCertificate.GetRSAPrivateKey())
        {
            if (rsa is not null)
            {
                algorithm = RsaAlgorithm;
                signature = rsa.SignData(
                    manifestBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
            else
            {
                using var ecdsa =
                    signingCertificate.GetECDsaPrivateKey()
                    ?? throw new InvalidDataException(
                        "The release certificate must contain an RSA or ECDSA signing key.");
                algorithm = EcdsaAlgorithm;
                signature = ecdsa.SignData(
                    manifestBytes,
                    HashAlgorithmName.SHA256);
            }
        }

        return new SignedHypershellReleaseManifest
        {
            ManifestBase64 = Convert.ToBase64String(manifestBytes),
            SignatureAlgorithm = algorithm,
            SigningCertificateBase64 = Convert.ToBase64String(
                signingCertificate.Export(X509ContentType.Cert)),
            SignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    public VerifiedHypershellReleaseManifest Verify(
        SignedHypershellReleaseManifest envelope,
        IEnumerable<string> trustedSignerSha256,
        bool requireTrustedCertificateChain,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(trustedSignerSha256);
        if (envelope.SchemaVersion
            != HypershellReleaseContract.CurrentEnvelopeSchemaVersion)
        {
            throw new InvalidDataException(
                $"Release envelope schema {envelope.SchemaVersion} is unsupported.");
        }

        var manifestBytes = DecodeBounded(
            envelope.ManifestBase64,
            HypershellReleaseContract.MaximumManifestBytes,
            "manifest");
        var certificateBytes = DecodeBounded(
            envelope.SigningCertificateBase64,
            64 * 1024,
            "signing certificate");
        var signature = DecodeBounded(
            envelope.SignatureBase64,
            16 * 1024,
            "manifest signature");
        var certificate = X509CertificateLoader.LoadCertificate(
            certificateBytes);
        try
        {
            var certificateSha256 = Convert.ToHexString(
                    SHA256.HashData(certificate.RawData))
                .ToLowerInvariant();
            var trusted = trustedSignerSha256
                .Select(NormalizeSha256)
                .ToHashSet(StringComparer.Ordinal);
            if (trusted.Count == 0 || !trusted.Contains(certificateSha256))
            {
                throw new InvalidDataException(
                    "The release manifest signer is not trusted by this Hypershell build.");
            }

            var now = nowUtc ?? DateTimeOffset.UtcNow;
            if (certificate.NotBefore.ToUniversalTime() > now.UtcDateTime
                || certificate.NotAfter.ToUniversalTime() < now.UtcDateTime)
            {
                throw new InvalidDataException(
                    "The release-signing certificate is outside its validity period.");
            }

            ValidateSigningCertificateUsage(certificate);

            if (requireTrustedCertificateChain)
            {
                using var chain = new X509Chain
                {
                    ChainPolicy =
                    {
                        RevocationMode = X509RevocationMode.Online,
                        RevocationFlag = X509RevocationFlag.ExcludeRoot,
                        VerificationFlags = X509VerificationFlags.NoFlag,
                        UrlRetrievalTimeout = TimeSpan.FromSeconds(15)
                    }
                };
                if (!chain.Build(certificate))
                {
                    throw new InvalidDataException(
                        "Windows did not trust the release-signing certificate chain.");
                }
            }

            var signatureValid = envelope.SignatureAlgorithm switch
            {
                RsaAlgorithm => VerifyRsa(
                    certificate,
                    manifestBytes,
                    signature),
                EcdsaAlgorithm => VerifyEcdsa(
                    certificate,
                    manifestBytes,
                    signature),
                _ => false
            };
            if (!signatureValid)
            {
                throw new InvalidDataException(
                    "The release manifest signature is invalid.");
            }

            HypershellReleaseManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<
                               HypershellReleaseManifest>(
                               manifestBytes,
                               JsonOptions)
                           ?? throw new JsonException(
                               "The release manifest is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"The release manifest is malformed: {exception.Message}",
                    exception);
            }

            ValidateManifest(manifest, now);
            return new VerifiedHypershellReleaseManifest(
                manifest,
                manifestBytes,
                certificate,
                certificateSha256);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    public byte[] SerializeEnvelope(
        SignedHypershellReleaseManifest envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            JsonOptions);
        if (payload.Length
            > HypershellReleaseContract.MaximumSignedManifestBytes)
        {
            throw new InvalidDataException(
                "The signed release manifest exceeds its protocol limit.");
        }

        return payload;
    }

    public SignedHypershellReleaseManifest DeserializeEnvelope(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length is <= 0
            || payload.Length
            > HypershellReleaseContract.MaximumSignedManifestBytes)
        {
            throw new InvalidDataException(
                "The signed release manifest has an invalid size.");
        }

        try
        {
            return JsonSerializer.Deserialize<
                       SignedHypershellReleaseManifest>(
                       payload,
                       JsonOptions)
                   ?? throw new JsonException(
                       "The signed release manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The signed release manifest is malformed: {exception.Message}",
                exception);
        }
    }

    public void VerifyArtifact(
        string path,
        HypershellReleaseArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(artifact);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != artifact.ByteLength)
        {
            throw new InvalidDataException(
                $"Release artifact '{artifact.FileName}' has the wrong size.");
        }

        using var stream = new FileStream(
            info.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);
        var hash = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        if (!string.Equals(hash, artifact.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Release artifact '{artifact.FileName}' failed SHA-256 verification.");
        }
    }

    private static void ValidateManifest(
        HypershellReleaseManifest manifest,
        DateTimeOffset now)
    {
        if (manifest.SchemaVersion
            != HypershellReleaseContract.CurrentManifestSchemaVersion
            || manifest.ProductId != HypershellReleaseContract.ProductId
            || !VersionPattern().IsMatch(manifest.Version)
            || !ChannelPattern().IsMatch(manifest.Channel)
            || manifest.Architecture
            != HypershellReleaseContract.WindowsX64Architecture
            || !GitCommitPattern().IsMatch(manifest.GitCommit)
            || manifest.PublishedAtUtc == default
            || manifest.PublishedAtUtc > now.AddMinutes(10)
            || !Version.TryParse(
                manifest.MinimumWindowsVersion,
                out _)
            || manifest.ReleaseNotes.Length > 32 * 1024
            || manifest.Artifacts.Count is < 1 or > 4)
        {
            throw new InvalidDataException(
                "The release manifest contains invalid identity or compatibility metadata.");
        }

        var kinds = new HashSet<HypershellReleaseArtifactKind>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in manifest.Artifacts)
        {
            var maximum = artifact.Kind
                == HypershellReleaseArtifactKind.Installer
                ? HypershellReleaseContract.MaximumInstallerBytes
                : HypershellReleaseContract.MaximumPortableBytes;
            if (!kinds.Add(artifact.Kind)
                || !names.Add(artifact.FileName)
                || artifact.FileName.Length is < 1 or > 160
                || artifact.FileName != Path.GetFileName(artifact.FileName)
                || artifact.FileName.IndexOfAny(
                    Path.GetInvalidFileNameChars()) >= 0
                || artifact.ByteLength is <= 0
                || artifact.ByteLength > maximum
                || artifact.Sha256.Length != 64
                || !artifact.Sha256.All(char.IsAsciiHexDigit)
                || !string.Equals(
                    artifact.Sha256,
                    artifact.Sha256.ToLowerInvariant(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The release manifest contains an invalid or duplicate artifact.");
            }
        }

        if (!kinds.Contains(HypershellReleaseArtifactKind.Installer)
            || (manifest.Channel is HypershellReleaseContract.StableChannel
                or HypershellReleaseContract.CompleteStableChannel)
            && manifest.Artifacts
                .Where(artifact =>
                    artifact.Kind
                    == HypershellReleaseArtifactKind.Installer)
                .Any(artifact => !artifact.RequiresAuthenticode))
        {
            throw new InvalidDataException(
                "A release must contain an installer; stable installers must require Authenticode.");
        }
    }

    private static byte[] DecodeBounded(
        string value,
        int maximumBytes,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > ((maximumBytes + 2) / 3 * 4) + 8)
        {
            throw new InvalidDataException(
                $"The release {field} has an invalid size.");
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            if (decoded.Length is <= 0 || decoded.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The release {field} has an invalid size.");
            }

            return decoded;
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"The release {field} is not valid base64.",
                exception);
        }
    }

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
            throw new InvalidDataException(
                "A trusted release-signer SHA-256 fingerprint is malformed.");
        }

        return normalized;
    }

    private static void ValidateSigningCertificateUsage(
        X509Certificate2 certificate)
    {
        var constraints = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .ToList();
        var keyUsages = certificate.Extensions
            .OfType<X509KeyUsageExtension>()
            .ToList();
        var enhancedKeyUsages = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .ToList();
        if (constraints.Count > 1
            || constraints.SingleOrDefault()?.CertificateAuthority == true
            || keyUsages.Count != 1
            || !keyUsages[0].KeyUsages.HasFlag(
                X509KeyUsageFlags.DigitalSignature)
            || enhancedKeyUsages.Count != 1
            || !enhancedKeyUsages[0].EnhancedKeyUsages
                .Cast<Oid>()
                .Any(usage => string.Equals(
                    usage.Value,
                    CodeSigningEnhancedKeyUsage,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The release-signing certificate is not restricted to a valid digital code-signing use.");
        }
    }

    private static bool VerifyRsa(
        X509Certificate2 certificate,
        byte[] payload,
        byte[] signature)
    {
        using var rsa = certificate.GetRSAPublicKey();
        return rsa is not null
               && rsa.VerifyData(
                   payload,
                   signature,
                   HashAlgorithmName.SHA256,
                   RSASignaturePadding.Pkcs1);
    }

    private static bool VerifyEcdsa(
        X509Certificate2 certificate,
        byte[] payload,
        byte[] signature)
    {
        using var ecdsa = certificate.GetECDsaPublicKey();
        return ecdsa is not null
               && ecdsa.VerifyData(
                   payload,
                   signature,
                   HashAlgorithmName.SHA256);
    }
}
