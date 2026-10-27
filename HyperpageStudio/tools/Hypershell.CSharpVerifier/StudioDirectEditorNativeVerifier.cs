using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

/// <summary>Exercises the actual Studio writer, native draft/save bridge, and owned gateway in a permanently hidden HWND.</summary>
public static class StudioDirectEditorNativeVerifier
{
    public static async Task VerifyAsync(string outputDirectory, string repositoryRoot, string libraryRoot)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "studio-direct-editor-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var receipt = new Receipt { Fixture = fixture };
        try
        {
            await RunStaAsync(async () =>
            {
                LoadResources(repositoryRoot);
                await RunAsync(fixture, libraryRoot, receipt);
            });
            receipt.Passed = true;
        }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "studio-direct-editor-native-verification.json"),
                JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"STUDIO_DIRECT_EDITOR_NATIVE_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|fixture={fixture}");
    }

    private static async Task RunAsync(string fixture, string libraryRoot, Receipt receipt)
    {
        var project = await new HyperlibWorkProjectService(libraryRoot).CreateAsync(fixture, "Native direct editor",
            HyperlibWorkProjectService.BuildDirectEditingDefinition("Native direct editor"));
        receipt.ProjectRoot = project;
        var pipeline = await new HyperlibProjectPipelineService().ReadAsync(project)
            ?? throw new InvalidOperationException("The owned direct project has no foundation marker.");
        var ownedLibrary = Path.GetFullPath(Path.Combine(project, "../.."));
        var recoveryPath = Path.Combine(project, ".studio", "authoring-draft.json");
        using var hosts = new HyperlibWorkPreviewHostService();
        using var model = new HyperlibStudioWorkspaceViewModel(project, (root, ct) => hosts.EnsureAsync(root, ct));
        await model.LoadAsync();
        using var view = new StudioHyperlibAuthoringView(model, Path.Combine(fixture, "browser-profile"));
        view.DraftStateChanged += dirty => receipt.DirtyTransitions.Add(dirty);
        view.ContentChanged += () => receipt.ContentChanges++;
        view.StatusChanged += message => receipt.StatusMessages.Add(message);
        using var host = new HwndSource(new HwndSourceParameters("Hidden direct Studio editor native verification")
        {
            Width = 1280, Height = 900, PositionX = -32000, PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
        });
        host.RootVisual = view;
        Layout(view, 1280, 900);
        void SampleSafety(string stage)
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var processId);
            var hidden = !IsWindowVisible(host.Handle);
            receipt.ForegroundSamples.Add(new(stage, foreground.ToInt64(), processId, hidden));
            if (!hidden || processId == Environment.ProcessId)
                throw new InvalidOperationException("The hidden native fixture became visible or acquired the desktop foreground during " + stage + ".");
        }
        CoreWebView2? core = null;
        try
        {
            SampleSafety("host-attached");
            await WaitAsync(() => Core(view) is not null,
                "The actual native authoring control did not initialize its private WebView. " + string.Join(" | ", receipt.StatusMessages), TimeSpan.FromMinutes(4));
            core = Core(view)!;
            core.WebMessageReceived += (_, args) => receipt.BrowserMessages.Add(args.WebMessageAsJson);
            receipt.BrowserProfile = core.Environment.UserDataFolder;
            await WaitAsync(async () => view.CurrentNodeId == pipeline.LandingPageId
                && await IsTrueAsync(core, "window.__studioAuthoring?.state()?.ready === true && !!document.querySelector('#page-frame')?.contentDocument?.querySelector('.hl-document')"),
                "The native editor did not open the editable Work landing Page and its real renderer.", TimeSpan.FromMinutes(4));
            Check(receipt, view.IsLoaded && !IsWindowVisible(host.Handle) && view.CurrentWorkId == pipeline.WorkId,
                "The real native control loads this owned Work and landing Page inside a permanently hidden, nonactivating HWND.");
            Check(receipt, Path.GetFullPath(receipt.BrowserProfile).StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "The native WebView browser profile stays inside the disposable fixture rather than sharing a live Studio session.");
            Check(receipt, await IsTrueAsync(core,
                "JSON.stringify([...document.querySelectorAll('#editor-panel [data-panel]')].map(e=>e.textContent.trim())) === JSON.stringify(['Template','Elements']) && !!document.querySelector('[data-left-panel=\"contents\"]') && !!document.querySelector('[data-left-panel=\"inspector\"]') && !document.querySelector('[data-panel=assistant]')"),
                "The native editor presents Template and Elements on the right and Contents and Properties on the left, with no Assistant tab.");
            await core.ExecuteScriptAsync("document.querySelector('[data-panel=\"elements\"]').click();");
            await WaitAsync(async () => await IsTrueAsync(core, "!!document.querySelector('[data-add-block=\"text\"]')"),
                "The Elements catalogue did not load its native Text tool.", TimeSpan.FromSeconds(15));
            Check(receipt, await IsTrueAsync(core,
                "!!document.querySelector('[data-add-block=\"text\"]') && !!document.querySelector('[data-add-block=\"image\"]') && !/step\\s+\\d+\\s+of\\s+\\d+/i.test(document.body.innerText)"),
                "The landing Page exposes actual text and media elements immediately without a step-by-step wizard.");
            SampleSafety("editor-ready");

            await core.ExecuteScriptAsync("document.querySelector('[data-add-block=\"text\"]').click();document.querySelector('#library-add')?.click();");
            await WaitAsync(async () => await IsTrueAsync(core,
                "!!document.querySelector('#page-frame')?.contentDocument?.querySelector('[data-block-id] [data-field=\"text\"][contenteditable=\"true\"]')"),
                "Adding Text through the real Elements panel did not produce an editable renderer field.", TimeSpan.FromMinutes(3));
            Check(receipt, (await new ContentStore(ownedLibrary).ReadAsync()).Pages[pipeline.LandingPageId].Blocks.Any(block => block.Type == "text"),
                "Selecting Text and using Add in the actual native Elements palette creates a real saved Text block through the owned gateway.");

            // Hold only the next real save request so the native recovery boundary can be observed deterministically.
            // The request is then released unchanged and must pass the normal revision-checked gateway.
            await core.ExecuteScriptAsync("window.__nativeFixtureReleaseSave=null;window.__nativeFixtureHoldSave=true;const originalFetch=window.fetch.bind(window);window.fetch=async(...args)=>{const url=typeof args[0]==='string'?args[0]:args[0]?.url;if(window.__nativeFixtureHoldSave&&url?.includes('/api/operations')&&args[1]?.method==='POST')await new Promise(resolve=>window.__nativeFixtureReleaseSave=resolve);return originalFetch(...args);};");
            var editedText = "Typed directly in the native Hyperpage Studio canvas.";
            var escapedText = JsonSerializer.Serialize(editedText);
            Check(receipt, await IsTrueAsync(core,
                "(()=>{const frame=document.querySelector('#page-frame'),field=frame?.contentDocument?.querySelector('[data-block-id] [data-field=\"text\"][contenteditable=\"true\"]');if(!field)return false;field.textContent=" + escapedText + ";field.dispatchEvent(new frame.contentWindow.Event('input',{bubbles:true}));return true;})()"),
                "The inline edit enters the real renderer field and emits its ordinary input event.");
            await WaitAsync(() => view.HasUnsavedContent && File.Exists(recoveryPath)
                && File.ReadAllText(recoveryPath).Contains(editedText, StringComparison.Ordinal),
                "The inline edit did not reach the native dirty state and project-local recovery file.", TimeSpan.FromSeconds(15));
            Check(receipt, receipt.DirtyTransitions.Contains(true)
                && !(await new ContentStore(ownedLibrary).ReadAsync()).Pages[pipeline.LandingPageId].Blocks.Any(block => block.Values.TryGetValue("text", out var text) && text.GetString() == editedText),
                "The native bridge marks the draft dirty and preserves its recovery while the real content write is pending.");
            await WaitAsync(async () => await IsTrueAsync(core, "typeof window.__nativeFixtureReleaseSave === 'function'"),
                "The inline draft never reached the real save-request boundary.", TimeSpan.FromSeconds(10));
            var nativeSave = view.SaveAsync();
            await Task.Delay(100);
            Check(receipt, !nativeSave.IsCompleted,
                "Native SaveAsync waits for the Page's real gateway acknowledgement rather than reporting an unsaved draft as saved.");
            await core.ExecuteScriptAsync("window.__nativeFixtureHoldSave=false;window.__nativeFixtureReleaseSave?.();");
            Check(receipt, await nativeSave, "The native save bridge confirms the released revision-checked inline edit.");
            await WaitAsync(() => !view.HasUnsavedContent && !File.Exists(recoveryPath),
                "A confirmed native save did not clear dirty state and its recoverable draft.", TimeSpan.FromSeconds(15));
            var saved = (await new ContentStore(ownedLibrary).ReadAsync()).Pages[pipeline.LandingPageId];
            Check(receipt, saved.Blocks.Any(block => block.Values.TryGetValue("text", out var text) && text.GetString() == editedText)
                && receipt.DirtyTransitions.LastOrDefault() == false && receipt.ContentChanges > 0,
                "The real content store contains the inline text, and native dirty/revision events confirm the completed save.");
            Check(receipt, receipt.BrowserMessages.Any(message => message.Contains("studio:flush-result", StringComparison.Ordinal)),
                "The successful Page save returns through the actual WebView-to-native flush-result message.");
            SampleSafety("inline-edit-saved");

            foreach (var width in new[] { 1280, 640, 960 })
            {
                Layout(view, width, 820);
                await WaitAsync(async () => await IsTrueAsync(core, "Math.abs(innerWidth-" + width + ") <= 2"),
                    "The live native browser viewport did not follow the resized host content.", TimeSpan.FromSeconds(10));
                await core.ExecuteScriptAsync("document.querySelector('#page-frame')?.contentDocument?.querySelector('[data-field=\"text\"]')?.click();document.querySelector('#properties-content [data-element-path=\"\"]')?.click();");
                await WaitAsync(async () => await IsTrueAsync(core, "window.__studioAuthoring.state().leftPanel === 'inspector' && !document.querySelector('#navigation').hidden && !document.querySelector('#properties-content').hidden && !!document.querySelector('#properties-content [data-field=\"text\"]')"),
                    "Selecting canvas text and its owning-block breadcrumb did not expose the relocated Properties inspector.", TimeSpan.FromSeconds(10));
                Check(receipt, await IsTrueAsync(core, "(()=>{const n=document.querySelector('#navigation').getBoundingClientRect();return n.width>180&&n.left>=-1&&n.right<=innerWidth+1&&document.querySelector('#properties-content [data-field=\"text\"]').value===" + escapedText + ";})()"),
                    "At " + width + "px selecting canvas text exposes bounded left Properties with its saved content intact.");
                await core.ExecuteScriptAsync("(()=>{const layout=document.querySelector('#app-layout'),toggle=document.querySelector('#studio-inspector-toggle');if(innerWidth<=960&&!layout.classList.contains('inspector-hidden'))toggle.click();if(layout.classList.contains('inspector-hidden'))toggle.click();})()");
                await WaitAsync(async () => await IsTrueAsync(core, "getComputedStyle(document.querySelector('#editor-panel')).display !== 'none' && document.querySelector('#editor-panel').getBoundingClientRect().width > 200"),
                    "The right library did not reopen through its normal responsive toggle.", TimeSpan.FromSeconds(10));
                Check(receipt, await IsTrueAsync(core,
                    "(()=>{const p=document.querySelector('#editor-panel'),m=document.querySelector('#main'),r=p?.getBoundingClientRect(),b=m?.getBoundingClientRect();return !!r&&r.width>=220&&r.height>200&&r.left>=-1&&r.right<=innerWidth+1&&!!b&&b.width>180&&b.left>=-1&&b.right<=innerWidth+1&&document.documentElement.scrollWidth<=innerWidth+2;})()"),
                    "At " + width + "px the native canvas and right Template/Elements panel remain bounded and reachable.");
                var screenshot = Path.Combine(fixture, "direct-editor-native-" + width + ".png");
                await using (var file = File.Create(screenshot)) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                receipt.Renders.Add(screenshot);
                SampleSafety("resize-" + width);
            }
            await view.NavigateToAsync(pipeline.LandingPageId);
            Check(receipt, await view.SaveAsync() && !view.HasUnsavedContent,
                "The saved native writer remains ready after resize and navigation without losing its Page or dirty-state contract.");
            SampleSafety("complete");
            Check(receipt, receipt.ForegroundSamples.All(sample => sample.HostHidden && sample.ProcessId != Environment.ProcessId),
                "The QA host stays hidden and never owns the sampled desktop foreground throughout real WebView editing and resizing.");
        }
        catch
        {
            if (core is not null)
            {
                try
                {
                    await core.ExecuteScriptAsync("window.__nativeFixtureHoldSave=false;window.__nativeFixtureReleaseSave?.();");
                    receipt.BrowserDiagnostics = await core.ExecuteScriptAsync("(()=>({url:location.href,state:window.__studioAuthoring?.state(),body:document.body.innerText.slice(0,16000),frame:document.querySelector('#page-frame')?.contentDocument?.body?.innerText.slice(0,12000)}))()");
                    await using var file = File.Create(Path.Combine(fixture, "direct-editor-native-failure.png"));
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
        view.Width = width; view.Height = height;
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
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
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Isolated direct editor native acceptance" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void LoadResources(string repositoryRoot)
    {
        if (Application.Current is not null) return;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(repositoryRoot, "src/Hypershell/App.xaml")).Root!;
        var dictionary = new XElement(presentation + "ResourceDictionary",
            source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
            source.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
        foreach (var element in dictionary.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
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
        public string BrowserProfile { get; set; } = "";
        public int ContentChanges { get; set; }
        public List<bool> DirtyTransitions { get; } = [];
        public List<string> Assertions { get; } = [];
        public List<string> Renders { get; } = [];
        public List<string> StatusMessages { get; } = [];
        public List<string> BrowserMessages { get; } = [];
        public List<ForegroundSample> ForegroundSamples { get; } = [];
        public string? BrowserDiagnostics { get; set; }
        public string? DiagnosticFailure { get; set; }
        public string? Failure { get; set; }
    }
    private sealed record ForegroundSample(string Stage, long Window, uint ProcessId, bool HostHidden);
}
