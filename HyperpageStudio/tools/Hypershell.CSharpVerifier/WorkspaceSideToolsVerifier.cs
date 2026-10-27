using Hypershell.Extensibility;
using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

/// <summary>Isolated settings migration and detached, real-XAML sidebar rendering.</summary>
public static class WorkspaceSideToolsVerifier
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string LegacyWorkItems = "hypershell.tool.project-work-items";
    private const string LegacyGit = "hypershell.tool.source-control";
    private const string UnknownTool = "example.extension.future-tool";

    public static void Verify(string outputDirectory, string? mainWindowPath = null)
    {
        var source = mainWindowPath ?? FindMainWindow();
        Directory.CreateDirectory(outputDirectory);
        Exception? failure = null;
        void Run()
        {
            try { VerifyOnSta(Path.GetFullPath(outputDirectory), source); }
            catch (Exception exception) { failure = exception; }
        }
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) Run();
        else
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Workspace side tools verifier" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyOnSta(string outputDirectory, string sourcePath)
    {
        var assertions = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            assertions.Add(description);
        }
        VerifyMigration(outputDirectory, Check);
        LoadApplicationResources(Path.Combine(Path.GetDirectoryName(sourcePath)!, "App.xaml"));
        var source = XDocument.Load(sourcePath);
        var cases = new List<object>();
        foreach (var width in new[] { 300d, 418d, 620d })
        foreach (var side in new[] { "FileExplorer", "CommandBuilder" })
        {
            var data = new HeaderSample();
            var header = CreateHeader(source, side + "HeaderGrid", width);
            header.DataContext = data;
            Layout(header);
            var items = (ItemsControl)header.FindName(side + "PageToggles");
            var collapse = (Button)header.FindName(side + "HeaderToggle");
            var buttons = Descendants<RadioButton>(items).ToArray();
            var expected = side == "FileExplorer" ? data.FileExplorerSideTools : data.CommandBuilderSideTools;
            Check(buttons.Length == expected.Count && buttons.Length == 3, $"{side} has three materialized selectors at {width} DIP.");
            var measured = new List<object>();
            foreach (var button in buttons)
            {
                var tool = (WorkspaceSideToolViewModel)button.DataContext;
                var bounds = Bounds(button, header);
                Check(Inside(bounds, header.RenderSize), $"{tool.DisplayName} fits inside the {width}-DIP header.");
                Check(!bounds.IntersectsWith(Bounds(collapse, header)), $"{tool.DisplayName} does not overlap collapse at {width} DIP.");
                Check(button.ActualWidth >= 28 && button.ActualHeight >= 28, $"{tool.DisplayName} retains a usable hit area.");
                Check(Equals(button.ToolTip, tool.DisplayName), $"{tool.DisplayName} has its descriptive tooltip.");
                Check(AutomationProperties.GetName(button) == tool.DisplayName, $"{tool.DisplayName} has a readable automation name.");
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Layout(header);
                Check(expected.Count(item => item.IsActive) == 1 && tool.IsActive && button.IsChecked == true,
                    $"{tool.DisplayName} click selects exactly one tool and updates its checked state.");
                if (tool.Kind is WorkspaceSideToolKind.ProjectWorkItems or WorkspaceSideToolKind.AtlasMyNet or WorkspaceSideToolKind.NetworkObservatory or WorkspaceSideToolKind.WorkspaceAi)
                {
                    var glyphName = tool.Kind == WorkspaceSideToolKind.WorkspaceAi ? "AiShellGlyph" : "WorkspaceSideToolGlyph";
                    var glyph = Descendants<FrameworkElement>(button).Single(element => element.GetType().Name == glyphName);
                    Check(glyph.Visibility == Visibility.Visible && glyph.ActualWidth >= 18, $"{tool.DisplayName} renders its native vector glyph.");
                    Check(TextElement.GetForeground(glyph).ToString() == button.Foreground.ToString(), $"{tool.DisplayName} vector glyph inherits selected foreground.");
                    Check(Inside(Bounds(glyph, button), button.RenderSize), $"{tool.DisplayName} vector glyph stays inside its hit area.");
                }
                measured.Add(new { tool.Id, bounds.X, bounds.Y, bounds.Width, bounds.Height });
            }
            Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(collapse)), $"{side} collapse has a readable automation name.");
            foreach (var scale in new[] { 1d, 1.5d, 2d })
                SaveBitmap(header, Path.Combine(outputDirectory, $"{side.ToLowerInvariant()}-{width:0}-{scale * 100:0}.png"), scale);
            cases.Add(new { Side = side, Width = width, Selectors = measured });
        }
        VerifyProjects(outputDirectory, Check);
        var receipt = Path.Combine(outputDirectory, "workspace-side-tools.json");
        File.WriteAllText(receipt, JsonSerializer.Serialize(new
        {
            Source = sourcePath,
            Scope = "Real detached header XAML and native vector controls; private settings fixtures; no application window or live profile.",
            ScaleCoverage = "96/144/192-DPI bitmap rendering, without changing the Windows display DPI.",
            Assertions = assertions, Cases = cases
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"WORKSPACE_SIDE_TOOLS_OK|assertions:{assertions.Count}|migration|settings-roundtrip|selectors|projects-repositories|narrow-wide|{receipt}");
    }

    private static void VerifyMigration(string outputDirectory, Action<bool, string> check)
    {
        var registry = WorkspacePanelRegistry.CreateWithBuiltIns();
        var definitions = WorkspaceSideToolRegistry.Definitions;
        var projects = definitions.Single(definition => definition.DisplayName == "Projects");
        check(definitions.Count == 6 && definitions.Select(definition => definition.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 6,
            "Consolidation exposes six unique tools.");
        check(definitions.All(definition => definition.Id != LegacyWorkItems && definition.Id != LegacyGit),
            "Legacy Work Items and Git are absent from top-level tools.");
        var defaults = WorkspaceLayoutService.CreateDefault(registry);
        check(defaults.Panels.Any(panel => panel.PanelId == WorkspacePanelRegistry.FileExplorerPanelId)
            && defaults.Panels.Any(panel => panel.PanelId == WorkspacePanelRegistry.CommandBuilderPanelId), "Persisted built-in host IDs remain stable.");
        var old = new WorkspaceLayoutSnapshot
        {
            IsUserUnlocked = true,
            Panels = [new() { PanelId = WorkspacePanelRegistry.FileExplorerPanelId, Slot = WorkspacePanelSlot.Right, Width = 612, Height = 500, Order = 8, IsVisible = false }],
            SideTools = [new() { ToolId = LegacyWorkItems, Order = 9, IsVisible = false },
                new() { ToolId = LegacyGit, Order = 4, IsVisible = true },
                new() { ToolId = UnknownTool, Order = 23, IsVisible = false }]
        };
        var migrated = WorkspaceLayoutService.Normalize(old, registry);
        var projectPlacement = WorkspaceLayoutService.GetRequiredSideTool(migrated, projects.Id);
        check(projectPlacement.IsVisible && projectPlacement.Order == 4, "Legacy combined Projects preserves visible Git and earlier order.");
        check(WorkspaceSideToolRegistry.GetRequired(LegacyGit).Id == projects.Id
            && WorkspaceLayoutService.GetRequiredSideTool(migrated, LegacyWorkItems).ToolId == projects.Id,
            "Legacy tool callers resolve to the consolidated Projects tool.");
        check(migrated.SideTools.All(tool => tool.ToolId != LegacyWorkItems && tool.ToolId != LegacyGit), "Retired tool IDs are not duplicated as dormant entries.");
        check(old.SideTools.Count == 3 && old.SideTools.All(tool => tool.ToolId != projects.Id), "Migration does not mutate the caller's legacy snapshot.");
        var panel = WorkspaceLayoutService.GetRequiredPanel(migrated, WorkspacePanelRegistry.FileExplorerPanelId);
        check(panel.Slot == WorkspacePanelSlot.Right && panel.Width == 612 && !panel.IsVisible && migrated.IsUserUnlocked,
            "Sidebar move, width, hidden state, and unlock preference survive migration.");
        var unknown = WorkspaceLayoutService.GetRequiredSideTool(migrated, UnknownTool);
        check(unknown.Order == 23 && !unknown.IsVisible, "Unknown extension tool settings remain dormant and intact.");
        check(Json(migrated) == Json(WorkspaceLayoutService.Normalize(migrated, registry)), "Repeated migration is idempotent.");
        var explicitProjects = WorkspaceLayoutService.Normalize(new()
        {
            SideTools = [new() { ToolId = projects.Id.ToUpperInvariant(), Order = 18, IsVisible = false },
                new() { ToolId = LegacyGit, Order = 1, IsVisible = true }]
        }, registry);
        projectPlacement = WorkspaceLayoutService.GetRequiredSideTool(explicitProjects, projects.Id);
        check(!projectPlacement.IsVisible && projectPlacement.Order == 18, "Explicit new Projects placement takes precedence over legacy settings, case-insensitively.");
        var hidden = WorkspaceLayoutService.Normalize(new()
        {
            SideTools = definitions.Select(definition => new WorkspaceSideToolPlacementSnapshot { ToolId = definition.Id, IsVisible = false }).ToArray()
        }, registry);
        foreach (var surface in new[] { BuiltInWorkspaceSurface.FileExplorer, BuiltInWorkspaceSurface.CommandBuilder })
            check(definitions.Where(definition => definition.HostSurface == surface).Count(definition => WorkspaceLayoutService.GetRequiredSideTool(hidden, definition.Id).IsVisible) == 1,
                $"All-hidden {surface} settings recover exactly one selectable tool.");
        var settings = new SettingsService(Path.Combine(outputDirectory, "settings-fixture", Guid.NewGuid().ToString("N")));
        var persisted = settings.Load();
        persisted.Application.CreateTerminalOnStartup = false;
        persisted.WorkspaceLayout = old;
        persisted.Explorer.Projects.Add(new ProjectDefinition { Id = "saved-project", Name = "Preserved project", Directories = [outputDirectory] });
        persisted.Explorer.ActiveProjectId = "saved-project";
        settings.Save(persisted);
        var loaded = settings.Load();
        check(Json(loaded.WorkspaceLayout) == Json(migrated), "Loading real legacy settings applies the same layout migration.");
        check(loaded.Explorer.Projects.Single().Name == "Preserved project" && loaded.Explorer.ActiveProjectId == "saved-project",
            "Legacy project identity and active project survive settings loading.");
        settings.Save(loaded);
        check(Json(settings.Load().WorkspaceLayout) == Json(migrated), "Saving and reloading the migrated profile remains stable.");
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    private static void VerifyProjects(string outputDirectory, Action<bool, string> check)
    {
        foreach (var width in new[] { 300d, 620d })
        {
            var data = new ProjectsSample(outputDirectory);
            var view = new Hypershell.Controls.ProjectsView { DataContext = data };
            var surface = new Grid { Width = width, Height = 640, Background = (Brush)Application.Current.FindResource("PanelBrush"), UseLayoutRounding = true };
            surface.Children.Add(view);
            var projectTab = (RadioButton)view.FindName("ProjectsWorkItemsTab");
            var repositoryTab = (RadioButton)view.FindName("ProjectsRepositoriesTab");
            var workItems = (FrameworkElement)view.FindName("ProjectWorkItemsContent");
            var repositories = (FrameworkElement)view.FindName("ProjectRepositoriesContent");
            var devices = (ComboBox)view.FindName("ProjectsRepositoryDevicePicker");
            var projects = (ComboBox)view.FindName("ProjectsRepositoryProjectPicker");
            foreach (var repositorySelected in new[] { false, true })
            {
                var selected = repositorySelected ? repositoryTab : projectTab;
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(selected, null);
                Layout(surface);
                check(projectTab.IsChecked == !repositorySelected && repositoryTab.IsChecked == repositorySelected,
                    $"Projects tabs select exactly one subview at {width} DIP ({repositorySelected}).");
                check(workItems.Visibility == (repositorySelected ? Visibility.Collapsed : Visibility.Visible),
                    $"Projects work items visibility follows subview selection at {width} DIP ({repositorySelected}).");
                check(Inside(Bounds(projectTab, surface), surface.RenderSize) && Inside(Bounds(repositoryTab, surface), surface.RenderSize),
                    $"Both Projects tabs remain inside the {width}-DIP sidebar.");
                if (repositorySelected)
                {
                    check(ReferenceEquals(repositories.DataContext, data.SourceControl), "Repositories retain the shared source-control view model.");
                    check(ReferenceEquals(devices.ItemsSource, data.FileExplorer.Devices) && ReferenceEquals(projects.ItemsSource, data.FileExplorer.ProjectOptions),
                        "Repository selectors use the shared explorer collections.");
                    check(Inside(Bounds(devices, surface), surface.RenderSize) && Inside(Bounds(projects, surface), surface.RenderSize)
                        && Inside(Bounds(repositories, surface), surface.RenderSize), $"Repository pickers and content fit the {width}-DIP sidebar.");
                    projects.SelectedItem = data.FileExplorer.ProjectOptions[1];
                    devices.SelectedItem = data.FileExplorer.Devices[1];
                    check(ReferenceEquals(data.FileExplorer.SelectedProjectOption, projects.SelectedItem) && ReferenceEquals(data.FileExplorer.SelectedDevice, devices.SelectedItem),
                        "Repository selector changes flow back to shared explorer selections.");
                    data.FileExplorer.IsBusy = true;
                    Layout(surface);
                    check(!projects.IsEnabled && !devices.IsEnabled, "Repository selectors are disabled while explorer navigation is busy.");
                    data.FileExplorer.IsBusy = false;
                    Layout(surface);
                    check(projects.IsEnabled && devices.IsEnabled, "Repository selectors re-enable after explorer navigation.");
                }
                foreach (var scale in new[] { 1d, 1.5d, 2d })
                    SaveBitmap(surface, Path.Combine(outputDirectory, $"projects-{(repositorySelected ? "repositories" : "work-items")}-{width:0}-{scale * 100:0}.png"), scale);
            }
        }
    }

    private static void LoadApplicationResources(string path)
    {
        if (Application.Current is not null)
        {
            if (!Application.Current.Dispatcher.CheckAccess()) throw new InvalidOperationException("Existing WPF application belongs to a different thread.");
            return;
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var source = XDocument.Load(path).Root!;
        var dictionary = new XElement(Presentation + "ResourceDictionary");
        CopyNamespaces(source, dictionary);
        dictionary.Add(source.Element(Presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
        Qualify(dictionary);
        dictionary.Descendants().Attributes("Click").Remove();
        application.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static Grid CreateHeader(XDocument source, string name, double width)
    {
        var markup = new XElement(source.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name));
        var wrapper = new XElement(Presentation + "Grid");
        CopyNamespaces(source.Root!, wrapper);
        wrapper.Add(new XElement(Presentation + "Grid.Resources", new XElement(source.Descendants(Presentation + "DataTemplate")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "WorkspaceSideToolIconTemplate"))));
        wrapper.Add(markup);
        Qualify(wrapper);
        var header = (Grid)XamlReader.Parse(wrapper.ToString());
        header.Width = width;
        header.Height = 46;
        header.Background = (Brush)Application.Current.FindResource("PanelBrush");
        header.UseLayoutRounding = true;
        header.SnapsToDevicePixels = true;
        TextElement.SetFontFamily(header, (FontFamily)Application.Current.FindResource("UiFontFamily"));
        return header;
    }

    private static void CopyNamespaces(XElement source, XElement target)
    {
        foreach (var attribute in source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            target.Add(new XAttribute(attribute));
    }

    private static void Qualify(XElement root)
    {
        foreach (var declaration in root.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            if (declaration.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !declaration.Value.Contains(";assembly=", StringComparison.Ordinal))
                declaration.Value += ";assembly=Hypershell";
        foreach (var element in root.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
    }

    private static void Layout(FrameworkElement surface)
    {
        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
        surface.UpdateLayout();
        surface.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        surface.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) => element.TransformToVisual(ancestor).TransformBounds(new Rect(element.RenderSize));
    private static bool Inside(Rect rectangle, Size size) => rectangle.Left >= -0.5 && rectangle.Top >= -0.5 && rectangle.Right <= size.Width + 0.5 && rectangle.Bottom <= size.Height + 0.5;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void SaveBitmap(FrameworkElement surface, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale), (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string FindMainWindow()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Hypershell", "MainWindow.xaml");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Supply the isolated MainWindow.xaml path.");
    }

    public sealed class HeaderSample
    {
        public ObservableCollection<WorkspaceSideToolViewModel> FileExplorerSideTools { get; } = [];
        public ObservableCollection<WorkspaceSideToolViewModel> CommandBuilderSideTools { get; } = [];
        public RelayCommand ToggleFileExplorerCommand { get; } = new(() => { });
        public RelayCommand ToggleCommandBuilderCommand { get; } = new(() => { });
        public HeaderSample()
        {
            foreach (var definition in WorkspaceSideToolRegistry.Definitions.OrderBy(definition => definition.DefaultOrder))
            {
                var collection = definition.HostSurface == BuiltInWorkspaceSurface.FileExplorer ? FileExplorerSideTools : CommandBuilderSideTools;
                collection.Add(new WorkspaceSideToolViewModel(definition, id =>
                {
                    foreach (var item in collection) item.IsActive = item.Id == id;
                }));
            }
            FileExplorerSideTools[0].IsActive = true;
            CommandBuilderSideTools[0].IsActive = true;
        }
    }

    public sealed class ProjectsSample : ObservableObject
    {
        private bool _repositories;
        public bool IsProjectsWorkItemsViewActive => !_repositories;
        public bool IsProjectsRepositoriesViewActive => _repositories;
        public ProjectWorkItemsViewModel WorkItems { get; } = new(new ProjectWorkItemService());
        public SourceControlViewModel SourceControl { get; } = new(new GitRepositoryService(new LocalGitCommandRunner()));
        public ExplorerSample FileExplorer { get; } = new();
        public RelayCommand ShowProjectsWorkItemsCommand { get; }
        public RelayCommand ShowProjectsRepositoriesCommand { get; }
        public ProjectsSample(string fixtureRoot)
        {
            ShowProjectsWorkItemsCommand = new(() => Select(false));
            ShowProjectsRepositoriesCommand = new(() => Select(true));
            WorkItems.UpdateProjectScopes([new ProjectDefinition { Id = "fixture-project", Name = "Long example project with saved work items", Directories = [fixtureRoot] }], "fixture-project", fixtureRoot);
        }
        private void Select(bool repositories)
        {
            _repositories = repositories;
            OnPropertyChanged(nameof(IsProjectsWorkItemsViewActive));
            OnPropertyChanged(nameof(IsProjectsRepositoriesViewActive));
        }
    }

    public sealed class ExplorerSample : ObservableObject
    {
        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
        public ObservableCollection<Choice> Devices { get; } = [new("This PC"), new("Linked development workstation")];
        public ObservableCollection<Choice> ProjectOptions { get; } = [new("Default"), new("Long project title that must remain contained")];
        public Choice SelectedDevice { get; set; }
        public Choice SelectedProjectOption { get; set; }
        public ExplorerSample() { SelectedDevice = Devices[0]; SelectedProjectOption = ProjectOptions[0]; }
    }

    public sealed record Choice(string DisplayName);
}
