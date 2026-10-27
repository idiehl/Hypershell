using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace Hypershell;

/// <summary>A persistent, independent desktop launcher for the user's local web apps.</summary>
public partial class MyNetDesktopWindow : Window
{
    private const double IconWidth = 88;
    // 60 for the icon and margins, 32 for two text lines, plus tile padding/border.
    private const double IconHeight = 104;
    private const double IconPitchX = 100;
    private const double IconPitchY = 108;
    private const double IconInset = 8;
    private readonly SettingsService _settings;
    private readonly AtlasMyNetViewModel _viewModel;
    private readonly MyNetDesktopStore _desktopStore;
    private readonly MyNetDesktopState _state;
    private readonly Dictionary<string, Button> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _statusTimer;
    private string? _selectedAppId;
    private Button? _dragTile;
    private Point _dragPointerOrigin;
    private Point _dragTileOrigin;
    private bool _dragMoved;
    private bool _isLoaded;
    private bool _isClosed;
    private bool _rebuildPending;
    private bool _persistenceUnavailable;
    private string? _startupWarning;
    private readonly IMyNetDesktopTray _tray;
    private bool _allowClose;
    private bool _trayDisposed;
    private HwndSource? _windowSource;

    public MyNetDesktopWindow(SettingsService settings) : this(settings, null) { }

    internal MyNetDesktopWindow(SettingsService settings, IMyNetDesktopTray? tray)
    {
        _settings = settings;
        _desktopStore = new MyNetDesktopStore(
            Path.Combine(settings.AtlasMyNetDirectory, "desktop.device.json"));
        try
        {
            _state = _desktopStore.Load();
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            // Keep the damaged file intact; a fallback must never replace its contents.
            _state = new MyNetDesktopState();
            _persistenceUnavailable = true;
            _startupWarning = $"Desktop settings could not be loaded. Your saved layout is preserved. {exception.Message}";
        }

        var store = new AtlasMyNetAppStore(settings.AtlasMyNetAppsPath, settings.AtlasMyNetTrustPath);
        var inventory = new WindowsSocketInventoryService();
        var web = new AtlasMyNetWebService();
        var processes = new AtlasMyNetProcessService(store, inventory, web);
        _viewModel = new AtlasMyNetViewModel(store, inventory, web, processes);
        _viewModel.OpenAppRequested += OpenAppRequested;
        _viewModel.Apps.CollectionChanged += AppsCollectionChanged;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += SaveTimerTick;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _statusTimer.Tick += StatusTimerTick;

        InitializeComponent();
        if (LoadImage(Path.Combine(AppContext.BaseDirectory, "Assets", "MyNet", "mynet.ico")) is { } brandIcon)
        {
            Icon = brandIcon;
        }
        DataContext = _viewModel;
        DesktopSurface.ContextMenu = IconCanvas.ContextMenu;
        DesktopSurface.MouseLeftButtonDown += DesktopMouseLeftButtonDown;
        ApplySavedPlacement();
        ApplyBackground();
        _tray = tray ?? new MyNetDesktopTray();
        _tray.AvailabilityChanged += TrayAvailabilityChanged;
        _tray.OpenRequested += TrayOpenRequested;
        _tray.ToggleRequested += TrayToggleRequested;
        _tray.ExitRequested += TrayExitRequested;
        ShowInTaskbar = !_tray.IsAvailable;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoaded) return;
        _isLoaded = true;
        await RunUiActionAsync(async () =>
        {
            _viewModel.SetActive(true);
            await _viewModel.InitializeAsync();
            RebuildIcons();
            if (_startupWarning is not null)
            {
                ShowStatus(_startupWarning, persistent: true);
            }
            else if (_viewModel.Apps.Count == 0)
            {
                ShowStatus(_viewModel.StatusMessage, persistent: true);
            }
        }, "MyNet could not initialize");
    }

    private void ApplySavedPlacement()
    {
        var workArea = SystemParameters.WorkArea;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = Math.Max(MinWidth, SystemParameters.VirtualScreenWidth);
        var virtualHeight = Math.Max(MinHeight, SystemParameters.VirtualScreenHeight);
        Width = Math.Clamp(_state.Width, MinWidth, virtualWidth);
        Height = Math.Clamp(_state.Height, MinHeight, virtualHeight);
        if (!_state.Left.HasValue || !_state.Top.HasValue)
        {
            Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 32));
            Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 32));
            Left = workArea.Right - Width - 16;
            Top = workArea.Top + 16;
        }
        else
        {
            // Retain the complete window and its top-right controls after a monitor is removed.
            Left = Math.Clamp(_state.Left.Value, virtualLeft, virtualLeft + virtualWidth - Width);
            Top = Math.Clamp(_state.Top.Value, virtualTop, virtualTop + virtualHeight - Height);
        }
    }

    private void ApplyBackground()
    {
        TransparentBackgroundItem.IsChecked = _state.BackgroundMode == MyNetDesktopBackgroundMode.Transparent;
        DefaultBackgroundItem.IsChecked = _state.BackgroundMode == MyNetDesktopBackgroundMode.Surface;
        DesktopSurface.BorderThickness = _state.BackgroundMode == MyNetDesktopBackgroundMode.Transparent
            ? new Thickness(0)
            : new Thickness(1);
        DesktopSurface.CornerRadius = new CornerRadius(0);
        CollectionLabel.Visibility = _state.BackgroundMode == MyNetDesktopBackgroundMode.Transparent
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (_state.BackgroundMode == MyNetDesktopBackgroundMode.Transparent)
        {
            // A one-alpha hit surface preserves desktop visibility and reliable background interaction.
            DesktopSurface.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            return;
        }

        if (_state.BackgroundMode == MyNetDesktopBackgroundMode.Image)
        {
            if (LoadImage(_state.BackgroundImagePath) is { } image)
            {
                DesktopSurface.Background = new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                return;
            }

            _startupWarning = "Your background image is unavailable. Choose it again from the background menu.";
            if (_isLoaded)
            {
                ShowStatus(_startupWarning);
            }
        }

        DesktopSurface.Background = new LinearGradientBrush(
            Color.FromRgb(22, 30, 43), Color.FromRgb(11, 16, 25), 80);
    }

    private void AppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (_isClosed || _rebuildPending)
        {
            return;
        }

        _rebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _rebuildPending = false;
            if (!_isClosed)
            {
                RebuildIcons();
            }
        }));
    }

    private void RebuildIcons()
    {
        EndIconDrag();
        IconCanvas.Children.Clear();
        _tiles.Clear();
        var width = Math.Max(IconWidth + IconInset * 2, IconScroll.ActualWidth);
        IconCanvas.Width = width;
        var columns = Math.Max(1, (int)((width - IconInset * 2 + IconPitchX - IconWidth) / IconPitchX));
        var index = 0;
        var addedPosition = false;
        foreach (var item in _viewModel.Apps)
        {
            if (!_state.Icons.TryGetValue(item.Definition.Id, out var position))
            {
                position = new MyNetDesktopIconPosition
                {
                    X = IconInset + index % columns * IconPitchX,
                    Y = IconInset + index / columns * IconPitchY
                };
                // Leave all existing coordinates alone when a newly registered app arrives.
                while (PositionOverlapsIcon(position, item.Definition.Id))
                {
                    index++;
                    position.X = IconInset + index % columns * IconPitchX;
                    position.Y = IconInset + index / columns * IconPitchY;
                }

                _state.Icons[item.Definition.Id] = position;
                addedPosition = true;
            }

            var tile = CreateIconTile(item);
            _tiles[item.Definition.Id] = tile;
            IconCanvas.Children.Add(tile);
            index++;
        }

        LayoutIcons();
        SelectIcon(_selectedAppId);
        EmptyState.Visibility = _viewModel.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CollectionLabel.Text = $"MyNet · {_viewModel.Apps.Count} {(_viewModel.Apps.Count == 1 ? "app" : "apps")}";
        if (addedPosition)
        {
            ScheduleSave();
        }
    }

    private bool PositionOverlapsIcon(MyNetDesktopIconPosition position, string exceptId) =>
        _state.Icons.Any(pair => !string.Equals(pair.Key, exceptId, StringComparison.OrdinalIgnoreCase)
            && _viewModel.Apps.Any(app => string.Equals(app.Definition.Id, pair.Key, StringComparison.OrdinalIgnoreCase))
            && Math.Abs(pair.Value.X - position.X) < IconWidth
            && Math.Abs(pair.Value.Y - position.Y) < IconHeight);

    private Button CreateIconTile(AtlasMyNetAppItemViewModel item)
    {
        var tile = new Button
        {
            DataContext = item,
            Style = (Style)FindResource("DesktopIcon"),
            ToolTip = CreateAppToolTip(item)
        };
        AutomationProperties.SetName(tile, item.DisplayName);
        AutomationProperties.SetHelpText(tile, "Double-click or press Enter to open. Drag to arrange. Right-click to manage.");
        var content = new StackPanel { Width = 78 };
        var icon = new Grid { Width = 54, Height = 54, Margin = new Thickness(0, 3, 0, 3) };
        if (LoadImage(item.Definition.IconPath) is { } source)
        {
            icon.Children.Add(new Image
            {
                Source = source,
                Width = 48,
                Height = 48,
                Stretch = Stretch.Uniform,
                Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.35 }
            });
        }
        else
        {
            icon.Children.Add(new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(Color.FromRgb(71, 119, 173), Color.FromRgb(38, 67, 112), 65),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, 166, 210, 252)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "\uE774",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 25,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        var indicator = new Ellipse
        {
            Width = 9, Height = 9,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2),
            Stroke = new SolidColorBrush(Color.FromRgb(16, 24, 36)),
            StrokeThickness = 1.5
        };
        var indicatorStyle = new Style(typeof(Ellipse));
        indicatorStyle.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        indicatorStyle.Setters.Add(new Setter(Shape.FillProperty, new SolidColorBrush(Color.FromRgb(89, 212, 142))));
        var running = new DataTrigger { Binding = new Binding(nameof(item.IsRunning)), Value = true };
        running.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        indicatorStyle.Triggers.Add(running);
        var starting = new DataTrigger { Binding = new Binding(nameof(item.Status)), Value = AtlasMyNetAppStatus.Starting };
        starting.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        starting.Setters.Add(new Setter(Shape.FillProperty, new SolidColorBrush(Color.FromRgb(242, 184, 82))));
        indicatorStyle.Triggers.Add(starting);
        var error = new DataTrigger { Binding = new Binding(nameof(item.HasError)), Value = true };
        error.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible));
        error.Setters.Add(new Setter(Shape.FillProperty, new SolidColorBrush(Color.FromRgb(255, 125, 125))));
        indicatorStyle.Triggers.Add(error);
        indicator.Style = indicatorStyle;
        icon.Children.Add(indicator);
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            Width = 78,
            MaxHeight = 32,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 }
        });
        tile.Content = content;
        tile.ContextMenu = CreateAppContextMenu(item);
        tile.PreviewMouseLeftButtonDown += IconMouseLeftButtonDown;
        tile.PreviewMouseMove += IconMouseMove;
        tile.PreviewMouseLeftButtonUp += IconMouseLeftButtonUp;
        tile.LostMouseCapture += IconLostMouseCapture;
        tile.PreviewMouseRightButtonDown += (_, _) => SelectIcon(item.Definition.Id);
        tile.KeyDown += IconKeyDown;
        tile.GotKeyboardFocus += (_, _) => SelectIcon(item.Definition.Id);
        return tile;
    }

    private static StackPanel CreateAppToolTip(AtlasMyNetAppItemViewModel item)
    {
        var panel = new StackPanel { DataContext = item, MaxWidth = 300 };
        panel.Children.Add(new TextBlock { Text = item.DisplayName, FontWeight = FontWeights.SemiBold });
        var status = new TextBlock();
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(item.StatusText)));
        panel.Children.Add(status);
        var url = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        url.SetBinding(TextBlock.TextProperty, new Binding(nameof(item.UrlText)));
        panel.Children.Add(url);
        var trust = new TextBlock();
        trust.SetBinding(TextBlock.TextProperty, new Binding(nameof(item.CommandTrustText)));
        panel.Children.Add(trust);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        error.SetBinding(TextBlock.TextProperty, new Binding(nameof(item.ErrorMessage)));
        error.SetBinding(VisibilityProperty, new Binding(nameof(item.HasError)) { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(error);
        return panel;
    }

    private ContextMenu CreateAppContextMenu(AtlasMyNetAppItemViewModel item)
    {
        var menu = new ContextMenu { DataContext = item };
        var open = AddMenuItem(menu, "Open", async () => await OpenAppAsync(item));
        // Open performs the command review itself, so untrusted apps remain discoverable.
        open.SetBinding(IsEnabledProperty, new Binding(nameof(item.Status))
        {
            Converter = new AppNotStartingConverter()
        });
        var start = AddMenuItem(menu, "Start", async () => await StartAppAsync(item));
        start.IsEnabled = item.Definition.Launch is not null && !item.IsRunning && item.Status != AtlasMyNetAppStatus.Starting;
        var stop = AddMenuItem(menu, "Stop…", async () => await StopAppAsync(item));
        stop.SetBinding(IsEnabledProperty, new Binding(nameof(item.CanStop)));
        var trust = AddMenuItem(menu, "Review and trust commands…", () => ConfirmTrust(item));
        trust.SetBinding(VisibilityProperty, new Binding(nameof(item.NeedsTrust)) { Converter = new BooleanToVisibilityConverter() });
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Choose icon…", () => ChooseIcon(item));
        AddMenuItem(menu, "Edit…", () => EditApp(item));
        var delete = AddMenuItem(menu, "Delete app…", () => DeleteApp(item));
        delete.IsEnabled = item.CanRemove;
        if (!item.CanRemove)
        {
            delete.ToolTip = "This app is included with MyNet.";
        }

        menu.Opened += (_, _) =>
        {
            start.IsEnabled = item.Definition.Launch is not null && !item.IsRunning && item.Status != AtlasMyNetAppStatus.Starting;
        };
        return menu;
    }

    private MenuItem AddMenuItem(ContextMenu menu, string text, Action action)
    {
        var entry = new MenuItem { Header = text };
        entry.Click += (_, _) => RunUiAction(action, $"{text.TrimEnd('…')} failed");
        menu.Items.Add(entry);
        return entry;
    }

    private MenuItem AddMenuItem(ContextMenu menu, string text, Func<Task> action)
    {
        var entry = new MenuItem { Header = text };
        entry.Click += async (_, _) => await RunUiActionAsync(action, $"{text.TrimEnd('…')} failed");
        menu.Items.Add(entry);
        return entry;
    }

    private void LayoutIcons()
    {
        if (IconCanvas is null || IconScroll.ActualWidth <= 0)
        {
            return;
        }

        var width = Math.Max(IconWidth + IconInset * 2, IconScroll.ActualWidth);
        IconCanvas.Width = width;
        var bottom = _tiles.Keys.Where(_state.Icons.ContainsKey)
            .Select(id => _state.Icons[id].Y + IconHeight + IconInset)
            .DefaultIfEmpty(0).Max();
        var height = Math.Max(Math.Max(IconHeight + IconInset * 2, IconScroll.ActualHeight), bottom);
        IconCanvas.Height = height;
        foreach (var (id, tile) in _tiles)
        {
            var position = MyNetDesktopStore.ClampIcon(_state.Icons[id], width, height, IconWidth, IconHeight);
            _state.Icons[id] = position;
            Canvas.SetLeft(tile, position.X);
            Canvas.SetTop(tile, position.Y);
        }
    }

    private void SelectIcon(string? id)
    {
        _selectedAppId = id is not null && _tiles.ContainsKey(id) ? id : null;
        foreach (var (tileId, tile) in _tiles)
        {
            var selected = string.Equals(tileId, _selectedAppId, StringComparison.OrdinalIgnoreCase);
            tile.Background = selected ? new SolidColorBrush(Color.FromArgb(64, 90, 139, 197)) : Brushes.Transparent;
            tile.BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(140, 151, 191, 242)) : Brushes.Transparent;
        }
    }

    private async void IconMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: AtlasMyNetAppItemViewModel item } tile)
        {
            return;
        }

        eventArgs.Handled = true;
        EndIconDrag();
        SelectIcon(item.Definition.Id);
        tile.Focus();
        if (eventArgs.ClickCount == 2)
        {
            await RunUiActionAsync(() => OpenAppAsync(item), "Open failed");
            return;
        }

        _dragTile = tile;
        _dragPointerOrigin = eventArgs.GetPosition(IconCanvas);
        _dragTileOrigin = new Point(Canvas.GetLeft(tile), Canvas.GetTop(tile));
        _dragMoved = false;
        tile.CaptureMouse();
    }

    private void IconMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_dragTile is not { DataContext: AtlasMyNetAppItemViewModel item } tile
            || eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pointer = eventArgs.GetPosition(IconCanvas);
        var change = pointer - _dragPointerOrigin;
        if (!_dragMoved && Math.Abs(change.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(change.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragMoved = true;
        MoveIconPosition(item.Definition.Id, new Point(_dragTileOrigin.X + change.X, _dragTileOrigin.Y + change.Y));
        eventArgs.Handled = true;
    }

    internal void MoveIconPosition(string appId, Point destination)
    {
        if (!_tiles.TryGetValue(appId, out var tile))
        {
            return;
        }

        var position = MyNetDesktopStore.ClampIcon(new MyNetDesktopIconPosition
        {
            X = destination.X,
            Y = destination.Y
        }, IconCanvas.Width, IconCanvas.Height, IconWidth, IconHeight);
        _state.Icons[appId] = position;
        Canvas.SetLeft(tile, position.X);
        Canvas.SetTop(tile, position.Y);
        ScheduleSave();
    }

    private void IconMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_dragTile is not null)
        {
            eventArgs.Handled = true;
            EndIconDrag();
        }
    }

    private void IconLostMouseCapture(object sender, MouseEventArgs eventArgs) => EndIconDrag();

    private void EndIconDrag()
    {
        var tile = _dragTile;
        var moved = _dragMoved;
        _dragTile = null;
        _dragMoved = false;
        if (tile?.IsMouseCaptured == true)
        {
            tile.ReleaseMouseCapture();
        }

        if (moved)
        {
            ScheduleSave();
        }
    }

    private async void IconKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: AtlasMyNetAppItemViewModel item })
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Enter:
                eventArgs.Handled = true;
                await RunUiActionAsync(() => OpenAppAsync(item), "Open failed");
                break;
            case Key.F2:
                eventArgs.Handled = true;
                RunUiAction(() => EditApp(item), "Edit failed");
                break;
            case Key.Delete:
                eventArgs.Handled = true;
                RunUiAction(() => DeleteApp(item), "Delete failed");
                break;
        }
    }

    private void DesktopMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.Handled || WindowControls.IsMouseOver || StatusToast.IsMouseOver)
        {
            return;
        }

        SelectIcon(null);
        eventArgs.Handled = true;
        MoveWindow();
    }

    private void MoveGripMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        MoveWindow();
    }

    protected override void OnSourceInitialized(EventArgs eventArgs)
    {
        base.OnSourceInitialized(eventArgs);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(DesktopWindowProc);
    }

    private IntPtr DesktopWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        const int wmMoving = 0x0216;
        if (message == wmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            // Borderless maximization must fill this monitor's usable area, with
            // no hidden resize border or taskbar overlap. Keep coordinates native.
            var currentMonitor = MonitorFromWindow(handle, 2);
            var monitorInfo = new DesktopMonitorInfo { Size = Marshal.SizeOf<DesktopMonitorInfo>() };
            if (currentMonitor != IntPtr.Zero && GetMonitorInfo(currentMonitor, ref monitorInfo))
            {
                var limits = Marshal.PtrToStructure<DesktopMinMaxInfo>(lParam);
                limits.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
                limits.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
                limits.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
                limits.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
                Marshal.StructureToPtr(limits, lParam, false);
                // WPF still applies the existing minimum/maximum resize limits.
            }
            return IntPtr.Zero;
        }

        if (message != wmMoving || lParam == IntPtr.Zero || WindowState != WindowState.Normal)
        {
            return IntPtr.Zero;
        }

        // WM_MOVING and monitor work areas both use native screen coordinates.
        // Adjust the proposed rectangle before Windows moves it, so normal drag
        // release, crossing between monitors, and placement persistence still work.
        var proposed = Marshal.PtrToStructure<DesktopNativeRect>(lParam);
        var monitor = MonitorFromRect(ref proposed, 2); // MONITOR_DEFAULTTONEAREST
        var info = new DesktopMonitorInfo { Size = Marshal.SizeOf<DesktopMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var dpi = GetDpiForWindow(handle);
        var threshold = Math.Round(12 * (dpi == 0 ? 1 : dpi / 96d));
        var snapped = SnapToWorkArea(proposed.ToRect(), info.Work.ToRect(), threshold);
        if (snapped == proposed.ToRect()) return IntPtr.Zero;

        var offsetX = (int)Math.Round(snapped.Left - proposed.Left);
        var offsetY = (int)Math.Round(snapped.Top - proposed.Top);
        proposed.Left += offsetX;
        proposed.Right += offsetX;
        proposed.Top += offsetY;
        proposed.Bottom += offsetY;
        Marshal.StructureToPtr(proposed, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    internal static Rect SnapToWorkArea(Rect bounds, Rect workArea, double threshold)
    {
        static double SnapOffset(double start, double end, double areaStart, double areaEnd, double distance)
        {
            var toStart = areaStart - start;
            var toEnd = areaEnd - end;
            if (Math.Abs(toStart) <= distance && Math.Abs(toStart) <= Math.Abs(toEnd)) return toStart;
            return Math.Abs(toEnd) <= distance ? toEnd : 0;
        }

        bounds.Offset(
            SnapOffset(bounds.Left, bounds.Right, workArea.Left, workArea.Right, threshold),
            SnapOffset(bounds.Top, bounds.Bottom, workArea.Top, workArea.Bottom, threshold));
        return bounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopNativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopMinMaxInfo
    {
        public DesktopNativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopNativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopMonitorInfo
    {
        public int Size;
        public DesktopNativeRect Monitor;
        public DesktopNativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref DesktopNativeRect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref DesktopMonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    private void MoveWindow()
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button can be released between the preview event and DragMove.
        }
    }

    private async Task OpenAppAsync(AtlasMyNetAppItemViewModel item)
    {
        if (item.Status == AtlasMyNetAppStatus.Starting)
        {
            ShowStatus($"{item.DisplayName} is starting…");
            return;
        }

        if (item.Definition.Launch is not null && !item.IsRunning && item.NeedsTrust && !ConfirmTrust(item))
        {
            return;
        }

        await _viewModel.OpenAppAsync(item);
        if (item.HasError)
        {
            ShowError("App could not open", item.ErrorMessage);
        }
    }

    private async Task StartAppAsync(AtlasMyNetAppItemViewModel item)
    {
        if (item.IsRunning || item.Status == AtlasMyNetAppStatus.Starting || item.Definition.Launch is null)
        {
            return;
        }

        if (item.NeedsTrust && !ConfirmTrust(item))
        {
            return;
        }

        await _viewModel.StartAppAsync(item);
        if (item.HasError)
        {
            ShowError("App could not start", item.ErrorMessage);
        }
        else
        {
            ShowStatus($"{item.DisplayName} is running.");
        }
    }

    private async Task StopAppAsync(AtlasMyNetAppItemViewModel item)
    {
        if (item.Runtime is not { } runtime)
        {
            return;
        }

        var review = $"Ask {item.DisplayName} to stop?\n\nPID: {runtime.ProcessId}\nExecutable: {(string.IsNullOrWhiteSpace(runtime.ExecutablePath) ? "Unknown" : runtime.ExecutablePath)}\nStarted: {runtime.StartedAtUtc.ToLocalTime():G}\n\nMyNet will only act if the PID still belongs to the same current-user process.";
        if (MessageBox.Show(this, review, "Stop local app", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _viewModel.RequestAppStopAsync(item);
        if (result.Outcome == AtlasMyNetStopOutcome.NeedsForce
            && !item.Definition.IsNative
            && MessageBox.Show(this,
                $"{result.Message}\n\nForce-stop only PID {runtime.ProcessId}? Child processes will not be stopped.",
                "Force-stop selected process", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            result = _viewModel.ForceStopApp(item);
        }

        ShowStatus(result.Message);
        if (result.Outcome is AtlasMyNetStopOutcome.Failed or AtlasMyNetStopOutcome.Rejected)
        {
            ShowError("App could not stop", result.Message);
        }
    }

    private bool ConfirmTrust(AtlasMyNetAppItemViewModel item)
    {
        if (!item.NeedsTrust)
        {
            return true;
        }

        var review = AtlasMyNetAppStore.CreateCommandReviewText(item.Definition);
        if (MessageBox.Show(this,
                $"Review the exact commands below. Trust applies only to this command fingerprint and will be revoked if it changes.\n\n{review}",
                $"Trust {item.DisplayName}", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return false;
        }

        _viewModel.Trust(item);
        ShowStatus($"Trusted {item.DisplayName}'s current commands.");
        return true;
    }

    private void OpenAppRequested(AtlasMyNetOpenAppRequest request)
    {
        if (request.Definition.IsNative)
        {
            return;
        }

        RunUiAction(() =>
        {
            // App windows have their own lifetime, independent of the launcher.
            new MyNetAppWindow(request, _settings).Show();
            ShowStatus($"Opened {request.Definition.DisplayName}.");
        }, "App window could not open");
    }

    private void AddAppClick(object sender, RoutedEventArgs eventArgs) => RunUiAction(() =>
    {
        var dialog = new AtlasMyNetAppDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Definition is { } definition)
        {
            _viewModel.SaveDefinition(definition);
            ShowStatus($"Added {definition.DisplayName}. Drag its icon to arrange it.");
        }
    }, "App definition was not saved");

    private void EditApp(AtlasMyNetAppItemViewModel item)
    {
        var dialog = new AtlasMyNetAppDialog(item.Definition) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Definition is { } definition)
        {
            _viewModel.SaveDefinition(definition);
            ShowStatus($"Updated {definition.DisplayName}.");
        }
    }

    private void DeleteApp(AtlasMyNetAppItemViewModel item)
    {
        if (!item.CanRemove || MessageBox.Show(this,
                $"Remove {item.DisplayName} from MyNet? This removes its icon and registration. Your app files will not be deleted and its running process will not be stopped.",
                "Delete app registration", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_viewModel.RemoveDefinition(item))
        {
            _state.Icons.Remove(item.Definition.Id);
            ScheduleSave();
            ShowStatus($"Removed {item.DisplayName} from MyNet.");
        }
    }

    private void ChooseIcon(AtlasMyNetAppItemViewModel item)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Choose an icon for {item.DisplayName}",
            Filter = "Icons and images (*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif|Windows icons (*.ico)|*.ico",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (LoadImage(dialog.FileName) is null)
        {
            ShowError("Icon could not load", "Choose a valid ICO, PNG, JPEG, BMP, or GIF image.");
            return;
        }

        var replacement = item.Definition.Clone();
        replacement.IconPath = dialog.FileName;
        _viewModel.SaveDefinition(replacement);
        ShowStatus($"Updated {item.DisplayName}'s icon.");
    }

    private void ChooseBackgroundClick(object sender, RoutedEventArgs eventArgs) => RunUiAction(() =>
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a MyNet desktop background",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (LoadImage(dialog.FileName) is null)
        {
            ShowError("Background could not load", "Choose a valid PNG, JPEG, BMP, or GIF image.");
            return;
        }

        _state.BackgroundImagePath = dialog.FileName;
        _state.BackgroundMode = MyNetDesktopBackgroundMode.Image;
        ApplyBackground();
        ScheduleSave();
    }, "Background was not changed");

    private void TransparentBackgroundClick(object sender, RoutedEventArgs eventArgs)
    {
        _state.BackgroundMode = MyNetDesktopBackgroundMode.Transparent;
        ApplyBackground();
        ScheduleSave();
    }

    private void DefaultBackgroundClick(object sender, RoutedEventArgs eventArgs)
    {
        _state.BackgroundMode = MyNetDesktopBackgroundMode.Surface;
        ApplyBackground();
        ScheduleSave();
    }

    private void ArrangeIconsClick(object sender, RoutedEventArgs eventArgs)
    {
        var columns = Math.Max(1, (int)((IconCanvas.Width - IconInset * 2 + IconPitchX - IconWidth) / IconPitchX));
        var index = 0;
        foreach (var item in _viewModel.Apps)
        {
            _state.Icons[item.Definition.Id] = new MyNetDesktopIconPosition
            {
                X = IconInset + index % columns * IconPitchX,
                Y = IconInset + index / columns * IconPitchY
            };
            index++;
        }

        LayoutIcons();
        ScheduleSave();
        ShowStatus("Icons arranged.");
    }

    private async void RefreshClick(object sender, RoutedEventArgs eventArgs) =>
        await RunUiActionAsync(RefreshAppsAsync, "MyNet refresh failed");

    private async Task RefreshAppsAsync()
    {
        await _viewModel.ReloadDefinitionsAsync();
        RebuildIcons();
        if (_viewModel.StatusMessage.StartsWith("Atlas MyNet refresh failed:", StringComparison.Ordinal))
        {
            ShowStatus(_viewModel.StatusMessage, persistent: true);
        }
        else
        {
            ShowStatus("Apps refreshed.");
        }
    }

    private async void WindowPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.F5)
        {
            eventArgs.Handled = true;
            await RunUiActionAsync(RefreshAppsAsync, "MyNet refresh failed");
        }
        else if (eventArgs.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            eventArgs.Handled = true;
            AddAppClick(sender, eventArgs);
        }
        else if (eventArgs.Key == Key.Escape)
        {
            EndIconDrag();
            SelectIcon(null);
            eventArgs.Handled = true;
        }
    }

    private void MinimizeClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_tray.IsAvailable) HideDesktop(); else WindowState = WindowState.Minimized;
    }

    private void WindowStateChanged(object? sender, EventArgs eventArgs)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore MyNet" : "Maximize MyNet";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore MyNet" : "Maximize MyNet");
        ScheduleSave();
        if (!_allowClose && WindowState == WindowState.Minimized && _tray.IsAvailable) HideDesktop();
    }

    internal void ShowDesktop()
    {
        if (_isClosed || _allowClose) return;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _viewModel.SetActive(true);
        Activate();
        OwnedWindows.OfType<Window>().FirstOrDefault(window => window.IsVisible)?.Activate();
    }

    private void HideDesktop()
    {
        if (_isClosed || _allowClose) return;
        // A tray click must not strand an open registration/trust dialog.
        if (!IsEnabled || OwnedWindows.OfType<Window>().Any(window => window.IsVisible))
        {
            ShowDesktop();
            return;
        }
        EndIconDrag();
        SaveDesktopState();
        Hide();
        _viewModel.SetActive(false);
    }

    internal void ExitDesktop()
    {
        if (_isClosed) return;
        if (!IsEnabled || OwnedWindows.OfType<Window>().Any(window => window.IsVisible))
        {
            ShowDesktop();
            ShowStatus("Finish or close the open MyNet dialog before exiting.");
            return;
        }
        _allowClose = true;
        Close();
    }

    internal void PrepareForShutdown()
    {
        if (_isClosed) return;
        _allowClose = true;
        SaveDesktopState();
        DisposeTray();
    }

    private void TrayAvailabilityChanged(object? sender, EventArgs eventArgs)
    {
        if (_isClosed || _allowClose || _trayDisposed) return;
        ShowInTaskbar = !_tray.IsAvailable;
        // Explorer can be missing or restarted: never leave the launcher unreachable.
        if (!_tray.IsAvailable) ShowDesktop();
    }

    private void TrayOpenRequested(object? sender, EventArgs eventArgs) => ShowDesktop();
    private void TrayToggleRequested(object? sender, EventArgs eventArgs)
    {
        if (IsVisible && WindowState != WindowState.Minimized) HideDesktop(); else ShowDesktop();
    }
    private void TrayExitRequested(object? sender, EventArgs eventArgs) => ExitDesktop();

    private void DisposeTray()
    {
        if (_trayDisposed) return;
        _trayDisposed = true;
        _tray.AvailabilityChanged -= TrayAvailabilityChanged;
        _tray.OpenRequested -= TrayOpenRequested;
        _tray.ToggleRequested -= TrayToggleRequested;
        _tray.ExitRequested -= TrayExitRequested;
        _tray.Dispose();
    }

    private void MaximizeClick(object sender, RoutedEventArgs eventArgs)
    {
        // Use the same state for our button, Windows shortcuts and drag-to-edge
        // maximization. Windows owns the restore rectangle for all three paths.
        CaptureWindowPlacement();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void ResizeHandleDragDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        if (WindowState != WindowState.Normal || sender is not Thumb { Tag: string edge })
        {
            return;
        }

        if (edge.Contains("Left", StringComparison.Ordinal))
        {
            var change = Math.Min(eventArgs.HorizontalChange, Width - MinWidth);
            Width -= change;
            Left += change;
        }
        else if (edge.Contains("Right", StringComparison.Ordinal))
        {
            Width = Math.Max(MinWidth, Width + eventArgs.HorizontalChange);
        }

        if (edge.Contains("Top", StringComparison.Ordinal))
        {
            var change = Math.Min(eventArgs.VerticalChange, Height - MinHeight);
            Height -= change;
            Top += change;
        }
        else if (edge.Contains("Bottom", StringComparison.Ordinal))
        {
            Height = Math.Max(MinHeight, Height + eventArgs.VerticalChange);
        }
    }

    private void WindowPlacementChanged(object? sender, EventArgs eventArgs)
    {
        if (_isLoaded)
        {
            ScheduleSave();
        }
    }

    private void WindowSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (_isLoaded)
        {
            LayoutIcons();
            ScheduleSave();
        }
    }

    private void IconViewportSizeChanged(object sender, SizeChangedEventArgs eventArgs) => LayoutIcons();

    private void CaptureWindowPlacement()
    {
        // RestoreBounds also preserves a resize immediately followed by native
        // maximization, before the delayed save has had a chance to run.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (!bounds.IsEmpty && double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top)
            && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height)
            && bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            _state.Left = bounds.Left;
            _state.Top = bounds.Top;
            _state.Width = bounds.Width;
            _state.Height = bounds.Height;
        }
    }

    private void ScheduleSave()
    {
        if (!_isLoaded || _isClosed || _persistenceUnavailable)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimerTick(object? sender, EventArgs eventArgs)
    {
        _saveTimer.Stop();
        SaveDesktopState();
    }

    private void SaveDesktopState()
    {
        if (_persistenceUnavailable)
        {
            return;
        }

        CaptureWindowPlacement();
        try
        {
            _desktopStore.Save(_state);
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException)
        {
            ShowStatus($"Desktop settings could not be saved: {exception.Message}", persistent: true);
        }
    }

    private void WindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!_allowClose && !_isClosed && _tray.IsAvailable)
        {
            eventArgs.Cancel = true;
            HideDesktop();
            return;
        }
        EndIconDrag();
        SaveDesktopState();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        _windowSource?.RemoveHook(DesktopWindowProc);
        _windowSource = null;
        DisposeTray();
        _isClosed = true;
        _saveTimer.Stop();
        _statusTimer.Stop();
        _saveTimer.Tick -= SaveTimerTick;
        _statusTimer.Tick -= StatusTimerTick;
        _viewModel.Apps.CollectionChanged -= AppsCollectionChanged;
        _viewModel.OpenAppRequested -= OpenAppRequested;
        _viewModel.SetActive(false);
        _viewModel.LeaveSessionAppsRunning();
        _viewModel.Dispose();
        base.OnClosed(eventArgs);
    }

    private void ShowStatus(string message, bool persistent = false)
    {
        if (_isClosed)
        {
            return;
        }

        StatusText.Text = message;
        StatusToast.Visibility = Visibility.Visible;
        _statusTimer.Stop();
        if (!persistent)
        {
            _statusTimer.Start();
        }
    }

    private void StatusTimerTick(object? sender, EventArgs eventArgs)
    {
        _statusTimer.Stop();
        StatusToast.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string title, string message)
    {
        if (_isClosed)
        {
            return;
        }

        ShowStatus(message);
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void RunUiAction(Action action, string errorTitle)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ShowError(errorTitle, exception.Message);
        }
    }

    private async Task RunUiActionAsync(Func<Task> action, string errorTitle)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_isClosed)
        {
        }
        catch (Exception exception)
        {
            ShowError(errorTitle, exception.Message);
        }
    }

    private static BitmapSource? LoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(frame => (long)frame.PixelWidth * frame.PixelHeight).FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or ArgumentException
                                           or FormatException
                                           or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class AppNotStartingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is not AtlasMyNetAppStatus.Starting;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }
}
