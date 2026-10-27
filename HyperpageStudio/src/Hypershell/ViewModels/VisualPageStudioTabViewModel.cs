using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed class VisualPageStudioTabViewModel : ObservableObject, IWorkspaceTab
{
    public const int MaximumUndoEntries = 100;
    private readonly VisualPageProjectService _projectService;
    private readonly VisualPageMutationEngine _mutationEngine;
    private readonly VisualPageGenerator _generator;
    private readonly VisualPageSchemaValidator _validator;
    private readonly VisualPageComponentCatalog _catalog;
    private readonly VisualPageTemplateService _templateService;
    private readonly VisualPageImportService _importService;
    private readonly VisualPageWebTemplateCaptureService
        _webTemplateCaptureService;
    private readonly VisualPageDesignCanvasRenderer _canvasRenderer;
    private readonly VisualPagePreviewBuildService _previewBuildService;
    private readonly VisualPageLocalHostService _hostService;
    private readonly VisualPageUserSourceService _userSourceService;
    private readonly AtlasArchivePublicationService _publicationService;
    private readonly Action<AtlasMyNetAppDefinition>? _atlasMyNetRegistration;
    private readonly WorkspaceAiAssistantViewModel? _aiAssistant;
    private readonly List<VisualPageDocument> _undo = [];
    private readonly List<VisualPageDocument> _redo = [];
    private VisualPageProjectSession _session;
    private VisualPageElement? _clipboard;
    private bool _clipboardIsCut;
    private string _savedProjectRevision;
    private string _savedPageRevision;
    private readonly Dictionary<string, string> _userSourceRecoveryNotices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _recoveredUserSources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unverifiedUserSources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _userSourceRecoveryIssues = new(StringComparer.Ordinal);
    private readonly List<VisualPageStudioDocumentRecovery> _pendingUserSourceRecoveries = [];
    private readonly List<VisualPageStudioDocumentRecovery> _pendingProjectDocumentRecoveries = [];
    private VisualPageElement? _selectedElement;
    private VisualPageComponentDescriptor? _selectedDescriptor;
    private VisualPageElement? _catalogElementDraft;
    private bool _isCatalogElementDraftActive;
    private bool _isCatalogDraftActivationReady;
    private bool _isRefreshingElementCatalog;
    private VisualPageAttributeDescriptor? _selectedAttributeDefinition;
    private VisualPagePropertyEntry? _selectedAttributeEntry;
    private VisualPagePropertyEntry? _selectedStyleEntry;
    private VisualPageTemplateDocument? _selectedTemplate;
    private string _templateSearchText = string.Empty;
    private string _selectedTemplateScopeFilter = "All types";
    private string _selectedTemplateLibraryFilter = "All libraries";
    private string _selectedTemplateCategoryFilter =
        VisualPageTemplateTaxonomy.AllCategories;
    private string _templateNameEditor = string.Empty;
    private string _templateDescriptionEditor = string.Empty;
    private string _templatePageName = "Archive Article";
    private string _templatePageRoute = "/archive/article-title";
    private string _webTemplateUrl = "https://";
    private string _webTemplateName = "Website starter";
    private string _webTemplateDescription =
        "Reusable semantic structure derived from an approved public website.";
    private string _webTemplateScope = "page";
    private string _webTemplateCategoryGroup = "Navigation";
    private string _webTemplateCategory = "Landing pages";
    private bool _webTemplatePermissionConfirmed;
    private VisualPageStudioToolDefinition? _selectedTool;
    private VisualPageProjectPage? _selectedProjectPage;
    private HyperpageProjectTreeNodeViewModel? _selectedProjectNode;
    private HyperpageProjectDocumentViewModel? _activeProjectDocument;
    private string _activeAuthoringContentId = string.Empty;
    private string _activeAuthoringLanguageId = string.Empty;
    private string _authoringSearchText = string.Empty;
    private string _selectedAuthoringCategory = "All categories";
    private HyperpageAuthoringRecipe? _selectedAuthoringRecipe;
    private string _authoringPreview = string.Empty;
    private VisualPageAsset? _selectedAsset;
    private VisualPageDiagnostic? _selectedDiagnostic;
    private IReadOnlyDictionary<string, string> _canvasAssetSources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private VisualPageBreakpoint _activeBreakpoint;
    private string _statusMessage = "Hyperpage Studio ready.";
    private string _pageNameEditor = string.Empty;
    private string _pageRouteEditor = string.Empty;
    private string _pageSlugEditor = string.Empty;
    private string _pageTitleEditor = string.Empty;
    private string _pageDescriptionEditor = string.Empty;
    private string _newPageName = "New Page";
    private string _newPageRoute = "/new-page";
    private string _assetAltText = string.Empty;
    private string _selectedTextEditor = string.Empty;
    private string _elementSearchText = string.Empty;
    private string _selectedElementScope = "Common";
    private string _selectedElementCategory = "All categories";
    private string _attributeName = string.Empty;
    private string _attributeValue = string.Empty;
    private string _styleName = string.Empty;
    private string _styleValue = string.Empty;
    private string _layoutDisplay = string.Empty;
    private string _layoutDirection = string.Empty;
    private string _layoutWrap = string.Empty;
    private string _layoutColumns = string.Empty;
    private string _layoutGap = string.Empty;
    private string _layoutPadding = string.Empty;
    private string _layoutMargin = string.Empty;
    private string _layoutFlexBasis = string.Empty;
    private string _layoutColumnSpan = string.Empty;
    private string _layoutRowSpan = string.Empty;
    private string _layoutMinWidth = string.Empty;
    private string _layoutMaxWidth = string.Empty;
    private string _layoutMinHeight = string.Empty;
    private string _layoutMaxHeight = string.Empty;
    private string _layoutAlignItems = string.Empty;
    private string _layoutJustifyContent = string.Empty;
    private string _layoutAlignSelf = string.Empty;
    private double _canvasZoomPercent = 100;
    private string _canvasViewportWidth = "Auto";
    private bool _showCanvasGrid;
    private bool _showCanvasOutlines;
    private string _stateName = "Value";
    private string _stateType = "string";
    private string _stateInitialValue = "string.Empty";
    private VisualPageStateKind _stateKind;
    private VisualPageEventKind _eventKind;
    private string _eventHandlerName = "HandleClick";
    private string _generatedRazor = string.Empty;
    private string _generatedCss = string.Empty;
    private string _generatedCSharp = string.Empty;
    private string _userCSharp = string.Empty;
    private string _userCss = string.Empty;
    private string _userJavaScript = string.Empty;
    private VisualPageUserSourceSnapshot? _csharpSnapshot;
    private VisualPageUserSourceSnapshot? _cssSnapshot;
    private VisualPageUserSourceSnapshot? _javaScriptSnapshot;
    private string _buildOutput = "No compiled preview has been built.";
    private VisualPageBuildResult? _latestBuild;
    private bool _isBusy;
    private bool _allowLanHosting;
    private bool _trustCustomCode;
    private string _adapterAddress = "127.0.0.1";
    private string _hostPortText = "0";
    private string _runtimePreviewUrl = string.Empty;
    private string _atlasCheckoutPath = string.Empty;
    private string _archiveSlug = string.Empty;
    private string _publishTargetBranch = string.Empty;
    private string _publicationReview = "No Atlas Archive publication has been prepared.";
    private string _publishedCommit = string.Empty;
    private bool _approveReviewedCode;
    private AtlasArchivePublicationPreparation? _publicationPreparation;
    private bool _isAiLiveAssistEnabled;
    private bool _isAiAssistantOpen;
    private bool _isDetached;
    private bool _isDisposed;
    private StudioLayoutSnapshot _layout;
    private StudioLayoutSnapshot? _personalDefaultLayout;
    private StudioLayoutSnapshot? _selectedLayoutPreset;
    private string _layoutPresetName = "My layout";
    private VisualPageStudioView? _studioView;
    private readonly Dictionary<string, HyperpageProjectDocumentViewModel>
        _projectDocumentsByContentId = new(StringComparer.OrdinalIgnoreCase);

    private VisualPageStudioTabViewModel(
        VisualPageProjectSession session,
        VisualPageProjectService projectService,
        VisualPageMutationEngine mutationEngine,
        VisualPageGenerator generator,
        VisualPageSchemaValidator validator,
        VisualPageComponentCatalog catalog,
        VisualPageTemplateService templateService,
        VisualPageDesignCanvasRenderer canvasRenderer,
        VisualPagePreviewBuildService previewBuildService,
        VisualPageLocalHostService hostService,
        VisualPageUserSourceService userSourceService,
        AtlasArchivePublicationService publicationService,
        Action<AtlasMyNetAppDefinition>? atlasMyNetRegistration,
        WorkspaceAiAssistantViewModel? aiAssistant,
        bool openAiAssistant)
    {
        _session = session;
        if (!string.IsNullOrWhiteSpace(session.Project.HyperlibAuthoringPath))
        {
            HyperlibWorkspace = new HyperlibStudioWorkspaceViewModel(session.RootPath);
            HyperlibWorkspace.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(HyperlibStudioWorkspaceViewModel.Context) or nameof(HyperlibStudioWorkspaceViewModel.ScopeTitle))
                {
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(DisplayTitle));
                    OnPropertyChanged(nameof(StudioDisplayTitle));
                    OnPropertyChanged(nameof(StudioObjectKind));
                }
                if (args.PropertyName == nameof(HyperlibStudioWorkspaceViewModel.HasUnsavedAppearanceChanges))
                {
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            };
        }
        _projectService = projectService;
        _mutationEngine = mutationEngine;
        _generator = generator;
        _validator = validator;
        _catalog = catalog;
        _templateService = templateService;
        _importService = new VisualPageImportService(catalog);
        _webTemplateCaptureService = new VisualPageWebTemplateCaptureService(
            catalog);
        _canvasRenderer = canvasRenderer;
        _previewBuildService = previewBuildService;
        _hostService = hostService;
        _userSourceService = userSourceService;
        _publicationService = publicationService;
        _atlasMyNetRegistration = atlasMyNetRegistration;
        _aiAssistant = aiAssistant;
        _isAiAssistantOpen = aiAssistant is not null
                             && (openAiAssistant
                                 || session.Project.Foundation.CodingMode ==
                                 HyperpageCodingMode.AiAssisted);
        _savedProjectRevision = session.Project.Revision;
        _savedPageRevision = session.Page.Revision;
        _layout = NormalizeLayoutSnapshot(VisualPageJson.Clone(
            session.Project.LayoutOverride
            ?? VisualPageProjectService.CreateDefaultStudioLayout()));
        SessionId = Guid.NewGuid().ToString("N");

        ElementDescriptors = catalog.Descriptors
            .OrderBy(value => value.Category, StringComparer.Ordinal)
            .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
            .ToList();
        ElementCategories = ["All categories"];
        TemplateCategoryFilters = [VisualPageTemplateTaxonomy.AllCategories];
        RefreshElementCatalog();
        Templates = new ObservableCollection<VisualPageTemplateDocument>(
            templateService.BuiltInTemplates);
        RefreshTemplateFilter();
        StudioTools = VisualPageStudioToolCatalog.Definitions;
        ProjectAssets = new ObservableCollection<VisualPageAsset>(
            session.Project.Assets);
        NamedLayoutPresets = [];
        RefreshCanvasAssetSources();
        _selectedDescriptor = FilteredElementDescriptors.FirstOrDefault();
        _selectedTemplate = FilteredTemplates.FirstOrDefault();
        _selectedTool = HasHyperlibAuthoring
            ? StudioTools.FirstOrDefault(tool => tool.Id == "project")
            : StudioTools.FirstOrDefault();
        _selectedProjectPage = session.Project.Pages.FirstOrDefault(page =>
            page.PageId == session.Page.PageId);
        AuthoringRecipes = HyperpageLanguageAuthoringCatalog.Recipes;
        _atlasCheckoutPath = FindDefaultAtlasCheckout(session.RootPath);
        _archiveSlug = session.Page.Slug;

        SaveCommand = new AsyncRelayCommand(
            async () => await SaveProjectAsync(),
            () => !IsBusy);
        BuildCommand = new AsyncRelayCommand(BuildAsync, () => !IsBusy);
        StartHostCommand = new AsyncRelayCommand(StartHostAsync, () => !IsBusy);
        StopHostCommand = new AsyncRelayCommand(StopHostAsync);
        PreparePublicationCommand = new AsyncRelayCommand(
            PreparePublicationAsync,
            () => !IsBusy);
        PublishExactCommitCommand = new AsyncRelayCommand(
            PublishExactCommitAsync,
            () => !IsBusy && _publicationPreparation is not null);
        AddElementCommand = new RelayCommand(
            AddSelectedElement,
            () => CanAddSelectedDescriptor);
        AddDescriptorCommand = new RelayCommand(
            parameter => AddDescriptorToSelected(parameter as string),
            parameter => parameter is string descriptorId
                         && CanAddDescriptorToSelected(descriptorId));
        InsertTemplateCommand = new AsyncRelayCommand(
            UseSelectedTemplateAsync,
            CanUseSelectedTemplate);
        ApplyTextCommand = new RelayCommand(
            ApplySelectedText,
            () => InspectorElementSupportsText);
        SetAttributeCommand = new RelayCommand(
            SetSelectedAttribute,
            CanSetSelectedAttribute);
        RemoveAttributeCommand = new RelayCommand(
            RemoveSelectedAttribute,
            CanRemoveSelectedAttribute);
        SetStyleCommand = new RelayCommand(
            SetSelectedStyle,
            CanSetSelectedStyle);
        RemoveStyleCommand = new RelayCommand(
            RemoveSelectedStyle,
            CanRemoveSelectedStyle);
        ApplyLayoutCommand = new RelayCommand(ApplySelectedLayout);
        AddStateCommand = new RelayCommand(AddState);
        AddEventCommand = new RelayCommand(
            AddEvent,
            () => SelectedElementSupportsEvents);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        CopyCommand = new RelayCommand(CopySelected, () => SelectedElement is not null);
        CutCommand = new RelayCommand(CutSelected, CanMutateSelectedNonRoot);
        PasteCommand = new RelayCommand(PasteIntoSelected, () => _clipboard is not null);
        DuplicateCommand = new RelayCommand(DuplicateSelected, CanMutateSelectedNonRoot);
        DeleteCommand = new RelayCommand(DeleteSelected, CanMutateSelectedNonRoot);
        WrapCommand = new RelayCommand(WrapSelected, CanMutateSelectedNonRoot);
        UnwrapCommand = new RelayCommand(UnwrapSelected, CanMutateSelectedNonRoot);
        ToggleLockCommand = new RelayCommand(ToggleLock, () => SelectedElement is not null);
        ToggleHiddenCommand = new RelayCommand(ToggleHidden, () => SelectedElement is not null);
        MoveUpCommand = new RelayCommand(
            () => MoveSelectedSibling(-1),
            () => CanMoveSelectedSibling(-1));
        MoveDownCommand = new RelayCommand(
            () => MoveSelectedSibling(1),
            () => CanMoveSelectedSibling(1));
        SelectParentCommand = new RelayCommand(
            SelectParent,
            () => SelectedElement is not null
                  && SelectedElement.ElementId != _session.Page.Root.ElementId);
        OpenInspectorCommand = new RelayCommand(
            OpenSelectedElementInspector,
            () => SelectedElement is not null);
        OpenSelectedPageCommand = new AsyncRelayCommand(OpenSelectedPageAsync, () => !IsBusy);
        CreatePageCommand = new AsyncRelayCommand(
            CreatePageAsync,
            () => !IsBusy && !HasUnsavedChanges);
        ApplyPageMetadataCommand = new RelayCommand(ApplyPageMetadata);
        ApplyAssetAltTextCommand = new RelayCommand(
            ApplyAssetAltText,
            () => SelectedAsset is not null);
        UseAssetCommand = new RelayCommand(
            UseSelectedAsset,
            () => SelectedAsset is not null && SelectedElement is not null);
        SaveLayoutPresetCommand = new RelayCommand(SaveLayoutPreset);
        ApplyLayoutPresetCommand = new RelayCommand(
            ApplySelectedLayoutPreset,
            () => SelectedLayoutPreset is not null);
        DeleteLayoutPresetCommand = new RelayCommand(
            DeleteSelectedLayoutPreset,
            () => SelectedLayoutPreset is not null);
        SetPersonalDefaultLayoutCommand = new RelayCommand(
            SetPersonalDefaultLayout);
        CanvasOnlyCommand = new RelayCommand(SetCanvasOnlyLayout);
        DocumentOnlyCommand = new RelayCommand(SetDocumentOnlyLayout);
        ResetLayoutCommand = new RelayCommand(ResetLayout);
        ResetCanvasViewCommand = new RelayCommand(ResetCanvasView);
        SplitHorizontalCommand = new RelayCommand(() =>
            SplitCanvas(StudioSplitOrientation.Horizontal));
        SplitVerticalCommand = new RelayCommand(() =>
            SplitCanvas(StudioSplitOrientation.Vertical));
        ToggleLeftToolPanelCommand = new RelayCommand(() =>
            ToggleToolPanel(left: true));
        ToggleRightToolPanelCommand = new RelayCommand(() =>
            ToggleToolPanel(left: false));
        ToggleAiAssistantCommand = new RelayCommand(ToggleAiAssistant);
        DetachCommand = new RelayCommand(() =>
            DetachRequested?.Invoke(this, EventArgs.Empty));
        QuickAddBindingCommand = new RelayCommand(
            QuickAddBinding,
            () => SelectedElementSupportsBindings);
        ConvertToTemplateCommand = new AsyncRelayCommand(
            () => ConvertSelectedToTemplateAsync(personal: false),
            CanCreateTemplate);
        ConvertToPersonalTemplateCommand = new AsyncRelayCommand(
            () => ConvertSelectedToTemplateAsync(personal: true),
            CanCreateTemplate);
        CaptureWebTemplateCommand = new AsyncRelayCommand(
            BeginWebTemplateCaptureAsync,
            CanPrepareWebTemplateCapture);
        OpenSourceCommand = new RelayCommand(() =>
            PaneRequested?.Invoke(this, "generated-razor"));
        RefreshProjectTreeCommand = new AsyncRelayCommand(
            RefreshProjectTreeAsync,
            () => !IsBusy);
        OpenProjectNodeCommand = new AsyncRelayCommand(
            OpenSelectedProjectNodeAsync,
            () => !IsBusy && SelectedProjectNode?.CanOpen == true);
        InsertAuthoringSnippetCommand = new RelayCommand(
            InsertSelectedAuthoringSnippet,
            () => CanInsertAuthoringSnippet);

        LoadPageMetadataEditors();
        RefreshDocumentState(selectElementId: session.Page.Root.ElementId);
        _isCatalogDraftActivationReady = true;
        if (!UsesUnifiedAuthoringCatalog) ActivateCatalogElementDraft(_selectedDescriptor);
    }

    public static async Task<VisualPageStudioTabViewModel> CreateAsync(
        VisualPageProjectSession session,
        VisualPageProjectService projectService,
        VisualPageMutationEngine mutationEngine,
        VisualPageGenerator generator,
        VisualPageSchemaValidator validator,
        VisualPageComponentCatalog catalog,
        VisualPageTemplateService templateService,
        VisualPageDesignCanvasRenderer canvasRenderer,
        VisualPagePreviewBuildService previewBuildService,
        VisualPageLocalHostService hostService,
        VisualPageUserSourceService userSourceService,
        AtlasArchivePublicationService publicationService,
        Action<AtlasMyNetAppDefinition>? atlasMyNetRegistration = null,
        CancellationToken cancellationToken = default,
        WorkspaceAiAssistantViewModel? aiAssistant = null,
        bool openAiAssistant = false)
    {
        var viewModel = new VisualPageStudioTabViewModel(
            session,
            projectService,
            mutationEngine,
            generator,
            validator,
            catalog,
            templateService,
            canvasRenderer,
            previewBuildService,
            hostService,
            userSourceService,
            publicationService,
            atlasMyNetRegistration,
            aiAssistant,
            openAiAssistant);
        await viewModel.LoadTemplateCatalogAsync(cancellationToken);
        await viewModel.LoadUserSourcesAsync(cancellationToken);
        await viewModel.RefreshProjectTreeAsync();
        await viewModel.RestoreProjectDocumentsAsync(cancellationToken);
        if (viewModel.HyperlibWorkspace is { } workspace)
            await workspace.LoadAsync(cancellationToken);
        return viewModel;
    }

    public event EventHandler? CanvasInvalidated;
    public event EventHandler? LayoutChanged;
    public event EventHandler? DetachRequested;
    public event EventHandler<string>? PaneRequested;
    public event EventHandler<HyperpageDocumentInsertionRequest>?
        ProjectDocumentInsertionRequested;
    public event EventHandler<string>? RuntimePreviewRequested;
    public event Action<string>? HyperlibProjectOpenRequested;
    public event Action<string?>? CatalogElementPreviewRequested;
    public event Action<string>? CatalogElementCommitRequested;
    public event EventHandler<IReadOnlyList<VisualPageSemanticChange>>?
        MutationApplied;

    public WorkspaceTabKind Kind => WorkspaceTabKind.VisualPage;
    public string SessionId { get; }
    public string AiLiveSourceId => $"visual-page:{SessionId}";
    public string Title => IsHyperlibEditor
        ? HyperlibWorkspace?.IsLibraryRoot == true ? "Hyperlib Editor" : $"{HyperlibWorkspace?.Context?.Title ?? _session.Project.Name} · Hyperlib"
        : HasHyperlibAuthoring
        ? HyperlibWorkspace?.Context?.Title ?? _session.Project.Name
        : _session.Project.Name;
    public string DisplayTitle => HasUnsavedChanges ? $"{Title} •" : Title;
    public string StudioObjectKind => !HasHyperlibAuthoring ? "Page"
        : HyperlibWorkspace?.IsLibraryRoot == true ? "Template"
        : HyperlibWorkspace?.Context?.WorkId is not null ? "Work" : "Template";
    public string StudioDisplayTitle => Project.Name;
    public string StudioProjectKindLabel => HasHyperlibAuthoring ? "Hyperlib"
        : Project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.Html ? "HTML" : "Razor";
    public string RenameText
    {
        get => _session.Page.Name;
        set { }
    }
    public bool IsRenaming => false;
    public bool CanRename => false;
    public bool HasUnsavedChanges => HasUnsavedHyperlibContent ||
        !string.Equals(_session.Page.Revision, _savedPageRevision, StringComparison.Ordinal)
        || !string.Equals(_session.Project.Revision, _savedProjectRevision, StringComparison.Ordinal)
        || SourceChanged(_csharpSnapshot, UserCSharp)
        || SourceChanged(_cssSnapshot, UserCss)
        || SourceChanged(_javaScriptSnapshot, UserJavaScript)
        || OpenProjectDocuments.Any(document => document.IsDirty)
        || _userSourceRecoveryIssues.Count != 0
        || _pendingUserSourceRecoveries.Count != 0
        || _pendingProjectDocumentRecoveries.Count != 0
        || HyperlibWorkspace?.HasUnsavedAppearanceChanges == true;
    public bool HasUnsavedHyperlibContent { get; private set; }
    public Func<Task<bool>>? SaveHyperlibContentAsync { get; set; }
    public Func<Task<bool>>? PrepareHyperlibContentToLeaveAsync { get; set; }
    public event Action? StudioNewProjectRequested;
    public void RequestStudioNewProject() => StudioNewProjectRequested?.Invoke();
    public void SetHyperlibContentDirty(bool dirty)
    {
        if (HasUnsavedHyperlibContent == dirty) return;
        HasUnsavedHyperlibContent = dirty;
        OnPropertyChanged(nameof(HasUnsavedHyperlibContent));
        NotifyDirtyState();
    }
    public string TabColor => "#35205A";
    public Brush TabBackgroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(53, 32, 90));
    public Brush TabForegroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(240, 237, 248));
    public VisualPageStudioView StudioView =>
        _studioView ??= new VisualPageStudioView(this);
    public string ProjectRoot => _session.RootPath;
    public string ProjectId => _session.Project.ProjectId;
    public string PageId => _session.Page.PageId;
    public string Revision => _session.Page.Revision;
    public string SavedProjectRevision => _savedProjectRevision;
    public string SavedPageRevision => _savedPageRevision;
    public VisualPageDocument Document => _session.Page;
    public VisualPageProjectManifest Project => _session.Project;
    public bool HasHyperlibAuthoring => !string.IsNullOrWhiteSpace(Project.HyperlibAuthoringPath);
    public bool IsStandardStudioProject => !HasHyperlibAuthoring;
    public bool IsHyperlibEditor { get; private set; }

    public void EnableHyperlibEditor()
    {
        if (!HasHyperlibAuthoring || IsHyperlibEditor) return;
        IsHyperlibEditor = true;
        ActivateCatalogElementDraft(_selectedDescriptor);
        // This is a workspace preference, not a source edit. Keep all open code and unsaved drafts.
        _layout = VisualPageJson.Clone(_layout);
        _layout.IsLeftToolPanelOpen = false;
        _layout.IsRightToolPanelOpen = false;
        IsAiAssistantOpen = false;
        OnPropertyChanged(nameof(IsHyperlibEditor));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Layout));
        RaiseToolPanelStateChanged();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
    public HyperlibStudioWorkspaceViewModel? HyperlibWorkspace { get; }
    public IReadOnlyList<HyperlibStudioProject> HyperlibProjects
    {
        get
        {
            try { return HasHyperlibAuthoring ? HyperlibStudioAuthoringService.DiscoverProjects(ProjectRoot) : []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or VisualPageValidationException) { return []; }
        }
    }
    public void OpenHyperlibProject(HyperlibStudioProject project)
    {
        if(HyperlibProjects.Any(p => string.Equals(p.RootPath, project.RootPath, StringComparison.OrdinalIgnoreCase)))
            HyperlibProjectOpenRequested?.Invoke(project.RootPath);
    }
    public bool IsHtmlProject => Project.Foundation.MarkupLanguage ==
                                 HyperpageMarkupLanguage.Html;
    public HyperpagePageFileType ActivePageFileType =>
        VisualPageProjectService.ResolvePageFileType(Project, Document);
    public bool SupportsCSharpSource => ActivePageFileType ==
                                        HyperpagePageFileType.RazorPage;
    public string GeneratedMarkupLabel => ActivePageFileType switch
    {
        HyperpagePageFileType.Html => "Generated HTML",
        HyperpagePageFileType.Cshtml => "Generated CSHTML",
        _ => "Generated Razor"
    };
    public string ProjectFoundationSummary
    {
        get
        {
            var markup = IsHtmlProject ? "HTML" : "Razor Pages";
            var hosting = Project.Foundation.HostingTarget ==
                          HyperpageHostingTarget.Local
                ? "Local"
                : "Web server";
            var pages = Project.Foundation.SiteStructure ==
                        HyperpageSiteStructure.DynamicSite
                ? "Dynamic site"
                : $"{Project.Foundation.PlannedPageCount} page"
                  + (Project.Foundation.PlannedPageCount == 1 ? string.Empty : "s");
            var coding = Project.Foundation.CodingMode ==
                         HyperpageCodingMode.AiAssisted
                ? "AI-assisted"
                : "Manual";
            return $"{markup} · {hosting} · {pages} · {coding}";
        }
    }
    public WorkspaceAiAssistantViewModel? AiAssistant => _aiAssistant;
    public bool HasAiAssistant => AiAssistant is not null;
    public IReadOnlyList<VisualPageComponentDescriptor> ElementDescriptors { get; }
    public IReadOnlyList<string> ElementScopes { get; } =
    [
        "Common", "All HTML", "Input controls", "Blazor & logic",
        "Atlas patterns", "Legacy HTML", "Everything"
    ];
    public ObservableCollection<string> ElementCategories { get; }
    public ObservableCollection<VisualPageComponentDescriptor>
        FilteredElementDescriptors
    { get; } = [];
    public ObservableCollection<VisualPageAttributeDescriptor>
        AvailableAttributes
    { get; } = [];
    public ObservableCollection<VisualPagePropertyEntry>
        SelectedElementAttributes
    { get; } = [];
    public ObservableCollection<VisualPagePropertyEntry>
        SelectedElementStyles
    { get; } = [];
    public ObservableCollection<VisualPageTemplateDocument> Templates { get; }
    public IReadOnlyList<VisualPageTemplateDocument> PageCreationTemplates =>
        Templates.Where(template => string.Equals(
                template.Scope,
                "page",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    public IReadOnlyList<HyperpagePageFileType> PageCreationFileTypes =>
        IsHtmlProject
            ? [HyperpagePageFileType.Html]
            : string.Equals(Project.RazorProjectPath, Project.LocalHostProjectPath, StringComparison.OrdinalIgnoreCase)
                ? [HyperpagePageFileType.Cshtml, HyperpagePageFileType.Html]
            :
            [
                HyperpagePageFileType.Html,
                HyperpagePageFileType.Cshtml,
                HyperpagePageFileType.RazorPage
            ];
    public bool CanCreatePage => !IsBusy && !HasUnsavedChanges;
    public ObservableCollection<VisualPageTemplateDocument> FilteredTemplates
    { get; } = [];
    public IReadOnlyList<string> TemplateScopeFilters { get; } =
        ["All types", "Pages", "Sections", "Elements"];
    public IReadOnlyList<string> TemplateLibraryFilters { get; } =
        [
            "All libraries", VisualPageAtlasTemplateCatalog.LibraryName,
            VisualPageCommonTemplateCatalog.LibraryName, "Core essentials",
            "This project", "My templates"
        ];
    public ObservableCollection<string> TemplateCategoryFilters { get; }
    public IReadOnlyList<string> WebTemplateScopes { get; } =
        ["page", "section", "element"];
    public IReadOnlyList<string> WebTemplateCategoryGroups =>
        VisualPageCommonTemplateCatalog.CategoryGroups;
    public IReadOnlyList<VisualPageStudioToolDefinition> StudioTools { get; }
    public IReadOnlyList<VisualPageBreakpoint> Breakpoints { get; } =
        Enum.GetValues<VisualPageBreakpoint>();
    public IReadOnlyList<VisualPageStateKind> StateKinds { get; } =
        Enum.GetValues<VisualPageStateKind>();
    public IReadOnlyList<VisualPageEventKind> EventKinds { get; } =
        Enum.GetValues<VisualPageEventKind>();
    public IReadOnlyList<double> CanvasZoomChoices { get; } =
        [25, 50, 75, 100, 125, 150, 200, 300, 400];
    public IReadOnlyList<string> CanvasViewportWidths { get; } =
        ["Auto", "375", "430", "768", "1024", "1280", "1440", "1920"];
    public IReadOnlyList<string> DisplayChoices { get; } =
        ["", "block", "inline", "inline-block", "flex", "inline-flex", "grid", "inline-grid", "contents", "none"];
    public IReadOnlyList<string> FlexDirectionChoices { get; } =
        ["", "row", "row-reverse", "column", "column-reverse"];
    public IReadOnlyList<string> FlexWrapChoices { get; } =
        ["", "nowrap", "wrap", "wrap-reverse"];
    public IReadOnlyList<string> AlignmentChoices { get; } =
        ["", "start", "end", "center", "stretch", "baseline", "normal"];
    public IReadOnlyList<string> JustificationChoices { get; } =
        ["", "start", "end", "center", "space-between", "space-around", "space-evenly", "stretch"];
    public IReadOnlyList<string> CssPropertySuggestions { get; } =
    [
        "accent-color", "aspect-ratio", "background", "background-color",
        "border", "border-color", "border-radius", "border-style",
        "border-width", "box-shadow", "color", "cursor", "filter", "font",
        "font-family", "font-size", "font-style", "font-weight", "height",
        "letter-spacing", "line-height", "list-style", "object-fit",
        "object-position", "opacity", "outline", "overflow", "overflow-x",
        "overflow-y", "text-align", "text-decoration", "text-overflow",
        "text-shadow", "text-transform", "transform", "transition",
        "vertical-align", "visibility", "white-space", "width", "word-break"
    ];
    public ObservableCollection<VisualPageTreeNodeViewModel> RootTree { get; } = [];
    public ObservableCollection<VisualPageDiagnostic> Diagnostics { get; } = [];
    public ObservableCollection<VisualPageAsset> ProjectAssets { get; }
    public ObservableCollection<StudioLayoutSnapshot> NamedLayoutPresets { get; }
    public IReadOnlyList<VisualPageProjectPage> ProjectPages => _session.Project.Pages;
    public ObservableCollection<HyperpageProjectTreeNodeViewModel> ProjectTree
    { get; } = [];
    public ObservableCollection<HyperpageProjectDocumentViewModel>
        OpenProjectDocuments
    { get; } = [];
    public IReadOnlyList<HyperpageAuthoringRecipe> AuthoringRecipes { get; }
    public ObservableCollection<HyperpageAuthoringRecipe>
        FilteredAuthoringRecipes
    { get; } = [];
    public ObservableCollection<string> AuthoringCategories { get; } = [];
    public ObservableCollection<HyperpageAuthoringFieldViewModel>
        AuthoringFields
    { get; } = [];
    public StudioLayoutSnapshot Layout => _layout;
    public bool IsCanvasOnlyWorkspace =>
        ContainsPaneContent(Layout.Root, "canvas")
        && EnumeratePanes(Layout.Root).SelectMany(pane => pane.PaneContents)
            .All(content => string.Equals(
                content,
                "canvas",
                StringComparison.Ordinal));
    public bool IsDocumentOnlyWorkspace =>
        !ContainsPaneContent(Layout.Root, "canvas")
        && ContainsCodePane(Layout.Root);
    public bool IsCodeRightWorkspace =>
        FindCanvasCodeSplit(Layout.Root)?.Orientation
        == StudioSplitOrientation.Horizontal;
    public bool IsCodeBelowWorkspace =>
        FindCanvasCodeSplit(Layout.Root)?.Orientation
        == StudioSplitOrientation.Vertical;
    public bool IsLeftToolPanelOpen => !IsHyperlibEditor && Layout.IsLeftToolPanelOpen;
    public bool IsLeftToolPanelClosed => !IsLeftToolPanelOpen;
    public bool IsRightToolPanelOpen => !IsHyperlibEditor && Layout.IsRightToolPanelOpen;
    public bool IsRightToolPanelClosed => !IsRightToolPanelOpen;
    public GridLength LeftToolPanelWidth => new(
        IsLeftToolPanelOpen ? 300 : 0);
    public GridLength LeftToolPanelSplitterWidth => new(
        IsLeftToolPanelOpen ? 5 : 0);
    public GridLength RightToolPanelWidth => new(
        IsRightToolPanelOpen ? 340 : 0);
    public GridLength RightToolPanelSplitterWidth => new(
        IsRightToolPanelOpen ? 5 : 0);
    public bool IsAiAssistantOpen
    {
        get => _isAiAssistantOpen;
        private set
        {
            if (SetProperty(ref _isAiAssistantOpen, value))
            {
                OnPropertyChanged(nameof(IsAiAssistantClosed));
                OnPropertyChanged(nameof(AiAssistantPanelHeight));
                OnPropertyChanged(nameof(AiAssistantSplitterHeight));
            }
        }
    }
    public bool IsAiAssistantClosed => !IsAiAssistantOpen;
    public GridLength AiAssistantPanelHeight => new(
        !IsHyperlibEditor && IsAiAssistantOpen && HasAiAssistant ? 300 : 0);
    public GridLength AiAssistantSplitterHeight => new(
        !IsHyperlibEditor && IsAiAssistantOpen && HasAiAssistant ? 5 : 0);
    public string CanvasHtml => _canvasRenderer.Render(
        _session.Page,
        SelectedElement?.ElementId,
        ActiveBreakpoint,
        _canvasAssetSources,
        new VisualPageCanvasOptions(
            CanvasZoomPercent,
            CanvasViewportWidth,
            ShowCanvasGrid,
            ShowCanvasOutlines),
        CanvasStylesheetUrls,
        CanvasFoundationStylesheetUrls);
    public IReadOnlyList<string> StylesheetPaths => Document.StylesheetPaths;
    public IReadOnlyList<string> CanvasStylesheetUrls =>
        new[] { VisualPageProjectService.UserCssPath(Project, Document) }
            .Concat(StylesheetPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => HyperpageStylesheetService.CanvasOrigin
                + HyperpageStylesheetService.PublishedUrl(Project, path))
            .ToList();
    public IReadOnlyList<string> CanvasFoundationStylesheetUrls =>
        (Project.SharedStylesheetPaths ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => HyperpageStylesheetService.CanvasOrigin
                + HyperpageStylesheetService.PublishedUrl(Project, path))
            .ToList();
    public string DocumentJson => System.Text.Json.JsonSerializer.Serialize(
        _session.Page,
        VisualPageJson.Options);

    public ICommand SaveCommand { get; }
    public ICommand BuildCommand { get; }
    public ICommand StartHostCommand { get; }
    public ICommand StopHostCommand { get; }
    public ICommand PreparePublicationCommand { get; }
    public ICommand PublishExactCommitCommand { get; }
    public ICommand AddElementCommand { get; }
    public RelayCommand AddDescriptorCommand { get; }
    public ICommand InsertTemplateCommand { get; }
    public ICommand ApplyTextCommand { get; }
    public ICommand SetAttributeCommand { get; }
    public ICommand RemoveAttributeCommand { get; }
    public ICommand SetStyleCommand { get; }
    public ICommand RemoveStyleCommand { get; }
    public ICommand ApplyLayoutCommand { get; }
    public ICommand AddStateCommand { get; }
    public ICommand AddEventCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand CutCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand WrapCommand { get; }
    public RelayCommand UnwrapCommand { get; }
    public RelayCommand ToggleLockCommand { get; }
    public RelayCommand ToggleHiddenCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand SelectParentCommand { get; }
    public RelayCommand OpenInspectorCommand { get; }
    public ICommand OpenSelectedPageCommand { get; }
    public ICommand CreatePageCommand { get; }
    public ICommand ApplyPageMetadataCommand { get; }
    public RelayCommand ApplyAssetAltTextCommand { get; }
    public RelayCommand UseAssetCommand { get; }
    public ICommand SaveLayoutPresetCommand { get; }
    public RelayCommand ApplyLayoutPresetCommand { get; }
    public RelayCommand DeleteLayoutPresetCommand { get; }
    public ICommand SetPersonalDefaultLayoutCommand { get; }
    public ICommand CanvasOnlyCommand { get; }
    public ICommand DocumentOnlyCommand { get; }
    public ICommand ResetLayoutCommand { get; }
    public ICommand ResetCanvasViewCommand { get; }
    public ICommand SplitHorizontalCommand { get; }
    public ICommand SplitVerticalCommand { get; }
    public ICommand ToggleLeftToolPanelCommand { get; }
    public ICommand ToggleRightToolPanelCommand { get; }
    public ICommand ToggleAiAssistantCommand { get; }
    public ICommand DetachCommand { get; private set; } = null!;
    public ICommand QuickAddBindingCommand { get; private set; } = null!;
    public ICommand ConvertToTemplateCommand { get; private set; } = null!;
    public ICommand ConvertToPersonalTemplateCommand { get; private set; } = null!;
    public AsyncRelayCommand CaptureWebTemplateCommand { get; private set; } = null!;
    public ICommand OpenSourceCommand { get; private set; } = null!;
    public AsyncRelayCommand RefreshProjectTreeCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenProjectNodeCommand { get; private set; } = null!;
    public RelayCommand InsertAuthoringSnippetCommand { get; private set; } = null!;

    public bool IsAiLiveAssistEnabled
    {
        get => _isAiLiveAssistEnabled;
        set => SetProperty(ref _isAiLiveAssistEnabled, value);
    }

    public bool IsDetached
    {
        get => _isDetached;
        set => SetProperty(ref _isDetached, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (BuildCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (StartHostCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (PreparePublicationCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (PublishExactCommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (CreatePageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanCreatePage));
                (InsertTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ConvertToTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ConvertToPersonalTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
                RefreshProjectTreeCommand?.RaiseCanExecuteChanged();
                OpenProjectNodeCommand?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanRenameSelectedProjectNode));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public HyperpageProjectTreeNodeViewModel? SelectedProjectNode
    {
        get => _selectedProjectNode;
        set
        {
            if (!SetProperty(ref _selectedProjectNode, value))
            {
                return;
            }
            OnPropertyChanged(nameof(SelectedProjectNodeSummary));
            OnPropertyChanged(nameof(CanRenameSelectedProjectNode));
            OpenProjectNodeCommand?.RaiseCanExecuteChanged();
        }
    }

    public string SelectedProjectNodeSummary => SelectedProjectNode is null
        ? ProjectRoot
        : SelectedProjectNode.IsProjectRoot
            ? ProjectRoot
            : SelectedProjectNode.RelativePath;

    public bool CanRenameSelectedProjectNode =>
        SelectedProjectNode?.CanRename == true && !IsBusy;

    public HyperpageProjectDocumentViewModel? ActiveProjectDocument
    {
        get => _activeProjectDocument;
        private set
        {
            if (SetProperty(ref _activeProjectDocument, value))
            {
                OnPropertyChanged(nameof(HasActiveProjectDocument));
            }
        }
    }

    public bool HasActiveProjectDocument => ActiveProjectDocument is not null;
    public string ActiveAuthoringContentId => _activeAuthoringContentId;
    public string ActiveAuthoringLanguageId => _activeAuthoringLanguageId;
    public bool HasActiveAuthoringDocument =>
        !string.IsNullOrWhiteSpace(ActiveAuthoringContentId)
        && HyperpageLanguageAuthoringCatalog.Supports(ActiveAuthoringLanguageId);
    public string ActiveAuthoringLanguageName => ActiveAuthoringLanguageId
        .ToLowerInvariant() switch
        {
            "css" => "CSS",
            "scss" => "SCSS",
            "sass" => "Sass",
            "less" => "Less",
            "javascript" => "JavaScript",
            "typescript" => "TypeScript",
            "csharp" => "C#",
            _ => "Code"
        };
    public string ActiveAuthoringDocumentName => ActiveProjectDocument?.FileName
                                                   ?? ActiveAuthoringContentId switch
                                                   {
                                                       "css" => Path.GetFileName(
                                                           VisualPageProjectService.UserCssPath(
                                                               Project,
                                                               Document)),
                                                       "javascript" => Path.GetFileName(
                                                           VisualPageProjectService.UserJavaScriptPath(
                                                               Project,
                                                               Document)),
                                                       "csharp" => Path.GetFileName(
                                                           VisualPageProjectService.UserCSharpPath(
                                                               Project,
                                                               Document)),
                                                       _ => "Choose a source document"
                                                   };
    public string AuthoringRelationshipSummary =>
        HyperpageLanguageAuthoringCatalog.RelationshipSummary(
            ActiveAuthoringLanguageId);
    public string AuthoringLibrarySummary => HasActiveAuthoringDocument
        ? $"{FilteredAuthoringRecipes.Count:N0} shown · "
          + $"{AuthoringRecipes.Count(recipe => recipe.Supports(ActiveAuthoringLanguageId)):N0} available"
        : "Choose a stylesheet or code document.";

    public string AuthoringSearchText
    {
        get => _authoringSearchText;
        set
        {
            if (SetProperty(ref _authoringSearchText, value ?? string.Empty))
            {
                RefreshAuthoringLibrary();
            }
        }
    }

    public string SelectedAuthoringCategory
    {
        get => _selectedAuthoringCategory;
        set
        {
            if (SetProperty(
                    ref _selectedAuthoringCategory,
                    string.IsNullOrWhiteSpace(value) ? "All categories" : value))
            {
                RefreshAuthoringLibrary();
            }
        }
    }

    public HyperpageAuthoringRecipe? SelectedAuthoringRecipe
    {
        get => _selectedAuthoringRecipe;
        set
        {
            if (!SetProperty(ref _selectedAuthoringRecipe, value))
            {
                return;
            }
            LoadAuthoringFields();
            OnPropertyChanged(nameof(HasSelectedAuthoringRecipe));
            OnPropertyChanged(nameof(CanInsertAuthoringSnippet));
            InsertAuthoringSnippetCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedAuthoringRecipe => SelectedAuthoringRecipe is not null;
    public string AuthoringPreview
    {
        get => _authoringPreview;
        private set => SetProperty(ref _authoringPreview, value);
    }
    public bool CanInsertAuthoringSnippet => HasActiveAuthoringDocument
                                             && ActiveProjectDocument?.IsReadOnly != true
                                             && SelectedAuthoringRecipe is not null
                                             && AuthoringFields.All(entry =>
                                                 !entry.IsRequired
                                                 || !string.IsNullOrWhiteSpace(
                                                     entry.Value));

    public VisualPageBreakpoint ActiveBreakpoint
    {
        get => _activeBreakpoint;
        set
        {
            if (SetProperty(ref _activeBreakpoint, value))
            {
                LoadLayoutEditor();
                InvalidateCanvas();
            }
        }
    }

    public double CanvasZoomPercent
    {
        get => _canvasZoomPercent;
        set
        {
            var normalized = Math.Clamp(Math.Round(value / 5d) * 5d, 25, 400);
            if (SetProperty(ref _canvasZoomPercent, normalized))
            {
                OnPropertyChanged(nameof(CanvasZoomLabel));
                InvalidateCanvas();
            }
        }
    }

    public string CanvasZoomLabel =>
        $"{CanvasZoomPercent.ToString("0", CultureInfo.InvariantCulture)}%";

    public string CanvasViewportWidth
    {
        get => _canvasViewportWidth;
        set
        {
            if (SetProperty(ref _canvasViewportWidth, value ?? "Auto"))
            {
                InvalidateCanvas();
            }
        }
    }

    public bool ShowCanvasGrid
    {
        get => _showCanvasGrid;
        set
        {
            if (SetProperty(ref _showCanvasGrid, value))
            {
                InvalidateCanvas();
            }
        }
    }

    public bool ShowCanvasOutlines
    {
        get => _showCanvasOutlines;
        set
        {
            if (SetProperty(ref _showCanvasOutlines, value))
            {
                InvalidateCanvas();
            }
        }
    }

    public void AdjustCanvasZoom(double delta) =>
        CanvasZoomPercent += delta;

    public void ExecuteCanvasCommand(string command)
    {
        var target = command switch
        {
            "undo" => UndoCommand,
            "redo" => RedoCommand,
            "duplicate" => DuplicateCommand,
            "delete" => DeleteCommand,
            "move-up" => MoveUpCommand,
            "move-down" => MoveDownCommand,
            "select-parent" => SelectParentCommand,
            _ => null
        };
        if (target?.CanExecute(null) == true)
        {
            target.Execute(null);
        }
    }

    public VisualPageElement? SelectedElement
    {
        get => _selectedElement;
        private set
        {
            DeactivateCatalogElementDraft(loadEditors: false);
            if (SetProperty(ref _selectedElement, value))
            {
                OnPropertyChanged(nameof(HasSelectedElement));
                OnPropertyChanged(nameof(SelectedElementSummary));
                OnPropertyChanged(nameof(SelectedElementDescription));
                OnPropertyChanged(nameof(SelectedElementMarkup));
                OnPropertyChanged(nameof(SelectedElementDescriptor));
                OnPropertyChanged(nameof(SelectedElementBadge));
                OnPropertyChanged(nameof(SelectedElementPlacementGuidance));
                OnPropertyChanged(nameof(SelectedElementChildrenGuidance));
                OnPropertyChanged(nameof(SelectedElementCanvasGuidance));
                OnPropertyChanged(nameof(SelectedElementWarning));
                OnPropertyChanged(nameof(HasSelectedElementWarning));
                OnPropertyChanged(nameof(SelectedElementTextEditorLabel));
                OnPropertyChanged(nameof(SelectedElementSupportsText));
                OnPropertyChanged(nameof(SelectedElementDoesNotSupportText));
                OnPropertyChanged(nameof(SelectedElementSupportsBindings));
                OnPropertyChanged(nameof(SelectedElementSupportsEvents));
                OnPropertyChanged(nameof(SelectedElementAllowsChildren));
                OnPropertyChanged(nameof(SelectedElementSuggestedChildren));
                RaiseInspectorElementPropertiesChanged();
                OnPropertyChanged(nameof(PasteActionText));
                OnPropertyChanged(nameof(CanAddSelectedDescriptor));
                OnPropertyChanged(nameof(AddElementHelp));
                OnPropertyChanged(nameof(AddElementButtonText));
                TemplateNameEditor = value?.Name ?? string.Empty;
                TemplateDescriptionEditor = value is null
                    ? string.Empty
                    : $"Reusable template created from {value.Name}.";
                LoadInspectorEditors();
                RefreshCommandState();
                UseAssetCommand.RaiseCanExecuteChanged();
                InvalidateCanvas();
            }
        }
    }

    public bool HasSelectedElement => SelectedElement is not null;
    public VisualPageComponentDescriptor? SelectedElementDescriptor =>
        SelectedElement is null
            ? null
            : _catalog.GetRequired(SelectedElement.DescriptorId);
    public string SelectedElementSummary => SelectedElement is null
        ? "No element selected"
        : $"{SelectedElement.Name} · {SelectedElement.DescriptorId} · {SelectedElement.ElementId[..Math.Min(8, SelectedElement.ElementId.Length)]}";
    public string SelectedElementDescription => SelectedElement is null
        ? "Select an element to inspect its descriptor and properties."
        : _catalog.GetRequired(SelectedElement.DescriptorId).Description;
    public string SelectedElementMarkup => SelectedElement is null
        ? string.Empty
        : $"<{_catalog.GetRequired(SelectedElement.DescriptorId).MarkupName}>";
    public string SelectedElementBadge => SelectedElementAuthoring?.Badge
                                          ?? string.Empty;
    public string SelectedElementPlacementGuidance =>
        SelectedElementAuthoring?.PlacementGuidance
        ?? "Select an element to see where it belongs.";
    public string SelectedElementChildrenGuidance =>
        SelectedElementAuthoring?.ChildrenGuidance
        ?? "Select an element to see what it can contain.";
    public string SelectedElementCanvasGuidance =>
        SelectedElementAuthoring?.CanvasGuidance
        ?? "Select an element to see how it appears on the canvas.";
    public string SelectedElementWarning => SelectedElementAuthoring?.Warning
                                            ?? string.Empty;
    public bool HasSelectedElementWarning =>
        !string.IsNullOrWhiteSpace(SelectedElementWarning);
    public string SelectedElementTextEditorLabel =>
        SelectedElementAuthoring?.TextEditorLabel ?? "Text content";
    public bool SelectedElementSupportsText =>
        SelectedElementAuthoring?.TextEditorKind is not null
            and not VisualPageElementTextEditorKind.None;
    public bool SelectedElementDoesNotSupportText =>
        HasSelectedElement && !SelectedElementSupportsText;
    public bool SelectedElementSupportsBindings =>
        SelectedElementAuthoring?.SupportsBindings == true;
    public bool SelectedElementSupportsEvents =>
        SelectedElementAuthoring?.SupportsEvents == true;
    public bool SelectedElementAllowsChildren =>
        SelectedElementDescriptor?.AllowsChildren == true;
    public string PasteActionText => SelectedElementAllowsChildren
        ? "Paste inside selection"
        : "Paste after selection";
    public IReadOnlyList<VisualPageComponentDescriptor>
        SelectedElementSuggestedChildren => SelectedElementDescriptor is null
            ? []
            : SelectedElementDescriptor.Authoring.SuggestedChildDescriptorIds
                .Select(id => _catalog.TryGet(id, out var descriptor)
                    ? descriptor
                    : null)
                .OfType<VisualPageComponentDescriptor>()
                .ToList();
    private VisualPageElementAuthoringDescriptor? SelectedElementAuthoring =>
        SelectedElement is null
            ? null
            : _catalog.GetRequired(SelectedElement.DescriptorId).Authoring;
    private VisualPageElement? InspectorElement => IsCatalogElementDraftActive
        ? _catalogElementDraft
        : SelectedElement;
    private VisualPageComponentDescriptor? InspectorElementDescriptor =>
        InspectorElement is null
            ? null
            : _catalog.GetRequired(InspectorElement.DescriptorId);
    private VisualPageElementAuthoringDescriptor? InspectorElementAuthoring =>
        InspectorElementDescriptor?.Authoring;
    public bool IsCatalogElementDraftActive =>
        _isCatalogElementDraftActive && _catalogElementDraft is not null;
    public bool IsPlacedElementInspector =>
        !IsCatalogElementDraftActive && SelectedElement is not null;
    public string InspectorElementTitle => InspectorElement?.Name
                                           ?? "No element selected";
    public string InspectorElementSummary => InspectorElement is null
        ? "Choose a catalog element or select an element on the page."
        : IsCatalogElementDraftActive
            ? $"Configure <{InspectorElementDescriptor!.MarkupName}> before adding it to the current selection."
            : SelectedElementSummary;
    public string InspectorElementDescription => InspectorElementDescriptor?.Description
                                                  ?? "Choose an element to inspect its properties.";
    public string InspectorElementMarkup => BuildInspectorMarkupPreview();
    public string InspectorElementBadge => InspectorElementAuthoring?.Badge
                                            ?? string.Empty;
    public string InspectorElementPlacementGuidance =>
        InspectorElementAuthoring?.PlacementGuidance
        ?? "Choose an element to see where it belongs.";
    public string InspectorElementChildrenGuidance =>
        InspectorElementAuthoring?.ChildrenGuidance
        ?? "Choose an element to see what it can contain.";
    public string InspectorElementCanvasGuidance =>
        InspectorElementAuthoring?.CanvasGuidance
        ?? "Choose an element to see how it appears on the canvas.";
    public string InspectorElementWarning => InspectorElementAuthoring?.Warning
                                             ?? string.Empty;
    public bool HasInspectorElementWarning =>
        !string.IsNullOrWhiteSpace(InspectorElementWarning);
    public string InspectorElementTextEditorLabel =>
        InspectorElementAuthoring?.TextEditorLabel ?? "Text content";
    public bool InspectorElementSupportsText =>
        InspectorElementAuthoring?.TextEditorKind is not null
            and not VisualPageElementTextEditorKind.None;
    public bool InspectorElementDoesNotSupportText =>
        InspectorElement is not null && !InspectorElementSupportsText;
    public string ElementCatalogSummary =>
        $"{FilteredElementDescriptors.Count} shown · "
        + $"{_catalog.StandardHtmlTags.Count} current HTML tags · "
        + $"{_catalog.ObsoleteHtmlTags.Count} legacy tags";
    public string SelectedDescriptorSummary => SelectedDescriptor is null
        ? "Choose an element."
        : $"<{SelectedDescriptor.MarkupName}> · {SelectedDescriptor.Description}";
    public string SelectedDescriptorBadge =>
        SelectedDescriptor?.Authoring.Badge ?? string.Empty;
    public string SelectedDescriptorCategorySummary => SelectedDescriptor is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(SelectedDescriptorBadge)
            ? SelectedDescriptor.Category
            : $"{SelectedDescriptor.Category} · {SelectedDescriptorBadge}";
    public IReadOnlyList<VisualPageAttributeDescriptor> SelectedDescriptorAttributes =>
        SelectedDescriptor is null
            ? []
            : _catalog.GetAttributeDefinitions(SelectedDescriptor.Id);
    public string SelectedDescriptorPlacement =>
        SelectedDescriptor?.Authoring.PlacementGuidance ?? string.Empty;
    public string SelectedDescriptorChildren =>
        SelectedDescriptor?.Authoring.ChildrenGuidance ?? string.Empty;
    public string SelectedDescriptorCanvas =>
        SelectedDescriptor?.Authoring.CanvasGuidance ?? string.Empty;
    public string SelectedDescriptorWarning =>
        SelectedDescriptor?.Authoring.Warning ?? string.Empty;
    public bool HasSelectedDescriptorWarning =>
        !string.IsNullOrWhiteSpace(SelectedDescriptorWarning);
    public string SelectedDescriptorStarterSummary
    {
        get
        {
            if (SelectedDescriptor is null)
            {
                return string.Empty;
            }
            var starters = SelectedDescriptor.Authoring.StarterChildren;
            return starters.Count == 0
                ? "Creates one empty or text-ready element."
                : "Ready-made contents: " + string.Join(
                    ", ",
                    starters.Select(starter =>
                        _catalog.GetRequired(starter.DescriptorId).DisplayName)) + ".";
        }
    }
    public bool UsesUnifiedAuthoringCatalog => HasHyperlibAuthoring && !IsHyperlibEditor;
    public string AddElementButtonText => UsesUnifiedAuthoringCatalog ? "Add to Page" : "Add";
    public bool CanAddSelectedDescriptor => SelectedDescriptor is not null
                                            && (UsesUnifiedAuthoringCatalog || CanAddDescriptorToSelected(
                                                SelectedDescriptor.Id));
    public string AddElementHelp => SelectedDescriptor is null
        ? "Choose an element from the list first."
        : CanAddSelectedDescriptor
            ? "Adds a ready-to-edit object at the current selection."
            : SelectedDescriptor.Authoring.PlacementGuidance;

    public VisualPageComponentDescriptor? SelectedDescriptor
    {
        get => _selectedDescriptor;
        set
        {
            if (SetProperty(ref _selectedDescriptor, value))
            {
                OnPropertyChanged(nameof(SelectedDescriptorSummary));
                OnPropertyChanged(nameof(SelectedDescriptorBadge));
                OnPropertyChanged(nameof(SelectedDescriptorCategorySummary));
                OnPropertyChanged(nameof(SelectedDescriptorAttributes));
                OnPropertyChanged(nameof(SelectedDescriptorPlacement));
                OnPropertyChanged(nameof(SelectedDescriptorChildren));
                OnPropertyChanged(nameof(SelectedDescriptorCanvas));
                OnPropertyChanged(nameof(SelectedDescriptorWarning));
                OnPropertyChanged(nameof(HasSelectedDescriptorWarning));
                OnPropertyChanged(nameof(SelectedDescriptorStarterSummary));
                OnPropertyChanged(nameof(CanAddSelectedDescriptor));
                OnPropertyChanged(nameof(AddElementHelp));
                OnPropertyChanged(nameof(AddElementButtonText));
                (AddElementCommand as RelayCommand)?.RaiseCanExecuteChanged();
                AddDescriptorCommand?.RaiseCanExecuteChanged();
                if (_isCatalogDraftActivationReady)
                {
                    if (UsesUnifiedAuthoringCatalog) CatalogElementPreviewRequested?.Invoke(value?.Id);
                    else ActivateCatalogElementDraft(value);
                }
            }
        }
    }

    public string ElementSearchText
    {
        get => _elementSearchText;
        set
        {
            if (SetProperty(ref _elementSearchText, value ?? string.Empty))
            {
                RefreshElementCatalog();
            }
        }
    }

    public string SelectedElementScope
    {
        get => _selectedElementScope;
        set
        {
            if (SetProperty(ref _selectedElementScope, value ?? "Common"))
            {
                _selectedElementCategory = "All categories";
                OnPropertyChanged(nameof(SelectedElementCategory));
                RefreshElementCatalog();
            }
        }
    }

    public string SelectedElementCategory
    {
        get => _selectedElementCategory;
        set
        {
            // Replacing the dropdown's items temporarily clears its two-way selection.
            // Keep the user's category while the catalog rebuilds those items.
            if (_isRefreshingElementCatalog) return;
            if (SetProperty(ref _selectedElementCategory, value ?? "All categories"))
            {
                RefreshElementCatalog();
            }
        }
    }

    public VisualPageAttributeDescriptor? SelectedAttributeDefinition
    {
        get => _selectedAttributeDefinition;
        set
        {
            if (!SetProperty(ref _selectedAttributeDefinition, value))
            {
                return;
            }
            if (value is not null)
            {
                AttributeName = value.Name;
                AttributeValue = InspectorElement?.Attributes.GetValueOrDefault(value.Name)
                    ?? (value.EditorKind == VisualPageAttributeEditorKind.Boolean
                        ? "true"
                        : value.SuggestedValues.FirstOrDefault() ?? string.Empty);
            }
            OnPropertyChanged(nameof(AttributeValueSuggestions));
            OnPropertyChanged(nameof(SelectedAttributeHelp));
            OnPropertyChanged(nameof(UsesBooleanAttributeEditor));
            OnPropertyChanged(nameof(UsesChoiceAttributeEditor));
            OnPropertyChanged(nameof(UsesTextAttributeEditor));
            OnPropertyChanged(nameof(AttributeChoiceIsEditable));
            OnPropertyChanged(nameof(AttributeEditorKindLabel));
            OnPropertyChanged(nameof(AttributeBooleanValue));
        }
    }

    public IReadOnlyList<string> AttributeValueSuggestions
    {
        get
        {
            if (SelectedAttributeDefinition is null)
            {
                return [];
            }
            var suggestions = SelectedAttributeDefinition.SuggestedValues;
            if (SelectedAttributeDefinition.EditorKind !=
                VisualPageAttributeEditorKind.IdReference)
            {
                return suggestions;
            }
            return suggestions.Concat(
                    EnumerateElements(_session.Page.Root)
                        .Select(element => element.Attributes.GetValueOrDefault("id"))
                        .OfType<string>()
                        .Where(value => !string.IsNullOrWhiteSpace(value)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }
    }
    public string SelectedAttributeHelp => SelectedAttributeDefinition is null
        ? "Choose a supported element or global attribute. Custom data-* and aria-* names remain available."
        : SelectedAttributeDefinition.Description;
    public bool UsesBooleanAttributeEditor =>
        SelectedAttributeDefinition?.EditorKind ==
        VisualPageAttributeEditorKind.Boolean;
    public bool UsesChoiceAttributeEditor =>
        SelectedAttributeDefinition?.EditorKind ==
        VisualPageAttributeEditorKind.Choice;
    public bool UsesTextAttributeEditor =>
        SelectedAttributeDefinition is null
        || SelectedAttributeDefinition.EditorKind is not
            (VisualPageAttributeEditorKind.Boolean or
             VisualPageAttributeEditorKind.Choice);
    public bool AttributeChoiceIsEditable =>
        SelectedAttributeDefinition?.RestrictToSuggestedValues != true;
    public string AttributeEditorKindLabel =>
        SelectedAttributeDefinition?.EditorKind switch
        {
            VisualPageAttributeEditorKind.Boolean => "On or off",
            VisualPageAttributeEditorKind.Number => "Number",
            VisualPageAttributeEditorKind.Url => "Web or project address",
            VisualPageAttributeEditorKind.Choice => "Choose a value",
            VisualPageAttributeEditorKind.TokenList => "Space-separated values",
            VisualPageAttributeEditorKind.DateTime => "Date or time",
            VisualPageAttributeEditorKind.Color => "Color",
            VisualPageAttributeEditorKind.IdReference => "Element ID",
            _ => "Value"
        };
    public bool AttributeBooleanValue
    {
        get => !AttributeValue.Equals("false", StringComparison.OrdinalIgnoreCase);
        set
        {
            AttributeValue = value ? "true" : "false";
            OnPropertyChanged();
        }
    }

    public VisualPagePropertyEntry? SelectedAttributeEntry
    {
        get => _selectedAttributeEntry;
        set
        {
            if (!SetProperty(ref _selectedAttributeEntry, value) || value is null)
            {
                return;
            }
            AttributeName = value.Name;
            AttributeValue = value.Value;
            SelectedAttributeDefinition = AvailableAttributes.FirstOrDefault(candidate =>
                candidate.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public VisualPagePropertyEntry? SelectedStyleEntry
    {
        get => _selectedStyleEntry;
        set
        {
            if (!SetProperty(ref _selectedStyleEntry, value) || value is null)
            {
                return;
            }
            StyleName = value.Name;
            StyleValue = value.Value;
        }
    }

    public VisualPageTemplateDocument? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                if (value?.Scope == "page")
                {
                    TemplatePageName = string.IsNullOrWhiteSpace(
                        value.SuggestedPageName)
                        ? value.Name
                        : value.SuggestedPageName;
                    TemplatePageRoute = string.IsNullOrWhiteSpace(
                        value.SuggestedPageRoute)
                        ? $"/archive/{VisualPageProjectService.MakeSlug(value.Name)}"
                        : value.SuggestedPageRoute;
                }
                OnPropertyChanged(nameof(IsSelectedTemplatePage));
                OnPropertyChanged(nameof(HasSelectedTemplate));
                OnPropertyChanged(nameof(SelectedTemplateActionText));
                OnPropertyChanged(nameof(SelectedTemplateUsageText));
                OnPropertyChanged(nameof(SelectedTemplateProperties));
                OnPropertyChanged(nameof(SelectedTemplateElements));
                OnPropertyChanged(nameof(SelectedTemplateElementsText));
                OnPropertyChanged(nameof(SelectedTemplateStructure));
                (InsertTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string TemplateSearchText
    {
        get => _templateSearchText;
        set
        {
            if (SetProperty(ref _templateSearchText, value ?? string.Empty))
            {
                RefreshTemplateFilter();
            }
        }
    }

    public string SelectedTemplateScopeFilter
    {
        get => _selectedTemplateScopeFilter;
        set
        {
            if (SetProperty(ref _selectedTemplateScopeFilter, value ?? "All types"))
            {
                RefreshTemplateFilter();
            }
        }
    }

    public string SelectedTemplateLibraryFilter
    {
        get => _selectedTemplateLibraryFilter;
        set
        {
            if (SetProperty(ref _selectedTemplateLibraryFilter, value ?? "All libraries"))
            {
                RefreshTemplateFilter();
            }
        }
    }

    public string SelectedTemplateCategoryFilter
    {
        get => _selectedTemplateCategoryFilter;
        set
        {
            if (SetProperty(
                    ref _selectedTemplateCategoryFilter,
                    value ?? VisualPageTemplateTaxonomy.AllCategories))
            {
                RefreshTemplateFilter();
            }
        }
    }

    public string TemplateNameEditor
    {
        get => _templateNameEditor;
        set
        {
            if (SetProperty(ref _templateNameEditor, value ?? string.Empty))
            {
                (ConvertToTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ConvertToPersonalTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string TemplateDescriptionEditor
    {
        get => _templateDescriptionEditor;
        set => SetProperty(ref _templateDescriptionEditor, value ?? string.Empty);
    }

    public string TemplatePageName
    {
        get => _templatePageName;
        set
        {
            if (SetProperty(ref _templatePageName, value ?? string.Empty))
            {
                (InsertTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string TemplatePageRoute
    {
        get => _templatePageRoute;
        set
        {
            if (SetProperty(ref _templatePageRoute, value ?? string.Empty))
            {
                (InsertTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string WebTemplateUrl
    {
        get => _webTemplateUrl;
        set
        {
            if (SetProperty(ref _webTemplateUrl, value ?? string.Empty))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string WebTemplateName
    {
        get => _webTemplateName;
        set
        {
            if (SetProperty(ref _webTemplateName, value ?? string.Empty))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string WebTemplateDescription
    {
        get => _webTemplateDescription;
        set
        {
            if (SetProperty(ref _webTemplateDescription, value ?? string.Empty))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string WebTemplateScope
    {
        get => _webTemplateScope;
        set
        {
            if (SetProperty(ref _webTemplateScope, value ?? "page"))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string WebTemplateCategoryGroup
    {
        get => _webTemplateCategoryGroup;
        set
        {
            if (SetProperty(
                    ref _webTemplateCategoryGroup,
                    value ?? "Navigation"))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string WebTemplateCategory
    {
        get => _webTemplateCategory;
        set
        {
            if (SetProperty(ref _webTemplateCategory, value ?? string.Empty))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool WebTemplatePermissionConfirmed
    {
        get => _webTemplatePermissionConfirmed;
        set
        {
            if (SetProperty(ref _webTemplatePermissionConfirmed, value))
            {
                CaptureWebTemplateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSelectedTemplatePage => SelectedTemplate?.Scope == "page";
    public bool HasSelectedTemplate => SelectedTemplate is not null;
    public string SelectedTemplateActionText => "Insert";
    public string SelectedTemplateUsageText => SelectedTemplate is null
        ? "Choose a page, section, or reusable element."
        : string.IsNullOrWhiteSpace(SelectedTemplate.UsageHint)
            ? SelectedTemplate.Description
            : SelectedTemplate.UsageHint;
    public string SelectedTemplateProperties => SelectedTemplate is null
        ? string.Empty
        : $"{SelectedTemplate.ScopeLabel} · {SelectedTemplate.CategoryGroupLabel} / {SelectedTemplate.Category} · {SelectedTemplate.StorageLabel}";
    public IReadOnlyList<string> SelectedTemplateElements =>
        SelectedTemplate is null
            ? []
            : EnumerateElements(SelectedTemplate.Root)
                .Select(TemplateElementLabel)
                .ToList();
    public string SelectedTemplateElementsText =>
        string.Join(Environment.NewLine, SelectedTemplateElements);
    public string SelectedTemplateStructure => SelectedTemplate is null
        ? string.Empty
        : BuildTemplateStructure(SelectedTemplate.Root);
    public string TemplateCatalogSummary =>
        $"{FilteredTemplates.Count} shown · {Templates.Count(template => template.Scope == "page")} page starters · "
        + $"{Templates.Count(template => template.Scope == "section")} sections · "
        + $"{Templates.Count(template => template.Scope == "element")} elements";

    public VisualPageStudioToolDefinition? SelectedTool
    {
        get => _selectedTool;
        set => SetProperty(ref _selectedTool, value);
    }

    public VisualPageProjectPage? SelectedProjectPage
    {
        get => _selectedProjectPage;
        set => SetProperty(ref _selectedProjectPage, value);
    }

    public VisualPageAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (SetProperty(ref _selectedAsset, value))
            {
                AssetAltText = value?.AltText ?? string.Empty;
                ApplyAssetAltTextCommand.RaiseCanExecuteChanged();
                UseAssetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public VisualPageDiagnostic? SelectedDiagnostic
    {
        get => _selectedDiagnostic;
        set
        {
            if (!SetProperty(ref _selectedDiagnostic, value) || value is null)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(value.ElementId))
            {
                SelectElement(value.ElementId);
            }
            if (!string.IsNullOrWhiteSpace(value.RelativePath))
            {
                RequestPaneContent("build-output");
            }
            StatusMessage = value.Summary;
        }
    }

    public string PageNameEditor
    {
        get => _pageNameEditor;
        set => SetProperty(ref _pageNameEditor, value ?? string.Empty);
    }

    public string PageRouteEditor
    {
        get => _pageRouteEditor;
        set => SetProperty(ref _pageRouteEditor, value ?? string.Empty);
    }

    public string PageSlugEditor
    {
        get => _pageSlugEditor;
        set => SetProperty(ref _pageSlugEditor, value ?? string.Empty);
    }

    public string PageTitleEditor
    {
        get => _pageTitleEditor;
        set => SetProperty(ref _pageTitleEditor, value ?? string.Empty);
    }

    public string PageDescriptionEditor
    {
        get => _pageDescriptionEditor;
        set => SetProperty(ref _pageDescriptionEditor, value ?? string.Empty);
    }

    public string NewPageName
    {
        get => _newPageName;
        set => SetProperty(ref _newPageName, value ?? string.Empty);
    }

    public string NewPageRoute
    {
        get => _newPageRoute;
        set => SetProperty(ref _newPageRoute, value ?? string.Empty);
    }

    public string AssetAltText
    {
        get => _assetAltText;
        set => SetProperty(ref _assetAltText, value ?? string.Empty);
    }

    public StudioLayoutSnapshot? PersonalDefaultLayout =>
        _personalDefaultLayout;

    public StudioLayoutSnapshot? SelectedLayoutPreset
    {
        get => _selectedLayoutPreset;
        set
        {
            if (SetProperty(ref _selectedLayoutPreset, value))
            {
                ApplyLayoutPresetCommand.RaiseCanExecuteChanged();
                DeleteLayoutPresetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LayoutPresetName
    {
        get => _layoutPresetName;
        set => SetProperty(ref _layoutPresetName, value ?? string.Empty);
    }

    public string SelectedTextEditor
    {
        get => _selectedTextEditor;
        set => SetProperty(ref _selectedTextEditor, value ?? string.Empty);
    }

    public string AttributeName
    {
        get => _attributeName;
        set
        {
            if (SetProperty(ref _attributeName, value?.Trim() ?? string.Empty))
            {
                (SetAttributeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RemoveAttributeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string AttributeValue
    {
        get => _attributeValue;
        set
        {
            if (SetProperty(ref _attributeValue, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(AttributeBooleanValue));
            }
        }
    }

    public string StyleName
    {
        get => _styleName;
        set
        {
            if (SetProperty(ref _styleName, value?.Trim() ?? string.Empty))
            {
                (SetStyleCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RemoveStyleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string StyleValue
    {
        get => _styleValue;
        set
        {
            if (SetProperty(ref _styleValue, value ?? string.Empty))
            {
                (SetStyleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string LayoutDisplay
    {
        get => _layoutDisplay;
        set => SetProperty(ref _layoutDisplay, value ?? string.Empty);
    }

    public string LayoutDirection
    {
        get => _layoutDirection;
        set => SetProperty(ref _layoutDirection, value ?? string.Empty);
    }

    public string LayoutWrap
    {
        get => _layoutWrap;
        set => SetProperty(ref _layoutWrap, value ?? string.Empty);
    }

    public string LayoutColumns
    {
        get => _layoutColumns;
        set => SetProperty(ref _layoutColumns, value ?? string.Empty);
    }

    public string LayoutGap
    {
        get => _layoutGap;
        set => SetProperty(ref _layoutGap, value ?? string.Empty);
    }

    public string LayoutPadding
    {
        get => _layoutPadding;
        set => SetProperty(ref _layoutPadding, value ?? string.Empty);
    }

    public string LayoutMargin
    {
        get => _layoutMargin;
        set => SetProperty(ref _layoutMargin, value ?? string.Empty);
    }

    public string LayoutFlexBasis
    {
        get => _layoutFlexBasis;
        set => SetProperty(ref _layoutFlexBasis, value ?? string.Empty);
    }

    public string LayoutColumnSpan
    {
        get => _layoutColumnSpan;
        set => SetProperty(ref _layoutColumnSpan, value ?? string.Empty);
    }

    public string LayoutRowSpan
    {
        get => _layoutRowSpan;
        set => SetProperty(ref _layoutRowSpan, value ?? string.Empty);
    }

    public string LayoutMinWidth
    {
        get => _layoutMinWidth;
        set => SetProperty(ref _layoutMinWidth, value ?? string.Empty);
    }

    public string LayoutMaxWidth
    {
        get => _layoutMaxWidth;
        set => SetProperty(ref _layoutMaxWidth, value ?? string.Empty);
    }

    public string LayoutMinHeight
    {
        get => _layoutMinHeight;
        set => SetProperty(ref _layoutMinHeight, value ?? string.Empty);
    }

    public string LayoutMaxHeight
    {
        get => _layoutMaxHeight;
        set => SetProperty(ref _layoutMaxHeight, value ?? string.Empty);
    }

    public string LayoutAlignItems
    {
        get => _layoutAlignItems;
        set => SetProperty(ref _layoutAlignItems, value ?? string.Empty);
    }

    public string LayoutJustifyContent
    {
        get => _layoutJustifyContent;
        set => SetProperty(ref _layoutJustifyContent, value ?? string.Empty);
    }

    public string LayoutAlignSelf
    {
        get => _layoutAlignSelf;
        set => SetProperty(ref _layoutAlignSelf, value ?? string.Empty);
    }

    public string StateName
    {
        get => _stateName;
        set => SetProperty(ref _stateName, value?.Trim() ?? string.Empty);
    }

    public string StateType
    {
        get => _stateType;
        set => SetProperty(ref _stateType, value?.Trim() ?? string.Empty);
    }

    public string StateInitialValue
    {
        get => _stateInitialValue;
        set => SetProperty(ref _stateInitialValue, value ?? string.Empty);
    }

    public VisualPageStateKind StateKind
    {
        get => _stateKind;
        set => SetProperty(ref _stateKind, value);
    }

    public VisualPageEventKind EventKind
    {
        get => _eventKind;
        set => SetProperty(ref _eventKind, value);
    }

    public string EventHandlerName
    {
        get => _eventHandlerName;
        set => SetProperty(ref _eventHandlerName, value?.Trim() ?? string.Empty);
    }

    public string GeneratedRazor
    {
        get => _generatedRazor;
        private set => SetProperty(ref _generatedRazor, value);
    }

    public string GeneratedCss
    {
        get => _generatedCss;
        private set => SetProperty(ref _generatedCss, value);
    }

    public string GeneratedCSharp
    {
        get => _generatedCSharp;
        private set => SetProperty(ref _generatedCSharp, value);
    }

    public string UserCSharp
    {
        get => _userCSharp;
        set
        {
            if (SetProperty(ref _userCSharp, value ?? string.Empty))
            {
                NotifyDirtyState();
            }
        }
    }

    public string UserCss
    {
        get => _userCss;
        set
        {
            if (SetProperty(ref _userCss, value ?? string.Empty))
            {
                NotifyDirtyState();
                InvalidateCanvas();
            }
        }
    }

    public string UserJavaScript
    {
        get => _userJavaScript;
        set
        {
            if (SetProperty(ref _userJavaScript, value ?? string.Empty))
            {
                NotifyDirtyState();
            }
        }
    }

    public string BuildOutput
    {
        get => _buildOutput;
        private set => SetProperty(ref _buildOutput, value);
    }

    public bool AllowLanHosting
    {
        get => _allowLanHosting;
        set
        {
            if (SetProperty(ref _allowLanHosting, value) && !value)
            {
                AdapterAddress = "127.0.0.1";
            }
        }
    }

    public bool TrustCustomCode
    {
        get => _trustCustomCode;
        set => SetProperty(ref _trustCustomCode, value);
    }

    public string AdapterAddress
    {
        get => _adapterAddress;
        set => SetProperty(ref _adapterAddress, value?.Trim() ?? string.Empty);
    }

    public string HostPortText
    {
        get => _hostPortText;
        set => SetProperty(ref _hostPortText, value?.Trim() ?? "0");
    }

    public string RuntimePreviewUrl
    {
        get => _runtimePreviewUrl;
        private set
        {
            if (SetProperty(ref _runtimePreviewUrl, value))
            {
                OnPropertyChanged(nameof(HasRuntimePreview));
            }
        }
    }

    public bool HasRuntimePreview => !string.IsNullOrWhiteSpace(RuntimePreviewUrl);

    public string AtlasCheckoutPath
    {
        get => _atlasCheckoutPath;
        set
        {
            if (SetProperty(ref _atlasCheckoutPath, value?.Trim() ?? string.Empty))
            {
                ClearPublicationPreparation();
            }
        }
    }

    public string ArchiveSlug
    {
        get => _archiveSlug;
        set
        {
            if (SetProperty(ref _archiveSlug, value?.Trim().ToLowerInvariant() ?? string.Empty))
            {
                ClearPublicationPreparation();
            }
        }
    }

    public string PublishTargetBranch
    {
        get => _publishTargetBranch;
        set
        {
            if (SetProperty(ref _publishTargetBranch, value?.Trim() ?? string.Empty))
            {
                ClearPublicationPreparation();
            }
        }
    }

    public string PublicationReview
    {
        get => _publicationReview;
        private set => SetProperty(ref _publicationReview, value);
    }

    public string PublishedCommit
    {
        get => _publishedCommit;
        private set => SetProperty(ref _publishedCommit, value);
    }

    public bool ApproveReviewedCode
    {
        get => _approveReviewedCode;
        set => SetProperty(ref _approveReviewedCode, value);
    }

    public bool IsPublicationPrepared => _publicationPreparation is not null;
    public bool RequiresReviewedCode =>
        _publicationPreparation?.RequiresReviewedCode == true;

    public void SelectElement(string? elementId)
    {
        DeactivateCatalogElementDraft(loadEditors: true);
        SelectedElement = string.IsNullOrWhiteSpace(elementId)
            ? null
            : Find(_session.Page.Root, elementId);
    }

    public bool ApplyMutation(
        VisualPageMutationSet mutationSet,
        bool recordUndo = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        try
        {
            ApplyMutationCore(mutationSet, recordUndo);
            return true;
        }
        catch (Exception exception) when (exception is
            VisualPageValidationException or VisualPageStaleRevisionException)
        {
            StatusMessage = exception.Message;
            return false;
        }
    }

    public VisualPageMutationResult PreviewMutationForAi(
        VisualPageMutationSet mutationSet)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _mutationEngine.Apply(
            VisualPageJson.Clone(_session.Page),
            mutationSet);
    }

    public VisualPageMutationResult ApplyMutationForAi(
        VisualPageMutationSet mutationSet) =>
        ApplyMutationCore(mutationSet, recordUndo: true);

    private VisualPageMutationResult ApplyMutationCore(
        VisualPageMutationSet mutationSet,
        bool recordUndo)
    {
        var before = VisualPageJson.Clone(_session.Page);
        var result = _mutationEngine.Apply(_session.Page, mutationSet);
        if (recordUndo)
        {
            _undo.Add(before);
            if (_undo.Count > MaximumUndoEntries)
            {
                _undo.RemoveAt(0);
            }
            _redo.Clear();
        }
        _session.Page = result.Document;
        RefreshDocumentState(result.Changes.LastOrDefault()?.ElementId);
        MutationApplied?.Invoke(this, result.Changes);
        StatusMessage = result.Changes.Count == 1
            ? result.Changes[0].Summary
            : $"Applied {result.Changes.Count} page changes.";
        return result;
    }

    public void MoveElement(string elementId, string parentElementId, int index) =>
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.MoveElement,
                TargetElementId = elementId,
                ParentElementId = parentElementId,
                Index = index
            }],
            "canvas",
            "drag/reparent"));

    public void ResizeElement(
        string elementId,
        double deltaX,
        double startWidth)
    {
        var element = Find(_session.Page.Root, elementId);
        if (element is null)
        {
            return;
        }
        var layout = element.Layout.TryGetValue(ActiveBreakpoint, out var active)
            ? VisualPageJson.Clone(active)
            : new VisualPageLayoutSpec();
        var parent = FindParent(_session.Page.Root, elementId);
        var parentLayout = parent is null
            ? null
            : ResolveEffectiveLayout(parent, ActiveBreakpoint);
        if (string.Equals(parentLayout?.Display, "grid", StringComparison.OrdinalIgnoreCase))
        {
            layout.ColumnSpan = Math.Clamp(
                (layout.ColumnSpan ?? 6) + (int)Math.Round(deltaX / 48d),
                1,
                12);
        }
        else
        {
            var current = TryPercent(layout.FlexBasis, out var percentage)
                ? percentage
                : 100d;
            var delta = startWidth <= 0 ? 0 : deltaX / startWidth * current;
            layout.FlexBasis = $"{Math.Clamp(current + delta, 10d, 100d):0.#}%";
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetLayout,
                TargetElementId = elementId,
                Breakpoint = ActiveBreakpoint,
                Layout = layout
            }],
            "canvas",
            "responsive resize"));
    }

    public string CreateAiReadSnapshot()
    {
        var snapshot = new
        {
            projectId = ProjectId,
            pageId = PageId,
            revision = Revision,
            route = Document.Route,
            selectedElementId = SelectedElement?.ElementId,
            document = Document,
            diagnostics = _validator.ValidatePage(Document)
        };
        return System.Text.Json.JsonSerializer.Serialize(
            snapshot,
            VisualPageJson.Options);
    }

    public IReadOnlyList<VisualPageDiagnostic> ValidateActivePage() =>
        _validator.ValidatePage(_session.Page);

    public bool MovePaneContent(
        string contentId,
        string targetNodeId,
        int targetIndex = int.MaxValue)
    {
        var panes = EnumeratePanes(_layout.Root).ToList();
        var source = panes.FirstOrDefault(pane =>
            pane.PaneContents.Contains(contentId, StringComparer.Ordinal));
        var target = panes.FirstOrDefault(pane => string.Equals(
            pane.NodeId,
            targetNodeId,
            StringComparison.Ordinal));
        if (source is null || target is null
            || (ReferenceEquals(source, target)
                && source.PaneContents.Count == 1)
            || (!ReferenceEquals(source, target)
                && source.PaneContents.Count == 1))
        {
            return false;
        }

        source.PaneContents.Remove(contentId);
        target.PaneContents.Remove(contentId);
        target.PaneContents.Insert(
            Math.Clamp(targetIndex, 0, target.PaneContents.Count),
            contentId);
        if (string.Equals(
                source.ActiveContentId,
                contentId,
                StringComparison.Ordinal))
        {
            source.ActiveContentId = source.PaneContents[0];
        }
        target.ActiveContentId = contentId;
        CommitLayoutChange();
        return true;
    }

    public void SelectPaneContent(string nodeId, string contentId)
    {
        var pane = EnumeratePanes(_layout.Root).FirstOrDefault(value =>
            string.Equals(value.NodeId, nodeId, StringComparison.Ordinal));
        if (pane is null
            || !pane.PaneContents.Contains(contentId, StringComparer.Ordinal))
        {
            return;
        }
        pane.ActiveContentId = contentId;
        SetActiveAuthoringContent(contentId);
    }

    public bool ClosePaneContent(string nodeId, string contentId)
    {
        var root = VisualPageJson.Clone(_layout.Root);
        var pane = EnumeratePanes(root).FirstOrDefault(value =>
            string.Equals(value.NodeId, nodeId, StringComparison.Ordinal));
        var index = pane?.PaneContents.FindIndex(value =>
            string.Equals(value, contentId, StringComparison.Ordinal)) ?? -1;
        if (pane is null || index < 0)
        {
            return false;
        }

        pane.PaneContents.RemoveAt(index);
        if (!pane.PaneContents.Contains(pane.ActiveContentId, StringComparer.Ordinal))
        {
            pane.ActiveContentId = pane.PaneContents.Count == 0
                ? string.Empty
                : pane.PaneContents[Math.Min(index, pane.PaneContents.Count - 1)];
        }
        var nextContent = pane.ActiveContentId;
        if (pane.PaneContents.Count == 0)
        {
            root = RemovePane(root, nodeId, out _) ?? new StudioLayoutNode
            {
                NodeId = nodeId,
                Kind = StudioLayoutNodeKind.Pane,
                PaneContents = [],
                ActiveContentId = string.Empty
            };
            nextContent = EnumeratePanes(root)
                .Select(value => value.ActiveContentId)
                .LastOrDefault(value => !string.IsNullOrEmpty(value)) ?? string.Empty;
        }

        // Closing a tab changes its visibility only. Keep its source/document
        // buffer registered so project Save and recovery retain unsaved edits,
        // and reopening the file restores the same draft instead of disk text.
        _layout = CreateLayoutSnapshot(ContainsPaneContent(root, "canvas") ? "Canvas" : "Documents", root);
        if (string.Equals(ActiveAuthoringContentId, contentId, StringComparison.OrdinalIgnoreCase))
        {
            SetActiveAuthoringContent(nextContent);
        }
        CommitLayoutChange();
        StatusMessage = "Closed the tab.";
        return true;
    }

    public void SetSplitProportion(string nodeId, double proportion)
    {
        var split = EnumerateLayoutNodes(_layout.Root).FirstOrDefault(node =>
            node.Kind == StudioLayoutNodeKind.Split
            && string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        if (split is null)
        {
            return;
        }
        split.FirstProportion = Math.Clamp(proportion, 0.15, 0.85);
        CommitLayoutChange();
    }

    public void SetToolPlacement(
        string toolId,
        StudioToolHostKind host,
        StudioFloatingBounds? bounds = null)
    {
        if (!StudioTools.Any(tool => string.Equals(
                tool.Id,
                toolId,
                StringComparison.Ordinal)))
        {
            throw new VisualPageValidationException(
                $"Unknown Studio tool '{toolId}'.");
        }
        var placement = _layout.ToolPlacements.FirstOrDefault(value =>
            string.Equals(value.ToolId, toolId, StringComparison.Ordinal));
        if (placement is null)
        {
            placement = new StudioToolPlacement
            {
                ToolId = toolId,
                Order = _layout.ToolPlacements.Count
            };
            _layout.ToolPlacements.Add(placement);
        }
        placement.Host = host;
        placement.IsVisible = true;
        placement.FloatingBounds = bounds is null
            ? null
            : VisualPageJson.Clone(bounds);
        CommitLayoutChange();
    }

    public void UpdateFloatingToolBounds(
        string toolId,
        StudioFloatingBounds bounds)
    {
        var placement = _layout.ToolPlacements.FirstOrDefault(value =>
            string.Equals(value.ToolId, toolId, StringComparison.Ordinal));
        if (placement is null
            || placement.Host != StudioToolHostKind.Floating
            || !double.IsFinite(bounds.Left)
            || !double.IsFinite(bounds.Top)
            || !double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height))
        {
            return;
        }
        var normalized = new StudioFloatingBounds
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = Math.Max(240, bounds.Width),
            Height = Math.Max(180, bounds.Height)
        };
        if (placement.FloatingBounds is { } current
            && Math.Abs(current.Left - normalized.Left) < 0.5
            && Math.Abs(current.Top - normalized.Top) < 0.5
            && Math.Abs(current.Width - normalized.Width) < 0.5
            && Math.Abs(current.Height - normalized.Height) < 0.5)
        {
            return;
        }
        placement.FloatingBounds = normalized;
        _layout.IsProjectOverride = true;
        _session.Project.LayoutOverride = VisualPageJson.Clone(_layout);
        _session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
            _session.Project);
        ClearPublicationPreparation();
        OnPropertyChanged(nameof(Layout));
        NotifyDirtyState();
    }

    public void RestoreLayoutPreferences(
        StudioLayoutSnapshot? personalDefault,
        IReadOnlyList<StudioLayoutSnapshot>? namedPresets)
    {
        _personalDefaultLayout = personalDefault is null
            ? null
            : NormalizeLayoutSnapshot(VisualPageJson.Clone(personalDefault));
        NamedLayoutPresets.Clear();
        foreach (var preset in namedPresets ?? [])
        {
            NamedLayoutPresets.Add(NormalizeLayoutSnapshot(
                VisualPageJson.Clone(preset)));
        }
        SelectedLayoutPreset = NamedLayoutPresets.FirstOrDefault();
        if (_session.Project.LayoutOverride is null
            && _personalDefaultLayout is not null)
        {
            _layout = NormalizeLayoutSnapshot(
                VisualPageJson.Clone(_personalDefaultLayout));
            _layout.IsProjectOverride = false;
            OnPropertyChanged(nameof(Layout));
            RaiseToolPanelStateChanged();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
        OnPropertyChanged(nameof(PersonalDefaultLayout));
        OnPropertyChanged(nameof(NamedLayoutPresets));
    }

    private void SaveLayoutPreset()
    {
        var name = string.IsNullOrWhiteSpace(LayoutPresetName)
            ? $"Layout {NamedLayoutPresets.Count + 1}"
            : LayoutPresetName.Trim();
        var existing = NamedLayoutPresets.FirstOrDefault(preset =>
            string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
        var snapshot = VisualPageJson.Clone(_layout);
        snapshot.Name = name;
        snapshot.IsProjectOverride = false;
        if (existing is null)
        {
            NamedLayoutPresets.Add(snapshot);
        }
        else
        {
            var index = NamedLayoutPresets.IndexOf(existing);
            NamedLayoutPresets[index] = snapshot;
        }
        SelectedLayoutPreset = snapshot;
        OnPropertyChanged(nameof(NamedLayoutPresets));
        StatusMessage = $"Saved personal layout preset {name}.";
    }

    private void ApplySelectedLayoutPreset()
    {
        if (SelectedLayoutPreset is null)
        {
            return;
        }
        _layout = NormalizeLayoutSnapshot(
            VisualPageJson.Clone(SelectedLayoutPreset));
        _layout.IsProjectOverride = true;
        CommitLayoutChange();
        StatusMessage = $"Applied layout preset {SelectedLayoutPreset.Name} as this project's override.";
    }

    private void DeleteSelectedLayoutPreset()
    {
        if (SelectedLayoutPreset is null)
        {
            return;
        }
        var name = SelectedLayoutPreset.Name;
        NamedLayoutPresets.Remove(SelectedLayoutPreset);
        SelectedLayoutPreset = NamedLayoutPresets.FirstOrDefault();
        OnPropertyChanged(nameof(NamedLayoutPresets));
        StatusMessage = $"Deleted personal layout preset {name}.";
    }

    private void SetPersonalDefaultLayout()
    {
        _personalDefaultLayout = VisualPageJson.Clone(_layout);
        _personalDefaultLayout.IsProjectOverride = false;
        OnPropertyChanged(nameof(PersonalDefaultLayout));
        StatusMessage = "Saved the current arrangement as the personal Hyperpage Studio default.";
    }

    private void CommitLayoutChange()
    {
        _layout.IsProjectOverride = true;
        _session.Project.LayoutOverride = VisualPageJson.Clone(_layout);
        _session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
            _session.Project);
        ClearPublicationPreparation();
        OnPropertyChanged(nameof(Layout));
        RaiseToolPanelStateChanged();
        NotifyDirtyState();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReportStatus(string message)
    {
        StatusMessage = message;
    }

    public void RequestPaneContent(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return;
        }
        SetActiveAuthoringContent(contentId);
        if (!EnumeratePanes(_layout.Root).Any(pane =>
                pane.PaneContents.Contains(contentId, StringComparer.Ordinal)))
        {
            var root = VisualPageJson.Clone(_layout.Root);
            var emptyPane = EnumeratePanes(root).FirstOrDefault(pane => pane.PaneContents.Count == 0);
            var codePane = EnumeratePanes(root).FirstOrDefault(IsCodePane);
            if (emptyPane is not null)
            {
                emptyPane.PaneContents.Add(contentId);
                emptyPane.ActiveContentId = contentId;
            }
            else if (string.Equals(contentId, "canvas", StringComparison.Ordinal))
            {
                root = CreateCanvasCodeSplit(StudioSplitOrientation.Horizontal, root);
            }
            else if (codePane is null)
            {
                root = SplitFirstCanvasPane(
                    root,
                    StudioSplitOrientation.Horizontal,
                    contentId,
                    out var splitApplied);
                if (!splitApplied)
                {
                    root = CreateCodePane(contentId);
                }
            }
            else
            {
                codePane.PaneContents.Add(contentId);
                codePane.ActiveContentId = contentId;
            }
            _layout = CreateLayoutSnapshot(
                ContainsPaneContent(root, "canvas")
                    ? "Canvas and code"
                    : "Documents",
                root);
            CommitLayoutChange();
        }
        PaneRequested?.Invoke(this, contentId);
    }

    public bool ClosePane(string nodeId)
    {
        var closesActiveAuthoringDocument = EnumeratePanes(_layout.Root).Any(
            pane => pane.NodeId.Equals(nodeId, StringComparison.Ordinal)
                    && pane.PaneContents.Contains(
                        ActiveAuthoringContentId,
                        StringComparer.OrdinalIgnoreCase));
        var root = RemovePane(
            VisualPageJson.Clone(_layout.Root),
            nodeId,
            out var removed);
        if (removed && root is null && IsDocumentOnlyWorkspace)
        {
            _layout = CreateLayoutSnapshot(
                "Canvas",
                StudioLayoutNode.CreateDefault());
            CommitLayoutChange();
            if (closesActiveAuthoringDocument)
            {
                SetActiveAuthoringContent(string.Empty);
            }
            StatusMessage = "Closed the document pane and restored the canvas.";
            return true;
        }
        if (!removed || root is null)
        {
            return false;
        }
        _layout = CreateLayoutSnapshot("Canvas", root);
        CommitLayoutChange();
        if (closesActiveAuthoringDocument)
        {
            var nextAuthoringContent = EnumeratePanes(_layout.Root)
                .Select(pane => pane.ActiveContentId)
                .LastOrDefault(contentId => contentId is "css" or "javascript" or "csharp"
                                            || _projectDocumentsByContentId.ContainsKey(
                                                contentId));
            SetActiveAuthoringContent(nextAuthoringContent ?? string.Empty);
        }
        StatusMessage = "Closed the code pane.";
        return true;
    }

    public void RestoreRecoveryDocument(
        VisualPageDocument recovery,
        string savedProjectRevision,
        string savedPageRevision)
    {
        if (!string.Equals(recovery.ProjectId, ProjectId, StringComparison.Ordinal)
            || !string.Equals(recovery.PageId, PageId, StringComparison.Ordinal))
        {
            throw new VisualPageValidationException(
                "The recovery document does not belong to this Hyperpage tab.");
        }
        _validator.ThrowIfInvalid(recovery);
        _savedProjectRevision = savedProjectRevision;
        _savedPageRevision = savedPageRevision;
        _session.Page = VisualPageJson.Clone(recovery);
        RefreshDocumentState(recovery.Root.ElementId);
        StatusMessage = "Recovered unsaved Hyperpage Studio changes from the prior session.";
    }

    public void RestoreUserSourceRecovery(
        string csharp,
        string css,
        string javaScript)
    {
        // Legacy sessions did not record source baselines. Preserve their text without granting overwrite authority.
        foreach (var state in UserSourceStates().ToArray())
        {
            var content = state.Id switch { "csharp" => csharp, "css" => css, _ => javaScript };
            if (content is null || string.Equals(state.Content, content, StringComparison.Ordinal)) continue;
            if (content.Length > VisualPageUserSourceService.MaximumUserSourceCharacters) continue;
            ApplyUserSourceRecovery(state.Id, state.Snapshot, new()
            {
                RelativePath = state.Snapshot.RelativePath, Content = content, BaselineUnavailable = true
            }, !File.Exists(VisualPageProjectService.ResolveProjectPath(ProjectRoot, state.Snapshot.RelativePath)), sourceChanged: false);
        }
        StatusMessage = "Recovered legacy source drafts. Their original disk versions are unknown; copy the drafts before replacing source.";
    }

    public int UserSourceRecoveryVersion { get; private set; }
    public string GetUserSourceRecoveryNotice(string contentId) => _userSourceRecoveryNotices.GetValueOrDefault(contentId, string.Empty);
    public string GetUserSourceContent(string contentId) => contentId switch { "csharp" => UserCSharp, "css" => UserCss, "javascript" => UserJavaScript, _ => string.Empty };
    public bool IsUserSourceDirty(string contentId) => _userSourceRecoveryIssues.Contains(contentId)
        || UserSourceStates().Any(state => state.Id == contentId && SourceChanged(state.Snapshot, state.Content));

    private IEnumerable<(string Id, VisualPageUserSourceSnapshot Snapshot, string Content)> UserSourceStates()
    {
        if (_csharpSnapshot is not null) yield return ("csharp", _csharpSnapshot, UserCSharp);
        if (_cssSnapshot is not null) yield return ("css", _cssSnapshot, UserCss);
        if (_javaScriptSnapshot is not null) yield return ("javascript", _javaScriptSnapshot, UserJavaScript);
    }

    public IReadOnlyList<VisualPageStudioDocumentRecovery> CaptureUserSourceRecovery() => UserSourceStates()
        .Where(state => IsUserSourceDirty(state.Id)).Select(state => new VisualPageStudioDocumentRecovery
        {
            RelativePath = state.Snapshot.RelativePath, Content = state.Content,
            BaseContent = _unverifiedUserSources.Contains(state.Id) ? string.Empty : state.Snapshot.Content,
            BaseSha256 = _unverifiedUserSources.Contains(state.Id) ? string.Empty : state.Snapshot.Sha256,
            BaselineUnavailable = _unverifiedUserSources.Contains(state.Id)
        }).Concat(_pendingUserSourceRecoveries)
        .DistinctBy(recovery => recovery.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task RestoreUserSourceRecoveryAsync(IEnumerable<VisualPageStudioDocumentRecovery> recoveries, CancellationToken cancellationToken = default)
    {
        foreach (var recovery in recoveries.Take(VisualPageStudioSessionService.MaximumRecoveredDocumentsPerProject))
        {
            if (!VisualPageStudioSessionService.IsValidDocumentRecovery(recovery)) continue;
            KeepPendingRecovery(_pendingUserSourceRecoveries, recovery);
            var state = UserSourceStates().FirstOrDefault(item => item.Snapshot.RelativePath.Replace('\\', '/')
                .Equals(recovery.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (state.Snapshot is null) continue;
            string path;
            try { path = VisualPageProjectService.ResolveProjectPath(ProjectRoot, state.Snapshot.RelativePath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or VisualPageValidationException) { continue; }
            var missing = !File.Exists(path);
            var changed = false;
            if (!missing && !recovery.BaselineUnavailable)
            {
                try
                {
                    changed = new FileInfo(path).Length > 4L * VisualPageUserSourceService.MaximumUserSourceCharacters
                        || !string.Equals(VisualPageHashing.Sha256Text(await File.ReadAllTextAsync(path, cancellationToken)), recovery.BaseSha256, StringComparison.Ordinal);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { changed = true; }
            }
            ApplyUserSourceRecovery(state.Id, state.Snapshot, recovery, missing, changed);
            _pendingUserSourceRecoveries.Remove(recovery);
        }
        if (_pendingUserSourceRecoveries.Count > 0)
            StatusMessage = "Some recovered source paths are unavailable. Their drafts remain in session recovery.";
        NotifyDirtyState();
    }

    private void ApplyUserSourceRecovery(string id, VisualPageUserSourceSnapshot snapshot,
        VisualPageStudioDocumentRecovery recovery, bool sourceMissing, bool sourceChanged)
    {
        snapshot.Content = recovery.BaseContent;
        snapshot.Sha256 = recovery.BaseSha256;
        _recoveredUserSources.Add(id);
        if (recovery.BaselineUnavailable) _unverifiedUserSources.Add(id); else _unverifiedUserSources.Remove(id);
        if (recovery.BaselineUnavailable || sourceMissing || sourceChanged) _userSourceRecoveryIssues.Add(id); else _userSourceRecoveryIssues.Remove(id);
        _userSourceRecoveryNotices[id] = recovery.BaselineUnavailable
            ? "Recovered legacy draft: the original source version was not recorded. Copy this text to a new file before replacing source."
            : sourceMissing ? "Recovered draft: the source file is missing. Copy this text to a new file; saving will not recreate the missing source."
            : sourceChanged ? "Recovered draft: the source changed outside Studio. Copy this text to a new file or reconcile the source before saving."
            : "Recovered unsaved changes from your previous session.";
        if (id == "csharp") UserCSharp = recovery.Content;
        else if (id == "css") UserCss = recovery.Content;
        else UserJavaScript = recovery.Content;
        NotifyUserSourceRecovery();
        RequestPaneContent(id);
    }

    private void NotifyUserSourceRecovery()
    {
        UserSourceRecoveryVersion++;
        OnPropertyChanged(nameof(UserSourceRecoveryVersion));
        NotifyDirtyState();
    }

    public IReadOnlyList<VisualPageStudioDocumentRecovery> CaptureProjectDocumentRecovery() =>
        OpenProjectDocuments.Where(document => document.IsDirty && !document.IsReadOnly)
            .Select(document => document.CreateRecovery()).Concat(_pendingProjectDocumentRecoveries)
            .DistinctBy(recovery => recovery.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).ToArray();

    private static void KeepPendingRecovery(List<VisualPageStudioDocumentRecovery> pending, VisualPageStudioDocumentRecovery recovery)
    {
        pending.RemoveAll(item => string.Equals(item.RelativePath.Replace('\\', '/'), recovery.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        pending.Add(recovery);
    }

    public async Task<int> RestoreProjectDocumentRecoveryAsync(
        IEnumerable<VisualPageStudioDocumentRecovery> recoveries,
        CancellationToken cancellationToken = default)
    {
        var restored = 0;
        var skipped = 0;
        foreach (var recovery in recoveries.Take(VisualPageStudioSessionService.MaximumRecoveredDocumentsPerProject))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!VisualPageStudioSessionService.IsValidDocumentRecovery(recovery)) { skipped++; continue; }
            KeepPendingRecovery(_pendingProjectDocumentRecoveries, recovery);
            try
            {
                var path = recovery.RelativePath.Replace('\\', '/');
                var fullPath = VisualPageProjectService.ResolveProjectPath(ProjectRoot, path);
                if (IsStudioManagedPath(path) || TryResolveBuiltInSourceContent(path, out _))
                { skipped++; continue; }
                var contentId = HyperpageProjectDocumentIds.FromPath(path);
                _projectDocumentsByContentId.TryGetValue(contentId, out var document);
                if (document is { IsReadOnly: true } or { IsDirty: true }) { skipped++; continue; }
                var sourceMissing = !File.Exists(fullPath);
                var sourceChanged = false;
                if (!sourceMissing)
                {
                    try
                    {
                        // Preserve the draft even when the current source is too large or unreadable.
                        sourceChanged = new FileInfo(fullPath).Length > 4L * VisualPageUserSourceService.MaximumUserSourceCharacters
                            || !string.Equals(VisualPageHashing.Sha256Text(await File.ReadAllTextAsync(fullPath, cancellationToken)),
                                recovery.BaseSha256, StringComparison.Ordinal);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { sourceChanged = true; }
                }
                if (document is null)
                {
                    document = new HyperpageProjectDocumentViewModel(new VisualPageUserSourceSnapshot
                    {
                        RelativePath = path, Content = recovery.BaseContent, Sha256 = recovery.BaseSha256
                    }, ResolveProjectLanguageId(path), isReadOnly: false);
                    RegisterProjectDocument(document);
                }
                document.RestoreRecovery(recovery, sourceMissing, sourceChanged);
                RequestPaneContent(document.ContentId);
                _pendingProjectDocumentRecoveries.Remove(recovery);
                restored++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or VisualPageValidationException)
            { skipped++; }
        }
        if (restored > 0 || skipped > 0)
        {
            NotifyDirtyState();
            StatusMessage = $"Recovered {restored} unsaved project document(s)."
                + (skipped == 0 ? string.Empty : $" {skipped} draft(s) could not reopen; valid drafts remain in session recovery.");
        }
        return restored;
    }

    internal VisualPageWebTemplateCaptureResult
        PrepareWebTemplateCaptureForAi(
            string html,
            VisualPageWebTemplateCaptureRequest request)
    {
        var result = _webTemplateCaptureService.Capture(
            html,
            request,
            DateTimeOffset.UtcNow);
        var fullPath = VisualPageProjectService.ResolveProjectPath(
            _session.RootPath,
            result.RelativePath);
        if (File.Exists(fullPath))
        {
            throw new VisualPageValidationException(
                "The generated project-template target already exists. Start a new capture so it receives a fresh identity.");
        }
        _templateService.ValidateForSave(fullPath, result.Template);
        return result;
    }

    internal async Task<string> SaveWebTemplateCaptureForAiAsync(
        VisualPageTemplateDocument template,
        string relativePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        var fullPath = VisualPageProjectService.ResolveProjectPath(
            _session.RootPath,
            relativePath);
        if (File.Exists(fullPath))
        {
            throw new VisualPageValidationException(
                "The reviewed project-template target already exists; no file was replaced.");
        }
        await _templateService.SaveAsync(
            fullPath,
            template,
            cancellationToken);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() =>
                RegisterWebTemplateCaptureCore(template, relativePath));
        }
        else
        {
            RegisterWebTemplateCaptureCore(template, relativePath);
        }
        return fullPath;
    }

    private void RegisterWebTemplateCaptureCore(
        VisualPageTemplateDocument template,
        string relativePath)
    {
        var fullPath = VisualPageProjectService.ResolveProjectPath(
            _session.RootPath,
            relativePath);
        if (!File.Exists(fullPath))
        {
            throw new VisualPageValidationException(
                "The reviewed website template file was not created, so its catalog entry was not registered.");
        }
        if (_session.Project.Templates.Any(reference =>
                reference.TemplateId.Equals(
                    template.TemplateId,
                    StringComparison.Ordinal)))
        {
            return;
        }

        template.SourcePath = fullPath;
        template.StorageScope = "project";
        template.Library = "This project";
        _session.Project.Templates.Add(new VisualPageTemplateReference
        {
            TemplateId = template.TemplateId,
            Name = template.Name,
            Scope = template.Scope,
            RelativePath = relativePath,
            AssetIds = template.AssetIds,
            Dependencies = template.Dependencies
        });
        _session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
            _session.Project);
        if (!Templates.Any(candidate => candidate.TemplateId.Equals(
                template.TemplateId,
                StringComparison.Ordinal)))
        {
            Templates.Add(template);
        }
        SelectedTemplateLibraryFilter = "This project";
        SelectedTemplateScopeFilter = template.Scope switch
        {
            "page" => "Pages",
            "section" => "Sections",
            _ => "Elements"
        };
        SelectedTemplateCategoryFilter = template.CategoryGroupLabel;
        TemplateSearchText = string.Empty;
        RefreshTemplateFilter();
        SelectedTemplate = template;
        ClearPublicationPreparation();
        NotifyDirtyState();
        StatusMessage =
            $"Created reviewed project template {template.Name} from website structure. Add your own copy, assets, and styles before publishing.";
    }

    public void BeginRename() { }
    public void CommitRename() { }
    public void CancelRename() { }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        _previewBuildService.Dispose();
        _hostService.Dispose();
        _studioView?.Dispose();
        _studioView = null;
        HyperlibWorkspace?.Dispose();
    }

    public async Task RefreshProjectTreeAsync()
    {
        var selectedPath = SelectedProjectNode?.RelativePath ?? string.Empty;
        try
        {
            var root = await Task.Run(() => BuildProjectTree(ProjectRoot));
            ProjectTree.Clear();
            ProjectTree.Add(root);
            SelectedProjectNode = FindProjectTreeNode(root, selectedPath) ?? root;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
            StatusMessage = $"Project files could not be refreshed: {exception.Message}";
        }
    }

    private async Task RestoreProjectDocumentsAsync(
        CancellationToken cancellationToken)
    {
        var panes = EnumeratePanes(_layout.Root).ToList();
        var contentIds = panes
            .SelectMany(pane => pane.PaneContents)
            .Where(contentId => HyperpageProjectDocumentIds.TryGetPath(
                contentId,
                out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var contentId in contentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HyperpageProjectDocumentIds.TryGetPath(
                    contentId,
                    out var relativePath))
            {
                continue;
            }

            try
            {
                var fullPath = VisualPageProjectService.ResolveProjectPath(
                    ProjectRoot,
                    relativePath);
                if (!File.Exists(fullPath)
                    || !await IsLikelyTextFileAsync(fullPath))
                {
                    continue;
                }
                var snapshot = await _userSourceService.LoadAsync(
                    ProjectRoot,
                    relativePath,
                    cancellationToken);
                RegisterProjectDocument(new HyperpageProjectDocumentViewModel(
                    snapshot,
                    ResolveProjectLanguageId(relativePath),
                    IsStudioManagedPath(relativePath)));
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or VisualPageValidationException)
            {
                StatusMessage =
                    $"One saved document tab could not be restored: {exception.Message}";
            }
        }

        var activeContentId = panes
            .Select(pane => pane.ActiveContentId)
            .LastOrDefault(contentId =>
                _projectDocumentsByContentId.ContainsKey(contentId));
        if (!string.IsNullOrWhiteSpace(activeContentId))
        {
            SetActiveAuthoringContent(activeContentId);
        }
    }

    public async Task OpenSelectedProjectNodeAsync()
    {
        if (SelectedProjectNode is not { } node)
        {
            return;
        }
        if (node.IsDirectory)
        {
            node.IsExpanded = true;
            return;
        }
        await OpenProjectNodeAsync(node);
    }

    public async Task OpenProjectNodeAsync(
        HyperpageProjectTreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsDirectory)
        {
            node.IsExpanded = true;
            return;
        }

        if (TryResolveBuiltInSourceContent(node.RelativePath, out var builtIn))
        {
            RequestPaneContent(builtIn);
            StatusMessage = $"Opened {node.Name}.";
            return;
        }

        var contentId = HyperpageProjectDocumentIds.FromPath(node.RelativePath);
        if (_projectDocumentsByContentId.TryGetValue(contentId, out var existing))
        {
            ActiveProjectDocument = existing;
            RequestPaneContent(contentId);
            StatusMessage = $"Opened {node.Name}.";
            return;
        }

        IsBusy = true;
        try
        {
            var fullPath = VisualPageProjectService.ResolveProjectPath(
                ProjectRoot,
                node.RelativePath);
            if (!await IsLikelyTextFileAsync(fullPath))
            {
                StatusMessage =
                    $"{node.Name} is visible in Project, but its binary format needs a dedicated viewer.";
                return;
            }
            var snapshot = await _userSourceService.LoadAsync(
                ProjectRoot,
                node.RelativePath);
            var document = new HyperpageProjectDocumentViewModel(
                snapshot,
                ResolveProjectLanguageId(node.RelativePath),
                node.IsProtected || IsStudioManagedPath(node.RelativePath));
            RegisterProjectDocument(document);
            ActiveProjectDocument = document;
            RequestPaneContent(document.ContentId);
            StatusMessage = document.IsReadOnly
                ? $"Opened {node.Name} read-only because Hyperpage Studio manages it."
                : $"Opened {node.Name}.";
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or VisualPageValidationException)
        {
            StatusMessage = $"Open stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<HyperpageProjectDocumentViewModel?> CreateProjectDocumentAsync(
        string typeId,
        string requestedName,
        bool linkStylesheetToCurrentPage = false)
    {
        IsBusy = true;
        try
        {
            var definition = HyperpageDocumentTypeCatalog.GetRequired(typeId);
            var fileName = ValidateDocumentFileName(requestedName, definition);
            var directory = linkStylesheetToCurrentPage && definition.Id == "css"
                ? HyperpageDocumentTypeCatalog.DefaultDirectory(definition, Project)
                : ResolveDocumentCreationDirectory(definition);
            var relativePath = CombineRelativePath(directory, fileName);
            var fullPath = VisualPageProjectService.ResolveProjectPath(
                ProjectRoot,
                relativePath);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                throw new VisualPageValidationException(
                    $"'{relativePath}' already exists. Choose a different file name.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var starter = HyperpageDocumentTypeCatalog.CreateStarter(
                definition,
                Project,
                fileName);
            await using (var stream = new FileStream(
                             fullPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(starter);
            }

            var snapshot = await _userSourceService.LoadAsync(
                ProjectRoot,
                relativePath);
            var document = new HyperpageProjectDocumentViewModel(
                snapshot,
                definition.LanguageId,
                isReadOnly: false);
            RegisterProjectDocument(document);
            await RefreshProjectTreeAsync();
            ActiveProjectDocument = document;
            RequestPaneContent(document.ContentId);
            if (linkStylesheetToCurrentPage && definition.Id == "css"
                && !SetStylesheetLinked(document, true))
            {
                return document;
            }
            StatusMessage = $"Created {relativePath}.";
            return document;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or VisualPageValidationException)
        {
            StatusMessage = $"New document stopped: {exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> CreateProjectDirectoryAsync(string requestedName)
    {
        IsBusy = true;
        try
        {
            var name = ValidateProjectLeafName(requestedName, "folder");
            var directory = ResolveSelectedProjectDirectory();
            var relativePath = CombineRelativePath(directory, name);
            var fullPath = VisualPageProjectService.ResolveProjectPath(
                ProjectRoot,
                relativePath);
            if (Path.Exists(fullPath))
            {
                throw new VisualPageValidationException(
                    $"'{relativePath}' already exists.");
            }
            Directory.CreateDirectory(fullPath);
            await RefreshProjectTreeAsync();
            StatusMessage = $"Created {relativePath}.";
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or VisualPageValidationException)
        {
            StatusMessage = $"New folder stopped: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool IsStylesheetLinked(HyperpageProjectDocumentViewModel document) =>
        StylesheetPaths.Contains(document.RelativePath, StringComparer.OrdinalIgnoreCase);

    public bool CanUseStylesheet(HyperpageProjectDocumentViewModel document)
    {
        try
        {
            HyperpageStylesheetService.PublishedUrl(Project, document.RelativePath);
            return !document.IsReadOnly;
        }
        catch (VisualPageValidationException)
        {
            return false;
        }
    }

    public bool SetStylesheetLinked(HyperpageProjectDocumentViewModel document, bool linked)
    {
        if (linked && !CanUseStylesheet(document))
        {
            StatusMessage = "Use a plain .css file in a project wwwroot folder; .razor.css is build-time isolated.";
            return false;
        }
        if (IsStylesheetLinked(document) == linked)
        {
            return true;
        }
        var paths = StylesheetPaths.Where(path => !path.Equals(
            document.RelativePath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (linked)
        {
            // Validate filesystem containment too, including every reparse-point ancestor.
            try
            {
                var fullPath = VisualPageProjectService.ResolveProjectPath(ProjectRoot, document.RelativePath);
                if (!File.Exists(fullPath))
                {
                    StatusMessage = "The stylesheet no longer exists. Refresh the project files before linking it.";
                    return false;
                }
            }
            catch (Exception exception) when (exception is VisualPageValidationException
                or IOException or UnauthorizedAccessException)
            {
                StatusMessage = exception.Message;
                return false;
            }
            paths.Add(document.RelativePath);
        }
        var applied = ApplyMutation(new VisualPageMutationSet(Document.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetStylesheets,
                StylesheetPaths = paths
            }], "studio", linked ? "Use stylesheet on this page" : "Unlink page stylesheet"));
        if (applied)
        {
            StatusMessage = linked
                ? $"{document.FileName} now styles {Document.Name}. Save to include it in preview and export."
                : $"{document.FileName} was unlinked from {Document.Name}; the file is unchanged.";
        }
        return applied;
    }

    public bool TryGetCanvasResource(Uri uri, out byte[] content, out string contentType)
    {
        content = [];
        if (!HyperpageStylesheetService.TryResolveCanvasPath(Project, uri, out var relativePath, out contentType))
        {
            return false;
        }
        try
        {
            var fullPath = VisualPageProjectService.ResolveProjectPath(ProjectRoot, relativePath);
            if (contentType.StartsWith("text/css", StringComparison.Ordinal))
            {
                string? source = relativePath.Equals(
                    NormalizeRelativePath(VisualPageProjectService.UserCssPath(Project, Document)),
                    StringComparison.OrdinalIgnoreCase)
                    ? UserCss
                    : OpenProjectDocuments.FirstOrDefault(document => document.RelativePath.Equals(
                        relativePath, StringComparison.OrdinalIgnoreCase))?.Content;
                if (source is not null)
                {
                    if (Encoding.UTF8.GetByteCount(source) > HyperpageStylesheetService.MaximumCanvasResourceBytes)
                    {
                        return false;
                    }
                    content = Encoding.UTF8.GetBytes(source);
                    return true;
                }
            }
            if (!File.Exists(fullPath))
            {
                return false;
            }
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > HyperpageStylesheetService.MaximumCanvasResourceBytes)
            {
                return false;
            }
            content = new byte[(int)stream.Length];
            stream.ReadExactly(content);
            // Reject a changed length without reading or allocating an unbounded tail.
            return stream.ReadByte() == -1;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or VisualPageValidationException)
        {
            return false;
        }
    }

    public async Task<bool> RenameSelectedProjectNodeAsync(string requestedName)
    {
        if (SelectedProjectNode is not { CanRename: true } node)
        {
            StatusMessage =
                "Select a user-owned file or folder before renaming it.";
            return false;
        }

        IsBusy = true;
        try
        {
            var newName = ValidateProjectLeafName(
                requestedName,
                node.IsDirectory ? "folder" : "file");
            var parent = Path.GetDirectoryName(node.RelativePath)?
                .Replace('\\', '/') ?? string.Empty;
            var newRelativePath = CombineRelativePath(parent, newName);
            if (string.Equals(
                    node.RelativePath,
                    newRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var affectedDocuments = OpenProjectDocuments
                .Where(document => PathIsOrContains(
                    node.RelativePath,
                    document.RelativePath,
                    node.IsDirectory))
                .ToList();
            if (affectedDocuments.Any(document => document.IsDirty))
            {
                throw new VisualPageValidationException(
                    "Save open documents inside this item before renaming it.");
            }

            // A file-system rename is not a page mutation. Do not leave saved pages or
            // undo history pointing at missing stylesheets; unlink explicitly first.
            foreach (var page in Project.Pages)
            {
                var pageDocument = System.Text.Json.JsonSerializer.Deserialize<VisualPageDocument>(
                        await File.ReadAllTextAsync(VisualPageProjectService.ResolveProjectPath(
                            ProjectRoot, page.DocumentPath)), VisualPageJson.Options);
                if (pageDocument?.StylesheetPaths?.Any(path => PathIsOrContains(
                        node.RelativePath, path, node.IsDirectory)) == true
                    || page.PageId == Document.PageId && StylesheetPaths.Any(path => PathIsOrContains(
                        node.RelativePath, path, node.IsDirectory)))
                {
                    throw new VisualPageValidationException(
                        $"Unlink this stylesheet from {page.Name} and save before renaming it, then link the renamed file. No file was moved.");
                }
            }

            var oldFullPath = VisualPageProjectService.ResolveProjectPath(
                ProjectRoot,
                node.RelativePath);
            var newFullPath = VisualPageProjectService.ResolveProjectPath(
                ProjectRoot,
                newRelativePath);
            if (Path.Exists(newFullPath))
            {
                throw new VisualPageValidationException(
                    $"'{newRelativePath}' already exists.");
            }
            if (node.IsDirectory)
            {
                Directory.Move(oldFullPath, newFullPath);
            }
            else
            {
                File.Move(oldFullPath, newFullPath);
            }
            var removedHistory = _undo.RemoveAll(snapshot => snapshot.StylesheetPaths.Any(path =>
                PathIsOrContains(node.RelativePath, path, node.IsDirectory)));
            removedHistory += _redo.RemoveAll(snapshot => snapshot.StylesheetPaths.Any(path =>
                PathIsOrContains(node.RelativePath, path, node.IsDirectory)));

            var replacements = new List<(string OldId, string NewId)>();
            foreach (var document in affectedDocuments)
            {
                var oldId = document.ContentId;
                _projectDocumentsByContentId.Remove(oldId);
                var suffix = node.IsDirectory
                    ? document.RelativePath[node.RelativePath.Length..]
                        .TrimStart('/', '\\')
                    : string.Empty;
                var updatedPath = node.IsDirectory
                    ? CombineRelativePath(newRelativePath, suffix)
                    : newRelativePath;
                document.UpdatePath(updatedPath);
                _projectDocumentsByContentId[document.ContentId] = document;
                replacements.Add((oldId, document.ContentId));
            }
            if (replacements.Count > 0)
            {
                ReplacePaneContentIds(replacements);
            }

            await RefreshProjectTreeAsync();
            RefreshCommandState();
            StatusMessage = $"Renamed {node.Name} to {newName}."
                + (removedHistory > 0 ? " Page-history entries referencing the old filename were cleared." : string.Empty);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or VisualPageValidationException
                                         or System.Text.Json.JsonException)
        {
            StatusMessage = $"Rename stopped: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool TryGetProjectDocument(
        string contentId,
        out HyperpageProjectDocumentViewModel? document) =>
        _projectDocumentsByContentId.TryGetValue(contentId, out document);

    public string PaneTitle(string contentId) =>
        _projectDocumentsByContentId.TryGetValue(contentId, out var document)
            ? document.Title
            : string.Empty;

    public string PaneToolTip(string contentId) =>
        _projectDocumentsByContentId.TryGetValue(contentId, out var document)
            ? document.RelativePath
            : "Drag this pane tab to another Studio pane.";

    public void AppendToActiveAuthoringDocument(string text)
    {
        if (!HasActiveAuthoringDocument || string.IsNullOrEmpty(text))
        {
            return;
        }
        var current = ActiveAuthoringText();
        var separator = current.EndsWith('\n')
            || current.Length == 0
            ? string.Empty
            : Environment.NewLine;
        var terminator = text.EndsWith('\n')
            ? string.Empty
            : Environment.NewLine;
        var updated = current
                      + separator
                      + text
                      + terminator;
        switch (ActiveAuthoringContentId)
        {
            case "css":
                UserCss = updated;
                break;
            case "javascript":
                UserJavaScript = updated;
                break;
            case "csharp":
                UserCSharp = updated;
                break;
            default:
                if (ActiveProjectDocument is { IsReadOnly: false } document)
                {
                    document.Content = updated;
                }
                break;
        }
    }

    private HyperpageProjectTreeNodeViewModel BuildProjectTree(string projectRoot)
    {
        var count = 0;
        var root = new HyperpageProjectTreeNodeViewModel
        {
            Name = Project.Name,
            RelativePath = string.Empty,
            IsDirectory = true,
            IsProjectRoot = true,
            IsProtected = true,
            IsExpanded = true
        };
        PopulateProjectTree(root, projectRoot, ref count);
        return root;
    }

    private void PopulateProjectTree(
        HyperpageProjectTreeNodeViewModel parent,
        string directory,
        ref int count)
    {
        const int maximumNodes = 5000;
        if (count >= maximumNodes)
        {
            return;
        }
        var entries = Directory.EnumerateFileSystemEntries(directory)
            .OrderBy(path => !Directory.Exists(path))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var entry in entries)
        {
            // Content and nested application projects have their own navigator.
            // Keep build output from crowding out authorable source in any Studio project.
            var entryName = Path.GetFileName(entry);
            if (Directory.Exists(entry) && (entryName is "bin" or "obj" or "node_modules" or ".git" or ".hyperlib" or ".vs"
                || HasHyperlibAuthoring && Path.GetFullPath(directory).Equals(Path.GetFullPath(ProjectRoot), StringComparison.OrdinalIgnoreCase)
                   && entryName is "Works" or "BaseTemplates" or "Starters" or "Content" or "Library" or "Media" or "output"))
                continue;
            if (HasHyperlibAuthoring && Path.GetRelativePath(ProjectRoot, directory).Equals("src", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(entry) && entryName is not ("Pages" or "Host"))
                continue;
            if (++count > maximumNodes)
            {
                parent.Children.Add(new HyperpageProjectTreeNodeViewModel
                {
                    Name = "More files…",
                    RelativePath = string.Empty,
                    IsDirectory = false,
                    IsProtected = true,
                    LanguageId = "plaintext"
                });
                return;
            }
            var relativePath = NormalizeRelativePath(
                Path.GetRelativePath(ProjectRoot, entry));
            var isDirectory = Directory.Exists(entry);
            var attributes = File.GetAttributes(entry);
            var node = new HyperpageProjectTreeNodeViewModel
            {
                Name = Path.GetFileName(entry),
                RelativePath = relativePath,
                IsDirectory = isDirectory,
                IsProtected = IsStudioManagedPath(relativePath),
                Size = isDirectory ? 0 : new FileInfo(entry).Length,
                LanguageId = isDirectory
                    ? "plaintext"
                    : ResolveProjectLanguageId(relativePath)
            };
            parent.Children.Add(node);
            if (isDirectory
                && !attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                try
                {
                    PopulateProjectTree(node, entry, ref count);
                }
                catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException)
                {
                    node.Children.Add(new HyperpageProjectTreeNodeViewModel
                    {
                        Name = "Unavailable",
                        RelativePath = relativePath,
                        IsDirectory = false,
                        IsProtected = true
                    });
                }
            }
        }
    }

    private static HyperpageProjectTreeNodeViewModel? FindProjectTreeNode(
        HyperpageProjectTreeNodeViewModel node,
        string relativePath)
    {
        if (string.Equals(
                node.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }
        foreach (var child in node.Children)
        {
            if (FindProjectTreeNode(child, relativePath) is { } match)
            {
                return match;
            }
        }
        return null;
    }

    private void RegisterProjectDocument(
        HyperpageProjectDocumentViewModel document)
    {
        if (_projectDocumentsByContentId.ContainsKey(document.ContentId))
        {
            return;
        }
        _projectDocumentsByContentId[document.ContentId] = document;
        OpenProjectDocuments.Add(document);
        document.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName is nameof(HyperpageProjectDocumentViewModel.Content)
                or nameof(HyperpageProjectDocumentViewModel.IsDirty))
            {
                ClearPublicationPreparation();
                NotifyDirtyState();
                if (document.LanguageId == "css")
                {
                    InvalidateCanvas();
                }
            }
        };
    }

    private async Task SaveProjectDocumentsAsync()
    {
        foreach (var document in OpenProjectDocuments.Where(document =>
                     document.IsDirty && !document.IsReadOnly).ToArray())
        {
            if (document.RecoveryBaselineUnavailable)
                throw new VisualPageValidationException($"'{document.RelativePath}' has no recorded original source version. Copy the recovered draft to a new file before replacing source.");
            if (document.IsRecovered && !File.Exists(VisualPageProjectService.ResolveProjectPath(ProjectRoot, document.RelativePath)))
                throw new VisualPageValidationException($"'{document.RelativePath}' is a recovered draft whose source file is missing. Copy the draft to a new file before saving; the original was not recreated.");
            var savedContent = document.Content;
            await _userSourceService.SaveAsync(
                ProjectRoot,
                document.Snapshot,
                savedContent);
            document.MarkSaved(savedContent);
        }
    }

    private void SetActiveAuthoringContent(string contentId)
    {
        _activeAuthoringContentId = contentId;
        _projectDocumentsByContentId.TryGetValue(
            contentId,
            out var projectDocument);
        ActiveProjectDocument = projectDocument;
        _activeAuthoringLanguageId = projectDocument?.LanguageId
                                     ?? contentId switch
                                     {
                                         "css" => "css",
                                         "javascript" => "javascript",
                                         "csharp" when SupportsCSharpSource => "csharp",
                                         _ => string.Empty
                                     };
        _authoringSearchText = string.Empty;
        _selectedAuthoringCategory = "All categories";
        OnPropertyChanged(nameof(ActiveAuthoringContentId));
        OnPropertyChanged(nameof(ActiveAuthoringLanguageId));
        OnPropertyChanged(nameof(HasActiveAuthoringDocument));
        OnPropertyChanged(nameof(ActiveAuthoringLanguageName));
        OnPropertyChanged(nameof(ActiveAuthoringDocumentName));
        OnPropertyChanged(nameof(AuthoringRelationshipSummary));
        OnPropertyChanged(nameof(AuthoringSearchText));
        OnPropertyChanged(nameof(SelectedAuthoringCategory));
        RefreshAuthoringLibrary();
    }

    private void RefreshAuthoringLibrary()
    {
        var activeRecipes = AuthoringRecipes
            .Where(recipe => recipe.Supports(ActiveAuthoringLanguageId))
            .ToList();
        AuthoringCategories.Clear();
        AuthoringCategories.Add("All categories");
        foreach (var category in activeRecipes
                     .Select(recipe => recipe.Category)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            AuthoringCategories.Add(category);
        }

        var selectedId = SelectedAuthoringRecipe?.Id;
        var search = AuthoringSearchText.Trim();
        var filtered = activeRecipes.Where(recipe =>
            (SelectedAuthoringCategory.Equals(
                 "All categories",
                 StringComparison.OrdinalIgnoreCase)
             || recipe.Category.Equals(
                 SelectedAuthoringCategory,
                 StringComparison.OrdinalIgnoreCase))
            && (search.Length == 0
                || recipe.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || recipe.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || recipe.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
                || recipe.Signature.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        FilteredAuthoringRecipes.Clear();
        foreach (var recipe in filtered)
        {
            FilteredAuthoringRecipes.Add(recipe);
        }
        SelectedAuthoringRecipe = FilteredAuthoringRecipes.FirstOrDefault(recipe =>
                                      recipe.Id.Equals(
                                          selectedId,
                                          StringComparison.OrdinalIgnoreCase))
                                  ?? FilteredAuthoringRecipes.FirstOrDefault();
        OnPropertyChanged(nameof(AuthoringLibrarySummary));
        OnPropertyChanged(nameof(CanInsertAuthoringSnippet));
        InsertAuthoringSnippetCommand?.RaiseCanExecuteChanged();
    }

    private void LoadAuthoringFields()
    {
        AuthoringFields.Clear();
        if (SelectedAuthoringRecipe is { } recipe)
        {
            foreach (var parameter in recipe.Parameters)
            {
                AuthoringFields.Add(new HyperpageAuthoringFieldViewModel(
                    parameter,
                    UpdateAuthoringPreview));
            }
        }
        UpdateAuthoringPreview();
    }

    private void UpdateAuthoringPreview()
    {
        var values = AuthoringFields.ToDictionary(
            field => field.Id,
            field => field.Value,
            StringComparer.Ordinal);
        AuthoringPreview = SelectedAuthoringRecipe?.Render(values) ?? string.Empty;
        OnPropertyChanged(nameof(CanInsertAuthoringSnippet));
        InsertAuthoringSnippetCommand?.RaiseCanExecuteChanged();
    }

    private void InsertSelectedAuthoringSnippet()
    {
        if (!CanInsertAuthoringSnippet
            || string.IsNullOrWhiteSpace(AuthoringPreview))
        {
            return;
        }
        var insertion = AuthoringPreview.TrimEnd() + Environment.NewLine;
        if (ProjectDocumentInsertionRequested is { } handler)
        {
            handler.Invoke(
                this,
                new HyperpageDocumentInsertionRequest(
                    ActiveAuthoringContentId,
                    insertion));
        }
        else
        {
            AppendToActiveAuthoringDocument(insertion);
        }
        StatusMessage = $"Inserted {SelectedAuthoringRecipe!.Name} into {ActiveAuthoringDocumentName}.";
    }

    private string ActiveAuthoringText() => ActiveAuthoringContentId switch
    {
        "css" => UserCss,
        "javascript" => UserJavaScript,
        "csharp" => UserCSharp,
        _ => ActiveProjectDocument?.Content ?? string.Empty
    };

    private bool TryResolveBuiltInSourceContent(
        string relativePath,
        out string contentId)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.Equals(
                NormalizeRelativePath(VisualPageProjectService.UserCssPath(
                    Project,
                    Document)),
                StringComparison.OrdinalIgnoreCase))
        {
            contentId = "css";
            return true;
        }
        if (normalized.Equals(
                NormalizeRelativePath(VisualPageProjectService.UserJavaScriptPath(
                    Project,
                    Document)),
                StringComparison.OrdinalIgnoreCase))
        {
            contentId = "javascript";
            return true;
        }
        if (SupportsCSharpSource
            && normalized.Equals(
                NormalizeRelativePath(VisualPageProjectService.UserCSharpPath(
                    Project,
                    Document)),
                StringComparison.OrdinalIgnoreCase))
        {
            contentId = "csharp";
            return true;
        }
        contentId = string.Empty;
        return false;
    }

    private string ResolveDocumentCreationDirectory(
        HyperpageDocumentTypeDefinition definition)
    {
        if (SelectedProjectNode is { IsProjectRoot: false, IsProtected: false } node)
        {
            return node.IsDirectory
                ? node.RelativePath
                : Path.GetDirectoryName(node.RelativePath)?.Replace('\\', '/')
                  ?? string.Empty;
        }
        return HyperpageDocumentTypeCatalog.DefaultDirectory(definition, Project);
    }

    private string ResolveSelectedProjectDirectory()
    {
        if (SelectedProjectNode is not { } node || node.IsProjectRoot)
        {
            return string.Empty;
        }
        if (node.IsProtected)
        {
            return string.Empty;
        }
        return node.IsDirectory
            ? node.RelativePath
            : Path.GetDirectoryName(node.RelativePath)?.Replace('\\', '/')
              ?? string.Empty;
    }

    private bool IsStudioManagedPath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.Equals(
                VisualPageProjectService.ProjectManifestFileName,
                StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".visual-page/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".visual-page", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".visual-page.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (new[]
            {
                "src", "src/Host", "src/Pages", "src/Pages/Generated",
                "Pages", "Shared", "Assets", "Templates"
            }.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        if (Project.Pages.Any(page =>
                NormalizeRelativePath(page.ComponentPath).Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase)
                || NormalizeRelativePath(page.DocumentPath).Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return normalized.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".razor.js", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".custom.css", StringComparison.OrdinalIgnoreCase);
    }

    private void ReplacePaneContentIds(
        IReadOnlyList<(string OldId, string NewId)> replacements)
    {
        var changed = false;
        foreach (var pane in EnumeratePanes(_layout.Root))
        {
            for (var index = 0; index < pane.PaneContents.Count; index++)
            {
                var replacement = replacements.FirstOrDefault(pair =>
                    pair.OldId.Equals(
                        pane.PaneContents[index],
                        StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(replacement.OldId))
                {
                    continue;
                }
                pane.PaneContents[index] = replacement.NewId;
                if (pane.ActiveContentId.Equals(
                        replacement.OldId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    pane.ActiveContentId = replacement.NewId;
                }
                if (_activeAuthoringContentId.Equals(
                        replacement.OldId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activeAuthoringContentId = replacement.NewId;
                }
                changed = true;
            }
        }
        if (changed)
        {
            CommitLayoutChange();
            SetActiveAuthoringContent(_activeAuthoringContentId);
        }
    }

    private static string ValidateDocumentFileName(
        string requestedName,
        HyperpageDocumentTypeDefinition definition)
    {
        var name = ValidateProjectLeafName(requestedName, "file");
        var extension = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return name + definition.Extension;
        }
        if (!extension.Equals(definition.Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new VisualPageValidationException(
                $"{definition.DisplayName} files use the {definition.Extension} extension.");
        }
        return name;
    }

    private static string ValidateProjectLeafName(
        string requestedName,
        string kind)
    {
        var name = requestedName.Trim();
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)
            || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new VisualPageValidationException(
                $"Choose a valid {kind} name without a path or trailing period.");
        }
        return name;
    }

    private static string CombineRelativePath(string first, string second) =>
        NormalizeRelativePath(string.IsNullOrWhiteSpace(first)
            ? second
            : Path.Combine(first, second));

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static bool PathIsOrContains(
        string parent,
        string candidate,
        bool isDirectory) =>
        candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || isDirectory && candidate.StartsWith(
            parent.TrimEnd('/', '\\') + "/",
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveProjectLanguageId(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return extension.ToLowerInvariant() switch
        {
            ".razor" or ".cshtml" => "html",
            ".sass" => "sass",
            ".less" => "less",
            _ => HyperpageDocumentTypeCatalog.ResolveLanguageId(relativePath)
        };
    }

    private static async Task<bool> IsLikelyTextFileAsync(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists
            || info.Length > VisualPageUserSourceService.MaximumUserSourceCharacters)
        {
            return false;
        }
        var binaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".ico",
            ".woff", ".woff2", ".ttf", ".zip", ".dll", ".exe", ".pdb",
            ".pdf", ".mp3", ".wav", ".mp4", ".webm"
        };
        if (binaryExtensions.Contains(info.Extension))
        {
            return false;
        }
        var buffer = new byte[Math.Min(4096, (int)info.Length)];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            buffer.Length == 0 ? 1 : buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = buffer.Length == 0
            ? 0
            : await stream.ReadAsync(buffer);
        return !buffer.AsSpan(0, read).Contains((byte)0);
    }

    private async Task LoadTemplateCatalogAsync(
        CancellationToken cancellationToken)
    {
        var knownIds = Templates.Select(template => template.TemplateId)
            .ToHashSet(StringComparer.Ordinal);
        var rejected = 0;
        foreach (var reference in _session.Project.Templates.Take(256))
        {
            try
            {
                var path = VisualPageProjectService.ResolveProjectPath(
                    _session.RootPath,
                    reference.RelativePath);
                var template = await _templateService.LoadAsync(
                    path,
                    cancellationToken);
                template.StorageScope = "project";
                template.Library = "This project";
                if (knownIds.Add(template.TemplateId))
                {
                    Templates.Add(template);
                }
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or VisualPageValidationException)
            {
                rejected++;
            }
        }
        VisualPageTemplateCatalogLoadResult personal;
        try
        {
            personal = await _templateService.LoadPersonalAsync(
                cancellationToken);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException)
        {
            personal = new VisualPageTemplateCatalogLoadResult([], 1);
        }
        rejected += personal.RejectedCount;
        foreach (var template in personal.Templates)
        {
            template.Library = "My templates";
            if (knownIds.Add(template.TemplateId))
            {
                Templates.Add(template);
            }
            else
            {
                rejected++;
            }
        }
        RefreshTemplateFilter();
        if (rejected > 0)
        {
            StatusMessage = $"Hyperpage Studio skipped {rejected} invalid or duplicate template catalog item(s).";
        }
    }

    private async Task LoadUserSourcesAsync(
        CancellationToken cancellationToken)
    {
        if (SupportsCSharpSource)
        {
            _csharpSnapshot = await _userSourceService.LoadAsync(
                _session.RootPath,
                VisualPageProjectService.UserCSharpPath(
                    _session.Project,
                    _session.Page),
                cancellationToken);
        }
        else
        {
            _csharpSnapshot = null;
        }
        _cssSnapshot = await _userSourceService.LoadAsync(
            _session.RootPath,
            VisualPageProjectService.UserCssPath(
                _session.Project,
                _session.Page),
            cancellationToken);
        _javaScriptSnapshot = await _userSourceService.LoadAsync(
            _session.RootPath,
            VisualPageProjectService.UserJavaScriptPath(
                _session.Project,
                _session.Page),
            cancellationToken);
        _recoveredUserSources.Clear();
        _unverifiedUserSources.Clear();
        _userSourceRecoveryIssues.Clear();
        _userSourceRecoveryNotices.Clear();
        UserSourceRecoveryVersion++;
        OnPropertyChanged(nameof(UserSourceRecoveryVersion));
        _userCSharp = _csharpSnapshot?.Content ?? string.Empty;
        _userCss = _cssSnapshot.Content;
        _userJavaScript = _javaScriptSnapshot.Content;
        OnPropertyChanged(nameof(UserCSharp));
        OnPropertyChanged(nameof(UserCss));
        OnPropertyChanged(nameof(UserJavaScript));
        NotifyDirtyState();
    }

    public async Task<bool> SaveProjectAsync()
    {
        if (HasUnsavedHyperlibContent && SaveHyperlibContentAsync is { } saveContent && !await saveContent())
        {
            StatusMessage = "Page content needs attention before the project can be saved.";
            return false;
        }
        if (HyperlibWorkspace?.HasUnsavedAppearanceChanges == true)
        {
            StatusMessage = "Save or reload Work appearance in the Work workspace before saving the project.";
            return false;
        }
        if (_pendingUserSourceRecoveries.Count != 0 || _pendingProjectDocumentRecoveries.Count != 0)
        {
            StatusMessage = "Some recovered drafts cannot reopen at their original paths. They remain in session recovery; restore those source paths before saving or explicitly discard this tab's drafts.";
            return false;
        }
        var saved = false;
        IsBusy = true;
        try
        {
            await SaveUserSourcesAsync();
            await SaveProjectDocumentsAsync();
            await _projectService.SaveAsync(
                _session,
                _savedProjectRevision,
                _savedPageRevision);
            _savedProjectRevision = _session.Project.Revision;
            _savedPageRevision = _session.Page.Revision;
            RefreshGeneratedSource();
            NotifyDirtyState();
            StatusMessage = "Hyperpage project saved atomically.";
            saved = true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException
            or VisualPageStaleRevisionException
            or VisualPageGeneratedDriftException
            or VisualPageUserSourceDriftException)
        {
            StatusMessage = $"Save stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        return saved;
    }

    private async Task SaveUserSourcesAsync()
    {
        foreach (var state in UserSourceStates().Where(state => IsUserSourceDirty(state.Id)).ToArray())
        {
            if (_unverifiedUserSources.Contains(state.Id))
                throw new VisualPageValidationException($"'{state.Snapshot.RelativePath}' is a legacy recovered draft without its original source version. Copy it to a new file before replacing source.");
            if (_recoveredUserSources.Contains(state.Id)
                && !File.Exists(VisualPageProjectService.ResolveProjectPath(ProjectRoot, state.Snapshot.RelativePath)))
                throw new VisualPageValidationException($"'{state.Snapshot.RelativePath}' is a recovered draft whose source is missing. Copy it to a new file; the original was not recreated.");
            await _userSourceService.SaveAsync(_session.RootPath, state.Snapshot, state.Content);
            _recoveredUserSources.Remove(state.Id);
            _userSourceRecoveryIssues.Remove(state.Id);
            _userSourceRecoveryNotices.Remove(state.Id);
            NotifyUserSourceRecovery();
        }
    }

    private async Task BuildAsync()
    {
        if (!TrustCustomCode)
        {
            StatusMessage =
                "Compiled preview can execute project C#, JavaScript, packages, and MSBuild targets with your Windows permissions. Review the project and enable explicit trust first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Building the latest isolated preview snapshot…";
        try
        {
            await BuildPreviewCoreAsync(CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<VisualPageBuildResult> BuildPreviewForAiAsync(
        CancellationToken cancellationToken) =>
        BuildPreviewCoreAsync(cancellationToken);

    private async Task<VisualPageBuildResult> BuildPreviewCoreAsync(
        CancellationToken cancellationToken)
    {
        _latestBuild = await _previewBuildService.BuildLatestAsync(
            _session,
            CreateSourceOverrides(),
            cancellationToken);
        BuildOutput = _latestBuild.Output;
        Diagnostics.Clear();
        foreach (var diagnostic in _latestBuild.Diagnostics)
        {
            Diagnostics.Add(diagnostic);
        }
        StatusMessage = _latestBuild.Outcome switch
        {
            VisualPageBuildOutcome.Succeeded =>
                "Compiled preview build succeeded.",
            VisualPageBuildOutcome.Cancelled =>
                "Preview build replaced by a newer build.",
            VisualPageBuildOutcome.SdkUnavailable =>
                ".NET 10 SDK is unavailable; the design canvas remains usable.",
            _ => "Compiled preview build failed; diagnostics are mapped to the page."
        };
        return _latestBuild;
    }

    private async Task StartHostAsync()
    {
        if (!TrustCustomCode)
        {
            StatusMessage =
                "Local hosting requires explicit trust because the preview build and host run project code with your Windows permissions.";
            return;
        }

        IsBusy = true;
        try
        {
            if (_latestBuild is null
                || _latestBuild.Outcome != VisualPageBuildOutcome.Succeeded
                || !string.Equals(
                    _latestBuild.Revision,
                    _session.Page.Revision,
                    StringComparison.Ordinal))
            {
                _latestBuild = await _previewBuildService.BuildLatestAsync(
                    _session,
                    CreateSourceOverrides());
                BuildOutput = _latestBuild.Output;
            }
            if (_latestBuild.Outcome != VisualPageBuildOutcome.Succeeded)
            {
                StatusMessage = "Hosting stopped because the latest preview build failed.";
                return;
            }
            var port = int.TryParse(
                HostPortText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : -1;
            var runtime = await _hostService.StartAsync(
                _latestBuild,
                new VisualPageHostRequest
                {
                    Port = port,
                    AdapterAddress = AdapterAddress,
                    AllowLan = AllowLanHosting,
                    TrustCustomCode = TrustCustomCode
                });
            RuntimePreviewUrl = runtime.Url;
            if (_atlasMyNetRegistration is not null)
            {
                _atlasMyNetRegistration(
                    _hostService.CreateAtlasMyNetDefinition(_session, runtime));
            }
            RuntimePreviewRequested?.Invoke(this, runtime.Url);
            StatusMessage = runtime.IsLanExposed
                ? $"Preview hosted on the explicitly selected LAN adapter at {runtime.Url}. No firewall rule was created."
                : $"Preview hosted on loopback at {runtime.Url}.";
        }
        catch (Exception exception) when (exception is
            VisualPageValidationException or IOException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusMessage = $"Hosting stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopHostAsync()
    {
        await _hostService.StopAsync();
        RuntimePreviewUrl = string.Empty;
        StatusMessage = "Local Hyperpage host stopped.";
    }

    private async Task PreparePublicationAsync()
    {
        if (HasUnsavedChanges && !await SaveProjectAsync())
        {
            StatusMessage = "Publication stopped because the active project could not be saved.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Preparing an isolated Atlas Archive worktree and running publication gates…";
        try
        {
            var preparation = await _publicationService.PrepareAsync(
                _session,
                AtlasCheckoutPath,
                ArchiveSlug,
                PublishTargetBranch);
            _publicationPreparation = preparation;
            _publishTargetBranch = preparation.TargetBranch;
            OnPropertyChanged(nameof(PublishTargetBranch));
            PublicationReview = $$"""
                VALIDATION
                {{preparation.ValidationOutput}}

                SOURCE REVIEW
                {{preparation.ReviewText}}
                """;
            PublishedCommit = string.Empty;
            ApproveReviewedCode = false;
            OnPropertyChanged(nameof(IsPublicationPrepared));
            OnPropertyChanged(nameof(RequiresReviewedCode));
            (PublishExactCommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            StatusMessage = preparation.RequiresReviewedCode
                ? "Atlas publication prepared. Review the source diff and explicitly approve reviewed code before pushing."
                : "Atlas fast-lane publication prepared. Review the deterministic source diff before pushing.";
        }
        catch (Exception exception) when (exception is
            AtlasArchivePublicationException or IOException
            or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException)
        {
            ClearPublicationPreparation();
            PublicationReview = $"Publication preparation stopped: {exception.Message}";
            StatusMessage = "Atlas publication preparation stopped; no commit was pushed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PublishExactCommitAsync()
    {
        var preparation = _publicationPreparation;
        if (preparation is null)
        {
            StatusMessage = "Prepare and review an Atlas publication first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Rechecking the reviewed hash and pushing the exact Atlas commit…";
        try
        {
            var receipt = await _publicationService.PublishExactCommitAsync(
                _session,
                preparation,
                ApproveReviewedCode);
            PublishedCommit = receipt.SourceCommit;
            PublicationReview = $"{PublicationReview}{Environment.NewLine}{Environment.NewLine}" +
                                $"PUSHED EXACT COMMIT{Environment.NewLine}" +
                                $"{receipt.SourceCommit} -> {receipt.TargetBranch}{Environment.NewLine}" +
                                $"Receipt: {receipt.ReceiptId}";
            _publicationPreparation = null;
            ApproveReviewedCode = false;
            OnPropertyChanged(nameof(IsPublicationPrepared));
            OnPropertyChanged(nameof(RequiresReviewedCode));
            (PublishExactCommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            StatusMessage =
                $"Atlas source commit {receipt.SourceCommit[..12]} pushed exactly. Production activation remains a separate reviewed release action.";
        }
        catch (Exception exception) when (exception is
            AtlasArchivePublicationException or IOException
            or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException)
        {
            PublicationReview = $"{PublicationReview}{Environment.NewLine}{Environment.NewLine}" +
                                $"PUBLISH STOPPED{Environment.NewLine}{exception.Message}";
            StatusMessage = "Atlas publication stopped; production was not activated.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearPublicationPreparation()
    {
        if (_publicationPreparation is null)
        {
            return;
        }
        _publicationPreparation = null;
        ApproveReviewedCode = false;
        PublicationReview = "Publication inputs changed. Prepare a fresh isolated review.";
        OnPropertyChanged(nameof(IsPublicationPrepared));
        OnPropertyChanged(nameof(RequiresReviewedCode));
        (PublishExactCommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private IReadOnlyDictionary<string, string> CreateSourceOverrides()
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [VisualPageProjectService.UserCssPath(
                _session.Project,
                _session.Page)] = UserCss,
            [VisualPageProjectService.UserJavaScriptPath(
                _session.Project,
                _session.Page)] = UserJavaScript
        };
        if (SupportsCSharpSource)
        {
            sources[VisualPageProjectService.UserCSharpPath(
                _session.Project,
                _session.Page)] = UserCSharp;
        }
        foreach (var document in OpenProjectDocuments.Where(document =>
                     !document.IsReadOnly))
        {
            sources[document.RelativePath] = document.Content;
        }
        return sources;
    }

    public async Task<bool> SelectProjectPageAsync(string pageId)
    {
        var page = ProjectPages.FirstOrDefault(candidate => candidate.PageId == pageId);
        if (page is null) return false;
        SelectedProjectPage = page;
        await OpenSelectedPageAsync();
        return PageId == pageId;
    }

    private async Task OpenSelectedPageAsync()
    {
        if (SelectedProjectPage is null
            || SelectedProjectPage.PageId == _session.Page.PageId)
        {
            return;
        }
        if (HasUnsavedChanges)
        {
            StatusMessage = "Save or undo current page changes before switching pages.";
            _selectedProjectPage = _session.Project.Pages.Single(page =>
                page.PageId == _session.Page.PageId);
            OnPropertyChanged(nameof(SelectedProjectPage));
            return;
        }
        IsBusy = true;
        try
        {
            _session = await _projectService.OpenAsync(
                _session.RootPath,
                SelectedProjectPage.PageId);
            _savedProjectRevision = _session.Project.Revision;
            _savedPageRevision = _session.Page.Revision;
            _selectedProjectPage = _session.Project.Pages.Single(page =>
                page.PageId == _session.Page.PageId);
            OnPropertyChanged(nameof(SelectedProjectPage));
            _undo.Clear();
            _redo.Clear();
            ClearPublicationPreparation();
            _archiveSlug = _session.Page.Slug;
            OnPropertyChanged(nameof(ArchiveSlug));
            SynchronizeProjectAssets();
            await LoadUserSourcesAsync(CancellationToken.None);
            await RefreshProjectTreeAsync();
            RefreshDocumentState(_session.Page.Root.ElementId);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(ActivePageFileType));
            OnPropertyChanged(nameof(SupportsCSharpSource));
            OnPropertyChanged(nameof(GeneratedMarkupLabel));
            if (ActiveAuthoringContentId is "css" or "javascript" or "csharp")
            {
                SetActiveAuthoringContent(ActiveAuthoringContentId);
            }
            OnPropertyChanged(nameof(ProjectPages));
            StatusMessage = $"Opened {_session.Page.Name}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CreatePageAsync()
    {
        await CreatePageAsync(new HyperpagePageCreationRequest(
            NewPageName,
            NewPageRoute,
            HyperpagePageFileType.ProjectDefault,
            null,
            string.Empty));
    }

    public async Task<bool> CreatePageAsync(
        HyperpagePageCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (HasUnsavedChanges)
        {
            StatusMessage = "Save the active page before adding another page.";
            return false;
        }
        var created = false;
        IsBusy = true;
        try
        {
            VisualPageElement? starterRoot = null;
            if (request.Template is { } template)
            {
                starterRoot = await InstantiateTemplateWithAssetsAsync(template);
                if (!starterRoot.DescriptorId.Equals(
                        "core.page",
                        StringComparison.Ordinal))
                {
                    throw new VisualPageValidationException(
                        "A page starter must contain a complete page root.");
                }
            }

            _session = await _projectService.AddPageAsync(
                _session,
                request.Name,
                request.Route,
                starterRoot,
                request.Template?.ThemeTokens,
                title: string.IsNullOrWhiteSpace(
                    request.Template?.SuggestedPageTitle)
                    ? request.Name
                    : request.Template.SuggestedPageTitle,
                description: request.Template?.SuggestedPageDescription,
                fileType: request.FileType,
                starterTemplateId: request.Template?.TemplateId,
                starterTemplateName: request.Template?.Name);
            _savedProjectRevision = _session.Project.Revision;
            _savedPageRevision = _session.Page.Revision;
            _selectedProjectPage = _session.Project.Pages.Single(page =>
                page.PageId == _session.Page.PageId);
            OnPropertyChanged(nameof(SelectedProjectPage));
            OnPropertyChanged(nameof(ProjectPages));
            _undo.Clear();
            _redo.Clear();
            ClearPublicationPreparation();
            _archiveSlug = _session.Page.Slug;
            OnPropertyChanged(nameof(ArchiveSlug));
            await LoadUserSourcesAsync(CancellationToken.None);
            await RefreshProjectTreeAsync();
            RefreshDocumentState(_session.Page.Root.ElementId);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(ActivePageFileType));
            OnPropertyChanged(nameof(SupportsCSharpSource));
            OnPropertyChanged(nameof(GeneratedMarkupLabel));
            if (ActiveAuthoringContentId is "css" or "javascript" or "csharp")
            {
                SetActiveAuthoringContent(ActiveAuthoringContentId);
            }
            NewPageName = "New Page";
            NewPageRoute = "/new-page";
            created = true;
            StatusMessage = request.Template is null
                ? $"Created and saved {_session.Page.Name}."
                : $"Created and saved {_session.Page.Name} from {request.Template.Name}.";
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException or VisualPageStaleRevisionException)
        {
            StatusMessage = $"New page stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (created && !string.IsNullOrWhiteSpace(request.Description))
        {
            await BeginPageFoundationAsync(request);
        }
        return created;
    }

    private async Task BeginPageFoundationAsync(
        HyperpagePageCreationRequest request)
    {
        if (_aiAssistant is null)
        {
            StatusMessage =
                $"Created {_session.Page.Name}; no AI assistant is available for its foundation.";
            return;
        }

        OpenAiAssistant();
        var templateName = request.Template?.Name ?? "Blank page";
        var fileType = ActivePageFileType switch
        {
            HyperpagePageFileType.Html => ".html",
            HyperpagePageFileType.Cshtml => ".cshtml",
            _ => ".razor"
        };
        var pageContext = $"""
            page_id={_session.Page.PageId}
            page_name={_session.Page.Name}
            page_route={_session.Page.Route}
            page_file_type={fileType}
            starter_template={templateName}
            current_revision={_session.Page.Revision}
            """;
        if (!_aiAssistant.PrepareHyperpagePageConversation(
                _session.RootPath,
                pageContext,
                request.Description,
                out var error))
        {
            StatusMessage =
                $"Created {_session.Page.Name}; its AI foundation is waiting: {error}";
            return;
        }

        if (!_aiAssistant.CanSend)
        {
            StatusMessage = string.IsNullOrWhiteSpace(
                _aiAssistant.ComposerBlockerMessage)
                ? $"Created {_session.Page.Name}; its AI request is ready in the assistant."
                : $"Created {_session.Page.Name}; its AI request is ready. "
                  + _aiAssistant.ComposerBlockerMessage;
            return;
        }

        StatusMessage =
            $"Created {_session.Page.Name}; the AI assistant is establishing its foundation.";
        await _aiAssistant.SendPreparedPromptAsync();
    }

    private void ApplyPageMetadata()
    {
        if (ApplyMutation(new VisualPageMutationSet(
                _session.Page.Revision,
                [new VisualPageMutationOperation
                {
                    Kind = VisualPageMutationKind.SetPageMetadata,
                    PageMetadata = new VisualPagePageMetadata
                    {
                        Name = PageNameEditor,
                        Route = PageRouteEditor,
                        Slug = PageSlugEditor,
                        Title = PageTitleEditor,
                        Description = PageDescriptionEditor
                    }
                }],
                "page-tool",
                "page metadata")))
        {
            _archiveSlug = _session.Page.Slug;
            OnPropertyChanged(nameof(ArchiveSlug));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(ProjectPages));
        }
    }

    public async Task ImportAssetFileAsync(
        string sourcePath,
        string? name = null,
        string? altText = null)
    {
        IsBusy = true;
        try
        {
            var asset = await _projectService.ImportAssetAsync(
                _session,
                sourcePath,
                string.IsNullOrWhiteSpace(name)
                    ? Path.GetFileNameWithoutExtension(sourcePath)
                    : name,
                altText ?? string.Empty);
            ProjectAssets.Add(asset);
            SelectedAsset = asset;
            RefreshCanvasAssetSources();
            InvalidateCanvas();
            ClearPublicationPreparation();
            NotifyDirtyState();
            StatusMessage = $"Imported {asset.DisplayName}.";
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException)
        {
            StatusMessage = $"Asset import stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportMarkupFileAsync(string sourcePath)
    {
        var info = new FileInfo(Path.GetFullPath(sourcePath));
        if (!info.Exists || info.Length is <= 0 or > 2 * 1024 * 1024)
        {
            StatusMessage = "HTML/Razor imports must be non-empty files no larger than 2 MB.";
            return;
        }
        try
        {
            var content = await File.ReadAllTextAsync(info.FullName);
            var result = _importService.Import(
                content,
                info.Extension.Equals(".razor", StringComparison.OrdinalIgnoreCase));
            var parent = ResolveInsertionParent();
            if (ApplyMutation(new VisualPageMutationSet(
                    _session.Page.Revision,
                    [new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.InsertChild,
                        ParentElementId = parent.ElementId,
                        Element = result.Root
                    }],
                    "import-tool",
                    info.Name)))
            {
                StatusMessage = result.ContainsCustomCode
                    ? $"Imported {info.Name}; unsupported constructs were preserved as reviewed custom-code nodes."
                    : $"Imported {info.Name} into {parent.Name}.";
            }
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException or System.Text.Json.JsonException)
        {
            StatusMessage = $"Markup import stopped: {exception.Message}";
        }
    }

    private void ApplyAssetAltText()
    {
        if (SelectedAsset is null)
        {
            return;
        }
        SelectedAsset.AltText = AssetAltText.Trim();
        _session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
            _session.Project);
        ClearPublicationPreparation();
        NotifyDirtyState();
        StatusMessage = $"Updated metadata for {Path.GetFileName(SelectedAsset.RelativePath)}.";
    }

    private void UseSelectedAsset()
    {
        if (SelectedAsset is null || SelectedElement is null)
        {
            return;
        }
        var descriptor = _catalog.GetRequired(SelectedElement.DescriptorId);
        var attributeName = descriptor.AllowedAttributes.Contains(
            "src",
            StringComparer.OrdinalIgnoreCase)
            ? "src"
            : descriptor.AllowedAttributes.Contains(
                "poster",
                StringComparer.OrdinalIgnoreCase)
                ? "poster"
                : string.Empty;
        if (string.IsNullOrEmpty(attributeName))
        {
            StatusMessage = "Select an image or media element before using an asset.";
            return;
        }
        var operations = new List<VisualPageMutationOperation>
        {
            new()
            {
                Kind = VisualPageMutationKind.SetAttribute,
                TargetElementId = SelectedElement.ElementId,
                Name = attributeName,
                Value = SelectedAsset.RelativePath.Replace('\\', '/')
            }
        };
        if (descriptor.RequiresAltText && !string.IsNullOrWhiteSpace(SelectedAsset.AltText))
        {
            operations.Add(new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetAttribute,
                TargetElementId = SelectedElement.ElementId,
                Name = "alt",
                Value = SelectedAsset.AltText
            });
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            operations,
            "assets-tool",
            SelectedAsset.RelativePath));
    }

    private void AddSelectedElement()
    {
        if (SelectedDescriptor is null)
        {
            return;
        }

        if (UsesUnifiedAuthoringCatalog)
        {
            CatalogElementCommitRequested?.Invoke(SelectedDescriptor.Id);
            return;
        }

        var element = IsCatalogElementDraftActive
                      && string.Equals(
                          _catalogElementDraft?.DescriptorId,
                          SelectedDescriptor.Id,
                          StringComparison.Ordinal)
            ? VisualPageJson.Clone(_catalogElementDraft!)
            : _catalog.CreateElementForAuthoring(SelectedDescriptor.Id);
        AddElementToSelected(SelectedDescriptor, element);
    }

    private bool CanAddDescriptorToSelected(string descriptorId)
    {
        if (!_catalog.TryGet(descriptorId, out var descriptor))
        {
            return false;
        }
        var parent = ResolveInsertionParent();
        var parentDescriptor = _catalog.GetRequired(parent.DescriptorId);
        return descriptor.Authoring.AllowedParentMarkupNames.Count == 0
               || descriptor.Authoring.AllowedParentMarkupNames.Contains(
                   parentDescriptor.MarkupName,
                   StringComparer.OrdinalIgnoreCase);
    }

    public void AddDescriptorToSelected(string? descriptorId)
    {
        if (string.IsNullOrWhiteSpace(descriptorId)
            || !_catalog.TryGet(descriptorId, out var descriptor))
        {
            return;
        }
        AddElementToSelected(
            descriptor,
            _catalog.CreateElementForAuthoring(descriptor.Id));
    }

    private void AddElementToSelected(
        VisualPageComponentDescriptor descriptor,
        VisualPageElement element)
    {
        var parent = ResolveInsertionParent();
        var parentDescriptor = _catalog.GetRequired(parent.DescriptorId);
        if (descriptor.Authoring.AllowedParentMarkupNames.Count > 0
            && !descriptor.Authoring.AllowedParentMarkupNames.Contains(
                parentDescriptor.MarkupName,
                StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = $"{descriptor.DisplayName} cannot be added here. "
                            + descriptor.Authoring.PlacementGuidance;
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.InsertChild,
                ParentElementId = parent.ElementId,
                Element = element
            }],
            "elements-tool",
            $"add {descriptor.Id}"));
    }

    private VisualPageElement ResolveInsertionParent()
    {
        if (SelectedElement is null)
        {
            return _session.Page.Root;
        }
        if (_catalog.GetRequired(SelectedElement.DescriptorId).AllowsChildren)
        {
            return SelectedElement;
        }
        return FindParent(_session.Page.Root, SelectedElement.ElementId)
               ?? _session.Page.Root;
    }

    private bool CanUseSelectedTemplate() =>
        SelectedTemplate is not null
        && !IsBusy
        && (!IsSelectedTemplatePage
            || !HasUnsavedChanges
            && !string.IsNullOrWhiteSpace(TemplatePageName)
            && !string.IsNullOrWhiteSpace(TemplatePageRoute));

    private async Task UseSelectedTemplateAsync()
    {
        if (IsSelectedTemplatePage)
        {
            await CreatePageFromSelectedTemplateAsync();
            return;
        }
        await InsertSelectedTemplateAsync();
    }

    private async Task InsertSelectedTemplateAsync()
    {
        if (SelectedTemplate is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var parent = ResolveInsertionParent();
            var element = await InstantiateTemplateWithAssetsAsync(
                SelectedTemplate);
            ApplyMutation(new VisualPageMutationSet(
                _session.Page.Revision,
                [new VisualPageMutationOperation
                {
                    Kind = VisualPageMutationKind.InsertChild,
                    ParentElementId = parent.ElementId,
                    Element = element
                }],
                "templates-tool",
                SelectedTemplate.TemplateId));
            RefreshCanvasAssetSources();
            StatusMessage = $"Inserted {SelectedTemplate.StorageScope} {SelectedTemplate.Scope} template {SelectedTemplate.Name}.";
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException)
        {
            StatusMessage = $"Template insertion stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreatePageFromSelectedTemplateAsync()
    {
        if (SelectedTemplate is null || !IsSelectedTemplatePage)
        {
            return;
        }
        if (HasUnsavedChanges)
        {
            StatusMessage = "Save the active page before creating a page from a starter.";
            return;
        }
        IsBusy = true;
        try
        {
            var template = SelectedTemplate;
            var root = await InstantiateTemplateWithAssetsAsync(template);
            if (!root.DescriptorId.Equals("core.page", StringComparison.Ordinal))
            {
                throw new VisualPageValidationException(
                    "A page starter must contain a complete page root.");
            }
            _session = await _projectService.AddPageAsync(
                _session,
                TemplatePageName,
                TemplatePageRoute,
                starterRoot: root,
                themeTokens: template.ThemeTokens,
                title: string.IsNullOrWhiteSpace(template.SuggestedPageTitle)
                    ? TemplatePageName
                    : template.SuggestedPageTitle,
                description: template.SuggestedPageDescription);
            _savedProjectRevision = _session.Project.Revision;
            _savedPageRevision = _session.Page.Revision;
            _selectedProjectPage = _session.Project.Pages.Single(page =>
                page.PageId == _session.Page.PageId);
            OnPropertyChanged(nameof(SelectedProjectPage));
            OnPropertyChanged(nameof(ProjectPages));
            _undo.Clear();
            _redo.Clear();
            ClearPublicationPreparation();
            _archiveSlug = _session.Page.Slug;
            OnPropertyChanged(nameof(ArchiveSlug));
            SynchronizeProjectAssets();
            await LoadUserSourcesAsync(CancellationToken.None);
            RefreshDocumentState(_session.Page.Root.ElementId);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            StatusMessage = $"Created {_session.Page.Name} from {template.Name}. Atlas theme tokens and responsive structure are ready to edit.";
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException or VisualPageStaleRevisionException)
        {
            StatusMessage = $"Page starter stopped: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<VisualPageElement> InstantiateTemplateWithAssetsAsync(
        VisualPageTemplateDocument template)
    {
        var assetRemapping = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var assetId in template.PackagedAssets.Keys)
        {
            var sourcePath = _templateService.ResolvePackagedAssetPath(
                template,
                assetId);
            var hash = VisualPageHashing.Sha256File(sourcePath);
            var projectAsset = _session.Project.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Sha256, hash, StringComparison.OrdinalIgnoreCase));
            if (projectAsset is null)
            {
                projectAsset = await _projectService.ImportAssetAsync(
                    _session,
                    sourcePath);
                ProjectAssets.Add(projectAsset);
            }
            assetRemapping[assetId] = projectAsset.RelativePath.Replace(
                '\\',
                '/');
        }
        return _templateService.Instantiate(template, assetRemapping);
    }

    private void ApplySelectedText()
    {
        var inspected = InspectorElement;
        if (inspected is null)
        {
            return;
        }
        if (IsCatalogElementDraftActive)
        {
            inspected.Text = SelectedTextEditor;
            OnPropertyChanged(nameof(InspectorElementMarkup));
            StatusMessage =
                $"Updated the <{InspectorElementDescriptor!.MarkupName}> draft. Add it when ready.";
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetText,
                TargetElementId = inspected.ElementId,
                Value = SelectedTextEditor
            }],
            "inspector",
            "set text"));
    }

    private void SetSelectedAttribute()
    {
        var kind = SelectedAttributeDefinition?.EditorKind ==
                   VisualPageAttributeEditorKind.Boolean
                   && AttributeValue.Equals(
                       "false",
                       StringComparison.OrdinalIgnoreCase)
            ? VisualPageMutationKind.RemoveAttribute
            : VisualPageMutationKind.SetAttribute;
        SetSelectedDictionaryValue(
            kind,
            AttributeName,
            kind == VisualPageMutationKind.RemoveAttribute
                ? null
                : AttributeValue);
    }

    private void RemoveSelectedAttribute() => SetSelectedDictionaryValue(
        VisualPageMutationKind.RemoveAttribute,
        AttributeName,
        null);

    private bool CanSetSelectedAttribute() =>
        InspectorElement is not null
        && !string.IsNullOrWhiteSpace(AttributeName);

    private bool CanRemoveSelectedAttribute() =>
        InspectorElement?.Attributes.ContainsKey(AttributeName) == true;

    private void SetSelectedStyle() => SetSelectedDictionaryValue(
        VisualPageMutationKind.SetStyle,
        StyleName,
        StyleValue);

    private void RemoveSelectedStyle() => SetSelectedDictionaryValue(
        VisualPageMutationKind.RemoveStyle,
        StyleName,
        null);

    private bool CanSetSelectedStyle() =>
        InspectorElement is not null
        && !string.IsNullOrWhiteSpace(StyleName)
        && !string.IsNullOrWhiteSpace(StyleValue);

    private bool CanRemoveSelectedStyle() =>
        InspectorElement?.Styles.ContainsKey(StyleName) == true;

    private void SetSelectedDictionaryValue(
        VisualPageMutationKind kind,
        string name,
        string? value)
    {
        var inspected = InspectorElement;
        if (inspected is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        if (IsCatalogElementDraftActive)
        {
            var normalizedName = name.Trim();
            var dictionary = kind is VisualPageMutationKind.SetStyle
                or VisualPageMutationKind.RemoveStyle
                ? inspected.Styles
                : inspected.Attributes;
            if (kind is VisualPageMutationKind.RemoveAttribute
                or VisualPageMutationKind.RemoveStyle)
            {
                dictionary.Remove(normalizedName);
            }
            else
            {
                dictionary[normalizedName] = value ?? string.Empty;
            }
            LoadInspectorEditors();
            RaiseInspectorElementPropertiesChanged();
            StatusMessage =
                $"Updated the <{InspectorElementDescriptor!.MarkupName}> draft. Add it when ready.";
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = kind,
                TargetElementId = inspected.ElementId,
                Name = name,
                Value = value
            }],
            "inspector",
            name));
    }

    private void ApplySelectedLayout()
    {
        if (SelectedElement is null)
        {
            return;
        }
        if (!TryParseGridSpan(LayoutColumnSpan, "column", out var columnSpan)
            || !TryParseGridSpan(LayoutRowSpan, "row", out var rowSpan))
        {
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetLayout,
                TargetElementId = SelectedElement.ElementId,
                Breakpoint = ActiveBreakpoint,
                Layout = new VisualPageLayoutSpec
                {
                    Display = LayoutDisplay,
                    FlexDirection = LayoutDirection,
                    FlexWrap = LayoutWrap,
                    GridTemplateColumns = LayoutColumns,
                    Gap = LayoutGap,
                    Padding = LayoutPadding,
                    Margin = LayoutMargin,
                    FlexBasis = LayoutFlexBasis,
                    ColumnSpan = columnSpan,
                    RowSpan = rowSpan,
                    MinWidth = LayoutMinWidth,
                    MaxWidth = LayoutMaxWidth,
                    MinHeight = LayoutMinHeight,
                    MaxHeight = LayoutMaxHeight,
                    AlignItems = LayoutAlignItems,
                    JustifyContent = LayoutJustifyContent,
                    AlignSelf = LayoutAlignSelf
                }
            }],
            "layout-tool",
            ActiveBreakpoint.ToString()));
    }

    private bool TryParseGridSpan(
        string value,
        string label,
        out int? span)
    {
        span = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 1 or > 12)
        {
            StatusMessage = $"Grid {label} span must be empty or between 1 and 12.";
            return false;
        }
        span = parsed;
        return true;
    }

    private void AddState()
    {
        if (string.IsNullOrWhiteSpace(StateName)
            || string.IsNullOrWhiteSpace(StateType))
        {
            StatusMessage = "State requires a C# name and type.";
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.AddState,
                State = new VisualPageStateDefinition
                {
                    Name = StateName,
                    TypeName = StateType,
                    InitialValue = string.IsNullOrWhiteSpace(StateInitialValue)
                        ? "default!"
                        : StateInitialValue,
                    Kind = StateKind
                }
            }],
            "state-tool",
            StateName));
    }

    private void AddEvent()
    {
        if (SelectedElement is null || string.IsNullOrWhiteSpace(EventHandlerName))
        {
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetEvent,
                TargetElementId = SelectedElement.ElementId,
                Event = new VisualPageEventBinding
                {
                    Kind = EventKind,
                    HandlerName = EventHandlerName
                }
            }],
            "events-tool",
            EventHandlerName));
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(VisualPageJson.Clone(_session.Page));
        _session.Page = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RefreshDocumentState(_session.Page.Root.ElementId);
        StatusMessage = "Undid the last Hyperpage operation.";
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(VisualPageJson.Clone(_session.Page));
        _session.Page = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RefreshDocumentState(_session.Page.Root.ElementId);
        StatusMessage = "Redid the Hyperpage operation.";
    }

    private void CopySelected()
    {
        if (SelectedElement is null)
        {
            return;
        }
        _clipboard = VisualPageJson.Clone(SelectedElement);
        _clipboardIsCut = false;
        StatusMessage = $"Copied {SelectedElement.Name}.";
        PasteCommand.RaiseCanExecuteChanged();
    }

    private void CutSelected()
    {
        if (!CanMutateSelectedNonRoot())
        {
            return;
        }
        _clipboard = VisualPageJson.Clone(SelectedElement!);
        _clipboardIsCut = true;
        DeleteSelected();
        StatusMessage = $"Cut {_clipboard.Name}.";
        PasteCommand.RaiseCanExecuteChanged();
    }

    private void PasteIntoSelected()
    {
        if (_clipboard is null)
        {
            return;
        }
        var parent = ResolveInsertionParent();
        var element = VisualPageJson.Clone(_clipboard);
        VisualPageMutationEngine.RemapElementIds(element);
        if (ApplyMutation(new VisualPageMutationSet(
                _session.Page.Revision,
                [new VisualPageMutationOperation
                {
                    Kind = VisualPageMutationKind.InsertChild,
                    ParentElementId = parent.ElementId,
                    Element = element
                }],
                "clipboard",
                _clipboardIsCut ? "move copied subtree" : "paste copy"))
            && _clipboardIsCut)
        {
            _clipboard = null;
            _clipboardIsCut = false;
            PasteCommand.RaiseCanExecuteChanged();
        }
    }

    private void DuplicateSelected() => ApplySelectedStructural(
        VisualPageMutationKind.DuplicateElement);

    private void DeleteSelected() => ApplySelectedStructural(
        VisualPageMutationKind.RemoveElement);

    private void WrapSelected()
    {
        if (!CanMutateSelectedNonRoot())
        {
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.WrapElement,
                TargetElementId = SelectedElement!.ElementId,
                Element = _catalog.CreateElement("core.container")
            }],
            "context-menu",
            "wrap"));
    }

    private void UnwrapSelected() => ApplySelectedStructural(
        VisualPageMutationKind.UnwrapElement);

    private void ToggleLock()
    {
        if (SelectedElement is null)
        {
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetLocked,
                TargetElementId = SelectedElement.ElementId,
                Flag = !SelectedElement.IsLocked
            }],
            "context-menu",
            "toggle lock"));
    }

    private void ToggleHidden()
    {
        if (SelectedElement is null)
        {
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetHidden,
                TargetElementId = SelectedElement.ElementId,
                Flag = !SelectedElement.IsHidden
            }],
            "context-menu",
            "toggle hidden"));
    }

    private bool CanMoveSelectedSibling(int delta)
    {
        if (!CanMutateSelectedNonRoot()
            || FindParent(_session.Page.Root, SelectedElement!.ElementId) is not { } parent)
        {
            return false;
        }
        var index = parent.Children.FindIndex(child =>
            child.ElementId == SelectedElement.ElementId);
        return index >= 0 && index + delta >= 0 && index + delta < parent.Children.Count;
    }

    private void MoveSelectedSibling(int delta)
    {
        if (!CanMoveSelectedSibling(delta)
            || FindParent(_session.Page.Root, SelectedElement!.ElementId) is not { } parent)
        {
            return;
        }
        var index = parent.Children.FindIndex(child =>
            child.ElementId == SelectedElement.ElementId);
        MoveElement(SelectedElement.ElementId, parent.ElementId, index + delta);
    }

    private void SelectParent()
    {
        if (SelectedElement is not null
            && FindParent(_session.Page.Root, SelectedElement.ElementId) is { } parent)
        {
            SelectElement(parent.ElementId);
            StatusMessage = $"Selected parent {parent.Name}.";
        }
    }

    private void OpenSelectedElementInspector()
    {
        if (SelectedElement is null)
        {
            return;
        }
        SelectedTool = StudioTools.FirstOrDefault(tool => tool.Id == "inspector");
        StatusMessage = $"Editing {SelectedElement.Name} properties.";
    }

    private void ApplySelectedStructural(VisualPageMutationKind kind)
    {
        if (!CanMutateSelectedNonRoot())
        {
            return;
        }
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = kind,
                TargetElementId = SelectedElement!.ElementId
            }],
            "context-menu",
            kind.ToString()));
    }

    private bool CanMutateSelectedNonRoot() =>
        SelectedElement is not null
        && SelectedElement.ElementId != _session.Page.Root.ElementId;

    private bool CanCreateTemplate() =>
        SelectedElement is not null
        && !IsBusy
        && !string.IsNullOrWhiteSpace(TemplateNameEditor);

    private void RefreshDocumentState(string? selectElementId)
    {
        LoadPageMetadataEditors();
        RootTree.Clear();
        RootTree.Add(new VisualPageTreeNodeViewModel(_session.Page.Root));
        _selectedDiagnostic = null;
        OnPropertyChanged(nameof(SelectedDiagnostic));
        Diagnostics.Clear();
        foreach (var diagnostic in _validator.ValidatePage(_session.Page))
        {
            Diagnostics.Add(diagnostic);
        }
        RefreshGeneratedSource();
        OnPropertyChanged(nameof(DocumentJson));
        OnPropertyChanged(nameof(StylesheetPaths));
        OnPropertyChanged(nameof(CanvasStylesheetUrls));
        OnPropertyChanged(nameof(CanvasFoundationStylesheetUrls));
        SelectElement(selectElementId ?? _session.Page.Root.ElementId);
        NotifyDirtyState();
        RefreshCommandState();
    }

    private void RefreshGeneratedSource()
    {
        try
        {
            var generated = _generator.Generate(_session.Project, _session.Page);
            GeneratedRazor = generated.Artifacts.Single(value => value.RelativePath.Equals(
                VisualPageProjectService.GeneratedMarkupPath(Project, Document),
                StringComparison.OrdinalIgnoreCase)).Content;
            GeneratedCss = generated.Artifacts.FirstOrDefault(value =>
                value.RelativePath.EndsWith(
                    ActivePageFileType == HyperpagePageFileType.RazorPage ? ".razor.css" : ".generated.css",
                    StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
            GeneratedCSharp = generated.Artifacts.FirstOrDefault(value =>
                value.RelativePath.EndsWith(
                    ".designer.g.cs",
                    StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        }
        catch (VisualPageValidationException exception)
        {
            StatusMessage = $"Generation stopped: {exception.Message}";
        }
    }

    private void RefreshElementCatalog()
    {
        if (_isRefreshingElementCatalog) return;
        _isRefreshingElementCatalog = true;
        try { RefreshElementCatalogCore(); }
        finally { _isRefreshingElementCatalog = false; }
        OnPropertyChanged(nameof(SelectedElementCategory));
    }

    private void RefreshElementCatalogCore()
    {
        var selectedId = SelectedDescriptor?.Id;
        var searchTerms = ElementSearchText.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scopeMatches = GetElementScopeCandidates(SelectedElementScope)
            .ToList();
        var candidates = searchTerms.Length > 0
            ? ElementDescriptors
            : scopeMatches;
        var categories = candidates
            .Select(value => value.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var categoryItems = new[] { "All categories" }.Concat(categories).ToArray();
        if (!ElementCategories.SequenceEqual(categoryItems, StringComparer.Ordinal))
        {
            ElementCategories.Clear();
            foreach (var availableCategory in categoryItems)
                ElementCategories.Add(availableCategory);
        }
        if (!ElementCategories.Contains(
                _selectedElementCategory,
                StringComparer.Ordinal))
        {
            _selectedElementCategory = "All categories";
            OnPropertyChanged(nameof(SelectedElementCategory));
        }
        var category = SelectedElementCategory;
        var exactSearch = ElementSearchText.Trim().Trim('<', '>');
        var matches = candidates.Where(descriptor =>
            (category == "All categories"
             || descriptor.Category.Equals(category, StringComparison.Ordinal))
            && searchTerms.All(term => descriptor.SearchText.Contains(
                term,
                StringComparison.OrdinalIgnoreCase)))
            .OrderBy(descriptor => searchTerms.Length > 0
                                   && (descriptor.MarkupName.Equals(
                                           exactSearch,
                                           StringComparison.OrdinalIgnoreCase)
                                       || descriptor.DisplayName.Equals(
                                           ElementSearchText.Trim(),
                                           StringComparison.OrdinalIgnoreCase))
                ? 0
                : 1)
            .ThenBy(descriptor => descriptor.Category, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal);
        FilteredElementDescriptors.Clear();
        foreach (var descriptor in matches)
        {
            FilteredElementDescriptors.Add(descriptor);
        }
        SelectedDescriptor = FilteredElementDescriptors.FirstOrDefault(value =>
                                 value.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                             ?? FilteredElementDescriptors.FirstOrDefault();
        OnPropertyChanged(nameof(ElementCatalogSummary));
    }

    private void RefreshTemplateFilter()
    {
        var selectedId = SelectedTemplate?.TemplateId;
        var searchTerms = TemplateSearchText.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var categoryCandidates = Templates.Where(template =>
            (SelectedTemplateScopeFilter == "All types"
             || SelectedTemplateScopeFilter == "Pages" && template.Scope == "page"
             || SelectedTemplateScopeFilter == "Sections" && template.Scope == "section"
             || SelectedTemplateScopeFilter == "Elements" && template.Scope == "element")
            && (SelectedTemplateLibraryFilter == "All libraries"
                || TemplateLibraryName(template).Equals(
                    SelectedTemplateLibraryFilter,
                    StringComparison.Ordinal)));
        var availableCategoryGroups = categoryCandidates
            .Select(template => template.CategoryGroupLabel)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        TemplateCategoryFilters.Clear();
        TemplateCategoryFilters.Add(VisualPageTemplateTaxonomy.AllCategories);
        foreach (var categoryGroup in availableCategoryGroups)
        {
            TemplateCategoryFilters.Add(categoryGroup);
        }
        if (!TemplateCategoryFilters.Contains(
                _selectedTemplateCategoryFilter,
                StringComparer.Ordinal))
        {
            _selectedTemplateCategoryFilter =
                VisualPageTemplateTaxonomy.AllCategories;
            OnPropertyChanged(nameof(SelectedTemplateCategoryFilter));
        }
        var matches = Templates.Where(template =>
                (SelectedTemplateScopeFilter == "All types"
                 || SelectedTemplateScopeFilter == "Pages" && template.Scope == "page"
                 || SelectedTemplateScopeFilter == "Sections" && template.Scope == "section"
                 || SelectedTemplateScopeFilter == "Elements" && template.Scope == "element")
                && (SelectedTemplateLibraryFilter == "All libraries"
                    || TemplateLibraryName(template).Equals(
                        SelectedTemplateLibraryFilter,
                        StringComparison.Ordinal))
                && (SelectedTemplateCategoryFilter ==
                        VisualPageTemplateTaxonomy.AllCategories
                    || template.CategoryGroupLabel.Equals(
                        SelectedTemplateCategoryFilter,
                        StringComparison.Ordinal))
                && searchTerms.All(term => template.SearchText.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderBy(template => template.Scope switch
            {
                "page" => 0,
                "section" => 1,
                _ => 2
            })
            .ThenBy(template => template.Scope == "page"
                ? Templates.IndexOf(template)
                : int.MaxValue)
            .ThenBy(template => TemplateLibraryName(template), StringComparer.Ordinal)
            .ThenBy(template => template.CategoryGroupLabel, StringComparer.Ordinal)
            .ThenBy(template => template.Category, StringComparer.Ordinal)
            .ThenBy(template => template.Name, StringComparer.Ordinal);
        FilteredTemplates.Clear();
        foreach (var template in matches)
        {
            FilteredTemplates.Add(template);
        }
        SelectedTemplate = FilteredTemplates.FirstOrDefault(template =>
                               template.TemplateId.Equals(
                                   selectedId,
                                   StringComparison.Ordinal))
                           ?? FilteredTemplates.FirstOrDefault();
        OnPropertyChanged(nameof(TemplateCatalogSummary));
    }

    private static string TemplateLibraryName(
        VisualPageTemplateDocument template) => template.StorageScope switch
        {
            "project" => "This project",
            "personal" => "My templates",
            _ => template.Library
        };

    private IEnumerable<VisualPageComponentDescriptor>
        GetElementScopeCandidates(string scope) => scope switch
        {
            "All HTML" => _catalog.CanonicalStandardHtmlElements,
            "Input controls" => ElementDescriptors.Where(descriptor =>
                descriptor.Category.Equals(
                    "Input controls",
                    StringComparison.OrdinalIgnoreCase)
                || descriptor.Category is "Forms" or "Validation"
                ),
            "Blazor & logic" => ElementDescriptors.Where(descriptor =>
                !descriptor.IsStandardHtmlElement
                && !descriptor.IsObsolete
                && descriptor.Category is
                    "Forms" or "Validation" or "Logic" or "Custom code"),
            "Atlas patterns" => ElementDescriptors.Where(descriptor =>
                descriptor.Id.StartsWith("atlas.", StringComparison.OrdinalIgnoreCase)),
            "Legacy HTML" => ElementDescriptors.Where(descriptor =>
                descriptor.IsObsolete),
            "Everything" => ElementDescriptors,
            _ => ElementDescriptors
                .Where(descriptor => descriptor.Authoring.IsCommon
                                     && descriptor.Id != "core.page")
                .GroupBy(
                    descriptor => descriptor.IsStandardHtmlElement
                        ? $"html:{descriptor.MarkupName}"
                        : $"object:{descriptor.Id}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(descriptor => descriptor.Id.StartsWith(
                        "core.",
                        StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(descriptor => descriptor.Id.Equals(
                        $"html.{descriptor.MarkupName}",
                        StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal)
                    .First())
        };

    private void ActivateCatalogElementDraft(
        VisualPageComponentDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            DeactivateCatalogElementDraft(loadEditors: true);
            return;
        }

        _catalogElementDraft = _catalog.CreateElementForAuthoring(descriptor.Id);
        _isCatalogElementDraftActive = true;
        SelectedTool = StudioTools.FirstOrDefault(tool => string.Equals(
            tool.Id,
            "inspector",
            StringComparison.Ordinal));
        RaiseInspectorElementPropertiesChanged();
        LoadInspectorEditors();
        RefreshCommandState();
        StatusMessage =
            $"Configure <{descriptor.MarkupName}> in Properties, then add it to the current selection.";
    }

    private void DeactivateCatalogElementDraft(bool loadEditors)
    {
        if (!IsCatalogElementDraftActive)
        {
            return;
        }

        _catalogElementDraft = null;
        _isCatalogElementDraftActive = false;
        RaiseInspectorElementPropertiesChanged();
        if (loadEditors)
        {
            LoadInspectorEditors();
            RefreshCommandState();
        }
    }

    private void RaiseInspectorElementPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsCatalogElementDraftActive));
        OnPropertyChanged(nameof(IsPlacedElementInspector));
        OnPropertyChanged(nameof(InspectorElementTitle));
        OnPropertyChanged(nameof(InspectorElementSummary));
        OnPropertyChanged(nameof(InspectorElementDescription));
        OnPropertyChanged(nameof(InspectorElementMarkup));
        OnPropertyChanged(nameof(InspectorElementBadge));
        OnPropertyChanged(nameof(InspectorElementPlacementGuidance));
        OnPropertyChanged(nameof(InspectorElementChildrenGuidance));
        OnPropertyChanged(nameof(InspectorElementCanvasGuidance));
        OnPropertyChanged(nameof(InspectorElementWarning));
        OnPropertyChanged(nameof(HasInspectorElementWarning));
        OnPropertyChanged(nameof(InspectorElementTextEditorLabel));
        OnPropertyChanged(nameof(InspectorElementSupportsText));
        OnPropertyChanged(nameof(InspectorElementDoesNotSupportText));
    }

    private string BuildInspectorMarkupPreview()
    {
        var element = InspectorElement;
        var descriptor = InspectorElementDescriptor;
        if (element is null || descriptor is null)
        {
            return string.Empty;
        }

        var attributes = new List<string>();
        foreach (var pair in element.Attributes.OrderBy(
                     value => value.Key,
                     StringComparer.Ordinal))
        {
            if (_catalog.TryGetAttributeDefinition(
                    descriptor,
                    pair.Key,
                    out var definition)
                && definition.EditorKind == VisualPageAttributeEditorKind.Boolean)
            {
                if (!pair.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    attributes.Add(pair.Key);
                }
                continue;
            }
            attributes.Add(
                $"{pair.Key}=\"{System.Net.WebUtility.HtmlEncode(pair.Value)}\"");
        }

        var opening = attributes.Count == 0
            ? $"<{descriptor.MarkupName}"
            : $"<{descriptor.MarkupName} {string.Join(" ", attributes)}";
        if (descriptor.IsVoid)
        {
            return opening + " />";
        }

        return opening + ">"
               + System.Net.WebUtility.HtmlEncode(element.Text)
               + $"</{descriptor.MarkupName}>";
    }

    private void LoadInspectorEditors()
    {
        var inspected = InspectorElement;
        SelectedTextEditor = inspected?.Text ?? string.Empty;
        AvailableAttributes.Clear();
        SelectedElementAttributes.Clear();
        SelectedElementStyles.Clear();
        if (inspected is not null)
        {
            foreach (var definition in _catalog.GetAttributeDefinitions(
                         inspected.DescriptorId))
            {
                AvailableAttributes.Add(definition);
            }
            foreach (var attribute in inspected.Attributes.OrderBy(
                         value => value.Key,
                         StringComparer.Ordinal))
            {
                if (!AvailableAttributes.Any(value => value.Name.Equals(
                        attribute.Key,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    AvailableAttributes.Add(VisualPageAttributeCatalog.Resolve(
                        attribute.Key,
                        VisualPageAttributeCatalog.IsGlobalName(attribute.Key)));
                }
                SelectedElementAttributes.Add(new VisualPagePropertyEntry(
                    attribute.Key,
                    attribute.Value));
            }
            foreach (var style in inspected.Styles.OrderBy(
                         value => value.Key,
                         StringComparer.Ordinal))
            {
                SelectedElementStyles.Add(new VisualPagePropertyEntry(
                    style.Key,
                    style.Value));
            }
        }
        _selectedAttributeDefinition = null;
        _selectedAttributeEntry = null;
        _selectedStyleEntry = null;
        OnPropertyChanged(nameof(SelectedAttributeDefinition));
        OnPropertyChanged(nameof(SelectedAttributeEntry));
        OnPropertyChanged(nameof(SelectedStyleEntry));
        OnPropertyChanged(nameof(AttributeValueSuggestions));
        OnPropertyChanged(nameof(SelectedAttributeHelp));
        OnPropertyChanged(nameof(UsesBooleanAttributeEditor));
        OnPropertyChanged(nameof(UsesChoiceAttributeEditor));
        OnPropertyChanged(nameof(UsesTextAttributeEditor));
        OnPropertyChanged(nameof(AttributeChoiceIsEditable));
        OnPropertyChanged(nameof(AttributeEditorKindLabel));
        OnPropertyChanged(nameof(AttributeBooleanValue));
        AttributeName = string.Empty;
        AttributeValue = string.Empty;
        StyleName = string.Empty;
        StyleValue = string.Empty;
        LoadLayoutEditor();
    }

    private void LoadPageMetadataEditors()
    {
        PageNameEditor = _session.Page.Name;
        PageRouteEditor = _session.Page.Route;
        PageSlugEditor = _session.Page.Slug;
        PageTitleEditor = _session.Page.Title;
        PageDescriptionEditor = _session.Page.Description;
    }

    private void SynchronizeProjectAssets()
    {
        ProjectAssets.Clear();
        foreach (var asset in _session.Project.Assets)
        {
            ProjectAssets.Add(asset);
        }
        SelectedAsset = null;
        RefreshCanvasAssetSources();
    }

    private void RefreshCanvasAssetSources()
    {
        const long maximumAssetPreviewBytes = 4L * 1024 * 1024;
        const long maximumTotalPreviewBytes = 16L * 1024 * 1024;
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var asset in _session.Project.Assets)
        {
            if (asset.MediaType is not
                ("image/png" or "image/jpeg" or "image/gif" or
                 "image/webp" or "image/avif"))
            {
                continue;
            }
            try
            {
                var path = VisualPageProjectService.ResolveProjectPath(
                    _session.RootPath,
                    asset.RelativePath);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > maximumAssetPreviewBytes
                    || total + info.Length > maximumTotalPreviewBytes
                    || !string.Equals(
                        VisualPageHashing.Sha256File(path),
                        asset.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                sources[asset.RelativePath.Replace('\\', '/')] =
                    $"data:{asset.MediaType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
                total += info.Length;
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException
                or VisualPageValidationException)
            {
                _ = exception;
            }
        }
        _canvasAssetSources = sources;
    }

    private void LoadLayoutEditor()
    {
        var layout = SelectedElement is not null
                     && SelectedElement.Layout.TryGetValue(ActiveBreakpoint, out var found)
            ? found
            : new VisualPageLayoutSpec();
        LayoutDisplay = layout.Display;
        LayoutDirection = layout.FlexDirection;
        LayoutWrap = layout.FlexWrap;
        LayoutColumns = layout.GridTemplateColumns;
        LayoutGap = layout.Gap;
        LayoutPadding = layout.Padding;
        LayoutMargin = layout.Margin;
        LayoutFlexBasis = layout.FlexBasis;
        LayoutColumnSpan = layout.ColumnSpan?.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty;
        LayoutRowSpan = layout.RowSpan?.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty;
        LayoutMinWidth = layout.MinWidth;
        LayoutMaxWidth = layout.MaxWidth;
        LayoutMinHeight = layout.MinHeight;
        LayoutMaxHeight = layout.MaxHeight;
        LayoutAlignItems = layout.AlignItems;
        LayoutJustifyContent = layout.JustifyContent;
        LayoutAlignSelf = layout.AlignSelf;
    }

    private void ResetCanvasView()
    {
        _canvasZoomPercent = 100;
        _canvasViewportWidth = "Auto";
        _showCanvasGrid = false;
        _showCanvasOutlines = false;
        OnPropertyChanged(nameof(CanvasZoomPercent));
        OnPropertyChanged(nameof(CanvasZoomLabel));
        OnPropertyChanged(nameof(CanvasViewportWidth));
        OnPropertyChanged(nameof(ShowCanvasGrid));
        OnPropertyChanged(nameof(ShowCanvasOutlines));
        InvalidateCanvas();
    }

    private void ToggleToolPanel(bool left)
    {
        if (left)
        {
            _layout.IsLeftToolPanelOpen = !_layout.IsLeftToolPanelOpen;
        }
        else
        {
            _layout.IsRightToolPanelOpen = !_layout.IsRightToolPanelOpen;
        }
        _layout.IsProjectOverride = true;
        _session.Project.LayoutOverride = VisualPageJson.Clone(_layout);
        _session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
            _session.Project);
        ClearPublicationPreparation();
        RaiseToolPanelStateChanged();
        NotifyDirtyState();
        StatusMessage = left
            ? _layout.IsLeftToolPanelOpen
                ? "Opened the left Studio tools."
                : "Collapsed the left Studio tools."
            : _layout.IsRightToolPanelOpen
                ? "Opened the right Studio tools."
                : "Collapsed the right Studio tools.";
    }

    private void ToggleAiAssistant()
    {
        if (!HasAiAssistant)
        {
            StatusMessage =
                "No AI assistant conversation is attached to this Hyperpage project tab.";
            return;
        }
        IsAiAssistantOpen = !IsAiAssistantOpen;
        StatusMessage = IsAiAssistantOpen
            ? "The project AI assistant is open below the canvas."
            : "The project AI assistant is collapsed.";
    }

    public void OpenAiAssistant()
    {
        if (HasAiAssistant)
        {
            IsAiAssistantOpen = true;
        }
    }

    private void RaiseToolPanelStateChanged()
    {
        OnPropertyChanged(nameof(IsCanvasOnlyWorkspace));
        OnPropertyChanged(nameof(IsDocumentOnlyWorkspace));
        OnPropertyChanged(nameof(IsCodeRightWorkspace));
        OnPropertyChanged(nameof(IsCodeBelowWorkspace));
        OnPropertyChanged(nameof(IsLeftToolPanelOpen));
        OnPropertyChanged(nameof(IsLeftToolPanelClosed));
        OnPropertyChanged(nameof(IsRightToolPanelOpen));
        OnPropertyChanged(nameof(IsRightToolPanelClosed));
        OnPropertyChanged(nameof(LeftToolPanelWidth));
        OnPropertyChanged(nameof(LeftToolPanelSplitterWidth));
        OnPropertyChanged(nameof(RightToolPanelWidth));
        OnPropertyChanged(nameof(RightToolPanelSplitterWidth));
    }

    private void SetCanvasOnlyLayout()
    {
        _layout = CreateLayoutSnapshot(
            "Canvas",
            StudioLayoutNode.CreateDefault());
        CommitLayoutChange();
        StatusMessage = "Showing the canvas only.";
    }

    private void SetDocumentOnlyLayout()
    {
        var root = VisualPageJson.Clone(Layout.Root);
        var codePane = EnumeratePanes(root).FirstOrDefault(IsCodePane)
                       ?? CreateCodePane("generated-razor");
        var activeContent = codePane.PaneContents.Contains(
            codePane.ActiveContentId,
            StringComparer.Ordinal)
            ? codePane.ActiveContentId
            : codePane.PaneContents[0];
        codePane.ActiveContentId = activeContent;
        _layout = CreateLayoutSnapshot("Documents", codePane);
        CommitLayoutChange();
        PaneRequested?.Invoke(this, activeContent);
        StatusMessage = "Showing documents only.";
    }

    private void ResetLayout()
    {
        _layout = VisualPageProjectService.CreateDefaultStudioLayout();
        CommitLayoutChange();
        StatusMessage = "Restored the default canvas workspace.";
    }

    private void SplitCanvas(StudioSplitOrientation orientation)
    {
        var root = VisualPageJson.Clone(Layout.Root);
        var codePane = EnumeratePanes(root).FirstOrDefault(IsCodePane);
        if (codePane is not null)
        {
            var activeContent = codePane.PaneContents.Contains(
                codePane.ActiveContentId,
                StringComparer.Ordinal)
                ? codePane.ActiveContentId
                : codePane.PaneContents[0];
            var split = FindCanvasCodeSplit(root);
            if (split is null && !ContainsPaneContent(root, "canvas"))
            {
                root = CreateCanvasCodeSplit(orientation, codePane);
                _layout = CreateLayoutSnapshot(
                    orientation == StudioSplitOrientation.Horizontal
                        ? "Code beside canvas"
                        : "Code below canvas",
                    root);
                CommitLayoutChange();
                PaneRequested?.Invoke(this, activeContent);
                StatusMessage = orientation == StudioSplitOrientation.Horizontal
                    ? "The existing document pane is beside the canvas."
                    : "The existing document pane is below the canvas.";
                return;
            }
            if (split is not null && split.Orientation != orientation)
            {
                split.Orientation = orientation;
                _layout = CreateLayoutSnapshot(
                    orientation == StudioSplitOrientation.Horizontal
                        ? "Code beside canvas"
                        : "Code below canvas",
                    root);
                CommitLayoutChange();
            }
            PaneRequested?.Invoke(this, activeContent);
            StatusMessage = orientation == StudioSplitOrientation.Horizontal
                ? "The existing code pane is beside the canvas."
                : "The existing code pane is below the canvas.";
            return;
        }

        root = SplitFirstCanvasPane(
            root,
            orientation,
            "generated-razor",
            out var splitApplied);
        if (!splitApplied)
        {
            root = CreateCanvasCodeSplit(
                orientation,
                "generated-razor");
        }
        _layout = CreateLayoutSnapshot(
            orientation == StudioSplitOrientation.Horizontal
                ? "Code beside canvas"
                : "Code below canvas",
            root);
        CommitLayoutChange();
        PaneRequested?.Invoke(this, "generated-razor");
    }

    private static StudioLayoutNode SplitFirstCanvasPane(
        StudioLayoutNode node,
        StudioSplitOrientation orientation,
        string contentId,
        out bool splitApplied)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane
            && node.PaneContents.Contains("canvas", StringComparer.Ordinal))
        {
            splitApplied = true;
            return new StudioLayoutNode
            {
                Kind = StudioLayoutNodeKind.Split,
                Orientation = orientation,
                FirstProportion = 0.6,
                First = node,
                Second = CreateCodePane(contentId)
            };
        }

        if (node.Kind == StudioLayoutNodeKind.Split)
        {
            if (node.First is not null)
            {
                node.First = SplitFirstCanvasPane(
                    node.First,
                    orientation,
                    contentId,
                    out splitApplied);
                if (splitApplied)
                {
                    return node;
                }
            }
            if (node.Second is not null)
            {
                node.Second = SplitFirstCanvasPane(
                    node.Second,
                    orientation,
                    contentId,
                    out splitApplied);
                return node;
            }
        }

        splitApplied = false;
        return node;
    }

    private static StudioLayoutNode CreateCodePane(string contentId) => new()
    {
        Kind = StudioLayoutNodeKind.Pane,
        PaneContents = [contentId],
        ActiveContentId = contentId
    };

    private static StudioLayoutNode CreateCanvasCodeSplit(
        StudioSplitOrientation orientation,
        string contentId) => new()
        {
            Kind = StudioLayoutNodeKind.Split,
            Orientation = orientation,
            FirstProportion = 0.6,
            First = StudioLayoutNode.CreateDefault(),
            Second = CreateCodePane(contentId)
        };

    private static StudioLayoutNode CreateCanvasCodeSplit(
        StudioSplitOrientation orientation,
        StudioLayoutNode codePane) => new()
        {
            Kind = StudioLayoutNodeKind.Split,
            Orientation = orientation,
            FirstProportion = 0.6,
            First = StudioLayoutNode.CreateDefault(),
            Second = codePane
        };

    private StudioLayoutSnapshot CreateLayoutSnapshot(
        string name,
        StudioLayoutNode root) => new()
        {
            Name = name,
            Root = root,
            ToolPlacements = VisualPageJson.Clone(_layout).ToolPlacements,
            IsLeftToolPanelOpen = _layout.IsLeftToolPanelOpen,
            IsRightToolPanelOpen = _layout.IsRightToolPanelOpen
        };

    private static bool IsCodePane(StudioLayoutNode node) =>
        node.Kind == StudioLayoutNodeKind.Pane
        && node.PaneContents.Count > 0
        && !node.PaneContents.Contains("canvas", StringComparer.Ordinal);

    private static StudioLayoutNode? FindCanvasCodeSplit(
        StudioLayoutNode node)
    {
        if (node.Kind != StudioLayoutNodeKind.Split)
        {
            return null;
        }
        if (node.First is not null
            && node.Second is not null
            && ((ContainsPaneContent(node.First, "canvas")
                 && ContainsCodePane(node.Second))
                || (ContainsPaneContent(node.Second, "canvas")
                    && ContainsCodePane(node.First))))
        {
            return node;
        }
        return node.First is null
            ? node.Second is null ? null : FindCanvasCodeSplit(node.Second)
            : FindCanvasCodeSplit(node.First)
              ?? (node.Second is null
                  ? null
                  : FindCanvasCodeSplit(node.Second));
    }

    private static bool ContainsPaneContent(
        StudioLayoutNode node,
        string contentId) => EnumeratePanes(node).Any(pane =>
            pane.PaneContents.Contains(contentId, StringComparer.Ordinal));

    private static bool ContainsCodePane(StudioLayoutNode node) =>
        EnumeratePanes(node).Any(IsCodePane);

    private static StudioLayoutSnapshot NormalizeLayoutSnapshot(
        StudioLayoutSnapshot snapshot)
    {
        // An explicitly empty final pane is a valid workspace after its last tab
        // closes. Preserve it across saving and reopening instead of resurrecting Canvas.
        if (snapshot.Root is { Kind: StudioLayoutNodeKind.Pane, PaneContents.Count: 0 } empty)
        {
            empty.ActiveContentId = string.Empty;
            return snapshot;
        }
        var seenContents = new HashSet<string>(StringComparer.Ordinal);
        var root = NormalizeLayoutNode(snapshot.Root, seenContents);
        snapshot.Root = root ?? StudioLayoutNode.CreateDefault();
        return snapshot;
    }

    private static StudioLayoutNode? NormalizeLayoutNode(
        StudioLayoutNode? node,
        ISet<string> seenContents)
    {
        if (node is null)
        {
            return null;
        }
        if (node.Kind == StudioLayoutNodeKind.Pane)
        {
            node.PaneContents = node.PaneContents
                .Where(content => !string.IsNullOrWhiteSpace(content)
                                  && seenContents.Add(content))
                .ToList();
            if (node.PaneContents.Count == 0)
            {
                return null;
            }
            if (!node.PaneContents.Contains(
                    node.ActiveContentId,
                    StringComparer.Ordinal))
            {
                node.ActiveContentId = node.PaneContents[0];
            }
            return node;
        }
        node.First = NormalizeLayoutNode(node.First, seenContents);
        node.Second = NormalizeLayoutNode(node.Second, seenContents);
        if (node.First is null)
        {
            return node.Second;
        }
        if (node.Second is null)
        {
            return node.First;
        }
        return node;
    }

    private static StudioLayoutNode? RemovePane(
        StudioLayoutNode node,
        string nodeId,
        out bool removed)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane)
        {
            removed = string.Equals(
                          node.NodeId,
                          nodeId,
                          StringComparison.Ordinal)
                      && !node.PaneContents.Contains(
                          "canvas",
                          StringComparer.Ordinal);
            return removed ? null : node;
        }
        var removedFirst = false;
        if (node.First is not null)
        {
            node.First = RemovePane(
                node.First,
                nodeId,
                out removedFirst);
        }
        if (removedFirst)
        {
            removed = true;
            return node.First is null ? node.Second
                : node.Second is null ? node.First : node;
        }
        var removedSecond = false;
        if (node.Second is not null)
        {
            node.Second = RemovePane(
                node.Second,
                nodeId,
                out removedSecond);
        }
        removed = removedSecond;
        if (node.First is null)
        {
            return node.Second;
        }
        if (node.Second is null)
        {
            return node.First;
        }
        return node;
    }

    private static IEnumerable<StudioLayoutNode> EnumeratePanes(
        StudioLayoutNode node)
    {
        if (node.Kind == StudioLayoutNodeKind.Pane)
        {
            yield return node;
            yield break;
        }
        if (node.First is not null)
        {
            foreach (var pane in EnumeratePanes(node.First))
            {
                yield return pane;
            }
        }
        if (node.Second is not null)
        {
            foreach (var pane in EnumeratePanes(node.Second))
            {
                yield return pane;
            }
        }
    }

    private static IEnumerable<StudioLayoutNode> EnumerateLayoutNodes(
        StudioLayoutNode node)
    {
        yield return node;
        if (node.First is not null)
        {
            foreach (var child in EnumerateLayoutNodes(node.First))
            {
                yield return child;
            }
        }
        if (node.Second is not null)
        {
            foreach (var child in EnumerateLayoutNodes(node.Second))
            {
                yield return child;
            }
        }
    }

    private void InvalidateCanvas()
    {
        OnPropertyChanged(nameof(CanvasHtml));
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCommandState()
    {
        (AddElementCommand as RelayCommand)?.RaiseCanExecuteChanged();
        AddDescriptorCommand.RaiseCanExecuteChanged();
        (SetAttributeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveAttributeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SetStyleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RemoveStyleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        CutCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        WrapCommand.RaiseCanExecuteChanged();
        UnwrapCommand.RaiseCanExecuteChanged();
        ToggleLockCommand.RaiseCanExecuteChanged();
        ToggleHiddenCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        SelectParentCommand.RaiseCanExecuteChanged();
        OpenInspectorCommand.RaiseCanExecuteChanged();
        (ApplyTextCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AddEventCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (QuickAddBindingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (InsertTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ConvertToTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ConvertToPersonalTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void NotifyDirtyState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(CanCreatePage));
        (CreatePageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (InsertTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private static bool SourceChanged(
        VisualPageUserSourceSnapshot? snapshot,
        string content) => snapshot is not null
                           && !string.Equals(
                               snapshot.Sha256,
                               VisualPageHashing.Sha256Text(content),
                               StringComparison.Ordinal);

    private static string FindDefaultAtlasCheckout(string projectRoot)
    {
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(projectRoot));
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return string.Empty;
        }
        var candidate = Path.Combine(driveRoot, "dev", "AtlasUniversalis");
        return Directory.Exists(candidate) ? candidate : string.Empty;
    }

    private static VisualPageElement? Find(
        VisualPageElement root,
        string elementId)
    {
        if (root.ElementId == elementId)
        {
            return root;
        }
        foreach (var child in root.Children)
        {
            var found = Find(child, elementId);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static VisualPageElement? FindParent(
        VisualPageElement root,
        string elementId)
    {
        if (root.Children.Any(child => child.ElementId == elementId))
        {
            return root;
        }
        foreach (var child in root.Children)
        {
            var parent = FindParent(child, elementId);
            if (parent is not null)
            {
                return parent;
            }
        }
        return null;
    }

    private static VisualPageLayoutSpec? ResolveEffectiveLayout(
        VisualPageElement element,
        VisualPageBreakpoint breakpoint)
    {
        VisualPageLayoutSpec? result = null;
        foreach (var candidate in Enum.GetValues<VisualPageBreakpoint>()
                     .Where(value => value <= breakpoint)
                     .OrderBy(value => value))
        {
            if (element.Layout.TryGetValue(candidate, out var layout))
            {
                result = layout;
            }
        }
        return result;
    }

    private static bool TryPercent(string value, out double percentage)
    {
        percentage = 0;
        return value.EndsWith('%')
               && double.TryParse(
                   value[..^1],
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out percentage);
    }

    private void QuickAddBinding()
    {
        if (SelectedElement is null)
        {
            return;
        }
        var expression = _session.Page.State.FirstOrDefault()?.Name ?? "true";
        var kind = _session.Page.State.Count == 0
            ? VisualPageBindingKind.Visibility
            : VisualPageBindingKind.Text;
        ApplyMutation(new VisualPageMutationSet(
            _session.Page.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.SetBinding,
                TargetElementId = SelectedElement.ElementId,
                Binding = new VisualPageBinding
                {
                    Kind = kind,
                    Expression = expression
                }
            }],
            "context-menu",
            "quick binding"));
        SelectedTool = StudioTools.FirstOrDefault(tool =>
            tool.Id == "state-bindings");
    }

    private async Task ConvertSelectedToTemplateAsync(bool personal) =>
        await SaveSelectedAsTemplateAsync(
            TemplateNameEditor,
            TemplateDescriptionEditor,
            personal);

    public string SelectedElementTemplateMarkup => SelectedElement is null
        ? string.Empty
        : RenderTemplateMarkup(SelectedElement);

    public HyperpageTemplateSourceAnalysis AnalyzeTemplateCode(
        string source,
        string scope,
        string templateName)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new VisualPageValidationException(
                "Paste HTML or Razor markup before analyzing the template.");
        }

        var import = _importService.Import(source, razor: true);
        var normalizedScope = NormalizeTemplateScope(scope);
        var root = NormalizeImportedTemplateRoot(
            import.Root,
            normalizedScope,
            templateName);
        var warningCount = import.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == VisualPageDiagnosticSeverity.Warning);
        var summary = $"Converted {EnumerateElements(root).Count()} semantic element(s)"
                      + (warningCount == 0
                          ? "."
                          : $" with {warningCount} review warning(s).")
                      + (import.ContainsCustomCode
                          ? " Unsupported Razor remains an explicit custom-code node."
                          : string.Empty);
        return new HyperpageTemplateSourceAnalysis(
            RenderTemplateMarkup(root),
            summary);
    }

    public async Task<bool> CreateTemplateFromCodeAsync(
        string source,
        string templateName,
        string description,
        string scope,
        bool personal)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(templateName))
        {
            StatusMessage = "Template creation requires a name and an idle Studio.";
            return false;
        }

        IsBusy = true;
        try
        {
            var import = _importService.Import(source, razor: true);
            var normalizedScope = NormalizeTemplateScope(scope);
            var root = NormalizeImportedTemplateRoot(
                import.Root,
                normalizedScope,
                templateName);
            await SaveTemplateCoreAsync(
                root,
                normalizedScope,
                templateName.Trim(),
                string.IsNullOrWhiteSpace(description)
                    ? $"Reusable {normalizedScope} template created from code."
                    : description.Trim(),
                personal);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException)
        {
            StatusMessage = $"Template creation stopped: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SaveSelectedAsTemplateAsync(
        string templateName,
        string description,
        bool personal)
    {
        if (SelectedElement is null
            || IsBusy
            || string.IsNullOrWhiteSpace(templateName))
        {
            StatusMessage = SelectedElement is null
                ? "Select an element before saving a template."
                : "Template creation requires a name and an idle Studio.";
            return false;
        }

        IsBusy = true;
        try
        {
            var root = VisualPageJson.Clone(SelectedElement);
            var scope = ResolveTemplateScope(root);
            await SaveTemplateCoreAsync(
                root,
                scope,
                templateName.Trim(),
                string.IsNullOrWhiteSpace(description)
                    ? $"Reusable {scope} template created from {SelectedElement.Name}."
                    : description.Trim(),
                personal);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException
            or VisualPageValidationException)
        {
            StatusMessage = $"Template creation stopped: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveTemplateCoreAsync(
        VisualPageElement root,
        string scope,
        string templateName,
        string templateDescription,
        bool personal)
    {
        var descriptor = _catalog.GetRequired(root.DescriptorId);
        var template = new VisualPageTemplateDocument
        {
            Name = templateName,
            Description = templateDescription,
            Scope = scope,
            StorageScope = personal ? "personal" : "project",
            Library = personal ? "My templates" : "This project",
            CategoryGroup = VisualPageTemplateTaxonomy.ResolveCategoryGroup(
                descriptor.Category,
                scope),
            Category = descriptor.Category,
            UsageHint = scope == "page"
                ? "Create a new page from this saved starter."
                : "Insert this saved object at the selected location.",
            Tags =
            [
                descriptor.MarkupName,
                descriptor.Category.ToLowerInvariant(),
                scope,
                "saved"
            ],
            SuggestedPageName = scope == "page"
                ? _session.Page.Name
                : string.Empty,
            SuggestedPageRoute = scope == "page"
                ? _session.Page.Route
                : string.Empty,
            SuggestedPageTitle = scope == "page"
                ? _session.Page.Title
                : string.Empty,
            SuggestedPageDescription = scope == "page"
                ? _session.Page.Description
                : string.Empty,
            ThemeTokens = scope == "page"
                ? new Dictionary<string, string>(
                    _session.Project.Theme.Tokens,
                    StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            Root = root,
            Dependencies = EnumerateElements(root)
                .Select(element => _catalog.GetRequired(element.DescriptorId))
                .Where(value => value.Classification != VisualPageCodeClassification.Declarative)
                .Select(value => value.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList()
        };
        var packagedAssetSources = CollectTemplateAssets(
            template,
            replaceWithStableReferences: personal);
        if (personal)
        {
            await _templateService.SavePersonalAsync(
                template,
                packagedAssetSources);
        }
        else
        {
            var relativePath = $"Templates/{VisualPageProjectService.MakeSlug(template.Name)}-{template.TemplateId[..8]}.visual-page-template.json";
            await _templateService.SaveAsync(
                VisualPageProjectService.ResolveProjectPath(
                    _session.RootPath,
                    relativePath),
                template);
            _session.Project.Templates.Add(new VisualPageTemplateReference
            {
                TemplateId = template.TemplateId,
                Name = template.Name,
                Scope = template.Scope,
                RelativePath = relativePath,
                AssetIds = template.AssetIds,
                Dependencies = template.Dependencies
            });
            _session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
                _session.Project);
            ClearPublicationPreparation();
            NotifyDirtyState();
        }
        Templates.Add(template);
        TemplateSearchText = string.Empty;
        SelectedTemplateLibraryFilter = personal
            ? "My templates"
            : "This project";
        SelectedTemplateScopeFilter = scope switch
        {
            "page" => "Pages",
            "section" => "Sections",
            _ => "Elements"
        };
        RefreshTemplateFilter();
        SelectedTemplate = template;
        StatusMessage = personal
            ? $"Saved device-personal {scope} template {template.Name} with {template.AssetIds.Count} remapped asset(s)."
            : $"Created project {scope} template {template.Name}. Save the project to keep its catalog entry.";
    }

    public async Task<string> StartWebTemplateCaptureAsync(string sourceUrl)
    {
        if (_aiAssistant is null)
        {
            StatusMessage =
                "Website template creation requires an available AI assistant.";
            return StatusMessage;
        }
        if (!Uri.TryCreate(
                sourceUrl.Trim(),
                UriKind.Absolute,
                out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            StatusMessage =
                "Website templates require a public HTTPS URL without credentials or a custom port.";
            return StatusMessage;
        }

        var host = uri.Host.StartsWith(
                "www.",
                StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
        var siteName = host.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Website";
        siteName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            siteName.Replace('-', ' ').Replace('_', ' '));
        WebTemplateUrl = uri.AbsoluteUri;
        WebTemplateName = $"{siteName} starter";
        WebTemplateDescription =
            $"Reusable semantic structure derived from {host}.";
        WebTemplateScope = "page";
        WebTemplateCategoryGroup = "Navigation";
        WebTemplateCategory = "Landing pages";
        WebTemplatePermissionConfirmed = true;

        var firstNewMessage = _aiAssistant.Messages.Count;
        await BeginWebTemplateCaptureAsync();
        var output = _aiAssistant.Messages
            .Skip(firstNewMessage)
            .Where(message => message.IsAssistant || message.IsError)
            .Select(message => message.Text.Trim())
            .Where(message => message.Length > 0)
            .LastOrDefault();
        if (_aiAssistant.PendingReview is { } review)
        {
            var reviewOutput = $"{review.Title}{Environment.NewLine}{review.Summary}{Environment.NewLine}{review.Preview}";
            output = string.IsNullOrWhiteSpace(output)
                ? reviewOutput
                : output + Environment.NewLine + Environment.NewLine + reviewOutput;
        }
        return string.IsNullOrWhiteSpace(output)
            ? StatusMessage
            : output;
    }

    private bool CanPrepareWebTemplateCapture() =>
        !IsBusy
        && HasAiAssistant
        && WebTemplatePermissionConfirmed
        && !string.IsNullOrWhiteSpace(WebTemplateUrl)
        && !string.IsNullOrWhiteSpace(WebTemplateName)
        && !string.IsNullOrWhiteSpace(WebTemplateDescription)
        && WebTemplateScope is "page" or "section" or "element"
        && VisualPageCommonTemplateCatalog.CategoryGroups.Contains(
            WebTemplateCategoryGroup,
            StringComparer.Ordinal)
        && !string.IsNullOrWhiteSpace(WebTemplateCategory);

    private async Task BeginWebTemplateCaptureAsync()
    {
        if (!CanPrepareWebTemplateCapture() || _aiAssistant is null)
        {
            StatusMessage = IsBusy
                ? "Wait for the current Studio or AI work cycle before creating a website template."
                : "Website template creation needs an available AI model and a valid confirmed HTTPS request.";
            return;
        }
        if (!Uri.TryCreate(
                WebTemplateUrl.Trim(),
                UriKind.Absolute,
                out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            StatusMessage =
                "Website template capture requires a public HTTPS URL without credentials or a custom port.";
            return;
        }

        IsAiAssistantOpen = true;
        if (!_aiAssistant.PrepareWebTemplateCaptureConversation(
                _session.RootPath,
                uri.AbsoluteUri,
                WebTemplateName,
                WebTemplateDescription,
                WebTemplateScope,
                WebTemplateCategoryGroup,
                WebTemplateCategory,
                out var error))
        {
            StatusMessage = $"Website template capture stopped: {error}";
            return;
        }

        if (!_aiAssistant.CanSend)
        {
            StatusMessage = string.IsNullOrWhiteSpace(
                _aiAssistant.ComposerBlockerMessage)
                ? "The website-template request is ready in the AI panel. Choose a model and Context, then send it."
                : "The website-template request is ready in the AI panel. "
                  + _aiAssistant.ComposerBlockerMessage;
            return;
        }

        StatusMessage =
            "The selected AI model is preparing a reviewed structure-only website template.";
        await _aiAssistant.SendPreparedPromptAsync();
    }

    private IReadOnlyDictionary<string, string> CollectTemplateAssets(
        VisualPageTemplateDocument template,
        bool replaceWithStableReferences)
    {
        var assetsByPath = _session.Project.Assets.ToDictionary(
            asset => asset.RelativePath.Replace('\\', '/'),
            StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in EnumerateElements(template.Root))
        {
            foreach (var attribute in element.Attributes.ToList())
            {
                var normalized = attribute.Value.Replace('\\', '/');
                if (!assetsByPath.TryGetValue(normalized, out var asset))
                {
                    continue;
                }
                if (!template.AssetIds.Contains(asset.AssetId, StringComparer.Ordinal))
                {
                    template.AssetIds.Add(asset.AssetId);
                }
                sources[asset.AssetId] = VisualPageProjectService.ResolveProjectPath(
                    _session.RootPath,
                    asset.RelativePath);
                if (replaceWithStableReferences)
                {
                    element.Attributes[attribute.Key] = $"asset:{asset.AssetId}";
                }
            }
        }
        template.AssetIds.Sort(StringComparer.Ordinal);
        return sources;
    }

    private string ResolveTemplateScope(VisualPageElement root)
    {
        var descriptor = _catalog.GetRequired(root.DescriptorId);
        return root.ElementId == _session.Page.Root.ElementId
            || root.DescriptorId.Equals("core.page", StringComparison.Ordinal)
            ? "page"
            : descriptor.MarkupName.Equals(
                    "section",
                    StringComparison.OrdinalIgnoreCase)
              || descriptor.Id is "atlas.hero" or "atlas.card-grid"
                  or "atlas.cta" or "atlas.editorial"
                ? "section"
                : "element";
    }

    private static string NormalizeTemplateScope(string scope) =>
        scope.Trim().ToLowerInvariant() switch
        {
            "page" => "page",
            "section" => "section",
            _ => "element"
        };

    private VisualPageElement NormalizeImportedTemplateRoot(
        VisualPageElement imported,
        string scope,
        string templateName)
    {
        if (scope == "page")
        {
            if (imported.DescriptorId.Equals(
                    "core.page",
                    StringComparison.Ordinal))
            {
                imported.Name = templateName.Trim();
                return imported;
            }

            var page = _catalog.CreateElement("core.page");
            page.Name = templateName.Trim();
            page.Text = imported.Text;
            page.Children = imported.Children;
            return page;
        }

        if (string.IsNullOrWhiteSpace(imported.Text)
            && imported.Attributes.Count == 0
            && imported.Styles.Count == 0
            && imported.Children.Count == 1)
        {
            return imported.Children[0];
        }
        return imported;
    }

    private string TemplateElementLabel(VisualPageElement element)
    {
        var descriptor = _catalog.GetRequired(element.DescriptorId);
        return $"<{descriptor.MarkupName}>  {element.Name}";
    }

    private string BuildTemplateStructure(VisualPageElement root)
    {
        var builder = new StringBuilder();
        AppendTemplateStructure(builder, root, depth: 0);
        return builder.ToString().TrimEnd();
    }

    private void AppendTemplateStructure(
        StringBuilder builder,
        VisualPageElement element,
        int depth)
    {
        builder.Append(' ', depth * 2);
        builder.Append(TemplateElementLabel(element));
        builder.AppendLine();
        foreach (var child in element.Children)
        {
            AppendTemplateStructure(builder, child, depth + 1);
        }
    }

    private string RenderTemplateMarkup(VisualPageElement root)
    {
        var builder = new StringBuilder();
        AppendTemplateMarkup(builder, root, depth: 0);
        return builder.ToString().TrimEnd();
    }

    private void AppendTemplateMarkup(
        StringBuilder builder,
        VisualPageElement element,
        int depth)
    {
        if (!string.IsNullOrWhiteSpace(element.CustomRazor))
        {
            builder.Append(' ', depth * 2);
            builder.AppendLine(element.CustomRazor.Trim());
            return;
        }

        var descriptor = _catalog.GetRequired(element.DescriptorId);
        var attributes = new SortedDictionary<string, string>(
            element.Attributes,
            StringComparer.Ordinal);
        if (element.Styles.Count > 0)
        {
            attributes["style"] = string.Join(
                "; ",
                element.Styles.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}: {pair.Value}"));
        }
        var attributeText = attributes.Count == 0
            ? string.Empty
            : " " + string.Join(
                " ",
                attributes.Select(pair =>
                    $"{pair.Key}=\"{WebUtility.HtmlEncode(pair.Value)}\""));
        var indentation = new string(' ', depth * 2);
        if (descriptor.IsVoid)
        {
            builder.Append(indentation);
            builder.Append('<');
            builder.Append(descriptor.MarkupName);
            builder.Append(attributeText);
            builder.AppendLine(" />");
            return;
        }

        builder.Append(indentation);
        builder.Append('<');
        builder.Append(descriptor.MarkupName);
        builder.Append(attributeText);
        builder.Append('>');
        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            builder.Append(WebUtility.HtmlEncode(element.Text));
        }
        if (element.Children.Count > 0)
        {
            builder.AppendLine();
            foreach (var child in element.Children)
            {
                AppendTemplateMarkup(builder, child, depth + 1);
            }
            builder.Append(indentation);
        }
        builder.Append("</");
        builder.Append(descriptor.MarkupName);
        builder.AppendLine(">");
    }

    private static IEnumerable<VisualPageElement> EnumerateElements(
        VisualPageElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in EnumerateElements(child))
            {
                yield return descendant;
            }
        }
    }
}

public sealed record HyperpageTemplateSourceAnalysis(
    string Output,
    string Summary);

public sealed record VisualPagePropertyEntry(string Name, string Value)
{
    public string Summary => string.IsNullOrEmpty(Value)
        ? Name
        : $"{Name} = {Value}";
}

public sealed class VisualPageTreeNodeViewModel
{
    public VisualPageTreeNodeViewModel(VisualPageElement element)
    {
        Element = element;
        Children = element.Children
            .Select(child => new VisualPageTreeNodeViewModel(child))
            .ToList();
    }

    public VisualPageElement Element { get; }
    public string ElementId => Element.ElementId;
    public string Name => Element.Name;
    public string DescriptorId => Element.DescriptorId;
    public string DisplayName => Element.IsHidden
        ? $"{Name} (hidden)"
        : Element.IsLocked
            ? $"{Name} (locked)"
            : Name;
    public IReadOnlyList<VisualPageTreeNodeViewModel> Children { get; }
}
