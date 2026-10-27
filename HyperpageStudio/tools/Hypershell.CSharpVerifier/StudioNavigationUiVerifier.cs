using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using HyperLib.Core;
using Hypershell;
using Hypershell.Services;
using Hypershell.ViewModels;

/// <summary>Native acceptance for the independent Navigation builder and the reordered six-stage workflow.</summary>
public static class StudioNavigationUiVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-navigation-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var receipt=new Receipt{Fixture=fixture};
        try{await RunStaAsync(()=>{LoadResources(repositoryRoot);foreach(var size in new[]{new Size(1280,940),new Size(640,940)})VerifySize(size,libraryRoot,fixture,receipt);return Task.CompletedTask;});receipt.Passed=true;}
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{await File.WriteAllTextAsync(Path.Combine(fixture,"studio-navigation-ui-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_NAVIGATION_UI_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }
    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        var model=new HyperlibWorkWizardViewModel(libraryRoot);model.InitializeTitle("Navigation builder acceptance");
        var header=model.LandingTemplate.HeaderLayout;var headerRow=header.AddBlock();var identity=header.AddElement(headerRow,"text");identity.Text="Independent header identity";identity.X=13.125;identity.Y=9.25;
        var headerSnapshot=JsonSerializer.Serialize(header.BuildDefinition(),ContentJson.Options);
        var view=new HyperlibWorkWizardView{DataContext=model};Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();
        using var host=new HwndSource(new HwndSourceParameters("Hidden Navigation builder fixture"){Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080});host.RootVisual=view;Layout(view,size);
        Check(receipt,model.CurrentStage=="background"&&model.NextLabel=="Next: Header","The reordered workflow begins with Background then Header.");
        model.NextCommand.Execute(null);Check(receipt,model.IsHeaderStage&&model.NextLabel=="Next: Navigation","Header leads directly to Navigation.");model.NextCommand.Execute(null);Layout(view,size);
        var editor=Named<HyperlibHeaderStageEditor>(view,"NavigationEditor");var scroll=Named<ScrollViewer>(view,"ChoicesScroll");var navigation=model.CurrentTemplate.NavigationLayout;
        Check(receipt,model.IsNavigationStage&&model.CurrentStage=="navigation"&&model.Step==2&&editor.Region=="navigation"&&editor.Visibility==Visibility.Visible
            &&Named<FrameworkElement>(view,"HeaderEditor").Visibility==Visibility.Collapsed&&Named<FrameworkElement>(view,"LibraryStageCard").Visibility==Visibility.Collapsed,
            "Stage 3 displays the independent Navigation Block builder instead of the Header builder or catalogue list.");
        Check(receipt,Named<TextBlock>(view,"StepTitleLabel").Text=="Navigation"&&Named<TextBlock>(view,"StepNumberLabel").Text=="Step 3 of 6"
            &&Named<TextBlock>(view,"StageBreadcrumbLabel").Text=="Background  ›  Header  ›  Navigation"&&model.NextLabel=="Next: Body","Navigation stage copy, breadcrumb, and next action use the actual reordered workflow.");
        var cards=Named<Panel>(editor,"NavigationPresetCards");Check(receipt,cards.Children.Count==6&&navigation.Presets.Count==6,"Navigation offers six editable starter layouts.");
        foreach(var button in cards.Children.OfType<Button>())
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
            Check(receipt,navigation.Enabled&&navigation.Blocks.Count>0&&Elements(navigation).Any(element=>element.Kind=="navigation")
                &&JsonSerializer.Serialize(header.BuildDefinition(),ContentJson.Options)==headerSnapshot,"Each Navigation preset hydrates editable Blocks and real navigation links without altering Header.");
        }
        cards.Children.OfType<Button>().Single(button=>AutomationProperties.GetName(button)=="Navigation layout Sidebar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(receipt,model.CurrentTemplate.NavigationPlacement=="left","The Sidebar preset selects an actual sidebar position rather than a full-width bar above content.");
        var firstPreset=(Button)cards.Children[0];model.CurrentTemplate.Background="#F4F2EC";firstPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(receipt,Elements(navigation).Single(element=>element.Kind=="navigation").BuildDefinition().Navigation!.TextColor=="#17212b","A transparent Navigation preset starts with readable dark links on a light Page background.");
        model.CurrentTemplate.Background="#101820";firstPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        var sticky=Descendants<CheckBox>(editor).Single(control=>AutomationProperties.GetName(control)=="Sticky navigation");sticky.SetCurrentValue(ToggleButton.IsCheckedProperty,true);
        Check(receipt,model.PreviewDefinition.LandingTemplate.NavigationSticky&&model.PreviewDefinition.LandingTemplate.NavigationLayout.Enabled,"The Sticky navigation control changes the live saved recipe.");
        var show=Descendants<CheckBox>(editor).Single(control=>AutomationProperties.GetName(control)=="Show navigation");var visibleLayout=JsonSerializer.Serialize(navigation.BuildDefinition(),ContentJson.Options);show.SetCurrentValue(ToggleButton.IsCheckedProperty,false);
        Check(receipt,!model.PreviewDefinition.LandingTemplate.IncludeNavigation&&model.NextCommand.CanExecute(null)&&JsonSerializer.Serialize(navigation.BuildDefinition(),ContentJson.Options)==visibleLayout,"Hiding navigation preserves its editable Blocks and allows progression with the visibility choice saved.");show.SetCurrentValue(ToggleButton.IsCheckedProperty,true);
        Render(view,Path.Combine(fixture,$"navigation-preset-{size.Width:0}.png"),size,receipt);
        var add=Named<Button>(editor,"AddNavigationBlockButton");add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);var row=navigation.Blocks.Last();
        var rootMenu=editor.CreateAddMenu(add,row,null);Check(receipt,rootMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Navigation links"))&&rootMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Template…")),"Navigation root insertion offers real links and the full Template library alongside normal elements.");
        rootMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Box")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout(view,size);var box=row.Elements.Single();
        var nestedMenu=editor.CreateAddMenu(add,row,box);nestedMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Navigation links")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout(view,size);var links=box.Children.Single();
        Check(receipt,links.Kind=="navigation"&&box.Arrangement=="positioned"&&editor.GetElementEditor(box).IsExpanded&&editor.GetElementEditor(links).IsExpanded,"Navigation links can be added inside an editable Box and open their own inline controls.");
        var tools=editor.GetInlineTools(links);var settings=links.Navigation;
        var home=Bound<TextBox>(tools,TextBox.TextProperty,settings,"HomeLabel");home.Text="Start here";home.CaretIndex=5;
        Descendants<Expander>(tools).Single(expander=>Equals(expander.Header,"Link appearance")).IsExpanded=true;Layout(view,size);
        Bound<TextBox>(tools,TextBox.TextProperty,settings,"Gap").Text="19.125";
        Bound<TextBox>(tools,TextBox.TextProperty,settings,"FontSize").Text="18.5";
        Bound<ComboBox>(tools,Selector.SelectedItemProperty,settings,"Orientation").SetCurrentValue(Selector.SelectedItemProperty,"vertical");
        Descendants<Expander>(tools).Single(expander=>AutomationProperties.GetName(expander)=="Hover color color picker").IsExpanded=true;Layout(view,size);
        var hover=Bound<HyperlibLiveColorPicker>(tools,HyperlibLiveColorPicker.ColorProperty,settings,"HoverColor");hover.SetCurrentValue(HyperlibLiveColorPicker.ColorProperty,"#294D41");Layout(view,size);
        var linkDefinition=links.BuildDefinition().Navigation!;
        Check(receipt,linkDefinition.HomeLabel=="Start here"&&linkDefinition.Gap==19.125&&linkDefinition.FontSize==18.5&&linkDefinition.Orientation=="vertical"&&linkDefinition.HoverColor=="#294D41",
            "Native link labels, spacing, font size, orientation, and live hover color update the actual Navigation element.");
        Check(receipt,ReferenceEquals(home,Bound<TextBox>(editor.GetInlineTools(links),TextBox.TextProperty,settings,"HomeLabel"))&&home.CaretIndex==5,"Navigation settings edits preserve their native input and caret.");
        var customAdd=Descendants<Button>(tools).Single(button=>AutomationProperties.GetName(button)=="Add custom navigation link");customAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        var label=Descendants<TextBox>(tools).Single(control=>AutomationProperties.GetName(control)=="Navigation link label");var target=Descendants<TextBox>(tools).Single(control=>AutomationProperties.GetName(control)=="Navigation link target");label.Text="Reference";target.Text="https://example.com/reference";Layout(view,size);
        Check(receipt,links.BuildDefinition().Navigation!.CustomLinks.Single().Label=="Reference"&&links.BuildDefinition().Navigation!.CustomLinks.Single().Target=="https://example.com/reference","Adding a custom link creates an independently editable saved label and destination.");
        target.Text="javascript:alert(1)";Layout(view,size);Check(receipt,!model.NextCommand.CanExecute(null)&&model.CurrentValidationMessage.Length>0,"An unsafe custom link destination blocks progression with validation feedback.");target.Text="https://example.com/reference";Layout(view,size);
        scroll.ScrollToVerticalOffset(Math.Min(120,scroll.ScrollableHeight));Layout(view,size);var offset=scroll.VerticalOffset;home.Text="Start here now";Layout(view,size);Check(receipt,Math.Abs(offset-scroll.VerticalOffset)<.1,"A Navigation link edit and preview update keep the choices scroll position.");
        scroll.ScrollToVerticalOffset(Math.Max(0,tools.TransformToAncestor(editor).Transform(new Point()).Y-10));Layout(view,size);Render(view,Path.Combine(fixture,$"navigation-inline-links-{size.Width:0}.png"),size,receipt);
        model.NextCommand.Execute(null);Check(receipt,model.IsBodyStage&&model.CurrentStage=="body"&&model.Step==3&&!model.ShowLibraryStage&&Named<FrameworkElement>(view,"BodyEditor").Visibility==Visibility.Visible,"Body follows Navigation with its own Block builder.");model.BackCommand.Execute(null);Check(receipt,model.IsNavigationStage&&links.BuildDefinition().Navigation!.HomeLabel=="Start here now","Back returns to the existing Navigation draft without losing edits.");
        var saved=model.BuildDefinition();var restored=new HyperlibWorkWizardViewModel(libraryRoot);restored.LoadDefinition(saved);
        Check(receipt,JsonSerializer.Serialize(saved.LandingTemplate.NavigationLayout,ContentJson.Options)==JsonSerializer.Serialize(restored.LandingTemplate.NavigationLayout.BuildDefinition(),ContentJson.Options)
            &&restored.LandingTemplate.NavigationSticky&&JsonSerializer.Serialize(restored.LandingTemplate.HeaderLayout.BuildDefinition(),ContentJson.Options)==headerSnapshot,"Navigation Blocks, link styling, custom destinations, and sticky behavior round-trip independently from Header.");
        Check(receipt,!restored.PageTemplates[0].NavigationLayout.Enabled,"The first reusable Page template has its own independent Navigation draft.");
        model.Step=5;model.NextCommand.Execute(null);model.Step=2;model.CurrentTemplate.NavigationLayout.ApplyPreset(HyperlibNavigationLayouts.All[1].Id);
        var namedSaved=model.BuildDefinition();Check(receipt,namedSaved.PageTemplates[0].Definition.NavigationLayout.Enabled
            &&JsonSerializer.Serialize(namedSaved.LandingTemplate.NavigationLayout,ContentJson.Options)==JsonSerializer.Serialize(saved.LandingTemplate.NavigationLayout,ContentJson.Options),"A named Page template can use a different Navigation layout while preserving the landing design.");
        Layout(view,size);var preview=Named<FrameworkElement>(view,"PreviewPanel").TransformToAncestor(view).TransformBounds(new Rect(Named<FrameworkElement>(view,"PreviewPanel").RenderSize));Check(receipt,preview.Width>180&&preview.Left>=0&&preview.Right<=size.Width+.1&&preview.Bottom<=size.Height+.1,"The live Page builder stays bounded in compact and wide Navigation workspaces.");
    }
    private static IEnumerable<HyperlibWizardHeaderElementViewModel> Elements(HyperlibWizardHeaderLayoutViewModel layout)
    {IEnumerable<HyperlibWizardHeaderElementViewModel> Visit(IEnumerable<HyperlibWizardHeaderElementViewModel> items){foreach(var item in items){yield return item;foreach(var child in Visit(item.Children))yield return child;}}return layout.Blocks.SelectMany(block=>Visit(block.Elements));}
    private static T Bound<T>(DependencyObject root,DependencyProperty property,object source,string path)where T:FrameworkElement=>Descendants<T>(root).First(control=>BindingOperations.GetBindingExpression(control,property)?.ParentBinding is{} binding&&ReferenceEquals(binding.Source,source)&&binding.Path?.Path==path);
    private static T Named<T>(FrameworkElement root,string name)where T:class=>root.FindName(name)as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Layout(FrameworkElement view,Size size){view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Navigation native acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null)return;XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public string? Failure{get;set;}}
}
