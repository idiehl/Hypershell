using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages;

public sealed class CredentialReleaseModel : PageModel
{
    private readonly CredentialReleaseService _releases;
    private readonly AccountSecurityStore _security;

    public CredentialReleaseModel(
        CredentialReleaseService releases,
        AccountSecurityStore security)
    {
        _releases = releases;
        _security = security;
    }

    [BindProperty(SupportsGet = true)]
    [StringLength(16)]
    public string UserCode { get; set; } = string.Empty;

    [BindProperty]
    public ReauthenticationInput Input { get; set; } = new();

    public PendingCredentialRelease? Pending { get; private set; }

    public bool Approved { get; private set; }

    public bool Denied { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPendingAsync(cancellationToken);
    }

    public IActionResult OnPostLookup()
    {
        ModelState.Remove("Input.Password");
        ModelState.Remove("Input.SecondFactor");
        if (string.IsNullOrWhiteSpace(UserCode))
        {
            ModelState.AddModelError(
                nameof(UserCode),
                "Enter the code shown by Hypershell.");
            return Page();
        }

        return RedirectToPage(
            "/CredentialRelease",
            new { userCode = UserCode.Trim() });
    }

    public async Task<IActionResult> OnPostApproveAsync(
        CancellationToken cancellationToken)
    {
        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (accountId is null || string.IsNullOrWhiteSpace(UserCode))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await LoadPendingAsync(cancellationToken);
            return Page();
        }

        var verification = await _security.VerifyAsync(
            accountId,
            Input.Password,
            Input.SecondFactor,
            cancellationToken);
        if (!verification.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                verification.LockedUntilUtc is null
                    ? "The account credentials are invalid."
                    : "The account is temporarily locked after repeated failures.");
            await LoadPendingAsync(cancellationToken);
            return Page();
        }

        Approved = await _releases.ApproveAsync(
            accountId,
            UserCode,
            cancellationToken);
        if (!Approved)
        {
            ModelState.AddModelError(
                nameof(UserCode),
                "That release request is invalid, expired, changed, or already used.");
            await LoadPendingAsync(cancellationToken);
        }

        Input = new ReauthenticationInput();
        return Page();
    }

    public async Task<IActionResult> OnPostDenyAsync(
        CancellationToken cancellationToken)
    {
        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (accountId is null || string.IsNullOrWhiteSpace(UserCode))
        {
            return Forbid();
        }

        Denied = await _releases.DenyAsync(
            accountId,
            UserCode,
            cancellationToken);
        if (!Denied)
        {
            ModelState.AddModelError(
                nameof(UserCode),
                "That release request is invalid, expired, or already used.");
        }

        return Page();
    }

    private async Task LoadPendingAsync(CancellationToken cancellationToken)
    {
        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (accountId is null || string.IsNullOrWhiteSpace(UserCode))
        {
            return;
        }

        try
        {
            Pending = await _releases.FindPendingAsync(
                accountId,
                UserCode,
                cancellationToken);
            if (Pending is null)
            {
                ModelState.AddModelError(
                    nameof(UserCode),
                    "That code is invalid, expired, changed, or already used.");
            }
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(
                nameof(UserCode),
                "That credential-release code is malformed.");
        }
    }

    public sealed class ReauthenticationInput
    {
        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(64)]
        public string SecondFactor { get; set; } = string.Empty;
    }
}
