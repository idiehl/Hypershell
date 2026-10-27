using Hypershell.Infrastructure;
using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed record WorkspaceAiProviderOption(string Id, string DisplayName);

public sealed record WorkspaceAiModelOption(string Id, string DisplayName);

public sealed class SettingsWindowViewModel
    : ObservableObject,
      IDisposable
{
    private readonly MainWindowViewModel _mainWindow;
    private readonly WorkspacePanelRegistry _workspacePanelRegistry;
    private WorkspaceLayoutSnapshot _workspaceSessionBaseline;
    private IReadOnlyList<PanelPlacementSnapshot> _unregisteredWorkspacePanels = [];
    private IReadOnlyList<WorkspaceSideToolPlacementSnapshot>
        _unregisteredWorkspaceSideTools = [];
    private TerminalConfigurationProfileEditorViewModel? _selectedTerminalConfiguration;
    private ProjectEditorViewModel? _selectedProject;
    private string _defaultTerminalProfileId;
    private bool _openSelectedProjectOnStartup;
    private string _generalStatus = string.Empty;
    private string _terminalStatus = string.Empty;
    private string _explorerStatus = string.Empty;
    private string _workspaceStatus = string.Empty;
    private string _languageToolsStatus = string.Empty;
    private string _languageToolCatalogSummary = string.Empty;
    private string _languageToolScanTimeText = string.Empty;
    private string _extensionsStatus = string.Empty;
    private string _extensionRootDirectory = string.Empty;
    private string _extensionCatalogSummary = string.Empty;
    private string _extensionScanTimeText = string.Empty;
    private ProfileImportPreview? _profileImportPreview;
    private ProfileSyncDownloadOutcome? _pendingRemoteDownload;
    private RemoteProfileEditorViewModel? _selectedRemoteProfile;
    private string _portableProfileDisplayName;
    private string _profileImportSourcePath = string.Empty;
    private string _profileImportName = "No profile selected";
    private string _profileImportSummary =
        "Choose a .hyperprofile file to validate and preview it before applying anything.";
    private string _profileImportIntegrityText = string.Empty;
    private string _profileStatus =
        "Portable profile actions are explicit. Nothing uploads or downloads automatically.";
    private string _profileSyncServerEndpoint = string.Empty;
    private string _profileSyncAccountId = string.Empty;
    private string _profileSyncStatus =
        "Authorize this PC through the private Hypershell portal.";
    private bool _rememberProfileSyncAccessKey = true;
    private bool _isProfileSyncBusy;
    private string _pendingProfileDeviceCode = string.Empty;
    private string _pendingProfileVerificationUri = string.Empty;
    private string _applicationUpdateStatus = string.Empty;
    private bool _isApplicationUpdateBusy;
    private double _applicationUpdateProgress;
    private ApplicationUpdateCandidate? _applicationUpdateCandidate;
    private ApplicationUpdateDownloadOutcome? _downloadedApplicationUpdate;
    private WorkspaceAiAccessEditorViewModel? _selectedWorkspaceAiAccess;
    private string _workspaceAiStatus =
        "AI workspace access is disabled until a device-local approval is saved.";
    private string _workspaceAiProviderId =
        WorkspaceAiProviderConfiguration.OpenAiProviderId;
    private string _workspaceAiProviderModel =
        WorkspaceAiProviderConfiguration.DefaultModel;
    private string _workspaceAiOpenAiModel =
        WorkspaceAiProviderConfiguration.DefaultModel;
    private string _workspaceAiLocalModel =
        WorkspaceAiProviderConfiguration.DefaultLocalModel;
    private string _workspaceAiReasoningEffort = "medium";
    private string _workspaceAiOpenAiReasoningEffort = "medium";
    private string _workspaceAiLocalReasoningEffort = "medium";
    private string _workspaceAiTextVerbosity = "medium";
    private string _workspaceAiOpenAiTextVerbosity = "medium";
    private string _workspaceAiLocalTextVerbosity = "medium";
    private string _workspaceAiProviderEndpoint =
        WorkspaceAiProviderConfiguration.OpenAiResponsesEndpoint;
    private string _workspaceAiProviderStatus =
        "Cloud requests are disabled until explicitly enabled.";
    private bool _allowWorkspaceAiCloudRequests;
    private bool _workspaceAiOpenAiCloudRequestsEnabled;
    private bool _hasWorkspaceAiCredential;
    private string _workspaceAiAutonomyContextName = "No selected Context";
    private string _workspaceAiAutonomyStatus = string.Empty;
    private bool _allowWorkspaceAiAutonomousChanges;
    private string _newWorkspaceAiRootName = string.Empty;
    private string _newWorkspaceAiRootPath = string.Empty;
    private bool _newWorkspaceAiRootAllowRead = true;
    private bool _newWorkspaceAiRootAllowWrite;
    private bool _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges;
    private bool _newWorkspaceAiRootAllowTerminalRead;
    private bool _newWorkspaceAiRootAllowTerminal;
    private WorkspaceAiTerminalExecutionPolicy
        _newWorkspaceAiRootTerminalExecutionPolicy =
            WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand;
    private bool _isSynchronizingTerminalTargets;
    private bool _isDisposed;

    public SettingsWindowViewModel(MainWindowViewModel mainWindow)
    {
        _mainWindow = mainWindow;
        _mainWindow.ProfileSyncSession.PropertyChanged +=
            OnProfileSyncSessionPropertyChanged;
        _workspacePanelRegistry = mainWindow.WorkspacePanelRegistry;
        var settings = mainWindow.SettingsModel;
        _portableProfileDisplayName = settings.PortableProfileDisplayName;
        var syncConfiguration = mainWindow.LoadProfileSyncConfiguration();
        _profileSyncServerEndpoint = string.IsNullOrWhiteSpace(
            syncConfiguration.ServerEndpoint)
            ? ProfileSyncEndpoint.ProductionEndpoint
            : syncConfiguration.ServerEndpoint;
        _profileSyncAccountId = syncConfiguration.AccountId;
        _profileSyncStatus = mainWindow.ProfileSyncLoadWarning;
        _applicationUpdateCandidate =
            mainWindow.LatestApplicationUpdateCheck?.Candidate;
        _applicationUpdateStatus =
            mainWindow.LatestApplicationUpdateCheck?.Message
            ?? (mainWindow.ApplicationUpdates.IsTrustConfigured
                ? $"Hypershell {mainWindow.ApplicationUpdates.CurrentVersionText}. Check the private stable channel when signed in."
                : "Signing unavailable");
        General = settings.Application.Clone();
        Explorer = settings.Explorer.Clone();
        _defaultTerminalProfileId = settings.ActiveTerminalProfileId;
        _openSelectedProjectOnStartup = !string.IsNullOrWhiteSpace(Explorer.ActiveProjectId);

        TerminalProfiles = mainWindow.TerminalProfiles;
        TerminalFontFamilies = mainWindow.TerminalFontFamilies;
        NewTabPlacementOptions = Enum.GetValues<NewTabPlacement>();
        _workspaceSessionBaseline = mainWindow.WorkspaceLayout.Clone();
        LoadWorkspaceDraft(_workspaceSessionBaseline);
        LoadLanguageToolCatalog(mainWindow.LanguageToolCatalog);
        LoadExtensionCatalog(mainWindow.ExtensionCatalog);
        LoadWorkspaceAiAccess();
        LoadWorkspaceAiProviderSettings();
        LoadWorkspaceAiAutonomySettings();
        LinkedHosts = new LinkedHostSettingsViewModel(mainWindow);

        var terminalConfigurations = settings.TerminalConfigurationProfiles
            .Select(configuration => configuration.Clone())
            .ToList();
        if (terminalConfigurations.Count == 0)
        {
            var sourceProfile = mainWindow.SelectedTerminalProfile;
            terminalConfigurations.Add(new TerminalConfigurationProfile
            {
                Name = $"{sourceProfile.Name} defaults",
                Description = "Shared appearance and behavior for selected shell types.",
                TargetTerminalProfileIds = [sourceProfile.Id],
                Settings = sourceProfile.Settings.Clone(),
                TabTitleTemplate = sourceProfile.TabTitleTemplate,
                TabColor = sourceProfile.TabColor,
                StartupCommand = sourceProfile.StartupCommand
            });
        }

        foreach (var configuration in terminalConfigurations)
        {
            AddTerminalConfigurationEditor(configuration);
        }

        SelectedTerminalConfiguration = TerminalConfigurations.FirstOrDefault();

        foreach (var project in Explorer.Projects)
        {
            Projects.Add(new ProjectEditorViewModel(project));
        }

        SelectedProject = Projects.FirstOrDefault(project =>
                              string.Equals(
                                  project.Id,
                                  Explorer.ActiveProjectId,
                                  StringComparison.OrdinalIgnoreCase))
                          ?? Projects.FirstOrDefault();

        AddTerminalConfigurationCommand = new RelayCommand(AddTerminalConfiguration);
        DuplicateTerminalConfigurationCommand = new RelayCommand(DuplicateTerminalConfiguration);
        DeleteTerminalConfigurationCommand = new RelayCommand(DeleteTerminalConfiguration);
        SelectAllTerminalTargetsCommand = new RelayCommand(() => SetTerminalTargets(true));
        ClearTerminalTargetsCommand = new RelayCommand(() => SetTerminalTargets(false));
        ApplyGeneralCommand = new RelayCommand(() => ApplyGeneral(false));
        SaveGeneralCommand = new RelayCommand(() => ApplyGeneral(true));
        ApplyTerminalCommand = new RelayCommand(() => ApplyTerminal(false));
        SaveTerminalCommand = new RelayCommand(() => ApplyTerminal(true));

        AddProjectCommand = new RelayCommand(AddProject);
        DuplicateProjectCommand = new RelayCommand(DuplicateProject);
        DeleteProjectCommand = new RelayCommand(DeleteProject);
        ApplyExplorerCommand = new RelayCommand(() => ApplyExplorer(false));
        SaveExplorerCommand = new RelayCommand(() => ApplyExplorer(true));
        ResetWorkspaceCommand = new RelayCommand(ResetWorkspaceDraft);
        CancelWorkspacePreviewCommand = new RelayCommand(CancelWorkspacePreview);
        ApplyWorkspaceCommand = new RelayCommand(() => ApplyWorkspace(false));
        SaveWorkspaceCommand = new RelayCommand(() => ApplyWorkspace(true));
        RefreshLanguageToolsCommand = new AsyncRelayCommand(RefreshLanguageToolsAsync);
        RefreshExtensionsCommand = new AsyncRelayCommand(RefreshExtensionsAsync);
        SynchronizeProfileSyncSessionState();
    }

    public ApplicationPreferences General { get; }
    public ExplorerSettings Explorer { get; }
    public IReadOnlyList<TerminalProfile> TerminalProfiles { get; }
    public IReadOnlyList<string> TerminalFontFamilies { get; }
    public IReadOnlyList<NewTabPlacement> NewTabPlacementOptions { get; }
    public ObservableCollection<TerminalConfigurationProfileEditorViewModel> TerminalConfigurations { get; } = [];
    public ObservableCollection<ProjectEditorViewModel> Projects { get; } = [];
    public ObservableCollection<WorkspacePanelLayoutEditorViewModel> WorkspacePanels { get; } = [];
    public ObservableCollection<WorkspaceSideToolLayoutEditorViewModel>
        WorkspaceSideTools
    { get; } = [];
    public ObservableCollection<LanguageToolGroupViewModel> LanguageToolGroups { get; } = [];
    public ObservableCollection<ExtensionPackageEditorViewModel> ExtensionPackages { get; } = [];
    public ObservableCollection<ProfileImportDiagnostic> ProfileImportDiagnostics { get; } = [];
    public ObservableCollection<RemoteProfileEditorViewModel> RemoteProfiles { get; } = [];
    public LinkedHostSettingsViewModel LinkedHosts { get; }
    public ObservableCollection<WorkspaceAiAccessEditorViewModel>
        WorkspaceAiAccessEntries
    { get; } = [];
    public ObservableCollection<WorkspaceAiAuditEntryViewModel>
        RecentWorkspaceAiOperations
    { get; } = [];
    public IReadOnlyList<string> WorkspaceAiReasoningEfforts { get; } =
        ["none", "low", "medium", "high", "xhigh", "max"];
    public IReadOnlyList<string> WorkspaceAiTextVerbosities { get; } =
        ["low", "medium", "high"];
    public IReadOnlyList<WorkspaceAiProviderOption> WorkspaceAiProviderOptions
    { get; } =
    [
        new(
            WorkspaceAiProviderConfiguration.LlamaCppProviderId,
            "Local · Qwen Engineer"),
        new(
            WorkspaceAiProviderConfiguration.OpenAiProviderId,
            "OpenAI · existing cloud setup")
    ];
    public IReadOnlyList<WorkspaceAiModelOption> WorkspaceAiLocalModels { get; }
        = WorkspaceAiModelCatalog.Generative
            .Select(model => new WorkspaceAiModelOption(
                model.Id,
                model.DisplayName))
            .ToArray();
    public IReadOnlyList<WorkspaceAiTerminalExecutionPolicyOption>
        WorkspaceAiTerminalExecutionPolicies
    { get; } =
        WorkspaceAiTerminalExecutionPolicyOption.All;
    public IReadOnlyList<string> DeviceLocalProfileExclusions =>
        PortableProfileService.DeviceLocalExclusions;

    public string DefaultTerminalProfileId
    {
        get => _defaultTerminalProfileId;
        set => SetProperty(ref _defaultTerminalProfileId, value);
    }

    public string ExplorerStartupDirectory
    {
        get => Explorer.StartupDirectory;
        set
        {
            if (string.Equals(Explorer.StartupDirectory, value, StringComparison.Ordinal))
            {
                return;
            }

            Explorer.StartupDirectory = value;
            OnPropertyChanged();
        }
    }

    public TerminalConfigurationProfileEditorViewModel? SelectedTerminalConfiguration
    {
        get => _selectedTerminalConfiguration;
        set => SetProperty(ref _selectedTerminalConfiguration, value);
    }

    public ProjectEditorViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(HasSelectedProject));
                OnPropertyChanged(nameof(HasNoSelectedProject));
            }
        }
    }

    public bool HasSelectedProject => SelectedProject is not null;
    public bool HasNoSelectedProject => !HasSelectedProject;

    public bool OpenSelectedProjectOnStartup
    {
        get => _openSelectedProjectOnStartup;
        set => SetProperty(ref _openSelectedProjectOnStartup, value);
    }

    public string GeneralStatus
    {
        get => _generalStatus;
        private set => SetProperty(ref _generalStatus, value);
    }

    public string TerminalStatus
    {
        get => _terminalStatus;
        private set => SetProperty(ref _terminalStatus, value);
    }

    public string ExplorerStatus
    {
        get => _explorerStatus;
        private set => SetProperty(ref _explorerStatus, value);
    }

    public string WorkspaceStatus
    {
        get => _workspaceStatus;
        private set => SetProperty(ref _workspaceStatus, value);
    }

    public string LanguageToolsStatus
    {
        get => _languageToolsStatus;
        private set => SetProperty(ref _languageToolsStatus, value);
    }

    public string LanguageToolCatalogSummary
    {
        get => _languageToolCatalogSummary;
        private set => SetProperty(ref _languageToolCatalogSummary, value);
    }

    public string LanguageToolScanTimeText
    {
        get => _languageToolScanTimeText;
        private set => SetProperty(ref _languageToolScanTimeText, value);
    }

    public string ExtensionsStatus
    {
        get => _extensionsStatus;
        private set => SetProperty(ref _extensionsStatus, value);
    }

    public string ExtensionRootDirectory
    {
        get => _extensionRootDirectory;
        private set => SetProperty(ref _extensionRootDirectory, value);
    }

    public string ExtensionCatalogSummary
    {
        get => _extensionCatalogSummary;
        private set => SetProperty(ref _extensionCatalogSummary, value);
    }

    public string ExtensionScanTimeText
    {
        get => _extensionScanTimeText;
        private set => SetProperty(ref _extensionScanTimeText, value);
    }

    public string PortableProfileDisplayName
    {
        get => _portableProfileDisplayName;
        set
        {
            if (SetProperty(
                    ref _portableProfileDisplayName,
                    value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SuggestedPortableProfileFileName));
            }
        }
    }

    public string CurrentPortableProfileIdentity
    {
        get
        {
            var settings = _mainWindow.SettingsModel;
            return $"{settings.PortableProfileId}  •  revision {settings.PortableProfileRevision}";
        }
    }

    public string CurrentPortableProfileSummary
    {
        get
        {
            var settings = _mainWindow.SettingsModel;
            return $"{settings.Environments.Count} command-library configuration(s), "
                   + $"{settings.TerminalConfigurationProfiles.Count} terminal configuration(s), "
                   + $"{settings.ProfileExtensions.Count} extension subscription(s), and "
                   + $"{settings.WorkspaceLayout.Panels.Count} panel placement(s).";
        }
    }

    public string SuggestedPortableProfileFileName
    {
        get
        {
            var characters = PortableProfileDisplayName
                .Trim()
                .Select(character => Path.GetInvalidFileNameChars().Contains(character)
                    ? '-'
                    : character)
                .ToArray();
            var baseName = new string(characters).Trim(' ', '.', '-');
            return $"{(string.IsNullOrWhiteSpace(baseName) ? "hypershell-profile" : baseName)}{ProfileContract.FileExtension}";
        }
    }

    public string ProfileImportSourcePath
    {
        get => _profileImportSourcePath;
        private set => SetProperty(ref _profileImportSourcePath, value);
    }

    public string ProfileImportName
    {
        get => _profileImportName;
        private set => SetProperty(ref _profileImportName, value);
    }

    public string ProfileImportSummary
    {
        get => _profileImportSummary;
        private set => SetProperty(ref _profileImportSummary, value);
    }

    public string ProfileImportIntegrityText
    {
        get => _profileImportIntegrityText;
        private set => SetProperty(ref _profileImportIntegrityText, value);
    }

    public string ProfileStatus
    {
        get => _profileStatus;
        private set => SetProperty(ref _profileStatus, value);
    }

    public bool CanImportPortableProfile =>
        _profileImportPreview?.CanImport == true;

    public string ProfileSyncServerEndpoint
    {
        get => _profileSyncServerEndpoint;
        set => SetProperty(
            ref _profileSyncServerEndpoint,
            value ?? string.Empty);
    }

    public string ProfileSyncAccountId
    {
        get => _profileSyncAccountId;
        set => SetProperty(
            ref _profileSyncAccountId,
            value ?? string.Empty);
    }

    public string ProfileSyncStatus
    {
        get => _profileSyncStatus;
        private set => SetProperty(ref _profileSyncStatus, value);
    }

    public string ProfileSyncSessionIdentity =>
        _mainWindow.ProfileSyncSession.SessionIdentity;

    public bool IsProfileSyncSignedIn =>
        _mainWindow.ProfileSyncSession.IsSignedIn;

    public string PendingProfileDeviceCode
    {
        get => _pendingProfileDeviceCode;
        private set
        {
            if (SetProperty(ref _pendingProfileDeviceCode, value))
            {
                OnPropertyChanged(
                    nameof(HasPendingProfileDeviceAuthorization));
            }
        }
    }

    public string PendingProfileVerificationUri
    {
        get => _pendingProfileVerificationUri;
        private set => SetProperty(
            ref _pendingProfileVerificationUri,
            value);
    }

    public bool HasPendingProfileDeviceAuthorization =>
        !string.IsNullOrWhiteSpace(PendingProfileDeviceCode);

    public bool RememberProfileSyncAccessKey
    {
        get => _rememberProfileSyncAccessKey;
        set => SetProperty(
            ref _rememberProfileSyncAccessKey,
            value);
    }

    public string ProfileSyncCredentialStatus =>
        _mainWindow.ProfileSyncSession.CredentialStatus;

    public string ProfileConflictDirectory =>
        _mainWindow.ProfileConflictDirectory;

    public RemoteProfileEditorViewModel? SelectedRemoteProfile
    {
        get => _selectedRemoteProfile;
        set
        {
            if (SetProperty(ref _selectedRemoteProfile, value))
            {
                OnPropertyChanged(nameof(CanDownloadRemoteProfile));
                OnPropertyChanged(nameof(CanDeleteRemoteProfile));
            }
        }
    }

    public bool IsProfileSyncBusy
    {
        get => _isProfileSyncBusy
               || _mainWindow.ProfileSyncSession.IsBusy;
        private set
        {
            if (SetProperty(ref _isProfileSyncBusy, value))
            {
                OnPropertyChanged(nameof(CanConfigureProfileSync));
                OnPropertyChanged(nameof(CanAuthorizeProfileSync));
                OnPropertyChanged(nameof(CanSignInProfileSync));
                OnPropertyChanged(nameof(CanSignOutProfileSync));
                OnPropertyChanged(nameof(CanUseProfileSync));
                OnPropertyChanged(nameof(CanDownloadRemoteProfile));
                OnPropertyChanged(nameof(CanDeleteRemoteProfile));
            }
        }
    }

    public bool CanConfigureProfileSync =>
        !IsProfileSyncBusy && !IsProfileSyncSignedIn;

    public bool CanSignInProfileSync =>
        !IsProfileSyncBusy && !IsProfileSyncSignedIn;

    public bool CanAuthorizeProfileSync =>
        !IsProfileSyncBusy && !IsProfileSyncSignedIn;

    public bool CanSignOutProfileSync =>
        !IsProfileSyncBusy && IsProfileSyncSignedIn;

    public bool CanUseProfileSync =>
        !IsProfileSyncBusy && IsProfileSyncSignedIn;

    public bool CanDownloadRemoteProfile =>
        CanUseProfileSync
        && SelectedRemoteProfile is { IsDeleted: false };

    public bool CanDeleteRemoteProfile =>
        CanUseProfileSync
        && SelectedRemoteProfile is { IsDeleted: false };

    public string ApplicationUpdateStatus
    {
        get => _applicationUpdateStatus;
        private set => SetProperty(ref _applicationUpdateStatus, value);
    }

    public double ApplicationUpdateProgress
    {
        get => _applicationUpdateProgress;
        private set => SetProperty(ref _applicationUpdateProgress, value);
    }

    public bool IsApplicationUpdateBusy
    {
        get => _isApplicationUpdateBusy;
        private set
        {
            if (SetProperty(ref _isApplicationUpdateBusy, value))
            {
                NotifyApplicationUpdateActionsChanged();
            }
        }
    }

    public bool CanCheckForApplicationUpdate =>
        !IsApplicationUpdateBusy
        && IsProfileSyncSignedIn
        && _mainWindow.ApplicationUpdates.IsTrustConfigured;

    public bool CanDownloadApplicationUpdate =>
        CanCheckForApplicationUpdate
        && _applicationUpdateCandidate is not null;

    public bool CanInstallApplicationUpdate =>
        !IsApplicationUpdateBusy
        && _downloadedApplicationUpdate is not null;

    public WorkspaceAiAccessEditorViewModel? SelectedWorkspaceAiAccess
    {
        get => _selectedWorkspaceAiAccess;
        set => SetProperty(ref _selectedWorkspaceAiAccess, value);
    }

    public string WorkspaceAiStatus
    {
        get => _workspaceAiStatus;
        private set => SetProperty(ref _workspaceAiStatus, value);
    }

    public string WorkspaceAiProviderId
    {
        get => _workspaceAiProviderId;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (!SetProperty(ref _workspaceAiProviderId, normalized))
            {
                return;
            }

            if (IsWorkspaceAiLocalProvider)
            {
                WorkspaceAiProviderEndpoint =
                    WorkspaceAiProviderConfiguration
                        .LlamaCppResponsesEndpoint;
                WorkspaceAiProviderModel = _workspaceAiLocalModel;
                WorkspaceAiReasoningEffort =
                    _workspaceAiLocalReasoningEffort;
                WorkspaceAiTextVerbosity =
                    _workspaceAiLocalTextVerbosity;
                AllowWorkspaceAiCloudRequests = false;
            }
            else
            {
                WorkspaceAiProviderEndpoint =
                    WorkspaceAiProviderConfiguration
                        .OpenAiResponsesEndpoint;
                WorkspaceAiProviderModel = _workspaceAiOpenAiModel;
                WorkspaceAiReasoningEffort =
                    _workspaceAiOpenAiReasoningEffort;
                WorkspaceAiTextVerbosity =
                    _workspaceAiOpenAiTextVerbosity;
                AllowWorkspaceAiCloudRequests =
                    _workspaceAiOpenAiCloudRequestsEnabled;
            }

            OnPropertyChanged(nameof(IsWorkspaceAiLocalProvider));
            OnPropertyChanged(nameof(IsWorkspaceAiOpenAiProvider));
            OnPropertyChanged(nameof(WorkspaceAiLocalRuntimeStatus));
        }
    }

    public bool IsWorkspaceAiLocalProvider => string.Equals(
        WorkspaceAiProviderId,
        WorkspaceAiProviderConfiguration.LlamaCppProviderId,
        StringComparison.Ordinal);

    public bool IsWorkspaceAiOpenAiProvider => string.Equals(
        WorkspaceAiProviderId,
        WorkspaceAiProviderConfiguration.OpenAiProviderId,
        StringComparison.Ordinal);

    public bool IsWorkspaceAiLocalRuntimeInstalled =>
        _mainWindow.WorkspaceAiLocalRuntime.IsInstalled;

    public string WorkspaceAiLocalRuntimeStatus =>
        IsWorkspaceAiLocalRuntimeInstalled
            ? $"llama.cpp {WorkspaceAiLocalRuntimeService.ExpectedLlamaCppVersion} is installed. Models load on demand and stay on this PC."
            : $"llama.cpp {WorkspaceAiLocalRuntimeService.ExpectedLlamaCppVersion} is required before local models can run.";

    public string WorkspaceAiProviderModel
    {
        get => _workspaceAiProviderModel;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref _workspaceAiProviderModel, normalized))
            {
                return;
            }

            if (IsWorkspaceAiLocalProvider)
            {
                _workspaceAiLocalModel = normalized;
            }
            else if (IsWorkspaceAiOpenAiProvider)
            {
                _workspaceAiOpenAiModel = normalized;
            }
        }
    }

    public string WorkspaceAiReasoningEffort
    {
        get => _workspaceAiReasoningEffort;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref _workspaceAiReasoningEffort, normalized))
            {
                return;
            }

            if (IsWorkspaceAiLocalProvider)
            {
                _workspaceAiLocalReasoningEffort = normalized;
            }
            else if (IsWorkspaceAiOpenAiProvider)
            {
                _workspaceAiOpenAiReasoningEffort = normalized;
            }
        }
    }

    public string WorkspaceAiTextVerbosity
    {
        get => _workspaceAiTextVerbosity;
        set
        {
            var normalized = value ?? string.Empty;
            if (!SetProperty(ref _workspaceAiTextVerbosity, normalized))
            {
                return;
            }

            if (IsWorkspaceAiLocalProvider)
            {
                _workspaceAiLocalTextVerbosity = normalized;
            }
            else if (IsWorkspaceAiOpenAiProvider)
            {
                _workspaceAiOpenAiTextVerbosity = normalized;
            }
        }
    }

    public string WorkspaceAiProviderEndpoint
    {
        get => _workspaceAiProviderEndpoint;
        private set => SetProperty(
            ref _workspaceAiProviderEndpoint,
            value);
    }

    public bool AllowWorkspaceAiCloudRequests
    {
        get => _allowWorkspaceAiCloudRequests;
        set
        {
            if (SetProperty(ref _allowWorkspaceAiCloudRequests, value))
            {
                _workspaceAiOpenAiCloudRequestsEnabled = value;
            }
        }
    }

    public bool HasWorkspaceAiCredential
    {
        get => _hasWorkspaceAiCredential;
        private set
        {
            if (SetProperty(ref _hasWorkspaceAiCredential, value))
            {
                OnPropertyChanged(nameof(WorkspaceAiCredentialStatus));
            }
        }
    }

    public string WorkspaceAiCredentialStatus =>
        HasWorkspaceAiCredential
            ? "A DPAPI-protected API key is saved for this Windows user."
            : "No OpenAI API key is saved on this device.";

    public string WorkspaceAiProviderStatus
    {
        get => _workspaceAiProviderStatus;
        private set => SetProperty(
            ref _workspaceAiProviderStatus,
            value);
    }

    public string WorkspaceAiAutonomyContextName
    {
        get => _workspaceAiAutonomyContextName;
        private set => SetProperty(
            ref _workspaceAiAutonomyContextName,
            value);
    }

    public bool AllowWorkspaceAiAutonomousChanges
    {
        get => _allowWorkspaceAiAutonomousChanges;
        set
        {
            if (SetProperty(
                    ref _allowWorkspaceAiAutonomousChanges,
                    value))
            {
                OnPropertyChanged(
                    nameof(WorkspaceAiAutonomyRequiresConfirmation));
            }
        }
    }

    public bool WorkspaceAiAutonomyRequiresConfirmation
    {
        get
        {
            var contexts = _mainWindow.WorkspaceAiContextService;
            var selected = contexts.GetContext(contexts.SelectedContextId);
            return AllowWorkspaceAiAutonomousChanges
                   && selected is { AskBeforeChanges: true };
        }
    }

    public string WorkspaceAiAutonomyStatus
    {
        get => _workspaceAiAutonomyStatus;
        private set => SetProperty(
            ref _workspaceAiAutonomyStatus,
            value);
    }

    public string WorkspaceAiPermissionPath =>
        _mainWindow.WorkspaceAiPermissionBroker.StatePath;

    public string WorkspaceAiAuditPath =>
        _mainWindow.WorkspaceAiAuditService.AuditPath;

    public string WorkspaceAiProviderConfigurationPath =>
        _mainWindow.WorkspaceAiProviderConfigurationService
            .ConfigurationPath;

    public string WorkspaceAiProviderCredentialPath =>
        _mainWindow.WorkspaceAiCredentialStore.CredentialPath;

    public string NewWorkspaceAiRootName
    {
        get => _newWorkspaceAiRootName;
        set => SetProperty(
            ref _newWorkspaceAiRootName,
            value ?? string.Empty);
    }

    public string NewWorkspaceAiRootPath
    {
        get => _newWorkspaceAiRootPath;
        set
        {
            if (SetProperty(
                    ref _newWorkspaceAiRootPath,
                    value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanAddWorkspaceAiRoot));
            }
        }
    }

    public bool NewWorkspaceAiRootAllowRead
    {
        get => _newWorkspaceAiRootAllowRead;
        set
        {
            if (!SetProperty(ref _newWorkspaceAiRootAllowRead, value))
            {
                return;
            }

            if (!value && _newWorkspaceAiRootAllowWrite)
            {
                _newWorkspaceAiRootAllowWrite = false;
                OnPropertyChanged(nameof(NewWorkspaceAiRootAllowWrite));
            }

            if (!value
                && _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges)
            {
                _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges = false;
                OnPropertyChanged(
                    nameof(NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges));
            }

            NotifyNewWorkspaceAiRootCapabilityState();
        }
    }

    public bool NewWorkspaceAiRootAllowWrite
    {
        get => _newWorkspaceAiRootAllowWrite;
        set
        {
            if (!SetProperty(ref _newWorkspaceAiRootAllowWrite, value))
            {
                return;
            }

            if (value && !_newWorkspaceAiRootAllowRead)
            {
                _newWorkspaceAiRootAllowRead = true;
                OnPropertyChanged(nameof(NewWorkspaceAiRootAllowRead));
            }

            if (!value
                && _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges)
            {
                _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges = false;
                OnPropertyChanged(
                    nameof(NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges));
            }

            NotifyNewWorkspaceAiRootCapabilityState();
        }
    }

    public bool NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges
    {
        get => _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges;
        set
        {
            if (!SetProperty(
                    ref _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges,
                    value))
            {
                return;
            }

            if (value && !_newWorkspaceAiRootAllowWrite)
            {
                _newWorkspaceAiRootAllowWrite = true;
                _newWorkspaceAiRootAllowRead = true;
                OnPropertyChanged(nameof(NewWorkspaceAiRootAllowWrite));
                OnPropertyChanged(nameof(NewWorkspaceAiRootAllowRead));
            }

            NotifyNewWorkspaceAiRootCapabilityState();
        }
    }

    public bool NewWorkspaceAiRootAllowTerminal
    {
        get => _newWorkspaceAiRootAllowTerminal;
        set
        {
            if (SetProperty(ref _newWorkspaceAiRootAllowTerminal, value))
            {
                if (!value)
                {
                    NewWorkspaceAiRootTerminalExecutionPolicy =
                        WorkspaceAiTerminalExecutionPolicy
                            .ConfirmEveryCommand;
                }

                NotifyNewWorkspaceAiRootCapabilityState();
            }
        }
    }

    public WorkspaceAiTerminalExecutionPolicy
        NewWorkspaceAiRootTerminalExecutionPolicy
    {
        get => _newWorkspaceAiRootTerminalExecutionPolicy;
        set
        {
            if (SetProperty(
                    ref _newWorkspaceAiRootTerminalExecutionPolicy,
                    value))
            {
                OnPropertyChanged(
                    nameof(NewWorkspaceAiRootTerminalExecutionPolicyDescription));
                OnPropertyChanged(nameof(NewWorkspaceAiRootCapabilitySummary));
                OnPropertyChanged(
                    nameof(NewWorkspaceAiRootAllowAutonomousActions));
            }
        }
    }

    public string NewWorkspaceAiRootTerminalExecutionPolicyDescription =>
        WorkspaceAiTerminalExecutionPolicyOption.Describe(
            NewWorkspaceAiRootTerminalExecutionPolicy);

    public bool NewWorkspaceAiRootAllowTerminalRead
    {
        get => _newWorkspaceAiRootAllowTerminalRead;
        set
        {
            if (SetProperty(
                    ref _newWorkspaceAiRootAllowTerminalRead,
                    value))
            {
                NotifyNewWorkspaceAiRootCapabilityState();
            }
        }
    }

    public bool NewWorkspaceAiRootAllowAutonomousActions
    {
        get => NewWorkspaceAiRootAllowWrite
               && NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges
               && NewWorkspaceAiRootAllowTerminal
               && NewWorkspaceAiRootTerminalExecutionPolicy
                   == WorkspaceAiTerminalExecutionPolicy.AutoExecute;
        set
        {
            if (value == NewWorkspaceAiRootAllowAutonomousActions)
            {
                return;
            }

            if (value)
            {
                _newWorkspaceAiRootAllowRead = true;
                _newWorkspaceAiRootAllowWrite = true;
                _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges = true;
                _newWorkspaceAiRootAllowTerminalRead = true;
                _newWorkspaceAiRootAllowTerminal = true;
                _newWorkspaceAiRootTerminalExecutionPolicy =
                    WorkspaceAiTerminalExecutionPolicy.AutoExecute;
            }
            else
            {
                _newWorkspaceAiRootAllowAutomaticFileAndEditorChanges = false;
                if (_newWorkspaceAiRootTerminalExecutionPolicy
                    == WorkspaceAiTerminalExecutionPolicy.AutoExecute)
                {
                    _newWorkspaceAiRootTerminalExecutionPolicy =
                        WorkspaceAiTerminalExecutionPolicy
                            .ConfirmEveryCommand;
                }
            }

            OnPropertyChanged(nameof(NewWorkspaceAiRootAllowRead));
            OnPropertyChanged(nameof(NewWorkspaceAiRootAllowWrite));
            OnPropertyChanged(
                nameof(NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges));
            OnPropertyChanged(nameof(NewWorkspaceAiRootAllowTerminalRead));
            OnPropertyChanged(nameof(NewWorkspaceAiRootAllowTerminal));
            OnPropertyChanged(
                nameof(NewWorkspaceAiRootTerminalExecutionPolicy));
            OnPropertyChanged(
                nameof(NewWorkspaceAiRootTerminalExecutionPolicyDescription));
            NotifyNewWorkspaceAiRootCapabilityState();
        }
    }

    public bool CanAddWorkspaceAiRoot =>
        Directory.Exists(NewWorkspaceAiRootPath)
        && (NewWorkspaceAiRootAllowRead
            || NewWorkspaceAiRootAllowWrite
            || NewWorkspaceAiRootAllowTerminalRead
            || NewWorkspaceAiRootAllowTerminal);

    public string NewWorkspaceAiRootCapabilitySummary =>
        BuildWorkspaceAiCapabilitySummary(
            NewWorkspaceAiRootAllowRead,
            NewWorkspaceAiRootAllowWrite,
            NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges,
            NewWorkspaceAiRootAllowTerminalRead,
            NewWorkspaceAiRootAllowTerminal,
            NewWorkspaceAiRootTerminalExecutionPolicy);

    public RelayCommand AddTerminalConfigurationCommand { get; }
    public RelayCommand DuplicateTerminalConfigurationCommand { get; }
    public RelayCommand DeleteTerminalConfigurationCommand { get; }
    public RelayCommand SelectAllTerminalTargetsCommand { get; }
    public RelayCommand ClearTerminalTargetsCommand { get; }
    public RelayCommand ApplyGeneralCommand { get; }
    public RelayCommand SaveGeneralCommand { get; }
    public RelayCommand ApplyTerminalCommand { get; }
    public RelayCommand SaveTerminalCommand { get; }
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand DuplicateProjectCommand { get; }
    public RelayCommand DeleteProjectCommand { get; }
    public RelayCommand ApplyExplorerCommand { get; }
    public RelayCommand SaveExplorerCommand { get; }
    public RelayCommand ResetWorkspaceCommand { get; }
    public RelayCommand CancelWorkspacePreviewCommand { get; }
    public RelayCommand ApplyWorkspaceCommand { get; }
    public RelayCommand SaveWorkspaceCommand { get; }
    public AsyncRelayCommand RefreshLanguageToolsCommand { get; }
    public AsyncRelayCommand RefreshExtensionsCommand { get; }

    public Task<HyperlibPackageInstallPreview> InspectHyperlibPackageAsync(
        string sourcePath) =>
        _mainWindow.InspectHyperlibPackageAsync(sourcePath);

    public async Task<HyperlibPackageInstallResult> InstallHyperlibPackageAsync(
        string sourcePath,
        bool allowUpdate)
    {
        ExtensionsStatus = allowUpdate
            ? "Validating and updating the selected Hyperlib package…"
            : "Validating and installing the selected Hyperlib package…";
        try
        {
            var result = await _mainWindow.InstallHyperlibPackageAsync(
                sourcePath,
                allowUpdate);
            LoadExtensionCatalog(_mainWindow.ExtensionCatalog);
            LoadWorkspaceDraft(_mainWindow.WorkspaceLayout);
            ExtensionsStatus = result.Disposition switch
            {
                HyperlibPackageInstallDisposition.AlreadyInstalled =>
                    $"{result.DisplayName} {result.Version} is already installed with the same content hash.",
                HyperlibPackageInstallDisposition.Update =>
                    $"Updated {result.DisplayName} to {result.Version}. The previous package was moved to the recovery folder.",
                _ => $"Installed {result.DisplayName} {result.Version} and activated its validated static content."
            };
            return result;
        }
        catch (Exception exception)
        {
            ExtensionsStatus = $"Hyperlib install failed: {exception.Message}";
            throw;
        }
    }

    public async Task<HyperlibPackageArchiveResult> PackHyperlibPackageAsync(
        string sourceDirectory,
        string destinationPath)
    {
        ExtensionsStatus =
            "Validating the source folder and building a deterministic Hyperlib archive…";
        try
        {
            var result = await _mainWindow.PackHyperlibPackageAsync(
                sourceDirectory,
                destinationPath);
            ExtensionsStatus =
                $"Packed {result.DisplayName} {result.Version} into {result.ArchivePath}.";
            return result;
        }
        catch (Exception exception)
        {
            ExtensionsStatus = $"Hyperlib packing failed: {exception.Message}";
            throw;
        }
    }

    public async Task<HyperlibPackageRemovalResult> RemoveHyperlibPackageAsync(
        ExtensionPackageEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ExtensionsStatus =
            $"Removing {editor.DisplayName} from the active extension catalog…";
        try
        {
            var result = await _mainWindow.RemoveHyperlibPackageAsync(
                editor.Package);
            LoadExtensionCatalog(_mainWindow.ExtensionCatalog);
            LoadWorkspaceDraft(_mainWindow.WorkspaceLayout);
            ExtensionsStatus =
                $"Removed {result.DisplayName}. Its files were moved to {result.RecoveryDirectory} for recovery.";
            return result;
        }
        catch (Exception exception)
        {
            ExtensionsStatus = $"Hyperlib removal failed: {exception.Message}";
            throw;
        }
    }

    public void RefreshWorkspaceAiAccess()
    {
        LoadWorkspaceAiAccess();
        LoadWorkspaceAiProviderSettings();
        LoadWorkspaceAiAutonomySettings();
        RefreshWorkspaceAiAssistants();
    }

    public void SaveWorkspaceAiAutonomy(bool riskConfirmed)
    {
        try
        {
            var contexts = _mainWindow.WorkspaceAiContextService;
            var selected = contexts.GetContext(contexts.SelectedContextId);
            if (selected is null)
            {
                WorkspaceAiAutonomyStatus =
                    "The selected AI Context is no longer available.";
                return;
            }

            if (AllowWorkspaceAiAutonomousChanges
                && selected.AskBeforeChanges
                && !riskConfirmed)
            {
                WorkspaceAiAutonomyStatus =
                    "Confirm the autonomy warning before enabling this setting.";
                return;
            }

            var saved = contexts.SaveContext(
                selected with
                {
                    AskBeforeChanges =
                        !AllowWorkspaceAiAutonomousChanges,
                    RiskWarningAcknowledged =
                        AllowWorkspaceAiAutonomousChanges
                            ? selected.RiskWarningAcknowledged
                              || riskConfirmed
                            : selected.RiskWarningAcknowledged
                },
                select: false);
            LoadWorkspaceAiAutonomySettings();
            RefreshWorkspaceAiAssistants();
            WorkspaceAiAutonomyStatus = saved.AskBeforeChanges
                ? $"{saved.Name} now reviews permitted changes and terminal executions before they run."
                : $"{saved.Name} now lets Engineer apply permitted changes and execute terminal commands without asking.";
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            WorkspaceAiAutonomyStatus =
                $"The Context autonomy setting was not saved: {exception.Message}";
        }
    }

    public void SaveWorkspaceAiAccess(string? apiKey)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _mainWindow.WorkspaceAiCredentialStore.Save(apiKey);
            }

            var current = _mainWindow
                .WorkspaceAiProviderConfigurationService
                .Load();
            var saved = _mainWindow
                .WorkspaceAiProviderConfigurationService
                .Save(current with
                {
                    AllowCloudRequests =
                        !current.IsLocal
                        && AllowWorkspaceAiCloudRequests,
                    OpenAiCloudRequestsEnabled =
                        AllowWorkspaceAiCloudRequests
                });
            ApplyWorkspaceAiProviderConfiguration(saved);
            HasWorkspaceAiCredential =
                _mainWindow.WorkspaceAiCredentialStore.Exists;
            WorkspaceAiProviderStatus = BuildWorkspaceAiAccessStatus(saved);
            RefreshWorkspaceAiAssistants();
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or WorkspaceAiProviderConfigurationException)
        {
            HasWorkspaceAiCredential =
                _mainWindow.WorkspaceAiCredentialStore.Exists;
            WorkspaceAiProviderStatus =
                $"AI access was not fully saved: {exception.Message}";
        }
    }

    public void ForgetWorkspaceAiCredential()
    {
        try
        {
            var current = _mainWindow
                .WorkspaceAiProviderConfigurationService
                .Load();
            var saved = _mainWindow
                .WorkspaceAiProviderConfigurationService
                .Save(current with
                {
                    AllowCloudRequests = false,
                    OpenAiCloudRequestsEnabled = false
                });
            _mainWindow.WorkspaceAiCredentialStore.Delete();
            ApplyWorkspaceAiProviderConfiguration(saved);
            HasWorkspaceAiCredential = false;
            WorkspaceAiProviderStatus =
                "Removed the device-protected OpenAI API key and disabled cloud requests.";
            RefreshWorkspaceAiAssistants();
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or WorkspaceAiProviderConfigurationException)
        {
            HasWorkspaceAiCredential =
                _mainWindow.WorkspaceAiCredentialStore.Exists;
            WorkspaceAiProviderStatus =
                $"The OpenAI credential could not be fully removed: {exception.Message}";
        }
    }

    public void ApproveSelectedWorkspaceAiAccess()
    {
        if (SelectedWorkspaceAiAccess is not { CanApprove: true } selected)
        {
            WorkspaceAiStatus =
                "Choose an available workspace and at least one capability before saving approval.";
            return;
        }

        var selectedId = selected.WorkspaceId;
        try
        {
            _mainWindow.WorkspaceAiPermissionBroker.Approve(
                selected.WorkspaceId,
                selected.RootPath,
                selected.AllowRead,
                selected.AllowWrite,
                selected.AllowTerminal,
                allowNetwork: false,
                displayName: selected.DisplayName,
                allowTerminalRead: selected.AllowTerminalRead,
                terminalExecutionPolicy:
                    selected.TerminalExecutionPolicy,
                allowAutomaticFileAndEditorChanges:
                    selected.AllowAutomaticFileAndEditorChanges);
            LoadWorkspaceAiAccess(selectedId);
            _mainWindow.WorkspaceAiAssistant.RefreshState();
            WorkspaceAiStatus =
                "Saved the device-local workspace approval. It is not part of a portable or synchronized profile.";
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            WorkspaceAiStatus =
                $"The workspace approval was not saved: {exception.Message}";
        }
    }

    public void AddWorkspaceAiRoot()
    {
        if (!CanAddWorkspaceAiRoot)
        {
            WorkspaceAiStatus =
                "Choose an existing folder and at least one capability before adding an AI root.";
            return;
        }

        try
        {
            var rootPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(NewWorkspaceAiRootPath.Trim()));
            var displayName = NewWorkspaceAiRootName.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = rootPath;
            }

            var workspaceId = WorkspaceAiPermissionBroker.CreateWorkspaceId(
                "manual",
                rootPath);
            _mainWindow.WorkspaceAiPermissionBroker.Approve(
                workspaceId,
                rootPath,
                NewWorkspaceAiRootAllowRead,
                NewWorkspaceAiRootAllowWrite,
                NewWorkspaceAiRootAllowTerminal,
                allowNetwork: false,
                displayName: displayName,
                allowTerminalRead: NewWorkspaceAiRootAllowTerminalRead,
                terminalExecutionPolicy:
                    NewWorkspaceAiRootTerminalExecutionPolicy,
                allowAutomaticFileAndEditorChanges:
                    NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges);
            LoadWorkspaceAiAccess(workspaceId);
            _mainWindow.WorkspaceAiAssistant.RefreshState();
            NewWorkspaceAiRootName = string.Empty;
            NewWorkspaceAiRootPath = string.Empty;
            WorkspaceAiStatus =
                $"Added '{displayName}' as a device-local AI root. Other approved roots remain available.";
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            WorkspaceAiStatus =
                $"The AI root was not added: {exception.Message}";
        }
    }

    public IReadOnlyList<string> GetReadyLocalFixedDriveRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed
                    && drive.IsReady
                    && Directory.Exists(drive.RootDirectory.FullName))
                {
                    roots.Add(Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(drive.RootDirectory.FullName)));
                }
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
            }
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetUpWorkspaceAiAccess(
        IReadOnlyList<string> rootPaths,
        bool fullDevice)
    {
        ArgumentNullException.ThrowIfNull(rootPaths);
        var normalizedRoots = rootPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(path)))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedRoots.Count == 0)
        {
            WorkspaceAiStatus = fullDevice
                ? "No ready local fixed drives were available for full-device AI access."
                : "The selected AI access folder is not available.";
            return;
        }

        var existingApprovals = _mainWindow.WorkspaceAiPermissionBroker
            .GetApprovals();
        var savedIds = new List<string>();
        var failures = new List<string>();
        foreach (var rootPath in normalizedRoots)
        {
            try
            {
                var existing = existingApprovals.FirstOrDefault(approval =>
                    string.Equals(
                        Path.TrimEndingDirectorySeparator(
                            approval.LocalRootPath),
                        rootPath,
                        StringComparison.OrdinalIgnoreCase));
                var workspaceId = existing?.WorkspaceId
                    ?? WorkspaceAiPermissionBroker.CreateWorkspaceId(
                        fullDevice ? "device-drive" : "setup-folder",
                        rootPath);
                var directoryName = Path.GetFileName(rootPath);
                var displayName = !string.IsNullOrWhiteSpace(
                    existing?.DisplayName)
                    ? existing.DisplayName
                    : fullDevice
                        ? $"This device — {rootPath}"
                        : string.IsNullOrWhiteSpace(directoryName)
                            ? rootPath
                            : directoryName;
                _mainWindow.WorkspaceAiPermissionBroker.Approve(
                    workspaceId,
                    rootPath,
                    allowRead: true,
                    allowWrite: true,
                    allowTerminal: true,
                    allowNetwork: false,
                    displayName: displayName,
                    allowTerminalRead: true,
                    terminalExecutionPolicy:
                        existing?.TerminalExecutionPolicy
                        ?? WorkspaceAiTerminalExecutionPolicy
                            .ConfirmEveryCommand,
                    allowAutomaticFileAndEditorChanges:
                        existing?.AllowAutomaticFileAndEditorChanges
                        ?? false);
                savedIds.Add(workspaceId);
            }
            catch (Exception exception) when (exception is ArgumentException
                                             or IOException
                                             or UnauthorizedAccessException
                                             or InvalidDataException
                                             or InvalidOperationException
                                             or NotSupportedException)
            {
                failures.Add($"{rootPath}: {exception.Message}");
            }
        }

        LoadWorkspaceAiAccess(savedIds.FirstOrDefault());
        _mainWindow.WorkspaceAiAssistant.RefreshState();
        _mainWindow.WorkspaceAiAssistant.SelectWorkspaceForPath(
            _mainWindow.ActiveWorkspaceTab switch
            {
                DocumentTabViewModel { HasFilePath: true } document =>
                    document.FilePath,
                TerminalTabViewModel terminal => terminal.WorkingDirectory,
                _ => null
            });
        WorkspaceAiStatus = savedIds.Count == 0
            ? $"AI access setup could not save any roots. {string.Join(" ", failures)}"
            : fullDevice
                ? $"Full-device AI access now covers {savedIds.Count} ready local fixed drive(s)."
                  + (failures.Count == 0
                      ? string.Empty
                      : $" {failures.Count} drive(s) could not be approved.")
                : "Restricted AI access to the selected folder. Explorer location remains independent.";
    }

    public void RevokeSelectedWorkspaceAiAccess()
    {
        if (SelectedWorkspaceAiAccess is not { IsApproved: true } selected)
        {
            WorkspaceAiStatus = "The selected workspace is not approved.";
            return;
        }

        var selectedId = selected.WorkspaceId;
        try
        {
            _mainWindow.WorkspaceAiPermissionBroker.Revoke(selectedId);
            LoadWorkspaceAiAccess(selectedId);
            _mainWindow.WorkspaceAiAssistant.RefreshState();
            WorkspaceAiStatus =
                "Revoked the device-local AI approval. Future operations for this workspace are denied.";
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException)
        {
            WorkspaceAiStatus =
                $"The workspace approval could not be revoked: {exception.Message}";
        }
    }

    public void SetTerminalBackgroundImage(string path)
    {
        if (SelectedTerminalConfiguration is not null)
        {
            SelectedTerminalConfiguration.Settings.BackgroundImagePath = path;
        }
    }

    public void AddProjectDirectory(string path)
    {
        if (SelectedProject is null)
        {
            ExplorerStatus = "Create or select a project before adding folders.";
            return;
        }

        var normalizedPath = NormalizeProjectLocation(path);
        if (SelectedProject.Directories.Contains(
                normalizedPath,
                StringComparer.OrdinalIgnoreCase))
        {
            ExplorerStatus = "That folder is already part of the selected project.";
            return;
        }

        SelectedProject.Directories.Add(normalizedPath);
        ExplorerStatus = $"Added {normalizedPath} to {SelectedProject.Name}.";
    }

    public void AddProject(ProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var editor = new ProjectEditorViewModel(project);
        Projects.Add(editor);
        SelectedProject = editor;
        OpenSelectedProjectOnStartup = true;
        ExplorerStatus = $"Created {editor.Name} in this draft. Apply or Save to update the Explorer.";
    }

    public void RemoveProjectDirectory(string? path)
    {
        if (SelectedProject is not null && path is not null)
        {
            SelectedProject.Directories.Remove(path);
        }
    }

    public void ApproveExtension(ExtensionPackageEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        try
        {
            _mainWindow.ApproveWebPanels(editor.Package);
            RefreshExtensionAndWorkspaceDrafts();
            ExtensionsStatus =
                "Approved this exact package version and content hash for local WebView panels on this device. No file, terminal, network, secret, or AI access was granted.";
        }
        catch (InvalidOperationException exception)
        {
            ExtensionsStatus = exception.Message;
        }
    }

    public void RevokeExtension(ExtensionPackageEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _mainWindow.RevokeWebPanels(editor.Package);
        RefreshExtensionAndWorkspaceDrafts();
        ExtensionsStatus =
            "Revoked the device-local approval and restored any built-in surface this package had replaced.";
    }

    public void PreviewExtension(
        ExtensionPackageEditorViewModel editor,
        ExtensionPanelActivationTarget target)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var slot = target switch
        {
            ExtensionPanelActivationTarget.Left => WorkspacePanelSlot.Left,
            ExtensionPanelActivationTarget.Right => WorkspacePanelSlot.Right,
            ExtensionPanelActivationTarget.Floating => WorkspacePanelSlot.Floating,
            _ => WorkspacePanelSlot.Bottom
        };
        _mainWindow.PreviewExtensionPanel(
            editor.Package,
            replacement: null,
            slot,
            out var statusMessage);
        LoadWorkspaceDraft(_mainWindow.WorkspaceLayout);
        LoadExtensionCatalog(_mainWindow.ExtensionCatalog);
        ExtensionsStatus = statusMessage;
    }

    public void DeactivateExtension(ExtensionPackageEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _mainWindow.DeactivateExtensionPanels(editor.Package);
        LoadWorkspaceDraft(_mainWindow.WorkspaceLayout);
        LoadExtensionCatalog(_mainWindow.ExtensionCatalog);
        ExtensionsStatus =
            "Closed this package's panels for the current session. Save Workspace Layout if you want that inactive state restored next time.";
    }

    public bool ExportPortableProfile(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(PortableProfileDisplayName))
        {
            ProfileStatus = "Give the portable profile a display name before exporting it.";
            return false;
        }

        try
        {
            var envelope = _mainWindow.ExportPortableProfile(
                destinationPath,
                PortableProfileDisplayName);
            ProfileStatus =
                $"Exported '{envelope.Profile.DisplayName}' with verified SHA-256 integrity. No device-local state was included.";
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or ArgumentException)
        {
            ProfileStatus = $"Export failed: {exception.Message}";
            return false;
        }
    }

    public void InspectPortableProfile(string sourcePath)
    {
        try
        {
            _pendingRemoteDownload = null;
            LoadProfileImportPreview(
                _mainWindow.InspectPortableProfile(sourcePath));
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or ArgumentException)
        {
            _profileImportPreview = null;
            ProfileImportDiagnostics.Clear();
            ProfileImportSourcePath = sourcePath;
            ProfileImportName = "Profile inspection failed";
            ProfileImportIntegrityText = string.Empty;
            ProfileImportSummary = exception.Message;
            ProfileStatus = "Nothing was applied.";
        }

        OnPropertyChanged(nameof(CanImportPortableProfile));
    }

    public async Task ApplyPortableProfileAsync(bool persist)
    {
        if (_profileImportPreview?.CanImport != true)
        {
            ProfileStatus = "Choose a portable profile that passes validation first.";
            return;
        }

        try
        {
            await _mainWindow.ApplyPortableProfileAsync(
                _profileImportPreview,
                persist);
            var acceptedRemoteCheckpoint = false;
            var attemptedRemoteCheckpoint = false;
            var remoteCheckpointFailure = string.Empty;
            var roamingRestoreStatus = string.Empty;
            if (persist
                && _pendingRemoteDownload is { } remoteDownload
                && ReferenceEquals(
                    remoteDownload.Preview,
                    _profileImportPreview))
            {
                attemptedRemoteCheckpoint = true;
                try
                {
                    _mainWindow.AcceptDownloadedProfile(remoteDownload);
                    try
                    {
                        var restored = await _mainWindow
                            .RestoreCloudProfileResourcesAsync(
                                remoteDownload.ProfileId);
                        roamingRestoreStatus =
                            $" Restored {restored.Packages.Downloaded} private package(s) and {restored.Conversations.DownloadedConversations} conversation(s).";
                        if (restored.Packages.HasConflicts
                            || restored.Conversations.ConflictPaths.Count > 0)
                        {
                            roamingRestoreStatus +=
                                " Conflicting local and remote resources were preserved for review.";
                        }
                    }
                    catch (ProfileSyncException exception) when (
                        exception.Kind == ProfileSyncFailureKind.NotFound)
                    {
                        roamingRestoreStatus =
                            " This legacy profile has no v2 roaming adjunct yet; Sync Now will create one.";
                    }

                    acceptedRemoteCheckpoint = true;
                }
                catch (Exception exception) when (IsProfileSyncException(exception))
                {
                    remoteCheckpointFailure = exception.Message;
                }
                finally
                {
                    _pendingRemoteDownload = null;
                }
            }

            if (persist && _profileImportPreview.Envelope is { } envelope)
            {
                PortableProfileDisplayName = envelope.Profile.DisplayName;
                OnPropertyChanged(nameof(CurrentPortableProfileIdentity));
                OnPropertyChanged(nameof(CurrentPortableProfileSummary));
            }

            ProfileStatus = persist
                ? acceptedRemoteCheckpoint
                    ? "Saved the downloaded revision as the current profile and advanced its conditional-sync checkpoint. Device-local state was preserved."
                      + roamingRestoreStatus
                    : attemptedRemoteCheckpoint
                        ? "Saved the downloaded profile locally, but did not advance its sync checkpoint because the saved device-compatible profile no longer exactly matches the remote hash or the checkpoint file could not be updated. Download it again before synchronizing. "
                          + remoteCheckpointFailure
                    : "Saved as the current portable profile. Local paths, approvals, credentials, sessions, and recovery data were preserved."
                : "Applied appearance, terminal behavior, Explorer display preferences, and panel layout for this session. Save to adopt command-library state and subscriptions.";
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or ArgumentException
                                         or ProfileSyncException)
        {
            ProfileStatus = $"Import failed without changing the profile: {exception.Message}";
        }
    }

    public void SaveProfileSyncConfiguration()
    {
        try
        {
            _mainWindow.SaveProfileSyncConfiguration(
                new ProfileSyncDeviceConfiguration
                {
                    ServerEndpoint = ProfileSyncServerEndpoint.Trim(),
                    AccountId = ProfileSyncAccountId.Trim()
                });
            SynchronizeProfileSyncSessionState();
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidOperationException
                                         or ArgumentException
                                         or ProfileSyncException)
        {
            ProfileSyncStatus =
                $"The device-local sync configuration was not saved: {exception.Message}";
        }
    }

    public async Task<bool> SignInProfileSyncAsync(string accessKey)
    {
        if (IsProfileSyncBusy || IsProfileSyncSignedIn)
        {
            return false;
        }

        IsProfileSyncBusy = true;
        ProfileSyncStatus =
            "Exchanging the account access key for a short-lived profile session…";
        try
        {
            _ = await _mainWindow.SignInProfileSyncAsync(
                ProfileSyncServerEndpoint,
                ProfileSyncAccountId,
                accessKey,
                RememberProfileSyncAccessKey);
            ProfileSyncServerEndpoint =
                _mainWindow.ProfileSyncSession.Configuration.ServerEndpoint;
            ProfileSyncAccountId =
                _mainWindow.ProfileSyncSession.Configuration.AccountId;
            SynchronizeProfileSyncSessionState();
            return true;
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            ProfileSyncStatus =
                $"Profile sign-in failed without changing local profile content: {exception.Message}";
            return false;
        }
        finally
        {
            IsProfileSyncBusy = false;
        }
    }

    public async Task<bool> AuthorizeProfileSyncDeviceAsync(
        Action<DeviceAuthorizationResponse> authorizationRequired,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizationRequired);
        if (IsProfileSyncBusy || IsProfileSyncSignedIn)
        {
            return false;
        }

        IsProfileSyncBusy = true;
        ProfileSyncStatus =
            "Opening secure Atlas sign-in in your browser…";
        try
        {
            _ = await _mainWindow.AuthorizeProfileSyncDeviceAsync(
                ProfileSyncServerEndpoint,
                challenge =>
                {
                    PendingProfileDeviceCode = challenge.UserCode;
                    PendingProfileVerificationUri =
                        HttpCloudDeviceAuthorizationClient
                            .CreateCompleteVerificationUri(challenge)
                            .AbsoluteUri;
                    ProfileSyncStatus =
                        "Finish signing in with your password and authenticator. Hypershell will continue automatically.";
                    authorizationRequired(challenge);
                },
                cancellationToken);
            ProfileSyncServerEndpoint =
                _mainWindow.ProfileSyncSession.Configuration.ServerEndpoint;
            ProfileSyncAccountId =
                _mainWindow.ProfileSyncSession.Configuration.AccountId;
            SynchronizeProfileSyncSessionState();
            return true;
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            ProfileSyncStatus = exception is OperationCanceledException
                or ProfileSyncException
            { Kind: ProfileSyncFailureKind.Cancelled }
                ? "Browser sign-in was cancelled."
                : $"Browser sign-in failed: {exception.Message}";
            return false;
        }
        finally
        {
            PendingProfileDeviceCode = string.Empty;
            PendingProfileVerificationUri = string.Empty;
            IsProfileSyncBusy = false;
        }
    }

    public async Task SignOutProfileSyncAsync()
    {
        if (IsProfileSyncBusy || !IsProfileSyncSignedIn)
        {
            return;
        }

        IsProfileSyncBusy = true;
        try
        {
            await _mainWindow.SignOutProfileSyncAsync(
                forgetCredential: true);
            ProfileSyncStatus =
                _mainWindow.ProfileSyncSession.StatusText;
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            ProfileSyncStatus = exception.Message;
        }
        finally
        {
            RemoteProfiles.Clear();
            SelectedRemoteProfile = null;
            IsProfileSyncBusy = false;
        }
    }

    public async Task RefreshRemoteProfilesAsync()
    {
        if (IsProfileSyncBusy)
        {
            return;
        }

        IsProfileSyncBusy = true;
        ProfileSyncStatus = "Requesting remote profile metadata…";
        try
        {
            await RefreshRemoteProfilesCoreAsync();
            ProfileSyncStatus = RemoteProfiles.Count == 0
                ? "The authenticated account has no remote profile records. Upload Current to create one."
                : $"Loaded {RemoteProfiles.Count} remote profile record(s). No profile content was downloaded.";
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            HandleProfileSyncFailure(
                "Remote profile refresh failed without changing local state",
                exception);
        }
        finally
        {
            IsProfileSyncBusy = false;
        }
    }

    public async Task CheckForApplicationUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanCheckForApplicationUpdate)
        {
            return;
        }

        IsApplicationUpdateBusy = true;
        ApplicationUpdateProgress = 0;
        ApplicationUpdateStatus = "Checking the private stable channel…";
        try
        {
            var outcome = await _mainWindow
                .CheckForApplicationUpdateAsync(cancellationToken);
            _applicationUpdateCandidate = outcome.Candidate;
            _downloadedApplicationUpdate = null;
            ApplicationUpdateStatus = outcome.Message;
        }
        catch (Exception exception) when (IsApplicationUpdateException(
                                             exception))
        {
            _applicationUpdateCandidate = null;
            _downloadedApplicationUpdate = null;
            ApplicationUpdateStatus =
                $"Update check failed: {exception.Message}";
        }
        finally
        {
            IsApplicationUpdateBusy = false;
            NotifyApplicationUpdateActionsChanged();
        }
    }

    public async Task DownloadApplicationUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanDownloadApplicationUpdate
            || _applicationUpdateCandidate is null)
        {
            return;
        }

        IsApplicationUpdateBusy = true;
        ApplicationUpdateProgress = 0;
        ApplicationUpdateStatus =
            $"Downloading Hypershell {_applicationUpdateCandidate.Manifest.Version}…";
        try
        {
            var progress = new Progress<double>(value =>
                ApplicationUpdateProgress =
                    Math.Clamp(value, 0, 1) * 100);
            _downloadedApplicationUpdate =
                await _mainWindow.DownloadApplicationUpdateAsync(
                    _applicationUpdateCandidate,
                    progress,
                    cancellationToken);
            ApplicationUpdateProgress = 100;
            ApplicationUpdateStatus =
                _downloadedApplicationUpdate.UsedExistingDownload
                    ? "The previously verified installer is ready."
                    : "The signed installer is downloaded and ready.";
        }
        catch (Exception exception) when (IsApplicationUpdateException(
                                             exception))
        {
            _downloadedApplicationUpdate = null;
            ApplicationUpdateStatus =
                $"Update download failed: {exception.Message}";
        }
        finally
        {
            IsApplicationUpdateBusy = false;
            NotifyApplicationUpdateActionsChanged();
        }
    }

    public bool StartApplicationUpdate()
    {
        if (!CanInstallApplicationUpdate
            || _downloadedApplicationUpdate is null)
        {
            return false;
        }

        try
        {
            _ = _mainWindow.StartApplicationUpdate(
                _downloadedApplicationUpdate);
            Application.Current.Shutdown();
            return true;
        }
        catch (Exception exception) when (IsApplicationUpdateException(
                                             exception))
        {
            ApplicationUpdateStatus =
                $"The installer could not start: {exception.Message}";
            return false;
        }
    }

    public async Task DownloadSelectedRemoteProfileAsync()
    {
        if (IsProfileSyncBusy
            || SelectedRemoteProfile is not { IsDeleted: false } selected)
        {
            return;
        }

        IsProfileSyncBusy = true;
        ProfileSyncStatus =
            $"Downloading '{selected.DisplayName}' into the existing import inspector…";
        try
        {
            var outcome = await _mainWindow.DownloadRemoteProfileAsync(
                selected.Profile.Id);
            _pendingRemoteDownload = outcome;
            LoadProfileImportPreview(outcome.Preview);
            ProfileSyncStatus = outcome.Preview.CanImport
                ? "Remote bundle passed integrity and schema inspection. Use Apply for a session preview or Save as current profile to adopt its ETag checkpoint."
                : "The remote bundle failed local inspection and cannot be applied or saved.";
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            HandleProfileSyncFailure(
                "Remote download failed without changing local state",
                exception);
        }
        finally
        {
            IsProfileSyncBusy = false;
        }
    }

    public async Task SyncAccountStateAsync()
    {
        if (IsProfileSyncBusy)
        {
            return;
        }

        IsProfileSyncBusy = true;
        ProfileSyncStatus =
            "Syncing the current workspace state with your Atlas account…";
        try
        {
            var outcome = await _mainWindow.SyncCurrentCloudStateAsync(
                cancellationToken: default);
            var settingsStatus = outcome.Settings.Kind switch
            {
                ProfileSyncUploadOutcomeKind.Unchanged =>
                    "Settings are up to date.",
                ProfileSyncUploadOutcomeKind.Created =>
                    "Settings synced.",
                ProfileSyncUploadOutcomeKind.Updated =>
                    "Settings synced.",
                _ => "A newer settings change was detected on another device. Nothing was overwritten."
            };
            var workspaceStatus = outcome.WorkspaceProfile.Kind switch
            {
                WorkspaceProfileRoamingSyncOutcomeKind.Unchanged =>
                    "Workspace resources are up to date.",
                WorkspaceProfileRoamingSyncOutcomeKind.Created =>
                    "Workspace resources synced.",
                WorkspaceProfileRoamingSyncOutcomeKind.Updated =>
                    "Workspace resources synced.",
                _ =>
                    "A newer workspace-resource change was detected on another device."
            };
            ProfileSyncStatus =
                $"{settingsStatus} {workspaceStatus} "
                + $"Synced {outcome.Packages.Uploaded} library package(s) and "
                + $"{outcome.Conversations.UploadedConversations} conversation(s).";
            if (outcome.Packages.HasConflicts
                || outcome.Conversations.ConflictPaths.Count > 0)
            {
                ProfileSyncStatus +=
                    " A newer resource remains active; no account data was overwritten.";
            }
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            HandleProfileSyncFailure(
                "Account sync was deferred; local work is safe",
                exception);
        }
        finally
        {
            IsProfileSyncBusy = false;
        }
    }

    public async Task DeleteSelectedRemoteProfileAsync()
    {
        if (IsProfileSyncBusy
            || SelectedRemoteProfile is not { IsDeleted: false } selected)
        {
            return;
        }

        IsProfileSyncBusy = true;
        ProfileSyncStatus =
            $"Requesting an ETag-conditional tombstone for '{selected.DisplayName}'…";
        try
        {
            var outcome = await _mainWindow.DeleteRemoteProfileAsync(
                selected.Profile);
            ProfileSyncStatus = outcome.Kind == ProfileSyncDeleteOutcomeKind.Deleted
                ? $"Remote revision {outcome.Tombstone?.ServerRevision} is now a tombstone. The local profile was not deleted."
                : "Delete was blocked because the remote ETag changed. Refresh the list and review the newer revision.";
            await RefreshRemoteProfilesCoreAsync();
        }
        catch (Exception exception) when (IsProfileSyncException(exception))
        {
            HandleProfileSyncFailure(
                "Remote delete failed; the local profile remains untouched",
                exception);
        }
        finally
        {
            IsProfileSyncBusy = false;
        }
    }

    private async Task RefreshRemoteProfilesCoreAsync()
    {
        var selectedId = SelectedRemoteProfile?.Profile.Id;
        var profiles = await _mainWindow.ListRemoteProfilesAsync(
            cancellationToken: default);
        RemoteProfiles.Clear();
        foreach (var profile in profiles)
        {
            RemoteProfiles.Add(new RemoteProfileEditorViewModel(profile));
        }

        SelectedRemoteProfile = RemoteProfiles.FirstOrDefault(profile =>
                                    string.Equals(
                                        profile.Profile.Id,
                                        selectedId,
                                        StringComparison.OrdinalIgnoreCase))
                                ?? RemoteProfiles.FirstOrDefault(profile =>
                                    !profile.IsDeleted)
                                ?? RemoteProfiles.FirstOrDefault();
    }

    private void SynchronizeProfileSyncSessionState()
    {
        ProfileSyncStatus =
            _mainWindow.ProfileSyncSession.StatusText;
        OnPropertyChanged(nameof(ProfileSyncSessionIdentity));
        OnPropertyChanged(nameof(IsProfileSyncSignedIn));
        OnPropertyChanged(nameof(ProfileSyncCredentialStatus));
        OnPropertyChanged(nameof(IsProfileSyncBusy));
        OnPropertyChanged(nameof(CanConfigureProfileSync));
        OnPropertyChanged(nameof(CanAuthorizeProfileSync));
        OnPropertyChanged(nameof(CanSignInProfileSync));
        OnPropertyChanged(nameof(CanSignOutProfileSync));
        OnPropertyChanged(nameof(CanUseProfileSync));
        OnPropertyChanged(nameof(CanDownloadRemoteProfile));
        OnPropertyChanged(nameof(CanDeleteRemoteProfile));
        NotifyApplicationUpdateActionsChanged();
    }

    private void NotifyApplicationUpdateActionsChanged()
    {
        OnPropertyChanged(nameof(CanCheckForApplicationUpdate));
        OnPropertyChanged(nameof(CanDownloadApplicationUpdate));
        OnPropertyChanged(nameof(CanInstallApplicationUpdate));
    }

    private void HandleProfileSyncFailure(string prefix, Exception exception)
    {
        if (exception is ProfileSyncException
            {
                Kind: ProfileSyncFailureKind.Authentication
                    or ProfileSyncFailureKind.Authorization
            }
            && _mainWindow.ProfileSyncSession.IsSignedIn)
        {
            _mainWindow.ProfileSyncSession.InvalidateSession(
                "The remote session is no longer valid. A protected access key, when available, will be used once to reconnect.");
        }

        ProfileSyncStatus = $"{prefix}: {exception.Message}";
    }

    private void LoadProfileImportPreview(ProfileImportPreview preview)
    {
        _profileImportPreview = preview;
        ProfileImportDiagnostics.Clear();
        foreach (var diagnostic in preview.Diagnostics)
        {
            ProfileImportDiagnostics.Add(diagnostic);
        }

        ProfileImportSourcePath = preview.SourcePath;
        if (preview.Envelope is { } envelope)
        {
            var profile = envelope.Profile;
            ProfileImportName =
                $"{profile.DisplayName}  •  schema {profile.SchemaVersion}  •  revision {profile.Revision}";
            ProfileImportIntegrityText =
                $"SHA-256 {envelope.ProfileSha256}  •  {preview.BundleBytes:N0} bytes";
            ProfileImportSummary =
                $"{profile.Configuration.Environments.Count} command-library configuration(s), "
                + $"{profile.Configuration.TerminalConfigurations.Count} terminal configuration(s), "
                + $"{profile.Extensions.Count} extension subscription(s), "
                + $"{profile.Libraries.Count} external library reference(s), and "
                + $"{profile.Configuration.WorkspaceLayout.Panels.Count} panel placement(s).";
        }
        else
        {
            ProfileImportName = "Unreadable portable profile";
            ProfileImportIntegrityText = $"{preview.BundleBytes:N0} bytes";
            ProfileImportSummary =
                "The payload could not be decoded as a supported portable profile.";
        }

        ProfileStatus = preview.CanImport
            ? preview.WarningCount == 0
                ? "Preview passed. Apply for this session or Save as current profile."
                : $"Preview passed with {preview.WarningCount} compatibility warning(s). Review them before continuing."
            : $"Import blocked by {preview.ErrorCount} validation error(s).";
        OnPropertyChanged(nameof(CanImportPortableProfile));
    }

    private static string BuildConflictStatus(
        ProfileSyncUploadOutcome outcome)
    {
        var paths = new[]
            {
                outcome.LocalConflictPath,
                outcome.RemoteConflictPath
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        return paths.Count == 0
            ? "Upload was blocked by a newer remote ETag. Nothing was overwritten."
            : "Upload was blocked by a newer remote ETag. Nothing was overwritten; conflict copies were written to "
              + string.Join(" and ", paths)
              + "."
              + (string.IsNullOrWhiteSpace(outcome.RemoteConflictFailure)
                  ? string.Empty
                  : " The current remote copy could not be preserved: "
                    + outcome.RemoteConflictFailure);
    }

    private static bool IsProfileSyncException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or OperationCanceledException
            or ProfileSyncException;

    private static bool IsApplicationUpdateException(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or OperationCanceledException
            or System.ComponentModel.Win32Exception
            or ProfileSyncException;

    private void ApplyGeneral(bool persist)
    {
        _mainWindow.ApplyApplicationPreferences(General, DefaultTerminalProfileId, persist);
        GeneralStatus = persist
            ? "Saved. These defaults will be restored next session."
            : "Applied to the running application. Save to keep these changes.";
    }

    private void ApplyTerminal(bool persist)
    {
        if (SelectedTerminalConfiguration is null)
        {
            TerminalStatus = "Create or select a terminal profile first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedTerminalConfiguration.Name))
        {
            TerminalStatus = "Give the terminal profile a name.";
            return;
        }

        if (SelectedTerminalConfiguration.Targets.All(target => !target.IsSelected))
        {
            TerminalStatus = "Select at least one shell for this terminal profile.";
            return;
        }

        if (!IsValidColor(SelectedTerminalConfiguration.TabColor)
            || !IsValidColor(SelectedTerminalConfiguration.Settings.ForegroundColor)
            || !IsValidColor(SelectedTerminalConfiguration.Settings.BackgroundColor)
            || !IsValidColor(SelectedTerminalConfiguration.Settings.AccentColor)
            || !IsValidColor(SelectedTerminalConfiguration.Settings.ErrorColor))
        {
            TerminalStatus = "One or more profile colors are invalid. Use a value such as #58A6FF.";
            return;
        }

        var configurations = TerminalConfigurations
            .Select(configuration => configuration.Export())
            .ToList();
        _mainWindow.ApplyTerminalConfigurationProfiles(
            configurations,
            SelectedTerminalConfiguration.Id,
            persist);
        TerminalStatus = persist
            ? "Saved terminal profiles and updated matching tabs."
            : "Applied to matching open tabs. Save to make these the defaults.";
    }

    private void ApplyExplorer(bool persist)
    {
        if (string.IsNullOrWhiteSpace(ExplorerStartupDirectory)
            || !Directory.Exists(ExplorerStartupDirectory))
        {
            ExplorerStatus = "Choose an existing startup directory.";
            return;
        }

        var invalidProject = Projects.FirstOrDefault(project =>
            string.IsNullOrWhiteSpace(project.Name)
            || project.Directories.Count == 0
            || project.Directories.Any(path => !IsAvailableProjectLocation(path)));
        if (invalidProject is not null)
        {
            SelectedProject = invalidProject;
            ExplorerStatus = string.IsNullOrWhiteSpace(invalidProject.Name)
                ? "Give every project a name."
                : invalidProject.Directories.Count == 0
                    ? $"Add at least one folder to {invalidProject.Name}."
                    : $"One or more folders in {invalidProject.Name} are unavailable.";
            return;
        }

        var draft = Explorer.Clone();
        draft.Projects = new ObservableCollection<ProjectDefinition>(
            Projects.Select(project => project.Export()));
        draft.ActiveProjectId = OpenSelectedProjectOnStartup
            ? SelectedProject?.Id ?? string.Empty
            : string.Empty;
        _mainWindow.ApplyExplorerSettings(draft, persist);
        ExplorerStatus = persist
            ? "Saved Explorer defaults and project definitions."
            : "Applied the Explorer view to this session. Save to keep it.";
    }

    private void ApplyWorkspace(bool persist)
    {
        var layout = ExportWorkspaceLayout();
        _mainWindow.ApplyWorkspaceLayout(layout, persist);
        if (persist)
        {
            _workspaceSessionBaseline = layout.Clone();
        }

        WorkspaceStatus = persist
            ? "Saved. This panel layout will be restored next session and can travel with a profile."
            : "Applied to the running workspace. Save to keep it.";
    }

    private async Task RefreshLanguageToolsAsync()
    {
        LanguageToolsStatus = "Refreshing device-local runtime and language-tool detection…";
        var snapshot = await _mainWindow.RefreshLanguageToolCatalogAsync();
        LoadLanguageToolCatalog(snapshot);
        LanguageToolsStatus =
            "Detection refreshed. Nothing was installed, downloaded, or started.";
    }

    private void LoadLanguageToolCatalog(LanguageToolCatalogSnapshot snapshot)
    {
        LanguageToolGroups.Clear();
        foreach (var group in snapshot.Tools
                     .GroupBy(tool => tool.Definition.Kind)
                     .OrderBy(group => group.Key))
        {
            LanguageToolGroups.Add(new LanguageToolGroupViewModel(
                group.Key,
                group
                    .OrderByDescending(tool => tool.IsAvailable)
                    .ThenBy(tool => tool.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(tool => new LanguageToolEditorViewModel(tool))
                    .ToList()));
        }

        LanguageToolCatalogSummary =
            $"{snapshot.AvailableRuntimeCount} runtime(s), "
            + $"{snapshot.AvailableCompilerCount} compiler(s), "
            + $"{snapshot.AvailableInteractiveShellCount} interactive shell(s), "
            + $"{snapshot.AvailableFormatterCount} formatter(s), and "
            + $"{snapshot.AvailableLanguageServerCount} language server(s) available on this device.";
        LanguageToolScanTimeText =
            $"Last scanned {snapshot.ScannedAtUtc.ToLocalTime():g}";
        LanguageToolsStatus =
            "Read-only PATH detection. Formatters and compilers run only after an explicit document action; interactive shells and language servers are not started by detection.";
    }

    private async Task RefreshExtensionsAsync()
    {
        ExtensionsStatus =
            "Refreshing package validation and the active Hyperlib catalog…";
        var snapshot = await _mainWindow.RefreshExtensionCatalogAsync();
        LoadExtensionCatalog(snapshot);
        LoadWorkspaceDraft(_mainWindow.WorkspaceLayout);
        ExtensionsStatus =
            "Discovery and Hyperlib content refreshed. Static ContentOnly packages were revalidated and loaded automatically; executable panel updates still require a new hash-bound device approval.";
    }

    private void LoadExtensionCatalog(ExtensionDiscoverySnapshot snapshot)
    {
        ExtensionPackages.Clear();
        foreach (var package in snapshot.Packages)
        {
            ExtensionPackages.Add(new ExtensionPackageEditorViewModel(
                package,
                _mainWindow));
        }

        ExtensionRootDirectory = snapshot.RootDirectory;
        var documentProviders = _mainWindow.DocumentProviderCatalog;
        ExtensionCatalogSummary = snapshot.Packages.Count == 0
            ? "No extension packages found. Add each package as its own folder beneath this location. "
              + $"{documentProviders.ActiveBuiltInCount} built-in document format mapping(s) remain available."
            : $"{snapshot.Packages.Count} package(s) discovered · "
              + $"{snapshot.ValidatedPackageCount} validated · "
              + $"{snapshot.ApprovalRequiredPackageCount} approval pending · "
              + $"{snapshot.RejectedPackageCount} quarantined · "
              + $"{snapshot.LibraryCandidates.Count} active Hyperlib content librar{(snapshot.LibraryCandidates.Count == 1 ? "y" : "ies")} "
              + $"with {snapshot.HyperlibEntryCount:N0} entr{(snapshot.HyperlibEntryCount == 1 ? "y" : "ies")} · "
              + $"{documentProviders.ActiveExtensionCount} active and "
              + $"{documentProviders.DormantExtensionCount} dormant static document mapping(s) · "
              + $"{snapshot.PanelCandidates.Count} local panel candidate(s).";
        ExtensionScanTimeText =
            $"Last scanned {snapshot.ScannedAtUtc.ToLocalTime():g}";
        ExtensionsStatus =
            "Discovery is read-only. Validated ContentOnly libraries load as static Hyperlib data; local WebView panels still require exact-hash device approval.";
    }

    private void RefreshExtensionAndWorkspaceDrafts()
    {
        LoadExtensionCatalog(_mainWindow.ExtensionCatalog);
        LoadWorkspaceDraft(_mainWindow.WorkspaceLayout);
    }

    private void ResetWorkspaceDraft()
    {
        LoadWorkspaceDraft(
            WorkspaceLayoutService.CreateDefault(_workspacePanelRegistry));
        WorkspaceStatus =
            "Reset the draft to the built-in panel positions and widths. Apply or Save to use it.";
    }

    private void CancelWorkspacePreview()
    {
        _mainWindow.ApplyWorkspaceLayout(
            _workspaceSessionBaseline,
            persist: false);
        LoadWorkspaceDraft(_workspaceSessionBaseline);
        WorkspaceStatus =
            "Canceled the preview and restored the layout from when this settings window opened.";
    }

    private WorkspaceLayoutSnapshot ExportWorkspaceLayout()
    {
        var panels = WorkspacePanels
            .Select((panel, index) => panel.Export(index))
            .Concat(_unregisteredWorkspacePanels.Select(panel => panel.Clone()))
            .ToList();
        var sideTools = WorkspaceSideTools
            .GroupBy(tool => tool.HostSurface)
            .SelectMany(group => group.Select((tool, index) => tool.Export(index)))
            .Concat(_unregisteredWorkspaceSideTools.Select(tool => tool.Clone()))
            .ToList();
        return WorkspaceLayoutService.Normalize(
            new WorkspaceLayoutSnapshot
            {
                IsUserUnlocked = true,
                Panels = panels,
                SideTools = sideTools
            },
            _workspacePanelRegistry);
    }

    private void LoadWorkspaceDraft(WorkspaceLayoutSnapshot source)
    {
        var normalized = WorkspaceLayoutService.Normalize(
            source,
            _workspacePanelRegistry);
        WorkspaceSideTools.Clear();
        foreach (var definition in WorkspaceSideToolRegistry.Definitions
                     .OrderBy(definition => definition.HostSurface)
                     .ThenBy(definition => WorkspaceLayoutService
                         .GetRequiredSideTool(normalized, definition.Id).Order)
                     .ThenBy(definition => definition.DefaultOrder))
        {
            WorkspaceSideTools.Add(
                new WorkspaceSideToolLayoutEditorViewModel(
                    definition,
                    WorkspaceLayoutService.GetRequiredSideTool(
                        normalized,
                        definition.Id),
                    MoveWorkspaceSideTool,
                    CanMoveWorkspaceSideTool));
        }

        var registeredToolIds = WorkspaceSideToolRegistry.Definitions
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _unregisteredWorkspaceSideTools = normalized.SideTools
            .Where(tool => !registeredToolIds.Contains(tool.ToolId))
            .Select(tool => tool.Clone())
            .ToList();
        RefreshWorkspaceSideToolMoveCommands();

        WorkspacePanels.Clear();
        foreach (var registration in _workspacePanelRegistry.Registrations
                     .OrderBy(registration => registration.BuiltInSurface switch
                     {
                         BuiltInWorkspaceSurface.FileExplorer => 0,
                         BuiltInWorkspaceSurface.CommandBuilder => 1,
                         _ => 2
                     })
                     .ThenBy(
                         registration => registration.Contribution.Id,
                         StringComparer.OrdinalIgnoreCase))
        {
            WorkspacePanels.Add(
                new WorkspacePanelLayoutEditorViewModel(
                    registration,
                    WorkspaceLayoutService.GetRequiredPanel(
                        normalized,
                        registration.Contribution.Id)));
        }

        var registeredIds = _workspacePanelRegistry.Registrations
            .Select(registration => registration.Contribution.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _unregisteredWorkspacePanels = normalized.Panels
            .Where(panel => !registeredIds.Contains(panel.PanelId))
            .Select(panel => panel.Clone())
            .ToList();
    }

    private void MoveWorkspaceSideTool(
        WorkspaceSideToolLayoutEditorViewModel tool,
        int direction)
    {
        var group = WorkspaceSideTools
            .Where(candidate => candidate.HostSurface == tool.HostSurface)
            .ToList();
        var groupIndex = group.IndexOf(tool);
        var targetGroupIndex = groupIndex + direction;
        if (groupIndex < 0
            || targetGroupIndex < 0
            || targetGroupIndex >= group.Count)
        {
            return;
        }

        var sourceIndex = WorkspaceSideTools.IndexOf(tool);
        var targetIndex = WorkspaceSideTools.IndexOf(group[targetGroupIndex]);
        WorkspaceSideTools.Move(sourceIndex, targetIndex);
        RefreshWorkspaceSideToolMoveCommands();
    }

    private bool CanMoveWorkspaceSideTool(
        WorkspaceSideToolLayoutEditorViewModel tool,
        int direction)
    {
        var group = WorkspaceSideTools
            .Where(candidate => candidate.HostSurface == tool.HostSurface)
            .ToList();
        var index = group.IndexOf(tool);
        return index >= 0
               && index + direction >= 0
               && index + direction < group.Count;
    }

    private void RefreshWorkspaceSideToolMoveCommands()
    {
        foreach (var tool in WorkspaceSideTools)
        {
            tool.RefreshMoveCommands();
        }
    }

    private void AddTerminalConfiguration()
    {
        var source = SelectedTerminalConfiguration?.Export();
        var configuration = source?.Clone() ?? new TerminalConfigurationProfile
        {
            Settings = _mainWindow.SelectedTerminalProfile.Settings.Clone(),
            TabTitleTemplate = _mainWindow.SelectedTerminalProfile.TabTitleTemplate,
            TabColor = _mainWindow.SelectedTerminalProfile.TabColor,
            StartupCommand = _mainWindow.SelectedTerminalProfile.StartupCommand
        };
        configuration.Id = Guid.NewGuid().ToString("N");
        configuration.Name = "New terminal profile";
        configuration.Description = "Shared configuration for selected shells.";
        configuration.TargetTerminalProfileIds = [];
        var editor = AddTerminalConfigurationEditor(configuration);
        SelectedTerminalConfiguration = editor;
        TerminalStatus = "New profile created in this draft. Select its target shells.";
    }

    private void DuplicateTerminalConfiguration()
    {
        if (SelectedTerminalConfiguration is null)
        {
            return;
        }

        var duplicate = SelectedTerminalConfiguration.Export();
        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.Name = $"{duplicate.Name} copy";
        duplicate.TargetTerminalProfileIds = [];
        SelectedTerminalConfiguration = AddTerminalConfigurationEditor(duplicate);
        TerminalStatus = "Duplicated the profile without target assignments.";
    }

    private void DeleteTerminalConfiguration()
    {
        if (SelectedTerminalConfiguration is null)
        {
            return;
        }

        var index = TerminalConfigurations.IndexOf(SelectedTerminalConfiguration);
        TerminalConfigurations.Remove(SelectedTerminalConfiguration);
        SelectedTerminalConfiguration = TerminalConfigurations.Count == 0
            ? null
            : TerminalConfigurations[Math.Clamp(index, 0, TerminalConfigurations.Count - 1)];
        TerminalStatus = "Removed the profile from this draft. Save to commit the deletion.";
    }

    private TerminalConfigurationProfileEditorViewModel AddTerminalConfigurationEditor(
        TerminalConfigurationProfile configuration)
    {
        var editor = new TerminalConfigurationProfileEditorViewModel(
            configuration,
            TerminalProfiles);
        foreach (var target in editor.Targets)
        {
            target.SelectionChanged += OnTerminalTargetSelectionChanged;
        }

        TerminalConfigurations.Add(editor);
        return editor;
    }

    private void OnTerminalTargetSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isSynchronizingTerminalTargets
            || sender is not SelectableSettingsTarget { IsSelected: true } selectedTarget
            || SelectedTerminalConfiguration is null)
        {
            return;
        }

        _isSynchronizingTerminalTargets = true;
        try
        {
            foreach (var configuration in TerminalConfigurations.Where(
                         configuration => !ReferenceEquals(configuration, SelectedTerminalConfiguration)))
            {
                var duplicateTarget = configuration.Targets.FirstOrDefault(target =>
                    string.Equals(target.Id, selectedTarget.Id, StringComparison.OrdinalIgnoreCase));
                if (duplicateTarget is not null)
                {
                    duplicateTarget.IsSelected = false;
                }
            }
        }
        finally
        {
            _isSynchronizingTerminalTargets = false;
        }
    }

    private void SetTerminalTargets(bool selected)
    {
        if (SelectedTerminalConfiguration is null)
        {
            return;
        }

        foreach (var target in SelectedTerminalConfiguration.Targets.Where(target => target.IsAvailable))
        {
            target.IsSelected = selected;
        }
    }

    private void AddProject()
    {
        var editor = new ProjectEditorViewModel(new ProjectDefinition
        {
            Name = "New project"
        });
        Projects.Add(editor);
        SelectedProject = editor;
        OpenSelectedProjectOnStartup = true;
        ExplorerStatus = "New project created in this draft. Add one or more folders.";
    }

    private static string NormalizeProjectLocation(string value)
    {
        if (WorkspaceLocation.TryParse(value, out var location))
        {
            return location.IsLocal ? location.Path : location.Identity;
        }

        return Path.GetFullPath(value);
    }

    private static bool IsAvailableProjectLocation(string value)
    {
        if (WorkspaceLocation.TryParse(value, out var location))
        {
            return location.IsSsh || Directory.Exists(location.Path);
        }

        try
        {
            return Directory.Exists(Path.GetFullPath(value));
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException)
        {
            return false;
        }
    }

    private void DuplicateProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var project = SelectedProject.Export();
        project.Id = Guid.NewGuid().ToString("N");
        project.Name = $"{project.Name} copy";
        var editor = new ProjectEditorViewModel(project);
        Projects.Add(editor);
        SelectedProject = editor;
        ExplorerStatus = "Duplicated the project.";
    }

    private void DeleteProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var index = Projects.IndexOf(SelectedProject);
        Projects.Remove(SelectedProject);
        SelectedProject = Projects.Count == 0
            ? null
            : Projects[Math.Clamp(index, 0, Projects.Count - 1)];
        if (Projects.Count == 0)
        {
            OpenSelectedProjectOnStartup = false;
        }

        ExplorerStatus = "Removed the project from this draft.";
    }

    private void LoadWorkspaceAiProviderSettings()
    {
        var configuration = _mainWindow
            .WorkspaceAiProviderConfigurationService
            .Load();
        ApplyWorkspaceAiProviderConfiguration(configuration);
        HasWorkspaceAiCredential =
            _mainWindow.WorkspaceAiCredentialStore.Exists;
        var warning = _mainWindow
            .WorkspaceAiProviderConfigurationService
            .LastLoadWarning;
        WorkspaceAiProviderStatus = !string.IsNullOrWhiteSpace(warning)
            ? warning
            : BuildWorkspaceAiAccessStatus(configuration);
    }

    private void LoadWorkspaceAiAutonomySettings()
    {
        var contexts = _mainWindow.WorkspaceAiContextService;
        var selected = contexts.GetContext(contexts.SelectedContextId);
        WorkspaceAiAutonomyContextName = selected is null
            ? "No selected Context"
            : selected.Name;
        AllowWorkspaceAiAutonomousChanges =
            selected is { AskBeforeChanges: false };
        WorkspaceAiAutonomyStatus = selected is null
            ? "Choose an AI Context in the conversation pane first."
            : selected.AskBeforeChanges
                ? "Per-action review is enabled for this Context."
                : "Autonomous execution is already enabled for this Context.";
        OnPropertyChanged(nameof(WorkspaceAiAutonomyRequiresConfirmation));
    }

    private void ApplyWorkspaceAiProviderConfiguration(
        WorkspaceAiProviderConfiguration configuration)
    {
        _workspaceAiOpenAiModel = configuration.OpenAiModel;
        _workspaceAiLocalModel = configuration.LocalModel;
        _workspaceAiOpenAiReasoningEffort =
            configuration.OpenAiReasoningEffort;
        _workspaceAiLocalReasoningEffort =
            configuration.LocalReasoningEffort;
        _workspaceAiOpenAiTextVerbosity =
            configuration.OpenAiTextVerbosity;
        _workspaceAiLocalTextVerbosity =
            configuration.LocalTextVerbosity;
        _workspaceAiOpenAiCloudRequestsEnabled =
            configuration.OpenAiCloudRequestsEnabled;
        WorkspaceAiProviderId = configuration.ProviderId;
        WorkspaceAiProviderModel = configuration.Model;
        WorkspaceAiReasoningEffort = configuration.ReasoningEffort;
        WorkspaceAiTextVerbosity = configuration.TextVerbosity;
        WorkspaceAiProviderEndpoint = configuration.Endpoint;
        AllowWorkspaceAiCloudRequests =
            configuration.OpenAiCloudRequestsEnabled;
    }

    private string BuildWorkspaceAiAccessStatus(
        WorkspaceAiProviderConfiguration configuration)
    {
        var local = IsWorkspaceAiLocalRuntimeInstalled
            ? "Local AI is ready on demand."
            : "The local AI runtime is not installed yet.";
        var openAi = configuration.OpenAiCloudRequestsEnabled
            ? HasWorkspaceAiCredential
                ? "OpenAI access is enabled and its device-protected key is ready."
                : "OpenAI access is enabled, but an API key is still required."
            : "OpenAI access is disabled.";
        return $"{local} {openAi} Model choices remain conversation-specific.";
    }

    private void RefreshWorkspaceAiAssistants()
    {
        foreach (var conversation in _mainWindow.WorkspaceAiConversations)
        {
            conversation.Assistant.RefreshState();
        }
    }

    private void LoadWorkspaceAiAccess(string? preferredWorkspaceId = null)
    {
        preferredWorkspaceId ??= SelectedWorkspaceAiAccess?.WorkspaceId;
        var approvals = _mainWindow.WorkspaceAiPermissionBroker
            .GetApprovals();
        var approvalById = approvals.ToDictionary(
            approval => approval.WorkspaceId,
            StringComparer.OrdinalIgnoreCase);
        var descriptors = _mainWindow
            .GetWorkspaceAiWorkspaceDescriptors()
            .Where(descriptor =>
                !string.IsNullOrWhiteSpace(descriptor.RootPath))
            .ToList();
        foreach (var approval in approvals)
        {
            var matchingCandidate = descriptors.FirstOrDefault(descriptor =>
                string.Equals(
                    Path.TrimEndingDirectorySeparator(descriptor.RootPath),
                    Path.TrimEndingDirectorySeparator(approval.LocalRootPath),
                    StringComparison.OrdinalIgnoreCase));
            if (matchingCandidate is not null)
            {
                descriptors.Remove(matchingCandidate);
            }

            var displayName = !string.IsNullOrWhiteSpace(approval.DisplayName)
                ? approval.DisplayName
                : matchingCandidate?.DisplayName;
            descriptors.Add(new WorkspaceAiWorkspaceDescriptor(
                approval.WorkspaceId,
                string.IsNullOrWhiteSpace(displayName)
                    ? approval.LocalRootPath
                    : displayName,
                approval.LocalRootPath,
                Directory.Exists(approval.LocalRootPath)));
        }

        WorkspaceAiAccessEntries.Clear();
        foreach (var descriptor in descriptors
                     .OrderBy(
                         descriptor => descriptor.DisplayName,
                         StringComparer.OrdinalIgnoreCase))
        {
            approvalById.TryGetValue(
                descriptor.WorkspaceId,
                out var approval);
            WorkspaceAiAccessEntries.Add(
                new WorkspaceAiAccessEditorViewModel(
                    descriptor,
                    approval));
        }

        SelectedWorkspaceAiAccess = WorkspaceAiAccessEntries
                                        .FirstOrDefault(entry => string.Equals(
                                            entry.WorkspaceId,
                                            preferredWorkspaceId,
                                            StringComparison.OrdinalIgnoreCase))
                                    ?? WorkspaceAiAccessEntries.FirstOrDefault();

        RecentWorkspaceAiOperations.Clear();
        foreach (var entry in _mainWindow.WorkspaceAiAuditService
                     .ReadRecent(40))
        {
            RecentWorkspaceAiOperations.Add(
                new WorkspaceAiAuditEntryViewModel(entry));
        }

        var warning = _mainWindow.WorkspaceAiPermissionBroker
            .LastLoadWarning;
        WorkspaceAiStatus = WorkspaceAiAccessEntries.Count == 0
            ? "No AI roots are configured. Add one directly or use an Explorer project/current folder candidate."
            : $"{approvals.Count} approved workspace root(s) on this device · "
              + $"{RecentWorkspaceAiOperations.Count} recent audit record(s)."
              + (string.IsNullOrWhiteSpace(warning)
                  ? string.Empty
                  : $" {warning}");
    }

    private void NotifyNewWorkspaceAiRootCapabilityState()
    {
        OnPropertyChanged(nameof(CanAddWorkspaceAiRoot));
        OnPropertyChanged(nameof(NewWorkspaceAiRootCapabilitySummary));
        OnPropertyChanged(nameof(NewWorkspaceAiRootAllowAutonomousActions));
    }

    internal static string BuildWorkspaceAiCapabilitySummary(
        bool allowRead,
        bool allowWrite,
        bool allowAutomaticFileAndEditorChanges,
        bool allowTerminalRead,
        bool allowTerminal,
        WorkspaceAiTerminalExecutionPolicy terminalExecutionPolicy)
    {
        var capabilities = new List<string>();
        if (allowRead)
        {
            capabilities.Add("bounded read/list");
        }

        if (allowWrite)
        {
            capabilities.Add(allowAutomaticFileAndEditorChanges
                ? "automatic atomic file/editor changes"
                : "reviewed atomic file/editor changes");
        }

        if (allowTerminalRead)
        {
            capabilities.Add("live terminal draft/transcript read");
        }

        if (allowTerminal)
        {
            capabilities.Add(terminalExecutionPolicy switch
            {
                WorkspaceAiTerminalExecutionPolicy.ReviewOnly =>
                    "terminal execution after review",
                WorkspaceAiTerminalExecutionPolicy.AutoExecute =>
                    "standing terminal auto-execution",
                _ => "terminal execution with review + confirmation"
            });
        }

        return capabilities.Count == 0
            ? "No capabilities selected"
            : string.Join(" · ", capabilities);
    }

    private void OnProfileSyncSessionPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs) =>
        SynchronizeProfileSyncSessionState();

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        LinkedHosts.Dispose();
        _mainWindow.ProfileSyncSession.PropertyChanged -=
            OnProfileSyncSessionPropertyChanged;
    }

    private static bool IsValidColor(string value)
    {
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(value)!;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class WorkspaceAiAccessEditorViewModel : ObservableObject
{
    private string _displayName;
    private bool _allowRead;
    private bool _allowWrite;
    private bool _allowAutomaticFileAndEditorChanges;
    private bool _allowTerminalRead;
    private bool _allowTerminal;
    private WorkspaceAiTerminalExecutionPolicy _terminalExecutionPolicy;

    public WorkspaceAiAccessEditorViewModel(
        WorkspaceAiWorkspaceDescriptor descriptor,
        WorkspaceAiApproval? approval)
    {
        WorkspaceId = descriptor.WorkspaceId;
        _displayName = !string.IsNullOrWhiteSpace(approval?.DisplayName)
            ? approval.DisplayName
            : descriptor.DisplayName;
        RootPath = descriptor.RootPath;
        IsAvailable = descriptor.IsAvailable
                      && Directory.Exists(descriptor.RootPath);
        IsApproved = approval is not null;
        ApprovedAtUtc = approval?.ApprovedAtUtc;
        _allowRead = approval?.AllowRead ?? true;
        _allowWrite = approval?.AllowWrite ?? false;
        _allowAutomaticFileAndEditorChanges =
            approval?.AllowAutomaticFileAndEditorChanges ?? false;
        _allowTerminalRead = approval?.AllowTerminalRead ?? false;
        _allowTerminal = approval?.AllowTerminal ?? false;
        _terminalExecutionPolicy = approval?.TerminalExecutionPolicy
            ?? WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand;
    }

    public string WorkspaceId { get; }
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value ?? string.Empty);
    }
    public string RootPath { get; }
    public bool IsAvailable { get; }
    public bool IsApproved { get; }
    public DateTimeOffset? ApprovedAtUtc { get; }

    public bool AllowRead
    {
        get => _allowRead;
        set
        {
            if (!SetProperty(ref _allowRead, value))
            {
                return;
            }

            if (!value && _allowWrite)
            {
                _allowWrite = false;
                OnPropertyChanged(nameof(AllowWrite));
            }

            if (!value && _allowAutomaticFileAndEditorChanges)
            {
                _allowAutomaticFileAndEditorChanges = false;
                OnPropertyChanged(
                    nameof(AllowAutomaticFileAndEditorChanges));
            }

            NotifyCapabilityState();
        }
    }

    public bool AllowWrite
    {
        get => _allowWrite;
        set
        {
            if (!SetProperty(ref _allowWrite, value))
            {
                return;
            }

            if (value && !_allowRead)
            {
                _allowRead = true;
                OnPropertyChanged(nameof(AllowRead));
            }

            if (!value && _allowAutomaticFileAndEditorChanges)
            {
                _allowAutomaticFileAndEditorChanges = false;
                OnPropertyChanged(
                    nameof(AllowAutomaticFileAndEditorChanges));
            }

            NotifyCapabilityState();
        }
    }

    public bool AllowAutomaticFileAndEditorChanges
    {
        get => _allowAutomaticFileAndEditorChanges;
        set
        {
            if (!SetProperty(
                    ref _allowAutomaticFileAndEditorChanges,
                    value))
            {
                return;
            }

            if (value && !_allowWrite)
            {
                _allowWrite = true;
                _allowRead = true;
                OnPropertyChanged(nameof(AllowWrite));
                OnPropertyChanged(nameof(AllowRead));
            }

            NotifyCapabilityState();
        }
    }

    public bool AllowTerminal
    {
        get => _allowTerminal;
        set
        {
            if (SetProperty(ref _allowTerminal, value))
            {
                if (!value)
                {
                    TerminalExecutionPolicy =
                        WorkspaceAiTerminalExecutionPolicy
                            .ConfirmEveryCommand;
                }

                NotifyCapabilityState();
            }
        }
    }

    public IReadOnlyList<WorkspaceAiTerminalExecutionPolicyOption>
        TerminalExecutionPolicies =>
            WorkspaceAiTerminalExecutionPolicyOption.All;

    public WorkspaceAiTerminalExecutionPolicy TerminalExecutionPolicy
    {
        get => _terminalExecutionPolicy;
        set
        {
            if (SetProperty(ref _terminalExecutionPolicy, value))
            {
                OnPropertyChanged(
                    nameof(TerminalExecutionPolicyDescription));
                OnPropertyChanged(nameof(AllowAutonomousActions));
                NotifyCapabilityState();
            }
        }
    }

    public string TerminalExecutionPolicyDescription =>
        WorkspaceAiTerminalExecutionPolicyOption.Describe(
            TerminalExecutionPolicy);

    public bool AllowTerminalRead
    {
        get => _allowTerminalRead;
        set
        {
            if (SetProperty(ref _allowTerminalRead, value))
            {
                NotifyCapabilityState();
            }
        }
    }

    public bool AllowAutonomousActions
    {
        get => AllowWrite
               && AllowAutomaticFileAndEditorChanges
               && AllowTerminal
               && TerminalExecutionPolicy
                   == WorkspaceAiTerminalExecutionPolicy.AutoExecute;
        set
        {
            if (value == AllowAutonomousActions)
            {
                return;
            }

            if (value)
            {
                _allowRead = true;
                _allowWrite = true;
                _allowAutomaticFileAndEditorChanges = true;
                _allowTerminalRead = true;
                _allowTerminal = true;
                _terminalExecutionPolicy =
                    WorkspaceAiTerminalExecutionPolicy.AutoExecute;
            }
            else
            {
                _allowAutomaticFileAndEditorChanges = false;
                if (_terminalExecutionPolicy
                    == WorkspaceAiTerminalExecutionPolicy.AutoExecute)
                {
                    _terminalExecutionPolicy =
                        WorkspaceAiTerminalExecutionPolicy
                            .ConfirmEveryCommand;
                }
            }

            OnPropertyChanged(nameof(AllowRead));
            OnPropertyChanged(nameof(AllowWrite));
            OnPropertyChanged(
                nameof(AllowAutomaticFileAndEditorChanges));
            OnPropertyChanged(nameof(AllowTerminalRead));
            OnPropertyChanged(nameof(AllowTerminal));
            OnPropertyChanged(nameof(TerminalExecutionPolicy));
            OnPropertyChanged(nameof(TerminalExecutionPolicyDescription));
            NotifyCapabilityState();
        }
    }

    public bool CanApprove =>
        IsAvailable
        && (AllowRead || AllowWrite || AllowTerminalRead || AllowTerminal);

    public bool CanRevoke => IsApproved;

    public string AvailabilityText => IsAvailable
        ? IsApproved
            ? "APPROVED ON THIS DEVICE"
            : "AVAILABLE · NOT APPROVED"
        : "ROOT UNAVAILABLE · OPERATIONS DENIED";

    public string ApprovedAtText => ApprovedAtUtc is { } approvedAt
        ? $"Approved {approvedAt.ToLocalTime():g}"
        : "No device-local trust decision has been saved.";

    public string CapabilitySummary
        => SettingsWindowViewModel.BuildWorkspaceAiCapabilitySummary(
            AllowRead,
             AllowWrite,
             AllowAutomaticFileAndEditorChanges,
             AllowTerminalRead,
             AllowTerminal,
             TerminalExecutionPolicy);

    private void NotifyCapabilityState()
    {
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CapabilitySummary));
        OnPropertyChanged(nameof(AllowAutonomousActions));
    }
}

public sealed record WorkspaceAiTerminalExecutionPolicyOption(
    WorkspaceAiTerminalExecutionPolicy Policy,
    string DisplayName,
    string Description)
{
    public static IReadOnlyList<WorkspaceAiTerminalExecutionPolicyOption>
        All
    { get; } =
        [
            new(
                WorkspaceAiTerminalExecutionPolicy.ConfirmEveryCommand,
                "Review + confirm every command",
                "Each execution pauses for the AI review card and a separate native confirmation dialog."),
            new(
                WorkspaceAiTerminalExecutionPolicy.ReviewOnly,
                "Review once",
                "Each execution pauses at the AI review card; approving it runs the command without a second dialog."),
            new(
                WorkspaceAiTerminalExecutionPolicy.AutoExecute,
                "Auto-execute (standing permission)",
                "AI-requested commands run without a per-command review or confirmation. The shell has the same filesystem and network reach as your Windows account.")
        ];

    public static string Describe(
        WorkspaceAiTerminalExecutionPolicy policy) =>
        All.FirstOrDefault(option => option.Policy == policy)?.Description
        ?? All[0].Description;
}

public sealed class WorkspaceAiAuditEntryViewModel
{
    public WorkspaceAiAuditEntryViewModel(WorkspaceAiAuditEntry entry)
    {
        Entry = entry;
    }

    public WorkspaceAiAuditEntry Entry { get; }
    public string TimeText => Entry.TimestampUtc.ToLocalTime().ToString("g");
    public string OperationText => Entry.Operation.ToString();
    public string OutcomeText => Entry.Outcome.ToString().ToUpperInvariant();
    public string TargetText => string.IsNullOrWhiteSpace(Entry.Target)
        ? "."
        : Entry.Target;
    public string SummaryText =>
        $"{Entry.WorkspaceId} · {Entry.Capability} · {Entry.Detail}";
}

public sealed class RemoteProfileEditorViewModel
{
    public RemoteProfileEditorViewModel(RemoteProfileSummary profile)
    {
        Profile = profile;
    }

    public RemoteProfileSummary Profile { get; }
    public string DisplayName => Profile.DisplayName;
    public bool IsDeleted => Profile.IsDeleted;
    public string StatusText => Profile.IsDeleted ? "TOMBSTONE" : "AVAILABLE";
    public string IdentityText =>
        $"{Profile.Id}  •  server r{Profile.ServerRevision}  •  profile r{Profile.ProfileRevision}";
    public string UpdatedText =>
        $"Updated {Profile.UpdatedAtUtc.ToLocalTime():g}  •  SHA-256 {Profile.ProfileSha256}";
    public string ETagText => $"ETag {Profile.ETag}";
}

public sealed class LanguageToolGroupViewModel
{
    public LanguageToolGroupViewModel(
        LanguageToolKind kind,
        IReadOnlyList<LanguageToolEditorViewModel> tools)
    {
        Kind = kind;
        Tools = tools;
    }

    public LanguageToolKind Kind { get; }
    public string DisplayName => Kind switch
    {
        LanguageToolKind.Runtime => "Runtimes",
        LanguageToolKind.Compiler => "Compilers",
        LanguageToolKind.InteractiveShell => "Interactive shells",
        LanguageToolKind.Formatter => "Formatters",
        LanguageToolKind.LanguageServer => "Language servers",
        _ => Kind.ToString()
    };
    public IReadOnlyList<LanguageToolEditorViewModel> Tools { get; }
}

public sealed class LanguageToolEditorViewModel
{
    public LanguageToolEditorViewModel(DiscoveredLanguageTool tool)
    {
        Tool = tool;
    }

    public DiscoveredLanguageTool Tool { get; }
    public string DisplayName => Tool.Definition.DisplayName;
    public LanguageToolAvailability Availability => Tool.Availability;
    public string AvailabilityText => Tool.Definition.IsHostBuiltIn
        ? "BUILT IN"
        : Availability switch
        {
            LanguageToolAvailability.Available => "AVAILABLE",
            LanguageToolAvailability.UnsupportedCommandShim => "WRAPPER IGNORED",
            _ => "NOT DETECTED"
        };
    public string LanguagesText => Tool.Definition.LanguageIds.Count == 0
        ? "General"
        : string.Join(", ", Tool.Definition.LanguageIds);
    public string LocationText => Tool.Definition.IsHostBuiltIn
        ? "Hosted inside Hypershell"
        : string.IsNullOrWhiteSpace(Tool.ExecutablePath)
            ? "No native executable detected"
            : Tool.ExecutablePath;
    public string InvocationText => Tool.Definition.IsHostBuiltIn
        ? "No process"
        : Tool.Definition.Kind == LanguageToolKind.Formatter
            ? "Direct process · bounded stdin/stdout"
            : Tool.Definition.Kind == LanguageToolKind.Compiler
                ? "Direct process · launched only by Run document"
                : Tool.Definition.Kind == LanguageToolKind.InteractiveShell
                    ? "Detection only · not started automatically"
            : Tool.Definition.Kind == LanguageToolKind.LanguageServer
                ? "Direct stdio process · stopped by default"
                : "Detection only";
    public string Description => Tool.Definition.Description;
    public string StatusMessage => Tool.StatusMessage;
}

public sealed class WorkspaceSideToolLayoutEditorViewModel : ObservableObject
{
    private readonly WorkspaceSideToolDefinition _definition;
    private bool _isVisible;

    public WorkspaceSideToolLayoutEditorViewModel(
        WorkspaceSideToolDefinition definition,
        WorkspaceSideToolPlacementSnapshot placement,
        Action<WorkspaceSideToolLayoutEditorViewModel, int> move,
        Func<WorkspaceSideToolLayoutEditorViewModel, int, bool> canMove)
    {
        _definition = definition;
        _isVisible = placement.IsVisible;
        MoveUpCommand = new RelayCommand(
            () => move(this, -1),
            () => canMove(this, -1));
        MoveDownCommand = new RelayCommand(
            () => move(this, 1),
            () => canMove(this, 1));
    }

    public string ToolId => _definition.Id;
    public string DisplayName => _definition.DisplayName;
    public BuiltInWorkspaceSurface HostSurface => _definition.HostSurface;
    public string HostLabel => HostSurface == BuiltInWorkspaceSurface.FileExplorer
        ? "Explorer dock"
        : "Developer tools dock";
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public WorkspaceSideToolPlacementSnapshot Export(int order) =>
        new()
        {
            ToolId = ToolId,
            Order = order,
            IsVisible = IsVisible
        };

    public void RefreshMoveCommands()
    {
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
    }
}

public sealed class WorkspacePanelLayoutEditorViewModel : ObservableObject
{
    private readonly WorkspacePanelRegistration _registration;
    private IReadOnlyList<WorkspacePanelSlot> _allowedSlots = [];
    private bool _isVisible;
    private WorkspacePanelSlot _selectedSlot;
    private double _width;
    private double _height;

    public WorkspacePanelLayoutEditorViewModel(
        WorkspacePanelRegistration registration,
        PanelPlacementSnapshot placement)
    {
        _registration = registration;
        RefreshAllowedSlots(placement.Slot);
        _isVisible = placement.IsVisible;
        _width = NormalizeWidth(placement.Width);
        _height = Math.Clamp(
            placement.Height,
            MinimumHeight,
            MaximumHeight);
    }

    public string PanelId => _registration.Contribution.Id;
    public string DisplayName => _registration.Contribution.DisplayName;
    public string SourceDescription => _registration.IsBuiltIn
        ? "Built-in Hypershell panel"
        : $"Approved extension panel · {_registration.OwnerId}";
    public bool IsExtension => !_registration.IsBuiltIn;
    public IReadOnlyList<WorkspacePanelSlot> AllowedSlots => _allowedSlots;
    public double MinimumWidth => SelectedSlot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right
        ? WorkspaceLayoutService.DockedPanelMinimumWidth
        : _registration.Contribution.Placement.MinimumWidth;
    public double MaximumWidth => SelectedSlot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right
        ? WorkspaceLayoutService.DockedPanelMaximumWidth
        : _registration.Contribution.Placement.MaximumWidth;
    public double MinimumHeight =>
        _registration.Contribution.Placement.MinimumHeight;
    public double MaximumHeight =>
        _registration.Contribution.Placement.MaximumHeight;
    public string HeightRangeText =>
        $"Allowed height: {MinimumHeight:0}–{MaximumHeight:0} px";
    public string HeightText => $"{Height:0} px";
    public bool ShowsHeightControl => SelectedSlot is WorkspacePanelSlot.Bottom
        or WorkspacePanelSlot.Floating;
    public string PlacementModeText => SelectedSlot switch
    {
        WorkspacePanelSlot.Left => "Left side dock · resize from the inner edge",
        WorkspacePanelSlot.Right => "Right side dock · resize from the inner edge",
        WorkspacePanelSlot.Bottom => "Bottom host · height controls the docked panel",
        WorkspacePanelSlot.Floating => "Floating window · resize from the window border",
        _ => SelectedSlot.ToString()
    };

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public WorkspacePanelSlot SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (AllowedSlots.Contains(value))
            {
                if (SetProperty(ref _selectedSlot, value))
                {
                    _width = NormalizeWidth(_width);
                    OnPropertyChanged(nameof(Width));
                    OnPropertyChanged(nameof(MinimumWidth));
                    OnPropertyChanged(nameof(MaximumWidth));
                    OnPropertyChanged(nameof(ShowsHeightControl));
                    OnPropertyChanged(nameof(PlacementModeText));
                }
            }
        }
    }

    public double Width
    {
        get => _width;
        set
        {
            SetProperty(ref _width, NormalizeWidth(value));
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            var normalized = double.IsFinite(value)
                ? Math.Clamp(value, MinimumHeight, MaximumHeight)
                : _registration.Contribution.Placement.PreferredHeight;
            if (SetProperty(ref _height, normalized))
            {
                OnPropertyChanged(nameof(HeightText));
            }
        }
    }

    public PanelPlacementSnapshot Export(int order) =>
        new()
        {
            ExtensionId = _registration.OwnerId,
            PanelId = PanelId,
            Slot = SelectedSlot,
            Replaces = null,
            Order = order,
            Width = Width,
            Height = Height,
            IsVisible = IsVisible
        };

    private void RefreshAllowedSlots(WorkspacePanelSlot requestedSlot)
    {
        var allowed = new List<WorkspacePanelSlot>
        {
            WorkspacePanelSlot.Left,
            WorkspacePanelSlot.Right
        };
        if (!_registration.IsBuiltIn
            && _registration.Contribution.Placement.AllowedSlots.Contains(
                WorkspacePanelSlot.Bottom))
        {
            allowed.Add(WorkspacePanelSlot.Bottom);
        }

        if (!_registration.IsBuiltIn
            && _registration.Contribution.Placement.AllowFloating
            && _registration.Contribution.Placement.AllowedSlots.Contains(
                WorkspacePanelSlot.Floating))
        {
            allowed.Add(WorkspacePanelSlot.Floating);
        }
        if (allowed.Count == 0)
        {
            allowed.Add(_registration.Contribution.Placement.PreferredSlot);
        }

        _allowedSlots = allowed;
        _selectedSlot = allowed.Contains(requestedSlot)
            ? requestedSlot
            : allowed[0];
        OnPropertyChanged(nameof(AllowedSlots));
        OnPropertyChanged(nameof(SelectedSlot));
    }

    private double NormalizeWidth(double value)
    {
        var fallback = Math.Max(
            _registration.Contribution.Placement.PreferredWidth,
            MinimumWidth);
        return double.IsFinite(value)
            ? Math.Clamp(value, MinimumWidth, MaximumWidth)
            : fallback;
    }
}

public enum ExtensionPanelActivationTarget
{
    Left,
    Right,
    Bottom,
    Floating
}

public sealed class ExtensionPackageEditorViewModel
{
    private readonly MainWindowViewModel _mainWindow;

    public ExtensionPackageEditorViewModel(
        DiscoveredExtensionPackage package,
        MainWindowViewModel mainWindow)
    {
        Package = package;
        _mainWindow = mainWindow;
    }

    public DiscoveredExtensionPackage Package { get; }
    public string DisplayName => Package.DisplayName;
    public ExtensionPackageStatus Status => Package.Status;
    public string StatusText => IsContentOnly
        && Package.Status == ExtensionPackageStatus.Validated
        ? HasActiveStaticContent
            ? "VALIDATED · CONTENT ACTIVE"
            : "VALIDATED · NO CONTENT"
        : IsApproved
        ? IsActive
            ? "APPROVED · ACTIVE"
            : "APPROVED · INACTIVE"
        : Package.StatusText;
    public string IdentityText => Package.IdentityText;
    public string PublisherText => Package.PublisherText;
    public string PackageDirectory => Package.PackageDirectory;
    public string DescriptionText => Package.DescriptionText;
    public string ContributionsText => Package.ContributionsText;
    public string CapabilitiesText => Package.CapabilitiesText;
    public string StatusDetail => IsContentOnly
        && Package.Status == ExtensionPackageStatus.Validated
        ? HasActiveStaticContent
            ? "Active Hyperlib entries and document mappings passed their host validation. Document mappings select a host-owned typed provider; package code is never loaded."
            : "The ContentOnly manifest passed, but no declared static contribution is currently active. Review validation diagnostics and document-format conflicts."
        : IsApproved
        ? "This exact version and package hash is approved on this device for isolated local WebView panels and any explicitly declared allowlisted message API. Approval does not travel with a profile."
        : Package.StatusDetail;
    public IReadOnlyList<ExtensionDiagnostic> Diagnostics => Package.Diagnostics;
    public bool HasLibraryContributions =>
        Package.Manifest?.Contributions.Libraries.Count > 0;
    public bool IsManagedHyperlibPackage =>
        IsContentOnly && HasLibraryContributions;
    public bool HasDocumentContributions =>
        Package.Manifest?.Contributions.DocumentFormats.Count > 0;
    public bool ShowsPanelActions =>
        Package.Manifest?.Contributions.Panels.Count > 0;
    public bool HasActiveHyperlibContent => LibraryCandidates.Count > 0;
    public bool HasActiveDocumentMappings =>
        DocumentProviderRegistrations.Any(registration => registration.IsActive);
    public bool HasActiveStaticContent =>
        HasActiveHyperlibContent || HasActiveDocumentMappings;
    public string ContentStatusText
    {
        get
        {
            if (!HasLibraryContributions)
            {
                return string.Empty;
            }

            if (!HasActiveHyperlibContent)
            {
                return "Content is not active. Review this package's validation diagnostics.";
            }

            var reviewStates = LibraryCandidates
                .Select(candidate =>
                    $"{candidate.Contribution.DisplayName}: {candidate.ReviewText}")
                .ToList();
            var entryCount = LibraryCandidates.Sum(candidate => candidate.EntryCount);
            return
                $"Host validation passed · {LibraryCandidates.Count} librar{(LibraryCandidates.Count == 1 ? "y" : "ies")} · "
                + $"{entryCount:N0} entr{(entryCount == 1 ? "y" : "ies")} · "
                + string.Join("  •  ", reviewStates);
        }
    }
    public bool IsApproved => _mainWindow.IsWebPanelApproved(Package);
    public string DocumentProviderStatusText
    {
        get
        {
            if (!HasDocumentContributions)
            {
                return string.Empty;
            }

            if (DocumentProviderRegistrations.Count == 0)
            {
                return "No host mapping was registered. Review this package's validation diagnostics.";
            }

            return string.Join(
                "  •  ",
                DocumentProviderRegistrations.Select(registration =>
                {
                    var extensions = string.Join(", ", registration.FileExtensions);
                    var state = registration.IsActive ? "ACTIVE" : "DORMANT";
                    return $"{extensions} → {registration.LanguageId} · {state} · {registration.StatusMessage}";
                }));
        }
    }
    public bool CanRevoke => IsApproved;
    public bool CanDeactivate => IsApproved && IsActive;
    public bool CanOpenLeft => IsApproved && PanelCandidates.Count > 0;
    public bool CanOpenRight => IsApproved && PanelCandidates.Count > 0;
    public bool CanOpenBottom => IsApproved && PanelCandidates.Any(candidate =>
        candidate.Registration.Contribution.Placement.AllowedSlots.Contains(
            WorkspacePanelSlot.Bottom));
    public bool CanOpenFloating => IsApproved && PanelCandidates.Any(candidate =>
        candidate.Registration.Contribution.Placement.AllowFloating
        && candidate.Registration.Contribution.Placement.AllowedSlots.Contains(
            WorkspacePanelSlot.Floating));
    public string PackageHashText => string.IsNullOrWhiteSpace(Package.PackageSha256)
        ? "Package hash unavailable"
        : $"SHA-256  {Package.PackageSha256}";
    public string ApprovalScopeText => IsContentOnly
        ? ContentOnlyScopeText
        : IsApproved
        ? "Device-local scope: WorkspacePanel"
          + (ApprovedCapabilities.Contains(
              ExtensionCapability.WorkspaceMetadata)
              ? ", WorkspaceMetadata"
              : string.Empty)
          + (PanelMessageApiVersion > 0
              ? $" · host messaging v{PanelMessageApiVersion}"
              : " · host messaging disabled")
        : CanApprove
            ? "Approval available for isolated local web panels"
            : CanApproveReason;

    public bool CanApprove => !IsApproved
                              && _mainWindow.CanApproveWebPanels(
                                  Package,
                                  out _);

    public string CanApproveReason
    {
        get
        {
            _mainWindow.CanApproveWebPanels(Package, out var reason);
            return reason;
        }
    }

    private bool IsActive
    {
        get
        {
            var extensionId = Package.Manifest?.Id;
            return !string.IsNullOrWhiteSpace(extensionId)
                   && _mainWindow.WorkspaceLayout.Panels.Any(panel =>
                       panel.IsVisible
                       && string.Equals(
                           panel.ExtensionId,
                           extensionId,
                           StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool IsContentOnly =>
        Package.Manifest?.Runtime == ExtensionRuntimeKind.ContentOnly;

    private string ContentOnlyScopeText
    {
        get
        {
            var scopes = new[]
            {
                HasLibraryContributions ? "LibraryContent" : string.Empty,
                HasDocumentContributions ? "DocumentProvider mapping" : string.Empty
            }.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            return scopes.Count == 0
                ? "Automatic static scope: no capabilities · no code execution or device approval"
                : $"Automatic static scope: {string.Join(", ", scopes)} only · no code execution or device approval";
        }
    }

    private int PanelMessageApiVersion =>
        Package.Manifest?.Contributions.Panels
            .Select(panel => panel.MessageApiVersion)
            .DefaultIfEmpty()
            .Max()
        ?? 0;

    private IReadOnlySet<ExtensionCapability> ApprovedCapabilities =>
        _mainWindow.GetApprovedPanelCapabilities(Package);

    private IReadOnlyList<ExtensionLibraryCandidate> LibraryCandidates =>
        _mainWindow.ExtensionCatalog.LibraryCandidates
            .Where(candidate =>
                ReferenceEquals(candidate.Package, Package)
                || string.Equals(
                    candidate.Package.Manifest?.Id,
                    Package.Manifest?.Id,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    candidate.Package.PackageSha256,
                    Package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

    private IReadOnlyList<ExtensionPanelCandidate> PanelCandidates =>
        _mainWindow.ExtensionCatalog.PanelCandidates
            .Where(candidate =>
                ReferenceEquals(candidate.Package, Package)
                || string.Equals(
                    candidate.Package.Manifest?.Id,
                    Package.Manifest?.Id,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    candidate.Package.PackageSha256,
                    Package.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

    private IReadOnlyList<DocumentProviderRegistration> DocumentProviderRegistrations =>
        string.IsNullOrWhiteSpace(Package.Manifest?.Id)
            ? []
            : _mainWindow.DocumentProviderCatalog.Registrations
                .Where(registration =>
                    !registration.IsBuiltIn
                    && string.Equals(
                        registration.OwnerId,
                        Package.Manifest.Id,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
}

public sealed class TerminalConfigurationProfileEditorViewModel : ObservableObject
{
    private string _name;
    private string _description;
    private string _tabTitleTemplate;
    private string _tabColor;
    private string _startupCommand;

    public TerminalConfigurationProfileEditorViewModel(
        TerminalConfigurationProfile configuration,
        IEnumerable<TerminalProfile> terminalProfiles)
    {
        Id = configuration.Id;
        _name = configuration.Name;
        _description = configuration.Description;
        _tabTitleTemplate = configuration.TabTitleTemplate;
        _tabColor = configuration.TabColor;
        _startupCommand = configuration.StartupCommand;
        Settings = configuration.Settings.Clone();
        Targets = new ObservableCollection<SelectableSettingsTarget>(
            terminalProfiles.Select(profile => new SelectableSettingsTarget(
                profile.Id,
                profile.DisplayName,
                profile.AvailabilityMessage,
                configuration.TargetTerminalProfileIds.Contains(
                    profile.Id,
                    StringComparer.OrdinalIgnoreCase),
                profile.IsAvailable)));
    }

    public string Id { get; }
    public TerminalSettings Settings { get; }
    public ObservableCollection<SelectableSettingsTarget> Targets { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string TabTitleTemplate
    {
        get => _tabTitleTemplate;
        set => SetProperty(ref _tabTitleTemplate, value);
    }

    public string TabColor
    {
        get => _tabColor;
        set => SetProperty(ref _tabColor, value);
    }

    public string StartupCommand
    {
        get => _startupCommand;
        set => SetProperty(ref _startupCommand, value);
    }

    public TerminalConfigurationProfile Export() =>
        new()
        {
            Id = Id,
            Name = Name.Trim(),
            Description = Description.Trim(),
            TargetTerminalProfileIds = Targets
                .Where(target => target.IsSelected)
                .Select(target => target.Id)
                .ToList(),
            Settings = Settings.Clone(),
            TabTitleTemplate = TabTitleTemplate.Trim(),
            TabColor = TabColor.Trim(),
            StartupCommand = StartupCommand.Trim()
        };
}

public sealed class ProjectEditorViewModel : ObservableObject
{
    private string _name;

    public ProjectEditorViewModel(ProjectDefinition project)
    {
        Id = project.Id;
        _name = project.Name;
        Directories = new ObservableCollection<string>(project.Directories);
    }

    public string Id { get; }
    public ObservableCollection<string> Directories { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public ProjectDefinition Export() =>
        new()
        {
            Id = Id,
            Name = Name.Trim(),
            Directories = new ObservableCollection<string>(
                Directories
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
        };
}

public sealed class SelectableSettingsTarget : ObservableObject
{
    private bool _isSelected;

    public SelectableSettingsTarget(
        string id,
        string name,
        string detail,
        bool isSelected,
        bool isAvailable = true)
    {
        Id = id;
        Name = name;
        Detail = detail;
        _isSelected = isSelected;
        IsAvailable = isAvailable;
    }

    public string Id { get; }
    public string Name { get; }
    public string Detail { get; }
    public bool IsAvailable { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
}
