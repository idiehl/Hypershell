using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;

namespace Hypershell.ViewModels;

/// <summary>Native library selection and exposed values. Source objects are always read-only.</summary>
public sealed class HyperlibHeaderTemplateLibraryViewModel : ObservableObject
{
    private readonly HyperlibWorkWizardViewModel _wizard;
    private string _search="",_group="All collections",_category="All objects",_message="";
    private HyperlibWizardLibraryObject? _selected;
    private IReadOnlyList<HyperlibWizardLibraryObject> _filtered=[];
    public HyperlibHeaderTemplateLibraryViewModel(HyperlibWorkWizardViewModel wizard)
    {
        _wizard=wizard;Groups=wizard.Groups;Categories=wizard.Categories;Filter();
    }
    public IReadOnlyList<string> Groups{get;}
    public IReadOnlyList<string> Categories{get;}
    public IReadOnlyList<HyperlibWizardLibraryObject> Objects=>_filtered;
    public string Search{get=>_search;set{if(SetProperty(ref _search,value??""))Filter();}}
    public string Group{get=>_group;set{if(SetProperty(ref _group,value??"All collections"))Filter();}}
    public string Category{get=>_category;set{if(SetProperty(ref _category,value??"All objects"))Filter();}}
    public string Summary=>$"{Objects.Count} objects · {_wizard.LibraryObjects.Count} in your library";
    public string Message{get=>_message;private set=>SetProperty(ref _message,value);}
    public HyperlibWizardLibraryObject? Selected
    {
        get=>_selected;set
        {
            if(!SetProperty(ref _selected,value))return;
            try{Source=value is null?null:_wizard.GetLibraryObject(value.Source);Message=Source is null&&value is not null?"This object's saved definition is unavailable. Reconnect its collection to insert it.":"";}
            catch(Exception error){Source=null;Message="This object is unavailable: "+error.Message;}
            foreach(var name in new[]{nameof(Source),nameof(CanInsert),nameof(ActionLabel),nameof(Purpose)})OnPropertyChanged(name);
        }
    }
    public TemplateLibraryObject? Source{get;private set;}
    public bool CanInsert=>Selected is not null&&Source is not null;
    public string ActionLabel=>Source?.Kind switch{"palette" or "style" or "typography"=>"Apply appearance","region"=>"Insert Box",_=>"Insert Template"};
    public string Purpose=>Source?.Kind switch
    {
        "palette" or "style" or "typography"=>"Creates a new Box using this collection's supported colors and fonts. Edit them or add elements inside the Box.",
        "region"=>"Creates an editable Box for this region. Add Text, media, other Boxes, or Templates inside it.",
        "layout"=>"Inserts this layout as one Template element. Each included object's exposed properties remain editable.",
        _=>"Inserts a reusable library object. Its exposed properties, size, and position remain editable in the header."
    };
    private void Filter()
    {
        var search=Search.Trim();var previous=Selected?.Id;
        _filtered=_wizard.LibraryObjects.Where(item=>(Group=="All collections"||item.GroupTitle==Group)
            &&(Category=="All objects"||item.Category==Category)
            &&(search.Length==0||(item.SearchText+" "+TemplateObjectLibrary.CatalogIdFor(item.Source.GroupId,item.Source.ObjectId)).Contains(search,StringComparison.OrdinalIgnoreCase))).ToArray();
        OnPropertyChanged(nameof(Objects));OnPropertyChanged(nameof(Summary));Selected=_filtered.FirstOrDefault(item=>item.Id==previous)??_filtered.FirstOrDefault();
    }
}

public sealed record HyperlibHeaderTemplateValueGroup(string Title,IReadOnlyList<HyperlibHeaderTemplateValueViewModel> Fields);

/// <summary>A field keeps invalid native text separate from the last valid JSON value.</summary>
public sealed class HyperlibHeaderTemplateValueViewModel : ObservableObject
{
    private string _text,_error="";
    private readonly Action<JsonElement> _commit;
    private readonly Action _changed;
    private readonly JsonElement _inherited;
    private readonly Action? _reset;
    public HyperlibHeaderTemplateValueViewModel(FieldDefinition definition,JsonElement value,Action<JsonElement> commit,Action changed,JsonElement? inherited=null,Action? reset=null)
    {Definition=ContentJson.Clone(definition);_text=HyperlibTemplateEditSession.ValueText(value);_commit=commit;_changed=changed;_inherited=(inherited??HyperlibTemplateEditSession.InitialValue(definition)).Clone();_reset=reset;}
    public FieldDefinition Definition{get;}
    public string Title=>Definition.Title+(Definition.Required?" *":"");
    public string Type=>Definition.Type;
    public IReadOnlyList<string> Options=>Definition.Options;
    public string Error{get=>_error;private set=>SetProperty(ref _error,value);}
    public string Text
    {
        get=>_text;set
        {
            if(!SetProperty(ref _text,value??""))return;
            try
            {
                var parsed=HyperlibTemplateEditSession.ParseValue(Type,_text);Validate(parsed);Error="";_commit(parsed);
            }
            catch(Exception error) when(error is FormatException or ContentValidationException){Error=error.Message;}
            OnPropertyChanged(nameof(BooleanValue));_changed();
        }
    }
    public bool BooleanValue{get=>bool.TryParse(Text,out var value)&&value;set=>Text=value?"true":"false";}
    internal void UpdateFromOwner(JsonElement value){_text=HyperlibTemplateEditSession.ValueText(value);Error="";OnPropertyChanged(nameof(Text));OnPropertyChanged(nameof(BooleanValue));}
    public void Reset()
    {
        if(_reset is null){Text=HyperlibTemplateEditSession.ValueText(_inherited);return;}
        _text=HyperlibTemplateEditSession.ValueText(_inherited);Error="";_reset();OnPropertyChanged(nameof(Text));OnPropertyChanged(nameof(BooleanValue));_changed();
    }
    private void Validate(JsonElement value)
    {
        if(Type=="number")
        {
            var number=value.GetDouble();if(Definition.Min is {} min&&number<min||Definition.Max is {} max&&number>max)
                throw new ContentValidationException($"Use a value between {Definition.Min?.ToString()??"the minimum"} and {Definition.Max?.ToString()??"the maximum"}.");
        }
        var text=value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
        if(text.Length>200000)throw new ContentValidationException("Keep this value to 200,000 characters or fewer.");
        if(Type=="select"&&!Options.Contains(text))throw new ContentValidationException("Choose one of the listed options.");
        if(Type=="color"&&!Regex.IsMatch(text,"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))throw new ContentValidationException("Use a hexadecimal color such as #65A88E.");
        if(Type is "url" or "media"&&text.Length>0&&!TemplateValidator.IsSafeUrl(text)
            &&!(Type=="media"&&System.IO.Path.IsPathFullyQualified(text)))throw new ContentValidationException("Use an HTTP(S) address or a local media file.");
    }
}

public static class HyperlibHeaderTemplateLibrary
{
    public static bool SupportsAppearance(FieldDefinition definition)=>definition.Id switch
    {
        "background" or "paper" or "ink" or "text" or "accent" or "surface" or "muted" or "border" or "secondary"=>definition.Type is "color" or "string" or "select",
        "font" or "fontFamily" or "headingFont"=>definition.Type is "string" or "select",
        "fontSize"=>definition.Type=="number",
        _=>false
    };
    public static HyperlibHeaderElementDefinition CreateElement(HyperlibWorkTemplateChoice choice,TemplateLibraryObject source)
    {
        var visual=source.Kind is "element" or "preset" or "layout";
        var value=new HyperlibHeaderElementDefinition{Id="header-element-"+Guid.NewGuid().ToString("N"),Kind=visual?"template":"box",TemplateObjectId=choice.Id,TemplateTitle=choice.Title,
            Height=visual?0:110,BorderWidth=visual?0:1,BorderRadius=visual?0:6,Padding=visual?0:10};
        if(!visual)
        {
            foreach(var field in source.Appearance.Where(field=>SupportsAppearance(field)&&field.DefaultValue.HasValue))value.TemplateAppearance[field.Id]=field.DefaultValue!.Value.Clone();
            string Read(params string[] keys)=>keys.Select(key=>value.TemplateAppearance.TryGetValue(key,out var found)&&found.ValueKind==JsonValueKind.String?found.GetString():null).FirstOrDefault(text=>!string.IsNullOrEmpty(text))??"";
            static string NativeColor(string color)=>Regex.IsMatch(color,"^#[0-9a-fA-F]{3}$")?"#"+string.Concat(color.Skip(1).Select(character=>new string(character,2))):Regex.IsMatch(color,"^#[0-9a-fA-F]{8}$")?color[..7]:color;
            value.FillColor=NativeColor(Read("background","paper","surface","panel"));var ink=NativeColor(Read("ink","text"));if(ink.Length>0)value.TextColor=ink;
            var font=Read("font");if(font.Length>0)value.FontFamily=font switch{"system"=>"system-ui","serif"=>"Georgia","humanist"=>"Segoe UI","geometric"=>"Arial",_=>font};
            if(value.TemplateAppearance.TryGetValue("fontSize",out var size)&&size.ValueKind==JsonValueKind.Number&&size.TryGetDouble(out var number)&&number is >=8 and <=200)value.FontSize=number;
        }
        return value;
    }
    public static IEnumerable<(string Key,BlockDefinition Schema,PageBlock? Block)> SourceBlocks(TemplateLibraryObject source)
    {
        if(source.Element is {} element){yield return("element",element,source.Blocks.FirstOrDefault());yield break;}
        foreach(var block in source.Blocks){var schema=source.Definition.Blocks.FirstOrDefault(item=>item.Id==block.Type);if(schema is not null)yield return(block.Id,schema,block);}
    }
}
