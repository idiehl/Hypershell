using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Hypershell.Services;
using Hypershell.ViewModels;

namespace Hypershell;

/// <summary>Browse complete designs beside the same composed Page preview used to create the project.</summary>
public sealed class StudioProjectTemplateGalleryView : ContentControl
{
    private readonly Grid _main=new();
    private readonly Border _previewHost=new(){BorderThickness=new(1),CornerRadius=new(7),ClipToBounds=true};
    private readonly TextBlock _empty=new(){Text="Choose a template to preview.",TextWrapping=TextWrapping.Wrap,Margin=new(18),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
    private StudioProjectTemplateGalleryViewModel? _model;
    private HyperlibGuidedPagePreview? _preview;
    internal bool PreviewEnabled{get;set;}=true;
    internal void DisposePreview()=>StopPreview();
    public StudioProjectTemplateGalleryView()
    {
        NameScope.SetNameScope(this,new NameScope());
        var outer=new DockPanel();Content=outer;
        var heading=new TextBlock{Text="Template library",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,15)};DockPanel.SetDock(heading,Dock.Top);outer.Children.Add(heading);
        _main.ColumnDefinitions.Add(new(){Width=new(285)});_main.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});outer.Children.Add(_main);
        var browse=new DockPanel{Margin=new(0,0,16,0)};_main.Children.Add(browse);var filters=new StackPanel();DockPanel.SetDock(filters,Dock.Top);browse.Children.Add(filters);
        var search=new TextBox{MinHeight=35,Padding=new(8,6,8,6),ToolTip="Find complete templates"};search.SetBinding(TextBox.TextProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.Search)){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AutomationProperties.SetName(search,"Find complete templates");RegisterName("CompleteTemplateSearch",search);filters.Children.Add(search);
        Select(filters,"Collection",nameof(StudioProjectTemplateGalleryViewModel.Collections),nameof(StudioProjectTemplateGalleryViewModel.Collection),"CompleteTemplateCollection");
        Select(filters,"Category",nameof(StudioProjectTemplateGalleryViewModel.Categories),nameof(StudioProjectTemplateGalleryViewModel.Category),"CompleteTemplateCategory");
        var summary=Note();summary.Margin=new(0,8,0,9);summary.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.Summary)));filters.Children.Add(summary);
        var noMatches=Note();noMatches.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.EmptyMessage)));var emptyStyle=new Style(typeof(TextBlock));emptyStyle.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));var noItems=new DataTrigger{Binding=new Binding(nameof(StudioProjectTemplateGalleryViewModel.HasMatches)),Value=false};noItems.Setters.Add(new Setter(VisibilityProperty,Visibility.Visible));emptyStyle.Triggers.Add(noItems);noMatches.Style=emptyStyle;DockPanel.SetDock(noMatches,Dock.Bottom);browse.Children.Add(noMatches);
        var list=new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new(0),Background=Brushes.Transparent};list.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.FilteredTemplates)));list.SetBinding(Selector.SelectedItemProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.Selected)){Mode=BindingMode.TwoWay});AutomationProperties.SetName(list,"Complete project templates");RegisterName("CompleteTemplateList",list);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(list,true);
        var rowStyle=new Style(typeof(ListBoxItem),TryFindResource(typeof(ListBoxItem))as Style);rowStyle.Setters.Add(new Setter(MarginProperty,new Thickness(0,0,0,7)));rowStyle.Setters.Add(new EventSetter(FrameworkElement.RequestBringIntoViewEvent,new RequestBringIntoViewEventHandler((_,args)=>args.Handled=true)));list.ItemContainerStyle=rowStyle;
        var item=new FrameworkElementFactory(typeof(StackPanel));item.SetValue(MarginProperty,new Thickness(7,9,7,9));
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioCompleteTemplateChoice.Title)));title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);item.AppendChild(title);
        var collection=new FrameworkElementFactory(typeof(TextBlock));collection.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioCompleteTemplateChoice.Collection)));collection.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);collection.SetValue(TextBlock.FontSizeProperty,11d);collection.SetValue(OpacityProperty,.7);collection.SetValue(MarginProperty,new Thickness(0,5,0,0));item.AppendChild(collection);
        var category=new FrameworkElementFactory(typeof(TextBlock));category.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioCompleteTemplateChoice.Category)));category.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);category.SetValue(TextBlock.FontSizeProperty,10d);category.SetValue(OpacityProperty,.6);category.SetValue(MarginProperty,new Thickness(0,3,0,0));item.AppendChild(category);list.ItemTemplate=new DataTemplate{VisualTree=item};browse.Children.Add(list);
        var right=new DockPanel();Grid.SetColumn(right,1);_main.Children.Add(right);var details=new StackPanel{Margin=new(0,0,0,12)};DockPanel.SetDock(details,Dock.Top);right.Children.Add(details);RegisterName("CompleteTemplateDetails",details);
        var name=new TextBlock{FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};name.SetBinding(TextBlock.TextProperty,new Binding("Selected.Title"));details.Children.Add(name);
        var description=Note();description.SetBinding(TextBlock.TextProperty,new Binding("Selected.Description"));details.Children.Add(description);
        var availability=Note();availability.Foreground=Theme("AttentionBrush",Brushes.Orange);availability.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.AvailabilityMessage)));details.Children.Add(availability);
        var status=Note();status.Foreground=Theme("AttentionBrush",Brushes.Orange);status.SetBinding(TextBlock.TextProperty,new Binding(nameof(StudioProjectTemplateGalleryViewModel.Message)));DockPanel.SetDock(status,Dock.Bottom);right.Children.Add(status);
        _previewHost.BorderBrush=Theme("BorderBrush",Brushes.Gray);_previewHost.Child=_empty;RegisterName("CompleteTemplatePreview",_previewHost);right.Children.Add(_previewHost);
        DataContextChanged+=(_,_)=>Connect(DataContext as StudioProjectTemplateGalleryViewModel);Loaded+=(_,_)=>Connect(DataContext as StudioProjectTemplateGalleryViewModel);Unloaded+=(_,_)=>Connect(null);
        IsVisibleChanged+=(_,_)=>{if(IsVisible)UpdatePreview();else StopPreview();};SizeChanged+=(_,_)=>_main.ColumnDefinitions[0].Width=new(ActualWidth<850?210:285);
    }
    private void Select(Panel parent,string label,string items,string selected,string name)
    {
        parent.Children.Add(new TextBlock{Text=label,FontSize=11,Opacity=.7,Margin=new(0,9,0,5)});var select=new ComboBox{MinHeight=33};select.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(items));select.SetBinding(Selector.SelectedItemProperty,new Binding(selected){Mode=BindingMode.TwoWay});AutomationProperties.SetName(select,"Template "+label.ToLowerInvariant());RegisterName(name,select);parent.Children.Add(select);
    }
    private void Connect(StudioProjectTemplateGalleryViewModel? model)
    {
        if(ReferenceEquals(_model,model)){UpdatePreview();return;}
        if(_model is not null)_model.PropertyChanged-=Changed;_model=model;
        if(_model is not null)_model.PropertyChanged+=Changed;else StopPreview();UpdatePreview();
    }
    private void Changed(object? sender,PropertyChangedEventArgs args){if(args.PropertyName==nameof(StudioProjectTemplateGalleryViewModel.PreviewDefinition))UpdatePreview();}
    private void UpdatePreview()
    {
        if(!IsLoaded||!IsVisible||!PreviewEnabled)return;
        if(_model?.PreviewDefinition is not {} definition||_model.TemplateLibraryRoot is not {} libraryRoot){StopPreview();return;}
        _preview??=new HyperlibGuidedPagePreview();if(!ReferenceEquals(_previewHost.Child,_preview))_previewHost.Child=_preview;
        _preview.SetDraft(libraryRoot,definition,definition.LandingTemplate,definition.WorkTitle);
    }
    private void StopPreview(){_preview?.Dispose();_preview=null;_previewHost.Child=_empty;}
    private static TextBlock Note()=>new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,7,0,0),Foreground=Theme("MutedForegroundBrush",Brushes.Gray)};
    private static Brush Theme(string name,Brush fallback)=>(Brush?)Application.Current?.TryFindResource(name)??fallback;
}
