using HyperLib.Core;

namespace Hypershell.Services;

public static class HyperlibExpandedCoreLibrary
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task EnsureAsync(string root, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await TemplateObjectLibrary.EnsureCoreLibraryAsync(root, ct);
            await HyperlibFoundationCatalog.EnsureAsync(root, ct);
            await HyperlibLegacyTemplateLibraryService.EnsureLegacyDesignGroupsAsync(root, ct);
            await HyperlibAtlasWebsiteCatalog.EnsureAsync(root, ct);
        }
        finally { Gate.Release(); }
    }
}
