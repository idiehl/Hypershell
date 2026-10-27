using Hypershell.Profiles;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Text;

internal static class WorkspaceAiPermissionVerifier
{
    private const string WorkspaceId = "project.verifier:workspace";
    private const string SecondaryWorkspaceId = "manual.verifier:secondary";
    private const string SensitiveContent =
        "workspace-secret-content-must-not-enter-audit";
    private const string SensitiveCommand =
        "Write-Output terminal-secret-must-not-enter-audit";

    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "workspace-ai-permissions");
        var workspaceRoot = Path.Combine(root, "workspace");
        var outsideRoot = Path.Combine(root, "outside");
        var appDataRoot = Path.Combine(root, "app-data");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(outsideRoot);
        Directory.CreateDirectory(appDataRoot);
        var originalCreationTime = Directory.GetCreationTimeUtc(workspaceRoot);
        var documentPath = Path.Combine(workspaceRoot, "notes.txt");
        var outsidePath = Path.Combine(outsideRoot, "outside.txt");
        await File.WriteAllTextAsync(
            documentPath,
            SensitiveContent + "\r\n",
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true,
                throwOnInvalidBytes: true));
        await File.WriteAllTextAsync(outsidePath, "outside-original");
        var searchSourceRoot = Path.Combine(workspaceRoot, "search-src");
        var ignoredDependencyRoot = Path.Combine(
            workspaceRoot,
            "node_modules");
        Directory.CreateDirectory(searchSourceRoot);
        Directory.CreateDirectory(ignoredDependencyRoot);
        await File.WriteAllTextAsync(
            Path.Combine(searchSourceRoot, "Beacon.cs"),
            "internal sealed class LandmarkBeacon { }\r\n",
            new UTF8Encoding(false));
        await File.WriteAllTextAsync(
            Path.Combine(ignoredDependencyRoot, "Ignored.cs"),
            "internal sealed class LandmarkBeacon { }\r\n",
            new UTF8Encoding(false));

        var auditPath = Path.Combine(
            appDataRoot,
            "ai-operations.audit.jsonl");
        var statePath = Path.Combine(
            appDataRoot,
            "ai-permissions.device.json");
        var audit = new WorkspaceAiAuditService(auditPath);
        var broker = new WorkspaceAiPermissionBroker(
            statePath,
            audit);
        var operations = new WorkspaceAiOperationService(
            broker,
            audit);

        var unapprovedRead = await operations.ReadTextAsync(
            WorkspaceId,
            "notes.txt");
        Require(
            unapprovedRead.Status == WorkspaceAiOperationStatus.Denied,
            "An unapproved workspace read was not denied.");

        var readApproval = broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: false,
            allowTerminal: false,
            displayName: "Verifier read root");
        Require(
            readApproval.AllowRead
            && !readApproval.AllowWrite
            && !readApproval.AllowTerminal
            && readApproval.DisplayName == "Verifier read root"
            && readApproval.RootFingerprint.Length == 64,
            "The read-only workspace approval was malformed.");

        var read = await operations.ReadTextAsync(
            WorkspaceId,
            "notes.txt");
        Require(
            read.Succeeded
            && read.Value is
            {
                Content: var content,
                EncodingKind: Hypershell.Models.DocumentEncodingKind.Utf8WithBom,
                LineEndingKind: Hypershell.Models.DocumentLineEndingKind.CrLf,
                Sha256.Length: 64
            }
            && content.Contains(SensitiveContent, StringComparison.Ordinal),
            "The approved bounded text read did not preserve document metadata.");

        var listing = await operations.ListDirectoryAsync(WorkspaceId);
        Require(
            listing.Succeeded
            && listing.Value?.Entries.Any(entry =>
                entry.Name == "notes.txt"
                && !entry.IsDirectory) == true,
            "The approved bounded directory listing did not return the workspace file.");

        var repositorySearch = await operations.SearchTextAsync(
            WorkspaceId,
            ".",
            "LandmarkBeacon",
            "*.cs",
            useRegex: false,
            caseSensitive: true);
        Require(
            repositorySearch.Succeeded
            && repositorySearch.Value is
            {
                IsTruncated: false,
                Matches.Count: 1
            }
            && repositorySearch.Value.Matches[0].DisplayPath
                .Replace('\\', '/') == "search-src/Beacon.cs"
            && repositorySearch.Value.Matches[0].Line == 1,
            "The bounded repository search did not find source text or failed to exclude dependency content.");

        var deniedWrite = await operations.WriteTextAsync(
            WorkspaceId,
            "notes.txt",
            "must-not-write",
            read.Value!.Sha256,
            allowCreate: false);
        Require(
            deniedWrite.Status == WorkspaceAiOperationStatus.Denied
            && (await File.ReadAllTextAsync(documentPath))
                .Contains(SensitiveContent, StringComparison.Ordinal),
            "A read-only workspace approval permitted a write.");

        var traversal = await operations.ReadTextAsync(
            WorkspaceId,
            "..\\outside\\outside.txt");
        var absolute = await operations.ReadTextAsync(
            WorkspaceId,
            outsidePath);
        var alternateDataStream = await operations.ReadTextAsync(
            WorkspaceId,
            "notes.txt:hidden");
        Require(
            traversal.Status == WorkspaceAiOperationStatus.Denied
            && absolute.Status == WorkspaceAiOperationStatus.Denied
            && alternateDataStream.Status == WorkspaceAiOperationStatus.Denied,
            "A traversal, absolute path, or alternate data stream escaped the workspace boundary.");

        var linkedDirectory = Path.Combine(workspaceRoot, "linked-outside");
        try
        {
            Directory.CreateSymbolicLink(linkedDirectory, outsideRoot);
            var linkedRead = await operations.ReadTextAsync(
                WorkspaceId,
                "linked-outside\\outside.txt");
            Require(
                linkedRead.Status == WorkspaceAiOperationStatus.Denied,
                "A symbolic-link traversal escaped the approved workspace root.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                         or IOException
                                         or PlatformNotSupportedException)
        {
        }

        var fullApproval = broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Verifier development root",
            allowTerminalRead: true);
        Require(
            fullApproval.AllowRead
            && fullApproval.AllowWrite
            && !fullApproval.AllowAutomaticFileAndEditorChanges
            && fullApproval.AllowTerminalRead
            && fullApproval.AllowTerminal
            && fullApproval.TerminalExecutionPolicy
                == WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand
            && !fullApproval.AllowNetwork
            && fullApproval.DisplayName == "Verifier development root",
            "The explicit workspace capability update was not retained.");

        var autonomyEditor = new WorkspaceAiAccessEditorViewModel(
            new WorkspaceAiWorkspaceDescriptor(
                WorkspaceId,
                "Verifier development root",
                workspaceRoot),
            fullApproval);
        autonomyEditor.AllowAutonomousActions = true;
        Require(
            autonomyEditor.AllowAutomaticFileAndEditorChanges
            && autonomyEditor.AllowTerminalRead
            && autonomyEditor.AllowTerminal
            && autonomyEditor.TerminalExecutionPolicy
                == WorkspaceAiTerminalExecutionPolicy.AutoExecute,
            "The combined autonomy toggle did not enable file/editor and terminal standing permissions.");
        autonomyEditor.AllowAutonomousActions = false;
        Require(
            !autonomyEditor.AllowAutomaticFileAndEditorChanges
            && autonomyEditor.AllowWrite
            && autonomyEditor.AllowTerminal
            && autonomyEditor.TerminalExecutionPolicy
                == WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            "Disabling combined autonomy did not restore cautious review defaults while preserving base capabilities.");

        var secondaryApproval = broker.Approve(
            SecondaryWorkspaceId,
            outsideRoot,
            allowRead: true,
            allowWrite: false,
            allowTerminal: false,
            displayName: "Verifier secondary root");
        var reloadedBroker = new WorkspaceAiPermissionBroker(
            statePath,
            audit);
        Require(
            secondaryApproval.DisplayName == "Verifier secondary root"
            && reloadedBroker.GetApprovals().Count == 2
            && reloadedBroker.GetApprovals().Any(approval =>
                approval.WorkspaceId == WorkspaceId
                && approval.DisplayName == "Verifier development root"
                && approval.AllowTerminalRead
                && approval.TerminalExecutionPolicy
                    == WorkspaceAiTerminalExecutionPolicy
                        .ConfirmEveryCommand),
            "Multiple named AI roots or their granular capabilities did not persist.");

        var utf16Path = Path.Combine(workspaceRoot, "utf16.txt");
        await File.WriteAllTextAsync(
            utf16Path,
            "utf16-original",
            Encoding.Unicode);
        var utf16Read = await operations.ReadTextAsync(
            WorkspaceId,
            "utf16.txt");
        Require(
            utf16Read.Succeeded && utf16Read.Value is not null,
            "The UTF-16 precondition fixture could not be read.");
        var oversizedEncodedWrite = await operations.WriteTextAsync(
            WorkspaceId,
            "utf16.txt",
            new string('x', (int)(WorkspaceAiOperationService.MaximumTextBytes / 2) + 1),
            utf16Read.Value!.Sha256,
            allowCreate: false);
        Require(
            oversizedEncodedWrite.Status == WorkspaceAiOperationStatus.Failed
            && await File.ReadAllTextAsync(utf16Path, Encoding.Unicode)
                == "utf16-original",
            "A text replacement exceeded the byte limit after encoding or changed the original file.");

        var staleWrite = await operations.WriteTextAsync(
            WorkspaceId,
            "notes.txt",
            "stale-write-must-not-land",
            new string('0', 64),
            allowCreate: false);
        Require(
            staleWrite.Status == WorkspaceAiOperationStatus.Failed
            && (await File.ReadAllTextAsync(documentPath))
                .Contains(SensitiveContent, StringComparison.Ordinal),
            "A stale SHA-256 precondition overwrote the workspace file.");

        const string replacement = "replacement applied atomically\r\n";
        var replacementWrite = await operations.WriteTextAsync(
            WorkspaceId,
            "notes.txt",
            replacement,
            read.Value.Sha256,
            allowCreate: false);
        Require(
            replacementWrite.Succeeded
            && replacementWrite.Value is
            {
                WasCreated: false,
                Sha256.Length: 64
            }
            && await File.ReadAllTextAsync(documentPath) == replacement,
            "The compare-before-write replacement did not apply atomically.");

        var createdWrite = await operations.WriteTextAsync(
            WorkspaceId,
            "created.cs",
            "Console.WriteLine(\"created\");\n",
            expectedSha256: null,
            allowCreate: true);
        Require(
            createdWrite.Succeeded
            && createdWrite.Value is { WasCreated: true }
            && File.Exists(Path.Combine(workspaceRoot, "created.cs")),
            "The explicitly authorized workspace file creation failed.");

        var dispatchCount = 0;
        var executedCount = 0;

        var terminalState = await operations.ReadTerminalStateAsync(
            WorkspaceId,
            "Verifier shell",
            "PowerShell",
            workspaceRoot,
            $"PS {workspaceRoot}>",
            isRunning: false,
            () => SensitiveCommand,
            () => new List<WorkspaceAiTerminalLineSnapshot>
            {
                new("Input", SensitiveCommand),
                new("Output", "terminal snapshot output")
            });
        Require(
            terminalState.Succeeded
            && terminalState.Value is
            {
                DraftCommand: SensitiveCommand,
                Transcript.Count: 2,
                IsTranscriptTruncated: false
            },
            "The separately approved live terminal-state snapshot was not returned.");

        Task Dispatch(
            string command,
            string workspaceRootPath,
            bool execute,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                command == SensitiveCommand,
                "The terminal dispatcher received different command text.");
            Require(
                string.Equals(
                    workspaceRootPath,
                    workspaceRoot,
                    StringComparison.OrdinalIgnoreCase),
                "The terminal dispatcher did not receive the approved workspace root.");
            dispatchCount++;
            if (execute)
            {
                executedCount++;
            }

            return Task.CompletedTask;
        }

        var staged = await operations.DispatchTerminalAsync(
            WorkspaceId,
            SensitiveCommand,
            execute: false,
            userConfirmed: false,
            Dispatch);
        var unconfirmed = await operations.DispatchTerminalAsync(
            WorkspaceId,
            SensitiveCommand,
            execute: true,
            userConfirmed: false,
            Dispatch);
        var confirmed = await operations.DispatchTerminalAsync(
            WorkspaceId,
            SensitiveCommand,
            execute: true,
            userConfirmed: true,
            Dispatch);
        Require(
            staged.Succeeded
            && staged.Value is { Executed: false }
            && unconfirmed.Status
            == WorkspaceAiOperationStatus.ConfirmationRequired
            && confirmed.Succeeded
            && confirmed.Value is { Executed: true }
            && dispatchCount == 2
            && executedCount == 1,
            "Terminal staging and fresh execution confirmation were not separated.");

        broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Verifier development root",
            allowTerminalRead: true,
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.ReviewOnly);
        var reviewOnlyExecution = await operations.DispatchTerminalAsync(
            WorkspaceId,
            SensitiveCommand,
            execute: true,
            userConfirmed: false,
            Dispatch);
        Require(
            reviewOnlyExecution.Succeeded
            && broker.GetTerminalExecutionPolicy(WorkspaceId)
                == WorkspaceAiTerminalExecutionPolicy.ReviewOnly,
            "The review-only terminal policy still required a second native confirmation.");

        broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Verifier development root",
            allowTerminalRead: true,
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.AutoExecute,
            allowAutomaticFileAndEditorChanges: true);
        var autonomousExecution = await operations.DispatchTerminalAsync(
            WorkspaceId,
            SensitiveCommand,
            execute: true,
            userConfirmed: false,
            Dispatch);
        var policyReload = new WorkspaceAiPermissionBroker(
            statePath,
            audit);
        Require(
            autonomousExecution.Succeeded
            && policyReload.AllowsAutomaticFileAndEditorChanges(WorkspaceId)
            && policyReload.GetTerminalExecutionPolicy(WorkspaceId)
                == WorkspaceAiTerminalExecutionPolicy.AutoExecute
            && dispatchCount == 4
            && executedCount == 3,
            "The standing auto-execution policy was not enforced or persisted per root.");

        Require(
            await File.ReadAllTextAsync(outsidePath) == "outside-original",
            "A workspace operation changed a file outside the approved root.");
        Require(
            !Directory.EnumerateFiles(
                    workspaceRoot,
                    "*.ai-write",
                    SearchOption.AllDirectories)
                .Any(),
            "A workspace AI operation left a temporary write artifact behind.");

        var auditText = await File.ReadAllTextAsync(auditPath);
        var permissionText = await File.ReadAllTextAsync(statePath);
        Require(
            !auditText.Contains(SensitiveContent, StringComparison.Ordinal)
            && !auditText.Contains(SensitiveCommand, StringComparison.Ordinal)
            && !permissionText.Contains(SensitiveContent, StringComparison.Ordinal)
            && !permissionText.Contains(SensitiveCommand, StringComparison.Ordinal)
            && audit.ReadRecent(100).Any(entry =>
                entry.Outcome == WorkspaceAiAuditOutcome.ConfirmationRequired),
            "The local permission or audit state leaked operation content or omitted confirmation metadata.");

        Directory.Delete(workspaceRoot, recursive: true);
        Directory.CreateDirectory(workspaceRoot);
        Directory.SetCreationTimeUtc(
            workspaceRoot,
            originalCreationTime.AddDays(-7));
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "notes.txt"),
            "replacement-root");
        var changedRootRead = await operations.ReadTextAsync(
            WorkspaceId,
            "notes.txt");
        Require(
            changedRootRead.Status == WorkspaceAiOperationStatus.Denied
            && changedRootRead.Message.Contains(
                "identity changed",
                StringComparison.OrdinalIgnoreCase),
            "A replaced workspace root inherited the prior device approval.");

        Require(
            broker.Revoke(WorkspaceId)
            && broker.Revoke(SecondaryWorkspaceId)
            && broker.GetApprovals().Count == 0,
            "Revoking a workspace did not remove its device-local approval.");

        var rotatingAudit = new WorkspaceAiAuditService(
            Path.Combine(appDataRoot, "rotation.audit.jsonl"),
            maximumLogBytes: 4096);
        for (var index = 0; index < 80; index++)
        {
            rotatingAudit.Record(new WorkspaceAiAuditEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                WorkspaceId = WorkspaceId,
                Operation = WorkspaceAiOperationKind.ReadText,
                Capability = WorkspaceAiCapability.Read,
                Target = $"entry-{index:D3}.txt",
                Outcome = WorkspaceAiAuditOutcome.Succeeded,
                Detail = new string('x', 128)
            });
        }

        Require(
            File.Exists(rotatingAudit.PreviousAuditPath)
            && new FileInfo(rotatingAudit.AuditPath).Length <= 4096,
            "The bounded local AI audit log did not rotate.");

        Console.WriteLine(
            "WORKSPACE_AI_OK|named-multi-root|granular-terminal-read|root-fingerprint|path-containment|reparse-guard|bounded-read-list|atomic-cas-write|autonomy-toggle|auto-file-editor|terminal-policies:3|content-free-audit|rotation");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
