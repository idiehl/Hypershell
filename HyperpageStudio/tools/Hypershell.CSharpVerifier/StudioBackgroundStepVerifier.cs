using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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

/// <summary>Acceptance checks for the focused first background step, without touching live Studio.</summary>
public static class StudioBackgroundStepVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-background-step-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        var receipt=new Receipt{Fixture=fixture};
        try
        {
            await RunStaAsync(()=>
            {
                LoadResources(Path.GetFullPath(repositoryRoot));
                foreach(var size in new[]{new Size(1280,900),new Size(640,900)})VerifySize(size,libraryRoot,fixture,receipt);
                return Task.CompletedTask;
            });receipt.Passed=true;
        }
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{await File.WriteAllTextAsync(Path.Combine(fixture,"studio-background-step-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_BACKGROUND_STEP_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }
    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        var model=new HyperlibWorkWizardViewModel(libraryRoot);model.InitializeTitle("Background acceptance");
        var view=new HyperlibWorkWizardView{DataContext=model};Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();
        using var presentation=new HwndSource(new HwndSourceParameters("Hidden background step fixture"){Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080});presentation.RootVisual=view;Layout(view,size);
        var page=model.CurrentTemplate;var editor=Named<HyperlibBackgroundStageEditor>(view,"BackgroundEditor");
        var picker=Named<HyperlibLiveColorPicker>(editor,"BackgroundColorPicker");var cards=Named<Panel>(editor,"BackgroundDesignCards");var outer=Named<ScrollViewer>(view,"ChoicesScroll");
        var buttons=cards.Children.OfType<Button>().ToArray();
        Check(receipt,editor.Visibility==Visibility.Visible && Named<Border>(view,"LibraryStageCard").Visibility==Visibility.Collapsed,
            $"At {size.Width:0}px, Step 1 replaces the catalogue/search wall with the focused background editor.");
        Check(receipt,buttons.Length==10 && model.BackgroundDesigns.Count==10 && buttons.All(button=>button.Command==model.SelectBackgroundDesignCommand),
            "Exactly ten curated thumbnail cards remain available without a collection or search filter.");
        Check(receipt,buttons.All(button=>Descendants<Image>(button).Any(image=>image.Source is BitmapSource)),
            "All ten native cards load the actual rendered background thumbnails.");
        var pickerTop=picker.TransformToAncestor(editor).Transform(new Point()).Y;
        var cardsTop=cards.TransformToAncestor(editor).Transform(new Point()).Y;
        Check(receipt,pickerTop<cardsTop && picker.ActualHeight>100,
            "The expanded inline color tool occupies the top of the choices before the thumbnail collection.");
        var previewBounds=Named<Border>(view,"PreviewPanel").TransformToAncestor(view).TransformBounds(new Rect(Named<Border>(view,"PreviewPanel").RenderSize));
        Check(receipt,previewBounds.Left>=0 && previewBounds.Right<=size.Width+.1 && previewBounds.Bottom<=size.Height+.1,
            "The persistent page builder remains inside the compact and wide workspace.");
        Render(view,Path.Combine(fixture,$"background-controls-{size.Width:0}.png"),size,receipt);

        var previewChanges=0;model.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(model.PreviewDefinition))previewChanges++;};
        var beforeSingleColorChange=previewChanges;page.Background="#224466";
        Check(receipt,previewChanges-beforeSingleColorChange==1,"A single color edit publishes one composed-preview update, avoiding a derived-property refresh cascade.");
        var channels=Descendants<Slider>(picker).ToArray();Check(receipt,channels.Length==3,"The inline color tool exposes Red, Green, and Blue controls without a modal dialog.");
        channels[0].Value=201;channels[1].Value=37;channels[2].Value=94;
        Check(receipt,page.Background=="#C9255E" && model.PreviewDefinition.LandingTemplate.BackgroundColor=="#C9255E" && previewChanges>0,
            "Changing RGB sliders synchronously updates the page and composed preview recipe without waiting for OK.");
        picker.SetCurrentValue(HyperlibLiveColorPicker.ColorProperty,"#2468AC");
        Check(receipt,page.Background=="#2468AC" && channels[0].Value==36 && channels[1].Value==104 && channels[2].Value==172,
            "Hex edits and RGB channels stay synchronized with the live page color.");
        Check(receipt,model.CollectedCount==0,"Plain color editing does not collect an unrelated library palette.");

        page.PageBorderColor="#B68453";page.PageBorderWidth=7;page.PageBorderStyle="dashed";page.PageBorderRadius=24;
        var first=(HyperlibBackgroundDesign)buttons[0].CommandParameter!;buttons[0].Command!.Execute(first);
        Check(receipt,page.BackgroundDesignId==first.Id && page.Background==first.DefaultColor && model.CollectedCount==0,
            "A curated card applies its actual design and starting color without a toolset import.");
        var second=(HyperlibBackgroundDesign)buttons[1].CommandParameter!;buttons[1].Command!.Execute(second);
        Check(receipt,page.BackgroundDesignId==second.Id && page.Background==second.DefaultColor && page.PageBorderWidth==7 && page.PageBorderColor=="#B68453",
            "Switching designs changes the actual background while retaining the chosen page border.");
        picker.SetCurrentValue(HyperlibLiveColorPicker.ColorProperty,"#234D61");
        Check(receipt,page.BackgroundDesignId==second.Id && page.Background=="#234D61",
            "Changing the color after choosing a design tints that design instead of silently reverting to a solid surface.");

        var imagePath=Path.Combine(fixture,$"background-image-{size.Width:0}.png");WriteImage(imagePath);
        page.SetBackgroundImage(imagePath);page.BackgroundImageFit="contain";
        Check(receipt,page.BackgroundDesignId=="image" && page.HasBackgroundImage && model.IsValid,
            "Choosing a local image activates the supported image mode and remains a valid wizard draft.");
        var imageDefinition=model.BuildDefinition();var restored=new HyperlibWorkWizardViewModel(libraryRoot);restored.LoadDefinition(imageDefinition);
        Check(receipt,restored.IsValid && restored.LandingTemplate.BackgroundDesignId=="image" && restored.LandingTemplate.BackgroundImagePath==imagePath
            && restored.LandingTemplate.BackgroundImageFit=="contain" && restored.LandingTemplate.PageBorderStyle=="dashed" && restored.LandingTemplate.PageBorderRadius==24,
            "Image mode, its path/fit, and all page border settings survive BuildDefinition and LoadDefinition.");
        var owned=ContentJson.Clone(imageDefinition.LandingTemplate.BackgroundSurface);owned.ImagePath="/media/background-acceptance.png";
        var css=HyperlibBackgroundDesigns.SurfaceCss(page.Background,owned);
        Check(receipt,css.Contains("url(\"/media/background-acceptance.png\")",StringComparison.Ordinal) && css.Contains("contain",StringComparison.Ordinal) && css.Contains("7px dashed #B68453",StringComparison.Ordinal),
            "The shared renderer CSS uses the owned image, selected fit, and page border.");
        page.BackgroundImageFit="tile";Check(receipt,model.IsValid && model.BuildDefinition().LandingTemplate.BackgroundSurface.ImageFit=="tile","Repeat image fit remains a valid saved choice.");
        page.SetBackgroundImage("");Check(receipt,page.BackgroundDesignId=="solid" && !page.HasBackgroundImage && page.PageBorderWidth==7,"Removing an image returns to the base color and preserves the page border.");
        buttons[0].Command!.Execute(first);page.UseSolidBackground();Check(receipt,page.BackgroundDesignId=="solid" && page.Background==first.DefaultColor,"The explicit Solid color action removes the design while keeping the current color.");

        var beforeOtherStages=JsonSerializer.Serialize(new{Header=page.HeaderStyle.Build(),Body=page.BodyStyle.Build(),Navigation=page.NavigationStyle.Build(),Footer=page.FooterStyle.Build(),Extras=page.ExtrasStyle.Build()});
        page.PageBorderWidth=-1;Check(receipt,!model.NextCommand.CanExecute(null) && model.CurrentValidationMessage.Contains("border width"),"An invalid border blocks Next with a useful message.");page.PageBorderWidth=7;
        picker.SetCurrentValue(HyperlibLiveColorPicker.ColorProperty,"#invalid");Check(receipt,!model.NextCommand.CanExecute(null),"An unfinished hex color cannot be saved as a background.");picker.SetCurrentValue(HyperlibLiveColorPicker.ColorProperty,"#234D61");
        var recipeBefore=model.BuildDefinition().LandingTemplate;
        model.NextCommand.Execute(null);Layout(view,size);
        Check(receipt,model.IsHeaderStage && editor.Visibility==Visibility.Collapsed && Named<HyperlibHeaderStageEditor>(view,"HeaderEditor").Visibility==Visibility.Visible,
            "Next enters the Header stage without changing the background recipe.");
        model.BackCommand.Execute(null);Layout(view,size);
        Check(receipt,model.IsBackgroundStage && JsonSerializer.Serialize(model.BuildDefinition().LandingTemplate)==JsonSerializer.Serialize(recipeBefore),
            "Returning from Header restores the background settings without reconstructing or losing the recipe.");
        Check(receipt,beforeOtherStages==JsonSerializer.Serialize(new{Header=page.HeaderStyle.Build(),Body=page.BodyStyle.Build(),Navigation=page.NavigationStyle.Build(),Footer=page.FooterStyle.Build(),Extras=page.ExtrasStyle.Build()}),
            "Background edits do not modify the other five stages' region styles.");

        // A restored old palette remains untouched until an explicit background choice.
        if(model.LibraryObjects.FirstOrDefault(item=>item.Kind=="palette") is {} legacyPalette)
        {
            var legacy=model.BuildDefinition();legacy.LandingTemplate.BackgroundTemplateId=legacyPalette.Id;legacy.SelectedToolsetIds.Add(legacyPalette.Id);
            var legacyModel=new HyperlibWorkWizardViewModel(libraryRoot);legacyModel.LoadDefinition(legacy);
            Check(receipt,legacyModel.BuildDefinition().LandingTemplate.BackgroundTemplateId==legacyPalette.Id,
                "Opening an existing source palette recipe preserves its original identity.");
            legacyModel.LandingTemplate.SetBackgroundColor("#347658");
            Check(receipt,legacyModel.BuildDefinition().LandingTemplate.BackgroundTemplateId is null,
                "An explicit new color choice releases the legacy palette recipe without deleting the retained project collection.");
        }

        outer.ScrollToVerticalOffset(Math.Min(outer.ScrollableHeight/2,300));Layout(view,size);
        var offset=outer.VerticalOffset;var source=model.BackgroundDesigns;
        var last=buttons[^1];last.BringIntoView();last.Command!.Execute(last.CommandParameter);Layout(view,size);
        Check(receipt,Math.Abs(outer.VerticalOffset-offset)<.1 && ReferenceEquals(source,model.BackgroundDesigns),
            "Clicking or previewing a background card keeps the choices at the user's current scroll position.");
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.MouseWheelEvent};outer.RaiseEvent(wheel);Layout(view,size);
        Check(receipt,wheel.Handled && outer.VerticalOffset>offset,"Intentional mouse-wheel scrolling remains available in the background controls.");
        Render(view,Path.Combine(fixture,$"background-designs-{size.Width:0}.png"),size,receipt);
    }
    private static void WriteImage(string path)
    {
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{45,80,150,255,60,100,180,255,80,130,200,255,30,65,120,255},8);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    private static T Named<T>(FrameworkElement root,string name) where T:class=>root.FindName(name) as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Layout(FrameworkElement view,Size size){view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Studio background step acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null){if(!Application.Current.Dispatcher.CheckAccess())throw new InvalidOperationException("Run background verification in its own process.");return;}
        XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;
        var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public string? Failure{get;set;}}
}
