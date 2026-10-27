using System.Windows.Media;

namespace Hypershell.ViewModels;

public enum WorkspaceTabKind
{
    Terminal,
    Document,
    Browser,
    Network,
    VisualPage
}

public interface IWorkspaceTab : IDisposable
{
    WorkspaceTabKind Kind { get; }
    string Title { get; }
    string DisplayTitle { get; }
    string RenameText { get; set; }
    bool IsRenaming { get; }
    bool CanRename { get; }
    bool HasUnsavedChanges { get; }
    string TabColor { get; }
    Brush TabBackgroundBrush { get; }
    Brush TabForegroundBrush { get; }
    string AiLiveSourceId { get; }
    bool IsAiLiveAssistEnabled { get; set; }

    void BeginRename();
    void CommitRename();
    void CancelRename();
}
