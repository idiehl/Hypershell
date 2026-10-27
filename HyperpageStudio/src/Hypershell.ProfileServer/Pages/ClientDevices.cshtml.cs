using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages;

public sealed class ClientDevicesModel : PageModel
{
    private readonly DeviceAuthorizationService _devices;

    public ClientDevicesModel(DeviceAuthorizationService devices) =>
        _devices = devices;

    public IReadOnlyList<ClientDeviceSummary> Devices { get; private set; } = [];

    [TempData]
    public string Notice { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Devices = await _devices.ListDevicesAsync(
            AccountId(),
            cancellationToken);

    public async Task<IActionResult> OnPostRevokeAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            Notice = await _devices.RevokeDeviceAsync(
                AccountId(),
                deviceId,
                cancellationToken)
                ? "Client PC revoked. Its refresh credentials and active API sessions are invalid."
                : "The client PC was already revoked or missing.";
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        return RedirectToPage();
    }

    private string AccountId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();
}
