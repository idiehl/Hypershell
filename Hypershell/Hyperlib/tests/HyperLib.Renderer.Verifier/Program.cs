using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using HyperLib.Core;
using HyperLib.Runtime;
using HyperLib.Runtime.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

var assertions = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; }
var componentTypes = typeof(Program).Assembly.GetTypes().Where(type => typeof(IComponent).IsAssignableFrom(type)).ToDictionary(type => type.Name);
var definition = StarterTemplate.Create();
var options = new RendererOptions { PackageId = "test", GenerationId = "test", GatewayUrl = "http://127.0.0.1:5277/", PathBase = "", Template = BaseTemplateCatalog.CreateTemplate("woodland-editorial"), Components = componentTypes };
var services = new ServiceCollection().AddLogging().AddSingleton(options).AddSingleton<RendererContentClient>()
    .AddSingleton<IErrorBoundaryLogger, TestErrorLogger>().AddSingleton<NavigationManager>(new TestNavigation()).AddSingleton<IJSRuntime, NoJs>().BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
var renderedCatalogBlocks = 0;
foreach (var schema in definition.Blocks)
{
    Check(componentTypes.ContainsKey(schema.Component), $"Missing compiled component {schema.Component}");
    var values = schema.Fields.Where(field => field.DefaultValue.HasValue).ToDictionary(field => field.Id, field => field.DefaultValue!.Value);
    var html = await Render(schema.Component, values);
    Check(!string.IsNullOrWhiteSpace(html), $"Empty component {schema.Component}");
}
foreach (var info in BaseTemplateCatalog.List())
{
    var template = BaseTemplateCatalog.CreateTemplate(info.Id);
    foreach (var block in template.DefaultBlocks)
    {
        var schema = template.Blocks.Single(schema => schema.Id == block.Type);
        var values = schema.Fields.Where(field => field.DefaultValue.HasValue).ToDictionary(field => field.Id, field => field.DefaultValue!.Value);
        foreach (var pair in block.Values) values[pair.Key] = pair.Value;
        Check(!string.IsNullOrWhiteSpace(await Render(schema.Component, values)), $"Catalog '{info.Id}' block '{block.Id}' did not render");
        renderedCatalogBlocks++;
    }
}
var malicious = new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("<script>alert('x')</script>"), ["url"] = JsonSerializer.SerializeToElement("javascript:alert(1)") };
var banner = await Render("BannerBlock", malicious);
Check(banner.Contains("&lt;script&gt;") && !banner.Contains("<script>") && !banner.Contains("javascript:"), "Banner must encode text and reject executable links");
var embed = await Render("EmbedBlock", new() { ["url"] = JsonSerializer.SerializeToElement("https://example.com/resource"), ["title"] = JsonSerializer.SerializeToElement("Resource") });
Check(!embed.Contains("<iframe") && embed.Contains("Load embedded content") && embed.Contains("noopener noreferrer"), "Embeds must load only after consent and retain a safe fallback link");
var localEmbed = await Render("EmbedBlock", new() { ["url"] = JsonSerializer.SerializeToElement("http://127.0.0.1:5277/api/state") });
Check(!localEmbed.Contains("<iframe") && !localEmbed.Contains("href="), "Same-origin and loopback embeds must be rejected");
var form = await Render("FormBlock", new());
Check(form.Contains("type=\"email\"") && form.Contains("required") && form.Contains("maxlength=\"4000\"") && form.Contains("without sending or storing"), "Local form must validate bounded fields and explain local behavior");
var map = await Render("MapBlock", new() { ["latitude"] = JsonSerializer.SerializeToElement(34.2), ["longitude"] = JsonSerializer.SerializeToElement(-118.5), ["zoom"] = JsonSerializer.SerializeToElement(12) });
Check(!map.Contains("<iframe") && map.Contains("Load OpenStreetMap") && map.Contains("mlat=34.2") && map.Contains("mlon=-118.5"), "Map must render accessible coordinate fallback without fetching an external frame");
var styled = new PageBlock { Values = new() { ["span"] = JsonSerializer.SerializeToElement("two-thirds"), ["font"] = JsonSerializer.SerializeToElement("monospace"), ["align"] = JsonSerializer.SerializeToElement("center"), ["color"] = JsonSerializer.SerializeToElement("#123456") } };
Check(RendererOptions.BlockSpanClass(styled) == "hl-span-two-thirds" && RendererOptions.BlockStyle(styled).Contains("text-align:center") && RendererOptions.BlockStyle(styled).Contains("#123456"), "Presentation values must reach block layout");
styled.Values["color"] = JsonSerializer.SerializeToElement("red;background:url(https://attacker.invalid)");
styled.Values["font"] = JsonSerializer.SerializeToElement("serif;background:red");
Check(!RendererOptions.BlockStyle(styled).Contains("background"), "Presentation values must not inject CSS declarations");
Check(RendererOptions.FontFamily("Georgia, serif").StartsWith("Georgia") && RendererOptions.FontFamily("system-ui, sans-serif").StartsWith("system-ui"), "Existing work font choices must remain compatible");
var legacy = new PageDocument { Id = "legacy", Title = "Legacy heading", WorkId = "work", TemplateId = definition.TemplateId };
Check((await RenderPage(legacy)).Contains("hl-page-heading"), "A legacy page restored by undo must retain its implicit heading");
legacy.UsesEditableChrome = true;
Check(!(await RenderPage(legacy)).Contains("hl-page-heading"), "Removing every modern header must not recreate an implicit heading");
foreach (var type in new[] { "header", "banner" })
{
    legacy.UsesEditableChrome = false;
    legacy.Blocks = [new PageBlock { Id = "heading", Region = "main", Type = type, Values = new() { ["title"] = JsonSerializer.SerializeToElement("Explicit heading") } }];
    var html = await RenderPage(legacy);
    Check(html.Contains("Explicit heading") && !html.Contains("hl-page-heading"), $"An explicit {type} must suppress legacy implicit headings");
}
var siteState = new LibraryState
{
    Nodes = [
        new() { Id = "library", Type = "library", Title = "Personal library" },
        new() { Id = "collection", ParentId = "library", Type = "collection", Title = "Collection" },
        new() { Id = "series", ParentId = "collection", Type = "series", Title = "Series" },
        new() { Id = "work", ParentId = "series", Type = "work", Title = "A <quiet> & useful site", Cover = "/media/cover.svg", Appearance = new() { ["accent"] = ContentJson.Value("#123456"), ["variant"] = ContentJson.Value("field-journal") } },
        new() { Id = "later", ParentId = "work", Type = "chapter", Title = "Later chapter", Order = 2 },
        new() { Id = "second", ParentId = "later", Type = "page", WorkId = "work", Title = "Second page" },
        new() { Id = "early", ParentId = "work", Type = "volume", Title = "Early volume", Order = 1 },
        new() { Id = "first", ParentId = "early", Type = "page", WorkId = "work", Title = "First <page>", Order = 2 },
        new() { Id = "opening", ParentId = "early", Type = "page", WorkId = "work", Title = "Opening page", Order = 1 },
        new() { Id = "foreign-page", ParentId = "early", Type = "page", WorkId = "other-work", Title = "Foreign page", Order = 0 },
        new() { Id = "other-work", ParentId = "collection", Type = "work", Title = "Another Work" },
        new() { Id = "other-page", ParentId = "other-work", Type = "page", WorkId = "other-work", Title = "Another page" }
    ]
};
var workNode = siteState.Nodes.Single(node => node.Id == "work");
var siteModel = WorkSiteModel.Create(siteState, workNode)!;
Check(siteModel.Pages.Select(node => node.Id).SequenceEqual(["opening", "first", "second"]), "Work reading order must follow nested sibling order and exclude foreign pages");
Check(siteModel.LibraryTarget?.Id == "collection" && siteModel.Start?.Id == "opening", "Work landing must start with its first page and return to the nearest collection");
var siteHtml = await RenderBrowse(siteState, workNode, options);
Check(siteHtml.Contains("hl-work-landing") && siteHtml.Contains("hl-variant-field-journal") && siteHtml.Contains("--hl-accent:#123456"), "Work landing must inherit its variant and appearance");
Check(siteHtml.Contains("A &lt;quiet&gt; &amp; useful site") && siteHtml.Contains("src=\"/media/cover.svg\"") && siteHtml.Contains("Start reading") && siteHtml.Contains("View contents"), "Work landing must render escaped titles, its cover, and clear reading destinations");
Check(siteHtml.Contains("href=\"/browse/work?edit=0#work-contents\"") && siteHtml.Contains("data-work-contents-jump")
    && !siteHtml.Contains("href=\"#work-contents\""), "View contents must resolve to the current Work route even when the document has a runtime base URL");
Check(siteHtml.Contains("href=\"/#collection\"") && !siteHtml.Contains("foreign-page") && !siteHtml.Contains("other-page"), "Work navigation must return to the outer library and exclude another Work's pages");
Check(siteHtml.IndexOf("Opening page", StringComparison.Ordinal) < siteHtml.IndexOf("First &lt;page&gt;", StringComparison.Ordinal)
    && siteHtml.IndexOf("First &lt;page&gt;", StringComparison.Ordinal) < siteHtml.IndexOf("Second page", StringComparison.Ordinal), "Rendered Contents must retain nested reading order");
var chapter = siteState.Nodes.Single(node => node.Id == "later");
var chapterHtml = await RenderBrowse(siteState, chapter, options);
Check(chapterHtml.Contains("<h1>Later chapter</h1>") && WorkSiteModel.Create(siteState, chapter)?.Start?.Id == "second", "A deeper reading section must retain its Work site and start within that section");
var middle = new PageDocument { Id = "first", WorkId = "work", Title = "First page", TemplateId = definition.TemplateId, UsesEditableChrome = true,
    Blocks = [new() { Id = "heading", Type = "header", Region = "main", Values = new() { ["title"] = ContentJson.Value("Existing template heading") } }] };
var middleHtml = await RenderPage(middle, siteState);
Check(middleHtml.Contains("hl-work-nav") && middleHtml.Contains("rel=\"prev\"") && middleHtml.Contains("rel=\"next\"")
    && middleHtml.Contains("Existing template heading") && !middleHtml.Contains("hl-page-heading"), "Rendered pages must offer shared navigation and previous/next without repeating their template heading");
var collectionHtml = await RenderBrowse(siteState, siteState.Nodes.Single(node => node.Id == "collection"), options);
Check(collectionHtml.Contains("hl-browse") && !collectionHtml.Contains("hl-work-nav"), "Collection browsing must remain the library experience");
var snapshotPath = Environment.GetEnvironmentVariable("HYPERLIB_WORK_SITE_SNAPSHOT");
var renderedWorks = 0;
if (!string.IsNullOrWhiteSpace(snapshotPath))
{
    var snapshot = JsonSerializer.Deserialize<LibraryState>(await File.ReadAllTextAsync(snapshotPath), ContentJson.Options)
        ?? throw new InvalidOperationException("The read-only Work site snapshot could not be loaded.");
    var libraryRoot = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!)!.FullName;
    var works = snapshot.Nodes.Where(node => node.Type == "work").ToArray();
    Check(works.Length == 37, "The real-library fixture must contain all 37 Works");
    foreach (var work in works)
    {
        var templatePath = Path.Combine(libraryRoot, "Works", work.Id, "authoring.template.json");
        var template = JsonSerializer.Deserialize<TemplateDefinition>(await File.ReadAllTextAsync(templatePath), ContentJson.Options)!;
        var workOptions = new RendererOptions { PackageId = work.Id, GenerationId = "readonly-fixture", GatewayUrl = options.GatewayUrl,
            PathBase = "/_runtime/" + work.Id + "/readonly-fixture", Template = template, Components = componentTypes };
        var model = WorkSiteModel.Create(snapshot, work)!;
        var html = await RenderBrowse(snapshot, work, workOptions);
        var styles = Regex.Matches(html, "style=\"([^\"]*)\"").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value));
        Check(html.Contains("hl-work-landing") && html.Contains(workOptions.VariantClass(snapshot, work))
            && styles.Contains(workOptions.AppearanceStyle(snapshot, work)), $"Work '{work.Title}' did not render with its saved appearance");
        var allowed = WorkSiteModel.Flatten(model.Contents).Select(item => item.Node.Id).ToHashSet(StringComparer.Ordinal);
        allowed.Add(work.Id);
        if (model.LibraryTarget is { } target) allowed.Add(target.Id);
        var destinations = Regex.Matches(html, "data-hyperlib-navigate=\"([^\"]*)\"")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        Check(destinations.IsSubsetOf(allowed) && model.Pages.All(page => destinations.Contains(page.Id)), $"Work '{work.Title}' contains foreign or missing reading destinations");
        Check(model.Pages.Count == 0 || html.Contains("Start reading"), $"Work '{work.Title}' lacks a clear reading entry point");
        Check(html.Contains("<details class=\"hl-work-menu\">") || model.Pages.Count == 0, $"Work '{work.Title}' lacks a compact Contents menu");
        if (work.Id == "atlas-climate")
        {
            foreach (var pageNode in model.Pages)
            {
                var page = snapshot.Pages[pageNode.Id];
                var pageHtml = await RenderFrozenPage(page, snapshot, workOptions);
                var savedHtml = string.Join('\n', page.Blocks.Where(block => block.Values.ContainsKey("html"))
                    .Select(block => block.Values["html"].GetString()));
                Check(pageHtml.Contains("class=\"atlas-nav\"") && !pageHtml.Contains("class=\"hl-work-nav\""),
                    $"Atlas page '{page.Title}' must retain its own header without a second Work bar");
                Check(pageHtml.Contains("data-hyperlib-work-id=\"atlas-climate\"")
                    && pageHtml.Contains("data-hyperlib-library-id=\"atlas-universalis\""),
                    $"Atlas page '{page.Title}' lacks scoped Work/library navigation metadata");
                var hasOwnPagination = savedHtml.Contains("class=\"chapter-pagination\"", StringComparison.Ordinal);
                Check(!hasOwnPagination || !pageHtml.Contains("class=\"hl-work-pagination\""),
                    $"Atlas page '{page.Title}' repeats its own previous/next navigation");
                Check(hasOwnPagination || pageHtml.Contains("class=\"hl-work-pagination\""),
                    $"Atlas page '{page.Title}' lacks previous/next navigation");
            }
        }
        renderedWorks++;
    }
    Console.WriteLine($"HYPERLIB_WORK_SITES_OK works={renderedWorks} revision={snapshot.Revision} nested-order|themes|all-page-links|compact-contents|collection-return|encoded-markup");
}
Console.WriteLine($"HYPERLIB_RENDERER_OK assertions={assertions} components={definition.Blocks.Count} catalogBlocks={renderedCatalogBlocks}");

async Task<string> Render(string name, Dictionary<string, JsonElement> values) => await renderer.Dispatcher.InvokeAsync(async () =>
{
    var result = await renderer.RenderComponentAsync(componentTypes[name], ParameterView.FromDictionary(new Dictionary<string, object?> { ["Context"] = new BlockContext { Id = "test-block", Values = values } }));
    return result.ToHtmlString();
});
async Task<string> RenderPage(PageDocument page, LibraryState? state = null) => await renderer.Dispatcher.InvokeAsync(async () =>
{
    var result = await renderer.RenderComponentAsync<TestDynamicPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["TestDocument"] = page, ["TestState"] = state }));
    return result.ToHtmlString();
});
async Task<string> RenderBrowse(LibraryState state, LibraryNode node, RendererOptions browseOptions)
{
    using var browseServices = new ServiceCollection().AddLogging().AddSingleton(browseOptions)
        .AddSingleton<IErrorBoundaryLogger, TestErrorLogger>().AddSingleton<NavigationManager>(new TestNavigation()).AddSingleton<IJSRuntime, NoJs>().BuildServiceProvider();
    await using var browseRenderer = new HtmlRenderer(browseServices, browseServices.GetRequiredService<ILoggerFactory>());
    return await browseRenderer.Dispatcher.InvokeAsync(async () =>
    {
        var result = await browseRenderer.RenderComponentAsync<TestBrowsePage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["TestState"] = state, ["TestNode"] = node }));
        return result.ToHtmlString();
    });
}
async Task<string> RenderFrozenPage(PageDocument page, LibraryState state, RendererOptions pageOptions)
{
    using var pageServices = new ServiceCollection().AddLogging().AddSingleton(pageOptions)
        .AddSingleton<IErrorBoundaryLogger, TestErrorLogger>().AddSingleton<NavigationManager>(new TestNavigation()).AddSingleton<IJSRuntime, NoJs>().BuildServiceProvider();
    await using var pageRenderer = new HtmlRenderer(pageServices, pageServices.GetRequiredService<ILoggerFactory>());
    return await pageRenderer.Dispatcher.InvokeAsync(async () =>
    {
        var result = await pageRenderer.RenderComponentAsync<TestDynamicPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["TestDocument"] = page, ["TestState"] = state }));
        return result.ToHtmlString();
    });
}
sealed class TestDynamicPage : ComponentBase
{
    [Parameter] public PageDocument TestDocument { get; set; } = new();
    [Parameter] public LibraryState? TestState { get; set; }
    [Inject] public RendererOptions Options { get; set; } = null!;
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        // Exercise the real page markup without its network-loading lifecycle
        // or InteractiveServer boundary, which HtmlRenderer cannot host.
        var page = new DynamicPage();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(DynamicPage).GetField("_page", flags)!.SetValue(page, TestDocument);
        if (TestState is { } state)
        {
            var node = state.Nodes.Single(candidate => candidate.Id == TestDocument.Id);
            var site = WorkSiteModel.Create(state, node);
            typeof(DynamicPage).GetField("_state", flags)!.SetValue(page, state);
            typeof(DynamicPage).GetField("_pageNode", flags)!.SetValue(page, node);
            typeof(DynamicPage).GetField("_work", flags)!.SetValue(page, site?.Work);
            typeof(DynamicPage).GetField("_site", flags)!.SetValue(page, site);
        }
        typeof(DynamicPage).GetProperty("Options", flags)!.SetValue(page, Options);
        typeof(DynamicPage).GetMethod("BuildRenderTree", flags)!.Invoke(page, [builder]);
    }
}
sealed class TestBrowsePage : ComponentBase
{
    [Parameter] public LibraryState TestState { get; set; } = new();
    [Parameter] public LibraryNode TestNode { get; set; } = new();
    [Inject] public RendererOptions Options { get; set; } = null!;
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        // Render production markup with a frozen state rather than a running gateway.
        var page = new BrowsePage();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(BrowsePage).GetField("_state", flags)!.SetValue(page, TestState);
        typeof(BrowsePage).GetField("_node", flags)!.SetValue(page, TestNode);
        typeof(BrowsePage).GetField("_site", flags)!.SetValue(page, WorkSiteModel.Create(TestState, TestNode));
        typeof(BrowsePage).GetProperty("Options", flags)!.SetValue(page, Options);
        typeof(BrowsePage).GetMethod("BuildRenderTree", flags)!.Invoke(page, [builder]);
    }
}
sealed class TestErrorLogger : IErrorBoundaryLogger { public ValueTask LogErrorAsync(Exception exception) => throw new InvalidOperationException("Page renderer raised a component error.", exception); }
sealed class TestNavigation : NavigationManager { public TestNavigation() => Initialize("http://127.0.0.1:5277/", "http://127.0.0.1:5277/page/test"); protected override void NavigateToCore(string uri, bool forceLoad) { } }
sealed class NoJs : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException("Static render must not invoke JavaScript.");
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}
