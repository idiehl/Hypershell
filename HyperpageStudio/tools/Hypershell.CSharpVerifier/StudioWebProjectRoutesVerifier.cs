using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;

/// <summary>Exercises both web project entry routes through the real scaffold, project mutations, compiler, and browser.</summary>
public static class StudioWebProjectRoutesVerifier
{
    public static async Task<string> RunAsync(string output, string repositoryRoot, string libraryRoot, CancellationToken ct = default)
    {
        output = Path.GetFullPath(output);
        if (!output.Contains("qa", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use isolated QA output.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projects = new VisualPageProjectService(validator, generator, catalog);
        var scaffold = new StudioProjectScaffoldService(libraryRoot);
        var all = new VisualPageTemplateService(catalog).BuiltInTemplates.Where(template => template.Scope == "page").ToArray();
        Check(all.Length >= 10, "The retained native collection includes at least ten complete editable Pages.");
        var allRazorHost = Path.Combine(output, "all-native-razor-host");
        Directory.CreateDirectory(Path.Combine(allRazorHost, "Pages"));
        var completePages = new List<object>();
        for (var index = 0; index < all.Length; index++)
        {
            foreach (var kind in new[] { StudioProjectKind.Html, StudioProjectKind.Razor })
            {
                var root = await scaffold.CreateAsync(new() { Kind = kind, Name = kind + " complete " + index,
                    ParentDirectory = output, CompleteTemplateId = "visual:" + all[index].TemplateId }, ct);
                var session = await projects.OpenAsync(root, cancellationToken: ct);
                Check(session.Project.Pages.Count == 1 && session.Page.Root.DescriptorId == "core.page" && Count(session.Page.Root) >= Count(all[index].Root),
                    kind + " retains the native editable tree of " + all[index].Name + ".");
                Check(projects.InspectGeneratedDrift(session).Count == 0 && validator.ValidatePage(session.Page).All(item => item.Severity != VisualPageDiagnosticSeverity.Error),
                    kind + " reopens " + all[index].Name + " without schema errors or generated drift.");
                Check(session.Project.SharedStylesheetPaths?.Count > 0 && session.Project.SharedJavaScriptPaths?.Count > 0
                    && Directory.EnumerateFiles(Path.Combine(root, "Templates"), "*.visual-page-template.json").Count() == 1,
                    kind + " retains the complete template for later Page creation and shared foundation assets.");
                var markup = await File.ReadAllTextAsync(VisualPageProjectService.ResolveProjectPath(root, session.Project.Pages[0].ComponentPath), ct);
                Check(!markup.Contains("asset:", StringComparison.Ordinal) && markup.Contains("/styles/site-foundation.css", StringComparison.Ordinal),
                    kind + " resolves local media references and attaches shared styles in " + all[index].Name + ".");
                if (kind == StudioProjectKind.Razor)
                {
                    if (index == 0)
                    {
                        File.Copy(Path.Combine(root, "src", "Host", "Host.csproj"), Path.Combine(allRazorHost, "Host.csproj"));
                        File.Copy(Path.Combine(root, "src", "Host", "Program.cs"), Path.Combine(allRazorHost, "Program.cs"));
                    }
                    await File.WriteAllTextAsync(Path.Combine(allRazorHost, "Pages", "Template" + index + ".cshtml"),
                        markup.Replace("@page \"/\"", "@page \"/template-" + index + "\"", StringComparison.Ordinal), ct);
                    completePages.Add(new { route = "/template-" + index, title = session.Page.Title });
                }
            }
        }
        Check(Directory.EnumerateFiles(Path.Combine(allRazorHost, "Pages"), "*.cshtml").Count() == all.Length,
            "Every original complete template participates in the actual Razor compiler fixture.");

        var image = Path.Combine(output, "wizard-local-image.png");
        await File.WriteAllBytesAsync(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jh9kAAAAASUVORK5CYII="), ct);
        var library = new HyperlibWorkProjectService(libraryRoot);
        var legacy = library.GetCatalog().Where(choice => choice.Kind == "element" && choice.GroupId.StartsWith("legacy-", StringComparison.Ordinal))
            .First(choice => all.Select(template => template.TemplateId).Concat(new VisualPageTemplateService(catalog).BuiltInTemplates.Select(template => template.TemplateId))
                .Contains(library.GetCatalogObject(choice.GroupId, choice.ObjectId).Element?.SourceTemplateId, StringComparer.Ordinal)
                && library.GetCatalogObject(choice.GroupId, choice.ObjectId).Element!.Fields.Any(field => field.Id.StartsWith("text-", StringComparison.Ordinal)));
        var legacyTextField = library.GetCatalogObject(legacy.GroupId, legacy.ObjectId).Element!.Fields.First(field => field.Id.StartsWith("text-", StringComparison.Ordinal)).Id;
        var recipe = new HyperlibPageTemplateDefinition
        {
            BackgroundColor = "#091528", BackgroundSurface = new() { DesignId = "atlas-dense", BorderWidth = 2, BorderRadius = 12 },
            HeaderLayout = HyperlibHeaderLayouts.Create("logo-title"), NavigationLayout = HyperlibNavigationLayouts.Create("brand-and-links"),
            FooterLayout = HyperlibFooterLayouts.Create("page-navigation"), BodyLayout = new()
            {
                Enabled = true, Blocks = [new() { Height = 440, Padding = 10, Elements = [new()
                {
                    Id = "web-nested-box", Kind = "box", X = 5, Y = 5, WidthPercent = 90, Height = 380, Padding = 12, BorderWidth = 2,
                    Children = [new() { Id = "web-image", Kind = "image", MediaPath = image, Alt = "A local image in a nested Box", WidthPercent = 25, Height = 120 },
                        new() { Id = "web-text", Text = "Reusable Body content", X = 30, Y = 0, WidthPercent = 65, Height = 80, IsContentSlot = true, SlotName = "Article text", SlotHint = "Write the article here." },
                        new() { Id = "web-template", Kind = "template", TemplateObjectId = legacy.Id, TemplateTitle = legacy.Title, X = 0, Y = 40, WidthPercent = 100, Height = 200,
                            TemplateValues = new() { ["element"] = new() { [legacyTextField] = ContentJson.Value("Customized reusable library copy") } } },
                        new() { Id = "web-empty-image-slot", Kind = "image", IsContentSlot = true, SlotName = "Optional illustration", X = 70, Y = 80, WidthPercent = 10, Height = 40 },
                        new() { Id = "web-empty-video-slot", Kind = "video", IsContentSlot = true, SlotName = "Optional video", X = 85, Y = 80, WidthPercent = 10, Height = 40 }]
                }] }]
            }
        };
        foreach (var navigation in new[] { recipe.NavigationLayout, recipe.FooterLayout }.SelectMany(HyperlibHeaderLayouts.Elements).Where(item => item.Kind == "navigation"))
        {
            navigation.Navigation!.HomeLabel = "Work home";
            navigation.Navigation.CustomLinks.Add(new() { Label = "Custom reference", Target = "https://example.com/reference" });
        }
        var work = new HyperlibWorkProjectDefinition { PipelineVersion = 2, WorkTitle = "Web wizard verification", SelectedToolsetIds = [legacy.Id],
            LandingTemplate = ContentJson.Clone(recipe), PageTemplate = recipe, PageTemplates = [new() { Id = "page-default", Name = "Article layout", Definition = recipe },
                new() { Id = "page-gallery", Name = "Gallery layout", Definition = new() { BodyLayout = HyperlibBodyLayouts.Create("media-and-text"), IncludeNavigation = false } },
                new() { Id = "page_a", Name = "Underscore layout", Definition = new() { BodyLayout = HyperlibBodyLayouts.Create("single-column"), IncludeNavigation = false } },
                new() { Id = "page-a", Name = "Hyphen layout", Definition = new() { BodyLayout = HyperlibBodyLayouts.Create("two-columns"), IncludeNavigation = false } }] };
        var browserFixtures = new List<object>();
        foreach (var kind in new[] { StudioProjectKind.Html, StudioProjectKind.Razor })
        {
            var root = await scaffold.CreateAsync(new() { Kind = kind, Name = kind + " scratch", ParentDirectory = output, Work = work }, ct);
            var session = await projects.OpenAsync(root, cancellationToken: ct);
            Check(session.Project.Pages.Count == 2 && Directory.EnumerateFiles(Path.Combine(root, "Templates"), "*.visual-page-template.json").Count() == 4,
                kind + " creates a landing Page and one first content Page while retaining every named Page layout.");
            Check(Flatten(session.Page.Root).Any(element => element.Attributes.GetValueOrDefault("data-header-element") == "web-nested-box")
                && Flatten(session.Page.Root).Any(element => element.Text == "Reusable Body content") && Flatten(session.Page.Root).Any(element => element.Name == legacy.Title),
                kind + " scratch creation retains nested Boxes, content slots, and genuine legacy template objects as editable elements.");
            Check(Flatten(session.Page.Root).Any(element => element.Text == "Customized reusable library copy"),
                kind + " preserves the user's configured content values when restoring a legacy object to its native editable tree.");
            Check(!JsonSerializer.Serialize(session.Page.Root, VisualPageJson.Options).Contains("Write the article here.", StringComparison.Ordinal)
                && !JsonSerializer.Serialize(session.Page.Root, VisualPageJson.Options).Contains("data-studio-slot-placeholder", StringComparison.Ordinal),
                kind + " keeps authoring hints and preview-only slot guides out of its public Page document.");
            Check(new[] { "web-empty-image-slot", "web-empty-video-slot" }.All(id => Flatten(session.Page.Root)
                    .Single(element => element.Attributes.GetValueOrDefault("data-header-element") == id).Children.Count == 0),
                kind + " keeps empty image and video content slots as blank layout wrappers without broken media or guide labels.");
            Check(Flatten(session.Page.Root).Where(element => catalog.GetRequired(element.DescriptorId).MarkupName == "img")
                    .All(element => !string.IsNullOrWhiteSpace(element.Attributes.GetValueOrDefault("src"))),
                kind + " assigns a real local placeholder asset to unfilled non-slot image elements.");
            var imported = Flatten(session.Page.Root).First(element => catalog.GetRequired(element.DescriptorId).MarkupName == "img" && element.Attributes.GetValueOrDefault("alt") == "A local image in a nested Box");
            Check(imported.Attributes["src"].StartsWith("/assets/", StringComparison.Ordinal)
                && File.Exists(Path.Combine(root, "src", "Host", "wwwroot", imported.Attributes["src"].TrimStart('/'))), kind + " imports wizard media into its own portable web root.");
            var savedTemplates = await Task.WhenAll(Directory.EnumerateFiles(Path.Combine(root, "Templates"), "*.visual-page-template.json")
                .Select(path => new VisualPageTemplateService(catalog).LoadAsync(path, ct)));
            Check(savedTemplates.Select(item => item.TemplateId).Distinct(StringComparer.Ordinal).Count() == 4
                && savedTemplates.Any(item => item.Name == "Underscore layout") && savedTemplates.Any(item => item.Name == "Hyphen layout")
                && savedTemplates.Single(item => item.Name == "Underscore layout").TemplateId != savedTemplates.Single(item => item.Name == "Hyphen layout").TemplateId,
                kind + " preserves distinct valid page_a and page-a layout identities without deduplicating either saved template.");
            var chosen = savedTemplates.Single(item => item.Name == "Article layout");
            var cssPath = VisualPageProjectService.ResolveProjectPath(root, VisualPageProjectService.UserCssPath(session.Project, session.Page));
            var jsPath = VisualPageProjectService.ResolveProjectPath(root, VisualPageProjectService.UserJavaScriptPath(session.Project, session.Page));
            await File.AppendAllTextAsync(cssPath, "\n/* retained authored CSS */\n", ct);
            await File.AppendAllTextAsync(jsPath, "\n// retained authored JavaScript\n", ct);
            var cssBefore = await File.ReadAllTextAsync(cssPath, ct); var jsBefore = await File.ReadAllTextAsync(jsPath, ct);
            var homeId = session.Page.PageId;
            session = await projects.AddPageAsync(session, "Added article", "/new-article", new VisualPageTemplateService(catalog).Instantiate(chosen),
                title: "Added article", fileType: kind == StudioProjectKind.Html ? HyperpagePageFileType.Html : HyperpagePageFileType.Cshtml,
                starterTemplateId: chosen.TemplateId, starterTemplateName: chosen.Name, cancellationToken: ct);
            var projectRevision = session.Project.Revision; var pageRevision = session.Page.Revision;
            session.Page.Name = "Renamed article"; session.Page.Title = "Renamed article"; session.Page.Route = "/renamed-article";
            session.Page.Root.Styles["background-color"] = "#223344";
            session.Page.Root.Styles["background-image"] = "none";
            session.Page.Root.Styles["border"] = "5px solid #f2b84b";
            session.Page.Revision = VisualPageHashing.ComputePageRevision(session.Page, pageRevision);
            await projects.SaveAsync(session, projectRevision, pageRevision, ct);
            session = await projects.OpenAsync(root, session.Page.PageId, ct);
            Check(session.Page.Root.Styles.GetValueOrDefault("background-color") == "#223344"
                && session.Page.Root.Styles.GetValueOrDefault("background-image") == "none"
                && session.Page.Root.Styles.GetValueOrDefault("border") == "5px solid #f2b84b",
                kind + " saves and reopens Page-specific background and border changes independently from its reusable foundation.");
            var canvasPath = Path.Combine(output, kind.ToString().ToLowerInvariant() + "-authored-canvas.html");
            var canvasRootId = session.Page.Root.ElementId;
            var canvasHtml = new VisualPageDesignCanvasRenderer(catalog).Render(session.Page, null, VisualPageBreakpoint.Base,
                stylesheetUrls: new[] { VisualPageProjectService.UserCssPath(session.Project, session.Page) }
                    .Concat(session.Page.StylesheetPaths).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(path => HyperpageStylesheetService.PublishedUrl(session.Project, path)).ToArray(),
                foundationStylesheetUrls: (session.Project.SharedStylesheetPaths ?? [])
                    .Select(path => HyperpageStylesheetService.PublishedUrl(session.Project, path)).ToArray());
            await File.WriteAllTextAsync(canvasPath, canvasHtml, ct);
            using (var index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, StudioWebProjectNavigationIndex.RelativePath), ct)))
                Check(index.RootElement.GetProperty("pages").GetArrayLength() == 3 && index.RootElement.GetProperty("pages")[2].GetProperty("name").GetString() == "Renamed article"
                    && index.RootElement.GetProperty("pages")[2].GetProperty("route").GetString()!.StartsWith("/renamed-article", StringComparison.Ordinal),
                    kind + " Add Page and route/name edits update the public route index through the normal manifest transaction.");
            Check(await File.ReadAllTextAsync(cssPath, ct) == cssBefore && await File.ReadAllTextAsync(jsPath, ct) == jsBefore,
                kind + " adding and editing Pages preserves existing authored CSS and JavaScript.");
            session = await projects.OpenAsync(root, homeId, ct);
            projectRevision = session.Project.Revision; pageRevision = session.Page.Revision;
            Flatten(session.Page.Root).First(element => element.Text == "Reusable Body content").Text = "Authored landing content";
            session.Page.Revision = VisualPageHashing.ComputePageRevision(session.Page, pageRevision);
            await projects.SaveAsync(session, projectRevision, pageRevision, ct);
            Check(await File.ReadAllTextAsync(cssPath, ct) == cssBefore && await File.ReadAllTextAsync(jsPath, ct) == jsBefore && projects.InspectGeneratedDrift(session).Count == 0,
                kind + " editing real visual content keeps custom files and generated-source integrity.");
            browserFixtures.Add(new { kind = kind.ToString(), root, host = Path.Combine(root, "src", "Host"), canvasPath, canvasRootId,
                routes = new[] { "/", "/page-1/", "/renamed-article/" } });
        }
        var serverOnly = library.GetCatalog().First(choice => choice.Kind == "element" && choice.GroupId == "blank" && choice.ComponentName == "CounterBlock");
        var rejectedWork = ContentJson.Clone(work); rejectedWork.LandingTemplate.BodyLayout.Blocks[0].Elements.Add(new() { Kind = "template", TemplateObjectId = serverOnly.Id, TemplateTitle = serverOnly.Title });
        try
        {
            await scaffold.CreateAsync(new() { Kind = StudioProjectKind.Html, Name = "Unsupported tool", ParentDirectory = output, Work = rejectedWork }, ct);
            throw new InvalidOperationException("A server-only tool unexpectedly became inert HTML.");
        }
        catch (VisualPageValidationException error)
        { Check(error.Message.Contains(serverOnly.Title, StringComparison.Ordinal) && !Directory.Exists(Path.Combine(output, "Unsupported tool")), "Unsupported server tools produce a named compatibility error before creating a target or losing behavior."); }

        var fixture = Path.Combine(output, "studio-web-routes-browser.json");
        await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new { allRazorHost, completePages, projects = browserFixtures }, ContentJson.Options), ct);
        var script = Path.Combine(AppContext.BaseDirectory, "verify-studio-web-project-routes.cjs");
        if (!File.Exists(script)) script = Path.Combine(repositoryRoot, "tools", "verify-studio-web-project-routes.cjs");
        var run = await new BoundedProcessRunner().RunAsync(new()
        {
            ExecutablePath = "C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe", Arguments = [script, fixture, output],
            WorkingDirectory = output, Timeout = TimeSpan.FromMinutes(8), MaximumStandardOutputCharacters = 50000, MaximumStandardErrorCharacters = 50000
        }, ct);
        Check(run.ExitCode == 0, "All native Razor templates and both scratch web Hosts pass actual compiler and browser checks: " + run.StandardOutput + run.StandardError);
        var result = "STUDIO_WEB_PROJECT_ROUTES_OK|checks=" + checks.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "studio-web-project-routes-result.json"), JsonSerializer.Serialize(new { result, checks, fixture, nativeCompleteTemplates = all.Length }, ContentJson.Options), ct);
        return result;
    }

    private static int Count(VisualPageElement root) => Flatten(root).Count();
    private static IEnumerable<VisualPageElement> Flatten(VisualPageElement root)
    { yield return root; foreach (var child in root.Children) foreach (var element in Flatten(child)) yield return element; }
}
