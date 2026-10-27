using System.Text.Json;
using HyperLib.Core;
using HyperLib.Runtime;
using HyperLib.Runtime.Components;
using HyperLib.Work.Generated;
using Host.Components;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);
var templatePath = Environment.GetEnvironmentVariable("HYPERLIB_TEMPLATE") ?? Path.GetFullPath("../../authoring.template.json");
var template = JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(templatePath), ContentJson.Options)!;
TemplateValidator.ValidateDefinition(template);
var components = typeof(WorkPage).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t)).ToDictionary(t => t.Name, StringComparer.Ordinal);
foreach(var block in template.Blocks)
    if(!components.TryGetValue(block.Component, out var type) || type.GetProperty("Context")?.PropertyType != typeof(BlockContext))
        throw new InvalidOperationException($"Template block '{block.Id}' needs a compiled component '{block.Component}' with a BlockContext parameter.");
var options = new RendererOptions {
    PackageId = Environment.GetEnvironmentVariable("HYPERLIB_PACKAGE") ?? "library",
    GenerationId = Environment.GetEnvironmentVariable("HYPERLIB_GENERATION") ?? "preview",
    PathBase = Environment.GetEnvironmentVariable("HYPERLIB_PATH_BASE") ?? "",
    GatewayUrl = Environment.GetEnvironmentVariable("HYPERLIB_GATEWAY") ?? "http://127.0.0.1:5277",
    Template = template, Components = components, ShellComponent = typeof(WorkPage)
};
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<RendererContentClient>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var app = builder.Build();
if(!string.IsNullOrEmpty(options.PathBase)) app.UsePathBase(options.PathBase);
app.UseRouting();
app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapGet("/healthz", () => Results.Json(new {status="healthy",packageId=options.PackageId,generation=options.GenerationId,components=template.Blocks.Select(b => b.Id)}));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(WorkPage).Assembly, typeof(DynamicPage).Assembly);
app.Run();
