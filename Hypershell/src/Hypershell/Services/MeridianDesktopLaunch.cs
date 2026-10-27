using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>Open Meridian independently of a running desktop-launcher process.</summary>
internal static class MeridianDesktopLaunch
{
    internal static async Task<AtlasMyNetOpenAppRequest> ResolveAsync(SettingsService settings)
    {
        var store = new AtlasMyNetAppStore(settings.AtlasMyNetAppsPath, settings.AtlasMyNetTrustPath);
        var definition = store.Load().SingleOrDefault(app =>
            string.Equals(app.Id, "atlas.meridian", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Meridian is not registered in this MyNet profile.");
        if (definition.IsNative)
            throw new InvalidOperationException("Meridian must be registered as a local web application.");
        using var web = new AtlasMyNetWebService();
        using var processes = new AtlasMyNetProcessService(store, new WindowsSocketInventoryService(), web);
        var runtime = await processes.StartAsync(definition);
        return new AtlasMyNetOpenAppRequest(definition, runtime.Url);
    }
}
