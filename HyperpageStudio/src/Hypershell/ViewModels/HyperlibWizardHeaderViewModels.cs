using System.Collections.ObjectModel;
using System.Text.Json;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HyperLib.Core;
using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;

namespace Hypershell.ViewModels;

/// <summary>Editable header rows and recursive Boxes. Selection is UI state, separate from the recipe.</summary>
public sealed class HyperlibWizardHeaderLayoutViewModel : ObservableObject
{
    private bool _enabled,_loading,_pending,_structurePending;
    private int _batch;
    private HyperlibWizardHeaderElementViewModel? _selected;
    private HyperlibHeaderLayoutDefinition? _undoSnapshot;
    public Func<string>? BackgroundColorProvider{get;set;}
    public Func<string,TemplateLibraryObject?>? TemplateObjectProvider{get;set;}
    public ObservableCollection<HyperlibWizardHeaderBlockViewModel> Blocks{get;}=[];
    public event Action? DefinitionChanged;
    public event Action? StructureChanged;
    public event Action? SelectionChanged;
    public bool IsNavigation { get; }
    public bool IsBody { get; }
    public bool IsFooter { get; }
    public HyperlibWizardHeaderLayoutViewModel(bool isNavigation=false,string region="header")
    {if(region is not("header" or "navigation" or "body" or "footer"))throw new ArgumentException("Choose a supported builder region.",nameof(region));
        IsNavigation=isNavigation||region=="navigation";IsBody=region=="body";IsFooter=region=="footer";Blocks.CollectionChanged+=(_,_)=>Changed(true);}
    public bool Enabled{get=>_enabled;set{if(SetProperty(ref _enabled,value)&&!_loading){_pending=true;if(_batch==0)Emit();}}}
    public bool HasBlocks=>Blocks.Count>0;
    public bool CanUndo=>_undoSnapshot is not null;
    public HyperlibWizardHeaderElementViewModel? SelectedElement
    {
        get=>_selected;set{if(SetProperty(ref _selected,value)){OnPropertyChanged(nameof(HasSelection));SelectionChanged?.Invoke();}}
    }
    public bool HasSelection=>SelectedElement is not null;
    public IReadOnlyList<HyperlibHeaderPreset> Presets=>IsNavigation?HyperlibNavigationLayouts.All:IsBody?HyperlibBodyLayouts.All:IsFooter?HyperlibFooterLayouts.All:HyperlibHeaderLayouts.All;
    public HyperlibWizardHeaderBlockViewModel AddBlock()
    {
        HyperlibWizardHeaderBlockViewModel? result=null;Batch(()=>{result=new(this,new HyperlibHeaderBlockDefinition{Id=NewId(IsBody?"body-block":IsFooter?"footer-block":"header-block"),Height=IsNavigation?72:IsBody?240:160,Arrangement="horizontal"});Blocks.Add(result);});return result!;
    }
    public HyperlibWizardHeaderElementViewModel AddElement(HyperlibWizardHeaderBlockViewModel block,string kind,HyperlibWizardHeaderElementViewModel? box=null)
    {
        if(kind is not("text" or "box" or "image" or "video" or "navigation"))throw new ArgumentException("Choose Text, Box, Image, Video, or Navigation links.",nameof(kind));
        if(box is not null&&box.Kind!="box")throw new InvalidOperationException("Only a Box can contain other elements.");
        HyperlibWizardHeaderElementViewModel? result=null;
        Batch(()=>
        {
            var definition=new HyperlibHeaderElementDefinition{Id=NewId(IsBody?"body-element":IsFooter?"footer-element":"header-element"),Kind=kind,Text=kind=="text"?(IsBody?"":IsFooter?"Footer text":"Header text"):"",Height=kind=="box"?110:kind is "image" or "video"?96:0,TextColor=ReadableText(block,box),
                Arrangement="positioned",WidthMode=kind is "image" or "video"?"pixels":"percent",WidthPixels=128,
                FillColor=kind=="box"?"#1c2a33":"",BorderWidth=kind=="box"?1:0,BorderRadius=kind=="box"?6:0,Padding=kind=="box"?10:0,Alt=kind=="image"?(IsBody?"Body image":IsFooter?"Footer image":"Header image"):kind=="video"?(IsBody?"Body video":IsFooter?"Footer video":"Header video"):""};
            if(kind=="navigation"){definition.Navigation=new(){TextColor=ReadableText(block,box)};definition.FontSize=16;}
            if(IsFooter&&kind=="navigation"){definition.Navigation!.AutoPages=false;definition.Navigation.ShowPreviousNext=true;definition.Navigation.CollapseOnSmallScreens=false;definition.Navigation.HomeLabel="Work home";}
            if(IsBody&&kind is "text" or "image" or "video"){definition.IsContentSlot=true;definition.SlotName=kind=="text"?"Text area":kind=="image"?"Image slot":"Video slot";}
            result=new(this,definition);var siblings=box?.Children??block.Elements;
            if((box?.Arrangement??block.Arrangement)=="positioned")PlaceInFreeSpace(siblings,result);siblings.Add(result);SelectedElement=result;
        });return result!;
    }
    public HyperlibWizardHeaderElementViewModel AddLibraryObject(HyperlibWizardHeaderBlockViewModel block,HyperlibWorkTemplateChoice choice,TemplateLibraryObject source,HyperlibWizardHeaderElementViewModel? box=null)
    {
        if(!Blocks.Contains(block)||box is not null&&(box.Kind!="box"||FindBlock(box)!=block))throw new InvalidOperationException("Choose a current Block or Box before inserting a Template.");
        HyperlibWizardHeaderElementViewModel? result=null;
        Batch(()=>
        {
            var definition=HyperlibHeaderTemplateLibrary.CreateElement(choice,source);if(definition.Kind=="box")definition.Arrangement="positioned";
            if(IsBody&&definition.Kind=="template"){definition.IsContentSlot=true;definition.SlotName=choice.Title;}
            result=new(this,definition,source);var siblings=box?.Children??block.Elements;
            if((box?.Arrangement??block.Arrangement)=="positioned")PlaceInFreeSpace(siblings,result);siblings.Add(result);SelectedElement=result;
        });return result!;
    }
    public void RemoveBlock(HyperlibWizardHeaderBlockViewModel block)=>Batch(()=>{if(block.Elements.Any(item=>Contains(item,SelectedElement)))SelectedElement=null;Blocks.Remove(block);});
    public void MoveBlock(HyperlibWizardHeaderBlockViewModel block,int direction){var index=Blocks.IndexOf(block);var target=index+direction;if(index>=0&&target>=0&&target<Blocks.Count)Batch(()=>Blocks.Move(index,target));}
    public void RemoveElement(HyperlibWizardHeaderElementViewModel element)
    {
        var siblings=FindSiblings(element);if(siblings is null)return;Batch(()=>{if(Contains(element,SelectedElement))SelectedElement=null;siblings.Remove(element);});
    }
    public void MoveElement(HyperlibWizardHeaderElementViewModel element,int direction)
    {
        var siblings=FindSiblings(element);if(siblings is null)return;var index=siblings.IndexOf(element);var target=index+direction;
        if(target>=0&&target<siblings.Count)Batch(()=>{if(ParentArrangement(element)=="positioned")
            {var other=siblings[target];(element.X,other.X)=(other.X,element.X);(element.Y,other.Y)=(other.Y,element.Y);(element.PositionAlignment,other.PositionAlignment)=(other.PositionAlignment,element.PositionAlignment);}
            siblings.Move(index,target);});
    }
    /// <summary>Commits one finished preview drag without replaying pointer updates into the undo history.</summary>
    public bool TryMoveElement(string elementId,string expectedParentId,double x,double y,double? expectedX=null,double? expectedY=null)
    {
        if(!Enabled||_loading||string.IsNullOrWhiteSpace(elementId)||string.IsNullOrWhiteSpace(expectedParentId)
            ||!double.IsFinite(x)||!double.IsFinite(y)||x<0||x>100||y<0||y>100
            ||expectedX is {} priorX&&!double.IsFinite(priorX)||expectedY is {} priorY&&!double.IsFinite(priorY))return false;
        var matches=new List<(HyperlibWizardHeaderElementViewModel Element,string ParentId,string Arrangement)>();
        void Find(IEnumerable<HyperlibWizardHeaderElementViewModel> items,string parent,string arrangement)
        {
            foreach(var item in items)
            {if(item.Id==elementId)matches.Add((item,parent,arrangement));Find(item.Children,item.Id,item.Arrangement);}
        }
        foreach(var block in Blocks)Find(block.Elements,block.Id,block.Arrangement);
        if(matches.Count!=1)return false;var match=matches[0];var element=match.Element;
        if(match.ParentId!=expectedParentId||match.Arrangement!="positioned"
            ||expectedX is {} previousX&&Math.Abs(previousX-element.X)>1e-9
            ||expectedY is {} previousY&&Math.Abs(previousY-element.Y)>1e-9)return false;
        if(element.X==x&&element.Y==y&&element.PositionAlignment=="manual")return true;
        Batch(()=>{element.X=x;element.Y=y;element.PositionAlignment="manual";SelectedElement=element;});return true;
    }
    /// <summary>Commits the finished resize as one undoable change while preserving the chosen width unit.</summary>
    public bool TryResizeElement(string elementId,string expectedParentId,string expectedArrangement,string handle,
        double x,double y,double width,double height,double parentWidth,double parentHeight,HyperlibHeaderElementDefinition expected)
    {
        if(!Enabled||_loading||expected is null||string.IsNullOrWhiteSpace(elementId)||string.IsNullOrWhiteSpace(expectedParentId)
            ||handle is not("n" or "ne" or "e" or "se" or "s" or "sw" or "w" or "nw")
            ||!double.IsFinite(x)||!double.IsFinite(y)||x<0||x>100||y<0||y>100
            ||!double.IsFinite(width)||!double.IsFinite(height)||!double.IsFinite(parentWidth)||!double.IsFinite(parentHeight)
            ||parentWidth<=0||parentHeight<=0||parentWidth>16000||parentHeight>16000
            ||width<0||height<0||width>16000||height>4000
            ||width>parentWidth+.5)return false;
        var matches=new List<(HyperlibWizardHeaderElementViewModel Element,string ParentId,string Arrangement)>();
        void Find(IEnumerable<HyperlibWizardHeaderElementViewModel> items,string parent,string arrangement)
        {
            foreach(var item in items){if(item.Id==elementId)matches.Add((item,parent,arrangement));Find(item.Children,item.Id,item.Arrangement);}
        }
        foreach(var block in Blocks)Find(block.Elements,block.Id,block.Arrangement);
        if(matches.Count!=1)return false;var match=matches[0];var element=match.Element;
        bool Same(double first,double second)=>Math.Abs(first-second)<1e-9;
        if(match.ParentId!=expectedParentId||match.Arrangement!=expectedArrangement||element.Id!=expected.Id
            ||element.WidthMode!=expected.WidthMode||element.PositionAlignment!=expected.PositionAlignment
            ||!Same(element.X,expected.X)||!Same(element.Y,expected.Y)||!Same(element.WidthPercent,expected.WidthPercent)
            ||!Same(element.WidthPixels,expected.WidthPixels)||!Same(element.Height,expected.Height))return false;
        var positioned=match.Arrangement=="positioned";
        if(positioned&&(height>parentHeight+.5||x/100*parentWidth+width>parentWidth+.5||y/100*parentHeight+height>parentHeight+.5))return false;
        var horizontal=handle.Contains('e')||handle.Contains('w');var vertical=handle.Contains('n')||handle.Contains('s');
        var physicalMinimum=Math.Max(16,2*(element.Padding+(element.BorderStyle=="none"?0:element.BorderWidth)));
        var minimumWidth=Math.Max(physicalMinimum,element.IsPixelWidth?0:parentWidth*.01);
        if(horizontal&&(width<minimumWidth-.01||element.IsPixelWidth&&width>4000)
            ||vertical&&height<physicalMinimum-.01)return false;
        var targetWidthPixels=horizontal&&element.IsPixelWidth?width:element.WidthPixels;
        var targetWidthPercent=horizontal&&!element.IsPixelWidth?Math.Clamp(width/parentWidth*100,1,100):element.WidthPercent;
        var targetHeight=vertical?height:element.Height;var targetX=positioned?x:element.X;var targetY=positioned?y:element.Y;
        var targetAlignment=positioned?"manual":element.PositionAlignment;
        if(Same(targetWidthPixels,element.WidthPixels)&&Same(targetWidthPercent,element.WidthPercent)&&Same(targetHeight,element.Height)
            &&Same(targetX,element.X)&&Same(targetY,element.Y)&&targetAlignment==element.PositionAlignment)return true;
        Batch(()=>{element.X=targetX;element.Y=targetY;element.PositionAlignment=targetAlignment;
            element.WidthPixels=targetWidthPixels;element.WidthPercent=targetWidthPercent;element.Height=targetHeight;SelectedElement=element;});return true;
    }
    public void Arrange(HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel? box=null)=>Batch(()=>{if(box is null)block.Arrangement="horizontal";else box.Arrangement="horizontal";});
    internal void SetArrangement(Action set)=>Batch(set);
    public string ParentArrangement(HyperlibWizardHeaderElementViewModel element)
    {
        string? Find(IEnumerable<HyperlibWizardHeaderElementViewModel> items)
        {foreach(var item in items){if(item.Children.Contains(element))return item.Arrangement;var nested=Find(item.Children);if(nested is not null)return nested;}return null;}
        foreach(var block in Blocks){if(block.Elements.Contains(element))return block.Arrangement;var nested=Find(block.Elements);if(nested is not null)return nested;}return "positioned";
    }
    public HyperlibWizardHeaderBlockViewModel? FindBlock(HyperlibWizardHeaderElementViewModel element)=>Blocks.FirstOrDefault(block=>block.Elements.Any(item=>Contains(item,element)));
    public int ElementCount=>Blocks.Sum(block=>block.Elements.Sum(Count));
    public string ValidationMessage
    {
        get
        {
            if(!Enabled)return "";
            var fieldError=Blocks.SelectMany(block=>Flatten(block.Elements)).SelectMany(element=>element.TemplateValueGroups.SelectMany(group=>group.Fields).Where(slotField=>!element.IsContentSlot||!HyperlibHeaderLayouts.IsSlotContentField(slotField.Definition)).Concat(element.TemplateAppearanceFields)).FirstOrDefault(templateField=>templateField.Error.Length>0)?.Error;
            if(fieldError is not null)return fieldError;
            try{var definition=BuildDefinition();HyperlibHeaderLayouts.Validate(definition,true);
                if(IsNavigation){var json=JsonSerializer.Serialize(definition,ContentJson.Options);if(json.Length>200_000||ContentJson.Value(json).GetRawText().Length>400_000)return "This Navigation layout exceeds the existing content field limit. Use fewer elements in this shared bar.";}
                return "";}
            catch(InvalidDataException error){return error.Message;}
        }
    }
    public void ApplyPreset(string id)
    {
        Remember();var snapshot=_undoSnapshot;var value=IsNavigation?HyperlibNavigationLayouts.Create(id):IsBody?HyperlibBodyLayouts.Create(id):IsFooter?HyperlibFooterLayouts.Create(id):HyperlibHeaderLayouts.Create(id);
        foreach(var row in value.Blocks)
        {
            var fill=row.FillColor.Length>0?row.FillColor:BackgroundColorProvider?.Invoke()??"#101820";
            void Adapt(IEnumerable<HyperlibHeaderElementDefinition> items,string inherited)
            {
                foreach(var item in items)
                {
                    var effective=item.FillColor.Length>0?item.FillColor:inherited;
                    if(item.Kind=="text"&&(item.TextColor.Equals("#f2f5f4",StringComparison.OrdinalIgnoreCase)||item.TextColor.Equals("#ffffff",StringComparison.OrdinalIgnoreCase)))item.TextColor=Readable(effective);
                    if(item.Kind=="navigation"&&(item.Navigation??=new()).TextColor.Equals("#dce9e2",StringComparison.OrdinalIgnoreCase))item.Navigation.TextColor=Readable(effective);
                    Adapt(item.Children,effective);
                }
            }
            Adapt(row.Elements,fill);
        }
        LoadDefinition(value);_undoSnapshot=snapshot;Changed(true);
    }
    public void Undo(){if(_undoSnapshot is not {} snapshot)return;LoadDefinition(snapshot);_undoSnapshot=null;_pending=true;_structurePending=true;Emit();OnPropertyChanged(nameof(CanUndo));}
    public HyperlibHeaderLayoutDefinition BuildDefinition()=>new(){Enabled=Enabled,Blocks=Blocks.Select(block=>block.BuildDefinition()).ToList()};
    public void LoadDefinition(HyperlibHeaderLayoutDefinition definition)
    {
        _loading=true;try{_enabled=definition.Enabled;Blocks.Clear();foreach(var block in definition.Blocks)Blocks.Add(new(this,block));SelectedElement=null;}
        finally{_loading=false;_pending=false;_structurePending=false;}
        _undoSnapshot=null;OnPropertyChanged(nameof(CanUndo));OnPropertyChanged(nameof(Enabled));OnPropertyChanged(nameof(HasBlocks));OnPropertyChanged(nameof(ElementCount));OnPropertyChanged(nameof(ValidationMessage));StructureChanged?.Invoke();
    }
    internal void Changed(bool structure=false)
    {
        if(_loading)return;_pending=true;_structurePending|=structure;
        if(!_enabled){_enabled=true;OnPropertyChanged(nameof(Enabled));}
        if(_batch==0)Emit();
    }
    private void Batch(Action action){if(_batch==0)Remember();_batch++;try{action();}finally{_batch--;if(_batch==0)Emit();}}
    private void Remember(){if(Enabled||IsBody||IsFooter)_undoSnapshot=BuildDefinition();OnPropertyChanged(nameof(CanUndo));}
    private void Emit()
    {
        if(!_pending)return;var structure=_structurePending;_pending=false;_structurePending=false;
        OnPropertyChanged(nameof(HasBlocks));OnPropertyChanged(nameof(ElementCount));OnPropertyChanged(nameof(ValidationMessage));OnPropertyChanged(nameof(CanUndo));if(structure)StructureChanged?.Invoke();DefinitionChanged?.Invoke();
    }
    private static void ArrangeCore(ObservableCollection<HyperlibWizardHeaderElementViewModel> elements)
    {
        if(elements.Count==0)return;var width=100d/elements.Count;
        for(var index=0;index<elements.Count;index++){elements[index].X=index*width;elements[index].Y=0;elements[index].WidthPercent=width;}
    }
    private static bool EvenlyArranged(ObservableCollection<HyperlibWizardHeaderElementViewModel> items)
    {
        if(items.Count==0)return true;var width=100d/items.Count;
        return items.Select((item,index)=>Math.Abs(item.X-index*width)<.001&&Math.Abs(item.Y)<.001&&Math.Abs(item.WidthPercent-width)<.001).All(value=>value);
    }
    private static void PlaceInFreeSpace(IEnumerable<HyperlibWizardHeaderElementViewModel> items,HyperlibWizardHeaderElementViewModel added)
    {
        var end=0d;var bestStart=0d;var bestWidth=0d;
        foreach(var item in items.OrderBy(item=>item.X)){if(item.X-end>bestWidth){bestStart=end;bestWidth=item.X-end;}end=Math.Max(end,Math.Min(100,item.X+item.WidthPercent));}
        if(100-end>bestWidth){bestStart=end;bestWidth=100-end;}
        added.WidthPercent=bestWidth>=15?Math.Min(30,bestWidth):25;added.X=bestWidth>=15?bestStart:5;added.Y=bestWidth>=15?0:75;
    }
    private ObservableCollection<HyperlibWizardHeaderElementViewModel>? FindSiblings(HyperlibWizardHeaderElementViewModel element)
    {
        ObservableCollection<HyperlibWizardHeaderElementViewModel>? Search(ObservableCollection<HyperlibWizardHeaderElementViewModel> items)
        {if(items.Contains(element))return items;foreach(var item in items){var found=Search(item.Children);if(found is not null)return found;}return null;}
        foreach(var block in Blocks){var found=Search(block.Elements);if(found is not null)return found;}return null;
    }
    private static bool Contains(HyperlibWizardHeaderElementViewModel element,HyperlibWizardHeaderElementViewModel? candidate)=>ReferenceEquals(element,candidate)||element.Children.Any(child=>Contains(child,candidate));
    private static IEnumerable<HyperlibWizardHeaderElementViewModel> Flatten(IEnumerable<HyperlibWizardHeaderElementViewModel> items){foreach(var item in items){yield return item;foreach(var child in Flatten(item.Children))yield return child;}}
    private static int Count(HyperlibWizardHeaderElementViewModel element)=>1+element.Children.Sum(Count);
    private string ReadableText(HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel? box)
    {
        var fill=BackgroundColorProvider?.Invoke()??"#101820";if(block.FillColor.Length>0)fill=block.FillColor;
        bool Find(IEnumerable<HyperlibWizardHeaderElementViewModel> items,List<HyperlibWizardHeaderElementViewModel> path)
        {
            foreach(var item in items){path.Add(item);if(ReferenceEquals(item,box)||Find(item.Children,path))return true;path.RemoveAt(path.Count-1);}return false;
        }
        if(box is not null){var path=new List<HyperlibWizardHeaderElementViewModel>();if(Find(block.Elements,path))foreach(var parent in path)if(parent.FillColor.Length>0)fill=parent.FillColor;}
        return Readable(fill);
    }
    private static string Readable(string fill)
    {
        if(!Regex.IsMatch(fill,"^#[0-9A-Fa-f]{6}$"))return "#f2f5f4";
        var red=Convert.ToInt32(fill.Substring(1,2),16);var green=Convert.ToInt32(fill.Substring(3,2),16);var blue=Convert.ToInt32(fill.Substring(5,2),16);
        return red*.299+green*.587+blue*.114>160?"#17212b":"#f2f5f4";
    }
    private static string NewId(string prefix)=>prefix+"-"+Guid.NewGuid().ToString("N");
}

public sealed class HyperlibWizardHeaderBlockViewModel : ObservableObject
{
    private readonly HyperlibWizardHeaderLayoutViewModel _owner;private readonly HyperlibHeaderBlockDefinition _value;
    public HyperlibWizardHeaderBlockViewModel(HyperlibWizardHeaderLayoutViewModel owner,HyperlibHeaderBlockDefinition definition)
    {
        _owner=owner;_value=ContentJson.Clone(definition);foreach(var element in definition.Elements)Elements.Add(new(owner,element));Elements.CollectionChanged+=(_,_)=>_owner.Changed(true);
    }
    public string Id=>_value.Id;
    public ObservableCollection<HyperlibWizardHeaderElementViewModel> Elements{get;}=[];
    public string Arrangement{get=>_value.Arrangement;set{if(_value.Arrangement==value)return;_owner.SetArrangement(()=>{Set(_value.Arrangement,value,item=>_value.Arrangement=item);OnPropertyChanged(nameof(ArrangeSideBySide));OnPropertyChanged(nameof(IsPositioned));});}}
    public bool ArrangeSideBySide{get=>Arrangement=="horizontal";set=>Arrangement=value?"horizontal":"positioned";}
    public bool IsPositioned=>Arrangement=="positioned";
    public double Height{get=>_value.Height;set=>Set(_value.Height,value,item=>_value.Height=item);}
    public string FillColor{get=>_value.FillColor;set=>Set(_value.FillColor,value??"",item=>_value.FillColor=item);}
    public double Padding{get=>_value.Padding;set=>Set(_value.Padding,value,item=>_value.Padding=item);}
    public HyperlibHeaderBlockDefinition BuildDefinition(){var value=ContentJson.Clone(_value);value.Elements=Elements.Select(item=>item.BuildDefinition()).ToList();return value;}
    private void Set<T>(T current,T value,Action<T> assign,[CallerMemberName]string? name=null){if(EqualityComparer<T>.Default.Equals(current,value))return;assign(value);OnPropertyChanged(name);_owner.Changed();}
}

public sealed class HyperlibWizardHeaderElementViewModel : ObservableObject
{
    private readonly HyperlibWizardHeaderLayoutViewModel _owner;private readonly HyperlibHeaderElementDefinition _value;
    public HyperlibWizardHeaderElementViewModel(HyperlibWizardHeaderLayoutViewModel owner,HyperlibHeaderElementDefinition definition,TemplateLibraryObject? source=null)
    {
        _owner=owner;_value=ContentJson.Clone(definition);Navigation=new(_value.Navigation??new(),()=>_owner.Changed());foreach(var child in definition.Children)Children.Add(new(owner,child));Children.CollectionChanged+=(_,_)=>_owner.Changed(true);
        if(source is null&&definition.TemplateObjectId.Length>0){try{source=owner.TemplateObjectProvider?.Invoke(definition.TemplateObjectId);}catch{ }}
        if(Kind=="template")
        {
            if(source is not null)
            {
                foreach(var (key,schema,block) in HyperlibHeaderTemplateLibrary.SourceBlocks(source))
                {
                    _value.TemplateValues.TryGetValue(key,out var values);var target=values??new(StringComparer.Ordinal);
                    var fields=schema.Fields.Select(field=>
                    {
                        var inherited=block?.Values.TryGetValue(field.Id,out var configured)==true?configured:HyperlibTemplateEditSession.InitialValue(field);
                        return new HyperlibHeaderTemplateValueViewModel(field,target.TryGetValue(field.Id,out var stored)?stored:inherited,
                            value=>{target[field.Id]=value.Clone();_value.TemplateValues[key]=target;},()=>_owner.Changed(),inherited,
                            ()=>{target.Remove(field.Id);if(target.Count==0)_value.TemplateValues.Remove(key);});
                    }).ToArray();TemplateValueGroups.Add(new(schema.Title,fields));
                }
            }
            else TemplateSourceMessage="This object's fields are unavailable. Its saved values are preserved; reconnect the original collection to edit them.";
        }
        var appearanceFields=source is null?_value.TemplateAppearance.Select(pair=>new FieldDefinition{Id=pair.Key,Title=pair.Key,Type=pair.Value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False?"boolean":pair.Value.ValueKind==System.Text.Json.JsonValueKind.Number?"number":"string",DefaultValue=pair.Value.Clone()}):Kind=="template"?source.Definition.Appearance:source.Appearance;
        foreach(var field in appearanceFields.Where(HyperlibHeaderTemplateLibrary.SupportsAppearance))
            TemplateAppearanceFields.Add(new(field,_value.TemplateAppearance.TryGetValue(field.Id,out var stored)?stored:HyperlibTemplateEditSession.InitialValue(field),
                value=>SetAppearanceValue(field.Id,value),()=>_owner.Changed(),reset:()=>{_value.TemplateAppearance.Remove(field.Id);if(Kind=="box")ApplyNativeAppearance(field.Id,HyperlibTemplateEditSession.InitialValue(field));}));
    }
    public string Id=>_value.Id;public string Kind=>_value.Kind;
    public HyperlibWizardNavigationViewModel Navigation { get; }
    public string Label=>IsContentSlot?(SlotName.Length>0?SlotName:Kind=="template"?TemplateTitle:Kind=="text"?"Text area":Kind=="image"?"Image slot":"Video slot"):Kind switch{"text"=>Text.Length>0?(Text.Length>28?Text[..28]+"…":Text):"Text","box"=>TemplateTitle.Length>0?"Box · "+TemplateTitle:"Box","image"=>"Image","template"=>"Template · "+TemplateTitle,"navigation"=>"Navigation links",_=>"Video"};
    public bool IsContentSlot{get=>_value.IsContentSlot;set=>Set(_value.IsContentSlot,value,item=>_value.IsContentSlot=item);}
    public string SlotName{get=>_value.SlotName;set=>Set(_value.SlotName,value??"",item=>_value.SlotName=item);}
    public string SlotHint{get=>_value.SlotHint;set=>Set(_value.SlotHint,value??"",item=>_value.SlotHint=item);}
    public string TemplateObjectId=>_value.TemplateObjectId;
    public string TemplateTitle=>_value.TemplateTitle;
    public string TemplateSourceMessage{get;}="";
    public List<HyperlibHeaderTemplateValueGroup> TemplateValueGroups{get;}=[];
    public List<HyperlibHeaderTemplateValueViewModel> TemplateAppearanceFields{get;}=[];
    public ObservableCollection<HyperlibWizardHeaderElementViewModel> Children{get;}=[];
    public string Arrangement{get=>_value.Arrangement;set{if(_value.Arrangement==value)return;_owner.SetArrangement(()=>{Set(_value.Arrangement,value,item=>_value.Arrangement=item);OnPropertyChanged(nameof(ArrangeSideBySide));OnPropertyChanged(nameof(IsPositioned));});}}
    public bool ArrangeSideBySide{get=>Arrangement=="horizontal";set=>Arrangement=value?"horizontal":"positioned";}
    public bool IsPositioned=>Arrangement=="positioned";
    public string WidthMode{get=>_value.WidthMode;set=>Set(_value.WidthMode,value??"percent",item=>_value.WidthMode=item);}
    public double WidthPixels{get=>_value.WidthPixels;set=>Set(_value.WidthPixels,value,item=>_value.WidthPixels=item);}
    public double Width{get=>WidthMode=="pixels"?WidthPixels:WidthPercent;set{if(WidthMode=="pixels")WidthPixels=value;else WidthPercent=value;}}
    public bool IsPixelWidth=>WidthMode=="pixels";
    public string SizeDescription=>Width.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+(IsPixelWidth?" px":"%")+" × "+(Height>0?Height.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" px":"auto");
    public string PositionAlignment{get=>_value.PositionAlignment;set=>Set(_value.PositionAlignment,value??"manual",item=>_value.PositionAlignment=item);}
    public double WidthPercent{get=>_value.WidthPercent;set=>Set(_value.WidthPercent,value,item=>_value.WidthPercent=item);}
    public double Height{get=>_value.Height;set=>Set(_value.Height,value,item=>_value.Height=item);}
    public double X{get=>_value.X;set=>Set(_value.X,value,item=>_value.X=item);}
    public double Y{get=>_value.Y;set=>Set(_value.Y,value,item=>_value.Y=item);}
    public string Alignment{get=>_value.Alignment;set=>Set(_value.Alignment,value??"left",item=>_value.Alignment=item);}
    public string FontFamily{get=>_value.FontFamily;set=>Set(_value.FontFamily,value??"system-ui",item=>_value.FontFamily=item);}
    public double FontSize{get=>_value.FontSize;set=>Set(_value.FontSize,value,item=>_value.FontSize=item);}
    public int FontWeight{get=>_value.FontWeight;set=>Set(_value.FontWeight,value,item=>_value.FontWeight=item);}
    public string Text{get=>_value.Text;set=>Set(_value.Text,value??"",item=>_value.Text=item);}
    public string TextColor{get=>_value.TextColor;set=>Set(_value.TextColor,value??"",item=>_value.TextColor=item);}
    public string FillColor{get=>_value.FillColor;set=>Set(_value.FillColor,value??"",item=>_value.FillColor=item);}
    public string BorderColor{get=>_value.BorderColor;set=>Set(_value.BorderColor,value??"",item=>_value.BorderColor=item);}
    public double BorderWidth{get=>_value.BorderWidth;set=>Set(_value.BorderWidth,value,item=>_value.BorderWidth=item);}
    public string BorderStyle{get=>_value.BorderStyle;set=>Set(_value.BorderStyle,value??"solid",item=>_value.BorderStyle=item);}
    public double BorderRadius{get=>_value.BorderRadius;set=>Set(_value.BorderRadius,value,item=>_value.BorderRadius=item);}
    public double Padding{get=>_value.Padding;set=>Set(_value.Padding,value,item=>_value.Padding=item);}
    public string MediaPath{get=>_value.MediaPath;set=>Set(_value.MediaPath,value??"",item=>_value.MediaPath=item);}
    public string Alt{get=>_value.Alt;set=>Set(_value.Alt,value??"",item=>_value.Alt=item);}
    public string MediaFit{get=>_value.MediaFit;set=>Set(_value.MediaFit,value??"contain",item=>_value.MediaFit=item);}
    public void SetPixelSize(double width,double height)=>_owner.SetArrangement(()=>{WidthMode="pixels";WidthPixels=width;Height=height;});
    public HyperlibHeaderElementDefinition BuildDefinition(){var value=ContentJson.Clone(_value);if(Kind=="navigation")value.Navigation=Navigation.BuildDefinition();value.Children=Children.Select(item=>item.BuildDefinition()).ToList();return value;}
    private void SetAppearanceValue(string key,System.Text.Json.JsonElement value){_value.TemplateAppearance[key]=value.Clone();if(Kind=="box")ApplyNativeAppearance(key,value);}
    private void ApplyNativeAppearance(string key,System.Text.Json.JsonElement value)
    {
        if(value.ValueKind==System.Text.Json.JsonValueKind.String)
        {
            var text=value.GetString()??"";
            if(key is "background" or "paper" or "surface"){if(Regex.IsMatch(text,"^#[0-9a-fA-F]{3}$"))text="#"+string.Concat(text.Skip(1).Select(character=>new string(character,2)));else if(Regex.IsMatch(text,"^#[0-9a-fA-F]{8}$"))text=text[..7];_value.FillColor=text;OnPropertyChanged(nameof(FillColor));}
            if(key is "ink" or "text"){if(text.Length==9)text=text[..7];if(text.Length==4)text="#"+string.Concat(text.Skip(1).Select(character=>new string(character,2)));_value.TextColor=text;OnPropertyChanged(nameof(TextColor));}
            if(key is "font" or "fontFamily"){_value.FontFamily=text switch{"system"=>"system-ui","serif"=>"Georgia","humanist"=>"Segoe UI","geometric"=>"Arial",_=>text};OnPropertyChanged(nameof(FontFamily));}
        }
        if(key=="fontSize"&&value.ValueKind==System.Text.Json.JsonValueKind.Number&&value.TryGetDouble(out var number)){_value.FontSize=number;OnPropertyChanged(nameof(FontSize));}
    }
    private void Set<T>(T current,T value,Action<T> assign,[CallerMemberName]string? name=null)
    {
        if(EqualityComparer<T>.Default.Equals(current,value))return;assign(value);
        var keys=name switch{nameof(FillColor)=>new[]{"background","paper","surface"},nameof(TextColor)=>new[]{"ink","text"},nameof(FontFamily)=>new[]{"font","fontFamily"},nameof(FontSize)=>new[]{"fontSize"},_=>Array.Empty<string>()};
        if(Kind=="box")foreach(var key in keys.Where(key=>_value.TemplateAppearance.ContainsKey(key)).ToArray())
        {if(value is string text&&text.Length==0)_value.TemplateAppearance.Remove(key);else _value.TemplateAppearance[key]=ContentJson.Value(value);TemplateAppearanceFields.FirstOrDefault(templateField=>templateField.Definition.Id==key)?.UpdateFromOwner(ContentJson.Value(value));}
        OnPropertyChanged(name);OnPropertyChanged(nameof(Label));
        if(name is nameof(WidthMode) or nameof(WidthPixels) or nameof(WidthPercent) or nameof(Height)){OnPropertyChanged(nameof(Width));OnPropertyChanged(nameof(IsPixelWidth));OnPropertyChanged(nameof(SizeDescription));}
        _owner.Changed();
    }
}
