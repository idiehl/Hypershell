using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Win32;

namespace Hypershell;

/// <summary>Shared native row-and-Box builder for independent Header, Navigation, Body, and Footer layouts.</summary>
public sealed class HyperlibHeaderStageEditor : ContentControl
{
    public static readonly DependencyProperty RegionProperty=DependencyProperty.Register(nameof(Region),typeof(string),typeof(HyperlibHeaderStageEditor),new PropertyMetadata("header",(sender,_)=>((HyperlibHeaderStageEditor)sender).Build()));
    public string Region{get=>(string)GetValue(RegionProperty);set=>SetValue(RegionProperty,value);}
    private bool IsNavigation=>Region=="navigation";
    private bool IsBody=>Region=="body";
    private bool IsFooter=>Region=="footer";
    private string RegionTitle=>IsNavigation?"Navigation":IsBody?"Body":IsFooter?"Footer":"Header";
    private HyperlibWorkWizardViewModel? _wizard;
    private HyperlibWizardTemplateViewModel? _template;
    private HyperlibWizardHeaderLayoutViewModel? _layout;
    private StackPanel? _rows;
    private readonly Dictionary<HyperlibWizardHeaderElementViewModel,Expander> _elementEditors=[];
    private readonly Dictionary<HyperlibWizardHeaderElementViewModel,StackPanel> _elementTools=[];
    private bool _updatingSelection;
    private int _invalidFields;
    private ItemsControl? _inventory;
    public HyperlibHeaderStageEditor()
    {
        DataContextChanged+=(_,_)=>Connect();Loaded+=(_,_)=>Connect();Unloaded+=(_,_)=>Disconnect();
        AddHandler(Validation.ErrorEvent,new EventHandler<ValidationErrorEventArgs>((_,args)=>
        {
            _invalidFields=Math.Max(0,_invalidFields+(args.Action==ValidationErrorEventAction.Added?1:-1));
            if(_wizard is not null)_wizard.InputValidationMessage=_invalidFields>0?"Enter a valid number for each "+Region+" field before continuing.":"";
        }));
    }
    private void Disconnect()
    {
        if(_wizard is not null)_wizard.PropertyChanged-=WizardChanged;
        if(_layout is not null){_layout.StructureChanged-=DrawRows;_layout.SelectionChanged-=UpdateSelection;_layout.DefinitionChanged-=RefreshInventory;}
        _wizard=null;_layout=null;_template=null;
    }
    private void Connect()
    {
        Disconnect();_wizard=DataContext as HyperlibWorkWizardViewModel;if(_wizard is null)return;
        _wizard.PropertyChanged+=WizardChanged;Build();
    }
    private void WizardChanged(object? sender,PropertyChangedEventArgs args){if(args.PropertyName==nameof(HyperlibWorkWizardViewModel.CurrentTemplate))Build();}
    private void Build()
    {
        if(_wizard is not {} wizard)return;var layout=IsNavigation?wizard.CurrentTemplate.NavigationLayout:IsBody?wizard.CurrentTemplate.BodyLayout:IsFooter?wizard.CurrentTemplate.FooterLayout:wizard.CurrentTemplate.HeaderLayout;
        if(ReferenceEquals(_template,wizard.CurrentTemplate)&&ReferenceEquals(_layout,layout))return;
        if(_layout is not null){_layout.StructureChanged-=DrawRows;_layout.SelectionChanged-=UpdateSelection;_layout.DefinitionChanged-=RefreshInventory;}
        _template=wizard.CurrentTemplate;_layout=layout;_layout.StructureChanged+=DrawRows;_layout.SelectionChanged+=UpdateSelection;_layout.DefinitionChanged+=RefreshInventory;
        _invalidFields=0;NameScope.SetNameScope(this,new NameScope());var root=new StackPanel();Content=root;
        var builder=Card(root);var top=new DockPanel();builder.Children.Add(top);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(8,0,0,10)};DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);
        var undo=ActionButton("↶","Undo the latest layout action",()=>_layout.Undo());undo.Width=36;undo.MinWidth=36;undo.FontSize=21;undo.Padding=new(0);undo.Margin=new(0,0,6,0);undo.SetBinding(IsEnabledProperty,new Binding(nameof(_layout.CanUndo)){Source=_layout});AutomationProperties.SetName(undo,"Undo "+Region+" layout");RegisterName("Undo"+RegionTitle+"LayoutButton",undo);actions.Children.Add(undo);
        var add=ActionButton("+ Add Block","Add a new horizontal "+Region+" row",()=>_layout.AddBlock());RegisterName("Add"+RegionTitle+"BlockButton",add);actions.Children.Add(add);top.Children.Add(Heading(RegionTitle+" Blocks"));
        if(IsBody)builder.Children.Add(Help("Arrange reusable content slots. Add writing and media when you create Pages."));
        if(IsNavigation)
        {
            var options=new WrapPanel{Margin=new(0,0,0,8)};builder.Children.Add(options);
            foreach(var (label,property) in new[]{("Show navigation",nameof(_template.IncludeNavigation)),("Sticky navigation",nameof(_template.NavigationSticky))})
            {var option=new CheckBox{Content=label,Margin=new(0,0,16,8)};option.SetBinding(ToggleButton.IsCheckedProperty,Bind(_template,property));AutomationProperties.SetName(option,label);options.Children.Add(option);}
            var placement=new ComboBox{ItemsSource=_template.NavigationPlacements.Where(option=>option.Id!="none").ToArray(),DisplayMemberPath="Title",SelectedValuePath="Id",MinHeight=33,Width=146,Margin=new(0,0,0,10),ToolTip="Navigation position"};placement.SetBinding(Selector.SelectedValueProperty,Bind(_template,nameof(_template.NavigationPlacement)));AutomationProperties.SetName(placement,"Navigation position");options.Children.Add(placement);
        }
        if(IsBody)
        {
            var options=new WrapPanel();builder.Children.Add(options);
            foreach(var (label,property,choices) in new[]{("Content width",nameof(_template.ContentWidth),_template.Widths),("Content alignment",nameof(_template.ContentAlignment),_template.Alignments)})
            {var field=new StackPanel{Width=146,Margin=new(0,0,12,12)};field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,0,0,5)});var select=new ComboBox{ItemsSource=choices,DisplayMemberPath="Title",SelectedValuePath="Id",MinHeight=33};select.SetBinding(Selector.SelectedValueProperty,Bind(_template,property));AutomationProperties.SetName(select,label);field.Children.Add(select);options.Children.Add(field);}
        }
        if(IsFooter){var show=new CheckBox{Content="Show footer",Margin=new(0,0,0,12)};show.SetBinding(ToggleButton.IsCheckedProperty,Bind(_template,nameof(_template.IncludeFooter)));AutomationProperties.SetName(show,"Show footer");builder.Children.Add(show);}
        _rows=new StackPanel();RegisterName(RegionTitle+"BlocksPanel",_rows);builder.Children.Add(_rows);
        DrawRows();
        _inventory=null;
        if(IsBody)
        {
            var inventory=Card(root);inventory.Children.Add(Heading("Slots and required objects"));_inventory=new ItemsControl();var line=new FrameworkElementFactory(typeof(TextBlock));line.SetBinding(TextBlock.TextProperty,new Binding());line.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);line.SetValue(TextBlock.FontSizeProperty,11d);line.SetValue(MarginProperty,new Thickness(0,0,0,5));_inventory.ItemTemplate=new DataTemplate{VisualTree=line};RegisterName("BodyObjectInventory",_inventory);inventory.Children.Add(new ScrollViewer{Content=_inventory,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});RefreshInventory();
        }
        var presets=Card(root);RegisterName(RegionTitle+"PresetsPanel",presets);presets.Children.Add(Heading(RegionTitle+" layouts"));
        var cards=new WrapPanel();RegisterName(RegionTitle+"PresetCards",cards);presets.Children.Add(cards);
        foreach(var preset in _layout.Presets)
        {
            var sample=IsNavigation?HyperlibNavigationLayouts.Create(preset.Id):IsBody?HyperlibBodyLayouts.Create(preset.Id):IsFooter?HyperlibFooterLayouts.Create(preset.Id):HyperlibHeaderLayouts.Create(preset.Id);var content=new StackPanel();content.Children.Add(IsBody||IsFooter?BodyThumbnail(sample):Thumbnail(sample));content.Children.Add(new TextBlock{Text=preset.Title,FontSize=12,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)});
            var card=ActionButton(content,preset.Description,()=>{_layout.ApplyPreset(preset.Id);if(IsNavigation&&_template is not null)_template.NavigationPlacement=preset.Id=="sidebar"?"left":"top";});card.Width=176;card.MinHeight=118;card.HorizontalContentAlignment=HorizontalAlignment.Stretch;card.Padding=new(9);card.Margin=new(0,0,10,10);AutomationProperties.SetName(card,RegionTitle+" layout "+preset.Title);cards.Children.Add(card);
        }
    }
    private void DrawRows()
    {
        if(_rows is null||_layout is null)return;_elementEditors.Clear();_elementTools.Clear();_rows.Children.Clear();
        if(!_layout.HasBlocks)return;
        for(var index=0;index<_layout.Blocks.Count;index++)
        {
            var block=_layout.Blocks[index];var panel=new StackPanel();var card=new Border{Child=panel,Padding=new(11),CornerRadius=new(6),BorderThickness=new(1),BorderBrush=Resource("BorderBrush",Brushes.Gray),Margin=new(0,0,0,10)};_rows.Children.Add(card);
            var title=new DockPanel();panel.Children.Add(title);var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);title.Children.Add(actions);
            actions.Children.Add(Small("↑","Move Block up",()=>_layout.MoveBlock(block,-1)));actions.Children.Add(Small("↓","Move Block down",()=>_layout.MoveBlock(block,1)));actions.Children.Add(Small("×","Remove this Block",()=>_layout.RemoveBlock(block)));
            title.Children.Add(new TextBlock{Text="Block "+(index+1),FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,0,8)});
            var fields=new WrapPanel();panel.Children.Add(fields);var rowHeight=(StackPanel)Field(fields,block,nameof(block.Height),"Row minimum height (px)",true);var heightLabel=(TextBlock)rowHeight.Children[0];heightLabel.SetBinding(TextBlock.TextProperty,new Binding(nameof(block.IsPositioned)){Source=block,Converter=new RowHeightLabelConverter()});ColorField(fields,block,nameof(block.FillColor),"Background",true);ArrangeToggle(panel,block,nameof(block.ArrangeSideBySide));
            foreach(var element in block.Elements)DrawElement(panel,block,element,0);
            var add=ActionButton("+ Add Element","Add Text, a Box, an Image, Video, or a library Template to this Block",()=>{});add.HorizontalAlignment=HorizontalAlignment.Left;add.Click+=(_,_)=>OpenAddMenu(add,block,null);panel.Children.Add(add);
        }
        UpdateSelection();
    }
    private void DrawElement(Panel parent,HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel element,int depth)
    {
        if(_layout is null)return;var frame=new Border{BorderThickness=new(1),BorderBrush=Resource("BorderBrush",Brushes.Gray),CornerRadius=new(5),Padding=depth==0?new(7):new(3),Margin=new(depth==0?0:4,0,0,8),Tag=element};var body=new StackPanel();var editor=new Expander{Content=body,HorizontalContentAlignment=HorizontalAlignment.Stretch};frame.Child=editor;parent.Children.Add(frame);_elementEditors[element]=editor;
        var row=new DockPanel{LastChildFill=true};editor.Header=row;var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
        actions.Children.Add(Small("←","Move this element before the previous element",()=>_layout.MoveElement(element,-1)));actions.Children.Add(Small("→","Move this element after the next element",()=>_layout.MoveElement(element,1)));actions.Children.Add(Small("×","Remove this element",()=>_layout.RemoveElement(element)));
        var select=ActionButton("", "Edit this "+element.Kind,()=>_layout.SelectedElement=element);select.HorizontalContentAlignment=HorizontalAlignment.Left;select.Padding=new(7,4,7,4);select.SetBinding(Button.ContentProperty,new Binding(nameof(element.Label)){Source=element});row.Children.Add(select);
        var selectedStyle=new Style(typeof(Button),TryFindResource(typeof(Button)) as Style);selectedStyle.Setters.Add(new Setter(BorderThicknessProperty,new Thickness(1)));
        var selectedTrigger=new DataTrigger{Binding=new Binding(nameof(_layout.SelectedElement)){Source=_layout},Value=element};selectedTrigger.Setters.Add(new Setter(BorderBrushProperty,Resource("SecondaryVividBrush",Brushes.LightGreen)));selectedTrigger.Setters.Add(new Setter(BackgroundProperty,Resource("SecondaryTintBrush",Brushes.DarkSlateGray)));selectedStyle.Triggers.Add(selectedTrigger);select.Style=selectedStyle;
        var tools=new StackPanel{Margin=new(4,10,4,3)};_elementTools[element]=tools;
        if(element.Kind=="box")
        {
            var add=ActionButton("+ Inside Box","Put another Box, Text, Image, Video, or a library Template inside this Box",()=>{});add.HorizontalAlignment=HorizontalAlignment.Left;add.Margin=new(4,7,0,0);add.Click+=(_,_)=>OpenAddMenu(add,block,element);body.Children.Add(add);
        }
        body.Children.Add(tools);
        if(element.Kind=="box")foreach(var child in element.Children)DrawElement(body,block,child,depth+1);
        editor.Expanded+=(_,args)=>{if(!ReferenceEquals(args.OriginalSource,editor))return;EnsureTools(element);if(!_updatingSelection)_layout.SelectedElement=element;};
        editor.Collapsed+=(_,args)=>{if(!ReferenceEquals(args.OriginalSource,editor))return;if(!_updatingSelection&&Contains(element,_layout.SelectedElement))_layout.SelectedElement=null;};
    }
    internal Panel GetInlineTools(HyperlibWizardHeaderElementViewModel element){EnsureTools(element);return _elementTools[element];}
    internal Expander GetElementEditor(HyperlibWizardHeaderElementViewModel element)=>_elementEditors[element];
    private static bool Contains(HyperlibWizardHeaderElementViewModel parent,HyperlibWizardHeaderElementViewModel? selected)=>ReferenceEquals(parent,selected)||parent.Children.Any(child=>Contains(child,selected));
    private void UpdateSelection()
    {
        if(_layout is null)return;_updatingSelection=true;try{foreach(var pair in _elementEditors){var expanded=Contains(pair.Key,_layout.SelectedElement);if(expanded)EnsureTools(pair.Key);pair.Value.IsExpanded=expanded;}}finally{_updatingSelection=false;}
    }
    private void EnsureTools(HyperlibWizardHeaderElementViewModel element){if(_elementTools.TryGetValue(element,out var tools)&&tools.Children.Count==0)DrawTools(tools,element);}
    private void OpenAddMenu(Button anchor,HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel? box)
    {
        if(_layout is null)return;var menu=CreateAddMenu(anchor,block,box);menu.IsOpen=true;
    }
    internal ContextMenu CreateAddMenu(Button anchor,HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel? box)
    {
        var menu=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom};
        foreach(var (title,kind) in IsBody?new[]{("Text area","text"),("Box","box"),("Image slot","image"),("Video slot","video")}:new[]{("Text","text"),("Box","box"),("Image","image"),("Video","video")})
        {var item=new MenuItem{Header=title};item.Click+=(_,_)=>_layout?.AddElement(block,kind,box);menu.Items.Add(item);}
        if(IsNavigation||IsFooter){var links=new MenuItem{Header=IsFooter?"Page navigation":"Navigation links"};links.Click+=(_,_)=>_layout?.AddElement(block,"navigation",box);menu.Items.Add(links);}
        menu.Items.Add(new Separator());var template=new MenuItem{Header="Template…",ToolTip="Choose any object from your Template library"};template.Click+=(_,_)=>ChooseTemplate(block,box);menu.Items.Add(template);return menu;
    }
    private void ChooseTemplate(HyperlibWizardHeaderBlockViewModel block,HyperlibWizardHeaderElementViewModel? box)
    {
        if(_wizard is null||_layout is null)return;var dialog=new HyperlibHeaderTemplatePicker(_wizard,block,box,Region){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()==true&&dialog.SelectedChoice is {} choice&&dialog.SelectedSource is {} source)_layout.AddLibraryObject(block,choice,source,box);
    }
    private void DrawTools(Panel tools,HyperlibWizardHeaderElementViewModel element)
    {
        if(_layout is null)return;
        if(IsBody&&(element.Kind is "text" or "image" or "video" or "template"))
        {
            var slot=new CheckBox{Content="Template content slot",Margin=new(0,0,0,10)};slot.SetBinding(ToggleButton.IsCheckedProperty,Bind(element,nameof(element.IsContentSlot)));AutomationProperties.SetName(slot,"Template content slot");tools.Children.Add(slot);
            var metadata=new StackPanel();SlotVisibility(metadata,element,true);tools.Children.Add(metadata);Field(metadata,element,nameof(element.SlotName),"Slot name");Field(metadata,element,nameof(element.SlotHint),"Slot hint");
        }
        if(element.Kind=="navigation")tools.Children.Add(new HyperlibNavigationSettingsEditor(element.Navigation));
        if(element.Kind=="text")
        {
            var content=new StackPanel();if(IsBody)SlotVisibility(content,element,false);tools.Children.Add(content);var text=new TextBox{MinHeight=82,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,0,0,12)};text.SetBinding(TextBox.TextProperty,Bind(element,nameof(element.Text)));AutomationProperties.SetName(text,RegionTitle+" text");content.Children.Add(text);
            var font=new WrapPanel();tools.Children.Add(font);Select(font,element,nameof(element.FontFamily),"Font",new[]{"system-ui","Arial","Georgia","Segoe UI","monospace"},true);Field(font,element,nameof(element.FontSize),"Font size (px)",true);
            Select(font,element,nameof(element.FontWeight),"Weight",new[]{new Choice(400,"Regular"),new Choice(500,"Medium"),new Choice(600,"Semibold"),new Choice(700,"Bold")});Select(font,element,nameof(element.Alignment),"Text alignment",new[]{"left","center","right"});ColorField(font,element,nameof(element.TextColor),"Text color");
        }
        var size=new WrapPanel();tools.Children.Add(size);Select(size,element,nameof(element.WidthMode),"Width units",new[]{"percent","pixels"});
        var percent=Field(size,element,nameof(element.WidthPercent),"Width (%)",true);ModeVisibility(percent,element,"percent");var pixels=Field(size,element,nameof(element.WidthPixels),"Width (px)",true);ModeVisibility(pixels,element,"pixels");Field(size,element,nameof(element.Height),"Height (px, 0 = auto)",true);
        var position=new WrapPanel();tools.Children.Add(position);Select(position,element,nameof(element.PositionAlignment),"Position alignment",new[]{"manual","left","center","right"});var x=Field(position,element,nameof(element.X),"X position (%)",true);var y=Field(position,element,nameof(element.Y),"Y position (%)",true);
        if(FindContainer(element) is {} container)foreach(var coordinate in new[]{x,y}){coordinate.SetBinding(IsEnabledProperty,new Binding("IsPositioned"){Source=container});coordinate.ToolTip="Uncheck Block Placement on the containing Block or Box to edit X and Y.";}
        if(element.Kind is "image" or "video")
        {
            var content=new StackPanel();if(IsBody)SlotVisibility(content,element,false);tools.Children.Add(content);
            var path=new DockPanel{Margin=new(0,0,0,12)};var choose=ActionButton("Choose file…","Choose a local "+element.Kind+" file",()=>ChooseMedia(element));DockPanel.SetDock(choose,Dock.Right);choose.Margin=new(8,0,0,0);path.Children.Add(choose);
            var input=new TextBox{MinHeight=34};input.SetBinding(TextBox.TextProperty,Bind(element,nameof(element.MediaPath)));AutomationProperties.SetName(input,RegionTitle+" media file");path.Children.Add(input);content.Children.Add(path);
            Field(content,element,nameof(element.Alt),"Description");Select(tools,element,nameof(element.MediaFit),"Media fit",new[]{"contain","cover","fill"});
        }
        if(element.Kind is "box" or "template" or "navigation")
        {
            if(element.Kind=="box")ArrangeToggle(tools,element,nameof(element.ArrangeSideBySide));
            if(element.Kind=="template")
            {
                if(element.TemplateSourceMessage.Length>0)tools.Children.Add(Help(element.TemplateSourceMessage));
                foreach(var group in element.TemplateValueGroups)
                {
                    tools.Children.Add(Heading(group.Title));var styling=group.Fields.Where(field=>!IsBody||!HyperlibHeaderLayouts.IsSlotContentField(field.Definition)).ToArray();if(styling.Length>0)tools.Children.Add(new HyperlibTemplateValuesEditor(styling));
                    if(IsBody){var content=new HyperlibTemplateValuesEditor(group.Fields.Where(field=>HyperlibHeaderLayouts.IsSlotContentField(field.Definition)));SlotVisibility(content,element,false);tools.Children.Add(content);}
                }
            }
            var appearance=new StackPanel();ColorField(appearance,element,nameof(element.FillColor),"Background",true);var border=new WrapPanel();appearance.Children.Add(border);Field(border,element,nameof(element.BorderWidth),"Border width (px)",true);Field(border,element,nameof(element.BorderRadius),"Corner radius (px)",true);ColorField(border,element,nameof(element.BorderColor),"Border color");Select(border,element,nameof(element.BorderStyle),"Border style",new[]{"none","solid","dashed","dotted","double"});Field(border,element,nameof(element.Padding),"Padding (px)",true);
            if(element.Kind=="box")foreach(UIElement child in appearance.Children.Cast<UIElement>().ToArray()){appearance.Children.Remove(child);tools.Children.Add(child);}else tools.Children.Add(new Expander{Header="Border and background",Content=appearance,Margin=new(0,8,0,12)});
            if(element.TemplateAppearanceFields.Count>0)tools.Children.Add(new Expander{Header="Template colors and fonts",Content=new HyperlibTemplateValuesEditor(element.TemplateAppearanceFields),Margin=new(0,10,0,0)});
        }
    }
    private void RefreshInventory()
    {
        if(!IsBody||_layout is null||_inventory is null)return;
        IEnumerable<HyperlibWizardHeaderElementViewModel> Visit(IEnumerable<HyperlibWizardHeaderElementViewModel> elements){foreach(var element in elements){yield return element;foreach(var child in Visit(element.Children))yield return child;}}
        var all=_layout.Blocks.SelectMany(block=>Visit(block.Elements)).ToArray();var lines=all.Where(element=>element.IsContentSlot).Select(element=>(string.IsNullOrWhiteSpace(element.SlotName)?"Unnamed slot":element.SlotName)+" · "+(element.Kind=="text"?"Text area":element.Kind=="template"?"Template":element.Kind=="image"?"Image":"Video")).ToList();
        lines.AddRange(all.Where(element=>element.Kind=="template").GroupBy(element=>element.TemplateObjectId).Select(group=>(group.First().TemplateTitle.Length>0?group.First().TemplateTitle:group.Key)+" × "+group.Count()+" required"));
        _inventory.ItemsSource=lines.Count==0?new[]{"No slots or library objects yet."}:lines;
    }
    private static void SlotVisibility(FrameworkElement control,HyperlibWizardHeaderElementViewModel element,bool isSlot)
    {
        var style=new Style(control.GetType());style.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));var condition=new DataTrigger{Binding=new Binding(nameof(element.IsContentSlot)){Source=element},Value=isSlot};condition.Setters.Add(new Setter(VisibilityProperty,Visibility.Visible));style.Triggers.Add(condition);control.Style=style;
    }
    private void ChooseMedia(HyperlibWizardHeaderElementViewModel element)
    {
        var dialog=new OpenFileDialog{Title="Choose "+Region+" "+element.Kind,CheckFileExists=true,Filter=element.Kind=="image"?"Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.avif;*.svg":"Video|*.mp4;*.webm;*.ogv"};if(dialog.ShowDialog(Window.GetWindow(this))==true)element.MediaPath=dialog.FileName;
    }
    private void ColorField(Panel parent,object source,string property,string label,bool allowClear=false)
    {
        var field=new StackPanel{Margin=new(0,0,12,12),Width=parent is WrapPanel?146:double.NaN};parent.Children.Add(field);field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,0,0,5)});
        var picker=new HyperlibLiveColorPicker();picker.SetBinding(HyperlibLiveColorPicker.ColorProperty,new Binding(property){Source=source,Mode=BindingMode.OneWay});picker.ColorChanged+=value=>{var member=source.GetType().GetProperty(property)!;if(!Equals(member.GetValue(source),value))member.SetValue(source,value);};
        var swatch=new Border{Width=22,Height=22,BorderBrush=Brushes.Gray,BorderThickness=new(1),CornerRadius=new(4),Margin=new(0,0,6,0)};swatch.SetBinding(Border.BackgroundProperty,new Binding(property){Source=source,Converter=new ColorBrushConverter()});var title=new StackPanel{Orientation=Orientation.Horizontal};title.Children.Add(swatch);title.Children.Add(new TextBlock{Text="Color",VerticalAlignment=VerticalAlignment.Center,FontSize=11});
        var expander=new Expander{Header=title,Content=picker,HorizontalContentAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetName(expander,label+" color picker");field.Children.Add(expander);
        if(parent is WrapPanel){var sizing=new Style(typeof(StackPanel));sizing.Setters.Add(new Setter(WidthProperty,146d));var open=new DataTrigger{Binding=new Binding(nameof(Expander.IsExpanded)){Source=expander},Value=true};open.Setters.Add(new Setter(WidthProperty,300d));sizing.Triggers.Add(open);field.ClearValue(WidthProperty);field.Style=sizing;}
        if(allowClear){var clear=ActionButton("Clear","Use a transparent "+label.ToLowerInvariant(),()=>source.GetType().GetProperty(property)!.SetValue(source,""));clear.Padding=new(7,3,7,3);clear.FontSize=10;clear.HorizontalAlignment=HorizontalAlignment.Left;clear.Margin=new(0,5,0,0);AutomationProperties.SetName(clear,"Clear "+label.ToLowerInvariant());field.Children.Add(clear);}
    }
    private static void ArrangeToggle(Panel parent,object source,string property)
    {
        var toggle=new CheckBox{Content="Block Placement",ToolTip="Checked: side by side. Unchecked: free X/Y placement.",VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,0,12)};toggle.SetBinding(ToggleButton.IsCheckedProperty,Bind(source,property));AutomationProperties.SetName(toggle,"Block Placement");parent.Children.Add(toggle);
    }
    private object? FindContainer(HyperlibWizardHeaderElementViewModel element)
    {
        if(_layout is null)return null;HyperlibWizardHeaderElementViewModel? Find(IEnumerable<HyperlibWizardHeaderElementViewModel> items){foreach(var item in items){if(item.Children.Contains(element))return item;var nested=Find(item.Children);if(nested is not null)return nested;}return null;}
        foreach(var block in _layout.Blocks){if(block.Elements.Contains(element))return block;var box=Find(block.Elements);if(box is not null)return box;}return null;
    }
    private static void ModeVisibility(FrameworkElement field,HyperlibWizardHeaderElementViewModel element,string mode)
    {
        var style=new Style(field.GetType());style.Setters.Add(new Setter(VisibilityProperty,Visibility.Collapsed));var trigger=new DataTrigger{Binding=new Binding(nameof(element.WidthMode)){Source=element},Value=mode};trigger.Setters.Add(new Setter(VisibilityProperty,Visibility.Visible));style.Triggers.Add(trigger);field.Style=style;
    }
    private static FrameworkElement Thumbnail(HyperlibHeaderLayoutDefinition layout)
    {
        var canvas=new Canvas{Height=66,Background=new SolidColorBrush(Color.FromRgb(12,23,31)),ClipToBounds=true};var total=Math.Max(1,layout.Blocks.Sum(block=>block.Height));var scale=66/total;var top=0d;
        foreach(var block in layout.Blocks)
        {
            var row=new Canvas{Width=154,Height=Math.Max(8,block.Height*scale),Background=Brush(block.FillColor,Color.FromRgb(21,39,49))};Canvas.SetTop(row,top);canvas.Children.Add(row);top+=block.Height*scale;
            void Draw(IEnumerable<HyperlibHeaderElementDefinition> elements,Canvas owner,double width,double height)
            {
                foreach(var element in elements)
                {
                    var itemWidth=Math.Max(2,width*element.WidthPercent/100);var itemHeight=element.Height>0?Math.Max(3,element.Height*scale):10;
                    var shape=new Border{Width=itemWidth,Height=itemHeight,Background=Brush(element.FillColor,element.Kind=="text"?Color.FromRgb(157,190,181):Color.FromRgb(49,84,96)),BorderBrush=Brush(element.BorderColor,Color.FromRgb(101,168,142)),BorderThickness=new(element.Kind=="box"?1:0),CornerRadius=new(2)};
                    Canvas.SetLeft(shape,width*element.X/100);Canvas.SetTop(shape,height*element.Y/100);owner.Children.Add(shape);
                    if(element.Kind=="box"&&element.Children.Count>0){var inside=new Canvas{ClipToBounds=true};shape.Child=inside;Draw(element.Children,inside,itemWidth,itemHeight);}
                }
            }
            Draw(block.Elements,row,154,block.Height*scale);
        }
        return canvas;
    }
    private static FrameworkElement BodyThumbnail(HyperlibHeaderLayoutDefinition layout)
    {
        const double width=154,height=66,gap=3;
        var canvas=new Canvas{Width=width,Height=height,Background=new SolidColorBrush(Color.FromRgb(12,23,31)),ClipToBounds=true};
        var available=Math.Max(1,height-gap*(layout.Blocks.Count+1));var total=Math.Max(1,layout.Blocks.Sum(block=>block.Height));var scale=available/total;var top=gap;
        foreach(var block in layout.Blocks)
        {
            var rowHeight=Math.Max(1,block.Height*scale);var row=new Canvas{Width=width-6,Height=rowHeight,Background=Brush(block.FillColor,Color.FromRgb(17,33,43)),ClipToBounds=true};Canvas.SetLeft(row,3);Canvas.SetTop(row,top);canvas.Children.Add(row);top+=rowHeight+gap;
            Draw(block.Elements,row,width-10,Math.Max(1,rowHeight-4),block.Arrangement,2,2);
        }
        return canvas;

        void Draw(IReadOnlyList<HyperlibHeaderElementDefinition> elements,Canvas owner,double areaWidth,double areaHeight,string arrangement,double left,double y)
        {
            if(elements.Count==0||areaWidth<1||areaHeight<1)return;
            var horizontal=arrangement=="horizontal";var vertical=arrangement=="vertical";
            var availableWidth=Math.Max(1,areaWidth-(horizontal?gap*(elements.Count-1):0));
            var weights=elements.Select(element=>Math.Max(1,element.WidthMode=="pixels"?element.WidthPixels:element.WidthPercent)).ToArray();var weightSum=weights.Sum();
            var wantedHeights=elements.Select(element=>element.Height>0?Math.Max(1,element.Height*scale):element.Kind=="text"?Math.Clamp(element.FontSize/5,3,7):areaHeight).ToArray();
            var verticalScale=vertical?Math.Min(1,Math.Max(1,areaHeight-gap*(elements.Count-1))/Math.Max(1,wantedHeights.Sum())):1;
            for(var index=0;index<elements.Count;index++)
            {
                var element=elements[index];var itemWidth=horizontal?availableWidth*weights[index]/weightSum:Math.Min(areaWidth,areaWidth*element.WidthPercent/100);var allocatedWidth=itemWidth;
                var itemHeight=Math.Min(areaHeight,wantedHeights[index]*(vertical?verticalScale:1));
                var x=left+(horizontal||vertical?0:areaWidth*element.X/100);var itemY=y+(horizontal||vertical?0:areaHeight*element.Y/100);
                if(element.Kind=="text")
                {
                    itemWidth*=Math.Min(.95,.5+Math.Min(element.Text.Length,24)*.018);
                    var line=new Border{Width=Math.Max(1,itemWidth),Height=Math.Max(1,itemHeight),CornerRadius=new(1),Background=Brush(element.TextColor,Color.FromRgb(157,190,181))};Canvas.SetLeft(line,x);Canvas.SetTop(line,itemY);owner.Children.Add(line);
                }
                else
                {
                    var shape=new Border{Width=Math.Max(1,itemWidth),Height=Math.Max(1,itemHeight),Background=Brush(element.FillColor,element.Kind is "image" or "video"?Color.FromRgb(49,84,96):Color.FromRgb(25,49,61)),BorderBrush=Brush(element.BorderColor,Color.FromRgb(101,168,142)),BorderThickness=new(1),CornerRadius=new(2),ClipToBounds=true};Canvas.SetLeft(shape,x);Canvas.SetTop(shape,itemY);owner.Children.Add(shape);
                    if(element.Kind=="box"&&element.Children.Count>0)
                    {var inside=new Canvas{ClipToBounds=true};shape.Child=inside;Draw(element.Children,inside,Math.Max(1,itemWidth-8),Math.Max(1,itemHeight-8),element.Arrangement,3,3);}
                    else if(element.Kind is "image" or "video")
                    {var inside=new Canvas{ClipToBounds=true};shape.Child=inside;var mountain=new System.Windows.Shapes.Polyline{Stroke=new SolidColorBrush(Color.FromRgb(157,190,181)),StrokeThickness=1,Points=new PointCollection{new(3,itemHeight-4),new(itemWidth*.35,itemHeight*.35),new(itemWidth*.55,itemHeight*.6),new(itemWidth*.72,itemHeight*.42),new(itemWidth-3,itemHeight-4)}};inside.Children.Add(mountain);}
                    else if(element.Kind=="navigation"&&element.Navigation is {} navigation)
                    {
                        var inside=new Canvas{ClipToBounds=true};shape.Child=inside;
                        var links=new List<string>();if(navigation.ShowPreviousNext)links.Add("‹");if(navigation.IncludeHome)links.Add("⌂");if(navigation.AutoPages)links.AddRange(new[]{"page","page"});links.AddRange(navigation.CustomLinks.Take(2).Select(_=>"page"));if(navigation.ShowPreviousNext)links.Add("›");
                        var count=Math.Min(5,links.Count);var verticalLinks=navigation.Orientation=="vertical";var linkWidth=Math.Max(1,verticalLinks?Math.Min(34,itemWidth-8):(itemWidth-8-gap*Math.Max(0,count-1))/Math.Max(1,count));var linkHeight=Math.Max(1,Math.Min(10,verticalLinks?(itemHeight-8-gap*Math.Max(0,count-1))/Math.Max(1,count):itemHeight-8));
                        for(var linkIndex=0;linkIndex<count;linkIndex++)
                        {
                            var pill=new Border{Width=linkWidth,Height=linkHeight,CornerRadius=new(2),Background=Brush(navigation.BackgroundColor,Color.FromRgb(38,69,77)),BorderBrush=Brush(navigation.TextColor,Color.FromRgb(178,209,198)),BorderThickness=new(.5)};
                            if(links[linkIndex]=="page")pill.Child=new Border{Width=Math.Max(1,linkWidth*.55),Height=Math.Min(1.5,linkHeight),Background=Brush(navigation.TextColor,Color.FromRgb(178,209,198)),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
                            else pill.Child=new TextBlock{Text=links[linkIndex],FontSize=Math.Min(10,linkHeight+1),LineHeight=Math.Max(1,linkHeight),Foreground=Brush(navigation.TextColor,Color.FromRgb(178,209,198)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                            Canvas.SetLeft(pill,verticalLinks?Math.Max(3,(itemWidth-linkWidth)/2):3+linkIndex*(linkWidth+gap));Canvas.SetTop(pill,verticalLinks?3+linkIndex*(linkHeight+gap):Math.Max(3,(itemHeight-linkHeight)/2));inside.Children.Add(pill);
                        }
                    }
                }
                if(horizontal)left+=allocatedWidth+gap;else if(vertical)y+=itemHeight+gap;
            }
        }
    }
    private static Button ActionButton(object content,string tooltip,Action click)
    {
        var button=new Button{Content=content,ToolTip=tooltip,Padding=new(10,7,10,7)};button.Click+=(_,_)=>click();button.RequestBringIntoView+=(_,args)=>args.Handled=true;return button;
    }
    private static Button Small(string text,string tooltip,Action click){var button=ActionButton(text,tooltip,click);button.Width=28;button.MinWidth=28;button.Padding=new(0);button.Margin=new(3,0,0,5);return button;}
    private static StackPanel Card(Panel parent){var panel=new StackPanel();parent.Children.Add(new Border{Child=panel,Padding=new(15),CornerRadius=new(8),BorderThickness=new(1),BorderBrush=Resource("BorderBrush",Brushes.Gray),Background=Resource("PanelBrush",Brushes.Black),Margin=new(0,0,0,14)});return panel;}
    private static TextBlock Heading(string text)=>new(){Text=text,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)};
    private static TextBlock Help(string text)=>new(){Text=text,FontSize=11,LineHeight=18,TextWrapping=TextWrapping.Wrap,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,5,0,12)};
    private static Binding Bind(object source,string path,bool numeric=false)=>new(path){Source=source,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnExceptions=numeric,NotifyOnValidationError=numeric,ConverterCulture=CultureInfo.InvariantCulture,StringFormat=numeric?"{0:0.###}":null};
    private static FrameworkElement Field(Panel parent,object source,string path,string label,bool numeric=false)
    {
        var field=new StackPanel{Margin=new(0,0,12,12),Width=parent is WrapPanel?146:double.NaN};field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,0,0,5),TextWrapping=TextWrapping.Wrap});var input=new TextBox{MinHeight=33};input.SetBinding(TextBox.TextProperty,Bind(source,path,numeric));AutomationProperties.SetName(input,label);field.Children.Add(input);parent.Children.Add(field);return field;
    }
    private static void Select(Panel parent,object source,string path,string label,object items,bool editable=false)
    {
        var field=new StackPanel{Margin=new(0,0,12,12),Width=parent is WrapPanel?146:double.NaN};field.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Resource("MutedForegroundBrush",Brushes.Gray),Margin=new(0,0,0,5)});var select=new ComboBox{ItemsSource=(System.Collections.IEnumerable)items,MinHeight=33};
        if(editable){var current=source.GetType().GetProperty(path)?.GetValue(source)as string;select.ItemsSource=((System.Collections.IEnumerable)items).Cast<string>().Append(current??"").Where(item=>item.Length>0).Distinct().ToArray();select.SetBinding(ComboBox.SelectedItemProperty,Bind(source,path));}else if(items is Choice[]){select.DisplayMemberPath="Title";select.SelectedValuePath="Value";select.SetBinding(ComboBox.SelectedValueProperty,Bind(source,path));}else select.SetBinding(ComboBox.SelectedItemProperty,Bind(source,path));AutomationProperties.SetName(select,label);field.Children.Add(select);parent.Children.Add(field);
    }
    private static Brush Resource(string key,Brush fallback)=>(Brush?)Application.Current?.TryFindResource(key)??fallback;
    private static Brush Brush(string value,Color fallback){try{return string.IsNullOrEmpty(value)?new SolidColorBrush(fallback):new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));}catch{return new SolidColorBrush(fallback);}}
    private sealed record Choice(int Value,string Title);
    private sealed class ColorBrushConverter:IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture){try{return value is string text&&text.Length>0?new SolidColorBrush((Color)ColorConverter.ConvertFromString(text)):Brushes.Transparent;}catch{return Brushes.Transparent;}}
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
    private sealed class RowHeightLabelConverter:IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is true?"Row height (px)":"Row minimum height (px)";
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
}
