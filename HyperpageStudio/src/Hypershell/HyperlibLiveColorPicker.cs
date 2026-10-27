using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Hypershell;

/// <summary>An inline color tool: every slider, swatch, and hex edit commits immediately.</summary>
public sealed class HyperlibLiveColorPicker : Grid
{
    public static readonly DependencyProperty ColorProperty=DependencyProperty.Register(nameof(Color),typeof(string),typeof(HyperlibLiveColorPicker),
        new FrameworkPropertyMetadata("#101820",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,(sender,_)=>((HyperlibLiveColorPicker)sender).Synchronize()));
    private readonly Slider[] _channels=[];
    private readonly Border _swatch=new(){Width=48,Height=35,CornerRadius=new(5),BorderThickness=new(1),BorderBrush=Brushes.Gray,Margin=new(0,0,10,0)};
    private bool _syncing;
    public string Color{get=>(string)GetValue(ColorProperty);set=>SetValue(ColorProperty,value);}
    public event Action<string>? ColorChanged;
    public HyperlibLiveColorPicker()
    {
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});
        var top=new DockPanel{Margin=new(0,0,0,10)};Children.Add(top);DockPanel.SetDock(_swatch,Dock.Left);top.Children.Add(_swatch);
        var hex=new TextBox{MinHeight=35,MaxLength=7,VerticalContentAlignment=VerticalAlignment.Center};hex.SetBinding(TextBox.TextProperty,new Binding(nameof(Color)){Source=this,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});AutomationProperties.SetName(hex,"Live color hex");top.Children.Add(hex);
        var rows=new StackPanel();SetRow(rows,1);Children.Add(rows);
        var channels=new List<Slider>();
        foreach(var label in new[]{"Red","Green","Blue"})
        {
            var row=new Grid{Margin=new(0,0,0,4)};row.ColumnDefinitions.Add(new(){Width=new(42)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new(36)});rows.Children.Add(row);
            row.Children.Add(new TextBlock{Text=label,FontSize=11,VerticalAlignment=VerticalAlignment.Center,Opacity=.75});
            var slider=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=16,MinHeight=23,Margin=new(4,0,8,0)};SetColumn(slider,1);row.Children.Add(slider);channels.Add(slider);AutomationProperties.SetName(slider,"Live color "+label.ToLowerInvariant());
            var number=new TextBlock{FontSize=11,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right};number.SetBinding(TextBlock.TextProperty,new Binding(nameof(Slider.Value)){Source=slider,StringFormat="{0:0}"});SetColumn(number,2);row.Children.Add(number);
            slider.ValueChanged+=(_,_)=>CommitChannels();
        }
        _channels=channels.ToArray();
        var palette=new WrapPanel{Margin=new(0,7,0,0)};SetRow(palette,2);Children.Add(palette);
        foreach(var color in new[]{"#FFFFFF","#F4F0E6","#CBD5E1","#101820","#18243A","#163C35","#582E4F","#B85C38"})
        {
            var button=new Button{Width=27,Height=25,MinWidth=0,Padding=new(0),Margin=new(0,0,6,2),Background=Brush(color),BorderBrush=Brushes.Gray,BorderThickness=new(1),ToolTip=color};AutomationProperties.SetName(button,"Choose color "+color);button.Click+=(_,_)=>SetCurrentValue(ColorProperty,color);palette.Children.Add(button);
        }
        Synchronize();
    }
    private void CommitChannels()
    {
        if(_syncing||_channels.Length!=3)return;
        SetCurrentValue(ColorProperty,$"#{(int)_channels[0].Value:X2}{(int)_channels[1].Value:X2}{(int)_channels[2].Value:X2}");
    }
    private void Synchronize()
    {
        if(_syncing)return;_syncing=true;
        try
        {
            if(TryColor(Color,out var color))
            {
                _swatch.Background=new SolidColorBrush(color);
                if(_channels.Length==3){_channels[0].Value=color.R;_channels[1].Value=color.G;_channels[2].Value=color.B;}
            }
        }
        finally{_syncing=false;}
        ColorChanged?.Invoke(Color);
    }
    private static bool TryColor(string value,out System.Windows.Media.Color color)
    {
        color=default;if(value.Length!=7||value[0]!='#'||!uint.TryParse(value.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb))return false;
        color=System.Windows.Media.Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);return true;
    }
    private static Brush Brush(string color)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color));
}
