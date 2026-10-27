using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell;

public partial class NewRemoteTerminalDialog : Window
{
    private readonly IReadOnlyList<RemoteTerminalShellOption> _shells =
    [
        new(RemoteTerminalShellKind.Auto, "Automatic"),
        new(RemoteTerminalShellKind.Bash, "Bash / POSIX"),
        new(RemoteTerminalShellKind.PowerShell, "PowerShell")
    ];

    public NewRemoteTerminalDialog(LinkedHostInventorySnapshot inventory)
    {
        InitializeComponent();
        var hosts = inventory.Hosts
            .OrderBy(host => host.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(host => CreateHostOption(host, inventory))
            .ToList();
        HostComboBox.ItemsSource = hosts;
        ShellComboBox.ItemsSource = _shells;
        ShellComboBox.SelectedItem = _shells[0];
        HostComboBox.SelectedItem = hosts.FirstOrDefault(option =>
            option.CanConnect);
        if (HostComboBox.SelectedItem is null)
        {
            ValidationText.Text = hosts.Count == 0
                ? "No linked SSH devices are available. Add one in Settings."
                : "Unlock an active SSH credential in Settings before connecting.";
        }
    }

    public RemoteTerminalLaunchRequest? LaunchRequest { get; private set; }

    private static RemoteTerminalHostOption CreateHostOption(
        RemoteHostSummary host,
        LinkedHostInventorySnapshot inventory)
    {
        var credential = inventory.Credentials.FirstOrDefault(item =>
            !item.IsRevoked
            && string.Equals(
                item.Id,
                host.CredentialId,
                StringComparison.Ordinal));
        var unlocked = credential is not null
            && inventory.LocallyUnlockedCredentialIds.Contains(credential.Id);
        var pinned = !string.IsNullOrWhiteSpace(host.HostKeyAlgorithm)
            && !string.IsNullOrWhiteSpace(host.HostKeyFingerprint);
        var canConnect = !host.IsDisabled
            && host.HasCredential
            && credential is not null
            && unlocked
            && pinned;
        var status = host.IsDisabled
            ? "Disabled"
            : credential is null
                ? "Credential missing or revoked"
                : !unlocked
                    ? "Credential locked on this PC"
                    : !pinned
                        ? "Host key not pinned"
                        : $"{host.UserName}@{host.HostName}:{host.Port}";
        return new RemoteTerminalHostOption(
            host,
            credential,
            canConnect,
            status);
    }

    private void HostSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (HostComboBox.SelectedItem is not RemoteTerminalHostOption option)
        {
            return;
        }

        WorkingDirectoryTextBox.Text = string.IsNullOrWhiteSpace(
            option.Host.DefaultRoot)
            ? option.Host.OperatingSystem == RemoteHostOperatingSystem.Windows
                ? "C:/"
                : "/"
            : option.Host.DefaultRoot;
        TitleTextBox.Text = $"{option.DisplayName} {{n}}";
        ShellComboBox.SelectedItem = _shells[0];
        RefreshShellCommand();
        ValidationText.Text = option.CanConnect ? string.Empty : option.Status;
    }

    private void ShellSelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs) =>
        RefreshShellCommand();

    private void RefreshShellCommand()
    {
        if (ShellComboBox.SelectedItem is not RemoteTerminalShellOption shell)
        {
            return;
        }

        var operatingSystem = HostComboBox.SelectedItem is
            RemoteTerminalHostOption host
            ? host.Host.OperatingSystem
            : RemoteHostOperatingSystem.Unknown;
        var resolved = RemoteTerminalProtocol.ResolveShellKind(
            operatingSystem,
            shell.Kind);
        ShellCommandTextBox.Text =
            RemoteTerminalProtocol.GetDefaultShellCommand(resolved);
    }

    private void ConnectClick(object sender, RoutedEventArgs eventArgs)
    {
        if (HostComboBox.SelectedItem is not RemoteTerminalHostOption
            {
                CanConnect: true,
                Credential: { } credential
            } host)
        {
            ValidationText.Text =
                "Choose a linked device with an unlocked credential and pinned host key.";
            return;
        }

        if (ShellComboBox.SelectedItem is not RemoteTerminalShellOption shell)
        {
            ValidationText.Text = "Choose a remote shell.";
            return;
        }

        try
        {
            _ = WorkspaceLocation.Ssh(
                host.Host.Id,
                WorkingDirectoryTextBox.Text);
        }
        catch (Exception exception)
        {
            ValidationText.Text = exception.Message;
            WorkingDirectoryTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(ShellCommandTextBox.Text))
        {
            ValidationText.Text = "Enter the command that starts the remote shell.";
            ShellCommandTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            ValidationText.Text = "Give the remote terminal tab a name.";
            TitleTextBox.Focus();
            return;
        }

        LaunchRequest = new RemoteTerminalLaunchRequest
        {
            Host = host.Host,
            Credential = credential,
            ShellKind = shell.Kind,
            ShellLaunchCommand = ShellCommandTextBox.Text.Trim(),
            InitialWorkingDirectory = WorkingDirectoryTextBox.Text.Trim(),
            Title = TitleTextBox.Text.Trim(),
            StartupCommand = StartupCommandTextBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs)
    {
        DialogResult = false;
    }

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private sealed record RemoteTerminalShellOption(
        RemoteTerminalShellKind Kind,
        string DisplayName);

    private sealed record RemoteTerminalHostOption(
        RemoteHostSummary Host,
        SshCredentialSummary? Credential,
        bool CanConnect,
        string Status)
    {
        public string DisplayName => Host.DisplayName;
    }
}
