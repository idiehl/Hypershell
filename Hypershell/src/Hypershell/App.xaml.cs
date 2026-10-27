namespace Hypershell;

public partial class App : System.Windows.Application
{
    private Services.MyNetDesktopHost? _desktopHost;
    private MyNetDesktopWindow? _desktopWindow;

    internal bool SuppressShellStartup { get; init; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        // Hosted UI verifiers load the real resources with isolated windows only.
        if (SuppressShellStartup)
        {
            return;
        }
        if (e.Args.Length == 1 && string.Equals(e.Args[0], "--meridian", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            _ = OpenMeridianAsync();
            return;
        }
        if (e.Args.Contains("--mynet-desktop", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
            var settings = new Services.SettingsService();
            if (!Services.MyNetDesktopHost.TryOwn(settings, out _desktopHost))
            {
                Shutdown();
                return;
            }

            void ShowDesktop()
            {
                if (_desktopWindow is null)
                {
                    _desktopWindow = new MyNetDesktopWindow(settings);
                    _desktopWindow.Closed += (_, _) => _desktopWindow = null;
                    MainWindow = _desktopWindow;
                }
                _desktopWindow.ShowDesktop();
            }

            _desktopHost!.Listen(() => Dispatcher.BeginInvoke((Action)ShowDesktop));
            ShowDesktop();
            return;
        }

        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private async Task OpenMeridianAsync()
    {
        try
        {
            var settings = new Services.SettingsService();
            var request = await Services.MeridianDesktopLaunch.ResolveAsync(settings);
            MainWindow = new MyNetAppWindow(request, settings);
            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
            MainWindow.Show();
        }
        catch (Exception exception) when (exception is System.IO.IOException
                                             or UnauthorizedAccessException
                                             or InvalidOperationException
                                             or TimeoutException
                                             or System.ComponentModel.Win32Exception)
        {
            System.Windows.MessageBox.Show(exception.Message, "Meridian could not open",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            Shutdown();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _desktopWindow?.PrepareForShutdown();
        _desktopHost?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(System.Windows.SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (!e.Cancel) _desktopWindow?.PrepareForShutdown();
    }

    private void OpenNewTerminalDialog(object sender, System.Windows.RoutedEventArgs eventArgs)
    {
        if (sender is not System.Windows.Controls.Button button
            || System.Windows.Window.GetWindow(button) is not MainWindow owner
            || owner.DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.RefreshTerminalProfiles();
        var dialog = new NewTerminalDialog(
            viewModel.TerminalProfiles,
            viewModel.SelectedTerminalProfile,
            viewModel.TerminalFontFamilies)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() == true && dialog.LaunchRequest is { } request)
        {
            viewModel.CreateTerminal(request);
        }
    }
}
