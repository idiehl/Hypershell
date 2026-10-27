using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using HyperLib.Core;

/// <summary>Exercises the native Block/Box header builder and its persisted editable recipes.</summary>
public static class StudioHeaderStepVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-header-step-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var receipt=new Receipt{Fixture=fixture};
        try
        {
            await RunStaAsync(()=>{LoadResources(Path.GetFullPath(repositoryRoot));foreach(var size in new[]{new Size(1280,940),new Size(640,940)})VerifySize(size,libraryRoot,fixture,receipt);return Task.CompletedTask;});receipt.Passed=true;
        }
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{await File.WriteAllTextAsync(Path.Combine(fixture,"studio-header-step-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_HEADER_STEP_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }
    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        var model=new HyperlibWorkWizardViewModel(libraryRoot);model.InitializeTitle("Header acceptance");model.Step=1;var page=model.CurrentTemplate;page.SetBackgroundColor("#F8F6EE");
        var legacy=model.LibraryObjects.FirstOrDefault(item=>HyperlibWorkWizardViewModel.MatchesStage(item,"header"));if(legacy is not null)page.ApplyObject(legacy.Source,"header");
        var view=new HyperlibWorkWizardView{DataContext=model};Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();
        using var presentation=new HwndSource(new HwndSourceParameters("Hidden header step fixture"){Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080});presentation.RootVisual=view;Layout(view,size);
        var editor=Named<HyperlibHeaderStageEditor>(view,"HeaderEditor");var header=page.HeaderLayout;var outer=Named<ScrollViewer>(view,"ChoicesScroll");
        Check(receipt,editor.Visibility==Visibility.Visible&&Named<Border>(view,"LibraryStageCard").Visibility==Visibility.Collapsed&&Named<HyperlibBackgroundStageEditor>(view,"BackgroundEditor").Visibility==Visibility.Collapsed,
            $"At {size.Width:0}px, Step 2 shows the Block builder instead of the legacy object list or background controls.");
        Check(receipt,!header.Enabled&&header.Blocks.Count==0&&(legacy is null||page.BuildDefinition().HeaderTemplateId==legacy.Id),"Opening Step 2 preserves the existing header until an explicit builder change.");
        var presetCards=Named<Panel>(editor,"HeaderPresetCards").Children.OfType<Button>().ToArray();
        Check(receipt,presetCards.Length>=10&&presetCards.Length==HyperlibHeaderLayouts.All.Count,"At least ten simple header layouts are available below the builder.");
        Render(view,Path.Combine(fixture,$"header-start-{size.Width:0}.png"),size,receipt);
        Named<Button>(editor,"AddHeaderBlockButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(view,size);
        Check(receipt,header.Enabled&&header.Blocks.Count==1&&header.Blocks[0].Height==160&&page.BuildDefinition().HeaderTemplateId is null,
            "The actual + Add Block button creates Block 1 as an editable horizontal row and activates the new layout.");
        var block=header.Blocks[0];var text=header.AddElement(block,"text");
        Check(receipt,text.TextColor=="#17212b","New manual Text begins with dark, readable text on the light Step 1 page.");
        var previewChanges=0;model.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(model.PreviewDefinition))previewChanges++;};var before=previewChanges;text.Text="Field notes";
        Check(receipt,previewChanges-before==1&&model.PreviewDefinition.LandingTemplate.HeaderLayout.Blocks[0].Elements[0].Text=="Field notes",
            "A text edit updates the composed header preview once without a save or duplicate notification cascade.");
        text.FontFamily="Georgia";text.FontSize=34;text.FontWeight=700;text.TextColor="#17212b";
        foreach(var alignment in new[]{"left","center","right"}){text.Alignment=alignment;Check(receipt,model.PreviewDefinition.LandingTemplate.HeaderLayout.Blocks[0].Elements[0].Alignment==alignment,"Text alignment "+alignment+" immediately reaches the preview recipe.");}
        var box=header.AddElement(block,"box");
        Check(receipt,block.ArrangeSideBySide&&block.Arrangement=="horizontal"&&text.WidthPercent>0&&box.WidthPercent>0,
            "New sibling elements use the saved horizontal flow without replacing their responsive width choices.");
        box.WidthPercent=60;box.X=20;box.Y=12;box.Height=120;box.FillColor="#F0E7D2";box.BorderColor="#112233";box.BorderWidth=3;box.BorderStyle="dashed";box.BorderRadius=8;box.Padding=14;
        var nested=header.AddElement(block,"box",box);nested.FillColor="#112233";nested.Height=70;
        var nestedText=header.AddElement(block,"text",nested);nestedText.Text="Nested identity";
        Check(receipt,nestedText.TextColor=="#f2f5f4","Text placed inside a dark nested Box begins with a readable light color.");
        var image=header.AddElement(block,"image",box);image.MediaPath="/media/header-logo.png";image.Alt="Header logo";image.MediaFit="cover";
        var firstGeometry=(text.X,text.Y,text.WidthPercent,box.X,box.Y,box.WidthPercent);var video=header.AddElement(block,"video");video.MediaPath="/media/header-introduction.webm";video.Alt="An introduction";video.Height=80;
        Check(receipt,firstGeometry==(text.X,text.Y,text.WidthPercent,box.X,box.Y,box.WidthPercent),
            "Adding another element preserves all earlier custom X/Y and width decisions.");
        var geometry=block.Elements.ToDictionary(item=>item.Id,item=>(item.X,item.Y,item.WidthPercent));header.MoveElement(video,-1);
        Check(receipt,block.Elements.All(item=>geometry[item.Id]==(item.X,item.Y,item.WidthPercent)),"Reordering custom-positioned elements changes order while preserving their geometry.");
        var second=header.AddBlock();second.Height=72;var secondId=second.Id;header.MoveBlock(second,-1);
        Check(receipt,header.Blocks[0].Id==secondId&&block.Height==160,"Blocks can be added and reordered independently while retaining their heights and stable identities.");
        header.MoveBlock(second,1);block.Height=220;header.SelectedElement=box;Layout(view,size);
        Check(receipt,editor.GetElementEditor(box).IsExpanded&&Descendants<TextBlock>(editor.GetInlineTools(box)).Any(item=>item.Text=="Padding (px)"),
            "Selecting a Box exposes its size, arbitrary position, fill, border, and padding inline inside that Box.");
        var definition=model.BuildDefinition();var saved=definition.LandingTemplate.HeaderLayout;
        Check(receipt,saved.Enabled&&saved.Blocks.Count==2&&HyperlibHeaderLayouts.Elements(saved).Any(item=>item.Kind=="video")
            &&saved.Blocks[0].Elements.Single(item=>item.Id==box.Id).Children.Single(item=>item.Id==nested.Id).Children.Single().Text=="Nested identity",
            "The saved recipe contains recursive Boxes, Text, Image, and Video as editable objects rather than opaque markup.");
        var restored=new HyperlibWorkWizardViewModel(libraryRoot);restored.LoadDefinition(definition);
        Check(receipt,JsonSerializer.Serialize(saved)==JsonSerializer.Serialize(restored.LandingTemplate.HeaderLayout.BuildDefinition())
            &&restored.LandingTemplate.HeaderLayout.SelectedElement is null,"Reopening preserves every row, stable ID, recursive element, style, and media path while resetting only selection.");
        Check(receipt,definition.LandingTemplate.BackgroundColor=="#F8F6EE"&&!definition.PageTemplates[0].Definition.HeaderLayout.Enabled,
            "Header editing preserves Step 1 and does not overwrite another named Page template.");
        var tools=editor.GetInlineTools(box);outer.ScrollToVerticalOffset(Math.Max(0,tools.TransformToAncestor(editor).Transform(new Point()).Y-20));Layout(view,size);
        Render(view,Path.Combine(fixture,$"header-box-tools-{size.Width:0}.png"),size,receipt);
        header.SelectedElement=video;header.RemoveElement(video);Check(receipt,header.SelectedElement is null&&!HyperlibHeaderLayouts.Elements(header.BuildDefinition()).Any(item=>item.Id==video.Id),"Removing a selected element clears the inspector so it cannot edit a detached object.");
        var custom=header.BuildDefinition();
        foreach(var preset in HyperlibHeaderLayouts.All){header.ApplyPreset(preset.Id);Check(receipt,header.Enabled&&header.Blocks.Count>0&&header.ValidationMessage.Length==0,"Preset "+preset.Title+" produces a valid, fully editable Block layout.");}
        header.ApplyPreset("simple-title");Check(receipt,HyperlibHeaderLayouts.Elements(header.BuildDefinition()).Where(item=>item.Kind=="text").All(item=>item.TextColor=="#17212b"),
            "Simple preset Text is readable when applied to a light page.");
        header.Blocks[0].Elements[0].TextColor="#8A3E30";Check(receipt,model.PreviewDefinition.LandingTemplate.HeaderLayout.Blocks[0].Elements[0].TextColor=="#8A3E30",
            "An explicit user text color remains authoritative after the preset's initial contrast choice.");
        header.ApplyPreset("boxed-identity");Check(receipt,HyperlibHeaderLayouts.Elements(header.BuildDefinition()).Where(item=>item.Kind=="text").All(item=>item.TextColor=="#f2f5f4"),
            "Preset Text inside an explicitly dark Box keeps its contrasting light color.");
        header.LoadDefinition(custom);header.ApplyPreset("simple-title");header.Blocks[0].Elements[0].Text="A customized preset";
        Check(receipt,model.PreviewDefinition.LandingTemplate.HeaderLayout.Blocks[0].Elements[0].Text=="A customized preset","A selected preset hydrates ordinary editable elements and immediately previews later edits.");
        header.Undo();Check(receipt,JsonSerializer.Serialize(custom)==JsonSerializer.Serialize(header.BuildDefinition()),"Undo layout action restores the prior custom arrangement after trying a preset.");
        var selected=header.Blocks[0].Elements[0];selected.WidthPercent=0;Check(receipt,!model.NextCommand.CanExecute(null)&&model.CurrentValidationMessage.Contains("width"),"Invalid element width blocks Next using the shared renderer's validation.");selected.WidthPercent=50;
        var beforeStage=JsonSerializer.Serialize(header.BuildDefinition());model.BackCommand.Execute(null);model.NextCommand.Execute(null);Layout(view,size);
        Check(receipt,model.IsHeaderStage&&JsonSerializer.Serialize(header.BuildDefinition())==beforeStage,"Back to Background and forward to Header retain the entire custom layout.");
        outer.ScrollToVerticalOffset(0);Layout(view,size);Render(view,Path.Combine(fixture,$"header-custom-{size.Width:0}.png"),size,receipt);
        var preview=Named<Border>(view,"PreviewPanel");var bounds=preview.TransformToAncestor(view).TransformBounds(new Rect(preview.RenderSize));
        Check(receipt,bounds.Left>=0&&bounds.Right<=size.Width+.1&&bounds.Bottom<=size.Height+.1,"The page builder remains visible and bounded at compact and wide sizes.");
    }
    private static T Named<T>(FrameworkElement root,string name) where T:class=>root.FindName(name) as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Layout(FrameworkElement view,Size size){view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Studio header step acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null){if(!Application.Current.Dispatcher.CheckAccess())throw new InvalidOperationException("Run header verification in its own process.");return;}
        XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;
        var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public string? Failure{get;set;}}
}
