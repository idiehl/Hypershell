using Hypershell.Models;
using System.IO;
using System.Text;

namespace Hypershell.Services;

public sealed record TextDocumentSnapshot(
    string Text,
    DocumentEncodingKind EncodingKind,
    DocumentLineEndingKind LineEndingKind);

public sealed class TextDocumentService
{
    public const long MaximumDocumentBytes = 32L * 1024L * 1024L;

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<TextDocumentSnapshot> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The document no longer exists.", fullPath);
        }

        if (file.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                $"The document is larger than {MaximumDocumentBytes / 1024 / 1024} MiB. " +
                "Open large or binary files with a dedicated document extension.");
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return Decode(bytes);
    }

    public async Task SaveAsync(
        string path,
        string text,
        DocumentEncodingKind encodingKind,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The document path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var bytes = Encode(text, encodingKind);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.hypershell-save");
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                bytes,
                cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static TextDocumentSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        var (encodingKind, encoding, preambleLength) = DetectEncoding(bytes);
        string text;
        try
        {
            text = encoding.GetString(bytes[preambleLength..]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "This file is not valid UTF-8 or a supported Unicode text document.",
                exception);
        }

        if (text.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException(
                "This file appears to be binary. Install or select a document extension that supports its format.");
        }

        return new TextDocumentSnapshot(
            text,
            encodingKind,
            DetectLineEnding(text));
    }

    public static byte[] Encode(
        string text,
        DocumentEncodingKind encodingKind)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoding = CreateEncoding(encodingKind);
        var content = encoding.GetBytes(text);
        var preamble = encoding.GetPreamble();
        var bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    public static DocumentLineEndingKind DetectLineEnding(string text)
    {
        var crLfCount = 0;
        var lfCount = 0;
        var crCount = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crLfCount++;
                    index++;
                }
                else
                {
                    crCount++;
                }
            }
            else if (text[index] == '\n')
            {
                lfCount++;
            }
        }

        var kindsPresent = (crLfCount > 0 ? 1 : 0)
                           + (lfCount > 0 ? 1 : 0)
                           + (crCount > 0 ? 1 : 0);
        if (kindsPresent > 1)
        {
            return DocumentLineEndingKind.Preserve;
        }

        if (lfCount > 0)
        {
            return DocumentLineEndingKind.Lf;
        }

        if (crCount > 0)
        {
            return DocumentLineEndingKind.Cr;
        }

        return DocumentLineEndingKind.CrLf;
    }

    public static string GetLineEndingSequence(DocumentLineEndingKind kind) =>
        kind switch
        {
            DocumentLineEndingKind.Lf => "\n",
            DocumentLineEndingKind.Cr => "\r",
            _ => "\r\n"
        };

    private static Encoding CreateEncoding(DocumentEncodingKind kind) =>
        kind switch
        {
            DocumentEncodingKind.Utf8 => new UTF8Encoding(false, true),
            DocumentEncodingKind.Utf8WithBom => new UTF8Encoding(true, true),
            DocumentEncodingKind.Utf16LittleEndian => new UnicodeEncoding(false, true, true),
            DocumentEncodingKind.Utf16BigEndian => new UnicodeEncoding(true, true, true),
            DocumentEncodingKind.Utf32LittleEndian => new UTF32Encoding(false, true, true),
            DocumentEncodingKind.Utf32BigEndian => new UTF32Encoding(true, true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static (DocumentEncodingKind Kind, Encoding Encoding, int PreambleLength)
        DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
        {
            return (
                DocumentEncodingKind.Utf8WithBom,
                new UTF8Encoding(false, true),
                3);
        }

        if (StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00]))
        {
            return (
                DocumentEncodingKind.Utf32LittleEndian,
                new UTF32Encoding(false, false, true),
                4);
        }

        if (StartsWith(bytes, [0x00, 0x00, 0xFE, 0xFF]))
        {
            return (
                DocumentEncodingKind.Utf32BigEndian,
                new UTF32Encoding(true, false, true),
                4);
        }

        if (StartsWith(bytes, [0xFF, 0xFE]))
        {
            return (
                DocumentEncodingKind.Utf16LittleEndian,
                new UnicodeEncoding(false, false, true),
                2);
        }

        if (StartsWith(bytes, [0xFE, 0xFF]))
        {
            return (
                DocumentEncodingKind.Utf16BigEndian,
                new UnicodeEncoding(true, false, true),
                2);
        }

        return (DocumentEncodingKind.Utf8, StrictUtf8, 0);
    }

    private static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix) =>
        value.Length >= prefix.Length
        && value[..prefix.Length].SequenceEqual(prefix);
}
