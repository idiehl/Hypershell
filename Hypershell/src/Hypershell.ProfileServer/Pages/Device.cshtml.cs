using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages;

public sealed class DeviceModel : PageModel
{
    private readonly DeviceAuthorizationService _authorizations;

    public DeviceModel(DeviceAuthorizationService authorizations)
    {
        _authorizations = authorizations;
    }

    [BindProperty(SupportsGet = true)]
    [StringLength(16)]
    public string UserCode { get; set; } = string.Empty;

    public ClientDeviceSummary? Approved { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UserCode))
        {
            return;
        }

        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (accountId is null)
        {
            ModelState.AddModelError(
                nameof(UserCode),
                "Sign in before completing the Hypershell request.");
            return;
        }

        Approved = await _authorizations.ApproveAsync(
            accountId,
            UserCode,
            cancellationToken);
        if (Approved is null)
        {
            ModelState.AddModelError(
                nameof(UserCode),
                "That code is invalid, expired, or already used.");
        }
    }

    public IActionResult OnPostLookup()
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(UserCode))
        {
            return Page();
        }

        return RedirectToPage("/Device", new { userCode = UserCode.Trim() });
    }

}
