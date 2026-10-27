using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using HyperLib.Core;
using Hypershell.Controls;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

/// <summary>Drives the real Studio sidebar and its native catalogue/import/preview/save bridge on one owned disposable Work.</summary>
public static class StudioLibrarySidebarNativeVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string libraryRoot)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "ls-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(fixture);
        var receipt = new Receipt { Fixture = fixture };
        try
        {
            await RunStaAsync(async () =>
            {
                LoadResources(repositoryRoot);
                await RunAsync(fixture, repositoryRoot, libraryRoot, receipt);
            });
            receipt.Passed = true;
        }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "studio-library-sidebar-native-verification.json"),
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STUDIO_LIBRARY_SIDEBAR_NATIVE_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }

    private static async Task RunAsync(string fixture, string repositoryRoot, string libraryRoot, Receipt receipt)
    {
        var project = await new HyperlibWorkProjectService(libraryRoot).CreateAsync(fixture, "Library sidebar acceptance",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Library sidebar acceptance"));
        receipt.ProjectRoot = project;
        var pipeline = await new HyperlibProjectPipelineService().ReadAsync(project)
            ?? throw new InvalidOperationException("The owned Work has no reusable Page state.");
        var ownedLibrary = Path.GetFullPath(Path.Combine(project, "../.."));
        using var hosts = new HyperlibWorkPreviewHostService();
        using var library = new StudioAuthoringLibraryService(libraryRoot, Path.Combine(fixture, "c"));
        using var model = new HyperlibStudioWorkspaceViewModel(project, (root, ct) => hosts.EnsureAsync(root, ct));
        await model.LoadAsync();
        using var view = new StudioHyperlibAuthoringView(model, Path.Combine(fixture, "b"), library);
        view.StatusChanged += message => receipt.StatusMessages.Add(message);
        using var host = new HwndSource(new HwndSourceParameters("Hidden Studio template library sidebar acceptance")
        {
            Width = 1500, Height = 1000, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
        });
        host.RootVisual = view;
        Layout(view, 1500, 1000);
        void SampleSafety(string stage)
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var processId);
            var hidden = !IsWindowVisible(host.Handle);
            receipt.ForegroundSamples.Add(new(stage, foreground.ToInt64(), processId, hidden));
            if (!hidden || processId == Environment.ProcessId)
                throw new InvalidOperationException("The isolated native sidebar fixture became visible or acquired foreground during " + stage + ".");
        }
        CoreWebView2? core = null;
        try
        {
            SampleSafety("attached");
            await WaitAsync(() => Core(view) is not null, "The actual native sidebar WebView did not initialize.", TimeSpan.FromMinutes(4));
            core = Core(view)!;
            core.WebMessageReceived += (_, args) => receipt.BrowserMessages.Add(args.WebMessageAsJson);
            await WaitAsync(async () => view.CurrentNodeId == pipeline.LandingPageId
                && await IsTrueAsync(core, "window.__studioAuthoring?.state()?.ready === true && !!document.querySelector('#page-frame')?.contentDocument?.querySelector('.hl-document')"),
                "The real authoring canvas did not open this Work's landing Page.", TimeSpan.FromMinutes(4));
            Check(receipt, core.Environment.UserDataFolder.StartsWith(fixture, StringComparison.OrdinalIgnoreCase),
                "The sidebar uses the real native control with a disposable browser profile and owned gateway.");
            var script = Path.Combine(repositoryRoot, "tools", "verify-studio-library-sidebar.cjs");
            var options = JsonSerializer.Serialize(new
            {
                workId = pipeline.WorkId, landingPageId = pipeline.LandingPageId,
                savedTemplateTitle = "Native saved Page " + Guid.NewGuid().ToString("N")[..8],
                text = "Independent content authored through the left Properties panel."
            });
            var previewBaseline = AuthoringFingerprint(project, ownedLibrary);
            await core.ExecuteScriptAsync("window.__studioLibrarySidebarQaOptions=" + options + ";\n" + await File.ReadAllTextAsync(script));
            string? lastProgress = null;
            var inspectedPreview = false;
            var previewDocumentVerified = false;
            await WaitAsync(async () => {
                using var snapshot = JsonDocument.Parse(await core.ExecuteScriptAsync("({done:window.__studioLibrarySidebarQa?.done === true,previewCheckpoint:window.__studioLibrarySidebarQa?.previewCheckpoint === true,progress:window.__studioLibrarySidebarQa?.progress || 'Starting'})"));
                var progress = snapshot.RootElement.GetProperty("progress").GetString();
                if (progress != lastProgress) { lastProgress = progress; Console.WriteLine("STUDIO_SIDEBAR_PROGRESS|" + progress); }
                if (!inspectedPreview && snapshot.RootElement.GetProperty("previewCheckpoint").GetBoolean())
                {
                    var afterPreview = AuthoringFingerprint(project, ownedLibrary);
                    var differences = previewBaseline.Keys.Union(afterPreview.Keys, StringComparer.OrdinalIgnoreCase)
                        .Where(file => !previewBaseline.TryGetValue(file, out var before) || !afterPreview.TryGetValue(file, out var after) || before != after).ToArray();
                    Check(receipt, differences.Length == 0,
                        "Selecting and switching actual library previews leaves every owned source file, Page projection, media file and content journal byte unchanged. " + string.Join(", ", differences.Take(8)));
                    var address = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.querySelector('#studio-library-preview-frame').src"))!;
                    var activePreview = new Uri(address);
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                    {
                        var html = await client.GetStringAsync(activePreview);
                        Check(receipt, activePreview.IsLoopback && html.Contains("hl-document", StringComparison.Ordinal),
                            "The selected native preview iframe serves an actual owned rendered Page while its preview session is active.");
                    }
                    previewDocumentVerified = true;
                    inspectedPreview = true;
                    await core.ExecuteScriptAsync("window.__studioLibrarySidebarQaContinuePreview=true");
                }
                return snapshot.RootElement.GetProperty("done").GetBoolean();
            },
                "The sidebar browser acceptance script did not finish. Check its progress and native bridge messages.", TimeSpan.FromMinutes(8));
            var resultText = await core.ExecuteScriptAsync("JSON.stringify(window.__studioLibrarySidebarQa)");
            var resultJson = JsonSerializer.Deserialize<string>(resultText) ?? throw new InvalidOperationException("The sidebar script returned no report.");
            receipt.BrowserResult = JsonSerializer.Deserialize<JsonElement>(resultJson);
            var result = receipt.BrowserResult.Value;
            if (!result.GetProperty("passed").GetBoolean())
                throw new InvalidOperationException("The actual native library sidebar failed: " + result.GetProperty("failure").GetString());
            foreach (var assertion in result.GetProperty("checks").EnumerateArray()) receipt.Assertions.Add(assertion.GetString()!);
            SampleSafety("library-actions-complete");
            Check(receipt, receipt.BrowserMessages.Any(message => message.Contains("studio:library-request", StringComparison.Ordinal))
                && receipt.BrowserMessages.Any(message => message.Contains("save-page-template", StringComparison.Ordinal)),
                "Catalogue, automatic preview, import and Page saving were requested through the actual browser-to-native library bridge.");
            Check(receipt, inspectedPreview, "The nonmutating preview checkpoint was inspected before any template placement.");
            Check(receipt, await view.SaveAsync() && !view.HasUnsavedContent,
                "The native writer confirms the completed sidebar edits before their disk state is inspected.");
            var state = await new ContentStore(ownedLibrary).ReadAsync();
            ContentRules.Validate(state);
            var data = result.GetProperty("data");
            var partId = data.GetProperty("partId").GetString()!;
            var chapterId = data.GetProperty("chapterId").GetString()!;
            var pageId = data.GetProperty("pageId").GetString()!;
            var part = state.Nodes.Single(node => node.Id == partId);
            var chapter = state.Nodes.Single(node => node.Id == chapterId);
            var page = state.Nodes.Single(node => node.Id == pageId);
            Check(receipt, part.Type == "part" && part.ParentId == pipeline.WorkId
                && chapter.Type == "chapter" && chapter.ParentId == part.Id
                && page.Type == "page" && page.ParentId == chapter.Id
                && new[] { part, chapter, page }.All(node => node.WorkId == pipeline.WorkId),
                "The real content journal preserves Part → Chapter → Page nesting and the original Work identity.");
            Check(receipt, state.Pages[pageId].Blocks.Any(block => block.Values.Values.Any(value => value.ValueKind == JsonValueKind.String
                && value.GetString() == data.GetProperty("editedText").GetString())),
                "Content edited in the relocated left Properties inspector is persisted in its actual Page document.");
            var template = JsonSerializer.Deserialize<TemplateDefinition>(await File.ReadAllTextAsync(Path.Combine(project, "authoring.template.json")), ContentJson.Options)!;
            foreach (var document in state.Pages.Values) TemplateValidator.ValidatePage(template, document);
            Check(receipt, true, "Every Page, including imported library objects, validates against the owned declared component and field schemas.");
            var savedFiles = Directory.EnumerateFiles(Path.Combine(ownedLibrary, "Templates"), "*", SearchOption.AllDirectories).ToArray();
            Check(receipt, savedFiles.Any(file => Path.GetFileName(file) == "authoring.template.json")
                && savedFiles.Any(file => Path.GetExtension(file) is ".razor" or ".cs"),
                "Saving the authored Page creates a real reusable project-owned package with source instead of a screenshot or external reference.");
            Check(receipt, data.GetProperty("catalogCount").GetInt32() > 500
                && data.GetProperty("previewLoaded").GetBoolean(),
                "The complete Core catalogue and a rendered native template preview are available in the live sidebar.");
            var previewUrl = new Uri(data.GetProperty("previewUrl").GetString()!);
            Check(receipt, previewUrl.Scheme == Uri.UriSchemeHttp && previewUrl.IsLoopback,
                "The native preview remains on its owned private loopback renderer.");
            Check(receipt, previewDocumentVerified,
                "The native preview document was verified during selection before later selection changes could dispose its temporary host.");

            foreach (var width in new[] { 1500, 960, 640, 390 })
            {
                Layout(view, width, 900);
                await WaitAsync(async () => await IsTrueAsync(core, "Math.abs(innerWidth-" + width + ")<=2"),
                    "The native sidebar viewport did not follow its resized host.", TimeSpan.FromSeconds(10));
                await core.ExecuteScriptAsync("document.querySelector('#page-frame')?.contentDocument?.querySelector('[data-field=\"text\"]')?.click();");
                await WaitAsync(async () => await IsTrueAsync(core, "window.__studioAuthoring.state().leftPanel === 'inspector' && !document.querySelector('#navigation').hidden && !document.querySelector('#properties-content').hidden && !!document.querySelector('#properties-content [data-field],#properties-content [data-element-attribute]')"),
                    "Selecting the saved Page clone did not expose its relocated Properties inspector.", TimeSpan.FromSeconds(10));
                Check(receipt, await IsTrueAsync(core, "(()=>{const l=document.querySelector('#navigation').getBoundingClientRect();return l.width>180&&l.left>=-1&&l.right<=innerWidth+1;})()"),
                    "At " + width + "px selecting the Page clone opens a bounded left Properties inspector through the real selection bridge.");
                await core.ExecuteScriptAsync("(()=>{const layout=document.querySelector('#app-layout'),toggle=document.querySelector('#studio-inspector-toggle');if(innerWidth<=960&&!layout.classList.contains('inspector-hidden'))toggle.click();if(layout.classList.contains('inspector-hidden'))toggle.click();})()");
                await WaitAsync(async () => await IsTrueAsync(core, "getComputedStyle(document.querySelector('#editor-panel')).display !== 'none' && document.querySelector('#editor-panel').getBoundingClientRect().width > 200"),
                    "The right template library did not reopen through its normal toggle.", TimeSpan.FromSeconds(10));
                Check(receipt, await IsTrueAsync(core, "(()=>{const r=document.querySelector('#editor-panel').getBoundingClientRect(),m=document.querySelector('#main').getBoundingClientRect();return document.documentElement.scrollWidth<=innerWidth+2&&r.width>200&&r.right<=innerWidth+1&&r.left>=-1&&m.width>180&&m.left>=-1&&m.right<=innerWidth+1;})()"),
                    "At " + width + "px the reopened template library and canvas remain bounded without horizontal clipping.");
                Check(receipt, await IsTrueAsync(core, "(()=>{const rows=[...document.querySelectorAll('.studio-template-row')],list=document.querySelector('.studio-template-list');return rows.length>1&&!!list&&list.scrollWidth<=list.clientWidth+2&&rows.every(row=>{const b=row.getBoundingClientRect(),cells=[...row.querySelectorAll('.studio-template-name,.studio-template-type,.studio-template-project')];return !row.querySelector('svg,img')&&cells.length===3&&row.scrollWidth<=row.clientWidth+2&&cells.every(cell=>{const c=cell.getBoundingClientRect();return c.left>=b.left&&c.right<=b.right&&cell.scrollHeight<=cell.clientHeight+2;});});})()"),
                    "At " + width + "px actual Name, Type and Project rows fit the template list without icons, clipped text or horizontal overflow.");
                using (var geometry = JsonDocument.Parse(await core.ExecuteScriptAsync("({width:innerWidth,rows:[...document.querySelectorAll('.studio-template-row')].slice(0,8).map(row=>({id:row.dataset.libraryId,name:row.querySelector('.studio-template-name').textContent,type:row.querySelector('.studio-template-type').textContent,project:row.querySelector('.studio-template-project').textContent,height:row.getBoundingClientRect().height,columns:getComputedStyle(row).gridTemplateColumns}))})")))
                    receipt.RowGeometry.Add(geometry.RootElement.Clone());
                var screenshot = Path.Combine(fixture, "studio-library-sidebar-" + width + ".png");
                await using (var file = File.Create(screenshot)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                receipt.Renders.Add(screenshot);
                if (width <= 640)
                {
                    await WaitAsync(async () => await IsTrueAsync(core, "!window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending && window.__studioAuthoring.state().currentGenerationReady"),
                        "The saved Page placement had not finished presenting before compact preview selection.", TimeSpan.FromMinutes(2));
                    await core.ExecuteScriptAsync("document.querySelector('[data-library-id]')?.click()");
                    await WaitAsync(async () =>
                    {
                        using var preview = JsonDocument.Parse(await core.ExecuteScriptAsync("({ready:document.querySelector('#studio-library-preview')?.dataset.state === 'ready' && document.querySelector('#studio-library-preview-frame')?.dataset.loaded === 'true',itemId:window.__studioAuthoring.state().libraryPreviewItemId,status:document.querySelector('.studio-library-status')?.textContent || ''})"));
                        var snapshot = preview.RootElement;
                        if (snapshot.GetProperty("ready").GetBoolean()) return true;
                        var error = snapshot.GetProperty("status").GetString();
                        if (snapshot.GetProperty("itemId").ValueKind == JsonValueKind.Null && !string.IsNullOrWhiteSpace(error))
                            throw new InvalidOperationException("The actual compact selection preview failed: " + error);
                        return false;
                    },
                        "The compact selection preview did not render in the actual main workspace.", TimeSpan.FromMinutes(3));
                    Check(receipt, await IsTrueAsync(core, "(()=>{const close=document.querySelector('#studio-library-preview-close'),bounds=close.getBoundingClientRect();return bounds.width>=24&&bounds.height>=24&&document.elementFromPoint(bounds.left+bounds.width/2,bounds.top+bounds.height/2)?.closest('#studio-library-preview-close')===close;})()"),
                        "At " + width + "px the preview Return to Page button receives real pointer hits while the library drawer remains open.");
                    var previewScreenshot = Path.Combine(fixture, "studio-library-selection-preview-" + width + ".png");
                    await using (var file = File.Create(previewScreenshot)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                    receipt.Renders.Add(previewScreenshot);
                    await core.ExecuteScriptAsync("document.querySelector('#studio-library-preview-close').click()");
                    await WaitAsync(async () => await IsTrueAsync(core, "document.querySelector('#studio-library-preview').hidden && !document.querySelector('#page-view').inert"),
                        "Returning from the compact preview did not restore the actual Page.", TimeSpan.FromSeconds(10));
                }
                SampleSafety("resize-" + width);
            }
            Check(receipt, receipt.ForegroundSamples.All(sample => sample.HostHidden && sample.ProcessId != Environment.ProcessId),
                "All sidebar and native bridge verification stays inside the hidden owned fixture without desktop activation.");
        }
        catch
        {
            if (core is not null)
            {
                try
                {
                    receipt.Diagnostics = await core.ExecuteScriptAsync("(()=>({url:location.href,report:window.__studioLibrarySidebarQa,state:window.__studioAuthoring?.state(),dialog:{open:document.querySelector('#dialog')?.open,title:document.querySelector('#dialog-title')?.textContent,error:document.querySelector('#dialog-error')?.textContent,submitDisabled:document.querySelector('#dialog-submit')?.disabled},libraryStatus:document.querySelector('.studio-library-status')?.textContent,inert:{main:document.querySelector('#main')?.inert,contents:document.querySelector('#studio-contents')?.inert,properties:document.querySelector('#properties-content')?.inert},text:document.body.innerText.slice(0,20000)}))()");
                    await using var file = File.Create(Path.Combine(fixture, "studio-library-sidebar-failure.png"));
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                }
                catch (Exception error) { receipt.DiagnosticFailure = error.ToString(); }
            }
            throw;
        }
        finally { host.RootVisual = null; }
    }

    private static CoreWebView2? Core(StudioHyperlibAuthoringView view) =>
        (typeof(StudioHyperlibAuthoringView).GetField("_browser", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as WebView2)?.CoreWebView2;
    private static async Task<bool> IsTrueAsync(CoreWebView2 core, string expression) => await core.ExecuteScriptAsync(expression) == "true";
    private static Dictionary<string, string> AuthoringFingerprint(string project, string library)
    {
        var roots = new[] { project, Path.Combine(library, "Content"), Path.Combine(library, "Library"),
            Path.Combine(library, "Media"), Path.Combine(library, "Templates"), Path.Combine(library, ".hyperlib", "journal") };
        return roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(file => !Path.GetRelativePath(library, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToDictionary(file => Path.GetRelativePath(library, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), StringComparer.OrdinalIgnoreCase);
    }
    private static void Check(Receipt receipt, bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); receipt.Assertions.Add(message); }
    private static Task WaitAsync(Func<bool> predicate, string failure, TimeSpan timeout) => WaitAsync(() => Task.FromResult(predicate()), failure, timeout);
    private static async Task WaitAsync(Func<Task<bool>> predicate, string failure, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end) { if (await predicate()) return; await Task.Delay(50); }
        throw new TimeoutException(failure);
    }
    private static void Layout(FrameworkElement view, double width, double height)
    {
        view.Width = width; view.Height = height; view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }
    private static Task RunStaAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); } catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Isolated Studio library sidebar acceptance" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void LoadResources(string repositoryRoot)
    {
        if (Application.Current is not null) return;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(repositoryRoot, "src/Hypershell/App.xaml")).Root!;
        var dictionary = new XElement(presentation + "ResourceDictionary", source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal)) element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
        dictionary.Descendants().Attributes("Click").Remove();
        new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString()) };
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string Fixture { get; set; } = "";
        public string ProjectRoot { get; set; } = "";
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
        public List<string> StatusMessages { get; } = [];
        public List<string> BrowserMessages { get; } = [];
        public List<JsonElement> RowGeometry { get; } = [];
        public List<ForegroundSample> ForegroundSamples { get; } = [];
        public JsonElement? BrowserResult { get; set; }
        public string? Diagnostics { get; set; }
        public string? DiagnosticFailure { get; set; }
        public string? Failure { get; set; }
    }
    private sealed record ForegroundSample(string Stage, long Window, uint ProcessId, bool HostHidden);
}
