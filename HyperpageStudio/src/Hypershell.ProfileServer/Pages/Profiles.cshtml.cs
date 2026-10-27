using Hypershell.Profiles;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Hypershell.ProfileServer.Pages;

public sealed class ProfilesModel : PageModel
{
    private readonly FileProfileStore _legacyProfiles;
    private readonly WorkspaceProfileRoamingStore _workspaceProfiles;
    private readonly ConversationStore _conversations;
    private readonly HyperlibVaultStore _packages;

    public ProfilesModel(
        FileProfileStore legacyProfiles,
        WorkspaceProfileRoamingStore workspaceProfiles,
        ConversationStore conversations,
        HyperlibVaultStore packages)
    {
        _legacyProfiles = legacyProfiles;
        _workspaceProfiles = workspaceProfiles;
        _conversations = conversations;
        _packages = packages;
    }

    public IReadOnlyList<RemoteProfileSummary> PortableProfiles { get; private set; } = [];
    public IReadOnlyList<WorkspaceProfileRoamingSummary> WorkspaceProfiles { get; private set; } = [];
    public IReadOnlyList<AiConversationSummary> Conversations { get; private set; } = [];
    public IReadOnlyList<HyperlibVaultPackageSummary> Packages { get; private set; } = [];
    public bool ConversationsTruncated { get; private set; }

    public string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? throw new UnauthorizedAccessException();
        var portable = _legacyProfiles.ListAsync(accountId, cancellationToken);
        var workspace = _workspaceProfiles.ListAsync(accountId, cancellationToken);
        var conversations = _conversations.ListAsync(
            accountId,
            profileId: null,
            cursor: null,
            pageSize: 200,
            cancellationToken);
        var packages = _packages.ListAsync(accountId, cancellationToken);
        await Task.WhenAll(portable, workspace, conversations, packages);
        PortableProfiles = await portable;
        WorkspaceProfiles = await workspace;
        var conversationPage = await conversations;
        Conversations = conversationPage.Conversations;
        ConversationsTruncated = !string.IsNullOrWhiteSpace(
            conversationPage.NextCursor);
        Packages = await packages;
    }
}
