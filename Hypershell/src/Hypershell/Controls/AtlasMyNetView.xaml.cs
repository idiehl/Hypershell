using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell.Controls;

public partial class AtlasMyNetView : UserControl
{
    public AtlasMyNetView()
    {
        InitializeComponent();
    }

    private AtlasMyNetViewModel? ViewModel => DataContext as AtlasMyNetViewModel;

    private Window Owner => Window.GetWindow(this);

    private void OpenDesktopClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            MyNetDesktopHost.OpenDesktop(new SettingsService());
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException
                                           or System.ComponentModel.Win32Exception)
        {
            ShowError("MyNet desktop could not open", exception.Message);
        }
    }

    private async void RefreshClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ReloadDefinitionsAsync();
        }
    }

    private void AddAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new AtlasMyNetAppDialog { Owner = Owner };
        if (dialog.ShowDialog() == true && dialog.Definition is { } definition)
        {
            TrySaveDefinition(viewModel, definition);
        }
    }

    private void EditAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || ResolveAppItem(sender) is not { } item)
        {
            return;
        }

        var dialog = new AtlasMyNetAppDialog(item.Definition) { Owner = Owner };
        if (dialog.ShowDialog() == true && dialog.Definition is { } definition)
        {
            TrySaveDefinition(viewModel, definition);
        }
    }

    private void RemoveAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || ResolveAppItem(sender) is not { } item
            || !item.CanRemove)
        {
            return;
        }

        var result = MessageBox.Show(
            Owner,
            $"Remove {item.DisplayName} from Atlas MyNet? This does not delete the app or stop its process.",
            "Remove registered app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                viewModel.RemoveDefinition(item);
            }
            catch (Exception exception) when (exception is InvalidDataException
                                               or IOException
                                               or UnauthorizedAccessException)
            {
                ShowError("Remove failed", exception.Message);
            }
        }
    }

    private async void OpenAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is { } viewModel
            && ResolveAppItem(sender) is { } item)
        {
            await viewModel.OpenAppAsync(item);
        }
    }

    private async void StartAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is { } viewModel
            && ResolveAppItem(sender) is { } item)
        {
            await viewModel.StartAppAsync(item);
        }
    }

    private async void StopAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || ResolveAppItem(sender) is not { Runtime: { } runtime } item)
        {
            return;
        }

        var confirm = MessageBox.Show(
            Owner,
            CreateProcessReview(
                $"Ask {item.DisplayName} to stop?",
                runtime.ProcessId,
                runtime.ExecutablePath,
                runtime.StartedAtUtc),
            "Stop local app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await viewModel.RequestAppStopAsync(item);
        if (result.Outcome == AtlasMyNetStopOutcome.NeedsForce && !item.Definition.IsNative)
        {
            var force = MessageBox.Show(
                Owner,
                $"{result.Message}\n\nForce-stop only PID {runtime.ProcessId}? Child processes will not be stopped.",
                "Force-stop selected process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (force == MessageBoxResult.Yes)
            {
                result = viewModel.ForceStopApp(item);
            }
        }

        ShowStopResult(result);
    }

    private void TrustAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || ResolveAppItem(sender) is not { } item)
        {
            return;
        }

        var review = AtlasMyNetAppStore.CreateCommandReviewText(item.Definition);
        var result = MessageBox.Show(
            Owner,
            $"Review the exact commands below. Trust applies only to this command fingerprint and will be revoked if it changes.\n\n{review}",
            $"Trust {item.DisplayName}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                viewModel.Trust(item);
            }
            catch (Exception exception) when (exception is InvalidDataException
                                               or IOException
                                               or UnauthorizedAccessException)
            {
                ShowError("Trust update failed", exception.Message);
            }
        }
    }

    private void ExternalResourcesClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || ResolveAppItem(sender) is not { } item
            || sender is not CheckBox checkBox)
        {
            return;
        }

        var allow = checkBox.IsChecked == true;
        try
        {
            viewModel.SaveExternalResourcePermission(item, allow);
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            item.Definition.AllowExternalResources = !allow;
            checkBox.IsChecked = !allow;
            ShowError("Permission update failed", exception.Message);
        }
    }

    private void OpenDiscoveredAppClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is { } viewModel
            && sender is FrameworkElement
            {
                DataContext: AtlasMyNetDiscoveredWebApp app
            })
        {
            viewModel.OpenDiscoveredApp(app);
        }
    }

    private void OpenSocketClientClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is { } viewModel
            && ResolveSocket(sender) is { } socket)
        {
            viewModel.OpenSocketClient(socket);
        }
    }

    private void NetworkSocketDoubleClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (ViewModel is { } viewModel
            && NetworkSocketList.SelectedItem is NetworkSocketSnapshot socket)
        {
            viewModel.OpenSocketClient(socket);
        }
    }

    private async void StopSocketProcessClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel
            || ResolveSocket(sender) is not { } socket)
        {
            return;
        }

        var confirm = MessageBox.Show(
            Owner,
            CreateProcessReview(
                $"Ask {socket.ProcessName} to stop?",
                socket.ProcessId,
                socket.ExecutablePath,
                socket.ProcessStartTimeUtc),
            "Stop socket owner",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await viewModel.RequestSocketProcessStopAsync(socket);
        if (result.Outcome == AtlasMyNetStopOutcome.NeedsForce)
        {
            var force = MessageBox.Show(
                Owner,
                $"{result.Message}\n\nForce-stop only PID {socket.ProcessId}? Child processes will not be stopped.",
                "Force-stop selected process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (force == MessageBoxResult.Yes)
            {
                result = viewModel.ForceStopSocketProcess(socket);
            }
        }

        ShowStopResult(result);
        await viewModel.RefreshAsync(forceWebProbe: false);
    }

    private void ImportAppsClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import Atlas MyNet app definitions",
            Filter = "Atlas MyNet JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        try
        {
            var definitions = viewModel.ReadImport(dialog.FileName);
            var currentIds = viewModel.Apps
                .Select(item => item.Definition.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var replacements = definitions.Count(definition =>
                currentIds.Contains(definition.Id));
            var review = new StringBuilder()
                .AppendLine($"Import {definitions.Count} app definition(s)?")
                .AppendLine($"Existing definitions replaced by ID: {replacements}")
                .AppendLine()
                .AppendLine("Imported commands are disabled until you review and trust them in the app card.")
                .AppendLine();
            foreach (var definition in definitions)
            {
                review.AppendLine($"[{definition.DisplayName}] ({definition.Id})");
                review.AppendLine(AtlasMyNetAppStore.CreateCommandReviewText(definition));
                review.AppendLine();
            }

            var result = MessageBox.Show(
                Owner,
                review.ToString(),
                "Review Atlas MyNet import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                viewModel.MergeImport(definitions);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            ShowError("Import failed", exception.Message);
        }
    }

    private void ExportAppsClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Atlas MyNet app definitions",
            Filter = "Atlas MyNet JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = "atlas-mynet-apps.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Owner) != true)
        {
            return;
        }

        try
        {
            viewModel.Export(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            ShowError("Export failed", exception.Message);
        }
    }

    private void ShowStopResult(AtlasMyNetStopResult result)
    {
        var icon = result.Outcome is AtlasMyNetStopOutcome.Stopped
            or AtlasMyNetStopOutcome.AlreadyStopped
            ? MessageBoxImage.Information
            : MessageBoxImage.Warning;
        MessageBox.Show(Owner, result.Message, "Atlas MyNet", MessageBoxButton.OK, icon);
    }

    private void ShowError(string title, string message) =>
        MessageBox.Show(
            Owner,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    private void TrySaveDefinition(
        AtlasMyNetViewModel viewModel,
        AtlasMyNetAppDefinition definition)
    {
        try
        {
            viewModel.SaveDefinition(definition);
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException)
        {
            ShowError("App definition was not saved", exception.Message);
        }
    }

    private static string CreateProcessReview(
        string heading,
        int processId,
        string executablePath,
        DateTimeOffset? startedAt) =>
        $"{heading}\n\nPID: {processId}\nExecutable: {BlankAsUnknown(executablePath)}\nStarted: {startedAt?.ToLocalTime().ToString("G") ?? "Unknown"}\n\nAtlas MyNet will only act if the PID still belongs to the same current-user process.";

    private static string BlankAsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private static AtlasMyNetAppItemViewModel? ResolveAppItem(object sender) =>
        sender is FrameworkElement
        {
            DataContext: AtlasMyNetAppItemViewModel item
        }
            ? item
            : null;

    private static NetworkSocketSnapshot? ResolveSocket(object sender) =>
        sender is FrameworkElement
        {
            DataContext: NetworkSocketSnapshot socket
        }
            ? socket
            : null;
}
