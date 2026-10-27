using Hypershell.Profiles;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Hypershell;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _linkedHostsPresentationCancellation;

    public SettingsWindow(MainWindowViewModel mainWindowViewModel, int initialSectionIndex = 0)
    {
        InitializeComponent();
        _viewModel = new SettingsWindowViewModel(mainWindowViewModel);
        DataContext = _viewModel;
        SettingsSections.SelectedIndex = Math.Clamp(initialSectionIndex, 0, 7);
    }

    public void SelectSection(int sectionIndex)
    {
        SettingsSections.SelectedIndex = Math.Clamp(sectionIndex, 0, 7);
    }

    public void ShowLinkedHosts(bool beginNewHost)
    {
        SelectSection(6);
        var canManageHosts = _viewModel.LinkedHosts.CanUse;
        if (beginNewHost && canManageHosts)
        {
            _viewModel.LinkedHosts.BeginNewHost();
        }

        _ = Dispatcher.InvokeAsync(
            () =>
            {
                LinkedHostsSettingsCard.BringIntoView();
                LinkedHostDisplayNameBox.Focus();
                Keyboard.Focus(LinkedHostDisplayNameBox);
            },
            DispatcherPriority.Loaded);

        if (!canManageHosts)
        {
            return;
        }

        _linkedHostsPresentationCancellation?.Cancel();
        _linkedHostsPresentationCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        _linkedHostsPresentationCancellation = cancellation;
        _ = RefreshLinkedHostsForPresentationAsync(
            beginNewHost,
            cancellation);
    }

    private async Task RefreshLinkedHostsForPresentationAsync(
        bool preserveNewHostEditor,
        CancellationTokenSource cancellation)
    {
        try
        {
            await _viewModel.LinkedHosts.RefreshAsync(
                cancellation.Token,
                preserveNewHostEditor);
        }
        catch (Exception exception)
        {
            _viewModel.LinkedHosts.ReportPresentationFailure(exception);
        }
        finally
        {
            if (ReferenceEquals(
                    _linkedHostsPresentationCancellation,
                    cancellation))
            {
                _linkedHostsPresentationCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CloseClick(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    private void SettingsWindowClosed(object? sender, EventArgs eventArgs)
    {
        _linkedHostsPresentationCancellation?.Cancel();
        _linkedHostsPresentationCancellation?.Dispose();
        _linkedHostsPresentationCancellation = null;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        SshCredentialPassphraseBox.Clear();
        WorkspaceAiApiKeyBox.Clear();
        _viewModel.Dispose();
    }

    private void MaximizeRestoreClick(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            MaximizeRestoreClick(sender, eventArgs);
            return;
        }

        DragMove();
    }

    private void TerminalColorClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string target }
            || _viewModel.SelectedTerminalConfiguration is not { } profile)
        {
            return;
        }

        var currentColor = target switch
        {
            "Tab" => profile.TabColor,
            "Foreground" => profile.Settings.ForegroundColor,
            "Background" => profile.Settings.BackgroundColor,
            "Accent" => profile.Settings.AccentColor,
            "Error" => profile.Settings.ErrorColor,
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(currentColor))
        {
            return;
        }

        var picker = new ColorPickerDialog($"{target} color", currentColor)
        {
            Owner = this
        };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        switch (target)
        {
            case "Tab":
                profile.TabColor = picker.SelectedColor;
                break;
            case "Foreground":
                profile.Settings.ForegroundColor = picker.SelectedColor;
                break;
            case "Background":
                profile.Settings.BackgroundColor = picker.SelectedColor;
                break;
            case "Accent":
                profile.Settings.AccentColor = picker.SelectedColor;
                break;
            case "Error":
                profile.Settings.ErrorColor = picker.SelectedColor;
                break;
        }
    }

    private void ChooseTerminalBackgroundImageClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose terminal background image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetTerminalBackgroundImage(dialog.FileName);
        }
    }

    private void ClearTerminalBackgroundImageClick(object sender, RoutedEventArgs eventArgs)
    {
        _viewModel.SetTerminalBackgroundImage(string.Empty);
    }

    private void BrowseExplorerStartupDirectoryClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the default Explorer directory",
            InitialDirectory = _viewModel.Explorer.StartupDirectory
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExplorerStartupDirectory = dialog.FolderName;
        }
    }

    private void CreateProjectClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new ProjectCreationDialog(
            _viewModel.Explorer.StartupDirectory)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.AddProject(dialog.Project);
        }
    }

    private void AddProjectDirectoryClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a project directory",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddProjectDirectory(dialog.FolderName);
        }
    }

    private void RemoveProjectDirectoryClick(object sender, RoutedEventArgs eventArgs)
    {
        _viewModel.RemoveProjectDirectory(ProjectDirectoriesList.SelectedItem as string);
    }

    private async void CheckApplicationUpdateClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await _viewModel.CheckForApplicationUpdateAsync(
            _lifetimeCancellation.Token);
    }

    private async void DownloadApplicationUpdateClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await _viewModel.DownloadApplicationUpdateAsync(
            _lifetimeCancellation.Token);
    }

    private void InstallApplicationUpdateClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_viewModel.CanInstallApplicationUpdate)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "Install the downloaded Hypershell update now?\n\n"
            + "The signed installer has passed manifest, hash, and Authenticode verification. Hypershell will close, Windows may request administrator approval, and the app will reopen after installation. The staged installer is retained for rollback.",
            "Install Hypershell update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _ = _viewModel.StartApplicationUpdate();
        }
    }

    private void ApproveExtensionClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveExtensionEditor(sender) is not { } editor)
        {
            return;
        }

        var manifest = editor.Package.Manifest;
        var requestedCapabilities = manifest?.RequestedCapabilities.Count > 0
            ? string.Join(", ", manifest.RequestedCapabilities)
            : "None";
        var messageApiVersion = manifest?.Contributions.Panels
            .Select(panel => panel.MessageApiVersion)
            .DefaultIfEmpty()
            .Max() ?? 0;
        var result = MessageBox.Show(
            this,
            $"Approve local web panels from this exact package?\n\n"
            + $"Extension: {manifest?.Id ?? editor.DisplayName}\n"
            + $"Version: {manifest?.Version ?? "Unknown"}\n"
            + $"SHA-256: {editor.Package.PackageSha256}\n"
            + $"Requested: {requestedCapabilities}\n\n"
            + "This approval grants WorkspacePanel and, when requested by an API-v1 panel, WorkspaceMetadata on this device only. "
            + (messageApiVersion > 0
                ? $"Declared panel host messaging v{messageApiVersion} is restricted to its allowlisted state and read-only context methods. "
                : "Panel host messaging remains disabled for this package. ")
            + "It does not grant file, terminal, network, secret, native-code, or AI access. "
            + "Any package-content change invalidates the approval.",
            "Approve extension panels",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ApproveExtension(editor);
        }
    }

    private void RevokeExtensionClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveExtensionEditor(sender) is not { } editor)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Revoke the device-local panel approval for {editor.DisplayName}?\n\n"
            + "Any active panels from this package will close and replaced built-in panels will return.",
            "Revoke extension approval",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.RevokeExtension(editor);
        }
    }

    private void OpenExtensionOnLeftClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        PreviewExtension(sender, ExtensionPanelActivationTarget.Left);

    private void OpenExtensionOnRightClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        PreviewExtension(sender, ExtensionPanelActivationTarget.Right);

    private void OpenExtensionAtBottomClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        PreviewExtension(sender, ExtensionPanelActivationTarget.Bottom);

    private void OpenExtensionFloatingClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        PreviewExtension(sender, ExtensionPanelActivationTarget.Floating);

    private void DeactivateExtensionClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ResolveExtensionEditor(sender) is { } editor)
        {
            _viewModel.DeactivateExtension(editor);
        }
    }

    private async void InstallHyperlibArchiveClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Install a Hyperlib package",
            Filter = "Hypershell Hyperlib package|*.hyperlib",
            DefaultExt = HyperlibPackageService.ArchiveExtension,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await InstallHyperlibSourceAsync(dialog.FileName);
        }
    }

    private async void InstallHyperlibFolderClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title =
                "Choose a ContentOnly package folder containing hypershell.extension.json",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            await InstallHyperlibSourceAsync(dialog.FolderName);
        }
    }

    private async void PackHyperlibFolderClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title =
                "Choose a validated ContentOnly Hyperlib package folder",
            Multiselect = false
        };
        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var preview = await _viewModel.InspectHyperlibPackageAsync(
                folderDialog.FolderName);
            var saveDialog = new SaveFileDialog
            {
                Title = "Pack a distributable Hyperlib archive",
                Filter = "Hypershell Hyperlib package|*.hyperlib",
                DefaultExt = HyperlibPackageService.ArchiveExtension,
                AddExtension = true,
                FileName = $"{preview.ExtensionId}-{preview.Version}.hyperlib",
                OverwritePrompt = true
            };
            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var result = await _viewModel.PackHyperlibPackageAsync(
                folderDialog.FolderName,
                saveDialog.FileName);
            MessageBox.Show(
                this,
                $"Created {result.DisplayName} {result.Version}.\n\n"
                + $"Archive: {result.ArchivePath}\n"
                + $"Files: {result.FileCount:N0}\n"
                + $"Content hash: {result.PackageSha256}",
                "Hyperlib package created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowHyperlibError("The Hyperlib package could not be created.", exception);
        }
    }

    private async void RemoveHyperlibPackageClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (ResolveExtensionEditor(sender) is not
            { IsManagedHyperlibPackage: true } editor)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Remove {editor.DisplayName} from Hypershell?\n\n"
            + "Its active Hyperlib entries and static document mappings will be unloaded. The package folder will be moved to Hypershell's recovery area instead of being permanently deleted.",
            "Remove Hyperlib package",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var removal = await _viewModel.RemoveHyperlibPackageAsync(editor);
            MessageBox.Show(
                this,
                $"Removed {removal.DisplayName}.\n\nRecovery copy: {removal.RecoveryDirectory}",
                "Hyperlib package removed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowHyperlibError("The Hyperlib package could not be removed.", exception);
        }
    }

    private async Task InstallHyperlibSourceAsync(string sourcePath)
    {
        try
        {
            var preview = await _viewModel.InspectHyperlibPackageAsync(
                sourcePath);
            if (preview.Disposition
                == HyperlibPackageInstallDisposition.AlreadyInstalled)
            {
                MessageBox.Show(
                    this,
                    $"{preview.DisplayName} {preview.Version} is already installed with the same content hash.",
                    "Hyperlib package already installed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var allowUpdate = preview.RequiresUpdateConfirmation;
            if (allowUpdate)
            {
                var existingVersion = string.IsNullOrWhiteSpace(
                    preview.ExistingVersion)
                    ? "unknown"
                    : preview.ExistingVersion;
                var confirmation = MessageBox.Show(
                    this,
                    $"Update {preview.DisplayName}?\n\n"
                    + $"Installed version: {existingVersion}\n"
                    + $"Selected version: {preview.Version}\n\n"
                    + "The existing package will be moved to Hypershell's recovery area before the new content is activated.",
                    "Update Hyperlib package",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var result = await _viewModel.InstallHyperlibPackageAsync(
                sourcePath,
                allowUpdate);
            var verb = result.Disposition
                == HyperlibPackageInstallDisposition.Update
                ? "Updated"
                : "Installed";
            MessageBox.Show(
                this,
                $"{verb} {result.DisplayName} {result.Version}.\n\n"
                + "Its validated static Hyperlib content is now available in the Command Lexicon.",
                $"Hyperlib package {verb.ToLowerInvariant()}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowHyperlibError("The Hyperlib package could not be installed.", exception);
        }
    }

    private void ShowHyperlibError(string summary, Exception exception)
    {
        MessageBox.Show(
            this,
            $"{summary}\n\n{exception.Message}",
            "Hyperlib package error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void ExportPortableProfileClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export portable Hypershell profile",
            Filter = "Hypershell portable profile|*.hyperprofile",
            DefaultExt = ".hyperprofile",
            AddExtension = true,
            FileName = _viewModel.SuggestedPortableProfileFileName,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExportPortableProfile(dialog.FileName);
        }
    }

    private void ChoosePortableProfileClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Inspect portable Hypershell profile",
            Filter = "Hypershell portable profile|*.hyperprofile",
            DefaultExt = ".hyperprofile",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.InspectPortableProfile(dialog.FileName);
        }
    }

    private async void ApplyPortableProfileClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.ApplyPortableProfileAsync(persist: false);

    private async void SavePortableProfileClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var result = MessageBox.Show(
            this,
            "Save the inspected bundle as this device's current portable profile?\n\n"
            + "Reusable application, terminal, Explorer display, command-library, extension-subscription, and panel-layout settings will be adopted. "
            + "Windows paths, projects, executables, startup commands, approvals, credentials, open documents, sessions, and recovery data will remain local and unchanged.",
            "Save portable profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.ApplyPortableProfileAsync(persist: true);
        }
    }

    private void SaveProfileSyncConfigurationClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        _viewModel.SaveProfileSyncConfiguration();

    private async void AuthorizeProfileSyncDeviceClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await _viewModel.AuthorizeProfileSyncDeviceAsync(
            challenge => Dispatcher.Invoke(() =>
                OpenProfileAuthorizationPortal(challenge)),
            _lifetimeCancellation.Token);
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
        catch (Exception exception) when (exception is InvalidOperationException
                                         or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                $"Open this address in a browser:\n{uri.AbsoluteUri}\n\nAfter password and authenticator sign-in, Hypershell will continue automatically.",
                "Atlas sign-in",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void SignOutProfileSyncClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.SignOutProfileSyncAsync();

    private async void RefreshRemoteProfilesClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.RefreshRemoteProfilesAsync();

    private async void DownloadRemoteProfileClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.DownloadSelectedRemoteProfileAsync();

    private async void SyncAccountNowClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.SyncAccountStateAsync();

    private async void DeleteRemoteProfileClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedRemoteProfile is not { IsDeleted: false } selected)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Create a remote tombstone for '{selected.DisplayName}'?\n\n"
            + "The request is protected by the selected ETag. If the remote revision changed, Hypershell will refuse the delete. "
            + "This device's profile, settings, documents, and conflict copies will not be deleted.",
            "Tombstone remote profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteSelectedRemoteProfileAsync();
        }
    }

    private async void RefreshLinkedHostsClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.LinkedHosts.RefreshAsync(
            _lifetimeCancellation.Token);

    private void NewLinkedHostClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        _viewModel.LinkedHosts.BeginNewHost();

    private async void SaveLinkedHostClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        await _viewModel.LinkedHosts.SaveHostAsync(
            _lifetimeCancellation.Token);

    private async void DisableLinkedHostClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.LinkedHosts.SelectedHost is not { } selected)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Disable the linked host '{selected.DisplayName}'?\n\n"
            + "It will disappear from active workspace choices. The host, its files, and the referenced encrypted credential are not deleted.",
            "Disable linked SSH host",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.LinkedHosts.DisableSelectedHostAsync(
                _lifetimeCancellation.Token);
        }
    }

    private async void PinLinkedHostKeyClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.LinkedHosts.SelectedHost is not { } selected)
        {
            return;
        }

        var previous = selected.Host.HostKeyFingerprint ?? string.Empty;
        var next = _viewModel.LinkedHosts.HostKeyFingerprint.Trim();
        if (!string.IsNullOrWhiteSpace(previous)
            && !string.Equals(previous, next, StringComparison.Ordinal))
        {
            var result = MessageBox.Show(
                this,
                "The entered fingerprint differs from the currently pinned SSH host key.\n\n"
                + $"Current: {previous}\n\nNew: {next}\n\n"
                + "Continue only after independently verifying that the host key was deliberately changed. An attacker can present a different key.",
                "Replace pinned host key",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await _viewModel.LinkedHosts.PinSelectedHostKeyAsync(
            _lifetimeCancellation.Token);
    }

    private void NewSshCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        SshCredentialPassphraseBox.Clear();
        _viewModel.LinkedHosts.BeginNewCredential();
    }

    private void BrowseSshCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an OpenSSH private key",
            Filter = "OpenSSH private keys|id_*;*.pem;*.key|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.LinkedHosts.CredentialFilePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(
                    _viewModel.LinkedHosts.CredentialDisplayName))
            {
                _viewModel.LinkedHosts.CredentialDisplayName =
                    Path.GetFileName(dialog.FileName);
            }
        }
    }

    private async void StoreSshCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var passphrase = SshCredentialPassphraseBox.Password;
        try
        {
            await _viewModel.LinkedHosts.StoreCredentialAsync(
                passphrase,
                _lifetimeCancellation.Token);
        }
        finally
        {
            SshCredentialPassphraseBox.Clear();
        }
    }

    private async void RotateSshCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.LinkedHosts.SelectedCredential is not { } selected)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Rotate '{selected.DisplayName}' to the selected private key?\n\n"
            + "Every outstanding release request and every locally cached copy of the previous version will become unusable. Linked hosts keep the same credential identity.",
            "Rotate SSH credential",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var passphrase = SshCredentialPassphraseBox.Password;
        try
        {
            await _viewModel.LinkedHosts.RotateSelectedCredentialAsync(
                passphrase,
                _lifetimeCancellation.Token);
        }
        finally
        {
            SshCredentialPassphraseBox.Clear();
        }
    }

    private async void UnlockSshCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await _viewModel.LinkedHosts.UnlockSelectedCredentialAsync(
            authorization => Dispatcher.Invoke(() =>
                OpenSshCredentialReleasePortal(authorization)),
            _lifetimeCancellation.Token);
    }

    private void OpenSshCredentialReleasePortal(
        SshCredentialReleaseAuthorization authorization)
    {
        var completeUri = authorization.VerificationUri
                          + "?userCode="
                          + Uri.EscapeDataString(authorization.UserCode);
        try
        {
            Process.Start(new ProcessStartInfo(completeUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                $"Open this address in a browser:\n{completeUri}\n\nRelease code: {authorization.UserCode}",
                "Unlock SSH credential",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void RevokeSshCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.LinkedHosts.SelectedCredential is not { } selected)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Revoke '{selected.DisplayName}'?\n\n"
            + "Hypershell will deny new releases and remove this PC's protected copy. This does not remove the corresponding public key from an SSH host; rotate or remove that authorized key separately when access must be cut off at the server.",
            "Revoke SSH credential",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.LinkedHosts.RevokeSelectedCredentialAsync(
                _lifetimeCancellation.Token);
        }
    }

    private void ApproveWorkspaceAiAccessClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedWorkspaceAiAccess is not
            { CanApprove: true } selected)
        {
            return;
        }

        var terminalWarning = BuildTerminalPolicyWarning(
            selected.AllowTerminal,
            selected.TerminalExecutionPolicy);
        var terminalReadWarning = selected.AllowTerminalRead
            ? "\n\nThe AI may read the current draft command and a bounded recent terminal transcript without a per-read confirmation when that terminal is rooted inside this approval."
            : string.Empty;
        var automaticChangeWarning = selected
            .AllowAutomaticFileAndEditorChanges
            ? "\n\nWARNING: File creations, atomic replacements, and live editor replacements may be applied without a per-change review prompt. Revision and path checks still apply, but accepted content can overwrite data."
            : string.Empty;
        var result = MessageBox.Show(
            this,
            $"Save device-local AI access for '{selected.DisplayName}'?\n\n"
            + $"Root: {selected.RootPath}\n\n"
            + $"Capabilities: {selected.CapabilitySummary}."
            + terminalReadWarning
            + automaticChangeWarning
            + terminalWarning
            + "\n\nThis approval cannot arrive through profile sync. File tools remain bounded to this root and expose no delete operation. Terminal commands are not an OS sandbox and may reach other paths or the network with your Windows account's permissions.",
            "Approve AI workspace access",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ApproveSelectedWorkspaceAiAccess();
        }
    }

    private void BrowseWorkspaceAiRootClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a device-local AI root",
            Multiselect = false
        };
        if (Directory.Exists(_viewModel.NewWorkspaceAiRootPath))
        {
            dialog.InitialDirectory = _viewModel.NewWorkspaceAiRootPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.NewWorkspaceAiRootPath = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(
                    _viewModel.NewWorkspaceAiRootName))
            {
                _viewModel.NewWorkspaceAiRootName = dialog.FolderName;
            }
        }
    }

    private void SetupWorkspaceAiFullDeviceClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var roots = _viewModel.GetReadyLocalFixedDriveRoots();
        if (roots.Count == 0)
        {
            MessageBox.Show(
                this,
                "No ready local fixed drives were found.",
                "AI access setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            "Allowing AI to make changes on your computer can be risky. Files may be permanently altered or deleted.\n\n"
            + "Allow Hypershell full access to data stored on this device?\n\n"
            + "Ready local fixed drives:\n"
            + string.Join("\n", roots.Select(root => $"• {root}"))
            + "\n\nThis grants read, write, live-terminal read, and terminal execution access. New roots begin with per-change review and per-command confirmation; those prompts can be disabled per root.",
            "Allow full-device AI access",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.SetUpWorkspaceAiAccess(roots, fullDevice: true);
        }
    }

    private void SetupWorkspaceAiSingleFolderClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Restrict Hypershell AI access to one folder",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "Allowing AI to make changes on your computer can be risky. Files may be permanently altered or deleted.\n\n"
            + "Restrict Hypershell to this single folder?\n\n"
            + dialog.FolderName
            + "\n\nThis grants read, write, live-terminal read, and terminal execution access inside the selected starting scope. New access begins with per-change review and per-command confirmation; those prompts can be disabled for this root.",
            "Restrict AI access to one folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.SetUpWorkspaceAiAccess(
                [dialog.FolderName],
                fullDevice: false);
        }
    }

    private void AddWorkspaceAiRootClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_viewModel.CanAddWorkspaceAiRoot)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(
            _viewModel.NewWorkspaceAiRootName)
            ? _viewModel.NewWorkspaceAiRootPath
            : _viewModel.NewWorkspaceAiRootName.Trim();
        var terminalWarning = BuildTerminalPolicyWarning(
            _viewModel.NewWorkspaceAiRootAllowTerminal,
            _viewModel.NewWorkspaceAiRootTerminalExecutionPolicy);
        var terminalReadWarning = _viewModel.NewWorkspaceAiRootAllowTerminalRead
            ? "\n\nThe AI may read the current draft command and a bounded recent terminal transcript without a per-read confirmation when that terminal is rooted inside this approval."
            : string.Empty;
        var automaticChangeWarning = _viewModel
            .NewWorkspaceAiRootAllowAutomaticFileAndEditorChanges
            ? "\n\nWARNING: File creations, atomic replacements, and live editor replacements may be applied without a per-change review prompt. Revision and path checks still apply, but accepted content can overwrite data."
            : string.Empty;
        var result = MessageBox.Show(
            this,
            $"Add '{label}' as a device-local AI root?\n\n"
            + $"Root: {_viewModel.NewWorkspaceAiRootPath}\n\n"
            + $"Capabilities: {_viewModel.NewWorkspaceAiRootCapabilitySummary}."
            + terminalReadWarning
            + automaticChangeWarning
            + terminalWarning
            + "\n\nExisting approved roots remain available. This root is not synchronized or restored on another device.",
            "Add AI root",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.AddWorkspaceAiRoot();
        }
    }

    private void SaveWorkspaceAiAccessClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.AllowWorkspaceAiCloudRequests)
        {
            var result = MessageBox.Show(
                this,
                "Allow AI conversations that select OpenAI to send requests?\n\n"
                + "Prompts, model replies, requested file/editor excerpts, and tool results may be sent to OpenAI and can incur API charges. "
                + "This does not change the model selected by any conversation. "
                + "Hypershell sends store=false, retains conversation state only in memory, and never grants workspace permissions automatically. "
                + "File/editor changes and terminal execution follow the host-enforced policies saved for the selected root, which may include standing auto-apply and auto-execution.",
                "Enable cloud AI requests",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _viewModel.SaveWorkspaceAiAccess(
            WorkspaceAiApiKeyBox.Password);
        WorkspaceAiApiKeyBox.Clear();
    }

    private void SaveWorkspaceAiAutonomyClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var confirmed = false;
        if (_viewModel.WorkspaceAiAutonomyRequiresConfirmation)
        {
            var result = MessageBox.Show(
                this,
                $"Allow Engineer to apply permitted changes and execute terminal commands in '{_viewModel.WorkspaceAiAutonomyContextName}' without asking each time?\n\nThis does not expand the Context's existing scopes. Commands still run with your Windows account's permissions, and the global Stop control remains available.",
                "Engineer autonomy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            confirmed = true;
        }

        _viewModel.SaveWorkspaceAiAutonomy(confirmed);
    }

    private void ForgetWorkspaceAiCredentialClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var result = MessageBox.Show(
            this,
            "Remove the DPAPI-protected OpenAI API key for this Windows user and disable cloud AI requests?\n\n"
            + "Workspace approvals and the content-free local audit trail will remain unchanged.",
            "Forget OpenAI credential",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            WorkspaceAiApiKeyBox.Clear();
            _viewModel.ForgetWorkspaceAiCredential();
        }
    }

    private void RevokeWorkspaceAiAccessClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_viewModel.SelectedWorkspaceAiAccess is not
            { IsApproved: true } selected)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Revoke AI access to '{selected.DisplayName}' on this device?\n\n"
            + "Future file and terminal requests for this workspace will be denied immediately. The local audit history will remain available.",
            "Revoke AI workspace access",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.RevokeSelectedWorkspaceAiAccess();
        }
    }

    private void RefreshWorkspaceAiAccessClick(
        object sender,
        RoutedEventArgs eventArgs) =>
        _viewModel.RefreshWorkspaceAiAccess();

    private static string BuildTerminalPolicyWarning(
        bool allowTerminal,
        WorkspaceAiTerminalExecutionPolicy policy)
    {
        if (!allowTerminal)
        {
            return string.Empty;
        }

        return policy switch
        {
            WorkspaceAiTerminalExecutionPolicy.ReviewOnly =>
                "\n\nTerminal commands will pause once at the AI review card. Approving that card runs the command without a second native confirmation dialog. The shell is not an OS sandbox.",
            WorkspaceAiTerminalExecutionPolicy.AutoExecute =>
                "\n\nWARNING: This grants standing terminal execution. AI-requested commands may run without any per-command review or confirmation and inherit your Windows account's filesystem and network reach. You can revoke or downgrade this root at any time.",
            _ =>
                "\n\nTerminal commands will pause at the AI review card and require a separate native confirmation before execution. The shell is not an OS sandbox."
        };
    }

    private void PreviewExtension(
        object sender,
        ExtensionPanelActivationTarget target)
    {
        if (ResolveExtensionEditor(sender) is { } editor)
        {
            _viewModel.PreviewExtension(editor, target);
        }
    }

    private static ExtensionPackageEditorViewModel? ResolveExtensionEditor(
        object sender) =>
        sender is Button { Tag: ExtensionPackageEditorViewModel editor }
            ? editor
            : null;
}
