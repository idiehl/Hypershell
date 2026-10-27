using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hypershell.ProfileServer.Pages;

public sealed class PublicDownloadsModel(ReleaseStore releases) : PageModel
{
    public IReadOnlyList<PublicHypershellRelease> Releases { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Releases = await releases.ListPublicAsync(cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    public static string DownloadPath(string version, string edition, string kind = "installer") =>
        $"/downloads/{Uri.EscapeDataString(version)}/{edition}/{kind}";

    public static string SizeOf(StoredHypershellRelease release) =>
        FormatBytes(release.Artifacts.Single(a => a.Artifact.Kind == HypershellReleaseArtifactKind.Installer).Artifact.ByteLength);

    public static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
        : $"{bytes / 1024d / 1024d:0.0} MB";
}
