using HyperLib.Core;
using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;

namespace Hypershell.ViewModels;

/// <summary>Whole-site template selection, independent of the scratch builder and source library.</summary>
public sealed class StudioProjectTemplateGalleryViewModel : ObservableObject
{
    private readonly StudioCompleteTemplateService? _service;
    private string _search="",_collection="All collections",_category="All categories",_projectTitle="My Work",_message="";
    private bool _isActive;
    private StudioProjectKind _projectKind;
    private StudioCompleteTemplateChoice? _selected;
    private IReadOnlyList<StudioCompleteTemplateChoice> _filtered=[];
    private HyperlibWorkProjectDefinition? _definition;
    private string? _preparedId,_preparedTitle;
    public StudioProjectTemplateGalleryViewModel(string? libraryRoot)
    {
        TemplateLibraryRoot=libraryRoot;
        try
        {
            _service=new StudioCompleteTemplateService(libraryRoot);TemplateLibraryRoot=_service.TemplateLibraryRoot??libraryRoot;Templates=_service.List();
        }
        catch(Exception error){_message=error.Message;}
        Collections=new[]{"All collections"}.Concat(Templates.Select(item=>item.Collection).Distinct().Order()).ToArray();
        Categories=new[]{"All categories"}.Concat(Templates.Select(item=>item.Category).Distinct().Order()).ToArray();
        Filter();
    }
    public string? TemplateLibraryRoot{get;}
    public IReadOnlyList<StudioCompleteTemplateChoice> Templates{get;}=[];
    public IReadOnlyList<StudioCompleteTemplateChoice> FilteredTemplates=>_filtered;
    public IReadOnlyList<string> Collections{get;}
    public IReadOnlyList<string> Categories{get;}
    public string Search{get=>_search;set{if(SetProperty(ref _search,value??""))Filter();}}
    public string Collection{get=>_collection;set{if(SetProperty(ref _collection,value??"All collections"))Filter();}}
    public string Category{get=>_category;set{if(SetProperty(ref _category,value??"All categories"))Filter();}}
    public string Summary=>$"{FilteredTemplates.Count} templates · {Templates.Count} in your library";
    public bool HasMatches=>FilteredTemplates.Count>0;
    public string EmptyMessage=>Templates.Count==0?"No complete templates are available.":"No matches. Try another search or collection.";
    public string Message{get=>_message;private set{if(SetProperty(ref _message,value)){OnPropertyChanged(nameof(HasMessage));OnPropertyChanged(nameof(CanCreate));}}}
    public bool HasMessage=>Message.Length>0;
    public bool IsActive{get=>_isActive;set{if(SetProperty(ref _isActive,value)&&value)PrepareDefinition();}}
    public string ProjectTitle{get=>_projectTitle;set{if(SetProperty(ref _projectTitle,value??""))PrepareDefinition();}}
    public StudioCompleteTemplateChoice? Selected
    {
        get=>_selected;set{if(SetProperty(ref _selected,value)){PrepareDefinition();OnPropertyChanged(nameof(HasSelection));OnPropertyChanged(nameof(CanCreate));OnPropertyChanged(nameof(AvailabilityMessage));}}
    }
    public StudioProjectKind ProjectKind{get=>_projectKind;set{if(SetProperty(ref _projectKind,value)){OnPropertyChanged(nameof(CanCreate));OnPropertyChanged(nameof(AvailabilityMessage));}}}
    public string AvailabilityMessage=>Selected is {} selected&&!selected.SupportedKinds.Contains(ProjectKind)?$"This template is available for {string.Join(", ",selected.SupportedKinds.Select(kind=>kind==StudioProjectKind.Hyperlib?"Hyperlib Works":kind==StudioProjectKind.Razor?"Razor Pages":"HTML"))}. Choose a compatible template or change the project type.":"";
    public bool HasSelection=>Selected is not null;
    public bool CanCreate=>Selected is not null&&_definition is not null&&Message.Length==0&&AvailabilityMessage.Length==0;
    public HyperlibWorkProjectDefinition? PreviewDefinition=>_definition;
    public HyperlibWorkProjectDefinition BuildDefinition()
    {
        PrepareDefinition();
        if(!CanCreate||_definition is null)throw new InvalidOperationException(Message.Length>0?Message:AvailabilityMessage.Length>0?AvailabilityMessage:"Choose a complete template before creating the project.");
        return ContentJson.Clone(_definition);
    }
    private void PrepareDefinition()
    {
        if(!IsActive)return;
        if(Selected is null){_definition=null;_preparedId=null;OnPropertyChanged(nameof(PreviewDefinition));OnPropertyChanged(nameof(CanCreate));return;}
        if(_preparedId==Selected.Id&&_preparedTitle==ProjectTitle.Trim()&&_definition is not null)return;
        try
        {
            _definition=_service?.BuildDefinition(Selected.Id,ProjectTitle.Trim())??throw new InvalidOperationException("The template library is unavailable.");
            _preparedId=Selected.Id;_preparedTitle=ProjectTitle.Trim();Message="";
        }
        catch(Exception error){_definition=null;_preparedId=null;Message="This template could not be prepared: "+error.Message;}
        OnPropertyChanged(nameof(PreviewDefinition));OnPropertyChanged(nameof(CanCreate));
    }
    private void Filter()
    {
        var previous=Selected?.Id;var search=Search.Trim();
        _filtered=Templates.Where(item=>(Collection=="All collections"||item.Collection==Collection)&&(Category=="All categories"||item.Category==Category)
            &&(search.Length==0||$"{item.Title} {item.Description} {item.Category} {item.Collection} {item.Id}".Contains(search,StringComparison.OrdinalIgnoreCase))).ToArray();
        OnPropertyChanged(nameof(FilteredTemplates));OnPropertyChanged(nameof(Summary));OnPropertyChanged(nameof(HasMatches));OnPropertyChanged(nameof(EmptyMessage));
        Selected=_filtered.FirstOrDefault(item=>item.Id==previous)??_filtered.FirstOrDefault();
    }
}
