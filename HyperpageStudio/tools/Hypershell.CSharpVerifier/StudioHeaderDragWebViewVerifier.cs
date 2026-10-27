using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using Hypershell;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

/// <summary>Uses an isolated, permanently hidden WebView and CDP pointer input; never sends desktop input or opens a visible window.</summary>
public static class StudioHeaderDragWebViewVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string libraryRoot)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "studio-header-drag-webview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture); var receipt = new Receipt { Fixture = fixture, Repository = Path.GetFullPath(repositoryRoot), BrowserProfile = Path.Combine(fixture, "browser-profile") };
        try { await RunStaAsync(() => RunAsync(fixture, libraryRoot, receipt)); receipt.Passed = true; }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally { await File.WriteAllTextAsync(Path.Combine(fixture, "studio-header-drag-webview-verification.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine($"STUDIO_HEADER_DRAG_WEBVIEW_OK|checks={receipt.Assertions.Count}|fixture={fixture}");
    }

    private static async Task RunAsync(string fixture, string libraryRoot, Receipt receipt)
    {
        var foreground = GetForegroundWindow(); receipt.ForegroundBefore = foreground.ToInt64(); GetWindowThreadProcessId(foreground, out var beforeProcess); receipt.ForegroundBeforeProcessId = beforeProcess;
        var wizard = new HyperlibWorkWizardViewModel(libraryRoot); wizard.InitializeTitle("Hidden native drag acceptance"); wizard.Step = 1;
        var header = wizard.CurrentTemplate.HeaderLayout; var row = header.AddBlock(); row.ArrangeSideBySide = false; row.Height = 240;
        var element = header.AddElement(row, "text"); element.Text = "Drag this header"; element.X = 10.125; element.Y = 12.25; element.WidthPercent = 32.5; element.Height = 48;
        using var preview = new HyperlibGuidedPagePreview(Path.Combine(fixture, "preview-cache"), receipt.BrowserProfile);
        var xInput = new TextBox { Width = 130 }; xInput.SetBinding(TextBox.TextProperty, new Binding(nameof(element.X)) { Source = element, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var yInput = new TextBox { Width = 130 }; yInput.SetBinding(TextBox.TextProperty, new Binding(nameof(element.Y)) { Source = element, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var widthInput = new TextBox { Width = 130 }; widthInput.SetBinding(TextBox.TextProperty, new Binding(nameof(element.WidthPercent)) { Source = element, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var heightInput = new TextBox { Width = 130 }; heightInput.SetBinding(TextBox.TextProperty, new Binding(nameof(element.Height)) { Source = element, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var toggle = new CheckBox { Content = "Block Placement" }; toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(row.ArrangeSideBySide)) { Source = row, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var fields = new StackPanel { Orientation = Orientation.Horizontal }; fields.Children.Add(xInput); fields.Children.Add(yInput); fields.Children.Add(widthInput); fields.Children.Add(heightInput); fields.Children.Add(toggle);
        var panel = new DockPanel(); DockPanel.SetDock(fields, Dock.Bottom); panel.Children.Add(fields); panel.Children.Add(preview);
        using var host = new HwndSource(new HwndSourceParameters("Hidden isolated header drag WebView") { Width = 960, Height = 700, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080 });
        host.RootVisual = panel; panel.Width = 960; panel.Height = 700; panel.Measure(new Size(960, 700)); panel.Arrange(new Rect(0, 0, 960, 700)); panel.UpdateLayout();
        void SampleSafety(string stage)
        {
            var current = GetForegroundWindow(); GetWindowThreadProcessId(current, out var owner);
            var hidden = !IsWindowVisible(host.Handle); receipt.ForegroundSamples.Add(new(stage, current.ToInt64(), owner, hidden));
            if (!hidden || owner == Environment.ProcessId) throw new InvalidOperationException("The hidden QA host became visible or its process acquired the foreground during " + stage + ".");
        }
        var accepted = 0; var requested = 0;var acceptedResizes=0;var requestedResizes=0;string? resizeJson=null;
        void Push() => preview.SetDraft(libraryRoot, wizard.PreviewDefinition, wizard.CurrentTemplate.BuildDefinition(), wizard.CurrentPageTitle, null,
            wizard.IsHeaderStage||wizard.IsNavigationStage||wizard.IsBodyStage||wizard.IsFooterStage ? wizard.CurrentTemplate.Id : null,
            wizard.IsNavigationStage?"navigation":wizard.IsBodyStage?"body":wizard.IsFooterStage?"footer":"header");
        wizard.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(wizard.PreviewDefinition)) Push(); };
        preview.HeaderDragRequested += (_, args) =>
        {
            requested++;
            var layout=args.Region=="header"&&wizard.IsHeaderStage?wizard.CurrentTemplate.HeaderLayout
                :args.Region=="navigation"&&wizard.IsNavigationStage?wizard.CurrentTemplate.NavigationLayout
                :args.Region=="body"&&wizard.IsBodyStage?wizard.CurrentTemplate.BodyLayout
                :args.Region=="footer"&&wizard.IsFooterStage?wizard.CurrentTemplate.FooterLayout:null;
            if (layout is not null && args.TemplateId == wizard.CurrentTemplate.Id)
                args.Accepted = layout.TryMoveElement(args.ElementId, args.ParentId, args.X, args.Y, args.PreviousX, args.PreviousY);
            if (args.Accepted) accepted++; receipt.AcceptedDrops = accepted;
        };
        preview.HeaderResizeRequested+=(_,args)=>
        {
            requestedResizes++;
            var layout=args.Region=="header"&&wizard.IsHeaderStage?wizard.CurrentTemplate.HeaderLayout
                :args.Region=="navigation"&&wizard.IsNavigationStage?wizard.CurrentTemplate.NavigationLayout
                :args.Region=="body"&&wizard.IsBodyStage?wizard.CurrentTemplate.BodyLayout
                :args.Region=="footer"&&wizard.IsFooterStage?wizard.CurrentTemplate.FooterLayout:null;
            if(layout is not null&&args.TemplateId==wizard.CurrentTemplate.Id)args.Accepted=layout.TryResizeElement(args.ElementId,args.ParentId,args.Arrangement,args.Handle,
                args.X,args.Y,args.Width,args.Height,args.ParentWidth,args.ParentHeight,args.Previous);
            if(args.Accepted)acceptedResizes++;
        };
        Push();
        await WaitAsync(() => Core(preview) is not null && Capability(preview) is not null, "The isolated preview did not initialize its native drag capability.", TimeSpan.FromMinutes(3));
        var core = Core(preview)!;
        core.WebMessageReceived+=(_,args)=>{receipt.BrowserMessages.Add(args.WebMessageAsJson);using var message=JsonDocument.Parse(args.WebMessageAsJson);if(message.RootElement.TryGetProperty("type",out var kind)&&kind.GetString()=="studio-header-resize")resizeJson=args.WebMessageAsJson;};
        await WaitAsync(() => Ready(Capability(preview)), "The isolated preview did not complete the document-ready handshake.", TimeSpan.FromSeconds(40));
        Check(receipt, preview.IsLoaded && !IsWindowVisible(host.Handle), "The real preview is loaded in a permanently hidden, nonactivating HWND.");
        SampleSafety("preview-ready");
        Check(receipt, core.Environment.UserDataFolder.StartsWith(fixture, StringComparison.OrdinalIgnoreCase), "The WebView browser profile is isolated from the live Studio session.");
        var firstCapability = Capability(preview)!; var originalX = element.X; var originalY = element.Y;
        var source = core.Source;
        await core.ExecuteScriptAsync("window.__nativePointerLog=[];for(const kind of ['pointerdown','pointermove','pointerup','pointercancel','lostpointercapture'])document.addEventListener(kind,e=>{const n=e.target instanceof Element?e.target:null;window.__nativePointerLog.push({type:e.type,x:e.clientX,y:e.clientY,target:n?.outerHTML.slice(0,900),primary:e.isPrimary,buttons:e.buttons});if(window.__nativePointerLog.length>50)window.__nativePointerLog.shift();},true);");
        try
        {
            await DragAsync(core, element.Id, 40, 46, SampleSafety);
            await WaitAsync(() => accepted == 1, "A real pointer drag did not reach the native WebMessage event and model commit.", TimeSpan.FromSeconds(15));
        }
        catch
        {
            var capability=Capability(preview);
            receipt.BrowserDiagnostics=await core.ExecuteScriptAsync("(()=>({source:location.href,nativeVersion:window.__hyperlibNativeDraftVersion,pointers:window.__nativePointerLog,elements:[...document.querySelectorAll('[data-header-element]')].map(n=>{const b=n.getBoundingClientRect(),p=n.parentElement;return {html:n.outerHTML,rect:{x:b.x,y:b.y,width:b.width,height:b.height},parent:{html:p.outerHTML.slice(0,1500),width:p.clientWidth,height:p.clientHeight}};})}))()");
            receipt.CapabilityDiagnostics=capability is null?"null":JsonSerializer.Serialize(new{capability.Generation,capability.Region,capability.TemplateId,Ready=Ready(capability),requested,accepted});
            using var screenshot=File.Create(Path.Combine(fixture,"native-first-drag-failure.png"));await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
            throw;
        }
        Check(receipt, requested == 1 && header.SelectedElement == element && element.X > originalX + 5 && element.Y > originalY + 5,
            "Real WebView pointer input commits through the guarded native event and selects the moved element.");
        Check(receipt, double.TryParse(xInput.Text, out var displayedX) && Math.Abs(displayedX - element.X) < .000001
            && double.TryParse(yInput.Text, out var displayedY) && Math.Abs(displayedY - element.Y) < .000001,
            "The same drag updates the native X/Y fields through their existing bindings.");
        Check(receipt, element.WidthPercent == 32.5 && element.Height == 48, "Dragging preserves the element's responsive width and exact height.");
        var firstX = element.X; var firstY = element.Y;
        var replay = JsonSerializer.Serialize(new { type = "studio-header-drag", token = firstCapability.Token, generation = firstCapability.Generation, documentId = firstCapability.DocumentId,
            elementId = element.Id, parentId = row.Id, x = 99, y = 99, previousX = originalX, previousY = originalY, sequence = 1 });
        await core.ExecuteScriptAsync("window.chrome.webview.postMessage(" + replay + ");"); await Task.Delay(150);
        Check(receipt, accepted == 1 && requested == 1 && element.X == firstX && element.Y == firstY, "Replaying the actual prior document capability cannot commit another movement.");
        await WaitAsync(() => Ready(Capability(preview)), "The native preview did not rearm after its committed draft.", TimeSpan.FromSeconds(20));
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        await WaitAsync(async () => await core.ExecuteScriptAsync("document.querySelector('[data-header-row] > .studio-header-row-content')?.dataset.headerArrangement") == "\"horizontal\"", "The checked Block did not render horizontal placement.", TimeSpan.FromSeconds(15));
        await DragAsync(core, element.Id, 25, 25, SampleSafety); await Task.Delay(150);
        Check(receipt, accepted == 1 && element.X == firstX && element.Y == firstY, "Checking the actual Block Placement checkbox prevents the same pointer gesture from moving its element.");
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        await WaitAsync(() => Ready(Capability(preview)), "Unchecked free placement did not rearm the bridge.", TimeSpan.FromSeconds(20));
        await DragAsync(core, element.Id, 22, 26, SampleSafety);
        await WaitAsync(() => accepted == 2, "A second free-placement drag did not commit after toggling back.", TimeSpan.FromSeconds(15));
        Check(receipt, core.Source == source && requested == 2, "Static header edits and repeated drags retain the same preview document.");
        header.Undo();
        var restored = header.Blocks.Single().Elements.Single();
        Check(receipt, Math.Abs(restored.X - firstX) < 1e-9 && Math.Abs(restored.Y - firstY) < 1e-9 && restored.WidthPercent == 32.5,
            "One native Undo restores both coordinates of the second real drag without changing the element size.");
        element=restored;
        foreach(var (input,property) in new[]{(xInput,nameof(element.X)),(yInput,nameof(element.Y)),(widthInput,nameof(element.WidthPercent)),(heightInput,nameof(element.Height))})
            input.SetBinding(TextBox.TextProperty,new Binding(property){Source=element,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        var beforeResize=element.BuildDefinition();
        await WaitAsync(()=>Ready(Capability(preview)),"Undo did not rearm the current preview before resizing.",TimeSpan.FromSeconds(20));
        await ResizeAsync(core,element.Id,"se",44,28,SampleSafety);
        await WaitAsync(()=>acceptedResizes==1,"A real WebView corner resize did not commit through the native bridge.",TimeSpan.FromSeconds(15));
        Check(receipt,requestedResizes==1&&element.WidthMode==beforeResize.WidthMode&&element.WidthPercent>beforeResize.WidthPercent&&element.Height>beforeResize.Height+20,
            "Real WebView resize input updates width and height atomically while retaining responsive percentage units.");
        Check(receipt,double.TryParse(widthInput.Text,out var displayedWidth)&&Math.Abs(displayedWidth-element.WidthPercent)<.000001
            &&double.TryParse(heightInput.Text,out var displayedHeight)&&Math.Abs(displayedHeight-element.Height)<.000001,
            "The committed preview size reaches the existing native width and height fields.");
        var resizedWidth=element.WidthPercent;var resizedHeight=element.Height;
        await core.ExecuteScriptAsync("window.chrome.webview.postMessage("+resizeJson+");");await Task.Delay(150);
        Check(receipt,requestedResizes==1&&element.WidthPercent==resizedWidth&&element.Height==resizedHeight,"Replaying an actual resize capability cannot create a second size change or undo entry.");
        header.Undo();restored=header.Blocks.Single().Elements.Single();
        Check(receipt,restored.WidthPercent==beforeResize.WidthPercent&&restored.Height==beforeResize.Height&&restored.X==beforeResize.X&&restored.Y==beforeResize.Y,
            "One native Undo restores the entire rectangle from one committed resize.");
        var floorBox=header.AddElement(header.Blocks.Single(),"box");floorBox.WidthMode="pixels";floorBox.WidthPixels=160;floorBox.Height=100;floorBox.Padding=18;floorBox.BorderWidth=2;floorBox.X=50;floorBox.Y=10;
        await WaitAsync(()=>Ready(Capability(preview)),"The padded Box did not arm its current resize capability.",TimeSpan.FromSeconds(20));
        await ResizeAsync(core,floorBox.Id,"se",-200,-100,SampleSafety);
        await WaitAsync(()=>acceptedResizes==2,"The padded Box did not commit its physical minimum.",TimeSpan.FromSeconds(15));
        Check(receipt,floorBox.WidthPixels==40&&floorBox.Height==40,"A real WebView resize commits the Box's representable padding and border floor.");
        await WaitAsync(()=>Ready(Capability(preview)),"The minimum-size Box did not rearm after its committed render.",TimeSpan.FromSeconds(20));
        await ResizeAsync(core,floorBox.Id,"se",30,20,SampleSafety);
        await WaitAsync(()=>acceptedResizes==3,"The saved minimum-size padded Box rejected a second real resize.",TimeSpan.FromSeconds(15));
        Check(receipt,floorBox.WidthPixels>69&&floorBox.Height>59,"A minimum-size padded Box rearms with the correct rendered baseline and accepts the next resize.");
        var navigation=wizard.CurrentTemplate.NavigationLayout;var navRow=navigation.AddBlock();navRow.ArrangeSideBySide=false;navRow.Height=140;
        var navElement=navigation.AddElement(navRow,"navigation");navElement.WidthMode="pixels";navElement.WidthPixels=260;navElement.Height=60;navElement.X=4;navElement.Y=8;
        wizard.Step=2;
        await WaitAsync(()=>Ready(Capability(preview))&&Capability(preview)?.Region=="navigation","The Navigation stage did not issue its own current-layout capability.",TimeSpan.FromSeconds(45));
        Check(receipt,await core.ExecuteScriptAsync("(()=>{const h=[...document.querySelectorAll('[data-header-element]')].find(e=>e.dataset.headerElement==="+JsonSerializer.Serialize(restored.Id)+");const n=[...document.querySelectorAll('[data-header-element]')].find(e=>e.dataset.headerElement==="+JsonSerializer.Serialize(navElement.Id)+");return !h?.hasAttribute('data-preview-header-resizable')&&n?.hasAttribute('data-preview-header-resizable');})()") == "true",
            "The real Navigation stage arms only its own objects and leaves visible Header objects protected.");
        await ResizeAsync(core,navElement.Id,"e",30,0,SampleSafety,"navigation");
        await WaitAsync(()=>acceptedResizes==4,"A Navigation resize did not reach its independent layout model.",TimeSpan.FromSeconds(15));
        Check(receipt,navElement.WidthPixels>280&&restored.WidthPercent==beforeResize.WidthPercent&&restored.Height==beforeResize.Height,
            "A real Navigation resize preserves pixel units and cannot alter the Header rectangle.");
        var body=wizard.CurrentTemplate.BodyLayout;
        body.LoadDefinition(new(){Enabled=true,Blocks=[new(){Id=row.Id,Height=240,Arrangement="positioned",Padding=12,Elements=[
            new(){Id=restored.Id,Text="Independent Body rectangle",X=8,Y=10,WidthPercent=35,Height=52}]}]});
        var bodyElement=body.Blocks.Single().Elements.Single();wizard.Step=3;
        foreach(var (input,property) in new[]{(xInput,nameof(bodyElement.X)),(yInput,nameof(bodyElement.Y)),(widthInput,nameof(bodyElement.WidthPercent)),(heightInput,nameof(bodyElement.Height))})
            input.SetBinding(TextBox.TextProperty,new Binding(property){Source=bodyElement,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        await WaitAsync(()=>Ready(Capability(preview))&&Capability(preview)?.Region=="body","The Body stage did not issue its independent geometry capability.",TimeSpan.FromSeconds(45));
        Check(receipt,await core.ExecuteScriptAsync("(()=>{const es=[...document.querySelectorAll('[data-header-element]')].filter(e=>e.dataset.headerElement==="+JsonSerializer.Serialize(restored.Id)+");return es.length===2&&es.every(e=>e.hasAttribute('data-preview-header-resizable')===(e.closest('[data-studio-region]')?.dataset.studioRegion==='body'));})()") == "true",
            "The Body stage protects Header objects even when both layouts contain identical row and element IDs.");
        var bodyBefore=bodyElement.BuildDefinition();var bodyAccepted=accepted;var bodyRequested=requested;
        try
        {
            await DragAsync(core,bodyElement.Id,28,28,SampleSafety,"body");
            receipt.BodyPointerDiagnostics=await core.ExecuteScriptAsync("window.__nativeDragGeometry");
            await WaitAsync(()=>accepted==bodyAccepted+1,"A real Body drag did not reach its independent model.",TimeSpan.FromSeconds(15));
        }
        catch
        {
            var capability=Capability(preview);
            receipt.BrowserDiagnostics=await core.ExecuteScriptAsync("(()=>({source:location.href,nativeVersion:window.__hyperlibNativeDraftVersion,geometry:window.__nativeDragGeometry,pointers:window.__nativePointerLog,elements:[...document.querySelectorAll('[data-header-element]')].map(n=>{const b=n.getBoundingClientRect();return {html:n.outerHTML,rect:{x:b.x,y:b.y,width:b.width,height:b.height}};})}))()");
            receipt.CapabilityDiagnostics=capability is null?"null":JsonSerializer.Serialize(new{capability.Generation,capability.Region,capability.TemplateId,Ready=Ready(capability),requested,accepted});
            using var screenshot=File.Create(Path.Combine(fixture,"native-body-drag-failure.png"));await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
            throw;
        }
        Check(receipt,bodyElement.X>bodyBefore.X+10&&bodyElement.Y>bodyBefore.Y+10
            &&double.TryParse(xInput.Text,out displayedX)&&Math.Abs(displayedX-bodyElement.X)<1e-6
            &&double.TryParse(yInput.Text,out displayedY)&&Math.Abs(displayedY-bodyElement.Y)<1e-6,
            "A real Body pointer drag updates its native X/Y fields through the guarded model event.");
        Check(receipt,restored.X==beforeResize.X&&restored.Y==beforeResize.Y&&navElement.WidthPixels>280,
            "Body dragging leaves the identically named Header object and the Navigation layout unchanged.");
        body.Undo();bodyElement=body.Blocks.Single().Elements.Single();
        Check(receipt,bodyElement.X==bodyBefore.X&&bodyElement.Y==bodyBefore.Y,"One Body Undo restores both coordinates from its committed pointer drag.");
        widthInput.SetBinding(TextBox.TextProperty,new Binding(nameof(bodyElement.WidthPercent)){Source=bodyElement,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        heightInput.SetBinding(TextBox.TextProperty,new Binding(nameof(bodyElement.Height)){Source=bodyElement,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        await WaitAsync(()=>Ready(Capability(preview))&&Capability(preview)?.Region=="body","Body Undo did not rearm the current layout capability.",TimeSpan.FromSeconds(20));
        var beforeBodyResize=bodyElement.BuildDefinition();var bodyResizeAccepted=acceptedResizes;var bodyResizeRequested=requestedResizes;
        await ResizeAsync(core,bodyElement.Id,"se",35,22,SampleSafety,"body");
        await WaitAsync(()=>acceptedResizes==bodyResizeAccepted+1,"A real Body resize did not reach its independent model.",TimeSpan.FromSeconds(15));
        Check(receipt,bodyElement.WidthPercent>beforeBodyResize.WidthPercent&&bodyElement.Height>beforeBodyResize.Height+15
            &&double.TryParse(widthInput.Text,out displayedWidth)&&Math.Abs(displayedWidth-bodyElement.WidthPercent)<1e-6
            &&double.TryParse(heightInput.Text,out displayedHeight)&&Math.Abs(displayedHeight-bodyElement.Height)<1e-6,
            "A real Body corner resize updates the native width and height fields with responsive units intact.");
        await core.ExecuteScriptAsync("window.chrome.webview.postMessage("+resizeJson+");");await Task.Delay(150);
        Check(receipt,requestedResizes==bodyResizeRequested+1&&acceptedResizes==bodyResizeAccepted+1,"Replaying a real Body resize cannot create a second mutation or undo item.");
        body.Undo();bodyElement=body.Blocks.Single().Elements.Single();
        Check(receipt,bodyElement.WidthPercent==beforeBodyResize.WidthPercent&&bodyElement.Height==beforeBodyResize.Height&&bodyElement.X==beforeBodyResize.X&&bodyElement.Y==beforeBodyResize.Y,
            "One Body Undo restores the entire resized rectangle without changing Header or Navigation.");
        var footer=wizard.CurrentTemplate.FooterLayout;
        footer.LoadDefinition(new(){Enabled=true,Blocks=[new(){Id=row.Id,Height=240,Arrangement="positioned",Padding=12,Elements=[
            new(){Id=restored.Id,Text="Independent Footer rectangle",X=7,Y=9,WidthPercent=34,Height=54}]}]});
        wizard.CurrentTemplate.FooterPlacement="bottom";var footerElement=footer.Blocks.Single().Elements.Single();wizard.Step=4;
        foreach(var (input,property) in new[]{(xInput,nameof(footerElement.X)),(yInput,nameof(footerElement.Y)),(widthInput,nameof(footerElement.WidthPercent)),(heightInput,nameof(footerElement.Height))})
            input.SetBinding(TextBox.TextProperty,new Binding(property){Source=footerElement,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        await WaitAsync(()=>Ready(Capability(preview))&&Capability(preview)?.Region=="footer","The Footer stage did not issue its independent geometry capability.",TimeSpan.FromSeconds(45));
        Check(receipt,await core.ExecuteScriptAsync("(()=>{const es=[...document.querySelectorAll('[data-header-element]')].filter(e=>e.dataset.headerElement==="+JsonSerializer.Serialize(restored.Id)+");return es.length===3&&es.every(e=>e.hasAttribute('data-preview-header-resizable')===(e.closest('[data-studio-region]')?.dataset.studioRegion==='footer'));})()") == "true",
            "The Footer stage protects matching Header and Body objects while enabling only its own rectangle.");
        var footerBefore=footerElement.BuildDefinition();var footerAccepted=accepted;var footerRequested=requested;
        await DragAsync(core,footerElement.Id,26,26,SampleSafety,"footer");
        await WaitAsync(()=>accepted==footerAccepted+1,"A real Footer drag did not reach its independent native model.",TimeSpan.FromSeconds(15));
        Check(receipt,footerElement.X>footerBefore.X+10&&footerElement.Y>footerBefore.Y+10
            &&double.TryParse(xInput.Text,out displayedX)&&Math.Abs(displayedX-footerElement.X)<1e-6
            &&double.TryParse(yInput.Text,out displayedY)&&Math.Abs(displayedY-footerElement.Y)<1e-6,
            "A real Footer drag updates native X/Y fields without editing another region.");
        Check(receipt,restored.X==beforeResize.X&&bodyElement.X==beforeBodyResize.X&&navElement.WidthPixels>280,
            "Footer movement leaves Header, Body and Navigation geometry intact.");
        footer.Undo();footerElement=footer.Blocks.Single().Elements.Single();
        Check(receipt,footerElement.X==footerBefore.X&&footerElement.Y==footerBefore.Y,"One Footer Undo restores both coordinates from the committed drag.");
        widthInput.SetBinding(TextBox.TextProperty,new Binding(nameof(footerElement.WidthPercent)){Source=footerElement,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        heightInput.SetBinding(TextBox.TextProperty,new Binding(nameof(footerElement.Height)){Source=footerElement,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        await WaitAsync(()=>Ready(Capability(preview))&&Capability(preview)?.Region=="footer","Footer Undo did not rearm the current capability.",TimeSpan.FromSeconds(20));
        var beforeFooterResize=footerElement.BuildDefinition();var footerResizeAccepted=acceptedResizes;var footerResizeRequested=requestedResizes;
        await ResizeAsync(core,footerElement.Id,"se",32,20,SampleSafety,"footer");
        await WaitAsync(()=>acceptedResizes==footerResizeAccepted+1,"A real Footer resize did not reach the native model.",TimeSpan.FromSeconds(15));
        Check(receipt,footerElement.WidthPercent>beforeFooterResize.WidthPercent&&footerElement.Height>beforeFooterResize.Height+15
            &&double.TryParse(widthInput.Text,out displayedWidth)&&Math.Abs(displayedWidth-footerElement.WidthPercent)<1e-6
            &&double.TryParse(heightInput.Text,out displayedHeight)&&Math.Abs(displayedHeight-footerElement.Height)<1e-6,
            "Footer corner resizing preserves responsive units and updates native width/height fields.");
        await core.ExecuteScriptAsync("window.chrome.webview.postMessage("+resizeJson+");");await Task.Delay(150);
        Check(receipt,requestedResizes==footerResizeRequested+1&&acceptedResizes==footerResizeAccepted+1,"A replayed Footer resize cannot create another mutation or Undo entry.");
        footer.Undo();footerElement=footer.Blocks.Single().Elements.Single();
        Check(receipt,footerElement.WidthPercent==beforeFooterResize.WidthPercent&&footerElement.Height==beforeFooterResize.Height&&footerElement.X==beforeFooterResize.X&&footerElement.Y==beforeFooterResize.Y,
            "One Footer Undo restores the complete resized rectangle.");
        wizard.Step = 5;
        await WaitAsync(() => Capability(preview) is null, "Leaving the Header stage did not invalidate its drag capability.", TimeSpan.FromSeconds(10));
        await DragAsync(core, restored.Id, 50, 50, SampleSafety); await Task.Delay(150);
        await DragAsync(core,bodyElement.Id,45,45,SampleSafety,"body");await Task.Delay(150);
        await DragAsync(core,footerElement.Id,45,45,SampleSafety,"footer");await Task.Delay(150);
        Check(receipt, requested == footerRequested+1, "Visible Header, Body and Footer objects cannot be dragged after leaving their builder stages.");
        using (var screenshot = File.Create(Path.Combine(fixture, "native-drag-preview.png"))) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
        receipt.AcceptedDrops = accepted;
        var after = GetForegroundWindow(); receipt.ForegroundAfter = after.ToInt64(); GetWindowThreadProcessId(after, out var afterProcess); receipt.ForegroundAfterProcessId = afterProcess;
        SampleSafety("verification-complete");
        receipt.ExternalForegroundChanged = receipt.ForegroundBefore != receipt.ForegroundAfter;
        Check(receipt, !IsWindowVisible(host.Handle) && afterProcess != Environment.ProcessId
            && receipt.ForegroundSamples.All(sample => sample.HostHidden && sample.ProcessId != Environment.ProcessId),
            "The QA host stays hidden and its process never owns the sampled foreground; switching between external apps is allowed.");
    }

    private static async Task ResizeAsync(CoreWebView2 core,string elementId,string handle,double dx,double dy,Action<string> sampleSafety,string region="header")
    {
        async Task Input(string type,double x,double y,string button="none",int buttons=0)
        {await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y,button,buttons,clickCount=type is "mousePressed" or "mouseReleased"?1:0}));sampleSafety("resize-"+type);}
        var centerScript="(()=>{const e=[...document.querySelectorAll('[data-header-element]')].find(n=>n.dataset.headerElement==="+JsonSerializer.Serialize(elementId)+"&&n.closest('[data-studio-region]')?.dataset.studioRegion==="+JsonSerializer.Serialize(region)+");e.scrollIntoView({block:'center'});const b=e.getBoundingClientRect();return {x:b.left+b.width/2,y:b.top+Math.min(14,b.height/2)};})()";
        using var center=JsonDocument.Parse(await core.ExecuteScriptAsync(centerScript));
        await Input("mouseMoved",center.RootElement.GetProperty("x").GetDouble(),center.RootElement.GetProperty("y").GetDouble());
        using var geometry=JsonDocument.Parse(await core.ExecuteScriptAsync("(()=>{const h=document.querySelector('#studio-header-preview-resize [data-header-resize-handle="+handle+"]');if(!h||h.parentElement.hidden)return null;const b=h.getBoundingClientRect();return {x:b.left+b.width/2,y:b.top+b.height/2};})()"));
        if(geometry.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("The real WebView resize handles did not become available.");
        var x=geometry.RootElement.GetProperty("x").GetDouble();var y=geometry.RootElement.GetProperty("y").GetDouble();
        await Input("mouseMoved",x,y);await Input("mousePressed",x,y,"left",1);
        await Input("mouseMoved",x+dx/2,y+dy/2,"none",1);await Input("mouseMoved",x+dx,y+dy,"none",1);await Input("mouseReleased",x+dx,y+dy,"left");
    }
    private static async Task DragAsync(CoreWebView2 core, string elementId, double x, double y, Action<string> sampleSafety,string region="header")
    {
        var script = "(()=>{const e=[...document.querySelectorAll('[data-header-element]')].find(n=>n.dataset.headerElement===" + JsonSerializer.Serialize(elementId)
            + "&&n.closest('[data-studio-region]')?.dataset.studioRegion==="+JsonSerializer.Serialize(region)+");if(!e)return null;const initial=e.getBoundingClientRect();const before={x:initial.x,y:initial.y,width:initial.width,height:initial.height,viewportHeight:innerHeight,viewportWidth:innerWidth};e.scrollIntoView({block:'center',inline:'nearest'});const b=e.getBoundingClientRect(),p=e.parentElement.getBoundingClientRect();window.__nativeDragGeometry={before,after:{x:b.x,y:b.y,width:b.width,height:b.height},region:"+JsonSerializer.Serialize(region)+"};return {startX:b.left+Math.min(b.width/2,50),startY:b.top+Math.min(b.height/2,18),"
            + "endX:p.left+p.width*" + (x / 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "+Math.min(b.width/2,50),endY:p.top+p.height*"
            + (y / 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "+Math.min(b.height/2,18)};})()";
        using var geometry = JsonDocument.Parse(await core.ExecuteScriptAsync(script)); var value = geometry.RootElement;
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("The real preview header element is missing.");
        var startX = value.GetProperty("startX").GetDouble(); var startY = value.GetProperty("startY").GetDouble();
        var endX = value.GetProperty("endX").GetDouble(); var endY = value.GetProperty("endY").GetDouble();
        async Task Input(string type, double pointerX, double pointerY, string button = "none", int buttons = 0)
        { await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x = pointerX, y = pointerY, button, buttons, clickCount = type is "mousePressed" or "mouseReleased" ? 1 : 0 })); sampleSafety(type); }
        await Input("mouseMoved", startX, startY); await Input("mousePressed", startX, startY, "left", 1);
        await Input("mouseMoved", (startX + endX) / 2, (startY + endY) / 2, "none", 1); await Input("mouseMoved", endX, endY, "none", 1);
        await Input("mouseReleased", endX, endY, "left");
    }
    private static CoreWebView2? Core(HyperlibGuidedPagePreview preview) => (typeof(HyperlibGuidedPagePreview).GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview) as WebView2)?.CoreWebView2;
    private static HyperlibHeaderDragCapability? Capability(HyperlibGuidedPagePreview preview) => typeof(HyperlibGuidedPagePreview).GetField("_headerDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview) as HyperlibHeaderDragCapability;
    private static bool Ready(HyperlibHeaderDragCapability? capability) => capability is not null && (bool)typeof(HyperlibHeaderDragCapability).GetField("_ready", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capability)!;
    private static void Check(Receipt receipt, bool condition, string message) { if (!condition) throw new InvalidOperationException(message); receipt.Assertions.Add(message); }
    private static Task WaitAsync(Func<bool> predicate, string failure, TimeSpan timeout) => WaitAsync(() => Task.FromResult(predicate()), failure, timeout);
    private static async Task WaitAsync(Func<Task<bool>> predicate, string failure, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout; while (DateTime.UtcNow < end) { if (await predicate()) return; await Task.Delay(50); }
        throw new TimeoutException(failure);
    }
    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () => { try { await action(); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } }); Dispatcher.Run(); }) { IsBackground = true, Name = "Isolated header drag WebView acceptance" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    private sealed class Receipt
    {
        public bool Passed { get; set; } public string Fixture { get; set; } = ""; public string Repository { get; set; } = ""; public string BrowserProfile { get; set; } = "";
        public long ForegroundBefore { get; set; } public long ForegroundAfter { get; set; } public uint ForegroundBeforeProcessId { get; set; } public uint ForegroundAfterProcessId { get; set; }
        public bool ExternalForegroundChanged { get; set; } public int AcceptedDrops { get; set; } public List<ForegroundSample> ForegroundSamples { get; } = [];
        public List<string> Assertions { get; } = []; public string? Failure { get; set; }
        public List<string> BrowserMessages { get; } = []; public string? BrowserDiagnostics { get; set; } public string? CapabilityDiagnostics { get; set; } public string? BodyPointerDiagnostics { get; set; }
    }
    private sealed record ForegroundSample(string Stage, long Window, uint ProcessId, bool HostHidden);
}
