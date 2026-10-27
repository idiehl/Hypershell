using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Hypershell.ProfileServer;

public sealed class SshCredentialSecretMaterial : IDisposable
{
    public SshCredentialSecretMaterial(
        byte[] privateKeyUtf8,
        byte[] passphraseUtf8)
    {
        PrivateKeyUtf8 = privateKeyUtf8;
        PassphraseUtf8 = passphraseUtf8;
    }

    public byte[] PrivateKeyUtf8 { get; }

    public byte[] PassphraseUtf8 { get; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(PrivateKeyUtf8);
        CryptographicOperations.ZeroMemory(PassphraseUtf8);
    }
}

/// <summary>
/// Encrypts each SSH secret with a random data key, then wraps that key with
/// the server-held master key. Credential identity and key version are bound
/// as AES-GCM associated data, preventing ciphertext substitution.
/// </summary>
public sealed class SshSecretEnvelopeService
{
    private const byte EnvelopeVersion = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int DataKeyBytes = 32;
    private const int HeaderBytes = 1 + NonceBytes + TagBytes + DataKeyBytes
                                    + NonceBytes + TagBytes + sizeof(int);

    private readonly SshVaultMasterKeyStore _masterKeys;

    public SshSecretEnvelopeService(SshVaultMasterKeyStore masterKeys)
    {
        _masterKeys = masterKeys;
    }

    public byte[] Encrypt(
        string accountId,
        string credentialId,
        int keyVersion,
        ReadOnlySpan<byte> privateKeyUtf8,
        ReadOnlySpan<byte> passphraseUtf8)
    {
        ValidateLengths(privateKeyUtf8.Length, passphraseUtf8.Length);
        var plaintext = Pack(privateKeyUtf8, passphraseUtf8);
        var associatedData = CreateAssociatedData(
            accountId,
            credentialId,
            keyVersion);
        var wrapAssociatedData = AppendWrapMarker(associatedData);
        var dataKey = RandomNumberGenerator.GetBytes(DataKeyBytes);
        var masterKey = _masterKeys.ReadOrCreate();
        var wrapNonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var wrapTag = new byte[TagBytes];
        var wrappedDataKey = new byte[DataKeyBytes];
        var payloadNonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var payloadTag = new byte[TagBytes];
        var ciphertext = new byte[plaintext.Length];
        try
        {
            using (var wrapper = new AesGcm(masterKey, TagBytes))
            {
                wrapper.Encrypt(
                    wrapNonce,
                    dataKey,
                    wrappedDataKey,
                    wrapTag,
                    wrapAssociatedData);
            }

            using (var cipher = new AesGcm(dataKey, TagBytes))
            {
                cipher.Encrypt(
                    payloadNonce,
                    plaintext,
                    ciphertext,
                    payloadTag,
                    associatedData);
            }

            var envelope = new byte[HeaderBytes + ciphertext.Length];
            var offset = 0;
            envelope[offset++] = EnvelopeVersion;
            Copy(wrapNonce, envelope, ref offset);
            Copy(wrapTag, envelope, ref offset);
            Copy(wrappedDataKey, envelope, ref offset);
            Copy(payloadNonce, envelope, ref offset);
            Copy(payloadTag, envelope, ref offset);
            BinaryPrimitives.WriteInt32LittleEndian(
                envelope.AsSpan(offset, sizeof(int)),
                ciphertext.Length);
            offset += sizeof(int);
            Copy(ciphertext, envelope, ref offset);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(associatedData);
            CryptographicOperations.ZeroMemory(wrapAssociatedData);
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public SshCredentialSecretMaterial Decrypt(
        string accountId,
        string credentialId,
        int keyVersion,
        ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < HeaderBytes || envelope[0] != EnvelopeVersion)
        {
            throw new CryptographicException(
                "The SSH credential envelope is malformed or unsupported.");
        }

        var offset = 1;
        var wrapNonce = envelope.Slice(offset, NonceBytes);
        offset += NonceBytes;
        var wrapTag = envelope.Slice(offset, TagBytes);
        offset += TagBytes;
        var wrappedDataKey = envelope.Slice(offset, DataKeyBytes);
        offset += DataKeyBytes;
        var payloadNonce = envelope.Slice(offset, NonceBytes);
        offset += NonceBytes;
        var payloadTag = envelope.Slice(offset, TagBytes);
        offset += TagBytes;
        var ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(
            envelope.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        if (ciphertextLength < 8
            || ciphertextLength > 2 * 1024 * 1024
            || offset + ciphertextLength != envelope.Length)
        {
            throw new CryptographicException(
                "The SSH credential envelope length is invalid.");
        }

        var associatedData = CreateAssociatedData(
            accountId,
            credentialId,
            keyVersion);
        var wrapAssociatedData = AppendWrapMarker(associatedData);
        var masterKey = _masterKeys.ReadOrCreate();
        var dataKey = new byte[DataKeyBytes];
        var plaintext = new byte[ciphertextLength];
        try
        {
            using (var wrapper = new AesGcm(masterKey, TagBytes))
            {
                wrapper.Decrypt(
                    wrapNonce,
                    wrappedDataKey,
                    wrapTag,
                    dataKey,
                    wrapAssociatedData);
            }

            using (var cipher = new AesGcm(dataKey, TagBytes))
            {
                cipher.Decrypt(
                    payloadNonce,
                    envelope.Slice(offset, ciphertextLength),
                    payloadTag,
                    plaintext,
                    associatedData);
            }

            return Unpack(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(associatedData);
            CryptographicOperations.ZeroMemory(wrapAssociatedData);
            CryptographicOperations.ZeroMemory(masterKey);
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Pack(
        ReadOnlySpan<byte> privateKeyUtf8,
        ReadOnlySpan<byte> passphraseUtf8)
    {
        var payload = new byte[
            sizeof(int) + privateKeyUtf8.Length
            + sizeof(int) + passphraseUtf8.Length];
        var offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(offset, sizeof(int)),
            privateKeyUtf8.Length);
        offset += sizeof(int);
        privateKeyUtf8.CopyTo(payload.AsSpan(offset));
        offset += privateKeyUtf8.Length;
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(offset, sizeof(int)),
            passphraseUtf8.Length);
        offset += sizeof(int);
        passphraseUtf8.CopyTo(payload.AsSpan(offset));
        return payload;
    }

    private static SshCredentialSecretMaterial Unpack(ReadOnlySpan<byte> value)
    {
        if (value.Length < 8)
        {
            throw new CryptographicException(
                "The decrypted SSH credential is truncated.");
        }

        var offset = 0;
        var privateKeyLength = BinaryPrimitives.ReadInt32LittleEndian(
            value.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        if (privateKeyLength <= 0
            || privateKeyLength > 1024 * 1024
            || offset + privateKeyLength + sizeof(int) > value.Length)
        {
            throw new CryptographicException(
                "The decrypted SSH private-key length is invalid.");
        }

        var privateKey = value.Slice(offset, privateKeyLength).ToArray();
        offset += privateKeyLength;
        var passphraseLength = BinaryPrimitives.ReadInt32LittleEndian(
            value.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        if (passphraseLength < 0
            || passphraseLength > 16 * 1024
            || offset + passphraseLength != value.Length)
        {
            CryptographicOperations.ZeroMemory(privateKey);
            throw new CryptographicException(
                "The decrypted SSH passphrase length is invalid.");
        }

        return new SshCredentialSecretMaterial(
            privateKey,
            value.Slice(offset, passphraseLength).ToArray());
    }

    private static void ValidateLengths(int privateKeyLength, int passphraseLength)
    {
        if (privateKeyLength is <= 0 or > 1024 * 1024)
        {
            throw new ArgumentException(
                "The SSH private key must contain 1-1,048,576 UTF-8 bytes.");
        }

        if (passphraseLength is < 0 or > 16 * 1024)
        {
            throw new ArgumentException(
                "The SSH passphrase exceeds its 16-KiB UTF-8 limit.");
        }
    }

    private static byte[] CreateAssociatedData(
        string accountId,
        string credentialId,
        int keyVersion) =>
        Encoding.UTF8.GetBytes(
            $"Hypershell.SshVault.v1\n{accountId}\n{credentialId}\n{keyVersion}");

    private static byte[] AppendWrapMarker(ReadOnlySpan<byte> associatedData)
    {
        var suffix = "\nwrap"u8;
        var result = new byte[associatedData.Length + suffix.Length];
        associatedData.CopyTo(result);
        suffix.CopyTo(result.AsSpan(associatedData.Length));
        return result;
    }

    private static void Copy(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        ref int offset)
    {
        source.CopyTo(destination[offset..]);
        offset += source.Length;
    }
}
