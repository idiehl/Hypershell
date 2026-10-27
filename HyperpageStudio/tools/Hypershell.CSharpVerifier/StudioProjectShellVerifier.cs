using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows.Threading;

public static class StudioProjectShellVerifier
{
    public static Task VerifyAsync(string outputDirectory, string repositoryRoot)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await VerifyOnStaAsync(outputDirectory, repositoryRoot); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    private static async Task VerifyOnStaAsync(string outputDirectory, string repositoryRoot)
    {
        var root = Path.Combine(Path.GetFullPath(outputDirectory), "project-shell", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); checks.Add(message); }
        var settings = new SettingsService(Path.Combine(root, "profile"));
        var preferences = settings.Load(); preferences.Application.CreateTerminalOnStartup = false;
        preferences.Explorer.StartupDirectory = root; settings.Save(preferences);
        using (var shell = new MainWindowViewModel(settings, StandaloneStudioMode.HyperpageStudio))
        {
            await shell.FileExplorer.WhenIdleAsync();
            Check(shell.WorkspaceTabs.Count == 1 && shell.ActiveWorkspaceTab is StudioProjectStartTabViewModel,
                "Studio begins with one project start tab before initialization.");
            await shell.InitializeAsync();
            Check(shell.ActiveWorkspaceTab is StudioProjectStartTabViewModel && shell.StudioWorkspaceTabs.Cast<object>().Count() == 1,
                "Initialization keeps the start surface active without opening the live Hyperlib library.");
            var start = (StudioProjectStartTabViewModel)shell.ActiveWorkspaceTab!;
            Check(start.ParentDirectory == VisualPageProjectService.DefaultProjectsDirectory,
                "The initial Studio project tab uses the shared installed Projects directory.");
            Check(start.StudioProjectKindLabel.Length == 0 && start.StudioDisplayTitle.Length > 0 && !start.HasUnsavedChanges,
                "The project start tab displays its name without a redundant type badge or fictitious document draft.");
            var second = shell.CreateStudioProjectStartTab();
            Check(second.ParentDirectory == VisualPageProjectService.DefaultProjectsDirectory,
                "Every upper + project tab starts at the installed Projects directory.");
            Check(shell.WorkspaceTabs.Count == 2 && ReferenceEquals(shell.ActiveWorkspaceTab, second), "Upper + creates and selects a fresh project start tab.");
            shell.CloseWorkspaceTab(second); shell.CloseWorkspaceTab(start);
            Check(shell.WorkspaceTabs.Count == 1 && shell.ActiveWorkspaceTab is StudioProjectStartTabViewModel,
                "Closing the final project brings back a usable start tab.");
            var path = await new StudioProjectScaffoldService().CreateAsync(new StudioProjectCreationRequest
            { Kind = StudioProjectKind.Html, Name = "Shell integration", ParentDirectory = root });
            var project = await shell.OpenVisualPageProjectAsync(path) ?? throw new InvalidOperationException(shell.StatusMessage);
            Check(project.StudioDisplayTitle == "Shell integration" && project.StudioProjectKindLabel == "HTML", "An upper tab identifies the whole project and its type.");
            var nextPage = project.Project.Pages.Last();
            var same = await shell.OpenVisualPageProjectAsync(Path.Combine(path, VisualPageProjectService.ProjectManifestFileName), nextPage.PageId);
            Check(ReferenceEquals(project, same) && shell.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 1
                && project.PageId == nextPage.PageId, "Opening another page selects it inside the same project instead of adding an upper tab.");
            await project.OpenProjectNodeAsync(new HyperpageProjectTreeNodeViewModel
            { Name = "README.md", RelativePath = "README.md", IsDirectory = false });
            var readme = project.OpenProjectDocuments.Single(item => item.RelativePath == "README.md");
            Check(readme.Content.Contains("Quick start") && shell.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 1,
                "Opening README opens a lower file tab inside its project.");
            var guide = new StudioProjectFileHelpService();
            var readmeHelp = guide.GetHelp(path, "README.md");
            var cssHelp = guide.GetHelp(path, VisualPageProjectService.UserCssPath(project.Project, project.Document));
            Check(readmeHelp is not null && cssHelp is not null && readmeHelp.Purpose != cssHelp.Purpose
                && cssHelp.EditGuidance.Length > 30 && guide.GetHelp(path, "../outside.md") is null,
                "File help provides distinct guidance and rejects paths outside the project.");
            var before = shell.WorkspaceTabs.Count;
            Check(await shell.OpenVisualPageProjectAsync(Path.Combine(root, "missing")) is null && shell.WorkspaceTabs.Count == before,
                "A failed load keeps existing projects and the start screen intact.");
            var workReceipt = Path.Combine(outputDirectory, "hyperlib-work-project-result.txt");
            if (File.Exists(workReceipt))
            {
                var workPath = File.ReadAllLines(workReceipt).Last(line => !string.IsNullOrWhiteSpace(line));
                var outerRoot = Path.GetFullPath(Path.Combine(workPath, "../.."));
                var work = await shell.OpenVisualPageProjectAsync(outerRoot) ?? throw new InvalidOperationException(shell.StatusMessage);
                var sameWork = await shell.OpenVisualPageProjectAsync(workPath);
                Check(ReferenceEquals(work, sameWork) && work.HasHyperlibAuthoring && !work.IsHyperlibEditor
                    && work.ProjectRoot == Path.GetFullPath(workPath),
                    "Loading a generated Work's outer folder and nested manifest selects one unified project with its real content workspace.");
                Check(work.HyperlibWorkspace!.Pages.Count >= 3 && work.HyperlibWorkspace.Works.Count == 1,
                    "The generated Work opens with its landing and content Pages available without creating a separate Editor app.");
            }
        }
        Check(StandaloneStudioHost.ParseMode(["--hyperlib-editor"]) == StandaloneStudioMode.HyperpageStudio
            && StandaloneStudioHost.GetAppUserModelId(StandaloneStudioMode.HyperlibEditor) == StandaloneStudioHost.GetAppUserModelId(StandaloneStudioMode.HyperpageStudio),
            "The old Editor launch flag and taskbar identity alias the unified Studio.");
        var window = XDocument.Load(Path.Combine(repositoryRoot, "src/Hypershell/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var plus = window.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "NewStudioTabButton");
        Check((string?)plus.Attribute("Click") == "NewStudioProjectClick" && !plus.Descendants().Any(element => element.Name.LocalName == "ContextMenu"),
            "Upper + opens the project start screen directly and has no obsolete object creation menu.");
        var view = XDocument.Load(Path.Combine(repositoryRoot, "src/Hypershell/VisualPageStudioView.xaml"));
        Check(!view.Descendants().Any(element => (string?)element.Attribute(x + "Name") == "HyperlibToolTab"),
            "The separate left-side Hyperlib tab is gone.");
        var viewCode = File.ReadAllText(Path.Combine(repositoryRoot, "src/Hypershell/VisualPageStudioView.xaml.cs"));
        Check(viewCode.Contains("Explain ") && viewCode.Contains("ShowPaneFileHelp(contentId)") && viewCode.Contains("Open project file…"),
            "Lower tabs expose file-specific help and an existing-project-file action.");
        await File.WriteAllTextAsync(Path.Combine(root, "project-shell.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"STUDIO_PROJECT_SHELL_OK|checks:{checks.Count}|{root}");
    }
}
