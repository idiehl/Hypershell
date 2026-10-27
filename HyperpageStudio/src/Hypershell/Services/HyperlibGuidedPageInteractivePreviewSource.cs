namespace Hypershell.Services;

public sealed partial class HyperlibGuidedPagePreviewService
{
    /// <summary>Resources stay on the owned host; document navigation also stays on the current preview route.</summary>
    public static bool IsPreviewDocumentNavigation(Uri document, string target)
        => Uri.TryCreate(target, UriKind.Absolute, out var next) && document.Scheme == next.Scheme && document.Host == next.Host
            && document.Port == next.Port && document.AbsolutePath == next.AbsolutePath && document.Query == next.Query;
    /// <summary>Changes only project-owned SDK copies; shared template fields belong to the native builder.</summary>
    public static string ProtectSharedTemplateFields(string script)
        => script.Replace("field.contentEditable = editing ? 'true' : 'false'; field.spellcheck = editing;",
            "const editable = editing && !field.closest('[data-studio-shared-template]'); field.contentEditable = editable ? 'true' : 'false'; field.spellcheck = editable;",
            StringComparison.Ordinal);

    private const string HostInteractiveApp = """
        @namespace HyperlibGuidedPreview
        @using Microsoft.AspNetCore.Components.Web
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"/><base href="/"/><meta name="viewport" content="width=device-width,initial-scale=1"/>
        <link rel="stylesheet" href="/_content/HyperLib.Runtime/renderer.css"/>
        <link rel="stylesheet" href="/_content/HyperLib.Work/work-theme.css"/>
        <link rel="stylesheet" href="/Host.styles.css"/>
        <style>html,body{margin:0;min-height:100%;background:transparent}.hl-document{box-sizing:border-box;min-height:100vh}.studio-staged-layout{padding:28px}.hl-region{min-width:0}.studio-empty-region{grid-column:1/-1;min-height:54px;border:1px dashed color-mix(in srgb,var(--hl-ink) 12%,transparent);border-radius:6px}.studio-slot-placeholder{display:block;border:1px dashed color-mix(in srgb,var(--hl-ink) 22%,transparent);padding:12px;border-radius:6px;color:var(--hl-muted);font:12px system-ui;text-align:center}</style>
        <style>@((Microsoft.AspNetCore.Components.MarkupString)Hypershell.Services.HyperlibHeaderLayouts.Css)</style>
        <style>@((Microsoft.AspNetCore.Components.MarkupString)Hypershell.Services.HyperlibNavigationLayouts.Css)</style>
        <HeadOutlet/></head><body><PreviewRoutes/><script src="/guided-preview.js"></script><script src="/_framework/blazor.web.js"></script></body></html>
        """;

    private const string HostInteractiveRoutes = """
        @namespace HyperlibGuidedPreview
        @using Microsoft.AspNetCore.Components.Routing
        <Router AppAssembly="@typeof(PreviewApp).Assembly"><Found Context="route"><RouteView RouteData="@route"/></Found>
        <NotFound><p role="alert">This local preview is unavailable.</p></NotFound></Router>
        """;

    private const string HostInteractivePage = """
        @namespace HyperlibGuidedPreview
        @page "/interactive/{RequestId}"
        @rendermode Microsoft.AspNetCore.Components.Web.RenderMode.InteractiveServer
        @implements IDisposable
        @using Microsoft.AspNetCore.Components
        @using Microsoft.JSInterop
        @inject PreviewContext Preview
        @inject NavigationManager Navigation
        @inject IJSRuntime JS
        @if (_error is not null) { <p data-preview-error role="alert">@_error</p> }
        else if (_data is not null) { <ObjectPage Data="_data" Interactive="true"/> }
        @code {
            [Parameter] public string RequestId { get; set; } = "";
            private PreviewData? _data;
            private string? _error;
            private long _version;
            private DotNetObjectReference<InteractivePage>? _reference;
            private (long Version, TaskCompletionSource<bool> Completion)? _pendingRender;
            protected override async Task OnParametersSetAsync()
            {
                try { _data = await PreviewDataLoader.ReadAsync(RequestId); Preview.Apply(_data, Navigation.BaseUri, RequestId); _error = null; }
                catch (Exception error) { _error = error.Message; }
            }
            protected override async Task OnAfterRenderAsync(bool firstRender)
            {
                // Async parameter loading can complete after an initial empty render. Connect once
                // the first real Page has rendered, rather than depending on that empty render's flag.
                if (_reference is null && _data is not null) {
                    _reference = DotNetObjectReference.Create(this);
                    await JS.InvokeVoidAsync("hyperlibGuidedPreview.connect", _reference);
                }
                if (_pendingRender is { } pending && pending.Version == _version) {
                    _pendingRender = null;
                    pending.Completion.TrySetResult(true);
                }
            }
            [JSInvokable] public async Task<bool> Refresh(string requestId, long version)
            {
                if (version <= _version) return false;
                _version = version;
                try {
                    var data = await PreviewDataLoader.ReadAsync(requestId);
                    if (version != _version) return false;
                    Preview.Apply(data, Navigation.BaseUri, RequestId); _data = data; _error = null;
                    _pendingRender?.Completion.TrySetResult(false);
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingRender = (version, completion);
                    await InvokeAsync(StateHasChanged);
                    return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                catch (Exception error) { _pendingRender?.Completion.TrySetResult(false); _pendingRender = null; _error = error.Message; await InvokeAsync(StateHasChanged); return false; }
            }
            public void Dispose() { _pendingRender?.Completion.TrySetResult(false); _pendingRender = null; _reference?.Dispose(); PreviewDataLoader.Active.TryRemove(RequestId, out _); }
        }
        """;

    private const string HostInteractiveContext = """
        using System.Collections.Concurrent;
        using System.Reflection;
        using System.Text.Json;
        using HyperLib.Core;
        using HyperLib.Runtime;
        using Microsoft.AspNetCore.Components;
        namespace HyperlibGuidedPreview;
        public sealed class PreviewContext {
            public RendererOptions? Options { get; private set; }
            public void Apply(PreviewData data, string origin, string initialRequestId) {
                PreviewDataLoader.Active[initialRequestId] = data.RequestId;
                if (Options is null) {
                    var assembly = Assembly.Load(Environment.GetEnvironmentVariable("HYPERLIB_GUIDED_PREVIEW_ASSEMBLY")!);
                    var components = assembly.GetTypes().Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type)).ToDictionary(type => type.Name, StringComparer.Ordinal);
                    components[nameof(StudioHeaderRowBlock)] = typeof(StudioHeaderRowBlock);
                    components[nameof(StudioNavigationLayoutBlock)] = typeof(StudioNavigationLayoutBlock);
                    components[nameof(StudioNavigationLinksBlock)] = typeof(StudioNavigationLinksBlock);
                    Options = new() { PackageId = "preview-work", GenerationId = "preview", GatewayUrl = origin.TrimEnd('/') + "/session/" + initialRequestId + "/",
                        PathBase = "", Template = data.Template, Components = components, ReadOnly = true };
                } else {
                    var template = Options.Template;
                    template.SchemaVersion = data.Template.SchemaVersion; template.TemplateId = data.Template.TemplateId; template.Title = data.Template.Title;
                    template.Regions = data.Template.Regions; template.Blocks = data.Template.Blocks; template.Appearance = data.Template.Appearance;
                    template.DefaultBlocks = data.Template.DefaultBlocks; template.EmptyPageDefault = data.Template.EmptyPageDefault;
                }
            }
        }
        public static class PreviewDataLoader {
            public static readonly ConcurrentDictionary<string,string> Active = new(StringComparer.Ordinal);
            public static async Task<PreviewData> ReadAsync(string requestId) {
                if(!Guid.TryParseExact(requestId,"N",out _)) throw new InvalidDataException("The preview identity is invalid.");
                var root = Environment.GetEnvironmentVariable("HYPERLIB_GUIDED_PREVIEW_CONTENT")!;
                var raw = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(root, requestId + ".json")), ContentJson.Options);
                var data = raw.Deserialize<PreviewData>(ContentJson.Options)! with { RequestId = requestId };
                var definition = raw.GetProperty("template"); Restore(definition, data.Template.Appearance, "appearance");
                foreach(var block in definition.GetProperty("blocks").EnumerateArray())
                    Restore(block, data.Template.Blocks.Single(item => item.Id == block.GetProperty("id").GetString()).Fields, "fields");
                TemplateValidator.ValidateDefinition(data.Template); TemplateValidator.ValidatePage(data.Template,data.Page); return data;
            }
            private static void Restore(JsonElement parent,List<FieldDefinition> fields,string property) {
                if(!parent.TryGetProperty(property,out var array)) return;
                foreach(var field in array.EnumerateArray()) if(field.TryGetProperty("defaultValue",out var value))
                    fields.Single(item => item.Id == field.GetProperty("id").GetString()).DefaultValue = value.Clone();
            }
            public static async Task<PreviewData> CurrentAsync(string initialId) => await ReadAsync(Active.GetValueOrDefault(initialId,initialId));
            public static LibraryState State(PreviewData data) {
                var pageNode = ContentJson.Clone(data.Node); pageNode.ParentId = "preview-work";
                return new() { LibraryId = "guided-preview-library", Nodes = [new() { Id = "library", Type = "library", Title = "Page builder" },
                    new() { Id = "preview-work", ParentId = "library", Type = "work", Title = data.Title }, pageNode], Pages = new() { [data.Page.Id] = data.Page } };
            }
        }
        """;

    private const string HostPreviewNavigationGuard = """
        (()=>{
          function anchorTarget(hash){
            let id;try{id=decodeURIComponent(hash.replace(/^#/,''));}catch{return null;}
            return id ? document.getElementById(id)||[...document.getElementsByName(id)].find(element=>element instanceof HTMLElement) : null;
          }
          document.addEventListener('click',event=>{
            const anchor=event.target.closest?.('a[href]');if(!anchor)return;
            const value=anchor.getAttribute('href')||'';
            let target;try{target=new URL(value,document.baseURI);}catch{event.preventDefault();return;}
            // A base URL is required by imported modules. Resolve genuine #targets against this
            // document explicitly so they scroll here instead of navigating to the host root.
            const current=new URL(location.href);
            const same=value.startsWith('#')||target.origin===current.origin&&target.pathname===current.pathname&&target.search===current.search;
            const destination=same?anchorTarget(value.startsWith('#')?value:target.hash):null;
            event.preventDefault();
            if(destination&&event.button===0&&!event.ctrlKey&&!event.metaKey&&!event.shiftKey&&!event.altKey){
              destination.scrollIntoView({block:'start',behavior:'instant'});
              current.hash=value.startsWith('#')?value:target.hash;history.replaceState(null,'',current.href);
            }
          },true);
        })();
        """;

    private const string HostInteractiveScript = HostPreviewNavigationGuard + "\n" + """
        (()=>{
          let bridge, readyResolve, pendingVersion=0;
          const ready=new Promise(resolve=>readyResolve=resolve);
          window.hyperlibGuidedPreview={
            connect(reference){bridge=reference;document.documentElement.dataset.guidedInteractiveReady='true';readyResolve();},
            whenReady(){return location.pathname.startsWith('/interactive/')?ready:Promise.resolve();},
            async update(requestId,version){
              pendingVersion=Math.max(pendingVersion,version);
              await ready;if(version!==pendingVersion)return false;
              const accepted=await bridge.invokeMethodAsync('Refresh',requestId,version);
              if(accepted&&version===pendingVersion)document.documentElement.dataset.guidedUpdateVersion=String(version);
              return accepted;
            }
          };
        })();
        """;
}
