using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hypershell.ProfileServer.Pages;

public sealed class DownloadsModel : PageModel
{
    private readonly ReleaseStore _releases;

    public DownloadsModel(ReleaseStore releases)
    {
        _releases = releases;
    }

    public IReadOnlyList<HypershellReleaseSummary> Releases { get; private set; }
        = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Releases = await _releases.ListAsync(
            HypershellReleaseContract.StableChannel,
            cancellationToken);
    }

    public async Task<IActionResult> OnGetArtifactAsync(
        string version,
        string kind,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<HypershellReleaseArtifactKind>(
                kind,
                ignoreCase: true,
                out var artifactKind))
        {
            return BadRequest();
        }

        StoredHypershellRelease? release;
        try
        {
            release = await _releases.GetAsync(version, cancellationToken);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        var artifact = release?.Artifacts.SingleOrDefault(
            candidate => candidate.Artifact.Kind == artifactKind);
        if (artifact is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new PhysicalFileResult(
            artifact.FullPath,
            "application/octet-stream")
        {
            FileDownloadName = artifact.Artifact.FileName,
            EnableRangeProcessing = true
        };
    }

    public string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 =>
            $"{bytes / 1024d / 1024d / 1024d:0.0} GiB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:0.0} MiB",
        >= 1024L => $"{bytes / 1024d:0.0} KiB",
        _ => $"{bytes} bytes"
    };
}
