using Hypershell.Models;
using Hypershell.Profiles;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Hypershell.Services;

public enum WorkspaceAiPendingActionKind
{
    WorkspaceTextWrite,
    WorkspaceTextDelete,
    OpenDocumentReplace,
    VisualPageMutation,
    VisualPageTemplateCapture,
    VisualPagePreviewBuild,
    TerminalCommand,
    GitOperation
}

public sealed class WorkspaceAiPendingReview
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public required string WorkspaceId { get; init; }
    public required WorkspaceAiPendingActionKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Target { get; init; }
    public required string Preview { get; init; }
    public bool ExecutesCommand { get; init; }
    public bool RequiresNativeConfirmation { get; init; }
    internal bool ContinueWithHostMessage { get; init; }
    internal WorkspaceAiPendingPayload Payload { get; init; } = null!;
}

public sealed record WorkspaceAiActiveDocumentHandle(
    string SessionId,
    string Title,
    string RelativePath,
    string LanguageId,
    string ProviderId,
    bool HasUnsavedChanges,
    Func<string> ReadContent,
    Func<DocumentReadResult> Analyze,
    Func<string, DocumentMutationResult> ReplaceContent);

public sealed record WorkspaceAiActiveVisualPageHandle(
    string SessionId,
    string ProjectRoot,
    string PageDocumentPath,
    string ProjectId,
    string PageId,
    string PageName,
    string Route,
    string Revision,
    Func<string> ReadSnapshot,
    Func<IReadOnlyList<VisualPageDiagnostic>> Validate,
    Func<VisualPageMutationSet, VisualPageMutationResult> PreviewMutation,
    Func<VisualPageMutationSet, VisualPageMutationResult> ApplyMutation,
    Func<CancellationToken, Task<VisualPageBuildResult>> BuildPreview,
    Action<IReadOnlyList<string>> HighlightElements,
    Func<string, VisualPageWebTemplateCaptureRequest,
        VisualPageWebTemplateCaptureResult> CaptureWebTemplate,
    Func<VisualPageTemplateDocument, string, CancellationToken, Task<string>>
        SaveProjectTemplate);

public sealed record WorkspaceAiActiveTerminalHandle(
    string SessionId,
    string Title,
    string ShellKind,
    string WorkingDirectory,
    string WorkspaceUri,
    string RemoteHostId,
    string Prompt,
    bool IsRunning,
    Func<string> ReadDraftCommand,
    Func<IReadOnlyList<WorkspaceAiTerminalLineSnapshot>> ReadTranscript);

public enum WorkspaceAiSourceTransferKind
{
    FullFile,
    ExactEdit
}

public sealed record WorkspaceAiPreparedWorkspaceWrite(
    string CallId,
    string ToolName,
    string WorkspaceId,
    string Target,
    string? ExpectedSha256,
    bool AllowCreate,
    WorkspaceAiSourceTransferKind TransferKind =
        WorkspaceAiSourceTransferKind.FullFile,
    string CurrentContent = "");

public sealed record WorkspaceAiPreparedVisualPageImport(
    string CallId,
    string ToolName,
    string WorkspaceId,
    string SessionId,
    string WorkingDirectory,
    string ProjectId,
    string PageId,
    string ExpectedRevision,
    string ParentElementId,
    bool Razor,
    string Reason);

public sealed record WorkspaceAiToolOutcome(
    string FunctionOutput,
    WorkspaceAiPendingReview? PendingReview = null,
    WorkspaceAiPreparedWorkspaceWrite? PreparedWorkspaceWrite = null,
    WorkspaceAiPreparedVisualPageImport? PreparedVisualPageImport = null);

public sealed record WorkspaceAiConversationTurnResult(
    string AssistantText,
    string StatusMessage,
    WorkspaceAiPendingReview? PendingReview = null,
    string DiagnosticsPath = "");

internal abstract record WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiWorkspaceWritePayload(
    string RelativePath,
    string Content,
    string? ExpectedSha256,
    bool AllowCreate) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiOpenDocumentReplacePayload(
    string SessionId,
    string RelativePath,
    string Content,
    string ExpectedSha256,
    string WorkingDirectory) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiWorkspaceDeletePayload(
    string Path,
    string ExpectedSha256) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiTerminalCommandPayload(
    string TerminalSessionId,
    string TerminalWorkspaceUri,
    string Command,
    bool Execute,
    string WorkingDirectory = "",
    WorkspaceAiProjectCheckPlan? ProjectCheck = null) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiGitOperationPayload(
    WorkspaceAiGitMutationRequest Request) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiVisualPageMutationPayload(
    string SessionId,
    string WorkingDirectory,
    string ProjectId,
    string PageId,
    VisualPageMutationSet MutationSet) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiVisualPagePreviewBuildPayload(
    string SessionId,
    string WorkingDirectory,
    string ProjectId,
    string PageId,
    string ExpectedRevision) : WorkspaceAiPendingPayload;

internal sealed record WorkspaceAiVisualPageTemplateCapturePayload(
    string SessionId,
    string WorkingDirectory,
    string ProjectId,
    string PageId,
    string ExpectedRevision,
    string TemplatePath,
    string RelativePath,
    VisualPageTemplateDocument Template) : WorkspaceAiPendingPayload;

public delegate Task<
    WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>>
    WorkspaceAiTerminalDispatcher(
        string workspaceId,
        string terminalSessionId,
        string terminalWorkspaceUri,
        string command,
        bool execute,
        bool userConfirmed,
        CancellationToken cancellationToken);

public sealed class WorkspaceAiToolHost
{
    public const int MaximumProviderTextCharacters = 128 * 1024;
    public const int MaximumProposedTextCharacters = 256 * 1024;
    public const string SourceTransferRequestMarker =
        "[HYPERSHELL_SOURCE_TRANSFER_REQUEST_V1]";
    public const string ExactEditTransferRequestMarker =
        "[HYPERSHELL_EXACT_EDIT_TRANSFER_REQUEST_V1]";
    public const string VisualPageMarkupTransferRequestMarker =
        "[HYPERSHELL_VISUAL_PAGE_MARKUP_TRANSFER_REQUEST_V1]";
    public const string VisualPageArgumentRecoveryMarker =
        "[HYPERSHELL_VISUAL_PAGE_ARGUMENT_RECOVERY_V1]";
    public const string SourceTransferBeginMarker =
        "[HYPERSHELL_SOURCE_FILE_V1]";
    public const string SourceTransferEndMarker =
        "[/HYPERSHELL_SOURCE_FILE_V1]";

    private readonly WorkspaceAiPermissionBroker _permissionBroker;
    private readonly WorkspaceAiOperationService _operations;
    private readonly Func<string, string, WorkspaceAiActiveDocumentHandle?>
        _getActiveDocument;
    private readonly Func<string, string, string,
        WorkspaceAiActiveDocumentHandle?>
        _getDocumentBySessionId;
    private readonly Func<string, string, WorkspaceAiActiveTerminalHandle?>
        _getActiveTerminal;
    private readonly Func<string, string, WorkspaceAiActiveVisualPageHandle?>
        _getActiveVisualPage;
    private readonly Func<string, string, string,
        WorkspaceAiActiveVisualPageHandle?> _getVisualPageBySessionId;
    private readonly WorkspaceAiTerminalDispatcher _dispatchTerminal;
    private readonly WorkspaceAiGitService? _gitTools;
    private readonly IHyperlibSemanticSearchService? _hyperlibSearch;
    private readonly VisualPageImportService _visualPageImportService =
        new(new VisualPageComponentCatalog());

    public WorkspaceAiToolHost(
        WorkspaceAiPermissionBroker permissionBroker,
        WorkspaceAiOperationService operations,
        Func<string, string, WorkspaceAiActiveDocumentHandle?>
            getActiveDocument,
        Func<string, string, string, WorkspaceAiActiveDocumentHandle?>
            getDocumentBySessionId,
        Func<string, string, WorkspaceAiActiveTerminalHandle?>
            getActiveTerminal,
        WorkspaceAiTerminalDispatcher dispatchTerminal,
        WorkspaceAiGitService? gitTools = null,
        IHyperlibSemanticSearchService? hyperlibSearch = null)
        : this(
            permissionBroker,
            operations,
            getActiveDocument,
            getDocumentBySessionId,
            getActiveTerminal,
            static (_, _) => null,
            static (_, _, _) => null,
            dispatchTerminal,
            gitTools,
            hyperlibSearch)
    {
    }

    public WorkspaceAiToolHost(
        WorkspaceAiPermissionBroker permissionBroker,
        WorkspaceAiOperationService operations,
        Func<string, string, WorkspaceAiActiveDocumentHandle?>
            getActiveDocument,
        Func<string, string, string, WorkspaceAiActiveDocumentHandle?>
            getDocumentBySessionId,
        Func<string, string, WorkspaceAiActiveTerminalHandle?>
            getActiveTerminal,
        Func<string, string, WorkspaceAiActiveVisualPageHandle?>
            getActiveVisualPage,
        Func<string, string, string, WorkspaceAiActiveVisualPageHandle?>
            getVisualPageBySessionId,
        WorkspaceAiTerminalDispatcher dispatchTerminal,
        WorkspaceAiGitService? gitTools = null,
        IHyperlibSemanticSearchService? hyperlibSearch = null)
    {
        _permissionBroker = permissionBroker;
        _operations = operations;
        _getActiveDocument = getActiveDocument;
        _getDocumentBySessionId = getDocumentBySessionId;
        _getActiveTerminal = getActiveTerminal;
        _getActiveVisualPage = getActiveVisualPage;
        _getVisualPageBySessionId = getVisualPageBySessionId;
        _dispatchTerminal = dispatchTerminal;
        _gitTools = gitTools;
        _hyperlibSearch = hyperlibSearch;
    }

    public Task<WorkspaceAiToolOutcome> ExecuteAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            workspaceId,
            call,
            string.Empty,
            string.Empty,
            cancellationToken);

    public Task<WorkspaceAiToolOutcome> ExecuteAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        string conversationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            workspaceId,
            call,
            conversationId,
            string.Empty,
            cancellationToken);

    public async Task<WorkspaceAiToolOutcome> ExecuteAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        string conversationId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(call);
        using var auditScope = _operations.BeginAuditScope(conversationId);
        try
        {
            using var arguments = ParseArguments(call.Arguments);
            return call.Name switch
            {
                "search_hyperlib" => await SearchHyperlibAsync(
                    arguments.RootElement,
                    cancellationToken),
                "list_workspace" => await ListWorkspaceAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "search_workspace_text" => await SearchWorkspaceTextAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "read_workspace_text" => await ReadWorkspaceTextAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "read_active_document" => await ReadActiveDocumentAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "read_active_visual_page" => await ReadActiveVisualPageAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "validate_visual_page" => await ValidateVisualPageAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "propose_visual_page_changes" =>
                    await ProposeVisualPageChangesAsync(
                        workspaceId,
                        call,
                        arguments.RootElement,
                        workingDirectory,
                        cancellationToken),
                "prepare_visual_page_markup" =>
                    PrepareVisualPageMarkup(
                        workspaceId,
                        call,
                        arguments.RootElement,
                        workingDirectory),
                "capture_web_template" =>
                    await ProposeWebTemplateCaptureAsync(
                        workspaceId,
                        call,
                        arguments.RootElement,
                        workingDirectory,
                        cancellationToken),
                "build_visual_page_preview" =>
                    await ProposeVisualPagePreviewBuildAsync(
                        workspaceId,
                        call,
                        arguments.RootElement,
                        workingDirectory),
                "read_active_terminal" => await ReadActiveTerminalAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "propose_workspace_write" => await ProposeWorkspaceWriteAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "prepare_workspace_write" => await PrepareWorkspaceWriteAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "prepare_workspace_edit" => await PrepareWorkspaceEditAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "propose_workspace_delete" => await ProposeWorkspaceDeleteAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "propose_active_document_replace" =>
                    await ProposeActiveDocumentReplaceAsync(
                        workspaceId,
                        call,
                        arguments.RootElement,
                        workingDirectory,
                        cancellationToken),
                "execute_terminal_command" => await HandleTerminalCommandAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    execute: true,
                    workingDirectory: workingDirectory,
                    cancellationToken: cancellationToken),
                "stage_terminal_command" => await HandleTerminalCommandAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    execute: false,
                    workingDirectory: workingDirectory,
                    cancellationToken: cancellationToken),
                "run_project_check" => await RunProjectCheckAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    workingDirectory,
                    cancellationToken),
                "fetch_url_text" => await FetchUrlTextAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    cancellationToken),
                "inspect_git_repository" => await InspectGitRepositoryAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    conversationId,
                    workingDirectory,
                    cancellationToken),
                "propose_git_operation" => await ProposeGitOperationAsync(
                    workspaceId,
                    call,
                    arguments.RootElement,
                    conversationId,
                    workingDirectory,
                    cancellationToken),
                _ => Error(
                    $"The host does not expose a tool named '{call.Name}'.")
            };
        }
        catch (WorkspaceAiToolArgumentException exception)
        {
            return Error(exception.Message);
        }
        catch (JsonException exception)
        {
            if (string.Equals(
                    call.Name,
                    "propose_visual_page_changes",
                    StringComparison.Ordinal))
            {
                return Error(
                    $"{VisualPageArgumentRecoveryMarker}\nThe semantic page-operation JSON was incomplete or malformed, so nothing changed. Use prepare_visual_page_markup for this multi-section page and transfer the HTML or Razor outside tool arguments. Parser detail: {exception.Message}");
            }

            return Error(
                $"The tool arguments were not valid bounded JSON: {exception.Message}");
        }
    }

    private async Task<WorkspaceAiToolOutcome> SearchHyperlibAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "query", "limit");
        if (_hyperlibSearch is null)
        {
            return Error(
                "The Hyperlib semantic search service is not available in this host.");
        }

        var query = RequiredString(
            arguments,
            "query",
            maximumLength: 1000,
            allowLineBreaks: true);
        var limit = RequiredInteger(arguments, "limit", 1, 20);
        try
        {
            var results = await _hyperlibSearch.SearchAsync(
                query,
                limit,
                cancellationToken);
            return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
            {
                ok = true,
                query,
                count = results.Count,
                results = results.Select(result => new
                {
                    entry_id = result.EntryId,
                    library_id = result.LibraryId,
                    library = result.LibraryName,
                    name = result.Name,
                    description = result.Description,
                    template = result.Template,
                    language = result.Language,
                    execution_target = result.ExecutionTarget,
                    coverage_tier = result.CoverageTier,
                    library_origin = result.LibraryOrigin,
                    provenance = result.Provenance,
                    content_origin = result.ContentOrigin,
                    review_state = result.ReviewState,
                    relevance = result.Score
                })
            }));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
                                         WorkspaceAiProviderException
                                         or InvalidDataException
                                         or IOException
                                         or ArgumentException)
        {
            return Error(
                $"Hyperlib semantic search is unavailable: {exception.Message}");
        }
    }

    public Task<WorkspaceAiToolOutcome> ApproveAsync(
        WorkspaceAiPendingReview review,
        bool userConfirmedExecution = false,
        CancellationToken cancellationToken = default) =>
        ApproveAsync(
            review,
            string.Empty,
            userConfirmedExecution,
            cancellationToken);

    public async Task<WorkspaceAiToolOutcome> ApproveAsync(
        WorkspaceAiPendingReview review,
        string conversationId,
        bool userConfirmedExecution = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        using var auditScope = _operations.BeginAuditScope(conversationId);
        return review.Payload switch
        {
            WorkspaceAiWorkspaceWritePayload write =>
                FromOperation(await _operations.WriteTextAsync(
                    review.WorkspaceId,
                    write.RelativePath,
                    write.Content,
                    write.ExpectedSha256,
                    write.AllowCreate,
                    cancellationToken)),
            WorkspaceAiOpenDocumentReplacePayload document =>
                await ApproveOpenDocumentReplaceAsync(
                    review,
                    document,
                    cancellationToken),
            WorkspaceAiVisualPageMutationPayload visualPage =>
                await ApproveVisualPageMutationAsync(
                    review,
                    visualPage,
                    cancellationToken),
            WorkspaceAiVisualPageTemplateCapturePayload template =>
                await ApproveVisualPageTemplateCaptureAsync(
                    review,
                    template,
                    cancellationToken),
            WorkspaceAiVisualPagePreviewBuildPayload preview =>
                await ApproveVisualPagePreviewBuildAsync(
                    review,
                    preview,
                    userConfirmedExecution,
                    cancellationToken),
            WorkspaceAiWorkspaceDeletePayload delete =>
                FromOperation(await _operations.DeleteTextAsync(
                    review.WorkspaceId,
                    delete.Path,
                    delete.ExpectedSha256,
                    cancellationToken)),
            WorkspaceAiTerminalCommandPayload terminal =>
                await DispatchTerminalWithTranscriptAsync(
                    review.WorkspaceId,
                    terminal,
                    userConfirmedExecution,
                    cancellationToken),
            WorkspaceAiGitOperationPayload git when _gitTools is not null =>
                FromGitMutation(await _gitTools.ExecuteMutationAsync(
                    review.WorkspaceId,
                    git.Request,
                    conversationId,
                    cancellationToken)),
            _ => Error("The pending AI action type is not supported.")
        };
    }

    public async Task<WorkspaceAiToolOutcome>
        CompletePreparedWorkspaceWriteAsync(
            WorkspaceAiPreparedWorkspaceWrite prepared,
            string content,
            string conversationId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(content);
        using var auditScope = _operations.BeginAuditScope(conversationId);
        if (content.Length > MaximumProposedTextCharacters
            || content.IndexOf('\0') >= 0
            || content.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            return Error(
                "The transferred source content is malformed or exceeds the 256 KiB limit.");
        }

        var proposedContent = content;
        if (prepared.TransferKind == WorkspaceAiSourceTransferKind.ExactEdit)
        {
            var edit = WorkspaceAiExactEdit.Apply(
                prepared.CurrentContent,
                content);
            if (!edit.Succeeded)
            {
                return Error(edit.Message);
            }

            proposedContent = edit.Content;
        }

        return await CompleteWorkspaceWriteAsync(
            prepared.WorkspaceId,
            prepared.CallId,
            prepared.ToolName,
            prepared.Target,
            proposedContent,
            prepared.ExpectedSha256,
            prepared.AllowCreate,
            continueWithHostMessage: true,
            cancellationToken);
    }

    public async Task<WorkspaceAiToolOutcome>
        CompletePreparedVisualPageImportAsync(
            WorkspaceAiPreparedVisualPageImport prepared,
            string markup,
            string conversationId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(markup);
        using var auditScope = _operations.BeginAuditScope(conversationId);
        if (string.IsNullOrWhiteSpace(markup)
            || markup.Length > MaximumProposedTextCharacters
            || markup.IndexOf('\0') >= 0
            || markup.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            return Error(
                "The transferred Hyperpage markup is empty, malformed, or exceeds the 256 KiB limit.");
        }

        var handle = _getVisualPageBySessionId(
            prepared.WorkspaceId,
            prepared.WorkingDirectory,
            prepared.SessionId);
        if (handle is null
            || !string.Equals(
                handle.ProjectId,
                prepared.ProjectId,
                StringComparison.Ordinal)
            || !string.Equals(
                handle.PageId,
                prepared.PageId,
                StringComparison.Ordinal))
        {
            return Error(
                "The prepared Hyperpage document is no longer available in this Context.");
        }
        if (!string.Equals(
                handle.Revision,
                prepared.ExpectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The active Hyperpage document changed during markup transfer. Read it again before retrying.");
        }

        VisualPageImportResult imported;
        try
        {
            imported = _visualPageImportService.Import(markup, prepared.Razor);
        }
        catch (VisualPageValidationException exception)
        {
            return Error($"The transferred Hyperpage markup could not be imported: {exception.Message}");
        }

        var operation = new VisualPageMutationOperation
        {
            OperationId = $"import-{Guid.NewGuid():N}",
            Kind = VisualPageMutationKind.InsertChild,
            ParentElementId = prepared.ParentElementId,
            Element = imported.Root
        };
        return await CompleteVisualPageChangesAsync(
            prepared.WorkspaceId,
            prepared.CallId,
            prepared.ToolName,
            prepared.ExpectedRevision,
            prepared.Reason,
            [operation],
            prepared.WorkingDirectory,
            handle,
            continueWithHostMessage: true,
            cancellationToken);
    }

    public WorkspaceAiToolOutcome Reject(WorkspaceAiPendingReview review) =>
        Reject(review, string.Empty);

    public WorkspaceAiToolOutcome Reject(
        WorkspaceAiPendingReview review,
        string conversationId)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (review.Payload is WorkspaceAiGitOperationPayload git
            && _gitTools is not null)
        {
            _gitTools.RecordRejected(
                git.Request,
                review.WorkspaceId,
                conversationId);
        }

        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = false,
            status = "rejected_by_user",
            message = "The user rejected the proposed action. No mutation was performed.",
            target = review.Target
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ListWorkspaceAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "relative_path");
        var relativePath = ResolveProjectPath(
            workingDirectory,
            RequiredString(
            arguments,
            "relative_path",
            maximumLength: 1024));
        var result = await _operations.ListDirectoryAsync(
            workspaceId,
            relativePath,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            relative_path = result.Value.RelativePath,
            is_truncated = result.Value.IsTruncated,
            entries = result.Value.Entries.Select(entry => new
            {
                name = entry.Name,
                relative_path = entry.RelativePath,
                kind = entry.IsDirectory ? "directory" : "file",
                size = entry.Size,
                last_write_utc = entry.LastWriteTimeUtc
            })
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ReadWorkspaceTextAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "relative_path",
            "start_line",
            "line_count");
        var relativePath = ResolveProjectPath(
            workingDirectory,
            RequiredString(
            arguments,
            "relative_path",
            maximumLength: 1024));
        var startLine = RequiredInteger(
            arguments,
            "start_line",
            minimum: 1,
            maximum: 10_000_000);
        var lineCount = RequiredInteger(
            arguments,
            "line_count",
            minimum: 1,
            maximum: 400);
        var result = await _operations.ReadTextAsync(
            workspaceId,
            relativePath,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        var slice = CreateLineSlice(
            result.Value.Content,
            startLine,
            lineCount);
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            relative_path = result.Value.RelativePath,
            language_id = result.Value.LanguageId,
            sha256 = result.Value.Sha256,
            byte_length = result.Value.ByteLength,
            total_lines = slice.TotalLines,
            start_line = startLine,
            returned_lines = slice.ReturnedLines,
            content_truncated = slice.IsTruncated,
            content = slice.Content
        }));
    }

    private async Task<WorkspaceAiToolOutcome> SearchWorkspaceTextAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "relative_path",
            "query",
            "file_pattern",
            "use_regex",
            "case_sensitive");
        var root = ResolveProjectPath(
            workingDirectory,
            RequiredString(
                arguments,
                "relative_path",
                maximumLength: 1024));
        var query = RequiredString(
            arguments,
            "query",
            maximumLength: 512,
            allowLineBreaks: true);
        var filePattern = RequiredString(
            arguments,
            "file_pattern",
            maximumLength: 2048);
        var useRegex = RequiredBoolean(arguments, "use_regex");
        var caseSensitive = RequiredBoolean(arguments, "case_sensitive");
        var result = await _operations.SearchTextAsync(
            workspaceId,
            root,
            query,
            filePattern,
            useRegex,
            caseSensitive,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            root = result.Value.Root,
            query = result.Value.Query,
            file_pattern = result.Value.FilePattern,
            files_scanned = result.Value.FilesScanned,
            files_skipped = result.Value.FilesSkipped,
            bytes_scanned = result.Value.BytesScanned,
            is_truncated = result.Value.IsTruncated,
            match_count = result.Value.Matches.Count,
            matches = result.Value.Matches.Select(match => new
            {
                path = match.Path,
                display_path = match.DisplayPath,
                line = match.Line,
                column = match.Column,
                preview = match.Preview
            })
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ReadActiveDocumentAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments);
        var handle = _getActiveDocument(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active typed document allowed by the selected Context.");
        }

        var result = await _operations.ReadOpenDocumentAsync(
            workspaceId,
            handle.RelativePath,
            handle.SessionId,
            handle.Title,
            handle.LanguageId,
            handle.ProviderId,
            handle.HasUnsavedChanges,
            handle.ReadContent,
            handle.Analyze,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        var bounded = BoundText(result.Value.Content);
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            session_id = result.Value.SessionId,
            title = result.Value.Title,
            relative_path = result.Value.RelativePath,
            language_id = result.Value.LanguageId,
            provider_id = result.Value.ProviderId,
            provider_summary = result.Value.ProviderSummary,
            diagnostics = result.Value.Diagnostics.Select(diagnostic => new
            {
                severity = diagnostic.Severity.ToString(),
                code = diagnostic.Code,
                message = diagnostic.Message,
                offset = diagnostic.Offset,
                length = diagnostic.Length
            }),
            sha256 = result.Value.Sha256,
            has_unsaved_changes = result.Value.HasUnsavedChanges,
            content_truncated = bounded.IsTruncated,
            content = bounded.Content
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ReadActiveTerminalAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments);
        var handle = _getActiveTerminal(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no terminal rooted inside the selected approved workspace.");
        }

        var result = await _operations.ReadTerminalStateAsync(
            workspaceId,
            handle.Title,
            handle.ShellKind,
            handle.WorkingDirectory,
            handle.WorkspaceUri,
            handle.Prompt,
            handle.IsRunning,
            handle.ReadDraftCommand,
            handle.ReadTranscript,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            title = result.Value.Title,
            shell_kind = result.Value.ShellKind,
            working_directory = result.Value.WorkingDirectory,
            workspace_uri = handle.WorkspaceUri,
            remote_host_id = handle.RemoteHostId,
            prompt = result.Value.Prompt,
            is_running = result.Value.IsRunning,
            draft_command = result.Value.DraftCommand,
            transcript_truncated = result.Value.IsTranscriptTruncated,
            transcript = result.Value.Transcript.Select(line => new
            {
                kind = line.Kind,
                text = line.Text
            })
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ReadActiveVisualPageAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments);
        var handle = _getActiveVisualPage(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active Hyperpage Studio document allowed by the selected Context.");
        }

        var result = await _operations.ReadVisualPageAsync(
            workspaceId,
            handle.PageDocumentPath,
            _ => Task.FromResult(handle.ReadSnapshot()),
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        var bounded = BoundText(result.Value);
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            project_id = handle.ProjectId,
            page_id = handle.PageId,
            page_name = handle.PageName,
            route = handle.Route,
            revision = handle.Revision,
            snapshot_truncated = bounded.IsTruncated,
            snapshot_json = bounded.Content,
            security_boundary =
                "Host-owned visual model only; no raw DOM, WebView, preview process, publish, firewall, or production-release access."
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ValidateVisualPageAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "expected_revision");
        var expectedRevision = RequiredString(
            arguments,
            "expected_revision",
            maximumLength: 128);
        var handle = _getActiveVisualPage(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active Hyperpage Studio document allowed by the selected Context.");
        }
        if (!string.Equals(
                handle.Revision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The active Hyperpage document changed. Read it again and use its current revision.");
        }

        var result = await _operations.ValidateVisualPageAsync(
            workspaceId,
            handle.PageDocumentPath,
            _ => Task.FromResult(handle.Validate()),
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            project_id = handle.ProjectId,
            page_id = handle.PageId,
            revision = handle.Revision,
            valid = result.Value.All(diagnostic =>
                diagnostic.Severity != VisualPageDiagnosticSeverity.Error),
            diagnostics = result.Value.Select(diagnostic => new
            {
                code = diagnostic.Code,
                severity = diagnostic.Severity.ToString(),
                message = diagnostic.Message,
                element_id = diagnostic.ElementId,
                relative_path = diagnostic.RelativePath,
                line = diagnostic.Line
            })
        }));
    }

    private WorkspaceAiToolOutcome PrepareVisualPageMarkup(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory)
    {
        EnsureProperties(
            arguments,
            "expected_revision",
            "parent_element_id",
            "markup_language",
            "reason");
        var expectedRevision = RequiredString(
            arguments,
            "expected_revision",
            maximumLength: 128);
        var parentElementId = RequiredString(
            arguments,
            "parent_element_id",
            maximumLength: 128);
        var markupLanguage = RequiredString(
            arguments,
            "markup_language",
            maximumLength: 16);
        var reason = RequiredString(
            arguments,
            "reason",
            maximumLength: 1000,
            allowLineBreaks: true);
        if (markupLanguage is not ("html" or "razor"))
        {
            return Error("markup_language must be either html or razor.");
        }

        var handle = _getActiveVisualPage(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active Hyperpage Studio document allowed by the selected Context.");
        }
        if (!string.Equals(
                handle.Revision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The active Hyperpage document changed. Read it again before preparing markup.");
        }

        return new WorkspaceAiToolOutcome(
            JsonSerializer.Serialize(new
            {
                ok = true,
                status = "ready_for_visual_page_markup_transfer",
                project_id = handle.ProjectId,
                page_id = handle.PageId,
                page_name = handle.PageName,
                revision = handle.Revision,
                parent_element_id = parentElementId,
                markup_language = markupLanguage,
                visual_page_markup_transfer =
                    VisualPageMarkupTransferRequestMarker,
                message =
                    "Return only one complete HTML or Razor body fragment as ordinary response text. Include a scoped style element when the requested design needs CSS. Do not use JSON arguments, tools, or commentary."
            }),
            PreparedVisualPageImport: new WorkspaceAiPreparedVisualPageImport(
                call.CallId,
                call.Name,
                workspaceId,
                handle.SessionId,
                workingDirectory,
                handle.ProjectId,
                handle.PageId,
                expectedRevision,
                parentElementId,
                string.Equals(
                    markupLanguage,
                    "razor",
                    StringComparison.Ordinal),
                reason));
    }

    private async Task<WorkspaceAiToolOutcome> ProposeVisualPageChangesAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "expected_revision",
            "operations",
            "reason");
        var expectedRevision = RequiredString(
            arguments,
            "expected_revision",
            maximumLength: 128);
        var reason = RequiredString(
            arguments,
            "reason",
            maximumLength: 1000,
            allowLineBreaks: true);
        if (!arguments.TryGetProperty("operations", out var operationsElement)
            || operationsElement.ValueKind != JsonValueKind.Array)
        {
            throw new WorkspaceAiToolArgumentException(
                "operations must be a JSON array of semantic visual-page operations.");
        }
        var rawOperations = operationsElement.GetRawText();
        if (rawOperations.Length > MaximumProposedTextCharacters)
        {
            return Error("The semantic mutation payload exceeds the 256 KiB limit.");
        }
        var operations = JsonSerializer.Deserialize<
                             List<VisualPageMutationOperation>>(
                             rawOperations,
                             VisualPageJson.Options)
                         ?? [];
        if (operations.Count is < 1 or > 4)
        {
            return Error(
                "A focused visual-page mutation set requires 1 to 4 operations. Use prepare_visual_page_markup for a multi-section or full-page change.");
        }

        var handle = _getActiveVisualPage(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active Hyperpage Studio document allowed by the selected Context.");
        }
        return await CompleteVisualPageChangesAsync(
            workspaceId,
            call.CallId,
            call.Name,
            expectedRevision,
            reason,
            operations,
            workingDirectory,
            handle,
            continueWithHostMessage: false,
            cancellationToken);
    }

    private async Task<WorkspaceAiToolOutcome> CompleteVisualPageChangesAsync(
        string workspaceId,
        string callId,
        string toolName,
        string expectedRevision,
        string reason,
        IReadOnlyList<VisualPageMutationOperation> operations,
        string workingDirectory,
        WorkspaceAiActiveVisualPageHandle handle,
        bool continueWithHostMessage,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                handle.Revision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The active Hyperpage document changed. Read it again before proposing a mutation.");
        }

        var mutationSet = new VisualPageMutationSet(
            expectedRevision,
            operations,
            "workspace-ai",
            reason);
        var preview = await _operations.PreviewVisualPageMutationAsync(
            workspaceId,
            handle.PageDocumentPath,
            _ => Task.FromResult(handle.PreviewMutation(mutationSet)),
            cancellationToken);
        if (!preview.Succeeded || preview.Value is null)
        {
            return FromFailure(preview);
        }

        var highlightedIds = preview.Value.Changes
            .Select(change => change.ElementId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        handle.HighlightElements(highlightedIds);

        if (_permissionBroker.AllowsAutomaticFileAndEditorChanges(workspaceId))
        {
            var applied = await _operations.MutateVisualPageAsync(
                workspaceId,
                handle.PageDocumentPath,
                _ => Task.FromResult(handle.ApplyMutation(mutationSet)),
                cancellationToken);
            return applied.Succeeded && applied.Value is not null
                ? VisualPageMutationOutcome(applied.Value, autoApplied: true)
                : FromFailure(applied);
        }

        var containsCustomCode = operations.Any(ContainsCustomCode);
        var review = new WorkspaceAiPendingReview
        {
            CallId = callId,
            ToolName = toolName,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.VisualPageMutation,
            Title = containsCustomCode
                ? "Review Hyperpage and custom-code changes"
                : "Review Hyperpage changes",
            Summary = containsCustomCode
                ? "The AI proposed revision-checked semantic changes containing a custom-code node. Nothing has been changed; publication will require the reviewed-code lane."
                : "The AI proposed revision-checked semantic changes. Nothing has been changed.",
            Target = $"{handle.PageName} ({handle.Route})",
            Preview = BuildVisualPageMutationPreview(
                expectedRevision,
                preview.Value),
            ContinueWithHostMessage = continueWithHostMessage,
            Payload = new WorkspaceAiVisualPageMutationPayload(
                handle.SessionId,
                workingDirectory,
                handle.ProjectId,
                handle.PageId,
                mutationSet)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private Task<WorkspaceAiToolOutcome> ProposeVisualPagePreviewBuildAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory)
    {
        EnsureProperties(arguments, "expected_revision");
        var expectedRevision = RequiredString(
            arguments,
            "expected_revision",
            maximumLength: 128);
        var handle = _getActiveVisualPage(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Task.FromResult(Error(
                "There is no active Hyperpage Studio document allowed by the selected Context."));
        }
        if (!string.Equals(
                handle.Revision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            return Task.FromResult(Error(
                "The active Hyperpage document changed. Read it again before building a preview."));
        }

        var prepared = _operations.PrepareVisualPagePreviewBuild(
            workspaceId,
            handle.ProjectRoot);
        if (!prepared.Succeeded)
        {
            return Task.FromResult(FromFailure(prepared));
        }

        var review = new WorkspaceAiPendingReview
        {
            CallId = call.CallId,
            ToolName = call.Name,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.VisualPagePreviewBuild,
            Title = "Trust and build isolated Blazor preview",
            Summary =
                "The build runs project C#, packages, and MSBuild targets with the current Windows user's ambient permissions. It creates only a bounded local cache snapshot and cannot host or publish the page.",
            Target = handle.ProjectRoot,
            Preview =
                $"Project: {handle.ProjectId}{Environment.NewLine}Page: {handle.PageId}{Environment.NewLine}Revision: {expectedRevision}{Environment.NewLine}Action: dotnet build of an isolated latest-revision snapshot",
            ExecutesCommand = true,
            RequiresNativeConfirmation = true,
            Payload = new WorkspaceAiVisualPagePreviewBuildPayload(
                handle.SessionId,
                workingDirectory,
                handle.ProjectId,
                handle.PageId,
                expectedRevision)
        };
        return Task.FromResult(new WorkspaceAiToolOutcome(string.Empty, review));
    }

    private async Task<WorkspaceAiToolOutcome> ProposeWebTemplateCaptureAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "url",
            "expected_revision",
            "template_name",
            "description",
            "scope",
            "category_group",
            "category",
            "permission_confirmed",
            "reason");
        var url = RequiredString(arguments, "url", maximumLength: 2048);
        var expectedRevision = RequiredString(
            arguments,
            "expected_revision",
            maximumLength: 128);
        var templateName = RequiredString(
            arguments,
            "template_name",
            maximumLength: 200);
        var description = RequiredString(
            arguments,
            "description",
            maximumLength: 800,
            allowLineBreaks: true);
        var scope = RequiredString(arguments, "scope", maximumLength: 16)
            .ToLowerInvariant();
        var categoryGroup = RequiredString(
            arguments,
            "category_group",
            maximumLength: 80);
        var category = RequiredString(
            arguments,
            "category",
            maximumLength: 80);
        var permissionConfirmed = RequiredBoolean(
            arguments,
            "permission_confirmed");
        var reason = RequiredString(
            arguments,
            "reason",
            maximumLength: 1000,
            allowLineBreaks: true);
        if (!permissionConfirmed)
        {
            return Error(
                "Website template capture requires the user to confirm that they own the site or have permission to reuse its structure. Ask before calling this tool again.");
        }
        if (scope is not ("page" or "section" or "element"))
        {
            return Error(
                "scope must be page, section, or element.");
        }
        if (!VisualPageCommonTemplateCatalog.CategoryGroups.Contains(
                categoryGroup,
                StringComparer.Ordinal))
        {
            return Error(
                "category_group must be one of Hyperpage Studio's published template category groups.");
        }

        var handle = _getActiveVisualPage(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active Hyperpage Studio document allowed by the selected Context.");
        }
        if (!string.Equals(
                handle.Revision,
                expectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The active Hyperpage document changed. Read it again before capturing a website template.");
        }

        var fetched = await _operations.FetchInternetTextAsync(
            workspaceId,
            url,
            cancellationToken);
        if (!fetched.Succeeded || fetched.Value is null)
        {
            return FromFailure(fetched);
        }
        if (!fetched.Value.ContentType.Equals(
                "text/html",
                StringComparison.OrdinalIgnoreCase)
            && !fetched.Value.ContentType.Equals(
                "application/xhtml+xml",
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                $"Website template capture requires an HTML response; the server returned '{fetched.Value.ContentType}'.");
        }

        VisualPageWebTemplateCaptureResult capture;
        try
        {
            capture = handle.CaptureWebTemplate(
                fetched.Value.Content,
                new VisualPageWebTemplateCaptureRequest(
                    fetched.Value.Url,
                    templateName,
                    description,
                    scope,
                    categoryGroup,
                    category));
        }
        catch (Exception exception) when (exception is
                                         VisualPageValidationException
                                         or ArgumentException
                                         or InvalidOperationException)
        {
            return Error(
                $"The fetched HTML could not become a bounded Hyperpage template: {exception.Message}");
        }

        var templatePath = VisualPageProjectService.ResolveProjectPath(
            handle.ProjectRoot,
            capture.RelativePath);
        var preview = await _operations.PreviewVisualPageTemplateAsync(
            workspaceId,
            handle.PageDocumentPath,
            templatePath,
            _ => Task.FromResult(capture),
            cancellationToken);
        if (!preview.Succeeded || preview.Value is null)
        {
            return FromFailure(preview);
        }

        var review = new WorkspaceAiPendingReview
        {
            CallId = call.CallId,
            ToolName = call.Name,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.VisualPageTemplateCapture,
            Title = "Review website structure template",
            Summary =
                "The public page was converted into inert semantic structure with placeholder copy. No scripts, remote assets, captured CSS, forms actions, or inline event code will be saved.",
            Target = capture.RelativePath,
            Preview =
                $"SOURCE: {capture.Template.SourceUrl}{Environment.NewLine}"
                + $"TEMPLATE: {capture.Template.Name}{Environment.NewLine}"
                + $"TYPE: {capture.Template.Scope}{Environment.NewLine}"
                + $"CATEGORY: {capture.Template.CategoryGroup} / {capture.Template.Category}{Environment.NewLine}"
                + $"ELEMENTS: {capture.ElementCount:N0}{Environment.NewLine}"
                + $"REMOVED ACTIVE BLOCKS: {capture.RemovedActiveBlocks:N0}{Environment.NewLine}"
                + $"REMOVED UNSUPPORTED NODES: {capture.RemovedUnsupportedNodes:N0}{Environment.NewLine}"
                + $"REMOVED EXTERNAL REFERENCES: {capture.RemovedExternalReferences:N0}{Environment.NewLine}"
                + $"CONTENT POLICY: {capture.Template.ContentPolicy}{Environment.NewLine}"
                + $"REASON: {reason}{Environment.NewLine}{Environment.NewLine}"
                + "Approving creates a new project template file and catalog reference. It does not alter the active page, download assets, run code, host, or publish.",
            Payload = new WorkspaceAiVisualPageTemplateCapturePayload(
                handle.SessionId,
                workingDirectory,
                handle.ProjectId,
                handle.PageId,
                expectedRevision,
                templatePath,
                capture.RelativePath,
                capture.Template)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private async Task<WorkspaceAiToolOutcome> ProposeWorkspaceWriteAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "relative_path",
            "content",
            "expected_sha256",
            "allow_create");
        var relativePath = ResolveProjectPath(
            workingDirectory,
            RequiredString(
            arguments,
            "relative_path",
            maximumLength: 1024));
        var content = RequiredString(
            arguments,
            "content",
            MaximumProposedTextCharacters,
            allowEmpty: true,
            allowLineBreaks: true);
        var expectedSha256 = NullableString(
            arguments,
            "expected_sha256",
            maximumLength: 64);
        var allowCreate = RequiredBoolean(arguments, "allow_create");
        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !IsSha256(expectedSha256))
        {
            return Error(
                "expected_sha256 must be null or exactly 64 hexadecimal characters.");
        }

        return await CompleteWorkspaceWriteAsync(
            workspaceId,
            call.CallId,
            call.Name,
            relativePath,
            content,
            expectedSha256,
            allowCreate,
            continueWithHostMessage: false,
            cancellationToken);
    }

    private async Task<WorkspaceAiToolOutcome> PrepareWorkspaceWriteAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "relative_path",
            "expected_sha256",
            "allow_create");
        var relativePath = ResolveProjectPath(
            workingDirectory,
            RequiredString(
            arguments,
            "relative_path",
            maximumLength: 1024));
        var expectedSha256 = NullableString(
            arguments,
            "expected_sha256",
            maximumLength: 64);
        var allowCreate = RequiredBoolean(arguments, "allow_create");
        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !IsSha256(expectedSha256))
        {
            return Error(
                "expected_sha256 must be null for a new file or exactly 64 hexadecimal characters from a current read of an existing file.");
        }

        var validated = await ValidateWorkspaceWriteTargetAsync(
            workspaceId,
            relativePath,
            expectedSha256,
            allowCreate,
            cancellationToken);
        if (validated.Failure is not null
            || validated.Target is null)
        {
            return validated.Failure
                   ?? Error("The workspace write target could not be prepared.");
        }

        return new WorkspaceAiToolOutcome(
            JsonSerializer.Serialize(new
            {
                ok = true,
                status = "ready_for_source_transfer",
                target = validated.Target.Target,
                exists = validated.Target.Exists,
                source_transfer = SourceTransferRequestMarker,
                message =
                    "Return only the exact file content as ordinary response text. Do not use JSON arguments, a terminal command, or commentary."
            }),
            PreparedWorkspaceWrite: new WorkspaceAiPreparedWorkspaceWrite(
                call.CallId,
                call.Name,
                workspaceId,
                validated.Target.Target,
                expectedSha256,
                allowCreate));
    }

    private async Task<WorkspaceAiToolOutcome> PrepareWorkspaceEditAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "relative_path",
            "expected_sha256");
        var relativePath = ResolveProjectPath(
            workingDirectory,
            RequiredString(
                arguments,
                "relative_path",
                maximumLength: 1024));
        var expectedSha256 = RequiredString(
            arguments,
            "expected_sha256",
            maximumLength: 64);
        if (!IsSha256(expectedSha256))
        {
            return Error(
                "expected_sha256 must be exactly 64 hexadecimal characters from a current read of the existing file.");
        }

        var validated = await ValidateWorkspaceWriteTargetAsync(
            workspaceId,
            relativePath,
            expectedSha256,
            allowCreate: false,
            cancellationToken);
        if (validated.Failure is not null || validated.Target is null)
        {
            return validated.Failure
                   ?? Error("The workspace edit target could not be prepared.");
        }

        if (!validated.Target.Exists)
        {
            return Error(
                "Exact edit requires an existing file. Use prepare_workspace_write to create a new file.");
        }

        return new WorkspaceAiToolOutcome(
            JsonSerializer.Serialize(new
            {
                ok = true,
                status = "ready_for_exact_edit_transfer",
                target = validated.Target.Target,
                source_transfer = ExactEditTransferRequestMarker,
                syntax = new
                {
                    begin = "<<<<<<< SEARCH",
                    separator = "=======",
                    end = ">>>>>>> REPLACE"
                },
                message =
                    "Return only one or more exact SEARCH/REPLACE blocks. SEARCH text must be copied exactly from the current file and match once. Do not use JSON, tools, fences, or commentary."
            }),
            PreparedWorkspaceWrite: new WorkspaceAiPreparedWorkspaceWrite(
                call.CallId,
                call.Name,
                workspaceId,
                validated.Target.Target,
                expectedSha256,
                AllowCreate: false,
                WorkspaceAiSourceTransferKind.ExactEdit,
                validated.Target.CurrentContent));
    }

    private async Task<WorkspaceAiToolOutcome> CompleteWorkspaceWriteAsync(
        string workspaceId,
        string callId,
        string toolName,
        string relativePath,
        string content,
        string? expectedSha256,
        bool allowCreate,
        bool continueWithHostMessage,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateWorkspaceWriteTargetAsync(
            workspaceId,
            relativePath,
            expectedSha256,
            allowCreate,
            cancellationToken);
        if (validated.Failure is not null
            || validated.Target is null)
        {
            return validated.Failure
                   ?? Error("The workspace write target could not be validated.");
        }

        var target = validated.Target;

        if (_permissionBroker.AllowsAutomaticFileAndEditorChanges(
                workspaceId))
        {
            return FromOperation(await _operations.WriteTextAsync(
                workspaceId,
                target.Target,
                content,
                expectedSha256,
                allowCreate,
                cancellationToken));
        }

        var review = new WorkspaceAiPendingReview
        {
            CallId = callId,
            ToolName = toolName,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.WorkspaceTextWrite,
            Title = target.Exists ? "Review file replacement" : "Review new file",
            Summary = target.Exists
                ? "The AI proposed an atomic full-file replacement. Nothing has been written."
                : "The AI proposed creating a text file. Nothing has been written.",
            Target = target.Target,
            Preview = BuildBeforeAfterPreview(
                target.Target,
                target.CurrentContent,
                content,
                target.CurrentSha256),
            ContinueWithHostMessage = continueWithHostMessage,
            Payload = new WorkspaceAiWorkspaceWritePayload(
                target.Target,
                content,
                expectedSha256,
                allowCreate)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private async Task<(
        WorkspaceAiTextTargetState? Target,
        WorkspaceAiToolOutcome? Failure)> ValidateWorkspaceWriteTargetAsync(
            string workspaceId,
            string relativePath,
            string? expectedSha256,
            bool allowCreate,
            CancellationToken cancellationToken)
    {
        var target = await _operations.InspectTextTargetAsync(
            workspaceId,
            relativePath,
            cancellationToken);
        if (!target.Succeeded || target.Value is null)
        {
            return (null, FromFailure(target));
        }

        if (target.Value.Exists)
        {
            if (!IsSha256(expectedSha256)
                || !string.Equals(
                    expectedSha256,
                    target.Value.CurrentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (
                    null,
                    Error(
                        "The existing file must be read first and its current SHA-256 supplied before a replacement can be reviewed. Never hash the proposed content."));
            }
        }
        else
        {
            if (!allowCreate)
            {
                return (
                    null,
                    Error(
                        "The target does not exist and allow_create is false."));
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                return (
                    null,
                    Error(
                        "A new file requires expected_sha256=null. That field is only the current hash of an existing file, never a hash of proposed content."));
            }
        }

        return (target.Value, null);
    }

    private async Task<WorkspaceAiToolOutcome>
        ProposeActiveDocumentReplaceAsync(
            string workspaceId,
            WorkspaceAiFunctionCall call,
            JsonElement arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "content", "expected_sha256");
        var content = RequiredString(
            arguments,
            "content",
            MaximumProposedTextCharacters,
            allowEmpty: true,
            allowLineBreaks: true);
        var expectedSha256 = RequiredString(
            arguments,
            "expected_sha256",
            64);
        if (!IsSha256(expectedSha256))
        {
            return Error(
                "expected_sha256 must be exactly 64 hexadecimal characters.");
        }

        var handle = _getActiveDocument(workspaceId, workingDirectory);
        if (handle is null)
        {
            return Error(
                "There is no active typed document allowed by the selected Context.");
        }

        var current = await _operations.ReadOpenDocumentAsync(
            workspaceId,
            handle.RelativePath,
            handle.SessionId,
            handle.Title,
            handle.LanguageId,
            handle.ProviderId,
            handle.HasUnsavedChanges,
            handle.ReadContent,
            handle.Analyze,
            cancellationToken);
        if (!current.Succeeded || current.Value is null)
        {
            return FromFailure(current);
        }

        if (!string.Equals(
                current.Value.Sha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                "The active document changed after it was read. Read it again before proposing a replacement.");
        }

        if (_permissionBroker.AllowsAutomaticFileAndEditorChanges(
                workspaceId))
        {
            return FromOperation(await _operations.ReplaceOpenDocumentAsync(
                workspaceId,
                current.Value.RelativePath,
                handle.SessionId,
                content,
                current.Value.Sha256,
                handle.ReadContent,
                replacement => handle.ReplaceContent(replacement),
                cancellationToken));
        }

        var review = new WorkspaceAiPendingReview
        {
            CallId = call.CallId,
            ToolName = call.Name,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.OpenDocumentReplace,
            Title = "Review editor replacement",
            Summary =
                $"The AI proposed replacing the live {handle.LanguageId} document through {handle.ProviderId}. The edit will remain unsaved until you save it.",
            Target = current.Value.RelativePath,
            Preview = BuildBeforeAfterPreview(
                current.Value.RelativePath,
                current.Value.Content,
                content,
                current.Value.Sha256),
            Payload = new WorkspaceAiOpenDocumentReplacePayload(
                handle.SessionId,
                current.Value.RelativePath,
                content,
                current.Value.Sha256,
                workingDirectory)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private async Task<WorkspaceAiToolOutcome> ProposeWorkspaceDeleteAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "path", "expected_sha256");
        var path = ResolveProjectPath(
            workingDirectory,
            RequiredString(arguments, "path", maximumLength: 1024));
        var expectedSha256 = RequiredString(
            arguments,
            "expected_sha256",
            maximumLength: 64);
        if (!IsSha256(expectedSha256))
        {
            return Error(
                "expected_sha256 must be exactly 64 hexadecimal characters.");
        }

        var authorization = _permissionBroker.AuthorizePath(
            workspaceId,
            path,
            WorkspaceAiCapability.Delete,
            WorkspaceAiOperationKind.DeleteText);
        if (!authorization.IsAllowed)
        {
            return Error(authorization.Message);
        }

        var current = await _operations.ReadTextAsync(
            workspaceId,
            authorization.RelativePath,
            cancellationToken);
        if (!current.Succeeded || current.Value is null)
        {
            return FromFailure(current);
        }

        if (!string.Equals(
                current.Value.Sha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                "The file changed after it was read. Read it again before requesting deletion.");
        }

        if (_permissionBroker.AllowsAutomaticFileAndEditorChanges(workspaceId))
        {
            return FromOperation(await _operations.DeleteTextAsync(
                workspaceId,
                authorization.RelativePath,
                expectedSha256,
                cancellationToken));
        }

        var review = new WorkspaceAiPendingReview
        {
            CallId = call.CallId,
            ToolName = call.Name,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.WorkspaceTextDelete,
            Title = "Review permanent deletion",
            Summary = "The AI requested permanently deleting this text file. Nothing has been deleted.",
            Target = authorization.RelativePath,
            Preview = $"DELETE: {authorization.RelativePath}\nSHA-256: {expectedSha256}\nSIZE: {current.Value.ByteLength:N0} byte(s)",
            Payload = new WorkspaceAiWorkspaceDeletePayload(
                authorization.RelativePath,
                expectedSha256)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private async Task<WorkspaceAiToolOutcome> HandleTerminalCommandAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        bool execute,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "command");
        var command = RequiredString(
            arguments,
            "command",
            WorkspaceAiOperationService.MaximumCommandCharacters,
            allowLineBreaks: true);
        var terminal = _getActiveTerminal(workspaceId, workingDirectory)
                       ?? CreateProjectTerminalHandle(workingDirectory);
        if (terminal is null)
        {
            return Error(
                "There is no local or remote terminal allowed by the selected Context.");
        }

        return await HandleResolvedTerminalCommandAsync(
            workspaceId,
            call,
            terminal,
            command,
            execute,
            workingDirectory,
            projectCheck: null,
            cancellationToken);
    }

    private async Task<WorkspaceAiToolOutcome> RunProjectCheckAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "check");
        var check = RequiredString(
            arguments,
            "check",
            maximumLength: 16).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return Error(
                "Choose a conversation working directory before running a project check.");
        }

        var root = ResolveProjectPath(workingDirectory, ".");
        var listing = await _operations.ListDirectoryAsync(
            workspaceId,
            root,
            cancellationToken);
        if (!listing.Succeeded || listing.Value is null)
        {
            return FromFailure(listing);
        }

        string? packageJson = null;
        var packageEntry = listing.Value.Entries.FirstOrDefault(entry =>
            !entry.IsDirectory
            && string.Equals(
                entry.Name,
                "package.json",
                StringComparison.OrdinalIgnoreCase));
        if (packageEntry is not null)
        {
            var packageRead = await _operations.ReadTextAsync(
                workspaceId,
                packageEntry.RelativePath,
                cancellationToken);
            if (packageRead.Succeeded && packageRead.Value is not null)
            {
                packageJson = packageRead.Value.Content;
            }
        }

        var terminal = _getActiveTerminal(workspaceId, workingDirectory)
                       ?? CreateProjectTerminalHandle(workingDirectory);
        if (terminal is null)
        {
            return Error(
                "There is no local or remote terminal allowed by the selected Context.");
        }

        var posixShell = WorkspaceLocation.TryParse(
                             terminal.WorkspaceUri,
                             out var terminalLocation)
                         && terminalLocation.IsSsh
                         || terminal.ShellKind.Contains(
                             "bash",
                             StringComparison.OrdinalIgnoreCase)
                         || terminal.ShellKind.Contains(
                             "zsh",
                             StringComparison.OrdinalIgnoreCase);
        var planning = WorkspaceAiProjectCheckPlanner.Create(
            check,
            listing.Value.Entries,
            packageJson,
            posixShell);
        if (!planning.Succeeded || planning.Plan is null)
        {
            return Error(planning.Message);
        }

        return await HandleResolvedTerminalCommandAsync(
            workspaceId,
            call,
            terminal,
            planning.Plan.Command,
            execute: true,
            workingDirectory,
            planning.Plan,
            cancellationToken);
    }

    private async Task<WorkspaceAiToolOutcome>
        HandleResolvedTerminalCommandAsync(
            string workspaceId,
            WorkspaceAiFunctionCall call,
            WorkspaceAiActiveTerminalHandle terminal,
            string command,
            bool execute,
            string workingDirectory,
            WorkspaceAiProjectCheckPlan? projectCheck,
            CancellationToken cancellationToken)
    {

        var authorization = _permissionBroker.AuthorizeTerminal(
            workspaceId,
            execute,
            userConfirmed: false,
            terminal.WorkspaceUri);
        if (authorization.Outcome == WorkspaceAiAuthorizationOutcome.Denied)
        {
            return Error(authorization.Message);
        }

        var policy = _permissionBroker.GetTerminalExecutionPolicy(workspaceId);
        if (execute
            && policy == WorkspaceAiTerminalExecutionPolicy.AutoExecute)
        {
            return await DispatchTerminalWithTranscriptAsync(
                workspaceId,
                new WorkspaceAiTerminalCommandPayload(
                    terminal.SessionId,
                    terminal.WorkspaceUri,
                    command,
                    Execute: true,
                    workingDirectory,
                    projectCheck),
                userConfirmedExecution: false,
                cancellationToken);
        }

        var review = new WorkspaceAiPendingReview
        {
            CallId = call.CallId,
            ToolName = call.Name,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.TerminalCommand,
            Title = projectCheck is not null
                ? $"Review {projectCheck.ProjectKind} {projectCheck.EffectiveCheck} check"
                : execute
                ? policy == WorkspaceAiTerminalExecutionPolicy.ReviewOnly
                    ? "Review terminal execution"
                    : "Confirm terminal execution"
                : "Review terminal staging",
            Summary = projectCheck is not null
                ? $"Hypershell detected {projectCheck.Target} and selected its conventional {projectCheck.EffectiveCheck} command. Approving runs it in the conversation working directory and returns the completed terminal output."
                : execute
                ? policy == WorkspaceAiTerminalExecutionPolicy.ReviewOnly
                    ? "The AI requested immediate execution. Approving this review runs the command without a second dialog; the shell uses your Windows permissions."
                    : "The AI requested immediate execution in a terminal rooted at this workspace. A separate native confirmation is still required, and the shell runs with your Windows permissions."
                : "The AI requested placing this text into a workspace terminal without executing it.",
            Target = terminal.WorkspaceUri,
            Preview = command,
            ExecutesCommand = execute,
            RequiresNativeConfirmation = execute
                && policy
                    == WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
            Payload = new WorkspaceAiTerminalCommandPayload(
                terminal.SessionId,
                terminal.WorkspaceUri,
                command,
                execute,
                workingDirectory,
                projectCheck)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private async Task<WorkspaceAiToolOutcome> FetchUrlTextAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        EnsureProperties(arguments, "url");
        var url = RequiredString(arguments, "url", maximumLength: 2048);
        var result = await _operations.FetchInternetTextAsync(
            workspaceId,
            url,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        var bounded = BoundText(result.Value.Content);
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            url = result.Value.Url,
            status_code = result.Value.StatusCode,
            content_type = result.Value.ContentType,
            content_truncated = bounded.IsTruncated,
            content = bounded.Content
        }));
    }

    private async Task<WorkspaceAiToolOutcome> InspectGitRepositoryAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string conversationId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "workspace",
            "query",
            "path",
            "reference",
            "diff_target",
            "limit");
        if (_gitTools is null)
        {
            return Error(
                "The semantic Git adapter is not available in this Hypershell session.");
        }

        var workspace = ResolveProjectPath(
            workingDirectory,
            RequiredString(
            arguments,
            "workspace",
            maximumLength: 2048));
        var kind = ParseGitInspectionKind(RequiredString(
            arguments,
            "query",
            maximumLength: 32));
        var path = OptionalString(
            arguments,
            "path",
            maximumLength: 4096);
        var reference = OptionalString(
            arguments,
            "reference",
            maximumLength: 512);
        var target = ParseGitDiffTarget(RequiredString(
            arguments,
            "diff_target",
            maximumLength: 32));
        var limit = RequiredInteger(
            arguments,
            "limit",
            minimum: 1,
            maximum: 200);
        var result = await _gitTools.InspectAsync(
            workspaceId,
            workspace,
            kind,
            path,
            reference,
            target,
            limit,
            conversationId,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            status = result.Status.ToString(),
            message = result.Message,
            query = kind.ToString(),
            workspace_uri = result.Value.WorkspaceUri,
            value = BuildGitInspectionValue(result.Value)
        }));
    }

    private async Task<WorkspaceAiToolOutcome> ProposeGitOperationAsync(
        string workspaceId,
        WorkspaceAiFunctionCall call,
        JsonElement arguments,
        string conversationId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        EnsureProperties(
            arguments,
            "workspace",
            "operation",
            "paths",
            "reference",
            "name",
            "new_name",
            "message",
            "target",
            "remote",
            "branch",
            "url",
            "patch",
            "flag",
            "reset_mode");
        if (_gitTools is null)
        {
            return Error(
                "The semantic Git adapter is not available in this Hypershell session.");
        }

        var request = new WorkspaceAiGitMutationRequest
        {
            WorkspaceIdentity = ResolveProjectPath(
                workingDirectory,
                RequiredString(
                arguments,
                "workspace",
                maximumLength: 2048)),
            Kind = ParseGitMutationKind(RequiredString(
                arguments,
                "operation",
                maximumLength: 48)),
            Paths = RequiredStringArray(
                arguments,
                "paths",
                WorkspaceAiGitService.MaximumPaths,
                maximumItemLength: 4096),
            Reference = OptionalString(
                arguments,
                "reference",
                maximumLength: 512),
            Name = OptionalString(
                arguments,
                "name",
                maximumLength: 512),
            NewName = OptionalString(
                arguments,
                "new_name",
                maximumLength: 512),
            Message = OptionalString(
                arguments,
                "message",
                maximumLength: 64 * 1024,
                allowLineBreaks: true),
            Target = OptionalString(
                arguments,
                "target",
                maximumLength: 512),
            Remote = OptionalString(
                arguments,
                "remote",
                maximumLength: 512),
            Branch = OptionalString(
                arguments,
                "branch",
                maximumLength: 512),
            Url = OptionalString(
                arguments,
                "url",
                maximumLength: 4096),
            Patch = OptionalString(
                arguments,
                "patch",
                WorkspaceAiGitService.MaximumPatchCharacters,
                allowLineBreaks: true,
                preserveWhitespace: true),
            Flag = RequiredBoolean(arguments, "flag"),
            ResetMode = ParseGitResetMode(RequiredString(
                arguments,
                "reset_mode",
                maximumLength: 16))
        };
        var prepared = _gitTools.PrepareMutation(
            workspaceId,
            request,
            conversationId);
        if (!prepared.Succeeded || prepared.Value is not { } plan)
        {
            return FromFailure(prepared);
        }

        if (_permissionBroker.AllowsAutomaticFileAndEditorChanges(workspaceId))
        {
            return FromGitMutation(await _gitTools.ExecuteMutationAsync(
                workspaceId,
                request,
                conversationId,
                cancellationToken));
        }

        _gitTools.RecordPendingReview(plan, conversationId);
        var review = new WorkspaceAiPendingReview
        {
            CallId = call.CallId,
            ToolName = call.Name,
            WorkspaceId = workspaceId,
            Kind = WorkspaceAiPendingActionKind.GitOperation,
            Title = plan.IsDestructive
                ? "Review destructive Git operation"
                : "Review Git operation",
            Summary = plan.RequiresNetwork
                ? "The AI proposed a semantic Git operation that can contact a configured remote. Nothing has run."
                : "The AI proposed a semantic Git operation through Hypershell's repository adapter. Nothing has run.",
            Target = plan.Location.Identity,
            Preview = BuildGitMutationPreview(request),
            Payload = new WorkspaceAiGitOperationPayload(request)
        };
        return new WorkspaceAiToolOutcome(string.Empty, review);
    }

    private static object BuildGitInspectionValue(
        WorkspaceAiGitInspectionSnapshot snapshot) => snapshot.Kind switch
        {
            WorkspaceAiGitInspectionKind.Status => BuildGitStatusValue(
                snapshot.Status
                ?? throw new InvalidOperationException(
                    "The Git status inspection returned no status.")),
            WorkspaceAiGitInspectionKind.Diff or
            WorkspaceAiGitInspectionKind.CommitPatch => BuildGitDiffValue(
                snapshot.Diff
                ?? throw new InvalidOperationException(
                    "The Git diff inspection returned no diff.")),
            WorkspaceAiGitInspectionKind.Conflict => BuildGitConflictValue(
                snapshot.Conflict
                ?? throw new InvalidOperationException(
                    "The Git conflict inspection returned no conflict data.")),
            WorkspaceAiGitInspectionKind.Commit => BuildGitCommitValue(
                snapshot.Commit
                ?? throw new InvalidOperationException(
                    "The Git commit inspection returned no commit.")),
            WorkspaceAiGitInspectionKind.History => new
            {
                commits = snapshot.History.Take(200).Select(BuildGitCommitValue),
                is_truncated = snapshot.History.Count > 200
            },
            WorkspaceAiGitInspectionKind.Branches => new
            {
                branches = snapshot.Branches.Take(512).Select(branch => new
                {
                    name = branch.Name,
                    commit_id = branch.CommitId,
                    upstream = branch.Upstream,
                    is_current = branch.IsCurrent,
                    is_remote = branch.IsRemote
                }),
                is_truncated = snapshot.Branches.Count > 512
            },
            WorkspaceAiGitInspectionKind.Tags => new
            {
                tags = snapshot.Tags.Take(512).Select(tag => new
                {
                    name = tag.Name,
                    target_commit_id = tag.TargetCommitId,
                    subject = tag.Subject
                }),
                is_truncated = snapshot.Tags.Count > 512
            },
            WorkspaceAiGitInspectionKind.Remotes => new
            {
                remotes = snapshot.Remotes.Take(256).Select(remote => new
                {
                    name = remote.Name,
                    fetch_url = GitRemoteCredentialPolicy.RedactForDisplay(
                        remote.FetchUrl),
                    push_url = GitRemoteCredentialPolicy.RedactForDisplay(
                        remote.PushUrl)
                }),
                is_truncated = snapshot.Remotes.Count > 256
            },
            WorkspaceAiGitInspectionKind.Stashes => new
            {
                stashes = snapshot.Stashes.Take(256).Select(stash => new
                {
                    reference = stash.Reference,
                    commit_id = stash.CommitId,
                    message = stash.Message
                }),
                is_truncated = snapshot.Stashes.Count > 256
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                snapshot.Kind,
                "The Git inspection kind is unsupported.")
        };

    private static object BuildGitStatusValue(GitRepositoryStatus status)
    {
        const int maximumChanges = 512;
        return new
        {
            repository_root = status.Repository.Root.Identity,
            is_bare = status.Repository.IsBare,
            branch = status.BranchName,
            head_commit_id = status.HeadCommitId,
            upstream = status.Upstream,
            ahead = status.Ahead,
            behind = status.Behind,
            has_conflicts = status.HasConflicts,
            changes = status.Entries.Take(maximumChanges).Select(entry => new
            {
                path = entry.Path,
                original_path = entry.OriginalPath,
                index_code = entry.IndexCode.ToString(),
                working_tree_code = entry.WorkingTreeCode.ToString(),
                is_untracked = entry.IsUntracked,
                is_ignored = entry.IsIgnored,
                is_conflict = entry.IsConflict,
                submodule_state = entry.SubmoduleState
            }),
            changes_truncated = status.Entries.Count > maximumChanges
        };
    }

    private static object BuildGitDiffValue(GitDiffDocument diff)
    {
        var bounded = BoundText(diff.Patch);
        return new
        {
            path = diff.Path,
            target = diff.Target.ToString(),
            base_revision = diff.BaseRevision,
            is_binary = diff.IsBinary,
            is_truncated = diff.IsTruncated || bounded.IsTruncated,
            patch = bounded.Content
        };
    }

    private static object BuildGitConflictValue(GitConflictDocument conflict)
    {
        var maximum = MaximumProviderTextCharacters / 3;
        var baseContent = BoundText(conflict.BaseContent, maximum);
        var ours = BoundText(conflict.OursContent, maximum);
        var theirs = BoundText(conflict.TheirsContent, maximum);
        return new
        {
            path = conflict.Path,
            is_binary = conflict.IsBinary,
            is_truncated = conflict.IsTruncated
                           || baseContent.IsTruncated
                           || ours.IsTruncated
                           || theirs.IsTruncated,
            base_content = baseContent.Content,
            ours_content = ours.Content,
            theirs_content = theirs.Content
        };
    }

    private static object BuildGitCommitValue(GitCommitInfo commit)
    {
        var body = BoundText(commit.Body, 32 * 1024);
        return new
        {
            commit_id = commit.CommitId,
            parent_ids = commit.ParentIds,
            author_name = commit.AuthorName,
            author_email = commit.AuthorEmail,
            authored_at = commit.AuthoredAt,
            subject = commit.Subject,
            body = body.Content,
            body_truncated = body.IsTruncated,
            decorations = commit.Decorations
        };
    }

    private static string BuildGitMutationPreview(
        WorkspaceAiGitMutationRequest request)
    {
        var lines = new List<string>
        {
            $"OPERATION: {request.Kind}",
            $"REPOSITORY: {request.WorkspaceIdentity}"
        };
        if (request.Paths.Count > 0)
        {
            lines.Add("PATHS:");
            lines.AddRange(request.Paths.Take(50).Select(path => $"  {path}"));
            if (request.Paths.Count > 50)
            {
                lines.Add($"  ... {request.Paths.Count - 50:N0} more path(s)");
            }
        }

        AddPreviewValue(lines, "REFERENCE", request.Reference);
        AddPreviewValue(lines, "NAME", request.Name);
        AddPreviewValue(lines, "NEW NAME", request.NewName);
        AddPreviewValue(lines, "TARGET", request.Target);
        AddPreviewValue(lines, "REMOTE", request.Remote);
        AddPreviewValue(lines, "BRANCH", request.Branch);
        AddPreviewValue(
            lines,
            "URL",
            GitRemoteCredentialPolicy.RedactForDisplay(request.Url));
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            var message = BoundText(request.Message, 16 * 1024);
            lines.Add("MESSAGE:");
            lines.Add(message.Content);
            if (message.IsTruncated)
            {
                lines.Add("[message truncated in review]");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Patch))
        {
            var patch = BoundText(request.Patch, 64 * 1024);
            lines.Add("PATCH:");
            lines.Add(patch.Content);
            if (patch.IsTruncated)
            {
                lines.Add("[patch truncated in review; the full bounded patch remains attached to the exact reviewed action]");
            }
        }

        lines.Add($"FLAG: {request.Flag}");
        if (request.Kind == WorkspaceAiGitMutationKind.Reset)
        {
            lines.Add($"RESET MODE: {request.ResetMode}");
        }

        return string.Join('\n', lines);
    }

    private static void AddPreviewValue(
        ICollection<string> lines,
        string label,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }

    private static WorkspaceAiToolOutcome FromGitMutation(
        WorkspaceAiOperationResult<GitOperationReceipt> result) =>
        new(JsonSerializer.Serialize(new
        {
            ok = result.Succeeded,
            status = result.Status.ToString(),
            message = result.Message,
            operation = result.Value?.Operation ?? string.Empty
        }));

    private async Task<WorkspaceAiToolOutcome>
        ApproveOpenDocumentReplaceAsync(
            WorkspaceAiPendingReview review,
            WorkspaceAiOpenDocumentReplacePayload payload,
            CancellationToken cancellationToken)
    {
        var handle = _getDocumentBySessionId(
            review.WorkspaceId,
            payload.WorkingDirectory,
            payload.SessionId);
        if (handle is null
            || !string.Equals(
                handle.RelativePath,
                payload.RelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                "The reviewed document is no longer open at the approved workspace path. No edit was applied.");
        }

        return FromOperation(await _operations.ReplaceOpenDocumentAsync(
            review.WorkspaceId,
            payload.RelativePath,
            payload.SessionId,
            payload.Content,
            payload.ExpectedSha256,
            handle.ReadContent,
            replacement => handle.ReplaceContent(replacement),
            cancellationToken));
    }

    private async Task<WorkspaceAiToolOutcome>
        ApproveVisualPageMutationAsync(
            WorkspaceAiPendingReview review,
            WorkspaceAiVisualPageMutationPayload payload,
            CancellationToken cancellationToken)
    {
        var handle = _getVisualPageBySessionId(
            review.WorkspaceId,
            payload.WorkingDirectory,
            payload.SessionId);
        if (handle is null
            || !string.Equals(
                handle.ProjectId,
                payload.ProjectId,
                StringComparison.Ordinal)
            || !string.Equals(
                handle.PageId,
                payload.PageId,
                StringComparison.Ordinal))
        {
            return Error(
                "The reviewed Hyperpage document is no longer open at the approved project/page identity. No mutation was applied.");
        }

        var result = await _operations.MutateVisualPageAsync(
            review.WorkspaceId,
            handle.PageDocumentPath,
            _ => Task.FromResult(handle.ApplyMutation(payload.MutationSet)),
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        handle.HighlightElements(result.Value.Changes
            .Select(change => change.ElementId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList());
        return VisualPageMutationOutcome(result.Value, autoApplied: false);
    }

    private async Task<WorkspaceAiToolOutcome>
        ApproveVisualPagePreviewBuildAsync(
            WorkspaceAiPendingReview review,
            WorkspaceAiVisualPagePreviewBuildPayload payload,
            bool userConfirmedExecution,
            CancellationToken cancellationToken)
    {
        var handle = _getVisualPageBySessionId(
            review.WorkspaceId,
            payload.WorkingDirectory,
            payload.SessionId);
        if (handle is null
            || !string.Equals(
                handle.ProjectId,
                payload.ProjectId,
                StringComparison.Ordinal)
            || !string.Equals(
                handle.PageId,
                payload.PageId,
                StringComparison.Ordinal))
        {
            return Error(
                "The reviewed Hyperpage document is no longer open at the approved project/page identity. No process was started.");
        }
        if (!string.Equals(
                handle.Revision,
                payload.ExpectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The Hyperpage document changed after review. No preview process was started; read and review the current revision again.");
        }

        var result = await _operations.BuildVisualPagePreviewAsync(
            review.WorkspaceId,
            handle.ProjectRoot,
            userConfirmedExecution,
            handle.BuildPreview,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }

        var output = BoundText(result.Value.Output, 96 * 1024);
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = result.Value.Outcome == VisualPageBuildOutcome.Succeeded,
            status = result.Value.Outcome.ToString(),
            revision = result.Value.Revision,
            output_truncated = output.IsTruncated,
            output = output.Content,
            diagnostics = result.Value.Diagnostics.Select(diagnostic => new
            {
                code = diagnostic.Code,
                severity = diagnostic.Severity.ToString(),
                message = diagnostic.Message,
                element_id = diagnostic.ElementId,
                relative_path = diagnostic.RelativePath,
                line = diagnostic.Line
            }),
            hosted = false,
            published = false
        }));
    }

    private async Task<WorkspaceAiToolOutcome>
        ApproveVisualPageTemplateCaptureAsync(
            WorkspaceAiPendingReview review,
            WorkspaceAiVisualPageTemplateCapturePayload payload,
            CancellationToken cancellationToken)
    {
        var handle = _getVisualPageBySessionId(
            review.WorkspaceId,
            payload.WorkingDirectory,
            payload.SessionId);
        if (handle is null
            || !string.Equals(
                handle.ProjectId,
                payload.ProjectId,
                StringComparison.Ordinal)
            || !string.Equals(
                handle.PageId,
                payload.PageId,
                StringComparison.Ordinal))
        {
            return Error(
                "The reviewed website-template proposal is no longer bound to the same open Hyperpage project and page. No template was created.");
        }
        if (!string.Equals(
                handle.Revision,
                payload.ExpectedRevision,
                StringComparison.Ordinal))
        {
            return Error(
                "The Hyperpage document changed after review. No template was created; read the current page and prepare the capture again.");
        }

        string resolvedTarget;
        try
        {
            resolvedTarget = VisualPageProjectService.ResolveProjectPath(
                handle.ProjectRoot,
                payload.RelativePath);
        }
        catch (Exception exception) when (exception is
                                         ArgumentException
                                         or IOException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            return Error(
                $"The reviewed template target is no longer valid: {exception.Message}");
        }
        if (!string.Equals(
                resolvedTarget,
                payload.TemplatePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                "The reviewed project-template target changed. No file was created.");
        }

        var result = await _operations.CreateVisualPageTemplateAsync(
            review.WorkspaceId,
            handle.PageDocumentPath,
            payload.TemplatePath,
            token => handle.SaveProjectTemplate(
                payload.Template,
                payload.RelativePath,
                token),
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromFailure(result);
        }
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            status = "created_after_review",
            template_id = payload.Template.TemplateId,
            name = payload.Template.Name,
            scope = payload.Template.Scope,
            category_group = payload.Template.CategoryGroup,
            category = payload.Template.Category,
            source_url = payload.Template.SourceUrl,
            relative_path = payload.RelativePath,
            content_policy = payload.Template.ContentPolicy,
            message =
                "The reviewed structure-only template was created in this project. The active page was not changed."
        }));
    }

    private async Task<WorkspaceAiToolOutcome>
        DispatchTerminalWithTranscriptAsync(
            string workspaceId,
            WorkspaceAiTerminalCommandPayload payload,
            bool userConfirmedExecution,
            CancellationToken cancellationToken)
    {
        var beforeHandle = _getActiveTerminal(
            workspaceId,
            payload.WorkingDirectory);
        var beforeSessionId = beforeHandle?.SessionId ?? string.Empty;
        var before = beforeHandle?.ReadTranscript().ToList() ?? [];
        var result = await _dispatchTerminal(
            workspaceId,
            payload.TerminalSessionId,
            payload.TerminalWorkspaceUri,
            payload.Command,
            payload.Execute,
            userConfirmedExecution,
            cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return FromOperation(result);
        }

        var afterHandle = _getActiveTerminal(
            workspaceId,
            payload.WorkingDirectory);
        var after = afterHandle?.ReadTranscript().ToList() ?? [];
        IReadOnlyList<WorkspaceAiTerminalLineSnapshot> delta;
        if (afterHandle is not null
            && string.Equals(
                beforeSessionId,
                afterHandle.SessionId,
                StringComparison.Ordinal)
            && after.Count >= before.Count)
        {
            delta = after.Skip(before.Count).ToList();
        }
        else
        {
            delta = after;
        }

        var bounded = BoundTerminalDelta(delta, out var truncated);
        return new WorkspaceAiToolOutcome(JsonSerializer.Serialize(new
        {
            ok = true,
            status = result.Status.ToString(),
            message = payload.Execute
                ? "The terminal command completed. Inspect terminal_output before deciding whether the check passed or another correction is required."
                : result.Message,
            value = result.Value,
            project_check = payload.ProjectCheck,
            terminal_output_truncated = truncated,
            terminal_output = bounded.Select(line => new
            {
                kind = line.Kind,
                text = line.Text
            })
        }));
    }

    private static IReadOnlyList<WorkspaceAiTerminalLineSnapshot>
        BoundTerminalDelta(
            IReadOnlyList<WorkspaceAiTerminalLineSnapshot> lines,
            out bool truncated)
    {
        const int maximumLines = 200;
        const int maximumCharacters = 96 * 1024;
        var selected = new List<WorkspaceAiTerminalLineSnapshot>();
        var characters = 0;
        for (var index = lines.Count - 1;
             index >= 0 && selected.Count < maximumLines;
             index--)
        {
            var line = lines[index];
            if (characters + line.Text.Length > maximumCharacters)
            {
                if (selected.Count == 0)
                {
                    selected.Add(new WorkspaceAiTerminalLineSnapshot(
                        line.Kind,
                        line.Text[^maximumCharacters..]));
                }

                break;
            }

            selected.Add(line);
            characters += line.Text.Length;
        }

        truncated = selected.Count < lines.Count;
        selected.Reverse();
        return selected;
    }

    private static WorkspaceAiToolOutcome FromOperation<T>(
        WorkspaceAiOperationResult<T> result) =>
        new(JsonSerializer.Serialize(new
        {
            ok = result.Succeeded,
            status = result.Status.ToString(),
            message = result.Message,
            value = result.Value
        }));

    private static WorkspaceAiToolOutcome FromFailure<T>(
        WorkspaceAiOperationResult<T> result) =>
        new(JsonSerializer.Serialize(new
        {
            ok = false,
            status = result.Status.ToString(),
            message = result.Message
        }));

    private static WorkspaceAiToolOutcome Error(string message) =>
        new(JsonSerializer.Serialize(new
        {
            ok = false,
            status = "host_rejected",
            message
        }));

    private static string ResolveProjectPath(
        string workingDirectory,
        string requestedPath)
    {
        try
        {
            return WorkspaceAiWorkingDirectory.ResolveProjectPath(
                workingDirectory,
                requestedPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            throw new WorkspaceAiToolArgumentException(exception.Message);
        }
    }

    private static WorkspaceAiActiveTerminalHandle?
        CreateProjectTerminalHandle(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        var normalized = WorkspaceAiWorkingDirectory.Normalize(
            workingDirectory,
            requireAvailable: false);
        if (WorkspaceLocation.TryParse(normalized, out var remote)
            && remote.IsSsh)
        {
            return new WorkspaceAiActiveTerminalHandle(
                string.Empty,
                "AI project terminal",
                "ssh",
                remote.Path,
                remote.Identity,
                remote.Authority,
                string.Empty,
                false,
                () => string.Empty,
                () => []);
        }

        var local = WorkspaceLocation.Local(normalized);
        return new WorkspaceAiActiveTerminalHandle(
            string.Empty,
            "AI project terminal",
            "PowerShell",
            local.Path,
            local.Identity,
            string.Empty,
            string.Empty,
            false,
            () => string.Empty,
            () => []);
    }

    private static JsonDocument ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)
            || Encoding.UTF8.GetByteCount(arguments) > 512 * 1024)
        {
            throw new WorkspaceAiToolArgumentException(
                "Tool arguments are empty or exceed the 512 KiB host limit.");
        }

        var document = JsonDocument.Parse(
            arguments,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new WorkspaceAiToolArgumentException(
                "Tool arguments must be one JSON object.");
        }

        return document;
    }

    private static void EnsureProperties(
        JsonElement arguments,
        params string[] expected)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!expectedSet.Contains(property.Name)
                || !seen.Add(property.Name))
            {
                throw new WorkspaceAiToolArgumentException(
                    $"Unexpected or duplicate tool argument '{property.Name}'.");
            }
        }

        var missing = expectedSet.Where(name => !seen.Contains(name)).ToList();
        if (missing.Count > 0)
        {
            throw new WorkspaceAiToolArgumentException(
                $"Missing required tool argument(s): {string.Join(", ", missing)}.");
        }
    }

    private static string RequiredString(
        JsonElement arguments,
        string name,
        int maximumLength,
        bool allowEmpty = false,
        bool allowLineBreaks = false)
    {
        var element = arguments.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' must be a string.");
        }

        var value = element.GetString() ?? string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value.Length > maximumLength
            || value.IndexOf('\0') >= 0
            || value.Any(character => char.IsControl(character)
                && (!allowLineBreaks
                    || character is not '\r' and not '\n' and not '\t')))
        {
            throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' is malformed or exceeds its limit.");
        }

        return value;
    }

    private static string? NullableString(
        JsonElement arguments,
        string name,
        int maximumLength)
    {
        var element = arguments.GetProperty(name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String
            && string.Equals(
                element.GetString()?.Trim(),
                "null",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return RequiredString(
            arguments,
            name,
            maximumLength,
            allowEmpty: false);
    }

    private static int RequiredInteger(
        JsonElement arguments,
        string name,
        int minimum,
        int maximum)
    {
        var element = arguments.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value)
            || value < minimum
            || value > maximum)
        {
            throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' must be an integer from {minimum} through {maximum}.");
        }

        return value;
    }

    private static bool RequiredBoolean(
        JsonElement arguments,
        string name)
    {
        var element = arguments.GetProperty(name);
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' must be true or false.")
        };
    }

    private static string OptionalString(
        JsonElement arguments,
        string name,
        int maximumLength,
        bool allowLineBreaks = false,
        bool preserveWhitespace = false)
    {
        var element = arguments.GetProperty(name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' must be a string or null.");
        }

        var value = element.GetString() ?? string.Empty;
        if (value.Length > maximumLength
            || value.IndexOf('\0') >= 0
            || value.Any(character => char.IsControl(character)
                && (!allowLineBreaks
                    || character is not '\r' and not '\n' and not '\t')))
        {
            throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' is malformed or exceeds its limit.");
        }

        return preserveWhitespace ? value : value.Trim();
    }

    private static IReadOnlyList<string> RequiredStringArray(
        JsonElement arguments,
        string name,
        int maximumCount,
        int maximumItemLength)
    {
        var element = arguments.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() > maximumCount)
        {
            throw new WorkspaceAiToolArgumentException(
                $"Tool argument '{name}' must be an array with at most {maximumCount} items.");
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new WorkspaceAiToolArgumentException(
                    $"Every '{name}' item must be a string.");
            }

            var value = item.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > maximumItemLength
                || value.IndexOf('\0') >= 0
                || value.Any(char.IsControl))
            {
                throw new WorkspaceAiToolArgumentException(
                    $"A '{name}' item is malformed or exceeds its limit.");
            }

            values.Add(value.Trim());
        }

        return values;
    }

    private static WorkspaceAiGitInspectionKind ParseGitInspectionKind(
        string value) => value switch
        {
            "status" => WorkspaceAiGitInspectionKind.Status,
            "diff" => WorkspaceAiGitInspectionKind.Diff,
            "history" => WorkspaceAiGitInspectionKind.History,
            "commit" => WorkspaceAiGitInspectionKind.Commit,
            "commit_patch" => WorkspaceAiGitInspectionKind.CommitPatch,
            "conflict" => WorkspaceAiGitInspectionKind.Conflict,
            "branches" => WorkspaceAiGitInspectionKind.Branches,
            "tags" => WorkspaceAiGitInspectionKind.Tags,
            "remotes" => WorkspaceAiGitInspectionKind.Remotes,
            "stashes" => WorkspaceAiGitInspectionKind.Stashes,
            _ => throw new WorkspaceAiToolArgumentException(
                $"Unsupported Git inspection query '{value}'.")
        };

    private static WorkspaceAiGitMutationKind ParseGitMutationKind(
        string value) => value switch
        {
            "initialize" => WorkspaceAiGitMutationKind.Initialize,
            "clone" => WorkspaceAiGitMutationKind.Clone,
            "stage" => WorkspaceAiGitMutationKind.Stage,
            "unstage" => WorkspaceAiGitMutationKind.Unstage,
            "apply_patch" => WorkspaceAiGitMutationKind.ApplyPatch,
            "commit" => WorkspaceAiGitMutationKind.Commit,
            "checkout" => WorkspaceAiGitMutationKind.Checkout,
            "create_branch" => WorkspaceAiGitMutationKind.CreateBranch,
            "rename_branch" => WorkspaceAiGitMutationKind.RenameBranch,
            "delete_branch" => WorkspaceAiGitMutationKind.DeleteBranch,
            "create_tag" => WorkspaceAiGitMutationKind.CreateTag,
            "delete_tag" => WorkspaceAiGitMutationKind.DeleteTag,
            "add_remote" => WorkspaceAiGitMutationKind.AddRemote,
            "set_remote_url" => WorkspaceAiGitMutationKind.SetRemoteUrl,
            "remove_remote" => WorkspaceAiGitMutationKind.RemoveRemote,
            "fetch" => WorkspaceAiGitMutationKind.Fetch,
            "pull" => WorkspaceAiGitMutationKind.Pull,
            "push" => WorkspaceAiGitMutationKind.Push,
            "force_push" => WorkspaceAiGitMutationKind.ForcePush,
            "create_stash" => WorkspaceAiGitMutationKind.CreateStash,
            "apply_stash" => WorkspaceAiGitMutationKind.ApplyStash,
            "drop_stash" => WorkspaceAiGitMutationKind.DropStash,
            "merge" => WorkspaceAiGitMutationKind.Merge,
            "continue_merge" => WorkspaceAiGitMutationKind.ContinueMerge,
            "abort_merge" => WorkspaceAiGitMutationKind.AbortMerge,
            "rebase" => WorkspaceAiGitMutationKind.Rebase,
            "continue_rebase" => WorkspaceAiGitMutationKind.ContinueRebase,
            "skip_rebase" => WorkspaceAiGitMutationKind.SkipRebase,
            "abort_rebase" => WorkspaceAiGitMutationKind.AbortRebase,
            "revert" => WorkspaceAiGitMutationKind.Revert,
            "reset" => WorkspaceAiGitMutationKind.Reset,
            "cherry_pick" => WorkspaceAiGitMutationKind.CherryPick,
            "abort_cherry_pick" => WorkspaceAiGitMutationKind.AbortCherryPick,
            _ => throw new WorkspaceAiToolArgumentException(
                $"Unsupported Git operation '{value}'.")
        };

    private static GitDiffTarget ParseGitDiffTarget(string value) => value switch
    {
        "working_tree" => GitDiffTarget.WorkingTree,
        "index" => GitDiffTarget.Index,
        "commit" => GitDiffTarget.Commit,
        _ => throw new WorkspaceAiToolArgumentException(
            $"Unsupported Git diff target '{value}'.")
    };

    private static GitResetMode ParseGitResetMode(string value) => value switch
    {
        "soft" => GitResetMode.Soft,
        "mixed" => GitResetMode.Mixed,
        "hard" => GitResetMode.Hard,
        _ => throw new WorkspaceAiToolArgumentException(
            $"Unsupported Git reset mode '{value}'.")
    };

    private static WorkspaceAiLineSlice CreateLineSlice(
        string content,
        int startLine,
        int lineCount)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var startIndex = startLine - 1;
        if (startIndex >= lines.Length)
        {
            return new WorkspaceAiLineSlice(
                string.Empty,
                lines.Length,
                0,
                IsTruncated: false);
        }

        var selected = lines
            .Skip(startIndex)
            .Take(lineCount)
            .ToList();
        var joined = string.Join("\n", selected);
        var bounded = BoundText(joined);
        return new WorkspaceAiLineSlice(
            bounded.Content,
            lines.Length,
            selected.Count,
            bounded.IsTruncated
            || startIndex + selected.Count < lines.Length);
    }

    private static WorkspaceAiBoundedText BoundText(
        string content,
        int maximumCharacters = MaximumProviderTextCharacters) =>
        content.Length <= maximumCharacters
            ? new WorkspaceAiBoundedText(content, IsTruncated: false)
            : new WorkspaceAiBoundedText(
                content[..maximumCharacters],
                IsTruncated: true);

    private static WorkspaceAiToolOutcome VisualPageMutationOutcome(
        VisualPageMutationResult result,
        bool autoApplied) =>
        new(JsonSerializer.Serialize(new
        {
            ok = true,
            status = autoApplied ? "auto_applied" : "applied_after_review",
            previous_revision = result.PreviousRevision,
            revision = result.Revision,
            changes = result.Changes.Select(change => new
            {
                operation_id = change.OperationId,
                kind = change.Kind.ToString(),
                element_id = change.ElementId,
                summary = change.Summary
            }),
            publish_performed = false
        }));

    private static string BuildVisualPageMutationPreview(
        string expectedRevision,
        VisualPageMutationResult preview)
    {
        var lines = new List<string>
        {
            $"EXPECTED REVISION: {expectedRevision}",
            $"PROPOSED REVISION: {preview.Revision}",
            $"SEMANTIC OPERATIONS: {preview.Changes.Count}",
            string.Empty
        };
        lines.AddRange(preview.Changes.Select(change =>
            $"{change.Kind}: {change.Summary} [element {change.ElementId}]"));
        return string.Join(Environment.NewLine, lines);
    }

    private static bool ContainsCustomCode(
        VisualPageMutationOperation operation) =>
        operation.Element is not null
        && ContainsCustomCode(operation.Element);

    private static bool ContainsCustomCode(VisualPageElement element) =>
        element.IsCustomCode || element.Children.Any(ContainsCustomCode);

    private static string BuildBeforeAfterPreview(
        string target,
        string current,
        string proposed,
        string? currentSha256) =>
        $"TARGET: {target}\n"
        + $"CURRENT SHA-256: {currentSha256 ?? "new file"}\n"
        + $"CURRENT: {current.Length:N0} character(s)\n"
        + $"PROPOSED: {proposed.Length:N0} character(s)\n\n"
        + "--- CURRENT CONTENT ---\n"
        + current
        + (current.EndsWith('\n') || current.EndsWith('\r')
            ? string.Empty
            : "\n")
        + "--- PROPOSED CONTENT ---\n"
        + proposed;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record WorkspaceAiLineSlice(
        string Content,
        int TotalLines,
        int ReturnedLines,
        bool IsTruncated);

    private sealed record WorkspaceAiBoundedText(
        string Content,
        bool IsTruncated);

    private sealed class WorkspaceAiToolArgumentException : Exception
    {
        public WorkspaceAiToolArgumentException(string message)
            : base(message)
        {
        }
    }
}

public sealed class WorkspaceAiConversationCoordinator
{
    private readonly IWorkspaceAiProvider _provider;
    private readonly WorkspaceAiProviderConfigurationService
        _configurationService;
    private readonly WorkspaceAiContextService? _contextService;
    private readonly IWorkspaceAiCredentialStore _credentialStore;
    private readonly WorkspaceAiToolHost _toolHost;
    private readonly WorkspaceAiComputerControlService? _computerControl;
    private readonly Func<string, string?>? _workItemsContextProvider;
    private readonly List<JsonElement> _history = [];
    private readonly List<JsonElement> _providerNeutralHistory = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WorkspaceAiPendingReview? _pendingReview;
    private string _conversationId;
    private string _historyProviderId = string.Empty;
    private string _historyModelId = string.Empty;
    private WorkspaceAiSessionModelSelection _sessionModelSelection;
    private string _workingDirectory = string.Empty;
    private string _conversationPrimer = string.Empty;
    private bool _isHyperpageProjectWorkflow;
    private WorkspaceAiTaskRoute _taskRoute = new(
        "conversation",
        []);
    private WorkspaceAiRunDiagnosticSession? _activeDiagnostics;

    public WorkspaceAiConversationCoordinator(
        IWorkspaceAiProvider provider,
        WorkspaceAiProviderConfigurationService configurationService,
        WorkspaceAiContextService contextService,
        IWorkspaceAiCredentialStore credentialStore,
        WorkspaceAiToolHost toolHost,
        WorkspaceAiComputerControlService? computerControl = null,
        Func<string, string?>? workItemsContextProvider = null,
        string conversationId = "")
    {
        _provider = provider;
        _configurationService = configurationService;
        _contextService = contextService;
        _credentialStore = credentialStore;
        _toolHost = toolHost;
        _computerControl = computerControl;
        _workItemsContextProvider = workItemsContextProvider;
        _conversationId = conversationId;
        _sessionModelSelection = WorkspaceAiSessionModelSelection.Default;
    }

    public WorkspaceAiConversationCoordinator(
        IWorkspaceAiProvider provider,
        WorkspaceAiProviderConfigurationService configurationService,
        IWorkspaceAiCredentialStore credentialStore,
        WorkspaceAiToolHost toolHost,
        WorkspaceAiComputerControlService? computerControl = null,
        Func<string, string?>? workItemsContextProvider = null,
        string conversationId = "")
    {
        _provider = provider;
        _configurationService = configurationService;
        _credentialStore = credentialStore;
        _toolHost = toolHost;
        _computerControl = computerControl;
        _workItemsContextProvider = workItemsContextProvider;
        _conversationId = conversationId;
        _sessionModelSelection = WorkspaceAiSessionModelSelection.Default;
    }

    public WorkspaceAiPendingReview? PendingReview => _pendingReview;

    public WorkspaceAiSessionModelSelection SessionModelSelection =>
        _sessionModelSelection;

    public string WorkingDirectory => _workingDirectory;

    public bool IsHyperpageProjectWorkflow => _isHyperpageProjectWorkflow;

    public string LastDiagnosticsPath { get; private set; } = string.Empty;

    public void SetWorkingDirectory(string? workingDirectory)
    {
        if (!_gate.Wait(0))
        {
            throw new InvalidOperationException(
                "Wait for the current AI request before changing its working directory.");
        }

        try
        {
            _workingDirectory = WorkspaceAiWorkingDirectory.Normalize(
                workingDirectory,
                requireAvailable: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetSessionModelSelection(
        WorkspaceAiSessionModelSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var normalized = selection.ApplyTo(_configurationService.Load());
        _sessionModelSelection =
            WorkspaceAiSessionModelSelection.FromConfiguration(normalized);
    }

    public void SetConversationPrimer(string? primer)
    {
        if (!_gate.Wait(0))
        {
            throw new InvalidOperationException(
                "Wait for the current AI request before preparing project context.");
        }
        try
        {
            var normalized = (primer ?? string.Empty).Trim();
            if (normalized.Length > 32 * 1024 || normalized.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    "The project foundation context is malformed or exceeds 32 KiB.",
                    nameof(primer));
            }
            _conversationPrimer = normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetHyperpageProjectWorkflow(bool enabled)
    {
        if (!_gate.Wait(0))
        {
            throw new InvalidOperationException(
                "Wait for the current AI request before changing the project workflow.");
        }
        try
        {
            _isHyperpageProjectWorkflow = enabled;
            _taskRoute = enabled
                ? WorkspaceAiTaskToolRouter.CreateHyperpageProjectRoute()
                : new WorkspaceAiTaskRoute("conversation", []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void BindConversationId(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        _conversationId = conversationId.Trim();
    }

    public event EventHandler<WorkspaceAiConversationProgressEventArgs>?
        Progress;

    public async Task<WorkspaceAiConversationTurnResult> SendAsync(
        string workspaceId,
        string userText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userText)
            || userText.IndexOf('\0') >= 0)
        {
            throw new WorkspaceAiProviderException(
                "Enter a non-empty text prompt.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pendingReview is not null)
            {
                throw new WorkspaceAiProviderException(
                    "Approve or reject the pending action before sending another prompt.");
            }

            EnsureProviderConfigured();
            _computerControl?.ResetForNewRequest();
            RemoveLocalComputerScreenshots();

            var routeContext = _contextService?.GetContext(workspaceId);
            var routeComputerControl = routeContext is
            {
                AskBeforeChanges: false
            }
                ? routeContext.ComputerControl
                : WorkspaceAiComputerControlMode.Off;
            var detectedRoute = WorkspaceAiTaskToolRouter.Route(
                userText,
                routeComputerControl,
                _taskRoute);
            _taskRoute = _isHyperpageProjectWorkflow
                ? WorkspaceAiTaskToolRouter.MergeHyperpageProjectRoute(
                    detectedRoute,
                    routeComputerControl)
                : detectedRoute;

            _activeDiagnostics?.Dispose();
            _activeDiagnostics = WorkspaceAiRunDiagnosticSession.TryStart(
                _workingDirectory,
                _conversationId,
                workspaceId,
                _sessionModelSelection.ProviderId,
                _sessionModelSelection.ModelId,
                _taskRoute,
                userText);
            LastDiagnosticsPath =
                _activeDiagnostics?.RunDirectory ?? string.Empty;

            var workItemsContext = _workItemsContextProvider?.Invoke(
                workspaceId);
            var contextCards = new[]
                {
                    workItemsContext,
                    _conversationPrimer
                }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var contextPrefix = string.Join("\n\n", contextCards);
            var providerContent = string.IsNullOrWhiteSpace(contextPrefix)
                ? userText
                : $"{contextPrefix}\n\n[CURRENT_USER_REQUEST]\n{userText}";
            _conversationPrimer = string.Empty;
            AddMessageHistory("user", providerContent);
            AddProviderNeutralHistory("user", providerContent);
            try
            {
                var result = await ContinueAsync(
                    workspaceId,
                    cancellationToken);
                CompleteDiagnosticsWhenFinal(result);
                return result with
                {
                    DiagnosticsPath = LastDiagnosticsPath
                };
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                CompleteDiagnostics(
                    "cancelled",
                    string.Empty,
                    "The user or host cancelled the work cycle.");
                throw;
            }
            catch (Exception exception)
            {
                CompleteDiagnostics(
                    "failed",
                    string.Empty,
                    exception.Message);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceAiConversationTurnResult> ApproveAsync(
        bool userConfirmedExecution = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var review = _pendingReview
                         ?? throw new WorkspaceAiProviderException(
                             "There is no pending AI action to approve.");
            var outcome = await _toolHost.ApproveAsync(
                review,
                _conversationId,
                userConfirmedExecution,
                cancellationToken);
            _pendingReview = null;
            if (review.ContinueWithHostMessage)
            {
                AddPreparedTransferResult(
                    review.ToolName,
                    outcome.FunctionOutput);
            }
            else
            {
                AddFunctionOutput(review.CallId, outcome.FunctionOutput);
            }
            var localMessage = ReadToolMessage(outcome.FunctionOutput);
            try
            {
                var continued = await ContinueAsync(
                    review.WorkspaceId,
                    cancellationToken,
                    enforceHyperpageImplementation: false);
                var result = continued with
                {
                    AssistantText = JoinMessages(
                        localMessage,
                        continued.AssistantText),
                    DiagnosticsPath = LastDiagnosticsPath
                };
                CompleteDiagnosticsWhenFinal(result);
                return result;
            }
            catch (WorkspaceAiProviderException exception)
            {
                var result = new WorkspaceAiConversationTurnResult(
                    localMessage,
                    $"The reviewed action finished locally, but the AI response could not continue: {exception.Message}",
                    DiagnosticsPath: LastDiagnosticsPath);
                CompleteDiagnostics(
                    "failed",
                    localMessage,
                    exception.Message);
                return result;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceAiConversationTurnResult> RejectAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var review = _pendingReview
                         ?? throw new WorkspaceAiProviderException(
                             "There is no pending AI action to reject.");
            var outcome = _toolHost.Reject(review, _conversationId);
            _pendingReview = null;
            if (review.ContinueWithHostMessage)
            {
                AddPreparedTransferResult(
                    review.ToolName,
                    outcome.FunctionOutput);
            }
            else
            {
                AddFunctionOutput(review.CallId, outcome.FunctionOutput);
            }
            try
            {
                var continued = await ContinueAsync(
                    review.WorkspaceId,
                    cancellationToken,
                    enforceHyperpageImplementation: false);
                var result = continued with
                {
                    AssistantText = JoinMessages(
                        "Rejected the proposed action. No mutation was performed.",
                        continued.AssistantText),
                    DiagnosticsPath = LastDiagnosticsPath
                };
                CompleteDiagnosticsWhenFinal(result);
                return result;
            }
            catch (WorkspaceAiProviderException exception)
            {
                var result = new WorkspaceAiConversationTurnResult(
                    "Rejected the proposed action. No mutation was performed.",
                    $"The rejection was recorded locally, but the AI response could not continue: {exception.Message}",
                    DiagnosticsPath: LastDiagnosticsPath);
                CompleteDiagnostics(
                    "failed",
                    result.AssistantText,
                    exception.Message);
                return result;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Reset()
    {
        if (!_gate.Wait(0))
        {
            throw new InvalidOperationException(
                "Wait for the current AI request before starting a new conversation.");
        }

        try
        {
            _activeDiagnostics?.Dispose();
            _activeDiagnostics = null;
            _history.Clear();
            _providerNeutralHistory.Clear();
            _pendingReview = null;
            _historyProviderId = string.Empty;
            _historyModelId = string.Empty;
            _conversationPrimer = string.Empty;
            _isHyperpageProjectWorkflow = false;
            _taskRoute = new WorkspaceAiTaskRoute("conversation", []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void RestoreContinuationContext(string compactedContext)
    {
        if (!_gate.Wait(0))
        {
            throw new InvalidOperationException(
                "Wait for the current AI request before restoring a conversation.");
        }

        try
        {
            _history.Clear();
            _providerNeutralHistory.Clear();
            _pendingReview = null;
            _historyProviderId = string.Empty;
            _historyModelId = string.Empty;
            _conversationPrimer = string.Empty;
            if (string.IsNullOrWhiteSpace(compactedContext))
            {
                return;
            }

            if (compactedContext.Length
                > HypershellCloudContract
                    .MaximumConversationCompactionCharacters)
            {
                throw new InvalidDataException(
                    "The stored AI continuation context is too large.");
            }

            using var document = JsonDocument.Parse(compactedContext);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The stored AI continuation context is malformed.");
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryGetContinuationProperty(
                        item,
                        "role",
                        "Role",
                        out var role)
                    || role.ValueKind != JsonValueKind.String
                    || role.GetString() is not ("user" or "assistant")
                    || !TryGetContinuationProperty(
                        item,
                        "content",
                        "Content",
                        out var content)
                    || content.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(content.GetString()))
                {
                    throw new InvalidDataException(
                        "The stored AI continuation context contains an unsupported item.");
                }

                var roleValue = role.GetString()!;
                var contentValue = content.GetString()!;
                AddMessageHistory(roleValue, contentValue);
                AddProviderNeutralHistory(
                    roleValue,
                    contentValue);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The stored AI continuation context is not valid JSON.",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool TryGetContinuationProperty(
        JsonElement item,
        string canonicalName,
        string legacyName,
        out JsonElement value)
    {
        if (item.TryGetProperty(canonicalName, out value))
        {
            return true;
        }

        return item.TryGetProperty(legacyName, out value);
    }

    private async Task<WorkspaceAiConversationTurnResult> ContinueAsync(
        string workspaceId,
        CancellationToken cancellationToken,
        bool enforceHyperpageImplementation = true)
    {
        WorkspaceAiPreparedWorkspaceWrite? preparedWorkspaceWrite = null;
        WorkspaceAiPreparedVisualPageImport? preparedVisualPageImport = null;
        var sourceTransferFormatFailures = 0;
        var hyperpageManualDeliveryFailures = 0;
        var hyperpageMutationToolCalls = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = CreateContextConfiguration(workspaceId);
            EnsureProviderHistoryCompatibility(configuration);
            if (!configuration.IsLocal
                && !configuration.AllowCloudRequests)
            {
                throw new WorkspaceAiProviderException(
                    "Cloud AI requests are disabled. Enable them in Settings > AI Access.");
            }

            var apiKey = string.Empty;
            if (configuration.RequiresCredential)
            {
                var credential = _credentialStore.Load();
                if (credential is null)
                {
                    throw new WorkspaceAiProviderException(
                        "No device-protected OpenAI API key is saved. Add one in Settings > AI Access.");
                }

                apiKey = credential.ApiKey;
            }

            _activeDiagnostics?.Record("provider_request", new
            {
                providerId = configuration.ProviderId,
                model = configuration.Model,
                taskRoute = configuration.TaskRouteName,
                availableTools = configuration.EnabledToolNames,
                historyItemCount = _history.Count,
                input = _history
            });

            var response = await _provider.CreateResponseAsync(
                configuration,
                apiKey,
                _history,
                cancellationToken);

            _activeDiagnostics?.Record("provider_response", new
            {
                response.AssistantText,
                functionCalls = response.FunctionCalls.Select(call => new
                {
                    call.CallId,
                    call.Name,
                    call.Arguments
                }),
                computerCallCount = response.ComputerCalls.Count,
                outputItems = response.OutputItems
            });

            var toolCallCount = response.FunctionCalls.Count
                                + response.ComputerCalls.Count;
            if ((preparedWorkspaceWrite is not null
                 || preparedVisualPageImport is not null)
                && toolCallCount != 0)
            {
                sourceTransferFormatFailures++;
                if (sourceTransferFormatFailures >= 2)
                {
                    var message = preparedVisualPageImport is not null
                        ? "Engineer attempted another tool call during Hyperpage markup transfer twice. No page change was applied."
                        : preparedWorkspaceWrite!.TransferKind
                          == WorkspaceAiSourceTransferKind.ExactEdit
                            ? "Engineer attempted another tool call during exact-edit transfer twice. No file was changed."
                            : "Engineer attempted another tool call during source transfer twice. No file was written.";
                    throw new WorkspaceAiProviderException(message);
                }

                AddMessageHistory(
                    "user",
                    preparedVisualPageImport is not null
                        ? BuildTransferRetryMessage(
                            preparedVisualPageImport,
                            "The previous markup-transfer response attempted another tool call, so no tool ran and nothing changed.")
                        : BuildTransferRetryMessage(
                            preparedWorkspaceWrite!,
                            "The previous transfer response attempted another tool call, so no tool ran and nothing changed."));
                continue;
            }

            if (toolCallCount == 0)
            {
                if (preparedWorkspaceWrite is not null)
                {
                    AppendProviderOutputOrText(response);
                    if (!TryExtractSourceTransferContent(
                            response.AssistantText,
                            out var sourceContent))
                    {
                        sourceTransferFormatFailures++;
                        if (sourceTransferFormatFailures >= 2)
                        {
                            throw new WorkspaceAiProviderException(
                                preparedWorkspaceWrite.TransferKind
                                == WorkspaceAiSourceTransferKind.ExactEdit
                                    ? "Engineer returned no usable exact-edit payload after two attempts. No file was changed."
                                    : "Engineer returned no usable source content after two attempts. No file was written.");
                        }

                        AddMessageHistory(
                            "user",
                            BuildTransferRetryMessage(
                                preparedWorkspaceWrite,
                                "The previous transfer response was not usable, so nothing changed."));
                        continue;
                    }

                    var sourceOutcome = await _toolHost
                        .CompletePreparedWorkspaceWriteAsync(
                            preparedWorkspaceWrite,
                            sourceContent,
                            _conversationId,
                            cancellationToken);
                    _activeDiagnostics?.Record(
                        "source_transfer_completed",
                        new
                        {
                            preparedWorkspaceWrite.Target,
                            preparedWorkspaceWrite.TransferKind,
                            characterCount = sourceContent.Length,
                            sourceOutcome.FunctionOutput,
                            requiresReview = sourceOutcome.PendingReview
                                             is not null
                        });
                    preparedWorkspaceWrite = null;
                    if (sourceOutcome.PendingReview is not null)
                    {
                        _pendingReview = sourceOutcome.PendingReview;
                        return new WorkspaceAiConversationTurnResult(
                            string.Empty,
                            "Waiting for your explicit review. Nothing has been changed.",
                            sourceOutcome.PendingReview);
                    }

                    AddSourceTransferResult(sourceOutcome.FunctionOutput);
                    continue;
                }

                if (preparedVisualPageImport is not null)
                {
                    AppendProviderOutputOrText(response);
                    if (!TryExtractSourceTransferContent(
                            response.AssistantText,
                            out var markupContent))
                    {
                        sourceTransferFormatFailures++;
                        if (sourceTransferFormatFailures >= 2)
                        {
                            throw new WorkspaceAiProviderException(
                                "Engineer returned no usable Hyperpage markup after two attempts. No page change was applied.");
                        }

                        AddMessageHistory(
                            "user",
                            BuildTransferRetryMessage(
                                preparedVisualPageImport,
                                "The previous markup-transfer response was not usable, so nothing changed."));
                        continue;
                    }

                    var preparedImport = preparedVisualPageImport;
                    var importOutcome = await _toolHost
                        .CompletePreparedVisualPageImportAsync(
                            preparedImport,
                            markupContent,
                            _conversationId,
                            cancellationToken);
                    _activeDiagnostics?.Record(
                        "visual_page_markup_transfer_completed",
                        new
                        {
                            preparedImport.ProjectId,
                            preparedImport.PageId,
                            preparedImport.ParentElementId,
                            characterCount = markupContent.Length,
                            importOutcome.FunctionOutput,
                            requiresReview = importOutcome.PendingReview
                                             is not null
                        });
                    preparedVisualPageImport = null;
                    if (importOutcome.PendingReview is not null)
                    {
                        _pendingReview = importOutcome.PendingReview;
                        return new WorkspaceAiConversationTurnResult(
                            string.Empty,
                            "Waiting for your explicit review. Nothing has been changed.",
                            importOutcome.PendingReview);
                    }

                    AddVisualPageTransferResult(importOutcome.FunctionOutput);
                    continue;
                }

                if (enforceHyperpageImplementation
                    && _isHyperpageProjectWorkflow
                    && hyperpageMutationToolCalls == 0
                    && LooksLikeManualHyperpageCodeDelivery(
                        response.AssistantText))
                {
                    hyperpageManualDeliveryFailures++;
                    if (hyperpageManualDeliveryFailures >= 2)
                    {
                        throw new WorkspaceAiProviderException(
                            "The selected model returned manual-install code twice instead of using the available project editing tools. No unreviewed file change was claimed. Try the OpenAI model or a stronger local tool-calling model.");
                    }

                    AppendProviderOutputOrText(response);
                    AddProviderNeutralHistory(
                        "assistant",
                        response.AssistantText);
                    const string correction =
                        "[HYPERPAGE_HOST_CORRECTION]\nDo not give the user code to install manually. Inspect the project, then use exactly one enabled workspace or Hyperpage mutation tool now to create or update the actual project files. Honor Context permissions and stop at review when review is required. If a host restriction blocks the change, report that exact restriction instead of pasting a replacement implementation.\n[/HYPERPAGE_HOST_CORRECTION]";
                    AddMessageHistory("user", correction);
                    AddProviderNeutralHistory("user", correction);
                    _activeDiagnostics?.Record(
                        "hyperpage_manual_delivery_retry",
                        new
                        {
                            failure = hyperpageManualDeliveryFailures,
                            reason = "The model returned code for manual installation without requesting a project mutation."
                        });
                    continue;
                }

                AppendProviderOutput(response.OutputItems);
                var text = string.IsNullOrWhiteSpace(response.AssistantText)
                    ? "The provider completed without returning display text."
                    : response.AssistantText.Trim();
                AddProviderNeutralHistory("assistant", text);
                return new WorkspaceAiConversationTurnResult(
                    text,
                    configuration.IsLocal
                        ? $"Local response complete · {configuration.Model}"
                        : $"OpenAI response complete · {configuration.Model}");
            }

            if (toolCallCount != 1)
            {
                throw new WorkspaceAiProviderException(
                    "The provider returned multiple tool calls even though parallel tools are disabled.");
            }

            var progressText = response.AssistantText.Trim();
            if (!string.IsNullOrWhiteSpace(progressText))
            {
                Progress?.Invoke(
                    this,
                    new WorkspaceAiConversationProgressEventArgs(
                        progressText));
            }

            if (response.ComputerCalls.Count == 1)
            {
                var computerControl = _computerControl
                                      ?? throw new WorkspaceAiProviderException(
                                          "Computer control is not available in this Hypershell session.");
                var computerCall = response.ComputerCalls[0];
                var computerOutput = await computerControl.ExecuteAsync(
                    workspaceId,
                    computerCall,
                    cancellationToken);
                _activeDiagnostics?.Record("computer_tool_result", new
                {
                    computerOutput
                });
                AppendProviderOutput(response.OutputItems);
                AddHistory(computerOutput);
                continue;
            }

            var call = response.FunctionCalls[0];
            if (IsHyperpageMutationTool(call.Name))
            {
                hyperpageMutationToolCalls++;
            }
            if (string.Equals(
                    call.Name,
                    WorkspaceAiWindowsUiFunctionTool.FunctionName,
                    StringComparison.Ordinal))
            {
                var computerControl = _computerControl
                                      ?? throw new WorkspaceAiProviderException(
                                          "Windows UI control is not available in this Hypershell session.");
                var functionOutput = await computerControl
                    .ExecuteWindowsUiFunctionAsync(
                        workspaceId,
                        call,
                        cancellationToken);
                _activeDiagnostics?.Record("tool_result", new
                {
                    call.Name,
                    call.Arguments,
                    output = functionOutput
                });
                AppendProviderOutput(response.OutputItems);
                AddFunctionOutput(call.CallId, functionOutput);
                continue;
            }

            if (configuration.IsLocal
                && string.Equals(
                    call.Name,
                    WorkspaceAiComputerFunctionTool.FunctionName,
                    StringComparison.Ordinal))
            {
                var computerControl = _computerControl
                                      ?? throw new WorkspaceAiProviderException(
                                          "Computer control is not available in this Hypershell session.");
                var computerResult = await computerControl
                    .ExecuteFunctionAsync(
                        workspaceId,
                        call,
                        cancellationToken);
                _activeDiagnostics?.Record("tool_result", new
                {
                    call.Name,
                    call.Arguments,
                    output = computerResult.FunctionOutput
                });
                AppendProviderOutput(response.OutputItems);
                AddFunctionOutput(call.CallId, computerResult.FunctionOutput);
                ReplaceLocalComputerScreenshot(
                    computerResult.ScreenshotInput);
                continue;
            }

            var outcome = await _toolHost.ExecuteAsync(
                workspaceId,
                call,
                _conversationId,
                _workingDirectory,
                cancellationToken);
            _activeDiagnostics?.Record("tool_result", new
            {
                call.Name,
                call.Arguments,
                outcome.FunctionOutput,
                requiresReview = outcome.PendingReview is not null,
                preparedSourceTransfer = outcome.PreparedWorkspaceWrite
                                         is not null,
                preparedVisualPageImport = outcome.PreparedVisualPageImport
                                           is not null
            });
            AppendProviderOutput(response.OutputItems);
            if (outcome.PendingReview is not null)
            {
                _pendingReview = outcome.PendingReview;
                return new WorkspaceAiConversationTurnResult(
                    string.Empty,
                    "Waiting for your explicit review. Nothing has been changed.",
                    outcome.PendingReview);
            }

            AddFunctionOutput(call.CallId, outcome.FunctionOutput);
            if (outcome.PreparedWorkspaceWrite is not null)
            {
                preparedWorkspaceWrite = outcome.PreparedWorkspaceWrite;
                sourceTransferFormatFailures = 0;
            }
            if (outcome.PreparedVisualPageImport is not null)
            {
                preparedVisualPageImport = outcome.PreparedVisualPageImport;
                sourceTransferFormatFailures = 0;
            }
        }
    }

    private static bool LooksLikeManualHyperpageCodeDelivery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return normalized.Contains("```", StringComparison.Ordinal)
               || normalized.Contains("copy and paste", StringComparison.Ordinal)
               || normalized.Contains("copy/paste", StringComparison.Ordinal)
               || normalized.Contains("add this code", StringComparison.Ordinal)
               || normalized.Contains("put this code", StringComparison.Ordinal)
               || normalized.Contains("create the following file", StringComparison.Ordinal)
               || normalized.Contains("update the following file", StringComparison.Ordinal)
               || normalized.Contains("files above", StringComparison.Ordinal)
               || normalized.Contains("manually add", StringComparison.Ordinal);
    }

    private static bool IsHyperpageMutationTool(string toolName) => toolName is
        "prepare_workspace_write"
        or "prepare_workspace_edit"
        or "propose_workspace_write"
        or "propose_active_document_replace"
        or "propose_visual_page_changes"
        or "prepare_visual_page_markup";

    private void EnsureProviderConfigured()
    {
        var configuration = _sessionModelSelection.ApplyTo(
            _configurationService.Load());
        if (!configuration.IsLocal
            && !configuration.AllowCloudRequests)
        {
            throw new WorkspaceAiProviderException(
                "Cloud AI requests are disabled. Enable them in Settings > AI Access.");
        }

        if (configuration.RequiresCredential
            && !_credentialStore.Exists)
        {
            throw new WorkspaceAiProviderException(
                "No device-protected OpenAI API key is saved. Add one in Settings > AI Access.");
        }
    }

    private WorkspaceAiProviderConfiguration CreateContextConfiguration(
        string contextId)
    {
        var configuration = _sessionModelSelection.ApplyTo(
            _configurationService.Load());
        if (_contextService is null)
        {
            return WorkspaceAiProviderConfigurationService.Normalize(
                configuration with
                {
                    ComputerControl = WorkspaceAiComputerControlMode.Off
                });
        }

        var context = _contextService.GetContext(contextId)
                      ?? throw new WorkspaceAiProviderException(
                          "The selected AI context is no longer available.");
        if (context.RequiresRiskWarning
            && !context.RiskWarningAcknowledged)
        {
            throw new WorkspaceAiProviderException(
                "Review and save this Context once on this PC before its unrestricted permissions can be used.");
        }

        var computerControl = !context.AskBeforeChanges
            ? context.ComputerControl
            : WorkspaceAiComputerControlMode.Off;
        var normalized = WorkspaceAiProviderConfigurationService.Normalize(
            configuration with
            {
                Model = WorkspaceAiModelCatalog.ResolveContextModel(
                    configuration,
                    _sessionModelSelection.ModelId,
                    computerControl),
                ReasoningEffort = context.ReasoningEffort,
                TextVerbosity = context.TextVerbosity,
                ComputerControl = computerControl
            });
        return normalized with
        {
            RuntimeEnvironmentContext = BuildRuntimeEnvironmentContext(
                context,
                normalized,
                _workingDirectory,
                _taskRoute),
            TaskRouteName = _taskRoute.Name,
            EnabledToolNames = _taskRoute.ToolNames
        };
    }

    private static string BuildRuntimeEnvironmentContext(
        WorkspaceAiContextDefinition context,
        WorkspaceAiProviderConfiguration configuration,
        string workingDirectory,
        WorkspaceAiTaskRoute taskRoute)
    {
        var model = WorkspaceAiModelCatalog.Find(configuration.Model);
        var modelRole = model?.Role.ToString() ?? "Cloud assistant";
        var vision = model is null
            ? "provider-managed"
            : model.SupportsVision ? "available" : "unavailable";
        var projectRoot = string.IsNullOrWhiteSpace(workingDirectory)
            ? "not selected"
            : WorkspaceAiWorkingDirectory.Display(workingDirectory);
        var availableTools = taskRoute.ToolNames.Count == 0
            ? "none"
            : string.Join(",", taskRoute.ToolNames);
        return $"""
            [HYPERSHELL_RUNTIME_ENVIRONMENT]
            host=Hypershell on Windows
            context={context.Name}
            provider={(configuration.IsLocal ? "local llama.cpp" : "OpenAI")}
            resolved_model={configuration.Model}
            model_role={modelRole}
            vision={vision}
            computer_control={configuration.ComputerControl}
            windows_ui_automation={(configuration.ComputerControl == WorkspaceAiComputerControlMode.Off ? "unavailable" : "available")}
            change_policy={(context.AskBeforeChanges ? "review before changes" : "standing Context permission")}
            terminal_execution_policy={(context.AskBeforeChanges ? "review before execution" : "auto-execute permitted commands")}
            read_scope={DescribePermission(context.Read)}
            create_scope={DescribePermission(context.Create)}
            edit_scope={DescribePermission(context.Edit)}
            delete_scope={DescribePermission(context.Delete)}
            terminal_scope={DescribePermission(context.Terminal)}
            internet={(context.AllowInternet ? "allowed" : "blocked")}
            working_directory={projectRoot}
            task_route={taskRoute.Name}
            available_tools={availableTools}
            Resolve every relative file, repository, and terminal action against working_directory. Never substitute another project root. If working_directory is not selected, ask the user to choose one before attempting relative project work.
            Treat this host-generated card as authoritative. Never claim a listed capability is unavailable without first attempting its matching host tool.
            [/HYPERSHELL_RUNTIME_ENVIRONMENT]
            """;
    }

    private static string DescribePermission(
        WorkspaceAiContextPathPermission permission) =>
        permission.Mode == WorkspaceAiContextAccessMode.FullAccess
            ? "full device and linked workspaces"
            : string.IsNullOrWhiteSpace(permission.Location)
              && permission.WorkspaceUris.Count == 0
                ? "no location selected"
                : "selected locations only";

    private void AddFunctionOutput(string callId, string output) =>
        AddHistory(new
        {
            type = "function_call_output",
            call_id = callId,
            output
        });

    private void AddSourceTransferResult(string output) =>
        AddMessageHistory(
            "user",
            $"[HYPERSHELL_SOURCE_TRANSFER_RESULT_V1]\n{output}");

    private void AddVisualPageTransferResult(string output) =>
        AddMessageHistory(
            "user",
            $"[HYPERSHELL_VISUAL_PAGE_MARKUP_TRANSFER_RESULT_V1]\n{output}");

    private void AddPreparedTransferResult(
        string toolName,
        string output)
    {
        if (string.Equals(
                toolName,
                "prepare_visual_page_markup",
                StringComparison.Ordinal))
        {
            AddVisualPageTransferResult(output);
            return;
        }

        AddSourceTransferResult(output);
    }

    private static string BuildTransferRetryMessage(
        WorkspaceAiPreparedWorkspaceWrite prepared,
        string reason) =>
        prepared.TransferKind == WorkspaceAiSourceTransferKind.ExactEdit
            ? $"{WorkspaceAiToolHost.ExactEditTransferRequestMarker}\n{reason} Retry once. Return only one or more exact <<<<<<< SEARCH / ======= / >>>>>>> REPLACE blocks. Copy SEARCH text exactly from the current file and do not use JSON, commentary, fences, or tools."
            : $"{WorkspaceAiToolHost.SourceTransferRequestMarker}\n{reason} Retry once. Return only the exact file content as ordinary response text. Do not use JSON, commentary, or tools.";

    private static string BuildTransferRetryMessage(
        WorkspaceAiPreparedVisualPageImport prepared,
        string reason) =>
        $"{WorkspaceAiToolHost.VisualPageMarkupTransferRequestMarker}\n{reason} Retry once. Return only one complete {((prepared.Razor) ? "Razor" : "HTML")} body fragment as ordinary response text. Include a scoped style element when CSS is needed. Do not use JSON, commentary, wrappers, or tools.";

    private static bool TryExtractSourceTransferContent(
        string response,
        out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(response))
        {
            return false;
        }

        var begin = response.IndexOf(
            WorkspaceAiToolHost.SourceTransferBeginMarker,
            StringComparison.Ordinal);
        var end = response.LastIndexOf(
            WorkspaceAiToolHost.SourceTransferEndMarker,
            StringComparison.Ordinal);
        if (begin >= 0 && end > begin)
        {
            var prefix = response[..begin];
            var contentStart = begin
                               + WorkspaceAiToolHost
                                   .SourceTransferBeginMarker.Length;
            var suffixStart = end
                              + WorkspaceAiToolHost
                                  .SourceTransferEndMarker.Length;
            var suffix = response[suffixStart..];
            if (string.IsNullOrWhiteSpace(prefix)
                && string.IsNullOrWhiteSpace(suffix))
            {
                if (contentStart < end && response[contentStart] == '\r')
                {
                    contentStart++;
                }

                if (contentStart < end && response[contentStart] == '\n')
                {
                    contentStart++;
                }

                content = response[contentStart..end];
                return true;
            }
        }

        // Source Transfer v2 treats the complete no-tool response as the
        // payload. This avoids sending code through either JSON arguments or
        // a fragile exact-marker protocol. Retain compatibility with models
        // that insist on one Markdown fence.
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal)
            && trimmed.EndsWith("```", StringComparison.Ordinal)
            && trimmed.Length > 6)
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf(
                "```",
                StringComparison.Ordinal);
            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
            {
                content = trimmed[(firstLineEnd + 1)..closingFence]
                    .TrimEnd('\r', '\n');
                return true;
            }
        }

        if (!trimmed.Contains('\n')
            && trimmed.Length < 512
            && new[]
            {
                "here is", "here's", "sure", "certainly", "below is",
                "i created", "i've created", "the file", "let me"
            }.Any(prefix => trimmed.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        content = response;
        return true;
    }

    private void ReplaceLocalComputerScreenshot(JsonElement screenshotInput)
    {
        RemoveLocalComputerScreenshots();
        AddHistory(screenshotInput);
    }

    private void RemoveLocalComputerScreenshots()
    {
        for (var index = _history.Count - 1; index >= 0; index--)
        {
            if (IsLocalComputerScreenshot(_history[index]))
            {
                _history.RemoveAt(index);
            }
        }
    }

    private static bool IsLocalComputerScreenshot(JsonElement item)
    {
        if (!item.TryGetProperty("role", out var role)
            || role.ValueKind != JsonValueKind.String
            || role.GetString() != "user"
            || !item.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return content.EnumerateArray().Any(part =>
            part.ValueKind == JsonValueKind.Object
            && part.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "input_text"
            && part.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
            && (text.GetString() ?? string.Empty).StartsWith(
                WorkspaceAiComputerFunctionTool.ScreenshotMarker,
                StringComparison.Ordinal));
    }

    private void AppendProviderOutput(
        IReadOnlyList<JsonElement> outputItems)
    {
        foreach (var outputItem in outputItems)
        {
            AddHistory(outputItem);
        }

        PruneHistoryBeforeLatestCompaction();
    }

    private void AppendProviderOutputOrText(
        WorkspaceAiProviderResponse response)
    {
        if (response.OutputItems.Count > 0)
        {
            AppendProviderOutput(response.OutputItems);
            return;
        }

        AddMessageHistory("assistant", response.AssistantText);
    }

    private void PruneHistoryBeforeLatestCompaction()
    {
        var latestCompactionIndex = -1;
        for (var index = _history.Count - 1; index >= 0; index--)
        {
            var item = _history[index];
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                && string.Equals(
                    typeElement.GetString(),
                    "compaction",
                    StringComparison.Ordinal))
            {
                latestCompactionIndex = index;
                break;
            }
        }

        // For stateless Responses API chaining, OpenAI's compaction item is
        // self-contained. Keep it and every newer output item, and discard
        // only the history the provider has already compacted.
        if (latestCompactionIndex > 0)
        {
            _history.RemoveRange(0, latestCompactionIndex);
        }
    }

    private void AddHistory(object value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        AddHistory(element);
    }

    private void AddMessageHistory(string role, string content) =>
        AddHistory(new
        {
            type = "message",
            role,
            content
        });

    private void AddHistory(JsonElement value)
    {
        _history.Add(value.Clone());
    }

    private void EnsureProviderHistoryCompatibility(
        WorkspaceAiProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(_historyProviderId))
        {
            _historyProviderId = configuration.ProviderId;
            _historyModelId = configuration.Model;
            return;
        }

        if (string.Equals(
                _historyProviderId,
                configuration.ProviderId,
                StringComparison.Ordinal)
            && string.Equals(
                _historyModelId,
                configuration.Model,
                StringComparison.Ordinal))
        {
            return;
        }

        _history.Clear();
        _history.AddRange(_providerNeutralHistory.Select(item =>
            item.Clone()));
        _historyProviderId = configuration.ProviderId;
        _historyModelId = configuration.Model;
    }

    private void AddProviderNeutralHistory(string role, string content)
    {
        const int maximumItems = 256;
        _providerNeutralHistory.Add(JsonSerializer.SerializeToElement(new
        {
            type = "message",
            role,
            content
        }));
        if (_providerNeutralHistory.Count > maximumItems)
        {
            _providerNeutralHistory.RemoveRange(
                0,
                _providerNeutralHistory.Count - maximumItems);
        }
    }

    private static string ReadToolMessage(string functionOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(functionOutput);
            if (document.RootElement.TryGetProperty(
                    "message",
                    out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "The reviewed action finished.";
            }
        }
        catch (JsonException)
        {
        }

        return "The reviewed action finished.";
    }

    private static string JoinMessages(string first, string second) =>
        string.IsNullOrWhiteSpace(second)
            ? first
            : $"{first}\n\n{second}";

    private void CompleteDiagnosticsWhenFinal(
        WorkspaceAiConversationTurnResult result)
    {
        if (result.PendingReview is null)
        {
            CompleteDiagnostics(
                "succeeded",
                result.AssistantText,
                string.Empty);
        }
        else
        {
            _activeDiagnostics?.Record("review_required", new
            {
                result.PendingReview.Kind,
                result.PendingReview.ToolName,
                result.PendingReview.Target,
                result.PendingReview.Summary
            });
        }
    }

    private void CompleteDiagnostics(
        string status,
        string response,
        string error)
    {
        var diagnostics = _activeDiagnostics;
        if (diagnostics is null)
        {
            return;
        }

        diagnostics.Record("cycle_completed", new
        {
            status,
            error
        });
        diagnostics.Complete(status, response, error);
        diagnostics.Dispose();
        _activeDiagnostics = null;
    }
}

public sealed class WorkspaceAiConversationProgressEventArgs : EventArgs
{
    public WorkspaceAiConversationProgressEventArgs(string assistantText)
    {
        AssistantText = assistantText;
    }

    public string AssistantText { get; }
}
