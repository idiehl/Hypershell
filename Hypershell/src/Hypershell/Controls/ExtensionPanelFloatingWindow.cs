using Hypershell.Profiles;
using Hypershell.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Hypershell.Controls;

public sealed class ExtensionPanelFloatingWindow : Window
{
    private readonly DispatcherTimer _boundsCommitTimer;
    private bool _allowClose;
    private bool _isApplyingPlacement;
    private bool _isCloseRequestPending;

    public ExtensionPanelFloatingWindow(
        ExtensionPanelCandidate candidate,
        ExtensionPanelView panelView,
        PanelPlacementSnapshot placement)
    {
        Candidate = candidate;
        PanelView = panelView;
        Title = $"{candidate.Registration.Contribution.DisplayName} — Hypershell";
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = System.Windows.Media.Brushes.Black;
        Content = panelView;
        _boundsCommitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _boundsCommitTimer.Tick += OnBoundsCommitTimerTick;
        Closing += OnClosing;
        SizeChanged += OnSizeChanged;
        ApplyPlacement(placement);
    }

    public ExtensionPanelCandidate Candidate { get; }
    public ExtensionPanelView PanelView { get; }

    public event EventHandler? CloseRequested;
    public event EventHandler<ExtensionPanelBoundsEventArgs>? BoundsCommitted;

    public void ApplyPlacement(PanelPlacementSnapshot placement)
    {
        var policy = Candidate.Registration.Contribution.Placement;
        _isApplyingPlacement = true;
        try
        {
            MinWidth = policy.MinimumWidth;
            MaxWidth = policy.MaximumWidth;
            MinHeight = policy.MinimumHeight;
            MaxHeight = policy.MaximumHeight;
            Width = Math.Clamp(placement.Width, policy.MinimumWidth, policy.MaximumWidth);
            Height = Math.Clamp(
                placement.Height,
                policy.MinimumHeight,
                policy.MaximumHeight);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    public ExtensionPanelView DetachAndCloseForLayout()
    {
        _allowClose = true;
        _boundsCommitTimer.Stop();
        Content = null;
        Close();
        return PanelView;
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowClose)
        {
            Cleanup();
            return;
        }

        eventArgs.Cancel = true;
        if (_isCloseRequestPending)
        {
            return;
        }

        _isCloseRequestPending = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _isCloseRequestPending = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (_isApplyingPlacement)
        {
            return;
        }

        _boundsCommitTimer.Stop();
        _boundsCommitTimer.Start();
    }

    private void OnBoundsCommitTimerTick(object? sender, EventArgs eventArgs)
    {
        _boundsCommitTimer.Stop();
        BoundsCommitted?.Invoke(
            this,
            new ExtensionPanelBoundsEventArgs(
                ActualWidth,
                ActualHeight));
    }

    private void Cleanup()
    {
        _boundsCommitTimer.Stop();
        _boundsCommitTimer.Tick -= OnBoundsCommitTimerTick;
        Closing -= OnClosing;
        SizeChanged -= OnSizeChanged;
    }
}

public sealed record ExtensionPanelBoundsEventArgs(
    double Width,
    double ContentHeight);
