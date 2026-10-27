using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HyperLib.Core;
using Hypershell;
using Hypershell.Controls;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;

public static partial class StudioWorkspaceChromeNativeVerifier
{
    /// <summary>Real legacy complete-page import and child editing, through the actual native workspace.</summary>
    private static async Task VerifyNestedCanvasAsync(VisualPageStudioView view, VisualPageStudioTabViewModel model,
        StudioHyperlibAuthoringView writer, CoreWebView2 core, Receipt receipt)
    {
        Console.WriteLine("STUDIO_WORKSPACE_STEP|" + DateTime.UtcNow.ToString("O") + "|nested-template-canvas");
        Layout(view, 1500, 1000);
        var library = Path.GetFullPath(Path.Combine(receipt.Project, "../.."));
        var store = new ContentStore(library);
        var before = await store.ReadAsync();
        var legacyRevision = model.Revision;
        var oldPage = writer.CurrentNodeId ?? throw new InvalidOperationException("The nested-element fixture has no current Page.");
        var beforeIds = before.Pages[oldPage].Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        await ChoosePanelAsync(core, "right", "templates");
        await core.ExecuteScriptAsync("(()=>{const scope=document.querySelector('#template-scope');scope.value='page';scope.dispatchEvent(new Event('change',{bubbles:true}));const search=document.querySelector('#library-search');search.value='Programming guide';search.dispatchEvent(new Event('input',{bubbles:true}));})()");
        const string templateId = "page:visual:atlas.archive.page.coding.v1";
        await WaitAsync(async () => await TrueAsync(core, "!!document.querySelector('[data-library-id=\"" + templateId + "\"]')"),
            "The actual complete Programming guide template is absent from the native-hosted Templates library.", 45);
        await core.ExecuteScriptAsync("document.querySelector('[data-library-id=\"" + templateId + "\"]').click()");
        await WaitAsync(async () => await TrueAsync(core, "!!document.querySelector('#library-add')&&!document.querySelector('#library-add').disabled"),
            "Selecting the real complete Page did not expose Add to Page.", 60);
        await core.ExecuteScriptAsync("document.querySelector('#library-add').click()");
        // Existing Landing content is retained; a complete Page is placed through its real production command.
        // The current native fixture is on that reserved Landing, so there is no add-Page modal.
        await WaitAsync(async () => await TrueAsync(core, "(()=>{const s=window.__studioAuthoring?.state(),f=document.querySelector('#page-frame');return s?.currentGenerationReady&&!s.libraryBusy&&!s.libraryMutationPending&&!f.inert&&!!f.contentDocument?.querySelector('figure img[data-vp-id]');})()"),
            "Adding a real complete Programming guide did not produce its independently targetable nested image.", 240);
        var pageId = writer.CurrentNodeId ?? oldPage;
        var imported = await store.ReadAsync();
        var owner = imported.Pages[pageId].Blocks.Single(block => !beforeIds.Contains(block.Id)
            && block.Type.Contains("atlas-archive-page-coding", StringComparison.Ordinal));
        var otherBlocks = imported.Pages[pageId].Blocks.Where(block => block.Id != owner.Id)
            .ToDictionary(block => block.Id, block => JsonSerializer.Serialize(block, ContentJson.Options), StringComparer.Ordinal);
        // Imports namespace the original component to prevent collisions with another
        // template package. Inspect the owning schema instead of assuming its old filename.
        var component = TemplateObjectLibrary.ReadDefinition(receipt.Project).Blocks.Single(block => block.Id == owner.Type).Component.Split('.').Last();
        var sourceHashes = Directory.EnumerateFiles(receipt.Project, component + ".*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Append(Path.Combine(receipt.Project, "authoring.template.json")).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, HashNestedSource, StringComparer.OrdinalIgnoreCase);
        Check(receipt, sourceHashes.Count >= 2 && pageId == oldPage && imported.Pages[pageId].Blocks.Count > beforeIds.Count,
            "A shipped complete Programming guide imports through Add to Page into the existing Landing while retaining its existing sibling objects and original owned source.");
        var ownerId = JsonSerializer.Serialize(owner.Id);
        string OnOwner(string body) => "(()=>{const f=document.querySelector('#page-frame'),d=f.contentDocument,b=[...d.querySelectorAll('[data-block-id]')].find(e=>e.dataset.blockId==="
            + ownerId + ");if(!b)throw new Error('The imported Page owner is absent');const qaFigure=()=>{const id=window.__studioNestedQa?.figureId;if(typeof id!=='string'||!id)throw new Error('The selected image figure has no captured identity');const e=f.contentWindow.hyperlibStudioElements.resolve(b,'@'+id);if(!e)throw new Error('The selected image figure is absent');return e;};" + body + "})()";
        await core.ExecuteScriptAsync(OnOwner("const image=b.querySelector('figure img[data-vp-id]'),figure=image?.closest('figure'),caption=figure?.querySelector('figcaption'),heading=b.querySelector('h1');if(!image||!figure?.dataset.vpId||!caption||!heading)throw new Error('The shipped Page lacks its original image/figure/caption/heading');const headingPath=f.contentWindow.hyperlibStudioElements.path(b,heading);if(headingPath===null)throw new Error('The original sibling heading has no supported selection path');window.__studioNestedQa={ownerId:b.dataset.blockId,imageId:image.dataset.vpId,figureId:figure.dataset.vpId,headingPath,heading:heading.textContent,caption:caption.textContent,imageAlt:image.alt,frame:f,document:d,src:f.src,controller:f.__studioCanvas,loads:0};f.addEventListener('load',()=>window.__studioNestedQa.loads++);"));

        async Task ClickNode(string reference, string tool)
        {
            await ChoosePanelAsync(core, "left", tool);
            await core.ExecuteScriptAsync(OnOwner("const q=window.__studioNestedQa,value=q[" + JsonSerializer.Serialize(reference)
                + "];if(typeof value!=='string')throw new Error('The intended descendant has no captured identity');const path="
                + (reference.EndsWith("Path", StringComparison.Ordinal) ? "value" : "'@'+value")
                + ",e=f.contentWindow.hyperlibStudioElements.resolve(b,path);if(!e)throw new Error('The intended descendant is absent');e.scrollIntoView({block:'center'});e.dispatchEvent(new f.contentWindow.MouseEvent('click',{bubbles:true,cancelable:true,view:f.contentWindow}));"));
            await WaitAsync(async () => await TrueAsync(core, "(()=>{const q=window.__studioNestedQa,s=window.__studioAuthoring.state(),p=document.querySelector('#properties-content');return s.workspaceLeftPanel?.selectedId==="
                + JsonSerializer.Serialize(tool) + "&&s.workspaceLeftPanel.isOpen&&s.selectedElement?.blockId===q.ownerId&&!!s.selectedElement.path&&p.dataset.elementBlock===q.ownerId&&p.dataset.elementPath===s.selectedElement.path;})()"),
                "Selecting an actual nested " + reference + " did not retain " + tool + " and target that descendant rather than the complete Page wrapper.", 30);
            await Task.Delay(140);
        }
        async Task SavedEdit(string expected)
        {
            await WaitAsync(async () =>
            {
                var saved = await store.ReadAsync();
                return saved.Pages[pageId].Blocks.Single(block => block.Id == owner.Id).Values.TryGetValue("studioElements", out var edits)
                    && edits.ValueKind == JsonValueKind.String && edits.GetString()!.Contains(expected, StringComparison.Ordinal)
                    && await TrueAsync(core, "window.__studioAuthoring.state().currentGenerationReady&&!window.__studioAuthoring.state().dirty");
            }, "The selected descendant change did not persist through the ordinary Page save route: " + expected, 90);
        }
        await ClickNode("imageId", "properties");
        Check(receipt, await TrueAsync(core, "(()=>{const q=window.__studioNestedQa,s=window.__studioAuthoring.state(),f=document.querySelector('#page-frame');return s.selectedElement.path.includes(q.imageId)&&!!document.querySelector('[data-element-attribute=\"alt\"]')&&f===q.frame&&f.contentDocument===q.document&&f.__studioCanvas===q.controller&&f.src===q.src&&q.loads===0&&!s.dirty;})()"),
            "Clicking an image inside an imported complete Page selects its stable descendant and real image controls, without navigating, recreating the canvas or dirtying content.");
        const string alt = "A selected image inside the complete Page";
        await core.ExecuteScriptAsync("(()=>{const e=document.querySelector('[data-element-attribute=\"alt\"]');e.value=" + JsonSerializer.Serialize(alt)
            + ";e.dispatchEvent(new Event('change',{bubbles:true}));})()");
        await SavedEdit(alt);
        Check(receipt, await TrueAsync(core, OnOwner("const q=window.__studioNestedQa,e=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]');return e?.alt===" + JsonSerializer.Serialize(alt) + ";")),
            "Properties edits the actual image attribute inside its figure, and the saved nested override updates the live Page.");

        await ClickNode("imageId", "appearance");
        await core.ExecuteScriptAsync("(()=>{document.querySelector('#element-style-name').value='border-radius';document.querySelector('#element-style-value').value='17px';document.querySelector('#element-style-apply').click();})()");
        await SavedEdit("17px");
        Check(receipt, await TrueAsync(core, OnOwner("const q=window.__studioNestedQa,e=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]');return f.contentWindow.getComputedStyle(e).borderRadius==='17px'&&window.__studioAuthoring.state().workspaceLeftPanel.selectedId==='appearance';")),
            "Appearance remains selected and changes only the nested image's actual rendered border radius.");

        // The breadcrumb must permit choosing the containing box even where a child fills its hit area.
        await ChoosePanelAsync(core, "left", "code");
        await WaitAsync(async () => await TrueAsync(core, "!!document.querySelector('#element-code')&&!!document.querySelector('#element-code-apply')"),
            "The primary left Code tool did not show its actual editable source interface.", 30);
        Check(receipt, await TrueAsync(core, "(()=>{const t=document.querySelector('#element-code').value;return /^<img\\b/i.test(t.trim())&&!t.includes('<h1');})()"),
            "Code initially displays the selected image alone rather than the entire imported Page template.");
        await core.ExecuteScriptAsync("(()=>{const q=window.__studioNestedQa,e=[...document.querySelectorAll('#properties-content button[data-element-path]')].find(e=>e.dataset.elementPath.includes(q.figureId));if(!e)throw new Error('The selected image has no containing-figure breadcrumb');e.click();})()");
        await WaitAsync(async () => await TrueAsync(core, "(()=>{const q=window.__studioNestedQa,t=document.querySelector('#element-code');return window.__studioAuthoring.state().selectedElement?.path.includes(q.figureId)&&!!t&&/^<figure\\b/i.test(t.value.trim())&&t.value.includes('<img')&&t.value.includes('<figcaption');})()"),
            "The image's parent breadcrumb did not select the actual containing figure and expose its full nested source.", 30);
        const string revisedCaption = "The containing box and its embedded image are independently editable.";
        await core.ExecuteScriptAsync("(()=>{const t=document.querySelector('#element-code'),parsed=new DOMParser().parseFromString(t.value,'text/html'),caption=parsed.querySelector('figcaption');if(!caption)throw new Error('The containing box source has no caption');caption.textContent="
            + JsonSerializer.Serialize(revisedCaption) + ";t.value=parsed.body.firstElementChild.outerHTML;t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#element-code-apply').click();})()");
        await SavedEdit(revisedCaption);
        var parentComposite = OnOwner("const q=window.__studioNestedQa,image=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]'),heading=f.contentWindow.hyperlibStudioElements.resolve(b,q.headingPath),figure=qaFigure();return figure?.querySelector('figcaption')?.textContent==="
            + JsonSerializer.Serialize(revisedCaption) + "&&image.alt===" + JsonSerializer.Serialize(alt)
            + "&&f.contentWindow.getComputedStyle(image).borderRadius==='17px'&&heading.textContent===q.heading;");
        try
        {
            await WaitAsync(async () => await TrueAsync(core, parentComposite),
                "The containing box's Code did not retain its child image's properties and appearance and sibling heading after the saved renderer settled.", 45);
        }
        catch
        {
            receipt.Samples.Add(new { stage = "nested-parent-code", diagnostics = await core.ExecuteScriptAsync(OnOwner("const q=window.__studioNestedQa,image=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]'),heading=f.contentWindow.hyperlibStudioElements.resolve(b,q.headingPath),figure=qaFigure();return {caption:figure?.querySelector('figcaption')?.textContent,alt:image?.alt,radius:image&&f.contentWindow.getComputedStyle(image).borderRadius,heading:heading?.textContent,expectedHeading:q.heading,state:window.__studioAuthoring.state()};")) });
            throw;
        }
        Check(receipt, true,
            "Editing a containing box's Code preserves its embedded image's independent properties and appearance while leaving the sibling heading untouched.");

        await core.ExecuteScriptAsync("document.querySelector('#undo-button').click()");
        await WaitAsync(async () => await TrueAsync(core, OnOwner("return qaFigure().querySelector('figcaption').textContent===window.__studioNestedQa.caption&&window.__studioAuthoring.state().currentGenerationReady&&!window.__studioAuthoring.state().dirty;")),
            "Undo did not restore the containing box's original code as one ordinary saved edit.", 90);
        await core.ExecuteScriptAsync("document.querySelector('#redo-button').click()");
        await SavedEdit(revisedCaption);
        await WaitAsync(async () => await TrueAsync(core, OnOwner("return qaFigure().querySelector('figcaption').textContent===" + JsonSerializer.Serialize(revisedCaption) + ";")),
            "Redo did not finish restoring the nested code in the actual Page renderer.", 45);
        Check(receipt, true,
            "Undo and Redo treat the selected box's complete code change as one action and restore the edited nested content.");

        const string inlineCaption = "Inline writing remains editable after a containing box's Code has changed.";
        await core.ExecuteScriptAsync(OnOwner("const c=qaFigure().querySelector('figcaption');if(!c?.isContentEditable||!c.dataset.field)throw new Error('The edited box lost its original inline caption field');c.focus();c.textContent="
            + JsonSerializer.Serialize(inlineCaption) + ";c.dispatchEvent(new f.contentWindow.InputEvent('input',{bubbles:true,inputType:'insertText',data:" + JsonSerializer.Serialize(inlineCaption) + "}));"));
        await SavedEdit(inlineCaption);
        Check(receipt, await TrueAsync(core, OnOwner("return qaFigure().querySelector('figcaption').textContent===" + JsonSerializer.Serialize(inlineCaption) + ";")),
            "Writing directly into a caption inside edited parent Code updates the saved override instead of reverting or losing its reusable field binding.");

        await ClickNode("imageId", "actions");
        const string handler = "this.dataset.studioQaClick = 'yes';";
        await core.ExecuteScriptAsync("(()=>{document.querySelector('#element-event').value='click';const t=document.querySelector('#element-handler');t.value="
            + JsonSerializer.Serialize(handler) + ";t.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#element-event-apply').click();})()");
        await SavedEdit("studioQaClick");
        await ClickNode("imageId", "actions");
        Check(receipt, await TrueAsync(core, OnOwner("const q=window.__studioNestedQa,e=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]');return !e.dataset.studioQaClick&&document.querySelector('#element-handler').value.includes('studioQaClick');")),
            "Actions edits the selected image's event handler through the actual Page save route, while editing clicks remain selection-only and do not execute reader behavior.");

        var persisted = await store.ReadAsync();
        Check(receipt, persisted.Pages[pageId].Blocks.Where(block => block.Id != owner.Id).All(block => otherBlocks.TryGetValue(block.Id, out var json)
                && json == JsonSerializer.Serialize(block, ContentJson.Options))
            && sourceHashes.All(pair => HashNestedSource(pair.Key) == pair.Value) && model.Revision == legacyRevision,
            "Nested Properties, Appearance, Code and Actions edits change only the current Page's owning block overrides; sibling objects, original template schema/components and the unrelated legacy editor remain unchanged.");
        var metadata = JsonDocument.Parse(await core.ExecuteScriptAsync("JSON.stringify({ownerId:window.__studioNestedQa.ownerId,imageId:window.__studioNestedQa.imageId,figureId:window.__studioNestedQa.figureId,caption:window.__studioNestedQa.caption,heading:window.__studioNestedQa.heading,headingPath:window.__studioNestedQa.headingPath})")).RootElement.GetString()!;
        var reloaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) => reloaded.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += NavigationCompleted;
        try { core.Reload(); Check(receipt, await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(45)), "The real native authoring runtime reopens the nested Page after saved code and event edits."); }
        finally { core.NavigationCompleted -= NavigationCompleted; }
        await WaitAsync(async () => writer.IsWorkspaceChromeReady && await TrueAsync(core, "window.__studioAuthoring?.state()?.currentGenerationReady===true"),
            "The saved nested Page did not reopen in the actual native workspace.", 120);
        await core.ExecuteScriptAsync("window.__studioNestedQa=" + metadata);
        await ClickNode("imageId", "code");
        Check(receipt, await TrueAsync(core, OnOwner("const q=window.__studioNestedQa,image=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]');return image.alt===" + JsonSerializer.Serialize(alt)
            + "&&f.contentWindow.getComputedStyle(image).borderRadius==='17px'&&qaFigure().querySelector('figcaption').textContent===" + JsonSerializer.Serialize(inlineCaption)
            + "&&document.querySelector('#element-code').value.includes(" + JsonSerializer.Serialize(alt) + ");")),
            "Reopening preserves nested image attributes, styling, parent Code edits and child selection in the new Code tool.");

        await WaitAsync(async () => await TrueAsync(core, OnOwner("const q=window.__studioNestedQa,image=b.querySelector('[data-vp-id=\"'+q.imageId+'\"]'),overlay=d.querySelector('[data-studio-canvas-overlay]');if(!image||!overlay||overlay.hidden)return false;const a=image.getBoundingClientRect(),o=overlay.getBoundingClientRect();return a.width>0&&a.height>0&&Math.abs(a.left-o.left)<=2&&Math.abs(a.top-o.top)<=2&&Math.abs(a.width-o.width)<=2&&Math.abs(a.height-o.height)<=2&&!!overlay.querySelector('[data-studio-handle=\"se\"]');")),
            "Canvas resize/move handles did not wrap the selected descendant instead of the complete Page wrapper.", 30);
        Check(receipt, true, "The live canvas move and resize overlay matches the selected nested image's exact bounds, with its original edge and corner handles available.");

        var beforePendingCode = (await store.ReadAsync()).Revision;
        await core.ExecuteScriptAsync("(()=>{const q=window.__studioNestedQa,t=document.querySelector('#element-code');q.savedCode=t.value;q.pendingCode=t.value.replace('<img ','<img data-code-draft=\"retained\" ');if(q.pendingCode===q.savedCode)throw new Error('The selected source is not an image');t.value=q.pendingCode;t.dispatchEvent(new Event('input',{bubbles:true}));})()");
        await ClickNode("headingPath", "code");
        await ClickNode("imageId", "code");
        Check(receipt, await TrueAsync(core, "document.querySelector('#element-code').value===window.__studioNestedQa.pendingCode&&window.__studioAuthoring.state().dirty"),
            "Unapplied Code follows its selected element while selecting another sibling and returning, instead of silently losing the typed source.");
        await core.ExecuteScriptAsync("window.__studioNestedQa.pendingFlushSaved=null;window.__studioAuthoring.flush('nested-code-pending-qa').then(saved=>window.__studioNestedQa.pendingFlushSaved=saved)");
        await WaitAsync(async () => await TrueAsync(core, "window.__studioNestedQa.pendingFlushSaved===false"),
            "Saving a Page with unapplied Code did not report its pending source before leaving.", 30);
        Check(receipt, (await store.ReadAsync()).Revision == beforePendingCode && await TrueAsync(core, "window.__studioAuthoring.state().dirty&&document.querySelector('#element-code').value===window.__studioNestedQa.pendingCode"),
            "The normal save/close flush blocks unapplied Code, retaining the typed editor draft and leaving saved content unchanged.");
        await core.ExecuteScriptAsync("document.querySelector('#element-code-reset').click()");
        await WaitAsync(async () => await TrueAsync(core, "!window.__studioAuthoring.state().dirty&&document.querySelector('#element-code').value===window.__studioNestedQa.savedCode"),
            "Reset editor did not discard only the unapplied Code and restore the clean saved source.", 30);
        Check(receipt, (await store.ReadAsync()).Revision == beforePendingCode,
            "Reset editor restores the saved selected-element source and clears the empty draft without writing another content revision.");
        foreach (var width in new[] { 1500, 640 })
        {
            Layout(view, width, 900);
            await ChoosePanelAsync(core, "left", "code");
            await WaitAsync(async () => await TrueAsync(core, "(()=>{const t=document.querySelector('#element-code');return !!t&&!t.disabled&&document.documentElement.scrollWidth<=innerWidth+2;})()"),
                "The editable Code tool clipped or disappeared at " + width + "px.", 30);
            var image = Path.Combine(receipt.Fixture, "studio-nested-code-" + width + ".png");
            await using (var output = File.Create(image)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output).WaitAsync(TimeSpan.FromSeconds(15));
            receipt.Renders.Add(image);
        }
        Layout(view, 1500, 1000);
        Check(receipt, !receipt.Messages.Any(message => message.Contains("\"type\":\"studio:error\"", StringComparison.Ordinal)),
            "Nested complete-Page selection, editable Code, independent styling, Actions, history and reload produce no uncaught authoring errors.");
    }

    private static string HashNestedSource(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
