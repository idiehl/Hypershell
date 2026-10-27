using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Hypershell.Services;

internal interface IMyNetDesktopTray : IDisposable
{
    bool IsAvailable { get; }
    event EventHandler? AvailabilityChanged;
    event EventHandler? OpenRequested;
    event EventHandler? ToggleRequested;
    event EventHandler? ExitRequested;
}

internal enum MyNetTrayCommand : uint { Add = 0, Modify = 1, Delete = 2, SetVersion = 4 }

/// <summary>Registration state is kept separate so shell loss and retry are deterministic to verify.</summary>
internal sealed class MyNetTrayRegistration(Func<MyNetTrayCommand, bool> notify) : IDisposable
{
    private bool _disposed;
    public bool IsAvailable { get; private set; }
    public bool UsesVersion4 { get; private set; }
    public event EventHandler? AvailabilityChanged;

    public void Register()
    {
        if (_disposed) return;
        var available = notify(MyNetTrayCommand.Add) || notify(MyNetTrayCommand.Modify);
        UsesVersion4 = available && notify(MyNetTrayCommand.SetVersion);
        SetAvailable(available);
    }

    private void SetAvailable(bool value)
    {
        if (IsAvailable == value) return;
        IsAvailable = value;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        notify(MyNetTrayCommand.Delete);
        SetAvailable(false);
    }
}

/// <summary>A native notification icon, independent of the launcher's visible HWND and app windows.</summary>
internal sealed class MyNetDesktopTray : IMyNetDesktopTray
{
    private const int CallbackMessage = 0x8000 + 51;
    private const int WmNull = 0, WmContextMenu = 0x7B, WmLeftButtonUp = 0x202, WmRightButtonUp = 0x205;
    private const int NinSelect = 0x400, NinKeySelect = 0x401;
    private readonly HwndSource? _source;
    private readonly DispatcherTimer _retry;
    private readonly MyNetTrayRegistration _registration;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _disposed;

    public bool IsAvailable => _registration.IsAvailable;
    internal bool UsesBrandIcon => _ownsIcon;
    internal IntPtr NotificationWindowHandle => _source is { IsDisposed: false } ? _source.Handle : IntPtr.Zero;
    public event EventHandler? AvailabilityChanged;
    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? ExitRequested;

    public MyNetDesktopTray()
    {
        _registration = new MyNetTrayRegistration(Notify);
        _registration.AvailabilityChanged += RegistrationChanged;
        _retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _retry.Tick += RetryRegistration;
        try
        {
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            // A hidden top-level window receives Explorer's TaskbarCreated broadcast;
            // an HWND_MESSAGE window would not. It has no visible/taskbar styles.
            _source = new HwndSource(new HwndSourceParameters("MyNet notification icon")
            {
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x80,
                Width = 0,
                Height = 0,
                ParentWindow = IntPtr.Zero
            });
            _source.AddHook(WindowMessage);
            _icon = LoadBrandIcon();
            _ownsIcon = _icon != IntPtr.Zero;
            if (_icon == IntPtr.Zero) _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
            Register();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
        {
            // The launcher retains a normal taskbar entry if the shell is unavailable.
            _source?.Dispose();
            if (_ownsIcon) DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            _ownsIcon = false;
        }
    }

    private void Register()
    {
        if (_disposed) return;
        _registration.Register();
        if (IsAvailable) _retry.Stop(); else _retry.Start();
    }

    private void RetryRegistration(object? sender, EventArgs e) => Register();
    private void RegistrationChanged(object? sender, EventArgs e) => AvailabilityChanged?.Invoke(this, e);

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if (_taskbarCreated != 0 && message == _taskbarCreated)
        {
            Register();
        }
        else if (message == CallbackMessage)
        {
            var notification = unchecked((int)lParam.ToInt64()) & 0xFFFF;
            if (notification == NinKeySelect)
                OpenRequested?.Invoke(this, EventArgs.Empty);
            else if (notification == NinSelect || (!_registration.UsesVersion4 && notification == WmLeftButtonUp))
                ToggleRequested?.Invoke(this, EventArgs.Empty);
            else if (notification == WmContextMenu || (!_registration.UsesVersion4 && notification == WmRightButtonUp))
                OpenMenu();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OpenMenu()
    {
        if (_source is null || _disposed) return;
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        uint choice;
        try
        {
            AppendMenu(menu, 0, new UIntPtr(1), "Open MyNet");
            AppendMenu(menu, 0x800, UIntPtr.Zero, null);
            AppendMenu(menu, 0, new UIntPtr(2), "Exit MyNet");
            GetCursorPos(out var point);
            SetForegroundWindow(_source.Handle);
            choice = TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, _source.Handle, IntPtr.Zero);
            PostMessage(_source.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
        if (choice == 1) OpenRequested?.Invoke(this, EventArgs.Empty);
        else if (choice == 2) ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    internal bool TryGetIconBounds(out Rect bounds)
    {
        bounds = Rect.Empty;
        if (_disposed || !IsAvailable) return false;
        var identity = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = NotificationWindowHandle, Id = 1
        };
        if (ShellNotifyIconGetRect(ref identity, out var rect) != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;
        bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    private bool Notify(MyNetTrayCommand command)
    {
        if (_source is null || _source.IsDisposed || _icon == IntPtr.Zero) return false;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1,
            Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = _icon,
            Tip = "MyNet", Info = string.Empty, InfoTitle = string.Empty, Version = 4
        };
        return ShellNotifyIcon((uint)command, ref data);
    }

    private static IntPtr LoadBrandIcon()
    {
        var target = Math.Max(16, GetSystemMetrics(49)); // SM_CXSMICON
        var myNetIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MyNet", "mynet.ico");
        var myNetIcon = LoadImage(IntPtr.Zero, myNetIconPath, 1, target, target, 0x10); // IMAGE_ICON, LR_LOADFROMFILE
        if (myNetIcon != IntPtr.Zero) return myNetIcon;

        try
        {
            using var stream = Application.GetResourceStream(new Uri(
                "pack://application:,,,/Hypershell;component/Assets/Brand/hypershell.ico"))?.Stream;
            if (stream is null) return IntPtr.Zero;
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) return IntPtr.Zero;
            var count = reader.ReadUInt16();
            if (count is 0 or > 256) return IntPtr.Zero;
            var entries = new List<(int Size, uint Length, uint Offset)>();
            for (var index = 0; index < count; index++)
            {
                var width = reader.ReadByte(); reader.ReadByte(); reader.ReadUInt16();
                reader.ReadUInt16(); reader.ReadUInt16();
                entries.Add((width == 0 ? 256 : width, reader.ReadUInt32(), reader.ReadUInt32()));
            }
            foreach (var entry in entries.OrderBy(e => Math.Abs(e.Size - target)))
            {
                if (entry.Length > 4 * 1024 * 1024 || entry.Offset + (long)entry.Length > stream.Length) continue;
                stream.Position = entry.Offset;
                var bytes = reader.ReadBytes((int)entry.Length);
                var icon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, target, target, 0);
                if (icon != IntPtr.Zero) return icon;
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException) { }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retry.Stop();
        _retry.Tick -= RetryRegistration;
        _registration.AvailabilityChanged -= RegistrationChanged;
        _registration.Dispose();
        if (_source is { IsDisposed: false })
        {
            _source.RemoveHook(WindowMessage);
            _source.Dispose();
        }
        if (_ownsIcon) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags;
        public int Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")] private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identity, out NativeRect rect);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint command, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] bytes, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
