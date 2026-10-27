using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Runtime.InteropServices;

internal static class MyNetAppWindowVerifier
{
    public static Task VerifyAsync(string verifierRoot, bool websiteOnly = false)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (websiteOnly) await VerifyWebsiteBrowserOnDispatcherAsync(verifierRoot);
                    else await VerifyOnDispatcherAsync(verifierRoot);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(
                        DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "MyNet browser verifier"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task VerifyWebsiteBrowserOnDispatcherAsync(string verifierRoot)
    {
        Require(Application.Current is null, "Website verification needs an isolated application.");
        var application = new App { SuppressShellStartup = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();
        var settings = new SettingsService(Path.Combine(verifierRoot, "profile"));
        await using var fixture = new LoopbackPageFixture();
        var websiteUrl = fixture.Url.Replace("127.0.0.1", "meridian-browser.localhost", StringComparison.Ordinal);
        var definition = new AtlasMyNetAppDefinition
        {
            Id = "atlas.meridian", DisplayName = "Meridian", UrlTemplate = fixture.Url,
            Ports = [new Uri(fixture.Url).Port], AllowExternalResources = false, ShowNavigationToolbar = false,
            HealthCheck = new AtlasMyNetHealthCheckDefinition { Path = "/", ExpectedStatusCode = 200 }
        };
        MyNetAppWindow? editor = null;
        try
        {
            // Resolve through the actual standalone-launch path, reusing a
            // healthy disposable server without executing any launch command.
            var store = new AtlasMyNetAppStore(settings.AtlasMyNetAppsPath, settings.AtlasMyNetTrustPath);
            store.Save([definition]);
            var request = await MeridianDesktopLaunch.ResolveAsync(settings);
            Require(request.Url == fixture.Url && request.Definition.Id == "atlas.meridian",
                "Standalone Meridian did not reuse the healthy registered endpoint.");
            editor = CreateHiddenWindow(request, settings);
            var model = (BrowserTabViewModel)editor.DataContext;
            var view = (WebView2)model.BrowserView.FindName("Browser");
            await WaitForPageAsync(model, view, fixture.Url);
            var core = view.CoreWebView2;
            Require(await core.ExecuteScriptAsync("window.__meridianWebsiteBrowser === true;") == "true",
                "The native website-browser capability marker is missing.");
            await core.ExecuteScriptAsync("window.meridianUnsavedProbe='keep this draft';");
            await core.ExecuteScriptAsync($"window.open({JsonSerializer.Serialize(websiteUrl)}, '_blank', 'popup,width=1120,height=820,noopener,noreferrer');");
            MeridianBrowserWindow? website = null;
            await WaitUntilAsync(() => (website = application.Windows.OfType<MeridianBrowserWindow>().SingleOrDefault()) is not null,
                "A website popup did not open in Meridian's native browser.");
            var websiteModel = website!.BrowserModel;
            var websiteView = (WebView2)websiteModel.BrowserView.FindName("Browser");
            await WaitForPageAsync(websiteModel, websiteView, websiteUrl);
            var webCore = websiteView.CoreWebView2;
            Require(!website.ShowInTaskbar && website.Left < -5000 && website.Top < -5000,
                "The website verifier escaped its offscreen settings.");
            Require(websiteModel.ShowNavigationToolbar && websiteModel.NavigationPolicy is null,
                "The website reader did not expose normal browser navigation.");
            Require(webCore.Profile.IsInPrivateModeEnabled && webCore.Environment.UserDataFolder != core.Environment.UserDataFolder,
                "The website browser inherited Meridian's trusted profile.");
            Require(!webCore.Settings.IsWebMessageEnabled && !webCore.Settings.AreHostObjectsAllowed,
                "A remote website received a native host bridge.");
            Require(await webCore.ExecuteScriptAsync("!window.__meridianNativeChrome && !window.__meridianWebsiteBrowser && window.opener === null;") == "true",
                "The website inherited app privileges or a Work opener.");
            var next = websiteUrl + "next";
            websiteModel.BrowserView.NavigateTo(next);
            await WaitForPageAsync(websiteModel, websiteView, next);
            Require(webCore.CanGoBack, "Website navigation did not retain history.");
            webCore.GoBack();
            await WaitForPageAsync(websiteModel, websiteView, websiteUrl);
            Require(webCore.CanGoForward, "Website Back lost forward history.");
            webCore.GoForward();
            await WaitForPageAsync(websiteModel, websiteView, next);
            webCore.Reload();
            await WaitForPageAsync(websiteModel, websiteView, next);
            var nested = websiteUrl + "nested";
            await webCore.ExecuteScriptAsync($"window.open({JsonSerializer.Serialize(nested)}, '_blank');");
            await WaitForPageAsync(websiteModel, websiteView, nested);
            Require(application.Windows.OfType<MeridianBrowserWindow>().Count() == 1,
                "Website popups escaped the browser reader.");
            webCore.Navigate("file:///C:/meridian-browser-private-fixture.html");
            await WaitUntilAsync(() => websiteModel.HasError, "Non-website navigation was not blocked.");
            Require(webCore.Source == nested, "The website browser navigated into a file URL.");
            websiteModel.BrowserView.NavigateTo(websiteUrl);
            await WaitForPageAsync(websiteModel, websiteView, websiteUrl);
            Require(!MeridianBrowserWindow.IsWebsiteAddress("javascript:alert(1)")
                    && !MeridianBrowserWindow.IsWebsiteAddress("data:text/html,test")
                    && !MeridianBrowserWindow.IsWebsiteAddress("https://user:secret@example.com/")
                    && MeridianBrowserWindow.IsWebsiteAddress("https://learn.microsoft.com/dotnet/api/system"),
                "Website URL validation accepts non-web or credential-bearing destinations.");
            var learnUrl = "https://learn.microsoft.com/en-us/dotnet/api/system.text?view=net-10.0";
            websiteModel.BrowserView.NavigateTo(learnUrl);
            await WaitForPageAsync(websiteModel, websiteView, learnUrl);
            Require(webCore.DocumentTitle.Contains("System.Text", StringComparison.Ordinal)
                    && await webCore.ExecuteScriptAsync("!window.__meridianNativeChrome && !window.__meridianWebsiteBrowser && window.opener === null;") == "true",
                "Microsoft Learn did not load as an isolated website.");
            await FlushLayoutAsync(website);
            await SaveHyperlibScreenshotAsync(website, websiteView, verifierRoot, "native-learn-browser.png", 1);
            File.WriteAllText(Path.Combine(verifierRoot, "website-smoke.json"), JsonSerializer.Serialize(new
            {
                url = webCore.Source, title = webCore.DocumentTitle,
                privateProfile = webCore.Profile.IsInPrivateModeEnabled,
                webMessages = webCore.Settings.IsWebMessageEnabled,
                hostObjects = webCore.Settings.AreHostObjectsAllowed,
                editorUrl = core.Source,
                capture = "native-learn-browser.png"
            }, new JsonSerializerOptions { WriteIndented = true }));
            website.Close();
            RequireBrowserDisposed(websiteView);
            core.Navigate(websiteUrl + "same-target");
            await WaitUntilAsync(() => application.Windows.OfType<MeridianBrowserWindow>().Any(),
                "Same-target external navigation did not use the website reader.");
            Require(core.Source == fixture.Url && editor.IsVisible
                    && (await core.ExecuteScriptAsync("window.meridianUnsavedProbe;")).Contains("keep this draft", StringComparison.Ordinal),
                "Website browsing replaced or discarded the original Work.");
            // Preserve ordinary Work/media popup behavior with the same patch.
            foreach (var popup in application.Windows.OfType<MeridianBrowserWindow>().ToArray()) popup.Close();
            await VerifyIndependentCanvasWindowsAsync(editor, settings, fixture.Url);
            Console.WriteLine("MERIDIAN_WEBSITE_BROWSER_OK|standalone-endpoint-reuse|native-popup|same-target|history|reload|address-navigation|nested-popup|unbridged|isolated-private-profile|invalid-schemes|opener-isolation|draft-preserved|local-work-popups");
        }
        finally
        {
            foreach (var popup in application.Windows.OfType<MeridianBrowserWindow>().ToArray()) popup.Close();
            editor?.Close();
            application.Shutdown();
        }
    }

    private static async Task VerifyOnDispatcherAsync(string verifierRoot)
    {
        Require(Application.Current is null,
            "The isolated MyNet browser verifier needs its own application.");
        var application = new App
        {
            SuppressShellStartup = true,
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        application.InitializeComponent();
        var settings = new SettingsService(
            Path.Combine(verifierRoot, "mynet-app-window"));
        var fixture = new LoopbackPageFixture();
        var definition = new AtlasMyNetAppDefinition
        {
            Id = "../app-id-must-not-be-a-directory",
            DisplayName = "MyNet browser verifier",
            UrlTemplate = fixture.Url,
            AllowExternalResources = false,
            ShowNavigationToolbar = false,
            IconPath = Path.Combine(verifierRoot, "missing.ico")
        };
        var request = new AtlasMyNetOpenAppRequest(definition, fixture.Url);
        MyNetAppWindow? first = null;
        MyNetAppWindow? second = null;
        var unrelatedWindow = new Window
        {
            Title = "Unrelated verifier window",
            Width = 120,
            Height = 100,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };
        try
        {
            VerifyToolbarPersistence(verifierRoot, definition);
            VerifyOrdinaryBrowserToolbar(fixture.Url);
            unrelatedWindow.Show();
            first = CreateHiddenWindow(request, settings);
            var firstModel = (BrowserTabViewModel)first.DataContext;
            var firstWebView = (WebView2)firstModel.BrowserView.FindName("Browser");
            await WaitForPageAsync(firstModel, firstWebView, fixture.Url);
            var core = firstWebView.CoreWebView2;
            Require(core.Profile.IsInPrivateModeEnabled,
                "MyNet app windows must use a real InPrivate WebView2 profile.");
            Require(Path.GetFullPath(core.Environment.UserDataFolder).StartsWith(
                    Path.GetFullPath(settings.AtlasMyNetDirectory)
                    + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase),
                "The MyNet browser profile escaped the overridden application data root.");
            Require(!core.Environment.UserDataFolder.Contains(
                    "app-id-must-not-be-a-directory", StringComparison.Ordinal),
                "An app ID was interpreted as a browser profile path.");
            Require(first.Owner is null,
                "An app window must remain independent of the launcher window.");
            await VerifyToolbarAndWindowChromeAsync(first, definition, settings, fixture.Url);
            await VerifyMeridianWindowChromeAsync(first, definition, settings, fixture.Url);
            await VerifyHyperlibWindowChromeAsync(first, definition, settings, fixture.Url);
            await VerifyIndependentCanvasWindowsAsync(first, settings, fixture.Url);

            var tray = new MyNetDesktopTrayFixture();
            var launcher = new MyNetDesktopWindow(settings, tray)
            {
                Left = -10000, Top = -10000
            };
            try
            {
                launcher.ShowDesktop();
                await core.ExecuteScriptAsync("window.myNetUnsavedDraft = 'preserve this open document';");
                launcher.Close();
                Require(!launcher.IsVisible && first.IsVisible && tray.DisposeCount == 0,
                    "Hiding the launcher must retain the independent app window and its tray.");
                tray.RequestOpen();
                tray.RequestExit();
                Require(!launcher.IsVisible && first.IsVisible && tray.DisposeCount == 1,
                    "Explicit launcher Exit must leave the independent app window open.");
                Require((await core.ExecuteScriptAsync("window.myNetUnsavedDraft;")).Contains(
                        "preserve this open document", StringComparison.Ordinal),
                    "Closing the launcher discarded the app's unsaved browser state.");
                using var client = new HttpClient();
                Require((await client.GetAsync(fixture.Url)).IsSuccessStatusCode,
                    "Closing the launcher stopped the independent app server.");
            }
            finally
            {
                if (tray.DisposeCount == 0)
                {
                    launcher.PrepareForShutdown();
                    launcher.Close();
                }
            }

            var blockedResource = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void ResourceObserved(object? sender,
                CoreWebView2WebResourceRequestedEventArgs args)
            {
                if (args.Request.Uri.Contains("mynet-policy.invalid",
                        StringComparison.Ordinal))
                {
                    blockedResource.TrySetResult(args.Response?.StatusCode ?? 0);
                }
            }
            core.WebResourceRequested += ResourceObserved;
            await core.ExecuteScriptAsync(
                "fetch('https://mynet-policy.invalid/verifier-resource').catch(() => {});");
            Require(await blockedResource.Task.WaitAsync(TimeSpan.FromSeconds(10)) == 403,
                "MyNet local-only browsing allowed an external page resource.");
            core.WebResourceRequested -= ResourceObserved;

            core.Navigate("file:///C:/mynet-private-verifier.html");
            await WaitUntilAsync(() => firstModel.HasError,
                "The non-web navigation was not blocked.");
            Require(core.Source == fixture.Url,
                "A managed app navigated away to a file URL.");
            firstModel.BrowserView.NavigateTo(fixture.Url);
            await WaitForPageAsync(firstModel, firstWebView, fixture.Url);

            var cookie = await core.ExecuteScriptAsync(
                "document.cookie = 'mynet_private_probe=present; path=/'; document.cookie;");
            Require(cookie.Contains("mynet_private_probe=present", StringComparison.Ordinal),
                "The private session cookie fixture could not be set.");
            first.Close();
            RequireBrowserDisposed(firstWebView);
            Require(unrelatedWindow.IsVisible,
                "Closing an app window also closed an unrelated window.");
            first = null;

            second = CreateHiddenWindow(request, settings);
            var secondModel = (BrowserTabViewModel)second.DataContext;
            var secondWebView = (WebView2)secondModel.BrowserView.FindName("Browser");
            await WaitForPageAsync(secondModel, secondWebView, fixture.Url);
            var reopenedCookie = await secondWebView.CoreWebView2.ExecuteScriptAsync(
                "document.cookie;");
            Require(!reopenedCookie.Contains("mynet_private_probe", StringComparison.Ordinal),
                "A private cookie survived closing the last app window.");
            second.Close();
            second = null;

            try
            {
                var invalid = new MyNetAppWindow(
                    new AtlasMyNetOpenAppRequest(definition, "https://example.com/"),
                    settings);
                invalid.Close();
                throw new InvalidDataException(
                    "A MyNet app window accepted an external initial address.");
            }
            catch (InvalidOperationException)
            {
            }

            Console.WriteLine(
                "MYNET_APP_WINDOW_OK|loopback|private-profile|isolated-data|resource-policy|cookie-reset|disposal|toolbar-preference|window-chrome|maximize-work-area|native-minimum-tracks|bottom-panel-scroll|independent-canvas-windows|meridian-file-menu|canvas-navigation|native-chrome-marker|native-import-activation|hyperlib-compact-chrome|hyperlib-edit-state|hyperlib-history|hyperlib-origin-frame-scope");
        }
        finally
        {
            first?.Close();
            second?.Close();
            foreach (var popup in application.Windows.OfType<MyNetAppWindow>().ToArray())
            {
                popup.Close();
            }
            unrelatedWindow.Close();
            await fixture.DisposeAsync();
            application.Shutdown();
        }
    }

    private static MyNetAppWindow CreateHiddenWindow(
        AtlasMyNetOpenAppRequest request,
        SettingsService settings)
    {
        var window = new MyNetAppWindow(request, settings)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        window.Show();
        return window;
    }

    private static void VerifyToolbarPersistence(
        string verifierRoot,
        AtlasMyNetAppDefinition definition)
    {
        Require(new AtlasMyNetAppDefinition().ShowNavigationToolbar,
            "Existing app definitions must retain browser navigation by default.");
        Require(!definition.Clone().ShowNavigationToolbar
                && !AtlasMyNetAppStore.ValidateDefinition(definition).ShowNavigationToolbar,
            "Cloning or validating an app lost its hidden-toolbar preference.");
        var root = Path.Combine(verifierRoot, "mynet-toolbar-registry");
        Directory.CreateDirectory(root);
        var store = new AtlasMyNetAppStore(
            Path.Combine(root, "apps.json"),
            Path.Combine(root, "trust.json"));
        var commandDefinition = definition.Clone();
        commandDefinition.Launch = new AtlasMyNetLaunchDefinition
        {
            Executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Verifier executable is unavailable."),
            WorkingDirectory = root,
            Arguments = ["--verifier-only-never-launched"]
        };
        store.Save([commandDefinition]);
        store.Trust(commandDefinition);
        var roundTripped = store.Load().Single(app => app.Id == definition.Id);
        Require(!roundTripped.ShowNavigationToolbar,
            "Saving and reloading an app lost its hidden-toolbar preference.");
        roundTripped.ShowNavigationToolbar = true;
        Require(store.IsTrusted(roundTripped),
            "A presentation-only change incorrectly revoked command trust.");
        roundTripped.ShowNavigationToolbar = false;
        var exportPath = Path.Combine(root, "export.json");
        store.Export(exportPath, [roundTripped]);
        Require(!store.ReadImport(exportPath).Single().ShowNavigationToolbar,
            "Exporting and importing an app lost its hidden-toolbar preference.");
        var legacyPath = Path.Combine(root, "legacy.json");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = AtlasMyNetAppDocument.CurrentSchemaVersion,
            Apps = new[] { new { definition.Id, definition.DisplayName, definition.UrlTemplate } }
        }));
        Require(store.ReadImport(legacyPath).Single().ShowNavigationToolbar,
            "An older app manifest without a toolbar field must keep its toolbar.");

        var dialog = new AtlasMyNetAppDialog(definition);
        try
        {
            var toolbarChoice = (CheckBox)dialog.FindName("ShowNavigationToolbarCheckBox");
            Require(toolbarChoice.IsChecked == false,
                "Editing an app did not load its hidden-toolbar preference.");
            var buildDefinition = typeof(AtlasMyNetAppDialog).GetMethod(
                "BuildDefinition", System.Reflection.BindingFlags.Instance
                                   | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidDataException("The app settings definition builder is unavailable.");
            var edited = (AtlasMyNetAppDefinition)buildDefinition.Invoke(dialog, null)!;
            Require(!edited.ShowNavigationToolbar,
                "Saving the app settings dialog restored the hidden browser toolbar.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void VerifyOrdinaryBrowserToolbar(string url)
    {
        using var browser = new BrowserTabViewModel(url);
        Require(browser.ShowNavigationToolbar,
            "Ordinary browser tabs must retain their navigation toolbar by default.");
        browser.BrowserView.Measure(new Size(800, 600));
        browser.BrowserView.Arrange(new Rect(0, 0, 800, 600));
        browser.BrowserView.UpdateLayout();
        Require(((Border)browser.BrowserView.FindName("NavigationToolbar")).Visibility
                == Visibility.Visible,
            "The ordinary browser tab's navigation toolbar is hidden.");
    }

    private static async Task VerifyToolbarAndWindowChromeAsync(
        MyNetAppWindow app,
        AtlasMyNetAppDefinition definition,
        SettingsService settings,
        string url)
    {
        var model = (BrowserTabViewModel)app.DataContext;
        var browser = (WebView2)model.BrowserView.FindName("Browser");
        var toolbar = (Border)model.BrowserView.FindName("NavigationToolbar");
        var address = (TextBox)model.BrowserView.FindName("AddressTextBox");
        var standardDefinition = definition.Clone();
        standardDefinition.ShowNavigationToolbar = true;
        var standard = CreateHiddenWindow(
            new AtlasMyNetOpenAppRequest(standardDefinition, url), settings);
        try
        {
            var standardModel = (BrowserTabViewModel)standard.DataContext;
            var standardBrowser = (WebView2)standardModel.BrowserView.FindName("Browser");
            var standardToolbar = (Border)standardModel.BrowserView.FindName("NavigationToolbar");
            await WaitForPageAsync(standardModel, standardBrowser, url);
            foreach (var size in new[] { new Size(480, 360), new Size(1400, 900) })
            {
                app.Width = standard.Width = size.Width;
                app.Height = standard.Height = size.Height;
                await FlushLayoutAsync(app);
                await FlushLayoutAsync(standard);
                Require(!model.ShowNavigationToolbar && toolbar.Visibility == Visibility.Collapsed,
                    "A native app requesting no toolbar still displays browser navigation.");
                Require(((Grid)model.BrowserView.Content).RowDefinitions[0].ActualHeight == 0,
                    "A hidden toolbar left a blank row above the app content.");
                Require(!address.IsVisible && !address.Focus() && !address.IsKeyboardFocusWithin,
                    "The hidden address field can still take keyboard focus.");
                Require(standardModel.ShowNavigationToolbar
                        && standardToolbar.Visibility == Visibility.Visible
                        && Math.Abs(standardToolbar.ActualHeight - 44) < 0.5,
                    "Standard app windows lost their 44px navigation toolbar.");
                Require(Math.Abs(browser.ActualHeight - standardBrowser.ActualHeight - 44) < 0.5,
                    "Hiding browser navigation did not return its full height to the canvas.");
                Require(Math.Abs(browser.ActualWidth - standardBrowser.ActualWidth) < 0.5
                        && browser.ActualWidth > 0 && browser.ActualHeight > 0,
                    "Resizing a toolbar-free app clipped or lost its browser content.");
            }
        }
        finally
        {
            standard.Close();
        }

        // Keep native state transitions invisible even when maximizing would move
        // the offscreen verifier window back onto the user's monitor.
        var windowHandle = new WindowInteropHelper(app).Handle;
        var cloaked = 1;
        Require(DwmSetWindowAttribute(windowHandle, 13, ref cloaked, sizeof(int)) >= 0,
            "The isolated native-window state checks could not cloak their test window.");
        try
        {
            RequireNativeMinimumTrackSize(app, "normal window");
            var maximize = (Button)app.FindName("MaximizeButton");
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => app.WindowState == WindowState.Maximized,
                "The native maximize button did not maximize the app.");
            Require(string.Equals(maximize.ToolTip?.ToString(), "Restore", StringComparison.Ordinal),
                "The native maximize button did not expose its restore action.");
            await FlushLayoutAsync(app);
            RequireMaximizedClientWithinWorkArea(app, "initial maximize");
            RequireNativeMinimumTrackSize(app, "maximized window");
            await VerifyBottomPanelScrollAsync(app, browser);
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => app.WindowState == WindowState.Normal,
                "The native restore button did not restore the app.");
            var minimize = Descendants<Button>(app).Single(button =>
                AutomationProperties.GetName(button) == "Minimize");
            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => app.WindowState == WindowState.Minimized,
                "The native minimize button did not minimize the app.");
            SystemCommands.RestoreWindow(app);
            await WaitUntilAsync(() => app.WindowState == WindowState.Normal,
                "The app did not restore after native minimize.");
            await FlushLayoutAsync(app);
            Require(toolbar.Visibility == Visibility.Collapsed && browser.ActualHeight > 0,
                "A native window-state change restored browser navigation or lost app content.");

            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => app.WindowState == WindowState.Maximized,
                "The app did not maximize again after restoring its normal window.");
            await FlushLayoutAsync(app);
            RequireMaximizedClientWithinWorkArea(app, "repeat maximize");
            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => app.WindowState == WindowState.Minimized,
                "The maximized app did not minimize.");
            SystemCommands.RestoreWindow(app);
            await WaitUntilAsync(() => app.WindowState == WindowState.Maximized,
                "Restoring a minimized maximized app lost its maximized state.");
            await FlushLayoutAsync(app);
            RequireMaximizedClientWithinWorkArea(app, "maximize/minimize/restore");
            await VerifyBottomPanelScrollAsync(app, browser);
            Require(toolbar.Visibility == Visibility.Collapsed && browser.ActualHeight > 0,
                "Restoring a maximized app restored browser navigation or lost app content.");
        }
        finally
        {
            app.WindowState = WindowState.Normal;
            app.Left = -10000;
            app.Top = -10000;
            cloaked = 0;
            DwmSetWindowAttribute(windowHandle, 13, ref cloaked, sizeof(int));
        }
    }

    private static async Task VerifyMeridianWindowChromeAsync(
        MyNetAppWindow ordinary,
        AtlasMyNetAppDefinition definition,
        SettingsService settings,
        string url)
    {
        Require(((Grid)ordinary.FindName("AppIdentity")).Visibility == Visibility.Visible
                && ((StackPanel)ordinary.FindName("MeridianNavigation")).Visibility == Visibility.Collapsed,
            "Meridian's canvas menu replaced an unrelated MyNet app's titlebar.");
        var ordinaryCore = ((WebView2)((BrowserTabViewModel)ordinary.DataContext)
            .BrowserView.FindName("Browser")).CoreWebView2;
        Require(await ordinaryCore.ExecuteScriptAsync("window.__meridianNativeChrome === true") == "false",
            "A non-Meridian app received the native canvas command bridge.");

        var meridianDefinition = definition.Clone();
        meridianDefinition.Id = "atlas.meridian";
        meridianDefinition.DisplayName = "Atlas Meridian";
        var meridian = CreateHiddenWindow(
            new AtlasMyNetOpenAppRequest(meridianDefinition, url), settings);
        try
        {
            var model = (BrowserTabViewModel)meridian.DataContext;
            var webView = (WebView2)model.BrowserView.FindName("Browser");
            await WaitForPageAsync(model, webView, url);
            var core = webView.CoreWebView2;
            Require(!core.Settings.IsWebMessageEnabled,
                "The native File menu enabled an unrestricted page-to-host message channel.");
            Require(meridian.ShouldHandleCanvasHistoryShortcut(Key.Left, ModifierKeys.Alt, false)
                    && meridian.ShouldHandleCanvasHistoryShortcut(Key.Right, ModifierKeys.Alt, false)
                    && !meridian.ShouldHandleCanvasHistoryShortcut(Key.Left, ModifierKeys.Alt, true)
                    && !meridian.ShouldHandleCanvasHistoryShortcut(Key.Right, ModifierKeys.Alt, true)
                    && !ordinary.ShouldHandleCanvasHistoryShortcut(Key.Left, ModifierKeys.Alt, false),
                "Native canvas history intercepts browser node-disclosure keys or unrelated app shortcuts.");
            Require(await core.ExecuteScriptAsync("(() => {"
                    + "const node = document.createElement('button'); document.body.append(node);"
                    + "node.addEventListener('keydown', event => {"
                    + "if (event.altKey && event.key === 'ArrowLeft') event.preventDefault();});"
                    + "const event = new KeyboardEvent('keydown',"
                    + "{key:'ArrowLeft',altKey:true,bubbles:true,cancelable:true});"
                    + "const dispatched = node.dispatchEvent(event); node.remove();"
                    + "return event.defaultPrevented && !dispatched; })()") == "true",
                "The canvas document could not retain its handled node-disclosure keyboard event.");
            Require(await core.ExecuteScriptAsync("window.__meridianNativeChrome === true") == "true",
                "Meridian's native-chrome marker was not ready for the first document.");
            Require(await core.ExecuteScriptAsync("window.meridianMarkerAtFirstScript === true") == "true",
                "Meridian's native-chrome marker was injected after the page's scripts ran.");
            Require(((Grid)meridian.FindName("AppIdentity")).Visibility == Visibility.Collapsed,
                "Meridian still repeats its icon and title in the outer native titlebar.");
            var navigation = (StackPanel)meridian.FindName("MeridianNavigation");
            Require(navigation.Visibility == Visibility.Visible,
                "Meridian's Back, Forward, File, and Tools titlebar controls are hidden.");
            Require(navigation.Parent is Grid { Margin.Left: 0, Background: not null },
                "The native header retains a left margin that exposes a dark gap.");
            var back = (Button)meridian.FindName("CanvasBackButton");
            var forward = (Button)meridian.FindName("CanvasForwardButton");
            var file = (Button)meridian.FindName("FileMenuButton");
            var tools = (Button)meridian.FindName("ToolsMenuButton");
            Require(!back.IsEnabled && !forward.IsEnabled,
                "The initial canvas enables history controls without any history.");
            Require(AutomationProperties.GetName(back) == "Back"
                    && AutomationProperties.GetName(forward) == "Forward"
                    && AutomationProperties.GetName(file) == "File"
                    && AutomationProperties.GetName(tools) == "Tools",
                "Native canvas menu controls lack their accessible names.");
            Require(file.Content is AccessText { Foreground: SolidColorBrush fileForeground }
                    && fileForeground.Color == Colors.White
                    && tools.Content is AccessText { Foreground: SolidColorBrush toolsForeground }
                    && toolsForeground.Color == Colors.White
                    && tools.Focusable && tools.IsTabStop
                    && WindowChrome.GetIsHitTestVisibleInChrome(tools),
                "Native File and Tools labels are dark or the Tools control cannot receive input.");
            var menu = file.ContextMenu
                ?? throw new InvalidDataException("The native File button has no menu.");
            var items = menu.Items.OfType<MenuItem>().ToArray();
            var commands = new[] { "save", "save-as", "checkpoint", "duplicate", "import",
                "history", "preview", "publish", "export" };
            var labels = new[] { "Save", "Save as", "Create checkpoint", "Duplicate", "Import",
                "History", "Preview", "Publish", "Export Work" };
            Require(items.Select(item => item.Tag?.ToString()).SequenceEqual(commands)
                    && items.Select(item => item.Header?.ToString()?.Replace("_", "", StringComparison.Ordinal))
                        .SequenceEqual(labels),
                "The native File menu lost a canvas command or its concise label.");
            var toolsMenu = tools.ContextMenu
                ?? throw new InvalidDataException("The native Tools button has no menu.");
            var toolsItems = toolsMenu.Items.OfType<MenuItem>().ToArray();
            var toolCommands = new[] { "modules", "composition" };
            Require(toolsItems.Select(item => item.Tag?.ToString()).SequenceEqual(toolCommands)
                    && toolsItems.Select(item => item.Header?.ToString()?.Replace("_", "", StringComparison.Ordinal))
                        .SequenceEqual(new[] { "Modules", "Composition tools" }),
                "The native Tools menu lost either relocated canvas tool.");
            tools.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(toolsMenu.IsOpen && ReferenceEquals(toolsMenu.PlacementTarget, tools),
                "The native Tools menu did not open below its own titlebar button.");
            toolsMenu.IsOpen = false;
            foreach (var item in toolsItems)
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            var expectedTools = JsonSerializer.Serialize(toolCommands);
            await WaitForScriptAsync(core, "JSON.stringify(window.meridianToolCommands)",
                JsonSerializer.Serialize(expectedTools),
                "Native Tools commands did not reach Meridian's matching command listener.");
            toolsItems[0].Tag = "not-an-allowed-tool-command";
            toolsItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            toolsItems[0].Tag = toolCommands[0];
            await Task.Delay(50);
            Require(JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(
                        "JSON.stringify(window.meridianToolCommands)")) == expectedTools,
                "The native Tools menu dispatched a command outside its explicit allowlist.");
            var qaDirectory = Environment.GetEnvironmentVariable("HYPERSHELL_MERIDIAN_CHROME_QA")
                ?? Path.Combine(AppContext.BaseDirectory, "meridian-chrome-qa");
            Directory.CreateDirectory(qaDirectory);
            foreach (var width in new[] { 420d, 1120d })
            {
                meridian.Width = width;
                meridian.Height = width == 420 ? 420 : 700;
                await FlushLayoutAsync(meridian);
                var visual = (FrameworkElement)meridian.Content;
                var previousRight = 0d;
                foreach (var element in new FrameworkElement[] { back, forward, file, tools })
                {
                    var bounds = element.TransformToAncestor(visual)
                        .TransformBounds(new Rect(element.RenderSize));
                    Require(element.IsVisible && bounds.Width > 0 && bounds.Height > 0
                            && bounds.Left >= previousRight - 0.5
                            && bounds.Right <= visual.ActualWidth - 132 + 0.5
                            && bounds.Top >= 0 && bounds.Bottom <= 38,
                        $"The {width}px Meridian bar overlaps, clips or reorders a navigation/menu control.");
                    previousRight = bounds.Right;
                }
                await SaveHyperlibScreenshotAsync(meridian, webView, qaDirectory,
                    $"meridian-titlebar-{(int)width}-100.png", 1);
                await SaveHyperlibScreenshotAsync(meridian, webView, qaDirectory,
                    $"meridian-titlebar-{(int)width}-150.png", 1.5);
            }
            // Let any earlier transient activation expire, then inspect it with
            // an explicitly non-activating probe before invoking the native menu.
            await Task.Delay(TimeSpan.FromMilliseconds(5500));
            using (var inactive = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync(
                       "Runtime.evaluate", "{\"expression\":\"navigator.userActivation.isActive\","
                       + "\"returnByValue\":true,\"userGesture\":false}")))
            {
                Require(!inactive.RootElement.GetProperty("result").GetProperty("value").GetBoolean(),
                    "The native File-menu activation fixture began with an earlier active gesture.");
            }
            foreach (var item in items)
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            var expectedCommands = JsonSerializer.Serialize(commands);
            await WaitForScriptAsync(core, "JSON.stringify(window.meridianFileCommands)",
                JsonSerializer.Serialize(expectedCommands),
                "Native File commands did not reach Meridian's matching command listener.");
            Require(await core.ExecuteScriptAsync(
                    "window.meridianCommandActivations.some(item => item.command === 'import' && item.active)")
                    == "true",
                "The native Import command reached the document without browser user activation.");
            var originalTag = items[0].Tag;
            items[0].Tag = "not-an-allowed-canvas-command";
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[0].Tag = originalTag;
            await Task.Delay(50);
            Require(JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(
                        "JSON.stringify(window.meridianFileCommands)")) == expectedCommands,
                "The native File menu dispatched a command outside its explicit allowlist.");

            var canvasUrl = url + "?canvas=in-current-window";
            await core.ExecuteScriptAsync($"window.location.assign({JsonSerializer.Serialize(canvasUrl)});");
            await WaitForPageAsync(model, webView, canvasUrl);
            await FlushLayoutAsync(meridian);
            Require(back.IsEnabled && !forward.IsEnabled,
                "The native history buttons did not follow the current canvas history.");
            var windowCount = Application.Current.Windows.OfType<MyNetAppWindow>().Count();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForPageAsync(model, webView, url);
            await FlushLayoutAsync(meridian);
            Require(!back.IsEnabled && forward.IsEnabled,
                "Back did not restore the previous canvas and enable Forward.");
            Require(await core.ExecuteScriptAsync("sessionStorage.getItem('meridian-last-direction')") == "\"back\"",
                "Native Back bypassed the canvas navigation listener and its save boundary.");
            forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForPageAsync(model, webView, canvasUrl);
            Require(await core.ExecuteScriptAsync("sessionStorage.getItem('meridian-last-direction')") == "\"forward\""
                    && await core.ExecuteScriptAsync("window.__meridianNativeChrome === true") == "true",
                "Forward bypassed the canvas listener or lost the native-chrome marker.");
            Require(Application.Current.Windows.OfType<MyNetAppWindow>().Count() == windowCount,
                "Canvas history created an extra native app window.");
            Console.WriteLine("MERIDIAN_NATIVE_TOOLS_OK|File+Tools-white|modules+composition|allowlist|420+1120|100+150-percent-render|" + qaDirectory);
        }
        finally
        {
            meridian.Close();
        }
    }

    private static async Task VerifyHyperlibWindowChromeAsync(
        MyNetAppWindow ordinary,
        AtlasMyNetAppDefinition definition,
        SettingsService settings,
        string url)
    {
        var ordinaryCore = ((WebView2)((BrowserTabViewModel)ordinary.DataContext)
            .BrowserView.FindName("Browser")).CoreWebView2;
        Require(await ordinaryCore.ExecuteScriptAsync("window.__hyperlibNativeChrome === true") == "false"
                && ((StackPanel)ordinary.FindName("HyperlibIdentity")).Visibility == Visibility.Collapsed
                && ((ToggleButton)ordinary.FindName("HyperlibEditToggle")).Visibility == Visibility.Collapsed,
            "Hyperlib's native chrome replaced an unrelated MyNet app's titlebar.");
        var hyperlibDefinition = definition.Clone();
        hyperlibDefinition.Id = "atlas.mynet.hyperlib";
        hyperlibDefinition.DisplayName = "HyperLib";
        // Existing saved Hyperlib registrations can still request browser chrome.
        hyperlibDefinition.ShowNavigationToolbar = true;
        var app = CreateHiddenWindow(
            new AtlasMyNetOpenAppRequest(hyperlibDefinition, url), settings);
        try
        {
            var model = (BrowserTabViewModel)app.DataContext;
            var browser = (WebView2)model.BrowserView.FindName("Browser");
            await WaitForPageAsync(model, browser, url);
            var core = browser.CoreWebView2;
            var toggle = (ToggleButton)app.FindName("HyperlibEditToggle");
            var back = (Button)app.FindName("CanvasBackButton");
            var forward = (Button)app.FindName("CanvasForwardButton");
            var navigation = (StackPanel)app.FindName("MeridianNavigation");
            var identity = (StackPanel)app.FindName("HyperlibIdentity");
            await WaitUntilAsync(() => toggle.IsEnabled,
                "Hyperlib's initial editable page did not enable its native Edit Mode toggle.");
            Require(core.Settings.IsWebMessageEnabled
                    && !model.ShowNavigationToolbar
                    && ((Border)model.BrowserView.FindName("NavigationToolbar")).Visibility == Visibility.Collapsed
                    && !((TextBox)model.BrowserView.FindName("AddressTextBox")).IsVisible,
                "A saved browser-toolbar preference restored Hyperlib's address bar.");
            Require(await core.ExecuteScriptAsync("window.hyperlibMarkerAtFirstScript === true") == "true"
                    && await core.ExecuteScriptAsync("window.hyperlibClassAtFirstScript === true") == "true"
                    && await core.ExecuteScriptAsync("window.__meridianNativeChrome === true") == "false",
                "Hyperlib's native marker/class was late or received Meridian's command bridge.");
            Require(((Grid)app.FindName("AppIdentity")).Visibility == Visibility.Collapsed
                    && ((TextBlock)app.FindName("PrivacyIndicator")).Visibility == Visibility.Collapsed
                    && ((ColumnDefinition)app.FindName("PrivacyColumn")).Width.Value == 0
                    && ((Button)app.FindName("FileMenuButton")).Visibility == Visibility.Collapsed
                    && ((Button)app.FindName("ToolsMenuButton")).Visibility == Visibility.Collapsed
                    && navigation.Visibility == Visibility.Visible
                    && identity.Visibility == Visibility.Visible
                    && toggle.Visibility == Visibility.Visible,
                "Hyperlib's single native bar retains duplicate identity, privacy or Meridian menu controls.");
            Require(AutomationProperties.GetName(toggle) == "Edit Mode"
                    && toggle.Focusable && toggle.IsTabStop
                    && WindowChrome.GetIsHitTestVisibleInChrome(toggle)
                    && WindowChrome.GetIsHitTestVisibleInChrome(back)
                    && WindowChrome.GetIsHitTestVisibleInChrome(forward),
                "Native Hyperlib controls cannot be identified, focused or clicked in window chrome.");
            Require(!back.IsEnabled && !forward.IsEnabled && toggle.IsChecked == false,
                "Hyperlib's initial history or edit state is inconsistent with its document.");

            var qaDirectory = Environment.GetEnvironmentVariable("HYPERSHELL_HYPERLIB_CHROME_QA")
                ?? Path.Combine(AppContext.BaseDirectory, "hyperlib-chrome-qa");
            Directory.CreateDirectory(qaDirectory);
            foreach (var width in new[] { 420d, 1120d })
            {
                app.Width = width;
                app.Height = width == 420 ? 420 : 700;
                await FlushLayoutAsync(app);
                var visual = (FrameworkElement)app.Content;
                var minimize = Descendants<Button>(app).Single(button =>
                    AutomationProperties.GetName(button) == "Minimize");
                var maximize = (Button)app.FindName("MaximizeButton");
                var close = Descendants<Button>(app).Single(button =>
                    AutomationProperties.GetName(button) == "Close");
                var ordered = new FrameworkElement[] { back, forward, identity, toggle, minimize, maximize, close };
                var previousRight = 0d;
                foreach (var element in ordered)
                {
                    var bounds = element.TransformToAncestor(visual)
                        .TransformBounds(new Rect(element.RenderSize));
                    Require(element.IsVisible && bounds.Width > 0 && bounds.Height > 0
                            && bounds.Left >= previousRight - 0.5
                            && bounds.Right <= visual.ActualWidth + 0.5
                            && bounds.Top >= 0 && bounds.Bottom <= 38,
                        $"The {width}px Hyperlib bar overlaps, clips or reorders {element.GetType().Name}.");
                    previousRight = bounds.Right;
                }
                Require(navigation.Parent is Grid header
                        && Math.Abs(header.ActualHeight - 36) < 0.5,
                    "Hyperlib's native header no longer fits its compact 36px bar.");
                var browserBounds = browser.TransformToAncestor(visual)
                    .TransformBounds(new Rect(browser.RenderSize));
                Require(browserBounds.Top >= 36 && browserBounds.Top <= 38
                        && Math.Abs(browserBounds.Bottom - (visual.ActualHeight - 1)) < 0.5
                        && browser.ActualWidth > 400 && browser.ActualHeight > 300,
                    "Hyperlib content does not start immediately below the native bar or fill its window: "
                    + $"width={width}, bounds={browserBounds}, browser={browser.ActualWidth}x{browser.ActualHeight}, "
                    + $"visual={visual.ActualWidth}x{visual.ActualHeight}, window={app.ActualWidth}x{app.ActualHeight}.");
                Require(await core.ExecuteScriptAsync(
                        "getComputedStyle(document.getElementById('hyperlib-web-header')).display === 'none'"
                        + " && document.querySelector('main').getBoundingClientRect().top === 0") == "true",
                    "The native marker left the fixture's duplicate web header or vertical gap visible.");
                await SaveHyperlibScreenshotAsync(app, browser, qaDirectory,
                    $"hyperlib-native-{width:0}.png", 1);
                if (width == 420)
                {
                    await SaveHyperlibScreenshotAsync(app, browser, qaDirectory,
                        "hyperlib-native-420-150pct.png", 1.5);
                }
            }

            toggle.IsChecked = true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await WaitForScriptAsync(core, "JSON.stringify(window.hyperlibEditCommands)", "\"[true]\"",
                "The native Edit Mode toggle did not send its enabled state to the document.");
            await core.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:false,canEdit:true});");
            await WaitUntilAsync(() => toggle.IsChecked == false && toggle.IsEnabled,
                "A document edit-state change did not synchronize the native toggle.");
            await core.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:true,canEdit:false});");
            await WaitUntilAsync(() => toggle.IsChecked == true && !toggle.IsEnabled,
                "A read-only document did not disable the native Edit Mode toggle.");
            await core.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:false,canEdit:true});");
            await WaitUntilAsync(() => toggle.IsChecked == false && toggle.IsEnabled,
                "Returning to an editable document did not restore the native toggle.");
            await core.ExecuteScriptAsync("for (const value of [null, 'hyperlib:chrome-state',"
                + "{type:'unrelated:state',editing:true,canEdit:false},"
                + "{type:'hyperlib:chrome-state',editing:'true',canEdit:false},"
                + "{type:'hyperlib:chrome-state',editing:true}]) window.chrome.webview.postMessage(value);"
                + "const frame = document.createElement('iframe'); frame.srcdoc = "
                + JsonSerializer.Serialize("<script>window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:true,canEdit:false});</script>")
                + "; document.body.append(frame);");
            await Task.Delay(200);
            Require(toggle.IsChecked == false && toggle.IsEnabled,
                "Malformed or child-frame page messages changed Hyperlib's native edit state.");

            var nextUrl = url + "?hyperlib=next";
            core.Navigate(nextUrl);
            await WaitForPageAsync(model, browser, nextUrl);
            await WaitUntilAsync(() => toggle.IsEnabled && back.IsEnabled && !forward.IsEnabled,
                "Hyperlib history did not enable Back after opening another page.");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForPageAsync(model, browser, url);
            await WaitUntilAsync(() => !back.IsEnabled && forward.IsEnabled && toggle.IsEnabled,
                "Native Hyperlib Back did not restore its previous page and enable Forward.");
            forward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForPageAsync(model, browser, nextUrl);
            await WaitUntilAsync(() => back.IsEnabled && !forward.IsEnabled && toggle.IsEnabled,
                "Native Hyperlib Forward did not restore its next page.");
            Require(await core.ExecuteScriptAsync("window.hyperlibMarkerAtFirstScript === true && window.hyperlibClassAtFirstScript === true") == "true",
                "Hyperlib history navigation lost the early native marker/class.");

            core.Navigate("file:///C:/hyperlib-native-chrome-verifier.html");
            await WaitUntilAsync(() => model.HasError,
                "Hyperlib's local-only navigation policy did not reject a file URL.");
            Require(core.Source == nextUrl && toggle.IsEnabled && toggle.IsChecked == false,
                "A cancelled Hyperlib navigation disabled Edit Mode in the retained document.");

            await using var foreignFixture = new LoopbackPageFixture();
            core.Navigate(foreignFixture.Url);
            await WaitForPageAsync(model, browser, foreignFixture.Url);
            Require(await core.ExecuteScriptAsync("window.__hyperlibNativeChrome === true") == "false",
                "Another loopback origin received the Hyperlib native-chrome marker.");
            await core.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:true,canEdit:true});");
            await Task.Delay(100);
            Require(toggle.IsChecked == false && !toggle.IsEnabled,
                "Another loopback origin changed Hyperlib's native edit state.");
            Console.WriteLine("HYPERLIB_NATIVE_CHROME_OK|36px|420+1120|100+150-percent-render|early-marker+class|edit-event+state|read-only|direct-history|cancelled-navigation|malformed+frame+origin-scope|" + qaDirectory);
        }
        finally
        {
            app.Close();
        }
    }

    private static async Task SaveHyperlibScreenshotAsync(
        Window app, WebView2 browser, string output, string name, double scale)
    {
        var visual = (FrameworkElement)app.Content;
        var native = new RenderTargetBitmap(
            (int)Math.Ceiling(visual.ActualWidth * scale),
            (int)Math.Ceiling(visual.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        native.Render(visual);
        using var pageStream = new MemoryStream();
        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, pageStream);
        pageStream.Position = 0;
        var page = BitmapFrame.Create(pageStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var composite = new DrawingVisual();
        using (var context = composite.RenderOpen())
        {
            context.DrawImage(native, new Rect(visual.RenderSize));
            context.DrawImage(page, browser.TransformToAncestor(visual)
                .TransformBounds(new Rect(browser.RenderSize)));
        }
        var bitmap = new RenderTargetBitmap(native.PixelWidth, native.PixelHeight,
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(composite);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name));
        encoder.Save(stream);
    }

    private static async Task WaitForScriptAsync(
        CoreWebView2 core, string script, string expected, string failure)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        string? actual = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            actual = await core.ExecuteScriptAsync(script);
            if (JsonSerializer.Deserialize<string>(actual) == JsonSerializer.Deserialize<string>(expected))
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new InvalidDataException($"{failure} Expected={expected}; Actual={actual}");
    }

    private static NativeRectangle CurrentMonitorWorkArea(IntPtr windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, 2); // MONITOR_DEFAULTTONEAREST
        Require(monitor != IntPtr.Zero,
            "The isolated app window has no current monitor.");
        var information = new NativeMonitorInformation
        {
            Size = Marshal.SizeOf<NativeMonitorInformation>()
        };
        Require(GetMonitorInfo(monitor, ref information),
            "The isolated app window's monitor work area could not be measured.");
        return information.WorkArea;
    }

    private static void RequireMaximizedClientWithinWorkArea(
        MyNetAppWindow app, string transition)
    {
        var handle = new WindowInteropHelper(app).Handle;
        var workArea = CurrentMonitorWorkArea(handle);
        Require(GetClientRect(handle, out var client),
            "The maximized app client rectangle could not be measured.");
        var topLeft = new NativePoint { X = client.Left, Y = client.Top };
        var bottomRight = new NativePoint { X = client.Right, Y = client.Bottom };
        Require(ClientToScreen(handle, ref topLeft)
                && ClientToScreen(handle, ref bottomRight),
            "The maximized app client corners could not be mapped to physical screen pixels.");
        Require(topLeft.X >= workArea.Left && topLeft.Y >= workArea.Top
                && bottomRight.X <= workArea.Right && bottomRight.Y <= workArea.Bottom,
            $"The app client escaped the monitor work area after {transition}: "
            + $"client=({topLeft.X},{topLeft.Y})..({bottomRight.X},{bottomRight.Y}), "
            + $"work=({workArea.Left},{workArea.Top})..({workArea.Right},{workArea.Bottom}).");
        Require(bottomRight.X > topLeft.X && bottomRight.Y > topLeft.Y,
            "The maximized app client has no usable area.");
    }

    private static void RequireNativeMinimumTrackSize(MyNetAppWindow app, string state)
    {
        var handle = new WindowInteropHelper(app).Handle;
        var source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidDataException("The native minimum-size probe has no HWND source.");
        var transform = source.CompositionTarget?.TransformToDevice
            ?? throw new InvalidDataException("The native minimum-size probe has no DPI transform.");
        var minimum = transform.Transform(new Point(app.MinWidth, app.MinHeight));
        var initial = new NativeMinMaxInformation
        {
            MinimumTrackSize = new NativePoint { X = 1, Y = 1 },
            MaximumTrackSize = new NativePoint { X = 32767, Y = 32767 }
        };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInformation>());
        try
        {
            Marshal.StructureToPtr(initial, memory, false);
            SendMessage(handle, 0x0024, IntPtr.Zero, memory); // WM_GETMINMAXINFO
            var result = Marshal.PtrToStructure<NativeMinMaxInformation>(memory);
            Require(result.MinimumTrackSize.X >= Math.Ceiling(minimum.X - 0.01)
                    && result.MinimumTrackSize.Y >= Math.Ceiling(minimum.Y - 0.01),
                $"Native minimum tracking bypassed the app's MinWidth/MinHeight in {state}: "
                + $"actual=({result.MinimumTrackSize.X},{result.MinimumTrackSize.Y}), "
                + $"required=({minimum.X},{minimum.Y}).");
            Require(result.MaximumTrackSize.X >= result.MinimumTrackSize.X
                    && result.MaximumTrackSize.Y >= result.MinimumTrackSize.Y,
                "The work-area correction produced invalid native minimum/maximum tracking bounds.");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static async Task VerifyBottomPanelScrollAsync(
        MyNetAppWindow app, WebView2 browser)
    {
        // A local fixture exercises the same fixed header / scrolling form body
        // structure as Meridian without accessing its server or saved canvases.
        const string script = """
            (() => {
              const panel = document.createElement('aside');
              panel.id = 'mynet-scroll-work-area-fixture';
              panel.style.cssText = 'position:fixed;top:0;right:0;bottom:0;width:min(310px,100%);display:flex;flex-direction:column;background:#171f2a;color:white;font:13px sans-serif;';
              const header = document.createElement('header');
              header.textContent = 'Appearance';
              header.style.cssText = 'flex:none;padding:20px;';
              const body = document.createElement('section');
              body.style.cssText = 'flex:1;min-height:0;overflow:auto;padding:14px 18px 24px;';
              const appendField = (name, parent) => {
                const label = document.createElement('label');
                label.style.cssText = 'display:block;margin:14px 0;';
                label.append(name);
                const input = document.createElement('input');
                input.setAttribute('aria-label', name);
                input.value = '210';
                input.style.cssText = 'display:block;box-sizing:border-box;width:100%;height:35px;margin-top:6px;';
                label.append(input); parent.append(label); return input;
              };
              const fieldCount = Math.ceil(window.innerHeight / 64) + 12;
              for (let index = 0; index < fieldCount; index++) appendField('Appearance field ' + index, body);
              const geometry = document.createElement('div');
              geometry.style.cssText = 'display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:9px;';
              for (const name of ['X', 'Y', 'Width', 'Height']) appendField(name, geometry);
              body.append(geometry); panel.append(header, body); document.body.append(panel);
              body.scrollTop = body.scrollHeight;
              const height = geometry.querySelector('[aria-label="Height"]');
              height.focus({preventScroll:true});
              const bounds = height.getBoundingClientRect(), scrollBounds = body.getBoundingClientRect();
              return {top:bounds.top,bottom:bounds.bottom,right:bounds.right,
                scrollTop:body.scrollTop,scrollHeight:body.scrollHeight,clientHeight:body.clientHeight,
                scrollBottom:scrollBounds.bottom,scrollTopEdge:scrollBounds.top,
                viewportHeight:window.visualViewport?.height ?? window.innerHeight,
                focused:document.activeElement === height};
            })()
            """;
        try
        {
            using var metrics = JsonDocument.Parse(
                await browser.CoreWebView2.ExecuteScriptAsync(script));
            var result = metrics.RootElement;
            var bottom = result.GetProperty("bottom").GetDouble();
            Require(result.GetProperty("scrollHeight").GetDouble()
                    > result.GetProperty("clientHeight").GetDouble() + 100
                    && result.GetProperty("scrollTop").GetDouble() > 100,
                "The bottom-panel fixture did not exercise a long scrolling form.");
            Require(result.GetProperty("top").GetDouble()
                        >= result.GetProperty("scrollTopEdge").GetDouble()
                    && bottom <= result.GetProperty("scrollBottom").GetDouble()
                    && bottom <= result.GetProperty("viewportHeight").GetDouble()
                    && result.GetProperty("focused").GetBoolean(),
                "The Height field at the form's scroll end is clipped or cannot take focus.");
            var physicalBottom = browser.PointToScreen(new Point(
                result.GetProperty("right").GetDouble() * browser.ZoomFactor,
                bottom * browser.ZoomFactor));
            var workArea = CurrentMonitorWorkArea(new WindowInteropHelper(app).Handle);
            Require(physicalBottom.X <= workArea.Right && physicalBottom.Y <= workArea.Bottom,
                "The Height field at the form's scroll end is hidden outside the monitor work area: "
                + $"field=({physicalBottom.X},{physicalBottom.Y}), "
                + $"work=({workArea.Left},{workArea.Top})..({workArea.Right},{workArea.Bottom}).");
        }
        finally
        {
            await browser.CoreWebView2.ExecuteScriptAsync(
                "document.getElementById('mynet-scroll-work-area-fixture')?.remove();");
        }
    }

    private static async Task VerifyIndependentCanvasWindowsAsync(
        MyNetAppWindow first,
        SettingsService settings,
        string url)
    {
        var model = (BrowserTabViewModel)first.DataContext;
        var webView = (WebView2)model.BrowserView.FindName("Browser");
        var core = webView.CoreWebView2;
        var waitingUrl = url + "?window=waiting";
        var canvasUrl = url + "?canvas=popup";
        await core.ExecuteScriptAsync(
            "window.myNetUnsavedDraft = 'retain the library';"
            + $"window.myNetCanvasWindow = window.open({JsonSerializer.Serialize(waitingUrl)}, '_blank');");
        MyNetAppWindow? child = null;
        try
        {
            await WaitUntilAsync(() =>
            {
                child = Application.Current.Windows.OfType<MyNetAppWindow>()
                    .SingleOrDefault(window => window != first && window.AppId == first.AppId);
                return child is not null;
            }, "Opening a canvas did not create an independent MyNet window.");
            var childModel = (BrowserTabViewModel)child!.DataContext;
            var childBrowser = (WebView2)childModel.BrowserView.FindName("Browser");
            await WaitForPageAsync(childModel, childBrowser, waitingUrl);
            Require(!child.ShowInTaskbar && child.Left < -5000 && child.Top < -5000,
                "A verifier popup escaped its isolated offscreen window settings.");
            Require(child.Owner is null && !childModel.ShowNavigationToolbar,
                "A canvas popup lost its independent window or hidden-toolbar setting.");
            Require(childBrowser.CoreWebView2.Profile.IsInPrivateModeEnabled
                    && childBrowser.CoreWebView2.Environment.UserDataFolder
                    == core.Environment.UserDataFolder
                    && Path.GetFullPath(childBrowser.CoreWebView2.Environment.UserDataFolder)
                        .StartsWith(Path.GetFullPath(settings.AtlasMyNetDirectory)
                                    + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "A canvas popup did not retain the app's isolated private browser profile.");
            Require(await core.ExecuteScriptAsync("!!window.myNetCanvasWindow;") == "true",
                "Opening a canvas did not return a usable window handle.");
            await core.ExecuteScriptAsync(
                $"window.myNetCanvasWindow.location.replace({JsonSerializer.Serialize(canvasUrl)});");
            await WaitForPageAsync(childModel, childBrowser, canvasUrl);
            await childBrowser.CoreWebView2.ExecuteScriptAsync("document.title = 'Canvas QA';");
            await WaitUntilAsync(() => child.Title == "Canvas QA · " + ((BrowserTabViewModel)first.DataContext).NavigationPolicy!.DisplayName,
                "A dedicated canvas window did not expose its canvas title and app identity.");
            Require(core.Source == url
                    && (await core.ExecuteScriptAsync("window.myNetUnsavedDraft;")).Contains(
                        "retain the library", StringComparison.Ordinal),
                "Opening or navigating a canvas replaced the library or discarded its draft.");
            child.Close();
            RequireBrowserDisposed(childBrowser);
            child = null;
            Require(first.IsVisible && core.Source == url,
                "Closing a canvas popup also closed or navigated its library window.");
            await core.ExecuteScriptAsync(
                $"window.myNetCancelledWindow = window.open({JsonSerializer.Serialize(waitingUrl)}, '_blank');");
            await WaitUntilAsync(() =>
            {
                child = Application.Current.Windows.OfType<MyNetAppWindow>()
                    .SingleOrDefault(window => window != first && window.AppId == first.AppId);
                return child is not null;
            }, "A second canvas reservation did not create a window.");
            childModel = (BrowserTabViewModel)child!.DataContext;
            childBrowser = (WebView2)childModel.BrowserView.FindName("Browser");
            await WaitForPageAsync(childModel, childBrowser, waitingUrl);
            await core.ExecuteScriptAsync("window.myNetCancelledWindow.close();");
            await WaitUntilAsync(() => !Application.Current.Windows.OfType<MyNetAppWindow>()
                    .Any(window => window != first && window.AppId == first.AppId),
                "Cancelling a reserved canvas did not close its native window.");
            RequireBrowserDisposed(childBrowser);
            child = null;
            Require(first.IsVisible && core.Source == url
                    && (await core.ExecuteScriptAsync("window.myNetUnsavedDraft;")).Contains(
                        "retain the library", StringComparison.Ordinal),
                "Cancelling a canvas reservation disturbed its library window.");
        }
        finally
        {
            child?.Close();
        }
    }

    private static async Task FlushLayoutAsync(FrameworkElement element)
    {
        element.UpdateLayout();
        await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T matched)
            {
                yield return matched;
            }
            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInformation
    {
        public int Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInformation
    {
        public NativePoint Reserved;
        public NativePoint MaximumSize;
        public NativePoint MaximumPosition;
        public NativePoint MinimumTrackSize;
        public NativePoint MaximumTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInformation information);

    private static async Task WaitForPageAsync(
        BrowserTabViewModel model,
        WebView2 webView,
        string expectedUrl)
    {
        try
        {
            await WaitUntilAsync(() => webView.CoreWebView2 is { } core
                                    && core.Source == expectedUrl
                                    && !model.IsLoading
                                    && !model.HasError,
                "The local MyNet page did not load.", TimeSpan.FromSeconds(30));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                "The local MyNet page did not load. "
                + $"Expected={expectedUrl}; CoreSource={webView.CoreWebView2?.Source ?? "<not initialized>"}; "
                + $"ViewSource={webView.Source}; IsLoading={model.IsLoading}; HasError={model.HasError}; "
                + $"Error={model.ErrorMessage}", exception);
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        string message,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!predicate() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
        Require(predicate(), message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static void RequireBrowserDisposed(WebView2 webView)
    {
        try
        {
            Require(webView.CoreWebView2 is null,
                "Closing a MyNet app window did not dispose its browser control.");
        }
        catch (ObjectDisposedException)
        {
            // Current WebView2 releases reject access after the controller closes.
        }
    }

    private sealed class LoopbackPageFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly List<Task> _clientTasks = [];
        private readonly Task _serverTask;

        public LoopbackPageFixture()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _serverTask = ServeAsync();
        }

        public string Url { get; }

        private async Task ServeAsync()
        {
            var body = Encoding.UTF8.GetBytes(
                "<!doctype html><html><head><title>MyNet verifier</title><script>"
                + "window.meridianMarkerAtFirstScript = window.__meridianNativeChrome === true;"
                + "window.hyperlibMarkerAtFirstScript = window.__hyperlibNativeChrome === true;"
                + "window.hyperlibClassAtFirstScript = document.documentElement.classList.contains('hyperlib-native-chrome');"
                + "window.hyperlibEditCommands = [];"
                + "addEventListener('hyperlib:edit-mode', e => { hyperlibEditCommands.push(e.detail.enabled);"
                + "window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:e.detail.enabled,canEdit:true}); });"
                + "addEventListener('DOMContentLoaded', () => { if (window.__hyperlibNativeChrome)"
                + "window.chrome.webview.postMessage({type:'hyperlib:chrome-state',editing:false,canEdit:true}); });"
                + "window.meridianFileCommands = [];"
                + "window.meridianToolCommands = [];"
                + "window.meridianCommandActivations = [];"
                + "addEventListener('meridian:tool-command', e => meridianToolCommands.push(e.detail.command));"
                + "addEventListener('meridian:file-command', e => {"
                + "meridianFileCommands.push(e.detail.command);"
                + "meridianCommandActivations.push({command:e.detail.command,active:navigator.userActivation.isActive});});"
                + "addEventListener('meridian:navigate', e => {"
                + "sessionStorage.setItem('meridian-last-direction', e.detail.direction);"
                + "if (e.detail.direction === 'back') history.back();"
                + "if (e.detail.direction === 'forward') history.forward();});"
                + "</script><style>"
                + "html,body{margin:0;background:#101619;color:#dfe8ec;font:14px 'Segoe UI',sans-serif;}"
                + "#hyperlib-web-header{padding:26px 20px;background:#172125;}"
                + ".hyperlib-native-chrome #hyperlib-web-header{display:none;}"
                + "main{padding:22px;box-sizing:border-box;}h1{font-size:22px;font-weight:500;margin:0 0 12px;}"
                + "p{color:#8ea3ad;line-height:1.6;}aside{border-top:1px solid #28383e;margin-top:24px;padding-top:16px;color:#abcac2;}"
                + "</style></head>"
                + "<body><header id='hyperlib-web-header'>HyperLib · Your living library</header>"
                + "<main><h1>HyperLib native chrome</h1><p>Isolated local verification page</p>"
                + "<aside>Library content starts directly beneath the native bar.</aside></main></body></html>");
            var responseHeader = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
                + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                    _clientTasks.Add(ServeClientAsync(client, responseHeader, body));
                }
            }
            catch (Exception exception) when (_shutdown.IsCancellationRequested
                                             && exception is (OperationCanceledException
                                                 or SocketException
                                                 or ObjectDisposedException))
            {
            }
        }

        private async Task ServeClientAsync(
            TcpClient client,
            byte[] responseHeader,
            byte[] body)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    var request = new byte[16384];
                    var length = 0;
                    while (length < request.Length)
                    {
                        var count = await stream.ReadAsync(
                            request.AsMemory(length), _shutdown.Token);
                        if (count == 0)
                        {
                            return;
                        }
                        length += count;
                        if (Encoding.ASCII.GetString(request, 0, length)
                            .Contains("\r\n\r\n", StringComparison.Ordinal))
                        {
                            break;
                        }
                    }
                    await stream.WriteAsync(responseHeader, _shutdown.Token);
                    await stream.WriteAsync(body, _shutdown.Token);
                }
                catch (IOException)
                {
                    // A browser may cancel a speculative request or reload.
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();
            await _serverTask;
            await Task.WhenAll(_clientTasks);
            _shutdown.Dispose();
        }
    }
}
