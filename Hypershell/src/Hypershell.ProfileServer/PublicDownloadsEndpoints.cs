using Hypershell.Profiles;

namespace Hypershell.ProfileServer;

public static class PublicDownloadsEndpoints
{
    public static void MapPublicDownloads(this WebApplication app)
    {
        app.MapGet("/downloads/releases", async (ReleaseStore releases, CancellationToken cancellationToken) =>
        {
            var published = await releases.ListPublicAsync(cancellationToken);
            return Results.Json(published.Select(release => new
            {
                release.Core.Summary.Version,
                release.Core.Summary.PublishedAtUtc,
                release.Core.Summary.ReleaseNotes,
                Core = release.Core.Summary.Artifacts,
                Complete = release.Complete.Summary.Artifacts
            }));
        }).AllowAnonymous().RequireRateLimiting("profile-api");

        app.MapGet("/downloads/{version}/{edition}/manifest", async (
            string version, string edition, ReleaseStore releases,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var release = await ResolveAsync(releases, version, edition, cancellationToken);
            if (release is null) return Results.NotFound();
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Json(release.SignedManifest);
        }).AllowAnonymous().RequireRateLimiting("profile-api");

        app.MapGet("/downloads/{version}/{edition}/{kind}", async (
            string version, string edition, string kind, ReleaseStore releases,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var artifactKind = kind.ToLowerInvariant() switch
            {
                "installer" => HypershellReleaseArtifactKind.Installer,
                "portable" => HypershellReleaseArtifactKind.Portable,
                _ => (HypershellReleaseArtifactKind?)null
            };
            if (artifactKind is null) return Results.NotFound();
            var release = await ResolveAsync(releases, version, edition, cancellationToken);
            var artifact = release?.Artifacts.SingleOrDefault(a => a.Artifact.Kind == artifactKind);
            if (artifact is null) return Results.NotFound();
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.File(artifact.FullPath, "application/octet-stream",
                artifact.Artifact.FileName, enableRangeProcessing: true);
        }).AllowAnonymous().RequireRateLimiting("profile-api");
    }

    private static async Task<StoredHypershellRelease?> ResolveAsync(
        ReleaseStore releases, string version, string edition, CancellationToken cancellationToken)
    {
        if (edition is not ("core" or "complete")) return null;
        PublicHypershellRelease? release;
        try { release = await releases.GetPublicAsync(version, cancellationToken); }
        catch (ArgumentException) { return null; }
        return edition == "core" ? release?.Core : release?.Complete;
    }
}
