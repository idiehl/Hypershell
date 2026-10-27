using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class MyNetDesktopWindowVerifier
{
    public static Task VerifyAsync(string verifierRoot)
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
                    await VerifyOnDispatcherAsync(verifierRoot);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "MyNet desktop UI verifier" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task VerifyOnDispatcherAsync(string verifierRoot)
    {
        Require(Application.Current is null,
            "Run the desktop UI verifier in its own process.");
        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();
        VerifyEdgeSnapGeometry();
        VerifyTrayRegistration();
        using (var nativeTray = new MyNetDesktopTray())
        {
            Require(nativeTray.IsAvailable && nativeTray.UsesBrandIcon,
                "The native MyNet tray icon did not register with the embedded brand icon.");
            await WaitUntilAsync(() => nativeTray.TryGetIconBounds(out _),
                "Explorer did not expose a valid rectangle for the registered notification icon.");
            Require(!IsWindowVisible(nativeTray.NotificationWindowHandle)
                    && (GetWindowLongPtr(nativeTray.NotificationWindowHandle, -20).ToInt64() & 0x80) != 0,
                "The tray notification HWND must remain a hidden tool window without a taskbar button.");
            var openEvents = 0;
            var toggleEvents = 0;
            nativeTray.OpenRequested += (_, _) => openEvents++;
            nativeTray.ToggleRequested += (_, _) => toggleEvents++;
            // Deliver real shell callback messages through the hidden HWND's hook.
            SendMessage(nativeTray.NotificationWindowHandle, 0x8000 + 51, IntPtr.Zero, new IntPtr((1 << 16) | 0x400));
            SendMessage(nativeTray.NotificationWindowHandle, 0x8000 + 51, IntPtr.Zero, new IntPtr((1 << 16) | 0x401));
            Require(openEvents == 1 && toggleEvents == 1,
                "Native notification callbacks did not raise pointer/keyboard activation events exactly once.");
            nativeTray.Dispose();
            Require(!nativeTray.IsAvailable && !nativeTray.TryGetIconBounds(out _)
                    && nativeTray.NotificationWindowHandle == IntPtr.Zero,
                "The native tray retained registration or its message window after disposal.");
        }
        var output = Path.Combine(verifierRoot, "mynet-desktop-ui");
        Directory.CreateDirectory(output);
        var settings = new SettingsService(Path.Combine(output, "app-data"));
        var stateStore = new MyNetDesktopStore(
            Path.Combine(settings.AtlasMyNetDirectory, "desktop.device.json"));
        var definitions = CreateAppFixtures(settings, output);
        var background = CreateBackgroundFixture(output);
        var state = new MyNetDesktopState
        {
            Width = 360,
            Height = 720,
            Left = 80,
            Top = 90,
            BackgroundMode = MyNetDesktopBackgroundMode.Surface,
            Icons = definitions.Select((app, index) => (app, index)).ToDictionary(
                pair => pair.app.Id,
                pair => new MyNetDesktopIconPosition
                {
                    X = 12 + pair.index % 3 * 100,
                    Y = 12 + pair.index / 3 * 108
                }, StringComparer.OrdinalIgnoreCase)
        };
        stateStore.Save(state);
        MyNetDesktopWindow? window = null;
        try
        {
            var tray = new MyNetDesktopTrayFixture();
            window = new MyNetDesktopWindow(settings, tray);
            Require(!window.ShowInTaskbar,
                "A launcher with an available tray must not create an additional taskbar button.");
            Require(window.Left == 80 && window.Top == 90,
                "Saved desktop location was not restored.");
            window.Left = -10000;
            window.Top = -10000;
            window.Show();
            var viewModel = (AtlasMyNetViewModel)window.DataContext;
            var canvas = (Canvas)window.FindName("IconCanvas");
            await WaitUntilAsync(() => canvas.Children.Count == definitions.Count
                                      && !viewModel.IsRefreshing,
                "Desktop app registrations did not finish rendering.");
            viewModel.SetActive(false);
            await WaitUntilAsync(() => !viewModel.IsRefreshing,
                "Desktop discovery did not settle before UI verification.");
            await FlushLayoutAsync(window);
            VerifyNativeTaskbarEligibility(window, expected: false);
            VerifyNativeEdgeSnapping(window);
            await VerifyMaximizeRestoreAsync(window, tray, stateStore);

            var tiles = canvas.Children.OfType<Button>().ToList();
            Require(tiles.Count == definitions.Count
                    && tiles.Select(tile => ((AtlasMyNetAppItemViewModel)tile.DataContext)
                        .Definition.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == definitions.Count,
                "The desktop duplicated app icons.");
            VerifyTilesInsideViewport(canvas, tiles);
            VerifyTwoLineLabels(tiles);
            VerifyContextMenus(tiles);
            var surface = (Border)window.FindName("DesktopSurface");
            VerifySquareSurface(surface, "surface");
            var controls = (Border)window.FindName("WindowControls");
            Keyboard.ClearFocus();
            await FlushLayoutAsync(window);
            Require(controls.Opacity == 0,
                "Window controls must stay hidden while idle.");
            SaveScreenshot(window, output, "surface-360x720.png");
            foreach (var scale in new[] { 1.25, 1.5, 2.0 })
            {
                VerifyTwoLineLabels(tiles);
                SaveScreenshot(window, output,
                    $"surface-360x720-{scale * 100:0}percent.png", scale);
            }

            var controlButton = VisualDescendants<Button>(controls).First();
            FocusManager.SetFocusedElement(window, controlButton);
            Keyboard.Focus(controlButton);
            await FlushLayoutAsync(window);
            Require(controls.IsKeyboardFocusWithin && controls.Opacity == 1,
                "Keyboard navigation did not reveal the window controls.");
            SaveScreenshot(window, output, "controls-360x720.png");
            Keyboard.Focus(tiles[0]);
            await FlushLayoutAsync(window);
            Require(!controls.IsKeyboardFocusWithin && controls.Opacity == 0,
                "Window controls stayed visible after focus moved to an app.");
            Keyboard.ClearFocus();
            Invoke(window, "SelectIcon", (object?)null);

            Invoke(window, "TransparentBackgroundClick", window, new RoutedEventArgs());
            await FlushLayoutAsync(window);
            Require(surface.Background is SolidColorBrush { Color.A: 1 }
                    && surface.BorderThickness == new Thickness(0)
                    && ((TextBlock)window.FindName("CollectionLabel")).Visibility
                    == Visibility.Collapsed,
                "Transparent desktop mode must expose the desktop and hide its frame.");
            VerifySquareSurface(surface, "transparent");
            SaveScreenshot(window, output, "transparent-360x720.png");

            var liveState = (MyNetDesktopState)GetField(window, "_state")!;
            liveState.BackgroundMode = MyNetDesktopBackgroundMode.Image;
            liveState.BackgroundImagePath = background;
            Invoke(window, "ApplyBackground");
            await FlushLayoutAsync(window);
            Require(surface.Background is ImageBrush { ImageSource: not null },
                "The selected desktop background image did not render.");
            VerifySquareSurface(surface, "image");
            SaveScreenshot(window, output, "image-360x720.png");

            window.Width = 640;
            window.Height = 720;
            Invoke(window, "ArrangeIconsClick", window, new RoutedEventArgs());
            ((Border)window.FindName("StatusToast")).Visibility = Visibility.Collapsed;
            await FlushLayoutAsync(window);
            VerifyTilesInsideViewport(canvas, tiles);
            VerifyTwoLineLabels(tiles);
            VerifyArrangedIconSpacing(tiles);
            SaveScreenshot(window, output, "image-640x720.png");

            var movedApp = viewModel.Apps.Single(app => app.Definition.Id == "verifier.meridian");
            window.MoveIconPosition(movedApp.Definition.Id, new Point(390, 280));
            Invoke(window, "SaveDesktopState");
            var moved = stateStore.Load();
            Require(moved.Icons[movedApp.Definition.Id].X == 390
                    && moved.Icons[movedApp.Definition.Id].Y == 280,
                "Moving an icon did not persist its desktop position.");
            Require(moved.Width == 640 && moved.Height == 720
                    && moved.BackgroundMode == MyNetDesktopBackgroundMode.Image
                    && moved.BackgroundImagePath == background,
                "Desktop size and background were not persisted.");
            await VerifyTrayLifecycleAsync(window, tray);
            window.ExitDesktop();
            Require(tray.DisposeCount == 1, "Explicit Exit must remove the tray exactly once.");
            window = null;

            var restoredTray = new MyNetDesktopTrayFixture();
            window = new MyNetDesktopWindow(settings, restoredTray)
            {
                Left = -10000,
                Top = -10000
            };
            Require(window.Width == 640 && window.Height == 720,
                "Desktop size did not survive reopening.");
            window.Show();
            var restoredCanvas = (Canvas)window.FindName("IconCanvas");
            var restoredModel = (AtlasMyNetViewModel)window.DataContext;
            await WaitUntilAsync(() => restoredCanvas.Children.Count == definitions.Count
                                      && !restoredModel.IsRefreshing,
                "The reopened desktop did not render its apps.");
            restoredModel.SetActive(false);
            await FlushLayoutAsync(window);
            VerifyNativeTaskbarEligibility(window, expected: false);
            var restoredTile = restoredCanvas.Children.OfType<Button>().Single(tile =>
                ((AtlasMyNetAppItemViewModel)tile.DataContext).Definition.Id == movedApp.Definition.Id);
            Require(Canvas.GetLeft(restoredTile) == 390 && Canvas.GetTop(restoredTile) == 280,
                "An icon's saved position did not survive reopening.");
            SaveScreenshot(window, output, "restored-640x720.png");
            window.PrepareForShutdown();
            window.Close();
            Require(restoredTray.DisposeCount == 1 && !window.IsVisible,
                "Session shutdown must close the launcher and remove its tray without hiding instead.");
            window = null;

            await VerifyMaximizedReopenAsync(output);

            Console.WriteLine(
                $"MYNET_DESKTOP_UI_OK|surface|transparent|image|square-corners|two-line-labels|scaled-rendering|edge-snap-geometry|native-edge-snap|native-maximize-restore|button-maximize-restore|maximize-control-state|maximized-work-area|expanded-hide-restore|normal-bounds-persistence|maximized-exit-reopen|compact|wide|controls|context-menu|placement|native-tray|native-taskbar-eligibility|hide-restore|shell-recovery|explicit-exit|session-shutdown|screenshots={output}");
        }
        finally
        {
            window?.PrepareForShutdown();
            window?.Close();
            application.Shutdown();
        }
    }

    private static void VerifyEdgeSnapGeometry()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        Check(new Rect(9, 160, 360, 720), workArea, new Rect(0, 160, 360, 720), "left");
        Check(new Rect(1551, 160, 360, 720), workArea, new Rect(1560, 160, 360, 720), "right");
        Check(new Rect(400, 8, 360, 720), workArea, new Rect(400, 0, 360, 720), "top");
        Check(new Rect(400, 311, 360, 720), workArea, new Rect(400, 320, 360, 720), "bottom taskbar edge");
        Check(new Rect(1552, 311, 360, 720), workArea, new Rect(1560, 320, 360, 720), "bottom-right corner");
        Check(new Rect(-5, -8, 360, 720), workArea, new Rect(0, 0, 360, 720), "top-left overshoot");
        Check(new Rect(1565, 328, 360, 720), workArea, new Rect(1560, 320, 360, 720), "bottom-right overshoot");
        Check(new Rect(12, 12, 360, 720), workArea, new Rect(0, 0, 360, 720), "inclusive threshold");
        Check(new Rect(13, 13, 360, 720), workArea, new Rect(13, 13, 360, 720), "outside threshold");
        Check(new Rect(-1913, 48, 360, 720), new Rect(-1920, 40, 1920, 1000),
            new Rect(-1920, 40, 360, 720), "negative-coordinate monitor with top taskbar");
        Check(new Rect(54, 160, 360, 720), new Rect(48, 0, 1872, 1080),
            new Rect(48, 160, 360, 720), "left taskbar inset");
        Check(new Rect(3, 160, 1910, 720), workArea, new Rect(0, 160, 1910, 720), "large window nearest left edge");
        Check(new Rect(8, 160, 1910, 720), workArea, new Rect(10, 160, 1910, 720), "large window nearest right edge");
        Check(new Rect(160, 3, 360, 1030), workArea, new Rect(160, 0, 360, 1030), "large window nearest top edge");
        Check(new Rect(160, 8, 360, 1030), workArea, new Rect(160, 10, 360, 1030), "large window nearest bottom edge");
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var scaledWork = new Rect(-1920 * scale, 40 * scale, 1920 * scale, 1000 * scale);
            var bounds = new Rect(scaledWork.Left + 10 * scale, scaledWork.Top + 10 * scale,
                360 * scale, 720 * scale);
            var snapped = Snap(bounds, scaledWork, 12 * scale);
            Require(snapped == new Rect(scaledWork.Location, bounds.Size),
                $"Edge snap did not preserve screen-pixel geometry at {scale * 100:0}% scale.");
        }

        static void Check(Rect bounds, Rect work, Rect expected, string scenario) =>
            Require(Snap(bounds, work, 12) == expected,
                $"Edge snap failed for {scenario}; expected {expected}, received {Snap(bounds, work, 12)}.");
    }

    private static Rect Snap(Rect bounds, Rect workArea, double threshold) =>
        (Rect)(typeof(MyNetDesktopWindow).GetMethod("SnapToWorkArea", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidDataException("Missing desktop edge-snap geometry helper."))
        .Invoke(null, [bounds, workArea, threshold])!;

    private static void VerifyNativeEdgeSnapping(MyNetDesktopWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        Require(GetMonitorInfo(MonitorFromWindow(handle, 2), ref info),
            "Cannot inspect the fixture monitor's native work area.");
        var originalLocation = new Point(window.Left, window.Top);
        var width = Math.Min(360, (info.Work.Right - info.Work.Left) / 2);
        var height = Math.Min(720, (info.Work.Bottom - info.Work.Top) / 2);
        var proposed = new NativeRect
        {
            Left = info.Work.Left + 3, Top = info.Work.Top + 5,
            Right = info.Work.Left + 3 + width, Bottom = info.Work.Top + 5 + height
        };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(proposed, buffer, false);
            var result = SendMessage(handle, 0x0216, IntPtr.Zero, buffer); // WM_MOVING
            var snapped = Marshal.PtrToStructure<NativeRect>(buffer);
            Require(result == new IntPtr(1)
                    && snapped.Left == info.Work.Left && snapped.Top == info.Work.Top
                    && snapped.Right - snapped.Left == width && snapped.Bottom - snapped.Top == height,
                "The native WM_MOVING hook did not snap the proposed rectangle to the monitor's work-area corner.");

            var threshold = (int)Math.Round(12 * GetDpiForWindow(handle) / 96.0);
            proposed.Left = info.Work.Left + threshold + 1;
            proposed.Top = info.Work.Top + threshold + 1;
            proposed.Right = proposed.Left + width;
            proposed.Bottom = proposed.Top + height;
            Marshal.StructureToPtr(proposed, buffer, false);
            SendMessage(handle, 0x0216, IntPtr.Zero, buffer);
            var unsnapped = Marshal.PtrToStructure<NativeRect>(buffer);
            Require(unsnapped.Left == proposed.Left && unsnapped.Top == proposed.Top
                    && unsnapped.Right == proposed.Right && unsnapped.Bottom == proposed.Bottom,
                "The native WM_MOVING hook snapped outside its DPI-scaled threshold.");
            Require(new Point(window.Left, window.Top) == originalLocation,
                "Synthetic moving-message verification unexpectedly moved the fixture window.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void VerifySquareSurface(Border surface, string mode) =>
        Require(surface.CornerRadius == new CornerRadius(0),
            $"The {mode} desktop surface must have square corners to meet screen edges without gaps.");

    private static async Task VerifyMaximizeRestoreAsync(
        MyNetDesktopWindow window,
        MyNetDesktopTrayFixture tray,
        MyNetDesktopStore store)
    {
        var original = new Rect(window.Left, window.Top, window.Width, window.Height);
        var originalOpacity = window.Opacity;
        try
        {
            // Exercise the real native maximize state on an invisible fixture near
            // the work area. Windows may otherwise relocate an off-screen restore
            // rectangle, which would obscure whether the button restores correctly.
            window.Opacity = 0;
            var workArea = SystemParameters.WorkArea;
            window.Left = workArea.Left + 40;
            window.Top = workArea.Top + 40;
            window.Width = 360;
            window.Height = 480;
            await FlushLayoutAsync(window);
            var normal = CurrentBounds(window);
            Invoke(window, "SaveDesktopState");
            VerifySavedNormalBounds(store, normal, "initial normal placement");

            for (var cycle = 1; cycle <= 2; cycle++)
            {
                window.WindowState = WindowState.Maximized;
                await FlushLayoutAsync(window);
                Require(window.WindowState == WindowState.Maximized,
                    "The regression fixture did not enter the native Windows maximized state.");
                if (cycle > 1)
                {
                    VerifyMaximizeControl(window, maximized: true);
                    VerifyNativeMaximizedWorkArea(window);
                }
                Invoke(window, "SaveDesktopState");
                VerifySavedNormalBounds(store, normal, $"native maximize cycle {cycle}");
                Invoke(window, "MaximizeClick", window, new RoutedEventArgs());
                await FlushLayoutAsync(window);
                VerifyRestoredBounds(window, normal, $"native maximize cycle {cycle}");
                Invoke(window, "SaveDesktopState");
                VerifySavedNormalBounds(store, normal, $"native restore cycle {cycle}");
            }

            for (var cycle = 1; cycle <= 2; cycle++)
            {
                Invoke(window, "MaximizeClick", window, new RoutedEventArgs());
                await FlushLayoutAsync(window);
                var expanded = CurrentBounds(window);
                Require(expanded.Width > normal.Width && expanded.Height > normal.Height,
                    $"Maximize button cycle {cycle} did not expand the desktop panel.");
                VerifyMaximizeControl(window, maximized: true);
                VerifyNativeMaximizedWorkArea(window);
                Invoke(window, "SaveDesktopState");
                VerifySavedNormalBounds(store, normal, $"button maximize cycle {cycle}");
                if (cycle == 1)
                {
                    tray.RequestToggle();
                    Require(!window.IsVisible,
                        "The expanded desktop did not hide through its tray toggle.");
                    VerifySavedNormalBounds(store, normal, "hide while expanded");
                    tray.RequestOpen();
                    await FlushLayoutAsync(window);
                    Require(window.IsVisible && BoundsMatch(CurrentBounds(window), expanded),
                        "Opening the hidden expanded desktop lost its expanded bounds.");
                    VerifyMaximizeControl(window, maximized: true);
                    VerifyNativeMaximizedWorkArea(window);
                    Invoke(window, "SaveDesktopState");
                    VerifySavedNormalBounds(store, normal, "reopen while expanded");
                }
                Invoke(window, "MaximizeClick", window, new RoutedEventArgs());
                await FlushLayoutAsync(window);
                VerifyRestoredBounds(window, normal, $"button maximize cycle {cycle}");
                Invoke(window, "SaveDesktopState");
                VerifySavedNormalBounds(store, normal, $"button restore cycle {cycle}");
            }
        }
        finally
        {
            window.WindowState = WindowState.Normal;
            window.Left = original.Left;
            window.Top = original.Top;
            window.Width = original.Width;
            window.Height = original.Height;
            window.Opacity = originalOpacity;
            ((AtlasMyNetViewModel)window.DataContext).SetActive(false);
            await FlushLayoutAsync(window);
        }
    }

    private static Rect CurrentBounds(Window window) =>
        new(window.Left, window.Top, window.ActualWidth, window.ActualHeight);

    private static bool BoundsMatch(Rect actual, Rect expected) =>
        Math.Abs(actual.Left - expected.Left) <= 1
        && Math.Abs(actual.Top - expected.Top) <= 1
        && Math.Abs(actual.Width - expected.Width) <= 1
        && Math.Abs(actual.Height - expected.Height) <= 1;

    private static void VerifyRestoredBounds(Window window, Rect normal, string scenario)
    {
        Require(window.WindowState == WindowState.Normal && BoundsMatch(CurrentBounds(window), normal),
            $"The maximize/restore button failed after {scenario}: state={window.WindowState}, "
            + $"bounds={CurrentBounds(window)}, expected normal bounds={normal}.");
        VerifyMaximizeControl(window, maximized: false);
    }

    private static void VerifyMaximizeControl(Window window, bool maximized)
    {
        var button = window.FindName("MaximizeButton") as Button
            ?? throw new InvalidDataException("The desktop maximize/restore button has no MaximizeButton name.");
        var expectedGlyph = maximized ? "\uE923" : "\uE922";
        var expectedLabel = maximized ? "Restore MyNet" : "Maximize MyNet";
        Require(button.Content?.ToString() == expectedGlyph
                && button.ToolTip?.ToString() == expectedLabel
                && AutomationProperties.GetName(button) == expectedLabel,
            $"The maximize/restore glyph, tooltip, and accessible name do not match the {(maximized ? "maximized" : "normal")} state.");
    }

    private static void VerifyNativeMaximizedWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        Require(window.WindowState == WindowState.Maximized
                && GetMonitorInfo(MonitorFromWindow(handle, 2), ref info),
            "Cannot inspect the maximized fixture's monitor work area.");
        Require(GetWindowRect(handle, out var bounds),
            "Cannot inspect the maximized fixture's native window bounds.");
        Require(Math.Abs(bounds.Left - info.Work.Left) <= 1
                && Math.Abs(bounds.Top - info.Work.Top) <= 1
                && Math.Abs(bounds.Right - info.Work.Right) <= 1
                && Math.Abs(bounds.Bottom - info.Work.Bottom) <= 1,
            "The maximized desktop's native window bounds do not exactly fill its monitor work area: "
            + $"actual=({bounds.Left},{bounds.Top})-({bounds.Right},{bounds.Bottom}), "
            + $"work=({info.Work.Left},{info.Work.Top})-({info.Work.Right},{info.Work.Bottom}).");
    }

    private static async Task VerifyMaximizedReopenAsync(string output)
    {
        var settings = new SettingsService(Path.Combine(output, "maximize-reopen-app-data"));
        var store = new MyNetDesktopStore(Path.Combine(settings.AtlasMyNetDirectory, "desktop.device.json"));
        var area = SystemParameters.WorkArea;
        store.Save(new MyNetDesktopState { Left = area.Left + 40, Top = area.Top + 40, Width = 360, Height = 480 });
        MyNetDesktopWindow? fixture = null;
        try
        {
            var tray = new MyNetDesktopTrayFixture();
            fixture = new MyNetDesktopWindow(settings, tray) { Opacity = 0 };
            fixture.Show();
            await WaitForFixtureAsync(fixture);
            var normal = CurrentBounds(fixture);
            Invoke(fixture, "SaveDesktopState");
            Invoke(fixture, "MaximizeClick", fixture, new RoutedEventArgs());
            await FlushLayoutAsync(fixture);
            VerifyNativeMaximizedWorkArea(fixture);
            fixture.ExitDesktop();
            Require(tray.DisposeCount == 1 && !fixture.IsVisible,
                "Exiting the maximized fixture did not close it and dispose its tray.");
            fixture = null;
            VerifySavedNormalBounds(store, normal, "explicit exit while maximized");

            fixture = new MyNetDesktopWindow(settings, new MyNetDesktopTrayFixture()) { Opacity = 0 };
            fixture.Show();
            await WaitForFixtureAsync(fixture);
            VerifyRestoredBounds(fixture, normal, "reopen after maximized exit");
            fixture.ExitDesktop();
            fixture = null;
        }
        finally
        {
            fixture?.PrepareForShutdown();
            fixture?.Close();
        }

        static async Task WaitForFixtureAsync(MyNetDesktopWindow fixture)
        {
            var canvas = (Canvas)fixture.FindName("IconCanvas");
            var model = (AtlasMyNetViewModel)fixture.DataContext;
            await WaitUntilAsync(() => canvas.Children.Count > 0 && !model.IsRefreshing,
                "The maximize/reopen fixture did not finish initial discovery.");
            model.SetActive(false);
            await FlushLayoutAsync(fixture);
        }
    }

    private static void VerifySavedNormalBounds(MyNetDesktopStore store, Rect normal, string scenario)
    {
        var saved = store.Load();
        Require(saved.Left is not null && saved.Top is not null,
            $"Desktop persistence lost its normal position after {scenario}.");
        var savedBounds = new Rect(saved.Left!.Value, saved.Top!.Value, saved.Width, saved.Height);
        Require(BoundsMatch(savedBounds, normal),
            $"Desktop persistence lost normal bounds after {scenario}: saved={savedBounds}, expected={normal}.");
    }

    private static void VerifyTrayRegistration()
    {
        var calls = new List<MyNetTrayCommand>();
        var addWorks = true;
        var modifyWorks = false;
        var versionWorks = true;
        var changes = 0;
        using var registration = new MyNetTrayRegistration(command =>
        {
            calls.Add(command);
            return command switch
            {
                MyNetTrayCommand.Add => addWorks,
                MyNetTrayCommand.Modify => modifyWorks,
                MyNetTrayCommand.SetVersion => versionWorks,
                _ => true
            };
        });
        registration.AvailabilityChanged += (_, _) => changes++;
        registration.Register();
        Require(registration.IsAvailable && registration.UsesVersion4
                && calls.SequenceEqual(new[] { MyNetTrayCommand.Add, MyNetTrayCommand.SetVersion }),
            "Tray startup must register once and negotiate keyboard-aware callbacks.");
        calls.Clear();
        registration.Register();
        Require(registration.IsAvailable && changes == 1
                && calls.SequenceEqual(new[] { MyNetTrayCommand.Add, MyNetTrayCommand.SetVersion }),
            "Explorer restart must re-add the icon even when it was previously available.");
        addWorks = false;
        registration.Register();
        Require(!registration.IsAvailable && changes == 2,
            "A failed shell registration must expose unavailability for the fallback taskbar window.");
        modifyWorks = true;
        versionWorks = false;
        registration.Register();
        Require(registration.IsAvailable && !registration.UsesVersion4 && changes == 3,
            "An existing icon and older callback protocol must recover without duplicating the icon.");
        registration.Dispose();
        var disposedCalls = calls.Count;
        registration.Dispose();
        registration.Register();
        Require(!registration.IsAvailable && calls.Count == disposedCalls
                && calls.Count(command => command == MyNetTrayCommand.Delete) == 1,
            "Disposed trays must be removed exactly once and never reappear after a retry.");
    }

    private static async Task VerifyTrayLifecycleAsync(MyNetDesktopWindow window, MyNetDesktopTrayFixture tray)
    {
        var model = window.DataContext;
        window.Close();
        Require(!window.IsVisible && tray.DisposeCount == 0,
            "The window close button must hide the launcher without disposing its tray.");
        tray.RequestOpen();
        Require(window.IsVisible && ReferenceEquals(model, window.DataContext) && !window.ShowInTaskbar,
            "Opening from the tray must restore the same launcher without a taskbar button.");
        await FlushLayoutAsync(window);
        VerifyNativeTaskbarEligibility(window, expected: false);
        window.WindowState = WindowState.Minimized;
        Require(!window.IsVisible, "System minimize must hide the launcher when the tray is available.");
        tray.RequestOpen();
        Require(window.IsVisible && window.WindowState == WindowState.Normal,
            "Tray Open must restore a minimized launcher.");
        await FlushLayoutAsync(window);
        VerifyNativeTaskbarEligibility(window, expected: false);
        tray.RequestToggle();
        Require(!window.IsVisible, "Tray click must hide a visible launcher.");
        tray.SetAvailable(false);
        Require(window.IsVisible && window.ShowInTaskbar,
            "A missing Explorer tray must restore a reachable taskbar window.");
        await FlushLayoutAsync(window);
        VerifyNativeTaskbarEligibility(window, expected: true);
        tray.SetAvailable(true);
        Require(!window.ShowInTaskbar, "Recovered tray registration must remove the fallback taskbar button.");
        await FlushLayoutAsync(window);
        VerifyNativeTaskbarEligibility(window, expected: false);
        using var owned = new WindowScope(new Window
        {
            Owner = window, Left = -10000, Top = -10000, Width = 120, Height = 100,
            ShowInTaskbar = false, Content = new TextBox { Text = "Unfinished registration" }
        });
        owned.Window.Show();
        tray.RequestToggle();
        tray.RequestExit();
        Require(window.IsVisible && owned.Window.IsVisible && tray.DisposeCount == 0,
            "Tray hide/Exit must not strand or discard an open launcher dialog.");
        owned.Window.Close();
        await FlushLayoutAsync(window);
    }

    private static void VerifyNativeTaskbarEligibility(Window window, bool expected)
    {
        // Check the HWND Windows sees as well as the WPF property. A launched app can
        // legitimately have a taskbar button while the separate launcher has none.
        var handle = new WindowInteropHelper(window).Handle;
        Require(handle != IntPtr.Zero && IsWindowVisible(handle),
            "Taskbar verification requires the launcher's visible native window.");
        var extendedStyle = GetWindowLongPtr(handle, -20).ToInt64(); // GWL_EXSTYLE
        var owner = GetWindow(handle, 4); // GW_OWNER
        var eligible = (extendedStyle & 0x80) == 0 // WS_EX_TOOLWINDOW
                       && ((extendedStyle & 0x40000) != 0 || owner == IntPtr.Zero); // WS_EX_APPWINDOW
        Require(eligible == expected,
            $"Launcher native taskbar eligibility was {eligible}, expected {expected}; "
            + $"ShowInTaskbar={window.ShowInTaskbar}, extendedStyle=0x{extendedStyle:X}, owner={owner}.");
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public Window Window { get; } = window;
        public void Dispose() { if (Window.IsVisible) Window.Close(); }
    }

    private static List<AtlasMyNetAppDefinition> CreateAppFixtures(
        SettingsService settings,
        string output)
    {
        var registry = new AtlasMyNetAppStore(settings.AtlasMyNetAppsPath, settings.AtlasMyNetTrustPath);
        var pingLab = registry.Load().Single(app => app.Id == AtlasMyNetAppStore.PingLabAppId);
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("No verifier process path.");
        var meridianIcon = CreateIconFixture(output, "meridian.ico", "AM", Color.FromRgb(38, 156, 162));
        var soundIcon = CreateIconFixture(output, "sound.ico", "S", Color.FromRgb(132, 91, 205));
        var toolsIcon = CreateIconFixture(output, "tools.ico", "T", Color.FromRgb(214, 133, 51));
        var studioIcon = CreateIconFixture(output, "studio.ico", "H", Color.FromRgb(93, 165, 158));
        var corruptIcon = Path.Combine(output, "corrupt.ico");
        File.WriteAllText(corruptIcon, "This is an intentionally invalid image fixture.");
        var apps = new List<AtlasMyNetAppDefinition>
        {
            new() { Id = "verifier.meridian", DisplayName = "Atlas Meridian", IconPath = meridianIcon,
                UrlTemplate = "http://127.0.0.1:61185/", Ports = [61185] },
            new() { Id = "verifier.sound", DisplayName = "Sound Lab", IconPath = soundIcon,
                UrlTemplate = "http://127.0.0.1:61186/", Ports = [61186],
                Launch = new() { Executable = executable, Arguments = ["--ui-fixture-never-launched"] } },
            new() { Id = "verifier.tools", DisplayName = "Local Tools", IconPath = toolsIcon,
                UrlTemplate = "http://127.0.0.1:61187/", Ports = [61187],
                Launch = new() { Executable = executable, Arguments = ["--ui-fixture-never-launched"] } },
            new() { Id = "verifier.review", DisplayName = "Review Needed", IconPath = corruptIcon,
                UrlTemplate = "http://127.0.0.1:61188/", Ports = [61188],
                Launch = new() { Executable = executable, Arguments = ["--ui-fixture-never-launched"] } },
            new() { Id = "verifier.studio", DisplayName = "Hyperpage Studio", IconPath = studioIcon,
                UrlTemplate = "http://127.0.0.1:61189/", Ports = [61189],
                Launch = new() { Executable = executable, Arguments = ["--ui-fixture-never-launched"] } },
            pingLab
        };
        registry.Save(apps);
        registry.Trust(apps.Single(app => app.Id == "verifier.sound"));
        registry.Trust(apps.Single(app => app.Id == "verifier.tools"));
        return apps;
    }

    private static void VerifyContextMenus(List<Button> tiles)
    {
        foreach (var tile in tiles)
        {
            var app = (AtlasMyNetAppItemViewModel)tile.DataContext;
            var menu = tile.ContextMenu ?? throw new InvalidDataException("An icon has no context menu.");
            var items = menu.Items.OfType<MenuItem>().ToDictionary(item => item.Header.ToString()!);
            Require(new[] { "Open", "Start", "Stop…", "Edit…", "Delete app…" }.All(items.ContainsKey),
                "The app context menu is missing a required action.");
            app.SetStatus(AtlasMyNetAppStatus.Stopped, "Stopped", null);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            items["Open"].GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();
            items["Stop…"].GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();
            Require(items["Open"].IsEnabled && !items["Stop…"].IsEnabled
                    && items["Start"].IsEnabled == (app.Definition.Launch is not null)
                    && items["Edit…"].IsEnabled
                    && items["Delete app…"].IsEnabled == app.CanRemove,
                $"Stopped app actions are incorrect for {app.DisplayName}.");
            if (app.Definition.Id == "verifier.tools")
            {
                app.SetStatus(AtlasMyNetAppStatus.Running, "Running", new AtlasMyNetAppRuntime(
                    app.Definition.Id, Environment.ProcessId, Environment.ProcessPath!,
                    DateTimeOffset.UtcNow, 61187, "http://127.0.0.1:61187/", false));
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                items["Stop…"].GetBindingExpression(UIElement.IsEnabledProperty)?.UpdateTarget();
                Require(!items["Start"].IsEnabled && items["Stop…"].IsEnabled,
                    "Running app actions did not update after the runtime changed.");
            }
        }
    }

    private static void VerifyTilesInsideViewport(Canvas canvas, IEnumerable<Button> tiles)
    {
        foreach (var tile in tiles)
        {
            var x = Canvas.GetLeft(tile);
            var y = Canvas.GetTop(tile);
            Require(double.IsFinite(x) && double.IsFinite(y)
                    && x >= 0 && y >= 0
                    && x + tile.ActualWidth <= canvas.ActualWidth + 1
                    && y + tile.ActualHeight <= canvas.ActualHeight + 1,
                "An app icon is clipped outside the desktop canvas.");
        }
    }

    private static void VerifyTwoLineLabels(IEnumerable<Button> tiles)
    {
        var studioSeen = false;
        foreach (var tile in tiles)
        {
            var app = (AtlasMyNetAppItemViewModel)tile.DataContext;
            var label = VisualDescendants<TextBlock>(tile).Single(block => block.Text == app.DisplayName);
            var content = (StackPanel)tile.Content;
            var presenter = VisualDescendants<ContentPresenter>(tile).First();
            Require(tile.ActualHeight == 104 && label.MaxHeight == 32 && label.LineHeight == 16
                    && label.LineStackingStrategy == LineStackingStrategy.BlockLineHeight,
                $"Desktop label sizing changed unexpectedly for {app.DisplayName}.");
            var probe = new TextBlock
            {
                Text = label.Text,
                FontFamily = label.FontFamily,
                FontSize = label.FontSize,
                FontStyle = label.FontStyle,
                FontWeight = label.FontWeight,
                FontStretch = label.FontStretch,
                LineHeight = label.LineHeight,
                LineStackingStrategy = label.LineStackingStrategy,
                TextWrapping = label.TextWrapping,
                TextAlignment = label.TextAlignment
            };
            probe.Measure(new Size(label.ActualWidth, double.PositiveInfinity));
            Require(label.ActualHeight + 0.1 >= probe.DesiredSize.Height,
                $"The full wrapped label for {app.DisplayName} needs {probe.DesiredSize.Height}px, "
                + $"but only {label.ActualHeight}px is visible.");
            var labelBounds = label.TransformToAncestor(tile).TransformBounds(new Rect(label.RenderSize));
            Require(labelBounds.Top >= tile.BorderThickness.Top + tile.Padding.Top - 0.1
                    && labelBounds.Bottom <= tile.ActualHeight - tile.BorderThickness.Bottom - tile.Padding.Bottom + 0.1
                    && content.DesiredSize.Height <= presenter.ActualHeight + 0.1,
                $"The tile or content presenter clips the wrapped label for {app.DisplayName}.");
            if (app.Definition.Id == "verifier.studio")
            {
                studioSeen = true;
                Require(probe.DesiredSize.Height > label.LineHeight
                        && probe.DesiredSize.Height <= label.LineHeight * 2 + 0.1,
                    "The Hyperpage Studio regression fixture must occupy exactly two visible lines.");
            }
        }
        Require(studioSeen, "The two-line Hyperpage Studio regression fixture was not rendered.");
    }

    private static void VerifyArrangedIconSpacing(IEnumerable<Button> tiles)
    {
        var rows = tiles.GroupBy(Canvas.GetTop).OrderBy(group => group.Key).ToList();
        foreach (var pair in rows.Zip(rows.Skip(1)))
        {
            Require(pair.Second.Key - pair.First.Key == 108
                    && pair.First.All(tile => Canvas.GetTop(tile) + tile.ActualHeight <= pair.Second.Key),
                "Taller two-line tiles must keep the existing 108px row spacing without overlap.");
        }
    }

    private static async Task FlushLayoutAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        await Task.Delay(60);
        window.UpdateLayout();
    }

    private static void SaveScreenshot(Window window, string output, string name, double scale = 1)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale),
            (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name));
        encoder.Save(stream);
    }

    private static string CreateIconFixture(string output, string name, string initials, Color color)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new LinearGradientBrush(color,
                    Color.FromRgb(22, 40, 65), 80), new Pen(new SolidColorBrush(
                    Color.FromArgb(200, 208, 238, 249)), 1), new Rect(2, 2, 60, 60), 15, 15);
            var text = new FormattedText(initials, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 22, Brushes.White, 1);
            drawing.DrawText(text, new Point((64 - text.Width) / 2, (64 - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        png.Save(memory);
        var bytes = memory.ToArray();
        var path = Path.Combine(output, name);
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)64); writer.Write((byte)64); writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(bytes.Length); writer.Write(22);
        writer.Write(bytes);
        return path;
    }

    private static string CreateBackgroundFixture(string output)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(27, 57, 81),
                Color.FromRgb(10, 21, 34), 90), null, new Rect(0, 0, 640, 720));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(75, 99, 173, 201)),
                null, new Point(530, 175), 90, 90);
            drawing.DrawGeometry(new SolidColorBrush(Color.FromArgb(110, 44, 102, 124)), null,
                Geometry.Parse("M 0,520 L 180,320 390,555 535,390 640,510 640,720 0,720 Z"));
            drawing.DrawGeometry(new SolidColorBrush(Color.FromArgb(160, 15, 36, 51)), null,
                Geometry.Parse("M 0,635 L 110,490 300,685 450,550 640,645 640,720 0,720 Z"));
        }
        var bitmap = new RenderTargetBitmap(640, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(output, "background-fixture.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static object? GetField(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

    private static void Invoke(object target, string name, params object?[] args) =>
        (target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
         ?? throw new InvalidDataException($"Missing desktop handler {name}.")).Invoke(target, args);

    private static async Task WaitUntilAsync(Func<bool> predicate, string message)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!predicate() && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
        Require(predicate(), message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}

internal sealed class MyNetDesktopTrayFixture : IMyNetDesktopTray
{
    public bool IsAvailable { get; private set; } = true;
    public int DisposeCount { get; private set; }
    public event EventHandler? AvailabilityChanged;
    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? ExitRequested;
    public void RequestOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
    public void RequestToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
    public void RequestExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
    public void SetAvailable(bool value) { IsAvailable = value; AvailabilityChanged?.Invoke(this, EventArgs.Empty); }
    public void Dispose() { DisposeCount++; IsAvailable = false; }
}
