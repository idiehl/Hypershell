using Hypershell.Models;
using Hypershell.Services;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Hypershell;

public partial class AtlasMyNetAppDialog : Window
{
    private readonly string _iconPath;
    private readonly bool _showNavigationToolbar;

    public AtlasMyNetAppDialog(AtlasMyNetAppDefinition? source = null)
    {
        _iconPath = source?.IconPath ?? string.Empty;
        _showNavigationToolbar = source?.ShowNavigationToolbar ?? true;
        InitializeComponent();
        HeadingText.Text = source is null
            ? "Add MyNet app"
            : "Edit MyNet app";
        StopModeComboBox.ItemsSource = Enum.GetValues<AtlasMyNetStopMode>();
        if (source is null)
        {
            IdTextBox.Text = Guid.NewGuid().ToString("N");
            UrlTextBox.Text = "http://127.0.0.1:{port}/";
            HealthPathTextBox.Text = "/";
            HealthStatusTextBox.Text = "200";
            StopModeComboBox.SelectedItem = AtlasMyNetStopMode.ManagedProcess;
        }
        else
        {
            LoadDefinition(source);
            IdTextBox.IsReadOnly = true;
        }

        UpdateStopCommandVisibility();
        UpdateNativeAppVisibility();
    }

    public AtlasMyNetAppDefinition? Definition { get; private set; }

    private void LoadDefinition(AtlasMyNetAppDefinition source)
    {
        DisplayNameTextBox.Text = source.DisplayName;
        IdTextBox.Text = source.Id;
        NativeAppCheckBox.IsChecked = source.IsNative;
        UrlTextBox.Text = source.UrlTemplate;
        PortsTextBox.Text = string.Join(", ", source.Ports);
        LaunchExecutableTextBox.Text = source.Launch?.Executable ?? string.Empty;
        LaunchArgumentsTextBox.Text = string.Join(
            Environment.NewLine,
            source.Launch?.Arguments ?? []);
        WorkingDirectoryTextBox.Text = source.Launch?.WorkingDirectory ?? string.Empty;
        EnvironmentTextBox.Text = string.Join(
            Environment.NewLine,
            source.Launch?.Environment.Select(pair => $"{pair.Key}={pair.Value}")
            ?? []);
        HealthPathTextBox.Text = source.HealthCheck?.Path ?? "/";
        HealthStatusTextBox.Text =
            (source.HealthCheck?.ExpectedStatusCode ?? 200).ToString(
                CultureInfo.InvariantCulture);
        HealthHeaderNameTextBox.Text = source.HealthCheck?.HeaderName ?? string.Empty;
        HealthHeaderValueTextBox.Text = source.HealthCheck?.HeaderValue ?? string.Empty;
        var stop = source.Stop ?? new AtlasMyNetStopDefinition();
        StopModeComboBox.SelectedItem = stop.Mode;
        StopExecutableTextBox.Text = stop.Executable;
        StopArgumentsTextBox.Text = string.Join(Environment.NewLine, stop.Arguments);
        StopWorkingDirectoryTextBox.Text = stop.WorkingDirectory;
        AllowExternalResourcesCheckBox.IsChecked = source.AllowExternalResources;
    }

    private void SaveClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Definition = AtlasMyNetAppStore.ValidateDefinition(
                BuildDefinition());
            DialogResult = true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or FormatException
                                           or OverflowException)
        {
            StatusText.Text = exception.Message;
        }
    }

    private AtlasMyNetAppDefinition BuildDefinition()
    {
        var displayName = DisplayNameTextBox.Text.Trim();
        var id = IdTextBox.Text.Trim();
        var isNative = NativeAppCheckBox.IsChecked == true;
        var url = isNative ? string.Empty : UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(id)
            || !isNative && string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidDataException(
                isNative
                    ? "Display name and stable ID are required."
                    : "Display name, stable ID, and URL template are required.");
        }

        var ports = isNative ? new List<int>() : ParsePorts(PortsTextBox.Text);
        if (url.Contains("{port}", StringComparison.OrdinalIgnoreCase)
            && ports.Count == 0)
        {
            throw new InvalidDataException(
                "Enter at least one port when the URL contains {port}.");
        }

        var launchExecutable = LaunchExecutableTextBox.Text.Trim();
        if (isNative && string.IsNullOrWhiteSpace(launchExecutable))
        {
            throw new InvalidDataException("A native desktop app requires a start executable.");
        }

        var launch = string.IsNullOrWhiteSpace(launchExecutable)
            ? null
            : new AtlasMyNetLaunchDefinition
            {
                Executable = launchExecutable,
                Arguments = ParseLines(LaunchArgumentsTextBox.Text),
                WorkingDirectory = WorkingDirectoryTextBox.Text.Trim(),
                Environment = ParseEnvironment(EnvironmentTextBox.Text)
            };

        var healthStatus = 200;
        if (!isNative && (!int.TryParse(
                HealthStatusTextBox.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out healthStatus)
            || healthStatus is < 100 or > 599))
        {
            throw new InvalidDataException(
                "Expected health status must be an HTTP status from 100 through 599.");
        }

        var stopMode = StopModeComboBox.SelectedItem is AtlasMyNetStopMode selected
            ? selected
            : AtlasMyNetStopMode.ManagedProcess;
        var stop = new AtlasMyNetStopDefinition
        {
            Mode = stopMode,
            Executable = StopExecutableTextBox.Text.Trim(),
            Arguments = ParseLines(StopArgumentsTextBox.Text),
            WorkingDirectory = StopWorkingDirectoryTextBox.Text.Trim()
        };
        if (stopMode == AtlasMyNetStopMode.Command
            && string.IsNullOrWhiteSpace(stop.Executable))
        {
            throw new InvalidDataException(
                "A command stop mode requires a stop executable.");
        }

        return new AtlasMyNetAppDefinition
        {
            Id = id,
            DisplayName = displayName,
            IconPath = _iconPath,
            IsNative = isNative,
            UrlTemplate = url,
            Ports = ports,
            Launch = launch,
            HealthCheck = isNative ? null : new AtlasMyNetHealthCheckDefinition
            {
                Path = HealthPathTextBox.Text.Trim(),
                ExpectedStatusCode = healthStatus,
                HeaderName = HealthHeaderNameTextBox.Text.Trim(),
                HeaderValue = HealthHeaderValueTextBox.Text.Trim()
            },
            Stop = stop,
            AllowExternalResources = !isNative && AllowExternalResourcesCheckBox.IsChecked == true,
            ShowNavigationToolbar = !isNative && _showNavigationToolbar
        };
    }

    private static List<int> ParsePorts(string input)
    {
        var ports = new SortedSet<int>();
        foreach (var segment in input.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var rangeParts = segment.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
            if (rangeParts.Length == 1)
            {
                ports.Add(ParsePort(rangeParts[0]));
                continue;
            }

            if (rangeParts.Length != 2)
            {
                throw new InvalidDataException($"Invalid port segment '{segment}'.");
            }

            var start = ParsePort(rangeParts[0]);
            var end = ParsePort(rangeParts[1]);
            if (end < start || end - start > 1000)
            {
                throw new InvalidDataException(
                    $"Invalid or excessively large port range '{segment}'.");
            }

            for (var port = start; port <= end; port++)
            {
                ports.Add(port);
            }
        }

        return [.. ports];
    }

    private static int ParsePort(string value)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidDataException(
                $"'{value}' is not a port from 1 through 65535.");
        }

        return port;
    }

    private static List<string> ParseLines(string value) =>
        value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .ToList();

    private static Dictionary<string, string> ParseEnvironment(string value)
    {
        var environment = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var line in ParseLines(value))
        {
            var equalsIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex <= 0)
            {
                throw new InvalidDataException(
                    $"Environment line '{line}' must use KEY=VALUE.");
            }

            environment[line[..equalsIndex].Trim()] = line[(equalsIndex + 1)..];
        }

        return environment;
    }

    private void BrowseLaunchExecutableClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose local app executable",
            Filter = "Windows programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            LaunchExecutableTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(WorkingDirectoryTextBox.Text))
            {
                WorkingDirectoryTextBox.Text =
                    Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void BrowseStopExecutableClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose stop executable",
            Filter = "Windows programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            StopExecutableTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(StopWorkingDirectoryTextBox.Text))
            {
                StopWorkingDirectoryTextBox.Text =
                    Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void StopModeSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs eventArgs) =>
        UpdateStopCommandVisibility();

    private void NativeAppChanged(object sender, RoutedEventArgs eventArgs) =>
        UpdateNativeAppVisibility();

    private void UpdateNativeAppVisibility()
    {
        if (WebAddressPanel is null || HealthCheckPanel is null
            || AllowExternalResourcesCheckBox is null || StartCommandHeading is null)
        {
            return;
        }

        var isNative = NativeAppCheckBox.IsChecked == true;
        var visibility = isNative ? Visibility.Collapsed : Visibility.Visible;
        WebAddressPanel.Visibility = visibility;
        HealthCheckPanel.Visibility = visibility;
        AllowExternalResourcesCheckBox.Visibility = visibility;
        StartCommandHeading.Text = isNative ? "START COMMAND" : "START COMMAND (OPTIONAL)";
    }

    private void UpdateStopCommandVisibility()
    {
        if (StopCommandPanel is null)
        {
            return;
        }

        StopCommandPanel.Visibility =
            StopModeComboBox.SelectedItem is AtlasMyNetStopMode.Command
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs) =>
        DialogResult = false;

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
