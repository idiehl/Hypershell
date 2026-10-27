using Hypershell.Models;
using Hypershell.Services;
using System.Text;

internal static class VisualPageVerifier
{
    public static async Task VerifyCoreAsync(string verifierRoot)
    {
        var projectRoot = Path.Combine(verifierRoot, "portable-project");
        var catalog = new VisualPageComponentCatalog();
        var expectedHtmlTags = "a abbr address area article aside audio b base bdi bdo blockquote body br button canvas caption cite code col colgroup data datalist dd del details dfn dialog div dl dt em embed fieldset figcaption figure footer form h1 h2 h3 h4 h5 h6 head header hgroup hr html i iframe img input ins kbd label legend li link main map mark menu meta meter nav noscript object ol optgroup option output p picture pre progress q rp rt ruby s samp script search section select selectedcontent slot small source span strong style sub summary sup table tbody td template textarea tfoot th thead time title tr track u ul var video wbr"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            catalog.StandardHtmlTags.SetEquals(expectedHtmlTags),
            "The Visual Page catalog does not cover every conforming named WHATWG HTML element. " +
            $"Missing: {string.Join(", ", expectedHtmlTags.Except(catalog.StandardHtmlTags))}. " +
            $"Unexpected: {string.Join(", ", catalog.StandardHtmlTags.Except(expectedHtmlTags))}.");
        Require(
            catalog.CanonicalStandardHtmlElements.Count == expectedHtmlTags.Count
            && catalog.CanonicalStandardHtmlElements
                .Select(descriptor => descriptor.MarkupName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expectedHtmlTags),
            "The canonical All HTML library must contain exactly one entry for every current HTML tag.");
        var expectedObsoleteHtmlTags =
            "applet acronym bgsound dir frame frameset noframes isindex keygen listing menuitem nextid noembed param plaintext rb rtc strike xmp basefont big blink center font marquee multicol nobr spacer tt"
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(
            catalog.ObsoleteHtmlTags.SetEquals(expectedObsoleteHtmlTags)
            && expectedObsoleteHtmlTags.All(tag =>
                catalog.TryGetHtmlElement(tag, null, out var descriptor)
                && descriptor.IsObsolete
                && descriptor.CanvasRepresentation ==
                VisualPageCanvasRepresentation.Placeholder
                && descriptor.Classification ==
                VisualPageCodeClassification.LegacyHtml),
            "The reviewed legacy-HTML import catalog is incomplete or escaped its safety classification.");
        Require(
            catalog.Descriptors.All(descriptor =>
                descriptor.Authoring is not null
                && !string.IsNullOrWhiteSpace(descriptor.Authoring.Badge)
                && !string.IsNullOrWhiteSpace(
                    descriptor.Authoring.PlacementGuidance)
                && !string.IsNullOrWhiteSpace(
                    descriptor.Authoring.ChildrenGuidance)
                && !string.IsNullOrWhiteSpace(
                    descriptor.Authoring.CanvasGuidance)
                && !string.IsNullOrWhiteSpace(
                    descriptor.Authoring.TextEditorLabel)
                && descriptor.AttributeDefinitions.All(attribute =>
                    !string.IsNullOrWhiteSpace(attribute.DisplayName)
                    && !string.IsNullOrWhiteSpace(attribute.Description))
                && descriptor.AllowedAttributes.All(name =>
                    descriptor.AttributeDefinitions.Any(attribute =>
                        attribute.Name.Equals(
                            name,
                            StringComparison.OrdinalIgnoreCase)))
                && descriptor.Authoring.QuickAttributeNames.All(name =>
                    descriptor.AllowedAttributes.Contains(
                        name,
                        StringComparer.OrdinalIgnoreCase))
                && descriptor.Authoring.SuggestedChildDescriptorIds.All(id =>
                    catalog.TryGet(id, out _))
                && (!descriptor.IsVoid
                    || descriptor.Authoring.TextEditorKind ==
                    VisualPageElementTextEditorKind.None)
                && (!descriptor.IsObsolete
                    || !string.IsNullOrWhiteSpace(descriptor.Authoring.Warning)
                    && descriptor.CanvasRepresentation ==
                    VisualPageCanvasRepresentation.Placeholder)),
            "One or more Studio objects lack complete authoring guidance, property metadata, safe presentation, or resolvable quick actions.");
        var inputTypes = new[]
        {
            "button", "checkbox", "color", "date", "datetime-local", "email",
            "file", "hidden", "image", "month", "number", "password", "radio",
            "range", "reset", "search", "submit", "tel", "text", "time", "url", "week"
        };
        Require(
            inputTypes.All(type => catalog.TryGet($"html.input.{type}", out _))
            && catalog.GetAttributeDefinitions("html.input.email").Any(value =>
                value.Name == "required"
                && value.EditorKind == VisualPageAttributeEditorKind.Boolean)
            && catalog.TryGetHtmlElement(
                "input",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["type"] = "email"
                },
                out var importedEmail)
            && importedEmail.Id == "html.input.email",
            "HTML input states or schema-backed attribute metadata are incomplete.");
        var emailInput = catalog.GetRequired("html.input.email");
        var checkboxInput = catalog.GetRequired("html.input.checkbox");
        var fileInput = catalog.GetRequired("html.input.file");
        var hiddenInput = catalog.GetRequired("html.input.hidden");
        Require(
            emailInput.AllowedAttributes.Contains("multiple")
            && !emailInput.AllowedAttributes.Contains("checked")
            && checkboxInput.AllowedAttributes.Contains("checked")
            && !checkboxInput.AllowedAttributes.Contains("placeholder")
            && fileInput.AllowedAttributes.Contains("accept")
            && fileInput.AllowedAttributes.Contains("multiple")
            && !fileInput.AllowedAttributes.Contains("min")
            && catalog.GetRequired("html.input.datetime-local").DisplayName ==
            "Local date and time input"
            && catalog.GetRequired("html.input.range").DisplayName ==
            "Range slider"
            && catalog.GetRequired("html.input.image").DisplayName ==
            "Image submit button"
            && hiddenInput.CanvasRepresentation ==
                VisualPageCanvasRepresentation.Placeholder
            && hiddenInput.Authoring.TextEditorKind ==
                VisualPageElementTextEditorKind.None,
            "Input-state property menus are not limited to relevant controls or hidden inputs became unselectable.");
        var buttonType = catalog.GetRequired("core.button")
            .AttributeDefinitions.Single(attribute => attribute.Name == "type");
        var orderedListType = catalog.GetRequired("core.ordered-list")
            .AttributeDefinitions.Single(attribute => attribute.Name == "type");
        var scriptType = catalog.GetRequired("html.script")
            .AttributeDefinitions.Single(attribute => attribute.Name == "type");
        var buttonProperties = catalog.GetAttributeDefinitions("core.button")
            .ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        Require(
            buttonType.RestrictToSuggestedValues
            && buttonType.SuggestedValues.SequenceEqual(
                ["button", "submit", "reset"])
            && orderedListType.RestrictToSuggestedValues
            && orderedListType.SuggestedValues.SequenceEqual(
                ["1", "a", "A", "i", "I"])
            && !scriptType.RestrictToSuggestedValues
            && catalog.GetRequired("core.option").AttributeDefinitions
                .Single(attribute => attribute.Name == "value").EditorKind ==
                VisualPageAttributeEditorKind.Text
            && catalog.GetRequired("html.progress").AttributeDefinitions
                .Single(attribute => attribute.Name == "value").EditorKind ==
                VisualPageAttributeEditorKind.Number
            && catalog.GetRequired("core.image").AllowedAttributes.Contains(
                "controls")
            && buttonProperties["popovertarget"].DisplayName ==
            "Popover target"
            && buttonProperties["commandfor"].DisplayName == "Command for"
            && buttonProperties["formaction"].DisplayName == "Form action"
            && buttonProperties["aria-labelledby"].DisplayName ==
            "ARIA labelled by",
            "Element-aware type/value editors, human-facing property names, or current image controls do not match the Living Standard catalog.");
        var starterTable = catalog.CreateElementForAuthoring("html.table");
        var starterDetails = catalog.CreateElementForAuthoring("html.details");
        var starterFigure = catalog.CreateElementForAuthoring("core.figure");
        var starterSection = catalog.CreateElementForAuthoring("core.section");
        Require(
            starterTable.Children.Select(child => child.DescriptorId)
                .SequenceEqual(["html.caption", "html.thead", "html.tbody"])
            && Flatten(starterTable).Count(element =>
                element.DescriptorId == "html.th") == 2
            && Flatten(starterTable).Count(element =>
                element.DescriptorId == "html.td") == 2
            && starterDetails.Children.First().DescriptorId == "html.summary"
            && starterFigure.Children.Select(child => child.DescriptorId)
                .SequenceEqual(["core.image", "core.caption"])
            && starterSection.Children.Select(child => child.DescriptorId)
                .SequenceEqual(["core.heading2", "core.paragraph"])
            && Flatten(starterTable).Select(element => element.ElementId)
                .Distinct(StringComparer.Ordinal).Count() ==
                Flatten(starterTable).Count(),
            "Complex HTML elements did not receive useful, valid, ID-safe starter structures.");
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projects = new VisualPageProjectService(
            validator,
            generator,
            catalog);
        var session = await projects.CreateAsync(
            projectRoot,
            "Verifier Pages",
            "Verifier.Pages");

        Require(
            File.Exists(Path.Combine(projectRoot, "visual-page.project.json"))
            && File.Exists(Path.Combine(projectRoot, "src", "Pages", "Pages.csproj"))
            && File.Exists(Path.Combine(projectRoot, "src", "Host", "Host.csproj")),
            "The portable multi-project scaffold was not created.");
        Require(
            !File.ReadAllText(Path.Combine(projectRoot, "src", "Pages", "Pages.csproj"))
                .Contains("Hypershell", StringComparison.OrdinalIgnoreCase),
            "Generated Blazor projects must not depend on Hypershell.");

        var first = generator.Generate(session.Project, session.Page);
        var second = generator.Generate(session.Project, session.Page);
        Require(
            first.Artifacts.Select(value => (value.RelativePath, value.Sha256))
                .SequenceEqual(second.Artifacts.Select(value =>
                    (value.RelativePath, value.Sha256))),
            "Visual page generation is not deterministic.");

        var engine = new VisualPageMutationEngine(validator);
        var originalPageRevision = session.Page.Revision;
        var section = catalog.CreateElement("core.section");
        section.Name = "Verifier section";
        var insert = engine.Apply(
            session.Page,
            new VisualPageMutationSet(
                session.Page.Revision,
                [
                    new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.InsertChild,
                        ParentElementId = session.Page.Root.ElementId,
                        Element = section
                    }
                ],
                "verifier",
                "exercise insert"));
        session.Page = insert.Document;
        Require(
            session.Page.Root.Children.Any(value =>
                value.ElementId == section.ElementId),
            "InsertChild did not update the element tree.");
        Expect<VisualPageStaleRevisionException>(
            () => engine.Apply(
                session.Page,
                new VisualPageMutationSet(
                    originalPageRevision,
                    [new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.SetText,
                        TargetElementId = section.ElementId,
                        Value = "stale"
                    }])),
            "Stale semantic mutations must be rejected.");

        var paragraph = catalog.CreateElement("core.paragraph");
        paragraph.Text = "Bound state: ";
        session.Page = engine.Apply(
            session.Page,
            new VisualPageMutationSet(
                session.Page.Revision,
                [
                    new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.InsertChild,
                        ParentElementId = section.ElementId,
                        Element = paragraph
                    },
                    new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.SetLayout,
                        TargetElementId = section.ElementId,
                        Breakpoint = VisualPageBreakpoint.Medium,
                        Layout = new VisualPageLayoutSpec
                        {
                            Display = "grid",
                            GridTemplateColumns = "repeat(2, minmax(0, 1fr))",
                            Gap = "1rem"
                        }
                    },
                    new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.AddState,
                        State = new VisualPageStateDefinition
                        {
                            Name = "Message",
                            TypeName = "string",
                            Kind = VisualPageStateKind.Field,
                            InitialValue = "\"Hello from state\""
                        }
                    },
                    new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.SetBinding,
                        TargetElementId = paragraph.ElementId,
                        Binding = new VisualPageBinding
                        {
                            Kind = VisualPageBindingKind.Text,
                            Expression = "Message"
                        }
                    },
                    new VisualPageMutationOperation
                    {
                        Kind = VisualPageMutationKind.SetEvent,
                        TargetElementId = section.ElementId,
                        Event = new VisualPageEventBinding
                        {
                            Kind = VisualPageEventKind.Click,
                            HandlerName = "HandleVerifierClick"
                        }
                    }
                ]))
            .Document;

        var duplicate = engine.Apply(
            session.Page,
            new VisualPageMutationSet(
                session.Page.Revision,
                [new VisualPageMutationOperation
                {
                    Kind = VisualPageMutationKind.DuplicateElement,
                    TargetElementId = paragraph.ElementId
                }]));
        session.Page = duplicate.Document;
        var elementIds = Flatten(session.Page.Root)
            .Select(value => value.ElementId)
            .ToList();
        Require(
            elementIds.Distinct(StringComparer.Ordinal).Count() == elementIds.Count,
            "Clipboard duplication did not remap stable element IDs.");

        await projects.SaveAsync(
            session,
            session.Project.Revision,
            originalPageRevision);
        var loaded = await projects.OpenAsync(projectRoot, session.Page.PageId);
        Require(
            loaded.Page.Revision == session.Page.Revision
            && loaded.Page.State.Any(state => state.Name == "Message")
            && loaded.Page.SourceMappings.Count > 0,
            "Atomic save did not preserve the mutated page and source mappings.");
        Require(
            generator.Generate(loaded.Project, loaded.Page).Classifications
                .Contains(VisualPageCodeClassification.CustomCSharp),
            "Generated state and binding expressions must enter the reviewed-code publication lane.");
        Require(
            Directory.EnumerateFiles(
                    Path.Combine(projectRoot, ".visual-page", "recovery"),
                    "*.json")
                .Any(),
            "Saving did not retain a bounded crash-recovery revision.");

        var generatedRazor = Path.Combine(
            projectRoot,
            "src",
            "Pages",
            "Generated",
            "HomePage.razor");
        var expectedGenerated = await File.ReadAllTextAsync(generatedRazor);
        await File.AppendAllTextAsync(
            generatedRazor,
            "\n@* external drift *@\n",
            new UTF8Encoding(false));
        Expect<VisualPageGeneratedDriftException>(
            () => projects.SaveAsync(
                    loaded,
                    loaded.Project.Revision,
                    loaded.Page.Revision)
                .GetAwaiter()
                .GetResult(),
            "Externally edited generated Razor must not be overwritten silently.");
        await File.WriteAllTextAsync(
            generatedRazor,
            expectedGenerated,
            new UTF8Encoding(false));

        var personalTemplateRoot = Path.Combine(
            verifierRoot,
            "personal-templates");
        var templates = new VisualPageTemplateService(
            catalog,
            personalTemplateRoot);
        var template = templates.BuiltInTemplates.Single(value =>
            value.TemplateId == "atlas.hero.split.v1");
        var firstTemplate = templates.Instantiate(
            template,
            new Dictionary<string, string>
            {
                ["hero-image"] = "Assets/remapped-hero.png"
            });
        var secondTemplate = templates.Instantiate(template);
        Require(
            firstTemplate.ElementId != secondTemplate.ElementId
            && Flatten(firstTemplate).Select(value => value.ElementId)
                .Intersect(
                    Flatten(secondTemplate).Select(value => value.ElementId),
                    StringComparer.Ordinal)
                .Count() == 0
            && Flatten(firstTemplate).Any(value =>
                value.Attributes.Values.Contains("Assets/remapped-hero.png")),
            "Template insertion did not remap IDs and asset dependencies.");
        var contactTemplate = templates.BuiltInTemplates.Single(value =>
            value.TemplateId == "core.contact.form.v1");
        var firstContact = templates.Instantiate(contactTemplate);
        var secondContact = templates.Instantiate(contactTemplate);
        var firstDomIds = Flatten(firstContact)
            .Select(value => value.Attributes.GetValueOrDefault("id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        var secondDomIds = Flatten(secondContact)
            .Select(value => value.Attributes.GetValueOrDefault("id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        Require(
            firstDomIds.Count == 3
            && !firstDomIds.Overlaps(secondDomIds)
            && Flatten(firstContact)
                .Where(value => value.Attributes.ContainsKey("for"))
                .All(value => firstDomIds.Contains(value.Attributes["for"])),
            "Reusable templates did not remap authored DOM IDs and their label/reference attributes.");

        var atlasPageTemplateIds = new HashSet<string>(
            [
                "atlas.archive.page.longform.v1",
                "atlas.archive.page.history.v1",
                "atlas.archive.page.language-culture.v1",
                "atlas.archive.page.evolution.v1",
                "atlas.archive.page.coding.v1",
                "atlas.archive.page.media-dossier.v1"
            ],
            StringComparer.Ordinal);
        var pageTemplates = templates.BuiltInTemplates
            .Where(value => value.Scope == "page")
            .ToList();
        var atlasPages = pageTemplates
            .Where(value => atlasPageTemplateIds.Contains(value.TemplateId))
            .ToList();
        var commonTemplates = templates.BuiltInTemplates
            .Where(value => value.Library ==
                            VisualPageCommonTemplateCatalog.LibraryName)
            .ToList();
        Require(
            templates.BuiltInTemplates.Count == 58
            && atlasPages.Count == 6
            && atlasPages.Select(value => value.TemplateId).ToHashSet(
                    StringComparer.Ordinal)
                .SetEquals(atlasPageTemplateIds)
            && templates.BuiltInTemplates.Count(value =>
                value.Scope == "page") == 14
            && templates.BuiltInTemplates.Count(value =>
                value.Scope == "section") == 27
            && templates.BuiltInTemplates.Count(value =>
                value.Scope == "element") == 17
            && commonTemplates.Count == 28
            && commonTemplates.Count(value => value.Scope == "page") == 8
            && commonTemplates.Count(value => value.Scope == "section") == 12
            && commonTemplates.Count(value => value.Scope == "element") == 8
            && commonTemplates.Select(value => value.CategoryGroup)
                .Distinct(StringComparer.Ordinal)
                .Count() >= 8
            && templates.BuiltInTemplates.All(value =>
                !string.IsNullOrWhiteSpace(value.Library)
                && !string.IsNullOrWhiteSpace(value.CategoryGroupLabel)
                && !string.IsNullOrWhiteSpace(value.Category)
                && !string.IsNullOrWhiteSpace(value.UsageHint)
                && value.Tags.Count > 0),
            "The built-in Hyperpage page, section, and reusable-element template taxonomy is incomplete or lacks authoring guidance.");
        foreach (var pageTemplate in pageTemplates)
        {
            var templatedDocument = new VisualPageDocument
            {
                ProjectId = session.Project.ProjectId,
                Name = pageTemplate.SuggestedPageName,
                ComponentName = "TemplateVerifierPage",
                Route = pageTemplate.SuggestedPageRoute,
                Slug = VisualPageProjectService.MakeSlug(
                    pageTemplate.SuggestedPageName),
                Title = pageTemplate.SuggestedPageTitle,
                Description = pageTemplate.SuggestedPageDescription,
                Root = templates.Instantiate(pageTemplate)
            };
            validator.ThrowIfInvalid(templatedDocument);
            var generatedTemplate = generator.Generate(
                session.Project,
                templatedDocument);
            Require(
                generatedTemplate.Artifacts.Any(value =>
                    value.RelativePath.EndsWith(
                        ".razor",
                        StringComparison.OrdinalIgnoreCase))
                && generatedTemplate.Artifacts.Any(value =>
                    value.RelativePath.EndsWith(
                        ".razor.css",
                        StringComparison.OrdinalIgnoreCase)),
                $"Atlas page starter '{pageTemplate.TemplateId}' did not generate portable Razor and scoped CSS.");
        }
        var starterProject = await projects.CreateAsync(
            Path.Combine(verifierRoot, "atlas-template-project"),
            "Atlas Starter Verifier",
            "Atlas.Starter.Verifier");
        var historyTemplate = atlasPages.Single(value =>
            value.TemplateId == "atlas.archive.page.history.v1");
        starterProject = await projects.AddPageAsync(
            starterProject,
            "Historical Test",
            "/archive/historical-test",
            starterRoot: templates.Instantiate(historyTemplate),
            themeTokens: historyTemplate.ThemeTokens,
            title: historyTemplate.SuggestedPageTitle,
            description: historyTemplate.SuggestedPageDescription);
        Require(
            starterProject.Project.Pages.Count == 2
            && starterProject.Page.Root.Children.Count >= 3
            && starterProject.Project.Theme.Tokens["color.background"] == "#070d1a"
            && starterProject.Project.Theme.Tokens["color.accent"] == "#c8963e"
            && File.Exists(Path.Combine(
                starterProject.RootPath,
                "src",
                "Pages",
                "Generated",
                $"{starterProject.Page.ComponentName}.razor")),
            "Creating a new project page from an Atlas starter did not atomically preserve its structure, metadata, and theme tokens.");

        var personalAssetId = Guid.NewGuid().ToString("N");
        var personalAssetSource = Path.Combine(verifierRoot, "template-asset.svg");
        await File.WriteAllTextAsync(
            personalAssetSource,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 2 2\"><path d=\"M0 0h2v2H0z\"/></svg>",
            new UTF8Encoding(false));
        var personalTemplate = new VisualPageTemplateDocument
        {
            Name = "Personal image",
            Description = "Device-personal verifier template.",
            Scope = "element",
            StorageScope = "personal",
            Root = catalog.CreateElement("core.image"),
            AssetIds = [personalAssetId]
        };
        personalTemplate.Root.Attributes["src"] = $"asset:{personalAssetId}";
        await templates.SavePersonalAsync(
            personalTemplate,
            new Dictionary<string, string>
            {
                [personalAssetId] = personalAssetSource
            });
        await File.WriteAllTextAsync(
            Path.Combine(
                personalTemplateRoot,
                "broken.visual-page-template.json"),
            "{",
            new UTF8Encoding(false));
        var personalCatalog = await templates.LoadPersonalAsync();
        var loadedPersonal = personalCatalog.Templates.Single(value =>
            value.TemplateId == personalTemplate.TemplateId);
        var packagedAssetPath = templates.ResolvePackagedAssetPath(
            loadedPersonal,
            personalAssetId);
        var personalInstance = templates.Instantiate(
            loadedPersonal,
            new Dictionary<string, string>
            {
                [personalAssetId] = "Assets/imported-personal.svg"
            });
        Require(
            personalCatalog.RejectedCount == 1
            && loadedPersonal.Library == "My templates"
            && File.Exists(packagedAssetPath)
            && personalInstance.Attributes["src"] == "Assets/imported-personal.svg",
            "Device-personal template packaging, bounded loading, or asset remapping failed.");

        var importer = new VisualPageImportService(catalog);
        var import = importer.Import(
            "<section><h2>Imported</h2><fancy-widget>@Model.Value</fancy-widget></section>",
            razor: true);
        Require(
            import.ContainsCustomCode
            && Flatten(import.Root).Any(value => value.DescriptorId == "custom.razor")
            && import.Diagnostics.Any(value => value.Code == "VPI001"),
            "Unsupported imported Razor was not preserved as an opaque custom node.");

        var modernImport = importer.Import(
            "<search><details open><summary>Question</summary><p>Answer</p></details>" +
            "<select aria-label=\"Choice\"><button><selectedcontent></selectedcontent></button>" +
            "<option selected value=\"a\">A</option></select>" +
            "<input type=\"email\" required aria-label=\"Email\"></search>",
            razor: false);
        var modernElements = Flatten(modernImport.Root).ToList();
        Require(
            !modernImport.ContainsCustomCode
            && modernImport.Diagnostics.All(value => value.Code != "VPI001")
            && modernElements.Any(value => value.DescriptorId == "html.search")
            && modernElements.Any(value => value.DescriptorId == "html.details")
            && modernElements.Any(value => value.DescriptorId == "html.selectedcontent")
            && modernElements.Any(value => value.DescriptorId == "html.input.email")
            && modernElements.Single(value => value.DescriptorId == "html.summary").Text == "Question",
            "Modern semantic HTML import did not preserve supported elements, input states, or source text.");

        var legacyImport = importer.Import(
            "<center><font color=\"red\">Legacy content</font></center>",
            razor: false);
        var legacyPage = new VisualPageDocument
        {
            ProjectId = loaded.Project.ProjectId,
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "Legacy import page",
                Children = [legacyImport.Root]
            }
        };
        Require(
            !legacyImport.ContainsCustomCode
            && Flatten(legacyImport.Root).Any(value =>
                value.DescriptorId == "html.center")
            && validator.ValidatePage(legacyPage).Any(value =>
                value.Code == "VP203")
            && generator.Generate(loaded.Project, legacyPage).Classifications
                .Contains(VisualPageCodeClassification.LegacyHtml),
            "Legacy HTML was not preserved as an explicit warned and reviewed visual node.");

        var schemaPage = new VisualPageDocument
        {
            ProjectId = loaded.Project.ProjectId,
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "Schema page",
                Children =
                [
                    catalog.CreateElement("html.bdo"),
                    catalog.CreateElement("html.source"),
                    catalog.CreateElement("html.head")
                ]
            }
        };
        schemaPage.Root.Children[0].Attributes["dir"] = "sideways";
        schemaPage.Root.Children[1].Children.Add(catalog.CreateElement("core.paragraph"));
        var schemaDiagnostics = validator.ValidatePage(schemaPage);
        Require(
            schemaDiagnostics.Any(value => value.Code == "VP138")
            && schemaDiagnostics.Any(value => value.Code == "VP112")
            && schemaDiagnostics.Any(value => value.Code == "VP139")
            && schemaDiagnostics.Any(value => value.Code == "VP204"),
            "Schema validation did not reject invalid values, void children, or constrained placement, or identify document-shell elements.");

        var executionPage = new VisualPageDocument
        {
            ProjectId = loaded.Project.ProjectId,
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "Execution boundary page",
                Children = [catalog.CreateElement("html.script")]
            }
        };
        executionPage.Root.Children[0].Text =
            "window.__visualPageVerifierShouldNotRun = true;";
        var executionCanvas = new VisualPageDesignCanvasRenderer(catalog).Render(
            executionPage,
            executionPage.Root.Children[0].ElementId,
            VisualPageBreakpoint.Base,
            options: new VisualPageCanvasOptions(
                ZoomPercent: 125,
                ViewportWidth: "375",
                ShowGrid: true,
                ShowOutlines: true));
        var generatedExecution = generator.Generate(loaded.Project, executionPage);
        Require(
            executionCanvas.Contains("vp-placeholder", StringComparison.Ordinal)
            && executionCanvas.Contains("width:375px", StringComparison.Ordinal)
            && executionCanvas.Contains("zoom:1.25", StringComparison.Ordinal)
            && executionCanvas.Contains("vp-grid", StringComparison.Ordinal)
            && executionCanvas.Contains(
                "body.vp-grid #vp-canvas-root",
                StringComparison.Ordinal)
            && executionCanvas.IndexOf(
                "window.__visualPageVerifierShouldNotRun",
                StringComparison.Ordinal) < executionCanvas.LastIndexOf(
                "<script>",
                StringComparison.Ordinal)
            && generatedExecution.Classifications.Contains(
                VisualPageCodeClassification.CustomJavaScript),
            "Canvas device tools or the custom-script execution boundary regressed.");
        var constrainedCanvasPage = new VisualPageDocument
        {
            ProjectId = loaded.Project.ProjectId,
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "Constrained canvas page",
                Children =
                [
                    starterTable,
                    catalog.CreateElementForAuthoring("html.input.hidden")
                ]
            }
        };
        var constrainedCanvas = new VisualPageDesignCanvasRenderer(catalog).Render(
            constrainedCanvasPage,
            starterTable.ElementId,
            VisualPageBreakpoint.Base);
        Require(
            constrainedCanvas.Contains("vp-placeholder-badge", StringComparison.Ordinal)
            && constrainedCanvas.Contains("Place directly inside &lt;table&gt;", StringComparison.Ordinal)
            && constrainedCanvas.Contains("type=hidden", StringComparison.Ordinal)
            && !constrainedCanvas.Contains("<table data-vp-id=", StringComparison.Ordinal)
            && constrainedCanvas.Contains("<span class=\"vp-drop-zone\"", StringComparison.Ordinal),
            "Grammar-sensitive or invisible elements lost their labelled, selectable canvas representation.");

        var booleanPage = new VisualPageDocument
        {
            ProjectId = loaded.Project.ProjectId,
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "Boolean page",
                Children =
                [
                    catalog.CreateElement("html.input.email"),
                    catalog.CreateElement("core.button"),
                    catalog.CreateElement("core.button")
                ]
            },
            State =
            [
                new VisualPageStateDefinition
                {
                    Name = "IsDisabled",
                    TypeName = "bool",
                    InitialValue = "false"
                }
            ]
        };
        booleanPage.Root.Children[0].Attributes["required"] = "true";
        booleanPage.Root.Children[0].Attributes["aria-label"] = "Email";
        booleanPage.Root.Children[1].Attributes["disabled"] = "false";
        booleanPage.Root.Children[2].Attributes["disabled"] = "true";
        booleanPage.Root.Children[2].Bindings.Add(new VisualPageBinding
        {
            Kind = VisualPageBindingKind.Attribute,
            Target = "disabled",
            Expression = "IsDisabled"
        });
        var booleanRazor = generator.Generate(loaded.Project, booleanPage)
            .Artifacts.Single(value => value.RelativePath.EndsWith(
                ".razor",
                StringComparison.Ordinal)).Content;
        Require(
            booleanRazor.Contains(" required", StringComparison.Ordinal)
            && !booleanRazor.Contains("required=", StringComparison.Ordinal)
            && booleanRazor.Contains(
                "disabled=\"@(IsDisabled)\"",
                StringComparison.Ordinal)
            && booleanRazor.Split("disabled", StringSplitOptions.None).Length == 2,
            "Generated Razor did not minimize true HTML booleans, omit false booleans, or replace a static Boolean with its binding.");

        var canvas = new VisualPageDesignCanvasRenderer(catalog).Render(
            import.Root is { } ? new VisualPageDocument
            {
                ProjectId = loaded.Project.ProjectId,
                Root = new VisualPageElement
                {
                    DescriptorId = "core.page",
                    Children = [import.Root]
                }
            } : loaded.Page,
            selectedElementId: null,
            VisualPageBreakpoint.Base);
        Require(
            canvas.Contains("Custom Razor fragment", StringComparison.Ordinal)
            && !canvas.Contains("@Model.Value</fancy-widget>", StringComparison.Ordinal)
            && canvas.Contains("window.chrome", StringComparison.Ordinal),
            "The design canvas did not keep custom code opaque under Hypershell-owned scripting.");

        var injectedAttribute = VisualPageJson.Clone(loaded.Page);
        Flatten(injectedAttribute.Root).First(value =>
                value.ElementId == paragraph.ElementId)
            .Attributes["title\" @onclick"] = "HandleInjected";
        Expect<VisualPageValidationException>(
            () => validator.ThrowIfInvalid(injectedAttribute),
            "Razor attribute-name injection must be rejected before generation.");

        var injectedBinding = VisualPageJson.Clone(loaded.Page);
        Flatten(injectedBinding.Root).First(value =>
                value.ElementId == paragraph.ElementId)
            .Bindings[0].Expression = "Message; System.IO.File.Delete(\"x\")";
        Expect<VisualPageValidationException>(
            () => validator.ThrowIfInvalid(injectedBinding),
            "Multistatement binding expressions must be rejected before generation.");

        var injectedCss = VisualPageJson.Clone(loaded.Page);
        Flatten(injectedCss.Root).First(value =>
                value.ElementId == section.ElementId)
            .Styles["color"] = "red; } @import url(https://example.invalid/x.css)";
        Expect<VisualPageValidationException>(
            () => validator.ThrowIfInvalid(injectedCss),
            "Declarative styles must not escape their generated CSS declaration.");

        var injectedProject = VisualPageJson.Clone(loaded.Project);
        injectedProject.RootNamespace = "Verifier.Pages;System.IO.File.Delete";
        Require(
            validator.ValidateProject(injectedProject).Any(value =>
                value.Severity == VisualPageDiagnosticSeverity.Error),
            "Generated namespace injection must be rejected by the project schema.");

        Expect<VisualPageValidationException>(
            () => VisualPageProjectService.ResolveProjectPath(
                projectRoot,
                "Pages/page.visual-page.json:alternate"),
            "Alternate data streams must be rejected by project path containment.");

        var allHtmlPage = new VisualPageDocument
        {
            ProjectId = loaded.Project.ProjectId,
            Name = "All HTML elements",
            ComponentName = "AllHtmlElements",
            Route = "/all-html-elements",
            Slug = "all-html-elements",
            Title = "All HTML elements",
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "All HTML elements"
            }
        };
        foreach (var tag in catalog.StandardHtmlTags.OrderBy(
                     value => value,
                     StringComparer.Ordinal))
        {
            Require(
                catalog.TryGetHtmlElement(tag, null, out var descriptor),
                $"No import descriptor could be resolved for <{tag}>.");
            allHtmlPage.Root.Children.Add(WrapForValidPlacement(
                catalog,
                catalog.CreateElement(descriptor.Id)));
        }
        foreach (var tag in catalog.ObsoleteHtmlTags.OrderBy(
                     value => value,
                     StringComparer.Ordinal))
        {
            Require(
                catalog.TryGetHtmlElement(tag, null, out var descriptor),
                $"No legacy import descriptor could be resolved for <{tag}>.");
            allHtmlPage.Root.Children.Add(WrapForValidPlacement(
                catalog,
                catalog.CreateElement(descriptor.Id)));
        }
        foreach (var inputType in inputTypes)
        {
            allHtmlPage.Root.Children.Add(catalog.CreateElement(
                $"html.input.{inputType}"));
        }
        var allHtmlGeneration = generator.Generate(loaded.Project, allHtmlPage);
        foreach (var artifact in allHtmlGeneration.Artifacts)
        {
            var artifactPath = Path.Combine(
                projectRoot,
                artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            await File.WriteAllTextAsync(
                artifactPath,
                artifact.Content,
                new UTF8Encoding(false));
        }
        Require(
            allHtmlGeneration.SourceMappings.Count
            == Flatten(allHtmlPage.Root).Count(),
            "Comprehensive HTML generation did not map every element back to its visual node.");

        await VerifyGeneratedBuildAsync(projectRoot);
        Console.WriteLine(
            "VISUAL_PAGE_CORE_OK|whatwg:113|legacy:29|authoring-profiles|contextual-attributes|human-property-names|starter-structures|safe-constrained-canvas|valid-placement|html-catalog|legacy-html-review|all-elements-build|input-states|attribute-schema|schema|mutation|deterministic-generation|drift|recovery|templates:58|archive-pages:6|common-pages:8|common-sections:12|common-elements:8|intent-taxonomy|atlas-theme-tokens|page-from-template|personal-templates|asset-remap|dom-id-remap|semantic-import|canvas-tools|execution-boundary|source-injection-guards|path-boundary|dotnet10");
    }

    private static VisualPageElement WrapForValidPlacement(
        VisualPageComponentCatalog catalog,
        VisualPageElement element,
        int depth = 0)
    {
        if (depth > 8)
        {
            throw new InvalidOperationException(
                $"Placement wrapping for {element.DescriptorId} is recursive.");
        }
        var descriptor = catalog.GetRequired(element.DescriptorId);
        if (descriptor.Authoring.AllowedParentMarkupNames.Count == 0)
        {
            return element;
        }
        var parentMarkup = descriptor.Authoring.AllowedParentMarkupNames
            .First(name => !name.Equals(
                "InputSelect",
                StringComparison.OrdinalIgnoreCase));
        Require(
            catalog.TryGetHtmlElement(parentMarkup, null, out var parentDescriptor),
            $"No parent descriptor could be resolved for <{parentMarkup}>.");
        var parent = catalog.CreateElement(parentDescriptor.Id);
        parent.Children.Add(element);
        return WrapForValidPlacement(catalog, parent, depth + 1);
    }

    private static async Task VerifyGeneratedBuildAsync(string projectRoot)
    {
        var dotnet = VisualPagePreviewBuildService.ResolveDotNetExecutable()
                     ?? throw new InvalidOperationException(
                         ".NET was not found for the generated-project verifier.");
        var result = await new BoundedProcessRunner().RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = dotnet,
                Arguments =
                [
                    "build",
                    Path.Combine(projectRoot, "src", "Host", "Host.csproj"),
                    "--configuration",
                    "Release",
                    "--nologo",
                    "--verbosity",
                    "minimal"
                ],
                WorkingDirectory = projectRoot,
                Timeout = TimeSpan.FromMinutes(3),
                MaximumStandardOutputCharacters = 2 * 1024 * 1024,
                MaximumStandardErrorCharacters = 2 * 1024 * 1024
            });
        Require(
            result.ExitCode == 0,
            "Generated .NET 10 Blazor project failed to build:\n" +
            result.StandardOutput + "\n" + result.StandardError + "\n" +
            result.FailureMessage);
    }

    private static IEnumerable<VisualPageElement> Flatten(
        VisualPageElement root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    private static void Expect<T>(Action action, string message)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
