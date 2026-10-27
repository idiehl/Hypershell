using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed class TerminalTabViewModel : ObservableObject, IWorkspaceTab
{
    private readonly ITerminalSession _session;
    private readonly IRemoteTerminalSession? _remoteSession;
    private CancellationTokenSource? _executionCancellation;
    private string _title;
    private string _renameText;
    private string _inputText = string.Empty;
    private string _workingDirectory;
    private string _startupCommand;
    private string _tabColor;
    private Brush _tabBackgroundBrush;
    private Brush _tabForegroundBrush;
    private bool _isRenaming;
    private bool _isRunning;
    private bool _disposeSessionWhenIdle;
    private bool _isDisposed;
    private bool _isAiLiveAssistEnabled;
    private RemoteTerminalConnectionState _remoteConnectionState =
        RemoteTerminalConnectionState.Disconnected;
    private string _remoteConnectionDiagnostic = string.Empty;

    public TerminalTabViewModel(
        string title,
        TerminalProfile profile,
        TerminalSettings settings,
        string tabColor,
        string startupCommand)
        : this(
            title,
            profile,
            settings,
            tabColor,
            startupCommand,
            session: null,
            initialWorkingDirectory: null,
            initializeSession: true)
    {
    }

    public TerminalTabViewModel(
        string title,
        TerminalProfile profile,
        TerminalSettings settings,
        string tabColor,
        string startupCommand,
        ITerminalSession session,
        string initialWorkingDirectory)
        : this(
            title,
            profile,
            settings,
            tabColor,
            startupCommand,
            session,
            initialWorkingDirectory,
            initializeSession: true)
    {
    }

    private TerminalTabViewModel(
        string title,
        TerminalProfile profile,
        TerminalSettings settings,
        string tabColor,
        string startupCommand,
        ITerminalSession? session,
        string? initialWorkingDirectory,
        bool initializeSession)
    {
        _ = initializeSession;
        _title = title;
        SessionId = Guid.NewGuid().ToString("N");
        _renameText = title;
        Profile = profile;
        Settings = settings;
        _startupCommand = startupCommand;
        _tabColor = tabColor;
        _tabBackgroundBrush = CreateBrush(tabColor, "#28313D");
        _tabForegroundBrush = CreateContrastingBrush(tabColor);
        _workingDirectory = !string.IsNullOrWhiteSpace(initialWorkingDirectory)
            ? initialWorkingDirectory
            : profile.ShellKind is TerminalShellKind.DockerContainer
                ? "/"
                : profile.ShellKind == TerminalShellKind.WslBash
                && !string.IsNullOrWhiteSpace(Settings.StartupDirectory)
                ? Settings.StartupDirectory
                : Directory.Exists(Settings.StartupDirectory)
                ? Path.GetFullPath(Settings.StartupDirectory)
                : Environment.CurrentDirectory;
        _session = session
            ?? TerminalSessionFactory.Create(profile, _workingDirectory);
        _remoteSession = _session as IRemoteTerminalSession;
        if (_remoteSession is not null)
        {
            _remoteConnectionState = _remoteSession.ConnectionState;
            _remoteConnectionDiagnostic = _remoteSession.ConnectionDiagnostic;
            _remoteSession.ConnectionStateChanged +=
                OnRemoteConnectionStateChanged;
        }

        RunCommand = new AsyncRelayCommand(
            ExecuteInputAsync,
            () => !IsRunning && !string.IsNullOrWhiteSpace(InputText));
        ReconnectCommand = new AsyncRelayCommand(
            ReconnectRemoteAsync,
            () => IsRemote && !IsRunning);
        ClearCommand = new RelayCommand(Clear);
        Lines.Add(new TerminalLine(
            IsRemote
                ? $"{RemoteHostDisplayName} remote terminal ready."
                : $"{profile.Name} terminal ready.",
            TerminalLineKind.System));
    }

    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public WorkspaceTabKind Kind => WorkspaceTabKind.Terminal;
    public string SessionId { get; }
    public string AiLiveSourceId => $"terminal:{SessionId}";
    public bool IsAiLiveAssistEnabled
    {
        get => _isAiLiveAssistEnabled;
        set => SetProperty(ref _isAiLiveAssistEnabled, value);
    }
    public string DisplayTitle => Title;
    public bool CanRename => true;
    public bool HasUnsavedChanges => false;

    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }

    public TerminalProfile Profile { get; }
    public TerminalSettings Settings { get; }
    public TerminalShellKind ShellKind => Profile.ShellKind;
    public bool IsRemote => _remoteSession is not null;
    public string RemoteHostId => _remoteSession?.RemoteHostId ?? string.Empty;
    public string RemoteHostDisplayName =>
        _remoteSession?.RemoteHostDisplayName ?? string.Empty;
    public RemoteTerminalShellKind RemoteShellKind =>
        _remoteSession?.ShellKind ?? RemoteTerminalShellKind.Auto;
    public RemoteTerminalConnectionState RemoteConnectionState =>
        _remoteConnectionState;
    public string RemoteConnectionDiagnostic => _remoteConnectionDiagnostic;
    public bool IsRemoteConnected =>
        _remoteConnectionState == RemoteTerminalConnectionState.Connected;
    public string StartupCommand => _startupCommand;
    public string TabColor => _tabColor;
    public Brush TabBackgroundBrush => _tabBackgroundBrush;
    public Brush TabForegroundBrush => _tabForegroundBrush;
    public ObservableCollection<TerminalLine> Lines { get; } = [];

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                RunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WorkingDirectory
    {
        get => _workingDirectory;
        private set
        {
            if (SetProperty(ref _workingDirectory, value))
            {
                OnPropertyChanged(nameof(Prompt));
            }
        }
    }

    public string Prompt => IsRemote
        ? $"{RemoteHostDisplayName}:{WorkingDirectory}" +
          (RemoteShellKind == RemoteTerminalShellKind.Bash ? "$" : ">")
        : Profile.ShellKind switch
    {
        TerminalShellKind.WslBash => $"{Profile.Distribution}:{WorkingDirectory}$",
        TerminalShellKind.GitBash => $"BASH {WorkingDirectory}$",
        TerminalShellKind.Python => $"PY {WorkingDirectory}>>>",
        TerminalShellKind.NodeJs => $"JS {WorkingDirectory}>",
        TerminalShellKind.TypeScript => $"TS {WorkingDirectory}>",
        TerminalShellKind.Java => $"JAVA {WorkingDirectory}>",
        TerminalShellKind.Cpp => $"C++ {WorkingDirectory}>",
        TerminalShellKind.CommandPrompt => $"{WorkingDirectory}>",
        TerminalShellKind.DockerCli => "DOCKER>",
        TerminalShellKind.DockerContainer => $"{Profile.Distribution}:{WorkingDirectory}#",
        TerminalShellKind.MySql => "mysql>",
        TerminalShellKind.AzureCloudShell => "AZURE>",
        TerminalShellKind.CSharp => $"C# {WorkingDirectory}>",
        _ => $"PS {WorkingDirectory}>"
    };

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RunCommand.RaiseCanExecuteChanged();
                ReconnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        private set => SetProperty(ref _isRenaming, value);
    }

    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public RelayCommand ClearCommand { get; }

    public async Task ConnectRemoteAsync()
    {
        if (_remoteSession is null || _isDisposed || IsRunning)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _executionCancellation = cancellation;
        IsRunning = true;
        try
        {
            await _remoteSession.ReconnectAsync(cancellation.Token);
            if (!_isDisposed)
            {
                Lines.Add(new TerminalLine(
                    _remoteSession.ConnectionDiagnostic,
                    TerminalLineKind.System));
            }
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
            {
                Lines.Add(new TerminalLine(
                exception.Message,
                TerminalLineKind.Error));
            }
        }
        finally
        {
            if (ReferenceEquals(_executionCancellation, cancellation))
            {
                _executionCancellation = null;
            }

            IsRunning = false;
            if (_disposeSessionWhenIdle)
            {
                _disposeSessionWhenIdle = false;
                _session.Dispose();
            }
        }
    }

    public async Task RunStartupCommandAsync()
    {
        if (!string.IsNullOrWhiteSpace(StartupCommand))
        {
            await ExecuteTextAsync(StartupCommand);
        }
    }

    public async Task ExecuteTextAsync(string command)
    {
        InputText = command;
        await ExecuteInputAsync();
    }

    public async Task ExecuteInputAsync()
    {
        var command = InputText.Trim();
        if (_isDisposed || string.IsNullOrWhiteSpace(command) || IsRunning)
        {
            return;
        }

        if (Settings.ClearOnRun)
        {
            Lines.Clear();
        }

        Lines.Add(new TerminalLine($"{Prompt} {command}", TerminalLineKind.Input));
        InputText = string.Empty;
        using var cancellation = new CancellationTokenSource();
        _executionCancellation = cancellation;
        IsRunning = true;

        try
        {
            var result = await _session.ExecuteAsync(
                command,
                WorkingDirectory,
                cancellation.Token);
            if (_isDisposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.WorkingDirectory))
            {
                WorkingDirectory = result.WorkingDirectory;
            }

            foreach (var line in result.Output)
            {
                Lines.Add(new TerminalLine(line));
            }

            foreach (var warning in result.Warnings)
            {
                Lines.Add(new TerminalLine($"WARNING: {warning}", TerminalLineKind.System));
            }

            foreach (var error in result.Errors)
            {
                Lines.Add(new TerminalLine(error, TerminalLineKind.Error));
            }
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            if (!_isDisposed)
            {
                Lines.Add(new TerminalLine(
                    "Command canceled.",
                    TerminalLineKind.System));
            }
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
            {
                Lines.Add(new TerminalLine(
                    exception.Message,
                    TerminalLineKind.Error));
            }
        }
        finally
        {
            if (ReferenceEquals(_executionCancellation, cancellation))
            {
                _executionCancellation = null;
            }

            IsRunning = false;
            if (_disposeSessionWhenIdle)
            {
                _disposeSessionWhenIdle = false;
                _session.Dispose();
            }
        }
    }

    public void PresentCompiledDocumentExecution(
        string sourceLabel,
        CompiledLanguageExecutionResult execution)
    {
        if (_isDisposed)
        {
            return;
        }

        Lines.Add(new TerminalLine(
            $"{Prompt} run {sourceLabel}",
            TerminalLineKind.Input));
        if (!string.IsNullOrWhiteSpace(execution.WorkingDirectory))
        {
            WorkingDirectory = execution.WorkingDirectory;
        }

        foreach (var line in execution.Output)
        {
            Lines.Add(new TerminalLine(line));
        }

        foreach (var warning in execution.Warnings)
        {
            Lines.Add(new TerminalLine(
                $"WARNING: {warning}",
                TerminalLineKind.System));
        }

        foreach (var diagnostic in execution.Diagnostics)
        {
            Lines.Add(new TerminalLine(diagnostic, TerminalLineKind.Error));
        }

        if (!execution.Succeeded && execution.Diagnostics.Count == 0)
        {
            Lines.Add(new TerminalLine(
                execution.Message,
                TerminalLineKind.Error));
        }
        else
        {
            Lines.Add(new TerminalLine(
                execution.Message,
                TerminalLineKind.System));
        }
    }

    public void Clear()
    {
        Lines.Clear();
    }

    public void BeginRename()
    {
        RenameText = Title;
        IsRenaming = true;
    }

    public void CommitRename()
    {
        if (!IsRenaming)
        {
            return;
        }

        var title = RenameText.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }

        RenameText = Title;
        IsRenaming = false;
    }

    public void CancelRename()
    {
        if (!IsRenaming)
        {
            return;
        }

        RenameText = Title;
        IsRenaming = false;
    }

    public void ApplyConfiguration(
        string title,
        string tabColor,
        string backgroundColor,
        string backgroundImagePath,
        string fontFamilyName,
        string startupCommand)
    {
        var normalizedTitle = title.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedTitle))
        {
            Title = normalizedTitle;
            RenameText = normalizedTitle;
        }

        ApplyTabColor(tabColor);

        Settings.BackgroundColor = backgroundColor;
        Settings.BackgroundImagePath = backgroundImagePath;
        Settings.FontFamilyName = fontFamilyName;
        _startupCommand = startupCommand.Trim();
        OnPropertyChanged(nameof(StartupCommand));
    }

    public void ApplyProfileConfiguration(TerminalConfigurationProfile configuration)
    {
        ApplyTabColor(configuration.TabColor);
        Settings.CopyFrom(configuration.Settings);
        _startupCommand = configuration.StartupCommand.Trim();
        OnPropertyChanged(nameof(StartupCommand));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_remoteSession is not null)
        {
            _remoteSession.ConnectionStateChanged -=
                OnRemoteConnectionStateChanged;
        }

        if (_executionCancellation is { } cancellation)
        {
            _disposeSessionWhenIdle = true;
            cancellation.Cancel();
            return;
        }

        _session.Dispose();
    }

    private async Task ReconnectRemoteAsync()
    {
        await ConnectRemoteAsync();
    }

    private void OnRemoteConnectionStateChanged(
        object? sender,
        RemoteTerminalConnectionStateChangedEventArgs eventArgs)
    {
        void Apply()
        {
            _remoteConnectionState = eventArgs.State;
            _remoteConnectionDiagnostic = eventArgs.Diagnostic;
            OnPropertyChanged(nameof(RemoteConnectionState));
            OnPropertyChanged(nameof(RemoteConnectionDiagnostic));
            OnPropertyChanged(nameof(IsRemoteConnected));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(Apply);
            return;
        }

        Apply();
    }

    private static Brush CreateBrush(string value, string fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!);
        }
        catch
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)!);
        }
    }

    private void ApplyTabColor(string tabColor)
    {
        if (string.IsNullOrWhiteSpace(tabColor)
            || string.Equals(_tabColor, tabColor, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _tabColor = tabColor;
        _tabBackgroundBrush = CreateBrush(tabColor, "#28313D");
        _tabForegroundBrush = CreateContrastingBrush(tabColor);
        OnPropertyChanged(nameof(TabColor));
        OnPropertyChanged(nameof(TabBackgroundBrush));
        OnPropertyChanged(nameof(TabForegroundBrush));
    }

    private static Brush CreateContrastingBrush(string value)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value)!;
            var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
            return new SolidColorBrush(luminance > 0.58
                ? Color.FromRgb(13, 17, 23)
                : Color.FromRgb(246, 248, 250));
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(246, 248, 250));
        }
    }

}
