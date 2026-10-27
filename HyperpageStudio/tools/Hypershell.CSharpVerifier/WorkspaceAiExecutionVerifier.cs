using Hypershell.Services;
using System.Text.Json;

internal static class WorkspaceAiExecutionVerifier
{
    public static void Verify(string verifierRoot)
    {
        var projectRoot = Path.Combine(
            verifierRoot,
            "workspace-ai-execution",
            "project");
        var sourceRoot = Path.Combine(projectRoot, "src");
        var outsideRoot = Path.Combine(
            verifierRoot,
            "workspace-ai-execution",
            "outside");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outsideRoot);

        var normalized = WorkspaceAiWorkingDirectory.Normalize(projectRoot);
        Require(
            string.Equals(
                WorkspaceAiWorkingDirectory.ResolveProjectPath(
                    normalized,
                    "."),
                normalized,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                WorkspaceAiWorkingDirectory.ResolveProjectPath(
                    normalized,
                    Path.Combine("src", "app.cs")),
                Path.Combine(sourceRoot, "app.cs"),
                StringComparison.OrdinalIgnoreCase),
            "The conversation working directory did not resolve short project-relative paths.");
        ExpectFailure(() => WorkspaceAiWorkingDirectory.ResolveProjectPath(
            normalized,
            Path.Combine(outsideRoot, "escape.cs")));
        ExpectFailure(() => WorkspaceAiWorkingDirectory.ResolveProjectPath(
            string.Empty,
            "relative.cs"));

        var coding = WorkspaceAiTaskToolRouter.Route(
            "Build and test a small HTML web app in this project.",
            WorkspaceAiComputerControlMode.Desktop);
        var desktop = WorkspaceAiTaskToolRouter.Route(
            "Click the browser menu on my desktop and type into the dialog.",
            WorkspaceAiComputerControlMode.Desktop);
        var fileEdit = WorkspaceAiTaskToolRouter.Route(
            "Create a file in this folder.",
            WorkspaceAiComputerControlMode.Desktop);
        var continuation = WorkspaceAiTaskToolRouter.Route(
            "Try again.",
            WorkspaceAiComputerControlMode.Desktop,
            coding);
        var conversation = WorkspaceAiTaskToolRouter.Route(
            "What is the capital of France?",
            WorkspaceAiComputerControlMode.Desktop,
            coding);
        var webTemplate = WorkspaceAiTaskToolRouter.Route(
            "Create a Hyperpage Studio website template from https://example.com/page.",
            WorkspaceAiComputerControlMode.Desktop);
        var numberedFoundationAnswer = WorkspaceAiTaskToolRouter.Route(
            "1. Families\n2. Minimal\n3. Three pages",
            WorkspaceAiComputerControlMode.Off);
        var hyperpageProject = WorkspaceAiTaskToolRouter
            .MergeHyperpageProjectRoute(
                numberedFoundationAnswer,
                WorkspaceAiComputerControlMode.Off);
        Require(
            coding.Name.Contains("coding", StringComparison.Ordinal)
            && coding.ToolNames.Contains("prepare_workspace_write")
            && coding.ToolNames.Contains("prepare_workspace_edit")
            && coding.ToolNames.Contains("search_workspace_text")
            && coding.ToolNames.Contains("run_project_check")
            && coding.ToolNames.Contains("execute_terminal_command")
            && !coding.ToolNames.Contains(
                WorkspaceAiComputerFunctionTool.FunctionName)
            && desktop.ToolNames.Contains(
                WorkspaceAiWindowsUiFunctionTool.FunctionName)
            && desktop.ToolNames.Contains(
                WorkspaceAiComputerFunctionTool.FunctionName)
            && !desktop.ToolNames.Contains("prepare_workspace_write")
            && fileEdit.Name.Contains("files", StringComparison.Ordinal)
            && fileEdit.ToolNames.Contains("prepare_workspace_write")
            && fileEdit.ToolNames.Contains("prepare_workspace_edit")
            && fileEdit.ToolNames.Contains("search_workspace_text")
            && continuation.Name.StartsWith(
                "continue:coding",
                StringComparison.Ordinal)
            && continuation.ToolNames.Contains("prepare_workspace_write")
            && webTemplate.ToolNames.Contains("read_active_visual_page")
            && webTemplate.ToolNames.Contains("capture_web_template")
            && webTemplate.ToolNames.Contains("fetch_url_text")
            && conversation.Name == "conversation"
            && conversation.ToolNames.Count == 0
            && hyperpageProject.Name ==
                WorkspaceAiTaskToolRouter.HyperpageProjectRouteName
            && hyperpageProject.ToolNames.Contains("prepare_workspace_write")
            && hyperpageProject.ToolNames.Contains("prepare_workspace_edit")
            && hyperpageProject.ToolNames.Contains("propose_visual_page_changes")
            && hyperpageProject.ToolNames.Contains("prepare_visual_page_markup")
            && hyperpageProject.ToolNames.Contains("build_visual_page_preview")
            && hyperpageProject.ToolNames.Contains("fetch_url_text"),
            "Task-time routing did not keep one model while narrowing coding and desktop tool surfaces independently.");

        var originalSource =
            "internal sealed class Counter\r\n"
            + "{\r\n"
            + "    private int _value = 1;\r\n"
            + "}\r\n";
        var exactEdit = WorkspaceAiExactEdit.Apply(
            originalSource,
            """
            <<<<<<< SEARCH
                private int _value = 1;
            =======
                private int _value = 2;
            >>>>>>> REPLACE
            """);
        var ambiguousEdit = WorkspaceAiExactEdit.Apply(
            "alpha alpha",
            """
            <<<<<<< SEARCH
            alpha
            =======
            beta
            >>>>>>> REPLACE
            """);
        Require(
            exactEdit.Succeeded
            && exactEdit.EditCount == 1
            && exactEdit.Content.Contains(
                "private int _value = 2;",
                StringComparison.Ordinal)
            && exactEdit.Content.Contains("\r\n", StringComparison.Ordinal)
            && !ambiguousEdit.Succeeded
            && ambiguousEdit.Message.Contains(
                "more than once",
                StringComparison.OrdinalIgnoreCase),
            "The SHA-ready exact-edit engine did not preserve line endings or reject an ambiguous match.");

        var projectEntries = new List<WorkspaceAiDirectoryEntry>
        {
            new(
                "Verifier.sln",
                "Verifier.sln",
                IsDirectory: false,
                Size: 128,
                DateTimeOffset.UtcNow),
            new(
                "src",
                "src",
                IsDirectory: true,
                Size: 0,
                DateTimeOffset.UtcNow)
        };
        var dotnetCheck = WorkspaceAiProjectCheckPlanner.Create(
            "test",
            projectEntries,
            packageJson: null,
            posixShell: false);
        var nodeCheck = WorkspaceAiProjectCheckPlanner.Create(
            "typecheck",
            [
                new WorkspaceAiDirectoryEntry(
                    "package.json",
                    "package.json",
                    IsDirectory: false,
                    Size: 128,
                    DateTimeOffset.UtcNow),
                new WorkspaceAiDirectoryEntry(
                    "pnpm-lock.yaml",
                    "pnpm-lock.yaml",
                    IsDirectory: false,
                    Size: 128,
                    DateTimeOffset.UtcNow)
            ],
            """
            { "scripts": { "typecheck": "tsc --noEmit" } }
            """,
            posixShell: false);
        Require(
            dotnetCheck.Plan is
            {
                ProjectKind: "dotnet",
                EffectiveCheck: "test"
            }
            && dotnetCheck.Plan.Command.StartsWith(
                "dotnet test",
                StringComparison.Ordinal)
            && nodeCheck.Plan is
            {
                ProjectKind: "node",
                Command: "pnpm run 'typecheck'"
            },
            "The semantic project-check planner did not select deterministic .NET and package-script commands.");

        using var diagnostics = WorkspaceAiRunDiagnosticSession.TryStart(
            projectRoot,
            "conversation-verifier",
            "context-verifier",
            WorkspaceAiProviderConfiguration.LlamaCppProviderId,
            WorkspaceAiModelCatalog.EngineerModelId,
            coding,
            "Build the verifier app.")
            ?? throw new InvalidOperationException(
                "The local AI diagnostic run could not be created.");
        diagnostics.Record("tool_result", new
        {
            tool = "read_workspace_text",
            authorization =
                "Authorization: Bearer sk-verifier-secret-token-1234567890",
            result = "read complete"
        });
        diagnostics.Complete(
            "succeeded",
            "The verifier app was built.");

        var summaryPath = Path.Combine(
            diagnostics.RunDirectory,
            "summary.json");
        var eventsPath = Path.Combine(
            diagnostics.RunDirectory,
            "events.jsonl");
        using var summary = JsonDocument.Parse(
            File.ReadAllText(summaryPath));
        var events = File.ReadAllText(eventsPath);
        Require(
            File.Exists(Path.Combine(
                projectRoot,
                ".hypershell",
                "ai-runs",
                "README.md"))
            && File.ReadAllText(Path.Combine(
                    projectRoot,
                    ".hypershell",
                    "ai-runs",
                    ".gitignore"))
                .Contains("*", StringComparison.Ordinal)
            && File.Exists(Path.Combine(
                diagnostics.RunDirectory,
                "prompt.md"))
            && File.Exists(Path.Combine(
                diagnostics.RunDirectory,
                "response.md"))
            && summary.RootElement.GetProperty("status").GetString()
                == "succeeded"
            && summary.RootElement.GetProperty("workingDirectory")
                .GetString() == normalized
            && summary.RootElement.GetProperty("taskRoute")
                .GetString() == coding.Name
            && events.Contains("[REDACTED_TOKEN]", StringComparison.Ordinal)
            && !events.Contains(
                "sk-verifier-secret-token",
                StringComparison.Ordinal),
            "Per-cycle project diagnostics lost required documents, metadata, or secret redaction.");

        Console.WriteLine(
            "WORKSPACE_AI_EXECUTION_OK|conversation-root|escape-blocked|task-routed-tools|hyperpage-persistent-tools|repository-search-route|exact-edit|semantic-project-checks|project-run-details|secret-redaction");
    }

    private static void ExpectFailure(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException)
        {
            return;
        }

        throw new InvalidOperationException(
            "A working-directory escape or unrooted relative path was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
