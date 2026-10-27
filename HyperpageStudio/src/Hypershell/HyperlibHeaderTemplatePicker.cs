using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;

namespace Hypershell;

/// <summary>Single-object library browser for a particular header insertion point.</summary>
public sealed class HyperlibHeaderTemplatePicker : Window
{
    private readonly HyperlibWorkWizardViewModel _wizard;
    private readonly string _blockId;
    private readonly string? _boxId;
    private readonly bool _navigation;
    private readonly bool _body;
    private readonly bool _footer;
    private readonly HyperlibGuidedPagePreview _preview=new();
    private readonly TextBlock _previewStatus=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,0)};
    private readonly Grid _main=new();
    private bool _closed;
    public HyperlibHeaderTemplateLibraryViewModel Model{get;}
    public HyperlibWorkTemplateChoice? SelectedChoice{get;private set;}
    public TemplateLibraryObject? SelectedSource{get;private set;}
    internal bool PreviewEnabled{get;set;}=true;
    internal void DisposePreview()=>_preview.Dispose();
    public HyperlibHeaderTemplatePicker(HyperlibWorkWizardViewModel wizard,HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel? box=null,string region="header")
    {
        _wizard=wizard;_blockId=block.Id;_boxId=box?.Id;_navigation=region=="navigation";_body=region=="body";_footer=region=="footer";Model=new(wizard);DataContext=Model;
        Title="Choose a Template";Width=1140;Height=820;MinWidth=640;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Theme("AppBackgroundBrush",Brushes.Black);Foreground=Theme("ForegroundBrush",Brushes.White);
        var outer=new DockPanel{Margin=new(20)};Content=outer;
        var heading=new StackPanel{Margin=new(0,0,0,14)};heading.Children.Add(new TextBlock{Text="Template library",FontSize=23,FontWeight=FontWeights.SemiBold});heading.Children.Add(Note("Browse all collections, preview an object, then add it to this "+(box is null?"Block.":"Box.")));DockPanel.SetDock(heading,Dock.Top);outer.Children.Add(heading);
        var footer=new DockPanel{Margin=new(0,14,0,0)};var actions=new StackPanel{Orientation=Orientation.Horizontal};
        var cancel=new Button{Content="Cancel",Padding=new(15,8,15,8),Margin=new(0,0,8,0)};cancel.Click+=(_,_)=>Close();actions.Children.Add(cancel);
        var insert=new Button{Padding=new(17,8,17,8)};insert.SetBinding(Button.ContentProperty,new Binding(nameof(Model.ActionLabel)));insert.SetBinding(IsEnabledProperty,new Binding(nameof(Model.CanInsert)));AutomationProperties.SetName(insert,"Insert selected library object");
        insert.Click+=(_,_)=>{if(!Model.CanInsert)return;SelectedChoice=Model.Selected!.Source;SelectedSource=Model.Source;DialogResult=true;};actions.Children.Add(insert);DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        var status=Note("");status.SetBinding(TextBlock.TextProperty,new Binding(nameof(Model.Message)));footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);outer.Children.Add(footer);
        _main.ColumnDefinitions.Add(new(){Width=new(285)});_main.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});outer.Children.Add(_main);
        var browse=new DockPanel{Margin=new(0,0,16,0)};_main.Children.Add(browse);var filters=new StackPanel{Margin=new(0,0,0,10)};DockPanel.SetDock(filters,Dock.Top);browse.Children.Add(filters);
        var search=new TextBox{MinHeight=35,Padding=new(8,6,8,6),ToolTip="Search title, object ID, collection, category, or purpose"};search.SetBinding(TextBox.TextProperty,new Binding(nameof(Model.Search)){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AutomationProperties.SetName(search,"Find Template library objects");filters.Children.Add(search);
        Select(filters,"Collection",nameof(Model.Groups),nameof(Model.Group));Select(filters,"Category",nameof(Model.Categories),nameof(Model.Category));
        var summary=Note("");summary.SetBinding(TextBlock.TextProperty,new Binding(nameof(Model.Summary)));filters.Children.Add(summary);
        var list=new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch};list.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(nameof(Model.Objects)));list.SetBinding(Selector.SelectedItemProperty,new Binding(nameof(Model.Selected)){Mode=BindingMode.TwoWay});AutomationProperties.SetName(list,"Template library objects");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        var rowStyle=new Style(typeof(ListBoxItem),TryFindResource(typeof(ListBoxItem)) as Style);rowStyle.Setters.Add(new EventSetter(FrameworkElement.RequestBringIntoViewEvent,new RequestBringIntoViewEventHandler((_,args)=>args.Handled=true)));list.ItemContainerStyle=rowStyle;
        var item=new FrameworkElementFactory(typeof(StackPanel));item.SetValue(MarginProperty,new Thickness(4,7,4,7));var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding(nameof(HyperlibWizardLibraryObject.Title)));title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);item.AppendChild(title);
        var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding(nameof(HyperlibWizardLibraryObject.Detail)));detail.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);detail.SetValue(TextBlock.FontSizeProperty,11d);detail.SetValue(OpacityProperty,.7);detail.SetValue(MarginProperty,new Thickness(0,4,0,0));item.AppendChild(detail);list.ItemTemplate=new DataTemplate{VisualTree=item};browse.Children.Add(list);
        var right=new DockPanel();Grid.SetColumn(right,1);_main.Children.Add(right);var information=new StackPanel{Margin=new(0,0,0,12)};DockPanel.SetDock(information,Dock.Top);right.Children.Add(information);
        var name=new TextBlock{FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};name.SetBinding(TextBlock.TextProperty,new Binding("Selected.Title"));information.Children.Add(name);
        var description=Note("");description.SetBinding(TextBlock.TextProperty,new Binding("Selected.Description"));information.Children.Add(description);var purpose=Note("");purpose.SetBinding(TextBlock.TextProperty,new Binding(nameof(Model.Purpose)));information.Children.Add(purpose);
        DockPanel.SetDock(_previewStatus,Dock.Bottom);right.Children.Add(_previewStatus);right.Children.Add(new Border{Child=_preview,BorderBrush=Theme("BorderBrush",Brushes.Gray),BorderThickness=new(1),CornerRadius=new(7),ClipToBounds=true});
        Model.PropertyChanged+=Changed;Loaded+=(_,_)=>UpdatePreview();SizeChanged+=(_,_)=>_main.ColumnDefinitions[0].Width=new(ActualWidth<850?235:285);
        Closed+=(_,_)=>{_closed=true;Model.PropertyChanged-=Changed;_preview.Dispose();};
    }
    private void Changed(object? sender,PropertyChangedEventArgs args){if(args.PropertyName==nameof(Model.Source))UpdatePreview();}
    private void UpdatePreview()
    {
        if(_closed||!PreviewEnabled||Model.Source is not {} source||Model.Selected is not {} selected)return;
        try
        {
            var definition=_wizard.PreviewDefinition;var recipe=ContentJson.Clone(_wizard.CurrentTemplate.BuildDefinition());var layout=new HyperlibWizardHeaderLayoutViewModel(_navigation,region:_body?"body":_footer?"footer":"header");layout.TemplateObjectProvider=id=>_wizard.LibraryObjects.FirstOrDefault(item=>item.Id==id) is {} choice?_wizard.GetLibraryObject(choice.Source):null;layout.LoadDefinition(_navigation?recipe.NavigationLayout:_body?recipe.BodyLayout:_footer?recipe.FooterLayout:recipe.HeaderLayout);
            var block=layout.Blocks.FirstOrDefault(item=>item.Id==_blockId)??layout.AddBlock();var box=_boxId is null?null:Find(block.Elements,_boxId);layout.AddLibraryObject(block,selected.Source,source,box);
            if(_navigation){recipe.NavigationLayout=layout.BuildDefinition();recipe.IncludeNavigation=true;if(recipe.NavigationPlacement=="none")recipe.NavigationPlacement="top";}
            else if(_body)recipe.BodyLayout=layout.BuildDefinition();
            else if(_footer){recipe.FooterLayout=layout.BuildDefinition();recipe.FooterPlacement="bottom";}
            else{recipe.HeaderLayout=layout.BuildDefinition();recipe.HeaderPlacement="top";}
            if(source.Kind is "element" or "preset" or "layout"&&!definition.SelectedToolsetIds.Contains(selected.Id))definition.SelectedToolsetIds.Add(selected.Id);
            var root=_wizard.TemplateLibraryRoot??(_wizard.OwnedProjectRoot is {} owned?System.IO.Path.GetFullPath(System.IO.Path.Combine(owned,"../..")):null);
            if(root is not null){_preview.SetDraft(root,definition,recipe,_wizard.CurrentPageTitle,_wizard.OwnedProjectRoot);_previewStatus.Text="Preview in this page template. Insert to keep this choice and edit its properties.";}
            else _previewStatus.Text="The source library is unavailable. Saved object definitions can still be inserted.";
        }
        catch(Exception error){_previewStatus.Text="Preview unavailable: "+error.Message;}
    }
    private static HyperlibWizardHeaderElementViewModel? Find(IEnumerable<HyperlibWizardHeaderElementViewModel> elements,string id){foreach(var element in elements){if(element.Id==id)return element;var child=Find(element.Children,id);if(child is not null)return child;}return null;}
    private static void Select(Panel parent,string label,string items,string selected)
    {
        parent.Children.Add(new TextBlock{Text=label,FontSize=11,Margin=new(0,9,0,5),Opacity=.7});var combo=new ComboBox{MinHeight=34};combo.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(items));combo.SetBinding(Selector.SelectedItemProperty,new Binding(selected){Mode=BindingMode.TwoWay});AutomationProperties.SetName(combo,"Template library "+label.ToLowerInvariant());parent.Children.Add(combo);
    }
    private static TextBlock Note(string text)=>new(){Text=text,FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Theme("MutedForegroundBrush",Brushes.Gray),Margin=new(0,6,0,0)};
    private static Brush Theme(string key,Brush fallback)=>(Brush?)Application.Current?.TryFindResource(key)??fallback;
}
