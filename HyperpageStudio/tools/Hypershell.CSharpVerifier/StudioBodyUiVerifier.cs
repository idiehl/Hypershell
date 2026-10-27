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

/// <summary>Native Body acceptance: real menus, nested inline edits, portable Template values, and region isolation.</summary>
public static class StudioBodyUiVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-body-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var receipt=new Receipt{Fixture=fixture};
        try{await RunStaAsync(()=>{LoadResources(repositoryRoot);foreach(var size in new[]{new Size(1280,940),new Size(640,940)})VerifySize(size,libraryRoot,fixture,receipt);return Task.CompletedTask;});receipt.Passed=true;}
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{await File.WriteAllTextAsync(Path.Combine(fixture,"studio-body-ui-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_BODY_UI_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }
    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        var wizard=new HyperlibWorkWizardViewModel(libraryRoot);wizard.InitializeTitle("Body builder acceptance");wizard.Step=3;
        var header=wizard.LandingTemplate.HeaderLayout;var headerRow=header.AddBlock();header.AddElement(headerRow,"text").Text="Preserved header";
        wizard.LandingTemplate.NavigationLayout.ApplyPreset(HyperlibNavigationLayouts.All[0].Id);wizard.LandingTemplate.FooterStyle.Text="Preserved footer";
        string OtherRegions()=>JsonSerializer.Serialize(new{Header=wizard.LandingTemplate.HeaderLayout.BuildDefinition(),Navigation=wizard.LandingTemplate.NavigationLayout.BuildDefinition(),Footer=wizard.LandingTemplate.FooterStyle.Build(),Extras=wizard.LandingTemplate.ExtrasStyle.Build()},ContentJson.Options);
        var otherSnapshot=OtherRegions();var body=wizard.LandingTemplate.BodyLayout;
        var view=new HyperlibWorkWizardView{DataContext=wizard};Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();
        using var host=new HwndSource(new HwndSourceParameters("Hidden Body builder fixture"){Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080});host.RootVisual=view;Layout(view,size);
        var editor=Named<HyperlibHeaderStageEditor>(view,"BodyEditor");var scroll=Named<ScrollViewer>(view,"ChoicesScroll");
        Check(receipt,wizard.IsBodyStage&&wizard.Step==3&&editor.Region=="body"&&editor.Visibility==Visibility.Visible&&!wizard.ShowLibraryStage
            &&Named<FrameworkElement>(view,"LibraryStageCard").Visibility==Visibility.Collapsed&&Named<FrameworkElement>(view,"HeaderEditor").Visibility==Visibility.Collapsed&&Named<FrameworkElement>(view,"NavigationEditor").Visibility==Visibility.Collapsed,
            "Step 4 uses the independent Body Block builder without duplicate Header, Navigation, or catalogue surfaces.");
        Check(receipt,Named<TextBlock>(view,"StepTitleLabel").Text=="Body"&&Named<TextBlock>(view,"StageBreadcrumbLabel").Text=="Background  ›  Header  ›  Navigation  ›  Body"&&wizard.NextLabel=="Next: Footer",
            "Body keeps the six-stage order, breadcrumb, and Footer next action.");
        var cards=Named<Panel>(editor,"BodyPresetCards");Check(receipt,cards.Children.Count==6&&body.Presets.Count==6,"Body provides six editable starter layouts.");
        var legacyBody=wizard.LibraryObjects.First(item=>item.Kind=="element"&&HyperlibWorkWizardViewModel.MatchesStage(item,"body"));wizard.LandingTemplate.ApplyObject(legacyBody.Source,"body");
        ((Button)cards.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Named<Button>(editor,"UndoBodyLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        Check(receipt,!body.Enabled&&body.Blocks.Count==0&&wizard.LandingTemplate.BuildDefinition().BodyTemplateId==legacyBody.Id&&OtherRegions()==otherSnapshot,"Undoing the first Body preset restores the disabled builder and its retained legacy Body recipe without altering Header or Navigation.");
        Named<Button>(editor,"AddBodyBlockButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Named<Button>(editor,"UndoBodyLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        Check(receipt,!body.Enabled&&body.Blocks.Count==0&&wizard.LandingTemplate.BuildDefinition().BodyTemplateId==legacyBody.Id,"Undoing the first manually added Body Block also restores the previous legacy foundation.");
        foreach(var card in cards.Children.OfType<Button>())
        {
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
            Check(receipt,body.Enabled&&body.Blocks.Count>0&&body.ElementCount>0&&OtherRegions()==otherSnapshot,"Every Body preset creates editable Blocks while preserving all other page regions.");
        }
        ((Button)cards.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        Bound<ComboBox>(editor,Selector.SelectedValueProperty,wizard.CurrentTemplate,nameof(wizard.CurrentTemplate.ContentWidth)).SetCurrentValue(Selector.SelectedValueProperty,"wide");
        Bound<ComboBox>(editor,Selector.SelectedValueProperty,wizard.CurrentTemplate,nameof(wizard.CurrentTemplate.ContentAlignment)).SetCurrentValue(Selector.SelectedValueProperty,"center");
        Check(receipt,wizard.PreviewDefinition.LandingTemplate.ContentWidth=="wide"&&wizard.PreviewDefinition.LandingTemplate.ContentAlignment=="center","Compact Body width and alignment controls change the global content layout.");
        Render(view,Path.Combine(fixture,$"body-preset-{size.Width:0}.png"),size,receipt);
        var add=Named<Button>(editor,"AddBodyBlockButton");add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);var row=body.Blocks.Last();
        Check(receipt,row.Height==240&&row.Arrangement=="horizontal","A new Body Block starts with useful editable height and the familiar placement mode.");
        var rootMenu=editor.CreateAddMenu(add,row,null);Check(receipt,rootMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Template…"))&&rootMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Box")),"The Body root menu includes the full Template library and ordinary Boxes.");
        rootMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Box")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout(view,size);var box=row.Elements.Single();
        var nestedMenu=editor.CreateAddMenu(add,row,box);nestedMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Box")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout(view,size);var inner=box.Children.Single();
        var innerMenu=editor.CreateAddMenu(add,row,inner);Check(receipt,innerMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Template…")),"The same Template action remains available inside recursively nested Body Boxes.");
        innerMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Text area")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Layout(view,size);var text=inner.Children.Single();var textId=text.Id;
        var textTools=editor.GetInlineTools(text);Bound<CheckBox>(textTools,ToggleButton.IsCheckedProperty,text,nameof(text.IsContentSlot)).SetCurrentValue(ToggleButton.IsCheckedProperty,false);Layout(view,size);var input=Descendants<TextBox>(textTools).Single(control=>AutomationProperties.GetName(control)=="Body text");input.Text="A reusable Body introduction";input.CaretIndex=7;
        Bound<TextBox>(textTools,TextBox.TextProperty,text,nameof(text.FontSize)).Text="29.125";Bound<ComboBox>(textTools,Selector.SelectedItemProperty,text,nameof(text.Alignment)).SetCurrentValue(Selector.SelectedItemProperty,"center");
        Check(receipt,text.Text=="A reusable Body introduction"&&text.FontSize==29.125&&text.Alignment=="center"&&editor.GetElementEditor(box).IsExpanded&&editor.GetElementEditor(inner).IsExpanded,"Adding nested Text exposes its real content, precise font size, and alignment inside expanded parent Boxes.");
        Check(receipt,ReferenceEquals(input,Descendants<TextBox>(editor.GetInlineTools(text)).Single(control=>AutomationProperties.GetName(control)=="Body text"))&&input.CaretIndex==7,"Typing and styling preserve the existing Body text editor and caret.");
        var fontSize=Bound<TextBox>(textTools,TextBox.TextProperty,text,nameof(text.FontSize));fontSize.Text="not-a-number";Layout(view,size);Check(receipt,!wizard.NextCommand.CanExecute(null)&&text.FontSize==29.125,"Invalid native numeric input blocks Next while preserving the last valid Body size.");fontSize.Text="29.125";Layout(view,size);
        scroll.ScrollToVerticalOffset(Math.Min(120,scroll.ScrollableHeight));Layout(view,size);var offset=scroll.VerticalOffset;input.Text="A reusable Body introduction, edited in place";Layout(view,size);Check(receipt,Math.Abs(scroll.VerticalOffset-offset)<.1,"A Body text and preview refresh keep the user's scroll position.");
        scroll.ScrollToVerticalOffset(Math.Max(0,textTools.TransformToAncestor(editor).Transform(new Point()).Y-10));Layout(view,size);Render(view,Path.Combine(fixture,$"body-inline-text-{size.Width:0}.png"),size,receipt);
        var selected=wizard.LibraryObjects.Where(item=>item.Kind=="element").Select(item=>(Choice:item,Source:wizard.GetLibraryObject(item.Source))).First(pair=>pair.Source?.Element?.Fields.Any(field=>field.Type is "text" or "string")==true);
        var first=body.AddLibraryObject(row,selected.Choice.Source,selected.Source!,inner);var second=body.AddLibraryObject(row,selected.Choice.Source,selected.Source!,inner);Layout(view,size);
        Check(receipt,inner.Children.Contains(first)&&inner.Children.Contains(second)&&first.Kind=="template"&&wizard.BuildDefinition().SelectedToolsetIds.Contains(selected.Choice.Id),"Real library Templates insert into the requested nested Body Box and join the portable project toolkit.");
        var firstId=first.Id;var value=first.TemplateValueGroups.SelectMany(group=>group.Fields).First(field=>field.Type is "text" or "string");var other=second.TemplateValueGroups.SelectMany(group=>group.Fields).Single(field=>field.Definition.Id==value.Definition.Id);var inherited=other.Text;
        body.SelectedElement=first;Layout(view,size);var tools=editor.GetInlineTools(first);Bound<CheckBox>(tools,ToggleButton.IsCheckedProperty,first,nameof(first.IsContentSlot)).SetCurrentValue(ToggleButton.IsCheckedProperty,false);Layout(view,size);var property=Descendants<TextBox>(tools).Single(control=>AutomationProperties.GetName(control)=="Template property "+value.Definition.Title);property.Text="Edited through the native Body Template field";Layout(view,size);
        Check(receipt,value.Text==property.Text&&other.Text==inherited&&first.BuildDefinition().TemplateValues["element"][value.Definition.Id].GetString()==property.Text,"Actual inline Template fields write independent typed overrides without changing another instance.");
        var beforeMove=JsonSerializer.Serialize(body.BuildDefinition(),ContentJson.Options);body.MoveElement(second,-1);Named<Button>(editor,"UndoBodyLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        Check(receipt,JsonSerializer.Serialize(body.BuildDefinition(),ContentJson.Options)==beforeMove,"The real Body Undo action restores a layout reorder together with nested geometry and Template overrides.");
        var restoredFirst=Elements(body).Single(element=>element.Id==firstId);body.SelectedElement=restoredFirst;Layout(view,size);tools=editor.GetInlineTools(restoredFirst);scroll.ScrollToVerticalOffset(Math.Max(0,tools.TransformToAncestor(editor).Transform(new Point()).Y-10));Layout(view,size);Render(view,Path.Combine(fixture,$"body-template-fields-{size.Width:0}.png"),size,receipt);
        Check(receipt,OtherRegions()==otherSnapshot,"Nested Body edits, Template insertion, and Undo leave Header, Navigation, Footer, and Extras unchanged.");
        var saved=wizard.BuildDefinition();var restored=new HyperlibWorkWizardViewModel(libraryRoot);restored.LoadDefinition(saved);
        Check(receipt,JsonSerializer.Serialize(saved.LandingTemplate.BodyLayout,ContentJson.Options)==JsonSerializer.Serialize(restored.LandingTemplate.BodyLayout.BuildDefinition(),ContentJson.Options),"Save and reload retain Body rows, recursive Boxes, Template IDs, overrides, and precise values.");
        wizard.NextCommand.Execute(null);Check(receipt,wizard.IsFooterStage,"Next from the Body builder reaches Footer.");wizard.BackCommand.Execute(null);Check(receipt,wizard.IsBodyStage&&Elements(wizard.CurrentTemplate.BodyLayout).Any(element=>element.Id==textId),"Back returns to the existing Body draft without replacing its elements.");
        wizard.Step=5;wizard.NextCommand.Execute(null);wizard.Step=3;Check(receipt,!wizard.CurrentTemplate.BodyLayout.Enabled,"A named Page template starts with an independent Body draft.");wizard.CurrentTemplate.BodyLayout.ApplyPreset(HyperlibBodyLayouts.All[1].Id);
        var named=wizard.BuildDefinition();Check(receipt,named.PageTemplates[0].Definition.BodyLayout.Enabled&&JsonSerializer.Serialize(named.LandingTemplate.BodyLayout,ContentJson.Options)==JsonSerializer.Serialize(saved.LandingTemplate.BodyLayout,ContentJson.Options),"Named templates can use their own Body design without altering the landing page.");
        var headerBeforeClear=JsonSerializer.Serialize(restored.LandingTemplate.HeaderLayout.BuildDefinition(),ContentJson.Options);restored.LandingTemplate.ClearStage("body");Check(receipt,!restored.LandingTemplate.BodyLayout.Enabled&&restored.LandingTemplate.BodyLayout.Blocks.Count==0&&JsonSerializer.Serialize(restored.LandingTemplate.HeaderLayout.BuildDefinition(),ContentJson.Options)==headerBeforeClear,"Clearing Body resets its builder while retaining the separate Header recipe.");
        Layout(view,size);var preview=Named<FrameworkElement>(view,"PreviewPanel");var bounds=preview.TransformToAncestor(view).TransformBounds(new Rect(preview.RenderSize));Check(receipt,bounds.Width>180&&bounds.Left>=0&&bounds.Right<=size.Width+.1&&bounds.Bottom<=size.Height+.1,"The persistent Body preview stays bounded at compact and wide workspace sizes.");
    }
    private static IEnumerable<HyperlibWizardHeaderElementViewModel> Elements(HyperlibWizardHeaderLayoutViewModel layout)
    {IEnumerable<HyperlibWizardHeaderElementViewModel> Visit(IEnumerable<HyperlibWizardHeaderElementViewModel> items){foreach(var item in items){yield return item;foreach(var child in Visit(item.Children))yield return child;}}return layout.Blocks.SelectMany(block=>Visit(block.Elements));}
    private static T Bound<T>(DependencyObject root,DependencyProperty property,object source,string path)where T:FrameworkElement=>Descendants<T>(root).First(control=>BindingOperations.GetBindingExpression(control,property)?.ParentBinding is{} binding&&ReferenceEquals(binding.Source,source)&&binding.Path?.Path==path);
    private static T Named<T>(FrameworkElement root,string name)where T:class=>root.FindName(name)as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Layout(FrameworkElement view,Size size){view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Body native acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null)return;XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public string? Failure{get;set;}}
}
