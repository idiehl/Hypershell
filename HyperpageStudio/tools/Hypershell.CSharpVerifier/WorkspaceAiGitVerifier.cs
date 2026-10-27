using Hypershell.Models;
using Hypershell.Services;
using System.Text;
using System.Text.Json;

internal static class WorkspaceAiGitVerifier
{
    private const string ConversationId = "ai-git-verifier-conversation";

    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "workspace-ai-git");
        var appData = Path.Combine(root, "app-data");
        var repositoryPath = Path.Combine(root, "repository");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(repositoryPath);

        var location = WorkspaceLocation.Local(repositoryPath);
        var runner = new LocalGitCommandRunner();
        await RunGitAsync(runner, location, ["init", "--initial-branch=main"]);
        await RunGitAsync(
            runner,
            location,
            ["config", "user.name", "Hypershell AI Git Verifier"]);
        await RunGitAsync(
            runner,
            location,
            ["config", "user.email", "ai-git-verifier@example.invalid"]);
        var sourcePath = Path.Combine(repositoryPath, "tracked.txt");
        await File.WriteAllTextAsync(
            sourcePath,
            "initial\n",
            new UTF8Encoding(false));
        await RunGitAsync(runner, location, ["add", "--", "tracked.txt"]);
        await RunGitAsync(runner, location, ["commit", "-m", "Initial"]);

        var contexts = new WorkspaceAiContextService(
            Path.Combine(appData, "contexts.json"));
        var context = contexts.SaveContext(contexts.CreateDraft() with
        {
            Name = "AI Git verifier",
            Read = Location(repositoryPath),
            Create = Location(repositoryPath),
            Edit = Location(repositoryPath),
            Delete = Location(repositoryPath),
            Terminal = Location(repositoryPath),
            DefaultSaveDirectory = repositoryPath,
            AskBeforeChanges = true,
            AllowInternet = false
        });
        var audit = new WorkspaceAiAuditService(
            Path.Combine(appData, "operations.audit.jsonl"));
        var broker = new WorkspaceAiPermissionBroker(
            Path.Combine(appData, "permissions.json"),
            audit,
            contexts);
        var operations = new WorkspaceAiOperationService(broker, audit);
        var repository = new GitRepositoryService(runner);
        var gitTools = new WorkspaceAiGitService(
            repository,
            broker,
            audit);
        var host = CreateToolHost(broker, operations, gitTools);
        Task<WorkspaceAiToolOutcome> ExecuteAsync(
            string contextId,
            WorkspaceAiFunctionCall call) => host.ExecuteAsync(
            contextId,
            call,
            ConversationId,
            repositoryPath);

        var status = await ExecuteAsync(
            context.Id,
            InspectCall("status"));
        using (var output = JsonDocument.Parse(status.FunctionOutput))
        {
            Require(
                output.RootElement.GetProperty("ok").GetBoolean()
                && output.RootElement.GetProperty("value")
                    .GetProperty("branch").GetString() == "main",
                "The AI Git status tool did not inspect the repository through the semantic adapter.");
        }

        await File.AppendAllTextAsync(
            sourcePath,
            "reviewed change\n",
            new UTF8Encoding(false));
        var stage = await ExecuteAsync(
            context.Id,
            MutationCall(
                "stage",
                paths: ["tracked.txt"]));
        Require(
            stage.PendingReview is
            {
                Kind: WorkspaceAiPendingActionKind.GitOperation
            }
            && !(await repository.GetStatusAsync(location)).Entries
                .Single(entry => entry.Path == "tracked.txt")
                .IsStaged,
            "A reviewed AI Git stage operation ran before approval.");
        var stageApproval = await host.ApproveAsync(
            stage.PendingReview!,
            ConversationId);
        Require(
            ParseOk(stageApproval.FunctionOutput)
            && (await repository.GetStatusAsync(location)).Entries
                .Single(entry => entry.Path == "tracked.txt")
                .IsStaged,
            "The approved AI Git stage operation was not applied.");

        const string commitMessage = "AI reviewed commit content";
        var commit = await ExecuteAsync(
            context.Id,
            MutationCall("commit", message: commitMessage));
        Require(
            commit.PendingReview is not null,
            "The reviewed AI Git commit did not stop for approval.");
        var commitApproval = await host.ApproveAsync(
            commit.PendingReview!,
            ConversationId);
        var history = await repository.GetHistoryAsync(location, 5);
        Require(
            ParseOk(commitApproval.FunctionOutput)
            && history[0].Subject == commitMessage,
            "The approved AI Git commit did not reach repository history.");

        var autonomous = contexts.SaveContext(context with
        {
            AskBeforeChanges = false,
            RiskWarningAcknowledged = true
        });
        await File.AppendAllTextAsync(
            sourcePath,
            "automatic change\n",
            new UTF8Encoding(false));
        var automaticStage = await ExecuteAsync(
            autonomous.Id,
            MutationCall("stage", paths: ["tracked.txt"]));
        Require(
            automaticStage.PendingReview is null
            && ParseOk(automaticStage.FunctionOutput)
            && (await repository.GetStatusAsync(location)).Entries
                .Single(entry => entry.Path == "tracked.txt")
                .IsStaged,
            "The autonomous Context did not apply an authorized semantic Git operation.");
        await repository.CommitAsync(location, "Automatic staging fixture");

        await repository.CreateBranchAsync(
            location,
            "delete/verifier",
            checkout: false);
        var deleteBranch = await ExecuteAsync(
            autonomous.Id,
            MutationCall(
                "delete_branch",
                name: "delete/verifier"));
        Require(
            deleteBranch.PendingReview is null
            && ParseOk(deleteBranch.FunctionOutput)
            && !(await repository.ListBranchesAsync(location)).Any(branch =>
                branch.Name == "delete/verifier"),
            "The autonomous Delete scope did not govern a destructive Git operation.");

        var deniedNetwork = await ExecuteAsync(
            autonomous.Id,
            MutationCall(
                "fetch",
                remote: "origin",
                flag: true));
        var embeddedCredential = await ExecuteAsync(
            autonomous.Id,
            MutationCall(
                "add_remote",
                name: "unsafe",
                url: "https://token@example.invalid/repository.git"));
        Require(
            !ParseOk(deniedNetwork.FunctionOutput)
            && !ParseOk(embeddedCredential.FunctionOutput)
            && embeddedCredential.FunctionOutput.Contains(
                "Git Credential Manager",
                StringComparison.Ordinal),
            "AI Git bypassed the Context network gate or accepted an embedded remote credential.");

        var remoteRoot = WorkspaceLocation.Ssh(
            "linked-verifier",
            "/srv/project").Identity;
        var remoteContext = contexts.SaveContext(autonomous with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Remote AI Git verifier",
            Read = WithRemote(autonomous.Read, remoteRoot),
            Create = WithRemote(autonomous.Create, remoteRoot),
            Edit = WithRemote(autonomous.Edit, remoteRoot),
            Delete = WithRemote(autonomous.Delete, remoteRoot),
            Terminal = WithRemote(autonomous.Terminal, remoteRoot),
            DefaultSaveWorkspaceUri = remoteRoot
        });
        var remotePlan = gitTools.PrepareMutation(
            remoteContext.Id,
            new WorkspaceAiGitMutationRequest
            {
                Kind = WorkspaceAiGitMutationKind.Stage,
                WorkspaceIdentity = remoteRoot,
                Paths = ["tracked.txt"]
            },
            ConversationId);
        Require(
            remotePlan.Succeeded
            && remotePlan.Value is { Location.IsSsh: true }
            && remotePlan.Value.Location.Authority == "linked-verifier",
            "The AI Git authorization layer lost the linked-host workspace identity.");
        gitTools.RecordPendingReview(remotePlan.Value!, ConversationId);

        var auditEntries = audit.ReadRecent(200);
        Require(
            auditEntries.Any(entry =>
                entry.Operation == WorkspaceAiOperationKind.InspectGit
                && entry.Outcome == WorkspaceAiAuditOutcome.Succeeded)
            && auditEntries.Any(entry =>
                entry.Operation == WorkspaceAiOperationKind.MutateGit
                && entry.Outcome == WorkspaceAiAuditOutcome.ConfirmationRequired)
            && auditEntries.Any(entry =>
                entry.Operation == WorkspaceAiOperationKind.DestructiveGit
                && entry.Outcome == WorkspaceAiAuditOutcome.Succeeded)
            && auditEntries.Any(entry =>
                entry.RemoteHostId == "linked-verifier"
                && entry.WorkspaceUri == remoteRoot)
            && auditEntries.All(entry =>
                entry.ConversationId == ConversationId
                && !entry.Detail.Contains(commitMessage, StringComparison.Ordinal)
                && !entry.Detail.Contains("token", StringComparison.OrdinalIgnoreCase)),
            "AI Git audit records lost operation/host identity or captured sensitive operation content.");

        Console.WriteLine(
            "WORKSPACE_AI_GIT_OK|semantic-inspection|reviewed-mutations|autonomous-mutations|delete-scope|network-gate|credential-redaction|linked-host-identity|content-free-audit");
    }

    private static WorkspaceAiToolHost CreateToolHost(
        WorkspaceAiPermissionBroker broker,
        WorkspaceAiOperationService operations,
        WorkspaceAiGitService gitTools) => new(
            broker,
            operations,
            (_, _) => null,
            (_, _, _) => null,
            (_, _) => null,
            (_, _, _, _, _, _, _) => Task.FromResult(
                new WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>
                {
                    Status = WorkspaceAiOperationStatus.Failed,
                    Message = "Terminal dispatch is outside this verifier."
                }),
            gitTools);

    private static WorkspaceAiFunctionCall InspectCall(string query) => new(
        "inspect-" + query,
        "inspect_git_repository",
        JsonSerializer.Serialize(new
        {
            workspace = ".",
            query,
            path = (string?)null,
            reference = (string?)null,
            diff_target = "working_tree",
            limit = 50
        }));

    private static WorkspaceAiFunctionCall MutationCall(
        string operation,
        IReadOnlyList<string>? paths = null,
        string? reference = null,
        string? name = null,
        string? newName = null,
        string? message = null,
        string? target = null,
        string? remote = null,
        string? branch = null,
        string? url = null,
        string? patch = null,
        bool flag = false,
        string resetMode = "mixed") => new(
            "mutation-" + operation,
            "propose_git_operation",
            JsonSerializer.Serialize(new
            {
                workspace = ".",
                operation,
                paths = paths ?? [],
                reference,
                name,
                new_name = newName,
                message,
                target,
                remote,
                branch,
                url,
                patch,
                flag,
                reset_mode = resetMode
            }));

    private static WorkspaceAiContextPathPermission Location(string path) =>
        new()
        {
            Mode = WorkspaceAiContextAccessMode.ChooseLocation,
            Location = path
        };

    private static WorkspaceAiContextPathPermission WithRemote(
        WorkspaceAiContextPathPermission permission,
        string workspaceUri) => permission with
    {
        WorkspaceUris = [workspaceUri]
    };

    private static bool ParseOk(string functionOutput)
    {
        using var document = JsonDocument.Parse(functionOutput);
        return document.RootElement.TryGetProperty("ok", out var ok)
               && ok.ValueKind == JsonValueKind.True;
    }

    private static async Task RunGitAsync(
        IGitCommandRunner runner,
        WorkspaceLocation location,
        IReadOnlyList<string> arguments)
    {
        var result = await runner.RunAsync(new GitCommandInvocation(
            location,
            arguments,
            IsReadOnly: false));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Synthetic Git setup failed: {result.StandardError}{result.FailureMessage}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
