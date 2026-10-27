namespace Hypershell.Services;

public static class HyperlibNavigationRenderingSource
{
    public const string LayoutComponent="""
        @using System.Text.Json
        @using HyperLib.Core
        @using HyperLib.Runtime
        @using Hypershell.Models
        @using Hypershell.Services
        @inject RendererOptions Options
        <div class="studio-navigation-layout" data-studio-layout-region="navigation" data-sticky="@Context.Values.GetValueOrDefault("sticky").ToString().ToLowerInvariant()">
            @if (_layout.Enabled)
            {
                @foreach (var row in _layout.Blocks)
                {
                    <DynamicComponent @key="row.Id" Type="@Options.Resolve(HyperlibHeaderLayouts.BlockType)" Parameters="@Parameters(row)" />
                }
            }
        </div>
        @code {
            [Parameter,EditorRequired] public BlockContext Context {get;set;}=new();
            private HyperlibHeaderLayoutDefinition _layout=new();
            protected override void OnParametersSet(){_layout=JsonSerializer.Deserialize<HyperlibHeaderLayoutDefinition>(Context.Text("layout","{}"),ContentJson.Options)??new();HyperlibHeaderLayouts.Validate(_layout);}
            private IDictionary<string,object> Parameters(HyperlibHeaderBlockDefinition row)=>new Dictionary<string,object>{["Context"]=new BlockContext{Id=Context.Id+"--"+row.Id,Editing=false,
                Values=new(){["layout"]=ContentJson.Value(JsonSerializer.Serialize(row,ContentJson.Options)),["studioRegion"]=ContentJson.Value("navigation")}}};
        }
        """;

    public const string LinksComponent="""
        @using Microsoft.AspNetCore.Components.Web
        @using System.Text.Json
        @using HyperLib.Core
        @using HyperLib.Runtime
        @using Hypershell.Models
        @using Hypershell.Services
        @inject RendererContentClient Content
        @inject RendererOptions Options
        @inject NavigationManager Navigation
        <style>@((MarkupString)HyperlibNavigationLayouts.ResponsiveCss(_key,_settings))</style>
        <nav class="studio-navigation-links @_key" data-direction="@_settings.Orientation" aria-label="Pages in this Work" style="@HyperlibNavigationLayouts.LinkStyle(_settings)">
            @if(_items.Count>0){<button type="button" class="studio-nav-menu" aria-expanded="@_open.ToString().ToLowerInvariant()" aria-controls="@(_key+"-items")" @onclick="()=>_open=!_open">@_settings.MenuLabel</button>}
            <ul class="studio-nav-items" id="@(_key+"-items")" data-open="@_open.ToString().ToLowerInvariant()">
                @foreach(var item in _items){<li @key="item.Id"><a href="@item.Href" target="@(item.NewTab?"_blank":null)" rel="@(item.NewTab?"noopener noreferrer":null)" aria-current="@(item.Active?"page":null)" @onclick="()=>_open=false">@item.Label</a></li>}
            </ul>
            @if(_items.Count==0&&!_settings.ShowPreviousNext&&(Context.Editing||Options.GenerationId=="preview")){<p class="studio-nav-empty">Pages added to this Work will appear here.</p>}
            @if(_settings.ShowPreviousNext&&Options.GenerationId=="preview"&&_pages.Count<=1){<div class="studio-nav-steps" data-studio-nav-preview-steps><span aria-disabled="true">← Previous Page</span><span aria-disabled="true">Next Page →</span></div>}
            else if(_settings.ShowPreviousNext&&_currentIndex>=0){<div class="studio-nav-steps">@if(_currentIndex>0){<a rel="prev" href="@_pages[_currentIndex-1].Href">← @_pages[_currentIndex-1].Label</a>}@if(_currentIndex<_pages.Count-1){<a rel="next" href="@_pages[_currentIndex+1].Href">@_pages[_currentIndex+1].Label →</a>}</div>}
        </nav>
        @code{
            [Parameter,EditorRequired]public BlockContext Context{get;set;}=new();
            private HyperlibNavigationDefinition _settings=new();private string _key="nav-000000000000";private bool _open;private List<Link> _items=[];private List<Link> _pages=[];private int _currentIndex=-1;
            private sealed record Link(string Id,string Label,string Href,bool Active,bool NewTab=false);
            protected override async Task OnParametersSetAsync(){
                _settings=JsonSerializer.Deserialize<HyperlibNavigationDefinition>(Context.Text("settings","{}"),ContentJson.Options)??new();HyperlibNavigationLayouts.Validate(_settings);_key=HyperlibNavigationLayouts.Key(Context.Id);
                var state=await Content.GetStateAsync();var work=state.Nodes.FirstOrDefault(node=>node.Id==Options.PackageId&&node.Type=="work");
                var landing=work?.Appearance.TryGetValue("workLandingPage",out var value)==true&&value.ValueKind==JsonValueKind.String?value.GetString():null;
                var current=Context.Text("current");var path=new Uri(Navigation.Uri).AbsolutePath;var marker=path.LastIndexOf("/page/",StringComparison.Ordinal);
                if(marker>=0)current=Uri.UnescapeDataString(path[(marker+6)..]).TrimEnd('/');if(current.Length==0)current=landing??"";
                var nodes=state.Nodes.Where(node=>node.Type=="page"&&(node.WorkId==Options.PackageId||node.ParentId==Options.PackageId)).ToArray();
                if(landing is null)landing=nodes.OrderBy(node=>node.Order).FirstOrDefault()?.Id;
                var branches=state.Nodes.Where(node=>node.Id!=Options.PackageId&&(node.WorkId==Options.PackageId||node.ParentId==Options.PackageId)).ToLookup(node=>node.ParentId??"");
                var ordered=new List<LibraryNode>();var visited=new HashSet<string>(StringComparer.Ordinal);
                void Visit(string parent){foreach(var child in branches[parent].OrderBy(node=>node.Order).ThenBy(node=>node.Title,StringComparer.OrdinalIgnoreCase)){
                    if(!visited.Add(child.Id))continue;if(child.Type=="page"){if(child.Id!=landing)ordered.Add(child);}else Visit(child.Id);}}
                Visit(Options.PackageId);
                if(nodes.FirstOrDefault(node=>node.Id==landing) is {} home)ordered.Insert(0,home);
                // Isolated previews may provide a Page without its complete parent chain.
                foreach(var detached in nodes.OrderBy(node=>node.Order).ThenBy(node=>node.Title,StringComparer.OrdinalIgnoreCase))if(detached.Id!=landing&&visited.Add(detached.Id))ordered.Add(detached);
                _pages=ordered
                    .Select(node=>new Link(node.Id,node.Id==landing?_settings.HomeLabel:node.Title,"page/"+Uri.EscapeDataString(node.Id),node.Id==current)).ToList();
                _currentIndex=_pages.FindIndex(item=>item.Id==current);_items=[];
                if(_settings.IncludeHome&&landing is not null)_items.Add(new("home-"+landing,_settings.HomeLabel,"page/"+Uri.EscapeDataString(landing),current==landing));
                if(_settings.AutoPages)_items.AddRange(_pages.Where(item=>item.Id!=landing));
                foreach(var item in _settings.CustomLinks){var href=HyperlibNavigationLayouts.ResolveTarget(item.Target,landing);var target=item.Target.StartsWith("page:",StringComparison.Ordinal)?item.Target[5..]:item.Target.StartsWith("/page/",StringComparison.Ordinal)?item.Target[6..]:null;
                    _items.Add(new(item.Id,item.Label,href,target==current||item.Target=="/"&&current==landing,item.NewTab));}
            }
        }
        """;
}
