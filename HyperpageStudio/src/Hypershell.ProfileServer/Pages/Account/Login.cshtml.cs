using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly AccountSecurityStore _security;
    private readonly ProfileAccountStore _accounts;

    public LoginModel(
        AccountSecurityStore security,
        ProfileAccountStore accounts)
    {
        _security = security;
        _accounts = accounts;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    [StringLength(2048)]
    public string ReturnUrl { get; set; } = "/";

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var account = await _accounts.FindAsync(
            Input.AccountId,
            cancellationToken);
        var verification = await _security.VerifyAsync(
            Input.AccountId,
            Input.Password,
            Input.SecondFactor,
            cancellationToken);
        if (account is null || account.IsDisabled || !verification.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                verification.LockedUntilUtc is null
                    ? "The account credentials are invalid."
                    : "The account is temporarily locked after repeated failures.");
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.AccountId),
            new Claim(ClaimTypes.Name, account.DisplayName),
            new Claim("hypershell_account", account.AccountId)
        };
        await HttpContext.SignInAsync(
            "HypershellAdmin",
            new ClaimsPrincipal(
                new ClaimsIdentity(claims, "HypershellAdmin")),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow
            });
        return LocalRedirect(GetSafeReturnUrl());
    }

    private string GetSafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

    public sealed class LoginInput
    {
        [Required]
        [StringLength(64)]
        public string AccountId { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(64)]
        public string SecondFactor { get; set; } = string.Empty;
    }
}
