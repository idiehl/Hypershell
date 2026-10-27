using Hypershell.Profiles;
using Hypershell.Services;
using System.Text;

internal static class ProfileProductionSmokeVerifier
{
    public static async Task VerifyAsync(
        string verifierRoot,
        string endpoint,
        string accountId,
        string accessKey)
    {
        var root = Path.Combine(
            verifierRoot,
            "profile-production-smoke");
        Directory.CreateDirectory(root);
        var deviceState = new ProfileSyncDeviceStateService(
            Path.Combine(root, "profile-sync.device.json"),
            Path.Combine(root, "profile-sync-checkpoints.device.json"),
            Path.Combine(root, "conflicts"));
        var credentialPath = Path.Combine(
            root,
            "profile-sync-credential.device.bin");
        var credentialStore =
            new DpapiProfileSyncCredentialStore(credentialPath);
        using var session = new ProfileSyncSessionService(
            deviceState,
            credentialStore);
        var configuration = new ProfileSyncDeviceConfiguration
        {
            ServerEndpoint = endpoint,
            AccountId = accountId
        };

        await session.SignInAsync(
            configuration,
            accessKey,
            rememberAccessKey: true);
        var accessToken = await session.GetAccessTokenAsync();
        await using var transport = new HttpProfileSyncTransport(
            session.Configuration.ServerEndpoint,
            accessToken);
        var profiles = await transport.ListAsync();
        await session.SignOutAsync(forgetCredential: true);

        if (credentialStore.Exists
            || Directory.EnumerateFiles(
                    root,
                    "*",
                    SearchOption.AllDirectories)
                .Any(path => File.ReadAllBytes(path).AsSpan().IndexOf(
                    Encoding.UTF8.GetBytes(accessKey)) >= 0))
        {
            throw new InvalidOperationException(
                "The production smoke test retained plaintext credential material.");
        }

        Console.WriteLine(
            $"PROFILE_PRODUCTION_SMOKE_OK|{new Uri(endpoint).Host}|account:{accountId}|profiles:{profiles.Count}|signed-out|credential-forgotten");
    }
}
