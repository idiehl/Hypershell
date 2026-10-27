using Hypershell.Controls;
using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Hypershell;

public partial class MainWindow : Window
{
    private const double CollapsedPaneWidth = 0;
    private const double MinimumCenterPaneWidth = 360;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int WorkspaceAiStopHotKeyId = 0x4853;
    private const int WindowsMessageGetMinMaxInfo = 0x0024;
    private const int WindowsMessageHotKey = 0x0312;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint HotKeyModifierAlt = 0x0001;
    private const uint HotKeyModifierControl = 0x0002;
    private const string ExtensionPanelDragDataFormat =
        "Hypershell.WorkspaceExtensionPanel";
    private static readonly TimeSpan PaneTransitionDuration = TimeSpan.FromMilliseconds(280);

    private readonly MainWindowViewModel _viewModel;
    private readonly WpfWindowComputerControlTarget
        _workspaceAiHypershellControlTarget;
    private readonly WpfWindowComputerControlTarget
        _workspaceAiDesktopControlTarget;
    private readonly Dictionary<RichTextBox, TerminalTranscriptSubscription>
        _terminalTranscriptSubscriptions = [];
    private readonly Dictionary<string, ExtensionPanelView> _extensionPanelViews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExtensionPanelFloatingWindow> _floatingExtensionPanels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DockedExtensionPanelChrome> _dockedExtensionPanels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<INotifyPropertyChanged> _workspaceTabSubscriptions = [];
    private readonly Dictionary<string, VisualPageStudioDetachedWindow>
        _detachedVisualPageWindows = new(StringComparer.Ordinal);
    private WorkspaceAiAssistantViewModel? _subscribedWorkspaceAiAssistant;
    private FileSystemEntry? _contextMenuEntry;
    private SettingsWindow? _settingsWindow;
    private Button? _workspaceCustomizationButton;
    private string _activeBottomPanelId = string.Empty;
    private double _fileExplorerOpenWidth = 300;
    private double _commandBuilderOpenWidth = 418;
    private double _fileExplorerTargetAngle;
    private double _commandBuilderTargetAngle = 180;
    private double _lastWindowClientWidth;
    private WorkspaceLayoutSnapshot? _workspaceCustomizationBaseline;
    private ExtensionPanelCandidate? _activePanelDragCandidate;
    private WorkspacePanelMoveTarget? _pendingPanelMoveTarget;
    private bool _isWorkspaceCustomizationActive;
    private bool _isCloseApproved;
    private bool _isCloseConfirmationPending;
    private bool _isWindowFinalized;
    private HwndSource? _windowMessageSource;
    private bool _isWorkspaceAiStopHotKeyRegistered;
    private CancellationTokenSource? _profileLoginCancellation;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr windowHandle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitorHandle,
        ref NativeMonitorInfo monitorInfo);

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
    private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    private enum DwmWindowCornerPreference
    {
        DoNotRound = 1,
        Round = 2
    }

    private enum WorkspacePanelMoveTarget
    {
        FileExplorer,
        CommandBuilder,
        Bottom,
        Floating
    }

    private sealed record TerminalTranscriptSubscription(
        TerminalTabViewModel Terminal,
        NotifyCollectionChangedEventHandler CollectionHandler,
        PropertyChangedEventHandler SettingsHandler);

    private sealed record DockedPanelScaleTarget(
        WorkspacePanelHost Host,
        Border? BuiltInPane,
        FrameworkElement? BuiltInContent,
        double MinimumWidth,
        double DesiredWidth,
        bool IsExpanded);

    private sealed record DockedExtensionPanelChrome(
        WorkspacePanelHost Host,
        Thumb ResizeThumb);

    public MainWindow() : this(StandaloneStudioMode.None)
    {
    }

    public MainWindow(StandaloneStudioMode standaloneMode)
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(new SettingsService(), standaloneMode);
        if (_viewModel.IsStandaloneStudio)
        {
            // Snap and resize can offer a client area narrower than the general shell's
            // 1100-DIP minimum. Studio adapts to that area instead of laying out off-screen.
            MinWidth = 640;
            MinHeight = 480;
        }
        _workspaceAiHypershellControlTarget =
            new WpfWindowComputerControlTarget(
                this,
                WorkspaceAiComputerControlMode.Hypershell);
        _workspaceAiDesktopControlTarget =
            new WpfWindowComputerControlTarget(
                this,
                WorkspaceAiComputerControlMode.Desktop);
        _viewModel.AttachWorkspaceAiComputerControlTarget(
            _workspaceAiHypershellControlTarget);
        _viewModel.AttachWorkspaceAiComputerControlTarget(
            _workspaceAiDesktopControlTarget);
        FileExplorerPanelHost.Registration = _viewModel.WorkspacePanelRegistry.GetRequired(
            WorkspacePanelRegistry.FileExplorerPanelId);
        CommandBuilderPanelHost.Registration = _viewModel.WorkspacePanelRegistry.GetRequired(
            WorkspacePanelRegistry.CommandBuilderPanelId);
        DataContext = _viewModel;
        ApplyWorkspaceLayout(_viewModel.WorkspaceLayout);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.WorkspaceTabs.CollectionChanged += OnWorkspaceTabsChanged;
        _viewModel.CommandBuilder.EditorInsertionRequested +=
            OnBuilderEditorInsertionRequested;
        _viewModel.VisualPageDetachRequested +=
            OnVisualPageDetachRequested;
        RefreshWorkspaceAiMessageSubscription();
        RefreshWorkspaceTabSubscriptions();
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnWindowStateChanged;
        SizeChanged += OnWindowSizeChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        PreviewMouseDown += OnWindowPreviewMouseDown;
        Deactivated += OnWindowDeactivated;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _lastWindowClientWidth = ActualWidth;
        try
        {
            await _viewModel.InitializeAsync();
            // A release can open the library in a new owned MyNet session without closing another window's drafts.
            if (Environment.GetCommandLineArgs().Contains("--open-hyperlib", StringComparer.Ordinal))
            {
                var hyperlib = _viewModel.AtlasMyNet.Apps.FirstOrDefault(item => item.Definition.Id == HyperlibMyNetRegistration.AppId);
                if (hyperlib is not null) await _viewModel.AtlasMyNet.OpenAppAsync(hyperlib);
            }
            _lastWindowClientWidth = ActualWidth;
        }
        catch (Exception exception)
        {
            if (_viewModel.IsStandaloneStudio)
                _viewModel.ReportStandaloneStartupFailure($"{_viewModel.StandaloneAppName} could not finish startup: {exception.Message}");
            else
                _viewModel.FailProfileLogin($"Hypershell could not finish startup: {exception.Message}");
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_isCloseApproved)
        {
            FinalizeWindowClose();
            return;
        }

        eventArgs.Cancel = true;
        if (_isCloseConfirmationPending)
        {
            return;
        }

        _isCloseConfirmationPending = true;
        var wasEnabled = IsEnabled;
        try
        {
            if (!await ConfirmCloseAllDocumentsAsync())
            {
                return;
            }

            // Keep the saved state stable while background apps finish stopping.
            IsEnabled = false;
            if (!await StopAtlasMyNetAppsForCloseAsync())
            {
                return;
            }

            await _viewModel.PrepareForShutdownAsync();
            _isCloseApproved = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, Close);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Hypershell could not finish closing. Your window remains open.\n\n{exception.Message}",
                "Could not close Hypershell",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (!_isCloseApproved)
            {
                IsEnabled = wasEnabled;
                _viewModel.CancelAtlasMyNetShutdown();
            }
            _isCloseConfirmationPending = false;
        }
    }

    private void FinalizeWindowClose()
    {
        if (_isWindowFinalized)
        {
            return;
        }

        _isWindowFinalized = true;
        TaskbarIdentityService.ClearWindowIdentity(new WindowInteropHelper(this).Handle);
        foreach (var (transcript, subscription) in _terminalTranscriptSubscriptions)
        {
            UnsubscribeTerminalTranscript(subscription);
        }

        _terminalTranscriptSubscriptions.Clear();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.WorkspaceTabs.CollectionChanged -= OnWorkspaceTabsChanged;
        _viewModel.CommandBuilder.EditorInsertionRequested -=
            OnBuilderEditorInsertionRequested;
        _viewModel.VisualPageDetachRequested -=
            OnVisualPageDetachRequested;
        if (_subscribedWorkspaceAiAssistant is not null)
        {
            _subscribedWorkspaceAiAssistant.Messages.CollectionChanged -=
                OnWorkspaceAiMessagesChanged;
            _subscribedWorkspaceAiAssistant = null;
        }
        foreach (var tab in _workspaceTabSubscriptions)
        {
            tab.PropertyChanged -= OnWorkspaceTabPropertyChanged;
        }

        _workspaceTabSubscriptions.Clear();
        foreach (var window in _detachedVisualPageWindows.Values.ToList())
        {
            window.Close();
        }
        _detachedVisualPageWindows.Clear();
        _profileLoginCancellation?.Cancel();
        _profileLoginCancellation?.Dispose();
        _profileLoginCancellation = null;
        DetachWorkspaceAiStopHotKey();
        SourceInitialized -= OnSourceInitialized;
        StateChanged -= OnWindowStateChanged;
        SizeChanged -= OnWindowSizeChanged;
        PreviewKeyDown -= OnWindowPreviewKeyDown;
        PreviewMouseDown -= OnWindowPreviewMouseDown;
        Deactivated -= OnWindowDeactivated;
        DisposeExtensionPanels();
        _viewModel.DetachWorkspaceAiComputerControlTarget(
            _workspaceAiHypershellControlTarget);
        _viewModel.DetachWorkspaceAiComputerControlTarget(
            _workspaceAiDesktopControlTarget);
        _viewModel.Dispose();
    }

    private async void OnWindowPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (Keyboard.Modifiers ==
                (ModifierKeys.Control | ModifierKeys.Alt)
            && eventArgs.Key == Key.Escape)
        {
            _viewModel.WorkspaceAiAssistant.StopComputerControlCommand.Execute(
                null);
            eventArgs.Handled = true;
            return;
        }

        if (_isWorkspaceCustomizationActive
            && eventArgs.Key == Key.Escape)
        {
            CancelWorkspaceCustomization();
            eventArgs.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (eventArgs.Key)
            {
                case Key.N:
                    OpenNewDocumentDialog();
                    eventArgs.Handled = true;
                    return;

                case Key.O:
                    eventArgs.Handled = true;
                    await OpenDocumentsFromDialogAsync();
                    return;

                case Key.S when _viewModel.ActiveDocument is { } document:
                    eventArgs.Handled = true;
                    await TrySaveDocumentAsync(document);
                    return;

                case Key.S when _viewModel.ActiveWorkspaceTab is
                    VisualPageStudioTabViewModel visualPage:
                    eventArgs.Handled = true;
                    await visualPage.SaveProjectAsync();
                    return;

                case Key.F when FindVisualChild<MonacoEditorControl>(this, "DocumentEditor") is { } findEditor:
                    findEditor.ShowFind();
                    eventArgs.Handled = true;
                    return;

                case Key.H when FindVisualChild<MonacoEditorControl>(this, "DocumentEditor") is { } replaceEditor:
                    replaceEditor.ShowReplace();
                    eventArgs.Handled = true;
                    return;

                case Key.G when FindVisualChild<MonacoEditorControl>(this, "DocumentEditor") is { } goToLineEditor:
                    goToLineEditor.ShowGoToLine();
                    eventArgs.Handled = true;
                    return;

                case Key.OemComma:
                    OpenSettingsWindow(0);
                    eventArgs.Handled = true;
                    return;
            }
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
            && eventArgs.Key == Key.S
            && _viewModel.ActiveDocument is { } activeDocument)
        {
            eventArgs.Handled = true;
            await TrySaveDocumentAsync(activeDocument, forceSaveAs: true);
            return;
        }

        if ((Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
             || Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
            && eventArgs.Key == Key.F
            && FindVisualChild<MonacoEditorControl>(this, "DocumentEditor") is { } formatEditor)
        {
            eventArgs.Handled = true;
            if (_viewModel.ActiveDocument is { } formatDocument)
            {
                await TryFormatDocumentAsync(formatDocument, formatEditor);
            }
        }
    }

    private void OnWindowPreviewMouseDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (!_viewModel.CommandBuilder.IsFlagsExpanded
            || eventArgs.OriginalSource is not DependencyObject source
            || IsDescendantOrSelf(source, FlagsDropDownToggle)
            || FlagsDropDownPopup.Child is DependencyObject popupContent
                && IsDescendantOrSelf(source, popupContent))
        {
            return;
        }

        _viewModel.CommandBuilder.IsFlagsExpanded = false;
    }

    private void OnWindowDeactivated(object? sender, EventArgs eventArgs)
    {
        _viewModel.CommandBuilder.IsFlagsExpanded = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(MainWindowViewModel.IsFileExplorerOpen):
                AnimatePane(
                    FileExplorerPane,
                    _viewModel.IsFileExplorerOpen
                        ? GetActiveFileExplorerWidth()
                        : CollapsedPaneWidth,
                    FileExplorerToggleRotation,
                    _fileExplorerTargetAngle);
                RefreshDockedPanelResizeThumbs();
                break;

            case nameof(MainWindowViewModel.IsProjectsPageActive):
                ApplyFileExplorerToolWidth(animate: true);
                break;

            case nameof(MainWindowViewModel.IsCommandBuilderOpen):
                AnimatePane(
                    CommandBuilderPane,
                    _viewModel.IsCommandBuilderOpen ? _commandBuilderOpenWidth : CollapsedPaneWidth,
                    CommandBuilderToggleRotation,
                    _commandBuilderTargetAngle);
                RefreshDockedPanelResizeThumbs();
                break;

            case nameof(MainWindowViewModel.WorkspaceLayout):
                ApplyWorkspaceLayout(_viewModel.WorkspaceLayout);
                break;

            case nameof(MainWindowViewModel.WorkspaceAiAssistant):
                RefreshWorkspaceAiMessageSubscription();
                break;
        }

        NotifyExtensionPanelHostContextChanged();
    }

    private void RefreshWorkspaceAiMessageSubscription()
    {
        var assistant = _viewModel.WorkspaceAiAssistant;
        if (ReferenceEquals(_subscribedWorkspaceAiAssistant, assistant))
        {
            return;
        }

        if (_subscribedWorkspaceAiAssistant is not null)
        {
            _subscribedWorkspaceAiAssistant.Messages.CollectionChanged -=
                OnWorkspaceAiMessagesChanged;
        }

        _subscribedWorkspaceAiAssistant = assistant;
        assistant.Messages.CollectionChanged += OnWorkspaceAiMessagesChanged;
        ScrollWorkspaceAiMessagesToEnd();
    }

    private void OnWorkspaceTabsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        RefreshWorkspaceTabSubscriptions();
        NotifyExtensionPanelHostContextChanged();
    }

    private void RefreshWorkspaceTabSubscriptions()
    {
        var currentTabs = _viewModel.WorkspaceTabs
            .OfType<INotifyPropertyChanged>()
            .ToHashSet();
        foreach (var removed in _workspaceTabSubscriptions
                     .Where(tab => !currentTabs.Contains(tab))
                     .ToList())
        {
            removed.PropertyChanged -= OnWorkspaceTabPropertyChanged;
            _workspaceTabSubscriptions.Remove(removed);
        }

        foreach (var added in currentTabs.Where(tab =>
                     !_workspaceTabSubscriptions.Contains(tab)))
        {
            added.PropertyChanged += OnWorkspaceTabPropertyChanged;
            _workspaceTabSubscriptions.Add(added);
        }
    }

    private void OnWorkspaceTabPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs) =>
        NotifyExtensionPanelHostContextChanged();

    private void NotifyExtensionPanelHostContextChanged()
    {
        foreach (var panelView in _extensionPanelViews.Values.ToList())
        {
            panelView.NotifyHostContextChanged();
        }
    }

    private static void AnimatePane(
        FrameworkElement pane,
        double targetWidth,
        RotateTransform toggleRotation,
        double targetAngle)
    {
        var startAngle = toggleRotation.Angle;
        var duration = new Duration(PaneTransitionDuration);

        AnimateElementWidth(pane, targetWidth);
        toggleRotation.Angle = targetAngle;

        toggleRotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(startAngle, targetAngle, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateElementWidth(
        FrameworkElement element,
        double targetWidth)
    {
        var startWidth = element.ActualWidth;
        element.Width = targetWidth;
        element.BeginAnimation(
            WidthProperty,
            new DoubleAnimation(
                startWidth,
                targetWidth,
                new Duration(PaneTransitionDuration))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseInOut
                },
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private double GetActiveFileExplorerWidth() =>
        _viewModel.IsProjectsPageActive
            ? Math.Max(_fileExplorerOpenWidth, _commandBuilderOpenWidth)
            : _fileExplorerOpenWidth;

    private void ApplyFileExplorerToolWidth(bool animate)
    {
        if (!ReferenceEquals(
                FileExplorerPanelHost.Content,
                FileExplorerPane))
        {
            return;
        }

        var targetWidth = GetActiveFileExplorerWidth();
        if (animate)
        {
            AnimateElementWidth(FileExplorerPanelContent, targetWidth);
        }
        else
        {
            FileExplorerPanelContent.BeginAnimation(WidthProperty, null);
            FileExplorerPanelContent.Width = targetWidth;
        }

        if (!_viewModel.IsFileExplorerOpen)
        {
            FileExplorerPane.BeginAnimation(WidthProperty, null);
            FileExplorerPane.Width = CollapsedPaneWidth;
        }
        else if (animate)
        {
            AnimateElementWidth(FileExplorerPane, targetWidth);
        }
        else
        {
            FileExplorerPane.BeginAnimation(WidthProperty, null);
            FileExplorerPane.Width = targetWidth;
        }
    }

    private void ApplyWorkspaceLayout(WorkspaceLayoutSnapshot layout)
    {
        var normalized = WorkspaceLayoutService.Normalize(
            layout,
            _viewModel.WorkspacePanelRegistry);
        var fileExplorer = WorkspaceLayoutService.GetRequiredPanel(
            normalized,
            WorkspacePanelRegistry.FileExplorerPanelId);
        var commandBuilder = WorkspaceLayoutService.GetRequiredPanel(
            normalized,
            WorkspacePanelRegistry.CommandBuilderPanelId);

        var contributedPanels = new List<(
            PanelPlacementSnapshot Placement,
            ExtensionPanelCandidate Candidate)>();
        foreach (var placement in normalized.Panels
                     .Where(panel => panel.IsVisible
                                     && !string.Equals(
                                         panel.ExtensionId,
                                         WorkspacePanelRegistry.CoreOwnerId,
                                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(panel => panel.Order))
        {
            if (_viewModel.TryGetApprovedPanelCandidate(
                    placement.PanelId,
                    out var candidate)
                && candidate is not null)
            {
                contributedPanels.Add((placement, candidate));
            }
        }

        var fileExplorerReplacement = contributedPanels.FirstOrDefault(item =>
            item.Placement.Replaces == BuiltInWorkspaceSurface.FileExplorer);
        var commandBuilderReplacement = contributedPanels.FirstOrDefault(item =>
            item.Placement.Replaces == BuiltInWorkspaceSurface.CommandBuilder);
        var bottomPanel = contributedPanels.FirstOrDefault(item =>
            item.Placement.Replaces is null
            && item.Placement.Slot == WorkspacePanelSlot.Bottom);
        var sidePanels = contributedPanels
            .Where(item => item.Placement.Replaces is null
                           && item.Placement.Slot is WorkspacePanelSlot.Left
                               or WorkspacePanelSlot.Right)
            .ToList();
        var floatingPanels = contributedPanels
            .Where(item => item.Placement.Replaces is null
                           && item.Placement.Slot == WorkspacePanelSlot.Floating)
            .ToList();
        var activePanelIds = floatingPanels
            .Select(item => item.Placement.PanelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fileExplorerReplacement.Candidate is not null)
        {
            activePanelIds.Add(fileExplorerReplacement.Placement.PanelId);
        }

        if (commandBuilderReplacement.Candidate is not null)
        {
            activePanelIds.Add(commandBuilderReplacement.Placement.PanelId);
        }

        if (bottomPanel.Candidate is not null)
        {
            activePanelIds.Add(bottomPanel.Placement.PanelId);
        }

        foreach (var sidePanel in sidePanels)
        {
            activePanelIds.Add(sidePanel.Placement.PanelId);
        }

        CloseObsoleteFloatingPanels(floatingPanels);
        DetachDockedExtensionViews();

        _fileExplorerOpenWidth = fileExplorer.Width;
        _commandBuilderOpenWidth = commandBuilder.Width;
        RestoreBuiltInHost(
            FileExplorerPanelHost,
            FileExplorerPane,
            WorkspacePanelRegistry.FileExplorerPanelId);
        RestoreBuiltInHost(
            CommandBuilderPanelHost,
            CommandBuilderPane,
            WorkspacePanelRegistry.CommandBuilderPanelId);
        _fileExplorerTargetAngle = ConfigureDockedPanel(
            FileExplorerPanelHost,
            FileExplorerPane,
            FileExplorerPanelContent,
            FileExplorerToggleRotation,
            fileExplorer,
            _viewModel.IsFileExplorerOpen);
        _commandBuilderTargetAngle = ConfigureDockedPanel(
            CommandBuilderPanelHost,
            CommandBuilderPane,
            CommandBuilderPanelContent,
            CommandBuilderToggleRotation,
            commandBuilder,
            _viewModel.IsCommandBuilderOpen);
        ArrangeFileExplorerHeader(fileExplorer.Slot);
        ArrangeCommandBuilderHeader(commandBuilder.Slot);

        if (fileExplorerReplacement.Candidate is not null)
        {
            ConfigureExtensionDockedPanel(
                FileExplorerPanelHost,
                fileExplorerReplacement.Placement,
                fileExplorerReplacement.Candidate);
        }

        if (commandBuilderReplacement.Candidate is not null)
        {
            ConfigureExtensionDockedPanel(
                CommandBuilderPanelHost,
                commandBuilderReplacement.Placement,
                commandBuilderReplacement.Candidate);
        }

        foreach (var sidePanel in sidePanels)
        {
            ConfigureDirectDockedExtensionPanel(
                sidePanel.Placement,
                sidePanel.Candidate);
        }

        ApplyFileExplorerToolWidth(animate: false);
        RefreshDockedPanelResizeThumbs();
        if (IsLoaded && ActualWidth > 0)
        {
            ScaleDockedPanelsForWindowResize(1, ActualWidth);
        }
        ConfigureBottomPanel(bottomPanel.Placement, bottomPanel.Candidate);
        ConfigureFloatingPanels(floatingPanels);
        DisposeInactiveExtensionViews(activePanelIds);
    }

    private void RestoreBuiltInHost(
        WorkspacePanelHost host,
        Border pane,
        string panelId)
    {
        host.Width = double.NaN;
        host.Height = double.NaN;
        host.Registration = _viewModel.WorkspacePanelRegistry.GetRequired(panelId);
        host.Content = pane;
    }

    private void ConfigureExtensionDockedPanel(
        WorkspacePanelHost host,
        PanelPlacementSnapshot placement,
        ExtensionPanelCandidate candidate)
    {
        var slot = placement.Slot == WorkspacePanelSlot.Left
            ? WorkspacePanelSlot.Left
            : WorkspacePanelSlot.Right;
        DockPanel.SetDock(host, slot == WorkspacePanelSlot.Left
            ? Dock.Left
            : Dock.Right);
        host.Registration = candidate.Registration;
        host.CurrentSlot = slot;
        host.Width = placement.Width;
        host.Height = double.NaN;
        host.Visibility = Visibility.Visible;
        host.Content = GetOrCreateExtensionPanelView(candidate);
    }

    private void ConfigureDirectDockedExtensionPanel(
        PanelPlacementSnapshot placement,
        ExtensionPanelCandidate candidate)
    {
        var slot = placement.Slot == WorkspacePanelSlot.Left
            ? WorkspacePanelSlot.Left
            : WorkspacePanelSlot.Right;
        var dock = slot == WorkspacePanelSlot.Left
            ? Dock.Left
            : Dock.Right;
        var host = new WorkspacePanelHost
        {
            Registration = candidate.Registration,
            CurrentSlot = slot,
            Width = Math.Max(
                WorkspaceLayoutService.DockedPanelMinimumWidth,
                placement.Width),
            Height = double.NaN,
            Visibility = Visibility.Visible,
            Content = GetOrCreateExtensionPanelView(candidate)
        };
        DockPanel.SetDock(host, dock);

        var resizeThumb = new Thumb
        {
            Style = (Style)FindResource("DockedPanelResizeThumbStyle"),
            Visibility = Visibility.Visible,
            ToolTip = $"Resize {candidate.Registration.Contribution.DisplayName}"
        };
        DockPanel.SetDock(resizeThumb, dock);
        resizeThumb.DragDelta += (_, eventArgs) =>
            ResizeDockedPanelDragDelta(
                host,
                builtInPane: null,
                builtInContent: null,
                eventArgs);
        resizeThumb.DragCompleted += (_, eventArgs) =>
            CommitDockedPanelResize(
                host,
                builtInPane: null,
                eventArgs);

        var insertionIndex = WorkspaceDock.Children.IndexOf(BottomPanelContainer);
        WorkspaceDock.Children.Insert(insertionIndex, host);
        WorkspaceDock.Children.Insert(insertionIndex + 1, resizeThumb);
        _dockedExtensionPanels[candidate.PanelId] =
            new DockedExtensionPanelChrome(host, resizeThumb);
    }

    private void ConfigureBottomPanel(
        PanelPlacementSnapshot? placement,
        ExtensionPanelCandidate? candidate)
    {
        if (placement is null || candidate is null)
        {
            _activeBottomPanelId = string.Empty;
            BottomPanelHost.Content = null;
            BottomPanelHost.Registration = null;
            BottomPanelContainer.Visibility = Visibility.Collapsed;
            return;
        }

        _activeBottomPanelId = placement.PanelId;
        BottomPanelHost.Registration = candidate.Registration;
        BottomPanelHost.CurrentSlot = WorkspacePanelSlot.Bottom;
        BottomPanelHost.Content = GetOrCreateExtensionPanelView(candidate);
        BottomPanelContainer.Height = placement.Height;
        BottomPanelContainer.Visibility = Visibility.Visible;
    }

    private void ConfigureFloatingPanels(
        IReadOnlyCollection<(
            PanelPlacementSnapshot Placement,
            ExtensionPanelCandidate Candidate)> floatingPanels)
    {
        foreach (var (placement, candidate) in floatingPanels)
        {
            if (_floatingExtensionPanels.TryGetValue(
                    placement.PanelId,
                    out var existingWindow))
            {
                existingWindow.ApplyPlacement(placement);
                continue;
            }

            var panelView = GetOrCreateExtensionPanelView(candidate);
            var window = new ExtensionPanelFloatingWindow(
                candidate,
                panelView,
                placement)
            {
                Owner = this
            };
            var panelId = placement.PanelId;
            window.CloseRequested += (_, _) =>
                _viewModel.HideExtensionPanel(panelId);
            window.BoundsCommitted += (_, bounds) =>
                _viewModel.ResizeExtensionPanel(
                    panelId,
                    bounds.Width,
                    bounds.ContentHeight);
            _floatingExtensionPanels.Add(panelId, window);
            window.Show();
        }
    }

    private ExtensionPanelView GetOrCreateExtensionPanelView(
        ExtensionPanelCandidate candidate)
    {
        if (_extensionPanelViews.TryGetValue(
                candidate.PanelId,
                out var existing))
        {
            var samePackage = string.Equals(
                                  existing.Candidate.Package.PackageSha256,
                                  candidate.Package.PackageSha256,
                                  StringComparison.OrdinalIgnoreCase)
                              && string.Equals(
                                  existing.Candidate.ViewResourcePath,
                                  candidate.ViewResourcePath,
                                  StringComparison.OrdinalIgnoreCase);
            if (samePackage)
            {
                existing.IsCustomizationEnabled =
                    _isWorkspaceCustomizationActive;
                return existing;
            }

            existing.DragRequested -= OnExtensionPanelDragRequested;
            existing.Dispose();
            _extensionPanelViews.Remove(candidate.PanelId);
        }

        var panelView = new ExtensionPanelView(
            candidate,
            _viewModel.ExtensionWebViewDataDirectory,
            _viewModel.ExtensionPanelStateDirectory,
            _viewModel.GetApprovedPanelCapabilities(candidate.Package),
            CreateExtensionPanelHostContext);
        var panelId = candidate.PanelId;
        panelView.CloseRequested += (_, _) =>
            _viewModel.HideExtensionPanel(panelId);
        panelView.DragRequested += OnExtensionPanelDragRequested;
        panelView.IsCustomizationEnabled = _isWorkspaceCustomizationActive;
        _extensionPanelViews.Add(panelId, panelView);
        return panelView;
    }

    private ExtensionPanelHostContext CreateExtensionPanelHostContext() =>
        new(
            1,
            _viewModel.ExtensionPanelWorkspaceContext,
            new ExtensionPanelThemeContext(
                "dark",
                ResolveThemeColor("AppBackgroundBrush", "#0D1117"),
                ResolveThemeColor("PanelBrush", "#161B22"),
                ResolveThemeColor("ForegroundBrush", "#E6EDF3"),
                ResolveThemeColor("MutedForegroundBrush", "#8B949E"),
                ResolveThemeColor("AccentBrush", "#58A6FF")));

    private string ResolveThemeColor(string resourceKey, string fallback) =>
        TryFindResource(resourceKey) is SolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : fallback;

    private void DetachDockedExtensionViews()
    {
        foreach (var chrome in _dockedExtensionPanels.Values)
        {
            chrome.Host.Content = null;
            WorkspaceDock.Children.Remove(chrome.ResizeThumb);
            WorkspaceDock.Children.Remove(chrome.Host);
        }

        _dockedExtensionPanels.Clear();
        if (FileExplorerPanelHost.Content is ExtensionPanelView)
        {
            FileExplorerPanelHost.Content = null;
        }

        if (CommandBuilderPanelHost.Content is ExtensionPanelView)
        {
            CommandBuilderPanelHost.Content = null;
        }

        if (BottomPanelHost.Content is ExtensionPanelView)
        {
            BottomPanelHost.Content = null;
        }
    }

    private void CloseObsoleteFloatingPanels(
        IReadOnlyCollection<(
            PanelPlacementSnapshot Placement,
            ExtensionPanelCandidate Candidate)> desiredPanels)
    {
        var desiredById = desiredPanels.ToDictionary(
            item => item.Placement.PanelId,
            item => item.Candidate,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (panelId, window) in _floatingExtensionPanels.ToList())
        {
            var keep = desiredById.TryGetValue(panelId, out var desiredCandidate)
                       && string.Equals(
                           desiredCandidate.Package.PackageSha256,
                           window.Candidate.Package.PackageSha256,
                           StringComparison.OrdinalIgnoreCase);
            if (keep)
            {
                continue;
            }

            window.DetachAndCloseForLayout();
            _floatingExtensionPanels.Remove(panelId);
        }
    }

    private void DisposeInactiveExtensionViews(ISet<string> activePanelIds)
    {
        foreach (var (panelId, panelView) in _extensionPanelViews.ToList())
        {
            if (activePanelIds.Contains(panelId))
            {
                continue;
            }

            panelView.DragRequested -= OnExtensionPanelDragRequested;
            panelView.Dispose();
            _extensionPanelViews.Remove(panelId);
        }
    }

    private void DisposeExtensionPanels()
    {
        foreach (var window in _floatingExtensionPanels.Values.ToList())
        {
            window.DetachAndCloseForLayout();
        }

        _floatingExtensionPanels.Clear();
        DetachDockedExtensionViews();
        foreach (var panelView in _extensionPanelViews.Values)
        {
            panelView.DragRequested -= OnExtensionPanelDragRequested;
            panelView.Dispose();
        }

        _extensionPanelViews.Clear();
    }

    private void BottomPanelResizeDragDelta(
        object sender,
        DragDeltaEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_activeBottomPanelId)
            || !_viewModel.TryGetApprovedPanelCandidate(
                _activeBottomPanelId,
                out var candidate)
            || candidate is null)
        {
            return;
        }

        var policy = candidate.Registration.Contribution.Placement;
        BottomPanelContainer.Height = Math.Clamp(
            BottomPanelContainer.Height - eventArgs.VerticalChange,
            policy.MinimumHeight,
            Math.Min(policy.MaximumHeight, Math.Max(policy.MinimumHeight, ActualHeight - 180)));
        eventArgs.Handled = true;
    }

    private void BottomPanelResizeDragCompleted(
        object sender,
        DragCompletedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_activeBottomPanelId))
        {
            _viewModel.ResizeExtensionPanel(
                _activeBottomPanelId,
                width: null,
                height: BottomPanelContainer.Height);
        }

        eventArgs.Handled = true;
    }

    private void FileExplorerResizeDragDelta(
        object sender,
        DragDeltaEventArgs eventArgs) =>
        ResizeDockedPanelDragDelta(
            FileExplorerPanelHost,
            FileExplorerPane,
            FileExplorerPanelContent,
            eventArgs);

    private void FileExplorerResizeDragCompleted(
        object sender,
        DragCompletedEventArgs eventArgs) =>
        CommitDockedPanelResize(
            FileExplorerPanelHost,
            FileExplorerPane,
            eventArgs);

    private void CommandBuilderResizeDragDelta(
        object sender,
        DragDeltaEventArgs eventArgs) =>
        ResizeDockedPanelDragDelta(
            CommandBuilderPanelHost,
            CommandBuilderPane,
            CommandBuilderPanelContent,
            eventArgs);

    private void CommandBuilderResizeDragCompleted(
        object sender,
        DragCompletedEventArgs eventArgs) =>
        CommitDockedPanelResize(
            CommandBuilderPanelHost,
            CommandBuilderPane,
            eventArgs);

    private void ResizeDockedPanelDragDelta(
        WorkspacePanelHost host,
        Border? builtInPane,
        FrameworkElement? builtInContent,
        DragDeltaEventArgs eventArgs)
    {
        if (host.Visibility != Visibility.Visible
            || !IsDockedPanelExpanded(host, builtInPane))
        {
            return;
        }

        var minimumWidth = WorkspaceLayoutService.DockedPanelMinimumWidth;
        var otherWidth = EnumerateDockedPanels()
            .Where(panel => !ReferenceEquals(panel.Host, host)
                            && panel.Host.Visibility == Visibility.Visible
                            && IsDockedPanelExpanded(
                                panel.Host,
                                panel.BuiltInPane))
            .Sum(panel => GetDockedPanelOpenWidth(
                panel.Host,
                panel.BuiltInPane));
        var dividerWidth = EnumerateDockedPanels()
            .Where(panel => panel.ResizeThumb.Visibility == Visibility.Visible)
            .Sum(panel => panel.ResizeThumb.ActualWidth);
        var availableWidth = Math.Max(
            minimumWidth,
            ActualWidth - otherWidth - dividerWidth - MinimumCenterPaneWidth);
        var maximumWidth = Math.Min(
            WorkspaceLayoutService.DockedPanelMaximumWidth,
            availableWidth);

        var horizontalChange = host.CurrentSlot == WorkspacePanelSlot.Left
            ? eventArgs.HorizontalChange
            : -eventArgs.HorizontalChange;
        var width = Math.Clamp(
            GetDockedPanelOpenWidth(host, builtInPane) + horizontalChange,
            minimumWidth,
            maximumWidth);
        ApplyDockedPanelWidth(host, builtInPane, builtInContent, width);
        eventArgs.Handled = true;
    }

    private void CommitDockedPanelResize(
        WorkspacePanelHost host,
        Border? builtInPane,
        DragCompletedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(host.PanelId))
        {
            _viewModel.UpdateWorkspacePanelSize(
                host.PanelId,
                GetDockedPanelOpenWidth(host, builtInPane),
                height: null,
                refreshLayout: false);
        }

        eventArgs.Handled = true;
    }

    private void RefreshDockedPanelResizeThumbs()
    {
        ConfigureDockedPanelResizeThumb(
            FileExplorerResizeThumb,
            FileExplorerPanelHost,
            FileExplorerPane,
            _viewModel.IsFileExplorerOpen);
        ConfigureDockedPanelResizeThumb(
            CommandBuilderResizeThumb,
            CommandBuilderPanelHost,
            CommandBuilderPane,
            _viewModel.IsCommandBuilderOpen);
        foreach (var chrome in _dockedExtensionPanels.Values)
        {
            ConfigureDockedPanelResizeThumb(
                chrome.ResizeThumb,
                chrome.Host,
                builtInPane: null,
                isBuiltInPaneOpen: true);
        }
    }

    private static void ConfigureDockedPanelResizeThumb(
        Thumb thumb,
        WorkspacePanelHost host,
        Border? builtInPane,
        bool isBuiltInPaneOpen)
    {
        DockPanel.SetDock(
            thumb,
            host.CurrentSlot == WorkspacePanelSlot.Left
                ? Dock.Left
                : Dock.Right);
        var isExpanded = !ReferenceEquals(host.Content, builtInPane)
                         || isBuiltInPaneOpen;
        thumb.Visibility = host.Visibility == Visibility.Visible
                           && isExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        thumb.ToolTip = $"Resize {host.DisplayName}";
    }

    private bool IsDockedPanelExpanded(
        WorkspacePanelHost host,
        Border? builtInPane)
    {
        if (builtInPane is null
            || !ReferenceEquals(host.Content, builtInPane))
        {
            return true;
        }

        return ReferenceEquals(host, FileExplorerPanelHost)
            ? _viewModel.IsFileExplorerOpen
            : _viewModel.IsCommandBuilderOpen;
    }

    private double GetDockedPanelOpenWidth(
        WorkspacePanelHost host,
        Border? builtInPane)
    {
        if (builtInPane is not null
            && ReferenceEquals(host.Content, builtInPane))
        {
            if (IsDockedPanelExpanded(host, builtInPane)
                && builtInPane.ActualWidth > 0)
            {
                return builtInPane.ActualWidth;
            }

            return ReferenceEquals(host, FileExplorerPanelHost)
                ? _fileExplorerOpenWidth
                : _commandBuilderOpenWidth;
        }

        return host.ActualWidth > 0
            ? host.ActualWidth
            : host.Width;
    }

    private double GetDockedPanelConfiguredWidth(
        WorkspacePanelHost host,
        Border? builtInPane)
    {
        if (builtInPane is not null
            && ReferenceEquals(host.Content, builtInPane))
        {
            if (ReferenceEquals(host, FileExplorerPanelHost)
                && _viewModel.IsProjectWorkItemsPageActive)
            {
                return GetActiveFileExplorerWidth();
            }

            return ReferenceEquals(host, FileExplorerPanelHost)
                ? _fileExplorerOpenWidth
                : _commandBuilderOpenWidth;
        }

        return double.IsFinite(host.Width) && host.Width > 0
            ? host.Width
            : host.ActualWidth;
    }

    private void ApplyDockedPanelWidth(
        WorkspacePanelHost host,
        Border? builtInPane,
        FrameworkElement? builtInContent,
        double width)
    {
        if (builtInPane is null
            || builtInContent is null
            || !ReferenceEquals(host.Content, builtInPane))
        {
            host.Width = width;
            return;
        }

        builtInPane.BeginAnimation(WidthProperty, null);
        builtInContent.BeginAnimation(WidthProperty, null);
        builtInContent.Width = width;
        builtInPane.Width = IsDockedPanelExpanded(host, builtInPane)
            ? width
            : CollapsedPaneWidth;
        if (ReferenceEquals(host, FileExplorerPanelHost))
        {
            _fileExplorerOpenWidth = width;
        }
        else
        {
            _commandBuilderOpenWidth = width;
        }
    }

    private IEnumerable<(
        WorkspacePanelHost Host,
        Border? BuiltInPane,
        FrameworkElement? BuiltInContent,
        Thumb ResizeThumb)> EnumerateDockedPanels()
    {
        yield return (
            FileExplorerPanelHost,
            FileExplorerPane,
            FileExplorerPanelContent,
            FileExplorerResizeThumb);
        yield return (
            CommandBuilderPanelHost,
            CommandBuilderPane,
            CommandBuilderPanelContent,
            CommandBuilderResizeThumb);
        foreach (var chrome in _dockedExtensionPanels.Values)
        {
            yield return (
                chrome.Host,
                null,
                null,
                chrome.ResizeThumb);
        }
    }

    private static double ConfigureDockedPanel(
        WorkspacePanelHost host,
        Border pane,
        FrameworkElement panelContent,
        RotateTransform toggleRotation,
        PanelPlacementSnapshot placement,
        bool isOpen)
    {
        var slot = placement.Slot == WorkspacePanelSlot.Left
            ? WorkspacePanelSlot.Left
            : WorkspacePanelSlot.Right;
        var dock = slot == WorkspacePanelSlot.Left
            ? Dock.Left
            : Dock.Right;
        DockPanel.SetDock(host, dock);
        host.CurrentSlot = slot;
        host.Visibility = placement.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        pane.BeginAnimation(WidthProperty, null);
        toggleRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        pane.Width = isOpen ? placement.Width : CollapsedPaneWidth;
        panelContent.Width = placement.Width;
        panelContent.HorizontalAlignment = slot == WorkspacePanelSlot.Left
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        pane.BorderThickness = slot == WorkspacePanelSlot.Left
            ? new Thickness(0, 0, 1, 0)
            : new Thickness(1, 0, 0, 0);
        var targetAngle = slot == WorkspacePanelSlot.Left ? 180 : 0;
        toggleRotation.Angle = targetAngle;
        return targetAngle;
    }

    private void ArrangeFileExplorerHeader(WorkspacePanelSlot slot)
    {
        var isLeft = slot == WorkspacePanelSlot.Left;
        FileExplorerHeaderPageColumn.Width = GridLength.Auto;
        FileExplorerHeaderPrimaryColumn.Width =
            new GridLength(1, GridUnitType.Star);
        FileExplorerHeaderToggleColumn.Width = GridLength.Auto;
        Grid.SetColumn(FileExplorerHeaderToggle, isLeft ? 2 : 0);
        Grid.SetColumn(FileExplorerPageToggles, isLeft ? 0 : 2);
        FileExplorerHeaderToggle.HorizontalAlignment = isLeft
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        FileExplorerPageToggles.HorizontalAlignment = isLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        FileExplorerPageToggles.Margin = isLeft
            ? new Thickness(8, 0, 0, 0)
            : new Thickness(0, 0, 8, 0);
    }

    private void ArrangeCommandBuilderHeader(WorkspacePanelSlot slot)
    {
        var isLeft = slot == WorkspacePanelSlot.Left;
        CommandBuilderHeaderToggleColumn.Width = GridLength.Auto;
        CommandBuilderHeaderPrimaryColumn.Width =
            new GridLength(1, GridUnitType.Star);
        CommandBuilderHeaderPageColumn.Width = GridLength.Auto;
        Grid.SetColumn(CommandBuilderHeaderToggle, isLeft ? 2 : 0);
        Grid.SetColumn(CommandBuilderPageToggles, isLeft ? 0 : 2);
        CommandBuilderHeaderToggle.HorizontalAlignment = isLeft
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        CommandBuilderPageToggles.HorizontalAlignment = isLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        CommandBuilderPageToggles.Margin = isLeft
            ? new Thickness(8, 0, 0, 0)
            : new Thickness(0, 0, 8, 0);
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        TaskbarIdentityService.ApplyWindowIdentity(new WindowInteropHelper(this).Handle);
        ApplyWindowCornerPreference();
        AttachWorkspaceAiStopHotKey();
    }

    private void AttachWorkspaceAiStopHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _windowMessageSource = HwndSource.FromHwnd(handle);
        _windowMessageSource?.AddHook(OnWindowMessage);
        _isWorkspaceAiStopHotKeyRegistered = RegisterHotKey(
            handle,
            WorkspaceAiStopHotKeyId,
            HotKeyModifierControl | HotKeyModifierAlt,
            (uint)KeyInterop.VirtualKeyFromKey(Key.Escape));
    }

    private void DetachWorkspaceAiStopHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (_isWorkspaceAiStopHotKeyRegistered && handle != IntPtr.Zero)
        {
            _ = UnregisterHotKey(handle, WorkspaceAiStopHotKeyId);
        }

        _isWorkspaceAiStopHotKeyRegistered = false;
        _windowMessageSource?.RemoveHook(OnWindowMessage);
        _windowMessageSource = null;
    }

    private IntPtr OnWindowMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == WindowsMessageGetMinMaxInfo)
        {
            handled = ApplyMaximizedWorkArea(windowHandle, longParameter);
            return IntPtr.Zero;
        }

        if (message == WindowsMessageHotKey
            && wordParameter.ToInt32() == WorkspaceAiStopHotKeyId)
        {
            _viewModel.WorkspaceAiAssistant.StopComputerControlCommand.Execute(
                null);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private bool ApplyMaximizedWorkArea(
        IntPtr windowHandle,
        IntPtr minMaxInfoPointer)
    {
        if (windowHandle == IntPtr.Zero || minMaxInfoPointer == IntPtr.Zero)
        {
            return false;
        }

        var monitorHandle = MonitorFromWindow(
            windowHandle,
            MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return false;
        }

        var minMaxInfo = Marshal.PtrToStructure<NativeMinMaxInfo>(
            minMaxInfoPointer);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left
                                       - monitorInfo.Monitor.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top
                                       - monitorInfo.Monitor.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right
                               - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom
                               - monitorInfo.WorkArea.Top;
        minMaxInfo.MaxTrackSize = minMaxInfo.MaxSize;
        var dpi = VisualTreeHelper.GetDpi(this);
        minMaxInfo.MinTrackSize.X = Math.Min(minMaxInfo.MaxSize.X,
            Math.Max(1, (int)Math.Ceiling(MinWidth * dpi.DpiScaleX)));
        minMaxInfo.MinTrackSize.Y = Math.Min(minMaxInfo.MaxSize.Y,
            Math.Max(1, (int)Math.Ceiling(MinHeight * dpi.DpiScaleY)));
        Marshal.StructureToPtr(
            minMaxInfo,
            minMaxInfoPointer,
            fDeleteOld: false);
        return true;
    }

    private void OnWindowStateChanged(object? sender, EventArgs eventArgs)
    {
        ApplyWindowCornerPreference();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        var newWidth = eventArgs.NewSize.Width;
        if (!IsLoaded
            || WindowState == WindowState.Minimized
            || _lastWindowClientWidth <= 0
            || newWidth <= 0)
        {
            _lastWindowClientWidth = newWidth;
            return;
        }

        var scale = newWidth / _lastWindowClientWidth;
        _lastWindowClientWidth = newWidth;
        if (!double.IsFinite(scale) || Math.Abs(scale - 1) < 0.001)
        {
            return;
        }

        ScaleDockedPanelsForWindowResize(scale, newWidth);
    }

    private void ScaleDockedPanelsForWindowResize(
        double scale,
        double newWindowWidth)
    {
        var targets = new List<DockedPanelScaleTarget>();
        foreach (var panel in EnumerateDockedPanels())
        {
            AddDockedPanelScaleTarget(
                targets,
                panel.Host,
                panel.BuiltInPane,
                panel.BuiltInContent,
                scale);
        }
        if (targets.Count == 0)
        {
            return;
        }

        var expandedTargets = targets
            .Where(target => target.IsExpanded)
            .ToList();
        var minimumTotal = expandedTargets.Sum(target => target.MinimumWidth);
        var desiredTotal = expandedTargets.Sum(target => target.DesiredWidth);
        var dividerWidth = EnumerateDockedPanels()
            .Where(panel => panel.ResizeThumb.Visibility == Visibility.Visible)
            .Sum(panel => panel.ResizeThumb.ActualWidth);
        var availableTotal = Math.Max(
            minimumTotal,
            newWindowWidth - dividerWidth - MinimumCenterPaneWidth);
        var flexTotal = expandedTargets.Sum(target =>
            target.DesiredWidth - target.MinimumWidth);
        var allowedFlex = Math.Max(0, availableTotal - minimumTotal);
        var flexScale = desiredTotal > availableTotal && flexTotal > 0
            ? Math.Min(1, allowedFlex / flexTotal)
            : 1;

        foreach (var target in targets)
        {
            var scaledWidth = target.IsExpanded
                ? target.MinimumWidth
                  + ((target.DesiredWidth - target.MinimumWidth) * flexScale)
                : target.DesiredWidth;
            ApplyDockedPanelWidth(
                target.Host,
                target.BuiltInPane,
                target.BuiltInContent,
                scaledWidth);
            _viewModel.UpdateWorkspacePanelSize(
                target.Host.PanelId,
                scaledWidth,
                height: null,
                refreshLayout: false);
        }
    }

    private void AddDockedPanelScaleTarget(
        ICollection<DockedPanelScaleTarget> targets,
        WorkspacePanelHost host,
        Border? builtInPane,
        FrameworkElement? builtInContent,
        double scale)
    {
        if (host.Visibility != Visibility.Visible)
        {
            return;
        }

        var minimumWidth = WorkspaceLayoutService.DockedPanelMinimumWidth;
        targets.Add(
            new DockedPanelScaleTarget(
                host,
                builtInPane,
                builtInContent,
                minimumWidth,
                Math.Clamp(
                    GetDockedPanelConfiguredWidth(host, builtInPane) * scale,
                    minimumWidth,
                    WorkspaceLayoutService.DockedPanelMaximumWidth),
                IsDockedPanelExpanded(host, builtInPane)));
    }

    private void ApplyWindowCornerPreference()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var preference = WindowState == WindowState.Maximized
            ? (int)DwmWindowCornerPreference.DoNotRound
            : (int)DwmWindowCornerPreference.Round;

        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            sizeof(int));
    }

    private async void FileListMouseDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        var entry = GetFileEntry(eventArgs.OriginalSource);
        if (entry is null)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            await _viewModel.FileExplorer.OpenEntryAsync(entry);
        }
        else
        {
            await OpenDocumentLocationAsync(entry.Location);
        }

        eventArgs.Handled = true;
    }

    private void CreateExplorerProjectClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new ProjectCreationDialog(
            _viewModel.FileExplorer.CurrentLocation)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _viewModel.CreateExplorerProject(dialog.Project);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Create project",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void FileListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left || eventArgs.ClickCount != 1)
        {
            return;
        }

        var entry = GetFileEntry(eventArgs.OriginalSource);
        if (entry?.IsDirectory == true)
        {
            await _viewModel.FileExplorer.ToggleExpansionAsync(entry);
        }
    }

    private void FileListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        var listBoxItem = GetFileListItem(eventArgs.OriginalSource);
        if (listBoxItem?.DataContext is not FileSystemEntry entry)
        {
            _contextMenuEntry = null;
            return;
        }

        _contextMenuEntry = entry;
        FileList.SelectedItems.Clear();
        listBoxItem.IsSelected = true;
        listBoxItem.Focus();
    }

    private void FileListContextMenuOpening(object sender, ContextMenuEventArgs eventArgs)
    {
        if (_contextMenuEntry is null)
        {
            eventArgs.Handled = true;
        }
    }

    private void InsertFilePathClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_contextMenuEntry is null)
        {
            return;
        }

        _viewModel.FileExplorer.InsertPaths([_contextMenuEntry]);
    }

    private async void OpenFileInEditorClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_contextMenuEntry is not { IsDirectory: false } entry)
        {
            return;
        }

        await OpenDocumentLocationAsync(entry.Location);
    }

    private async void CreateExplorerFileClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new ExplorerTextPromptDialog(
            "Create file",
            "FILE NAME",
            "Create")
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await RunExplorerMutationAsync(
                () => _viewModel.FileExplorer.CreateFileAsync(dialog.Value),
                "Create file");
        }
    }

    private async void CreateExplorerFolderClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new ExplorerTextPromptDialog(
            "Create folder",
            "FOLDER NAME",
            "Create")
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await RunExplorerMutationAsync(
                () => _viewModel.FileExplorer.CreateDirectoryAsync(dialog.Value),
                "Create folder");
        }
    }

    private async void UploadExplorerFileClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Upload files to the selected device",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            if (!await RunExplorerMutationAsync(
                    () => _viewModel.FileExplorer.UploadFileAsync(fileName),
                    "Upload file"))
            {
                return;
            }
        }
    }

    private async void RenameExplorerEntryClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_contextMenuEntry is not { } entry)
        {
            return;
        }

        var dialog = new ExplorerTextPromptDialog(
            "Rename item",
            "NEW NAME",
            "Rename",
            entry.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            await RunExplorerMutationAsync(
                () => _viewModel.FileExplorer.RenameEntryAsync(
                    entry,
                    dialog.Value),
                "Rename item");
        }
    }

    private async void MoveExplorerEntryClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_contextMenuEntry is not { } entry)
        {
            return;
        }

        var dialog = new ExplorerTextPromptDialog(
            "Move item",
            "DESTINATION FOLDER PATH OR WORKSPACE URI",
            "Move",
            _viewModel.FileExplorer.CurrentPath)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunExplorerMutationAsync(
            () => _viewModel.FileExplorer.MoveEntryAsync(
                entry,
                _viewModel.FileExplorer.ResolveUserLocation(dialog.Value)),
            "Move item");
    }

    private async void DownloadExplorerEntryClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_contextMenuEntry is not { IsDirectory: false } entry)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Download file",
            FileName = entry.Name,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await RunExplorerMutationAsync(
                () => _viewModel.FileExplorer.DownloadFileAsync(
                    entry,
                    dialog.FileName),
                "Download file");
        }
    }

    private async void DeleteExplorerEntryClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_contextMenuEntry is not { } entry)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Permanently delete {entry.Location.Identity}?\n\n" +
            "This operation cannot be undone.",
            "Delete permanently",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await RunExplorerMutationAsync(
                () => _viewModel.FileExplorer.DeleteEntryAsync(
                    entry,
                    recursive: entry.IsDirectory),
                "Delete item");
        }
    }

    private async Task<bool> RunExplorerMutationAsync(
        Func<Task> operation,
        string title)
    {
        try
        {
            await operation();
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException
                                          or WorkspaceFileSystemException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private FileSystemEntry? GetFileEntry(object originalSource)
    {
        return GetFileListItem(originalSource)?.DataContext as FileSystemEntry;
    }

    private ListBoxItem? GetFileListItem(object originalSource)
    {
        return originalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(FileList, source) as ListBoxItem
            : null;
    }

    private void TerminalInputPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (sender is not TextBox input)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if ((eventArgs.Key == Key.V
             && modifiers.HasFlag(ModifierKeys.Control)
             && modifiers.HasFlag(ModifierKeys.Shift))
            || (eventArgs.Key == Key.Insert
                && modifiers.HasFlag(ModifierKeys.Shift)))
        {
            input.Paste();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.C
            && modifiers.HasFlag(ModifierKeys.Control)
            && modifiers.HasFlag(ModifierKeys.Shift))
        {
            input.Copy();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key != Key.Enter
            || input.DataContext is not TerminalTabViewModel terminal)
        {
            return;
        }

        var shouldRun = terminal.Settings.EnterRunsCommand
            ? !modifiers.HasFlag(ModifierKeys.Shift)
            : modifiers.HasFlag(ModifierKeys.Control)
              && !modifiers.HasFlag(ModifierKeys.Shift);
        if (!shouldRun)
        {
            return;
        }

        if (terminal.RunCommand.CanExecute(null))
        {
            terminal.RunCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void TerminalTranscriptLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not RichTextBox transcript
            || transcript.DataContext is not TerminalTabViewModel terminal)
        {
            return;
        }

        BindTerminalTranscript(transcript, terminal);
        transcript.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                if (!IsTerminalTranscriptBoundTo(transcript, terminal))
                {
                    return;
                }

                transcript.ScrollToEnd();
                if (ReferenceEquals(_viewModel.ActiveTerminal, terminal))
                {
                    FocusInlineTerminalInput(
                        VisualTreeHelper.GetParent(transcript));
                }
            });
    }

    private void TerminalTranscriptDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (sender is not RichTextBox transcript)
        {
            return;
        }

        if (eventArgs.NewValue is TerminalTabViewModel terminal)
        {
            BindTerminalTranscript(transcript, terminal);
            transcript.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () =>
                {
                    if (IsTerminalTranscriptBoundTo(transcript, terminal))
                    {
                        transcript.ScrollToEnd();
                    }
                });
            return;
        }

        UnbindTerminalTranscript(transcript);
    }

    private void BindTerminalTranscript(
        RichTextBox transcript,
        TerminalTabViewModel terminal)
    {
        if (_terminalTranscriptSubscriptions.TryGetValue(
                transcript,
                out var existingSubscription))
        {
            if (ReferenceEquals(existingSubscription.Terminal, terminal))
            {
                RenderTerminalTranscript(transcript, terminal);
                return;
            }

            UnsubscribeTerminalTranscript(existingSubscription);
            _terminalTranscriptSubscriptions.Remove(transcript);
        }

        NotifyCollectionChangedEventHandler collectionHandler = (_, _) =>
            transcript.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    if (!IsTerminalTranscriptBoundTo(transcript, terminal))
                    {
                        return;
                    }

                    var shouldScroll = IsTranscriptAtEnd(transcript);
                    RenderTerminalTranscript(transcript, terminal);
                    if (shouldScroll)
                    {
                        transcript.ScrollToEnd();
                    }
                });
        PropertyChangedEventHandler settingsHandler = (_, _) =>
            transcript.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    if (IsTerminalTranscriptBoundTo(transcript, terminal))
                    {
                        RenderTerminalTranscript(transcript, terminal);
                    }
                });
        terminal.Lines.CollectionChanged += collectionHandler;
        terminal.Settings.PropertyChanged += settingsHandler;
        _terminalTranscriptSubscriptions[transcript] =
            new TerminalTranscriptSubscription(
                terminal,
                collectionHandler,
                settingsHandler);

        RenderTerminalTranscript(transcript, terminal);
    }

    private void TerminalTranscriptUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is RichTextBox transcript)
        {
            UnbindTerminalTranscript(transcript);
        }
    }

    private void UnbindTerminalTranscript(RichTextBox transcript)
    {
        if (_terminalTranscriptSubscriptions.Remove(
                transcript,
                out var subscription))
        {
            UnsubscribeTerminalTranscript(subscription);
        }
    }

    private static void UnsubscribeTerminalTranscript(
        TerminalTranscriptSubscription subscription)
    {
        subscription.Terminal.Lines.CollectionChanged -=
            subscription.CollectionHandler;
        subscription.Terminal.Settings.PropertyChanged -=
            subscription.SettingsHandler;
    }

    private bool IsTerminalTranscriptBoundTo(
        RichTextBox transcript,
        TerminalTabViewModel terminal) =>
        _terminalTranscriptSubscriptions.TryGetValue(
            transcript,
            out var subscription)
        && ReferenceEquals(subscription.Terminal, terminal);

    private void TerminalTranscriptPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (sender is not RichTextBox transcript)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (eventArgs.Key == Key.C
            && modifiers.HasFlag(ModifierKeys.Control)
            && modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (!transcript.Selection.IsEmpty)
            {
                transcript.Copy();
            }

            eventArgs.Handled = true;
            return;
        }

        if ((eventArgs.Key == Key.V
             && modifiers.HasFlag(ModifierKeys.Control)
             && modifiers.HasFlag(ModifierKeys.Shift))
            || (eventArgs.Key == Key.Insert
                && modifiers.HasFlag(ModifierKeys.Shift)))
        {
            PasteIntoTerminal(transcript);
            eventArgs.Handled = true;
        }
    }

    private void TerminalCopyMenuItemClick(object sender, RoutedEventArgs eventArgs)
    {
        if (GetTranscriptFromMenuItem(sender) is { } transcript
            && !transcript.Selection.IsEmpty)
        {
            transcript.Copy();
        }
    }

    private void TerminalPasteMenuItemClick(object sender, RoutedEventArgs eventArgs)
    {
        if (GetTranscriptFromMenuItem(sender) is { } transcript)
        {
            PasteIntoTerminal(transcript);
        }
    }

    private void TerminalSelectAllMenuItemClick(object sender, RoutedEventArgs eventArgs)
    {
        if (GetTranscriptFromMenuItem(sender) is not { } transcript)
        {
            return;
        }

        transcript.Focus();
        transcript.SelectAll();
    }

    private static RichTextBox? GetTranscriptFromMenuItem(object sender)
    {
        if (sender is not MenuItem menuItem
            || ItemsControl.ItemsControlFromItemContainer(menuItem) is not ContextMenu contextMenu)
        {
            return null;
        }

        return contextMenu.PlacementTarget as RichTextBox;
    }

    private static void PasteIntoTerminal(RichTextBox transcript)
    {
        var root = VisualTreeHelper.GetParent(transcript);
        if (root is null
            || FindVisualChild<TextBox>(root, "InlineTerminalInput") is not { } input)
        {
            return;
        }

        input.Focus();
        input.Paste();
        input.CaretIndex = input.SelectionStart + input.SelectionLength;
    }

    private static bool IsTranscriptAtEnd(RichTextBox transcript)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(transcript);
        return scrollViewer is null
               || scrollViewer.ScrollableHeight <= 0
               || scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 24;
    }

    private static void RenderTerminalTranscript(
        RichTextBox transcript,
        TerminalTabViewModel terminal)
    {
        var mutedBrush = transcript.TryFindResource("MutedForegroundBrush") as Brush
                         ?? Brushes.Gray;
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = terminal.Settings.TerminalFontFamily,
            FontSize = terminal.Settings.TerminalFontSize,
            FontWeight = terminal.Settings.TerminalFontWeight,
            FontStyle = terminal.Settings.TerminalFontStyle,
            Foreground = terminal.Settings.ForegroundBrush
        };

        foreach (var line in terminal.Lines)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                LineHeight = Math.Max(12, terminal.Settings.TerminalFontSize + 3),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            if (terminal.Settings.ShowTimestamps)
            {
                paragraph.Inlines.Add(new Run($"{line.Timestamp:HH:mm:ss} ")
                {
                    Foreground = mutedBrush
                });
            }

            var foreground = line.Kind switch
            {
                TerminalLineKind.Input => terminal.Settings.AccentBrush,
                TerminalLineKind.Error => terminal.Settings.ErrorBrush,
                TerminalLineKind.System => mutedBrush,
                _ => terminal.Settings.ForegroundBrush
            };
            paragraph.Inlines.Add(new Run(line.Text)
            {
                Foreground = foreground,
                Background = terminal.Settings.TranscriptBackdropBrush
            });
            document.Blocks.Add(paragraph);
        }

        transcript.Document = document;
    }

    private void WorkspaceTabHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left
            || eventArgs.ClickCount != 2
            || sender is not FrameworkElement header
            || header.DataContext is not IWorkspaceTab tab
            || !tab.CanRename)
        {
            return;
        }

        tab.BeginRename();
        header.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (FindVisualChild<TextBox>(header, "TabTitleEditor") is { } editor)
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            });
        eventArgs.Handled = true;
    }

    private void WorkspaceTabsSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (sender is not TabControl { SelectedItem: { } selectedItem } tabControl)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (tabControl.ItemContainerGenerator.ContainerFromItem(selectedItem)
                    is TabItem selectedTab)
                {
                    selectedTab.BringIntoView();
                }
            }));
    }

    private void WorkspaceTabContextMenuOpening(object sender, ContextMenuEventArgs eventArgs)
    {
        if (sender is FrameworkElement { DataContext: IWorkspaceTab tab })
        {
            _viewModel.ActiveWorkspaceTab = tab;
        }
    }

    private void WorkspaceTabLiveAssistClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not ToggleButton
            {
                DataContext: IWorkspaceTab tab
            } toggle)
        {
            return;
        }

        _viewModel.SetWorkspaceTabLiveAssistance(
            tab,
            toggle.IsChecked == true);
        toggle.IsChecked = tab.IsAiLiveAssistEnabled;
        eventArgs.Handled = true;
    }

    private void EditTerminalTabClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not MenuItem { DataContext: TerminalTabViewModel terminal })
        {
            return;
        }

        var dialog = new NewTerminalDialog(terminal, _viewModel.TerminalFontFamilies)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.LaunchRequest is { } request)
        {
            _viewModel.UpdateTerminal(terminal, request);
        }
    }

    private async void CloseWorkspaceTabClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: IWorkspaceTab tab })
        {
            return;
        }

        eventArgs.Handled = true;
        if (tab is StudioProjectStartTabViewModel { HasUnsavedChanges: true }
            && MessageBox.Show(this, "Discard this project's unfinished toolset and Page-template choices?", "Unfinished project", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (tab is BrowserTabViewModel { IsStudioContentPage: true } page
            && !await ConfirmStudioPageSavedAsync(page)) return;
        if (tab is DocumentTabViewModel document
            && !await ConfirmCloseDocumentAsync(document))
        {
            return;
        }
        if (tab is VisualPageStudioTabViewModel visualPage
            && !await ConfirmCloseVisualPageAsync(visualPage))
        {
            return;
        }

        _viewModel.CloseWorkspaceTab(tab);
    }

    private void OpenNewItemMenu(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private void OpenProfileAccountMenuClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        eventArgs.Handled = true;
    }

    private void ChooseProfileImageClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Hypershell profile image",
            Filter =
                "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _viewModel.SaveProfileAvatar(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or NotSupportedException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Profile Image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RemoveProfileImageClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        try
        {
            _viewModel.RemoveProfileAvatar();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"The profile image could not be removed.\n\n{exception.Message}",
                "Profile Image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void NewDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        OpenNewDocumentDialog();
    }

    private void NewBrowserClick(object sender, RoutedEventArgs eventArgs)
    {
        _viewModel.CreateBrowser();
        eventArgs.Handled = true;
    }

    private void ReturnToHypershellClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _viewModel.ReturnToHypershell();
    }

    private void NewStudioProjectClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _viewModel.CreateStudioProjectStartTab();
    }

    private async void NewStudioObjectClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (sender is not MenuItem { Tag: string action }) return;
        var studio = _viewModel.ActiveWorkspaceTab as VisualPageStudioTabViewModel;
        var templateRoot = studio is { HasHyperlibAuthoring: true, HyperlibWorkspace.HasLibraryContent: false }
            ? studio.ProjectRoot : null;
        if (studio is { HasHyperlibAuthoring: false })
        {
            if (action == "new-work") NewVisualPageProjectClick(sender, eventArgs);
            else studio.StudioView.RequestHyperlibAction(action);
            return;
        }
        if (_viewModel.ActiveWorkspaceTab is BrowserTabViewModel { StudioProjectRoot: { } workRoot })
        {
            studio = await _viewModel.OpenVisualPageProjectAsync(workRoot);
            studio?.EnableHyperlibEditor();
        }
        if (studio?.HyperlibWorkspace is null || templateRoot is not null)
            studio = await _viewModel.OpenHyperlibEditorAsync();
        if (studio is null) return;
        if (templateRoot is not null && studio.HyperlibWorkspace is { } library)
            library.SelectedTemplate = library.AvailableTemplates.FirstOrDefault(template =>
                string.Equals(Path.GetFullPath(template.ProjectRoot), Path.GetFullPath(templateRoot), StringComparison.OrdinalIgnoreCase))
                ?? library.SelectedTemplate;
        studio.StudioView.RequestHyperlibAction(action);
    }

    private async void NewVisualPageProjectClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        try
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Choose a parent folder for the new Hyperpage project",
                Multiselect = false,
                InitialDirectory =
                    VisualPageProjectService.EnsureDefaultProjectsDirectory()
            };
            if (folderDialog.ShowDialog(this) != true)
            {
                return;
            }
            // The picker normally selects a parent. Make selecting an existing
            // Studio project explicit, especially the HyperLib library root.
            if (File.Exists(Path.Combine(folderDialog.FolderName,
                    VisualPageProjectService.ProjectManifestFileName)))
            {
                var choice = MessageBox.Show(this,
                    "This folder already contains a Hyperpage project.\n\n"
                    + "Yes — open the existing project\n"
                    + "No — create a separate project in a new subfolder\n"
                    + "Cancel — return without making changes",
                    "Existing Hyperpage project", MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question, MessageBoxResult.Yes);
                if (choice == MessageBoxResult.Cancel) return;
                if (choice == MessageBoxResult.Yes)
                {
                    await OpenVisualPageProjectWithFeedbackAsync(folderDialog.FolderName);
                    return;
                }
            }
            var creationDialog = new HyperpageProjectCreationDialog(
                folderDialog.FolderName,
                _viewModel.HyperpageCreationTemplates)
            {
                Owner = this
            };
            if (creationDialog.ShowDialog() != true
                || creationDialog.CreationOptions is null)
            {
                return;
            }
            var tab = await _viewModel.CreateVisualPageProjectAsync(
                folderDialog.FolderName,
                creationDialog.ProjectName,
                creationDialog.CreationOptions);
            if (tab is null)
            {
                MessageBox.Show(
                    this,
                    _viewModel.StatusMessage,
                    "New Hyperpage project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowVisualPageEntryError("Create project", exception.Message);
        }
    }

    private async void OpenVisualPageProjectClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        try
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Open a Hyperpage project folder",
                Multiselect = false,
                InitialDirectory =
                    VisualPageProjectService.EnsureDefaultProjectsDirectory()
            };
            if (folderDialog.ShowDialog(this) == true)
            {
                await OpenVisualPageProjectWithFeedbackAsync(folderDialog.FolderName);
            }
        }
        catch (Exception exception)
        {
            ShowVisualPageEntryError("Open project", exception.Message);
        }
    }

    private async void OpenHyperlibEditorClick(object sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        try
        {
            if (await _viewModel.OpenHyperlibEditorAsync() is null)
                MessageBox.Show(this, _viewModel.StatusMessage, "Hyperlib Editor",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Hyperlib Editor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task OpenVisualPageProjectWithFeedbackAsync(string path)
    {
        if (await _viewModel.OpenVisualPageProjectAsync(path) is null)
            ShowVisualPageEntryError("Open project", _viewModel.StatusMessage);
    }

    private void ShowVisualPageEntryError(string action, string message) =>
        MessageBox.Show(this, string.IsNullOrWhiteSpace(message)
                ? "The project could not be opened. Select a folder containing visual-page.project.json."
                : message,
            "Hyperpage Studio · " + action, MessageBoxButton.OK, MessageBoxImage.Warning);

    private async void SaveVisualPageProjectClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveVisualPage(sender) is { } visualPage)
        {
            await visualPage.SaveProjectAsync();
        }
        eventArgs.Handled = true;
    }

    private void DetachVisualPageStudioClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveVisualPage(sender) is { } visualPage)
        {
            ShowDetachedVisualPageStudio(visualPage);
        }
        eventArgs.Handled = true;
    }

    private void OnVisualPageDetachRequested(
        object? sender,
        VisualPageStudioTabViewModel visualPage) =>
        ShowDetachedVisualPageStudio(visualPage);

    private void ShowDetachedVisualPageStudio(
        VisualPageStudioTabViewModel visualPage)
    {
        if (_detachedVisualPageWindows.TryGetValue(
                visualPage.SessionId,
                out var existing))
        {
            existing.Activate();
            return;
        }
        var window = new VisualPageStudioDetachedWindow(visualPage, this);
        _detachedVisualPageWindows[visualPage.SessionId] = window;
        visualPage.IsDetached = true;
        window.Closed += (_, _) =>
            _detachedVisualPageWindows.Remove(visualPage.SessionId);
        window.Show();
    }

    private static VisualPageStudioTabViewModel? ResolveVisualPage(
        object sender) => sender switch
        {
            FrameworkElement
            {
                DataContext: VisualPageStudioTabViewModel visualPage
            } => visualPage,
            _ => null
        };

    private void OnBuilderEditorInsertionRequested(
        object? sender,
        EditorInsertionRequest request)
    {
        var activeDocument = _viewModel.ActiveDocument;
        if (activeDocument is not null
            && CommandLanguageRegistry.CanInsertIntoEditor(
                request.Language,
                activeDocument.LanguageId))
        {
            var activeEditor = FindVisualChild<MonacoEditorControl>(
                this,
                "DocumentEditor");
            if (activeEditor?.InsertText(request.Text) == true)
            {
                request.Succeeded = true;
                request.Message =
                    $"Inserted {request.Language} source into {activeDocument.Title}.";
                activeEditor.FocusEditor();
                return;
            }
        }

        var targetDocument = activeDocument is not null
                             && CommandLanguageRegistry.CanInsertIntoEditor(
                                 request.Language,
                                 activeDocument.LanguageId)
            ? activeDocument
            : _viewModel.WorkspaceTabs
                .OfType<DocumentTabViewModel>()
                .LastOrDefault(document =>
                    CommandLanguageRegistry.CanInsertIntoEditor(
                        request.Language,
                        document.LanguageId));
        if (targetDocument is not null)
        {
            var insertion = CreateDocumentInsertion(
                targetDocument,
                request.Text);
            var result = targetDocument.InsertThroughProvider(
                insertion.Offset,
                insertion.Text);
            if (!result.Succeeded)
            {
                request.Message = string.IsNullOrWhiteSpace(result.Message)
                    ? $"Unable to insert the {request.Language} source into {targetDocument.Title}."
                    : result.Message;
                return;
            }

            _viewModel.ActiveWorkspaceTab = targetDocument;
            request.Succeeded = true;
            request.Message =
                $"Inserted {request.Language} source into {targetDocument.Title}.";
            return;
        }

        var languageId =
            CommandLanguageRegistry.GetPreferredEditorLanguageId(
                request.Language);
        var language = EditorLanguageCatalog.GetLanguage(languageId);
        var fileName =
            _viewModel.GetNextUntitledDocumentBaseName()
            + language.DefaultExtension;
        var createdDocument = _viewModel.CreateUntitledDocument(
            new NewDocumentRequest(
                fileName,
                EditorLanguageCatalog.CreateDefaultSettings(language.Id),
                InitialContent: request.Text));

        request.Succeeded = true;
        request.Message =
            $"Created {createdDocument.Title} and inserted the composed {request.Language} source.";
    }

    private static (int Offset, string Text) CreateDocumentInsertion(
        DocumentTabViewModel document,
        string source)
    {
        var content = document.Content;
        var offset = content.Length;
        if (string.Equals(
                EditorLanguageCatalog.GetLanguage(document.LanguageId).Id,
                "html",
                StringComparison.OrdinalIgnoreCase))
        {
            offset = content.LastIndexOf(
                "</body",
                StringComparison.OrdinalIgnoreCase);
            if (offset < 0)
            {
                offset = content.LastIndexOf(
                    "</html",
                    StringComparison.OrdinalIgnoreCase);
            }

            if (offset < 0)
            {
                offset = content.Length;
            }
        }

        var lineEnding = ResolveDocumentLineEnding(document);
        var prefix = offset > 0
                     && content[offset - 1] is not '\r' and not '\n'
                     && !source.StartsWith('\r')
                     && !source.StartsWith('\n')
            ? lineEnding
            : string.Empty;
        var suffix = offset < content.Length
                     && content[offset] is not '\r' and not '\n'
                     && !source.EndsWith('\r')
                     && !source.EndsWith('\n')
            ? lineEnding
            : string.Empty;
        return (offset, prefix + source + suffix);
    }

    private static string ResolveDocumentLineEnding(
        DocumentTabViewModel document)
    {
        if (document.Content.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (document.Content.Contains('\n'))
        {
            return "\n";
        }

        if (document.Content.Contains('\r'))
        {
            return "\r";
        }

        return document.LineEndingKind == DocumentLineEndingKind.Preserve
            ? Environment.NewLine
            : TextDocumentService.GetLineEndingSequence(
                document.LineEndingKind);
    }

    private void OpenNewDocumentDialog()
    {
        var dialog = new NewDocumentDialog(
            _viewModel.GetNextUntitledDocumentBaseName(),
            _viewModel.TerminalFontFamilies)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.Request is { } request)
        {
            _viewModel.CreateUntitledDocument(request);
        }
    }

    private async void OpenDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        await OpenDocumentsFromDialogAsync();
    }

    private async void SaveDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            await TrySaveDocumentAsync(document);
            eventArgs.Handled = true;
        }
    }

    private async void SaveDocumentAsClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            await TrySaveDocumentAsync(document, forceSaveAs: true);
            eventArgs.Handled = true;
        }
    }

    private async void PreviewDocumentInBrowserClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is not { } document
            || !document.CanPreviewInBrowser)
        {
            return;
        }

        eventArgs.Handled = true;
        if ((!document.HasFilePath || document.HasUnsavedChanges)
            && !await TrySaveDocumentAsync(document))
        {
            return;
        }

        _viewModel.OpenWebPreview(document.LocalWorkingFilePath);
    }

    private async void RunDocumentClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is not { } document
            || !document.CanRunDocument)
        {
            return;
        }

        eventArgs.Handled = true;
        if ((!document.HasFilePath || document.HasUnsavedChanges)
            && !await TrySaveDocumentAsync(document))
        {
            return;
        }

        await _viewModel.RunDocumentAsync(document);
    }

    private async void ExportDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            eventArgs.Handled = true;
            await TryExportDocumentAsync(document);
        }
    }

    private async void FormatDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveEditor(sender) is { } editor
            && ResolveDocument(sender) is { } document)
        {
            eventArgs.Handled = true;
            await TryFormatDocumentAsync(document, editor);
        }
    }

    private void FindDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveEditor(sender) is { } editor)
        {
            editor.ShowFind();
            eventArgs.Handled = true;
        }
    }

    private void ReplaceDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveEditor(sender) is { } editor)
        {
            editor.ShowReplace();
            eventArgs.Handled = true;
        }
    }

    private void GoToLineDocumentClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveEditor(sender) is { } editor)
        {
            editor.ShowGoToLine();
            eventArgs.Handled = true;
        }
    }

    private void DocumentLanguageSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (sender is ComboBox
            {
                DataContext: DocumentTabViewModel document,
                SelectedValue: string languageId
            })
        {
            _viewModel.ChangeDocumentLanguage(document, languageId);
        }
    }

    private void MarkdownFormatClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { Tag: string style }
            && ResolveEditor(sender) is { } editor)
        {
            editor.ApplyMarkdownFormat(style);
            eventArgs.Handled = true;
        }
    }

    private void DocumentViewModeClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { Tag: string modeName }
            && ResolveDocument(sender) is { } document
            && Enum.TryParse<DocumentEditorViewMode>(
                modeName,
                ignoreCase: true,
                out var mode))
        {
            document.SetEditorViewMode(mode);
            eventArgs.Handled = true;
        }
    }

    private void DocumentFontFamilyClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { Tag: string fontFamily }
            && ResolveDocument(sender) is { } document)
        {
            document.SetEditorFontFamily(fontFamily);
            eventArgs.Handled = true;
        }
    }

    private void DocumentFontSizeClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { Tag: string sizeText }
            && int.TryParse(sizeText, out var fontSize)
            && ResolveDocument(sender) is { } document)
        {
            document.SetEditorFontSize(fontSize);
            eventArgs.Handled = true;
        }
    }

    private void ToggleDocumentWordWrapClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            document.ToggleWordWrap();
            eventArgs.Handled = true;
        }
    }

    private void ToggleDocumentLineNumbersClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            document.ToggleLineNumbers();
            eventArgs.Handled = true;
        }
    }

    private void ToggleDocumentMinimapClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            document.ToggleMinimap();
            eventArgs.Handled = true;
        }
    }

    private void DocumentEditorCommandCompleted(
        object sender,
        EditorCommandCompletedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Message))
        {
            _viewModel.ReportEditorStatus(eventArgs.Message);
        }

        eventArgs.Handled = true;
    }

    private async void DocumentEditorFormatRequested(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is MonacoEditorControl editor
            && ResolveDocument(sender) is { } document)
        {
            eventArgs.Handled = true;
            await TryFormatDocumentAsync(document, editor);
        }
    }

    private async Task TryFormatDocumentAsync(
        DocumentTabViewModel document,
        MonacoEditorControl editor)
    {
        var result = await _viewModel.FormatDocumentAsync(document);
        if (_isWindowFinalized)
        {
            return;
        }

        if (result.ShouldUseEditorFallback)
        {
            editor.FormatInEditor();
        }
        else
        {
            editor.FocusEditor();
        }
    }

    private void DocumentEditorNewDocumentRequested(
        object sender,
        RoutedEventArgs eventArgs)
    {
        OpenNewDocumentDialog();
        eventArgs.Handled = true;
    }

    private async void DocumentEditorOpenDocumentRequested(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await OpenDocumentsFromDialogAsync();
        eventArgs.Handled = true;
    }

    private async void DocumentEditorSaveRequested(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            await TrySaveDocumentAsync(document);
            eventArgs.Handled = true;
        }
    }

    private async void DocumentEditorSaveAsRequested(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveDocument(sender) is { } document)
        {
            await TrySaveDocumentAsync(document, forceSaveAs: true);
            eventArgs.Handled = true;
        }
    }

    private void OpenNewTerminalDialog(object sender, RoutedEventArgs eventArgs)
    {
        _viewModel.RefreshTerminalProfiles();
        var terminalProfiles = _viewModel.GetNewTabTerminalProfiles();
        var selectedTerminalProfile = terminalProfiles.First(profile =>
            string.Equals(
                profile.Id,
                _viewModel.SelectedTerminalProfile.Id,
                StringComparison.OrdinalIgnoreCase));
        var dialog = new NewTerminalDialog(
            terminalProfiles,
            selectedTerminalProfile,
            _viewModel.TerminalFontFamilies)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.LaunchRequest is { } request)
        {
            _viewModel.CreateTerminal(request);
        }
    }

    private async void OpenNewRemoteTerminalDialog(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            var inventory = await _viewModel.LoadLinkedHostInventoryAsync();
            var dialog = new NewRemoteTerminalDialog(inventory)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true
                && dialog.LaunchRequest is { } request)
            {
                _viewModel.CreateRemoteTerminal(request);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Remote devices could not be loaded.\n\n{exception.Message}",
                "Remote Terminal",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenSettingsWindowClick(object sender, RoutedEventArgs eventArgs)
    {
        OpenSettingsWindow(0);
    }

    private void ShowProfileLoginGateClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _viewModel.ShowProfileLoginGate();
        eventArgs.Handled = true;
    }

    private void OpenProfileSyncSettingsClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        OpenSettingsWindow(6);
        eventArgs.Handled = true;
    }

    private void OpenLinkedHostsSettingsClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        try
        {
            var settingsWindow = OpenSettingsWindow(6);
            settingsWindow?.ShowLinkedHosts(beginNewHost: true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Linked SSH host settings could not be opened.\n\n{exception.Message}",
                "Linked SSH Hosts",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void BeginProfileLoginClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_viewModel.BeginProfileLogin())
        {
            return;
        }

        _profileLoginCancellation?.Cancel();
        _profileLoginCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _profileLoginCancellation = cancellation;
        try
        {
            _ = await _viewModel.AuthorizeProfileSyncDeviceAsync(
                ProfileSyncEndpoint.ProductionEndpoint,
                challenge => Dispatcher.Invoke(() =>
                {
                    _viewModel.ShowProfileLoginChallenge(challenge);
                    OpenProfileAuthorizationPortal(challenge);
                }),
                cancellation.Token);
            _viewModel.CompleteProfileLogin();
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == ProfileSyncFailureKind.Cancelled)
        {
            _viewModel.FailProfileLogin(
                "Sign-in was cancelled. You can try again or continue offline.");
        }
        catch (Exception exception)
        {
            _viewModel.FailProfileLogin(
                $"Hypershell could not complete sign-in: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_profileLoginCancellation, cancellation))
            {
                _profileLoginCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelProfileLoginClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _viewModel.ReportProfileLoginCancellation();
        _profileLoginCancellation?.Cancel();
        eventArgs.Handled = true;
    }

    private void ContinueWithoutProfileLoginClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _viewModel.ContinueWithoutProfileLogin();
        eventArgs.Handled = true;
    }

    private void OpenProfileAuthorizationPortal(
        DeviceAuthorizationResponse challenge)
    {
        var uri = HttpCloudDeviceAuthorizationClient
            .CreateCompleteVerificationUri(challenge);
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            _viewModel.ReportProfileLoginPortalFailure(
                $"The browser could not be opened automatically. Open {uri.AbsoluteUri}; after password and authenticator sign-in, Hypershell will continue automatically.");
        }
    }

    private async void SignOutProfileSyncFromHeaderClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_viewModel.CanProfileSyncSignOut)
        {
            return;
        }

        eventArgs.Handled = true;
        try
        {
            await _viewModel.SignOutProfileSyncAsync(
                forgetCredential: true);
            _viewModel.ShowProfileLoginGate(
                "You’re logged out. Sign in again or continue with the local workspace.");
        }
        catch (Exception exception) when (exception is ProfileSyncException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Hypershell could not log out cleanly.\n\n{exception.Message}",
                "Hypershell Account",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenWorkspaceAiSettingsClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        OpenSettingsWindow(7);

    private void CreateWorkspaceAiContextClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var assistant = _viewModel.WorkspaceAiAssistant;
        if (!assistant.CanChangeContext)
        {
            return;
        }

        OpenWorkspaceAiContextDialog(
            assistant.CreateContextDraft(),
            isNew: true);
    }

    private void EditWorkspaceAiContextClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var assistant = _viewModel.WorkspaceAiAssistant;
        if (!assistant.CanChangeContext
            || assistant.SelectedContextDefinition is not { } context)
        {
            return;
        }

        OpenWorkspaceAiContextDialog(context, isNew: false);
    }

    private void BrowseWorkspaceAiWorkingDirectoryClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var assistant = _viewModel.WorkspaceAiAssistant;
        if (!assistant.CanChangeWorkingDirectory)
        {
            return;
        }

        var initialDirectory = Directory.Exists(assistant.WorkingDirectory)
            ? assistant.WorkingDirectory
            : Directory.Exists(_viewModel.FileExplorer.CurrentPath)
                ? _viewModel.FileExplorer.CurrentPath
                : Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the AI project working directory",
            Multiselect = false,
            InitialDirectory = initialDirectory
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!assistant.TrySetWorkingDirectory(
                dialog.FolderName,
                out var error))
        {
            MessageBox.Show(
                this,
                error,
                "Working directory could not be changed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearWorkspaceAiWorkingDirectoryClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var assistant = _viewModel.WorkspaceAiAssistant;
        if (assistant.CanChangeWorkingDirectory)
        {
            assistant.TrySetWorkingDirectory(string.Empty, out _);
        }
    }

    private void OpenWorkspaceAiContextDialog(
        WorkspaceAiContextDefinition context,
        bool isNew)
    {
        var dialog = new WorkspaceAiContextDialog(
            context,
            isNew,
            _viewModel.GetWorkspaceAiWorkspaceDescriptors())
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result)
        {
            return;
        }

        try
        {
            _viewModel.WorkspaceAiAssistant.SaveContext(result);
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Context could not be saved",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void WorkspaceAiPromptPreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not Key.Enter and not Key.Return
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        eventArgs.Handled = true;
        var command = _viewModel.WorkspaceAiAssistant.SendCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
        else
        {
            _viewModel.WorkspaceAiAssistant.ExplainSendAvailability();
        }
    }

    private void OnWorkspaceAiMessagesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs) =>
        ScrollWorkspaceAiMessagesToEnd();

    private void WorkspaceAiMessagesScrollViewerIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.NewValue is true)
        {
            ScrollWorkspaceAiMessagesToEnd();
        }
    }

    private void ScrollWorkspaceAiMessagesToEnd()
    {
        if (!IsLoaded)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => WorkspaceAiMessagesScrollViewer.ScrollToEnd());
    }

    private void CreateWorkspaceAiConversationClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _viewModel.CreateWorkspaceAiConversation();
    }

    private void CloseWorkspaceAiConversationClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement
            {
                DataContext: WorkspaceAiConversationTabViewModel conversation
            })
        {
            return;
        }

        _viewModel.CloseWorkspaceAiConversation(conversation);
    }

    private async void ApproveWorkspaceAiReviewClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var assistant = _viewModel.WorkspaceAiAssistant;
        if (assistant.PendingReview is not { } review
            || !assistant.CanReview)
        {
            return;
        }

        if (review.Kind == WorkspaceAiPendingActionKind.TerminalCommand
            && !review.RequiresNativeConfirmation)
        {
            await assistant.ApprovePendingAsync(
                userConfirmedExecution: false);
            return;
        }

        var consequence = review.Kind
            == WorkspaceAiPendingActionKind.VisualPagePreviewBuild
            ? "This will build an isolated project snapshot. Project C#, packages, and MSBuild targets run with your current Windows permissions; it will not host or publish the page."
            : review.Kind == WorkspaceAiPendingActionKind.VisualPageMutation
                ? "This will apply only the displayed semantic operations to the exact visual-page revision and leave the project unsaved."
            : review.ExecutesCommand
            ? "This will execute the displayed command in a normal shell with your Windows permissions."
            : review.Kind == WorkspaceAiPendingActionKind.TerminalCommand
                ? "This will stage the displayed command without executing it."
                : review.Kind == WorkspaceAiPendingActionKind.WorkspaceTextDelete
                    ? "This will permanently delete the displayed file."
                : review.Kind == WorkspaceAiPendingActionKind.GitOperation
                    ? "This will execute the exact displayed Git operation through Hypershell's local or linked-SSH repository adapter."
                : review.Kind == WorkspaceAiPendingActionKind.OpenDocumentReplace
                    ? "This will replace the live editor content through its typed provider and leave the document unsaved."
                    : "This will atomically create or replace the reviewed workspace text file.";
        var result = MessageBox.Show(
            this,
            $"Approve the exact AI action shown in the review card?\n\n"
            + $"Target: {review.Target}\n\n"
            + consequence
            + "\n\nIf the content or target changed after review, Hypershell will reject the stale action.",
            review.ExecutesCommand
                ? review.Kind == WorkspaceAiPendingActionKind.VisualPagePreviewBuild
                    ? "Trust and build Hyperpage preview"
                    : "Confirm AI terminal execution"
                : "Approve AI workspace action",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await assistant.ApprovePendingAsync(
                userConfirmedExecution:
                    review.RequiresNativeConfirmation);
        }
    }

    private async void RejectWorkspaceAiReviewClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.WorkspaceAiAssistant.CanReview)
        {
            await _viewModel.WorkspaceAiAssistant.RejectPendingAsync();
        }
    }

    private void ToggleWorkspaceCustomizationClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _workspaceCustomizationButton = sender as Button;
        if (_isWorkspaceCustomizationActive)
        {
            FinishWorkspaceCustomization();
            return;
        }

        BeginWorkspaceCustomization();
    }

    private void BeginWorkspaceCustomization()
    {
        if (_isWorkspaceCustomizationActive)
        {
            return;
        }

        _workspaceCustomizationBaseline = _viewModel.WorkspaceLayout.Clone();
        SetWorkspaceCustomizationActive(true);
        WorkspaceCustomizationStatusText.Text =
            "Drag an approved extension panel by its highlighted header.";
    }

    private void CancelWorkspaceCustomizationClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        CancelWorkspaceCustomization();

    private void CancelWorkspaceCustomization()
    {
        if (!_isWorkspaceCustomizationActive)
        {
            return;
        }

        var baseline = _workspaceCustomizationBaseline;
        SetWorkspaceCustomizationActive(false);
        _workspaceCustomizationBaseline = null;
        if (baseline is not null)
        {
            _viewModel.ApplyWorkspaceLayout(baseline, persist: false);
        }
    }

    private void FinishWorkspaceCustomizationClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        FinishWorkspaceCustomization();

    private void FinishWorkspaceCustomization()
    {
        SetWorkspaceCustomizationActive(false);
        _workspaceCustomizationBaseline = null;
    }

    private void SaveWorkspaceCustomizationClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _viewModel.SaveCurrentWorkspaceLayout();
        SetWorkspaceCustomizationActive(false);
        _workspaceCustomizationBaseline = null;
    }

    private void OpenWorkspaceLayoutSettingsFromCustomizationClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        FinishWorkspaceCustomization();
        OpenSettingsWindow(3);
    }

    private void SetWorkspaceCustomizationActive(bool isActive)
    {
        _isWorkspaceCustomizationActive = isActive;
        WorkspaceCustomizationOverlay.Visibility = isActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        FileExplorerPanelHost.IsCustomizationEnabled = isActive;
        CommandBuilderPanelHost.IsCustomizationEnabled = isActive;
        BottomPanelHost.IsCustomizationEnabled = isActive;
        foreach (var panelView in _extensionPanelViews.Values)
        {
            panelView.IsCustomizationEnabled = isActive;
        }

        if (isActive)
        {
            if (_workspaceCustomizationButton is { } button)
            {
                button.Background = TryFindResource("SecondaryTintBrush") as Brush;
                button.ToolTip = "Finish workspace customization";
                System.Windows.Automation.AutomationProperties.SetName(
                    button,
                    "Finish workspace customization");
            }

            UpdateWorkspaceDropTargetStates(candidate: null);
            return;
        }

        _activePanelDragCandidate = null;
        _pendingPanelMoveTarget = null;
        if (_workspaceCustomizationButton is { } customizationButton)
        {
            customizationButton.ClearValue(Button.BackgroundProperty);
            customizationButton.ToolTip = "Customize workspace layout";
            System.Windows.Automation.AutomationProperties.SetName(
                customizationButton,
                "Customize workspace layout");
        }
    }

    private void OnExtensionPanelDragRequested(object? sender, EventArgs eventArgs)
    {
        if (!_isWorkspaceCustomizationActive
            || sender is not ExtensionPanelView panelView
            || !panelView.Candidate.Registration.Contribution.Placement.AllowUserMove)
        {
            return;
        }

        var candidate = panelView.Candidate;
        _activePanelDragCandidate = candidate;
        _pendingPanelMoveTarget = null;
        WorkspaceCustomizationStatusText.Text =
            $"Moving {candidate.Registration.Contribution.DisplayName}. Choose an allowed host.";
        UpdateWorkspaceDropTargetStates(candidate);

        WorkspacePanelMoveTarget? acceptedTarget;
        try
        {
            var dragData = new DataObject();
            dragData.SetData(
                ExtensionPanelDragDataFormat,
                candidate.PanelId);
            DragDrop.DoDragDrop(
                panelView,
                dragData,
                DragDropEffects.Move);
        }
        finally
        {
            acceptedTarget = _pendingPanelMoveTarget;
            _pendingPanelMoveTarget = null;
            _activePanelDragCandidate = null;
            UpdateWorkspaceDropTargetStates(candidate: null);
        }

        if (acceptedTarget is { } target)
        {
            MoveExtensionPanel(candidate, target);
        }
        else if (_isWorkspaceCustomizationActive)
        {
            WorkspaceCustomizationStatusText.Text =
                "Move canceled. Drag an approved panel by its highlighted header.";
        }
    }

    private void WorkspacePanelDropTargetDragEnter(
        object sender,
        DragEventArgs eventArgs) =>
        UpdateWorkspacePanelDragEffect(sender, eventArgs, highlight: true);

    private void WorkspacePanelDropTargetDragOver(
        object sender,
        DragEventArgs eventArgs) =>
        UpdateWorkspacePanelDragEffect(sender, eventArgs, highlight: true);

    private void WorkspacePanelDropTargetDragLeave(
        object sender,
        DragEventArgs eventArgs)
    {
        UpdateWorkspaceDropTargetStates(_activePanelDragCandidate);
        eventArgs.Handled = true;
    }

    private void WorkspacePanelDropTargetDrop(
        object sender,
        DragEventArgs eventArgs)
    {
        if (sender is Border targetBorder
            && TryResolveMoveTarget(targetBorder, out var target)
            && TryResolveDraggedPanel(eventArgs, out var candidate)
            && candidate is not null
            && CanMoveExtensionPanel(candidate, target, out _))
        {
            _pendingPanelMoveTarget = target;
            eventArgs.Effects = DragDropEffects.Move;
        }
        else
        {
            eventArgs.Effects = DragDropEffects.None;
        }

        eventArgs.Handled = true;
    }

    private void UpdateWorkspacePanelDragEffect(
        object sender,
        DragEventArgs eventArgs,
        bool highlight)
    {
        if (sender is Border targetBorder
            && TryResolveMoveTarget(targetBorder, out var target)
            && TryResolveDraggedPanel(eventArgs, out var candidate)
            && candidate is not null
            && CanMoveExtensionPanel(candidate, target, out _))
        {
            eventArgs.Effects = DragDropEffects.Move;
            if (highlight)
            {
                UpdateWorkspaceDropTargetStates(candidate);
                targetBorder.Background =
                    TryFindResource("SecondaryTintBrush") as Brush;
                targetBorder.BorderBrush =
                    TryFindResource("SecondaryBrush") as Brush;
                targetBorder.BorderThickness = new Thickness(2);
            }
        }
        else
        {
            eventArgs.Effects = DragDropEffects.None;
        }

        eventArgs.Handled = true;
    }

    private bool TryResolveDraggedPanel(
        DragEventArgs eventArgs,
        out ExtensionPanelCandidate? candidate)
    {
        candidate = _activePanelDragCandidate;
        if (!_isWorkspaceCustomizationActive
            || candidate is null
            || !eventArgs.Data.GetDataPresent(ExtensionPanelDragDataFormat)
            || eventArgs.Data.GetData(ExtensionPanelDragDataFormat) is not string panelId)
        {
            return false;
        }

        return string.Equals(
            panelId,
            candidate.PanelId,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveMoveTarget(
        Border targetBorder,
        out WorkspacePanelMoveTarget target) =>
        Enum.TryParse(
            targetBorder.Tag as string,
            ignoreCase: true,
            out target);

    private bool CanMoveExtensionPanel(
        ExtensionPanelCandidate candidate,
        WorkspacePanelMoveTarget target,
        out string reason)
    {
        var (replacement, slot) = ResolveMoveTarget(target);
        return _viewModel.CanMoveExtensionPanel(
            candidate.PanelId,
            replacement,
            slot,
            out reason);
    }

    private void MoveExtensionPanel(
        ExtensionPanelCandidate candidate,
        WorkspacePanelMoveTarget target)
    {
        var (replacement, slot) = ResolveMoveTarget(target);
        _viewModel.MoveExtensionPanel(
            candidate.PanelId,
            replacement,
            slot,
            out var statusMessage);
        if (_isWorkspaceCustomizationActive)
        {
            WorkspaceCustomizationStatusText.Text = statusMessage;
        }
    }

    private static (BuiltInWorkspaceSurface? Replacement, WorkspacePanelSlot Slot)
        ResolveMoveTarget(WorkspacePanelMoveTarget target) =>
        target switch
        {
            WorkspacePanelMoveTarget.FileExplorer =>
                (BuiltInWorkspaceSurface.FileExplorer, WorkspacePanelSlot.Left),
            WorkspacePanelMoveTarget.CommandBuilder =>
                (BuiltInWorkspaceSurface.CommandBuilder, WorkspacePanelSlot.Right),
            WorkspacePanelMoveTarget.Bottom =>
                (null, WorkspacePanelSlot.Bottom),
            WorkspacePanelMoveTarget.Floating =>
                (null, WorkspacePanelSlot.Floating),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

    private void UpdateWorkspaceDropTargetStates(
        ExtensionPanelCandidate? candidate)
    {
        WorkspaceDropPalette.Opacity = candidate is null ? 0.72 : 1;
        foreach (var (target, targetBorder) in GetWorkspaceDropTargets())
        {
            var reason = string.Empty;
            var isAllowed = candidate is not null
                            && CanMoveExtensionPanel(
                                candidate,
                                target,
                                out reason);
            targetBorder.Background =
                TryFindResource("PanelRaisedBrush") as Brush;
            targetBorder.BorderBrush =
                TryFindResource("BorderBrush") as Brush;
            targetBorder.BorderThickness = new Thickness(1);
            targetBorder.Opacity = candidate is null
                ? 0.72
                : isAllowed
                    ? 1
                    : 0.38;
            targetBorder.ToolTip = candidate is null
                ? "Start dragging a movable extension panel to inspect this target."
                : isAllowed
                    ? "This target is allowed by the panel manifest."
                    : reason;
        }
    }

    private IEnumerable<(WorkspacePanelMoveTarget Target, Border TargetBorder)>
        GetWorkspaceDropTargets()
    {
        yield return (
            WorkspacePanelMoveTarget.FileExplorer,
            FileExplorerDropTarget);
        yield return (
            WorkspacePanelMoveTarget.CommandBuilder,
            CommandBuilderDropTarget);
        yield return (WorkspacePanelMoveTarget.Bottom, BottomDropTarget);
        yield return (WorkspacePanelMoveTarget.Floating, FloatingDropTarget);
    }

    private SettingsWindow? OpenSettingsWindow(int sectionIndex)
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.SelectSection(sectionIndex);
            _settingsWindow.Activate();
            return _settingsWindow;
        }

        try
        {
            _settingsWindow = new SettingsWindow(_viewModel, sectionIndex)
            {
                Owner = this
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            return _settingsWindow;
        }
        catch (Exception exception)
        {
            _settingsWindow = null;
            MessageBox.Show(
                this,
                $"Settings could not be opened.\n\n{exception.Message}",
                "Hypershell Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }
    }

    private void TerminalTabTitleEditorPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (sender is not TextBox { DataContext: IWorkspaceTab tab }
            || !tab.CanRename)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Enter:
                tab.CommitRename();
                Keyboard.ClearFocus();
                eventArgs.Handled = true;
                break;

            case Key.Escape:
                tab.CancelRename();
                Keyboard.ClearFocus();
                eventArgs.Handled = true;
                break;
        }
    }

    private void TerminalTabTitleEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs eventArgs)
    {
        if (sender is TextBox { DataContext: IWorkspaceTab tab }
            && tab.CanRename)
        {
            tab.CommitRename();
        }
    }

    private async Task OpenDocumentsFromDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open document",
            Filter = _viewModel.CreateOpenDocumentDialogFilter(),
            CheckFileExists = true,
            Multiselect = true
        };
        if (Directory.Exists(_viewModel.FileExplorer.CurrentPath))
        {
            dialog.InitialDirectory = _viewModel.FileExplorer.CurrentPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var fileName in dialog.FileNames)
        {
            await OpenDocumentPathAsync(fileName);
        }
    }

    private async Task OpenDocumentPathAsync(string path)
    {
        await OpenDocumentLocationAsync(WorkspaceLocation.Local(path));
    }

    private async Task OpenDocumentLocationAsync(WorkspaceLocation location)
    {
        if (string.Equals(
                Path.GetExtension(location.Path),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            if (await _viewModel.OpenPdfDocumentAsync(location) is not null)
            {
                return;
            }

            MessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
                    ? "Hypershell could not open this PDF."
                    : _viewModel.StatusMessage,
                "Unable to open PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (await _viewModel.OpenDocumentAsync(location) is not null)
        {
            return;
        }

        MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
                ? "Hypershell could not open this document."
                : _viewModel.StatusMessage,
            "Unable to open document",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task<bool> TrySaveDocumentAsync(
        DocumentTabViewModel document,
        bool forceSaveAs = false)
    {
        if (document.IsRemoteDocument && !forceSaveAs)
        {
            var remoteResult = await _viewModel.SaveRemoteDocumentAsync(
                document,
                overwriteWithoutVersion: false);
            if (remoteResult.Succeeded)
            {
                return true;
            }

            if (remoteResult.Outcome == WorkspaceDocumentSaveOutcome.Conflict)
            {
                return await ResolveRemoteDocumentConflictAsync(
                    document,
                    remoteResult.Message);
            }

            MessageBox.Show(
                this,
                remoteResult.Message,
                "Unable to save remote document",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        var targetPath = document.FilePath;
        if (forceSaveAs || string.IsNullOrWhiteSpace(targetPath))
        {
            var extension = Path.GetExtension(document.Title).TrimStart('.');
            var dialog = new SaveFileDialog
            {
                Title = forceSaveAs ? "Save document as" : "Save document",
                Filter = CreateSaveDocumentDialogFilter(document),
                FileName = document.Title,
                AddExtension = true,
                OverwritePrompt = true,
                DefaultExt = string.IsNullOrWhiteSpace(extension)
                    ? ".txt"
                    : $".{extension}"
            };
            if (Directory.Exists(_viewModel.FileExplorer.CurrentPath))
            {
                dialog.InitialDirectory = _viewModel.FileExplorer.CurrentPath;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            targetPath = dialog.FileName;
        }

        if (await _viewModel.SaveDocumentAsync(document, targetPath))
        {
            return true;
        }

        MessageBox.Show(
            this,
            string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
                ? "Hypershell could not save this document."
                : _viewModel.StatusMessage,
            "Unable to save document",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        return false;
    }

    private async Task<bool> ResolveRemoteDocumentConflictAsync(
        DocumentTabViewModel document,
        string message)
    {
        var dialog = new RemoteDocumentConflictDialog(
            document.Title,
            document.DisplayPath,
            message)
        {
            Owner = this
        };
        _ = dialog.ShowDialog();
        switch (dialog.Resolution)
        {
            case RemoteDocumentConflictResolution.Reload:
                return await _viewModel.ReloadRemoteDocumentAsync(document);
            case RemoteDocumentConflictResolution.Overwrite:
                {
                    var overwrite = await _viewModel.SaveRemoteDocumentAsync(
                        document,
                        overwriteWithoutVersion: true);
                    if (overwrite.Succeeded)
                    {
                        return true;
                    }

                    MessageBox.Show(
                        this,
                        overwrite.Message,
                        "Unable to overwrite remote document",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }
            case RemoteDocumentConflictResolution.SaveCopy:
                return await TrySaveDocumentAsync(document, forceSaveAs: true);
            default:
                return false;
        }
    }

    private async Task<bool> TryExportDocumentAsync(DocumentTabViewModel document)
    {
        if (document.ExportFormats.Count == 0)
        {
            MessageBox.Show(
                this,
                $"{document.ProviderDisplayName} does not currently provide an export format for this document.",
                "Export unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var formats = document.ExportFormats;
        var baseName = Path.GetFileNameWithoutExtension(document.Title);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Document";
        }

        var dialog = new SaveFileDialog
        {
            Title = $"Export {document.Title}",
            Filter = string.Join(
                '|',
                formats.Select(format =>
                    $"{format.DisplayName} (*{format.FileExtension})|*{format.FileExtension}")),
            FilterIndex = 1,
            FileName = baseName,
            AddExtension = true,
            OverwritePrompt = true,
            ValidateNames = true
        };
        var sourceDirectory = document.HasFilePath
            ? Path.GetDirectoryName(document.LocalWorkingFilePath)
            : null;
        if (!string.IsNullOrWhiteSpace(sourceDirectory)
            && Directory.Exists(sourceDirectory))
        {
            dialog.InitialDirectory = sourceDirectory;
        }
        else if (Directory.Exists(_viewModel.FileExplorer.CurrentPath))
        {
            dialog.InitialDirectory = _viewModel.FileExplorer.CurrentPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        var selectedIndex = Math.Clamp(dialog.FilterIndex - 1, 0, formats.Count - 1);
        var selectedFormat = formats[selectedIndex];
        var result = await _viewModel.ExportDocumentAsync(
            document,
            selectedFormat.Id,
            dialog.FileName);
        if (result.Succeeded)
        {
            return true;
        }

        if (result.Outcome != DocumentExportOutcome.Cancelled)
        {
            MessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(result.Message)
                    ? "Hypershell could not export this document."
                    : result.Message,
                result.Outcome == DocumentExportOutcome.Rejected
                    ? "Export rejected"
                    : "Unable to export document",
                MessageBoxButton.OK,
                result.Outcome == DocumentExportOutcome.Rejected
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Error);
        }

        return false;
    }

    private async Task<bool> ConfirmCloseDocumentAsync(
        DocumentTabViewModel document,
        ICollection<DocumentTabViewModel>? discardedDocuments = null)
    {
        if (!document.HasUnsavedChanges)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            $"Save changes to {document.Title} before closing it?",
            "Unsaved document",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            discardedDocuments?.Add(document);
            return true;
        }

        return await TrySaveDocumentAsync(document);
    }

    private async Task<bool> ConfirmCloseAllDocumentsAsync()
    {
        foreach (var page in _viewModel.WorkspaceTabs.OfType<BrowserTabViewModel>().Where(page => page.IsStudioContentPage).ToList())
            if (!await ConfirmStudioPageSavedAsync(page)) return false;
        var discardedDocuments = new List<DocumentTabViewModel>();
        var unsavedDocuments = _viewModel.WorkspaceTabs
            .OfType<DocumentTabViewModel>()
            .Where(document => document.HasUnsavedChanges)
            .ToList();
        foreach (var document in unsavedDocuments)
        {
            _viewModel.ActiveWorkspaceTab = document;
            if (!await ConfirmCloseDocumentAsync(document, discardedDocuments))
            {
                return false;
            }
        }

        foreach (var document in discardedDocuments)
        {
            _viewModel.ExcludeDocumentFromSession(document);
        }

        var visualPages = _viewModel.WorkspaceTabs
            .OfType<VisualPageStudioTabViewModel>()
            .Where(visualPage => visualPage.HasUnsavedChanges
                || visualPage.PrepareHyperlibContentToLeaveAsync is not null)
            .ToList();
        foreach (var start in _viewModel.WorkspaceTabs.OfType<StudioProjectStartTabViewModel>().Where(tab => tab.HasUnsavedChanges))
        {
            if (MessageBox.Show(this, "Discard the unfinished toolset and Page-template choices before closing Studio?", "Unfinished project", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                _viewModel.ActiveWorkspaceTab = start;
                return false;
            }
        }
        foreach (var visualPage in visualPages)
        {
            _viewModel.ActiveWorkspaceTab = visualPage;
            if (!await ConfirmCloseVisualPageAsync(visualPage))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> ConfirmCloseVisualPageAsync(
        VisualPageStudioTabViewModel visualPage)
    {
        if (visualPage.PrepareHyperlibContentToLeaveAsync is { } prepareContent && !await prepareContent())
        {
            _viewModel.ActiveWorkspaceTab = visualPage;
            return false;
        }
        if (visualPage.HyperlibWorkspace is { HasUnsavedAppearanceChanges: true } hyperlib)
        {
            var discard = MessageBox.Show(this,
                "This workspace has unsaved Work appearance changes.\n\n"
                + "Yes — discard appearance drafts and continue closing\n"
                + "No — return to the Work workspace to save them",
                "Unsaved Work appearance", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
            if (discard != MessageBoxResult.Yes)
            {
                _viewModel.ActiveWorkspaceTab = visualPage;
                visualPage.StudioView.RequestHyperlibAction("work");
                return false;
            }
            hyperlib.DiscardAppearanceDrafts();
        }
        if (!visualPage.HasUnsavedChanges)
        {
            return true;
        }
        var result = MessageBox.Show(
            this,
            $"Save changes to {visualPage.Title} before closing it?",
            "Unsaved Hyperpage project",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.No => true,
            MessageBoxResult.Yes => await visualPage.SaveProjectAsync(),
            _ => false
        };
    }

    private async Task<bool> ConfirmStudioPageSavedAsync(BrowserTabViewModel page)
    {
        if (await page.BrowserView.TryPrepareForStudioCloseAsync()) return true;
        _viewModel.ActiveWorkspaceTab = page;
        MessageBox.Show(this, "This Page still has unsaved changes. Resolve the save notice in the Page editor before closing it. Your draft remains open.",
            "Page changes need attention", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private async Task<bool> StopAtlasMyNetAppsForCloseAsync()
    {
        var results = await _viewModel.StopAtlasMyNetSessionAppsAsync();
        var unresolved = results.Where(result =>
            result.Outcome is not AtlasMyNetStopOutcome.Stopped
                and not AtlasMyNetStopOutcome.AlreadyStopped).ToList();
        if (unresolved.Count == 0)
        {
            return true;
        }

        var choice = MessageBox.Show(
            this,
            "Some local apps could not be stopped:\n\n"
            + string.Join("\n", unresolved.Select(result => $"• {result.Message}"))
            + "\n\nClose Hypershell and leave these apps running?",
            "Some apps are still running",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice == MessageBoxResult.Yes)
        {
            _viewModel.LeaveAtlasMyNetSessionAppsRunning();
            return true;
        }

        return false;
    }

    private DocumentTabViewModel? ResolveDocument(object sender)
    {
        if (sender is FrameworkElement
            {
                DataContext: DocumentTabViewModel document
            })
        {
            return document;
        }

        return ResolveContextMenuOwner(sender)?.DataContext
                   as DocumentTabViewModel
               ?? _viewModel.ActiveDocument;
    }

    private static MonacoEditorControl? ResolveEditor(object sender)
    {
        if (sender is Button { CommandParameter: MonacoEditorControl editor })
        {
            return editor;
        }

        return ResolveContextMenuOwner(sender)?.CommandParameter
            as MonacoEditorControl;
    }

    private static Button? ResolveContextMenuOwner(object sender)
    {
        if (sender is Button button)
        {
            return button;
        }

        return sender is MenuItem menuItem
               && ItemsControl.ItemsControlFromItemContainer(menuItem)
                   is ContextMenu menu
            ? menu.PlacementTarget as Button
            : null;
    }

    private const string CodeAndTextDocumentPatterns =
        "*.cs;*.csx;*.ps1;*.psm1;*.py;*.js;*.jsx;*.ts;*.tsx;*.java;*.c;*.h;*.cpp;*.hpp;*.go;*.rs;*.rb;*.php;*.swift;*.kt;*.kts;*.html;*.htm;*.css;*.scss;*.less;*.json;*.jsonc;*.md;*.markdown;*.xml;*.xaml;*.yaml;*.yml;*.toml;*.ini;*.sql;*.sh;*.bash;*.txt;*.log;*.csv";

    private static string CreateSaveDocumentDialogFilter(
        DocumentTabViewModel document)
    {
        var language = EditorLanguageCatalog.GetLanguage(document.LanguageId);
        var extensions = document.ProviderFileExtensions
            .Concat(language.FileExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var languagePatterns = string.Join(
            ';',
            extensions.Select(extension => $"*{extension}"));
        return $"{language.DisplayName} ({languagePatterns})|{languagePatterns}"
               + $"|Code and text documents|{CodeAndTextDocumentPatterns}"
               + "|All files|*.*";
    }

    private static void FocusInlineTerminalInput(DependencyObject root)
    {
        if (FindVisualChild<TextBox>(root, "InlineTerminalInput") is not { } input)
        {
            return;
        }

        input.Focus();
        input.CaretIndex = input.Text.Length;
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string? name = null)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match
                && (string.IsNullOrWhiteSpace(name) || string.Equals(match.Name, name, StringComparison.Ordinal)))
            {
                return match;
            }

            if (FindVisualChild<T>(child, name) is { } nestedMatch)
            {
                return nestedMatch;
            }
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = child switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(child),
                FrameworkContentElement frameworkContent => frameworkContent.Parent,
                ContentElement content => ContentOperations.GetParent(content),
                _ => LogicalTreeHelper.GetParent(child)
            };
        }

        return null;
    }

    private static bool IsDescendantOrSelf(
        DependencyObject? child,
        DependencyObject ancestor)
    {
        while (child is not null)
        {
            if (ReferenceEquals(child, ancestor))
            {
                return true;
            }

            child = child switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(child),
                FrameworkContentElement frameworkContent => frameworkContent.Parent,
                ContentElement content => ContentOperations.GetParent(content),
                _ => LogicalTreeHelper.GetParent(child)
            };
        }

        return false;
    }

    private void MinimizeWindowClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindowClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindowClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
