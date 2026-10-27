using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

/// <summary>
/// Normalizes the project root selected for one AI conversation and resolves
/// every relative workspace operation against it. The selected Context still
/// decides whether the resulting absolute target is authorized.
/// </summary>
public static class WorkspaceAiWorkingDirectory
{
    public static string Normalize(
        string? value,
        bool requireAvailable = true)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0)
        {
            return string.Empty;
        }

        if (candidate.Length > 2048
            || candidate.IndexOf('\0') >= 0
            || candidate.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The working directory is malformed.");
        }

        if (WorkspaceLocation.TryParse(candidate, out var location))
        {
            if (location.IsSsh)
            {
                return location.Identity;
            }

            candidate = location.Path;
        }

        if (!Path.IsPathFullyQualified(candidate))
        {
            throw new ArgumentException(
                "Choose an absolute project working directory.");
        }

        var fullPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidate));
        if (requireAvailable && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The project working directory does not exist: {fullPath}");
        }

        return fullPath;
    }

    public static string ResolveProjectPath(
        string? workingDirectory,
        string requestedPath)
    {
        var requested = (requestedPath ?? string.Empty).Trim();
        if (requested.Length == 0)
        {
            throw new ArgumentException("A workspace path is required.");
        }

        var root = Normalize(
            workingDirectory,
            requireAvailable: false);
        if (root.Length == 0)
        {
            if (WorkspaceLocation.TryParse(requested, out var location))
            {
                return location.IsLocal ? location.Path : location.Identity;
            }

            if (!Path.IsPathFullyQualified(requested))
            {
                throw new ArgumentException(
                    "This conversation has no working directory. Choose one in the AI pane before using relative workspace paths.");
            }

            return Path.GetFullPath(requested);
        }

        if (WorkspaceLocation.TryParse(root, out var remoteRoot)
            && remoteRoot.IsSsh)
        {
            return ResolveRemote(remoteRoot, requested);
        }

        if (WorkspaceLocation.TryParse(requested, out var requestedLocation))
        {
            if (!requestedLocation.IsLocal)
            {
                throw new ArgumentException(
                    "A remote workspace target cannot be used while this conversation is bound to a local project.");
            }

            requested = requestedLocation.Path;
        }

        var fullPath = Path.GetFullPath(
            Path.IsPathFullyQualified(requested)
                ? requested
                : Path.Combine(root, requested));
        if (!IsWithinLocalRoot(root, fullPath))
        {
            throw new ArgumentException(
                $"The requested path is outside this conversation's working directory: {root}");
        }

        return fullPath;
    }

    public static string Display(string? value)
    {
        var normalized = Normalize(value, requireAvailable: false);
        if (WorkspaceLocation.TryParse(normalized, out var location)
            && location.IsLocal)
        {
            return location.Path;
        }

        return normalized;
    }

    private static string ResolveRemote(
        WorkspaceLocation root,
        string requested)
    {
        WorkspaceLocation target;
        if (WorkspaceLocation.TryParse(requested, out var parsed))
        {
            if (!parsed.IsSsh
                || !string.Equals(
                    parsed.Authority,
                    root.Authority,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The requested remote target is on a different linked device.");
            }

            target = parsed;
        }
        else
        {
            var path = requested == "."
                ? root.Path
                : requested.StartsWith('/')
                    ? requested
                    : root.Path.TrimEnd('/') + "/" + requested;
            target = WorkspaceLocation.Ssh(root.Authority, path);
        }

        var rootPath = root.Path.TrimEnd('/');
        var targetPath = target.Path.TrimEnd('/');
        if (!string.Equals(rootPath, targetPath, StringComparison.Ordinal)
            && !targetPath.StartsWith(
                rootPath + "/",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The requested path is outside this conversation's working directory: {root.Identity}");
        }

        return target.Identity;
    }

    private static bool IsWithinLocalRoot(string root, string candidate)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root));
        var canonicalCandidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidate));
        if (string.Equals(
                canonicalRoot,
                canonicalCandidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return canonicalCandidate.StartsWith(
            canonicalRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record WorkspaceAiTaskRoute(
    string Name,
    IReadOnlyList<string> ToolNames);

/// <summary>
/// Keeps one monolithic assistant while presenting a smaller, task-relevant
/// tool surface for each prompt. No model profile, capability, or permission
/// is removed; a later prompt can select a different route automatically.
/// </summary>
public static class WorkspaceAiTaskToolRouter
{
    public const string HyperpageProjectRouteName = "hyperpage-project";

    private static readonly string[] HyperpageProjectTools =
    [
        "list_workspace",
        "search_workspace_text",
        "read_workspace_text",
        "read_active_document",
        "prepare_workspace_write",
        "prepare_workspace_edit",
        "propose_workspace_write",
        "propose_active_document_replace",
        "read_active_visual_page",
        "validate_visual_page",
        "propose_visual_page_changes",
        "prepare_visual_page_markup",
        "build_visual_page_preview",
        "search_hyperlib",
        "read_active_terminal",
        "execute_terminal_command",
        "run_project_check",
        "inspect_git_repository",
        "fetch_url_text"
    ];

    private static readonly string[] CodingTerms =
    [
        "code", "coding", "implement", "build", "compile", "debug",
        "bug", "test", "repository", "repo", "project", "source",
        "html", "css", "javascript", "typescript", "python", "c#",
        "c++", "dotnet", "java", "rust", "go", "sql", "bash", "json",
        "yaml", "xaml", "razor", "website", "web app", "api", "apis"
    ];

    private static readonly string[] FileTerms =
    [
        "file", "files", "folder", "directory", "working directory",
        "workspace", "path", "rename", "copy", "move", "organize"
    ];

    private static readonly string[] FileMutationTerms =
    [
        "create", "write", "edit", "change", "update", "replace",
        "rename", "copy", "move", "save", "add", "organize"
    ];

    private static readonly string[] DesktopTerms =
    [
        "desktop", "cursor", "mouse", "click", "window", "menu",
        "dialog", "screen", "screenshot", "application", "browser",
        "open sublime", "open word", "type into", "navigate the app"
    ];

    private static readonly string[] TerminalTerms =
    [
        "terminal", "powershell", "shell", "command", "ssh", "server",
        "service", "process", "install", "package", "run ", "execute"
    ];

    private static readonly string[] WritingTerms =
    [
        "write", "draft", "rewrite", "document", "article", "report",
        "essay", "copyedit", "proofread", "summarize"
    ];

    private static readonly string[] VisualPageTerms =
    [
        "hyperpage", "hyperpage studio", "visual page", "visual-page", "page studio", "design canvas",
        "canvas element", "blazor page", "razor page", "page element",
        "responsive layout", "breakpoint"
    ];

    private static readonly string[] ContinuationTerms =
    [
        "continue", "continue please", "go ahead", "do it", "try again",
        "retry", "fix it", "finish it", "keep going", "proceed", "yes",
        "yes please", "okay", "ok"
    ];

    public static WorkspaceAiTaskRoute Route(
        string? userText,
        WorkspaceAiComputerControlMode computerControl,
        WorkspaceAiTaskRoute? previousRoute = null)
    {
        var text = (userText ?? string.Empty).ToLowerInvariant();
        var coding = ContainsAny(text, CodingTerms);
        var file = ContainsAny(text, FileTerms);
        var fileMutation = file && ContainsAny(text, FileMutationTerms);
        var desktop = ContainsAny(text, DesktopTerms);
        var terminal = ContainsAny(text, TerminalTerms);
        var writing = ContainsAny(text, WritingTerms);
        var visualPage = ContainsAny(text, VisualPageTerms);
        var git = ContainsAny(
            text,
            ["git", "commit", "branch", "pull request"]);
        var internet = text.Contains("http://", StringComparison.Ordinal)
                       || text.Contains("https://", StringComparison.Ordinal)
                       || ContainsAny(text, ["fetch", "research"]);

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (coding || file || terminal || git || writing)
        {
            names.Add("list_workspace");
            names.Add("search_workspace_text");
            names.Add("read_workspace_text");
            names.Add("read_active_document");
        }

        if (coding || fileMutation || writing)
        {
            names.Add("prepare_workspace_write");
            names.Add("prepare_workspace_edit");
            names.Add("propose_workspace_write");
            names.Add("propose_active_document_replace");
        }

        if (visualPage)
        {
            names.Add("read_active_visual_page");
            names.Add("validate_visual_page");
            names.Add("propose_visual_page_changes");
            names.Add("prepare_visual_page_markup");
            if (internet && ContainsAny(
                    text,
                    ["website template", "web template", "template from", "url to template", "capture website"]))
            {
                names.Add("capture_web_template");
            }
            if (ContainsAny(text, ["build", "compile", "preview", "diagnostic"]))
            {
                names.Add("build_visual_page_preview");
            }
        }

        if (coding)
        {
            names.Add("search_hyperlib");
            names.Add("read_active_terminal");
            names.Add("execute_terminal_command");
            names.Add("run_project_check");
            names.Add("inspect_git_repository");
            names.Add("fetch_url_text");
        }

        if (terminal)
        {
            names.Add("read_active_terminal");
            names.Add("execute_terminal_command");
        }

        if (ContainsAny(text, ["delete", "remove file", "remove files"]))
        {
            names.Add("propose_workspace_delete");
        }

        if (ContainsAny(text, ["stage command", "put the command"]))
        {
            names.Add("stage_terminal_command");
        }

        if (git)
        {
            names.Add("inspect_git_repository");
            names.Add("propose_git_operation");
        }

        if (internet)
        {
            names.Add("fetch_url_text");
        }

        if (desktop
            && computerControl != WorkspaceAiComputerControlMode.Off)
        {
            names.Add(WorkspaceAiWindowsUiFunctionTool.FunctionName);
            names.Add(WorkspaceAiComputerFunctionTool.FunctionName);
        }

        var routeName = string.Join(
            "+",
            new[]
            {
                coding ? "coding" : string.Empty,
                file ? "files" : string.Empty,
                desktop ? "desktop" : string.Empty,
                terminal ? "terminal" : string.Empty,
                git ? "git" : string.Empty,
                writing ? "writing" : string.Empty,
                visualPage ? "visual-page" : string.Empty,
                internet ? "web" : string.Empty
            }.Where(value => value.Length > 0));
        if (routeName.Length == 0)
        {
            if (previousRoute is not null
                && previousRoute.Name != "conversation"
                && IsContinuation(text))
            {
                var continuedTools = previousRoute.ToolNames
                    .Where(name => computerControl
                                   != WorkspaceAiComputerControlMode.Off
                                   || name is not WorkspaceAiWindowsUiFunctionTool.FunctionName
                                       and not WorkspaceAiComputerFunctionTool.FunctionName)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                var continuedName = previousRoute.Name.StartsWith(
                    "continue:",
                    StringComparison.Ordinal)
                    ? previousRoute.Name
                    : $"continue:{previousRoute.Name}";
                return new WorkspaceAiTaskRoute(
                    continuedName,
                    continuedTools);
            }

            routeName = "conversation";
        }

        return new WorkspaceAiTaskRoute(
            routeName,
            names.Order(StringComparer.Ordinal).ToList());
    }

    public static WorkspaceAiTaskRoute CreateHyperpageProjectRoute() => new(
        HyperpageProjectRouteName,
        HyperpageProjectTools.Order(StringComparer.Ordinal).ToList());

    public static WorkspaceAiTaskRoute MergeHyperpageProjectRoute(
        WorkspaceAiTaskRoute detectedRoute,
        WorkspaceAiComputerControlMode computerControl)
    {
        ArgumentNullException.ThrowIfNull(detectedRoute);
        var names = HyperpageProjectTools
            .Concat(detectedRoute.ToolNames)
            .Where(name => computerControl != WorkspaceAiComputerControlMode.Off
                           || name is not WorkspaceAiWindowsUiFunctionTool.FunctionName
                               and not WorkspaceAiComputerFunctionTool.FunctionName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var routeName = detectedRoute.Name == "conversation"
            ? HyperpageProjectRouteName
            : $"{HyperpageProjectRouteName}+{detectedRoute.Name}";
        return new WorkspaceAiTaskRoute(routeName, names);
    }

    private static bool ContainsAny(
        string text,
        IEnumerable<string> terms) => terms.Any(term =>
        ContainsWholeTerm(text, term));

    private static bool IsContinuation(string text)
    {
        var normalized = text.Trim().TrimEnd('.', '!', '?');
        return normalized.Length <= 48
               && ContinuationTerms.Any(term => string.Equals(
                   normalized,
                   term,
                   StringComparison.Ordinal));
    }

    private static bool ContainsWholeTerm(string text, string term)
    {
        var candidate = term.Trim();
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(
                candidate,
                start,
                StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var end = index + candidate.Length;
            var leftBoundary = index == 0
                               || !char.IsLetterOrDigit(text[index - 1]);
            var rightBoundary = end == text.Length
                                || !char.IsLetterOrDigit(text[end]);
            if (leftBoundary && rightBoundary)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }
}
