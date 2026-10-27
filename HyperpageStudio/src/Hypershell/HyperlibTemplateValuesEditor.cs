using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Hypershell.ViewModels;
using Microsoft.Win32;

namespace Hypershell;

/// <summary>Edits the actual exposed schema fields of any imported library object.</summary>
public sealed class HyperlibTemplateValuesEditor : StackPanel
{
    public HyperlibTemplateValuesEditor(IEnumerable<HyperlibHeaderTemplateValueViewModel> fields)
    {
        foreach(var field in fields)
        {
            var row=new StackPanel{Margin=new(0,0,0,13)};Children.Add(row);var heading=new DockPanel();row.Children.Add(heading);
            var reset=new Button{Content="Reset",Padding=new(7,2,7,2),FontSize=10,ToolTip="Restore this field's library default",Margin=new(8,0,0,4)};reset.Click+=(_,_)=>field.Reset();DockPanel.SetDock(reset,Dock.Right);heading.Children.Add(reset);
            heading.Children.Add(new TextBlock{Text=field.Title,FontSize=12,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,3,0,6)});
            FrameworkElement input;
            if(field.Type=="boolean")
            {
                var check=new CheckBox{Content="Enabled",Margin=new(0,5,0,5)};check.SetBinding(ToggleButton.IsCheckedProperty,Bind(field,nameof(field.BooleanValue)));input=check;
            }
            else if(field.Type=="select")
            {
                var select=new ComboBox{ItemsSource=field.Options,MinHeight=34};select.SetBinding(Selector.SelectedItemProperty,Bind(field,nameof(field.Text)));input=select;
            }
            else
            {
                var text=new TextBox{MinHeight=34,Padding=new(7,5,7,5)};text.SetBinding(TextBox.TextProperty,Bind(field,nameof(field.Text)));
                if(field.Type=="text"){text.AcceptsReturn=true;text.TextWrapping=TextWrapping.Wrap;text.MinHeight=80;text.MaxHeight=190;text.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;}
                input=text;
            }
            AutomationProperties.SetName(input,"Template property "+field.Definition.Title);row.Children.Add(input);
            if(field.Type=="media")
            {
                var choose=new Button{Content="Choose media file…",Padding=new(10,6,10,6),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,6,0,0)};
                choose.Click+=(_,_)=>{var dialog=new OpenFileDialog{Title="Choose "+field.Definition.Title,CheckFileExists=true,Filter="Media files|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.avif;*.svg;*.mp4;*.webm;*.ogv;*.mp3;*.wav;*.flac;*.ogg|All files|*.*"};if(dialog.ShowDialog(Window.GetWindow(this))==true)field.Text=dialog.FileName;};row.Children.Add(choose);
            }
            if(field.Type=="color")
            {
                var picker=new HyperlibLiveColorPicker();picker.SetBinding(HyperlibLiveColorPicker.ColorProperty,new Binding(nameof(field.Text)){Source=field,Mode=BindingMode.OneWay});picker.ColorChanged+=value=>field.Text=value;row.Children.Add(new Expander{Header="Pick a color",Content=picker,Margin=new(0,5,0,0)});
            }
            var error=new TextBlock{Foreground=Brushes.IndianRed,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,4,0,0)};error.SetBinding(TextBlock.TextProperty,new Binding(nameof(field.Error)){Source=field});row.Children.Add(error);
        }
    }
    private static Binding Bind(object source,string property)=>new(property){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged};
}
