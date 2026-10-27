using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hypershell.Services;

public sealed class WorkspaceAiProviderException : Exception
{
    public WorkspaceAiProviderException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed record WorkspaceAiFunctionCall(
    string CallId,
    string Name,
    string Arguments);

public sealed record WorkspaceAiComputerAction(JsonElement Payload)
{
    public string Type => Payload.GetProperty("type").GetString()
                          ?? string.Empty;
}

public sealed record WorkspaceAiComputerCall(
    string CallId,
    IReadOnlyList<WorkspaceAiComputerAction> Actions);

public sealed record WorkspaceAiProviderResponse(
    IReadOnlyList<JsonElement> OutputItems,
    string AssistantText,
    IReadOnlyList<WorkspaceAiFunctionCall> FunctionCalls,
    IReadOnlyList<WorkspaceAiComputerCall> ComputerCalls,
    IReadOnlyList<WorkspaceAiCitation>? Citations = null,
    IReadOnlyList<WorkspaceAiResearchSource>? Sources = null,
    string Status = "completed");

public sealed record WorkspaceAiCitation(string Url, string Title, int StartIndex, int EndIndex);
public sealed record WorkspaceAiResearchSource(string Url, string Title);
public sealed record WorkspaceAiRequestProfile(
    string Instructions,
    IReadOnlyList<JsonElement> FunctionDefinitions,
    bool HostedWebSearch = false);

public interface IWorkspaceAiProvider
{
    Task<WorkspaceAiProviderResponse> CreateResponseAsync(
        WorkspaceAiProviderConfiguration configuration,
        string apiKey,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken = default);
}

public sealed class ResponsesWorkspaceAiProvider
    : IWorkspaceAiProvider,
      IDisposable
{
    // These are transport-allocation guards, not conversation limits. The
    // Responses API's token-aware compaction below governs conversation state.
    public const int MaximumRequestBytes = 64 * 1024 * 1024;
    public const int MaximumResponseBytes = 64 * 1024 * 1024;
    public const int CompactThresholdTokens = 800_000;

    private const string LocalToolCallRecoveryMarker =
        "[HYPERSHELL_TOOL_CALL_RECOVERY]";
    private const string LocalToolCallRecoveryInstruction =
        LocalToolCallRecoveryMarker
        + " Your preceding response was rejected before any tool ran because its function arguments were not valid JSON. Retry the pending task now with exactly one small, syntactically valid tool call. For a multi-section Hyperpage or full-page foundation, call prepare_visual_page_markup; Hypershell will request raw HTML or Razor separately. For a narrow change to an existing source file, call prepare_workspace_edit with only the path and current SHA. For a new file or intentional full rewrite, call prepare_workspace_write. Hypershell will request the edit or content separately. Do not put HTML, source code, a here-string, or a heredoc in JSON tool arguments or a terminal command. Use execute_terminal_command only for an actual shell command. Do not narrate this recovery.";

    private const string VisualPageArgumentRecoveryInstructions =
        "HYPERSHELL HYPERPAGE RECOVERY: The previous semantic page-operation arguments were incomplete or malformed. Call prepare_visual_page_markup exactly once with the current revision, target parent element ID, markup language, and a short reason. Do not retry propose_visual_page_changes, include markup in JSON, or narrate the recovery.";

    private const string SourceTransferInstructions =
        "HYPERSHELL SOURCE TRANSFER MODE: Return only the exact source-file content as ordinary response text. Do not call a tool, add an explanation, or JSON-escape the file. A single Markdown fence is tolerated by the host but should be omitted. The host captures this bounded response, then re-runs path authorization, SHA preconditions, review policy, and the atomic write. Preserve quotes, backslashes, HTML, CSS, JavaScript, JSON, and line breaks exactly as they belong in the file.";

    private const string ExactEditTransferInstructions =
        "HYPERSHELL EXACT EDIT TRANSFER MODE: Return only one or more exact edit blocks using these three marker lines: <<<<<<< SEARCH, =======, and >>>>>>> REPLACE. Copy each SEARCH section exactly from the current file and include enough unchanged context to make it unique. Put replacement text after =======; an empty replacement deletes the matched text. Do not call a tool, add commentary, use JSON, or wrap the blocks in a Markdown fence. The host applies the blocks outside JSON, then re-runs path authorization, the current-file SHA precondition, review policy, and the atomic write.";

    private const string VisualPageMarkupTransferInstructions =
        "HYPERSHELL HYPERPAGE MARKUP TRANSFER MODE: Return only one complete HTML or Razor body fragment as ordinary response text. Do not call a tool, add an explanation, or JSON-escape the markup. A single Markdown fence is tolerated by the host but should be omitted. For a visually designed page, include a scoped style element in the fragment. The host imports supported markup into semantic Hyperpage elements, preserves unsupported constructs as reviewable custom-code nodes, checks the exact page revision again, previews the result, and obeys the selected Context review policy. Do not include html, head, or body wrapper elements.";

    private const string Instructions =
        "You are the AI workspace assistant embedded in Hypershell. "
        + "Use only the host-owned tools provided in this request to inspect or affect the selected Context. "
        + "Use search_hyperlib before guessing a command, language feature, framework API, or reusable syntax that may already be documented in Hypershell's validated library catalog. "
        + "Live-assistance events mean the host detected activity. Inspect current state with the available tools and use any Context-authorized tool when it would materially help. "
        + "When a user turn contains a HYPERSHELL_PROJECT_WORK_ITEMS block, treat it as an ordered secondary backlog: stay focused on the current request, opportunistically include an aligned item, prefer starred items within the next one to three work cycles, and never claim an item is complete unless it was actually implemented and verified. "
        + "When the computer tool is available, its host-enforced scope is defined by the selected Context and may be either Hypershell or the full Windows desktop. Prefer semantic editor, file, and terminal tools when they are more precise. "
        + "When control_windows_ui is available, inspect first, then prefer its named controls and snapshot-scoped element IDs for standard Windows buttons, fields, menus, lists, and windows. Re-inspect after every UI change because IDs expire with each returned snapshot. If the UI Automation snapshot is unavailable or omits a custom-rendered control, fall back to control_computer. "
        + "When control_computer is available, begin with one screenshot action, inspect the host-attached screenshot, and use only coordinates within its stated pixel bounds. The host returns a fresh screenshot after every action batch. "
        + "Never invent file contents, command results, or mutation success. "
        + "For an active Hyperpage Studio tab, use read_active_visual_page and validate_visual_page. For a multi-section landing page, a full-page foundation, or any change requiring more than four semantic operations, call prepare_visual_page_markup with the exact revision and parent element ID; the host will request raw HTML or Razor in a separate no-tools response and import it safely. Use propose_visual_page_changes only for one to four focused operations with stable element IDs and the exact current revision. The host previews semantic changes, highlights affected elements, and applies only through Context review policy. build_visual_page_preview is a separate explicit-trust action that can only build an isolated cache snapshot; no AI tool can host, publish, change a firewall, or activate an Atlas release. "
        + "File, editor, delete, terminal, and internet operations obey the independently scoped host policy in the selected Context. "
        + "Local paths and canonical ssh:// workspace URIs are both supported when included in the Context. Preserve the workspace URI returned by remote tools in follow-up calls. "
        + "Use the semantic Git tools for repository inspection and mutation instead of composing raw git terminal commands; they route local and linked-SSH workspaces through the same audited repository adapter as the Source Control panel. "
        + "The host either creates an exact review or applies the SHA-guarded action under standing permission. Trust only the returned function output. "
        + "For coding and other requested work, use search_workspace_text for repository-wide discovery instead of repeatedly listing directories or guessing where symbols live. Read the relevant ranges before changing them. Prefer prepare_workspace_edit for narrow changes to existing files; it transfers exact SEARCH/REPLACE blocks outside JSON and keeps the current SHA precondition. Use prepare_workspace_write for new files or intentional full-file rewrites. Never put a file body in a terminal here-string or heredoc. Use propose_workspace_write only for short text that is safe to express in one JSON string. expected_sha256 is null when creating a file and is the current hash returned by read_workspace_text when replacing a file; it is never a hash of the proposed content. Call run_project_check for conventional build, test, lint, or typecheck work when the project has a recognized manifest; it selects the command, obeys terminal policy, waits for completion, and returns bounded terminal output. Use execute_terminal_command for commands that are not covered by a semantic host tool; the host's saved policy decides whether execution is immediate or pauses for review. Use stage_terminal_command only when the user explicitly asks to place a command in the terminal without running it. Never ask for permission that the selected Context already grants. For a coding task, follow inspect, edit, verify, inspect failures, and iterate before reporting completion. "
        + "Prefer narrow reads, preserve existing style, and clearly summarize what you learned or propose.";

    private static readonly JsonArray ToolDefinitions =
        JsonNode.Parse(
            """
            [
              {
                "type": "function",
                "name": "search_hyperlib",
                "description": "Semantically search Hypershell's static Hyperlib command and programming-reference catalog. Results include source, provenance, and review metadata and do not execute library content.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string", "minLength": 1, "maxLength": 1000 },
                    "limit": { "type": "integer", "minimum": 1, "maximum": 20 }
                  },
                  "required": ["query", "limit"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "list_workspace",
                "description": "List one directory allowed by the selected Context. Use '.', an allowed absolute local path, or a canonical ssh:// workspace URI returned by Hypershell.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "relative_path": { "type": "string", "minLength": 1, "maxLength": 1024 }
                  },
                  "required": ["relative_path"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "search_workspace_text",
                "description": "Recursively search bounded text files under the conversation working directory or a narrower allowed local/SSH path. Generated, dependency, VCS, large, and binary content is skipped. Use '*' or semicolon-separated simple patterns such as '*.cs;*.xaml'.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "relative_path": { "type": "string", "minLength": 1, "maxLength": 1024 },
                    "query": { "type": "string", "minLength": 1, "maxLength": 512 },
                    "file_pattern": { "type": "string", "minLength": 1, "maxLength": 2048 },
                    "use_regex": { "type": "boolean" },
                    "case_sensitive": { "type": "boolean" }
                  },
                  "required": ["relative_path", "query", "file_pattern", "use_regex", "case_sensitive"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "read_workspace_text",
                "description": "Read a bounded line range from a text file allowed by the selected Context. Use a local path or canonical ssh:// workspace URI.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "relative_path": { "type": "string", "minLength": 1, "maxLength": 1024 },
                    "start_line": { "type": "integer", "minimum": 1, "maximum": 10000000 },
                    "line_count": { "type": "integer", "minimum": 1, "maximum": 400 }
                  },
                  "required": ["relative_path", "start_line", "line_count"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "read_active_document",
                "description": "Read the live content and typed-provider analysis of the active editor document when it is inside this conversation's working directory and the selected Context allows it. Unsaved documents resolve against the conversation working directory.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {},
                  "required": [],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "read_active_visual_page",
                "description": "Read the active Hyperpage Studio model, stable element IDs, current revision, selection, and host-owned validation diagnostics. This exposes no DOM, WebView, preview process, hosting, or publication capability.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {},
                  "required": [],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "validate_visual_page",
                "description": "Validate the active visual-page tree and accessibility rules using Hypershell-owned code. Supply the exact revision from read_active_visual_page so stale validation is rejected.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "expected_revision": { "type": "string", "minLength": 1, "maxLength": 128 }
                  },
                  "required": ["expected_revision"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "propose_visual_page_changes",
                "description": "Propose 1-4 focused semantic operations against the exact active visual-page revision. Use stable element IDs from read_active_visual_page. For a multi-section page, full-page foundation, or larger change, use prepare_visual_page_markup instead so markup is transferred outside JSON. The host validates on a clone, highlights affected elements, and uses the normal editor review or auto-apply policy. This tool cannot publish.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "expected_revision": { "type": "string", "minLength": 1, "maxLength": 128 },
                    "reason": { "type": "string", "minLength": 1, "maxLength": 1000 },
                    "operations": {
                      "type": "array",
                      "minItems": 1,
                      "maxItems": 4,
                      "items": {
                        "type": "object",
                        "properties": {
                          "operationId": { "type": "string", "minLength": 1, "maxLength": 128 },
                          "kind": { "type": "string", "enum": ["InsertChild", "RemoveElement", "MoveElement", "DuplicateElement", "WrapElement", "UnwrapElement", "SetText", "SetAttribute", "RemoveAttribute", "SetStyle", "RemoveStyle", "SetLayout", "SetBinding", "RemoveBinding", "SetEvent", "RemoveEvent", "SetLocked", "SetHidden", "AddState", "RemoveState"] },
                          "targetElementId": { "type": "string", "maxLength": 128 },
                          "parentElementId": { "type": "string", "maxLength": 128 },
                          "index": { "type": ["integer", "null"], "minimum": 0, "maximum": 10000 },
                          "name": { "type": "string", "maxLength": 512 },
                          "value": { "type": ["string", "null"], "maxLength": 131072 },
                          "flag": { "type": ["boolean", "null"] },
                          "breakpoint": { "type": "string", "enum": ["Base", "Small", "Medium", "Large", "ExtraLarge"] },
                          "element": {
                            "type": ["object", "null"],
                            "properties": {
                              "elementId": { "type": "string", "minLength": 1, "maxLength": 128 },
                              "descriptorId": { "type": "string", "minLength": 1, "maxLength": 256 },
                              "name": { "type": "string", "minLength": 1, "maxLength": 512 },
                              "text": { "type": "string", "maxLength": 131072 },
                              "isLocked": { "type": "boolean" },
                              "isHidden": { "type": "boolean" },
                              "customRazor": { "type": ["string", "null"], "maxLength": 131072 }
                            },
                            "required": ["elementId", "descriptorId", "name", "text", "isLocked", "isHidden", "customRazor"],
                            "additionalProperties": false
                          },
                          "layout": {
                            "type": ["object", "null"],
                            "properties": {
                              "display": { "type": "string", "maxLength": 128 },
                              "flexDirection": { "type": "string", "maxLength": 128 },
                              "flexWrap": { "type": "string", "maxLength": 128 },
                              "gridTemplateColumns": { "type": "string", "maxLength": 512 },
                              "columnSpan": { "type": ["integer", "null"], "minimum": 1, "maximum": 12 },
                              "rowSpan": { "type": ["integer", "null"], "minimum": 1, "maximum": 100 },
                              "flexBasis": { "type": "string", "maxLength": 128 },
                              "gap": { "type": "string", "maxLength": 128 },
                              "padding": { "type": "string", "maxLength": 128 },
                              "margin": { "type": "string", "maxLength": 128 },
                              "minWidth": { "type": "string", "maxLength": 128 },
                              "maxWidth": { "type": "string", "maxLength": 128 },
                              "minHeight": { "type": "string", "maxLength": 128 },
                              "maxHeight": { "type": "string", "maxLength": 128 },
                              "alignItems": { "type": "string", "maxLength": 128 },
                              "justifyContent": { "type": "string", "maxLength": 128 },
                              "alignSelf": { "type": "string", "maxLength": 128 }
                            },
                            "required": ["display", "flexDirection", "flexWrap", "gridTemplateColumns", "columnSpan", "rowSpan", "flexBasis", "gap", "padding", "margin", "minWidth", "maxWidth", "minHeight", "maxHeight", "alignItems", "justifyContent", "alignSelf"],
                            "additionalProperties": false
                          },
                          "binding": {
                            "type": ["object", "null"],
                            "properties": {
                              "bindingId": { "type": "string", "minLength": 1, "maxLength": 128 },
                              "kind": { "type": "string", "enum": ["Text", "Attribute", "Value", "Visibility", "CssClass", "Validation", "LoopItems"] },
                              "target": { "type": "string", "maxLength": 512 },
                              "expression": { "type": "string", "maxLength": 4096 },
                              "format": { "type": ["string", "null"], "maxLength": 512 }
                            },
                            "required": ["bindingId", "kind", "target", "expression", "format"],
                            "additionalProperties": false
                          },
                          "event": {
                            "type": ["object", "null"],
                            "properties": {
                              "eventId": { "type": "string", "minLength": 1, "maxLength": 128 },
                              "kind": { "type": "string", "enum": ["Click", "Change", "Input", "Submit", "Focus", "Blur", "KeyDown"] },
                              "handlerName": { "type": "string", "minLength": 1, "maxLength": 256 },
                              "argumentType": { "type": "string", "maxLength": 512 }
                            },
                            "required": ["eventId", "kind", "handlerName", "argumentType"],
                            "additionalProperties": false
                          },
                          "state": {
                            "type": ["object", "null"],
                            "properties": {
                              "stateId": { "type": "string", "minLength": 1, "maxLength": 128 },
                              "name": { "type": "string", "minLength": 1, "maxLength": 256 },
                              "typeName": { "type": "string", "minLength": 1, "maxLength": 512 },
                              "kind": { "type": "string", "enum": ["Field", "Parameter"] },
                              "initialValue": { "type": "string", "maxLength": 4096 },
                              "isRequired": { "type": "boolean" }
                            },
                            "required": ["stateId", "name", "typeName", "kind", "initialValue", "isRequired"],
                            "additionalProperties": false
                          }
                        },
                        "required": ["operationId", "kind", "targetElementId", "parentElementId", "index", "name", "value", "flag", "breakpoint", "element", "layout", "binding", "event", "state"],
                        "additionalProperties": false
                      }
                    }
                  },
                  "required": ["expected_revision", "operations", "reason"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "prepare_visual_page_markup",
                "description": "Prepare a multi-section or full-page HTML/Razor fragment for the active Hyperpage without putting markup in JSON tool arguments. After the host validates the revision and parent element, the next response transfers raw markup with tools disabled. Supported HTML becomes semantic canvas elements; unsupported Razor or markup remains a reviewable custom-code node. This tool cannot publish.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "expected_revision": { "type": "string", "minLength": 1, "maxLength": 128 },
                    "parent_element_id": { "type": "string", "minLength": 1, "maxLength": 128 },
                    "markup_language": { "type": "string", "enum": ["html", "razor"] },
                    "reason": { "type": "string", "minLength": 1, "maxLength": 1000 }
                  },
                  "required": ["expected_revision", "parent_element_id", "markup_language", "reason"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "capture_web_template",
                "description": "Fetch a public HTTPS page and propose a new inert, structure-only Hyperpage project template. The host strips scripts, remote assets, captured CSS, inline events, form actions, and site copy; approval is always required. Set permission_confirmed=true only after the user explicitly states they own the site or have permission to reuse its structure. This tool cannot alter the active page, run code, host, or publish.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "url": { "type": "string", "minLength": 1, "maxLength": 2048 },
                    "expected_revision": { "type": "string", "minLength": 1, "maxLength": 128 },
                    "template_name": { "type": "string", "minLength": 1, "maxLength": 200 },
                    "description": { "type": "string", "minLength": 1, "maxLength": 800 },
                    "scope": { "type": "string", "enum": ["page", "section", "element"] },
                    "category_group": { "type": "string", "enum": ["Marketing & conversion", "Content & publishing", "Discovery & collections", "Applications & accounts", "Business & contact", "Navigation", "Layout & structure", "Content blocks", "Data display", "Forms & input", "Feedback & status", "Identity & people"] },
                    "category": { "type": "string", "minLength": 1, "maxLength": 80 },
                    "permission_confirmed": { "type": "boolean", "description": "True only when the user explicitly confirmed ownership or permission to reuse the source site's structure." },
                    "reason": { "type": "string", "minLength": 1, "maxLength": 1000 }
                  },
                  "required": ["url", "expected_revision", "template_name", "description", "scope", "category_group", "category", "permission_confirmed", "reason"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "build_visual_page_preview",
                "description": "Request an explicit-trust build of the exact active visual-page revision in a bounded local cache snapshot. Project C#, packages, and MSBuild targets run with the Windows user's ambient permissions, so this always pauses for review. The tool cannot start a host, bind a port, open a firewall, publish, or activate a release.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "expected_revision": { "type": "string", "minLength": 1, "maxLength": 128 }
                  },
                  "required": ["expected_revision"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "read_active_terminal",
                "description": "Read a bounded snapshot of an active terminal allowed by the selected Context, including its current draft command and recent transcript.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {},
                  "required": [],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "propose_workspace_write",
                "description": "Request creating or atomically replacing a short local or remote text file whose complete content is safe to encode in one JSON string. Use prepare_workspace_write instead for source files or multiline content. Preserve canonical ssh:// workspace URIs; the Context independently enforces create or edit scope and review.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "relative_path": { "type": "string", "minLength": 1, "maxLength": 1024 },
                    "content": { "type": "string", "maxLength": 262144 },
                    "expected_sha256": { "type": ["string", "null"], "maxLength": 64, "description": "Use null for a new file. For replacement, use the current SHA-256 returned by read_workspace_text. Never hash the proposed content." },
                    "allow_create": { "type": "boolean" }
                  },
                  "required": ["relative_path", "content", "expected_sha256", "allow_create"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "prepare_workspace_write",
                "description": "Prepare a source file or other multiline text create/replacement without placing its body in JSON. After the host validates the target, the next response is a raw source-transfer payload that the host reviews or applies atomically.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "relative_path": { "type": "string", "minLength": 1, "maxLength": 1024 },
                    "expected_sha256": { "type": ["string", "null"], "maxLength": 64, "description": "Use null for a new file. For replacement, use the current SHA-256 returned by read_workspace_text. Never hash the proposed content." },
                    "allow_create": { "type": "boolean" }
                  },
                  "required": ["relative_path", "expected_sha256", "allow_create"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "prepare_workspace_edit",
                "description": "Prepare a narrow SHA-guarded edit to an existing local or remote text file. After validating the path and current SHA, the next response contains exact SEARCH/REPLACE blocks outside JSON; the host applies them only when each SEARCH section matches once.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "relative_path": { "type": "string", "minLength": 1, "maxLength": 1024 },
                    "expected_sha256": { "type": "string", "minLength": 64, "maxLength": 64, "description": "Use the current SHA-256 returned by read_workspace_text." }
                  },
                  "required": ["relative_path", "expected_sha256"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "propose_workspace_delete",
                "description": "Request permanently deleting a local or remote text file allowed by the selected Context. Preserve its canonical ssh:// URI and supply the SHA-256 returned by a current read.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string", "minLength": 1, "maxLength": 1024 },
                    "expected_sha256": { "type": "string", "minLength": 64, "maxLength": 64 }
                  },
                  "required": ["path", "expected_sha256"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "propose_active_document_replace",
                "description": "Request replacing the full live content of the active typed document when it is inside this conversation's working directory and the selected Context allows editing it.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "content": { "type": "string", "maxLength": 262144 },
                    "expected_sha256": { "type": "string", "minLength": 64, "maxLength": 64 }
                  },
                  "required": ["content", "expected_sha256"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "execute_terminal_command",
                "description": "Request executing a command in the active local or SSH terminal allowed by the selected Context. The host's saved terminal policy determines automatic execution or required review; the action stays bound to that exact terminal tab and location.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "command": { "type": "string", "minLength": 1, "maxLength": 32768 }
                  },
                  "required": ["command"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "stage_terminal_command",
                "description": "Place a command in the active local or SSH terminal without running it. Use this only when the user explicitly asks to stage or preview a command instead of executing it.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "command": { "type": "string", "minLength": 1, "maxLength": 32768 }
                  },
                  "required": ["command"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "run_project_check",
                "description": "Detect a recognized project manifest at the conversation working-directory root, choose its conventional build/test/lint/typecheck command, run it through the normal terminal policy, wait for completion, and return bounded terminal output.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "check": { "type": "string", "enum": ["build", "test", "lint", "typecheck"] }
                  },
                  "required": ["check"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "fetch_url_text",
                "description": "Fetch bounded text from a public HTTPS URL when the selected Context allows internet access. Redirects, private addresses, credentials, custom ports, and binary responses are rejected.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "url": { "type": "string", "minLength": 1, "maxLength": 2048 }
                  },
                  "required": ["url"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "inspect_git_repository",
                "description": "Inspect a local or linked-SSH Git repository through Hypershell's semantic repository adapter. Read access to the workspace is required. Unused nullable fields must be null.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "workspace": { "type": "string", "minLength": 1, "maxLength": 2048 },
                    "query": { "type": "string", "enum": ["status", "diff", "history", "commit", "commit_patch", "conflict", "branches", "tags", "remotes", "stashes"] },
                    "path": { "type": ["string", "null"], "maxLength": 4096 },
                    "reference": { "type": ["string", "null"], "maxLength": 512 },
                    "diff_target": { "type": "string", "enum": ["working_tree", "index", "commit"] },
                    "limit": { "type": "integer", "minimum": 1, "maximum": 200 }
                  },
                  "required": ["workspace", "query", "path", "reference", "diff_target", "limit"],
                  "additionalProperties": false
                }
              },
              {
                "type": "function",
                "name": "propose_git_operation",
                "description": "Propose a validated semantic Git operation for a local or linked-SSH workspace. The selected Context enforces Create, Edit, Delete, internet, and review policy. Use null for unused strings, [] for unused paths, and flag for the operation's documented boolean option: initialize=bare, apply_patch=reverse, commit=amend, create_branch=checkout, delete_branch=force, set_remote_url=push URL, fetch=prune, pull=rebase, push=set upstream, create_stash=include untracked, apply_stash=pop.",
                "strict": true,
                "parameters": {
                  "type": "object",
                  "properties": {
                    "workspace": { "type": "string", "minLength": 1, "maxLength": 2048 },
                    "operation": { "type": "string", "enum": ["initialize", "clone", "stage", "unstage", "apply_patch", "commit", "checkout", "create_branch", "rename_branch", "delete_branch", "create_tag", "delete_tag", "add_remote", "set_remote_url", "remove_remote", "fetch", "pull", "push", "force_push", "create_stash", "apply_stash", "drop_stash", "merge", "continue_merge", "abort_merge", "rebase", "continue_rebase", "skip_rebase", "abort_rebase", "revert", "reset", "cherry_pick", "abort_cherry_pick"] },
                    "paths": { "type": "array", "maxItems": 256, "items": { "type": "string", "minLength": 1, "maxLength": 4096 } },
                    "reference": { "type": ["string", "null"], "maxLength": 512 },
                    "name": { "type": ["string", "null"], "maxLength": 512 },
                    "new_name": { "type": ["string", "null"], "maxLength": 512 },
                    "message": { "type": ["string", "null"], "maxLength": 65536 },
                    "target": { "type": ["string", "null"], "maxLength": 512 },
                    "remote": { "type": ["string", "null"], "maxLength": 512 },
                    "branch": { "type": ["string", "null"], "maxLength": 512 },
                    "url": { "type": ["string", "null"], "maxLength": 4096 },
                    "patch": { "type": ["string", "null"], "maxLength": 262144 },
                    "flag": { "type": "boolean" },
                    "reset_mode": { "type": "string", "enum": ["soft", "mixed", "hard"] }
                  },
                  "required": ["workspace", "operation", "paths", "reference", "name", "new_name", "message", "target", "remote", "branch", "url", "patch", "flag", "reset_mode"],
                  "additionalProperties": false
                }
              }
            ]
            """)?.AsArray()
        ?? throw new InvalidOperationException(
            "The built-in AI tool definitions are malformed.");

    private static readonly HashSet<string> ComputerActionTypes =
        new(StringComparer.Ordinal)
        {
            "click",
            "double_click",
            "scroll",
            "type",
            "wait",
            "keypress",
            "drag",
            "move",
            "screenshot"
        };

    private readonly HttpClient _httpClient;
    private readonly IWorkspaceAiLocalRuntime? _localRuntime;
    private readonly bool _ownsHttpClient;
    private bool _isDisposed;

    public ResponsesWorkspaceAiProvider(
        HttpClient? httpClient = null,
        IWorkspaceAiLocalRuntime? localRuntime = null)
    {
        _localRuntime = localRuntime;
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            return;
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        _ownsHttpClient = true;
    }

    public async Task<WorkspaceAiProviderResponse> CreateResponseAsync(
        WorkspaceAiProviderConfiguration configuration,
        string apiKey,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(input);
        var normalized = WorkspaceAiProviderConfigurationService.Normalize(
            configuration);
        if (normalized.IsLocal)
        {
            if (_localRuntime is not null)
            {
                await _localRuntime.EnsureReadyAsync(
                    normalized.Model,
                    normalized.ComputerControl
                        != WorkspaceAiComputerControlMode.Off
                    && WorkspaceAiModelCatalog.Find(normalized.Model)
                        is { SupportsVision: true },
                    cancellationToken);
            }
        }
        else if (!normalized.AllowCloudRequests)
        {
            throw Failure(
                "Cloud AI requests are disabled in device-local settings.");
        }

        if (normalized.RequiresCredential
            && (string.IsNullOrWhiteSpace(apiKey)
            || apiKey.Length > 512
            || apiKey.Any(char.IsWhiteSpace)
            || apiKey.Any(char.IsControl)))
        {
            throw Failure(
                "A valid device-protected OpenAI API key is required.");
        }

        var payload = BuildPayload(normalized, input);
        var malformedToolCallRecoveryAttempted = false;
        while (true)
        {
            byte[] requestBytes;
            try
            {
                requestBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            }
            catch (Exception exception) when (exception is JsonException
                                             or NotSupportedException)
            {
                throw Failure(
                    "The bounded OpenAI request could not be serialized.",
                    exception);
            }

            if (requestBytes.LongLength > MaximumRequestBytes)
            {
                Array.Clear(requestBytes);
                throw Failure(
                    "The serialized OpenAI request exceeded Hypershell's 64 MiB transport allocation. The provider could not receive this request.");
            }

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    normalized.Endpoint)
                {
                    Content = new ByteArrayContent(requestBytes)
                };
                request.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/json");
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
                var authorizationToken = normalized.IsLocal
                    ? _localRuntime?.AuthorizationToken
                    : apiKey;
                if (!string.IsNullOrWhiteSpace(authorizationToken))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Bearer",
                            authorizationToken);
                }
                request.Headers.UserAgent.ParseAdd("Hypershell/0.1");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var responseBytes = await ReadBoundedAsync(
                    response,
                    cancellationToken);
                try
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (normalized.IsLocal
                            && IsMalformedToolCallResponse(
                                response.StatusCode,
                                responseBytes))
                        {
                            if (!malformedToolCallRecoveryAttempted)
                            {
                                payload = BuildLocalToolCallRecoveryPayload(
                                    payload, normalized.RequestProfile is not null);
                                malformedToolCallRecoveryAttempted = true;
                                continue;
                            }

                            throw Failure(normalized.RequestProfile is not null
                                ? "The content assistant produced invalid JSON tool arguments twice. No tool from either response was executed. Try a smaller content request."
                                : "Engineer produced invalid JSON tool arguments twice. Hypershell retried automatically, and no tool from either response was executed. Retry the action; multi-section Hyperpages should use the prepared markup-transfer path, and source files should use the prepared source-transfer path rather than JSON content or a terminal here-string.");
                        }

                        throw Failure(BuildProviderError(
                            normalized.IsLocal
                                ? "Local llama.cpp"
                                : "OpenAI",
                            response.StatusCode,
                            responseBytes));
                    }

                    return ParseResponse(responseBytes);
                }
                finally
                {
                    Array.Clear(responseBytes);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (WorkspaceAiProviderException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException
                                             or IOException
                                             or TaskCanceledException)
            {
                throw Failure(
                    normalized.IsLocal
                        ? "The local llama.cpp Responses request could not be completed."
                        : "The OpenAI Responses request could not be completed.",
                    exception);
            }
            finally
            {
                Array.Clear(requestBytes);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static JsonObject BuildPayload(
        WorkspaceAiProviderConfiguration configuration,
        IReadOnlyList<JsonElement> input)
    {
        var inputArray = new JsonArray();
        foreach (var item in input)
        {
            var node = JsonNode.Parse(item.GetRawText());
            if (node is JsonObject message
                && !message.ContainsKey("type")
                && message["role"] is JsonValue role
                && role.TryGetValue<string>(out var roleValue)
                && roleValue is "user" or "assistant"
                && message.ContainsKey("content"))
            {
                message.Insert(0, "type", "message");
            }

            inputArray.Add(node);
        }

        var sourceTransferRequested = configuration.RequestProfile is null && input.Count > 0
                                      && ContainsTransferRequestMarker(
                                          input[^1],
                                          WorkspaceAiToolHost
                                              .SourceTransferRequestMarker);
        var exactEditTransferRequested = configuration.RequestProfile is null && input.Count > 0
                                         && ContainsTransferRequestMarker(
                                             input[^1],
                                             WorkspaceAiToolHost
                                                 .ExactEditTransferRequestMarker);
        var visualPageMarkupTransferRequested = configuration.RequestProfile is null && input.Count > 0
                                                && ContainsTransferRequestMarker(
                                                    input[^1],
                                                    WorkspaceAiToolHost
                                                        .VisualPageMarkupTransferRequestMarker);
        var visualPageArgumentRecoveryRequested = configuration.RequestProfile is null && input.Count > 0
                                                  && ContainsTransferRequestMarker(
                                                      input[^1],
                                                      WorkspaceAiToolHost
                                                          .VisualPageArgumentRecoveryMarker);
        var transferRequested = sourceTransferRequested
                                || exactEditTransferRequested
                                || visualPageMarkupTransferRequested;
        var instructions = BuildInstructions(configuration);
        if (visualPageMarkupTransferRequested)
        {
            instructions += $"\n\n{VisualPageMarkupTransferInstructions}";
        }
        else if (exactEditTransferRequested)
        {
            instructions += $"\n\n{ExactEditTransferInstructions}";
        }
        else if (sourceTransferRequested)
        {
            instructions += $"\n\n{SourceTransferInstructions}";
        }
        else if (visualPageArgumentRecoveryRequested)
        {
            instructions += $"\n\n{VisualPageArgumentRecoveryInstructions}";
        }

        var tools = transferRequested
            ? new JsonArray()
            : visualPageArgumentRecoveryRequested
                ? BuildVisualPageRecoveryTools(configuration)
                : BuildTools(configuration);

        var payload = new JsonObject
        {
            ["model"] = configuration.Model,
            ["instructions"] = instructions,
            ["input"] = inputArray,
            ["tools"] = tools,
            ["tool_choice"] = transferRequested ? "none" : "auto",
            ["parallel_tool_calls"] = false,
            ["store"] = false
        };

        if (!configuration.IsLocal && configuration.RequestProfile is { HostedWebSearch: true })
            payload["include"] = new JsonArray("web_search_call.action.sources");

        if (configuration.IsLocal)
        {
            var isLocalActionLoop = input.Any(
                ContainsLocalActionLoopMarker);
            payload["max_output_tokens"] = transferRequested
                ? 8192
                : visualPageArgumentRecoveryRequested
                    ? 1024
                : isLocalActionLoop
                    ? 1536
                    : 4096;
            if (string.Equals(
                    configuration.Model,
                    WorkspaceAiModelCatalog.EngineerModelId,
                    StringComparison.Ordinal))
            {
                payload["chat_template_kwargs"] = new JsonObject
                {
                    ["enable_thinking"] = !transferRequested
                        && !visualPageArgumentRecoveryRequested
                        && !isLocalActionLoop
                        && !string.Equals(
                        configuration.ReasoningEffort,
                        "none",
                        StringComparison.Ordinal)
                };
            }

            return payload;
        }

        payload["context_management"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "compaction",
                ["compact_threshold"] = CompactThresholdTokens
            }
        };
        payload["reasoning"] = new JsonObject
        {
            ["effort"] = configuration.ReasoningEffort
        };
        payload["text"] = new JsonObject
        {
            ["verbosity"] = configuration.TextVerbosity
        };
        payload["safety_identifier"] = configuration.SafetyIdentifier;
        return payload;
    }

    private static JsonObject BuildLocalToolCallRecoveryPayload(
        JsonObject original,
        bool contentProfile = false)
    {
        var retry = original.DeepClone().AsObject();
        if (retry["input"] is not JsonArray input)
        {
            throw Failure(
                "The local AI recovery request lost its bounded input array.");
        }

        input.Add(new JsonObject
        {
            ["type"] = "message",
            ["role"] = "user",
            ["content"] = contentProfile
                ? "The preceding function arguments were not valid JSON and no tool was executed. Retry exactly one of the available content tools using valid JSON matching its schema. Do not request source editing, terminal commands, or unavailable tools."
                : LocalToolCallRecoveryInstruction
        });
        retry["max_output_tokens"] = 2048;
        if (retry["chat_template_kwargs"] is JsonObject templateArguments)
        {
            templateArguments["enable_thinking"] = false;
        }

        return retry;
    }

    private static JsonArray BuildTools(
        WorkspaceAiProviderConfiguration configuration)
    {
        if (configuration.RequestProfile is { } profile)
        {
            var profileTools = new JsonArray();
            foreach (var definition in profile.FunctionDefinitions)
                profileTools.Add(JsonNode.Parse(definition.GetRawText()));
            if (profile.HostedWebSearch)
            {
                if (configuration.IsLocal)
                    throw Failure("Hosted web research requires the configured OpenAI provider. No provider was changed.");
                profileTools.Add(new JsonObject { ["type"] = "web_search" });
            }
            return profileTools;
        }
        var enabled = configuration.EnabledToolNames is null
            ? null
            : new HashSet<string>(
                configuration.EnabledToolNames,
                StringComparer.Ordinal);
        var tools = new JsonArray();
        foreach (var definition in ToolDefinitions)
        {
            if (definition is not JsonObject tool)
            {
                continue;
            }

            var name = tool["name"]?.GetValue<string>();
            if (enabled is not null
                && (string.IsNullOrWhiteSpace(name)
                    || !enabled.Contains(name)))
            {
                continue;
            }

            tools.Add(tool.DeepClone());
        }

        if (configuration.ComputerControl
            != WorkspaceAiComputerControlMode.Off)
        {
            if (enabled is null
                || enabled.Contains(
                    WorkspaceAiWindowsUiFunctionTool.FunctionName))
            {
                tools.Add(
                    WorkspaceAiWindowsUiFunctionTool.CreateDefinition());
            }

            if (!configuration.IsLocal
                && (enabled is null
                    || enabled.Contains(
                        WorkspaceAiComputerFunctionTool.FunctionName)))
            {
                tools.Add(new JsonObject { ["type"] = "computer" });
            }
            else if (WorkspaceAiModelCatalog.Find(configuration.Model)
                     is { SupportsVision: true }
                     && (enabled is null
                         || enabled.Contains(
                             WorkspaceAiComputerFunctionTool.FunctionName)))
            {
                tools.Add(
                    WorkspaceAiComputerFunctionTool.CreateDefinition());
            }
        }

        return tools;
    }

    private static JsonArray BuildVisualPageRecoveryTools(
        WorkspaceAiProviderConfiguration configuration)
    {
        var tools = BuildTools(configuration);
        var recoveryTools = new JsonArray();
        foreach (var tool in tools.OfType<JsonObject>())
        {
            if (string.Equals(
                    tool["name"]?.GetValue<string>(),
                    "prepare_visual_page_markup",
                    StringComparison.Ordinal))
            {
                recoveryTools.Add(tool.DeepClone());
            }
        }

        return recoveryTools.Count > 0 ? recoveryTools : tools;
    }

    private static string BuildInstructions(
        WorkspaceAiProviderConfiguration configuration)
    {
        if (configuration.RequestProfile is { } profile) return profile.Instructions;
        var builder = new StringBuilder(Instructions);
        if (configuration.IsLocal
            && WorkspaceAiModelCatalog.Find(configuration.Model)
               is { Role: WorkspaceAiModelRole.Engineer })
        {
            builder.Append(
                " ENGINEER ROLE: Work in a short inspect-act-verify loop. Use exact host tool results as your state, choose semantic operations before pixel coordinates, make the smallest useful action, and verify the resulting state before reporting success.");
        }

        if (!string.IsNullOrWhiteSpace(
                configuration.RuntimeEnvironmentContext))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(configuration.RuntimeEnvironmentContext.Trim());
        }

        return builder.ToString();
    }

    private static bool ContainsLocalActionLoopMarker(
        JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                return text.Contains(
                           WorkspaceAiComputerFunctionTool.ScreenshotMarker,
                           StringComparison.Ordinal)
                       || text.Contains(
                           WorkspaceAiWindowsUiFunctionTool.SnapshotMarker,
                           StringComparison.Ordinal);
            case JsonValueKind.Array:
                return value.EnumerateArray().Any(
                    ContainsLocalActionLoopMarker);
            case JsonValueKind.Object:
                return value.EnumerateObject().Any(property =>
                    ContainsLocalActionLoopMarker(property.Value));
            default:
                return false;
        }
    }

    private static bool ContainsTransferRequestMarker(
        JsonElement value,
        string marker)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return (value.GetString() ?? string.Empty).Contains(
                    marker,
                    StringComparison.Ordinal);
            case JsonValueKind.Array:
                return value.EnumerateArray().Any(
                    item => ContainsTransferRequestMarker(item, marker));
            case JsonValueKind.Object:
                return value.EnumerateObject().Any(property =>
                    ContainsTransferRequestMarker(property.Value, marker));
            default:
                return false;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength
            is > MaximumResponseBytes)
        {
            throw Failure(
                "The OpenAI response exceeded Hypershell's 64 MiB transport allocation.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var memory = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (memory.Length + read > MaximumResponseBytes)
                {
                    throw Failure(
                        "The OpenAI response exceeded Hypershell's 64 MiB transport allocation.");
                }

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }
        finally
        {
            Array.Clear(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static WorkspaceAiProviderResponse ParseResponse(
        byte[] responseBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(
                responseBytes,
                new JsonDocumentOptions { MaxDepth = 64 });
            if (!document.RootElement.TryGetProperty(
                    "output",
                    out var output)
                || output.ValueKind != JsonValueKind.Array)
            {
                throw Failure(
                    "The OpenAI response did not contain an output array.");
            }

            var outputItems = new List<JsonElement>();
            var functionCalls = new List<WorkspaceAiFunctionCall>();
            var computerCalls = new List<WorkspaceAiComputerCall>();
            var citations = new List<WorkspaceAiCitation>();
            var sources = new List<WorkspaceAiResearchSource>();
            var text = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                outputItems.Add(item.Clone());
                if (!item.TryGetProperty("type", out var typeElement)
                    || typeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeElement.GetString();
                if (type == "web_search_call"
                    && item.TryGetProperty("action", out var searchAction)
                    && searchAction.TryGetProperty("sources", out var searchSources)
                    && searchSources.ValueKind == JsonValueKind.Array)
                {
                    foreach (var source in searchSources.EnumerateArray())
                        if (TryResearchUrl(source, out var url))
                            sources.Add(new(url, ResearchTitle(source, url)));
                }
                if (string.Equals(
                        type,
                        "function_call",
                        StringComparison.Ordinal))
                {
                    var callId = RequiredString(item, "call_id", 256);
                    var name = RequiredString(item, "name", 128);
                    var arguments = RequiredString(
                        item,
                        "arguments",
                        512 * 1024,
                        allowEmpty: true);
                    functionCalls.Add(new WorkspaceAiFunctionCall(
                        callId,
                        name,
                        arguments));
                    continue;
                }

                if (string.Equals(
                        type,
                        "computer_call",
                        StringComparison.Ordinal))
                {
                    var callId = RequiredString(item, "call_id", 256);
                    if (!item.TryGetProperty("actions", out var actions)
                        || actions.ValueKind != JsonValueKind.Array
                        || actions.GetArrayLength() < 1)
                    {
                        throw Failure(
                            "The OpenAI computer call contained an invalid action batch.");
                    }

                    var parsedActions = new List<WorkspaceAiComputerAction>();
                    foreach (var action in actions.EnumerateArray())
                    {
                        if (action.ValueKind != JsonValueKind.Object)
                        {
                            throw Failure(
                                "The OpenAI computer call contained a malformed action.");
                        }

                        var actionType = RequiredString(
                            action,
                            "type",
                            32);
                        if (!ComputerActionTypes.Contains(actionType))
                        {
                            throw Failure(
                                $"The OpenAI computer call requested unsupported action '{actionType}'.");
                        }

                        parsedActions.Add(new WorkspaceAiComputerAction(
                            action.Clone()));
                    }

                    computerCalls.Add(new WorkspaceAiComputerCall(
                        callId,
                        parsedActions));
                    continue;
                }

                if (!string.Equals(type, "message", StringComparison.Ordinal)
                    || !item.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty(
                            "type",
                            out var contentType)
                        && contentType.ValueKind == JsonValueKind.String
                        && string.Equals(
                            contentType.GetString(),
                            "output_text",
                            StringComparison.Ordinal)
                        && contentItem.TryGetProperty(
                            "text",
                            out var outputText)
                        && outputText.ValueKind == JsonValueKind.String)
                    {
                        if (text.Length > 0)
                        {
                            text.AppendLine();
                        }

                        var offset = text.Length;
                        text.Append(outputText.GetString());
                        if (contentItem.TryGetProperty("annotations", out var annotations)
                            && annotations.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var annotation in annotations.EnumerateArray())
                            {
                                if (!annotation.TryGetProperty("type", out var annotationType)
                                    || annotationType.GetString() != "url_citation"
                                    || !TryResearchUrl(annotation, out var url)) continue;
                                var title = ResearchTitle(annotation, url);
                                var start = annotation.TryGetProperty("start_index", out var startValue) && startValue.TryGetInt32(out var startIndex) ? startIndex : 0;
                                var end = annotation.TryGetProperty("end_index", out var endValue) && endValue.TryGetInt32(out var endIndex) ? endIndex : start;
                                if (start >= 0 && end >= start && end <= (outputText.GetString()?.Length ?? 0))
                                    citations.Add(new(url, title, offset + start, offset + end));
                                sources.Add(new(url, title));
                            }
                        }
                    }
                }
            }

            return new WorkspaceAiProviderResponse(
                outputItems,
                text.ToString(),
                functionCalls,
                computerCalls,
                citations,
                sources.DistinctBy(source => source.Url).ToList(),
                document.RootElement.TryGetProperty("status", out var status) ? status.GetString() ?? "unknown" : "completed");
        }
        catch (WorkspaceAiProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Failure(
                "The OpenAI response was not valid bounded JSON.",
                exception);
        }
    }

    private static bool TryResearchUrl(JsonElement element, out string url)
    {
        url = element.TryGetProperty("url", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static string ResearchTitle(JsonElement element, string fallback) =>
        element.TryGetProperty("title", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private static string RequiredString(
        JsonElement item,
        string propertyName,
        int maximumLength,
        bool allowEmpty = false)
    {
        if (!item.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw Failure(
                $"The OpenAI function call omitted {propertyName}.");
        }

        var value = element.GetString() ?? string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value.Length > maximumLength
            || value.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            throw Failure(
                $"The OpenAI function call contained an invalid {propertyName}.");
        }

        return value;
    }

    private static string BuildProviderError(
        string providerName,
        HttpStatusCode statusCode,
        byte[] responseBytes)
    {
        var detail = ReadProviderErrorDetail(responseBytes);
        return string.IsNullOrWhiteSpace(detail)
            ? $"{providerName} returned HTTP {(int)statusCode} ({statusCode})."
            : $"{providerName} returned HTTP {(int)statusCode}: {detail}";
    }

    private static bool IsMalformedToolCallResponse(
        HttpStatusCode statusCode,
        byte[] responseBytes)
    {
        if (statusCode is not HttpStatusCode.InternalServerError
            and not HttpStatusCode.BadRequest)
        {
            return false;
        }

        var detail = ReadProviderErrorDetail(responseBytes);
        return detail.Contains(
                   "tool call",
                   StringComparison.OrdinalIgnoreCase)
               && detail.Contains(
                   "JSON",
                   StringComparison.OrdinalIgnoreCase)
               && (detail.Contains(
                       "parse",
                       StringComparison.OrdinalIgnoreCase)
                   || detail.Contains(
                       "invalid",
                       StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadProviderErrorDetail(byte[] responseBytes)
    {
        var detail = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(
                responseBytes,
                new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                detail = SanitizeProviderMessage(message.GetString());
            }
        }
        catch (JsonException)
        {
        }

        return detail;
    }

    private static string SanitizeProviderMessage(string? value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character)
                || character == ' ')
            .Take(500)
            .ToArray());
        return sanitized.Trim();
    }

    private static WorkspaceAiProviderException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}
