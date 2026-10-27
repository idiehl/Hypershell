using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using HyperLib.Core;
using Hypershell.Infrastructure;
using Hypershell.Models;

namespace Hypershell.ViewModels;

/// <summary>Independent settings for one reusable navigation-links element.</summary>
public sealed class HyperlibWizardNavigationViewModel : ObservableObject
{
    private readonly HyperlibNavigationDefinition _value;
    private readonly Action _changed;
    public HyperlibWizardNavigationViewModel(HyperlibNavigationDefinition definition, Action changed)
    {
        _value = ContentJson.Clone(definition); _changed = changed;
        foreach (var link in _value.CustomLinks) CustomLinks.Add(new(link, Changed));
        CustomLinks.CollectionChanged += (_, _) => Changed();
    }
    public ObservableCollection<HyperlibWizardNavigationLinkViewModel> CustomLinks { get; } = [];
    public bool AutoPages { get => _value.AutoPages; set => Set(_value.AutoPages,value,item=>_value.AutoPages=item); }
    public bool IncludeHome { get => _value.IncludeHome; set => Set(_value.IncludeHome,value,item=>_value.IncludeHome=item); }
    public string HomeLabel { get => _value.HomeLabel; set => Set(_value.HomeLabel,value??"",item=>_value.HomeLabel=item); }
    public bool ShowPreviousNext { get => _value.ShowPreviousNext; set => Set(_value.ShowPreviousNext,value,item=>_value.ShowPreviousNext=item); }
    public string Orientation { get => _value.Orientation; set => Set(_value.Orientation,value,item=>_value.Orientation=item); }
    public string Alignment { get => _value.Alignment; set => Set(_value.Alignment,value,item=>_value.Alignment=item); }
    public double Gap { get => _value.Gap; set => Set(_value.Gap,value,item=>_value.Gap=item); }
    public double LinkPaddingX { get => _value.LinkPaddingX; set => Set(_value.LinkPaddingX,value,item=>_value.LinkPaddingX=item); }
    public double LinkPaddingY { get => _value.LinkPaddingY; set => Set(_value.LinkPaddingY,value,item=>_value.LinkPaddingY=item); }
    public double Radius { get => _value.Radius; set => Set(_value.Radius,value,item=>_value.Radius=item); }
    public double FontSize { get => _value.FontSize; set => Set(_value.FontSize,value,item=>_value.FontSize=item); }
    public string FontFamily { get => _value.FontFamily; set => Set(_value.FontFamily,value,item=>_value.FontFamily=item); }
    public string TextColor { get => _value.TextColor; set => Set(_value.TextColor,value??"",item=>_value.TextColor=item); }
    public string BackgroundColor { get => _value.BackgroundColor; set => Set(_value.BackgroundColor,value??"",item=>_value.BackgroundColor=item); }
    public string HoverColor { get => _value.HoverColor; set => Set(_value.HoverColor,value??"",item=>_value.HoverColor=item); }
    public string ActiveColor { get => _value.ActiveColor; set => Set(_value.ActiveColor,value??"",item=>_value.ActiveColor=item); }
    public bool CollapseOnSmallScreens { get => _value.CollapseOnSmallScreens; set => Set(_value.CollapseOnSmallScreens,value,item=>_value.CollapseOnSmallScreens=item); }
    public string MenuLabel { get => _value.MenuLabel; set => Set(_value.MenuLabel,value??"",item=>_value.MenuLabel=item); }
    public double CollapseBreakpoint { get => _value.CollapseBreakpoint; set => Set(_value.CollapseBreakpoint,value,item=>_value.CollapseBreakpoint=item); }
    public void AddLink() => CustomLinks.Add(new(new() { Label = "New link", Target = "" }, Changed));
    public void RemoveLink(HyperlibWizardNavigationLinkViewModel link) => CustomLinks.Remove(link);
    public void MoveLink(HyperlibWizardNavigationLinkViewModel link,int direction)
    { var index=CustomLinks.IndexOf(link);var next=index+direction;if(index>=0&&next>=0&&next<CustomLinks.Count)CustomLinks.Move(index,next); }
    public HyperlibNavigationDefinition BuildDefinition()
    {var value=ContentJson.Clone(_value);value.CustomLinks=CustomLinks.Select(link=>link.BuildDefinition()).ToList();return value;}
    private void Changed(){_changed();}
    private void Set<T>(T old,T value,Action<T> assign,[CallerMemberName]string? name=null)
    {if(EqualityComparer<T>.Default.Equals(old,value))return;assign(value);OnPropertyChanged(name);Changed();}
}

public sealed class HyperlibWizardNavigationLinkViewModel : ObservableObject
{
    private readonly HyperlibNavigationLinkDefinition _value;private readonly Action _changed;
    public HyperlibWizardNavigationLinkViewModel(HyperlibNavigationLinkDefinition value,Action changed){_value=ContentJson.Clone(value);_changed=changed;}
    public string Id=>_value.Id;
    public string Label{get=>_value.Label;set{if(_value.Label==value)return;_value.Label=value??"";OnPropertyChanged();_changed();}}
    public string Target{get=>_value.Target;set{if(_value.Target==value)return;_value.Target=value??"";OnPropertyChanged();_changed();}}
    public bool NewTab{get=>_value.NewTab;set{if(_value.NewTab==value)return;_value.NewTab=value;OnPropertyChanged();_changed();}}
    public HyperlibNavigationLinkDefinition BuildDefinition()=>ContentJson.Clone(_value);
}
