using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace Hypershell.ProfileServer.Pages;

public sealed class BackupsModel : PageModel
{
    private readonly BackupExportStore _exports;

    public BackupsModel(BackupExportStore exports) => _exports = exports;

    public BackupExportInventory Inventory { get; private set; } =
        new([], 0, string.Empty);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Inventory = await _exports.InspectAsync(cancellationToken);

    public async Task<IActionResult> OnGetDownloadAsync(
        string id,
        CancellationToken cancellationToken)
    {
        VerifiedBackupExport? export;
        try
        {
            export = await _exports.OpenVerifiedAsync(id, cancellationToken);
        }
        catch (Exception exception) when (exception is
                                          IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or JsonException)
        {
            return NotFound();
        }

        if (export is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new FileStreamResult(
            export.Content,
            "application/pgp-encrypted")
        {
            FileDownloadName = export.Summary.FileName,
            EnableRangeProcessing = false
        };
    }

    public string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:0.0} GiB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:0.0} MiB",
        >= 1024L => $"{bytes / 1024d:0.0} KiB",
        _ => $"{bytes} bytes"
    };
}
