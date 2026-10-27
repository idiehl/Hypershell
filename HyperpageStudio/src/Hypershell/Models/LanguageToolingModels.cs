namespace Hypershell.Models;

public enum LanguageToolKind
{
    Runtime,
    Compiler,
    InteractiveShell,
    Formatter,
    LanguageServer
}

public enum LanguageToolAvailability
{
    Available,
    Missing,
    UnsupportedCommandShim
}

public enum DocumentFormattingOutcome
{
    Applied,
    Unchanged,
    Unavailable,
    Rejected,
    Failed,
    TimedOut
}

public enum LanguageServerProcessState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record LanguageToolDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required LanguageToolKind Kind { get; init; }
    public IReadOnlyList<string> LanguageIds { get; init; } = [];
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = [];
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public int Priority { get; init; } = 100;
    public bool IsHostBuiltIn { get; init; }
    public bool AcceptsDocumentOnStandardInput { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed record DiscoveredLanguageTool
{
    public required LanguageToolDefinition Definition { get; init; }
    public required LanguageToolAvailability Availability { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;

    public bool IsAvailable => Availability == LanguageToolAvailability.Available;
}

public sealed record LanguageToolCatalogSnapshot
{
    public DateTimeOffset ScannedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<DiscoveredLanguageTool> Tools { get; init; } = [];

    public int AvailableRuntimeCount => Tools.Count(tool =>
        tool.Definition.Kind == LanguageToolKind.Runtime && tool.IsAvailable);
    public int AvailableCompilerCount => Tools.Count(tool =>
        tool.Definition.Kind == LanguageToolKind.Compiler && tool.IsAvailable);
    public int AvailableInteractiveShellCount => Tools.Count(tool =>
        tool.Definition.Kind == LanguageToolKind.InteractiveShell && tool.IsAvailable);
    public int AvailableFormatterCount => Tools.Count(tool =>
        tool.Definition.Kind == LanguageToolKind.Formatter && tool.IsAvailable);
    public int AvailableLanguageServerCount => Tools.Count(tool =>
        tool.Definition.Kind == LanguageToolKind.LanguageServer && tool.IsAvailable);
}

public sealed record DocumentFormattingRequest
{
    public required string LanguageId { get; init; }
    public required string Content { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = "Untitled.txt";
    public string WorkingDirectory { get; init; } = string.Empty;
}

public sealed record DocumentFormattingResult
{
    public required DocumentFormattingOutcome Outcome { get; init; }
    public string FormattedText { get; init; } = string.Empty;
    public string FormatterId { get; init; } = string.Empty;
    public string FormatterDisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool WasApplied => Outcome is
        DocumentFormattingOutcome.Applied or DocumentFormattingOutcome.Unchanged;
    public bool ShouldUseEditorFallback => !WasApplied;
}

public sealed record BoundedProcessRequest
{
    public required string ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string StandardInput { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaximumStandardInputCharacters { get; init; } = 8 * 1024 * 1024;
    public int MaximumStandardOutputCharacters { get; init; } = 8 * 1024 * 1024;
    public int MaximumStandardErrorCharacters { get; init; } = 64 * 1024;
}

public sealed record BoundedProcessResult
{
    public bool Started { get; init; }
    public bool TimedOut { get; init; }
    public bool OutputLimitExceeded { get; init; }
    public int? ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
}

public enum CompiledLanguageExecutionOutcome
{
    Succeeded,
    Unavailable,
    Rejected,
    CompilationFailed,
    RuntimeFailed,
    TimedOut
}

public sealed record CompiledLanguageExecutionRequest
{
    public required string LanguageId { get; init; }
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public string WorkingDirectory { get; init; } = string.Empty;
}

public sealed record CompiledLanguageExecutionResult
{
    public required CompiledLanguageExecutionOutcome Outcome { get; init; }
    public string LanguageId { get; init; } = string.Empty;
    public string ToolchainId { get; init; } = string.Empty;
    public string EntryPoint { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public IReadOnlyList<string> Output { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public string Message { get; init; } = string.Empty;

    public bool Succeeded => Outcome == CompiledLanguageExecutionOutcome.Succeeded;
}

public sealed record CompiledLanguageToolchainDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string LanguageId { get; init; }
    public required string CompilerToolId { get; init; }
    public required string RuntimeToolId { get; init; }
    public bool RequiresRuntime { get; init; } = true;
}

public sealed record DiscoveredCompiledLanguageToolchain
{
    public required CompiledLanguageToolchainDefinition Definition { get; init; }
    public DiscoveredLanguageTool? Compiler { get; init; }
    public DiscoveredLanguageTool? Runtime { get; init; }
    public string StatusMessage { get; init; } = string.Empty;

    public bool IsAvailable => Compiler?.IsAvailable == true
                               && (!Definition.RequiresRuntime
                                   || Runtime?.IsAvailable == true);
}

public sealed record LanguageServerLaunchRequest
{
    public required DiscoveredLanguageTool Tool { get; init; }
    public string WorkingDirectory { get; init; } = string.Empty;
}

public sealed class LanguageServerStateChangedEventArgs : EventArgs
{
    public LanguageServerStateChangedEventArgs(
        LanguageServerProcessState state,
        string message)
    {
        State = state;
        Message = message;
    }

    public LanguageServerProcessState State { get; }
    public string Message { get; }
}
