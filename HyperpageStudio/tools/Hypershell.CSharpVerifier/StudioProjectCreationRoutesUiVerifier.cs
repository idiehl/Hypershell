using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

/// <summary>Native single-action creation, retained template services, and font-independent Studio toolbar.</summary>
public static class StudioProjectCreationRoutesUiVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot,string? libraryRoot=null)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-project-routes-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var receipt=new Receipt{Fixture=fixture};
        try{await RunStaAsync(async()=>{LoadResources(repositoryRoot);foreach(var size in new[]{new Size(640,900),new Size(1280,940)})VerifySize(size,libraryRoot,fixture,receipt);await VerifyDirectCreationAsync(libraryRoot,fixture,receipt);});receipt.Passed=true;}
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{await File.WriteAllTextAsync(Path.Combine(fixture,"studio-project-routes-ui-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_PROJECT_ROUTES_UI_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }
    private static void VerifySize(Size size,string? libraryRoot,string fixture,Receipt receipt)
    {
        using var model=new StudioProjectStartTabViewModel(_=>Task.FromResult(true),libraryRoot,fixture);model.ProjectName="A new site";
        var view=new StudioProjectStartView(model);Named<HyperlibGuidedPagePreview>(Named<HyperlibWorkWizardView>(view,"WorkWizard"),"PageBuilderPreview").Dispose();var gallery=Named<StudioProjectTemplateGalleryView>(view,"TemplateGallery");gallery.PreviewEnabled=false;
        using var host=new HwndSource(new HwndSourceParameters("Hidden project creation routes fixture"){Width=(int)size.Width,Height=(int)size.Height,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080});host.RootVisual=view;Layout(view,size);
        Check(receipt,model.IsScratchSelected&&!model.IsTemplateSelected&&model.ShowFoundationChoices&&model.ShowCreate
            &&model.ContinueLabel=="Create project"&&model.CreateCommand.CanExecute(null),"The single Create action opens an editable project without a start-method choice or forced wizard.");
        Named<ScrollViewer>(view,"StartContentScrollViewer").ScrollToBottom();Layout(view,size);
        Check(receipt,view.FindName("TemplateStartCard") is null && view.FindName("ScratchStartCard") is null && view.FindName("StartMethodsPanel") is null && view.FindName("BeginWorkWizardButton") is null,"The launch surface has no template-versus-scratch choice or separate gallery entry.");
        var launchCreate=Named<Button>(view,"CreateProjectButton");var launchBounds=Bounds(launchCreate,view);Check(receipt,launchCreate.IsVisible&&launchCreate.IsEnabled&&ReferenceEquals(launchCreate.Command,model.CreateCommand)&&launchBounds.Left>=0&&launchBounds.Right<=size.Width+.1&&launchBounds.Top>=0&&launchBounds.Bottom<=size.Height,"The single Create project action remains reachable at "+size.Width+"px.");
        Render(view,Path.Combine(fixture,$"creation-launch-{size.Width:0}.png"),size,receipt);
        foreach(var choose in new[]{model.ChooseHyperlibCommand,model.ChooseRazorCommand,model.ChooseHtmlCommand})
        {
            choose.Execute(null);model.ChooseScratchCommand.Execute(null);Layout(view,size);
            Check(receipt,!model.IsWizardVisible&&!model.IsTemplateGalleryVisible&&Named<HyperlibWorkWizardView>(view,"WorkWizard").Visibility==Visibility.Collapsed,"Scratch leaves the step-by-step builder out of the launch flow for "+model.SelectedKind+".");
            Check(receipt,model.CreateCommand.CanExecute(null)&&model.ContinueCommand.CanExecute(null)
                &&Named<Button>(view,"CreateProjectButton").Visibility==Visibility.Visible,"Each project type uses the same direct Create action without foundation review.");
        }
        model.Wizard.LandingTemplate.Background="#203040";model.Wizard.Step=3;var scratch=JsonSerializer.Serialize(model.Wizard.PreviewDefinition,ContentJson.Options);
        model.ChooseHyperlibCommand.Execute(null);model.ChooseTemplateCommand.Execute(null);model.ContinueCommand.Execute(null);Layout(view,size);
        Check(receipt,model.IsTemplateGalleryVisible&&!model.IsWizardVisible&&!model.ShowFoundationChoices&&gallery.IsVisible&&model.HasUnsavedChanges,"The retained gallery API can still open its bounded surface and preserve layout drafts without a launch-screen entry.");
        var all=model.Gallery.Templates;var originals=new VisualPageTemplateService(new VisualPageComponentCatalog()).BuiltInTemplates.Where(item=>item.Scope=="page").ToArray();
        Check(receipt,originals.All(item=>all.Any(choice=>choice.Id=="visual:"+item.TemplateId))&&all.Any(item=>item.Id.StartsWith("hyperlib:",StringComparison.Ordinal)),"The gallery includes original complete visual designs and complete Hyperlib templates rather than individual objects.");receipt.CatalogCount=all.Count;
        var selected=all.First(item=>item.SupportedKinds.Contains(StudioProjectKind.Hyperlib));model.Gallery.Selected=selected;Layout(view,size);
        Check(receipt,model.CreateCommand.CanExecute(null)&&model.Gallery.BuildDefinition().PipelineVersion==2&&model.Gallery.BuildDefinition().PageTemplates.Count>0,"Selecting a compatible complete template prepares a reusable foundation and enables direct creation without visiting the wizard.");
        Check(receipt,JsonSerializer.Serialize(model.Gallery.BuildDefinition().LandingTemplate,ContentJson.Options)==JsonSerializer.Serialize(model.Gallery.PreviewDefinition!.LandingTemplate,ContentJson.Options),"Gallery preview and creation consume the same prepared actual landing recipe.");
        var restored=new HyperlibWorkWizardViewModel(libraryRoot);restored.LoadDefinition(model.Gallery.BuildDefinition());Check(receipt,restored.LandingTemplate.BuildDefinition().CompleteTemplateObjectId==model.Gallery.BuildDefinition().LandingTemplate.CompleteTemplateObjectId&&restored.LandingTemplate.BuildDefinition().CompleteTemplateAppearanceId==model.Gallery.BuildDefinition().LandingTemplate.CompleteTemplateAppearanceId,"Opening and saving a complete-template foundation preserves its original-design identities.");
        var search=Named<TextBox>(gallery,"CompleteTemplateSearch");search.Text=selected.Title;Layout(view,size);Check(receipt,model.Gallery.FilteredTemplates.Any(item=>item.Id==selected.Id)&&model.Gallery.FilteredTemplates.All(item=>$"{item.Title} {item.Description} {item.Category} {item.Collection} {item.Id}".Contains(selected.Title,StringComparison.OrdinalIgnoreCase)),"The native gallery search filters full template metadata.");
        search.Text="no-template-matches-"+Guid.NewGuid().ToString("N");Layout(view,size);Check(receipt,!model.Gallery.HasMatches&&model.Gallery.Selected is null&&!model.CreateCommand.CanExecute(null)&&model.Gallery.PreviewDefinition is null,"An empty search clears the chosen template and prevents stale-template creation.");search.Text="";
        var collection=Named<ComboBox>(gallery,"CompleteTemplateCollection");collection.SetCurrentValue(Selector.SelectedItemProperty,selected.Collection);Layout(view,size);Check(receipt,model.Gallery.FilteredTemplates.All(item=>item.Collection==selected.Collection),"The collection filter narrows complete templates without changing their source.");collection.SetCurrentValue(Selector.SelectedItemProperty,"All collections");
        var category=Named<ComboBox>(gallery,"CompleteTemplateCategory");category.SetCurrentValue(Selector.SelectedItemProperty,selected.Category);Layout(view,size);Check(receipt,model.Gallery.FilteredTemplates.All(item=>item.Category==selected.Category),"Category filtering composes with collection and search.");category.SetCurrentValue(Selector.SelectedItemProperty,"All categories");Layout(view,size);
        var list=Named<ListBox>(gallery,"CompleteTemplateList");var source=list.ItemsSource;var listScroll=Descendants<ScrollViewer>(list).First();listScroll.ScrollToVerticalOffset(Math.Min(7,listScroll.ScrollableHeight));Layout(view,size);var offset=listScroll.VerticalOffset;var visible=list.Items.Cast<object>().Select(item=>list.ItemContainerGenerator.ContainerFromItem(item)).OfType<ListBoxItem>().LastOrDefault(item=>item.IsVisible);
        if(visible is not null){visible.SetCurrentValue(ListBoxItem.IsSelectedProperty,true);visible.BringIntoView();Layout(view,size);Check(receipt,Math.Abs(offset-listScroll.VerticalOffset)<.1&&ReferenceEquals(source,list.ItemsSource),"Selecting and previewing a gallery card keeps the list source and scroll position.");}
        listScroll.ScrollToVerticalOffset(0);Layout(view,size);Check(receipt,listScroll.VerticalOffset<.1,"Explicit gallery scrollbar scrolling remains available.");
        model.Gallery.Selected=selected;Layout(view,size);var preview=Bounds(Named<Border>(gallery,"CompleteTemplatePreview"),view);var create=Bounds(Named<Button>(view,"CreateProjectButton"),view);Check(receipt,preview.Width>180&&preview.Right<=size.Width+.1&&preview.Bottom<=size.Height+.1&&create.Right<=size.Width&&create.Bottom<=size.Height,"The actual template preview and fixed Create action remain bounded at "+size.Width+"px.");Render(view,Path.Combine(fixture,$"complete-template-gallery-{size.Width:0}.png"),size,receipt);
        model.ChangeProjectTypeCommand.Execute(null);model.ChooseScratchCommand.Execute(null);Layout(view,size);Check(receipt,!model.IsWizardVisible&&model.Wizard.Step==3&&JsonSerializer.Serialize(model.Wizard.PreviewDefinition,ContentJson.Options)==scratch,"Returning to scratch keeps previous layout drafts intact without forcing their wizard open.");model.ChooseTemplateCommand.Execute(null);model.ContinueCommand.Execute(null);Check(receipt,model.Gallery.Selected?.Id==selected.Id,"Returning to template creation retains the chosen complete design.");
        foreach(var kind in new[]{StudioProjectKind.Razor,StudioProjectKind.Html}){model.Gallery.ProjectKind=kind;model.Gallery.Selected=all.First(item=>item.SupportedKinds.Contains(kind));Check(receipt,model.Gallery.CanCreate,"Complete-template creation is available for "+kind+".");}
        using var incompatible=new StudioProjectStartTabViewModel(_=>Task.FromResult(true),libraryRoot,fixture);incompatible.ProjectName="Compatibility check";incompatible.ChooseHtmlCommand.Execute(null);incompatible.BeginWizard();incompatible.Wizard.Step=7;Check(receipt,incompatible.CreateCommand.CanExecute(null),"A primitive scratch foundation is compatible with HTML creation.");var serverObject=incompatible.Wizard.LibraryObjects.First(item=>item.Kind=="element"&&item.Source.ComponentName=="CalloutBlock");var serverBlock=incompatible.Wizard.LandingTemplate.HeaderLayout.AddBlock();incompatible.Wizard.LandingTemplate.HeaderLayout.AddLibraryObject(serverBlock,serverObject.Source,incompatible.Wizard.GetLibraryObject(serverObject.Source)!);Check(receipt,!incompatible.CreateCommand.CanExecute(null)&&incompatible.CompatibilityMessage.Contains(serverObject.Title,StringComparison.Ordinal),"An unsupported server tool is named before generation and disables web creation.");
        gallery.DisposePreview();
    }
    private static async Task VerifyDirectCreationAsync(string? libraryRoot,string fixture,Receipt receipt)
    {
        var catalogue=new VisualPageComponentCatalog();
        var projects=new VisualPageProjectService(new VisualPageSchemaValidator(catalogue),new VisualPageGenerator(catalogue,new VisualPageSchemaValidator(catalogue)),catalogue);
        foreach(var kind in new[]{StudioProjectKind.Hyperlib,StudioProjectKind.Razor,StudioProjectKind.Html})
        {
            var opened=new List<string>();var wizardWasShown=false;var busyWasShown=false;
            using var model=new StudioProjectStartTabViewModel(path=>{opened.Add(path);return Task.FromResult(kind!=StudioProjectKind.Html||opened.Count>1);},libraryRoot,fixture);
            model.ProjectName="Direct "+kind;
            (kind==StudioProjectKind.Hyperlib?model.ChooseHyperlibCommand:kind==StudioProjectKind.Razor?model.ChooseRazorCommand:model.ChooseHtmlCommand).Execute(null);
            model.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(model.IsWizardVisible))wizardWasShown|=model.IsWizardVisible;if(args.PropertyName==nameof(model.IsBusy))busyWasShown|=model.IsBusy;};
            await model.CreateAsync();
            var root=model.GeneratedProjectPath??throw new InvalidOperationException(model.ErrorMessage);
            Check(receipt,opened.Count==1&&opened[0]==root&&busyWasShown&&!wizardWasShown&&!model.IsWizardVisible&&!model.IsTemplateGalleryVisible,
                kind+" creates its actual project and opens its editor directly without displaying a wizard or gallery.");
            var session=await projects.OpenAsync(root);
            Check(receipt,session.Project.Name==model.ProjectName&&Directory.Exists(root)&&File.Exists(Path.Combine(root,"README.md")),
                kind+" preserves the chosen project title, location, and project instructions.");
            if(kind==StudioProjectKind.Hyperlib)
            {
                var pipeline=await new HyperlibProjectPipelineService().ReadAsync(root)??throw new InvalidOperationException("Missing direct editing foundation.");
                var content=await new ContentStore(Path.GetFullPath(Path.Combine(root,"../.."))).ReadAsync();
                var template=JsonSerializer.Deserialize<TemplateDefinition>(await File.ReadAllTextAsync(Path.Combine(root,"authoring.template.json")),ContentJson.Options)!;
                Check(receipt,content.Pages.Count==1&&content.Pages[pipeline.LandingPageId].Blocks.Count==0
                    &&pipeline.LandingTemplate.Blocks.Count==0&&pipeline.PageTemplate.Blocks.Count==0,
                    "The direct Hyperlib canvas starts empty, with no example content, generated header, or navigation.");
                Check(receipt,template.Blocks.Any(block=>block.Component.Contains("Text",StringComparison.Ordinal))
                    &&template.Blocks.Any(block=>block.Component.Contains("Image",StringComparison.Ordinal))
                    &&pipeline.PageTemplates.Count>0,
                    "The empty Work retains real text and media elements plus an independent reusable Page foundation.");
                await VerifyToolbarAsync(session,projects,catalogue,fixture,receipt);
            }
            else
            {
                Check(receipt,string.IsNullOrWhiteSpace(session.Project.HyperlibAuthoringPath)
                    &&session.Project.Pages.All(page=>page.FileType==(kind==StudioProjectKind.Razor?HyperpagePageFileType.Cshtml:HyperpagePageFileType.ProjectDefault)),
                    kind+" retains its native browser project model and editable source page type.");
                Check(receipt,session.Page.Root.Children.All(element=>string.IsNullOrWhiteSpace(element.Text))
                    &&session.Project.Templates.Count>0&&projects.InspectGeneratedDrift(session).Count==0,
                    kind+" starts without sample page copy and retains reusable local layouts without managed-source drift.");
            }
            if(kind==StudioProjectKind.Html)
            {
                var manifest=Path.Combine(root,VisualPageProjectService.ProjectManifestFileName);var hash=SHA256.HashData(await File.ReadAllBytesAsync(manifest));
                Check(receipt,model.HasError&&!model.IsEditable&&model.ShowCreate&&model.CreateCommand.CanExecute(null),
                    "If direct creation succeeds but opening fails, the new project remains available for an opening retry.");
                await model.CreateAsync();
                Check(receipt,opened.Count==2&&opened.All(path=>path==root)&&!model.HasError
                    &&SHA256.HashData(await File.ReadAllBytesAsync(manifest)).SequenceEqual(hash),
                    "Direct-creation retry opens the same existing project without recreating or overwriting it.");
            }
            else Check(receipt,!model.HasError&&!model.IsBusy&&!model.IsEditable,
                kind+" completes direct creation and restores its busy state after the project opens.");
        }
    }
    private static async Task VerifyToolbarAsync(VisualPageProjectSession session, VisualPageProjectService projects,
        VisualPageComponentCatalog catalogue, string fixture, Receipt receipt)
    {
        var validator = new VisualPageSchemaValidator(catalogue);
        var generator = new VisualPageGenerator(catalogue, validator);
        using var preview = new VisualPagePreviewBuildService(new BoundedProcessRunner(), generator, Path.Combine(fixture, "toolbar-preview-cache"));
        using var localHost = new VisualPageLocalHostService(new BoundedProcessRunner());
        using var model = await VisualPageStudioTabViewModel.CreateAsync(session, projects,
            new VisualPageMutationEngine(validator), generator, validator, catalogue,
            new VisualPageTemplateService(catalogue, Path.Combine(fixture, "toolbar-personal-templates")),
            new VisualPageDesignCanvasRenderer(catalogue), preview, localHost, new VisualPageUserSourceService(),
            new AtlasArchivePublicationService(new BoundedProcessRunner(), projects, generator, validator, Path.Combine(fixture, "toolbar-publications")));
        using var actualView = new VisualPageStudioView(model);
        var toolbar = Named<WrapPanel>(actualView, "HyperlibToolbar");
        // Render the actual compiled control's toolbar, without loading the surrounding browser panes.
        // The original buttons, templates, bindings, and Path visuals are preserved unchanged.
        ((Panel)VisualTreeHelper.GetParent(toolbar)).Children.Remove(toolbar);
        toolbar.DataContext = model;
        var surface = new Grid { Background = (Brush)actualView.FindResource("AppBackgroundBrush") };
        surface.Children.Add(toolbar);
        using var host = new HwndSource(new HwndSourceParameters("Hidden Studio vector toolbar fixture")
        {
            Width = 1280, Height = 100, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
        });
        host.RootVisual = surface;
        try
        {
            foreach (var width in new[] { 640, 1280 })
            {
                var size = new Size(width, 100);
                Layout(surface, size);
                Check(receipt, !IsWindowVisible(host.Handle) && toolbar.IsVisible && toolbar.Children.Count == 4,
                    "The actual compiled four-button toolbar renders in a permanently hidden native host at " + width + "px.");
                Render(surface, Path.Combine(fixture, $"studio-toolbar-{width}.png"), size, receipt);
                var hashes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var button in toolbar.Children.OfType<Button>())
                {
                    if (button.Content is System.Windows.Shapes.Path sampledPath)
                        receipt.ToolbarIcons.Add(new ToolbarIconSample(width, button.ToolTip?.ToString() ?? "", sampledPath.ActualWidth,
                            sampledPath.ActualHeight, sampledPath.Data?.Bounds.ToString() ?? "", sampledPath.Stroke?.ToString() ?? ""));
                    Check(receipt, button.Content is System.Windows.Shapes.Path path && path.Data is not null
                        && !path.Data.Bounds.IsEmpty && path.Data.Bounds.Width > 10 && path.Data.Bounds.Height > 10
                        && path.Stroke is SolidColorBrush brush && brush.Color.A > 0
                        // Stretch=Uniform keeps each geometry's own aspect ratio inside the 18px constraint.
                        // Its arranged render size need not be a square; painted pixels below prove visibility.
                        && path.ActualWidth >= 14 && path.ActualHeight >= 14 && path.ActualWidth <= 18.1 && path.ActualHeight <= 18.1,
                        button.ToolTip + " uses a visible vector icon without a font glyph dependency.");
                    var icon = (System.Windows.Shapes.Path)button.Content;
                    var bounds = Bounds(button, surface);
                    Check(receipt, bounds.Left >= 0 && bounds.Right <= width && bounds.Top >= 0 && bounds.Bottom <= size.Height,
                        button.ToolTip + " stays within the compact and wide toolbar bounds.");
                    var pixels = new byte[24 * 24 * 4];
                    var bitmap = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(icon);
                    bitmap.CopyPixels(pixels, 24 * 4, 0);
                    Check(receipt, Enumerable.Range(0, 24 * 24).Count(index => pixels[index * 4 + 3] > 20) > 30,
                        button.ToolTip + " paints its actual line work into the native render.");
                    hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
                }
                Check(receipt, hashes.Count == 4, "The four toolbar actions have distinct rendered icon shapes.");
            }
            Check(receipt, ReferenceEquals(Named<Button>(actualView, "HyperlibSaveButton").Command, model.SaveCommand)
                && toolbar.Children.OfType<Button>().Select(button => button.ToolTip?.ToString()).SequenceEqual(new[]
                { "Reusable page layouts", "Page editor", "Publish to Hyperlib", "Save project and Page content" }),
                "The vector replacement retains the four original action tooltips and bound Save command.");
        }
        finally
        {
            host.RootVisual = null;
            surface.Children.Clear();
        }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    private static T Named<T>(FrameworkElement root,string name)where T:class=>root.FindName(name)as T??throw new InvalidOperationException("Missing "+name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static Rect Bounds(FrameworkElement element,Visual root)=>element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Layout(FrameworkElement view,Size size){view.Width=size.Width;view.Height=size.Height;view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){Layout(view,size);var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Project creation routes native acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null)return;XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    private sealed record ToolbarIconSample(int WorkspaceWidth, string Action, double RenderWidth, double RenderHeight, string GeometryBounds, string Stroke);
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public int CatalogCount{get;set;}public List<string> Assertions{get;}=[];public List<string> Renders{get;}=[];public List<ToolbarIconSample> ToolbarIcons{get;}=[];public string? Failure{get;set;}}
}
