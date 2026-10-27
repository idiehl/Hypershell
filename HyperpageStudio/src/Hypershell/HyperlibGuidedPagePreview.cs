using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HyperLib.Core;
using Hypershell.Models;
using Hypershell.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hypershell;

/// <summary>Persistent live Page builder. Only the newest draft reaches the browser.</summary>
public sealed class HyperlibGuidedPagePreview : ContentControl, IDisposable
{
    private readonly HyperlibGuidedPagePreviewService _service;
    private readonly string _browserProfile;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Border _surface = new() { Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)), MinHeight = 80 };
    private readonly TextBlock _status = new() { Text = "Your Page preview will appear here.", TextWrapping = TextWrapping.Wrap,
        FontSize = 12, Margin = new(0, 10, 0, 0), Opacity = .8 };
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private CancellationTokenSource _activation = new();
    private CancellationTokenSource? _debounce;
    private WebView2? _browser;
    private Uri? _allowed;
    private Uri? _documentAddress;
    private PreviewDraft? _draft;
    private long _version;
    private bool _disposed;
    private bool _attachedClose;
    private string? _displaySignature;
    private bool _displayInteractive;
    private string _documentId = "";
    private HyperlibHeaderDragCapability? _headerDrag;
    private bool _navigationInProgress;

    public event EventHandler<HyperlibHeaderDragEventArgs>? HeaderDragRequested;
    public event EventHandler<HyperlibHeaderResizeEventArgs>? HeaderResizeRequested;

    public HyperlibGuidedPagePreview() : this(null, null) { }

    internal HyperlibGuidedPagePreview(string? cacheRoot, string? browserProfile)
    {
        _service = new(cacheRoot);
        _browserProfile = browserProfile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell", "HyperlibGuidedPreview", "Browser");
        var panel = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom); panel.Children.Add(_status); panel.Children.Add(_surface); Content = panel;
        Loaded += (_, _) =>
        {
            if (_disposed) return;
            _activation.Dispose();
            _activation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            if (!_attachedClose && Window.GetWindow(this) is { } window) { window.Closed += (_, _) => Dispose(); _attachedClose = true; }
            if (_draft is not null) QueueUpdate();
        };
        Unloaded += (_, _) =>
        {
            if (_disposed) return;
            Interlocked.Increment(ref _version);
            InvalidateHeaderDrag();
            _debounce?.Cancel(); _activation.Cancel(); _service.Suspend();
            _browser?.Dispose(); _browser = null; _surface.Child = null; _allowed = null; _documentAddress = null; _displaySignature = null; _displayInteractive = false;
        };
    }

    public void SetDraft(string libraryRoot, HyperlibWorkProjectDefinition definition, HyperlibPageTemplateDefinition recipe, string title,
        string? ownedProjectRoot = null, string? headerDragTemplateId = null, string editingLayoutRegion = "header")
    {
        if (_disposed) return;
        _draft = new(libraryRoot, ContentJson.Clone(definition), ContentJson.Clone(recipe), title, ownedProjectRoot, headerDragTemplateId, editingLayoutRegion);
        Interlocked.Increment(ref _version);
        InvalidateHeaderDrag();
        _ = ApplyBackgroundAsync(_draft, Interlocked.Read(ref _version));
        if (!_displayInteractive && !HyperlibGuidedPagePreviewService.HasInteractiveElements(recipe))
            _ = ApplyHeaderAsync(_draft, Interlocked.Read(ref _version));
        // Header text/geometry and background color/media are local presentation changes. Keep the
        // current document and its other regions alive while a slider moves or the user types.
        if (_displayInteractive || HyperlibGuidedPagePreviewService.HasInteractiveElements(recipe) || _displaySignature != CompositionSignature(_draft)) QueueUpdate();
        else { _debounce?.Cancel(); _status.Text = "Live template preview · Empty slots receive content in Authoring."; }
    }

    private static string CompositionSignature(PreviewDraft draft)
    {
        var recipe = ContentJson.Clone(draft.Recipe);
        recipe.BackgroundColor = ""; recipe.BackgroundSurface = new();
        recipe.HeaderLayout = new() { Enabled = recipe.HeaderLayout.Enabled };
        recipe.BodyLayout = new() { Enabled = recipe.BodyLayout.Enabled };
        recipe.FooterLayout = new() { Enabled = recipe.FooterLayout.Enabled };
        return System.Text.Json.JsonSerializer.Serialize(new { draft.LibraryRoot, draft.OwnedProjectRoot, draft.Title,
            ids = draft.Definition.SelectedToolsetIds.Order(StringComparer.Ordinal), recipe }, ContentJson.Options);
    }

    private async Task ApplyHeaderAsync(PreviewDraft draft, long version)
    {
        if (_disposed || !IsLoaded || _displayInteractive || HyperlibGuidedPagePreviewService.HasInteractiveElements(draft.Recipe)
            || _browser?.CoreWebView2 is null || _allowed is null) return;
        try
        {
            foreach (var (region, layout, visible) in new[] {
                ("header", draft.Recipe.HeaderLayout, draft.Recipe.HeaderPlacement == "top"),
                ("body", draft.Recipe.BodyLayout, true),
                ("footer", draft.Recipe.FooterLayout, draft.Recipe.FooterPlacement == "bottom") })
            {
                var html = visible ? await _service.PreviewHeaderHtmlAsync(draft.LibraryRoot, layout,
                    draft.OwnedProjectRoot, _activation.Token, region) : "";
                if (_disposed || version != Interlocked.Read(ref _version) || _browser?.CoreWebView2 is null || _allowed is null) return;
                await _browser.CoreWebView2.ExecuteScriptAsync(HyperlibGuidedPagePreviewService.HeaderUpdateScript(html, region));
            }
            await ConfigureHeaderDragAsync(draft, version);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException)
        { /* Incomplete text/media settings retain the latest valid header. */ }
    }

    private async Task ApplyBackgroundAsync(PreviewDraft draft, long version)
    {
        if (_disposed || !IsLoaded || _browser?.CoreWebView2 is null || _allowed is null) return;
        try
        {
            var css = await _service.PreviewBackgroundCssAsync(draft.LibraryRoot, draft.Recipe.BackgroundColor,
                draft.Recipe.BackgroundSurface, draft.OwnedProjectRoot, _activation.Token);
            if (_disposed || version != Interlocked.Read(ref _version) || _browser?.CoreWebView2 is null || _allowed is null) return;
            // Parse trusted CSS once, then replace every surface longhand, including image:none and
            // border:0. Old designs cannot accumulate beneath the latest slider or image selection.
            await _browser.CoreWebView2.ExecuteScriptAsync("(()=>{const root=document.querySelector('[data-guided-page-preview]');if(!root)return false;"
                + "const surface=document.createElement('div').style;surface.cssText=" + System.Text.Json.JsonSerializer.Serialize(css) + ";"
                + "root.style.setProperty('--hl-paper'," + System.Text.Json.JsonSerializer.Serialize(draft.Recipe.BackgroundColor) + ");"
                + "for(let i=0;i<surface.length;i++){const key=surface.item(i);root.style.setProperty(key,surface.getPropertyValue(key));}return true;})()");
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException)
        { /* Keep the last valid surface while a typed color or image path is incomplete. */ }
    }

    private void QueueUpdate()
    {
        _debounce?.Cancel(); _debounce?.Dispose();
        _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _activation.Token);
        _ = UpdateAsync(_debounce.Token);
    }

    private async Task UpdateAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(320, debounceToken);
            if (_disposed || !IsLoaded || _draft is null) return;
            await _renderGate.WaitAsync(debounceToken);
            try
            {
                // Let one source preparation finish even when typing continues. The following queued
                // update will reuse that build; a stale result never replaces the visible Page.
                var version = Interlocked.Read(ref _version);
                var draft = _draft;
                var activeToken = _activation.Token;
                _status.Text = _browser is null ? "Preparing your live Page preview..." : "Updating your Page...";
                var preview = await Task.Run(() => _service.PreviewAsync(draft.LibraryRoot, draft.Definition, draft.Recipe, draft.Title, activeToken,
                    draft.OwnedProjectRoot), activeToken);
                if (_disposed || version != Interlocked.Read(ref _version)) return;
                await EnsureBrowserAsync();
                if (_disposed || version != Interlocked.Read(ref _version)) return;
                var canRefresh = preview.Interactive && _displayInteractive && _allowed is not null
                    && _allowed.GetLeftPart(UriPartial.Authority) == preview.Url.GetLeftPart(UriPartial.Authority);
                var refreshed = false;
                if (canRefresh)
                {
                    // The shared component tree owns its DOM. Send a new retained payload to the
                    // existing Blazor circuit so unchanged tools retain their live instances.
                    var capability = PrepareHeaderDrag(draft, version);
                    var script = "(()=>{if(!window.hyperlibGuidedPreview)return false;(async()=>{const accepted=await window.hyperlibGuidedPreview.update("
                        + JsonSerializer.Serialize(preview.RequestId) + "," + version + ");if(!accepted)return;"
                        + HeaderDragConfigurationScript(capability, version)
                        + "})().catch(error=>console.error('Page preview update failed',error));return true;})()";
                    refreshed = await _browser!.CoreWebView2.ExecuteScriptAsync(script) == "true";
                }
                _allowed = preview.Url;
                _displaySignature = CompositionSignature(draft);
                _displayInteractive = preview.Interactive;
                if (!refreshed) { InvalidateHeaderDrag(); _documentAddress = preview.Url; _browser!.CoreWebView2.Navigate(preview.Url.AbsoluteUri); }
                _status.Text = preview.Message;
            }
            finally { _renderGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_disposed && !debounceToken.IsCancellationRequested)
                _status.Text = "The Page preview needs attention: " + error.Message;
        }
    }

    private async Task EnsureBrowserAsync()
    {
        if (_browser is not null) return;
        var browser = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
        _browser = browser; _surface.Child = browser;
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _browserProfile);
        if (_disposed || !IsLoaded || !ReferenceEquals(_browser, browser)) return;
        await browser.EnsureCoreWebView2Async(environment);
        if (_disposed || !IsLoaded || !ReferenceEquals(_browser, browser)) return;
        var core = browser.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = true;
        core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(HyperlibHeaderPreviewDragSource.Script);
        core.NavigationStarting += (_, args) =>
        {
            if (_documentAddress is null || !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(_documentAddress, args.Uri)) { args.Cancel = true; return; }
            _navigationInProgress = true; _documentId = Guid.NewGuid().ToString("N"); InvalidateHeaderDrag();
        };
        core.WebMessageReceived += (_, args) => HandleHeaderDragMessage(args.Source, args.WebMessageAsJson);
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.NavigationCompleted += async (_, args) =>
        {
            _navigationInProgress = false;
            if (args.IsSuccess && _draft is { } latest)
            {
                var version = Interlocked.Read(ref _version);
                await ApplyBackgroundAsync(latest, version);
                if (!_displayInteractive) await ApplyHeaderAsync(latest, version);
                else await ConfigureHeaderDragAsync(latest, version);
            }
            else InvalidateHeaderDrag();
        };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!IsAllowed(args.Request.Uri) && !args.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Local preview only", "");
        };
    }

    private bool IsAllowed(string address) => _allowed is not null && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (uri.Scheme == _allowed.Scheme || _allowed.Scheme == "http" && uri.Scheme == "ws")
        && uri.Host == _allowed.Host && uri.Port == _allowed.Port;

    private void InvalidateHeaderDrag()
    {
        _headerDrag = null;
        _ = CancelHeaderDragAsync(Interlocked.Read(ref _version));
    }

    private async Task CancelHeaderDragAsync(long version)
    {
        try
        {
            if (_browser?.CoreWebView2 is not { } core) return;
            await core.ExecuteScriptAsync("window.__hyperlibNativeDraftVersion=" + version
                + ";window.hyperlibGuidedHeaderDrag?.configure(null);");
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private HyperlibHeaderDragCapability? PrepareHeaderDrag(PreviewDraft draft, long version)
    {
        if (_disposed || !IsLoaded || _navigationInProgress || version != Interlocked.Read(ref _version)
            || !ReferenceEquals(draft, _draft) || string.IsNullOrEmpty(draft.HeaderDragTemplateId)
            || draft.EditingLayoutRegion is not ("header" or "navigation" or "body" or "footer")
            || draft.EditingLayoutRegion == "header" && (draft.Recipe.HeaderPlacement != "top" || !draft.Recipe.HeaderLayout.Enabled)
            || draft.EditingLayoutRegion == "navigation" && !draft.Recipe.NavigationLayout.Enabled
            || draft.EditingLayoutRegion == "body" && !draft.Recipe.BodyLayout.Enabled
            || draft.EditingLayoutRegion == "footer" && (draft.Recipe.FooterPlacement != "bottom" || !draft.Recipe.FooterLayout.Enabled)
            || _documentAddress is null || _documentId.Length == 0 || _browser?.CoreWebView2 is not { } core
            || !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(_documentAddress, core.Source)) return null;
        return _headerDrag = new(Guid.NewGuid().ToString("N"), version, _documentId, draft.HeaderDragTemplateId,
            _documentAddress, draft.EditingLayoutRegion switch { "navigation" => draft.Recipe.NavigationLayout, "body" => draft.Recipe.BodyLayout, "footer" => draft.Recipe.FooterLayout, _ => draft.Recipe.HeaderLayout }, draft.EditingLayoutRegion);
    }

    private static string HeaderDragConfigurationScript(HyperlibHeaderDragCapability? capability, long version)
    {
        var config = capability is null ? "null" : JsonSerializer.Serialize(new { token = capability.Token,
            generation = capability.Generation, documentId = capability.DocumentId, region = capability.Region });
        return "if(window.__hyperlibNativeDraftVersion!==undefined&&window.__hyperlibNativeDraftVersion!==" + version + ")return;"
            + "await window.hyperlibGuidedPreview?.whenReady?.();"
            + "if(window.__hyperlibNativeDraftVersion!==undefined&&window.__hyperlibNativeDraftVersion!==" + version + ")return;"
            + "window.__hyperlibNativeDraftVersion=" + version + ";window.hyperlibGuidedHeaderDrag?.configure(" + config + ");";
    }

    private async Task ConfigureHeaderDragAsync(PreviewDraft draft, long version)
    {
        try
        {
            if (_disposed || version != Interlocked.Read(ref _version) || _browser?.CoreWebView2 is not { } core) return;
            var capability = PrepareHeaderDrag(draft, version);
            await core.ExecuteScriptAsync("(async()=>{" + HeaderDragConfigurationScript(capability, version) + "})().catch(()=>{});");
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private void HandleHeaderDragMessage(string source, string json)
    {
        if (_disposed || !IsLoaded || _navigationInProgress || _draft is not { } draft || _headerDrag is not { } capability
            || _browser?.CoreWebView2 is not { } core || draft.HeaderDragTemplateId != capability.TemplateId
            || draft.EditingLayoutRegion != capability.Region) return;
        var moved = capability.TryReceive(source, core.Source, json, Interlocked.Read(ref _version), out var move);
        HyperlibHeaderResizeEventArgs? resize = null;
        var resized = !moved && capability.TryReceiveResize(source, core.Source, json, Interlocked.Read(ref _version), out resize);
        if (!moved && !resized) return;
        // A drop consumes its capability before invoking the owner. An accepted commit synchronously
        // queues a new draft; replayed messages can never create another undo item or move another Page.
        _headerDrag = null;
        var accepted = false;
        if (moved && move is not null) { HeaderDragRequested?.Invoke(this, move); accepted = move.Accepted; }
        else if (resize is not null) { HeaderResizeRequested?.Invoke(this, resize); accepted = resize.Accepted; }
        if (!accepted) _ = RestoreRejectedHeaderDragAsync(draft, capability.Generation);
        else if (ReferenceEquals(draft, _draft) && capability.Generation == Interlocked.Read(ref _version))
            _ = ConfigureHeaderDragAsync(draft, capability.Generation);
    }

    private async Task RestoreRejectedHeaderDragAsync(PreviewDraft draft, long version)
    {
        await CancelHeaderDragAsync(Interlocked.Read(ref _version));
        if (_disposed || !ReferenceEquals(draft, _draft) || version != Interlocked.Read(ref _version)) return;
        if (_displayInteractive) { Interlocked.Increment(ref _version); InvalidateHeaderDrag(); QueueUpdate(); }
        else await ApplyHeaderAsync(draft, version);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvalidateHeaderDrag();
        _lifetime.Cancel(); _activation.Cancel(); _debounce?.Cancel(); _debounce?.Dispose(); _service.Dispose();
        _browser?.Dispose(); _browser = null; _surface.Child = null;
    }

    private sealed record PreviewDraft(string LibraryRoot, HyperlibWorkProjectDefinition Definition, HyperlibPageTemplateDefinition Recipe, string Title,
        string? OwnedProjectRoot, string? HeaderDragTemplateId, string EditingLayoutRegion);
}

/// <summary>A drop request from the current owned preview, already checked against its displayed recipe.</summary>
public sealed class HyperlibHeaderDragEventArgs(string templateId, string elementId, string parentId, double x, double y, double previousX, double previousY, string region = "header") : EventArgs
{
    public string TemplateId { get; } = templateId;
    public string Region { get; } = region;
    public string ElementId { get; } = elementId;
    public string ParentId { get; } = parentId;
    public double X { get; } = x;
    public double Y { get; } = y;
    public double PreviousX { get; } = previousX;
    public double PreviousY { get; } = previousY;
    public bool Accepted { get; set; }
}

public sealed class HyperlibHeaderResizeEventArgs(string templateId,string region,string elementId,string parentId,string arrangement,string handle,
    double x,double y,double width,double height,double parentWidth,double parentHeight,HyperlibHeaderElementDefinition previous) : EventArgs
{
    public string TemplateId { get; }=templateId; public string Region { get; }=region;
    public string ElementId { get; }=elementId; public string ParentId { get; }=parentId;
    public string Arrangement { get; }=arrangement; public string Handle { get; }=handle;
    public double X { get; }=x; public double Y { get; }=y; public double Width { get; }=width; public double Height { get; }=height;
    public double ParentWidth { get; }=parentWidth; public double ParentHeight { get; }=parentHeight;
    public HyperlibHeaderElementDefinition Previous { get; }=previous; public bool Accepted { get; set; }
}

/// <summary>One-shot capability shared by the real WebView event and isolated native bridge checks.</summary>
internal sealed class HyperlibHeaderDragCapability(string token, long generation, string documentId, string templateId,
    Uri document, HyperlibHeaderLayoutDefinition layout, string region = "header")
{
    public string Token { get; } = token;
    public long Generation { get; } = generation;
    public string DocumentId { get; } = documentId;
    public string TemplateId { get; } = templateId;
    public string Region { get; } = region;
    private bool _ready, _consumed;

    internal bool TryReceive(string source, string currentDocument, string json, long currentGeneration, out HyperlibHeaderDragEventArgs? move)
    {
        move = null;
        if (_consumed || currentGeneration != Generation || json.Length > 4096
            || !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(document, source)
            || !HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(document, currentDocument)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var value = parsed.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count()
                || Text(value, "token") != Token || Text(value, "documentId") != DocumentId
                || (Text(value,"region") is { Length: > 0 } messageRegion ? messageRegion : "header") != Region
                || !value.TryGetProperty("generation", out var version) || !version.TryGetInt64(out var receivedVersion) || receivedVersion != Generation) return false;
            var type = Text(value, "type");
            if (type == "studio-header-drag-ready") { _ready = true; return false; }
            if (!_ready || type != "studio-header-drag" || !value.TryGetProperty("sequence", out var sequence)
                || !sequence.TryGetInt64(out var number) || number < 1 || number > 9007199254740991) return false;
            var elementId = Text(value, "elementId"); var parentId = Text(value, "parentId");
            if (elementId.Length is 0 or > 200 || parentId.Length is 0 or > 200
                || !Coordinate(value, "x", out var x) || !Coordinate(value, "y", out var y)
                || !Coordinate(value, "previousX", out var previousX) || !Coordinate(value, "previousY", out var previousY)) return false;
            var candidates = new List<(HyperlibHeaderElementDefinition Element, string Parent, string Arrangement)>();
            void Visit(IEnumerable<HyperlibHeaderElementDefinition> elements, string parent, string arrangement)
            {
                foreach (var element in elements)
                {
                    if (element.Id == elementId) candidates.Add((element, parent, arrangement));
                    if (element.Kind == "box") Visit(element.Children, element.Id, element.Arrangement);
                }
            }
            foreach (var row in layout.Blocks) Visit(row.Elements, row.Id, row.Arrangement);
            if (!layout.Enabled || candidates.Count != 1 || candidates[0].Parent != parentId || candidates[0].Arrangement != "positioned"
                || candidates[0].Element.X != previousX || candidates[0].Element.Y != previousY) return false;
            _consumed = true;
            move = new(TemplateId, elementId, parentId, x, y, previousX, previousY, Region);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException) { return false; }
    }

    internal bool TryReceiveResize(string source,string currentDocument,string json,long currentGeneration,out HyperlibHeaderResizeEventArgs? resize)
    {
        resize=null;
        if(_consumed||!_ready||currentGeneration!=Generation||json.Length>4096
            ||!HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(document,source)
            ||!HyperlibGuidedPagePreviewService.IsPreviewDocumentNavigation(document,currentDocument))return false;
        try
        {
            using var parsed=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=4});var value=parsed.RootElement;
            if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count()
                ||Text(value,"type")!="studio-header-resize"||Text(value,"token")!=Token||Text(value,"documentId")!=DocumentId
                ||(Text(value,"region") is {Length:>0} suppliedRegion?suppliedRegion:"header")!=Region
                ||!value.TryGetProperty("generation",out var generationValue)||!generationValue.TryGetInt64(out var generationNumber)||generationNumber!=Generation
                ||!value.TryGetProperty("sequence",out var sequenceValue)||!sequenceValue.TryGetInt64(out var sequenceNumber)||sequenceNumber<1||sequenceNumber>9007199254740991)return false;
            var id=Text(value,"elementId");var parentId=Text(value,"parentId");var handle=Text(value,"handle");
            if(handle is not("n" or "ne" or "e" or "se" or "s" or "sw" or "w" or "nw")
                ||!Coordinate(value,"x",out var x)||!Coordinate(value,"y",out var y)
                ||!Dimension(value,"width",out var width)||!Dimension(value,"height",out var height)
                ||!Dimension(value,"parentWidth",out var parentWidth)||!Dimension(value,"parentHeight",out var parentHeight)
                ||!value.TryGetProperty("previous",out var prior)||prior.ValueKind!=JsonValueKind.Object
                ||prior.EnumerateObject().Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=prior.EnumerateObject().Count()
                ||!Dimension(prior,"renderedWidth",out var renderedWidth)||!Dimension(prior,"renderedHeight",out var renderedHeight))return false;
            var matches=new List<(HyperlibHeaderElementDefinition Element,string Parent,string Arrangement)>();
            void Visit(IEnumerable<HyperlibHeaderElementDefinition> elements,string parent,string arrangement)
            {foreach(var element in elements){if(element.Id==id)matches.Add((element,parent,arrangement));if(element.Kind=="box")Visit(element.Children,element.Id,element.Arrangement);}}
            foreach(var row in layout.Blocks)Visit(row.Elements,row.Id,row.Arrangement);
            if(!layout.Enabled||matches.Count!=1||matches[0].Parent!=parentId)return false;
            var match=matches[0];var baseline=match.Element;
            bool Exact(string key,double expected)=>prior.TryGetProperty(key,out var field)&&field.TryGetDouble(out var number)&&double.IsFinite(number)&&number==expected;
            if(Text(prior,"widthMode")!=baseline.WidthMode||Text(prior,"positionAlignment")!=baseline.PositionAlignment
                ||!Exact("x",baseline.X)||!Exact("y",baseline.Y)||!Exact("widthPercent",baseline.WidthPercent)
                ||!Exact("widthPixels",baseline.WidthPixels)||!Exact("height",baseline.Height)
                ||parentWidth<=0||parentHeight<=0||baseline.WidthMode=="pixels"&&width>4000||height>4000||width>parentWidth+.5
                ||match.Arrangement=="positioned"&&(height>parentHeight+.5||x/100*parentWidth+width>parentWidth+.5||y/100*parentHeight+height>parentHeight+.5))return false;
            var physicalFloor=2*(baseline.Padding+(baseline.BorderStyle=="none"?0:baseline.BorderWidth));
            var minimum=Math.Max(16,physicalFloor);
            var minimumWidth=Math.Max(minimum,baseline.WidthMode=="percent"?parentWidth*.01:0);
            if((handle.Contains('e')||handle.Contains('w'))&&width<minimumWidth-.01
                ||(handle.Contains('n')||handle.Contains('s'))&&height<minimum-.01)return false;
            var expectedWidth=Math.Max(physicalFloor,Math.Min(parentWidth,baseline.WidthMode=="pixels"?baseline.WidthPixels:parentWidth*baseline.WidthPercent/100));
            if(renderedWidth>expectedWidth+1.5||match.Arrangement!="horizontal"&&Math.Abs(renderedWidth-expectedWidth)>1.5)return false;
            if(baseline.Height>0)
            {
                var expectedHeight=Math.Max(physicalFloor,match.Arrangement=="positioned"&&baseline.Kind is "image" or "video"?Math.Min(baseline.Height,parentHeight):baseline.Height);
                if(Math.Abs(renderedHeight-expectedHeight)>1.5)return false;
            }
            _consumed=true;resize=new(TemplateId,Region,id,parentId,match.Arrangement,handle,x,y,width,height,parentWidth,parentHeight,ContentJson.Clone(baseline));return true;
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or FormatException or OverflowException){return false;}
    }

    private static bool Dimension(JsonElement value,string name,out double result)
    {result=0;return value.TryGetProperty(name,out var field)&&field.TryGetDouble(out result)&&double.IsFinite(result)&&result>=0&&result<=16000;}

    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static bool Coordinate(JsonElement value, string name, out double result)
    {
        result = 0;
        return value.TryGetProperty(name, out var item) && item.TryGetDouble(out result) && double.IsFinite(result) && result >= 0 && result <= 100;
    }
}
