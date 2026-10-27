using Microsoft.AspNetCore.DataProtection;

namespace Hypershell.ProfileServer;

public static class ProfileServerCommandLine
{
    public static async Task<int> RunReleaseCommandAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        if (arguments.Length < 2)
        {
            WriteReleaseUsage();
            return 2;
        }

        try
        {
            var options = ProfileServerOptions.FromEnvironment(
                ReadOption(arguments, "--data-root"));
            using var metadata = new CloudMetadataStore(options);
            await metadata.InitializeAsync(cancellationToken);
            var releases = new ReleaseStore(
                metadata,
                new StorageCapacityService(options),
                options);
            switch (arguments[1].ToLowerInvariant())
            {
                case "publish":
                {
                    var result = await releases.ImportBundleAsync(
                        Path.GetFullPath(
                            RequireOption(arguments, "--bundle")),
                        HasOption(arguments, "--pin"),
                        ReadOption(arguments, "--expected-git-commit"),
                        cancellationToken);
                    Console.WriteLine(
                        $"RELEASE_{result.Disposition.ToString().ToUpperInvariant()}|{result.Release.Version}|{result.Release.Channel}|{result.Release.GitCommit}");
                    return 0;
                }
                case "list":
                {
                    foreach (var release in await releases.ListAsync(
                                 ReadOption(arguments, "--channel"),
                                 cancellationToken))
                    {
                        Console.WriteLine(
                            $"RELEASE|{release.Version}|{release.Channel}|{release.GitCommit}|{release.PublishedAtUtc:O}|{(release.IsPinned ? "pinned" : "retained")}");
                    }

                    return 0;
                }
                default:
                    WriteReleaseUsage();
                    return 2;
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidDataException
                                          or InvalidOperationException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"RELEASE_COMMAND_FAILED|{exception.Message}");
            return 1;
        }
    }

    public static async Task<int> RunAccountCommandAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        if (arguments.Length < 2)
        {
            WriteAccountUsage();
            return 2;
        }

        try
        {
            var dataRoot = ReadOption(arguments, "--data-root");
            var options = ProfileServerOptions.FromEnvironment(dataRoot);
            var accounts = new ProfileAccountStore(options);
            var command = arguments[1].ToLowerInvariant();
            switch (command)
            {
                case "create":
                {
                    var id = RequireOption(arguments, "--id");
                    var displayName = ReadOption(arguments, "--display-name")
                                      ?? id;
                    var provisioned = await accounts.CreateAsync(
                        id,
                        displayName,
                        cancellationToken);
                    WriteProvisioned("ACCOUNT_CREATED", provisioned, options);
                    return 0;
                }
                case "rotate-key":
                {
                    var provisioned = await accounts.RotateAccessKeyAsync(
                        RequireOption(arguments, "--id"),
                        cancellationToken);
                    WriteProvisioned("ACCOUNT_KEY_ROTATED", provisioned, options);
                    return 0;
                }
                case "rename":
                {
                    using var metadata = new CloudMetadataStore(options);
                    var renames = new AccountIdentityRenameService(
                        metadata,
                        accounts);
                    var result = await renames.RenameBeforeUseAsync(
                        RequireOption(arguments, "--id"),
                        RequireOption(arguments, "--new-id"),
                        cancellationToken);
                    Console.WriteLine(
                        $"ACCOUNT_RENAMED|{result.PreviousAccountId}|{result.AccountId}|credential-version:{result.CredentialVersion}|security:{(result.SecurityEnrollmentMigrated ? "migrated" : "not-enrolled")}");
                    return 0;
                }
                case "disable":
                case "enable":
                {
                    var disabled = command == "disable";
                    var account = await accounts.SetDisabledAsync(
                        RequireOption(arguments, "--id"),
                        disabled,
                        cancellationToken);
                    Console.WriteLine(
                        $"ACCOUNT_{(disabled ? "DISABLED" : "ENABLED")}|{account.AccountId}|credential-version:{account.CredentialVersion}");
                    return 0;
                }
                case "list":
                {
                    foreach (var account in await accounts.ListAsync(
                                 cancellationToken))
                    {
                        Console.WriteLine(
                            $"ACCOUNT|{account.AccountId}|{account.DisplayName}|{(account.IsDisabled ? "disabled" : "enabled")}|credential-version:{account.CredentialVersion}");
                    }

                    return 0;
                }
                case "set-password":
                {
                    var accountId = RequireOption(arguments, "--id");
                    var passwordPath = Path.GetFullPath(
                        RequireOption(arguments, "--password-file"));
                    ValidateSecretFile(passwordPath);
                    var password = (await File.ReadAllTextAsync(
                            passwordPath,
                            cancellationToken))
                        .TrimEnd('\r', '\n');
                    var metadata = new CloudMetadataStore(options);
                    await metadata.InitializeAsync(cancellationToken);
                    var protectionPath = Path.Combine(
                        options.DataRoot,
                        "data-protection-keys");
                    Directory.CreateDirectory(protectionPath);
                    var protection = Microsoft.AspNetCore.DataProtection
                        .DataProtectionProvider.Create(
                            new DirectoryInfo(protectionPath),
                            builder =>
                            {
                                builder.SetApplicationName(
                                    "Hypershell.Cloud");
                                if (options.DataProtectionCertificatePath
                                    is not null)
                                {
                                    builder.ProtectKeysWithCertificate(
                                        ProfileServerApplication
                                            .LoadDataProtectionCertificate(
                                                options));
                                }
                            });
                    var security = new AccountSecurityStore(
                        metadata,
                        accounts,
                        protection,
                        TimeProvider.System);
                    var enrollment = await security.SetPasswordAsync(
                        accountId,
                        password,
                        cancellationToken);
                    Console.WriteLine(
                        $"ACCOUNT_PASSWORD_SET|{enrollment.AccountId}");
                    Console.WriteLine($"TOTP_SECRET|{enrollment.TotpSecret}");
                    Console.WriteLine($"TOTP_URI|{enrollment.ProvisioningUri}");
                    foreach (var recoveryCode in enrollment.RecoveryCodes)
                    {
                        Console.WriteLine($"RECOVERY_CODE|{recoveryCode}");
                    }

                    Console.WriteLine(
                        "The authenticator secret and recovery codes are shown once. Store them outside the server.");
                    metadata.Dispose();
                    return 0;
                }
                default:
                    WriteAccountUsage();
                    return 2;
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or KeyNotFoundException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"ACCOUNT_COMMAND_FAILED|{exception.Message}");
            return 1;
        }
    }

    public static string? ReadOption(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    public static string[] RemoveOption(string[] arguments, string name)
    {
        var remaining = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            remaining.Add(arguments[index]);
        }

        return remaining.ToArray();
    }

    private static bool HasOption(string[] arguments, string name) =>
        arguments.Any(argument => string.Equals(
            argument,
            name,
            StringComparison.OrdinalIgnoreCase));

    private static string RequireOption(string[] arguments, string name) =>
        ReadOption(arguments, name)
        ?? throw new ArgumentException($"Required option '{name}' is missing.");

    private static void WriteProvisioned(
        string eventName,
        ProvisionedProfileAccount account,
        ProfileServerOptions options)
    {
        Console.WriteLine(
            $"{eventName}|{account.AccountId}|credential-version:{account.CredentialVersion}");
        Console.WriteLine($"ACCESS_KEY|{account.AccessKey}");
        Console.WriteLine(
            "Store this access key securely. It is shown once and only its PBKDF2 hash is retained.");
        Console.WriteLine($"DATA_ROOT|{options.DataRoot}");
    }

    private static void ValidateSecretFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 4096)
        {
            throw new ArgumentException(
                "The password file is missing, empty, or too large.");
        }

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode unsafePermissions =
            UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute;
        if ((mode & unsafePermissions) != 0)
        {
            throw new ArgumentException(
                "The password file must not be accessible by group or other users.");
        }
    }

    private static void WriteAccountUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n"
            + "  Hypershell.ProfileServer account create --id <id> [--display-name <name>] [--data-root <path>]\n"
            + "  Hypershell.ProfileServer account rename --id <current-id> --new-id <new-id> [--data-root <path>]\n"
            + "  Hypershell.ProfileServer account rotate-key --id <id> [--data-root <path>]\n"
            + "  Hypershell.ProfileServer account disable|enable --id <id> [--data-root <path>]\n"
            + "  Hypershell.ProfileServer account set-password --id <id> --password-file <path> [--data-root <path>]\n"
            + "  Hypershell.ProfileServer account list [--data-root <path>]");
    }

    private static void WriteReleaseUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n"
            + "  Hypershell.ProfileServer release publish --bundle <directory> [--pin] [--expected-git-commit <sha>] [--data-root <path>]\n"
            + "  Hypershell.ProfileServer release list [--channel <channel>] [--data-root <path>]\n\n"
            + "Set HYPERSHELL_RELEASE_SIGNER_SHA256 to the trusted code-signing certificate SHA-256 fingerprint before publishing.");
    }
}
