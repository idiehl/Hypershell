using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hypershell.ViewModels;
using Microsoft.Win32;

namespace Hypershell;

/// <summary>Shows only the controls useful to the builder's current region.</summary>
public sealed class HyperlibWizardStageSettings : ContentControl
{
    private HyperlibWorkWizardViewModel? _wizard;
    private HyperlibWizardTemplateViewModel? _template;
    private string _stage="";
    private int _invalidFields;
    public HyperlibWizardStageSettings()
    {
        DataContextChanged+=(_,_)=>Connect(); Loaded+=(_,_)=>Connect();
        Unloaded+=(_,_)=>{if(_wizard is not null)_wizard.PropertyChanged-=Changed;_wizard=null;};
        AddHandler(Validation.ErrorEvent,new EventHandler<ValidationErrorEventArgs>((_,args)=>
        {
            _invalidFields=Math.Max(0,_invalidFields+(args.Action==ValidationErrorEventAction.Added?1:-1));
            if(_wizard is not null)_wizard.InputValidationMessage=_invalidFields>0?"Enter a valid number for every style field before continuing.":"";
        }));
    }
    private void Connect()
    {
        if(_wizard is not null)_wizard.PropertyChanged-=Changed;_wizard=DataContext as HyperlibWorkWizardViewModel;
        if(_wizard is not null)_wizard.PropertyChanged+=Changed;_template=null;Build();
    }
    private void Changed(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName==nameof(HyperlibWorkWizardViewModel.CurrentTemplate)||args.PropertyName==nameof(HyperlibWorkWizardViewModel.CurrentStage))Build();
    }
    private void Build()
    {
        if(_wizard is not {} wizard)return;
        if(ReferenceEquals(_template,wizard.CurrentTemplate)&&_stage==wizard.CurrentStage)return;
        _template=wizard.CurrentTemplate;_stage=wizard.CurrentStage;_invalidFields=0;wizard.InputValidationMessage="";
        var page=_template;var panel=new StackPanel();Content=panel;
        Heading(panel,_stage=="background"?"Make it yours":"Style this part of the page");
        if(_stage=="background")
        {
            var colors=new WrapPanel();panel.Children.Add(colors);ColorField(colors,page,nameof(page.Background),"Background color");ColorField(colors,page,nameof(page.Accent),"Accent color");
            Help(panel,"The page builder shows your current design throughout the process. Return to any earlier step to make changes.");return;
        }
        if(_stage=="header")Select(panel,page,nameof(page.HeaderPlacement),"Header position",page.HeaderPlacements,true);
        if(_stage=="footer")Select(panel,page,nameof(page.FooterPlacement),"Footer position",page.FooterPlacements,true);
        if(_stage=="navigation")
        {
            var check=new CheckBox{Content="Connect the landing page and Pages",Margin=new(0,0,0,12)};check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(page.IncludeNavigation)){Source=page,Mode=BindingMode.TwoWay});panel.Children.Add(check);
            Select(panel,page,nameof(page.NavigationPlacement),"Navigation position",page.NavigationPlacements,true);
        }
        if(_stage is "body" or "extras")
        {
            var layout=new WrapPanel();panel.Children.Add(layout);Select(layout,page,nameof(page.ContentWidth),"Content width",page.Widths,true);Select(layout,page,nameof(page.ContentAlignment),"Content alignment",page.Alignments,true);
            Heading(panel,"Your empty slots");
            var slots=new ItemsControl();slots.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(nameof(wizard.CurrentPlacements)){Source=wizard});
            var template=new DataTemplate();var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(MarginProperty,new Thickness(0,0,0,8));
            var remove=new FrameworkElementFactory(typeof(Button));remove.SetValue(ContentProperty,"×");remove.SetValue(WidthProperty,28d);remove.SetValue(Button.PaddingProperty,new Thickness(0));remove.SetValue(DockPanel.DockProperty,Dock.Right);remove.SetValue(ToolTipProperty,"Remove this empty slot");remove.SetBinding(Button.CommandProperty,new Binding(nameof(page.RemovePlacementCommand)){Source=page});remove.SetBinding(Button.CommandParameterProperty,new Binding());row.AppendChild(remove);
            var title=new FrameworkElementFactory(typeof(TextBlock));title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);title.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);title.SetBinding(TextBlock.TextProperty,new Binding("SelectedObject.Title"));row.AppendChild(title);template.VisualTree=row;slots.ItemTemplate=template;panel.Children.Add(slots);
            Help(panel,"Choose another design above to add a slot. Text and media content are filled in later, on individual Pages.");
        }
        var style=wizard.CurrentStyle;var fields=new WrapPanel{Margin=new(0,14,0,0)};panel.Children.Add(fields);
        ColorField(fields,style,nameof(style.BackgroundColor),"Background (empty = inherit)");ColorField(fields,style,nameof(style.TextColor),"Text color");ColorField(fields,style,nameof(style.BorderColor),"Border color");
        Field(fields,style,nameof(style.BorderWidth),"Border width",true);Field(fields,style,nameof(style.BorderRadius),"Corner radius",true);Field(fields,style,nameof(style.Padding),"Padding",true);
        Select(fields,style,nameof(style.FontFamily),"Font",style.Fonts);Field(fields,style,nameof(style.FontSize),"Font size",true);Select(fields,style,nameof(style.Alignment),"Alignment",style.Alignments);
        if(_stage is "header" or "footer")
        {
            Field(panel,style,nameof(style.Text),"Reusable label or title",false,true);
            Help(panel,"This label repeats with the template. Article content is written on individual Pages later.");
        }
        if(_stage=="header")
        {
            Heading(panel,"Reusable logo");var row=new DockPanel{Margin=new(0,0,0,12)};panel.Children.Add(row);
            var choose=new Button{Content="Choose image…",Padding=new(10,7,10,7),Margin=new(8,0,0,0)};DockPanel.SetDock(choose,Dock.Right);row.Children.Add(choose);
            var file=new TextBox{MinHeight=34};file.SetBinding(TextBox.TextProperty,new Binding(nameof(style.LogoPath)){Source=style,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AutomationProperties.SetName(file,"Header logo image");row.Children.Add(file);
            choose.Click+=(_,_)=>{var dialog=new OpenFileDialog{Title="Choose a reusable header logo",Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.avif",CheckFileExists=true};if(dialog.ShowDialog(Window.GetWindow(this))==true)style.LogoPath=dialog.FileName;};
            Select(panel,style,nameof(style.LogoPlacement),"Logo placement",style.LogoPlacements);
        }
    }
    private static void Heading(Panel parent,string text)=>parent.Children.Add(new TextBlock{Text=text,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
    private static void Help(Panel parent,string text)=>parent.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,LineHeight=18,Foreground=(Brush?)Application.Current?.TryFindResource("MutedForegroundBrush"),Margin=new(0,0,0,12)});
    private static StackPanel Container(Panel parent,string label,bool full=false)
    {
        var field=new StackPanel{Margin=new(0,0,12,12),Width=full||parent is not WrapPanel?double.NaN:152};
        field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=(Brush?)Application.Current?.TryFindResource("MutedForegroundBrush"),Margin=new(0,0,0,5),TextWrapping=TextWrapping.Wrap});parent.Children.Add(field);return field;
    }
    private static void Field(Panel parent,object source,string path,string label,bool numeric=false,bool full=false)
    {
        var field=Container(parent,label,full);var input=new TextBox{MinHeight=34};if(path=="Text")input.MaxLength=500;
        input.SetBinding(TextBox.TextProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=numeric,NotifyOnValidationError=numeric,ConverterCulture=CultureInfo.InvariantCulture});AutomationProperties.SetName(input,label);field.Children.Add(input);
    }
    private static void Select(Panel parent,object source,string path,string label,object choices,bool ids=false)
    {
        var field=Container(parent,label);var input=new ComboBox{ItemsSource=(System.Collections.IEnumerable)choices,MinHeight=34};
        if(path=="FontFamily"){input.IsEditable=true;input.SetBinding(ComboBox.TextProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});}
        else if(ids){input.DisplayMemberPath="Title";input.SelectedValuePath="Id";input.SetBinding(ComboBox.SelectedValueProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay});}
        else input.SetBinding(ComboBox.SelectedItemProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay});AutomationProperties.SetName(input,label);field.Children.Add(input);
    }
    private void ColorField(Panel parent,object source,string path,string label)
    {
        var field=Container(parent,label);var row=new DockPanel();field.Children.Add(row);var choose=new Button{Content="●",Width=28,Padding=new(0),Margin=new(5,0,0,0),ToolTip="Choose "+label.ToLowerInvariant()};DockPanel.SetDock(choose,Dock.Right);row.Children.Add(choose);
        var input=new TextBox{MinHeight=34};input.SetBinding(TextBox.TextProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AutomationProperties.SetName(input,label);row.Children.Add(input);
        choose.Click+=(_,_)=>{var property=source.GetType().GetProperty(path)!;var initial=property.GetValue(source) as string;var dialog=new ColorPickerDialog(label,string.IsNullOrEmpty(initial)?"#101820":initial){Owner=Window.GetWindow(this)};if(dialog.ShowDialog()==true)property.SetValue(source,dialog.SelectedColor);};
    }
}
