using Hypershell.Profiles;
using Hypershell.Services;

internal static class SuiteUpdateGuardVerifier
{
    internal static async Task VerifyAsync(string root)
    {
        var installation = Path.Combine(root, "installed-layout");
        var studio = Path.Combine(installation, "Components", "Authoring", "studio");
        Directory.CreateDirectory(studio);
        File.WriteAllText(Path.Combine(studio, "Hypershell.exe"), "layout fixture only");
        Require(ApplicationUpdateService.DetectSuiteComponents(installation), "Core host recognizes authoring component");
        Require(ApplicationUpdateService.DetectSuiteComponents(studio), "Nested Studio recognizes suite root");
        var isolated = Path.Combine(root, "core-only-layout");
        Directory.CreateDirectory(isolated);
        Require(!ApplicationUpdateService.DetectSuiteComponents(isolated), "Core-only layout stays eligible for Core updater");

        var updateDirectory = Path.Combine(root, "guarded-updates");
        var trustPath = Path.Combine(root, "guard-trust.json");
        File.WriteAllText(trustPath, "{\"schemaVersion\":1,\"signerCertificateSha256\":[]}");
        var suiteInstalled = true;
        var networkCalls = 0;
        var service = new ApplicationUpdateService(updateDirectory, new HypershellReleaseTrustStore(trustPath),
            clientFactory: _ => { networkCalls++; throw new Exception("Unexpected network call"); },
            requiresSuiteInstaller: () => suiteInstalled);
        var outcome = await service.CheckAsync("https://example.invalid", "unused");
        Require(outcome.Kind == ApplicationUpdateCheckKind.ManualSuiteUpdate && outcome.Candidate is null
            && outcome.Message.Contains("Complete installer") && networkCalls == 0, "Suite check gives manual action without network or candidate");
        var candidate = new ApplicationUpdateCandidate(new SignedHypershellReleaseManifest(),
            new HypershellReleaseManifest { Version = "2.0.0" },
            new HypershellReleaseArtifact { FileName = "old-core-candidate.msi" }, new string('a', 64));
        await Refused(async () => { await service.DownloadAsync(candidate, "https://example.invalid", "unused"); });
        await Refused(() =>
        {
            service.StartInstallAndRestart(new ApplicationUpdateDownloadOutcome(candidate, "must-not-launch.msi", false), Environment.ProcessId);
            return Task.CompletedTask;
        });
        Require(!Directory.Exists(updateDirectory) && networkCalls == 0, "Guards run before staging, downloads, verification or launch");
        suiteInstalled = false;
        Require((await service.CheckAsync("https://example.invalid", "unused")).Kind == ApplicationUpdateCheckKind.TrustNotConfigured,
            "Core-only behavior unchanged");
        Console.WriteLine("SUITE_UPDATE_GUARD_OK|root-and-nested-detection|manual-action|no-network|no-staging|no-core-install-handoff");
    }

    private static async Task Refused(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException error) when (error.Message.Contains("Complete installer")) { return; }
        throw new InvalidOperationException("Suite accepted a Core updater action.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
