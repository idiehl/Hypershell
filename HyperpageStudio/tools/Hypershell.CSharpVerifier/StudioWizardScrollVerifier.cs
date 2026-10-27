using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Hypershell;
using Hypershell.ViewModels;

/// <summary>Exercises the real nested wizard list without opening a window or starting a preview host.</summary>
public static class StudioWizardScrollVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-wizard-scroll-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var receipt=new Receipt{Fixture=fixture};
        try
        {
            await RunStaAsync(()=>
            {
                LoadResources(Path.GetFullPath(repositoryRoot));
                foreach(var size in new[]{new Size(1280,620),new Size(640,760)}) VerifySize(size,libraryRoot,fixture,receipt);
                return Task.CompletedTask;
            });
            receipt.Passed=true;
        }
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture,"studio-wizard-scroll-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"STUDIO_WIZARD_SCROLL_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }

    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        var model=new HyperlibWorkWizardViewModel(libraryRoot);model.InitializeTitle("Scroll regression");model.Step=5;
        var view=new HyperlibWorkWizardView{DataContext=model};
        // A presentation source makes FrameworkElement.BringIntoView follow its
        // normal WPF route. The owned HWND is hidden and cannot activate; dispose
        // the preview first so Loaded cannot initialize WebView or a local host.
        Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();
        using var presentation=new HwndSource(new HwndSourceParameters("Hidden wizard scroll fixture")
        {
            Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,
            WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080
        });
        presentation.RootVisual=view;
        Layout(view,size);
        var list=Named<ListBox>(view,"StageObjectChoices");
        var outer=Named<ScrollViewer>(view,"ChoicesScroll");
        list.ApplyTemplate();Layout(view,size);
        var inner=Descendants<ScrollViewer>(list).FirstOrDefault()??throw new InvalidOperationException("The real design list has no scroll viewer.");
        Check(receipt,list.Items.Count>8 && inner.ScrollableHeight>0,$"The {size.Width:0}px fixture has a real overflowing Extras element list.");
        Check(receipt,outer.ScrollableHeight>4,$"The {size.Width:0}px fixture also overflows the outer choices surface.");

        // Logical virtualization is intentional: the bottom realized row is clipped by the
        // viewport just as it is when a user clicks the partially visible bottom card.
        inner.ScrollToVerticalOffset(Math.Max(1,inner.ScrollableHeight/2));
        outer.ScrollToVerticalOffset(Math.Min(32,outer.ScrollableHeight/2));
        Layout(view,size);
        var partial=FindPartialCard(list,inner);
        if(partial is null)
        {
            // Different system font metrics can fit the normal 250px list exactly. A
            // slightly shorter viewport preserves the production item template while
            // ensuring that this regression actually exercises a clipped bottom card.
            foreach(var height in new[]{233d,219d,207d,197d})
            {
                list.Height=height;Layout(view,size);
                inner.ScrollToVerticalOffset(Math.Max(1,inner.ScrollableHeight/2));Layout(view,size);
                partial=FindPartialCard(list,inner);if(partial is not null)break;
            }
        }
        var card=partial??throw new InvalidOperationException("Could not prepare a partially visible bottom design card.");
        Check(receipt,card.Bounds.Top>=0 && card.Bounds.Top<card.ViewportHeight && card.Bounds.Bottom>card.ViewportHeight+0.5,
            $"The {size.Width:0}px selected card is partially clipped at the bottom of the actual list viewport.");
        var button=Descendants<Button>(card.Container).FirstOrDefault()??throw new InvalidOperationException("The design row has no selection button.");
        var choice=button.CommandParameter as HyperlibWizardLibraryObject??throw new InvalidOperationException("The actual card does not carry a library object command parameter.");
        Check(receipt,button.Command?.CanExecute(choice)==true,"The partially visible design card retains its live selection command.");

        var source=list.ItemsSource;
        var before=Capture(inner,outer);
        Render(view,Path.Combine(fixture,$"wizard-scroll-before-{size.Width:0}.png"),size,receipt);
        var requestCount=0;var handledCount=0;
        RequestBringIntoViewEventHandler observe=(_,args)=>{requestCount++;if(args.Handled)handledCount++;};
        list.AddHandler(FrameworkElement.RequestBringIntoViewEvent,observe,true);
        try
        {
            // Both requests are raised by normal WPF focus/selection paths. Calling
            // BringIntoView uses their real routed event and ScrollViewer behavior.
            card.Container.BringIntoView();button.BringIntoView();
            Layout(view,size);
            Check(receipt,requestCount>=2 && handledCount==requestCount,
                "Focus/selection bring-into-view requests are consumed inside design rows before reaching either scroll viewer.");
            CheckStable(receipt,before,Capture(inner,outer),"Requesting the clipped card's full bounds leaves both scroll offsets unchanged.");

            list.SelectedItem=choice;
            button.Command!.Execute(choice);
            Layout(view,size);
            Check(receipt,ReferenceEquals(source,list.ItemsSource),
                "Applying a design keeps the same filtered ItemsSource instead of resetting the list enumeration.");
            CheckStable(receipt,before,Capture(inner,outer),"Selecting and applying the bottom card keeps the list and the outer choices surface in place.");
            Check(receipt,model.LandingTemplate.BuildDefinition().RepeatingObjects.Any(item=>item.TemplateObjectId==choice.Id&&item.Region=="sidebar") && choice.IsSelected,
                "The stable card click still applies the chosen Extras object to the preview recipe.");

            var requestedAccent=model.LandingTemplate.Accent.Equals("#D65A4A",StringComparison.OrdinalIgnoreCase)?"#326B5C":"#D65A4A";
            model.LandingTemplate.Accent=requestedAccent;
            Layout(view,size);
            Check(receipt,ReferenceEquals(source,list.ItemsSource) && model.PreviewDefinition.LandingTemplate.AccentColor==requestedAccent,
                "Styling refreshes preserve list source identity while updating the composed preview definition.");
            CheckStable(receipt,before,Capture(inner,outer),"A preview/style update does not reset either scroll position.");
        }
        finally {list.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent,observe);}
        Render(view,Path.Combine(fixture,$"wizard-scroll-selected-{size.Width:0}.png"),size,receipt);

        // Remove only the production row visibility-request guard as a control.
        // The same clipped-row request must now move an actual ScrollViewer;
        // otherwise a disconnected/ineffective test could incorrectly pass.
        var productionStyle=list.ItemContainerStyle;
        var unguardedStyle=new Style(typeof(ListBoxItem),productionStyle.BasedOn);
        foreach(var setter in productionStyle.Setters.OfType<Setter>())
            unguardedStyle.Setters.Add(new Setter(setter.Property,setter.Value));
        list.ItemContainerStyle=unguardedStyle;Layout(view,size);
        inner.ScrollToVerticalOffset(before.Inner);outer.ScrollToVerticalOffset(before.Outer);Layout(view,size);
        var unguardedCard=FindPartialCard(list,inner)??throw new InvalidOperationException("The baseline control has no clipped row.");
        var beforeBaseline=Capture(inner,outer);
        unguardedCard.Container.BringIntoView();Layout(view,size);
        var afterBaseline=Capture(inner,outer);
        Check(receipt,!Near(beforeBaseline.Inner,afterBaseline.Inner)||!Near(beforeBaseline.Outer,afterBaseline.Outer),
            $"Removing the row guard reproduces the focus scroll jump: inner {beforeBaseline.Inner:0.###}→{afterBaseline.Inner:0.###}; outer {beforeBaseline.Outer:0.###}→{afterBaseline.Outer:0.###}.");
        list.ItemContainerStyle=productionStyle;Layout(view,size);
        inner.ScrollToVerticalOffset(before.Inner);outer.ScrollToVerticalOffset(before.Outer);Layout(view,size);

        // The guard applies only to automatic focus visibility requests. Real scroll
        // input continues through the production ScrollViewer and ScrollBar handlers.
        var beforeWheel=Capture(inner,outer);
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.MouseWheelEvent};
        inner.RaiseEvent(wheel);Layout(view,size);
        Check(receipt,wheel.Handled && inner.VerticalOffset>beforeWheel.Inner,
            "Mouse wheel input still moves the design list after focus auto-scroll is suppressed.");
        Check(receipt,Near(outer.VerticalOffset,beforeWheel.Outer),"Wheel input consumed by the inner list leaves the outer choices position alone.");

        var bar=Descendants<ScrollBar>(inner).FirstOrDefault(item=>item.Orientation==Orientation.Vertical)
            ??throw new InvalidOperationException("The design list has no vertical scrollbar.");
        var beforeBar=inner.VerticalOffset;
        var scrollCommand=beforeBar>0?ScrollBar.PageUpCommand:ScrollBar.PageDownCommand;
        Check(receipt,scrollCommand.CanExecute(null,bar),"The real list scrollbar still exposes an enabled page scrolling command.");
        scrollCommand.Execute(null,bar);Layout(view,size);
        Check(receipt,!Near(inner.VerticalOffset,beforeBar),"Using the scrollbar's page command still moves the list.");

        var outerBeforeWheel=outer.VerticalOffset;
        var outerWheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.MouseWheelEvent};
        outer.RaiseEvent(outerWheel);Layout(view,size);
        Check(receipt,outerWheel.Handled && outer.VerticalOffset>outerBeforeWheel,
            "Mouse wheel input aimed at the outer settings surface also remains functional.");

        var previousSource=list.ItemsSource;
        model.NextCommand.Execute(null);Layout(view,size);
        Check(receipt,model.IsBackgroundStage && !model.IsLandingStep && !ReferenceEquals(previousSource,list.ItemsSource) && Near(outer.VerticalOffset,0),
            "An actual wizard stage transition intentionally changes the choices and starts the new settings surface at its top.");
        receipt.Offsets.Add(new(){Width=size.Width,InnerBefore=before.Inner,OuterBefore=before.Outer,WheelAfter=beforeWheel.Inner,SelectedId=choice.Id});
        Named<HyperlibGuidedPagePreview>(view,"PageBuilderPreview").Dispose();
    }

    private static PartialCard? FindPartialCard(ListBox list,ScrollViewer scroll)
    {
        var viewport=Descendants<ScrollContentPresenter>(scroll).FirstOrDefault();if(viewport is null)return null;
        for(var index=0;index<list.Items.Count;index++)
        {
            if(list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container || container.ActualHeight<=0)continue;
            var bounds=container.TransformToAncestor(viewport).TransformBounds(new Rect(0,0,container.ActualWidth,container.ActualHeight));
            if(bounds.Top>=0 && bounds.Top<viewport.ActualHeight-2 && bounds.Bottom>viewport.ActualHeight+0.5)
                return new(container,bounds,viewport.ActualHeight);
        }
        return null;
    }
    private static ScrollPosition Capture(ScrollViewer inner,ScrollViewer outer)=>new(inner.VerticalOffset,outer.VerticalOffset);
    private static bool Near(double first,double second)=>Math.Abs(first-second)<0.1;
    private static void CheckStable(Receipt receipt,ScrollPosition expected,ScrollPosition actual,string message)
        =>Check(receipt,Near(expected.Inner,actual.Inner)&&Near(expected.Outer,actual.Outer),$"{message} Inner {expected.Inner:0.###}→{actual.Inner:0.###}; outer {expected.Outer:0.###}→{actual.Outer:0.###}.");
    private static T Named<T>(FrameworkElement root,string name) where T:class=>root.FindName(name) as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private static void Check(Receipt receipt,bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);
    }
    private static void Layout(FrameworkElement view,Size size)
    {
        view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();
        view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();
    }
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt)
    {
        Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);
    }
    private static Task RunStaAsync(Func<Task> work)
    {
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async()=>{try{await work();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});
            Dispatcher.Run();
        }){IsBackground=true,Name="Studio wizard scroll regression"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null)
        {
            if(!Application.Current.Dispatcher.CheckAccess())throw new InvalidOperationException("Run wizard scroll verification in its own process.");return;
        }
        XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;
        var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),
            source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))
            if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())
            if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed record PartialCard(ListBoxItem Container,Rect Bounds,double ViewportHeight);
    private sealed record ScrollPosition(double Inner,double Outer);
    private sealed class Receipt
    {
        public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public List<OffsetReceipt> Offsets{get;}=[];public string? Failure{get;set;}
    }
    private sealed class OffsetReceipt
    {
        public double Width{get;set;}public double InnerBefore{get;set;}public double OuterBefore{get;set;}public double WheelAfter{get;set;}public string SelectedId{get;set;}="";
    }
}
