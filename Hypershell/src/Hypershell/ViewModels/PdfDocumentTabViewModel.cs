using Hypershell.Infrastructure;
using Hypershell.Models;
using System.IO;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed class PdfDocumentTabViewModel : ObservableObject, IWorkspaceTab
{
    private string _errorMessage = string.Empty;
    private bool _isAiLiveAssistEnabled;
    private bool _isDisposed;

    public PdfDocumentTabViewModel(
        string path,
        WorkspaceLocation? workspaceLocation = null)
    {
        FilePath = Path.GetFullPath(path);
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException("The PDF document no longer exists.", FilePath);
        }

        SessionId = Guid.NewGuid().ToString("N");
        WorkspaceLocation = workspaceLocation;
        Title = workspaceLocation is null
            ? Path.GetFileName(FilePath)
            : GetWorkspaceFileName(workspaceLocation.Path);
        PdfView = new PdfDocumentView(this);
    }

    public WorkspaceTabKind Kind => WorkspaceTabKind.Document;
    public string SessionId { get; }
    public string AiLiveSourceId => $"pdf:{SessionId}";
    public string FilePath { get; }
    public WorkspaceLocation? WorkspaceLocation { get; }
    public string WorkspaceUri => WorkspaceLocation?.Identity
                                  ?? Hypershell.Models.WorkspaceLocation.Local(FilePath).Identity;
    public PdfDocumentView PdfView { get; }
    public string Title { get; }
    public string DisplayTitle => Title;
    public string RenameText
    {
        get => Title;
        set
        {
        }
    }

    public bool IsRenaming => false;
    public bool CanRename => false;
    public bool HasUnsavedChanges => false;
    public string TabColor => "#3A273E";
    public Brush TabBackgroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(58, 39, 62));
    public Brush TabForegroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(230, 237, 243));
    public bool IsAiLiveAssistEnabled
    {
        get => _isAiLiveAssistEnabled;
        set => SetProperty(ref _isAiLiveAssistEnabled, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void ReportError(string message) =>
        ErrorMessage = string.IsNullOrWhiteSpace(message)
            ? "The PDF document could not be displayed."
            : message.Trim();

    public void ClearError() => ErrorMessage = string.Empty;

    public void BeginRename()
    {
    }

    public void CommitRename()
    {
    }

    public void CancelRename()
    {
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        PdfView.Dispose();
    }

    private static string GetWorkspaceFileName(string providerPath)
    {
        var normalized = providerPath.TrimEnd('/', '\\');
        var separator = Math.Max(
            normalized.LastIndexOf('/'),
            normalized.LastIndexOf('\\'));
        return separator >= 0
            ? normalized[(separator + 1)..]
            : normalized;
    }
}
