using Hypershell.ProfileServer;

if (args.Length > 0
    && string.Equals(args[0], "account", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode =
        await ProfileServerCommandLine.RunAccountCommandAsync(args);
    return;
}

if (args.Length > 0
    && string.Equals(args[0], "release", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode =
        await ProfileServerCommandLine.RunReleaseCommandAsync(args);
    return;
}

var explicitDataRoot = ProfileServerCommandLine.ReadOption(
    args,
    "--data-root");
var hostArguments = ProfileServerCommandLine.RemoveOption(
    args,
    "--data-root");
var options = ProfileServerOptions.FromEnvironment(explicitDataRoot);
var app = ProfileServerApplication.Build(options, hostArguments);
await app.RunAsync();
