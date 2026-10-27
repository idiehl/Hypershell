using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class VisualPageStudioVerifier
{
    public static Task VerifyPlainCssWorkflowAsync(string verifierRoot) =>
        HyperpageCssVerifier.VerifyAsync(verifierRoot);

    public static async Task VerifyAsync(string verifierRoot)
    {
        await VerifyGuidedProjectFoundationAsync(verifierRoot);
        await VerifyPageCreationFileTypesAsync(verifierRoot);
        await VerifyWorkspaceTabsAsync(verifierRoot);

        var projectRoot = Path.Combine(verifierRoot, "studio-project");
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var mutationEngine = new VisualPageMutationEngine(validator);
        var projectService = new VisualPageProjectService(
            validator,
            generator,
            catalog);
        var session = await projectService.CreateAsync(
            projectRoot,
            "Studio Verifier",
            "Studio.Verifier");
        using var preview = new VisualPagePreviewBuildService(
            new BoundedProcessRunner(),
            generator,
            Path.Combine(verifierRoot, "preview-cache"));
        using var host = new VisualPageLocalHostService(
            new BoundedProcessRunner());
        using var tab = await VisualPageStudioTabViewModel.CreateAsync(
            session,
            projectService,
            mutationEngine,
            generator,
            validator,
            catalog,
            new VisualPageTemplateService(
                catalog,
                Path.Combine(verifierRoot, "personal-templates")),
            new VisualPageDesignCanvasRenderer(catalog),
            preview,
            host,
            new VisualPageUserSourceService(),
            new AtlasArchivePublicationService(
                new BoundedProcessRunner(),
                projectService,
                generator,
                validator,
                Path.Combine(verifierRoot, "atlas-publications")));

        Require(tab.Kind == WorkspaceTabKind.VisualPage
                && tab.StudioTools.Select(tool => tool.Id).ToHashSet(
                        StringComparer.Ordinal)
                    .IsSupersetOf(
                    [
                        "elements",
                        "project",
                        "language-library",
                        "page-tree",
                        "templates",
                        "assets",
                        "language-builder",
                        "inspector",
                        "layout",
                        "styles",
                        "state-bindings",
                        "events",
                        "code",
                        "ai",
                        "build",
                        "host",
                        "publish"
                    ])
                && tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane
                && tab.Layout.Root.PaneContents.SequenceEqual(
                    ["canvas"],
                    StringComparer.Ordinal),
            "The detachable Studio tab or its descriptor-driven tool set was incomplete.");
        var friendlyToolNames = tab.StudioTools.ToDictionary(
            tool => tool.Id,
            tool => tool.DisplayName,
            StringComparer.Ordinal);
        Require(
            friendlyToolNames["page-tree"] == "Structure"
            && friendlyToolNames["inspector"] == "Properties"
            && friendlyToolNames["styles"] == "Appearance"
            && friendlyToolNames["state-bindings"] == "Data"
            && friendlyToolNames["events"] == "Actions"
            && friendlyToolNames["build"] == "Preview"
            && friendlyToolNames["host"] == "Host",
            "Studio tool names did not retain the plain-language authoring vocabulary.");
        Require(
            string.Equals(
                Path.GetFullPath(
                    VisualPageProjectService.DefaultProjectsDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hyperpage Studio", "Projects"),
                StringComparison.OrdinalIgnoreCase),
            "Hyperpage Studio did not use the installed Program Files Projects directory.");
        VerifyReadOnlyStudioBindings();
        VerifyCanvasNavigationBoundary();

        Require(
            tab.Templates.Select(value => value.TemplateId).ToHashSet(
                    StringComparer.Ordinal)
                .IsSupersetOf(
                [
                    "atlas.hero.split.v1",
                    "atlas.card-grid.v1",
                    "atlas.cta.v1",
                    "core.navigation.header.v1",
                    "core.contact.form.v1",
                    "core.faq.disclosure.v1",
                    "core.accessible.table.v1",
                    "common.page.landing.v1",
                    "common.page.directory.v1",
                    "common.page.documentation.v1",
                    "common.page.dashboard.v1",
                    "common.section.sidebar-shell.v1",
                    "common.section.main-content-panel.v1",
                    "common.element.pagination.v1"
                ]),
            "The expanded built-in page-pattern template set was incomplete.");
        Require(
            tab.Templates.Count == 58
            && tab.Templates.Count(value => value.Scope == "page") == 14
            && tab.Templates.Count(value => value.Scope == "section") == 27
            && tab.Templates.Count(value => value.Scope == "element") == 17
            && tab.FilteredTemplates.FirstOrDefault()?.TemplateId ==
            "atlas.archive.page.longform.v1"
            && tab.TemplateCatalogSummary.Contains(
                "14 page starters",
                StringComparison.Ordinal),
            "The combined Hyperpage template library did not expose its page, section, and element starters in an intuitive order.");
        tab.SelectedTemplateScopeFilter = "Pages";
        tab.SelectedTemplateLibraryFilter = "Atlas Archive";
        tab.TemplateSearchText = "language audio";
        Require(
            tab.FilteredTemplates.Count == 1
            && tab.FilteredTemplates[0].TemplateId ==
            "atlas.archive.page.language-culture.v1"
            && tab.IsSelectedTemplatePage
            && tab.SelectedTemplateActionText ==
            "Insert"
            && tab.TemplatePageRoute == "/archive/language-culture-guide"
            && !string.IsNullOrWhiteSpace(tab.SelectedTemplateElementsText)
            && tab.SelectedTemplateElementsText.Contains(
                "<main>",
                StringComparison.Ordinal),
            "Template search, filters, page guidance, or suggested Archive metadata was incomplete.");
        tab.TemplateSearchText = string.Empty;
        tab.SelectedTemplateLibraryFilter =
            VisualPageCommonTemplateCatalog.LibraryName;
        tab.SelectedTemplateCategoryFilter = "Discovery & collections";
        Require(
            tab.FilteredTemplates.Count == 2
            && tab.FilteredTemplates.Select(value => value.TemplateId)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(
                [
                    "common.page.directory.v1",
                    "common.page.portfolio.v1"
                ])
            && tab.TemplateCategoryFilters.Contains(
                "Discovery & collections"),
            "Intent-first category filtering did not expose the expected discovery page starters.");
        tab.SelectedTemplateScopeFilter = "All types";
        tab.SelectedTemplateLibraryFilter = "All libraries";
        tab.SelectedTemplateCategoryFilter =
            VisualPageTemplateTaxonomy.AllCategories;
        Require(!tab.CaptureWebTemplateCommand.CanExecute(null),
            "Website capture unexpectedly enabled without an attached AI assistant and explicit permission.");
        Require(
            tab.SelectedElementScope == "Common"
            && tab.ElementScopes.SequenceEqual(
            [
                "Common", "All HTML", "Input controls", "Blazor & logic",
                "Atlas patterns", "Legacy HTML", "Everything"
            ])
            && tab.FilteredElementDescriptors
                .Where(value => value.IsStandardHtmlElement)
                .Select(value => value.MarkupName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            == tab.FilteredElementDescriptors.Count(value =>
                value.IsStandardHtmlElement)
            && tab.ElementCatalogSummary.Contains(
                "113 current HTML tags · 29 legacy tags",
                StringComparison.Ordinal),
            "The simplified element library did not open in a deduplicated Common view with visible coverage totals.");
        tab.ElementSearchText = "selectedcontent";
        Require(
            tab.FilteredElementDescriptors.Count == 1
            && tab.FilteredElementDescriptors[0].MarkupName == "selectedcontent",
            "Searching from the Common library did not reach every modern HTML element.");
        Require(
            !string.IsNullOrWhiteSpace(tab.SelectedDescriptorBadge)
            && !string.IsNullOrWhiteSpace(tab.SelectedDescriptorPlacement)
            && !string.IsNullOrWhiteSpace(tab.SelectedDescriptorChildren)
            && !string.IsNullOrWhiteSpace(tab.SelectedDescriptorCanvas)
            && !string.IsNullOrWhiteSpace(tab.SelectedDescriptorSummary)
            && tab.SelectedDescriptorAttributes.Count > 0
            && tab.SelectedDescriptorAttributes.Any(attribute =>
                attribute.Name == "class" && attribute.IsGlobal),
            "The Elements tool did not explain the selected element or expose its supported attributes.");
        tab.ElementSearchText = string.Empty;
        tab.SelectedElementScope = "All HTML";
        Require(
            tab.FilteredElementDescriptors.Count == 113
            && tab.FilteredElementDescriptors
                .Select(value => value.MarkupName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(catalog.StandardHtmlTags),
            "The All HTML library did not expose exactly one entry for every current HTML element.");
        tab.SelectedElementCategory = "Tables";
        Require(
            tab.FilteredElementDescriptors.Count == 10
            && tab.FilteredElementDescriptors.All(value =>
                value.Category == "Tables"),
            "Element catalog category filtering did not isolate a complete tool group.");
        tab.SelectedElementCategory = "All categories";
        var studioInputTypes = new[]
        {
            "button", "checkbox", "color", "date", "datetime-local", "email",
            "file", "hidden", "image", "month", "number", "password", "radio",
            "range", "reset", "search", "submit", "tel", "text", "time", "url", "week"
        };
        tab.SelectedElementScope = "Input controls";
        Require(
            studioInputTypes.All(type => tab.FilteredElementDescriptors.Any(value =>
                value.Id.Equals(
                    $"html.input.{type}",
                    StringComparison.OrdinalIgnoreCase))),
            "The Input controls library did not expose all 23 native input states.");
        tab.SelectedElementScope = "Legacy HTML";
        Require(
            tab.FilteredElementDescriptors.Count == 29
            && tab.FilteredElementDescriptors.All(value =>
                value.IsObsolete
                && !string.IsNullOrWhiteSpace(value.Authoring.Warning)),
            "The compatibility library did not expose and clearly warn about every legacy HTML element.");
        tab.SelectedElementScope = "Common";

        tab.CanvasZoomPercent = 125;
        tab.CanvasViewportWidth = "375";
        tab.ShowCanvasGrid = true;
        tab.ShowCanvasOutlines = true;
        Require(
            tab.CanvasHtml.Contains("width:375px", StringComparison.Ordinal)
            && tab.CanvasHtml.Contains("zoom:1.25", StringComparison.Ordinal)
            && tab.CanvasHtml.Contains("vp-grid", StringComparison.Ordinal)
            && tab.CanvasHtml.Contains("vp-outlines", StringComparison.Ordinal),
            "Canvas zoom, device width, grid, or outline controls were not reflected in the live canvas.");
        tab.AdjustCanvasZoom(10);
        Require(tab.CanvasZoomPercent == 135,
            "Canvas wheel-style zoom did not use the bounded zoom model.");
        tab.ResetCanvasViewCommand.Execute(null);
        Require(tab.CanvasZoomPercent == 100
                && tab.CanvasViewportWidth == "Auto"
                && !tab.ShowCanvasGrid
                && !tab.ShowCanvasOutlines,
            "Reset Canvas View did not restore the authoring defaults.");

        var firstSection = catalog.CreateElement("core.section");
        firstSection.Name = "First drop zone";
        var secondSection = catalog.CreateElement("core.section");
        secondSection.Name = "Grid drop zone";
        var child = catalog.CreateElement("core.paragraph");
        child.Name = "Movable paragraph";
        child.Text = "Move and resize me";
        var inserted = tab.ApplyMutation(new VisualPageMutationSet(
            tab.Revision,
            [
                new VisualPageMutationOperation
                {
                    OperationId = "studio-insert-first",
                    Kind = VisualPageMutationKind.InsertChild,
                    ParentElementId = tab.Document.Root.ElementId,
                    Element = firstSection
                },
                new VisualPageMutationOperation
                {
                    OperationId = "studio-insert-second",
                    Kind = VisualPageMutationKind.InsertChild,
                    ParentElementId = tab.Document.Root.ElementId,
                    Element = secondSection
                },
                new VisualPageMutationOperation
                {
                    OperationId = "studio-insert-child",
                    Kind = VisualPageMutationKind.InsertChild,
                    ParentElementId = firstSection.ElementId,
                    Element = child
                }
            ],
            "studio-verifier",
            "exercise canvas insertion"));
        Require(inserted && tab.HasUnsavedChanges,
            "Canvas semantic insertion did not dirty the Studio document.");

        tab.SelectElement(tab.Document.Root.ElementId);
        var countBeforeInvalidPlacement = CountElements(tab.Document.Root);
        tab.SelectedDescriptor = catalog.GetRequired("html.td");
        Require(
            !tab.CanAddSelectedDescriptor
            && !tab.AddElementCommand.CanExecute(null)
            && tab.AddElementButtonText == "Add"
            && tab.AddElementHelp.Contains("<tr>", StringComparison.Ordinal),
            "The Elements tool presented an impossible placement as an available add action.");
        tab.AddDescriptorToSelected("html.td");
        Require(
            CountElements(tab.Document.Root) == countBeforeInvalidPlacement
            && tab.StatusMessage.Contains(
                "cannot be added here",
                StringComparison.OrdinalIgnoreCase)
            && tab.StatusMessage.Contains("<tr>", StringComparison.Ordinal),
            "The Elements tool did not stop invalid constrained placement with useful guidance.");

        var idsBeforeStarter = Flatten(tab.Document.Root)
            .Select(value => value.ElementId)
            .ToHashSet(StringComparer.Ordinal);
        tab.SelectedDescriptor = catalog.GetRequired("html.table");
        Require(
            tab.CanAddSelectedDescriptor
            && tab.AddElementCommand.CanExecute(null),
            "A valid complex element was unexpectedly disabled in the Elements tool.");
        tab.AddElementCommand.Execute(null);
        var authoredTable = Flatten(tab.Document.Root).Single(value =>
            value.DescriptorId == "html.table"
            && !idsBeforeStarter.Contains(value.ElementId));
        Require(
            authoredTable.Children.Select(value => value.DescriptorId)
                .SequenceEqual(["html.caption", "html.thead", "html.tbody"])
            && Flatten(authoredTable).Count(value => value.DescriptorId == "html.th") == 2
            && Flatten(authoredTable).Count(value => value.DescriptorId == "html.td") == 2,
            "Adding a complex HTML object did not create its useful valid starter structure.");

        tab.SelectElement(firstSection.ElementId);
        var idsBeforeDraft = Flatten(tab.Document.Root)
            .Select(value => value.ElementId)
            .ToHashSet(StringComparer.Ordinal);
        tab.SelectedDescriptor = catalog.GetRequired("core.container");
        Require(
            tab.IsCatalogElementDraftActive
            && !tab.IsPlacedElementInspector
            && tab.SelectedTool?.Id == "inspector"
            && tab.InspectorElementMarkup == "<div></div>"
            && tab.AvailableAttributes.Count > 0,
            "Selecting a catalog element did not open its pre-insertion Properties draft.");
        tab.AttributeName = "data-draft";
        tab.AttributeValue = "apples";
        tab.SetAttributeCommand.Execute(null);
        tab.SelectedTextEditor = "Draft apples";
        tab.ApplyTextCommand.Execute(null);
        Require(
            tab.InspectorElementMarkup.Contains(
                "<div data-draft=\"apples\">Draft apples</div>",
                StringComparison.Ordinal),
            "The pre-insertion Properties draft did not preview configured markup.");
        tab.AddElementCommand.Execute(null);
        var authoredDraft = Flatten(tab.Document.Root).Single(value =>
            value.DescriptorId == "core.container"
            && !idsBeforeDraft.Contains(value.ElementId));
        Require(
            authoredDraft.Text == "Draft apples"
            && authoredDraft.Attributes.GetValueOrDefault("data-draft") == "apples"
            && !tab.IsCatalogElementDraftActive
            && tab.IsPlacedElementInspector,
            "Add to Selection did not carry the configured draft properties into the document.");

        tab.SelectElement(firstSection.ElementId);
        tab.AddDescriptorToSelected("html.br");
        var authoredBreak = tab.SelectedElement!;
        Require(
            authoredBreak.DescriptorId == "html.br"
            && !tab.SelectedElementSupportsText
            && tab.SelectedElementDoesNotSupportText
            && tab.CanvasHtml.Contains(authoredBreak.ElementId, StringComparison.Ordinal)
            && tab.CanvasHtml.Contains("vp-placeholder", StringComparison.Ordinal),
            "An empty HTML element exposed a misleading text editor or became unselectable on the canvas.");
        var rootChildrenBeforeSiblingInsert = tab.Document.Root.Children.Count;
        var sectionChildrenBeforeSiblingInsert = Find(
            tab.Document.Root,
            firstSection.ElementId)!.Children.Count;
        tab.AddDescriptorToSelected("core.paragraph");
        Require(
            tab.Document.Root.Children.Count == rootChildrenBeforeSiblingInsert
            && Find(tab.Document.Root, firstSection.ElementId)!.Children.Count
            == sectionChildrenBeforeSiblingInsert + 1
            && tab.SelectedElement?.DescriptorId == "core.paragraph",
            "Adding after an empty element unexpectedly moved the new object to the page root.");

        tab.MoveElement(child.ElementId, secondSection.ElementId, 0);
        Require(Find(tab.Document.Root, secondSection.ElementId)!.Children
                    .Any(element => element.ElementId == child.ElementId)
                && Find(tab.Document.Root, firstSection.ElementId)!.Children
                    .All(element => element.ElementId != child.ElementId),
            "Responsive canvas drag did not reparent through a valid drop zone.");

        tab.ApplyMutation(new VisualPageMutationSet(
            tab.Revision,
            [
                new VisualPageMutationOperation
                {
                    OperationId = "studio-grid-parent",
                    Kind = VisualPageMutationKind.SetLayout,
                    TargetElementId = secondSection.ElementId,
                    Breakpoint = VisualPageBreakpoint.Base,
                    Layout = new VisualPageLayoutSpec
                    {
                        Display = "grid",
                        GridTemplateColumns = "repeat(12, minmax(0, 1fr))",
                        Gap = "1rem"
                    }
                },
                new VisualPageMutationOperation
                {
                    OperationId = "studio-grid-child",
                    Kind = VisualPageMutationKind.SetLayout,
                    TargetElementId = child.ElementId,
                    Breakpoint = VisualPageBreakpoint.Base,
                    Layout = new VisualPageLayoutSpec
                    {
                        ColumnSpan = 6
                    }
                }
            ],
            "studio-verifier",
            "configure responsive grid"));
        tab.SelectElement(child.ElementId);
        tab.ResizeElement(child.ElementId, deltaX: 96, startWidth: 600);
        Require(tab.SelectedElement?.ElementId == child.ElementId
                && Find(tab.Document.Root, child.ElementId)!
                    .Layout[VisualPageBreakpoint.Base].ColumnSpan == 8
                && tab.CanvasHtml.Contains(
                    child.ElementId,
                    StringComparison.Ordinal),
            "Canvas selection/panel synchronization or responsive grid resize failed.");

        tab.LayoutDisplay = "flex";
        tab.LayoutDirection = "column";
        tab.LayoutWrap = "wrap";
        tab.LayoutGap = "0.75rem";
        tab.LayoutPadding = "1rem";
        tab.LayoutMargin = "0 auto";
        tab.LayoutFlexBasis = "50%";
        tab.LayoutColumnSpan = "8";
        tab.LayoutRowSpan = "2";
        tab.LayoutMinWidth = "12rem";
        tab.LayoutMaxWidth = "48rem";
        tab.LayoutMinHeight = "4rem";
        tab.LayoutMaxHeight = "60rem";
        tab.LayoutAlignItems = "stretch";
        tab.LayoutJustifyContent = "space-between";
        tab.LayoutAlignSelf = "center";
        tab.ApplyLayoutCommand.Execute(null);
        var authoredLayout = Find(tab.Document.Root, child.ElementId)!
            .Layout[VisualPageBreakpoint.Base];
        Require(
            authoredLayout.Display == "flex"
            && authoredLayout.FlexDirection == "column"
            && authoredLayout.FlexWrap == "wrap"
            && authoredLayout.RowSpan == 2
            && authoredLayout.Margin == "0 auto"
            && authoredLayout.MinWidth == "12rem"
            && authoredLayout.MaxHeight == "60rem"
            && authoredLayout.AlignItems == "stretch"
            && authoredLayout.JustifyContent == "space-between"
            && authoredLayout.AlignSelf == "center",
            "The expanded responsive layout tool did not preserve its authored constraints.");

        var sibling = catalog.CreateElement("core.button");
        sibling.Name = "Sibling button";
        tab.ApplyMutation(new VisualPageMutationSet(
            tab.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.InsertChild,
                ParentElementId = secondSection.ElementId,
                Element = sibling
            }],
            "studio-verifier",
            "exercise layer ordering"));
        tab.SelectElement(sibling.ElementId);
        Require(
            tab.AvailableAttributes.Any(value => value.Name == "command")
            && tab.AvailableAttributes.Any(value =>
                value.Name == "disabled"
                && value.EditorKind == VisualPageAttributeEditorKind.Boolean)
            && tab.AvailableAttributes.Any(value => value.Name == "aria-label")
            && tab.CssPropertySuggestions.Contains("transform", StringComparer.Ordinal)
            && tab.CssPropertySuggestions.Contains("transition", StringComparer.Ordinal),
            "The schema-backed Inspector or CSS authoring suggestions were incomplete.");
        Require(
            tab.SelectedElementSupportsText
            && tab.SelectedElementSupportsBindings
            && tab.SelectedElementSupportsEvents
            && !string.IsNullOrWhiteSpace(tab.SelectedElementBadge)
            && !string.IsNullOrWhiteSpace(tab.SelectedElementPlacementGuidance)
            && !string.IsNullOrWhiteSpace(tab.SelectedElementChildrenGuidance)
            && !string.IsNullOrWhiteSpace(tab.SelectedElementCanvasGuidance),
            "Selecting an object did not synchronize its element-aware editor guidance and relevant actions.");
        Require(
            !tab.SetAttributeCommand.CanExecute(null)
            && !tab.RemoveAttributeCommand.CanExecute(null)
            && !tab.SetStyleCommand.CanExecute(null)
            && !tab.RemoveStyleCommand.CanExecute(null),
            "Property actions appeared available before a property was chosen.");
        tab.SelectedAttributeDefinition = tab.AvailableAttributes.Single(value =>
            value.Name == "disabled");
        Require(
            tab.UsesBooleanAttributeEditor
            && !tab.UsesChoiceAttributeEditor
            && !tab.UsesTextAttributeEditor
            && tab.SetAttributeCommand.CanExecute(null)
            && !tab.RemoveAttributeCommand.CanExecute(null)
            && tab.AttributeEditorKindLabel == "On or off",
            "Boolean HTML properties did not receive an intuitive on/off editor.");
        tab.AttributeBooleanValue = true;
        tab.SetAttributeCommand.Execute(null);
        Require(
            Find(tab.Document.Root, sibling.ElementId)!.Attributes.ContainsKey(
                "disabled"),
            "Enabling a Boolean property did not apply it to the selected element.");
        tab.SelectedAttributeDefinition = tab.AvailableAttributes.Single(value =>
            value.Name == "disabled");
        Require(tab.RemoveAttributeCommand.CanExecute(null),
            "An applied property did not enable its Remove action.");
        tab.AttributeBooleanValue = false;
        tab.SetAttributeCommand.Execute(null);
        Require(
            !Find(tab.Document.Root, sibling.ElementId)!.Attributes.ContainsKey(
                "disabled"),
            "Turning off a Boolean property left a confusing false-valued HTML attribute behind.");
        tab.SelectedAttributeDefinition = tab.AvailableAttributes.Single(value =>
            value.Name == "type");
        Require(
            tab.UsesChoiceAttributeEditor
            && !tab.AttributeChoiceIsEditable
            && tab.AttributeValueSuggestions.SequenceEqual(
                ["button", "submit", "reset"]),
            "The button type property did not present its exact valid choices.");
        tab.SelectedAttributeDefinition = tab.AvailableAttributes.Single(value =>
            value.Name == "aria-label");
        Require(
            tab.UsesTextAttributeEditor
            && !tab.UsesBooleanAttributeEditor
            && !string.IsNullOrWhiteSpace(tab.SelectedAttributeHelp),
            "Free-text and accessibility properties did not retain a plain-language value editor and help text.");
        tab.StyleName = "border-radius";
        Require(!tab.SetStyleCommand.CanExecute(null),
            "An empty CSS value presented a no-op Apply action.");
        tab.StyleValue = "0.5rem";
        Require(
            tab.SetStyleCommand.CanExecute(null)
            && !tab.RemoveStyleCommand.CanExecute(null),
            "A complete CSS property did not enable Apply or incorrectly enabled Remove.");
        tab.SetStyleCommand.Execute(null);
        tab.StyleName = "border-radius";
        Require(tab.RemoveStyleCommand.CanExecute(null),
            "An applied CSS property did not enable Remove.");
        tab.RemoveStyleCommand.Execute(null);
        Require(
            !Find(tab.Document.Root, sibling.ElementId)!.Styles.ContainsKey(
                "border-radius"),
            "Removing an applied CSS property left it on the selected object.");
        tab.MoveUpCommand.Execute(null);
        Require(
            Find(tab.Document.Root, secondSection.ElementId)!.Children[0].ElementId
            == sibling.ElementId,
            "Move Up did not reorder the selected layer.");
        tab.MoveDownCommand.Execute(null);
        Require(
            Find(tab.Document.Root, secondSection.ElementId)!.Children[1].ElementId
            == sibling.ElementId,
            "Move Down did not restore the selected layer order.");
        tab.SelectParentCommand.Execute(null);
        Require(tab.SelectedElement?.ElementId == secondSection.ElementId,
            "Select Parent did not synchronize the canvas and Page Tree selection.");
        tab.SelectElement(child.ElementId);

        var countBeforeDuplicate = CountElements(tab.Document.Root);
        tab.DuplicateCommand.Execute(null);
        var ids = Flatten(tab.Document.Root)
            .Select(element => element.ElementId)
            .ToList();
        Require(CountElements(tab.Document.Root) > countBeforeDuplicate
                && ids.Count == ids.Distinct(StringComparer.Ordinal).Count(),
            "Context-menu duplicate did not remap stable subtree IDs.");

        var textBefore = Find(tab.Document.Root, child.ElementId)!.Text;
        tab.ApplyMutation(new VisualPageMutationSet(
            tab.Revision,
            [new VisualPageMutationOperation
            {
                OperationId = "studio-undo-text",
                Kind = VisualPageMutationKind.SetText,
                TargetElementId = child.ElementId,
                Value = "Undo/redo verifier text"
            }],
            "studio-verifier",
            "exercise bounded undo"));
        tab.UndoCommand.Execute(null);
        Require(Find(tab.Document.Root, child.ElementId)!.Text == textBefore,
            "Bounded semantic undo did not restore the prior visual document.");
        tab.RedoCommand.Execute(null);
        Require(Find(tab.Document.Root, child.ElementId)!.Text
                    == "Undo/redo verifier text",
            "Bounded semantic redo did not restore the visual mutation.");

        var detachCount = 0;
        tab.DetachRequested += (_, _) => detachCount++;
        tab.DetachCommand.Execute(null);
        Require(detachCount == 1,
            "Detach did not preserve and request the same live Studio view model.");

        Require(
            tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane
            && tab.Layout.Root.ActiveContentId == "canvas"
            && tab.IsCanvasOnlyWorkspace
            && !tab.IsDocumentOnlyWorkspace
            && !tab.IsCodeRightWorkspace
            && !tab.IsCodeBelowWorkspace,
            "Hyperpage Studio did not open in the canvas-only workspace.");
        tab.DocumentOnlyCommand.Execute(null);
        var layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        Require(
            tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane
            && tab.IsDocumentOnlyWorkspace
            && !tab.IsCanvasOnlyWorkspace
            && !tab.IsCodeRightWorkspace
            && !tab.IsCodeBelowWorkspace
            && !tab.Layout.Root.PaneContents.Contains(
                "canvas",
                StringComparer.Ordinal)
            && tab.Layout.Root.PaneContents.Contains(
                "generated-razor",
                StringComparer.Ordinal),
            "Documents-only mode did not fill the workspace with the source pane.");
        tab.RequestPaneContent("css");
        tab.RequestPaneContent("javascript");
        layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        Require(
            tab.IsDocumentOnlyWorkspace
            && layoutNodes.Count == 1
            && CountPaneContent(layoutNodes, "css") == 1
            && CountPaneContent(layoutNodes, "javascript") == 1
            && tab.Layout.Root.ActiveContentId == "javascript",
            "The documents-only pane did not add and select project CSS and JavaScript files.");
        tab.SplitHorizontalCommand.Execute(null);
        layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        Require(
            tab.Layout.Root.Kind == StudioLayoutNodeKind.Split
            && tab.Layout.Root.Orientation == StudioSplitOrientation.Horizontal
            && tab.IsCodeRightWorkspace
            && !tab.IsCanvasOnlyWorkspace
            && !tab.IsDocumentOnlyWorkspace
            && !tab.IsCodeBelowWorkspace
            && layoutNodes.Count(node =>
                node.Kind == StudioLayoutNodeKind.Split) == 1
            && CountPaneContent(layoutNodes, "generated-razor") == 1,
            "Opening code beside the canvas did not restore the canvas beside the existing document pane.");
        tab.SplitHorizontalCommand.Execute(null);
        tab.SplitHorizontalCommand.Execute(null);
        layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        Require(
            layoutNodes.Count(node =>
                node.Kind == StudioLayoutNodeKind.Split) == 1
            && CountPaneContent(layoutNodes, "generated-razor") == 1,
            "Repeated Code right clicks created duplicate editor panes.");
        tab.SplitVerticalCommand.Execute(null);
        layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        Require(
            tab.Layout.Root.Orientation == StudioSplitOrientation.Vertical
            && tab.IsCodeBelowWorkspace
            && !tab.IsCanvasOnlyWorkspace
            && !tab.IsCodeRightWorkspace
            && layoutNodes.Count(node =>
                node.Kind == StudioLayoutNodeKind.Split) == 1
            && CountPaneContent(layoutNodes, "generated-razor") == 1,
            "Code below did not reposition the existing editor pane.");

        tab.RequestPaneContent("css");
        layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        var canvasPane = layoutNodes.First(node =>
            node.Kind == StudioLayoutNodeKind.Pane
            && node.PaneContents.Contains("canvas", StringComparer.Ordinal));
        var codePane = layoutNodes.Single(node =>
            node.Kind == StudioLayoutNodeKind.Pane
            && !node.PaneContents.Contains("canvas", StringComparer.Ordinal));
        Require(
            codePane.PaneContents.Contains("css", StringComparer.Ordinal)
            && CountPaneContent(layoutNodes, "css") == 1,
            "Requesting another code file did not reuse the existing editor pane.");
        Require(tab.MovePaneContent("css", canvasPane.NodeId)
                && canvasPane.PaneContents.Contains("css", StringComparer.Ordinal),
            "A code pane tab could not move into another Studio pane.");
        codePane = FlattenLayout(tab.Layout.Root).Single(node =>
            node.Kind == StudioLayoutNodeKind.Pane
            && !node.PaneContents.Contains("canvas", StringComparer.Ordinal));
        Require(
            tab.MovePaneContent("css", codePane.NodeId)
            && codePane.PaneContents.Contains("css", StringComparer.Ordinal),
            "A moved code tab could not return to the editor pane.");
        Require(
            tab.ClosePane(codePane.NodeId)
            && tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane
            && tab.IsCanvasOnlyWorkspace
            && !tab.HasActiveAuthoringDocument
            && tab.Layout.Root.PaneContents.SequenceEqual(
                ["canvas"],
                StringComparer.Ordinal),
            "The code-pane close action did not restore the canvas-only workspace.");
        tab.CanvasOnlyCommand.Execute(null);
        Require(tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane
                && tab.Layout.Root.ActiveContentId == "canvas"
                && tab.IsCanvasOnlyWorkspace,
            "Canvas-only mode did not retain one canvas pane.");
        tab.ResetLayoutCommand.Execute(null);
        Require(
            tab.Layout.Root.Kind == StudioLayoutNodeKind.Pane
            && tab.IsCanvasOnlyWorkspace
            && tab.IsLeftToolPanelOpen
            && tab.IsRightToolPanelOpen,
            "Reset layout did not restore the canvas and both Studio tool shelves.");
        tab.ToggleLeftToolPanelCommand.Execute(null);
        tab.ToggleRightToolPanelCommand.Execute(null);
        Require(
            tab.IsLeftToolPanelClosed
            && tab.IsRightToolPanelClosed
            && tab.LeftToolPanelWidth.Value == 0
            && tab.RightToolPanelWidth.Value == 0
            && tab.LeftToolPanelSplitterWidth.Value == 0
            && tab.RightToolPanelSplitterWidth.Value == 0,
            "The Studio tool shelves did not collapse into borderless zero-width edges.");
        tab.ToggleLeftToolPanelCommand.Execute(null);
        tab.ToggleRightToolPanelCommand.Execute(null);
        Require(
            tab.IsLeftToolPanelOpen
            && tab.IsRightToolPanelOpen
            && tab.LeftToolPanelWidth.Value == 300
            && tab.RightToolPanelWidth.Value == 340,
            "The collapsed Studio tool shelves did not reopen independently.");

        tab.Project.LayoutOverride = null;
        tab.RestoreLayoutPreferences(
            new StudioLayoutSnapshot
            {
                Name = "Legacy duplicate editors",
                Root = new StudioLayoutNode
                {
                    Kind = StudioLayoutNodeKind.Split,
                    Orientation = StudioSplitOrientation.Horizontal,
                    First = StudioLayoutNode.CreateDefault(),
                    Second = new StudioLayoutNode
                    {
                        Kind = StudioLayoutNodeKind.Split,
                        First = new StudioLayoutNode
                        {
                            Kind = StudioLayoutNodeKind.Pane,
                            PaneContents = ["generated-razor"],
                            ActiveContentId = "generated-razor"
                        },
                        Second = new StudioLayoutNode
                        {
                            Kind = StudioLayoutNodeKind.Pane,
                            PaneContents = ["generated-razor"],
                            ActiveContentId = "generated-razor"
                        }
                    }
                }
            },
            []);
        layoutNodes = FlattenLayout(tab.Layout.Root).ToList();
        Require(
            layoutNodes.Count(node =>
                node.Kind == StudioLayoutNodeKind.Split) == 1
            && CountPaneContent(layoutNodes, "generated-razor") == 1,
            "A saved layout containing duplicate editor panes was not normalized on restore.");
        tab.ResetLayoutCommand.Execute(null);
        tab.LayoutPresetName = "Verifier arrangement";
        tab.SaveLayoutPresetCommand.Execute(null);
        tab.SetPersonalDefaultLayoutCommand.Execute(null);
        tab.SetToolPlacement(
            "assets",
            StudioToolHostKind.Floating,
            new StudioFloatingBounds
            {
                Left = 120,
                Top = 80,
                Width = 420,
                Height = 560
            });
        tab.UpdateFloatingToolBounds(
            "assets",
            new StudioFloatingBounds
            {
                Left = 160,
                Top = 100,
                Width = 460,
                Height = 600
            });
        var floatingAssets = tab.Layout.ToolPlacements.Single(value =>
            value.ToolId == "assets");
        Require(tab.NamedLayoutPresets.Single().Name == "Verifier arrangement"
                && tab.PersonalDefaultLayout is not null
                && floatingAssets.Host == StudioToolHostKind.Floating
                && floatingAssets.FloatingBounds?.Left == 160
                && floatingAssets.FloatingBounds.Top == 100
                && floatingAssets.FloatingBounds.Width == 460
                && floatingAssets.FloatingBounds.Height == 600
                && tab.Project.LayoutOverride?.IsProjectOverride == true,
            "Named, personal-default, project-override, or floating tool layout state was not retained.");

        var assetSource = Path.Combine(verifierRoot, "pixel.png");
        await File.WriteAllBytesAsync(
            assetSource,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        await tab.ImportAssetFileAsync(
            assetSource,
            "Pixel",
            "Verifier pixel");
        Require(tab.ProjectAssets.Count == 1
                && tab.ProjectAssets[0].DisplayName == "Pixel"
                && tab.ProjectAssets[0].AltText == "Verifier pixel"
                && File.Exists(Path.Combine(
                    projectRoot,
                    tab.ProjectAssets[0].RelativePath)),
            "The Assets tool did not name, describe, copy, and hash a bounded project asset.");
        tab.SelectedAsset = tab.ProjectAssets[0];
        var image = catalog.CreateElement("core.image");
        tab.ApplyMutation(new VisualPageMutationSet(
            tab.Revision,
            [new VisualPageMutationOperation
            {
                Kind = VisualPageMutationKind.InsertChild,
                ParentElementId = tab.Document.Root.ElementId,
                Element = image
            }],
            "studio-verifier",
            "insert image asset target"));
        var altDiagnostic = tab.Diagnostics.Single(value =>
            value.Code == "VP201"
            && value.ElementId == image.ElementId);
        tab.SelectElement(tab.Document.Root.ElementId);
        tab.SelectedDiagnostic = altDiagnostic;
        Require(
            tab.SelectedElement?.ElementId == image.ElementId
            && tab.StatusMessage.Contains("VP201", StringComparison.Ordinal),
            "Selecting a diagnostic did not reveal and select its canvas element.");
        tab.UseAssetCommand.Execute(null);
        Require(Find(tab.Document.Root, image.ElementId)!.Attributes["alt"]
                    == "Verifier pixel"
                && tab.CanvasHtml.Contains(
                    "data:image/png;base64,",
                    StringComparison.Ordinal),
            "A verified raster asset was not mapped onto the selected media element and sanitized canvas.");

        var markupSource = Path.Combine(verifierRoot, "import.html");
        await File.WriteAllTextAsync(
            markupSource,
            "<section><h2>Imported through Studio</h2><p>Portable content</p></section>");
        var countBeforeImport = CountElements(tab.Document.Root);
        await tab.ImportMarkupFileAsync(markupSource);
        Require(CountElements(tab.Document.Root) > countBeforeImport
                && Flatten(tab.Document.Root).Any(element =>
                    element.Text == "Imported through Studio"),
            "The Studio import tool did not insert bounded HTML through semantic mutations.");

        tab.PageNameEditor = "Verifier Landing";
        tab.PageRouteEditor = "/verifier-landing";
        tab.PageSlugEditor = "verifier-landing";
        tab.PageTitleEditor = "Verifier Landing Title";
        tab.PageDescriptionEditor = "Metadata edited through the Page tool.";
        tab.ApplyPageMetadataCommand.Execute(null);
        Require(tab.Document.Name == "Verifier Landing"
                && tab.Document.Route == "/verifier-landing"
                && tab.Document.Slug == "verifier-landing",
            "Page metadata did not flow through the shared semantic mutation protocol.");

        tab.UserCss += Environment.NewLine + ".verifier { color: rebeccapurple; }";
        tab.UserJavaScript += Environment.NewLine
                              + "export function verifier() { return true; }";
        Require(tab.HasUnsavedChanges,
            "Editable CSS/JavaScript companion panes did not participate in dirty state.");

        var sessionPath = Path.Combine(
            verifierRoot,
            "session",
            "hyperpage-studio.session.json");
        var sessionService = new VisualPageStudioSessionService(sessionPath);
        var recovery = new VisualPageStudioSessionSnapshot
        {
            ActiveProjectId = tab.ProjectId,
            PersonalDefaultLayout = VisualPageJson.Clone(tab.Layout),
            NamedLayoutPresets =
            [
                new StudioLayoutSnapshot
                {
                    Name = "Verifier preset",
                    Root = StudioLayoutNode.CreateDefault()
                }
            ],
            Projects =
            [
                new VisualPageStudioSessionEntry
                {
                    ProjectRoot = tab.ProjectRoot,
                    ProjectId = tab.ProjectId,
                    PageId = tab.PageId,
                    SavedProjectRevision = tab.SavedProjectRevision,
                    SavedPageRevision = tab.SavedPageRevision,
                    RecoveryDocument = VisualPageJson.Clone(tab.Document),
                    HasUserSourceRecovery = true,
                    UserCSharp = tab.UserCSharp,
                    UserCss = tab.UserCss,
                    UserJavaScript = tab.UserJavaScript
                }
            ]
        };
        await sessionService.SaveAsync(recovery);
        var restored = await sessionService.LoadAsync();
        Require(restored.Projects is [{ RecoveryDocument: not null }]
                && restored.Projects[0].RecoveryDocument!.Revision
                    == tab.Revision
                && restored.Projects[0].HasUserSourceRecovery
                && restored.NamedLayoutPresets.Single().Name
                    == "Verifier preset",
            "Studio restart recovery lost the visual document, source panes, or named layouts.");

        Require(await tab.SaveProjectAsync()
                && !tab.HasUnsavedChanges
                && File.ReadAllText(Path.Combine(
                        projectRoot,
                        tab.Project.Pages.Single(page =>
                            page.PageId == tab.PageId).DocumentPath))
                    .Contains(tab.Revision, StringComparison.Ordinal),
            "Studio Save did not atomically persist the page and companion source state.");

        var firstPageId = tab.PageId;
        tab.NewPageName = "Second Page";
        tab.NewPageRoute = "/second-page";
        await tab.CreatePageAsync();
        Require(tab.Project.Pages.Count == 2
                && tab.PageId != firstPageId
                && tab.Document.Route == "/second-page"
                && !tab.HasUnsavedChanges
                && File.Exists(Path.Combine(
                    projectRoot,
                    tab.Project.Pages.Single(page =>
                        page.PageId == tab.PageId).ComponentPath)),
            "The Studio did not create, generate, save, and open a second portable page atomically.");

        tab.AddDescriptorToSelected("atlas.card");
        tab.TemplateNameEditor = "Verifier reusable Atlas card";
        tab.TemplateDescriptionEditor =
            "A saved element available from this project's template library.";
        tab.ConvertToTemplateCommand.Execute(null);
        var templateDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (tab.IsBusy && DateTimeOffset.UtcNow < templateDeadline)
        {
            await Task.Delay(25);
        }
        var savedElementTemplate = tab.Templates.SingleOrDefault(value =>
            value.Name == "Verifier reusable Atlas card");
        Require(
            !tab.IsBusy
            && savedElementTemplate is
            {
                Scope: "element",
                StorageScope: "project",
                Library: "This project"
            }
            && tab.SelectedTemplateScopeFilter == "Elements"
            && tab.SelectedTemplateLibraryFilter == "This project"
            && tab.FilteredTemplates.Contains(savedElementTemplate)
            && tab.Project.Templates.Any(value =>
                value.TemplateId == savedElementTemplate.TemplateId),
            "Saving a selected Atlas object did not create a clearly categorized, immediately reusable project template.");
        Require(await tab.SaveProjectAsync() && !tab.HasUnsavedChanges,
            "The saved element-template catalog entry did not persist with the project.");

        tab.TemplateSearchText = string.Empty;
        tab.SelectedTemplateScopeFilter = "Pages";
        tab.SelectedTemplateLibraryFilter = "Atlas Archive";
        tab.SelectedTemplate = tab.FilteredTemplates.Single(value =>
            value.TemplateId == "atlas.archive.page.history.v1");
        tab.TemplatePageName = "Templated History";
        tab.TemplatePageRoute = "/archive/templated-history";
        Require(tab.InsertTemplateCommand.CanExecute(null),
            "A clean project could not create a page from an Atlas starter.");
        tab.InsertTemplateCommand.Execute(null);
        var pageTemplateDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (tab.IsBusy && DateTimeOffset.UtcNow < pageTemplateDeadline)
        {
            await Task.Delay(25);
        }
        Require(
            !tab.IsBusy
            && tab.Project.Pages.Count == 3
            && tab.Document.Route == "/archive/templated-history"
            && tab.Document.Root.Children.Count >= 3
            && tab.Project.Theme.Tokens["color.background"] == "#070d1a"
            && tab.Project.Theme.Tokens["color.accent"] == "#c8963e",
            "The Template Library command did not create and open an Atlas-themed Archive page atomically.");

        await VerifyProjectDocumentsAndLanguageAuthoringAsync(tab, projectRoot);

        const string studioVerification =
            "tab|tools:17|hyperpage-brand|guided-project-foundation|html-scaffold|template-pages|ai-primer|bottom-ai|compact-ai-options|minimal-ai-options|flat-ai-options|direct-ai-implementation|publish-isolation|friendly-labels|plain-language-ui|studio-tab-chrome|icon-first-toolbar|single-row-project-toolbar|canvas-column-toolbar-alignment|equal-docked-toolbar-groups|proportional-toolbar-controls|live-toolbar-resize|raised-side-shelves|responsive-popout|canvas-header-controls|responsive-page-picker|play-preview|active-workspace-modes|default-project-root|common-library|all-html-scope:113|input-states:23|legacy-library:29|search-all|category-filter|tag-first-elements|element-guides|catalog-draft-properties|dynamic-property-editors|disabled-noop-actions|smart-starters|placement-guard|disabled-invalid-placement|schema-inspector|css-suggestions|templates:58|archive-pages:6|common-pages:8|common-sections:12|common-elements:8|intent-taxonomy|web-template-capture|template-search|template-filters|saved-elements|project-template-save|page-starters|page-dialog|grouped-document-menu|styles-section|code-section|project-tree|language-library|statement-builder|monaco-project-documents|catalog-css:154|catalog-js:1270|catalog-web:953|catalog-csharp:130|global-tab-menu|canvas-document-plus|toolbar-document-plus|raised-page-tab|toolbar-gutters|auto-page-open|page-file-types|ai-page-foundation|atlas-theme|canvas-zoom|device-widths|grid-outlines|layer-order|diagnostic-navigation|multi-page|page-metadata|assets|bounded-import|selection-sync|drag-reparent|responsive-resize|expanded-layout|context-actions|clipboard-id-remap|keyboard-history|canvas-default|idempotent-code-pane|closable-code-pane|pane-move|layout-deduplication|collapsible-tool-panels|tool-placement|floating-bounds|read-only-bindings|sanitized-navigation|named-layouts|personal-default|project-override|detach-live-vm|taskbar-safe-maximize|session-recovery|atomic-save";
        Console.WriteLine($"HYPERPAGE_STUDIO_OK|{studioVerification}");
        Console.WriteLine($"VISUAL_PAGE_STUDIO_OK|compat-alias|{studioVerification}");
    }

    private static async Task VerifyProjectDocumentsAndLanguageAuthoringAsync(
        VisualPageStudioTabViewModel tab,
        string projectRoot)
    {
        Require(
            HyperpageDocumentTypeCatalog.Definitions.Count == 8
            && HyperpageDocumentTypeCatalog.Definitions.Count(definition =>
                definition.Section == "Styles") == 4
            && HyperpageDocumentTypeCatalog.Definitions.Count(definition =>
                definition.Section == "Code") == 4,
            "The grouped Styles and Code document-type catalog was incomplete.");

        var css = await tab.CreateProjectDocumentAsync("css", "theme.css");
        var scss = await tab.CreateProjectDocumentAsync("scss", "theme.scss");
        var sass = await tab.CreateProjectDocumentAsync("sass", "theme.sass");
        var less = await tab.CreateProjectDocumentAsync("less", "theme.less");
        var javaScript = await tab.CreateProjectDocumentAsync(
            "javascript",
            "app.js");
        var module = await tab.CreateProjectDocumentAsync(
            "javascript-module",
            "feature.mjs");
        var typeScript = await tab.CreateProjectDocumentAsync(
            "typescript",
            "app.ts");
        var csharp = await tab.CreateProjectDocumentAsync(
            "csharp",
            "Feature.cs");
        var created = new[]
        {
            css, scss, sass, less, javaScript, module, typeScript, csharp
        };
        Require(
            created.All(document => document is not null)
            && created.OfType<HyperpageProjectDocumentViewModel>().All(document =>
                File.Exists(Path.Combine(
                    projectRoot,
                    document.RelativePath.Replace('/', Path.DirectorySeparatorChar))))
            && css!.RelativePath == "src/Pages/wwwroot/styles/theme.css"
            && scss!.RelativePath == "src/Styles/theme.scss"
            && sass!.RelativePath == "src/Styles/theme.sass"
            && less!.RelativePath == "src/Styles/theme.less"
            && javaScript!.RelativePath == "src/Pages/wwwroot/scripts/app.js"
            && module!.RelativePath == "src/Pages/wwwroot/scripts/feature.mjs"
            && typeScript!.RelativePath == "src/Scripts/app.ts"
            && csharp!.RelativePath == "src/Pages/Code/Feature.cs",
            "Project-aware document creation did not write every requested style and code type to its safe default directory.");
        var cssDocument = css!;
        var scssDocument = scss!;
        var sassDocument = sass!;
        var lessDocument = less!;
        var javaScriptDocument = javaScript!;
        var moduleDocument = module!;
        var typeScriptDocument = typeScript!;
        var csharpDocument = csharp!;
        var collocatedJavaScriptStarter = HyperpageDocumentTypeCatalog.CreateStarter(
            HyperpageDocumentTypeCatalog.GetRequired("javascript"),
            tab.Project,
            "FeatureCard.razor.js");
        Require(
            cssDocument.Content.Contains("<link rel=\"stylesheet\"", StringComparison.Ordinal)
            && scssDocument.Content.Contains("compile this file to .css", StringComparison.Ordinal)
            && sassDocument.Content.Contains("Indented Sass", StringComparison.Ordinal)
            && lessDocument.Content.Contains("Less is authoring syntax", StringComparison.Ordinal)
            && javaScriptDocument.Content.Contains("JavaScript runs in the browser", StringComparison.Ordinal)
            && !javaScriptDocument.Content.Contains("export function", StringComparison.Ordinal)
            && moduleDocument.Content.Contains("export function", StringComparison.Ordinal)
            && collocatedJavaScriptStarter.Contains("ES module", StringComparison.Ordinal)
            && collocatedJavaScriptStarter.Contains("export function", StringComparison.Ordinal)
            && typeScriptDocument.Content.Contains("compiled to JavaScript", StringComparison.Ordinal)
            && csharpDocument.Content.Contains("namespace Studio.Verifier;", StringComparison.Ordinal),
            "New document starters did not explain the Razor, CSS, JavaScript, TypeScript, and C# runtime boundaries.");

        var cssCount = tab.AuthoringRecipes.Count(recipe => recipe.Supports("css"));
        var javaScriptCount = tab.AuthoringRecipes.Count(recipe =>
            recipe.Supports("javascript"));
        var csharpCount = tab.AuthoringRecipes.Count(recipe =>
            recipe.Supports("csharp"));
        Require(
            cssCount >= 160
            && javaScriptCount >= 2200
            && csharpCount >= CSharpKeywordCatalogService.ExpectedKeywordCount + 8
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "sass.mixin")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "ts.interface")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "css.razor-isolation")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "js.blazor-module")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "csharp.razor-partial")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "csharp.page-model")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "csharp.js-interop")
            && tab.AuthoringRecipes.Any(recipe => recipe.Id == "csharp.razor-parameter"),
            $"Packaged language inventories or practical patterns were incomplete: css={cssCount}, js={javaScriptCount}, csharp={csharpCount}.");

        tab.RequestPaneContent(cssDocument.ContentId);
        tab.AuthoringSearchText = "Grid layout";
        tab.SelectedAuthoringRecipe = tab.FilteredAuthoringRecipes.Single(recipe =>
            recipe.Id == "css.grid");
        tab.AuthoringFields.Single(field => field.Id == "selector").Value =
            ".verified-grid";
        Require(
            tab.HasActiveAuthoringDocument
            && tab.ActiveAuthoringLanguageId == "css"
            && tab.AuthoringPreview.Contains(".verified-grid", StringComparison.Ordinal)
            && tab.InsertAuthoringSnippetCommand.CanExecute(null),
            "The contextual CSS library and structured statement builder did not follow the active project document.");
        tab.InsertAuthoringSnippetCommand.Execute(null);
        Require(
            cssDocument.Content.Contains(".verified-grid", StringComparison.Ordinal)
            && cssDocument.IsDirty,
            "The structured builder did not insert its generated CSS into the active document.");

        tab.RequestPaneContent(sassDocument.ContentId);
        var sassReference = tab.FilteredAuthoringRecipes.First(recipe =>
            recipe.IsReference);
        Require(
            tab.ActiveAuthoringLanguageId == "sass"
            && tab.ActiveAuthoringLanguageName == "Sass"
            && !sassReference.Template.TrimEnd().EndsWith(';'),
            "Indented Sass did not retain its own label and semicolon-free property syntax.");
        tab.RequestPaneContent(cssDocument.ContentId);
        Require(await tab.SaveProjectAsync() && !cssDocument.IsDirty,
            "A generated project-document edit did not save through the drift-aware project save path.");
        var cssDiskPath = Path.Combine(
            projectRoot,
            cssDocument.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Require(
            (await File.ReadAllTextAsync(cssDiskPath)).Contains(
                ".verified-grid",
                StringComparison.Ordinal),
            "The saved CSS insertion was not persisted to the physical project file.");

        Require(
            await tab.CreateProjectDocumentAsync("css", @"..\escape.css") is null
            && !File.Exists(Path.Combine(projectRoot, "escape.css")),
            "Project-document creation accepted a traversal-like file name.");

        tab.SelectedProjectNode = tab.ProjectTree.Single();
        Require(await tab.CreateProjectDirectoryAsync("Modules")
                && Directory.Exists(Path.Combine(projectRoot, "Modules")),
            "The project sidebar could not create a bounded project directory.");
        await File.WriteAllBytesAsync(
            Path.Combine(projectRoot, "Assets", "sample.bin"),
            [0, 1, 2, 3]);
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, ".visual-page", "managed.cs"),
            "internal sealed class ManagedMarker { }");
        await tab.RefreshProjectTreeAsync();
        var projectNodes = FlattenProjectTree(tab.ProjectTree.Single()).ToList();
        Require(
            projectNodes.Any(node => node.RelativePath == cssDocument.RelativePath)
            && projectNodes.Any(node => node.RelativePath == javaScriptDocument.RelativePath)
            && projectNodes.Any(node => node.RelativePath == csharpDocument.RelativePath)
            && projectNodes.Any(node => node.RelativePath == "Assets/sample.bin")
            && projectNodes.Any(node => node.RelativePath == ".visual-page/managed.cs")
            && projectNodes.Any(node => node.RelativePath == "Modules"),
            "The project tree did not represent the complete mixed-language directory structure.");

        var binary = projectNodes.Single(node =>
            node.RelativePath == "Assets/sample.bin");
        await tab.OpenProjectNodeAsync(binary);
        Require(tab.StatusMessage.Contains("binary format", StringComparison.Ordinal),
            "A binary project item was not retained in the tree with a safe dedicated-viewer boundary.");

        var managedSource = projectNodes.Single(node =>
            node.RelativePath == ".visual-page/managed.cs");
        await tab.OpenProjectNodeAsync(managedSource);
        Require(
            tab.HasActiveAuthoringDocument
            && tab.ActiveProjectDocument?.IsReadOnly == true
            && !tab.CanInsertAuthoringSnippet,
            "Studio-managed source did not retain a readable but insertion-safe boundary.");

        tab.SelectedProjectNode = projectNodes.Single(node =>
            node.RelativePath == cssDocument.RelativePath);
        var oldContentId = cssDocument.ContentId;
        Require(await tab.RenameSelectedProjectNodeAsync("theme-renamed.css")
                && cssDocument.RelativePath == "src/Pages/wwwroot/styles/theme-renamed.css"
                && File.Exists(Path.Combine(
                    projectRoot,
                    "src",
                    "Pages",
                    "wwwroot",
                    "styles",
                    "theme-renamed.css"))
                && !FlattenLayout(tab.Layout.Root).Any(pane =>
                    pane.PaneContents.Contains(oldContentId, StringComparer.Ordinal)),
            "A user-owned project document did not rename safely while updating its open pane identity.");
        Require(await tab.SaveProjectAsync(),
            "The project layout did not save after a project-document rename.");

        var reopenedCatalog = new VisualPageComponentCatalog();
        var reopenedValidator = new VisualPageSchemaValidator(reopenedCatalog);
        var reopenedGenerator = new VisualPageGenerator(
            reopenedCatalog,
            reopenedValidator);
        var reopenedProjectService = new VisualPageProjectService(
            reopenedValidator,
            reopenedGenerator,
            reopenedCatalog);
        var reopenedSession = await reopenedProjectService.OpenAsync(projectRoot);
        var verifierRoot = Path.GetDirectoryName(projectRoot)!;
        using var reopenedPreview = new VisualPagePreviewBuildService(
            new BoundedProcessRunner(),
            reopenedGenerator,
            Path.Combine(verifierRoot, "reopen-preview-cache"));
        using var reopenedHost = new VisualPageLocalHostService(
            new BoundedProcessRunner());
        using var reopenedTab = await VisualPageStudioTabViewModel.CreateAsync(
            reopenedSession,
            reopenedProjectService,
            new VisualPageMutationEngine(reopenedValidator),
            reopenedGenerator,
            reopenedValidator,
            reopenedCatalog,
            new VisualPageTemplateService(
                reopenedCatalog,
                Path.Combine(verifierRoot, "reopen-personal-templates")),
            new VisualPageDesignCanvasRenderer(reopenedCatalog),
            reopenedPreview,
            reopenedHost,
            new VisualPageUserSourceService(),
            new AtlasArchivePublicationService(
                new BoundedProcessRunner(),
                reopenedProjectService,
                reopenedGenerator,
                reopenedValidator,
                Path.Combine(verifierRoot, "reopen-atlas-publications")));
        Require(
            reopenedTab.TryGetProjectDocument(
                cssDocument.ContentId,
                out var restoredDocument)
            && restoredDocument is not null
            && restoredDocument.RelativePath == cssDocument.RelativePath
            && restoredDocument.Content.Contains(
                ".verified-grid",
                StringComparison.Ordinal)
            && FlattenLayout(reopenedTab.Layout.Root).Any(pane =>
                pane.PaneContents.Contains(
                    cssDocument.ContentId,
                    StringComparer.Ordinal)),
            "Saved project-document tabs did not restore from the project layout with their physical content.");
    }

    private static IEnumerable<HyperpageProjectTreeNodeViewModel>
        FlattenProjectTree(HyperpageProjectTreeNodeViewModel root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in FlattenProjectTree(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task VerifyGuidedProjectFoundationAsync(
        string verifierRoot)
    {
        var projectParent = Path.Combine(
            verifierRoot,
            "guided-projects");
        Directory.CreateDirectory(projectParent);
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projectService = new VisualPageProjectService(
            validator,
            generator,
            catalog);
        var templates = new VisualPageTemplateService(
                catalog,
                Path.Combine(verifierRoot, "guided-personal-templates"))
            .BuiltInTemplates
            .Where(template => template.Scope == "page")
            .Take(2)
            .ToList();
        Require(templates.Count == 2,
            "The guided project verifier could not find two page templates.");

        var htmlOptions = new HyperpageProjectCreationOptions
        {
            Foundation = new HyperpageProjectFoundation
            {
                MarkupLanguage = HyperpageMarkupLanguage.Html,
                HostingTarget = HyperpageHostingTarget.WebServer,
                SiteStructure = HyperpageSiteStructure.FixedPages,
                PlannedPageCount = 2,
                UsesTemplates = true,
                CodingMode = HyperpageCodingMode.Manual,
                AiProvider = HyperpageAiProvider.None,
                ClarifyingQuestionsFirst = true
            },
            Pages =
            [
                new HyperpagePageCreationDefinition
                {
                    Name = "Home",
                    Route = "/",
                    Template = templates[0]
                },
                new HyperpagePageCreationDefinition
                {
                    Name = "Research",
                    Route = "/archive/research",
                    Template = templates[1]
                }
            ]
        };
        var htmlSession = await projectService.CreateInDirectoryAsync(
            projectParent,
            "Guided HTML",
            htmlOptions,
            "Guided.Html");
        var htmlRoot = htmlSession.RootPath;
        var secondPage = await projectService.OpenAsync(
            htmlRoot,
            htmlSession.Project.Pages[1].PageId);
        var generatedHome = await File.ReadAllTextAsync(Path.Combine(
            htmlRoot,
            "src",
            "Host",
            "wwwroot",
            "index.html"));
        var generatedResearch = await File.ReadAllTextAsync(Path.Combine(
            htmlRoot,
            "src",
            "Host",
            "wwwroot",
            "archive",
            "research",
            "index.html"));
        var hostProject = await File.ReadAllTextAsync(Path.Combine(
            htmlRoot,
            "src",
            "Host",
            "Host.csproj"));
        var allProjectText = string.Join(
            "\n",
            Directory.EnumerateFiles(
                    htmlRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        Require(
            htmlSession.Project.Foundation.MarkupLanguage ==
                HyperpageMarkupLanguage.Html
            && htmlSession.Project.Foundation.HostingTarget ==
                HyperpageHostingTarget.WebServer
            && htmlSession.Project.Foundation.PlannedPageCount == 2
            && htmlSession.Project.Pages.Count == 2
            && htmlSession.Project.Pages.All(page =>
                !string.IsNullOrWhiteSpace(page.StarterTemplateId)
                && !string.IsNullOrWhiteSpace(page.StarterTemplateName))
            && htmlSession.Page.Root.Children.Count > 0
            && secondPage.Page.Root.Children.Count > 0
            && generatedHome.Contains("<!doctype html>", StringComparison.Ordinal)
            && generatedHome.Contains(
                "/assets/home.custom.js",
                StringComparison.Ordinal)
            && generatedResearch.Contains(
                "data-vp-id=",
                StringComparison.Ordinal)
            && !Directory.Exists(Path.Combine(htmlRoot, "src", "Pages"))
            && !hostProject.Contains(
                "ProjectReference",
                StringComparison.Ordinal)
            && !allProjectText.Contains(
                "Build something worth exploring",
                StringComparison.Ordinal),
            "The guided HTML project did not create two populated template pages, a static host scaffold, or remove the generic starter.");
        using (var htmlPreview = new VisualPagePreviewBuildService(
                   new BoundedProcessRunner(),
                   generator,
                   Path.Combine(verifierRoot, "guided-html-preview-cache")))
        {
            var htmlBuild = await htmlPreview.BuildLatestAsync(htmlSession);
            Require(
                htmlBuild.Outcome == VisualPageBuildOutcome.Succeeded,
                "The generated HTML host did not compile: "
                + htmlBuild.Output);
        }

        var settingsService = new SettingsService(
            Path.Combine(verifierRoot, "guided-ai-app-data"));
        var settings = settingsService.Load();
        settings.Application.CreateTerminalOnStartup = false;
        settings.Explorer.StartupDirectory = projectParent;
        settingsService.Save(settings);
        using var workspace = new MainWindowViewModel(settingsService);
        await workspace.FileExplorer.WhenIdleAsync();
        var aiOptions = new HyperpageProjectCreationOptions
        {
            Foundation = new HyperpageProjectFoundation
            {
                MarkupLanguage = HyperpageMarkupLanguage.RazorPages,
                HostingTarget = HyperpageHostingTarget.Local,
                SiteStructure = HyperpageSiteStructure.DynamicSite,
                PlannedPageCount = 0,
                UsesTemplates = false,
                CodingMode = HyperpageCodingMode.AiAssisted,
                AiProvider = HyperpageAiProvider.Local,
                ClarifyingQuestionsFirst = true
            },
            Pages =
            [
                new HyperpagePageCreationDefinition
                {
                    Name = "Home",
                    Route = "/"
                }
            ]
        };
        var aiTab = await workspace.CreateVisualPageProjectAsync(
            projectParent,
            "Guided AI",
            aiOptions);
        Require(aiTab is not null,
            $"The guided AI project did not open: {workspace.StatusMessage}");
        var assistant = aiTab!.AiAssistant;
        Require(
            aiTab.IsAiAssistantOpen
            && aiTab.HasAiAssistant
            && aiTab.Project.Foundation.SiteStructure ==
                HyperpageSiteStructure.DynamicSite
            && aiTab.Project.Foundation.AiProvider ==
                HyperpageAiProvider.Local
            && aiTab.Document.Root.Children.Count == 0
            && assistant is not null
            && assistant.SelectedProviderModel?.IsLocal == true
            && string.Equals(
                Path.GetFullPath(assistant.WorkingDirectoryDisplay),
                Path.GetFullPath(aiTab.ProjectRoot),
                StringComparison.OrdinalIgnoreCase)
            && assistant.Messages.Count == 1
            && assistant.Messages[0].Text ==
                "Let's Begin. Describe the project you'd like to build..."
            && aiTab.ProjectFoundationSummary.Contains(
                "Dynamic site",
                StringComparison.Ordinal)
            && !File.ReadAllText(Path.Combine(
                    aiTab.ProjectRoot,
                    aiTab.Project.Pages[0].DocumentPath)).Contains(
                "Build something worth exploring",
                StringComparison.Ordinal),
            "The guided AI project did not open a blank dynamic foundation with a local-model conversation and the required first prompt.");

        aiTab.ToggleAiAssistantCommand.Execute(null);
        Require(
            aiTab.IsAiAssistantClosed
            && !aiTab.IsAiAssistantOpen
            && aiTab.AiAssistantPanelHeight.Value == 0
            && aiTab.AiAssistantSplitterHeight.Value == 0,
            "The collapsed AI assistant continued reserving workspace height.");
        aiTab.ToggleAiAssistantCommand.Execute(null);
        Require(
            aiTab.IsAiAssistantOpen
            && !aiTab.IsAiAssistantClosed
            && aiTab.AiAssistantPanelHeight.Value == 300
            && aiTab.AiAssistantSplitterHeight.Value == 5,
            "The AI assistant did not reopen from the bottom edge control.");

        Console.WriteLine(
            "HYPERPAGE_PROJECT_FOUNDATION_OK|html|web-server|fixed-pages:2|templates|manual|dynamic-site|ai-local|clarifying-first|blank-no-junk");
    }

    private static async Task VerifyPageCreationFileTypesAsync(
        string verifierRoot)
    {
        var projectRoot = Path.Combine(
            verifierRoot,
            "page-creation-file-types");
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projectService = new VisualPageProjectService(
            validator,
            generator,
            catalog);
        var session = await projectService.CreateAsync(
            projectRoot,
            "Page Creation Types",
            "Page.Creation.Types");

        session = await projectService.AddPageAsync(
            session,
            "Static Landing",
            "/static-landing",
            fileType: HyperpagePageFileType.Html);
        var htmlEntry = session.Project.Pages.Single(page =>
            page.PageId == session.Page.PageId);
        var htmlPath = Path.Combine(
            session.RootPath,
            htmlEntry.ComponentPath.Replace(
                '/',
                Path.DirectorySeparatorChar));

        session = await projectService.AddPageAsync(
            session,
            "Account Details",
            "/account-details",
            fileType: HyperpagePageFileType.Cshtml);
        var cshtmlEntry = session.Project.Pages.Single(page =>
            page.PageId == session.Page.PageId);
        var cshtmlPath = Path.Combine(
            session.RootPath,
            cshtmlEntry.ComponentPath.Replace(
                '/',
                Path.DirectorySeparatorChar));

        session = await projectService.AddPageAsync(
            session,
            "Interactive Dashboard",
            "/interactive-dashboard",
            fileType: HyperpagePageFileType.RazorPage);
        var razorEntry = session.Project.Pages.Single(page =>
            page.PageId == session.Page.PageId);
        var razorPath = Path.Combine(
            session.RootPath,
            razorEntry.ComponentPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        var razorCodeBehindPath = Path.Combine(
            session.RootPath,
            VisualPageProjectService.UserCSharpPath(
                    session.Project,
                    session.Page)
                .Replace('/', Path.DirectorySeparatorChar));

        var html = await File.ReadAllTextAsync(htmlPath);
        var cshtml = await File.ReadAllTextAsync(cshtmlPath);
        var razor = await File.ReadAllTextAsync(razorPath);
        Require(
            htmlEntry.FileType == HyperpagePageFileType.Html
            && cshtmlEntry.FileType == HyperpagePageFileType.Cshtml
            && razorEntry.FileType == HyperpagePageFileType.RazorPage
            && htmlEntry.ComponentPath.EndsWith(
                "/index.html",
                StringComparison.Ordinal)
            && cshtmlEntry.ComponentPath.EndsWith(
                ".cshtml",
                StringComparison.Ordinal)
            && razorEntry.ComponentPath.EndsWith(
                ".razor",
                StringComparison.Ordinal)
            && html.Contains("<!doctype html>", StringComparison.Ordinal)
            && cshtml.Contains(
                "@page \"/account-details\"",
                StringComparison.Ordinal)
            && razor.Contains(
                "@page \"/interactive-dashboard\"",
                StringComparison.Ordinal)
            && File.Exists(razorCodeBehindPath)
            && string.IsNullOrEmpty(
                VisualPageProjectService.UserCSharpPath(
                    session.Project,
                    new VisualPageDocument
                    {
                        FileType = HyperpagePageFileType.Cshtml
                    })),
            "The shared Create Page foundation did not emit distinct HTML, CSHTML, and Razor files with the expected code-behind boundary.");

        using var preview = new VisualPagePreviewBuildService(
            new BoundedProcessRunner(),
            generator,
            Path.Combine(verifierRoot, "page-creation-preview-cache"));
        var build = await preview.BuildLatestAsync(session);
        Require(
            build.Outcome == VisualPageBuildOutcome.Succeeded,
            "The mixed HTML, CSHTML, and Razor page project did not compile: "
            + build.Output);
        Console.WriteLine(
            "HYPERPAGE_PAGE_CREATION_OK|html|cshtml|razor|code-behind-boundary|mixed-project-build");
    }

    private static async Task VerifyWorkspaceTabsAsync(string verifierRoot)
    {
        var projectsDirectory = Path.Combine(verifierRoot, "workspace-projects");
        Directory.CreateDirectory(projectsDirectory);
        var existingFile = Path.Combine(projectsDirectory, "keep-me.txt");
        await File.WriteAllTextAsync(existingFile, "Existing location content.");
        var settingsService = new SettingsService(
            Path.Combine(verifierRoot, "workspace-app-data"));
        var settings = settingsService.Load();
        settings.Application.CreateTerminalOnStartup = false;
        settings.Explorer.StartupDirectory = projectsDirectory;
        settingsService.Save(settings);

        using var workspace = new MainWindowViewModel(settingsService);
        await workspace.FileExplorer.WhenIdleAsync();
        var first = await workspace.CreateVisualPageProjectAsync(
            projectsDirectory,
            "First project");
        Require(first is not null,
            $"The first Hyperpage project did not open: {workspace.StatusMessage}");
        first!.UserCss += "\n/* unsaved first project */";
        first.CanvasZoomPercent = 125;
        var firstCss = first.UserCss;
        var firstManifest = await File.ReadAllTextAsync(Path.Combine(
            first.ProjectRoot,
            VisualPageProjectService.ProjectManifestFileName));

        var second = await workspace.CreateVisualPageProjectAsync(
            projectsDirectory,
            "Second project");
        Require(second is not null
                && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 2
                && ReferenceEquals(workspace.ActiveWorkspaceTab, second),
            "Creating a second named Hyperpage project in the same location did not open a new active tab: "
            + workspace.StatusMessage);
        Require(first.ProjectRoot == Path.Combine(projectsDirectory, "First project")
                && second!.ProjectRoot == Path.Combine(projectsDirectory, "Second project")
                && first.ProjectId != second.ProjectId
                && first.SessionId != second.SessionId
                && !ReferenceEquals(first.Project, second.Project)
                && !ReferenceEquals(first.Document, second.Document),
            "Hyperpage projects did not receive independent folders, sessions, and document models.");
        Require(first.HasUnsavedChanges
                && first.UserCss == firstCss
                && !second!.HasUnsavedChanges
                && !second.UserCss.Contains("unsaved first project", StringComparison.Ordinal)
                && second.CanvasZoomPercent == 100,
            "Opening another Hyperpage tab changed or shared the first project's unsaved state.");

        second!.UserCss += "\n/* saved second project */";
        Require(await second.SaveProjectAsync()
                && !second.HasUnsavedChanges
                && first.HasUnsavedChanges
                && first.UserCss == firstCss,
            "Saving one Hyperpage tab affected another project's unsaved edits.");

        var third = await workspace.CreateVisualPageProjectAsync(
            projectsDirectory,
            "Third project");
        Require(third is not null
                && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 3
                && ReferenceEquals(workspace.ActiveWorkspaceTab, third),
            "The workspace could not keep three Hyperpage projects open simultaneously.");

        var existing = await workspace.OpenVisualPageProjectAsync(second.ProjectRoot);
        Require(ReferenceEquals(existing, second)
                && ReferenceEquals(workspace.ActiveWorkspaceTab, second)
                && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 3,
            "Opening an already-open project did not select its own existing tab.");

        var duplicate = await workspace.CreateVisualPageProjectAsync(
            projectsDirectory,
            "First project");
        Require(duplicate is null
                && workspace.StatusMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 3
                && ReferenceEquals(workspace.ActiveWorkspaceTab, second)
                && first.UserCss == firstCss
                && firstManifest == await File.ReadAllTextAsync(Path.Combine(
                    first.ProjectRoot,
                    VisualPageProjectService.ProjectManifestFileName)),
            "A duplicate project name did not report a safe failure while preserving the open projects.");

        var fileCollision = await workspace.CreateVisualPageProjectAsync(
            projectsDirectory,
            "keep-me.txt");
        Require(fileCollision is null
                && workspace.StatusMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                && await File.ReadAllTextAsync(existingFile) == "Existing location content.",
            "Creating a project over an existing file did not preserve it and report the name conflict.");
        foreach (var invalidName in new[]
                 {
                     ".", "..", "../escaped-project", @"..\escaped-project",
                     "First project.", "First project:stream",
                     Path.Combine(verifierRoot, "escaped-project")
                 })
        {
            Require(await workspace.CreateVisualPageProjectAsync(
                        projectsDirectory,
                        invalidName) is null
                    && workspace.StatusMessage.Contains("folder name", StringComparison.OrdinalIgnoreCase),
                $"An invalid project folder name was not rejected: {invalidName}");
        }
        Require(!Directory.Exists(Path.Combine(verifierRoot, "escaped-project"))
                && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 3
                && ReferenceEquals(workspace.ActiveWorkspaceTab, second),
            "An invalid creation request escaped its parent folder or changed the selected tab.");

        workspace.ActiveWorkspaceTab = first;
        Require(first.CanvasZoomPercent == 125 && first.UserCss == firstCss,
            "Switching back to a Hyperpage project lost its tab state.");
        Require(workspace.CloseWorkspaceTab(third!)
                && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 2
                && ReferenceEquals(workspace.ActiveWorkspaceTab, first)
                && first.HasUnsavedChanges,
            "Closing another Hyperpage tab affected the active project.");

        Console.WriteLine(
            "VISUAL_PAGE_WORKSPACE_TABS_OK|three-projects|shared-location|independent-state|save-isolation|existing-project-focus|duplicate-name-guard|path-boundary|files-preserved|close-isolation");
    }

    private static void VerifyReadOnlyStudioBindings()
    {
        var relativePath = Path.Combine(
            "src",
            "Hypershell",
            "VisualPageStudioView.xaml");
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };
        string? xamlPath = null;
        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(Path.GetFullPath(candidate));
            while (current is not null)
            {
                var path = Path.Combine(current.FullName, relativePath);
                if (File.Exists(path))
                {
                    xamlPath = path;
                    break;
                }
                current = current.Parent;
            }
            if (xamlPath is not null)
            {
                break;
            }
        }
        Require(xamlPath is not null,
            "The Studio XAML could not be located for binding verification.");
        var document = XDocument.Load(xamlPath!);
        var xaml = File.ReadAllText(xamlPath!);
        Require(
            xaml.Contains(
                "ItemsSource=\"{Binding FilteredTemplates}\"",
                StringComparison.Ordinal)
            && xaml.Contains(
                "SelectedTemplateScopeFilter",
                StringComparison.Ordinal)
            && xaml.Contains(
                "SelectedTemplateLibraryFilter",
                StringComparison.Ordinal)
            && xaml.Contains(
                "SelectedTemplateCategoryFilter",
                StringComparison.Ordinal)
            && xaml.Contains(
                "Header=\"Elements\" IsExpanded=\"False\"",
                StringComparison.Ordinal)
            && xaml.Contains(
                "SelectedTemplateElementsText, Mode=OneWay",
                StringComparison.Ordinal)
            && xaml.Contains(
                "SelectedTemplateStructure, Mode=OneWay",
                StringComparison.Ordinal)
            && xaml.Contains(
                "Header=\"Template\"",
                StringComparison.Ordinal)
            && xaml.Contains(
                "CreateTemplateFromSelectionClick",
                StringComparison.Ordinal)
            && xaml.Contains(
                "TemplatePageRoute",
                StringComparison.Ordinal)
            && xaml.Contains(
                "Click=\"CreateTemplateClick\"",
                StringComparison.Ordinal)
            && xaml.Contains(
                "Content=\"Insert\"",
                StringComparison.Ordinal)
            && new[]
            {
                "Find a starting point",
                "Save selection as a template",
                "Create from a website with AI",
                "TemplateCatalogSummary",
                "SelectedTemplateUsageText",
                "ItemsSource=\"{Binding SelectedTemplateElements, Mode=OneWay}\""
            }.All(token => !xaml.Contains(token, StringComparison.Ordinal)),
            "The compact Template Library UI did not retain filters, selection details, page-start controls, or unified create actions.");
        Require(
            new[]
            {
                "StudioToolbarGroupStyle",
                "StudioToolbarToggleButtonStyle",
                "IsCanvasOnlyWorkspace",
                "IsCodeRightWorkspace",
                "IsCodeBelowWorkspace",
                "AutomationProperties.Name=\"Build app preview\"",
                "AutomationProperties.Name=\"Current project page\"",
                "AutomationProperties.Name=\"Editing breakpoint\"",
                "AutomationProperties.Name=\"Canvas viewport width\"",
                "AutomationProperties.Name=\"Canvas zoom\"",
                "SelectionChanged=\"ProjectPageSelectionChanged\"",
                "Click=\"NewProjectDocumentClick\"",
                "ToolTip=\"Build App Preview\"",
                "Command=\"{Binding DetachCommand}\""
            }.All(token => xaml.Contains(token, StringComparison.Ordinal))
            && new[]
            {
                "Text=\"PAGE\"",
                "Text=\"WORKSPACE\"",
                "Text=\"EDIT FOR\"",
                "Text=\"WIDTH\"",
                "Text=\"ZOOM\"",
                "Content=\"Canvas\"",
                "Content=\"Code right\"",
                "Content=\"Code below\"",
                "Content=\"Grid\"",
                "Content=\"Boxes\"",
                "OpenSelectedPageCommand",
                "ResetLayoutCommand",
                "ResetCanvasViewCommand",
                "Command=\"{Binding UndoCommand}\"",
                "Command=\"{Binding RedoCommand}\""
            }.All(token => !xaml.Contains(token, StringComparison.Ordinal)),
            "The Studio toolbar did not retain its edge-aligned icon groups, automatic page selection, page dialog, compact build action, or keyboard-only history controls.");
        var readOnlyProperties = typeof(VisualPageStudioTabViewModel)
            .GetProperties()
            .Where(property => !property.CanWrite)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var unsafeBindings = document.Descendants()
            .Where(element => element.Name.LocalName is "TextBox" or "Run")
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Where(value => value.StartsWith("{Binding ", StringComparison.Ordinal))
            .Select(value => new
            {
                Value = value,
                Property = value[9..]
                    .Split([',', '}'], 2, StringSplitOptions.TrimEntries)[0]
            })
            .Where(binding => readOnlyProperties.Contains(binding.Property)
                              && !binding.Value.Contains(
                                  "Mode=OneWay",
                                  StringComparison.Ordinal))
            .ToList();
        Require(unsafeBindings.Count == 0,
            "A read-only Studio TextBox retained the default TwoWay binding mode and can crash when a tool is reparented.");

        var studioTabHosts = document.Descendants()
            .Where(element => element.Name.LocalName == "TabControl"
                              && element.Attributes().Any(attribute =>
                                  attribute.Name.LocalName == "Name"
                                  && attribute.Value is
                                      "LeftToolTabs" or "RightToolTabs"))
            .ToList();
        var studioButtonStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                               && element.Attributes().Any(attribute =>
                                   attribute.Name.LocalName == "Key"
                                   && attribute.Value ==
                                   "StudioToolButtonStyle"));
        var studioPaneCloseStyle = document.Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "Style"
                                        && element.Attributes().Any(attribute =>
                                            attribute.Name.LocalName == "Key"
                                            && attribute.Value ==
                                            "StudioPaneCloseButtonStyle"));
        var studioPanelToggleStyle = document.Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "Style"
                                        && element.Attributes().Any(attribute =>
                                            attribute.Name.LocalName == "Key"
                                            && attribute.Value ==
                                            "StudioPanelToggleButtonStyle"));
        var studioPanelHeaderSelectorStyle = document.Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "Style"
                                        && element.Attributes().Any(attribute =>
                                            attribute.Name.LocalName == "Key"
                                            && attribute.Value ==
                                            "StudioPanelHeaderSelectorStyle"));
        var studioPanelToggleHeight = studioPanelToggleStyle?.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Height")
            ?.Attribute("Value")?.Value;
        var studioPanelHeaderSelectorHeight =
            studioPanelHeaderSelectorStyle?.Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value == "Height")
                ?.Attribute("Value")?.Value;
        var studioPanelHeaderSelectors = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "ComboBox"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Name"
                    && attribute.Value is "LeftToolSelector"
                        or "RightToolSelector"))
            .ToList();
        var tabHeaders = document.Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName == "Header")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        var bindingValues = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToList();
        var aiAssistantToggleButtons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button"
                              && element.Attributes().Any(attribute =>
                                  attribute.Name.LocalName == "Command"
                                  && attribute.Value.Contains(
                                      "ToggleAiAssistantCommand",
                                       StringComparison.Ordinal)))
            .ToList();
        var boundaryToggleButtons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button"
                              && element.Attributes().Any(attribute =>
                                  attribute.Name.LocalName == "Style"
                                  && attribute.Value.Contains(
                                      "BoundaryToggleButtonStyle",
                                      StringComparison.Ordinal)))
            .ToList();
        var edgeRevealHostNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "StudioLeftPanelEdgeRevealHost",
            "StudioRightPanelEdgeRevealHost",
            "AiAssistantBottomEdgeRevealHost"
        };
        var studioEdgeRevealHosts = document.Descendants()
            .Where(element => element.Name.LocalName == "Popup"
                              && element.Attributes().Any(attribute =>
                                  attribute.Name.LocalName == "Name"
                                  && edgeRevealHostNames.Contains(
                                      attribute.Value)))
            .ToList();
        var edgeRevealStateBindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StudioLeftPanelEdgeRevealHost"] = "{Binding IsLeftToolPanelClosed, Mode=OneWay}",
            ["StudioRightPanelEdgeRevealHost"] = "{Binding IsRightToolPanelClosed, Mode=OneWay}",
            ["AiAssistantBottomEdgeRevealHost"] = "{Binding IsAiAssistantClosed, Mode=OneWay}"
        };
        bool HasFocusedPropertyContract(XElement element, string property, string standardValue, string focusedValue)
        {
            var style = element.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal))?
                .Elements().SingleOrDefault(child => child.Name.LocalName == "Style");
            if (style is null) return false;
            // With editor mode false, the base setter retains the standard Studio binding/value.
            // Only focused editor mode overrides it, so no local value may defeat that override.
            return element.Attribute(property) is null
                && style.Elements().Any(child => child.Name.LocalName == "Setter"
                    && child.Attribute("Property")?.Value == property && child.Attribute("Value")?.Value == standardValue)
                && style.Descendants().Any(child => child.Name.LocalName == "DataTrigger"
                    && child.Attribute("Binding")?.Value == "{Binding IsHyperlibEditor}"
                    && child.Attribute("Value")?.Value == "True"
                    && child.Elements().Any(setter => setter.Name.LocalName == "Setter"
                        && setter.Attribute("Property")?.Value == property && setter.Attribute("Value")?.Value == focusedValue));
        }
        var studioCentralWorkspace = document.Descendants().Single(element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName == "Name" && attribute.Value == "CentralWorkspaceHost"));
        var edgeRevealButtonNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "StudioLeftPanelReopenButton",
            "StudioRightPanelReopenButton",
            "AiAssistantReopenButton"
        };
        var studioEdgeRevealButtons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button"
                              && element.Attributes().Any(attribute =>
                                  attribute.Name.LocalName == "Name"
                                  && edgeRevealButtonNames.Contains(
                                      attribute.Value)))
            .ToList();
        var xamlText = File.ReadAllText(xamlPath!);
        var codeBehind = File.ReadAllText(xamlPath + ".cs");
        var sourceDirectory = Path.GetDirectoryName(xamlPath!)!;
        var mainWindowXaml = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "MainWindow.xaml"));
        var mainWindowCodeBehind = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "MainWindow.xaml.cs"));
        var creationDialogXaml = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpageProjectCreationDialog.xaml"));
        var creationDialogCodeBehind = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpageProjectCreationDialog.xaml.cs"));
        var pageCreationDialogXaml = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpagePageCreationDialog.xaml"));
        var pageCreationDialogCodeBehind = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpagePageCreationDialog.xaml.cs"));
        var templateCreationDialogXaml = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpageTemplateCreationDialog.xaml"));
        var templateCreationDialogCodeBehind = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpageTemplateCreationDialog.xaml.cs"));
        var assetImportDialogXaml = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "HyperpageAssetImportDialog.xaml"));
        var projectServiceSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "Services",
            "VisualPageProjectService.cs"));
        var languageAuthoringSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "Services",
            "HyperpageLanguageAuthoringCatalog.cs"));
        var mainWindowViewModelSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "ViewModels",
            "MainWindowViewModel.cs"));
        var assistantViewModelSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "ViewModels",
            "WorkspaceAiAssistantViewModel.cs"));
        var studioViewModelSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "ViewModels",
            "VisualPageStudioTabViewModel.cs"));
        var conversationSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "Services",
            "WorkspaceAiConversation.cs"));
        // The content-aware HyperLib workspace uses explicit action names and
        // explanatory copy; its flows have a separate native UI verifier.
        // Keep the compact-copy contract for every existing general Studio tool.
        var compactStudioElements = document.Descendants().Where(element =>
            !element.AncestorsAndSelf().Any(parent => parent.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value is "HyperlibToolbar" or "HyperlibToolTab")));
        var verboseStudioLabels = compactStudioElements
            .SelectMany(element => element.Attributes()
                .Where(attribute => attribute.Name.LocalName is "Text" or "ToolTip")
                .Select(attribute => attribute.Value))
            .Where(value => !value.StartsWith("{Binding", StringComparison.Ordinal)
                            && Regex.Matches(value, @"[\p{L}\p{N}]+")
                                .Count > 3)
            .ToList();
        var verboseStudioButtons = compactStudioElements
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName == "Content")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value)
                            && !value.StartsWith("{Binding", StringComparison.Ordinal)
                            && Regex.Matches(value, @"[\p{L}\p{N}]+").Count > 1)
            .ToList();
        Require(
            verboseStudioLabels.Count == 0
            && verboseStudioButtons.Count == 0
            && !xamlText.Contains("Selected asset", StringComparison.Ordinal)
            && !xamlText.Contains("ApplyAssetAltTextCommand", StringComparison.Ordinal)
            && !xamlText.Contains("Diagnostics.Count", StringComparison.Ordinal)
            && !xamlText.Contains("<Run Text=\"rev ", StringComparison.Ordinal)
            && !xamlText.Contains("ADD &amp; ORGANIZE", StringComparison.Ordinal)
            && !xamlText.Contains("EDIT &amp; OUTPUT", StringComparison.Ordinal)
            && xamlText.Contains("Content=\"Import\"", StringComparison.Ordinal)
            && xamlText.Contains("Content=\"Use\"", StringComparison.Ordinal)
            && assetImportDialogXaml.Contains("x:Name=\"AssetNameTextBox\"", StringComparison.Ordinal)
            && assetImportDialogXaml.Contains("x:Name=\"AltTextBox\"", StringComparison.Ordinal)
            && codeBehind.Contains("HyperpageAssetImportDialog", StringComparison.Ordinal)
            && codeBehind.Contains("metadataDialog.AssetName", StringComparison.Ordinal),
            "The minimal Studio copy, asset-import metadata flow, count removal, or revision-bar removal regressed.");
        var ignoredSourceSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Assets{Path.DirectorySeparatorChar}EditorHost{Path.DirectorySeparatorChar}"
        };
        var staleBrandFiles = Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".xaml")
            .Where(path => !ignoredSourceSegments.Any(segment => path.Contains(
                segment,
                StringComparison.OrdinalIgnoreCase)))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("Visual Page Studio", StringComparison.OrdinalIgnoreCase)
                       || source.Contains("Visual Page project", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(sourceDirectory, path))
            .ToList();
        Require(
            staleBrandFiles.Count == 0,
            "Product-facing source still contains the retired Visual Page branding: "
            + string.Join(", ", staleBrandFiles));
        Require(
            mainWindowXaml.Contains("Header=\"Create project\"", StringComparison.Ordinal)
            && mainWindowXaml.Contains("Header=\"Open project\"", StringComparison.Ordinal)
            && mainWindowXaml.Contains("Header=\"Open file\"", StringComparison.Ordinal)
            && mainWindowXaml.Contains("Click=\"NewVisualPageProjectClick\"", StringComparison.Ordinal)
            && mainWindowXaml.Contains("Click=\"OpenVisualPageProjectClick\"", StringComparison.Ordinal)
            && mainWindowXaml.Contains("Header=\"Detach\"", StringComparison.Ordinal)
            && mainWindowXaml.Contains("Click=\"DetachVisualPageStudioClick\"", StringComparison.Ordinal)
            && xamlText.Contains(
                "AutomationProperties.Name=\"Open Studio in separate window\"",
                StringComparison.Ordinal)
            && codeBehind.Contains("Hyperpage Studio ·", StringComparison.Ordinal)
            && projectServiceSource.Contains(
                    "Text = \"Hyperpage Studio\"",
                    StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(
                sourceDirectory,
                "Services",
                "WorkspaceAiProvider.cs")).Contains(
                    "active Hyperpage Studio tab",
                    StringComparison.Ordinal),
            "Hyperpage Studio was not wired through workspace commands, detached windows, generated content, and AI guidance.");
        Require(
            studioTabHosts.Count == 2
            && studioTabHosts.All(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Style"
                && attribute.Value.Contains(
                    "StudioDockedToolTabControlStyle",
                    StringComparison.Ordinal)))
            && studioButtonStyle.Descendants().Any(element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Width"
                && element.Attribute("Value")?.Value == "Auto")
             && codeBehind.Contains(
                 "Resources[\"StudioTabControlStyle\"]",
                 StringComparison.Ordinal),
            "Studio panes did not retain the header-dropdown content host or flexible button sizing.");
        Require(
            HasFocusedPropertyContract(studioCentralWorkspace, "Margin", "0,50,0,0", "0")
            && HasFocusedPropertyContract(document.Root!.Elements().Single(element => element.Name.LocalName == "Grid")
                .Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions")
                .Elements().First(element => element.Name.LocalName == "RowDefinition"), "Height", "50", "0")
            && HasFocusedPropertyContract(document.Descendants().Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "HyperlibToolbar")), "Visibility",
                "{Binding HasHyperlibAuthoring, Converter={StaticResource BooleanToVisibilityConverter}}", "Collapsed"),
            "Focused Hyperlib editing did not remove its redundant toolbar and margin while preserving standard Studio layout defaults.");
        Require(
            studioPaneCloseStyle is not null
            && studioPanelToggleStyle is not null
            && studioEdgeRevealHosts.Count == 3
            && studioEdgeRevealHosts.All(element =>
                element.Attribute("Placement")?.Value == "Custom"
                && element.Attribute("AllowsTransparency")?.Value == "True"
                && element.Attribute("StaysOpen")?.Value == "True"
                && HasFocusedPropertyContract(element, "IsOpen", edgeRevealStateBindings[element.Attributes()
                    .Single(attribute => attribute.Name.LocalName == "Name").Value], "False"))
            && studioEdgeRevealButtons.Count == 3
            && studioEdgeRevealButtons.Count(button =>
                button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style"
                    && attribute.Value ==
                        "{StaticResource WorkspaceEdgeRevealButtonStyle}")) == 2
            && studioEdgeRevealButtons.Count(button =>
                button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style"
                    && attribute.Value ==
                        "{StaticResource WorkspaceBottomEdgeRevealButtonStyle}")) == 1
            && bindingValues.Count(value => value.Contains(
                "ToggleLeftToolPanelCommand",
                StringComparison.Ordinal)) == 2
            && bindingValues.Count(value => value.Contains(
                "ToggleRightToolPanelCommand",
                StringComparison.Ordinal)) == 2
            && bindingValues.Any(value => value.Contains(
                "LeftToolPanelWidth",
                StringComparison.Ordinal))
            && bindingValues.Any(value => value.Contains(
                "RightToolPanelWidth",
                StringComparison.Ordinal))
            && xamlText.Contains(
                "Width=\"{Binding LeftToolPanelWidth}\" MinWidth=\"0\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Width=\"{Binding RightToolPanelWidth}\" MinWidth=\"0\"",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "ConfigureEdgeRevealPopups()",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "CustomPopupPlacementCallback",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "PlacePopupAtBottomEdge",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "StudioPanelGrid.SizeChanged += OnStudioPanelGridSizeChanged",
                StringComparison.Ordinal)
            && tabHeaders.IsSupersetOf(
            [
                "Structure",
                "Properties",
                "Appearance",
                "Data",
                "Actions",
                "Preview",
                "Host"
            ])
            && xaml.Contains(
                "StudioPanelHeaderSelectorStyle",
                StringComparison.Ordinal)
            && studioPanelHeaderSelectorStyle is not null
            && studioPanelToggleHeight == "32"
            && studioPanelHeaderSelectorHeight == "34"
            && boundaryToggleButtons.Count == 3
            && xaml.Contains("MaxWidth\" Value=\"180\"", StringComparison.Ordinal)
            && xaml.Contains("BorderThickness\" Value=\"0\"", StringComparison.Ordinal)
            && studioPanelHeaderSelectors.Count == 2
            && studioPanelHeaderSelectors.All(element =>
                element.Attribute("Margin")?.Value == "4")
            && studioPanelHeaderSelectors.All(element =>
                element.Parent?.Parent?.Elements()
                    .FirstOrDefault(child =>
                        child.Name.LocalName == "Grid.RowDefinitions")
                    ?.Elements()
                    .FirstOrDefault(child =>
                        child.Name.LocalName == "RowDefinition")
                    ?.Attribute("Height")?.Value == "46")
            && studioPanelHeaderSelectorStyle.Elements().Any(element =>
                element.Name.LocalName == "Setter"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Property"
                    && attribute.Value == "FontFamily")
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Value"
                    && attribute.Value == "{StaticResource UiFontFamily}"))
            && studioPanelHeaderSelectorStyle.Elements().Any(element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "FontSize"
                && element.Attribute("Value")?.Value == "15")
            && studioPanelHeaderSelectorStyle.Elements().Any(element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Background"
                && element.Attribute("Value")?.Value ==
                "Transparent")
            && studioPanelHeaderSelectorStyle.Descendants().Any(element =>
                element.Name.LocalName == "Border"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Name"
                    && attribute.Value == "ArrowSurface")
                && element.Attribute("Width")?.Value == "24"
                && element.Attribute("Background")?.Value ==
                "Transparent")
            && studioPanelHeaderSelectorStyle.Descendants().Any(element =>
                element.Name.LocalName == "TextBlock"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Foreground"
                    && attribute.Value == "{TemplateBinding Foreground}")
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "FontFamily"
                    && attribute.Value == "{TemplateBinding FontFamily}"))
            && xaml.Contains(
                "ItemsSource=\"{Binding ElementName=LeftToolTabs, Path=Items}\"",
                StringComparison.Ordinal)
            && xaml.Contains(
                "ItemsSource=\"{Binding ElementName=RightToolTabs, Path=Items}\"",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "ClosePane(node.NodeId)",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "Close code pane",
                StringComparison.Ordinal),
            "Studio boundary controls, compact tool selectors, or the in-pane close affordance were not wired into the view.");
        Require(
            new[]
            {
                "Template from code",
                "Template from selection",
                "Template from URL"
            }.All(token => templateCreationDialogCodeBehind.Contains(
                token,
                StringComparison.Ordinal))
            && new[]
            {
                "Converted template output"
            }.All(token => templateCreationDialogXaml.Contains(
                token,
                StringComparison.Ordinal))
            && templateCreationDialogCodeBehind.Contains(
                "AI OUTPUT",
                StringComparison.Ordinal)
            && templateCreationDialogCodeBehind.Contains(
                "AnalyzeTemplateCode",
                StringComparison.Ordinal)
            && templateCreationDialogCodeBehind.Contains(
                "SaveSelectedAsTemplateAsync",
                StringComparison.Ordinal)
            && templateCreationDialogCodeBehind.Contains(
                "StartWebTemplateCaptureAsync",
                StringComparison.Ordinal),
            "The unified Create Template dialog did not cover code, selection, and reviewed AI URL workflows.");
        Require(
            mainWindowCodeBehind.Split(
                nameof(VisualPageProjectService.EnsureDefaultProjectsDirectory),
                StringSplitOptions.None).Length >= 3,
            "New and open Hyperpage project pickers do not both start in the default projects directory.");
        var publishTab = document.Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "TabItem"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Header"
                    && attribute.Value == "Publish"));
        var projectPagesToolbar = document.Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Border"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "ToolTip"
                    && attribute.Value == "Project pages"));
        var responsiveCanvasToolbar = document.Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Border"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "ToolTip"
                    && attribute.Value == "Responsive canvas"));
        var canvasLayoutToolbar = document.Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Border"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "ToolTip"
                    && attribute.Value == "Canvas layout"));
        var alignedToolbarHost = projectPagesToolbar?.Parent;
        var alignedToolbarShell = alignedToolbarHost?.Parent;
        var responsiveToolbarGrid = responsiveCanvasToolbar?
            .Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Grid");
        var alignedToolbarHostColumns = alignedToolbarHost?
            .Elements()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Grid.ColumnDefinitions")?
            .Elements()
            .ToList() ?? [];
        var alignedToolbarShellColumns = alignedToolbarShell?
            .Elements()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Grid.ColumnDefinitions")?
            .Elements()
            .ToList() ?? [];
        Require(
            new[]
            {
                "Text=\"Language\"",
                "Text=\"Hosting\"",
                "Text=\"Pages\"",
                "Text=\"Templates\"",
                "Text=\"Mode\"",
                "HtmlRadio",
                "RazorRadio",
                "LocalHostingRadio",
                "WebServerHostingRadio",
                "DynamicSiteRadio",
                "TemplatesYesRadio",
                "AiCodingRadio",
                "OpenAiProviderRadio",
                "LocalAiProviderRadio"
            }.All(token => creationDialogXaml.Contains(
                token,
                StringComparison.Ordinal))
            && creationDialogCodeBehind.Contains(
                "HyperpageProjectCreationOptions",
                StringComparison.Ordinal)
            && creationDialogCodeBehind.Contains(
                "PageDrafts.Select",
                StringComparison.Ordinal)
            && mainWindowCodeBehind.Contains(
                "new HyperpageProjectCreationDialog",
                StringComparison.Ordinal)
            && mainWindowCodeBehind.Contains(
                "creationDialog.CreationOptions",
                StringComparison.Ordinal),
            "The New Hyperpage project command did not use the complete guided foundation form.");
        Require(
            new[]
            {
                "Page name",
                "Page template",
                "File type",
                "Describe this page",
                "Create page"
            }.All(token => pageCreationDialogXaml.Contains(
                token,
                StringComparison.Ordinal))
            && new[]
            {
                "Static HTML (.html)",
                "Razor markup (.cshtml)",
                "Razor page (.razor)",
                "HyperpagePageCreationRequest",
                "ShowAndCreateAsync"
            }.All(token => pageCreationDialogCodeBehind.Contains(
                token,
                StringComparison.Ordinal))
            && xamlText.Contains(
                "SelectionChanged=\"ProjectPageSelectionChanged\"",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "OpenSelectedPageCommand.Execute(null)",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Binding ShowCanvasGrid, Mode=TwoWay",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Binding ShowCanvasOutlines, Mode=TwoWay",
                StringComparison.Ordinal)
            && !mainWindowXaml.Contains(
                "ActiveWorkspaceTab.ShowCanvasGrid",
                StringComparison.Ordinal)
            && !mainWindowXaml.Contains(
                "ActiveWorkspaceTab.ShowCanvasOutlines",
                StringComparison.Ordinal)
            && !mainWindowXaml.Contains(
                "HyperpageTabToggleStyle",
                StringComparison.Ordinal)
            && mainWindowXaml.Contains(
                "AutomationProperties.Name=\"New tab\"",
                StringComparison.Ordinal)
            && mainWindowXaml.Contains(
                "ToolTip=\"New tab\"",
                StringComparison.Ordinal)
            && !mainWindowXaml.Contains(
                "Value=\"Create page\"",
                StringComparison.Ordinal)
            && !mainWindowCodeBehind.Contains(
                "HyperpagePageCreationDialog.ShowAndCreateAsync",
                StringComparison.Ordinal)
            && new[]
            {
                "ShowCreatePageDialogAsync",
                "HyperpagePageCreationDialog.ShowAndCreateAsync",
                "StudioCanvasTabControlStyle",
                "isCanvasPane"
            }.All(token => codeBehind.Contains(
                token,
                StringComparison.Ordinal))
            && new[]
            {
                "x:Key=\"StudioCanvasTabControlStyle\"",
                "Style=\"{StaticResource TerminalTabAddButtonStyle}\"",
                "AutomationProperties.Name=\"Create document\""
            }.All(token => xamlText.Contains(
                token,
                StringComparison.Ordinal))
            && xamlText.Split(
                "Click=\"NewProjectDocumentClick\"",
                StringSplitOptions.None).Length >= 3
            && !codeBehind.Contains(
                "CreatePageTabPreviewMouseLeftButtonDown",
                StringComparison.Ordinal)
            && studioViewModelSource.Contains(
                "PrepareHyperpagePageConversation",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "implement the user's description directly in that page",
                StringComparison.Ordinal)
            && projectServiceSource.Contains(
                "HyperpagePageFileType.Cshtml =>",
                StringComparison.Ordinal),
            "The shared Create Page dialog, automatic page switching, per-page output type, tab-bar entry point, or AI foundation handoff was incomplete.");
        Require(
            projectPagesToolbar is not null
            && responsiveCanvasToolbar is not null
            && canvasLayoutToolbar is not null
            && ReferenceEquals(
                projectPagesToolbar.Parent,
                responsiveCanvasToolbar.Parent)
            && projectPagesToolbar.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Grid.Column"
                && attribute.Value == "0")
            && responsiveCanvasToolbar.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Grid.Column"
                && attribute.Value == "2")
            && projectPagesToolbar.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Style"
                && attribute.Value.Contains(
                    "StudioDockedToolbarGroupStyle",
                    StringComparison.Ordinal))
            && responsiveCanvasToolbar.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Style"
                && attribute.Value.Contains(
                    "StudioDockedToolbarGroupStyle",
                    StringComparison.Ordinal))
            && alignedToolbarHostColumns.Count == 3
            && alignedToolbarHostColumns[0].Attributes().Any(attribute =>
                attribute.Name.LocalName == "Width"
                && attribute.Value == "*")
            && alignedToolbarHostColumns[1].Attributes().Any(attribute =>
                attribute.Name.LocalName == "Width"
                && attribute.Value == "10")
            && alignedToolbarHostColumns[2].Attributes().Any(attribute =>
                attribute.Name.LocalName == "Width"
                && attribute.Value == "*")
            && alignedToolbarHost?.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Margin"
                && attribute.Value == "8,0") == true
            && alignedToolbarHost.Attributes().Any(attribute =>
                attribute.Name.LocalName == "SnapsToDevicePixels"
                && attribute.Value == "True")
            && responsiveToolbarGrid is not null
            && responsiveToolbarGrid
                .Elements()
                .Single(element =>
                    element.Name.LocalName == "Grid.ColumnDefinitions")
                .Elements()
                .Count() == 10
            && alignedToolbarHost?.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Grid.Column"
                && attribute.Value == "2") == true
            && alignedToolbarShellColumns.Count == 5
            && alignedToolbarShellColumns[0].Attributes().Any(attribute =>
                attribute.Name.LocalName == "Width"
                && attribute.Value.Contains(
                    "ActualWidth, ElementName=LeftToolPanelHost",
                    StringComparison.Ordinal))
            && alignedToolbarShellColumns[4].Attributes().Any(attribute =>
                attribute.Name.LocalName == "Width"
                && attribute.Value.Contains(
                    "ActualWidth, ElementName=RightToolPanelHost",
                    StringComparison.Ordinal))
            && responsiveCanvasToolbar.Descendants().Any(element =>
                element.Name.LocalName == "Button"
                && element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Command"
                    && attribute.Value.Contains(
                        "DetachCommand",
                        StringComparison.Ordinal)))
            && !canvasLayoutToolbar.Descendants().Any(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Command"
                    && attribute.Value.Contains(
                        "DetachCommand",
                        StringComparison.Ordinal)))
            && xamlText.Contains(
                "ToolTip=\"Canvas display\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Name=\"WorkspaceLayoutToolbar\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "ColumnDefinition Width=\"3*\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "ColumnDefinition Width=\"1.5*\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "MinWidth=\"0\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "HorizontalAlignment=\"Stretch\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Name=\"CentralWorkspaceHost\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Data=\"M 4 2 L 15 9 L 4 16 Z\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Key=\"StudioDockedToolbarGroupStyle\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "BorderThickness\" Value=\"0\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "CornerRadius\" Value=\"8\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Padding\" Value=\"3\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "VerticalAlignment\" Value=\"Bottom\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Name=\"StudioPanelGrid\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Grid.RowSpan=\"2\"",
                StringComparison.Ordinal)
            && HasFocusedPropertyContract(studioCentralWorkspace, "Margin", "0,50,0,0", "0")
            && xamlText.Contains(
                "Panel.ZIndex=\"20\"",
                StringComparison.Ordinal)
            && xamlText.Split(
                "Command=\"{Binding DetachCommand}\"",
                StringSplitOptions.None).Length == 2
            && !xamlText.Contains(
                "Data=\"M 2 4 L 5 1 L 10 6 L 8 8 Z M 7 8 L 15 16 L 17 14 L 9 6\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "ResetLayoutCommand",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "ResetCanvasViewCommand",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Command=\"{Binding UndoCommand}\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Command=\"{Binding RedoCommand}\"",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "AddMenu(menu, \"Undo\", _viewModel.UndoCommand, \"Ctrl+Z\")",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "AddMenu(menu, \"Redo\", _viewModel.RedoCommand, \"Ctrl+Y\")",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "eventArgs.Key == Key.Y",
                StringComparison.Ordinal),
            "The proportional toolbar controls, borderless shelves, responsive pop-out action, canvas groups, page picker, preview action, or history controls were not wired consistently.");
        Require(
            mainWindowCodeBehind.Contains(
                "WindowsMessageGetMinMaxInfo",
                StringComparison.Ordinal)
            && mainWindowCodeBehind.Contains(
                "ApplyMaximizedWorkArea",
                StringComparison.Ordinal)
            && mainWindowCodeBehind.Contains(
                "monitorInfo.WorkArea.Bottom",
                StringComparison.Ordinal)
            && mainWindowCodeBehind.Contains(
                "MonitorDefaultToNearest",
                StringComparison.Ordinal),
            "The borderless main window did not constrain maximized bounds to the Windows work area.");
        Require(
            xamlText.Contains(
                "Command=\"{Binding DocumentOnlyCommand}\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "IsChecked=\"{Binding IsDocumentOnlyWorkspace, Mode=OneWay}\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "AutomationProperties.Name=\"Documents only\"",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "CreateNewDocumentTab()",
                StringComparison.Ordinal)
            && languageAuthoringSource.Contains(
                "CSS stylesheet",
                StringComparison.Ordinal)
            && languageAuthoringSource.Contains(
                "JavaScript file",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "HyperpageDocumentTypeCatalog.Definitions",
                StringComparison.Ordinal)
            && studioViewModelSource.Contains(
                "CreateProjectDocumentAsync",
                StringComparison.Ordinal)
            && studioViewModelSource.Contains(
                "RestoreProjectDocumentsAsync",
                StringComparison.Ordinal),
            "The document-only workspace control or grouped project-document menu was not wired into the live source pane.");
        Require(
            publishTab is not null
            && publishTab.Attribute("Tag")?.Value == "publish"
            && !publishTab.ToString().Contains(
                "AiAssistant",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "AI &amp; publish",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "AiAssistantPanelHeight",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "IsAiAssistantClosed",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Name=\"AiAssistantSplitterRow\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Name=\"AiAssistantPanelRow\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "x:Name=\"AiAssistantBottomEdgeRevealHost\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Placement=\"Custom\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "MinHeight=\"46\"",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "ApplyAiAssistantLayout()",
                StringComparison.Ordinal)
            && codeBehind.Contains(
                "AiAssistantPanelRow.SetCurrentValue",
                StringComparison.Ordinal)
            && aiAssistantToggleButtons.Count == 2
            && aiAssistantToggleButtons.Any(button =>
                button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style"
                    && attribute.Value ==
                        "{StaticResource WorkspaceHorizontalBoundaryToggleButtonStyle}")
                && button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.Name"
                    && attribute.Value == "Hide assistant"))
            && aiAssistantToggleButtons.Any(button =>
                button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Style"
                    && attribute.Value ==
                        "{StaticResource WorkspaceBottomEdgeRevealButtonStyle}")
                && button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.Name"
                    && attribute.Value == "Show AI assistant panel"))
            && aiAssistantToggleButtons.All(button =>
                button.Descendants().Any(element =>
                    element.Name.LocalName == "Path"
                    && element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Data"
                        && attribute.Value ==
                            "{StaticResource StudioDoubleChevronGeometry}")))
            && aiAssistantToggleButtons.SelectMany(button =>
                    button.Attributes())
                .Where(attribute => attribute.Name.LocalName ==
                    "AutomationProperties.Name")
                .Select(attribute => attribute.Value)
                .ToHashSet(StringComparer.Ordinal)
                .IsSupersetOf([
                    "Hide assistant",
                    "Show AI assistant panel"
                ])
            && xamlText.Contains(
                "HyperpageAiMessagesScrollViewer",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "AiAssistant.ProviderModelOptions",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "AiAssistant.SelectedContext",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "HyperpageAiCompactOptions",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "AutomationProperties.Name=\"AI model\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "AutomationProperties.Name=\"Context and permissions\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Text=\"PROJECT CONTEXT\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Text=\"{Binding AiAssistant.WorkingDirectoryDisplay",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Text=\"MODEL\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Text=\"CONTEXT\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Project scope",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "AiAssistant.ProviderStatus",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "Text=\"{Binding ProjectFoundationSummary}\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "Background=\"Transparent\"",
                StringComparison.Ordinal)
            && xamlText.Contains(
                "ApproveHyperpageAiReviewClick",
                StringComparison.Ordinal),
            "The AI assistant was not placed in a full-width bottom drawer with only the model and Context selectors, redundant labels or status remained, or publishing still contains AI controls.");
        Require(
            mainWindowViewModelSource.Contains(
                "BuildHyperpageProjectFoundationContext",
                StringComparison.Ordinal)
            && mainWindowViewModelSource.Contains(
                "clarifying_questions_first=true",
                StringComparison.Ordinal)
            && mainWindowViewModelSource.Contains(
                "Publishing remains a separate user-reviewed workflow",
                StringComparison.Ordinal)
            && mainWindowViewModelSource.Contains(
                "Do not paste a code solution",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "PrepareHyperpageProjectConversation",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "Let's Begin. Describe the project you'd like to build...",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "SetConversationPrimer",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "SetHyperpageProjectWorkflow",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "HYPERPAGE_PROJECT_SESSION_RESTORED",
                StringComparison.Ordinal)
            && conversationSource.Contains(
                "[CURRENT_USER_REQUEST]",
                StringComparison.Ordinal)
            && conversationSource.Contains(
                "_conversationPrimer = string.Empty",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "PrepareWebTemplateCaptureConversation",
                StringComparison.Ordinal)
            && assistantViewModelSource.Contains(
                "permission_confirmed=true",
                StringComparison.Ordinal)
            && conversationSource.Contains(
                "capture_web_template",
                StringComparison.Ordinal)
            && conversationSource.Contains(
                "HYPERPAGE_HOST_CORRECTION",
                StringComparison.Ordinal)
            && conversationSource.Contains(
                "LooksLikeManualHyperpageCodeDelivery",
                StringComparison.Ordinal),
            "The project foundation and website-template workflow were not bounded to the AI turn with clarifying-first, permission, and publish-isolation guidance.");
        var requiredAuthoringBindings = new[]
        {
            "ElementScopes",
            "SelectedElementScope",
            "SelectedDescriptorAttributes",
            "SelectedDescriptorPlacement",
            "SelectedDescriptorChildren",
            "SelectedDescriptorCanvas",
            "HasSelectedDescriptorWarning",
            "InspectorElementSupportsText",
            "UsesBooleanAttributeEditor",
            "UsesChoiceAttributeEditor",
            "UsesTextAttributeEditor",
            "AttributeBooleanValue"
        };
        Require(
            requiredAuthoringBindings.All(property => bindingValues.Any(value =>
                value.Contains(property, StringComparison.Ordinal)))
            && new[]
            {
                "Text=\"Search\"", "Text=\"Library\"", "Text=\"Category\"",
                "Text=\"Placement\"", "Text=\"Children\"", "Text=\"Canvas\"",
                "Header=\"Info\" IsExpanded=\"False\"", "Text=\"Properties\"",
                "Content=\"Apply\"", "Content=\"Remove\"", "PasteActionText",
                "Content=\"Event\"", "ToolTipService.ShowOnDisabled",
                "Content=\"Add\"", "Content=\"Import\""
            }.All(label => xamlText.Contains(label, StringComparison.Ordinal))
            && new[]
            {
                "Search all elements", "Where to use it", "What it contains",
                "Properties &amp; attributes", "Apply property", "Remove property",
                "Add to Selection", "Import existing HTML / Razor"
            }.All(label => !xamlText.Contains(label, StringComparison.Ordinal))
            && !xamlText.Contains(
                "Text=\"{Binding ElementCatalogSummary, Mode=OneWay}\"",
                StringComparison.Ordinal)
            && !xamlText.Contains(
                "AutomationProperties.Name=\"Choose a Hyperpage Studio tool\"",
                StringComparison.Ordinal)
            && new[]
            {
                "Edit properties", "Add child", "Paste after",
                "Add event handler", "AddDescriptorCommand"
            }.All(token => codeBehind.Contains(token, StringComparison.Ordinal)),
            "Studio element discovery, guides, dynamic property controls, or contextual quick actions were not presented in the view.");
    }

    private static void VerifyCanvasNavigationBoundary()
    {
        var method = typeof(VisualPageStudioView).GetMethod(
            "IsAllowedCanvasNavigation",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static);
        Require(method is not null,
            "The sanitized canvas navigation boundary was not found.");
        var allowsGeneratedCanvas = (bool)method!.Invoke(
            null,
            ["data:text/html,<main>safe canvas</main>", false])!;
        var blocksUserDataNavigation = !(bool)method.Invoke(
            null,
            ["data:text/html,<main>user navigation</main>", true])!;
        var blocksNetworkNavigation = !(bool)method.Invoke(
            null,
            ["https://example.invalid/", false])!;
        Require(allowsGeneratedCanvas
                && blocksUserDataNavigation
                && blocksNetworkNavigation,
            "The design canvas navigation boundary blocked generated HTML or allowed user/network navigation.");
    }

    private static IEnumerable<VisualPageElement> Flatten(
        VisualPageElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static VisualPageElement? Find(
        VisualPageElement root,
        string elementId) => Flatten(root).FirstOrDefault(element =>
            element.ElementId == elementId);

    private static int CountElements(VisualPageElement root) =>
        Flatten(root).Count();

    private static IEnumerable<StudioLayoutNode> FlattenLayout(
        StudioLayoutNode root)
    {
        yield return root;
        if (root.First is not null)
        {
            foreach (var child in FlattenLayout(root.First))
            {
                yield return child;
            }
        }
        if (root.Second is not null)
        {
            foreach (var child in FlattenLayout(root.Second))
            {
                yield return child;
            }
        }
    }

    private static int CountPaneContent(
        IEnumerable<StudioLayoutNode> nodes,
        string contentId) => nodes
            .Where(node => node.Kind == StudioLayoutNodeKind.Pane)
            .Sum(node => node.PaneContents.Count(content => string.Equals(
                content,
                contentId,
                StringComparison.Ordinal)));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
