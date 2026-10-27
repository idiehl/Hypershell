using Hypershell.Infrastructure;

namespace Hypershell.ViewModels;

public sealed class WorkspaceAiConversationTabViewModel
    : ObservableObject, IDisposable
{
    private string _conversationId;
    private string _displayTitle;
    private DateTimeOffset _createdAtUtc;
    private bool _isDisposed;

    public WorkspaceAiConversationTabViewModel(
        string displayTitle,
        WorkspaceAiAssistantViewModel assistant)
        : this(
            Guid.NewGuid().ToString("N"),
            displayTitle,
            DateTimeOffset.UtcNow,
            assistant)
    {
    }

    public WorkspaceAiConversationTabViewModel(
        string conversationId,
        string displayTitle,
        DateTimeOffset createdAtUtc,
        WorkspaceAiAssistantViewModel assistant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);
        ArgumentNullException.ThrowIfNull(assistant);

        _conversationId = conversationId;
        _displayTitle = displayTitle;
        _createdAtUtc = createdAtUtc;
        Assistant = assistant;
    }

    public string ConversationId => _conversationId;
    public string DisplayTitle => _displayTitle;
    public DateTimeOffset CreatedAtUtc => _createdAtUtc;
    public WorkspaceAiAssistantViewModel Assistant { get; }

    public void Rename(string displayTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);
        SetProperty(ref _displayTitle, displayTitle, nameof(DisplayTitle));
    }

    public void BeginNewConversation(string displayTitle)
    {
        _conversationId = Guid.NewGuid().ToString("N");
        _createdAtUtc = DateTimeOffset.UtcNow;
        Rename(displayTitle);
        OnPropertyChanged(nameof(ConversationId));
        OnPropertyChanged(nameof(CreatedAtUtc));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Assistant.Dispose();
    }
}
