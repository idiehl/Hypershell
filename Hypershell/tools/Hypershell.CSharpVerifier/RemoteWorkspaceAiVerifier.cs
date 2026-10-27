using Hypershell.Models;
using Hypershell.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class RemoteWorkspaceAiVerifier
{
    private const string HostId = "remote-ai-host";
    private const string ConversationId = "conversation-remote-ai-verifier";

    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "remote-workspace-ai");
        var backingRoot = Path.Combine(root, "linked-host");
        var appDataRoot = Path.Combine(root, "app-data");
        var localFallback = Path.Combine(root, "local-fallback");
        foreach (var name in new[]
                 {
                     "read", "create", "edit", "delete", "terminal", "outside"
                 })
        {
            Directory.CreateDirectory(Path.Combine(backingRoot, name));
        }

        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(localFallback);
        await WriteAsync(backingRoot, "read/readme.md", "REMOTE_READ_SECRET");
        await WriteAsync(backingRoot, "create/existing.txt", "create only");
        await WriteAsync(backingRoot, "edit/edit.txt", "edit original");
        await WriteAsync(backingRoot, "delete/delete.txt", "delete original");

        await using var registry = new WorkspaceFileSystemProviderRegistry(
            new LocalWorkspaceFileSystemProvider());
        registry.Register(new WorkspaceDocumentVerifier.MappedSshWorkspaceProvider(
            HostId,
            backingRoot));

        var readRoot = Remote("/read");
        var createRoot = Remote("/create");
        var editRoot = Remote("/edit");
        var deleteRoot = Remote("/delete");
        var terminalRoot = Remote("/terminal");
        var contextService = new WorkspaceAiContextService(
            Path.Combine(appDataRoot, "ai-contexts.device.json"));
        var context = contextService.SaveContext(
            contextService.CreateDraft() with
            {
                Name = "Remote AI verifier",
                Read = UriPermission(readRoot, editRoot),
                Create = UriPermission(createRoot),
                Edit = UriPermission(editRoot),
                Delete = UriPermission(deleteRoot),
                Terminal = UriPermission(terminalRoot),
                DefaultSaveDirectory = localFallback,
                DefaultSaveWorkspaceUri = createRoot,
                AskBeforeChanges = false,
                RiskWarningAcknowledged = true
            });
        var audit = new WorkspaceAiAuditService(
            Path.Combine(appDataRoot, "remote-ai.audit.jsonl"));
        var broker = new WorkspaceAiPermissionBroker(
            Path.Combine(appDataRoot, "legacy-permissions.device.json"),
            audit,
            contextService);
        var operations = new WorkspaceAiOperationService(
            broker,
            audit,
            workspaceProviders: registry);

        using (operations.BeginAuditScope(ConversationId))
        {
            var listing = await operations.ListDirectoryAsync(
                context.Id,
                readRoot);
            var read = await operations.ReadTextAsync(
                context.Id,
                Remote("/read/readme.md"));
            var search = await operations.SearchTextAsync(
                context.Id,
                readRoot,
                "REMOTE_READ_SECRET",
                "*.md",
                useRegex: false,
                caseSensitive: true);
            Require(
                listing.Succeeded
                && listing.Value?.Entries.Single().RelativePath
                    == Remote("/read/readme.md")
                && read.Succeeded
                && read.Value?.Content == "REMOTE_READ_SECRET"
                && search.Succeeded
                && search.Value?.Matches.Single().Path
                    == Remote("/read/readme.md"),
                "The AI could not list, read, and recursively search the URI-scoped linked-host workspace.");

            var created = await operations.WriteTextAsync(
                context.Id,
                Remote("/create/new.txt"),
                "remote created",
                expectedSha256: null,
                allowCreate: true);
            var deniedCreateOverwrite = await operations.WriteTextAsync(
                context.Id,
                Remote("/create/existing.txt"),
                "must not replace",
                Hash("create only"),
                allowCreate: false);
            var deniedEditCreate = await operations.WriteTextAsync(
                context.Id,
                Remote("/edit/new.txt"),
                "must not create",
                expectedSha256: null,
                allowCreate: true);
            Require(
                created.Succeeded
                && deniedCreateOverwrite.Status == WorkspaceAiOperationStatus.Failed
                && deniedEditCreate.Status == WorkspaceAiOperationStatus.Failed
                && await ReadAsync(backingRoot, "create/existing.txt") == "create only"
                && !File.Exists(Path.Combine(backingRoot, "edit", "new.txt")),
                "Remote Create and Edit scopes were not enforced independently.");

            var edited = await operations.WriteTextAsync(
                context.Id,
                Remote("/edit/edit.txt"),
                "remote edited",
                Hash("edit original"),
                allowCreate: false);
            var deleted = await operations.DeleteTextAsync(
                context.Id,
                Remote("/delete/delete.txt"),
                Hash("delete original"));
            Require(
                edited.Succeeded
                && deleted.Succeeded
                && await ReadAsync(backingRoot, "edit/edit.txt") == "remote edited"
                && !File.Exists(Path.Combine(backingRoot, "delete", "delete.txt")),
                "The SHA-guarded remote edit or confirmed permanent deletion failed.");

            await VerifyRemoteDocumentAsync(context.Id, operations, editRoot);
        }

        await VerifyRemoteTerminalAsync(
            context.Id,
            broker,
            operations,
            terminalRoot);

        var records = audit.ReadRecent(200);
        var auditText = await File.ReadAllTextAsync(audit.AuditPath);
        Require(
            records.Any(record => record.ConversationId == ConversationId
                                  && record.RemoteHostId == HostId
                                  && record.WorkspaceUri.StartsWith(
                                      $"ssh://{HostId}/",
                                      StringComparison.Ordinal))
            && !auditText.Contains("REMOTE_READ_SECRET", StringComparison.Ordinal)
            && !auditText.Contains("REMOTE_TERMINAL_SECRET", StringComparison.Ordinal)
            && !auditText.Contains("remote created", StringComparison.Ordinal)
            && !auditText.Contains("remote edited", StringComparison.Ordinal),
            "The remote AI audit trail lost its scope identifiers or recorded sensitive content.");

        Console.WriteLine(
            "REMOTE_WORKSPACE_AI_OK|uri-scopes|list-read-search|create-edit-delete-separated|sha-preconditions|live-remote-document|exact-terminal-binding|conversation-host-audit|content-free-audit");
    }

    private static async Task VerifyRemoteDocumentAsync(
        string contextId,
        WorkspaceAiOperationService operations,
        string editRoot)
    {
        var provider = new TextDocumentProvider();
        var content = "live remote editor";
        var location = $"{editRoot}/edit.txt";
        var read = await operations.ReadOpenDocumentAsync(
            contextId,
            location,
            "remote-editor-session",
            "edit.txt",
            "plaintext",
            TextDocumentProvider.ProviderId,
            hasUnsavedChanges: true,
            () => content,
            () => provider.Read(new DocumentReadRequest(
                content,
                "plaintext",
                location)));
        Require(read.Succeeded && read.Value is not null,
            "The AI could not inspect the active remote document.");

        var replaced = await operations.ReplaceOpenDocumentAsync(
            contextId,
            location,
            "remote-editor-session",
            "live remote replacement",
            read.Value!.Sha256,
            () => content,
            replacement =>
            {
                var mutation = provider.Replace(new DocumentReplaceRequest(
                    content,
                    0,
                    content.Length,
                    replacement,
                    "plaintext",
                    location));
                if (mutation.Succeeded)
                {
                    content = mutation.Content;
                }

                return mutation;
            });
        Require(
            replaced.Succeeded && content == "live remote replacement",
            "The AI could not replace the active remote document through its typed provider.");
    }

    private static async Task VerifyRemoteTerminalAsync(
        string contextId,
        WorkspaceAiPermissionBroker broker,
        WorkspaceAiOperationService operations,
        string terminalRoot)
    {
        var capturedSession = string.Empty;
        var capturedWorkspace = string.Empty;
        var dispatched = 0;
        WorkspaceAiActiveTerminalHandle ActiveTerminal() => new(
            "remote-terminal-session",
            "Remote Bash",
            "Bash",
            "/terminal",
            terminalRoot,
            HostId,
            "remote$",
            IsRunning: false,
            () => "pwd",
            () =>
            [
                new WorkspaceAiTerminalLineSnapshot("Output", "/terminal")
            ]);
        var toolHost = new WorkspaceAiToolHost(
            broker,
            operations,
            (_, _) => null,
            (_, _, _) => null,
            (_, _) => ActiveTerminal(),
            async (workspaceId, sessionId, workspaceUri, command, execute,
                userConfirmed, cancellationToken) =>
            {
                capturedSession = sessionId;
                capturedWorkspace = workspaceUri;
                return await operations.DispatchTerminalAsync(
                    workspaceId,
                    workspaceUri,
                    command,
                    execute,
                    userConfirmed,
                    (_, _, _, _) =>
                    {
                        dispatched++;
                        return Task.CompletedTask;
                    },
                    cancellationToken);
            });

        var terminalRead = await toolHost.ExecuteAsync(
            contextId,
            Call("remote-terminal-read", "read_active_terminal", new { }),
            ConversationId);
        using (var output = JsonDocument.Parse(terminalRead.FunctionOutput))
        {
            Require(
                output.RootElement.GetProperty("workspace_uri").GetString()
                    == terminalRoot
                && output.RootElement.GetProperty("remote_host_id").GetString()
                    == HostId,
                "The active terminal snapshot lost its linked-host identity.");
        }

        var terminalCommand = await toolHost.ExecuteAsync(
            contextId,
            Call(
                "remote-terminal-command",
                "execute_terminal_command",
                new
                {
                    command = "printf REMOTE_TERMINAL_SECRET"
                }),
            ConversationId);
        var outside = await operations.DispatchTerminalAsync(
            contextId,
            Remote("/outside"),
            "pwd",
            execute: true,
            userConfirmed: true,
            (_, _, _, _) => Task.CompletedTask);
        Require(
            terminalCommand.PendingReview is null
            && dispatched == 1
            && capturedSession == "remote-terminal-session"
            && capturedWorkspace == terminalRoot
            && outside.Status == WorkspaceAiOperationStatus.Denied,
            "Remote terminal execution was not bound to the exact authorized tab and workspace URI.");
    }

    private static WorkspaceAiContextPathPermission UriPermission(
        params string[] workspaceUris) => new()
        {
            Mode = WorkspaceAiContextAccessMode.ChooseLocation,
            WorkspaceUris = workspaceUris
        };

    private static WorkspaceAiFunctionCall Call(
        string callId,
        string name,
        object arguments) =>
        new(callId, name, JsonSerializer.Serialize(arguments));

    private static string Remote(string path) =>
        WorkspaceLocation.Ssh(HostId, path).Identity;

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();

    private static Task WriteAsync(
        string root,
        string relativePath,
        string content) =>
        File.WriteAllTextAsync(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            content,
            new UTF8Encoding(false));

    private static Task<string> ReadAsync(string root, string relativePath) =>
        File.ReadAllTextAsync(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
