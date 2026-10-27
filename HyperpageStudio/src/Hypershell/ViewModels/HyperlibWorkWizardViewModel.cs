using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;

namespace Hypershell.ViewModels;

/// <summary>A guided, region-by-region builder for the landing page and named reusable Page templates.</summary>
public sealed class HyperlibWorkWizardViewModel : ObservableObject
{
    private string _workTitle = "My Work", _search = "", _group = "All collections", _category = "All objects", _inputValidationMessage="", _defaultPageTemplateId="";
    private int _step, _templateIndex = -1;
    private bool _titleInitialized, _updating;
    private HyperlibWizardLibraryObject? _selectedObject;
    private string[] _missingObjectIds = [];
    private readonly HashSet<string> _retainedToolsetIds=new(StringComparer.Ordinal);
    private readonly IReadOnlyList<HyperlibProjectToolkitItem> _savedToolkit;
    private readonly Dictionary<string,HyperLib.Core.TemplateLibraryObject> _libraryObjectCache=new(StringComparer.Ordinal);
    private IReadOnlyList<HyperlibWizardLibraryObject> _filteredObjects=[];
    private (string Stage,string Group,string Search)? _browseFilter;
    public HyperlibWorkWizardViewModel(string? libraryRoot = null, IReadOnlyList<HyperlibProjectToolkitItem>? savedToolkit = null,
        string? ownedProjectRoot = null)
    {
        OwnedProjectRoot = ownedProjectRoot;
        _savedToolkit=savedToolkit??[];
        IReadOnlyList<HyperlibWorkTemplateChoice> catalog = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(libraryRoot) && !System.IO.Directory.Exists(libraryRoot)) throw new System.IO.DirectoryNotFoundException();
            var service = new HyperlibWorkProjectService(libraryRoot); TemplateLibraryRoot = service.TemplateLibraryRoot; catalog = service.GetCatalog();
        }
        catch { CatalogMessage = savedToolkit is { Count: > 0 } ? "Using this project's saved collection. Reconnect the original library to choose additional objects." : "The template library is unavailable. Reconnect it to choose library objects; your layout choices are retained."; }
        if (savedToolkit is { Count: > 0 })
        {
            var owned = savedToolkit.Select(item => { var split = item.ChoiceId.IndexOf(':'); return new HyperlibWorkTemplateChoice(item.ChoiceId,item.Title,item.Description,item.Category,item.Kind,split > 0 ? item.ChoiceId[..split] : "project-toolset",split > 0 ? item.ChoiceId[(split+1)..] : item.ChoiceId,item.GroupTitle,""); }).ToArray();
            var ids = owned.Select(item => item.Id).ToHashSet(StringComparer.Ordinal); catalog = owned.Concat(catalog.Where(item => !ids.Contains(item.Id))).ToArray();
        }
        LibraryObjects = catalog.OrderBy(item => item.Title).ThenBy(item => item.GroupTitle).Select(item => new HyperlibWizardLibraryObject(item)).ToArray();
        Groups = new[] { "All collections" }.Concat(catalog.Select(item => item.GroupTitle).Distinct().Order()).ToArray();
        Categories = new[] { "All objects" }.Concat(catalog.Select(item => item.Category).Distinct().Order()).ToArray();
        LandingTemplate = NewTemplate("work-landing", "Work landing page");
        PageTemplates.Add(NewTemplate("page-template-" + Guid.NewGuid().ToString("N"), "Content Page"));
        foreach (var item in LibraryObjects) item.PropertyChanged += (_, _) => { if (!_updating) Refresh(); };
        BackCommand = new RelayCommand(GoBack, () => Step > 0 || !IsLandingStep);
        NextCommand = new RelayCommand(GoNext, () => ShowNext && CurrentValidationMessage.Length == 0);
        AddPageTemplateCommand = new RelayCommand(AddPageTemplate);
        FinishTemplatesCommand = new RelayCommand(() => Step = 7, () => IsValid);
        EditCollectionCommand = new RelayCommand(() => { _templateIndex = -1; Step = 0; Refresh(); });
        EditLandingCommand = EditCollectionCommand;
        EditPageTemplateCommand = new RelayCommand(item => { var template = item as HyperlibWizardTemplateViewModel ?? PageTemplates.First(); _templateIndex = PageTemplates.IndexOf(template); Step = 0; Refresh(); });
        SelectObjectCommand = new RelayCommand(item => { if (item is HyperlibWizardLibraryObject value) ApplyObject(value); });
        SelectBackgroundDesignCommand = new RelayCommand(item => { if(item is HyperlibBackgroundDesign design) CurrentTemplate.ApplyBackgroundDesign(design); });
        RemoveObjectCommand = new RelayCommand(item => { if (item is HyperlibWizardLibraryObject value) { CurrentTemplate?.RemoveObject(value.Id, CurrentStage); Refresh(); } });
        ClearStageObjectCommand = new RelayCommand(() => { CurrentTemplate?.ClearStage(CurrentStage); SelectedObject = null; Refresh(); });
        Refresh();
    }
    private HyperlibWizardTemplateViewModel NewTemplate(string id, string name)
    {
        var template = new HyperlibWizardTemplateViewModel(name) { Id = id }; template.UpdateChoices(LibraryObjects.Select(item => item.Source).ToArray());
        template.HeaderLayout.TemplateObjectProvider=choiceId=>LibraryObjects.FirstOrDefault(item=>item.Id==choiceId) is {} choice?GetLibraryObject(choice.Source):null;
        template.NavigationLayout.TemplateObjectProvider=template.HeaderLayout.TemplateObjectProvider;
        template.BodyLayout.TemplateObjectProvider=template.HeaderLayout.TemplateObjectProvider;
        template.FooterLayout.TemplateObjectProvider=template.HeaderLayout.TemplateObjectProvider;
        template.DefinitionChanged += () => { if (!_updating) { CollectUsedObjects(); Refresh(); } }; return template;
    }
    public string? TemplateLibraryRoot { get; }
    public string? OwnedProjectRoot { get; }
    public HyperLib.Core.TemplateLibraryObject? GetLibraryObject(HyperlibWorkTemplateChoice choice)
    {
        if(_libraryObjectCache.TryGetValue(choice.Id,out var cached))return cached;
        HyperLib.Core.TemplateLibraryObject? result=null;
        if(OwnedProjectRoot is {} owned&&_savedToolkit.FirstOrDefault(item=>item.ChoiceId==choice.Id) is {} toolkit)
        {
            var definition=HyperLib.Core.TemplateObjectLibrary.ReadDefinition(owned);
            var element=toolkit.Kind is "element" or "preset"?toolkit.SourceBlockDefinitions.GetValueOrDefault("element")??definition.Blocks.FirstOrDefault(item=>toolkit.BlockTypeIds.Contains(item.Id)):null;
            var appearance=definition.Appearance.Where(item=>toolkit.LayoutAppearance.ContainsKey(item.Id)).Select(item=>{var field=HyperLib.Core.ContentJson.Clone(item);field.DefaultValue=toolkit.LayoutAppearance[item.Id].Clone();return field;}).ToList();
            result=new(){Id=choice.ObjectId,GroupId=choice.GroupId,Title=choice.Title,Description=choice.Description,Category=choice.Category,Kind=choice.Kind,
                CatalogId=HyperLib.Core.TemplateObjectLibrary.CatalogIdFor(choice.GroupId,choice.ObjectId),Definition=definition,Element=element,Blocks=HyperLib.Core.ContentJson.Clone(toolkit.LayoutBlocks),Appearance=appearance};
        }
        if(result is null&&TemplateLibraryRoot is {} source)result=new HyperlibWorkProjectService(source).GetCatalogObject(choice.GroupId,choice.ObjectId);
        if(result is not null)_libraryObjectCache[choice.Id]=result;return result;
    }
    public IReadOnlyList<HyperlibWizardLibraryObject> LibraryObjects { get; }
    public IReadOnlyList<string> Groups { get; }
    public IReadOnlyList<string> Categories { get; }
    public string CatalogMessage { get; } = "";
    public bool HasCatalogMessage => CatalogMessage.Length > 0;
    public HyperlibWizardTemplateViewModel LandingTemplate { get; }
    public ObservableCollection<HyperlibWizardTemplateViewModel> PageTemplates { get; } = [];
    public HyperlibWizardTemplateViewModel PageTemplate => PageTemplates.FirstOrDefault(item=>item.Id==DefaultPageTemplateId)??PageTemplates.First();
    public string DefaultPageTemplateId {get=>string.IsNullOrEmpty(_defaultPageTemplateId)?PageTemplates.First().Id:_defaultPageTemplateId;set{if(SetProperty(ref _defaultPageTemplateId,value??""))Refresh();}}
    public ICommand BackCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand AddPageTemplateCommand { get; }
    public ICommand FinishTemplatesCommand { get; }
    public ICommand EditCollectionCommand { get; }
    public ICommand EditLandingCommand { get; }
    public ICommand EditPageTemplateCommand { get; }
    public ICommand SelectObjectCommand { get; }
    public ICommand SelectBackgroundDesignCommand { get; }
    public IReadOnlyList<HyperlibBackgroundDesign> BackgroundDesigns=>HyperlibBackgroundDesigns.All;
    public ICommand RemoveObjectCommand { get; }
    public ICommand ClearStageObjectCommand { get; }
    public string WorkTitle { get => _workTitle; set { if (SetProperty(ref _workTitle, value ?? "")) Refresh(); } }
    public string InputValidationMessage { get=>_inputValidationMessage;set {if(SetProperty(ref _inputValidationMessage,value??""))Refresh();} }
    public string Search { get => _search; set { if (SetProperty(ref _search, value ?? "")) RefreshBrowse(); } }
    public string SelectedGroup { get => _group; set { if (SetProperty(ref _group, value ?? "All collections")) RefreshBrowse(); } }
    public string SelectedCategory { get => _category; set { if (SetProperty(ref _category, value ?? "All objects")) RefreshBrowse(); } }
    public IEnumerable<HyperlibWizardLibraryObject> FilteredObjects => _filteredObjects;
    public IEnumerable<HyperlibWizardLibraryObject> CollectedObjects => LibraryObjects.Where(item => item.IsSelected);
    public int CollectedCount => LibraryObjects.Count(item => item.IsSelected);
    public bool HasCollectedObjects => CollectedCount > 0;
    public string CollectionSummary => $"{CollectedCount} objects in your project";
    public string BrowseSummary => $"{FilteredObjects.Count()} {CurrentStage} choices";
    public bool HasStageObjects => FilteredObjects.Any();
    public string EmptyStageMessage => Search.Length > 0 ? "No matches. Try another search or collection." : "Use the built-in styling below, or select another collection for more choices.";
    public HyperlibWizardLibraryObject? SelectedObject { get => _selectedObject; set => SetProperty(ref _selectedObject,value); }
    public bool HasSelectedObject => SelectedObject is not null;
    public int Step { get => _step; set { if (SetProperty(ref _step,Math.Clamp(value,0,7))) { _search = ""; _selectedObject = null; Refresh(); } } }
    public bool IsShoppingStep => false;
    public bool IsStagingStep => Step < 6;
    public bool IsLandingStep => _templateIndex < 0;
    public bool IsReviewStep => Step == 7;
    public bool IsTemplatePromptStep => Step == 6;
    public bool ShowNext => Step < 6;
    public HyperlibWizardTemplateViewModel CurrentTemplate => IsLandingStep ? LandingTemplate : PageTemplates[Math.Min(_templateIndex,PageTemplates.Count-1)];
    public string CurrentPageTitle => IsLandingStep ? WorkTitle + " · landing page" : CurrentTemplate.Name;
    public string CurrentStage => Step switch { 0 => "background", 1 => "header", 2 => "navigation", 3 => "body", 4 => "footer", _ => "extras" };
    public bool IsBackgroundStage => Step == 0;
    public bool ShowLibraryStage => IsStagingStep && IsExtrasStage;
    public bool IsHeaderStage => Step == 1;
    public bool IsBodyStage => Step == 3;
    public bool IsNavigationStage => Step == 2;
    public bool IsFooterStage => Step == 4;
    public bool IsExtrasStage => Step == 5;
    public bool ShowRegionStyle => IsStagingStep && !IsBackgroundStage;
    public bool ShowChromeText => Step is 1 or 4;
    public bool ShowLogo => Step == 1;
    public bool ShowContentLayout => Step is 3 or 5;
    public bool ShowPageName => !IsLandingStep && IsStagingStep;
    public HyperlibWizardRegionStyleViewModel CurrentStyle => CurrentStage switch { "header" => CurrentTemplate.HeaderStyle, "navigation" => CurrentTemplate.NavigationStyle, "footer" => CurrentTemplate.FooterStyle, "extras" => CurrentTemplate.ExtrasStyle, _ => CurrentTemplate.BodyStyle };
    public IEnumerable<HyperlibWizardRepeatingObject> CurrentPlacements => CurrentTemplate.RepeatingObjects.Where(item => IsExtrasStage ? item.Region == "sidebar" : item.Region == "main");
    public string StageSelectionSummary => CurrentTemplate.GetStageSummary(CurrentStage);
    public string StepNumber => IsReviewStep ? "READY TO START AUTHORING" : IsTemplatePromptStep ? "PAGE TEMPLATE COMPLETE" : $"Step {Step+1} of 6";
    public string StepTitle => Step switch { 0 => "Background",1 => "Header",2 => "Navigation",3 => "Body",4 => "Footer",5 => "Extras",6 => "Would you like to add another Page template?",_ => "Your site foundation is ready" };
    public string StageBreadcrumb => IsStagingStep ? string.Join("  ›  ", new[] { "Background", "Header", "Navigation", "Body", "Footer", "Extras" }.Take(Step + 1)) : "";
    public bool ShowStepDescription => !IsStagingStep;
    public string StepDescription => Step switch
    {
        < 6 => "",
        6 => "Each named template is a reusable starting point. Make another layout for a different kind of Page, or begin creating Pages and adding content.",
        _ => "Your landing design and named Page templates are saved with the project. Choose a template whenever you create a Page, then add its content in Studio."
    };
    public string NextLabel => Step switch { 0 => "Next: Header",1 => "Next: Navigation",2 => "Next: Body",3 => "Next: Footer",4 => "Next: Extras",5 when IsLandingStep => "Build a Page template",_ => "Complete this template" };
    public string CollectionValidationMessage => _missingObjectIds.Length > 0 ? "Restore their collection before saving: " + string.Join(", ",_missingObjectIds.Take(3)) : CollectedCount > 180 ? "Use 180 objects or fewer in this project's collection." : "";
    public string CurrentValidationMessage => InputValidationMessage.Length > 0 ? InputValidationMessage : CollectionValidationMessage.Length > 0 ? CollectionValidationMessage : IsReviewStep || IsTemplatePromptStep ? ValidationMessage
        : !IsLandingStep && (string.IsNullOrWhiteSpace(CurrentTemplate.Name) || CurrentTemplate.Name.Length > 120 || CurrentTemplate.Name.Any(char.IsControl)) ? "Give this Page template a name with 120 printable characters or fewer."
        : IsBackgroundStage ? CurrentTemplate.BackgroundValidationMessage : IsHeaderStage && CurrentTemplate.HeaderLayout.Enabled ? CurrentTemplate.HeaderLayout.ValidationMessage
        : IsNavigationStage && CurrentTemplate.NavigationLayout.Enabled ? CurrentTemplate.NavigationLayout.ValidationMessage
        : IsBodyStage && CurrentTemplate.BodyLayout.Enabled ? CurrentTemplate.BodyLayout.ValidationMessage
        : IsFooterStage && CurrentTemplate.FooterLayout.Enabled ? CurrentTemplate.FooterLayout.ValidationMessage : CurrentStyle.ValidationMessage;
    public bool HasCurrentValidationError => CurrentValidationMessage.Length > 0;
    public string ValidationMessage => InputValidationMessage.Length > 0 ? InputValidationMessage : string.IsNullOrWhiteSpace(WorkTitle) ? "Give the project a name." : WorkTitle.Length > 200 || WorkTitle.Any(char.IsControl) ? "Use a project name with 200 printable characters or fewer."
        : CollectionValidationMessage.Length > 0 ? CollectionValidationMessage : LandingTemplate.ValidationMessage.Length > 0 ? "Landing page: " + LandingTemplate.ValidationMessage
        : PageTemplates.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 || item.Name.Any(char.IsControl)) ? "Give every Page template a name with 120 printable characters or fewer."
        : PageTemplates.Select(item => item.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != PageTemplates.Count ? "Give each Page template a different name."
        : PageTemplates.FirstOrDefault(item => item.ValidationMessage.Length > 0) is { } invalid ? invalid.Name + ": " + invalid.ValidationMessage : "";
    public bool IsValid => ValidationMessage.Length == 0;
    public string ReviewTitle => WorkTitle.Trim();
    public string ReviewDescription => $"Landing page · {PageTemplates.Count} reusable Page {(PageTemplates.Count == 1 ? "template" : "templates")} · {CollectedCount} library objects";
    public HyperlibWorkProjectDefinition PreviewDefinition => CreateDefinition();
    public void InitializeTitle(string title) { if (_titleInitialized) return; _titleInitialized = true; WorkTitle = title; }
    public void LoadDefinition(HyperlibWorkProjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition); _updating = true;
        try
        {
            _titleInitialized = true; WorkTitle = definition.WorkTitle;
            var ids = definition.SelectedToolsetIds.ToHashSet(StringComparer.Ordinal); _retainedToolsetIds.Clear();_retainedToolsetIds.UnionWith(ids);
            _missingObjectIds = ids.Where(id => LibraryObjects.All(item => item.Id != id)).ToArray();
            foreach (var item in LibraryObjects) item.IsSelected = ids.Contains(item.Id);
            LandingTemplate.LoadDefinition(definition.LandingTemplate); PageTemplates.Clear();
            foreach (var named in definition.PageTemplates) { var page = NewTemplate(named.Id,named.Name); page.LoadDefinition(named.Definition); PageTemplates.Add(page); }
            if (PageTemplates.Count == 0) { var page = NewTemplate("page-template-default","Content Page"); page.LoadDefinition(definition.PageTemplate); PageTemplates.Add(page); }
            _defaultPageTemplateId=PageTemplates.Any(item=>item.Id==definition.DefaultPageTemplateId)?definition.DefaultPageTemplateId:PageTemplates.First().Id;
            _templateIndex = -1; _step = 0;
        }
        finally { _updating = false; }
        Refresh();
    }
    internal string CaptureDraftState() => JsonSerializer.Serialize(CreateDefinition());
    public HyperlibWorkProjectDefinition BuildDefinition() { if (!IsValid) throw new InvalidOperationException(ValidationMessage); return CreateDefinition(); }
    private HyperlibWorkProjectDefinition CreateDefinition()
    {
        var templates=PageTemplates.Select(item=>new HyperlibNamedPageTemplateDefinition{Id=item.Id,Name=item.Name.Trim(),Definition=item.BuildDefinition()}).ToList();
        var chosen=templates.FirstOrDefault(item=>item.Id==DefaultPageTemplateId)??templates.First();
        return new(){PipelineVersion=2,WorkTitle=WorkTitle.Trim(),Description="",TemplateId="blank",SelectedToolsetIds=CollectedObjects.Select(item=>item.Id).ToList(),
            LandingTemplate=LandingTemplate.BuildDefinition(),PageTemplate=chosen.Definition,DefaultPageTemplateId=chosen.Id,PageTemplates=templates,PageDefinitions=[]};
    }
    private void GoNext()
    {
        if (Step < 5) Step++;
        else if (IsLandingStep) { _templateIndex=0; Step=0; Refresh(); }
        else if(_templateIndex+1<PageTemplates.Count){_templateIndex++;Step=0;Refresh();}
        else Step=6;
    }
    private void GoBack()
    {
        if (Step == 7) { Step=6; return; }
        if (Step == 6) { Step=5; return; }
        if (Step > 0) { Step--; return; }
        if (_templateIndex >= 0) { _templateIndex--; Step=5; Refresh(); }
    }
    private void AddPageTemplate()
    {
        var number=PageTemplates.Count+1; var name="Page template " + number;
        while (PageTemplates.Any(item => item.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) name="Page template " + ++number;
        PageTemplates.Add(NewTemplate("page-template-"+Guid.NewGuid().ToString("N"),name)); _templateIndex=PageTemplates.Count-1; Step=0; Refresh();
    }
    private void ApplyObject(HyperlibWizardLibraryObject item)
    {
        if (!MatchesStage(item,CurrentStage)) return; item.IsSelected=true; SelectedObject=item;
        CurrentTemplate.ApplyObject(item.Source,CurrentStage);
        if(CurrentStage=="background"&&TemplateLibraryRoot is {} root)
        {
            try
            {
                var palette=new HyperlibWorkProjectService(root).GetCatalogObject(item.Source.GroupId,item.Source.ObjectId);
                foreach(var field in palette.Appearance.Where(field=>field.Type=="color"&&field.DefaultValue is not null))
                {
                    var color=field.DefaultValue!.Value.ValueKind==JsonValueKind.String?field.DefaultValue.Value.GetString():null;
                    if(color is null||!Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))continue;
                    if(field.Id is "background" or "pageBackground" or "pageBg")CurrentTemplate.Background=color;
                    if(field.Id is "accent" or "accentColor")CurrentTemplate.Accent=color;
                }
            }
            catch { /* A moved project can continue using its owned palette and explicit color controls. */ }
        }
        CollectUsedObjects(); Refresh();
    }
    private void CollectUsedObjects()
    {
        if (_updating) return; _updating=true;
        try
        {
            var used=new[] {LandingTemplate}.Concat(PageTemplates).SelectMany(item => item.UsedIds()).ToHashSet(StringComparer.Ordinal);
            used.UnionWith(_retainedToolsetIds);
            foreach (var item in LibraryObjects) item.IsSelected=used.Contains(item.Id);
        }
        finally { _updating=false; }
    }
    public static bool MatchesStage(HyperlibWizardLibraryObject item,string stage)
    {
        var text=(item.Title+" "+item.Category+" "+item.Source.ComponentName).ToLowerInvariant();
        var header=text.Contains("header") || text.Contains("banner"); var footer=text.Contains("footer"); var nav=item.Category.Contains("Navigation",StringComparison.OrdinalIgnoreCase) && item.Kind != "region" || text.Contains("navigation") || text.Contains("menu") || text.Contains("breadcrumb");
        var surface=item.Kind == "palette" || text.Contains("background") || text.Contains("surface");
        var element=item.Kind is "element" or "preset" || item.Source.ComponentName.Length > 0;
        return stage switch { "background" => surface && !element,"header" => header && element,"footer" => footer && element,"navigation" => nav && element,
            "body" => element && !header && !footer && !nav,
            "extras" => element && !header && !footer && !nav && (text.Contains("tool") || text.Contains("container") || text.Contains("panel") || text.Contains("callout") || text.Contains("card") || text.Contains("grid") || text.Contains("embed") || text.Contains("sidebar") || text.Contains("special")),_=>false };
    }
    private void RefreshBrowse()
    {
        var filter=(CurrentStage,SelectedGroup,Search.Trim());
        if(_browseFilter!=filter)
        {
            // Keep item containers and their scroll anchor when only the selected design or styling changes.
            _filteredObjects=LibraryObjects.Where(item=>MatchesStage(item,CurrentStage)
                &&(SelectedGroup=="All collections"||item.GroupTitle==SelectedGroup)
                &&(filter.Item3.Length==0||item.SearchText.Contains(filter.Item3,StringComparison.OrdinalIgnoreCase))).ToArray();
            _browseFilter=filter;OnPropertyChanged(nameof(FilteredObjects));
        }
        foreach(var name in new[]{nameof(BrowseSummary),nameof(HasStageObjects),nameof(EmptyStageMessage)})OnPropertyChanged(name);
    }
    private void Refresh()
    {
        foreach(var name in new[]{nameof(IsShoppingStep),nameof(IsStagingStep),nameof(IsLandingStep),nameof(IsReviewStep),nameof(IsTemplatePromptStep),nameof(ShowNext),nameof(CurrentTemplate),nameof(CurrentPageTitle),nameof(CurrentStage),nameof(IsBackgroundStage),nameof(ShowLibraryStage),nameof(IsHeaderStage),nameof(IsBodyStage),nameof(IsNavigationStage),nameof(IsFooterStage),nameof(IsExtrasStage),nameof(ShowRegionStyle),nameof(ShowChromeText),nameof(ShowLogo),nameof(ShowContentLayout),nameof(ShowPageName),nameof(CurrentStyle),nameof(CurrentPlacements),nameof(StageSelectionSummary),nameof(StepNumber),nameof(StepTitle),nameof(StageBreadcrumb),nameof(ShowStepDescription),nameof(StepDescription),nameof(NextLabel),nameof(CurrentValidationMessage),nameof(HasCurrentValidationError),nameof(ValidationMessage),nameof(IsValid),nameof(ReviewTitle),nameof(ReviewDescription),nameof(CollectedObjects),nameof(CollectedCount),nameof(HasCollectedObjects),nameof(CollectionSummary),nameof(PreviewDefinition),nameof(Search)}) OnPropertyChanged(name);
        RefreshBrowse(); foreach(var command in new[]{BackCommand,NextCommand,FinishTemplatesCommand,AddPageTemplateCommand}) (command as RelayCommand)?.RaiseCanExecuteChanged();
    }
}

public sealed class HyperlibWizardLibraryObject : ObservableObject
{
    private bool _selected;
    public HyperlibWizardLibraryObject(HyperlibWorkTemplateChoice source)=>Source=source;
    public HyperlibWorkTemplateChoice Source {get;}
    public string Id=>Source.Id; public string Title=>Source.Title; public string GroupTitle=>Source.GroupTitle; public string Category=>Source.Category; public string Kind=>Source.Kind; public string Description=>Source.Description;
    public string Detail=>GroupTitle+" · "+Category; public string SearchText=>$"{Id} {Title} {Description} {GroupTitle} {Category} {Kind} {Source.ComponentName}";
    public bool IsSelected {get=>_selected;set=>SetProperty(ref _selected,value);}
}
public sealed record HyperlibWizardTemplateOption(string? Id,string Title,string Description);
public sealed record HyperlibWizardPlacementOption(string Id,string Title);

public sealed class HyperlibWizardTemplateViewModel : ObservableObject
{
    private static readonly HyperlibWizardTemplateOption Empty=new(null,"Built-in / empty slot","");
    private HyperlibWizardTemplateOption _layout=Empty,_header=Empty,_footer=Empty,_body=Empty,_navigation=Empty;
    private string _name,_background="#101820",_accent="#65A88E",_alignment="left",_width="comfortable",_headerPlacement="top",_footerPlacement="bottom",_navigationPlacement="top";
    private string? _backgroundTemplateId,_bodyLayoutTemplateId,_completeTemplateObjectId,_completeTemplateAppearanceId; private bool _includeNavigation=true,_navigationSticky;
    private string _backgroundDesignId="solid",_backgroundImagePath="",_backgroundImageFit="cover",_pageBorderColor="#65a88e",_pageBorderStyle="solid";
    private double _pageBorderWidth,_pageBorderRadius;
    public HyperlibWizardTemplateViewModel(string title)
    {
        _name=title; foreach(var style in new[]{HeaderStyle,BodyStyle,NavigationStyle,FooterStyle,ExtrasStyle}) style.PropertyChanged+=(_,_)=>Refresh();
        HeaderLayout.BackgroundColorProvider=()=>Background;
        HeaderLayout.DefinitionChanged+=()=>{if(HeaderLayout.Enabled){_header=Empty;_headerPlacement="top";OnPropertyChanged(nameof(SelectedHeader));OnPropertyChanged(nameof(HeaderPlacement));}Refresh();};
        NavigationLayout.BackgroundColorProvider=()=>Background;
        NavigationLayout.DefinitionChanged+=()=>{if(NavigationLayout.Enabled){_navigation=Empty;_includeNavigation=true;if(_navigationPlacement=="none")_navigationPlacement="top";OnPropertyChanged(nameof(SelectedNavigation));OnPropertyChanged(nameof(IncludeNavigation));OnPropertyChanged(nameof(NavigationPlacement));}Refresh();};
        BodyLayout.BackgroundColorProvider=()=>Background;
        BodyLayout.DefinitionChanged+=Refresh;
        FooterLayout.BackgroundColorProvider=()=>Background;
        FooterLayout.DefinitionChanged+=()=>{if(FooterLayout.Enabled){_footerPlacement="bottom";OnPropertyChanged(nameof(FooterPlacement));OnPropertyChanged(nameof(IncludeFooter));}Refresh();};
        RemovePlacementCommand=new RelayCommand(item=>{if(item is HyperlibWizardRepeatingObject placement)RepeatingObjects.Remove(placement);});
        AddPlacementCommand=new RelayCommand(()=>{if(ElementChoices.FirstOrDefault(item=>item.Id is not null) is {} choice) AddPlacement(choice,"main");});
        RepeatingObjects.CollectionChanged+=(_,_)=>Refresh();
    }
    public string Id {get;set;}="page-template-default";
    public event Action? DefinitionChanged;
    public string Name {get=>_name;set{if(SetProperty(ref _name,value??""))Refresh();}}
    public string Title=>Name;
    public IReadOnlyList<HyperlibWizardTemplateOption> LayoutChoices {get;private set;}=[];
    public IReadOnlyList<HyperlibWizardTemplateOption> ElementChoices {get;private set;}=[];
    public IReadOnlyList<HyperlibWizardTemplateOption> HeaderChoices=>ElementChoices;
    public IReadOnlyList<HyperlibWizardTemplateOption> BodyChoices=>ElementChoices;
    public IReadOnlyList<HyperlibWizardTemplateOption> FooterChoices=>ElementChoices;
    public IReadOnlyList<HyperlibWizardTemplateOption> NavigationChoices=>ElementChoices;
    public IReadOnlyList<HyperlibWizardPlacementOption> Alignments {get;}=[new("left","Left"),new("center","Center"),new("right","Right")];
    public IReadOnlyList<HyperlibWizardPlacementOption> Widths {get;}=[new("comfortable","Reading width"),new("wide","Wide"),new("full","Full width")];
    public IReadOnlyList<HyperlibWizardPlacementOption> HeaderPlacements {get;}=[new("top","Top of page"),new("none","No header")];
    public IReadOnlyList<HyperlibWizardPlacementOption> FooterPlacements {get;}=[new("bottom","Bottom of page"),new("none","No footer")];
    public IReadOnlyList<HyperlibWizardPlacementOption> NavigationPlacements {get;}=[new("top","Above content"),new("left","Left sidebar"),new("right","Right sidebar"),new("bottom","Below content"),new("none","None")];
    public ObservableCollection<HyperlibWizardRepeatingObject> RepeatingObjects {get;}=[];
    public HyperlibWizardHeaderLayoutViewModel HeaderLayout{get;}=new();
    public HyperlibWizardHeaderLayoutViewModel NavigationLayout{get;}=new(true);
    public HyperlibWizardHeaderLayoutViewModel BodyLayout{get;}=new(region:"body");
    public HyperlibWizardHeaderLayoutViewModel FooterLayout{get;}=new(region:"footer");
    public HyperlibWizardRegionStyleViewModel HeaderStyle {get;}=new(); public HyperlibWizardRegionStyleViewModel BodyStyle {get;}=new(); public HyperlibWizardRegionStyleViewModel NavigationStyle {get;}=new(); public HyperlibWizardRegionStyleViewModel FooterStyle {get;}=new(); public HyperlibWizardRegionStyleViewModel ExtrasStyle {get;}=new();
    public ICommand AddPlacementCommand {get;} public ICommand RemovePlacementCommand {get;}
    public HyperlibWizardTemplateOption SelectedLayout {get=>_layout;set{if(value is not null&&SetProperty(ref _layout,value))Refresh();}}
    public HyperlibWizardTemplateOption SelectedHeader {get=>_header;set{if(value is not null&&SetProperty(ref _header,value))Refresh();}}
    public HyperlibWizardTemplateOption SelectedFooter {get=>_footer;set{if(value is not null&&SetProperty(ref _footer,value))Refresh();}}
    public HyperlibWizardTemplateOption SelectedBody {get=>_body;set{if(value is not null&&SetProperty(ref _body,value))Refresh();}}
    public HyperlibWizardTemplateOption SelectedNavigation {get=>_navigation;set{if(value is not null&&SetProperty(ref _navigation,value))Refresh();}}
    public string Background {get=>_background;set{if(SetProperty(ref _background,value??""))Refresh();}} public string Accent {get=>_accent;set{if(SetProperty(ref _accent,value??""))Refresh();}}
    public string BackgroundDesignId{get=>_backgroundDesignId;set{if(SetProperty(ref _backgroundDesignId,value??"solid"))Refresh();}}
    public string BackgroundImagePath{get=>_backgroundImagePath;set{if(SetProperty(ref _backgroundImagePath,value??""))Refresh();}}
    public string BackgroundImageFit{get=>_backgroundImageFit;set{if(SetProperty(ref _backgroundImageFit,value??"cover"))Refresh();}}
    public string PageBorderColor{get=>_pageBorderColor;set{if(SetProperty(ref _pageBorderColor,value??""))Refresh();}}
    public string PageBorderStyle{get=>_pageBorderStyle;set{if(SetProperty(ref _pageBorderStyle,value??"solid"))Refresh();}}
    public double PageBorderWidth{get=>_pageBorderWidth;set{if(SetProperty(ref _pageBorderWidth,value))Refresh();}}
    public double PageBorderRadius{get=>_pageBorderRadius;set{if(SetProperty(ref _pageBorderRadius,value))Refresh();}}
    public bool HasBackgroundImage=>BackgroundImagePath.Length>0;
    public string BackgroundDesignSummary=>BackgroundImagePath.Length>0?"Custom image":BackgroundDesignId=="solid"?"Solid color":HyperlibBackgroundDesigns.Find(BackgroundDesignId)?.Title??"Custom background";
    public void SetBackgroundColor(string color)
    {
        var changed=_backgroundTemplateId is not null||_background!=color;_backgroundTemplateId=null;
        if(!changed)return;_background=color;OnPropertyChanged(nameof(Background));Refresh();
    }
    public void SetBackgroundImage(string path)
    {
        _backgroundTemplateId=null;_backgroundImagePath=path;_backgroundDesignId=string.IsNullOrEmpty(path)?"solid":"image";
        OnPropertyChanged(nameof(BackgroundImagePath));OnPropertyChanged(nameof(BackgroundDesignId));Refresh();
    }
    public void UseSolidBackground()
    {
        _backgroundTemplateId=null;_backgroundDesignId="solid";_backgroundImagePath="";
        OnPropertyChanged(nameof(BackgroundDesignId));OnPropertyChanged(nameof(BackgroundImagePath));Refresh();
    }
    public void ApplyBackgroundDesign(HyperlibBackgroundDesign design)
    {
        _backgroundTemplateId=null;_backgroundDesignId=design.Id;_backgroundImagePath="";_background=design.DefaultColor;
        OnPropertyChanged(nameof(BackgroundDesignId));OnPropertyChanged(nameof(BackgroundImagePath));OnPropertyChanged(nameof(Background));Refresh();
    }
    public string ContentAlignment {get=>_alignment;set{if(SetProperty(ref _alignment,value))Refresh();}} public string ContentWidth {get=>_width;set{if(SetProperty(ref _width,value))Refresh();}}
    public string HeaderPlacement {get=>_headerPlacement;set{if(SetProperty(ref _headerPlacement,value))Refresh();}} public string FooterPlacement {get=>_footerPlacement;set{if(SetProperty(ref _footerPlacement,value)){OnPropertyChanged(nameof(IncludeFooter));Refresh();}}} public string NavigationPlacement {get=>_navigationPlacement;set{if(SetProperty(ref _navigationPlacement,value))Refresh();}}
    public bool IncludeFooter{get=>FooterPlacement!="none";set=>FooterPlacement=value?"bottom":"none";}
    public bool IncludeNavigation {get=>_includeNavigation;set{if(SetProperty(ref _includeNavigation,value))Refresh();}}
    public bool NavigationSticky {get=>_navigationSticky;set{if(SetProperty(ref _navigationSticky,value))Refresh();}}
    public Brush PreviewBackground=>ColorBrush(Background,Color.FromRgb(16,24,32)); public Brush PreviewAccent=>ColorBrush(Accent,Color.FromRgb(101,168,142));
    public string PlanSummary=>$"{ContentWidth} width · {ContentAlignment} alignment\nHeader: {(HeaderPlacement=="none"?"none":HeaderLayout.Enabled?$"{HeaderLayout.Blocks.Count} Blocks · {HeaderLayout.ElementCount} elements":SelectedHeader.Title)} · Footer: {(FooterPlacement=="none"?"none":FooterLayout.Enabled?$"{FooterLayout.Blocks.Count} Blocks · {FooterLayout.ElementCount} elements":SelectedFooter.Title)}\nNavigation: {(IncludeNavigation?NavigationLayout.Enabled?$"{NavigationLayout.Blocks.Count} Blocks · {NavigationLayout.ElementCount} elements":NavigationPlacement:"none")}\nBody: {(BodyLayout.Enabled?$"{BodyLayout.Blocks.Count} Blocks · {BodyLayout.ElementCount} elements":SelectedBody.Title)} · {RepeatingObjects.Count} additional objects";
    public string BackgroundValidationMessage=>!Regex.IsMatch(Background,"^#[0-9a-fA-F]{6}$")?"Use a background color such as #101820.":!Regex.IsMatch(Accent,"^#[0-9a-fA-F]{6}$")?"Use an accent color such as #65A88E.":!Regex.IsMatch(PageBorderColor,"^#[0-9a-fA-F]{6}$")?"Use a border color such as #65A88E.":!double.IsFinite(PageBorderWidth)||PageBorderWidth is <0 or >20||!double.IsFinite(PageBorderRadius)||PageBorderRadius is <0 or >200?"Use border width 0–20 and corner radius 0–200.":PageBorderStyle is not("none" or "solid" or "dashed" or "dotted" or "double")?"Choose a supported border style.":BackgroundImageFit is not("cover" or "contain" or "tile")?"Choose how the background image should fit.":BackgroundDesignId is not("solid" or "image")&&HyperlibBackgroundDesigns.Find(BackgroundDesignId) is null?"Choose one of the available background designs.":"";
    public string ValidationMessage=>BackgroundValidationMessage.Length>0?BackgroundValidationMessage:HeaderLayout.ValidationMessage.Length>0?HeaderLayout.ValidationMessage:NavigationLayout.ValidationMessage.Length>0?NavigationLayout.ValidationMessage:BodyLayout.ValidationMessage.Length>0?BodyLayout.ValidationMessage:FooterLayout.ValidationMessage.Length>0?FooterLayout.ValidationMessage:new[]{HeaderStyle,BodyStyle,NavigationStyle,FooterStyle,ExtrasStyle}.FirstOrDefault(style=>style.ValidationMessage.Length>0)?.ValidationMessage??"";
    internal void UpdateChoices(IReadOnlyList<HyperlibWorkTemplateChoice> all)
    {
        LayoutChoices=new[]{Empty}.Concat(all.Where(item=>item.Kind=="layout").Select(Option)).ToArray(); ElementChoices=new[]{Empty}.Concat(all.Where(item=>item.Kind is "element" or "preset"||item.ComponentName.Length>0).Select(Option)).ToArray();
    }
    private static HyperlibWizardTemplateOption Option(HyperlibWorkTemplateChoice item)=>new(item.Id,item.Title,item.Description);
    public void ApplyObject(HyperlibWorkTemplateChoice item,string stage)
    {
        var choice=Option(item);
        switch(stage){case "background":_backgroundTemplateId=item.Id;break;case "header":SelectedHeader=choice;HeaderPlacement="top";break;case "footer":SelectedFooter=choice;FooterPlacement="bottom";break;case "navigation":SelectedNavigation=choice;IncludeNavigation=true;if(NavigationPlacement=="none")NavigationPlacement="top";break;case "body" when item.Kind=="layout":SelectedLayout=choice;break;case "body" when SelectedBody.Id is null:SelectedBody=choice;break;case "body":AddPlacement(choice,"main");break;case "extras":AddPlacement(choice,"sidebar");break;}Refresh();
    }
    private void AddPlacement(HyperlibWizardTemplateOption choice,string region){var placement=new HyperlibWizardRepeatingObject(choice,ElementChoices){Region=region};placement.PropertyChanged+=(_,_)=>Refresh();RepeatingObjects.Add(placement);}
    public void ClearStage(string stage){switch(stage){case "background":_backgroundTemplateId=null;break;case "header":SelectedHeader=Empty;HeaderPlacement="none";break;case "footer":FooterLayout.LoadDefinition(new());SelectedFooter=Empty;FooterPlacement="none";break;case "navigation":SelectedNavigation=Empty;IncludeNavigation=false;break;case "body":BodyLayout.LoadDefinition(new());_bodyLayoutTemplateId=null;SelectedBody=Empty;foreach(var item in RepeatingObjects.Where(item=>item.Region=="main").ToArray())RepeatingObjects.Remove(item);break;case "extras":foreach(var item in RepeatingObjects.Where(item=>item.Region=="sidebar").ToArray())RepeatingObjects.Remove(item);break;}Refresh();}
    public void RemoveObject(string id,string stage){foreach(var item in RepeatingObjects.Where(item=>item.SelectedObject.Id==id).ToArray())RepeatingObjects.Remove(item);Refresh();}
    public string GetStageSummary(string stage)=>stage switch{"background"=>_backgroundTemplateId is null?"Custom background color":"Library surface selected","header"=>HeaderPlacement=="none"?"No header":SelectedHeader.Id is null?"Built-in header":SelectedHeader.Title,"footer"=>FooterPlacement=="none"?"No footer":FooterLayout.Enabled?$"{FooterLayout.Blocks.Count} Blocks · {FooterLayout.ElementCount} elements":SelectedFooter.Id is null?"Built-in footer":SelectedFooter.Title,"navigation"=>IncludeNavigation?SelectedNavigation.Id is null?"Connected Work navigation":SelectedNavigation.Title:"No navigation","body"=>BodyLayout.Enabled?$"{BodyLayout.Blocks.Count} Blocks · {BodyLayout.ElementCount} elements":(SelectedBody.Id is null?"Empty content area":SelectedBody.Title)+$" · {RepeatingObjects.Count(item=>item.Region=="main")} additional slots",_=>$"{RepeatingObjects.Count(item=>item.Region=="sidebar")} optional objects"};
    public IEnumerable<string> UsedIds()=>new[]{_backgroundTemplateId,_bodyLayoutTemplateId,_completeTemplateObjectId,_completeTemplateAppearanceId,SelectedLayout.Id,SelectedHeader.Id,SelectedFooter.Id,SelectedBody.Id,SelectedNavigation.Id}.Concat(RepeatingObjects.Select(item=>item.SelectedObject.Id)).Where(id=>id is not null).Cast<string>()
        .Concat(new[]{HeaderLayout,NavigationLayout,BodyLayout,FooterLayout}.SelectMany(layout=>HyperlibHeaderLayouts.Elements(layout.BuildDefinition())).Where(item=>item.Kind=="template"&&!string.IsNullOrWhiteSpace(item.TemplateObjectId)).Select(item=>item.TemplateObjectId));
    public HyperlibPageTemplateDefinition BuildDefinition()=>new(){CompleteTemplateObjectId=_completeTemplateObjectId,CompleteTemplateAppearanceId=_completeTemplateAppearanceId,BackgroundTemplateId=_backgroundTemplateId,BackgroundSurface=new(){DesignId=BackgroundDesignId,ImagePath=BackgroundImagePath,ImageFit=BackgroundImageFit,BorderColor=PageBorderColor,BorderWidth=PageBorderWidth,BorderStyle=PageBorderStyle,BorderRadius=PageBorderRadius},HeaderLayout=HeaderLayout.BuildDefinition(),NavigationLayout=NavigationLayout.BuildDefinition(),BodyLayout=BodyLayout.BuildDefinition(),FooterLayout=FooterLayout.BuildDefinition(),NavigationSticky=NavigationSticky,LayoutTemplateId=SelectedLayout.Id,BodyLayoutTemplateId=_bodyLayoutTemplateId,HeaderTemplateId=SelectedHeader.Id,FooterTemplateId=SelectedFooter.Id,BodyTemplateId=SelectedBody.Id,NavigationTemplateId=SelectedNavigation.Id,BackgroundColor=Background,AccentColor=Accent,ContentAlignment=ContentAlignment,ContentWidth=ContentWidth,HeaderPlacement=HeaderPlacement,FooterPlacement=FooterPlacement,NavigationPlacement=NavigationPlacement,IncludeNavigation=IncludeNavigation,HeaderStyle=HeaderStyle.Build(),BodyStyle=BodyStyle.Build(),NavigationStyle=NavigationStyle.Build(),FooterStyle=FooterStyle.Build(),ExtrasStyle=ExtrasStyle.Build(),RepeatingObjects=RepeatingObjects.Select((item,order)=>new HyperlibTemplatePlacementDefinition{Id=item.Id,TemplateObjectId=item.SelectedObject.Id!,Region=item.Region,Order=order}).ToList()};
    internal void LoadDefinition(HyperlibPageTemplateDefinition definition)
    {
        HyperlibWizardTemplateOption Find(string? id,IReadOnlyList<HyperlibWizardTemplateOption> choices)=>choices.FirstOrDefault(item=>item.Id==id)??Empty;
        _backgroundTemplateId=definition.BackgroundTemplateId;_bodyLayoutTemplateId=definition.BodyLayoutTemplateId;_completeTemplateObjectId=definition.CompleteTemplateObjectId;_completeTemplateAppearanceId=definition.CompleteTemplateAppearanceId;
        var surface=definition.BackgroundSurface??new HyperlibPageBackgroundDefinition();BackgroundDesignId=surface.DesignId;BackgroundImagePath=surface.ImagePath;BackgroundImageFit=surface.ImageFit;PageBorderColor=surface.BorderColor;PageBorderWidth=surface.BorderWidth;PageBorderStyle=surface.BorderStyle;PageBorderRadius=surface.BorderRadius;
        SelectedLayout=Find(definition.LayoutTemplateId,LayoutChoices);SelectedHeader=Find(definition.HeaderTemplateId,ElementChoices);SelectedFooter=Find(definition.FooterTemplateId,ElementChoices);SelectedBody=Find(definition.BodyTemplateId,ElementChoices);SelectedNavigation=Find(definition.NavigationTemplateId,ElementChoices);
        Background=definition.BackgroundColor;Accent=definition.AccentColor;ContentAlignment=definition.ContentAlignment;ContentWidth=definition.ContentWidth;HeaderPlacement=definition.HeaderPlacement;FooterPlacement=definition.FooterPlacement;NavigationPlacement=definition.NavigationPlacement;IncludeNavigation=definition.IncludeNavigation;
        HeaderLayout.LoadDefinition(definition.HeaderLayout??new HyperlibHeaderLayoutDefinition());NavigationLayout.LoadDefinition(definition.NavigationLayout??new HyperlibHeaderLayoutDefinition());BodyLayout.LoadDefinition(definition.BodyLayout??new HyperlibHeaderLayoutDefinition());FooterLayout.LoadDefinition(definition.FooterLayout??new HyperlibHeaderLayoutDefinition());NavigationSticky=definition.NavigationSticky;HeaderStyle.Load(definition.HeaderStyle);BodyStyle.Load(definition.BodyStyle);NavigationStyle.Load(definition.NavigationStyle);FooterStyle.Load(definition.FooterStyle);ExtrasStyle.Load(definition.ExtrasStyle);RepeatingObjects.Clear();
        foreach(var item in definition.RepeatingObjects.OrderBy(item=>item.Order)){var choice=Find(item.TemplateObjectId,ElementChoices);if(choice.Id is null)continue;var placement=new HyperlibWizardRepeatingObject(choice,ElementChoices){Id=string.IsNullOrWhiteSpace(item.Id)?"slot-"+Guid.NewGuid().ToString("N"):item.Id,Region=item.Region};placement.PropertyChanged+=(_,_)=>Refresh();RepeatingObjects.Add(placement);}Refresh();
    }
    private static Brush ColorBrush(string value,Color fallback){try{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));}catch{return new SolidColorBrush(fallback);}}
    private void Refresh(){foreach(var name in new[]{nameof(Title),nameof(PreviewBackground),nameof(PreviewAccent),nameof(PlanSummary),nameof(ValidationMessage),nameof(BackgroundValidationMessage),nameof(HasBackgroundImage),nameof(BackgroundDesignSummary)})OnPropertyChanged(name);DefinitionChanged?.Invoke();}
}

public sealed class HyperlibWizardRegionStyleViewModel : ObservableObject
{
    private string _background="",_textColor="",_borderColor="",_font="inherit",_alignment="left",_logoPlacement="left",_logoPath="",_text="";
    private double _borderWidth,_borderRadius,_padding=16,_fontSize=16;
    public IReadOnlyList<string> Fonts {get;}=["inherit","system-ui","Arial","Georgia","monospace"];
    public IReadOnlyList<string> Alignments {get;}=["left","center","right"];
    public IReadOnlyList<string> LogoPlacements {get;}=["left","center","right","none"];
    public string BackgroundColor {get=>_background;set=>SetProperty(ref _background,value??"");} public string TextColor {get=>_textColor;set=>SetProperty(ref _textColor,value??"");} public string BorderColor {get=>_borderColor;set=>SetProperty(ref _borderColor,value??"");}
    public double BorderWidth {get=>_borderWidth;set=>SetProperty(ref _borderWidth,value);} public double BorderRadius {get=>_borderRadius;set=>SetProperty(ref _borderRadius,value);} public double Padding {get=>_padding;set=>SetProperty(ref _padding,value);} public double FontSize {get=>_fontSize;set=>SetProperty(ref _fontSize,value);}
    public string FontFamily {get=>_font;set=>SetProperty(ref _font,value??"inherit");} public string Alignment {get=>_alignment;set=>SetProperty(ref _alignment,value??"left");} public string LogoPlacement {get=>_logoPlacement;set=>SetProperty(ref _logoPlacement,value??"left");} public string LogoPath {get=>_logoPath;set=>SetProperty(ref _logoPath,value??"");} public string Text {get=>_text;set=>SetProperty(ref _text,value??"");}
    public string ValidationMessage=>new[]{BackgroundColor,TextColor,BorderColor}.Any(value=>value.Length>0&&!Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$"))?"Use six-digit colors, such as #101820, or leave a color empty to inherit.":new[]{BorderWidth,BorderRadius,Padding,FontSize}.Any(value=>!double.IsFinite(value))||BorderWidth is <0 or >20||BorderRadius is <0 or >200||Padding is <0 or >200||FontSize is <8 or >100?"Use border 0–20, radius and padding 0–200, and font size 8–100.":FontFamily.Length>100||!Regex.IsMatch(FontFamily,"^[A-Za-z0-9 ,_-]+$")?"Choose a font family of 100 characters or fewer using letters, numbers, spaces, commas, or hyphens.":Text.Length>500?"Keep reusable header and footer text to 500 characters or fewer.":"";
    public HyperlibTemplateRegionStyle Build()=>new(){BackgroundColor=BackgroundColor,TextColor=TextColor,BorderColor=BorderColor,BorderWidth=BorderWidth,BorderRadius=BorderRadius,Padding=Padding,FontFamily=FontFamily,FontSize=FontSize,Alignment=Alignment,LogoPlacement=LogoPlacement,LogoPath=LogoPath,Text=Text};
    public void Load(HyperlibTemplateRegionStyle style){BackgroundColor=style.BackgroundColor;TextColor=style.TextColor;BorderColor=style.BorderColor;BorderWidth=style.BorderWidth;BorderRadius=style.BorderRadius;Padding=style.Padding;FontFamily=style.FontFamily;FontSize=style.FontSize;Alignment=style.Alignment;LogoPlacement=style.LogoPlacement;LogoPath=style.LogoPath;Text=style.Text;}
}
public sealed class HyperlibWizardRepeatingObject : ObservableObject
{
    private HyperlibWizardTemplateOption _selected;private string _region="main";
    public HyperlibWizardRepeatingObject(HyperlibWizardTemplateOption selected,IReadOnlyList<HyperlibWizardTemplateOption> choices){_selected=selected;Choices=choices;}
    public string Id {get;set;}="slot-"+Guid.NewGuid().ToString("N"); public IReadOnlyList<HyperlibWizardTemplateOption> Choices {get;private set;}
    public IReadOnlyList<HyperlibWizardPlacementOption> Regions {get;}=[new("header","Header"),new("main","Main content"),new("sidebar","Sidebar"),new("footer","Footer")];
    public HyperlibWizardTemplateOption SelectedObject {get=>_selected;set{if(value is not null)SetProperty(ref _selected,value);}} public string Region {get=>_region;set=>SetProperty(ref _region,value);}
    internal void UpdateChoices(IReadOnlyList<HyperlibWizardTemplateOption> choices){Choices=choices;OnPropertyChanged(nameof(Choices));}
}
