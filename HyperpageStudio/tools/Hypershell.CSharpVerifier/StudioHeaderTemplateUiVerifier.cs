using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using HyperLib.Core;
using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;

public static class StudioHeaderTemplateUiVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-header-template-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var receipt=new Receipt{Fixture=fixture};
        try{await RunStaAsync(()=>{LoadResources(repositoryRoot);foreach(var size in new[]{new Size(1280,940),new Size(640,940)})VerifySize(size,libraryRoot,fixture,receipt);VerifyTypedFields(receipt);return Task.CompletedTask;});receipt.Passed=true;}
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{await File.WriteAllTextAsync(Path.Combine(fixture,"studio-header-template-ui-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_HEADER_TEMPLATE_UI_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }
    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        var wizard=new HyperlibWorkWizardViewModel(libraryRoot);wizard.InitializeTitle("Library header");wizard.Step=1;
        var browser=new HyperlibHeaderTemplateLibraryViewModel(wizard);
        receipt.CatalogCounts.Add(browser.Objects.Count);
        Check(receipt,browser.Objects.Count==wizard.LibraryObjects.Count&&browser.Objects.Count>10,"The Template browser includes the complete catalog rather than the ten header presets.");
        Check(receipt,browser.Objects.Any(item=>!HyperlibWorkWizardViewModel.MatchesStage(item,"header"))&&browser.Groups.Count==wizard.Groups.Count&&browser.Categories.Count==wizard.Categories.Count,"Non-header objects and every saved collection/category remain navigable.");
        var selected=wizard.LibraryObjects.Where(item=>item.Kind=="element"&&!HyperlibWorkWizardViewModel.MatchesStage(item,"header"))
            .Select(item=>(Choice:item,Source:wizard.GetLibraryObject(item.Source))).First(pair=>pair.Source?.Element?.Fields.Any(field=>field.Type is "text" or "string")==true);
        browser.Search=selected.Choice.Id;Check(receipt,browser.Objects.Count==1&&browser.Selected?.Id==selected.Choice.Id,"Search finds a non-header library object by its exact canonical ID.");
        browser.Search=TemplateObjectLibrary.CatalogIdFor(selected.Choice.Source.GroupId,selected.Choice.Source.ObjectId);Check(receipt,browser.Objects.Any(item=>item.Id==selected.Choice.Id),"The familiar public catalog ID remains searchable.");
        browser.Search="";browser.Group=selected.Choice.GroupTitle;browser.Category=selected.Choice.Category;
        Check(receipt,browser.Objects.All(item=>item.GroupTitle==selected.Choice.GroupTitle&&item.Category==selected.Choice.Category),"Collection and category filters combine without restricting browsing to header objects.");
        browser.Group="All collections";browser.Category="All objects";browser.Selected=selected.Choice;
        var view=new HyperlibWorkWizardView{DataContext=wizard};Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();using var host=Present(view,size);var header=wizard.CurrentTemplate.HeaderLayout;var row=header.AddBlock();var box=header.AddElement(row,"box");box.X=13;box.Y=8;box.WidthPercent=65;
        var editor=Named<HyperlibHeaderStageEditor>(view,"HeaderEditor");Layout(view,size);var add=Descendants<Button>(editor).First(button=>Equals(button.Content,"+ Add Element"));var inside=Descendants<Button>(editor).First(button=>Equals(button.Content,"+ Inside Box"));
        var rootMenu=editor.CreateAddMenu(add,row,null);var nestedMenu=editor.CreateAddMenu(inside,row,box);
        Check(receipt,rootMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Template…"))&&nestedMenu.Items.OfType<MenuItem>().Any(item=>Equals(item.Header,"Template…")),"Both actual + Add Element and + Inside Box menus expose Template.");
        rootMenu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"Text")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(receipt,row.Elements.Any(item=>item.Kind=="text")&&box.X==13&&box.Y==8&&box.WidthPercent==65,"The existing Text action still works and preserves the customized Box geometry.");
        var first=header.AddLibraryObject(row,selected.Choice.Source,selected.Source!,box);var second=header.AddLibraryObject(row,selected.Choice.Source,selected.Source!,box);
        Check(receipt,first.Kind=="template"&&first.TemplateObjectId==selected.Choice.Id&&box.Children.Contains(first)&&box.Children.Contains(second),"A non-header Template inserts at the requested nested Box location with its actual canonical identity.");
        Check(receipt,first.BuildDefinition().TemplateValues.Count==0&&second.BuildDefinition().TemplateValues.Count==0,"Untouched source defaults are inherited instead of copying media references into overrides.");
        Check(receipt,wizard.BuildDefinition().SelectedToolsetIds.Contains(selected.Choice.Id),"Nested library objects automatically join the project's portable toolkit.");
        var value=first.TemplateValueGroups.SelectMany(group=>group.Fields).First(field=>field.Type is "text" or "string");var other=second.TemplateValueGroups.SelectMany(group=>group.Fields).Single(field=>field.Definition.Id==value.Definition.Id);var original=other.Text;var changes=0;
        wizard.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(wizard.PreviewDefinition))changes++;};var before=changes;value.Text="A custom reusable identity";
        Check(receipt,changes-before==1&&other.Text==original&&first.BuildDefinition().TemplateValues["element"][value.Definition.Id].GetString()==value.Text,"A field edit updates preview once and changes only this Template instance.");
        header.SelectedElement=first;Layout(view,size);var tools=editor.GetInlineTools(first);
        Check(receipt,editor.GetElementEditor(first).IsExpanded&&Descendants<HyperlibTemplateValuesEditor>(tools).Any(),"Selecting an inserted object exposes native editors for its actual schema fields inline inside its own card.");
        var input=Descendants<TextBox>(tools).First(item=>AutomationProperties.GetName(item)=="Template property "+value.Definition.Title);input.Text="Changed through the native field";Layout(view,size);
        Check(receipt,value.Text=="Changed through the native field"&&wizard.PreviewDefinition.LandingTemplate.HeaderLayout.Blocks[0].Elements.Single(item=>item.Id==box.Id).Children.Single(item=>item.Id==first.Id).TemplateValues["element"][value.Definition.Id].GetString()==value.Text,"Typing in the actual native property editor changes the composed preview definition immediately.");
        var saved=wizard.BuildDefinition();var restored=new HyperlibWorkWizardViewModel(libraryRoot);restored.LoadDefinition(saved);
        Check(receipt,JsonSerializer.Serialize(saved.LandingTemplate.HeaderLayout)==JsonSerializer.Serialize(restored.LandingTemplate.HeaderLayout.BuildDefinition()),"Save/load retains recursive Template instances, independent overrides, IDs, and custom geometry.");
        value.Reset();Check(receipt,!first.BuildDefinition().TemplateValues.ContainsKey("element")&&value.Text==original,"Reset removes the override and restores the source default without persisting its asset path.");
        var layoutChoice=wizard.LibraryObjects.First(item=>item.Kind=="layout");var layoutSource=wizard.GetLibraryObject(layoutChoice.Source)!;var importedLayout=header.AddLibraryObject(row,layoutChoice.Source,layoutSource);
        Check(receipt,importedLayout.TemplateValueGroups.Count==layoutSource.Blocks.Count&&importedLayout.TemplateValueGroups.SelectMany(group=>group.Fields).Count()==layoutSource.Blocks.Sum(block=>layoutSource.Definition.Blocks.Single(schema=>schema.Id==block.Type).Fields.Count),"A complete library layout exposes the actual properties of each original source block.");
        var palette=wizard.LibraryObjects.First(item=>item.Kind=="palette");browser.Selected=palette;Check(receipt,browser.ActionLabel=="Apply appearance"&&browser.Purpose.Contains("new Box"),"Nonvisual styles explain their explicit new-Box action.");
        var styled=header.AddLibraryObject(row,palette.Source,wizard.GetLibraryObject(palette.Source)!,box);Check(receipt,styled.Kind=="box"&&styled.BuildDefinition().TemplateAppearance.Count>0&&box.Children.Contains(styled),"A palette creates an editable styled Box inside the requested Box rather than an invisible Template.");
        var fillField=styled.TemplateAppearanceFields.First(field=>field.Definition.Id is "background" or "paper" or "surface");fillField.Text="#234567";Check(receipt,styled.FillColor=="#234567","Changing a palette Box's exposed background field also changes its visible native fill.");styled.FillColor="#345678";Check(receipt,styled.BuildDefinition().TemplateAppearance[fillField.Definition.Id].GetString()=="#345678"&&fillField.Text=="#345678","A later manual Box fill remains authoritative in its scoped template appearance and matching exposed field.");
        var region=wizard.LibraryObjects.First(item=>item.Kind=="region");browser.Selected=region;var container=header.AddLibraryObject(row,region.Source,wizard.GetLibraryObject(region.Source)!);
        Check(receipt,browser.ActionLabel=="Insert Box"&&container.Kind=="box"&&container.BorderWidth>0&&container.Height>0,"A region creates a visible editable container with space for later elements.");
        var outer=Named<ScrollViewer>(view,"ChoicesScroll");header.SelectedElement=first;Layout(view,size);tools=editor.GetInlineTools(first);outer.ScrollToVerticalOffset(Math.Max(0,tools.TransformToAncestor(editor).Transform(new Point()).Y-20));Layout(view,size);Render(view,Path.Combine(fixture,$"template-properties-{size.Width:0}.png"),size,receipt);
        using(var picker=new PickerScope(new HyperlibHeaderTemplatePicker(wizard,row,box){PreviewEnabled=false}))
        {
            picker.Window.DisposePreview();picker.Window.Model.Selected=selected.Choice;var content=(FrameworkElement)picker.Window.Content;picker.Window.Content=null;content.DataContext=picker.Window.Model;
            var renderRoot=new Grid{Background=picker.Window.Background};renderRoot.SetValue(Control.ForegroundProperty,picker.Window.Foreground);renderRoot.Children.Add(content);using var pickerHost=Present(renderRoot,size);Render(renderRoot,Path.Combine(fixture,$"template-library-{size.Width:0}.png"),size,receipt);
            Check(receipt,Descendants<ListBox>(content).Any(list=>list.Items.Count>10)&&Descendants<ComboBox>(content).Count()>=2,"The actual library dialog renders its full list, search, collection, category, details, and preview at compact and wide widths.");
            foreach(var button in Descendants<Button>(content).Where(button=>Equals(button.Content,"Cancel")||AutomationProperties.GetName(button)=="Insert selected library object"))
            {
                var bounds=button.TransformToAncestor(renderRoot).TransformBounds(new Rect(button.RenderSize));
                Check(receipt,button.Visibility==Visibility.Visible&&bounds.Width>50&&bounds.Height>20&&bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=size.Width+.1&&bounds.Bottom<=size.Height+.1,"The "+button.Content+" footer button remains visible and fully bounded at "+size.Width+"px.");
            }
            VerifyPickerScroll(renderRoot,size,receipt);
        }
    }
    private static void VerifyPickerScroll(FrameworkElement root,Size size,Receipt receipt)
    {
        var list=Descendants<ListBox>(root).Single();var scroll=Descendants<ScrollViewer>(list).First();Check(receipt,scroll.ScrollableHeight>10,"The real library list overflows and can reproduce bottom-row focus scrolling.");
        scroll.ScrollToVerticalOffset(scroll.ScrollableHeight/2);Layout(root,size);var partial=PartialLibraryRow(list,scroll);
        if(partial is null){list.Height=Math.Max(150,list.ActualHeight-17);Layout(root,size);scroll.ScrollToVerticalOffset(scroll.ScrollableHeight/2);Layout(root,size);partial=PartialLibraryRow(list,scroll);}
        var card=partial??throw new InvalidOperationException("No partially visible bottom library row was prepared.");var before=scroll.VerticalOffset;var source=list.ItemsSource;var count=0;var handled=0;
        RequestBringIntoViewEventHandler observe=(_,args)=>{count++;if(args.Handled)handled++;};list.AddHandler(FrameworkElement.RequestBringIntoViewEvent,observe,true);
        card.BringIntoView();list.SelectedItem=list.ItemContainerGenerator.ItemFromContainer(card);Layout(root,size);list.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent,observe);
        Check(receipt,count>0&&count==handled&&Math.Abs(scroll.VerticalOffset-before)<.1&&ReferenceEquals(source,list.ItemsSource),"Selecting the partially visible library row consumes its visibility request, keeps the scroll offset, and preserves the filtered list source.");
        var guarded=list.ItemContainerStyle;var baseline=new Style(typeof(ListBoxItem),guarded.BasedOn);foreach(var setter in guarded.Setters.OfType<Setter>())baseline.Setters.Add(new Setter(setter.Property,setter.Value));list.ItemContainerStyle=baseline;Layout(root,size);scroll.ScrollToVerticalOffset(before);Layout(root,size);
        var unguarded=PartialLibraryRow(list,scroll)??throw new InvalidOperationException("The baseline has no partially visible library row.");unguarded.BringIntoView();Layout(root,size);Check(receipt,Math.Abs(scroll.VerticalOffset-before)>.1,"Removing only the library row guard reproduces the automatic focus scroll jump.");
        list.ItemContainerStyle=guarded;Layout(root,size);scroll.ScrollToVerticalOffset(before);Layout(root,size);var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.MouseWheelEvent};scroll.RaiseEvent(wheel);Layout(root,size);
        Check(receipt,wheel.Handled&&scroll.VerticalOffset>before,"Mouse wheel scrolling still moves the library after automatic selection scrolling is suppressed.");
        var bar=Descendants<ScrollBar>(scroll).First(item=>item.Orientation==Orientation.Vertical);var beforeBar=scroll.VerticalOffset;ScrollBar.PageUpCommand.Execute(null,bar);Layout(root,size);Check(receipt,Math.Abs(scroll.VerticalOffset-beforeBar)>.1,"The library scrollbar's explicit page command remains operational.");
    }
    private static ListBoxItem? PartialLibraryRow(ListBox list,ScrollViewer scroll)
    {
        var viewport=Descendants<ScrollContentPresenter>(scroll).First();for(var index=0;index<list.Items.Count;index++)if(list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row&&row.ActualHeight>0)
        {var bounds=row.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));if(bounds.Top>=0&&bounds.Top<viewport.ActualHeight-2&&bounds.Bottom>viewport.ActualHeight+.5)return row;}return null;
    }
    private static void VerifyTypedFields(Receipt receipt)
    {
        var definitions=new List<FieldDefinition>{new(){Id="text",Title="Text",Type="text",DefaultValue=ContentJson.Value("Inherited text")},new(){Id="number",Title="Size",Type="number",Min=2,Max=40,DefaultValue=ContentJson.Value(12d)},new(){Id="enabled",Title="Enabled",Type="boolean",DefaultValue=ContentJson.Value(true)},new(){Id="color",Title="Color",Type="color",DefaultValue=ContentJson.Value("#123456")},new(){Id="alignment",Title="Alignment",Type="select",Options=["left","right"],DefaultValue=ContentJson.Value("left")},new(){Id="media",Title="Media",Type="media",DefaultValue=ContentJson.Value("/media/original.svg")},new(){Id="link",Title="Link",Type="url",DefaultValue=ContentJson.Value("https://example.com/")}};
        var schema=new BlockDefinition{Id="sample",Title="Custom saved object",Component="Sample",Fields=definitions};var source=new TemplateLibraryObject{Id="element-sample",GroupId="custom",Title=schema.Title,Description="User saved fields",Category="Custom",Kind="element",CatalogId="HLC-SAMPLE",Element=schema,Definition=new(){TemplateId="custom",Blocks=[schema],Regions=[new(){Id="main",Title="Main"}],Appearance=[new(){Id="customLogo",Title="Custom appearance media",Type="media",DefaultValue=ContentJson.Value("/media/logo.svg")},new(){Id="accent",Title="Accent",Type="color",DefaultValue=ContentJson.Value("#123456")} ]}};
        var owner=new HyperlibWizardHeaderLayoutViewModel();var row=owner.AddBlock();var item=owner.AddLibraryObject(row,new("custom:element-sample",schema.Title,"","Custom","element","custom","element-sample","Saved collection","Sample"),source);var fields=item.TemplateValueGroups.Single().Fields.ToDictionary(field=>field.Definition.Id);
        Check(receipt,item.TemplateAppearanceFields.Count==1&&item.TemplateAppearanceFields[0].Definition.Id=="accent"&&fields.ContainsKey("media"),"The optional appearance panel exposes supported scoped colors/fonts only, while object-level media remains fully editable.");
        fields["number"].Text="18";fields["number"].Text="not a number";Check(receipt,owner.ValidationMessage.Length>0&&item.BuildDefinition().TemplateValues["element"]["number"].GetDouble()==18,"Invalid numeric drafts preserve the last valid typed JSON value and block the wizard's header validation.");fields["number"].Text="1";Check(receipt,fields["number"].Error.Length>0,"Native field validation respects custom source minimum and maximum values.");fields["number"].Text="24";
        fields["color"].Text="bad color";Check(receipt,fields["color"].Error.Length>0&&!item.BuildDefinition().TemplateValues["element"].ContainsKey("color"),"Invalid colors do not replace inherited defaults with unusable preview values.");fields["color"].Text="#AA4433";fields["enabled"].BooleanValue=false;fields["alignment"].Text="right";
        Check(receipt,item.BuildDefinition().TemplateValues["element"]["enabled"].ValueKind==JsonValueKind.False&&item.BuildDefinition().TemplateValues["element"]["alignment"].GetString()=="right","Boolean and select editors retain typed, allowed source values.");
        fields["media"].Text=@"C:\fixtures\custom-logo.svg";fields["link"].Text="javascript:alert(1)";Check(receipt,fields["media"].Error.Length==0&&fields["link"].Error.Length>0,"Media fields accept a local file for owned import while unsafe link drafts remain blocked.");fields["link"].Reset();fields["number"].Reset();Check(receipt,owner.ValidationMessage.Length==0&&!item.BuildDefinition().TemplateValues["element"].ContainsKey("number"),"Reset clears invalid text and explicit overrides without disturbing other custom values.");
    }
    private static HwndSource Present(FrameworkElement view,Size size){var host=new HwndSource(new HwndSourceParameters("Hidden Template library acceptance"){Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080});host.RootVisual=view;Layout(view,size);return host;}
    private static T Named<T>(FrameworkElement root,string name)where T:class=>root.FindName(name)as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Layout(FrameworkElement view,Size size){view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Studio Template library acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null)return;XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;
        var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed class PickerScope(HyperlibHeaderTemplatePicker window):IDisposable{public HyperlibHeaderTemplatePicker Window{get;}=window;public void Dispose()=>Window.Close();}
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<int> CatalogCounts{get;}=[];public List<string> Renders{get;}=[];public string? Failure{get;set;}}
}
