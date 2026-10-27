using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Hypershell.ViewModels;

public sealed class WorkspaceAiAssistantViewModel : ObservableObject, IDisposable
{
    public const string DesktopLiveSourceId = "desktop:live-session";

    private static readonly TimeSpan LiveActivityDelay =
        TimeSpan.FromMilliseconds(1400);
    private static readonly JsonSerializerOptions ContinuationJsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private readonly WorkspaceAiContextService _contextService;
    private readonly WorkspaceAiProviderConfigurationService
        _configurationService;
    private readonly IWorkspaceAiCredentialStore _credentialStore;
    private readonly IWorkspaceAiLocalRuntime _localRuntime;
    private readonly WorkspaceAiConversationCoordinator _coordinator;
    private readonly WorkspaceAiComputerControlService _computerControl;
    private WorkspaceAiSessionModelSelection _sessionModelSelection;
    private readonly HashSet<string> _liveSources =
        new(StringComparer.Ordinal);
    private CancellationTokenSource? _liveDelayCancellation;
    private CancellationTokenSource? _liveRequestCancellation;
    private CancellationTokenSource? _activeRequestCancellation;
    private WorkspaceAiLiveActivity? _pendingLiveActivity;
    private string _activeLiveSourceId = string.Empty;
    private string _lastLiveError = string.Empty;
    private string _lastLiveComment = string.Empty;
    private long _liveActivityVersion;
    private long _activeLiveActivityVersion;
    private WorkspaceAiContextOptionViewModel? _selectedContext;
    private WorkspaceAiProviderModelOption? _selectedProviderModel;
    private WorkspaceAiPendingReview? _pendingReview;
    private string _prompt = string.Empty;
    private string _statusMessage = string.Empty;
    private string _providerStatus = string.Empty;
    private string _activeModel = WorkspaceAiProviderConfiguration.DefaultModel;
    private string _activeReasoningEffort = "medium";
    private string _activeTextVerbosity = "medium";
    private string _workingDirectory = string.Empty;
    private string _lastRunDiagnosticsPath = string.Empty;
    private string _providerAvailabilityBlocker = string.Empty;
    private bool _isBusy;
    private bool _isProviderReady;
    private bool _canEnableSelectedOpenAi;
    private bool _isApplyingContext;
    private bool _isApplyingProviderSelection;
    private bool _isLiveRequestRunning;
    private bool _isDesktopLiveSessionEnabled;
    private bool _isDisposed;

    public WorkspaceAiAssistantViewModel(
        WorkspaceAiContextService contextService,
        WorkspaceAiProviderConfigurationService configurationService,
        IWorkspaceAiCredentialStore credentialStore,
        WorkspaceAiConversationCoordinator coordinator,
        WorkspaceAiComputerControlService computerControl,
        IWorkspaceAiLocalRuntime localRuntime)
    {
        _contextService = contextService;
        _configurationService = configurationService;
        _credentialStore = credentialStore;
        _localRuntime = localRuntime;
        _coordinator = coordinator;
        _computerControl = computerControl;
        _sessionModelSelection = coordinator.SessionModelSelection;
        _coordinator.Progress += OnCoordinatorProgress;
        _computerControl.StateChanged += OnComputerControlStateChanged;
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
        StopWorkCycleCommand = new RelayCommand(
            StopWorkCycle,
            () => CanStopWorkCycle);
        RefreshCommand = new RelayCommand(RefreshState);
        StopComputerControlCommand = new RelayCommand(StopAiControl);
        EnableSelectedOpenAiCommand = new RelayCommand(
            EnableSelectedOpenAi,
            () => CanEnableSelectedOpenAi);
        ToggleDesktopLiveSessionCommand = new RelayCommand(
            ToggleDesktopLiveSession);
        RefreshState();
    }

    public event EventHandler? LiveSourcesInvalidated;
    public event EventHandler? ConversationChanging;
    public event EventHandler? ConversationChanged;
    public event EventHandler<WorkspaceAiDesktopLiveSessionChangedEventArgs>?
        DesktopLiveSessionChanged;

    public ObservableCollection<WorkspaceAiContextOptionViewModel>
        Contexts
    { get; } = [];

    public ObservableCollection<WorkspaceAiChatMessageViewModel>
        Messages
    { get; } = [];

    public ObservableCollection<WorkspaceAiProviderModelOption>
        ProviderModelOptions
    { get; } = [];

    public IReadOnlyList<string> ReasoningEfforts { get; } =
        ["none", "low", "medium", "high", "xhigh", "max"];

    public IReadOnlyList<string> TextVerbosities { get; } =
        ["low", "medium", "high"];

    public WorkspaceAiContextOptionViewModel? SelectedContext
    {
        get => _selectedContext;
        set
        {
            if (value is null || IsBusy || ReferenceEquals(_selectedContext, value))
            {
                return;
            }

            try
            {
                _contextService.Select(value.Id);
            }
            catch (InvalidOperationException exception)
            {
                StatusMessage = exception.Message;
                return;
            }

            if (!SetProperty(ref _selectedContext, value))
            {
                return;
            }

            ApplyBehavior(value.Definition);
            ResetConversationForContextChange();
            OnPropertyChanged(nameof(CanChangeContext));
            NotifySendAvailabilityChanged();
        }
    }

    public WorkspaceAiContextDefinition? SelectedContextDefinition =>
        SelectedContext?.Definition;

    public string SessionProviderId => _sessionModelSelection.ProviderId;

    public string SessionModelId => _sessionModelSelection.ModelId;

    public string WorkingDirectory => _workingDirectory;

    public string WorkingDirectoryDisplay =>
        WorkspaceAiWorkingDirectory.Display(_workingDirectory);

    public bool HasWorkingDirectory =>
        !string.IsNullOrWhiteSpace(_workingDirectory);

    public string LastRunDiagnosticsPath
    {
        get => _lastRunDiagnosticsPath;
        private set => SetProperty(ref _lastRunDiagnosticsPath, value);
    }

    public WorkspaceAiProviderModelOption? SelectedProviderModel
    {
        get => _selectedProviderModel;
        set
        {
            if (value is null
                || _isApplyingProviderSelection
                || !CanChangeProvider
                || Equals(_selectedProviderModel, value))
            {
                return;
            }

            ApplyProviderModelSelection(value);
        }
    }

    public WorkspaceAiPendingReview? PendingReview
    {
        get => _pendingReview;
        private set
        {
            if (SetProperty(ref _pendingReview, value))
            {
                OnPropertyChanged(nameof(HasPendingReview));
                OnPropertyChanged(nameof(CanReview));
                OnPropertyChanged(nameof(CanChangeContext));
                OnPropertyChanged(nameof(CanChangeProvider));
                OnPropertyChanged(nameof(CanChangeWorkingDirectory));
                NotifySendAvailabilityChanged();
            }
        }
    }

    public string Prompt
    {
        get => _prompt;
        set
        {
            if (SetProperty(ref _prompt, value ?? string.Empty))
            {
                NotifySendAvailabilityChanged();
            }
        }
    }

    public string ActiveModel
    {
        get => _activeModel;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (SetProperty(ref _activeModel, normalized))
            {
                PromoteBehaviorToCustom();
            }
        }
    }

    public string ActiveReasoningEffort
    {
        get => _activeReasoningEffort;
        set
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (SetProperty(ref _activeReasoningEffort, normalized))
            {
                PromoteBehaviorToCustom();
            }
        }
    }

    public string ActiveTextVerbosity
    {
        get => _activeTextVerbosity;
        set
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (SetProperty(ref _activeTextVerbosity, normalized))
            {
                PromoteBehaviorToCustom();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ProviderStatus
    {
        get => _providerStatus;
        private set => SetProperty(ref _providerStatus, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanReview));
                OnPropertyChanged(nameof(CanChangeContext));
                OnPropertyChanged(nameof(CanChangeProvider));
                OnPropertyChanged(nameof(CanChangeWorkingDirectory));
                OnPropertyChanged(nameof(CanStopWorkCycle));
                NotifySendAvailabilityChanged();
                StopWorkCycleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsProviderReady
    {
        get => _isProviderReady;
        private set
        {
            if (SetProperty(ref _isProviderReady, value))
            {
                NotifySendAvailabilityChanged();
            }
        }
    }

    public bool HasPendingReview => PendingReview is not null;
    public bool HasMessages => Messages.Count > 0;
    public bool IsConversationEmpty => Messages.Count == 0;
    public bool IsLiveRequestRunning => _isLiveRequestRunning;
    public bool IsDesktopLiveSessionEnabled =>
        _isDesktopLiveSessionEnabled;
    public bool IsComputerControlActive => _computerControl.IsActive;
    public bool IsComputerControlAvailable => _computerControl.IsAvailable;
    public bool CanReview => HasPendingReview && !IsBusy;
    public bool CanChangeContext => !IsBusy && !HasPendingReview;
    public bool CanChangeProvider => !IsBusy && !HasPendingReview;
    public bool CanChangeWorkingDirectory => !IsBusy && !HasPendingReview;
    public bool CanStopWorkCycle => IsBusy || IsComputerControlActive;
    public bool CanSend => !IsBusy
                           && !HasPendingReview
                           && IsProviderReady
                           && SelectedContext is not null
                           && !string.IsNullOrWhiteSpace(Prompt);
    public bool CanEnableSelectedOpenAi => _canEnableSelectedOpenAi
                                           && !IsBusy
                                           && !HasPendingReview;
    public string ComposerBlockerMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Prompt) || CanSend || IsBusy)
            {
                return string.Empty;
            }

            if (HasPendingReview)
            {
                return "Approve or reject the pending action before sending another message.";
            }

            if (!string.IsNullOrWhiteSpace(_providerAvailabilityBlocker))
            {
                return _providerAvailabilityBlocker;
            }

            return SelectedContext is null
                ? "Choose an AI Context before sending this message."
                : "The selected provider is not ready yet.";
        }
    }
    public bool HasComposerBlocker =>
        !string.IsNullOrWhiteSpace(ComposerBlockerMessage);

    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand StopWorkCycleCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand StopComputerControlCommand { get; }
    public RelayCommand EnableSelectedOpenAiCommand { get; }
    public RelayCommand ToggleDesktopLiveSessionCommand { get; }

    public void ExplainSendAvailability()
    {
        var explanation = ComposerBlockerMessage;
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            StatusMessage = explanation;
        }
    }

    public void RefreshState()
    {
        RefreshProviderState();
        RefreshContexts();
    }

    public bool TrySetWorkingDirectory(
        string? value,
        out string error)
    {
        error = string.Empty;
        if (!CanChangeWorkingDirectory)
        {
            error =
                "Wait for the current AI work cycle or review to finish before changing its working directory.";
            StatusMessage = error;
            return false;
        }

        try
        {
            var normalized = WorkspaceAiWorkingDirectory.Normalize(value);
            if (string.Equals(
                    _workingDirectory,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            ApplyWorkingDirectory(normalized);
            StatusMessage = normalized.Length == 0
                ? "This conversation has no working directory. Relative file and terminal actions remain disabled until you choose one."
                : $"Working directory: {WorkspaceAiWorkingDirectory.Display(normalized)}";
            ConversationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            error = exception.Message;
            StatusMessage = error;
            return false;
        }
    }

    public bool PrepareHyperpageProjectConversation(
        string projectRoot,
        string projectFoundationContext,
        HyperpageAiProvider provider,
        out string error)
    {
        error = string.Empty;
        if (IsBusy || HasPendingReview)
        {
            error =
                "Wait for the current AI work cycle or review before preparing a Hyperpage project conversation.";
            StatusMessage = error;
            return false;
        }

        try
        {
            if (Messages.Count > 0)
            {
                StartNewConversation();
            }
            RefreshState();
            var selection = ProviderModelOptions.FirstOrDefault(option =>
                provider == HyperpageAiProvider.Local
                    ? option.IsLocal
                    : !option.IsLocal);
            if (selection is not null)
            {
                SelectedProviderModel = selection;
            }
            if (!TrySetWorkingDirectory(projectRoot, out error))
            {
                return false;
            }

            _coordinator.SetHyperpageProjectWorkflow(enabled: true);
            _coordinator.SetConversationPrimer(projectFoundationContext);
            Prompt = string.Empty;
            AddAssistantMessage(
                "Let's Begin. Describe the project you'd like to build...");
            StatusMessage =
                "Project foundation loaded. The assistant will clarify scope, then implement the approved foundation in the project.";
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or InvalidOperationException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            error = exception.Message;
            StatusMessage = error;
            return false;
        }
    }

    public bool PrepareHyperpagePageConversation(
        string projectRoot,
        string pageContext,
        string description,
        out string error)
    {
        error = string.Empty;
        if (IsBusy || HasPendingReview)
        {
            error =
                "Wait for the current AI work cycle or review before preparing another page.";
            StatusMessage = error;
            return false;
        }

        try
        {
            RefreshState();
            if (!TrySetWorkingDirectory(projectRoot, out error))
            {
                return false;
            }

            _coordinator.SetHyperpageProjectWorkflow(enabled: true);
            _coordinator.SetConversationPrimer(
                "You are establishing the foundation of a newly created page in Hyperpage Studio. "
                + "The page already exists and is the active visual page. Treat the supplied page identity, file type, selected template, working directory, current revision, Context permissions, and review policy as authoritative. "
                + "Inspect the active visual page, then implement the user's description directly in that page with reviewed semantic Hyperpage tools and workspace tools only when editable source is required. "
                + "Use the existing starter as the foundation rather than replacing it blindly. Do not paste an installation guide, alter another page, host, or publish. Ask a concise clarifying question only when an unanswered choice would materially change the page; otherwise begin the reviewed implementation now.");
            Prompt = $"""
                Establish this new page according to the description below.

                [HYPERPAGE_PAGE_CONTEXT]
                {pageContext.Trim()}
                [/HYPERPAGE_PAGE_CONTEXT]

                [PAGE_DESCRIPTION]
                {description.Trim()}
                [/PAGE_DESCRIPTION]
                """;
            StatusMessage =
                "New-page context loaded. The assistant will establish the page through reviewed project tools.";
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or InvalidOperationException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            error = exception.Message;
            StatusMessage = error;
            return false;
        }
    }

    public bool PrepareWebTemplateCaptureConversation(
        string projectRoot,
        string url,
        string templateName,
        string description,
        string scope,
        string categoryGroup,
        string category,
        out string error)
    {
        error = string.Empty;
        if (IsBusy || HasPendingReview)
        {
            error =
                "Wait for the current AI work cycle or review before preparing a website template.";
            StatusMessage = error;
            return false;
        }

        try
        {
            if (!TrySetWorkingDirectory(projectRoot, out error))
            {
                return false;
            }
            _coordinator.SetConversationPrimer(
                "You are working inside Hyperpage Studio's reviewed website-template workflow. "
                + "Use host semantic tools only. A website capture is structure-only: never request or preserve scripts, remote assets, captured CSS, inline event code, form actions, tracking markup, or source-site copy. "
                + "The host and selected Context remain authoritative for HTTPS access, Create/Edit scope, review, and audit. "
                + "Ask at most three short clarifying questions only when an unanswered choice would materially change the template; otherwise proceed.");
            Prompt =
                "Create a Hyperpage Studio website template from this URL: "
                + url.Trim()
                + Environment.NewLine
                + $"Template name: {templateName.Trim()}"
                + Environment.NewLine
                + $"Description: {description.Trim()}"
                + Environment.NewLine
                + $"Template scope: {scope}"
                + Environment.NewLine
                + $"Category group: {categoryGroup.Trim()}"
                + Environment.NewLine
                + $"Pattern category: {category.Trim()}"
                + Environment.NewLine
                + "I confirm that I own this site or have permission to reuse its structure. "
                + "Read the active visual page to obtain its exact revision, then use capture_web_template with permission_confirmed=true. "
                + "The result must use placeholder copy and local placeholders only. Do not alter the active page, download assets, run code, host, or publish.";
            StatusMessage =
                "Website-template request prepared. The selected OpenAI or local model will use the Context-gated capture tool.";
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or InvalidOperationException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            error = exception.Message;
            StatusMessage = error;
            return false;
        }
    }

    public async Task<bool> SendPreparedPromptAsync()
    {
        if (!CanSend)
        {
            ExplainSendAvailability();
            return false;
        }
        await SendAsync();
        return true;
    }

    public WorkspaceAiContextDefinition CreateContextDraft() =>
        _contextService.CreateDraft(SelectedContextDefinition);

    // Context selection is intentionally independent of Explorer and active-tab
    // navigation. Retain this compatibility hook for older callers.
    public void SelectWorkspaceForPath(string? path)
    {
    }

    public void SaveContext(WorkspaceAiContextDefinition context)
    {
        var saved = _contextService.SaveContext(context);
        RefreshContexts(saved.Id);
        ResetConversationForContextChange();
    }

    public bool SetLiveSourceEnabled(string sourceId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (_isDisposed)
        {
            return false;
        }

        if (!enabled)
        {
            _liveSources.Remove(sourceId);
            CancelLiveActivity(sourceId);
            return true;
        }

        if (!IsProviderReady)
        {
            StatusMessage =
                "Live assistance requires a configured AI provider.";
            return false;
        }

        if (SelectedContextDefinition is not { AllowLiveAssistance: true })
        {
            StatusMessage =
                "Enable live assistance in the selected Context first.";
            return false;
        }

        _liveSources.Add(sourceId);
        StatusMessage = string.Empty;
        return true;
    }

    public bool IsLiveSourceEnabled(string sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId)
        && _liveSources.Contains(sourceId);

    public void QueueLiveActivity(WorkspaceAiLiveActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (_isDisposed
            || (IsBusy && !_isLiveRequestRunning)
            || HasPendingReview
            || !_liveSources.Contains(activity.SourceId)
            || SelectedContextDefinition is not { AllowLiveAssistance: true })
        {
            return;
        }

        _pendingLiveActivity = activity;
        _liveActivityVersion++;
        SchedulePendingLiveActivity();
    }

    private void ToggleDesktopLiveSession()
    {
        if (_isDesktopLiveSessionEnabled)
        {
            SetDesktopLiveSessionEnabled(false);
            StatusMessage = string.Empty;
            return;
        }

        if (SelectedContextDefinition is not
            {
                AllowLiveAssistance: true,
                ComputerControl: WorkspaceAiComputerControlMode.Desktop
            })
        {
            StatusMessage =
                "Full desktop live assistance requires Live Assistance and Full Windows desktop control in the selected Context.";
            return;
        }

        if (!SetLiveSourceEnabled(DesktopLiveSourceId, true))
        {
            return;
        }

        SetDesktopLiveSessionEnabled(true);
        StatusMessage = "Full desktop live assistance is active.";
    }

    private void StopAiControl()
    {
        _computerControl.Stop();
        CancelLiveActivity();
        SetDesktopLiveSessionEnabled(false);
        StatusMessage = "AI control stopped.";
    }

    private void SetDesktopLiveSessionEnabled(bool enabled)
    {
        if (_isDesktopLiveSessionEnabled == enabled)
        {
            return;
        }

        _isDesktopLiveSessionEnabled = enabled;
        if (!enabled)
        {
            _liveSources.Remove(DesktopLiveSourceId);
            CancelLiveActivity(DesktopLiveSourceId);
        }

        OnPropertyChanged(nameof(IsDesktopLiveSessionEnabled));
        DesktopLiveSessionChanged?.Invoke(
            this,
            new WorkspaceAiDesktopLiveSessionChangedEventArgs(enabled));
    }

    public void CancelLiveActivity(string? sourceId = null)
    {
        var cancelPending = sourceId is null
                            || _pendingLiveActivity is { } pending
                            && string.Equals(
                                pending.SourceId,
                                sourceId,
                                StringComparison.Ordinal);
        if (cancelPending)
        {
            _pendingLiveActivity = null;
            _liveActivityVersion++;
            CancelAndDispose(ref _liveDelayCancellation);
        }

        if (sourceId is null
            || string.Equals(
                sourceId,
                _activeLiveSourceId,
                StringComparison.Ordinal))
        {
            CancelAndDispose(ref _liveRequestCancellation);
        }
    }

    public void CancelActiveRequest()
    {
        _activeRequestCancellation?.Cancel();
        CancelLiveActivity();
        _computerControl.Stop();
    }

    private void StopWorkCycle()
    {
        if (!CanStopWorkCycle)
        {
            return;
        }

        StatusMessage =
            "Stop requested for the current AI work cycle.";
        CancelActiveRequest();
    }

    public async Task ApprovePendingAsync(
        bool userConfirmedExecution = false,
        CancellationToken cancellationToken = default)
    {
        if (!CanReview)
        {
            return;
        }

        IsBusy = true;
        var requestCancellation = BeginActiveRequest(cancellationToken);
        try
        {
            var result = await _coordinator.ApproveAsync(
                userConfirmedExecution,
                requestCancellation.Token);
            PendingReview = result.PendingReview;
            AddAssistantMessage(result.AssistantText);
            ApplyTurnMetadata(result);
        }
        catch (OperationCanceledException)
            when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AddHostError(exception.Message);
        }
        finally
        {
            CompleteActiveRequest(requestCancellation);
            IsBusy = false;
        }
    }

    public async Task RejectPendingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanReview)
        {
            return;
        }

        IsBusy = true;
        var requestCancellation = BeginActiveRequest(cancellationToken);
        try
        {
            var result = await _coordinator.RejectAsync(
                requestCancellation.Token);
            PendingReview = result.PendingReview;
            AddAssistantMessage(result.AssistantText);
            ApplyTurnMetadata(result);
        }
        catch (OperationCanceledException)
            when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AddHostError(exception.Message);
        }
        finally
        {
            CompleteActiveRequest(requestCancellation);
            IsBusy = false;
        }
    }

    public void StartNewConversation()
    {
        CancelLiveActivity();
        SetDesktopLiveSessionEnabled(false);
        ConversationChanging?.Invoke(this, EventArgs.Empty);
        _coordinator.Reset();
        ApplyWorkingDirectory(string.Empty);
        Messages.Clear();
        PendingReview = null;
        Prompt = string.Empty;
        StatusMessage = string.Empty;
        _lastLiveComment = string.Empty;
        OnMessagesChanged();
    }

    public void BindConversationId(string conversationId) =>
        _coordinator.BindConversationId(conversationId);

    public IReadOnlyList<AiConversationItem> SnapshotConversationItems()
    {
        long sequence = 0;
        return Messages.Select(message => new AiConversationItem
        {
            Sequence = ++sequence,
            Role = ToConversationRole(message),
            Kind = AiConversationItemKind.Message,
            Text = BoundConversationText(message.Text),
            CreatedAtUtc = message.Timestamp.ToUniversalTime()
        })
            .ToList();
    }

    public string ExportContinuationContext()
    {
        var messages = Messages
            .Where(message => ToConversationRole(message)
                              is AiConversationRole.User
                                  or AiConversationRole.Assistant)
            .Select(message => new ContinuationMessage(
                ToConversationRole(message) == AiConversationRole.User
                    ? "user"
                    : "assistant",
                BoundConversationText(message.Text)))
            .ToList();
        return BuildContinuationContext(messages);
    }

    public void RestoreConversation(LocalAiConversationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CancelLiveActivity();
        SetDesktopLiveSessionEnabled(false);
        var workingDirectoryWarning = string.Empty;
        try
        {
            ApplyWorkingDirectory(WorkspaceAiWorkingDirectory.Normalize(
                document.WorkingDirectory));
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException
                                         or UnauthorizedAccessException)
        {
            ApplyWorkingDirectory(string.Empty);
            workingDirectoryWarning =
                $"The saved working directory is unavailable on this PC: {exception.Message}";
        }

        if (!string.IsNullOrWhiteSpace(document.ProviderId)
            && !string.IsNullOrWhiteSpace(document.ModelId))
        {
            var restoredSelection = new WorkspaceAiSessionModelSelection(
                document.ProviderId,
                document.ModelId);
            _coordinator.SetSessionModelSelection(restoredSelection);
            _sessionModelSelection = _coordinator.SessionModelSelection;
        }

        _coordinator.Reset();
        var continuationWasDiscarded = false;
        try
        {
            _coordinator.RestoreContinuationContext(
                document.CompactedContext);
        }
        catch (InvalidDataException)
        {
            var fallback = document.Items
                .Where(item => item.Kind == AiConversationItemKind.Message
                               && (item.Role is AiConversationRole.User
                                   or AiConversationRole.Assistant)
                               && !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => new ContinuationMessage(
                    item.Role == AiConversationRole.User
                        ? "user"
                        : "assistant",
                    BoundConversationText(item.Text)))
                .ToList();
            try
            {
                _coordinator.RestoreContinuationContext(
                    BuildContinuationContext(fallback));
            }
            catch (InvalidDataException)
            {
                _coordinator.Reset();
                continuationWasDiscarded = true;
            }
        }

        var isHyperpageProjectWorkflow =
            IsRestoredHyperpageProjectConversation(document);
        _coordinator.SetHyperpageProjectWorkflow(
            isHyperpageProjectWorkflow);
        if (isHyperpageProjectWorkflow)
        {
            _coordinator.SetConversationPrimer($"""
                [HYPERPAGE_PROJECT_SESSION_RESTORED]
                The device-local working directory is still {WorkingDirectory}. This is an existing Hyperpage Studio project workflow. Inspect visual-page.project.json and the current project files before continuing. Preserve the selected Context permissions and review requirements. When the user requests implementation, use the enabled Hyperpage or workspace tools to update the project directly; do not give them code to install manually.
                [/HYPERPAGE_PROJECT_SESSION_RESTORED]
                """);
        }

        Messages.Clear();
        foreach (var item in document.Items.Where(item =>
                     item.Kind == AiConversationItemKind.Message))
        {
            Messages.Add(new WorkspaceAiChatMessageViewModel(
                item.Role switch
                {
                    AiConversationRole.User => "You",
                    AiConversationRole.Assistant => "Hypershell AI",
                    _ => "Host"
                },
                item.Text,
                IsError: item.Role == AiConversationRole.Host,
                CreatedAtUtc: item.CreatedAtUtc));
        }

        PendingReview = null;
        Prompt = string.Empty;
        StatusMessage = continuationWasDiscarded
            ? "The saved AI continuation context could not be restored. The visible conversation history remains available."
            : workingDirectoryWarning;
        _lastLiveComment = string.Empty;
        RefreshProviderState();
        OnMessagesChanged();
    }

    private async Task SendAsync()
    {
        if (!CanSend || SelectedContext is null)
        {
            return;
        }

        CancelLiveActivity();
        var userText = Prompt.Trim();
        var contextId = SelectedContext.Id;
        Prompt = string.Empty;
        Messages.Add(new WorkspaceAiChatMessageViewModel("You", userText));
        OnMessagesChanged();
        IsBusy = true;
        var requestCancellation = BeginActiveRequest();
        try
        {
            var result = await _coordinator.SendAsync(
                contextId,
                userText,
                requestCancellation.Token);
            PendingReview = result.PendingReview;
            AddAssistantMessage(result.AssistantText);
            ApplyTurnMetadata(result);
        }
        catch (OperationCanceledException)
            when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            AddHostError(exception.Message);
        }
        finally
        {
            CompleteActiveRequest(requestCancellation);
            IsBusy = false;
        }
    }

    private CancellationTokenSource BeginActiveRequest(
        CancellationToken cancellationToken = default)
    {
        var requestCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken)
            : new CancellationTokenSource();
        _activeRequestCancellation = requestCancellation;
        return requestCancellation;
    }

    private void CompleteActiveRequest(
        CancellationTokenSource requestCancellation)
    {
        if (ReferenceEquals(
                _activeRequestCancellation,
                requestCancellation))
        {
            _activeRequestCancellation = null;
        }

        requestCancellation.Dispose();
    }

    private void SchedulePendingLiveActivity()
    {
        CancelAndDispose(ref _liveDelayCancellation);
        if (_pendingLiveActivity is null || _isDisposed)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _liveDelayCancellation = cancellation;
        var version = _liveActivityVersion;
        _ = ProcessLiveActivityAsync(
            version,
            LiveActivityDelay,
            cancellation);
    }

    private async Task ProcessLiveActivityAsync(
        long version,
        TimeSpan delay,
        CancellationTokenSource delayCancellation)
    {
        try
        {
            await Task.Delay(delay, delayCancellation.Token);
        }
        catch (OperationCanceledException)
            when (delayCancellation.IsCancellationRequested)
        {
            return;
        }

        if (_isDisposed
            || !ReferenceEquals(_liveDelayCancellation, delayCancellation)
            || version != _liveActivityVersion)
        {
            return;
        }

        _liveDelayCancellation = null;
        delayCancellation.Dispose();
        if (_isLiveRequestRunning)
        {
            return;
        }

        if (IsBusy || HasPendingReview)
        {
            _pendingLiveActivity = null;
            return;
        }

        var activity = _pendingLiveActivity;
        var context = SelectedContext;
        if (activity is null
            || context is null
            || !context.Definition.AllowLiveAssistance
            || !_liveSources.Contains(activity.SourceId))
        {
            _pendingLiveActivity = null;
            return;
        }

        _pendingLiveActivity = null;
        var requestCancellation = new CancellationTokenSource();
        _liveRequestCancellation = requestCancellation;
        _activeLiveActivityVersion = version;
        _activeLiveSourceId = activity.SourceId;
        _isLiveRequestRunning = true;
        OnPropertyChanged(nameof(IsLiveRequestRunning));
        IsBusy = true;
        try
        {
            var result = await _coordinator.SendAsync(
                context.Id,
                BuildLiveActivityPrompt(activity),
                requestCancellation.Token);
            PendingReview = result.PendingReview;
            ApplyTurnMetadata(result);
            var response = result.AssistantText.Trim();
            if (!string.Equals(
                    response,
                    "NO_COMMENT",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    response,
                    _lastLiveComment,
                    StringComparison.Ordinal))
            {
                _lastLiveComment = response;
                Messages.Add(new WorkspaceAiChatMessageViewModel(
                    "Hypershell AI · Live",
                    response));
                OnMessagesChanged();
            }

            _lastLiveError = string.Empty;
        }
        catch (OperationCanceledException)
            when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ReportLiveError(exception.Message);
        }
        catch (Exception exception)
        {
            ReportLiveError(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_liveRequestCancellation, requestCancellation))
            {
                _liveRequestCancellation = null;
            }

            requestCancellation.Dispose();
            _activeLiveSourceId = string.Empty;
            _isLiveRequestRunning = false;
            OnPropertyChanged(nameof(IsLiveRequestRunning));
            IsBusy = false;

            if (!_isDisposed
                && _pendingLiveActivity is not null
                && _liveActivityVersion > _activeLiveActivityVersion)
            {
                SchedulePendingLiveActivity();
            }
        }
    }

    private void ReportLiveError(string detail)
    {
        if (_isDisposed)
        {
            return;
        }

        var message = $"Live assistance paused: {detail}";
        if (string.Equals(
                _lastLiveError,
                message,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastLiveError = message;
        AddHostError(message);
    }

    private static string BuildLiveActivityPrompt(
        WorkspaceAiLiveActivity activity) =>
        $$"""
        This is an automatic event in the user's ongoing Hypershell live-assistance session, not a new isolated conversation.
        Activity: {{activity.Kind}}
        Source: {{activity.Title}}
        Signal: {{activity.Signal}}

        Inspect the current state with the tools authorized by the selected Context. For desktop activity, begin with a computer screenshot. You may read or edit files and the active document, stage or execute terminal commands, access the internet, and control the authorized computer scope when those tools are available and doing so would materially help. Preserve the user's intent and existing work. When standing permission is enabled, briefly explain the improvement you identified and carry it through in this same live session; when review is required, propose the action through the normal review tool. If no useful intervention or comment is warranted, respond with exactly NO_COMMENT.
        """;

    private static void CancelAndDispose(
        ref CancellationTokenSource? cancellation)
    {
        var current = cancellation;
        cancellation = null;
        if (current is null)
        {
            return;
        }

        current.Cancel();
        current.Dispose();
    }

    private void RefreshProviderState()
    {
        var deviceConfiguration = _configurationService.Load();
        var warning = _configurationService.LastLoadWarning;
        var configuration = _sessionModelSelection.ApplyTo(
            deviceConfiguration);
        IsProviderReady = string.IsNullOrWhiteSpace(warning)
                          && (configuration.IsLocal
                              ? _localRuntime.IsInstalled
                              : configuration.AllowCloudRequests
                                && _credentialStore.Exists);
        _canEnableSelectedOpenAi = string.IsNullOrWhiteSpace(warning)
                                   && !configuration.IsLocal
                                   && !configuration.AllowCloudRequests;
        _providerAvailabilityBlocker = !string.IsNullOrWhiteSpace(warning)
            ? warning
            : configuration.IsLocal
                ? _localRuntime.IsInstalled
                    ? string.Empty
                    : "The local AI runtime is not installed. Open AI settings to install it."
                : !configuration.AllowCloudRequests
                    ? "OpenAI is selected, but cloud requests are off. Enable OpenAI to send this message."
                    : !_credentialStore.Exists
                        ? "OpenAI is selected, but no API key is saved. Open AI settings to add one."
                        : string.Empty;
        var effectiveComputerControl = GetEffectiveComputerControl();
        var effectiveModel = WorkspaceAiModelCatalog.ResolveContextModel(
            configuration,
            _sessionModelSelection.ModelId,
            effectiveComputerControl);
        var localModelName = WorkspaceAiModelCatalog.Find(
                                 effectiveModel)?.DisplayName
                             ?? effectiveModel;
        var routedForDesktop = configuration.IsLocal
                               && !string.Equals(
                                   effectiveModel,
                                   _sessionModelSelection.ModelId,
                                   StringComparison.Ordinal)
                               && effectiveComputerControl
                               != WorkspaceAiComputerControlMode.Off;
        ProviderStatus = !string.IsNullOrWhiteSpace(warning)
            ? warning
            : configuration.IsLocal
                ? _localRuntime.IsInstalled
                    ? $"Local ready · {localModelName}"
                      + (routedForDesktop
                          ? " · selected for desktop capability"
                          : string.Empty)
                    : "llama.cpp install required"
                : !configuration.AllowCloudRequests
                ? "Cloud requests off"
                : !_credentialStore.Exists
                    ? "API key required"
                    : $"OpenAI ready · {configuration.Model}";
        RefreshProviderModelOptions(configuration);
        OnPropertyChanged(nameof(CanEnableSelectedOpenAi));
        EnableSelectedOpenAiCommand.RaiseCanExecuteChanged();
        NotifySendAvailabilityChanged();
    }

    private void EnableSelectedOpenAi()
    {
        if (!CanEnableSelectedOpenAi)
        {
            return;
        }

        try
        {
            var deviceConfiguration = _configurationService.Load();
            var configuration = _sessionModelSelection.ApplyTo(
                deviceConfiguration);
            if (configuration.IsLocal)
            {
                return;
            }

            _configurationService.Save(deviceConfiguration with
            {
                AllowCloudRequests = !deviceConfiguration.IsLocal,
                OpenAiCloudRequestsEnabled = true
            });
            RefreshProviderState();
            StatusMessage = _credentialStore.Exists
                ? "OpenAI requests are enabled. Your message is ready to send."
                : "OpenAI requests are enabled. Add an API key in AI settings to continue.";
        }
        catch (Exception exception) when (IsExpectedException(exception)
                                          || exception is ArgumentException)
        {
            StatusMessage = exception.Message;
            RefreshProviderState();
        }
    }

    private void RefreshProviderModelOptions(
        WorkspaceAiProviderConfiguration configuration)
    {
        var openAiModel = _sessionModelSelection.IsLocal
            ? configuration.OpenAiModel
            : _sessionModelSelection.ModelId;
        var options = new List<WorkspaceAiProviderModelOption>
        {
            new(
                WorkspaceAiProviderConfiguration.OpenAiProviderId,
                openAiModel,
                "OpenAI")
        };
        options.AddRange(WorkspaceAiModelCatalog.Generative.Select(model =>
            new WorkspaceAiProviderModelOption(
                WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                model.Id,
                "Local")));

        ProviderModelOptions.Clear();
        foreach (var option in options)
        {
            ProviderModelOptions.Add(option);
        }

        var effectiveModel = WorkspaceAiModelCatalog.ResolveContextModel(
            configuration,
            _sessionModelSelection.ModelId,
            GetEffectiveComputerControl());
        var selected = options.FirstOrDefault(option =>
                           string.Equals(
                               option.ProviderId,
                               _sessionModelSelection.ProviderId,
                               StringComparison.Ordinal)
                           && string.Equals(
                               option.ModelId,
                               effectiveModel,
                               StringComparison.Ordinal))
                       ?? options.First(option => string.Equals(
                           option.ProviderId,
                           _sessionModelSelection.ProviderId,
                           StringComparison.Ordinal));

        var wasApplying = _isApplyingProviderSelection;
        _isApplyingProviderSelection = true;
        try
        {
            if (!ReferenceEquals(_selectedProviderModel, selected))
            {
                _selectedProviderModel = selected;
                OnPropertyChanged(nameof(SelectedProviderModel));
            }
        }
        finally
        {
            _isApplyingProviderSelection = wasApplying;
        }
    }

    private void ApplyProviderModelSelection(
        WorkspaceAiProviderModelOption selection)
    {
        var wasApplying = _isApplyingProviderSelection;
        _isApplyingProviderSelection = true;
        try
        {
            var configuration = _configurationService.Load();
            if (!string.IsNullOrWhiteSpace(
                    _configurationService.LastLoadWarning))
            {
                throw new WorkspaceAiProviderConfigurationException(
                    _configurationService.LastLoadWarning);
            }

            var isLocal = selection.IsLocal;
            var wasRoutedForDesktop = false;
            if (isLocal
                && GetEffectiveComputerControl()
                != WorkspaceAiComputerControlMode.Off
                && WorkspaceAiModelCatalog.Find(selection.ModelId)
                   is not { SupportsVision: true })
            {
                selection = ProviderModelOptions.First(option =>
                    option.IsLocal
                    && string.Equals(
                        option.ModelId,
                        WorkspaceAiModelCatalog.EngineerModelId,
                        StringComparison.Ordinal));
                wasRoutedForDesktop = true;
            }

            var sessionSelection = new WorkspaceAiSessionModelSelection(
                isLocal
                    ? WorkspaceAiProviderConfiguration.LlamaCppProviderId
                    : WorkspaceAiProviderConfiguration.OpenAiProviderId,
                selection.ModelId);
            _ = sessionSelection.ApplyTo(configuration);
            _coordinator.SetSessionModelSelection(sessionSelection);
            _sessionModelSelection = _coordinator.SessionModelSelection;
            SetProperty(
                ref _selectedProviderModel,
                selection,
                nameof(SelectedProviderModel));
            OnPropertyChanged(nameof(SessionProviderId));
            OnPropertyChanged(nameof(SessionModelId));
            RefreshProviderState();
            StatusMessage =
                isLocal
                    ? wasRoutedForDesktop
                        ? $"This conversation now uses {selection.DisplayName}. This Context grants desktop control, so Hypershell selected its vision-capable Engineer model."
                        : $"This conversation now uses {selection.DisplayName}. Other conversations are unchanged."
                    : IsProviderReady
                        ? $"This conversation now uses {selection.DisplayName}. Other conversations are unchanged."
                        : $"This conversation now uses {selection.DisplayName}. Enable OpenAI access in this pane or Settings before sending.";
            ConversationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsExpectedException(exception)
                                          || exception is ArgumentException)
        {
            StatusMessage = exception.Message;
            RefreshProviderState();
            OnPropertyChanged(nameof(SelectedProviderModel));
        }
        finally
        {
            _isApplyingProviderSelection = wasApplying;
        }
    }

    private void RefreshContexts(string? selectId = null)
    {
        selectId ??= SelectedContext?.Id
                     ?? _contextService.SelectedContextId;
        Contexts.Clear();
        foreach (var context in _contextService.GetContexts())
        {
            Contexts.Add(new WorkspaceAiContextOptionViewModel(context));
        }

        var selected = Contexts.FirstOrDefault(context => string.Equals(
                           context.Id,
                           selectId,
                           StringComparison.OrdinalIgnoreCase))
                       ?? Contexts.FirstOrDefault();
        _isApplyingContext = true;
        try
        {
            SetProperty(ref _selectedContext, selected, nameof(SelectedContext));
            if (selected is not null)
            {
                ApplyBehavior(selected.Definition);
            }
        }
        finally
        {
            _isApplyingContext = false;
        }

        OnPropertyChanged(nameof(SelectedContextDefinition));
        NotifySendAvailabilityChanged();
    }

    private void ApplyBehavior(WorkspaceAiContextDefinition context)
    {
        _isApplyingContext = true;
        try
        {
            SetProperty(ref _activeModel, context.Model, nameof(ActiveModel));
            SetProperty(
                ref _activeReasoningEffort,
                context.ReasoningEffort,
                nameof(ActiveReasoningEffort));
            SetProperty(
                ref _activeTextVerbosity,
                context.TextVerbosity,
                nameof(ActiveTextVerbosity));
            OnPropertyChanged(nameof(SelectedContextDefinition));
        }
        finally
        {
            _isApplyingContext = false;
        }

        RefreshProviderState();
    }

    private WorkspaceAiComputerControlMode GetEffectiveComputerControl() =>
        SelectedContextDefinition is
        {
            AskBeforeChanges: false,
            ComputerControl: var computerControl
        }
            ? computerControl
            : WorkspaceAiComputerControlMode.Off;

    private void PromoteBehaviorToCustom()
    {
        if (_isApplyingContext
            || SelectedContext is null
            || string.IsNullOrWhiteSpace(ActiveModel)
            || !ReasoningEfforts.Contains(
                ActiveReasoningEffort,
                StringComparer.OrdinalIgnoreCase)
            || !TextVerbosities.Contains(
                ActiveTextVerbosity,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var custom = _contextService.UpdateCustomBehavior(
                SelectedContext.Definition,
                ActiveModel,
                ActiveReasoningEffort,
                ActiveTextVerbosity);
            RefreshContexts(custom.Id);
        }
        catch (Exception exception) when (IsExpectedException(exception)
                                         || exception is ArgumentException)
        {
            StatusMessage = exception.Message;
        }
    }

    private void ResetConversationForContextChange()
    {
        if (_isApplyingContext)
        {
            return;
        }

        CancelLiveActivity();
        SetDesktopLiveSessionEnabled(false);
        _liveSources.Clear();
        ConversationChanging?.Invoke(this, EventArgs.Empty);
        _coordinator.Reset();
        Messages.Clear();
        PendingReview = null;
        Prompt = string.Empty;
        StatusMessage = string.Empty;
        _lastLiveComment = string.Empty;
        OnMessagesChanged();
        OnPropertyChanged(nameof(SelectedContextDefinition));
        LiveSourcesInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void AddAssistantMessage(string text)
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Messages.Add(new WorkspaceAiChatMessageViewModel(
            "Hypershell AI",
            text.Trim()));
        OnMessagesChanged();
    }

    private void OnCoordinatorProgress(
        object? sender,
        WorkspaceAiConversationProgressEventArgs eventArgs)
    {
        if (_isDisposed
            || string.IsNullOrWhiteSpace(eventArgs.AssistantText))
        {
            return;
        }

        Messages.Add(new WorkspaceAiChatMessageViewModel(
            _isLiveRequestRunning
                ? "Hypershell AI · Live"
                : "Hypershell AI",
            eventArgs.AssistantText.Trim()));
        OnMessagesChanged();
    }

    private void AddHostError(string message)
    {
        if (_isDisposed)
        {
            return;
        }

        StatusMessage = message;
        Messages.Add(new WorkspaceAiChatMessageViewModel(
            "Host",
            message,
            IsError: true));
        OnMessagesChanged();
    }

    private void OnMessagesChanged()
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(IsConversationEmpty));
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    private static AiConversationRole ToConversationRole(
        WorkspaceAiChatMessageViewModel message) =>
        message.Author.StartsWith("You", StringComparison.OrdinalIgnoreCase)
            ? AiConversationRole.User
            : message.Author.StartsWith(
                "Hypershell AI",
                StringComparison.OrdinalIgnoreCase)
                ? AiConversationRole.Assistant
                : AiConversationRole.Host;

    private static string BoundConversationText(string value)
    {
        var normalized = (value ?? string.Empty).Replace("\0", string.Empty);
        var maximum = HypershellCloudContract
            .MaximumConversationTextCharacters;
        return normalized.Length <= maximum
            ? normalized
            : normalized[..(maximum - 16)] + "\n[TRUNCATED]";
    }

    private static string BuildContinuationContext(
        IReadOnlyList<ContinuationMessage> source)
    {
        var selected = new List<ContinuationMessage>();
        var remainingCharacters = 400 * 1024;
        for (var index = source.Count - 1; index >= 0; index--)
        {
            var item = source[index];
            var estimated = item.Content.Length + item.Role.Length + 32;
            if (estimated > remainingCharacters && selected.Count > 0)
            {
                break;
            }

            selected.Insert(0, item);
            remainingCharacters -= Math.Min(
                remainingCharacters,
                estimated);
        }

        var payload = JsonSerializer.Serialize(
            selected,
            ContinuationJsonOptions);
        while (payload.Length
               > HypershellCloudContract
                   .MaximumConversationCompactionCharacters
               && selected.Count > 0)
        {
            selected.RemoveAt(0);
            payload = JsonSerializer.Serialize(
                selected,
                ContinuationJsonOptions);
        }

        return payload;
    }

    private bool IsRestoredHyperpageProjectConversation(
        LocalAiConversationDocument document)
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectory)
            || !File.Exists(Path.Combine(
                WorkingDirectory,
                VisualPageProjectService.ProjectManifestFileName)))
        {
            return false;
        }

        return document.Items.Any(item =>
                   item.Role == AiConversationRole.Assistant
                   && item.Text.StartsWith(
                       "Let's Begin. Describe the project you'd like to build...",
                       StringComparison.Ordinal))
               || document.Title.EndsWith(
                   " · Hyperpage",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void OnComputerControlStateChanged(
        object? sender,
        EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(IsComputerControlActive));
        OnPropertyChanged(nameof(IsComputerControlAvailable));
        OnPropertyChanged(nameof(CanStopWorkCycle));
        StopWorkCycleCommand.RaiseCanExecuteChanged();
    }

    private void NotifySendAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanEnableSelectedOpenAi));
        OnPropertyChanged(nameof(ComposerBlockerMessage));
        OnPropertyChanged(nameof(HasComposerBlocker));
        SendCommand.RaiseCanExecuteChanged();
        EnableSelectedOpenAiCommand.RaiseCanExecuteChanged();
    }

    private void ApplyWorkingDirectory(string value)
    {
        _coordinator.SetWorkingDirectory(value);
        if (!SetProperty(
                ref _workingDirectory,
                value,
                nameof(WorkingDirectory)))
        {
            return;
        }

        OnPropertyChanged(nameof(WorkingDirectoryDisplay));
        OnPropertyChanged(nameof(HasWorkingDirectory));
    }

    private void ApplyTurnMetadata(
        WorkspaceAiConversationTurnResult result)
    {
        LastRunDiagnosticsPath = result.DiagnosticsPath;
        if (string.IsNullOrWhiteSpace(result.DiagnosticsPath))
        {
            StatusMessage = result.StatusMessage;
            return;
        }

        string displayPath;
        try
        {
            displayPath = string.IsNullOrWhiteSpace(_workingDirectory)
                ? result.DiagnosticsPath
                : Path.GetRelativePath(
                    _workingDirectory,
                    result.DiagnosticsPath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException)
        {
            displayPath = result.DiagnosticsPath;
        }

        StatusMessage = $"{result.StatusMessage} · Run details: {displayPath}";
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _coordinator.Progress -= OnCoordinatorProgress;
        _computerControl.StateChanged -= OnComputerControlStateChanged;
        CancelActiveRequest();
        CancelLiveActivity();
        SetDesktopLiveSessionEnabled(false);
        _liveSources.Clear();
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is WorkspaceAiProviderException
            or WorkspaceAiProviderConfigurationException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException;
}

public enum WorkspaceAiLiveActivityKind
{
    DocumentChanged,
    TerminalDraftChanged,
    TerminalCommandCompleted,
    DesktopActivityPaused
}

public sealed record WorkspaceAiLiveActivity(
    string SourceId,
    WorkspaceAiLiveActivityKind Kind,
    string Title,
    string Signal);

public sealed class WorkspaceAiDesktopLiveSessionChangedEventArgs : EventArgs
{
    public WorkspaceAiDesktopLiveSessionChangedEventArgs(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public bool IsEnabled { get; }
}

public sealed record WorkspaceAiChatMessageViewModel(
    string Author,
    string Text,
    bool IsError = false,
    DateTimeOffset? CreatedAtUtc = null)
{
    public DateTimeOffset Timestamp { get; } =
        CreatedAtUtc ?? DateTimeOffset.Now;
    public string TimestampText => Timestamp.ToString("t");
    public bool IsUser => Author.StartsWith(
        "You",
        StringComparison.OrdinalIgnoreCase);
    public bool IsAssistant => Author.StartsWith(
        "Hypershell AI",
        StringComparison.OrdinalIgnoreCase);
}

public sealed record WorkspaceAiProviderModelOption(
    string ProviderId,
    string ModelId,
    string DisplayName)
{
    public bool IsLocal => string.Equals(
        ProviderId,
        WorkspaceAiProviderConfiguration.LlamaCppProviderId,
        StringComparison.Ordinal);

    public override string ToString() => DisplayName;
}

internal sealed record ContinuationMessage(string Role, string Content);

public sealed class WorkspaceAiContextOptionViewModel
{
    public WorkspaceAiContextOptionViewModel(
        WorkspaceAiContextDefinition definition)
    {
        Definition = definition;
    }

    public WorkspaceAiContextDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;

    public override string ToString() => Name;
}
