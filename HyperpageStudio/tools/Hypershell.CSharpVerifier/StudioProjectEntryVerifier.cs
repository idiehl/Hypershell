using Hypershell;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

/// <summary>Exercises Studio entry against a copied package and a private profile.</summary>
public static class StudioProjectEntryVerifier
{
    public static async Task VerifyAsync(string verifierRoot, string sourceLibraryRoot, string? mainWindowPath = null)
    {
        var root = Path.Combine(Path.GetFullPath(verifierRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var copiedLibrary = Path.Combine(root, "library-copy");
        CopyProject(Path.GetFullPath(sourceLibraryRoot), copiedLibrary);
        var beforeOpen = Fingerprint(copiedLibrary);
        var checks = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        var settingsService = new SettingsService(Path.Combine(root, "private-profile"));
        var settings = settingsService.Load();
        settings.Application.CreateTerminalOnStartup = false;
        settings.Explorer.StartupDirectory = root;
        settingsService.Save(settings);
        using (var workspace = new MainWindowViewModel(settingsService))
        {
            await workspace.FileExplorer.WhenIdleAsync();
            var library = await workspace.OpenVisualPageProjectAsync(copiedLibrary);
            Check(library is not null, "Full workspace opens the copied HyperLib folder: " + workspace.StatusMessage);
            Check(ReferenceEquals(workspace.ActiveWorkspaceTab, library) && library!.HasHyperlibAuthoring
                && library.ProjectTree.Count == 1, "Opening the library activates a fully initialized Studio tab and project tree.");
            var same = await workspace.OpenVisualPageProjectAsync(Path.Combine(copiedLibrary, VisualPageProjectService.ProjectManifestFileName));
            Check(ReferenceEquals(same, library) && workspace.WorkspaceTabs.OfType<VisualPageStudioTabViewModel>().Count() == 1,
                "Opening the manifest after its folder focuses the existing tab.");
            var firstWork = library!.HyperlibProjects.First(project => !string.Equals(project.RootPath, copiedLibrary, StringComparison.OrdinalIgnoreCase));
            var work = await workspace.OpenVisualPageProjectAsync(firstWork.RootPath);
            Check(work is not null && work.HasHyperlibAuthoring && work.ProjectId != library.ProjectId
                && ReferenceEquals(workspace.ActiveWorkspaceTab, work), "A Work opens in its own initialized Studio tab.");
            Check(Fingerprint(copiedLibrary) == beforeOpen, "Opening library and Work leaves all copied project and content files unchanged.");

            var missing = Path.Combine(root, "folder-without-project");
            Directory.CreateDirectory(missing);
            await File.WriteAllTextAsync(Path.Combine(missing, "keep.txt"), "Keep existing folder content.");
            var previous = workspace.ActiveWorkspaceTab;
            var count = workspace.WorkspaceTabs.Count;
            var failed = await workspace.OpenVisualPageProjectAsync(missing);
            Check(failed is null && workspace.StatusMessage.Contains("visual-page.project.json", StringComparison.OrdinalIgnoreCase),
                "Missing project metadata returns an actionable opening error.");
            Check(ReferenceEquals(workspace.ActiveWorkspaceTab, previous) && workspace.WorkspaceTabs.Count == count,
                "A failed open preserves the active tab and existing sessions.");
            Check(await File.ReadAllTextAsync(Path.Combine(missing, "keep.txt")) == "Keep existing folder content.",
                "A failed open leaves unrelated folder content unchanged.");
            var malformed = Path.Combine(root, "invalid-project");
            Directory.CreateDirectory(malformed);
            var invalidManifest = Path.Combine(malformed, VisualPageProjectService.ProjectManifestFileName);
            await File.WriteAllTextAsync(invalidManifest, "{invalid-json");
            Check(await workspace.OpenVisualPageProjectAsync(malformed) is null
                && workspace.StatusMessage.Contains("could not open", StringComparison.OrdinalIgnoreCase), "Malformed project JSON is reported as a failed open.");
            Check(await File.ReadAllTextAsync(invalidManifest) == "{invalid-json", "Malformed metadata is not replaced while opening.");

            var parent = Path.Combine(root, "new-projects");
            Directory.CreateDirectory(parent);
            await File.WriteAllTextAsync(Path.Combine(parent, "keep.txt"), "Existing parent content.");
            var created = await workspace.CreateVisualPageProjectAsync(parent, "New project", new HyperpageProjectCreationOptions());
            Check(created is not null && created.ProjectRoot == Path.Combine(parent, "New project")
                && ReferenceEquals(workspace.ActiveWorkspaceTab, created), "Create uses the picker parent and opens the new named child project.");
            Check(await File.ReadAllTextAsync(Path.Combine(parent, "keep.txt")) == "Existing parent content.", "Creating a child preserves the selected parent's files.");
            var createdSnapshot = Fingerprint(created!.ProjectRoot);
            count = workspace.WorkspaceTabs.Count;
            Check(await workspace.CreateVisualPageProjectAsync(parent, "New project", new HyperpageProjectCreationOptions()) is null
                && workspace.StatusMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase), "Creation collision reports the existing project instead of overwriting it.");
            Check(workspace.WorkspaceTabs.Count == count && ReferenceEquals(workspace.ActiveWorkspaceTab, created)
                && Fingerprint(created.ProjectRoot) == createdSnapshot, "Failed creation preserves project files, tab count, and selection.");
        }
        Check(Fingerprint(copiedLibrary) == beforeOpen, "Closing the private workspace does not save over the copied library.");
        VerifyCreationDialog(root, mainWindowPath, Check);
        var report = Path.Combine(root, "studio-project-entry.json");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
        {
            Passed = true, Fixture = root, SourceLibrary = Path.GetFullPath(sourceLibraryRoot),
            Scope = "Private profile and copied library; actual workspace entry APIs and detached real creation dialog; no application window or native folder picker.",
            Assertions = checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"STUDIO_PROJECT_ENTRY_OK|assertions:{checks.Count}|full-workspace|copy-preserved|create-parent|failure-preservation|real-dialog|{report}");
    }

    private static void VerifyCreationDialog(string fixture, string? mainWindowPath, Action<bool, string> check)
    {
        Exception? error = null;
        void Run()
        {
            try
            {
                if (Application.Current is null)
                {
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var main = mainWindowPath ?? FindMainWindow();
                    XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                    var app = XDocument.Load(Path.Combine(Path.GetDirectoryName(main)!, "App.xaml")).Root!;
                    var dictionary = new XElement(presentation + "ResourceDictionary",
                        app.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => new XAttribute(attribute)),
                        app.Element(presentation + "Application.Resources")!.Elements().Select(element => new XElement(element)));
                    foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                        if (attribute.Value.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !attribute.Value.Contains(";assembly=", StringComparison.Ordinal)) attribute.Value += ";assembly=Hypershell";
                    foreach (var element in dictionary.DescendantsAndSelf())
                        if (element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Hypershell");
                    dictionary.Descendants().Attributes("Click").Remove();
                    application.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
                }
                if (Application.Current?.Dispatcher.CheckAccess() != true) throw new InvalidOperationException("The verifier requires its own STA WPF application.");
                var templates = new VisualPageTemplateService(new VisualPageComponentCatalog()).BuiltInTemplates;
                var dialog = new HyperpageProjectCreationDialog(fixture, templates);
                try
                {
                    dialog.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    check(dialog.PageDrafts.Count == 1 && ((RadioButton)dialog.FindName("FixedPagesRadio")).IsChecked == true,
                        "The real creation dialog initializes one fixed page without startup exceptions.");
                    check(dialog.ProjectDirectoryPreview == Path.Combine(fixture, "Hyperpage Project"), "The creation dialog previews the named child location.");
                    ((RadioButton)dialog.FindName("DynamicSiteRadio")).IsChecked = true;
                    check(!((TextBox)dialog.FindName("PageCountTextBox")).IsEnabled && dialog.PageDrafts.Count == 1,
                        "Dynamic-site selection keeps one foundation page and disables fixed count.");
                    ((RadioButton)dialog.FindName("FixedPagesRadio")).IsChecked = true;
                    ((TextBox)dialog.FindName("PageCountTextBox")).Text = "3";
                    check(dialog.PageDrafts.Count == 3, "Fixed-page choice creates the requested initial page drafts.");
                    ((RadioButton)dialog.FindName("AiCodingRadio")).IsChecked = true;
                    check(((Border)dialog.FindName("AiProviderPanel")).IsEnabled, "AI choice enables the provider controls.");
                    ((RadioButton)dialog.FindName("TemplatesYesRadio")).IsChecked = true;
                    check(dialog.UseTemplates && dialog.PageDrafts.All(page => page.SelectedTemplate is not null), "Choosing templates initializes every page's template choice.");
                    ((TextBox)dialog.FindName("ProjectNameTextBox")).Text = "..";
                    typeof(HyperpageProjectCreationDialog).GetMethod("CreateClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(dialog, [dialog, new RoutedEventArgs()]);
                    check(!string.IsNullOrWhiteSpace(((TextBlock)dialog.FindName("StatusText")).Text) && dialog.CreationOptions is null,
                        "Invalid project names remain in the dialog with a visible error and no creation options.");
                }
                finally { dialog.Close(); }
            }
            catch (Exception exception) { error = exception; }
        }
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) Run();
        else
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Studio entry dialog verifier" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void CopyProject(string source, string destination)
    {
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Project fixture cannot traverse filesystem links.");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Project fixture cannot copy file links.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            if (Path.GetFileName(directory) is not ("bin" or "obj" or ".git" or ".hyperlib" or "node_modules"))
                CopyProject(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string Fingerprint(string root) => string.Join('\n', Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    private static string FindMainWindow()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "Hypershell", "MainWindow.xaml");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Supply the isolated MainWindow.xaml path.");
    }
}
