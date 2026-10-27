using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Win32;

namespace Hypershell;

/// <summary>The background step's color, image, border, and ten curated visual choices.</summary>
public sealed class HyperlibBackgroundStageEditor : ContentControl
{
    private HyperlibWorkWizardViewModel? _wizard;
    private HyperlibWizardTemplateViewModel? _template;
    public HyperlibBackgroundStageEditor()
    {
        DataContextChanged+=(_,_)=>Connect();Loaded+=(_,_)=>Connect();
        Unloaded+=(_,_)=>{if(_wizard is not null)_wizard.PropertyChanged-=Changed;_wizard=null;};
    }
    private void Connect()
    {
        if(_wizard is not null)_wizard.PropertyChanged-=Changed;_wizard=DataContext as HyperlibWorkWizardViewModel;
        if(_wizard is not null)_wizard.PropertyChanged+=Changed;_template=null;Build();
    }
    private void Changed(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName==nameof(HyperlibWorkWizardViewModel.CurrentTemplate))Build();
    }
    private void Build()
    {
        if(_wizard is not {} wizard||ReferenceEquals(_template,wizard.CurrentTemplate))return;_template=wizard.CurrentTemplate;var page=_template;
        NameScope.SetNameScope(this,new NameScope());var root=new StackPanel();Content=root;
        var colors=Card(root);var colorHeading=new DockPanel();colors.Children.Add(colorHeading);
        var solid=new Button{Content="Solid color",Padding=new(9,5,9,5),ToolTip="Use only this color, without a design or image"};DockPanel.SetDock(solid,Dock.Right);colorHeading.Children.Add(solid);solid.Click+=(_,_)=>page.UseSolidBackground();
        colorHeading.Children.Add(Heading("Background color"));
        var picker=new HyperlibLiveColorPicker();RegisterName("BackgroundColorPicker",picker);picker.SetBinding(HyperlibLiveColorPicker.ColorProperty,new Binding(nameof(page.Background)){Source=page,Mode=BindingMode.OneWay});
        picker.ColorChanged+=color=>{if(!string.Equals(page.Background,color,StringComparison.Ordinal))page.SetBackgroundColor(color);};colors.Children.Add(picker);
        colors.Children.Add(Help("Drag the sliders or enter a hex color. Your page updates while you choose."));

        var image=Card(root);image.Children.Add(Heading("Background image"));
        var imageRow=new DockPanel{Margin=new(0,0,0,10)};
        var remove=new Button{Content="×",Width=30,MinWidth=30,Padding=new(0),Margin=new(6,0,0,0),ToolTip="Remove background image"};remove.SetBinding(IsEnabledProperty,new Binding(nameof(page.HasBackgroundImage)){Source=page});remove.Click+=(_,_)=>page.SetBackgroundImage("");DockPanel.SetDock(remove,Dock.Right);imageRow.Children.Add(remove);AutomationProperties.SetName(remove,"Remove background image");
        var choose=new Button{Content="Choose image…",Padding=new(10,7,10,7),Margin=new(6,0,0,0)};RegisterName("ChooseBackgroundImageButton",choose);DockPanel.SetDock(choose,Dock.Right);imageRow.Children.Add(choose);
        var path=new TextBox{IsReadOnly=true,MinHeight=34,VerticalContentAlignment=VerticalAlignment.Center};path.SetBinding(TextBox.TextProperty,new Binding(nameof(page.BackgroundImagePath)){Source=page});path.SetBinding(ToolTipProperty,new Binding(nameof(page.BackgroundImagePath)){Source=page});imageRow.Children.Add(path);AutomationProperties.SetName(path,"Background image path");image.Children.Add(imageRow);
        choose.Click+=(_,_)=>{var dialog=new OpenFileDialog{Title="Choose a page background image",Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.avif",CheckFileExists=true};if(dialog.ShowDialog(Window.GetWindow(this))==true)page.SetBackgroundImage(dialog.FileName);};
        var fit=Select(page,nameof(page.BackgroundImageFit),new[]{new Option("cover","Fill the page"),new Option("contain","Show the entire image"),new Option("tile","Repeat as a pattern")},"Image fit");RegisterName("BackgroundImageFit",fit);image.Children.Add(fit);

        var border=Card(root);border.Children.Add(Heading("Page border"));
        var fields=new WrapPanel();border.Children.Add(fields);
        var colorField=FieldContainer(fields,"Border color");var hex=new TextBox{MinHeight=33};hex.SetBinding(TextBox.TextProperty,new Binding(nameof(page.PageBorderColor)){Source=page,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});colorField.Children.Add(hex);AutomationProperties.SetName(hex,"Page border color");
        var styleField=FieldContainer(fields,"Style");styleField.Children.Add(Select(page,nameof(page.PageBorderStyle),new[]{new Option("none","None"),new Option("solid","Solid"),new Option("dashed","Dashed"),new Option("dotted","Dotted"),new Option("double","Double")},"Page border style"));
        Numeric(fields,page,nameof(page.PageBorderWidth),"Width",0,20);Numeric(fields,page,nameof(page.PageBorderRadius),"Corner radius",0,200);
        var borderPicker=new HyperlibLiveColorPicker();borderPicker.SetBinding(HyperlibLiveColorPicker.ColorProperty,new Binding(nameof(page.PageBorderColor)){Source=page,Mode=BindingMode.OneWay});borderPicker.ColorChanged+=color=>{if(!string.Equals(page.PageBorderColor,color,StringComparison.Ordinal))page.PageBorderColor=color;};
        var expand=new Expander{Header="Pick a border color",Content=borderPicker,Margin=new(0,0,0,6)};border.Children.Add(expand);

        var designs=Card(root);designs.Children.Add(Heading("Background designs"));designs.Children.Add(Help("Ten starting points. Choose a design, then adjust its color above."));
        var current=new TextBlock{FontSize=11,Foreground=Resource("SecondaryVividBrush",Brushes.LightGreen),Margin=new(0,0,0,10)};current.SetBinding(TextBlock.TextProperty,new Binding(nameof(page.BackgroundDesignSummary)){Source=page,StringFormat="Current: {0}"});designs.Children.Add(current);
        var cards=new WrapPanel();RegisterName("BackgroundDesignCards",cards);designs.Children.Add(cards);
        foreach(var design in wizard.BackgroundDesigns)
        {
            var content=new StackPanel();var thumb=new Border{Height=82,CornerRadius=new(5),ClipToBounds=true,Background=ColorBrush(design.DefaultColor)};
            var imagePath=Path.Combine(AppContext.BaseDirectory,design.ThumbnailFile.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(imagePath))
            {
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri(imagePath);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();thumb.Child=new Image{Source=bitmap,Stretch=Stretch.UniformToFill};
            }
            content.Children.Add(thumb);content.Children.Add(new TextBlock{Text=design.Title,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,8,0,0)});
            var button=new Button{Content=content,Width=176,MinHeight=129,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(9),Margin=new(0,0,10,10),Command=wizard.SelectBackgroundDesignCommand,CommandParameter=design,ToolTip=design.Description};
            AutomationProperties.SetName(button,"Background design "+design.Title);
            var style=new Style(typeof(Button),TryFindResource(typeof(Button)) as Style);
            style.Setters.Add(new Setter(BorderThicknessProperty,new Thickness(1)));style.Setters.Add(new Setter(BorderBrushProperty,Resource("BorderBrush",Brushes.Gray)));
            var selected=new DataTrigger{Binding=new Binding(nameof(page.BackgroundDesignId)){Source=page},Value=design.Id};selected.Setters.Add(new Setter(BorderBrushProperty,Resource("SecondaryVividBrush",Brushes.LightGreen)));selected.Setters.Add(new Setter(BorderThicknessProperty,new Thickness(2)));style.Triggers.Add(selected);button.Style=style;
            button.RequestBringIntoView+=(_,args)=>args.Handled=true;cards.Children.Add(button);
        }
    }
    private static StackPanel Card(Panel parent)
    {
        var panel=new StackPanel();parent.Children.Add(new Border{Child=panel,Padding=new(15),CornerRadius=new(8),BorderThickness=new(1),BorderBrush=Resource("BorderBrush",Brushes.Gray),Background=Resource("PanelBrush",Brushes.Black),Margin=new(0,0,0,14)});return panel;
    }
    private static TextBlock Heading(string text)=>new(){Text=text,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,11)};
    private static TextBlock Help(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,FontSize=11,LineHeight=18,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,8,0,12)};
    private static StackPanel FieldContainer(Panel parent,string label)
    {
        var field=new StackPanel{Width=146,Margin=new(0,0,12,12)};field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,0,0,5)});parent.Children.Add(field);return field;
    }
    private static ComboBox Select(object source,string path,IEnumerable<Option> choices,string name)
    {
        var input=new ComboBox{ItemsSource=choices,DisplayMemberPath="Title",SelectedValuePath="Id",MinHeight=33};input.SetBinding(ComboBox.SelectedValueProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay});AutomationProperties.SetName(input,name);return input;
    }
    private static void Numeric(Panel parent,object source,string path,string label,double minimum,double maximum)
    {
        var field=FieldContainer(parent,label);var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new(36)});
        var slider=new Slider{Minimum=minimum,Maximum=maximum,TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=maximum/10,MinHeight=33,Margin=new(0,0,8,0)};slider.SetBinding(Slider.ValueProperty,new Binding(path){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AutomationProperties.SetName(slider,"Page border "+label.ToLowerInvariant());row.Children.Add(slider);
        var value=new TextBlock{VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right};value.SetBinding(TextBlock.TextProperty,new Binding(path){Source=source,StringFormat="{0:0}"});Grid.SetColumn(value,1);row.Children.Add(value);field.Children.Add(row);
    }
    private static Brush Resource(string key,Brush fallback)=>(Brush?)Application.Current?.TryFindResource(key)??fallback;
    private static Brush ColorBrush(string value)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private sealed record Option(string Id,string Title);
}
