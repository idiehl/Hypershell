using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Infrastructure;
using Hypershell.Services;

namespace Hypershell.ViewModels;

public sealed record HyperlibStudioWorkDesign(string WorkId, string Title, string TemplateTitle,
    long ContentRevision, TemplateDefinition Definition, IReadOnlyDictionary<string, JsonElement> Appearance,
    IReadOnlyDictionary<string, JsonElement> EffectiveAppearance);
public sealed record HyperlibStudioContentItem(HyperlibStudioNode Node, int Depth)
{
    public string Id => Node.Id;
    public string Title => Node.Title;
    public string Type => Node.Type;
    public bool IsPage => Type == "page";
    public string IndentedTitle => new string(' ', Depth * 3) + Title;
}
internal sealed record HyperlibStudioAppearanceDraft(HyperlibStudioWorkDesign Design,
    Dictionary<string, JsonElement> Values, Dictionary<string, string> Texts, Dictionary<string, string> Errors);

/// <summary>Native library navigation. Opening this model never starts a host or modifies content.</summary>
public sealed class HyperlibStudioWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly string _projectRoot;
    private readonly HyperlibStudioWorkspaceService _service;
    private readonly HyperlibStudioTemplateCatalogService _templateCatalog = new();
    private readonly bool _ownsService;
    private readonly CancellationTokenSource _lifetime = new();
    private LibraryState? _state;
    private IReadOnlyList<HyperlibStudioNode> _nodes = [];
    private HyperlibStudioContext? _context;
    private HyperlibStudioNode? _selectedWork;
    private HyperlibStudioNode? _selectedPage;
    private HyperlibStudioContentItem? _selectedWorkContent;
    private HyperlibStudioNode? _lastCreatedWork;
    private BaseTemplateInfo? _selectedStarter;
    private IReadOnlyList<HyperlibStudioTemplate> _allTemplates = [];
    private HyperlibStudioTemplate? _selectedTemplate;
    private string _templateSearchText = "";
    private string? _selectedParentId;
    private string _searchText = "";
    private string _draftTitle = "";
    private bool _isBusy;
    private string _statusMessage = "Choose a Template, Work, or Page.";
    private string? _errorMessage;
    private string _templateSummary = "";
    private HyperlibStudioPreview? _preview;
    private bool _disposed;
    private bool _rebuilding;
    private bool _hasUnsavedAppearanceChanges;
    private Func<string, CancellationToken, Task<Uri>>? _endpointFactory;

    public HyperlibStudioWorkspaceViewModel(string projectRoot,
        Func<string, CancellationToken, Task<Uri>>? endpointFactory = null,
        HyperlibStudioWorkspaceService? service = null)
    {
        _projectRoot = projectRoot;
        _service = service ?? new HyperlibStudioWorkspaceService();
        _ownsService = service is null;
        _endpointFactory = endpointFactory;
        Starters = new(BaseTemplateCatalog.List());
        _selectedStarter = Starters.FirstOrDefault(item => item.Id == BaseTemplateCatalog.DefaultStarterId);
        RefreshCommand = new(() => RefreshAsync(), () => !IsBusy);
        CreateWorkCommand = new(async () => { await CreateWorkAsync(SelectedParentId ?? WorkParents.FirstOrDefault()?.Id ?? "", DraftTitle, SelectedStarter?.Id ?? BaseTemplateCatalog.DefaultStarterId); },
            () => CanMutate && WorkParents.Count != 0 && !string.IsNullOrWhiteSpace(DraftTitle));
        CreatePageCommand = new(async () => { await CreatePageAsync(SelectedParentId is { } parent && PageParents.Any(item => item.Id == parent) ? parent : SelectedWork?.Id ?? "", DraftTitle); },
            () => CanMutate && SelectedWork is not null && !string.IsNullOrWhiteSpace(DraftTitle));
        RenameCommand = new(async () => { if ((SelectedPage ?? SelectedWork) is { } node) await RenameAsync(node.Id, DraftTitle); },
            () => CanMutate && (SelectedPage ?? SelectedWork) is not null && !string.IsNullOrWhiteSpace(DraftTitle));
        UndoCommand = new(async () => { await UndoAsync(); }, () => CanMutate);
        RedoCommand = new(async () => { await RedoAsync(); }, () => CanMutate);
        OpenWorkCommand = new(value => OpenWork(value as HyperlibStudioNode ?? SelectedWork), value => !IsBusy && (value as HyperlibStudioNode ?? SelectedWork)?.ProjectRoot is not null);
        OpenContentCommand = new(value => OpenContent(value as HyperlibStudioNode ?? SelectedPage),
            value => !IsBusy && HasLibraryContent && (value is HyperlibStudioNode || SelectedPage is not null));
        OpenSourceCommand = new(value => OpenSource(value as HyperlibStudioSource), value => !IsBusy && value is HyperlibStudioSource);
        EditTemplateCommand = new(() => EditTemplateRequested?.Invoke(), () => !IsBusy && Context is not null);
        PreviewCommand = new(async () => { await PreviewAsync(); }, () => CanMutate);
        OpenTemplateProjectCommand = new(async () =>
        {
            if (SelectedTemplate is not { } template) return;
            var root = await GetTemplateProjectAsync(template.Id);
            if (root is not null) OpenTemplateProjectRequested?.Invoke(root);
        }, () => CanManageTemplates && SelectedTemplate is not null);
    }

    public event Action<string>? OpenWorkProjectRequested;
    public event Action<string>? OpenContentRequested;
    public event Action<string>? OpenRelativeCodeRequested;
    public event Action? EditTemplateRequested;
    public event Action<HyperlibStudioPreview>? PreviewRequested;
    public event Action<string>? OpenTemplateProjectRequested;
    public event Action<string, string>? OpenTemplateSourceRequested;
    public event Action? DiscardAppearanceDraftsRequested;

    public Func<string, CancellationToken, Task<Uri>>? EndpointFactory
    { get => _endpointFactory; set { _endpointFactory = value; RaiseCommands(); } }
    public HyperlibStudioContext? Context { get => _context; private set => SetProperty(ref _context, value); }
    public ObservableCollection<HyperlibStudioNode> Works { get; } = [];
    public IReadOnlyList<HyperlibStudioNode> TemplateSourceWorks => _nodes.Where(item => item.Type == "work").ToArray();
    public ObservableCollection<HyperlibStudioNode> Pages { get; } = [];
    public ObservableCollection<HyperlibStudioContentItem> WorkContents { get; } = [];
    public ObservableCollection<HyperlibStudioNode> Collections { get; } = [];
    public ObservableCollection<HyperlibStudioNode> WorkParents { get; } = [];
    public ObservableCollection<HyperlibStudioNode> PageParents { get; } = [];
    public ObservableCollection<HyperlibStudioSourceGroup> SourceGroups { get; } = [];
    public ObservableCollection<BaseTemplateInfo> Starters { get; }
    public ObservableCollection<HyperlibStudioTemplate> Templates { get; } = [];
    public IReadOnlyList<HyperlibStudioTemplate> AvailableTemplates => _allTemplates;
    public HyperlibStudioTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (value is not null && !_allTemplates.Any(item => item.Id == value.Id)) return;
            if (SetProperty(ref _selectedTemplate, value)) RaiseCommands();
        }
    }
    public string TemplateSearchText
    {
        get => _templateSearchText;
        set { if (SetProperty(ref _templateSearchText, value ?? "")) RebuildTemplates(); }
    }
    public bool HasTemplates => Templates.Count != 0;
    public bool CanManageTemplates => !_disposed && !IsBusy && Context?.LibraryRoot is not null;
    public HyperlibStudioNode? LastCreatedWork { get => _lastCreatedWork; private set => SetProperty(ref _lastCreatedWork, value); }
    public HyperlibStudioNode? SelectedWork
    {
        get => _selectedWork;
        set
        {
            if (_rebuilding) return;
            if (value is not null && !_nodes.Any(item => item.Type == "work" && item.Id == value.Id)) return;
            if (!SetProperty(ref _selectedWork, value)) return;
            RebuildPages();
            OnPropertyChanged(nameof(HasSelectedWork));
            RaiseCommands();
        }
    }
    public HyperlibStudioNode? SelectedPage
    {
        get => _selectedPage;
        set { if (_rebuilding || value is not null && !Pages.Any(item => item.Id == value.Id)) return; if (SetProperty(ref _selectedPage, value)) RaiseCommands(); }
    }
    public HyperlibStudioContentItem? SelectedWorkContent
    {
        get => _selectedWorkContent;
        set
        {
            if (_rebuilding) return;
            var item = value is null ? null : WorkContents.FirstOrDefault(candidate => candidate.Id == value.Id);
            if (value is not null && item is null) return;
            if (!SetProperty(ref _selectedWorkContent, item)) return;
            if (item?.IsPage == true) SelectedPage = Pages.FirstOrDefault(page => page.Id == item.Id);
        }
    }
    public BaseTemplateInfo? SelectedStarter { get => _selectedStarter; set => SetProperty(ref _selectedStarter, value); }
    public string? SelectedParentId { get => _selectedParentId; set => SetProperty(ref _selectedParentId, value); }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value ?? "")) RebuildLists(); } }
    public string DraftTitle { get => _draftTitle; set { if (SetProperty(ref _draftTitle, value ?? "")) RaiseCommands(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommands(); } }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasUnsavedAppearanceChanges { get => _hasUnsavedAppearanceChanges; private set => SetProperty(ref _hasUnsavedAppearanceChanges, value); }
    internal Dictionary<string, HyperlibStudioAppearanceDraft> AppearanceDrafts { get; } = new(StringComparer.Ordinal);
    public void SetAppearanceDraftState(bool hasChanges) => HasUnsavedAppearanceChanges = hasChanges || AppearanceDrafts.Count != 0;
    public void DiscardAppearanceDrafts()
    { AppearanceDrafts.Clear(); DiscardAppearanceDraftsRequested?.Invoke(); HasUnsavedAppearanceChanges = false; }
    public bool IsLibraryRoot => Context?.IsLibraryRoot == true;
    public bool HasLibraryContent => Context?.HasLibraryContent == true;
    public bool HasWorks => Works.Count != 0;
    public bool HasPages => Pages.Count != 0;
    public bool HasWorkContents => WorkContents.Count != 0;
    public bool HasSelectedWork => SelectedWork is not null;
    public long Revision => _state?.Revision ?? 0;
    public string ScopeTitle => Context?.Title ?? "HyperLib";
    public string ScopeDescription => !HasLibraryContent ? Context?.ContentUnavailableReason ?? "Loading project…"
        : IsLibraryRoot ? "Templates supply reusable elements. Works organize Pages and define their structure and style. Write Pages in Studio."
        : "Organize this Work and define its Page structure and style. Write its Pages in Studio.";
    public string TemplateSummary { get => _templateSummary; private set => SetProperty(ref _templateSummary, value); }
    public bool CanMutate => !_disposed && !IsBusy && HasLibraryContent && EndpointFactory is not null;
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateWorkCommand { get; }
    public AsyncRelayCommand CreatePageCommand { get; }
    public AsyncRelayCommand RenameCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public AsyncRelayCommand RedoCommand { get; }
    public RelayCommand OpenWorkCommand { get; }
    public RelayCommand OpenContentCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public RelayCommand EditTemplateCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand OpenTemplateProjectCommand { get; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);
    public void ReportExternalError(string message) { ErrorMessage = message; StatusMessage = "The action could not be completed."; }
    public void ClearError() => ErrorMessage = null;
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(async token =>
        {
            var snapshot = await _service.ReadAsync(_projectRoot, token);
            Context = snapshot.Context;
            _state = snapshot.State;
            _nodes = snapshot.Nodes;
            Replace(SourceGroups, snapshot.SourceGroups);
            await LoadTemplateSummaryAsync(token);
            await LoadTemplatesAsync(token);
            RebuildLists();
            NotifyContext();
            StatusMessage = HasLibraryContent ? $"Content revision {Revision}." : "Independent template and source project.";
            return true;
        }, cancellationToken);
    }

    public Task<bool> CreateWorkAsync(string parentId, string title, string starterId, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            RequireState();
            if (!WorkParents.Any(item => item.Id == parentId)) throw new InvalidOperationException("Choose a collection or series for the new Work.");
            ValidateTitle(title);
            if (!_allTemplates.Any(item => item.Id == starterId)) throw new InvalidOperationException("Choose a listed template.");
            var workId = Guid.NewGuid().ToString("N");
            var pageId = Guid.NewGuid().ToString("N");
            var batch = new OperationBatch { ExpectedRevision = Revision, Description = "Create Work: " + title.Trim(), Operations =
            [new() { Kind = "createNode", Id = workId, ParentId = parentId, NodeType = "work", Title = title.Trim(), StarterId = starterId },
             new() { Kind = "createNode", Id = pageId, ParentId = workId, NodeType = "page", Title = "Home" }] };
            await ApplyAsync(batch, token);
            LastCreatedWork = _service.BuildNodes(Context! with { IsLibraryRoot = true }, _state).First(item => item.Id == workId);
            if (IsLibraryRoot) SearchText = "";
            SelectedWork = Works.FirstOrDefault(item => item.Id == workId) ?? SelectedWork;
            SelectedPage = Pages.FirstOrDefault(item => item.Id == pageId);
            StatusMessage = "Work created with its first Page. Organize and design the Work here; write Pages in Studio.";
            return true;
        }, cancellationToken);

    public Task<bool> CreatePageAsync(string parentId, string title, CancellationToken cancellationToken = default)
        => CreatePageAsync(parentId, title, false, cancellationToken);

    public Task<bool> CreatePageAsync(string parentId, string title, bool blank, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            RequireState();
            if (!PageParents.Any(item => item.Id == parentId)) throw new InvalidOperationException("Choose a location in the current Work.");
            ValidateTitle(title);
            var pageId = Guid.NewGuid().ToString("N");
            PageDocument? page = null;
            if (blank)
            {
                var workRoot = SelectedWork?.ProjectRoot ?? throw new InvalidOperationException("Open this Work's source project first.");
                var snapshot = await _service.ReadAsync(workRoot, token);
                var template = await HyperlibStudioAuthoringService.LoadAsync(workRoot, snapshot.Context.AuthoringRelativePath);
                page = new PageDocument { TemplateId = template.Definition.TemplateId, UsesEditableChrome = true };
            }
            await ApplyAsync(new OperationBatch { ExpectedRevision = Revision, Description = "Create page: " + title.Trim(), Operations =
                [new() { Kind = "createNode", Id = pageId, ParentId = parentId, NodeType = "page", Title = title.Trim(), Page = page }] }, token);
            SearchText = "";
            SelectedPage = Pages.FirstOrDefault(item => item.Id == pageId);
            StatusMessage = blank ? "Blank Page created. Write it in Studio." : "Page created from this Work's structure. Write it in Studio.";
            return true;
        }, cancellationToken);

    public IReadOnlyList<HyperlibStudioNode> GetContainerParents(string type)
    {
        var parentTypes = type switch
        {
            "volume" => new[] { "work" },
            "part" => new[] { "work", "volume" },
            "chapter" => new[] { "work", "volume", "part" },
            _ => []
        };
        return _nodes.Where(node => node.WorkId == SelectedWork?.Id && parentTypes.Contains(node.Type, StringComparer.Ordinal)).ToArray();
    }

    /// <summary>Creates one organizational container, with no Page document or presentation source change.</summary>
    public Task<bool> CreateContainerAsync(string parentId, string type, string title, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            RequireState();
            if (type is not ("volume" or "part" or "chapter"))
                throw new InvalidOperationException("Choose a Volume, Part, or Chapter.");
            if (!GetContainerParents(type).Any(parent => parent.Id == parentId))
                throw new InvalidOperationException("Choose a valid location in the current Work.");
            ValidateTitle(title);
            var id = Guid.NewGuid().ToString("N");
            // Use the same content rules as the gateway before connecting or submitting a write.
            var proposed = ContentJson.Clone(_state!);
            proposed.Nodes.Add(new() { Id = id, Type = type, Title = title.Trim(), ParentId = parentId, WorkId = SelectedWork!.Id });
            ContentRules.Validate(proposed);
            await ApplyAsync(new OperationBatch { ExpectedRevision = Revision, Description = "Create " + type + ": " + title.Trim(), Operations =
                [new() { Kind = "createNode", Id = id, ParentId = parentId, NodeType = type, Title = title.Trim() }] }, token);
            SearchText = "";
            SelectedWorkContent = WorkContents.FirstOrDefault(item => item.Id == id);
            StatusMessage = char.ToUpperInvariant(type[0]) + type[1..] + " created in this Work.";
            return true;
        }, cancellationToken);

    public Task<bool> CreateBlankTemplateAsync(string title, CancellationToken cancellationToken = default)
        => SaveNewTemplateAsync(token => _templateCatalog.CreateBlankAsync(RequireLibraryRoot(), title, token), cancellationToken);

    public Task<bool> CreateTemplateFromObjectsAsync(string title, IEnumerable<TemplateObjectSelection> objects,
        CancellationToken cancellationToken = default)
    {
        var selection = objects.ToArray();
        return SaveNewTemplateAsync(token => _templateCatalog.CreateFromObjectsAsync(RequireLibraryRoot(), title, selection, token), cancellationToken);
    }

    public Task<bool> DuplicateTemplateAsync(string templateId, string title, CancellationToken cancellationToken = default)
        => SaveNewTemplateAsync(token => _templateCatalog.DuplicateAsync(RequireLibraryRoot(), templateId, title, token), cancellationToken);

    public Task<bool> CreateTemplateFromWorkAsync(string workId, string title, CancellationToken cancellationToken = default)
        => SaveNewTemplateAsync(token => _templateCatalog.CreateFromWorkAsync(RequireLibraryRoot(), workId, title, token), cancellationToken);

    private Task<bool> SaveNewTemplateAsync(Func<CancellationToken, Task<HyperlibStudioTemplate>> create, CancellationToken cancellationToken)
        => RunAsync(async token =>
        {
            var template = await create(token);
            _templateSearchText = "";
            OnPropertyChanged(nameof(TemplateSearchText));
            await LoadTemplatesAsync(token);
            SelectedTemplate = Templates.FirstOrDefault(item => item.Id == template.Id);
            StatusMessage = "Template created. Edit its reusable elements or use it to create a Work.";
            return true;
        }, cancellationToken);

    public async Task<string?> GetTemplateProjectAsync(string templateId, CancellationToken cancellationToken = default)
    {
        string? root = null;
        await RunAsync(async token =>
        {
            root = await _templateCatalog.OpenProjectAsync(RequireLibraryRoot(), templateId, token);
            return true;
        }, cancellationToken);
        return root;
    }

    public async Task OpenTemplateSourceAsync(string templateId, string relativePath, CancellationToken cancellationToken = default)
    {
        var root = await GetTemplateProjectAsync(templateId, cancellationToken);
        if (root is null) return;
        try
        {
            _ = VisualPageProjectService.ResolveProjectPath(root, relativePath);
            OpenTemplateSourceRequested?.Invoke(root, relativePath);
        }
        catch (Exception error) { ReportExternalError(error.Message); }
    }

    private string RequireLibraryRoot() => Context?.LibraryRoot
        ?? throw new InvalidOperationException("Open a HyperLib library to manage its reusable templates.");

    private async Task LoadTemplatesAsync(CancellationToken token)
    {
        _allTemplates = Context?.LibraryRoot is { } root ? await _templateCatalog.ListAsync(root, token) : [];
        OnPropertyChanged(nameof(AvailableTemplates));
        RebuildTemplates();
    }

    private void RebuildTemplates()
    {
        var selectedId = SelectedTemplate?.Id;
        var search = TemplateSearchText.Trim();
        Replace(Templates, _allTemplates.Where(item => search.Length == 0
            || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(search, StringComparison.OrdinalIgnoreCase)));
        SelectedTemplate = Templates.FirstOrDefault(item => item.Id == selectedId) ?? Templates.FirstOrDefault();
        OnPropertyChanged(nameof(HasTemplates));
    }

    public Task<bool> RenameAsync(string nodeId, string title, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            RequireState();
            if (!_nodes.Any(item => item.Id == nodeId)) throw new InvalidOperationException("Choose an item in this project.");
            ValidateTitle(title);
            await ApplyAsync(new OperationBatch { ExpectedRevision = Revision, Description = "Rename item: " + title.Trim(), Operations =
                [new() { Kind = "updateNode", Id = nodeId, Title = title.Trim() }] }, token);
            StatusMessage = "Renamed. The project's stable source path is unchanged.";
            return true;
        }, cancellationToken);

    /// <summary>Capture a Work's appearance and revision together so saving never overwrites a newer editor.</summary>
    public async Task<HyperlibStudioWorkDesign?> GetWorkDesignAsync(string workId, CancellationToken cancellationToken = default)
    {
        if (_disposed || Context is null || _state is null) return null;
        var context = Context;
        var state = _state;
        var work = state.Nodes.FirstOrDefault(node => node.Type == "work" && node.Id == workId
            && (context.IsLibraryRoot || node.WorkId == context.WorkId));
        if (work is null || context.LibraryRoot is null) return null;
        var root = VisualPageProjectService.ResolveProjectPath(context.LibraryRoot, work.ProjectPath!);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var snapshot = await _service.ReadAsync(root, linked.Token);
        var loaded = await HyperlibStudioAuthoringService.LoadAsync(root, snapshot.Context.AuthoringRelativePath);
        linked.Token.ThrowIfCancellationRequested();
        loaded.Definition.Appearance = StandardAppearance.WithDefaults(loaded.Definition.Appearance).ToList();
        var effective = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in loaded.Definition.Appearance)
        {
            var node = work;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (node is not null && visited.Add(node.Id))
            {
                if (node.Appearance.TryGetValue(field.Id, out var value)) { effective[field.Id] = value.Clone(); break; }
                node = state.Nodes.FirstOrDefault(candidate => candidate.Id == node.ParentId);
            }
            if (effective.ContainsKey(field.Id)) continue;
            if (state.Appearance.TryGetValue(field.Id, out var inherited)) effective[field.Id] = inherited.Clone();
            else if (field.DefaultValue is { } fallback) effective[field.Id] = fallback.Clone();
        }
        return new(work.Id, work.Title, loaded.Definition.Title, state.Revision, loaded.Definition,
            work.Appearance.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal), effective);
    }

    public Task<bool> SaveWorkAppearanceAsync(HyperlibStudioWorkDesign draft,
        IReadOnlyDictionary<string, JsonElement> appearance, CancellationToken cancellationToken = default)
        => RunAsync(async token =>
        {
            RequireState();
            if (!_nodes.Any(node => node.Type == "work" && node.Id == draft.WorkId))
                throw new InvalidOperationException("Choose a Work in this project.");
            TemplateValidator.ValidateValues(draft.Definition.Appearance, appearance.ToDictionary(pair => pair.Key, pair => pair.Value), "Work appearance");
            await ApplyAsync(new OperationBatch { ExpectedRevision = draft.ContentRevision,
                Description = "Set Work appearance: " + draft.Title,
                Operations = [new() { Kind = "setAppearance", Id = draft.WorkId,
                    Appearance = appearance.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal) }] }, token);
            StatusMessage = "Work appearance saved. Pages inherit these defaults; their content stays independent.";
            return true;
        }, cancellationToken);

    public Task<bool> UndoAsync(CancellationToken cancellationToken = default) => ChangeHistoryAsync(false, cancellationToken);
    public Task<bool> RedoAsync(CancellationToken cancellationToken = default) => ChangeHistoryAsync(true, cancellationToken);
    private Task<bool> ChangeHistoryAsync(bool redo, CancellationToken cancellationToken) => RunAsync(async token =>
    {
        RequireState();
        var endpoint = await EndpointAsync(token);
        var state = redo ? await _service.RedoAsync(Context!, endpoint, Revision, token) : await _service.UndoAsync(Context!, endpoint, Revision, token);
        ApplyState(state);
        StatusMessage = redo ? "Redid the last library content change." : "Undid the last library content change.";
        return true;
    }, cancellationToken);

    public async Task<Uri?> GetContentUrlAsync(string? pageId = null, CancellationToken cancellationToken = default)
    {
        Uri? result = null;
        await RunAsync(async token =>
        {
            RequireState();
            result = await _service.GetContentUrlAsync(Context!, _state!, await EndpointAsync(token), pageId ?? SelectedPage?.Id ?? SelectedWork?.Id, token);
            StatusMessage = "Content editor ready.";
            return true;
        }, cancellationToken);
        return result;
    }

    public async Task<HyperlibStudioPreview?> PreviewAsync(string? pageId = null, CancellationToken cancellationToken = default)
    {
        HyperlibStudioPreview? result = null;
        await RunAsync(async token =>
        {
            RequireState();
            StatusMessage = "Preparing the project preview…";
            result = await _service.PreviewAsync(Context!, _state!, await EndpointAsync(token), pageId ?? SelectedPage?.Id, token);
            _preview = result;
            StatusMessage = result.Diagnostics is null ? "Preview ready." : "Showing the last working preview. " + result.Diagnostics;
            PreviewRequested?.Invoke(result);
            return true;
        }, cancellationToken);
        return result;
    }

    public async Task KeepPreviewAliveAsync(CancellationToken cancellationToken = default)
    {
        if (_preview is null || Context is null || _disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try { await _service.KeepPreviewAliveAsync(Context, _preview, linked.Token); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; StatusMessage = "The preview connection is unavailable."; }
    }

    private async Task ApplyAsync(OperationBatch batch, CancellationToken token)
        => ApplyState(await _service.ApplyAsync(Context!, await EndpointAsync(token), batch, token));
    private Task<Uri> EndpointAsync(CancellationToken token)
        => _service.ConnectAsync(Context!, EndpointFactory ?? throw new InvalidOperationException("Connect this project to MyNet to edit content or run its preview."), token);
    private void ApplyState(LibraryState state)
    {
        _state = state;
        _nodes = _service.BuildNodes(Context!, state);
        if (Context!.WorkId is { } workId && state.Nodes.FirstOrDefault(item => item.Id == workId) is { } work)
            Context = Context with { Title = work.Title };
        RebuildLists();
        NotifyContext();
    }

    private void RebuildLists()
    {
        _rebuilding = true;
        try
        {
            var selectedWorkId = SelectedWork?.Id ?? Context?.WorkId;
            var matchingContentWorks = _nodes.Where(item => item.WorkId is not null && Matches(item)).Select(item => item.WorkId).ToHashSet();
            Replace(Works, _nodes.Where(item => item.Type == "work" && (Matches(item) || matchingContentWorks.Contains(item.Id))));
            // Parent choices deliberately include library collections when a Work creates another independent Work.
            var allNodes = Context is null ? [] : _service.BuildNodes(Context with { IsLibraryRoot = true }, _state);
            Replace(WorkParents, allNodes.Where(item => item.Type is "collection" or "series"));
            Replace(Collections, allNodes.Where(item => item.Type == "collection"));
            _selectedWork = Works.FirstOrDefault(item => item.Id == selectedWorkId) ?? Works.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedWork));
            OnPropertyChanged(nameof(HasWorks));
            OnPropertyChanged(nameof(HasSelectedWork));
            RebuildPages();
            RaiseCommands();
        }
        finally { _rebuilding = false; }
    }

    private void RebuildPages()
    {
        var wasRebuilding = _rebuilding;
        _rebuilding = true;
        try
        {
            var selectedPageId = SelectedPage?.Id;
            Replace(Pages, _nodes.Where(item => item.Type == "page" && item.WorkId == SelectedWork?.Id && Matches(item)));
            Replace(PageParents, _nodes.Where(item => item.WorkId == SelectedWork?.Id && item.Type is "work" or "volume" or "part" or "chapter"));
            RebuildWorkContents();
            _selectedPage = Pages.FirstOrDefault(item => item.Id == selectedPageId);
            OnPropertyChanged(nameof(SelectedPage));
            OnPropertyChanged(nameof(HasPages));
        }
        finally { _rebuilding = wasRebuilding; }
    }
    private void RebuildWorkContents()
    {
        var selectedId = SelectedWorkContent?.Id;
        var children = _nodes.Where(node => node.WorkId == SelectedWork?.Id && node.Type != "work")
            .ToLookup(node => node.ParentId, StringComparer.Ordinal);
        var items = new List<HyperlibStudioContentItem>();
        if (SelectedWork is { } work) AddChildren(work.Id, 0);
        Replace(WorkContents, items);
        _selectedWorkContent = WorkContents.FirstOrDefault(item => item.Id == selectedId)
            ?? WorkContents.FirstOrDefault(item => item.Id == SelectedPage?.Id);
        OnPropertyChanged(nameof(SelectedWorkContent));
        OnPropertyChanged(nameof(HasWorkContents));

        void AddChildren(string parentId, int depth)
        {
            foreach (var node in children[parentId])
            {
                items.Add(new(node, depth));
                AddChildren(node.Id, depth + 1);
            }
        }
    }
    private bool Matches(HyperlibStudioNode node) => string.IsNullOrWhiteSpace(SearchText)
        || node.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
        || node.Breadcrumb.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task LoadTemplateSummaryAsync(CancellationToken token)
    {
        var path = VisualPageProjectService.ResolveProjectPath(Context!.ProjectRoot, Context.AuthoringRelativePath);
        if (!File.Exists(path)) { TemplateSummary = "No element schema file is available yet."; return; }
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("The template file is too large.");
        var template = JsonSerializer.Deserialize<TemplateDefinition>(await File.ReadAllTextAsync(path, token), ContentJson.Options)
            ?? throw new InvalidDataException("The element schema is empty.");
        TemplateSummary = $"{template.Title} · {template.Blocks.Count} elements · {template.DefaultBlocks.Count} starter blocks";
    }

    private void OpenWork(HyperlibStudioNode? node)
    {
        if (node is null || !_nodes.Any(item => item.Type == "work" && item.Id == node.Id)) return;
        OpenValidatedWork(node.Id);
    }
    public void OpenLastCreatedWork()
    {
        if (LastCreatedWork is not null) OpenValidatedWork(LastCreatedWork.Id);
    }
    private void OpenValidatedWork(string workId)
    {
        try
        {
            if (_state?.Nodes.FirstOrDefault(item => item.Type == "work" && item.Id == workId) is not { } work)
                throw new InvalidOperationException("The Work no longer exists in this library. Refresh the project.");
            var root = VisualPageProjectService.ResolveProjectPath(Context!.LibraryRoot!, work.ProjectPath!);
            if (!File.Exists(VisualPageProjectService.ResolveProjectPath(root, VisualPageProjectService.ProjectManifestFileName)))
                throw new FileNotFoundException("The Work's source project is still being prepared. Refresh and try again.");
            OpenWorkProjectRequested?.Invoke(root);
        }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    private void OpenContent(HyperlibStudioNode? node)
    {
        if (node is not null && _nodes.Any(item => item.Id == node.Id)) OpenContentRequested?.Invoke(node.Id);
        else if (node is null && IsLibraryRoot) OpenContentRequested?.Invoke("library");
    }
    private void OpenSource(HyperlibStudioSource? source)
    {
        if (source is null || !SourceGroups.SelectMany(group => group.Items).Any(item => item.RelativePath == source.RelativePath)) return;
        try { _ = VisualPageProjectService.ResolveProjectPath(Context!.ProjectRoot, source.RelativePath); OpenRelativeCodeRequested?.Invoke(source.RelativePath); }
        catch (Exception error) { ErrorMessage = error.Message; }
    }
    private void RequireState()
    {
        if (_state is null || !HasLibraryContent) throw new InvalidOperationException(Context?.ContentUnavailableReason ?? "Load this library first.");
    }
    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200 || title.Any(char.IsControl))
            throw new InvalidOperationException("Enter a title of 1–200 printable characters.");
    }
    private async Task<bool> RunAsync(Func<CancellationToken, Task<bool>> action, CancellationToken token)
    {
        if (IsBusy || _disposed) return false;
        IsBusy = true;
        ErrorMessage = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try { return await action(linked.Token); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { StatusMessage = "Operation canceled. Refresh to check the latest content before retrying."; return false; }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
            StatusMessage = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Conflict }
                ? "Content changed elsewhere. Refresh, then review and retry your draft."
                : error is HttpRequestException { StatusCode: null } or TaskCanceledException
                ? "The gateway did not confirm the result. Refresh before retrying."
                : "The action could not be completed.";
            return false;
        }
        finally { IsBusy = false; }
    }
    private void NotifyContext()
    {
        foreach (var property in new[] { nameof(IsLibraryRoot), nameof(HasLibraryContent), nameof(Revision), nameof(ScopeTitle), nameof(ScopeDescription), nameof(CanMutate), nameof(TemplateSourceWorks) }) OnPropertyChanged(property);
        RaiseCommands();
    }
    private void RaiseCommands()
    {
        RefreshCommand?.RaiseCanExecuteChanged(); CreateWorkCommand?.RaiseCanExecuteChanged(); CreatePageCommand?.RaiseCanExecuteChanged();
        RenameCommand?.RaiseCanExecuteChanged(); UndoCommand?.RaiseCanExecuteChanged(); RedoCommand?.RaiseCanExecuteChanged();
        OpenWorkCommand?.RaiseCanExecuteChanged(); OpenContentCommand?.RaiseCanExecuteChanged(); OpenSourceCommand?.RaiseCanExecuteChanged();
        EditTemplateCommand?.RaiseCanExecuteChanged(); PreviewCommand?.RaiseCanExecuteChanged();
        OpenTemplateProjectCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(CanManageTemplates));
    }
    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        var values = items.ToArray();
        collection.Clear();
        foreach (var item in values) collection.Add(item);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_ownsService) _service.Dispose();
    }
}
