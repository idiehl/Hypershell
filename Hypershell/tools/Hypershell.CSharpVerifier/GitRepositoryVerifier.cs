using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Text;

internal static class GitRepositoryVerifier
{
    public static void DeleteVerifierRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        foreach (var path in Directory.EnumerateDirectories(
                     root,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(path => path.Length))
        {
            File.SetAttributes(path, FileAttributes.Directory);
        }

        Directory.Delete(root, recursive: true);
    }

    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "git-repository");
        var repositoryPath = Path.Combine(root, "working");
        var barePath = Path.Combine(root, "origin.git");
        var clonePath = Path.Combine(root, "clone");
        Directory.CreateDirectory(root);

        var runner = new LocalGitCommandRunner();
        var service = new GitRepositoryService(runner);
        var availability = await service.GetAvailabilityAsync();
        Require(
            availability.IsAvailable
            && Path.IsPathFullyQualified(availability.ExecutablePath)
            && availability.Version.Length > 0
            && availability.CredentialMessage.Length > 0
            && (!availability.CredentialManagerAvailable
                || availability.CredentialManagerVersion.Length > 0),
            "The installed Git CLI or credential-manager diagnostics were incomplete.");

        var repository = WorkspaceLocation.Local(repositoryPath);
        await service.InitializeAsync(repository);
        await ConfigureIdentityAsync(runner, repository);
        var discoveredFromChild = Path.Combine(repositoryPath, "nested");
        Directory.CreateDirectory(discoveredFromChild);
        var identity = await service.DiscoverAsync(
            WorkspaceLocation.Local(discoveredFromChild));
        Require(
            identity?.Root.Path == repository.Path
            && !identity.IsBare,
            "Repository discovery did not resolve the stable working-tree root.");

        var firstPath = Path.Combine(repositoryPath, "alpha.txt");
        await File.WriteAllTextAsync(
            firstPath,
            "alpha one\nalpha two\n",
            new UTF8Encoding(false));
        var untracked = await service.GetStatusAsync(repository);
        Require(
            untracked.Entries.Single() is
            {
                Path: "alpha.txt",
                IsUntracked: true
            },
            "Porcelain-v2 status did not identify an untracked file.");
        await service.StageAsync(repository, ["alpha.txt"]);
        await service.CommitAsync(repository, "Initial commit");
        var clean = await service.GetStatusAsync(repository);
        var primaryBranch = clean.BranchName;
        Require(
            !clean.HasChanges
            && clean.HeadCommitId.Length == 40
            && !string.IsNullOrWhiteSpace(primaryBranch),
            "The initial commit did not produce a clean named branch.");

        await File.WriteAllTextAsync(
            firstPath,
            "alpha changed\nalpha two\n",
            new UTF8Encoding(false));
        var workingDiff = await service.GetDiffAsync(
            repository,
            "alpha.txt",
            GitDiffTarget.WorkingTree);
        Require(
            workingDiff.Patch.Contains("alpha changed", StringComparison.Ordinal)
            && !workingDiff.IsBinary,
            "The working-tree diff was not returned as a bounded patch.");
        await service.ApplyPatchToIndexAsync(repository, workingDiff.Patch);
        var staged = await service.GetStatusAsync(repository);
        Require(
            staged.Entries.Single() is
            {
                IsStaged: true,
                HasWorkingTreeChange: false
            },
            "Applying a selected patch did not stage the index independently.");
        var indexDiff = await service.GetDiffAsync(
            repository,
            "alpha.txt",
            GitDiffTarget.Index);
        await service.ApplyPatchToIndexAsync(
            repository,
            indexDiff.Patch,
            reverse: true);
        var unstaged = await service.GetStatusAsync(repository);
        Require(
            unstaged.Entries.Single() is
            {
                IsStaged: false,
                HasWorkingTreeChange: true
            },
            "Reversing a selected index patch did not unstage the hunk.");
        await service.StageAsync(repository, ["alpha.txt"]);
        await service.CommitAsync(repository, "Update alpha");

        await service.CreateBranchAsync(
            repository,
            "feature/verifier",
            checkout: true);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "feature.txt"),
            "feature\n",
            new UTF8Encoding(false));
        await service.StageAsync(repository, ["feature.txt"]);
        await service.CommitAsync(repository, "Feature commit");
        var featureCommit = (await service.GetStatusAsync(repository)).HeadCommitId;
        await service.CheckoutAsync(repository, primaryBranch);
        await service.MergeAsync(repository, "feature/verifier");
        Require(
            File.Exists(Path.Combine(repositoryPath, "feature.txt")),
            "The branch merge did not update the working tree.");

        var branches = await service.ListBranchesAsync(repository);
        Require(
            branches.Any(branch => branch.Name == primaryBranch && branch.IsCurrent)
            && branches.Any(branch => branch.Name == "feature/verifier"),
            "Branch enumeration lost current or secondary branch metadata.");
        await service.RenameBranchAsync(
            repository,
            "feature/verifier",
            "feature/renamed");
        ExpectConfirmation(() => service.DeleteBranchAsync(
            repository,
            "feature/renamed",
            force: true,
            destructiveConfirmed: false));
        await service.DeleteBranchAsync(
            repository,
            "feature/renamed",
            force: false,
            destructiveConfirmed: false);

        await service.CreateTagAsync(
            repository,
            "v0.1.0",
            featureCommit,
            "Verifier release");
        Require(
            (await service.ListTagsAsync(repository)).Single() is
            {
                Name: "v0.1.0",
                Subject: "Verifier release"
            },
            "Annotated tag metadata was not parsed.");
        ExpectConfirmation(() => service.DeleteTagAsync(
            repository,
            "v0.1.0",
            destructiveConfirmed: false));

        var history = await service.GetHistoryAsync(repository);
        var inspected = await service.GetCommitAsync(
            repository,
            history[0].CommitId);
        var commitPatch = await service.GetCommitPatchAsync(
            repository,
            history[0].CommitId);
        var fileHistory = await service.GetHistoryAsync(
            repository,
            path: "alpha.txt");
        Require(
            history.Count >= 3
            && inspected.Subject == history[0].Subject
            && commitPatch.Patch.Contains(
                inspected.Subject,
                StringComparison.Ordinal)
            && fileHistory.Count == 2,
            "Repository, commit patch, or file history did not round-trip through stable formats.");

        await File.AppendAllTextAsync(firstPath, "stash me\n");
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "untracked.txt"),
            "untracked stash\n",
            new UTF8Encoding(false));
        await service.CreateStashAsync(repository, "Verifier stash");
        var stashes = await service.ListStashesAsync(repository);
        Require(
            stashes.Count == 1
            && stashes[0].Message.Contains("Verifier stash", StringComparison.Ordinal),
            "Stash creation or stable stash parsing failed.");
        await service.ApplyStashAsync(repository, stashes[0].Reference, pop: true);
        Require(
            (await service.ListStashesAsync(repository)).Count == 0
            && File.Exists(Path.Combine(repositoryPath, "untracked.txt")),
            "Popping a stash did not restore tracked and untracked changes.");
        File.Delete(Path.Combine(repositoryPath, "untracked.txt"));
        await RunRequiredAsync(runner, repository, ["restore", "--", "alpha.txt"]);

        await service.CreateBranchAsync(
            repository,
            "conflict/verifier",
            checkout: true);
        await File.WriteAllTextAsync(
            firstPath,
            "feature conflict\nalpha two\n",
            new UTF8Encoding(false));
        await service.StageAsync(repository, ["alpha.txt"]);
        await service.CommitAsync(repository, "Feature conflict side");
        await service.CheckoutAsync(repository, primaryBranch);
        await File.WriteAllTextAsync(
            firstPath,
            "primary conflict\nalpha two\n",
            new UTF8Encoding(false));
        await service.StageAsync(repository, ["alpha.txt"]);
        await service.CommitAsync(repository, "Primary conflict side");
        await ExpectGitFailureAsync(() => service.MergeAsync(
            repository,
            "conflict/verifier"));
        var conflictStatus = await service.GetStatusAsync(repository);
        var conflict = await service.GetConflictAsync(
            repository,
            "alpha.txt");
        Require(
            conflictStatus.HasConflicts
            && conflict.BaseContent.Contains("alpha changed", StringComparison.Ordinal)
            && conflict.OursContent.Contains("primary conflict", StringComparison.Ordinal)
            && conflict.TheirsContent.Contains("feature conflict", StringComparison.Ordinal),
            "Three-way conflict data did not preserve base, ours, and theirs stages.");
        await File.WriteAllTextAsync(
            firstPath,
            "resolved conflict\nalpha two\n",
            new UTF8Encoding(false));
        await service.StageAsync(repository, ["alpha.txt"]);
        await service.ContinueMergeAsync(repository);
        Require(
            !(await service.GetStatusAsync(repository)).HasConflicts,
            "Continuing a resolved merge did not leave a clean conflict state.");
        await service.DeleteBranchAsync(
            repository,
            "conflict/verifier",
            force: false,
            destructiveConfirmed: false);

        var bare = WorkspaceLocation.Local(barePath);
        await service.InitializeAsync(bare, bare: true);
        await service.AddRemoteAsync(repository, "origin", barePath);
        var remotes = await service.ListRemotesAsync(repository);
        Require(
            remotes.Single() is { Name: "origin" } origin
            && Path.GetFullPath(origin.FetchUrl) == Path.GetFullPath(barePath),
            "Remote creation or URL inspection failed.");
        await service.PushAsync(
            repository,
            "origin",
            primaryBranch,
            setUpstream: true);
        await service.FetchAsync(repository);
        var tracked = await service.GetStatusAsync(repository);
        Require(
            tracked.Upstream == $"origin/{primaryBranch}"
            && tracked.Ahead == 0
            && tracked.Behind == 0,
            "Push/upstream selection or ahead-behind status parsing failed.");

        await ExpectCredentialRejectionAsync(() => service.AddRemoteAsync(
            repository,
            "unsafe",
            "https://embedded-token@example.invalid/repository.git"));
        await ExpectCredentialRejectionAsync(() => service.SetRemoteUrlAsync(
            repository,
            "origin",
            "https://example.invalid/repository.git?access_token=embedded"));
        await service.AddRemoteAsync(
            repository,
            "ssh-safe",
            "git@example.invalid:owner/repository.git");
        Require(
            (await service.ListRemotesAsync(repository)).Any(remote =>
                remote.Name == "ssh-safe"),
            "A normal username-only SCP-style SSH remote was rejected.");
        await service.RemoveRemoteAsync(
            repository,
            "ssh-safe",
            destructiveConfirmed: true);

        await service.CloneAsync(barePath, WorkspaceLocation.Local(clonePath), primaryBranch);
        var clone = WorkspaceLocation.Local(clonePath);
        await ConfigureIdentityAsync(runner, clone);
        await File.WriteAllTextAsync(
            Path.Combine(clonePath, "from-clone.txt"),
            "clone change\n",
            new UTF8Encoding(false));
        await service.StageAsync(clone, ["from-clone.txt"]);
        await service.CommitAsync(clone, "Commit from clone");
        await service.PushAsync(clone, "origin", primaryBranch);
        await service.PullAsync(repository, "origin", primaryBranch);
        Require(
            File.Exists(Path.Combine(repositoryPath, "from-clone.txt")),
            "Clone, push, and pull did not exchange commits through the bare origin.");

        ExpectConfirmation(() => service.PushAsync(
            repository,
            "origin",
            primaryBranch,
            forceWithLease: true,
            destructiveConfirmed: false));
        ExpectConfirmation(() => service.ResetAsync(
            repository,
            "HEAD~1",
            GitResetMode.Hard,
            destructiveConfirmed: false));
        ExpectConfirmation(() => service.RemoveRemoteAsync(
            repository,
            "origin",
            destructiveConfirmed: false));

        await VerifyRemoteRoutingAsync();

        Console.WriteLine(
            "GIT_REPOSITORY_OK|discovery|porcelain-v2|stage-unstage-hunks|diff|commit|commit-patch|branches|tags|history|file-history|stash|three-way-conflict|merge-continue|bare-origin|clone|fetch-pull-push|upstream|credential-manager-diagnostics|credential-free-remotes|destructive-confirmation|bounded-cli|ssh-routing|posix-quoting|windows-encoded-command");
    }

    private static async Task VerifyRemoteRoutingAsync()
    {
        var host = new RemoteHostSummary
        {
            Id = "remote-one",
            DisplayName = "Remote One",
            HostName = "remote.invalid",
            UserName = "tester",
            OperatingSystem = RemoteHostOperatingSystem.Linux,
            CredentialId = "credential-one",
            HasCredential = true,
            HostKeyAlgorithm = "ssh-ed25519",
            HostKeyFingerprint = "SHA256:synthetic"
        };
        var credential = new SshCredentialSummary
        {
            Id = "credential-one",
            DisplayName = "Synthetic",
            KeyVersion = 1,
            PublicKeyFingerprint = "SHA256:credential"
        };
        var transport = new FakeSshGitTransport();
        var sshRunner = new SshGitCommandRunner(
            host,
            credential,
            transport);
        var availability = await sshRunner.GetAvailabilityAsync();
        Require(
            availability.IsAvailable && availability.Version == "2.51.0",
            "The SSH Git runner did not probe the remote Git version.");

        var remote = WorkspaceLocation.Ssh(
            host.Id,
            "/srv/project with spaces/it's-safe");
        var result = await sshRunner.RunAsync(new GitCommandInvocation(
            remote,
            ["status", "--porcelain=v2", "$(not-a-command)"],
            "synthetic patch",
            IsReadOnly: true));
        Require(
            result.Succeeded
            && transport.LastRequest is { } posix
            && posix.StandardInput == "synthetic patch"
            && posix.CommandText.Contains(
                "'/srv/project with spaces/it'\"'\"'s-safe'",
                StringComparison.Ordinal)
            && posix.CommandText.Contains(
                "'$(not-a-command)'",
                StringComparison.Ordinal)
            && posix.CommandText.Contains(
                "'GIT_OPTIONAL_LOCKS=0'",
                StringComparison.Ordinal),
            "POSIX remote Git arguments were not shell-quoted or bounded correctly.");

        var remoteService = new GitRepositoryService(sshRunner);
        await remoteService.CloneAsync(
            "ssh://git.example/repository.git",
            WorkspaceLocation.Ssh(host.Id, "/srv/work/New Repository"));
        Require(
            transport.LastRequest is { } clone
            && clone.CommandText.Contains("'-C' '/srv/work'", StringComparison.Ordinal)
            && clone.CommandText.Contains("'New Repository'", StringComparison.Ordinal),
            "Remote clone did not execute from the provider-neutral parent workspace.");

        var windowsHost = host with
        {
            Id = "remote-windows",
            OperatingSystem = RemoteHostOperatingSystem.Windows
        };
        var windowsTransport = new FakeSshGitTransport();
        var windowsRunner = new SshGitCommandRunner(
            windowsHost,
            credential,
            windowsTransport);
        _ = await windowsRunner.GetAvailabilityAsync();
        _ = await windowsRunner.RunAsync(new GitCommandInvocation(
            WorkspaceLocation.Ssh(
                windowsHost.Id,
                "C:/Work/O'Brien"),
            ["status", "--porcelain=v2"],
            IsReadOnly: true));
        var encodedCommand = windowsTransport.LastRequest?.CommandText
                             ?? string.Empty;
        var encodedPayload = encodedCommand
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;
        string decoded;
        try
        {
            decoded = Encoding.Unicode.GetString(
                Convert.FromBase64String(encodedPayload));
        }
        catch (FormatException)
        {
            decoded = string.Empty;
        }

        Require(
            encodedCommand.StartsWith(
                "powershell -NoLogo -NoProfile -NonInteractive -EncodedCommand ",
                StringComparison.Ordinal)
            && decoded.Contains("'C:/Work/O''Brien'", StringComparison.Ordinal)
            && decoded.Contains("$env:GIT_OPTIONAL_LOCKS='0'", StringComparison.Ordinal)
            && decoded.EndsWith("exit $LASTEXITCODE }", StringComparison.Ordinal),
            "Windows remote Git did not use an encoded, literal-safe PowerShell command.");

        var registry = new GitCommandRunnerRegistry(
            new FakeGitRunner("local", location => location.IsLocal));
        registry.RegisterSsh(host.Id, sshRunner);
        var routed = await registry.RunAsync(new GitCommandInvocation(
            remote,
            ["status"],
            IsReadOnly: true));
        Require(
            routed.Succeeded,
            "The provider-neutral Git runner did not route the SSH workspace.");
        registry.RemoveSsh(host.Id);
        var missing = await registry.RunAsync(new GitCommandInvocation(
            remote,
            ["status"],
            IsReadOnly: true));
        Require(
            !missing.Succeeded
            && missing.FailureMessage.Contains(
                "unlock",
                StringComparison.OrdinalIgnoreCase),
            "Removed SSH Git runners did not fail with an actionable diagnostic.");
    }

    private static async Task ConfigureIdentityAsync(
        IGitCommandRunner runner,
        WorkspaceLocation repository)
    {
        await RunRequiredAsync(
            runner,
            repository,
            ["config", "user.name", "Hypershell Verifier"]);
        await RunRequiredAsync(
            runner,
            repository,
            ["config", "user.email", "verifier@hypershell.invalid"]);
    }

    private static async Task RunRequiredAsync(
        IGitCommandRunner runner,
        WorkspaceLocation repository,
        IReadOnlyList<string> arguments)
    {
        var result = await runner.RunAsync(new GitCommandInvocation(
            repository,
            arguments));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Synthetic Git setup failed: {result.StandardError}{result.FailureMessage}");
        }
    }

    private static void ExpectConfirmation(Func<Task> action)
    {
        try
        {
            _ = action();
            throw new InvalidOperationException(
                "The destructive Git operation did not require confirmation.");
        }
        catch (GitRepositoryException exception) when (
            exception.Message.Contains(
                "confirmation",
                StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static async Task ExpectGitFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
            throw new InvalidOperationException(
                "The synthetic conflicting Git operation unexpectedly succeeded.");
        }
        catch (GitRepositoryException)
        {
        }
    }

    private static async Task ExpectCredentialRejectionAsync(Func<Task> action)
    {
        try
        {
            await action();
            throw new InvalidOperationException(
                "A remote URL containing embedded credentials was accepted.");
        }
        catch (ArgumentException exception) when (
            exception.Message.Contains(
                "Git Credential Manager",
                StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeSshGitTransport : ISshGitCommandTransport
    {
        private int _calls;

        public SshGitTransportRequest? LastRequest { get; private set; }

        public Task<GitCommandResult> ExecuteAsync(
            RemoteHostSummary host,
            SshCredentialSummary credential,
            SshGitTransportRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var output = _calls++ == 0
                ? "git version 2.51.0\n"
                : "synthetic remote result\n";
            return Task.FromResult(new GitCommandResult(
                true,
                0,
                output,
                string.Empty,
                string.Empty,
                false,
                false));
        }
    }

    private sealed class FakeGitRunner : IGitCommandRunner
    {
        private readonly Func<WorkspaceLocation, bool> _canHandle;

        public FakeGitRunner(
            string id,
            Func<WorkspaceLocation, bool> canHandle)
        {
            RunnerId = id;
            _canHandle = canHandle;
        }

        public string RunnerId { get; }

        public bool CanHandle(WorkspaceLocation location) =>
            _canHandle(location);

        public Task<GitToolAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitToolAvailability(
                true,
                RunnerId,
                "synthetic",
                "Available"));

        public Task<GitCommandResult> RunAsync(
            GitCommandInvocation invocation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCommandResult(
                true,
                0,
                "synthetic",
                string.Empty,
                string.Empty,
                false,
                false));
    }
}
