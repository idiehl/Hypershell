using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Hypershell.Services;

public sealed class WindowsUserActivityMonitor : IDisposable
{
    private static readonly TimeSpan InputPause =
        TimeSpan.FromMilliseconds(1500);

    private readonly DispatcherTimer _timer;
    private uint _observedInputTick;
    private bool _sawActivitySinceStart;
    private bool _raisedForObservedInput;
    private bool _isDisposed;

    public WindowsUserActivityMonitor()
    {
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            OnTimerTick,
            Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    public event EventHandler<WindowsUserActivityPausedEventArgs>?
        ActivityPaused;

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ResetBaseline();
        _timer.Start();
    }

    public void Stop()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        _sawActivitySinceStart = false;
        _raisedForObservedInput = false;
    }

    public void ResetBaseline()
    {
        if (!TryReadLastInput(out _observedInputTick))
        {
            _observedInputTick = 0;
        }

        _sawActivitySinceStart = false;
        _raisedForObservedInput = false;
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        if (!TryReadLastInput(out var lastInputTick))
        {
            return;
        }

        if (lastInputTick != _observedInputTick)
        {
            _observedInputTick = lastInputTick;
            _sawActivitySinceStart = true;
            _raisedForObservedInput = false;
            return;
        }

        if (!_sawActivitySinceStart || _raisedForObservedInput)
        {
            return;
        }

        var elapsedMilliseconds = unchecked(
            (uint)Environment.TickCount - lastInputTick);
        if (elapsedMilliseconds < InputPause.TotalMilliseconds)
        {
            return;
        }

        _raisedForObservedInput = true;
        ActivityPaused?.Invoke(
            this,
            new WindowsUserActivityPausedEventArgs(
                TimeSpan.FromMilliseconds(elapsedMilliseconds)));
    }

    private static bool TryReadLastInput(out uint tick)
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (GetLastInputInfo(ref info))
        {
            tick = info.Time;
            return true;
        }

        tick = 0;
        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}

public sealed class WindowsUserActivityPausedEventArgs : EventArgs
{
    public WindowsUserActivityPausedEventArgs(TimeSpan idleDuration)
    {
        IdleDuration = idleDuration;
    }

    public TimeSpan IdleDuration { get; }
}
