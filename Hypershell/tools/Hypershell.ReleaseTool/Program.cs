using Hypershell.Profiles;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

return await RunAsync(args);

static Task<int> RunAsync(string[] arguments)
{
    try
    {
        if (arguments.Length == 0)
        {
            WriteUsage();
            return Task.FromResult(2);
        }

        return Task.FromResult(arguments[0].ToLowerInvariant() switch
        {
            "sign-manifest" => SignManifest(arguments),
            "verify-bundle" => VerifyBundle(arguments),
            "signer-info" => WriteSignerInfo(arguments),
            _ => UnknownCommand()
        });
    }
    catch (Exception exception) when (exception is ArgumentException
                                      or InvalidDataException
                                      or InvalidOperationException
                                      or IOException
                                      or UnauthorizedAccessException
                                      or CryptographicException)
    {
        Console.Error.WriteLine($"RELEASE_TOOL_FAILED|{exception.Message}");
        return Task.FromResult(1);
    }
}

static int SignManifest(string[] arguments)
{
    var installerPath = Path.GetFullPath(
        RequireOption(arguments, "--installer"));
    var portableOption = ReadOption(arguments, "--portable");
    var portablePath = string.IsNullOrWhiteSpace(portableOption)
        ? null
        : Path.GetFullPath(portableOption);
    var notesPath = Path.GetFullPath(
        RequireOption(arguments, "--notes-file"));
    var notes = ReadBoundedText(notesPath, 32 * 1024);
    var publishedAt = DateTimeOffset.Parse(
        RequireOption(arguments, "--published-at"),
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind);
    using var certificate = LoadSigningCertificate(arguments);
    var artifacts = new List<HypershellReleaseArtifact>
    {
        CreateArtifact(
            HypershellReleaseArtifactKind.Installer,
            installerPath,
            requiresAuthenticode: true)
    };
    if (portablePath is not null)
    {
        artifacts.Add(
            CreateArtifact(
                HypershellReleaseArtifactKind.Portable,
                portablePath,
                requiresAuthenticode: false));
    }

    var manifest = new HypershellReleaseManifest
    {
        Version = RequireOption(arguments, "--version"),
        Channel = ReadOption(arguments, "--channel")
                  ?? HypershellReleaseContract.StableChannel,
        GitCommit = RequireOption(arguments, "--git-commit"),
        PublishedAtUtc = publishedAt,
        MinimumWindowsVersion =
            ReadOption(arguments, "--minimum-windows")
            ?? "10.0.17763",
        ReleaseNotes = notes,
        Artifacts = artifacts
    };
    var security = new HypershellReleaseManifestSecurity();
    var signed = security.Sign(manifest, certificate, publishedAt);
    var outputPath = Path.GetFullPath(
        RequireOption(arguments, "--output"));
    Directory.CreateDirectory(
        Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException(
            "The release-manifest output has no parent directory."));
    File.WriteAllBytes(
        outputPath,
        security.SerializeEnvelope(signed));
    Console.WriteLine(
        $"RELEASE_MANIFEST_SIGNED|{manifest.Version}|{ComputeCertificateSha256(certificate)}|{outputPath}");
    return 0;
}

static int VerifyBundle(string[] arguments)
{
    var manifestPath = Path.GetFullPath(
        RequireOption(arguments, "--manifest"));
    var security = new HypershellReleaseManifestSecurity();
    var signed = security.DeserializeEnvelope(
        ReadBoundedBytes(
            manifestPath,
            HypershellReleaseContract.MaximumSignedManifestBytes));
    var trustedSigners = ReadOptions(
        arguments,
        "--trusted-signer");
    using var verified = new VerifiedLease(
        security.Verify(
            signed,
            trustedSigners,
            requireTrustedCertificateChain:
                HasOption(arguments, "--require-chain")));
    var bundleRoot = Path.GetDirectoryName(manifestPath)
                     ?? throw new InvalidOperationException(
                         "The signed manifest has no bundle directory.");
    foreach (var artifact in verified.Value.Manifest.Artifacts)
    {
        security.VerifyArtifact(
            Path.Combine(bundleRoot, artifact.FileName),
            artifact);
    }

    Console.WriteLine(
        $"RELEASE_BUNDLE_VALID|{verified.Value.Manifest.Version}|{verified.Value.SigningCertificateSha256}|{verified.Value.Manifest.Artifacts.Count}");
    return 0;
}

static int WriteSignerInfo(string[] arguments)
{
    using var certificate = LoadSigningCertificate(arguments);
    Console.WriteLine(
        $"RELEASE_SIGNER|{ComputeCertificateSha256(certificate)}|{certificate.Subject}|{certificate.NotAfter.ToUniversalTime():O}");
    return 0;
}

static X509Certificate2 LoadSigningCertificate(string[] arguments)
{
    var certificatePath = ReadOption(arguments, "--certificate-path");
    var thumbprint = ReadOption(arguments, "--certificate-thumbprint");
    if (!string.IsNullOrWhiteSpace(certificatePath)
        == !string.IsNullOrWhiteSpace(thumbprint))
    {
        throw new ArgumentException(
            "Provide exactly one of --certificate-path or --certificate-thumbprint.");
    }

    if (!string.IsNullOrWhiteSpace(certificatePath))
    {
        var passwordPath = Path.GetFullPath(
            RequireOption(arguments, "--certificate-password-file"));
        var password = ReadBoundedText(passwordPath, 4096)
            .TrimEnd('\r', '\n');
        return X509CertificateLoader.LoadPkcs12FromFile(
            Path.GetFullPath(certificatePath),
            password,
            X509KeyStorageFlags.EphemeralKeySet);
    }

    var normalizedThumbprint = NormalizeThumbprint(thumbprint!);
    using var store = new X509Store(
        StoreName.My,
        StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
    var certificate = store.Certificates
        .Find(
            X509FindType.FindByThumbprint,
            normalizedThumbprint,
            validOnly: false)
        .OfType<X509Certificate2>()
        .FirstOrDefault(candidate => candidate.HasPrivateKey)
        ?? throw new InvalidOperationException(
            "The requested CurrentUser code-signing certificate with a private key was not found.");
    return new X509Certificate2(certificate);
}

static HypershellReleaseArtifact CreateArtifact(
    HypershellReleaseArtifactKind kind,
    string path,
    bool requiresAuthenticode)
{
    var info = new FileInfo(path);
    if (!info.Exists || info.Length <= 0)
    {
        throw new InvalidDataException(
            $"Release artifact '{info.FullName}' is missing or empty.");
    }

    using var stream = new FileStream(
        info.FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        128 * 1024,
        FileOptions.SequentialScan);
    return new HypershellReleaseArtifact
    {
        Kind = kind,
        FileName = info.Name,
        ByteLength = info.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant(),
        RequiresAuthenticode = requiresAuthenticode
    };
}

static string ComputeCertificateSha256(X509Certificate2 certificate) =>
    Convert.ToHexString(SHA256.HashData(certificate.RawData))
        .ToLowerInvariant();

static byte[] ReadBoundedBytes(string path, int maximumBytes)
{
    var info = new FileInfo(path);
    if (!info.Exists || info.Length is <= 0 || info.Length > maximumBytes)
    {
        throw new InvalidDataException(
            $"'{info.FullName}' is missing, empty, or exceeds its size limit.");
    }

    return File.ReadAllBytes(info.FullName);
}

static string ReadBoundedText(string path, int maximumBytes) =>
    System.Text.Encoding.UTF8.GetString(
        ReadBoundedBytes(path, maximumBytes));

static string? ReadOption(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(
                arguments[index],
                name,
                StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }

    return null;
}

static IReadOnlyList<string> ReadOptions(
    string[] arguments,
    string name)
{
    var values = new List<string>();
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(
                arguments[index],
                name,
                StringComparison.OrdinalIgnoreCase))
        {
            values.Add(arguments[index + 1]);
            index++;
        }
    }

    if (values.Count == 0)
    {
        throw new ArgumentException(
            $"At least one '{name}' value is required.");
    }

    return values;
}

static string RequireOption(string[] arguments, string name) =>
    ReadOption(arguments, name)
    ?? throw new ArgumentException(
        $"Required option '{name}' is missing.");

static bool HasOption(string[] arguments, string name) =>
    arguments.Any(argument => string.Equals(
        argument,
        name,
        StringComparison.OrdinalIgnoreCase));

static string NormalizeThumbprint(string value)
{
    var normalized = value
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace(":", string.Empty, StringComparison.Ordinal)
        .ToUpperInvariant();
    if (normalized.Length < 40 || !normalized.All(char.IsAsciiHexDigit))
    {
        throw new ArgumentException(
            "The certificate thumbprint is malformed.");
    }

    return normalized;
}

static int UnknownCommand()
{
    WriteUsage();
    return 2;
}

static void WriteUsage()
{
    Console.Error.WriteLine(
        "Usage:\n"
        + "  Hypershell.ReleaseTool sign-manifest --version <semver> --git-commit <hash> --published-at <ISO-8601> --notes-file <path> --installer <msi> [--portable <zip>] (--certificate-path <pfx> --certificate-password-file <path> | --certificate-thumbprint <thumbprint>) --output <release-manifest.json>\n"
        + "  Hypershell.ReleaseTool verify-bundle --manifest <release-manifest.json> --trusted-signer <sha256> [--trusted-signer <sha256> ...] [--require-chain]\n"
        + "  Hypershell.ReleaseTool signer-info (--certificate-path <pfx> --certificate-password-file <path> | --certificate-thumbprint <thumbprint>)");
}

sealed class VerifiedLease : IDisposable
{
    public VerifiedLease(VerifiedHypershellReleaseManifest value)
    {
        Value = value;
    }

    public VerifiedHypershellReleaseManifest Value { get; }

    public void Dispose() => Value.SigningCertificate.Dispose();
}
