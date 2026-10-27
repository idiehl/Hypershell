using Hypershell.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Hypershell;

public partial class ProjectCreationDialog : Window
{
    private readonly string _initialDirectory;
    private readonly ObservableCollection<string> _directories = [];

    public ProjectCreationDialog(string? initialDirectory = null)
    {
        InitializeComponent();
        _initialDirectory = Directory.Exists(initialDirectory)
            ? Path.GetFullPath(initialDirectory)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ProjectDirectoriesList.ItemsSource = _directories;
        Loaded += (_, _) => ProjectNameTextBox.Focus();
    }

    public ProjectCreationDialog(WorkspaceLocation initialLocation)
        : this(initialLocation.IsLocal ? initialLocation.Path : null)
    {
        if (initialLocation.IsSsh)
        {
            _directories.Add(initialLocation.Identity);
            ProjectNameTextBox.Text = GetWorkspaceFileName(initialLocation.Path);
            StatusText.Text = "Added the current linked-device folder.";
        }
    }

    public ProjectDefinition Project { get; private set; } = null!;

    private void AddFolderClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a project directory",
            InitialDirectory = _directories.LastOrDefault()
                               ?? _initialDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = Path.GetFullPath(dialog.FolderName);
        if (_directories.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            ProjectDirectoriesList.SelectedItem = _directories.First(
                directory => string.Equals(
                    directory,
                    path,
                    StringComparison.OrdinalIgnoreCase));
            StatusText.Text = "That folder is already part of this project.";
            return;
        }

        _directories.Add(path);
        ProjectDirectoriesList.SelectedItem = path;
        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
        {
            ProjectNameTextBox.Text = new DirectoryInfo(path).Name;
        }

        StatusText.Text = $"Added {path}";
    }

    private void RemoveFolderClick(object sender, RoutedEventArgs eventArgs)
    {
        if (ProjectDirectoriesList.SelectedItem is string path)
        {
            _directories.Remove(path);
            StatusText.Text = "Removed the selected folder from this draft.";
        }
    }

    private void CreateClick(object sender, RoutedEventArgs eventArgs)
    {
        var name = ProjectNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Give the project a name.";
            ProjectNameTextBox.Focus();
            return;
        }

        if (_directories.Count == 0)
        {
            StatusText.Text = "Add at least one project folder.";
            return;
        }

        Project = new ProjectDefinition
        {
            Name = name,
            Directories = new ObservableCollection<string>(_directories)
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
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static string GetWorkspaceFileName(string providerPath)
    {
        var normalized = providerPath.TrimEnd('/', '\\');
        var separator = Math.Max(
            normalized.LastIndexOf('/'),
            normalized.LastIndexOf('\\'));
        var name = separator >= 0
            ? normalized[(separator + 1)..]
            : normalized;
        return string.IsNullOrWhiteSpace(name) ? "Remote project" : name;
    }
}
