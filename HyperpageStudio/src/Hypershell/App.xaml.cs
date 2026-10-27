namespace Hypershell;

public partial class App : System.Windows.Application
{
    private Services.StandaloneStudioHost? _studioHost;
    protected override void OnStartup(System.Windows.StartupEventArgs eventArgs)
    {
        // The real application ends the launcher's recursion guard before it
        // creates terminals or any other child processes.
        Environment.SetEnvironmentVariable("HYPERSHELL_LAUNCHER_ACTIVE", null);
        var mode = Services.StandaloneStudioHost.ParseMode(eventArgs.Args);
        if (mode != Services.StandaloneStudioMode.None)
        {
            Services.StandaloneStudioHost.ConfigureProcess(mode);
            _studioHost = new Services.StandaloneStudioHost(mode);
            if (!_studioHost.IsPrimaryInstance)
            {
                _studioHost.ActivateExisting();
                Shutdown();
                return;
            }
        }
        Services.TaskbarIdentityService.InitializeProcessIdentity();
        base.OnStartup(eventArgs);
        var window = new MainWindow(mode);
        MainWindow = window;
        _studioHost?.Attach(window);
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs eventArgs)
    {
        _studioHost?.Dispose();
        base.OnExit(eventArgs);
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
