using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Hypershell.Services;

public sealed record AuthenticodeVerificationResult(
    string SignerCertificateSha256,
    string SignerSubject);

public interface IAuthenticodeVerifier
{
    AuthenticodeVerificationResult Verify(
        string path,
        string expectedSignerCertificateSha256);
}

public sealed class WindowsAuthenticodeVerifier : IAuthenticodeVerifier
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdRevocationCheckChainExcludeRoot = 0x80;
    private const uint WtdSaferFlag = 0x100;

    private static readonly Guid GenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public AuthenticodeVerificationResult Verify(
        string path,
        string expectedSignerCertificateSha256)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Authenticode verification requires Windows.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The update artifact does not exist.",
                fullPath);
        }

        var expectedSigner = NormalizeSha256(
            expectedSignerCertificateSha256);
        var pathPointer = Marshal.StringToCoTaskMemUni(fullPath);
        var fileInfoPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = pathPointer
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(
                Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(
                fileInfo,
                fileInfoPointer,
                false);
            var trustData = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeWholeChain,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = WtdStateActionIgnore,
                ProviderFlags =
                    WtdRevocationCheckChainExcludeRoot
                    | WtdSaferFlag
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(
                new IntPtr(-1),
                ref action,
                ref trustData);
            if (result != 0)
            {
                throw new InvalidDataException(
                    $"Windows rejected the Authenticode signature (0x{unchecked((uint)result):X8}).");
            }

#pragma warning disable SYSLIB0057 // The legacy API is the Windows framework's signed-file certificate extractor; trust is established by WinVerifyTrust above.
            using var legacyCertificate =
                X509Certificate.CreateFromSignedFile(fullPath);
#pragma warning restore SYSLIB0057
            using var signer = X509CertificateLoader.LoadCertificate(
                legacyCertificate.Export(X509ContentType.Cert));
            var actualSigner = Convert.ToHexString(
                    SHA256.HashData(signer.RawData))
                .ToLowerInvariant();
            if (!string.Equals(
                    actualSigner,
                    expectedSigner,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Authenticode signer does not match the signed release manifest.");
            }

            return new AuthenticodeVerificationResult(
                actualSigner,
                signer.Subject);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                "The Authenticode signer certificate could not be read.",
                exception);
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }

            Marshal.FreeCoTaskMem(pathPointer);
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
                "The expected Authenticode signer fingerprint is malformed.");
        }

        return normalized;
    }

    [DllImport(
        "wintrust.dll",
        ExactSpelling = true,
        PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        ref Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
