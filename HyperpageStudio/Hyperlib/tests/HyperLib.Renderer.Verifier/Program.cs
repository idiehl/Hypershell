using System.Text.Json;
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
var olderTemplate = ContentJson.Clone(options.Template);
olderTemplate.Appearance.RemoveAll(field => field.Id == "fontSize");
var olderOptions = new RendererOptions { PackageId = "test", GenerationId = "test", GatewayUrl = options.GatewayUrl,
    PathBase = "", Template = olderTemplate, Components = componentTypes };
var owningWork = new LibraryNode { Id = "sized-work", Type = "work", Appearance = new() { ["fontSize"] = ContentJson.Value(24.5) } };
var sizedPage = new LibraryNode { Id = "sized-page", Type = "page", ParentId = owningWork.Id };
var appearanceState = new LibraryState { Nodes = [owningWork, sizedPage] };
Check(olderOptions.AppearanceStyle(null, null).Contains("--hl-text-size:18px"), "Older Work templates receive a safe text size without a source rewrite");
var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    Check(olderOptions.AppearanceStyle(appearanceState, sizedPage).Contains("--hl-text-size:24.5px"),
        "Page typography inherits Work text size with culture-independent CSS numbers");
}
finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
sizedPage.Appearance["fontSize"] = ContentJson.Value(16);
Check(olderOptions.AppearanceStyle(appearanceState, sizedPage).Contains("--hl-text-size:16px"), "Existing local Page appearance retains priority over Work typography");
foreach (var unsafeSize in new[] { ContentJson.Value(100), ContentJson.Value("20;display:none") })
{
    sizedPage.Appearance["fontSize"] = unsafeSize;
    var renderedStyle = olderOptions.AppearanceStyle(appearanceState, sizedPage);
    Check(renderedStyle.Contains("--hl-text-size:18px") && !renderedStyle.Contains("display:none"), "Invalid saved text size cannot inject CSS or produce unbounded typography");
}
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
Console.WriteLine($"HYPERLIB_RENDERER_OK assertions={assertions} components={definition.Blocks.Count} catalogBlocks={renderedCatalogBlocks}");

async Task<string> Render(string name, Dictionary<string, JsonElement> values) => await renderer.Dispatcher.InvokeAsync(async () =>
{
    var result = await renderer.RenderComponentAsync(componentTypes[name], ParameterView.FromDictionary(new Dictionary<string, object?> { ["Context"] = new BlockContext { Id = "test-block", Values = values } }));
    return result.ToHtmlString();
});
async Task<string> RenderPage(PageDocument page) => await renderer.Dispatcher.InvokeAsync(async () =>
{
    var result = await renderer.RenderComponentAsync<TestDynamicPage>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["TestDocument"] = page }));
    return result.ToHtmlString();
});
sealed class TestDynamicPage : ComponentBase
{
    [Parameter] public PageDocument TestDocument { get; set; } = new();
    [Inject] public RendererOptions Options { get; set; } = null!;
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        // Exercise the real page markup without its network-loading lifecycle
        // or InteractiveServer boundary, which HtmlRenderer cannot host.
        var page = new DynamicPage();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(DynamicPage).GetField("_page", flags)!.SetValue(page, TestDocument);
        typeof(DynamicPage).GetProperty("Options", flags)!.SetValue(page, Options);
        typeof(DynamicPage).GetMethod("BuildRenderTree", flags)!.Invoke(page, [builder]);
    }
}
sealed class TestErrorLogger : IErrorBoundaryLogger { public ValueTask LogErrorAsync(Exception exception) => throw new InvalidOperationException("Page renderer raised a component error.", exception); }
sealed class TestNavigation : NavigationManager { public TestNavigation() => Initialize("http://127.0.0.1:5277/", "http://127.0.0.1:5277/page/test"); protected override void NavigateToCore(string uri, bool forceLoad) { } }
sealed class NoJs : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException("Static render must not invoke JavaScript.");
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}
