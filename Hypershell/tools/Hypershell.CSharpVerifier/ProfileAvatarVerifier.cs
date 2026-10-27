using Hypershell.Services;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ProfileAvatarVerifier
{
    public static void Verify(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "profile-avatar");
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.png");
        var avatarPath = Path.Combine(root, "profile-avatar.device.png");

        const int width = 600;
        const int height = 300;
        const int bytesPerPixel = 4;
        var pixels = new byte[width * height * bytesPerPixel];
        for (var index = 0; index < pixels.Length; index += bytesPerPixel)
        {
            pixels[index] = 0xCE;
            pixels[index + 1] = 0x6F;
            pixels[index + 2] = 0x2F;
            pixels[index + 3] = 0xFF;
        }

        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * bytesPerPixel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(sourcePath))
        {
            encoder.Save(output);
        }

        var service = new ProfileAvatarService(avatarPath);
        service.Save(sourcePath);
        var loaded = service.TryLoad() as BitmapSource;
        Require(
            File.Exists(avatarPath)
            && loaded is not null
            && loaded.PixelWidth == ProfileAvatarService.AvatarPixelSize
            && loaded.PixelHeight == ProfileAvatarService.AvatarPixelSize / 2,
            "The profile avatar was not normalized to a persistent bounded PNG.");
        Require(
            ProfileAvatarService.CreateInitials(
                "ian.higgins@example.com") == "IH"
            && ProfileAvatarService.CreateInitials(
                null,
                "ihigg") == "IH",
            "Profile-avatar initials were not derived predictably.");

        service.Remove();
        Require(
            !File.Exists(avatarPath) && service.TryLoad() is null,
            "Removing the profile avatar did not restore the initials fallback.");

        Console.WriteLine(
            "PROFILE_AVATAR_OK|bounded-png|persistent-local-copy|initials-fallback|remove");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
