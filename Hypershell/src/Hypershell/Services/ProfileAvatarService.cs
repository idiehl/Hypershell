using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hypershell.Services;

public sealed class ProfileAvatarService
{
    public const int MaximumSourceBytes = 12 * 1024 * 1024;
    public const int AvatarPixelSize = 256;

    public ProfileAvatarService(string avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath))
        {
            throw new ArgumentException(
                "A profile-avatar path is required.",
                nameof(avatarPath));
        }

        AvatarPath = Path.GetFullPath(avatarPath);
    }

    public string AvatarPath { get; }

    public ImageSource? TryLoad()
    {
        if (!File.Exists(AvatarPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                AvatarPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or InvalidOperationException)
        {
            return null;
        }
    }

    public void Save(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException(
                "Choose an image file.",
                nameof(sourcePath));
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(fullSourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException(
                "The selected profile image no longer exists.",
                fullSourcePath);
        }

        if (sourceInfo.Length <= 0
            || sourceInfo.Length > MaximumSourceBytes)
        {
            throw new InvalidOperationException(
                $"Choose a non-empty image no larger than {MaximumSourceBytes / (1024 * 1024)} MB.");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(AvatarPath)
            ?? throw new InvalidOperationException(
                "The profile-avatar destination has no parent directory."));
        var temporaryPath = AvatarPath
                            + ".tmp-"
                            + Guid.NewGuid().ToString("N");
        try
        {
            var image = DecodeBoundedImage(fullSourcePath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                encoder.Save(destination);
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, AvatarPath, overwrite: true);
        }
        catch (Exception exception) when (exception is FileFormatException
                                         or NotSupportedException
                                         or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Hypershell could not decode that image. Choose a PNG, JPEG, WebP, BMP, GIF, or TIFF file.",
                exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Remove()
    {
        if (File.Exists(AvatarPath))
        {
            File.Delete(AvatarPath);
        }
    }

    public static string CreateInitials(
        string? accountIdentity,
        string? fallbackIdentity = null)
    {
        var identity = string.IsNullOrWhiteSpace(accountIdentity)
            ? fallbackIdentity
            : accountIdentity;
        if (string.IsNullOrWhiteSpace(identity))
        {
            return "AU";
        }

        var localIdentity = identity.Trim();
        var atIndex = localIdentity.IndexOf('@');
        if (atIndex > 0)
        {
            localIdentity = localIdentity[..atIndex];
        }

        var parts = localIdentity
            .Split(
                [' ', '.', '_', '-'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Select(part => new string(part
                .Where(char.IsLetterOrDigit)
                .ToArray()))
            .Where(part => part.Length > 0)
            .ToArray();
        if (parts.Length >= 2)
        {
            return string.Concat(parts[0][0], parts[^1][0])
                .ToUpperInvariant();
        }

        var compact = parts.FirstOrDefault()
                      ?? new string(localIdentity
                          .Where(char.IsLetterOrDigit)
                          .ToArray());
        return compact.Length switch
        {
            0 => "AU",
            1 => compact.ToUpperInvariant(),
            _ => compact[..2].ToUpperInvariant()
        };
    }

    private static BitmapSource DecodeBoundedImage(string sourcePath)
    {
        int pixelWidth;
        int pixelHeight;
        using (var metadataStream = new FileStream(
                   sourcePath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(
                metadataStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault()
                        ?? throw new InvalidOperationException(
                            "The selected file contains no image frame.");
            pixelWidth = frame.PixelWidth;
            pixelHeight = frame.PixelHeight;
        }

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new InvalidOperationException(
                "The selected image has invalid dimensions.");
        }

        using var imageStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        if (pixelWidth >= pixelHeight)
        {
            image.DecodePixelWidth = AvatarPixelSize;
        }
        else
        {
            image.DecodePixelHeight = AvatarPixelSize;
        }

        image.StreamSource = imageStream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
