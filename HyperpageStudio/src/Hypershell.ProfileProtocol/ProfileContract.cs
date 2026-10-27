namespace Hypershell.Profiles;

public static class ProfileContract
{
    public const string BundleFormat = "hypershell-profile";
    public const int CurrentBundleVersion = 1;
    public const int CurrentPortableSchemaVersion = 1;
    public const int CurrentDeviceStateSchemaVersion = 1;
    public const long MaximumBundleBytes = 4L * 1024 * 1024;
    public const string FileExtension = ".hyperprofile";
}
