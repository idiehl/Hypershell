using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Collections.ObjectModel;

public static class ExplorerProjectVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var appData = Path.Combine(verifierRoot, "app-data");
        var defaultRoot = Path.Combine(verifierRoot, "Default");
        var firstRoot = Path.Combine(verifierRoot, "Hypershell");
        var secondRoot = Path.Combine(verifierRoot, "OtherProject");
        Directory.CreateDirectory(defaultRoot);
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var firstRootMarker = Path.Combine(firstRoot, "keep-me.txt");
        File.WriteAllText(firstRootMarker, "Project removal must preserve this file.");

        var settingsService = new SettingsService(appData);
        var initialSettings = settingsService.Load();
        initialSettings.Explorer.StartupDirectory = defaultRoot;
        initialSettings.Explorer.ActiveProjectId = string.Empty;
        settingsService.Save(initialSettings);

        using var mainWindow = new MainWindowViewModel(settingsService);
        await mainWindow.FileExplorer.WhenIdleAsync();
        Require(PathsMatch(mainWindow.FileExplorer.CurrentPath, defaultRoot),
            "The Default Explorer option did not open the configured startup directory.");
        mainWindow.CreateExplorerProject(new ProjectDefinition
        {
            Name = "Hypershell",
            Directories = new ObservableCollection<string>([firstRoot])
        });
        await mainWindow.FileExplorer.WhenIdleAsync();

        var saved = settingsService.Load();
        Require(saved.Explorer.Projects.Count == 1,
            "Quick project creation was not persisted.");
        Require(saved.Explorer.Projects[0].Name == "Hypershell"
                && saved.Explorer.Projects[0].Directories.SequenceEqual(
                    [firstRoot],
                    StringComparer.OrdinalIgnoreCase),
            "The quick-created project lost its name or directory.");
        Require(mainWindow.FileExplorer.ProjectOptions.Count == 2,
            "The Explorer selector did not include Default and the saved project.");
        Require(mainWindow.FileExplorer.ProjectOptions.Single(option =>
                    option.IsDefault).DisplayName == "Default",
            "The Explorer selector did not label its default location as Default.");
        Require(mainWindow.FileExplorer.SelectedProject?.Name == "Hypershell",
            "The new project was not selected in the Explorer.");
        Require(PathsMatch(mainWindow.FileExplorer.CurrentPath, firstRoot),
            "Selecting a project did not move the address to its root directory.");

        mainWindow.FileExplorer.SelectedProjectOption =
            mainWindow.FileExplorer.ProjectOptions.Single(option =>
                option.IsDefault);
        await mainWindow.FileExplorer.WhenIdleAsync();
        Require(mainWindow.FileExplorer.SelectedProject is null,
            "The Default option did not clear project mode.");
        Require(PathsMatch(mainWindow.FileExplorer.CurrentPath, defaultRoot),
            "The Default option did not return to the configured startup directory.");

        Require(mainWindow.FileExplorer.BackCommand.CanExecute(null),
            "Project selection did not create an Explorer history entry.");
        mainWindow.FileExplorer.BackCommand.Execute(null);
        await mainWindow.FileExplorer.WhenIdleAsync();
        Require(mainWindow.FileExplorer.SelectedProject?.Name == "Hypershell"
                && PathsMatch(mainWindow.FileExplorer.CurrentPath, firstRoot),
            "Explorer Back did not restore the previous project location.");

        mainWindow.FileExplorer.CurrentPath = secondRoot;
        mainWindow.FileExplorer.GoCommand.Execute(null);
        await mainWindow.FileExplorer.WhenIdleAsync();
        Require(mainWindow.FileExplorer.SelectedProject is null
                && PathsMatch(mainWindow.FileExplorer.CurrentPath, secondRoot),
            "Committing an address did not browse to the folder in Default mode.");
        mainWindow.FileExplorer.BackCommand.Execute(null);
        await mainWindow.FileExplorer.WhenIdleAsync();
        Require(mainWindow.FileExplorer.SelectedProject?.Name == "Hypershell"
                && PathsMatch(mainWindow.FileExplorer.CurrentPath, firstRoot),
            "Explorer Back did not restore project mode after address navigation.");

        mainWindow.FileExplorer.CurrentPath = Path.Combine(
            verifierRoot,
            "uncommitted-address");
        mainWindow.FileExplorer.RefreshCommand.Execute(null);
        await mainWindow.FileExplorer.WhenIdleAsync();
        Require(PathsMatch(mainWindow.FileExplorer.CurrentPath, firstRoot),
            "Refresh treated uncommitted address text as a loaded location.");

        using (var settings = new SettingsWindowViewModel(mainWindow))
        {
            settings.AddProject(new ProjectDefinition
            {
                Name = "Other Project",
                Directories = new ObservableCollection<string>([secondRoot])
            });
            Require(settings.Projects.Count == 2
                    && settings.SelectedProject?.Directories.Contains(
                        secondRoot,
                        StringComparer.OrdinalIgnoreCase) == true,
                "The Settings project draft did not retain its selected directory.");
            settings.SaveExplorerCommand.Execute(null);
            await mainWindow.FileExplorer.WhenIdleAsync();
        }

        saved = settingsService.Load();
        Require(saved.Explorer.Projects.Count == 2,
            "Settings Save did not persist the created project.");
        Require(saved.Explorer.Projects.Any(project =>
                project.Name == "Other Project"
                && project.Directories.Contains(
                    secondRoot,
                    StringComparer.OrdinalIgnoreCase)),
            "The Settings-created project was saved without its directory.");

        var selectedProjectId = mainWindow.FileExplorer.SelectedProject?.Id
                                ?? throw new InvalidOperationException(
                                    "The active project was unavailable for sidebar removal verification.");
        Require(mainWindow.RemoveExplorerProject(selectedProjectId),
            "The selected project was not removed through the Explorer action.");
        await mainWindow.FileExplorer.WhenIdleAsync();

        saved = settingsService.Load();
        Require(saved.Explorer.Projects.Count == 1
                && saved.Explorer.Projects.All(project =>
                    !string.Equals(
                        project.Id,
                        selectedProjectId,
                        StringComparison.OrdinalIgnoreCase)),
            "Explorer project removal was not persisted.");
        Require(string.IsNullOrWhiteSpace(saved.Explorer.ActiveProjectId),
            "Removing the active project did not reset the persisted Explorer selection.");
        Require(mainWindow.FileExplorer.SelectedProject is null,
            "Removing the active project did not return the Explorer to Default.");
        Require(mainWindow.FileExplorer.ProjectOptions.Count == 2
                && mainWindow.FileExplorer.ProjectOptions.All(option =>
                    !string.Equals(
                        option.Project?.Id,
                        selectedProjectId,
                        StringComparison.OrdinalIgnoreCase)),
            "The removed project remained in the Explorer project picker.");
        Require(!mainWindow.RemoveExplorerProject(selectedProjectId),
            "Removing a project that no longer exists should be a no-op.");
        Require(File.Exists(firstRootMarker),
            "Removing a saved project deleted a file from its project directory.");

        Console.WriteLine(
            $"EXPLORER_PROJECTS_OK|{saved.Explorer.Projects.Count}|default-option|project-address|back-history|address-commit|quick-create|settings-save|sidebar-remove|files-preserved");
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
