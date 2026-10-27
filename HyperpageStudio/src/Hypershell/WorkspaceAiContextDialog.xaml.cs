using Hypershell.Models;
using Hypershell.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hypershell;

public partial class WorkspaceAiContextDialog : Window
{
    private readonly WorkspaceAiContextDefinition _original;
    private readonly IReadOnlyList<WorkspaceAiWorkspaceDescriptor>
        _workspaceLocations;
    private readonly IReadOnlyList<AccessModeOption> _accessModes =
    [
        new("Choose Location", WorkspaceAiContextAccessMode.ChooseLocation),
        new("Full Access", WorkspaceAiContextAccessMode.FullAccess)
    ];
    private readonly IReadOnlyList<ComputerControlModeOption>
        _computerControlModes =
    [
        new("Off", WorkspaceAiComputerControlMode.Off),
        new("Hypershell only", WorkspaceAiComputerControlMode.Hypershell),
        new("Full Windows desktop", WorkspaceAiComputerControlMode.Desktop)
    ];

    public WorkspaceAiContextDialog(
        WorkspaceAiContextDefinition context,
        bool isNew,
        IReadOnlyList<WorkspaceAiWorkspaceDescriptor>? workspaceLocations = null)
    {
        InitializeComponent();
        _original = context;
        _workspaceLocations = workspaceLocations ?? [];
        DialogTitleText.Text = isNew ? "New Context" : $"Edit {context.Name}";
        ContextNameTextBox.IsEnabled = !context.IsBuiltIn;
        EffortComboBox.ItemsSource =
            new[] { "none", "low", "medium", "high", "xhigh", "max" };
        VerbosityComboBox.ItemsSource = new[] { "low", "medium", "high" };
        ComputerControlComboBox.ItemsSource = _computerControlModes;
        ComputerControlComboBox.DisplayMemberPath =
            nameof(ComputerControlModeOption.DisplayName);
        ComputerControlComboBox.SelectedValuePath =
            nameof(ComputerControlModeOption.Mode);
        foreach (var comboBox in PermissionModeComboBoxes())
        {
            comboBox.ItemsSource = _accessModes;
            comboBox.DisplayMemberPath = nameof(AccessModeOption.DisplayName);
            comboBox.SelectedValuePath = nameof(AccessModeOption.Mode);
        }

        ContextNameTextBox.Text = context.Name;
        LoadPermission(ReadModeComboBox, ReadLocationTextBox, context.Read);
        LoadPermission(CreateModeComboBox, CreateLocationTextBox, context.Create);
        LoadPermission(EditModeComboBox, EditLocationTextBox, context.Edit);
        LoadPermission(DeleteModeComboBox, DeleteLocationTextBox, context.Delete);
        LoadPermission(TerminalModeComboBox, TerminalLocationTextBox, context.Terminal);
        EffortComboBox.SelectedItem = context.ReasoningEffort;
        VerbosityComboBox.SelectedItem = context.TextVerbosity;
        AskPermissionCheckBox.IsChecked = context.AskBeforeChanges;
        InternetAccessCheckBox.IsChecked = context.AllowInternet;
        LiveAssistanceCheckBox.IsChecked = context.AllowLiveAssistance;
        ComputerControlComboBox.SelectedValue = context.ComputerControl;
        RefreshPermissionControls();
    }

    public WorkspaceAiContextDefinition? Result { get; private set; }

    private void PermissionModeChanged(
        object sender,
        SelectionChangedEventArgs eventArgs) =>
        RefreshPermissionControls();

    private void BrowsePermissionClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        var target = key switch
        {
            "Read" => ReadLocationTextBox,
            "Create" => CreateLocationTextBox,
            "Edit" => EditLocationTextBox,
            "Delete" => DeleteLocationTextBox,
            "Terminal" => TerminalLocationTextBox,
            _ => null
        };
        if (target is not null)
        {
            OpenLocationMenu(
                sender as Button,
                target,
                $"Choose the {key.ToLowerInvariant()} location");
        }
    }

    private void OpenLocationMenu(
        Button? placementTarget,
        TextBox target,
        string title)
    {
        var candidates = _workspaceLocations
            .Where(item => item.IsAvailable)
            .ToList();
        if (placementTarget is null || candidates.Count == 0)
        {
            BrowseInto(target, title);
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget
        };
        var browseLocal = new MenuItem
        {
            Header = "Choose local folder…"
        };
        browseLocal.Click += (_, _) => BrowseInto(target, title);
        menu.Items.Add(browseLocal);
        menu.Items.Add(new Separator());
        foreach (var candidate in candidates)
        {
            var value = string.IsNullOrWhiteSpace(candidate.WorkspaceUri)
                ? candidate.RootPath
                : candidate.WorkspaceUri;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var item = new MenuItem
            {
                Header = (string.IsNullOrWhiteSpace(candidate.WorkspaceUri)
                    ? "This PC · "
                    : "Linked device · ") + candidate.DisplayName,
                Tag = value
            };
            item.Click += (_, _) => target.Text = value;
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void BrowseInto(TextBox target, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = Directory.Exists(target.Text)
                ? target.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FolderName;
        }
    }

    private void SaveClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            var context = _original with
            {
                Name = ContextNameTextBox.Text.Trim(),
                Read = ReadPermission(
                    ReadModeComboBox,
                    ReadLocationTextBox),
                Create = ReadPermission(
                    CreateModeComboBox,
                    CreateLocationTextBox),
                Edit = ReadPermission(
                    EditModeComboBox,
                    EditLocationTextBox),
                Delete = ReadPermission(
                    DeleteModeComboBox,
                    DeleteLocationTextBox),
                Terminal = ReadPermission(
                    TerminalModeComboBox,
                    TerminalLocationTextBox),
                ReasoningEffort = EffortComboBox.SelectedItem as string
                    ?? string.Empty,
                TextVerbosity = VerbosityComboBox.SelectedItem as string
                    ?? string.Empty,
                DefaultSaveDirectory = string.Empty,
                DefaultSaveWorkspaceUri = string.Empty,
                AskBeforeChanges = AskPermissionCheckBox.IsChecked == true,
                AllowInternet = InternetAccessCheckBox.IsChecked == true,
                AllowLiveAssistance =
                    LiveAssistanceCheckBox.IsChecked == true,
                ComputerControl = ComputerControlComboBox.SelectedValue
                    is WorkspaceAiComputerControlMode computerControl
                    ? computerControl
                    : WorkspaceAiComputerControlMode.Off
            };
            Validate(context);
            if (context.RequiresRiskWarning && !context.RiskWarningAcknowledged)
            {
                var warning = MessageBox.Show(
                    this,
                    "Allowing AI to make changes on your computer can be risky. Files may be permanently altered or deleted.",
                    "AI context access",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);
                if (warning != MessageBoxResult.OK)
                {
                    return;
                }

                context = context with { RiskWarningAcknowledged = true };
            }

            Result = context;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            ValidationText.Text = exception.Message;
        }
    }

    private static void Validate(WorkspaceAiContextDefinition context)
    {
        if (string.IsNullOrWhiteSpace(context.Name))
        {
            throw new ArgumentException("Give the context a name.");
        }

        if (string.IsNullOrWhiteSpace(context.Model))
        {
            throw new ArgumentException("Choose an OpenAI model.");
        }

        if (context.ComputerControl != WorkspaceAiComputerControlMode.Off
            && context.AskBeforeChanges)
        {
            throw new ArgumentException(
                "Turn off 'Ask permission before making changes' to enable autonomous computer control.");
        }

        foreach (var (label, permission) in new[]
                 {
                     ("Read", context.Read),
                     ("Create", context.Create),
                     ("Edit", context.Edit),
                     ("Delete", context.Delete),
                     ("Terminal", context.Terminal)
                 })
        {
            if (permission.Mode == WorkspaceAiContextAccessMode.ChooseLocation
                && !Directory.Exists(permission.Location)
                && !permission.WorkspaceUris.Any(IsRemoteWorkspaceUri))
            {
                throw new DirectoryNotFoundException(
                    $"Choose an existing location for {label} access.");
            }
        }
    }

    private static WorkspaceAiContextPathPermission ReadPermission(
        ComboBox mode,
        TextBox location)
    {
        var selectedMode = mode.SelectedValue is WorkspaceAiContextAccessMode selected
            ? selected
            : WorkspaceAiContextAccessMode.ChooseLocation;
        var value = location.Text.Trim();

        return WorkspaceLocation.TryParse(value, out var workspace)
               && workspace.IsSsh
            ? new WorkspaceAiContextPathPermission
            {
                Mode = selectedMode,
                Location = string.Empty,
                WorkspaceUris = [workspace.Identity]
            }
            : new WorkspaceAiContextPathPermission
            {
                Mode = selectedMode,
                Location = value,
                WorkspaceUris = []
            };
    }

    private static void LoadPermission(
        ComboBox mode,
        TextBox location,
        WorkspaceAiContextPathPermission permission)
    {
        mode.SelectedValue = permission.Mode;
        location.Text = permission.WorkspaceUris.FirstOrDefault()
                        ?? permission.Location;
    }

    private static bool IsRemoteWorkspaceUri(string? value) =>
        WorkspaceLocation.TryParse(value, out var workspace)
        && workspace.IsSsh;

    private void RefreshPermissionControls()
    {
        RefreshPermissionControl(ReadModeComboBox, ReadLocationTextBox, ReadBrowseButton);
        RefreshPermissionControl(CreateModeComboBox, CreateLocationTextBox, CreateBrowseButton);
        RefreshPermissionControl(EditModeComboBox, EditLocationTextBox, EditBrowseButton);
        RefreshPermissionControl(DeleteModeComboBox, DeleteLocationTextBox, DeleteBrowseButton);
        RefreshPermissionControl(TerminalModeComboBox, TerminalLocationTextBox, TerminalBrowseButton);
    }

    private static void RefreshPermissionControl(
        ComboBox mode,
        TextBox location,
        Button browse)
    {
        var choose = mode.SelectedValue is not WorkspaceAiContextAccessMode.FullAccess;
        location.IsEnabled = choose;
        browse.IsEnabled = choose;
        location.Visibility = choose ? Visibility.Visible : Visibility.Hidden;
        browse.Visibility = choose ? Visibility.Visible : Visibility.Hidden;
    }

    private IEnumerable<ComboBox> PermissionModeComboBoxes()
    {
        yield return ReadModeComboBox;
        yield return CreateModeComboBox;
        yield return EditModeComboBox;
        yield return DeleteModeComboBox;
        yield return TerminalModeComboBox;
    }

    private void CancelClick(object sender, RoutedEventArgs eventArgs) =>
        DialogResult = false;

    private void TitleBarMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private sealed record AccessModeOption(
        string DisplayName,
        WorkspaceAiContextAccessMode Mode)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record ComputerControlModeOption(
        string DisplayName,
        WorkspaceAiComputerControlMode Mode)
    {
        public override string ToString() => DisplayName;
    }
}
