namespace Hypershell.ProfileServer;

public sealed class ProfileStoreMigrationHostedService(
    FileProfileStore profiles,
    ILogger<ProfileStoreMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var migratedAccounts =
            await profiles.MigrateAllAccountsToCurrentStateAsync(
                cancellationToken);
        logger.LogInformation(
            "Current-state settings storage verified for {AccountCount} account(s).",
            migratedAccounts);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
