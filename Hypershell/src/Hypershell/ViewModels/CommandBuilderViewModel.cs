using Hypershell.Infrastructure;
using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;

namespace Hypershell.ViewModels;

public sealed partial class CommandBuilderViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private CommandCatalogService? _catalogService;
    private bool _isSynchronizingLibraries;
    private bool _isApplyingEnvironmentSnapshot;
    private string? _selectedAction;
    private string? _selectedObject;
    private CommandDefinition? _selectedCommand;
    private CommandSearchResultViewModel? _selectedSearchResult;
    private EnvironmentProfile? _selectedEnvironment;
    private string _searchQuery = string.Empty;
    private string _commandText = string.Empty;
    private string _stagedStatementText = "Choose an Action and Object to preview the current statement.";
    private string _statusMessage = "Loading command libraries…";
    private string _newProfileName = string.Empty;
    private string _newProfileLanguage = "PowerShell";
    private string _selectedEnvironmentNameDraft = string.Empty;
    private string _selectedEnvironmentDescriptionDraft = string.Empty;
    private bool _isLoading;
    private bool _isSelectedCommandMetadataLoading;
    private bool _isCommandGuideLoading;
    private bool _isProfileCreatorOpen;
    private bool _isLibraryPickerExpanded;
    private bool _isFlagsExpanded;
    private bool _isCommandGuideExpanded;
    private bool _isActionDescriptionExpanded = true;
    private bool _isObjectDescriptionExpanded = true;
    private CommandGuideContent? _selectedCommandGuide;
    private int _parameterLoadVersion;
    private int _commandGuideLoadVersion;

    public CommandBuilderViewModel(
        AppSettings settings,
        SettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _selectedEnvironment = _settings.Environments.FirstOrDefault(environment => environment.Id == _settings.ActiveEnvironmentId)
            ?? _settings.Environments.First();
        _selectedInputSyntax = NormalizeLanguageName(
            _settings.CommandBuilder.ActiveLanguage);
        if (string.IsNullOrWhiteSpace(_selectedInputSyntax))
        {
            _selectedInputSyntax = NormalizeLanguageName(
                _selectedEnvironment.Language);
        }

        if (string.IsNullOrWhiteSpace(_selectedInputSyntax))
        {
            _selectedInputSyntax = "PowerShell";
        }

        _settings.CommandBuilder.ActiveLanguage = _selectedInputSyntax;
        _newProfileLanguage = NormalizeLanguageName(
            _selectedEnvironment.Language);
        _selectedEnvironmentNameDraft = _selectedEnvironment.Name;
        _selectedEnvironmentDescriptionDraft =
            _selectedEnvironment.Description;

        AddStatementCommand = new RelayCommand(
            AddSelectedCommand,
            () => CanAddCurrentStatement);
        ClearBuilderCommand = new RelayCommand(ClearBuilder);
        CopyComposedTextCommand = new RelayCommand(CopyComposedText);
        InsertIntoEditorCommand = new RelayCommand(InsertIntoEditor);
        SendToTerminalCommand = new RelayCommand(SendToTerminal);
        RunBuilderCommand = new RelayCommand(RunCommand);
        PrimaryActionCommand = new RelayCommand(ExecutePrimaryAction);
        RemoveStatementCommand = new RelayCommand(parameter => RemoveStatement(parameter as CommandSegmentViewModel));
        ToggleProfileCreatorCommand = new RelayCommand(ToggleProfileCreator);
        CreateProfileCommand = new RelayCommand(CreateProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile);
        SaveProfileCommand = new RelayCommand(SaveSelectedProfile);
        SelectAllLibrariesCommand = new RelayCommand(
            () => SetLibrariesSelected(true),
            () => CanEditSelectedLanguageTab);
        ClearLibrarySelectionCommand = new RelayCommand(
            () => SetLibrariesSelected(false),
            () => CanEditSelectedLanguageTab);
        ObjectsView = CollectionViewSource.GetDefaultView(Objects);
        ObjectsView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(CommandObjectOption.ModuleName)));
        InitializeCSharpComposer();
    }

    public ObservableCollection<CommandLibrary> Libraries { get; } = [];
    public ObservableCollection<CommandDefinition> Commands { get; } = [];
    public ObservableCollection<CommandSearchResultViewModel> SearchResults
    { get; } = [];
    public ObservableCollection<LibrarySelectionViewModel> LibrarySelections { get; } = [];
    public ObservableCollection<string> Actions { get; } = [];
    public ObservableCollection<CommandObjectOption> Objects { get; } = [];
    public ICollectionView ObjectsView { get; }
    public ObservableCollection<CommandDefinition> MatchingCommands { get; } = [];
    public ObservableCollection<ParameterEditorViewModel> ParameterEditors { get; } = [];
    public ObservableCollection<CommandSegmentViewModel> Pipeline { get; } = [];
    public ObservableCollection<EnvironmentProfile> Environments => _settings.Environments;

    internal void ApplyEnvironmentProfiles(
        IEnumerable<EnvironmentProfile> environments,
        string activeEnvironmentId)
    {
        ArgumentNullException.ThrowIfNull(environments);
        var imported = environments
            .Select(environment => environment.Clone())
            .ToList();
        if (imported.Count == 0)
        {
            throw new InvalidOperationException(
                "A portable profile must contain at least one command profile.");
        }

        _isApplyingEnvironmentSnapshot = true;
        try
        {
            Environments.Clear();
            foreach (var environment in imported)
            {
                Environments.Add(environment);
            }

            SelectedEnvironment = Environments.FirstOrDefault(environment =>
                string.Equals(
                    environment.Id,
                    activeEnvironmentId,
                    StringComparison.OrdinalIgnoreCase))
                ?? Environments[0];
        }
        finally
        {
            _isApplyingEnvironmentSnapshot = false;
        }

        var configuredLanguage = NormalizeLanguageName(
            _settings.CommandBuilder.ActiveLanguage);
        if (!string.IsNullOrWhiteSpace(configuredLanguage)
            && !LanguagesMatch(configuredLanguage, SelectedInputSyntax))
        {
            SelectedInputSyntax = configuredLanguage;
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                return;
            }

            RefreshSearchResults();
            OnPropertyChanged(nameof(HasSearchQuery));
            OnPropertyChanged(nameof(SearchResultSummary));
        }
    }

    public CommandSearchResultViewModel? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (!SetProperty(ref _selectedSearchResult, value) || value is null)
            {
                return;
            }

            OpenSearchResult(value);
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasNoSearchResults => HasSearchQuery && SearchResults.Count == 0;
    public string SearchResultSummary => !HasSearchQuery
        ? IsCSharpComposerWorkspace
            ? HasCSharpComposerFramework
                ? $"Search only {SelectedSurfaceLibrary?.DisplayName} and its exposed surface."
                : "Select a framework before searching."
            : SelectedBuilderSurface is null
                ? $"Select a {SelectedInputSyntax} library before searching."
                : $"Search only {SelectedBuilderSurface.DisplayName}."
        : SearchResults.Count == 0
            ? IsCSharpComposerWorkspace
                ? "No entries in the selected framework match this search."
                : "No entries in the selected library match this search."
            : $"{SearchResults.Count:N0} matching entr{(SearchResults.Count == 1 ? "y" : "ies")}";

    public string? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetProperty(ref _selectedAction, value))
            {
                return;
            }

            // An object only has meaning inside its chosen action. Clearing it
            // also dismisses any flags from the previously selected command.
            SelectedObject = null;
            RefreshObjects();
            IsActionDescriptionExpanded = true;
            IsFlagsExpanded = false;
            IsCommandGuideExpanded = false;
            OnPropertyChanged(nameof(HasSelectedAction));
            OnPropertyChanged(nameof(SelectedActionDescription));
            OnPropertyChanged(nameof(HasSelectedActionDescription));
            OnPropertyChanged(nameof(SelectedActionDescriptionHeading));
            OnPropertyChanged(nameof(ActionHint));
            OnPropertyChanged(nameof(ObjectHint));
            OnPropertyChanged(nameof(FlagsSelectorSummary));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
        }
    }

    public string? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (!SetProperty(ref _selectedObject, value))
            {
                return;
            }

            RefreshMatchingCommands();
            IsObjectDescriptionExpanded = true;
            IsFlagsExpanded = false;
            IsCommandGuideExpanded = false;
            OnPropertyChanged(nameof(HasSelectedObject));
            OnPropertyChanged(nameof(SelectedObjectDescription));
            OnPropertyChanged(nameof(HasSelectedObjectDescription));
            OnPropertyChanged(nameof(SelectedObjectDescriptionHeading));
            OnPropertyChanged(nameof(ObjectHint));
            OnPropertyChanged(nameof(FlagsSelectorSummary));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
        }
    }

    public bool HasSelectedAction => !string.IsNullOrWhiteSpace(SelectedAction);
    public bool HasSelectedObject => !string.IsNullOrWhiteSpace(SelectedObject);
    public bool HasSelectedCommand => SelectedCommand is not null;
    public bool HasCommandVariants => MatchingCommands.Count > 1;
    public string PrimaryFacetHeading =>
        IsDotNetToolchainSurface ? "1 · COMMAND GROUP" : "1 · ACTION";
    public string SecondaryFacetHeading =>
        IsDotNetToolchainSurface ? "2 · COMMAND" : "2 · OBJECT";
    public string PrimaryFacetToolTip => IsDotNetToolchainSurface
        ? "Choose an official command group reported by the installed dotnet help."
        : "Choose the action or verb to perform.";
    public string SecondaryFacetToolTip => IsDotNetToolchainSurface
        ? "Choose a command from the selected installed group."
        : "Choose an Action to reveal associated Objects.";

    public string ActionHint => IsLoading
        ? "Discovering commands…"
        : Actions.Count == 0
            ? "No command actions are available in the selected library."
            : BuildActionHint();

    public string ObjectHint => !HasSelectedAction
        ? IsDotNetToolchainSurface
            ? "Choose a command group to reveal its commands."
            : "Choose an Action to reveal associated Objects."
        : Objects.Count == 0
            ? IsDotNetToolchainSurface
                ? "No installed commands are available in this group."
                : "No objects in the selected library match this action."
            : IsDotNetToolchainSurface
                ? "Choose the dotnet command to compose."
                : "Choose what the action applies to.";

    public string SelectedActionDescription => ResolveSelectedActionDescription();
    public bool HasSelectedActionDescription =>
        !string.IsNullOrWhiteSpace(SelectedActionDescription);
    public string SelectedActionDescriptionHeading =>
        string.IsNullOrWhiteSpace(SelectedAction)
            ? IsDotNetToolchainSurface
                ? "ABOUT COMMAND GROUP"
                : "ABOUT ACTION"
            : IsDotNetToolchainSurface
                ? $"GROUP · {SelectedAction.ToUpperInvariant()}"
                : $"ACTION · {SelectedAction.ToUpperInvariant()}";

    public string SelectedObjectDescription => ResolveSelectedObjectDescription();
    public bool HasSelectedObjectDescription =>
        !string.IsNullOrWhiteSpace(SelectedObjectDescription);
    public string SelectedObjectDescriptionHeading =>
        string.IsNullOrWhiteSpace(SelectedObject)
            ? IsDotNetToolchainSurface
                ? "ABOUT COMMAND"
                : "ABOUT OBJECT"
            : IsDotNetToolchainSurface
                ? $"COMMAND · {SelectedObject.ToUpperInvariant()}"
                : $"OBJECT · {SelectedObject.ToUpperInvariant()}";

    public string CommandVariantHint => "More than one command matches this action and object. Choose its implementation.";

    public CommandDefinition? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (!SetProperty(ref _selectedCommand, value))
            {
                return;
            }

            _parameterLoadVersion++;
            _commandGuideLoadVersion++;
            IsFlagsExpanded = false;
            IsCommandGuideExpanded = false;
            ClearParameterEditors();
            SelectedCommandGuide = null;
            if (value is null)
            {
                IsSelectedCommandMetadataLoading = false;
                IsCommandGuideLoading = false;
            }
            else
            {
                IsCommandGuideLoading = true;
                LoadSelectedCommandGuideAsync(value, _commandGuideLoadVersion);
                if (value.HasDeferredGuide)
                {
                    IsSelectedCommandMetadataLoading = true;
                }
                else if (value.ExecutionTarget != ExecutionTarget.PowerShell
                    || value.Parameters.Count > 0
                    || value.CompositionMode
                    == CommandCompositionMode.Statement)
                {
                    IsSelectedCommandMetadataLoading = false;
                    PopulateParameterEditors(value);
                }
                else
                {
                    IsSelectedCommandMetadataLoading = true;
                    LoadSelectedCommandMetadataAsync(value, _parameterLoadVersion);
                }
            }

            OnPropertyChanged(nameof(SelectedCommandDescription));
            OnPropertyChanged(nameof(SelectedCommandTarget));
            OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
            OnPropertyChanged(nameof(ParameterSectionHeading));
            OnPropertyChanged(nameof(HasSelectedCommand));
            OnPropertyChanged(nameof(SelectedObjectDescription));
            OnPropertyChanged(nameof(HasSelectedObjectDescription));
            OnPropertyChanged(nameof(FlagsSelectorSummary));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
            OnPropertyChanged(nameof(IsCSharpLanguagePattern));
            RefreshStagedStatement();
            AddStatementCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedCommandDescription => SelectedCommand?.Description
        ?? (IsCSharpRecipeSurface
            ? "Choose a library recipe to expose its configurable inputs."
            : IsCSharpSurface
            ? IsCSharpLanguageSurface
                ? "Choose a language pattern to expose its typed inputs."
                : "Choose a namespace, type, member, and overload to expose its typed arguments."
            : IsDotNetToolchainSurface
                ? "Choose a command group and command to expose its arguments."
                : "Choose an action and object to expose its metadata-driven parameters.");
    public string SelectedCommandTarget => SelectedCommand is null
        ? "Select a command to see its execution target."
        : !SelectedCommand.IsExecutionSupported
            ? $"{SelectedEnvironmentLanguage} project/editor target · dependency required"
        : SelectedCommand.ExecutionTarget switch
        {
            ExecutionTarget.CSharp => "C# runner target",
            ExecutionTarget.External => GetExternalCommandTargetLabel(),
            _ => IsDotNetToolchainCommand(SelectedCommand)
                ? ".NET CLI through a PowerShell terminal"
                : SelectedCommand.PreferredShellKind == TerminalShellKind.WindowsPowerShell
                ? "Windows PowerShell 5.1 terminal target"
                : "PowerShell terminal target"
        };

    private string GetExternalCommandTargetLabel() =>
        CommandLanguageRegistry.TryGet(
            SelectedEnvironmentLanguage,
            out var language)
        && !language.SupportsTerminalExecution
            ? $"{language.DisplayName} editor target"
            : $"{CommandLanguageRegistry.GetRuntimeDisplayName(SelectedCommand?.PreferredShellKind ?? TerminalShellKind.External)} terminal target";

    public string ParameterSectionHeading =>
        IsCSharpComposerWorkspace || IsHierarchySurface
            ? "PARAMETERS"
            : "FLAGS";

    public string ParameterSelectorHint => IsCSharpComposerWorkspace
        ? "Edit the arguments exposed by the selected command, method, constructor, or property."
        : IsCSharpSurface
            ? IsCSharpLanguageSurface
                ? "Choose a language pattern to reveal its typed inputs."
                : "Choose an overload to reveal its typed arguments."
        : IsDotNetToolchainSurface
            ? "Choose a dotnet command to reveal its positional arguments and options."
        : IsHierarchySurface
            ? "Choose a library entry to reveal its typed inputs."
            : "Choose an Object to reveal associated Flags.";

    public string SelectedCommandMetadataStatus => SelectedCommand is null
        ? string.Empty
        : IsSelectedCommandMetadataLoading
            ? "Loading parameter metadata…"
            : ParameterEditors.Count == 0
                ? IsDotNetToolchainSurface
                    ? "This dotnet operation does not require additional arguments."
                    : "No typed parameters are available for this command. Add arguments directly in the composed command field."
                : IsDotNetToolchainSurface
                    ? $"{ParameterEditors.Count} command-line argument field" +
                      $"{(ParameterEditors.Count == 1 ? string.Empty : "s")} available."
                    : $"{ParameterEditors.Count} metadata-driven parameters available.";

    public CommandGuideContent? SelectedCommandGuide
    {
        get => _selectedCommandGuide;
        private set
        {
            if (SetProperty(ref _selectedCommandGuide, value))
            {
                OnPropertyChanged(nameof(HasSelectedCommandGuide));
                OnPropertyChanged(nameof(SelectedActionDescription));
                OnPropertyChanged(nameof(HasSelectedActionDescription));
                OnPropertyChanged(nameof(SelectedObjectDescription));
                OnPropertyChanged(nameof(HasSelectedObjectDescription));
            }
        }
    }

    public bool HasSelectedCommandGuide => SelectedCommandGuide is not null;

    public bool IsCommandGuideLoading
    {
        get => _isCommandGuideLoading;
        private set => SetProperty(ref _isCommandGuideLoading, value);
    }

    public string CommandGuideContextLabel => SelectedCommand is null
        ? string.Empty
        : IsDotNetRuntimeCommand(SelectedCommand)
            ? $"{SelectedCommand.NamespaceName} → " +
              $"{SelectedCommand.TypeName} → " +
              $"{SelectedCommand.MemberName} · " +
              $"{(SelectedCommand.ExecutionTarget == ExecutionTarget.CSharp ? "C#" : "PowerShell")}"
        : IsHierarchySurface
            ? string.IsNullOrWhiteSpace(HierarchyBreadcrumb)
                ? SelectedCommand.DisplayName
                : HierarchyBreadcrumb
        : SelectedCommand.ExecutionTarget == ExecutionTarget.CSharp
            ? string.IsNullOrWhiteSpace(SelectedCommand.NamespaceName)
                ? $"C# language pattern · {SelectedCommand.DisplayName}"
                : $"{SelectedCommand.NamespaceName} → {SelectedCommand.TypeName} → {SelectedCommand.MemberName}"
            : IsDotNetToolchainCommand(SelectedCommand)
                ? $"{SelectedAction} → {SelectedCommand.DisplayName} · " +
                  $"{SelectedCommand.ModuleName}"
                : $"{SelectedAction} → {SelectedObject} · {SelectedCommand.ModuleName}";

    public bool IsFlagsExpanded
    {
        get => _isFlagsExpanded;
        set => SetProperty(ref _isFlagsExpanded, value);
    }

    public bool IsCommandGuideExpanded
    {
        get => _isCommandGuideExpanded;
        set => SetProperty(ref _isCommandGuideExpanded, value);
    }

    public bool IsActionDescriptionExpanded
    {
        get => _isActionDescriptionExpanded;
        set => SetProperty(ref _isActionDescriptionExpanded, value);
    }

    public bool IsObjectDescriptionExpanded
    {
        get => _isObjectDescriptionExpanded;
        set => SetProperty(ref _isObjectDescriptionExpanded, value);
    }

    public bool HasParameterEditors => ParameterEditors.Count > 0;

    public string FlagsSelectorSummary
    {
        get
        {
            if (SelectedCommand is null
                || (!IsCSharpSurface
                    && !IsHierarchySurface
                    && !HasSelectedObject))
            {
                return string.Empty;
            }

            if (IsSelectedCommandMetadataLoading)
            {
                return "Loading available flags…";
            }

            var noun = SelectedCommand.ExecutionTarget == ExecutionTarget.CSharp
                       || IsDotNetRuntimeCommand(SelectedCommand)
                       || IsDotNetToolchainCommand(SelectedCommand)
                ? "argument"
                : "flag";
            var available = ParameterEditors.Count;
            if (available == 0)
            {
                return $"No {noun}s available for this command";
            }

            var selected = ParameterEditors.Count(editor => editor.IsIncluded);
            return selected == 0
                ? $"{available} {noun}{(available == 1 ? string.Empty : "s")} available"
                : $"{selected} of {available} {noun}{(available == 1 ? string.Empty : "s")} selected";
        }
    }

    public EnvironmentProfile? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            if (!SetProperty(ref _selectedEnvironment, value) || value is null)
            {
                return;
            }

            _settings.ActiveEnvironmentId = value.Id;
            LoadSelectedProfileDraft(value);
            OnPropertyChanged(nameof(SelectedEnvironmentName));
            OnPropertyChanged(nameof(SelectedEnvironmentDescription));
            DuplicateProfileCommand.RaiseCanExecuteChanged();
            DeleteProfileCommand.RaiseCanExecuteChanged();
            SaveProfileCommand.RaiseCanExecuteChanged();
            if (!_isApplyingEnvironmentSnapshot)
            {
                PersistSettings();
            }
        }
    }

    public string SelectedEnvironmentName
    {
        get => _selectedEnvironmentNameDraft;
        set
        {
            if (!SetProperty(
                    ref _selectedEnvironmentNameDraft,
                    value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(ProfileSaveHint));
        }
    }

    public string SelectedEnvironmentDescription
    {
        get => _selectedEnvironmentDescriptionDraft;
        set
        {
            if (!SetProperty(
                    ref _selectedEnvironmentDescriptionDraft,
                    value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(ProfileSaveHint));
        }
    }

    public string ProfileSaveHint =>
        GetProfileNameValidationError(
            SelectedEnvironmentName,
            SelectedEnvironment) is { } error
            ? error
            : "Press Save to apply the profile name, description, and current library selection.";

    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

    public string StagedStatementText
    {
        get => _stagedStatementText;
        private set => SetProperty(ref _stagedStatementText, value);
    }

    public bool HasPipelineStatements => Pipeline.Count > 0;
    public bool IsPipelineEmpty => Pipeline.Count == 0;
    public string PipelineSummaryLabel =>
        $"{Pipeline.Count} " + (Pipeline.Count == 1
            ? CompositionUnitSingular
            : CompositionUnitPlural);
    public string PipelineEmptyHint => IsCSharpComposerWorkspace
        ? "No statements yet. Add language or API items above to build the script."
        : $"No {CompositionUnitPlural} yet. Choose an entry above, set any parameters, then add it here.";
    private CommandLanguageDefinition? SelectedLanguageDefinition =>
        CommandLanguageRegistry.TryGet(
            SelectedInputSyntax,
            out var definition)
            ? definition
            : null;
    public string CompositionUnitSingular =>
        SelectedLanguageDefinition?.CompositionUnitSingular ?? "item";
    public string CompositionUnitPlural =>
        SelectedLanguageDefinition?.CompositionUnitPlural ?? "items";
    public string PipelineHeading =>
        SelectedLanguageDefinition?.PipelineHeading ?? "COMPOSITION";
    public string ComposedTextHeading =>
        SelectedLanguageDefinition?.ComposedTextHeading
        ?? (IsCSharpComposerWorkspace ? "SCRIPT" : "COMPOSED SOURCE");
    public bool HasTerminalActions =>
        SelectedLanguageDefinition?.SupportsTerminalExecution == true;
    public bool HasEditorOnlyActions => !HasTerminalActions;
    public bool IsEditorPrimaryAction =>
        SelectedLanguageDefinition?.PrimaryDestination
            == CommandBuilderPrimaryDestination.Editor;
    public bool IsTerminalPrimaryAction =>
        !IsEditorPrimaryAction && HasTerminalActions;
    public string AddStatementActionLabel => $"Add {CompositionUnitSingular}";
    public string AddStatementActionToolTip =>
        $"Add the previewed {CompositionUnitSingular} to the {PipelineHeading.ToLowerInvariant()}.";
    public string EditorActionLabel =>
        HasTerminalActions
            ? "To editor"
            : CommandLanguageRegistry.NormalizeId(SelectedInputSyntax) switch
            {
                "html" => "Insert markup",
                "css" => "Insert styles",
                "web" => "Insert snippet",
                _ => "Insert source"
            };
    public string EditorActionToolTip =>
        $"Insert the exact composed source at the cursor in the active compatible " +
        $"{EditorLanguageCatalog.GetLanguage(
            CommandLanguageRegistry.GetPreferredEditorLanguageId(
                SelectedInputSyntax)).DisplayName} document" +
        (IsEditorPrimaryAction
            ? ". Ctrl+Enter from the composed source performs this action."
            : ".");
    public string TerminalActionLabel => "Stage";
    public string TerminalActionToolTip =>
        $"Load the exact composed source into the " +
        $"{CommandLanguageRegistry.GetRuntimeDisplayName(
            SelectedLanguageDefinition?.PreferredShellKind
            ?? TerminalShellKind.External)} terminal without running it.";
    public string RunActionToolTip =>
        (SelectedLanguageDefinition?.RunActionDescription
         ?? $"Run the composition in a compatible " +
            $"{CommandLanguageRegistry.GetRuntimeDisplayName(
                SelectedLanguageDefinition?.PreferredShellKind
                ?? TerminalShellKind.External)} terminal.") +
        (IsTerminalPrimaryAction
            ? " Ctrl+Enter from the composed source performs this action."
            : string.Empty);
    public string PrimaryActionToolTip => IsEditorPrimaryAction
        ? EditorActionToolTip
        : RunActionToolTip;
    public string ComposedTextToolTip => HasTerminalActions
        ? "This is the exact source used by Copy, To editor, Stage, and Run. Changing the composition rebuilds this field."
        : "This is the exact source used by Copy and editor insertion. Changing the composition rebuilds this field.";

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasVisibleStatusMessage));
                OnPropertyChanged(nameof(IsStatusMessageError));
            }
        }
    }

    public bool HasVisibleStatusMessage =>
        !string.IsNullOrWhiteSpace(StatusMessage)
        && (!IsCSharpComposerWorkspace || IsStatusMessageError);

    public bool IsStatusMessageError =>
        StatusMessage.StartsWith("Unable", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Select", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Choose", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Enter", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Build", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Add a", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("There is no", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("No item", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Open a compatible", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.StartsWith("Wait", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.Contains(" cannot ", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.Contains(" incompatible ", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.Contains(" does not have ", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.Contains(" requires ", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.Contains(" editor-only", StringComparison.OrdinalIgnoreCase)
        || StatusMessage.Contains(" not available", StringComparison.OrdinalIgnoreCase);

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (SetProperty(
                    ref _newProfileName,
                    value ?? string.Empty))
            {
                OnPropertyChanged(nameof(NewProfileValidationMessage));
            }
        }
    }

    public IReadOnlyList<string> ProfileLanguageOptions =>
        CommandLanguageRegistry.OrderLanguages(
            CommandLanguageRegistry.ProfileLanguageOptions.Concat(
                Libraries.Select(library => library.Language)));

    public string NewProfileLanguage
    {
        get => _newProfileLanguage;
        set
        {
            var normalized = NormalizeLanguageName(value);
            if (SetProperty(ref _newProfileLanguage, normalized))
            {
                OnPropertyChanged(nameof(NewProfileLanguageHint));
                OnPropertyChanged(nameof(NewProfileValidationMessage));
            }
        }
    }

    public string NewProfileLanguageHint =>
        $"Creates an empty {NewProfileLanguage} profile. Choose its " +
        "libraries after creation, then press Save.";
    public string NewProfileValidationMessage =>
        GetNewProfileValidationError() ??
        $"Ready to create the {NewProfileName.Trim()} profile.";

    public bool IsProfileCreatorOpen
    {
        get => _isProfileCreatorOpen;
        private set
        {
            if (SetProperty(ref _isProfileCreatorOpen, value))
            {
                OnPropertyChanged(nameof(ProfileCreatorToggleLabel));
            }
        }
    }

    public string ProfileCreatorToggleLabel =>
        IsProfileCreatorOpen ? "Cancel" : "New";

    public bool IsLibraryPickerExpanded
    {
        get => _isLibraryPickerExpanded;
        set => SetProperty(ref _isLibraryPickerExpanded, value);
    }

    public string ActiveLibraryCountLabel => $"{LibrarySelections.Count(selection => selection.IsSelected)} active";

    public string ActiveLibrarySummary
    {
        get
        {
            var names = LibrarySelections
                .Where(selection => selection.IsSelected)
                .Select(selection => selection.DisplayName)
                .ToList();
            return names.Count == 0 ? "No command lexicon sources selected" : string.Join(" · ", names);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ActionHint));
            }
        }
    }

    public bool IsSelectedCommandMetadataLoading
    {
        get => _isSelectedCommandMetadataLoading;
        private set
        {
            if (SetProperty(ref _isSelectedCommandMetadataLoading, value))
            {
                OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
                OnPropertyChanged(nameof(FlagsSelectorSummary));
                RefreshStagedStatement();
                AddStatementCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CommandLibraryDirectory => _settingsService.CommandLibraryDirectory;

    public RelayCommand AddStatementCommand { get; }
    public RelayCommand ClearBuilderCommand { get; }
    public RelayCommand CopyComposedTextCommand { get; }
    public RelayCommand InsertIntoEditorCommand { get; }
    public RelayCommand SendToTerminalCommand { get; }
    public RelayCommand RunBuilderCommand { get; }
    public RelayCommand PrimaryActionCommand { get; }
    public RelayCommand RemoveStatementCommand { get; }
    public RelayCommand ToggleProfileCreatorCommand { get; }
    public RelayCommand CreateProfileCommand { get; }
    public RelayCommand DuplicateProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand SelectAllLibrariesCommand { get; }
    public RelayCommand ClearLibrarySelectionCommand { get; }

    public event EventHandler<CommandExecutionRequest>? CommandDispatchRequested;
    public event EventHandler<EditorInsertionRequest>? EditorInsertionRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler? SettingsSaveRequested;

    public async Task LoadLibrariesAsync(CommandCatalogService catalogService)
    {
        _catalogService = catalogService;
        await LoadLibrariesCoreAsync();
    }

    private async Task LoadLibrariesCoreAsync()
    {
        if (_catalogService is null)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = "Discovering command libraries…";

        try
        {
            var libraries = await _catalogService.LoadAsync();
            var dotNetApiTasks = libraries
                .Where(library =>
                    CSharpFrameworkCatalogService
                        .IsIndexedManagedApiLibrary(library)
                    && string.Equals(
                        library.FrameworkName,
                        ".NET",
                        StringComparison.OrdinalIgnoreCase))
                .Select(_dotNetApiIndexService.LoadAsync)
                .ToList();
            Libraries.Clear();
            Commands.Clear();
            foreach (var library in libraries.OrderBy(library => library.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                Libraries.Add(library);
                foreach (var command in library.Commands)
                {
                    Commands.Add(command);
                }
            }
            OnPropertyChanged(nameof(ProfileLanguageOptions));
            OnPropertyChanged(nameof(InputSyntaxOptions));
            if (!InputSyntaxOptions.Contains(
                    SelectedInputSyntax,
                    StringComparer.OrdinalIgnoreCase))
            {
                SelectedInputSyntax = InputSyntaxOptions.FirstOrDefault()
                                      ?? "PowerShell";
            }

            if (EnsureLibraryProfiles())
            {
                // Profiles introduced by a newly discovered library are part of
                // the application setup, not an unsaved user edit.
                _settingsService.Save(_settings);
            }

            SynchronizeLibrarySelections();
            await MaterializeBuilderSurfaceAsync(
                SelectedBuilderSurface,
                updateStatus: false);
            var dotNetApiIndexes = await Task.WhenAll(dotNetApiTasks);
            ApplyDotNetApiIndexes(dotNetApiIndexes);
            StatusMessage =
                $"Loaded {libraries.Sum(library => library.EffectiveEntryCount):N0} indexed entries plus " +
                $"{dotNetApiIndexes.Sum(index => index.OverloadCount):N0} public " +
                $".NET API variations across {dotNetApiIndexes.Length:N0} installed " +
                $"framework surface{(dotNetApiIndexes.Length == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to load command libraries: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void InsertPaths(IEnumerable<string> paths)
    {
        var values = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (values.Count == 0)
        {
            return;
        }

        var targetParameter = FindPathParameter()
            ?? ParameterEditors.FirstOrDefault(parameter => parameter.IsIncluded && !parameter.Definition.IsSwitch)
            ?? ParameterEditors.FirstOrDefault(parameter => !parameter.Definition.IsSwitch);
        var formattedPaths = SelectedCommand?.ExecutionTarget == ExecutionTarget.CSharp
            ? string.Join(", ", values.Select(ToCSharpStringLiteral))
            : string.Join(
                ", ",
                values.Select(path => QuotePath(
                    path,
                    SelectedCommand?.ExecutionTarget == ExecutionTarget.External)));

        if (targetParameter is not null)
        {
            targetParameter.IsIncluded = true;
            targetParameter.Value = formattedPaths;
            StatusMessage = $"Inserted {values.Count} path{(values.Count == 1 ? string.Empty : "s")} into {targetParameter.Definition.DisplayName}.";
            return;
        }

        CommandText = string.IsNullOrWhiteSpace(CommandText)
            ? formattedPaths
            : $"{CommandText} {formattedPaths}";
        StatusMessage = $"Inserted {values.Count} path{(values.Count == 1 ? string.Empty : "s")} into the command text.";
    }

    private void SynchronizeLibrarySelections()
    {
        _isSynchronizingLibraries = true;
        try
        {
            LibrarySelections.Clear();
            var inputLanguage = SelectedInputSyntax;
            foreach (var library in Libraries)
            {
                var isCompatible = LanguagesMatch(
                    inputLanguage,
                    library.Language);
                var selection = new LibrarySelectionViewModel(
                    library,
                    isCompatible,
                    inputLanguage);
                selection.SelectionChanged += OnLibrarySelectionChanged;
                LibrarySelections.Add(selection);
            }
        }
        finally
        {
            _isSynchronizingLibraries = false;
        }

        RefreshLibraryLanguageTabs();
        NotifyLibrarySelectionSummaryChanged();
        RefreshBuilderSurfaces();
        RefreshSearchResults();
    }

    private void OnLibrarySelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isSynchronizingLibraries || SelectedEnvironment is null)
        {
            return;
        }

        ApplyLibrarySelectionToProfile();
    }

    private void SetLibrariesSelected(bool isSelected)
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        _isSynchronizingLibraries = true;
        try
        {
            var selections = SelectedLibraryLanguageTab?.Libraries
                             ?? LibrarySelections;
            foreach (var selection in selections.Where(selection =>
                         selection.IsCompatibleWithProfile))
            {
                selection.IsSelected = isSelected;
            }
        }
        finally
        {
            _isSynchronizingLibraries = false;
        }

        ApplyLibrarySelectionToProfile();
    }

    private void ApplyLibrarySelectionToProfile()
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        SelectedEnvironment.LibraryIds = LibrarySelections
            .Where(selection =>
                selection.IsSelected
                && selection.IsCompatibleWithProfile
                && LanguagesMatch(
                    SelectedEnvironment.Language,
                    selection.Library.Language))
            .Select(selection => selection.Library.Id)
            .ToList();
        NotifyLibrarySelectionSummaryChanged();
        PersistSettings();
        RefreshBuilderSurfaces();
        RefreshSearchResults();
        StatusMessage =
            $"{SelectedEnvironment.LibraryIds.Count} " +
            $"{SelectedEnvironment.Language} library " +
            $"item{(SelectedEnvironment.LibraryIds.Count == 1 ? string.Empty : "s")} active for {SelectedEnvironment.Name}.";
    }

    private bool EnsureLibraryProfiles()
    {
        var changed = false;
        if (_settings.CommandLibraryLayoutVersion < 1)
        {
            var starterLibraryIds = Libraries
                .Where(library =>
                    library.ExecutionTarget == ExecutionTarget.CSharp
                    && library.IsStarterLibrary)
                .Select(library => library.Id)
                .ToList();
            var csharpProfile = Environments.FirstOrDefault(environment =>
                string.Equals(environment.Id, "csharp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    environment.SourceLibraryId,
                    "csharp-basics",
                    StringComparison.OrdinalIgnoreCase));
            if (csharpProfile is null && starterLibraryIds.Count > 0)
            {
                csharpProfile = new EnvironmentProfile
                {
                    Id = "csharp",
                    Name = "C# starter environment",
                    Description =
                        "Bundled C# language patterns and .NET APIs that run without additional packages.",
                    Language = "C#",
                    LibraryIds = [.. starterLibraryIds],
                    SourceLibraryId = "csharp-basics"
                };
                Environments.Add(csharpProfile);
                changed = true;
            }

            if (csharpProfile is not null)
            {
                foreach (var libraryId in starterLibraryIds)
                {
                    if (!csharpProfile.LibraryIds.Contains(
                            libraryId,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        csharpProfile.LibraryIds.Add(libraryId);
                        changed = true;
                    }
                }

                if (string.Equals(
                        csharpProfile.Name,
                        "C# core library",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        csharpProfile.Name,
                        "C# Statements",
                        StringComparison.OrdinalIgnoreCase))
                {
                    csharpProfile.Name = "C# starter environment";
                    changed = true;
                }

                if (csharpProfile.Description.StartsWith(
                        "Curated, example-driven C#",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        csharpProfile.Description,
                        "C# command templates for composing code statements.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    csharpProfile.Description =
                        "Bundled C# language patterns and .NET APIs that run without additional packages.";
                    changed = true;
                }
            }

            _settings.CommandLibraryLayoutVersion = 1;
            changed = true;
        }

        if (_settings.CommandLibraryLayoutVersion < 2)
        {
            var csharpProfile = Environments.FirstOrDefault(environment =>
                string.Equals(
                    environment.Id,
                    "csharp",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    environment.SourceLibraryId,
                    "csharp-basics",
                    StringComparison.OrdinalIgnoreCase));
            if (csharpProfile is not null
                && Libraries.Any(library => string.Equals(
                    library.Id,
                    CSharpFrameworkCatalogService.DotNet8LibraryId,
                    StringComparison.OrdinalIgnoreCase))
                && !csharpProfile.LibraryIds.Contains(
                    CSharpFrameworkCatalogService.DotNet8LibraryId,
                    StringComparer.OrdinalIgnoreCase))
            {
                csharpProfile.LibraryIds.Insert(
                    0,
                    CSharpFrameworkCatalogService.DotNet8LibraryId);
                changed = true;
            }

            _settings.CommandLibraryLayoutVersion = 2;
            changed = true;
        }

        if (_settings.CommandLibraryLayoutVersion < 3)
        {
            var csharpProfile = Environments.FirstOrDefault(environment =>
                string.Equals(
                    environment.Id,
                    "csharp",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    environment.SourceLibraryId,
                    "csharp-basics",
                    StringComparison.OrdinalIgnoreCase));
            if (csharpProfile is not null
                && Libraries.Any(library => string.Equals(
                    library.Id,
                    CSharpFrameworkCatalogService.DotNet10LibraryId,
                    StringComparison.OrdinalIgnoreCase))
                && !csharpProfile.LibraryIds.Contains(
                    CSharpFrameworkCatalogService.DotNet10LibraryId,
                    StringComparer.OrdinalIgnoreCase))
            {
                csharpProfile.LibraryIds.Insert(
                    0,
                    CSharpFrameworkCatalogService.DotNet10LibraryId);
                changed = true;
            }

            _settings.CommandLibraryLayoutVersion = 3;
            changed = true;
        }

        if (_settings.CommandLibraryLayoutVersion < 4)
        {
            foreach (var profile in Environments)
            {
                var removedCount = profile.LibraryIds.RemoveAll(id =>
                    CommandCatalogService.UnpublishedCSharpCatalogIds.Contains(
                        id));
                if (removedCount > 0)
                {
                    changed = true;
                }

                if (CommandCatalogService.UnpublishedCSharpCatalogIds.Contains(
                        profile.SourceLibraryId))
                {
                    profile.SourceLibraryId = LanguagesMatch(
                        profile.Language,
                        "C#")
                        ? CSharpFrameworkCatalogService.DotNet10LibraryId
                        : string.Empty;
                    changed = true;
                }

                if (string.Equals(
                        profile.Id,
                        "csharp",
                        StringComparison.OrdinalIgnoreCase)
                    && profile.Description.StartsWith(
                        "Bundled C# language patterns",
                        StringComparison.OrdinalIgnoreCase))
                {
                    profile.Description =
                        "Installed .NET framework, SDK, and CLI surfaces available to the C# Command Builder.";
                    changed = true;
                }
            }

            _settings.CommandLibraryLayoutVersion = 4;
            changed = true;
        }

        if (_settings.CommandLibraryLayoutVersion < 5)
        {
            var csharpProfile = Environments.FirstOrDefault(environment =>
                string.Equals(
                    environment.Id,
                    "csharp",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    environment.SourceLibraryId,
                    CSharpFrameworkCatalogService.DotNet10LibraryId,
                    StringComparison.OrdinalIgnoreCase));
            if (csharpProfile is not null)
            {
                foreach (var toolchainLibraryId in new[]
                         {
                             DotNetToolchainCatalogService.DotNetSdkLibraryId,
                             DotNetToolchainCatalogService.DotNetCliLibraryId
                         })
                {
                    if (Libraries.Any(library => string.Equals(
                            library.Id,
                            toolchainLibraryId,
                            StringComparison.OrdinalIgnoreCase))
                        && !csharpProfile.LibraryIds.Contains(
                            toolchainLibraryId,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        csharpProfile.LibraryIds.Add(toolchainLibraryId);
                        changed = true;
                    }
                }
            }

            _settings.CommandLibraryLayoutVersion = 5;
            changed = true;
        }

        foreach (var library in Libraries.Where(library =>
                     library.CreateStandaloneProfile))
        {
            var profile = Environments.FirstOrDefault(environment =>
                string.Equals(environment.SourceLibraryId, library.Id, StringComparison.OrdinalIgnoreCase));
            if (profile is not null)
            {
                if (string.IsNullOrWhiteSpace(profile.Language))
                {
                    profile.Language = library.Language;
                    changed = true;
                }

                if (string.Equals(
                        library.Id,
                        "csharp-basics",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(
                            profile.Name,
                            "C# Statements",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        profile.Name = library.DisplayName;
                        changed = true;
                    }

                    if (string.Equals(
                            profile.Description,
                            "C# command templates for composing code statements.",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        profile.Description = library.Description;
                        changed = true;
                    }
                }

                continue;
            }

            profile = FindLegacyStandaloneProfile(library);
            if (profile is not null)
            {
                profile.SourceLibraryId = library.Id;
                profile.Name = GetStandaloneProfileName(library);
                profile.Description = GetStandaloneProfileDescription(library);
                profile.Language = library.Language;
                changed = true;
                continue;
            }

            Environments.Add(new EnvironmentProfile
            {
                Id = $"library-{library.Id}",
                Name = GetStandaloneProfileName(library),
                Description = GetStandaloneProfileDescription(library),
                Language = library.Language,
                LibraryIds = [library.Id],
                SourceLibraryId = library.Id
            });
            changed = true;
        }

        if (_settings.CommandLibraryLayoutVersion < 6)
        {
            var baseLibraryCandidates = new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["PowerShell"] = [CommandCatalogService.PowerShellCatalogId],
                ["C#"] =
                [
                    CSharpFrameworkCatalogService.DotNet10LibraryId,
                    CSharpFrameworkCatalogService.DotNet8LibraryId,
                    "csharp-core"
                ],
                ["C++"] = ["cpp-core"],
                ["Python"] = ["python-core"],
                ["JavaScript"] = ["javascript-core"],
                ["TypeScript"] = ["typescript-core"],
                ["Java"] = ["java-core"],
                ["HTML"] = ["html-core"],
                ["CSS"] = ["css-core"],
                ["Web APIs"] = ["dom-standard"]
            };
            foreach (var definition in CommandLanguageRegistry.Definitions)
            {
                var starterIds = Libraries
                    .Where(library =>
                        library.IsStarterLibrary
                        && !library.CreateStandaloneProfile
                        && LanguagesMatch(
                            library.Language,
                            definition.DisplayName))
                    .Select(library => library.Id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (starterIds.Count == 0
                    || !baseLibraryCandidates.TryGetValue(
                        definition.DisplayName,
                        out var candidateIds))
                {
                    continue;
                }

                var baseLibrary = candidateIds
                    .Select(id => Libraries.FirstOrDefault(library =>
                        string.Equals(
                            library.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(library => library is not null);
                if (baseLibrary is null)
                {
                    continue;
                }

                var profile = Environments.FirstOrDefault(environment =>
                                  string.Equals(
                                      environment.SourceLibraryId,
                                      baseLibrary.Id,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? Environments.FirstOrDefault(environment =>
                                  string.Equals(
                                      environment.Id,
                                      definition.Id,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? Environments.FirstOrDefault(environment =>
                                  LanguagesMatch(
                                      environment.Language,
                                      definition.DisplayName)
                                  && environment.LibraryIds.Contains(
                                      baseLibrary.Id,
                                      StringComparer.OrdinalIgnoreCase));
                if (profile is null)
                {
                    continue;
                }

                foreach (var starterId in starterIds)
                {
                    if (profile.LibraryIds.Contains(
                            starterId,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    profile.LibraryIds.Add(starterId);
                    changed = true;
                }

                var hasDefaultName = string.Equals(
                                         profile.Name,
                                         GetStandaloneProfileName(baseLibrary),
                                         StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(
                                         profile.Name,
                                         $"{definition.DisplayName} Core",
                                         StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(
                                         profile.Name,
                                         $"{definition.DisplayName} starter environment",
                                         StringComparison.OrdinalIgnoreCase)
                                     || definition.Id == "powershell"
                                     && string.Equals(
                                         profile.Name,
                                         "PowerShell Default",
                                         StringComparison.OrdinalIgnoreCase);
                if (hasDefaultName)
                {
                    profile.Name =
                        $"{definition.DisplayName} + popular libraries";
                    profile.Description =
                        $"Built-in {definition.DisplayName} foundations plus curated recipes for popular open-source libraries. External dependencies are never installed automatically.";
                    changed = true;
                }
            }

            _settings.CommandLibraryLayoutVersion = 6;
            changed = true;
        }

        changed |= EnsureProfileLanguageInvariant();
        return changed;
    }

    private EnvironmentProfile? FindLegacyStandaloneProfile(CommandLibrary library)
    {
        var legacyId = library.Id switch
        {
            CommandCatalogService.PowerShellCatalogId => "powershell",
            "csharp-basics" => "csharp",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(legacyId)
            ? null
            : Environments.FirstOrDefault(environment => string.Equals(environment.Id, legacyId, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStandaloneProfileName(CommandLibrary library) =>
        string.Equals(library.Id, CommandCatalogService.PowerShellCatalogId, StringComparison.OrdinalIgnoreCase)
            ? "PowerShell Default"
            : library.DisplayName;

    private static string GetStandaloneProfileDescription(CommandLibrary library) =>
        string.Equals(library.Id, CommandCatalogService.PowerShellCatalogId, StringComparison.OrdinalIgnoreCase)
            ? "Built-in PowerShell commands discovered from the embedded engine."
            : library.Description;

    private void ToggleProfileCreator()
    {
        IsProfileCreatorOpen = !IsProfileCreatorOpen;
        if (IsProfileCreatorOpen)
        {
            NewProfileLanguage = SelectedEnvironmentLanguage;
        }
        else
        {
            NewProfileName = string.Empty;
        }
    }

    private string? GetNewProfileValidationError()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a profile name to continue.";
        }

        if (!ProfileLanguageOptions.Contains(
                NewProfileLanguage,
                StringComparer.OrdinalIgnoreCase))
        {
            return "Choose a supported profile language.";
        }

        return GetProfileNameValidationError(name, null);
    }

    private void CreateProfile()
    {
        if (GetNewProfileValidationError() is { } error)
        {
            StatusMessage = error;
            return;
        }

        var language = NewProfileLanguage;
        var profile = new EnvironmentProfile
        {
            Id = $"custom-{Guid.NewGuid():N}",
            Name = NewProfileName.Trim(),
            Description = $"Custom {language} command profile.",
            Language = language,
            LibraryIds = []
        };

        Environments.Add(profile);
        NewProfileName = string.Empty;
        IsProfileCreatorOpen = false;
        SelectedEnvironment = profile;
        StatusMessage =
            $"Created {profile.Name}. Choose its {language} libraries, then press Save.";
    }

    private void DuplicateProfile()
    {
        if (SelectedEnvironment is null)
        {
            StatusMessage = "Choose a command profile before copying it.";
            return;
        }

        var duplicate = SelectedEnvironment.Clone();
        duplicate.Id = $"custom-{Guid.NewGuid():N}";
        duplicate.Name = CreateUniqueProfileName(
            $"{duplicate.Name} copy");
        duplicate.SourceLibraryId = string.Empty;
        Environments.Add(duplicate);
        SelectedEnvironment = duplicate;
        StatusMessage = $"Created {duplicate.Name}.";
    }

    private void SaveSelectedProfile()
    {
        if (SelectedEnvironment is null)
        {
            StatusMessage = "Choose a command profile before saving.";
            return;
        }

        if (GetProfileNameValidationError(
                SelectedEnvironmentName,
                SelectedEnvironment) is { } error)
        {
            StatusMessage = error;
            return;
        }

        _selectedEnvironmentNameDraft = SelectedEnvironmentName.Trim();
        _selectedEnvironmentDescriptionDraft =
            SelectedEnvironmentDescription.Trim();
        SelectedEnvironment.Name = _selectedEnvironmentNameDraft;
        SelectedEnvironment.Description =
            _selectedEnvironmentDescriptionDraft;
        CollectionViewSource.GetDefaultView(Environments).Refresh();
        OnPropertyChanged(nameof(SelectedEnvironmentName));
        OnPropertyChanged(nameof(SelectedEnvironmentDescription));
        OnPropertyChanged(nameof(ProfileSaveHint));
        OnPropertyChanged(nameof(ActiveBuilderProfileSummary));
        PersistSettingsImmediately();
        StatusMessage =
            $"Saved {SelectedEnvironment.Name} with " +
            $"{SelectedEnvironment.LibraryIds.Count} " +
            $"{SelectedEnvironment.Language} library " +
            $"item{(SelectedEnvironment.LibraryIds.Count == 1 ? string.Empty : "s")}.";
    }

    private void DeleteProfile()
    {
        if (SelectedEnvironment is null)
        {
            StatusMessage = "Choose a command profile before deleting.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                SelectedEnvironment.SourceLibraryId))
        {
            StatusMessage =
                $"{SelectedEnvironment.Name} is the built-in " +
                $"{SelectedEnvironment.Language} profile and cannot be deleted. " +
                "Use Copy to create a removable profile.";
            return;
        }

        if (Environments.Count <= 1)
        {
            StatusMessage =
                "At least one command profile must remain.";
            return;
        }

        var deletedName = SelectedEnvironment.Name;
        var index = Environments.IndexOf(SelectedEnvironment);
        Environments.Remove(SelectedEnvironment);
        SelectedEnvironment = Environments[Math.Clamp(index, 0, Environments.Count - 1)];
        PersistSettingsImmediately();
        StatusMessage = $"Deleted {deletedName}.";
    }

    private void LoadSelectedProfileDraft(EnvironmentProfile profile)
    {
        _selectedEnvironmentNameDraft = profile.Name;
        _selectedEnvironmentDescriptionDraft = profile.Description;
        OnPropertyChanged(nameof(SelectedEnvironmentName));
        OnPropertyChanged(nameof(SelectedEnvironmentDescription));
        OnPropertyChanged(nameof(ProfileSaveHint));
    }

    private string? GetProfileNameValidationError(
        string value,
        EnvironmentProfile? currentProfile)
    {
        var name = value.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Profile names cannot be empty.";
        }

        return Environments.Any(profile =>
            !ReferenceEquals(profile, currentProfile)
            && string.Equals(
                profile.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            ? $"A profile named \"{name}\" already exists. Choose a unique name."
            : null;
    }

    private string CreateUniqueProfileName(string baseName)
    {
        var candidate = baseName;
        var suffix = 2;
        while (Environments.Any(profile => string.Equals(
                   profile.Name,
                   candidate,
                   StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private void NotifyLibrarySelectionSummaryChanged()
    {
        OnPropertyChanged(nameof(ActiveLibraryCountLabel));
        OnPropertyChanged(nameof(ActiveLibrarySummary));
    }

    private void RefreshSearchResults()
    {
        SearchResults.Clear();
        SelectedSearchResult = null;
        var query = SearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            NotifySearchResultsChanged();
            return;
        }

        var terms = query.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selectedCatalogIds = SelectedSurfaceLibrary is { } surfaceLibrary
            ? new HashSet<string>(
                [surfaceLibrary.Id],
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libraryById = Libraries.ToDictionary(
            library => library.Id,
            StringComparer.OrdinalIgnoreCase);
        var candidates = Commands
            .Where(command => selectedCatalogIds.Contains(command.CatalogId))
            .Select(command =>
            {
                libraryById.TryGetValue(command.CatalogId, out var library);
                var librarySearchText = library is null
                    ? string.Empty
                    : $"{library.DisplayName} {library.Description} " +
                      $"{library.Language} {library.Kind} " +
                      $"{library.FrameworkName} {library.FrameworkVersion} " +
                      $"{library.TargetFramework} " +
                      $"{string.Join(' ', library.Keywords)}";
                var searchText = $"{command.SearchText} {librarySearchText}";
                return (
                    Result: CommandSearchResultViewModel.FromCommand(
                        command,
                        ScoreSearchResult(command, query, searchText)),
                    SearchText: searchText);
            })
            .Where(candidate => terms.All(term =>
                candidate.SearchText.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            .Select(candidate => candidate.Result)
            .ToList();

        foreach (var index in GetSelectedDotNetApiIndexes())
        {
            candidates.AddRange(index
                .Search(query, 100)
                .Select(entry => CommandSearchResultViewModel.FromApi(
                    entry,
                    ScoreApiSearchResult(entry, query))));
        }

        foreach (var result in candidates
                     .DistinctBy(
                         candidate => candidate.Key,
                         StringComparer.OrdinalIgnoreCase)
                     .OrderBy(candidate => candidate.Score)
                     .ThenBy(
                         candidate => candidate.DisplayName,
                         StringComparer.OrdinalIgnoreCase)
                     .Take(50))
        {
            SearchResults.Add(result);
        }

        NotifySearchResultsChanged();
    }

    private static int ScoreSearchResult(
        CommandDefinition command,
        string query,
        string searchText)
    {
        if (string.Equals(
                command.Name,
                query,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                command.DisplayName,
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
            || command.DisplayName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (command.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || command.DisplayName.Contains(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (command.ObjectName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || command.Action.Contains(query, StringComparison.OrdinalIgnoreCase)
            || command.ModuleName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return searchText.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? 4
            : 5;
    }

    private static int ScoreApiSearchResult(
        DotNetApiSearchEntry entry,
        string query)
    {
        if (string.Equals(
                entry.QualifiedMemberName,
                query,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                entry.DisplayName,
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (entry.QualifiedMemberName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase)
            || entry.DisplayName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (entry.MemberName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private void NotifySearchResultsChanged()
    {
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasNoSearchResults));
        OnPropertyChanged(nameof(SearchResultSummary));
    }

    private void OpenSearchResult(CommandSearchResultViewModel result)
    {
        if (result.ApiEntry is not null)
        {
            if (IsCSharpComposerWorkspace)
            {
                OpenCSharpComposerApiEntry(result.ApiEntry);
                return;
            }

            OpenDotNetSearchResult(result.ApiEntry);
            return;
        }

        var command = result.Command;
        if (command is null)
        {
            return;
        }

        if (IsCSharpComposerWorkspace)
        {
            OpenCSharpComposerCommand(command);
            return;
        }

        if (command.ExecutionTarget == ExecutionTarget.CSharp)
        {
            OpenCSharpSearchResult(command);
            return;
        }

        var commandLibrary = Libraries.FirstOrDefault(library => string.Equals(
            library.Id,
            command.CatalogId,
            StringComparison.OrdinalIgnoreCase));
        if (commandLibrary?.ExecutionTarget == ExecutionTarget.External)
        {
            OpenHierarchyCommand(command);
            return;
        }

        SelectBuilderSurface(command.CatalogId);
        if (!TryGetCommandParts(command, out var action, out var @object))
        {
            return;
        }

        SelectedAction = Actions.FirstOrDefault(candidate => string.Equals(
            candidate,
            action,
            StringComparison.OrdinalIgnoreCase));
        if (SelectedAction is null)
        {
            return;
        }

        SelectedObject = Objects
            .Select(option => option.DisplayName)
            .FirstOrDefault(candidate => string.Equals(
                candidate,
                @object,
                StringComparison.OrdinalIgnoreCase));
        if (SelectedObject is null)
        {
            return;
        }

        SelectedCommand = MatchingCommands.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, command.Id, StringComparison.OrdinalIgnoreCase))
            ?? command;
        StatusMessage =
            $"Opened {command.DisplayName} from search.";
    }

    private void RefreshCommandFacets()
    {
        var facets = GetActiveCommandFacets().ToList();
        ReplaceItems(Actions, facets
            .Select(facet => facet.Action)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(action => action, StringComparer.OrdinalIgnoreCase));

        if (SelectedAction is not null && !Actions.Any(action => string.Equals(action, SelectedAction, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedAction = null;
        }
        else
        {
            RefreshObjects(facets);
        }

        OnPropertyChanged(nameof(ActionHint));
        OnPropertyChanged(nameof(ObjectHint));
        OnPropertyChanged(nameof(SelectedActionDescription));
        OnPropertyChanged(nameof(HasSelectedActionDescription));
        OnPropertyChanged(nameof(SelectedObjectDescription));
        OnPropertyChanged(nameof(HasSelectedObjectDescription));
        OnPropertyChanged(nameof(FlagsSelectorSummary));
    }

    private string BuildActionHint()
    {
        if (IsDotNetToolchainSurface)
        {
            return "Choose a command group exposed by the installed .NET toolchain.";
        }

        return SelectedBuilderSurface?.ExecutionTarget switch
        {
            ExecutionTarget.PowerShell =>
                "Choose a PowerShell verb. Commands keep their native Verb-Noun structure.",
            ExecutionTarget.External =>
                "Choose the action facet supplied by the external library.",
            _ => "Choose a command action."
        };
    }

    private string ResolveSelectedActionDescription()
    {
        if (!HasSelectedAction)
        {
            return string.Empty;
        }

        if (SelectedCommandGuide is not null
            && string.Equals(
                SelectedCommandGuide.ActionName,
                SelectedAction,
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(SelectedCommandGuide.ActionDescription))
        {
            return SelectedCommandGuide.ActionDescription.Trim();
        }

        var command = GetActiveCommandFacets()
            .Where(facet => string.Equals(
                facet.Action,
                SelectedAction,
                StringComparison.OrdinalIgnoreCase))
            .Select(facet => facet.Command)
            .FirstOrDefault();
        if (command is null)
        {
            return string.Empty;
        }

        if (_catalogService is not null)
        {
            return _catalogService.GetActionDescription(command);
        }

        return command.ActionDescription?.Trim() ?? string.Empty;
    }

    private string ResolveSelectedObjectDescription()
    {
        if (!HasSelectedObject)
        {
            return string.Empty;
        }

        if (SelectedCommandGuide is not null
            && string.Equals(
                SelectedCommandGuide.ObjectName,
                SelectedObject,
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(SelectedCommandGuide.ObjectDescription))
        {
            return SelectedCommandGuide.ObjectDescription.Trim();
        }

        var command = SelectedCommand
            ?? GetActiveCommandFacets()
                .Where(facet => string.Equals(
                                    facet.Action,
                                    SelectedAction,
                                    StringComparison.OrdinalIgnoreCase)
                                && string.Equals(
                                    facet.Object,
                                    SelectedObject,
                                    StringComparison.OrdinalIgnoreCase))
                .Select(facet => facet.Command)
                .FirstOrDefault();
        if (command is null)
        {
            return string.Empty;
        }

        if (_catalogService is not null)
        {
            return _catalogService.GetObjectDescription(command);
        }

        return command.ObjectDescription?.Trim() ?? string.Empty;
    }

    private void RefreshObjects(IReadOnlyList<CommandFacet>? facets = null)
    {
        if (!HasSelectedAction)
        {
            ReplaceItems(Objects, []);
            ClearMatchingCommands();
            OnPropertyChanged(nameof(ObjectHint));
            return;
        }

        var source = facets ?? GetActiveCommandFacets().ToList();
        ReplaceItems(Objects, source
            .Where(facet => string.Equals(facet.Action, SelectedAction, StringComparison.OrdinalIgnoreCase))
            .Select(facet => new CommandObjectOption(
                facet.Object,
                ResolveObjectModuleName(facet.Command)))
            .DistinctBy(
                option => $"{option.ModuleName}\u001F{option.DisplayName}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase));

        if (SelectedObject is null || !Objects.Any(option =>
                string.Equals(option.DisplayName, SelectedObject, StringComparison.OrdinalIgnoreCase)))
        {
            if (SelectedObject is null)
            {
                ClearMatchingCommands();
            }
            else
            {
                SelectedObject = null;
            }
        }
        else
        {
            RefreshMatchingCommands(source);
        }

        OnPropertyChanged(nameof(ObjectHint));
    }

    private string ResolveObjectModuleName(CommandDefinition command)
    {
        if (!string.IsNullOrWhiteSpace(command.ModuleName))
        {
            return command.ModuleName.Trim();
        }

        return Libraries.FirstOrDefault(library =>
                   string.Equals(library.Id, command.CatalogId, StringComparison.OrdinalIgnoreCase))
               ?.DisplayName.Trim()
               ?? "Other";
    }

    private void RefreshMatchingCommands(IReadOnlyList<CommandFacet>? facets = null)
    {
        if (!HasSelectedAction || string.IsNullOrWhiteSpace(SelectedObject))
        {
            ClearMatchingCommands();
            return;
        }

        var source = facets ?? GetActiveCommandFacets().ToList();
        var matches = source
            .Where(facet => string.Equals(facet.Action, SelectedAction, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(facet.Object, SelectedObject, StringComparison.OrdinalIgnoreCase))
            .Select(facet => facet.Command)
            .OrderBy(command => command.ExecutionTarget == ExecutionTarget.PowerShell ? 0 : 1)
            .ThenBy(command => command.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceItems(MatchingCommands, matches);
        SelectedCommand = MatchingCommands.FirstOrDefault();
        OnPropertyChanged(nameof(HasCommandVariants));
    }

    private void ClearMatchingCommands()
    {
        MatchingCommands.Clear();
        SelectedCommand = null;
        OnPropertyChanged(nameof(HasCommandVariants));
    }

    private IEnumerable<CommandFacet> GetActiveCommandFacets()
    {
        if (IsCSharpSurface)
        {
            yield break;
        }

        var selectedCatalogIds = LibrarySelections
            .Where(selection => selection.IsSelected)
            .Select(selection => selection.Library.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(
                SelectedBuilderSurface?.LibraryId))
        {
            selectedCatalogIds.IntersectWith(
                [SelectedBuilderSurface.LibraryId]);
        }
        var executionTarget = SelectedBuilderSurface?.ExecutionTarget;
        foreach (var command in Commands)
        {
            if (!selectedCatalogIds.Contains(command.CatalogId)
                || (executionTarget is not null
                    && command.ExecutionTarget != executionTarget)
                || !TryGetCommandParts(command, out var action, out var @object))
            {
                continue;
            }

            yield return new CommandFacet(command, action, @object);
        }
    }

    private static bool TryGetCommandParts(CommandDefinition command, out string action, out string @object)
    {
        action = (command.Action ?? string.Empty).Trim();
        @object = (command.ObjectName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(action) && !string.IsNullOrWhiteSpace(@object))
        {
            return true;
        }

        if (string.Equals(command.CommandType, "Alias", StringComparison.OrdinalIgnoreCase)
            || !TryInferCommandParts(command.Name, out var inferredAction, out var inferredObject))
        {
            return false;
        }

        action = string.IsNullOrWhiteSpace(action) ? inferredAction : action;
        @object = string.IsNullOrWhiteSpace(@object) ? inferredObject : @object;
        return !string.IsNullOrWhiteSpace(action) && !string.IsNullOrWhiteSpace(@object);
    }

    private static bool TryInferCommandParts(string name, out string action, out string @object)
    {
        action = string.Empty;
        @object = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var separator = name.IndexOf('-');
        if (separator > 0 && separator < name.Length - 1)
        {
            action = name[..separator].Trim();
            @object = name[(separator + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(action) && !string.IsNullOrWhiteSpace(@object);
        }

        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        @object = parts[^2];
        action = ExtractActionFromMemberName(parts[^1]);
        return !string.IsNullOrWhiteSpace(action) && !string.IsNullOrWhiteSpace(@object);
    }

    private static string ExtractActionFromMemberName(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return string.Empty;
        }

        var knownActions = new[]
        {
            "Unregister", "Disconnect", "Uninstall", "Register", "Restart", "Disable", "Enable", "Export", "Import",
            "Install", "Convert", "Connect", "Receive", "Remove", "Invoke", "Update", "Create", "Delete", "Format",
            "Measure", "Select", "Start", "Clear", "Copy", "Move", "Open", "Close", "Read", "Write", "Find", "Send",
            "Stop", "Test", "Wait", "Join", "Split", "New", "Set", "Get", "Add"
        };

        foreach (var knownAction in knownActions)
        {
            if (memberName.StartsWith(knownAction, StringComparison.OrdinalIgnoreCase)
                && (memberName.Length == knownAction.Length || char.IsUpper(memberName[knownAction.Length])))
            {
                return knownAction;
            }
        }

        var boundary = 1;
        while (boundary < memberName.Length && !char.IsUpper(memberName[boundary]))
        {
            boundary++;
        }

        return boundary == 0 ? string.Empty : memberName[..boundary];
    }

    private static void ReplaceItems<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private sealed record CommandFacet(CommandDefinition Command, string Action, string Object);

    private void AddSelectedCommand()
    {
        if (IsCSharpComposerWorkspace)
        {
            AddCurrentCSharpComposerStatement();
            return;
        }

        if (SelectedCommand is null)
        {
            return;
        }

        var statement = BuildStatement(SelectedCommand);
        Pipeline.Add(new CommandSegmentViewModel(
            statement,
            SelectedCommand,
            SelectedCommandGuide,
            SelectedEnvironmentLanguage));
        RebuildCommandText();
        StatusMessage =
            $"Added {SelectedCommand.DisplayName} as {CompositionUnitSingular} {Pipeline.Count}.";
    }

    private async void LoadSelectedCommandGuideAsync(
        CommandDefinition command,
        int loadVersion)
    {
        if (_catalogService is null)
        {
            IsCommandGuideLoading = false;
            return;
        }

        try
        {
            var guide = await _catalogService.LoadCommandGuideAsync(command);
            if (loadVersion != _commandGuideLoadVersion
                || !ReferenceEquals(command, SelectedCommand))
            {
                return;
            }

            SelectedCommandGuide = guide;
            if (command.IsGuideLoaded
                && command.ExecutionTarget != ExecutionTarget.PowerShell)
            {
                PopulateParameterEditors(command);
                IsSelectedCommandMetadataLoading = false;
                OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
            }
            foreach (var segment in Pipeline.Where(segment =>
                         segment.MatchesCommand(command)))
            {
                segment.ApplyGuide(guide);
            }
        }
        catch (Exception exception)
        {
            if (loadVersion == _commandGuideLoadVersion
                && ReferenceEquals(command, SelectedCommand))
            {
                StatusMessage =
                    $"Unable to load the command guide for {command.DisplayName}: {exception.Message}";
            }
        }
        finally
        {
            if (loadVersion == _commandGuideLoadVersion
                && ReferenceEquals(command, SelectedCommand))
            {
                IsCommandGuideLoading = false;
                if (command.HasDeferredGuide)
                {
                    IsSelectedCommandMetadataLoading = false;
                    OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
                }
            }
        }
    }

    private async void LoadSelectedCommandMetadataAsync(CommandDefinition command, int loadVersion)
    {
        if (_catalogService is null)
        {
            IsSelectedCommandMetadataLoading = false;
            return;
        }

        try
        {
            var parameters = await _catalogService.LoadParametersAsync(command);
            if (loadVersion != _parameterLoadVersion || !ReferenceEquals(command, SelectedCommand))
            {
                return;
            }

            command.Parameters = parameters;
            PopulateParameterEditors(command);
            StatusMessage = parameters.Count == 0
                ? $"{command.DisplayName} has no discoverable typed parameters."
                : $"Loaded {parameters.Count} parameters for {command.DisplayName}.";
        }
        catch (Exception exception)
        {
            if (loadVersion == _parameterLoadVersion && ReferenceEquals(command, SelectedCommand))
            {
                StatusMessage = $"Unable to load parameters for {command.DisplayName}: {exception.Message}";
            }
        }
        finally
        {
            if (loadVersion == _parameterLoadVersion && ReferenceEquals(command, SelectedCommand))
            {
                IsSelectedCommandMetadataLoading = false;
                OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
            }
        }
    }

    private void PopulateParameterEditors(CommandDefinition command)
    {
        ClearParameterEditors();
        foreach (var parameter in command.Parameters)
        {
            var editor = new ParameterEditorViewModel(
                parameter,
                command.ExecutionTarget,
                IsDotNetRuntimeCommand(command));
            editor.PropertyChanged += OnParameterEditorPropertyChanged;
            ParameterEditors.Add(editor);
        }

        OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
        OnPropertyChanged(nameof(HasParameterEditors));
        OnPropertyChanged(nameof(FlagsSelectorSummary));
        if (IsCSharpComposerWorkspace)
        {
            NotifyCSharpComposerChanged();
        }
        else
        {
            RefreshStagedStatement();
        }
    }

    private string BuildStatement(CommandDefinition command)
    {
        var statement = string.IsNullOrWhiteSpace(command.Template) ? command.Name : command.Template;
        var usesTypedArguments = command.ExecutionTarget
                                == ExecutionTarget.CSharp
                                || IsDotNetRuntimeCommand(command);
        var usesCommandLineArguments =
            IsDotNetToolchainCommand(command);
        foreach (var editor in ParameterEditors)
        {
            var parameter = editor.Definition;
            var marker = string.IsNullOrWhiteSpace(parameter.Placeholder) ? parameter.Name : parameter.Placeholder;
            var placeholder = $"{{{marker}}}";
            if ((usesTypedArguments
                 || usesCommandLineArguments
                 || command.ExecutionTarget == ExecutionTarget.External)
                && !editor.IsIncluded)
            {
                statement = RemoveArgumentPlaceholder(statement, placeholder);
                continue;
            }

            var isGenericTypePlaceholder = statement.Contains(
                $"<{placeholder}>",
                StringComparison.Ordinal);
            var value = editor.IsSwitch
                ? (editor.IsIncluded ? "true" : "false")
                : string.IsNullOrWhiteSpace(editor.Value)
                    ? isGenericTypePlaceholder
                        ? parameter.Name
                        : $"<{parameter.Name}>"
                    : command.ExecutionTarget == ExecutionTarget.CSharp
                        ? FormatCSharpArgument(parameter, editor.Value)
                        : IsDotNetRuntimeCommand(command)
                            ? FormatPowerShellArgument(
                                parameter,
                                editor.Value)
                        : editor.Value;

            if (statement.Contains(placeholder, StringComparison.Ordinal))
            {
                statement = statement.Replace(placeholder, value, StringComparison.Ordinal);
                continue;
            }

            if (command.ExecutionTarget == ExecutionTarget.PowerShell && editor.IsIncluded)
            {
                if (editor.IsSwitch)
                {
                    statement += $" {parameter.DisplayName}";
                }
                else if (!string.IsNullOrWhiteSpace(editor.Value))
                {
                    statement += $" {parameter.DisplayName} {editor.Value}";
                }
            }
        }

        return usesCommandLineArguments
            ? statement.Trim()
            : statement;
    }

    private static bool IsDotNetRuntimeCommand(
        CommandDefinition command) =>
        command.CatalogId.StartsWith(
            "csharp-dotnet-",
            StringComparison.OrdinalIgnoreCase)
        && command.CatalogId.EndsWith(
            "-runtime-api",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDotNetToolchainCommand(
        CommandDefinition command) =>
        DotNetToolchainCatalogService.IsToolchainLibraryId(
            command.CatalogId);

    private static string RemoveArgumentPlaceholder(
        string statement,
        string placeholder) =>
        statement
            .Replace($", {placeholder}", string.Empty, StringComparison.Ordinal)
            .Replace($"{placeholder}, ", string.Empty, StringComparison.Ordinal)
            .Replace(placeholder, string.Empty, StringComparison.Ordinal);

    private static string FormatCSharpArgument(
        CommandParameterDefinition parameter,
        string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('=') && trimmed.Length > 1)
        {
            return trimmed[1..].TrimStart();
        }

        if (!IsCSharpStringType(parameter.TypeName)
            || LooksLikeCSharpStringLiteral(trimmed))
        {
            return trimmed;
        }

        return ToCSharpStringLiteral(value);
    }

    private static string FormatPowerShellArgument(
        CommandParameterDefinition parameter,
        string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('=') && trimmed.Length > 1)
        {
            return trimmed[1..].TrimStart();
        }

        if (!IsCSharpStringType(parameter.TypeName)
            || LooksLikePowerShellStringLiteral(trimmed))
        {
            return trimmed;
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static bool IsCSharpStringType(string typeName)
    {
        var normalized = typeName.Trim();
        foreach (var modifier in new[] { "ref ", "out ", "in " })
        {
            if (normalized.StartsWith(
                    modifier,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[modifier.Length..].TrimStart();
                break;
            }
        }

        return string.Equals(
                   normalized,
                   "string",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   normalized,
                   "string?",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   normalized,
                   "System.String",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCSharpStringLiteral(string value) =>
        value.Length >= 2
        && ((value.StartsWith('"') && value.EndsWith('"'))
            || (value.StartsWith("@\"", StringComparison.Ordinal)
                && value.EndsWith('"'))
            || (value.StartsWith("$\"", StringComparison.Ordinal)
                && value.EndsWith('"'))
            || (value.StartsWith("$@\"", StringComparison.Ordinal)
                && value.EndsWith('"'))
            || (value.StartsWith("@$\"", StringComparison.Ordinal)
                && value.EndsWith('"')));

    private static bool LooksLikePowerShellStringLiteral(string value) =>
        value.Length >= 2
        && ((value.StartsWith('\'') && value.EndsWith('\''))
            || (value.StartsWith('"') && value.EndsWith('"')));

    private static string ToCSharpStringLiteral(string value) =>
        JsonSerializer.Serialize(value);

    private void RemoveStatement(CommandSegmentViewModel? segment)
    {
        if (segment is null)
        {
            return;
        }

        var removesLiveCSharpComposerStatement =
            IsCSharpComposerWorkspace
            && string.Equals(
                segment.CommandId,
                LiveCSharpComposerCommandId,
                StringComparison.Ordinal);
        if (!Pipeline.Remove(segment))
        {
            return;
        }

        RebuildCommandText();
        if (removesLiveCSharpComposerStatement)
        {
            // The Pipeline row is the visible projection of the active token
            // graph. Deleting that row must clear the graph as well, otherwise
            // the cached type/member can silently recreate the statement.
            ResetCSharpComposerState(clearStatus: true);
        }
    }

    private void ClearBuilder()
    {
        Pipeline.Clear();
        CommandText = string.Empty;
        if (IsCSharpComposerWorkspace)
        {
            ResetCSharpComposerState(clearStatus: true);
        }

        NotifyPipelineChanged();
        StatusMessage = IsCSharpComposerWorkspace
            ? string.Empty
            : "Command builder cleared.";
    }

    private void RebuildCommandText()
    {
        var targets = Pipeline
            .Select(segment => segment.ExecutionTarget)
            .Distinct()
            .ToList();
        var separator = targets.Count == 1
                        && targets[0] == ExecutionTarget.PowerShell
                        && Pipeline.All(segment =>
                            segment.CompositionMode
                            != CommandCompositionMode.Statement)
            ? " | "
            : Environment.NewLine;
        CommandText = string.Join(separator, Pipeline.Select(segment => segment.Text));
        NotifyPipelineChanged();
    }

    private void InsertIntoEditor()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            StatusMessage =
                $"Add a {CompositionUnitSingular} before inserting it into the editor.";
            return;
        }

        var request = new EditorInsertionRequest(
            CommandText,
            SelectedEnvironmentLanguage);
        EditorInsertionRequested?.Invoke(this, request);
        StatusMessage = string.IsNullOrWhiteSpace(request.Message)
            ? request.Succeeded
                ? "Inserted the composed source into the active document."
                : "Open a compatible document before inserting this source."
            : request.Message;
    }

    private void CopyComposedText()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            StatusMessage = $"Add a {CompositionUnitSingular} before copying it.";
            return;
        }

        try
        {
            Clipboard.SetDataObject(CommandText, copy: true);
            StatusMessage = "Copied the composed source to the clipboard.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Unable to copy the composed source: {exception.Message}";
        }
    }

    private void ExecutePrimaryAction()
    {
        if (IsEditorPrimaryAction)
        {
            InsertIntoEditor();
            return;
        }

        RunCommand();
    }

    private void SendToTerminal()
    {
        DispatchCommand(executeImmediately: false);
    }

    private void RunCommand()
    {
        DispatchCommand(executeImmediately: true);
    }

    private void DispatchCommand(bool executeImmediately)
    {
        if (string.IsNullOrWhiteSpace(CommandText)
            && IsCSharpComposerWorkspace
            && HasCSharpComposerStatement)
        {
            AddCurrentCSharpComposerStatement();
        }

        if (string.IsNullOrWhiteSpace(CommandText))
        {
            var action = executeImmediately ? "running it" : "staging it";
            StatusMessage = IsCSharpComposerWorkspace
                ? $"Build a C# statement before {action}."
                : $"Add a {CompositionUnitSingular} to the {PipelineHeading.ToLowerInvariant()} " +
                  $"before {action}.";
            return;
        }

        if (!HasTerminalActions)
        {
            StatusMessage =
                $"{SelectedInputSyntax} source is editor-only; use {EditorActionLabel.ToLowerInvariant()}.";
            return;
        }

        var unsupportedSegment = Pipeline.FirstOrDefault(segment =>
            !segment.IsExecutionSupported);
        if (unsupportedSegment is not null)
        {
            StatusMessage = string.IsNullOrWhiteSpace(
                unsupportedSegment.ExecutionSupportMessage)
                ? $"{unsupportedSegment.CommandName} is editor-only in this build."
                : unsupportedSegment.ExecutionSupportMessage;
            return;
        }

        var targets = Pipeline
            .Select(segment => segment.ExecutionTarget)
            .Distinct()
            .ToList();
        if (targets.Count > 1)
        {
            StatusMessage =
                "The composition mixes execution targets. Keep PowerShell, C#, and external-runtime source in separate compositions.";
            return;
        }

        var executionTarget = targets.FirstOrDefault(
            SelectedCommand?.ExecutionTarget ?? ExecutionTarget.PowerShell);
        var preferredShellKinds = Pipeline
            .Select(segment => segment.PreferredShellKind)
            .Distinct()
            .ToList();
        var preferredShellKind = executionTarget switch
        {
            ExecutionTarget.CSharp => TerminalShellKind.CSharp,
            ExecutionTarget.External when preferredShellKinds.Count == 1 =>
                preferredShellKinds[0],
            ExecutionTarget.External =>
                SelectedCommand?.PreferredShellKind ?? TerminalShellKind.External,
            _ when preferredShellKinds.Contains(
                TerminalShellKind.WindowsPowerShell) =>
                TerminalShellKind.WindowsPowerShell,
            _ => TerminalShellKind.PowerShell
        };
        if (executionTarget == ExecutionTarget.External
            && (preferredShellKind == TerminalShellKind.External
                || preferredShellKinds.Count > 1))
        {
            StatusMessage =
                "The composition mixes incompatible external runtimes. Use one language runtime at a time.";
            return;
        }

        if (executionTarget == ExecutionTarget.External
            && !CommandLanguageRegistry.CanExecute(
                SelectedEnvironmentLanguage,
                executionTarget,
                preferredShellKind))
        {
            StatusMessage =
                $"{SelectedEnvironmentLanguage} does not have an approved terminal adapter in this build.";
            return;
        }
        CommandDispatchRequested?.Invoke(
            this,
            new CommandExecutionRequest(
                CommandText,
                executionTarget,
                preferredShellKind,
                executeImmediately));
    }

    private void ClearParameterEditors()
    {
        foreach (var editor in ParameterEditors)
        {
            editor.PropertyChanged -= OnParameterEditorPropertyChanged;
        }

        ParameterEditors.Clear();
        OnPropertyChanged(nameof(HasParameterEditors));
        OnPropertyChanged(nameof(FlagsSelectorSummary));
    }

    private void OnParameterEditorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ParameterEditorViewModel.IsIncluded)
            or nameof(ParameterEditorViewModel.Value))
        {
            OnPropertyChanged(nameof(FlagsSelectorSummary));
            if (IsCSharpComposerWorkspace)
            {
                NotifyCSharpComposerChanged();
            }
            else
            {
                RefreshStagedStatement();
            }
        }
    }

    private void RefreshStagedStatement()
    {
        StagedStatementText = IsCSharpComposerWorkspace
            ? HasCSharpComposerStatement
                ? CSharpComposerStatementText
                : string.Empty
            : SelectedCommand is null
            ? IsCSharpSurface
                ? IsCSharpLanguageSurface
                    ? "Choose a C# language pattern to preview the statement."
                    : "Choose a namespace, type, member, and overload to " +
                      $"preview the {SelectedDotNetInvocationSyntax.DisplayName} statement."
                : IsHierarchySurface
                    ? "Choose a library path and entry to preview the current statement."
                    : "Choose an Action and Object to preview the current statement."
            : IsSelectedCommandMetadataLoading
                ? $"Loading parameters for {SelectedCommand.DisplayName}…"
                : BuildStatement(SelectedCommand);
        OnPropertyChanged(nameof(HasStatementPreview));
        OnPropertyChanged(nameof(HasCatalogStatementPreview));
        OnPropertyChanged(nameof(HasProfileDrivenStatementPreview));
        OnPropertyChanged(nameof(CanAddCurrentStatement));
        AddStatementCommand.RaiseCanExecuteChanged();
    }

    private void NotifyPipelineChanged()
    {
        OnPropertyChanged(nameof(HasPipelineStatements));
        OnPropertyChanged(nameof(IsPipelineEmpty));
        OnPropertyChanged(nameof(PipelineSummaryLabel));
        OnPropertyChanged(nameof(PipelineEmptyHint));
    }

    private void NotifyLanguagePresentationChanged()
    {
        OnPropertyChanged(nameof(CompositionUnitSingular));
        OnPropertyChanged(nameof(CompositionUnitPlural));
        OnPropertyChanged(nameof(PipelineHeading));
        OnPropertyChanged(nameof(PipelineSummaryLabel));
        OnPropertyChanged(nameof(PipelineEmptyHint));
        OnPropertyChanged(nameof(ComposedTextHeading));
        OnPropertyChanged(nameof(HasTerminalActions));
        OnPropertyChanged(nameof(HasEditorOnlyActions));
        OnPropertyChanged(nameof(IsEditorPrimaryAction));
        OnPropertyChanged(nameof(IsTerminalPrimaryAction));
        OnPropertyChanged(nameof(AddStatementActionLabel));
        OnPropertyChanged(nameof(AddStatementActionToolTip));
        OnPropertyChanged(nameof(EditorActionLabel));
        OnPropertyChanged(nameof(EditorActionToolTip));
        OnPropertyChanged(nameof(TerminalActionLabel));
        OnPropertyChanged(nameof(TerminalActionToolTip));
        OnPropertyChanged(nameof(RunActionToolTip));
        OnPropertyChanged(nameof(PrimaryActionToolTip));
        OnPropertyChanged(nameof(ComposedTextToolTip));
    }

    private void PersistSettings()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PersistSettingsImmediately()
    {
        if (SettingsSaveRequested is null)
        {
            _settingsService.Save(_settings);
            return;
        }

        SettingsSaveRequested.Invoke(this, EventArgs.Empty);
    }

    private ParameterEditorViewModel? FindPathParameter()
    {
        var preferredNames = new[] { "Path", "LiteralPath", "FilePath", "Directory", "Destination", "Source" };
        foreach (var name in preferredNames)
        {
            var match = ParameterEditors.FirstOrDefault(parameter =>
                !parameter.Definition.IsSwitch
                && string.Equals(parameter.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string QuotePath(string path, bool useDoubleQuotes)
    {
        return useDoubleQuotes
            ? $"\"{path.Replace("\"", "\\\"")}\""
            : $"'{path.Replace("'", "''")}'";
    }
}

public sealed class ParameterEditorViewModel : ObservableObject
{
    private readonly bool _usesDotNetPowerShellSyntax;
    private bool _isIncluded;
    private string _value = string.Empty;

    public ParameterEditorViewModel(
        CommandParameterDefinition definition,
        ExecutionTarget executionTarget,
        bool usesDotNetPowerShellSyntax = false)
    {
        Definition = definition;
        ExecutionTarget = executionTarget;
        _usesDotNetPowerShellSyntax =
            usesDotNetPowerShellSyntax
            && executionTarget == ExecutionTarget.PowerShell;
        _value = definition.DefaultValue;
        // PowerShell reports requirements across all parameter sets. Starting unchecked
        // prevents an empty requirement from becoming a literal <placeholder> in a
        // statement built for a different, valid parameter set.
        _isIncluded = definition.IncludeByDefault
                      || (executionTarget != ExecutionTarget.PowerShell
                          && definition.IsRequired);
    }

    public CommandParameterDefinition Definition { get; }
    public ExecutionTarget ExecutionTarget { get; }
    public bool IsSwitch => Definition.IsSwitch;
    public bool CanToggleInclusion =>
        !Definition.IsRequired
        || (ExecutionTarget == ExecutionTarget.PowerShell
            && !Definition.IncludeByDefault);
    public string Label => Definition.IsRequired ? $"{Definition.DisplayName} *" : Definition.DisplayName;
    public string Hint => ExecutionTarget == ExecutionTarget.CSharp
                          && IsCSharpStringType(Definition.TypeName)
        ? "String literal; prefix = to use a C# expression"
        : _usesDotNetPowerShellSyntax
          && IsCSharpStringType(Definition.TypeName)
            ? "String literal; prefix = to use a PowerShell expression"
        : string.IsNullOrWhiteSpace(Definition.TypeName)
            ? "Value"
            : Definition.TypeName;

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value)
                && !string.IsNullOrWhiteSpace(value)
                && !IsIncluded)
            {
                IsIncluded = true;
            }
        }
    }

    private static bool IsCSharpStringType(string typeName)
    {
        var normalized = typeName.Trim();
        foreach (var modifier in new[] { "ref ", "out ", "in " })
        {
            if (normalized.StartsWith(
                    modifier,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[modifier.Length..].TrimStart();
                break;
            }
        }

        return string.Equals(
                   normalized,
                   "string",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   normalized,
                   "string?",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   normalized,
                   "System.String",
                   StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CommandSegmentViewModel : ObservableObject
{
    private string _syntax;
    private string _syntaxHeading;
    private string _syntaxExplanation;
    private IReadOnlyList<CommandUsageExample> _examples;
    private bool _usesParameterSets;
    private bool _isSyntaxExpanded;

    public CommandSegmentViewModel(
        string text,
        CommandDefinition command,
        CommandGuideContent? guide,
        string language)
    {
        Text = text;
        CommandId = command.Id;
        CatalogId = command.CatalogId;
        CommandName = command.Name;
        ExecutionTarget = command.ExecutionTarget;
        PreferredShellKind = command.PreferredShellKind;
        CompositionMode = command.CompositionMode;
        IsExecutionSupported = command.IsExecutionSupported;
        ExecutionSupportMessage = command.ExecutionSupportMessage;
        Language = CommandLanguageRegistry.NormalizeDisplayName(language);
        _syntax = string.IsNullOrWhiteSpace(command.Template)
            ? command.Name
            : command.Template;
        _usesParameterSets =
            command.ExecutionTarget == ExecutionTarget.PowerShell
            && command.CompositionMode != CommandCompositionMode.Statement;
        _syntaxHeading = _usesParameterSets ? "PARAMETER SETS" : "SYNTAX";
        _syntaxExplanation =
            DotNetToolchainCatalogService.IsToolchainLibraryId(
                command.CatalogId)
            ? "Enter positional arguments and options exactly as accepted by the installed dotnet command."
            : _usesParameterSets
            ? "Each block is a valid combination of parameters. Use one parameter set at a time."
            : command.ExecutionTarget == ExecutionTarget.PowerShell
                ? "Replace each placeholder with a PowerShell value or expression of the indicated .NET type."
                : command.ExecutionTarget == ExecutionTarget.External
                    ? CommandLanguageRegistry.TryGet(
                            Language,
                            out var languageDefinition)
                        && !languageDefinition.SupportsTerminalExecution
                        ? "Replace each placeholder with source text for the selected editor surface, then insert it into a compatible document."
                        : "Replace each placeholder with source text valid for the selected language runtime."
                    : "Replace each placeholder with a C# value or expression of the indicated type.";
        _examples = command.Examples.ToArray();

        if (guide is not null)
        {
            ApplyGuide(guide);
        }
    }

    public string Text { get; }
    public string CommandId { get; }
    public string CatalogId { get; }
    public string CommandName { get; }
    public ExecutionTarget ExecutionTarget { get; }
    public TerminalShellKind PreferredShellKind { get; }
    public CommandCompositionMode CompositionMode { get; }
    public bool IsExecutionSupported { get; }
    public string ExecutionSupportMessage { get; }
    public string Language { get; }
    public string Syntax => _syntax;
    public string SyntaxHeading => _syntaxHeading;
    public string SyntaxExplanation => _syntaxExplanation;
    public IReadOnlyList<CommandUsageExample> Examples => _examples;
    public bool UsesParameterSets => _usesParameterSets;
    public bool HasSyntax => !string.IsNullOrWhiteSpace(Syntax);
    public bool HasExamples => Examples.Count > 0;
    public bool HasDocumentation => HasSyntax || HasExamples;
    public bool HasUsefulDocumentation =>
        HasDocumentation
        && !CommandId.StartsWith(
            "csharp-composer:",
            StringComparison.Ordinal);

    public bool IsSyntaxExpanded
    {
        get => _isSyntaxExpanded;
        set => SetProperty(ref _isSyntaxExpanded, value);
    }

    public string TargetLabel =>
        DotNetToolchainCatalogService.IsToolchainLibraryId(CatalogId)
            ? ".NET CLI"
            : ExecutionTarget switch
            {
                ExecutionTarget.CSharp => "C#",
                ExecutionTarget.External =>
                    CommandLanguageRegistry.TryGet(
                            Language,
                            out var languageDefinition)
                        && !languageDefinition.SupportsTerminalExecution
                        ? $"{languageDefinition.DisplayName} editor"
                        : CommandLanguageRegistry.GetRuntimeDisplayName(
                            PreferredShellKind),
                _ => PreferredShellKind == TerminalShellKind.WindowsPowerShell
                    ? "Windows PowerShell 5.1"
                    : "PowerShell"
            };

    public bool MatchesCommand(CommandDefinition command)
    {
        if (!string.IsNullOrWhiteSpace(CommandId)
            && !string.IsNullOrWhiteSpace(command.Id))
        {
            return string.Equals(
                CommandId,
                command.Id,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
                   CatalogId,
                   command.CatalogId,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   CommandName,
                   command.Name,
                   StringComparison.OrdinalIgnoreCase);
    }

    public void ApplyGuide(CommandGuideContent guide)
    {
        _syntax = guide.Syntax;
        _usesParameterSets = guide.UsesParameterSets;
        _syntaxHeading = guide.SyntaxHeading;
        _syntaxExplanation =
            DotNetToolchainCatalogService.IsToolchainLibraryId(CatalogId)
                ? "Enter positional arguments and options exactly as accepted by the installed dotnet command."
                : ExecutionTarget == ExecutionTarget.PowerShell
            && CompositionMode == CommandCompositionMode.Statement
                ? "Replace each placeholder with a PowerShell value or " +
                  "expression of the indicated .NET type."
                : guide.SyntaxExplanation;
        _examples = guide.Examples.ToArray();
        OnPropertyChanged(nameof(Syntax));
        OnPropertyChanged(nameof(UsesParameterSets));
        OnPropertyChanged(nameof(SyntaxHeading));
        OnPropertyChanged(nameof(SyntaxExplanation));
        OnPropertyChanged(nameof(HasSyntax));
        OnPropertyChanged(nameof(Examples));
        OnPropertyChanged(nameof(HasExamples));
        OnPropertyChanged(nameof(HasDocumentation));
        OnPropertyChanged(nameof(HasUsefulDocumentation));
    }
}

public sealed record CommandExecutionRequest(
    string CommandText,
    ExecutionTarget ExecutionTarget,
    TerminalShellKind PreferredShellKind,
    bool ExecuteImmediately);

public sealed class EditorInsertionRequest : EventArgs
{
    public EditorInsertionRequest(string text, string language)
    {
        Text = text;
        Language = language;
    }

    public string Text { get; }
    public string Language { get; }
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed record CommandObjectOption(string DisplayName, string ModuleName);

public sealed class LibrarySelectionViewModel : ObservableObject
{
    private bool _isSelected;

    public LibrarySelectionViewModel(
        CommandLibrary library,
        bool isSelected,
        string inputLanguage)
    {
        Library = library;
        IsCompatibleWithProfile = CommandBuilderViewModel.LanguagesMatch(
            library.Language,
            inputLanguage);
        ProfileLanguage =
            CommandBuilderViewModel.NormalizeLanguageName(inputLanguage);
        _isSelected = isSelected && IsCompatibleWithProfile;
    }

    public CommandLibrary Library { get; }
    public string DisplayName => Library.DisplayName;
    public string Description => Library.Description;
    public string Language =>
        CommandBuilderViewModel.NormalizeLanguageName(Library.Language);
    public string ProfileLanguage { get; }
    public bool IsCompatibleWithProfile { get; }
    public bool IsExecutionSupported => Library.IsExecutionSupported;
    public bool HasExecutionSupportNotice =>
        !Library.IsExecutionSupported
        && !string.IsNullOrWhiteSpace(Library.ExecutionSupportMessage);
    public string ExecutionSupportMessage =>
        Library.ExecutionSupportMessage;
    public string SelectionHint => !IsCompatibleWithProfile
        ? $"Input Syntax is set to {ProfileLanguage}. Switch to {Language} " +
          "to activate this library."
        : Library.Origin == CommandLibraryOrigin.ContentPackage
            ? $"{Description} Host validation passed. "
              + (Library.ContentReviewState == HyperlibReviewState.Reviewed
                  ? $"Publisher review: {Library.ContentReviewedBy}. "
                  : "Publisher review: Draft. ")
              + $"Provenance: {Library.ContentProvenance}"
        : string.IsNullOrWhiteSpace(Library.ExecutionSupportMessage)
            ? Description
            : $"{Description} {Library.ExecutionSupportMessage}";
    public string CountLabel
    {
        get
        {
            if (Library.EffectiveEntryCount > 0)
            {
                return $"{Library.EffectiveEntryCount:N0} entries";
            }

            if (Library.ReferenceAssemblyCount > 0)
            {
                return
                    $"{Library.ReferenceAssemblyCount:N0} reference assemblies";
            }

            return Library.Kind switch
            {
                CommandLibraryKind.Framework => "Framework",
                CommandLibraryKind.Sdk => "SDK",
                CommandLibraryKind.CommandLineTool => "Command-line tool",
                CommandLibraryKind.Core => "Core library",
                CommandLibraryKind.StandardLibrary => "Standard library",
                CommandLibraryKind.Specification => "Specification",
                CommandLibraryKind.PlatformApi => "Platform API",
                CommandLibraryKind.Language => "Language surface",
                CommandLibraryKind.Runtime => "Runtime surface",
                CommandLibraryKind.Module => "Module",
                CommandLibraryKind.Package => "Package",
                CommandLibraryKind.Extension => "Extension",
                _ => "Content library"
            };
        }
    }

    public string SourceLabel
    {
        get
        {
            var source = Library.Origin switch
            {
                CommandLibraryOrigin.Bundled => Library.IsStarterLibrary
                    ? "Bundled starter"
                    : "Bundled",
                CommandLibraryOrigin.Runtime =>
                    Library.Kind is CommandLibraryKind.Framework
                        or CommandLibraryKind.Sdk
                        or CommandLibraryKind.CommandLineTool
                        ? "Installed"
                        : "Runtime discovery",
                CommandLibraryOrigin.ContentPackage => "Hyperlib package",
                _ => "External library"
            };
            var kind = Library.Kind switch
            {
                CommandLibraryKind.Framework => "framework",
                CommandLibraryKind.Sdk => "SDK",
                CommandLibraryKind.CommandLineTool => "CLI",
                CommandLibraryKind.Core => "core",
                CommandLibraryKind.StandardLibrary => "standard library",
                CommandLibraryKind.Specification => "specification",
                CommandLibraryKind.PlatformApi => "platform API",
                CommandLibraryKind.Language => "language",
                CommandLibraryKind.Runtime => "runtime",
                CommandLibraryKind.Module => "module",
                CommandLibraryKind.Package => "package",
                CommandLibraryKind.Extension => "extension",
                _ => "content"
            };
            var labels = new[]
                {
                    source,
                    kind,
                    Library.Kind is CommandLibraryKind.Sdk
                        or CommandLibraryKind.CommandLineTool
                        ? Library.FrameworkVersion
                        : Library.TargetFramework
                }
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(" · ", labels);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsCompatibleWithProfile)
            {
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
}
