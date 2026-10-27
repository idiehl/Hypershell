using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Hypershell.ViewModels;

namespace Hypershell;

/// <summary>Native controls for one navigation-links object; fields stay alive during live edits.</summary>
public sealed class HyperlibNavigationSettingsEditor : StackPanel
{
    private readonly HyperlibWizardNavigationViewModel _model;
    private readonly StackPanel _links=new();
    public HyperlibNavigationSettingsEditor(HyperlibWizardNavigationViewModel model)
    {
        _model=model;
        Loaded+=(_,_)=>{model.CustomLinks.CollectionChanged-=LinksChanged;model.CustomLinks.CollectionChanged+=LinksChanged;DrawLinks();};
        Unloaded+=(_,_)=>model.CustomLinks.CollectionChanged-=LinksChanged;
        var source=Section("Links",true);
        Toggle(source,model,nameof(model.AutoPages),"Follow Work Pages");
        Toggle(source,model,nameof(model.IncludeHome),"Include Work home");
        Text(source,model,nameof(model.HomeLabel),"Home label");
        source.Children.Add(_links);
        var add=Button("+ Add link","Add custom navigation link",()=>model.AddLink());source.Children.Add(add);
        DrawLinks();
        var appearance=Section("Link appearance",false);var layout=new WrapPanel();appearance.Children.Add(layout);
        Select(layout,model,nameof(model.Orientation),"Direction",["horizontal","vertical"]);
        Select(layout,model,nameof(model.Alignment),"Alignment",["left","center","right"]);
        Select(layout,model,nameof(model.FontFamily),"Font",new[]{"system-ui","Arial","Georgia","Segoe UI","monospace",model.FontFamily}.Distinct().ToArray());Number(layout,model,nameof(model.FontSize),"Font size (px)");Number(layout,model,nameof(model.Gap),"Spacing (px)");
        Number(layout,model,nameof(model.LinkPaddingX),"Horizontal padding (px)");Number(layout,model,nameof(model.LinkPaddingY),"Vertical padding (px)");Number(layout,model,nameof(model.Radius),"Corner radius (px)");
        Color(appearance,model,nameof(model.TextColor),"Text color");Color(appearance,model,nameof(model.BackgroundColor),"Link background",true);
        Color(appearance,model,nameof(model.HoverColor),"Hover color");Color(appearance,model,nameof(model.ActiveColor),"Active color");
        var behavior=Section("Behavior",false);
        Toggle(behavior,model,nameof(model.ShowPreviousNext),"Previous and next Page links");
        Toggle(behavior,model,nameof(model.CollapseOnSmallScreens),"Collapsible menu on small screens");
        Text(behavior,model,nameof(model.MenuLabel),"Menu label");Number(behavior,model,nameof(model.CollapseBreakpoint),"Collapse below (px)");
    }
    private StackPanel Section(string title,bool expanded)
    {var panel=new StackPanel{Margin=new(0,10,0,0)};Children.Add(new Expander{Header=title,IsExpanded=expanded,Content=panel,Margin=new(0,0,0,12),HorizontalContentAlignment=HorizontalAlignment.Stretch});return panel;}
    private void DrawLinks()
    {
        _links.Children.Clear();foreach(var link in _model.CustomLinks)
        {
            var panel=new StackPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            actions.Children.Add(Button("↑","Move navigation link up",()=>_model.MoveLink(link,-1)));
            actions.Children.Add(Button("↓","Move navigation link down",()=>_model.MoveLink(link,1)));
            actions.Children.Add(Button("×","Remove navigation link",()=>_model.RemoveLink(link)));panel.Children.Add(actions);
            Text(panel,link,nameof(link.Label),"Label","Navigation link label");
            var target=Text(panel,link,nameof(link.Target),"Destination","Navigation link target");target.ToolTip="Use a website address, a #section anchor, or a project Page link.";
            Toggle(panel,link,nameof(link.NewTab),"Open in a new tab");
            _links.Children.Add(new Border{Child=panel,Padding=new(8),BorderThickness=new(1),BorderBrush=Resource("BorderBrush",Brushes.Gray),CornerRadius=new(5),Margin=new(0,0,0,10)});
        }
    }
    private void LinksChanged(object? sender,System.Collections.Specialized.NotifyCollectionChangedEventArgs args)=>DrawLinks();
    private static Binding Bind(object source,string property,bool numeric=false)=>new(property){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=numeric,NotifyOnValidationError=numeric,ConverterCulture=CultureInfo.InvariantCulture,StringFormat=numeric?"{0:0.###}":null};
    private static StackPanel Field(Panel parent,string label)
    {var field=new StackPanel{Margin=new(0,0,12,12),Width=parent is WrapPanel?146:double.NaN};parent.Children.Add(field);field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,0,0,5)});return field;}
    private static TextBox Text(Panel parent,object source,string property,string label,string? automation=null)
    {var field=Field(parent,label);var input=new TextBox{MinHeight=34};input.SetBinding(TextBox.TextProperty,Bind(source,property));AutomationProperties.SetName(input,automation??label);field.Children.Add(input);return input;}
    private static void Number(Panel parent,object source,string property,string label)
    {var field=Field(parent,label);var input=new TextBox{MinHeight=34};input.SetBinding(TextBox.TextProperty,Bind(source,property,true));AutomationProperties.SetName(input,label);field.Children.Add(input);}
    private static void Select(Panel parent,object source,string property,string label,string[] options)
    {var field=Field(parent,label);var combo=new ComboBox{ItemsSource=options,MinHeight=34};combo.SetBinding(Selector.SelectedItemProperty,Bind(source,property));AutomationProperties.SetName(combo,label);field.Children.Add(combo);}
    private static void Toggle(Panel parent,object source,string property,string label)
    {var toggle=new CheckBox{Content=label,Margin=new(0,0,0,10)};toggle.SetBinding(ToggleButton.IsCheckedProperty,Bind(source,property));AutomationProperties.SetName(toggle,label);parent.Children.Add(toggle);}
    private static Button Button(string content,string name,Action action)
    {var button=new Button{Content=content,ToolTip=name,Padding=new(8,5,8,5),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,5,8)};AutomationProperties.SetName(button,name);button.Click+=(_,_)=>action();button.RequestBringIntoView+=(_,args)=>args.Handled=true;return button;}
    private static void Color(Panel parent,object source,string property,string label,bool clear=false)
    {
        var picker=new HyperlibLiveColorPicker{MaxWidth=300,HorizontalAlignment=HorizontalAlignment.Left};picker.SetBinding(HyperlibLiveColorPicker.ColorProperty,new Binding(property){Source=source,Mode=BindingMode.OneWay});
        picker.ColorChanged+=value=>{var field=source.GetType().GetProperty(property)!;if(!Equals(field.GetValue(source),value))field.SetValue(source,value);};
        var panel=new StackPanel{Margin=new(0,8,0,0)};panel.Children.Add(picker);if(clear)panel.Children.Add(Button("Clear","Clear navigation link background",()=>source.GetType().GetProperty(property)!.SetValue(source,"")));
        var expander=new Expander{Header=label,Content=panel,Margin=new(0,0,0,10),HorizontalContentAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetName(expander,label+" color picker");parent.Children.Add(expander);
    }
    private static Brush Resource(string key,Brush fallback)=>Application.Current?.TryFindResource(key)as Brush??fallback;
}
