using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class HypershellReleaseTrustStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumTrustDocumentBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IReadOnlyList<string> _signerCertificateSha256;

    public HypershellReleaseTrustStore(string? trustDocumentPath = null)
    {
        TrustDocumentPath = Path.GetFullPath(
            trustDocumentPath
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Release",
                "trusted-signers.json"));
        _signerCertificateSha256 = Load();
    }

    public string TrustDocumentPath { get; }

    public IReadOnlyList<string> SignerCertificateSha256 =>
        _signerCertificateSha256;

    public bool IsConfigured => _signerCertificateSha256.Count > 0;

    private IReadOnlyList<string> Load()
    {
        var info = new FileInfo(TrustDocumentPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumTrustDocumentBytes)
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<ReleaseTrustDocument>(
                               File.ReadAllBytes(info.FullName),
                               JsonOptions)
                           ?? throw new JsonException(
                               "The release trust document was empty.");
            if (document.SchemaVersion != CurrentSchemaVersion
                || document.SignerCertificateSha256.Count > 8)
            {
                throw new JsonException(
                    "The release trust document schema or signer count is invalid.");
            }

            return document.SignerCertificateSha256
                .Select(NormalizeSha256)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException
                                         or InvalidDataException)
        {
            throw new InvalidDataException(
                "The bundled release trust policy is malformed.",
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
                "A bundled release-signer SHA-256 fingerprint is malformed.");
        }

        return normalized;
    }

    private sealed class ReleaseTrustDocument
    {
        [JsonRequired]
        public int SchemaVersion { get; init; }

        [JsonRequired]
        public List<string> SignerCertificateSha256 { get; init; } = [];
    }
}
