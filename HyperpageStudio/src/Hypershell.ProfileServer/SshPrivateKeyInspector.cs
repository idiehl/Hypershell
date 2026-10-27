using Renci.SshNet;
using Renci.SshNet.Common;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.ProfileServer;

public sealed record SshPrivateKeyIdentity(
    string Fingerprint,
    IReadOnlyList<string> Algorithms);

public static class SshPrivateKeyInspector
{
    public static SshPrivateKeyIdentity Inspect(
        ReadOnlySpan<byte> privateKeyUtf8,
        ReadOnlySpan<byte> passphraseUtf8)
    {
        if (privateKeyUtf8 is { Length: <= 0 or > 1024 * 1024 })
        {
            throw new ArgumentException(
                "The SSH private key must contain 1-1,048,576 UTF-8 bytes.");
        }

        if (passphraseUtf8.Length > 16 * 1024)
        {
            throw new ArgumentException(
                "The SSH passphrase exceeds its 16-KiB UTF-8 limit.");
        }

        var keyBytes = privateKeyUtf8.ToArray();
        string? passphrase = null;
        try
        {
            using var stream = new MemoryStream(
                keyBytes,
                writable: false);
            PrivateKeyFile key;
            if (passphraseUtf8.IsEmpty)
            {
                key = new PrivateKeyFile(stream);
            }
            else
            {
                passphrase = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                    .GetString(passphraseUtf8);
                key = new PrivateKeyFile(stream, passphrase);
            }

            using (key)
            {
                var algorithms = key.HostKeyAlgorithms
                    .Select(algorithm => algorithm.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                var publicKey = key.HostKeyAlgorithms.FirstOrDefault()?.Data;
                if (publicKey is null || publicKey.Length == 0)
                {
                    throw new InvalidDataException(
                        "The SSH private key contains no public host algorithm.");
                }

                var digest = SHA256.HashData(publicKey);
                try
                {
                    return new SshPrivateKeyIdentity(
                        "SHA256:"
                        + Convert.ToBase64String(digest).TrimEnd('='),
                        algorithms);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(digest);
                }
            }
        }
        catch (Exception exception) when (exception is
                                          SshException
                                          or InvalidDataException
                                          or DecoderFallbackException
                                          or ArgumentException)
        {
            throw new ArgumentException(
                "The SSH private key or passphrase is invalid or unsupported.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            _ = passphrase;
        }
    }
}
