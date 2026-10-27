using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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

/// <summary>Exercises the native project start and composition wizard without opening a desktop window.</summary>
public static class StudioProjectStartVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string? libraryRoot = null)
    {
        var root = Path.Combine(Path.GetFullPath(outputDirectory), "studio-project-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var receipt = new Receipt { Fixture = root };
        try
        {
            await RunStaAsync(async () =>
            {
                LoadResources(Path.GetFullPath(repositoryRoot));
                using (var defaultLocationModel = new StudioProjectStartTabViewModel(_ => Task.FromResult(true), libraryRoot))
                {
                    Check(receipt, defaultLocationModel.ParentDirectory == Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hyperpage Studio", "Projects")
                        && defaultLocationModel.ParentDirectory == VisualPageProjectService.DefaultProjectsDirectory,
                        "The project start screen and legacy project dialogs share the installed Program Files Projects default.");
                }
                using var model = new StudioProjectStartTabViewModel(_ => Task.FromResult(true), libraryRoot, root);
                Check(receipt, model.ParentDirectory == root,
                    "An explicitly selected project location still overrides the installed default.");
                var view = new StudioProjectStartView(model);
                var wizardView = Named<HyperlibWorkWizardView>(view, "WorkWizard");
                foreach (var size in new[] { new Size(640, 820), new Size(1280, 900) })
                {
                    Layout(view, size);
                    Check(receipt, wizardView.Visibility == Visibility.Collapsed,
                        "The start tab shows its three project choices before the Hyperlib wizard begins.");
                    var cards = new[] { Named<Button>(view, "HyperlibProjectCard"), Named<Button>(view, "RazorProjectCard"), Named<Button>(view, "HtmlProjectCard") };
                    var bounds = cards.Select(card => Bounds(card, view)).ToArray();
                    Check(receipt, cards.All(card => card.Visibility == Visibility.Visible && card.ActualWidth > 120)
                        && bounds.All(rect => rect.Left >= 0 && rect.Right <= size.Width)
                        && bounds[0].Right <= bounds[1].Left && bounds[1].Right <= bounds[2].Left,
                        $"All three project cards remain distinct and reachable at {size.Width:0}px.");
                    var open = Bounds(Named<Button>(view, "OpenProjectButton"), view);
                    var begin = Bounds(Named<Button>(view, "CreateProjectButton"), view);
                    Check(receipt, open.Right <= size.Width && begin.Right <= size.Width && begin.Bottom <= size.Height
                        && Named<Button>(view, "CreateProjectButton").IsEnabled,
                        "Opening an existing project and beginning a Work remain reachable in the compact and wide start surfaces.");
                    Render(view, Path.Combine(root, $"project-start-{size.Width:0}.png"), size, receipt);
                }

                model.ChooseRazorCommand.Execute(null);
                Layout(view, new Size(640, 820));
                Check(receipt, model.SelectedKind == StudioProjectKind.Razor && Named<Button>(view, "CreateProjectButton").Visibility == Visibility.Visible
                    && Named<Button>(view, "CreateProjectButton").Visibility == Visibility.Visible && model.ContinueCommand.CanExecute(null),
                    "Razor exposes the same single Create project action before constructing its foundation.");
                model.ChooseHtmlCommand.Execute(null);
                Check(receipt, model.SelectedKind == StudioProjectKind.Html && model.ContinueCommand.CanExecute(null)&&model.CreateCommand.CanExecute(null), "HTML exposes a direct editable project without a separate template route on launch.");
                model.ProjectName = "CON";
                Check(receipt, model.HasValidationError && !model.ContinueCommand.CanExecute(null)&&!model.CreateCommand.CanExecute(null), "A reserved Windows project name is rejected before creation.");
                model.ProjectName = "../outside";
                Check(receipt, model.HasValidationError && !model.ContinueCommand.CanExecute(null)&&!model.CreateCommand.CanExecute(null), "Path separators cannot escape the selected parent folder.");
                Directory.CreateDirectory(Path.Combine(root, "Existing"));
                model.ProjectName = "Existing";
                Check(receipt, model.HasValidationError && !model.ContinueCommand.CanExecute(null)&&!model.CreateCommand.CanExecute(null), "An existing project folder is protected from replacement.");
                model.ProjectName = "My portable Work";
                Check(receipt, model.StudioDisplayTitle == "My portable Work" && model.StudioProjectKindLabel.Length == 0,
                    "The upper tab uses only the chosen project name while the project is being built.");
                model.ChooseHyperlibCommand.Execute(null);
                model.BeginWizardCommand.Execute(null);
                Layout(view, new Size(640, 820));
                var wizard = model.Wizard;
                Check(receipt, Named<FrameworkElement>(view, "ProjectStartHeading").Visibility == Visibility.Collapsed,
                    "Starting the wizard removes the entire start-screen heading and Open project band.");
                Check(receipt, model.IsWizardVisible && wizardView.Visibility == Visibility.Visible && wizard.IsBackgroundStage && wizard.IsLandingStep
                    && wizard.WorkTitle == "My portable Work", "A Hyperlib project starts by building the landing background, without a catalogue shopping wall.");
                Check(receipt, wizard.LibraryObjects.Count > 15 && wizard.Groups.Count > 1,
                    "The guided choices use the actual Core and saved template catalogue.");
                Check(receipt, wizard.CollectedCount == 0 && wizard.NextCommand.CanExecute(null) && wizard.PageTemplates.Count == 1,
                    "Custom built-in styling can continue without an arbitrary mandatory object or finished Page count.");
                Check(receipt, wizard.FilteredObjects.Any() && wizard.FilteredObjects.All(item => HyperlibWorkWizardViewModel.MatchesStage(item, "background")
                    && item.Kind is not ("element" or "preset")),
                    "Background choices contain surfaces and palettes, never arbitrary text, media, or header blocks.");
                var background = wizard.FilteredObjects.First();
                wizard.SelectObjectCommand.Execute(background);
                Check(receipt, wizard.CollectedCount == 1 && wizard.LandingTemplate.BuildDefinition().BackgroundTemplateId == background.Id,
                    "Choosing a background automatically collects its library recipe for the project.");
                var catalogueService=new HyperlibWorkProjectService(libraryRoot);
                string? PaletteBackground(HyperlibWizardLibraryObject item)=>catalogueService.GetCatalogObject(item.Source.GroupId,item.Source.ObjectId).Appearance
                    .FirstOrDefault(field=>field.Id is "background" or "pageBackground" or "pageBg")?.DefaultValue?.GetString();
                var firstColor=PaletteBackground(background);
                var anotherPalette=wizard.FilteredObjects.FirstOrDefault(item=>item.Id!=background.Id
                    && PaletteBackground(item) is {Length:>0} color && !color.Equals(firstColor,StringComparison.OrdinalIgnoreCase));
                if(anotherPalette is not null)
                {
                    wizard.SelectObjectCommand.Execute(anotherPalette);
                    Check(receipt,wizard.LandingTemplate.Background.Equals(PaletteBackground(anotherPalette),StringComparison.OrdinalIgnoreCase)
                        && wizard.CollectedCount==1 && !background.IsSelected,
                        "Selecting another surface adopts its real background color and releases the replaced unused recipe.");
                    wizard.SelectObjectCommand.Execute(background);
                }
                wizard.Search = "no match exists for this phrase";
                Check(receipt, !wizard.FilteredObjects.Any() && wizard.CollectedCount == 1, "Search changes do not remove previously chosen designs.");
                wizard.Search = "";
                wizard.LandingTemplate.Background = "#invalid";
                Check(receipt, !wizard.NextCommand.CanExecute(null) && wizard.CurrentValidationMessage.Contains("background"),
                    "An invalid background blocks progression with a specific message.");
                wizard.LandingTemplate.Background = "#F4F2EC"; wizard.LandingTemplate.Accent = "#326B5C";
                foreach (var size in new[] { new Size(640, 920), new Size(1280, 900) })
                {
                    Render(view, Path.Combine(root, $"guided-background-{size.Width:0}.png"), size, receipt);
                    var preview = Bounds(Named<FrameworkElement>(wizardView, "PreviewPanel"), view);
                    Check(receipt, preview.Width > 180 && preview.Height > 160 && preview.Left >= 0 && preview.Right <= size.Width && preview.Bottom <= size.Height,
                        $"The persistent page builder stays within the {size.Width:0}px workspace.");
                }
                wizard.NextCommand.Execute(null);
                Check(receipt, wizard.IsHeaderStage && wizard.FilteredObjects.All(item => HyperlibWorkWizardViewModel.MatchesStage(item, "header")),
                    "The next step offers header designs only.");
                Layout(view, new Size(640, 920));
                Check(receipt, Named<TextBlock>(wizardView, "StepNumberLabel").Text == "Step 2 of 6"
                    && Named<TextBlock>(wizardView, "StepTitleLabel").Text == "Header"
                    && Named<FrameworkElement>(wizardView, "StepDescriptionLabel").Visibility == Visibility.Collapsed
                    && Named<TextBlock>(wizardView, "StageBreadcrumbLabel").Text == "Background  ›  Header",
                    "The Header stage has concise copy and a breadcrumb showing only completed and current stages.");
                var header = wizard.FilteredObjects.FirstOrDefault();
                if (header is not null) wizard.SelectObjectCommand.Execute(header);
                wizard.LandingTemplate.HeaderStyle.Text = "Shared site identity";
                wizard.LandingTemplate.HeaderStyle.BackgroundColor = "#DCE8E0";
                wizard.LandingTemplate.HeaderStyle.Padding = 24;
                wizard.LandingTemplate.HeaderStyle.FontFamily = "Georgia";
                wizard.LandingTemplate.HeaderStyle.BorderWidth = 2;
                wizard.LandingTemplate.HeaderStyle.BorderColor = "#326B5C";
                foreach (var size in new[] { new Size(640, 920), new Size(1280, 900) })
                    Render(view, Path.Combine(root, $"guided-header-{size.Width:0}.png"), size, receipt);
                wizard.NextCommand.Execute(null);
                Check(receipt,wizard.IsNavigationStage&&wizard.Step==2&&wizard.StepTitle=="Navigation"&&wizard.NextLabel=="Next: Body"
                    &&!wizard.ShowLibraryStage&&Named<FrameworkElement>(wizardView,"NavigationEditor").Visibility==Visibility.Visible,
                    "Navigation follows Header with its own editable Block builder instead of a catalogue wall.");
                wizard.LandingTemplate.NavigationLayout.ApplyPreset(HyperlibNavigationLayouts.All[0].Id);
                wizard.LandingTemplate.NavigationPlacement="left";
                wizard.NextCommand.Execute(null);
                Check(receipt, wizard.IsBodyStage && !wizard.ShowLibraryStage&&Named<FrameworkElement>(wizardView,"BodyEditor").Visibility==Visibility.Visible,
                    "The Body stage offers its own Block builder and editable presets instead of a second catalogue surface.");
                wizard.LandingTemplate.BodyLayout.ApplyPreset(HyperlibBodyLayouts.All[0].Id);
                var slotId = wizard.LandingTemplate.BodyLayout.Blocks.First().Id;
                Check(receipt, wizard.LandingTemplate.BodyLayout.Enabled && slotId.Length > 0,
                    "Body presets create independently identified editable Blocks.");
                wizard.LandingTemplate.ContentWidth = "wide"; wizard.LandingTemplate.ContentAlignment = "center";
                foreach (var size in new[] { new Size(640, 920), new Size(1280, 900) })
                    Render(view, Path.Combine(root, $"guided-body-{size.Width:0}.png"), size, receipt);
                wizard.NextCommand.Execute(null);
                Check(receipt, wizard.IsFooterStage && !wizard.ShowLibraryStage&&Named<FrameworkElement>(wizardView,"FooterEditor").Visibility==Visibility.Visible,
                    "Footer has its own reusable Block builder and Page navigation tools.");
                wizard.LandingTemplate.FooterLayout.ApplyPreset(HyperlibFooterLayouts.All[0].Id);
                wizard.LandingTemplate.FooterStyle.Text = "Shared footer";
                wizard.NextCommand.Execute(null);
                Check(receipt, wizard.IsExtrasStage && wizard.FilteredObjects.All(item => HyperlibWorkWizardViewModel.MatchesStage(item, "extras")),
                    "Optional panels, special containers, and tools are the final page-building step.");
                if (wizard.FilteredObjects.FirstOrDefault() is { } extra) wizard.SelectObjectCommand.Execute(extra);
                wizard.BackCommand.Execute(null); wizard.BackCommand.Execute(null); wizard.BackCommand.Execute(null);
                Check(receipt, wizard.IsNavigationStage && wizard.LandingTemplate.NavigationPlacement == "left"
                    && wizard.LandingTemplate.HeaderStyle.Text == "Shared site identity"
                    && wizard.LandingTemplate.BodyLayout.Blocks.Any(item => item.Id == slotId),
                    "Back navigation preserves styling, reusable chrome, and stable object placements.");
                wizard.NextCommand.Execute(null); wizard.NextCommand.Execute(null); wizard.NextCommand.Execute(null); wizard.NextCommand.Execute(null);
                Check(receipt, !wizard.IsLandingStep && wizard.IsBackgroundStage && !wizard.IsTemplatePromptStep,
                    "Completing the landing page immediately starts the first reusable Page template.");
                wizard.CurrentTemplate.Name = "Article";
                wizard.CurrentTemplate.HeaderStyle.Text = "Article identity";
                var articleId = wizard.CurrentTemplate.Id;
                for (var index=0; index<6; index++) wizard.NextCommand.Execute(null);
                Check(receipt, wizard.IsTemplatePromptStep && wizard.StepTitle.Contains("another Page template")
                    && !wizard.ShowNext && !model.CreateCommand.CanExecute(null),
                    "After the first complete Page template, an explicit add-another question precedes authoring.");
                foreach (var size in new[] { new Size(640, 920), new Size(1280, 900) })
                    Render(view, Path.Combine(root, $"guided-add-another-{size.Width:0}.png"), size, receipt);
                wizard.AddPageTemplateCommand.Execute(null);
                wizard.CurrentTemplate.Name = "Gallery"; wizard.CurrentTemplate.Background = "#19202C";
                wizard.CurrentTemplate.ContentWidth = "full";
                var galleryId = wizard.CurrentTemplate.Id;
                Check(receipt, wizard.PageTemplates.Count == 2 && wizard.IsBackgroundStage && galleryId != articleId,
                    "Adding another template starts an independent named six-stage draft with a stable identity.");
                wizard.BackCommand.Execute(null);
                Check(receipt, wizard.CurrentTemplate.Id == articleId && wizard.IsExtrasStage,
                    "Back from a new template returns to the preceding template with its draft intact.");
                wizard.NextCommand.Execute(null);
                Check(receipt, wizard.CurrentTemplate.Id == galleryId && wizard.CurrentTemplate.Background == "#19202C",
                    "Forward navigation returns to the existing next template without duplicating or replacing it.");
                for (var index=0; index<6; index++) wizard.NextCommand.Execute(null);
                wizard.FinishTemplatesCommand.Execute(null); wizard.DefaultPageTemplateId = galleryId;
                Layout(view, new Size(1280,900));
                Check(receipt, wizard.IsReviewStep && model.CreateCommand.CanExecute(null)
                    && Named<Button>(view, "CreateProjectButton").Visibility == Visibility.Visible,
                    "Finishing named templates leads to the saved-project review and creation action.");
                var definition=wizard.BuildDefinition();
                using (var closedPreview = new HyperlibGuidedPagePreview())
                {
                    closedPreview.SetDraft(libraryRoot ?? repositoryRoot, definition, definition.LandingTemplate, "Closing preview");
                    closedPreview.Dispose();
                    closedPreview.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    closedPreview.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    Check(receipt, true, "Late native load and unload events after preview disposal are harmless and cannot start a browser.");
                }
                Check(receipt, definition.PipelineVersion==2 && definition.PageDefinitions.Count==0 && definition.Description.Length==0
                    && definition.PageTemplates.Count==2 && definition.PageTemplates.Select(item=>item.Name).SequenceEqual(new[]{"Article","Gallery"})
                    && definition.DefaultPageTemplateId==galleryId && definition.PageTemplate.BackgroundColor=="#19202C"
                    && definition.LandingTemplate.HeaderStyle.Text=="Shared site identity"
                    && definition.LandingTemplate.BodyLayout.Enabled&&definition.LandingTemplate.BodyLayout.Blocks.Any(item=>item.Id==slotId),
                    "The contract stores multiple named reusable recipes, their default, chrome styles, and stable slots without finished content Pages.");
                foreach (var size in new[] { new Size(640, 920), new Size(1280, 900) })
                    Render(view, Path.Combine(root, $"guided-foundation-review-{size.Width:0}.png"), size, receipt);
                var restored=new HyperlibWorkWizardViewModel(libraryRoot); restored.LoadDefinition(definition);
                Check(receipt, restored.PageTemplates.Count==2 && restored.DefaultPageTemplateId==galleryId
                    && restored.PageTemplates.Single(item=>item.Id==articleId).HeaderStyle.Text=="Article identity"
                    && restored.LandingTemplate.BodyLayout.Blocks.Any(item=>item.Id==slotId)
                    && restored.CaptureDraftState()==wizard.CaptureDraftState(),
                    "Reopening round-trips all named drafts, stable placements, default identity, and chrome styling.");
                restored.PageTemplates[1].Name="Article";
                Check(receipt,!restored.IsValid && restored.ValidationMessage.Contains("different name"),"Duplicate template names receive explicit validation.");
                var missingDraft=new HyperlibWorkWizardViewModel(libraryRoot);
                var unavailable=ContentJson.Clone(definition);unavailable.SelectedToolsetIds.Add("missing-collection:missing-object");missingDraft.LoadDefinition(unavailable);
                Check(receipt,!missingDraft.IsValid && !missingDraft.NextCommand.CanExecute(null)
                    && missingDraft.CurrentValidationMessage.Contains("Restore their collection"),
                    "Unavailable saved objects block changes without silently losing project choices.");
                var ownedMetadata=wizard.CollectedObjects.Select(item=>new HyperlibProjectToolkitItem
                {ChoiceId=item.Id,Title=item.Title,Description=item.Description,GroupTitle=item.GroupTitle,Kind=item.Kind,Category=item.Category}).ToArray();
                var portableDraft=new HyperlibWorkWizardViewModel(Path.Combine(root,"missing-source-library"),ownedMetadata);portableDraft.LoadDefinition(definition);
                Check(receipt,portableDraft.IsValid && portableDraft.PageTemplates.Count==2
                    && portableDraft.DefaultPageTemplateId==galleryId && portableDraft.LandingTemplate.BodyLayout.Blocks.Any(item=>item.Id==slotId),
                    "A moved project retains its named templates and saved collection without the original library.");
                var wizardSource=XDocument.Load(Path.Combine(repositoryRoot,"src/Hypershell/HyperlibWorkWizardView.xaml"));
                Check(receipt,!wizardSource.ToString().Contains("PageCountText") && !wizardSource.ToString().Contains("SelectedStarter")
                    && !wizardSource.ToString().Contains("LibraryShoppingStep") && wizardSource.ToString().Contains("PageBuilderPreview"),
                    "The normal wizard uses a persistent builder and guided choices rather than Page counts, mandatory foundations, or an initial catalogue wall.");
                var openedPaths = new List<string>();
                using var retry = new StudioProjectStartTabViewModel(path =>
                {
                    openedPaths.Add(path);
                    return Task.FromResult(openedPaths.Count > 1);
                }, libraryRoot, root);
                retry.ProjectName = "Retry opening this HTML project";
                retry.ChooseHtmlCommand.Execute(null);
                retry.ChooseTemplateCommand.Execute(null);retry.ContinueCommand.Execute(null);
                retry.Gallery.Selected=retry.Gallery.Templates.First(item=>item.SupportedKinds.Contains(StudioProjectKind.Html));
                await retry.CreateAsync();
                var created = retry.GeneratedProjectPath ?? throw new InvalidOperationException(retry.ErrorMessage);
                var manifest = Path.Combine(created, VisualPageProjectService.ProjectManifestFileName);
                var hash = SHA256.HashData(File.ReadAllBytes(manifest));
                Check(receipt, openedPaths.Count == 1 && Directory.Exists(created) && retry.HasError && !retry.IsEditable
                    && retry.CreateCommand.CanExecute(null), "A successful creation followed by an open failure retains the project and offers an opening retry.");
                await retry.CreateAsync();
                Check(receipt, openedPaths.Count == 2 && openedPaths.All(path => path == created)
                    && SHA256.HashData(File.ReadAllBytes(manifest)).SequenceEqual(hash) && !retry.HasError,
                    "Retry opens the same generated project without recreating it or changing its files.");
            });
            receipt.Passed = true;
        }
        catch (Exception ex) { receipt.Failure = ex.ToString(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(root, "studio-project-start-verification.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STUDIO_PROJECT_START_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={root}");
    }
    private static T Named<T>(FrameworkElement view, string name) where T : class => view.FindName(name) as T ?? throw new InvalidOperationException("Missing " + name);
    private static IEnumerable<TextBlock> DescendantTexts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in DescendantTexts(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Check(Receipt receipt, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        receipt.Assertions.Add(message);
    }
    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    private static void Layout(FrameworkElement view, Size size)
    {
        view.Width = size.Width; view.Height = size.Height;
        view.Measure(size); view.Arrange(new Rect(new Point(), size)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }
    private static void Render(FrameworkElement view, string path, Size size, Receipt receipt)
    {
        Layout(view, size);
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file); receipt.Renders.Add(path);
    }
    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Studio project start acceptance" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void LoadResources(string repository)
    {
        if (Application.Current is not null)
        {
            if (!Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Run project start verification in its own process.");
            return;
        }
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(repository, "src", "Hypershell", "App.xaml")).Root!;
        var dictionary = new XElement(presentation + "ResourceDictionary",
            source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }
    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string Fixture { get; set; } = "";
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
        public string? Failure { get; set; }
    }
}
