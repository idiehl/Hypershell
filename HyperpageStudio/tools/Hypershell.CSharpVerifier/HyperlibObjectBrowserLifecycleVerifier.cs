using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Hypershell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

/// <summary>Exercises the real WebView2 initialization boundary without loading a library or compiling a renderer.</summary>
public static class HyperlibObjectBrowserLifecycleVerifier
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task VerifyAsync(string outputRoot, string sourceLibraryRoot)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLibraryRoot));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Browser lifecycle verification must use output outside the source library.");
        var fixture = Path.Combine(output, "object-browser-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var report = new Receipt { Fixture = fixture, SourceLibrary = source };
        var contentPath = Path.Combine(source, "Content", "library.json");
        var original = File.Exists(contentPath) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(contentPath))) : null;
        try
        {
            await RunStaAsync(async () =>
            {
                if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var dispatcher = Dispatcher.CurrentDispatcher;
                DispatcherUnhandledExceptionEventHandler onUnhandled = (_, args) =>
                {
                    report.Unhandled.Add(args.Exception.ToString());
                    args.Handled = true;
                };
                dispatcher.UnhandledException += onUnhandled;
                try
                {
                    await VerifyOverlappingInitializationAsync(fixture, report);
                    await VerifyCloseDuringInitializationAsync(fixture, report);
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check(report, report.Unhandled.Count == 0, "Overlapping initialization and immediate close produce no unhandled dispatcher exception.");
                }
                finally { dispatcher.UnhandledException -= onUnhandled; }
            }).WaitAsync(TimeSpan.FromSeconds(110));
            Check(report, !Directory.Exists(Path.Combine(fixture, "unopened-library")),
                "The real dialogs never loaded, migrated, or wrote even their private placeholder library.");
            var after = File.Exists(contentPath) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(contentPath))) : null;
            Check(report, original == after, "The user's library content index remains unchanged.");
            report.Passed = true;
        }
        catch (Exception error) { report.Failure = error.ToString(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "hyperlib-object-browser-lifecycle.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"HYPERLIB_OBJECT_BROWSER_LIFECYCLE_OK|assertions:{report.Assertions.Count}|captures:{report.Captures.Count}|actual-webview2|{fixture}");
    }

    private static async Task VerifyOverlappingInitializationAsync(string fixture, Receipt report)
    {
        var dialog = CreateDialog(fixture, "overlap-profile");
        Process? browserProcess = null;
        var browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreWebView2Environment? environment = null;
        EventHandler<CoreWebView2BrowserProcessExitedEventArgs>? onExited = null;
        try
        {
            dialog.Show();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            var first = Initialize(dialog);
            Check(report, !first.IsCompleted, "The first request is still initializing when a second request arrives.");
            var second = Initialize(dialog);
            var views = await Task.WhenAll(first, second).WaitAsync(Timeout);
            Check(report, ReferenceEquals(views[0], views[1]) && views[0].CoreWebView2 is not null,
                "Concurrent preview requests receive the same fully initialized WebView2 control.");
            Check(report, ReferenceEquals(Field<WebView2>(dialog, "_preview"), views[0]),
                "The initialized control remains the dialog's single owned preview browser.");
            var core = views[0].CoreWebView2 ?? throw new InvalidOperationException("The shared preview core was not initialized.");
            Check(report, !core.Settings.AreHostObjectsAllowed && !core.Settings.IsWebMessageEnabled && !core.Settings.AreDevToolsEnabled,
                "Every caller observes the configured preview restrictions before browser initialization completes.");
            environment = core.Environment;
            browserProcess = Process.GetProcessById(checked((int)core.BrowserProcessId));
            report.BrowserProcessIds.Add(browserProcess.Id);
            onExited = (_, _) => browserExited.TrySetResult();
            environment.BrowserProcessExited += onExited;
            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sampleDirectory = Path.Combine(fixture, "sample-page");
            Directory.CreateDirectory(sampleDirectory);
            var sampleAddress = new Uri("https://hyperlib-preview.test/sample.html");
            ulong? expectedNavigationId = null;
            EventHandler<CoreWebView2NavigationStartingEventArgs> onStarting = (_, args) =>
            {
                if (args.Uri == sampleAddress.AbsoluteUri) expectedNavigationId = args.NavigationId;
            };
            EventHandler<CoreWebView2NavigationCompletedEventArgs> onNavigation = (_, args) =>
            {
                if (args.NavigationId != expectedNavigationId) return;
                report.NavigationStatus = args.WebErrorStatus.ToString();
                navigation.TrySetResult(args.IsSuccess);
            };
            core.NavigationStarting += onStarting;
            core.NavigationCompleted += onNavigation;
            try
            {
                await File.WriteAllTextAsync(Path.Combine(sampleDirectory, "sample.html"), """
                    <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                    <style>body{margin:0;padding:40px;background:#f5f3ed;color:#253a32;font:18px/1.6 system-ui}main{max-width:680px;margin:auto}h1{font-size:38px;line-height:1.2}section{padding:24px;border:2px solid #376954;border-radius:10px;background:white}small{color:#60756a}</style></head>
                    <body><main><small>HYPERLIB · BROWSER LIFECYCLE CHECK</small><h1>Preview initialized</h1><section>Two overlapping requests share this ready browser.</section></main></body></html>
                    """);
                core.SetVirtualHostNameToFolderMapping(sampleAddress.Host, sampleDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
                SetField(dialog, "_allowedPreview", sampleAddress);
                core.Navigate(sampleAddress.AbsoluteUri);
                Check(report, await navigation.Task.WaitAsync(Timeout), "The shared initialized browser successfully navigates a harmless local sample (" + report.NavigationStatus + ").");
                var capture = Path.Combine(fixture, "shared-preview-browser.png");
                await using (var image = File.Create(capture))
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image).WaitAsync(Timeout);
                Check(report, new FileInfo(capture).Length > 1000, "The initialized browser produces a real PNG capture.");
                report.Captures.Add(capture);
            }
            finally { core.NavigationStarting -= onStarting; core.NavigationCompleted -= onNavigation; }
            dialog.Close();
            await browserExited.Task.WaitAsync(Timeout);
            Check(report, browserProcess.HasExited, "Closing the dialog shuts down its isolated WebView2 browser process.");
        }
        finally
        {
            if (!Field<bool>(dialog, "_closed")) dialog.Close();
            if (environment is not null && onExited is not null) environment.BrowserProcessExited -= onExited;
            // This PID came from our uniquely profiled control, never a user's browser session.
            if (browserProcess is not null)
            {
                try { if (!browserProcess.HasExited) browserProcess.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                finally { browserProcess.Dispose(); }
            }
        }
    }

    private static async Task VerifyCloseDuringInitializationAsync(string fixture, Receipt report)
    {
        var dialog = CreateDialog(fixture, "immediate-close-profile");
        try
        {
            dialog.Show();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            var pending = Initialize(dialog);
            Check(report, !pending.IsCompleted, "A fresh isolated browser is still initializing when its dialog closes.");
            dialog.Close();
            try { await pending.WaitAsync(Timeout); }
            catch (Exception error) when (Field<bool>(dialog, "_closed")
                && (error is ObjectDisposedException or OperationCanceledException
                    || error is COMException com && com.HResult == unchecked((int)0x80004004)))
            { report.ObservedCloseOutcome = error.GetType().Name; }
            if (report.ObservedCloseOutcome is null) report.ObservedCloseOutcome = "Completed after close without publishing a browser";
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(report, Field<bool>(dialog, "_closed") && Field<WebView2?>(dialog, "_preview") is null,
                "A late initialization completion cannot resurrect a browser after dialog closure.");
        }
        finally { if (!Field<bool>(dialog, "_closed")) dialog.Close(); }
    }

    private static HyperlibObjectLibraryDialog CreateDialog(string fixture, string profile)
    {
        var dialog = new HyperlibObjectLibraryDialog(Path.Combine(fixture, "unopened-library"))
        {
            PreviewEnabled = false, Width = 1100, Height = 800, Left = -30000, Top = -30000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false
        };
        SetField(dialog, "_loaded", true);
        var property = typeof(HyperlibObjectLibraryDialog).GetProperty("PreviewBrowserProfilePath", PrivateInstance)
            ?? throw new MissingMemberException("The preview browser profile isolation hook is unavailable.");
        property.SetValue(dialog, Path.Combine(fixture, profile));
        return dialog;
    }

    private static Task<WebView2> Initialize(HyperlibObjectLibraryDialog dialog)
        => (Task<WebView2>)(typeof(HyperlibObjectLibraryDialog).GetMethod("EnsurePreviewBrowserAsync", PrivateInstance)
            ?? throw new MissingMethodException("The shared preview browser initialization method is unavailable.")).Invoke(dialog, null)!;

    private static T Field<T>(object target, string name)
        => (T)(target.GetType().GetField(name, PrivateInstance) ?? throw new MissingFieldException(name)).GetValue(target)!;

    private static void SetField(object target, string name, object? value)
        => (target.GetType().GetField(name, PrivateInstance) ?? throw new MissingFieldException(name)).SetValue(target, value);

    private static void Check(Receipt report, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        report.Assertions.Add(message);
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
        }) { IsBackground = true, Name = "Hyperlib browser lifecycle verification" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public required string Fixture { get; init; }
        public required string SourceLibrary { get; init; }
        public string? Failure { get; set; }
        public string? ObservedCloseOutcome { get; set; }
        public string? NavigationStatus { get; set; }
        public List<string> Assertions { get; } = [];
        public List<string> Captures { get; } = [];
        public List<int> BrowserProcessIds { get; } = [];
        public List<string> Unhandled { get; } = [];
    }
}
