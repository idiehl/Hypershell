using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static class VisualPageAiVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        const string workspaceId = "visual-page-ai-verifier";
        var workspaceRoot = Path.Combine(verifierRoot, "workspace");
        var appDataRoot = Path.Combine(verifierRoot, "app-data");
        var pageDirectory = Path.Combine(workspaceRoot, "pages");
        Directory.CreateDirectory(pageDirectory);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "Templates"));
        Directory.CreateDirectory(appDataRoot);

        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var engine = new VisualPageMutationEngine(validator);
        var paragraph = catalog.CreateElement("core.paragraph");
        paragraph.Text = "Original visual text";
        var document = new VisualPageDocument
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            PageId = Guid.NewGuid().ToString("N"),
            Name = "AI verifier page",
            ComponentName = "AiVerifierPage",
            Route = "/ai-verifier",
            Slug = "ai-verifier",
            Root = catalog.CreateElement("core.page")
        };
        document.Root.Children.Add(paragraph);
        document.Revision = VisualPageHashing.ComputePageRevision(
            document,
            string.Empty);
        var pagePath = Path.Combine(pageDirectory, "ai-verifier.visual-page.json");
        await File.WriteAllTextAsync(
            pagePath,
            JsonSerializer.Serialize(document, VisualPageJson.Options),
            new UTF8Encoding(false));

        var auditPath = Path.Combine(appDataRoot, "ai.audit.jsonl");
        var audit = new WorkspaceAiAuditService(auditPath);
        var broker = new WorkspaceAiPermissionBroker(
            Path.Combine(appDataRoot, "permissions.json"),
            audit);
        const string capturedSiteCopy = "COMPETITOR_HEADLINE_MUST_NOT_PERSIST";
        const string capturedScript = "REMOTE_SCRIPT_MUST_NOT_PERSIST";
        const string capturedAttributeCopy =
            "SOURCE_ALT_TEXT_MUST_NOT_PERSIST";
        using var networkClient = new HttpClient(new StaticHtmlHandler($$"""
            <!doctype html>
            <html>
            <head>
              <style>.brand { color: red; }</style>
              <script>{{capturedScript}}</script>
            </head>
            <body onload="track()">
              <nav class="brand"><a href="https://tracker.example/path">Actual brand</a></nav>
              <main>
                <h1>{{capturedSiteCopy}}</h1>
                <p>Protected source-site paragraph.</p>
                <img src="https://cdn.example/brand.png" alt="{{capturedAttributeCopy}}" data-tracker="private" onerror="track()">
                <form action="https://collector.example/submit"><label for="email">Private label</label><input id="email" type="email"></form>
                <iframe src="https://embed.example/widget"></iframe>
              </main>
            </body>
            </html>
            """));
        var operationService = new WorkspaceAiOperationService(
            broker,
            audit,
            networkClient: networkClient);
        var highlighted = new List<string>();
        var buildCount = 0;
        VisualPageTemplateDocument? capturedTemplate = null;
        string capturedTemplatePath = string.Empty;

        WorkspaceAiActiveVisualPageHandle ActiveVisualPage() => new(
            "visual-page-session",
            workspaceRoot,
            Path.GetRelativePath(workspaceRoot, pagePath),
            document.ProjectId,
            document.PageId,
            document.Name,
            document.Route,
            document.Revision,
            () => JsonSerializer.Serialize(new
            {
                projectId = document.ProjectId,
                pageId = document.PageId,
                revision = document.Revision,
                document
            }, VisualPageJson.Options),
            () => validator.ValidatePage(document),
            mutation => engine.Apply(VisualPageJson.Clone(document), mutation),
            mutation =>
            {
                var result = engine.Apply(document, mutation);
                document = result.Document;
                return result;
            },
            _ =>
            {
                buildCount++;
                return Task.FromResult(new VisualPageBuildResult
                {
                    Outcome = VisualPageBuildOutcome.Succeeded,
                    Revision = document.Revision,
                    Output = "Synthetic isolated build succeeded."
                });
            },
            elementIds =>
            {
                highlighted.Clear();
                highlighted.AddRange(elementIds);
            },
            (html, request) =>
                new VisualPageWebTemplateCaptureService(catalog).Capture(
                    html,
                    request,
                    DateTimeOffset.UtcNow),
            async (template, relativePath, cancellationToken) =>
            {
                capturedTemplate = template;
                capturedTemplatePath = VisualPageProjectService.ResolveProjectPath(
                    workspaceRoot,
                    relativePath);
                await new VisualPageTemplateService(
                        catalog,
                        Path.Combine(appDataRoot, "personal-templates"))
                    .SaveAsync(
                        capturedTemplatePath,
                        template,
                        cancellationToken);
                return capturedTemplatePath;
            });

        var toolHost = new WorkspaceAiToolHost(
            broker,
            operationService,
            static (_, _) => null,
            static (_, _, _) => null,
            static (_, _) => null,
            (_, _) => ActiveVisualPage(),
            (_, _, sessionId) => sessionId == "visual-page-session"
                ? ActiveVisualPage()
                : null,
            static (_, _, _, _, _, _, _) => Task.FromResult(
                new WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>
                {
                    Status = WorkspaceAiOperationStatus.Failed,
                    Message = "No terminal dispatch is exposed by this verifier."
                }));

        Task<WorkspaceAiToolOutcome> ExecuteAsync(
            string id,
            string name,
            object arguments) => toolHost.ExecuteAsync(
                workspaceId,
                new WorkspaceAiFunctionCall(
                    id,
                    name,
                    JsonSerializer.Serialize(arguments)),
                "visual-page-ai-conversation",
                workspaceRoot);

        var deniedRead = await ExecuteAsync(
            "read-denied",
            "read_active_visual_page",
            new { });
        Require(!IsOk(deniedRead),
            "Visual-page AI read must be denied before Context approval.");

        var ownershipDenied = await ExecuteAsync(
            "capture-without-rights",
            "capture_web_template",
            new
            {
                url = "https://example.com/approved-layout",
                expected_revision = document.Revision,
                template_name = "Denied website starter",
                description = "Must not be captured without explicit permission.",
                scope = "page",
                category_group = "Marketing & conversion",
                category = "Landing pages",
                permission_confirmed = false,
                reason = "Verifier ownership boundary"
            });
        Require(!IsOk(ownershipDenied)
                && ownershipDenied.PendingReview is null,
            "Website template capture did not require explicit ownership or reuse permission.");

        broker.Approve(
            workspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Visual Page AI verifier",
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            allowAutomaticFileAndEditorChanges: false);

        var read = await ExecuteAsync(
            "read-approved",
            "read_active_visual_page",
            new { });
        Require(IsOk(read)
                && read.FunctionOutput.Contains(
                    document.Revision,
                    StringComparison.Ordinal)
                && read.FunctionOutput.Contains(
                    "no raw DOM",
                    StringComparison.OrdinalIgnoreCase),
            "Approved visual-page read did not return the bounded host model and security boundary.");

        var validation = await ExecuteAsync(
            "validate",
            "validate_visual_page",
            new { expected_revision = document.Revision });
        Require(IsOk(validation)
                && validation.FunctionOutput.Contains(
                    "diagnostics",
                    StringComparison.Ordinal),
            "The semantic visual-page validator tool did not run.");

        var truncatedMutation = await toolHost.ExecuteAsync(
            workspaceId,
            new WorkspaceAiFunctionCall(
                "truncated-full-page",
                "propose_visual_page_changes",
                $"{{\"expected_revision\":\"{document.Revision}\",\"reason\":\"full landing page\",\"operations\":[{{\"operationId\":\"hero\",\"kind\":\"InsertChild\",\"element\":{{\"text\":\"unfinished"),
            "visual-page-ai-conversation",
            workspaceRoot);
        Require(
            !IsOk(truncatedMutation)
            && truncatedMutation.FunctionOutput.Contains(
                WorkspaceAiToolHost.VisualPageArgumentRecoveryMarker,
                StringComparison.Ordinal)
            && truncatedMutation.FunctionOutput.Contains(
                "prepare_visual_page_markup",
                StringComparison.Ordinal),
            "A truncated full-page semantic call did not route the model to the prepared markup-transfer path.");

        var beforeMarkupImportCount = document.Root.Children.Count;
        var preparedMarkup = await ExecuteAsync(
            "prepare-portfolio-markup",
            "prepare_visual_page_markup",
            new
            {
                expected_revision = document.Revision,
                parent_element_id = document.Root.ElementId,
                markup_language = "html",
                reason = "Create a dark static portfolio landing page"
            });
        Require(
            preparedMarkup.PreparedVisualPageImport is not null
            && preparedMarkup.PendingReview is null
            && preparedMarkup.FunctionOutput.Contains(
                WorkspaceAiToolHost.VisualPageMarkupTransferRequestMarker,
                StringComparison.Ordinal)
            && document.Root.Children.Count == beforeMarkupImportCount,
            "The staged Hyperpage markup tool did not validate a small preflight without mutating the page.");

        const string portfolioMarkup = """
            <style>
            .portfolio-shell { background: #111827; color: #f8fafc; padding: 4rem; }
            .project-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; }
            </style>
            <main class="portfolio-shell">
              <header><h1>Portfolio Placeholder</h1><p>Apps made for curious peers.</p></header>
              <section><h2>About</h2><p>A concise placeholder biography.</p></section>
              <section class="project-grid"><h2>Selected Work</h2><article><h3>App One</h3><p>Project placeholder.</p></article></section>
              <section><h2>Contact</h2><a href="mailto:hello@example.com">hello@example.com</a></section>
              <footer>Portfolio footer</footer>
            </main>
            """;
        var importedMarkup = await toolHost.CompletePreparedVisualPageImportAsync(
            preparedMarkup.PreparedVisualPageImport!,
            portfolioMarkup,
            "visual-page-ai-conversation");
        Require(
            importedMarkup.PendingReview is
            {
                Kind: WorkspaceAiPendingActionKind.VisualPageMutation
            }
            && document.Root.Children.Count == beforeMarkupImportCount,
            "Transferred landing-page markup did not stop at the normal Hyperpage review boundary.");
        var approvedMarkup = await toolHost.ApproveAsync(
            importedMarkup.PendingReview!,
            "visual-page-ai-conversation");
        var importedElements = Flatten(document.Root).ToList();
        Require(
            IsOk(approvedMarkup)
            && document.Root.Children.Count == beforeMarkupImportCount + 1
            && importedElements.Any(element => element.Text.Contains(
                "Portfolio Placeholder",
                StringComparison.Ordinal))
            && importedElements.Any(element => element.Text.Contains(
                "Selected Work",
                StringComparison.Ordinal))
            && importedElements.Any(element => element.Text.Contains(
                "hello@example.com",
                StringComparison.Ordinal)),
            "Reviewed raw landing-page markup was not imported into the active semantic Hyperpage document.");

        const string coordinatorMarkup = """
            <section class="coordinator-transfer">
              <h2>Coordinator Transfer</h2>
              <p>This multi-section path keeps markup outside tool JSON.</p>
            </section>
            """;
        var coordinatorProvider = new ScriptedProvider(
            FunctionResponse(
                "coordinator-prepare-markup",
                "prepare_visual_page_markup",
                new
                {
                    expected_revision = document.Revision,
                    parent_element_id = document.Root.ElementId,
                    markup_language = "html",
                    reason = "Verify the complete staged Hyperpage path"
                }),
            TextResponse(coordinatorMarkup),
            TextResponse("The static Hyperpage markup was imported and reviewed."));
        var coordinator = new WorkspaceAiConversationCoordinator(
            coordinatorProvider,
            new WorkspaceAiProviderConfigurationService(
                Path.Combine(appDataRoot, "coordinator-provider.json")),
            new DpapiWorkspaceAiCredentialStore(
                Path.Combine(appDataRoot, "coordinator-credential.bin")),
            toolHost);
        coordinator.SetWorkingDirectory(workspaceRoot);
        coordinator.SetHyperpageProjectWorkflow(enabled: true);
        var coordinatorPending = await coordinator.SendAsync(
            workspaceId,
            "Build the agreed static landing-page foundation in the active Hyperpage.");
        Require(
            coordinatorPending.PendingReview is
            {
                Kind: WorkspaceAiPendingActionKind.VisualPageMutation
            }
            && coordinatorProvider.Inputs.Count == 2
            && coordinatorProvider.Inputs[1].Last().GetProperty("output")
                .GetString()!.Contains(
                    WorkspaceAiToolHost.VisualPageMarkupTransferRequestMarker,
                    StringComparison.Ordinal)
            && !Flatten(document.Root).Any(element => element.Text.Contains(
                "Coordinator Transfer",
                StringComparison.Ordinal)),
            "The conversation coordinator did not isolate raw landing-page markup from tool JSON or stop at review.");
        var coordinatorCompleted = await coordinator.ApproveAsync();
        Require(
            coordinatorCompleted.PendingReview is null
            && coordinatorCompleted.AssistantText.Contains(
                "imported and reviewed",
                StringComparison.Ordinal)
            && Flatten(document.Root).Any(element => element.Text.Contains(
                "Coordinator Transfer",
                StringComparison.Ordinal)),
            "The reviewed staged-markup conversation did not resume after applying the active Hyperpage change.");

        const string secretText = "TOP_SECRET_CANVAS_TEXT";
        var reviewedRevision = document.Revision;
        var proposal = await ExecuteAsync(
            "propose-reviewed",
            "propose_visual_page_changes",
            new
            {
                expected_revision = reviewedRevision,
                reason = "Verifier reviewed mutation",
                operations = new[]
                {
                    new
                    {
                        operationId = "set-text-reviewed",
                        kind = "SetText",
                        targetElementId = paragraph.ElementId,
                        value = secretText
                    }
                }
            });
        Require(proposal.PendingReview is
        {
            Kind: WorkspaceAiPendingActionKind.VisualPageMutation
        }
                && document.Root.Children[0].Text == "Original visual text"
                && highlighted.Contains(paragraph.ElementId),
            "Default visual-page mutation policy did not preflight, highlight, and pause without changing state.");

        var applied = await toolHost.ApproveAsync(
            proposal.PendingReview!,
            "visual-page-ai-conversation");
        Require(IsOk(applied)
                && document.Root.Children[0].Text == secretText
                && document.Revision != reviewedRevision,
            "Reviewed semantic visual-page mutation was not applied through the stable mutation protocol.");

        var stale = await ExecuteAsync(
            "propose-stale",
            "propose_visual_page_changes",
            new
            {
                expected_revision = reviewedRevision,
                reason = "Stale mutation",
                operations = new[]
                {
                    new
                    {
                        operationId = "stale-set-text",
                        kind = "SetText",
                        targetElementId = paragraph.ElementId,
                        value = "must not apply"
                    }
                }
            });
        Require(!IsOk(stale)
                && stale.PendingReview is null
                && document.Root.Children[0].Text == secretText,
            "A stale expected visual-page revision was not rejected before review.");

        broker.Approve(
            workspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Visual Page AI verifier auto-apply",
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            allowAutomaticFileAndEditorChanges: true);
        var autoApplied = await ExecuteAsync(
            "propose-auto",
            "propose_visual_page_changes",
            new
            {
                expected_revision = document.Revision,
                reason = "Verifier standing-policy mutation",
                operations = new[]
                {
                    new
                    {
                        operationId = "auto-set-text",
                        kind = "SetText",
                        targetElementId = paragraph.ElementId,
                        value = "Automatically applied text"
                    }
                }
            });
        Require(autoApplied.PendingReview is null
                && IsOk(autoApplied)
                && document.Root.Children[0].Text
                    == "Automatically applied text",
            "The explicit per-workspace auto-apply policy was not honored.");

        broker.Approve(
            workspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Visual Page AI verifier reviewed",
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            allowAutomaticFileAndEditorChanges: false);
        var customElementId = Guid.NewGuid().ToString("N");
        var customProposal = await ExecuteAsync(
            "propose-custom",
            "propose_visual_page_changes",
            new
            {
                expected_revision = document.Revision,
                reason = "Verifier custom Razor classification",
                operations = new[]
                {
                    new
                    {
                        operationId = "insert-custom",
                        kind = "InsertChild",
                        parentElementId = document.Root.ElementId,
                        element = new
                        {
                            elementId = customElementId,
                            descriptorId = "custom.razor",
                            name = "Reviewed custom fragment",
                            text = string.Empty,
                            isLocked = false,
                            isHidden = false,
                            customRazor = "<strong>Reviewed code</strong>"
                        }
                    }
                }
            });
        Require(customProposal.PendingReview is
        {
            Kind: WorkspaceAiPendingActionKind.VisualPageMutation
        } customReview
                && customReview.Title.Contains(
                    "custom-code",
                    StringComparison.OrdinalIgnoreCase),
            "Custom Razor was not classified into the reviewed-code lane.");
        _ = toolHost.Reject(
            customProposal.PendingReview!,
            "visual-page-ai-conversation");
        Require(document.Root.Children.All(child =>
                child.ElementId != customElementId),
            "Rejecting a custom-code proposal still changed the page.");

        broker.Approve(
            workspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            allowNetwork: true,
            displayName: "Visual Page AI website capture verifier",
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            allowAutomaticFileAndEditorChanges: true);
        var websiteCapture = await ExecuteAsync(
            "capture-reviewed",
            "capture_web_template",
            new
            {
                url = "https://example.com/approved-layout?tracking=discarded",
                expected_revision = document.Revision,
                template_name = "Approved website starter",
                description = "A reviewed structure-only template.",
                scope = "page",
                category_group = "Marketing & conversion",
                category = "Landing pages",
                permission_confirmed = true,
                reason = "Verify bounded structure capture"
            });
        Require(websiteCapture.PendingReview is
        {
            Kind: WorkspaceAiPendingActionKind.VisualPageTemplateCapture
        } websiteReview
                && websiteReview.Preview.Contains(
                    VisualPageWebTemplateCaptureService.StructureOnlyPolicy,
                    StringComparison.Ordinal)
                && capturedTemplate is null,
            "Website capture did not sanitize into a proposal and pause for review even under auto-apply policy.");
        var websiteCreated = await toolHost.ApproveAsync(
            websiteCapture.PendingReview!,
            "visual-page-ai-conversation");
        var capturedJson = JsonSerializer.Serialize(
            capturedTemplate,
            VisualPageJson.Options);
        Require(IsOk(websiteCreated)
                && capturedTemplate is
                {
                    Scope: "page",
                    SourceKind: "website-structure-capture"
                }
                && capturedTemplate.SourceUrl ==
                    "https://example.com/approved-layout"
                && File.Exists(capturedTemplatePath)
                && !capturedJson.Contains(capturedSiteCopy, StringComparison.Ordinal)
                && !capturedJson.Contains(capturedScript, StringComparison.Ordinal)
                && !capturedJson.Contains(capturedAttributeCopy, StringComparison.Ordinal)
                && !capturedJson.Contains("tracker.example", StringComparison.Ordinal)
                && !capturedJson.Contains("cdn.example", StringComparison.Ordinal)
                && !capturedJson.Contains("collector.example", StringComparison.Ordinal)
                && !capturedJson.Contains("custom.razor", StringComparison.Ordinal)
                && capturedJson.Contains("assets/placeholder.svg", StringComparison.Ordinal),
            "Reviewed website capture retained source copy, executable/remote content, or failed to create the project template.");

        var buildProposal = await ExecuteAsync(
            "build-preview",
            "build_visual_page_preview",
            new { expected_revision = document.Revision });
        Require(buildProposal.PendingReview is
        {
            Kind: WorkspaceAiPendingActionKind.VisualPagePreviewBuild,
            RequiresNativeConfirmation: true,
            ExecutesCommand: true
        }
                && buildCount == 0,
            "AI preview build did not pause behind the explicit ambient-code trust review.");
        var built = await toolHost.ApproveAsync(
            buildProposal.PendingReview!,
            "visual-page-ai-conversation",
            userConfirmedExecution: true);
        Require(IsOk(built)
                && buildCount == 1
                && built.FunctionOutput.Contains(
                    "\"hosted\":false",
                    StringComparison.Ordinal)
                && built.FunctionOutput.Contains(
                    "\"published\":false",
                    StringComparison.Ordinal),
            "Reviewed preview compilation did not remain isolated from host/publish capability.");

        var forbiddenPublish = await ExecuteAsync(
            "forbidden-publish",
            "publish_visual_page",
            new { });
        Require(!IsOk(forbiddenPublish),
            "The AI host unexpectedly exposed a visual-page publication tool.");

        var auditText = await File.ReadAllTextAsync(auditPath);
        Require(!auditText.Contains(secretText, StringComparison.Ordinal)
                && !auditText.Contains(capturedSiteCopy, StringComparison.Ordinal)
                && !auditText.Contains(capturedScript, StringComparison.Ordinal)
                && !auditText.Contains(capturedAttributeCopy, StringComparison.Ordinal)
                && auditText.Contains(
                    nameof(WorkspaceAiOperationKind.ReadVisualPage),
                    StringComparison.Ordinal)
                && auditText.Contains(
                    nameof(WorkspaceAiOperationKind.ProposeVisualPageMutation),
                    StringComparison.Ordinal)
                && auditText.Contains(
                    nameof(WorkspaceAiOperationKind.MutateVisualPage),
                    StringComparison.Ordinal)
                && auditText.Contains(
                    nameof(WorkspaceAiOperationKind.ProposeVisualPageTemplate),
                    StringComparison.Ordinal)
                && auditText.Contains(
                    nameof(WorkspaceAiOperationKind.CreateVisualPageTemplate),
                    StringComparison.Ordinal)
                && auditText.Contains(
                    nameof(WorkspaceAiOperationKind.BuildVisualPagePreview),
                    StringComparison.Ordinal),
            "Visual-page AI audit metadata was incomplete or leaked proposed page content.");

        Console.WriteLine(
            "VISUAL_PAGE_AI_OK|default-denial|reviewed-apply|optional-auto-apply|stale-revision|custom-code-lane|prepared-markup-transfer|truncated-operation-recovery|static-landing-page|coordinator-markup-transfer|website-template-rights|structure-only-capture|always-reviewed-template|content-free-audit|preview-only|no-publish");
    }

    private static IEnumerable<VisualPageElement> Flatten(
        VisualPageElement element)
    {
        yield return element;
        foreach (var child in element.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static WorkspaceAiProviderResponse FunctionResponse(
        string callId,
        string name,
        object arguments)
    {
        var argumentsJson = JsonSerializer.Serialize(arguments);
        return new WorkspaceAiProviderResponse(
            [JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = callId,
                name,
                arguments = argumentsJson
            })],
            string.Empty,
            [new WorkspaceAiFunctionCall(callId, name, argumentsJson)],
            []);
    }

    private static WorkspaceAiProviderResponse TextResponse(string text) =>
        new([], text, [], []);

    private static bool IsOk(WorkspaceAiToolOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome.FunctionOutput))
        {
            return false;
        }
        using var json = JsonDocument.Parse(outcome.FunctionOutput);
        return json.RootElement.TryGetProperty("ok", out var ok)
               && ok.ValueKind == JsonValueKind.True;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StaticHtmlHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    html,
                    Encoding.UTF8,
                    "text/html")
            });
    }

    private sealed class ScriptedProvider(
        params WorkspaceAiProviderResponse[] responses)
        : IWorkspaceAiProvider
    {
        private readonly Queue<WorkspaceAiProviderResponse> _responses =
            new(responses);

        public List<IReadOnlyList<JsonElement>> Inputs { get; } = [];

        public Task<WorkspaceAiProviderResponse> CreateResponseAsync(
            WorkspaceAiProviderConfiguration configuration,
            string apiKey,
            IReadOnlyList<JsonElement> input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(input.Select(item => item.Clone()).ToArray());
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "The visual-page scripted provider ran out of responses.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
