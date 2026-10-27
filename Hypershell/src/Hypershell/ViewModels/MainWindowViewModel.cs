using Hypershell.Infrastructure;
using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hypershell.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly ProfileAvatarService _profileAvatarService;
    private readonly WorkspacePanelRegistry _workspacePanelRegistry;
    private readonly AppSettings _settings;
    private readonly CommandCatalogService _catalogService;
    private readonly ExtensionDiscoveryService _extensionDiscoveryService;
    private readonly HyperlibPackageService _hyperlibPackageService;
    private readonly ExtensionApprovalService _extensionApprovalService;
    private readonly TextDocumentService _textDocumentService = new();
    private readonly DocumentProviderRegistry _documentProviderRegistry;
    private readonly DocumentExportService _documentExportService = new();
    private readonly PortableProfileService _portableProfileService = new();
    private readonly ProfileSyncDeviceStateService _profileSyncDeviceStateService;
    private readonly ProfileSyncCoordinator _profileSyncCoordinator;
    private readonly ProfileSyncSessionService _profileSyncSessionService;
    private readonly ISshCredentialStore _sshCredentialStore;
    private readonly SshWorkspaceConnectionService _sshWorkspaceConnections;
    private readonly GitCommandRunnerRegistry _gitCommandRunners;
    private readonly GitRepositoryService _gitRepositoryService;
    private readonly WorkspaceFileSystemProviderRegistry _workspaceFileSystems;
    private readonly WorkspaceDocumentService _workspaceDocumentService;
    private readonly HashSet<string> _registeredSshWorkspaceProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ProfileRoamingDeviceStateService
        _profileRoamingDeviceStateService;
    private readonly WorkspaceProfileRoamingService
        _workspaceProfileRoamingService;
    private readonly HyperlibPackageRoamingService
        _hyperlibPackageRoamingService;
    private readonly LocalAiConversationStore _localAiConversationStore;
    private readonly ConversationRoamingService _conversationRoamingService;
    private readonly ApplicationUpdateService _applicationUpdateService;
    private readonly WorkspaceAiAuditService _workspaceAiAuditService;
    private readonly WorkspaceAiContextService _workspaceAiContextService;
    private readonly WorkspaceAiComputerControlService
        _workspaceAiComputerControlService;
    private readonly WindowsUserActivityMonitor
        _workspaceAiUserActivityMonitor;
    private readonly WorkspaceAiPermissionBroker _workspaceAiPermissionBroker;
    private readonly WorkspaceAiOperationService _workspaceAiOperationService;
    private readonly WorkspaceAiGitService _workspaceAiGitService;
    private readonly WorkspaceAiProviderConfigurationService
        _workspaceAiProviderConfigurationService;
    private readonly IWorkspaceAiCredentialStore _workspaceAiCredentialStore;
    private readonly WorkspaceAiLocalRuntimeService _workspaceAiLocalRuntime;
    private readonly WorkspaceAiEmbeddingService _workspaceAiEmbeddings;
    private readonly HyperlibSemanticSearchService _hyperlibSemanticSearch;
    private readonly ResponsesWorkspaceAiProvider _workspaceAiProvider;
    private readonly WorkspaceAiToolHost _workspaceAiToolHost;
    private readonly LanguageToolDiscoveryService _languageToolDiscoveryService;
    private readonly ExternalDocumentFormatterService _externalDocumentFormatterService;
    private readonly CompiledDocumentExecutionService
        _compiledDocumentExecutionService;
    private readonly EditorSessionService _editorSessionService;
    private readonly SemaphoreSlim _editorSessionSaveGate = new(1, 1);
    private readonly SemaphoreSlim _documentFormatGate = new(1, 1);
    private readonly SemaphoreSlim _documentExecutionGate = new(1, 1);
    private readonly SemaphoreSlim _documentExportGate = new(1, 1);
    private readonly SemaphoreSlim _cloudSyncGate = new(1, 1);
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _autosaveCueTimer;
    private readonly DispatcherTimer _statusCueTimer;
    private readonly DispatcherTimer _editorRecoveryTimer;
    private readonly DispatcherTimer _cloudSyncTimer;
    private readonly Dictionary<string, int> _terminalNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TerminalSettings> _observedTerminalSettings = [];
    private readonly ApplicationPreferences _runtimeApplicationPreferences;
    private readonly ExplorerSettings _runtimeExplorerSettings;
    private WorkspaceLayoutSnapshot _runtimeWorkspaceLayout;
    private ExtensionDiscoverySnapshot _extensionCatalog;
    private DocumentProviderCatalogSnapshot _documentProviderCatalog;
    private LanguageToolCatalogSnapshot _languageToolCatalog;
    private IReadOnlyList<TerminalConfigurationProfile> _runtimeTerminalConfigurationProfiles;
    private IWorkspaceTab? _activeWorkspaceTab;
    private WorkspaceAiConversationTabViewModel?
        _selectedWorkspaceAiConversation;
    private TerminalProfile _selectedTerminalProfile;
    private bool _isFileExplorerOpen = true;
    private bool _isCommandBuilderOpen = true;
    private string _activeFileExplorerSideToolId =
        WorkspaceSideToolRegistry.FileExplorerToolId;
    private CommandBuilderPanePage _commandBuilderPage =
        CommandBuilderPanePage.Builder;
    private string _statusMessage = string.Empty;
    private string _autosaveStatusText = string.Empty;
    private int _configurationChangeVersion;
    private int _untitledDocumentNumber;
    private int _workspaceAiConversationNumber;
    private bool _isRestoringEditorSession;
    private bool _editorSessionDirty;
    private string _lastEditorSessionError = string.Empty;
    private ApplicationUpdateCheckOutcome? _latestApplicationUpdateCheck;
    private bool _lastProfileSyncSignedIn;
    private bool _isProfileLoginGateVisible = true;
    private bool _isProfileLoginFlowActive;
    private string _profileLoginStatus =
        "Checking this PC for a saved Atlas session…";
    private string _pendingProfileLoginCode = string.Empty;
    private string _pendingProfileLoginUri = string.Empty;
    private ImageSource? _profileAvatarImageSource;
    private bool _isAccountSyncBootstrapPending;
    private bool _isDisposed;

    public MainWindowViewModel()
        : this(new SettingsService())
    {
    }

    public MainWindowViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _profileAvatarService = new ProfileAvatarService(
            _settingsService.ProfileAvatarPath);
        _profileAvatarImageSource = _profileAvatarService.TryLoad();
        _profileSyncDeviceStateService = new ProfileSyncDeviceStateService(
            _settingsService.ProfileSyncConfigurationPath,
            _settingsService.ProfileSyncStatePath,
            _settingsService.ProfileConflictDirectory);
        _profileSyncCoordinator = new ProfileSyncCoordinator(
            _portableProfileService,
            _profileSyncDeviceStateService);
        _profileSyncSessionService = new ProfileSyncSessionService(
            _profileSyncDeviceStateService,
            new DpapiProfileSyncCredentialStore(
                _settingsService.ProfileSyncCredentialPath));
        _sshCredentialStore = new DpapiSshCredentialStore(
            _settingsService.SshCredentialVaultPath);
        _sshWorkspaceConnections = new SshWorkspaceConnectionService(
            _sshCredentialStore,
            () => _profileSyncSessionService.Configuration);
        _gitCommandRunners = new GitCommandRunnerRegistry(
            new LocalGitCommandRunner());
        _gitRepositoryService = new GitRepositoryService(_gitCommandRunners);
        _workspaceFileSystems = new WorkspaceFileSystemProviderRegistry(
            new LocalWorkspaceFileSystemProvider());
        _workspaceDocumentService = new WorkspaceDocumentService(
            _workspaceFileSystems,
            _settingsService.RemoteDocumentCacheDirectory);
        _profileSyncSessionService.PropertyChanged +=
            OnProfileSyncSessionPropertyChanged;
        _applicationUpdateService = new ApplicationUpdateService(
            _settingsService.ApplicationUpdateDirectory);
        _workspaceAiAuditService = new WorkspaceAiAuditService(
            _settingsService.WorkspaceAiAuditPath);
        _workspaceAiContextService = new WorkspaceAiContextService(
            _settingsService.WorkspaceAiContextPath);
        _profileRoamingDeviceStateService =
            new ProfileRoamingDeviceStateService(
                _settingsService.ProfileRoamingStatePath,
                _settingsService.ProfileConflictDirectory);
        _workspaceProfileRoamingService =
            new WorkspaceProfileRoamingService(
                _workspaceAiContextService,
                _profileRoamingDeviceStateService);
        _localAiConversationStore = new LocalAiConversationStore(
            _settingsService.ConversationDirectory,
            _settingsService.ProfileConflictDirectory);
        _conversationRoamingService = new ConversationRoamingService(
            _localAiConversationStore,
            _profileRoamingDeviceStateService);
        _workspaceAiComputerControlService =
            new WorkspaceAiComputerControlService(
                _workspaceAiContextService);
        _workspaceAiUserActivityMonitor = new WindowsUserActivityMonitor();
        _workspaceAiPermissionBroker = new WorkspaceAiPermissionBroker(
            _settingsService.WorkspaceAiPermissionPath,
            _workspaceAiAuditService,
            _workspaceAiContextService);
        _workspaceAiOperationService = new WorkspaceAiOperationService(
            _workspaceAiPermissionBroker,
            _workspaceAiAuditService,
            _textDocumentService,
            workspaceProviders: _workspaceFileSystems);
        _workspaceAiGitService = new WorkspaceAiGitService(
            _gitRepositoryService,
            _workspaceAiPermissionBroker,
            _workspaceAiAuditService);
        _workspaceAiProviderConfigurationService =
            new WorkspaceAiProviderConfigurationService(
                _settingsService.WorkspaceAiProviderConfigurationPath);
        _workspaceAiCredentialStore =
            new DpapiWorkspaceAiCredentialStore(
                _settingsService.WorkspaceAiProviderCredentialPath);
        _workspaceAiLocalRuntime = new WorkspaceAiLocalRuntimeService();
        _workspaceAiProvider = new ResponsesWorkspaceAiProvider(
            localRuntime: _workspaceAiLocalRuntime);
        _editorSessionService = new EditorSessionService(
            _settingsService.EditorSessionPath,
            _settingsService.EditorRecoveryDirectory);
        _workspacePanelRegistry = WorkspacePanelRegistry.CreateWithBuiltIns();
        _settings = _settingsService.Load();
        _extensionDiscoveryService = new ExtensionDiscoveryService(
            _settingsService.ExtensionDirectory);
        _hyperlibPackageService = new HyperlibPackageService(
            _settingsService.ExtensionDirectory,
            _settingsService.ExtensionStagingDirectory,
            _settingsService.ExtensionRecoveryDirectory);
        _hyperlibPackageRoamingService =
            new HyperlibPackageRoamingService(
                _hyperlibPackageService,
                _settingsService.ProfilePackageCacheDirectory);
        _extensionCatalog = _extensionDiscoveryService.Discover();
        _documentProviderRegistry = new DocumentProviderRegistry(_extensionCatalog);
        _documentProviderCatalog = _documentProviderRegistry.Catalog;
        _catalogService = new CommandCatalogService(
            _settingsService,
            _extensionCatalog);
        _workspaceAiEmbeddings = new WorkspaceAiEmbeddingService();
        _hyperlibSemanticSearch = new HyperlibSemanticSearchService(
            _catalogService,
            _workspaceAiEmbeddings);
        _extensionApprovalService = new ExtensionApprovalService(
            _settingsService.ExtensionStatePath);
        _languageToolDiscoveryService = new LanguageToolDiscoveryService();
        _languageToolCatalog = _languageToolDiscoveryService.Discover();
        _externalDocumentFormatterService = new ExternalDocumentFormatterService();
        _compiledDocumentExecutionService =
            new CompiledDocumentExecutionService();
        SynchronizeApprovedPanelRegistry();
        _runtimeApplicationPreferences = _settings.Application.Clone();
        _runtimeExplorerSettings = _settings.Explorer.Clone();
        _runtimeWorkspaceLayout = WorkspaceLayoutService.Normalize(
            _settings.WorkspaceLayout,
            _workspacePanelRegistry);
        _runtimeTerminalConfigurationProfiles = _settings.TerminalConfigurationProfiles
            .Select(configuration => configuration.Clone())
            .ToList();
        _isFileExplorerOpen = _runtimeApplicationPreferences.OpenFileExplorerOnStartup;
        _isCommandBuilderOpen = _runtimeApplicationPreferences.OpenCommandBuilderOnStartup;
        TerminalProfiles = _settings.TerminalProfiles.ToList();
        AvailableTerminalProfiles = _settings.TerminalProfiles
            .Where(profile => profile.IsAvailable)
            .ToList();
        _selectedTerminalProfile = AvailableTerminalProfiles.First(profile =>
            string.Equals(profile.Id, _settings.ActiveTerminalProfileId, StringComparison.OrdinalIgnoreCase));
        RefreshObservedTerminalSettings();

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autosaveTimer.Tick += OnAutosaveTimerTick;
        _autosaveCueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autosaveCueTimer.Tick += OnAutosaveCueTimerTick;
        _statusCueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusCueTimer.Tick += OnStatusCueTimerTick;
        _editorRecoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _editorRecoveryTimer.Tick += OnEditorRecoveryTimerTick;
        _cloudSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        _cloudSyncTimer.Tick += OnCloudSyncTimerTick;

        CommandBuilder = new CommandBuilderViewModel(_settings, _settingsService);
        CommandBuilder.CommandDispatchRequested += async (_, request) => await DispatchBuilderCommandAsync(request);
        CommandBuilder.SettingsChanged += (_, _) =>
        {
            TouchPortableProfile();
            ScheduleAutosave();
        };
        CommandBuilder.SettingsSaveRequested += (_, _) =>
            SaveSettingsImmediately(
                "Command profile saved.",
                portableChange: true);

        FileExplorer = new FileSystemExplorerViewModel(
            _runtimeExplorerSettings,
            _workspaceFileSystems);
        FileExplorer.PathsRequested += (_, paths) =>
        {
            CommandBuilder.InsertPaths(paths);
            IsCommandBuilderOpen = true;
            SetCommandBuilderPage(CommandBuilderPanePage.Builder);
        };
        FileExplorer.PropertyChanged += OnFileExplorerPropertyChanged;
        WorkItems = new ProjectWorkItemsViewModel(
            new ProjectWorkItemService());
        WorkItems.UpdateProjectScopes(
            _runtimeExplorerSettings.Projects,
            _runtimeExplorerSettings.ActiveProjectId,
            FileExplorer.CurrentPath);
        SourceControl = new SourceControlViewModel(_gitRepositoryService);
        SourceControl.DiffRequested += OnSourceControlDiffRequested;
        SourceControl.ConflictRequested += OnSourceControlConflictRequested;
        UpdateSourceControlWorkspaceLocation();
        var atlasMyNetStore = new AtlasMyNetAppStore(
            _settingsService.AtlasMyNetAppsPath,
            _settingsService.AtlasMyNetTrustPath);
        var atlasMyNetInventory = new WindowsSocketInventoryService();
        var atlasMyNetWeb = new AtlasMyNetWebService();
        var atlasMyNetProcesses = new AtlasMyNetProcessService(
            atlasMyNetStore,
            atlasMyNetInventory,
            atlasMyNetWeb);
        AtlasMyNet = new AtlasMyNetViewModel(
            atlasMyNetStore,
            atlasMyNetInventory,
            atlasMyNetWeb,
            atlasMyNetProcesses);
        AtlasMyNet.OpenAppRequested += OnAtlasMyNetOpenAppRequested;
        AtlasMyNet.NetworkClientRequested += OnAtlasMyNetNetworkClientRequested;
        NetworkObservatory = new NetworkObservatoryViewModel(
            new WindowsNetworkObservationService(),
            new LocalNetworkDiscoveryService(),
            new NetworkExposureAnalysisService(),
            new NetworkCensusService(),
            atlasMyNetInventory,
            new NetworkDeviceCorrelationService(),
            new NetworkObservatoryStateStore(
                _settingsService.NetworkObservatoryStatePath));

        _workspaceAiToolHost = new WorkspaceAiToolHost(
            _workspaceAiPermissionBroker,
            _workspaceAiOperationService,
            GetWorkspaceAiActiveDocument,
            GetWorkspaceAiDocumentBySessionId,
            GetWorkspaceAiActiveTerminal,
            DispatchWorkspaceAiTerminalCommandAsync,
            _workspaceAiGitService,
            _hyperlibSemanticSearch);
        foreach (var document in _localAiConversationStore.Load(
                     _settings.PortableProfileId,
                     openOnly: true))
        {
            WorkspaceAiConversations.Add(
                CreateWorkspaceAiConversationCore(document));
        }

        if (WorkspaceAiConversations.Count == 0)
        {
            WorkspaceAiConversations.Add(
                CreateWorkspaceAiConversationCore());
        }

        _selectedWorkspaceAiConversation = WorkspaceAiConversations[0];
        AttachWorkspaceAiAssistant(
            _selectedWorkspaceAiConversation.Assistant);
        _workspaceAiUserActivityMonitor.ActivityPaused +=
            OnWindowsUserActivityPaused;

        ToggleFileExplorerCommand = new RelayCommand(() => IsFileExplorerOpen = !IsFileExplorerOpen);
        ToggleCommandBuilderCommand = new RelayCommand(() => IsCommandBuilderOpen = !IsCommandBuilderOpen);
        ShowCommandBuilderToolCommand = new RelayCommand(
            () => SetCommandBuilderPage(CommandBuilderPanePage.Builder));
        ShowWorkspaceAiCommand = new RelayCommand(
            () => SetCommandBuilderPage(CommandBuilderPanePage.Ai));
        RefreshWorkspaceSideTools();

        TerminalFontFamilies = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Append("JetBrains Mono")
            .Append("VT323")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_runtimeApplicationPreferences.CreateTerminalOnStartup)
        {
            CreateTerminal();
        }
    }

    public TerminalSettings TerminalSettings => SelectedTerminalProfile.Settings;
    public IReadOnlyList<TerminalProfile> TerminalProfiles { get; private set; }
    public IReadOnlyList<TerminalProfile> AvailableTerminalProfiles { get; private set; }
    public FileSystemExplorerViewModel FileExplorer { get; }
    public ProjectWorkItemsViewModel WorkItems { get; }
    public SourceControlViewModel SourceControl { get; }
    public AtlasMyNetViewModel AtlasMyNet { get; }
    public NetworkObservatoryViewModel NetworkObservatory { get; }
    public CommandBuilderViewModel CommandBuilder { get; }
    public ObservableCollection<WorkspaceSideToolViewModel>
        FileExplorerSideTools
    { get; } = [];
    public ObservableCollection<WorkspaceSideToolViewModel>
        CommandBuilderSideTools
    { get; } = [];
    public ObservableCollection<WorkspaceAiConversationTabViewModel>
        WorkspaceAiConversations
    { get; } = [];

    public WorkspaceAiConversationTabViewModel SelectedWorkspaceAiConversation
    {
        get => _selectedWorkspaceAiConversation
               ?? throw new InvalidOperationException(
                   "An AI conversation is not available.");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!WorkspaceAiConversations.Contains(value)
                || ReferenceEquals(_selectedWorkspaceAiConversation, value))
            {
                return;
            }

            var previous = _selectedWorkspaceAiConversation;
            if (previous is not null)
            {
                if (previous.Assistant.IsDesktopLiveSessionEnabled
                    || previous.Assistant.IsComputerControlActive)
                {
                    previous.Assistant.StopComputerControlCommand.Execute(null);
                }

                previous.Assistant.CancelLiveActivity();
                DetachWorkspaceAiAssistant(previous.Assistant);
            }

            if (!SetProperty(ref _selectedWorkspaceAiConversation, value))
            {
                return;
            }

            AttachWorkspaceAiAssistant(value.Assistant);
            value.Assistant.RefreshState();
            foreach (var tab in WorkspaceTabs)
            {
                tab.IsAiLiveAssistEnabled =
                    value.Assistant.IsLiveSourceEnabled(tab.AiLiveSourceId);
            }

            OnPropertyChanged(nameof(WorkspaceAiAssistant));
        }
    }

    public WorkspaceAiAssistantViewModel WorkspaceAiAssistant =>
        SelectedWorkspaceAiConversation.Assistant;
    public ObservableCollection<IWorkspaceTab> WorkspaceTabs { get; } = [];
    public IEnumerable<TerminalTabViewModel> Terminals =>
        WorkspaceTabs.OfType<TerminalTabViewModel>();
    public IReadOnlyList<string> TerminalFontFamilies { get; }
    public bool ShowSaveStatusInTitleBar => _runtimeApplicationPreferences.ShowSaveStatusInTitleBar;
    public bool IsProfileSyncSignedIn =>
        _profileSyncSessionService.IsSignedIn;
    public bool IsProfileSyncOffline =>
        _profileSyncSessionService.IsOffline;
    public bool IsProfileSyncBusy =>
        _profileSyncSessionService.IsBusy;
    public bool CanProfileSyncSignIn =>
        !IsProfileSyncSignedIn && !IsProfileSyncBusy;
    public bool CanProfileSyncSignOut =>
        IsProfileSyncSignedIn && !IsProfileSyncBusy;
    public bool IsProfileLoginGateVisible
    {
        get => _isProfileLoginGateVisible;
        private set
        {
            if (SetProperty(ref _isProfileLoginGateVisible, value))
            {
                NotifyProfileLoginStateChanged();
            }
        }
    }
    public bool IsProfileLoginFlowActive
    {
        get => _isProfileLoginFlowActive;
        private set
        {
            if (SetProperty(ref _isProfileLoginFlowActive, value))
            {
                NotifyProfileLoginStateChanged();
            }
        }
    }
    public string ProfileLoginStatus
    {
        get => _profileLoginStatus;
        private set => SetProperty(ref _profileLoginStatus, value);
    }
    public string PendingProfileLoginCode
    {
        get => _pendingProfileLoginCode;
        private set
        {
            if (SetProperty(ref _pendingProfileLoginCode, value))
            {
                OnPropertyChanged(
                    nameof(HasPendingProfileLoginAuthorization));
            }
        }
    }
    public string PendingProfileLoginUri
    {
        get => _pendingProfileLoginUri;
        private set => SetProperty(ref _pendingProfileLoginUri, value);
    }
    public bool HasPendingProfileLoginAuthorization =>
        !string.IsNullOrWhiteSpace(PendingProfileLoginCode);
    public bool CanBeginProfileLogin =>
        IsProfileLoginGateVisible
        && !IsProfileLoginFlowActive
        && !IsProfileSyncBusy
        && !IsProfileSyncSignedIn;
    public bool CanCancelProfileLogin =>
        IsProfileLoginGateVisible && IsProfileLoginFlowActive;
    public bool CanContinueWithoutProfileLogin =>
        IsProfileLoginGateVisible
        && !IsProfileLoginFlowActive
        && !IsProfileSyncBusy;
    public string ProfileSyncAccountLabel
    {
        get
        {
            var accountId = _profileSyncSessionService.Configuration.AccountId;
            if (IsProfileSyncSignedIn)
            {
                return string.IsNullOrWhiteSpace(accountId)
                    ? "Signed in"
                    : $"Signed in as {accountId}";
            }

            if (IsProfileSyncOffline
                && !string.IsNullOrWhiteSpace(accountId))
            {
                return $"{accountId} · offline";
            }

            return "Not signed in";
        }
    }
    public string ProfileAvatarInitials => ProfileAvatarService.CreateInitials(
        _profileSyncSessionService.Configuration.AccountId,
        Environment.UserName);
    public ImageSource? ProfileAvatarImageSource =>
        _profileAvatarImageSource;
    public bool HasProfileAvatarImage =>
        ProfileAvatarImageSource is not null;
    public string ProfileSyncTitleToolTip =>
        _profileSyncSessionService.StatusText;
    public ApplicationUpdateCheckOutcome? LatestApplicationUpdateCheck =>
        _latestApplicationUpdateCheck;

    internal AppSettings SettingsModel => _settings;
    internal WorkspacePanelRegistry WorkspacePanelRegistry =>
        _workspacePanelRegistry;
    internal WorkspaceLayoutSnapshot WorkspaceLayout =>
        _runtimeWorkspaceLayout;
    internal ExtensionDiscoverySnapshot ExtensionCatalog =>
        _extensionCatalog;
    internal LanguageToolCatalogSnapshot LanguageToolCatalog =>
        _languageToolCatalog;
    internal DocumentProviderCatalogSnapshot DocumentProviderCatalog =>
        _documentProviderCatalog;
    internal WorkspaceAiPermissionBroker WorkspaceAiPermissionBroker =>
        _workspaceAiPermissionBroker;
    internal WorkspaceAiAuditService WorkspaceAiAuditService =>
        _workspaceAiAuditService;
    internal WorkspaceAiContextService WorkspaceAiContextService =>
        _workspaceAiContextService;

    internal void AttachWorkspaceAiComputerControlTarget(
        IWorkspaceAiComputerControlTarget target) =>
        _workspaceAiComputerControlService.Attach(target);

    internal void DetachWorkspaceAiComputerControlTarget(
        IWorkspaceAiComputerControlTarget target) =>
        _workspaceAiComputerControlService.Detach(target);
    internal WorkspaceAiOperationService WorkspaceAiOperations =>
        _workspaceAiOperationService;
    internal WorkspaceAiProviderConfigurationService
        WorkspaceAiProviderConfigurationService =>
        _workspaceAiProviderConfigurationService;
    internal IWorkspaceAiCredentialStore WorkspaceAiCredentialStore =>
        _workspaceAiCredentialStore;
    internal WorkspaceAiLocalRuntimeService WorkspaceAiLocalRuntime =>
        _workspaceAiLocalRuntime;
    internal string ExtensionWebViewDataDirectory =>
        _settingsService.ExtensionWebViewDataDirectory;
    internal string ExtensionPanelStateDirectory =>
        _settingsService.ExtensionPanelStateDirectory;
    internal ExtensionPanelWorkspaceContext ExtensionPanelWorkspaceContext
    {
        get
        {
            var activeProject = _runtimeExplorerSettings.Projects
                .FirstOrDefault(project => string.Equals(
                    project.Id,
                    _runtimeExplorerSettings.ActiveProjectId,
                    StringComparison.OrdinalIgnoreCase));
            ExtensionPanelActiveTabContext? activeTab = ActiveWorkspaceTab switch
            {
                DocumentTabViewModel document => new ExtensionPanelActiveTabContext(
                    "document",
                    document.Title,
                    document.HasUnsavedChanges,
                    document.LanguageId,
                    string.Empty),
                TerminalTabViewModel terminal => new ExtensionPanelActiveTabContext(
                    "terminal",
                    terminal.Title,
                    false,
                    string.Empty,
                    terminal.ShellKind.ToString()),
                _ => null
            };
            return new ExtensionPanelWorkspaceContext(
                activeProject is null
                    ? null
                    : new ExtensionPanelActiveProjectContext(
                        activeProject.Id,
                        activeProject.Name),
                WorkspaceTabs.Count,
                activeTab);
        }
    }

    internal IReadOnlyList<WorkspaceAiWorkspaceDescriptor>
        GetWorkspaceAiWorkspaceDescriptors()
    {
        var descriptors = new List<WorkspaceAiWorkspaceDescriptor>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaceUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in _settings.Explorer.Projects)
        {
            foreach (var configuredRoot in project.Directories)
            {
                if (string.IsNullOrWhiteSpace(configuredRoot))
                {
                    continue;
                }
                var configuredValue = configuredRoot;

                if (WorkspaceLocation.TryParse(
                        configuredRoot,
                        out var workspaceLocation))
                {
                    if (workspaceLocation.IsSsh)
                    {
                        if (!workspaceUris.Add(workspaceLocation.Identity))
                        {
                            continue;
                        }

                        var leaf = workspaceLocation.Path
                            .TrimEnd('/')
                            .Split('/')
                            .LastOrDefault();
                        descriptors.Add(new WorkspaceAiWorkspaceDescriptor(
                            WorkspaceAiPermissionBroker.CreateWorkspaceUriId(
                                $"project-{project.Id}",
                                workspaceLocation.Identity),
                            string.IsNullOrWhiteSpace(leaf)
                                ? project.Name
                                : $"{project.Name} — {leaf}",
                            string.Empty,
                            _workspaceFileSystems.IsRegistered(
                                workspaceLocation.ProviderId,
                                workspaceLocation.Authority),
                            workspaceLocation.Identity));
                        continue;
                    }

                    configuredValue = workspaceLocation.Path;
                }

                string rootPath;
                try
                {
                    rootPath = Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(configuredValue));
                }
                catch (Exception exception) when (exception is ArgumentException
                                                 or IOException
                                                 or NotSupportedException)
                {
                    continue;
                }

                if (!Directory.Exists(rootPath) || !roots.Add(rootPath))
                {
                    continue;
                }

                var directoryName = Path.GetFileName(rootPath);
                descriptors.Add(new WorkspaceAiWorkspaceDescriptor(
                    WorkspaceAiPermissionBroker.CreateWorkspaceId(
                        $"project-{project.Id}",
                        rootPath),
                    string.IsNullOrWhiteSpace(directoryName)
                        ? project.Name
                        : $"{project.Name} — {directoryName}",
                    rootPath));
            }
        }

        foreach (var device in FileExplorer.Devices.Where(device =>
                     !device.IsLocal))
        {
            var location = device.DefaultLocation;
            if (!workspaceUris.Add(location.Identity))
            {
                continue;
            }

            descriptors.Add(new WorkspaceAiWorkspaceDescriptor(
                WorkspaceAiPermissionBroker.CreateWorkspaceUriId(
                    $"device-{device.Id}",
                    location.Identity),
                $"{device.DisplayName} — default root",
                string.Empty,
                _workspaceFileSystems.IsRegistered(
                    location.ProviderId,
                    location.Authority),
                location.Identity));
        }

        if (WorkspaceLocation.TryParse(
                FileExplorer.CurrentWorkspaceUri,
                out var currentWorkspace)
            && currentWorkspace.IsSsh
            && workspaceUris.Add(currentWorkspace.Identity))
        {
            var leaf = currentWorkspace.Path
                .TrimEnd('/')
                .Split('/')
                .LastOrDefault();
            descriptors.Add(new WorkspaceAiWorkspaceDescriptor(
                WorkspaceAiPermissionBroker.CreateWorkspaceUriId(
                    "folder",
                    currentWorkspace.Identity),
                string.IsNullOrWhiteSpace(leaf)
                    ? "Current remote folder"
                    : $"Current remote folder — {leaf}",
                string.Empty,
                _workspaceFileSystems.IsRegistered(
                    currentWorkspace.ProviderId,
                    currentWorkspace.Authority),
                currentWorkspace.Identity));
        }

        if (Directory.Exists(FileExplorer.CurrentPath))
        {
            var currentRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(FileExplorer.CurrentPath));
            if (roots.Add(currentRoot))
            {
                var directoryName = Path.GetFileName(currentRoot);
                descriptors.Add(new WorkspaceAiWorkspaceDescriptor(
                    WorkspaceAiPermissionBroker.CreateWorkspaceId(
                        "folder",
                        currentRoot),
                    string.IsNullOrWhiteSpace(directoryName)
                        ? "Current Explorer folder"
                        : $"Current folder — {directoryName}",
                    currentRoot));
            }
        }

        return descriptors
            .OrderBy(
                descriptor => descriptor.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private WorkspaceAiActiveDocumentHandle? GetWorkspaceAiActiveDocument(
        string workspaceId,
        string workingDirectory) =>
        CreateWorkspaceAiDocumentHandle(
            workspaceId,
            workingDirectory,
            ActiveDocument);

    private WorkspaceAiActiveDocumentHandle?
        GetWorkspaceAiDocumentBySessionId(
            string workspaceId,
            string workingDirectory,
            string sessionId) =>
        CreateWorkspaceAiDocumentHandle(
            workspaceId,
            workingDirectory,
            WorkspaceTabs
                .OfType<DocumentTabViewModel>()
                .FirstOrDefault(document => string.Equals(
                    document.SessionId,
                    sessionId,
                    StringComparison.Ordinal)));

    private WorkspaceAiActiveTerminalHandle? GetWorkspaceAiActiveTerminal(
        string workspaceId,
        string workingDirectory)
    {
        var candidates = ActiveTerminal is null
            ? Terminals.Reverse()
            : new[] { ActiveTerminal }
                .Concat(Terminals.Reverse().Where(terminal =>
                    !ReferenceEquals(terminal, ActiveTerminal)));
        var terminal = candidates.FirstOrDefault(candidate =>
        {
            var target = candidate.IsRemote
                ? WorkspaceLocation.Ssh(
                    candidate.RemoteHostId,
                    candidate.WorkingDirectory).Identity
                : candidate.WorkingDirectory;
            return IsTerminalAtWorkingDirectory(
                       candidate,
                       workingDirectory)
                   && _workspaceAiPermissionBroker.AuthorizeTerminalRead(
                       workspaceId,
                       target).IsAllowed;
        });
        if (terminal is null)
        {
            return null;
        }

        return new WorkspaceAiActiveTerminalHandle(
            terminal.SessionId,
            terminal.Title,
            terminal.IsRemote
                ? terminal.RemoteShellKind.ToString()
                : terminal.ShellKind.ToString(),
            terminal.WorkingDirectory,
            terminal.IsRemote
                ? WorkspaceLocation.Ssh(
                    terminal.RemoteHostId,
                    terminal.WorkingDirectory).Identity
                : WorkspaceLocation.Local(
                    terminal.WorkingDirectory).Identity,
            terminal.RemoteHostId,
            terminal.Prompt,
            terminal.IsRunning,
            () => terminal.InputText,
            () => terminal.Lines
                .Select(line => new WorkspaceAiTerminalLineSnapshot(
                    line.Kind.ToString(),
                    line.Text))
                .ToList());
    }

    private static bool IsTerminalAtWorkingDirectory(
        TerminalTabViewModel terminal,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return true;
        }

        try
        {
            var normalized = WorkspaceAiWorkingDirectory.Normalize(
                workingDirectory,
                requireAvailable: false);
            var requested = WorkspaceLocation.TryParse(
                normalized,
                out var remote)
                ? remote
                : WorkspaceLocation.Local(normalized);
            var actual = terminal.IsRemote
                ? WorkspaceLocation.Ssh(
                    terminal.RemoteHostId,
                    terminal.WorkingDirectory)
                : WorkspaceLocation.Local(terminal.WorkingDirectory);
            return WorkspaceLocation.SameIdentity(requested, actual);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsTerminalInsideRoot(
        TerminalTabViewModel terminal,
        string rootPath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(rootPath));
            var workingDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(terminal.WorkingDirectory));
            if (string.Equals(
                    root,
                    workingDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = Path.EndsInDirectorySeparator(root)
                ? root
                : root + Path.DirectorySeparatorChar;
            return workingDirectory.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException)
        {
            return false;
        }
    }

    private WorkspaceAiActiveDocumentHandle?
        CreateWorkspaceAiDocumentHandle(
            string workspaceId,
            string workingDirectory,
            DocumentTabViewModel? document)
    {
        if (document is null)
        {
            return null;
        }

        string authorizationTarget;
        try
        {
            if (document.IsRemoteDocument)
            {
                authorizationTarget = document.WorkspaceUri;
            }
            else if (document.HasFilePath
                && !string.IsNullOrWhiteSpace(document.FilePath))
            {
                authorizationTarget = Path.GetFullPath(document.FilePath);
            }
            else
            {
                var fileName = Path.GetFileName(document.Title);
                if (string.IsNullOrWhiteSpace(workingDirectory)
                    || string.IsNullOrWhiteSpace(fileName)
                    || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return null;
                }

                if (WorkspaceLocation.TryParse(
                        workingDirectory,
                        out var remoteRoot)
                    && remoteRoot.IsSsh)
                {
                    authorizationTarget = WorkspaceLocation.Ssh(
                        remoteRoot.Authority,
                        remoteRoot.Path.TrimEnd('/') + "/" + fileName)
                        .Identity;
                }
                else
                {
                    authorizationTarget = Path.GetFullPath(Path.Combine(
                        workingDirectory,
                        fileName));
                }
            }

            authorizationTarget = WorkspaceAiWorkingDirectory
                .ResolveProjectPath(
                    workingDirectory,
                    authorizationTarget);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or NotSupportedException)
        {
            return null;
        }

        var authorization = _workspaceAiPermissionBroker.AuthorizePath(
            workspaceId,
            authorizationTarget,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ReadOpenDocument);
        if (!authorization.IsAllowed)
        {
            return null;
        }

        return new WorkspaceAiActiveDocumentHandle(
            document.SessionId,
            document.Title,
            authorization.RelativePath,
            document.LanguageId,
            document.DocumentProviderId,
            document.HasUnsavedChanges,
            () => document.Content,
            document.ReadThroughProvider,
            replacement => document.ReplaceThroughProvider(
                0,
                document.Content.Length,
                replacement));
    }

    internal Task<WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>>
        DispatchWorkspaceAiTerminalCommandAsync(
            string workspaceId,
            string terminalSessionId,
            string terminalWorkspaceUri,
            string command,
            bool execute,
            bool userConfirmed,
            CancellationToken cancellationToken = default) =>
        _workspaceAiOperationService.DispatchTerminalAsync(
            workspaceId,
            terminalWorkspaceUri,
            command,
            execute,
            userConfirmed,
            async (
                authorizedCommand,
                workspaceRoot,
                executeImmediately,
                dispatchCancellation) =>
            {
                dispatchCancellation.ThrowIfCancellationRequested();
                var terminal = Terminals.FirstOrDefault(candidate =>
                    !candidate.IsRunning
                    && string.Equals(
                        candidate.SessionId,
                        terminalSessionId,
                        StringComparison.Ordinal));
                if (terminal is not null)
                {
                    var currentIdentity = terminal.IsRemote
                        ? WorkspaceLocation.Ssh(
                            terminal.RemoteHostId,
                            terminal.WorkingDirectory).Identity
                        : WorkspaceLocation.Local(
                            terminal.WorkingDirectory).Identity;
                    if (!string.Equals(
                            currentIdentity,
                            terminalWorkspaceUri,
                            terminal.IsRemote
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase))
                    {
                        terminal = null;
                    }
                }

                WorkspaceLocation? requestedLocation = null;
                if (WorkspaceLocation.TryParse(
                        terminalWorkspaceUri,
                        out var parsedLocation))
                {
                    requestedLocation = parsedLocation;
                }

                if (terminal is null && requestedLocation?.IsLocal == true)
                {
                    terminal = CreateTerminal(
                        CreateDefaultLaunchRequest(SelectedTerminalProfile),
                        runStartupCommand: false,
                        workingDirectoryOverride: requestedLocation.Path);
                }

                if (terminal is null)
                {
                    throw new InvalidOperationException(
                        requestedLocation?.IsSsh == true
                            ? "The reviewed remote terminal tab is no longer open at the approved location."
                            : "No compatible terminal is available for the approved workspace.");
                }

                if (terminal.IsRunning)
                {
                    throw new InvalidOperationException(
                        "The workspace terminal is already running a command.");
                }

                ActiveTerminal = terminal;
                if (executeImmediately)
                {
                    await terminal.ExecuteTextAsync(authorizedCommand);
                    ShowTransientStatus(
                        "The authorized AI terminal command completed host dispatch.");
                }
                else
                {
                    terminal.InputText = authorizedCommand;
                    ShowTransientStatus(
                        "The AI terminal command was staged and was not executed.");
                }
            },
            cancellationToken);

    internal async Task<ExtensionDiscoverySnapshot> RefreshExtensionCatalogAsync()
    {
        _extensionCatalog = _extensionDiscoveryService.Discover();
        _documentProviderCatalog = _documentProviderRegistry.Rebuild(
            _extensionCatalog);
        foreach (var document in WorkspaceTabs.OfType<DocumentTabViewModel>())
        {
            BindDocumentProvider(document);
        }
        _catalogService.SetExtensionCatalog(_extensionCatalog);
        _hyperlibSemanticSearch.InvalidateIndex();
        SynchronizeApprovedPanelRegistry();
        _runtimeWorkspaceLayout = WorkspaceLayoutService.Normalize(
            _runtimeWorkspaceLayout,
            _workspacePanelRegistry);
        OnPropertyChanged(nameof(ExtensionCatalog));
        OnPropertyChanged(nameof(DocumentProviderCatalog));
        OnPropertyChanged(nameof(WorkspaceLayout));
        await CommandBuilder.LoadLibrariesAsync(_catalogService);
        return _extensionCatalog;
    }

    internal Task<HyperlibPackageInstallPreview> InspectHyperlibPackageAsync(
        string sourcePath) =>
        Task.Run(() =>
            _hyperlibPackageService.InspectInstallSource(sourcePath));

    internal async Task<HyperlibPackageInstallResult> InstallHyperlibPackageAsync(
        string sourcePath,
        bool allowUpdate)
    {
        var result = await Task.Run(() =>
            _hyperlibPackageService.Install(sourcePath, allowUpdate));
        await RefreshExtensionCatalogAsync();
        ScheduleCloudSync();
        return result;
    }

    internal Task<HyperlibPackageArchiveResult> PackHyperlibPackageAsync(
        string sourceDirectory,
        string destinationPath) =>
        Task.Run(() =>
            _hyperlibPackageService.Pack(
                sourceDirectory,
                destinationPath));

    internal async Task<HyperlibPackageRemovalResult> RemoveHyperlibPackageAsync(
        DiscoveredExtensionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Manifest?.Runtime != ExtensionRuntimeKind.ContentOnly
            || package.Manifest.Contributions.Libraries.Count == 0)
        {
            throw new InvalidOperationException(
                "Only ContentOnly packages with Hyperlib contributions are managed by this package lifecycle.");
        }

        DeactivateExtensionPanels(package);
        var result = await Task.Run(() =>
            _hyperlibPackageService.Remove(package));
        await RefreshExtensionCatalogAsync();
        ScheduleCloudSync();
        return result;
    }

    internal async Task<LanguageToolCatalogSnapshot> RefreshLanguageToolCatalogAsync()
    {
        var snapshot = await Task.Run(() => _languageToolDiscoveryService.Discover());
        _languageToolCatalog = snapshot;
        RefreshTerminalProfiles();
        OnPropertyChanged(nameof(LanguageToolCatalog));
        return snapshot;
    }

    internal PortableProfileEnvelope ExportPortableProfile(
        string destinationPath,
        string displayName)
    {
        var snapshot = _portableProfileService.CreateSnapshot(
            _settings,
            _extensionCatalog,
            displayName);
        return _portableProfileService.Export(destinationPath, snapshot);
    }

    internal ProfileImportPreview InspectPortableProfile(string sourcePath) =>
        _portableProfileService.Inspect(
            sourcePath,
            _extensionCatalog,
            TerminalProfiles);

    internal ProfileSyncDeviceConfiguration LoadProfileSyncConfiguration() =>
        _profileSyncSessionService.Configuration;

    internal string ProfileSyncLoadWarning =>
        _profileSyncSessionService.StatusText;

    internal ProfileSyncSessionService ProfileSyncSession =>
        _profileSyncSessionService;

    internal ApplicationUpdateService ApplicationUpdates =>
        _applicationUpdateService;

    internal string ProfileConflictDirectory =>
        _profileSyncDeviceStateService.ConflictDirectory;

    internal void SaveProfileSyncConfiguration(
        ProfileSyncDeviceConfiguration configuration)
    {
        _profileSyncSessionService.SaveConfiguration(configuration);
        OnPropertyChanged(nameof(ProfileAvatarInitials));
    }

    internal void SaveProfileAvatar(string sourcePath)
    {
        _profileAvatarService.Save(sourcePath);
        ReloadProfileAvatar();
    }

    internal void RemoveProfileAvatar()
    {
        _profileAvatarService.Remove();
        ReloadProfileAvatar();
    }

    internal async Task<ProfileSessionResponse>
        AuthorizeProfileSyncDeviceAsync(
            string endpoint,
            Action<DeviceAuthorizationResponse> authorizationRequired,
            CancellationToken cancellationToken = default)
    {
        var platform =
            $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}";
        return await _profileSyncSessionService.AuthorizeDeviceAsync(
            endpoint,
            Environment.MachineName,
            platform,
            authorizationRequired,
            cancellationToken);
    }

    public void ShowProfileLoginGate(string? status = null)
    {
        if (IsProfileSyncSignedIn)
        {
            return;
        }

        PendingProfileLoginCode = string.Empty;
        PendingProfileLoginUri = string.Empty;
        ProfileLoginStatus = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim();
        IsProfileLoginGateVisible = true;
        NotifyProfileLoginStateChanged();
    }

    public void ContinueWithoutProfileLogin()
    {
        if (!CanContinueWithoutProfileLogin)
        {
            return;
        }

        PendingProfileLoginCode = string.Empty;
        PendingProfileLoginUri = string.Empty;
        ProfileLoginStatus =
            "Continuing with device-local settings for this session.";
        IsProfileLoginGateVisible = false;
    }

    public bool BeginProfileLogin()
    {
        if (!CanBeginProfileLogin)
        {
            return false;
        }

        PendingProfileLoginCode = string.Empty;
        PendingProfileLoginUri = string.Empty;
        ProfileLoginStatus =
            "Opening secure Atlas sign-in in your browser…";
        IsProfileLoginFlowActive = true;
        return true;
    }

    public void ShowProfileLoginChallenge(
        DeviceAuthorizationResponse challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        PendingProfileLoginCode = challenge.UserCode;
        PendingProfileLoginUri = HttpCloudDeviceAuthorizationClient
            .CreateCompleteVerificationUri(challenge)
            .AbsoluteUri;
        ProfileLoginStatus =
            "Finish signing in with your password and authenticator. Hypershell will continue automatically.";
    }

    public void ReportProfileLoginCancellation()
    {
        if (IsProfileLoginFlowActive)
        {
            ProfileLoginStatus = "Cancelling secure sign-in…";
        }
    }

    public void ReportProfileLoginPortalFailure(string message)
    {
        if (IsProfileLoginFlowActive && !string.IsNullOrWhiteSpace(message))
        {
            ProfileLoginStatus = message.Trim();
        }
    }

    public void CompleteProfileLogin()
    {
        PendingProfileLoginCode = string.Empty;
        PendingProfileLoginUri = string.Empty;
        IsProfileLoginFlowActive = false;
        ProfileLoginStatus = IsProfileSyncSignedIn
            ? $"Welcome back. {ProfileSyncAccountLabel}."
            : "The saved Atlas session is ready.";
        IsProfileLoginGateVisible = false;
    }

    public void FailProfileLogin(string message)
    {
        PendingProfileLoginCode = string.Empty;
        PendingProfileLoginUri = string.Empty;
        IsProfileLoginFlowActive = false;
        ProfileLoginStatus = string.IsNullOrWhiteSpace(message)
            ? "Hypershell could not complete sign-in. Try again."
            : message.Trim();
        IsProfileLoginGateVisible = true;
        NotifyProfileLoginStateChanged();
    }

    internal async Task<ProfileSessionResponse> SignInProfileSyncAsync(
        string endpoint,
        string accountId,
        string accessKey,
        bool rememberAccessKey,
        CancellationToken cancellationToken = default)
    {
        return await _profileSyncSessionService.SignInAsync(
            new ProfileSyncDeviceConfiguration
            {
                ServerEndpoint = endpoint,
                AccountId = accountId
            },
            accessKey,
            rememberAccessKey,
            cancellationToken);
    }

    internal async Task SignOutProfileSyncAsync(
        bool forgetCredential,
        CancellationToken cancellationToken = default)
    {
        await _profileSyncSessionService.SignOutAsync(
            forgetCredential,
            cancellationToken);
    }

    internal async Task<LinkedHostInventorySnapshot>
        LoadLinkedHostInventoryAsync(
            CancellationToken cancellationToken = default)
    {
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        var hostsTask = client.ListHostsAsync(cancellationToken);
        var credentialsTask = client.ListCredentialsAsync(cancellationToken);
        await Task.WhenAll(hostsTask, credentialsTask);
        var localIds = _sshCredentialStore.ListCredentialIds(
            configuration.ServerEndpoint,
            configuration.AccountId);
        var snapshot = new LinkedHostInventorySnapshot(
            await hostsTask,
            await credentialsTask,
            localIds.ToHashSet(StringComparer.Ordinal));
        await ConfigureLinkedHostWorkspaceProvidersAsync(snapshot);
        return snapshot;
    }

    private async Task ConfigureLinkedHostWorkspaceProvidersAsync(
        LinkedHostInventorySnapshot snapshot)
    {
        var activeHostIds = snapshot.Hosts
            .Where(host => !host.IsDisabled && host.HasCredential)
            .Select(host => host.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _registeredSshWorkspaceProviders
                     .Where(id => !activeHostIds.Contains(id))
                     .ToList())
        {
            await _workspaceFileSystems.RemoveAsync(
                WorkspaceProviderIds.Ssh,
                removed);
            _gitCommandRunners.RemoveSsh(removed);
            _registeredSshWorkspaceProviders.Remove(removed);
        }

        foreach (var host in snapshot.Hosts.Where(host =>
                     !host.IsDisabled && host.HasCredential))
        {
            var credential = snapshot.Credentials.FirstOrDefault(item =>
                !item.IsRevoked
                && string.Equals(
                    item.Id,
                    host.CredentialId,
                    StringComparison.Ordinal));
            if (credential is null)
            {
                continue;
            }

            _workspaceFileSystems.Register(
                new SshWorkspaceFileSystemProvider(
                    host,
                    credential,
                    _sshWorkspaceConnections));
            _gitCommandRunners.RegisterSsh(
                host.Id,
                new SshGitCommandRunner(
                    host,
                    credential,
                    new SshGitCommandTransport(_sshWorkspaceConnections)));
            _registeredSshWorkspaceProviders.Add(host.Id);
        }

        FileExplorer.ConfigureRemoteDevices(snapshot.Hosts);
    }

    private async Task ClearLinkedHostWorkspaceProvidersAsync()
    {
        foreach (var hostId in _registeredSshWorkspaceProviders.ToArray())
        {
            await _workspaceFileSystems.RemoveAsync(
                WorkspaceProviderIds.Ssh,
                hostId);
            _gitCommandRunners.RemoveSsh(hostId);
        }

        _registeredSshWorkspaceProviders.Clear();
        _gitCommandRunners.ClearSsh();
        FileExplorer.ConfigureRemoteDevices([]);
    }

    internal async Task<SshCredentialSummary> StoreSshCredentialAsync(
        string displayName,
        string privateKeyPath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        var privateKey = await ReadPrivateKeyFileAsync(
            privateKeyPath,
            cancellationToken);
        var passphraseUtf8 = Encoding.UTF8.GetBytes(passphrase ?? string.Empty);
        try
        {
            var configuration = _profileSyncSessionService.Configuration;
            var token = await _profileSyncSessionService.GetAccessTokenAsync(
                cancellationToken);
            await using var client = new HttpLinkedHostClient(
                configuration.ServerEndpoint,
                token);
            return await client.CreateCredentialAsync(
                new SshCredentialMutationRequest
                {
                    DisplayName = displayName,
                    PrivateKeyUtf8 = privateKey,
                    PassphraseUtf8 = passphraseUtf8
                },
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(passphraseUtf8);
        }
    }

    internal async Task<SshCredentialSummary> RotateSshCredentialAsync(
        SshCredentialSummary credential,
        string displayName,
        string privateKeyPath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var privateKey = await ReadPrivateKeyFileAsync(
            privateKeyPath,
            cancellationToken);
        var passphraseUtf8 = Encoding.UTF8.GetBytes(passphrase ?? string.Empty);
        var configuration = _profileSyncSessionService.Configuration;
        try
        {
            var token = await _profileSyncSessionService.GetAccessTokenAsync(
                cancellationToken);
            await using var client = new HttpLinkedHostClient(
                configuration.ServerEndpoint,
                token);
            var rotated = await client.RotateCredentialAsync(
                credential.Id,
                new SshCredentialMutationRequest
                {
                    DisplayName = displayName,
                    PrivateKeyUtf8 = privateKey,
                    PassphraseUtf8 = passphraseUtf8,
                    ExpectedKeyVersion = credential.KeyVersion
                },
                cancellationToken);
            _sshCredentialStore.Delete(
                configuration.ServerEndpoint,
                configuration.AccountId,
                credential.Id);
            return rotated;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(passphraseUtf8);
        }
    }

    internal async Task RevokeSshCredentialAsync(
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        await client.RevokeCredentialAsync(credentialId, cancellationToken);
        _sshCredentialStore.Delete(
            configuration.ServerEndpoint,
            configuration.AccountId,
            credentialId);
    }

    internal async Task<RemoteHostSummary> CreateLinkedHostAsync(
        RemoteHostMutationRequest host,
        CancellationToken cancellationToken = default)
    {
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        return await client.CreateHostAsync(host, cancellationToken);
    }

    internal async Task<RemoteHostSummary> UpdateLinkedHostAsync(
        string hostId,
        RemoteHostMutationRequest host,
        CancellationToken cancellationToken = default)
    {
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        return await client.UpdateHostAsync(
            hostId,
            host,
            cancellationToken);
    }

    internal async Task<RemoteHostSummary> PinLinkedHostKeyAsync(
        string hostId,
        RemoteHostKeyPinRequest pin,
        CancellationToken cancellationToken = default)
    {
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        return await client.PinHostKeyAsync(
            hostId,
            pin,
            cancellationToken);
    }

    internal async Task DisableLinkedHostAsync(
        string hostId,
        CancellationToken cancellationToken = default)
    {
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        await client.DisableHostAsync(hostId, cancellationToken);
    }

    internal async Task<SshCredentialUnlockOutcome>
        UnlockSshCredentialAsync(
            string credentialId,
            Action<SshCredentialReleaseAuthorization> authorizationRequired,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizationRequired);
        var configuration = _profileSyncSessionService.Configuration;
        var token = await _profileSyncSessionService.GetAccessTokenAsync(
            cancellationToken);
        await using var client = new HttpLinkedHostClient(
            configuration.ServerEndpoint,
            token);
        var authorization = await client.StartCredentialReleaseAsync(
            credentialId,
            cancellationToken);
        authorizationRequired(authorization);
        while (DateTimeOffset.UtcNow < authorization.ExpiresAtUtc)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(
                    Math.Clamp(authorization.PollIntervalSeconds, 2, 30)),
                cancellationToken);
            var poll = await client.PollCredentialReleaseAsync(
                authorization.ReleaseToken,
                cancellationToken);
            switch (poll.Kind)
            {
                case SshCredentialReleaseClientKind.Pending:
                    continue;
                case SshCredentialReleaseClientKind.Approved
                    when poll.Credential is { } released:
                    try
                    {
                        _sshCredentialStore.Save(
                            configuration.ServerEndpoint,
                            configuration.AccountId,
                            released);
                        return new SshCredentialUnlockOutcome(
                            released.CredentialId,
                            released.KeyVersion,
                            released.PublicKeyFingerprint);
                    }
                    finally
                    {
                        HttpLinkedHostClient.ClearReleasedCredential(released);
                    }
                case SshCredentialReleaseClientKind.Denied:
                    throw new ProfileSyncException(
                        ProfileSyncFailureKind.Authorization,
                        "The portal denied the SSH credential release.");
                case SshCredentialReleaseClientKind.Expired:
                    throw new ProfileSyncException(
                        ProfileSyncFailureKind.Timeout,
                        "The SSH credential release expired before approval.");
                case SshCredentialReleaseClientKind.Changed:
                    throw new ProfileSyncException(
                        ProfileSyncFailureKind.Conflict,
                        "The SSH credential changed after the release was requested. Refresh and retry.");
                case SshCredentialReleaseClientKind.Consumed:
                    throw new ProfileSyncException(
                        ProfileSyncFailureKind.Conflict,
                        "The SSH credential release was already consumed.");
                default:
                    throw new ProfileSyncException(
                        ProfileSyncFailureKind.NotFound,
                        "The SSH credential release is no longer valid.");
            }
        }

        throw new ProfileSyncException(
            ProfileSyncFailureKind.Timeout,
            "The SSH credential release expired before approval.");
    }

    private static async Task<byte[]> ReadPrivateKeyFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path?.Trim() ?? string.Empty);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length is <= 0 or > 1024 * 1024)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Choose a private-key file containing 1-1,048,576 bytes.");
        }

        var bytes = new byte[(int)info.Length];
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    internal async Task<ApplicationUpdateCheckOutcome>
        CheckForApplicationUpdateAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken = await _profileSyncSessionService
            .GetAccessTokenAsync(cancellationToken);
        _latestApplicationUpdateCheck =
            await _applicationUpdateService.CheckAsync(
                _profileSyncSessionService.Configuration.ServerEndpoint,
                accessToken,
                cancellationToken);
        OnPropertyChanged(nameof(LatestApplicationUpdateCheck));
        return _latestApplicationUpdateCheck;
    }

    internal async Task<ApplicationUpdateDownloadOutcome>
        DownloadApplicationUpdateAsync(
            ApplicationUpdateCandidate candidate,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
    {
        var accessToken = await _profileSyncSessionService
            .GetAccessTokenAsync(cancellationToken);
        return await _applicationUpdateService.DownloadAsync(
            candidate,
            _profileSyncSessionService.Configuration.ServerEndpoint,
            accessToken,
            progress,
            cancellationToken);
    }

    internal Process StartApplicationUpdate(
        ApplicationUpdateDownloadOutcome download) =>
        _applicationUpdateService.StartInstallAndRestart(
            download,
            Environment.ProcessId);

    internal async Task<IReadOnlyList<RemoteProfileSummary>> ListRemoteProfilesAsync(
        CancellationToken cancellationToken = default)
        => await UseProfileSyncTransportAsync(
            (transport, token) =>
                _profileSyncCoordinator.ListAsync(transport, token),
            cancellationToken);

    internal async Task<ProfileSyncDownloadOutcome> DownloadRemoteProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
        => await UseProfileSyncTransportAsync(
            (transport, token) =>
                _profileSyncCoordinator.DownloadPreviewAsync(
                    transport,
                    _profileSyncSessionService.Configuration.ServerEndpoint,
                    profileId,
                    _extensionCatalog,
                    TerminalProfiles,
                    token),
            cancellationToken);

    internal void AcceptDownloadedProfile(
        ProfileSyncDownloadOutcome download) =>
        _profileSyncCoordinator.AcceptDownloadedProfile(
            download,
            _settings,
            _extensionCatalog);

    internal async Task<ProfileSyncUploadOutcome> UploadCurrentProfileAsync(
        CancellationToken cancellationToken = default)
        => await UseProfileSyncTransportAsync(
            (transport, token) =>
                _profileSyncCoordinator.UploadAsync(
                    transport,
                    _profileSyncSessionService.Configuration.ServerEndpoint,
                    _settings,
                    _extensionCatalog,
                    TerminalProfiles,
                    token),
            cancellationToken);

    internal async Task<ProfileCloudSyncOutcome> SyncCurrentCloudStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _cloudSyncGate.WaitAsync(cancellationToken);
        try
        {
            var portable = await UploadCurrentProfileAsync(
                cancellationToken);
            return await UseCloudRoamingTransportAsync(
                async (transport, token) =>
                {
                    var workspace = await _workspaceProfileRoamingService
                        .UploadAsync(
                            transport,
                            _profileSyncSessionService.Configuration
                                .ServerEndpoint,
                            _settings,
                            _extensionCatalog,
                            token);
                    if (workspace.Kind
                        == WorkspaceProfileRoamingSyncOutcomeKind.Conflict)
                    {
                        return new ProfileCloudSyncOutcome(
                            portable,
                            workspace,
                            new HyperlibPackageRoamingOutcome(
                                0,
                                0,
                                0,
                                [],
                                []),
                            new ConversationRoamingSyncOutcome(
                                0,
                                0,
                                0,
                                []));
                    }

                    var snapshot = _workspaceProfileRoamingService
                        .CreateSnapshot(_settings, _extensionCatalog);
                    var packages = await _hyperlibPackageRoamingService
                        .UploadMissingAsync(
                            transport,
                            _extensionCatalog,
                            snapshot.Document.Packages,
                            token);
                    var conversations = await _conversationRoamingService
                        .UploadAllAsync(
                            transport,
                            _profileSyncSessionService.Configuration
                                .ServerEndpoint,
                            _settings.PortableProfileId,
                            token);
                    return new ProfileCloudSyncOutcome(
                        portable,
                        workspace,
                        packages,
                        conversations);
                },
                cancellationToken);
        }
        finally
        {
            _cloudSyncGate.Release();
        }
    }

    internal async Task<ProfileCloudRestoreOutcome>
        RestoreCloudProfileResourcesAsync(
            string profileId,
            CancellationToken cancellationToken = default)
    {
        var outcome = await UseCloudRoamingTransportAsync(
            async (transport, token) =>
            {
                var workspace = await _workspaceProfileRoamingService
                    .DownloadAndApplyAsync(
                        transport,
                        _profileSyncSessionService.Configuration
                            .ServerEndpoint,
                        _settings,
                        _extensionCatalog,
                        profileId,
                        token);
                var packages = await _hyperlibPackageRoamingService
                    .RestoreMissingAsync(
                        transport,
                        _extensionCatalog,
                        workspace.PackageSelections,
                        token);
                var conversations = await _conversationRoamingService
                    .DownloadAllAsync(
                        transport,
                        _profileSyncSessionService.Configuration
                            .ServerEndpoint,
                        profileId,
                        token);
                return new ProfileCloudRestoreOutcome(
                    workspace,
                    packages,
                    conversations);
            },
            cancellationToken);

        if (outcome.Packages.Downloaded > 0)
        {
            await RefreshExtensionCatalogAsync();
        }

        _settingsService.Save(_settings);
        ApplyExplorerSettings(_settings.Explorer, persist: false);
        foreach (var conversation in WorkspaceAiConversations)
        {
            conversation.Assistant.RefreshState();
        }

        return outcome;
    }

    internal async Task<ProfileSyncDeleteOutcome> DeleteRemoteProfileAsync(
        RemoteProfileSummary profile,
        CancellationToken cancellationToken = default)
        => await UseProfileSyncTransportAsync(
            (transport, token) =>
                _profileSyncCoordinator.DeleteAsync(
                    transport,
                    _profileSyncSessionService.Configuration.ServerEndpoint,
                    profile,
                    token),
            cancellationToken);

    private async Task<T> UseProfileSyncTransportAsync<T>(
        Func<IProfileSyncTransport, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = await _profileSyncSessionService
                .GetAccessTokenAsync(cancellationToken);
            var endpoint = _profileSyncSessionService.Configuration
                .ServerEndpoint;
            await using var transport = new HttpProfileSyncTransport(
                endpoint,
                accessToken);
            try
            {
                return await operation(transport, cancellationToken);
            }
            catch (ProfileSyncException exception) when (
                attempt == 0
                && _profileSyncSessionService.HasSavedCredential
                && (exception.Kind is ProfileSyncFailureKind.Authentication
                    or ProfileSyncFailureKind.Authorization))
            {
                _profileSyncSessionService.InvalidateSession(
                    "The server rejected the prior session. Hypershell will use the protected access key once to request a replacement.");
            }
        }

        throw new ProfileSyncException(
            ProfileSyncFailureKind.Authentication,
            "The profile server rejected the renewed application session.");
    }

    private async Task<T> UseCloudRoamingTransportAsync<T>(
        Func<ICloudRoamingTransport, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = await _profileSyncSessionService
                .GetAccessTokenAsync(cancellationToken);
            await using var transport = new HttpCloudRoamingTransport(
                _profileSyncSessionService.Configuration.ServerEndpoint,
                accessToken);
            try
            {
                return await operation(transport, cancellationToken);
            }
            catch (ProfileSyncException exception) when (
                attempt == 0
                && _profileSyncSessionService.HasSavedCredential
                && exception.Kind is ProfileSyncFailureKind.Authentication
                    or ProfileSyncFailureKind.Authorization)
            {
                _profileSyncSessionService.InvalidateSession(
                    "The server rejected the prior session. Hypershell will use the protected access key once to request a replacement.");
            }
        }

        throw new ProfileSyncException(
            ProfileSyncFailureKind.Authentication,
            "The account service rejected the renewed application session.");
    }

    internal async Task ApplyPortableProfileAsync(
        ProfileImportPreview preview,
        bool persist)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.CanImport || preview.Envelope is null)
        {
            throw new InvalidOperationException(
                "The account settings failed validation and cannot be applied.");
        }

        var candidate = _portableProfileService.CreateMergedSettings(
            _settings,
            preview.Envelope.Profile,
            _workspacePanelRegistry);
        if (!persist)
        {
            ApplyApplicationPreferences(
                candidate.Application,
                candidate.ActiveTerminalProfileId,
                persist: false);
            ApplyTerminalConfigurationProfiles(
                candidate.TerminalConfigurationProfiles,
                selectedConfigurationId: string.Empty,
                persist: false);
            ApplyExplorerSettings(candidate.Explorer, persist: false);
            ApplyWorkspaceLayout(candidate.WorkspaceLayout, persist: false);
            ShowTransientStatus(
                "Applied the account appearance and layout for this session.");
            return;
        }

        // Commit the fully merged candidate before mutating the live model. If
        // the atomic settings write fails, the running workspace is untouched.
        _settingsService.Save(candidate);
        CopyImportedSettings(candidate);
        RefreshTerminalProfiles();
        CommandBuilder.ApplyEnvironmentProfiles(
            candidate.Environments,
            candidate.ActiveEnvironmentId);
        ApplyApplicationPreferences(
            candidate.Application,
            candidate.ActiveTerminalProfileId,
            persist: false);
        ApplyTerminalConfigurationProfiles(
            candidate.TerminalConfigurationProfiles,
            selectedConfigurationId: string.Empty,
            persist: false);
        ApplyExplorerSettings(candidate.Explorer, persist: false);
        ApplyWorkspaceLayout(candidate.WorkspaceLayout, persist: false);
        await CommandBuilder.LoadLibrariesAsync(_catalogService);
        ShowTransientStatus(
            "Saved the current Atlas account settings. Device-local paths, approvals, and live sessions were preserved.");
    }

    internal bool IsWebPanelApproved(DiscoveredExtensionPackage package) =>
        _extensionApprovalService.IsWebPanelApproved(package);

    internal IReadOnlySet<ExtensionCapability> GetApprovedPanelCapabilities(
        DiscoveredExtensionPackage package) =>
        _extensionApprovalService.GetApprovedCapabilities(package);

    internal bool CanApproveWebPanels(
        DiscoveredExtensionPackage package,
        out string reason) =>
        _extensionApprovalService.CanApproveWebPanels(package, out reason);

    internal void ApproveWebPanels(DiscoveredExtensionPackage package)
    {
        _extensionApprovalService.ApproveWebPanels(package);
        SynchronizeApprovedPanelRegistry();
        var normalized = WorkspaceLayoutService.Normalize(
            _runtimeWorkspaceLayout,
            _workspacePanelRegistry);
        var extensionId = package.Manifest?.Id ?? string.Empty;
        _runtimeWorkspaceLayout = new WorkspaceLayoutSnapshot
        {
            IsUserUnlocked = normalized.IsUserUnlocked,
            SideTools = normalized.SideTools
                .Select(tool => tool.Clone())
                .ToList(),
            Panels = normalized.Panels
                .Select(panel => string.Equals(
                    panel.ExtensionId,
                    extensionId,
                    StringComparison.OrdinalIgnoreCase)
                    ? CopyPanelPlacement(
                        panel,
                        isVisible: false,
                        replacement: panel.Replaces)
                    : panel.Clone())
                .ToList()
        };
        OnPropertyChanged(nameof(WorkspaceLayout));
    }

    internal void RevokeWebPanels(DiscoveredExtensionPackage package)
    {
        HideExtensionPanels(package.Manifest?.Id ?? string.Empty);
        if (package.Manifest is { } manifest)
        {
            _extensionApprovalService.Revoke(manifest.Id);
        }

        SynchronizeApprovedPanelRegistry();
        _runtimeWorkspaceLayout = WorkspaceLayoutService.Normalize(
            _runtimeWorkspaceLayout,
            _workspacePanelRegistry);
        OnPropertyChanged(nameof(WorkspaceLayout));
    }

    internal void DeactivateExtensionPanels(
        DiscoveredExtensionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        HideExtensionPanels(package.Manifest?.Id ?? string.Empty);
    }

    internal bool TryGetApprovedPanelCandidate(
        string panelId,
        out ExtensionPanelCandidate? candidate)
    {
        candidate = _extensionCatalog.PanelCandidates.FirstOrDefault(item =>
            string.Equals(
                item.PanelId,
                panelId,
                StringComparison.OrdinalIgnoreCase)
            && _extensionApprovalService.IsWebPanelApproved(item.Package));
        return candidate is not null;
    }

    internal IReadOnlyList<TerminalProfile> GetNewTabTerminalProfiles() =>
        TerminalProfiles.Select(CreateEffectiveTerminalProfile).ToList();

    public TerminalProfile SelectedTerminalProfile
    {
        get => _selectedTerminalProfile;
        set
        {
            if (!value.IsAvailable || !SetProperty(ref _selectedTerminalProfile, value))
            {
                return;
            }

            _settings.ActiveTerminalProfileId = value.Id;
            OnPropertyChanged(nameof(TerminalSettings));
            OnPropertyChanged(nameof(SelectedTerminalProfileStatus));
            ScheduleAutosave();
        }
    }

    public string SelectedTerminalProfileStatus => SelectedTerminalProfile.AvailabilityMessage;

    public IWorkspaceTab? ActiveWorkspaceTab
    {
        get => _activeWorkspaceTab;
        set
        {
            var previous = _activeWorkspaceTab;
            if (SetProperty(ref _activeWorkspaceTab, value))
            {
                if (previous is not null)
                {
                    WorkspaceAiAssistant.CancelLiveActivity(
                        previous.AiLiveSourceId);
                }

                OnPropertyChanged(nameof(ActiveTerminal));
                OnPropertyChanged(nameof(ActiveDocument));
                OnPropertyChanged(nameof(ExtensionPanelWorkspaceContext));
                WorkspaceAiAssistant.SelectWorkspaceForPath(value switch
                {
                    DocumentTabViewModel { HasFilePath: true } document =>
                        document.LocalWorkingFilePath,
                    TerminalTabViewModel terminal =>
                        terminal.WorkingDirectory,
                    _ => null
                });
                ScheduleEditorSessionSave();
            }
        }
    }

    public TerminalTabViewModel? ActiveTerminal
    {
        get => ActiveWorkspaceTab as TerminalTabViewModel;
        set => ActiveWorkspaceTab = value;
    }

    public DocumentTabViewModel? ActiveDocument =>
        ActiveWorkspaceTab as DocumentTabViewModel;

    public bool IsCommandBuilderOpen
    {
        get => _isCommandBuilderOpen;
        set
        {
            if (SetProperty(ref _isCommandBuilderOpen, value))
            {
                OnPropertyChanged(nameof(IsCommandBuilderClosed));
                RefreshNetworkObservatoryActivation();
            }
        }
    }

    public bool IsCommandBuilderClosed => !IsCommandBuilderOpen;

    public bool IsFileExplorerToolPageActive => string.Equals(
        _activeFileExplorerSideToolId,
        WorkspaceSideToolRegistry.FileExplorerToolId,
        StringComparison.OrdinalIgnoreCase);
    public bool IsProjectWorkItemsPageActive => string.Equals(
        _activeFileExplorerSideToolId,
        WorkspaceSideToolRegistry.ProjectWorkItemsToolId,
        StringComparison.OrdinalIgnoreCase);
    public bool IsSourceControlPageActive => string.Equals(
        _activeFileExplorerSideToolId,
        WorkspaceSideToolRegistry.SourceControlToolId,
        StringComparison.OrdinalIgnoreCase);
    public bool IsAtlasMyNetPageActive => string.Equals(
        _activeFileExplorerSideToolId,
        WorkspaceSideToolRegistry.AtlasMyNetToolId,
        StringComparison.OrdinalIgnoreCase);

    public bool IsCommandBuilderToolPageActive =>
        _commandBuilderPage == CommandBuilderPanePage.Builder;
    public bool IsCommandBuilderConfigurationPageActive => false;
    public bool IsWorkspaceAiPageActive =>
        _commandBuilderPage == CommandBuilderPanePage.Ai;
    public bool IsNetworkObservatoryPageActive =>
        _commandBuilderPage == CommandBuilderPanePage.NetworkObservatory;

    public bool IsFileExplorerOpen
    {
        get => _isFileExplorerOpen;
        set
        {
            if (SetProperty(ref _isFileExplorerOpen, value))
            {
                OnPropertyChanged(nameof(IsFileExplorerClosed));
                RefreshAtlasMyNetActivation();
            }
        }
    }

    public bool IsFileExplorerClosed => !IsFileExplorerOpen;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AutosaveStatusText
    {
        get => _autosaveStatusText;
        private set => SetProperty(ref _autosaveStatusText, value);
    }

    public RelayCommand ToggleFileExplorerCommand { get; }
    public RelayCommand ToggleCommandBuilderCommand { get; }
    public RelayCommand ShowCommandBuilderToolCommand { get; }
    public RelayCommand ShowWorkspaceAiCommand { get; }

    private void SetCommandBuilderPage(CommandBuilderPanePage page)
    {
        if (_commandBuilderPage == page)
        {
            RefreshWorkspaceSideToolSelection();
            return;
        }

        _commandBuilderPage = page;
        OnPropertyChanged(nameof(IsCommandBuilderToolPageActive));
        OnPropertyChanged(nameof(IsCommandBuilderConfigurationPageActive));
        OnPropertyChanged(nameof(IsWorkspaceAiPageActive));
        OnPropertyChanged(nameof(IsNetworkObservatoryPageActive));
        RefreshWorkspaceSideToolSelection();
        if (page == CommandBuilderPanePage.Ai)
        {
            WorkspaceAiAssistant.RefreshState();
        }

        RefreshNetworkObservatoryActivation();
    }

    private void SelectWorkspaceSideTool(string toolId)
    {
        var definition = WorkspaceSideToolRegistry.GetRequired(toolId);
        switch (definition.Kind)
        {
            case WorkspaceSideToolKind.FileExplorer:
            case WorkspaceSideToolKind.ProjectWorkItems:
            case WorkspaceSideToolKind.SourceControl:
            case WorkspaceSideToolKind.AtlasMyNet:
                SetFileExplorerPage(definition.Id);
                if (definition.Kind == WorkspaceSideToolKind.SourceControl)
                {
                    _ = SourceControl.RefreshAsync();
                }
                break;

            case WorkspaceSideToolKind.CommandBuilder:
                IsCommandBuilderOpen = true;
                SetCommandBuilderPage(CommandBuilderPanePage.Builder);
                break;

            case WorkspaceSideToolKind.WorkspaceAi:
                IsCommandBuilderOpen = true;
                SetCommandBuilderPage(CommandBuilderPanePage.Ai);
                break;

            case WorkspaceSideToolKind.NetworkObservatory:
                IsCommandBuilderOpen = true;
                SetCommandBuilderPage(CommandBuilderPanePage.NetworkObservatory);
                break;
        }
    }

    private void SetFileExplorerPage(string toolId)
    {
        _activeFileExplorerSideToolId = toolId;
        IsFileExplorerOpen = true;
        OnPropertyChanged(nameof(IsFileExplorerToolPageActive));
        OnPropertyChanged(nameof(IsProjectWorkItemsPageActive));
        OnPropertyChanged(nameof(IsSourceControlPageActive));
        OnPropertyChanged(nameof(IsAtlasMyNetPageActive));
        RefreshWorkspaceSideToolSelection();
        RefreshAtlasMyNetActivation();
    }

    private void OnFileExplorerPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(FileSystemExplorerViewModel.CurrentWorkspaceUri)
            or nameof(FileSystemExplorerViewModel.CurrentPath)
            or nameof(FileSystemExplorerViewModel.SelectedDevice))
        {
            UpdateSourceControlWorkspaceLocation();
        }

        if (!string.Equals(
                eventArgs.PropertyName,
                nameof(FileSystemExplorerViewModel.SelectedProject),
                StringComparison.Ordinal))
        {
            return;
        }

        _runtimeExplorerSettings.ActiveProjectId =
            FileExplorer.SelectedProject?.Id ?? string.Empty;
        WorkItems.UpdateProjectScopes(
            _runtimeExplorerSettings.Projects,
            _runtimeExplorerSettings.ActiveProjectId,
            FileExplorer.CurrentPath);
        OnPropertyChanged(nameof(ExtensionPanelWorkspaceContext));
        UpdateSourceControlWorkspaceLocation();
    }

    private void UpdateSourceControlWorkspaceLocation()
    {
        if (WorkspaceLocation.TryParse(
                FileExplorer.CurrentWorkspaceUri,
                out var location))
        {
            SourceControl.SetWorkspaceLocation(location);
        }
    }

    private void OnSourceControlDiffRequested(
        object? sender,
        GitDiffDocument diff)
    {
        var name = Path.GetFileName(diff.Path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "changes";
        }

        CreateUntitledDocument(new NewDocumentRequest(
            name + ".diff",
            EditorLanguageCatalog.CreateDefaultSettings("diff") with
            {
                WordWrap = false,
                FormatOnType = false,
                FormatOnPaste = false
            },
            LineEndingKind: DocumentLineEndingKind.Lf,
            InitialContent: diff.Patch));
    }

    private void OnSourceControlConflictRequested(
        object? sender,
        GitConflictDocument conflict)
    {
        var fileName = Path.GetFileName(conflict.Path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "conflict.txt";
        }

        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var settings = EditorLanguageCatalog.CreateDefaultSettings(
            EditorLanguageCatalog.ResolveLanguageId(fileName)) with
        {
            FormatOnType = false,
            FormatOnPaste = false
        };
        CreateConflictStageDocument(
            $"{stem}.BASE{extension}",
            conflict.BaseContent,
            settings);
        CreateConflictStageDocument(
            $"{stem}.OURS{extension}",
            conflict.OursContent,
            settings);
        CreateConflictStageDocument(
            $"{stem}.THEIRS{extension}",
            conflict.TheirsContent,
            settings);
    }

    private void CreateConflictStageDocument(
        string name,
        string content,
        DocumentEditorSettings settings)
    {
        CreateUntitledDocument(new NewDocumentRequest(
            name,
            settings,
            LineEndingKind: DocumentLineEndingKind.Lf,
            InitialContent: content));
    }

    private string? GetWorkspaceAiWorkItemsContext(string contextId)
    {
        if (string.IsNullOrWhiteSpace(WorkItems.StorePath)
            || !File.Exists(WorkItems.StorePath))
        {
            return null;
        }

        var authorization = _workspaceAiPermissionBroker.AuthorizePath(
            contextId,
            WorkItems.StorePath,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ReadText);
        if (!authorization.IsAllowed)
        {
            return null;
        }

        try
        {
            return WorkItems.CreateAiWorkCycleContext();
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void RefreshWorkspaceSideTools()
    {
        var placements = _runtimeWorkspaceLayout.SideTools
            .ToDictionary(
                tool => tool.ToolId,
                StringComparer.OrdinalIgnoreCase);
        var visibleDefinitions = WorkspaceSideToolRegistry.Definitions
            .Where(definition => placements.TryGetValue(
                                     definition.Id,
                                     out var placement)
                                 && placement.IsVisible)
            .OrderBy(definition => placements[definition.Id].Order)
            .ThenBy(definition => definition.DefaultOrder)
            .ToList();

        RebuildWorkspaceSideToolCollection(
            FileExplorerSideTools,
            visibleDefinitions.Where(definition =>
                definition.HostSurface == BuiltInWorkspaceSurface.FileExplorer));
        RebuildWorkspaceSideToolCollection(
            CommandBuilderSideTools,
            visibleDefinitions.Where(definition =>
                definition.HostSurface == BuiltInWorkspaceSurface.CommandBuilder));

        if (!FileExplorerSideTools.Any(tool => string.Equals(
                tool.Id,
                _activeFileExplorerSideToolId,
                StringComparison.OrdinalIgnoreCase)))
        {
            _activeFileExplorerSideToolId =
                FileExplorerSideTools.First().Id;
            OnPropertyChanged(nameof(IsFileExplorerToolPageActive));
            OnPropertyChanged(nameof(IsProjectWorkItemsPageActive));
            OnPropertyChanged(nameof(IsSourceControlPageActive));
            OnPropertyChanged(nameof(IsAtlasMyNetPageActive));
        }

        var activeCommandToolId = GetActiveCommandBuilderSideToolId();
        if (!CommandBuilderSideTools.Any(tool => string.Equals(
                tool.Id,
                activeCommandToolId,
                StringComparison.OrdinalIgnoreCase)))
        {
            _commandBuilderPage = CommandBuilderSideTools.First().Kind switch
            {
                WorkspaceSideToolKind.WorkspaceAi => CommandBuilderPanePage.Ai,
                WorkspaceSideToolKind.NetworkObservatory =>
                    CommandBuilderPanePage.NetworkObservatory,
                _ => CommandBuilderPanePage.Builder
            };
            OnPropertyChanged(nameof(IsCommandBuilderToolPageActive));
            OnPropertyChanged(nameof(IsCommandBuilderConfigurationPageActive));
            OnPropertyChanged(nameof(IsWorkspaceAiPageActive));
            OnPropertyChanged(nameof(IsNetworkObservatoryPageActive));
        }

        RefreshWorkspaceSideToolSelection();
        RefreshAtlasMyNetActivation();
        RefreshNetworkObservatoryActivation();
    }

    private void RefreshAtlasMyNetActivation() =>
        AtlasMyNet.SetActive(IsFileExplorerOpen && IsAtlasMyNetPageActive);

    private void RefreshNetworkObservatoryActivation() =>
        NetworkObservatory.SetActive(
            IsCommandBuilderOpen && IsNetworkObservatoryPageActive);

    private void RebuildWorkspaceSideToolCollection(
        ObservableCollection<WorkspaceSideToolViewModel> target,
        IEnumerable<WorkspaceSideToolDefinition> definitions)
    {
        target.Clear();
        foreach (var definition in definitions)
        {
            target.Add(new WorkspaceSideToolViewModel(
                definition,
                SelectWorkspaceSideTool));
        }
    }

    private void RefreshWorkspaceSideToolSelection()
    {
        foreach (var tool in FileExplorerSideTools)
        {
            tool.IsActive = string.Equals(
                tool.Id,
                _activeFileExplorerSideToolId,
                StringComparison.OrdinalIgnoreCase);
        }

        var activeCommandToolId = GetActiveCommandBuilderSideToolId();
        foreach (var tool in CommandBuilderSideTools)
        {
            tool.IsActive = string.Equals(
                tool.Id,
                activeCommandToolId,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private string GetActiveCommandBuilderSideToolId() =>
        _commandBuilderPage switch
        {
            CommandBuilderPanePage.Ai =>
                WorkspaceSideToolRegistry.WorkspaceAiToolId,
            CommandBuilderPanePage.NetworkObservatory =>
                WorkspaceSideToolRegistry.NetworkObservatoryToolId,
            _ => WorkspaceSideToolRegistry.CommandBuilderToolId
        };

    public WorkspaceAiConversationTabViewModel CreateWorkspaceAiConversation()
    {
        var conversation = CreateWorkspaceAiConversationCore();
        WorkspaceAiConversations.Add(conversation);
        SelectedWorkspaceAiConversation = conversation;
        SaveConversation(conversation, isOpenOnThisDevice: true);
        ScheduleCloudSync();
        return conversation;
    }

    public bool CloseWorkspaceAiConversation(
        WorkspaceAiConversationTabViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (!WorkspaceAiConversations.Contains(conversation))
        {
            return false;
        }

        conversation.Assistant.CancelActiveRequest();
        var closingIndex = WorkspaceAiConversations.IndexOf(conversation);
        if (WorkspaceAiConversations.Count == 1)
        {
            var replacement = CreateWorkspaceAiConversationCore();
            WorkspaceAiConversations.Add(replacement);
            SelectedWorkspaceAiConversation = replacement;
        }
        else if (ReferenceEquals(
                     SelectedWorkspaceAiConversation,
                     conversation))
        {
            SelectedWorkspaceAiConversation = closingIndex + 1
                                              < WorkspaceAiConversations.Count
                ? WorkspaceAiConversations[closingIndex + 1]
                : WorkspaceAiConversations[closingIndex - 1];
        }

        SaveConversation(conversation, isOpenOnThisDevice: false);
        ScheduleCloudSync();
        UnsubscribeConversationPersistence(conversation);
        WorkspaceAiConversations.Remove(conversation);
        conversation.Dispose();
        return true;
    }

    private WorkspaceAiConversationTabViewModel
        CreateWorkspaceAiConversationCore(
            LocalAiConversationDocument? restored = null)
    {
        var conversationId = restored?.Id ?? Guid.NewGuid().ToString("N");
        var coordinator = new WorkspaceAiConversationCoordinator(
            _workspaceAiProvider,
            _workspaceAiProviderConfigurationService,
            _workspaceAiContextService,
            _workspaceAiCredentialStore,
            _workspaceAiToolHost,
            _workspaceAiComputerControlService,
            GetWorkspaceAiWorkItemsContext,
            conversationId);
        var assistant = new WorkspaceAiAssistantViewModel(
            _workspaceAiContextService,
            _workspaceAiProviderConfigurationService,
            _workspaceAiCredentialStore,
            coordinator,
            _workspaceAiComputerControlService,
            _workspaceAiLocalRuntime);
        WorkspaceAiConversationTabViewModel conversation;
        if (restored is null)
        {
            var number = ++_workspaceAiConversationNumber;
            conversation = new WorkspaceAiConversationTabViewModel(
                conversationId,
                $"AI {number}",
                DateTimeOffset.UtcNow,
                assistant);
        }
        else
        {
            _workspaceAiConversationNumber++;
            assistant.RestoreConversation(restored);
            conversation = new WorkspaceAiConversationTabViewModel(
                restored.Id,
                restored.Title,
                restored.CreatedAtUtc,
                assistant);
        }

        SubscribeConversationPersistence(conversation);
        return conversation;
    }

    private void SubscribeConversationPersistence(
        WorkspaceAiConversationTabViewModel conversation)
    {
        conversation.Assistant.ConversationChanging +=
            OnWorkspaceAiConversationChanging;
        conversation.Assistant.ConversationChanged +=
            OnWorkspaceAiConversationChanged;
    }

    private void UnsubscribeConversationPersistence(
        WorkspaceAiConversationTabViewModel conversation)
    {
        conversation.Assistant.ConversationChanging -=
            OnWorkspaceAiConversationChanging;
        conversation.Assistant.ConversationChanged -=
            OnWorkspaceAiConversationChanged;
    }

    private void OnWorkspaceAiConversationChanging(
        object? sender,
        EventArgs eventArgs)
    {
        var conversation = WorkspaceAiConversations.FirstOrDefault(item =>
            ReferenceEquals(item.Assistant, sender));
        if (conversation is null)
        {
            return;
        }

        SaveConversation(conversation, isOpenOnThisDevice: false);
        conversation.BeginNewConversation(
            $"AI {++_workspaceAiConversationNumber}");
        conversation.Assistant.BindConversationId(conversation.ConversationId);
    }

    private void OnWorkspaceAiConversationChanged(
        object? sender,
        EventArgs eventArgs)
    {
        var conversation = WorkspaceAiConversations.FirstOrDefault(item =>
            ReferenceEquals(item.Assistant, sender));
        if (conversation is null)
        {
            return;
        }

        if (conversation.DisplayTitle.StartsWith(
                "AI ",
                StringComparison.Ordinal)
            && conversation.Assistant.Messages.FirstOrDefault(message =>
                   message.Author.StartsWith(
                       "You",
                       StringComparison.OrdinalIgnoreCase)) is { } first)
        {
            conversation.Rename(BuildConversationTitle(first.Text));
        }

        SaveConversation(conversation, isOpenOnThisDevice: true);
        ScheduleCloudSync();
    }

    private void SaveConversation(
        WorkspaceAiConversationTabViewModel conversation,
        bool isOpenOnThisDevice)
    {
        try
        {
            _localAiConversationStore.Save(
                new LocalAiConversationDocument
                {
                    Id = conversation.ConversationId,
                    ProfileId = _settings.PortableProfileId,
                    Title = conversation.DisplayTitle,
                    IsOpenOnThisDevice = isOpenOnThisDevice,
                    ProviderId = conversation.Assistant.SessionProviderId,
                    ModelId = conversation.Assistant.SessionModelId,
                    WorkingDirectory = conversation.Assistant.WorkingDirectory,
                    CompactedContext = conversation.Assistant
                        .ExportContinuationContext(),
                    Items = conversation.Assistant
                        .SnapshotConversationItems(),
                    CreatedAtUtc = conversation.CreatedAtUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException)
        {
            StatusMessage =
                $"Conversation history could not be saved: {exception.Message}";
        }
    }

    private static string BuildConversationTitle(string prompt)
    {
        var normalized = string.Join(
            ' ',
            (prompt ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 44)
        {
            normalized = normalized[..41].TrimEnd() + "…";
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? "AI conversation"
            : normalized;
    }

    private void AttachWorkspaceAiAssistant(
        WorkspaceAiAssistantViewModel assistant)
    {
        assistant.LiveSourcesInvalidated +=
            OnWorkspaceAiLiveSourcesInvalidated;
        assistant.DesktopLiveSessionChanged +=
            OnWorkspaceAiDesktopLiveSessionChanged;
        assistant.PropertyChanged += OnWorkspaceAiAssistantPropertyChanged;
        if (assistant.IsDesktopLiveSessionEnabled && !assistant.IsBusy)
        {
            _workspaceAiUserActivityMonitor.Start();
        }
    }

    private void DetachWorkspaceAiAssistant(
        WorkspaceAiAssistantViewModel assistant)
    {
        assistant.LiveSourcesInvalidated -=
            OnWorkspaceAiLiveSourcesInvalidated;
        assistant.DesktopLiveSessionChanged -=
            OnWorkspaceAiDesktopLiveSessionChanged;
        assistant.PropertyChanged -= OnWorkspaceAiAssistantPropertyChanged;
        _workspaceAiUserActivityMonitor.Stop();
    }

    public void RefreshTerminalProfiles()
    {
        var selectedProfileId = SelectedTerminalProfile.Id;
        TerminalProfileService.ReconcileProfiles(_settings);
        TerminalProfiles = _settings.TerminalProfiles.ToList();
        AvailableTerminalProfiles = TerminalProfiles
            .Where(profile => profile.IsAvailable)
            .ToList();
        RefreshObservedTerminalSettings();

        var refreshedSelection = AvailableTerminalProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, selectedProfileId, StringComparison.OrdinalIgnoreCase))
            ?? AvailableTerminalProfiles.First(profile => profile.Id == "powershell");
        if (!ReferenceEquals(_selectedTerminalProfile, refreshedSelection))
        {
            _selectedTerminalProfile = refreshedSelection;
            OnPropertyChanged(nameof(SelectedTerminalProfile));
            OnPropertyChanged(nameof(TerminalSettings));
            OnPropertyChanged(nameof(SelectedTerminalProfileStatus));
        }

        OnPropertyChanged(nameof(TerminalProfiles));
        OnPropertyChanged(nameof(AvailableTerminalProfiles));
    }

    public async Task InitializeAsync()
    {
        StatusMessage = "Connecting saved profile…";
        var restoredProfileSession =
            await _profileSyncSessionService.InitializeAsync();
        if (restoredProfileSession)
        {
            CompleteProfileLogin();
        }
        else
        {
            ResolveStartupProfileLogin();
        }

        StatusMessage = "Restoring editor session…";
        var (restoredDocumentCount, unavailableRecoveryCount) =
            await RestoreEditorSessionAsync();
        StatusMessage = "Loading Atlas MyNet…";
        await AtlasMyNet.InitializeAsync();
        RefreshAtlasMyNetActivation();
        StatusMessage = "Loading command lexicon…";
        await CommandBuilder.LoadLibrariesAsync(_catalogService);
        if (restoredProfileSession
            && _applicationUpdateService.IsTrustConfigured)
        {
            _ = CheckForApplicationUpdateOnStartupAsync();
        }
        if (unavailableRecoveryCount > 0)
        {
            ShowTransientStatus(
                $"Restored {restoredDocumentCount} document tab(s); " +
                $"{unavailableRecoveryCount} unsaved recovery snapshot(s) were unavailable.");
        }
        else if (restoredDocumentCount > 0)
        {
            ShowTransientStatus(
                $"Restored {restoredDocumentCount} document tab(s) from the previous workspace.");
        }
        else if (restoredProfileSession)
        {
            ShowTransientStatus(
                $"{_profileSyncSessionService.SessionIdentity}. Your current account state syncs automatically.");
        }
        else if (_profileSyncSessionService.IsOffline
                 && _profileSyncSessionService.HasSavedCredential)
        {
            ShowTransientStatus(
                "Profile sync is offline; the local workspace remains fully available.");
        }
        else if (CommandBuilder.StatusMessage.StartsWith("Loaded ", StringComparison.Ordinal))
        {
            ShowTransientStatus(CommandBuilder.StatusMessage);
        }
        else
        {
            _statusCueTimer.Stop();
            StatusMessage = CommandBuilder.StatusMessage;
        }
    }

    private async Task CheckForApplicationUpdateOnStartupAsync()
    {
        try
        {
            var outcome = await CheckForApplicationUpdateAsync();
            if (outcome.Kind == ApplicationUpdateCheckKind.Available)
            {
                ShowTransientStatus(outcome.Message);
            }
        }
        catch (Exception exception) when (exception is ProfileSyncException
                                         or InvalidDataException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            // Startup update checks are advisory. The Settings surface exposes
            // the actionable error without interfering with local startup.
        }
    }

    public async Task<DocumentFormattingResult> FormatDocumentAsync(
        DocumentTabViewModel document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _documentFormatGate.WaitAsync(cancellationToken);
        try
        {
            var sourceContent = document.Content;
            DocumentFormattingResult result;
            try
            {
                result = await _externalDocumentFormatterService.FormatAsync(
                    new DocumentFormattingRequest
                    {
                        LanguageId = document.LanguageId,
                        Content = sourceContent,
                        FilePath = document.LocalWorkingFilePath,
                        FileName = document.Title,
                        WorkingDirectory = FileExplorer.CurrentPath
                    },
                    _languageToolCatalog,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = new DocumentFormattingResult
                {
                    Outcome = DocumentFormattingOutcome.Failed,
                    Message = $"External formatting failed: {exception.Message}"
                };
            }

            if (_isDisposed)
            {
                return new DocumentFormattingResult
                {
                    Outcome = DocumentFormattingOutcome.Rejected,
                    FormatterId = result.FormatterId,
                    FormatterDisplayName = result.FormatterDisplayName,
                    Message = "The workspace closed before formatting completed."
                };
            }

            if (result.WasApplied
                && !string.Equals(document.Content, sourceContent, StringComparison.Ordinal))
            {
                result = new DocumentFormattingResult
                {
                    Outcome = DocumentFormattingOutcome.Rejected,
                    FormatterId = result.FormatterId,
                    FormatterDisplayName = result.FormatterDisplayName,
                    Message = "The document changed while the formatter was running; its stale result was discarded."
                };
            }

            if (result.Outcome == DocumentFormattingOutcome.Applied)
            {
                document.ApplyFormattedContent(result.FormattedText);
                ShowTransientStatus(result.Message);
            }
            else if (result.Outcome == DocumentFormattingOutcome.Unchanged)
            {
                ShowTransientStatus(result.Message);
            }
            else
            {
                ShowTransientStatus(
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "Using the in-editor formatting fallback."
                        : $"{result.Message} Using the in-editor fallback.");
            }

            return result;
        }
        finally
        {
            _documentFormatGate.Release();
        }
    }

    public async Task<CompiledLanguageExecutionResult> RunDocumentAsync(
        DocumentTabViewModel document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _documentExecutionGate.WaitAsync(cancellationToken);
        try
        {
            var sourceContent = document.Content;
            var workingDirectory = document.HasFilePath
                ? Path.GetDirectoryName(document.LocalWorkingFilePath)
                  ?? FileExplorer.CurrentPath
                : FileExplorer.CurrentPath;
            CompiledLanguageExecutionResult result;
            try
            {
                result = await _compiledDocumentExecutionService.ExecuteAsync(
                    new CompiledLanguageExecutionRequest
                    {
                        LanguageId = document.LanguageId,
                        FilePath = document.LocalWorkingFilePath,
                        Content = sourceContent,
                        WorkingDirectory = workingDirectory
                    },
                    _languageToolCatalog,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = new CompiledLanguageExecutionResult
                {
                    Outcome = CompiledLanguageExecutionOutcome.Rejected,
                    LanguageId = document.LanguageId,
                    WorkingDirectory = workingDirectory,
                    Message = $"Document execution failed safely: {exception.Message}"
                };
            }

            if (!string.Equals(
                    document.Content,
                    sourceContent,
                    StringComparison.Ordinal))
            {
                result = result with
                {
                    Warnings =
                    [
                        .. result.Warnings,
                        "The document changed while the build was running; this output reflects the earlier snapshot."
                    ]
                };
            }

            var documentShellKind = CommandLanguageRegistry.TryGet(
                document.LanguageId,
                out var documentLanguage)
                ? documentLanguage.PreferredShellKind
                : TerminalShellKind.External;
            var documentProfile = AvailableTerminalProfiles.FirstOrDefault(profile =>
                profile.ShellKind == documentShellKind);
            if (documentProfile is not null)
            {
                var terminal = ActiveTerminal?.ShellKind == documentShellKind
                    ? ActiveTerminal
                    : CreateTerminal(
                        CreateDefaultLaunchRequest(documentProfile),
                        runStartupCommand: false);
                if (terminal is not null)
                {
                    terminal.PresentCompiledDocumentExecution(
                        document.LocalWorkingFilePath,
                        result);
                    ActiveTerminal = terminal;
                }
            }

            ShowTransientStatus(result.Message);
            return result;
        }
        finally
        {
            _documentExecutionGate.Release();
        }
    }

    public string CreateOpenDocumentDialogFilter() =>
        _documentProviderRegistry.CreateOpenDialogFilter();

    public async Task<DocumentExportResult> ExportDocumentAsync(
        DocumentTabViewModel document,
        string formatId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _documentExportGate.WaitAsync(cancellationToken);
        try
        {
            var resolution = _documentProviderRegistry.Resolve(
                document.WorkspaceLocation?.Path
                ?? (document.HasFilePath ? document.FilePath : document.Title),
                document.LanguageId);
            document.ApplyDocumentProvider(resolution);
            var sourceContent = document.Content;
            DocumentExportArtifact artifact;
            try
            {
                artifact = await _documentExportService.CreateArtifactAsync(
                    resolution.Provider,
                    new DocumentExportRequest
                    {
                        Content = sourceContent,
                        LanguageId = document.LanguageId,
                        Title = document.Title,
                        FilePath = document.DocumentReference,
                        FormatId = formatId
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new DocumentExportResult
                {
                    Outcome = DocumentExportOutcome.Cancelled,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = formatId,
                    Message = "Document export was cancelled."
                };
            }
            catch (Exception exception)
            {
                var failed = new DocumentExportResult
                {
                    Outcome = exception is InvalidDataException
                        or InvalidOperationException
                        or NotSupportedException
                        ? DocumentExportOutcome.Rejected
                        : DocumentExportOutcome.Failed,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = formatId,
                    Message = $"Unable to create the export: {exception.Message}"
                };
                ShowTransientStatus(failed.Message);
                return failed;
            }

            if (_isDisposed)
            {
                return new DocumentExportResult
                {
                    Outcome = DocumentExportOutcome.Cancelled,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = formatId,
                    Message = "The workspace closed before export completed."
                };
            }

            if (!string.Equals(
                    document.Content,
                    sourceContent,
                    StringComparison.Ordinal))
            {
                var stale = new DocumentExportResult
                {
                    Outcome = DocumentExportOutcome.Rejected,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = formatId,
                    Message =
                        "The document changed while the export was being created; the stale artifact was discarded."
                };
                ShowTransientStatus(stale.Message);
                return stale;
            }

            try
            {
                var fullPath = await _documentExportService.WriteArtifactAsync(
                    artifact,
                    outputPath,
                    document.LocalWorkingFilePath,
                    cancellationToken);
                var result = new DocumentExportResult
                {
                    Outcome = DocumentExportOutcome.Exported,
                    OutputPath = fullPath,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = artifact.Format.Id,
                    Message = $"Exported {Path.GetFileName(fullPath)} with {resolution.Provider.Definition.DisplayName}."
                };
                ShowTransientStatus(result.Message);
                return result;
            }
            catch (OperationCanceledException)
            {
                return new DocumentExportResult
                {
                    Outcome = DocumentExportOutcome.Cancelled,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = formatId,
                    Message = "Document export was cancelled."
                };
            }
            catch (Exception exception)
            {
                var failed = new DocumentExportResult
                {
                    Outcome = exception is InvalidDataException
                        or InvalidOperationException
                        or NotSupportedException
                        ? DocumentExportOutcome.Rejected
                        : DocumentExportOutcome.Failed,
                    ProviderId = resolution.Provider.Definition.Id,
                    FormatId = formatId,
                    Message = $"Unable to write the export: {exception.Message}"
                };
                ShowTransientStatus(failed.Message);
                return failed;
            }
        }
        finally
        {
            _documentExportGate.Release();
        }
    }

    private void CreateTerminal()
    {
        CreateTerminal(CreateDefaultLaunchRequest(SelectedTerminalProfile));
    }

    public TerminalTabViewModel? CreateTerminal(TerminalLaunchRequest request)
    {
        return CreateTerminal(request, runStartupCommand: true);
    }

    public TerminalTabViewModel? CreateRemoteTerminal(
        RemoteTerminalLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Host.IsDisabled || !request.Host.HasCredential)
        {
            ShowTransientStatus(
                "Choose an enabled linked host with an SSH credential.");
            return null;
        }

        if (request.Credential.IsRevoked
            || !string.Equals(
                request.Host.CredentialId,
                request.Credential.Id,
                StringComparison.Ordinal))
        {
            ShowTransientStatus(
                "The linked host does not reference the selected active credential.");
            return null;
        }

        try
        {
            var shellKind = RemoteTerminalProtocol.ResolveShellKind(
                request.Host.OperatingSystem,
                request.ShellKind);
            var initialDirectory = string.IsNullOrWhiteSpace(
                request.InitialWorkingDirectory)
                ? string.IsNullOrWhiteSpace(request.Host.DefaultRoot)
                    ? request.Host.OperatingSystem ==
                      RemoteHostOperatingSystem.Windows
                        ? "C:/"
                        : "/"
                    : request.Host.DefaultRoot
                : request.InitialWorkingDirectory;
            initialDirectory = WorkspaceLocation.Ssh(
                request.Host.Id,
                initialDirectory).Path;

            var visualKind = shellKind == RemoteTerminalShellKind.Bash
                ? TerminalShellKind.WslBash
                : TerminalShellKind.PowerShell;
            var visualSource = AvailableTerminalProfiles.FirstOrDefault(
                                   profile => profile.ShellKind == visualKind)
                               ?? SelectedTerminalProfile;
            var profile = CreateEffectiveTerminalProfile(visualSource);
            var profileId = $"ssh:{request.Host.Id}:{shellKind}";
            profile.Id = profileId;
            profile.Name = $"{request.Host.DisplayName} {shellKind}";
            profile.ShellKind = visualKind;
            profile.Distribution = request.Host.DisplayName;
            profile.IsBuiltIn = false;
            profile.Settings.StartupDirectory = initialDirectory;

            var terminalNumber = _terminalNumbers.GetValueOrDefault(profileId) + 1;
            _terminalNumbers[profileId] = terminalNumber;
            var titleTemplate = string.IsNullOrWhiteSpace(request.Title)
                ? $"{request.Host.DisplayName} {{n}}"
                : request.Title.Trim();
            var title = ResolveTerminalTitle(
                titleTemplate,
                profile,
                terminalNumber);
            var shellLaunchCommand = string.IsNullOrWhiteSpace(
                request.ShellLaunchCommand)
                ? RemoteTerminalProtocol.GetDefaultShellCommand(shellKind)
                : request.ShellLaunchCommand.Trim();
            var session = new SshRemoteTerminalSession(
                request.Host,
                request.Credential,
                shellKind,
                shellLaunchCommand,
                _sshWorkspaceConnections);
            var terminal = new TerminalTabViewModel(
                title,
                profile,
                profile.Settings.Clone(),
                string.IsNullOrWhiteSpace(profile.TabColor)
                    ? "#28313D"
                    : profile.TabColor,
                request.StartupCommand,
                session,
                initialDirectory);
            InsertWorkspaceTab(terminal);
            ActiveTerminal = terminal;
            _ = InitializeRemoteTerminalAsync(terminal);
            return terminal;
        }
        catch (Exception exception)
        {
            ShowTransientStatus(
                $"Unable to create the remote terminal: {exception.Message}");
            return null;
        }
    }

    public bool CloseTerminal(TerminalTabViewModel terminal)
    {
        return CloseWorkspaceTab(terminal);
    }

    public bool SetWorkspaceTabLiveAssistance(
        IWorkspaceTab tab,
        bool enabled)
    {
        if (!WorkspaceTabs.Contains(tab))
        {
            return false;
        }

        var accepted = WorkspaceAiAssistant.SetLiveSourceEnabled(
            tab.AiLiveSourceId,
            enabled);
        tab.IsAiLiveAssistEnabled = accepted && enabled;
        if (!accepted)
        {
            ShowTransientStatus(WorkspaceAiAssistant.StatusMessage);
            return false;
        }

        if (enabled)
        {
            ActiveWorkspaceTab = tab;
        }

        ShowTransientStatus(enabled
            ? $"Live AI assistance enabled for {tab.Title}."
            : $"Live AI assistance disabled for {tab.Title}.");
        return true;
    }

    public bool CloseWorkspaceTab(IWorkspaceTab tab)
    {
        var closingIndex = WorkspaceTabs.IndexOf(tab);
        if (closingIndex < 0)
        {
            return false;
        }

        var wasActive = ReferenceEquals(ActiveWorkspaceTab, tab);
        var replacement = wasActive
            ? closingIndex + 1 < WorkspaceTabs.Count
                ? WorkspaceTabs[closingIndex + 1]
                : closingIndex > 0
                    ? WorkspaceTabs[closingIndex - 1]
                    : null
            : ActiveWorkspaceTab;
        var title = tab.Title;
        var stoppedRunningCommand = tab is TerminalTabViewModel
        {
            IsRunning: true
        };

        foreach (var conversation in WorkspaceAiConversations)
        {
            conversation.Assistant.SetLiveSourceEnabled(
                tab.AiLiveSourceId,
                enabled: false);
        }
        tab.IsAiLiveAssistEnabled = false;
        WorkspaceTabs.RemoveAt(closingIndex);
        ActiveWorkspaceTab = replacement;
        if (tab is DocumentTabViewModel document)
        {
            document.PropertyChanged -= OnDocumentPropertyChanged;
            _editorSessionService.DeleteRecovery(document.SessionId);
            ScheduleEditorSessionSave();
        }
        else if (tab is TerminalTabViewModel terminal)
        {
            terminal.PropertyChanged -= OnTerminalPropertyChanged;
        }
        tab.Dispose();
        ShowTransientStatus(stoppedRunningCommand
            ? $"{title} closed; its running command was stopped."
            : $"{title} closed.");
        return true;
    }

    public string GetNextUntitledDocumentBaseName()
    {
        var nextNumber = _untitledDocumentNumber + 1;
        return nextNumber == 1
            ? "Untitled"
            : $"Untitled-{nextNumber}";
    }

    public DocumentTabViewModel CreateUntitledDocument(
        NewDocumentRequest request)
    {
        _untitledDocumentNumber++;
        var document = DocumentTabViewModel.CreateUntitled(
            request,
            _textDocumentService,
            _workspaceDocumentService);
        BindDocumentProvider(document);
        InsertWorkspaceTab(document);
        ActiveWorkspaceTab = document;
        ShowTransientStatus($"{document.Title} created as {document.LanguageId}.");
        return document;
    }

    public BrowserTabViewModel CreateBrowser(
        string? initialAddress = null)
    {
        var browser = new BrowserTabViewModel(initialAddress);
        InsertWorkspaceTab(browser);
        ActiveWorkspaceTab = browser;
        ShowTransientStatus("Browser tab opened.");
        return browser;
    }

    private void OnAtlasMyNetOpenAppRequested(
        AtlasMyNetOpenAppRequest request)
    {
        var policy = new BrowserNavigationPolicy(
            request.Definition.Id,
            request.Definition.DisplayName,
            request.Url,
            request.Definition.AllowExternalResources);
        var browser = new BrowserTabViewModel(
            request.Url,
            navigationPolicy: policy,
            showNavigationToolbar: request.Definition.ShowNavigationToolbar);
        InsertWorkspaceTab(browser);
        ActiveWorkspaceTab = browser;
        ShowTransientStatus(
            $"{request.Definition.DisplayName} opened in a managed local tab.");
    }

    private void OnAtlasMyNetNetworkClientRequested(
        NetworkClientLaunchRequest request)
    {
        var network = new NetworkClientTabViewModel(request);
        InsertWorkspaceTab(network);
        ActiveWorkspaceTab = network;
        ShowTransientStatus(
            $"{request.Transport.ToString().ToUpperInvariant()} client opened in a new tab.");
    }

    public BrowserTabViewModel? OpenWebPreview(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var extension = Path.GetExtension(fullPath);
            if (!File.Exists(fullPath)
                || (!string.Equals(
                        extension,
                        ".html",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        extension,
                        ".htm",
                        StringComparison.OrdinalIgnoreCase)))
            {
                ShowTransientStatus(
                    "Save the HTML document as an .html or .htm file before previewing it.");
                return null;
            }

            var existing = WorkspaceTabs
                .OfType<BrowserTabViewModel>()
                .FirstOrDefault(browser => string.Equals(
                    browser.PreviewSourcePath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.RefreshLocalPreview();
                ActiveWorkspaceTab = existing;
                ShowTransientStatus($"Refreshed preview for {Path.GetFileName(fullPath)}.");
                return existing;
            }

            var preview = new BrowserTabViewModel(fullPath, fullPath);
            InsertWorkspaceTab(preview);
            ActiveWorkspaceTab = preview;
            ShowTransientStatus($"Previewing {Path.GetFileName(fullPath)} in Hypershell Browser.");
            return preview;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            ShowTransientStatus($"Unable to preview the web page: {exception.Message}");
            return null;
        }
    }

    public PdfDocumentTabViewModel? OpenPdfDocument(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var existing = WorkspaceTabs
                .OfType<PdfDocumentTabViewModel>()
                .FirstOrDefault(document => string.Equals(
                    document.FilePath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                ActiveWorkspaceTab = existing;
                return existing;
            }

            var document = new PdfDocumentTabViewModel(fullPath);
            InsertWorkspaceTab(document);
            ActiveWorkspaceTab = document;
            ShowTransientStatus($"{document.Title} opened in the PDF viewer.");
            return document;
        }
        catch (Exception exception)
        {
            ShowTransientStatus($"Unable to open PDF: {exception.Message}");
            return null;
        }
    }

    public async Task<PdfDocumentTabViewModel?> OpenPdfDocumentAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            var existing = WorkspaceTabs
                .OfType<PdfDocumentTabViewModel>()
                .FirstOrDefault(document => string.Equals(
                    document.WorkspaceUri,
                    location.Identity,
                    location.IsLocal
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal));
            if (existing is not null)
            {
                ActiveWorkspaceTab = existing;
                return existing;
            }

            var cached = await _workspaceDocumentService.CacheFileAsync(
                location,
                WorkspaceDocumentService.MaximumCachedBinaryBytes,
                cancellationToken);
            var document = new PdfDocumentTabViewModel(
                cached.LocalPath,
                cached.Location);
            InsertWorkspaceTab(document);
            ActiveWorkspaceTab = document;
            ShowTransientStatus(
                $"{document.Title} opened in the PDF viewer" +
                (cached.Location.IsSsh ? " from the linked device." : "."));
            return document;
        }
        catch (Exception exception)
        {
            ShowTransientStatus($"Unable to open PDF: {exception.Message}");
            return null;
        }
    }

    public async Task<DocumentTabViewModel?> OpenDocumentAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        await OpenDocumentAsync(
            WorkspaceLocation.Local(path),
            cancellationToken);

    public async Task<DocumentTabViewModel?> OpenDocumentAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(location);
            var existing = WorkspaceTabs
                .OfType<DocumentTabViewModel>()
                .FirstOrDefault(document => string.Equals(
                    document.WorkspaceUri,
                    location.Identity,
                    location.IsLocal
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal));
            if (existing is not null)
            {
                ActiveWorkspaceTab = existing;
                return existing;
            }

            var provider = _documentProviderRegistry.Resolve(location.Path);
            var document = await DocumentTabViewModel.OpenAsync(
                location,
                _textDocumentService,
                _workspaceDocumentService,
                cancellationToken,
                provider.LanguageId);
            document.ApplyDocumentProvider(provider);
            InsertWorkspaceTab(document);
            ActiveWorkspaceTab = document;
            ShowTransientStatus($"{document.Title} opened.");
            return document;
        }
        catch (Exception exception)
        {
            ShowTransientStatus($"Unable to open document: {exception.Message}");
            return null;
        }
    }

    public void ChangeDocumentLanguage(
        DocumentTabViewModel document,
        string languageId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(languageId)
            || string.Equals(
                document.LanguageId,
                languageId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        document.ApplyDocumentProvider(
            _documentProviderRegistry.ResolveByLanguage(languageId));
        ShowTransientStatus(
            $"{document.Title} is now using {EditorLanguageCatalog.GetLanguage(languageId).DisplayName} editing.");
    }

    public async Task<bool> SaveDocumentAsync(
        DocumentTabViewModel document,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await document.SaveAsync(path, cancellationToken);
            BindDocumentProvider(document);
            _editorSessionService.DeleteRecovery(document.SessionId);
            ScheduleEditorSessionSave();
            await PersistEditorSessionAsync(cancellationToken: cancellationToken);
            ShowTransientStatus($"{document.Title} saved.");
            return true;
        }
        catch (Exception exception)
        {
            ShowTransientStatus($"Unable to save {document.Title}: {exception.Message}");
            return false;
        }
    }

    public async Task<WorkspaceDocumentSaveResult> SaveRemoteDocumentAsync(
        DocumentTabViewModel document,
        bool overwriteWithoutVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            await document.SaveRemoteAsync(
                overwriteWithoutVersion,
                cancellationToken);
            BindDocumentProvider(document);
            _editorSessionService.DeleteRecovery(document.SessionId);
            ScheduleEditorSessionSave();
            await PersistEditorSessionAsync(cancellationToken: cancellationToken);
            ShowTransientStatus($"{document.Title} saved to its linked device.");
            return new WorkspaceDocumentSaveResult(
                WorkspaceDocumentSaveOutcome.Saved,
                $"{document.Title} was saved to its linked device.");
        }
        catch (WorkspaceFileSystemException exception)
            when (exception.Kind == WorkspaceFailureKind.Conflict)
        {
            ShowTransientStatus(exception.Message);
            return new WorkspaceDocumentSaveResult(
                WorkspaceDocumentSaveOutcome.Conflict,
                exception.Message);
        }
        catch (Exception exception)
        {
            ShowTransientStatus(
                $"Unable to save {document.Title}: {exception.Message}");
            return new WorkspaceDocumentSaveResult(
                WorkspaceDocumentSaveOutcome.Failed,
                exception.Message);
        }
    }

    public async Task<bool> ReloadRemoteDocumentAsync(
        DocumentTabViewModel document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            await document.ReloadRemoteAsync(cancellationToken);
            _editorSessionService.DeleteRecovery(document.SessionId);
            ScheduleEditorSessionSave();
            await PersistEditorSessionAsync(cancellationToken: cancellationToken);
            ShowTransientStatus($"{document.Title} reloaded from its linked device.");
            return true;
        }
        catch (Exception exception)
        {
            ShowTransientStatus(
                $"Unable to reload {document.Title}: {exception.Message}");
            return false;
        }
    }

    private TerminalTabViewModel? CreateTerminal(
        TerminalLaunchRequest request,
        bool runStartupCommand,
        string? workingDirectoryOverride = null)
    {
        var profile = request.Profile;
        if (!profile.IsAvailable)
        {
            ShowTransientStatus($"{profile.Name} is not available on this computer.");
            return null;
        }

        var terminalNumber = _terminalNumbers.GetValueOrDefault(profile.Id) + 1;
        _terminalNumbers[profile.Id] = terminalNumber;
        try
        {
            var settings = profile.Settings.Clone();
            if (_runtimeExplorerSettings.UseAsTerminalStartupDirectory
                && Directory.Exists(_runtimeExplorerSettings.StartupDirectory))
            {
                settings.StartupDirectory = _runtimeExplorerSettings.StartupDirectory;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectoryOverride)
                && Directory.Exists(workingDirectoryOverride))
            {
                settings.StartupDirectory = Path.GetFullPath(
                    workingDirectoryOverride);
            }

            if (!string.IsNullOrWhiteSpace(request.BackgroundColor))
            {
                settings.BackgroundColor = request.BackgroundColor;
            }

            if (!string.IsNullOrWhiteSpace(request.FontFamilyName))
            {
                settings.FontFamilyName = request.FontFamilyName;
            }

            var managedBackgroundPath = string.IsNullOrWhiteSpace(request.BackgroundImagePath)
                ? string.Empty
                : _settingsService.ImportTerminalBackground(request.BackgroundImagePath);
            settings.BackgroundImagePath = managedBackgroundPath;

            var titleTemplate = string.IsNullOrWhiteSpace(request.TitleTemplate)
                ? profile.TabTitleTemplate
                : request.TitleTemplate.Trim();
            if (string.IsNullOrWhiteSpace(titleTemplate))
            {
                titleTemplate = $"{profile.Name} {{n}}";
            }

            var tabColor = string.IsNullOrWhiteSpace(request.TabColor)
                ? profile.TabColor
                : request.TabColor;
            if (string.IsNullOrWhiteSpace(tabColor))
            {
                tabColor = "#28313D";
            }

            if (request.SaveAsProfileDefaults)
            {
                ApplyProfileDefaults(
                    profile,
                    titleTemplate,
                    tabColor,
                    settings.BackgroundColor,
                    managedBackgroundPath,
                    settings.FontFamilyName,
                    request.StartupCommand);
            }

            var title = ResolveTerminalTitle(titleTemplate, profile, terminalNumber);
            var terminal = new TerminalTabViewModel(
                title,
                profile,
                settings,
                tabColor,
                request.StartupCommand);
            InsertWorkspaceTab(terminal);

            ActiveTerminal = terminal;
            if (runStartupCommand)
            {
                _ = terminal.RunStartupCommandAsync();
            }

            return terminal;
        }
        catch (Exception exception)
        {
            ShowTransientStatus($"Unable to start {profile.Name}: {exception.Message}");
            return null;
        }
    }

    private async Task InitializeRemoteTerminalAsync(
        TerminalTabViewModel terminal)
    {
        await terminal.ConnectRemoteAsync();
        if (!terminal.IsRemoteConnected)
        {
            ShowTransientStatus(terminal.RemoteConnectionDiagnostic);
            return;
        }

        ShowTransientStatus(
            $"Connected to {terminal.RemoteHostDisplayName}.");
        await terminal.RunStartupCommandAsync();
    }

    private void InsertWorkspaceTab(IWorkspaceTab tab)
    {
        if (tab is DocumentTabViewModel document)
        {
            document.PropertyChanged += OnDocumentPropertyChanged;
            ScheduleEditorSessionSave();
        }
        else if (tab is TerminalTabViewModel terminal)
        {
            terminal.PropertyChanged += OnTerminalPropertyChanged;
        }

        if (_isRestoringEditorSession)
        {
            WorkspaceTabs.Add(tab);
            return;
        }

        if (_runtimeApplicationPreferences.NewTabPlacement ==
            NewTabPlacement.AfterCurrent
            && ActiveWorkspaceTab is not null
            && WorkspaceTabs.IndexOf(ActiveWorkspaceTab) is var activeIndex
            && activeIndex >= 0)
        {
            WorkspaceTabs.Insert(activeIndex + 1, tab);
            return;
        }

        WorkspaceTabs.Add(tab);
    }

    public bool UpdateTerminal(
        TerminalTabViewModel terminal,
        TerminalLaunchRequest request)
    {
        if (!ReferenceEquals(terminal.Profile, request.Profile))
        {
            ShowTransientStatus("A running tab cannot switch to a different shell.");
            return false;
        }

        try
        {
            var managedBackgroundPath = string.IsNullOrWhiteSpace(request.BackgroundImagePath)
                ? string.Empty
                : _settingsService.ImportTerminalBackground(request.BackgroundImagePath);
            terminal.ApplyConfiguration(
                request.TitleTemplate,
                request.TabColor,
                request.BackgroundColor,
                managedBackgroundPath,
                request.FontFamilyName,
                request.StartupCommand);

            if (request.SaveAsProfileDefaults)
            {
                ApplyProfileDefaults(
                    terminal.Profile,
                    terminal.Title,
                    request.TabColor,
                    request.BackgroundColor,
                    managedBackgroundPath,
                    request.FontFamilyName,
                    request.StartupCommand);
            }

            ShowTransientStatus($"{terminal.Title} settings updated.");
            return true;
        }
        catch (Exception exception)
        {
            ShowTransientStatus($"Unable to update {terminal.Title}: {exception.Message}");
            return false;
        }
    }

    private async Task DispatchBuilderCommandAsync(CommandExecutionRequest request)
    {
        var terminal = ActiveTerminal;
        var requestedShellKind = request.ExecutionTarget == ExecutionTarget.CSharp
            ? TerminalShellKind.CSharp
            : request.PreferredShellKind;
        var activeTerminalCanRunRequest = IsCompatibleBuilderTerminal(
            terminal?.ShellKind,
            requestedShellKind);
        if (!activeTerminalCanRunRequest)
        {
            var compatibleProfile = AvailableTerminalProfiles.FirstOrDefault(profile =>
                profile.IsAvailable
                && profile.ShellKind == requestedShellKind);
            if (compatibleProfile is not null)
            {
                terminal = CreateTerminal(
                    CreateDefaultLaunchRequest(compatibleProfile),
                    runStartupCommand: false);
                if (terminal is not null)
                {
                    await terminal.RunStartupCommandAsync();
                }
            }
            else
            {
                ShowTransientStatus(GetUnavailableBuilderRuntimeMessage(
                    requestedShellKind));
                return;
            }
        }

        if (terminal is null)
        {
            return;
        }

        if (request.ExecuteImmediately && terminal.IsRunning)
        {
            ShowTransientStatus("Wait for the active terminal command to finish, then run the pipeline again.");
            return;
        }

        ActiveTerminal = terminal;
        if (!request.ExecuteImmediately)
        {
            terminal.InputText = request.CommandText;
            ShowTransientStatus(
                $"Command builder statement sent to the " +
                $"{CommandLanguageRegistry.GetRuntimeDisplayName(requestedShellKind)} terminal.");
            return;
        }

        await terminal.ExecuteTextAsync(request.CommandText);
        ShowTransientStatus(
            $"Command builder statement executed in the " +
            $"{CommandLanguageRegistry.GetRuntimeDisplayName(requestedShellKind)} terminal.");
    }

    private static bool IsCompatibleBuilderTerminal(
        TerminalShellKind? activeShellKind,
        TerminalShellKind requestedShellKind) =>
        requestedShellKind switch
        {
            TerminalShellKind.PowerShell => activeShellKind is
                TerminalShellKind.PowerShell
                or TerminalShellKind.DeveloperPowerShell
                or TerminalShellKind.WindowsPowerShell,
            _ => activeShellKind == requestedShellKind
        };

    private static string GetUnavailableBuilderRuntimeMessage(
        TerminalShellKind shellKind) =>
        shellKind switch
        {
            TerminalShellKind.CSharp =>
                "C# execution requires a locally installed .NET SDK.",
            TerminalShellKind.WindowsPowerShell =>
                "Windows PowerShell 5.1 is not available for this command.",
            TerminalShellKind.Python =>
                "Python is not installed or available to Hypershell.",
            TerminalShellKind.NodeJs =>
                "Node.js is not installed or available to Hypershell.",
            TerminalShellKind.TypeScript =>
                "TypeScript execution requires Node.js 22.13 or newer with native type stripping.",
            TerminalShellKind.Java =>
                "Java execution requires a locally installed JDK 17 or newer.",
            TerminalShellKind.Cpp =>
                "C++ execution requires Clang, GCC, or an active Visual Studio developer environment.",
            _ =>
                $"{CommandLanguageRegistry.GetRuntimeDisplayName(shellKind)} " +
                "is not available for this command."
        };

    private TerminalLaunchRequest CreateDefaultLaunchRequest(TerminalProfile profile)
    {
        var effectiveProfile = CreateEffectiveTerminalProfile(profile);
        return new TerminalLaunchRequest
        {
            Profile = effectiveProfile,
            TitleTemplate = effectiveProfile.TabTitleTemplate,
            TabColor = effectiveProfile.TabColor,
            BackgroundColor = effectiveProfile.Settings.BackgroundColor,
            BackgroundImagePath = effectiveProfile.Settings.BackgroundImagePath,
            FontFamilyName = effectiveProfile.Settings.FontFamilyName,
            StartupCommand = effectiveProfile.StartupCommand
        };
    }

    private TerminalProfile CreateEffectiveTerminalProfile(TerminalProfile profile)
    {
        var effectiveProfile = profile.Clone();
        var configuration = _runtimeTerminalConfigurationProfiles.LastOrDefault(candidate =>
            candidate.TargetTerminalProfileIds.Contains(profile.Id, StringComparer.OrdinalIgnoreCase));
        if (configuration is null)
        {
            return effectiveProfile;
        }

        effectiveProfile.Settings.CopyFrom(configuration.Settings);
        effectiveProfile.TabTitleTemplate = configuration.TabTitleTemplate;
        effectiveProfile.TabColor = configuration.TabColor;
        effectiveProfile.StartupCommand = configuration.StartupCommand;
        return effectiveProfile;
    }

    private void ApplyProfileDefaults(
        TerminalProfile profile,
        string titleTemplate,
        string tabColor,
        string backgroundColor,
        string backgroundImagePath,
        string fontFamilyName,
        string startupCommand)
    {
        profile.TabTitleTemplate = titleTemplate;
        profile.TabColor = tabColor;
        profile.StartupCommand = startupCommand.Trim();
        profile.Settings.BackgroundColor = backgroundColor;
        profile.Settings.BackgroundImagePath = backgroundImagePath;
        profile.Settings.FontFamilyName = fontFamilyName;
        ScheduleAutosave();
    }

    internal void ApplyApplicationPreferences(
        ApplicationPreferences preferences,
        string defaultTerminalProfileId,
        bool persist)
    {
        _runtimeApplicationPreferences.CopyFrom(preferences);
        IsFileExplorerOpen = preferences.OpenFileExplorerOnStartup;
        IsCommandBuilderOpen = preferences.OpenCommandBuilderOnStartup;
        OnPropertyChanged(nameof(ShowSaveStatusInTitleBar));

        var defaultProfile = AvailableTerminalProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, defaultTerminalProfileId, StringComparison.OrdinalIgnoreCase));
        if (defaultProfile is not null && !ReferenceEquals(_selectedTerminalProfile, defaultProfile))
        {
            _selectedTerminalProfile = defaultProfile;
            OnPropertyChanged(nameof(SelectedTerminalProfile));
            OnPropertyChanged(nameof(TerminalSettings));
            OnPropertyChanged(nameof(SelectedTerminalProfileStatus));
        }

        if (!persist)
        {
            ShowTransientStatus("Applied general settings to this session.");
            return;
        }

        _settings.Application.CopyFrom(preferences);
        if (defaultProfile is not null)
        {
            _settings.ActiveTerminalProfileId = defaultProfile.Id;
        }

        SaveSettingsImmediately(
            "General settings saved.",
            portableChange: true);
    }

    internal void ApplyTerminalConfigurationProfiles(
        IReadOnlyList<TerminalConfigurationProfile> configurations,
        string selectedConfigurationId,
        bool persist)
    {
        var normalizedConfigurations = configurations
            .OrderBy(configuration => string.Equals(
                configuration.Id,
                selectedConfigurationId,
                StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .Select(configuration => configuration.Clone())
            .ToList();
        _runtimeTerminalConfigurationProfiles = normalizedConfigurations
            .Select(configuration => configuration.Clone())
            .ToList();

        foreach (var terminal in Terminals)
        {
            var configuration = normalizedConfigurations.LastOrDefault(candidate =>
                candidate.TargetTerminalProfileIds.Contains(
                    terminal.Profile.Id,
                    StringComparer.OrdinalIgnoreCase));
            if (configuration is not null)
            {
                terminal.ApplyProfileConfiguration(configuration);
            }
        }

        if (!persist)
        {
            ShowTransientStatus("Applied terminal profiles to matching open tabs.");
            return;
        }

        foreach (var configuration in normalizedConfigurations)
        {
            if (!string.IsNullOrWhiteSpace(configuration.Settings.BackgroundImagePath))
            {
                configuration.Settings.BackgroundImagePath =
                    _settingsService.ImportTerminalBackground(configuration.Settings.BackgroundImagePath);
            }
        }
        _runtimeTerminalConfigurationProfiles = normalizedConfigurations
            .Select(configuration => configuration.Clone())
            .ToList();

        _settings.TerminalConfigurationProfiles = new ObservableCollection<TerminalConfigurationProfile>(
            normalizedConfigurations.Select(configuration => configuration.Clone()));
        foreach (var terminalProfile in _settings.TerminalProfiles)
        {
            var configuration = normalizedConfigurations.LastOrDefault(candidate =>
                candidate.TargetTerminalProfileIds.Contains(
                    terminalProfile.Id,
                    StringComparer.OrdinalIgnoreCase));
            if (configuration is null)
            {
                continue;
            }

            terminalProfile.Settings.CopyFrom(configuration.Settings);
            terminalProfile.TabTitleTemplate = configuration.TabTitleTemplate;
            terminalProfile.TabColor = configuration.TabColor;
            terminalProfile.StartupCommand = configuration.StartupCommand.Trim();
        }

        _settings.Terminal = _settings.TerminalProfiles
            .FirstOrDefault(profile => string.Equals(profile.Id, "powershell", StringComparison.OrdinalIgnoreCase))
            ?.Settings
            ?? _settings.Terminal;
        RefreshObservedTerminalSettings();
        SaveSettingsImmediately(
            "Terminal profiles saved.",
            portableChange: true);
    }

    internal void ApplyExplorerSettings(ExplorerSettings settings, bool persist)
    {
        _runtimeExplorerSettings.CopyFrom(settings);
        FileExplorer.ApplySettings(_runtimeExplorerSettings);
        WorkItems.UpdateProjectScopes(
            _runtimeExplorerSettings.Projects,
            _runtimeExplorerSettings.ActiveProjectId,
            FileExplorer.CurrentPath);
        OnPropertyChanged(nameof(ExtensionPanelWorkspaceContext));
        if (!persist)
        {
            ShowTransientStatus("Applied File System Explorer settings to this session.");
            return;
        }

        _settings.Explorer.CopyFrom(settings);
        SaveSettingsImmediately(
            "File System Explorer settings saved.",
            portableChange: true);
    }

    public void CreateExplorerProject(ProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var name = project.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Give the project a name.",
                nameof(project));
        }

        var directories = project.Directories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeProjectLocation)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (directories.Count == 0)
        {
            throw new ArgumentException(
                "Add at least one available project folder.",
                nameof(project));
        }

        if (_runtimeExplorerSettings.Projects.Any(existing =>
                string.Equals(
                    existing.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"A project named '{name}' already exists.");
        }

        var normalized = new ProjectDefinition
        {
            Id = string.IsNullOrWhiteSpace(project.Id)
                ? Guid.NewGuid().ToString("N")
                : project.Id,
            Name = name,
            Directories = new ObservableCollection<string>(directories)
        };
        _runtimeExplorerSettings.Projects.Add(normalized);
        _runtimeExplorerSettings.ActiveProjectId = normalized.Id;
        FileExplorer.ApplySettings(_runtimeExplorerSettings);
        WorkItems.UpdateProjectScopes(
            _runtimeExplorerSettings.Projects,
            normalized.Id,
            FileExplorer.CurrentPath);
        _settings.Explorer.CopyFrom(_runtimeExplorerSettings);
        SaveSettingsImmediately(
            $"Created project {normalized.Name}.",
            portableChange: true);
    }

    public bool RemoveExplorerProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return false;
        }

        var project = _runtimeExplorerSettings.Projects.FirstOrDefault(
            candidate => string.Equals(
                candidate.Id,
                projectId,
                StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            return false;
        }

        _runtimeExplorerSettings.Projects.Remove(project);
        if (string.Equals(
                _runtimeExplorerSettings.ActiveProjectId,
                project.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            _runtimeExplorerSettings.ActiveProjectId = string.Empty;
        }

        FileExplorer.ApplySettings(_runtimeExplorerSettings);
        WorkItems.UpdateProjectScopes(
            _runtimeExplorerSettings.Projects,
            _runtimeExplorerSettings.ActiveProjectId,
            FileExplorer.CurrentPath);
        OnPropertyChanged(nameof(ExtensionPanelWorkspaceContext));
        _settings.Explorer.CopyFrom(_runtimeExplorerSettings);
        SaveSettingsImmediately(
            $"Removed {project.Name} from Explorer. Project files were not changed.",
            portableChange: true);
        return true;
    }

    private static string? NormalizeProjectLocation(string value)
    {
        if (WorkspaceLocation.TryParse(value, out var location))
        {
            return location.IsSsh || Directory.Exists(location.Path)
                ? location.IsLocal ? location.Path : location.Identity
                : null;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    internal void ApplyWorkspaceLayout(
        WorkspaceLayoutSnapshot layout,
        bool persist)
    {
        _runtimeWorkspaceLayout = WorkspaceLayoutService.Normalize(
            layout,
            _workspacePanelRegistry);
        RefreshWorkspaceSideTools();
        OnPropertyChanged(nameof(WorkspaceLayout));
        if (!persist)
        {
            ShowTransientStatus(
                "Applied workspace layout to this session.");
            return;
        }

        _settings.WorkspaceLayout = _runtimeWorkspaceLayout.Clone();
        SaveSettingsImmediately(
            "Workspace layout saved.",
            portableChange: true);
    }

    internal bool PreviewExtensionPanel(
        DiscoveredExtensionPackage package,
        BuiltInWorkspaceSurface? replacement,
        WorkspacePanelSlot slot,
        out string statusMessage)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!_extensionApprovalService.IsWebPanelApproved(package))
        {
            statusMessage =
                "Approve this package's local web panels before previewing them.";
            return false;
        }

        var manifestId = package.Manifest?.Id ?? string.Empty;
        var candidate = _extensionCatalog.PanelCandidates.FirstOrDefault(item =>
            string.Equals(
                item.Package.Manifest?.Id,
                manifestId,
                StringComparison.OrdinalIgnoreCase)
            && (slot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right
                ? true
                : replacement is { } surface
                ? item.Registration.Contribution.Placement.MayReplace.Contains(surface)
                : item.Registration.Contribution.Placement.AllowedSlots.Contains(slot)));
        if (candidate is null)
        {
            statusMessage = replacement is not null
                ? "This package does not declare a panel that may replace that built-in surface."
                : $"This package does not declare a panel that supports the {slot} host.";
            return false;
        }

        return PlaceExtensionPanel(
            candidate,
            replacement,
            slot,
            actionVerb: "Previewing",
            requireUserMove: false,
            out statusMessage);
    }

    internal bool CanMoveExtensionPanel(
        string panelId,
        BuiltInWorkspaceSurface? replacement,
        WorkspacePanelSlot slot,
        out string reason)
    {
        if (!TryGetApprovedPanelCandidate(panelId, out var candidate)
            || candidate is null)
        {
            reason = "The panel is not approved on this device.";
            return false;
        }

        if (!candidate.Registration.Contribution.Placement.AllowUserMove)
        {
            reason = "This panel's manifest does not allow user movement.";
            return false;
        }

        return TryValidateExtensionPanelPlacement(
            candidate,
            replacement,
            ref slot,
            out reason);
    }

    internal bool MoveExtensionPanel(
        string panelId,
        BuiltInWorkspaceSurface? replacement,
        WorkspacePanelSlot slot,
        out string statusMessage)
    {
        if (!TryGetApprovedPanelCandidate(panelId, out var candidate)
            || candidate is null)
        {
            statusMessage = "The panel is no longer approved on this device.";
            return false;
        }

        return PlaceExtensionPanel(
            candidate,
            replacement,
            slot,
            actionVerb: "Moved",
            requireUserMove: true,
            out statusMessage);
    }

    internal void SaveCurrentWorkspaceLayout()
    {
        _settings.WorkspaceLayout = _runtimeWorkspaceLayout.Clone();
        SaveSettingsImmediately(
            "Workspace layout saved.",
            portableChange: true);
    }

    private bool PlaceExtensionPanel(
        ExtensionPanelCandidate candidate,
        BuiltInWorkspaceSurface? replacement,
        WorkspacePanelSlot slot,
        string actionVerb,
        bool requireUserMove,
        out string statusMessage)
    {
        if (requireUserMove
            && !candidate.Registration.Contribution.Placement.AllowUserMove)
        {
            statusMessage = "This panel's manifest does not allow user movement.";
            return false;
        }

        if (!TryValidateExtensionPanelPlacement(
                candidate,
                replacement,
                ref slot,
                out statusMessage))
        {
            return false;
        }

        var manifestId = candidate.Package.Manifest?.Id ?? string.Empty;
        var panels = _runtimeWorkspaceLayout.Panels
            .Select(panel => panel.Clone())
            .ToList();
        var displacedPanelNames = new List<string>();
        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var conflictsWithTarget = replacement is not null
                                      && panel.Replaces == replacement
                                      && !string.Equals(
                                          panel.PanelId,
                                          candidate.PanelId,
                                          StringComparison.OrdinalIgnoreCase);
            var conflictsWithBottom = replacement is null
                                      && slot == WorkspacePanelSlot.Bottom
                                      && panel.Replaces is null
                                      && panel.Slot == WorkspacePanelSlot.Bottom
                                      && !string.Equals(
                                          panel.PanelId,
                                          candidate.PanelId,
                                          StringComparison.OrdinalIgnoreCase);
            if (conflictsWithTarget || conflictsWithBottom)
            {
                if (panel.IsVisible)
                {
                    var displacedRegistration = _workspacePanelRegistry
                        .Registrations
                        .FirstOrDefault(registration => string.Equals(
                            registration.Contribution.Id,
                            panel.PanelId,
                            StringComparison.OrdinalIgnoreCase));
                    displacedPanelNames.Add(
                        displacedRegistration?.Contribution.DisplayName
                        ?? panel.PanelId);
                }

                panels[index] = CopyPanelPlacement(
                    panel,
                    isVisible: false,
                    replacement: null);
            }
        }

        var existingIndex = panels.FindIndex(panel => string.Equals(
            panel.PanelId,
            candidate.PanelId,
            StringComparison.OrdinalIgnoreCase));
        var existing = existingIndex >= 0 ? panels[existingIndex] : null;
        var placement = candidate.Registration.Contribution.Placement;
        var activated = new PanelPlacementSnapshot
        {
            ExtensionId = manifestId,
            PanelId = candidate.PanelId,
            Slot = slot,
            Replaces = replacement,
            Order = existing?.Order ?? panels.Count,
            Width = existing?.Width is > 0
                ? existing.Width
                : placement.PreferredWidth,
            Height = existing?.Height is > 0
                ? existing.Height
                : placement.PreferredHeight,
            IsVisible = true
        };
        if (existingIndex >= 0)
        {
            panels[existingIndex] = activated;
        }
        else
        {
            panels.Add(activated);
        }

        ApplyWorkspaceLayout(
            new WorkspaceLayoutSnapshot
            {
                IsUserUnlocked = true,
                SideTools = _runtimeWorkspaceLayout.SideTools
                    .Select(tool => tool.Clone())
                    .ToList(),
                Panels = panels
            },
            persist: false);
        statusMessage = replacement is { }
            ? $"{actionVerb} {candidate.Registration.Contribution.DisplayName} in place of {replacement}. Save Workspace Layout to keep it."
            : $"{actionVerb} {candidate.Registration.Contribution.DisplayName} to the {slot} host. Save Workspace Layout to keep it.";
        if (displacedPanelNames.Count > 0)
        {
            statusMessage +=
                $" Hidden conflicting panel: {string.Join(", ", displacedPanelNames)}.";
        }

        return true;
    }

    private bool TryValidateExtensionPanelPlacement(
        ExtensionPanelCandidate candidate,
        BuiltInWorkspaceSurface? replacement,
        ref WorkspacePanelSlot slot,
        out string statusMessage)
    {
        if (slot == WorkspacePanelSlot.Floating
            && !candidate.Registration.Contribution.Placement.AllowFloating)
        {
            statusMessage = "This panel does not allow floating presentation.";
            return false;
        }

        if (replacement is { } targetSurface)
        {
            if (!candidate.Registration.Contribution.Placement.MayReplace
                    .Contains(targetSurface))
            {
                statusMessage =
                    $"This panel's manifest does not allow it to replace {targetSurface}.";
                return false;
            }

            var targetPanelId = targetSurface switch
            {
                BuiltInWorkspaceSurface.FileExplorer =>
                    WorkspacePanelRegistry.FileExplorerPanelId,
                BuiltInWorkspaceSurface.CommandBuilder =>
                    WorkspacePanelRegistry.CommandBuilderPanelId,
                _ => string.Empty
            };
            var targetPlacement = WorkspaceLayoutService.GetRequiredPanel(
                _runtimeWorkspaceLayout,
                targetPanelId);
            if (!candidate.Registration.Contribution.Placement.AllowedSlots
                    .Contains(targetPlacement.Slot))
            {
                statusMessage =
                    $"The panel does not allow the target surface's current {targetPlacement.Slot} position.";
                return false;
            }

            slot = targetPlacement.Slot;
        }
        else if (slot is not WorkspacePanelSlot.Left
                 and not WorkspacePanelSlot.Right
                 && !candidate.Registration.Contribution.Placement.AllowedSlots
                      .Contains(slot))
        {
            statusMessage =
                $"This panel's manifest does not allow the {slot} host.";
            return false;
        }

        statusMessage = string.Empty;
        return true;
    }

    internal void HideExtensionPanel(string panelId)
    {
        var panels = _runtimeWorkspaceLayout.Panels
            .Select(panel => string.Equals(
                panel.PanelId,
                panelId,
                StringComparison.OrdinalIgnoreCase)
                ? CopyPanelPlacement(
                    panel,
                    isVisible: false,
                    replacement: null)
                : panel.Clone())
            .ToList();
        ApplyWorkspaceLayout(
            new WorkspaceLayoutSnapshot
            {
                IsUserUnlocked = true,
                SideTools = _runtimeWorkspaceLayout.SideTools
                    .Select(tool => tool.Clone())
                    .ToList(),
                Panels = panels
            },
            persist: false);
    }

    internal void ResizeExtensionPanel(
        string panelId,
        double? width,
        double? height) =>
        UpdateWorkspacePanelSize(
            panelId,
            width,
            height,
            refreshLayout: true);

    internal void UpdateWorkspacePanelSize(
        string panelId,
        double? width,
        double? height,
        bool refreshLayout)
    {
        var registration = _workspacePanelRegistry.Registrations.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Contribution.Id,
                panelId,
                StringComparison.OrdinalIgnoreCase));
        var currentPlacement = _runtimeWorkspaceLayout.Panels.FirstOrDefault(panel =>
            string.Equals(
                panel.PanelId,
                panelId,
                StringComparison.OrdinalIgnoreCase));
        var isSideDocked = currentPlacement?.Slot is WorkspacePanelSlot.Left
            or WorkspacePanelSlot.Right;
        if (registration is null
            || !isSideDocked
            && registration.Contribution.Placement.AllowUserResize != true)
        {
            return;
        }

        var panels = _runtimeWorkspaceLayout.Panels
            .Select(panel => string.Equals(
                panel.PanelId,
                panelId,
                StringComparison.OrdinalIgnoreCase)
                ? new PanelPlacementSnapshot
                {
                    ExtensionId = panel.ExtensionId,
                    PanelId = panel.PanelId,
                    Slot = panel.Slot,
                    Replaces = panel.Replaces,
                    Order = panel.Order,
                    Width = width is > 0 ? width.Value : panel.Width,
                    Height = height is > 0 ? height.Value : panel.Height,
                    IsVisible = panel.IsVisible
                }
                : panel.Clone())
            .ToList();
        _runtimeWorkspaceLayout = WorkspaceLayoutService.Normalize(
            new WorkspaceLayoutSnapshot
            {
                IsUserUnlocked = true,
                SideTools = _runtimeWorkspaceLayout.SideTools
                    .Select(tool => tool.Clone())
                    .ToList(),
                Panels = panels
            },
            _workspacePanelRegistry);
        if (refreshLayout)
        {
            OnPropertyChanged(nameof(WorkspaceLayout));
        }
    }

    private void HideExtensionPanels(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var panels = _runtimeWorkspaceLayout.Panels
            .Select(panel => string.Equals(
                panel.ExtensionId,
                extensionId,
                StringComparison.OrdinalIgnoreCase)
                ? CopyPanelPlacement(
                    panel,
                    isVisible: false,
                    replacement: null)
                : panel.Clone())
            .ToList();
        ApplyWorkspaceLayout(
            new WorkspaceLayoutSnapshot
            {
                IsUserUnlocked = true,
                SideTools = _runtimeWorkspaceLayout.SideTools
                    .Select(tool => tool.Clone())
                    .ToList(),
                Panels = panels
            },
            persist: false);
    }

    private void SynchronizeApprovedPanelRegistry()
    {
        _workspacePanelRegistry.ClearContributedPanels();
        foreach (var candidate in _extensionCatalog.PanelCandidates.Where(candidate =>
                     candidate.IsWebView
                     && _extensionApprovalService.IsWebPanelApproved(
                         candidate.Package)))
        {
            _workspacePanelRegistry.Register(candidate.Registration);
        }
    }

    private static PanelPlacementSnapshot CopyPanelPlacement(
        PanelPlacementSnapshot source,
        bool isVisible,
        BuiltInWorkspaceSurface? replacement) =>
        new()
        {
            ExtensionId = source.ExtensionId,
            PanelId = source.PanelId,
            Slot = source.Slot,
            Replaces = replacement,
            Order = source.Order,
            Width = source.Width,
            Height = source.Height,
            IsVisible = isVisible
        };

    private void CopyImportedSettings(AppSettings source)
    {
        _settings.TypographyVersion = source.TypographyVersion;
        _settings.InputBehaviorVersion = source.InputBehaviorVersion;
        _settings.TerminalProfilesVersion = source.TerminalProfilesVersion;
        _settings.CommandLibraryLayoutVersion =
            source.CommandLibraryLayoutVersion;
        _settings.PortableProfileId = source.PortableProfileId;
        _settings.PortableProfileDisplayName =
            source.PortableProfileDisplayName;
        _settings.PortableProfileRevision = source.PortableProfileRevision;
        _settings.PortableProfileUpdatedAtUtc =
            source.PortableProfileUpdatedAtUtc;
        _settings.ProfileExtensions =
            new ObservableCollection<ProfileExtensionReference>(
                source.ProfileExtensions.Select(reference =>
                    new ProfileExtensionReference
                    {
                        ExtensionId = reference.ExtensionId,
                        VersionRange = reference.VersionRange,
                        Enabled = reference.Enabled,
                        ActivateOnStartup = reference.ActivateOnStartup
                    }));
        _settings.ProfileLibraries =
            new ObservableCollection<ProfileLibraryReference>(
                source.ProfileLibraries.Select(reference =>
                    new ProfileLibraryReference
                    {
                        ExtensionId = reference.ExtensionId,
                        LibraryId = reference.LibraryId,
                        VersionRange = reference.VersionRange,
                        Enabled = reference.Enabled
                    }));
        _settings.Application.CopyFrom(source.Application);
        _settings.CommandBuilder.CopyFrom(source.CommandBuilder);
        _settings.Explorer.CopyFrom(source.Explorer);
        _settings.WorkspaceLayout = source.WorkspaceLayout.Clone();
        _settings.ActiveTerminalProfileId = source.ActiveTerminalProfileId;
        _settings.TerminalProfiles = new ObservableCollection<TerminalProfile>(
            source.TerminalProfiles.Select(profile => profile.Clone()));
        _settings.Terminal = _settings.TerminalProfiles
            .FirstOrDefault(profile => string.Equals(
                profile.Id,
                "powershell",
                StringComparison.OrdinalIgnoreCase))
            ?.Settings
            ?? source.Terminal.Clone();
        _settings.TerminalConfigurationProfiles =
            new ObservableCollection<TerminalConfigurationProfile>(
                source.TerminalConfigurationProfiles.Select(configuration =>
                    configuration.Clone()));
        _settings.ActiveEnvironmentId = source.ActiveEnvironmentId;
    }

    private void SaveSettingsImmediately(
        string statusMessage,
        bool portableChange = false)
    {
        if (portableChange)
        {
            TouchPortableProfile();
        }

        _configurationChangeVersion++;
        _autosaveTimer.Stop();
        _autosaveCueTimer.Stop();
        _settingsService.Save(_settings);
        ScheduleCloudSync();
        AutosaveStatusText = "Saved";
        _autosaveCueTimer.Start();
        ShowTransientStatus(statusMessage);
    }

    private void TouchPortableProfile()
    {
        _settings.PortableProfileRevision =
            _settings.PortableProfileRevision == long.MaxValue
                ? long.MaxValue
                : _settings.PortableProfileRevision + 1;
        _settings.PortableProfileUpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string ResolveTerminalTitle(
        string template,
        TerminalProfile profile,
        int terminalNumber)
    {
        var title = template
            .Replace("{shell}", profile.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{n}", terminalNumber.ToString(), StringComparison.OrdinalIgnoreCase)
            .Trim();
        return string.IsNullOrWhiteSpace(title)
            ? $"{profile.Name} {terminalNumber}"
            : title;
    }

    private void OnTerminalSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(TerminalSettings.ForegroundBrush)
            or nameof(TerminalSettings.BackgroundBrush)
            or nameof(TerminalSettings.AccentBrush)
            or nameof(TerminalSettings.ErrorBrush)
            or nameof(TerminalSettings.TerminalFontFamily)
            or nameof(TerminalSettings.TerminalFontWeight)
            or nameof(TerminalSettings.TerminalFontStyle)
            or nameof(TerminalSettings.TerminalFontSize)
            or nameof(TerminalSettings.TranscriptBackdropBrush)
            or nameof(TerminalSettings.InputBackdropBrush)
            or nameof(TerminalSettings.InputBackdropBorderBrush))
        {
            return;
        }

        ScheduleAutosave();
    }

    private void RefreshObservedTerminalSettings()
    {
        var activeSettings = _settings.TerminalProfiles
            .Select(profile => profile.Settings)
            .ToHashSet();
        foreach (var removedSettings in _observedTerminalSettings
                     .Where(settings => !activeSettings.Contains(settings))
                     .ToList())
        {
            removedSettings.PropertyChanged -= OnTerminalSettingsChanged;
            _observedTerminalSettings.Remove(removedSettings);
        }

        foreach (var terminalSettings in activeSettings)
        {
            if (_observedTerminalSettings.Add(terminalSettings))
            {
                terminalSettings.PropertyChanged += OnTerminalSettingsChanged;
            }
        }
    }

    private void ScheduleAutosave()
    {
        if (_isDisposed)
        {
            return;
        }

        _configurationChangeVersion++;
        _autosaveCueTimer.Stop();
        _autosaveTimer.Stop();
        AutosaveStatusText = "Unsaved";
        _autosaveTimer.Start();
    }

    private async void OnAutosaveTimerTick(object? sender, EventArgs eventArgs)
    {
        _autosaveTimer.Stop();
        var saveVersion = _configurationChangeVersion;
        AutosaveStatusText = "Saving";

        // Let WPF render the Saving cue before the small synchronous settings write.
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (_isDisposed || saveVersion != _configurationChangeVersion)
        {
            return;
        }

        try
        {
            _settingsService.Save(_settings);
            ScheduleCloudSync();
        }
        catch (Exception exception)
        {
            if (!_isDisposed && saveVersion == _configurationChangeVersion)
            {
                AutosaveStatusText = "Unsaved";
                _statusCueTimer.Stop();
                StatusMessage = $"Unable to save configuration: {exception.Message}";
                _autosaveTimer.Start();
            }

            return;
        }

        // Keep the transient state readable even when the file write is nearly instant.
        await Task.Delay(500);
        if (_isDisposed || saveVersion != _configurationChangeVersion)
        {
            return;
        }

        AutosaveStatusText = "Saved";
        _autosaveCueTimer.Stop();
        _autosaveCueTimer.Start();
    }

    private void OnAutosaveCueTimerTick(object? sender, EventArgs eventArgs)
    {
        _autosaveCueTimer.Stop();
        if (AutosaveStatusText == "Saved")
        {
            AutosaveStatusText = string.Empty;
        }
    }

    public void ReportEditorStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowTransientStatus(message);
        }
    }

    public void ExcludeDocumentFromSession(DocumentTabViewModel document)
    {
        document.ExcludeFromFutureSession();
        _editorSessionService.DeleteRecovery(document.SessionId);
        ScheduleEditorSessionSave();
    }

    public async Task PrepareForShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        _editorRecoveryTimer.Stop();
        _editorSessionDirty = true;
        await PersistEditorSessionAsync(
            force: true,
            cancellationToken: cancellationToken);
        foreach (var conversation in WorkspaceAiConversations)
        {
            SaveConversation(conversation, isOpenOnThisDevice: true);
        }

        if (_profileSyncSessionService.IsSignedIn)
        {
            using var profileShutdown =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            profileShutdown.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await SyncCurrentCloudStateAsync(profileShutdown.Token);
                await _profileSyncSessionService.SignOutAsync(
                    forgetCredential: false,
                    profileShutdown.Token);
            }
            catch (OperationCanceledException)
            {
                _profileSyncSessionService.InvalidateSession(
                    "The application closed before remote session revocation completed.");
            }
        }
    }

    public bool HasRunningAtlasMyNetSessionApps() =>
        AtlasMyNet.HasRunningSessionApps();

    public Task<IReadOnlyList<AtlasMyNetStopResult>>
        StopAtlasMyNetSessionAppsAsync() =>
        AtlasMyNet.StopSessionAppsGracefullyAsync();

    public void LeaveAtlasMyNetSessionAppsRunning() =>
        AtlasMyNet.LeaveSessionAppsRunning();

    private void OnProfileSyncSessionPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        var wasSignedIn = _lastProfileSyncSignedIn;
        var isSignedIn = _profileSyncSessionService.IsSignedIn;
        _lastProfileSyncSignedIn = isSignedIn;
        OnPropertyChanged(nameof(IsProfileSyncSignedIn));
        OnPropertyChanged(nameof(IsProfileSyncOffline));
        OnPropertyChanged(nameof(IsProfileSyncBusy));
        OnPropertyChanged(nameof(CanProfileSyncSignIn));
        OnPropertyChanged(nameof(CanProfileSyncSignOut));
        OnPropertyChanged(nameof(ProfileSyncAccountLabel));
        OnPropertyChanged(nameof(ProfileSyncTitleToolTip));
        OnPropertyChanged(nameof(ProfileAvatarInitials));
        NotifyProfileLoginStateChanged();
        if (isSignedIn)
        {
            CompleteProfileLogin();
            if (!wasSignedIn)
            {
                _isAccountSyncBootstrapPending = true;
                _ = BootstrapAccountStateAsync();
                _ = RefreshLinkedHostWorkspaceProvidersAsync();
            }
            else
            {
                ScheduleCloudSync();
            }
        }
        else if (wasSignedIn)
        {
            _isAccountSyncBootstrapPending = false;
            _ = ClearLinkedHostWorkspaceProvidersAsync();
        }
    }

    private void ReloadProfileAvatar()
    {
        _profileAvatarImageSource = _profileAvatarService.TryLoad();
        OnPropertyChanged(nameof(ProfileAvatarImageSource));
        OnPropertyChanged(nameof(HasProfileAvatarImage));
    }

    private void ResolveStartupProfileLogin()
    {
        PendingProfileLoginCode = string.Empty;
        PendingProfileLoginUri = string.Empty;
        IsProfileLoginFlowActive = false;
        ProfileLoginStatus = _profileSyncSessionService.IsOffline
                             && _profileSyncSessionService.HasSavedCredential
            ? "Atlas is temporarily unavailable. You can retry sign-in or continue with the local workspace."
            : _profileSyncSessionService.State
                == ProfileSyncConnectionState.Error
                ? _profileSyncSessionService.StatusText
                : string.Empty;
        IsProfileLoginGateVisible = true;
        NotifyProfileLoginStateChanged();
    }

    private void NotifyProfileLoginStateChanged()
    {
        OnPropertyChanged(nameof(CanBeginProfileLogin));
        OnPropertyChanged(nameof(CanCancelProfileLogin));
        OnPropertyChanged(nameof(CanContinueWithoutProfileLogin));
    }

    private async Task RefreshLinkedHostWorkspaceProvidersAsync()
    {
        try
        {
            _ = await LoadLinkedHostInventoryAsync();
        }
        catch (Exception exception) when (exception is ProfileSyncException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException)
        {
            if (!_isDisposed)
            {
                ShowTransientStatus(
                    $"Linked SSH devices could not be refreshed: {exception.Message}");
            }
        }
    }

    private async Task BootstrapAccountStateAsync()
    {
        var shouldScheduleUpload = false;
        try
        {
            var profiles = await ListRemoteProfilesAsync();
            var remote = profiles
                .Where(profile => !profile.IsDeleted)
                .OrderByDescending(profile => profile.UpdatedAtUtc)
                .ThenByDescending(profile => profile.ServerRevision)
                .FirstOrDefault();
            var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(
                _profileSyncSessionService.Configuration.ServerEndpoint);

            if (remote is not null)
            {
                var localSnapshot = _portableProfileService.CreateSnapshot(
                    _settings,
                    _extensionCatalog);
                var localHash = _portableProfileService.ComputeProfileSha256(
                    localSnapshot);
                var checkpoint = _profileSyncDeviceStateService.FindCheckpoint(
                    endpointKey,
                    remote.Id);
                var sameProfile = string.Equals(
                    _settings.PortableProfileId,
                    remote.Id,
                    StringComparison.OrdinalIgnoreCase);
                var localChangedSinceCheckpoint = checkpoint is not null
                    && !string.Equals(
                        checkpoint.ProfileSha256,
                        localHash,
                        StringComparison.OrdinalIgnoreCase);
                var remoteChangedSinceCheckpoint = checkpoint is null
                    || !string.Equals(
                        checkpoint.ETag,
                        remote.ETag,
                        StringComparison.Ordinal);
                var shouldPull = !sameProfile
                    || checkpoint is null
                    || (remoteChangedSinceCheckpoint
                        && (!localChangedSinceCheckpoint
                            || remote.UpdatedAtUtc
                            >= _settings.PortableProfileUpdatedAtUtc));

                if (shouldPull)
                {
                    var download = await DownloadRemoteProfileAsync(remote.Id);
                    if (!download.Preview.CanImport
                        || download.Preview.Envelope is null)
                    {
                        throw new ProfileSyncException(
                            ProfileSyncFailureKind.Protocol,
                            "The current account settings failed local validation.");
                    }

                    await ApplyPortableProfileAsync(
                        download.Preview,
                        persist: true);
                    AcceptDownloadedProfile(download);
                    try
                    {
                        _ = await RestoreCloudProfileResourcesAsync(remote.Id);
                    }
                    catch (ProfileSyncException exception) when (
                        exception.Kind == ProfileSyncFailureKind.NotFound)
                    {
                        // Older accounts may not have the adjunct resource yet.
                        // The automatic upload below creates the current form.
                    }

                    ShowTransientStatus(
                        "Loaded the latest Atlas account settings for this device.");
                }
                else if (remoteChangedSinceCheckpoint)
                {
                    // The local state is newer. Adopt the server's current ETag
                    // so the automatic write replaces it instead of creating a
                    // user-visible conflict artifact.
                    _profileSyncDeviceStateService.SaveCheckpoint(
                        new ProfileSyncCheckpoint
                        {
                            EndpointKey = endpointKey,
                            ProfileId = remote.Id,
                            ETag = remote.ETag,
                            ServerRevision = remote.ServerRevision,
                            ProfileSha256 = remote.ProfileSha256,
                            LastPulledAtUtc = checkpoint?.LastPulledAtUtc,
                            LastPushedAtUtc = checkpoint?.LastPushedAtUtc,
                            IsRemoteDeleted = false
                        });
                }
            }
            else
            {
                var matchingTombstone = profiles
                    .Where(profile => profile.IsDeleted
                                      && string.Equals(
                                          profile.Id,
                                          _settings.PortableProfileId,
                                          StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(profile => profile.ServerRevision)
                    .FirstOrDefault();
                if (matchingTombstone is not null)
                {
                    _profileSyncDeviceStateService.SaveCheckpoint(
                        new ProfileSyncCheckpoint
                        {
                            EndpointKey = endpointKey,
                            ProfileId = matchingTombstone.Id,
                            ETag = matchingTombstone.ETag,
                            ServerRevision = matchingTombstone.ServerRevision,
                            ProfileSha256 = matchingTombstone.ProfileSha256,
                            IsRemoteDeleted = true
                        });
                }
            }

            shouldScheduleUpload = true;
        }
        catch (Exception exception) when (exception is ProfileSyncException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException)
        {
            if (!_isDisposed)
            {
                StatusMessage =
                    $"Account sync deferred; local work is safe: {exception.Message}";
            }
        }
        finally
        {
            _isAccountSyncBootstrapPending = false;
            if (shouldScheduleUpload)
            {
                ScheduleCloudSync();
            }
        }
    }

    private void ScheduleCloudSync()
    {
        if (_isDisposed
            || !_profileSyncSessionService.IsSignedIn
            || _isAccountSyncBootstrapPending)
        {
            return;
        }

        _cloudSyncTimer.Stop();
        _cloudSyncTimer.Start();
    }

    private async void OnCloudSyncTimerTick(
        object? sender,
        EventArgs eventArgs)
    {
        _cloudSyncTimer.Stop();
        if (_isDisposed || !_profileSyncSessionService.IsSignedIn)
        {
            return;
        }

        try
        {
            _ = await SyncCurrentCloudStateAsync();
        }
        catch (Exception exception) when (exception is ProfileSyncException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException)
        {
            if (!_isDisposed)
            {
                StatusMessage =
                    $"Cloud sync deferred; local work is safe: {exception.Message}";
            }
        }
    }

    private async Task<(int RestoredDocumentCount, int UnavailableRecoveryCount)>
        RestoreEditorSessionAsync(
            CancellationToken cancellationToken = default)
    {
        _isRestoringEditorSession = true;
        var restoredDocuments = new List<DocumentTabViewModel>();
        var unavailableRecoveryCount = 0;
        try
        {
            var session = await _editorSessionService.LoadSessionAsync(
                cancellationToken);
            foreach (var entry in session.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recovery = await _editorSessionService.LoadRecoveryAsync(
                    entry.SessionId,
                    cancellationToken);
                if (entry.HadUnsavedChanges && recovery is null)
                {
                    unavailableRecoveryCount++;
                }

                var document = await DocumentTabViewModel.RestoreAsync(
                    entry,
                    recovery,
                    _textDocumentService,
                    _workspaceDocumentService,
                    cancellationToken);
                if (document is null)
                {
                    continue;
                }

                if (document.HasFilePath
                    && WorkspaceTabs
                        .OfType<DocumentTabViewModel>()
                        .Any(existing => string.Equals(
                            existing.DocumentReference,
                            document.DocumentReference,
                            document.IsRemoteDocument
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase)))
                {
                    document.Dispose();
                    continue;
                }

                BindDocumentProvider(document);
                InsertWorkspaceTab(document);
                restoredDocuments.Add(document);
                UpdateUntitledDocumentNumber(document);
            }

            var activeDocument = restoredDocuments.FirstOrDefault(document =>
                string.Equals(
                    document.SessionId,
                    session.ActiveDocumentSessionId,
                    StringComparison.OrdinalIgnoreCase));
            if (activeDocument is not null)
            {
                ActiveWorkspaceTab = activeDocument;
            }
            else if (ActiveWorkspaceTab is null && restoredDocuments.Count > 0)
            {
                ActiveWorkspaceTab = restoredDocuments[0];
            }
        }
        finally
        {
            _isRestoringEditorSession = false;
            _editorSessionDirty = false;
            _editorRecoveryTimer.Stop();
        }

        return (restoredDocuments.Count, unavailableRecoveryCount);
    }

    private void UpdateUntitledDocumentNumber(DocumentTabViewModel document)
    {
        if (document.HasFilePath)
        {
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(document.Title);
        if (string.Equals(baseName, "Untitled", StringComparison.OrdinalIgnoreCase))
        {
            _untitledDocumentNumber = Math.Max(_untitledDocumentNumber, 1);
            return;
        }

        const string prefix = "Untitled-";
        if (baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(baseName[prefix.Length..], out var number))
        {
            _untitledDocumentNumber = Math.Max(_untitledDocumentNumber, number);
        }
    }

    private void BindDocumentProvider(DocumentTabViewModel document)
    {
        var resolution = _documentProviderRegistry.Resolve(
            document.WorkspaceLocation?.Path
            ?? (document.HasFilePath ? document.FilePath : document.Title),
            document.LanguageId);
        document.ApplyDocumentProvider(resolution);
    }

    private void OnDocumentPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(DocumentTabViewModel.Content)
            or nameof(DocumentTabViewModel.HasUnsavedChanges)
            or nameof(DocumentTabViewModel.FilePath)
            or nameof(DocumentTabViewModel.WorkspaceUri)
            or nameof(DocumentTabViewModel.EditorViewState)
            or nameof(DocumentTabViewModel.EditorSettings)
            or nameof(DocumentTabViewModel.EncodingKind)
            or nameof(DocumentTabViewModel.LineEndingKind)
            or nameof(DocumentTabViewModel.Title))
        {
            ScheduleEditorSessionSave();
        }

        if (eventArgs.PropertyName == nameof(DocumentTabViewModel.Content)
            && sender is DocumentTabViewModel
            {
                IsAiLiveAssistEnabled: true
            } document
            && ReferenceEquals(ActiveDocument, document))
        {
            WorkspaceAiAssistant.QueueLiveActivity(
                new WorkspaceAiLiveActivity(
                    document.AiLiveSourceId,
                    WorkspaceAiLiveActivityKind.DocumentChanged,
                    document.Title,
                    "The user changed the active document and paused typing."));
        }
    }

    private void OnTerminalPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (sender is not TerminalTabViewModel
            {
                IsAiLiveAssistEnabled: true
            } terminal
            || !ReferenceEquals(ActiveTerminal, terminal))
        {
            return;
        }

        if (eventArgs.PropertyName == nameof(TerminalTabViewModel.InputText))
        {
            if (terminal.IsRunning
                || string.IsNullOrWhiteSpace(terminal.InputText))
            {
                WorkspaceAiAssistant.CancelLiveActivity(
                    terminal.AiLiveSourceId);
                return;
            }

            WorkspaceAiAssistant.QueueLiveActivity(
                new WorkspaceAiLiveActivity(
                    terminal.AiLiveSourceId,
                    WorkspaceAiLiveActivityKind.TerminalDraftChanged,
                    terminal.Title,
                    "The user changed the command draft and paused typing."));
            return;
        }

        if (eventArgs.PropertyName != nameof(TerminalTabViewModel.IsRunning))
        {
            return;
        }

        if (terminal.IsRunning)
        {
            WorkspaceAiAssistant.CancelLiveActivity(
                terminal.AiLiveSourceId);
            return;
        }

        WorkspaceAiAssistant.QueueLiveActivity(
            new WorkspaceAiLiveActivity(
                terminal.AiLiveSourceId,
                WorkspaceAiLiveActivityKind.TerminalCommandCompleted,
                terminal.Title,
                "The active terminal command finished; inspect its recent output."));
    }

    private void OnWorkspaceAiLiveSourcesInvalidated(
        object? sender,
        EventArgs eventArgs)
    {
        foreach (var tab in WorkspaceTabs)
        {
            tab.IsAiLiveAssistEnabled = false;
        }
    }

    private void OnWorkspaceAiDesktopLiveSessionChanged(
        object? sender,
        WorkspaceAiDesktopLiveSessionChangedEventArgs eventArgs)
    {
        if (sender is not WorkspaceAiAssistantViewModel assistant
            || !ReferenceEquals(assistant, WorkspaceAiAssistant))
        {
            return;
        }

        if (eventArgs.IsEnabled && !assistant.IsBusy)
        {
            _workspaceAiUserActivityMonitor.Start();
        }
        else
        {
            _workspaceAiUserActivityMonitor.Stop();
        }
    }

    private void OnWorkspaceAiAssistantPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (sender is not WorkspaceAiAssistantViewModel assistant
            || !ReferenceEquals(assistant, WorkspaceAiAssistant)
            || eventArgs.PropertyName != nameof(
                WorkspaceAiAssistantViewModel.IsBusy)
            || !assistant.IsDesktopLiveSessionEnabled)
        {
            return;
        }

        if (assistant.IsBusy)
        {
            // Pausing the monitor while AI acts prevents its own injected
            // mouse and keyboard events from recursively triggering a turn.
            _workspaceAiUserActivityMonitor.Stop();
        }
        else
        {
            _workspaceAiUserActivityMonitor.Start();
        }
    }

    private void OnWindowsUserActivityPaused(
        object? sender,
        WindowsUserActivityPausedEventArgs eventArgs)
    {
        if (!WorkspaceAiAssistant.IsDesktopLiveSessionEnabled
            || WorkspaceAiAssistant.IsBusy)
        {
            return;
        }

        WorkspaceAiAssistant.QueueLiveActivity(
            new WorkspaceAiLiveActivity(
                WorkspaceAiAssistantViewModel.DesktopLiveSourceId,
                WorkspaceAiLiveActivityKind.DesktopActivityPaused,
                "Windows desktop",
                $"The user interacted with Windows and then paused for {eventArgs.IdleDuration.TotalSeconds:0.0} seconds."));
    }

    private void ScheduleEditorSessionSave()
    {
        if (_isDisposed || _isRestoringEditorSession)
        {
            return;
        }

        _editorSessionDirty = true;
        if (!_editorRecoveryTimer.IsEnabled)
        {
            _editorRecoveryTimer.Start();
        }
    }

    private async void OnEditorRecoveryTimerTick(
        object? sender,
        EventArgs eventArgs)
    {
        _editorRecoveryTimer.Stop();
        await PersistEditorSessionAsync();
    }

    private async Task PersistEditorSessionAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        await _editorSessionSaveGate.WaitAsync(cancellationToken);
        try
        {
            if (_isDisposed || (!force && !_editorSessionDirty))
            {
                return;
            }

            _editorSessionDirty = false;
            var documents = WorkspaceTabs
                .OfType<DocumentTabViewModel>()
                .Where(document => !document.ExcludeFromSessionRestore)
                .Take(EditorSessionService.MaximumDocumentCount)
                .ToList();
            var session = new EditorSessionSnapshot
            {
                ActiveDocumentSessionId = ActiveDocument is { } activeDocument
                                          && !activeDocument.ExcludeFromSessionRestore
                    ? activeDocument.SessionId
                    : string.Empty,
                Documents = documents
                    .Select(document => document.CreateSessionEntry())
                    .ToList()
            };
            var recoveries = documents
                .Where(document => document.HasUnsavedChanges)
                .Select(document => document.CreateRecoverySnapshot())
                .ToList();
            var result = await _editorSessionService.SaveAsync(
                session,
                recoveries,
                cancellationToken);
            _lastEditorSessionError = string.Empty;
            if (result.SkippedRecoverySessionIds.Count > 0)
            {
                ShowTransientStatus(
                    $"Recovery skipped for {result.SkippedRecoverySessionIds.Count} " +
                    $"document(s) over {EditorSessionService.MaximumRecoveryContentBytes / 1024 / 1024} MiB.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!string.Equals(
                    _lastEditorSessionError,
                    exception.Message,
                    StringComparison.Ordinal))
            {
                _lastEditorSessionError = exception.Message;
                ShowTransientStatus(
                    $"Editor recovery could not be updated: {exception.Message}");
            }
        }
        finally
        {
            _editorSessionSaveGate.Release();
            if (_editorSessionDirty && !_isDisposed && !_editorRecoveryTimer.IsEnabled)
            {
                _editorRecoveryTimer.Start();
            }
        }
    }

    private void ShowTransientStatus(string message)
    {
        _statusCueTimer.Stop();
        StatusMessage = message;
        _statusCueTimer.Start();
    }

    private void OnStatusCueTimerTick(object? sender, EventArgs eventArgs)
    {
        _statusCueTimer.Stop();
        StatusMessage = string.Empty;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= OnAutosaveTimerTick;
        _autosaveCueTimer.Stop();
        _autosaveCueTimer.Tick -= OnAutosaveCueTimerTick;
        _statusCueTimer.Stop();
        _statusCueTimer.Tick -= OnStatusCueTimerTick;
        _editorRecoveryTimer.Stop();
        _editorRecoveryTimer.Tick -= OnEditorRecoveryTimerTick;
        _cloudSyncTimer.Stop();
        _cloudSyncTimer.Tick -= OnCloudSyncTimerTick;
        _profileSyncSessionService.PropertyChanged -=
            OnProfileSyncSessionPropertyChanged;
        SourceControl.DiffRequested -= OnSourceControlDiffRequested;
        SourceControl.ConflictRequested -= OnSourceControlConflictRequested;
        AtlasMyNet.OpenAppRequested -= OnAtlasMyNetOpenAppRequested;
        AtlasMyNet.NetworkClientRequested -= OnAtlasMyNetNetworkClientRequested;
        AtlasMyNet.Dispose();
        NetworkObservatory.Dispose();
        FileExplorer.PropertyChanged -= OnFileExplorerPropertyChanged;
        FileExplorer.Dispose();
        foreach (var terminalSettings in _observedTerminalSettings)
        {
            terminalSettings.PropertyChanged -= OnTerminalSettingsChanged;
        }
        _observedTerminalSettings.Clear();
        foreach (var tab in WorkspaceTabs)
        {
            if (tab is DocumentTabViewModel document)
            {
                document.PropertyChanged -= OnDocumentPropertyChanged;
            }
            else if (tab is TerminalTabViewModel terminal)
            {
                terminal.PropertyChanged -= OnTerminalPropertyChanged;
            }

            tab.Dispose();
        }

        _settingsService.Save(_settings);
        _profileSyncSessionService.Dispose();
        if (_selectedWorkspaceAiConversation is { } selectedConversation)
        {
            DetachWorkspaceAiAssistant(selectedConversation.Assistant);
        }

        _workspaceAiUserActivityMonitor.ActivityPaused -=
            OnWindowsUserActivityPaused;
        _workspaceAiUserActivityMonitor.Dispose();
        foreach (var conversation in WorkspaceAiConversations)
        {
            SaveConversation(conversation, isOpenOnThisDevice: true);
            UnsubscribeConversationPersistence(conversation);
            conversation.Dispose();
        }

        WorkspaceAiConversations.Clear();
        _selectedWorkspaceAiConversation = null;
        _workspaceAiComputerControlService.Dispose();
        _workspaceAiProvider.Dispose();
        _workspaceAiEmbeddings.Dispose();
        _workspaceAiLocalRuntime.Dispose();
        _workspaceFileSystems.DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}

public sealed class WorkspaceSideToolViewModel : ObservableObject
{
    private bool _isActive;

    public WorkspaceSideToolViewModel(
        WorkspaceSideToolDefinition definition,
        Action<string> select)
    {
        Definition = definition;
        SelectCommand = new RelayCommand(() => select(Id));
    }

    public WorkspaceSideToolDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string ShortDisplayName => Definition.ShortDisplayName;
    public string Glyph => Definition.Glyph;
    public string GlyphFontFamily => Definition.GlyphFontFamily;
    public WorkspaceSideToolKind Kind => Definition.Kind;
    public bool IsTextGlyph => Kind is WorkspaceSideToolKind.WorkspaceAi
        or WorkspaceSideToolKind.ProjectWorkItems
        or WorkspaceSideToolKind.SourceControl;
    public RelayCommand SelectCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

public enum CommandBuilderPanePage
{
    Builder,
    Ai,
    NetworkObservatory
}
