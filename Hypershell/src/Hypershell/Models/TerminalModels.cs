using Hypershell.Infrastructure;
using Hypershell.Profiles;

namespace Hypershell.Models;

public enum TerminalLineKind
{
    Output,
    Input,
    Error,
    System
}

public sealed class TerminalLine
{
    public TerminalLine(string text, TerminalLineKind kind = TerminalLineKind.Output)
    {
        Text = text;
        Kind = kind;
        Timestamp = DateTime.Now;
    }

    public string Text { get; }
    public TerminalLineKind Kind { get; }
    public DateTime Timestamp { get; }
}

public sealed class TerminalExecutionResult
{
    public List<string> Output { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public string? WorkingDirectory { get; set; }
}

public sealed class TerminalLaunchRequest
{
    public required TerminalProfile Profile { get; init; }
    public string TitleTemplate { get; init; } = string.Empty;
    public string TabColor { get; init; } = string.Empty;
    public string BackgroundColor { get; init; } = string.Empty;
    public string BackgroundImagePath { get; init; } = string.Empty;
    public string FontFamilyName { get; init; } = string.Empty;
    public string StartupCommand { get; init; } = string.Empty;
    public bool SaveAsProfileDefaults { get; init; }
}

public enum RemoteTerminalShellKind
{
    Auto,
    Bash,
    PowerShell
}

public sealed class RemoteTerminalLaunchRequest
{
    public required RemoteHostSummary Host { get; init; }

    public required SshCredentialSummary Credential { get; init; }

    public RemoteTerminalShellKind ShellKind { get; init; } =
        RemoteTerminalShellKind.Auto;

    public string ShellLaunchCommand { get; init; } = string.Empty;

    public string InitialWorkingDirectory { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string StartupCommand { get; init; } = string.Empty;
}

public sealed class FileSystemEntry : ObservableObject
{
    private bool _isExpanded;

    public string Name { get; init; } = string.Empty;
    public required WorkspaceLocation Location { get; init; }
    public string FullPath => Location.IsLocal
        ? Location.Path
        : Location.Identity;
    public string ProviderPath => Location.Path;
    public bool IsDirectory { get; init; }
    public bool IsSymbolicLink { get; init; }
    public int Depth { get; init; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpansionGlyph));
            }
        }
    }

    public string ExpansionGlyph => IsDirectory ? (IsExpanded ? "−" : "+") : string.Empty;
}
