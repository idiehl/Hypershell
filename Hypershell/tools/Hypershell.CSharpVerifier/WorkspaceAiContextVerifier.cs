using Hypershell.Profiles;
using Hypershell.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class WorkspaceAiContextVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "workspace-ai-contexts");
        var appData = Path.Combine(root, "app-data");
        var readRoot = Path.Combine(root, "read");
        var createRoot = Path.Combine(root, "create");
        var editRoot = Path.Combine(root, "edit");
        var deleteRoot = Path.Combine(root, "delete");
        var terminalRoot = Path.Combine(root, "terminal");
        var saveRoot = Path.Combine(root, "save");
        foreach (var path in new[]
                 {
                     appData, readRoot, createRoot, editRoot,
                     deleteRoot, terminalRoot, saveRoot
                 })
        {
            Directory.CreateDirectory(path);
        }

        var readPath = Path.Combine(readRoot, "read.txt");
        var createExistingPath = Path.Combine(createRoot, "existing.txt");
        var editPath = Path.Combine(editRoot, "edit.txt");
        var deletePath = Path.Combine(deleteRoot, "delete.txt");
        await File.WriteAllTextAsync(readPath, "read allowed", Encoding.UTF8);
        await File.WriteAllTextAsync(createExistingPath, "create only", Encoding.UTF8);
        await File.WriteAllTextAsync(editPath, "edit original", Encoding.UTF8);
        await File.WriteAllTextAsync(deletePath, "delete original", Encoding.UTF8);

        var contextPath = Path.Combine(appData, "ai-contexts.device.json");
        var service = new WorkspaceAiContextService(contextPath);
        Require(
            service.GetContexts().Count >= 2
            && service.GetContext(WorkspaceAiContextDefinition.DefaultId)
                is { AskBeforeChanges: true, AllowInternet: false }
            && service.GetContext(WorkspaceAiContextDefinition.CustomId)
                is not null,
            "Default and Custom contexts were not created with limited defaults.");

        var draft = service.CreateDraft() with
        {
            Name = "Verifier Context",
            Read = Location(readRoot),
            Create = Location(createRoot),
            Edit = Location(editRoot),
            Delete = Location(deleteRoot),
            Terminal = Location(terminalRoot),
            Model = WorkspaceAiProviderConfiguration.DefaultModel,
            ReasoningEffort = "high",
            TextVerbosity = "low",
            DefaultSaveDirectory = saveRoot,
            AskBeforeChanges = true,
            AllowInternet = false
        };
        var saved = service.SaveContext(draft);
        var reloadedService = new WorkspaceAiContextService(contextPath);
        Require(
            reloadedService.SelectedContextId == saved.Id
            && reloadedService.GetContext(saved.Id) is
            {
                Name: "Verifier Context",
                ReasoningEffort: "high",
                TextVerbosity: "low",
                DefaultSaveDirectory: "",
                DefaultSaveWorkspaceUri: ""
            },
            "A named AI Context did not persist or retained the retired Context-level default project root.");

        var audit = new WorkspaceAiAuditService(
            Path.Combine(appData, "ai-context.audit.jsonl"));
        var broker = new WorkspaceAiPermissionBroker(
            Path.Combine(appData, "legacy-permissions.device.json"),
            audit,
            reloadedService);
        var operations = new WorkspaceAiOperationService(broker, audit);

        var read = await operations.ReadTextAsync(saved.Id, readPath);
        var deniedCrossScopeRead = await operations.ReadTextAsync(
            saved.Id,
            editPath);
        Require(
            read.Succeeded
            && deniedCrossScopeRead.Status == WorkspaceAiOperationStatus.Denied,
            "The Context Read location was not enforced independently.");

        var created = await operations.WriteTextAsync(
            saved.Id,
            Path.Combine(createRoot, "new.txt"),
            "created",
            expectedSha256: null,
            allowCreate: true);
        var deniedCreateOverwrite = await operations.WriteTextAsync(
            saved.Id,
            createExistingPath,
            "must not replace",
            Hash(await File.ReadAllBytesAsync(createExistingPath)),
            allowCreate: false);
        Require(
            created.Succeeded
            && deniedCreateOverwrite.Status == WorkspaceAiOperationStatus.Denied
            && await File.ReadAllTextAsync(createExistingPath) == "create only",
            "Create scope overwrote an existing file or rejected a new file.");

        var edited = await operations.WriteTextAsync(
            saved.Id,
            editPath,
            "edited",
            Hash(await File.ReadAllBytesAsync(editPath)),
            allowCreate: false);
        Require(
            edited.Succeeded && await File.ReadAllTextAsync(editPath) == "edited",
            "The independent Edit location did not authorize a SHA-guarded replacement.");

        var deleted = await operations.DeleteTextAsync(
            saved.Id,
            deletePath,
            Hash(await File.ReadAllBytesAsync(deletePath)));
        Require(
            deleted.Succeeded && !File.Exists(deletePath),
            "The independent Delete location did not authorize a SHA-guarded deletion.");

        var terminal = broker.AuthorizeTerminal(
            saved.Id,
            execute: true,
            userConfirmed: false);
        Require(
            terminal.Outcome
                == WorkspaceAiAuthorizationOutcome.ConfirmationRequired
            && terminal.RootPath == terminalRoot
            && broker.GetTerminalExecutionPolicy(saved.Id)
                == Hypershell.Profiles.WorkspaceAiTerminalExecutionPolicy.ReviewOnly
            && broker.AuthorizeNetwork(saved.Id).Outcome
                == WorkspaceAiAuthorizationOutcome.Denied,
            "The Context terminal, review, or internet policy was not enforced.");

        var autonomous = reloadedService.SaveContext(saved with
        {
            AskBeforeChanges = false,
            AllowInternet = true,
            RiskWarningAcknowledged = true
        });
        Require(
            broker.AllowsAutomaticFileAndEditorChanges(autonomous.Id)
            && broker.GetTerminalExecutionPolicy(autonomous.Id)
                == Hypershell.Profiles.WorkspaceAiTerminalExecutionPolicy.AutoExecute
            && broker.AuthorizeNetwork(autonomous.Id).IsAllowed,
            "The Context autonomy or internet setting did not take effect immediately.");

        var roamingSource = reloadedService.SaveContext(autonomous with
        {
            Read = autonomous.Read with
            {
                WorkspaceUris =
                [
                    "ssh://host-verifier/srv/project"
                ]
            },
            DefaultSaveWorkspaceUri =
                "ssh://host-verifier/srv/project"
        });
        var roaming = reloadedService.ExportRoamingDefinitions();
        var roamingJson = JsonSerializer.Serialize(roaming);
        Require(
            !roamingJson.Contains(readRoot, StringComparison.OrdinalIgnoreCase)
            && roaming.Single(context => context.Id == roamingSource.Id)
                .Read.WorkspaceUris.Single()
               == "ssh://host-verifier/srv/project",
            "Device-local paths leaked into an exported roaming AI Context.");

        var newDeviceService = new WorkspaceAiContextService(
            Path.Combine(appData, "new-device-ai-contexts.json"));
        var imported = newDeviceService.ImportRoamingDefinitions(
            roaming,
            roamingSource.Id);
        var importedAutonomous = newDeviceService.GetContext(
            roamingSource.Id);
        Require(
            imported.SelectedContextId
                == WorkspaceAiContextDefinition.DefaultId
            && imported.RequiresDeviceConfirmationIds.Contains(
                roamingSource.Id)
            && importedAutonomous is
            {
                RiskWarningAcknowledged: false,
                AllowInternet: true,
                AskBeforeChanges: false
            },
            "A new PC activated a roaming unrestricted Context without fresh confirmation.");
        ExpectInvalidOperation(() => newDeviceService.Select(roamingSource.Id));
        var confirmed = newDeviceService.SaveContext(
            importedAutonomous! with
            {
                RiskWarningAcknowledged = true
            });
        Require(
            confirmed.RiskWarningAcknowledged
            && newDeviceService.SelectedContextId == roamingSource.Id,
            "A user-confirmed roaming Context could not be activated locally.");

        Console.WriteLine(
            "WORKSPACE_AI_CONTEXT_OK|default-custom|persistent|read-create-edit-delete-separated|terminal-scope|review-toggle|internet-gate|roaming-without-local-paths|fresh-device-confirmation");
    }

    private static WorkspaceAiContextPathPermission Location(string path) =>
        new()
        {
            Mode = WorkspaceAiContextAccessMode.ChooseLocation,
            Location = path
        };

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ExpectInvalidOperation(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException(
                "The expected invalid-operation failure did not occur.");
        }
        catch (InvalidOperationException exception) when (
            !exception.Message.StartsWith(
                "The expected",
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
}
