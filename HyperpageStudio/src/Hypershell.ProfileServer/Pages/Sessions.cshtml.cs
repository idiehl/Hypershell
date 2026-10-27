using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages;

public sealed class SessionsModel : PageModel
{
    private readonly ProfileSessionTokenService _sessions;

    public SessionsModel(ProfileSessionTokenService sessions) =>
        _sessions = sessions;

    public IReadOnlyList<ProfileSessionSummary> Sessions { get; private set; } = [];

    [TempData]
    public string Notice { get; set; } = string.Empty;

    public void OnGet() => Sessions = _sessions.ListActive(AccountId());

    public IActionResult OnPostRevoke(string sessionId)
    {
        Notice = _sessions.RevokeById(AccountId(), sessionId)
            ? "Desktop API session revoked."
            : "The session was already expired, revoked, or missing.";
        return RedirectToPage();
    }

    private string AccountId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();
}
